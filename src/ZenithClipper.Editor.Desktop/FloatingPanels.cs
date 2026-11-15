using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Panel docking (Premiere's panel groups): Mixer, Scopes and History can sit in the right panel group, the left
/// (Project) group, or float in their own window. Drag a tab onto the other group's tabs to dock it there, or off both to
/// float it; right-click a tab for the same choices. Closing a floating window docks the panel back where it came from.</summary>
public sealed partial class EditorWindow
{
    static readonly string[] DockablePanels = ["Mixer", "Scopes", "History"];
    readonly Dictionary<string, Window> floatingPanels = [];
    readonly HashSet<string> dockedLeft = [];
    Grid? rightDockHost; readonly Grid leftDockHost = new() { IsVisible = false };
    internal bool PanelShown(string name) => floatingPanels.ContainsKey(name) || (dockedLeft.Contains(name) ? libraryFilter == name : rightPanel == name);
    Avalonia.Controls.Control? PanelControl(string name) => name switch { "Mixer" => mixerPanel, "Scopes" => scopes, "History" => historyPanel, _ => null };

    void Reparent(Avalonia.Controls.Control control, Panel? to)
    {
        if (control.Parent is Panel from) from.Children.Remove(control);
        else if (control.Parent is Window w) w.Content = null;
        if (to is null) return;
        Grid.SetRow(control, to == rightDockHost ? 1 : 0); to.Children.Add(control);
    }

    /// <summary>Moves a panel to "left", "right" or "float".</summary>
    internal void MovePanel(string name, string where)
    {
        if (PanelControl(name) is not { } control) return;
        if (floatingPanels.Remove(name, out var open)) { open.Content = null; open.Close(); }
        if (where == "float")
        {
            Reparent(control, null);
            var window = new Window { Title = name + " · ZenithClipper", Width = 380, Height = 520, Background = PanelBrush, Content = control, ShowInTaskbar = false };
            floatingPanels[name] = window;
            window.Closed += (_, _) => { if (floatingPanels.TryGetValue(name, out var w) && w == window) { floatingPanels.Remove(name); window.Content = null; Reparent(control, dockedLeft.Contains(name) ? leftDockHost : rightDockHost); RefreshPanelGroups(); } };
            RefreshPanelGroups();
            if (!HeadlessVerification) window.Show(this);
            status.Text = $"{name} is floating. Close its window to dock it back.";
            return;
        }
        if (where == "left") { dockedLeft.Add(name); Reparent(control, leftDockHost); libraryFilter = name; }
        else { dockedLeft.Remove(name); Reparent(control, rightDockHost); rightPanel = name; if (libraryFilter == name) libraryFilter = "Media"; }
        RefreshPanelGroups();
        status.Text = $"{name} docked in the {(where == "left" ? "left" : "right")} panel group.";
    }
    internal void FloatPanel(string name) { if (floatingPanels.TryGetValue(name, out var w)) { w.Activate(); return; } MovePanel(name, "float"); }
    void DockAllPanels() { foreach (var name in DockablePanels) if (floatingPanels.ContainsKey(name) || dockedLeft.Contains(name)) MovePanel(name, "right"); }
    void RefreshPanelGroups() { BuildLibraryTabs(); rebuildRightTabs?.Invoke(); UpdateLibrary(); }

    /// <summary>Tab behaviour shared by both groups: click selects; drag onto a group's tabs docks there, elsewhere floats.</summary>
    void MakeDockableTab(Button tab, string name)
    {
        var menu = new ContextMenu();
        MenuItem It(string header, string where) { var m = new MenuItem { Header = header }; m.Click += (_, _) => MovePanel(name, where); return m; }
        menu.Items.Add(It("Float this panel", "float"));
        menu.Items.Add(dockedLeft.Contains(name) ? It("Dock in the right group", "right") : It("Dock in the left group", "left"));
        tab.ContextMenu = menu; ToolTip.SetTip(tab, "Drag to the other panel group or out to float. Right-click for options.");
        Point? press = null; bool moved = false;
        tab.AddHandler(PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed) { press = e.GetPosition(this); moved = false; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        tab.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (press is not { } start) return; var now = e.GetPosition(this);
            if (!moved && Math.Abs(now.X - start.X) + Math.Abs(now.Y - start.Y) > 10) { moved = true; e.Pointer.Capture(tab); Cursor = new Cursor(StandardCursorType.DragMove); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        tab.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (press is null) return; press = null; if (!moved) return;
            moved = false; e.Pointer.Capture(null); Cursor = null; e.Handled = true;
            bool Over(Avalonia.Controls.Control c) { var p = e.GetPosition(c); return p.X >= -8 && p.Y >= -12 && p.X <= c.Bounds.Width + 8 && p.Y <= c.Bounds.Height + 12; }
            string where = Over(libraryTabs) ? "left" : rightTabStrip is not null && Over(rightTabStrip) ? "right" : "float";
            Dispatcher.UIThread.Post(() => MovePanel(name, where));
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    internal void VerifyFloatingPanels()
    {
        var keep = rightPanel; var keepLeft = libraryFilter;
        FloatPanel("Scopes");
        if (!floatingPanels.ContainsKey("Scopes") || scopes.Parent is Panel) throw new InvalidOperationException("Scopes did not float.");
        floatingPanels["Scopes"].Close();
        if (floatingPanels.ContainsKey("Scopes") || scopes.Parent != rightDockHost) throw new InvalidOperationException("Scopes did not dock back.");
        MovePanel("Mixer", "left");
        if (mixerPanel.Parent != leftDockHost || !PanelShown("Mixer") || !leftDockHost.IsVisible || library.IsVisible) throw new InvalidOperationException("Mixer did not dock in the left group.");
        if (!libraryTabs.Children.OfType<Button>().Any(b => b.Content is string s && s == "Mixer")) throw new InvalidOperationException("Left group has no Mixer tab.");
        MovePanel("Mixer", "right");
        if (mixerPanel.Parent != rightDockHost || dockedLeft.Count != 0) throw new InvalidOperationException("Mixer did not dock back right.");
        rightPanel = keep; libraryFilter = keepLeft is "Mixer" ? "Media" : keepLeft; RefreshPanelGroups();
    }
}
