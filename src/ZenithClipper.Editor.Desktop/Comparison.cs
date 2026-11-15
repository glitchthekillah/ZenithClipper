using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Premiere's Comparison View: a captured reference frame left of a draggable divider, the live program right of it.</summary>
internal sealed class ComparisonOverlay : Avalonia.Controls.Control, ICustomHitTest
{
    internal Bitmap? Reference; internal double Split = .5; internal int FrameWidth = 1920, FrameHeight = 1080; bool dragging;
    public ComparisonOverlay() { IsVisible = false; ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeWestEast); }
    Rect Picture()
    {
        double s = Math.Min(Bounds.Width / Math.Max(1, FrameWidth), Bounds.Height / Math.Max(1, FrameHeight)); double w = FrameWidth * s, h = FrameHeight * s;
        return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
    }
    public bool HitTest(Point p) { var r = Picture(); return IsVisible && Math.Abs(p.X - (r.X + r.Width * Split)) <= 8 && p.Y >= r.Y && p.Y <= r.Bottom; }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { dragging = true; e.Pointer.Capture(this); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { if (!dragging) return; var r = Picture(); Split = Math.Clamp((e.GetPosition(this).X - r.X) / Math.Max(1, r.Width), 0, 1); InvalidateVisual(); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { dragging = false; e.Pointer.Capture(null); }
    public override void Render(DrawingContext context)
    {
        if (Reference is null) return;
        var r = Picture(); double x = r.X + r.Width * Split;
        using (context.PushClip(new Rect(r.X, r.Y, Math.Max(0, x - r.X), r.Height))) context.DrawImage(Reference, new Rect(Reference.Size), r);
        context.DrawLine(new Pen(Brushes.White, 2), new Point(x, r.Y), new Point(x, r.Bottom));
        var grip = new Rect(x - 5, r.Y + r.Height / 2 - 14, 10, 28); context.FillRectangle(Brushes.White, grip, 3);
        void Tag(string text, double at, bool right)
        {
            var t = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
            double tx = right ? at + 8 : at - 8 - t.Width - 12; var bg = new Rect(tx, r.Y + 8, t.Width + 12, t.Height + 6);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), bg, 3); context.DrawText(t, new Point(bg.X + 6, bg.Y + 3));
        }
        if (Split > .08) Tag("Reference", x, false); if (Split < .92) Tag("Current", x, true);
    }
}

public sealed partial class EditorWindow
{
    readonly ComparisonOverlay comparison = new();
    /// <summary>Toggles Comparison View; turning it on captures the frame on screen now as the reference.</summary>
    internal void ToggleComparison(bool recapture = false)
    {
        if (comparison.IsVisible && !recapture) { comparison.IsVisible = false; status.Text = "Comparison view off."; return; }
        if (bitmap is null) { status.Text = "Nothing on the monitor to compare yet."; return; }
        using var ms = new MemoryStream(); bitmap.Save(ms, PngBitmapEncoderOptions.Default); ms.Position = 0;
        comparison.Reference?.Dispose(); comparison.Reference = new Bitmap(ms);
        comparison.FrameWidth = Sequence.Width; comparison.FrameHeight = Sequence.Height; comparison.IsVisible = true; comparison.InvalidateVisual();
        status.Text = $"Comparison view: reference captured at {Clock(playhead)}. Move the playhead or change effects; drag the divider to compare.";
    }
}
