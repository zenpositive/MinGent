using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace MinGent
{
    class ConsoleView : Control
    {
        class Row
        {
            public ConsoleEntry E;
            public List<string> Lines;
            public int Y, H;
        }

        ConsoleLog log;
        Theme th = Theme.Light(Color.SteelBlue);
        readonly Font mono;
        readonly List<Row> rows = new List<Row>();
        readonly HashSet<ConsoleEntry> picked = new HashSet<ConsoleEntry>();
        readonly WheelAcc wheel = new WheelAcc();
        readonly Timer settle = new Timer();
        readonly Dictionary<string, int> widths = new Dictionary<string, int>(StringComparer.Ordinal);
        int scroll, total, charW = 7, lineH = 15, laidW = -1, anchor = -1, dragOff;
        bool stick = true, dirty = true, sbDrag, sbHover;
        volatile bool queued;
        public string EmptyText = "Nothing logged yet.";

        public ConsoleView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Font = UI.Font;
            mono = MonoFont(UI.Font.Size * 0.95f);
            Size cell = TextRenderer.MeasureText("MMMMMMMMMM", mono, new Size(4000, 200), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            charW = Math.Max(4, cell.Width / 10);
            lineH = Math.Max(mono.Height, cell.Height) + UI.S(2);
            settle.Interval = 90;
            settle.Tick += delegate
            {
                settle.Stop();
                queued = false;
                dirty = true;
                Invalidate();
            };
        }

        public static Font MonoFont(float size)
        {
            try
            {
                Font f = new Font("Consolas", size);
                if (f.Name == "Consolas") return f;
                f.Dispose();
            }
            catch { }
            return new Font(FontFamily.GenericMonospace, size);
        }

        public ConsoleLog Log
        {
            get { return log; }
            set
            {
                if (log != null) log.Changed -= OnChanged;
                log = value;
                if (log != null) log.Changed += OnChanged;
                picked.Clear();
                dirty = true;
                Invalidate();
            }
        }

        public void SetTheme(Theme t) { th = t; BackColor = t.Back; Invalidate(); }

        public static Color Warn(Theme t)
        {
            if (t.HighContrast) return t.Fore;
            return t.Dark ? Color.FromArgb(0xE2, 0xB1, 0x4A) : Color.FromArgb(0x95, 0x63, 0x00);
        }

        void OnChanged()
        {
            if (queued || IsDisposed || !IsHandleCreated) return;
            queued = true;
            try { BeginInvoke(new Action(delegate { if (!settle.Enabled) settle.Start(); })); }
            catch { queued = false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (log != null) log.Changed -= OnChanged;
                settle.Dispose();
                mono.Dispose();
            }
            base.Dispose(disposing);
        }

        int TextX { get { return UI.S(30); } }
        int SbW { get { return UI.S(10); } }
        int Pad { get { return UI.S(3); } }

        int Cols(int width) { return Math.Max(8, (width - TextX - UI.S(12) - SbW) / charW); }

        static List<string> Wrap(string text, int first, int cols, int max)
        {
            List<string> l = new List<string>();
            int extra = 0;
            string[] parts = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Split('\n');
            foreach (string part in parts)
            {
                string s = part;
                while (s != null)
                {
                    int w = l.Count == 0 ? first : cols;
                    string piece;
                    if (s.Length <= w) { piece = s; s = null; }
                    else
                    {
                        int cut = s.LastIndexOf(' ', w);
                        if (cut < w / 2) cut = w;
                        piece = s.Substring(0, cut);
                        s = s.Substring(cut).TrimStart(' ');
                        if (s.Length == 0) s = null;
                    }
                    if (l.Count < max) l.Add(piece);
                    else extra++;
                }
            }
            if (extra > 0) l.Add("\u2026 " + extra.ToString(CultureInfo.InvariantCulture) + " more lines (right-click to copy all)");
            return l;
        }

        int Measure(string s)
        {
            int w;
            if (widths.TryGetValue(s, out w)) return w;
            if (widths.Count > 2000) widths.Clear();
            w = Fonts.Width(s, Fonts.Small);
            widths[s] = w;
            return w;
        }

        int SideWidth(ConsoleEntry e)
        {
            int w = 0;
            if (e.Agent && e.Kind == "input") w = Measure("agent") + UI.S(10);
            else if (e.Where.Length > 0) w = Measure(e.Where) + UI.S(10);
            if (e.Count > 1) w += Measure(e.Count.ToString(CultureInfo.InvariantCulture)) + UI.S(18);
            return w;
        }

        void LayoutRows(int width)
        {
            rows.Clear();
            total = 0;
            if (log == null) return;
            int cols = Cols(width);
            foreach (ConsoleEntry e in log.Entries())
            {
                Row r = new Row();
                r.E = e;
                if (e.Kind == "page")
                {
                    r.Lines = new List<string>(new string[] { "Navigated to " + e.Text });
                    r.H = Fonts.Small.Height + UI.S(10);
                }
                else
                {
                    int side = SideWidth(e);
                    int first = Math.Max(8, cols - (side + charW - 1) / charW);
                    r.Lines = Wrap(e.Text.Length == 0 ? " " : e.Text, first, cols, 40);
                    r.H = r.Lines.Count * lineH + Pad * 2;
                }
                r.Y = total;
                total += r.H;
                rows.Add(r);
            }
            HashSet<ConsoleEntry> still = new HashSet<ConsoleEntry>();
            foreach (Row r in rows) if (picked.Contains(r.E)) still.Add(r.E);
            picked.Clear();
            foreach (ConsoleEntry e in still) picked.Add(e);
            laidW = width;
            dirty = false;
        }

        int MaxScroll { get { return Math.Max(0, total - Height); } }

        void Ensure()
        {
            if (!dirty && laidW == Width) return;
            LayoutRows(Width);
            if (stick) scroll = MaxScroll;
            scroll = Math.Max(0, Math.Min(scroll, MaxScroll));
        }

        Rectangle Track() { return new Rectangle(Width - SbW, 0, SbW, Height); }

        Rectangle Thumb()
        {
            if (total <= Height || Height <= 0) return Rectangle.Empty;
            int len = Math.Max(UI.S(24), (int)((long)Height * Height / total));
            int y = (int)((long)(Height - len) * scroll / Math.Max(1, MaxScroll));
            return new Rectangle(Width - SbW, y, SbW, len);
        }

        public void ScrollToEnd()
        {
            stick = true;
            dirty = true;
            Invalidate();
        }

        void SetScroll(int v)
        {
            Ensure();
            int s = Math.Max(0, Math.Min(v, MaxScroll));
            stick = s >= MaxScroll - 2;
            if (s == scroll) return;
            scroll = s;
            Invalidate();
        }

        Color TextColor(ConsoleEntry e)
        {
            switch (e.Kind)
            {
                case "error": case "fault": return th.HighContrast ? th.Fore : th.Dark ? Theme.Blend(th.Error, Color.White, 0.3) : th.Error;
                case "warn": return Warn(th);
                case "debug": case "note": case "refused": case "page": return th.ChromeDim;
            }
            return th.Fore;
        }

        Color Band(ConsoleEntry e)
        {
            if (th.HighContrast) return Color.Empty;
            if (e.Kind == "error" || e.Kind == "fault") return Theme.Blend(th.Back, th.Error, th.Dark ? 0.14 : 0.07);
            if (e.Kind == "warn") return Theme.Blend(th.Back, Warn(th), th.Dark ? 0.12 : 0.09);
            return Color.Empty;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(th.Back);
            Ensure();
            if (rows.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Fonts.Small, new Rectangle(TextX, UI.S(8), Math.Max(10, Width - TextX - UI.S(12)), UI.S(40)), th.ChromeDim, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                return;
            }
            int right = Width - (Thumb().IsEmpty ? 0 : SbW) - UI.S(10);
            Color rule = Theme.Blend(th.Back, th.ChromeDim, th.Dark ? 0.18 : 0.14);
            Color pick = Theme.Blend(th.Back, th.Accent, th.Dark ? 0.24 : 0.14);
            using (Pen rp = new Pen(rule))
            {
                foreach (Row r in rows)
                {
                    int y = r.Y - scroll;
                    if (y + r.H < e.ClipRectangle.Top || y > e.ClipRectangle.Bottom) continue;
                    ConsoleEntry en = r.E;
                    Color band = picked.Contains(en) ? pick : Band(en);
                    if (band != Color.Empty) using (SolidBrush b = new SolidBrush(band)) g.FillRectangle(b, 0, y, Width, r.H);
                    if (r.Y > 0) g.DrawLine(rp, 0, y, Width, y);
                    if (en.Kind == "page")
                    {
                        TextRenderer.DrawText(g, r.Lines[0], Fonts.Small, new Rectangle(TextX, y, Math.Max(10, right - TextX), r.H), th.ChromeDim,
                            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);
                        continue;
                    }
                    Color fg = TextColor(en);
                    Rectangle icon = new Rectangle(UI.S(9), y + Pad + (lineH - UI.S(14)) / 2, UI.S(14), UI.S(14));
                    switch (en.Kind)
                    {
                        case "error": case "fault": Glyphs.Draw("cross", g, icon, fg); break;
                        case "warn": Glyphs.Draw("warning", g, icon, fg); break;
                        case "refused": Glyphs.Draw("shield", g, icon, th.ChromeDim); break;
                        case "input": case "result":
                            TextRenderer.DrawText(g, en.Kind == "input" ? "\u203a" : "\u2039", mono, new Rectangle(icon.X, y + Pad, icon.Width, lineH),
                                en.Kind == "input" ? (en.Agent ? th.Accent : th.ChromeFore) : th.ChromeDim, TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
                            break;
                    }
                    int ly = y + Pad;
                    foreach (string line in r.Lines)
                    {
                        if (ly + lineH >= e.ClipRectangle.Top && ly <= e.ClipRectangle.Bottom)
                            TextRenderer.DrawText(g, line, mono, new Point(TextX, ly), en.Kind == "result" ? th.ChromeFore : fg, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                        ly += lineH;
                    }
                    int sx = right;
                    string side = en.Agent && en.Kind == "input" ? "agent" : en.Where;
                    if (side.Length > 0)
                    {
                        int sw = Fonts.Width(side, Fonts.Small);
                        sx -= sw;
                        TextRenderer.DrawText(g, side, Fonts.Small, new Rectangle(sx, y + Pad, sw + UI.S(2), lineH), en.Agent && en.Kind == "input" ? th.Accent : th.ChromeDim,
                            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        sx -= UI.S(10);
                    }
                    if (en.Count > 1)
                    {
                        string c = en.Count.ToString(CultureInfo.InvariantCulture);
                        int cw = Fonts.Width(c, Fonts.Small) + UI.S(12);
                        Rectangle pill = new Rectangle(sx - cw, y + Pad + (lineH - UI.S(16)) / 2, cw, UI.S(16));
                        Glyphs.FillRound(g, Theme.Blend(th.Back, fg, th.Dark ? 0.22 : 0.14), pill, UI.S(8));
                        TextRenderer.DrawText(g, c, Fonts.Small, pill, fg, TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                }
            }
            Rectangle tr = Thumb();
            if (!tr.IsEmpty)
            {
                int inset = sbHover || sbDrag ? UI.S(2) : UI.S(3);
                Glyphs.FillRound(g, sbHover || sbDrag ? th.ThumbHover : th.Thumb, new Rectangle(tr.X + inset, tr.Y + 2, tr.Width - inset * 2, tr.Height - 4), UI.S(4));
            }
        }

        int RowAt(int y)
        {
            Ensure();
            int at = y + scroll;
            for (int i = 0; i < rows.Count; i++) if (at >= rows[i].Y && at < rows[i].Y + rows[i].H) return i;
            return -1;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int n = wheel.Take(e.Delta, SystemInformation.MouseWheelScrollLines);
            if (n != 0) SetScroll(scroll - n * lineH);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            Rectangle t = Thumb();
            if (e.Button == MouseButtons.Left && !t.IsEmpty && Track().Contains(e.Location))
            {
                if (t.Contains(e.Location)) { sbDrag = true; dragOff = e.Y - t.Y; Capture = true; }
                else SetScroll(scroll + (e.Y < t.Y ? -Height : Height));
                return;
            }
            int i = RowAt(e.Y);
            if (e.Button == MouseButtons.Right)
            {
                if (i >= 0 && !picked.Contains(rows[i].E)) { picked.Clear(); picked.Add(rows[i].E); anchor = i; Invalidate(); }
                ShowMenu(e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (i < 0) { picked.Clear(); anchor = -1; Invalidate(); return; }
            if ((ModifierKeys & Keys.Shift) != 0 && anchor >= 0 && anchor < rows.Count)
            {
                picked.Clear();
                for (int k = Math.Min(anchor, i); k <= Math.Max(anchor, i); k++) picked.Add(rows[k].E);
            }
            else if ((ModifierKeys & Keys.Control) != 0)
            {
                if (!picked.Remove(rows[i].E)) picked.Add(rows[i].E);
                anchor = i;
            }
            else
            {
                picked.Clear();
                picked.Add(rows[i].E);
                anchor = i;
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (sbDrag)
            {
                Rectangle t = Thumb();
                int room = Math.Max(1, Height - t.Height);
                SetScroll((int)((long)(e.Y - dragOff) * MaxScroll / room));
                return;
            }
            bool over = !Thumb().IsEmpty && Track().Contains(e.Location);
            if (over != sbHover) { sbHover = over; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (sbDrag) { sbDrag = false; Capture = false; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (sbHover && !sbDrag) { sbHover = false; Invalidate(); }
        }

        protected override bool IsInputKey(Keys k)
        {
            Keys c = k & Keys.KeyCode;
            return c == Keys.Up || c == Keys.Down || c == Keys.PageUp || c == Keys.PageDown || c == Keys.Home || c == Keys.End || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.C) { Copy(false); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.A) { Ensure(); picked.Clear(); foreach (Row r in rows) picked.Add(r.E); Invalidate(); e.Handled = true; return; }
            switch (e.KeyCode)
            {
                case Keys.PageUp: SetScroll(scroll - Height + lineH); break;
                case Keys.PageDown: SetScroll(scroll + Height - lineH); break;
                case Keys.Home: if (e.Control) SetScroll(0); break;
                case Keys.End: if (e.Control) SetScroll(MaxScroll); break;
                case Keys.Up: SetScroll(scroll - lineH); break;
                case Keys.Down: SetScroll(scroll + lineH); break;
                default: return;
            }
            e.Handled = true;
        }

        void Copy(bool all)
        {
            Ensure();
            StringBuilder sb = new StringBuilder();
            foreach (Row r in rows)
            {
                if (!all && !picked.Contains(r.E)) continue;
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(r.E.Kind == "page" ? "Navigated to " + r.E.Text : r.E.Plain().Replace("\n", "\r\n"));
            }
            if (sb.Length == 0) return;
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }

        public event EventHandler ClearRequested;

        void ShowMenu(Point at)
        {
            ContextMenuStrip m = Menus.Create(th);
            m.ShowCheckMargin = false;
            ToolStripMenuItem copy = Menus.Detail("Copy", "Ctrl+C", false, delegate { Copy(false); });
            copy.Enabled = picked.Count > 0;
            m.Items.Add(copy);
            ToolStripMenuItem all = Menus.Item("Copy all", false, delegate { Copy(true); });
            all.Enabled = rows.Count > 0;
            m.Items.Add(all);
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem clear = Menus.Item("Clear the console", false, delegate { if (ClearRequested != null) ClearRequested(this, EventArgs.Empty); });
            clear.Enabled = rows.Count > 0;
            m.Items.Add(clear);
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(this, at);
        }
    }

    class ConsoleDrawer : Control
    {
        public readonly ConsoleView View = new ConsoleView();
        readonly CueTextBox input = new CueTextBox();
        readonly FlatButton bClear = new FlatButton("", "trash", false);
        readonly FlatButton bClose = new FlatButton("", "x", false);
        readonly ThemedTip tip = new ThemedTip();
        readonly List<string> history = new List<string>();
        readonly Font mono;
        Theme th = Theme.Light(Color.SteelBlue);
        int histPos = -1, resizeY = -1, resizeFrom;
        string draft = "";
        bool linkHot, gripHot;
        Rectangle link = Rectangle.Empty;
        string notice;
        int errors, warnings;

        public event Action<string> RunRequested;
        public event EventHandler CloseRequested, ClearRequested, NoticeClicked;
        public event Action<int> HeightDragged;

        public ConsoleDrawer()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = UI.Font;
            mono = ConsoleView.MonoFont(UI.Font.Size * 0.95f);
            input.BorderStyle = BorderStyle.None;
            input.Multiline = true;
            input.AcceptsReturn = true;
            input.WordWrap = true;
            input.ScrollBars = ScrollBars.None;
            input.Font = mono;
            input.Cue = "Run JavaScript in this page";
            input.KeyDown += OnInputKey;
            input.TextChanged += delegate { PerformLayout(); };
            bClear.Click += delegate { if (ClearRequested != null) ClearRequested(this, EventArgs.Empty); };
            bClose.Click += delegate { if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); };
            View.ClearRequested += delegate { if (ClearRequested != null) ClearRequested(this, EventArgs.Empty); };
            tip.SetToolTip(bClear, "Clear the console");
            tip.SetToolTip(bClose, "Close the console (Ctrl+Shift+J)");
            Controls.AddRange(new Control[] { View, input, bClear, bClose });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tip.Dispose(); mono.Dispose(); }
            base.Dispose(disposing);
        }

        public void SetTheme(Theme t)
        {
            th = t;
            tip.Th = t;
            BackColor = t.Chrome;
            View.SetTheme(t);
            input.BackColor = t.Back;
            input.ForeColor = t.Fore;
            input.CueColor = t.ChromeDim;
            input.MenuTheme = t;
            bClear.SetTheme(t);
            bClose.SetTheme(t);
            Invalidate();
        }

        public void SetCounts(int errors, int warnings)
        {
            if (this.errors == errors && this.warnings == warnings) return;
            this.errors = errors;
            this.warnings = warnings;
            Invalidate(new Rectangle(0, 0, Width, HeaderH));
        }

        public string Notice
        {
            get { return notice; }
            set { if (notice == value) return; notice = value; Invalidate(new Rectangle(0, 0, Width, HeaderH)); }
        }

        public void FocusInput() { try { input.Focus(); } catch { } }

        int HeaderH { get { return UI.S(32); } }
        int Grip { get { return UI.S(5); } }

        int InputH
        {
            get
            {
                int lines = Math.Max(1, Math.Min(6, Native.LineCount(input)));
                if (lines <= 1) lines = Math.Max(1, Math.Min(6, input.Text.Split('\n').Length));
                return Math.Max(UI.S(32), lines * input.Font.Height + UI.S(14));
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int bw = UI.S(28), by = (HeaderH - bw) / 2;
            bClose.SetBounds(Width - UI.S(6) - bw, by, bw, bw);
            bClear.SetBounds(bClose.Left - UI.S(2) - bw, by, bw, bw);
            int ih = InputH;
            View.SetBounds(0, HeaderH, Width, Math.Max(0, Height - HeaderH - ih));
            input.SetBounds(UI.S(30), Height - ih + UI.S(7), Math.Max(UI.S(40), Width - UI.S(40)), Math.Max(10, ih - UI.S(10)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(th.Back);
            Rectangle head = new Rectangle(0, 0, Width, HeaderH);
            using (SolidBrush b = new SolidBrush(th.Chrome)) g.FillRectangle(b, head);
            using (Pen p = new Pen(gripHot || resizeY >= 0 ? th.Accent : th.Border)) g.DrawLine(p, 0, 0, Width, 0);
            using (Pen p = new Pen(th.Border)) g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);
            int x = UI.S(12);
            int tw = Fonts.Width("Console", Fonts.SmallSemi);
            TextRenderer.DrawText(g, "Console", Fonts.SmallSemi, new Rectangle(x, 0, tw + UI.S(4), HeaderH), th.ChromeFore, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += tw + UI.S(14);
            int limit = bClear.Left - UI.S(8);
            if (errors > 0) x = Count(g, x, limit, errors == 1 ? "1 error" : errors.ToString(CultureInfo.InvariantCulture) + " errors", th.HighContrast ? th.Fore : th.Error);
            if (warnings > 0) x = Count(g, x, limit, warnings == 1 ? "1 warning" : warnings.ToString(CultureInfo.InvariantCulture) + " warnings", ConsoleView.Warn(th));
            link = Rectangle.Empty;
            if (!string.IsNullOrEmpty(notice) && x < limit)
            {
                string act = "Read it";
                int aw = Fonts.Width(act, Fonts.Small);
                int nw = Math.Min(Fonts.Width(notice, Fonts.Small), Math.Max(0, limit - x - aw - UI.S(10)));
                TextRenderer.DrawText(g, notice, Fonts.Small, new Rectangle(x, 0, nw, HeaderH), th.ChromeDim, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                x += nw + UI.S(8);
                if (x + aw <= limit)
                {
                    link = new Rectangle(x, (HeaderH - Fonts.Small.Height) / 2, aw, Fonts.Small.Height);
                    using (Font u = linkHot ? new Font(Fonts.Small, FontStyle.Underline) : null)
                        TextRenderer.DrawText(g, act, u ?? Fonts.Small, link, th.Accent, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
            int iy = Height - InputH;
            using (Pen p = new Pen(Theme.Blend(th.Back, th.ChromeDim, th.Dark ? 0.25 : 0.2))) g.DrawLine(p, 0, iy, Width, iy);
            TextRenderer.DrawText(g, "\u203a", mono, new Rectangle(UI.S(9), iy + UI.S(7), UI.S(14), input.Font.Height), input.Focused ? th.Accent : th.ChromeDim,
                TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        }

        int Count(Graphics g, int x, int limit, string text, Color c)
        {
            int w = Fonts.Width(text, Fonts.Small);
            if (x + w > limit) return x;
            TextRenderer.DrawText(g, text, Fonts.Small, new Rectangle(x, 0, w + UI.S(2), HeaderH), c, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return x + w + UI.S(12);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (resizeY >= 0)
            {
                if (HeightDragged != null) HeightDragged(resizeFrom - (PointToScreen(e.Location).Y - resizeY));
                return;
            }
            bool grip = e.Y < Grip;
            bool hot = !link.IsEmpty && link.Contains(e.Location);
            Cursor = grip ? Cursors.HSplit : hot ? Cursors.Hand : Cursors.Default;
            if (grip != gripHot || hot != linkHot) { gripHot = grip; linkHot = hot; Invalidate(new Rectangle(0, 0, Width, HeaderH)); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (resizeY < 0 && (gripHot || linkHot)) { gripHot = linkHot = false; Invalidate(new Rectangle(0, 0, Width, HeaderH)); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (e.Y < Grip) { resizeY = PointToScreen(e.Location).Y; resizeFrom = Height; Capture = true; return; }
            if (!link.IsEmpty && link.Contains(e.Location) && NoticeClicked != null) { NoticeClicked(this, EventArgs.Empty); return; }
            if (e.Y >= Height - InputH) input.Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (resizeY < 0) return;
            resizeY = -1;
            Capture = false;
            Invalidate(new Rectangle(0, 0, Width, HeaderH));
        }

        void OnInputKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.Handled = e.SuppressKeyPress = true;
                string code = input.Text.Trim();
                if (code.Length == 0) return;
                if (history.Count == 0 || history[history.Count - 1] != code) history.Add(code);
                if (history.Count > 100) history.RemoveAt(0);
                histPos = -1;
                draft = "";
                input.Text = "";
                View.ScrollToEnd();
                if (RunRequested != null) RunRequested(code);
                return;
            }
            if (e.KeyCode == Keys.Escape && input.TextLength > 0) { e.Handled = e.SuppressKeyPress = true; input.Text = ""; histPos = -1; return; }
            string text = input.Text;
            int caret = Math.Max(0, Math.Min(text.Length, input.SelectionStart));
            bool up = e.KeyCode == Keys.Up && !e.Shift && (caret == 0 || text.LastIndexOf('\n', caret - 1) < 0);
            bool down = e.KeyCode == Keys.Down && !e.Shift && text.IndexOf('\n', caret) < 0;
            if ((up || down) && history.Count > 0)
            {
                if (up)
                {
                    if (histPos < 0) { draft = input.Text; histPos = history.Count; }
                    if (histPos == 0) return;
                    histPos--;
                }
                else
                {
                    if (histPos < 0) return;
                    histPos++;
                }
                e.Handled = e.SuppressKeyPress = true;
                string t = histPos >= 0 && histPos < history.Count ? history[histPos] : draft;
                if (histPos >= history.Count) histPos = -1;
                input.Text = t;
                input.SelectionStart = input.TextLength;
            }
        }
    }
}
