namespace ZenithClipper.Editing;

public sealed record AppendedMedia(Guid SelectedClipId,Guid SelectedTrackId,Guid? VideoClipId,Guid? AudioClipId);
public static partial class EditCommands
{
 public static AppendedMedia AppendMedia(EditProject p,Guid sequenceId,Guid mediaId,long start,Guid? preferredTrack=null)
 {
  var s=Sequence(p,sequenceId);var media=p.Media.Single(m=>m.Id==mediaId);long duration=Math.Max(1,s.Fps.Frames(media.DurationSeconds));
  long sourceIn=0;if(media.SubclipIn is {} subIn&&media.SubclipOut is {} subOut){sourceIn=s.Fps.Frames(subIn);duration=Math.Max(1,s.Fps.Frames(subOut)-sourceIn);}
  EditTrack Target(TrackKind kind){var t=s.Tracks.FirstOrDefault(t=>t.Id==preferredTrack&&t.Kind==kind&&!t.Locked)??s.Tracks.FirstOrDefault(t=>t.Kind==kind&&!t.Locked);if(t is null){t=new EditTrack{Kind=kind,Name=$"{kind} {s.Tracks.Count(x=>x.Kind==kind)+1}"};s.Tracks.Add(t);}return t;}
  TimelineClip New(ClipStreamIntent intent)=>new(){MediaId=media.Id,Name=media.Name,Start=start,SourceIn=sourceIn,Duration=duration,StreamIntent=intent};
  if(media.Kind==MediaKind.Audio){var t=Target(TrackKind.Audio);var a=New(ClipStreamIntent.AudioOnly);t.Clips.Add(a);return new(a.Id,t.Id,null,a.Id);}
  var video=New(ClipStreamIntent.VideoOnly);var videoTrack=Target(TrackKind.Video);videoTrack.Clips.Add(video);TimelineClip? audio=null;
  if(media.Kind==MediaKind.Video&&media.HasAudio){audio=New(ClipStreamIntent.AudioOnly);audio.Name=media.Name+" · audio";var link=Guid.NewGuid();video.LinkId=audio.LinkId=video.SourcePairId=audio.SourcePairId=link;Target(TrackKind.Audio).Clips.Add(audio);}
  return new(video.Id,videoTrack.Id,video.Id,audio?.Id);
 }
 static List<(EditTrack Track,TimelineClip Clip)> Members(EditProject p,Guid s,Guid c)
 {
  var all=Sequence(p,s).Tracks.SelectMany(t=>t.Clips.Select(clip=>(Track:t,Clip:clip))).ToList();var selected=all.Single(x=>x.Clip.Id==c);return selected.Clip.LinkId is {} link?all.Where(x=>x.Clip.LinkId==link).ToList():[selected];
 }
 static void Writable(IEnumerable<(EditTrack Track,TimelineClip Clip)> members){if(members.Any(m=>m.Track.Locked))throw new InvalidOperationException("Unlock every linked track before changing linked clips.");}
 static void Atomic(EditProject p,Guid s,Action<EditProject> edit)
 {
  var copy=ProjectValidation.Clone(p);edit(copy);ProjectValidation.Validate(copy);
  var existing=Sequence(p,s).Tracks.SelectMany(t=>t.Clips).ToDictionary(c=>c.Id);
  foreach(var track in Sequence(p,s).Tracks){var changed=Sequence(copy,s).Tracks.Single(t=>t.Id==track.Id);var clips=new List<TimelineClip>();foreach(var value in changed.Clips){if(existing.TryGetValue(value.Id,out var current)){CopyClip(current,value);clips.Add(current);}else clips.Add(value);}track.Clips.Clear();track.Clips.AddRange(clips);}
  var originalTracks=Sequence(p,s).Tracks;var ids=originalTracks.Select(t=>t.Id).ToHashSet();originalTracks.AddRange(Sequence(copy,s).Tracks.Where(t=>!ids.Contains(t.Id)));
 }
 static void CopyClip(TimelineClip a,TimelineClip b){a.MediaId=b.MediaId;a.Name=b.Name;a.Start=b.Start;a.SourceIn=b.SourceIn;a.Duration=b.Duration;a.Speed=b.Speed;a.GainDb=b.GainDb;a.Opacity=b.Opacity;a.Reverse=b.Reverse;a.LinkId=b.LinkId;a.GroupId=b.GroupId;a.MaintainPitch=b.MaintainPitch;a.MulticamAngle=b.MulticamAngle;a.SourcePairId=b.SourcePairId;a.StreamIntent=b.StreamIntent;a.Transform=b.Transform;a.Effects=b.Effects;a.Enabled=b.Enabled;a.TransitionIn=b.TransitionIn;a.TransitionOut=b.TransitionOut;a.HoldFrame=b.HoldFrame;a.Label=b.Label;a.BlendMode=b.BlendMode;a.Motion=b.Motion;a.Mask=b.Mask;}
 public static void Move(EditProject p,Guid s,Guid t,Guid c,Guid targetTrack,long start)=>Atomic(p,s,copy=>{var group=Members(copy,s,c);Writable(group);var selected=group.Single(x=>x.Clip.Id==c);long delta=checked(start-selected.Clip.Start);foreach(var member in group)MoveSingle(copy,s,member.Track.Id,member.Clip.Id,member.Clip.Id==c?targetTrack:member.Track.Id,checked(member.Clip.Start+delta));});
 public static void Trim(EditProject p,Guid s,Guid t,Guid c,long start,long duration)=>Atomic(p,s,copy=>{var group=Members(copy,s,c);Writable(group);var selected=group.Single(x=>x.Clip.Id==c);long deltaStart=start-selected.Clip.Start,deltaDuration=duration-selected.Clip.Duration;foreach(var member in group)TrimSingle(copy,s,member.Track.Id,member.Clip.Id,checked(member.Clip.Start+deltaStart),checked(member.Clip.Duration+deltaDuration));});
 public static void Split(EditProject p,Guid s,Guid t,Guid c,long frame)=>Atomic(p,s,copy=>
 {
  var group=Members(copy,s,c);Writable(group);var selected=group.Single(x=>x.Clip.Id==c);long local=frame-selected.Clip.Start;Guid? left=selected.Clip.LinkId;Guid? right=left.HasValue?Guid.NewGuid():null;
  foreach(var member in group){var ids=member.Track.Clips.Select(x=>x.Id).ToHashSet();SplitSingle(copy,s,member.Track.Id,member.Clip.Id,member.Clip.Start+local);var created=member.Track.Clips.Single(x=>!ids.Contains(x.Id));member.Clip.LinkId=left;created.LinkId=right;if(left.HasValue){member.Clip.SourcePairId=left;created.SourcePairId=right;}}
 });
 public static void Delete(EditProject p,Guid s,Guid t,Guid c,bool ripple=false)=>Atomic(p,s,copy=>
 {
  var group=Members(copy,s,c);Writable(group);var shifts=new Dictionary<Guid,long>();
  if(ripple)foreach(var removed in group)foreach(var next in removed.Track.Clips.Where(x=>x.Id!=removed.Clip.Id&&x.Start>=removed.Clip.End))foreach(var partner in Members(copy,s,next.Id)){if(partner.Track.Locked)throw new InvalidOperationException("A linked track is locked.");shifts[partner.Clip.Id]=-removed.Clip.Duration;}
  foreach(var member in group)member.Track.Clips.Remove(member.Clip);
  foreach(var clip in Sequence(copy,s).Tracks.SelectMany(x=>x.Clips))if(shifts.TryGetValue(clip.Id,out var delta))clip.Start=checked(clip.Start+delta);
 });
 public static void ChangeLinkedClip(EditProject p,Guid s,Guid c,Action<TimelineClip> change)=>Atomic(p,s,copy=>
 {
  var group=Members(copy,s,c);var selected=group.Single(x=>x.Clip.Id==c);if(selected.Track.Locked)throw new InvalidOperationException("Track is locked.");var before=ProjectValidation.Clone(selected.Clip);change(selected.Clip);var after=selected.Clip;
  bool timing=before.Start!=after.Start||before.SourceIn!=after.SourceIn||before.Duration!=after.Duration||before.Speed!=after.Speed||before.Reverse!=after.Reverse;
  if(!timing)return;Writable(group);foreach(var member in group.Where(x=>x.Clip.Id!=c)){var other=member.Clip;other.Start=checked(other.Start+after.Start-before.Start);other.SourceIn=checked(other.SourceIn+after.SourceIn-before.SourceIn);other.Duration=checked(other.Duration+after.Duration-before.Duration);other.Speed*=after.Speed/before.Speed;if(before.Reverse!=after.Reverse)other.Reverse=after.Reverse;}
 });
 public static void Unlink(EditProject p,Guid s,Guid c)=>Atomic(p,s,copy=>
 {
  var group=Members(copy,s,c);Writable(group);foreach(var member in group){if(member.Clip.SourcePairId is null)member.Clip.SourcePairId=member.Clip.LinkId;if(member.Track.Kind==TrackKind.Audio)member.Clip.StreamIntent=ClipStreamIntent.AudioOnly;else if(group.Any(x=>x.Track.Kind==TrackKind.Audio&&x.Clip.MediaId==member.Clip.MediaId))member.Clip.StreamIntent=ClipStreamIntent.VideoOnly;member.Clip.LinkId=null;}
 });
 public static void Relink(EditProject p,Guid s,Guid c)=>Atomic(p,s,copy=>
 {
  var sequence=Sequence(copy,s);var selected=sequence.Tracks.SelectMany(t=>t.Clips.Select(clip=>(Track:t,Clip:clip))).Single(x=>x.Clip.Id==c);if(selected.Clip.LinkId is not null)throw new InvalidOperationException("This clip is already linked.");
  var candidates=sequence.Tracks.Where(t=>t.Kind!=selected.Track.Kind).SelectMany(t=>t.Clips.Select(clip=>(Track:t,Clip:clip))).Where(x=>x.Clip.LinkId is null&&x.Clip.MediaId==selected.Clip.MediaId&&(selected.Clip.SourcePairId is not null?x.Clip.SourcePairId==selected.Clip.SourcePairId:x.Clip.SourceIn==selected.Clip.SourceIn&&x.Clip.Duration==selected.Clip.Duration)).ToList();
  if(candidates.Count==0&&selected.Track.Kind==TrackKind.Video&&copy.Media.Single(m=>m.Id==selected.Clip.MediaId) is {Kind:MediaKind.Video,HasAudio:true}){var target=sequence.Tracks.FirstOrDefault(t=>t.Kind==TrackKind.Audio&&!t.Locked);if(target is null){target=new EditTrack{Kind=TrackKind.Audio,Name="Audio "+(sequence.Tracks.Count(t=>t.Kind==TrackKind.Audio)+1)};sequence.Tracks.Add(target);}var audio=ProjectValidation.Clone(selected.Clip);audio.Id=Guid.NewGuid();audio.Name+=" · audio";audio.StreamIntent=ClipStreamIntent.AudioOnly;audio.Effects.RemoveAll(e=>e.Id!="volume");target.Clips.Add(audio);candidates.Add((target,audio));}
  if(candidates.Count!=1)throw new InvalidOperationException("No unique unlinked source partner was found. Keep the matching source audio/video clip in this sequence.");var partner=candidates[0];Writable([selected,partner]);var link=Guid.NewGuid();partner.Clip.Start=selected.Clip.Start;partner.Clip.SourceIn=selected.Clip.SourceIn;partner.Clip.Duration=selected.Clip.Duration;partner.Clip.Speed=selected.Clip.Speed;partner.Clip.Reverse=selected.Clip.Reverse;
  foreach(var member in new[]{selected,partner}){member.Clip.LinkId=member.Clip.SourcePairId=link;member.Clip.StreamIntent=member.Track.Kind==TrackKind.Audio?ClipStreamIntent.AudioOnly:ClipStreamIntent.VideoOnly;}
 });
}
