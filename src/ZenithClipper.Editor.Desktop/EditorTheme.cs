using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;

namespace ZenithClipper.Editor.Desktop;

/// <summary>App-wide styles in the ZenithClipper look. Every colour comes from EditorWindow's theme brushes, so
/// Settings > Appearance recolours inputs, lists, menus and tooltips live.</summary>
internal static class EditorTheme
{
    static Style S(Func<Selector?, Selector> selector, params (AvaloniaProperty Property, object? Value)[] setters)
    {
        var style = new Style(selector);
        foreach (var (p, v) in setters) style.Setters.Add(new Setter(p, v));
        return style;
    }

    internal static void Apply(Application app)
    {
        var text = Brush.Parse("#E8E9EC");
        app.Styles.Add(S(x => x.OfType<TextBox>(),
            (TextBox.BackgroundProperty, EditorWindow.ChromeBrush), (TextBox.ForegroundProperty, text), (TextBox.BorderBrushProperty, EditorWindow.BorderLineBrush),
            (TextBox.CornerRadiusProperty, new CornerRadius(5)), (TextBox.FontSizeProperty, 12d), (TextBox.MinHeightProperty, 28d), (TextBox.PaddingProperty, new Thickness(8, 0)),
            (TextBox.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center), (TextBox.SelectionBrushProperty, EditorWindow.AccentGhostBrush)));
        // Fluent pads combo boxes 5 px top / 7 px bottom, which sits the text 1 px high. Even it out and match our inputs.
        app.Styles.Add(S(x => x.OfType<ComboBox>(),
            (ComboBox.PaddingProperty, new Thickness(10, 6, 0, 6)), (ComboBox.BackgroundProperty, EditorWindow.ChromeBrush), (ComboBox.BorderBrushProperty, EditorWindow.BorderLineBrush),
            (ComboBox.CornerRadiusProperty, new CornerRadius(5)), (ComboBox.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center)));
        app.Styles.Add(S(x => x.OfType<ListBoxItem>(),
            (ListBoxItem.PaddingProperty, new Thickness(6, 0)), (ListBoxItem.CornerRadiusProperty, new CornerRadius(5)), (ListBoxItem.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch)));
        // Selected media cards: a quiet raised fill with an accent edge, so the name and details stay readable
        // (Fluent's default fills the whole card with the accent and the grey details vanish into it).
        foreach (var state in new[] { ":pointerover", ":selected", ":selected:pointerover", ":selected:focus" })
        {
            bool selected = state.StartsWith(":selected", StringComparison.Ordinal);
            app.Styles.Add(S(x => Classes(x.OfType<ListBoxItem>(), state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                (ContentPresenter.BackgroundProperty, selected ? EditorWindow.SelectedBrush : EditorWindow.HoverBrush),
                (ContentPresenter.BorderBrushProperty, selected ? EditorWindow.AccentBrush : Brushes.Transparent),
                (ContentPresenter.BorderThicknessProperty, new Thickness(selected ? 1.5 : 0))));
        }

        // Right-click menus: our own surface, rounded, roomy rows, accent-free hover, a short fade in.
        var menu = S(x => x.OfType<ContextMenu>(),
            (ContextMenu.BackgroundProperty, EditorWindow.PanelBrush), (ContextMenu.BorderBrushProperty, EditorWindow.BorderLineBrush), (ContextMenu.BorderThicknessProperty, new Thickness(1)),
            (ContextMenu.CornerRadiusProperty, new CornerRadius(8)), (ContextMenu.PaddingProperty, new Thickness(4)), (ContextMenu.MinWidthProperty, 210d),
            (ContextMenu.FontSizeProperty, 12.5d), (ContextMenu.ForegroundProperty, text));
        if (!EditorWindow.Look.ReduceMotion)
            menu.Animations.Add(new Animation
            {
                Duration = TimeSpan.FromMilliseconds(130), Easing = new CubicEaseOut(), FillMode = FillMode.Forward,
                Children = { Frame(0, 0d, 4d), Frame(1, 1d, 0d) },
            });
        app.Styles.Add(menu);
        app.Styles.Add(S(x => x.OfType<MenuItem>(),
            (MenuItem.HeightProperty, 30d), (MenuItem.PaddingProperty, new Thickness(10, 0)), (MenuItem.FontSizeProperty, 12.5d),
            (MenuItem.ForegroundProperty, text), (MenuItem.CornerRadiusProperty, new CornerRadius(5))));
        foreach (var state in new[] { ":pointerover", ":selected", ":open" })
            app.Styles.Add(S(x => Classes(x.OfType<MenuItem>(), state).Template().OfType<Border>().Name("PART_LayoutRoot"),
                (Border.BackgroundProperty, EditorWindow.HoverBrush)));
        app.Styles.Add(S(x => x.OfType<MenuItem>().Class(":disabled"), (MenuItem.OpacityProperty, .45)));
        app.Styles.Add(S(x => x.OfType<Separator>(),
            (Separator.BackgroundProperty, EditorWindow.LineBrush), (Separator.MarginProperty, new Thickness(6, 4)), (Separator.HeightProperty, 1d)));
        app.Styles.Add(S(x => x.OfType<ToolTip>(),
            (ToolTip.BackgroundProperty, EditorWindow.ControlBrush), (ToolTip.ForegroundProperty, text), (ToolTip.BorderBrushProperty, EditorWindow.BorderLineBrush),
            (ToolTip.BorderThicknessProperty, new Thickness(1)), (ToolTip.CornerRadiusProperty, new CornerRadius(6)), (ToolTip.FontSizeProperty, 11.5d), (ToolTip.PaddingProperty, new Thickness(8, 5))));
    }

    static Selector Classes(Selector selector, string state)
    {
        foreach (var part in state.Split(':', StringSplitOptions.RemoveEmptyEntries)) selector = selector.Class(":" + part);
        return selector;
    }
    static KeyFrame Frame(double cue, double opacity, double y) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(Visual.OpacityProperty, opacity), new Setter(Avalonia.Media.TranslateTransform.YProperty, y) },
    };
}
