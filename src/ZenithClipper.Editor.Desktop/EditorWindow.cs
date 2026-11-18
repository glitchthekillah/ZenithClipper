using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ZenithClipper.Editing;
using ZenithClipper.Mlt;
namespace ZenithClipper.Editor.Desktop;

public sealed partial class EditorWindow : Window
{
    internal void VerifyHeadlessLayout()
    {
        Refresh();
        if (headers.Children.Count < 3 || inspector.Children.Count < 1 || timeline.Children.Count == 0)
            throw new InvalidOperationException("The editor did not populate its initial tracks, inspector and timeline.");
        VerifyPreviewMailbox();
        VerifyTimelineEditing();
        PremiereChecks.Run();
        VerifySourceMonitor();
        VerifyTransformOverlay();
        VerifyCaptionTools();
        VerifyShortcuts();
        VerifyWindowsAndAnchor();
        VerifyArrange();
        VerifySpelling();
        VerifyPreviewRenders();
        VerifyFloatingPanels();
        VerifyGuides();
        VerifyKeyframeEasing();
        VerifySmartRender();
        ScopesView.Verify();
        VerifyLongTimeline();
        VerifyRejectedImportFormat();
    }
    internal void VerifyTimelineAlignment()
    {
        var headerOrigin = headers.TranslatePoint(default, this) ?? throw new InvalidOperationException("Track headers are not laid out.");
        var timelineOrigin = timeline.TranslatePoint(default, this) ?? throw new InvalidOperationException("Timeline is not laid out.");
        if (Math.Abs(timelineOrigin.X - headerOrigin.X - TrackHeaderWidth) > .5)
            throw new InvalidOperationException("Timeline frame zero is not aligned with its viewport edge.");
        if (Math.Abs(headerOrigin.Y - timelineOrigin.Y) > .5)
            throw new InvalidOperationException($"Timeline/header origin mismatch: {timelineOrigin.Y} vs {headerOrigin.Y}.");
        for (var index = 0; index < Sequence.Tracks.Count; index++)
        {
            var track = DisplayTracks()[index];
            var row = headers.Children.First(c => Equals(c.Tag, track.Id));
            var origin = row.TranslatePoint(default, this) ?? throw new InvalidOperationException("Track row is not laid out.");
            var expected = timelineOrigin.Y + RowTop(index);
            if (Math.Abs(origin.Y - expected) > .5)
                throw new InvalidOperationException($"Track {index + 1} is misaligned: {origin.Y} vs {expected}.");
        }
        Console.WriteLine($"Timeline layout smoke passed: ruler/header origin {timelineOrigin.Y:0.##}, {Sequence.Tracks.Count} rows aligned.");
    }
    void VerifyLongTimeline()
    {
        var previous = session;
        try
        {
            var project = ProjectCopy;
            var image = new MediaAsset { Kind = MediaKind.Image };
            project.Media.Add(image);
            project.Sequences[0].Tracks[0].Clips.Add(new TimelineClip { MediaId = image.Id, Duration = int.MaxValue - 1L });
            session = new(project);
            timeline.Rebuild();
            if (timeline.Children.Count > 510)
                throw new InvalidOperationException("Long timeline allocated unbounded ruler elements.");
            Console.WriteLine("Long timeline smoke passed: ruler stays bounded at maximum supported frame range.");
        }
        finally { session = previous; timeline.Rebuild(); }
    }
    void VerifyRejectedImportFormat()
    {
        var previousSession = session;
        var previousFormat = formatChosen;
        var previousStatus = status.Text;
        try
        {
            session = new EditorSession(NewProject());
            formatChosen = false;
            var invalid = new MediaAsset { Kind = MediaKind.Video, Width = 65537, Height = 1080, DurationSeconds = 1 };
            try{AddImportedMedia(invalid);}catch(InvalidDataException){}
            if (formatChosen || Project.Media.Any(m => m.Id == invalid.Id))
                throw new InvalidOperationException("A rejected media import changed the sequence-format selection state.");
            var valid = new MediaAsset { Kind = MediaKind.Video, Width = 2560, Height = 1440, Fps = new FrameRate(165, 1), DurationSeconds = 1 };
            AddImportedMedia(valid);
            if (!formatChosen || Sequence.Width != 2560 || Sequence.Height != 1440 || Sequence.Fps != new FrameRate(165, 1))
                throw new InvalidOperationException("A valid video after a rejected import did not adopt its native format.");
            Console.WriteLine("Import regression smoke passed: rejected oversized metadata preserve format selection; next valid 165 fps video is adopted.");
        }
        finally
        {
            session = previousSession;
            formatChosen = previousFormat;
            Refresh();
            status.Text = previousStatus;
        }
    }
    EditorSession session = new(NewProject());
    readonly MltSession engine = new();
    readonly SemaphoreSlim renderGate = new(1, 1);
    readonly Image preview = new() { Stretch = Stretch.Uniform };
    CropOverlay cropOverlay = null!;
    /// <summary>Applies a crop drawn on the monitor (fractions cut from each edge) to the selected clip.</summary>
    internal void ApplyCrop(double left, double right, double top, double bottom)
    {
        if (selectedClip is not { } id) return;
        Change("Crop", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, id, c => c.Transform = new ClipTransform { X = c.Transform.X, Y = c.Transform.Y, ScaleX = c.Transform.ScaleX, ScaleY = c.Transform.ScaleY, Rotation = c.Transform.Rotation, AnchorX = c.Transform.AnchorX, AnchorY = c.Transform.AnchorY, CropLeft = left, CropRight = right, CropTop = top, CropBottom = bottom }));
    }
    TransformOverlay transformOverlay = null!;
    /// <summary>Commits an on-monitor move/scale/rotate. Keyframed properties get a keyframe at the playhead.</summary>
    internal void ApplyMotion(TransformOverlay.Values was, TransformOverlay.Values to)
    {
        if (selectedClip is not { } id) return;
        var clip = Sequence.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == id); if (clip is null) return;
        long rel = Math.Clamp(ProgramPlayhead - clip.Start, 0, clip.Duration);
        var changes = new List<(string Param, double Value, Action<ClipTransform, double> Static)>();
        if (was.X != to.X) changes.Add(("x", to.X, (t, v) => t.X = v));
        if (was.Y != to.Y) changes.Add(("y", to.Y, (t, v) => t.Y = v));
        if (was.ScaleX != to.ScaleX) changes.Add(("scale_x", to.ScaleX, (t, v) => t.ScaleX = v));
        if (was.ScaleY != to.ScaleY) changes.Add(("scale_y", to.ScaleY, (t, v) => t.ScaleY = v));
        if (was.Rotation != to.Rotation) changes.Add(("rotation", to.Rotation, (t, v) => t.Rotation = v));
        if (changes.Count == 0) return;
        Change("Motion", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, id, c =>
        {
            foreach (var (param, value, apply) in changes)
                if (c.Motion.Any(k => k.Parameter == param)) SetKeyframe(c, param, rel, value); else apply(c.Transform, value);
        }));
    }
    readonly TextBlock status = new() { Text = "Import media to begin. Drag clips on the timeline to arrange them." };
    readonly TextBlock time = new();
    readonly ListBox library = new() { MinHeight = 120 };
    readonly StackPanel inspector = new() { Spacing = 7, Margin = new Thickness(12) };
    readonly StackPanel headers = new() { Spacing = 0 };
    readonly ScrollViewer headerScroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden };
    readonly TimelineView timeline;
    readonly Slider zoom = new() { Minimum = 4, Maximum = 1200, Value = 40, Width = 140 };
    readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Width = 140, IsVisible = false };
    Button cancelExport = null!;
    readonly CheckBox snap = new() { Content = "Snap", IsChecked = true };
    readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromSeconds(Prefs.AutosaveSeconds) };
    readonly string[] arguments;
    CancellationTokenSource? exportCancellation;
    string? projectPath;
    Guid? selectedClip, selectedTrack;
    long playhead;
    FrameRate timelineFps = FrameRate.Default;
    bool playing, closingApproved, formatChosen;
    WriteableBitmap? bitmap;
    // One copy of the project per change, not one per read: reads happen hundreds of times per frame (layout, drag, render),
    // and track/clip lookups by identity only work when every read returns the same objects.
    EditProject? projectView; object? projectViewVersion; Guid projectViewSequence;
    /// <summary>A private copy for code that changes the project it reads (previews, exports, scratch sequences).</summary>
    EditProject ProjectCopy => session.Project;
    /// <summary>Shared read view: never modify what this returns; use Change() or ProjectCopy.</summary>
    EditProject Project
    {
        get
        {
            if (projectView is null || !ReferenceEquals(projectViewVersion, session.Version) || projectViewSequence != session.ActiveSequenceId)
            { projectView = session.Project; projectViewVersion = session.Version; projectViewSequence = session.ActiveSequenceId; }
            return projectView;
        }
    }
    EditSequence Sequence => Project.Sequences.First(s => s.Id == Project.ActiveSequenceId);
    static EditProject NewProject()
    {
        var s = new EditSequence { Tracks = [new() { Name = "Video 1", Kind = TrackKind.Video }, new() { Name = "Audio 1", Kind = TrackKind.Audio }] };
        return new()
        {
            Name = "Untitled",
            ActiveSequenceId = s.Id,
            Sequences = [s]
        };
    }
    public EditorWindow(string[] args)
    {
        arguments = args;
        Title = "ZenithClipper Editor";
        if (typeof(EditorWindow).Assembly.GetManifestResourceStream("zenithclipper.png") is { } logo) using (logo) Icon = new WindowIcon(logo);
        Width = 1440;
        Height = 920;
        MinWidth = 1000;
        MinHeight = 650;
        Background = CanvasBrush;
        MediaPreviews.FfmpegPath = FindFfmpeg();
        timeline = new TimelineView(this); HookMediaDrag();
        MediaPreviews.Updated += UpdateLibrary;
        StartMeters();
        RegisterWindow();
        BuildWorkspace();
        KeyDown += OnKey;
        Opened += (_, _) => { Refresh(); Run(OpenArguments); };
        autosave.Tick += (_, _) => { if (session.Dirty) try { ProjectStore.SaveAutosave(RecoveryPath(), Project); } catch (Exception ex) { status.Text = "Autosave: " + ex.Message; } };
        autosave.Start();
        Closing += (_, e) => { if (!closingApproved) { e.Cancel = true; Run(ConfirmClose); } };
        Closed += (_, _) => { editorClosed = true; try { voiceOver?.Kill(true); } catch (InvalidOperationException) { } InvalidatePreview(); autosave.Stop(); exportCancellation?.Cancel(); engine.Dispose(); bitmap?.Dispose(); };
    }
    Avalonia.Controls.Control MenuBar()
    {
        MenuItem Item(string name, Action action)
        {
            var i = new MenuItem { Header = name };
            i.Click += (_, _) => action();
            return i;
        }
        MenuItem Menu(string header, params MenuItem[] items) => new() { Header = header, ItemsSource = items };
        return new Menu { ItemsSource = new[] {
            Menu("File", Item("New project", () => Run(New)), Item("Open project…", () => Run(Open)), Item("New window", () => NewWindow()), Item("Smart export (no re-encode)…", () => Run(SmartExport)), Item("Open in new window…", () => Run(OpenInNewWindow)), Item("Save", () => Run(() => Save(false))), Item("Save as…", () => Run(() => Save(true))), Item("Recover autosave", () => Run(Recover)),
                Item("Import media…", () => Run(Import)), Item("Import image sequence…", () => Run(ImportImageSequence)), Item("Import Premiere / FCP XML…", () => Run(ImportXml)), Item("Export Premiere / FCP XML…", () => Run(ExportXml)), Item("Create proxies", () => Run(CreateProxies)), Item("Toggle proxies", ToggleProxies), Item("Collect files into folder…", () => Run(CollectFiles)),
                Item("Export…  Ctrl+M", () => Run(Export)), Item("Start export queue", () => Run(StartExportQueue)), Item("Clear export queue", () => { exportQueue.Clear(); status.Text = "Export queue cleared."; })),
            Menu("Edit", Item("Undo", () => { Undo(); }), Item("Redo", () => { Redo(); }), Item("Split at playhead", Split), Item("Delete clip", () => Delete(false)), Item("Ripple delete", () => Delete(true)),
                Item("Group  Ctrl+G", GroupSelected), Item("Ungroup  Ctrl+Shift+G", UngroupSelected), Item("Add marker", AddMarker), Item("Unlink video and audio", UnlinkSelected), Item("Relink source video and audio", RelinkSelected),
                Item("Keyboard shortcuts…", () => Run(EditShortcuts)), Item("Preferences…", () => Run(EditPreferences))),
            Menu("Sequence", Item("New sequence", NewSequence), Item("Render In to Out  Enter", () => Run(RenderInToOut)), Item("Delete render files", DeleteRenderFiles), Item("Sequence settings…", () => Run(SequenceSettings)), Item("Nest selected clip", () => Run(NestSelected)), Item("Create multicam from selected clips", CreateMulticam),
                Item("Insert from Source  ,", () => SourceEdit(true)), Item("Overwrite from Source  .", () => SourceEdit(false)), Item("Make subclip from Source In/Out  Ctrl+U", MakeSubclip),
                Item("Scene edit detection (selected clip)", () => Run(SceneEditDetection)), Item("Add video track", () => AddTrack(TrackKind.Video)), Item("Add audio track", () => AddTrack(TrackKind.Audio)),
                Item("Taller tracks  Ctrl+=", () => ResizeTracks(12)), Item("Shorter tracks  Ctrl+-", () => ResizeTracks(-12)), Item("Record voice-over  Shift+V", () => Run(ToggleVoiceOver))),
            Menu("Graphics", Item("Create title…", () => Run(CreateTitle)), Item("Create credit roll…", () => Run(CreateCreditRoll)), Item("Rectangle", () => AddShape("rectangle", "Rectangle")), Item("Rounded rectangle", () => AddShape("rounded", "Rounded Rectangle")), Item("Ellipse", () => AddShape("ellipse", "Ellipse")),
                Menu("Align and distribute", Item("Align left", () => AlignSelection("left")), Item("Align horizontal centre", () => AlignSelection("center")), Item("Align right", () => AlignSelection("right")), Item("Align top", () => AlignSelection("top")), Item("Align vertical centre", () => AlignSelection("middle")), Item("Align bottom", () => AlignSelection("bottom")), Item("Distribute horizontally", () => AlignSelection("distribute_h")), Item("Distribute vertically", () => AlignSelection("distribute_v")), Item("Toggle: align to video frame", () => { alignToFrame = !alignToFrame; status.Text = alignToFrame ? "Align now uses the video frame." : "Align now uses the selected clips."; })), Item("Adjustment layer", AddAdjustmentLayer), Item("Color matte", () => AddGenerated("Color Matte", "#2B5CFF")), Item("Black video", () => AddGenerated("Black Video", "#000000")), Item("Transparent video", () => AddGenerated("Transparent Video", "transparent")), Item("Bars", () => AddGenerated("Bars", "bars")), Item("Gradient", () => AddGenerated("Gradient", "gradient:#1E2A78:#FF6B4A"))),
            Menu("Captions", Item("Generate auto captions", () => Run(GenerateAutoCaptions)), Item("Show / hide captions", ToggleCaptions), Item("Remove auto captions", RemoveAutoCaptions), Item("Import SRT captions…", () => Run(ImportCaptions)), Item("Export SRT captions…", () => Run(ExportCaptions)), Item("Caption style…", () => Run(RestyleCaptions)), Item("Find and replace…", () => Run(FindReplaceText)), Item("Check spelling…", () => Run(CheckSpelling)), Item("Split into single words", SplitCaptionsIntoWords)),
            Menu("Window", Item("Workspace: Editing", () => Workspace("Editing")), Item("Workspace: Color", () => Workspace("Color")), Item("Workspace: Audio", () => Workspace("Audio")), Item("Workspace: Effects", () => Workspace("Effects")), Item("Workspace: Captions", () => Workspace("Captions")),
                Item("History", () => Workspace("History")), ProjectsMenu(), Item("Float Mixer", () => FloatPanel("Mixer")), Item("Float Scopes", () => FloatPanel("Scopes")), Item("Float History", () => FloatPanel("History")), Item("Dock all panels", DockAllPanels), Item("Full screen playback  Ctrl+`", ToggleFullScreen), Item("Program on second display", () => ShowProgramFullScreen(true)), Item("Comparison view", () => ToggleComparison()), Menu("Rulers and guides", Item("Show rulers", ToggleRulers), Item("Show guides", ToggleGuides), Item("Lock guides", ToggleLockGuides), Item("Add vertical guide", () => AddGuide(true)), Item("Add horizontal guide", () => AddGuide(false)), Item("Clear guides", ClearGuides)), Item("Comparison: new reference frame", () => ToggleComparison(true)), Item("Show / hide timecode overlay", () => { timecodeOverlay.IsVisible = !timecodeOverlay.IsVisible; UpdateTime(); }))
        }, Foreground = MutedBrush, FontSize = 13 };
    }
    static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Foreground = SoftTextBrush, Margin = new Thickness(4) };
    Button Button(string text, Action action)
    {
        var b = new Button { Content = text, Margin = new Thickness(0), Padding = new Thickness(10, 6), Height = 30, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, Background = ControlBrush, Foreground = TextBrush, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1) };
        StyleButton(b);
        b.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } };
        return b;
    }
    async void Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException ex) { status.Text = "Cancelled."; Log(ex); }
        catch (Exception ex) { status.Text = ex.Message; Log(ex); }
    }
    /// <summary>Appends failures to %LOCALAPPDATA%\ZenithClipper\editor.log (kept under 1 MB) so problems can be diagnosed after the fact.</summary>
    internal static void Log(Exception ex)
    {
        try
        {
            // Portable installs keep the log beside the editor (build\editor\logs); otherwise %LOCALAPPDATA%\ZenithClipper.
            string portable = Path.Combine(AppContext.BaseDirectory, "logs");
            var path = Path.Combine(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "mlt")) ? portable : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZenithClipper"), "editor.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 1 << 20) File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch (Exception) { }
    }
    void Change(string name, Action<EditProject> action)
    {
        try
        {
            EnsureEditable();
            // Sequence edits act at the program playhead, so the shared monitor returns to Program (except Insert/Overwrite from Source).
            if (sourceMode && !sourceEditing) ShowProgram();
            session.Execute(name, action);
            Refresh(true);
        }
        catch (Exception ex) { status.Text = ex.Message; if (ex is not (ArgumentException or InvalidOperationException)) Log(ex); }
    }
    void Refresh(bool graphChanged = false)
    {
        timelineFps = Sequence.Fps;
        if (graphChanged)
        {
            graphRevision++;
            InvalidatePreview();
        }
        Title = $"{Project.Name}{(session.Dirty ? " *" : "")} — ZenithClipper Editor";
        UpdateChrome();
        if (selectedTrack is null || !Sequence.Tracks.Any(t => t.Id == selectedTrack))
            selectedTrack = Sequence.Tracks.FirstOrDefault()?.Id;
        BuildTrackHeaders();
        UpdateSequenceTabs();
        timeline.Rebuild();
        BuildInspector();
        UpdateTime();
        Run(async () => { if (graphChanged && playing) { playing = false; UpdateTransportState(); await engine.PauseAsync(); } await Render(); });
    }
    void UpdateTime() { time.Text = Clock(playhead); if (timecodeOverlay.IsVisible) timecodeOverlay.Text = (sourceMode ? "SRC " : "") + Clock(playhead); ToolTip.SetTip(time,$"Frame {playhead} · {timelineFps.Seconds(playhead):0.000} seconds"); UpdateTransportState(); }
    /// <summary>Adds the selected library item at the playhead, or after everything already on the timeline (double-click),
    /// overwriting whatever it lands on instead of stacking clips on top of each other.</summary>
    void AppendSelected(bool atEnd = false)
    {
        if(library.SelectedItem is not MediaRow row)return;var media=Project.Media.Single(m=>m.Id==row.Id);
        long start=atEnd?Sequence.Tracks.SelectMany(t=>t.Clips).Select(c=>c.Start+c.Duration).DefaultIfEmpty(0).Max():ProgramPlayhead;
        Change("Add "+media.Name,p=>
        {
            var result=EditCommands.AppendMedia(p,p.ActiveSequenceId,media.Id,start,selectedTrack);
            EditCommands.MoveOverwrite(p,p.ActiveSequenceId,result.SelectedClipId,start,new Dictionary<Guid,Guid>());
            selectedClip=result.SelectedClipId;selectedTrack=result.SelectedTrackId;
            if(media.Kind==MediaKind.Text){var clip=p.Sequences.Single(s=>s.Id==p.ActiveSequenceId).Tracks.SelectMany(t=>t.Clips).Single(c=>c.Id==result.SelectedClipId);clip.Transform=TextClip(media,Sequence,start,clip.Duration,false).Transform;}
        });
    }
    // Grouped like Photoshop's import dialog. Anything FFmpeg can read still imports through "All files".
    static readonly string[] VideoExtensions = ["mp4", "mov", "mkv", "webm", "avi", "m4v", "wmv", "flv", "ts", "mts", "m2ts", "mpg", "mpeg", "3gp", "ogv"];
    static readonly string[] AudioExtensions = ["mp3", "wav", "aac", "m4a", "flac", "ogg", "opus", "wma", "aif", "aiff"];
    static readonly string[] ImageExtensions = ["png", "jpg", "jpeg", "gif", "webp", "bmp", "tif", "tiff", "psd", "heic", "avif", "svg"];
    static FilePickerFileType Types(string name, IEnumerable<string> exts) => new(name) { Patterns = exts.Select(e => "*." + e).ToList() };
    static readonly FilePickerFileType[] ImportTypes =
    [
        Types("All media", VideoExtensions.Concat(AudioExtensions).Concat(ImageExtensions)), Types("Video", VideoExtensions),
        Types("Audio", AudioExtensions), Types("Images and GIFs", ImageExtensions), new("All files") { Patterns = ["*"] },
    ];
    void AddTrack(TrackKind kind) => Change("Add track", p => { var s = p.Sequences.First(s => s.Id == p.ActiveSequenceId); var t = new EditTrack { Kind = kind, Name = $"{kind} {s.Tracks.Count(t => t.Kind == kind) + 1}" }; s.Tracks.Add(t); selectedTrack = t.Id; });
    /// <summary>Ctrl+K: cuts the selected clip at the playhead; if the playhead isn't over it (or nothing is selected),
    /// cuts every clip under the playhead instead of silently doing nothing.</summary>
    void Split()
    {
        long at = playhead;
        var selected = Sequence.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == selectedClip);
        if (selectedTrack is { } t && selected is { } c && at > c.Start && at < c.End)
            Change("Split clip", p => EditCommands.Split(p, p.ActiveSequenceId, t, c.Id, at));
        else if (Sequence.Tracks.SelectMany(t => t.Clips).Any(x => at > x.Start && at < x.End))
            Change("Add edit", p => EditCommands.AddEditAll(p, p.ActiveSequenceId, at));
        else status.Text = "Move the playhead over a clip to cut it.";
    }
    void Delete(bool ripple)
    {
        if (selectedClip is null) return;
        var ids = SelectedClips.ToList();
        Change(ripple ? "Ripple delete" : ids.Count > 1 ? "Delete clips" : "Delete clip", p => EditCommands.DeleteMany(p, p.ActiveSequenceId, ids, ripple));
        selectedClip = null; extraSelection.Clear(); Refresh();
    }
    void AddMarker() => Change("Add marker", p => p.Sequences.First(s => s.Id == p.ActiveSequenceId).Markers.Add(new() { Frame = playhead, Name = $"Marker {Sequence.Markers.Count + 1}" }));
    async Task Import()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Import video, audio or images",AllowMultiple=true,FileTypeFilter=ImportTypes});await ImportPathsAsync(files.Select(f=>f.TryGetLocalPath()).Where(p=>p is not null).Cast<string>());
    }
    internal static async Task<MediaAsset> Probe(string path)
    {
        if(!File.Exists(path))throw new FileNotFoundException("The file does not exist or cannot be accessed.",path);
        var json=await MediaTools.RunAsync(FindProbe(),["-v","error","-show_format","-show_streams","-of","json",path]);return MediaMetadata.ParseProbe(json,path);
    }
    static string FindProbe()
    {
        var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        if (Environment.GetEnvironmentVariable("ZENITH_FFPROBE") is { } configured)
            return configured;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            foreach (var relative in new[] { name, Path.Combine("windows", name), Path.Combine("build", "windows", name), Path.Combine("tools", "ffmpeg", "bin", name), Path.Combine("tools","mlt-runtime","package","Shotcut",name) })
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return name;
    }
    async Task Save(bool saveAs)
    {
        if (saveAs || projectPath is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "Save project",
                SuggestedFileName = Project.Name + ".zenith",
                DefaultExtension = "zenith",
                FileTypeChoices = [new("Zenith project") { Patterns = ["*.zenith"] }]
            });
            if (file?.TryGetLocalPath() is not { } path)
                return;
            projectPath = path;
        }
        ProjectStore.Save(projectPath, Project);
        session.MarkSaved();
        Refresh();
        status.Text = "Saved " + projectPath;
    }
    async Task Open()
    {
        if (!await CanReplace())
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Open project",
            FileTypeFilter = [new("Zenith project") { Patterns = ["*.zenith", "*.json"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            LoadProject(path);
    }
    void LoadProject(string path)
    {
        InvalidatePreview();
        session = new(ProjectStore.Load(path));
        formatChosen = true;
        projectPath = path;
        playhead = 0;
        selectedClip = null;
        Refresh(true);
        var missing = Project.Media.Where(m => !string.IsNullOrEmpty(m.Path) && !File.Exists(m.Path)).ToList();
        status.Text = missing.Count == 0 ? "Opened " + path : $"{missing.Count} media files are missing. Use Relink in the library menu.";
        if (missing.Count > 0)
            Run(() => Relink(missing));
    }
    async Task Relink(List<MediaAsset> missing)
    {
        foreach (var media in missing)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = "Locate missing media: " + media.Name
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
                Change("Relink media", p => p.Media.First(m => m.Id == media.Id).Path = path);
        }
    }
    string RecoveryPath() => projectPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZenithClipper", UntitledName);
    async Task Recover()
    {
        if (!await CanReplace())
            return;
        var recovered = ProjectStore.LoadRecovery(RecoveryPath()) ?? throw new IOException("No autosave is available for this project.");
        InvalidatePreview();
        session = new(recovered);
        session.MarkUnsaved();
        formatChosen = true;
        Refresh(true);
        status.Text = "Recovered autosave. Save the project to keep it.";
    }
    async Task New()
    {
        if (!await CanReplace())
            return;
        InvalidatePreview();
        session = new(NewProject());
        formatChosen = false;
        projectPath = null;
        playhead = 0;
        selectedClip = null;
        Refresh(true);
    }
    async Task<bool> CanReplace()
    {
        if (exportFinished is not null)
        {
            status.Text = "Finish or cancel the export before opening another project.";
            return false;
        }
        if (playing)
        {
            playing = false; UpdateTransportState();
            await engine.PauseAsync();
        }
        if (!session.Dirty)
            return true;
        var result = await Ask("Unsaved changes", "Save changes before continuing?", ["Save", "Discard", "Cancel"]);
        if (result == "Save")
        {
            await Save(false);
            return !session.Dirty;
        }
        return result == "Discard";
    }
    async Task<string?> Ask(string title, string message, string[] choices)
    {
        var dialog = new Window { Title = title, Width = 460, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = PanelBrush, Foreground = TextBrush };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 15 };
        dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = SoftTextBrush, LineHeight = 19 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(buttons);
        foreach (var choice in choices)
            { var b = Button(choice, () => dialog.Close(choice)); b.HorizontalAlignment = HorizontalAlignment.Left; if (choice == choices[0]) { b.Background = AccentBrush; b.Foreground = InkBrush; b.Classes.Add("primary"); } buttons.Children.Add(b); }
        return await dialog.ShowDialog<string?>(this);
    }
    async Task SequenceSettings()
    {
        var dialog = new Window { Title = "Sequence settings", Width = 380, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        dialog.Content = panel;
        var name = new TextBox { Text = Sequence.Name };
        var width = new TextBox { Text = Sequence.Width.ToString(CultureInfo.InvariantCulture) };
        var height = new TextBox { Text = Sequence.Height.ToString(CultureInfo.InvariantCulture) };
        var fps = new TextBox { Text = $"{Sequence.Fps.Numerator}/{Sequence.Fps.Denominator}" };
        foreach (var pair in new[] { ("Name", name), ("Width", width), ("Height", height), ("Frame rate (numerator/denominator)", fps) })
        {
            panel.Children.Add(new TextBlock { Text = pair.Item1 });
            panel.Children.Add(pair.Item2);
        }
        panel.Children.Add(Button("Apply", () => { var parts = fps.Text!.Split('/'); var rate = new FrameRate(int.Parse(parts[0], CultureInfo.InvariantCulture), parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 1); Change("Sequence settings", p => { var s = p.Sequences.First(s => s.Id == p.ActiveSequenceId); s.Name = name.Text ?? "Sequence"; s.Width = int.Parse(width.Text!, CultureInfo.InvariantCulture); s.Height = int.Parse(height.Text!, CultureInfo.InvariantCulture); s.Fps = rate; formatChosen = true; }); dialog.Close(); }));
        await dialog.ShowDialog(this);
    }
    async Task OpenArguments()
    {
        var destination = session;
        var path = arguments.FirstOrDefault(a => a != "--project" && !a.StartsWith("--", StringComparison.Ordinal));
        if (path is null)
            return;
        if (Path.GetExtension(path) is ".zenith" or ".json")
            LoadProject(path);
        else
        {
            var ids=await ImportPathsAsync([path]);
            if(ids.Count>0){CheckImportDestination(destination);AppendSelected();}
        }
    }
    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox)
            return;
        if (ShortcutAlias(e) is { } aliased) { OnKeyCore(sender, aliased); e.Handled = aliased.Handled; return; }
        OnKeyCore(sender, e);
    }
    void OnKeyCore(object? sender, KeyEventArgs e)
    {
        if (HandlePremiereKey(e)) { e.Handled = true; return; }
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (control && e.Key == Key.S)
        {
            Run(() => Save(e.KeyModifiers.HasFlag(KeyModifiers.Shift)));
            e.Handled = true;
        }
        else if (control && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                Redo();
            else
                Undo();
            e.Handled = true;
        }
        else if (control && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            Run(TogglePlay);
            e.Handled = true;
        }
        else if (control && e.Key == Key.K)
        {
            Split();
            e.Handled = true;
        }
        else if (!control && e.Key == Key.S)
        {
            snap.IsChecked = snap.IsChecked != true; status.Text = snap.IsChecked == true ? "Snapping on." : "Snapping off.";
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            Delete(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
        else if (e.Key is Key.Home or Key.End)
        {
            Seek(e.Key == Key.Home ? 0 : Math.Max(0, MonitorDuration - 1));
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.None)
        {
            ToggleCaptions();
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right)
        {
            Seek(playhead + (e.Key == Key.Left ? -1 : 1) * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 30 : 1));
            e.Handled = true;
        }
    }
    sealed record MediaRow(Guid Id, string Text, bool Used = false)
    {
        public override string ToString() => Text;
    }
}
