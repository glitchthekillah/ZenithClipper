using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;
public sealed partial class EditorWindow
{
    internal void VerifyDesignBehavior()
    {
        if (bitmap is not null)
        {
            ToggleComparison(); if (!comparison.IsVisible || comparison.Reference is null || comparison.Reference.PixelSize != bitmap.PixelSize) throw new InvalidOperationException("Comparison view did not capture the reference frame.");
            ToggleComparison(); if (comparison.IsVisible) throw new InvalidOperationException("Comparison view did not turn off.");
            Console.WriteLine("Comparison view smoke passed.");
            var keepValues = transformOverlay.current;
            transformOverlay.Load(new(.1, -.05, .5, .4, 30), new ClipTransform { AnchorX = .3, AnchorY = .6 }, 1920, 1080);
            var probe = transformOverlay.ClipToScreen(.2, .7); var back = transformOverlay.ScreenToClip(probe);
            if (Math.Abs(back.U - .2) > 1e-9 || Math.Abs(back.V - .7) > 1e-9) throw new InvalidOperationException("Pen mask monitor mapping does not round-trip.");
            transformOverlay.Load(keepValues, new ClipTransform(), Sequence.Width, Sequence.Height); transformOverlay.Hide(); UpdateTransformOverlay();
            Console.WriteLine("Pen mask mapping smoke passed.");
        }
        var beforeSession=session;var beforeTrack=selectedTrack;var beforeClip=selectedClip;var frameUnits=timeFieldsFrames;
        try
        {
            var project=ProjectCopy;var sequence=project.Sequences.First(s=>s.Id==project.ActiveSequenceId);
            for(var index=0;index<10;index++)sequence.Tracks.Add(new EditTrack{Name="Additional track "+index});
            session=new(project);BuildTrackHeaders();timeline.Rebuild();UpdateLayout();Dispatcher.UIThread.RunJobs();
            timelineScroller.Offset=new Vector(0,timelineScroller.Extent.Height);UpdateLayout();Dispatcher.UIThread.RunJobs();
            VerifyTimelineAlignment();
            if(timelineScroller.Offset.Y<=0)throw new InvalidOperationException("Many-track scroll fixture did not reach the lower tracks.");
        }
        finally{session=beforeSession;selectedTrack=beforeTrack;selectedClip=beforeClip;BuildTrackHeaders();timeline.Rebuild();timelineScroller.Offset=default;headerScroll.Offset=default;UpdateLayout();Dispatcher.UIThread.RunJobs();}
        if(selectedClip is not null)
        {
            var json=JsonSerializer.Serialize(Project);
            timeFieldsFrames=!timeFieldsFrames;inspectorSections["Transform"]=true;BuildInspector();BuildInspector();
            var controls=inspector.GetVisualDescendants().ToList();
            if(!controls.OfType<TextBox>().Any(box=>box.Name=="property-Scale-width"))throw new InvalidOperationException("Grouped inspector lost transform inputs.");
            if(!controls.OfType<StackPanel>().Any(panel=>Equals(panel.Tag,"Transform")&&panel.IsVisible))throw new InvalidOperationException("Inspector rebuild collapsed the opened transform section.");
            timeFieldsFrames=frameUnits;BuildInspector();
            if(JsonSerializer.Serialize(Project)!=json)throw new InvalidOperationException("Changing displayed timing units mutated the edit graph.");
            var savedSession=session;try{session=new EditorSession(Project);timeFieldsFrames=false;BuildInspector();var oldInput=inspector.GetVisualDescendants().OfType<TextBox>().First(box=>box.Name=="property-Duration");oldInput.Text="2.5";timeFieldsFrames=true;oldInput.RaiseEvent(new Avalonia.Input.FocusChangedEventArgs(Avalonia.Input.InputElement.LostFocusEvent));if(Sequence.Tracks.SelectMany(track=>track.Clips).First(clip=>clip.Id==selectedClip).Duration!=timelineFps.Frames(2.5))throw new InvalidOperationException("Pending seconds were committed using a changed frame-display mode.");}finally{session=savedSession;timeFieldsFrames=frameUnits;UpdateChrome();BuildTrackHeaders();timeline.Rebuild();}
            inspectorSections["Transform"]=false;BuildInspector();
        }
        var shownTab=libraryFilter;libraryFilter="Media";
        if(Project.Media.Count>0){mediaSearch.Text="__no_such_media__";Dispatcher.UIThread.RunJobs();if(!libraryNoMatches.IsVisible||library.IsVisible)throw new InvalidOperationException("Search without matches did not explain the empty result.");mediaSearch.Text="";Dispatcher.UIThread.RunJobs();}
        libraryFilter=shownTab;BuildLibraryTabs();UpdateLibrary();
        if(Sequence.Duration==0){if(exportPrimary.IsEnabled||playTransport.IsEnabled||frameNavigation.Any(button=>button.IsEnabled))throw new InvalidOperationException("Empty sequence actions were not disabled.");var previousStatus=status.Text;if(!Export().IsCompletedSuccessfully)throw new InvalidOperationException("Empty export did not return before opening a picker.");status.Text=previousStatus;}
        var previousFps=timelineFps;timelineFps=new FrameRate(30000,1001);var clock=Clock(1798);timelineFps=previousFps;
        if(clock!="00:00:59.993")throw new InvalidOperationException("Fractional frame-rate elapsed clock is incorrect: "+clock);
        playing=true;UpdateTransportState();playing=false;UpdateTransportState();if(lastTransportPlaying)throw new InvalidOperationException("Transport did not reset to its paused state.");
        UpdateLayout();Dispatcher.UIThread.RunJobs();
        var nativeType=typeof(ZenithClipper.Mlt.MltSession).Assembly.GetType("ZenithClipper.Mlt.Native")!;var ready=(Lazy<bool>)nativeType.GetField("ready",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;if(ready.IsValueCreated||nativeType.GetField("Root",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null) is not null)throw new InvalidOperationException("Headless UI verification initialized native media.");
        Console.WriteLine("Native isolation passed: MLT/SDL initialization was not requested.");
        VerifyAlignment();
        Console.WriteLine("Design checks passed: bottom-track alignment, persistent inspector sections, timing-unit roundtrip, elapsed clock and transport state.");
    }

    /// <summary>
    /// Measures the laid-out window: every icon sits exactly in the middle of its button, and every vertically centred
    /// control in a horizontal row (toolbars, transport, title bar, track headers, inspector rows) shares the row's centre line.
    /// </summary>
    internal void VerifyAlignment()
    {
        UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var problems = new List<string>(); int icons = 0, rowItems = 0;
        Rect? BoundsOf(Visual v) => v.TranslatePoint(default, this) is { } o ? new Rect(o, v.Bounds.Size) : null;
        string Describe(Visual v) => v is Button b ? $"button '{Avalonia.Automation.AutomationProperties.GetName(b)}'" : v is TextBlock t ? $"text '{t.Text}'" : v.GetType().Name;
        foreach (var visual in this.GetVisualDescendants().OfType<Visual>().Where(v => v.IsEffectivelyVisible && v.Bounds.Width > 0))
        {
            if (visual is Button button && (button.Content as Viewbox ?? (button.Content is Panel { Children.Count: 1 } single ? single.Children[0] as Viewbox : null)) is { } icon && BoundsOf(button) is { } outer && BoundsOf(icon) is { } inner)
            {
                icons++;
                if (Math.Abs(outer.Center.X - inner.Center.X) > .5 || Math.Abs(outer.Center.Y - inner.Center.Y) > .5)
                    problems.Add($"{Describe(button)}: icon off centre by {inner.Center.X - outer.Center.X:0.##}, {inner.Center.Y - outer.Center.Y:0.##}");
            }
            if (visual is StackPanel { Orientation: Orientation.Horizontal } row && BoundsOf(row) is { } rowBox)
                foreach (var child in row.Children.Where(c => c.IsVisible && c.VerticalAlignment != VerticalAlignment.Top && c.VerticalAlignment != VerticalAlignment.Bottom))
                    if (BoundsOf(child) is { } box && ++rowItems > 0 && Math.Abs(box.Center.Y - rowBox.Center.Y) > .5)
                        problems.Add($"{Describe(child)} in a row: centre {box.Center.Y - rowBox.Center.Y:0.##}px off the row's centre line");
            if (visual is Grid { RowDefinitions.Count: <= 1 } grid && grid.ColumnDefinitions.Count > 1 && BoundsOf(grid) is { } gridBox && gridBox.Height <= 64)
                foreach (var child in grid.Children.Where(c => c.IsVisible && c.VerticalAlignment != VerticalAlignment.Top && c.VerticalAlignment != VerticalAlignment.Bottom))
                    if (BoundsOf(child) is { } box && ++rowItems > 0 && Math.Abs(box.Center.Y - gridBox.Center.Y) > .5)
                        problems.Add($"{Describe(child)} in a grid row{(grid.TemplatedParent is { } owner ? " of " + owner.GetType().Name : "")}: centre {box.Center.Y - gridBox.Center.Y:0.##}px off");
        }
        if (problems.Count > 0) throw new InvalidOperationException("Alignment check failed:\n  " + string.Join("\n  ", problems.Distinct().Take(40)));
        Console.WriteLine($"Alignment check passed ({icons} icon buttons, {rowItems} row items measured): every icon is centred in its button and every toolbar, transport and header row shares one centre line.");
    }
}
