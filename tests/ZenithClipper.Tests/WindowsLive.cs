using ZenithClipper;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

[SupportedOSPlatform("windows")]
static class WindowsLive
{
    public static async Task<int> RunAsync(string ffmpeg)
    {
        string root = Path.Combine(Path.GetTempPath(), "ZenithClipper-live-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string? oldHome = Environment.GetEnvironmentVariable("ZENITHCLIPPER_HOME"); Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME", Path.Combine(root,"home"));
        int failed = 0;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
            try
            {
                var s = new Settings { FfmpegPath=ffmpeg, Fps=60, ClipSeconds=3, BufferSeconds=10, ClipDirectory=Path.Combine(root,"clips"), CaptureAudio=true };
                await using var recorder=new Recorder(s);
                await recorder.StartAsync(timeout.Token);
                for(int i=0;i<8;i++) { await Task.Delay(1000,timeout.Token); await recorder.TickAsync(timeout.Token); Console.WriteLine("LIVE " + JsonSerializer.Serialize(recorder.Status())); }
                var status=recorder.Status(); if(!status.Capturing || status.BufferedSeconds<3) throw new Exception("No usable live buffer: "+status.Error);
                string clip=await recorder.SaveAsync(3,timeout.Token);
                await recorder.StopAsync();
                await MediaTools.RunAsync(ffmpeg,["-v","error","-xerror","-threads","1","-filter_threads","1","-i",clip,"-f","null","-"],timeout.Token);
                using var probe=JsonDocument.Parse(await MediaTools.ProbeAsync(ffmpeg,clip));
                var video=probe.RootElement.GetProperty("streams").EnumerateArray().First(x=>x.GetProperty("codec_type").GetString()=="video");
                Console.WriteLine($"PASS live hardware desktop + audio: {video.GetProperty("width")}x{video.GetProperty("height")} {video.GetProperty("r_frame_rate")} encoder={status.Encoder}");
                Console.WriteLine("Live clip bytes: "+new FileInfo(clip).Length);
            }
            catch(Exception ex) { failed++; Console.WriteLine("FAIL live capture: "+ex); }
            try { await AudioExclusionAsync(ffmpeg,root,timeout.Token); }
            catch(Exception ex) { failed++; Console.WriteLine("FAIL live audio exclusion: "+ex); }
        }
        finally { Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",oldHome); try { Directory.Delete(root,true); } catch(IOException) { } }
        return failed;
    }
    static async Task AudioExclusionAsync(string ffmpeg,string root,CancellationToken ct)
    {
        string ffplay=Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffplay.exe");
        if(!File.Exists(ffplay))
        {
            string tools=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../tools/ffmpeg-package"));
            if(!Directory.Exists(tools))tools="C:/Users/szajf/My Projects/ZenithClipper/tools/ffmpeg-package";
            ffplay=Directory.EnumerateFiles(tools,"ffplay.exe",SearchOption.AllDirectories).First();
        }
        string excluded=Path.Combine(root,"ZenithClipperExcluded.exe"); File.Copy(ffplay,excluded);
        string includedTone=Path.Combine(root,"included.wav"), excludedTone=Path.Combine(root,"excluded.wav");
        foreach(var (frequency,path) in new[]{(440,includedTone),(880,excludedTone)})
            await MediaTools.RunAsync(ffmpeg,["-v","error","-y","-f","lavfi","-i",$"sine=frequency={frequency}:sample_rate=48000","-af","volume=0.12","-t","12",path],ct);
        using var included=MediaTools.Start(ffplay,["-nodisp","-autoexit","-loglevel","error",includedTone]);
        using var unwanted=MediaTools.Start(excluded,["-nodisp","-autoexit","-loglevel","error",excludedTone]);
        try
        {
            await Task.Delay(1000,ct);
            Console.WriteLine($"AUDIO test players: included={included.Id}, excluded={unwanted.Id}");
            foreach (var pid in WindowsSessions.IncludedPids(["ZenithClipperExcluded"])) { using var process=Process.GetProcessById(pid); Console.WriteLine($"AUDIO allowed {pid} {process.ProcessName}"); }
            var otherApps=WindowsSessions.IncludedPids([]).Where(pid=>pid!=Environment.ProcessId && pid!=included.Id).Select(pid=> { using var p=Process.GetProcessById(pid);return p.ProcessName; }).Append("ZenithClipperExcluded").Distinct().ToArray();
            await using var mixer=new WindowsAudioMixer(new Settings {ExcludedApps=otherApps});
            await mixer.StartAsync(ct);
            string pcm=Path.Combine(root,"mixed.pcm");
            await MediaTools.RunAsync(ffmpeg,["-v","error","-y","-f","s16le","-ar","48000","-ac","2","-i",mixer.Source,"-t","4","-c:a","pcm_s16le","-f","s16le",pcm],ct);
            byte[] bytes=File.ReadAllBytes(pcm); double allowed=Amplitude(bytes,440),blocked=Amplitude(bytes,880);
            if(allowed<.0001) throw new Exception($"Included tone not audible in capture ({allowed}); playback/capture device may be unavailable.");
            if(blocked/allowed>.08) throw new Exception($"Excluded tone leaked: allowed={allowed}, blocked={blocked}, ratio {blocked/allowed}");
            Console.WriteLine($"PASS live audio exclusion: included 440Hz={allowed:F6}, excluded 880Hz={blocked:F6}, ratio={blocked/allowed:F6}");
        }
        finally { foreach(var player in new[]{included,unwanted}) { if(!player.HasExited) player.Kill(true); await player.WaitForExitAsync(); } }
    }
    static double Amplitude(byte[] bytes,double hz)
    {
        var samples=MemoryMarshal.Cast<byte,short>(bytes); int start=48000/2,end=Math.Min(samples.Length/2,start+48000*2); double amplitude=0; int windows=0;
        for(int window=start;window+4800<=end;window+=4800) {
            double re=0,im=0;
            for(int i=window;i<window+4800;i++) { double value=samples[i*2]/32768.0,angle=2*Math.PI*hz*i/48000; re+=value*Math.Cos(angle); im+=value*Math.Sin(angle); }
            amplitude+=Math.Sqrt(re*re+im*im)*2/4800; windows++;
        }
        return amplitude/windows;
    }
}
