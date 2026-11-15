using System.Diagnostics;
namespace ZenithClipper;

/// <summary>Launches only the requested panel process; no editor assemblies or resident UI.</summary>
public static class PanelLauncher
{
    public static ProcessStartInfo Create(string applicationDirectory, string engineExecutable, bool windows)
    {
        string ui=Path.Combine(applicationDirectory,"ZenithClipper.Windows.exe");
        var info=new ProcessStartInfo(windows && File.Exists(ui)?ui:engineExecutable){UseShellExecute=false,CreateNoWindow=true};
        if(info.FileName==engineExecutable && string.Equals(Path.GetFileNameWithoutExtension(engineExecutable),"dotnet",StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Path.Combine(applicationDirectory,"zenithclipper.dll"));
        info.ArgumentList.Add("panel");
        return info;
    }
}
