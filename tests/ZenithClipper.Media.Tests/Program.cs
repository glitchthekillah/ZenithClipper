using System.Diagnostics;
using System.Text.Json;
using ZenithClipper.Media;

string root = Path.GetFullPath(args.Length == 0 ? "../.." : args[0]);
string ffmpeg = Path.Combine(root, "build/windows/ffmpeg.exe");
string ffprobe = Path.Combine(root, "build/windows/ffprobe.exe");
string test = Path.Combine(root, "outputs", "mp4-mux-tests-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(test);
ForcedBoundaryChecks.Run(test);
HotPathChecks.Run(test);
if (Environment.GetEnvironmentVariable("ZENITH_MEDIA_TEST_HOTPATH_ONLY") == "1") return;
string Run(string exe, params string[] arguments)
{
    var info = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (string a in arguments) info.ArgumentList.Add(a);
    using var process = Process.Start(info)!;
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(60000)) { process.Kill(true); throw new Exception("Process timed out."); }
    Task.WaitAll(output, error);
    if (process.ExitCode != 0) throw new Exception($"{Path.GetFileName(exe)} failed: {error.Result}");
    return output.Result;
}
void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
Run(ffmpeg, "-hide_banner", "-v", "error", "-threads", "1", "-filter_threads", "1", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=165", "-t", "7", "-an", "-c:v", "libx264", "-threads", "1", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-x264-params", "keyint=330:min-keyint=330:scenecut=0:bframes=0:repeat-headers=1:aud=1:cabac=1:8x8dct=1", "-f", "h264", Path.Combine(test, "video.h264"));
Run(ffmpeg, "-hide_banner", "-v", "error", "-threads", "1", "-filter_threads", "1", "-f", "lavfi", "-i", "sine=frequency=997:sample_rate=48000", "-t", "7", "-ac", "2", "-c:a", "aac", "-threads", "1", "-b:a", "192k", "-f", "adts", Path.Combine(test, "audio.aac"));
byte[] h264 = File.ReadAllBytes(Path.Combine(test, "video.h264"));
List<(int Start, int Length, bool Key)> frames = [];
List<int> auds = [];
for (int i = 0; i + 5 < h264.Length; i++)
{
    int prefix = h264[i] == 0 && h264[i + 1] == 0 && h264[i + 2] == 0 && h264[i + 3] == 1 ? 4 : h264[i] == 0 && h264[i + 1] == 0 && h264[i + 2] == 1 ? 3 : 0;
    if (prefix != 0) { if ((h264[i + prefix] & 31) == 9) auds.Add(i); i += prefix; }
}
for (int i = 0; i < auds.Count; i++) frames.Add((auds[i], (i + 1 < auds.Count ? auds[i + 1] : h264.Length) - auds[i], i % 330 == 0));
byte[] adts = File.ReadAllBytes(Path.Combine(test, "audio.aac"));
List<(int Start, int Length)> packets = [];
for (int i = 0; i < adts.Length;)
{
    Assert(adts[i] == 255 && (adts[i + 1] & 0xf6) == 0xf0, "Invalid ADTS fixture");
    int length = (adts[i + 3] & 3) << 11 | adts[i + 4] << 3 | adts[i + 5] >> 5;
    int header = (adts[i + 1] & 1) == 1 ? 7 : 9;
    packets.Add((i + header, length - header)); i += length;
}
if (Environment.GetEnvironmentVariable("ZENITH_MEDIA_TEST_FDK_ONLY") == "1")
{
    await MediaCompatibilityChecks.RunAll(root, test, ffmpeg, ffprobe, h264, frames, adts, packets, Run);
    Console.WriteLine(test); return;
}
using var writer = new FragmentedReplayWriter(test, new(160, 90), retentionSeconds: 4);
int ai = 0;
for (int vi = 0; vi < frames.Count; vi++)
{
    var v = frames[vi]; writer.AddVideo(h264.AsSpan(v.Start, v.Length), vi, v.Key);
    while (ai < packets.Count && ai * 1024.0 / 48000 < (vi + 1.0) / 165)
    {
        var a = packets[ai]; writer.AddAudio(adts.AsSpan(a.Start, a.Length), ai * 1024L); ai++;
    }
}
while (ai < packets.Count) { var a = packets[ai]; writer.AddAudio(adts.AsSpan(a.Start, a.Length), ai * 1024L); ai++; }
writer.Finish();
foreach (double seconds in new[] { 1.0, 2.5, 60.0 })
{
    string path = Path.Combine(test, $"replay-{seconds}.mp4");
    var export = writer.ExportNewest(path, seconds);
    Run(ffmpeg, "-hide_banner", "-v", "error", "-xerror", "-err_detect", "explode", "-threads", "1", "-i", path, "-threads", "1", "-f", "null", "-");
    using var probe = JsonDocument.Parse(Run(ffprobe, "-v", "error", "-show_streams", "-show_packets", "-of", "json", path));
    var streams = probe.RootElement.GetProperty("streams");
    Assert(streams.GetArrayLength() == 2, "Two streams required");
    Assert(streams[0].GetProperty("codec_name").GetString() == "h264", "H264 expected");
    Assert(streams[0].GetProperty("profile").GetString() == "High", "High-profile avcC fixture expected");
    Assert(streams[1].GetProperty("codec_name").GetString() == "aac", "AAC expected");
    double[] first = [double.NaN, double.NaN], last = [0, 0]; long[] count = [0, 0];
    foreach (var packet in probe.RootElement.GetProperty("packets").EnumerateArray())
    {
        int track = packet.GetProperty("stream_index").GetInt32();
        double pts = double.Parse(packet.GetProperty("pts_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        double duration = double.Parse(packet.GetProperty("duration_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        if (double.IsNaN(first[track])) { first[track] = pts; if (track == 0) Assert(packet.GetProperty("flags").GetString()!.Contains('K'), "Export must start at IDR"); }
        else Assert(Math.Abs(pts - last[track]) < 0.000002, "Packets must have continuous timestamps");
        last[track] = pts + duration; count[track]++;
    }
    Assert(first[0] == 0 && first[1] >= 0 && first[1] < 1024.0 / 48000, "Export origins/sync incorrect");
    Assert(Math.Abs(last[0] - export.DurationSeconds) < 0.00001, "Video duration mismatch");
    Assert(Math.Abs(last[0] - last[1]) < 0.05, "Audio/video end sync exceeds 50ms");
    Assert(export.DurationSeconds >= Math.Min(seconds, 5), "Whole-GOP coverage too short");
    Console.WriteLine($"PASS {Path.GetFileName(path)}: duration={export.DurationSeconds:F3}s fragments={export.Fragments} video={count[0]} audio={count[1]} sync={Math.Abs(last[0] - last[1]):F6}s");
    byte[] original = File.ReadAllBytes(path);
    try { writer.ExportNewest(path, 1); throw new Exception("Existing output overwritten"); } catch (IOException) { }
    Assert(original.SequenceEqual(File.ReadAllBytes(path)), "Original export changed");
}
Assert(Directory.GetFiles(writer.SpoolDirectory, "*.m4s").Length <= 3, "Retention failed");
Console.WriteLine("PASS existing-output protection and bounded retained fragment count");
Console.WriteLine(test);
// Two minutes at 165 fps exercises pruning without GPU, capture devices, or payload accumulation.
using var ring = new FragmentedReplayWriter(test, new(160, 90), retentionSeconds: 4);
long stable = 0;
int audioCursor = 0;
for (int frame = 0; frame < 120 * 165; frame++)
{
    var source = frames[frame % 660];
    ring.AddVideo(h264.AsSpan(source.Start, source.Length), frame, frame % 330 == 0);
    while (audioCursor * 1024.0 / 48000 < (frame + 1.0) / 165)
    {
        var sourceAudio = packets[audioCursor % (packets.Count - 2)];
        ring.AddAudio(adts.AsSpan(sourceAudio.Start, sourceAudio.Length), audioCursor * 1024L); audioCursor++;
    }
    if (frame == 10 * 165) stable = GC.GetTotalMemory(true);
}
long retainedGrowth = GC.GetTotalMemory(true) - stable;
Assert(retainedGrowth < 2 * 1024 * 1024, $"Managed retained memory grew by {retainedGrowth} bytes");
Assert(Directory.GetFiles(ring.SpoolDirectory, "*.m4s").Length <= 3, "Long-running ring failed pruning");
var live = ring.ExportNewest(Path.Combine(test, "live-after-120s.mp4"), 1);
Run(ffmpeg, "-hide_banner", "-v", "error", "-xerror", "-err_detect", "explode", "-threads", "1", "-i", live.Path, "-threads", "1", "-f", "null", "-");
Console.WriteLine($"PASS 120-second ring, 19800 video frames: retained managed growth={retainedGrowth} bytes, live export={live.DurationSeconds:F3}s");
// Stop one frame after the IDR at 4 seconds. The prior AAC packet covers that final
// six-millisecond GOP; there is deliberately no AAC packet with a start inside it.
using (var nearIdr = new FragmentedReplayWriter(test, new(160, 90)))
{
    int nextAudio = 0;
    for (int frame = 0; frame <= 660; frame++)
    {
        var source = frames[frame]; nearIdr.AddVideo(h264.AsSpan(source.Start, source.Length), frame, source.Key);
        while (nextAudio * 1024.0 / 48000 < (frame + 1.0) / 165)
        {
            var sourceAudio = packets[nextAudio];
            nearIdr.AddAudio(adts.AsSpan(sourceAudio.Start, sourceAudio.Length), nextAudio * 1024L); nextAudio++;
        }
    }
    Assert(nextAudio * 1024 == 192512, "Near-IDR fixture must have exactly one AAC packet covering the last tiny GOP");
    nearIdr.Finish();
    var nearExport = nearIdr.ExportNewest(Path.Combine(test, "near-idr-stop.mp4"), 1);
    Assert(nearExport.Fragments == 2 && Math.Abs(nearExport.DurationSeconds - 331.0 / 165) < 0.000001, "Final IDR video was dropped");
    Run(ffmpeg, "-hide_banner", "-v", "error", "-xerror", "-err_detect", "explode", "-threads", "1", "-i", nearExport.Path, "-threads", "1", "-f", "null", "-");
    using var nearProbe = JsonDocument.Parse(Run(ffprobe, "-v", "error", "-show_streams", "-show_packets", "-of", "json", nearExport.Path));
    Assert(nearProbe.RootElement.GetProperty("streams").GetArrayLength() == 2, "AAC track must remain in moov");
    int videoPackets = 0, audioPackets = 0; double videoEnd = 0, audioEnd = 0;
    foreach (var packet in nearProbe.RootElement.GetProperty("packets").EnumerateArray())
    {
        int track = packet.GetProperty("stream_index").GetInt32();
        double pts = double.Parse(packet.GetProperty("pts_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        double duration = double.Parse(packet.GetProperty("duration_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        if (track == 0)
        {
            Assert(Math.Abs(pts - videoEnd) < 0.000002, "Near-IDR video timestamps are not contiguous");
            if (++videoPackets == 331) Assert(packet.GetProperty("flags").GetString()!.Contains('K'), "Final video sample must be the preserved IDR");
            videoEnd = pts + duration;
        }
        else
        {
            if (audioPackets++ > 0) Assert(Math.Abs(pts - audioEnd) < 0.000002, "Near-IDR AAC timestamps are not contiguous");
            audioEnd = pts + duration;
        }
    }
    Assert(videoPackets == 331 && audioPackets == 94, "Near-IDR export lost video or fabricated AAC");
    Assert(audioEnd >= videoEnd && audioEnd - videoEnd < 1024.0 / 48000, "Previous AAC packet must cover the final tiny GOP");
    // Verify the final moof has exactly one traf (video), while the preceding one has audio.
    byte[] exported = File.ReadAllBytes(nearExport.Path); int lastMoof = -1;
    for (int offset = 0; offset + 8 <= exported.Length;)
    {
        int size = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(exported.AsSpan(offset, 4)));
        if (exported.AsSpan(offset + 4, 4).SequenceEqual("moof"u8)) lastMoof = offset;
        Assert(size >= 8, "Invalid exported MP4 box"); offset += size;
    }
    int trafs = 0, moofEnd = lastMoof + checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(exported.AsSpan(lastMoof, 4)));
    for (int offset = lastMoof + 8; offset < moofEnd;)
    {
        if (exported.AsSpan(offset + 4, 4).SequenceEqual("traf"u8)) trafs++;
        offset += checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(exported.AsSpan(offset, 4)));
    }
    Assert(trafs == 1, "Final no-AAC GOP must omit the audio traf");
    Console.WriteLine($"PASS near-IDR stop: preserved {videoPackets} video/{audioPackets} AAC packets, final moof video-only, duration={nearExport.DurationSeconds:F6}s");
}
await MediaCompatibilityChecks.RunAll(root, test, ffmpeg, ffprobe, h264, frames, adts, packets, Run);

if (Environment.GetEnvironmentVariable("ZENITH_MEDIA_TEST_PRESENTATION_ONLY") == "1") return;

// Disk-index scalability: twelve virtual hours at one IDR/second, above the old 4096-GOP cap.
int hourFragments = Environment.GetEnvironmentVariable("ZENITH_MEDIA_TEST_QUICK") == "1" ? 5000 : 43200;
using var hours = new FragmentedReplayWriter(test, new(160, 90, 1), retentionSeconds: 86400, includeAudio: false);
var idr = frames[0];
long hourBaseline = 0;
for (int i = 0; i < hourFragments; i++)
{
    hours.AddVideo(h264.AsSpan(idr.Start, idr.Length), i, true);
    if (i == 4096) hourBaseline = GC.GetTotalMemory(true);
}
hours.Finish();
var hourMetrics = hours.Metrics;
long hoursGrowth = GC.GetTotalMemory(true) - hourBaseline;
Assert(hourMetrics.CompletedFragments == hourFragments && hourMetrics.DurationSeconds == hourFragments, "Disk index lost hours of fragments");
Assert(hoursGrowth < 2 * 1024 * 1024, $"Hours metadata grew RAM by {hoursGrowth}");
var hourExport = hours.ExportNewest(Path.Combine(test, "hours-video-only.mp4"), 3);
Run(ffmpeg, "-hide_banner", "-v", "error", "-xerror", "-err_detect", "explode", "-threads", "1", "-i", hourExport.Path, "-threads", "1", "-f", "null", "-");
using (var videoProbe = JsonDocument.Parse(Run(ffprobe, "-v", "error", "-show_streams", "-of", "json", hourExport.Path)))
    Assert(videoProbe.RootElement.GetProperty("streams").GetArrayLength() == 1, "Video-only moov contains audio");
Console.WriteLine($"PASS disk index: fragments={hourMetrics.CompletedFragments} bytes={hourMetrics.TotalBytes} retained managed growth={hoursGrowth}");
// Cancellation, busy-export rejection, and unlocked ingestion while a long snapshot is copied.
string canceledPath = Path.Combine(test, "cancel-long-export.mp4");
using var cancelExport = new CancellationTokenSource();
Task saving = Task.Run(() => { try { hours.ExportNewest(canceledPath, 43200, cancelExport.Token); } catch (OperationCanceledException) { } });
Assert(SpinWait.SpinUntil(() => hours.Metrics.ExportActive || saving.IsCompleted, 10000), "Export failed to start");
Assert(!saving.IsCompleted, "Long export completed before lease test");
try { hours.ExportNewest(Path.Combine(test, "busy.mp4"), 1); throw new Exception("Concurrent export was allowed"); } catch (InvalidOperationException) { }
// Finished sessions still allow retention changes; this must return without waiting for export copying.
hours.SetRetention(2, 8L * 1024 * 1024 * 1024);
Assert(hours.Metrics.CompletedFragments == hourFragments, "Export lease did not protect snapshot");
Assert(SpinWait.SpinUntil(() => File.Exists(canceledPath), 10000), "Export never created its temporary destination");
cancelExport.Cancel(); Assert(saving.Wait(10000), "Canceled export did not finish");
Assert(!File.Exists(canceledPath) && !hours.Metrics.ExportActive, "Cancellation leaked output or lease");
Assert(SpinWait.SpinUntil(() => hours.Metrics.CompletedFragments <= 3, 60000), "Lease release did not prune");
Assert(Directory.GetFiles(hours.SpoolDirectory, "index-*.bin").Length == 1, "Old index blocks were not removed");
// Enforce a byte budget including open spool payload and index bytes.
hours.SetRetention(86400, 1024 * 1024);
Assert(hours.Metrics.TotalBytes <= 1024 * 1024, "Byte budget pruning failed");
using var liveIngest = new FragmentedReplayWriter(test, new(160, 90, 1), retentionSeconds: 86400, includeAudio: false);
for (int i = 0; i < 5000; i++) liveIngest.AddVideo(h264.AsSpan(idr.Start, idr.Length), i, true);
string liveCancelPath = Path.Combine(test, "cancel-ingest-export.mp4");
using var liveCancel = new CancellationTokenSource();
Task liveSaving = Task.Run(() => { try { liveIngest.ExportNewest(liveCancelPath, 5000, liveCancel.Token); } catch (OperationCanceledException) { } });
Assert(SpinWait.SpinUntil(() => liveIngest.Metrics.ExportActive || liveSaving.IsCompleted, 10000), "Live export failed to start");
liveIngest.AddVideo(h264.AsSpan(idr.Start, idr.Length), 5000, true);
Assert(liveIngest.Metrics.CompletedFragments == 5000 && liveIngest.Metrics.ExportActive, "Ingest blocked or snapshot changed");
liveIngest.SetRetention(86400, 1024 * 1024);
try { liveIngest.AddVideo(h264.AsSpan(idr.Start, idr.Length), 5001, true); throw new Exception("Leased budget overflow accepted"); } catch (IOException) { }
liveCancel.Cancel(); Assert(liveSaving.Wait(10000), "Live cancellation hung");
// A pre-existing locked file must survive, with the export lease released after CreateNew fails.
string lockedPath = Path.Combine(test, "locked-original.mp4"); File.WriteAllBytes(lockedPath, [1, 2, 3]);
using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
{
    try { liveIngest.ExportNewest(lockedPath, 1); throw new Exception("Locked destination overwritten"); } catch (IOException) { }
    Assert(!liveIngest.Metrics.ExportActive, "Failed export leaked lease");
}
Assert(File.ReadAllBytes(lockedPath).SequenceEqual(new byte[] {1,2,3}), "Locked original changed");
// Disposal cancels an active export and waits for cleanup before deleting its private spool.
string disposePath = Path.Combine(test, "dispose-canceled.mp4");
Task disposingExport = Task.Run(() => { try { liveIngest.ExportNewest(disposePath, 5000); } catch (OperationCanceledException) { } });
Assert(SpinWait.SpinUntil(() => liveIngest.Metrics.ExportActive || disposingExport.IsCompleted, 10000), "Dispose test export failed to start");
Assert(SpinWait.SpinUntil(() => File.Exists(disposePath), 10000), "Dispose test output was not created");
string disposedSpool = liveIngest.SpoolDirectory;
liveIngest.Dispose(); Assert(disposingExport.Wait(10000), "Dispose did not release export");
Assert(!Directory.Exists(disposedSpool) && !File.Exists(disposePath), "Dispose leaked spool or partial export");
Console.WriteLine($"PASS video-only, {hourFragments}-GOP metadata, retention/byte budget, concurrent ingest, busy rejection, cancellation/error/dispose cleanup");










