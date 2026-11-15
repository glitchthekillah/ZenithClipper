using Avalonia;
using Avalonia.Controls;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Essential Sound: one click tags a clip as Dialogue, Music, SFX or Ambience and applies a tuned chain.</summary>
public sealed partial class EditorWindow
{
    static readonly Dictionary<string, (string Id, (string, string)[] Params)[]> SoundTypes = new()
    {
        ["Dialogue"] = [("highpass", [("frequency", "80")]), ("denoise_audio", [("amount", "8")]), ("deesser", [("amount", "40")]), ("compressor", [("threshold", "-20"), ("ratio", "3"), ("attack", "10"), ("release", "150"), ("makeup", "3")]), ("loudness", [("target", "-16 LUFS (podcast)")]), ("limiter", [("limit", "-1")])],
        ["Music"] = [("loudness", [("target", "-23 LUFS (broadcast)")]), ("limiter", [("limit", "-1")])],
        ["SFX"] = [("compressor", [("threshold", "-14"), ("ratio", "2"), ("attack", "5"), ("release", "100"), ("makeup", "0")]), ("limiter", [("limit", "-1")])],
        ["Ambience"] = [("lowpass", [("frequency", "12000")]), ("eq3", [("low", "-3"), ("mid", "-2"), ("high", "0")])],
    };

    internal static IEnumerable<string> SoundTypeNames => SoundTypes.Keys;
    internal static (string Id, (string, string)[] Params)[] SoundChain(string type) => SoundTypes[type];

    void EssentialSoundButtons(StackPanel host, TimelineClip clip)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,*,4,*,4,*"), Margin = new Thickness(0, 4, 0, 2) };
        int col = 0; var id = clip.Id;
        foreach (var type in SoundTypes.Keys)
        {
            var name = type;
            var b = Button(type, () => ApplySoundType(id, name)); b.FontSize = 11.5; b.Padding = new Thickness(4, 0); b.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            ToolTip.SetTip(b, $"Essential Sound · {type}: replaces this clip's audio effects with a tuned chain ({string.Join(", ", SoundTypes[type].Select(x => ZenithClipper.Mlt.EffectCatalog.Find(x.Id)?.Name))}).");
            Grid.SetColumn(b, col); row.Children.Add(b); col += 2;
        }
        host.Children.Add(Copy("Essential Sound", 11, FaintBrush)); host.Children.Add(row);
    }

    void ApplySoundType(Guid clipId, string type)
    {
        var chain = SoundTypes[type];
        Change("Essential Sound: " + type, p => EditCommands.ChangeLinkedClip(p, p.ActiveSequenceId, clipId, c =>
        {
            var track = p.Sequences.First(s => s.Id == p.ActiveSequenceId).Tracks.First(t => t.Clips.Contains(c));
            if (track.Kind != TrackKind.Audio) return;
            c.Effects.RemoveAll(e => ZenithClipper.Mlt.EffectCatalog.Find(e.Id)?.Kind == ZenithClipper.Mlt.EffectKind.Audio);
            foreach (var (fx, parameters) in chain) c.Effects.Add(new ClipEffect { Id = fx, Parameters = parameters.ToDictionary(x => x.Item1, x => x.Item2) });
        }));
        status.Text = $"Tagged as {type}. Fine-tune the effects below.";
    }
}
