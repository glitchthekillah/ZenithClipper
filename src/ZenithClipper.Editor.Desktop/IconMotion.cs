using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using VectorPath = Avalonia.Controls.Shapes.Path;

namespace ZenithClipper.Editor.Desktop;

/// <summary>
/// Small click reactions for toolbar icons: the bin lid lifts, the scissors snip, the bookmark drops in, arrows nudge.
/// Each one moves parts of the icon's own 24×24 drawing for under half a second, then puts everything back exactly.
/// Pure transforms on a few paths, driven by one short-lived timer; nothing runs while idle.
/// </summary>
internal static class IconMotion
{
    static readonly Dictionary<Canvas, (DispatcherTimer Timer, Action<double> Frame, Action Finish)> running = [];

    public static void Play(Button button, string icon)
    {
        if (EditorWindow.Look.ReduceMotion) return;
        if (FindCanvas(button) is not { } canvas) return;
        var paths = canvas.Children.OfType<VectorPath>().ToList();
        if (paths.Count == 0) return;
        if (running.Remove(canvas, out var previous)) { previous.Timer.Stop(); Reset(canvas, paths); }
        // Timing follows common micro-interaction practice: 120–250 ms for feedback, ease-out arrivals, a few pixels of travel,
        // at most ~30° of rotation, scale kept within 0.9–1.1, one moving part, no springs or bounces. The bin is the one
        // deliberate "story" (lift, tilt, set down) and still finishes in under 400 ms.
        (double Ms, Action<double> Frame)? motion = icon switch
        {
            "delete" => (380, t =>
            {
                double up = t < .35 ? EaseOut(t / .35) : t < .55 ? 1 : 1 - EaseInOut((t - .55) / .45);
                double tilt = t < .15 ? 0 : t < .45 ? EaseOut((t - .15) / .30) : t < .55 ? 1 : 1 - EaseInOut((t - .55) / .45);
                foreach (int i in new[] { 0, 2 }) Transform(paths, i, new Point(4, 6), -18 * tilt, 0, -3 * up);
            }),
            "cut" => (300, t =>
            {
                double phase = (t * 2) % 1, close = phase < .4 ? EaseIn(phase / .4) : 1 - EaseOut((phase - .4) / .6);
                foreach (int i in new[] { 0, 3, 4 }) Transform(paths, i, new Point(12, 12), -24 * close, 0, 0);
                foreach (int i in new[] { 1, 2 }) Transform(paths, i, new Point(12, 12), 24 * close, 0, 0);
            }),
            "marker" => (200, t => Whole(canvas, 0, 0, 0, Press(t))),
            "undo" => (220, t => Whole(canvas, -20 * Arc(t), 0, 0, 1)),
            "redo" => (220, t => Whole(canvas, 20 * Arc(t), 0, 0, 1)),
            "ripple" => (200, t => { double x = -3 * Arc(t); Transform(paths, 0, default, 0, x, 0); Transform(paths, 1, default, 0, x, 0); }),
            "plus" => (200, t => Whole(canvas, 90 * EaseOut(t), 0, 0, 1)),
            "minus" => (160, t => Whole(canvas, 0, 0, 0, 1 - .2 * Arc(t), 1)),
            "fit" => (200, t => { double d = 1.5 * Arc(t); (double X, double Y)[] corners = [(-d, -d), (d, -d), (-d, d), (d, d)]; for (int i = 0; i < 4; i++) Transform(paths, i, default, 0, corners[i].X, corners[i].Y); }),
            "play" or "pause" => (180, t => Whole(canvas, 0, 0, 0, Press(t))),
            "previous" or "start" => (160, t => Whole(canvas, 0, -2 * Arc(t), 0, 1)),
            "next" or "end" => (160, t => Whole(canvas, 0, 2 * Arc(t), 0, 1)),
            "export" => (200, t => { double y = -2.5 * Arc(t); Transform(paths, 1, default, 0, 0, y); Transform(paths, 2, default, 0, 0, y); }),
            "import" => (200, t => { double y = 2.5 * Arc(t); Transform(paths, 1, default, 0, 0, y); Transform(paths, 2, default, 0, 0, y); }),
            "captions" => (240, t => { for (int i = 1; i < paths.Count; i++) paths[i].Opacity = Math.Clamp(t * 3 - (i - 1) * .45, .2, 1); }),
            "text" => (180, t => Whole(canvas, 0, 0, 0, Press(t))),
            "media" => (180, t => Transform(paths, 1, default, 0, 2 * Arc(t), 0)),
            "audio" => (240, t => { for (int i = 1; i < paths.Count; i++) paths[i].Opacity = 1 - .7 * Arc(Math.Clamp(t * 1.4 - (i - 1) * .3, 0, 1)); }),
            "lock" => (200, t => Transform(paths, 1, default, 0, 0, -2 * Arc(t))),
            "eye" or "hidden" => (160, t => Whole(canvas, 0, 0, 0, 1, 1 - .85 * Arc(t))),
            _ => null
        };
        if (motion is not { } m) return;
        var frame = m.Frame; double duration = m.Ms;
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        void Finish() { timer.Stop(); running.Remove(canvas); Reset(canvas, paths); }
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration);
            if (t >= 1 || !canvas.IsAttachedToVisualTree()) { Finish(); return; }
            frame(t);
        };
        running[canvas] = (timer, frame, Finish); frame(0); timer.Start();
    }

    /// <summary>Hover tint: the bin turns red while the pointer is over it, so a delete never surprises anyone.</summary>
    public static void Hover(Button button, string icon, bool over)
    {
        if (EditorWindow.Look.ReduceMotion) return;
        if (icon != "delete" || FindCanvas(button) is not { } canvas) return;
        foreach (var path in canvas.Children.OfType<VectorPath>())
        {
            if (over) { path.Tag ??= path.Stroke; path.Stroke = Danger; }
            else if (path.Tag is IBrush original) { path.Stroke = original; path.Tag = null; }
        }
    }
    static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#F04848"));
    static Canvas? FindCanvas(Button button) => button.Content switch
    {
        Viewbox { Child: Canvas c } => c,
        Panel panel => panel.Children.OfType<Viewbox>().Select(v => v.Child).OfType<Canvas>().FirstOrDefault(),
        _ => null
    };
    static bool IsAttachedToVisualTree(this Visual visual) => TopLevel.GetTopLevel(visual) is not null;
    static void Reset(Canvas canvas, List<VectorPath> paths) { canvas.RenderTransform = null; foreach (var p in paths) { p.RenderTransform = null; p.Opacity = 1; } }
    static void Transform(List<VectorPath> paths, int index, Point pivot, double degrees, double x, double y)
    {
        if (index >= paths.Count) return;
        var group = new TransformGroup();
        if (degrees != 0) { group.Children.Add(new TranslateTransform(-pivot.X, -pivot.Y)); group.Children.Add(new RotateTransform(degrees)); group.Children.Add(new TranslateTransform(pivot.X, pivot.Y)); }
        if (x != 0 || y != 0) group.Children.Add(new TranslateTransform(x, y));
        paths[index].RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        paths[index].RenderTransform = group;
    }
    static void Whole(Canvas canvas, double degrees, double x, double y, double scaleX, double scaleY = double.NaN)
    {
        var group = new TransformGroup();
        group.Children.Add(new TranslateTransform(-12, -12));
        group.Children.Add(new ScaleTransform(scaleX, double.IsNaN(scaleY) ? scaleX : scaleY));
        if (degrees != 0) group.Children.Add(new RotateTransform(degrees));
        group.Children.Add(new TranslateTransform(12 + x, 12 + y));
        canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        canvas.RenderTransform = group;
    }
    static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    static double EaseIn(double t) => t * t * t;
    static double EaseInOut(double t) => t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    /// <summary>0 → 1 → 0 with an ease-out rise and ease-in fall; the shape for "nudge and return".</summary>
    static double Arc(double t) => t < .45 ? EaseOut(t / .45) : 1 - EaseInOut((t - .45) / .55);
    /// <summary>Press feedback: scale down to 0.9, then return with a 3% overshoot that settles by the end.</summary>
    static double Press(double t) => t < .35 ? 1 - .1 * EaseOut(t / .35) : t < .75 ? .9 + .13 * EaseOut((t - .35) / .4) : 1.03 - .03 * EaseInOut((t - .75) / .25);
    /// <summary>Headless check: every animation leaves its icon exactly as it found it.</summary>
    internal static void Verify(Func<string, Avalonia.Controls.Control> glyph)
    {
        foreach (var icon in new[] { "delete", "cut", "marker", "undo", "redo", "ripple", "plus", "minus", "fit", "play", "previous", "next", "export", "captions", "text", "media", "audio", "lock", "eye" })
        {
            var button = new Button { Content = glyph(icon) };
            Play(button, icon);
            var canvas = FindCanvas(button)!;
            if (!running.TryGetValue(canvas, out var animation)) throw new InvalidOperationException($"Icon '{icon}' has no click animation.");
            bool moved = false;
            for (double t = .05; t < 1; t += .05)
            {
                animation.Frame(t);
                moved |= canvas.RenderTransform is not null || canvas.Children.OfType<VectorPath>().Any(p => p.RenderTransform is not null || p.Opacity != 1);
            }
            if (!moved) throw new InvalidOperationException($"Icon '{icon}' animation never moved anything.");
            animation.Finish();
            if (running.ContainsKey(canvas) || canvas.RenderTransform is not null || canvas.Children.OfType<VectorPath>().Any(p => p.RenderTransform is not null || p.Opacity != 1))
                throw new InvalidOperationException($"Icon '{icon}' did not return to rest after its animation.");
        }
        Console.WriteLine("Icon motion smoke passed: 19 click animations run and every icon returns exactly to rest.");
    }
}
