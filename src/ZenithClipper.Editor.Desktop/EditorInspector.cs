using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

public sealed partial class EditorWindow
{
    void BuildInspector()
    {
        var scrollOffset=propertiesScroller.Offset;
        inspector.Children.Clear();
        Avalonia.Threading.Dispatcher.UIThread.Post(()=>{if(!editorClosed)propertiesScroller.Offset=scrollOffset;},Avalonia.Threading.DispatcherPriority.Loaded);
        var clip = Sequence.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == selectedClip);
        if (clip is null || Sequence.Tracks.First(t => t.Clips.Any(c => c.Id == clip.Id)).Kind == TrackKind.Audio) { cropOverlay.Active = false; cropOverlay.Transform = null; }
        UpdateTransformOverlay();
        if (clip is null)
        {
            var empty = new StackPanel { Spacing = 10, Margin = new Thickness(8, 30) };
            empty.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), Background = ControlBrush, HorizontalAlignment = HorizontalAlignment.Center, Child = Glyph("motion", MutedBrush, 22) });
            empty.Children.Add(new TextBlock { Text = "Fine-tune your edit", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextAlignment = TextAlignment.Center });
            empty.Children.Add(new TextBlock { Text = "Select a timeline clip to adjust timing, motion, sound and effects.", FontSize = 11, LineHeight = 18, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            inspector.Children.Add(empty); return;
        }
        var clipTrack = Sequence.Tracks.First(t => t.Clips.Any(c => c.Id == clip.Id));
        var selected = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*"), Margin = new Thickness(0, 0, 0, 10) };
        selected.Children.Add(new Border { Width = 40, Height = 26, CornerRadius = new CornerRadius(4), Background = ControlBrush, Child = Glyph(clipTrack.Kind == TrackKind.Audio ? "audio" : "media", MutedBrush, 16) });
        var names = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center }; names.Children.Add(Copy(clip.Name, 13, TextBrush, FontWeight.SemiBold));
        names.Children.Add(Copy($"{clipTrack.Name} · {timelineFps.Seconds(clip.Duration):0.##} s · {clip.Duration} frames{(clip.LinkId is not null ? " · linked" : "")}", 11, FaintBrush)); Grid.SetColumn(names, 1); selected.Children.Add(names);
        inspector.Children.Add(selected);
        InspectorTabs();
        if (Project.Media.First(m => m.Id == clip.MediaId).Kind == MediaKind.Text) { var edit = Button("Edit text…", () => Run(EditSelectedText)); edit.Margin = new Thickness(0, 0, 0, 8); inspector.Children.Add(edit); }
        var source=Project.Media.First(m=>m.Id==clip.MediaId); if(source.Kind==MediaKind.Video&&source.HasAudio){var link=Button(clip.LinkId is not null?"Unlink video & audio":"Relink source video & audio",clip.LinkId is not null?UnlinkSelected:RelinkSelected);ToolTip.SetTip(link,"Linked timing edits move both source streams. Relinking aligns the source partner to this clip.");link.Margin=new Thickness(0,0,0,8);inspector.Children.Add(link);}
        if (source.Kind == MediaKind.Color) { var gen = InspectorSection("Generated item", "media"); GeneratorControls(gen, source); }
        if (source.Kind == MediaKind.Image && Path.GetFileName(source.Path).StartsWith("shape-", StringComparison.Ordinal)) { var shape = InspectorSection("Shape", "media"); ShapeControls(shape, source); }
        {
            // Premiere's Info / Metadata panel, folded into the inspector.
            var info = InspectorSection("Source info", "media", false);
            void Line(string k, string v) { var g = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*"), MinHeight = 22 }; g.Children.Add(Copy(k, 11.5, FaintBrush)); var t = Copy(v, 11.5, SoftTextBrush); t.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(t, v); Grid.SetColumn(t, 1); g.Children.Add(t); info.Children.Add(g); }
            Line("Type", source.Kind.ToString());
            if (!string.IsNullOrEmpty(source.Path)) Line("File", source.Path);
            if (source.Width > 0) Line("Frame size", $"{source.Width}×{source.Height}");
            if (source.Fps is { } f && f.Value > 0) Line("Frame rate", $"{f.Value:0.###} fps");
            if (source.DurationSeconds > 0) Line("Media length", $"{source.DurationSeconds:0.###} s");
            if (source.SubclipIn is { } si && source.SubclipOut is { } so) Line("Subclip", $"{si:0.###} s to {so:0.###} s");
            Line("Audio", source.HasAudio ? "Yes" : "No");
            if (source.Kind == MediaKind.Video && source.Fps is { } native && native.Value > 0)
            {
                // Interpret Footage: play the file as if shot at another rate (e.g. 120 fps footage as 30 fps slow motion).
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*,34"), MinHeight = 30 }; row.Children.Add(Copy("Interpret as", 11.5, FaintBrush));
                string was = (source.InterpretFps ?? native.Value).ToString("0.###", CultureInfo.InvariantCulture);
                var fpsBox = new TextBox { Text = was, FontSize = 11.5, MinHeight = 26, Height = 26, Padding = new Thickness(6, 0) }; Grid.SetColumn(fpsBox, 1); row.Children.Add(fpsBox);
                var u = Copy("fps", 11, FaintBrush); u.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(u, 2); row.Children.Add(u);
                ToolTip.SetTip(fpsBox, "Interpret Footage: the frame rate to play this file at. Lower than its real rate gives smooth slow motion. Audio is unchanged.");
                var mid = source.Id; double nativeFps = native.Value, nativeLength = source.InterpretFps is { } f0 ? source.DurationSeconds * f0 / nativeFps : source.DurationSeconds;
                fpsBox.LostFocus += (_, _) =>
                {
                    if (fpsBox.Text == was || !double.TryParse(fpsBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 1 || v > 1000) return;
                    Change("Interpret footage", p => { var m = p.Media.First(x => x.Id == mid); bool same = Math.Abs(v - nativeFps) < 1e-6; m.InterpretFps = same ? null : v; m.DurationSeconds = nativeLength * nativeFps / v; });
                };
                info.Children.Add(row);
                var fieldRow = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*"), MinHeight = 30 }; fieldRow.Children.Add(Copy("Field order", 11.5, FaintBrush));
                string[] orders = [null!, "progressive", "upper", "lower"]; string[] labels = ["Use file", "Progressive", "Upper field first", "Lower field first"];
                var fieldBox = new ComboBox { ItemsSource = labels, SelectedIndex = Math.Max(0, Array.IndexOf(orders, source.FieldOrder)), FontSize = 11.5, MinHeight = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Stretch, Name = "field-order" }; Grid.SetColumn(fieldBox, 1); fieldRow.Children.Add(fieldBox);
                ToolTip.SetTip(fieldBox, "Interpret Footage field order. Set it when interlaced footage shows combing or judder; it's then deinterlaced to progressive.");
                fieldBox.SelectionChanged += (_, _) => { var chosen = orders[Math.Max(0, fieldBox.SelectedIndex)]; if (chosen == source.FieldOrder) return; Change("Field order", p => p.Media.First(x => x.Id == mid).FieldOrder = chosen); };
                info.Children.Add(fieldRow);
            }
            if (source.Kind is MediaKind.Video or MediaKind.Image)
            {
                var alphaRow = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*"), MinHeight = 30 }; alphaRow.Children.Add(Copy("Alpha", 11.5, FaintBrush));
                string?[] modes = [null, "ignore", "invert", "premultiplied"]; string[] alphaNames = ["Use file", "Ignore alpha", "Invert alpha", "Premultiplied"];
                var alphaBox = new ComboBox { ItemsSource = alphaNames, SelectedIndex = Math.Max(0, Array.IndexOf(modes, source.AlphaMode)), FontSize = 11.5, MinHeight = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Stretch, Name = "alpha-mode" }; Grid.SetColumn(alphaBox, 1); alphaRow.Children.Add(alphaBox);
                ToolTip.SetTip(alphaBox, "Interpret Footage alpha. Premultiplied fixes dark fringes on footage rendered with a premultiplied alpha channel.");
                var amid = source.Id; alphaBox.SelectionChanged += (_, _) => { var chosen = modes[Math.Max(0, alphaBox.SelectedIndex)]; if (chosen == source.AlphaMode) return; Change("Alpha interpretation", p => p.Media.First(x => x.Id == amid).AlphaMode = chosen); };
                info.Children.Add(alphaRow);
            }
            Line("Clip in / out", $"{Clock(clip.SourceIn)} / {Clock(clip.SourceIn + (long)Math.Round(clip.Duration * clip.Speed))}");
            Line("Speed", TimeRemap.Has(clip) ? "Time remapped" : $"{clip.Speed * 100:0.#}%{(clip.Reverse ? " reversed" : "")}");
        }
        var timing = InspectorSection("Timing", "cut");
        var units=new CheckBox{Content="Show frame numbers",IsChecked=timeFieldsFrames,FontSize=11,Foreground=SoftTextBrush}; units.IsCheckedChanged+=(_,_)=>{timeFieldsFrames=units.IsChecked==true;BuildInspector();};timing.Children.Add(units);
        TimeField("Timeline start",clip.Start,(c,v)=>c.Start=v);TimeField("Source in",clip.SourceIn,(c,v)=>c.SourceIn=v);TimeField("Duration",clip.Duration,(c,v)=>c.Duration=v);
        void TimeField(string label,long frames,Action<TimelineClip,long> apply) { bool inFrames=timeFieldsFrames; var rate=timelineFps; Field(timing,label,inFrames?frames:rate.Seconds(frames),inFrames?"fr":"s",(c,v)=>apply(c,inFrames?checked((long)v):rate.Frames(v))); }
        {
            // Premiere's Speed/Duration: percent, with "Ripple edit, shifting trailing clips".
            var row=new Grid{ColumnDefinitions=new ColumnDefinitions("*,84,24"),MinHeight=30};row.Children.Add(Copy("Speed",12,TextBrush));
            string was=(clip.Speed*100).ToString("0.##",CultureInfo.InvariantCulture);
            var box=new TextBox{Name="property-Speed",Text=was,FontSize=12,MinHeight=28,Height=28,Padding=new Thickness(8,0),HorizontalContentAlignment=HorizontalAlignment.Right,IsEnabled=!TimeRemap.Has(clip)};Grid.SetColumn(box,1);row.Children.Add(box);
            var pct=Copy("%",11,FaintBrush);pct.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(pct,2);row.Children.Add(pct);timing.Children.Add(row);
            var ripple=new CheckBox{Content="Ripple edit, shifting trailing clips",IsChecked=rippleSpeed,FontSize=11,Foreground=SoftTextBrush,Margin=new Thickness(0,2)};ripple.IsCheckedChanged+=(_,_)=>rippleSpeed=ripple.IsChecked==true;timing.Children.Add(ripple);
            var cid=clip.Id;
            box.LostFocus+=(_,_)=>{if(box.Text==was||!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)||v<=0)return;bool r=rippleSpeed;Change("Speed/Duration",p=>EditCommands.SetSpeedRipple(p,p.ActiveSequenceId,cid,Math.Clamp(v,1,10000)/100,r));};
        }
        var reverse = new CheckBox { Content = "Reverse playback", IsChecked = clip.Reverse, FontSize = 11, Foreground = SoftTextBrush, Margin = new Thickness(0,2) }; reverse.IsCheckedChanged+=(_,_)=>Mutate(c=>c.Reverse=reverse.IsChecked==true); timing.Children.Add(reverse);
        var pitch = new CheckBox { Content = "Maintain audio pitch", IsChecked = clip.MaintainPitch, FontSize = 11, Foreground = SoftTextBrush, Margin = new Thickness(0,2) }; pitch.IsCheckedChanged+=(_,_)=>Mutate(c=>c.MaintainPitch=pitch.IsChecked==true); ToolTip.SetTip(pitch, "When the speed isn't 100%, stretch the sound without making it higher or lower."); timing.Children.Add(pitch);
        TimeRemapControls(timing, clip, Sequence.Tracks.First(t=>t.Clips.Any(c=>c.Id==clip.Id)), Project.Media.First(m=>m.Id==clip.MediaId));
        bool audioOnly=clip.StreamIntent==ClipStreamIntent.AudioOnly||Sequence.Tracks.First(t=>t.Clips.Any(c=>c.Id==clip.Id)).Kind==TrackKind.Audio;bool videoOnly=clip.StreamIntent==ClipStreamIntent.VideoOnly;
        if(!audioOnly){
        var motion = InspectorSection("Transform", "motion", true);
        KeyframeNavigator(motion, clip);
        KeyField(motion,"Position X","x",clip.Transform.X,"%",100,(c,v)=>c.Transform.X=v);KeyField(motion,"Position Y","y",clip.Transform.Y,"%",100,(c,v)=>c.Transform.Y=v);
        KeyField(motion,"Scale width","scale_x",clip.Transform.ScaleX,"%",100,(c,v)=>c.Transform.ScaleX=v);KeyField(motion,"Scale height","scale_y",clip.Transform.ScaleY,"%",100,(c,v)=>c.Transform.ScaleY=v);
        KeyField(motion,"Rotation","rotation",clip.Transform.Rotation,"°",1,(c,v)=>c.Transform.Rotation=v);int aw=Sequence.Width,ah=Sequence.Height;Field(motion,"Anchor X",clip.Transform.AnchorX*aw,"px",(c,v)=>c.Transform.AnchorX=v/aw);Field(motion,"Anchor Y",clip.Transform.AnchorY*ah,"px",(c,v)=>c.Transform.AnchorY=v/ah);KeyField(motion,"Opacity","opacity",clip.Opacity,"%",100,(c,v)=>c.Opacity=v);
        KeyframeGraphSection(motion, clip);
        BlendAndMask(motion, clip);
        // Crop: numeric edges plus handles on the monitor.
        var cropSection = InspectorSection("Crop", "fit", clip.Transform.HasCrop);
        cropOverlay.Load(clip.Transform, Sequence.Width, Sequence.Height);
        UpdateTransformOverlay();
        var cropTools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        var onMonitor = Button(cropOverlay.Active ? "Done cropping" : "Crop on monitor", () => { cropOverlay.Active = !cropOverlay.Active; BuildInspector(); });
        if (cropOverlay.Active) { onMonitor.Background = AccentBrush; onMonitor.Foreground = InkBrush; onMonitor.Classes.Add("primary"); }
        ToolTip.SetTip(onMonitor, "Drag the edges or corners on the preview. Drag inside to move the crop.");
        cropTools.Children.Add(onMonitor);
        var resetCrop = Button("Reset", () => ApplyCrop(0, 0, 0, 0)); resetCrop.IsEnabled = clip.Transform.HasCrop; Grid.SetColumn(resetCrop, 2); cropTools.Children.Add(resetCrop);
        cropSection.Children.Add(cropTools);
        Field(cropSection,"Crop left",clip.Transform.CropLeft*100,"%",(c,v)=>c.Transform.CropLeft=Math.Clamp(v,0,99)/100);Field(cropSection,"Crop right",clip.Transform.CropRight*100,"%",(c,v)=>c.Transform.CropRight=Math.Clamp(v,0,99)/100);
        Field(cropSection,"Crop top",clip.Transform.CropTop*100,"%",(c,v)=>c.Transform.CropTop=Math.Clamp(v,0,99)/100);Field(cropSection,"Crop bottom",clip.Transform.CropBottom*100,"%",(c,v)=>c.Transform.CropBottom=Math.Clamp(v,0,99)/100);
        }
        var sound = InspectorSection("Sound", "audio", true);
        if(!videoOnly)KeyField(sound,"Volume","volume",clip.GainDb,"dB",1,(c,v)=>c.GainDb=v);else if(source.HasAudio)sound.Children.Add(new TextBlock{Text="Select the separate audio clip to adjust its volume.",TextWrapping=TextWrapping.Wrap,Foreground=MutedBrush,FontSize=11});else sound.Children.Add(new TextBlock{Text="This clip has no sound.",Foreground=MutedBrush,FontSize=11});
        if(audioOnly&&clipTrack.Kind==TrackKind.Audio)EssentialSoundButtons(sound,clip);
        InspectorEffectControls(clip, clipTrack);
        if(audioOnly){ApplyInspectorTab();return;}
        var color = InspectorSection("Color & effects", "motion", true);
        var fades=new Grid{ColumnDefinitions=new ColumnDefinitions("*,6,*")};var fadeIn=Button("Fade in",()=>AddFade(true));var fadeOut=Button("Fade out",()=>AddFade(false));Grid.SetColumn(fadeOut,2);fades.Children.Add(fadeIn);fades.Children.Add(fadeOut);color.Children.Add(fades);
        color.Children.Add(Copy("Video fades · 0.5 seconds",11,FaintBrush));
        Effect("Brightness","brightness","level",1,0,15);Effect("Animated opacity","brightness","alpha",1,0,1);Effect("Gamma","gamma","gamma",1,.01,10);Effect("Blur radius","box_blur","hradius",0,0,100);
        foreach(var effect in clip.Effects){var captured=effect;color.Children.Add(Button("Remove "+effect.Id,()=>Mutate(c=>c.Effects.RemoveAll(e=>e.Id==captured.Id))));}
        ApplyInspectorTab();
        void Mutate(Action<TimelineClip> action)=>Change("Edit clip",p=>EditCommands.ChangeLinkedClip(p,p.ActiveSequenceId,clip.Id,action));
        // A Premiere-style animatable property: the diamond adds/removes a keyframe at the playhead; once a property has
        // keyframes, typing a value sets the keyframe at the playhead instead of the static value.
        void KeyField(StackPanel host,string name,string param,double staticModel,string unit,double scale,Action<TimelineClip,double> applyStatic)
        {
            long rel=Math.Clamp(ProgramPlayhead-clip.Start,0,clip.Duration);bool animated=clip.Motion.Any(k=>k.Parameter==param);bool onKey=clip.Motion.Any(k=>k.Parameter==param&&k.Frame==rel);
            double model=ClipBlend.At(clip,param,rel)??staticModel;
            var row=new Grid{ColumnDefinitions=new ColumnDefinitions("*,84,24,26"),MinHeight=30};row.Children.Add(Copy(name,12,animated?TextBrush:SoftTextBrush));
            var originalText=(model*scale).ToString("0.###",CultureInfo.InvariantCulture);
            var box=new TextBox{Name="property-"+name.Replace(" ","-"),Text=originalText,FontSize=12,MinHeight=28,Height=28,Padding=new Thickness(8,0),HorizontalContentAlignment=HorizontalAlignment.Right,VerticalContentAlignment=VerticalAlignment.Center};
            if(animated)box.Foreground=AccentSoftBrush;
            Grid.SetColumn(box,1);row.Children.Add(box);var suffix=Copy(unit,11,FaintBrush);suffix.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(suffix,2);row.Children.Add(suffix);
            var diamond=ActionButton(onKey?"diamondfill":"diamond",onKey?"Remove keyframe":"Add keyframe at playhead",()=>
            {
                if(onKey)Mutate(c=>c.Motion.RemoveAll(k=>k.Parameter==param&&k.Frame==rel));
                else Mutate(c=>SetKeyframe(c,param,rel,model));
            },iconOnly:true);
            diamond.Foreground=onKey?AccentBrush:animated?SoftTextBrush:FaintBrush;Grid.SetColumn(diamond,3);row.Children.Add(diamond);
            host.Children.Add(row);
            ToolTip.SetTip(box,animated?$"{name} ({unit}) · keyframed: editing sets a keyframe at the playhead":$"{name} ({unit})");
            box.LostFocus+=(_,_)=>{if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)||string.Equals(box.Text,originalText,StringComparison.Ordinal))return;double m=v/scale;if(animated)Mutate(c=>SetKeyframe(c,param,rel,m));else Mutate(c=>applyStatic(c,m));};
        }
        void Field(StackPanel host,string name,double value,string unit,Action<TimelineClip,double> apply)
        {
            var row=new Grid{ColumnDefinitions=new ColumnDefinitions("*,84,24"),MinHeight=30};row.Children.Add(Copy(name,12,SoftTextBrush));
            var originalText=value.ToString("0.###",CultureInfo.InvariantCulture);var box=new TextBox{Name="property-"+name.Replace(" ","-"),Text=originalText,FontSize=12,MinHeight=28,Height=28,Padding=new Thickness(8,0),HorizontalContentAlignment=HorizontalAlignment.Right,VerticalContentAlignment=VerticalAlignment.Center};Grid.SetColumn(box,1);row.Children.Add(box);var suffix=Copy(unit,11,FaintBrush);suffix.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(suffix,2);row.Children.Add(suffix);host.Children.Add(row);
            ToolTip.SetTip(box,$"{name} ({unit})");box.LostFocus+=(_,_)=>{if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&!string.Equals(box.Text,originalText,StringComparison.Ordinal))Mutate(c=>apply(c,v));};
        }
        void Effect(string name,string id,string parameter,double initial,double min,double max)
        {
            var stack=new StackPanel{Spacing=6,Margin=new Thickness(0,8,0,2)};color.Children.Add(stack);
            var row=new Grid{ColumnDefinitions=new ColumnDefinitions("*,84")};row.Children.Add(Copy(name,12,SoftTextBrush));var existing=clip.Effects.FirstOrDefault(e=>e.Id==id);var input=new TextBox{Text=existing?.Parameters.GetValueOrDefault(parameter)??initial.ToString(CultureInfo.InvariantCulture),Height=28,MinHeight=28,FontSize=12,Padding=new Thickness(8,0),HorizontalContentAlignment=HorizontalAlignment.Right,VerticalContentAlignment=VerticalAlignment.Center};Grid.SetColumn(input,1);row.Children.Add(input);stack.Children.Add(row);
            effectTarget=stack;EffectKeyframeControls(clip,id,parameter,input,initial,min,max);effectTarget=null;
            input.LostFocus+=(_,_)=>{if(!double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||value<min||value>max){status.Text=$"{name} must be between {min} and {max}.";return;}if(input.Text==(existing?.Parameters.GetValueOrDefault(parameter)??initial.ToString(CultureInfo.InvariantCulture)))return;Mutate(c=>{var effect=EnsureEffect(c,id);effect.Parameters[parameter]=value.ToString(CultureInfo.InvariantCulture);if(id=="box_blur")effect.Parameters["vradius"]=value.ToString(CultureInfo.InvariantCulture);});};
        }
    }
}
