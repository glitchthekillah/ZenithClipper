using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace ZenithClipper.Editor.Desktop;

public sealed partial class EditorWindow
{
    Grid? monitorViewport; ScrollViewer? monitorScroller;
    /// <summary>Program monitor zoom as a fraction of the sequence's pixel size; 0 means Fit.</summary>
    internal double monitorZoom;
    static readonly (string Label, double Zoom)[] MonitorZoomLevels = [("Fit", 0), ("10%", .1), ("25%", .25), ("50%", .5), ("75%", .75), ("100%", 1), ("150%", 1.5), ("200%", 2), ("400%", 4)];

    ComboBox MonitorZoom()
    {
        var box = new ComboBox { ItemsSource = MonitorZoomLevels.Select(z => z.Label).ToArray(), SelectedIndex = 0, FontSize = 11.5, MinHeight = 26, Height = 26, Width = 70, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 4, 0), Name = "monitor-zoom" };
        ToolTip.SetTip(box, "Program monitor zoom. Above Fit, scroll or drag the scroll bars to look around.");
        box.SelectionChanged += (_, _) => SetMonitorZoom(MonitorZoomLevels[Math.Max(0, box.SelectedIndex)].Zoom);
        return box;
    }

    internal void SetMonitorZoom(double zoom)
    {
        monitorZoom = zoom;
        if (monitorViewport is null || monitorScroller is null) return;
        var bar = zoom > 0 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        monitorScroller.HorizontalScrollBarVisibility = bar; monitorScroller.VerticalScrollBarVisibility = bar;
        if (zoom > 0) { monitorViewport.Width = Sequence.Width * zoom; monitorViewport.Height = Sequence.Height * zoom; monitorViewport.HorizontalAlignment = HorizontalAlignment.Center; monitorViewport.VerticalAlignment = VerticalAlignment.Center; }
        else { monitorViewport.Width = double.NaN; monitorViewport.Height = double.NaN; monitorViewport.HorizontalAlignment = HorizontalAlignment.Stretch; monitorViewport.VerticalAlignment = VerticalAlignment.Stretch; }
        // Render the preview sharp enough for the zoom: never below the on-screen size, capped at full resolution.
        if (zoom > 0) { int want = (int)Math.Min(Math.Min(1920, Sequence.Width), Math.Ceiling(Sequence.Width * zoom)); if (want > previewWidth) { previewWidth = want; graphRevision++; Refresh(true); } }
        status.Text = zoom > 0 ? $"Monitor at {zoom * 100:0}%." : "Monitor fits the panel.";
    }
}
