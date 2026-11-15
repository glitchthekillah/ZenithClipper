using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>
/// Timeline previews the way NLEs do them: a filmstrip of small frames per video file and a peak envelope per audio stream.
/// Each file is read once by ffmpeg in the background (below-normal priority, two files at a time) and cached on disk next to
/// the editor's other data, keyed by path, size and modified time, so reopening a project costs nothing.
/// </summary>
internal static class MediaPreviews
{
    internal const int PeaksPerSecond = 100, ThumbHeight = 90;
    internal sealed class Entry
    {
        public double Interval; public string[] Frames = []; public Bitmap?[] Loaded = [];
        public byte[]? Peaks; public bool ThumbsDone, PeaksDone;
        public Bitmap? Frame(int index)
        {
            if (Frames.Length == 0) return null;
            index = Math.Clamp(index, 0, Frames.Length - 1);
            if (Loaded[index] is null) try { Loaded[index] = new Bitmap(Frames[index]); } catch (Exception) { return null; }
            return Loaded[index];
        }
    }
    static readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    static readonly SemaphoreSlim gate = new(2);
    /// <summary>Raised on the UI thread when a preview finishes, so the timeline and media bin can redraw.</summary>
    public static event Action? Updated;
    public static string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Headless snapshots: wait (pumping the UI queue) until every started preview has finished.</summary>
    internal static void WaitAll(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout && pending > 0) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(50); }
        Dispatcher.UIThread.RunJobs();
    }
    static int pending;
    public static Entry? Get(MediaAsset media)
    {
        if (media.Kind is MediaKind.Text or MediaKind.Color || string.IsNullOrEmpty(media.Path) || !File.Exists(media.Path)) return null;
        return entries.GetOrAdd(media.Path, _ => Start(media));
    }

    static Entry Start(MediaAsset media)
    {
        var entry = new Entry();
        var info = new FileInfo(media.Path);
        string key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))[..20];
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZenithClipper", "cache", "previews", key);
        bool wantThumbs = media.Kind is MediaKind.Video or MediaKind.Image, wantPeaks = media.Kind == MediaKind.Audio || media.HasAudio;
        if (wantThumbs && TryLoadThumbs(dir, entry)) wantThumbs = false;
        if (wantPeaks && File.Exists(Path.Combine(dir, "peaks.done"))) { entry.Peaks = File.ReadAllBytes(Path.Combine(dir, "peaks.bin")); entry.PeaksDone = true; wantPeaks = false; }
        if (!wantThumbs && !wantPeaks) return entry;
        Interlocked.Increment(ref pending);
        _ = Task.Run(async () =>
        {
            await gate.WaitAsync();
            try
            {
                Directory.CreateDirectory(dir);
                if (wantThumbs)
                {
                    double interval = media.Kind == MediaKind.Image ? 1 : Math.Clamp(media.DurationSeconds / 240, .5, 30);
                    foreach (var old in Directory.EnumerateFiles(dir, "t*.jpg")) File.Delete(old);
                    var args = new List<string> { "-v", "error", "-nostdin" };
                    if (media.Kind == MediaKind.Video && media.DurationSeconds > 120) args.AddRange(["-skip_frame", "nokey"]);
                    args.AddRange(["-i", media.Path, "-an", "-sn", "-dn"]);
                    if (media.Kind == MediaKind.Image) args.AddRange(["-frames:v", "1", "-vf", $"scale=-2:{ThumbHeight}"]);
                    else args.AddRange(["-vf", $"fps=1/{interval.ToString(System.Globalization.CultureInfo.InvariantCulture)},scale=-2:{ThumbHeight}"]);
                    args.AddRange(["-q:v", "5", Path.Combine(dir, "t%05d.jpg")]);
                    await Run(args, null);
                    File.WriteAllText(Path.Combine(dir, "thumbs.done"), interval.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    TryLoadThumbs(dir, entry);
                    Post();
                }
                if (wantPeaks)
                {
                    var peaks = new List<byte>(Math.Max(16, (int)(media.DurationSeconds * PeaksPerSecond)));
                    const int window = 8000 / PeaksPerSecond; int count = 0, peak = 0; byte carry = 0; bool odd = false;
                    await Run(["-v", "error", "-nostdin", "-i", media.Path, "-vn", "-sn", "-dn", "-map", "0:a:0", "-ac", "1", "-ar", "8000", "-f", "s16le", "-"], chunk =>
                    {
                        foreach (byte b in chunk)
                        {
                            if (!odd) { carry = b; odd = true; continue; }
                            odd = false; int sample = Math.Abs((short)(carry | (b << 8))); if (sample > peak) peak = sample;
                            if (++count == window) { peaks.Add((byte)Math.Round(255 * Math.Sqrt(Math.Min(1, peak / 32767.0)))); count = 0; peak = 0; }
                        }
                    });
                    var data = peaks.ToArray();
                    File.WriteAllBytes(Path.Combine(dir, "peaks.bin"), data); File.WriteAllText(Path.Combine(dir, "peaks.done"), "1");
                    entry.Peaks = data; entry.PeaksDone = true;
                    Post();
                }
            }
            catch (Exception ex) { Trace.WriteLine("Preview failed for " + media.Path + ": " + ex.Message); }
            finally { gate.Release(); Interlocked.Decrement(ref pending); }
        });
        return entry;
    }

    static bool TryLoadThumbs(string dir, Entry entry)
    {
        var done = Path.Combine(dir, "thumbs.done");
        if (!File.Exists(done) || !double.TryParse(File.ReadAllText(done), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var interval)) return false;
        var frames = Directory.EnumerateFiles(dir, "t*.jpg").Order(StringComparer.Ordinal).ToArray();
        if (frames.Length == 0) return false;
        entry.Interval = interval; entry.Loaded = new Bitmap?[frames.Length]; entry.Frames = frames; entry.ThumbsDone = true;
        return true;
    }

    static void Post() => Dispatcher.UIThread.Post(() => Updated?.Invoke(), DispatcherPriority.Background);

    static async Task Run(IEnumerable<string> arguments, Action<ReadOnlySpan<byte>>? stdout)
    {
        var start = new ProcessStartInfo(FfmpegPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = stdout is not null, RedirectStandardError = true };
        foreach (var a in arguments) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start.");
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
        var errors = process.StandardError.ReadToEndAsync();
        if (stdout is not null)
        {
            var buffer = new byte[65536]; var stream = process.StandardOutput.BaseStream; int read;
            while ((read = await stream.ReadAsync(buffer)) > 0) stdout(buffer.AsSpan(0, read));
        }
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException((await errors).Trim());
    }
}

/// <summary>Continuous filmstrip for a video clip: frames tiled edge to edge, each showing the source time under it.</summary>
internal sealed class Filmstrip : Control
{
    public MediaPreviews.Entry? Entry; public double SourceSeconds, SecondsPerPixel, Aspect = 16 / 9.0; public bool Reverse; public double SpanSeconds;
    public override void Render(DrawingContext context)
    {
        var bounds = Bounds; if (Entry is not { ThumbsDone: true } entry || bounds.Width < 1 || bounds.Height < 1) return;
        double tileWidth = Math.Max(8, bounds.Height * Aspect);
        if (bounds.Width / tileWidth > 600) tileWidth = bounds.Width / 600;
        using (context.PushClip(new Rect(bounds.Size)))
            for (double x = 0; x < bounds.Width; x += tileWidth)
            {
                double offset = (x + tileWidth / 2) * SecondsPerPixel, t = Reverse ? SourceSeconds + SpanSeconds - offset : SourceSeconds + offset;
                if (entry.Frame((int)Math.Floor(Math.Max(0, t) / Math.Max(.001, entry.Interval))) is not { } frame) continue;
                // Crop the source to the tile's shape so frames never stretch.
                var size = frame.Size; double tileAspect = tileWidth / bounds.Height, w = size.Width, h = size.Height;
                Rect source = w / h > tileAspect ? new Rect((w - h * tileAspect) / 2, 0, h * tileAspect, h) : new Rect(0, (h - w / tileAspect) / 2, w, w / tileAspect);
                context.DrawImage(frame, source, new Rect(x, 0, Math.Min(tileWidth, bounds.Width - x) + .5, bounds.Height));
            }
        // A hairline between tiles, like a film strip.
        var line = new Pen(new SolidColorBrush(Color.Parse("#66000000")), 1);
        for (double x = tileWidth; x < bounds.Width; x += tileWidth) context.DrawLine(line, new Point(Math.Round(x) + .5, 0), new Point(Math.Round(x) + .5, bounds.Height));
    }
}

/// <summary>Peak waveform, mirrored around the centre line and scaled by the clip and track gain.</summary>
internal sealed class Waveform : Control
{
    public MediaPreviews.Entry? Entry; public double SourceSeconds, SecondsPerPixel, SpanSeconds, Gain = 1; public bool Reverse; public IBrush Fill = Brushes.Gray;
    public override void Render(DrawingContext context)
    {
        var bounds = Bounds; if (Entry?.Peaks is not { Length: > 0 } peaks || bounds.Width < 1) return;
        double mid = bounds.Height / 2, step = Math.Max(2, bounds.Width / 3000);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
            for (double x = 0; x < bounds.Width; x += step)
            {
                double a = x * SecondsPerPixel, b = (x + step) * SecondsPerPixel;
                double from = Reverse ? SourceSeconds + SpanSeconds - b : SourceSeconds + a, to = Reverse ? SourceSeconds + SpanSeconds - a : SourceSeconds + b;
                int i0 = Math.Max(0, (int)(from * MediaPreviews.PeaksPerSecond)), i1 = Math.Min(peaks.Length - 1, (int)Math.Ceiling(to * MediaPreviews.PeaksPerSecond));
                int peak = 0; for (int i = i0; i <= i1; i++) if (peaks[i] > peak) peak = peaks[i];
                // Stored peaks are sqrt-scaled; draw on a -48 dB..0 dB scale so quiet game audio and voice stay readable.
                double amplitude = Math.Pow(peak / 255.0, 2) * Gain, level = amplitude <= 0 ? 0 : Math.Clamp((20 * Math.Log10(amplitude) + 48) / 48, 0, 1);
                double h = Math.Max(.5, level * (mid - 1));
                g.BeginFigure(new Point(x, mid - h), true); g.LineTo(new Point(x + step - .6, mid - h)); g.LineTo(new Point(x + step - .6, mid + h)); g.LineTo(new Point(x, mid + h)); g.EndFigure(true);
            }
        context.DrawGeometry(Fill, null, geometry);
    }
}
