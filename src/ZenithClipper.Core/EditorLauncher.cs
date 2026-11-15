using System.Diagnostics;
namespace ZenithClipper;

/// <summary>Starts the separately packaged editor; recorder initialization never loads its assemblies.</summary>
public static class EditorLauncher
{
    public static string? FindExecutable()
    {
        string? configured=Environment.GetEnvironmentVariable("ZENITHCLIPPER_EDITOR_PATH");
        if(!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))return Path.GetFullPath(configured);
        string name=OperatingSystem.IsWindows()?"ZenithClipper.Editor.exe":"ZenithClipper.Editor";
        foreach(string folder in new[]{"editor",OperatingSystem.IsWindows()?"editor":"editor-linux"})
        {
            string candidate=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..",folder,name));
            if(File.Exists(candidate))return candidate;
        }
        return null;
    }
    public static bool Launch(string? file=null)
    {
        string? executable=FindExecutable();if(executable==null)return false;
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};
        if(!string.IsNullOrWhiteSpace(file))info.ArgumentList.Add(Path.GetFullPath(file));
        if(!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("ZENITHCLIPPER_MLT_ROOT") is {Length:>0} runtime)
            info.Environment["LD_LIBRARY_PATH"]=Path.Combine(runtime,"lib")+":"+Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        using var process=Process.Start(info)??throw new IOException("Could not start the editor.");return true;
    }
}
