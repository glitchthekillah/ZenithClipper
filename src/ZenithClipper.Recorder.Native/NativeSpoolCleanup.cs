namespace ZenithClipper.Native;

/// <summary>Called only while the daemon owns engine.lock, before creating any replay writer.</summary>
public static class NativeSpoolCleanup
{
    public static void RemoveAbandoned(string buffer)
    {
        string root=Path.GetFullPath(buffer);
        if(!Directory.Exists(root))return;
        foreach(string path in Directory.EnumerateDirectories(root,"replay-*",SearchOption.TopDirectoryOnly))
        {
            string name=Path.GetFileName(path);
            if(name.Length!=39||!Guid.TryParseExact(name.AsSpan(7),"N",out _))continue;
            var directory=new DirectoryInfo(path);
            if((directory.Attributes&FileAttributes.ReparsePoint)!=0)continue;
            // Refuse to traverse junctions/symlinks even in a correctly named owned spool.
            RemoveContents(directory);
            directory.Delete();
        }
    }
    static void RemoveContents(DirectoryInfo directory)
    {
        foreach(var entry in directory.EnumerateFileSystemInfos())
        {
            if((entry.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("A replay spool contains a linked path; cleanup was refused.");
            if(entry is DirectoryInfo child){RemoveContents(child);child.Delete();}else entry.Delete();
        }
    }
}
