using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Premiere's value graph for one keyframed property: the curve over the clip, keyframe points you drag in time
/// and value, and a right-click menu for Linear, Hold, Bezier (smooth), Ease In, Ease Out, Ease In and Out, or Delete.
/// Clicking empty space moves the playhead there.</summary>
internal sealed class KeyframeGraph : Avalonia.Controls.Control
{
    readonly EditorWindow owner; readonly Guid clipId; readonly string parameter; readonly long duration, start; readonly double scale;
    readonly List<Keyframe> keys; int dragging = -1; Point pressAt; bool moved;
    double lo, hi;
    const double Pad = 8;
    internal static readonly (string Id, string Label)[] Kinds = [("linear", "Linear"), ("discrete", "Hold"), ("smooth", "Bezier (smooth)"), ("ease_in", "Ease In"), ("ease_out", "Ease Out"), ("ease_in_out", "Ease In and Out")];

    public KeyframeGraph(EditorWindow owner, TimelineClip clip, string parameter, double scale)
    {
        this.owner = owner; clipId = clip.Id; this.parameter = parameter; this.scale = scale; duration = Math.Max(1, clip.Duration); start = clip.Start;
        keys = clip.Motion.Where(k => k.Parameter == parameter).OrderBy(k => k.Frame).Select(k => new Keyframe { Parameter = k.Parameter, Frame = k.Frame, Value = k.Value, Interpolation = k.Interpolation }).ToList();
        Height = 120; ClipToBounds = true; Focusable = true; Range();
    }
    void Range()
    {
        lo = keys.Count == 0 ? 0 : keys.Min(k => k.Value); hi = keys.Count == 0 ? 1 : keys.Max(k => k.Value);
        double span = Math.Max(1e-6, hi - lo); if (span < 1e-3) { lo -= .5 / scale; hi += .5 / scale; span = hi - lo; }
        lo -= span * .15; hi += span * .15;
    }
    double X(long f) => Pad + f * (Bounds.Width - 2 * Pad) / duration;
    double Y(double v) => Pad + (hi - v) * (Bounds.Height - 2 * Pad) / Math.Max(1e-9, hi - lo);
    long FrameAt(double x) => Math.Clamp((long)Math.Round((x - Pad) * duration / Math.Max(1, Bounds.Width - 2 * Pad)), 0, duration);
    double ValueAt(double y) => hi - (y - Pad) * (hi - lo) / Math.Max(1, Bounds.Height - 2 * Pad);
    int Near(Point p) { for (int i = 0; i < keys.Count; i++) if (Math.Abs(X(keys[i].Frame) - p.X) <= 6 && Math.Abs(Y(keys[i].Value) - p.Y) <= 6) return i; return -1; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this); int hit = Near(p); e.Handled = true;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (hit < 0) return; int index = hit; var menu = new ContextMenu();
            foreach (var (id, label) in Kinds)
            {
                var item = new MenuItem { Header = (keys[index].Interpolation == id ? "• " : "") + label }; var kind = id;
                item.Click += (_, _) => { keys[index].Interpolation = kind; Commit("Keyframe interpolation"); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var del = new MenuItem { Header = "Delete keyframe" }; del.Click += (_, _) => { keys.RemoveAt(index); Commit("Delete keyframe"); }; menu.Items.Add(del);
            menu.Open(this); return;
        }
        if (hit < 0) { owner.SeekProgram(start + FrameAt(p.X)); return; }
        dragging = hit; pressAt = p; moved = false; e.Pointer.Capture(this);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (dragging < 0) { Cursor = Near(p) >= 0 ? new Cursor(StandardCursorType.SizeAll) : null; return; }
        if (!moved && Math.Abs(p.X - pressAt.X) + Math.Abs(p.Y - pressAt.Y) < 3) return; moved = true;
        long min = dragging > 0 ? keys[dragging - 1].Frame + 1 : 0, max = dragging < keys.Count - 1 ? keys[dragging + 1].Frame - 1 : duration;
        // Shift locks time, so a drag only changes the value.
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)) keys[dragging].Frame = Math.Clamp(FrameAt(p.X), min, Math.Max(min, max));
        keys[dragging].Value = Math.Round(ValueAt(p.Y) * scale, 2) / scale;
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (dragging < 0) return; dragging = -1; e.Pointer.Capture(null);
        if (moved) Commit("Move keyframe");
    }
    void Commit(string label)
    {
        var copy = keys.Select(k => new Keyframe { Parameter = parameter, Frame = k.Frame, Value = k.Value, Interpolation = k.Interpolation }).ToList();
        string param = parameter; Guid id = clipId;
        owner.ChangeKeyframes(label, id, param, copy);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(EditorWindow.GraphBrush, bounds, 6);
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
        for (int i = 1; i < 4; i++) { double y = Math.Round(Pad + i * (Bounds.Height - 2 * Pad) / 4) + .5; context.DrawLine(grid, new Point(Pad, y), new Point(Bounds.Width - Pad, y)); }
        if (keys.Count == 0) return;
        var probe = new TimelineClip { Motion = keys };
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            int steps = Math.Max(2, (int)(Bounds.Width - 2 * Pad));
            for (int i = 0; i <= steps; i++)
            {
                long f = (long)Math.Round(i * (double)duration / steps); var pt = new Point(X(f), Y(ClipBlend.At(probe, parameter, f) ?? 0));
                if (i == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
            }
        }
        context.DrawGeometry(null, new Pen(EditorWindow.AccentBrush, 1.6), geometry);
        long ph = owner.ProgramPlayhead - start;
        if (ph >= 0 && ph <= duration) { double x = Math.Round(X(ph)) + .5; context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1), new Point(x, 0), new Point(x, Bounds.Height)); }
        foreach (var k in keys)
        {
            var c = new Point(X(k.Frame), Y(k.Value));
            var diamond = new StreamGeometry(); using (var g = diamond.Open()) { g.BeginFigure(new Point(c.X, c.Y - 5), true); g.LineTo(new Point(c.X + 5, c.Y)); g.LineTo(new Point(c.X, c.Y + 5)); g.LineTo(new Point(c.X - 5, c.Y)); g.EndFigure(true); }
            context.DrawGeometry(k.Interpolation == "linear" ? Brushes.White : new SolidColorBrush(Color.Parse("#FFC24A")), new Pen(Brushes.Black, 1), diamond);
        }
        string caption = dragging >= 0 ? $"{keys[dragging].Value * scale:0.##} at frame {keys[dragging].Frame}" : $"{hi * scale:0.##}";
        context.DrawText(new FormattedText(caption, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 9.5, new SolidColorBrush(Color.FromArgb(160, 255, 255, 255))), new Point(Pad + 2, 2));
    }
}

public sealed partial class EditorWindow
{
    string graphParameter = "";
    internal void SeekProgram(long frame) { ShowProgram(); Seek(frame); }
    internal void ChangeKeyframes(string label, Guid clipId, string parameter, List<Keyframe> keys)
    {
        Change(label, p => { var c = EditableClip(p, clipId); c.Motion.RemoveAll(k => k.Parameter == parameter); c.Motion.AddRange(keys); });
        Dispatcher.UIThread.Post(BuildInspector);
    }
    /// <summary>The value graph under the Transform section, for whichever keyframed property is picked.</summary>
    void KeyframeGraphSection(StackPanel host, TimelineClip clip)
    {
        var labels = new (string Id, string Label, double Scale)[] { ("x", "Position X", 100), ("y", "Position Y", 100), ("scale_x", "Scale width", 100), ("scale_y", "Scale height", 100), ("rotation", "Rotation", 1), ("opacity", "Opacity", 100), ("volume", "Volume", 1), ("speed", "Speed", 100) };
        var animated = labels.Where(l => clip.Motion.Any(k => k.Parameter == l.Id)).ToList();
        if (animated.Count == 0) return;
        if (animated.All(a => a.Id != graphParameter)) graphParameter = animated[0].Id;
        var pick = new ComboBox { ItemsSource = animated.Select(a => a.Label).ToList(), SelectedIndex = animated.FindIndex(a => a.Id == graphParameter), FontSize = 11.5, MinHeight = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 4), Name = "graph-parameter" };
        pick.SelectionChanged += (_, _) => { if (pick.SelectedIndex >= 0 && animated[pick.SelectedIndex].Id != graphParameter) { graphParameter = animated[pick.SelectedIndex].Id; BuildInspector(); } };
        host.Children.Add(pick);
        var chosen = animated.First(a => a.Id == graphParameter);
        var graph = new KeyframeGraph(this, clip, chosen.Id, chosen.Scale) { Name = "keyframe-graph" };
        ToolTip.SetTip(graph, "Drag keyframes to change time and value (Shift: value only). Right-click a keyframe for Linear, Hold, Bezier or Ease. Click to move the playhead.");
        host.Children.Add(graph);
    }

    internal void VerifyKeyframeEasing()
    {
        var c = new TimelineClip { Duration = 100 };
        c.Motion.Add(new Keyframe { Parameter = "opacity", Frame = 0, Value = 0, Interpolation = "ease_out" }); c.Motion.Add(new Keyframe { Parameter = "opacity", Frame = 10, Value = 1 });
        double v = ClipBlend.At(c, "opacity", 3)!.Value;
        if (Math.Abs(v - (1 - Math.Pow(.7, 3))) > 1e-9) throw new InvalidOperationException("Ease Out did not shape the keyframe curve.");
        c.Motion[0].Interpolation = "discrete";
        if (ClipBlend.At(c, "opacity", 9) != 0 || ClipBlend.At(c, "opacity", 10) != 1) throw new InvalidOperationException("Hold keyframes do not step at the next key.");
        if (Environment.GetEnvironmentVariable("ZENITH_GRAPH_PNG") is { } png)
        {
            var g = new TimelineClip { Duration = 100 };
            g.Motion.AddRange([new Keyframe { Parameter = "x", Frame = 0, Value = 0, Interpolation = "ease_in_out" }, new Keyframe { Parameter = "x", Frame = 40, Value = .3, Interpolation = "discrete" }, new Keyframe { Parameter = "x", Frame = 70, Value = .1, Interpolation = "smooth" }, new Keyframe { Parameter = "x", Frame = 100, Value = .25 }]);
            var graph = new KeyframeGraph(this, g, "x", 100) { Width = 300 };
            graph.Measure(new Size(300, 120)); graph.Arrange(new Rect(0, 0, 300, 120));
            using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(600, 240), new Vector(192, 192)); bmp.Render(graph); bmp.Save(png, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
