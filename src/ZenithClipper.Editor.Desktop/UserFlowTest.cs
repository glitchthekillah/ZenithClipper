using Avalonia;
using ZenithClipper.Mlt;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Drives the editor like a person would (import, drag, double-click, right-click, tabs, tools) on the headless
/// surface and saves a screenshot after each step. Run with --user-flow &lt;media folder&gt; &lt;output folder&gt;.</summary>
internal static class UserFlowTest
{
    static readonly List<string> failures = [];
    static int shot;
    static string outDir = "";

    internal static void Run(string media, string output)
    {
        outDir = output; Directory.CreateDirectory(outDir);
        AppBuilder.Configure<EditorApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var w = new EditorWindow([]) { Width = 1600, Height = 1000 };
        w.Show(); Pump(); Snap(w, "empty");

        var files = new[] { "clip.mp4", "song.mp3", "anim.gif", "still.png", "overwatch.mp4", "v.mov", "v.mkv", "v.webm", "v.avi", "a.wav", "a.flac", "a.ogg", "i.jpg", "i.webp", "i.bmp" }.Select(f => Path.Combine(media, f)).Where(File.Exists).ToList();
        var import = w.ImportPathsAsync(files);
        Wait(() => import.IsCompleted, 60); if (import.IsFaulted) Fail("import threw " + import.Exception!.GetBaseException().Message);
        MediaPreviews.WaitAll(TimeSpan.FromSeconds(60)); Pump(); Pump();
        Check(w.TestMediaCount == files.Count, $"imported {w.TestMediaCount} of {files.Count} files");
        foreach (var f in files)
        {
            var kind = w.TestKindOf(Path.GetFileName(f));
            var want = Path.GetExtension(f) switch { ".mp3" or ".wav" or ".flac" or ".ogg" => MediaKind.Audio, ".png" or ".jpg" or ".webp" or ".bmp" => MediaKind.Image, _ => MediaKind.Video };
            Check(kind == want || (Path.GetExtension(f) == ".gif" && kind is MediaKind.Video or MediaKind.Image), $"{Path.GetFileName(f)} imports as {kind}");
        }
        Snap(w, "imported");
        w.TestOpenSource(1); Pump(); Snap(w, "source-audio");
        Check(w.TestDurationText.Contains("00:00:08", StringComparison.Ordinal), $"source shows the file's length ({w.TestDurationText})");
        w.ShowProgram(); Pump();

        // Drag the video card onto the timeline.
        int before = w.TestClipCount;
        var card = w.TestCardCenter(0); var lane = w.TestLaneCenter(TrackKind.Video);
        if (card is { } c && lane is { } l)
        {
            w.MouseDown(c, MouseButton.Left); Pump();
            for (int i = 1; i <= 12; i++) { w.MouseMove(c + (l - c) * (i / 12.0), RawInputModifiers.LeftMouseButton); Pump(); }
            Check(w.TestGhostTop is > 0, $"drag preview sits on a track (top {w.TestGhostTop})");
            Check(w.TestDragChipVisible, "a card follows the pointer while dragging");
            Snap(w, "dragging");
            w.MouseUp(l, MouseButton.Left); Pump();
            Check(w.TestClipCount > before, $"drag video to timeline: clips {before} -> {w.TestClipCount}");
        }
        else Fail("could not locate card or lane");
        Snap(w, "after-drag");

        // Double-click the audio card.
        before = w.TestClipCount;
        if (w.TestCardCenter(1) is { } a)
        {
            w.MouseDown(a, MouseButton.Left); w.MouseUp(a, MouseButton.Left); w.MouseDown(a, MouseButton.Left); w.MouseUp(a, MouseButton.Left); Pump();
            Check(w.TestClipCount > before, $"double-click audio adds it: clips {before} -> {w.TestClipCount}");
        }
        Snap(w, "after-doubleclick");

        // Drag the gif and the png too.
        foreach (int index in new[] { 2, 3 })
        {
            before = w.TestClipCount;
            if (w.TestCardCenter(index) is { } g && w.TestLaneCenter(TrackKind.Video) is { } lane0)
            {
                var dst = lane0 + new Point(index * 220, 0);
                w.MouseDown(g, MouseButton.Left); for (int i = 1; i <= 8; i++) { w.MouseMove(g + (dst - g) * (i / 8.0), RawInputModifiers.LeftMouseButton); Pump(); }
                w.MouseUp(dst, MouseButton.Left); Pump();
                Check(w.TestClipCount > before, $"drag item {index} to timeline");
            }
        }
        Snap(w, "all-on-timeline");

        // Right-click a card: our own menu should open.
        if (w.TestCardCenter(0) is { } rc) { w.MouseDown(rc, MouseButton.Right); w.MouseUp(rc, MouseButton.Right); Pump(); Snap(w, "card-menu"); w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump(); }

        // Every tool button should become the active tool.
        for (int i = 0; i < w.TestToolCount; i++)
        {
            var (tool, point) = w.TestToolButton(i);
            w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left); Pump();
            Check(w.tool == tool, $"tool button {tool}");
        }
        w.SetTool(EditorWindow.EditTool.Selection); Pump();

        // Tabs: every tab button should change what is shown.
        foreach (var (name, point, isActive) in w.TestTabs())
        {
            w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left); Pump();
            Check(isActive(), $"tab {name}");
        }
        Snap(w, "tabs");

        // Right-click a clip on the timeline.
        if (w.TestLaneCenter(TrackKind.Video) is { } clipPoint) { w.MouseDown(clipPoint, MouseButton.Right); w.MouseUp(clipPoint, MouseButton.Right); Pump(); Snap(w, "clip-menu"); w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump(); }

        // Appearance from the panel recolours the open editor.
        EditorWindow.ApplyLook(new ZenithClipper.Appearance { Accent = "#4A8BFF", Tone = "Midnight" }); Pump();
        Check(EditorWindow.AccentBrush.Color == Avalonia.Media.Color.Parse("#4A8BFF"), "accent follows appearance");
        Snap(w, "azure-midnight");
        EditorWindow.ApplyLook(new ZenithClipper.Appearance()); Pump();
        w.RunDeeperChecks(Check, Snap, Pump, Wait, outDir);
        Console.WriteLine(failures.Count == 0 ? "USER FLOW PASSED" : "USER FLOW FAILED:\n  " + string.Join("\n  ", failures));
        w.Close();
    }

    static void Pump() { for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs(); }
    static void Wait(Func<bool> done, int seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
    }
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures.Add(what); }
    static void Fail(string what) => Check(false, what);
    static void Snap(Window w, string name)
    {
        Pump();
        using var bmp = new RenderTargetBitmap(new PixelSize((int)w.Width, (int)w.Height));
        bmp.Render(w);
        bmp.Save(Path.Combine(outDir, $"{++shot:00}-{name}.png"), PngBitmapEncoderOptions.Default);
    }
}

public sealed partial class EditorWindow
{
    internal double? TestGhostTop => timeline.TestGhostTop;
    internal void TestOpenSource(int index) => OpenInSource(Project.Media[index].Id);
    internal string TestDurationText => durationHeading.Text ?? "";
    /// <summary>Second half of the user flow: keyboard split, trimming with the mouse, undo/redo, playback, save and
    /// reopen, and a real export checked with ffprobe.</summary>
    internal void RunDeeperChecks(Action<bool, string> check, Action<Window, string> snap, Action pump, Action<Func<bool>, int> wait, string outDir)
    {
        // Click empty space to dismiss any open menu, as a person would, and go back to the Media tab.
        this.MouseDown(new Point(Width / 2, Height - 8), MouseButton.Left); this.MouseUp(new Point(Width / 2, Height - 8), MouseButton.Left); pump();
        check(this.InputHitTest(new Point(Width / 2, Height / 2)) ?.GetType().Name != "LightDismissOverlayLayer", "clicking outside closes the right-click menu");
        libraryFilter = "Media"; BuildLibraryTabs(); UpdateLibrary(); pump();
        ShowProgram(); SetTool(EditTool.Selection); pump();
        // A real recorded clip, if one was supplied: double-click it onto the end of the timeline.
        library.ScrollIntoView(4); pump();
        if (Project.Media.Count > 4 && TestCardCenter(4) is { } real)
        {
            int n = TestClipCount;
            this.MouseDown(real, MouseButton.Left); this.MouseUp(real, MouseButton.Left); this.MouseDown(real, MouseButton.Left); this.MouseUp(real, MouseButton.Left); pump();
            check(TestClipCount > n, "double-click adds the real recording");
            snap(this, "real-clip");
        }
        var video = Sequence.Tracks.First(t => t.Kind == TrackKind.Video).Clips.OrderBy(c => c.Start).First();
        // Ctrl+K splits at the playhead.
        Seek(video.Start + video.Duration / 2); pump();
        int before = TestClipCount;
        this.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k"); pump();
        check(TestClipCount > before, $"Ctrl+K splits at the playhead: clips {before} -> {TestClipCount}");
        // Undo / redo with the keyboard.
        this.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z"); pump();
        check(TestClipCount == before, "Ctrl+Z undoes the split");
        this.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y"); pump();
        if (TestClipCount == before) { this.KeyPress(Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Z, "Z"); pump(); }
        check(TestClipCount > before, "redo brings the split back");
        // Trim: drag the right edge of the last video clip 40 px to the left.
        var last = Sequence.Tracks.First(t => t.Kind == TrackKind.Video).Clips.First(c => Project.Media.FirstOrDefault(m => m.Id == c.MediaId)?.Name == "still.png");
        long oldDuration = last.Duration;
        var card = timeline.Children.OfType<Border>().FirstOrDefault(b => b.Tag is Guid id && id == last.Id);
        if (card?.TranslatePoint(new Point(card.Bounds.Width - 2, card.Bounds.Height / 2), this) is { } edge)
        {
            this.MouseDown(edge, MouseButton.Left); pump();
            for (int i = 1; i <= 8; i++) { this.MouseMove(edge - new Point(5 * i, 0), RawInputModifiers.LeftMouseButton); pump(); }
            this.MouseUp(edge - new Point(40, 0), MouseButton.Left); pump();
            var after = Sequence.Tracks.SelectMany(t => t.Clips).First(c => c.Id == last.Id);
            check(after.Duration < oldDuration, $"dragging a clip's right edge trims it ({oldDuration} -> {after.Duration} frames)");
        }
        else check(false, "found the last clip on the timeline");
        snap(this, "after-trim");
        // Playback: the playhead should move.
        Seek(0); pump(); long start = ProgramPlayhead;
        var play = TogglePlay(); wait(() => play.IsCompleted, 20);
        var until = DateTime.UtcNow.AddSeconds(2); while (DateTime.UtcNow < until) { pump(); Thread.Sleep(30); }
        long moved = ProgramPlayhead;
        if (playing) { var pause = TogglePlay(); wait(() => pause.IsCompleted, 10); }
        check(moved > start, $"playback moves the playhead ({start} -> {moved})");
        // Save, reopen, compare.
        string projectFile = Path.Combine(outDir, "flow.zcproj");
        ProjectStore.Save(projectFile, Project);
        int clips = TestClipCount, media = TestMediaCount;
        LoadProject(projectFile); pump();
        check(TestClipCount == clips && TestMediaCount == media, $"save and reopen keeps {clips} clips and {media} files");
        // Export the timeline and check the file.
        if (Environment.GetEnvironmentVariable("ZC_SKIP_EXPORT") is not null) return;
        string output = Path.Combine(outDir, "export.mp4"); File.Delete(output);
        // Export 10 s that include the real recording when there is one (a full 120 fps render is too slow for a check).
        var realClip = Sequence.Tracks.SelectMany(t => t.Clips).Where(c => Project.Media.FirstOrDefault(m => m.Id == c.MediaId)?.Name == "overwatch.mp4").OrderBy(c => c.Start).FirstOrDefault();
        long? from = realClip?.Start, to = realClip is null ? null : realClip.Start + Sequence.Fps.Frames(10) - 1;
        var export = RunExports([(ProjectCopy, output, new ExportOptions(ExportPresets.All[1], from, to))]);
        wait(() => export.IsCompleted, 900);
        if (export.IsFaulted) check(false, "export threw " + export.Exception!.GetBaseException().Message);
        double seconds = ProbeSeconds(output), expected = from is null ? Sequence.Fps.Seconds(Sequence.Duration) : 10;
        check(seconds > 0 && Math.Abs(seconds - expected) < 1, $"export is a playable {seconds:0.0}s file (timeline {expected:0.0}s)");
        // Every container format exports a playable 2 s file.
        foreach (var preset in ExportPresets.All.Where(p => p.Group is "Formats" || p.AudioOnly || p.Extension == "mov"))
        {
            string file = Path.Combine(outDir, "format-" + preset.Extension + (preset.Name.Contains("ProRes") ? "-prores" : "") + "." + preset.Extension); File.Delete(file);
            var job = RunExports([(ProjectCopy, file, new ExportOptions(preset, 0, Sequence.Fps.Frames(2)))]);
            wait(() => job.IsCompleted, 300);
            double got = ProbeSeconds(file);
            check(!job.IsFaulted && got > 1.4 && got < 2.6, $"{preset.Name} exports ({got:0.0}s{(job.IsFaulted ? ", " + job.Exception!.GetBaseException().Message : "")})");
        }
    }
    static double ProbeSeconds(string file)
    {
        if (!File.Exists(file)) return 0;
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffprobe", ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", file]) { RedirectStandardOutput = true })!;
            string o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
            return double.TryParse(o.Trim(), System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0;
        }
        catch (System.ComponentModel.Win32Exception) { return 0; }
    }
    internal MediaKind? TestKindOf(string name) => Project.Media.FirstOrDefault(m => m.Name == name || Path.GetFileName(m.Path) == name)?.Kind;
    internal bool TestDragChipVisible => dragChip is { IsVisible: true } && dragChip.GetVisualParent() is not null;
    internal int TestMediaCount => Project.Media.Count;
    internal int TestClipCount => Sequence.Tracks.Sum(t => t.Clips.Count);
    internal Point? TestCardCenter(int index)
    {
        UpdateLibrary(); Dispatcher.UIThread.RunJobs(); library.ScrollIntoView(index); for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
        if (library.ContainerFromIndex(index) is not Avalonia.Controls.Control item || !item.IsEffectivelyVisible) return null;
        return item.TranslatePoint(new Point(item.Bounds.Width / 2, 30), this);
    }
    internal Point? TestLaneCenter(TrackKind kind, int skip = 0)
    {
        var rows = DisplayTracks(); int seen = 0;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Kind == kind && seen++ == skip)
                return timeline.TranslatePoint(new Point(timelineScroller.Offset.X + 260, RowTop(i) + TrackHeight / 2.0), this);
        return null;
    }
    internal int TestToolCount => toolPalette.Children.Count;
    internal (EditTool, Point) TestToolButton(int i)
    {
        var child = (Avalonia.Controls.Control)toolPalette.Children[i];
        return (ToolList[i].Tool, child.TranslatePoint(new Point(child.Bounds.Width / 2, child.Bounds.Height / 2), this) ?? default);
    }
    /// <summary>Tab strips rebuild on click, so each tab is located fresh: (label, strip, index, check after click).</summary>
    internal IEnumerable<(string, Point, Func<bool>)> TestTabs()
    {
        foreach (var (strip, check) in new (Panel, Func<string, bool>)[] { (libraryTabs, n => n == libraryFilter), (rightTabStrip!, n => n == rightPanel), (monitorTabs, n => n.StartsWith("Source", StringComparison.Ordinal) ? sourceMode : !sourceMode) })
            for (int i = 0; i < strip.Children.Count; i++)
            {
                if (strip.Children[i] is not Button b || !b.IsEffectivelyEnabled || !b.IsEffectivelyVisible) continue;
                string name = b.Content as string ?? Avalonia.Automation.AutomationProperties.GetName(b) ?? "?";
                if (b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), this) is not { } p) continue;
                yield return (name, p, () => check(name));
            }
    }
}

/// <summary>Renders README screenshots (--showcase &lt;clip&gt; &lt;output folder&gt;) at 2x.</summary>
internal static class Showcase
{
    internal static void Run(string clip, string output)
    {
        Directory.CreateDirectory(output);
        AppBuilder.Configure<EditorApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var w = new EditorWindow([]) { Width = 1600, Height = 960 };
        w.Show(); Pump();
        var import = w.ImportPathsAsync([clip]); Wait(() => import.IsCompleted, 120);
        MediaPreviews.WaitAll(TimeSpan.FromSeconds(120)); Pump();
        w.ShowcaseSetup(); Wait(() => false, 6);
        Save(w, Path.Combine(output, "editor.png"));
        w.ShowcaseLook(new ZenithClipper.Appearance { Accent = "#4A8BFF", Tone = "Midnight" }); Wait(() => false, 2);
        Save(w, Path.Combine(output, "editor-azure.png"));
        w.Close();
    }
    static void Pump() { for (int i = 0; i < 6; i++) Dispatcher.UIThread.RunJobs(); }
    static void Wait(Func<bool> done, int seconds) { var until = DateTime.UtcNow.AddSeconds(seconds); while (!done() && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); } }
    static void Save(Window w, string path)
    {
        Pump();
        using var bmp = new RenderTargetBitmap(new PixelSize((int)w.Width * 2, (int)w.Height * 2), new Vector(192, 192));
        bmp.Render(w); bmp.Save(path, PngBitmapEncoderOptions.Default);
    }
}

public sealed partial class EditorWindow
{
    /// <summary>Clip on the timeline, cut in two, playhead mid-action, second half selected so the inspector is populated.</summary>
    internal void ShowcaseSetup()
    {
        library.SelectedIndex = 0; AppendSelected(); Dispatcher.UIThread.RunJobs();
        var c = Sequence.Tracks.SelectMany(t => t.Clips).First();
        Seek(c.Start + c.Duration * 2 / 5); Split();
        var second = Sequence.Tracks.First(t => t.Kind == TrackKind.Video).Clips.OrderBy(x => x.Start).Last();
        selectedClip = second.Id; selectedTrack = Sequence.Tracks.First(t => t.Kind == TrackKind.Video).Id;
        ShowProgram(); Seek(c.Start + c.Duration / 4); BuildInspector();
    }
    internal void ShowcaseLook(ZenithClipper.Appearance look) => ApplyLook(look);
}
