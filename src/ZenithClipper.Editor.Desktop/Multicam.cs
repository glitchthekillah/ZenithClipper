using Avalonia.Input;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Multi-camera editing: build a multicam clip from selected clips, then press 1-9 to cut to a camera at the playhead.</summary>
public sealed partial class EditorWindow
{
    void CreateMulticam()
    {
        var ids = EditCommands.Closure(Project, Project.ActiveSequenceId, SelectedClips).ToList();
        Guid? made = null; string name = $"Multicam {Project.Sequences.Count}";
        Change("Create multicam", p => made = EditCommands.CreateMulticam(p, p.ActiveSequenceId, ids, name));
        if (made is { } id) { selectedClip = id; extraSelection.Clear(); Refresh(); status.Text = "Multicam ready. Play, and press 1-9 to cut to that camera at the playhead."; }
    }

    bool HandleMulticamKey(KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return false;
        int angle = e.Key switch { >= Key.D1 and <= Key.D9 => e.Key - Key.D0, >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad0, _ => 0 };
        if (angle == 0) return false;
        long at = ProgramPlayhead;
        // The multicam clip under the playhead (selected one first, else any on an unlocked video track).
        var hit = Sequence.Tracks.Where(t => !t.Locked).SelectMany(t => t.Clips.Select(c => (Track: t, Clip: c)))
            .Where(x => x.Clip.MulticamAngle is not null && x.Clip.Start <= at && x.Clip.End > at)
            .OrderByDescending(x => x.Clip.Id == selectedClip).FirstOrDefault();
        if (hit.Clip is null) return false;
        Change($"Switch to camera {angle}", p => EditCommands.SwitchAngle(p, p.ActiveSequenceId, hit.Track.Id, hit.Clip.Id, at, angle));
        status.Text = $"Camera {angle} from {Clock(at)}.";
        return true;
    }
}
