using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ZenithClipper.Windows.Ui;
using Control = System.Windows.Forms.Control;

/// <summary>Custom-painted control with a short hover/press fade. Timers only run while animating.</summary>
abstract class ZControl : Control
{
    readonly System.Windows.Forms.Timer fade = new() { Interval = 15 };
    protected float HoverT { get; private set; }
    protected bool Hovered { get; private set; }
    protected bool Pressed { get; private set; }
    protected ZControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Font = Theme.Body; ForeColor = Theme.Text;
        fade.Tick += (_, _) =>
        {
            float target = Hovered ? 1 : 0; HoverT += Math.Sign(target - HoverT) * 0.2f;
            if (Math.Abs(target - HoverT) < 0.2f) { HoverT = target; fade.Stop(); }
            Invalidate();
        };
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (!Enabled) return; Hovered = true; fade.Start(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hovered = false; Pressed = false; fade.Start(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Solid(this)); Theme.Prep(g); Draw(g);
    }
    protected abstract void Draw(Graphics g);
    protected void FocusRing(Graphics g, RectangleF r, float radius)
    {
        if (Focused && ShowFocusCues) Theme.Stroke(g, RectangleF.Inflate(r, Theme.S(2f), Theme.S(2f)), radius + Theme.S(2f), Theme.Alpha(Theme.AccentEnd, 200), Theme.S(1.5f));
    }
    protected override void Dispose(bool disposing) { if (disposing) fade.Dispose(); base.Dispose(disposing); }
}

enum ButtonKind { Primary, Secondary, Ghost, Danger, Hero }

sealed class ZButton : ZControl
{
    ButtonKind kind;
    string glyph = "", hint = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonKind Kind { get => kind; set { kind = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get => glyph; set { glyph = value; Invalidate(); } }
    /// <summary>Secondary text drawn on the right, e.g. a keyboard shortcut.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Hint { get => hint; set { hint = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IconOnly { get; set; }
    /// <summary>When set, rows size the button to its content.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoWidth { get; set; } = true;
    public ZButton(string text, ButtonKind kind = ButtonKind.Secondary, string glyph = "")
    {
        Text = text; this.kind = kind; this.glyph = glyph; Cursor = Cursors.Hand; TabStop = true;
        Height = Theme.S(kind == ButtonKind.Hero ? 40 : 32);
        if (kind == ButtonKind.Hero) Font = Theme.Hero; else if (kind == ButtonKind.Primary) Font = Theme.Strong;
    }
    public int PreferredWidth()
    {
        if (IconOnly) return Height;
        int w = Theme.Measure(Text, Font).Width + Theme.S(32);
        if (glyph.Length > 0) w += Theme.S(24);
        if (hint.Length > 0) w += Theme.Measure(hint, Theme.Small).Width + Theme.S(16);
        return w;
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); }
    }
    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        float radius = Theme.S(6f);
        if (Pressed) r.Inflate(-Theme.S(1f), -Theme.S(1f));
        Color fore = Enabled ? Theme.Text : Theme.Faint;
        switch (kind)
        {
            case ButtonKind.Primary or ButtonKind.Hero when Enabled:
                using (var brush = Theme.AccentBrush(r)) Theme.Fill(g, r, radius, brush);
                if (HoverT > 0) Theme.Fill(g, r, radius, Theme.Alpha(Color.White, (int)(28 * HoverT)));
                fore = Theme.Ink; break;
            case ButtonKind.Danger:
                Theme.Fill(g, r, radius, Theme.Mix(Theme.Alpha(Theme.Live, 34), Theme.Alpha(Theme.Live, 70), HoverT));
                fore = Enabled ? Theme.Mix(Theme.Live, Color.White, 0.25f) : Theme.Faint; break;
            case ButtonKind.Ghost:
                if (HoverT > 0) Theme.Fill(g, r, radius, Theme.Alpha(Theme.Hover, (int)(255 * HoverT)));
                fore = Enabled ? Theme.Mix(Theme.Muted, Theme.Text, HoverT) : Theme.Faint; break;
            default:
                Theme.Fill(g, r, radius, Enabled ? Theme.Mix(Theme.Raised, Theme.Hover, HoverT) : Theme.Raised);
                Theme.Stroke(g, r, radius, Theme.Line); break;
        }
        FocusRing(g, r, radius);
        var content = Rectangle.Round(r);
        if (IconOnly) { Theme.Glyph(g, glyph, Font.Size + 1, fore, content); return; }
        int pad = Theme.S(kind == ButtonKind.Hero ? 16 : 12);
        if (hint.Length > 0)
        {
            var size = Theme.Measure(hint, Theme.Small);
            var pill = new Rectangle(content.Right - pad - size.Width - Theme.S(12), content.Y + (content.Height - size.Height - Theme.S(8)) / 2, size.Width + Theme.S(12), size.Height + Theme.S(8));
            Theme.Stroke(g, pill, Theme.S(4f), Theme.Alpha(fore, 80));
            Theme.Label(g, hint, Theme.Small, Theme.Alpha(fore, 220), pill, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            content.Width -= size.Width + Theme.S(20);
        }
        var measured = Theme.Measure(Text, Font);
        int glyphW = glyph.Length > 0 ? Theme.S(24) : 0;
        bool centered = hint.Length == 0;
        int x = centered ? content.X + (content.Width - measured.Width - glyphW) / 2 : content.X + pad;
        if (glyph.Length > 0) Theme.Glyph(g, glyph, 10.5f, fore, new Rectangle(x, content.Y, glyphW - Theme.S(6), content.Height));
        Theme.Label(g, Text, Font, fore, new Rectangle(x + glyphW, content.Y, content.Right - x - glyphW, content.Height));
    }
}

sealed class ZToggle : ZControl
{
    bool on;
    float knob;
    readonly System.Windows.Forms.Timer slide = new() { Interval = 15 };
    public event EventHandler? CheckedChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked { get => on; set { if (on == value) return; on = value; slide.Start(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
    public void SetSilently(bool value) { on = value; knob = value ? 1 : 0; Invalidate(); }
    public ZToggle()
    {
        Size = new(Theme.S(36), Theme.S(20)); Cursor = Cursors.Hand; TabStop = true;
        slide.Tick += (_, _) => { float t = on ? 1 : 0; knob += Math.Sign(t - knob) * 0.2f; if (Math.Abs(t - knob) < 0.2f) { knob = t; slide.Stop(); } Invalidate(); };
    }
    protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) Checked = !Checked; }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) Checked = !Checked; }
    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(1, 1, Width - 2, Height - 2);
        if (knob > 0) using (var b = Theme.AccentBrush(r, (int)(255 * knob))) Theme.Fill(g, r, r.Height / 2, b);
        if (knob < 1) Theme.Fill(g, r, r.Height / 2, Theme.Alpha(Theme.Hover, (int)(255 * (1 - knob))));
        if (knob < 1) Theme.Stroke(g, r, r.Height / 2, Theme.Alpha(Theme.Line, (int)(255 * (1 - knob))));
        float d = r.Height - Theme.S(6f), x = r.X + Theme.S(3f) + (r.Width - d - Theme.S(6f)) * knob;
        using var brush = new SolidBrush(Enabled ? Color.White : Theme.Muted);
        g.FillEllipse(brush, x, r.Y + Theme.S(3f), d, d);
        FocusRing(g, r, r.Height / 2);
    }
    protected override void Dispose(bool disposing) { if (disposing) slide.Dispose(); base.Dispose(disposing); }
}

/// <summary>Selectable pill chips that wrap onto new rows; a segmented control when <see cref="Segmented"/> is set.</summary>
sealed class ZChips : ZControl, IMeasured
{
    readonly List<(string Label, object Value)> items = [];
    readonly List<Rectangle> boxes = [];
    int selected = -1, hover = -1;
    public event EventHandler? SelectionChanged;
    public Func<object, bool>? IsDim { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Segmented { get; set; }
    public ZChips() { Cursor = Cursors.Hand; TabStop = true; Height = Theme.S(30); }
    public ZChips Add(string label, object value) { items.Add((label, value)); Relayout(); return this; }
    public void Clear() { items.Clear(); selected = -1; Relayout(); }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get => selected; set { if (selected == value) return; selected = value; Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty); } }
    public object? SelectedValue => selected >= 0 && selected < items.Count ? items[selected].Value : null;
    public bool Select(object value)
    {
        int index = items.FindIndex(x => Equals(x.Value, value)); selected = index; Invalidate(); return index >= 0;
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }
    List<Rectangle> Compute(int width)
    {
        var list = new List<Rectangle>(); int x = 0, y = 0, h = Theme.S(30), gap = Segmented ? 0 : Theme.S(6);
        if (Segmented && items.Count > 0)
        {
            // Each segment gets its label width plus padding, and the spare room is shared out evenly, so a long label
            // like "Custom" is never squeezed into the same slot as "1h".
            int pad = Theme.S(20);
            var need = items.Select(it => Theme.Measure(it.Label, Font).Width + pad).ToArray();
            int total = need.Sum();
            double scale = total > width ? width / (double)total : 1;
            int spare = Math.Max(0, width - total);
            int x0 = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int w = i == items.Count - 1 ? width - x0 : (int)Math.Round(need[i] * scale + spare / (double)items.Count);
                list.Add(new Rectangle(x0, 0, w, h)); x0 += w;
            }
            return list;
        }
        foreach (var item in items)
        {
            int w = Theme.Measure(item.Label, Font).Width + Theme.S(26);
            if (x > 0 && x + w > width) { x = 0; y += h + gap; }
            list.Add(new Rectangle(x, y, w, h)); x += w + gap;
        }
        return list;
    }
    public int MeasureHeight(int width) { var list = Compute(width); return list.Count == 0 ? Theme.S(30) : list.Max(b => b.Bottom); }
    /// <summary>True when every label fits inside its segment or chip (used by the panel's snapshot check).</summary>
    public bool LabelsFit() { var list = Compute(Width); for (int i = 0; i < list.Count; i++) if (Theme.Measure(items[i].Label, Font).Width + Theme.S(8) > list[i].Width || list[i].Right > Width) return false; return true; }
    void Relayout() { boxes.Clear(); boxes.AddRange(Compute(Width)); Invalidate(); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = boxes.FindIndex(b => b.Contains(e.Location)); if (h != hover) { hover = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); int i = boxes.FindIndex(b => b.Contains(e.Location)); if (i >= 0 && Enabled) { Focus(); SelectedIndex = i; } }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (items.Count == 0) return;
        if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(items.Count - 1, selected + 1);
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, selected - 1);
    }
    protected override void Draw(Graphics g)
    {
        if (Segmented && boxes.Count > 0)
        {
            var all = new RectangleF(0.5f, 0.5f, Width - 1, boxes[0].Height - 1);
            Theme.Fill(g, all, Theme.S(6f), Theme.Bg); Theme.Stroke(g, all, Theme.S(6f), Theme.Line);
            FocusRing(g, all, Theme.S(6f));
        }
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = (RectangleF)boxes[i]; bool sel = i == selected, dim = IsDim?.Invoke(items[i].Value) == true;
            if (Segmented) b.Inflate(-Theme.S(2f), -Theme.S(2f)); else b.Inflate(-0.5f, -0.5f);
            float radius = Theme.S(Segmented ? 4f : 6f);
            if (sel) Theme.Fill(g, b, radius, Theme.Selected);
            else if (!Segmented) { Theme.Fill(g, b, radius, i == hover ? Theme.Hover : Theme.Raised); Theme.Stroke(g, b, radius, Theme.Line); }
            else if (i == hover) Theme.Fill(g, b, radius, Theme.Hover);
            Color c = sel ? Theme.Text : dim ? Theme.Faint : i == hover ? Theme.Text : Theme.Muted;
            Theme.Label(g, items[i].Label, Font, c, Rectangle.Round(b), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        if (!Segmented && selected >= 0 && selected < boxes.Count) FocusRing(g, boxes[selected], boxes[selected].Height / 2f);
    }
}

sealed class ZSlider : ZControl
{
    double min, max = 1, value;
    bool dragging;
    public event EventHandler? ValueChanged;
    public Func<double, string>? Format { get; set; }
    public ZSlider(double min, double max, double value) { this.min = min; this.max = max; this.value = value; Height = Theme.S(28); Cursor = Cursors.Hand; TabStop = true; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value { get => value; set { double v = Math.Clamp(value, min, max); if (v == this.value) return; this.value = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    public void SetRange(double lo, double hi) { min = lo; max = Math.Max(lo + 1e-6, hi); value = Math.Clamp(value, min, max); Invalidate(); }
    int LabelWidth => Format == null ? 0 : Theme.S(64);
    RectangleF Track => new(Theme.S(8f), Height / 2f - Theme.S(2f), Width - Theme.S(16f) - LabelWidth, Theme.S(4f));
    void SetFrom(int x) { var t = Track; Value = min + (max - min) * Math.Clamp((x - t.X) / t.Width, 0, 1); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); dragging = true; SetFrom(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFrom(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); double step = (max - min) / 50;
        if (e.KeyCode == Keys.Right) Value += step; if (e.KeyCode == Keys.Left) Value -= step;
    }
    protected override void Draw(Graphics g)
    {
        var t = Track; float frac = (float)((value - min) / (max - min));
        Theme.Fill(g, t, t.Height / 2, Theme.Hover);
        var filled = new RectangleF(t.X, t.Y, Math.Max(t.Height, t.Width * frac), t.Height);
        using (var b = Theme.AccentBrush(filled)) Theme.Fill(g, filled, t.Height / 2, b);
        float d = Theme.S(16f) + (HoverT + (dragging ? 1 : 0)) * Theme.S(1.5f), cx = t.X + t.Width * frac;
        using (var shadow = new SolidBrush(Theme.Alpha(Color.Black, 90))) g.FillEllipse(shadow, cx - d / 2, Height / 2f - d / 2 + Theme.S(1f), d, d);
        using (var knob = new SolidBrush(Color.White)) g.FillEllipse(knob, cx - d / 2, Height / 2f - d / 2, d, d);
        if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.AccentEnd, Theme.S(1.5f))) g.DrawEllipse(pen, cx - d / 2 - 2, Height / 2f - d / 2 - 2, d + 4, d + 4);
        if (Format != null) Theme.Label(g, Format(value), Theme.Mono, Theme.Text, new Rectangle(Width - LabelWidth, 0, LabelWidth, Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Rounded dark text field hosting a borderless TextBox.</summary>
sealed class ZField : Panel
{
    public TextBox Box { get; } = new() { BorderStyle = BorderStyle.None, BackColor = Theme.Raised, ForeColor = Theme.Text, Font = Theme.Body };
    string glyph;
    public ZField(string placeholder = "", string glyph = "")
    {
        this.glyph = glyph;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = Theme.S(32); Box.PlaceholderText = placeholder; Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate(); Box.LostFocus += (_, _) => Invalidate();
        Cursor = Cursors.IBeam; Click += (_, _) => Box.Focus();
    }
    [AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }
    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int left = Theme.S(glyph.Length > 0 ? 34 : 12);
        // A borderless TextBox draws its text at the top of its bounds, so centre the font's line height, not the
        // control's preferred height (which adds space below and pushed the text a few pixels low).
        int line = Box.Font.Height;
        Box.SetBounds(left, (Height - line + 1) / 2, Width - left - Theme.S(12), line);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Solid(Parent)); Theme.Prep(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.Fill(g, r, Theme.S(6f), Theme.Raised);
        Theme.Stroke(g, r, Theme.S(6f), Box.Focused ? Theme.Accent : Theme.Line, Box.Focused ? Theme.S(1.5f) : 1);
        if (glyph.Length > 0) Theme.Glyph(g, glyph, 10, Theme.Muted, new Rectangle(Theme.S(8), Box.Top, Theme.S(22), Box.Height));
    }
}

/// <summary>Click, then press a key combination. Produces the same text format HotkeySpec parses.</summary>
sealed class HotkeyBox : ZControl
{
    string value = "";
    bool listening;
    public event EventHandler? ValueChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value { get => value; set { this.value = value; Invalidate(); } }
    public HotkeyBox() { Height = Theme.S(32); Cursor = Cursors.Hand; TabStop = true; }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Flash = null; SetListening(true); }
    protected override void OnLostFocus(EventArgs e) { SetListening(false); base.OnLostFocus(e); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); if (listening) SetListening(true); }
    protected override void OnHandleDestroyed(EventArgs e) { if (IsHandleCreated) RemovePropW(Handle, WindowsHotkeyCapture.ListeningProperty); base.OnHandleDestroyed(e); }
    void SetListening(bool active)
    {
        listening = active;
        if (IsHandleCreated)
        {
            if (active)
            {
                if (!SetPropW(Handle, WindowsHotkeyCapture.ListeningProperty, 1)) { listening = false; Flash = "Cannot listen for shortcuts right now."; }
            }
            else RemovePropW(Handle, WindowsHotkeyCapture.ListeningProperty);
        }
        Invalidate();
    }
    void CommitBinding(string binding)
    {
        value = binding; Flash = null; SetListening(false);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WindowsHotkeyCapture.ForwardMessage)
        {
            if (listening && WindowsHotkeyCapture.TryDecodeForwardedMessage(message.Msg, message.LParam, out var binding)) CommitBinding(binding);
            message.Result = 0;
            return;
        }
        base.WndProc(ref message);
    }
    protected override bool IsInputKey(Keys keyData) => true;
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!listening) return base.ProcessCmdKey(ref msg, keyData);
        Keys key = keyData & Keys.KeyCode;
        if (key == Keys.Escape) { Flash = null; SetListening(false); return true; }
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return true;
        var parts = new List<string>();
        if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keyData & Keys.Shift) != 0) parts.Add("Shift");
        if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
        string name = key == Keys.Oemtilde ? "~" : key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (key - Keys.D0))).ToString() : key is >= Keys.A and <= Keys.Z ? key.ToString() : key is >= Keys.F1 and <= Keys.F24 ? key.ToString() : "";
        if (name.Length == 0 || parts.Count == 0) { Flash = "Use Ctrl/Shift/Alt + a letter, digit, ~ or F-key"; Invalidate(); return true; }
        parts.Add(name); CommitBinding(string.Join('+', parts)); return true;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetPropW(nint window, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint RemovePropW(nint window, string name);
    string? Flash { get; set; }
    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.Fill(g, r, Theme.S(6f), listening ? Theme.Hover : Theme.Mix(Theme.Raised, Theme.Hover, HoverT));
        Theme.Stroke(g, r, Theme.S(6f), listening ? Theme.Accent : Theme.Line, listening ? Theme.S(1.5f) : 1);
        var inner = Rectangle.Round(RectangleF.Inflate(r, -Theme.S(8f), 0));
        if (listening) { Theme.Label(g, Flash ?? "Press a shortcut…  Esc to cancel", Theme.Small, Flash != null ? Theme.Warn : Theme.Muted, inner); return; }
        int x = inner.X;
        foreach (string part in value.Split('+'))
        {
            var size = Theme.Measure(part, Theme.Small); var key = new Rectangle(x, inner.Y + (inner.Height - Theme.S(24)) / 2, size.Width + Theme.S(16), Theme.S(24));
            Theme.Fill(g, key, Theme.S(4f), Theme.Bg); Theme.Stroke(g, key, Theme.S(4f), Theme.Line);
            Theme.Label(g, part, Theme.Small, Theme.Text, key, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x = key.Right + Theme.S(6);
        }
        Theme.Glyph(g, Glyphs.Keyboard, 10, Theme.Faint, new Rectangle(inner.Right - Theme.S(20), inner.Y, Theme.S(20), inner.Height));
    }
}

/// <summary>Rounded surface with an optional glyph + title header. Children lay out vertically.
/// When it is the whole content of a borderless window, its header drags the window and can show a close button.</summary>
class ZCard : VStack
{
    readonly string title, glyph;
    bool closeHot;
    public string? Note { get; set; }
    string? subtitle;
    /// <summary>One line under the title saying what the card is for.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Subtitle { get => subtitle; set { subtitle = value; Padding = new Padding(Padding.Left, title.Length > 0 ? Theme.S(value is null ? 48 : 66) : Padding.Top, Padding.Right, Padding.Bottom); Invalidate(); Parent?.PerformLayout(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Closable { get; set; }
    public event EventHandler? CloseRequested;
    public ZCard(string title = "", string glyph = "", Color? fill = null)
    {
        this.title = title; this.glyph = glyph; BackColor = fill ?? Theme.Surface;
        int top = title.Length > 0 ? Theme.S(48) : Theme.S(16);
        Padding = new Padding(Theme.S(16), top, Theme.S(16), Theme.S(16));
    }
    bool Hosted => Parent is Form;
    Rectangle CloseBox => new(Width - Theme.S(44), Theme.S(12), Theme.S(30), Theme.S(28));
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hot = Closable && CloseBox.Contains(e.Location);
        if (hot != closeHot) { closeHot = hot; Invalidate(CloseBox); }
        Cursor = Hosted && e.Y < Theme.S(48) && !hot ? Cursors.SizeAll : Cursors.Default;
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (closeHot) { closeHot = false; Invalidate(CloseBox); } }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (Closable && CloseBox.Contains(e.Location)) { CloseRequested?.Invoke(this, EventArgs.Empty); return; }
        if (Hosted && e.Y < Theme.S(48) && FindForm() is { } f) Native.DragWindow(f);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Prep(g);
        if (Hosted) g.Clear(BackColor);
        else
        {
            g.Clear(Theme.Solid(Parent));
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.Fill(g, r, Theme.S(8f), BackColor); Theme.Stroke(g, r, Theme.S(8f), Theme.Divider);
        }
        if (title.Length == 0) return;
        int x = Theme.S(16);
        if (glyph.Length > 0)
        {
            if (subtitle is null) { var chip = new Rectangle(x, Theme.S(14), Theme.S(18), Theme.S(24)); Theme.Glyph(g, glyph, 9.5f, Theme.Muted, chip); x = chip.Right + Theme.S(8); }
            else
            {
                // Bigger header: the glyph in a tinted tile, title and subtitle beside it.
                var tile = new RectangleF(x, Theme.S(14), Theme.S(36), Theme.S(36));
                Theme.Fill(g, tile, Theme.S(8f), Theme.Alpha(Theme.Accent, 34));
                Theme.Glyph(g, glyph, 11f, Theme.Accent, Rectangle.Round(tile)); x = (int)tile.Right + Theme.S(12);
            }
        }
        if (subtitle is not null)
        {
            Theme.Label(g, title, Theme.Title, Theme.Text, new Rectangle(x, Theme.S(13), Width - x - Theme.S(16), Theme.S(20)));
            Theme.Label(g, subtitle, Theme.Small, Theme.Muted, new Rectangle(x, Theme.S(33), Width - x - Theme.S(16), Theme.S(18)));
            return;
        }
        Theme.Label(g, title, Theme.Strong, Theme.Text, new Rectangle(x, Theme.S(14), Width - x - Theme.S(56), Theme.S(24)));
        if (Note != null) Theme.Label(g, Note, Theme.Small, Theme.Muted, new Rectangle(x, Theme.S(14), Width - x - Theme.S(56), Theme.S(24)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        if (Closable)
        {
            if (closeHot) Theme.Fill(g, CloseBox, Theme.S(8f), Theme.Hover);
            Theme.Glyph(g, Glyphs.Close, 8.5f, closeHot ? Theme.Text : Theme.Muted, CloseBox);
        }
    }
}

/// <summary>Stacks children top to bottom at full width. Children opt into measured heights via <see cref="IMeasured"/>.</summary>
class VStack : Panel
{
    public int Gap { get; set; } = Theme.S(10);
    public VStack()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }
    public T Add<T>(T control) where T : Control { Controls.Add(control); return control; }
    /// <summary>Use normal Dock layout instead of stacking (for hosts with a fill area and a docked bar).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool UseDock { get; set; }
    /// <summary>The last visible child stretches to fill any height left over, so short content never leaves a dead gap.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool FillLast { get; set; }
    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (UseDock) { base.OnLayout(levent); return; }
        int y = Padding.Top - (AutoScroll ? -AutoScrollPosition.Y : 0), w = ClientSize.Width - Padding.Horizontal;
        var visible = Controls.Cast<Control>().Where(c => c.Visible).ToList();
        for (int i = 0; i < visible.Count; i++)
        {
            var c = visible[i];
            int cw = w - c.Margin.Horizontal;
            int h = c is IMeasured m ? m.MeasureHeight(cw) : c.Height;
            y += c.Margin.Top;
            if (FillLast && i == visible.Count - 1) h = Math.Max(h, ClientSize.Height - Padding.Bottom - c.Margin.Bottom - y);
            c.SetBounds(Padding.Left + c.Margin.Left, y, cw, h);
            y += h + c.Margin.Bottom + Gap;
        }
        if (AutoScroll) AutoScrollMinSize = new Size(0, y - Gap + Padding.Bottom - AutoScrollPosition.Y);
        // A nested stack whose content grew or shrank asks its parent stack for a new height, so nothing below gets
        // squashed into the old space (this was the "Clip audio" overlap).
        if (this is IMeasured self && Parent is VStack { UseDock: false } owner)
        {
            int want = self.MeasureHeight(Width);
            bool stretched = owner.FillLast && owner.Controls.Cast<Control>().LastOrDefault(c => c.Visible) == this;
            if (stretched ? want > Height : want != Height) owner.PerformLayout();
        }
    }
    /// <summary>Total height needed at the given width, for nesting a stack inside another stack.</summary>
    public int ContentHeight(int width)
    {
        int y = Padding.Vertical, w = width - Padding.Horizontal, count = 0;
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            int cw = w - c.Margin.Horizontal;
            if (c is IMeasured m) { int mh = m.MeasureHeight(cw); y += mh; } else y += c.Height;
            y += c.Margin.Vertical; count++;
        }
        return y + Math.Max(0, count - 1) * Gap;
    }
}

interface IMeasured { int MeasureHeight(int width); }

sealed class AutoCard(string title = "", string glyph = "") : ZCard(title, glyph), IMeasured
{
    public int MeasureHeight(int width) => ContentHeight(width);
}

/// <summary>Lays children left to right; children with Dock=Fill share the remaining width.</summary>
sealed class HRow : Panel, IMeasured
{
    public int Gap { get; set; } = Theme.S(8);
    public HRow(int height = 36)
    {
        Height = Theme.S(height);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }
    public T Add<T>(T control, bool fill = false) where T : Control { control.Tag = fill ? "fill" : control.Tag; Controls.Add(control); return control; }
    public int MeasureHeight(int width) => Height;
    protected override void OnLayout(LayoutEventArgs levent)
    {
        var visible = Controls.Cast<Control>().Where(c => c.Visible).ToList();
        int fixedW = visible.Where(c => !Fill(c)).Sum(W);
        int fills = visible.Count(Fill), gaps = Math.Max(0, visible.Count - 1) * Gap;
        int fillW = fills == 0 ? 0 : Math.Max(0, (ClientSize.Width - fixedW - gaps) / fills), x = 0;
        int firstRight = visible.FindIndex(c => Equals(c.Tag, "right"));
        for (int i = 0; i < visible.Count; i++)
        {
            var c = visible[i];
            if (i == firstRight) x = Math.Max(x, ClientSize.Width - visible.Skip(i).Sum(W) - (visible.Count - i - 1) * Gap);
            int w = Fill(c) ? fillW : W(c);
            int h = c is Label or ZLabel ? ClientSize.Height : Math.Min(c.Height, ClientSize.Height);
            c.SetBounds(x, (ClientSize.Height - h) / 2, w, h); x += w + Gap;
        }
    }
    static int W(Control c) => c is ZButton { AutoWidth: true } b ? b.PreferredWidth() : c.Width;
    static bool Fill(Control c) => Equals(c.Tag, "fill");
}

/// <summary>Plain text label in the Zenith type scale; wraps when <see cref="Wraps"/> is set.</summary>
sealed class ZLabel : Control, IMeasured
{
    public bool Wraps { get; set; }
    public TextFormatFlags Align { get; set; } = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
    public ZLabel(string text = "", Font? font = null, Color? color = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Text = text; Font = font ?? Theme.Body; ForeColor = color ?? Theme.Text; Height = Theme.Measure("Ag", Font).Height + Theme.S(4);
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); Parent?.PerformLayout(); }
    public int MeasureHeight(int width) => Wraps ? Math.Max(Theme.Measure("Ag", Font).Height, Theme.WrapHeight(Text, Font, width)) + Theme.S(2) : Height;
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Solid(this));
        if (Wraps) Theme.Wrap(e.Graphics, Text, Font, ForeColor, ClientRectangle);
        else Theme.Label(e.Graphics, Text, Font, ForeColor, ClientRectangle, Align);
    }
}

/// <summary>Labelled settings row: title + optional description on the left, a control on the right.</summary>
sealed class SettingRow : Panel, IMeasured
{
    readonly string title; string? detail;
    readonly Control control;
    readonly int controlWidth;
    public SettingRow(string title, string? detail, Control control, int controlWidth = 0)
    {
        this.title = title; this.detail = detail; this.control = control; this.controlWidth = controlWidth == 0 ? control.Width : Theme.S(controlWidth);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Controls.Add(control);
    }
    public string? Detail { get => detail; set { detail = value; Parent?.PerformLayout(); Invalidate(); } }
    int TextWidth(int width) => width - controlWidth - Theme.S(16);
    public int MeasureHeight(int width)
    {
        int text = Theme.Measure("Ag", Theme.Body).Height + (string.IsNullOrEmpty(detail) ? 0 : Theme.S(2) + Theme.WrapHeight(detail, Theme.Small, TextWidth(width)));
        return Math.Max(Math.Max(text, ControlHeight), Theme.S(36)) + Theme.S(4);
    }
    int ControlHeight => control is IMeasured m ? m.MeasureHeight(controlWidth) : control.Height;
    protected override void OnLayout(LayoutEventArgs levent)
    {
        int h = ControlHeight;
        control.SetBounds(Width - controlWidth, (Height - h) / 2, controlWidth, h);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Solid(Parent));
        int line = Theme.Measure("Ag", Theme.Body).Height, w = TextWidth(Width);
        int detailH = string.IsNullOrEmpty(detail) ? 0 : Theme.WrapHeight(detail, Theme.Small, w) + Theme.S(2);
        int y = (Height - line - detailH) / 2;
        Theme.Label(g, title, Theme.Body, Theme.Text, new Rectangle(0, y, w, line));
        if (detailH > 0) Theme.Wrap(g, detail!, Theme.Small, Theme.Muted, new Rectangle(0, y + line + Theme.S(2), w, detailH));
        // Rows after the first in a group get a hairline above, so groups read as one list.
        if (Parent != null && Parent.Controls.OfType<SettingRow>().FirstOrDefault() != this) Theme.HLine(g, 0, Width, 0);
    }
}

/// <summary>Inline status banner (info, success, warning, error) with an optional action link.</summary>
sealed class Banner : ZControl, IMeasured
{
    public enum Tone { Info, Success, Warning, Error }
    Tone tone; string? action;
    Rectangle actionBox;
    public event EventHandler? ActionClicked;
    public Banner() { Visible = false; Height = Theme.S(40); }
    public void Show(string text, Tone tone, string? action = null)
    {
        Text = text; this.tone = tone; this.action = action; Visible = true; Invalidate(); Parent?.PerformLayout();
    }
    public void Dismiss() { if (!Visible) return; Visible = false; Parent?.PerformLayout(); }
    Color ToneColor => tone switch { Tone.Success => Theme.Ok, Tone.Warning => Theme.Warn, Tone.Error => Theme.Live, _ => Theme.AccentEnd };
    string ToneGlyph => tone switch { Tone.Success => Glyphs.Check, Tone.Warning or Tone.Error => Glyphs.Warning, _ => Glyphs.Info };
    int ActionWidth => action == null ? 0 : Theme.Measure(action, Theme.Strong).Width + Theme.S(20);
    public int MeasureHeight(int width) => Math.Max(Theme.S(40), Theme.WrapHeight(Text, Theme.Small, width - Theme.S(52) - ActionWidth) + Theme.S(20));
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Cursor = actionBox.Contains(e.Location) ? Cursors.Hand : Cursors.Default; }
    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); if (action != null && actionBox.Contains(e.Location)) ActionClicked?.Invoke(this, EventArgs.Empty); }
    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1); var c = ToneColor;
        Theme.Fill(g, r, Theme.S(6f), Theme.Alpha(c, 22)); Theme.Stroke(g, r, Theme.S(6f), Theme.Alpha(c, 80));
        Theme.Glyph(g, ToneGlyph, 10, c, new Rectangle(Theme.S(10), 0, Theme.S(24), Height));
        int textW = Width - Theme.S(48) - ActionWidth;
        Theme.Wrap(g, Text, Theme.Small, Theme.Text, new Rectangle(Theme.S(40), (Height - Theme.WrapHeight(Text, Theme.Small, textW)) / 2, textW, Height));
        if (action != null)
        {
            actionBox = new Rectangle(Width - ActionWidth - Theme.S(6), Theme.S(6), ActionWidth, Height - Theme.S(12));
            Theme.Fill(g, actionBox, Theme.S(4f), Theme.Alpha(c, 45));
            Theme.Label(g, action, Theme.Strong, Theme.Text, actionBox, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>Indeterminate shimmer progress for exports whose duration is unknown.</summary>
sealed class Shimmer : Control
{
    readonly System.Windows.Forms.Timer tick = new() { Interval = 16 };
    float phase;
    public Shimmer()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = Theme.S(6); tick.Tick += (_, _) => { phase = (phase + 0.012f) % 1.4f; Invalidate(); };
    }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); tick.Enabled = Visible; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Solid(Parent)); Theme.Prep(g);
        var r = new RectangleF(0, 0, Width, Height); Theme.Fill(g, r, Height / 2f, Theme.Hover);
        float w = Width * 0.35f, x = (phase - 0.35f) * Width;
        var bar = RectangleF.Intersect(r, new RectangleF(x, 0, w, Height));
        if (bar.Width > 1) using (var b = Theme.AccentBrush(bar)) Theme.Fill(g, bar, Height / 2f, b);
    }
    protected override void Dispose(bool disposing) { if (disposing) tick.Dispose(); base.Dispose(disposing); }
}

/// <summary>Small dark input dialog (used for rename and custom durations).</summary>
sealed class PromptForm : Form
{
    readonly ZField input;
    public string Value => input.Text;
    PromptForm(string title, string label, string value)
    {
        Text = title; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Bg; ClientSize = new Size(Theme.S(380), Theme.S(150)); Font = Theme.Body;
        var stack = new VStack { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(18)) };
        stack.Add(new ZLabel(label, Theme.Body, Theme.Muted));
        input = stack.Add(new ZField()); input.Text = value;
        var row = stack.Add(new HRow());
        var cancel = row.Add(new ZButton("Cancel", ButtonKind.Ghost) { Tag = "right" });
        var ok = row.Add(new ZButton("OK", ButtonKind.Primary));
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        input.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DialogResult = DialogResult.OK; Close(); } if (e.KeyCode == Keys.Escape) Close(); };
        Controls.Add(stack);
        Shown += (_, _) => { input.Box.Focus(); input.Box.SelectAll(); };
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.StyleWindow(this); }
    /// <summary>Shows above everything (the panel and its widgets are topmost), owned by whichever window asked.</summary>
    public static string? Ask(IWin32Window owner, string title, string label, string value)
    {
        using var form = new PromptForm(title, label, value) { TopMost = true };
        form.Shown += (_, _) => { form.Activate(); form.BringToFront(); };
        return form.ShowDialog(owner) == DialogResult.OK ? form.Value : null;
    }
}

/// <summary>Accent colour picker: one round swatch per preset plus a "custom" swatch that opens the Windows colour dialog.</summary>
sealed class AccentSwatches : ZControl, IMeasured
{
    readonly List<Rectangle> boxes = [];
    int hover = -1;
    string selected = Appearance.DefaultAccent;
    public event Action<string>? Picked;
    public AccentSwatches() { Cursor = Cursors.Hand; TabStop = true; Height = Theme.S(34); }
    public void SetSelected(string hex) { selected = hex.ToUpperInvariant(); Invalidate(); }
    int Count => Appearance.AccentPresets.Length + 1;
    int D => Theme.S(26);
    int Gap => Theme.S(8);
    List<Rectangle> Compute(int width)
    {
        var list = new List<Rectangle>(); int x = 0, y = Theme.S(4);
        for (int i = 0; i < Count; i++)
        {
            if (x > 0 && x + D > width) { x = 0; y += D + Gap; }
            list.Add(new Rectangle(x, y, D, D)); x += D + Gap;
        }
        return list;
    }
    public int MeasureHeight(int width) => Compute(width).Max(b => b.Bottom) + Theme.S(4);
    protected override void OnResize(EventArgs e) { base.OnResize(e); boxes.Clear(); boxes.AddRange(Compute(Width)); }
    bool IsCustom => Appearance.AccentPresets.All(p => !p.Hex.Equals(selected, StringComparison.OrdinalIgnoreCase));
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = boxes.FindIndex(b => b.Contains(e.Location)); if (h != hover) { hover = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e); int i = boxes.FindIndex(b => b.Contains(e.Location)); if (i < 0) return;
        if (i < Appearance.AccentPresets.Length) { Pick(Appearance.AccentPresets[i].Hex); return; }
        using var dialog = new ColorDialog { FullOpen = true, Color = Theme.Accent, AnyColor = true };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK) Pick($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
    }
    void Pick(string hex) { SetSelected(hex); Picked?.Invoke(selected); }
    protected override void Draw(Graphics g)
    {
        if (boxes.Count != Count) { boxes.Clear(); boxes.AddRange(Compute(Width)); }
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = (RectangleF)boxes[i];
            bool custom = i == Appearance.AccentPresets.Length;
            bool sel = custom ? IsCustom : Appearance.AccentPresets[i].Hex.Equals(selected, StringComparison.OrdinalIgnoreCase);
            if (sel || hover == i) Theme.Stroke(g, RectangleF.Inflate(b, Theme.S(3f), Theme.S(3f)), b.Width, sel ? Theme.Text : Theme.Line, Theme.S(1.5f));
            var inner = RectangleF.Inflate(b, -Theme.S(1f), -Theme.S(1f));
            if (custom)
            {
                if (IsCustom && Appearance.TryParse(selected, out var c)) using (var fill = new SolidBrush(Color.FromArgb(c.R, c.G, c.B))) g.FillEllipse(fill, inner);
                else
                {
                    using var path = new GraphicsPath(); path.AddEllipse(inner);
                    using var rainbow = new PathGradientBrush(path) { CenterColor = Color.White, SurroundColors = [Color.FromArgb(255, 90, 90), Color.FromArgb(255, 210, 80), Color.FromArgb(90, 220, 130), Color.FromArgb(80, 170, 255), Color.FromArgb(190, 110, 255)] };
                    g.FillEllipse(rainbow, inner);
                }
                Theme.Glyph(g, Glyphs.Add, 8f, IsCustom ? Theme.Ink : Color.FromArgb(40, 40, 40), Rectangle.Round(b));
                continue;
            }
            Appearance.TryParse(Appearance.AccentPresets[i].Hex, out var rgb);
            using (var fill = new SolidBrush(Color.FromArgb(rgb.R, rgb.G, rgb.B))) g.FillEllipse(fill, inner);
        }
        FocusRing(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.S(6f));
    }
}
