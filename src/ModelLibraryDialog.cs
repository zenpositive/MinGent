using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MinGent
{
    class RowItem
    {
        public string Title = "", Sub = "";
        public List<KeyValuePair<string, int>> Badges = new List<KeyValuePair<string, int>>();
        public List<string> Traits = new List<string>();
        public List<string> Buttons = new List<string>();
        public object Tag;
        public int Progress = -1;
        public string ProgressText = "";
        public bool Selected;
    }

    class RowList : Control
    {
        public readonly List<RowItem> Items = new List<RowItem>();
        public string EmptyText = "";
        public int RowHeight = 58;
        public bool QuietDownloads;
        Theme th = Theme.Light(Color.SteelBlue);
        int scroll, hoverRow = -1, hoverBtn = -1, downRow = -1, downBtn = -1, dragOff;
        bool sbDrag, sbHover;
        readonly ToolTip tip = new ToolTip();
        readonly List<KeyValuePair<Rectangle, string>> tipSpots = new List<KeyValuePair<Rectangle, string>>();
        string tipShown;
        public event Action<RowItem, string> ButtonClicked;
        public event Action<RowItem> RowClicked;

        public RowList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Font = UI.Font;
        }

        public void SetTheme(Theme t) { th = t; BackColor = t.Back; Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        public static string TraitName(string trait)
        {
            switch (trait)
            {
                case "tools": return "Tool use";
                case "notools": return "No tool use";
                case "vision": return "Vision";
                case "thinking": return "Thinking";
            }
            return trait;
        }

        public static string TraitTip(string trait)
        {
            switch (trait)
            {
                case "tools": return "Tool use: this model can call tools. The agent needs this to read and change files, run commands and use the browser.";
                case "notools": return "No tool use: this model's chat template has no tool calls, so the agent may not be able to work with it.";
                case "vision": return "Vision: this model can see images, such as pictures you attach and screenshots of pages and programs.";
                case "thinking": return "Thinking: this model can think before it answers. The Thinking chip under the message box sets how much.";
            }
            return null;
        }

        Color TraitColor(string trait)
        {
            if (trait == "notools") return th.Error;
            if (th.HighContrast) return th.Fore;
            if (trait == "tools") return th.Dark ? Color.FromArgb(0x86, 0xAE, 0xFF) : Color.FromArgb(0x23, 0x63, 0xC9);
            if (trait == "vision") return th.Dark ? Color.FromArgb(0xE9, 0xB5, 0x4E) : Color.FromArgb(0x9A, 0x62, 0x00);
            return th.Dark ? Color.FromArgb(0xB8, 0xA0, 0xFF) : Color.FromArgb(0x6E, 0x47, 0xC4);
        }

        static string TraitIcon(string trait) { return trait == "vision" ? "eye" : trait == "thinking" ? "bulb" : trait == "notools" ? "warning" : "tool"; }

        int RowH { get { return UI.S(RowHeight); } }
        int MaxScroll { get { return Math.Max(0, Items.Count * RowH - Height); } }

        public void Changed() { scroll = Math.Max(0, Math.Min(scroll, MaxScroll)); Invalidate(); }

        public void ScrollToTop() { scroll = 0; Invalidate(); }

        Rectangle Track() { return new Rectangle(Width - UI.S(10), 0, UI.S(10), Height); }

        Rectangle Thumb()
        {
            int total = Items.Count * RowH;
            if (total <= Height || Height <= 0) return Rectangle.Empty;
            int len = Math.Max(UI.S(28), (int)((long)Height * Height / total));
            int y = (int)((long)(Height - len) * scroll / Math.Max(1, MaxScroll));
            return new Rectangle(Width - UI.S(10), y, UI.S(10), len);
        }

        int RightEdge { get { return Width - (Thumb().IsEmpty ? 0 : UI.S(10)); } }

        List<Rectangle> ButtonRects(int row, Graphics g)
        {
            List<Rectangle> l = new List<Rectangle>();
            RowItem it = Items[row];
            int x = RightEdge - UI.S(12) - UI.S(8);
            int y = row * RowH - scroll + (RowH - UI.S(30)) / 2;
            for (int i = it.Buttons.Count - 1; i >= 0; i--)
            {
                int w = TextRenderer.MeasureText(it.Buttons[i], Font).Width + UI.S(24);
                x -= w;
                l.Insert(0, new Rectangle(x, y, w, UI.S(30)));
                x -= UI.S(6);
            }
            return l;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Chrome);
            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            Glyphs.FillRound(g, th.Back, box, UI.S(10));
            using (System.Drawing.Drawing2D.GraphicsPath clip = Glyphs.Round(new Rectangle(1, 1, Width - 2, Height - 2), UI.S(9), true))
                g.SetClip(clip);
            PaintRows(g);
            g.ResetClip();
            Glyphs.DrawRound(g, th.CardBorder, box, UI.S(10));
            if (Focused && ShowFocusCues && SelectedIndex < 0) Glyphs.Ring(g, th.Accent, new Rectangle(0, 0, Width, Height), UI.S(10), Glyphs.FocusWidth);
        }

        int SelectedIndex { get { return Items.FindIndex(delegate (RowItem r) { return r.Selected; }); } }

        void SelectRow(int i)
        {
            for (int k = 0; k < Items.Count; k++) Items[k].Selected = k == i;
            Invalidate();
        }

        void EnsureVisible(int i)
        {
            int top = i * RowH;
            if (top < scroll) scroll = top;
            else if (top + RowH > scroll + Height) scroll = top + RowH - Height;
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
            Invalidate();
        }

        static bool MainAction(string b) { return b == "Use" || b == "Download"; }

        void ShowActions(int row, Point at)
        {
            if (row < 0 || row >= Items.Count) return;
            RowItem it = Items[row];
            ContextMenuStrip m = Menus.Create(th);
            m.ShowCheckMargin = false;
            foreach (string b in it.Buttons)
            {
                string name = b;
                ToolStripMenuItem mi = Menus.Item(name, false, delegate { if (ButtonClicked != null && Items.Contains(it) && it.Buttons.Contains(name)) ButtonClicked(it, name); });
                mi.Enabled = name != "In use";
                m.Items.Add(mi);
            }
            List<string> traits = new List<string>();
            foreach (string t in it.Traits) traits.Add(TraitName(t));
            string text = string.Join("\r\n", new string[] { it.Title, string.Join(", ", traits.ToArray()), it.Sub, it.ProgressText }).Replace("\r\n\r\n", "\r\n").Trim();
            if (text.Length > 0)
            {
                if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator());
                m.Items.Add(Menus.Item("Copy details", false, delegate { try { Clipboard.SetText(text); } catch { } }));
            }
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(this, at);
        }

        void PaintRows(Graphics g)
        {
            tipSpots.Clear();
            if (Items.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Font, new Rectangle(UI.S(16), UI.S(16), Width - UI.S(32), Height - UI.S(32)), th.ChromeDim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
                return;
            }
            int first = scroll / RowH;
            int right = RightEdge;
            for (int i = first; i < Items.Count; i++)
            {
                int y = i * RowH - scroll;
                if (y > Height) break;
                RowItem it = Items[i];
                Rectangle row = new Rectangle(UI.S(6), y + UI.S(2), right - UI.S(12), RowH - UI.S(4));
                if (it.Selected) Glyphs.FillRound(g, Theme.Blend(th.Back, th.Accent, th.Dark ? 0.22 : 0.10), row, UI.S(8));
                else if (i == hoverRow) Glyphs.Hover(g, th, th.TabHover, row, UI.S(8));
                if (it.Selected && Focused && ShowFocusCues) Glyphs.Ring(g, th.Accent, row, UI.S(8), Glyphs.FocusWidth);
                List<Rectangle> btns = ButtonRects(i, g);
                int textRight = btns.Count > 0 ? btns[0].Left - UI.S(10) : right - UI.S(20);
                int x = UI.S(18);
                int top = y + (RowH - UI.S(44)) / 2;
                List<KeyValuePair<string, int>> badges = it.Badges.FindAll(delegate (KeyValuePair<string, int> k) { return k.Value == 2; });
                int warnW = 0;
                foreach (KeyValuePair<string, int> b in badges) warnW += TextRenderer.MeasureText(b.Key, Font).Width + UI.S(20);
                badges.AddRange(it.Badges.FindAll(delegate (KeyValuePair<string, int> k) { return k.Value != 2; }));
                int chip = UI.S(24), chipGap = UI.S(4);
                int traitW = it.Traits.Count > 0 ? it.Traits.Count * (chip + chipGap) + UI.S(6) : 0;
                int tw = Math.Min(TextRenderer.MeasureText(it.Title, Font).Width, Math.Max(UI.S(40), textRight - x - UI.S(18) - warnW - traitW));
                using (Font bold = Fonts.Semibold(Font.Size))
                    TextRenderer.DrawText(g, it.Title, bold, new Rectangle(x, top, tw + UI.S(14), UI.S(20)), th.Fore, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
                int bx = x + tw + UI.S(18);
                foreach (string t in it.Traits)
                {
                    Rectangle cr = new Rectangle(bx, top, chip, UI.S(20));
                    if (cr.Right > textRight) break;
                    Color c = TraitColor(t);
                    Glyphs.FillRound(g, Theme.Blend(th.Back, c, th.Dark ? 0.20 : 0.12), cr, UI.S(6));
                    Rectangle ir = new Rectangle(cr.X + (cr.Width - UI.S(14)) / 2, cr.Y + (cr.Height - UI.S(14)) / 2, UI.S(14), UI.S(14));
                    Glyphs.Draw(TraitIcon(t), g, ir, c);
                    tipSpots.Add(new KeyValuePair<Rectangle, string>(cr, TraitTip(t)));
                    bx += chip + chipGap;
                }
                if (it.Traits.Count > 0) bx += UI.S(6);
                foreach (KeyValuePair<string, int> b in badges)
                {
                    int bw = TextRenderer.MeasureText(b.Key, Font).Width + UI.S(14);
                    if (bx + bw > textRight) break;
                    Rectangle br = new Rectangle(bx, top, bw, UI.S(20));
                    Color c = b.Value == 1 ? th.Accent : b.Value == 2 ? th.Error : b.Value == 3 ? (th.Dark ? Color.FromArgb(0x6C, 0xC2, 0x6C) : Color.FromArgb(0x2E, 0x7D, 0x32)) : th.ChromeDim;
                    Glyphs.DrawRound(g, Theme.Blend(th.Back, c, 0.7), br, UI.S(10));
                    TextRenderer.DrawText(g, b.Key, Font, br, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                    bx += bw + UI.S(6);
                }
                if (it.Progress >= 0)
                {
                    TextRenderer.DrawText(g, it.ProgressText, Font, new Rectangle(x, top + UI.S(21), Math.Max(0, textRight - x), UI.S(18)), th.ChromeDim, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
                    Rectangle bar = new Rectangle(x, top + UI.S(40), Math.Max(UI.S(40), textRight - x), UI.S(4));
                    Glyphs.Hover(g, th, th.ButtonHover, bar, UI.S(2));
                    Glyphs.FillRound(g, th.Accent, new Rectangle(bar.X, bar.Y, Math.Max(UI.S(4), (int)((long)bar.Width * Math.Min(1000, it.Progress) / 1000)), bar.Height), UI.S(2));
                }
                else
                    TextRenderer.DrawText(g, it.Sub, Font, new Rectangle(x, top + UI.S(23), Math.Max(0, textRight - x), UI.S(20)), th.ChromeDim, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
                for (int b = 0; b < btns.Count; b++)
                {
                    bool hot = i == hoverRow && b == hoverBtn;
                    bool primary = b == 0 && MainAction(it.Buttons[b]) && (it.Buttons[b] == "Use" || !QuietDownloads || it.Selected);
                    Rectangle r = btns[b];
                    Color fill = primary ? (hot ? Theme.Blend(th.Accent, th.Dark ? Color.White : Color.Black, 0.12) : th.Accent) : (hot ? th.ButtonHover : th.Back);
                    Glyphs.FillRound(g, fill, r, UI.S(8));
                    Glyphs.DrawRound(g, primary ? fill : th.InputBorder, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), UI.S(8));
                    TextRenderer.DrawText(g, it.Buttons[b], Font, r, primary ? (th.Dark ? Color.Black : Color.White) : th.Fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                }
            }
            Rectangle tr = Thumb();
            if (!tr.IsEmpty)
            {
                int inset = sbHover || sbDrag ? UI.S(2) : UI.S(3);
                Glyphs.FillRound(g, sbHover || sbDrag ? th.ThumbHover : th.Thumb, new Rectangle(tr.X + inset, tr.Y + 2, tr.Width - inset * 2, tr.Height - 4), UI.S(4));
            }
        }

        void HitTest(Point p, out int row, out int btn)
        {
            row = (p.Y + scroll) / RowH;
            if (row >= Items.Count || p.Y < 0 || p.X >= RightEdge) row = -1;
            btn = -1;
            if (row < 0) return;
            using (Graphics g = CreateGraphics())
            {
                List<Rectangle> rs = ButtonRects(row, g);
                for (int i = 0; i < rs.Count; i++) if (rs[i].Contains(p)) btn = i;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (sbDrag)
            {
                Rectangle t = Thumb();
                int range = Math.Max(1, Height - t.Height);
                scroll = Math.Max(0, Math.Min(MaxScroll, (int)((long)(e.Y - dragOff) * MaxScroll / range)));
                Invalidate();
                return;
            }
            bool sb = !Thumb().IsEmpty && Track().Contains(e.Location);
            int row, btn;
            HitTest(e.Location, out row, out btn);
            if (row != hoverRow || btn != hoverBtn || sb != sbHover) { hoverRow = row; hoverBtn = btn; sbHover = sb; Cursor = !sb && (btn >= 0 || (row >= 0 && RowClicked != null)) ? Cursors.Hand : Cursors.Default; Invalidate(); }
            string hint = null;
            foreach (KeyValuePair<Rectangle, string> s in tipSpots) if (s.Key.Contains(e.Location)) { hint = s.Value; break; }
            if (hint != tipShown) { tipShown = hint; tip.SetToolTip(this, hint); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (tipShown != null) { tipShown = null; tip.SetToolTip(this, null); }
            if (!sbDrag) { hoverRow = hoverBtn = -1; sbHover = false; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            downRow = downBtn = -1;
            if (e.Button != MouseButtons.Left) return;
            Rectangle t = Thumb();
            if (!t.IsEmpty && Track().Contains(e.Location))
            {
                if (t.Contains(e.Location)) { sbDrag = true; dragOff = e.Y - t.Y; Capture = true; }
                else { scroll = Math.Max(0, Math.Min(MaxScroll, scroll + (e.Y < t.Y ? -Height : Height))); Invalidate(); }
                return;
            }
            HitTest(e.Location, out downRow, out downBtn);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (sbDrag) { sbDrag = false; Capture = false; Invalidate(); return; }
            int row, btn;
            HitTest(e.Location, out row, out btn);
            hoverRow = row; hoverBtn = btn;
            if (e.Button == MouseButtons.Right)
            {
                if (row >= 0 && Items[row].Buttons.Count > 0) { SelectRow(row); ShowActions(row, e.Location); }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (row < 0 || row != downRow || btn != downBtn) return;
            RowItem it = Items[row];
            if (RowClicked == null) SelectRow(row);
            if (btn >= 0 && btn < it.Buttons.Count) { if (ButtonClicked != null) ButtonClicked(it, it.Buttons[btn]); return; }
            if (RowClicked != null) RowClicked(it);
        }

        readonly WheelAcc wheel = new WheelAcc();

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - wheel.Take(e.Delta, RowH)));
            int row, btn;
            HitTest(PointToClient(Cursor.Position), out row, out btn);
            hoverRow = row; hoverBtn = btn;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: case Keys.Enter: case Keys.Apps: return true;
            }
            if (keyData == (Keys.Shift | Keys.F10)) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Items.Count == 0) return;
            int cur = SelectedIndex;
            int page = Math.Max(1, Height / RowH - 1), next = cur;
            if (e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Modifiers == Keys.Shift))
            {
                e.Handled = e.SuppressKeyPress = true;
                if (cur < 0 || Items[cur].Buttons.Count == 0) return;
                EnsureVisible(cur);
                Rectangle first;
                using (Graphics g = CreateGraphics()) first = ButtonRects(cur, g)[0];
                ShowActions(cur, new Point(first.Left, Math.Min(Height - UI.S(4), first.Bottom + UI.S(4))));
                return;
            }
            switch (e.KeyCode)
            {
                case Keys.Down: next = Math.Min(Items.Count - 1, cur + 1); break;
                case Keys.Up: next = Math.Max(0, cur < 0 ? 0 : cur - 1); break;
                case Keys.PageDown: next = Math.Min(Items.Count - 1, Math.Max(0, cur) + page); break;
                case Keys.PageUp: next = Math.Max(0, cur - page); break;
                case Keys.Home: next = 0; break;
                case Keys.End: next = Items.Count - 1; break;
                case Keys.Enter:
                    e.Handled = true;
                    if (cur < 0) return;
                    if (RowClicked != null) RowClicked(Items[cur]);
                    else if (Items[cur].Buttons.Count > 0 && MainAction(Items[cur].Buttons[0]) && ButtonClicked != null) ButtonClicked(Items[cur], Items[cur].Buttons[0]);
                    return;
                default: return;
            }
            e.Handled = true;
            if (next < 0 || next == cur) return;
            SelectRow(next);
            EnsureVisible(next);
            if (RowClicked != null) RowClicked(Items[next]);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    class ModelLibraryDialog : ThemedForm
    {
        readonly Settings s;
        readonly Segmented tabs;
        readonly PillButton engineChip;
        readonly Label hardware;
        readonly Panel banner = new Panel();
        readonly Label bannerText;
        readonly Button bannerButton, bannerSkip;
        readonly RowList local = new RowList();
        readonly Panel hubPanel = new Panel();
        readonly InputBox search;
        readonly Button searchButton;
        readonly RowList repos = new RowList();
        readonly RowList reco = new RowList();
        readonly RowList variants = new RowList();
        readonly Label variantsTitle, detailSub, detailDesc, recoHint, reposCaption;
        readonly System.Windows.Forms.Timer typing = new System.Windows.Forms.Timer();
        CancelToken searchCt;
        readonly PillButton moreToggle, pageLink;
        bool showAll;
        int searchSeq;
        readonly Label footer;
        readonly Button close, folderButton;
        HubRepo shownRepo;
        GgufShape shownShape;
        volatile bool installing;
        bool justInstalled, updateDismissed;
        string bannerMode = "";
        EngineRelease failedRelease;
        CancelToken installCt;

        public event Action<string> ModelChosen;

        public bool Busy { get { return installing; } }

        public ModelLibraryDialog(Settings settings, Theme t) : base(t, "Model library", WindowW, WindowH)
        {
            s = settings;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(UI.S(640), UI.S(420));
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterParent;
            tabs = new Segmented(Th, new string[] { "On this PC", "Download" }, 0);
            tabs.AccessibleName = "Model library view";
            tabs.SelectedIndexChanged += delegate { ShowTab(); };
            engineChip = new PillButton("", Th, false);
            engineChip.Chevron = true;
            engineChip.Click += delegate { EngineMenu(); };
            hardware = Dialogs.MakeLabel(HardwareText(), Th, true);
            hardware.AutoEllipsis = true;
            hardware.TextAlign = ContentAlignment.MiddleRight;
            banner.BackColor = Theme.Blend(Th.Chrome, Th.Accent, Th.Dark ? 0.14 : 0.08);
            banner.Paint += delegate (object o, PaintEventArgs pe)
            {
                pe.Graphics.Clear(Th.Chrome);
                Glyphs.FillRound(pe.Graphics, banner.BackColor, new Rectangle(0, 0, banner.Width - 1, banner.Height - 1), UI.S(10));
            };
            bannerText = Dialogs.MakeLabel("", Th, false);
            bannerText.ForeColor = Th.Fore;
            bannerButton = Dialogs.MakeButton("Set up the engine", Th, true);
            bannerButton.Click += delegate { BannerClicked(); };
            bannerSkip = Dialogs.MakeButton("Not now", Th, false);
            bannerSkip.Visible = false;
            bannerSkip.Click += delegate { updateDismissed = true; failedRelease = null; UpdateBanner(); };
            banner.Controls.Add(bannerText);
            banner.Controls.Add(bannerSkip);
            banner.Controls.Add(bannerButton);
            local.SetTheme(Th);
            local.EmptyText = "No models on this PC yet.\n\nUse Download to get one, for example a Qwen 3 model. GGUF models you already have from LM Studio show up here too.";
            local.ButtonClicked += OnLocalButton;
            search = new InputBox("Search Hugging Face, for example qwen3");
            search.Field = true;
            search.Glyph = "search";
            search.SetTheme(Th);
            search.Box.KeyDown += delegate (object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; typing.Stop(); Search(); } };
            typing.Interval = 400;
            typing.Tick += delegate { typing.Stop(); Search(); };
            search.Box.TextChanged += delegate { typing.Stop(); typing.Start(); };
            searchButton = Dialogs.MakeButton("Search", Th, false);
            searchButton.Click += delegate { typing.Stop(); Search(); };
            reposCaption = Dialogs.MakeLabel("", Th, true);
            reposCaption.AutoEllipsis = true;
            reposCaption.Visible = false;
            repos.SetTheme(Th);
            repos.EmptyText = "Search Hugging Face for GGUF models, for example \"qwen\", \"gemma\" or \"coder\".";
            repos.RowClicked += delegate (RowItem it) { ShowVariants(it); };
            reco.SetTheme(Th);
            reco.RowHeight = 64;
            reco.EmptyText = "";
            reco.ButtonClicked += OnVariantButton;
            variants.SetTheme(Th);
            variants.QuietDownloads = true;
            variants.EmptyText = "Pick a model on the left.";
            variants.ButtonClicked += OnVariantButton;
            variantsTitle = Dialogs.MakeLabel("", Th, false);
            variantsTitle.ForeColor = Th.Fore;
            variantsTitle.Font = Fonts.Semibold(UI.Font.Size * 1.25f);
            variantsTitle.AutoEllipsis = true;
            detailSub = Dialogs.MakeLabel("", Th, true);
            detailSub.AutoEllipsis = true;
            detailDesc = Dialogs.MakeLabel("", Th, false);
            detailDesc.ForeColor = Th.Fore;
            detailDesc.AutoEllipsis = true;
            recoHint = Dialogs.MakeLabel("", Th, true);
            moreToggle = new PillButton("Other sizes", Th, false);
            moreToggle.Chevron = true;
            moreToggle.Click += delegate { showAll = !showAll; UpdateMore(); PerformLayout(); };
            pageLink = new PillButton("Model page", Th, false);
            pageLink.Glyph = "external";
            pageLink.Click += delegate { if (shownRepo != null) BrowserPane.OpenExternally(Hub.Host + "/" + shownRepo.Id); };
            hubPanel.Controls.AddRange(new Control[] { search, searchButton, reposCaption, repos, variantsTitle, detailSub, detailDesc, pageLink, recoHint, reco, moreToggle, variants });
            reco.Visible = moreToggle.Visible = pageLink.Visible = false;
            hubPanel.BackColor = Th.Chrome;
            footer = Dialogs.MakeLabel("", Th, true);
            UpdateFooter();
            folderButton = Dialogs.MakeButton("Change folder...", Th, false);
            folderButton.Click += delegate { ChooseFolder(); };
            close = Dialogs.MakeButton("Close", Th, false);
            close.Click += delegate { Close(); };
            CancelButton = close;
            Controls.AddRange(new Control[] { tabs, engineChip, hardware, banner, local, hubPanel, footer, folderButton, close });
            ModelDownloads.Changed += OnJobChanged;
            EngineUpdates.Changed += OnUpdatesChanged;
            FormClosed += delegate { ModelDownloads.Changed -= OnJobChanged; EngineUpdates.Changed -= OnUpdatesChanged; };
            UpdateBanner();
            ShowTab();
            RefreshLocal(false);
            EngineUpdates.CheckSoon();
        }

        static string Gb(long bytes)
        {
            double gb = bytes / (double)(1L << 30);
            return (gb >= 10 ? Math.Round(gb).ToString("0", CultureInfo.InvariantCulture) : gb.ToString("0.#", CultureInfo.InvariantCulture)) + " GB";
        }

        static string HardwareText()
        {
            long ram = Hardware.RamBytes;
            List<GpuInfo> used = Hardware.EngineGpus();
            if (used.Count == 0 && Hardware.BestGpu != null) used.Add(Hardware.BestGpu);
            long vram = Hardware.TotalVram(used);
            string gpu = used.Count == 0 ? "No graphics card found" : Hardware.Names(used, true) + (vram > 0 ? " \u00B7 " + Gb(vram) + " VRAM" : "");
            return gpu + (ram > 0 ? " \u00B7 " + Gb(ram) + " RAM" : "");
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (tabs == null || close == null) return;
            int pad = UI.S(16), w = ClientSize.Width, h = ClientSize.Height;
            tabs.SetBounds(pad, pad, tabs.PreferredWidth, UI.S(34));
            int right = w - pad;
            if (engineChip.Visible)
            {
                int ew = engineChip.PreferredWidth;
                engineChip.SetBounds(right - ew, pad + UI.S(1), ew, UI.S(32));
                right = engineChip.Left - UI.S(14);
            }
            hardware.SetBounds(tabs.Right + UI.S(16), pad, Math.Max(UI.S(10), right - tabs.Right - UI.S(16)), UI.S(34));
            int y = pad + UI.S(48);
            if (banner.Visible)
            {
                int buttonsW = bannerButton.Width + (bannerSkip.Visible ? bannerSkip.Width + UI.S(8) : 0);
                int textW = w - pad * 2 - buttonsW - UI.S(36);
                banner.SetBounds(pad, y, w - pad * 2, Math.Max(UI.S(64), Fonts.Height(bannerText.Text, bannerText.Font, textW) + UI.S(24)));
                bannerButton.Location = new Point(banner.Width - bannerButton.Width - UI.S(12), (banner.Height - bannerButton.Height) / 2);
                if (bannerSkip.Visible) bannerSkip.Location = new Point(bannerButton.Left - UI.S(8) - bannerSkip.Width, bannerButton.Top);
                int textRight = bannerSkip.Visible ? bannerSkip.Left : bannerButton.Left;
                bannerText.SetBounds(UI.S(12), UI.S(6), Math.Max(10, textRight - UI.S(24)), banner.Height - UI.S(12));
                bannerText.TextAlign = ContentAlignment.MiddleLeft;
                y += banner.Height + UI.S(10);
            }
            int footW = Math.Max(UI.S(40), w - pad * 2 - close.Width - folderButton.Width - UI.S(24));
            int footH = Math.Max(UI.S(40), Fonts.Height(footer.Text, footer.Font, footW) + UI.S(4));
            int bottom = h - Math.Max(UI.S(52), footH + UI.S(12));
            local.SetBounds(pad, y, w - pad * 2, bottom - y);
            hubPanel.SetBounds(pad, y, w - pad * 2, bottom - y);
            int hw = hubPanel.Width, hh = hubPanel.Height;
            int half = Math.Max(UI.S(240), hw * 44 / 100);
            searchButton.Location = new Point(half - searchButton.Width, 0);
            search.SetBounds(0, 0, searchButton.Left - UI.S(8), UI.S(32));
            int listTop = UI.S(44);
            if (reposCaption.Visible)
            {
                reposCaption.SetBounds(UI.S(2), listTop - UI.S(4), half - UI.S(2), UI.S(22));
                reposCaption.TextAlign = ContentAlignment.MiddleLeft;
                listTop += UI.S(22);
            }
            repos.SetBounds(0, listTop, half, hh - listTop);
            int rx = half + UI.S(18), rw = Math.Max(UI.S(120), hw - rx);
            int pw = pageLink.PreferredWidth;
            pageLink.SetBounds(rx + rw - pw, 0, pw, UI.S(30));
            variantsTitle.SetBounds(rx, 0, Math.Max(UI.S(40), rw - (pageLink.Visible ? pw + UI.S(8) : 0)), UI.S(30));
            variantsTitle.TextAlign = ContentAlignment.MiddleLeft;
            detailSub.SetBounds(rx, UI.S(30), rw, UI.S(22));
            detailSub.TextAlign = ContentAlignment.MiddleLeft;
            int ry = UI.S(60);
            if (detailDesc.Text.Length > 0)
            {
                int dh = Math.Min(Fonts.Height(detailDesc.Text, detailDesc.Font, rw), UI.S(58));
                detailDesc.SetBounds(rx, ry - UI.S(2), rw, dh);
                ry += dh + UI.S(8);
            }
            else detailDesc.SetBounds(rx, ry, rw, 0);
            if (reco.Visible)
            {
                int hintH = Math.Max(UI.S(18), Fonts.Height(recoHint.Text, UI.Font, rw));
                recoHint.SetBounds(rx, ry, rw, hintH);
                ry += hintH + UI.S(6);
                reco.SetBounds(rx, ry, rw, UI.S(64));
                ry += UI.S(64) + UI.S(6);
            }
            else recoHint.SetBounds(rx, ry, rw, 0);
            if (moreToggle.Visible)
            {
                moreToggle.SetBounds(rx, ry, moreToggle.PreferredWidth, UI.S(32));
                ry += UI.S(40);
            }
            variants.SetBounds(rx, ry, rw, Math.Max(0, hh - ry));
            close.Location = new Point(w - pad - close.Width, h - UI.S(42));
            folderButton.Location = new Point(close.Left - UI.S(8) - folderButton.Width, h - UI.S(42));
            footer.SetBounds(pad, h - UI.S(6) - footH, folderButton.Left - pad - UI.S(12), footH);
        }

        void ShowTab()
        {
            local.Visible = tabs.SelectedIndex == 0;
            hubPanel.Visible = tabs.SelectedIndex == 1;
            if (tabs.SelectedIndex == 1 && repos.Items.Count == 0) Search();
            PerformLayout();
        }

        void OnUpdatesChanged() { Post(delegate { UpdateBanner(); }); }

        void BannerClicked()
        {
            if (installing)
            {
                if (installCt != null) installCt.Cancel();
                return;
            }
            if (bannerMode == "ready")
            {
                bool find = bannerButton.Text == "Find a model";
                justInstalled = false;
                UpdateBanner();
                if (find) { if (tabs.SelectedIndex == 1) ShowTab(); else tabs.SelectedIndex = 1; }
                return;
            }
            if (bannerMode == "update")
            {
                EngineRelease r = EngineUpdates.Available;
                if (r != null) UpdateEngine(r);
                return;
            }
            if (bannerMode == "failed" && failedRelease != null)
            {
                if (EngineUpdates.Enabled) UpdateEngine(failedRelease);
                else { failedRelease = null; UpdateBanner(); }
                return;
            }
            InstallMenu();
        }

        void UpdateBanner()
        {
            if (installing) return;
            if (hardware != null) hardware.Text = HardwareText();
            Engine e = Engine.Instance;
            EngineBuild have = e.Installed ? Engine.BuildFor(e.InstalledBackend) : null;
            engineChip.Visible = have != null;
            if (have != null) engineChip.Text = "Engine: " + have.Title;
            bannerSkip.Visible = false;
            string why;
            string rec = Hardware.RecommendedBackend(out why);
            EngineBuild b = Engine.BuildFor(rec);
            EngineRelease upd = EngineUpdates.Available;
            if (have == null)
            {
                bannerMode = "setup";
                banner.Visible = true;
                bannerText.Text = "The built-in engine runs models on this PC: llama.cpp " + Engine.BuildVersion + " from GitHub, checked against checksums built into MinGent.\nRecommended for this PC: " + (b != null ? b.Title + " (about " + ModelCatalog.Size(b.ApproxBytes) + ")" : rec) + ".";
                bannerButton.Text = "Set up the engine";
                bannerButton.Visible = true;
            }
            else if (justInstalled)
            {
                bannerMode = "ready";
                banner.Visible = true;
                bannerText.Text = "The built-in engine is ready (" + e.InstalledText + ", " + have.Title + "). It only runs on this PC.\n" +
                                  (local.Items.Count == 0 ? "Next, download a model, then pick it in the model menu under the message box." : "Pick a model below, or choose it in the model menu under the message box.");
                bannerButton.Text = local.Items.Count == 0 ? "Find a model" : "OK";
                bannerButton.Visible = true;
            }
            else if (upd != null && !updateDismissed)
            {
                bannerMode = "update";
                banner.Visible = true;
                bannerText.Text = "llama.cpp " + upd.Version + " is out. You have " + e.InstalledText + (e.OnTestedBuild ? ", the release MinGent was tested with" : "") + ".\n" +
                                  "The update (" + ModelCatalog.Size(upd.Bytes) + ") is checked against the checksums GitHub lists for it. If it runs worse, go back from the Engine menu.";
                bannerButton.Text = "Update";
                bannerButton.Visible = true;
                bannerSkip.Visible = true;
            }
            else
            {
                bannerMode = "";
                banner.Visible = false;
            }
            PerformLayout();
        }

        void InstallMenu()
        {
            if (installing)
            {
                if (installCt != null) installCt.Cancel();
                return;
            }
            string why;
            string rec = Hardware.RecommendedBackend(out why);
            ContextMenuStrip m = Menus.Create(Th);
            m.Items.Add(Menus.Header(why, Th));
            foreach (EngineBuild b in Engine.Builds)
            {
                EngineBuild build = b;
                m.Items.Add(Menus.Item(b.Title + (b.Backend == rec ? " (recommended)" : "") + "  \u00B7  " + ModelCatalog.Size(b.ApproxBytes), false, delegate { Install(build.Backend); }));
            }
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(bannerButton, new Point(0, bannerButton.Height));
        }

        void EngineMenu()
        {
            if (installing) return;
            string why;
            string rec = Hardware.RecommendedBackend(out why);
            Engine e = Engine.Instance;
            string cur = e.InstalledBackend;
            ContextMenuStrip m = Menus.Create(Th);
            m.Items.Add(Menus.Header((e.InstalledText ?? "llama.cpp") + "  \u00B7  " + why, Th));
            foreach (EngineBuild b in Engine.Builds)
            {
                EngineBuild build = b;
                m.Items.Add(Menus.Item(b.Title + (b.Backend == rec ? " (recommended)" : "") + "  \u00B7  " + ModelCatalog.Size(b.ApproxBytes), b.Backend == cur, delegate
                {
                    if (build.Backend == Engine.Instance.InstalledBackend) return;
                    if (!StopFirst("Switching builds stops it.", "Switch and stop it")) return;
                    Install(build.Backend);
                }));
            }
            EngineRelease upd = EngineUpdates.Available;
            if (upd != null || !e.OnTestedBuild) m.Items.Add(new ToolStripSeparator());
            if (upd != null)
                m.Items.Add(Menus.Item("Update to llama.cpp " + upd.Version, false, delegate { UpdateEngine(upd); }));
            if (!e.OnTestedBuild && cur != null)
            {
                string backend = cur;
                m.Items.Add(Menus.Item("Go back to llama.cpp " + Engine.BuildVersion + ", the tested release", false, delegate
                {
                    if (!StopFirst("Going back stops it.", "Go back and stop it")) return;
                    Install(backend);
                }));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Menus.Item("Remove the engine", false, delegate
            {
                if (!Alert.Confirm(this, Th, "Remove the built-in engine from this PC? Downloaded models stay where they are.", Text, "Remove", true)) return;
                string err = Engine.Instance.Uninstall();
                if (err != null) Alert.Show(this, Th, "Could not remove the engine: " + err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                justInstalled = false;
                UpdateBanner();
            }));
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(engineChip, new Point(engineChip.Width, engineChip.Height + UI.S(4)), ToolStripDropDownDirection.BelowLeft);
        }

        bool StopFirst(string what, string action)
        {
            if (!Engine.Instance.Running && !Engine.Instance.Starting) return true;
            return Alert.Ask(this, Th, "The built-in engine is running a model. " + what, Text, action);
        }

        void UpdateEngine(EngineRelease r)
        {
            if (r == null || !StopFirst("Updating stops it.", "Update and stop it")) return;
            Run(r.Backend, r);
        }

        void Install(string backend) { Run(backend, null); }

        void Run(string backend, EngineRelease rel)
        {
            banner.Visible = true;
            engineChip.Visible = false;
            bannerButton.Visible = true;
            bannerSkip.Visible = false;
            PerformLayout();
            installing = true;
            installCt = new CancelToken();
            CancelToken ct = installCt;
            bannerButton.Text = "Cancel";
            bannerText.Text = "Preparing...";
            Thread th = new Thread(delegate ()
            {
                string err;
                try
                {
                    Action<string, long, long> progress = delegate (string status, long done, long total)
                    {
                        string pct = total > 0 ? " - " + (done * 100 / total) + "%" : "";
                        Post(delegate { bannerText.Text = status + pct; });
                    };
                    err = rel != null ? Engine.Instance.Install(rel, progress, ct) : Engine.Instance.Install(backend, progress, ct);
                }
                catch (Exception ex) { err = ex.Message; }
                Post(delegate
                {
                    installing = false;
                    if (err != null)
                    {
                        bannerMode = "failed";
                        failedRelease = rel;
                        bannerText.Text = (rel != null ? "Update failed: " : "Setup failed: ") + err;
                        bannerButton.Text = "Try again";
                        bannerSkip.Visible = rel != null;
                        EngineBuild have = Engine.Instance.Installed ? Engine.BuildFor(Engine.Instance.InstalledBackend) : null;
                        engineChip.Visible = have != null;
                        if (have != null) engineChip.Text = "Engine: " + have.Title;
                        PerformLayout();
                    }
                    else
                    {
                        failedRelease = null;
                        justInstalled = true;
                        UpdateBanner();
                        RefreshLocal(true);
                    }
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        readonly List<Action> early = new List<Action>();

        void Post(Action a)
        {
            try
            {
                if (IsDisposed) return;
                if (!IsHandleCreated)
                {
                    lock (early)
                    {
                        if (!IsHandleCreated) { early.Add(a); return; }
                    }
                }
                BeginInvoke(a);
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Action[] q;
            lock (early) { q = early.ToArray(); early.Clear(); }
            foreach (Action a in q) { try { BeginInvoke(a); } catch { } }
        }

        bool localBusy, localAgain, localForce;

        void RefreshLocal(bool force)
        {
            if (localBusy) { localAgain = true; localForce |= force; return; }
            localBusy = true;
            Thread t = new Thread(delegate ()
            {
                List<LocalModel> list;
                try { list = ModelLibrary.Scan(force); } catch { list = new List<LocalModel>(); }
                Dictionary<string, GgufShape> found = new Dictionary<string, GgufShape>(StringComparer.OrdinalIgnoreCase);
                foreach (LocalModel m in list)
                {
                    if (m.IsProjector || m.Helper || m.PartIndex > 1) continue;
                    try { GgufShape g = ModelLibrary.ShapeOf(m.Path); if (g != null) found[m.Path] = g; } catch { }
                }
                Post(delegate
                {
                    FillLocal(list, found);
                    localBusy = false;
                    if (localAgain)
                    {
                        bool f = localForce;
                        localAgain = localForce = false;
                        RefreshLocal(f);
                    }
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        double KvPerElem { get { return Hub.KvBytesPerElem(s.EngineKv); } }

        KeyValuePair<string, int> FitBadge(long bytes, GgufShape shape)
        {
            string f = Hub.Fit(bytes, shape, KvPerElem);
            if (f == "fits") return new KeyValuePair<string, int>("Fits your GPU", 3);
            if (f == "fitsram") return new KeyValuePair<string, int>("Fits in memory", 3);
            if (f == "partial") return new KeyValuePair<string, int>("Partly in RAM, slower", 0);
            if (f == "tight") return new KeyValuePair<string, int>("Tight on memory", 0);
            return new KeyValuePair<string, int>("Too big for this PC", 2);
        }

        void FillLocal(List<LocalModel> list, Dictionary<string, GgufShape> shapes)
        {
            string keep = null;
            foreach (RowItem r in local.Items) if (r.Selected && r.Tag is LocalModel) keep = ((LocalModel)r.Tag).Path;
            local.Items.Clear();
            string cur = s.AiModel ?? "";
            foreach (DownloadJob j in ModelDownloads.Active())
            {
                RowItem jr = new RowItem();
                jr.Title = j.Title;
                jr.Badges.Add(new KeyValuePair<string, int>("Downloading", 1));
                jr.Progress = j.Progress;
                jr.ProgressText = j.Text;
                jr.Buttons.Add("Cancel");
                jr.Tag = j;
                local.Items.Add(jr);
            }
            foreach (LocalModel m in list)
            {
                if (m.IsProjector || m.Helper || m.PartIndex > 1) continue;
                RowItem it = new RowItem();
                it.Title = m.DisplayName;
                GgufShape shape;
                shapes.TryGetValue(m.Path, out shape);
                it.Sub = ModelCatalog.Join("  \u00B7  ", m.ParamsText, m.Quant, ModelCatalog.Size(m.TotalBytes), Needs(m.TotalBytes, shape), m.TrainContext > 0 ? "up to " + Tokens(m.TrainContext) + " context" : null,
                                           m.SourceLabel, m.Modified > DateTime.MinValue ? "added " + Day(m.Modified) : null, m.Error);
                bool inUse = string.Equals(cur, "engine:" + m.Path, StringComparison.OrdinalIgnoreCase);
                if (inUse) it.Badges.Add(new KeyValuePair<string, int>("In use", 1));
                if (!m.Incomplete) it.Badges.Add(FitBadge(m.TotalBytes, shape));
                it.Traits.Add(m.ToolTemplate ? "tools" : "notools");
                if (m.ProjectorPath != null) it.Traits.Add("vision");
                if (m.Thinking) it.Traits.Add("thinking");
                if (!m.Incomplete && !inUse) it.Buttons.Add("Use");
                it.Buttons.Add("Folder");
                if (m.OwnDownload) it.Buttons.Add("Delete");
                it.Tag = m;
                it.Selected = keep != null && string.Equals(keep, m.Path, StringComparison.OrdinalIgnoreCase);
                local.Items.Add(it);
            }
            local.Changed();
            PerformLayout();
        }

        void OnLocalButton(RowItem it, string b)
        {
            DownloadJob job = it.Tag as DownloadJob;
            if (job != null)
            {
                if (b == "Cancel") { job.D.Cancel = true; it.ProgressText = "Cancelling..."; local.Invalidate(); }
                return;
            }
            LocalModel m = (LocalModel)it.Tag;
            if (b == "Use")
            {
                if (!Engine.Instance.Installed)
                {
                    Alert.Show(this, Th, "Set up the built-in engine first (the button at the top), then choose this model.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (ModelChosen != null) ModelChosen("engine:" + m.Path);
                RefreshLocal(false);
            }
            else if (b == "Folder")
            {
                try { Process.Start(Native.WindowsExe("explorer.exe"), "/select,\"" + m.Path + "\""); } catch { }
            }
            else if (b == "Delete")
            {
                if (!Alert.Confirm(this, Th, "Delete " + m.DisplayName + " (" + ModelCatalog.Size(m.TotalBytes) + ")?", Text, "Delete", true)) return;
                if ((Engine.Instance.Running || Engine.Instance.Starting) && string.Equals(Engine.Instance.ModelPath, m.Path, StringComparison.OrdinalIgnoreCase)) Engine.Instance.Stop();
                string err;
                if (!ModelLibrary.DeleteOwn(m, out err)) Alert.Show(this, Th, "Could not delete it: " + err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshLocal(true);
            }
        }

        void Search()
        {
            string q = search.Box.Text.Trim();
            int seq = ++searchSeq;
            if (searchCt != null) searchCt.Cancel();
            CancelToken ct = new CancelToken();
            searchCt = ct;
            bool suggest = q.Length == 0;
            repos.Items.Clear();
            repos.EmptyText = suggest ? "Finding models that suit this PC..." : "Searching...";
            repos.ScrollToTop();
            repos.Changed();
            Thread t = new Thread(delegate ()
            {
                string err = null;
                List<HubRepo> res;
                HashSet<string> good = null;
                bool fallback = false;
                try
                {
                    if (suggest)
                    {
                        res = Hub.Suggested(ct, out err);
                        if (res.Count == 0 && !ct.IsCancelled)
                        {
                            string err2;
                            List<HubRepo> top = Hub.Search("", ct, out err2);
                            if (top.Count > 0) { res = top; err = null; suggest = false; fallback = true; }
                        }
                    }
                    else
                    {
                        HashSet<string> sizes = null;
                        Thread side = new Thread(delegate () { try { sizes = Hub.GoodSizes(q, ct); } catch { } });
                        side.IsBackground = true;
                        side.Start();
                        res = Hub.Search(q, ct, out err);
                        side.Join(20000);
                        good = sizes;
                    }
                }
                catch (Exception ex) { res = new List<HubRepo>(); err = ex.Message; }
                if (ct.IsCancelled) return;
                if (good != null && good.Count > 0)
                {
                    List<HubRepo> first = new List<HubRepo>(), rest = new List<HubRepo>();
                    foreach (HubRepo r in res) { r.GoodSize = good.Contains(r.Id); if (r.GoodSize) first.Add(r); else rest.Add(r); }
                    first.AddRange(rest);
                    res = first;
                }
                Post(delegate
                {
                    if (seq != searchSeq) return;
                    repos.Items.Clear();
                    if (err != null && Net.Offline) repos.EmptyText = "Offline mode is on (Settings > Privacy), so MinGent doesn't connect to Hugging Face. The models you already have are under On this PC.";
                    else if (err != null) repos.EmptyText = (suggest ? "Could not load suggestions from Hugging Face: " : "Could not search Hugging Face: ") + err;
                    else repos.EmptyText = suggest ? "No suggestions right now. Search for a model by name, for example \"qwen\" or \"gemma\"." : "Nothing found. Try a shorter name.";
                    double lo, hi;
                    Hub.ParamRange(out lo, out hi);
                    reposCaption.Text = fallback ? "Most downloaded on Hugging Face" : suggest ? "Popular now, sized for this PC (" + lo.ToString("0", CultureInfo.InvariantCulture) + "B to " + hi.ToString("0", CultureInfo.InvariantCulture) + "B)" : "";
                    reposCaption.Visible = (suggest || fallback) && res.Count > 0;
                    foreach (HubRepo r in res)
                    {
                        RowItem it = new RowItem();
                        it.Title = r.Name;
                        string published = r.Created > DateTime.MinValue ? "published " + Month(r.Created) : null;
                        string updated = r.Modified > DateTime.MinValue && (r.Created == DateTime.MinValue || (r.Modified - r.Created).TotalDays >= 7) ? "updated " + Ago(r.Modified) : null;
                        it.Sub = ModelCatalog.Join("  \u00B7  ", r.SizeText, "by " + r.Author, Count(r.Downloads) + " downloads", published, updated);
                        if (r.GoodSize && !suggest) it.Badges.Add(new KeyValuePair<string, int>("Suits this PC", 3));
                        string trust = r.TrustLabel;
                        if (trust.Length > 0) it.Badges.Add(new KeyValuePair<string, int>(trust, trust == "Few downloads" ? 2 : 1));
                        if (r.Gated) it.Badges.Add(new KeyValuePair<string, int>("Needs login", 2));
                        if (r.TemplateKnown) it.Traits.Add(r.Tools ? "tools" : "notools");
                        if (r.Vision) it.Traits.Add("vision");
                        if (r.Thinking) it.Traits.Add("thinking");
                        it.Tag = r;
                        repos.Items.Add(it);
                    }
                    repos.Changed();
                    PerformLayout();
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void UpdateMore()
        {
            int n = variants.Items.Count;
            moreToggle.Visible = n > 0 && reco.Items.Count > 0;
            moreToggle.Text = showAll ? "Hide other sizes" : "Other sizes (" + n + ")";
            moreToggle.ChevronUp = showAll;
            variants.Visible = showAll || reco.Items.Count == 0;
            moreToggle.Invalidate();
        }

        static string Count(long n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (n >= 1000) return (n / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return n.ToString(CultureInfo.InvariantCulture);
        }

        static string Month(DateTime utc) { return utc.ToLocalTime().ToString("MMM yyyy", CultureInfo.InvariantCulture); }

        static string Day(DateTime utc) { return utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture); }

        string Needs(long bytes, GgufShape shape)
        {
            if (shape == null || !shape.Known || bytes <= 0) return null;
            int ctx = shape.Context > 0 ? Math.Min(32768, shape.Context) : 32768;
            return "needs ~" + Gb(Hub.NeedBytes(bytes, shape, ctx, KvPerElem)) + " at " + Tokens(ctx);
        }

        static string Ago(DateTime utc)
        {
            TimeSpan d = DateTime.UtcNow - utc;
            if (d.TotalDays < 1) return "today";
            if (d.TotalDays < 2) return "yesterday";
            if (d.TotalDays < 60) return (int)d.TotalDays + " days ago";
            if (d.TotalDays < 730) return (int)(d.TotalDays / 30) + " months ago";
            return (int)(d.TotalDays / 365) + " years ago";
        }

        void ShowVariants(RowItem it)
        {
            foreach (RowItem r in repos.Items) r.Selected = r == it;
            repos.Invalidate();
            HubRepo repo = (HubRepo)it.Tag;
            shownRepo = repo;
            showAll = false;
            variantsTitle.Text = repo.Name;
            shownShape = null;
            detailSub.Text = DetailLine(repo, null);
            detailDesc.Text = "";
            pageLink.Visible = true;
            reco.Items.Clear();
            reco.Visible = false;
            recoHint.Text = "";
            variants.Items.Clear();
            variants.EmptyText = "Loading the file list...";
            variants.Visible = true;
            variants.Changed();
            UpdateMore();
            PerformLayout();
            Thread t = new Thread(delegate ()
            {
                string err;
                List<HubVariant> vs;
                List<LocalModel> have;
                try { vs = Hub.Variants(repo.Id, new CancelToken(), out err); }
                catch (Exception ex) { vs = new List<HubVariant>(); err = ex.Message; }
                try { have = ModelLibrary.Scan(false); } catch { have = new List<LocalModel>(); }
                GgufShape shape = null;
                try { shape = Hub.Shape(repo.Id, vs, new CancelToken()); } catch { }
                Thread side = new Thread(delegate ()
                {
                    string desc = null;
                    try { desc = Hub.Describe(repo.Id, repo.BaseModel, new CancelToken()); } catch { }
                    if (desc != null) Post(delegate { if (shownRepo == repo) { detailDesc.Text = desc; PerformLayout(); } });
                });
                side.IsBackground = true;
                side.Start();
                double bpe = Hub.KvBytesPerElem(s.EngineKv);
                int room;
                HubVariant best = Hub.Recommend(vs, shape, bpe, s.EngineContext, out room);
                Post(delegate
                {
                    if (shownRepo != repo) return;
                    shownShape = shape;
                    detailSub.Text = DetailLine(repo, shape);
                    reco.Items.Clear();
                    variants.Items.Clear();
                    variants.EmptyText = err != null ? "Could not list the files: " + err : "This repository has no GGUF files.";
                    foreach (HubVariant v in vs)
                    {
                        RowItem r = VariantRow(repo, v, have, v == best, shape);
                        if (v == best) reco.Items.Add(r); else variants.Items.Add(r);
                    }
                    reco.Visible = reco.Items.Count > 0;
                    if (best != null)
                    {
                        string fit = Hub.Fit(best.TotalBytes, shape, bpe);
                        string roomText = room <= 0 ? null : shape != null && shape.Context > 0 && room >= shape.Context ? "its full " + Tokens(shape.Context) + " context" : "about " + Tokens(room) + " tokens of chat";
                        recoHint.Text = (fit == "fits" || fit == "fitsram") && roomText != null ? (fit == "fits" ? "Best for this PC: the highest quality that fits in your graphics memory, with room for " : "Best for this PC: a good balance of speed and quality on the processor, with room for ") + roomText + "." :
                                        fit == "fits" || fit == "fitsram" ? "Recommended for this PC: the best quality that still leaves room for a long chat." :
                                        fit == "partial" ? "This model is larger than your graphics memory, so part of it runs from system memory and it will be slower." :
                                        fit == "tight" ? "This model needs most of this PC's memory, so it may be slow and leave little room for other apps." :
                                        "Even the smallest size of this model is too big for this PC.";
                    }
                    reco.Changed();
                    variants.Changed();
                    UpdateMore();
                    PerformLayout();
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        static string DetailLine(HubRepo repo, GgufShape shape)
        {
            return ModelCatalog.Join("  \u00B7  ", "by " + repo.Author, repo.TrustLabel.Length > 0 ? repo.TrustLabel : null, Count(repo.Downloads) + " downloads",
                                     repo.Created > DateTime.MinValue ? "published " + Day(repo.Created) : null,
                                     shape != null && shape.Context > 0 ? "up to " + Tokens(shape.Context) + " context" : null,
                                     repo.License != null ? "license: " + repo.License : null);
        }

        static string Tokens(int n)
        {
            if (n >= 1024) return (n / 1024).ToString(CultureInfo.InvariantCulture) + "K";
            return n.ToString(CultureInfo.InvariantCulture);
        }

        RowItem VariantRow(HubRepo repo, HubVariant v, List<LocalModel> have, bool recommended, GgufShape shape)
        {
            RowItem r = new RowItem();
            r.Title = v.Quant.Length > 0 ? v.Quant : v.Label;
            r.Sub = ModelCatalog.Join("  \u00B7  ", ModelCatalog.Size(v.TotalBytes), Needs(v.TotalBytes, shape), QuantNote(v.Quant), v.Projector != null ? "can see images" : null);
            if (recommended) r.Badges.Add(new KeyValuePair<string, int>("Best for this PC", 1));
            r.Badges.Add(FitBadge(v.TotalBytes, shape));
            bool verified = true;
            foreach (HubFile f in v.Files) if (f.Sha256.Length == 0) verified = false;
            if (!verified) r.Badges.Add(new KeyValuePair<string, int>("No checksum", 2));
            string localPath = ModelLibrary.LocalPathFor(repo.Id, v, have);
            r.Tag = new object[] { repo, v, localPath };
            DownloadJob job = ModelDownloads.Find(repo.Id, v);
            if (job != null && (!job.Finished || job.Error != null)) ApplyJob(r, job);
            else r.Buttons.Add(localPath != null ? "Use" : "Download");
            return r;
        }

        void ApplyJob(RowItem r, DownloadJob job)
        {
            object[] tag = r.Tag as object[];
            r.Buttons.Clear();
            if (!job.Finished)
            {
                r.Progress = job.Progress;
                r.ProgressText = job.D.Cancel ? "Cancelling..." : job.Text;
                r.Buttons.Add("Cancel");
                return;
            }
            r.Progress = -1;
            if (job.Error != null)
            {
                r.Sub = job.Error;
                r.Buttons.Add("Download");
                return;
            }
            if (tag != null) tag[2] = job.LocalPath;
            r.Sub = job.Text + (job.LocalPath != null ? "  \u00B7  " + job.LocalPath : "");
            r.Buttons.Add(job.LocalPath != null ? "Use" : "Download");
        }

        void OnJobChanged(DownloadJob job)
        {
            Post(delegate
            {
                if (IsDisposed) return;
                foreach (RowList list in new RowList[] { reco, variants })
                {
                    foreach (RowItem r in list.Items)
                    {
                        object[] tag = r.Tag as object[];
                        if (tag == null || !job.Is(((HubRepo)tag[0]).Id, tag[1] as HubVariant)) continue;
                        ApplyJob(r, job);
                    }
                    list.Invalidate();
                }
                bool shown = false;
                foreach (RowItem r in local.Items)
                {
                    if (r.Tag != job) continue;
                    shown = true;
                    r.Progress = job.Progress;
                    r.ProgressText = job.D.Cancel ? "Cancelling..." : job.Text;
                }
                if (job.Finished || !shown) RefreshLocal(job.Finished);
                else local.Invalidate();
            });
        }

        void UpdateFooter()
        {
            footer.Text = "Downloads come from huggingface.co and are checked against its checksums when it lists them. New models are saved in " + ModelLibrary.OwnRoot + ".";
        }

        void ChooseFolder()
        {
            string err;
            if (ModelFolder.Choose(this, Th, s, out err))
            {
                UpdateFooter();
                RefreshLocal(true);
                PerformLayout();
            }
            else if (err != null) Alert.Show(this, Th, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }


        static string QuantNote(string q)
        {
            string u = (q ?? "").ToUpperInvariant();
            if (u.StartsWith("Q4_K_M")) return "good balance";
            if (u.StartsWith("Q5_K_M") || u.StartsWith("Q6_K")) return "higher quality";
            if (u.StartsWith("Q8")) return "near-original quality";
            if (u.StartsWith("Q3") || u.StartsWith("Q2") || u.StartsWith("IQ2") || u.StartsWith("IQ1")) return "smallest, lower quality";
            if (u.StartsWith("BF16") || u.StartsWith("F16") || u.StartsWith("F32")) return "unquantized, very large";
            return null;
        }

        void OnVariantButton(RowItem it, string b)
        {
            object[] tag = (object[])it.Tag;
            HubRepo repo = (HubRepo)tag[0];
            HubVariant v = (HubVariant)tag[1];
            if (b == "Use")
            {
                string path = tag[2] as string;
                if (path == null) return;
                if (!Engine.Instance.Installed)
                {
                    Alert.Show(this, Th, "Set up the built-in engine first (the button at the top), then choose this model.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (ModelChosen != null) ModelChosen("engine:" + path);
                return;
            }
            if (b == "Cancel")
            {
                DownloadJob running = ModelDownloads.Find(repo.Id, v);
                if (running != null && !running.Finished) { running.D.Cancel = true; it.ProgressText = "Cancelling..."; RefreshRowsFor(it); }
                return;
            }
            if (b != "Download") return;
            string fit = Hub.Fit(v.TotalBytes, shownRepo == repo ? shownShape : null, KvPerElem);
            if (fit == "toobig" && !Alert.Ask(this, Th, "This file (" + ModelCatalog.Size(v.TotalBytes) + ") is larger than this PC's graphics and system memory can hold, so it will likely not run.", Text, "Download anyway")) return;
            try
            {
                Directory.CreateDirectory(ModelLibrary.OwnRoot);
                DriveInfo di = new DriveInfo(Path.GetPathRoot(ModelLibrary.OwnRoot));
                long need = v.TotalBytes + (v.Projector != null ? v.Projector.Size : 0);
                long pending = ModelDownloads.PendingBytes();
                if (di.AvailableFreeSpace < need + pending + 512L * 1024 * 1024)
                {
                    Alert.Show(this, Th, "There is not enough free disk space on " + di.Name + " (" + ModelCatalog.Size(di.AvailableFreeSpace) + " free, " + ModelCatalog.Size(need) + " needed" +
                               (pending > 0 ? ", plus " + ModelCatalog.Size(pending) + " for the downloads already running" : "") + "). You can pick another folder with Change folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                Alert.Show(this, Th, "MinGent can't use the model folder " + ModelLibrary.OwnRoot + ": " + ex.Message + "\n\nPick another folder with Change folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DownloadJob job = ModelDownloads.Start(repo, v);
            ApplyJob(it, job);
            RefreshRowsFor(it);
            RefreshLocal(false);
        }

        void RefreshRowsFor(RowItem it)
        {
            reco.Invalidate();
            variants.Invalidate();
            local.Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (installing && installCt != null) installCt.Cancel();
            typing.Stop();
            if (searchCt != null) searchCt.Cancel();
            base.OnFormClosing(e);
        }
    }

    static class ModelFolder
    {
        public static bool Choose(IWin32Window owner, Theme th, Settings s, out string error)
        {
            error = null;
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Choose where MinGent saves the models it downloads. Models already downloaded stay where they are and keep working.";
                d.ShowNewFolderButton = true;
                try { if (Directory.Exists(ModelLibrary.OwnRoot)) d.SelectedPath = ModelLibrary.OwnRoot; } catch { }
                if (d.ShowDialog(owner) != DialogResult.OK) return false;
                return Set(s, d.SelectedPath, out error);
            }
        }

        public static bool Set(Settings s, string folder, out string error)
        {
            error = ModelLibrary.CheckFolder(folder);
            if (error != null) return false;
            string full = Path.GetFullPath(folder).TrimEnd('\\', '/');
            if (string.Equals(full, ModelLibrary.OwnRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return false;
            bool isDefault = string.Equals(full, ModelLibrary.DefaultRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            List<string> olds = new List<string>();
            if (!string.IsNullOrEmpty(s.ModelsFolder)) olds.Add(s.ModelsFolder);
            foreach (string o in (s.ModelsOldFolders ?? "").Split('|'))
                if (o.Trim().Length > 0 && !olds.Exists(delegate (string x) { return string.Equals(x, o.Trim(), StringComparison.OrdinalIgnoreCase); })) olds.Add(o.Trim());
            olds.RemoveAll(delegate (string x) { return string.Equals(x.TrimEnd('\\', '/'), full, StringComparison.OrdinalIgnoreCase); });
            if (olds.Count > 5) olds.RemoveRange(5, olds.Count - 5);
            s.ModelsFolder = isDefault ? "" : full;
            s.ModelsOldFolders = string.Join("|", olds.ToArray());
            s.Save();
            ModelLibrary.Apply(s);
            return true;
        }
    }
}
