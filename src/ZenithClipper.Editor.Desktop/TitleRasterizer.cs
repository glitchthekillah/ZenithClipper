using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Essential Graphics text look beyond size and color.</summary>
internal sealed record TitleStyle(string Font = "", bool Bold = true, string Align = "Center", double Tracking = 0, double Leading = 0, string? Stroke = null, double StrokeWidth = 0, bool Shadow = false, string? GradientTo = null)
{
    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this);
    public static TitleStyle FromJson(string? json) { try { return json is null ? new() : System.Text.Json.JsonSerializer.Deserialize<TitleStyle>(json) ?? new(); } catch (System.Text.Json.JsonException) { return new(); } }
}

internal static class TitleRasterizer
{
    internal static void VerifyRasterization()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zenith-title-smoke-" + Guid.NewGuid().ToString("N"));
        MediaAsset? asset = null;
        try
        {
            asset = Create("Zenith title\nCaption α", 48, "#ffffff", 3, directory);
            using var image = new Bitmap(asset.Path);
            if (image.PixelSize.Width != asset.Width || image.PixelSize.Height != asset.Height || asset.Width > 2048 || asset.Height > 2048)
                throw new InvalidOperationException("Title dimensions do not match the compact PNG.");
            var bytes = checked(asset.Width * asset.Height * 4);
            var address = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes);
            try
            {
                image.CopyPixels(new PixelRect(0, 0, asset.Width, asset.Height), address, bytes, asset.Width * 4);
                var pixels = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(address, pixels, 0, bytes);
                var opaque = false;
                var transparent = false;
                for (var i = 3; i < pixels.Length; i += 4)
                {
                    opaque |= pixels[i] > 0;
                    transparent |= pixels[i] == 0;
                }
                if (!opaque || !transparent)
                    throw new InvalidOperationException("Title PNG must contain rendered text and transparent background pixels.");
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(address); }
            Console.WriteLine($"Title raster smoke passed: {asset.Width} × {asset.Height}, visible glyphs and transparent background.");
            var styled = Create("Styled title", 64, "#ffffff", 3, directory, null, new TitleStyle("", true, "Left", 4, 1.3, "#FF6B4A", 4, true, "#FFD54A"));
            if (styled.Width <= 0 || TitleStyle.FromJson(styled.TextStyle).Stroke != "#FF6B4A") throw new InvalidOperationException("Styled title lost its style.");
            if (Environment.GetEnvironmentVariable("ZENITH_TITLE_SAMPLE") is { } sample) File.Copy(styled.Path, sample, true);
            File.Delete(styled.Path);
        }
        finally
        {
            if (asset is not null)
                File.Delete(asset.Path);
            if (Directory.Exists(directory))
                Directory.Delete(directory);
        }
    }

    public static MediaAsset Create(string text, double fontSize, string color, double duration, string directory, string? background = null, TitleStyle? style = null)
    {
        style ??= new();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000)
            throw new ArgumentException("Enter between 1 and 4,000 characters.");
        if (!double.IsFinite(fontSize) || fontSize < 12 || fontSize > 256)
            throw new ArgumentException("Font size must be between 12 and 256 pixels.");
        if (!double.IsFinite(duration) || duration <= 0 || duration > 86400)
            throw new ArgumentException("Duration must be between 0 and 86,400 seconds.");
        IBrush foreground = style.GradientTo is { Length: > 0 } to
            ? new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(color), 0), new GradientStop(Color.Parse(to), 1) } }
            : new SolidColorBrush(Color.Parse(color));
        var align = style.Align switch { "Left" => TextAlignment.Left, "Right" => TextAlignment.Right, _ => TextAlignment.Center };
        var family = string.IsNullOrWhiteSpace(style.Font) ? FontFamily.Default : new FontFamily(style.Font);
        TextBlock Label(IBrush brush) => new()
        {
            Text = text, FontSize = fontSize, Foreground = brush, FontFamily = family,
            FontWeight = style.Bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap, TextAlignment = align, MaxWidth = 1600,
            LetterSpacing = style.Tracking, LineHeight = style.Leading > 0 ? fontSize * style.Leading : double.NaN
        };
        var label = Label(foreground);
        if (style.Shadow) label.Effect = new DropShadowEffect { OffsetX = fontSize * .05, OffsetY = fontSize * .07, BlurRadius = fontSize * .18, Color = Color.FromArgb(200, 0, 0, 0) };
        Avalonia.Controls.Control content = label;
        if (style.Stroke is { Length: > 0 } strokeColor && style.StrokeWidth > 0)
        {
            // An outline: copies of the text in the stroke color, offset around a circle, under the fill.
            var stack = new Grid(); var strokeBrush = new SolidColorBrush(Color.Parse(strokeColor)); int copies = 16; double r = style.StrokeWidth;
            for (int i = 0; i < copies; i++) { double a = i * Math.PI * 2 / copies; var c = Label(strokeBrush); c.RenderTransform = new TranslateTransform(Math.Cos(a) * r, Math.Sin(a) * r); stack.Children.Add(c); }
            stack.Children.Add(label); stack.Margin = new Thickness(r); content = stack;
        }
        var box = new Border { Padding = background is null ? new Thickness(0) : new Thickness(fontSize * .45, fontSize * .2), CornerRadius = new CornerRadius(background is null ? 0 : fontSize * .2), Background = background is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(background)), Child = content, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var visual = new Border { Padding = new Thickness(20), Background = Brushes.Transparent, Child = box };
        visual.Measure(new Size(1640, double.PositiveInfinity));
        var width = Math.Max(2, (int)Math.Ceiling(visual.DesiredSize.Width));
        var height = Math.Max(2, (int)Math.Ceiling(visual.DesiredSize.Height));
        if (width > 2048 || height > 2048)
            throw new ArgumentException("This text is too tall. Split it into shorter titles or use a smaller font.");
        visual.Arrange(new Rect(0, 0, width, height));
        using var raster = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        raster.Render(visual);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"title-{Guid.NewGuid():N}.png");
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            raster.Save(output, PngBitmapEncoderOptions.Default);
        return new MediaAsset
        {
            Name = text.Length > 45 ? text[..45] + "…" : text,
            Kind = MediaKind.Text,
            Path = path,
            Text = text,
            Color = color,
            FontSize = fontSize,
            Background = background,
            TextStyle = style.ToJson(),
            DurationSeconds = duration,
            Width = width,
            Height = height
        };
    }
}
