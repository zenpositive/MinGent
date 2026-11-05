using System;
using System.Drawing;
using System.Windows.Forms;

namespace MinGent
{
    class FlatButton : Control
    {
        public bool Toggle, Checked;
        public string Glyph;
        public bool Lit, Outlined;
        Theme th = Theme.Light(Color.SteelBlue);
        bool hover, down;

        public FlatButton(string text, string glyph, bool toggle)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
            TabStop = false;
            Text = text;
            Glyph = glyph;
            Toggle = toggle;
            Font = UI.Font;
            Cursor = Cursors.Hand;
        }

        public void SetTheme(Theme t) { th = t; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }

        protected override void OnClick(EventArgs e)
        {
            if (Toggle) { Checked = !Checked; Invalidate(); }
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Chrome);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            bool on = Toggle && Checked;
            Color bg = Color.Empty;
            if (on) bg = th.ToggleOn;
            else if (Lit) bg = th.ButtonHover;
            if (down) bg = th.ButtonPressed;
            else if (hover) bg = on ? Theme.Blend(th.ToggleOn, th.ButtonHover, 0.4) : Lit ? th.ButtonPressed : th.ButtonHover;
            int rad = Outlined ? UI.S(8) : UI.S(4);
            if (th.HighContrast && !on)
            {
                if (down || hover) Glyphs.DrawRound(g, SystemColors.Highlight, r, rad);
                else if (Lit || Outlined) Glyphs.DrawRound(g, th.Fore, r, rad);
            }
            else
            {
                if (bg != Color.Empty) Glyphs.FillRound(g, bg, r, rad);
                if (Outlined && !on) Glyphs.DrawRound(g, hover && Enabled ? th.ChromeDim : th.WellBorder, r, rad);
            }
            if (on) Glyphs.DrawRound(g, th.Accent, r, rad);
            Color fg = Enabled ? th.ChromeFore : th.MenuDisabled;
            if (th.HighContrast && on) fg = SystemColors.HighlightText;
            switch (Glyph)
            {
                case "x": Glyphs.X(g, ClientRectangle, fg, UI.S(4)); break;
                case "plus": Glyphs.Plus(g, ClientRectangle, fg, UI.S(5)); break;
                case "left": Glyphs.ChevronH(g, ClientRectangle, fg, UI.S(5), true); break;
                case "right": Glyphs.ChevronH(g, ClientRectangle, fg, UI.S(5), false); break;
                case "more":
                    for (int i = -1; i <= 1; i++)
                        Glyphs.Dot(g, new Rectangle(Width / 2 + i * UI.S(5) - UI.S(2), Height / 2 - UI.S(2), UI.S(4), UI.S(4)), fg, UI.S(3));
                    break;
                case "up": Glyphs.Chevron(g, ClientRectangle, fg, UI.S(5), true); break;
                case "down": Glyphs.Chevron(g, ClientRectangle, fg, UI.S(5), false); break;
                case "word":
                    {
                        Rectangle tr = new Rectangle(0, -UI.S(2), Width, Height);
                        TextRenderer.DrawText(g, "ab", Font, tr, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        int cx = Width / 2, y = Height / 2 + UI.S(6), w = UI.S(8);
                        using (Pen p = new Pen(fg, Math.Max(1f, UI.Scale)))
                            g.DrawLines(p, new Point[] { new Point(cx - w, y - UI.S(3)), new Point(cx - w, y), new Point(cx + w, y), new Point(cx + w, y - UI.S(3)) });
                        break;
                    }
                case "face":
                    {
                        int fs = Math.Min(Width, Height) - UI.S(8);
                        Glyphs.Face(g, new Rectangle((Width - fs) / 2, (Height - fs) / 2, fs, fs), th, Glyphs.FaceIdle, 0, false);
                        break;
                    }
                default:
                    if (Glyph != null && Glyphs.Draw(Glyph, g, ClientRectangle, fg)) break;
                    TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                    break;
            }
        }
    }

    class CueTextBox : TextBox
    {
        public string Cue = "";
        public Color CueColor = SystemColors.GrayText;
        public Theme MenuTheme;
        public Func<bool> CanPasteOther;
        public Action PasteOther;
        bool idleCaret, idleOn = true;
        Timer idleBlink;

        public bool IdleCaret
        {
            get { return idleCaret; }
            set { idleCaret = value; Idle(); }
        }

        public bool IdleCaretShown { get { return IdleShown && idleOn; } }

        bool IdleShown { get { return idleCaret && Multiline && Visible && !Focused && TextLength == 0 && !IsDisposed; } }

        Rectangle CaretSpot
        {
            get
            {
                Point o = Native.EditTextOrigin(this);
                return new Rectangle(o.X, o.Y, Glyphs.CaretWidth, Math.Max(1, Math.Min(ClientSize.Height - o.Y, Font.Height)));
            }
        }

        void Idle()
        {
            idleOn = true;
            int ms = SystemInformation.CaretBlinkTime;
            if (IdleShown && ms > 0)
            {
                if (idleBlink == null)
                {
                    idleBlink = new Timer();
                    idleBlink.Tick += delegate
                    {
                        if (!IdleShown) { idleBlink.Stop(); return; }
                        idleOn = !idleOn;
                        Invalidate(Rectangle.Inflate(CaretSpot, 1, 0));
                    };
                }
                idleBlink.Interval = Math.Max(200, ms);
                idleBlink.Stop();
                idleBlink.Start();
            }
            else if (idleBlink != null) idleBlink.Stop();
            if (idleCaret && IsHandleCreated) Invalidate(Rectangle.Inflate(CaretSpot, 1, 0));
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Idle(); }

        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Idle(); }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); Idle(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && idleBlink != null) { idleBlink.Dispose(); idleBlink = null; }
            base.Dispose(disposing);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (Multiline && TextLength <= 1) Invalidate();
            if (idleCaret && TextLength <= 1) Idle();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;
            if (e.KeyCode == Keys.Back && e.Modifiers == Keys.Control)
            {
                e.Handled = e.SuppressKeyPress = true;
                if (!ReadOnly) DeleteWordBack();
            }
            else if (e.KeyCode == Keys.A && e.Modifiers == Keys.Control)
            {
                e.Handled = e.SuppressKeyPress = true;
                SelectAll();
            }
            else if (e.KeyCode == Keys.Enter && !Multiline) e.SuppressKeyPress = true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)127) { e.Handled = true; return; }
            base.OnKeyPress(e);
        }

        void ShowEditMenu(int lp)
        {
            if (!Focused) Focus();
            Point at;
            if (lp == -1)
            {
                Point c = GetPositionFromCharIndex(Math.Max(0, Math.Min(TextLength - 1, SelectionStart)));
                at = PointToScreen(new Point(Math.Max(0, Math.Min(ClientSize.Width - 1, c.X)), Math.Min(ClientSize.Height - 1, c.Y + Font.Height)));
            }
            else at = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            bool ro = ReadOnly, sel = SelectionLength > 0, secret = UseSystemPasswordChar || PasswordChar != '\0', paste = false;
            bool other = !ro && CanPasteOther != null && PasteOther != null && CanPasteOther();
            try { paste = other || (!ro && Clipboard.ContainsText()); } catch { }
            ContextMenuStrip m = Menus.Create(MenuTheme);
            m.ShowCheckMargin = false;
            Add(m, "Undo", "Ctrl+Z", !ro && CanUndo, delegate { Undo(); });
            m.Items.Add(new ToolStripSeparator());
            Add(m, "Cut", "Ctrl+X", sel && !ro && !secret, delegate { Cut(); });
            Add(m, "Copy", "Ctrl+C", sel && !secret, delegate { Copy(); });
            Add(m, "Paste", "Ctrl+V", paste, delegate { if (other) PasteOther(); else Paste(); });
            Add(m, "Delete", "Del", sel && !ro, delegate { SelectedText = ""; });
            m.Items.Add(new ToolStripSeparator());
            Add(m, "Select all", "Ctrl+A", TextLength > 0, delegate { SelectAll(); });
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(at);
        }

        static void Add(ContextMenuStrip m, string text, string keys, bool on, EventHandler click)
        {
            ToolStripMenuItem mi = Menus.Detail(text, keys, false, click);
            mi.Enabled = on;
            m.Items.Add(mi);
        }

        public void DeleteWordBack()
        {
            if (SelectionLength > 0) { SelectedText = ""; return; }
            int pos = SelectionStart;
            if (pos == 0) return;
            int i = pos;
            if (UseSystemPasswordChar || PasswordChar != '\0') i = 0;
            else
            {
                string t = Text;
                while (i > 0 && char.IsWhiteSpace(t[i - 1])) i--;
                while (i > 0 && !char.IsWhiteSpace(t[i - 1])) i--;
            }
            Select(i, pos - i);
            SelectedText = "";
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x007B && MenuTheme != null && !IsDisposed)
            {
                ShowEditMenu(unchecked((int)(long)m.LParam));
                return;
            }
            if (m.Msg == 0x0302 && !ReadOnly && CanPasteOther != null && PasteOther != null && CanPasteOther())
            {
                PasteOther();
                return;
            }
            bool cue = m.Msg == 0x000F && Multiline && TextLength == 0;
            Rectangle area = cue ? Native.UpdateArea(this) : Rectangle.Empty;
            base.WndProc(ref m);
            if (cue && !area.IsEmpty && (Cue.Length > 0 || IdleCaretShown))
            {
                bool hid = Focused && Native.HideCaretIn(this);
                try
                {
                    using (Graphics g = CreateGraphics())
                    {
                        g.SetClip(area);
                        Rectangle spot = CaretSpot;
                        if (Cue.Length > 0)
                            TextRenderer.DrawText(g, Cue, Font, new Rectangle(spot.X, spot.Y, Math.Max(1, ClientSize.Width - spot.X), Math.Max(1, ClientSize.Height - spot.Y)), CueColor,
                                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
                        if (IdleCaretShown)
                            using (SolidBrush b = new SolidBrush(ForeColor))
                                g.FillRectangle(b, spot);
                    }
                }
                catch { }
                finally { if (hid) Native.ShowCaretIn(this); }
            }
        }
    }

    class InputBox : Control, IThemed
    {
        public readonly TextBox Box = new CueTextBox();
        readonly string cue;
        Theme th = Theme.Light(Color.SteelBlue);
        public bool Field, Pill;
        public string Glyph;
        bool hover;

        public InputBox(string cue)
        {
            this.cue = cue;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.IBeam;
            Box.BorderStyle = BorderStyle.None;
            Box.Font = UI.Font;
            Controls.Add(Box);
            ((CueTextBox)Box).Cue = cue ?? "";
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Box.MultilineChanged += delegate { PerformLayout(); };
            Box.MouseEnter += delegate { hover = true; Invalidate(); };
            Box.MouseLeave += delegate { hover = ClientRectangle.Contains(PointToClient(Control.MousePosition)); Invalidate(); };
            Box.HandleCreated += delegate { if (!string.IsNullOrEmpty(this.cue) && !Box.Multiline) Native.SetCueBanner(Box, this.cue); };
        }

        public void SetTheme(Theme t)
        {
            th = t;
            Box.BackColor = Field ? t.Well : t.InputBack;
            Box.ForeColor = t.Fore;
            ((CueTextBox)Box).CueColor = t.ChromeDim;
            ((CueTextBox)Box).MenuTheme = t;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Box.Multiline)
            {
                int px = UI.S(10), py = UI.S(8);
                Box.SetBounds(px, py, Math.Max(10, Width - px * 2), Math.Max(10, Height - py * 2));
                return;
            }
            int h = Box.PreferredHeight;
            int sx = Pill ? UI.S(14) : Field ? UI.S(11) : UI.S(8);
            int gx = Glyph != null ? UI.S(24) : 0;
            Box.SetBounds(sx + gx, Math.Max(0, (Height - h) / 2), Math.Max(10, Width - sx * 2 - gx), h);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = Box.ClientRectangle.Contains(Box.PointToClient(Control.MousePosition)); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Box.Focus();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Box.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Chrome);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (Field)
            {
                int fr = Pill ? Height / 2 : UI.S(8);
                Glyphs.FillRound(g, th.Well, r, fr);
                if (Box.Focused) Glyphs.Ring(g, th.Accent, new Rectangle(0, 0, Width, Height), fr, Glyphs.FocusWidth);
                else Glyphs.DrawRound(g, hover && Enabled ? th.ChromeDim : th.WellBorder, r, fr);
                if (Glyph != null) Glyphs.Draw(Glyph, g, new Rectangle(UI.S(11), (Height - UI.S(16)) / 2, UI.S(16), UI.S(16)), th.ChromeDim);
                return;
            }
            int radius = Box.Multiline ? UI.S(10) : UI.S(4);
            Glyphs.FillRound(g, th.InputBack, r, radius);
            Glyphs.DrawRound(g, Box.Focused && Box.Multiline ? th.Accent : th.InputBorder, r, radius);
            if (Box.Multiline) return;
            if (Box.Focused)
            {
                int lw = Math.Max(2, UI.S(2));
                using (SolidBrush b = new SolidBrush(th.Accent))
                    g.FillRectangle(b, UI.S(3), Height - lw, Width - UI.S(6), lw);
            }
        }
    }

    class FindBar : Control
    {
        public readonly InputBox FindIn = new InputBox("Find");
        public readonly InputBox ReplIn = new InputBox("Replace with");
        readonly FlatButton bCase = new FlatButton("Aa", null, true);
        readonly FlatButton bWord = new FlatButton("", "word", true);
        readonly FlatButton bPrev = new FlatButton("", "up", false);
        readonly FlatButton bNext = new FlatButton("", "down", false);
        readonly FlatButton bClose = new FlatButton("", "x", false);
        readonly FlatButton bRepl = new FlatButton("Replace", null, false);
        readonly FlatButton bReplAll = new FlatButton("Replace all", null, false);
        readonly ThemedTip tip = new ThemedTip();
        Theme th = Theme.Light(Color.SteelBlue);
        bool replaceMode, suppress;
        string info = "";
        bool infoErr;
        int infoX;

        public event EventHandler TermChanged, OptionsChanged, CloseRequested, ReplaceRequested, ReplaceAllRequested;
        public event Action<bool> FindRequested;

        public FindBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = UI.Font;
            Controls.AddRange(new Control[] { FindIn, bCase, bWord, bPrev, bNext, bClose, ReplIn, bRepl, bReplAll });
            FindIn.TabIndex = 0;
            ReplIn.TabIndex = 1;
            FindIn.Field = ReplIn.Field = true;
            bRepl.Outlined = bReplAll.Outlined = true;

            tip.SetToolTip(bCase, "Match case (Alt+C)");
            tip.SetToolTip(bWord, "Whole word (Alt+W)");
            tip.SetToolTip(bPrev, "Previous match (Shift+Enter)");
            tip.SetToolTip(bNext, "Next match (Enter)");
            tip.SetToolTip(bClose, "Close (Esc)");
            tip.SetToolTip(bRepl, "Replace next (Enter in the replace box)");
            tip.SetToolTip(bReplAll, "Replace all (Ctrl+Alt+Enter)");

            FindIn.Box.TextChanged += delegate { if (!suppress && TermChanged != null) TermChanged(this, EventArgs.Empty); };
            FindIn.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    if (FindRequested != null) FindRequested(e.Shift);
                    e.Handled = e.SuppressKeyPress = true;
                }
            };
            ReplIn.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    if (e.Control && e.Alt) { if (ReplaceAllRequested != null) ReplaceAllRequested(this, EventArgs.Empty); }
                    else if (ReplaceRequested != null) ReplaceRequested(this, EventArgs.Empty);
                    e.Handled = e.SuppressKeyPress = true;
                }
            };
            bCase.Click += delegate { RaiseOptions(); };
            bWord.Click += delegate { RaiseOptions(); };
            bPrev.Click += delegate { if (FindRequested != null) FindRequested(true); };
            bNext.Click += delegate { if (FindRequested != null) FindRequested(false); };
            bClose.Click += delegate { if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); };
            bRepl.Click += delegate { if (ReplaceRequested != null) ReplaceRequested(this, EventArgs.Empty); };
            bReplAll.Click += delegate { if (ReplaceAllRequested != null) ReplaceAllRequested(this, EventArgs.Empty); };
            ReplaceMode = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        void RaiseOptions() { if (OptionsChanged != null) OptionsChanged(this, EventArgs.Empty); }

        public string Term { get { return FindIn.Box.Text; } }
        public string Replacement { get { return ReplIn.Box.Text; } }

        public void SetTerm(string s)
        {
            suppress = true;
            FindIn.Box.Text = s;
            suppress = false;
        }

        public bool MatchCase { get { return bCase.Checked; } set { bCase.Checked = value; bCase.Invalidate(); } }
        public bool WholeWord { get { return bWord.Checked; } set { bWord.Checked = value; bWord.Invalidate(); } }

        public bool ReplaceMode
        {
            get { return replaceMode; }
            set
            {
                replaceMode = value;
                ReplIn.Visible = bRepl.Visible = bReplAll.Visible = value;
                Height = value ? UI.S(76) : UI.S(42);
                PerformLayout();
                Invalidate();
            }
        }

        public void SetInfo(string text, bool error)
        {
            if (text == info && error == infoErr) return;
            info = text;
            infoErr = error;
            Invalidate(new Rectangle(infoX, 0, Math.Max(1, bClose.Left - infoX), UI.S(42)));
        }

        public void FocusFind()
        {
            FindIn.Box.Focus();
            FindIn.Box.SelectAll();
        }

        public void SetTheme(Theme t)
        {
            th = t;
            tip.Th = t;
            BackColor = t.Chrome;
            FindIn.SetTheme(t);
            ReplIn.SetTheme(t);
            foreach (Control c in Controls)
            {
                FlatButton fb = c as FlatButton;
                if (fb != null) fb.SetTheme(t);
            }
            Invalidate(true);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.C)) { MatchCase = !MatchCase; RaiseOptions(); return true; }
            if (keyData == (Keys.Alt | Keys.W)) { WholeWord = !WholeWord; RaiseOptions(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = UI.S(8), h = UI.S(28), gap = UI.S(4), bw = UI.S(30), y = UI.S(7);
            int boxW = Math.Max(UI.S(140), Math.Min(UI.S(340), Width - UI.S(400)));
            int x = pad;
            FindIn.SetBounds(x, y, boxW, h); x += boxW + gap;
            bCase.SetBounds(x, y, bw, h); x += bw + UI.S(2);
            bWord.SetBounds(x, y, bw, h); x += bw + gap * 2;
            bPrev.SetBounds(x, y, bw, h); x += bw + UI.S(2);
            bNext.SetBounds(x, y, bw, h); x += bw + gap * 3;
            infoX = x;
            bClose.SetBounds(Width - pad - bw, y, bw, h);
            if (replaceMode)
            {
                int y2 = y + h + UI.S(6);
                ReplIn.SetBounds(pad, y2, boxW, h);
                int rx = pad + boxW + gap;
                int w1 = TextRenderer.MeasureText("Replace", Font).Width + UI.S(24);
                int w2 = TextRenderer.MeasureText("Replace all", Font).Width + UI.S(24);
                bRepl.SetBounds(rx, y2, w1, h);
                bReplAll.SetBounds(rx + w1 + UI.S(6), y2, w2, h);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(th.Chrome)) g.FillRectangle(b, ClientRectangle);
            using (Pen p = new Pen(th.Border)) g.DrawLine(p, 0, 0, Width, 0);
            if (info.Length > 0)
            {
                Rectangle r = new Rectangle(infoX, UI.S(7), Math.Max(0, bClose.Left - infoX - UI.S(8)), UI.S(28));
                TextRenderer.DrawText(g, info, Font, r, infoErr ? th.Error : th.ChromeDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }
    }

    class GoToDialog : ThemedForm
    {
        readonly InputBox input;
        readonly int max;
        public int Line;

        public GoToDialog(Theme t, int current, int max) : base(t, "Go to line", 340, 164)
        {
            this.max = max;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Label lbl = Dialogs.MakeLabel("Line number (1-" + max + ")", Th, false);
            lbl.ForeColor = Th.Fore;
            lbl.SetBounds(UI.S(22), UI.S(18), ClientSize.Width - UI.S(44), UI.S(22));
            input = MakeField(current.ToString(), false, false);
            input.SetBounds(UI.S(20), UI.S(46), ClientSize.Width - UI.S(40), UI.S(34));
            Button ok = Dialogs.MakeButton("Go", Th, true);
            Button cancel = Dialogs.MakeButton("Cancel", Th, false);
            cancel.Location = new Point(ClientSize.Width - UI.S(20) - cancel.Width, ClientSize.Height - UI.S(18) - cancel.Height);
            ok.Location = new Point(cancel.Left - UI.S(8) - ok.Width, cancel.Top);
            ok.Click += delegate { Accept(); };
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { lbl, input, ok, cancel });
            Shown += delegate { input.Box.Focus(); input.Box.SelectAll(); };
        }

        void Accept()
        {
            int n;
            if (int.TryParse(input.Box.Text.Trim(), out n) && n >= 1)
            {
                Line = Math.Min(n, max);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                input.Box.Focus();
                input.Box.SelectAll();
            }
        }
    }
}
