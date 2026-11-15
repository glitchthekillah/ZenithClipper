extern alias desktop;
using System.Reflection;
using System.Text.Json;
using System.Buffers.Binary;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ZenithClipper;
using ZenithClipper.Editing;
using desktop::ZenithClipper.Editor.Desktop;
using ZenithClipper.Mlt;
internal static class Program
{
 static int passed;static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 [STAThread]static int Main(string[] args){if(args.Contains("--metadata-only")){try{MetadataDurationChecks.Run();return 0;}catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}}AppBuilder.Configure<EditorApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();if(SynchronizationContext.Current is null)SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());var task=Run(args);while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}try{return task.GetAwaiter().GetResult();}catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}}
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
 static EditorSession Session(EditorWindow w)=>(EditorSession)typeof(EditorWindow).GetField("session",Private)!.GetValue(w)!;
 static void Invoke(EditorWindow w,string name)=>typeof(EditorWindow).GetMethod(name,Private)!.Invoke(w,null);
 static double Rms(byte[] pcm){double sum=0;int count=pcm.Length/2;for(int i=0;i<count;i++){double x=BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i*2,2))/32768d;sum+=x*x;}return count==0?0:Math.Sqrt(sum/count);}
 static async Task<int> Run(string[] args)
 {
  passed+=MetadataDurationChecks.Run();
  string ffmpeg=args.FirstOrDefault()??"ffmpeg",root=Path.Combine(Path.GetTempPath(),"zc-import-av-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string movie=Path.Combine(root,"common av.mp4"),mkv=Path.Combine(root,"unicode café clip.mkv");
  await MediaTools.RunAsync(ffmpeg,["-v","error","-threads","1","-f","lavfi","-i","color=c=blue:s=160x90:r=30000/1001","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-t","2","-threads","1","-filter_threads","1","-filter_complex_threads","1","-c:v","libx264","-preset","ultrafast","-c:a","aac",movie]);
  await MediaTools.RunAsync(ffmpeg,["-v","error","-threads","1","-i",movie,"-map","0","-c","copy",mkv]);string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(movie)));
  foreach(var text in new[]{"1801170000/10925497","14850000/90089"}){using var json=JsonDocument.Parse("{\"avg_frame_rate\":\""+text+"\",\"r_frame_rate\":\"330/1\"}");var rate=MediaMetadata.ReadFrameRate(json.RootElement);var pieces=text.Split('/');double measured=double.Parse(pieces[0],System.Globalization.CultureInfo.InvariantCulture)/double.Parse(pieces[1],System.Globalization.CultureInfo.InvariantCulture);Check(rate.Numerator<=1000000&&rate.Denominator<=1000000&&Math.Abs(rate.Value-measured)<1e-7,"large user-capture rational normalized without misleading 330 fps");}
  var os=new EditSequence();var op=new EditProject{Sequences=[os],ActiveSequenceId=os.Id};var odd=new MediaAsset{Kind=MediaKind.Video,Width=1919,Height=1081,Fps=new(165,1),DurationSeconds=2};Check(MediaMetadata.TryAdoptFirstVideo(op,odd,false)&&os.Width==1920&&os.Height==1082&&odd.Width==1919,"odd source retained with codec-safe output canvas");
  var window=new EditorWindow([]){HeadlessVerification=true};
  try
  {
   var imported=await window.ImportPathsAsync([movie,mkv]);var p=Session(window).Project;Check(imported.Count==2&&p.Media.All(m=>m.HasAudio&&m.Kind==MediaKind.Video),"real MP4 and Unicode-path MKV import");Check(p.Sequences[0].Fps==new FrameRate(30000,1001),"fractional source frame rate preserved");
   Invoke(window,"AppendSelected");p=Session(window).Project;var v=p.Sequences[0].Tracks.Single(t=>t.Kind==TrackKind.Video).Clips.Single();var a=p.Sequences[0].Tracks.Single(t=>t.Kind==TrackKind.Audio).Clips.Single();Check(v.LinkId is not null&&v.LinkId==a.LinkId&&v.StreamIntent==ClipStreamIntent.VideoOnly&&a.StreamIntent==ClipStreamIntent.AudioOnly,"Add to timeline UI creates separate linked A/V");
   Invoke(window,"UnlinkSelected");p=Session(window).Project;Check(p.Sequences[0].Tracks.SelectMany(t=>t.Clips).All(c=>c.LinkId is null)&&p.Sequences[0].Tracks[0].Clips[0].StreamIntent==ClipStreamIntent.VideoOnly,"UI unlink preserves video-only intent");Invoke(window,"RelinkSelected");Check(Session(window).Project.Sequences[0].Tracks.SelectMany(t=>t.Clips).Select(c=>c.LinkId).Distinct().Count()==1,"UI relink restores source pair");
   int errors=window.ImportErrorPresentations;var missing=await window.ImportPathsAsync([Path.Combine(root,"missing.mp4")]);Check(missing.Count==0&&window.ImportErrorPresentations==errors+1&&window.LastImportError!.Contains("missing.mp4",StringComparison.Ordinal),"failed import presents error instead of false success");var dialog=window.CreateImportErrorDialog(window.LastImportError!);Check(dialog.Title=="Media could not be imported"&&dialog.Content is not null,"readable import error dialog constructed");
  }
  finally{typeof(EditorWindow).GetField("closingApproved",Private)!.SetValue(window,true);window.Close();}
  var asset=await EditorWindow.Probe(movie);var video=new EditTrack{Kind=TrackKind.Video};var audio=new EditTrack{Kind=TrackKind.Audio};var seq=new EditSequence{Width=160,Height=90,Fps=asset.Fps,Tracks=[video,audio]};var project=new EditProject{ActiveSequenceId=seq.Id,Sequences=[seq],Media=[asset]};EditCommands.AppendMedia(project,seq.Id,asset.Id,0,video.Id);
  await using(var engine=new MltSession())
  {
   await engine.LoadAsync(project,160);double linked=Rms((await engine.GetFrameAsync(5)).AudioPcm);EditCommands.Unlink(project,seq.Id,video.Clips[0].Id);await engine.LoadAsync(project,160);double unlinked=Rms((await engine.GetFrameAsync(5)).AudioPcm);Check(linked>.005&&Math.Abs(unlinked/linked-1)<.05,"native audio amplitude not doubled after unlink");
   audio.Muted=true;await engine.LoadAsync(project,160);Check((await engine.GetFrameAsync(5)).AudioPcm.All(b=>b==0),"muted detached audio does not reveal embedded duplicate");audio.Muted=false;
   EditCommands.Move(project,seq.Id,video.Id,video.Clips[0].Id,video.Id,15);await engine.LoadAsync(project,160);var frame=await engine.GetFrameAsync(5);Check(Rms(frame.AudioPcm)>.005&&frame.Rgba[(45*160+80)*4+2]<20,"detached video moves independently while audio stays audible");EditCommands.Relink(project,seq.Id,video.Clips[0].Id);Check(video.Clips[0].Start==audio.Clips[0].Start,"relink resynchronizes independent movement");
   string linkedOutput=Path.Combine(root,"linked-av.mp4");await engine.ExportAsync(project,linkedOutput);EditCommands.Unlink(project,seq.Id,video.Clips[0].Id);string output=Path.Combine(root,"separate-av.mp4");await engine.ExportAsync(project,output);string linkedPcm=Path.Combine(root,"linked.pcm"),unlinkedPcm=Path.Combine(root,"unlinked.pcm");foreach(var pair in new[]{(linkedOutput,linkedPcm),(output,unlinkedPcm)})await MediaTools.RunAsync(ffmpeg,["-v","error","-threads","1","-ss","0.7","-i",pair.Item1,"-t","1","-vn","-ac","2","-ar","48000","-f","s16le",pair.Item2]);double linkedExportRms=Rms(await File.ReadAllBytesAsync(linkedPcm)),unlinkedExportRms=Rms(await File.ReadAllBytesAsync(unlinkedPcm));Check(linkedExportRms>.005&&Math.Abs(unlinkedExportRms/linkedExportRms-1)<.03,"exported audio amplitude stays unchanged after unlink");using var probe=JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,output));Check(probe.RootElement.GetProperty("streams").EnumerateArray().Any(s=>s.GetProperty("codec_type").GetString()=="audio"),"separate linked streams export actual audio");
  }
  Check(hash==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(movie))),"original fixture bytes unchanged");Console.WriteLine($"{passed} editor import/A-V checks passed. Fixtures retained: {root}");return 0;
 }
}
