namespace ZenithClipper.Mlt;

/// <summary>
/// ITU-R BS.1770 / EBU R128 loudness for 48 kHz stereo 16-bit PCM: K-weighting, 400 ms momentary, 3 s short-term and
/// gated integrated loudness (absolute -70 LUFS, relative -10 LU), plus true-ish sample peak.
/// </summary>
public sealed class LoudnessMeter
{
    // K-weighting biquads for 48 kHz (BS.1770-4).
    static readonly double[] B1 = [1.53512485958697, -2.69169618940638, 1.19839281085285], A1 = [-1.69065929318241, 0.73248077421585];
    static readonly double[] B2 = [1.0, -2.0, 1.0], A2 = [-1.99004745483398, 0.99007225036621];
    readonly double[,] z = new double[2, 4];
    const int Hop = 4800; // 100 ms
    double hopSum; int hopCount;
    readonly Queue<double> hops = new(); // mean-square energy (L+R) per 100 ms, last 3 s
    readonly List<double> blocks = []; // 400 ms block energies for integration
    readonly object gate = new();
    double peak;

    public void Reset() { lock (gate) { Array.Clear(z); hopSum = 0; hopCount = 0; hops.Clear(); blocks.Clear(); peak = 0; } }

    public void Add(byte[] pcm)
    {
        lock (gate)
        for (int i = 0; i + 3 < pcm.Length; i += 4)
        {
            double sum = 0;
            for (int ch = 0; ch < 2; ch++)
            {
                double x = (short)(pcm[i + ch * 2] | pcm[i + ch * 2 + 1] << 8) / 32768.0;
                peak = Math.Max(peak, Math.Abs(x));
                // Two cascaded direct-form-II-transposed biquads.
                double y1 = B1[0] * x + z[ch, 0]; z[ch, 0] = B1[1] * x - A1[0] * y1 + z[ch, 1]; z[ch, 1] = B1[2] * x - A1[1] * y1;
                double y2 = B2[0] * y1 + z[ch, 2]; z[ch, 2] = B2[1] * y1 - A2[0] * y2 + z[ch, 3]; z[ch, 3] = B2[2] * y1 - A2[1] * y2;
                sum += y2 * y2;
            }
            hopSum += sum; hopCount++;
            if (hopCount == Hop)
            {
                hops.Enqueue(hopSum / Hop); hopSum = 0; hopCount = 0;
                while (hops.Count > 30) hops.Dequeue();
                if (hops.Count >= 4) blocks.Add(hops.Skip(hops.Count - 4).Average());
            }
        }
    }

    static double Lufs(double meanSquare) => meanSquare <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(meanSquare);

    public readonly record struct Reading(double Momentary, double ShortTerm, double Integrated, double PeakDb);

    public Reading Read()
    {
        lock (gate)
        {
            var h = hops.ToArray();
            double m = h.Length >= 4 ? Lufs(h[^4..].Average()) : double.NegativeInfinity;
            double s = h.Length >= 30 ? Lufs(h.Average()) : h.Length > 0 ? Lufs(h.Average()) : double.NegativeInfinity;
            return new(m, s, Integrated(blocks), peak > 0 ? 20 * Math.Log10(peak) : double.NegativeInfinity);
        }
    }

    static double Integrated(List<double> blocks)
    {
        var loud = blocks.Where(b => Lufs(b) > -70).ToList(); if (loud.Count == 0) return double.NegativeInfinity;
        double relative = Lufs(loud.Average()) - 10;
        var gated = loud.Where(b => Lufs(b) > relative).ToList();
        return gated.Count == 0 ? double.NegativeInfinity : Lufs(gated.Average());
    }

    /// <summary>Integrated loudness of a whole 48 kHz stereo 16-bit WAV file.</summary>
    public static Reading MeasureWav(string path)
    {
        var meter = new LoudnessMeter();
        using var file = File.OpenRead(path); using var reader = new BinaryReader(file);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a WAV file."); reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAV file.");
        while (file.Position + 8 <= file.Length)
        {
            string id = new(reader.ReadChars(4)); int size = reader.ReadInt32();
            if (id == "fmt ")
            {
                short format = reader.ReadInt16(), channels = reader.ReadInt16(); int rate = reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt16(); short bits = reader.ReadInt16();
                if (format != 1 || channels != 2 || rate != 48000 || bits != 16) throw new InvalidDataException("Loudness needs 48 kHz stereo 16-bit PCM.");
                file.Position += size - 16;
            }
            else if (id == "data")
            {
                long remaining = size <= 0 ? file.Length - file.Position : Math.Min(size, file.Length - file.Position); var buffer = new byte[48000 * 4];
                while (remaining > 0) { int n = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); if (n <= 0) break; remaining -= n; meter.Add(n == buffer.Length ? buffer : buffer[..n]); }
                break;
            }
            else file.Position += size + (size & 1);
        }
        return meter.Read();
    }
}
