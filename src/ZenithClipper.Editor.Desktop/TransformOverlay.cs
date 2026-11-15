using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>
/// Premiere's direct manipulation in the Program Monitor: the selected clip gets a bounding box. Drag inside to move,
/// drag a corner to scale (both axes), an edge to stretch one axis, or just outside a corner to rotate (Shift snaps to
/// 15°). Releasing commits one undoable edit; keyframed properties get a keyframe at the playhead.
/// </summary>
internal sealed class TransformOverlay : Control, ICustomHitTest
{
    readonly EditorWindow owner;
    public TransformOverlay(EditorWindow owner) { this.owner = owner; ClipToBounds = true; }
    public record struct Values(double X, double Y, double ScaleX, double ScaleY, double Rotation);
    internal Values current, start; ClipTransform crop = new(); bool visible;
    internal int frameWidth = 1920, frameHeight = 1080;
    string? grab; Point grabStart; bool showing => visible && IsVisible;

    public void Load(Values values, ClipTransform transform, int width, int height)
    {
        if (grab is not null) return;
        current = values; crop = transform; frameWidth = Math.Max(1, width); frameHeight = Math.Max(1, height); visible = true; InvalidateVisual();
    }
    public void Hide() { visible = false; grab = null; InvalidateVisual(); }

    Rect Picture()
    {
        double scale = Math.Min(Bounds.Width / frameWidth, Bounds.Height / frameHeight);
        double w = frameWidth * scale, h = frameHeight * scale;
        return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
    }
    /// <summary>The visible (cropped) clip rectangle before rotation, matching the renderer's placement.</summary>
    Rect Box(Values v)
    {
        var p = Picture(); var g = TransformGeometry.Place(p.Width, p.Height, v.X, v.Y, v.ScaleX, v.ScaleY, v.Rotation, crop);
        return new Rect(p.X + g.Left, p.Y + g.Top, g.W, g.H);
    }
    /// <summary>Clip-frame fractions (0..1 across the clip's own uncropped frame) to monitor coordinates, and back.</summary>
    internal Point ClipToScreen(double u, double v)
    {
        var p = Picture(); var a = AnchorOnScreen(current); double w = p.Width * current.ScaleX, h = p.Height * current.ScaleY;
        var flat = new Point(a.X + (u - crop.AnchorX) * w, a.Y + (v - crop.AnchorY) * h);
        return Rotate(flat, a, current.Rotation);
    }
    internal (double U, double V) ScreenToClip(Point s)
    {
        var p = Picture(); var a = AnchorOnScreen(current); double w = Math.Max(1e-6, p.Width * current.ScaleX), h = Math.Max(1e-6, p.Height * current.ScaleY);
        var flat = Rotate(s, a, -current.Rotation);
        return (crop.AnchorX + (flat.X - a.X) / w, crop.AnchorY + (flat.Y - a.Y) / h);
    }
    Point AnchorOnScreen(Values v)
    {
        var p = Picture(); var g = TransformGeometry.Place(p.Width, p.Height, v.X, v.Y, v.ScaleX, v.ScaleY, v.Rotation, crop);
        return new Point(p.X + g.AnchorX, p.Y + g.AnchorY);
    }
    static Point Rotate(Point p, Point c, double degrees)
    {
        double r = degrees * Math.PI / 180, s = Math.Sin(r), k = Math.Cos(r), dx = p.X - c.X, dy = p.Y - c.Y;
        return new Point(c.X + dx * k - dy * s, c.Y + dx * s + dy * k);
    }
    static IEnumerable<(string Name, Point At)> Handles(Rect r)
    {
        yield return ("tl", r.TopLeft); yield return ("t", new Point(r.Center.X, r.Y)); yield return ("tr", r.TopRight);
        yield return ("l", new Point(r.X, r.Center.Y)); yield return ("r", new Point(r.Right, r.Center.Y));
        yield return ("bl", r.BottomLeft); yield return ("b", new Point(r.Center.X, r.Bottom)); yield return ("br", r.BottomRight);
    }

    /// <summary>Which part of the box a screen point is on: a handle, "move", "rotate", or null.</summary>
    string? PartAt(Point screen)
    {
        if (!showing) return null;
        var box = Box(current); if (box.Width < 1 || box.Height < 1) return null;
        var local = Rotate(screen, box.Center, -current.Rotation);
        foreach (var (name, at) in Handles(box)) if (Math.Abs(at.X - local.X) <= 7 && Math.Abs(at.Y - local.Y) <= 7) return name;
        if (box.Contains(local)) return "move";
        foreach (var corner in new[] { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight })
            if (Math.Abs(corner.X - local.X) <= 22 && Math.Abs(corner.Y - local.Y) <= 22) return "rotate";
        return null;
    }
    public bool HitTest(Point point) => PartAt(point) is not null;

    public override void Render(DrawingContext context)
    {
        if (!showing) return;
        var box = Box(current); if (box.Width < 1 || box.Height < 1) return;
        var accent = EditorWindow.AccentBrush;
        using (context.PushTransform(Matrix.CreateTranslation(-box.Center.X, -box.Center.Y) * Matrix.CreateRotation(current.Rotation * Math.PI / 180) * Matrix.CreateTranslation(box.Center.X, box.Center.Y)))
        {
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 3), box);
            context.DrawRectangle(null, new Pen(accent, 1.25), box);
            foreach (var (_, at) in Handles(box))
            {
                var h = new Rect(at.X - 4, at.Y - 4, 8, 8);
                context.FillRectangle(Brushes.White, h); context.DrawRectangle(null, new Pen(accent, 1.25), h);
            }
        }
        {
            // Anchor point crosshair: the pivot for position, scale and rotation.
            var c = AnchorOnScreen(current); var pen = new Pen(Brushes.White, 1);
            context.DrawEllipse(null, pen, c, 5, 5); context.DrawLine(pen, new Point(c.X - 8, c.Y), new Point(c.X + 8, c.Y)); context.DrawLine(pen, new Point(c.X, c.Y - 8), new Point(c.X, c.Y + 8));
        }
        if (grab is not null)
        {
            var text = new FormattedText($"Position {current.X * frameWidth:0}, {current.Y * frameHeight:0}   Scale {current.ScaleX * 100:0.#}%{(Math.Abs(current.ScaleX - current.ScaleY) > 1e-4 ? $" × {current.ScaleY * 100:0.#}%" : "")}   Rotation {current.Rotation:0.#}°",
                System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 11, Brushes.White);
            var pic = Picture(); var pill = new Rect(pic.X + 8, pic.Bottom - text.Height - 14, text.Width + 12, text.Height + 6);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 16, 17, 19)), pill); context.DrawText(text, new Point(pill.X + 6, pill.Y + 3));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this); grab = PartAt(p); if (grab is null) return;
        grabStart = p; start = current; e.Pointer.Capture(this); e.Handled = true; InvalidateVisual();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (grab is null)
        {
            var part = PartAt(p);
            Cursor = part is null ? null : new Cursor(part switch { "move" => StandardCursorType.SizeAll, "rotate" => StandardCursorType.Hand, "l" or "r" => StandardCursorType.SizeWestEast, "t" or "b" => StandardCursorType.SizeNorthSouth, "tl" or "br" => StandardCursorType.TopLeftCorner, _ => StandardCursorType.TopRightCorner });
            return;
        }
        current = Drag(start, grab, grabStart, p, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        InvalidateVisual(); e.Handled = true;
    }
    internal Values Drag(Values origin, string part, Point a, Point b, bool shift)
    {
        var pic = Picture(); var box = Box(origin); var c = box.Center;
        switch (part)
        {
            case "move":
            {
                double x = origin.X + (b.X - a.X) / pic.Width, y = origin.Y + (b.Y - a.Y) / pic.Height;
                // Snap the clip centre to the frame centre lines, like Premiere's snapping in the monitor.
                double snap = 6 / pic.Width; if (Math.Abs(x) < snap) x = 0; if (Math.Abs(y) < 6 / pic.Height) y = 0;
                if (shift) { if (Math.Abs(b.X - a.X) > Math.Abs(b.Y - a.Y)) y = origin.Y; else x = origin.X; }
                return origin with { X = x, Y = y };
            }
            case "rotate":
            {
                double angle = origin.Rotation + (Math.Atan2(b.Y - c.Y, b.X - c.X) - Math.Atan2(a.Y - c.Y, a.X - c.X)) * 180 / Math.PI;
                angle = Math.Round(angle, 1); if (shift) angle = Math.Round(angle / 15) * 15;
                return origin with { Rotation = angle };
            }
            default:
            {
                // Work in the box's own axes so handles behave the same when the clip is rotated.
                var la = Rotate(a, c, -origin.Rotation); var lb = Rotate(b, c, -origin.Rotation);
                double fx = Math.Abs(la.X - c.X) > 1 ? Math.Abs(lb.X - c.X) / Math.Abs(la.X - c.X) : 1;
                double fy = Math.Abs(la.Y - c.Y) > 1 ? Math.Abs(lb.Y - c.Y) / Math.Abs(la.Y - c.Y) : 1;
                if (part is "l" or "r") return origin with { ScaleX = Math.Clamp(origin.ScaleX * fx, .01, 20) };
                if (part is "t" or "b") return origin with { ScaleY = Math.Clamp(origin.ScaleY * fy, .01, 20) };
                double d0 = Math.Sqrt((la.X - c.X) * (la.X - c.X) + (la.Y - c.Y) * (la.Y - c.Y)), d1 = Math.Sqrt((lb.X - c.X) * (lb.X - c.X) + (lb.Y - c.Y) * (lb.Y - c.Y));
                double f = d0 > 1 ? d1 / d0 : 1;
                return origin with { ScaleX = Math.Clamp(Math.Round(origin.ScaleX * f, 4), .01, 20), ScaleY = Math.Clamp(Math.Round(origin.ScaleY * f, 4), .01, 20) };
            }
        }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); if (grab is null) return;
        grab = null; e.Pointer.Capture(null); InvalidateVisual();
        if (current != start) owner.ApplyMotion(start, current);
    }
}

public sealed partial class EditorWindow
{
    /// <summary>Shows handles for the selected video clip while the program playhead is over it and playback is paused.</summary>
    internal void UpdateTransformOverlay()
    {
        if (transformOverlay is null) return;
        var sequence = Sequence;
        var hit = sequence.Tracks.SelectMany(t => t.Clips.Select(c => (Track: t, Clip: c))).FirstOrDefault(x => x.Clip.Id == selectedClip);
        long ph = ProgramPlayhead;
        if (hit.Clip is not { } clip || hit.Track.Kind != TrackKind.Video || hit.Track.Hidden || playing || sourceMode || cropOverlay.Active || ph < clip.Start || ph >= clip.End)
        { transformOverlay.Hide(); return; }
        long at = ph - clip.Start; var t = clip.Transform;
        transformOverlay.Load(new(ClipBlend.At(clip, "x", at) ?? t.X, ClipBlend.At(clip, "y", at) ?? t.Y, ClipBlend.At(clip, "scale_x", at) ?? t.ScaleX, ClipBlend.At(clip, "scale_y", at) ?? t.ScaleY, ClipBlend.At(clip, "rotation", at) ?? t.Rotation), t, sequence.Width, sequence.Height);
    }
}

public sealed partial class EditorWindow
{
    /// <summary>On-monitor move/scale/rotate math and commit (static vs keyframed).</summary>
    internal void VerifyTransformOverlay()
    {
        var o = new TransformOverlay(this); o.Measure(new Size(1600, 900)); o.Arrange(new Rect(0, 0, 1600, 900));
        o.Load(new(0, 0, .5, .5, 0), new ClipTransform(), 1600, 900);
        var v = new TransformOverlay.Values(0, 0, .5, .5, 0);
        var moved = o.Drag(v, "move", new Point(800, 450), new Point(960, 360), false);
        if (Math.Abs(moved.X - .1) > 1e-9 || Math.Abs(moved.Y + .1) > 1e-9) throw new InvalidOperationException("Monitor move did not map to position.");
        var scaled = o.Drag(v, "br", new Point(1200, 675), new Point(1600, 900), false);
        if (Math.Abs(scaled.ScaleX - 1) > 1e-3 || Math.Abs(scaled.ScaleY - 1) > 1e-3) throw new InvalidOperationException("Corner drag did not scale uniformly.");
        var wide = o.Drag(v, "r", new Point(1200, 450), new Point(1400, 450), false);
        if (Math.Abs(wide.ScaleX - .75) > 1e-3 || wide.ScaleY != .5) throw new InvalidOperationException("Edge drag did not stretch one axis.");
        var turned = o.Drag(v, "rotate", new Point(1300, 450), new Point(800, 950), true);
        if (Math.Abs(turned.Rotation - 90) > 1e-9) throw new InvalidOperationException("Rotate drag did not turn clockwise / snap.");
        // A rotated box: dragging its (rotated) right edge outward still stretches width.
        var r90 = v with { Rotation = 90 };
        var tall = o.Drag(r90, "r", new Point(800, 850), new Point(800, 950), false);
        if (Math.Abs(tall.ScaleX - .625) > 1e-3) throw new InvalidOperationException("Handles ignore rotation.");

        var fps = new FrameRate(30, 1);
        var media = new MediaAsset { Name = "m", Kind = MediaKind.Video, DurationSeconds = 20, Width = 1920, Height = 1080, Fps = fps, Path = "/x.mp4" };
        var seq = new EditSequence { Fps = fps, Tracks = [new EditTrack { Kind = TrackKind.Video, Name = "V1" }] };
        var clip = new TimelineClip { MediaId = media.Id, Duration = 100, StreamIntent = ClipStreamIntent.VideoOnly };
        clip.Motion.Add(new Keyframe { Parameter = "rotation", Frame = 0, Value = 0 }); seq.Tracks[0].Clips.Add(clip);
        var keep = session; var keepSel = selectedClip; var keepPh = playhead;
        try
        {
            session = new EditorSession(new EditProject { ActiveSequenceId = seq.Id, Sequences = [seq], Media = [media] }); selectedClip = clip.Id; playhead = 40;
            ApplyMotion(new(0, 0, 1, 1, 0), new(.25, 0, 1, 1, 45));
            var c = Sequence.Tracks[0].Clips[0];
            if (c.Transform.X != .25 || c.Transform.Rotation != 0 || !c.Motion.Any(k => k.Parameter == "rotation" && k.Frame == 40 && k.Value == 45))
                throw new InvalidOperationException("Monitor edit did not set static vs keyframed values correctly.");
        }
        finally { session = keep; selectedClip = keepSel; playhead = keepPh; Refresh(); }
        Console.WriteLine("Transform overlay smoke passed: move, uniform/edge scale, rotation snap, rotated handles, keyframe-aware commit.");
    }
}
