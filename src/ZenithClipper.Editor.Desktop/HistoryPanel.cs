using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Premiere's History panel: every step, click one to step back (or forward) to it.</summary>
public sealed partial class EditorWindow
{
    readonly ScrollViewer historyPanel = new() { IsVisible = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

    void UpdateHistory()
    {
        historyPanel.IsVisible = PanelShown("History");
        if (!historyPanel.IsVisible) return;
        var done = session.UndoLabels; var undone = session.RedoLabels;
        var list = new StackPanel { Spacing = 1, Margin = new Thickness(8) };
        void Row(string label, bool current, bool future, Action go)
        {
            var b = Button(label, go); b.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left; b.Height = 28; b.FontSize = 12;
            b.Classes.Add("subtle"); b.Background = current ? SelectedBrush : Brushes.Transparent; b.Tag = current ? SelectedBrush : null;
            b.Foreground = future ? FaintBrush : current ? TextBrush : SoftTextBrush; b.FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal;
            list.Children.Add(b);
        }
        Row("Open", done.Count == 0, false, () => StepHistory(-done.Count));
        for (int i = 0; i < done.Count; i++) { int back = done.Count - 1 - i; Row(done[i], back == 0, false, () => StepHistory(-back)); }
        for (int j = 0; j < undone.Count; j++) { int forward = j + 1; Row(undone[j], false, true, () => StepHistory(forward)); }
        list.Children.Add(Copy($"{done.Count} step{(done.Count == 1 ? "" : "s")} · click a step to go back to it; greyed steps can be redone.", 11, FaintBrush));
        historyPanel.Content = list;
    }

    void StepHistory(int steps)
    {
        if (steps == 0) return;
        if (exportFinished is not null) { status.Text = "Wait for the export to finish."; return; }
        for (int i = 0; i < Math.Abs(steps); i++) { if (steps < 0) session.Undo(); else session.Redo(); }
        Refresh(true);
    }
}

public sealed partial class EditorWindow { bool rippleSpeed = true; }
