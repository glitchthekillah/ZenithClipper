namespace ZenithClipper.Editing;

/// <summary>
/// Premiere Pro timeline commands: add edit to all tracks, ripple trims to the playhead (Q/W), ripple delete of gaps,
/// lift/extract between In and Out, paste and paste-insert, slip/slide/roll on linked clips, frame holds and
/// transitions. Every command is atomic and undoable through the editor session.
/// </summary>
public static partial class EditCommands
{
 static IEnumerable<EditTrack> Unlocked(EditProject p,Guid s)=>Sequence(p,s).Tracks.Where(t=>!t.Locked);

 /// <summary>Ctrl+Shift+K: cut every unlocked clip under <paramref name="frame"/>.</summary>
 public static int AddEditAll(EditProject p,Guid s,long frame)
 {
  int cuts=0;
  Atomic(p,s,copy=>
  {
   foreach(var track in Unlocked(copy,s).ToList())
    foreach(var clip in track.Clips.Where(c=>c.Start<frame&&c.End>frame).ToList()){SplitSingle(copy,s,track.Id,clip.Id,frame);cuts++;}
  });
  return cuts;
 }

 /// <summary>
 /// Shifts everything that starts at or after <paramref name="from"/> by <paramref name="delta"/> on the given tracks,
 /// keeping sync: when a left shift would collide on a track, that track (and so the edit) refuses.
 /// </summary>
 static void Shift(IEnumerable<EditTrack> tracks,long from,long delta,HashSet<Guid>? skip=null)
 {
  foreach(var track in tracks)
  {
   var moving=track.Clips.Where(c=>c.Start>=from&&(skip is null||!skip.Contains(c.Id))).ToList();
   if(moving.Count==0)continue;
   if(delta<0)
   {
    long limit=track.Clips.Where(c=>c.Start<from&&(skip is null||!skip.Contains(c.Id))).Select(c=>c.End).DefaultIfEmpty(0).Max();
    if(moving.Min(c=>c.Start)+delta<limit)throw new InvalidOperationException($"Not enough room on {track.Name} to ripple. Unlock or clear it first.");
   }
   foreach(var c in moving)c.Start=checked(c.Start+delta);
  }
 }

 /// <summary>Q (head) / W (tail): trim the clip under the playhead on the selected track to the playhead and close the gap.</summary>
 public static void RippleTrimToPlayhead(EditProject p,Guid s,Guid t,long frame,bool head)=>Atomic(p,s,copy=>
 {
  var track=Track(copy,s,t);var clip=track.Clips.FirstOrDefault(c=>c.Start<frame&&c.End>frame)??throw new InvalidOperationException("Put the playhead over a clip on the selected track.");
  var group=Members(copy,s,clip.Id);Writable(group);
  long amount=head?frame-clip.Start:clip.End-frame,edge=head?clip.Start:clip.End;
  foreach(var m in group)
  {
   if(head)TrimSingle(copy,s,m.Track.Id,m.Clip.Id,frame,m.Clip.End-frame);
   else TrimSingle(copy,s,m.Track.Id,m.Clip.Id,m.Clip.Start,frame-m.Clip.Start);
  }
  // Everything after the removed span slides left on every unlocked track (Premiere's sync-locked ripple).
  var ids=group.Select(m=>m.Clip.Id).ToHashSet();
  Shift(Unlocked(copy,s),head?frame:edge,-amount,head?null:ids);
 });

 /// <summary>Ripple delete the empty space under <paramref name="frame"/> on a track.</summary>
 public static void CloseGap(EditProject p,Guid s,Guid t,long frame)=>Atomic(p,s,copy=>
 {
  var track=Track(copy,s,t);
  if(track.Clips.Any(c=>c.Start<=frame&&c.End>frame))throw new InvalidOperationException("There is no gap there.");
  long before=track.Clips.Where(c=>c.End<=frame).Select(c=>c.End).DefaultIfEmpty(0).Max();
  long after=track.Clips.Where(c=>c.Start>frame).Select(c=>c.Start).DefaultIfEmpty(-1).Min();
  if(after<0)throw new InvalidOperationException("Nothing follows this gap.");
  Shift(Unlocked(copy,s),after,before-after);
 });

 /// <summary>; Lift (leave a gap) or ' Extract (close it) the range between In and Out on every unlocked track.</summary>
 public static void LiftExtract(EditProject p,Guid s,long inPoint,long outPoint,bool extract)=>Atomic(p,s,copy=>
 {
  if(outPoint<=inPoint)throw new ArgumentException("Mark an In point before the Out point.");
  var none=new HashSet<Guid>();
  foreach(var track in Unlocked(copy,s).ToList())Clear(copy,s,track,inPoint,outPoint,none);
  if(extract)foreach(var track in Unlocked(copy,s))foreach(var c in track.Clips.Where(c=>c.Start>=outPoint))c.Start-=outPoint-inPoint;
 });

 /// <summary>
 /// Ctrl+V overwrites, Ctrl+Shift+V inserts (pushing everything after the playhead right). Clips keep their track and
 /// relative timing; links inside the pasted set are preserved with fresh ids.
 /// </summary>
 public static List<Guid> Paste(EditProject p,Guid s,IReadOnlyList<(Guid TrackId,TimelineClip Clip)> clipboard,long at,bool insert)
 {
  var created=new List<Guid>();
  Atomic(p,s,copy=>
  {
   if(clipboard.Count==0)return;
   long origin=clipboard.Min(c=>c.Clip.Start),span=clipboard.Max(c=>c.Clip.End)-origin;
   var tracks=Sequence(copy,s).Tracks;var links=new Dictionary<Guid,Guid>();
   if(insert)
   {
    foreach(var track in Unlocked(copy,s).ToList())foreach(var clip in track.Clips.Where(c=>c.Start<at&&c.End>at).ToList())SplitSingle(copy,s,track.Id,clip.Id,at);
    foreach(var track in Unlocked(copy,s))foreach(var c in track.Clips.Where(c=>c.Start>=at))c.Start+=span;
   }
   var placed=new List<(EditTrack,TimelineClip)>();
   foreach(var (trackId,source) in clipboard)
   {
    var track=tracks.FirstOrDefault(x=>x.Id==trackId)??throw new InvalidOperationException("The track these clips came from no longer exists.");
    if(track.Locked)throw new InvalidOperationException($"{track.Name} is locked.");
    var clip=ProjectValidation.Clone(source);clip.Id=Guid.NewGuid();clip.Start=at+source.Start-origin;
    if(source.LinkId is {} l){if(!links.TryGetValue(l,out var n))links[l]=n=Guid.NewGuid();clip.LinkId=n;clip.SourcePairId=n;}
    if(source.GroupId is {} g){if(!links.TryGetValue(g,out var n))links[g]=n=Guid.NewGuid();clip.GroupId=n;}
    placed.Add((track,clip));created.Add(clip.Id);
   }
   var ids=placed.Select(x=>x.Item2.Id).ToHashSet();
   foreach(var (track,clip) in placed){Clear(copy,s,track,clip.Start,clip.End,ids);track.Clips.Add(clip);Check(copy,s,clip);}
   foreach(var track in tracks)track.Clips.Sort((a,b)=>a.Start.CompareTo(b.Start));
  });
  return created;
 }

 /// <summary>Slip tool (Y): change which part of the source plays without moving the clip. Linked clips slip together.</summary>
 public static void SlipLinked(EditProject p,Guid s,Guid c,long delta)=>Atomic(p,s,copy=>
 {
  var group=Members(copy,s,c);Writable(group);
  foreach(var m in group){var candidate=ProjectValidation.Clone(m.Clip);candidate.SourceIn=checked(m.Clip.SourceIn+(long)Math.Round(delta*m.Clip.Speed)*(m.Clip.Reverse?-1:1));if(candidate.SourceIn<0)throw new ArgumentException("No more source media in that direction.");Check(copy,s,candidate);m.Clip.SourceIn=candidate.SourceIn;}
 });

 /// <summary>Rolling edit (N): move the cut between a clip and its neighbour; both change length, nothing else moves.</summary>
 public static void RollEdit(EditProject p,Guid s,Guid t,Guid left,Guid right,long delta)=>Atomic(p,s,copy=>
 {
  Roll(copy,s,t,left,right,delta);
  // Linked partners roll with them when they share the same cut on their own track.
  var lp=Members(copy,s,left).Where(m=>m.Clip.Id!=left).ToList();var rp=Members(copy,s,right).Where(m=>m.Clip.Id!=right).ToList();
  foreach(var a in lp){var b=rp.FirstOrDefault(r=>r.Track.Id==a.Track.Id&&r.Clip.Start==a.Clip.End-0&&a.Clip.End==r.Clip.Start);if(b.Clip is not null)Roll(copy,s,a.Track.Id,a.Clip.Id,b.Clip.Id,delta);}
 });

 /// <summary>
 /// Slide tool (U): move a clip between its neighbours. The clip keeps its content and length; the clip before it
 /// gets longer/shorter at its tail and the one after at its head, so the overall sequence length is unchanged.
 /// </summary>
 public static void Slide(EditProject p,Guid s,Guid t,Guid c,long delta)=>Atomic(p,s,copy=>
 {
  var track=Track(copy,s,t);var clip=Clip(track,c);
  var before=track.Clips.FirstOrDefault(x=>x.End==clip.Start&&x.Id!=clip.Id);var after=track.Clips.FirstOrDefault(x=>x.Start==clip.End&&x.Id!=clip.Id);
  if(before is null&&after is null)throw new InvalidOperationException("Slide needs a clip directly before or after.");
  if(before is not null){var a=ProjectValidation.Clone(before);a.Duration=checked(a.Duration+delta);if(a.Reverse)a.SourceIn=checked(a.SourceIn-Source(delta*a.Speed));a.Motion=Rebase(before,0,a.Duration);Check(copy,s,a);before.Duration=a.Duration;before.SourceIn=a.SourceIn;before.Motion=a.Motion;}
  else if(delta<0&&track.Clips.Any(x=>x.Id!=clip.Id&&x.End>clip.Start+delta&&x.Start<clip.Start))throw new InvalidOperationException("No room to slide left.");
  if(after is not null){var b=ProjectValidation.Clone(after);b.Start=checked(b.Start+delta);b.Duration=checked(b.Duration-delta);if(!b.Reverse)b.SourceIn=checked(b.SourceIn+Advance(after,delta));b.Motion=Rebase(after,delta,b.Duration);Check(copy,s,b);after.Start=b.Start;after.Duration=b.Duration;after.SourceIn=b.SourceIn;after.Motion=b.Motion;}
  else if(delta>0&&track.Clips.Any(x=>x.Id!=clip.Id&&x.Start<clip.End+delta&&x.End>clip.End))throw new InvalidOperationException("No room to slide right.");
  clip.Start=checked(clip.Start+delta);
 });

 /// <summary>Rate Stretch (R): change a clip's length by changing its speed; linked clips stretch with it.</summary>
 public static void RateStretchLinked(EditProject p,Guid s,Guid c,long duration,bool fromHead)=>Atomic(p,s,copy=>
 {
  var group=Members(copy,s,c);Writable(group);
  foreach(var m in group){long end=m.Clip.End;RateStretch(copy,s,m.Track.Id,m.Clip.Id,duration);if(fromHead)m.Clip.Start=end-duration;}
 });

 /// <summary>Add Frame Hold: from <paramref name="frame"/> to the end, the clip freezes on that frame.</summary>
 public static void AddFrameHold(EditProject p,Guid s,Guid t,Guid c,long frame,bool segmentOnly,long segmentLength)=>Atomic(p,s,copy=>
 {
  var track=Track(copy,s,t);var clip=Clip(track,c);if(frame<=clip.Start||frame>=clip.End)throw new InvalidOperationException("Put the playhead inside the clip.");
  long source=clip.Reverse?clip.SourceIn+Source((clip.End-frame)*clip.Speed):clip.SourceIn+Advance(clip,frame-clip.Start);
  var ids=track.Clips.Select(x=>x.Id).ToHashSet();SplitSingle(copy,s,t,c,frame);
  var held=track.Clips.Single(x=>!ids.Contains(x.Id));held.LinkId=null;held.HoldFrame=source;held.StreamIntent=ClipStreamIntent.VideoOnly;
  if(segmentOnly)
  {
   // Insert Frame Hold Segment: a still of segmentLength frames, then the rest of the clip continues after it.
   long rest=held.End;var tail=ProjectValidation.Clone(held);tail.Id=Guid.NewGuid();tail.HoldFrame=null;tail.LinkId=clip.LinkId;
   held.Duration=Math.Min(segmentLength,held.Duration);
   foreach(var other in Unlocked(copy,s))foreach(var x in other.Clips.Where(x=>x.Start>=frame&&x.Id!=held.Id))x.Start+=held.Duration;
   tail.Start=held.End;tail.SourceIn=source;tail.Duration=rest-frame;track.Clips.Add(tail);
  }
 });

 /// <summary>Applies, replaces or removes (kind = null) a transition on a clip's head or tail, mirroring Premiere's Ctrl+D.</summary>
 public static void SetTransition(EditProject p,Guid s,Guid t,Guid c,bool head,string? kind,long duration)=>Atomic(p,s,copy=>
 {
  var clip=Clip(Track(copy,s,t),c);
  var value=kind is null?null:new ClipTransition{Kind=kind,Duration=Math.Max(1,Math.Min(duration,clip.Duration))};
  if(head)clip.TransitionIn=value;else clip.TransitionOut=value;
  Check(copy,s,clip);
 });
 /// <summary>Track Select Forward (A / Shift+A) drag: move everything from <paramref name="from"/> on, on the given tracks (all when null).</summary>
 public static void ShiftFrom(EditProject p,Guid s,long from,long delta,IReadOnlyCollection<Guid>? trackIds=null)=>Atomic(p,s,copy=>
 {
  var tracks=Unlocked(copy,s).Where(t=>trackIds is null||trackIds.Contains(t.Id)).ToList();
  if(delta<0)Shift(tracks,from,delta);
  else foreach(var t in tracks)foreach(var c in t.Clips.Where(c=>c.Start>=from))c.Start=checked(c.Start+delta);
 });

 /// <summary>
 /// Speed/Duration with "Ripple edit, shifting trailing clips": sets the speed of a clip and its links (duration scales),
 /// and moves everything after them on their tracks so nothing overlaps or leaves a gap.
 /// </summary>
 public static void SetSpeedRipple(EditProject p,Guid s,Guid c,double speed,bool ripple)=>Atomic(p,s,copy=>
 {
  if(!double.IsFinite(speed)||speed<=0)throw new ArgumentException("Playback speed must be positive.");
  var group=Members(copy,s,c);Writable(group);var lead=group.Single(x=>x.Clip.Id==c).Clip;
  if(TimeRemap.Has(lead))throw new InvalidOperationException("Turn off time remapping to set a constant speed.");
  long oldEnd=lead.End,newDuration=Math.Max(1,checked((long)Math.Round(lead.Duration*lead.Speed/speed))),delta=newDuration-lead.Duration;
  var tracks=group.Select(m=>m.Track.Id).ToHashSet();
  if(ripple&&delta>0)foreach(var t in Sequence(copy,s).Tracks.Where(t=>tracks.Contains(t.Id)))foreach(var x in t.Clips.Where(x=>x.Start>=oldEnd))x.Start+=delta;
  foreach(var m in group){m.Clip.Duration=newDuration;m.Clip.Speed=speed;Check(copy,s,m.Clip);}
  if(ripple&&delta<0)foreach(var t in Sequence(copy,s).Tracks.Where(t=>tracks.Contains(t.Id)))foreach(var x in t.Clips.Where(x=>x.Start>=oldEnd))x.Start+=delta;
 });

 /// <summary>Ripple Edit tool (B): trim a clip edge and move everything after it so no gap or overlap appears.</summary>
 public static void RippleTrimEdge(EditProject p,Guid s,Guid c,bool head,long delta)=>Atomic(p,s,copy=>
 {
  var group=Members(copy,s,c);Writable(group);var clip=group.Single(m=>m.Clip.Id==c).Clip;long oldEnd=clip.End,start=clip.Start;
  // head: dragging right (delta>0) removes frames from the start; the clip stays put and later material moves left.
  long change=head?-delta:delta;
  if(clip.Duration+change<1)throw new ArgumentException("A clip needs at least one frame.");
  var ids=group.Select(m=>m.Clip.Id).ToHashSet();
  foreach(var m in group)
  {
   if(head){TrimSingle(copy,s,m.Track.Id,m.Clip.Id,m.Clip.Start+delta,m.Clip.Duration-delta);m.Clip.Start=start;}
   else TrimSingle(copy,s,m.Track.Id,m.Clip.Id,m.Clip.Start,m.Clip.Duration+delta);
  }
  Shift(Unlocked(copy,s),oldEnd,change,ids);
 });
}
