namespace ZenithClipper.Editing;

/// <summary>Premiere's Align and Distribute for clips on the program: moves Position only.</summary>
public static class ClipAlign
{
    public static readonly string[] Modes = ["left", "center", "right", "top", "middle", "bottom", "distribute_h", "distribute_v"];

    /// <summary>Aligns or distributes the video clips <paramref name="ids"/>. One clip (or <paramref name="toFrame"/>) aligns to the
    /// frame; several align to their combined bounds. Keyframed positions shift by the same amount. Returns how many moved.</summary>
    public static int Apply(EditProject p, Guid s, IEnumerable<Guid> ids, string mode, bool toFrame)
    {
        var seq = p.Sequences.First(x => x.Id == s); double W = seq.Width, H = seq.Height;
        var clips = seq.Tracks.Where(t => t.Kind == TrackKind.Video && !t.Locked).SelectMany(t => t.Clips).Where(c => ids.Contains(c.Id)).ToList();
        if (clips.Count == 0) return 0;
        var box = clips.ToDictionary(c => c, c => { var t = c.Transform; var g = TransformGeometry.Place(W, H, t.X, t.Y, t.ScaleX, t.ScaleY, 0, t); return (L: g.Left, T: g.Top, R: g.Left + g.W, B: g.Top + g.H); });
        bool frame = toFrame || clips.Count == 1;
        double L = frame ? 0 : box.Values.Min(b => b.L), T = frame ? 0 : box.Values.Min(b => b.T), R = frame ? W : box.Values.Max(b => b.R), B = frame ? H : box.Values.Max(b => b.B);
        var moves = new Dictionary<TimelineClip, (double Dx, double Dy)>();
        switch (mode)
        {
            case "left": foreach (var (c, b) in box) moves[c] = (L - b.L, 0); break;
            case "center": foreach (var (c, b) in box) moves[c] = ((L + R) / 2 - (b.L + b.R) / 2, 0); break;
            case "right": foreach (var (c, b) in box) moves[c] = (R - b.R, 0); break;
            case "top": foreach (var (c, b) in box) moves[c] = (0, T - b.T); break;
            case "middle": foreach (var (c, b) in box) moves[c] = (0, (T + B) / 2 - (b.T + b.B) / 2); break;
            case "bottom": foreach (var (c, b) in box) moves[c] = (0, B - b.B); break;
            case "distribute_h" or "distribute_v":
            {
                bool h = mode == "distribute_h"; if (clips.Count < 3 && !frame) return 0;
                var order = box.OrderBy(x => h ? (x.Value.L + x.Value.R) / 2 : (x.Value.T + x.Value.B) / 2).ToList();
                double first = frame ? (h ? L : T) : h ? (order[0].Value.L + order[0].Value.R) / 2 : (order[0].Value.T + order[0].Value.B) / 2;
                double last = frame ? (h ? R : B) : h ? (order[^1].Value.L + order[^1].Value.R) / 2 : (order[^1].Value.T + order[^1].Value.B) / 2;
                for (int i = 0; i < order.Count; i++)
                {
                    // To the frame, centres sit in equal slots; among clips, the outer two stay put.
                    double target = frame ? first + (last - first) * (i + .5) / order.Count : first + (last - first) * i / (order.Count - 1);
                    var b = order[i].Value; double centre = h ? (b.L + b.R) / 2 : (b.T + b.B) / 2;
                    moves[order[i].Key] = h ? (target - centre, 0) : (0, target - centre);
                }
                break;
            }
            default: throw new ArgumentException("Unknown align mode " + mode);
        }
        int moved = 0;
        foreach (var (c, (dx, dy)) in moves)
        {
            if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) continue; moved++;
            c.Transform.X += dx / W; c.Transform.Y += dy / H;
            foreach (var k in c.Motion) { if (k.Parameter == "x") k.Value += dx / W; else if (k.Parameter == "y") k.Value += dy / H; }
        }
        return moved;
    }
}
