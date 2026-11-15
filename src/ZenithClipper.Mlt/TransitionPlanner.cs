using System.Globalization;
using ZenithClipper.Editing;

namespace ZenithClipper.Mlt;

/// <summary>
/// Turns clip transitions into per-clip render instructions, the way Premiere places transitions on edit points:
/// centered on the cut when both clips have spare media (handles), otherwise ending or starting at the cut, otherwise
/// playing over the incoming clip's own first frames. Dips use a color layer; wipes use a generated luma pattern.
/// </summary>
internal sealed class ClipPlan
{
    public long HeadExt, TailExt;
    public readonly List<(long At, double Value)> Alpha = [];
    public readonly List<(long At, double Dx, double Dy, double Scale)> Motion = [];
    public readonly List<(long At, double Db, bool Smooth)> Volume = [];
    public (string Pattern, long From, long To)? Wipe;
    public readonly List<(long Start, long Duration, string Color, List<(long At, double Value)> Alpha)> Overlays = [];
}

internal static class TransitionPlanner
{
    public static Dictionary<Guid, ClipPlan> Plan(EditProject project, EditSequence seq, Func<long, long> convert, double fps)
    {
        var plans = new Dictionary<Guid, ClipPlan>();
        ClipPlan Of(TimelineClip c) => plans.TryGetValue(c.Id, out var p) ? p : plans[c.Id] = new ClipPlan();
        foreach (var track in seq.Tracks)
        {
            var clips = track.Clips.Where(c => c.Enabled).OrderBy(c => c.Start).ToList();
            bool audio = track.Kind == TrackKind.Audio;
            for (int i = 0; i < clips.Count; i++)
            {
                var x = clips[i];
                var prev = i > 0 && clips[i - 1].End == x.Start ? clips[i - 1] : null;
                // A transition placed on the outgoing side of a cut belongs to the cut.
                var incoming = x.TransitionIn ?? (prev?.TransitionOut is { } carried && prev.TransitionOut is not null ? carried : null);
                if (incoming is not null && Valid(incoming, audio))
                {
                    long d = Math.Max(1, Math.Min(convert(incoming.Duration), convert(x.Duration)));
                    if (prev is null) Edge(Of(x), x, incoming.Kind, d, true, convert, audio);
                    else Cut(Of(prev), prev, Of(x), x, incoming.Kind, d, project, convert, fps, audio);
                }
                var next = i + 1 < clips.Count && clips[i + 1].Start == x.End ? clips[i + 1] : null;
                if (x.TransitionOut is { } outgoing && next is null && Valid(outgoing, audio))
                    Edge(Of(x), x, outgoing.Kind, Math.Max(1, Math.Min(convert(outgoing.Duration), convert(x.Duration))), false, convert, audio);
            }
        }
        return plans;
    }

    static bool Valid(ClipTransition t, bool audio) => TransitionCatalog.Find(t.Kind) is { } def && def.Audio == audio;

    /// <summary>Transition at a clip edge with nothing on the other side: from/to black (or silence).</summary>
    static void Edge(ClipPlan plan, TimelineClip clip, string kind, long d, bool atStart, Func<long, long> convert, bool audio)
    {
        long length = convert(clip.Duration), from = atStart ? 0 : length - d;
        if (audio) { Ramp(plan.Volume, from, d, atStart ? -60 : 0, atStart ? 0 : -60, kind); return; }
        if (kind is "dip_black" or "dip_white")
        {
            var keys = atStart ? new List<(long, double)> { (0, 1), (d - 1, 0) } : [(0, 0), (d - 1, 1)];
            plan.Overlays.Add((convert(clip.Start) + from, d, kind == "dip_white" ? "#ffffffff" : "#ff000000", keys));
            return;
        }
        // Any other video transition at an edge reveals the clip over black, the way Premiere treats a single-sided transition.
        Apply(plan, kind, from, d, atStart, null, 0);
    }

    static void Cut(ClipPlan a, TimelineClip prev, ClipPlan b, TimelineClip next, string kind, long d, EditProject project, Func<long, long> convert, double fps, bool audio)
    {
        long cut = convert(next.Start);
        if (kind is "dip_black" or "dip_white")
        {
            long half = d / 2;
            b.Overlays.Add((cut - half, d, kind == "dip_white" ? "#ffffffff" : "#ff000000", [(0, 0), (half, 1), (d - 1, 0)]));
            return;
        }
        long head = Math.Min(cut, Handle(project, next, true, convert, fps)), tail = Handle(project, prev, false, convert, fps);
        long windowStart;
        if (head >= d - d / 2 && tail >= d / 2) { b.HeadExt = d - d / 2; a.TailExt = d / 2; windowStart = cut - b.HeadExt; }
        else if (head >= d) { b.HeadExt = d; windowStart = cut - d; }
        else if (tail >= d) { a.TailExt = d; windowStart = cut; }
        else windowStart = cut;
        long bRel = windowStart - (cut - b.HeadExt), aRel = windowStart - convert(prev.Start);
        if (audio)
        {
            Ramp(b.Volume, bRel, d, -60, 0, kind);
            // The outgoing clip fades only where the incoming one overlaps it.
            if (a.TailExt > 0 || b.HeadExt > 0) Ramp(a.Volume, aRel, d, 0, -60, kind);
            return;
        }
        Apply(b, kind, bRel, d, true, a, aRel);
    }

    /// <summary>
    /// Fade keys sampled from the real gain curve (converted to dB for MLT's volume filter): constant power uses
    /// sin/cos so a crossfade holds its loudness, constant gain is a straight amplitude line, exponential eases in.
    /// </summary>
    static void Ramp(List<(long, double, bool)> keys, long from, long d, double startDb, double endDb, string kind)
    {
        bool fadeIn = startDb < endDb; const int steps = 10;
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps, x = fadeIn ? t : 1 - t;
            double amp = kind switch { "constant_gain" => x, "exponential_fade" => x * x, _ => Math.Sin(x * Math.PI / 2) };
            keys.Add((from + (long)Math.Round(t * (d - 1)), Math.Max(-60, 20 * Math.Log10(Math.Max(amp, 1e-3))), false));
        }
    }

    /// <summary>Spare source media before (head) or after (tail) a clip, in output frames. Stills and titles never run out.</summary>
    static long Handle(EditProject project, TimelineClip clip, bool head, Func<long, long> convert, double fps)
    {
        var media = project.Media.First(m => m.Id == clip.MediaId);
        if (media.Kind is MediaKind.Image or MediaKind.Text or MediaKind.Color or MediaKind.Adjustment || clip.HoldFrame is not null) return long.MaxValue / 4;
        if (clip.Reverse || TimeRemap.Has(clip)) return 0;
        double first = convert(clip.SourceIn) / clip.Speed, total = media.DurationSeconds * fps / clip.Speed;
        return Math.Max(0, (long)Math.Floor(head ? first : total - first - convert(clip.Duration)));
    }

    static void Apply(ClipPlan b, string kind, long from, long d, bool reveal, ClipPlan? a, long aFrom)
    {
        long end = from + d - 1;
        (double Dx, double Dy) Dir(string k) => k.EndsWith("left") ? (1, 0) : k.EndsWith("right") ? (-1, 0) : k.EndsWith("up") ? (0, 1) : (0, -1);
        switch (kind)
        {
            case "cross_dissolve" or "additive_dissolve" or "film_dissolve":
                b.Alpha.Add((from, reveal ? 0 : 1)); b.Alpha.Add((end, reveal ? 1 : 0)); break;
            case var k when k.StartsWith("slide_"):
            {
                var (dx, dy) = Dir(k);
                if (reveal) { b.Motion.Add((from, dx, dy, 1)); b.Motion.Add((end, 0, 0, 1)); } else { b.Motion.Add((from, 0, 0, 1)); b.Motion.Add((end, -dx, -dy, 1)); }
                break;
            }
            case var k when k.StartsWith("push_"):
            {
                var (dx, dy) = Dir(k);
                if (reveal) { b.Motion.Add((from, dx, dy, 1)); b.Motion.Add((end, 0, 0, 1)); } else { b.Motion.Add((from, 0, 0, 1)); b.Motion.Add((end, -dx, -dy, 1)); }
                if (a is not null) { a.Motion.Add((aFrom, 0, 0, 1)); a.Motion.Add((aFrom + d - 1, -dx, -dy, 1)); }
                break;
            }
            case "zoom_in" or "zoom_out":
            {
                double small = kind == "zoom_in" ? .05 : 2.5;
                if (reveal) { b.Motion.Add((from, 0, 0, small)); b.Motion.Add((end, 0, 0, 1)); } else { b.Motion.Add((from, 0, 0, 1)); b.Motion.Add((end, 0, 0, small)); }
                b.Alpha.Add((from, reveal ? 0 : 1)); b.Alpha.Add((end, reveal ? 1 : 0));
                break;
            }
            default: // pattern wipes and split
                b.Wipe = (kind == "split" ? "barn_doors" : kind, reveal ? from : end, reveal ? end : from);
                break;
        }
    }

    /// <summary>Writes (once) and returns a grayscale luma pattern: dark pixels are revealed first.</summary>
    public static string Pattern(string kind)
    {
        string dir = Path.Combine(Path.GetTempPath(), "zenithclipper-lumas"); Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, kind + ".pgm");
        if (File.Exists(path)) return path;
        const int w = 480, h = 270; var data = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = x / (w - 1.0), v = y / (h - 1.0), cx = u - .5, cy = (v - .5) * h / w;
                double value = kind switch
                {
                    "wipe_left" => u, "wipe_right" => 1 - u, "wipe_down" => v, "wipe_up" => 1 - v,
                    "wipe_diagonal" => (u + v) / 2,
                    "barn_doors" => Math.Abs(u - .5) * 2,
                    "iris_round" => Math.Min(1, Math.Sqrt(cx * cx + cy * cy) / .58),
                    "iris_box" => Math.Max(Math.Abs(u - .5), Math.Abs(v - .5)) * 2,
                    "iris_diamond" => (Math.Abs(u - .5) + Math.Abs(v - .5)),
                    "clock_wipe" => (Math.Atan2(cx, -cy) + Math.PI) / (2 * Math.PI),
                    "radial_wipe" => Math.Atan2(v, u) / (Math.PI / 2),
                    "venetian_blinds" => (v * 8) % 1,
                    "checker_wipe" => ((int)(u * 8) + (int)(v * 6)) % 2 == 0 ? u * .5 : .5 + u * .5,
                    _ => u
                };
                data[y * w + x] = (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
            }
        using (var file = File.Create(path + ".tmp")) { var header = System.Text.Encoding.ASCII.GetBytes($"P5 {w} {h} 255\n"); file.Write(header); file.Write(data); }
        File.Move(path + ".tmp", path, true);
        return path;
    }

    public static string Keys(IEnumerable<(long At, double Value)> keys, Func<double, string> format) =>
        string.Join(';', keys.OrderBy(k => k.At).Select(k => $"{Math.Max(0, k.At).ToString(CultureInfo.InvariantCulture)}={format(k.Value)}"));
}
