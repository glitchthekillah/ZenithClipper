namespace ZenithClipper.Editing;

/// <summary>
/// Premiere's Time Remapping: "speed" keyframes (as a fraction, 1 = 100%) vary playback speed across a clip, with
/// linear ramps between keyframes. Playback is quantised into constant-speed runs (speeds snap to about 3% steps) so
/// the renderer can play each run with one constant-speed producer; editing and rendering share these exact runs.
/// </summary>
public static class TimeRemap
{
    public const double Min = .1, Max = 20;
    public static bool Has(TimelineClip clip) => clip.Motion.Any(k => k.Parameter == "speed");

    /// <summary>Speed used for clip frame <paramref name="frame"/>, already quantised.</summary>
    public static double SpeedAt(TimelineClip clip, long frame)
    {
        double raw = Math.Clamp(ClipBlend.At(clip, "speed", Math.Max(0, frame)) ?? clip.Speed, Min, Max);
        // Log-spaced steps keep slow and fast ramps equally smooth. Each distinct speed costs the renderer a decoder,
        // so steep or long ramps use coarser steps to stay within about 16 speeds per clip.
        double step = Step(clip);
        return Math.Round(Math.Exp(Math.Round(Math.Log(raw) / step) * step), 6);
    }

    static double Step(TimelineClip clip)
    {
        var keys = clip.Motion.Where(k => k.Parameter == "speed").OrderBy(k => k.Frame).Select(k => Math.Log(Math.Clamp(k.Value, Min, Max))).ToList();
        double travel = 0; for (int i = 1; i < keys.Count; i++) travel += Math.Abs(keys[i] - keys[i - 1]);
        return Math.Max(Math.Log(1.03), travel / 16);
    }

    /// <summary>Source frames consumed by the first <paramref name="frames"/> clip frames (negative = before the start).</summary>
    public static double Consumed(TimelineClip clip, long frames)
    {
        if (frames <= 0) return frames * SpeedAt(clip, 0);
        double total = 0; foreach (var run in Runs(clip, frames)) total += run.Length * run.Speed; return total;
    }

    public readonly record struct Run(long Start, long Length, double Speed, double Source);

    /// <summary>Constant-speed runs covering clip frames [0, length), with the source offset (in sequence frames, from SourceIn) where each starts.</summary>
    public static List<Run> Runs(TimelineClip clip, long? length = null)
    {
        long n = length ?? clip.Duration; var runs = new List<Run>(); double source = 0;
        long i = 0;
        while (i < n)
        {
            double s = SpeedAt(clip, i); long j = i + 1;
            while (j < n && SpeedAt(clip, j) == s) j++;
            runs.Add(new(i, j - i, s, source)); source += (j - i) * s; i = j;
        }
        return runs;
    }
}
