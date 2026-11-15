using System.Globalization;

namespace ZenithClipper;

/// <summary>Look shared by the panel and the editor: accent colour, background tone and motion. Stored as a tiny
/// key=value file next to settings.json so both apps (and the trimmed engine) read it without a JSON context.
/// Changing it never touches recording.</summary>
public sealed record Appearance
{
    public string Accent { get; init; } = DefaultAccent;
    public string Tone { get; init; } = "Graphite";
    public bool ReduceMotion { get; init; }

    public const string DefaultAccent = "#FF6B4A";
    public static readonly (string Name, string Hex)[] AccentPresets =
    [
        ("Ember", "#FF6B4A"), ("Crimson", "#F0485A"), ("Amber", "#F5A524"), ("Lime", "#8BD346"), ("Mint", "#3ECF8E"),
        ("Cyan", "#2BC4E8"), ("Azure", "#4A8BFF"), ("Violet", "#9B7BFF"), ("Pink", "#FF5FA2"), ("Silver", "#C9CCD3"),
    ];
    public static readonly string[] Tones = ["Graphite", "Midnight", "Slate", "Black"];

    public static string File => Path.Combine(AppPaths.Root, "appearance.ini");

    public static Appearance Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new();
            var a = new Appearance();
            foreach (var line in System.IO.File.ReadAllLines(File))
            {
                int eq = line.IndexOf('='); if (eq <= 0) continue;
                string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
                a = key switch
                {
                    "accent" when TryParse(value, out _) => a with { Accent = Normalize(value) },
                    "tone" when Tones.Contains(value, StringComparer.OrdinalIgnoreCase) => a with { Tone = Tones.First(t => t.Equals(value, StringComparison.OrdinalIgnoreCase)) },
                    "reduce_motion" => a with { ReduceMotion = value is "1" or "true" },
                    _ => a,
                };
            }
            return a;
        }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Root);
        string temp = File + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            System.IO.File.WriteAllText(temp, $"accent={Normalize(Accent)}\ntone={Tone}\nreduce_motion={(ReduceMotion ? 1 : 0)}\n");
            System.IO.File.Move(temp, File, true);
        }
        finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
    }

    // ---- Colours, as plain RGB so WinForms and Avalonia can both use them ----

    public static bool TryParse(string hex, out (byte R, byte G, byte B) rgb)
    {
        rgb = default; hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
        rgb = ((byte)(v >> 16), (byte)(v >> 8), (byte)v); return true;
    }
    static string Normalize(string hex) { TryParse(hex, out var c); return $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }
    public (byte R, byte G, byte B) AccentRgb => TryParse(Accent, out var c) ? c : ((byte)255, (byte)107, (byte)74);

    /// <summary>Text colour that reads on the accent: near-black ink on light accents, white on dark ones.</summary>
    public (byte R, byte G, byte B) InkRgb
    {
        get
        {
            var (r, g, b) = AccentRgb;
            double lum = 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
            return lum > 0.18 ? ((byte)26, (byte)13, (byte)8) : ((byte)255, (byte)255, (byte)255);
            static double Lin(byte c) { double v = c / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        }
    }

    /// <summary>Re-tints one of the Graphite neutrals for the chosen tone. Graphite returns it unchanged.</summary>
    public (byte R, byte G, byte B) Neutral(byte r, byte g, byte b) => Tone switch
    {
        "Midnight" => (Clamp(r - 3), Clamp(g - 1), Clamp(b + 7)),
        "Slate" => (Clamp(r + 9), Clamp(g + 10), Clamp(b + 13)),
        "Black" => (Clamp(r * .5), Clamp(g * .5), Clamp(b * .5)),
        _ => (r, g, b),
    };
    static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    public static (byte R, byte G, byte B) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t) =>
        (Clamp(a.R + (b.R - a.R) * t), Clamp(a.G + (b.G - a.G) * t), Clamp(a.B + (b.B - a.B) * t));
}
