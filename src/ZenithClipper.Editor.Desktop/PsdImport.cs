using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>One Photoshop layer, as straight (unpremultiplied) RGBA at the layer's own bounds.</summary>
internal sealed record PsdLayer(string Name, int Left, int Top, int Width, int Height, byte[] Rgba, double Opacity, bool Visible);

/// <summary>Reads the layers of an 8-bit RGB Photoshop file (.psd), raw or RLE (PackBits) compressed.</summary>
internal static class PsdReader
{
    public static (int Width, int Height, List<PsdLayer> Layers) Read(string path)
    {
        using var stream = File.OpenRead(path); var r = new Reader(stream);
        if (r.Ascii(4) != "8BPS") throw new InvalidDataException("Not a Photoshop file.");
        int version = r.U16(); if (version != 1) throw new InvalidDataException("Large documents (.psb) aren't supported; save as .psd.");
        r.Skip(6); int channels = r.U16(); int height = (int)r.U32(), width = (int)r.U32(); int depth = r.U16(), mode = r.U16();
        if (depth != 8 || mode != 3) throw new InvalidDataException("Only 8-bit RGB Photoshop files can be split into layers. Import it as a merged image instead.");
        r.Skip(r.U32()); r.Skip(r.U32()); // color mode data, image resources
        long layerMaskLength = r.U32(); var layers = new List<PsdLayer>(); if (layerMaskLength == 0) return (width, height, layers);
        long layerInfoLength = r.U32(); if (layerInfoLength == 0) return (width, height, layers);
        int count = Math.Abs((short)r.U16());
        var records = new List<(int T, int L, int B, int R, List<(short Id, long Len)> Ch, byte Opacity, bool Hidden, string Name, bool Divider)>();
        for (int i = 0; i < count; i++)
        {
            int top = (int)r.U32(), left = (int)r.U32(), bottom = (int)r.U32(), right = (int)r.U32();
            int n = r.U16(); var ch = new List<(short, long)>(); for (int c = 0; c < n; c++) ch.Add(((short)r.U16(), r.U32()));
            if (r.Ascii(4) != "8BIM") throw new InvalidDataException("Damaged layer record.");
            r.Skip(4); byte opacity = r.U8(); r.U8(); byte flags = r.U8(); r.U8();
            long extra = r.U32(), end = stream.Position + extra;
            r.Skip(r.U32()); r.Skip(r.U32()); // layer mask, blending ranges
            int nameLength = r.U8(); string name = Encoding.Latin1.GetString(r.Bytes(nameLength)); r.Skip((4 - (nameLength + 1) % 4) % 4);
            bool divider = false;
            while (stream.Position + 12 <= end)
            {
                string sig = r.Ascii(4); if (sig != "8BIM" && sig != "8B64") break;
                string key = r.Ascii(4); long len = r.U32(), next = stream.Position + len + (len % 2);
                if (key == "luni") { int chars = (int)r.U32(); name = Encoding.BigEndianUnicode.GetString(r.Bytes(chars * 2)).TrimEnd('\0'); }
                else if (key is "lsct" or "lsdk") { int kind = (int)r.U32(); divider = kind is 1 or 2 or 3; }
                stream.Position = Math.Min(next, end);
            }
            stream.Position = end;
            records.Add((top, left, bottom, right, ch, opacity, (flags & 2) != 0, name, divider));
        }
        foreach (var rec in records)
        {
            int w = Math.Max(0, rec.R - rec.L), h = Math.Max(0, rec.B - rec.T); var rgba = new byte[w * h * 4];
            for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
            foreach (var (id, len) in rec.Ch)
            {
                long end = stream.Position + len; int compression = r.U16();
                // Masks (id -2/-3) have their own bounds; skip them.
                if (id < -1 || w == 0 || h == 0) { stream.Position = end; continue; }
                int offset = id == -1 ? 3 : id; if (offset > 3) { stream.Position = end; continue; }
                var plane = compression switch { 0 => r.Bytes(w * h), 1 => Rle(r, w, h), _ => throw new InvalidDataException("Zip-compressed layers aren't supported; save the file with maximum compatibility off or as a flat image.") };
                for (int p = 0; p < w * h; p++) rgba[p * 4 + offset] = plane[p];
                stream.Position = end;
            }
            if (rec.Divider || w == 0 || h == 0) continue;
            layers.Add(new PsdLayer(string.IsNullOrWhiteSpace(rec.Name) ? $"Layer {layers.Count + 1}" : rec.Name, rec.L, rec.T, w, h, rgba, rec.Opacity / 255.0, !rec.Hidden));
        }
        return (width, height, layers);
    }
    static byte[] Rle(Reader r, int w, int h)
    {
        var counts = new int[h]; for (int y = 0; y < h; y++) counts[y] = r.U16();
        var plane = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            var row = r.Bytes(counts[y]); int i = 0, o = y * w, stop = o + w;
            while (i < row.Length && o < stop)
            {
                int n = (sbyte)row[i++];
                if (n >= 0) { int k = Math.Min(n + 1, stop - o); Array.Copy(row, i, plane, o, Math.Min(k, row.Length - i)); i += n + 1; o += k; }
                else if (n != -128) { int k = Math.Min(1 - n, stop - o); byte v = row[i++]; plane.AsSpan(o, k).Fill(v); o += k; }
            }
        }
        return plane;
    }
    sealed class Reader(Stream s)
    {
        readonly byte[] b = new byte[4];
        void Fill(int n) { if (s.ReadAtLeast(b.AsSpan(0, n), n, false) != n) throw new EndOfStreamException("The Photoshop file ended early."); }
        public byte U8() { Fill(1); return b[0]; }
        public int U16() { Fill(2); return BinaryPrimitives.ReadUInt16BigEndian(b); }
        public long U32() { Fill(4); return BinaryPrimitives.ReadUInt32BigEndian(b); }
        public string Ascii(int n) => Encoding.ASCII.GetString(Bytes(n));
        public byte[] Bytes(int n) { var a = new byte[n]; if (s.ReadAtLeast(a, n, false) != n) throw new EndOfStreamException("The Photoshop file ended early."); return a; }
        public void Skip(long n) => s.Position += n;
    }
}

/// <summary>Minimal PNG encoder for straight RGBA (layer exports, generated test images).</summary>
internal static class PngWriter
{
    public static void Write(string path, int width, int height, byte[] rgba)
    {
        var raw = new byte[height * (width * 4 + 1)];
        for (int y = 0; y < height; y++) Array.Copy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        using var file = File.Create(path);
        void Chunk(string type, byte[] data)
        {
            Span<byte> n = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(n, data.Length); file.Write(n);
            var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray(); file.Write(body);
            BinaryPrimitives.WriteUInt32BigEndian(n, Crc(body)); file.Write(n);
        }
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var head = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(head, width); BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(4), height); head[8] = 8; head[9] = 6;
        Chunk("IHDR", head);
        using (var ms = new MemoryStream()) { using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw); Chunk("IDAT", ms.ToArray()); }
        Chunk("IEND", []);
    }
    static readonly uint[] Table = Enumerable.Range(0, 256).Select(n => { uint c = (uint)n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; return c; }).ToArray();
    static uint Crc(byte[] d) { uint c = 0xFFFFFFFF; foreach (byte x in d) c = Table[(c ^ x) & 0xFF] ^ (c >> 8); return ~c; }
}

public sealed partial class EditorWindow
{
    /// <summary>Premiere's "Import As: Sequence" for Photoshop files: every layer becomes a still on its own video track,
    /// bottom layer lowest, at document size, keeping layer opacity and visibility, in a new sequence named after the file.</summary>
    internal IReadOnlyList<Guid> ImportPsdLayers(string path, double seconds = 5)
    {
        var (width, height, layers) = PsdReader.Read(path);
        if (layers.Count == 0) throw new InvalidDataException("This Photoshop file has no layers. Import it as a merged image instead.");
        string dir = Path.Combine(AssetDirectory, "psd-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path) + File.GetLastWriteTimeUtc(path).Ticks)))[..12]);
        Directory.CreateDirectory(dir);
        var assets = new List<(MediaAsset Asset, PsdLayer Layer)>();
        for (int i = 0; i < layers.Count; i++)
        {
            var l = layers[i]; var canvas = new byte[width * height * 4];
            for (int y = 0; y < l.Height; y++)
            {
                int cy = l.Top + y; if (cy < 0 || cy >= height) continue;
                for (int x = 0; x < l.Width; x++) { int cx = l.Left + x; if (cx < 0 || cx >= width) continue; Array.Copy(l.Rgba, (y * l.Width + x) * 4, canvas, (cy * width + cx) * 4, 4); }
            }
            string png = Path.Combine(dir, $"{i:00}.png"); PngWriter.Write(png, width, height, canvas);
            assets.Add((new MediaAsset { Name = $"{l.Name}/{Path.GetFileNameWithoutExtension(path)}", Path = png, Kind = MediaKind.Image, Width = width, Height = height, DurationSeconds = seconds, Bin = Path.GetFileNameWithoutExtension(path) }, l));
        }
        Change("Import Photoshop layers", p =>
        {
            foreach (var (a, _) in assets) p.Media.Add(a);
            var id = EditCommands.NewSequence(p, Path.GetFileNameWithoutExtension(path)); var seq = p.Sequences.First(s => s.Id == id);
            seq.Tracks.RemoveAll(t => t.Kind == TrackKind.Video);
            double fit = Math.Min((double)seq.Width / width, (double)seq.Height / height);
            long frames = Math.Max(1, seq.Fps.Frames(seconds));
            for (int i = 0; i < assets.Count; i++)
            {
                var (a, l) = assets[i];
                var track = new EditTrack { Name = l.Name, Kind = TrackKind.Video, Hidden = !l.Visible };
                track.Clips.Add(new TimelineClip { MediaId = a.Id, Name = l.Name, Duration = frames, StreamIntent = ClipStreamIntent.VideoOnly, Opacity = l.Opacity, Transform = new ClipTransform { ScaleX = width * fit / seq.Width, ScaleY = height * fit / seq.Height } });
                seq.Tracks.Insert(i, track);
            }
            EditCommands.EnsureSequenceMedia(p, seq);
        });
        return assets.Select(a => a.Asset.Id).ToList();
    }
}
