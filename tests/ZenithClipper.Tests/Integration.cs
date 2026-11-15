using ZenithClipper;
using System.Diagnostics;
using System.Text.Json;

static class Integration
{
    public static async Task<int> RunAsync(string ffmpeg)
    {
        string root = Path.Combine(Path.GetTempPath(), "ZenithClipper-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); int failed = 0; int passed = 0;
        string? oldHome = Environment.GetEnvironmentVariable("ZENITHCLIPPER_HOME"); Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME", Path.Combine(root, "home"));
        async Task Test(string name, Func<Task> action)
        {
            try { await action(); passed++; Console.WriteLine("PASS integration: " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL integration: " + name + ": " + ex); }
        }
        var settings = new Settings { FfmpegPath = ffmpeg, Encoder = "software", ClipDirectory = Path.Combine(root, "clips"), ClipSeconds = 3, BufferSeconds = 10 };
        string source = Path.Combine(root, "source.mp4");
        try
        {
            await Test("generate 60 FPS video with audio", async () => {
                await MediaTools.RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=60", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "8", "-c:v", "libx264", "-preset", "ultrafast", "-g", "120", "-bf", "0", "-c:a", "aac", source]);
                Check(File.Exists(source), "fixture absent");
            });
            string segments = Path.Combine(root, "segments"); Directory.CreateDirectory(segments);
            await Test("closed CSV segment index", async () => {
                await MediaTools.RunAsync(ffmpeg, ["-v", "error", "-y", "-i", source, "-c", "copy", "-f", "segment", "-segment_time", "2", "-segment_list", Path.Combine(segments,"segments.csv"), "-segment_list_type", "csv", "-reset_timestamps", "1", Path.Combine(segments,"%09d.ts")]);
                Check(ReplayIndex.Read(segments).Count == 4, "expected four closed segments");
            });
            await Test("one-second replay retains decodable video", async () => {
                using var snapshot=new ReplaySnapshot(ReplayIndex.Select(ReplayIndex.Read(segments),1),Path.Combine(root,"short-snapshots"));
                string clip=await snapshot.SaveAsync(settings,1);
                await MediaTools.RunAsync(ffmpeg,["-v","error","-threads","1","-filter_threads","1","-i",clip,"-f","null","-"]);
                using var probe=JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,clip));
                Check(probe.RootElement.GetProperty("streams").EnumerateArray().Any(x=>x.GetProperty("codec_type").GetString()=="video"),"one-second clip has no video");
            });
            await Test("snapshot includes newest unfinished TS segment",async()=>{
                var all=ReplayIndex.Read(segments);
                using var snapshot=new ReplaySnapshot(all.Take(3).ToArray(),Path.Combine(root,"tail-snapshots"),all[3].Path);
                string clip=await snapshot.SaveAsync(settings,3);
                using var probe=JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,clip));
                double duration=double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
                Check(duration>=3.9 && duration<4.2,"tail missing or old extra segments retained: "+duration);
            });
            await Test("partly written tail exports without a corrupt final frame",async()=>{
                var all=ReplayIndex.Read(segments);byte[] bytes=File.ReadAllBytes(all[3].Path);string partial=Path.Combine(root,"partial.ts");File.WriteAllBytes(partial,bytes[..(bytes.Length*2/3/188*188)]);
                using var snapshot=new ReplaySnapshot(all.Take(3).ToArray(),Path.Combine(root,"partial-snapshots"),partial);
                string clip=await snapshot.SaveAsync(settings,3);await MediaTools.RunAsync(ffmpeg,["-v","error","-xerror","-threads","1","-filter_threads","1","-i",clip,"-f","null","-"]);
            });
            string? saved = null;
            await Test("snapshot survives source pruning and saves playable MP4", async () => {
                var selected = ReplayIndex.Select(ReplayIndex.Read(segments), 3);
                using var snapshot = new ReplaySnapshot(selected, Path.Combine(root,"exports"));
                foreach (var segment in selected) File.Delete(segment.Path);
                saved = await snapshot.SaveAsync(settings,3);
                using var probe = JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg, saved));
                var streams = probe.RootElement.GetProperty("streams");
                Check(streams.EnumerateArray().Any(x=>x.GetProperty("codec_type").GetString()=="audio"), "audio missing");
                var video = streams.EnumerateArray().First(x=>x.GetProperty("codec_type").GetString()=="video");
                Check(video.GetProperty("r_frame_rate").GetString()=="60/1", "source FPS changed");
                await MediaTools.RunAsync(ffmpeg,["-v","error","-threads","1","-filter_threads","1","-i",saved,"-f","null","-"]);
            });
            await Test("precise trim crop volume and speed export", async () => {
                string edited = Path.Combine(root,"edited.mp4");
                await Editor.ExportAsync(settings,new EditRequest(source,edited) { StartSeconds=1,DurationSeconds=2,FastTrim=false,CropWidth=160,CropHeight=90,Volume=.5,Speed=1,Encoder="software" });
                using var probe=JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,edited));
                var video=probe.RootElement.GetProperty("streams").EnumerateArray().First(x=>x.GetProperty("codec_type").GetString()=="video");
                Check(video.GetProperty("width").GetInt32()==160 && video.GetProperty("height").GetInt32()==90,"crop dimensions wrong");
                double duration=double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
                Check(Math.Abs(duration-2)<.15,"precise duration wrong: "+duration);
            });
            await Test("fast mute export has no audio", async () => {
                string muted=Path.Combine(root,"muted.mp4"); await Editor.ExportAsync(settings,new EditRequest(source,muted){Mute=true,DurationSeconds=2});
                using var probe=JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,muted));
                Check(!probe.RootElement.GetProperty("streams").EnumerateArray().Any(x=>x.GetProperty("codec_type").GetString()=="audio"),"mute retained audio");
            });
            await Test("speed applies to the selected source duration",async()=>{
                string sped=Path.Combine(root,"sped.mp4");await Editor.ExportAsync(settings,new EditRequest(source,sped){StartSeconds=1,DurationSeconds=2,Speed=2,FastTrim=false,Encoder="software"});
                using var probe=JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,sped));double duration=double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
                Check(Math.Abs(duration-1)<.15,"2 seconds at 2x should produce 1 second: "+duration);
            });
            await Test("existing export never overwritten", async () => {
                var before=File.ReadAllBytes(source);
                try { await Editor.ExportAsync(settings,new EditRequest(source,source+".copy")); await Editor.ExportAsync(settings,new EditRequest(source,source+".copy")); throw new Exception("overwrite accepted"); }
                catch(IOException) { }
                Check(before.SequenceEqual(File.ReadAllBytes(source)),"original changed");
            });
            await Test("cancelled media process releases its output file",async()=>{
                string cancelled=Path.Combine(root,"cancelled.mp4");using var cancel=new CancellationTokenSource();
                var work=MediaTools.RunAsync(ffmpeg,["-v","error","-y","-re","-i",source,"-c","copy",cancelled],cancel.Token);
                await Task.Delay(350);cancel.Cancel();
                try{await work;throw new Exception("expected cancellation");}catch(OperationCanceledException){}
                if(File.Exists(cancelled)){using var exclusive=new FileStream(cancelled,FileMode.Open,FileAccess.ReadWrite,FileShare.None);}
            });
            await Test("atomic settings survive rejected update", () => {
                AppPaths.Save(settings); try { AppPaths.Save(settings with {ClipSeconds=0}); } catch(ArgumentException) { }
                Check(AppPaths.Load().ClipSeconds==3,"good settings lost"); return Task.CompletedTask;
            });
            await Test("abandoned snapshots cleaned without touching other folders",()=>{
                string exports=Path.Combine(root,"abandoned"); Directory.CreateDirectory(Path.Combine(exports,"keep")); string owned=Path.Combine(exports,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(owned);File.WriteAllText(Path.Combine(owned,"0.ts"),"pinned");
                ExportRecovery.Clean(exports); Check(!Directory.Exists(owned) && Directory.Exists(Path.Combine(exports,"keep")),"recovery scope wrong");return Task.CompletedTask;
            });
            await Test("hardware auto detection accepts usable NVENC minimum dimensions",async()=>{
                if(!OperatingSystem.IsWindows())return;
                try{await MediaTools.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i","color=s=320x180:r=30","-frames:v","1","-c:v","h264_nvenc","-preset","p1","-f","null","-"]);}
                catch(IOException){Console.WriteLine("SKIP NVENC unavailable on this machine");return;}
                string selected=await MediaTools.ResolveEncoder(settings with{Encoder="auto"},CancellationToken.None);
                Check(selected=="nvenc","working NVENC was rejected: "+selected);
            });
            await Test("startup requires video rather than only a living process", async () => {
                if(!OperatingSystem.IsWindows())return;
                string helper=Path.Combine(AppContext.BaseDirectory,"ZenithClipper.Tests.exe");
                await using var recorder=new Recorder(settings with{FfmpegPath=helper,CaptureAudio=false,Fps=30});
                using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(12));
                try { await recorder.StartAsync(cancel.Token);throw new Exception("Startup accepted an encoder that produced no video."); }
                catch(IOException) {Check(!recorder.Status().Capturing,"stalled startup retained active state");}
            });
            await Test("pause invalidates resumes already queued at the server",async()=>{
                using var stop=new CancellationTokenSource();await using var recorder=new Recorder(settings with{CaptureAudio=false,Fps=30,FfmpegPath=Path.Combine(AppContext.BaseDirectory,"ZenithClipper.Tests.exe")});
                var server=new ControlServer(recorder,stop);var running=server.RunAsync(stop.Token);
                var gate=(SemaphoreSlim)typeof(ControlServer).GetField("mutations",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(server)!;
                await gate.WaitAsync();Task<ControlResponse>? first=null,second=null;
                try{
                    first=Control.SendAsync(new("resume"));await Task.Delay(150);second=Control.SendAsync(new("resume"));await Task.Delay(150);
                    Check((await Control.SendAsync(new("pause"))).Ok,"pause rejected");gate.Release();
                    var replies=await Task.WhenAll(first,second).WaitAsync(TimeSpan.FromSeconds(2));
                    Check(replies.All(x=>!x.Ok) && recorder.Status().State=="Paused","queued resume undid pause");
                }
                finally{stop.Cancel();if(gate.CurrentCount==0)gate.Release();foreach(var task in new[]{first,second})if(task!=null)try{await task;}catch(IOException){}await running;}
            });
            await Test("pause cancels a pending capture start", async () => {
                await using var recorder=new Recorder(settings);
                var gate=(SemaphoreSlim)typeof(Recorder).GetField("lifecycle",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(recorder)!;
                await gate.WaitAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var start=recorder.StartAsync(deadline.Token);var paused=recorder.StopAsync();
                try {
                    await Task.Delay(100);
                    Check(start.IsCompleted,"pause left startup waiting");
                    gate.Release();await paused.WaitAsync(TimeSpan.FromSeconds(2));
                    Check(recorder.Status().State=="Paused","pause state lost");
                }
                finally {deadline.Cancel();if(gate.CurrentCount==0)gate.Release();try{await start;}catch(OperationCanceledException){}catch(TimeoutException){}await paused;}
            });
            await Test("quit remains responsive while capture startup is blocked", async () => {
                using var stop=new CancellationTokenSource(); await using var recorder=new Recorder(settings);
                var gate=(SemaphoreSlim)typeof(Recorder).GetField("lifecycle",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(recorder)!;
                await gate.WaitAsync(); var server=new ControlServer(recorder,stop); var running=server.RunAsync(stop.Token);
                Task<ControlResponse>? resume=null;
                try {
                    resume=Control.SendAsync(new("resume")); await Task.Delay(150);
                    var response=await Control.SendAsync(new("shutdown")).WaitAsync(TimeSpan.FromSeconds(2));
                    Check(response.Ok,"quit rejected");
                }
                finally {stop.Cancel();gate.Release();if(resume!=null)try{await resume;}catch(IOException){}await running;}
            });
            await Test("real pipe status configure malformed message and shutdown", async () => {
                using var stop=new CancellationTokenSource(); await using var recorder=new Recorder(settings);
                var server=new ControlServer(recorder,stop); var running=server.RunAsync(stop.Token);
                try {
                    Check((await Control.SendAsync(new("status"))).Status?.Capturing==false,"paused status wrong");
                    Check((await Control.SendAsync(new("configure",Settings:settings with {ClipSeconds=4}))).Ok,"configure failed");
                    Check((await Control.SendAsync(new("settings"))).Settings?.ClipSeconds==4,"configure did not persist");
                    Check(!(await Control.SendAsync(new("destroy"))).Ok,"unknown command accepted");
                    Check(!(await Control.SendAsync(new("save"))).Ok,"empty buffer saved");
                    Check((await Control.SendAsync(new("shutdown"))).Ok,"shutdown response failed");
                    await running.WaitAsync(TimeSpan.FromSeconds(5));
                } finally { stop.Cancel(); await running; }
            });
            string enginePath=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../build/windows/zenithclipper.exe"));
            if(OperatingSystem.IsWindows() && File.Exists(enginePath)) await Test("packaged engine accepts control from a separate process",async()=>{
                AppPaths.Save(settings with { AutoStartCapture=false });
                using var engine=MediaTools.Start(enginePath,["daemon"]);
                var stderr=engine.StandardError.ReadToEndAsync(); var stdout=engine.StandardOutput.ReadToEndAsync();
                try {
                    ControlResponse? status=null;var startup=Stopwatch.StartNew();
                    while(startup.Elapsed<TimeSpan.FromSeconds(20))
                    {
                        if(engine.HasExited)throw new Exception("Engine exited during startup: "+await stderr);
                        try{status=await Control.SendAsync(new("status"));break;}catch(Exception ex) when(ex is TimeoutException or IOException){await Task.Delay(100);}
                    }
                    Check(status?.Ok==true,"separate process status failed after startup deadline");
                    Check((await Control.SendAsync(new("shutdown"))).Ok,"separate process shutdown failed");
                    await engine.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    Check(engine.ExitCode==0,"engine exit code wrong: "+await stderr);
                }catch(Exception ex){ if(!engine.HasExited)engine.Kill(true);await engine.WaitForExitAsync();throw new Exception(ex.Message+" Child stderr: "+await stderr+" Client pipe: "+Control.PipeName,ex);}
            });
        }
        finally { Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",oldHome); Directory.Delete(root,true); }
        Console.WriteLine($"Integration: {passed} passed, {failed} failed"); return failed;
    }
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
}
