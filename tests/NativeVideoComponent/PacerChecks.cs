using System.Diagnostics;
using ZenithClipper.Native.Video;

static class PacerChecks
{
    public static int Run()
    {
        string? previous = Environment.GetEnvironmentVariable("ZENITH_NATIVE_PACER");
        try
        {
            // Interleave modes to reduce warmup/background drift. Synthetic test owns no display/GPU resources.
            foreach (string mode in new[] { "legacy", "deadline", "deadline", "legacy", "legacy", "deadline" })
            {
                Environment.SetEnvironmentVariable("ZENITH_NATIVE_PACER", mode);
                using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; var wall = Stopwatch.StartNew();
                long packets = 0; using var finished = new ManualResetEventSlim();
                using var recorder = new NativeVideoRecorder(new NativeVideoConfiguration { FrameRate = 165 },
                    (ReadOnlySpan<byte> _, long index, bool _) => { if (index != packets++) throw new Exception("Synthetic pacing lost/duplicated an index."); },
                    (2560, 1600, 165), 5, TimeSpan.FromSeconds(10));
                recorder.WorkerFinishing += () => finished.Set(); recorder.Start();
                if (!finished.Wait(TimeSpan.FromSeconds(8))) throw new Exception("Synthetic pacing exceeded its fixed deadline."); recorder.Stop();
                wall.Stop(); process.Refresh(); var stats = recorder.Statistics;
                if (recorder.Fault != null || packets != 825 || stats.Elapsed.TotalSeconds is < 4.98 or > 5.1) throw new Exception("Synthetic pacing did not preserve frame count or deadlines: " + recorder.Fault);
                Console.WriteLine($"mode={mode} frames={packets} mediaSeconds={stats.Elapsed.TotalSeconds:F6} wallSeconds={wall.Elapsed.TotalSeconds:F6} cpuMs={(process.TotalProcessorTime-cpu).TotalMilliseconds:F3} misses={stats.DeadlineMisses}");
            }
            return 0;
        }
        finally { Environment.SetEnvironmentVariable("ZENITH_NATIVE_PACER", previous); }
    }
}
