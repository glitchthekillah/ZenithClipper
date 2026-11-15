using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VectorPath = Avalonia.Controls.Shapes.Path;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>
/// Editor shell in the ZenithClipper look: neutral greys, one orange-red accent, flat panels split by hairlines.
/// Title bar · media | program monitor | inspector · timeline · status bar.
/// </summary>
public sealed partial class EditorWindow
{
    static IBrush Brush(string hex) => Avalonia.Media.Brush.Parse(hex);
    // Theme brushes are mutable so Settings > Appearance (in the panel) recolours the open editor live; see ApplyLook.
    static SolidColorBrush Mutable(string hex) => new(Color.Parse(hex));
    internal static readonly SolidColorBrush CanvasBrush = Mutable("#0F1012"), ChromeBrush = Mutable("#131416"), PanelBrush = Mutable("#16171A"), ControlBrush = Mutable("#1D1F23"),
        HoverBrush = Mutable("#25282D"), SelectedBrush = Mutable("#2E3137"), LineBrush = Mutable("#24262B"), BorderLineBrush = Mutable("#2A2D33"), DeepBrush = Mutable("#0B0C0E"), GraphBrush = Mutable("#15171A"),
        AccentBrush = Mutable("#FF6B4A"), AccentSoftBrush = Mutable("#FF8A6E"), AccentHoverBrush = Mutable("#FF8466"), AccentPressedBrush = Mutable("#E85A3B"),
        AccentTintBrush = Mutable("#33FF6B4A"), AccentGhostBrush = Mutable("#40FF6B4A"), InkBrush = Mutable("#1A0D08");
    static readonly IBrush TextBrush = Brush("#E8E9EC"), SoftTextBrush = Brush("#C9CCD3"), MutedBrush = Brush("#9A9EA8"), FaintBrush = Brush("#6B6F78"), AmberBrush = Brush("#F5A524");
    static readonly GradientStops OrbStops = [new GradientStop(Color.Parse("#FFD9CB"), 0), new GradientStop(Color.Parse("#FF8A5C"), .32), new GradientStop(Color.Parse("#E5432A"), .7), new GradientStop(Color.Parse("#5A1D14"), 1)];
    internal static ZenithClipper.Appearance Look { get; private set; } = new();
    internal static Avalonia.Themes.Fluent.ColorPaletteResources? FluentPalette;

    static FileSystemWatcher? lookWatcher;
    /// <summary>The panel saves appearance.ini when you pick colours; follow it so the open editor changes too.</summary>
    internal static void WatchLook()
    {
        try
        {
            Directory.CreateDirectory(ZenithClipper.AppPaths.Root);
            lookWatcher = new FileSystemWatcher(ZenithClipper.AppPaths.Root, "appearance.ini") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime };
            void Changed() => Avalonia.Threading.Dispatcher.UIThread.Post(() => { var next = ZenithClipper.Appearance.Load(); if (next != Look) ApplyLook(next); });
            lookWatcher.Changed += (_, _) => Changed(); lookWatcher.Created += (_, _) => Changed(); lookWatcher.Renamed += (_, _) => Changed();
            lookWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }
    /// <summary>Recolours every theme brush (and so every control using one) for an appearance.</summary>
    internal static void ApplyLook(ZenithClipper.Appearance look)
    {
        Look = look;
        Color C((byte R, byte G, byte B) c, byte a = 255) => Color.FromArgb(a, c.R, c.G, c.B);
        Color N(byte r, byte g, byte b) => C(look.Neutral(r, g, b));
        CanvasBrush.Color = N(15, 16, 18); ChromeBrush.Color = N(19, 20, 22); PanelBrush.Color = N(22, 23, 26); ControlBrush.Color = N(29, 31, 35);
        HoverBrush.Color = N(37, 40, 45); SelectedBrush.Color = N(46, 49, 55); LineBrush.Color = N(36, 38, 43); BorderLineBrush.Color = N(42, 45, 51);
        DeepBrush.Color = N(11, 12, 14); GraphBrush.Color = N(21, 23, 26);
        var accent = look.AccentRgb; (byte, byte, byte) white = (255, 255, 255), black = (0, 0, 0);
        AccentBrush.Color = C(accent); AccentSoftBrush.Color = C(ZenithClipper.Appearance.Mix(accent, white, .2));
        AccentHoverBrush.Color = C(ZenithClipper.Appearance.Mix(accent, white, .12)); AccentPressedBrush.Color = C(ZenithClipper.Appearance.Mix(accent, black, .1));
        AccentTintBrush.Color = C(accent, 0x33); AccentGhostBrush.Color = C(accent, 0x40); InkBrush.Color = C(look.InkRgb);
        OrbStops[0].Color = C(ZenithClipper.Appearance.Mix(accent, white, .75)); OrbStops[1].Color = C(ZenithClipper.Appearance.Mix(accent, white, .16));
        OrbStops[2].Color = C(ZenithClipper.Appearance.Mix(accent, black, .12)); OrbStops[3].Color = C(ZenithClipper.Appearance.Mix(accent, black, .65));
        if (FluentPalette is { } palette) { palette.Accent = C(accent); palette.RegionColor = N(22, 23, 26); }
    }
    const int TrackHeaderWidth = 180, RulerHeight = 26;
    /// <summary>Lane height; Ctrl+= and Ctrl+- grow and shrink every track (Premiere expand/minimize tracks).</summary>
    internal static int TrackHeight { get; set; } = 52;
    readonly TextBlock projectHeading = new(), saveState = new(), sequenceHeading = new(), profileHeading = new(), mediaCount = new(), durationHeading = new();
    readonly TextBox mediaSearch = new() { PlaceholderText = "Search clips", Margin = new Thickness(12, 10, 12, 0), Height = 30 };
    readonly Dictionary<string, bool> inspectorSections = [];
    string inspectorTab = "Video", libraryFilter = "Media", binFilter = "All bins";
    readonly ComboBox binPicker = new() { IsVisible = false }; bool updatingBins;
    double mediaCardWidth = 114;
    Grid workspace = null!;
    ScrollViewer timelineScroller = null!, propertiesScroller = null!;
    bool timeFieldsFrames;
    internal bool HeadlessVerification { get; set; }
    Border libraryNoMatches = null!;
    Border libraryEmpty = null!, previewEmpty = null!;
    Grid sourceAudioView = null!; readonly Waveform sourceWave = new(); readonly TextBlock sourceAudioName = new();
    Button appendMedia = null!, playTransport = null!, exportPrimary = null!, undoButton = null!, redoButton = null!;
    readonly List<Button> frameNavigation = [];
    readonly StackPanel libraryTabs = new() { Orientation = Orientation.Horizontal, Spacing = 2 }; StackPanel? rightTabStrip; Grid? librarySearchRow;
    StackPanel? effectTarget = null;
    bool lastTransportPlaying;

    static TextBlock Copy(string text, double size = 12, IBrush? color = null, FontWeight? weight = null) => new() { Text = text, FontSize = size, Foreground = color ?? TextBrush, FontWeight = weight ?? FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    static Border Frame(Control child, Thickness? padding = null) => new() { Child = child, Background = PanelBrush, Padding = padding ?? new Thickness(0), ClipToBounds = true };
    static Border Hairline(bool vertical) => vertical ? new Border { Width = 1, Background = LineBrush } : new Border { Height = 1, Background = LineBrush };
    static Border Separator() => new() { Width = 1, Height = 18, Background = BorderLineBrush, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
    // Icon outlines follow Lucide (ISC licence): a 24×24 grid, 2px round strokes. Every icon is drawn on the full grid inside a
    // Viewbox, so all of them centre the same way at any size instead of being stretched to their own bounds.
    static string Circle(double cx, double cy, double r) => FormattableString.Invariant($"M{cx - r},{cy} a{r},{r} 0 1,0 {2 * r},0 a{r},{r} 0 1,0 {-2 * r},0");
    static readonly Dictionary<string, (string[] Paths, bool Filled)> Icons = new()
    {
        ["import"] = (["M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "M7 10l5 5 5-5", "M12 15V3"], false),
        ["export"] = (["M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "M17 8l-5-5-5 5", "M12 3v12"], false),
        ["play"] = (["M8 5.5v13a1 1 0 0 0 1.5.86l10.5-6.5a1 1 0 0 0 0-1.72L9.5 4.64A1 1 0 0 0 8 5.5z"], true),
        ["pause"] = (["M7 4h3a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z", "M14 4h3a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-3a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z"], true),
        ["previous"] = (["M16 6.8v10.4a1 1 0 0 1-1.55.83l-7.8-5.2a1 1 0 0 1 0-1.66l7.8-5.2A1 1 0 0 1 16 6.8z"], true),
        ["next"] = (["M8 6.8v10.4a1 1 0 0 0 1.55.83l7.8-5.2a1 1 0 0 0 0-1.66l-7.8-5.2A1 1 0 0 0 8 6.8z"], true),
        ["start"] = (["M19 6.8v10.4a1 1 0 0 1-1.55.83l-7.8-5.2a1 1 0 0 1 0-1.66l7.8-5.2A1 1 0 0 1 19 6.8z", "M6 6a1 1 0 0 1 1 1v10a1 1 0 0 1-2 0V7a1 1 0 0 1 1-1z"], true),
        ["end"] = (["M5 6.8v10.4a1 1 0 0 0 1.55.83l7.8-5.2a1 1 0 0 0 0-1.66l-7.8-5.2A1 1 0 0 0 5 6.8z", "M18 6a1 1 0 0 1 1 1v10a1 1 0 0 1-2 0V7a1 1 0 0 1 1-1z"], true),
        ["undo"] = (["M9 14L4 9l5-5", "M4 9h10.5a5.5 5.5 0 0 1 0 11H11"], false),
        ["redo"] = (["M15 14l5-5-5-5", "M20 9H9.5a5.5 5.5 0 0 0 0 11H13"], false),
        ["cut"] = ([Circle(6, 6, 3), Circle(6, 18, 3), "M20 4L8.12 15.88", "M14.47 14.48L20 20", "M8.12 8.12L12 12"], false),
        ["delete"] = (["M3 6h18", "M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6", "M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2", "M10 11v6", "M14 11v6"], false),
        ["link"] = (["M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71", "M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"], false),
        ["solo"] = (["M3 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a9 9 0 0 1 18 0v7a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3"], false),
        ["diamond"] = (["M12 3.5l8.5 8.5-8.5 8.5L3.5 12z"], false),
        ["diamondfill"] = (["M12 3.5l8.5 8.5-8.5 8.5L3.5 12z"], true),
        ["pointer"] = (["M4.04 4.69a.5.5 0 0 1 .65-.65l16 6.5a.5.5 0 0 1-.06.95l-6.12 1.58a2 2 0 0 0-1.44 1.44l-1.58 6.12a.5.5 0 0 1-.95.06z"], false),
        ["forward"] = (["M6 17l5-5-5-5", "M13 17l5-5-5-5"], false),
        ["rippleedit"] = (["M7 4v16", "M7 12h11", "M14 8l4 4-4 4"], false),
        ["rolling"] = (["M12 4v16", "M8 8l-4 4 4 4", "M16 8l4 4-4 4"], false),
        ["slip"] = (["M3 6h18", "M3 18h18", "M8 12h8", "M10 9.5L7.5 12l2.5 2.5", "M14 9.5l2.5 2.5-2.5 2.5"], false),
        ["slide"] = (["M4 5v14", "M20 5v14", "M8 9h8v6H8z"], false),
        ["stretch"] = (["M2 12h20", "M6 8l-4 4 4 4", "M18 8l4 4-4 4", "M12 6v12"], false),
        ["hand"] = (["M18 11V6a2 2 0 0 0-4 0", "M14 10V4a2 2 0 0 0-4 0v2", "M10 10.5V6a2 2 0 0 0-4 0v8", "M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.86-6-2.34l-3.6-3.6a2 2 0 0 1 2.83-2.82L7 15"], false),
        ["zoom"] = ([Circle(11, 11, 8), "M21 21l-4.3-4.3", "M11 8v6", "M8 11h6"], false),
        ["fx"] = (["M15 4V2", "M15 16v-2", "M8 9h2", "M20 9h2", "M17.8 11.8L19 13", "M17.8 6.2L19 5", "M3 21l9-9", "M12.2 6.2L11 5"], false),
        ["transition"] = (["M4 4h10v10H4z", "M10 10h10v10H10z"], false),
        ["ripple"] = (["M21 12H9", "M13 8l-4 4 4 4", "M4 5v14"], false),
        ["marker"] = (["M19 21l-7-4-7 4V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"], false),
        ["audio"] = (["M11 5L6 9H2v6h4l5 4z", "M15.54 8.46a5 5 0 0 1 0 7.07", "M19.07 4.93a10 10 0 0 1 0 14.14"], false),
        ["muted"] = (["M11 5L6 9H2v6h4l5 4z", "M22 9l-6 6", "M16 9l6 6"], false),
        ["media"] = (["M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z", "M10 9l5 3-5 3z"], false),
        ["motion"] = (["M5 9l-3 3 3 3", "M9 5l3-3 3 3", "M15 19l-3 3-3-3", "M19 9l3 3-3 3", "M2 12h20", "M12 2v20"], false),
        ["chevron"] = (["M9 18l6-6-6-6"], false), ["down"] = (["M6 9l6 6 6-6"], false), ["up"] = (["M18 15l-6-6-6 6"], false),
        ["plus"] = (["M5 12h14", "M12 5v14"], false), ["minus"] = (["M5 12h14"], false),
        ["text"] = (["M4 7V4h16v3", "M9 20h6", "M12 4v16"], false),
        ["captions"] = (["M5 5h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z", "M7 15h4", "M15 15h2", "M7 11h2", "M13 11h4"], false),
        ["lock"] = (["M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z", "M7 11V7a5 5 0 0 1 10 0v4"], false),
        ["eye"] = (["M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7z", Circle(12, 12, 3)], false),
        ["hidden"] = (["M9.88 9.88a3 3 0 1 0 4.24 4.24", "M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68", "M6.61 6.61A13.53 13.53 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61", "M2 2l20 20"], false),
        ["search"] = ([Circle(11, 11, 8), "M21 21l-4.3-4.3"], false),
        ["list"] = (["M8 6h12", "M8 12h12", "M8 18h12", "M4 6h.01", "M4 12h.01", "M4 18h.01"], false),
        ["compare"] = (["M5 4h14a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z", "M12 2v20"], false),
        ["fit"] = (["M8 3H5a2 2 0 0 0-2 2v3", "M21 8V5a2 2 0 0 0-2-2h-3", "M3 16v3a2 2 0 0 0 2 2h3", "M16 21h3a2 2 0 0 0 2-2v-3"], false),
        ["menu"] = (["M4 6h16", "M4 12h16", "M4 18h16"], false),
        ["minimize"] = (["M5 12h14"], false),
        ["maximize"] = (["M6 5h12a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z"], false),
        ["restore"] = (["M8 8h10a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H8a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1z", "M10 5h9a1 1 0 0 1 1 1v9"], false),
        ["close"] = (["M6 6l12 12", "M18 6L6 18"], false),
    };
    static Control Glyph(string name, IBrush? color = null, double size = 16)
    {
        var (paths, filled) = Icons.TryGetValue(name, out var icon) ? icon : Icons["menu"];
        var brush = color ?? MutedBrush;
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var data in paths)
            canvas.Children.Add(new VectorPath { Data = StreamGeometry.Parse(data), Stroke = filled ? null : brush, Fill = filled ? brush : null, StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round });
        return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = canvas, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
    }
    static Control Orb(double size)
    {
        var orb = new Ellipse
        {
            Width = size, Height = size, Stroke = Brush("#3A3D44"), StrokeThickness = 1,
            Fill = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(.34, .28, RelativeUnit.Relative), Center = new RelativePoint(.34, .28, RelativeUnit.Relative), RadiusX = new RelativeScalar(.78, RelativeUnit.Relative), RadiusY = new RelativeScalar(.78, RelativeUnit.Relative),
                GradientStops = OrbStops
            }
        };
        return orb;
    }
    static Control CaptionGlyph(bool on) => new Border
    {
        Width = 22, Height = 15, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1.5), BorderBrush = on ? AccentSoftBrush : MutedBrush, VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = "CC", FontSize = 8.5, FontWeight = FontWeight.Bold, Foreground = on ? AccentSoftBrush : MutedBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
    };
    static Control ToolbarLabel(string icon, string label)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(Glyph(icon, SoftTextBrush, 15)); content.Children.Add(Copy(label, 12, SoftTextBrush, FontWeight.Medium));
        return content;
    }
    Button ActionButton(string icon, string label, Action action, bool primary = false, bool iconOnly = false)
    {
        var button = Button(label, action);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(Glyph(icon, primary ? InkBrush : iconOnly ? SoftTextBrush : SoftTextBrush, iconOnly ? 16 : 15));
        if (!iconOnly) content.Children.Add(Copy(label, 12, primary ? InkBrush : TextBrush, FontWeight.SemiBold));
        button.Content = content; Avalonia.Automation.AutomationProperties.SetName(button, label);
        if (primary) { button.Background = AccentBrush; button.Foreground = InkBrush; button.Classes.Add("primary"); }
        if (iconOnly) { button.Width = 30; button.Height = 30; button.Padding = new Thickness(0); button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center; button.Background = Brushes.Transparent; button.Classes.Add("subtle"); }
        ToolTip.SetTip(button, label);
        button.Click += (_, _) => IconMotion.Play(button, button == playTransport ? (playing ? "pause" : "play") : icon);
        button.PointerEntered += (_, _) => IconMotion.Hover(button, icon, true); button.PointerExited += (_, _) => IconMotion.Hover(button, icon, false);
        return button;
    }
    Button ToolbarButton(string icon, string label, Action action)
    {
        var button = Button(label, action); button.Content = ToolbarLabel(icon, label); button.Height = 28; button.Padding = new Thickness(9, 0);
        button.HorizontalAlignment = HorizontalAlignment.Left; button.Background = Brushes.Transparent; button.Classes.Add("subtle");
        ToolTip.SetTip(button, label); Avalonia.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => IconMotion.Play(button, button == captionsGenerate && captionCancellation is not null ? "delete" : icon); return button;
    }
    void StyleButton(Button button)
    {
        button.Template = new FuncControlTemplate<Button>((owner, _) =>
        {
            var presenter = new ContentPresenter { HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            presenter.Bind(ContentPresenter.ContentProperty, owner.GetObservable(ContentControl.ContentProperty));
            presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, owner.GetObservable(HorizontalContentAlignmentProperty));
            var border = new Border { CornerRadius = new CornerRadius(6), Child = presenter, RenderTransformOrigin = RelativePoint.Center };
            // Hover fades the fill (150 ms, gentle ease); press dips to 97% (ease-out) and springs back on release.
            if (!Look.ReduceMotion)
                border.Transitions =
                [
                    new Avalonia.Animation.BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new Avalonia.Animation.Easings.SineEaseInOut() },
                    new Avalonia.Animation.TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(110), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
                ];
            border.Bind(Border.BackgroundProperty, owner.GetObservable(BackgroundProperty));
            border.Bind(Border.BorderBrushProperty, owner.GetObservable(BorderBrushProperty));
            border.Bind(Border.BorderThicknessProperty, owner.GetObservable(BorderThicknessProperty));
            border.Bind(Border.PaddingProperty, owner.GetObservable(PaddingProperty));
            return border;
        });
        IBrush Rest() => button.Classes.Contains("primary") ? AccentBrush : button.Classes.Contains("light") ? TextBrush : button.Classes.Contains("subtle") ? (button.Tag as IBrush ?? Brushes.Transparent) : ControlBrush;
        button.GotFocus += (_, e) => { if (e.NavigationMethod == Avalonia.Input.NavigationMethod.Tab) button.BorderBrush = AccentSoftBrush; }; // keyboard focus only, not after a click button.LostFocus += (_, _) => button.BorderBrush = Brushes.Transparent;
        button.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(IsEnabled)) button.Opacity = button.IsEnabled ? 1 : .4; };
        button.PointerEntered += (_, _) => button.Background = button.Classes.Contains("primary") ? AccentHoverBrush : button.Classes.Contains("light") ? Brushes.White : HoverBrush;
        button.PointerExited += (_, _) => button.Background = Rest();
        static void Press(Button b, bool down) { if (Avalonia.VisualTree.VisualExtensions.GetVisualChildren(b).FirstOrDefault() is Border border && !Look.ReduceMotion) border.RenderTransform = down ? Pressed : Released; }
        button.AddHandler(PointerPressedEvent, (_, e) => { if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return; button.Background = button.Classes.Contains("primary") ? AccentPressedBrush : button.Classes.Contains("light") ? Brush("#C9CCD3") : SelectedBrush; Press(button, true); }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(PointerReleasedEvent, (_, _) => { button.Background = button.IsPointerOver ? (button.Classes.Contains("primary") ? AccentHoverBrush : button.Classes.Contains("light") ? Brushes.White : HoverBrush) : Rest(); Press(button, false); }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        button.PointerCaptureLost += (_, _) => Press(button, false);
    }
    static readonly Avalonia.Media.Transformation.TransformOperations Pressed = Avalonia.Media.Transformation.TransformOperations.Parse("scale(0.97)"), Released = Avalonia.Media.Transformation.TransformOperations.Parse("scale(1)");
    static Border PanelHeader(Control content) => new() { Height = 36, Background = PanelBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 0, 6, 0), Child = content };

    Button maximizeButton = null!;
    void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    /// <summary>46×44 caption button, Windows-sized. Close turns red on hover like every Windows app.</summary>
    Button CaptionButton(string icon, string label, Action action, Avalonia.Input.WindowDecorationsElementRole role)
    {
        bool close = icon == "close";
        var button = new Button { Width = 46, Height = 44, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Content = Glyph(icon, SoftTextBrush, 14) };
        button.Template = new FuncControlTemplate<Button>((owner, _) =>
        {
            var presenter = new ContentPresenter { HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            presenter.Bind(ContentPresenter.ContentProperty, owner.GetObservable(ContentControl.ContentProperty));
            var border = new Border { Child = presenter }; border.Bind(Border.BackgroundProperty, owner.GetObservable(BackgroundProperty)); return border;
        });
        button.PointerEntered += (_, _) => { button.Background = close ? Brush("#E81123") : HoverBrush; if (close) button.Content = Glyph(icon, Brushes.White, 14); };
        button.PointerExited += (_, _) => { button.Background = Brushes.Transparent; if (close) button.Content = Glyph(icon, SoftTextBrush, 14); };
        button.Click += (_, _) => action();
        Avalonia.Controls.Chrome.WindowDecorationProperties.SetElementRole(button, role);
        ToolTip.SetTip(button, label); Avalonia.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
    void BuildWorkspace()
    {
        Background = CanvasBrush; Foreground = TextBrush; FontSize = 12;
        var root = new Grid { RowDefinitions = new RowDefinitions("44,*,24") };
        Content = root;

        // Title bar
        // Our own title bar: the window draws no system caption. The bar itself is marked as the title bar (drag, double-click
        // to maximise, Windows snapping) and the three caption buttons carry the matching roles (Snap Layouts on maximise).
        ExtendClientAreaToDecorationsHint = true; ExtendClientAreaTitleBarHeightHint = 44; WindowDecorations = WindowDecorations.BorderOnly;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Background = ChromeBrush };
        Avalonia.Controls.Chrome.WindowDecorationProperties.SetElementRole(top, Avalonia.Input.WindowDecorationsElementRole.TitleBar);
        top.PointerPressed += (_, e) => { if (e.Source == top || e.Source is TextBlock or Ellipse) { if (e.ClickCount == 2) ToggleMaximize(); else if (e.GetCurrentPoint(top).Properties.IsLeftButtonPressed) BeginMoveDrag(e); } };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 10, 0) };
        brand.Children.Add(Orb(20)); brand.Children.Add(Copy("ZenithClipper", 13, TextBrush, FontWeight.SemiBold)); top.Children.Add(brand);
        var menus = MenuBar(); Grid.SetColumn(menus, 1); menus.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(menus);
        var center = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        projectHeading.FontSize = 13; projectHeading.Foreground = TextBrush; projectHeading.VerticalAlignment = VerticalAlignment.Center; projectHeading.TextTrimming = TextTrimming.CharacterEllipsis; projectHeading.MaxWidth = 420;
        saveState.FontSize = 12; saveState.Foreground = FaintBrush; saveState.VerticalAlignment = VerticalAlignment.Center;
        center.Children.Add(projectHeading); center.Children.Add(saveState); Grid.SetColumn(center, 2); top.Children.Add(center);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        undoButton = ActionButton("undo", "Undo · Ctrl+Z", Undo, iconOnly: true); redoButton = ActionButton("redo", "Redo · Ctrl+Y", Redo, iconOnly: true);
        actions.Children.Add(undoButton); actions.Children.Add(redoButton); actions.Children.Add(Separator());
        var export = ActionButton("export", "Export", () => Run(Export), true); exportPrimary = export; export.MinWidth = 92; export.Height = 30; export.Margin = new Thickness(4, 0, 0, 0); actions.Children.Add(export);
        Grid.SetColumn(actions, 3); top.Children.Add(actions);
        var caption = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch };
        caption.Children.Add(CaptionButton("minimize", "Minimize", () => WindowState = WindowState.Minimized, Avalonia.Input.WindowDecorationsElementRole.MinimizeButton));
        maximizeButton = CaptionButton("maximize", "Maximize", ToggleMaximize, Avalonia.Input.WindowDecorationsElementRole.MaximizeButton); caption.Children.Add(maximizeButton);
        caption.Children.Add(CaptionButton("close", "Close", Close, Avalonia.Input.WindowDecorationsElementRole.CloseButton));
        Grid.SetColumn(caption, 4); top.Children.Add(caption);
        foreach (var interactive in new Control[] { menus, actions }) Avalonia.Controls.Chrome.WindowDecorationProperties.SetElementRole(interactive, Avalonia.Input.WindowDecorationsElementRole.User);
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty && e.Property != OffScreenMarginProperty) return;
            root.Margin = WindowState == WindowState.Maximized ? OffScreenMargin : default;
            maximizeButton.Content = Glyph(WindowState == WindowState.Maximized ? "restore" : "maximize", SoftTextBrush, 14);
            ToolTip.SetTip(maximizeButton, WindowState == WindowState.Maximized ? "Restore down" : "Maximize");
        };
        root.Children.Add(new Border { Child = top, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 0, 1) });

        // Media | monitor | inspector, timeline underneath
        workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("280,1,*,1,320"), RowDefinitions = new RowDefinitions("*,1,352") };
        Grid.SetRow(workspace, 1); root.Children.Add(workspace);
        workspace.Children.Add(BuildMediaPanel());
        var leftLine = Hairline(true); Grid.SetColumn(leftLine, 1); workspace.Children.Add(leftLine);
        var monitor = BuildMonitor(); Grid.SetColumn(monitor, 2); workspace.Children.Add(monitor);
        var rightLine = Hairline(true); Grid.SetColumn(rightLine, 3); workspace.Children.Add(rightLine);
        var properties = BuildPropertiesPanel(); Grid.SetColumn(properties, 4); workspace.Children.Add(properties);
        var midLine = Hairline(false); Grid.SetRow(midLine, 1); Grid.SetColumnSpan(midLine, 5); workspace.Children.Add(midLine);
        var tracks = BuildTimelinePanel(); Grid.SetRow(tracks, 2); Grid.SetColumnSpan(tracks, 5); workspace.Children.Add(tracks);

        // Status bar
        var bottom = new DockPanel { Background = ChromeBrush };
        var bottomHost = new Border { Child = bottom, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 0) };
        Grid.SetRow(bottomHost, 2); root.Children.Add(bottomHost);
        cancelExport = Button("Cancel export", () => exportCancellation?.Cancel()); cancelExport.IsVisible = false; cancelExport.Height = 20; cancelExport.FontSize = 11; cancelExport.Padding = new Thickness(8, 0); DockPanel.SetDock(cancelExport, Dock.Right); bottom.Children.Add(cancelExport);
        progress.Height = 4; progress.Width = 120; progress.Margin = new Thickness(10, 0); progress.Foreground = AccentBrush; DockPanel.SetDock(progress, Dock.Right); bottom.Children.Add(progress);
        var tip = Copy("Space play  ·  J K L shuttle  ·  Ctrl+K cut  ·  Ctrl+D dissolve", 11, FaintBrush); DockPanel.SetDock(tip, Dock.Right); bottom.Children.Add(tip);
        var dot = new Ellipse { Width = 6, Height = 6, Fill = Brush("#3ECF8E"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(dot, Dock.Left); bottom.Children.Add(dot);
        status.FontSize = 11; status.Foreground = MutedBrush; status.TextTrimming = TextTrimming.CharacterEllipsis; status.VerticalAlignment = VerticalAlignment.Center; bottom.Children.Add(status);

        SizeChanged += (_, _) =>
        {
            bool narrow = Bounds.Width < 1250;
            workspace.ColumnDefinitions[0].Width = new GridLength(narrow ? 236 : 280);
            double card = narrow ? 92 : 114; if (Math.Abs(card - mediaCardWidth) > .5) { mediaCardWidth = card; UpdateLibrary(); }
            workspace.ColumnDefinitions[4].Width = new GridLength(narrow ? 280 : 320);
            workspace.RowDefinitions[2].Height = new GridLength(Math.Clamp(Bounds.Height * .38, 240, 420));
        };
    }

    Control BuildMediaPanel()
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("36,Auto,*,Auto,28") };
        var heading = new DockPanel();
        var add = ActionButton("plus", "Import media", () => Run(Import), iconOnly: true); DockPanel.SetDock(add, Dock.Right); heading.Children.Add(add);
        // Scrolls sideways when docked panels add more tabs than fit.
        heading.Children.Add(new ScrollViewer { Content = libraryTabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 4, 0) });
        layout.Children.Add(PanelHeader(heading));
        // Search plus a bin picker (Premiere's bins / folders).
        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(0, 10, 0, 0) }; mediaSearch.Margin = new Thickness(12, 0, 6, 0); searchRow.Children.Add(mediaSearch);
        // Premiere's Project panel: sort and list / icon view.
        var sortBox = new ComboBox { ItemsSource = new[] { "Added", "Name", "Type", "Duration", "Used first" }, SelectedIndex = 0, Height = 30, MinWidth = 86, FontSize = 12, Margin = new Thickness(0, 0, 6, 0), Name = "library-sort" };
        ToolTip.SetTip(sortBox, "Sort the project items"); sortBox.SelectionChanged += (_, _) => { librarySort = (string)sortBox.SelectedItem!; UpdateLibrary(); };
        Grid.SetColumn(sortBox, 2); searchRow.Children.Add(sortBox);
        var viewToggle = ActionButton("list", "List view / icon view", () => { libraryListView = !libraryListView; UpdateLibrary(); }, iconOnly: true); viewToggle.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(viewToggle, 3); searchRow.Children.Add(viewToggle);
        binPicker.Margin = new Thickness(0, 0, 12, 0); binPicker.Height = 30; binPicker.MinWidth = 110; binPicker.FontSize = 12; Grid.SetColumn(binPicker, 1); searchRow.Children.Add(binPicker);
        binPicker.SelectionChanged += (_, _) => { if (!updatingBins && binPicker.SelectedItem is string b) { binFilter = b; UpdateLibrary(); } };
        ToolTip.SetTip(binPicker, "Bins: right-click a clip to file it in a bin.");
        Grid.SetRow(searchRow, 1); layout.Children.Add(searchRow); librarySearchRow = searchRow; mediaSearch.TextChanged += (_, _) => UpdateLibrary();
        library.Background = Brushes.Transparent; library.BorderThickness = new Thickness(0); library.Margin = new Thickness(8, 10, 8, 0); library.MinHeight = 0; library.Padding = new Thickness(0);
        library.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal });
        library.ItemTemplate = new FuncDataTemplate<MediaRow>((row, _) =>
        {
            if (row is null) return new Border();
            var parts = row.Text.Split('\n', 3);
            string kind = parts.Length > 1 ? parts[1] : "", duration = parts.Length > 2 ? parts[2] : "";
            bool audio = kind.StartsWith("Audio", StringComparison.Ordinal), text = kind.StartsWith("Text", StringComparison.Ordinal);
            var card = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 4), Width = mediaCardWidth };
            var thumb = new Grid { Height = 66 };
            thumb.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(audio ? "#24282E" : text ? "#2A2620" : "#2E3138"), 0), new GradientStop(Color.Parse("#1D1F23"), 1) } } });
            var asset = Project.Media.FirstOrDefault(m => m.Id == row.Id);
            if (asset is not null && MediaPreviews.Get(asset) is { ThumbsDone: true } preview && preview.Frame(Math.Min(preview.Frames.Length - 1, preview.Frames.Length / 3)) is { } poster)
                thumb.Children.Add(new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = new Image { Source = poster, Stretch = Stretch.UniformToFill } });
            else thumb.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = Glyph(audio ? "audio" : text ? "text" : "media", text ? AmberBrush : MutedBrush, 24) });
            if (duration.Length > 0) thumb.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4), Padding = new Thickness(4, 0), CornerRadius = new CornerRadius(3), Background = Brush("#B3000000"), Child = Copy(duration, 10, TextBrush) });
            if (row.Used) thumb.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(5) });
            card.Children.Add(thumb);
            var copy = new StackPanel { Spacing = 1 }; copy.Children.Add(Copy(parts[0], 12, TextBrush)); copy.Children.Add(Copy(kind, 11, FaintBrush)); card.Children.Add(copy);
            if (libraryListView)
            {
                // List view: one compact row per item across the panel.
                card.Orientation = Orientation.Horizontal; card.Spacing = 10; card.Width = Math.Max(160, library.Bounds.Width - 24); card.Margin = new Thickness(0, 1);
                thumb.Height = 34; thumb.Width = 60; copy.VerticalAlignment = VerticalAlignment.Center;
            }
            ToolTip.SetTip(card, parts[0] + " · double-click or drag onto the timeline · right-click for more");
            var rowId = row.Id;
            card.ContextRequested += (_, e) =>
            {
                e.Handled = true;
                var menuItems = new List<MenuItem>();
                MenuItem It(string header, Action a) { var m = new MenuItem { Header = header }; m.Click += (_, _) => a(); return m; }
                menuItems.Add(It("Open in Source Monitor", () => OpenInSource(rowId)));
                menuItems.Add(It("Move to bin…", () => Run(async () => { var name = await Prompt("Move to bin", "Bin name (new or existing).", binFilter is "All bins" or "Unsorted" ? "Bin 1" : binFilter); if (!string.IsNullOrWhiteSpace(name)) Change("Move to bin", p => p.Media.First(m => m.Id == rowId).Bin = name.Trim()); })));
                foreach (var b in Project.Media.Select(m => m.Bin).Where(b => b is not null).Distinct().Take(8)) { var bin = b!; menuItems.Add(It("Move to " + bin, () => Change("Move to bin", p => p.Media.First(m => m.Id == rowId).Bin = bin))); }
                menuItems.Add(It("Remove from bin", () => Change("Remove from bin", p => p.Media.First(m => m.Id == rowId).Bin = null)));
                menuItems.Add(It("Rename…", () => Run(async () => { var current = Project.Media.First(m => m.Id == rowId).Name; var name = await Prompt("Rename", "New name for this item.", current); if (!string.IsNullOrWhiteSpace(name)) Change("Rename", p => p.Media.First(m => m.Id == rowId).Name = name.Trim()); })));
                new ContextMenu { ItemsSource = menuItems }.Open(card);
            };
            // Drag a card onto a track to place it there (audio files only land on audio tracks). The press starts the drag;
            // the window follows the pointer, so it keeps working even if the list rebuilds or grabs the pointer.
            card.Background = Brushes.Transparent;
            card.AddHandler(PointerPressedEvent, (_, e) => { if (asset is not null && e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) mediaDrag = (asset, e.GetPosition(this), false); }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            return card;
        });
        var listHost = new Grid(); listHost.Children.Add(library); listHost.Children.Add(effectsBrowser); listHost.Children.Add(leftDockHost);
        libraryEmpty = EmptyState("media", "Start with your clips", "Import video, audio or images.\nYour originals stay untouched.", "Import media", () => Run(Import)); listHost.Children.Add(libraryEmpty);
        libraryNoMatches = new Border { Child = new TextBlock { Text = "Nothing matches\nTry a different name or tab.", Foreground = MutedBrush, FontSize = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, IsVisible = false }; listHost.Children.Add(libraryNoMatches);
        Grid.SetRow(listHost, 2); layout.Children.Add(listHost);
        appendMedia = ActionButton("plus", "Add to timeline", () => AppendSelected()); appendMedia.Margin = new Thickness(12, 8, 12, 10); Grid.SetRow(appendMedia, 3); layout.Children.Add(appendMedia);
        library.SelectionChanged += (_, _) => appendMedia.IsEnabled = library.SelectedItem is MediaRow; library.DoubleTapped += (_, _) => { if (library.SelectedItem is not MediaRow) return; mediaDrag = null; AppendSelected(atEnd: true); if (selectedClip is { } id && Sequence.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == id) is { } added) Seek(added.Start + added.Duration); };
        mediaCount.Foreground = FaintBrush; mediaCount.FontSize = 11; mediaCount.VerticalAlignment = VerticalAlignment.Center;
        var foot = new Border { BorderBrush = LineBrush, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 0), Child = mediaCount }; Grid.SetRow(foot, 4); layout.Children.Add(foot);
        BuildLibraryTabs();
        return Frame(layout);
    }
    (MediaAsset Asset, Point Start, bool Moving)? mediaDrag;
    /// <summary>Window-level half of dragging a media card onto the timeline (see the card's press handler).</summary>
    void HookMediaDrag()
    {
        AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (mediaDrag is not { } d) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { CancelMediaDrag(); return; }
            var now = e.GetPosition(this);
            if (!d.Moving && Math.Abs(now.X - d.Start.X) + Math.Abs(now.Y - d.Start.Y) < 6) return;
            if (!d.Moving) { mediaDrag = d with { Moving = true }; Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.DragCopy); }
            MoveDragChip(d.Asset, now);
            if (OverTimeline(e)) timeline.DragMediaOver(d.Asset, e.GetPosition(timeline)); else timeline.DropMedia(null);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (mediaDrag is not { Moving: true }) { mediaDrag = null; return; }
            timeline.DropMedia(OverTimeline(e) ? e.GetPosition(timeline) : null);
            mediaDrag = null; Cursor = null; HideDragChip(); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Avalonia.Input.Key.Escape && mediaDrag is { Moving: true }) { CancelMediaDrag(); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => { if (mediaDrag is { Moving: true }) CancelMediaDrag(); };
    }
    void CancelMediaDrag() { mediaDrag = null; Cursor = null; HideDragChip(); timeline.DropMedia(null); }
    Border? dragChip;
    /// <summary>A small card that follows the pointer for the whole drag, so you can see what you're carrying before
    /// it reaches a track (the track itself then shows where it will land).</summary>
    void MoveDragChip(MediaAsset asset, Point at)
    {
        if (Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(this) is not { } layer) return;
        if (dragChip is null)
        {
            string glyph = asset.Kind switch { MediaKind.Audio => "♪", MediaKind.Image => "▣", _ => "▶" };
            dragChip = new Border
            {
                Background = ControlBrush, BorderBrush = AccentBrush, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 6), IsHitTestVisible = false, Opacity = .95, MaxWidth = 260,
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 18, Color = Color.FromArgb(110, 0, 0, 0) }),
                Child = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = glyph, Foreground = AccentBrush, FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                        new TextBlock { Text = asset.Name, Foreground = TextBrush, FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                    },
                },
            };
            layer.Children.Add(dragChip);
        }
        Canvas.SetLeft(dragChip, at.X + 14); Canvas.SetTop(dragChip, at.Y + 12);
    }
    void HideDragChip()
    {
        if (dragChip is not null && Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(this) is { } layer) layer.Children.Remove(dragChip);
        dragChip = null;
    }
    bool OverTimeline(Avalonia.Input.PointerEventArgs e) { var p = e.GetPosition(timelineScroller); return p.X >= 0 && p.Y >= 0 && p.X <= timelineScroller.Viewport.Width && p.Y <= timelineScroller.Viewport.Height; }
    void BuildLibraryTabs()
    {
        libraryTabs.Children.Clear();
        if (DockablePanels.Contains(libraryFilter) && (!dockedLeft.Contains(libraryFilter) || floatingPanels.ContainsKey(libraryFilter))) libraryFilter = "Media";
        foreach (var name in new[] { "Media", "Titles", "Captions", "Effects" }.Concat(DockablePanels.Where(n => dockedLeft.Contains(n) && !floatingPanels.ContainsKey(n))))
        {
            bool on = name == libraryFilter;
            var tab = Button(name, () => { libraryFilter = name; BuildLibraryTabs(); UpdateLibrary(); });
            tab.Height = 26; tab.Padding = new Thickness(7, 0); tab.FontSize = 12; tab.HorizontalAlignment = HorizontalAlignment.Left;
            tab.Foreground = on ? TextBrush : MutedBrush; tab.Classes.Add("subtle"); tab.Tag = on ? SelectedBrush : null; tab.Background = on ? SelectedBrush : Brushes.Transparent;
            if (DockablePanels.Contains(name)) MakeDockableTab(tab, name);
            libraryTabs.Children.Add(tab);
            if (on) Dispatcher.UIThread.Post(() => tab.BringIntoView(), DispatcherPriority.Background);
        }
    }
    Border EmptyState(string icon, string title, string description, string action, Action callback)
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 300 };
        stack.Children.Add(new Border { Width = 40, Height = 40, Background = ControlBrush, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, Child = Glyph(icon, MutedBrush, 22) });
        stack.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = description, FontSize = 12, LineHeight = 18, Foreground = MutedBrush, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var button = ActionButton("import", action, callback, true); button.HorizontalAlignment = HorizontalAlignment.Center; button.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(button);
        return new Border { Child = stack, Background = Brushes.Transparent };
    }

    Control BuildMonitor()
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("36,*,48"), Background = CanvasBrush };
        var heading = new DockPanel();
        profileHeading.Foreground = FaintBrush; profileHeading.FontSize = 11; profileHeading.VerticalAlignment = VerticalAlignment.Center; profileHeading.Margin = new Thickness(0, 0, 6, 0); DockPanel.SetDock(profileHeading, Dock.Right); heading.Children.Add(profileHeading);
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; BuildMonitorTabs(); title.Children.Add(monitorTabs);
        sequenceHeading.Foreground = FaintBrush; sequenceHeading.FontSize = 12; sequenceHeading.VerticalAlignment = VerticalAlignment.Center; title.Children.Add(sequenceHeading); heading.Children.Add(title);
        panel.Children.Add(PanelHeader(heading));
        var viewport = new Grid { Background = DeepBrush }; viewport.Children.Add(preview);
        viewport.Children.Add(comparison); viewport.Children.Add(safeMargins); viewport.Children.Add(timecodeOverlay); transformOverlay = new TransformOverlay(this); viewport.Children.Add(transformOverlay); cropOverlay = new CropOverlay(this); viewport.Children.Add(cropOverlay); guidesOverlay = new GuidesOverlay(this); viewport.Children.Add(guidesOverlay); penOverlay = new PenMaskOverlay(this); viewport.Children.Add(penOverlay);
        previewEmpty = EmptyState("cut", "Make your first cut", "Add a clip to the timeline\nto preview your edit here.", "Import media", () => Run(Import)); viewport.Children.Add(previewEmpty);
        // Audio in the Source Monitor: there is no picture, so show the file's waveform and name instead of black.
        sourceAudioView = new Grid { IsVisible = false, Background = DeepBrush, RowDefinitions = new RowDefinitions("*,Auto,Auto,*") };
        sourceWave.Height = 120; sourceWave.Margin = new Thickness(40, 0); sourceWave.Fill = AccentSoftBrush; Grid.SetRow(sourceWave, 1); sourceAudioView.Children.Add(sourceWave);
        sourceAudioName.FontSize = 13; sourceAudioName.Foreground = SoftTextBrush; sourceAudioName.HorizontalAlignment = HorizontalAlignment.Center; sourceAudioName.Margin = new Thickness(0, 14, 0, 0); Grid.SetRow(sourceAudioName, 2); sourceAudioView.Children.Add(sourceAudioName);
        viewport.Children.Add(sourceAudioView);
        monitorViewport = viewport; monitorScroller = new ScrollViewer { Content = viewport, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var frame = new Border { Child = monitorScroller, ClipToBounds = true, Margin = new Thickness(8), BorderBrush = LineBrush, BorderThickness = new Thickness(1) }; Grid.SetRow(frame, 1); panel.Children.Add(frame);

        var transport = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Background = PanelBrush };
        var clock = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        time.FontSize = 13; time.Foreground = AccentSoftBrush; time.VerticalAlignment = VerticalAlignment.Center; time.FontFeatures = FontFeatureCollection.Parse("tnum");
        durationHeading.Foreground = FaintBrush; durationHeading.FontSize = 13; durationHeading.VerticalAlignment = VerticalAlignment.Center; durationHeading.FontFeatures = FontFeatureCollection.Parse("tnum");
        // Click the timecode to type a time and jump there: 1:23, 00:01:23.500, 83, or h:m:s:ff with frames. Enter or clicking away jumps; Esc cancels.
        var timeEntry = new TextBox { FontSize = 13, Width = 118, Height = 26, MinHeight = 26, Padding = new Thickness(6, 0), IsVisible = false, VerticalContentAlignment = VerticalAlignment.Center, FontFeatures = FontFeatureCollection.Parse("tnum"), Foreground = AccentSoftBrush, Background = ControlBrush, BorderBrush = AccentBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        var timeButton = new Border { Child = time, Padding = new Thickness(6, 3), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Ibeam) };
        ToolTip.SetTip(timeButton, "Click to jump to a time");
        timeButton.PointerEntered += (_, _) => timeButton.Background = HoverBrush; timeButton.PointerExited += (_, _) => timeButton.Background = Brushes.Transparent;
        bool open = false;
        void CloseEntry() { open = false; timeEntry.IsVisible = false; timeButton.IsVisible = true; }
        void Commit()
        {
            if (!open) return;
            var typed = ParseTimecode(timeEntry.Text ?? "", playhead); CloseEntry();
            if (typed is { } frame) Seek(frame); else if (!string.IsNullOrWhiteSpace(timeEntry.Text)) status.Text = "Type a time like 1:23 or 00:01:23.500.";
        }
        // Only digits, ':' and '.' get in.
        timeEntry.AddHandler(Avalonia.Input.InputElement.TextInputEvent, (_, e) => { if (e.Text is { } t && t.Any(c => !char.IsAsciiDigit(c) && c is not ':' and not '.')) e.Handled = true; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        timeEntry.PastingFromClipboard += (_, e) => e.Handled = true;
        timeButton.PointerPressed += (_, e) => { open = true; timeButton.IsVisible = false; timeEntry.IsVisible = true; timeEntry.Text = Clock(playhead); timeEntry.Focus(); timeEntry.SelectAll(); e.Handled = true; };
        timeEntry.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) { CloseEntry(); e.Handled = true; }
            else if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Tab) { Commit(); e.Handled = true; }
        };
        timeEntry.LostFocus += (_, _) => Commit();
        timeButton.Margin = new Thickness(-6, 0, 0, 0);
        clock.Children.Add(timeButton); clock.Children.Add(timeEntry); clock.Children.Add(durationHeading); transport.Children.Add(clock);
        var playback = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var toStart = ActionButton("start", "Go to start · Home", () => Seek(0), iconOnly: true); frameNavigation.Add(toStart); playback.Children.Add(toStart);
        var previous = ActionButton("previous", "Previous frame · Left", () => Seek(playhead - 1), iconOnly: true); frameNavigation.Add(previous); playback.Children.Add(previous);
        playTransport = ActionButton("play", "Play", () => Run(TogglePlay), iconOnly: true);
        playTransport.Classes.Remove("subtle"); playTransport.Classes.Add("light"); playTransport.Width = 34; playTransport.Height = 34; playTransport.Background = TextBrush; playTransport.Content = Glyph("play", CanvasBrush, 16); playTransport.Margin = new Thickness(6, 0);
        playTransport.Template = new FuncControlTemplate<Button>((owner, _) =>
        {
            var presenter = new ContentPresenter { HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            presenter.Bind(ContentPresenter.ContentProperty, owner.GetObservable(ContentControl.ContentProperty));
            var border = new Border { CornerRadius = new CornerRadius(17), Child = presenter };
            border.Bind(Border.BackgroundProperty, owner.GetObservable(BackgroundProperty));
            return border;
        });
        playback.Children.Add(playTransport);
        var next = ActionButton("next", "Next frame · Right", () => Seek(playhead + 1), iconOnly: true); frameNavigation.Add(next); playback.Children.Add(next);
        var toEnd = ActionButton("end", "Go to end · End", () => Seek(Math.Max(0, MonitorDuration - 1)), iconOnly: true); frameNavigation.Add(toEnd); playback.Children.Add(toEnd);
        foreach (var (button, glyph) in new[] { (toStart, "start"), (previous, "previous"), (next, "next"), (toEnd, "end") }) button.Content = Glyph(glyph, SoftTextBrush, 20);
        Grid.SetColumn(playback, 1); transport.Children.Add(playback);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        captionsToggle = Button("Captions", ToggleCaptions); captionsToggle.Width = 38; captionsToggle.Height = 30; captionsToggle.Padding = new Thickness(0); captionsToggle.Classes.Add("subtle"); captionsToggle.Background = Brushes.Transparent; ToolTip.SetTip(transportMeter, "Program audio level"); right.Children.Add(transportMeter);
        var safe = ActionButton("fit", "Safe margins (action 90%, title 80%)", () => { safeMargins.IsVisible = !safeMargins.IsVisible; safeMargins.FrameWidth = Sequence.Width; safeMargins.FrameHeight = Sequence.Height; safeMargins.InvalidateVisual(); }, iconOnly: true);
        var fxMute = ActionButton("fx", "Global FX mute: preview without effects to compare before/after", () => { effectsBypassed = !effectsBypassed; graphRevision++; Refresh(true); status.Text = effectsBypassed ? "Effects muted in the preview (export still includes them)." : "Effects back on."; }, iconOnly: true);
        var full = ActionButton("maximize", "Full screen playback · Ctrl+`", ToggleFullScreen, iconOnly: true);
        right.Children.Add(MonitorZoom()); var compare = ActionButton("compare", "Comparison view: capture this frame as a reference and compare it side by side with the live program", () => ToggleComparison(), iconOnly: true); right.Children.Add(compare); right.Children.Add(PlaybackResolution()); right.Children.Add(fxMute); right.Children.Add(full); right.Children.Add(safe); right.Children.Add(captionsToggle);
        Grid.SetColumn(right, 2); transport.Children.Add(right);
        Grid.SetRow(transport, 2); panel.Children.Add(new Border { Child = transport, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 1, 0, 0), [Grid.RowProperty] = 2 });
        return panel;
    }

    Control BuildPropertiesPanel()
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("36,*") };
        // Premiere keeps Effect Controls, the Audio Track Mixer and Lumetri Scopes as tabs in the same panel group.
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        inspector.Margin = new Thickness(12, 10, 12, 12); inspector.Spacing = 0;
        var scroll = new ScrollViewer { Content = inspector, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; propertiesScroller = scroll; Grid.SetRow(scroll, 1); panel.Children.Add(scroll);
        rightDockHost = panel; rightTabStrip = tabs;
        Grid.SetRow(mixerPanel, 1); panel.Children.Add(mixerPanel); Grid.SetRow(scopes, 1); panel.Children.Add(scopes); Grid.SetRow(historyPanel, 1); panel.Children.Add(historyPanel);
        void Build()
        {
            tabs.Children.Clear();
            if (floatingPanels.ContainsKey(rightPanel) || dockedLeft.Contains(rightPanel)) rightPanel = "Inspector";
            foreach (var name in new[] { "Inspector", "Mixer", "Scopes", "History" }.Where(n => !floatingPanels.ContainsKey(n) && !dockedLeft.Contains(n)))
            {
                bool on = name == rightPanel;
                var tab = Button(name, () => { rightPanel = name; Build(); scroll.IsVisible = rightPanel == "Inspector"; UpdateMixer(); UpdateScopesPanel(); UpdateHistory(); });
                tab.Height = 26; tab.Padding = new Thickness(9, 0); tab.FontSize = 12; tab.HorizontalAlignment = HorizontalAlignment.Left; tab.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                tab.Foreground = on ? TextBrush : MutedBrush; tab.Classes.Add("subtle"); tab.Tag = on ? SelectedBrush : null; tab.Background = on ? SelectedBrush : Brushes.Transparent;
                if (name != "Inspector") MakeDockableTab(tab, name);
                tabs.Children.Add(tab);
            }
        }
        Build(); rebuildRightTabs = () => { Build(); scroll.IsVisible = rightPanel == "Inspector"; UpdateMixer(); UpdateScopesPanel(); UpdateHistory(); };
        panel.Children.Add(PanelHeader(tabs));
        return Frame(panel);
    }

    Control BuildTimelinePanel()
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("30,40,*"), ColumnDefinitions = new ColumnDefinitions($"{TrackHeaderWidth},*"), Background = ChromeBrush };
        // Premiere shows one tab per open sequence across the top of the Timeline panel.
        var tabsHost = new Border { Child = sequenceTabs, Background = ChromeBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 0) };
        Grid.SetColumnSpan(tabsHost, 2); panel.Children.Add(tabsHost);
        var toolbar = new DockPanel { Background = PanelBrush };
        var toolbarHost = new Border { Child = toolbar, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0) };
        Grid.SetRow(toolbarHost, 1); Grid.SetColumnSpan(toolbarHost, 2); panel.Children.Add(toolbarHost);
        var zoomGroup = new Grid { ColumnDefinitions = new ColumnDefinitions("30,120,30,Auto"), VerticalAlignment = VerticalAlignment.Center };
        var zoomOut = ActionButton("minus", "Zoom out", () => zoom.Value = Math.Max(zoom.Minimum, zoom.Value / 1.4), iconOnly: true); zoomGroup.Children.Add(zoomOut);
        zoom.Width = 112; zoom.Margin = new Thickness(4, 0); zoom.VerticalAlignment = VerticalAlignment.Center; StyleSlider(zoom); Grid.SetColumn(zoom, 1); zoomGroup.Children.Add(zoom);
        var zoomIn = ActionButton("plus", "Zoom in", () => zoom.Value = Math.Min(zoom.Maximum, zoom.Value * 1.4), iconOnly: true); Grid.SetColumn(zoomIn, 2); zoomGroup.Children.Add(zoomIn);
        var fit = ActionButton("fit", "Fit timeline to window", FitTimeline, iconOnly: true); fit.Margin = new Thickness(4, 0, 0, 0); Grid.SetColumn(fit, 3); zoomGroup.Children.Add(fit);
        DockPanel.SetDock(zoomGroup, Dock.Right); toolbar.Children.Add(zoomGroup);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        BuildToolPalette(); tools.Children.Add(toolPalette); tools.Children.Add(Separator());
        tools.Children.Add(ActionButton("cut", "Add edit at playhead · Ctrl+K (Ctrl+Shift+K all tracks)", Split, iconOnly: true));
        tools.Children.Add(ActionButton("delete", "Delete clip · Delete", () => Delete(false), iconOnly: true));
        tools.Children.Add(ActionButton("ripple", "Ripple delete · Shift+Delete", () => Delete(true), iconOnly: true));
        tools.Children.Add(ActionButton("marker", "Add marker", AddMarker, iconOnly: true));
        tools.Children.Add(Separator());
        ToolTip.SetTip(snap, "Snap · S"); snap.Content = "Snap"; snap.FontSize = 12; snap.Foreground = SoftTextBrush; snap.VerticalAlignment = VerticalAlignment.Center; snap.Margin = new Thickness(4, 0, 6, 0); tools.Children.Add(snap);
        tools.Children.Add(Separator());
        tools.Children.Add(ToolbarButton("media", "Video track", () => AddTrack(TrackKind.Video)));
        tools.Children.Add(ToolbarButton("audio", "Audio track", () => AddTrack(TrackKind.Audio)));
        tools.Children.Add(ToolbarButton("text", "Title", () => Run(CreateTitle)));
        var adjust = ActionButton("fx", "New adjustment layer · effects on it apply to everything below", AddAdjustmentLayer, iconOnly: true); tools.Children.Add(adjust);
        tools.Children.Add(Separator());
        captionsGenerate = ToolbarButton("captions", "Auto captions", () => Run(GenerateAutoCaptions)); tools.Children.Add(captionsGenerate);
        toolbar.Children.Add(tools);

        headerScroll.Content = headers; headerScroll.VerticalAlignment = VerticalAlignment.Top; headers.VerticalAlignment = VerticalAlignment.Top; headerScroll.VerticalContentAlignment = VerticalAlignment.Top; headerScroll.Background = PanelBrush;
        var headerHost = new Border { Child = headerScroll, Background = PanelBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 1, 0) };
        Grid.SetRow(headerHost, 2); panel.Children.Add(headerHost);
        timeline.VerticalAlignment = VerticalAlignment.Top; timeline.HorizontalAlignment = HorizontalAlignment.Left;
        var scroller = new ScrollViewer { Content = timeline, VerticalContentAlignment = VerticalAlignment.Top, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; timelineScroller = scroller;
        Grid.SetColumn(scroller, 1); Grid.SetRow(scroller, 2); panel.Children.Add(scroller);
        scroller.ScrollChanged += (_, _) => { if (scroller.Viewport.Height > 0) headerScroll.Height = scroller.Viewport.Height; headerScroll.Offset = new Vector(0, scroller.Offset.Y); };
        // The lanes always fill the visible width, so the ruler never stops short of the window edge.
        scroller.SizeChanged += (_, _) => { if (Math.Abs(timeline.ViewportWidth - scroller.Viewport.Width) > 1) { timeline.ViewportWidth = scroller.Viewport.Width; timeline.Rebuild(); } };
        zoom.PropertyChanged += (_, e) => { if (e.Property.Name == nameof(Slider.Value)) timeline.Rebuild(); };
        // Ctrl + wheel zooms around the pointer (the frame under the cursor stays put); Shift + wheel scrolls sideways.
        scroller.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
            {
                double pointerX = e.GetPosition(scroller).X, contentX = scroller.Offset.X + pointerX;
                double seconds = contentX / zoom.Value;
                zoom.Value = Math.Clamp(zoom.Value * Math.Pow(1.18, e.Delta.Y), zoom.Minimum, zoom.Maximum);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { scroller.UpdateLayout(); scroller.Offset = new Vector(Math.Max(0, seconds * zoom.Value - pointerX), scroller.Offset.Y); }, Avalonia.Threading.DispatcherPriority.Loaded);
                e.Handled = true;
            }
            else if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
            {
                scroller.Offset = new Vector(Math.Max(0, scroller.Offset.X - e.Delta.Y * 80), scroller.Offset.Y);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ToolTip.SetTip(zoom, "Zoom · Ctrl + scroll on the timeline");
        return panel;
    }
    /// <summary>Thin 3px track with a small white knob, matching the panel's sliders instead of Fluent's large accent thumb.</summary>
    static void StyleSlider(Slider slider)
    {
        var r = slider.Resources;
        r["SliderHorizontalThumbWidth"] = 12.0; r["SliderHorizontalThumbHeight"] = 12.0; r["SliderTrackThemeHeight"] = 3.0;
        foreach (var key in new[] { "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed" }) r[key] = TextBrush;
        foreach (var key in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed" }) r[key] = SoftTextBrush;
        foreach (var key in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" }) r[key] = BorderLineBrush;
    }
    void FitTimeline()
    {
        var seconds = Math.Max(1, timelineFps.Seconds(Sequence.Duration));
        double width = timelineScroller.Viewport.Width > 0 ? timelineScroller.Viewport.Width : Bounds.Width - TrackHeaderWidth;
        zoom.Value = Math.Clamp((width - 40) / seconds, zoom.Minimum, zoom.Maximum);
        timelineScroller.Offset = new Vector(0, timelineScroller.Offset.Y);
    }

    string librarySort = "Added"; bool libraryListView;
    void UpdateLibrary()
    {
        var previous = (library.SelectedItem as MediaRow)?.Id;
        var project = Project; var all = project.Media; var search = mediaSearch.Text?.Trim() ?? "";
        var sequence = project.Sequences.First(s => s.Id == project.ActiveSequenceId);
        var used = sequence.Tracks.SelectMany(t => t.Clips).Select(c => c.MediaId).ToHashSet();
        var captionMedia = sequence.Tracks.Where(IsCaptionTrack).SelectMany(t => t.Clips).Select(c => c.MediaId).ToHashSet();
        bool InTab(MediaAsset m) => libraryFilter switch { "Titles" => m.Kind == MediaKind.Text && !captionMedia.Contains(m.Id), "Captions" => m.Kind == MediaKind.Text && captionMedia.Contains(m.Id), _ => m.Kind != MediaKind.Text };
        // Bins: rebuild the picker from the bins in use; "Unsorted" holds items not filed anywhere.
        var bins = all.Select(m => m.Bin).Where(b => b is not null).Select(b => b!).Distinct().OrderBy(b => b).ToList();
        if (binFilter is not ("All bins" or "Unsorted") && !bins.Contains(binFilter)) binFilter = "All bins";
        updatingBins = true; binPicker.ItemsSource = new[] { "All bins" }.Concat(bins).Append("Unsorted").ToList(); binPicker.SelectedItem = binFilter; binPicker.IsVisible = bins.Count > 0; updatingBins = false;
        bool InBin(MediaAsset m) => binFilter switch { "All bins" => true, "Unsorted" => m.Bin is null, _ => m.Bin == binFilter };
        var rows = all.Where(InTab).Where(InBin).Where(m => search.Length == 0 || m.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || (m.Bin?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(m => librarySort == "Name" ? m.Name : librarySort == "Type" ? m.Kind.ToString() : "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => librarySort == "Duration" ? -(m.SubclipOut - m.SubclipIn ?? m.DurationSeconds) : librarySort == "Used first" ? (used.Contains(m.Id) ? 0 : 1) : 0)
            .Select(m => new MediaRow(m.Id, $"{m.Name}\n{KindLabel(m)}\n{(m.Kind is MediaKind.Video or MediaKind.Audio or MediaKind.Sequence ? ShortClock(m.SubclipOut - m.SubclipIn ?? m.DurationSeconds) : "")}", used.Contains(m.Id))).ToList();
        library.ItemsSource = rows; if (previous.HasValue) library.SelectedItem = rows.FirstOrDefault(r => r.Id == previous);
        int inTab = all.Count(InTab);
        mediaCount.Text = libraryFilter switch { "Captions" => $"{inTab} caption lines", "Titles" => $"{inTab} titles", _ => $"{inTab} clips · originals stay untouched" };
        libraryEmpty.IsVisible = all.Count == 0; library.IsVisible = rows.Count > 0; libraryNoMatches.IsVisible = all.Count > 0 && rows.Count == 0; appendMedia.IsEnabled = library.SelectedItem is MediaRow;
        bool docked = DockablePanels.Contains(libraryFilter); leftDockHost.IsVisible = docked; if (librarySearchRow is not null) librarySearchRow.IsVisible = !docked;
        if (libraryFilter is "Effects" || docked) { libraryEmpty.IsVisible = library.IsVisible = libraryNoMatches.IsVisible = false; appendMedia.IsVisible = false; } else appendMedia.IsVisible = true;
        UpdateEffectsBrowser(); UpdateMixer(); UpdateScopesPanel(); UpdateHistory();
    }
    static string KindLabel(MediaAsset m) => m.Kind switch
    {
        MediaKind.Video => m.Width > 0 ? $"Video · {m.Width}×{m.Height}" : "Video",
        MediaKind.Audio => "Audio", MediaKind.Image => "Image", MediaKind.Text => "Text", MediaKind.Sequence => $"Sequence · {m.Width}×{m.Height}", MediaKind.Adjustment => "Adjustment layer", _ => m.Kind.ToString()
    };
    static string ShortClock(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}"; }
    void UpdateChrome()
    {
        projectHeading.Text = Project.Name; saveState.Text = session.Dirty ? "Unsaved changes" : "Saved";
        var sequence = Sequence; sequenceHeading.Text = sequence.Name; profileHeading.Text = $"{sequence.Width}×{sequence.Height} · {sequence.Fps.Value:0.##} fps";
        // The Source Monitor shows the loaded file's own length and is playable even when the timeline is empty.
        var source = sourceMode ? Project.Media.FirstOrDefault(m => m.Id == sourceMedia) : null;
        long shown = source is not null ? SourceLength : sequence.Duration;
        durationHeading.Text = " / " + Clock(shown); previewEmpty.IsVisible = source is null && sequence.Duration == 0;
        sourceAudioView.IsVisible = source is { Kind: MediaKind.Audio };
        if (source is { Kind: MediaKind.Audio })
        {
            sourceWave.Entry = MediaPreviews.Get(source); sourceWave.SourceSeconds = 0; sourceWave.SpanSeconds = Math.Max(.1, source.DurationSeconds);
            sourceWave.SecondsPerPixel = sourceWave.SpanSeconds / Math.Max(1, (monitorViewport?.Bounds.Width ?? 880) - 80); sourceWave.InvalidateVisual();
            sourceAudioName.Text = source.Name;
        }
        exportPrimary.IsEnabled = sequence.Duration > 0; playTransport.IsEnabled = shown > 0; foreach (var button in frameNavigation) button.IsEnabled = shown > 0;
        undoButton.IsEnabled = session.CanUndo; redoButton.IsEnabled = session.CanRedo;
        UpdateLibrary(); UpdateCaptionControls();
    }
    /// <summary>Reads a typed time: digits with ':' and '.', as h:m:s(.ms), m:s, plain seconds, or h:m:s:ff with frames.</summary>
    internal long? ParseTimecode(string input, long current)
    {
        var text = input.Trim(); if (text.Length == 0 || text.Any(c => !char.IsAsciiDigit(c) && c is not ':' and not '.')) return null;
        var ci = CultureInfo.InvariantCulture; double fps = timelineFps.Value, frames = 0, seconds = 0;
        var parts = text.Split(':'); if (parts.Length > 4 || parts.Any(p => p.Length == 0)) return null;
        if (parts.Length == 4) { if (!int.TryParse(parts[3], NumberStyles.None, ci, out var ff)) return null; frames = ff; parts = parts[..3]; }
        foreach (var part in parts) { if (part.Count(c => c == '.') > 1 || !double.TryParse(part, NumberStyles.AllowDecimalPoint, ci, out var v)) return null; seconds = seconds * 60 + v; }
        seconds += frames / fps;
        if (!double.IsFinite(seconds) || seconds > 86400 * 7) return null;
        return Math.Max(0, (long)Math.Round(seconds * fps));
    }
    string Clock(long frames) { var elapsed = TimeSpan.FromSeconds(timelineFps.Seconds(frames)); return $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}"; }
    void UpdateTransportState()
    {
        if (playTransport is null || lastTransportPlaying == playing) return; lastTransportPlaying = playing; playTransport.Content = Glyph(playing ? "pause" : "play", CanvasBrush, 16); ToolTip.SetTip(playTransport, playing ? "Pause · Space" : "Play · Space"); Avalonia.Automation.AutomationProperties.SetName(playTransport, playing ? "Pause" : "Play"); UpdateTransformOverlay();
    }

    /// <summary>Video / Color / Audio tabs over the inspector sections. Every section is still built so state survives tab switches.</summary>
    void InspectorTabs()
    {
        var tabs = new Border { Background = ChromeBrush, BorderBrush = BorderLineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(2), Margin = new Thickness(0, 0, 0, 6) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        string[] names = ["Video", "Color", "Audio", "Effects"];
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i]; bool on = name == inspectorTab;
            var tab = Button(name, () => { inspectorTab = name; BuildInspector(); });
            tab.Height = 24; tab.FontSize = 12; tab.Padding = new Thickness(0); tab.HorizontalContentAlignment = HorizontalAlignment.Center; tab.Foreground = on ? TextBrush : MutedBrush; tab.Classes.Add("subtle"); tab.Tag = on ? SelectedBrush : null; tab.Background = on ? SelectedBrush : Brushes.Transparent;
            Grid.SetColumn(tab, i); grid.Children.Add(tab);
        }
        tabs.Child = grid; inspector.Children.Add(tabs);
    }
    void ApplyInspectorTab()
    {
        static string TabFor(string section) => section switch { "Color & effects" => "Color", "Sound" => "Audio", "Effects" or "Transitions" => "Effects", _ => "Video" };
        bool any = false;
        foreach (var child in inspector.Children.OfType<Border>())
            if (child.Tag is string tag && tag.StartsWith("section:", StringComparison.Ordinal)) { child.IsVisible = TabFor(tag[8..]) == inspectorTab; any |= child.IsVisible; }
        if (!any) inspector.Children.Add(new TextBlock { Text = inspectorTab == "Color" ? "Color settings apply to video, image and title clips." : "This clip has no sound of its own.", Foreground = MutedBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12) });
    }
    StackPanel InspectorSection(string name, string icon, bool defaultOpen = true)
    {
        bool open = inspectorSections.GetValueOrDefault(name, defaultOpen);
        var body = new StackPanel { Tag = name, Spacing = 4, Margin = new Thickness(0, 0, 0, 10), IsVisible = open };
        var content = new StackPanel();
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16") };
        heading.Children.Add(Copy(name, 11, MutedBrush, FontWeight.SemiBold));
        var arrow = Glyph(open ? "down" : "chevron", MutedBrush, 14); Grid.SetColumn(arrow, 1); heading.Children.Add(arrow);
        var toggle = Button(name, () => { body.IsVisible = !body.IsVisible; inspectorSections[name] = body.IsVisible; heading.Children.Remove(arrow); arrow = Glyph(body.IsVisible ? "down" : "chevron", MutedBrush, 14); Grid.SetColumn(arrow, 1); heading.Children.Add(arrow); });
        toggle.Content = heading; Avalonia.Automation.AutomationProperties.SetName(toggle, name); toggle.HorizontalContentAlignment = HorizontalAlignment.Stretch; toggle.Background = Brushes.Transparent; toggle.Classes.Add("subtle"); toggle.Padding = new Thickness(0); toggle.Height = 30;
        content.Children.Add(toggle); content.Children.Add(body);
        inspector.Children.Add(new Border { Tag = "section:" + name, Child = content, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 1, 0, 0) });
        return body;
    }
}
