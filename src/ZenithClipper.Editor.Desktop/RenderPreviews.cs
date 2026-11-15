using ZenithClipper.Editing;
using ZenithClipper.Mlt;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Sequence → Render In to Out (Premiere's Enter): renders the picture of the marked range (or the whole sequence)
/// to a preview file that plays instead of the live composite until something in that range changes.</summary>
public sealed partial class EditorWindow
{
    static readonly ExportPreset PreviewRenderPreset = new("Preview render", "Internal", "mp4", "All-intra H.264, picture only, for smooth playback.",
        [("f", "mp4"), ("vcodec", "libx264"), ("crf", "16"), ("preset", "ultrafast"), ("g", "1"), ("pix_fmt", "yuv420p"), ("an", "1")]);
    object? previewCacheVersion; List<PreviewRender> previewCache = [];
    /// <summary>Still-valid preview renders of the active sequence, cached per project version (the key hashes the range).</summary>
    internal List<PreviewRender> ValidPreviews()
    {
        if (!ReferenceEquals(previewCacheVersion, session.Version)) { var p = Project; previewCache = PreviewRenders.Valid(p, p.Sequences.First(s => s.Id == p.ActiveSequenceId)).ToList(); previewCacheVersion = session.Version; }
        return previewCache;
    }
    string PreviewDirectory => Path.Combine(AssetDirectory, "Preview Files");

    async Task RenderInToOut()
    {
        var project = ProjectCopy; var seq = Sequence;
        if (seq.Duration < 1) { status.Text = "Add clips before rendering previews."; return; }
        if (exportFinished is not null) { status.Text = "Wait for the current export to finish."; return; }
        long from = Math.Clamp(markIn ?? 0, 0, seq.Duration - 1), to = Math.Clamp(markOut ?? seq.Duration, from + 1, seq.Duration);
        string key = PreviewRenders.Key(project, seq, from, to); Directory.CreateDirectory(PreviewDirectory);
        string file = Path.Combine(PreviewDirectory, key + ".mp4");
        if (!File.Exists(file))
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); exportFinished = finished;
            try { await RunExports([(project, file, new ExportOptions(PreviewRenderPreset, from, to))]); }
            finally { progress.IsVisible = false; cancelExport.IsVisible = false; exportCancellation?.Dispose(); exportCancellation = null; exportFinished = null; finished.TrySetResult(); }
        }
        var sid = seq.Id;
        Change("Render preview", p => PreviewRenders.Add(p.Sequences.First(s => s.Id == sid), new PreviewRender { From = from, To = to, Key = key, Path = file }));
        graphRevision++; Refresh(true);
        status.Text = $"Rendered {Clock(from)} to {Clock(to)}. It plays from the preview file until you change something there.";
    }

    void DeleteRenderFiles()
    {
        var sid = Sequence.Id; var files = Sequence.Previews.Select(r => r.Path).ToList();
        if (files.Count == 0) { status.Text = "No preview files for this sequence."; return; }
        Change("Delete render files", p => p.Sequences.First(s => s.Id == sid).Previews.Clear());
        int removed = 0;
        foreach (var f in files.Distinct()) try { if (File.Exists(f) && Project.Sequences.All(s => s.Previews.All(r => r.Path != f))) { File.Delete(f); removed++; } } catch (IOException) { } catch (UnauthorizedAccessException) { }
        graphRevision++; Refresh(true); status.Text = $"Deleted {removed} preview file{(removed == 1 ? "" : "s")}.";
    }

    internal void VerifyPreviewRenders()
    {
        var media = new MediaAsset { Name = "a", Path = "/tmp/zc-none.mp4", Kind = MediaKind.Video, Width = 1920, Height = 1080, DurationSeconds = 10, HasAudio = true };
        var seq = new EditSequence { Tracks = [new EditTrack { Kind = TrackKind.Video }, new EditTrack { Kind = TrackKind.Audio }] };
        var v = new TimelineClip { MediaId = media.Id, Duration = 100, LinkId = Guid.NewGuid(), StreamIntent = ClipStreamIntent.VideoOnly }; var a = new TimelineClip { MediaId = media.Id, Duration = 100, LinkId = v.LinkId, StreamIntent = ClipStreamIntent.AudioOnly };
        seq.Tracks[0].Clips.Add(v); seq.Tracks[1].Clips.Add(a);
        var p = new EditProject { ActiveSequenceId = seq.Id, Sequences = [seq], Media = [media] };
        string dir = Path.Combine(Path.GetTempPath(), "zc-preview-check"); Directory.CreateDirectory(dir); string file = Path.Combine(dir, "r.mp4"); File.WriteAllText(file, "x");
        string key = PreviewRenders.Key(p, seq, 20, 60); PreviewRenders.Add(seq, new PreviewRender { From = 20, To = 60, Key = key, Path = file });
        var play = ProjectValidation.Clone(p); if (PreviewRenders.Substitute(play, seq.Id) != 1) throw new InvalidOperationException("Preview render was not used.");
        var ps = play.Sequences[0]; var vt = ps.Tracks[0];
        if (vt.Clips.Count != 2 || vt.Clips[0].End != 20 || vt.Clips[1].Start != 60 || ps.Tracks[1].Kind != TrackKind.Video || ps.Tracks[1].Clips.Single().Start != 20 || ps.Tracks[2].Clips.Count != 1)
            throw new InvalidOperationException("Preview render did not replace exactly the rendered range.");
        // An edit inside the range invalidates it; an edit outside doesn't.
        var outside = ProjectValidation.Clone(p); outside.Sequences[0].Tracks[1].Clips[0].GainDb = -6;
        if (!PreviewRenders.Valid(outside, outside.Sequences[0]).Any()) throw new InvalidOperationException("An audio change invalidated a picture render.");
        var inside = ProjectValidation.Clone(p); inside.Sequences[0].Tracks[0].Clips[0].Opacity = .5;
        if (PreviewRenders.Valid(inside, inside.Sequences[0]).Any()) throw new InvalidOperationException("A picture change kept a stale render.");
    }
}
