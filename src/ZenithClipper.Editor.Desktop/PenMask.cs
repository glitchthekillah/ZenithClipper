using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>Premiere's pen mask on the program monitor: click to place vertices, click the first one (or double-click,
/// or Enter) to close; once closed, drag vertices to reshape. Esc stops drawing.</summary>
internal sealed class PenMaskOverlay : Control
{
    readonly EditorWindow owner;
    internal readonly List<(double U, double V)> Points = []; internal bool Closed; int dragging = -1;
    public PenMaskOverlay(EditorWindow owner) { this.owner = owner; IsHitTestVisible = false; ClipToBounds = true; Focusable = true; }
    public bool Active { get => IsHitTestVisible; set { IsHitTestVisible = value; Cursor = value ? new Cursor(StandardCursorType.Cross) : null; InvalidateVisual(); } }

    public void Load(ClipMask? mask)
    {
        Points.Clear(); Closed = false;
        if (mask is { Shape: "path" }) { for (int i = 0; i + 1 < mask.Points.Count; i += 2) Points.Add((mask.Points[i], mask.Points[i + 1])); Closed = Points.Count >= 3; }
        InvalidateVisual();
    }
    int Near(Point p) { for (int i = 0; i < Points.Count; i++) { var s = owner.ClipToScreenPoint(Points[i]); if (Math.Abs(s.X - p.X) <= 7 && Math.Abs(s.Y - p.Y) <= 7) return i; } return -1; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var at = e.GetPosition(this); e.Handled = true; Focus();
        int hit = Near(at);
        if (!Closed)
        {
            if ((hit == 0 && Points.Count >= 3) || (e.ClickCount >= 2 && Points.Count >= 3)) { Closed = true; owner.CommitPenMask(Points); return; }
            Points.Add(owner.ScreenToClipPoint(at)); InvalidateVisual(); return;
        }
        if (hit >= 0) { dragging = hit; e.Pointer.Capture(this); }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (dragging < 0) return; Points[dragging] = owner.ScreenToClipPoint(e.GetPosition(this)); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (dragging < 0) return; dragging = -1; e.Pointer.Capture(null); owner.CommitPenMask(Points);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Closed && Points.Count >= 3) { Closed = true; owner.CommitPenMask(Points); e.Handled = true; }
        else if (e.Key == Key.Escape) { owner.StopPen(); e.Handled = true; }
        else if (e.Key == Key.Back && !Closed && Points.Count > 0) { Points.RemoveAt(Points.Count - 1); InvalidateVisual(); e.Handled = true; }
    }
    public override void Render(DrawingContext context)
    {
        if (!Active) return;
        var accent = EditorWindow.AccentBrush; var pen = new Pen(accent, 1.5);
        var screen = Points.Select(owner.ClipToScreenPoint).ToList();
        for (int i = 1; i < screen.Count; i++) context.DrawLine(pen, screen[i - 1], screen[i]);
        if (Closed && screen.Count >= 3) context.DrawLine(pen, screen[^1], screen[0]);
        for (int i = 0; i < screen.Count; i++)
        {
            var r = new Rect(screen[i].X - 4, screen[i].Y - 4, 8, 8);
            context.FillRectangle(i == 0 && !Closed ? accent : Brushes.White, r); context.DrawRectangle(null, new Pen(accent, 1.25), r);
        }
        if (!Closed)
        {
            var t = new FormattedText(Points.Count < 3 ? "Click to place points" : "Click the first point or press Enter to close", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
            var bg = new Rect(10, 10, t.Width + 12, t.Height + 6); context.FillRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), bg, 3); context.DrawText(t, new Point(16, 13));
        }
    }
}

public sealed partial class EditorWindow
{
    PenMaskOverlay? penOverlay;
    internal Point ClipToScreenPoint((double U, double V) p) => transformOverlay.ClipToScreen(p.U, p.V);
    internal (double U, double V) ScreenToClipPoint(Point p) => transformOverlay.ScreenToClip(p);

    internal void StartPen(bool redraw)
    {
        if (penOverlay is null || selectedClip is null) return;
        UpdateTransformOverlay();
        var clip = Sequence.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == selectedClip);
        penOverlay.Load(redraw ? null : clip?.Mask); penOverlay.Active = true; penOverlay.Focus();
        status.Text = penOverlay.Closed ? "Drag the mask points to reshape. Esc when done." : "Pen: click on the program to place mask points. Click the first point or press Enter to close.";
    }
    internal void StopPen() { if (penOverlay is null) return; penOverlay.Active = false; status.Text = "Pen mask done."; BuildInspector(); }
    internal void CommitPenMask(IReadOnlyList<(double U, double V)> points)
    {
        if (selectedClip is not { } id || points.Count < 3) return;
        var flat = points.SelectMany(p => new[] { Math.Round(p.U, 5), Math.Round(p.V, 5) }).ToList();
        Change("Pen mask", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, id, c =>
        {
            var m = c.Mask is { Shape: "path" } old ? old : new ClipMask { Shape = "path", Feather = .05 };
            c.Mask = new ClipMask { Shape = "path", Points = flat, Smooth = m.Smooth, Feather = m.Feather, Invert = m.Invert };
        }));
        status.Text = "Pen mask applied. Drag points to reshape; Esc when done.";
    }
}
