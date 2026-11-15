using System.Runtime.InteropServices;

namespace ZenithClipper;

/// <summary>Immutable snapshot pins compressed segments without duplicating hours of media.</summary>
public sealed class ReplaySnapshot : IDisposable
{
    public string DirectoryPath { get; }
    public double Duration { get; }
    public int Count { get; }
    readonly double[] durations;
    readonly bool hasTail;
    public ReplaySnapshot(IReadOnlyList<Segment> selected, string parent, string? unfinished=null)
    {
        if (selected.Count == 0) throw new ArgumentException("No footage to save.");
        DirectoryPath = Path.Combine(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(DirectoryPath);
        Duration = selected[^1].End - selected[0].Start;
        durations=selected.Select(x=>x.End-x.Start).ToArray();
        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                string source = selected[i].Path, dest = Path.Combine(DirectoryPath, $"{i:D9}.ts");
                bool linked = OperatingSystem.IsWindows() ? CreateHardLinkW(dest, source, 0) : Link(source, dest) == 0;
                if (!linked) File.Copy(source, dest); // filesystems without hardlinks
            }
            if(unfinished!=null && File.Exists(unfinished))
            {
                using var input=new FileStream(unfinished,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536);
                // A complete TS packet can still contain only part of a video frame. Drop the newest PES, not just a partial packet.
                long remaining=TsSnapshot.SafeLength(input,input.Length);
                if(remaining>=188*10)
                {
                    using var output=new FileStream(Path.Combine(DirectoryPath,$"{selected.Count:D9}.ts"),FileMode.CreateNew,FileAccess.Write);
                    byte[] buffer=new byte[65536];
                    while(remaining>0) { int read=input.Read(buffer,0,(int)Math.Min(buffer.Length,remaining)); if(read==0) break; output.Write(buffer,0,read); remaining-=read; }
                    hasTail=true;
                }
            }
            Count=selected.Count+(hasTail?1:0);
        }
        catch { Dispose(); throw; }
    }
    public async Task<string> SaveAsync(Settings settings, double seconds, CancellationToken ct = default)
    {
        Directory.CreateDirectory(settings.ClipDirectory);
        string name = $"Clip-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}-{Guid.NewGuid().ToString("N")[..6]}.mp4";
        string output = Path.Combine(settings.ClipDirectory, name), temp = Path.Combine(settings.ClipDirectory, "." + name);
        int count=Count; double available=Duration;
        if(hasTail)
        {
            try
            {
                using var tail=System.Text.Json.JsonDocument.Parse(await MediaTools.ProbeAsync(MediaTools.FindFfmpeg(settings),Path.Combine(DirectoryPath,$"{count-1:D9}.ts"),ct));
                string? duration=tail.RootElement.GetProperty("format").GetProperty("duration").GetString();
                if(double.TryParse(duration,System.Globalization.CultureInfo.InvariantCulture,out var value) && value>0) available+=value; else count--;
            }
            catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException or KeyNotFoundException) { count--; }
        }
        int first=0;
        while(first<durations.Length && first<count-1 && available-durations[first]>=seconds) { available-=durations[first]; first++; }
        await File.WriteAllLinesAsync(Path.Combine(DirectoryPath,"input.txt"),Enumerable.Range(first,count-first).Select(i=>$"file '{i:D9}.ts'"),ct);
        try
        {
            await MediaTools.RunAsync(MediaTools.FindFfmpeg(settings), ["-v", "error", "-y", "-fflags", "+discardcorrupt", "-f", "concat", "-safe", "1", "-i", Path.Combine(DirectoryPath, "input.txt"), "-map", "0", "-c", "copy", "-movflags", "+faststart", "-avoid_negative_ts", "make_zero", temp], ct);
            // A valid output must contain a video stream, even for a very short requested clip.
            string probe = await MediaTools.ProbeAsync(MediaTools.FindFfmpeg(settings), temp, ct);
            using var doc = System.Text.Json.JsonDocument.Parse(probe);
            if (!doc.RootElement.GetProperty("streams").EnumerateArray().Any(x => x.GetProperty("codec_type").GetString() == "video")) throw new IOException("No complete video keyframe available; wait for more footage or save a longer clip.");
            File.Move(temp, output, false); return output;
        }
        finally { Recorder.TryDelete(temp); }
    }
    public void Dispose() { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool CreateHardLinkW(string newName, string existingName, nint reserved);
    [DllImport("libc", EntryPoint = "link", SetLastError = true)] static extern int Link(string existingName, string newName);
}
