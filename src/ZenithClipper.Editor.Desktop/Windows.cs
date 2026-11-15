using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Several projects open at once, each in its own window (Premiere's multiple open projects).</summary>
public sealed partial class EditorWindow
{
    static readonly List<EditorWindow> openWindows = [];
    static int windowCounter;
    int windowNumber;
    static Guid clipboardProject; static Dictionary<Guid, (int Index, TrackKind Kind)> clipboardTracks = []; static List<MediaAsset> clipboardMedia = [];

    void RegisterWindow()
    {
        windowNumber = ++windowCounter; openWindows.Add(this);
        Closed += (_, _) => { openWindows.Remove(this); MediaPreviews.Updated -= UpdateLibrary; };
    }
    string UntitledName => windowNumber > 1 ? $"Untitled-{windowNumber}.zenith" : "Untitled.zenith";

    /// <summary>A fresh editor window with an empty project, offset from this one.</summary>
    internal EditorWindow NewWindow(string? project = null)
    {
        var w = new EditorWindow(project is null ? [] : [project]) { Width = Width, Height = Height, WindowStartupLocation = WindowStartupLocation.Manual, Position = new Avalonia.PixelPoint(Position.X + 32, Position.Y + 32) };
        if (!HeadlessVerification) w.Show();
        return w;
    }
    async Task OpenInNewWindow()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Open project in a new window", FileTypeFilter = [new("Zenith project") { Patterns = ["*.zenith", "*.json"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        if (openWindows.FirstOrDefault(w => w.projectPath is { } open && string.Equals(Path.GetFullPath(open), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) is { } already)
        { already.Activate(); status.Text = "That project is already open."; return; }
        NewWindow(path);
    }
    /// <summary>Window → Open projects: every open project window; click one to bring it forward.</summary>
    MenuItem ProjectsMenu()
    {
        var menu = new MenuItem { Header = "Open projects" };
        void Fill()
        {
            menu.Items.Clear();
            foreach (var w in openWindows)
            {
                var target = w; string name = target.projectPath is { } path ? Path.GetFileNameWithoutExtension(path) : Path.GetFileNameWithoutExtension(target.UntitledName);
                var item = new MenuItem { Header = (target == this ? "• " : "") + name }; item.Click += (_, _) => target.Activate(); menu.Items.Add(item);
            }
        }
        Fill(); menu.SubmenuOpened += (_, _) => Fill();
        return menu;
    }

    void RememberClipboardSource()
    {
        clipboardProject = Project.Id;
        var tracks = Sequence.Tracks;
        clipboardTracks = tracks.ToDictionary(x => x.Id, x => (tracks.Where(t => t.Kind == x.Kind).ToList().IndexOf(x), x.Kind));
        var used = clipboard.Select(c => c.Clip.MediaId).ToHashSet();
        clipboardMedia = Project.Media.Where(m => used.Contains(m.Id)).Select(ProjectValidation.Clone).ToList();
    }
    /// <summary>The clipboard ready to paste into <paramref name="p"/>: same-project clips as they are; clips from another
    /// project bring their media along and land on the same-numbered track of the same kind.</summary>
    static List<(Guid TrackId, TimelineClip Clip)> ClipboardFor(EditProject p, out string? note)
    {
        note = null;
        if (p.Id == clipboardProject) return clipboard.ToList();
        var seq = p.Sequences.First(s => s.Id == p.ActiveSequenceId); var board = new List<(Guid, TimelineClip)>(); int skipped = 0, missingTrack = 0;
        foreach (var (trackId, clip) in clipboard)
        {
            var media = clipboardMedia.FirstOrDefault(m => m.Id == clip.MediaId);
            if (media is null || media.Kind == MediaKind.Sequence) { skipped++; continue; }
            if (!clipboardTracks.TryGetValue(trackId, out var place)) { missingTrack++; continue; }
            var sameKind = seq.Tracks.Where(t => t.Kind == place.Kind).ToList();
            if (sameKind.Count == 0) { missingTrack++; continue; }
            var target = sameKind[Math.Clamp(place.Index, 0, sameKind.Count - 1)];
            if (p.Media.All(m => m.Id != media.Id)) p.Media.Add(ProjectValidation.Clone(media));
            board.Add((target.Id, ProjectValidation.Clone(clip)));
        }
        if (skipped + missingTrack > 0) note = $"Pasted from another project. Skipped {skipped + missingTrack} clip(s) that are nested sequences or have no matching track.";
        else if (board.Count > 0) note = "Pasted from another project; its media was added to this project.";
        return board;
    }
}

public sealed partial class EditorWindow
{
    internal void VerifyWindowsAndAnchor()
    {
        var t = new ClipTransform { ScaleX = .5, ScaleY = .5, AnchorX = 0, AnchorY = 0 };
        var g = TransformGeometry.Place(1920, 1080, 0, 0, .5, .5, 90, t);
        if (Math.Abs(g.Left - 210) > 1e-6 || Math.Abs(g.Top - 750) > 1e-6 || g.AnchorX != 960) throw new InvalidOperationException($"Anchor rotation placed the clip at {g.Left},{g.Top}.");
        var d = TransformGeometry.Place(1920, 1080, .1, 0, .5, .5, 0, new ClipTransform());
        if (Math.Abs(d.Left - (480 + 192)) > 1e-6) throw new InvalidOperationException("Default anchor moved the clip.");
        // Cross-project paste brings media along and lands on the same-numbered track.
        var keep = session; var keepSel = selectedClip;
        try
        {
            var media = new MediaAsset { Name = "a", Path = "/tmp/a.mp4", Kind = MediaKind.Video, Width = 1920, Height = 1080, DurationSeconds = 10 };
            var src = new EditSequence { Tracks = [new EditTrack { Kind = TrackKind.Video }, new EditTrack { Kind = TrackKind.Video }] };
            var clip = new TimelineClip { MediaId = media.Id, Duration = 30, StreamIntent = ClipStreamIntent.VideoOnly };
            src.Tracks[1].Clips.Add(clip);
            session = new EditorSession(new EditProject { ActiveSequenceId = src.Id, Sequences = [src], Media = [media] });
            selectedClip = clip.Id; CopySelection(false);
            var dst = new EditSequence { Tracks = [new EditTrack { Kind = TrackKind.Video }, new EditTrack { Kind = TrackKind.Video }, new EditTrack { Kind = TrackKind.Audio }] };
            var other = new EditProject { ActiveSequenceId = dst.Id, Sequences = [dst] };
            var board = ClipboardFor(other, out var note);
            if (board.Count != 1 || board[0].TrackId != dst.Tracks[1].Id || other.Media.Count != 1 || note is null) throw new InvalidOperationException("Cross-project paste did not map track and media.");
            var made = EditCommands.Paste(other, dst.Id, board, 0, false);
            if (made.Count != 1 || dst.Tracks[1].Clips.Count != 1) throw new InvalidOperationException("Cross-project paste did not land.");
        }
        finally { session = keep; selectedClip = keepSel; clipboard.Clear(); }
        if (Environment.GetEnvironmentVariable("ZENITH_PSD_TEST") is { Length: > 0 } psd)
        {
            var before = session;
            try
            {
                session = new EditorSession(ProjectCopy);
                var ids = ImportPsdLayers(psd); var seq = Sequence;
                if (ids.Count != 3 || seq.Tracks.Count(t => t.Kind == TrackKind.Video) != 3 || !seq.Tracks[2].Hidden || Math.Abs(seq.Tracks[1].Clips[0].Opacity - 128 / 255.0) > 1e-9) throw new InvalidOperationException("Photoshop layers did not import as a sequence.");
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "zc-psd.mlt"), ZenithClipper.Mlt.MltXml.Compile(Project).ToString());
                Console.WriteLine("Photoshop layer import smoke passed.");
            }
            finally { session = before; }
        }
        SetMonitorZoom(1); if (monitorViewport is null || monitorViewport.Width != Sequence.Width) throw new InvalidOperationException("Monitor zoom did not size the viewport.");
        SetMonitorZoom(0); if (!double.IsNaN(monitorViewport.Width)) throw new InvalidOperationException("Monitor did not return to Fit.");
    }
}

public sealed partial class EditorWindow
{
    internal static void VerifyArrange()
    {
        var seq = new EditSequence { Width = 1000, Height = 500, Tracks = [new EditTrack { Kind = TrackKind.Video }] };
        TimelineClip C(double x, double s) => new() { MediaId = Guid.NewGuid(), Duration = 10, Transform = new ClipTransform { X = x, ScaleX = s, ScaleY = s } };
        var a = C(-.3, .2); var b = C(0, .1); var c = C(.35, .2); seq.Tracks[0].Clips.AddRange([a, b, c]);
        var p = new EditProject { Sequences = [seq], ActiveSequenceId = seq.Id };
        ClipAlign.Apply(p, seq.Id, [a.Id, b.Id, c.Id], "left", false);
        double La = 500 - 100 + a.Transform.X * 1000, Lb = 500 - 50 + b.Transform.X * 1000;
        if (Math.Abs(La - Lb) > 1e-6 || Math.Abs(La - 100) > 1e-6) throw new InvalidOperationException($"Align left gave {La}, {Lb}.");
        a.Transform.X = -.3; b.Transform.X = .3; c.Transform.X = .35;
        ClipAlign.Apply(p, seq.Id, [a.Id, b.Id, c.Id], "distribute_h", false);
        if (Math.Abs(b.Transform.X - .025) > 1e-9) throw new InvalidOperationException("Distribute did not centre the middle clip.");
        ClipAlign.Apply(p, seq.Id, [b.Id], "bottom", false);
        if (Math.Abs(500 / 2 + b.Transform.Y * 500 + 25 - 500) > 1e-6) throw new InvalidOperationException("Single-clip align did not use the frame.");
    }
}
