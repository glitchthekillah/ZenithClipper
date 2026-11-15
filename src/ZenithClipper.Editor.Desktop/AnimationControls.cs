using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

public sealed partial class EditorWindow
{
    void EffectKeyframeControls(TimelineClip clip, string id, string parameter, TextBox input, double initial, double min, double max)
    {
        var interpolation = new ComboBox { ItemsSource = ClipBlend.Interpolations, SelectedIndex = 0, MinWidth = 100 };
        ToolTip.SetTip(interpolation, "How this keyframe moves toward the next value: linear, hold (discrete), smooth, or eased.");
        (effectTarget ?? inspector).Children.Add(interpolation);
        var add = Button("Add keyframe here", () =>
        {
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < min || value > max)
                throw new ArgumentException($"Enter a value between {min} and {max} before adding a keyframe.");
            if (ProgramPlayhead < clip.Start || ProgramPlayhead >= clip.End)
                throw new InvalidOperationException("Move the ProgramPlayhead inside the selected clip first.");
            var localFrame = ProgramPlayhead - clip.Start;
            Change("Add effect keyframe", p =>
            {
                var current = EditableClip(p, clip.Id);
                var effect = EnsureEffect(current, id);
                effect.Parameters.TryAdd(parameter, initial.ToString(CultureInfo.InvariantCulture));
                Set(parameter);
                if (id == "box_blur")
                    Set("vradius");
                void Set(string name)
                {
                    effect.Keyframes.RemoveAll(k => k.Parameter == name && k.Frame == localFrame);
                    effect.Keyframes.Add(new Keyframe { Parameter = name, Frame = localFrame, Value = value, Interpolation = interpolation.SelectedItem as string ?? "linear" });
                }
            });
        });
        add.Focusable = false;
        (effectTarget ?? inspector).Children.Add(add);
        var keys = clip.Effects.FirstOrDefault(e => e.Id == id)?.Keyframes.Where(k => k.Parameter == parameter).OrderBy(k => k.Frame).ToList();
        if (keys is not { Count: > 0 })
            return;
        (effectTarget ?? inspector).Children.Add(new TextBlock { Text = string.Join(" · ", keys.Select(k => $"f{k.Frame}: {k.Value:0.###} ({k.Interpolation})")), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11 });
        var remove = Button("Remove keyframe here", () => Change("Remove effect keyframe", p =>
        {
            var current = EditableClip(p, clip.Id);
            var effect = current.Effects.FirstOrDefault(e => e.Id == id);
            effect?.Keyframes.RemoveAll(k => k.Frame == ProgramPlayhead - current.Start && (k.Parameter == parameter || id == "box_blur" && k.Parameter == "vradius"));
        }));
        remove.Focusable = false;
        (effectTarget ?? inspector).Children.Add(remove);
    }

    void AddFade(bool fadeIn)
    {
        if (selectedClip is not { } id)
            return;
        var halfSecond = Sequence.Fps.Frames(.5);
        Change(fadeIn ? "Fade video in" : "Fade video out", p =>
        {
            var clip = EditableClip(p, id);
            var sequence = p.Sequences.First(s => s.Id == p.ActiveSequenceId);
            if (sequence.Tracks.First(t => t.Clips.Any(c => c.Id == id)).Kind != TrackKind.Video)
                throw new InvalidOperationException("Select a video or title clip for a video fade.");
            if (clip.Duration < 2)
                throw new InvalidOperationException("A fade needs a clip of at least two frames.");
            var effect = EnsureEffect(clip, "brightness");
            var length = Math.Min(Math.Max(1, halfSecond), Math.Max(1, (clip.Duration - 1) / 2));
            var start = fadeIn ? 0 : clip.Duration - 1 - length;
            var end = fadeIn ? length : clip.Duration - 1;
            effect.Keyframes.RemoveAll(k => k.Parameter == "alpha" && k.Frame >= start && k.Frame <= end);
            effect.Keyframes.Add(new()
            {
                Parameter = "alpha",
                Frame = start,
                Value = fadeIn ? 0 : 1
            });
            effect.Keyframes.Add(new()
            {
                Parameter = "alpha",
                Frame = end,
                Value = fadeIn ? 1 : 0
            });
        });
    }

    static TimelineClip EditableClip(EditProject project, Guid id)
    {
        var sequence = project.Sequences.First(s => s.Id == project.ActiveSequenceId);
        var track = sequence.Tracks.First(t => t.Clips.Any(c => c.Id == id));
        if (track.Locked)
            throw new InvalidOperationException("Unlock this track before editing.");
        return track.Clips.First(c => c.Id == id);
    }

    static ClipEffect EnsureEffect(TimelineClip clip, string id)
    {
        var effect = clip.Effects.FirstOrDefault(e => e.Id == id);
        if (effect is null)
        {
            effect = new ClipEffect { Id = id };
            clip.Effects.Add(effect);
        }
        if (id == "brightness")
        {
            effect.Parameters.TryAdd("level", "1");
            effect.Parameters.TryAdd("alpha", "1");
        }
        return effect;
    }
}
