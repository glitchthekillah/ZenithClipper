using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Essential Graphics shapes: a rectangle or ellipse drawn at sequence size (transparent around it), moved and scaled on the monitor.</summary>
public sealed partial class EditorWindow
{
    internal static string RenderShape(string kind, string fill, string? stroke, int width, int height, string directory)
    {
        var size = new PixelSize(Math.Clamp(width, 16, 3840), Math.Clamp(height, 16, 2160));
        using var raster = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var ctx = raster.CreateDrawingContext())
        {
            double w = size.Width * .4, h = size.Height * .4; var r = new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h);
            IBrush brush = new SolidColorBrush(Color.Parse(fill)); IPen? pen = stroke is null ? null : new Pen(new SolidColorBrush(Color.Parse(stroke)), Math.Max(2, size.Height / 120.0));
            if (kind == "ellipse") ctx.DrawEllipse(brush, pen, r); else ctx.DrawRectangle(brush, pen, r, kind == "rounded" ? Math.Min(w, h) * .12 : 0);
        }
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"shape-{kind}-{Guid.NewGuid():N}.png");
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) raster.Save(output, PngBitmapEncoderOptions.Default);
        return path;
    }

    void AddShape(string kind, string name)
    {
        var path = RenderShape(kind, "#FF6B4A", null, Sequence.Width, Sequence.Height, AssetDirectory);
        if (PlaceNewItem(new MediaAsset { Name = name, Kind = MediaKind.Image, Path = path }))
            status.Text = $"{name} added. Drag it on the monitor to move or scale it.";
    }

    /// <summary>Inspector: recolor a shape (fill and optional stroke).</summary>
    void ShapeControls(Avalonia.Controls.StackPanel host, MediaAsset media)
    {
        var file = Path.GetFileName(media.Path);
        if (media.Kind != MediaKind.Image || !file.StartsWith("shape-", StringComparison.Ordinal)) return;
        string kind = file.Split('-')[1]; var id = media.Id; int w = media.Width, h = media.Height;
        var fill = new Avalonia.Controls.TextBox { Text = media.Color ?? "#FF6B4A", FontSize = 11.5, Height = 26, MinHeight = 26 };
        var stroke = new Avalonia.Controls.TextBox { Text = media.Background ?? "", PlaceholderText = "stroke: none", FontSize = 11.5, Height = 26, MinHeight = 26 };
        void Apply()
        {
            string f = (fill.Text ?? "").Trim(), st = (stroke.Text ?? "").Trim();
            if (!Color.TryParse(f, out _) || (st.Length > 0 && !Color.TryParse(st, out _))) { status.Text = "Use colors like #FF6B4A."; return; }
            if (f == (media.Color ?? "#FF6B4A") && st == (media.Background ?? "")) return;
            var path = RenderShape(kind, f, st.Length == 0 ? null : st, w, h, AssetDirectory);
            Change("Shape color", p => { var m = p.Media.First(x => x.Id == id); m.Path = path; m.Color = f; m.Background = st.Length == 0 ? null : st; });
        }
        fill.LostFocus += (_, _) => Apply(); stroke.LostFocus += (_, _) => Apply();
        host.Children.Add(Copy("Fill", 11, FaintBrush)); host.Children.Add(fill); host.Children.Add(Copy("Stroke", 11, FaintBrush)); host.Children.Add(stroke);
    }
}
