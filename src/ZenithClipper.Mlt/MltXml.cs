using System.Globalization;
using System.Xml.Linq;
using ZenithClipper.Editing;
namespace ZenithClipper.Mlt;

public static class MltXml
{
    static string N(double value)=>value.ToString("0.########",CultureInfo.InvariantCulture);
    static XElement P(string name,object value)=>new("property",new XAttribute("name",name),value);
    public static EditSequence Sequence(EditProject project)=>project.Sequences.Single(x=>x.Id==project.ActiveSequenceId);
    public static string ProfileText(int width,int height,FrameRate fps)=>$"description=ZenithClipper\nframe_rate_num={fps.Numerator}\nframe_rate_den={fps.Denominator}\nwidth={width}\nheight={height}\nprogressive=1\nsample_aspect_num=1\nsample_aspect_den=1\ndisplay_aspect_num={width}\ndisplay_aspect_den={height}\ncolorspace=709\n";
    public static XDocument Compile(EditProject project,int width=0,int height=0,FrameRate? previewRate=null)
    {
        ProjectValidation.Validate(project);
        var seq=Sequence(project);var fps=previewRate??seq.Fps;width=width>0?width:seq.Width;height=height>0?height:seq.Height;
        var root=new XElement("mlt",new XAttribute("producer","timeline"),new XElement("profile",new XAttribute("description","ZenithClipper"),new XAttribute("width",width),new XAttribute("height",height),new XAttribute("frame_rate_num",fps.Numerator),new XAttribute("frame_rate_den",fps.Denominator),new XAttribute("progressive",1),new XAttribute("sample_aspect_num",1),new XAttribute("sample_aspect_den",1),new XAttribute("display_aspect_num",width),new XAttribute("display_aspect_den",height),new XAttribute("colorspace",709)));
        BuildSequence(root,project,seq,"",width,height,fps,0);
        // Structural guard: every MLT service element names exactly one service (a duplicate silently changes behaviour).
        foreach(var el in root.Descendants().Where(e=>e.Name.LocalName is "producer" or "filter" or "transition"))
            if(el.Elements("property").Count(x=>(string?)x.Attribute("name")=="mlt_service")!=1)throw new InvalidOperationException($"Internal error compiling {(string?)el.Attribute("id")??el.Name.LocalName}: expected one mlt_service.");
        return new XDocument(new XDeclaration("1.0","utf-8",null),root);
    }

    /// <summary>
    /// Compiles one sequence into a tractor added to <paramref name="root"/>. Nested sequences call this recursively with
    /// a unique id prefix; their empty areas are transparent so tracks below show through, as in Premiere.
    /// </summary>
    static XElement BuildSequence(XElement root,EditProject project,EditSequence seq,string prefix,int width,int height,FrameRate fps,int depth,int? angle=null)
    {
        if(depth>8)throw new ArgumentException("Sequences are nested too deeply.");
        long ConvertFrame(long frame)=>fps.Frames(seq.Fps.Seconds(frame));
        long length=Math.Max(1,ConvertFrame(seq.Duration));
        if(length>int.MaxValue)throw new ArgumentException("Sequence exceeds MLT's frame position range.");
        root.Add(new XElement("producer",new XAttribute("id",prefix+"black"),P("mlt_service","color"),P("resource",depth==0?"#000000":"#00000000"),P("length",length),P("eof","pause")));
        root.Add(new XElement("playlist",new XAttribute("id",prefix+"background"),new XElement("entry",new XAttribute("producer",prefix+"black"),new XAttribute("in",0),new XAttribute("out",length-1))));
        var tractor=new XElement("tractor",new XAttribute("id",prefix+"timeline"),new XAttribute("in",0),new XAttribute("out",length-1),new XElement("track",new XAttribute("producer",prefix+"background")));
        var transitions=new List<XElement>();int index=0;
        var plans=TransitionPlanner.Plan(project,seq,ConvertFrame,fps.Value);
        bool anySolo=seq.Tracks.Any(t=>t.Kind==TrackKind.Audio&&t.Solo);
        // Multicam: only the chosen camera's video track plays.
        var cameraTracks=seq.Tracks.Where(t=>t.Kind==TrackKind.Video).ToList();var shadowOf=new Dictionary<Guid,(Guid Source,ClipEffect Fx)>();
        foreach(var track in seq.Tracks.Where(t=>angle is null||t.Kind!=TrackKind.Video||cameraTracks.IndexOf(t)==angle-1))
        foreach(var clip in WithShadows(track,width,height,shadowOf))
        {
            var media=project.Media.Single(x=>x.Id==clip.MediaId);
            string id=prefix+"clip"+index,list=prefix+"track"+index;index++;
            long start=ConvertFrame(clip.Start),duration=Math.Max(1,ConvertFrame(clip.Duration));
            double speed=clip.Speed*(clip.Reverse?-1:1);
            long sourceIn=clip.Reverse?Math.Max(0,fps.Frames(media.DurationSeconds)-(long)Math.Ceiling(ConvertFrame(clip.SourceIn)+duration*clip.Speed)):ConvertFrame(clip.SourceIn);
            double rawFirst=sourceIn/clip.Speed;
            if(!double.IsFinite(rawFirst) || rawFirst<0 || rawFirst+duration-1>int.MaxValue || (media.Kind is MediaKind.Video or MediaKind.Audio && media.DurationSeconds*fps.Value/clip.Speed>int.MaxValue))throw new ArgumentException("This speed/source range exceeds MLT frame limits.");
            long first=checked((long)Math.Round(rawFirst,MidpointRounding.AwayFromZero));
            var plan=plans.GetValueOrDefault(shadowOf.TryGetValue(clip.Id,out var shadowSrc)?shadowSrc.Source:clip.Id)??new ClipPlan();
            // Transition handles: play extra source before/after the clip so it can overlap its neighbour.
            if(plan.HeadExt>0||plan.TailExt>0)
            {
                // The planner only extends into media that exists and never before 0:00.
                start-=plan.HeadExt;if(media.Kind is not (MediaKind.Image or MediaKind.Text or MediaKind.Color))first-=plan.HeadExt;duration+=plan.HeadExt+plan.TailExt;
            }
            if(media.Kind==MediaKind.Adjustment)
            {
                // Adjustment layer: its video effects are planted on the tractor at this point in the compositing
                // order, acting on track 0, which by now holds everything composited below it.
                if(track.Kind==TrackKind.Video&&!track.Hidden)
                foreach(var effect in clip.Effects.Where(e=>e.Enabled))
                {
                    EffectPolicy.Validate(effect);
                    if(EffectCatalog.Find(effect.Id) is not {Kind:EffectKind.Video} def)continue;
                    string Value(string key)=>effect.Parameters.TryGetValue(key,out var v)?v:def.Params.First(x=>x.Key==key).DefaultText;
                    if(def.Params.Any(x=>x.Type==ParamType.File&&string.IsNullOrEmpty(Value(x.Key))))continue;
                    foreach(var (service,properties) in def.Compile(Value)){var f=Filter(service,start,duration,properties);f.Add(P("track",0));transitions.Add(f);}
                }
                index--; // no tractor track was added for this clip
                continue;
            }
            var producer=new XElement("producer",new XAttribute("id",id));
            XElement? nested=null,remapped=null;
            if(media.Kind==MediaKind.Sequence)
            {
                // A nested sequence plays as its own tractor; this clip's filters attach to that tractor.
                var inner=project.Sequences.First(x=>x.Id==media.SequenceId);
                nested=BuildSequence(root,project,inner,prefix+"n"+index+"_",width,height,fps,depth+1,clip.MulticamAngle);
                id=(string)nested.Attribute("id")!;
            }
            else if(TimeRemap.Has(clip))
            {
                if(!File.Exists(media.Path))throw new FileNotFoundException("Relink missing media before playback/export.",media.Path);
                remapped=BuildRemap(root,id,clip,media,ConvertFrame,fps.Value/seq.Fps.Value);first=0;
            }
            else if(media.Kind==MediaKind.Color)
            {
                // Generated items (Premiere's New Item): color matte, black/transparent video, bars, gradient.
                string spec=media.Color??"#000000";long len=Math.Max(length,first+duration);
                if(spec=="bars")producer.Add(P("mlt_service","frei0r.test_pat_B"),P("0",0),P("length",len),P("out",len-1),P("eof","pause"));
                else if(spec.StartsWith("gradient:",StringComparison.Ordinal))
                {
                    var c=spec.Split(':');string a=c.Length>1?c[1]:"#000000",b=c.Length>2?c[2]:"#FFFFFF";
                    producer.Add(P("mlt_service","color"),P("resource",a),P("length",len),P("eof","pause"));
                    producer.Add(new XElement("filter",P("mlt_service","frei0r.cairogradient"),P("0","gradient_linear"),P("1",a),P("2",1),P("3",b),P("4",1),P("5",0.5),P("6",1),P("7",0.5),P("8",0),P("9",0),P("10","normal")));
                }
                else producer.Add(P("mlt_service","color"),P("resource",spec=="transparent"?"#00000000":spec),P("length",len),P("eof","pause"));
            }
            else
            {
                if(!File.Exists(media.Path))throw new FileNotFoundException("Relink missing media before playback/export.",media.Path);
                if(Math.Abs(speed-1)>.000001 && media.Kind is MediaKind.Video or MediaKind.Audio)
                {
                    producer.Add(P("mlt_service","timewarp"),P("resource",speed.ToString("G17",CultureInfo.InvariantCulture)+":"+Path.GetFullPath(media.Path)),P("warp_speed",speed.ToString("G17",CultureInfo.InvariantCulture)),P("warp_resource",Path.GetFullPath(media.Path)));
                    if(clip.MaintainPitch)producer.Add(P("warp_pitch",1));
                }
                else producer.Add(P("mlt_service","avformat"),P("resource",Path.GetFullPath(media.Path)));
                if(media.InterpretFps is {} forced&&media.Kind==MediaKind.Video)producer.Add(P("force_fps",forced));
                // Field order override; interlaced sources are then deinterlaced by the consumer for a progressive sequence.
                if(media.Kind==MediaKind.Video)switch(media.FieldOrder){case "progressive":producer.Add(P("force_progressive",1));break;case "upper":producer.Add(P("force_progressive",0),P("force_tff",1));break;case "lower":producer.Add(P("force_progressive",0),P("force_tff",0));break;}
                producer.Add(P("threads",2),P("eof",media.Kind is MediaKind.Image or MediaKind.Text?"loop":"pause"));
            }
            bool linkedAudio=clip.LinkId!=null && seq.Tracks.Where(t=>t.Kind==TrackKind.Audio).SelectMany(t=>t.Clips).Any(c=>c.Enabled && c.LinkId==clip.LinkId && c.MediaId==clip.MediaId);
            bool audioOff=track.Muted || (anySolo && !(track.Kind==TrackKind.Audio && track.Solo)) || clip.StreamIntent==ClipStreamIntent.VideoOnly || (track.Kind==TrackKind.Video && linkedAudio) || !media.HasAudio || TimeRemap.Has(clip);
            if(audioOff)producer.Add(P("audio_index",-1),P("set.test_audio",1));
            if(track.Kind==TrackKind.Audio || clip.StreamIntent==ClipStreamIntent.AudioOnly || track.Hidden)producer.Add(P("video_index",-1),P("set.test_image",1));
            // MLT mutes a one-frame playlist entry as a repeated still. Give an audible
            // one-frame AV cut an unused handle, then cut its track back to the logical end.
            bool audioHandle=duration==1 && !audioOff && media.Kind is MediaKind.Audio or MediaKind.Video;
            if(audioHandle)
            {
                long sourceLength=checked((long)Math.Ceiling(Math.Max(first+2,media.DurationSeconds*fps.Value/clip.Speed)));
                if(sourceLength>int.MaxValue)throw new ArgumentException("Single-frame audio handle exceeds MLT frame limits.");
                producer.Add(P("length",sourceLength),P("out",sourceLength-1));
            }
            // A keyframed volume replaces the clip's static volume (track volume still applies).
            double gain=(clip.Motion.Any(k=>k.Parameter=="volume")?0:clip.GainDb)+track.GainDb;
            // Interpret Footage alpha: ignore it, invert it, or treat it as premultiplied (unpremultiply so edges don't darken).
            if(track.Kind==TrackKind.Video&&media.AlphaMode is {} alpha&&media.Kind is MediaKind.Video or MediaKind.Image)
                switch(alpha)
                {
                    case "ignore":producer.Add(Filter("avfilter.lutrgb",first,duration,new(){["av.a"]="255"}));break;
                    case "invert":producer.Add(Filter("avfilter.lutrgb",first,duration,new(){["av.a"]="negval"}));break;
                    case "premultiplied":{static string U(string c)=>$"if(gt(alpha(X,Y),0),min(255,{c}(X,Y)*255/alpha(X,Y)),0)";producer.Add(Filter("avfilter.geq",first,duration,new(){["av.r"]=U("r"),["av.g"]=U("g"),["av.b"]=U("b"),["av.a"]="alpha(X,Y)"}));break;}
                }
            if(!audioOff && Math.Abs(gain)>.000001)producer.Add(Filter("volume",first,duration,new(){["level"]=N(gain)+"dB"}));
            var crop=clip.Transform;
            if(track.Kind==TrackKind.Video&&crop.HasCrop&&media.Kind is not MediaKind.Audio)
            {
                // Core "crop" cuts source pixels; the placement rect below shrinks by the same fractions, so the kept
                // region stays exactly where it was and the cut edges show the tracks underneath (Premiere's Crop effect).
                double sw=media.Width>0?media.Width:width,sh=media.Height>0?media.Height:height;
                producer.Add(Filter("crop",first,duration,new(){["left"]=N(Math.Round(crop.CropLeft*sw)),["right"]=N(Math.Round(crop.CropRight*sw)),["top"]=N(Math.Round(crop.CropTop*sh)),["bottom"]=N(Math.Round(crop.CropBottom*sh))}));
            }
            if(clip.HoldFrame is {} hold&&media.Kind is MediaKind.Video)
                producer.Add(Filter("freeze",first,duration,new(){["frame"]=N(Math.Round(ConvertFrame(hold)/clip.Speed)),["freeze_after"]="0",["freeze_before"]="0"}));
            // Keyframes are stored in clip frames from the clip start; filters count from the (possibly extended) start.
            long KeyAt(long clipFrame)=>ConvertFrame(clipFrame)+plan.HeadExt;
            string Anim(string parameter,Func<double,string> format)=>string.Join(';',clip.Motion.Where(k=>k.Parameter==parameter).OrderBy(k=>k.Frame).Select(k=>$"{KeyAt(k.Frame)}{ClipBlend.KeyOperator(k.Interpolation)}{format(k.Value)}"));
            bool opacityKeys=clip.Motion.Any(k=>k.Parameter=="opacity");
            if(opacityKeys&&track.Kind==TrackKind.Video)producer.Add(Filter("brightness",first,duration,new(){["level"]="1",["alpha"]=Anim("opacity",v=>N(Math.Clamp(v,0,1))),["threads"]="2"}));
            else if(plan.Alpha.Count>0)producer.Add(Filter("brightness",first,duration,new(){["level"]="1",["alpha"]=TransitionPlanner.Keys(plan.Alpha,v=>N(v*clip.Opacity)),["threads"]="2"}));
            else if(clip.Opacity<.999999)producer.Add(Filter("brightness",first,duration,new(){["level"]="1",["alpha"]=N(clip.Opacity),["threads"]="2"}));
            if(plan.Wipe is {} wipe&&track.Kind==TrackKind.Video)
                producer.Add(Filter("shape",first,duration,new(){["resource"]=TransitionPlanner.Pattern(wipe.Pattern),["mix"]=TransitionPlanner.Keys([(wipe.From,0),(wipe.To,100)],N),["softness"]="0.06",["use_luminance"]="1",["use_mix"]="1",["invert"]="0"}));
            if(clip.Motion.Any(k=>k.Parameter=="volume")&&!audioOff)producer.Add(Filter("volume",first,duration,new(){["level"]=Anim("volume",v=>N(Math.Clamp(v,-96,24)))}));
            if(track.Kind==TrackKind.Audio&&Math.Abs(track.Pan)>.001&&!audioOff)producer.Add(Filter("panner",first,duration,new(){["start"]=N(Math.Clamp((track.Pan+1)/2,0,1)),["split"]=N(Math.Clamp((track.Pan+1)/2,0,1)),["channel"]="-1"}));
            if(clip.Mask is {Shape:"ellipse" or "rectangle",Track.Count:>=3} tracked&&track.Kind==TrackKind.Video)
            {
                // Tracked mask: alphaspot follows the tracked centre (static luma masks can't move).
                string Path(int axis)=>string.Join(';',Enumerable.Range(0,tracked.Track.Count/3).Select(i=>$"{KeyAt((long)tracked.Track[i*3])}={N(Math.Clamp(tracked.Track[i*3+axis],0,1))}"));
                producer.Add(Filter("frei0r.alphaspot",first,duration,new(){["0"]=tracked.Shape=="ellipse"?"0.3":"0",["1"]=Path(1),["2"]=Path(2),["3"]=N(Math.Clamp(tracked.Width/2,0,1)),["4"]=N(Math.Clamp(tracked.Height/2,0,1)),["5"]="0.5",["6"]=N(Math.Clamp(tracked.Feather*.5,0.001,1)),["7"]=tracked.Invert?"1":"0",["8"]=tracked.Invert?"0":"1",["9"]="0.45"}));
            }
            else if(clip.Mask is {} mask&&track.Kind==TrackKind.Video&&(mask.Shape!="path"||mask.Points.Count>=6))
                producer.Add(Filter("shape",first,duration,new(){["resource"]=MaskPattern(mask,width,height),["mix"]="50",["softness"]=N(Math.Clamp(mask.Feather,0.01,1)*.5),["use_luminance"]="1",["use_mix"]="1",["invert"]="0"}));
            if(plan.Volume.Count>0&&!audioOff)
                producer.Add(Filter("volume",first,duration,new(){["level"]=string.Join(';',plan.Volume.OrderBy(k=>k.At).Select(k=>$"{Math.Max(0,k.At)}{(k.Smooth?"~=":"=")}{N(k.Db)}"))}));
            foreach(var effect in clip.Effects.Where(e=>e.Enabled))
            {
                EffectPolicy.Validate(effect);
                if(EffectCatalog.Find(effect.Id) is {} def)
                {
                    // Audio effects only on clips that carry sound; video effects only where there is a picture.
                    bool wantsAudio=def.Kind==EffectKind.Audio;
                    if(wantsAudio?audioOff:track.Kind==TrackKind.Audio||clip.StreamIntent==ClipStreamIntent.AudioOnly)continue;
                    string Value(string key)=>effect.Parameters.TryGetValue(key,out var v)?v:def.Params.First(x=>x.Key==key).DefaultText;
                    // An effect still waiting for its file (a LUT not chosen yet) simply does nothing.
                    if(def.Params.Any(x=>x.Type==ParamType.File&&string.IsNullOrEmpty(Value(x.Key))))continue;
                    foreach(var (service,properties) in def.Compile(Value))producer.Add(Filter(service,first,duration,properties));
                    continue;
                }
                if(string.IsNullOrWhiteSpace(effect.Id) || effect.Id.Any(char.IsWhiteSpace))throw new ArgumentException("Invalid effect service.");
                var parameters=new Dictionary<string,string>(effect.Parameters){["threads"]="2"};
                foreach(var channel in effect.Keyframes.GroupBy(k=>k.Parameter))
                    parameters[channel.Key]=string.Join(';',channel.OrderBy(k=>k.Frame).Select(k=>$"{ConvertFrame(k.Frame)}{ClipBlend.KeyOperator(k.Interpolation)}{N(k.Value)}"));
                producer.Add(Filter(effect.Id,first,duration,parameters));
            }
            if(shadowOf.TryGetValue(clip.Id,out var shadow))foreach(var (service,properties) in ShadowFilters(shadow.Fx))producer.Add(Filter(service,first,duration,properties));
            if(nested is not null)foreach(var f in producer.Elements("filter").ToList()){f.Remove();nested.Add(f);}
            else if(remapped is not null){foreach(var f in producer.Elements("filter").ToList()){f.Remove();remapped.Add(f);}root.Add(remapped);}
            else root.Add(producer);
            var playlist=new XElement("playlist",new XAttribute("id",list));if(start>0)playlist.Add(new XElement("blank",new XAttribute("length",start)));
            // Raster sources have one decodable frame. Repeat that frame in the playlist;
            // playlist filters retain timeline positions so still-image keyframes can animate.
            var entry=new XElement("entry",new XAttribute("producer",id),new XAttribute("in",media.Kind is MediaKind.Image or MediaKind.Text?0:first),new XAttribute("out",media.Kind is MediaKind.Image or MediaKind.Text?0:first+duration-1+(audioHandle?1:0)));
            if(media.Kind is MediaKind.Image or MediaKind.Text)
            {
                entry.Add(new XAttribute("repeat",duration));
                foreach(var filter in producer.Elements("filter").ToList())
                {
                    filter.Remove();filter.SetAttributeValue("in",start);filter.SetAttributeValue("out",start+duration-1);playlist.Add(filter);
                }
            }
            playlist.Add(entry);
            root.Add(playlist);var t=new XElement("track",new XAttribute("producer",list));
            if(audioHandle)t.Add(new XAttribute("in",0),new XAttribute("out",start+duration-1));
            if(track.Kind==TrackKind.Audio)t.Add(new XAttribute("hide",track.Muted?"both":"video"));
            else if(track.Hidden)t.Add(new XAttribute("hide",audioOff?"both":"video"));else if(audioOff)t.Add(new XAttribute("hide","audio"));
            tractor.Add(t);
            if(track.Kind==TrackKind.Video && !track.Hidden)
            {
                var transform=clip.Transform;var (x,y,w,h,_,_)=TransformGeometry.Place(width,height,transform.X,transform.Y,transform.ScaleX,transform.ScaleY,transform.Rotation,transform);
                bool motionKeys=clip.Motion.Any(k=>k.Parameter is "x" or "y" or "scale_x" or "scale_y" or "rotation");
                if(motionKeys||clip.BlendMode is {} mode&&mode!="normal")
                {
                    transitions.Add(CairoComposite(clip,plan,start,duration,width,height,ConvertFrame,index));
                    goto composited;
                }
                string rect=$"{N(x)} {N(y)} {N(w)} {N(h)} 1";
                if(plan.Motion.Count>0)
                    rect=string.Join(';',plan.Motion.OrderBy(k=>k.At).Select(k=>{double w2=w*k.Scale,h2=h*k.Scale;return $"{Math.Max(0,k.At)}={N(x+(w-w2)/2+k.Dx*width)} {N(y+(h-h2)/2+k.Dy*height)} {N(w2)} {N(h2)} 1";}));
                var blend=new XElement("transition",new XAttribute("in",start),new XAttribute("out",start+duration-1),P("mlt_service","affine"),P("a_track",0),P("b_track",index),P("distort",1),P("fill",1),P("rect",rect),P("fix_rotate_x",N(transform.Rotation)),P("always_active",1));
                transitions.Add(blend);
                composited:;
            }
            if(!audioOff)transitions.Add(new XElement("transition",new XAttribute("in",start),new XAttribute("out",start+duration-1),P("mlt_service","mix"),P("a_track",0),P("b_track",index),P("always_active",1),P("sum",1)));
            // Dip-to-color layers sit directly above the clip they belong to.
            if(track.Kind==TrackKind.Video&&!track.Hidden)
            foreach(var overlay in plan.Overlays)
            {
                string oid=prefix+"dip"+index;index++;long oStart=Math.Max(0,overlay.Start),oLen=Math.Max(1,overlay.Duration-(oStart-overlay.Start));
                root.Add(new XElement("producer",new XAttribute("id",oid),P("mlt_service","color"),P("resource",overlay.Color),P("length",oLen),P("eof","pause"),
                    Filter("brightness",0,oLen,new(){["level"]="1",["alpha"]=TransitionPlanner.Keys(overlay.Alpha.Select(k=>(k.At-(oStart-overlay.Start),k.Value)),N)})));
                var oList=new XElement("playlist",new XAttribute("id",oid+"list"));if(oStart>0)oList.Add(new XElement("blank",new XAttribute("length",oStart)));
                oList.Add(new XElement("entry",new XAttribute("producer",oid),new XAttribute("in",0),new XAttribute("out",oLen-1)));root.Add(oList);
                tractor.Add(new XElement("track",new XAttribute("producer",oid+"list"),new XAttribute("hide","audio")));
                transitions.Add(new XElement("transition",new XAttribute("in",oStart),new XAttribute("out",oStart+oLen-1),P("mlt_service","affine"),P("a_track",0),P("b_track",index),P("distort",1),P("fill",1),P("rect",$"0 0 {width} {height} 1"),P("always_active",1)));
            }
        }
        tractor.Add(transitions);root.Add(tractor);return tractor;
    }
    /// <summary>
    /// Time remapping: one constant-speed producer per distinct speed, and a playlist that plays each run of the clip
    /// from the right source position. Video only, like Premiere (the audio stays on its own, unremapped clip).
    /// </summary>
    static XElement BuildRemap(XElement root,string id,TimelineClip clip,MediaAsset media,Func<long,long> convert,double ratio)
    {
        var producers=new Dictionary<double,string>();var list=new XElement("playlist",new XAttribute("id",id));
        string path=Path.GetFullPath(media.Path);
        foreach(var run in TimeRemap.Runs(clip))
        {
            if(!producers.TryGetValue(run.Speed,out var pid))
            {
                pid=id+"_s"+producers.Count;producers[run.Speed]=pid;var prod=new XElement("producer",new XAttribute("id",pid));
                string sp=run.Speed.ToString("G17",CultureInfo.InvariantCulture);
                if(Math.Abs(run.Speed-1)>1e-6)prod.Add(P("mlt_service","timewarp"),P("resource",sp+":"+path),P("warp_speed",sp),P("warp_resource",path));
                else prod.Add(P("mlt_service","avformat"),P("resource",path));
                prod.Add(P("threads",2),P("eof","pause"),P("audio_index",-1),P("set.test_audio",1));root.Add(prod);
            }
            long len=Math.Max(1,convert(run.Start+run.Length)-convert(run.Start));
            long at=Math.Max(0,(long)Math.Round((clip.SourceIn+run.Source)*ratio/run.Speed));
            list.Add(new XElement("entry",new XAttribute("producer",pid),new XAttribute("in",at),new XAttribute("out",at+len-1)));
        }
        return list;
    }
    /// <summary>
    /// Keyframed motion and blend modes use frei0r's cairoaffineblend, which animates position, scale and rotation
    /// (about the clip centre) and composites with Premiere's blend modes. Its parameters are normalised: a centre at
    /// pixel cx is (cx/W+2)/5, a width w is (w/W)/5, rotation is degrees/360.
    /// </summary>
    /// <summary>The track's enabled clips in order; a clip with Drop Shadow is preceded by its shadow, a silhouette copy
    /// offset by the shadow distance, so the shadow composites just under the clip and can fall outside its frame.</summary>
    static IEnumerable<TimelineClip> WithShadows(EditTrack track,int width,int height,Dictionary<Guid,(Guid Source,ClipEffect Fx)> shadowOf)
    {
        foreach(var clip in track.Clips.Where(c=>c.Enabled).OrderBy(c=>c.Start))
        {
            if(track.Kind==TrackKind.Video&&!track.Hidden&&clip.Effects.FirstOrDefault(e=>e.Enabled&&e.Id=="drop_shadow") is {} fx)
            {
                double P(string k,double d)=>fx.Parameters.TryGetValue(k,out var v)&&double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out var x)?x:d;
                double dir=P("direction",135)*Math.PI/180,dist=P("distance",10);
                double dx=Math.Sin(dir)*dist/Math.Max(1,width),dy=-Math.Cos(dir)*dist/Math.Max(1,height);
                var copy=ProjectValidation.Clone(clip);copy.Id=Guid.NewGuid();copy.LinkId=null;copy.StreamIntent=ClipStreamIntent.VideoOnly;copy.BlendMode=null;
                copy.Effects=copy.Effects.Where(e=>e.Id!="drop_shadow").ToList();
                copy.Transform.X+=dx;copy.Transform.Y+=dy;
                foreach(var k in copy.Motion){if(k.Parameter=="x")k.Value+=dx;else if(k.Parameter=="y")k.Value+=dy;}
                shadowOf[copy.Id]=(clip.Id,fx);
                yield return copy;
            }
            yield return clip;
        }
    }
    static IEnumerable<(string,Dictionary<string,string>)> ShadowFilters(ClipEffect fx)
    {
        double P(string k,double d)=>fx.Parameters.TryGetValue(k,out var v)&&double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out var x)?x:d;
        string hex=(fx.Parameters.GetValueOrDefault("color")??"#000000").TrimStart('#');if(hex.Length<6)hex="000000";
        int R=Convert.ToInt32(hex[..2],16),G=Convert.ToInt32(hex[2..4],16),B=Convert.ToInt32(hex[4..6],16);
        double op=Math.Clamp(P("opacity",50)/100,0,1),soft=Math.Clamp(P("softness",10),0,100);
        yield return("avfilter.lutrgb",new(){["av.r"]=R.ToString(CultureInfo.InvariantCulture),["av.g"]=G.ToString(CultureInfo.InvariantCulture),["av.b"]=B.ToString(CultureInfo.InvariantCulture),["av.a"]=$"val*{N(op)}"});
        if(soft>=1)yield return("avfilter.boxblur",new(){["av.luma_radius"]=N(Math.Round(soft/2)),["av.luma_power"]="2",["av.alpha_radius"]=N(Math.Round(soft/2)),["av.alpha_power"]="2"});
    }
    static XElement CairoComposite(TimelineClip clip,ClipPlan plan,long start,long duration,int width,int height,Func<long,long> convert,int track)
    {
        var tr=clip.Transform;
        var times=new SortedSet<long>{0};
        foreach(var k in clip.Motion.Where(k=>k.Parameter is "x" or "y" or "scale_x" or "scale_y" or "rotation"))times.Add(convert(k.Frame)+plan.HeadExt);
        // Eased, smooth and hold segments are sampled every frame (cairoaffineblend only interpolates linearly).
        foreach(var g in clip.Motion.Where(k=>k.Parameter is "x" or "y" or "scale_x" or "scale_y" or "rotation").GroupBy(k=>k.Parameter))
        {
            var ks=g.OrderBy(k=>k.Frame).ToList();
            for(int i=0;i+1<ks.Count;i++)if(ks[i].Interpolation!="linear")for(long f=ks[i].Frame+1;f<ks[i+1].Frame;f++)times.Add(convert(f)+plan.HeadExt);
        }
        foreach(var m in plan.Motion)times.Add(Math.Max(0,m.At));
        long ToClip(long t)=>Math.Max(0,(long)Math.Round((t-plan.HeadExt)*(double)clip.Duration/Math.Max(1,convert(clip.Duration))));
        (double Dx,double Dy,double S) PlanAt(long t)
        {
            if(plan.Motion.Count==0)return(0,0,1);var keys=plan.Motion.OrderBy(m=>m.At).ToList();
            if(t<=keys[0].At)return(keys[0].Dx,keys[0].Dy,keys[0].Scale);if(t>=keys[^1].At)return(keys[^1].Dx,keys[^1].Dy,keys[^1].Scale);
            for(int i=1;i<keys.Count;i++)if(t<=keys[i].At){var a=keys[i-1];var b=keys[i];double u=(t-a.At)/(double)Math.Max(1,b.At-a.At);return(a.Dx+(b.Dx-a.Dx)*u,a.Dy+(b.Dy-a.Dy)*u,a.Scale+(b.Scale-a.Scale)*u);}
            return(0,0,1);
        }
        var px=new List<string>();var py=new List<string>();var sx=new List<string>();var sy=new List<string>();var rot=new List<string>();
        foreach(long t in times)
        {
            long cf=ToClip(t);
            double X=ClipBlend.At(clip,"x",cf)??tr.X,Y=ClipBlend.At(clip,"y",cf)??tr.Y,SX=ClipBlend.At(clip,"scale_x",cf)??tr.ScaleX,SY=ClipBlend.At(clip,"scale_y",cf)??tr.ScaleY,R=ClipBlend.At(clip,"rotation",cf)??tr.Rotation;
            var (dx,dy,ps)=PlanAt(t);
            var (left,top,w,h,_,_)=TransformGeometry.Place(width,height,X+dx,Y+dy,SX*ps,SY*ps,R,tr);
            double cx=left+w/2,cy=top+h/2;
            px.Add($"{t}={N((cx/width+2)/5)}");py.Add($"{t}={N((cy/height+2)/5)}");sx.Add($"{t}={N(w/width/5)}");sy.Add($"{t}={N(h/height/5)}");
            rot.Add($"{t}={N((R%360+360)%360/360)}");
        }
        return new XElement("transition",new XAttribute("in",start),new XAttribute("out",start+duration-1),P("mlt_service","frei0r.cairoaffineblend"),P("a_track",0),P("b_track",track),
            P("0",string.Join(';',px)),P("1",string.Join(';',py)),P("2",string.Join(';',sx)),P("3",string.Join(';',sy)),P("4",string.Join(';',rot)),P("5","1"),P("6",clip.BlendMode??"normal"),P("7","0.5"),P("8","0.5"),P("always_active",1));
    }
    /// <summary>Static mask as a luma pattern: dark inside (kept), light outside (cut), with a feathered edge.</summary>
    static string MaskPattern(ClipMask mask,int frameWidth,int frameHeight)
    {
        string key=string.Join('_',mask.Shape,N(mask.X),N(mask.Y),N(mask.Width),N(mask.Height),N(mask.Feather),mask.Invert?"i":"n",frameWidth*1000/Math.Max(1,frameHeight),mask.Smooth?"s":"c",string.Join(',',mask.Points.Select(N)));
        string dir=Path.Combine(Path.GetTempPath(),"zenithclipper-masks");Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16]+".pgm");
        if(File.Exists(path))return path;
        int w=480,h=Math.Max(2,(int)Math.Round(480.0*frameHeight/Math.Max(1,frameWidth)));var data=new byte[w*h];
        double rx=Math.Max(.001,mask.Width/2),ry=Math.Max(.001,mask.Height/2),f=Math.Max(.001,mask.Feather);
        if(mask.Shape=="path")
        {
            // Pen mask: signed distance to the outline in raster pixels, inside by the even-odd rule, feather centred on the edge.
            var poly=mask.Outline().Select(q=>(X:q.X*w,Y:q.Y*h)).ToList();
            double bw=poly.Max(q=>q.X)-poly.Min(q=>q.X),bh=poly.Max(q=>q.Y)-poly.Min(q=>q.Y),fp=Math.Max(.75,f*Math.Max(1,Math.Min(bw,bh))/2);
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                double px=x+.5,py=y+.5,best=double.MaxValue;bool inPoly=false;
                for(int i=0,j=poly.Count-1;i<poly.Count;j=i++)
                {
                    var(ax,ay)=poly[j];var(bx,by)=poly[i];
                    if((by>py)!=(ay>py)&&px<(ax-bx)*(py-by)/(ay-by)+bx)inPoly=!inPoly;
                    double ex=ax-bx,ey=ay-by,len=ex*ex+ey*ey,t=len<1e-12?0:Math.Clamp(((px-bx)*ex+(py-by)*ey)/len,0,1);
                    double dx=px-(bx+t*ex),dy=py-(by+t*ey);best=Math.Min(best,dx*dx+dy*dy);
                }
                double sd=(inPoly?1:-1)*Math.Sqrt(best);double inside=Math.Clamp(sd/fp+.5,0,1);if(mask.Invert)inside=1-inside;
                data[y*w+x]=(byte)Math.Round((1-inside)*255);
            }
        }
        else
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            double u=(x+.5)/w-mask.X,v=(y+.5)/h-mask.Y;
            double d=mask.Shape=="rectangle"?Math.Max(Math.Abs(u)/rx,Math.Abs(v)/ry):Math.Sqrt(u*u/(rx*rx)+v*v/(ry*ry));
            double inside=Math.Clamp((1+f/2-d)/f,0,1);if(mask.Invert)inside=1-inside;
            data[y*w+x]=(byte)Math.Round((1-inside)*255);
        }
        using(var file=File.Create(path+".tmp")){file.Write(System.Text.Encoding.ASCII.GetBytes($"P5 {w} {h} 255\n"));file.Write(data);}
        File.Move(path+".tmp",path,true);return path;
    }
    static XElement Filter(string service,long first,long duration,Dictionary<string,string> parameters)=>new("filter",new XAttribute("in",first),new XAttribute("out",first+duration-1),P("mlt_service",service),parameters.Select(kv=>P(kv.Key,kv.Value)));
}






















