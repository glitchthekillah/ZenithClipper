using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

public sealed partial class EditorWindow
{
    bool alignToFrame;
    /// <summary>Graphics → Align and distribute, on the selected video clips (one clip aligns to the frame).</summary>
    internal void AlignSelection(string mode)
    {
        var ids = SelectionClosure(); int moved = 0;
        if (ids.Count == 0) { status.Text = "Select the clips to align first."; return; }
        bool frame = alignToFrame;
        Change(mode.StartsWith("distribute", StringComparison.Ordinal) ? "Distribute" : "Align", p => moved = ClipAlign.Apply(p, p.ActiveSequenceId, ids, mode, frame));
        status.Text = moved == 0 ? (mode.StartsWith("distribute", StringComparison.Ordinal) ? "Distribute needs three clips, or align to video frame turned on." : "Already aligned.") : $"Moved {moved} clip{(moved == 1 ? "" : "s")}.";
        BuildInspector(); UpdateTransformOverlay();
    }
}
