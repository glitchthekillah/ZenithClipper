namespace ZenithClipper;

/// <summary>A consistent process-tree/name view without repeated per-PID process enumeration.</summary>
public sealed record ProcessSnapshot(Dictionary<int,int> Parents,Dictionary<int,string> Names)
{
    public int[] Excluded(IEnumerable<string> rules) => Names.Where(p=>AudioPolicy.Excluded(p.Value,rules)).Select(p=>p.Key).Order().ToArray();

    public int[] AudioTrees(IEnumerable<int> candidates,string[] rules,int[]? blocked=null)
    {
        bool Eligible(int pid)
        {
            if(!Names.ContainsKey(pid))return false;
            var seen=new HashSet<int>();
            do
            {
                if(Names.TryGetValue(pid,out string? name)&&AudioPolicy.Excluded(name,rules))return false;
            }while(Parents.TryGetValue(pid,out pid)&&pid!=0&&seen.Add(pid));
            return true;
        }
        return AudioPolicy.SafeTrees(candidates.Where(Eligible),Parents,blocked??Excluded(rules));
    }
}
