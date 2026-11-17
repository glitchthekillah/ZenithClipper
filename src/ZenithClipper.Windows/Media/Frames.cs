using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZenithClipper.Windows.Media;
using Control = System.Windows.Forms.Control;

public sealed record ClipInfo(double Duration, int Width, int Height, double Fps, bool HasAudio, long Bytes);

/// <summary>
/// On-demand FFmpeg helpers for the UI processes only. Every call is a short-lived FFmpeg child that decodes
/// a few frames into raw BGRA; nothing here runs inside the engine or while the UI is closed.
/// </summary>
static class Frames
{
    static readonly SemaphoreSlim jobs = new(1);
    static string Ffmpeg(Settings s) => MediaTools.FindFfmpeg(s);
    static string Inv(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    public static string CacheDir { get { string dir = Path.Combine(AppPaths.Root, "cache"); Directory.CreateDirectory(dir); return dir; } }
    static string Key(string path)
    {
        var info = new FileInfo(path);
        string id = $"{Path.GetFullPath(path).ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..20];
    }

    public static async Task<ClipInfo> InfoAsync(Settings settings, string path, CancellationToken ct = default)
    {
        string cache = Path.Combine(CacheDir, Key(path) + ".metadata-v2.json");
        try { if (File.Exists(cache)) { var cached=JsonSerializer.Deserialize<ClipInfo>(await File.ReadAllTextAsync(cache,ct)); if(cached is {Width:>0,Height:>0,Duration:>0} && double.IsFinite(cached.Fps) && cached.Fps>0)return cached; } }
        catch (Exception ex) when(ex is JsonException or IOException) { }
        string json = await MediaTools.ProbeAsync(Ffmpeg(settings), path, ct);
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var video = streams.First(x => x.GetProperty("codec_type").GetString() == "video");
        double duration = ReadDuration(doc.RootElement, video);
        double fps = 60;
        if (video.TryGetProperty("avg_frame_rate", out var rate) && rate.GetString() is { } text && text.Split('/') is [var n, var m] &&
            double.TryParse(n, CultureInfo.InvariantCulture, out var num) && double.TryParse(m, CultureInfo.InvariantCulture, out var den) && den > 0 && num > 0) fps = num / den;
        var info = new ClipInfo(duration, video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(), Math.Round(fps, 2),
            streams.Any(x => x.GetProperty("codec_type").GetString() == "audio"), new FileInfo(path).Length);
        try { await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(info), ct); } catch (IOException) { }
        return info;
    }

    internal static double ReadDuration(JsonElement root, JsonElement video)
    {
        static double Number(JsonElement item)
        {
            if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("duration",out var token))return 0;
            return double.TryParse(token.ValueKind==JsonValueKind.String?token.GetString():token.GetRawText(),NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&double.IsFinite(value)&&value>0?value:0;
        }
        double duration=root.TryGetProperty("format",out var format)?Number(format):0;
        double videoDuration=Number(video);
        bool cover=video.ValueKind==JsonValueKind.Object&&video.TryGetProperty("disposition",out var flags)&&flags.TryGetProperty("attached_pic",out var attached)&&attached.GetInt32()==1;
        return !cover&&videoDuration>0&&videoDuration<=duration&&duration-videoDuration<=.1+1e-9?videoDuration:duration;
    }

    /// <summary>One frame at <paramref name="seconds"/>, decoded on the GPU when FFmpeg supports it, scaled to exactly width × height.</summary>
    public static async Task<Bitmap?> GrabAsync(Settings settings, string path, double seconds, int width, int height, CancellationToken ct = default)
    {
        width = Math.Max(2, width & ~1); height = Math.Max(2, height & ~1);
        await jobs.WaitAsync(ct);
        try
        {
            var info = new ProcessStartInfo(Ffmpeg(settings)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-hwaccel", "auto", "-threads", "1", "-filter_threads", "1", "-filter_complex_threads", "1", "-ss", Inv(Math.Max(0, seconds)), "-i", path, "-frames:v", "1", "-an",
                "-vf", $"scale={width}:{height}:flags=bilinear,format=bgra", "-f", "rawvideo", "pipe:1" }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info) ?? throw new IOException("Could not start FFmpeg.");
            MediaTools.LowerPriority(process);
            _ = process.StandardError.ReadToEndAsync(ct);
            var buffer = new byte[width * height * 4];
            int read = 0;
            try
            {
                var stream = process.StandardOutput.BaseStream;
                while (read < buffer.Length) { int n = await stream.ReadAsync(buffer.AsMemory(read), ct); if (n == 0) break; read += n; }
                await process.WaitForExitAsync(ct);
            }
            catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
            return read == buffer.Length ? ToBitmap(buffer, width, height) : null;
        }
        finally { jobs.Release(); }
    }

    public static Bitmap ToBitmap(byte[] bgra, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        Copy(bgra, bitmap); return bitmap;
    }
    public static void Copy(byte[] bgra, Bitmap target)
    {
        var data = target.LockBits(new Rectangle(0, 0, target.Width, target.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            int row = target.Width * 4;
            if (data.Stride == row) Marshal.Copy(bgra, 0, data.Scan0, Math.Min(bgra.Length, row * target.Height));
            else for (int y = 0; y < target.Height; y++) Marshal.Copy(bgra, y * row, data.Scan0 + y * data.Stride, row);
        }
        finally { target.UnlockBits(data); }
    }

    /// <summary>Cached 16:9 library thumbnail.</summary>
    public static async Task<Bitmap?> ThumbnailAsync(Settings settings, string path, CancellationToken ct = default)
    {
        string cache = Path.Combine(CacheDir, Key(path) + ".jpg");
        if (File.Exists(cache)) try { using var stream = File.OpenRead(cache); return new Bitmap(stream); } catch (ArgumentException) { }
        var info = await InfoAsync(settings, path, ct);
        int h = 144, w = Math.Max(2, (int)Math.Round(h * info.Width / (double)Math.Max(1, info.Height)) & ~1);
        var frame = await GrabAsync(settings, path, Math.Min(info.Duration * 0.3, Math.Max(0, info.Duration - 0.1)), w, h, ct);
        if (frame == null) return null;
        try { frame.Save(cache, ImageFormat.Jpeg); } catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.ExternalException) { }
        return frame;
    }

    /// <summary>Evenly spaced frames for the timeline filmstrip, delivered as they arrive.</summary>
    public static async Task FilmstripAsync(Settings settings, string path, ClipInfo info, int count, int tileHeight, Action<int, Bitmap> each, CancellationToken ct)
    {
        int h = Math.Max(2, tileHeight & ~1), w = Math.Max(2, (int)Math.Round(h * info.Width / (double)Math.Max(1, info.Height)) & ~1);
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            double t = info.Duration * (i + 0.5) / count;
            var frame = await GrabAsync(settings, path, t, w, h, ct);
            if (frame != null) each(i, frame);
        });
        await Task.WhenAll(tasks);
    }

    /// <summary>Peak audio level per bucket (0–1), decoded at a low rate so even hour-long clips finish quickly.</summary>
    public static async Task<float[]> PeaksAsync(Settings settings, string path, ClipInfo info, int buckets, CancellationToken ct)
    {
        var peaks = new float[buckets];
        if (!info.HasAudio || info.Duration <= 0) return peaks;
        const int rate = 2000;
        var start = new ProcessStartInfo(Ffmpeg(settings)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-threads", "1", "-filter_threads", "1", "-filter_complex_threads", "1", "-i", path, "-vn", "-ac", "1", "-ar", rate.ToString(CultureInfo.InvariantCulture), "-f", "s16le", "pipe:1" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start FFmpeg.");
        MediaTools.LowerPriority(process);
        _ = process.StandardError.ReadToEndAsync(ct);
        double perBucket = Math.Max(1, info.Duration * rate / buckets);
        var buffer = new byte[16384]; long sample = 0; int carry = -1;
        try
        {
            var stream = process.StandardOutput.BaseStream;
            int n;
            while ((n = await stream.ReadAsync(buffer, ct)) > 0)
            {
                int i = 0;
                if (carry >= 0) { Add((short)(carry | buffer[0] << 8)); i = 1; carry = -1; }
                for (; i + 1 < n; i += 2) Add((short)(buffer[i] | buffer[i + 1] << 8));
                if (i < n) carry = buffer[i];
            }
            await process.WaitForExitAsync(ct);
        }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
        float max = Math.Max(0.05f, peaks.Max());
        for (int i = 0; i < peaks.Length; i++) peaks[i] = MathF.Sqrt(peaks[i] / max);
        return peaks;
        void Add(short value)
        {
            int b = (int)Math.Min(buckets - 1, sample++ / perBucket);
            float v = Math.Abs(value / 32768f); if (v > peaks[b]) peaks[b] = v;
        }
    }
}

/// <summary>
/// Silent in-editor playback. FFmpeg decodes at real-time pace (-re) into raw frames on a pipe; the UI thread pulls
/// only the newest frame, so a slow repaint drops frames instead of queueing memory.
/// </summary>
sealed class PreviewPlayer : IDisposable
{
    Process? process;
    CancellationTokenSource? cts;
    byte[]? latest, delivering;
    double latestTime;
    readonly object gate = new();
    public bool Playing => process is { HasExited: false };
    public event Action? Ended;

    public void Start(Settings settings, string path, double from, double until, int width, int height, double fps, SynchronizationContext ui, Action<byte[], double> frame)
    {
        Stop();
        width = Math.Max(2, width & ~1); height = Math.Max(2, height & ~1); fps = Math.Clamp(fps, 10, 30);
        var info = new ProcessStartInfo(MediaTools.FindFfmpeg(settings)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-hwaccel", "auto", "-threads", "1", "-filter_threads", "1", "-filter_complex_threads", "1", "-re", "-ss", from.ToString("0.###", CultureInfo.InvariantCulture), "-i", path,
            "-t", Math.Max(0.05, until - from).ToString("0.###", CultureInfo.InvariantCulture), "-an",
            "-vf", $"fps={fps.ToString("0.###", CultureInfo.InvariantCulture)},scale={width}:{height}:flags=bilinear,format=bgra", "-f", "rawvideo", "pipe:1" }) info.ArgumentList.Add(arg);
        var p = Process.Start(info) ?? throw new IOException("Could not start FFmpeg.");
        MediaTools.LowerPriority(p);
        process = p; var source = cts = new CancellationTokenSource(); var token = source.Token;
        _ = p.StandardError.ReadToEndAsync(token);
        int size = width * height * 4;
        _ = Task.Run(async () =>
        {
            var pool = new[] { new byte[size], new byte[size], new byte[size] }; int slot = 0; long index = 0;
            try
            {
                var stream = p.StandardOutput.BaseStream;
                while (!token.IsCancellationRequested)
                {
                    byte[] buf;
                    lock(gate) { while(ReferenceEquals(pool[slot],latest) || ReferenceEquals(pool[slot],delivering)) slot=(slot+1)%pool.Length; buf=pool[slot]; }
                    int read = 0;
                    while (read < size) { int n = await stream.ReadAsync(buf.AsMemory(read, size - read), token); if (n == 0) break; read += n; }
                    if (read < size) break;
                    double t = from + index++ / fps; bool post;
                    lock (gate) { post = latest == null; latest = buf; latestTime = t; }
                    slot = (slot + 1) % pool.Length;
                    if (post) ui.Post(_ => { if(!token.IsCancellationRequested)Deliver(frame); }, null);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            if (!token.IsCancellationRequested) ui.Post(_ => { if (ReferenceEquals(process, p)) { Stop(); Ended?.Invoke(); } }, null);
        }, token);
    }
    void Deliver(Action<byte[], double> frame)
    {
        byte[]? buf; double t;
        lock (gate) { buf = latest; t = latestTime; latest = null; delivering=buf; }
        try { if (buf != null && Playing) frame(buf, t); }
        finally { lock(gate)delivering=null; }
    }
    public void Stop()
    {
        cts?.Cancel(); cts?.Dispose(); cts = null;
        if (process != null)
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            process.Dispose(); process = null;
        }
        lock (gate) latest = null;
    }
    public void Dispose() => Stop();
}
