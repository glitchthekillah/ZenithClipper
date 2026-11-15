using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Sequence tabs above the timeline, new/rename/delete sequence, Nest and opening nested sequences.</summary>
public sealed partial class EditorWindow
{
    readonly StackPanel sequenceTabs = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    string sequenceTabsKey = "";

    void UpdateSequenceTabs()
    {
        var project = ProjectCopy;
        string key = project.ActiveSequenceId + "|" + string.Join("|", project.Sequences.Select(s => s.Id + s.Name));
        if (key == sequenceTabsKey) return;
        sequenceTabsKey = key; sequenceTabs.Children.Clear();
        foreach (var seq in project.Sequences)
        {
            bool on = seq.Id == project.ActiveSequenceId; var id = seq.Id;
            var tab = Button(seq.Name, () => OpenSequence(id));
            tab.Height = 24; tab.Padding = new Thickness(10, 0); tab.FontSize = 12; tab.HorizontalAlignment = HorizontalAlignment.Left; tab.MaxWidth = 220;
            tab.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal; tab.Foreground = on ? TextBrush : MutedBrush;
            tab.Classes.Add("subtle"); tab.Tag = on ? SelectedBrush : null; tab.Background = on ? SelectedBrush : Brushes.Transparent;
            ToolTip.SetTip(tab, $"{seq.Name} · {seq.Width}×{seq.Height} · {seq.Fps.Value:0.###} fps\nRight-click to rename or delete");
            tab.ContextRequested += (_, e) =>
            {
                e.Handled = true;
                var menu = new ContextMenu { ItemsSource = new[]
                {
                    MenuItem("Rename / settings…", () => { OpenSequence(id); Run(SequenceSettings); }),
                    MenuItem("Duplicate", () => DuplicateSequence(id)),
                    MenuItem("Delete sequence", () => Change("Delete sequence", p => EditCommands.DeleteSequence(p, id))),
                } };
                menu.Open(tab);
            };
            sequenceTabs.Children.Add(tab);
        }
        var add = ActionButton("plus", "New sequence", NewSequence, iconOnly: true); add.Height = 24; add.Width = 24;
        sequenceTabs.Children.Add(add);
    }

    static MenuItem MenuItem(string header, Action action) { var item = new MenuItem { Header = header }; item.Click += (_, _) => action(); return item; }

    internal void OpenSequence(Guid id)
    {
        if (id == Project.ActiveSequenceId) return;
        try
        {
            session.OpenSequence(id);
            selectedClip = null; extraSelection.Clear(); selectedTrack = null; playhead = 0;
            Refresh(true);
            FitTimeline();
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    void NewSequence()
    {
        int n = Project.Sequences.Count + 1;
        Change("New sequence", p => EditCommands.NewSequence(p, "Sequence " + n));
        selectedClip = null; playhead = 0; Refresh(true);
    }

    void DuplicateSequence(Guid id)
    {
        Change("Duplicate sequence", p =>
        {
            var copy = ProjectValidation.Clone(p.Sequences.First(s => s.Id == id));
            copy.Id = Guid.NewGuid(); copy.Name += " copy";
            foreach (var t in copy.Tracks) { t.Id = Guid.NewGuid(); foreach (var c in t.Clips) c.Id = Guid.NewGuid(); }
            // Linked pairs keep linking each other in the copy, with fresh link ids.
            var map = new Dictionary<Guid, Guid>();
            foreach (var c in copy.Tracks.SelectMany(t => t.Clips).Where(c => c.LinkId is not null)) { if (!map.TryGetValue(c.LinkId!.Value, out var l)) map[c.LinkId.Value] = l = Guid.NewGuid(); c.LinkId = l; }
            p.Sequences.Add(copy); p.ActiveSequenceId = copy.Id; EditCommands.EnsureSequenceMedia(p, copy);
        });
    }

    async Task NestSelected()
    {
        if (selectedClip is not { } id) { status.Text = "Select a clip to nest."; return; }
        string name = $"Nested Sequence {Project.Sequences.Count}";
        Guid? result = null;
        var ids = SelectedClips.ToList();
        Change("Nest", p => result = EditCommands.Nest(p, p.ActiveSequenceId, EditCommands.Closure(p, p.ActiveSequenceId, ids), name));
        if (result is { } nested) { selectedClip = nested; extraSelection.Clear(); Refresh(); status.Text = "Nested. Double-click the clip to open it."; }
        await Task.CompletedTask;
    }
}

public sealed partial class EditorWindow
{
    /// <summary>
    /// Premiere's New Item → Adjustment Layer: a 5 s clip on the top-most free video track (a new one if needed).
    /// Effects added to it apply to everything below it.
    /// </summary>
    void AddAdjustmentLayer()
    {
        if (PlaceNewItem(new MediaAsset { Name = "Adjustment Layer", Kind = MediaKind.Adjustment, Path = "" }))
        { inspectorTab = "Effects"; Refresh(); status.Text = "Adjustment layer added. Drop effects on it to change everything below."; }
    }

    /// <summary>Premiere's New Item: color matte, black video, transparent video, bars, gradient.</summary>
    void AddGenerated(string name, string spec)
    {
        if (PlaceNewItem(new MediaAsset { Name = name, Kind = MediaKind.Color, Color = spec, Path = "" }))
            status.Text = $"{name} added. Change its colors in the inspector.";
    }

    /// <summary>Adds a generated item (default still duration) to the bin and the top-most free video track at the playhead (a new track if needed).</summary>
    bool PlaceNewItem(MediaAsset media)
    {
        long at = ProgramPlayhead; Guid? created = null;
        Change("New " + media.Name, p =>
        {
            var s = p.Sequences.First(x => x.Id == p.ActiveSequenceId);
            media.DurationSeconds = Prefs.StillSeconds; media.Width = s.Width; media.Height = s.Height; media.Fps = s.Fps;
            p.Media.Add(media);
            long length = s.Fps.Frames(Prefs.StillSeconds);
            var track = s.Tracks.Where(t => t.Kind == TrackKind.Video && !t.Locked).LastOrDefault();
            if (track is null || track.Clips.Any(c => c.Start < at + length && c.End > at))
            { track = new EditTrack { Kind = TrackKind.Video, Name = $"Video {s.Tracks.Count(t => t.Kind == TrackKind.Video) + 1}" }; s.Tracks.Add(track); }
            var clip = new TimelineClip { Name = media.Name, MediaId = media.Id, Start = at, Duration = length, StreamIntent = ClipStreamIntent.VideoOnly };
            track.Clips.Add(clip); track.Clips.Sort((a, b) => a.Start.CompareTo(b.Start));
            created = clip.Id; selectedTrack = track.Id;
        });
        if (created is not { } id) return false;
        selectedClip = id; extraSelection.Clear(); Refresh(); return true;
    }

    /// <summary>Inspector rows for a generated item's colors.</summary>
    void GeneratorControls(StackPanel host, MediaAsset media)
    {
        if (media.Kind != MediaKind.Color || media.Color is "bars" or "transparent") return;
        bool gradient = media.Color?.StartsWith("gradient:", StringComparison.Ordinal) == true;
        var parts = gradient ? media.Color!.Split(':') : [media.Color ?? "#000000"];
        var id = media.Id;
        void Row(string label, int index)
        {
            string value = parts.Length > index ? parts[index] : "#FFFFFF";
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,30,96"), MinHeight = 30 };
            row.Children.Add(Copy(label, 12, TextBrush));
            var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), BorderBrush = LineBrush, BorderThickness = new Thickness(1), Background = Brush(value.Length == 7 ? value : "#000000"), HorizontalAlignment = HorizontalAlignment.Center };
            var box = new TextBox { Text = value, FontSize = 11.5, MinHeight = 26, Height = 26, Padding = new Thickness(6, 0) };
            box.LostFocus += (_, _) =>
            {
                var t = (box.Text ?? "").Trim().ToUpperInvariant(); if (!t.StartsWith('#')) t = "#" + t;
                if (t.Length != 7 || !t.Skip(1).All(Uri.IsHexDigit) || t == value) return;
                Change("Matte color", p => { var m = p.Media.First(x => x.Id == id); if (gradient) { var c = m.Color!.Split(':'); c[index] = t; m.Color = string.Join(':', c); } else m.Color = t; });
            };
            Grid.SetColumn(swatch, 1); Grid.SetColumn(box, 2); row.Children.Add(swatch); row.Children.Add(box); host.Children.Add(row);
        }
        if (gradient) { Row("Top color", 1); Row("Bottom color", 2); } else Row("Matte color", 0);
    }
}
