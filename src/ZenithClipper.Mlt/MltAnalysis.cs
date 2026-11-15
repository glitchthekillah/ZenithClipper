using System.Globalization;
using System.Runtime.InteropServices;
using ZenithClipper.Editing;

namespace ZenithClipper.Mlt;

/// <summary>Analysis passes that need extra MLT modules. Run them in a helper process (ZENITHCLIPPER_MLT_OPENCV=1) so the
/// editor never loads OpenCV.</summary>
public static class MltAnalysis
{
    /// <summary>Follows the pixel rectangle <paramref name="rect"/> (in a <paramref name="width"/>×<paramref name="height"/> frame)
    /// from source frame <paramref name="sourceIn"/> for <paramref name="frames"/> frames with OpenCV's KCF tracker. Returns the
    /// tracked rectangle per frame, frames counted from <paramref name="sourceIn"/>.</summary>
    public static List<(long Frame, double X, double Y, double W, double H)> TrackRect(string path, int width, int height, FrameRate fps, long sourceIn, long frames, (int X, int Y, int W, int H) rect, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Native.Initialize();
        nint profile = Native.Profile(MltXml.ProfileText(width, height, fps)), producer = 0, filter = 0;
        try
        {
            producer = Native.Producer(profile, "avformat", path); if (producer == 0) throw new IOException("MLT could not open the clip for tracking.");
            Native.SetInOut(producer, checked((int)sourceIn), checked((int)(sourceIn + frames - 1)));
            filter = Native.Filter(profile, "opencv.tracker", 0); if (filter == 0) throw new InvalidOperationException("Motion tracking isn't available in this MLT runtime (opencv module missing).");
            var fp = Native.FilterProperties(filter);
            Native.Set(fp, "rect", FormattableString.Invariant($"{rect.X} {rect.Y} {rect.W} {rect.H}"));
            Native.Set(fp, "algo", "KCF"); Native.Set(fp, "steps", "1"); Native.Set(fp, "shape_width", "1"); // the tracker skips analysis entirely when it draws nothing
            if (Native.Attach(Native.ProducerService(producer), filter) != 0) throw new InvalidOperationException("Could not attach the tracker.");
            for (int i = 0; i < frames; i++)
            {
                ct.ThrowIfCancellationRequested();
                Native.Seek(producer, i);
                if (Native.GetFrame(Native.ProducerService(producer), out nint frame, 0) != 0 || frame == 0) break;
                try { int format = 2, w = width, h = height; Native.Image(frame, out _, ref format, ref w, ref h, 0); }
                finally { Native.FrameClose(frame); }
                progress?.Report((i + 1) / (double)frames);
            }
            string results = Marshal.PtrToStringUTF8(Native.Get(fp, "results")) ?? "";
            return Parse(results);
        }
        finally
        {
            if (filter != 0) Native.FilterClose(filter); if (producer != 0) Native.ProducerClose(producer); if (profile != 0) Native.ProfileClose(profile);
        }
    }
    /// <summary>MLT rect animation ("0=x y w h 1;1=…", frames possibly as timecodes) to per-frame rectangles.</summary>
    public static List<(long Frame, double X, double Y, double W, double H)> Parse(string animation)
    {
        var list = new List<(long, double, double, double, double)>();
        foreach (var part in animation.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('='); if (eq <= 0) continue;
            string key = part[..eq].TrimEnd('~', '|', '$', '-').Trim();
            if (!long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out long f)) continue;
            var v = part[(eq + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x.TrimEnd('%'), CultureInfo.InvariantCulture)).ToArray();
            if (v.Length >= 4) list.Add((f, v[0], v[1], v[2], v[3]));
        }
        return list;
    }
}
