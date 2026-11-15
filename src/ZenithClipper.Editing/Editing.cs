using System.Text.Json;
namespace ZenithClipper.Editing;
public static class ProjectValidation
{
 public static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
 private static void ValidateRate(FrameRate fps)
 {
  fps.Validate();
  if(fps.Numerator>1000000||fps.Denominator>1000000||fps.Value<0.1||fps.Value>1000)throw new InvalidDataException("Frame rate must be between 0.1 and 1000 fps with bounded rational components.");
 }
 public static void Validate(EditProject p)
 {
  ArgumentNullException.ThrowIfNull(p);
  if(p.Version!=1)throw new InvalidDataException("Unsupported project version.");
  if(p.Id==Guid.Empty || p.Media is null || p.Sequences is null || p.Name is null)throw new InvalidDataException("Incomplete project.");
  var ids=new HashSet<Guid>();
  void Id(Guid id){if(id==Guid.Empty||!ids.Add(id))throw new InvalidDataException("Invalid or duplicate identifier.");}
  Id(p.Id);
  foreach(var m in p.Media){Id(m.Id);ValidateRate(m.Fps);if(m.Width<0||m.Height<0||m.Width>65536||m.Height>65536)throw new InvalidDataException("Invalid media dimensions.");if(m.Path is null||m.Name is null||!Enum.IsDefined(m.Kind)||!double.IsFinite(m.DurationSeconds)||m.DurationSeconds<0)throw new InvalidDataException("Invalid media.");}
  if(p.Sequences.Count==0||!p.Sequences.Any(s=>s.Id==p.ActiveSequenceId))throw new InvalidDataException("Active sequence is missing.");
  foreach(var m in p.Media.Where(m=>m.SubclipIn is not null||m.SubclipOut is not null))
   if(m.SubclipIn is not {} a||m.SubclipOut is not {} b||!double.IsFinite(a)||!double.IsFinite(b)||a<0||b<=a||(m.DurationSeconds>0&&b>m.DurationSeconds+1e-6))throw new InvalidDataException($"Subclip {m.Name} has an invalid range.");
  // Nested sequences: the target must exist, its length tracks the sequence, and no sequence may contain itself.
  foreach(var m in p.Media.Where(m=>m.Kind==MediaKind.Sequence))
  {
   var target=p.Sequences.FirstOrDefault(x=>x.Id==m.SequenceId)??throw new InvalidDataException("A nested sequence is missing.");
   m.DurationSeconds=target.Fps.Seconds(target.Duration);m.Width=target.Width;m.Height=target.Height;m.Fps=target.Fps;m.HasAudio=true;
  }
  var nestedIn=p.Sequences.ToDictionary(x=>x.Id,x=>x.Tracks.SelectMany(t=>t.Clips).Select(c=>p.Media.FirstOrDefault(m=>m.Id==c.MediaId)).Where(m=>m?.Kind==MediaKind.Sequence).Select(m=>m!.SequenceId!.Value).Distinct().ToList());
  var state=new Dictionary<Guid,int>();
  void Visit(Guid id){if(state.GetValueOrDefault(id)==1)throw new InvalidDataException("A sequence can't contain itself.");if(state.GetValueOrDefault(id)==2)return;state[id]=1;foreach(var n in nestedIn.GetValueOrDefault(id)??[])Visit(n);state[id]=2;}
  foreach(var x in p.Sequences)Visit(x.Id);
  foreach(var s in p.Sequences)
  {
   Id(s.Id);ValidateRate(s.Fps);if(s.Width<2||s.Height<2||s.Width>16384||s.Height>16384||s.Width%2!=0||s.Height%2!=0||(long)s.Width*s.Height>67108864||s.Tracks is null||s.Markers is null)throw new InvalidDataException("Invalid sequence.");
   foreach(var t in s.Tracks){Id(t.Id);if(!Enum.IsDefined(t.Kind)||!double.IsFinite(t.GainDb)||t.Clips is null)throw new InvalidDataException("Invalid track.");foreach(var c in t.Clips){Id(c.Id);ValidateClip(p,s,c);}}
   foreach(var m in s.Markers){Id(m.Id);if(m.Frame<0||m.Frame>int.MaxValue)throw new InvalidDataException("Invalid marker.");}
  }
 }
 public static void ValidateClip(EditProject p,EditSequence s,TimelineClip c)
 {
  foreach(var tx in new[]{c.TransitionIn,c.TransitionOut})if(tx is not null&&(TransitionCatalog.Find(tx.Kind) is null||tx.Duration<1||tx.Duration>c.Duration*2+1||tx.Duration>int.MaxValue))throw new InvalidDataException("Invalid transition.");
  if(c.HoldFrame is {} hold&&(hold<0||hold>int.MaxValue))throw new InvalidDataException("Invalid frame hold.");
  if(c.Label is {} label&&(label.Length!=7||label[0]!='#'))throw new InvalidDataException("Invalid label color.");
  if(c.BlendMode is {} blend&&!ClipBlend.Modes.Contains(blend))throw new InvalidDataException("Unknown blend mode.");
  if(c.Motion.Count>4096||c.Motion.Any(k=>!ClipBlend.MotionParameters.Contains(k.Parameter)||k.Frame<0||k.Frame>int.MaxValue||!double.IsFinite(k.Value)||!ClipBlend.Interpolations.Contains(k.Interpolation)))throw new InvalidDataException("Invalid keyframe.");
  if(c.Mask is {} mask&&(mask.Shape is not ("ellipse" or "rectangle" or "path")||mask.Points.Count%2!=0||mask.Track.Count%3!=0||mask.Track.Count>300000||mask.Track.Any(v=>!double.IsFinite(v))||mask.Points.Count>2000||mask.Points.Any(v=>!double.IsFinite(v)||v<-1||v>2)||new[]{mask.X,mask.Y,mask.Width,mask.Height,mask.Feather}.Any(v=>!double.IsFinite(v)||v<0||v>1)))throw new InvalidDataException("Invalid mask.");
  if(!Enum.IsDefined(c.StreamIntent)||c.Start<0||c.Start>int.MaxValue||c.SourceIn<0||c.SourceIn>int.MaxValue||c.Duration<=0||c.Duration>int.MaxValue||c.Start>int.MaxValue-c.Duration||!double.IsFinite(c.Speed)||c.Speed<=0||!double.IsFinite(c.GainDb)||!double.IsFinite(c.Opacity)||c.Opacity<0||c.Opacity>1||c.Transform is null||c.Effects is null)throw new InvalidDataException("Invalid clip bounds or properties.");
  var tr=c.Transform;
  if(new[]{tr.CropLeft,tr.CropRight,tr.CropTop,tr.CropBottom}.Any(v=>!double.IsFinite(v)||v<0||v>.99)||tr.CropLeft+tr.CropRight>.99||tr.CropTop+tr.CropBottom>.99)throw new InvalidDataException("Crop must leave at least 1% of the frame.");
  if(new[]{tr.X,tr.Y,tr.ScaleX,tr.ScaleY,tr.Rotation}.Any(v=>!double.IsFinite(v))||Math.Abs(tr.X)>100||Math.Abs(tr.Y)>100||tr.ScaleX<=0||tr.ScaleY<=0||tr.ScaleX>100||tr.ScaleY>100||Math.Abs(tr.Rotation)>360000)throw new InvalidDataException("Invalid transform.");
  var media=p.Media.SingleOrDefault(m=>m.Id==c.MediaId)??throw new InvalidDataException("Clip media is missing.");
  // Candidate clips (edits in progress) aren't in a track yet; find the track by id, if any.
  var owner=s.Tracks.FirstOrDefault(t=>t.Clips.Any(x=>x.Id==c.Id));
  if(media.Kind==MediaKind.Adjustment&&owner is {Kind:not TrackKind.Video})throw new InvalidDataException("Adjustment layers go on video tracks.");
  if(c.MulticamAngle is {} angle&&(media.Kind!=MediaKind.Sequence||angle<1||angle>p.Sequences.First(x=>x.Id==media.SequenceId).Tracks.Count(t=>t.Kind==TrackKind.Video)))throw new InvalidDataException("That camera angle doesn't exist in the multicam sequence.");
  if(media.Kind==MediaKind.Sequence&&(Math.Abs(c.Speed-1)>1e-9||c.Reverse||c.HoldFrame is not null))throw new InvalidDataException("Nested sequences play at normal speed; change speed inside the nested sequence.");
  bool remap=TimeRemap.Has(c);
  if(remap&&(media.Kind!=MediaKind.Video||owner is {Kind:not TrackKind.Video}||Math.Abs(c.Speed-1)>1e-9||c.Reverse||c.HoldFrame is not null||c.LinkId is not null||c.Motion.Any(k=>k.Parameter=="speed"&&(k.Value<TimeRemap.Min||k.Value>TimeRemap.Max))))
   throw new InvalidDataException("Time remapping works on unlinked video clips at 100% speed, not reversed or held, between 10% and 2000%.");
  double end=c.SourceIn+(remap?TimeRemap.Consumed(c,c.Duration):c.Duration*c.Speed);
  if(!double.IsFinite(end)||end>int.MaxValue)throw new InvalidDataException("Source bounds overflow.");
  if(media.Kind is MediaKind.Audio or MediaKind.Video && media.DurationSeconds>0 && end>s.Fps.Frames(media.DurationSeconds)+0.5)throw new InvalidDataException("Clip exceeds source duration.");
  foreach(var e in c.Effects){if(e.Id is null||e.Parameters is null||e.Keyframes is null)throw new InvalidDataException("Invalid effect.");foreach(var k in e.Keyframes)if(k.Frame<0||!double.IsFinite(k.Value))throw new InvalidDataException("Invalid keyframe.");}
 }
}
public sealed class EditorSession
{
 private EditProject current;
 private readonly List<(string Label,EditProject Before,EditProject After)> undo=[];
 private readonly Stack<(string Label,EditProject Before,EditProject After)> redo=[];
 private string saved;
 public EditorSession(EditProject project){ProjectValidation.Validate(project);current=ProjectValidation.Clone(project);saved=JsonSerializer.Serialize(current);}
 public EditProject Project=>ProjectValidation.Clone(current);
 /// <summary>Cheap read of the active sequence's monitor guides (copies only the guides, not the project).</summary>
 public List<MonitorGuide> ActiveGuides=>current.Sequences.First(s=>s.Id==current.ActiveSequenceId).Guides.Select(g=>new MonitorGuide{Vertical=g.Vertical,Position=g.Position}).ToList();
 /// <summary>Changes identity whenever the project changes (edit, undo, redo); for caching derived values.</summary>
 public object Version=>current;
 /// <summary>The open sequence; switching sequences is not an edit, so it is not part of <see cref="Version"/>.</summary>
 public Guid ActiveSequenceId=>current.ActiveSequenceId;
 public bool CanUndo=>undo.Count>0;
 public bool CanRedo=>redo.Count>0;
 public bool Dirty=>JsonSerializer.Serialize(current)!=saved;
 public string? UndoLabel=>CanUndo?undo[^1].Label:null;
 /// <summary>History panel: applied steps oldest first, and steps that can be redone (next first).</summary>
 public IReadOnlyList<string> UndoLabels=>undo.Select(x=>x.Label).ToList();
 public IReadOnlyList<string> RedoLabels=>redo.Select(x=>x.Label).ToList();
 public string? RedoLabel=>CanRedo?redo.Peek().Label:null;
 public void Execute(string label,Action<EditProject> edit)
 {
  ArgumentNullException.ThrowIfNull(edit);
  var next=ProjectValidation.Clone(current);edit(next);ProjectValidation.Validate(next);
  foreach(var s in current.Sequences)foreach(var t in s.Tracks.Where(t=>t.Locked))
  {
   var nextTrack=next.Sequences.FirstOrDefault(x=>x.Id==s.Id)?.Tracks.FirstOrDefault(x=>x.Id==t.Id);
   if(nextTrack is null)throw new InvalidOperationException("Track is locked.");
   // Locking protects clip content, track identity and type; mixer and visibility controls remain available.
   if(t.Id!=nextTrack.Id||t.Kind!=nextTrack.Kind||JsonSerializer.Serialize(t.Clips)!=JsonSerializer.Serialize(nextTrack.Clips))throw new InvalidOperationException("Track is locked.");
  }
  if(JsonSerializer.Serialize(current)==JsonSerializer.Serialize(next))return;
  var before=current;current=ProjectValidation.Clone(next);undo.Add((label,before,current));if(undo.Count>100)undo.RemoveAt(0);redo.Clear();
 }
 /// <summary>Switches the open sequence. Not an edit, so it isn't added to undo history.</summary>
 public void OpenSequence(Guid id){if(!current.Sequences.Any(s=>s.Id==id))throw new InvalidOperationException("Sequence not found.");current.ActiveSequenceId=id;}
 public void Undo(){if(!CanUndo)return;var entry=undo[^1];undo.RemoveAt(undo.Count-1);current=entry.Before;redo.Push(entry);}
 public void Redo(){if(!CanRedo)return;var entry=redo.Pop();current=entry.After;undo.Add(entry);}
 public void MarkSaved()=>saved=JsonSerializer.Serialize(current);
 public void MarkUnsaved()=>saved=string.Empty;
}
public static partial class EditCommands
{
 public static Guid Import(EditProject p,MediaAsset media)
 {
  var asset=ProjectValidation.Clone(media);if(p.Media.Any(m=>m.Id==asset.Id))throw new ArgumentException("Asset already imported.");
  var candidate=ProjectValidation.Clone(p);candidate.Media.Add(asset);ProjectValidation.Validate(candidate);p.Media.Add(asset);return asset.Id;
 }
 public static Guid AddTrack(EditProject p,Guid sequenceId,TrackKind kind,string name)
 {
  if(!Enum.IsDefined(kind)||string.IsNullOrWhiteSpace(name))throw new ArgumentException("Track kind and name are required.");var track=new EditTrack{Kind=kind,Name=name};Sequence(p,sequenceId).Tracks.Add(track);return track.Id;
 }
 public static Guid AddClip(EditProject p,Guid sequenceId,Guid trackId,TimelineClip clip)
 {
  var track=Track(p,sequenceId,trackId);var copy=ProjectValidation.Clone(clip);if(p.Sequences.SelectMany(s=>s.Tracks).SelectMany(t=>t.Clips).Any(c=>c.Id==copy.Id))throw new ArgumentException("Clip already exists.");Check(p,sequenceId,copy);track.Clips.Add(copy);return copy.Id;
 }
 public static void ChangeSpeed(EditProject p,Guid s,Guid t,Guid c,double speed,bool preserveSource=true)
 {
  if(!double.IsFinite(speed)||speed<=0)throw new ArgumentException("Speed must be positive.");ChangeLinkedClip(p,s,c,clip=>{long duration=preserveSource?Source(clip.Duration*clip.Speed/speed):clip.Duration;clip.Speed=speed;clip.Duration=duration;});
 }
 public static Guid Link(EditProject p,Guid sequenceId,IEnumerable<Guid> clipIds)
 {
  var ids=clipIds.Distinct().ToHashSet();if(ids.Count<2)throw new ArgumentException("Select at least two clips.");var found=new List<TimelineClip>();foreach(var track in Sequence(p,sequenceId).Tracks){var clips=track.Clips.Where(c=>ids.Contains(c.Id)).ToList();if(clips.Count>0&&track.Locked)throw new InvalidOperationException("Track is locked.");found.AddRange(clips);}if(found.Count!=ids.Count)throw new ArgumentException("Clip missing from sequence.");var link=Guid.NewGuid();foreach(var clip in found)clip.LinkId=link;return link;
 }
 private static EditSequence Sequence(EditProject p,Guid s)=>p.Sequences.Single(x=>x.Id==s);
 private static EditTrack Track(EditProject p,Guid s,Guid t){var track=Sequence(p,s).Tracks.Single(x=>x.Id==t);if(track.Locked)throw new InvalidOperationException("Track is locked.");return track;}
 private static TimelineClip Clip(EditTrack t,Guid c)=>t.Clips.Single(x=>x.Id==c);
 private static long Source(double value)=>checked((long)Math.Round(value,MidpointRounding.AwayFromZero));
 /// <summary>Source frames covered by <paramref name="frames"/> timeline frames from the clip start (time remapping aware).</summary>
 internal static long Advance(TimelineClip clip,long frames)=>TimeRemap.Has(clip)?Source(TimeRemap.Consumed(clip,frames)):Source(frames*clip.Speed);
 private static void Check(EditProject p,Guid s,TimelineClip c)=>ProjectValidation.ValidateClip(p,Sequence(p,s),c);
 /// <summary>Re-bases a clip's keyframes onto a new window of it (offset = new start minus old start), keeping the values at the cut edges.</summary>
 internal static List<Keyframe> Rebase(TimelineClip clip,long offset,long duration)
 {
  if(clip.Motion.Count==0)return [];
  var result=new List<Keyframe>();
  foreach(var param in clip.Motion.Select(k=>k.Parameter).Distinct())
  {
   var inside=clip.Motion.Where(k=>k.Parameter==param&&k.Frame-offset>=0&&k.Frame-offset<=duration).OrderBy(k=>k.Frame).Select(k=>new Keyframe{Parameter=param,Frame=k.Frame-offset,Value=k.Value,Interpolation=k.Interpolation}).ToList();
   if(!inside.Any(k=>k.Frame==0))inside.Insert(0,new Keyframe{Parameter=param,Frame=0,Value=ClipBlend.At(clip,param,offset)!.Value});
   if(!inside.Any(k=>k.Frame==duration))inside.Add(new Keyframe{Parameter=param,Frame=duration,Value=ClipBlend.At(clip,param,offset+duration)!.Value});
   result.AddRange(inside);
  }
  return result;
 }
 internal static void SplitSingle(EditProject p,Guid s,Guid t,Guid c,long frame)
 {
  var track=Track(p,s,t);var clip=Clip(track,c);if(frame<=clip.Start||frame>=clip.End)throw new ArgumentException("Split must be inside clip.");
  var left=ProjectValidation.Clone(clip);var right=ProjectValidation.Clone(clip);right.Id=Guid.NewGuid();left.Duration=frame-clip.Start;right.Start=frame;right.Duration=clip.End-frame;left.TransitionOut=null;right.TransitionIn=null;
  if(clip.Reverse)left.SourceIn=checked(clip.SourceIn+Source(right.Duration*clip.Speed));else right.SourceIn=checked(clip.SourceIn+Advance(clip,left.Duration));
  left.Motion=Rebase(clip,0,left.Duration);right.Motion=Rebase(clip,left.Duration,right.Duration);
  Check(p,s,left);Check(p,s,right);clip.Duration=left.Duration;clip.SourceIn=left.SourceIn;clip.TransitionOut=null;clip.Motion=left.Motion;track.Clips.Insert(track.Clips.IndexOf(clip)+1,right);
 }
 internal static void DeleteSingle(EditProject p,Guid s,Guid t,Guid c,bool ripple=false)
 {
  var track=Track(p,s,t);var clip=Clip(track,c);var end=clip.End;track.Clips.Remove(clip);if(ripple)foreach(var next in track.Clips.Where(x=>x.Start>=end))next.Start-=clip.Duration;
 }
 internal static void MoveSingle(EditProject p,Guid s,Guid t,Guid c,Guid targetTrack,long start)
 {
  var from=Track(p,s,t);var target=Track(p,s,targetTrack);if(from.Kind!=target.Kind)throw new ArgumentException("Track kind must match.");var clip=Clip(from,c);var candidate=ProjectValidation.Clone(clip);candidate.Start=start;Check(p,s,candidate);clip.Start=start;if(from!=target){from.Clips.Remove(clip);target.Clips.Add(clip);}
 }
 // Trim keeps the source/timeline mapping fixed; extending needs available source handles.
 internal static void TrimSingle(EditProject p,Guid s,Guid t,Guid c,long start,long duration)
 {
  var clip=Clip(Track(p,s,t),c);var candidate=ProjectValidation.Clone(clip);candidate.Start=start;candidate.Duration=duration;
  candidate.SourceIn=clip.Reverse?checked(clip.SourceIn+Source((clip.End-checked(start+duration))*clip.Speed)):checked(clip.SourceIn+Advance(clip,start-clip.Start));
  candidate.Motion=Rebase(clip,start-clip.Start,duration);
  Check(p,s,candidate);clip.Start=start;clip.Duration=duration;clip.SourceIn=candidate.SourceIn;clip.Motion=candidate.Motion;
 }
 public static void Slip(EditProject p,Guid s,Guid t,Guid c,long delta)
 {
  var clip=Clip(Track(p,s,t),c);var candidate=ProjectValidation.Clone(clip);candidate.SourceIn=checked(clip.SourceIn+delta);Check(p,s,candidate);clip.SourceIn=candidate.SourceIn;
 }
 public static void Roll(EditProject p,Guid s,Guid t,Guid c,Guid next,long delta)
 {
  var track=Track(p,s,t);var left=Clip(track,c);var right=Clip(track,next);if(left.End!=right.Start||left.Id==right.Id)throw new ArgumentException("Roll requires adjacent clips.");
  var a=ProjectValidation.Clone(left);var b=ProjectValidation.Clone(right);a.Duration=checked(a.Duration+delta);b.Start=checked(b.Start+delta);b.Duration=checked(b.Duration-delta);
  if(a.Reverse)a.SourceIn=checked(a.SourceIn-Source(delta*a.Speed));if(!b.Reverse)b.SourceIn=checked(b.SourceIn+Advance(right,delta));
  a.Motion=Rebase(left,0,a.Duration);b.Motion=Rebase(right,delta,b.Duration);Check(p,s,a);Check(p,s,b);
  left.Duration=a.Duration;left.SourceIn=a.SourceIn;right.Start=b.Start;right.Duration=b.Duration;right.SourceIn=b.SourceIn;left.Motion=a.Motion;right.Motion=b.Motion;
 }
 public static void RateStretch(EditProject p,Guid s,Guid t,Guid c,long duration)
 {
  var clip=Clip(Track(p,s,t),c);if(duration<=0)throw new ArgumentException("Duration must be positive.");if(TimeRemap.Has(clip))throw new InvalidOperationException("Rate stretch isn't available on time-remapped clips. Change the speed keyframes instead.");var candidate=ProjectValidation.Clone(clip);candidate.Speed=clip.Speed*clip.Duration/duration;candidate.Duration=duration;
  foreach(var k in candidate.Motion)k.Frame=Math.Min(duration,(long)Math.Round(k.Frame*(double)duration/clip.Duration));
  Check(p,s,candidate);clip.Speed=candidate.Speed;clip.Duration=duration;clip.Motion=candidate.Motion;
 }
}
public static class ProjectStore
{
 private static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
 public static void Save(string path,EditProject p)
 {
  ProjectValidation.Validate(p);var full=Path.GetFullPath(path);var comparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal; if(p.Media.Where(m=>!string.IsNullOrWhiteSpace(m.Path)).Any(m=>comparer.Equals(Path.GetFullPath(m.Path),full)))throw new InvalidOperationException("A project cannot overwrite an imported source.");var folder=Path.GetDirectoryName(full)!;Directory.CreateDirectory(folder);var temp=Path.Combine(folder,"."+Path.GetFileName(full)+"."+Guid.NewGuid()+".tmp");
  try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(file,p,Options);file.Flush(true);}File.Move(temp,full,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public static EditProject Load(string path)
 {
  try{using var file=File.OpenRead(path);var p=JsonSerializer.Deserialize<EditProject>(file)??throw new InvalidDataException("Empty project.");ProjectValidation.Validate(p);return p;}
  catch(JsonException e){throw new InvalidDataException("Invalid project JSON.",e);}
 }
 public static string AutosavePath(string path)=>path+".autosave";
 public static void SaveAutosave(string path,EditProject p)=>Save(AutosavePath(path),p);
 public static EditProject? LoadRecovery(string path)=>File.Exists(AutosavePath(path))?Load(AutosavePath(path)):null;
 public static IReadOnlyList<MediaAsset> MissingMedia(EditProject p)=>p.Media.Where(m=>m.Kind is MediaKind.Video or MediaKind.Audio or MediaKind.Image).Where(m=>!File.Exists(m.Path)).Select(ProjectValidation.Clone).ToList();
}






