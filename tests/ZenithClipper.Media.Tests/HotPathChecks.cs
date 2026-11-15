using System.Diagnostics;
using ZenithClipper.Media;

static class HotPathChecks
{
    public static void Run(string directory)
    {
        byte[] key = [0,0,0,1,0x67,66,0,30,0x80,0,0,0,1,0x68,0x80,0,0,0,1,0x65,0x80];
        byte[] delta = [0,0,0,1,0x41,0x80];
        byte[] aac = [1,2,3,4];
        using var writer = new FragmentedReplayWriter(directory, new(160,90), retentionSeconds: 86400, maximumSamplesPerFragment: 32768);
        int audio = 0;
        void Add(int frame)
        {
            writer.AddVideo(frame is 0 or 330 ? key : delta, frame, frame is 0 or 330);
            while (audio * 1024.0 / 48000 < (frame + 1.0) / 165)
                writer.AddAudio(aac, audio++ * 1024L);
        }
        for (int i = 0; i < 400; i++) Add(i);
        _ = writer.Metrics;
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        int firstAudio = audio;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        const int count = 12000;
        for (int i = 400; i < 400 + count; i++) { Add(i); _ = writer.Metrics; }
        timer.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Console.WriteLine($"HOTPATH video={count} audio={audio-firstAudio} elapsedMs={timer.Elapsed.TotalMilliseconds:F2} cpuMs={(process.TotalProcessorTime-cpu).TotalMilliseconds:F2} allocatedBytes={bytes}");
        // Deny further readers while permitting the already-open index writer.
        string index = Directory.GetFiles(writer.SpoolDirectory, "index-*.bin").Single();
        using (var denyNewReaders = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.Write))
        {
            Add(12400);
            if (writer.Metrics.CompletedFragments != 1) throw new Exception("Expected unchanged oldest fragment.");
        }
        if (bytes > 2_000_000) throw new Exception($"Steady ingest allocated {bytes} bytes; repeated index-reader allocation regressed.");
        writer.Finish();
        var before = writer.Metrics;
        writer.SetRetention(0.01, 8L * 1024 * 1024 * 1024);
        if (writer.Metrics.CompletedFragments >= before.CompletedFragments) throw new Exception("Retention did not advance oldest record.");
        if (Math.Abs(writer.Metrics.DurationSeconds - (12401 - 330) / 165.0) > 0.000001) throw new Exception("Duration used stale oldest record after pruning.");
        Console.WriteLine("PASS unchanged oldest needs no index-reader opens; ingest allocation bound; prune cache invalidation");
    }
}


