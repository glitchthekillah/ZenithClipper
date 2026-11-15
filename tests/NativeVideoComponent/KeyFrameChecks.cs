using ZenithClipper.Native.Video;

static class KeyFrameChecks
{
    static void Require(bool value,string message) { if(!value)throw new Exception(message); }
    public static int Run(bool actual)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"keyframe-request.h264");
        using var output=actual?File.Create(path):null;using var finished=new ManualResetEventSlim();
        NativeVideoRecorder? recorder=null;long packets=0;var keys=new List<long>();int finishing=0;
        long[] requests=actual?[250,330]:[20,21];long last=actual?380:90;
        VideoPacketHandler handler=(ReadOnlySpan<byte> packet,long index,bool key)=>
        {
            Require(index==packets++,"Keyframe requests interrupted frame indexes.");
            output?.Write(packet);if(key)keys.Add(index);
            if(requests.Contains(index))recorder!.RequestKeyFrame();
            if(index==last)recorder!.Stop();
        };
        recorder=actual?new(new NativeVideoConfiguration{UseNv12Input=true,Quantizer=20},handler)
            :new(new NativeVideoConfiguration{UseNv12Input=true,FrameRate=165},handler,(2560,1600,165),6,TimeSpan.FromSeconds(10));
        using(recorder)
        {
            try{recorder.RequestKeyFrame();throw new Exception("An unstarted recorder accepted a keyframe request.");}catch(InvalidOperationException){}
            recorder.WorkerFinishing+=()=>{finishing++;finished.Set();};recorder.Start();
            Require(finished.Wait(TimeSpan.FromSeconds(10)),"Requested keyframe capture did not finish.");recorder.Stop();
            Require(recorder.Fault==null,"Keyframe request failed: "+recorder.Fault);
            Require(finishing==1&&packets==last+1,"Keyframe request changed frame count or finishing lifetime.");
            foreach(long requested in requests)Require(keys.Contains(requested+1),"Next frame was not an IDR after request at "+requested);
            if(actual)Require(keys.Contains(330),"Automatic two-second IDR cadence changed.");
            Require(keys[0]==0,"Initial IDR changed.");
            try{recorder.RequestKeyFrame();throw new Exception("A stopped recorder accepted a keyframe request.");}catch(InvalidOperationException){}
            Console.WriteLine($"PASS {(actual?"actual":"fake")} keyframe request: packets={packets} keys={string.Join(',',keys)} resolution={recorder.Width}x{recorder.Height} fps={recorder.FrameRate} output={(actual?path:"none")}");
        }
        try{recorder.RequestKeyFrame();throw new Exception("Disposed recorder accepted a keyframe request.");}catch(ObjectDisposedException){}
        if(!actual)
        {
            using(var quarantine=new NativeVideoRecorder(new NativeVideoConfiguration(),(ReadOnlySpan<byte> _,long _,bool _)=>{},(2560,1600,165),4,TimeSpan.FromMilliseconds(30)))
            {
                quarantine.Start();quarantine.RequestKeyFrame();
                try{quarantine.Stop();throw new Exception("IDR control bit incorrectly released a quarantined fake worker.");}catch(TimeoutException){}
                Require(quarantine.TestReferences.Alive==1,"Keyframe request invalidated timeout quarantine.");
                quarantine.ReleaseTestWorker();quarantine.Stop();
                Require((quarantine.TestControlFlags&3)==3,"Release/keyframe control bits clobbered each other.");
            }
            for(int cycle=0;cycle<20;cycle++)
            {
                using var racing=new NativeVideoRecorder(new NativeVideoConfiguration(),(ReadOnlySpan<byte> _,long _,bool _)=>{},(2560,1600,165),6,TimeSpan.FromSeconds(10));
                racing.Start();Exception? failure=null;
                Parallel.Invoke(()=>{for(int request=0;request<100;request++)try{racing.RequestKeyFrame();}catch(Exception error)when(error is InvalidOperationException or ObjectDisposedException){}catch(Exception error){failure=error;}},racing.Dispose);
                Require(failure==null&&!racing.IsRunning,"Concurrent keyframe/disposal failed: "+failure);
            }
            Console.WriteLine("PASS keyframe flags preserve timeout-release bit and race safely with Stop/Dispose");
        }
        return 0;
    }
}
