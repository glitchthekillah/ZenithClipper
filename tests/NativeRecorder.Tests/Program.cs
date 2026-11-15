using ZenithClipper;
using ZenithClipper.Native;
if(args.Contains("--live-checkpoint"))
{
 string projectRoot=args.Last();string fixture=Path.Combine(projectRoot,".performance-validation","checkpoint-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
 Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",fixture);
 try
 {
  var liveSettings=new Settings{AutoStartCapture=false,ClipSeconds=1,BufferSeconds=8,MaxBufferMiB=128,ClipDirectory=Path.Combine(fixture,"clips")};
  await using var live=new NativeRecorder(liveSettings);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
  await live.StartAsync(timeout.Token);await Task.Delay(350,timeout.Token);
  var watch=System.Diagnostics.Stopwatch.StartNew();string path=await live.SaveAsync(1,timeout.Token);watch.Stop();await live.StopAsync();
  string evidence=Path.Combine(projectRoot,"outputs","live-checkpoint");Directory.CreateDirectory(evidence);File.Copy(path,Path.Combine(evidence,"early-save.mp4"),true);
  File.WriteAllText(Path.Combine(evidence,"result.txt"),$"PASS early live save before first natural GOP; saveMs={watch.Elapsed.TotalMilliseconds:F3}; buffered={live.Status().BufferedSeconds:F6}");
  Console.WriteLine(File.ReadAllText(Path.Combine(evidence,"result.txt")));
 }
 finally {Directory.Delete(fixture,true);}
 return;
}
string home=Path.Combine(Path.GetTempPath(),"ZenithClipper-native-unit-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(home);Environment.SetEnvironmentVariable("ZENITHCLIPPER_HOME",home);
try
{
 var buffer=Path.Combine(home,"buffer");Directory.CreateDirectory(buffer);
 string stale=Path.Combine(buffer,"replay-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stale);File.WriteAllText(Path.Combine(stale,"part.bin"),"orphan");
 string keep=Path.Combine(buffer,"replay-user-data");Directory.CreateDirectory(keep);File.WriteAllText(Path.Combine(keep,"keep.txt"),"keep");
 NativeSpoolCleanup.RemoveAbandoned(buffer);
 if(Directory.Exists(stale)||!File.Exists(Path.Combine(keep,"keep.txt")))throw new Exception("Owned spool cleanup violated scope.");
 Console.WriteLine("PASS abandoned owned spools removed; unrelated directories preserved");
 var settings=new Settings{AutoStartCapture=false};await using var recorder=new NativeRecorder(settings);
 foreach(var requested in new[]{settings with{Encoder="software"},settings with{WindowsCaptureBackend="desktop-duplication"}})
 {
  try{await recorder.UpdateAsync(requested,default);throw new Exception("Unsupported live switch was accepted.");}catch(NotSupportedException){}
  if(recorder.Settings!=settings||File.Exists(AppPaths.Config))throw new Exception("Rejected switch mutated settings.");
 }
 Console.WriteLine("PASS incompatible live backend settings rejected before mutation");
 await recorder.StopAsync();
 var deferred=typeof(NativeRecorder).GetMethod("StartAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance,null,[typeof(CancellationToken),typeof(long?)],null)!;
 await (Task)deferred.Invoke(recorder,[CancellationToken.None,(long?)0])!;
 if(recorder.Status().Capturing||recorder.Status().State!="Paused")throw new Exception("Stale automatic restart overrode pause.");
 Console.WriteLine("PASS pause invalidates an automatic restart queued before it");
 await (Task)deferred.Invoke(recorder,[CancellationToken.None,(long?)1])!;
 if(recorder.Status().Capturing||recorder.Status().State!="Paused")throw new Exception("Automatic restart ignored stopped desired state with matching generation.");
 Console.WriteLine("PASS stopped intent rejects automatic restart even with matching generation");
}
finally {Directory.Delete(home,true);}



