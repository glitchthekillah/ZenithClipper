using System.Globalization;

namespace ZenithClipper;

public sealed record Segment(string Path, double Start, double End, long Bytes);
public static class ReplayIndex
{
    public static IReadOnlyList<Segment> Read(string directory)
    {
        var csv = Path.Combine(directory, "segments.csv");
        if (!File.Exists(csv)) return [];
        // FFmpeg only appends a CSV entry once that segment is closed.
        using var stream = new FileStream(csv, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var result = new List<Segment>();
        string text=reader.ReadToEnd();int complete=text.LastIndexOf('\n');
        if(complete<0) return result;
        foreach(string line in text[..complete].Split('\n'))
        {
            var parts = line.Split(',');
            if (parts.Length != 3 || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out var start) || !double.TryParse(parts[2], CultureInfo.InvariantCulture, out var end)) continue;
            var path = Path.Combine(directory, Path.GetFileName(parts[0].Trim('"')));
            if (File.Exists(path) && end > start) result.Add(new(path, start, end, new FileInfo(path).Length));
        }
        return result;
    }
    public static IReadOnlyList<Segment> Select(IReadOnlyList<Segment> segments, double seconds)
    {
        if (seconds <= 0 || !double.IsFinite(seconds)) throw new ArgumentException("Duration must be positive.");
        if (segments.Count == 0) throw new ArgumentException("Buffer is warming up; wait for the first segment.");
        double cutoff = segments[^1].End - seconds;
        return segments.Where(x => x.End > cutoff).ToArray();
    }
    public static IReadOnlyList<Segment> Expired(IReadOnlyList<Segment> segments, double seconds, long bytes)
    {
        if (segments.Count < 2) return [];
        long total = segments.Sum(x => x.Bytes); double cutoff = segments[^1].End - seconds;
        var expired = new List<Segment>();
        foreach (var item in segments.Take(segments.Count - 1))
        {
            if (item.End > cutoff && total <= bytes) break;
            expired.Add(item); total -= item.Bytes;
        }
        return expired;
    }
}
