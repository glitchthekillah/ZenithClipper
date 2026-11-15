namespace ZenithClipper.Editing;

public static partial class EditCommands
{
 /// <summary>
 /// Premiere-style drag: the clip (and its linked partners) land at <paramref name="start"/> on the lanes given in
 /// <paramref name="lanes"/> (clip id → track id; partners not listed keep their lane) and overwrite whatever they cover.
 /// Covered clips are removed, trimmed or split around the drop; a covered clip that was linked is unlinked so its partner
 /// is never silently cut. One atomic, undoable edit.
 /// </summary>
 public static void MoveOverwrite(EditProject p,Guid s,Guid c,long start,IReadOnlyDictionary<Guid,Guid>? lanes=null,IEnumerable<Guid>? alsoMove=null)=>Atomic(p,s,copy=>
 {
  var ids=Closure(copy,s,[c,..alsoMove??[]]);
  var group=Sequence(copy,s).Tracks.SelectMany(t=>t.Clips.Select(x=>(Track:t,Clip:x))).Where(x=>ids.Contains(x.Clip.Id)).ToList();Writable(group);var selected=group.Single(x=>x.Clip.Id==c);long delta=checked(start-selected.Clip.Start);
  var moving=group.Select(m=>m.Clip.Id).ToHashSet();var placed=new List<(EditTrack Track,TimelineClip Clip)>();
  foreach(var member in group)
  {
   var target=lanes is not null&&lanes.TryGetValue(member.Clip.Id,out var lane)?Track(copy,s,lane):member.Track;
   if(target.Kind!=member.Track.Kind)throw new ArgumentException("Video clips go on video tracks and audio clips on audio tracks.");
   if(target.Locked)throw new InvalidOperationException($"{target.Name} is locked.");
   long next=checked(member.Clip.Start+delta);if(next<0)throw new ArgumentException("Clips cannot start before 0:00.");
   member.Track.Clips.Remove(member.Clip);member.Clip.Start=next;placed.Add((target,member.Clip));
  }
  foreach(var (track,clip) in placed){Clear(copy,s,track,clip.Start,clip.End,moving);track.Clips.Add(clip);Check(copy,s,clip);}
  foreach(var track in placed.Select(x=>x.Track).Distinct())track.Clips.Sort((a,b)=>a.Start.CompareTo(b.Start));
 });

 /// <summary>The given clips plus everything linked or grouped with them, transitively.</summary>
 public static HashSet<Guid> Closure(EditProject p,Guid s,IEnumerable<Guid> clipIds)
 {
  var all=Sequence(p,s).Tracks.SelectMany(t=>t.Clips).ToList();var result=new HashSet<Guid>(clipIds.Where(id=>all.Any(c=>c.Id==id)));
  for(bool grew=true;grew;)
  {
   grew=false;
   var keys=all.Where(c=>result.Contains(c.Id)).SelectMany(c=>new[]{c.LinkId,c.GroupId}).Where(x=>x is not null).ToHashSet();
   foreach(var c in all)if(!result.Contains(c.Id)&&(keys.Contains(c.LinkId)&&c.LinkId is not null||keys.Contains(c.GroupId)&&c.GroupId is not null)){result.Add(c.Id);grew=true;}
  }
  return result;
 }

 public static void Group(EditProject p,Guid s,IEnumerable<Guid> clipIds)
 {
  var ids=Closure(p,s,clipIds);if(ids.Count<2)throw new InvalidOperationException("Select two or more clips to group (Shift+click to add clips).");
  var group=Guid.NewGuid();foreach(var c in Sequence(p,s).Tracks.SelectMany(t=>t.Clips).Where(c=>ids.Contains(c.Id)))c.GroupId=group;
 }
 public static void Ungroup(EditProject p,Guid s,IEnumerable<Guid> clipIds)
 {
  var ids=Closure(p,s,clipIds);foreach(var c in Sequence(p,s).Tracks.SelectMany(t=>t.Clips).Where(c=>ids.Contains(c.Id)))c.GroupId=null;
 }

 /// <summary>Deletes every listed clip and its links/group, optionally rippling later clips on the same tracks left.</summary>
 public static void DeleteMany(EditProject p,Guid s,IEnumerable<Guid> clipIds,bool ripple)=>Atomic(p,s,copy=>
 {
  var ids=Closure(copy,s,clipIds);
  var gaps=new List<(long Start,long Length)>();var touched=new HashSet<Guid>();
  foreach(var track in Sequence(copy,s).Tracks)
  {
   var removed=track.Clips.Where(c=>ids.Contains(c.Id)).OrderByDescending(c=>c.Start).ToList();if(removed.Count==0)continue;
   if(track.Locked)throw new InvalidOperationException($"{track.Name} is locked.");
   touched.Add(track.Id);
   foreach(var r in removed){track.Clips.Remove(r);if(ripple){foreach(var later in track.Clips.Where(x=>x.Start>=r.End))later.Start-=r.Duration;gaps.Add((r.Start,r.Duration));}}
  }
  // Sync lock: other unlocked, sync-locked tracks close the same gaps when nothing sits inside them.
  if(ripple)foreach(var (gapStart,gapLength) in gaps.GroupBy(g=>g).Select(g=>g.Key).OrderByDescending(g=>g.Start))
  foreach(var track in Sequence(copy,s).Tracks.Where(t=>!touched.Contains(t.Id)&&t.SyncLock&&!t.Locked))
  {
   if(track.Clips.Any(c=>c.Start<gapStart+gapLength&&c.End>gapStart))continue;
   foreach(var later in track.Clips.Where(x=>x.Start>=gapStart+gapLength))later.Start-=gapLength;
  }
 });

 /// <summary>Opens the range [from, to) on one track by removing, trimming or splitting the clips in it.</summary>
 static void Clear(EditProject p,Guid s,EditTrack track,long from,long to,HashSet<Guid> keep)
 {
  var cut=new HashSet<Guid>();
  foreach(var other in track.Clips.Where(x=>!keep.Contains(x.Id)&&x.Start<to&&x.End>from).ToList())
  {
   if(other.LinkId is {} link)cut.Add(link);
   if(other.Start>=from&&other.End<=to){track.Clips.Remove(other);continue;}
   if(other.Start<from&&other.End>to)
   {
    var before=track.Clips.Select(x=>x.Id).ToHashSet();SplitSingle(p,s,track.Id,other.Id,to);
    var right=track.Clips.Single(x=>!before.Contains(x.Id));right.LinkId=null;
    TrimSingle(p,s,track.Id,other.Id,other.Start,from-other.Start);continue;
   }
   if(other.Start<from)TrimSingle(p,s,track.Id,other.Id,other.Start,from-other.Start);
   else TrimSingle(p,s,track.Id,other.Id,to,other.End-to);
  }
  if(cut.Count==0)return;
  foreach(var clip in Sequence(p,s).Tracks.SelectMany(t=>t.Clips).Where(x=>x.LinkId is {} l&&cut.Contains(l)&&!keep.Contains(x.Id))){clip.SourcePairId??=clip.LinkId;clip.LinkId=null;}
 }
}
