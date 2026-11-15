namespace ZenithClipper;

/// <summary>Only a short FFmpeg CSV window is read each tick; long history lives in a bounded queue.</summary>
public sealed class ReplayCatalog(string directory)
{
    readonly Queue<Segment> segments = new();
    readonly object sync = new();
    double newest;
    long bytes;
    string? nextPath=Path.Combine(directory,"000000000.ts");
    public string? UnfinishedPath { get { lock(sync) return nextPath; } }
    public double NewestEnd { get { lock(sync) return newest; } }
    public void Poll()
    {
        lock (sync)
            foreach (var item in ReplayIndex.Read(directory))
                if (item.End > newest) {
                    if(newest>0 && item.Start>newest+.25) throw new IOException("Capture metadata was missed during a long stall. Resume capture to reset the buffer safely.");
                    segments.Enqueue(item); bytes += item.Bytes; newest = item.End;
                    if(int.TryParse(Path.GetFileNameWithoutExtension(item.Path),out int index)) nextPath=Path.Combine(directory,$"{index+1:D9}.ts");
                }
    }
    public IReadOnlyList<Segment> Snapshot() { lock (sync) return segments.ToArray(); }
    public (double seconds, long bytes) Metrics()
    {
        lock (sync)
        {
            long activeBytes=0;
            if (nextPath!=null)
            {
                var active=new FileInfo(nextPath); if(active.Exists) activeBytes=active.Length;
            }
            return (segments.Count==0?0:newest-segments.Peek().Start,bytes+activeBytes);
        }
    }
    public void Prune(double seconds,long limit)
    {
        lock(sync)
            while(segments.Count>1 && (segments.Peek().End<=newest-seconds || bytes>limit))
            {
                var old=segments.Peek(); File.Delete(old.Path); segments.Dequeue(); bytes-=old.Bytes;
            }
    }
}
