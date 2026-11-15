using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Preferences (autosave, default durations, track height) and Premiere-style workspaces.</summary>
public sealed partial class EditorWindow
{
    internal sealed class EditorPreferences
    {
        public double AutosaveSeconds { get; set; } = 30;
        public double TransitionSeconds { get; set; } = 1;
        public double StillSeconds { get; set; } = 5;
        public int TrackHeight { get; set; } = 52;
        public double PreRollSeconds { get; set; } = 2;
        public double PostRollSeconds { get; set; } = 2;
        public int LargeTrimOffset { get; set; } = 5;
    }
    static string PreferencesPath => Path.Combine(SettingsDirectory, "preferences.json");
    internal static EditorPreferences Prefs { get; private set; } = LoadPreferences();
    Action? rebuildRightTabs;

    static EditorPreferences LoadPreferences()
    {
        try { var p = File.Exists(PreferencesPath) ? JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(PreferencesPath)) ?? new() : new(); TrackHeight = Math.Clamp(p.TrackHeight, 36, 124); return p; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    async Task EditPreferences()
    {
        var dialog = new Window { Title = "Preferences", Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = PanelBrush, Foreground = TextBrush };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Preferences", FontSize = 15, FontWeight = FontWeight.SemiBold });
        TextBox Field(string label, double value) { panel.Children.Add(new TextBlock { Text = label, Foreground = SoftTextBrush, FontSize = 12 }); var b = new TextBox { Text = value.ToString("0.##", CultureInfo.InvariantCulture) }; panel.Children.Add(b); return b; }
        var autosaveBox = Field("Autosave every (seconds, 10 to 600)", Prefs.AutosaveSeconds);
        var transitionBox = Field("Default transition duration (seconds)", Prefs.TransitionSeconds);
        var stillBox = Field("Default still / title / generated item duration (seconds)", Prefs.StillSeconds);
        var heightBox = Field("Track height (36 to 124 px)", TrackHeight);
        var preBox = Field("Playback pre-roll for Play around, Shift+K (seconds)", Prefs.PreRollSeconds);
        var postBox = Field("Playback post-roll (seconds)", Prefs.PostRollSeconds);
        var largeBox = Field("Large trim and nudge offset (frames)", Prefs.LargeTrimOffset);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = Button("Save", () => dialog.Close(true)); ok.Background = AccentBrush; ok.Foreground = InkBrush; ok.Classes.Add("primary");
        buttons.Children.Add(Button("Cancel", () => dialog.Close(false))); buttons.Children.Add(ok); panel.Children.Add(buttons);
        if (!await dialog.ShowDialog<bool>(this)) return;
        double Read(TextBox b, double min, double max, double fallback) => double.TryParse(b.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, min, max) : fallback;
        Prefs = new EditorPreferences
        {
            AutosaveSeconds = Read(autosaveBox, 10, 600, Prefs.AutosaveSeconds), TransitionSeconds = Read(transitionBox, .04, 30, Prefs.TransitionSeconds),
            StillSeconds = Read(stillBox, .1, 600, Prefs.StillSeconds), TrackHeight = (int)Read(heightBox, 36, 124, TrackHeight),
            PreRollSeconds = Read(preBox, 0, 30, Prefs.PreRollSeconds), PostRollSeconds = Read(postBox, 0, 30, Prefs.PostRollSeconds), LargeTrimOffset = (int)Read(largeBox, 2, 100, Prefs.LargeTrimOffset)
        };
        Directory.CreateDirectory(SettingsDirectory); File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(Prefs, new JsonSerializerOptions { WriteIndented = true }));
        autosave.Interval = TimeSpan.FromSeconds(Prefs.AutosaveSeconds); TrackHeight = Prefs.TrackHeight; BuildTrackHeaders(); timeline.Rebuild();
        status.Text = "Preferences saved.";
    }

    /// <summary>Workspaces: arrange the panels for a kind of work, like Premiere's Editing / Color / Audio / Effects / Captions.</summary>
    void Workspace(string name)
    {
        (libraryFilter, rightPanel, inspectorTab) = name switch
        {
            "Color" => ("Media", "Inspector", "Color"),
            "Audio" => ("Media", "Mixer", "Audio"),
            "Effects" => ("Effects", "Inspector", "Effects"),
            "Captions" => ("Captions", "Inspector", "Video"),
            "History" => (libraryFilter, "History", inspectorTab),
            _ => ("Media", "Inspector", "Video"),
        };
        BuildLibraryTabs(); UpdateLibrary(); rebuildRightTabs?.Invoke(); BuildInspector();
        status.Text = name == "History" ? "History panel." : $"{name} workspace.";
    }
}
