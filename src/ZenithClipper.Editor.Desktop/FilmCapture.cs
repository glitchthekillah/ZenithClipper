using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ZenithClipper.Mlt;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Website capture (--film &lt;clip folder&gt; &lt;output folder&gt;): drives the real editor headlessly with the mouse
/// and keyboard, the same way a person would, and saves every step as numbered 30 fps frames.</summary>
internal static class FilmCapture
{
    static string dir = "";
    static int n;
    internal static void Run(string clips, string output)
    {
        dir = Path.Combine(output, "editor"); Directory.CreateDirectory(dir);
        AppBuilder.Configure<EditorApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var w = new EditorWindow([]) { Width = 1600, Height = 900 };
        w.Show(); Settle(1);
        w.Film(clips, output);
        File.WriteAllText(Path.Combine(output, "editor-frames.txt"), n.ToString());
        w.Close();
    }
    internal static void Pump() { for (int i = 0; i < 6; i++) Dispatcher.UIThread.RunJobs(); }
    internal static void Settle(double seconds) { var until = DateTime.UtcNow.AddSeconds(seconds); while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(15); } }
    internal static void Wait(Func<bool> done, int seconds) { var until = DateTime.UtcNow.AddSeconds(seconds); while (!done() && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); } }
    internal static void Frame(Window w, int hold = 1, double settle = .06)
    {
        Settle(settle); Pump();
        using var bmp = new RenderTargetBitmap(new PixelSize((int)w.Width, (int)w.Height), new Vector(96, 96));
        bmp.Render(w);
        string first = Path.Combine(dir, $"f{++n:D5}.png"); bmp.Save(first, PngBitmapEncoderOptions.Default);
        for (int i = 1; i < hold; i++) File.Copy(first, Path.Combine(dir, $"f{++n:D5}.png"), true);
    }
    internal static void Still(Window w, string path)
    {
        Settle(.3); Pump();
        using var bmp = new RenderTargetBitmap(new PixelSize((int)(w.Width * 2), (int)(w.Height * 2)), new Vector(192, 192));
        bmp.Render(w); bmp.Save(path, PngBitmapEncoderOptions.Default);
    }
}

public sealed partial class EditorWindow
{
    internal void Film(string clipsDir, string output)
    {
        void F(int hold = 1, double settle = .06) => FilmCapture.Frame(this, hold, settle);
        F(20);
        string[] names = ["hero.mp4", "fighter.mp4", "golf.mp4", "mech.mp4"];
        var import = ImportPathsAsync(names.Select(x => Path.Combine(clipsDir, x)).Where(File.Exists).ToArray());
        FilmCapture.Wait(() => import.IsCompleted, 120); MediaPreviews.WaitAll(TimeSpan.FromSeconds(120)); FilmCapture.Pump();
        ShowProgram(); F(24, .5);

        // Drag the first clip from the media bin onto the video track.
        if (TestCardCenter(0) is { } c && TestLaneCenter(TrackKind.Video) is { } lc)
        {
            var l = new Point(Math.Max(215, lc.X - 650), lc.Y);
            this.MouseDown(c, MouseButton.Left); F(4);
            for (int i = 1; i <= 22; i++) { this.MouseMove(c + (l - c) * (1 - Math.Pow(1 - i / 22.0, 3)), RawInputModifiers.LeftMouseButton); F(); }
            this.MouseUp(l, MouseButton.Left);
        }
        FitTimeline(); this.MouseMove(new Point(800, 880)); F(14, .8);

        // Scrub through it: the preview shows the real footage.
        var video = Sequence.Tracks.First(t => t.Kind == TrackKind.Video).Clips.OrderBy(x => x.Start).First();
        for (int i = 0; i <= 40; i++) { Seek(video.Start + (long)(video.Duration * .45 * i / 40)); F(1, .4); }

        // Ctrl+K splits at the playhead.
        F(8);
        this.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k"); F(18, .7);

        // Trim: drag the right edge of the second half to the left.
        var second = Sequence.Tracks.First(t => t.Kind == TrackKind.Video).Clips.OrderBy(x => x.Start).Last();
        var card = timeline.Children.OfType<Border>().FirstOrDefault(b => b.Tag is Guid id && id == second.Id);
        if (card?.TranslatePoint(new Point(card.Bounds.Width - 2, card.Bounds.Height / 2), this) is { } edge)
        {
            double span = Math.Min(card.Bounds.Width * .35, 160);
            this.MouseDown(edge, MouseButton.Left); F(3);
            for (int i = 1; i <= 18; i++) { this.MouseMove(edge - new Point(span * i / 18, 0), RawInputModifiers.LeftMouseButton); F(1, .25); }
            this.MouseUp(edge - new Point(span, 0), MouseButton.Left);
        }
        this.MouseMove(new Point(800, 880)); F(14, .7);

        // Double-click the second clip to put it after everything.
        if (TestCardCenter(1) is { } b)
        { this.MouseDown(b, MouseButton.Left); this.MouseUp(b, MouseButton.Left); this.MouseDown(b, MouseButton.Left); this.MouseUp(b, MouseButton.Left); }
        this.MouseMove(new Point(800, 880)); FitTimeline(); F(16, .8);

        // A title over the opening.
        Seek(video.Start + video.Duration / 10); FilmCapture.Pump();
        var asset = TitleRasterizer.Create("GG", 120, "#FFFFFF", 3, AssetDirectory);
        Change("Create title", p =>
        {
            var sequence = p.Sequences.First(s => s.Id == p.ActiveSequenceId);
            var track = OverlayTrack(sequence, "Titles");
            var clip = TextClip(asset, sequence, ProgramPlayhead, sequence.Fps.Frames(3), false);
            p.Media.Add(asset); track.Clips.Add(clip); selectedTrack = track.Id; selectedClip = clip.Id;
        });
        FitTimeline(); F(16, .8);

        // Play the edit through.
        long end = Sequence.Tracks.SelectMany(t => t.Clips).Select(x => x.Start + x.Duration).DefaultIfEmpty(0).Max();
        for (int i = 0; i <= 90; i++) { Seek(end * i / 90); F(1, .4); }
        F(10);

        // The export dialog, as its own window.
        var choose = ChooseExport(); FilmCapture.Settle(1);
        foreach (var owned in OwnedWindows.ToList()) { FilmCapture.Still(owned, Path.Combine(output, "export-dialog.png")); owned.Close(); }
        FilmCapture.Wait(() => choose.IsCompleted, 5);
        FilmCapture.Still(this, Path.Combine(output, "editor-final.png"));
    }
}
