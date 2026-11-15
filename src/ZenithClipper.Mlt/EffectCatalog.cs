using System.Globalization;
using ZenithClipper.Editing;

namespace ZenithClipper.Mlt;

public enum EffectKind { Video, Audio }
public enum ParamType { Number, Choice, Color, File }

/// <summary>One adjustable value of a catalog effect.</summary>
public sealed record EffectParam(string Key, string Label, double Min, double Max, double Default, string Unit = "", ParamType Type = ParamType.Number, string[]? Choices = null, string? TextDefault = null)
{
    public string DefaultText => Type == ParamType.Number ? Default.ToString("0.###", CultureInfo.InvariantCulture) : TextDefault ?? Choices?.FirstOrDefault() ?? "";
}

/// <summary>A Premiere-style effect: a name and category for the effects panel, its parameters, and how it compiles to MLT filters.</summary>
public sealed record EffectDef(string Id, string Name, string Category, EffectKind Kind, EffectParam[] Params, Func<Func<string, string>, IEnumerable<(string Service, Dictionary<string, string> Properties)>> Compile, string Description = "");

/// <summary>
/// The effects library (Premiere's Effects panel, minus AI and stock). Every entry compiles to bundled MLT services,
/// mostly ffmpeg filters through MLT's avfilter module, and was checked by rendering with melt.
/// </summary>
public static class EffectCatalog
{
    static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
    static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    static (string, Dictionary<string, string>) Av(string filter, params (string Key, string Value)[] options)
    {
        var p = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in options) p["av." + k] = v;
        return ("avfilter." + filter, p);
    }
    /// <summary>One sox effect per filter: MLT's ffmpeg biquad filters (equalizer, bass, highpass…) pass audio through unchanged, sox does not.</summary>
    static (string, Dictionary<string, string>) Sox(string effect) => ("sox", new() { ["effect"] = effect });
    static EffectParam Num(string key, string label, double min, double max, double def, string unit = "") => new(key, label, min, max, def, unit);
    static EffectParam Pick(string key, string label, params string[] choices) => new(key, label, 0, 0, 0, "", ParamType.Choice, choices);

    public static readonly EffectDef[] All =
    [
        // ---- Lumetri Color ----
        new("lumetri_basic", "Lumetri: Basic Correction", "Lumetri Color", EffectKind.Video,
        [
            Num("temperature", "Temperature", -100, 100, 0), Num("tint", "Tint", -100, 100, 0),
            Num("exposure", "Exposure", -5, 5, 0, "stops"), Num("contrast", "Contrast", -100, 100, 0),
            Num("highlights", "Highlights", -100, 100, 0), Num("shadows", "Shadows", -100, 100, 0),
            Num("whites", "Whites", -100, 100, 0), Num("blacks", "Blacks", -100, 100, 0),
            Num("saturation", "Saturation", 0, 200, 100), Num("vibrance", "Vibrance", -100, 100, 0),
        ], v => LumetriBasic(v), "White balance, tone and saturation in one pass."),
        new("lumetri_wheels", "Lumetri: Color Wheels", "Lumetri Color", EffectKind.Video,
        [
            Num("sr", "Shadows red", -100, 100, 0), Num("sg", "Shadows green", -100, 100, 0), Num("sb", "Shadows blue", -100, 100, 0),
            Num("mr", "Midtones red", -100, 100, 0), Num("mg", "Midtones green", -100, 100, 0), Num("mb", "Midtones blue", -100, 100, 0),
            Num("hr", "Highlights red", -100, 100, 0), Num("hg", "Highlights green", -100, 100, 0), Num("hb", "Highlights blue", -100, 100, 0),
        ], v => [Av("colorbalance", ("rs", F(D(v("sr")) / 100)), ("gs", F(D(v("sg")) / 100)), ("bs", F(D(v("sb")) / 100)), ("rm", F(D(v("mr")) / 100)), ("gm", F(D(v("mg")) / 100)), ("bm", F(D(v("mb")) / 100)), ("rh", F(D(v("hr")) / 100)), ("gh", F(D(v("hg")) / 100)), ("bh", F(D(v("hb")) / 100)))]),
        new("lumetri_curves", "Lumetri: Curves", "Lumetri Color", EffectKind.Video,
        [Pick("preset", "Curve", "increase_contrast", "strong_contrast", "medium_contrast", "linear_contrast", "lighter", "darker", "vintage", "cross_process", "color_negative", "negative")],
            v => [Av("curves", ("preset", v("preset")))]),
        new("lumetri_hsl", "Lumetri: Hue / Saturation", "Lumetri Color", EffectKind.Video,
        [Num("hue", "Hue shift", -180, 180, 0, "°"), Num("saturation", "Saturation", 0, 300, 100, "%"), Num("brightness", "Brightness", -10, 10, 0)],
            v => [Av("hue", ("h", v("hue")), ("s", F(D(v("saturation")) / 100)), ("b", v("brightness")))]),
        new("lut", "Lumetri: Look (LUT)", "Lumetri Color", EffectKind.Video,
        [new("file", "LUT file (.cube)", 0, 0, 0, "", ParamType.File)], v => [Av("lut3d", ("file", v("file")), ("interp", "tetrahedral"))]),
        new("vignette", "Lumetri: Vignette", "Lumetri Color", EffectKind.Video,
        [Num("amount", "Amount", 0, 100, 50)], v => [Av("vignette", ("angle", F(D(v("amount")) / 100 * Math.PI / 2.2)))]),
        new("auto_color", "Auto Color", "Color Correction", EffectKind.Video, [Num("strength", "Strength", 0, 100, 50)],
            v => [Av("normalize", ("strength", F(D(v("strength")) / 100)), ("smoothing", "20"))]),
        // ---- Image control ----
        new("black_white", "Black & White", "Image Control", EffectKind.Video, [], _ => [Av("hue", ("s", "0"))]),
        new("invert", "Invert", "Channel", EffectKind.Video, [], _ => [Av("negate")]),
        new("tint_color", "Tint", "Image Control", EffectKind.Video, [Num("amount", "Amount", 0, 100, 50), new("color", "Color", 0, 0, 0, "", ParamType.Color, null, "#FF6B4A")],
            v => { var (r, g, b) = Rgb(v("color")); double a = D(v("amount")) / 100; return [Av("hue", ("s", F(1 - a))), Av("colorbalance", ("rm", F((r - .5) * a)), ("gm", F((g - .5) * a)), ("bm", F((b - .5) * a)))]; }),
        new("posterize", "Posterize", "Stylize", EffectKind.Video, [Num("levels", "Levels", 2, 32, 6)],
            v => { string step = F(256 / Math.Round(D(v("levels")))); string e = $"floor(val/{step})*{step}"; return [Av("lutrgb", ("r", e), ("g", e), ("b", e))]; }),
        // ---- Blur & sharpen ----
        new("gaussian_blur", "Gaussian Blur", "Blur & Sharpen", EffectKind.Video, [Num("amount", "Blurriness", 0, 100, 10)], v => [Av("gblur", ("sigma", F(D(v("amount")) / 2)))]),
        new("directional_blur", "Directional Blur", "Blur & Sharpen", EffectKind.Video, [Num("angle", "Direction", 0, 360, 45, "°"), Num("length", "Blur length", 1, 100, 10)],
            v => [Av("dblur", ("angle", v("angle")), ("radius", v("length")))]),
        new("sharpen", "Sharpen", "Blur & Sharpen", EffectKind.Video, [Num("amount", "Amount", 0, 100, 30)], v => [Av("unsharp", ("luma_msize_x", "5"), ("luma_msize_y", "5"), ("luma_amount", F(D(v("amount")) / 100 * 3)))]),
        new("unsharp_mask", "Unsharp Mask", "Blur & Sharpen", EffectKind.Video, [Num("amount", "Amount", 0, 500, 50, "%"), Num("radius", "Radius", 1, 6, 3)],
            // FFmpeg's unsharp caps the matrix at 13×13.
            v => { string size = F(Math.Clamp(Math.Round(D(v("radius"))), 1, 6) * 2 + 1); return [Av("unsharp", ("luma_msize_x", size), ("luma_msize_y", size), ("luma_amount", F(Math.Clamp(D(v("amount")) / 100, -2, 5))))]; }),
        // ---- Distort / transform ----
        new("flip_h", "Horizontal Flip", "Transform", EffectKind.Video, [], _ => [Av("hflip")]),
        new("flip_v", "Vertical Flip", "Transform", EffectKind.Video, [], _ => [Av("vflip")]),
        new("lens_distortion", "Lens Distortion", "Distort", EffectKind.Video, [Num("curvature", "Curvature", -100, 100, 20)],
            v => { double k = D(v("curvature")) / 100; return [Av("lenscorrection", ("k1", F(k * .5)), ("k2", F(k * .2)))]; }),
        // FFmpeg's deshake only accepts search ranges that are multiples of 16.
        new("stabilize", "Stabilize", "Distort", EffectKind.Video, [Num("smoothness", "Search range", 16, 64, 16, "px")],
            v => { string r = F(Math.Clamp(Math.Round(D(v("smoothness")) / 16) * 16, 16, 64)); return [Av("deshake", ("rx", r), ("ry", r), ("edge", "3"))]; }),
        // ---- Keying ----
        new("ultra_key", "Ultra Key (chroma)", "Keying", EffectKind.Video, [new("color", "Key color", 0, 0, 0, "", ParamType.Color, null, "#00FF00"), Num("similarity", "Tolerance", 1, 100, 15), Num("blend", "Softness", 0, 100, 5)],
            v => [Av("chromakey", ("color", "0x" + v("color").TrimStart('#')), ("similarity", F(D(v("similarity")) / 100)), ("blend", F(D(v("blend")) / 100)))]),
        new("luma_key", "Luma Key", "Keying", EffectKind.Video, [Num("threshold", "Threshold", 0, 100, 0), Num("tolerance", "Tolerance", 0, 100, 10), Num("softness", "Softness", 0, 100, 0)],
            v => [Av("lumakey", ("threshold", F(D(v("threshold")) / 100)), ("tolerance", F(D(v("tolerance")) / 100)), ("softness", F(D(v("softness")) / 100)))]),
        // ---- Noise ----
        new("noise", "Noise / Film Grain", "Noise & Grain", EffectKind.Video, [Num("amount", "Amount", 0, 100, 20)], v => [Av("noise", ("alls", F(Math.Round(D(v("amount"))))), ("allf", "t+u"))]),
        new("median", "Median", "Noise & Grain", EffectKind.Video, [Num("radius", "Radius", 1, 20, 2)], v => [Av("median", ("radius", F(Math.Round(D(v("radius"))))))]),
        new("denoise", "Reduce Noise", "Noise & Grain", EffectKind.Video, [Num("strength", "Strength", 0, 100, 30)],
            v => { double s = D(v("strength")) / 100 * 10; return [Av("hqdn3d", ("luma_spatial", F(s)), ("chroma_spatial", F(s * .75)), ("luma_tmp", F(s * 1.5)), ("chroma_tmp", F(s * 1.1)))]; }),
        // ---- Stylize ----
        new("mosaic", "Mosaic", "Stylize", EffectKind.Video, [Num("blocks", "Block size", 2, 128, 16, "px")], v => [Av("pixelize", ("width", F(Math.Round(D(v("blocks"))))), ("height", F(Math.Round(D(v("blocks"))))))]),
        new("find_edges", "Find Edges", "Stylize", EffectKind.Video, [], _ => [Av("edgedetect", ("mode", "colormix"))]),
        new("emboss", "Emboss", "Stylize", EffectKind.Video, [], _ => [Av("convolution", ("0m", "-2 -1 0 -1 1 1 0 1 2"), ("1m", "-2 -1 0 -1 1 1 0 1 2"), ("2m", "-2 -1 0 -1 1 1 0 1 2"))]),
        new("echo", "Echo", "Time", EffectKind.Video, [Num("frames", "Echoes", 2, 10, 4)], v => [Av("tmix", ("frames", F(Math.Round(D(v("frames"))))))]),
        // ---- More Color Correction / Image Control ----
        new("brightness_contrast", "Brightness & Contrast", "Color Correction", EffectKind.Video, [Num("brightness", "Brightness", -100, 100, 0), Num("contrast", "Contrast", -100, 100, 0)],
            v => [Av("eq", ("brightness", F(D(v("brightness")) / 200)), ("contrast", F(1 + D(v("contrast")) / 100)))]),
        new("gamma", "Gamma Correction", "Color Correction", EffectKind.Video, [Num("gamma", "Gamma", 10, 300, 100, "%")], v => [Av("eq", ("gamma", F(D(v("gamma")) / 100)))]),
        new("levels", "Levels", "Color Correction", EffectKind.Video, [Num("in_black", "Input black", 0, 254, 0), Num("in_white", "Input white", 1, 255, 255), Num("out_black", "Output black", 0, 255, 0), Num("out_white", "Output white", 0, 255, 255)],
            v => { string ib = F(D(v("in_black")) / 255), iw = F(Math.Max(D(v("in_black")) + 1, D(v("in_white"))) / 255), ob = F(D(v("out_black")) / 255), ow = F(D(v("out_white")) / 255);
                   return [Av("colorlevels", ("rimin", ib), ("gimin", ib), ("bimin", ib), ("rimax", iw), ("gimax", iw), ("bimax", iw), ("romin", ob), ("gomin", ob), ("bomin", ob), ("romax", ow), ("gomax", ow), ("bomax", ow))]; }),
        new("color_balance", "Color Balance", "Color Correction", EffectKind.Video,
        [Num("rs", "Shadow red", -100, 100, 0), Num("gs", "Shadow green", -100, 100, 0), Num("bs", "Shadow blue", -100, 100, 0), Num("rm", "Midtone red", -100, 100, 0), Num("gm", "Midtone green", -100, 100, 0), Num("bm", "Midtone blue", -100, 100, 0), Num("rh", "Highlight red", -100, 100, 0), Num("gh", "Highlight green", -100, 100, 0), Num("bh", "Highlight blue", -100, 100, 0)],
            v => [Av("colorbalance", [.. new[] { "rs", "gs", "bs", "rm", "gm", "bm", "rh", "gh", "bh" }.Select(k => (k, F(D(v(k)) / 100)))])]),
        new("channel_mixer", "Channel Mixer", "Channel", EffectKind.Video,
        [Num("rr", "Red from red", -200, 200, 100, "%"), Num("rg", "Red from green", -200, 200, 0, "%"), Num("rb", "Red from blue", -200, 200, 0, "%"), Num("gr", "Green from red", -200, 200, 0, "%"), Num("gg", "Green from green", -200, 200, 100, "%"), Num("gb", "Green from blue", -200, 200, 0, "%"), Num("br", "Blue from red", -200, 200, 0, "%"), Num("bg", "Blue from green", -200, 200, 0, "%"), Num("bb", "Blue from blue", -200, 200, 100, "%")],
            v => [Av("colorchannelmixer", [.. new[] { "rr", "rg", "rb", "gr", "gg", "gb", "br", "bg", "bb" }.Select(k => (k, F(D(v(k)) / 100)))])]),
        new("hue_rotate", "Change Hue", "Color Correction", EffectKind.Video, [Num("hue", "Hue shift", -180, 180, 30, "°")], v => [Av("hue", ("h", v("hue")))]),
        new("leave_color", "Leave Color", "Color Correction", EffectKind.Video, [new("color", "Color to leave", 0, 0, 0, "", ParamType.Color, null, "#FF0000"), Num("tolerance", "Tolerance", 1, 100, 25), Num("softness", "Edge softness", 0, 100, 10)],
            v => [Av("colorhold", ("color", "0x" + v("color").TrimStart('#')), ("similarity", F(D(v("tolerance")) / 100)), ("blend", F(D(v("softness")) / 100)))]),
        new("hsl_secondary", "HSL Secondary", "Lumetri Color", EffectKind.Video,
        [Pick("range", "Colors", "Reds", "Yellows", "Greens", "Cyans", "Blues", "Magentas", "Skin (reds + yellows)"), Num("hue", "Hue shift", -180, 180, 0, "°"), Num("saturation", "Saturation", -100, 100, 0), Num("lightness", "Lightness", -100, 100, 0)],
            v => { string range = v("range") switch { "Yellows" => "y", "Greens" => "g", "Cyans" => "c", "Blues" => "b", "Magentas" => "m", "Skin (reds + yellows)" => "r+y", _ => "r" };
                   return [Av("huesaturation", ("colors", range), ("hue", v("hue")), ("saturation", F(D(v("saturation")) / 100)), ("intensity", F(D(v("lightness")) / 100)), ("strength", "6"))]; }),
        new("sepia", "Sepia", "Image Control", EffectKind.Video, [Num("amount", "Amount", 0, 100, 100, "%")],
            v => { double a = D(v("amount")) / 100; double M(double sep, double id) => id + (sep - id) * a;
                   return [Av("colorchannelmixer", ("rr", F(M(.393, 1))), ("rg", F(M(.769, 0))), ("rb", F(M(.189, 0))), ("gr", F(M(.349, 0))), ("gg", F(M(.686, 1))), ("gb", F(M(.168, 0))), ("br", F(M(.272, 0))), ("bg", F(M(.534, 0))), ("bb", F(M(.131, 1))))]; }),
        new("threshold", "Threshold", "Stylize", EffectKind.Video, [Num("level", "Level", 0, 255, 128)],
            v => [Av("lutyuv", ("y", $"if(gt(val,{F(D(v("level")))}),235,16)"), ("u", "128"), ("v", "128"))]),
        // ---- More Distort / Stylize ----
        new("corner_pin", "Corner Pin", "Distort", EffectKind.Video,
        [Num("x0", "Top left X", -50, 150, 0, "%"), Num("y0", "Top left Y", -50, 150, 0, "%"), Num("x1", "Top right X", -50, 150, 100, "%"), Num("y1", "Top right Y", -50, 150, 0, "%"), Num("x2", "Bottom left X", -50, 150, 0, "%"), Num("y2", "Bottom left Y", -50, 150, 100, "%"), Num("x3", "Bottom right X", -50, 150, 100, "%"), Num("y3", "Bottom right Y", -50, 150, 100, "%")],
            // perspective maps these source corners to the frame corners, so place the inverse: sense=destination.
            // A transparent 1px border first: perspective repeats edge pixels outside the picture, so they become clear.
            v => [Av("drawbox", ("x", "0"), ("y", "0"), ("w", "iw"), ("h", "ih"), ("t", "1"), ("color", "black@0"), ("replace", "1")), Av("perspective", [.. Enumerable.Range(0, 4).SelectMany(i => new[] { ($"x{i}", $"W*{F(D(v($"x{i}")) / 100)}"), ($"y{i}", $"H*{F(D(v($"y{i}")) / 100)}") }), ("sense", "destination"), ("interpolation", "linear")])]),
        // scroll with zero speed is a static, wrapping shift (its start position is a fraction of the frame).
        new("offset", "Offset", "Distort", EffectKind.Video, [Num("x", "Shift X", -100, 100, 0, "%"), Num("y", "Shift Y", -100, 100, 0, "%")],
            v => { double W(double f) => ((f % 1) + 1) % 1; return [Av("scroll", ("h", "0"), ("v", "0"), ("hpos", F(W(-D(v("x")) / 100))), ("vpos", F(W(-D(v("y")) / 100))))]; }),
        new("chromatic_aberration", "Chromatic Aberration", "Stylize", EffectKind.Video, [Num("amount", "Split", 0, 40, 6, "px")],
            v => { string a = F(Math.Round(D(v("amount")))); return [Av("rgbashift", ("rh", "-" + a), ("bh", a))]; }),
        new("mirror", "Mirror", "Distort", EffectKind.Video, [Pick("side", "Reflect", "Left onto right", "Right onto left", "Top onto bottom", "Bottom onto top")],
            v => [("mirror", new() { ["mirror"] = v("side").StartsWith("Top") || v("side").StartsWith("Bottom") ? "vertical" : "horizontal", ["reverse"] = v("side").StartsWith("Right") || v("side").StartsWith("Bottom") ? "1" : "0" })]),
        new("posterize_time", "Posterize Time", "Time", EffectKind.Video, [Num("fps", "Frame rate", 1, 30, 8, "fps")],
            // choppy repeats each frame N times; N from the target rate assuming a 30 fps-ish sequence.
            v => [("choppy", new() { ["amount"] = F(Math.Max(1, Math.Round(30 / Math.Max(1, D(v("fps")))))) })]),
        new("strobe", "Strobe Light", "Stylize", EffectKind.Video, [Num("interval", "Interval", 1, 30, 4, "frames")], v => [("strobe", new() { ["interval"] = F(Math.Round(D(v("interval")))) })]),
        new("old_film", "Old Film", "Stylize", EffectKind.Video, [Num("amount", "Amount", 0, 100, 50)],
            v => { double a = D(v("amount")) / 100; return [("oldfilm", new() { ["delta"] = F(Math.Round(28 * a)), ["every"] = F(Math.Round(40 * a)), ["brightnessdelta_up"] = F(Math.Round(30 * a)), ["brightnessdelta_down"] = F(Math.Round(40 * a)), ["brightnessdelta_every"] = F(Math.Round(70 * a)) }), ("grain", new() { ["noise"] = F(Math.Round(60 * a)), ["contrast"] = "100", ["brightness"] = "100" })]; }),
        new("charcoal", "Charcoal", "Stylize", EffectKind.Video, [Num("scale", "Strength", 1, 10, 2)], v => [("charcoal", new() { ["x_scatter"] = "1", ["y_scatter"] = "1", ["scale"] = F(D(v("scale"))), ["mix"] = "0", ["invert"] = "0" })]),
        new("wave", "Wave", "Distort", EffectKind.Video, [Num("amplitude", "Amplitude", 1, 50, 10), Num("speed", "Speed", 0, 20, 5)],
            v => [("wave", new() { ["wave"] = F(Math.Round(D(v("amplitude")))), ["speed"] = F(Math.Round(D(v("speed")))), ["deformX"] = "1", ["deformY"] = "1" })]),
        new("wave_warp", "Wave Warp", "Distort", EffectKind.Video, [Num("amplitude", "Wave height", 0, 100, 20), Num("frequency", "Wave width", 0, 100, 10)],
            v => [("frei0r.distort0r", new() { ["0"] = F(D(v("amplitude")) / 100), ["1"] = F(D(v("frequency")) / 1000) })]),
        // Drop Shadow is compiled as a silhouette copy of the clip placed under it (see MltXml), so it can fall outside the clip's frame.
        new("drop_shadow", "Drop Shadow", "Perspective", EffectKind.Video, [new("color", "Shadow color", 0, 0, 0, "", ParamType.Color, null, "#000000"), Num("opacity", "Opacity", 0, 100, 50, "%"), Num("direction", "Direction", 0, 360, 135, "°"), Num("distance", "Distance", 0, 500, 10, "px"), Num("softness", "Softness", 0, 100, 10)], _ => []),
        // Basic 3D: the frame turned about its vertical (swivel) and horizontal (tilt) axes and seen through a camera 2 frame
        // widths away; the projected corners drive a perspective warp (expressions in W and H keep any aspect right).
        new("basic_3d", "Basic 3D", "Perspective", EffectKind.Video, [Num("swivel", "Swivel", -90, 90, 0, "°"), Num("tilt", "Tilt", -90, 90, 0, "°"), Num("distance", "Distance to image", -50, 300, 0, "%")], v =>
        {
            double sw = D(v("swivel")) * Math.PI / 180, tl = D(v("tilt")) * Math.PI / 180, cs = Math.Cos(sw), ss = Math.Sin(sw), ct = Math.Cos(tl), st = Math.Sin(tl), k = 2, dz = D(v("distance")) / 100 * k;
            var opts = new List<(string, string)>();
            int i = 0;
            foreach (var (a, b) in new[] { (-.5, -.5), (.5, -.5), (-.5, .5), (.5, .5) })
            {
                string den = $"({F(k + dz)}*W+({F(b * st)})*H-({F(a * ss * ct)})*W)";
                opts.Add(($"x{i}", $"W/2+({F(a * cs)})*W*{F(k)}*W/{den}"));
                opts.Add(($"y{i}", $"H/2+(({F(b * ct)})*H+({F(a * ss * st)})*W)*{F(k)}*W/{den}"));
                i++;
            }
            opts.Add(("sense", "destination")); opts.Add(("interpolation", "linear"));
            return [Av("drawbox", ("x", "0"), ("y", "0"), ("w", "iw"), ("h", "ih"), ("t", "1"), ("color", "black@0"), ("replace", "1")), Av("perspective", [.. opts])];
        }),
        new("bevel_edges", "Bevel Edges", "Perspective", EffectKind.Video, [Num("thickness", "Edge thickness", 0, 25, 3, "%"), Num("light", "Light intensity", 0, 100, 40, "%")], v =>
        {
            string k = F(D(v("thickness")) / 100), l = F(D(v("light")) / 100);
            return [Av("drawbox", ("x", "0"), ("y", "0"), ("w", "iw"), ("h", $"ih*{k}"), ("color", $"white@{l}"), ("t", "fill")),
                    Av("drawbox", ("x", "0"), ("y", "0"), ("w", $"ih*{k}"), ("h", "ih"), ("color", $"white@{l}"), ("t", "fill")),
                    Av("drawbox", ("x", "0"), ("y", $"ih-ih*{k}"), ("w", "iw"), ("h", $"ih*{k}"), ("color", $"black@{l}"), ("t", "fill")),
                    Av("drawbox", ("x", $"iw-ih*{k}"), ("y", "0"), ("w", $"ih*{k}"), ("h", "ih"), ("color", $"black@{l}"), ("t", "fill"))];
        }),
        new("glow", "Glow", "Stylize", EffectKind.Video, [Num("amount", "Glow", 0, 100, 30)], v => [("frei0r.glow", new() { ["0"] = F(D(v("amount")) / 100) })]),
        new("cartoon", "Cartoon", "Stylize", EffectKind.Video, [Num("levels", "Edges", 0, 100, 60)], v => [("frei0r.cartoon", new() { ["0"] = F(D(v("levels")) / 100), ["1"] = "0.00390625" })]),
        new("color_halftone", "Color Halftone", "Stylize", EffectKind.Video, [Num("radius", "Dot size", 1, 100, 30)], v => [("frei0r.colorhalftone", new() { ["0"] = F(D(v("radius")) / 100) })]),
        new("glitch", "Glitch", "Stylize", EffectKind.Video, [Num("frequency", "Frequency", 0, 100, 40), Num("shift", "Shift", 0, 100, 50), Num("color", "Color glitch", 0, 100, 30)],
            v => [("frei0r.glitch0r", new() { ["0"] = F(D(v("frequency")) / 100), ["1"] = "0.5", ["2"] = F(D(v("shift")) / 100), ["3"] = F(D(v("color")) / 100) })]),
        // cairogradient's y axis points up, so a top-to-bottom ramp runs from y=1 to y=0.
        new("gradient_ramp", "Gradient Ramp", "Generate", EffectKind.Video,
        [new("start", "Start color", 0, 0, 0, "", ParamType.Color, null, "#000000"), new("end", "End color", 0, 0, 0, "", ParamType.Color, null, "#FFFFFF"), Num("opacity", "Opacity", 0, 100, 100, "%"), Pick("shape", "Shape", "Linear", "Radial")],
            v => { string o = F(D(v("opacity")) / 100); bool radial = v("shape") == "Radial";
                   return [("frei0r.cairogradient", new() { ["0"] = radial ? "gradient_radial" : "gradient_linear", ["1"] = v("start"), ["2"] = o, ["3"] = v("end"), ["4"] = o, ["5"] = "0.5", ["6"] = radial ? "0.5" : "1", ["7"] = radial ? "1" : "0.5", ["8"] = radial ? "0.5" : "0", ["9"] = "0", ["10"] = "normal" })]; }),
        new("letterbox", "Letterbox", "Transform", EffectKind.Video, [Num("amount", "Bar height", 0, 40, 12, "%")],
            v => { string h = $"ih*{F(D(v("amount")) / 100)}"; return [Av("drawbox", ("x", "0"), ("y", "0"), ("w", "iw"), ("h", h), ("color", "black"), ("t", "fill")), Av("drawbox", ("x", "0"), ("y", $"ih-{h}"), ("w", "iw"), ("h", h), ("color", "black"), ("t", "fill"))]; }),

        // ---- Audio ----
        new("bass", "Bass", "Audio: Filter & EQ", EffectKind.Audio, [Num("gain", "Boost", -20, 20, 6, "dB")], v => [Sox($"bass {F(D(v("gain")))}")]),
        new("treble", "Treble", "Audio: Filter & EQ", EffectKind.Audio, [Num("gain", "Boost", -20, 20, 6, "dB")], v => [Sox($"treble {F(D(v("gain")))}")]),
        new("bandpass", "Bandpass", "Audio: Filter & EQ", EffectKind.Audio, [Num("freq", "Center", 50, 15000, 1000, "Hz"), Num("q", "Q", 0.1, 10, 1)], v => [Sox($"bandpass {F(D(v("freq")))} {F(D(v("q")))}q")]),
        new("notch", "Notch Filter", "Audio: Filter & EQ", EffectKind.Audio, [Num("freq", "Frequency", 20, 15000, 1000, "Hz"), Num("q", "Q", 0.5, 30, 10)], v => [Sox($"bandreject {F(D(v("freq")))} {F(D(v("q")))}q")]),
        new("amplify", "Amplify", "Audio: Amplitude & Compression", EffectKind.Audio, [Num("gain", "Gain", -40, 24, 6, "dB")], v => [Sox($"gain {F(D(v("gain")))}")]),
        new("invert_audio", "Invert Polarity", "Audio: Channel", EffectKind.Audio, [], _ => [Sox("vol -1")]),
        new("chorus", "Chorus", "Audio: Modulation", EffectKind.Audio, [Num("depth", "Depth", 1, 100, 40)], v => [Sox($"chorus 0.7 0.9 55 0.4 {F(0.1 + D(v("depth")) / 100 * 0.4)} 2 -t")]),
        new("flanger", "Flanger", "Audio: Modulation", EffectKind.Audio, [Num("depth", "Depth", 0, 10, 2, "ms"), Num("speed", "Speed", 0.1, 10, 0.5, "Hz")], v => [Sox($"flanger 0 {F(D(v("depth")))} 0 71 {F(D(v("speed")))} sine 25 linear")]),
        new("phaser", "Phaser", "Audio: Modulation", EffectKind.Audio, [Num("speed", "Speed", 0.1, 2, 0.5, "Hz")], v => [Sox($"phaser 0.8 0.74 3 0.4 {F(D(v("speed")))} -t")]),
        new("tremolo", "Tremolo", "Audio: Modulation", EffectKind.Audio, [Num("speed", "Speed", 0.5, 20, 5, "Hz"), Num("depth", "Depth", 0, 100, 50, "%")], v => [Sox($"tremolo {F(D(v("speed")))} {F(D(v("depth")))}")]),
        new("pan", "Pan / Balance", "Audio: Channel", EffectKind.Audio, [Num("pan", "Pan", -100, 100, 0)],
            v => [("panner", new() { ["start"] = F(D(v("pan")) / 200 + .5), ["split"] = F(D(v("pan")) / 200 + .5), ["channel"] = "-1" })]),
        new("eq3", "EQ (Low / Mid / High)", "Audio: Filter & EQ", EffectKind.Audio, [Num("low", "Low (100 Hz)", -24, 24, 0, "dB"), Num("mid", "Mid (1 kHz)", -24, 24, 0, "dB"), Num("high", "High (8 kHz)", -24, 24, 0, "dB")],
            v => [Sox($"bass {v("low")} 100"), Sox($"equalizer 1000 1o {v("mid")}"), Sox($"treble {v("high")} 8000")]),
        new("parametric_eq", "Parametric EQ band", "Audio: Filter & EQ", EffectKind.Audio, [Num("frequency", "Frequency", 20, 20000, 1000, "Hz"), Num("gain", "Gain", -24, 24, 0, "dB"), Num("q", "Width", .1, 10, 1, "oct")],
            v => [Sox($"equalizer {v("frequency")} {v("q")}o {v("gain")}")]),
        new("highpass", "Highpass", "Audio: Filter & EQ", EffectKind.Audio, [Num("frequency", "Cutoff", 20, 2000, 80, "Hz")], v => [Sox($"highpass {v("frequency")}")]),
        new("lowpass", "Lowpass", "Audio: Filter & EQ", EffectKind.Audio, [Num("frequency", "Cutoff", 500, 20000, 8000, "Hz")], v => [Sox($"lowpass {v("frequency")}")]),
        new("dehum", "DeHummer", "Audio: Noise Reduction", EffectKind.Audio, [Pick("mains", "Mains", "60", "50")],
            v => [Sox($"bandreject {v("mains")} 8q"), Sox($"bandreject {F(D(v("mains")) * 2)} 8q"), Sox($"bandreject {F(D(v("mains")) * 3)} 8q")]),
        new("compressor", "Dynamics: Compressor", "Audio: Amplitude & Compression", EffectKind.Audio, [Num("threshold", "Threshold", -60, 0, -18, "dB"), Num("ratio", "Ratio", 1, 20, 4, ":1"), Num("attack", "Attack", .01, 200, 20, "ms"), Num("release", "Release", 1, 2000, 250, "ms"), Num("makeup", "Make-up gain", 0, 24, 0, "dB")],
            v => [Av("acompressor", ("threshold", F(Math.Pow(10, D(v("threshold")) / 20))), ("ratio", v("ratio")), ("attack", v("attack")), ("release", v("release")), ("makeup", F(Math.Pow(10, D(v("makeup")) / 20))))]),
        new("limiter", "Hard Limiter", "Audio: Amplitude & Compression", EffectKind.Audio, [Num("limit", "Maximum amplitude", -20, 0, -1, "dB")],
            v => [Av("alimiter", ("limit", F(Math.Pow(10, D(v("limit")) / 20))), ("level", "0"))]),
        new("gate", "Noise Gate", "Audio: Amplitude & Compression", EffectKind.Audio, [Num("threshold", "Threshold", -80, 0, -40, "dB")], v => [Av("agate", ("threshold", F(Math.Pow(10, D(v("threshold")) / 20))))]),
        new("denoise_audio", "DeNoise", "Audio: Noise Reduction", EffectKind.Audio, [Num("amount", "Reduction", 1, 40, 12, "dB")],
            // MLT can't host FFmpeg's planar-only denoisers (afftdn/anlmdn), so this is rumble removal plus a downward
            // expander that pushes the noise floor (below about -45 dB) down by the chosen amount.
            v => [Sox("highpass 70"), Sox($"compand 0.005,0.15 -90,{F(-90 - D(v("amount")))},-45,-45 0 -90 0")]),
        new("deesser", "DeEsser", "Audio: Noise Reduction", EffectKind.Audio, [Num("amount", "Intensity", 0, 100, 50)], v => [Av("deesser", ("i", F(D(v("amount")) / 100)))]),
        new("reverb", "Reverb", "Audio: Reverb", EffectKind.Audio, [Num("room", "Room size", 0, 100, 40), Num("mix", "Mix", 0, 100, 30, "%")],
            v => { double r = D(v("room")) / 100, m = D(v("mix")) / 100; return [Av("aecho", ("in_gain", F(1 - m * .4)), ("out_gain", F(.6 + m * .3)), ("delays", $"{F(20 + r * 40)}|{F(37 + r * 70)}|{F(59 + r * 110)}"), ("decays", $"{F(.25 + r * .3)}|{F(.2 + r * .25)}|{F(.15 + r * .2)}"))]; }),
        new("delay", "Delay", "Audio: Delay & Echo", EffectKind.Audio, [Num("time", "Delay time", 20, 2000, 300, "ms"), Num("feedback", "Feedback", 0, 90, 40, "%")],
            v => [Av("aecho", ("in_gain", "0.8"), ("out_gain", "0.9"), ("delays", v("time")), ("decays", F(D(v("feedback")) / 100)))]),
        new("pitch", "Pitch Shifter", "Audio: Time & Pitch", EffectKind.Audio, [Num("semitones", "Semitones", -12, 12, 0)],
            v => [("rbpitch", new() { ["pitchscale"] = F(Math.Pow(2, D(v("semitones")) / 12)) })]),
        new("loudness", "Loudness Normalize", "Audio: Amplitude & Compression", EffectKind.Audio, [Pick("target", "Target", "-14 LUFS (YouTube, streaming)", "-16 LUFS (podcast)", "-23 LUFS (broadcast)")],
            v => [("dynamic_loudness", new() { ["target_loudness"] = v("target").Split(' ')[0], ["window"] = "3", ["max_gain"] = "15", ["min_gain"] = "-15" })]),
        new("mono", "Mono (mix to both channels)", "Audio: Channel", EffectKind.Audio, [], _ => [("mono", new() { ["channels"] = "2" }), ("volume", new() { ["level"] = "-6dB" })]),
        new("fill_left", "Fill Right with Left", "Audio: Channel", EffectKind.Audio, [], _ => [("channelcopy", new() { ["from"] = "0", ["to"] = "1" })]),
        new("fill_right", "Fill Left with Right", "Audio: Channel", EffectKind.Audio, [], _ => [("channelcopy", new() { ["from"] = "1", ["to"] = "0" })]),
        new("swap_channels", "Swap Channels", "Audio: Channel", EffectKind.Audio, [], _ => [("channelcopy", new() { ["from"] = "0", ["to"] = "1", ["swap"] = "1" })]),
    ];

    public static EffectDef? Find(string id) => All.FirstOrDefault(e => e.Id == id);

    static (double, double, double) Rgb(string hex)
    {
        hex = hex.TrimStart('#'); if (hex.Length != 6) return (1, 1, 1);
        return (Convert.ToInt32(hex[..2], 16) / 255.0, Convert.ToInt32(hex[2..4], 16) / 255.0, Convert.ToInt32(hex[4..6], 16) / 255.0);
    }

    static IEnumerable<(string, Dictionary<string, string>)> LumetriBasic(Func<string, string> v)
    {
        double temperature = D(v("temperature")), tint = D(v("tint")), exposure = D(v("exposure")), contrast = D(v("contrast"));
        double highlights = D(v("highlights")) / 100, shadows = D(v("shadows")) / 100, whites = D(v("whites")) / 100, blacks = D(v("blacks")) / 100;
        double saturation = D(v("saturation")) / 100, vibrance = D(v("vibrance")) / 100;
        if (temperature != 0) yield return Av("colortemperature", ("temperature", F(6500 - temperature * 35)), ("mix", "1"), ("pl", "0.5"));
        if (tint != 0) yield return Av("colorbalance", ("gm", F(-tint / 250)), ("rm", F(tint / 500)), ("bm", F(tint / 500)));
        if (exposure != 0) yield return Av("exposure", ("exposure", F(exposure)));
        if (contrast != 0 || saturation != 1) yield return Av("eq", ("contrast", F(1 + contrast / 100)), ("saturation", F(saturation)));
        if (highlights != 0 || shadows != 0 || whites != 0 || blacks != 0)
        {
            // A five-point master curve: blacks lift/crush the toe, shadows/highlights bend the quarter points, whites move the top.
            double y0 = Math.Clamp(Math.Max(0, blacks) * .2, 0, .2), y1 = Math.Clamp(.25 + shadows * .15 + Math.Min(0, blacks) * .05, .02, .48), y3 = Math.Clamp(.75 + highlights * .15, .52, .98), y4 = Math.Clamp(1 + Math.Min(0, whites) * .2, .8, 1);
            double x4 = Math.Clamp(1 - Math.Max(0, whites) * .2, .8, 1), x0 = Math.Clamp(-Math.Min(0, blacks) * .2, 0, .2);
            yield return Av("curves", ("all", $"{F(x0)}/{F(y0)} 0.25/{F(y1)} 0.5/0.5 0.75/{F(y3)} {F(x4)}/{F(y4)}"));
        }
        if (vibrance != 0) yield return Av("vibrance", ("intensity", F(vibrance)));
    }
}
