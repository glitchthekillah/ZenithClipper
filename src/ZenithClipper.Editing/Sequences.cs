namespace ZenithClipper.Editing;

/// <summary>Multiple sequences and nesting (Premiere's Nest command and sequence-in-sequence editing).</summary>
public static partial class EditCommands
{
 /// <summary>Adds an empty sequence with the active sequence's settings and two tracks, and makes it active.</summary>
 public static Guid NewSequence(EditProject p,string name,int? width=null,int? height=null,FrameRate? fps=null)
 {
  var active=p.Sequences.First(s=>s.Id==p.ActiveSequenceId);
  var seq=new EditSequence{Name=name,Width=width??active.Width,Height=height??active.Height,Fps=fps??active.Fps,Tracks=[new EditTrack{Name="Video 1",Kind=TrackKind.Video},new EditTrack{Name="Audio 1",Kind=TrackKind.Audio}]};
  var candidate=ProjectValidation.Clone(p);candidate.Sequences.Add(seq);candidate.ActiveSequenceId=seq.Id;ProjectValidation.Validate(candidate);
  p.Sequences.Add(seq);p.ActiveSequenceId=seq.Id;EnsureSequenceMedia(p,seq);return seq.Id;
 }

 /// <summary>Every sequence also appears in the media bin so it can be dragged into another one.</summary>
 public static MediaAsset EnsureSequenceMedia(EditProject p,EditSequence seq)
 {
  var item=p.Media.FirstOrDefault(m=>m.Kind==MediaKind.Sequence&&m.SequenceId==seq.Id);
  if(item is null){item=new MediaAsset{Kind=MediaKind.Sequence,SequenceId=seq.Id,Name=seq.Name,Path="",Fps=seq.Fps,Width=seq.Width,Height=seq.Height,HasAudio=true};p.Media.Add(item);}
  item.Name=seq.Name;item.DurationSeconds=seq.Fps.Seconds(seq.Duration);return item;
 }

 public static void DeleteSequence(EditProject p,Guid id)
 {
  if(p.Sequences.Count<=1)throw new InvalidOperationException("A project needs at least one sequence.");
  var media=p.Media.Where(m=>m.Kind==MediaKind.Sequence&&m.SequenceId==id).Select(m=>m.Id).ToHashSet();
  if(p.Sequences.Where(s=>s.Id!=id).SelectMany(s=>s.Tracks).SelectMany(t=>t.Clips).Any(c=>media.Contains(c.MediaId)))throw new InvalidOperationException("This sequence is nested in another one. Remove those clips first.");
  p.Sequences.RemoveAll(s=>s.Id==id);p.Media.RemoveAll(m=>media.Contains(m.Id));
  if(p.ActiveSequenceId==id)p.ActiveSequenceId=p.Sequences[0].Id;
 }

 /// <summary>
 /// Nest: moves the given clips (and their linked partners) into a new sequence, keeping their tracks and relative
 /// timing, and puts one nested clip in their place on the top-most video track they used (or the first audio track).
 /// </summary>
 public static Guid Nest(EditProject p,Guid s,IReadOnlyCollection<Guid> clipIds,string name)
 {
  var seq=p.Sequences.First(x=>x.Id==s);
  var ids=new HashSet<Guid>(clipIds);
  foreach(var c in seq.Tracks.SelectMany(t=>t.Clips).Where(c=>ids.Contains(c.Id)).ToList())if(c.LinkId is {} l)foreach(var x in seq.Tracks.SelectMany(t=>t.Clips).Where(x=>x.LinkId==l))ids.Add(x.Id);
  var picked=seq.Tracks.SelectMany(t=>t.Clips.Select(c=>(Track:t,Clip:c))).Where(x=>ids.Contains(x.Clip.Id)).ToList();
  if(picked.Count==0)throw new InvalidOperationException("Select a clip to nest.");
  if(picked.Any(x=>x.Track.Locked))throw new InvalidOperationException("Unlock the tracks first.");
  long start=picked.Min(x=>x.Clip.Start),end=picked.Max(x=>x.Clip.End);
  var inner=new EditSequence{Name=name,Width=seq.Width,Height=seq.Height,Fps=seq.Fps};
  foreach(var track in seq.Tracks.Where(t=>picked.Any(x=>x.Track.Id==t.Id)))
  {
   var copy=new EditTrack{Name=track.Name,Kind=track.Kind,GainDb=0};
   foreach(var (_,clip) in picked.Where(x=>x.Track.Id==track.Id)){var moved=ProjectValidation.Clone(clip);moved.Start-=start;copy.Clips.Add(moved);}
   inner.Tracks.Add(copy);
  }
  if(!inner.Tracks.Any(t=>t.Kind==TrackKind.Video))inner.Tracks.Insert(0,new EditTrack{Name="Video 1",Kind=TrackKind.Video});
  if(!inner.Tracks.Any(t=>t.Kind==TrackKind.Audio))inner.Tracks.Add(new EditTrack{Name="Audio 1",Kind=TrackKind.Audio});
  var host=picked.Where(x=>x.Track.Kind==TrackKind.Video).Select(x=>x.Track).LastOrDefault()??seq.Tracks.First(t=>t.Kind==TrackKind.Video&&!t.Locked);
  var candidate=ProjectValidation.Clone(p);
  var cs=candidate.Sequences.First(x=>x.Id==s);
  foreach(var t in cs.Tracks)t.Clips.RemoveAll(c=>ids.Contains(c.Id));
  candidate.Sequences.Add(inner);
  var media=EnsureSequenceMedia(candidate,inner);
  var nested=new TimelineClip{Name=name,MediaId=media.Id,Start=start,Duration=end-start,StreamIntent=ClipStreamIntent.Auto};
  cs.Tracks.First(t=>t.Id==host.Id).Clips.Add(nested);cs.Tracks.First(t=>t.Id==host.Id).Clips.Sort((a,b)=>a.Start.CompareTo(b.Start));
  ProjectValidation.Validate(candidate);
  // Apply in place so callers holding the sequence keep a live reference.
  foreach(var t in seq.Tracks){var n=cs.Tracks.First(x=>x.Id==t.Id);t.Clips.Clear();t.Clips.AddRange(n.Clips);}
  p.Sequences.Add(candidate.Sequences.First(x=>x.Id==inner.Id));
  p.Media.Add(candidate.Media.First(m=>m.Id==media.Id));
  return nested.Id;
 }
 /// <summary>
 /// Create Multi-Camera Source Sequence: each selected video clip becomes one camera (its own video track, synced by
 /// in point, with its linked audio), and one multicam clip showing camera 1 replaces them at the earliest start.
 /// </summary>
 public static Guid CreateMulticam(EditProject p,Guid s,IReadOnlyCollection<Guid> clipIds,string name)
 {
  var seq=p.Sequences.First(x=>x.Id==s);
  var all=seq.Tracks.SelectMany(t=>t.Clips.Select(c=>(Track:t,Clip:c))).ToList();
  var cameras=all.Where(x=>clipIds.Contains(x.Clip.Id)&&x.Track.Kind==TrackKind.Video).OrderBy(x=>x.Track.Kind).ThenBy(x=>seq.Tracks.IndexOf(x.Track)).ThenBy(x=>x.Clip.Start).ToList();
  if(cameras.Count<2)throw new InvalidOperationException("Select two or more video clips (Shift+click) to make a multicam clip.");
  if(cameras.Count>9)throw new InvalidOperationException("Multicam supports up to 9 cameras.");
  if(all.Any(x=>clipIds.Contains(x.Clip.Id)&&x.Track.Locked))throw new InvalidOperationException("Unlock the tracks first.");
  var inner=new EditSequence{Name=name,Width=seq.Width,Height=seq.Height,Fps=seq.Fps};
  var removed=new HashSet<Guid>();int n=0;
  foreach(var (track,clip) in cameras)
  {
   n++;var v=new EditTrack{Name=$"Camera {n}",Kind=TrackKind.Video};var moved=ProjectValidation.Clone(clip);moved.Start=0;moved.TransitionIn=moved.TransitionOut=null;v.Clips.Add(moved);inner.Tracks.Add(v);removed.Add(clip.Id);
   if(clip.LinkId is {} link)foreach(var partner in all.Where(x=>x.Clip.LinkId==link&&x.Clip.Id!=clip.Id))
   {var a=new EditTrack{Name=$"Camera {n} audio",Kind=TrackKind.Audio};var pm=ProjectValidation.Clone(partner.Clip);pm.Start=0;pm.TransitionIn=pm.TransitionOut=null;a.Clips.Add(pm);inner.Tracks.Add(a);removed.Add(partner.Clip.Id);}
  }
  // Only camera 1's audio plays by default, like Premiere's "Camera 1" audio sync option.
  foreach(var t in inner.Tracks.Where(t=>t.Kind==TrackKind.Audio).Skip(1))t.Muted=true;
  if(!inner.Tracks.Any(t=>t.Kind==TrackKind.Audio))inner.Tracks.Add(new EditTrack{Name="Audio 1",Kind=TrackKind.Audio});
  long start=cameras.Min(x=>x.Clip.Start),length=inner.Tracks.SelectMany(t=>t.Clips).Max(c=>c.End);
  var host=cameras[0].Track.Id;
  var candidate=ProjectValidation.Clone(p);var cs=candidate.Sequences.First(x=>x.Id==s);
  foreach(var t in cs.Tracks)t.Clips.RemoveAll(c=>removed.Contains(c.Id));
  candidate.Sequences.Add(inner);var media=EnsureSequenceMedia(candidate,inner);
  var clipOut=new TimelineClip{Name=name,MediaId=media.Id,Start=start,Duration=length,StreamIntent=ClipStreamIntent.Auto,MulticamAngle=1};
  var hostTrack=cs.Tracks.First(t=>t.Id==host);
  if(hostTrack.Clips.Any(c=>c.Start<start+length&&c.End>start))throw new InvalidOperationException("Make room after the first camera clip: the multicam clip runs as long as the longest camera.");
  hostTrack.Clips.Add(clipOut);hostTrack.Clips.Sort((a,b)=>a.Start.CompareTo(b.Start));
  ProjectValidation.Validate(candidate);
  foreach(var t in seq.Tracks){var x=cs.Tracks.First(y=>y.Id==t.Id);t.Clips.Clear();t.Clips.AddRange(x.Clips);}
  p.Sequences.Add(candidate.Sequences.First(x=>x.Id==inner.Id));p.Media.Add(candidate.Media.First(m=>m.Id==media.Id));
  return clipOut.Id;
 }

 /// <summary>Multicam switching: cut the multicam clip at <paramref name="frame"/> and show camera <paramref name="angle"/> from there.</summary>
 public static void SwitchAngle(EditProject p,Guid s,Guid trackId,Guid clipId,long frame,int angle)
 {
  var track=Sequence(p,s).Tracks.First(t=>t.Id==trackId);var clip=track.Clips.First(c=>c.Id==clipId);
  if(clip.MulticamAngle is null)throw new InvalidOperationException("Select a multicam clip.");
  if(frame>clip.Start&&frame<clip.End){Split(p,s,trackId,clipId,frame);clip=track.Clips.First(c=>c.Start==frame);}
  var candidate=ProjectValidation.Clone(p);Sequence(candidate,s).Tracks.First(t=>t.Id==trackId).Clips.First(c=>c.Id==clip.Id).MulticamAngle=angle;ProjectValidation.Validate(candidate);
  clip.MulticamAngle=angle;
 }
}
