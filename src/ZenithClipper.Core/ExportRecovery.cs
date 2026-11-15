namespace ZenithClipper;

public static class ExportRecovery
{
    /// <summary>Call only after acquiring the engine's exclusive instance lock, before accepting saves.</summary>
    public static void Clean(string exports)
    {
        if(!Directory.Exists(exports)) return;
        foreach(string directory in Directory.EnumerateDirectories(exports))
            if(Guid.TryParseExact(Path.GetFileName(directory),"N",out _) && (File.GetAttributes(directory)&FileAttributes.ReparsePoint)==0) Directory.Delete(directory,true);
    }
}
