using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Full-screen playback (Premiere Ctrl+`): the program on a black full-screen window; Esc returns.</summary>
public sealed partial class EditorWindow
{
    Window? fullScreenWindow; Image? fullScreenImage;
    long droppedFrames, lastPlayedFrame = -1;
    readonly Border droppedIndicator = new() { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    void UpdateDroppedIndicator()
    {
        droppedIndicator.Background = droppedFrames == 0 ? new SolidColorBrush(Color.Parse("#3FBF6A")) : droppedFrames < 10 ? AmberBrush : new SolidColorBrush(Color.Parse("#E5484D"));
        ToolTip.SetTip(droppedIndicator, droppedFrames == 0 ? "No dropped frames" : $"{droppedFrames} dropped frame{(droppedFrames == 1 ? "" : "s")} during playback. Try a lower playback resolution or proxies. Click to reset.");
    }

    void ToggleFullScreen() => ShowProgramFullScreen(false);
    /// <summary>Full-screen program; with <paramref name="otherDisplay"/>, on a display other than the editor's
    /// (Premiere's Mercury Transmit to a second monitor), leaving the editor usable.</summary>
    void ShowProgramFullScreen(bool otherDisplay)
    {
        if (fullScreenWindow is not null) { fullScreenWindow.Close(); return; }
        Avalonia.Platform.Screen? target = null;
        if (otherDisplay)
        {
            var mine = Screens.ScreenFromWindow(this);
            target = Screens.All.FirstOrDefault(x => !x.Equals(mine));
            if (target is null) { status.Text = "Only one display is connected; showing the program full screen here instead."; otherDisplay = false; }
        }
        fullScreenImage = new Image { Stretch = Stretch.Uniform, Source = bitmap };
        var w = new Window { Background = Brushes.Black, Content = fullScreenImage, WindowDecorations = Avalonia.Controls.WindowDecorations.None, Title = "Program", ShowActivated = !otherDisplay };
        if (target is not null) { w.WindowStartupLocation = WindowStartupLocation.Manual; w.Position = target.Bounds.Position; w.Width = target.Bounds.Width / target.Scaling; w.Height = target.Bounds.Height / target.Scaling; w.Opened += (_, _) => w.WindowState = WindowState.FullScreen; }
        else w.WindowState = WindowState.FullScreen;
        w.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.OemTilde && e.KeyModifiers.HasFlag(KeyModifiers.Control))) { w.Close(); e.Handled = true; return; }
            // Everything else (Space, J/K/L, arrows…) drives the editor as usual.
            OnKey(this, e);
        };
        bool second = otherDisplay;
        w.Closed += (_, _) => { fullScreenWindow = null; fullScreenImage = null; if (!second) Activate(); };
        fullScreenWindow = w; w.Show(this);
        if (second) { Activate(); status.Text = "Program is on the second display. Use Window → Program on second display again to close it."; }
    }
}
