namespace ZenithClipper.Editing;

/// <summary>Smart rendering for the common clipping case: when the exported range is one untouched clip (and its own sound),
/// the file can be cut by copying the original stream instead of re-encoding: instant and lossless.</summary>
public static class SmartRender
{
    public sealed record Plan(MediaAsset Media, double StartSeconds, double EndSeconds, bool Audio);

    /// <summary>A copy plan for [from, to) of the active sequence, or null with the reason it has to be re-encoded.</summary>
    public static Plan? Check(EditProject p, long from, long to, out string reason)
    {
        var s = p.Sequences.First(x => x.Id == p.ActiveSequenceId);
        var live = s.Tracks.Where(t => !t.Hidden && !(t.Kind == TrackKind.Audio && t.Muted))
            .SelectMany(t => t.Clips.Where(c => c.Enabled && c.Start < to && c.End > from).Select(c => (Track: t, Clip: c))).ToList();
        var video = live.Where(x => x.Track.Kind == TrackKind.Video).ToList();
        reason = "";
        if (video.Count != 1) { reason = video.Count == 0 ? "there is no video in the range" : "more than one clip is visible in the range"; return null; }
        var (vt, v) = video[0]; var media = p.Media.FirstOrDefault(m => m.Id == v.MediaId);
        if (media is not { Kind: MediaKind.Video } || !File.Exists(media.Path)) { reason = "the clip isn't a video file"; return null; }
        if (v.Start > from || v.End < to) { reason = "the range includes empty space"; return null; }
        var t = v.Transform;
        bool untouched = Math.Abs(v.Speed - 1) < 1e-9 && !v.Reverse && v.HoldFrame is null && v.Motion.Count == 0 && v.Effects.All(e => !e.Enabled) && v.Mask is null
            && Math.Abs(v.Opacity - 1) < 1e-9 && (v.BlendMode is null or "normal") && v.TransitionIn is null && v.TransitionOut is null && v.MulticamAngle is null
            && t.X == 0 && t.Y == 0 && Math.Abs(t.ScaleX - 1) < 1e-9 && Math.Abs(t.ScaleY - 1) < 1e-9 && t.Rotation == 0 && t.CropLeft == 0 && t.CropRight == 0 && t.CropTop == 0 && t.CropBottom == 0;
        if (!untouched) { reason = "the clip has effects, motion, speed or transition changes"; return null; }
        if (media.Width != s.Width || media.Height != s.Height || media.Fps != s.Fps || media.InterpretFps is not null || media.FieldOrder is not null || media.AlphaMode is not null)
        { reason = "the clip's size or frame rate differs from the sequence"; return null; }
        // Sound: none, the clip's own sound (on the video clip or its linked audio clips), untouched.
        var audio = live.Where(x => x.Track.Kind == TrackKind.Audio).ToList();
        bool videoCarriesSound = v.StreamIntent != ClipStreamIntent.VideoOnly && media.HasAudio && !audio.Any(a => a.Clip.LinkId == v.LinkId && v.LinkId is not null);
        foreach (var (at, a) in audio)
        {
            if (a.MediaId != v.MediaId || a.Start != v.Start || a.SourceIn != v.SourceIn || a.Duration != v.Duration || Math.Abs(a.Speed - 1) > 1e-9 || a.Reverse) { reason = "other sound plays in the range"; return null; }
            if (Math.Abs(a.GainDb) > 1e-9 || a.Effects.Any(e => e.Enabled) || a.Motion.Count > 0 || a.TransitionIn is not null || a.TransitionOut is not null || Math.Abs(at.GainDb) > 1e-9 || Math.Abs(at.Pan) > 1e-9)
            { reason = "the sound has gain, effects or fades"; return null; }
        }
        if (audio.Count > 1) { reason = "the sound is layered"; return null; }
        if (videoCarriesSound && Math.Abs(vt.GainDb) > 1e-9) { reason = "the sound has gain"; return null; }
        double fps = s.Fps.Value;
        return new Plan(media, (v.SourceIn + (from - v.Start)) / fps, (v.SourceIn + (to - v.Start)) / fps, audio.Count == 1 || videoCarriesSound);
    }
}
