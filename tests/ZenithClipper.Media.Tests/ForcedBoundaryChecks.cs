using System.Reflection;
using ZenithClipper.Media;

static class ForcedBoundaryChecks
{
    public static void Run(string directory)
    {
        foreach (int delay in new[] { 0, 1600 })
        {
            byte[] key = [0,0,0,1,0x67,66,0,30,0x80,0,0,0,1,0x68,0x80,0,0,0,1,0x65,0x80];
            byte[] delta = [0,0,0,1,0x41,0x80];
            using var writer = new FragmentedReplayWriter(directory, new(160,90), new(EncoderDelaySamples:delay));
            var checkpoint = typeof(FragmentedReplayWriter).GetProperty("CompletedThroughVideoFrame", BindingFlags.Public | BindingFlags.Instance);
            long Closed() => checkpoint == null ? writer.Metrics.CompletedFragments == 0 ? 0 : 330 : (long)checkpoint.GetValue(writer)!;
            if (Closed() != 0) throw new Exception("Empty writer checkpoint must be zero.");
            for (int frame = 0; frame < 330; frame++) writer.AddVideo(frame == 0 ? key : delta, frame, frame == 0);
            long codedEnd = 0;
            while (codedEnd - delay < 96000 + 1024) { writer.AddAudio([1,2,3,4],codedEnd); codedEnd += 1024; }
            writer.AddVideo(key,330,true);
            if (Closed() != 330) throw new Exception("Closed checkpoint must exclude the current open frame.");
            int coveredThrough = (int)((codedEnd - delay) * 165 / 48000);
            for (int frame = 331; frame <= coveredThrough; frame++)
            {
                writer.AddVideo(key,frame,true);
                if (writer.Metrics.CompletedFragments != frame - 329) throw new Exception($"Live one-frame GOP stalled despite preceding committed AAC coverage; delay={delay} frame={frame}");
                if (Closed() != frame) throw new Exception("Forced boundary checkpoint omitted a previously encoded video frame.");
            }
            writer.AddVideo(key,coveredThrough + 1,true);
            if (Closed() != coveredThrough) throw new Exception("Uncovered tiny GOP was committed without AAC coverage.");
            if (checkpoint == null) throw new Exception("CompletedThroughVideoFrame checkpoint is missing.");
        }
        Console.WriteLine("PASS live forced one-frame GOP, carried preceding AAC coverage, uncovered rejection and frame checkpoint; delays=0,1600");
    }
}

