using System.Text.Json.Serialization;
namespace ZenithClipper.Editing;
public readonly record struct FrameRate(int Numerator,int Denominator)
{
 public static FrameRate Default => new(30,1);
 public double Value { get { Validate(); return (double)Numerator/Denominator; } }
 public void Validate(){if(Numerator<=0||Denominator<=0)throw new ArgumentException("Frame rate must be positive.");}
 public long Frames(double seconds) { if(!double.IsFinite(seconds)) throw new ArgumentException("Time must be finite."); return checked((long)Math.Round(seconds*Value,MidpointRounding.AwayFromZero)); }
 public double Seconds(long frames)=>frames/Value;
}
public enum TrackKind { Video, Audio }
public enum MediaKind { Video, Audio, Image, Color, Text, Sequence, Adjustment }
public enum ClipStreamIntent { Auto, VideoOnly, AudioOnly }
public sealed class EditProject
{
 public int Version {get;set;}=1;
 public Guid Id {get;set;}=Guid.NewGuid();
 public string Name {get;set;}="Untitled";
 public Guid ActiveSequenceId {get;set;}
 public List<MediaAsset> Media {get;set;}=[];
 public List<EditSequence> Sequences {get;set;}=[];
}
public sealed class EditSequence
{
 public Guid Id {get;set;}=Guid.NewGuid();
 public string Name {get;set;}="Sequence 1";
 public int Width {get;set;}=1920;
 public int Height {get;set;}=1080;
 public FrameRate Fps {get;set;}=FrameRate.Default;
 public List<EditTrack> Tracks {get;set;}=[];
 public List<SequenceMarker> Markers {get;set;}=[];
 /// <summary>Rendered preview files (Premiere's render bar). A range only plays from its file while its key still matches.</summary>
 public List<PreviewRender> Previews {get;set;}=[];
 /// <summary>Program monitor guides: vertical ones at a fraction of the width, horizontal at a fraction of the height.</summary>
 public List<MonitorGuide> Guides {get;set;}=[];
 [JsonIgnore] public long Duration=>Tracks.SelectMany(t=>t.Clips).Select(c=>c.End).DefaultIfEmpty().Max();
}
public sealed class MonitorGuide
{
 public bool Vertical {get;set;}
 public double Position {get;set;}
}
public sealed class PreviewRender
{
 public long From {get;set;}
 public long To {get;set;}
 public string Key {get;set;}="";
 public string Path {get;set;}="";
}
public sealed class EditTrack
{
 public Guid Id {get;set;}=Guid.NewGuid();
 public string Name {get;set;}="Video 1";
 public TrackKind Kind {get;set;}
 public bool Locked {get;set;}
 public bool Muted {get;set;}
 /// <summary>Solo: while any audio track is soloed, only soloed tracks are heard.</summary>
 public bool Solo {get;set;}
 public bool Hidden {get;set;}
 /// <summary>Sync lock (Premiere, on by default): ripple deletes elsewhere shift this track too, keeping it in sync.</summary>
 public bool SyncLock {get;set;}=true;
 public double GainDb {get;set;}
 /// <summary>Track pan, -1 (left) to 1 (right).</summary>
 public double Pan {get;set;}
 public List<TimelineClip> Clips {get;set;}=[];
}
public sealed class MediaAsset
{
 public Guid Id {get;set;}=Guid.NewGuid();
 public string Path {get;set;}="";
 public string Name {get;set;}="Media";
 public MediaKind Kind {get;set;}
 public double DurationSeconds {get;set;}
 public int Width {get;set;}
 public int Height {get;set;}
 public FrameRate Fps {get;set;}=FrameRate.Default;
 public bool HasAudio {get;set;}
 public string? Text {get;set;}
 public string? Color {get;set;}
 /// <summary>Proxy: a small edit-friendly copy used for preview when proxies are on; export always uses Path.</summary>
 public string? ProxyPath {get;set;}
 public int ProxyWidth {get;set;}
 public int ProxyHeight {get;set;}
 /// <summary>Project panel bin (folder) this item is filed in; null = unsorted.</summary>
 public string? Bin {get;set;}
 /// <summary>Interpret Footage: play the video as if it were shot at this frame rate.</summary>
 public double? InterpretFps {get;set;}
 /// <summary>Interpret Footage field order: null uses the file's own flag; "progressive", "upper" (top field first) or "lower".</summary>
 public string? FieldOrder {get;set;}
 /// <summary>Interpret Footage alpha: null as the file says, "ignore", "invert" or "premultiplied".</summary>
 public string? AlphaMode {get;set;}
 /// <summary>Subclip (Premiere Ctrl+U): this bin item plays only [SubclipIn, SubclipOut) seconds of its file.</summary>
 public double? SubclipIn {get;set;}
 public double? SubclipOut {get;set;}
 /// <summary>For MediaKind.Text: the style it was drawn with, so it can be redrawn (find/replace, restyle).</summary>
 public double? FontSize {get;set;}
 public string? Background {get;set;}
 /// <summary>Essential Graphics style for text media (font, weight, alignment, tracking, leading, stroke, shadow) as JSON.</summary>
 public string? TextStyle {get;set;}
 /// <summary>For MediaKind.Sequence: the nested sequence this item plays.</summary>
 public Guid? SequenceId {get;set;}
}
public sealed class TimelineClip
{
 public Guid Id {get;set;}=Guid.NewGuid();
 public Guid MediaId {get;set;}
 public string Name {get;set;}="Clip";
 public long Start {get;set;}
 // SourceIn is the lower source bound in sequence-frame units, including for reverse clips.
 public long SourceIn {get;set;}
 public long Duration {get;set;}=1;
 public double Speed {get;set;}=1;
 public double GainDb {get;set;}
 public double Opacity {get;set;}=1;
 public bool Reverse {get;set;}
 /// <summary>Multi-camera: for a nested multicam sequence, which video track (1-based angle) this clip shows.</summary>
 public int? MulticamAngle {get;set;}
 /// <summary>Speed/Duration "Maintain audio pitch": time-stretch audio without changing its pitch.</summary>
 public bool MaintainPitch {get;set;}
 /// <summary>Premiere group (Ctrl+G): grouped clips select, move, nudge and delete together.</summary>
 public Guid? GroupId {get;set;}
 public Guid? LinkId {get;set;}
 public Guid? SourcePairId {get;set;}
 public ClipStreamIntent StreamIntent {get;set;}
 public ClipTransform Transform {get;set;}=new();
 public List<ClipEffect> Effects {get;set;}=[];
 public bool Enabled {get;set;}=true;
 /// <summary>Transition into this clip (from the clip that ends where this one starts, or from black/silence).</summary>
 public ClipTransition? TransitionIn {get;set;}
 /// <summary>Transition out of this clip when nothing follows it (fade to black/silence).</summary>
 public ClipTransition? TransitionOut {get;set;}
 /// <summary>Frame hold: when set, the clip shows this source frame (sequence-frame units) for its whole duration.</summary>
 public long? HoldFrame {get;set;}
 /// <summary>Premiere label color for the clip (null = default for its track kind).</summary>
 public string? Label {get;set;}
 /// <summary>Blend mode against the tracks below (null = Normal). See ClipBlend.Modes.</summary>
 public string? BlendMode {get;set;}
 /// <summary>Keyframes for motion, opacity and volume. Parameter: x, y, scale_x, scale_y, rotation, opacity, volume. Frame is relative to the clip start.</summary>
 public List<Keyframe> Motion {get;set;}=[];
 /// <summary>Optional shape mask (Premiere's opacity mask).</summary>
 public ClipMask? Mask {get;set;}
 [JsonIgnore] public long End=>checked(Start+Duration);
}
public sealed class ClipTransition
{
 /// <summary>See TransitionCatalog for kinds (cross_dissolve, dip_black, wipe_left, push_left, constant_power…).</summary>
 public string Kind {get;set;}="cross_dissolve";
 /// <summary>Length in sequence frames.</summary>
 public long Duration {get;set;}=30;
}
public sealed class ClipTransform
{
 // Normalized output-center offsets: (0,0) is center; X=1 is one output width to the right.
 public double X {get;set;}
 public double Y {get;set;}
 public double ScaleX {get;set;}=1;
 public double ScaleY {get;set;}=1;
 public double Rotation {get;set;}
 // Anchor point as a fraction of the clip's own frame; (0.5,0.5) is the centre. Position, scale and rotation pivot here.
 public double AnchorX {get;set;}=.5;
 public double AnchorY {get;set;}=.5;
 // Crop, as fractions of the clip's own frame cut from each edge. The cut area becomes transparent; nothing is rescaled.
 public double CropLeft {get;set;}
 public double CropRight {get;set;}
 public double CropTop {get;set;}
 public double CropBottom {get;set;}
 [System.Text.Json.Serialization.JsonIgnore] public bool HasCrop=>CropLeft>0||CropRight>0||CropTop>0||CropBottom>0;
}
public sealed class ClipEffect
{
 public string Id {get;set;}="";
 public bool Enabled {get;set;}=true;
 public Dictionary<string,string> Parameters {get;set;}=[];
 public List<Keyframe> Keyframes {get;set;}=[];
}
public sealed class Keyframe
{
 public string Parameter {get;set;}="";
 public long Frame {get;set;}
 public double Value {get;set;}
 public string Interpolation {get;set;}="linear";
}
public sealed class SequenceMarker
{
 public Guid Id {get;set;}=Guid.NewGuid();
 public long Frame {get;set;}
 public string Name {get;set;}="Marker";
 public string Color {get;set;}="#f6b94a";
 public string Notes {get;set;}="";
 public long Duration {get;set;}
}


public sealed class ClipMask
{
 public string Shape {get;set;}="ellipse";
 /// <summary>Centre and size as fractions of the frame.</summary>
 public double X {get;set;}=.5;
 public double Y {get;set;}=.5;
 public double Width {get;set;}=.5;
 public double Height {get;set;}=.5;
 /// <summary>Soft edge width as a fraction of the mask size.</summary>
 public double Feather {get;set;}=.1;
 public bool Invert {get;set;}
 /// <summary>Pen mask (Shape "path"): vertices as x0,y0,x1,y1… fractions of the clip's frame.</summary>
 public List<double> Points {get;set;}=[];
 /// <summary>Pen mask: round the corners into a smooth curve through the vertices.</summary>
 public bool Smooth {get;set;}
 /// <summary>Mask tracking: clip frame, centre X, centre Y (fractions of the clip frame) per tracked frame.</summary>
 public List<double> Track {get;set;}=[];
 /// <summary>The pen outline to fill: the vertices, or a Catmull-Rom curve through them when Smooth.</summary>
 public List<(double X,double Y)> Outline()
 {
  var v=new List<(double,double)>();for(int i=0;i+1<Points.Count;i+=2)v.Add((Points[i],Points[i+1]));
  if(!Smooth||v.Count<3)return v;
  var o=new List<(double,double)>();int n=v.Count;
  for(int i=0;i<n;i++)
  {
   var(p0x,p0y)=v[(i-1+n)%n];var(p1x,p1y)=v[i];var(p2x,p2y)=v[(i+1)%n];var(p3x,p3y)=v[(i+2)%n];
   for(int k=0;k<10;k++){double t=k/10.0,t2=t*t,t3=t2*t;
    o.Add((.5*(2*p1x+(-p0x+p2x)*t+(2*p0x-5*p1x+4*p2x-p3x)*t2+(-p0x+3*p1x-3*p2x+p3x)*t3),.5*(2*p1y+(-p0y+p2y)*t+(2*p0y-5*p1y+4*p2y-p3y)*t2+(-p0y+3*p1y-3*p2y+p3y)*t3)));}
  }
  return o;
 }
}
public static class ClipBlend
{
 public static readonly string[] Modes=["normal","add","multiply","screen","overlay","darken","lighten","colordodge","colorburn","hardlight","softlight","difference","exclusion","hslhue","hslsaturation","hslcolor","hslluminosity","saturate"];
 /// <summary>Keyframe interpolation, for the segment from a key to the next: Premiere's Linear, Hold, Bezier and Ease In/Out.</summary>
 public static readonly string[] Interpolations=["linear","discrete","smooth","ease_in","ease_out","ease_in_out"];
 /// <summary>Shapes 0..1 progress the way the engine does (MLT 7.22: cubic easing; smooth is a smoothstep stand-in for its spline).</summary>
 public static double Ease(string interpolation,double t)=>interpolation switch{"smooth"=>t*t*(3-2*t),"ease_in"=>t*t*t,"ease_out"=>1-Math.Pow(1-t,3),"ease_in_out"=>t<.5?4*t*t*t:1-Math.Pow(-2*t+2,3)/2,"discrete"=>0,_=>t};
 /// <summary>The MLT keyframe operator for an interpolation.</summary>
 public static string KeyOperator(string interpolation)=>interpolation switch{"discrete"=>"|=","smooth"=>"~=","ease_in"=>"g=","ease_out"=>"h=","ease_in_out"=>"i=",_=>"="};
 public static readonly string[] MotionParameters=["x","y","scale_x","scale_y","rotation","opacity","volume","speed"];
 /// <summary>Value of a keyframed parameter at a clip-relative frame (linear between keys, held outside them).</summary>
 public static double? At(TimelineClip clip,string parameter,long frame)
 {
  var keys=clip.Motion.Where(k=>k.Parameter==parameter).OrderBy(k=>k.Frame).ToList();
  if(keys.Count==0)return null;if(frame<=keys[0].Frame)return keys[0].Value;if(frame>=keys[^1].Frame)return keys[^1].Value;
  for(int i=1;i<keys.Count;i++)if(frame<=keys[i].Frame){var a=keys[i-1];var b=keys[i];if(a.Interpolation=="discrete")return frame==b.Frame?b.Value:a.Value;double t=(frame-a.Frame)/(double)Math.Max(1,b.Frame-a.Frame);return a.Value+(b.Value-a.Value)*Ease(a.Interpolation,t);}
  return keys[^1].Value;
 }
}
