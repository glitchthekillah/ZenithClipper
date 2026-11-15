namespace ZenithClipper.Editing;

/// <summary>
/// Essential Sound's auto-ducking: music clips get volume keyframes that dip while dialogue plays. Activity comes from
/// the caller (waveform levels), so this stays pure and testable.
/// </summary>
public static class Ducking
{
    /// <param name="activeAt">True when a dialogue clip is audible at timeline frame f.</param>
    /// <returns>Number of music clips changed.</returns>
    public static int Apply(EditSequence seq, Guid musicTrack, Func<long, bool> activeAt, double duckDb = 12, double fadeSeconds = .4, double holdSeconds = .6)
    {
        var track = seq.Tracks.First(t => t.Id == musicTrack);
        if (track.Kind != TrackKind.Audio) throw new InvalidOperationException("Pick an audio track for the music.");
        if (track.Locked) throw new InvalidOperationException($"{track.Name} is locked.");
        long fade = Math.Max(1, seq.Fps.Frames(fadeSeconds)), hold = seq.Fps.Frames(holdSeconds), minimum = seq.Fps.Frames(.15);
        int changed = 0;
        foreach (var clip in track.Clips)
        {
            // Dialogue regions overlapping this clip, with short pauses bridged so the music doesn't pump between words.
            var regions = new List<(long A, long B)>(); long? open = null; long lastActive = long.MinValue;
            for (long f = clip.Start; f < clip.End; f++)
            {
                if (activeAt(f)) { open ??= f; lastActive = f; }
                else if (open is { } a && f - lastActive > hold) { regions.Add((a, lastActive + 1)); open = null; }
            }
            if (open is { } o) regions.Add((o, lastActive + 1));
            regions.RemoveAll(r => r.B - r.A < minimum);
            clip.Motion.RemoveAll(k => k.Parameter == "volume");
            if (regions.Count == 0) continue;
            double up = clip.GainDb, down = Math.Max(-96, clip.GainDb - duckDb);
            var keys = new SortedDictionary<long, double> { [0] = up, [clip.Duration] = up };
            foreach (var (a, b) in regions)
            {
                long ra = a - clip.Start, rb = b - clip.Start;
                // Fade down so the music is already low when the voice starts, and recover after it ends.
                long downStart = Math.Max(0, ra - fade), upEnd = Math.Min(clip.Duration, rb + fade);
                foreach (var k in keys.Keys.Where(k => k > downStart && k < upEnd).ToList()) keys.Remove(k);
                if (downStart > 0 || ra > 0) keys[downStart] = keys.Where(k => k.Key <= downStart).Select(k => k.Value).LastOrDefault(up);
                keys[Math.Max(0, ra)] = down; keys[Math.Min(clip.Duration, rb)] = down;
                if (upEnd < clip.Duration || rb < clip.Duration) keys[upEnd] = up;
            }
            foreach (var (frame, value) in keys) clip.Motion.Add(new Keyframe { Parameter = "volume", Frame = frame, Value = Math.Round(value, 2), Interpolation = "linear" });
            clip.Motion.Sort((x, y) => x.Parameter == y.Parameter ? x.Frame.CompareTo(y.Frame) : string.CompareOrdinal(x.Parameter, y.Parameter));
            changed++;
        }
        return changed;
    }
}
