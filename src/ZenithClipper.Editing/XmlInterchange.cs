using System.Globalization;
using System.Xml.Linq;

namespace ZenithClipper.Editing;

/// <summary>
/// Final Cut Pro 7 XML (xmeml), the "Final Cut Pro XML" Premiere exports and imports. Reading builds a sequence from
/// its tracks and clip items; writing exports the active sequence so it can go back into Premiere or Resolve.
/// </summary>
public static class XmlInterchange
{
    public sealed record XmlClip(string FileId, string Path, string Name, bool Video, int Track, long Start, long End, long In, bool Enabled);
    public sealed record XmlSequence(string Name, FrameRate Fps, int Width, int Height, List<XmlClip> Clips, int VideoTracks, int AudioTracks);

    static FrameRate Rate(XElement? rate)
    {
        int timebase = (int?)rate?.Element("timebase") ?? 30; bool ntsc = string.Equals((string?)rate?.Element("ntsc"), "TRUE", StringComparison.OrdinalIgnoreCase);
        return ntsc ? new FrameRate(timebase * 1000, 1001) : new FrameRate(timebase, 1);
    }

    static string PathFromUrl(string url)
    {
        if (!url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return url;
        var path = Uri.UnescapeDataString(url.Replace("file://localhost", "", StringComparison.OrdinalIgnoreCase).Replace("file://", "", StringComparison.OrdinalIgnoreCase));
        // file:///C:/x or /C:/x → C:/x on Windows paths.
        if (path.Length > 2 && path[0] == '/' && path[2] == ':') path = path[1..];
        return path.Replace('/', System.IO.Path.DirectorySeparatorChar);
    }

    public static XmlSequence Read(string xml)
    {
        var doc = XDocument.Parse(xml);
        var seq = doc.Descendants("sequence").FirstOrDefault() ?? throw new InvalidDataException("No <sequence> in this XML.");
        var fps = Rate(seq.Element("rate"));
        var format = seq.Element("media")?.Element("video")?.Element("format")?.Element("samplecharacteristics");
        int width = (int?)format?.Element("width") ?? 1920, height = (int?)format?.Element("height") ?? 1080;
        // Files are described once (with pathurl) and referenced by id afterwards.
        var files = new Dictionary<string, (string Path, string Name)>();
        foreach (var f in doc.Descendants("file"))
            if ((string?)f.Attribute("id") is { } id && f.Element("pathurl") is { } url) files[id] = (PathFromUrl(url.Value), (string?)f.Element("name") ?? System.IO.Path.GetFileName(PathFromUrl(url.Value)));
        var clips = new List<XmlClip>(); int videoTracks = 0, audioTracks = 0;
        foreach (var (kind, video) in new[] { ("video", true), ("audio", false) })
        {
            var tracks = seq.Element("media")?.Element(kind)?.Elements("track").ToList() ?? [];
            if (video) videoTracks = tracks.Count; else audioTracks = tracks.Count;
            for (int t = 0; t < tracks.Count; t++)
            foreach (var item in tracks[t].Elements("clipitem"))
            {
                string? fileId = (string?)item.Element("file")?.Attribute("id"); if (fileId is null || !files.TryGetValue(fileId, out var file)) continue;
                long start = (long?)item.Element("start") ?? -1, end = (long?)item.Element("end") ?? -1, inPoint = (long?)item.Element("in") ?? 0;
                if (start < 0 || end <= start) continue; // transitions make start/end -1; skip those edges
                bool enabled = !string.Equals((string?)item.Element("enabled"), "FALSE", StringComparison.OrdinalIgnoreCase);
                clips.Add(new XmlClip(fileId, file.Path, (string?)item.Element("name") ?? file.Name, video, t, start, end, inPoint, enabled));
            }
        }
        return new XmlSequence((string?)seq.Element("name") ?? "Imported sequence", fps, width, height, clips, Math.Max(1, videoTracks), Math.Max(1, audioTracks));
    }

    /// <summary>Builds the sequence once every file is available as media (keyed by XML file id). Video and audio items of the same file, start and in point are linked.</summary>
    public static EditSequence Build(XmlSequence x, IReadOnlyDictionary<string, MediaAsset> media)
    {
        var seq = new EditSequence { Name = x.Name, Fps = x.Fps, Width = x.Width, Height = x.Height };
        var video = Enumerable.Range(0, x.VideoTracks).Select(i => new EditTrack { Name = $"Video {i + 1}", Kind = TrackKind.Video }).ToList();
        var audio = Enumerable.Range(0, x.AudioTracks).Select(i => new EditTrack { Name = $"Audio {i + 1}", Kind = TrackKind.Audio }).ToList();
        seq.Tracks.AddRange(video); seq.Tracks.AddRange(audio);
        var links = new Dictionary<(string, long, long), Guid>();
        foreach (var c in x.Clips)
        {
            if (!media.TryGetValue(c.FileId, out var m)) continue;
            var clip = new TimelineClip { Name = c.Name, MediaId = m.Id, Start = c.Start, Duration = c.End - c.Start, SourceIn = c.In, Enabled = c.Enabled, StreamIntent = c.Video ? ClipStreamIntent.VideoOnly : ClipStreamIntent.AudioOnly };
            if (m.Kind is MediaKind.Image) { clip.SourceIn = 0; }
            else if (m.DurationSeconds > 0)
            {
                // Retimed or rate-mismatched items can point past the file; keep what exists.
                long available = x.Fps.Frames(m.DurationSeconds);
                if (clip.SourceIn >= available) continue;
                clip.Duration = Math.Min(clip.Duration, available - clip.SourceIn);
            }
            if (m.Kind == MediaKind.Video && m.HasAudio)
            {
                var key = (c.FileId, c.Start, c.In);
                if (!links.TryGetValue(key, out var link)) links[key] = link = Guid.NewGuid();
                clip.LinkId = clip.SourcePairId = link;
            }
            var track = (c.Video ? video : audio)[Math.Min(c.Track, (c.Video ? video : audio).Count - 1)];
            if (track.Clips.Any(o => o.Start < clip.End && o.End > clip.Start)) continue; // overlapping items (rare) are skipped
            track.Clips.Add(clip);
        }
        // A link is only real when both halves made it in.
        foreach (var group in seq.Tracks.SelectMany(t => t.Clips).Where(c => c.LinkId is not null).GroupBy(c => c.LinkId).Where(g => g.Count() < 2))
            foreach (var c in group) c.LinkId = c.SourcePairId = null;
        foreach (var t in seq.Tracks) t.Clips.Sort((a, b) => a.Start.CompareTo(b.Start));
        return seq;
    }

    /// <summary>Exports a sequence as FCP7 XML (cuts, tracks, in/out points). Effects and transitions stay behind.</summary>
    public static string Write(EditProject p, EditSequence s)
    {
        bool ntsc = s.Fps.Denominator == 1001; int timebase = ntsc ? (int)Math.Round(s.Fps.Numerator / 1000.0) : (int)Math.Round(s.Fps.Value);
        XElement Rate() => new("rate", new XElement("timebase", timebase), new XElement("ntsc", ntsc ? "TRUE" : "FALSE"));
        var written = new HashSet<Guid>();
        XElement File(MediaAsset m)
        {
            if (!written.Add(m.Id)) return new XElement("file", new XAttribute("id", "file-" + m.Id.ToString("N")));
            var full = System.IO.Path.GetFullPath(m.Path);
            var url = "file://localhost/" + Uri.EscapeDataString(full.Replace('\\', '/')).Replace("%2F", "/").Replace("%3A", ":");
            return new XElement("file", new XAttribute("id", "file-" + m.Id.ToString("N")), new XElement("name", m.Name), new XElement("pathurl", url), Rate(),
                new XElement("duration", (long)Math.Round(m.DurationSeconds * s.Fps.Value)),
                new XElement("media", m.Kind == MediaKind.Audio ? null : new XElement("video", new XElement("samplecharacteristics", new XElement("width", m.Width), new XElement("height", m.Height))), m.HasAudio || m.Kind == MediaKind.Audio ? new XElement("audio", new XElement("channelcount", 2)) : null));
        }
        XElement Track(EditTrack t) => new("track", t.Clips.Where(c => p.Media.FirstOrDefault(m => m.Id == c.MediaId) is { Kind: MediaKind.Video or MediaKind.Audio or MediaKind.Image } m && !string.IsNullOrEmpty(m.Path)).Select((c, i) =>
        {
            var m = p.Media.First(x => x.Id == c.MediaId);
            return new XElement("clipitem", new XAttribute("id", "clip-" + c.Id.ToString("N")), new XElement("name", c.Name), new XElement("enabled", c.Enabled ? "TRUE" : "FALSE"), Rate(),
                new XElement("start", c.Start), new XElement("end", c.End), new XElement("in", c.SourceIn), new XElement("out", c.SourceIn + (long)Math.Round(c.Duration * c.Speed)), File(m));
        }), new XElement("enabled", t.Hidden || t.Muted ? "FALSE" : "TRUE"), new XElement("locked", t.Locked ? "TRUE" : "FALSE"));
        var doc = new XDocument(new XDocumentType("xmeml", null, null, null), new XElement("xmeml", new XAttribute("version", "4"),
            new XElement("sequence", new XAttribute("id", "sequence-" + s.Id.ToString("N")), new XElement("name", s.Name), new XElement("duration", s.Duration), Rate(),
                new XElement("media",
                    new XElement("video", new XElement("format", new XElement("samplecharacteristics", Rate(), new XElement("width", s.Width), new XElement("height", s.Height))), s.Tracks.Where(t => t.Kind == TrackKind.Video).Select(Track)),
                    new XElement("audio", s.Tracks.Where(t => t.Kind == TrackKind.Audio).Select(Track))))));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + doc.ToString();
    }
}
