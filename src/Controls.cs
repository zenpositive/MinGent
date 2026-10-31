using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MinGent
{
    static class Fonts
    {
        static Font note, small, semi, smallSemi, heading, title;

        public static Font Note { get { return note ?? (note = new Font(UI.Font.FontFamily, UI.Font.Size * 0.93f)); } }
        public static Font Small { get { return small ?? (small = new Font(UI.Font.FontFamily, UI.Font.Size * 0.86f)); } }
        public static Font Semi { get { return semi ?? (semi = Semibold(UI.Font.Size)); } }
        public static Font SmallSemi { get { return smallSemi ?? (smallSemi = Semibold(UI.Font.Size * 0.9f)); } }
        public static Font Heading { get { return heading ?? (heading = Semibold(UI.Font.Size * 1.08f)); } }
        public static Font Title { get { return title ?? (title = Semibold(UI.Font.Size * 1.6f)); } }

        public static Font Semibold(float size)
        {
            try
            {
                Font f = new Font("Segoe UI Semibold", size);
                if (string.Equals(f.Name, "Segoe UI Semibold", StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch { }
            return new Font(UI.Font.FontFamily, size, FontStyle.Bold);
        }

        public const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.Left | TextFormatFlags.Top;
        public const TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.Left;

        public static int Height(string text, Font f, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return TextRenderer.MeasureText(text, f, new Size(Math.Max(1, width), int.MaxValue), Wrap).Height;
        }

        public static int Width(string text, Font f)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return TextRenderer.MeasureText(text, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        }
    }

    class Tween : IDisposable
    {
        readonly Timer timer = new Timer();
        readonly Action changed;
        DateTime start;
        float from, to;
        int ms;
        public float Value;

        public Tween(float value, Action onChange)
        {
            Value = value;
            changed = onChange;
            timer.Interval = 15;
            timer.Tick += delegate { Step(); };
        }

        public void To(float target, int durationMs, bool animate)
        {
            if (!animate || durationMs <= 0 || !Native.Animations)
            {
                timer.Stop();
                Value = target;
                changed();
                return;
            }
            from = Value;
            to = target;
            ms = durationMs;
            start = DateTime.UtcNow;
            timer.Start();
        }

        void Step()
        {
            double t = (DateTime.UtcNow - start).TotalMilliseconds / ms;
            if (t >= 1) { Value = to; timer.Stop(); }
            else Value = (float)(from + (to - from) * (1 - Math.Pow(1 - t, 3)));
            changed();
        }

        public void Dispose() { timer.Stop(); timer.Dispose(); }
    }

    static partial class Glyphs
    {
        public static GraphicsPath Pill(RectangleF r)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(r.Width, r.Height);
            if (d <= 0) return p;
            p.AddArc(r.X, r.Y, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }

        public static GraphicsPath RoundF(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRoundF(Graphics g, Color c, RectangleF r, float rad)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = RoundF(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
            g.SmoothingMode = old;
        }

        public static void Ring(Graphics g, Color c, Rectangle outer, int radius, float width)
        {
            if (outer.Width <= 0 || outer.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float hw = width / 2f;
            using (GraphicsPath p = RoundF(new RectangleF(outer.X + hw, outer.Y + hw, outer.Width - width, outer.Height - width), Math.Max(0f, radius - hw)))
            using (Pen pen = new Pen(c, width))
                g.DrawPath(pen, p);
            g.SmoothingMode = old;
        }

        public static float FocusWidth { get { return Math.Max(2f, 2f * UI.Scale); } }

        public static void StepMark(Graphics g, Rectangle r, int state, Theme th)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = Math.Min(r.Width, r.Height) - 1;
            RectangleF c = new RectangleF(r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d);
            float w = Math.Max(1.2f, 1.3f * UI.Scale);
            if (state == 2)
            {
                Color fill = th.HighContrast ? th.Fore : th.Good;
                using (SolidBrush b = new SolidBrush(fill)) g.FillEllipse(b, c);
                using (Pen p = new Pen(th.HighContrast ? th.Back : th.Dark ? Color.FromArgb(0x0B, 0x0B, 0x0B) : Color.White, Math.Max(1.4f, 1.5f * UI.Scale)))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new PointF[] {
                        new PointF(c.X + d * 0.28f, c.Y + d * 0.52f),
                        new PointF(c.X + d * 0.44f, c.Y + d * 0.67f),
                        new PointF(c.X + d * 0.73f, c.Y + d * 0.36f) });
                }
            }
            else if (state == 1)
            {
                using (Pen p = new Pen(th.Accent, w)) g.DrawEllipse(p, c.X + w / 2, c.Y + w / 2, d - w, d - w);
                float i = d * 0.46f;
                using (SolidBrush b = new SolidBrush(th.Accent)) g.FillEllipse(b, c.X + (d - i) / 2f, c.Y + (d - i) / 2f, i, i);
            }
            else
                using (Pen p = new Pen(th.ChromeDim, w)) g.DrawEllipse(p, c.X + w / 2, c.Y + w / 2, d - w, d - w);
            g.SmoothingMode = old;
        }

        public static void Cap(Graphics g, Theme th, Rectangle r, string text, Font f)
        {
            Color fill = th.HighContrast ? th.Back : th.Ctl;
            Color line = th.HighContrast ? th.Fore : th.CtlBorder;
            Color under = th.HighContrast ? th.Fore : Theme.Blend(th.CtlBorder, Color.Black, th.Dark ? 0.35 : 0.12);
            int rad = UI.S(5);
            FillRound(g, under, new Rectangle(r.X, r.Y + UI.S(1), r.Width, r.Height), rad);
            FillRound(g, fill, new Rectangle(r.X, r.Y, r.Width, r.Height - UI.S(1)), rad);
            DrawRound(g, line, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), rad);
            TextRenderer.DrawText(g, text, f, new Rectangle(r.X, r.Y, r.Width, r.Height - UI.S(1)), th.Fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    static class SwitchPaint
    {
        public static Size Size { get { return new Size(UI.S(40), UI.S(20)); } }

        public static void Draw(Graphics g, Rectangle track, float t, bool hover, bool enabled, Theme th)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            t = Math.Max(0f, Math.Min(1f, t));
            Color shade = th.Dark ? Color.White : Color.Black;
            Color offLine = !enabled ? th.MenuDisabled : hover ? th.ChromeFore : th.ChromeDim;
            Color onFill = th.HighContrast ? SystemColors.Highlight : !enabled ? Theme.Blend(th.Chrome, th.ChromeDim, 0.5) : hover ? Theme.Blend(th.Accent, shade, 0.1) : th.Accent;
            Color onKnob = th.HighContrast ? SystemColors.HighlightText : th.OnAccent;
            RectangleF tr = new RectangleF(track.X + 0.5f, track.Y + 0.5f, track.Width - 1f, track.Height - 1f);
            using (GraphicsPath p = Glyphs.Pill(tr))
            {
                if (t < 1f)
                    using (Pen pen = new Pen(Color.FromArgb((int)(255 * (1 - t)), offLine), Math.Max(1f, UI.Scale)))
                        g.DrawPath(pen, p);
                if (t > 0f)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(255 * t), onFill)))
                        g.FillPath(b, p);
            }
            float h = track.Height;
            float offD = h - UI.S(8), onD = h - UI.S(6);
            float d = offD + (onD - offD) * t + (hover && enabled ? UI.S(1) : 0);
            float cxL = track.X + h / 2f, cxR = track.Right - h / 2f;
            float cx = cxL + (cxR - cxL) * t, cy = track.Y + h / 2f;
            using (SolidBrush b = new SolidBrush(Theme.Blend(offLine, onKnob, t)))
                g.FillEllipse(b, cx - d / 2f, cy - d / 2f, d, d);
            g.SmoothingMode = old;
        }
    }

    class PillButton : Button, IThemed
    {
        Theme th;
        readonly bool primary;
        public bool Danger;
        bool hover, down;

        public void SetTheme(Theme t) { th = t; Invalidate(true); }

        public PillButton(string text, Theme t, bool primary)
        {
            th = t;
            this.primary = primary;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            UseMnemonic = false;
            Font = UI.Font;
            Text = text;
            Size = new Size(PreferredWidth, UI.S(32));
        }

        public bool Chevron, ChevronUp, PathText;
        public string Glyph;

        public int PreferredWidth { get { return Math.Max(UI.S(80), Fonts.Width(Text, Font) + UI.S(32) + (Chevron ? UI.S(18) : 0) + (Glyph != null ? UI.S(22) : 0)); } }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color bg = Parent != null ? Parent.BackColor : th.Chrome;
            g.Clear(bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            int rad = UI.S(8);
            Color shade = th.Dark ? Color.White : Color.Black;
            Color fill, line, text;
            if (!Enabled)
            {
                fill = primary ? Theme.Blend(bg, th.Accent, 0.35) : Theme.Blend(bg, th.Ctl, 0.6);
                line = primary ? fill : Theme.Blend(bg, th.CtlBorder, 0.6);
                text = primary ? Theme.Blend(fill, th.OnAccent, 0.55) : th.MenuDisabled;
            }
            else if (primary)
            {
                fill = down ? Theme.Blend(th.Accent, shade, 0.2) : hover ? Theme.Blend(th.Accent, shade, 0.1) : th.Accent;
                line = fill;
                text = th.OnAccent;
            }
            else if (Danger)
            {
                fill = down ? Theme.Blend(th.Ctl, th.Error, 0.24) : hover ? Theme.Blend(th.Ctl, th.Error, 0.13) : th.Ctl;
                line = hover || down ? Theme.Blend(th.CtlBorder, th.Error, 0.55) : th.CtlBorder;
                text = th.Dark && !th.HighContrast && (hover || down) ? Theme.Blend(th.Error, Color.White, 0.35) : th.Error;
            }
            else
            {
                fill = down ? Theme.Blend(th.CtlHover, shade, 0.06) : hover ? th.CtlHover : th.Ctl;
                line = th.CtlBorder;
                text = th.Fore;
            }
            Glyphs.FillRound(g, fill, r, rad);
            if (line != fill) Glyphs.DrawRound(g, line, r, rad);
            if (Focused && ShowFocusCues) Glyphs.Ring(g, primary ? th.Fore : th.Accent, new Rectangle(0, 0, Width, Height), rad, Glyphs.FocusWidth);
            Rectangle tr = r;
            if (Chevron)
            {
                tr = new Rectangle(r.X + UI.S(4), r.Y, r.Width - UI.S(22), r.Height);
                Glyphs.Chevron(g, new Rectangle(r.Right - UI.S(26), r.Y, UI.S(20), r.Height), Enabled ? th.ChromeDim : th.MenuDisabled, UI.S(4), ChevronUp);
            }
            if (Glyph != null)
            {
                int tw = Math.Min(Fonts.Width(Text, Font), Math.Max(0, tr.Width - UI.S(40)));
                int gx = tr.X + (tr.Width - tw - UI.S(22)) / 2;
                Glyphs.Draw(Glyph, g, new Rectangle(gx, r.Y + (r.Height - UI.S(16)) / 2, UI.S(16), UI.S(16)), primary ? text : th.ChromeDim);
                tr = new Rectangle(gx + UI.S(22), r.Y, tw + UI.S(4), r.Height);
                TextRenderer.DrawText(g, Text, Font, tr, text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                return;
            }
            TextRenderer.DrawText(g, Text, Font, Rectangle.Inflate(tr, -UI.S(10), 0), text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                  (PathText ? TextFormatFlags.PathEllipsis : TextFormatFlags.EndEllipsis));
        }
    }

    interface IRow
    {
        int MeasureHeight(int width);
    }

    class Card : Control, IThemed
    {
        Theme th;
        readonly List<Control> rows = new List<Control>();
        public bool Dividers = true;

        public void SetTheme(Theme t) { th = t; BackColor = t.Card; Invalidate(true); }

        public Card(Theme t)
        {
            th = t;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = t.Card;
            Font = UI.Font;
        }

        public static int Pad { get { return UI.S(4); } }
        public static int Radius { get { return UI.S(10); } }

        public T Add<T>(T row) where T : Control
        {
            rows.Add(row);
            Controls.Add(row);
            return row;
        }

        public int Arrange(int width)
        {
            int y = Pad;
            for (int i = 0; i < rows.Count; i++)
            {
                Control c = rows[i];
                IRow r = c as IRow;
                int h = r != null ? r.MeasureHeight(width - Pad * 2) : c.Height;
                c.SetBounds(Pad, y, width - Pad * 2, h);
                y += h;
                if (Dividers && i < rows.Count - 1) y += 1;
            }
            Size = new Size(width, y + Pad);
            return Height;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Chrome);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Glyphs.FillRound(g, th.Card, r, Radius);
            Glyphs.DrawRound(g, th.CardBorder, r, Radius);
            if (!Dividers) return;
            using (Pen p = new Pen(th.CardBorder))
                for (int i = 0; i < rows.Count - 1; i++)
                {
                    int y = rows[i].Bottom;
                    g.DrawLine(p, UI.S(16), y, Width - UI.S(16), y);
                }
        }
    }

    class SwitchRow : Control, IRow, IThemed
    {
        Theme th;
        readonly Tween knob;
        string note;
        bool on, hover;
        public event EventHandler CheckedChanged;

        public void SetTheme(Theme t) { th = t; Invalidate(true); }

        public SwitchRow(Theme t, string title, string note, bool value)
        {
            th = t;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true;
            Font = UI.Font;
            Text = title;
            this.note = note ?? "";
            on = value;
            knob = new Tween(on ? 1f : 0f, Invalidate);
            AccessibleRole = AccessibleRole.CheckButton;
        }

        public string Note { get { return note; } set { note = value ?? ""; Invalidate(); } }

        public bool Checked
        {
            get { return on; }
            set { Set(value, false); }
        }

        void Set(bool v, bool animate)
        {
            if (on == v) return;
            on = v;
            knob.To(on ? 1f : 0f, 170, animate && IsHandleCreated && Visible);
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }

        public void Toggle() { if (Enabled) Set(!on, true); }

        static int PadX { get { return UI.S(14); } }

        int TextWidth(int width) { return Math.Max(UI.S(60), width - PadX * 2 - SwitchPaint.Size.Width - UI.S(20)); }

        int TextBlock(int tw)
        {
            int h = Fonts.Height(Text, Font, tw);
            if (note.Length > 0) h += UI.S(2) + Fonts.Height(note, Fonts.Note, tw);
            return h;
        }

        public int MeasureHeight(int width) { return Math.Max(UI.S(52), TextBlock(TextWidth(width)) + UI.S(24)); }

        Rectangle Track
        {
            get
            {
                Size s = SwitchPaint.Size;
                return new Rectangle(Width - PadX - s.Width, (Height - s.Height) / 2, s.Width, s.Height);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Focus();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Toggle();
        }

        bool spaceDown;

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None) { spaceDown = true; e.Handled = e.SuppressKeyPress = true; }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && spaceDown) { spaceDown = false; Toggle(); e.Handled = true; }
        }

        protected override void OnLeave(EventArgs e) { base.OnLeave(e); spaceDown = false; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Card);
            if (hover && Enabled && !th.HighContrast) Glyphs.FillRound(g, th.CardHover, new Rectangle(0, 0, Width - 1, Height - 1), UI.S(8));
            int tw = TextWidth(Width);
            int block = TextBlock(tw);
            int y = (Height - block) / 2;
            int th1 = Fonts.Height(Text, Font, tw);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(PadX, y, tw, th1), Enabled ? th.Fore : th.MenuDisabled, Fonts.Wrap);
            if (note.Length > 0)
                TextRenderer.DrawText(g, note, Fonts.Note, new Rectangle(PadX, y + th1 + UI.S(2), tw, Height), th.ChromeDim, Fonts.Wrap);
            Rectangle tr = Track;
            SwitchPaint.Draw(g, tr, knob.Value, hover, Enabled, th);
            if (Focused && ShowFocusCues)
            {
                Rectangle ring = Rectangle.Inflate(tr, UI.S(4), UI.S(4));
                Glyphs.Ring(g, th.Accent, ring, ring.Height / 2, Glyphs.FocusWidth);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) knob.Dispose();
            base.Dispose(disposing);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acc(this); }

        class Acc : ControlAccessibleObject
        {
            readonly SwitchRow o;
            public Acc(SwitchRow r) : base(r) { o = r; }
            public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }
            public override string Name { get { return o.Text; } set { } }
            public override string Description { get { return o.note; } }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates s = base.State;
                    if (o.on) s |= AccessibleStates.Checked;
                    return s;
                }
            }
            public override string DefaultAction { get { return o.on ? "Turn off" : "Turn on"; } }
            public override void DoDefaultAction() { o.Toggle(); }
        }
    }

    class FieldRow : Control, IRow, IThemed
    {
        Theme th;
        readonly string note;
        public readonly Control Field;
        readonly Control extra;
        readonly bool stacked;
        readonly int fieldW, fieldH;

        public void SetTheme(Theme t) { th = t; Invalidate(true); }

        public FieldRow(Theme t, string title, string note, Control field, int fieldWidth, int fieldHeight, bool stacked, Control extra)
        {
            th = t;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = UI.Font;
            Text = title ?? "";
            this.note = note ?? "";
            Field = field;
            this.extra = extra;
            this.stacked = stacked;
            fieldW = fieldWidth;
            fieldH = fieldHeight > 0 ? fieldHeight : UI.S(32);
            if (field != null)
            {
                Controls.Add(field);
                InputBox ib = field as InputBox;
                Control target = ib != null ? (Control)ib.Box : field;
                if (target is ButtonBase)
                {
                    if (string.IsNullOrEmpty(target.AccessibleDescription) && Text.Length > 0) target.AccessibleDescription = Text + (this.note.Length > 0 ? ". " + this.note : "");
                }
                else
                {
                    if (string.IsNullOrEmpty(target.AccessibleName) && Text.Length > 0) target.AccessibleName = Text;
                    if (string.IsNullOrEmpty(target.AccessibleDescription) && this.note.Length > 0) target.AccessibleDescription = this.note;
                }
            }
            if (extra != null) Controls.Add(extra);
        }

        static int PadX { get { return UI.S(14); } }
        static int PadY { get { return UI.S(12); } }

        int TextWidth(int width) { return stacked ? Math.Max(UI.S(60), width - PadX * 2) : Math.Max(UI.S(80), width - PadX * 2 - fieldW - UI.S(20)); }

        int TitleH(int tw) { return Text.Length > 0 ? Fonts.Height(Text, Font, tw) : 0; }

        int TextBlock(int tw)
        {
            int h = TitleH(tw);
            if (note.Length > 0) h += (h > 0 ? UI.S(2) : 0) + Fonts.Height(note, Fonts.Note, tw);
            return h;
        }

        public int MeasureHeight(int width)
        {
            int tb = TextBlock(TextWidth(width));
            if (stacked) return PadY + tb + (tb > 0 ? UI.S(10) : 0) + fieldH + PadY + UI.S(2);
            return Math.Max(UI.S(56), Math.Max(tb, fieldH) + PadY * 2);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Field == null) return;
            if (stacked)
            {
                int tb = TextBlock(TextWidth(Width));
                int y = PadY + tb + (tb > 0 ? UI.S(10) : 0);
                int ew = extra != null ? extra.Width : 0;
                int fw = Width - PadX * 2 - (extra != null ? ew + UI.S(8) : 0);
                Field.SetBounds(PadX, y, Math.Max(UI.S(40), fw), fieldH);
                if (extra != null) extra.SetBounds(PadX + fw + UI.S(8), y, ew, extra.Height);
            }
            else Field.SetBounds(Width - PadX - fieldW, (Height - fieldH) / 2, fieldW, fieldH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Card);
            int tw = TextWidth(Width);
            int tb = TextBlock(tw);
            int y = stacked ? PadY : (Height - tb) / 2;
            int h1 = TitleH(tw);
            if (h1 > 0) TextRenderer.DrawText(g, Text, Font, new Rectangle(PadX, y, tw, h1), Enabled ? th.Fore : th.MenuDisabled, Fonts.Wrap);
            if (note.Length > 0)
                TextRenderer.DrawText(g, note, Fonts.Note, new Rectangle(PadX, y + h1 + (h1 > 0 ? UI.S(2) : 0), tw, Height), th.ChromeDim, Fonts.Wrap);
        }
    }

    class KeysRow : Control, IRow, IThemed
    {
        Theme th;
        readonly List<string[]> combos = new List<string[]>();

        public void SetTheme(Theme t) { th = t; Invalidate(true); }

        public KeysRow(Theme t, string title, params string[] keys)
        {
            th = t;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = UI.Font;
            Text = title;
            foreach (string k in keys) combos.Add(k.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = title + ": " + string.Join(", ", keys).Replace(' ', '+');
        }

        public int MeasureHeight(int width) { return UI.S(44); }

        static int CapW(string k) { return Math.Max(UI.S(24), Fonts.Width(k, Fonts.Small) + UI.S(12)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Card);
            int capH = UI.S(22);
            int total = 0;
            for (int c = 0; c < combos.Count; c++)
            {
                if (c > 0) total += UI.S(14);
                for (int k = 0; k < combos[c].Length; k++) total += CapW(combos[c][k]) + (k > 0 ? UI.S(4) : 0);
            }
            int x = Width - UI.S(14) - total;
            int y = (Height - capH) / 2;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(UI.S(14), 0, Math.Max(10, x - UI.S(26)), Height), th.Fore, Fonts.Line);
            for (int c = 0; c < combos.Count; c++)
            {
                if (c > 0) x += UI.S(14);
                for (int k = 0; k < combos[c].Length; k++)
                {
                    if (k > 0) x += UI.S(4);
                    int w = CapW(combos[c][k]);
                    Glyphs.Cap(g, th, new Rectangle(x, y, w, capH), combos[c][k], Fonts.Small);
                    x += w;
                }
            }
        }
    }

    class BulletRow : Control, IRow, IThemed
    {
        Theme th;
        readonly string glyph;

        public void SetTheme(Theme t) { th = t; Invalidate(true); }

        public BulletRow(Theme t, string glyph, string text)
        {
            th = t;
            this.glyph = glyph;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = UI.Font;
            Text = text;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = text;
        }

        static int TextX { get { return UI.S(42); } }

        public int MeasureHeight(int width) { return Math.Max(UI.S(34), Fonts.Height(Text, Font, width - TextX - UI.S(14)) + UI.S(14)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Card);
            int tw = Width - TextX - UI.S(14);
            int h = Fonts.Height(Text, Font, tw);
            int y = (Height - h) / 2;
            int line = Fonts.Height("Ag", Font, 1000);
            Glyphs.Draw(glyph, g, new Rectangle(UI.S(14), y + (line - UI.S(16)) / 2, UI.S(16), UI.S(16)), th.Good);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(TextX, y, tw, h), th.Fore, Fonts.Wrap);
        }
    }

    class Segmented : Control, IThemed
    {
        Theme th;
        readonly string[] items;
        readonly Tween slide;
        int sel, hot = -1;
        public event EventHandler SelectedIndexChanged;

        public void SetTheme(Theme t) { th = t; Invalidate(true); }

        public Segmented(Theme t, string[] list, int selected)
        {
            th = t;
            items = list;
            sel = Math.Max(0, Math.Min(list.Length - 1, selected));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true;
            Font = UI.Font;
            slide = new Tween(sel, Invalidate);
        }

        public int SelectedIndex
        {
            get { return sel; }
            set { Pick(value, false); }
        }

        void Pick(int i, bool animate)
        {
            i = Math.Max(0, Math.Min(items.Length - 1, i));
            if (i == sel) return;
            sel = i;
            slide.To(sel, 190, animate && IsHandleCreated && Visible);
            AccessibilityNotifyClients(AccessibleEvents.Selection, sel);
            if (Focused) AccessibilityNotifyClients(AccessibleEvents.Focus, sel);
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        static int Inner { get { return UI.S(3); } }

        int SegW(int i) { return Fonts.Width(items[i], Fonts.Semi) + UI.S(30); }

        public int PreferredWidth
        {
            get
            {
                int w = Inner * 2;
                for (int i = 0; i < items.Length; i++) w += SegW(i);
                return w;
            }
        }

        Rectangle Seg(int i)
        {
            int extra = Math.Max(0, Width - PreferredWidth) / Math.Max(1, items.Length);
            int x = Inner;
            for (int k = 0; k < i; k++) x += SegW(k) + extra;
            return new Rectangle(x, Inner, SegW(i) + extra, Height - Inner * 2);
        }

        int IndexAt(Point p)
        {
            for (int i = 0; i < items.Length; i++)
            {
                Rectangle r = Seg(i);
                if (p.X >= r.Left && p.X < r.Right && p.Y >= 0 && p.Y < Height) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = IndexAt(e.Location);
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            int i = IndexAt(e.Location);
            if (i >= 0) Pick(i, true);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Left || k == Keys.Right || k == Keys.Home || k == Keys.End) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int n = sel;
            if (e.KeyCode == Keys.Left) n = sel - 1;
            else if (e.KeyCode == Keys.Right) n = sel + 1;
            else if (e.KeyCode == Keys.Home) n = 0;
            else if (e.KeyCode == Keys.End) n = items.Length - 1;
            else return;
            e.Handled = true;
            Pick(n, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Card);
            int rad = UI.S(8);
            Glyphs.FillRound(g, th.Track, new Rectangle(0, 0, Width - 1, Height - 1), rad);
            if (th.HighContrast) Glyphs.DrawRound(g, th.Fore, new Rectangle(0, 0, Width - 1, Height - 1), rad);
            float pos = Math.Max(0f, Math.Min(items.Length - 1, slide.Value));
            int a = (int)Math.Floor(pos), b = Math.Min(items.Length - 1, a + 1);
            float f = pos - a;
            Rectangle ra = Seg(a), rb = Seg(b);
            RectangleF thumb = new RectangleF(ra.X + (rb.X - ra.X) * f, ra.Y, ra.Width + (rb.Width - ra.Width) * f, ra.Height);
            Color tf = th.HighContrast ? SystemColors.Highlight : th.Dark ? th.CtlHover : Color.White;
            Glyphs.FillRoundF(g, tf, thumb, UI.S(6));
            if (!th.Dark && !th.HighContrast)
            {
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath p = Glyphs.RoundF(new RectangleF(thumb.X + 0.5f, thumb.Y + 0.5f, thumb.Width - 1f, thumb.Height - 1f), UI.S(6)))
                using (Pen pen = new Pen(th.CtlBorder))
                    g.DrawPath(pen, p);
                g.SmoothingMode = old;
            }
            for (int i = 0; i < items.Length; i++)
            {
                bool s = i == sel;
                Color c = th.HighContrast ? (s ? SystemColors.HighlightText : th.Fore) : s ? th.Fore : i == hot ? th.ChromeFore : th.ChromeDim;
                TextRenderer.DrawText(g, items[i], s ? Fonts.Semi : Font, Seg(i), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            if (Focused && ShowFocusCues) Glyphs.Ring(g, th.Accent, new Rectangle(0, 0, Width, Height), rad, Glyphs.FocusWidth);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) slide.Dispose();
            base.Dispose(disposing);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acc(this); }

        class Acc : ControlAccessibleObject
        {
            readonly Segmented o;
            public Acc(Segmented s) : base(s) { o = s; }
            public override AccessibleRole Role { get { return AccessibleRole.Grouping; } }
            public override string Value { get { return o.items[o.sel]; } set { } }
            public override int GetChildCount() { return o.items.Length; }
            public override AccessibleObject GetChild(int index) { return index >= 0 && index < o.items.Length ? new Part(o, this, index) : null; }
            public override AccessibleObject GetFocused() { return o.Focused ? GetChild(o.sel) : null; }
        }

        class Part : AccessibleObject
        {
            readonly Segmented o;
            readonly AccessibleObject parent;
            readonly int i;
            public Part(Segmented s, AccessibleObject p, int index) { o = s; parent = p; i = index; }
            public override string Name { get { return o.items[i]; } set { } }
            public override AccessibleRole Role { get { return AccessibleRole.RadioButton; } }
            public override AccessibleObject Parent { get { return parent; } }
            public override Rectangle Bounds { get { return o.RectangleToScreen(o.Seg(i)); } }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates s = AccessibleStates.Focusable | AccessibleStates.Selectable;
                    if (i == o.sel) s |= AccessibleStates.Checked | AccessibleStates.Selected | (o.Focused ? AccessibleStates.Focused : 0);
                    return s;
                }
            }
            public override string DefaultAction { get { return "Select"; } }
            public override void DoDefaultAction() { o.Pick(i, true); }
        }
    }

    class NavList : Control, IThemed
    {
        Theme th;
        readonly List<string> titles = new List<string>();
        readonly List<string> glyphs = new List<string>();
        readonly Tween bar;
        int sel = -1, hot = -1;
        public event EventHandler SelectedIndexChanged;

        public void SetTheme(Theme t) { th = t; BackColor = t.Chrome; Invalidate(true); }

        public NavList(Theme t)
        {
            th = t;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true;
            Font = UI.Font;
            BackColor = t.Chrome;
            bar = new Tween(0f, Invalidate);
            AccessibleRole = AccessibleRole.PageTabList;
            AccessibleName = "Settings pages";
        }

        public void Add(string title, string glyph)
        {
            titles.Add(title);
            glyphs.Add(glyph);
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return sel; }
            set { Pick(value, false); }
        }

        void Pick(int i, bool animate)
        {
            if (i < 0 || i >= titles.Count || i == sel) return;
            bool first = sel < 0;
            sel = i;
            bar.To(i, 200, animate && !first && IsHandleCreated && Visible);
            AccessibilityNotifyClients(AccessibleEvents.Selection, sel);
            if (Focused) AccessibilityNotifyClients(AccessibleEvents.Focus, sel);
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        static int ItemH { get { return UI.S(38); } }
        static int Gap { get { return UI.S(2); } }
        static int TopPad { get { return UI.S(14); } }

        Rectangle Item(int i) { return new Rectangle(UI.S(10), TopPad + i * (ItemH + Gap), Width - UI.S(20), ItemH); }

        int IndexAt(Point p)
        {
            for (int i = 0; i < titles.Count; i++) if (Item(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = IndexAt(e.Location);
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int i = IndexAt(e.Location);
            if (i < 0) return;
            Focus();
            Pick(i, true);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int n;
            if (e.KeyCode == Keys.Up) n = Math.Max(0, sel - 1);
            else if (e.KeyCode == Keys.Down) n = Math.Min(titles.Count - 1, sel + 1);
            else if (e.KeyCode == Keys.Home) n = 0;
            else if (e.KeyCode == Keys.End) n = titles.Count - 1;
            else return;
            e.Handled = true;
            Pick(n, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int rad = UI.S(8);
            for (int i = 0; i < titles.Count; i++)
            {
                Rectangle r = Item(i);
                bool s = i == sel;
                if (s) Glyphs.FillRound(g, th.SideSel, r, rad);
                else if (i == hot) Glyphs.Hover(g, th, th.SideHover, r, rad);
                Color fg = th.HighContrast && s ? SystemColors.HighlightText : s ? th.Fore : th.ChromeFore;
                Rectangle gr = new Rectangle(r.X + UI.S(13), r.Y + (r.Height - UI.S(18)) / 2, UI.S(18), UI.S(18));
                if (glyphs[i] == "face") Glyphs.Face(g, gr, th, Glyphs.FaceIdle, 0, false);
                else Glyphs.Draw(glyphs[i], g, gr, th.HighContrast && s ? SystemColors.HighlightText : s ? th.Fore : th.ChromeDim);
                TextRenderer.DrawText(g, titles[i], s ? Fonts.Semi : Font, new Rectangle(r.X + UI.S(42), r.Y, r.Width - UI.S(48), r.Height), fg, Fonts.Line);
            }
            if (sel >= 0 && !th.HighContrast)
            {
                float y = TopPad + bar.Value * (ItemH + Gap) + (ItemH - UI.S(16)) / 2f;
                Glyphs.FillRoundF(g, th.Accent, new RectangleF(UI.S(10) + UI.S(1), y, UI.S(3), UI.S(16)), 1.5f * UI.Scale);
            }
            if (Focused && ShowFocusCues && sel >= 0) Glyphs.Ring(g, th.HighContrast ? SystemColors.HighlightText : th.Accent, Item(sel), rad, Glyphs.FocusWidth);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) bar.Dispose();
            base.Dispose(disposing);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acc(this); }

        class Acc : ControlAccessibleObject
        {
            readonly NavList o;
            public Acc(NavList n) : base(n) { o = n; }
            public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
            public override int GetChildCount() { return o.titles.Count; }
            public override AccessibleObject GetChild(int index) { return index >= 0 && index < o.titles.Count ? new Tab(o, this, index) : null; }
            public override AccessibleObject GetFocused() { return o.Focused && o.sel >= 0 ? GetChild(o.sel) : null; }
        }

        class Tab : AccessibleObject
        {
            readonly NavList o;
            readonly AccessibleObject parent;
            readonly int i;
            public Tab(NavList n, AccessibleObject p, int index) { o = n; parent = p; i = index; }
            public override string Name { get { return o.titles[i]; } set { } }
            public override AccessibleRole Role { get { return AccessibleRole.PageTab; } }
            public override AccessibleObject Parent { get { return parent; } }
            public override Rectangle Bounds { get { return o.RectangleToScreen(o.Item(i)); } }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates s = AccessibleStates.Focusable | AccessibleStates.Selectable;
                    if (i == o.sel) s |= AccessibleStates.Selected | (o.Focused ? AccessibleStates.Focused : 0);
                    return s;
                }
            }
            public override string DefaultAction { get { return "Switch"; } }
            public override void DoDefaultAction() { o.Pick(i, true); }
        }
    }

    interface IThemed
    {
        void SetTheme(Theme t);
    }

    sealed class WheelRouter : IMessageFilter
    {
        static WheelRouter installed;

        public static void Install()
        {
            if (installed != null) return;
            installed = new WheelRouter();
            try { Application.AddMessageFilter(installed); } catch { }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct ScrollInfo { public int Size, Mask, Min, Max, Page, Pos, TrackPos; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A) return false;
            TextBox box = Control.FromHandle(m.HWnd) as TextBox;
            if (box == null) return false;
            ScrollHost host = null;
            for (Control p = box.Parent; p != null && host == null; p = p.Parent) host = p as ScrollHost;
            if (host == null) return false;
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            if (box.Multiline && CanScroll(box, delta)) return false;
            host.Wheel(delta);
            return true;
        }

        static bool CanScroll(TextBox box, int delta)
        {
            try
            {
                ScrollInfo si = new ScrollInfo();
                si.Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ScrollInfo));
                si.Mask = 0x7;
                if (!GetScrollInfo(box.Handle, 1, ref si)) return false;
                int last = si.Max - Math.Max(0, si.Page - 1);
                if (last <= si.Min) return false;
                return delta > 0 ? si.Pos > si.Min : si.Pos < last;
            }
            catch { return true; }
        }
    }

    class ScrollHost : Control, IThemed
    {
        Theme th;
        public readonly Panel Content = new Panel();
        readonly Tween glide;
        int contentH, target;
        bool dragging, barHover;
        int dragOffset;

        public void SetTheme(Theme t) { th = t; BackColor = t.Chrome; Content.BackColor = t.Chrome; Invalidate(true); }

        public ScrollHost(Theme t)
        {
            th = t;
            glide = new Tween(0f, Apply);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = t.Chrome;
            Content.BackColor = t.Chrome;
            Controls.Add(Content);
            WheelRouter.Install();
        }

        public static int BarW { get { return UI.S(12); } }

        public int ContentHeight
        {
            get { return contentH; }
            set { contentH = Math.Max(0, value); target = Clamp(target); glide.To(target, 0, false); }
        }

        int Max { get { return Math.Max(0, contentH - Height); } }
        int Clamp(int y) { return Math.Max(0, Math.Min(Max, y)); }
        public int Offset { get { return (int)Math.Round(glide.Value); } }

        void Apply()
        {
            Content.SetBounds(0, -Offset, Math.Max(1, Width - BarW), Math.Max(Height, contentH));
            Invalidate(new Rectangle(Width - BarW, 0, BarW, Height));
        }

        public void ScrollTo(int y, bool animate)
        {
            target = Clamp(y);
            glide.To(target, 140, animate);
        }

        public void Reveal(Control c)
        {
            if (c == null || !Content.Contains(c)) return;
            Rectangle r = Content.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
            int margin = UI.S(16);
            int y = target;
            if (r.Top - margin < y) y = r.Top - margin;
            else if (r.Bottom + margin > y + Height) y = Math.Min(r.Top - margin, r.Bottom + margin - Height);
            if (y != target) ScrollTo(y, true);
        }

        public void Watch(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c.TabStop || c is TextBoxBase) c.Enter += delegate (object o, EventArgs e) { if (Control.MouseButtons == MouseButtons.None) Reveal((Control)o); };
                if (c.HasChildren) Watch(c);
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (glide == null) return;
            target = Clamp(target);
            glide.To(target, 0, false);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Wheel(e.Delta);
        }

        readonly WheelAcc wheel = new WheelAcc();

        public void Wheel(int delta)
        {
            if (Max == 0) return;
            int px = wheel.Take(delta, UI.S(56));
            if (px != 0) ScrollTo(target - px, true);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragging && !Capture) { dragging = false; barHover = false; Invalidate(); }
        }

        Rectangle Thumb()
        {
            if (Max == 0 || Height <= 0) return Rectangle.Empty;
            int len = Math.Max(UI.S(28), (int)((long)Height * Height / Math.Max(1, contentH)));
            int y = (int)((long)(Height - len) * Offset / Math.Max(1, Max));
            return new Rectangle(Width - BarW, y, BarW, len);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                Rectangle t = Thumb();
                int range = Math.Max(1, Height - t.Height);
                ScrollTo((int)((long)(e.Y - dragOffset) * Max / range), false);
                return;
            }
            bool h = e.X >= Width - BarW && Max > 0;
            if (h != barHover) { barHover = h; Invalidate(new Rectangle(Width - BarW, 0, BarW, Height)); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!dragging && barHover) { barHover = false; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || e.X < Width - BarW || Max == 0) return;
            Rectangle t = Thumb();
            if (t.Contains(e.Location)) { dragging = true; dragOffset = e.Y - t.Y; Capture = true; }
            else ScrollTo(target + (e.Y < t.Y ? -Height : Height) * 9 / 10, true);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            barHover = e.X >= Width - BarW;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Rectangle t = Thumb();
            if (t.IsEmpty) return;
            bool wide = barHover || dragging;
            int w = wide ? UI.S(8) : UI.S(4);
            Rectangle r = new Rectangle(Width - UI.S(3) - w, t.Y + UI.S(2), w, t.Height - UI.S(4));
            Glyphs.FillRound(g, wide ? th.ThumbHover : th.Thumb, r, w / 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) glide.Dispose();
            base.Dispose(disposing);
        }
    }

    class Choice
    {
        public string Title = "", Note = "", Badge = "";
    }

    class ThemedCombo : Control, IThemed
    {
        readonly List<Choice> items = new List<Choice>();
        Theme th;
        int sel = -1;
        bool hover;
        ChoicePopup popup;
        int closedTick = Environment.TickCount - 100000;
        public bool NotesBelow;
        public event EventHandler SelectedIndexChanged;

        public void SetTheme(Theme t) { th = t; Invalidate(true); }

        public ThemedCombo(Theme t, string[] list, int selected)
        {
            th = t;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true;
            Font = UI.Font;
            AccessibleRole = AccessibleRole.ComboBox;
            if (list != null)
                foreach (string s in list)
                {
                    Choice c = new Choice();
                    c.Title = s ?? "";
                    items.Add(c);
                }
            if (selected >= 0 && selected < items.Count) sel = selected;
        }

        public void SetNote(int i, string note) { if (i >= 0 && i < items.Count) items[i].Note = note ?? ""; }
        public void SetBadge(int i, string badge) { if (i >= 0 && i < items.Count) items[i].Badge = badge ?? ""; }

        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                int v = value >= 0 && value < items.Count ? value : -1;
                if (v == sel) return;
                sel = v;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public string SelectedText { get { return sel >= 0 ? items[sel].Title : ""; } }

        public int PreferredWidth
        {
            get
            {
                int w = 0;
                foreach (Choice c in items) w = Math.Max(w, Fonts.Width(c.Title, Font));
                return w + UI.S(12) + UI.S(38);
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new ComboAccessible(this); }

        class ComboAccessible : Control.ControlAccessibleObject
        {
            readonly ThemedCombo c;
            public ComboAccessible(ThemedCombo owner) : base(owner) { c = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.ComboBox; } }
            public override string Value { get { return c.SelectedText; } set { } }
            public override string DefaultAction { get { return "Open"; } }
            public override void DoDefaultAction() { c.ShowList(); }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates s = base.State | AccessibleStates.HasPopup;
                    s |= c.popup != null ? AccessibleStates.Expanded : AccessibleStates.Collapsed;
                    return s;
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            if (popup != null) { popup.Close(); return; }
            if (unchecked(Environment.TickCount - closedTick) < 300) return;
            ShowList();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if ((keyData & Keys.Control) != 0) return base.IsInputKey(keyData);
            if ((keyData & Keys.Alt) != 0) return k == Keys.Down || k == Keys.Up || base.IsInputKey(keyData);
            if (k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Space || k == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (items.Count == 0 || e.Control) return;
            if (e.Alt)
            {
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) { ShowList(); e.Handled = e.SuppressKeyPress = true; }
                return;
            }
            if (e.Shift && e.KeyCode != Keys.Down && e.KeyCode != Keys.Up) return;
            if (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { ShowList(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown) { SelectedIndex = Math.Min(items.Count - 1, sel + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.PageUp) { SelectedIndex = Math.Max(0, sel - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Home) { SelectedIndex = 0; e.Handled = true; }
            else if (e.KeyCode == Keys.End) { SelectedIndex = items.Count - 1; e.Handled = true; }
        }

        public void ShowList()
        {
            if (items.Count == 0 || !Enabled || popup != null) return;
            ChoicePopup p = new ChoicePopup(th, items, sel, NotesBelow, Width);
            popup = p;
            p.Picked += delegate (int i) { SelectedIndex = i; };
            p.Closed += delegate (object o, ToolStripDropDownClosedEventArgs e)
            {
                if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && IsHandleCreated && ClientRectangle.Contains(PointToClient(Control.MousePosition)))
                    closedTick = Environment.TickCount;
                popup = null;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(delegate { p.Dispose(); }));
            };
            Invalidate();
            bool wide = p.Width > Width;
            int gap = UI.S(4), below = int.MaxValue, above = 0;
            try
            {
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                below = wa.Bottom - PointToScreen(new Point(0, Height + gap)).Y;
                above = PointToScreen(Point.Empty).Y - gap - wa.Top;
            }
            catch { }
            bool up = p.Height > below && above > below;
            p.LimitHeight(Math.Max(UI.S(120), (up ? above : below) - UI.S(8)));
            if (up) p.Show(this, new Point(wide ? Width : 0, -gap), wide ? ToolStripDropDownDirection.AboveLeft : ToolStripDropDownDirection.AboveRight);
            else if (wide) p.Show(this, new Point(Width, Height + gap), ToolStripDropDownDirection.BelowLeft);
            else p.Show(this, new Point(0, Height + gap));
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color bg = Parent != null ? Parent.BackColor : th.Card;
            g.Clear(bg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            int rad = UI.S(8);
            bool open = popup != null;
            Color fill = !Enabled ? Theme.Blend(bg, th.Ctl, 0.5) : open || hover ? th.CtlHover : th.Ctl;
            Color line = !Enabled ? Theme.Blend(bg, th.CtlBorder, 0.6) : open ? th.Accent : th.CtlBorder;
            Glyphs.FillRound(g, fill, r, rad);
            Glyphs.DrawRound(g, line, r, rad);
            int arrowW = UI.S(34);
            Rectangle tr = new Rectangle(UI.S(12), 0, Math.Max(1, Width - UI.S(12) - arrowW), Height);
            TextRenderer.DrawText(g, SelectedText, Font, tr, Enabled ? th.Fore : th.MenuDisabled, Fonts.Line);
            Glyphs.Chevron(g, new Rectangle(Width - arrowW, 0, arrowW - UI.S(6), Height), Enabled ? th.ChromeDim : th.MenuDisabled, UI.S(4), open);
            if (Focused && ShowFocusCues && !open) Glyphs.Ring(g, th.Accent, new Rectangle(0, 0, Width, Height), rad, Glyphs.FocusWidth);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && popup != null)
            {
                ChoicePopup p = popup;
                popup = null;
                try { p.Close(); } catch { }
                try { p.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }

    class ChoicePopup : ToolStripDropDown
    {
        readonly ChoiceList list;
        readonly Color border;
        public event Action<int> Picked;

        public ChoicePopup(Theme t, List<Choice> items, int selected, bool below, int minWidth)
        {
            border = t.Border;
            list = new ChoiceList(t, items, selected, below);
            Menus.Track(this);
            AutoSize = false;
            Padding = Padding.Empty;
            Margin = Padding.Empty;
            DropShadowEnabled = true;
            BackColor = t.MenuBack;
            Size sz = list.Measure(minWidth);
            list.Size = sz;
            ToolStripControlHost host = new ToolStripControlHost(list);
            host.AutoSize = false;
            host.Margin = Padding.Empty;
            host.Padding = Padding.Empty;
            host.Size = sz;
            Items.Add(host);
            Size = sz;
            list.Picked += delegate (int i)
            {
                if (Picked != null) Picked(i);
                Close();
            };
            list.Cancelled += delegate { Close(); };
        }

        public void LimitHeight(int h)
        {
            if (h <= 0 || h >= Height) return;
            list.Height = h;
            foreach (ToolStripItem it in Items) it.Size = new Size(it.Width, h);
            Size = new Size(Width, h);
            list.CenterHot();
        }

        bool styled;

        void Style()
        {
            if (styled || list == null || !IsHandleCreated) return;
            styled = true;
            list.Rounded = Native.RoundPopup(Handle, border);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Style();
        }

        protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
        {
            base.OnOpening(e);
            Style();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            Style();
            list.Focus();
        }
    }

    class ChoiceList : Control
    {
        readonly Theme th;
        readonly List<Choice> items;
        readonly bool below;
        readonly int sel;
        int hot, scroll;
        public bool Rounded;
        public event Action<int> Picked;
        public event EventHandler Cancelled;

        public ChoiceList(Theme t, List<Choice> list, int selected, bool notesBelow)
        {
            th = t;
            items = list;
            sel = selected;
            hot = selected >= 0 ? selected : 0;
            below = notesBelow;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true;
            Font = UI.Font;
            BackColor = t.MenuBack;
        }

        static int Pad { get { return UI.S(4); } }

        int RowH(int i) { return below && items[i].Note.Length > 0 ? UI.S(50) : UI.S(34); }

        int ContentH
        {
            get
            {
                int h = Pad * 2;
                for (int i = 0; i < items.Count; i++) h += RowH(i);
                return h;
            }
        }

        int MaxScroll { get { return Math.Max(0, ContentH - Height); } }

        static int BadgeW(string b) { return Fonts.Width(b, Fonts.Small) + UI.S(12); }

        public Size Measure(int minWidth)
        {
            int w = 0;
            foreach (Choice c in items)
            {
                int line = Fonts.Width(c.Title, Font) + (c.Badge.Length > 0 ? UI.S(8) + BadgeW(c.Badge) : 0);
                if (c.Note.Length > 0)
                {
                    int nw = Fonts.Width(c.Note, Fonts.Note);
                    line = below ? Math.Max(line, nw) : line + UI.S(28) + nw;
                }
                w = Math.Max(w, line);
            }
            w += Pad * 2 + UI.S(18) + UI.S(16);
            int maxW = UI.S(520);
            int maxH = UI.S(420);
            try { maxH = Math.Max(UI.S(200), Screen.FromPoint(Control.MousePosition).WorkingArea.Height * 6 / 10); } catch { }
            return new Size(Math.Min(maxW, Math.Max(minWidth, w)), Math.Min(maxH, ContentH));
        }

        Rectangle RowRect(int i)
        {
            int y = Pad - scroll;
            for (int k = 0; k < i; k++) y += RowH(k);
            return new Rectangle(Pad, y, Width - Pad * 2 - (MaxScroll > 0 ? UI.S(6) : 0), RowH(i));
        }

        int IndexAt(Point p)
        {
            for (int i = 0; i < items.Count; i++) if (RowRect(i).Contains(p)) return i;
            return -1;
        }

        void SetHot(int i)
        {
            if (i < 0 || i >= items.Count || i == hot) return;
            hot = i;
            Rectangle r = RowRect(i);
            if (r.Top < Pad) scroll = Math.Max(0, scroll - (Pad - r.Top));
            else if (r.Bottom > Height - Pad) scroll = Math.Min(MaxScroll, scroll + r.Bottom - (Height - Pad));
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Focus, hot);
            AccessibilityNotifyClients(AccessibleEvents.Selection, hot);
        }

        public void Pick(int i)
        {
            if (i >= 0 && i < items.Count && Picked != null) Picked(i);
        }

        bool revealed;

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (revealed) return;
            CenterHot();
        }

        public void CenterHot()
        {
            if (Height <= 0 || hot < 0 || hot >= items.Count) return;
            revealed = true;
            int y = Pad;
            for (int k = 0; k < hot; k++) y += RowH(k);
            scroll = Math.Max(0, Math.Min(MaxScroll, y - (Height - RowH(hot)) / 2));
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Location);
            if (i >= 0 && i != hot) { hot = i; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int i = IndexAt(e.Location);
            if (i >= 0) Pick(i);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScroll == 0) return;
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - wheel.Take(e.Delta, UI.S(34))));
            Invalidate();
        }

        readonly WheelAcc wheel = new WheelAcc();

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if ((keyData & Keys.Alt) != 0) return k == Keys.F4 || k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
            if ((keyData & Keys.Control) != 0) return base.IsInputKey(keyData);
            if (k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Enter || k == Keys.Space || k == Keys.Escape || k == Keys.Tab) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Alt)
            {
                if (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    if (Cancelled != null) Cancelled(this, EventArgs.Empty);
                    e.Handled = e.SuppressKeyPress = true;
                }
                return;
            }
            if (e.Control) return;
            switch (e.KeyCode)
            {
                case Keys.Down: SetHot(Math.Min(items.Count - 1, hot + 1)); break;
                case Keys.Up: SetHot(Math.Max(0, hot - 1)); break;
                case Keys.PageDown: case Keys.End: SetHot(items.Count - 1); break;
                case Keys.PageUp: case Keys.Home: SetHot(0); break;
                case Keys.Enter: case Keys.Space: case Keys.Tab: Pick(hot); break;
                case Keys.Escape: if (Cancelled != null) Cancelled(this, EventArgs.Empty); break;
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (char.IsControl(e.KeyChar) || items.Count == 0) return;
            string k = e.KeyChar.ToString();
            for (int n = 1; n <= items.Count; n++)
            {
                int i = (hot + n) % items.Count;
                if (items[i].Title.StartsWith(k, StringComparison.CurrentCultureIgnoreCase)) { SetHot(i); break; }
            }
            e.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(th.MenuBack);
            for (int i = 0; i < items.Count; i++)
            {
                Rectangle r = RowRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                Choice c = items[i];
                bool hc = th.HighContrast && i == hot;
                if (i == hot) Glyphs.FillRound(g, th.MenuHover, r, UI.S(6));
                if (i == sel && !th.HighContrast)
                {
                    int bh = UI.S(16);
                    Glyphs.FillRoundF(g, th.Accent, new RectangleF(r.X + UI.S(1), r.Y + (r.Height - bh) / 2f, UI.S(3), bh), UI.S(2));
                }
                Color fore = hc ? th.MenuHoverFore : th.MenuFore;
                Color dim = hc ? th.MenuHoverFore : th.ChromeDim;
                int x = r.X + UI.S(14);
                int titleH = Fonts.Height("Ag", Font, 1000);
                int noteH = Fonts.Height("Ag", Fonts.Note, 1000);
                bool twoLine = below && c.Note.Length > 0;
                int ty = twoLine ? r.Y + (r.Height - titleH - noteH - UI.S(2)) / 2 : r.Y + (r.Height - titleH) / 2;
                int right = r.Right - UI.S(12);
                int noteW = !twoLine && c.Note.Length > 0 ? Math.Min(Fonts.Width(c.Note, Fonts.Note), Math.Max(0, (right - x) / 2)) : 0;
                int titleMax = right - x - (noteW > 0 ? noteW + UI.S(20) : 0) - (c.Badge.Length > 0 ? BadgeW(c.Badge) + UI.S(8) : 0);
                int tw = Math.Min(Fonts.Width(c.Title, i == sel ? Fonts.Semi : Font), Math.Max(UI.S(20), titleMax));
                TextRenderer.DrawText(g, c.Title, i == sel ? Fonts.Semi : Font, new Rectangle(x, ty, tw + UI.S(2), titleH), fore, Fonts.Line);
                if (c.Badge.Length > 0)
                {
                    int bw = BadgeW(c.Badge), bh = UI.S(18);
                    Rectangle br = new Rectangle(x + tw + UI.S(8), ty + (titleH - bh) / 2, bw, bh);
                    Color bf = th.HighContrast ? th.MenuBack : Theme.Blend(th.MenuBack, th.Accent, th.Dark ? 0.24 : 0.12);
                    Color bt = th.HighContrast ? fore : th.Dark ? Theme.Blend(th.Accent, Color.White, 0.35) : Theme.Blend(th.Accent, Color.Black, 0.2);
                    Glyphs.FillRound(g, bf, br, bh / 2);
                    if (th.HighContrast) Glyphs.DrawRound(g, fore, new Rectangle(br.X, br.Y, br.Width - 1, br.Height - 1), bh / 2);
                    TextRenderer.DrawText(g, c.Badge, Fonts.Small, br, bt, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
                if (twoLine)
                    TextRenderer.DrawText(g, c.Note, Fonts.Note, new Rectangle(x, ty + titleH + UI.S(2), right - x, noteH), dim, Fonts.Line);
                else if (noteW > 0)
                    TextRenderer.DrawText(g, c.Note, Fonts.Note, new Rectangle(right - noteW, r.Y, noteW, r.Height), dim, Fonts.Line | TextFormatFlags.Right);
            }
            if (MaxScroll > 0)
            {
                int track = Height - Pad * 2;
                int len = Math.Max(UI.S(24), track * Height / Math.Max(1, ContentH));
                int y = Pad + (track - len) * scroll / Math.Max(1, MaxScroll);
                Glyphs.FillRound(g, th.Thumb, new Rectangle(Width - UI.S(7), y, UI.S(4), len), UI.S(2));
            }
            if (!Rounded)
                using (Pen p = new Pen(th.Border))
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new ListAcc(this); }

        class ListAcc : ControlAccessibleObject
        {
            readonly ChoiceList o;
            public ListAcc(ChoiceList l) : base(l) { o = l; }
            public override AccessibleRole Role { get { return AccessibleRole.List; } }
            public override int GetChildCount() { return o.items.Count; }
            public override AccessibleObject GetChild(int index) { return index >= 0 && index < o.items.Count ? new ItemAcc(o, this, index) : null; }
            public override AccessibleObject GetFocused() { return o.hot >= 0 && o.hot < o.items.Count ? GetChild(o.hot) : null; }
            public override AccessibleObject GetSelected() { return GetFocused(); }
        }

        class ItemAcc : AccessibleObject
        {
            readonly ChoiceList o;
            readonly AccessibleObject parent;
            readonly int i;
            public ItemAcc(ChoiceList l, AccessibleObject p, int index) { o = l; parent = p; i = index; }
            public override string Name
            {
                get
                {
                    Choice c = o.items[i];
                    string s = c.Title;
                    if (c.Badge.Length > 0) s += ", " + c.Badge;
                    if (c.Note.Length > 0) s += ", " + c.Note;
                    return s;
                }
                set { }
            }
            public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }
            public override AccessibleObject Parent { get { return parent; } }
            public override Rectangle Bounds { get { return o.RectangleToScreen(o.RowRect(i)); } }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates s = AccessibleStates.Focusable | AccessibleStates.Selectable;
                    if (i == o.hot) s |= AccessibleStates.Focused | AccessibleStates.Selected;
                    if (i == o.sel) s |= AccessibleStates.Checked;
                    return s;
                }
            }
            public override string DefaultAction { get { return "Choose"; } }
            public override void DoDefaultAction() { o.Pick(i); }
        }
    }
}

namespace MinGent
{
    class Alert : ThemedForm
    {
        readonly string message;
        readonly MessageBoxIcon icon;

        Alert(Theme t, string text, string caption, MessageBoxIcon icon, string[] labels, DialogResult[] results, int def, int danger, int cancel)
            : base(t, string.IsNullOrEmpty(caption) ? Program.AppName : caption, 460, 200)
        {
            message = text ?? "";
            this.icon = icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            int pad = UI.S(24);
            int iconW = icon != MessageBoxIcon.None ? UI.S(36) + UI.S(16) : 0;
            int textW = ClientSize.Width - pad * 2 - iconW;
            string[] paras = message.Replace("\r\n", "\n").Split(new string[] { "\n\n" }, StringSplitOptions.None);
            int block = 0;
            for (int i = 0; i < paras.Length; i++)
            {
                string para = paras[i].Trim('\n');
                if (para.Length == 0) continue;
                block += (block > 0 ? UI.S(10) : 0) + Fonts.Height(para, paras.Length > 1 && i == 0 ? Fonts.Semi : UI.Font, textW);
            }
            int y = pad + (iconW > 0 && block < UI.S(36) ? (UI.S(36) - block) / 2 : UI.S(2));
            for (int i = 0; i < paras.Length; i++)
            {
                string para = paras[i].Trim('\n');
                if (para.Length == 0) continue;
                Label l = Dialogs.MakeLabel(para, Th, false);
                l.Font = paras.Length > 1 && i == 0 ? Fonts.Semi : UI.Font;
                l.ForeColor = Th.Fore;
                int h = Fonts.Height(para, l.Font, textW);
                l.SetBounds(pad + iconW, y, textW, h + UI.S(2));
                Controls.Add(l);
                y += h + UI.S(10);
            }
            y = Math.Max(y, pad + (iconW > 0 ? UI.S(36) : 0)) + UI.S(14);
            int x = ClientSize.Width - pad;
            Button[] bs = new Button[labels.Length];
            for (int i = labels.Length - 1; i >= 0; i--)
            {
                Button b = Dialogs.MakeButton(labels[i], Th, i == def && danger < 0);
                PillButton pb = b as PillButton;
                if (pb != null && i == danger) pb.Danger = true;
                b.DialogResult = results[i];
                b.TabIndex = 100 + i;
                x -= b.Width;
                b.Location = new Point(x, y);
                x -= UI.S(8);
                Controls.Add(b);
                bs[i] = b;
            }
            AcceptButton = bs[def];
            CancelButton = bs[cancel];
            ClientSize = new Size(ClientSize.Width, y + bs[0].Height + pad);
            Shown += delegate { bs[def].Focus(); };
            buttons = bs;
            dangerIndex = danger;
            AccessibleRole = AccessibleRole.Dialog;
            AccessibleDescription = message.Length > 1000 ? message.Substring(0, 1000) : message;
        }

        Button[] buttons;
        int dangerIndex = -1;

        protected override bool ProcessDialogChar(char charCode)
        {
            if (buttons != null && !char.IsControl(charCode) && (Control.ModifierKeys & (Keys.Control | Keys.Alt)) == Keys.Alt)
            {
                char want = char.ToUpperInvariant(charCode);
                int hit = -1, count = 0;
                for (int i = 0; i < buttons.Length; i++)
                {
                    string t = buttons[i].Text ?? "";
                    if (t.Length > 0 && char.ToUpperInvariant(t[0]) == want) { hit = i; count++; }
                }
                if (count == 1 && hit != dangerIndex) { buttons[hit].PerformClick(); return true; }
            }
            return base.ProcessDialogChar(charCode);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                if (icon == MessageBoxIcon.Warning) System.Media.SystemSounds.Exclamation.Play();
                else if (icon == MessageBoxIcon.Error) System.Media.SystemSounds.Hand.Play();
                else if (icon == MessageBoxIcon.Information) System.Media.SystemSounds.Asterisk.Play();
            }
            catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C))
            {
                try { Clipboard.SetText(Text + "\r\n\r\n" + message.Replace("\r\n", "\n").Replace("\n", "\r\n")); } catch { }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintIcon(e.Graphics, Th, icon);
        }

        public static void PaintIcon(Graphics g, Theme th, MessageBoxIcon icon)
        {
            if (icon == MessageBoxIcon.None) return;
            Color c = icon == MessageBoxIcon.Warning ? (th.Dark ? Color.FromArgb(0xFB, 0xBF, 0x24) : Color.FromArgb(0xD9, 0x77, 0x06))
                    : icon == MessageBoxIcon.Error ? th.Error : th.Accent;
            if (th.HighContrast) c = th.Fore;
            int pad = UI.S(24), d = UI.S(36);
            Rectangle r = new Rectangle(pad, pad, d, d);
            Glyphs.Dot(g, r, Theme.Blend(th.Chrome, c, th.Dark ? 0.22 : 0.14), d);
            if (icon == MessageBoxIcon.Error) Glyphs.X(g, r, c, UI.S(6));
            else
            {
                string ch = icon == MessageBoxIcon.Warning ? "!" : icon == MessageBoxIcon.Question ? "?" : "i";
                TextRenderer.DrawText(g, ch, Fonts.Heading, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        static IWin32Window OwnerOf(IWin32Window owner)
        {
            Control c = owner as Control;
            if (c != null && c.IsDisposed) return null;
            if (c != null) return c.FindForm() ?? c;
            return owner;
        }

        public static DialogResult Show(IWin32Window owner, Theme t, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(owner, t, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, Theme t, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton def)
        {
            string[] labels;
            DialogResult[] results;
            int cancel;
            switch (buttons)
            {
                case MessageBoxButtons.OKCancel: labels = new string[] { "OK", "Cancel" }; results = new DialogResult[] { DialogResult.OK, DialogResult.Cancel }; cancel = 1; break;
                case MessageBoxButtons.YesNo: labels = new string[] { "Yes", "No" }; results = new DialogResult[] { DialogResult.Yes, DialogResult.No }; cancel = 1; break;
                case MessageBoxButtons.YesNoCancel: labels = new string[] { "Yes", "No", "Cancel" }; results = new DialogResult[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel }; cancel = 2; break;
                case MessageBoxButtons.RetryCancel: labels = new string[] { "Try again", "Cancel" }; results = new DialogResult[] { DialogResult.Retry, DialogResult.Cancel }; cancel = 1; break;
                default: labels = new string[] { "OK" }; results = new DialogResult[] { DialogResult.OK }; cancel = 0; break;
            }
            int d = def == MessageBoxDefaultButton.Button2 ? 1 : def == MessageBoxDefaultButton.Button3 ? 2 : 0;
            if (d >= labels.Length) d = 0;
            return Run(owner, t, text, caption, icon, labels, results, d, -1, cancel);
        }

        public static DialogResult Choose(IWin32Window owner, Theme t, string text, string caption, MessageBoxIcon icon, string[] labels, DialogResult[] results, int def, int cancel)
        {
            return Run(owner, t, text, caption, icon, labels, results, def, -1, cancel);
        }

        public static DialogResult Choose(IWin32Window owner, Theme t, string text, string caption, MessageBoxIcon icon, string[] labels, DialogResult[] results, int def, int cancel, int danger)
        {
            return Run(owner, t, text, caption, icon, labels, results, def, danger, cancel);
        }

        public static bool Ask(IWin32Window owner, Theme t, string text, string caption, string action)
        {
            DialogResult r = Run(owner, t, text, caption, MessageBoxIcon.Question,
                new string[] { action, "Cancel" }, new DialogResult[] { DialogResult.OK, DialogResult.Cancel }, 1, -1, 1);
            return r == DialogResult.OK;
        }

        public static bool Confirm(IWin32Window owner, Theme t, string text, string caption, string action, bool danger)
        {
            DialogResult r = Run(owner, t, text, caption, danger ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                new string[] { action, "Cancel" }, new DialogResult[] { DialogResult.OK, DialogResult.Cancel }, danger ? 1 : 0, danger ? 0 : -1, 1);
            return r == DialogResult.OK;
        }

        static DialogResult Run(IWin32Window owner, Theme t, string text, string caption, MessageBoxIcon icon, string[] labels, DialogResult[] results, int def, int danger, int cancel)
        {
            if (t == null) t = Theme.Light(Theme.SystemAccent(false));
            using (Alert a = new Alert(t, text, caption, icon, labels, results, def, danger, cancel))
            {
                IWin32Window o = OwnerOf(owner);
                if (o == null) a.StartPosition = FormStartPosition.CenterScreen;
                DialogResult r = o != null ? a.ShowDialog(o) : a.ShowDialog();
                return r == DialogResult.None ? results[cancel] : r;
            }
        }
    }
}
