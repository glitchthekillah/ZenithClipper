using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

public sealed partial class EditorWindow
{
    sealed partial class TimelineView
    {
        enum Gesture { None, EdgeTrim, Slip, Slide, TrackForward, Hand }
        Gesture gesture; long gestureDelta;
        // Edge trim
        Guid trimClip, trimTrack; bool trimHead; long trimStart, trimDuration; TimelineClip? trimNeighbour; Border? trimCard, neighbourCard;
        // Slide / slip / track forward
        List<(Border Card, long Start)> forward = []; long forwardFrom;
        Vector handOffset; Point handOrigin;
        // Effects dragged from the Effects panel
        Border? effectHighlight; string? effectId; bool effectIsTransition;

        const double EdgeGrab = 7;
        bool TrimTool => owner.tool is EditTool.Selection or EditTool.Ripple or EditTool.Rolling or EditTool.RateStretch;

        internal List<long> SnapPoints(HashSet<Guid> ignore)
        {
            var points = owner.Sequence.Tracks.SelectMany(t => t.Clips).Where(c => !ignore.Contains(c.Id)).SelectMany(c => new[] { c.Start, c.End }).ToList();
            points.Add(owner.ProgramPlayhead); points.Add(0);
            points.AddRange(owner.Sequence.Markers.Select(m => m.Frame));
            if (owner.markIn is { } i) points.Add(i); if (owner.markOut is { } o) points.Add(o);
            return points;
        }
        long Snap(long frame, HashSet<Guid> ignore)
        {
            snapLine.IsVisible = false;
            if (owner.snap.IsChecked != true) return frame;
            double ppf = PixelsPerFrame; long best = frame; double bestDistance = 10;
            foreach (long p in SnapPoints(ignore)) { double d = Math.Abs(p - frame) * ppf; if (d < bestDistance) { bestDistance = d; best = p; } }
            if (best != frame || bestDistance < 10) { snapLine.IsVisible = bestDistance < 10; snapLine.Height = Height; SetLeft(snapLine, best * ppf); SetTop(snapLine, 0); }
            return best;
        }
        Border? CardFor(Guid id) => Children.OfType<Border>().FirstOrDefault(b => b.Tag is Guid g && g == id);
        void Badge(string text, double x, double y) { dragBadge.Child = Copy(text, 10.5, TextBrush, FontWeight.Medium); dragBadge.IsVisible = true; SetLeft(dragBadge, x); SetTop(dragBadge, y); }
        string Delta(long frames) { double s = owner.timelineFps.Seconds(Math.Abs(frames)); return (frames < 0 ? "−" : "+") + (s >= 60 ? $"{(int)s / 60}:{s % 60:00.00}" : $"{s:0.00}s"); }

        /// <summary>Which clip edge (if any) is under the pointer, Premiere-style: the outer 7 px of a clip are its trim handles.</summary>
        (EditTrack Track, TimelineClip Clip, bool Head)? EdgeAt(Point p)
        {
            int row = owner.RowAt(p.Y); if (row < 0) return null;
            var track = owner.DisplayTracks()[row]; double ppf = PixelsPerFrame;
            foreach (var c in track.Clips)
            {
                double left = c.Start * ppf, right = c.End * ppf; if (right - left < EdgeGrab * 3) continue;
                if (Math.Abs(p.X - left) <= EdgeGrab) return (track, c, true);
                if (Math.Abs(p.X - right) <= EdgeGrab) return (track, c, false);
            }
            return null;
        }

        bool ToolPressed(PointerPressedEventArgs e, Point p)
        {
            gesture = Gesture.None; gestureDelta = 0; var tool = owner.tool; double ppf = PixelsPerFrame;
            if (tool == EditTool.Hand) { gesture = Gesture.Hand; handOrigin = e.GetPosition(owner.timelineScroller); handOffset = owner.timelineScroller.Offset; e.Pointer.Capture(this); e.Handled = true; return true; }
            if (tool == EditTool.Zoom) { ZoomAt(p.X, e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 1 / 1.6 : 1.6); e.Handled = true; return true; }
            if (p.Y < RulerHeight) return false;
            var hit = HitTest(p);
            if (tool == EditTool.Razor && hit is { Clip: { } cut } razor)
            {
                long at = Snap((long)Math.Round(p.X / ppf), []); bool all = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (at > cut.Start && at < cut.End)
                {
                    var t = razor.Track.Id; var id = cut.Id;
                    if (all) owner.Change("Razor all tracks", pr => EditCommands.AddEditAll(pr, pr.ActiveSequenceId, at));
                    else owner.Change("Razor", pr => EditCommands.Split(pr, pr.ActiveSequenceId, t, id, at));
                }
                snapLine.IsVisible = false; e.Handled = true; return true;
            }
            if (tool == EditTool.TrackForward && hit is { } lane)
            {
                long from = hit.Value.Clip?.Start ?? (long)Math.Round(p.X / ppf);
                var tracks = owner.trackForwardSingle ? [lane.Track] : owner.Sequence.Tracks.Where(t => !t.Locked).ToList();
                forward = [.. tracks.SelectMany(t => t.Clips).Where(c => c.Start >= from).Select(c => (CardFor(c.Id)!, c.Start)).Where(x => x.Item1 is not null)];
                if (forward.Count == 0) return true;
                forwardFrom = from; trimTrack = lane.Track.Id; gesture = Gesture.TrackForward;
                foreach (var (card, _) in forward) { card.BorderBrush = AccentBrush; card.ZIndex = 10; }
                e.Pointer.Capture(this); e.Handled = true; return true;
            }
            if (TrimTool && EdgeAt(p) is { } edge && !edge.Track.Locked)
            {
                trimClip = edge.Clip.Id; trimTrack = edge.Track.Id; trimHead = edge.Head; trimStart = edge.Clip.Start; trimDuration = edge.Clip.Duration;
                trimCard = CardFor(edge.Clip.Id);
                trimNeighbour = edge.Head ? edge.Track.Clips.FirstOrDefault(c => c.End == edge.Clip.Start) : edge.Track.Clips.FirstOrDefault(c => c.Start == edge.Clip.End);
                neighbourCard = trimNeighbour is null ? null : CardFor(trimNeighbour.Id);
                owner.selectedTrack = edge.Track.Id; owner.selectedClip = edge.Clip.Id; owner.BuildInspector();
                gesture = Gesture.EdgeTrim; e.Pointer.Capture(this); e.Handled = true; return true;
            }
            if (tool is EditTool.Slip or EditTool.Slide && hit is { Clip: { } body } target && !target.Track.Locked)
            {
                trimClip = body.Id; trimTrack = target.Track.Id; trimStart = body.Start; trimDuration = body.Duration; trimCard = CardFor(body.Id);
                owner.selectedTrack = target.Track.Id; owner.selectedClip = body.Id; owner.BuildInspector();
                gesture = tool == EditTool.Slip ? Gesture.Slip : Gesture.Slide; e.Pointer.Capture(this); e.Handled = true; return true;
            }
            return false;
        }

        bool ToolMoved(PointerEventArgs e, Point p)
        {
            double ppf = PixelsPerFrame;
            if (gesture == Gesture.None)
            {
                // Hover feedback: trim cursors on clip edges, a red/blue edge for ripple/roll like Premiere.
                if (TrimTool && dragGroup is null && !scrubbing)
                {
                    var edge = EdgeAt(p);
                    Cursor = edge is null ? owner.timeline.Cursor : new Cursor(owner.tool switch { EditTool.Rolling => StandardCursorType.SizeWestEast, EditTool.RateStretch => StandardCursorType.SizeWestEast, _ => edge.Value.Head ? StandardCursorType.LeftSide : StandardCursorType.RightSide });
                }
                return false;
            }
            long raw = (long)Math.Round((p.X - origin.X) / ppf);
            switch (gesture)
            {
                case Gesture.Hand:
                {
                    var now = e.GetPosition(owner.timelineScroller);
                    owner.timelineScroller.Offset = new Vector(Math.Max(0, handOffset.X - (now.X - handOrigin.X)), Math.Max(0, handOffset.Y - (now.Y - handOrigin.Y)));
                    return true;
                }
                case Gesture.TrackForward:
                {
                    long delta = Math.Max(raw, -forward.Min(f => f.Start));
                    long snapped = Snap(forwardFrom + delta, forward.Select(f => (Guid)f.Card.Tag!).ToHashSet()); delta = snapped - forwardFrom;
                    gestureDelta = delta;
                    foreach (var (card, start) in forward) SetLeft(card, (start + delta) * ppf);
                    Badge(Delta(delta), (forwardFrom + delta) * ppf, RulerHeight + 2);
                    return true;
                }
                case Gesture.Slip:
                    gestureDelta = raw;
                    Badge("Slip " + Delta(-raw), trimStart * ppf, Canvas.GetTop(trimCard!) + TrackHeight - 2);
                    return true;
                case Gesture.Slide:
                {
                    long snapped = Snap(trimStart + raw, [trimClip]); gestureDelta = snapped - trimStart;
                    SetLeft(trimCard!, snapped * ppf);
                    Badge("Slide " + Delta(gestureDelta), snapped * ppf, Canvas.GetTop(trimCard!) + TrackHeight - 2);
                    return true;
                }
                case Gesture.EdgeTrim:
                {
                    long edgeFrame = Snap((trimHead ? trimStart : trimStart + trimDuration) + raw, [trimClip]);
                    long delta = edgeFrame - (trimHead ? trimStart : trimStart + trimDuration);
                    // Keep at least one frame; selection trims also stop at the neighbouring clip.
                    delta = trimHead ? Math.Min(delta, trimDuration - 1) : Math.Max(delta, 1 - trimDuration);
                    if (owner.tool == EditTool.Selection && trimNeighbour is not null)
                        delta = trimHead ? Math.Max(delta, trimNeighbour.End - trimStart) : Math.Min(delta, trimNeighbour.Start - (trimStart + trimDuration));
                    if (trimHead && owner.tool is EditTool.Selection or EditTool.Rolling or EditTool.RateStretch) delta = Math.Max(delta, -trimStart);
                    gestureDelta = delta;
                    if (trimCard is not null)
                    {
                        bool ripple = owner.tool == EditTool.Ripple;
                        double left = trimHead && !ripple ? (trimStart + delta) * ppf : trimStart * ppf;
                        double width = Math.Max(4, (trimDuration + (trimHead ? -delta : delta)) * ppf);
                        SetLeft(trimCard, left); trimCard.Width = width; trimCard.BorderBrush = owner.tool == EditTool.Ripple ? Brush("#F5C542") : owner.tool == EditTool.Rolling ? Brush("#6CB8FF") : AccentBrush;
                        if (owner.tool == EditTool.Rolling && neighbourCard is not null && trimNeighbour is not null)
                        {
                            if (trimHead) neighbourCard.Width = Math.Max(4, (trimNeighbour.Duration + delta) * ppf);
                            else { SetLeft(neighbourCard, (trimNeighbour.Start + delta) * ppf); neighbourCard.Width = Math.Max(4, (trimNeighbour.Duration - delta) * ppf); }
                        }
                    }
                    string what = owner.tool switch { EditTool.Ripple => "Ripple", EditTool.Rolling => "Roll", EditTool.RateStretch => $"Stretch {(trimDuration * 100.0 / Math.Max(1, trimDuration + (trimHead ? -delta : delta))):0}% speed", _ => "Trim" };
                    Badge($"{what} {Delta(delta)}", edgeFrame * ppf + 6, Canvas.GetTop(trimCard!) + TrackHeight - 2);
                    return true;
                }
            }
            return false;
        }

        bool ToolReleased(PointerReleasedEventArgs e)
        {
            if (gesture == Gesture.None) return false;
            var g = gesture; gesture = Gesture.None; e.Pointer.Capture(null); snapLine.IsVisible = false; dragBadge.IsVisible = false;
            long delta = gestureDelta; var id = trimClip; var t = trimTrack; bool head = trimHead; long start = trimStart, duration = trimDuration;
            try
            {
                switch (g)
                {
                    case Gesture.Hand: return true;
                    case Gesture.TrackForward when delta != 0:
                    {
                        long from = forwardFrom; IReadOnlyCollection<Guid>? tracks = owner.trackForwardSingle ? [t] : null;
                        owner.Change("Move clips forward", p => EditCommands.ShiftFrom(p, p.ActiveSequenceId, from, delta, tracks)); break;
                    }
                    case Gesture.Slip when delta != 0: owner.Change("Slip", p => EditCommands.SlipLinked(p, p.ActiveSequenceId, id, -delta)); break;
                    case Gesture.Slide when delta != 0: owner.Change("Slide", p => EditCommands.Slide(p, p.ActiveSequenceId, t, id, delta)); break;
                    case Gesture.EdgeTrim when delta != 0:
                        switch (owner.tool)
                        {
                            case EditTool.Ripple: owner.Change("Ripple trim", p => EditCommands.RippleTrimEdge(p, p.ActiveSequenceId, id, head, delta)); break;
                            case EditTool.Rolling when trimNeighbour is { } n:
                            {
                                Guid left = head ? n.Id : id, right = head ? id : n.Id;
                                owner.Change("Rolling edit", p => EditCommands.RollEdit(p, p.ActiveSequenceId, t, left, right, delta)); break;
                            }
                            case EditTool.RateStretch:
                            {
                                long length = duration + (head ? -delta : delta);
                                owner.Change("Rate stretch", p => EditCommands.RateStretchLinked(p, p.ActiveSequenceId, id, length, head)); break;
                            }
                            default:
                                owner.Change("Trim", p => EditCommands.Trim(p, p.ActiveSequenceId, t, id, head ? start + delta : start, head ? duration - delta : duration + delta)); break;
                        }
                        break;
                }
            }
            finally { Rebuild(); }
            return true;
        }

        void ZoomAt(double x, double factor)
        {
            double frame = x / PixelsPerFrame, offset = x - owner.timelineScroller.Offset.X;
            owner.zoom.Value = Math.Clamp(owner.zoom.Value * factor, owner.zoom.Minimum, owner.zoom.Maximum);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => owner.timelineScroller.Offset = new Vector(Math.Max(0, frame * PixelsPerFrame - offset), owner.timelineScroller.Offset.Y), Avalonia.Threading.DispatcherPriority.Loaded);
        }

        // ---- Effects and transitions dragged from the Effects panel ----
        public void DragEffectOver(string id, string name, bool transition, Point p)
        {
            effectId = id; effectIsTransition = transition;
            if (effectHighlight is null || !Children.Contains(effectHighlight))
            {
                effectHighlight = new Border { BorderBrush = AccentBrush, BorderThickness = new Thickness(2), Background = AccentTintBrush, CornerRadius = new CornerRadius(3), IsHitTestVisible = false, ZIndex = 30, IsVisible = false };
                Children.Add(effectHighlight);
            }
            var hit = HitTest(p);
            if (hit is not { Clip: { } clip } target) { effectHighlight.IsVisible = false; return; }
            double ppf = PixelsPerFrame, top = owner.RowTop(owner.DisplayTracks().IndexOf(target.Track)) + 3;
            if (transition)
            {
                // Transitions snap to the nearer edge of the clip, sized to the default one second.
                bool head = p.X - clip.Start * ppf < clip.End * ppf - p.X; double w = Math.Min(clip.Duration * ppf, owner.timelineFps.Value * DefaultTransitionSeconds * ppf);
                effectHighlight.Width = Math.Max(8, w); effectHighlight.Height = TrackHeight - 6; SetLeft(effectHighlight, head ? clip.Start * ppf : clip.End * ppf - w); SetTop(effectHighlight, top);
            }
            else { effectHighlight.Width = Math.Max(8, clip.Duration * ppf); effectHighlight.Height = TrackHeight - 6; SetLeft(effectHighlight, clip.Start * ppf); SetTop(effectHighlight, top); }
            effectHighlight.IsVisible = true;
            Badge(name, p.X + 12, p.Y + 12);
        }
        public void DropEffect(Point? p)
        {
            if (effectHighlight is not null) effectHighlight.IsVisible = false; dragBadge.IsVisible = false;
            var id = effectId; effectId = null;
            if (id is null || p is not { } at || HitTest(at) is not { Clip: { } clip } target) return;
            if (effectIsTransition)
            {
                double ppf = PixelsPerFrame; bool head = at.X - clip.Start * ppf < clip.End * ppf - at.X;
                var def = TransitionCatalog.Find(id)!;
                if (def.Audio != (target.Track.Kind == TrackKind.Audio)) { owner.status.Text = def.Audio ? "Audio transitions go on audio clips." : "Video transitions go on video clips."; return; }
                owner.selectedClip = clip.Id; owner.selectedTrack = target.Track.Id;
                owner.ApplyTransitionTo(target.Track.Id, clip.Id, id, head);
            }
            else owner.ApplyEffectTo(clip.Id, id);
        }
    }
}
