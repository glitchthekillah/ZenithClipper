using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using ZenithClipper;
namespace ZenithClipper.Native.Audio
{
 [SupportedOSPlatform("windows")]
 public sealed class ProcessLoopbackCapture:IDisposable
 {
  readonly CancellationTokenSource stop=new();readonly Task worker;readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);long packets,bytes;
  public Task Ready=>ready.Task;public long Packets=>Interlocked.Read(ref packets);public long Bytes=>Interlocked.Read(ref bytes);
  readonly Action<byte[],int>? onAudio;
  public bool Faulted=>worker.IsFaulted;
  public ProcessLoopbackCapture(int processId,bool include,Action<byte[],int> onAudio){this.onAudio=onAudio;worker=Task.Factory.StartNew(()=>Run(processId,include),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);}
  unsafe void Run(int excludedPid,bool include)
  {
   IAudioClient? audio=null;IAudioCaptureClient? stream=null;bool initialized=false;
   try
   {
    Marshal.ThrowExceptionForHR(Api.CoInitializeEx(0,0));initialized=true;audio=Activate(excludedPid,include,stop.Token);
    WaveFormat format=new(){Tag=1,Channels=2,Samples=48000,BytesPerSecond=192000,BlockAlign=4,Bits=16};
    audio.Initialize(0,0x80000000|0x00020000|0x00040000,0,0,ref format,0);
    Guid captureId=typeof(IAudioCaptureClient).GUID;audio.GetService(ref captureId,out nint pointer);try{stream=UniqueComInterfaceMarshaller<IAudioCaptureClient>.ConvertToManaged((void*)pointer)!;}finally{if(pointer!=0)Marshal.Release(pointer);}
    using var signal=new AutoResetEvent(false);audio.SetEventHandle(signal.SafeWaitHandle.DangerousGetHandle());audio.Start();ready.TrySetResult();var storage=new AudioPacketBuffer();WaitHandle[] handles=[signal,stop.Token.WaitHandle];
    while(!stop.IsCancellationRequested)
    {
     if(WaitHandle.WaitAny(handles,1000)==1)break;stream.GetNextPacketSize(out uint frames);
     while(frames>0&&!stop.IsCancellationRequested)
     {
      stream.GetBuffer(out nint data,out uint count,out uint flags,out _,out _);
      try{int length=checked((int)count*4);var buffer=storage.ForPacket(length);if((flags&2)!=0)Array.Clear(buffer,0,length);else Marshal.Copy(data,buffer,0,length);onAudio?.Invoke(buffer,length);Interlocked.Increment(ref packets);Interlocked.Add(ref bytes,length);}
      finally{stream.ReleaseBuffer(count);}stream.GetNextPacketSize(out frames);
     }
    }
   }
   catch(Exception ex){ready.TrySetException(ex);if(!stop.IsCancellationRequested)throw;}
   finally{if(audio!=null){try{audio.Stop();}catch(COMException){}}if((object?)stream is ComObject captureObject)captureObject.FinalRelease();if((object?)audio is ComObject audioObject)audioObject.FinalRelease();if(initialized)Api.CoUninitialize();}
  }
  static unsafe IAudioClient Activate(int pid,bool include,CancellationToken token)
  {
   var memory=Marshal.AllocCoTaskMem(Marshal.SizeOf<Activation>());Marshal.StructureToPtr(new Activation{Type=1,Pid=(uint)pid,Mode=include?0u:1u},memory,false);
   var callback=new Completion(memory);void* handler=ComInterfaceMarshaller<ICompletion>.ConvertToUnmanaged(callback);nint operation=0;
   try
   {
    PropVariant variant=new(){Vt=65,Size=(uint)Marshal.SizeOf<Activation>(),Data=memory};Guid clientId=typeof(IAudioClient).GUID;
    int hr=Api.ActivateAudioInterfaceAsync("VAD\\Process_Loopback",ref clientId,ref variant,(nint)handler,out operation);
    if(hr<0){callback.FailedBeforeCallback();Marshal.ThrowExceptionForHR(hr);}
    nint client=callback.Take(token);try{return UniqueComInterfaceMarshaller<IAudioClient>.ConvertToManaged((void*)client)!;}finally{Marshal.Release(client);}
   }
   finally{callback.Abandon();if(operation!=0)Marshal.Release(operation);ComInterfaceMarshaller<ICompletion>.Free(handler);GC.KeepAlive(callback);}
  }
  readonly object stopGate=new();bool stopped;
  public void Stop()
  {
   lock(stopGate)
   {
    if(stopped)return;stopped=true;stop.Cancel();
    try{worker.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();}
    finally
    {
     if(worker.IsCompleted)stop.Dispose();
     else _=worker.ContinueWith(static(t,state)=>{_ = t.Exception;((CancellationTokenSource)state!).Dispose();},stop,CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
   }
  }
  public void Dispose()=>Stop();
 }
 [StructLayout(LayoutKind.Sequential)]struct Activation{public uint Type,Pid,Mode;}
 [StructLayout(LayoutKind.Explicit,Size=24)]struct PropVariant{[FieldOffset(0)]public ushort Vt;[FieldOffset(8)]public uint Size;[FieldOffset(16)]public nint Data;}
 [StructLayout(LayoutKind.Sequential,Pack=2)]struct WaveFormat{public ushort Tag,Channels;public uint Samples,BytesPerSecond;public ushort BlockAlign,Bits,Extra;}
 [GeneratedComInterface,Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]partial interface IAudioClient
 {
  void Initialize(int mode,uint flags,long duration,long periodicity,ref WaveFormat format,nint session);
  void GetBufferSize(out uint frames);void GetStreamLatency(out long latency);void GetCurrentPadding(out uint padding);
  [PreserveSig]int IsFormatSupported(int mode,nint format,out nint closest);
  void GetMixFormat(out nint format);void GetDevicePeriod(out long normal,out long minimum);void Start();void Stop();void Reset();void SetEventHandle(nint handle);void GetService(ref Guid iid,out nint service);
 }
 [GeneratedComInterface,Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]partial interface IAudioCaptureClient
 {
  void GetBuffer(out nint data,out uint frames,out uint flags,out ulong position,out ulong qpc);void ReleaseBuffer(uint frames);void GetNextPacketSize(out uint frames);
 }
 [GeneratedComInterface,Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]partial interface IActivateOperation{void GetActivateResult(out int result,out nint client);}
 [GeneratedComInterface,Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]partial interface ICompletion{[PreserveSig]int ActivateCompleted(nint operation);}
 [GeneratedComInterface,Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]partial interface IAgileObject{}
 [GeneratedComClass]sealed partial class Completion:ICompletion,IAgileObject
 {
  readonly object sync=new();readonly TaskCompletionSource<nint> result=new(TaskCreationOptions.RunContinuationsAsynchronously);nint activation;bool taken,abandoned;
  public Completion(nint activation)=>this.activation=activation;
  public unsafe int ActivateCompleted(nint operation)
  {
   IActivateOperation? wrapper=null;nint client=0;
   try
   {
    wrapper=UniqueComInterfaceMarshaller<IActivateOperation>.ConvertToManaged((void*)operation)!;wrapper.GetActivateResult(out int hr,out client);Marshal.ThrowExceptionForHR(hr);
    lock(sync){if(abandoned){if(client!=0)Marshal.Release(client);client=0;}else if(result.TrySetResult(client))client=0;}
   }
   catch(Exception error){result.TrySetException(error);}
   finally{if(client!=0)Marshal.Release(client);if((object?)wrapper is ComObject obj)obj.FinalRelease();FreeActivation();}
   return 0;
  }
  public nint Take(CancellationToken token)
  {
   try{nint client=result.Task.WaitAsync(TimeSpan.FromSeconds(10),token).GetAwaiter().GetResult();lock(sync){if(abandoned)throw new OperationCanceledException();taken=true;return client;}}
   catch{Abandon();throw;}
  }
  public void Abandon(){lock(sync){if(taken||abandoned)return;abandoned=true;if(result.Task.IsCompletedSuccessfully)Marshal.Release(result.Task.Result);}}
  public void FailedBeforeCallback(){Abandon();FreeActivation();}
  void FreeActivation(){nint memory=Interlocked.Exchange(ref activation,0);if(memory!=0)Marshal.FreeCoTaskMem(memory);}
 }
 static partial class Api
 {
  [LibraryImport("Mmdevapi.dll",StringMarshalling=StringMarshalling.Utf16)]internal static partial int ActivateAudioInterfaceAsync(string device,ref Guid iid,ref PropVariant parameters,nint callback,out nint operation);
  [LibraryImport("ole32.dll")]internal static partial int CoInitializeEx(nint reserved,uint mode);
  [LibraryImport("ole32.dll")]internal static partial void CoUninitialize();
 }
}

