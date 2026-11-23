using System.Drawing.Drawing2D;

namespace ZenithClipper.Windows.Ui;

/// <summary>Themed dropdown list for picker buttons. Replaces the stock Windows menu (light checkbox margin, square
/// corners) and closes itself only after the choice has been handed back, so nothing is used after disposal.</summary>
sealed class ChoicePopup : Form
{
    readonly IReadOnlyList<string> names;
    readonly int selected;
    readonly Action<int> pick;
    readonly int rowH = Theme.S(34), pad = Theme.S(6), visibleRows;
    int hover = -1, top;
    bool done;

    ChoicePopup(System.Windows.Forms.Control anchor, IReadOnlyList<string> names, int selected, Action<int> pick)
    {
        this.names = names; this.selected = selected; this.pick = pick;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; KeyPreview = true;
        BackColor = Theme.Raised; Font = Theme.Body;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        visibleRows = Math.Min(names.Count, 9);
        int widest = names.Count == 0 ? 0 : names.Max(n => Theme.Measure(n, Theme.Body).Width);
        var size = new Size(Math.Max(anchor.Width, widest + Theme.S(64)), visibleRows * rowH + pad * 2);
        var below = anchor.PointToScreen(new Point(0, anchor.Height + Theme.S(4)));
        var area = Screen.FromControl(anchor).WorkingArea;
        int y = below.Y + size.Height <= area.Bottom ? below.Y : anchor.PointToScreen(Point.Empty).Y - size.Height - Theme.S(4);
        Bounds = new Rectangle(Math.Clamp(below.X, area.Left, Math.Max(area.Left, area.Right - size.Width)), Math.Max(area.Top, y), size.Width, size.Height);
        if (selected >= visibleRows) top = Math.Min(selected - visibleRows / 2, names.Count - visibleRows);
        hover = selected;
    }

    /// <summary>Shows the list under <paramref name="anchor"/>; <paramref name="pick"/> gets the chosen index.</summary>
    public static void Show(System.Windows.Forms.Control anchor, IReadOnlyList<string> names, int selected, Action<int> pick)
    {
        var popup = new ChoicePopup(anchor, names, selected, pick);
        popup.FormClosed += (_, _) => popup.BeginInvoke(popup.Dispose);
        popup.Show(anchor.FindForm());
        Native.StyleWindow(popup);
        popup.Activate();
    }

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; cp.ExStyle |= 0x80; return cp; } } // drop shadow, tool window
    protected override bool ShowWithoutActivation => false;
    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Finish(-1); }

    void Finish(int index)
    {
        if (done) return; done = true;
        Close();
        if (index >= 0) pick(index);
    }

    int IndexAt(Point p) { int i = (p.Y - pad) / rowH + top; return p.Y < pad || i < 0 || i >= names.Count || i >= top + visibleRows ? -1 : i; }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = IndexAt(e.Location); if (h != hover && h >= 0) { hover = h; Invalidate(); } }
    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); int i = IndexAt(e.Location); if (i >= 0) Finish(i); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int next = Math.Clamp(top - Math.Sign(e.Delta), 0, Math.Max(0, names.Count - visibleRows));
        if (next != top) { top = next; Invalidate(); }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { Finish(-1); return; }
        if (e.KeyCode == Keys.Enter && hover >= 0) { Finish(hover); return; }
        int step = e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.Up ? -1 : 0; if (step == 0) return;
        hover = Math.Clamp((hover < 0 ? selected : hover) + step, 0, names.Count - 1);
        if (hover < top) top = hover; else if (hover >= top + visibleRows) top = hover - visibleRows + 1;
        Invalidate(); e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Raised); Theme.Prep(g);
        for (int row = 0; row < visibleRows; row++)
        {
            int i = top + row; if (i >= names.Count) break;
            var r = new RectangleF(pad, pad + row * rowH, Width - pad * 2, rowH);
            bool sel = i == selected, hot = i == hover;
            if (hot) Theme.Fill(g, RectangleF.Inflate(r, 0, -Theme.S(1f)), Theme.S(6f), Theme.Hover);
            // Selected item: accent bar and check, like the settings nav.
            if (sel)
            {
                Theme.Fill(g, new RectangleF(r.X + Theme.S(4f), r.Y + r.Height / 2 - Theme.S(8f), Theme.S(3f), Theme.S(16f)), Theme.S(1.5f), Theme.Accent);
                using var pen = new Pen(Theme.Accent, Theme.S(2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                float cx = r.Right - Theme.S(20f), cy = r.Y + r.Height / 2;
                g.DrawLines(pen, [new PointF(cx - Theme.S(5f), cy), new PointF(cx - Theme.S(1.5f), cy + Theme.S(3.5f)), new PointF(cx + Theme.S(5f), cy - Theme.S(4f))]);
            }
            Theme.Label(g, names[i], sel ? Theme.Strong : Theme.Body, sel || hot ? Theme.Text : Theme.Muted,
                Rectangle.Round(new RectangleF(r.X + Theme.S(16f), r.Y, r.Width - Theme.S(48f), r.Height)), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
        // Scroll hint when the list continues past the bottom or top.
        if (names.Count > visibleRows)
        {
            float track = Height - pad * 2f, thumb = Math.Max(Theme.S(24f), track * visibleRows / names.Count);
            float y = pad + (track - thumb) * top / Math.Max(1, names.Count - visibleRows);
            Theme.Fill(g, new RectangleF(Width - Theme.S(5f), y, Theme.S(3f), thumb), Theme.S(1.5f), Theme.Line);
        }
        Theme.Stroke(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Theme.S(8f), Theme.Line);
    }
}
