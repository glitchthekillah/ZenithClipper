using Avalonia;
using Avalonia.Media;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>Premiere's Safe Margins: action-safe (90%) and title-safe (80%) guides plus a centre mark over the program.</summary>
internal sealed class SafeMarginsOverlay : Control
{
    public SafeMarginsOverlay() { IsHitTestVisible = false; IsVisible = false; }
    public int FrameWidth { get; set; } = 1920;
    public int FrameHeight { get; set; } = 1080;
    public override void Render(DrawingContext context)
    {
        double scale = Math.Min(Bounds.Width / Math.Max(1, FrameWidth), Bounds.Height / Math.Max(1, FrameHeight));
        double w = FrameWidth * scale, h = FrameHeight * scale; var pic = new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
        var shadow = new Pen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), 3); var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1);
        foreach (double f in new[] { .9, .8 })
        {
            var r = pic.Deflate(new Thickness(w * (1 - f) / 2, h * (1 - f) / 2));
            context.DrawRectangle(null, shadow, r); context.DrawRectangle(null, pen, r);
        }
        var c = pic.Center; double arm = Math.Min(w, h) * .03;
        foreach (var p in new[] { shadow, pen }) { context.DrawLine(p, new Point(c.X - arm, c.Y), new Point(c.X + arm, c.Y)); context.DrawLine(p, new Point(c.X, c.Y - arm), new Point(c.X, c.Y + arm)); }
    }
}

public sealed partial class EditorWindow
{
    readonly SafeMarginsOverlay safeMargins = new();
    /// <summary>Burned-in-style timecode over the program monitor (preview only).</summary>
    readonly Avalonia.Controls.TextBlock timecodeOverlay = new() { IsVisible = false, IsHitTestVisible = false, FontSize = 15, Foreground = Avalonia.Media.Brushes.White, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(170, 0, 0, 0)), Padding = new Avalonia.Thickness(8, 3), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, Margin = new Avalonia.Thickness(0, 0, 0, 14), FontFamily = new Avalonia.Media.FontFamily("Consolas, Menlo, monospace") };
    int previewResolution;
    int PreviewWidth()
    {
        int width = Sequence.Width;
        return previewResolution switch
        {
            1 => Math.Min(1920, width),
            2 => Math.Clamp(width / 2, 160, 960),
            3 => Math.Clamp(width / 4, 160, 480),
            _ => Math.Min(width, width > 1920 || timelineFps.Value > 50 ? 640 : 960)
        };
    }
    Avalonia.Controls.ComboBox PlaybackResolution()
    {
        var box = new Avalonia.Controls.ComboBox { ItemsSource = new[] { "Auto", "Full", "1/2", "1/4" }, SelectedIndex = previewResolution, FontSize = 11.5, MinHeight = 26, Height = 26, Width = 70, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 4, 0) };
        Avalonia.Controls.ToolTip.SetTip(box, "Playback resolution. Lower is smoother on big clips; export is always full quality.");
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            previewResolution = box.SelectedIndex;
            graphRevision++; Refresh(true);
        };
        return box;
    }
}
