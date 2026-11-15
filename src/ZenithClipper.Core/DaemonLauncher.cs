using System.Diagnostics;
namespace ZenithClipper;

public static class DaemonLauncher
{
    public static bool CanUseNative(Settings settings,string directory)
        => OperatingSystem.IsWindowsVersionAtLeast(10,0,26100) && settings.WindowsCaptureBackend=="auto"
        && (settings.Encoder is "auto" or "nvenc") && File.Exists(Path.Combine(directory,"ZenithClipper.Recorder.exe"))
        && WindowsDisplay.HasNvidiaAdapter();

    public static ProcessStartInfo Create(string directory,string legacyExecutable,bool nativeEligible)
    {
        string native=Path.Combine(directory,"ZenithClipper.Recorder.exe");
        var start=new ProcessStartInfo(nativeEligible&&File.Exists(native)?native:legacyExecutable){UseShellExecute=false,CreateNoWindow=true};
        if(start.FileName==legacyExecutable&&Path.GetFileNameWithoutExtension(legacyExecutable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Path.Combine(directory,"zenithclipper.dll"));
        start.ArgumentList.Add("daemon");return start;
    }
}

