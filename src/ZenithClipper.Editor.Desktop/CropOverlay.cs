using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>
/// Crop handles drawn over the program monitor, like Premiere's Crop effect: drag any edge or corner of the selected
/// clip and the area outside dims while you drag; releasing applies the crop as one undoable edit.
/// </summary>
internal sealed class CropOverlay : Control
{
    readonly EditorWindow owner;
    public CropOverlay(EditorWindow owner) { this.owner = owner; IsHitTestVisible = false; ClipToBounds = true; }
    public Guid? ClipId; public int FrameWidth = 1920, FrameHeight = 1080;
    public ClipTransform? Transform;
    double left, right, top, bottom; string? grab; Point grabStart; (double L, double R, double T, double B) startCrop;
    public bool Active { get => IsHitTestVisible; set { IsHitTestVisible = value; Cursor = value ? new Cursor(StandardCursorType.Cross) : null; InvalidateVisual(); } }

    public void Load(ClipTransform transform, int frameWidth, int frameHeight)
    {
        Transform = transform; FrameWidth = Math.Max(1, frameWidth); FrameHeight = Math.Max(1, frameHeight);
        if (grab is null) (left, right, top, bottom) = (transform.CropLeft, transform.CropRight, transform.CropTop, transform.CropBottom);
        InvalidateVisual();
    }

    Rect Picture()
    {
        double scale = Math.Min(Bounds.Width / FrameWidth, Bounds.Height / FrameHeight);
        double w = FrameWidth * scale, h = FrameHeight * scale;
        return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
    }
    /// <summary>Where the clip's full (uncropped) frame sits on screen.</summary>
    Rect ClipRect()
    {
        var p = Picture(); var t = Transform ?? new ClipTransform();
        double w = p.Width * t.ScaleX, h = p.Height * t.ScaleY;
        return new Rect(p.X + p.Width / 2 + t.X * p.Width - t.AnchorX * w, p.Y + p.Height / 2 + t.Y * p.Height - t.AnchorY * h, w, h);
    }
    Rect Kept(Rect c) => new(c.X + c.Width * left, c.Y + c.Height * top, c.Width * Math.Max(0, 1 - left - right), c.Height * Math.Max(0, 1 - top - bottom));

    public override void Render(DrawingContext context)
    {
        if (!Active || Transform is null) return;
        var picture = Picture(); var clip = ClipRect(); var kept = Kept(clip);
        // Dim what will be cut away.
        var dim = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        using (context.PushClip(picture))
        {
            context.FillRectangle(dim, new Rect(clip.X, clip.Y, clip.Width, kept.Y - clip.Y));
            context.FillRectangle(dim, new Rect(clip.X, kept.Bottom, clip.Width, clip.Bottom - kept.Bottom));
            context.FillRectangle(dim, new Rect(clip.X, kept.Y, kept.X - clip.X, kept.Height));
            context.FillRectangle(dim, new Rect(kept.Right, kept.Y, clip.Right - kept.Right, kept.Height));
        }
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#66FFFFFF")), 1, new DashStyle([4, 4], 0)), clip);
        var accent = EditorWindow.AccentBrush;
        context.DrawRectangle(null, new Pen(accent, 1.5), kept);
        // Rule-of-thirds guides inside the kept area while dragging.
        if (grab is not null)
        {
            var guide = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
            for (int i = 1; i < 3; i++)
            {
                context.DrawLine(guide, new Point(kept.X + kept.Width * i / 3, kept.Y), new Point(kept.X + kept.Width * i / 3, kept.Bottom));
                context.DrawLine(guide, new Point(kept.X, kept.Y + kept.Height * i / 3), new Point(kept.Right, kept.Y + kept.Height * i / 3));
            }
        }
        foreach (var (_, at) in Handles(kept))
        {
            var box = new Rect(at.X - 5, at.Y - 5, 10, 10);
            context.FillRectangle(Brushes.White, box); context.DrawRectangle(null, new Pen(accent, 1.5), box);
        }
        if (grab is not null)
        {
            var text = new FormattedText($"L {left * 100:0.#}%  R {right * 100:0.#}%  T {top * 100:0.#}%  B {bottom * 100:0.#}%", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11, Brushes.White);
            var pill = new Rect(kept.X + 6, kept.Y + 6, text.Width + 12, text.Height + 6);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 16, 17, 19)), pill);
            context.DrawText(text, new Point(pill.X + 6, pill.Y + 3));
        }
    }

    static IEnumerable<(string Name, Point At)> Handles(Rect r)
    {
        yield return ("tl", r.TopLeft); yield return ("t", new Point(r.Center.X, r.Y)); yield return ("tr", r.TopRight);
        yield return ("l", new Point(r.X, r.Center.Y)); yield return ("r", new Point(r.Right, r.Center.Y));
        yield return ("bl", r.BottomLeft); yield return ("b", new Point(r.Center.X, r.Bottom)); yield return ("br", r.BottomRight);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (!Active || Transform is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this); var kept = Kept(ClipRect());
        grab = Handles(kept).Where(h => Math.Abs(h.At.X - p.X) <= 9 && Math.Abs(h.At.Y - p.Y) <= 9).Select(h => h.Name).FirstOrDefault()
            ?? (Math.Abs(p.X - kept.X) <= 6 && p.Y >= kept.Y && p.Y <= kept.Bottom ? "l" : Math.Abs(p.X - kept.Right) <= 6 && p.Y >= kept.Y && p.Y <= kept.Bottom ? "r"
              : Math.Abs(p.Y - kept.Y) <= 6 && p.X >= kept.X && p.X <= kept.Right ? "t" : Math.Abs(p.Y - kept.Bottom) <= 6 && p.X >= kept.X && p.X <= kept.Right ? "b"
              : kept.Contains(p) ? "move" : null);
        if (grab is null) return;
        grabStart = p; startCrop = (left, right, top, bottom); e.Pointer.Capture(this); e.Handled = true; InvalidateVisual();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); if (!Active) return;
        var p = e.GetPosition(this); var clip = ClipRect();
        if (grab is null)
        {
            var kept = Kept(clip);
            string? over = Handles(kept).Where(h => Math.Abs(h.At.X - p.X) <= 9 && Math.Abs(h.At.Y - p.Y) <= 9).Select(h => h.Name).FirstOrDefault();
            Cursor = new Cursor(over switch { "tl" => StandardCursorType.TopLeftCorner, "tr" => StandardCursorType.TopRightCorner, "bl" => StandardCursorType.BottomLeftCorner, "br" => StandardCursorType.BottomRightCorner, "l" or "r" => StandardCursorType.SizeWestEast, "t" or "b" => StandardCursorType.SizeNorthSouth, _ => kept.Contains(p) ? StandardCursorType.SizeAll : StandardCursorType.Cross });
            return;
        }
        double fx = Math.Clamp((p.X - clip.X) / clip.Width, 0, 1), fy = Math.Clamp((p.Y - clip.Y) / clip.Height, 0, 1);
        const double min = .01;
        if (grab == "move")
        {
            // Drag inside: slide the crop window without changing its size.
            double dx = (p.X - grabStart.X) / clip.Width, dy = (p.Y - grabStart.Y) / clip.Height;
            dx = Math.Clamp(dx, -startCrop.L, startCrop.R); dy = Math.Clamp(dy, -startCrop.T, startCrop.B);
            (left, right, top, bottom) = (startCrop.L + dx, startCrop.R - dx, startCrop.T + dy, startCrop.B - dy);
        }
        else
        {
            if (grab.Contains('l')) left = Math.Clamp(fx, 0, 1 - right - min);
            if (grab.Contains('r')) right = Math.Clamp(1 - fx, 0, 1 - left - min);
            if (grab.Contains('t')) top = Math.Clamp(fy, 0, 1 - bottom - min);
            if (grab.Contains('b')) bottom = Math.Clamp(1 - fy, 0, 1 - top - min);
        }
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); if (grab is null) return;
        grab = null; e.Pointer.Capture(null); InvalidateVisual();
        if ((left, right, top, bottom) != startCrop) owner.ApplyCrop(Math.Round(left, 4), Math.Round(right, 4), Math.Round(top, 4), Math.Round(bottom, 4));
    }
}
