using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ZenithClipper.Editing;
using ZenithClipper.Mlt;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Premiere's effect presets: save a clip's effect stack under a name and apply it to other clips.</summary>
public sealed partial class EditorWindow
{
    internal sealed class EffectPreset { public string Name { get; set; } = ""; public bool Audio { get; set; } public List<ClipEffect> Effects { get; set; } = []; }

    static string SettingsDirectory => Directory.Exists(Path.Combine(AppContext.BaseDirectory, "mlt"))
        ? Path.Combine(AppContext.BaseDirectory, "settings")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZenithClipper");
    static string PresetsPath => Path.Combine(SettingsDirectory, "effect-presets.json");

    internal static List<EffectPreset> LoadPresets()
    {
        try { return File.Exists(PresetsPath) ? JsonSerializer.Deserialize<List<EffectPreset>>(File.ReadAllText(PresetsPath)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Log(ex); return []; }
    }
    static void SavePresets(List<EffectPreset> presets)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temp = PresetsPath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, PresetsPath, true);
    }

    async Task SaveEffectPreset(TimelineClip clip, bool audio)
    {
        var effects = clip.Effects.Where(e => EffectCatalog.Find(e.Id) is { } d && (d.Kind == EffectKind.Audio) == audio).Select(ProjectValidation.Clone).ToList();
        if (effects.Count == 0) { status.Text = "This clip has no effects to save."; return; }
        var name = await Prompt("Save effect preset", "Name this preset. It appears under Presets in the Effects tab.", string.Join(" + ", effects.Select(e => EffectCatalog.Find(e.Id)!.Name)));
        if (string.IsNullOrWhiteSpace(name)) return;
        var presets = LoadPresets(); presets.RemoveAll(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        presets.Add(new EffectPreset { Name = name.Trim(), Audio = audio, Effects = effects }); SavePresets(presets);
        status.Text = $"Saved preset \"{name.Trim()}\"."; UpdateEffectsBrowser();
    }

    internal void ApplyPresetTo(Guid clipId, string presetName)
    {
        var preset = LoadPresets().FirstOrDefault(p => p.Name == presetName); if (preset is null) return;
        var track = Sequence.Tracks.FirstOrDefault(t => t.Clips.Any(c => c.Id == clipId)); if (track is null) return;
        if (preset.Audio != (track.Kind == TrackKind.Audio)) { status.Text = preset.Audio ? "That's an audio preset. Use it on a clip with sound." : "That's a video preset. Use it on a video clip."; return; }
        var copies = preset.Effects.Select(ProjectValidation.Clone).ToList();
        Change("Apply preset " + preset.Name, p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, clipId, c => c.Effects.AddRange(copies.Select(ProjectValidation.Clone))));
        selectedClip = clipId; BuildInspector(); timeline.Rebuild(); status.Text = $"Applied preset \"{preset.Name}\".";
    }

    void DeletePreset(string name) { var presets = LoadPresets(); presets.RemoveAll(p => p.Name == name); SavePresets(presets); UpdateEffectsBrowser(); }

    /// <summary>Preset rows for the Effects tab: click applies to the selected clip, right-click deletes.</summary>
    void AddPresetRows(StackPanel list, Func<string, string, bool> match)
    {
        var presets = LoadPresets().Where(p => match(p.Name, "Presets")).ToList(); if (presets.Count == 0) return;
        list.Children.Add(new TextBlock { Text = "PRESETS", FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = FaintBrush, Margin = new Thickness(4, 10, 0, 4) });
        foreach (var preset in presets)
        {
            var name = preset.Name;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*"), Height = 28 };
            row.Children.Add(Glyph(preset.Audio ? "audio" : "fx", AmberBrush, 14));
            var label = Copy(name, 12, SoftTextBrush); Grid.SetColumn(label, 1); row.Children.Add(label);
            var host = new Border { Child = row, Padding = new Thickness(6, 0), CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            ToolTip.SetTip(host, $"{string.Join(", ", preset.Effects.Select(e => EffectCatalog.Find(e.Id)?.Name ?? e.Id))}\nClick to apply to the selected clip. Right-click to delete.");
            host.PointerEntered += (_, _) => host.Background = HoverBrush; host.PointerExited += (_, _) => host.Background = Brushes.Transparent;
            host.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Right) { DeletePreset(name); status.Text = $"Deleted preset \"{name}\"."; return; }
                if (selectedClip is { } c) ApplyPresetTo(c, name); else status.Text = "Select a clip first.";
            };
            list.Children.Add(host);
        }
    }

    async Task<string?> Prompt(string title, string message, string initial)
    {
        var dialog = new Window { Title = title, Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = PanelBrush, Foreground = TextBrush };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = SoftTextBrush });
        var box = new TextBox { Text = initial }; panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = Button("Save", () => dialog.Close(box.Text)); ok.Background = AccentBrush; ok.Foreground = InkBrush; ok.Classes.Add("primary");
        buttons.Children.Add(ok); buttons.Children.Add(Button("Cancel", () => dialog.Close(null))); panel.Children.Add(buttons);
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) dialog.Close(box.Text); else if (e.Key == Avalonia.Input.Key.Escape) dialog.Close(null); };
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        return await dialog.ShowDialog<string?>(this);
    }
}
