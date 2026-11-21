using System.Diagnostics;
using System.Runtime.InteropServices;
using ZenithClipper.Editing;
namespace ZenithClipper.Mlt;

public sealed record PreviewFrame(long Position,int Width,int Height,byte[] Rgba)
{
    public byte[] AudioPcm {get;init;}=[];
}
public sealed class MltSession : IDisposable,IAsyncDisposable
{
    readonly SemaphoreSlim gate=new(1),exportGate=new(1);
    readonly object playSync=new();
    CancellationTokenSource? playCancel;
    Task? playback;
    nint profile,producer;
    string? directory;
    FrameRate fps=new(30,1);
    int width,height;
    long nextDecodedFrame = -1;
    readonly CancellationTokenSource lifetime=new();
    readonly TaskCompletionSource disposal=new(TaskCreationOptions.RunContinuationsAsynchronously);
    int disposalState;
    void ThrowIfDisposed()=>ObjectDisposedException.ThrowIf(Volatile.Read(ref disposalState)!=0,this);
    public long Position {get;private set;}
    public long Duration {get;private set;}
    public string? AudioError {get;private set;}
    public static string RuntimeVersion {get{Native.Initialize();return Marshal.PtrToStringUTF8(Native.Version())??"unknown";}}

    public async Task LoadAsync(EditProject project,int previewWidth=960,CancellationToken ct=default)
    {
        ThrowIfDisposed();ct.ThrowIfCancellationRequested();await PauseAsync().ConfigureAwait(false);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await Task.Run(()=>
            {
                ct.ThrowIfCancellationRequested();Native.Initialize();CloseGraph();
                var seq=MltXml.Sequence(project);fps=seq.Fps;Duration=seq.Duration;Position=0;
                double scale=Math.Min(1,Math.Min(Math.Clamp(previewWidth,160,1920)/(double)seq.Width,1080d/seq.Height));
                width=Math.Max(2,(int)Math.Round(seq.Width*scale)&~1);height=Math.Max(2,(int)Math.Round(seq.Height*scale)&~1);
                directory=Path.Combine(Path.GetTempPath(),"zenith-mlt-preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
                string xml=Path.Combine(directory,"timeline.mlt");MltXml.Compile(project,width,height).Save(xml);
                profile=Native.Profile(MltXml.ProfileText(width,height,fps));if(profile==0)throw new IOException("MLT preview profile could not be created.");
                producer=Native.Producer(profile,"xml",xml);if(producer==0)throw new IOException("MLT could not load this sequence.");
                Native.Set(Native.ProducerProperties(producer),"eof","pause");
                ct.ThrowIfCancellationRequested();
            },ct).ConfigureAwait(false);
        }
        catch{CloseGraph();throw;}
        finally{gate.Release();}
    }
    public async Task<PreviewFrame> GetFrameAsync(long projectFrame,CancellationToken ct=default)
    {
        ThrowIfDisposed();await gate.WaitAsync(ct).ConfigureAwait(false);
        try{ThrowIfDisposed();return await Task.Run(()=>ReadFrame(projectFrame,true,true),ct).ConfigureAwait(false);}
        finally{gate.Release();}
    }
    PreviewFrame ReadFrame(long frameNumber,bool image,bool audio)
    {
        if(producer==0)throw new InvalidOperationException("Open a project sequence first.");
        long position=Math.Clamp(frameNumber,0,Math.Max(0,Duration-1));
        if (nextDecodedFrame != position)
            Native.Seek(producer, checked((int)position));
        nextDecodedFrame = -1;
        if(Native.GetFrame(Native.ProducerService(producer),out nint frame,0)!=0 || frame==0)throw new IOException("MLT could not decode this frame.");
        try
        {
            byte[] rgba=[];int w=width,h=height;
            if(image)
            {
                int format=2;
                if(Native.Image(frame,out nint pixels,ref format,ref w,ref h,0)!=0 || pixels==0 || format!=2)throw new IOException("MLT did not provide an RGBA preview frame.");
                if(w<1 || h<1 || w>4096 || h>4096)throw new IOException("Preview dimensions exceed the bounded image buffer.");
                rgba=new byte[checked(w*h*4)];Marshal.Copy(pixels,rgba,0,rgba.Length);
            }
            byte[] pcm=[];
            if(audio)
            {
                int format=1,frequency=48000,channels=2;
                int samples=(int)Math.Max(1,Math.Floor((position+1)*48000/fps.Value)-Math.Floor(position*48000/fps.Value));
                // The validated minimum rate is 0.1 fps: at most 10 seconds / 480,000 samples
                // (1.92 MB of 48 kHz stereo s16 PCM) belong to one timeline frame.
                if(Native.Audio(frame,out nint buffer,ref format,ref frequency,ref channels,ref samples)==0 && buffer!=0 && format==1 && channels==2 && frequency==48000 && samples is >0 and <=480000)
                {pcm=new byte[checked(samples*channels*2)];Marshal.Copy(buffer,pcm,0,pcm.Length);}
            }
            Position=position; nextDecodedFrame = position + 1;
            return new(position,w,h,rgba){AudioPcm=pcm};
        }
        finally{Native.FrameClose(frame);}
    }
    int peakLeft,peakRight;
    /// <summary>Program loudness (LUFS) of what has played since the last reset.</summary>
    public LoudnessMeter Loudness {get;}=new();
    /// <summary>Highest sample level (0..1) per channel since the last call; feeds the audio meters during playback.</summary>
    public (float Left,float Right) TakePeak()=>(Interlocked.Exchange(ref peakLeft,0)/32767f,Interlocked.Exchange(ref peakRight,0)/32767f);
    void TrackPeak(byte[] pcm)
    {
        int l=0,r=0;
        for(int i=0;i+3<pcm.Length;i+=4){int a=Math.Abs((int)(short)(pcm[i]|pcm[i+1]<<8)),b=Math.Abs((int)(short)(pcm[i+2]|pcm[i+3]<<8));if(a>l)l=a;if(b>r)r=b;}
        int seen;do{seen=Volatile.Read(ref peakLeft);}while(l>seen&&Interlocked.CompareExchange(ref peakLeft,l,seen)!=seen);
        do{seen=Volatile.Read(ref peakRight);}while(r>seen&&Interlocked.CompareExchange(ref peakRight,r,seen)!=seen);
    }
    public Task PlayAsync(Action<PreviewFrame> onFrame,CancellationToken ct=default)
    {
        ThrowIfDisposed();
        lock(playSync)
        {
            ThrowIfDisposed();
            if(playback is {IsCompleted:false})return playback;
            playCancel?.Dispose();playCancel=CancellationTokenSource.CreateLinkedTokenSource(ct);var token=playCancel.Token;
            playback=Task.Run(async()=>
            {
                if(producer==0)throw new InvalidOperationException("Open a sequence before playing.");
                AudioDevice? audio=null;AudioError=null;
                try
                {
                    try{audio=new();}catch(Exception ex) when(ex is IOException or DllNotFoundException){AudioError=ex.Message;}
                    long start=Position>=Duration-1?0:Position;double nextImage=start;var clock=Stopwatch.StartNew();
                    for(long p=start;p<Duration;p++)
                    {
                        token.ThrowIfCancellationRequested();bool show=p>=nextImage;
                        await gate.WaitAsync(token).ConfigureAwait(false);PreviewFrame decoded;
                        try{decoded=ReadFrame(p,show,audio!=null);}finally{gate.Release();}
                        // Backpressure leaves at most 100 ms queued before adding one bounded frame.
                        if(audio!=null){audio.Queue(decoded.AudioPcm);TrackPeak(decoded.AudioPcm);Loudness.Add(decoded.AudioPcm);}
                        if(show){onFrame(decoded);nextImage=p+Math.Max(1,fps.Value/30);}
                        while(audio?.QueuedBytes>19200){await Task.Delay(5,token).ConfigureAwait(false);}
                        double wait=(p-start+1)*1000/fps.Value-clock.Elapsed.TotalMilliseconds;
                        if(wait>1)await Task.Delay(TimeSpan.FromMilliseconds(wait),token).ConfigureAwait(false);
                    }
                    while(audio?.QueuedBytes>0)await Task.Delay(5,token).ConfigureAwait(false);
                }
                finally{audio?.Dispose();}
            },token);
            return playback;
        }
    }
    public async Task PauseAsync()
    {
        Task? work;lock(playSync){playCancel?.Cancel();work=playback;}
        try{if(work!=null)await work.ConfigureAwait(false);}
        catch(OperationCanceledException){}
        catch(Exception ex){AudioError="Playback stopped: "+ex.Message;}
        finally{lock(playSync){if(ReferenceEquals(playback,work)){playback=null;playCancel?.Dispose();playCancel=null;}}}
    }
    public Task ExportAsync(EditProject project,string output,IProgress<double>? progress=null,CancellationToken ct=default)=>ExportAsync(project,output,new ExportOptions(ExportPresets.All[1]),progress,ct);
    public async Task ExportAsync(EditProject project,string output,ExportOptions options,IProgress<double>? progress=null,CancellationToken ct=default)
    {
        ThrowIfDisposed();using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.Token);
        await Task.Run(()=>ExportCoreAsync(project,output,options,progress,linked.Token),linked.Token).ConfigureAwait(false);
    }
    async Task ExportCoreAsync(EditProject project,string output,ExportOptions options,IProgress<double>? progress,CancellationToken ct)
    {
        ThrowIfDisposed();ct.ThrowIfCancellationRequested();
        string target=Path.GetFullPath(output);
        if(project.Media.Where(m=>!string.IsNullOrWhiteSpace(m.Path)).Any(m=>Path.GetFullPath(m.Path).Equals(target,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)))throw new IOException("Export must not replace source media.");
        if(string.IsNullOrWhiteSpace(Path.GetFileName(target)))throw new ArgumentException("Choose an export filename.");
        if(File.Exists(target))throw new IOException("Choose a new export filename.");
        await PauseAsync().ConfigureAwait(false);await exportGate.WaitAsync(ct).ConfigureAwait(false);
        string work=Path.Combine(Path.GetTempPath(),"zenith-mlt-export-"+Guid.NewGuid().ToString("N"));

        string temp=Path.Combine(Path.GetDirectoryName(target)!,".zenith-render-"+Guid.NewGuid().ToString("N")+"."+options.Preset.Extension);
        nint outProfile=0,outProducer=0,consumer=0;int fatal=0;
        Native.EventListener listener=(_,_,_)=>Interlocked.Exchange(ref fatal,1);
        try
        {
            ThrowIfDisposed();ct.ThrowIfCancellationRequested();Directory.CreateDirectory(work);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Native.Initialize();var seq=MltXml.Sequence(project);if(seq.Duration<1)throw new ArgumentException("Add clips to the sequence before exporting.");
            string xml=Path.Combine(work,"timeline.mlt");MltXml.Compile(project).Save(xml);
            outProfile=Native.Profile(MltXml.ProfileText(seq.Width,seq.Height,seq.Fps));
            outProducer=Native.Producer(outProfile,"xml",xml);if(outProducer==0)throw new IOException("MLT could not load the export sequence.");
            consumer=Native.Consumer(outProfile,"avformat",temp);if(consumer==0)throw new IOException("MLT's avformat encoder is unavailable.");
            var props=Native.ConsumerProperties(consumer);
            Native.Listen(props,0,"consumer-fatal-error",listener);
            long first=Math.Clamp(options.In??0,0,seq.Duration-1),final=Math.Clamp((options.Out??seq.Duration)-1,first,seq.Duration-1);
            // The export range belongs on the producer: the avformat consumer ignores in/out, so it rendered the whole timeline.
            Native.SetInOut(outProducer,(int)first,(int)final);
            var settings=new List<(string,string)>{("threads","2"),("real_time","-2"),("terminate_on_pause","1")};
            settings.AddRange(ExportPresets.Resolve(options.Preset,seq.Fps.Seconds(final-first+1),seq.Width,seq.Height));
            foreach(var (key,value) in settings)Native.Set(props,key,value);
            if(Native.Connect(consumer,Native.ProducerService(outProducer))!=0 || Native.Start(consumer)!=0)throw new IOException("MLT export could not start.");
            var lastProgress=Stopwatch.StartNew();int last=-1;
            while(Native.IsStopped(consumer)==0)
            {
                ct.ThrowIfCancellationRequested();int pos=Native.ConsumerPosition(consumer);
                progress?.Report(Math.Clamp((pos+1-first)/(double)(final-first+1),0,1));
                if(pos!=last){last=pos;lastProgress.Restart();}
                if(lastProgress.Elapsed>TimeSpan.FromMinutes(2))throw new IOException("Export stopped making progress.");
                await Task.Delay(50,ct).ConfigureAwait(false);
            }
            int finalPosition=Native.ConsumerPosition(consumer);Native.Stop(consumer);
            if(Volatile.Read(ref fatal)!=0 || finalPosition<final)throw new IOException("MLT could not finish encoding the complete sequence.");
            ct.ThrowIfCancellationRequested();Native.ConsumerClose(consumer);consumer=0;
            if(!File.Exists(temp) || new FileInfo(temp).Length<256)throw new IOException("MLT produced no usable export.");
            File.Move(temp,target,false);progress?.Report(1);
        }
        finally
        {
            try
            {
                if(consumer!=0){Native.Stop(consumer);Native.ConsumerClose(consumer);}
                GC.KeepAlive(listener);
                if(outProducer!=0)Native.ProducerClose(outProducer);if(outProfile!=0)Native.ProfileClose(outProfile);
                if(File.Exists(temp))File.Delete(temp);try{Directory.Delete(work,true);}catch(IOException){}
            }
            finally{exportGate.Release();}
        }
    }
    void CloseGraph()
    {
        nextDecodedFrame = -1;
        if(producer!=0){Native.ProducerClose(producer);producer=0;}if(profile!=0){Native.ProfileClose(profile);profile=0;}
        if(directory!=null){try{Directory.Delete(directory,true);}catch(IOException){}directory=null;}
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.CompareExchange(ref disposalState,1,0)!=0){await disposal.Task.ConfigureAwait(false);return;}
        lifetime.Cancel();
        try
        {
            await PauseAsync().ConfigureAwait(false);await exportGate.WaitAsync().ConfigureAwait(false);
            try{await gate.WaitAsync().ConfigureAwait(false);try{CloseGraph();}finally{gate.Release();}}
            finally{exportGate.Release();}
            playCancel?.Dispose();lifetime.Dispose();disposal.TrySetResult();
        }
        catch(Exception ex){disposal.TrySetException(ex);throw;}
    }
    public void Dispose()=>DisposeAsync().AsTask().GetAwaiter().GetResult();
}




