using ZenithClipper;
using ZenithClipper.Editing;
using ZenithClipper.Mlt;
string root=Path.Combine(Path.GetTempPath(),"zenith-mlt-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
string ffmpeg=args.FirstOrDefault()??"ffmpeg";
try
{

    string red=Path.Combine(root,"red.mp4"),green=Path.Combine(root,"green.mp4");
    foreach(var (color,file,hz) in new[]{("red",red,440),("green",green,880)})
        await MediaTools.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i",$"color=c={color}:s=160x90:r=30","-f","lavfi","-i",$"sine=frequency={hz}:sample_rate=48000","-t","1","-filter_threads","1","-threads","1","-c:v","libx264","-preset","ultrafast","-c:a","aac",file]);
    var a=new MediaAsset{Name="Red",Path=red,Kind=MediaKind.Video,DurationSeconds=1,Width=160,Height=90,Fps=new(30,1),HasAudio=true};
    var b=new MediaAsset{Name="Green",Path=green,Kind=MediaKind.Video,DurationSeconds=1,Width=160,Height=90,Fps=new(30,1),HasAudio=true};
    var blue=new MediaAsset{Name="Blue",Kind=MediaKind.Color,Color="#0000ff",DurationSeconds=10,Width=160,Height=90};
    var track=new EditTrack{Name="Video 1",Kind=TrackKind.Video,Clips=[new(){MediaId=a.Id,Name="Red",Duration=30},new(){MediaId=b.Id,Name="Green",Start=30,Duration=30}]};
    var overlay=new EditTrack{Name="Overlay",Kind=TrackKind.Video,Clips=[new(){MediaId=blue.Id,Start=15,Duration=30,Transform=new(){ScaleX=.5,ScaleY=.5}}]};
    var sequence=new EditSequence{Width=160,Height=90,Fps=new(30,1),Tracks=[track,overlay]};
    var project=new EditProject{ActiveSequenceId=sequence.Id,Sequences=[sequence],Media=[a,b,blue]};
    var malicious=new ClipEffect{Id="vidstab",Parameters=new(){["filename"]=Path.Combine(root,"sentinel"),["analyze"]="1"}};
    track.Clips[0].Effects.Add(malicious);bool rejected=false;
    try{MltXml.Compile(project);}catch(ArgumentException){rejected=true;}
    track.Clips[0].Effects.Clear();if(!rejected)throw new Exception("Untrusted file-writing effect accepted.");
    track.Clips[0].Effects.Add(new(){Id="brightness",Parameters=new(){["mlt_service"]="vidstab"}});rejected=false;
    try{MltXml.Compile(project);}catch(ArgumentException){rejected=true;}
    track.Clips[0].Effects.Clear();if(!rejected)throw new Exception("Effect service override accepted.");
    Console.WriteLine("PASS unsafe native effect services/parameters rejected before loading");
    Console.WriteLine("MLT runtime "+MltSession.RuntimeVersion);
    await using var engine=new MltSession();
    await engine.LoadAsync(project,160);
    var first=await engine.GetFrameAsync(5);AssertPixel(first,80,45,0,"red clip");Console.WriteLine("PASS MLT first clip decoded");
    var middle=await engine.GetFrameAsync(20);AssertPixel(middle,80,45,2,"blue overlay");AssertPixel(middle,5,5,0,"red outside overlay");Console.WriteLine("PASS MLT layered transform");
    var end=await engine.GetFrameAsync(50);AssertPixel(end,80,45,1,"green clip");Console.WriteLine("PASS MLT second clip after cut");
    var beforeCut = await engine.GetFrameAsync(29);
    var atCut = await engine.GetFrameAsync(30);
    var afterCut = await engine.GetFrameAsync(31);
    AssertPixel(beforeCut, 5, 5, 0, "last red frame");
    AssertPixel(atCut, 5, 5, 1, "first green frame");
    AssertPixel(afterCut, 5, 5, 1, "next green frame");
    AssertPixel(await engine.GetFrameAsync(28), 5, 5, 0, "backward seek");
    Console.WriteLine("PASS MLT sequential decode crosses the cut and supports backward seeks");
    if(first.AudioPcm.Length==0 || first.AudioPcm.All(x=>x==0))throw new Exception("Native preview PCM audio missing.");
    Console.WriteLine("PASS MLT native preview audio samples");
    track.Muted=true;await engine.LoadAsync(project,160);var muted=await engine.GetFrameAsync(5);
    if(muted.AudioPcm.Any(x=>x!=0))throw new Exception("Muted track leaked audio.");track.Muted=false;
    Console.WriteLine("PASS MLT track mute");
    overlay.Clips[0].Opacity=.5;await engine.LoadAsync(project,160);var blended=await engine.GetFrameAsync(20);int center=(45*160+80)*4;
    if(blended.Rgba[center]<50 || blended.Rgba[center+2]<50)throw new Exception("Opacity did not blend layers.");overlay.Clips[0].Opacity=1;
    Console.WriteLine("PASS MLT opacity compositing");
    track.Clips[0].Effects.Add(new(){Id="brightness",Parameters=new(){["alpha"]="1"},Keyframes=[new(){Parameter="level",Frame=0,Value=.1},new(){Parameter="level",Frame=29,Value=1}]});
    await engine.LoadAsync(project,160);var dark=await engine.GetFrameAsync(1);var bright=await engine.GetFrameAsync(12);
    if(bright.Rgba[center]<dark.Rgba[center]+30)throw new Exception("Effect keyframes did not change pixels.");track.Clips[0].Effects.Clear();
    Console.WriteLine("PASS MLT effect keyframes");
    await engine.LoadAsync(project,160);
    try{await engine.PlayAsync(_=>throw new InvalidOperationException("test callback fault"));}catch(InvalidOperationException){}
    await engine.PauseAsync();await engine.LoadAsync(project,160);AssertPixel(await engine.GetFrameAsync(5),80,45,0,"recovered playback");
    Console.WriteLine("PASS MLT playback fault recovery");
    using(var playbackCancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(180)))
    {
        int frames=0;try{await engine.PlayAsync(_=>Interlocked.Increment(ref frames),playbackCancel.Token);}catch(OperationCanceledException){}
        await engine.PauseAsync();if(frames<1 || engine.AudioError!=null)throw new Exception("Dummy audio playback failed: "+engine.AudioError);
    }
    Console.WriteLine("PASS MLT synchronized preview loop with queued audio");
    await engine.LoadAsync(project, 160);
    var shown = new List<long>();
    await engine.PlayAsync(frame => shown.Add(frame.Position)).WaitAsync(TimeSpan.FromSeconds(10));
    if (shown.Count < 2 || shown[0] != 0 || shown[^1] != engine.Duration - 1 ||
        shown.Zip(shown.Skip(1)).Any(pair => pair.First >= pair.Second))
        throw new Exception("Playback must show ordered frames and reach the timeline end.");
    Console.WriteLine("PASS MLT paced preview presents the first and final frames in order");
    string titlePng=Path.Combine(root,"alpha-title.png");
    await MediaTools.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i","color=c=black@0:s=64x16,format=rgba,drawbox=x=0:y=0:w=32:h=16:color=white@1:t=fill:replace=1","-frames:v","1","-threads","1","-filter_threads","1",titlePng]);
    var captionProject=ProjectValidation.Clone(project);var captionSequence=captionProject.Sequences[0];
    var caption=new MediaAsset{Kind=MediaKind.Text,Path=titlePng,Name="Caption",Text="Test",DurationSeconds=1,Width=64,Height=16};captionProject.Media.Add(caption);
    captionSequence.Tracks[1].Clips=[new(){MediaId=caption.Id,Duration=30,Transform=new(){ScaleX=.4,ScaleY=.4}}];
    await engine.LoadAsync(captionProject,160);var title=await engine.GetFrameAsync(5);
    int leftTitle=(45*160+60)*4,rightTitle=(45*160+100)*4;
    if(title.Rgba[leftTitle]<180 || title.Rgba[leftTitle+1]<180 || title.Rgba[rightTitle+1]>40)throw new Exception("Transparent text/image composition failed: "+string.Join(" / ",new[]{40,50,60,80,100,120}.Select(x=>$"x{x}="+string.Join(',',title.Rgba.Skip((45*160+x)*4).Take(4)))));
    foreach(int frame in new[]{0,29})
    {
        var held=await engine.GetFrameAsync(frame);
        if(held.Rgba[leftTitle]<180 || held.Rgba[leftTitle+1]<180 || held.Rgba[rightTitle+1]>40)throw new Exception("Still frame/alpha failed at "+frame);
    }
    captionSequence.Tracks[1].Clips[0].Effects.Add(new(){Id="brightness",Keyframes=[new(){Parameter="level",Frame=0,Value=.1},new(){Parameter="level",Frame=29,Value=1}]});
    await engine.LoadAsync(captionProject,160);var titleDark=await engine.GetFrameAsync(0);var titleBright=await engine.GetFrameAsync(29);
    if(titleBright.Rgba[leftTitle+1]<titleDark.Rgba[leftTitle+1]+100)throw new Exception("Repeated still-image keyframes did not animate.");
    captionSequence.Tracks[1].Clips[0].Effects.Clear();
    string captionOutput=Path.Combine(root,"title-render.mp4");await engine.ExportAsync(captionProject,captionOutput);
    string captionRaw=Path.Combine(root,"title-render.rgba");await MediaTools.RunAsync(ffmpeg,["-v","error","-xerror","-threads","1","-i",captionOutput,"-filter_threads","1","-vf","select=eq(n\\,29)","-frames:v","1","-f","rawvideo","-pix_fmt","rgba",captionRaw]);
    var encodedTitle=File.ReadAllBytes(captionRaw);if(encodedTitle.Length!=160*90*4 || encodedTitle[leftTitle+1]<150 || encodedTitle[rightTitle+1]>60)throw new Exception("Exported PNG alpha/last frame failed.");
    Console.WriteLine("PASS MLT compact PNG alpha at first/middle/last frame, animated effect, and exported title");
    var speedProject=ProjectValidation.Clone(project);speedProject.Media[0].DurationSeconds=1000;var speedClip=speedProject.Sequences[0].Tracks[0].Clips[0];speedClip.SourceIn=10000;speedClip.Speed=.000001;
    bool speedRejected=false;try{MltXml.Compile(speedProject);}catch(ArgumentException){speedRejected=true;}if(!speedRejected)throw new Exception("Timewarp producer range overflow accepted.");
    Console.WriteLine("PASS MLT speed-adjusted source range bounded before native loading");
    string audio441=Path.Combine(root,"audio441.wav");await MediaTools.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i","sine=frequency=880:sample_rate=44100","-t","1",audio441]);
    var audioAsset=new MediaAsset{Path=audio441,Kind=MediaKind.Audio,DurationSeconds=1,HasAudio=true};
    var audioProject=ProjectValidation.Clone(project);audioProject.Media.Add(audioAsset);audioProject.Sequences[0].Tracks=[new(){Kind=TrackKind.Audio,Clips=[new(){MediaId=audioAsset.Id,Duration=30}]}];
    await engine.LoadAsync(audioProject,160);var resampled=await engine.GetFrameAsync(5);
    if(resampled.AudioPcm.Length!=6400 || Rms(resampled.AudioPcm)<100)throw new Exception("44.1 kHz audio was not resampled to 48 kHz stereo PCM.");
    audioProject.Sequences[0].Tracks.Add(new(){Kind=TrackKind.Audio,Clips=[new(){MediaId=a.Id,Duration=30}]});
    await engine.LoadAsync(audioProject,160);var mixedAudio=await engine.GetFrameAsync(5);
    if(Rms(mixedAudio.AudioPcm)<Rms(resampled.AudioPcm)*1.1)throw new Exception("Simultaneous audio tracks did not mix.");
    Console.WriteLine("PASS MLT 44.1 kHz resampling and simultaneous audio track mix");
    var slowProject=ProjectValidation.Clone(audioProject);slowProject.Sequences[0].Fps=new(1,1);slowProject.Sequences[0].Tracks=[new(){Kind=TrackKind.Audio,Clips=[new(){MediaId=audioAsset.Id,Duration=1}]}];
    await engine.LoadAsync(slowProject,160);var slowAudio=await engine.GetFrameAsync(0);
    if(slowAudio.AudioPcm.Length!=192000 || Rms(slowAudio.AudioPcm)<100)throw new Exception("One FPS sequence lost its one-second stereo PCM frame: bytes="+slowAudio.AudioPcm.Length+", rms="+Rms(slowAudio.AudioPcm));
    slowProject.Sequences[0].Tracks[0].Clips[0].MediaId=a.Id;
    await engine.LoadAsync(slowProject,160);var slowAac=await engine.GetFrameAsync(0);
    if(slowAac.AudioPcm.Length!=192000 || Rms(slowAac.AudioPcm)<100 || engine.Duration!=1)throw new Exception("One-frame AAC preview lost audio or changed logical duration.");
    string slowOutput=Path.Combine(root,"one-frame.mp4");await engine.ExportAsync(slowProject,slowOutput);
    using(var slowProbe=System.Text.Json.JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,slowOutput)))
    {
        var slowStreams=slowProbe.RootElement.GetProperty("streams").EnumerateArray();var videoStream=slowStreams.Single(x=>x.GetProperty("codec_type").GetString()=="video");var audioStream=slowStreams.Single(x=>x.GetProperty("codec_type").GetString()=="audio");
        if(videoStream.GetProperty("nb_frames").GetString()!="1" || double.Parse(audioStream.GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture) is <.98 or >1.1)throw new Exception("Native audio handle leaked an extra export frame or changed audio duration.");
    }
    string slowPcm=Path.Combine(root,"one-frame.pcm");await MediaTools.RunAsync(ffmpeg,["-v","error","-xerror","-threads","1","-i",slowOutput,"-vn","-ac","2","-ar","48000","-f","s16le",slowPcm]);
    if(Rms(File.ReadAllBytes(slowPcm))<100)throw new Exception("One-frame export lost audio.");
    Console.WriteLine("PASS MLT one FPS WAV/AAC PCM and one-frame export with audible one-second audio");
    await engine.LoadAsync(project,160);
    string blocked=Path.Combine(root,"not-a-directory");File.WriteAllText(blocked,"keep");
    try{await engine.ExportAsync(project,Path.Combine(blocked,"output.mp4"));throw new Exception("Export accepted invalid directory");}catch(IOException){}
    string output=Path.Combine(root,"render.mp4");await engine.ExportAsync(project,output).WaitAsync(TimeSpan.FromSeconds(5));
    await MediaTools.RunAsync(ffmpeg,["-v","error","-xerror","-threads","1","-i",output,"-f","null","-"]);
    using var probe=System.Text.Json.JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,output));
    var streams=probe.RootElement.GetProperty("streams").EnumerateArray();if(!streams.Any(x=>x.GetProperty("codec_type").GetString()=="audio"))throw new Exception("Rendered audio missing");
    Console.WriteLine("PASS MLT two-track audio/video render and strict decode");
    using var cancel=new CancellationTokenSource();cancel.Cancel();try{await engine.ExportAsync(project,Path.Combine(root,"cancelled.mp4"),ct:cancel.Token);throw new Exception("Cancelled export accepted");}catch(OperationCanceledException){}
    Console.WriteLine("PASS MLT cancelled export");
    var longProject=ProjectValidation.Clone(project);longProject.Sequences[0].Tracks=[new(){Kind=TrackKind.Video,Clips=[new(){MediaId=blue.Id,Duration=18000}]}];
    string interrupted=Path.Combine(root,"interrupted.mp4");using(var interrupt=new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
    {
        try{await engine.ExportAsync(longProject,interrupted,ct:interrupt.Token);throw new Exception("In-progress export ignored cancellation.");}catch(OperationCanceledException){}
    }
    if(File.Exists(interrupted)||Directory.GetFiles(root,".zenith-render-*").Length>0)throw new Exception("Cancelled export left an output file.");
    Console.WriteLine("PASS MLT in-progress export cancellation and cleanup");
    var disposedEngine=new MltSession();await disposedEngine.LoadAsync(project,160);
    var inProgress=disposedEngine.ExportAsync(longProject,Path.Combine(root,"disposed.mp4"));await Task.Delay(30);await disposedEngine.DisposeAsync();
    try{await inProgress;throw new Exception("Disposed engine published ongoing export.");}catch(OperationCanceledException){}
    try{await disposedEngine.LoadAsync(project,160);throw new Exception("Disposed engine loaded a graph.");}catch(ObjectDisposedException){}
    Console.WriteLine("PASS MLT disposal cancels export and rejects new graphs");return 0;
}
catch(Exception ex){Console.WriteLine("FAIL MLT: "+ex);return 1;}
finally{try{Directory.Delete(root,true);}catch(IOException){}}
static void AssertPixel(PreviewFrame f,int x,int y,int channel,string label)
{
    int p=(y*f.Width+x)*4;int wanted=f.Rgba[p+channel];if(wanted<70 || Enumerable.Range(0,3).Where(c=>c!=channel).Any(c=>f.Rgba[p+c]>=wanted-20))throw new Exception($"{label}: unexpected pixel {string.Join(',',f.Rgba.Skip(p).Take(4))}");
}

static double Rms(byte[] pcm)
{
 if(pcm.Length==0)return 0;double sum=0;for(int i=0;i<pcm.Length;i+=2){short sample=BitConverter.ToInt16(pcm,i);sum+=(double)sample*sample;}return Math.Sqrt(sum/(pcm.Length/2));
}










