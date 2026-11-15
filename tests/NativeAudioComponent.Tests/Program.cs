using System.Runtime.CompilerServices;
using ZenithClipper;
using ZenithClipper.Native.Audio;

foreach(string[] rules in new[]{new[]{"Spotify"},Array.Empty<string>(),new[]{"not-a-real-app-zenith-test"}})
{
    var native=NativeAudioSessions.Read(rules);
    Console.WriteLine($"rules={string.Join(',',rules)} included={string.Join(',',native.Included)} excluded={string.Join(',',native.Excluded)} parents={native.Parents.Count}");
    if(RuntimeFeature.IsDynamicCodeSupported)
    {
        var original=WindowsSessions.Read(rules);
        if(!native.Included.SequenceEqual(original.Included)||!native.Excluded.SequenceEqual(original.Excluded))
            throw new Exception("Generated/native and classic/Core session policy mismatch.");
        Console.WriteLine("PASS NativeAudioSessions matches Core WindowsSessions");
    }
    if(AudioPolicy.RootsContainExcluded(native.Included,native.Parents,native.Excluded))throw new Exception("Unsafe included tree.");
}
for(int i=0;i<50;i++)_ = NativeAudioSessions.Read(["Spotify"]);
Console.WriteLine("PASS repeated generated-COM session enumeration; metadata only, no capture");
var baseline=NativeAudioSessions.Read([]);
if(baseline.Included.Length>0)
{
    int pid=baseline.Included[0];
    using var process=System.Diagnostics.Process.GetProcessById(pid);
    var snapshot=NativeAudioSessions.Processes();Console.WriteLine($"DIAGNOSTIC pid={pid} ProcessName={process.ProcessName} nativeSnapshotName={snapshot.Names.GetValueOrDefault(pid)}");
    var excluded=NativeAudioSessions.Read([process.ProcessName]);
    if(!excluded.Excluded.Contains(pid))throw new Exception("Actual process-name exclusion missed live session owner.");
    if(AudioPolicy.RootsContainExcluded(excluded.Included,excluded.Parents,excluded.Excluded))throw new Exception("Excluded session subtree included.");
    if(RuntimeFeature.IsDynamicCodeSupported){var original=WindowsSessions.Read([process.ProcessName]);if(!excluded.Included.SequenceEqual(original.Included)||!excluded.Excluded.SequenceEqual(original.Excluded))throw new Exception("Live owner exclusion policy differs.");}
    Console.WriteLine($"PASS actual session owner exclusion name={process.ProcessName} blockedCount={excluded.Excluded.Length}");
}

