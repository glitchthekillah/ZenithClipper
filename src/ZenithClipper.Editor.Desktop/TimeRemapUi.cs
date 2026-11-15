using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Premiere's Time Remapping controls: speed keyframes with ramps, video only.</summary>
public sealed partial class EditorWindow
{
    void TimeRemapControls(StackPanel host, TimelineClip clip, EditTrack track, MediaAsset media)
    {
        if (track.Kind != TrackKind.Video || media.Kind != MediaKind.Video) return;
        var id = clip.Id;
        if (!TimeRemap.Has(clip))
        {
            var enable = Button("Time remapping…", () => EnableTimeRemap(id));
            ToolTip.SetTip(enable, "Vary speed across the clip with keyframes and ramps (video only; linked audio is unlinked and keeps playing normally).");
            enable.Margin = new Thickness(0, 4, 0, 0); host.Children.Add(enable); return;
        }
        long rel = Math.Clamp(ProgramPlayhead - clip.Start, 0, clip.Duration - 1);
        bool onKey = clip.Motion.Any(k => k.Parameter == "speed" && k.Frame == rel);
        double now = TimeRemap.SpeedAt(clip, rel);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,84,24,26"), MinHeight = 30, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(Copy("Remap speed", 12, TextBrush));
        var box = new TextBox { Text = (now * 100).ToString("0.#", CultureInfo.InvariantCulture), FontSize = 12, Height = 28, MinHeight = 28, Padding = new Thickness(8, 0), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Foreground = AccentSoftBrush };
        Grid.SetColumn(box, 1); row.Children.Add(box);
        var unit = Copy("%", 11, FaintBrush); unit.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(unit, 2); row.Children.Add(unit);
        var diamond = ActionButton(onKey ? "diamondfill" : "diamond", onKey ? "Remove speed keyframe" : "Add speed keyframe at playhead", () =>
        {
            if (onKey) RemapEdit(id, c => { if (c.Motion.Count(k => k.Parameter == "speed") > 1) c.Motion.RemoveAll(k => k.Parameter == "speed" && k.Frame == rel); });
            else RemapEdit(id, c => SetKeyframe(c, "speed", rel, now));
        }, iconOnly: true);
        diamond.Foreground = onKey ? AccentBrush : SoftTextBrush; Grid.SetColumn(diamond, 3); row.Children.Add(diamond);
        string original = box.Text!;
        box.LostFocus += (_, _) =>
        {
            if (box.Text == original || !double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) return;
            double v = Math.Clamp(pct / 100, TimeRemap.Min, TimeRemap.Max);
            RemapEdit(id, c => SetKeyframe(c, "speed", rel, v));
        };
        ToolTip.SetTip(box, "Speed at the playhead (10% to 2000%). Sets a keyframe; speeds ramp linearly between keyframes.");
        host.Children.Add(row);
        var hint = Copy($"{clip.Motion.Count(k => k.Parameter == "speed")} speed keyframes · uses {Clock(TimeRemap.Has(clip) ? (long)Math.Round(TimeRemap.Consumed(clip, clip.Duration)) : clip.Duration)} of source", 11, FaintBrush);
        hint.Margin = new Thickness(0, 0, 0, 4); host.Children.Add(hint);
        var off = Button("Remove time remapping", () => DisableTimeRemap(id)); host.Children.Add(off);
    }

    void RemapEdit(Guid id, Action<TimelineClip> edit) =>
        Change("Time remapping", p => edit(p.Sequences.First(s => s.Id == p.ActiveSequenceId).Tracks.SelectMany(t => t.Clips).First(c => c.Id == id)));

    /// <summary>Unlinks the clip from its audio, converts its constant speed into the first keyframe, and starts remapping.</summary>
    void EnableTimeRemap(Guid id)
    {
        Change("Enable time remapping", p =>
        {
            var s = p.Sequences.First(x => x.Id == p.ActiveSequenceId);
            var clip = s.Tracks.SelectMany(t => t.Clips).First(c => c.Id == id);
            if (clip.Reverse || clip.HoldFrame is not null) throw new InvalidOperationException("Turn off reverse and frame hold before remapping time.");
            if (clip.LinkId is { } link) foreach (var c in s.Tracks.SelectMany(t => t.Clips).Where(c => c.LinkId == link)) c.LinkId = null;
            double speed = Math.Clamp(clip.Speed, TimeRemap.Min, TimeRemap.Max); clip.Speed = 1;
            clip.Motion.Add(new Keyframe { Parameter = "speed", Frame = 0, Value = speed, Interpolation = "linear" });
            clip.StreamIntent = ClipStreamIntent.VideoOnly;
        });
        inspectorTab = "Video"; BuildInspector();
        status.Text = "Time remapping on. Move the playhead, set Remap speed, and speeds ramp between keyframes.";
    }

    /// <summary>Back to one constant speed that covers the same source range.</summary>
    void DisableTimeRemap(Guid id) => RemapEdit(id, c =>
    {
        double used = TimeRemap.Consumed(c, c.Duration);
        c.Motion.RemoveAll(k => k.Parameter == "speed"); c.Speed = Math.Max(.01, used / c.Duration);
    });
}
