using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Multi-clip selection (Shift/Ctrl+click) and Premiere groups (Ctrl+G / Ctrl+Shift+G).</summary>
public sealed partial class EditorWindow
{
    /// <summary>Clips selected besides <see cref="selectedClip"/> (the primary one the inspector shows).</summary>
    internal readonly HashSet<Guid> extraSelection = [];

    internal IEnumerable<Guid> SelectedClips => selectedClip is { } c ? extraSelection.Where(x => x != c).Append(c) : extraSelection;

    /// <summary>Everything that acts with the selection: the clips plus their links and groups.</summary>
    internal HashSet<Guid> SelectionClosure()
    {
        var project = Project; return EditCommands.Closure(project, project.ActiveSequenceId, SelectedClips);
    }

    /// <summary>Shift/Ctrl+click: add or remove a clip; the clicked clip becomes the primary selection.</summary>
    internal void ToggleSelection(Guid clip)
    {
        var closure = SelectionClosure();
        if (closure.Contains(clip) && (selectedClip is not null))
        {
            var drop = EditCommands.Closure(Project, Project.ActiveSequenceId, [clip]);
            extraSelection.RemoveWhere(drop.Contains);
            if (selectedClip is { } c && drop.Contains(c)) { selectedClip = extraSelection.FirstOrDefault() is var next && next != Guid.Empty ? next : null; if (selectedClip is { } n) extraSelection.Remove(n); }
        }
        else { if (selectedClip is { } c) extraSelection.Add(c); selectedClip = clip; }
    }

    internal void ClearExtraSelection() => extraSelection.Clear();

    void GroupSelected()
    {
        var ids = SelectedClips.ToList();
        Change("Group", p => EditCommands.Group(p, p.ActiveSequenceId, ids));
        status.Text = "Grouped. Grouped clips select, move and delete together (Ctrl+Shift+G to ungroup).";
    }

    void UngroupSelected()
    {
        var ids = SelectedClips.ToList(); if (ids.Count == 0) return;
        Change("Ungroup", p => EditCommands.Ungroup(p, p.ActiveSequenceId, ids));
    }
}
