using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZenithClipper.Editing;

/// <summary>Render In to Out: a range of the sequence's picture rendered to a file and played from it, like Premiere's
/// preview files. Any edit that touches the picture in that range changes its key, so the old file stops being used.</summary>
public static class PreviewRenders
{
    /// <summary>A fingerprint of everything that draws the picture in [from, to).</summary>
    public static string Key(EditProject p, EditSequence s, long from, long to)
    {
        var mediaIds = new HashSet<Guid>(); var nested = new List<EditSequence>();
        var tracks = s.Tracks.Where(t => t.Kind == TrackKind.Video).Select(t => new
        {
            t.Hidden,
            Clips = t.Clips.Where(c => c.Enabled && c.Start < to && c.End > from).Select(c => { mediaIds.Add(c.MediaId); return c; }).ToList(),
        }).ToList();
        void Nest(Guid mediaId, int depth)
        {
            if (depth > 8 || p.Media.FirstOrDefault(m => m.Id == mediaId) is not { Kind: MediaKind.Sequence, SequenceId: { } sid } || p.Sequences.FirstOrDefault(x => x.Id == sid) is not { } inner || nested.Contains(inner)) return;
            nested.Add(inner); foreach (var c in inner.Tracks.SelectMany(t => t.Clips)) { mediaIds.Add(c.MediaId); Nest(c.MediaId, depth + 1); }
        }
        foreach (var id in mediaIds.ToList()) Nest(id, 0);
        var media = p.Media.Where(m => mediaIds.Contains(m.Id)).Select(m => new { m, Stamp = File.Exists(m.Path) ? File.GetLastWriteTimeUtc(m.Path).Ticks : 0 }).ToList();
        var json = JsonSerializer.Serialize(new { v = 1, s.Width, s.Height, s.Fps, from, to, tracks, media, nested = nested.Select(n => new { n.Id, n.Width, n.Height, n.Fps, n.Tracks }) });
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(json)))[..20];
    }
    public static IEnumerable<PreviewRender> Valid(EditProject p, EditSequence s) =>
        s.Previews.Where(r => r.To > r.From && File.Exists(r.Path) && Key(p, s, r.From, r.To) == r.Key);

    /// <summary>Records a finished render, replacing any it overlaps.</summary>
    public static void Add(EditSequence s, PreviewRender render)
    {
        s.Previews.RemoveAll(r => r.From < render.To && r.To > render.From); s.Previews.Add(render); s.Previews.Sort((a, b) => a.From.CompareTo(b.From));
    }

    /// <summary>For playback only: in every still-valid rendered range, the sequence's own picture is switched off
    /// (audio kept) and the rendered file plays on a new top track instead.</summary>
    public static int Substitute(EditProject p, Guid sequenceId)
    {
        var s = p.Sequences.First(x => x.Id == sequenceId); var valid = Valid(p, s).ToList(); if (valid.Count == 0) return 0;
        var top = new EditTrack { Name = "Rendered previews", Kind = TrackKind.Video };
        foreach (var r in valid)
        {
            foreach (var track in s.Tracks.Where(t => t.Kind == TrackKind.Video).ToList())
            {
                foreach (var c in track.Clips.Where(c => c.Start < r.From && c.End > r.From).ToList()) EditCommands.SplitSingle(p, s.Id, track.Id, c.Id, r.From);
                foreach (var c in track.Clips.Where(c => c.Start < r.To && c.End > r.To).ToList()) EditCommands.SplitSingle(p, s.Id, track.Id, c.Id, r.To);
                foreach (var c in track.Clips.Where(c => c.Start >= r.From && c.End <= r.To).ToList())
                {
                    var media = p.Media.FirstOrDefault(m => m.Id == c.MediaId);
                    // Picture-only pieces go; pieces that also carry sound keep playing it.
                    if (c.StreamIntent == ClipStreamIntent.VideoOnly || media is null || media.Kind == MediaKind.Adjustment || !media.HasAudio) track.Clips.Remove(c);
                    else { c.StreamIntent = ClipStreamIntent.AudioOnly; c.Mask = null; }
                }
            }
            var asset = new MediaAsset { Name = "Rendered preview", Kind = MediaKind.Video, Path = r.Path, Width = s.Width, Height = s.Height, Fps = s.Fps, DurationSeconds = s.Fps.Seconds(r.To - r.From), HasAudio = false };
            p.Media.Add(asset);
            top.Clips.Add(new TimelineClip { MediaId = asset.Id, Name = "Rendered", Start = r.From, Duration = r.To - r.From, StreamIntent = ClipStreamIntent.VideoOnly });
        }
        int lastVideo = s.Tracks.FindLastIndex(t => t.Kind == TrackKind.Video);
        s.Tracks.Insert(lastVideo + 1, top);
        return valid.Count;
    }
}
