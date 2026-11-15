using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ZenithClipper.Editor.Desktop;

/// <summary>
/// Customizable keyboard shortcuts: each command keeps its Premiere default, and you can add your own key for it.
/// Your key is translated to the default before the editor handles it, so every command stays available.
/// </summary>
public sealed partial class EditorWindow
{
    internal static readonly (string Default, string Command)[] DefaultShortcuts =
    [
        ("Space", "Play / pause"), ("J", "Shuttle left"), ("K", "Stop"), ("Shift+K", "Play around"), ("Enter", "Render In to Out"), ("L", "Shuttle right"), ("I", "Mark In"), ("O", "Mark Out"),
        ("V", "Selection tool"), ("A", "Track select forward tool"), ("B", "Ripple edit tool"), ("N", "Rolling edit tool"), ("R", "Rate stretch tool"),
        ("C", "Razor tool"), ("Y", "Slip tool"), ("U", "Slide tool"), ("H", "Hand tool"), ("Z", "Zoom tool"),
        ("Ctrl+K", "Add edit at playhead"), ("Q", "Ripple trim previous edit to playhead"), ("W", "Ripple trim next edit to playhead"),
        ("Delete", "Delete"), ("Shift+Delete", "Ripple delete"), ("Ctrl+C", "Copy"), ("Ctrl+X", "Cut"), ("Ctrl+V", "Paste (overwrite)"), ("Ctrl+Shift+V", "Paste insert"),
        ("Ctrl+Z", "Undo"), ("Ctrl+Shift+Z", "Redo"), ("Ctrl+D", "Default video transition"), ("Shift+D", "Default transitions (video and audio)"),
        ("M", "Add marker"), ("Ctrl+G", "Group"), ("Ctrl+Shift+G", "Ungroup"), ("Alt+Left", "Nudge left"), ("Alt+Right", "Nudge right"),
        ("OemComma", "Insert from Source"), ("OemPeriod", "Overwrite from Source"), ("Ctrl+U", "Make subclip"), ("Ctrl+M", "Export"),
        ("Shift+V", "Record voice-over"), ("Ctrl+OemPlus", "Taller tracks"), ("Ctrl+OemMinus", "Shorter tracks"), ("OemPlus", "Zoom in timeline"), ("OemMinus", "Zoom out timeline"),
        ("S", "Snapping on/off"), ("Ctrl+OemTilde", "Full screen playback"), ("Home", "Go to start"), ("End", "Go to end"), ("Ctrl+S", "Save"),
    ];

    static string ShortcutsPath => Path.Combine(SettingsDirectory, "shortcuts.json");
    Dictionary<string, string> shortcutAliases = LoadShortcuts();

    static Dictionary<string, string> LoadShortcuts()
    {
        try { return File.Exists(ShortcutsPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ShortcutsPath)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    static string Gesture(Key key, KeyModifiers mods) => new KeyGesture(key, mods & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta)).ToString();

    /// <summary>The default key a custom key stands for, as a fresh key event; null when the key isn't customized.</summary>
    KeyEventArgs? ShortcutAlias(KeyEventArgs e)
    {
        if (shortcutAliases.Count == 0 || !shortcutAliases.TryGetValue(Gesture(e.Key, e.KeyModifiers), out var target)) return null;
        try { var g = KeyGesture.Parse(target); return new KeyEventArgs { Key = g.Key, KeyModifiers = g.KeyModifiers, RoutedEvent = e.RoutedEvent, Source = e.Source }; }
        catch (ArgumentException) { return null; }
    }

    async Task EditShortcuts()
    {
        var dialog = new Window { Title = "Keyboard shortcuts", Width = 560, Height = 640, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = PanelBrush, Foreground = TextBrush };
        var list = new StackPanel { Spacing = 2, Margin = new Thickness(18) };
        list.Children.Add(new TextBlock { Text = "Add your own key for any command. Click a box and press the key; Backspace clears it. Premiere defaults keep working.", TextWrapping = TextWrapping.Wrap, Foreground = SoftTextBrush, Margin = new Thickness(0, 0, 0, 8) });
        var boxes = new List<(string Default, TextBox Box)>();
        foreach (var (def, command) in DefaultShortcuts)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120,140"), MinHeight = 30 };
            row.Children.Add(Copy(command, 12, TextBrush));
            var d = Copy(KeyGesture.Parse(def).ToString(), 11.5, FaintBrush); Grid.SetColumn(d, 1); row.Children.Add(d);
            string current = shortcutAliases.FirstOrDefault(x => x.Value == def).Key ?? "";
            var box = new TextBox { Text = current, IsReadOnly = true, FontSize = 11.5, Height = 26, MinHeight = 26, PlaceholderText = "add key" };
            box.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
                box.Text = e.Key is Key.Back && e.KeyModifiers == KeyModifiers.None ? "" : Gesture(e.Key, e.KeyModifiers); e.Handled = true;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Grid.SetColumn(box, 2); row.Children.Add(box); list.Children.Add(row); boxes.Add((def, box));
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var save = Button("Save", () => dialog.Close(true)); save.Background = AccentBrush; save.Foreground = InkBrush; save.Classes.Add("primary");
        buttons.Children.Add(Button("Reset all", () => { foreach (var (_, b) in boxes) b.Text = ""; })); buttons.Children.Add(Button("Cancel", () => dialog.Close(false))); buttons.Children.Add(save);
        list.Children.Add(buttons);
        dialog.Content = new ScrollViewer { Content = list };
        if (!await dialog.ShowDialog<bool>(this)) return;
        var map = new Dictionary<string, string>();
        foreach (var (def, box) in boxes) if (!string.IsNullOrWhiteSpace(box.Text)) map[box.Text] = KeyGesture.Parse(def).ToString();
        Directory.CreateDirectory(SettingsDirectory); File.WriteAllText(ShortcutsPath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        shortcutAliases = map; status.Text = $"Saved {map.Count} custom shortcut{(map.Count == 1 ? "" : "s")}.";
    }

    internal void VerifyShortcuts()
    {
        var keep = shortcutAliases; var keepTool = tool;
        try
        {
            shortcutAliases = new() { [Gesture(Key.B, KeyModifiers.Control)] = KeyGesture.Parse("C").ToString() };
            var e = new KeyEventArgs { Key = Key.B, KeyModifiers = KeyModifiers.Control, RoutedEvent = KeyDownEvent };
            OnKey(this, e);
            if (tool != EditTool.Razor || !e.Handled) throw new InvalidOperationException("A custom shortcut did not run its command.");
            foreach (var (def, _) in DefaultShortcuts) KeyGesture.Parse(def);
        }
        finally { shortcutAliases = keep; SetTool(keepTool); }
        Console.WriteLine("Shortcut smoke passed: custom key runs its command; every default parses.");
    }
}
