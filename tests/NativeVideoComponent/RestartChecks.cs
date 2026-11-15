using System.Diagnostics;
using ZenithClipper.Native.Video;

static class RestartChecks
{
    public static int Run(string[] args)
    {
        int Number(string flag, int fallback) { int index = Array.IndexOf(args, flag); return index < 0 ? fallback : int.Parse(args[index + 1]); }
        int cycles = Number("--cycles", 5), seconds = Number("--seconds", 3);
        if (cycles is < 1 or > 50 || seconds is < 2 or > 30) throw new ArgumentOutOfRangeException(nameof(args));
        bool inject = args.Contains("--callback-fault");
        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            using var measured = Process.GetCurrentProcess(); var cpuStart = measured.TotalProcessorTime; var wall = Stopwatch.StartNew();
            string path = Path.Combine(AppContext.BaseDirectory, $"restart-{(inject ? "fault-" : "")}{cycle:D2}.h264");
            using var output = File.Create(path); long packets = 0, keys = 0; int packetThread = 0, finishingThread = 0, finishing = 0;
            using (var recorder = new NativeVideoRecorder(new NativeVideoConfiguration { UseNv12Input = true, Quantizer = 20 },
                (ReadOnlySpan<byte> packet, long index, bool key) =>
                {
                    if (index != packets) throw new InvalidOperationException("Packet frame index discontinuity.");
                    packetThread = Environment.CurrentManagedThreadId;
                    if (inject && index == 10) throw new InvalidOperationException("Injected restart packet callback fault.");
                    output.Write(packet); packets++; if (key) keys++;
                }))
            {
                recorder.WorkerFinishing += () => { finishing++; finishingThread = Environment.CurrentManagedThreadId; };
                recorder.Start();
                if (inject) { if (!SpinWait.SpinUntil(() => recorder.Fault != null, 5000)) throw new Exception("Injected capture fault was hidden."); }
                else Thread.Sleep(seconds * 1000);
                recorder.Stop(); output.Flush();
                if (finishing != 1 || finishingThread != packetThread) throw new Exception("Worker finishing count or OS worker ownership changed.");
                if (inject ? recorder.Fault?.Message != "Injected restart packet callback fault." : recorder.Fault != null || packets < recorder.FrameRate * (seconds - 1) || keys < 2)
                    throw new Exception("Restart capture/fault verification failed: " + recorder.Fault);
                var stats = recorder.Statistics;
                Console.WriteLine($"cycle={cycle} dimensions={recorder.Width}x{recorder.Height} fps={recorder.FrameRate} packets={packets} keys={keys} repeated={stats.RepeatedFrames} missed={stats.DeadlineMisses} elapsed={stats.Elapsed.TotalSeconds:F3} file={path}");
            }
            using var process = Process.GetCurrentProcess();
            Console.WriteLine($"cycle={cycle} stage=disposed ws={process.WorkingSet64} private={process.PrivateMemorySize64} peakWs={process.PeakWorkingSet64} handles={process.HandleCount} managed={GC.GetTotalMemory(false)} cpuMs={(process.TotalProcessorTime-cpuStart).TotalMilliseconds:F3} wallSeconds={wall.Elapsed.TotalSeconds:F6}");
        }
        Console.WriteLine("PASS same-process native capture restart/teardown without forced GC");
        return 0;
    }
}
