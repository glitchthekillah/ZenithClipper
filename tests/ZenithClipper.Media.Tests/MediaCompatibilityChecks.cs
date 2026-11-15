using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ZenithClipper.Editing;
using ZenithClipper.Media;
using ZenithClipper.Mlt;

static class MediaCompatibilityChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task RunAll(string root, string output, string ffmpeg, string ffprobe,
        byte[] video, IReadOnlyList<(int Start, int Length, bool Key)> frames,
        byte[] audio, IReadOnlyList<(int Start, int Length)> packets, Func<string, string[], string> run)
    {
        if (Environment.GetEnvironmentVariable("ZENITH_MEDIA_TEST_FDK_ONLY") != "1")
        using (var longWriter = new FragmentedReplayWriter(output, new(160, 90), retentionSeconds: 180))
        {
            int nextAudio = 0;
            for (int frame = 0; frame < 120 * 165; frame++)
            {
                var source = frames[frame % 660];
                longWriter.AddVideo(video.AsSpan(source.Start, source.Length), frame, frame % 330 == 0);
                while (nextAudio * 1024.0 / 48000 < (frame + 1.0) / 165)
                {
                    var packet = packets[nextAudio % (packets.Count - 2)];
                    longWriter.AddAudio(audio.AsSpan(packet.Start, packet.Length), nextAudio * 1024L); nextAudio++;
                }
            }
            longWriter.Finish();
            var clip = longWriter.ExportNewest(Path.Combine(output, "long-indexed-120s.mp4"), 120);
            VerifyIndexAndHeaders(clip.Path, 60, 19800, 165, 5760000, 0);
            var timer = Stopwatch.StartNew();
            using var probe = JsonDocument.Parse(run(ffprobe, ["-v", "error", "-show_format", "-show_streams", "-of", "json", clip.Path]));
            Require(Math.Abs(Number(probe.RootElement.GetProperty("format").GetProperty("duration")) - 120) < 0.001, "Long-clip container duration is wrong");
            Require(timer.Elapsed < TimeSpan.FromSeconds(10), "Long-clip duration probe scanned too slowly");
            double probeMilliseconds = timer.Elapsed.TotalMilliseconds;
            foreach (double seconds in new[] { 60.0, 119.0 })
            {
                string result = run(ffmpeg, ["-hide_banner", "-v", "error", "-xerror", "-err_detect", "explode", "-threads", "1", "-ss", seconds.ToString(CultureInfo.InvariantCulture), "-i", clip.Path, "-t", "0.5", "-threads", "1", "-progress", "pipe:1", "-f", "null", "-"]);
                Require(result.Split('\n').Any(line => line.StartsWith("frame=", StringComparison.Ordinal) && int.TryParse(line[6..].Trim(), out int count) && count >= 80), "Middle/end seek did not decode the expected frames");
            }
            await VerifyEditorSeek(clip.Path, 120, [0, 9900, 19797, 19798, 19799]);
            Console.WriteLine($"PASS 120s indexed export: accurate headers, 60 tfra entries, duration probe={probeMilliseconds:F1}ms, strict middle/end seeks, native MLT import/seeks");
        }

        string fixture = Path.Combine(root, "tests", "NativeAac.Fdk", "fixtures", "six-seconds");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "manifest.json")));
        byte[] fdk = File.ReadAllBytes(Path.Combine(fixture, "raw-aac.bin"));
        byte[] sourcePcm = File.ReadAllBytes(Path.Combine(fixture, "source.pcm"));
        var fdkPackets = manifest.RootElement.GetProperty("Packets").EnumerateArray().Select(item => (
            Offset: item.GetProperty("Offset").GetInt32(), Length: item.GetProperty("Length").GetInt32(),
            Index: item.GetProperty("SampleIndex").GetInt64())).ToArray();
        Require(manifest.RootElement.GetProperty("EncoderDelaySamples").GetInt32() == 1600, "FDK fixture delay changed");
        // Close a natural GOP at 330, then force another IDR at 331. The one-frame
        // middle GOP has no AAC starts; only the preceding committed AAC covers it.
        foreach (int delay in new[] { 0, 1600 })
        {
            using var forced = new FragmentedReplayWriter(output, new(160,90), new(EncoderDelaySamples:delay));
            for (int frame = 0; frame < 330; frame++)
            {
                var source = frames[frame]; forced.AddVideo(video.AsSpan(source.Start,source.Length),frame,source.Key);
            }
            long codedEnd = 0; int packetIndex = 0;
            while (codedEnd - delay < 96000 + 1024)
            {
                if (delay == 0)
                {
                    var packet = packets[packetIndex]; forced.AddAudio(audio.AsSpan(packet.Start,packet.Length),codedEnd);
                }
                else
                {
                    var packet = fdkPackets[packetIndex]; forced.AddAudio(fdk.AsSpan(packet.Offset,packet.Length),packet.Index);
                }
                packetIndex++; codedEnd += 1024;
            }
            var boundary = frames[330]; forced.AddVideo(video.AsSpan(boundary.Start,boundary.Length),330,true);
            Require(forced.CompletedThroughVideoFrame == 330,"Open IDR must remain outside the completed checkpoint");
            var firstIdr = frames[0]; forced.AddVideo(video.AsSpan(firstIdr.Start,firstIdr.Length),331,true);
            Require(forced.CompletedThroughVideoFrame == 331 && forced.Metrics.CompletedFragments == 2,"Forced boundary must commit the last requested frame before Finish");
            var clip = forced.ExportNewest(Path.Combine(output,$"live-forced-tiny-delay-{delay}.mp4"),60);
            Require(clip.Fragments == 2 && Math.Abs(clip.DurationSeconds-331/165.0)<0.000001,"Live forced export omitted hotkey-time frame");
            VerifyIndexAndHeaders(clip.Path,2,331,165,codedEnd,delay);
            run(ffmpeg,["-hide_banner","-v","error","-xerror","-err_detect","explode","-threads","1","-i",clip.Path,"-threads","1","-f","null","-"]);
            await VerifyEditorSeek(clip.Path,clip.DurationSeconds,[0,329,330]);
            Console.WriteLine($"PASS live forced tiny GOP: checkpoint=331 fragments=2 delay={delay}; strict decode, AAC headers and native MLT tail seek");
        }
        using var primed = new FragmentedReplayWriter(output, new(160, 90), new(EncoderDelaySamples: 1600));
        int nextPacket = 0;
        for (int frame = 0; frame < 6 * 165; frame++)
        {
            var source = frames[frame]; primed.AddVideo(video.AsSpan(source.Start, source.Length), frame, source.Key);
            while (nextPacket < fdkPackets.Length && (fdkPackets[nextPacket].Index - 1600) / 48000.0 < (frame + 1.0) / 165)
            {
                var packet = fdkPackets[nextPacket++]; primed.AddAudio(fdk.AsSpan(packet.Offset, packet.Length), packet.Index);
            }
        }
        Require(nextPacket == fdkPackets.Length, "FDK fixture packets were not drained");
        // A live export ends at four seconds; its audible tail must cover that video end.
        var live = primed.ExportNewest(Path.Combine(output, "fdk-live-gop-2-to-4.mp4"), 1);
        primed.Finish();
        var full = primed.ExportNewest(Path.Combine(output, "fdk-full-6s.mp4"), 6);
        var end = primed.ExportNewest(Path.Combine(output, "fdk-last-gop-4-to-6.mp4"), 1);
        VerifyIndexAndHeaders(full.Path, 3, 990, 165, 289792, 1600);
        using (var finalProbe = JsonDocument.Parse(run(ffprobe, ["-v", "error", "-show_format", "-show_streams", "-of", "json", full.Path])))
        {
            Require(Math.Abs(Number(finalProbe.RootElement.GetProperty("streams")[0].GetProperty("duration")) - 6) < 0.000002, "Primed clip video duration must be exact");
            double reported = Number(finalProbe.RootElement.GetProperty("format").GetProperty("duration"));
            Require(reported >= 6 && reported <= 6 + (1600 + 1024.0) / 48000, "Coded AAC duration exceeds known priming/padding extent");
        }
        byte[] fullPcm = Decode(full.Path);
        Require(fullPcm.Length / 4 <= 288000 + 1024, "Decoded end exceeds AAC packet granularity after presentation trim");
        Require(Correlation(fullPcm, 0, sourcePcm, 0, 48000) > 0.99, "AAC edit did not remove the exact initial priming delay");
        int firstPeak = Peak(fullPcm, 156000), secondPeak = Peak(fullPcm, 216000);
        Require(Math.Abs(firstPeak - 156000) <= 4 && Math.Abs(secondPeak - 216000) <= 4 && Math.Abs(secondPeak - firstPeak - 60000) <= 4, "FDK impulse alignment drifted");
        VerifyLater(live.Path, 96000, 156000, 2);
        VerifyLater(end.Path, 192000, 216000, 2);
        await VerifyEditorSeek(full.Path, 6, [0, 495, 987, 988, 989]);
        Console.WriteLine("PASS FDK delay1600: edts/elst trims initial priming, both impulses align without drift, later-GOP boundary gaps <1024 samples, live audio covers full video tail, native MLT seeks");

        byte[] Decode(string path)
        {
            string pcmPath = path + ".pcm";
            run(ffmpeg, ["-hide_banner", "-v", "error", "-xerror", "-err_detect", "explode", "-threads", "1", "-i", path, "-map", "0:a:0", "-ac", "2", "-ar", "48000", "-f", "s16le", pcmPath]);
            return File.ReadAllBytes(pcmPath);
        }
        void VerifyLater(string path, int originSamples, int impulse, double videoSeconds)
        {
            using var probe = JsonDocument.Parse(run(ffprobe, ["-v", "error", "-select_streams", "a:0", "-show_streams", "-show_packets", "-of", "json", path]));
            var list = probe.RootElement.GetProperty("packets");
            double first = Number(list[0].GetProperty("pts_time"));
            var last = list[list.GetArrayLength() - 1];
            double tail = Number(last.GetProperty("pts_time")) + Number(last.GetProperty("duration_time"));
            int gap = (int)Math.Round(first * 48000);
            Require(gap >= 0 && gap < 1024, "FDK clip boundary gap exceeds one AAC packet");
            Require(tail >= videoSeconds && tail - videoSeconds < 1024.0 / 48000, "Presentation partition omitted live audible tail");
            using (var presentationProbe = JsonDocument.Parse(run(ffprobe, ["-v", "error", "-show_format", "-show_streams", "-of", "json", path])))
            {
                Require(Math.Abs(Number(presentationProbe.RootElement.GetProperty("streams")[0].GetProperty("duration")) - videoSeconds) < 0.000002, "Later clip video presentation is not exact");
                double reported = Number(presentationProbe.RootElement.GetProperty("format").GetProperty("duration"));
                Require(reported >= videoSeconds && reported <= videoSeconds + 2048.0 / 48000, "Later coded AAC duration exceeds boundary gap plus one packet tail");
            }
            byte[] decoded = Decode(path);
            Require(decoded.Length / 4 + gap <= (int)Math.Round(videoSeconds * 48000) + 1024, "Later decoded end exceeds AAC packet granularity");
            Require(Correlation(decoded, 2048, sourcePcm, originSamples + gap + 2048, 4096) > 0.99, "Later-GOP audio phase does not match source clock");
            int expected = impulse - originSamples - gap;
            Require(Math.Abs(Peak(decoded, expected) - expected) <= 4, "Later-GOP impulse is not aligned after priming edit");
            Console.WriteLine($"PASS {Path.GetFileName(path)}: initial packet gap={gap} samples, audible tail={tail:F6}s, impulse source alignment exact");
        }
    }
    static async Task VerifyEditorSeek(string path, double duration, long[] positions)
    {
        var media = new MediaAsset { Name = Path.GetFileName(path), Path = path, Kind = MediaKind.Video, DurationSeconds = duration, Width = 160, Height = 90, Fps = new(165, 1), HasAudio = true };
        var sequence = new EditSequence { Width = 160, Height = 90, Fps = new(165, 1), Tracks = [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = media.Id, Duration = (long)Math.Round(duration * 165) }] }] };
        var project = new EditProject { Media = [media], Sequences = [sequence], ActiveSequenceId = sequence.Id };
        await using var editor = new MltSession();
        await editor.LoadAsync(project, 160);
        foreach (long position in positions)
        {
            var frame = await editor.GetFrameAsync(position);
            Require(frame.Position == position && frame.Rgba.Length == 160 * 90 * 4 && frame.Rgba.Where((_, i) => i % 4 != 3).Any(value => value > 32), "Native editor seek did not produce valid image pixels");
            Require(frame.AudioPcm.Length > 0 && frame.AudioPcm.Any(value => value != 0), "Native editor seek lost audio");
        }
    }
    static void VerifyIndexAndHeaders(string path, int fragments, long videoFrames, int fps, long audioCodedEnd, int delay)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[16]; byte[]? moov = null; long offset = 0;
        while (offset + 8 <= input.Length)
        {
            input.Position = offset; input.ReadExactly(header[..8]); int size = checked((int)U32(header));
            Require(size >= 8 && offset + size <= input.Length, "Invalid top-level MP4 size");
            if (header.Slice(4, 4).SequenceEqual("moov"u8)) { moov = new byte[size]; input.Position = offset; input.ReadExactly(moov); }
            offset += size;
        }
        Require(offset == input.Length && moov != null, "Missing MP4 movie header");
        byte[] movie = moov!;
        int mvhd = Child(movie, 0, "mvhd");
        uint videoMs = checked((uint)videoFrames);
        Require(U32(movie.AsSpan(mvhd + 20)) == fps, "Movie timescale must preserve exact video frame boundaries");
        uint audioMs = checked((uint)Math.Min(videoFrames, (Math.Max(0, audioCodedEnd - delay) * fps + 47999) / 48000));
        Require(U32(movie.AsSpan(mvhd + 24)) == Math.Max(videoMs, audioMs), "mvhd duration was not patched");
        foreach (int track in Children(movie, 0).Where(box => Encoding.ASCII.GetString(movie, box + 4, 4) == "trak"))
        {
            int tkhd = Child(movie, track, "tkhd"), mdia = Child(movie, track, "mdia"), mdhd = Child(movie, mdia, "mdhd");
            uint id = U32(movie.AsSpan(tkhd + 20));
            Require(U32(movie.AsSpan(tkhd + 28)) == (id == 1 ? videoMs : audioMs), "tkhd duration was not patched");
            Require(U32(movie.AsSpan(mdhd + 24)) == (id == 1 ? videoFrames : audioCodedEnd), "mdhd duration was not patched");
            if (id == 2)
            {
                int edit = Child(movie, Child(movie, track, "edts"), "elst");
                Require(U32(movie.AsSpan(edit + 16)) == audioMs && U32(movie.AsSpan(edit + 20)) == delay, "AAC priming edit is wrong");
            }
        }
        input.Position = input.Length - 16; input.ReadExactly(header);
        Require(U32(header) == 16 && header.Slice(4, 4).SequenceEqual("mfro"u8), "mfra footer missing");
        uint mfraSize = U32(header[12..]); long mfra = input.Length - mfraSize;
        Require(mfraSize == 48 + fragments * 19, "mfra size is wrong");
        input.Position = mfra + 8; Span<byte> tfra = stackalloc byte[24]; input.ReadExactly(tfra);
        Require(tfra.Slice(4, 4).SequenceEqual("tfra"u8) && U32(tfra[8..]) == 0x01000000 && U32(tfra[12..]) == 1 && U32(tfra[20..]) == fragments, "tfra track/version/count is wrong");
        Span<byte> entry = stackalloc byte[19]; ulong previousTime = 0, previousOffset = 0;
        for (int i = 0; i < fragments; i++)
        {
            input.ReadExactly(entry); long next = input.Position;
            ulong time = BinaryPrimitives.ReadUInt64BigEndian(entry), moof = BinaryPrimitives.ReadUInt64BigEndian(entry[8..]);
            Require((i == 0 ? time == 0 : time > previousTime) && time < (ulong)videoFrames && moof > previousOffset && moof < (ulong)mfra && entry[16] == 1 && entry[17] == 1 && entry[18] == 1, "Invalid tfra random-access entry");
            input.Position = (long)moof + 4; input.ReadExactly(header[..4]); Require(header[..4].SequenceEqual("moof"u8), "tfra offset does not point at the copied moof");
            input.Position = next; previousTime = time; previousOffset = moof;
        }
    }
    static uint U32(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32BigEndian(bytes);
    static IEnumerable<int> Children(byte[] bytes, int parent)
    {
        int end = parent + checked((int)U32(bytes.AsSpan(parent)));
        for (int position = parent + 8; position + 8 <= end;)
        {
            int size = checked((int)U32(bytes.AsSpan(position))); Require(size >= 8 && position + size <= end, "Malformed child box");
            yield return position; position += size;
        }
    }
    static int Child(byte[] bytes, int parent, string name) => Children(bytes, parent).Single(position => Encoding.ASCII.GetString(bytes, position + 4, 4) == name);
    static double Number(JsonElement element) => double.Parse(element.GetString()!, CultureInfo.InvariantCulture);
    static short Sample(byte[] pcm, int frame) => BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(frame * 4, 2));
    static double Correlation(byte[] a, int firstA, byte[] b, int firstB, int count)
    {
        double product = 0, aa = 0, bb = 0;
        for (int i = 0; i < count; i++) { double x = Sample(a, firstA + i), y = Sample(b, firstB + i); product += x * y; aa += x * x; bb += y * y; }
        return product / Math.Sqrt(aa * bb);
    }
    static int Peak(byte[] pcm, int expected)
    {
        int peak = Math.Max(0, expected - 1024);
        for (int i = peak + 1; i < Math.Min(pcm.Length / 4, expected + 1025); i++) if (Math.Abs((int)Sample(pcm, i)) > Math.Abs((int)Sample(pcm, peak))) peak = i;
        return peak;
    }
}




