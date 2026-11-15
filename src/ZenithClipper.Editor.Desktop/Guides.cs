using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Program monitor rulers and guides (Premiere's View → Show Rulers / Show Guides). Drag out of a ruler to make a
/// guide, drag a guide to move it, drag it off the picture to remove it. Guides are saved with the sequence.</summary>
internal sealed class GuidesOverlay : Avalonia.Controls.Control, ICustomHitTest
{
    const double Ruler = 16;
    readonly EditorWindow owner;
    internal bool ShowRulers, ShowGuides = true, Locked;
    internal int FrameWidth = 1920, FrameHeight = 1080;
    int dragging = -1; bool newVertical; MonitorGuide? fresh;
    public GuidesOverlay(EditorWindow owner) { this.owner = owner; ClipToBounds = true; }
    List<MonitorGuide> Guides => owner.CurrentGuides;
    Rect Picture()
    {
        double s = Math.Min(Bounds.Width / Math.Max(1, FrameWidth), Bounds.Height / Math.Max(1, FrameHeight)); double w = FrameWidth * s, h = FrameHeight * s;
        return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
    }
    int Near(Point p)
    {
        if (!ShowGuides || Locked) return -1; var r = Picture(); var g = Guides;
        for (int i = 0; i < g.Count; i++) { double at = g[i].Vertical ? r.X + g[i].Position * r.Width : r.Y + g[i].Position * r.Height; if (Math.Abs((g[i].Vertical ? p.X : p.Y) - at) <= 4) return i; }
        return -1;
    }
    bool InRuler(Point p, out bool vertical)
    {
        var r = Picture(); vertical = false; if (!ShowRulers || Locked) return false;
        if (p.Y >= r.Y && p.Y <= r.Y + Ruler && p.X >= r.X && p.X <= r.Right) { vertical = false; return true; }
        if (p.X >= r.X && p.X <= r.X + Ruler && p.Y >= r.Y && p.Y <= r.Bottom) { vertical = true; return true; }
        return false;
    }
    public bool HitTest(Point p) => IsVisible && !owner.PenActive && (dragging >= 0 || Near(p) >= 0 || InRuler(p, out _));
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (dragging < 0) { int n = Near(p); Cursor = n >= 0 ? new Cursor(Guides[n].Vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth) : InRuler(p, out var v) ? new Cursor(v ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth) : null; return; }
        var r = Picture(); var guide = fresh ?? Guides[dragging];
        guide.Position = guide.Vertical ? (p.X - r.X) / Math.Max(1, r.Width) : (p.Y - r.Y) / Math.Max(1, r.Height);
        InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this); int n = Near(p);
        if (n >= 0) { dragging = n; fresh = new MonitorGuide { Vertical = Guides[n].Vertical, Position = Guides[n].Position }; }
        else if (InRuler(p, out var v)) { newVertical = v; dragging = int.MaxValue; fresh = new MonitorGuide { Vertical = v, Position = 0 }; }
        else return;
        e.Pointer.Capture(this); e.Handled = true; InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (dragging < 0 || fresh is null) return;
        int index = dragging; var g = fresh; dragging = -1; fresh = null; e.Pointer.Capture(null);
        bool gone = g.Position < 0 || g.Position > 1;
        owner.ChangeGuides(list =>
        {
            if (index != int.MaxValue && index < list.Count) list.RemoveAt(index);
            if (!gone) list.Add(new MonitorGuide { Vertical = g.Vertical, Position = Math.Round(g.Position, 5) });
        }, gone ? "Remove guide" : index == int.MaxValue ? "Add guide" : "Move guide");
    }
    public override void Render(DrawingContext context)
    {
        var r = Picture(); var cyan = new SolidColorBrush(Color.Parse("#3FC8E8"));
        if (ShowGuides)
        {
            var pen = new Pen(cyan, 1);
            var list = Guides.Select((g, i) => (g, i)).Where(x => fresh is null || x.i != dragging).Select(x => x.g).ToList(); if (fresh is not null) list.Add(fresh);
            foreach (var g in list)
            {
                if (g.Vertical) { double x = Math.Round(r.X + g.Position * r.Width) + .5; context.DrawLine(pen, new Point(x, r.Y), new Point(x, r.Bottom)); }
                else { double y = Math.Round(r.Y + g.Position * r.Height) + .5; context.DrawLine(pen, new Point(r.X, y), new Point(r.Right, y)); }
            }
            if (fresh is not null)
            {
                string label = fresh.Vertical ? $"X {Math.Round(fresh.Position * FrameWidth)}" : $"Y {Math.Round(fresh.Position * FrameHeight)}";
                var t = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), new Rect(r.X + 20, r.Y + 20, t.Width + 10, t.Height + 4), 3); context.DrawText(t, new Point(r.X + 25, r.Y + 22));
            }
        }
        if (!ShowRulers) return;
        var bg = new SolidColorBrush(Color.FromArgb(200, 20, 21, 24)); var tick = new Pen(new SolidColorBrush(Color.FromArgb(200, 220, 220, 220)), 1);
        context.FillRectangle(bg, new Rect(r.X, r.Y, r.Width, Ruler)); context.FillRectangle(bg, new Rect(r.X, r.Y, Ruler, r.Height));
        double step = new[] { 10, 20, 50, 100, 200, 500, 1000 }.First(s => s * r.Width / FrameWidth >= 40 || s == 1000);
        for (double v = 0; v <= FrameWidth; v += step / 5)
        {
            double x = r.X + v * r.Width / FrameWidth; bool major = Math.Abs(v % step) < 1e-6;
            context.DrawLine(tick, new Point(x + .5, r.Y + (major ? 4 : 11)), new Point(x + .5, r.Y + Ruler));
            if (major && v > 0) context.DrawText(new FormattedText(v.ToString("0"), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 8.5, Brushes.Gainsboro), new Point(x + 2, r.Y + 1));
        }
        for (double v = 0; v <= FrameHeight; v += step / 5)
        {
            double y = r.Y + v * r.Height / FrameHeight; bool major = Math.Abs(v % step) < 1e-6;
            context.DrawLine(tick, new Point(r.X + (major ? 4 : 11), y + .5), new Point(r.X + Ruler, y + .5));
        }
    }
}

public sealed partial class EditorWindow
{
    GuidesOverlay? guidesOverlay;
    internal bool PenActive => penOverlay?.Active == true;
    internal List<MonitorGuide> CurrentGuides => session.ActiveGuides;
    internal void ChangeGuides(Action<List<MonitorGuide>> edit, string label)
    {
        Change(label, p => edit(p.Sequences.First(s => s.Id == p.ActiveSequenceId).Guides));
        guidesOverlay?.InvalidateVisual();
    }
    void RefreshGuides() { if (guidesOverlay is null) return; guidesOverlay.FrameWidth = Sequence.Width; guidesOverlay.FrameHeight = Sequence.Height; guidesOverlay.InvalidateVisual(); }
    void ToggleRulers() { if (guidesOverlay is null) return; guidesOverlay.ShowRulers = !guidesOverlay.ShowRulers; RefreshGuides(); status.Text = guidesOverlay.ShowRulers ? "Rulers on. Drag from a ruler to add a guide." : "Rulers off."; }
    void ToggleGuides() { if (guidesOverlay is null) return; guidesOverlay.ShowGuides = !guidesOverlay.ShowGuides; RefreshGuides(); status.Text = guidesOverlay.ShowGuides ? "Guides shown." : "Guides hidden."; }
    void ToggleLockGuides() { if (guidesOverlay is null) return; guidesOverlay.Locked = !guidesOverlay.Locked; status.Text = guidesOverlay.Locked ? "Guides locked." : "Guides unlocked."; }
    void AddGuide(bool vertical) { ChangeGuides(l => l.Add(new MonitorGuide { Vertical = vertical, Position = .5 }), "Add guide"); if (guidesOverlay is not null) guidesOverlay.ShowGuides = true; RefreshGuides(); }
    void ClearGuides() { if (Sequence.Guides.Count == 0) { status.Text = "No guides to clear."; return; } ChangeGuides(l => l.Clear(), "Clear guides"); }

    internal void VerifyGuides()
    {
        int before = Sequence.Guides.Count;
        AddGuide(true); if (Sequence.Guides.Count != before + 1 || Sequence.Guides[^1].Position != .5) throw new InvalidOperationException("Guide was not added.");
        Undo(); if (Sequence.Guides.Count != before) throw new InvalidOperationException("Guide add did not undo.");
    }
}
