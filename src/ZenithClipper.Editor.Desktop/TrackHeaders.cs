using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ZenithClipper.Editing;
namespace ZenithClipper.Editor.Desktop;
public sealed partial class EditorWindow
{
    void BuildTrackHeaders()
    {
        headers.Children.Clear();
        headers.Children.Add(new Border { Height = RulerHeight, Background = PanelBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = Copy("TRACKS", 10, FaintBrush, FontWeight.SemiBold), Padding = new Thickness(12, 0) });
        // Numbers follow the edit model (V1 is the bottom video layer); rows are shown mirrored around the divider.
        var codes = new Dictionary<Guid, string>(); int video = 0, audio = 0;
        foreach (var t in Sequence.Tracks)
        {
            bool c = IsCaptionTrack(t), ti = t.Kind == TrackKind.Video && t.Name.StartsWith("Titles", StringComparison.Ordinal);
            codes[t.Id] = c ? "CC" : ti ? "T" : t.Kind == TrackKind.Audio ? $"A{++audio}" : $"V{++video}";
        }
        var rows = DisplayTracks();
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var track = rows[rowIndex];
            if (HasDivider && rowIndex == VideoRows) headers.Children.Add(new Border { Height = DividerHeight, Background = DeepBrush, BorderBrush = BorderLineBrush, BorderThickness = new Thickness(0, 1) });
            var id = track.Id; bool caption = IsCaptionTrack(track), title = track.Kind == TrackKind.Video && track.Name.StartsWith("Titles", StringComparison.Ordinal);
            string code = codes[id];
            IBrush tag = caption ? FaintBrush : title ? AmberBrush : track.Kind == TrackKind.Audio ? MutedBrush : AccentBrush;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,Auto"), Margin = new Thickness(9, 0, 4, 0) };
            // Source patching / targeting (Premiere): click V1/A1 to choose where Insert, Overwrite and Paste land.
            Guid? target = track.Kind == TrackKind.Video ? targetVideo : targetAudio; bool targeted = target == id, kindOff = target == Guid.Empty;
            var codeButton = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(3, 1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Background = targeted ? AccentBrush : Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Child = Copy(code, 11, targeted ? InkBrush : kindOff ? FaintBrush : MutedBrush, FontWeight.SemiBold) };
            ToolTip.SetTip(codeButton, targeted ? $"Target for Insert / Overwrite / Paste. Click to turn {(track.Kind == TrackKind.Video ? "video" : "audio")} targeting off." : $"Click to target {code} for Insert / Overwrite / Paste.");
            codeButton.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(codeButton).Properties.IsLeftButtonPressed) return; e.Handled = true; if (track.Kind == TrackKind.Video) targetVideo = targeted ? Guid.Empty : id; else targetAudio = targeted ? Guid.Empty : id; BuildTrackHeaders(); };
            row.Children.Add(codeButton);
            var select = Button(track.Name, () => { selectedTrack = id; Refresh(); }); select.Background = Brushes.Transparent; select.Classes.Add("subtle"); select.Height = 26; select.Padding = new Thickness(2, 0); select.HorizontalContentAlignment = HorizontalAlignment.Left;
            select.Content = Copy(track.Name == AutoCaptionTrack ? "Auto captions" : track.Name, 12, selectedTrack == id ? TextBrush : SoftTextBrush, FontWeight.Medium); Avalonia.Automation.AutomationProperties.SetName(select, "Select " + track.Name);
            Grid.SetColumn(select, 1); row.Children.Add(select);
            var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(flags, 2); row.Children.Add(flags);
            if (track.Kind == TrackKind.Video) Flag(track.Hidden ? "hidden" : "eye", track.Hidden, track.Hidden ? "Show track" : "Hide track", t => t.Hidden = !t.Hidden);
            if (track.Kind == TrackKind.Audio) Flag("solo", track.Solo, track.Solo ? "Unsolo track" : "Solo track", t => t.Solo = !t.Solo);
            if (track.Kind == TrackKind.Audio || !caption && !title) Flag(track.Muted ? "muted" : "audio", track.Muted, track.Muted ? "Unmute track" : "Mute track", t => t.Muted = !t.Muted);
            Flag("lock", track.Locked, track.Locked ? "Unlock track" : "Lock track", t => t.Locked = !t.Locked);
            var header = new Border
            {
                Height = TrackHeight, Tag = id, Child = new Border { Child = row, BorderBrush = tag, BorderThickness = new Thickness(3, 0, 0, 0) },
                Background = selectedTrack == id ? Brush("#1F2125") : PanelBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var owned = track;
            header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(header).Properties.IsRightButtonPressed) { selectedTrack = id; ShowMenu(header, e, TrackMenu(owned)); } };
            ToolTip.SetTip(header, "Right-click for track options");
            headers.Children.Add(header);
            void Flag(string icon, bool active, string label, Action<EditTrack> mutate)
            {
                var button = ActionButton(icon, label, () => Change(label, p => mutate(p.Sequences.First(s => s.Id == p.ActiveSequenceId).Tracks.First(t => t.Id == id))), iconOnly: true);
                button.Width = 26; button.Height = 26; button.Content = Glyph(icon, active ? (icon == "lock" ? AmberBrush : AccentSoftBrush) : MutedBrush, 14); flags.Children.Add(button);
            }
        }
        // Same empty drop space below the last lane as the timeline, so both columns scroll together.
        headers.Children.Add(new Border { Height = TrackHeight, Background = PanelBrush });
    }
}
