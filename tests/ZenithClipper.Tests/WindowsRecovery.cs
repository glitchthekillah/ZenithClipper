using ZenithClipper;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;

[SupportedOSPlatform("windows")]
static class WindowsRecovery
{
    public static async Task<int> RunAsync(string ffmpeg,bool native=false)
    {
        string root=Path.Combine(Path.GetTempPath(),"ZenithClipper-recovery-"+Guid.NewGuid().ToString("N"));
        string? old=Environment.GetEnvironmentVariable("ZENITHCLIPPER_HOME");
        Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",root);
        Directory.CreateDirectory(root);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            var s=new Settings { Encoder="auto", FfmpegPath=ffmpeg, Fps=native?0:30, OutputWidth=native?0:1280, WindowsCaptureBackend=native?"auto":"desktop-duplication", ClipSeconds=2, BufferSeconds=8, ExcludedApps=["Spotify"], ClipDirectory=Path.Combine(root,"clips") };
            // FFprobe exits immediately when given capture arguments, after the audio sources have opened.
            var broken=s with { Encoder="amf", FfmpegPath=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe.exe") };
            await using(var recorder=new Recorder(broken))
            {
                var elapsed=Stopwatch.StartNew();
                try { await recorder.StartAsync(timeout.Token); await Task.Delay(1000,timeout.Token); await recorder.TickAsync(timeout.Token); }
                catch(IOException) { }
                if(recorder.Status().Capturing) throw new Exception("Failed capture still marked active.");
                await recorder.StopAsync().WaitAsync(TimeSpan.FromSeconds(6));
                Console.WriteLine($"PASS failed startup and audio cleanup: {elapsed.Elapsed.TotalSeconds:F2}s");
            }
            await using(var recorder=new Recorder(s))
            {
                for(int cycle=0;cycle<(native?1:2);cycle++)
                {
                    await recorder.StartAsync(timeout.Token);
                    for(int i=0;i<(native?1:4);i++){await Task.Delay(1000,timeout.Token);await recorder.TickAsync(timeout.Token);}
                    var status=recorder.Status();
                    if(!status.Capturing || status.BufferedSeconds<2)throw new Exception("Native capture failed: "+status.Error);
                    string clip=await recorder.SaveAsync(2,timeout.Token);
                    await recorder.StopAsync().WaitAsync(TimeSpan.FromSeconds(6));
                    await MediaTools.RunAsync(ffmpeg,["-v","error","-xerror","-threads","1","-filter_threads","1","-i",clip,"-f","null","-"],timeout.Token);
                    using var probe=JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,clip));
                    var video=probe.RootElement.GetProperty("streams").EnumerateArray().First(x=>x.GetProperty("codec_type").GetString()=="video");
                    string fps=video.GetProperty("r_frame_rate").GetString()!;
                    
                    string[] average=video.GetProperty("avg_frame_rate").GetString()!.Split('/'); double actual=double.Parse(average[0])/double.Parse(average[1]);
                    if(Math.Abs(actual-(native?WindowsDisplay.RefreshRate(0):30))>1)throw new Exception("Expected FPS lost: "+actual);
                    Console.WriteLine($"PASS capture save/decode/stop cycle {cycle+1}: {video.GetProperty("width")}x{video.GetProperty("height")} {actual:F2} FPS average, Spotify excluded; {status.Notice}");
                }
            }
            return 0;
        }
        catch(Exception ex){Console.WriteLine("FAIL recovery: "+ex);return 1;}
        finally {Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",old);try{Directory.Delete(root,true);}catch(IOException){} }
    }
}