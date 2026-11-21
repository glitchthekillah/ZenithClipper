using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ZenithClipper;
namespace ZenithClipper.Native.Audio;

/// <summary>
/// Bounded per-app allowlist capture. Mix on the video clock without a second scheduling loop.
/// Each captured app tree carries a gain (its per-app clip volume) applied while mixing, so level changes are live.
/// With no exclusions and every app at 100% a single whole-system loopback is used instead (cheapest path).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NativeAudioMixer:IDisposable
{
 readonly Settings settings;
 readonly string[] exclusions;readonly bool perApp;readonly object gate=new();readonly Dictionary<int,(ProcessLoopbackCapture Capture,PcmRing Ring)> sources=new();
 readonly Dictionary<int,string> names=new();readonly Dictionary<int,int> gains=new();IReadOnlyDictionary<string,int> volumes;
 readonly short[] scratch=new short[1920];readonly int[] mixed=new int[1920];bool disposed;
 public NativeAudioMixer(Settings settings)
 {
  this.settings=settings;exclusions=settings.ExcludedApps;volumes=settings.AppVolumes;
  perApp=settings.CaptureAudio&&settings.NeedsPerAppAudio;
 }
 /// <summary>Whether this mixer separates apps. A level change that flips this needs a new mixer (see <see cref="SetVolumes"/>).</summary>
 public bool PerApp=>perApp;
 /// <summary>Applies new per-app levels immediately. Returns false when the new levels need the other capture mode.</summary>
 public bool SetVolumes(IReadOnlyDictionary<string,int> next)
 {
  bool wantPerApp=settings.CaptureAudio&&(exclusions.Length>0||next.Values.Any(v=>v!=100));if(wantPerApp!=perApp)return false;
  lock(gate){volumes=next;foreach(int pid in sources.Keys)gains[pid]=Gain(pid);}return true;
 }
 int Gain(int pid)
 {
  if(!perApp||!names.TryGetValue(pid,out string? name))return 256;
  return volumes.TryGetValue(AudioPolicy.Normalize(name),out int percent)?Math.Clamp(percent,0,200)*256/100:256;
 }
 public bool Faulted{get{lock(gate)return sources.Values.Any(s=>s.Capture.Faulted);}}
 public async Task StartAsync(CancellationToken token)
 {
  if (settings.CaptureMicrophone)
   await AddDeviceAsync(-1, true, settings.WindowsInputDevice, token);
  if (!settings.CaptureAudio) return;
  if (perApp) await RefreshAsync(token);
  else await AddDeviceAsync(Environment.ProcessId, false, settings.WindowsOutputDevice, token);
 }
 async Task AddDeviceAsync(int key, bool microphone, string device, CancellationToken token)
 {
  var ring = new PcmRing();
  var capture = microphone || device.Length > 0
   ? new ProcessLoopbackCapture(device, microphone, ring.Write)
   : new ProcessLoopbackCapture(Environment.ProcessId, false, ring.Write);
  try
  {
   await capture.Ready.WaitAsync(TimeSpan.FromSeconds(10), token);
   lock (gate) { sources.Add(key, (capture, ring)); gains[key] = 256; }
  }
  catch { capture.Dispose(); throw; }
 }
 public async Task<bool> RefreshAsync(CancellationToken token)
 {
  ObjectDisposedException.ThrowIf(disposed,this);if(!perApp)return false;
  var view=NativeAudioSessions.Read(exclusions, settings.WindowsOutputDevice);int[] current;lock(gate){current=sources.Keys.Where(pid => pid > 0).ToArray();foreach(int pid in view.Included)if(view.Names.TryGetValue(pid,out var n))names[pid]=n;}
  if(AudioPolicy.RootsContainExcluded(current,view.Parents,view.Excluded))return true;
  if(view.Included.Length>32)throw new IOException("More than 32 audio process trees; capture stopped to keep memory bounded.");
  foreach(int pid in current.Except(view.Included)){(ProcessLoopbackCapture Capture,PcmRing Ring) old;lock(gate){old=sources[pid];sources.Remove(pid);gains.Remove(pid);names.Remove(pid);}old.Capture.Dispose();}
  foreach(int pid in view.Included.Except(current)){var ring=new PcmRing();var capture=new ProcessLoopbackCapture(pid,true,ring.Write);try{await capture.Ready.WaitAsync(TimeSpan.FromSeconds(10),token);lock(gate){sources.Add(pid,(capture,ring));gains[pid]=Gain(pid);}}catch{capture.Dispose();throw;}}
  return false;
 }
 public bool RequiresHistoryReset()
 {
  if(!perApp||exclusions.Length==0)return false;var view=NativeAudioSessions.Read(exclusions, settings.WindowsOutputDevice);lock(gate)return AudioPolicy.RootsContainExcluded(sources.Keys,view.Parents,view.Excluded);
 }
 public void BeginEpoch(){lock(gate)foreach(var source in sources.Values)source.Ring.Clear();}
 public void Read20Milliseconds(byte[] destination)
 {
  if(destination.Length!=3840)throw new ArgumentException("Require 20 ms of stereo PCM16.",nameof(destination));Array.Clear(mixed);
  lock(gate)foreach(var (pid,source) in sources)
  {
   source.Ring.Read(scratch);int gain=gains.GetValueOrDefault(pid,256);
   if(gain==256)for(int i=0;i<mixed.Length;i++)mixed[i]+=scratch[i];
   else if(gain>0)for(int i=0;i<mixed.Length;i++)mixed[i]+=scratch[i]*gain>>8;
  }
  var output=MemoryMarshal.Cast<byte,short>(destination.AsSpan());for(int i=0;i<output.Length;i++)output[i]=(short)Math.Clamp(mixed[i],short.MinValue,short.MaxValue);
 }
 public void Dispose()
 {
  ProcessLoopbackCapture[] pending;lock(gate){if(disposed)return;disposed=true;pending=sources.Values.Select(s=>s.Capture).ToArray();sources.Clear();}
  Exception? failure=null;foreach(var capture in pending)try{capture.Dispose();}catch(Exception ex){failure??=ex;}
  if(failure!=null)throw new IOException("Audio capture cleanup failed: "+failure.Message,failure);
 }
}
