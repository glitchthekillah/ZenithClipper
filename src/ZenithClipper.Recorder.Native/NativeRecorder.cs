using ZenithClipper;
using ZenithClipper.Media;
using ZenithClipper.Native.Audio;
using ZenithClipper.Native.Video;

namespace ZenithClipper.Native;

/// <summary>Single-process native/AOT recording, bounded audio queues and a disk-backed MP4 replay ring.</summary>
public sealed class NativeRecorder(Settings initialSettings) : IRecorderEngine
{
    readonly SemaphoreSlim lifecycle=new(1),saves=new(1);
    readonly CancellationTokenSource lifetime=new();int disposing;
    readonly object startupGate=new();readonly HashSet<CancellationTokenSource> starts=[];long stopGeneration;bool desiredRunning;
    NativeVideoRecorder? video;NativeAudioMixer? audio;FragmentedReplayWriter? replay;
    AacEncoder? aac;readonly byte[] pcm=new byte[3840];long audioSamples,lastFrame=-1;
    Settings settings=initialSettings;volatile string state="Paused";volatile string? error,lastClip,notice;volatile bool capturing;
    public Settings Settings=>settings;
    public RecorderStatus Status()
    {
        ReplayMetrics? metrics=null;try{metrics=replay?.Metrics;}catch(Exception ex)when(ex is IOException or ObjectDisposedException){error=ex.Message;}
        return new(capturing,capturing?(metrics?.DurationSeconds>0?"Capturing":"Warming up"):state,error,video!=null?"NVENC native":null,metrics?.DurationSeconds??0,metrics?.TotalBytes??0,lastClip,Environment.ProcessId,notice);
    }
    public Task StartAsync(CancellationToken ct=default)=>StartAsync(ct,null);
    async Task StartAsync(CancellationToken ct,long? expectedGeneration)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposing)!=0,this);
        using var cancel=new CancellationTokenSource();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25));using var token=CancellationTokenSource.CreateLinkedTokenSource(ct,cancel.Token,deadline.Token,lifetime.Token);
        lock(startupGate){if(expectedGeneration.HasValue&&(!desiredRunning||expectedGeneration.Value!=stopGeneration))return;if(!expectedGeneration.HasValue)desiredRunning=true;starts.Add(cancel);}
        try
        {
            await lifecycle.WaitAsync(token.Token);
            try
            {
                if(capturing)return;StopInternal(true);settings.Validate();error=null;notice=null;state="Starting";
                if(settings.Encoder is not ("auto" or "nvenc"))throw new PlatformNotSupportedException("The low-memory native recorder requires Auto or NVIDIA encoding.");
                if(settings.WindowsCaptureBackend!="auto")throw new PlatformNotSupportedException("The native recorder requires the Auto Windows capture backend.");
                if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,20348))throw new PlatformNotSupportedException("Native app-audio capture requires Windows build 20348 or newer.");
                video=new(new NativeVideoConfiguration{MonitorIndex=settings.Monitor,FrameRate=settings.Fps,Quantizer=settings.Quality,OutputWidth=settings.OutputWidth,UseNv12Input=true},OnVideo);
                replay=null;
                lastFrame=-1;audioSamples=0;aac=null;
                if(settings.CaptureAudio || settings.CaptureMicrophone){audio=new(settings);await audio.StartAsync(token.Token);}
                video.WorkerFinishing+=WorkerFinishing;video.CaptureFault+=VideoFault;
                token.Token.ThrowIfCancellationRequested();video.Start();token.Token.ThrowIfCancellationRequested();capturing=true;state="Warming up";
            }
            catch(Exception ex){error=ex.Message;try{StopInternal(true);}catch{}state="Error";throw;}
            finally{lifecycle.Release();}
        }
        finally{lock(startupGate)starts.Remove(cancel);}
    }
    void VideoFault(Exception failure){error=failure.Message;capturing=false;state="Error";}
    void OnVideo(ReadOnlySpan<byte> packet,long index,bool key)
    {
        if(index==0)
        {
            audio?.BeginEpoch();if(audio!=null)aac=new AacEncoder(OnAudio);
            replay=new(AppPaths.Buffer,new VideoTrackConfig(video!.Width,video.Height,video.FrameRate),
                new AudioTrackConfig(EncoderDelaySamples:0),settings.BufferSeconds,
                includeAudio:audio!=null,maxBytes:settings.MaxBufferMiB*1024*1024);
        }
        var writer=replay??throw new InvalidOperationException("Replay writer is unavailable.");
        writer.AddVideo(packet,index,key);lastFrame=index;
        if(aac!=null)
        {
            long target=(index+1)*48000/video!.FrameRate;
            while(audioSamples+960<=target){audio!.Read20Milliseconds(pcm);aac.Encode(pcm,audioSamples);audioSamples+=960;}
        }
    }
    void OnAudio(ReadOnlySpan<byte> packet,long index,int duration)=>replay!.AddAudio(packet,index,duration);
    void WorkerFinishing()
    {
        try
        {
            if(aac!=null)
            {
                long target=(lastFrame+1)*48000/video!.FrameRate;
                if(audioSamples<target){audio!.Read20Milliseconds(pcm);int frames=(int)Math.Min(960,target-audioSamples);aac.Encode(pcm.AsSpan(0,frames*4),audioSamples);audioSamples+=frames;}
                aac.Complete();
            }
            replay?.Finish();
        }
        finally{aac?.Dispose();aac=null;}
    }
    public async Task TickAsync(CancellationToken ct)
    {
        bool restart=false;long generation;await lifecycle.WaitAsync(ct);lock(startupGate)generation=stopGeneration;
        try
        {
            if(video==null)return;
            if(video.Fault!=null||audio?.Faulted==true){error=video.Fault?.Message??"Audio capture ended; resume to retry.";StopInternal(false);state="Error";return;}
            if(audio!=null&&await audio.RefreshAsync(ct)){StopInternal(true);state="Audio app changed; rebuilding safe buffer";notice="An excluded app entered a captured tree. Previous history was cleared.";restart=true;return;}
            var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(AppPaths.Buffer))!);if(drive.AvailableFreeSpace<128L*1024*1024){error="Low free disk space; recording stopped.";StopInternal(false);state="Error";}
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
        {error=ex.Message;StopInternal(true);state="Error";}
        finally
        {
            lifecycle.Release();
            if(restart)try{await StartAsync(ct,generation);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or TimeoutException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception){error=ex.Message;state="Error";}
        }
    }
    public async Task<string> SaveAsync(int? seconds=null,CancellationToken ct=default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposing)!=0,this);using var operation=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.Token);ct=operation.Token;
        if(!await saves.WaitAsync(0,ct))throw new IOException("A clip is already being saved.");
        try
        {
            int duration=seconds??settings.ClipSeconds;if(duration<1||duration>settings.BufferSeconds)throw new ArgumentException("Clip duration must fit in the replay buffer.");
            await TickAsync(ct);FragmentedReplayWriter writer;NativeVideoRecorder? active;long requestedFrame;
            await lifecycle.WaitAsync(ct);
            try
            {
                if(video!=null&&!capturing)throw new IOException("Native recording has not finished stopping; no clip can be exported yet.");
                writer=replay??throw new IOException("No replay history is available.");active=video;requestedFrame=Volatile.Read(ref lastFrame)+1;
                if(active!=null&&writer.CompletedThroughVideoFrame<requestedFrame)active.RequestKeyFrame();
            }
            finally{lifecycle.Release();}
            if(active!=null)
            {
                var checkpoint=System.Diagnostics.Stopwatch.StartNew();
                while(writer.CompletedThroughVideoFrame<requestedFrame)
                {
                    ct.ThrowIfCancellationRequested();
                    if(active.Fault is { } failure)throw new IOException("Recording ended before the clip could include the requested moment.",failure);
                    if(checkpoint.Elapsed>TimeSpan.FromSeconds(2))throw new IOException("The current clip fragment did not finish in time; try saving again.");
                    await Task.Delay(5,ct);
                }
            }
            Directory.CreateDirectory(settings.ClipDirectory);string destination=Path.Combine(settings.ClipDirectory,$"Clip-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}-{Guid.NewGuid().ToString("N")[..6]}.mp4");
            ReplayExport result=await Task.Run(()=>writer.ExportNewest(destination,duration,ct),ct);lastClip = result.Path;
            ClipSaveSound.Play(settings);
            return result.Path;
        }
        finally{saves.Release();}
    }
    public async Task StopAsync()
    {
        lock(startupGate){desiredRunning=false;stopGeneration++;foreach(var pending in starts)pending.Cancel();}await lifecycle.WaitAsync();
        try
        {
            bool clear=false;if(audio!=null)try{clear=audio.RequiresHistoryReset();}catch{clear=true;}
            StopInternal(clear);state="Paused";
        }
        finally{lifecycle.Release();}
    }
    void StopInternal(bool clear)
    {
        capturing=false;var closing=video;
        try{closing?.Stop();}
        catch(Exception ex)
        {
            // A timed-out native worker can still call OnVideo/WorkerFinishing. Keep its session
            // dependencies alive and reject replacement until a later Stop confirms it has joined.
            error="Native recording is still stopping: "+ex.Message;state="Error";
            throw new IOException(error,ex);
        }
        Exception? failure=closing?.Fault;if(failure!=null){clear=true;error=failure.Message;state="Error";}
        try{closing?.Dispose();}catch(Exception ex){failure??=ex;}finally{video=null;}
        if(!clear&&audio!=null)try{clear=audio.RequiresHistoryReset();}catch{clear=true;}
        try{audio?.Dispose();}catch(Exception ex){failure??=ex;}finally{audio=null;}
        if(clear){try{replay?.Dispose();}catch(Exception ex){failure??=ex;}finally{replay=null;}}
        if(failure!=null)throw new IOException("Recorder cleanup failed: "+failure.Message,failure);
    }
    public async Task UpdateAsync(Settings value,CancellationToken ct)
    {
        value.Validate();if(value.Encoder is not ("auto" or "nvenc")||value.WindowsCaptureBackend!="auto")throw new NotSupportedException("This running recorder uses native NVIDIA capture. Stop and relaunch ZenithClipper with the requested encoder/backend; current settings and recording have been preserved.");
        // Microphone on/off and device choices only rebuild the audio mixer, so they apply live while video and the
        // replay history keep running (same path as per-app volume changes).
        bool audioRouting=value.CaptureMicrophone!=settings.CaptureMicrophone||value.WindowsInputDevice!=settings.WindowsInputDevice||value.WindowsOutputDevice!=settings.WindowsOutputDevice;
        bool liveAudio=audioRouting&&audio!=null&&(value.CaptureAudio||value.CaptureMicrophone);
        var compared=liveAudio?settings with{CaptureMicrophone=value.CaptureMicrophone,WindowsInputDevice=value.WindowsInputDevice,WindowsOutputDevice=value.WindowsOutputDevice}:settings;
        if(!SettingsChanges.RequiresRestart(compared,value))
        {
            await lifecycle.WaitAsync(ct);
            try
            {
                replay?.SetRetention(value.BufferSeconds,value.MaxBufferMiB*1024*1024);
                // Per-app levels apply live. Moving between whole-system and per-app capture swaps the mixer in place;
                // video and the replay history keep running.
                if(audio is {} current&&(liveAudio||!current.SetVolumes(value.AppVolumes)))
                {
                    var next=new NativeAudioMixer(value);
                    try{await next.StartAsync(ct);}catch{next.Dispose();throw;}
                    next.BeginEpoch();Interlocked.Exchange(ref audio,next)?.Dispose();
                }
                AppPaths.Save(value);settings=value;
            }
            finally{lifecycle.Release();}
            return;
        }
        bool active=capturing;await StopAsync();AppPaths.Save(value);settings=value;if(active)await StartAsync(ct);
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposing,1)!=0)return;lifetime.Cancel();Exception? failure=null;
        try{await StopAsync();}catch(Exception ex){failure=ex;}
        if(video==null){try{replay?.Dispose();}catch(Exception ex){failure??=ex;}finally{replay=null;}}
        await saves.WaitAsync();lifecycle.Dispose();saves.Dispose();lifetime.Dispose();
        if(failure!=null)throw new IOException("Recorder disposal failed: "+failure.Message,failure);
    }
}








