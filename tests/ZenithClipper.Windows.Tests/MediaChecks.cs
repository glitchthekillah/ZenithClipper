using ZenithClipper;
using ZenithClipper.Windows.Media;

static class MediaChecks
{
    public static async Task<int> RunAsync(string ffmpeg)
    {
        string root=Path.Combine(Path.GetTempPath(),"ZenithClipper-ui-media-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string? old=Environment.GetEnvironmentVariable("ZENITHCLIPPER_HOME");Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",root);
        try
        {
            var s=new Settings{FfmpegPath=ffmpeg};string video=Path.Combine(root,"fixture.mp4");
            await MediaTools.RunAsync(ffmpeg,["-v","error","-f","lavfi","-i","testsrc2=s=160x90:r=15","-t","1","-threads","1","-c:v","libx264","-preset","ultrafast",video]);
            var first=await Frames.InfoAsync(s,video);
            if(first.Width!=160 || first.Height!=90)throw new Exception("Media probe dimensions incorrect.");
            string cache=Directory.GetFiles(Frames.CacheDir,"*.json").Single();await File.WriteAllTextAsync(cache,"null");
            var recovered=await Frames.InfoAsync(s,video);
            if(recovered==null || recovered.Width!=160)throw new Exception("Null thumbnail metadata cache was not regenerated.");
            using var bitmap=await Frames.GrabAsync(s,video,.25,80,44);
            if(bitmap?.Width!=80 || bitmap.Height!=44)throw new Exception("Frame decode failed.");
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            try{using var rejected=await Frames.GrabAsync(s,video,0,80,44,cancel.Token);throw new Exception("Cancelled decode accepted.");}catch(OperationCanceledException){}
            using var next=await Frames.GrabAsync(s,video,.5,80,44);
            if(next==null)throw new Exception("Cancelled request leaked the decode slot.");
            Console.WriteLine("PASS UI metadata cache recovery, frame decode and cancelled request cleanup");return 0;
        }
        catch(Exception ex){Console.WriteLine("FAIL UI media: "+ex);return 1;}
        finally{Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",old);Directory.Delete(root,true);}
    }
}
