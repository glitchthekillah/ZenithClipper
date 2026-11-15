using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;
using Control = Avalonia.Controls.Control;

/// <summary>Right-click menus for tracks and clips, drawn in the editor's own style instead of the stock Fluent menu.</summary>
public sealed partial class EditorWindow
{
    internal sealed record MenuItemSpec(string Icon, string Label, Action? Action, string? Hint = null, bool Danger = false, bool Enabled = true)
    {
        public static readonly MenuItemSpec Divider = new("", "", null);
    }
    Popup? openMenu;

    void ShowMenu(Control anchor, PointerEventArgs e, IReadOnlyList<MenuItemSpec> items)
    {
        openMenu?.Close();
        var list = new StackPanel { Spacing = 1, MinWidth = 220 };
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Pointer, IsLightDismissEnabled = true,
            Child = new Border
            {
                Background = Brush("#1B1C20"), BorderBrush = BorderLineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4),
                BoxShadow = BoxShadows.Parse("0 8 24 0 #80000000"), Child = list
            }
        };
        foreach (var item in items)
        {
            if (item.Action is null && item.Label.Length == 0) { list.Children.Add(new Border { Height = 1, Background = LineBrush, Margin = new Thickness(6, 4) }); continue; }
            IBrush color = !item.Enabled ? FaintBrush : item.Danger ? Brush("#FF6B6B") : TextBrush;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,Auto"), Height = 30 };
            row.Children.Add(Glyph(item.Icon, item.Danger && item.Enabled ? Brush("#FF6B6B") : item.Enabled ? SoftTextBrush : FaintBrush, 15));
            var label = Copy(item.Label, 12.5, color); Grid.SetColumn(label, 1); row.Children.Add(label);
            if (item.Hint is { } hint) { var h = Copy(hint, 11, FaintBrush); h.Margin = new Thickness(16, 0, 4, 0); Grid.SetColumn(h, 2); row.Children.Add(h); }
            var button = new Button { Content = row, Padding = new Thickness(6, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = item.Enabled, CornerRadius = new CornerRadius(5) };
            button.Template = new Avalonia.Controls.Templates.FuncControlTemplate<Button>((owner, _) =>
            {
                var presenter = new Avalonia.Controls.Presenters.ContentPresenter { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
                presenter.Bind(Avalonia.Controls.Presenters.ContentPresenter.ContentProperty, owner.GetObservable(ContentControl.ContentProperty));
                var border = new Border { CornerRadius = new CornerRadius(5), Child = presenter };
                border.Bind(Border.BackgroundProperty, owner.GetObservable(BackgroundProperty)); border.Bind(Border.PaddingProperty, owner.GetObservable(PaddingProperty));
                return border;
            });
            button.PointerEntered += (_, _) => button.Background = item.Danger ? Brush("#3A1A1D") : HoverBrush;
            button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
            button.Click += (_, _) => { popup.Close(); try { item.Action!(); } catch (Exception ex) { status.Text = ex.Message; } };
            Avalonia.Automation.AutomationProperties.SetName(button, item.Label);
            list.Children.Add(button);
        }
        // The popup lives in the anchor's visual tree; parent it to the window root so it survives header rebuilds.
        if (Content is Panel root) { root.Children.Add(popup); popup.Closed += (_, _) => root.Children.Remove(popup); }
        openMenu = popup; popup.Open();
        e.Handled = true;
    }

    IReadOnlyList<MenuItemSpec> TrackMenu(EditTrack track)
    {
        var id = track.Id; var display = DisplayTracks(); int index = display.FindIndex(t => t.Id == id), clips = track.Clips.Count;
        bool CanMove(int d) => index + d >= 0 && index + d < display.Count && display[index + d].Kind == track.Kind;
        bool video = track.Kind == TrackKind.Video;
        void Edit(string label, Action<EditTrack> change) => Change(label, p => change(p.Sequences.First(s => s.Id == p.ActiveSequenceId).Tracks.First(t => t.Id == id)));
        return
        [
            new("text", "Rename…", () => Run(() => RenameTrack(id))),
            new("link", track.SyncLock ? "Turn sync lock off" : "Turn sync lock on", () => Edit(track.SyncLock ? "Sync lock off" : "Sync lock on", t => t.SyncLock = !t.SyncLock)),
            new("media", "Add video track", () => AddTrack(TrackKind.Video)),
            new("audio", "Add audio track", () => AddTrack(TrackKind.Audio)),
            MenuItemSpec.Divider,
            new("previous", "Move up", () => MoveTrack(id, -1), Enabled: CanMove(-1)),
            new("next", "Move down", () => MoveTrack(id, 1), Enabled: CanMove(1)),
            MenuItemSpec.Divider,
            new(track.Muted ? "audio" : "muted", track.Muted ? "Unmute" : "Mute", () => Edit(track.Muted ? "Unmute track" : "Mute track", t => t.Muted = !t.Muted)),
            .. video ? new MenuItemSpec[] { new(track.Hidden ? "eye" : "hidden", track.Hidden ? "Show" : "Hide", () => Edit(track.Hidden ? "Show track" : "Hide track", t => t.Hidden = !t.Hidden)) } : [],
            new("lock", track.Locked ? "Unlock" : "Lock", () => Edit(track.Locked ? "Unlock track" : "Lock track", t => t.Locked = !t.Locked)),
            MenuItemSpec.Divider,
            new("delete", clips == 0 ? "Delete track" : $"Delete track and {clips} clip{(clips == 1 ? "" : "s")}", () => DeleteTrack(id), Danger: true, Enabled: !track.Locked && Sequence.Tracks.Count > 1),
        ];
    }

    IReadOnlyList<MenuItemSpec> ClipMenu(EditTrack track, TimelineClip clip, MediaAsset? media)
    {
        void Select() { selectedTrack = track.Id; selectedClip = clip.Id; }
        bool inside = playhead > clip.Start && playhead < clip.End;
        return
        [
            new("cut", "Add edit at playhead", () => { Select(); Split(); }, "Ctrl+K", Enabled: inside && !track.Locked),
            new("media", "Copy", () => { Select(); CopySelection(false); }, "Ctrl+C"),
            new("cut", "Cut", () => { Select(); CopySelection(true); }, "Ctrl+X", Enabled: !track.Locked),
            new("fx", "Paste attributes", () => { Select(); PasteAttributes(); }, "Ctrl+Alt+V", Enabled: clipboard.Count > 0 && !track.Locked),
            MenuItemSpec.Divider,
            new("transition", track.Kind == TrackKind.Audio ? "Add crossfade at start" : "Add dissolve at start", () => ApplyTransitionTo(track.Id, clip.Id, track.Kind == TrackKind.Audio ? "constant_power" : "cross_dissolve", true), Enabled: !track.Locked),
            new("transition", track.Kind == TrackKind.Audio ? "Add crossfade at end" : "Add dissolve at end", () => ApplyTransitionTo(track.Id, clip.Id, track.Kind == TrackKind.Audio ? "constant_power" : "cross_dissolve", false), Enabled: !track.Locked),
            .. track.Kind == TrackKind.Video && media?.Kind == MediaKind.Video ? new MenuItemSpec[]
            {
                new("pause", "Add frame hold", () => { long at = playhead; Change("Add frame hold", p => EditCommands.AddFrameHold(p, p.ActiveSequenceId, track.Id, clip.Id, at, false, 0)); }, Enabled: inside && !track.Locked),
                new("pause", "Insert frame hold segment", () => { long at = playhead, len = timelineFps.Frames(2); Change("Insert frame hold segment", p => EditCommands.AddFrameHold(p, p.ActiveSequenceId, track.Id, clip.Id, at, true, len)); }, Enabled: inside && !track.Locked),
            } : [],
            new(clip.Enabled ? "hidden" : "eye", clip.Enabled ? "Disable" : "Enable", () => { Select(); ToggleEnabled(); }, "Shift+E"),
            new("marker", "Mark clip (In/Out)", () => { Select(); MarkClip(); }, "X"),
            MenuItemSpec.Divider,
            .. LabelItems(clip),
            .. media?.Kind == MediaKind.Text ? new MenuItemSpec[] { new("text", "Edit text…", () => { Select(); Run(EditSelectedText); }) } : [],
            .. media is { Kind: MediaKind.Sequence, SequenceId: { } nestedId } ? new MenuItemSpec[] { new("media", "Open nested sequence", () => OpenSequence(nestedId)) } : [],
            new("media", "Nest…", () => { Select(); Run(NestSelected); }, Enabled: !track.Locked),
            .. media?.Kind == MediaKind.Video && track.Kind == TrackKind.Video ? new MenuItemSpec[] { new("cut", "Scene edit detection", () => { Select(); Run(SceneEditDetection); }, Enabled: !track.Locked) } : [],
            MenuItemSpec.Divider,
            new("delete", "Unlink video and audio", () => { Select(); UnlinkSelected(); }, Enabled: clip.LinkId is not null),
            new("media", clip.StreamIntent == ClipStreamIntent.Auto ? "Separate and link audio" : "Relink video and audio", () => { Select(); RelinkSelected(); }, Enabled: clip.LinkId is null && media is { Kind: MediaKind.Video, HasAudio: true }),
            MenuItemSpec.Divider,
            new("delete", "Delete", () => { Select(); Delete(false); }, "Del", Danger: true, Enabled: !track.Locked),
            new("ripple", "Ripple delete", () => { Select(); Delete(true); }, "Shift+Del", Danger: true, Enabled: !track.Locked),
        ];
    }

    static readonly (string Name, string? Color)[] Labels = [("Default", null), ("Violet", "#7E5BD6"), ("Iris", "#5B79D6"), ("Caribbean", "#2FA39A"), ("Lavender", "#B48CD9"), ("Cerulean", "#3C9AD6"), ("Forest", "#4C9A4C"), ("Rose", "#D9668C"), ("Mango", "#E59A3A"), ("Purple", "#9B4DCA"), ("Blue", "#3D5BD6"), ("Teal", "#2E8C8C"), ("Magenta", "#C64FA2"), ("Tan", "#B79A6B"), ("Green", "#5BAE3A"), ("Brown", "#8C5A3C"), ("Yellow", "#D6C23A")];
    /// <summary>Premiere's clip label colors.</summary>
    IEnumerable<MenuItemSpec> LabelItems(TimelineClip clip) => Labels.Where(l => l.Color != clip.Label).Take(9).Select(l =>
        new MenuItemSpec("marker", "Label: " + l.Name, () => { var id = clip.Id; var color = l.Color; Change("Label", p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, id, c => c.Label = color)); }));
    void DeleteTrack(Guid id)
    {
        var track = Sequence.Tracks.First(t => t.Id == id);
        Change("Delete track", p => { var s = p.Sequences.First(s => s.Id == p.ActiveSequenceId); if (s.Tracks.Count <= 1) throw new InvalidOperationException("A sequence needs at least one track."); s.Tracks.RemoveAll(t => t.Id == id && !t.Locked); });
        if (selectedTrack == id) { selectedTrack = Sequence.Tracks.FirstOrDefault()?.Id; selectedClip = null; }
        status.Text = $"Deleted {track.Name}. Ctrl+Z brings it back.";
    }
    /// <summary>Moves a track one row up or down on screen by swapping it with its on-screen neighbour of the same kind.</summary>
    void MoveTrack(Guid id, int delta)
    {
        var display = DisplayTracks(); int row = display.FindIndex(t => t.Id == id), other = row + delta;
        if (row < 0 || other < 0 || other >= display.Count || display[other].Kind != display[row].Kind) return;
        Guid neighbour = display[other].Id;
        Change("Move track", p =>
        {
            var tracks = p.Sequences.First(s => s.Id == p.ActiveSequenceId).Tracks; int i = tracks.FindIndex(t => t.Id == id), j = tracks.FindIndex(t => t.Id == neighbour);
            (tracks[i], tracks[j]) = (tracks[j], tracks[i]);
        });
    }
    async Task RenameTrack(Guid id)
    {
        var track = Sequence.Tracks.First(t => t.Id == id);
        var box = new TextBox { Text = track.Name, MaxLength = 60 };
        var dialog = new Window { Title = "Rename track", Width = 380, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = PanelBrush, Foreground = TextBrush };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = "Rename track", FontSize = 15, FontWeight = FontWeight.SemiBold }); panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var save = Button("Rename", () => dialog.Close(box.Text)); save.HorizontalAlignment = HorizontalAlignment.Left; save.Background = AccentBrush; save.Foreground = InkBrush; save.Classes.Add("primary");
        var cancel = Button("Cancel", () => dialog.Close(null)); cancel.HorizontalAlignment = HorizontalAlignment.Left;
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons); dialog.Content = panel;
        box.KeyDown += (_, k) => { if (k.Key == Key.Enter) dialog.Close(box.Text); else if (k.Key == Key.Escape) dialog.Close(null); };
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        var name = (await dialog.ShowDialog<string?>(this))?.Trim();
        if (string.IsNullOrEmpty(name) || name == track.Name) return;
        Change("Rename track", p => p.Sequences.First(s => s.Id == p.ActiveSequenceId).Tracks.First(t => t.Id == id).Name = name);
    }
}
