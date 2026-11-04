using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MinGent
{
    class ChatInfo
    {
        public string Path = "", Title = "";
        public DateTime When;
    }

    class Sidebar : Control
    {
        class Row
        {
            public ChatInfo Chat;
            public string Header;
            public bool Title;
            public int Top, H;
        }

        const int ZNew = 1, ZLibrary = 2, ZSettings = 3, ZArchived = 4, ZRow = 10, ZMore = 11;

        Theme th = Theme.Light(Color.SteelBlue);
        readonly FlatButton bCollapse = new FlatButton("", "sidebar", false);
        readonly FlatButton bRailNew = new FlatButton("", "newchat", false);
        readonly FlatButton bRailSearch = new FlatButton("", "search", false);
        readonly FlatButton bRailLibrary = new FlatButton("", "library", false);
        readonly FlatButton bRailSettings = new FlatButton("", "gear", false);
        readonly CueTextBox search = new CueTextBox();
        readonly ThemedTip tip = new ThemedTip();
        readonly Timer spinTimer = new Timer();
        readonly List<ChatInfo> chats = new List<ChatInfo>();
        readonly List<Row> rows = new List<Row>();
        Image logo;
        bool collapsed, running, listHover, archiveView;
        int archivedCount;
        string activePath, filter = "";
        int scroll, contentH, hoverRow = -1, hoverZone, pressRow = -1, pressZone, phase, focusRow = -1;

        public event EventHandler NewChatClicked, SettingsClicked, LibraryClicked, CollapseClicked, DeleteAllClicked;
        public event Action<string> OpenClicked, DeleteClicked, RenameClicked, ArchiveClicked, RestoreClicked;
        public event EventHandler ArchiveViewClicked;

        public bool ArchiveView { get { return archiveView; } }

        public Sidebar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Font = UI.Font;
            AccessibleName = "Chats";
            AccessibleRole = AccessibleRole.List;
            search.BorderStyle = BorderStyle.None;
            search.Font = UI.Font;
            search.Multiline = false;
            search.TextChanged += delegate { filter = search.Text.Trim(); scroll = 0; Rebuild(); };
            search.GotFocus += delegate { Invalidate(); };
            search.LostFocus += delegate { Invalidate(); };
            search.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; search.Text = ""; Focus(); }
                else if (e.KeyCode == Keys.Down) { e.Handled = e.SuppressKeyPress = true; Focus(); MoveFocus(1); }
                else if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; int i = FirstChatRow(); if (i >= 0) Open(rows[i].Chat); }
            };
            search.HandleCreated += delegate { Native.SetCueBanner(search, archiveView ? "Search archived chats" : "Search chats"); };
            Controls.AddRange(new Control[] { bCollapse, bRailNew, bRailSearch, bRailLibrary, bRailSettings, search });
            bCollapse.Click += delegate { if (CollapseClicked != null) CollapseClicked(this, EventArgs.Empty); };
            bRailNew.Click += delegate { if (NewChatClicked != null) NewChatClicked(this, EventArgs.Empty); };
            bRailSearch.Click += delegate { if (CollapseClicked != null) CollapseClicked(this, EventArgs.Empty); BeginInvoke(new Action(delegate { search.Focus(); })); };
            bRailLibrary.Click += delegate { if (LibraryClicked != null) LibraryClicked(this, EventArgs.Empty); };
            bRailSettings.Click += delegate { if (SettingsClicked != null) SettingsClicked(this, EventArgs.Empty); };
            tip.SetToolTip(bRailNew, "New chat (Ctrl+Shift+N)");
            tip.SetToolTip(bRailSearch, "Search chats");
            tip.SetToolTip(bRailLibrary, "Models");
            tip.SetToolTip(bRailSettings, "Settings");
            spinTimer.Interval = 90;
            spinTimer.Tick += delegate { phase++; InvalidateActive(); };
            try
            {
                using (Stream st = typeof(Sidebar).Assembly.GetManifestResourceStream("MinGent.ico"))
                    if (st != null) using (Icon ic = new Icon(st, UI.S(20), UI.S(20))) logo = ic.ToBitmap();
            }
            catch { logo = null; }
            Collapsed = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { spinTimer.Dispose(); tip.Dispose(); if (logo != null) logo.Dispose(); }
            base.Dispose(disposing);
        }

        public static int FullWidth { get { return UI.S(264); } }
        public static int RailWidth { get { return UI.S(52); } }

        public bool Collapsed
        {
            get { return collapsed; }
            set
            {
                collapsed = value;
                search.Visible = !value;
                bRailNew.Visible = bRailSearch.Visible = bRailLibrary.Visible = bRailSettings.Visible = value;
                tip.SetToolTip(bCollapse, value ? "Show the sidebar" : "Hide the sidebar");
                hoverRow = -1;
                hoverZone = 0;
                PerformLayout();
                Invalidate();
            }
        }

        public void SetTheme(Theme t)
        {
            th = t;
            tip.Th = t;
            BackColor = t.Side;
            foreach (FlatButton b in new FlatButton[] { bCollapse, bRailNew, bRailSearch, bRailLibrary, bRailSettings }) b.SetTheme(t);
            search.BackColor = SearchFill;
            search.ForeColor = t.Fore;
            search.CueColor = t.ChromeDim;
            search.MenuTheme = t;
            Invalidate(true);
        }

        Color SearchFill { get { return th.HighContrast ? th.InputBack : th.Dark ? Theme.Blend(th.Side, Color.White, 0.06) : th.InputBack; } }

        public void SetChats(List<ChatInfo> list, string active, bool isRunning)
        {
            SetChats(list, active, isRunning, archivedCount, archiveView);
        }

        public void SetChats(List<ChatInfo> list, string active, bool isRunning, int archived, bool showingArchived)
        {
            chats.Clear();
            if (list != null) chats.AddRange(list);
            activePath = active;
            bool modeChanged = archiveView != showingArchived;
            archivedCount = archived;
            archiveView = showingArchived;
            if (modeChanged)
            {
                scroll = 0;
                focusRow = -1;
                if (search.Text.Length > 0) search.Text = "";
                if (search.IsHandleCreated) Native.SetCueBanner(search, archiveView ? "Search archived chats" : "Search chats");
            }
            SetRunning(isRunning);
            Rebuild();
        }

        public void SetActive(string active, bool isRunning)
        {
            activePath = active;
            SetRunning(isRunning);
            Invalidate();
        }

        void SetRunning(bool on)
        {
            running = on;
            if (on && !spinTimer.Enabled) spinTimer.Start();
            else if (!on && spinTimer.Enabled) spinTimer.Stop();
        }

        public void FocusSearch() { if (!collapsed) search.Focus(); }

        int M { get { return UI.S(10); } }
        int TopH { get { return UI.S(52); } }
        Rectangle NewRect { get { return new Rectangle(M, TopH, Width - M * 2, UI.S(36)); } }
        Rectangle SearchRect { get { return new Rectangle(M, NewRect.Bottom + UI.S(6), Width - M * 2, UI.S(34)); } }
        int ListTop { get { return SearchRect.Bottom + UI.S(10); } }
        Rectangle SettingsRect { get { return new Rectangle(M, Height - UI.S(10) - UI.S(36), Width - M * 2, UI.S(36)); } }
        Rectangle LibraryRect { get { Rectangle s = SettingsRect; return new Rectangle(M, s.Top - UI.S(36), s.Width, UI.S(36)); } }
        bool ShowArchived { get { return archivedCount > 0 || archiveView; } }
        Rectangle ArchivedRect { get { Rectangle l = LibraryRect; return ShowArchived ? new Rectangle(M, l.Top - UI.S(36), l.Width, UI.S(36)) : Rectangle.Empty; } }
        int ListBottom { get { return (ShowArchived ? ArchivedRect.Top : LibraryRect.Top) - UI.S(10); } }
        Rectangle ListRect { get { return new Rectangle(0, ListTop, Width, Math.Max(0, ListBottom - ListTop)); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int b = UI.S(30);
            if (collapsed)
            {
                int x = (Width - b) / 2;
                bCollapse.SetBounds(x, (UI.S(42) - b) / 2, b, b);
                Rectangle nr = NewRect, sr = SearchRect;
                bRailNew.SetBounds(x, nr.Y + (nr.Height - b) / 2, b, b);
                bRailSearch.SetBounds(x, sr.Y + (sr.Height - b) / 2, b, b);
                bRailSettings.SetBounds(x, Height - UI.S(12) - b, b, b);
                bRailLibrary.SetBounds(x, bRailSettings.Top - b - UI.S(6), b, b);
            }
            else
            {
                bCollapse.SetBounds(Width - UI.S(10) - b, (UI.S(42) - b) / 2, b, b);
                Rectangle sr = SearchRect;
                int h = search.PreferredHeight;
                search.SetBounds(sr.X + UI.S(32), sr.Y + (sr.Height - h) / 2, Math.Max(UI.S(20), sr.Width - UI.S(40)), h);
            }
            Rebuild();
        }

        static string GroupOf(DateTime when)
        {
            DateTime today = DateTime.Now.Date;
            DateTime d = when.Date;
            if (d >= today) return "Today";
            if (d >= today.AddDays(-1)) return "Yesterday";
            if (d >= today.AddDays(-7)) return "Previous 7 days";
            if (d >= today.AddDays(-30)) return "Previous 30 days";
            return "Older";
        }

        void Rebuild()
        {
            string focused = RowPath(focusRow);
            rows.Clear();
            int y = 0;
            string last = null;
            if (archiveView)
            {
                Row top = new Row();
                top.Header = "Archived chats";
                top.Title = true;
                top.Top = 0;
                top.H = UI.S(28);
                rows.Add(top);
                y = top.H + UI.S(4);
                last = "";
            }
            foreach (ChatInfo c in chats)
            {
                if (filter.Length > 0 && (c.Title ?? "").IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                string g = GroupOf(c.When);
                if (g != last)
                {
                    Row h = new Row();
                    h.Header = g;
                    h.Top = y + (string.IsNullOrEmpty(last) ? 0 : UI.S(8));
                    h.H = UI.S(26);
                    rows.Add(h);
                    y = h.Top + h.H;
                    last = g;
                }
                Row r = new Row();
                r.Chat = c;
                r.Top = y;
                r.H = UI.S(34);
                rows.Add(r);
                y += r.H;
            }
            contentH = y + UI.S(8);
            ClampScroll();
            if (focused != null)
            {
                focusRow = -1;
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].Chat != null && string.Equals(rows[i].Chat.Path, focused, StringComparison.OrdinalIgnoreCase)) { focusRow = i; break; }
            }
            if (focusRow >= rows.Count) focusRow = -1;
            hoverRow = -1;
            Invalidate();
        }

        void ClampScroll()
        {
            int max = Math.Max(0, contentH - ListRect.Height);
            scroll = Math.Max(0, Math.Min(scroll, max));
        }

        int FirstChatRow()
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i].Chat != null) return i;
            return -1;
        }

        Rectangle RowRect(int i)
        {
            Row r = rows[i];
            return new Rectangle(UI.S(8), ListTop + r.Top - scroll, Width - UI.S(16), r.H);
        }

        Rectangle MoreRect(int i)
        {
            Rectangle r = RowRect(i);
            int s = UI.S(26);
            return new Rectangle(r.Right - s - UI.S(4), r.Y + (r.Height - s) / 2, s, s);
        }

        void InvalidateActive()
        {
            if (collapsed) return;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Chat != null && IsActive(rows[i].Chat)) { Invalidate(RowRect(i)); return; }
        }

        bool IsActive(ChatInfo c) { return activePath != null && string.Equals(c.Path, activePath, StringComparison.OrdinalIgnoreCase); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(th.Side);
            using (Pen p = new Pen(th.Border)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);
            if (collapsed) return;
            int lx = UI.S(16);
            if (logo != null)
            {
                g.DrawImage(logo, new Rectangle(lx, (UI.S(42) - UI.S(20)) / 2, UI.S(20), UI.S(20)));
                lx += UI.S(28);
            }
            using (Font brand = new Font(UI.Font.FontFamily, UI.Font.Size * 1.08f, FontStyle.Bold))
                TextRenderer.DrawText(g, Program.AppName, brand, new Rectangle(lx, 0, Math.Max(10, bCollapse.Left - lx - UI.S(4)), UI.S(42)), th.Fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            PaintAction(g, NewRect, "newchat", "New chat", hoverZone == ZNew ? "Ctrl+Shift+N" : null, hoverZone == ZNew);
            Rectangle sr = SearchRect;
            Color sf = SearchFill;
            Glyphs.FillRound(g, sf, sr, UI.S(8));
            if (search.Focused) Glyphs.Ring(g, th.Accent, sr, UI.S(8), Glyphs.FocusWidth);
            else if (!th.Dark || th.HighContrast) Glyphs.DrawRound(g, th.Border, new Rectangle(sr.X, sr.Y, sr.Width - 1, sr.Height - 1), UI.S(8));
            Glyphs.Draw("search", g, new Rectangle(sr.X + UI.S(8), sr.Y, UI.S(16), sr.Height), th.ChromeDim);
            PaintList(g);
            if (ShowArchived) PaintAction(g, ArchivedRect, archiveView ? "back" : "archive", archiveView ? "Back to chats" : "Archived chats", archiveView ? null : archivedCount.ToString(CultureInfo.CurrentCulture), hoverZone == ZArchived);
            PaintAction(g, LibraryRect, "library", "Models", null, hoverZone == ZLibrary);
            PaintAction(g, SettingsRect, "gear", "Settings", null, hoverZone == ZSettings);
        }

        void PaintAction(Graphics g, Rectangle r, string glyph, string text, string hint, bool hot)
        {
            if (hot) Glyphs.Hover(g, th, th.SideHover, r, UI.S(8));
            Glyphs.Draw(glyph, g, new Rectangle(r.X + UI.S(8), r.Y, UI.S(18), r.Height), th.ChromeFore);
            int tx = r.X + UI.S(34);
            TextRenderer.DrawText(g, text, Font, new Rectangle(tx, r.Y, r.Right - tx - UI.S(8), r.Height), th.Fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (hint != null)
                TextRenderer.DrawText(g, hint, Font, new Rectangle(tx, r.Y, r.Right - tx - UI.S(10), r.Height), th.ChromeDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        void TitleText(Graphics g, string title, Rectangle box, Color fg, Color bg)
        {
            TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;
            if (th.HighContrast)
            {
                TextRenderer.DrawText(g, title, Font, box, fg, f | TextFormatFlags.EndEllipsis);
                return;
            }
            bool fits = TextRenderer.MeasureText(g, title, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width <= box.Width;
            System.Drawing.Drawing2D.GraphicsState st = g.Save();
            g.SetClip(box, System.Drawing.Drawing2D.CombineMode.Intersect);
            TextRenderer.DrawText(g, title, Font, new Rectangle(box.X, box.Y, fits ? box.Width : box.Width + UI.S(400), box.Height), fg, f);
            g.Restore(st);
            if (fits) return;
            int fw = Math.Min(UI.S(28), box.Width / 2);
            Rectangle fade = new Rectangle(box.Right - fw, box.Y + 1, fw, box.Height - 2);
            using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(fade.X - 1, fade.Y, fade.Width + 1, fade.Height), Color.FromArgb(0, bg), bg, System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
            {
                b.WrapMode = System.Drawing.Drawing2D.WrapMode.TileFlipX;
                g.FillRectangle(b, fade);
            }
        }

        void PaintList(Graphics g)
        {
            Rectangle lr = ListRect;
            if (lr.Height <= 0) return;
            System.Drawing.Drawing2D.GraphicsState st = g.Save();
            g.SetClip(lr);
            if (rows.Count == 0)
            {
                string msg = filter.Length > 0 ? "No chats match \u201C" + filter + "\u201D." : archiveView ? "No archived chats." : "Your chats will show up here.";
                TextRenderer.DrawText(g, msg, Font, new Rectangle(UI.S(18), lr.Y + UI.S(4), Width - UI.S(36), UI.S(60)), th.ChromeDim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
            }
            using (Font small = new Font(UI.Font.FontFamily, UI.Font.Size * 0.9f))
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    Rectangle r = RowRect(i);
                    if (r.Bottom < lr.Top || r.Top > lr.Bottom) continue;
                    Row row = rows[i];
                    if (row.Header != null)
                    {
                        if (row.Title)
                        {
                            using (Font bold = new Font(UI.Font.FontFamily, UI.Font.Size, FontStyle.Bold))
                                TextRenderer.DrawText(g, row.Header, bold, new Rectangle(r.X + UI.S(8), r.Y, r.Width - UI.S(16), r.Height), th.Fore,
                                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                            continue;
                        }
                        TextRenderer.DrawText(g, row.Header, small, new Rectangle(r.X + UI.S(8), r.Y, r.Width - UI.S(16), r.Height), th.ChromeDim,
                            TextFormatFlags.Bottom | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                        continue;
                    }
                    bool active = IsActive(row.Chat);
                    bool hot = i == hoverRow;
                    if (active) Glyphs.FillRound(g, th.SideSel, r, UI.S(8));
                    else if (hot) Glyphs.Hover(g, th, th.SideHover, r, UI.S(8));
                    if (i == focusRow && Focused && ShowFocusCues) Glyphs.Ring(g, active && th.HighContrast ? SystemColors.HighlightText : th.Accent, r, UI.S(8), Glyphs.FocusWidth);
                    Color fg = th.HighContrast ? (active ? SystemColors.HighlightText : th.Fore) : active || hot ? th.ChromeFore : Theme.Blend(th.ChromeFore, th.Side, 0.22);
                    int right = r.Right - UI.S(10);
                    Rectangle mr = MoreRect(i);
                    if (hot) right = mr.Left - UI.S(2);
                    else if (active && running) right = r.Right - UI.S(30);
                    string title = row.Chat.Title.Length > 0 ? row.Chat.Title : "Untitled chat";
                    Rectangle tbox = new Rectangle(r.X + UI.S(10), r.Y, Math.Max(10, right - r.X - UI.S(10)), r.Height);
                    TitleText(g, title, tbox, fg, active ? th.SideSel : hot ? th.SideHover : th.Side);
                    if (hot)
                    {
                        bool moreHot = hoverZone == ZMore;
                        if (moreHot)
                        {
                            if (th.HighContrast) Glyphs.DrawRound(g, active ? SystemColors.HighlightText : SystemColors.Highlight, new Rectangle(mr.X, mr.Y, mr.Width - 1, mr.Height - 1), UI.S(6));
                            else Glyphs.FillRound(g, Theme.Blend(active ? th.SideSel : th.SideHover, th.ChromeFore, 0.07), mr, UI.S(6));
                        }
                        Glyphs.Draw("more", g, mr, th.HighContrast ? (active ? SystemColors.HighlightText : th.ChromeFore) : moreHot ? th.ChromeFore : th.ChromeDim);
                    }
                    else if (active && running)
                        Glyphs.Spinner(g, new Rectangle(r.Right - UI.S(26), r.Y + (r.Height - UI.S(18)) / 2, UI.S(18), UI.S(18)), th.HighContrast ? SystemColors.HighlightText : th.Accent, phase);
                }
            }
            g.Restore(st);
            if (listHover && contentH > lr.Height)
            {
                int len = Math.Max(UI.S(24), (int)((long)lr.Height * lr.Height / contentH));
                int max = contentH - lr.Height;
                int ty = lr.Top + (int)((long)(lr.Height - len) * scroll / Math.Max(1, max));
                Glyphs.FillRound(g, th.Thumb, new Rectangle(Width - UI.S(7), ty, UI.S(4), len), UI.S(2));
            }
        }

        void HitTest(Point p, out int row, out int zone)
        {
            row = -1;
            zone = 0;
            if (collapsed) return;
            if (NewRect.Contains(p)) { zone = ZNew; return; }
            if (LibraryRect.Contains(p)) { zone = ZLibrary; return; }
            if (ShowArchived && ArchivedRect.Contains(p)) { zone = ZArchived; return; }
            if (SettingsRect.Contains(p)) { zone = ZSettings; return; }
            if (!ListRect.Contains(p)) return;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Chat == null) continue;
                Rectangle r = RowRect(i);
                if (!r.Contains(p)) continue;
                row = i;
                zone = MoreRect(i).Contains(p) ? ZMore : ZRow;
                return;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row, zone;
            HitTest(e.Location, out row, out zone);
            bool lh = ListRect.Contains(e.Location);
            if (row != hoverRow || zone != hoverZone || lh != listHover)
            {
                hoverRow = row;
                hoverZone = zone;
                listHover = lh;
                Cursor = zone != 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
                RowTip(row, zone);
            }
        }

        string rowTip;

        void RowTip(int row, int zone)
        {
            string want = null;
            if (row >= 0 && row < rows.Count && rows[row].Chat != null && zone != ZMore)
            {
                string title = rows[row].Chat.Title.Length > 0 ? rows[row].Chat.Title : "Untitled chat";
                int room = MoreRect(row).Left - UI.S(2) - (RowRect(row).X + UI.S(10));
                if (TextRenderer.MeasureText(title, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width > room) want = title;
            }
            if (want == rowTip) return;
            rowTip = want;
            tip.Hide(this);
            tip.SetToolTip(this, want);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverRow >= 0 || hoverZone != 0 || listHover) { hoverRow = -1; hoverZone = 0; listHover = false; Invalidate(); }
            RowTip(-1, 0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            HitTest(e.Location, out pressRow, out pressZone);
            pressPath = RowPath(pressRow);
            if (pressRow >= 0) { focusRow = pressRow; Focus(); }
        }

        string pressPath;

        string RowPath(int row)
        {
            return row >= 0 && row < rows.Count && rows[row].Chat != null ? rows[row].Chat.Path : null;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int row, zone;
            HitTest(e.Location, out row, out zone);
            int pr = pressRow, pz = pressZone;
            pressRow = -1;
            pressZone = 0;
            if (row != pr || zone != pz) return;
            if (!string.Equals(RowPath(row), pressPath, StringComparison.OrdinalIgnoreCase)) return;
            if (e.Button == MouseButtons.Right)
            {
                if (row >= 0) ShowRowMenu(row, e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            switch (zone)
            {
                case ZNew: if (NewChatClicked != null) NewChatClicked(this, EventArgs.Empty); break;
                case ZLibrary: if (LibraryClicked != null) LibraryClicked(this, EventArgs.Empty); break;
                case ZArchived: if (ArchiveViewClicked != null) ArchiveViewClicked(this, EventArgs.Empty); break;
                case ZSettings: if (SettingsClicked != null) SettingsClicked(this, EventArgs.Empty); break;
                case ZRow: Open(rows[row].Chat); break;
                case ZMore: { Rectangle m = MoreRect(row); ShowRowMenu(row, new Point(m.Left, m.Bottom + UI.S(2))); } break;
            }
        }

        void Open(ChatInfo c) { if (c != null && OpenClicked != null) OpenClicked(c.Path); }

        void Delete(ChatInfo c) { if (c != null && DeleteClicked != null) DeleteClicked(c.Path); }

        void ShowRowMenu(int row, Point p)
        {
            ChatInfo c = rows[row].Chat;
            if (c == null) return;
            ContextMenuStrip m = Menus.Create(th);
            m.ShowCheckMargin = false;
            if (archiveView)
            {
                m.Items.Add(Menus.Item("Open", false, delegate { Open(c); }));
                m.Items.Add(Menus.Item("Move back to chats", false, delegate { if (RestoreClicked != null) RestoreClicked(c.Path); }));
                m.Items.Add(Menus.Item("Delete chat", false, delegate { Delete(c); }));
            }
            else
            {
                m.Items.Add(Menus.Item("Open", false, delegate { Open(c); }));
                m.Items.Add(Menus.Item("Rename chat...", false, delegate { if (RenameClicked != null) RenameClicked(c.Path); }));
                m.Items.Add(Menus.Item("Archive chat", false, delegate { if (ArchiveClicked != null) ArchiveClicked(c.Path); }));
                m.Items.Add(Menus.Item("Delete chat", false, delegate { Delete(c); }));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(Menus.Item("Delete all chats...", false, delegate { if (DeleteAllClicked != null) DeleteAllClicked(this, EventArgs.Empty); }));
            }
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(this, p);
        }

        readonly WheelAcc wheel = new WheelAcc();

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (collapsed) return;
            scroll -= wheel.Take(e.Delta, UI.S(34) * 3);
            ClampScroll();
            int row, zone;
            HitTest(PointToClient(Cursor.Position), out row, out zone);
            hoverRow = row;
            hoverZone = zone;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End: case Keys.Enter: case Keys.Delete: case Keys.F2: case Keys.Apps: return true;
            }
            return base.IsInputKey(keyData);
        }

        void MoveFocus(int dir)
        {
            if (rows.Count == 0) return;
            int i = focusRow;
            if (i < 0) i = dir > 0 ? -1 : rows.Count;
            do { i += dir; } while (i >= 0 && i < rows.Count && rows[i].Chat == null);
            if (i < 0 || i >= rows.Count) return;
            focusRow = i;
            Rectangle r = RowRect(i);
            Rectangle lr = ListRect;
            if (r.Top < lr.Top) scroll -= lr.Top - r.Top;
            else if (r.Bottom > lr.Bottom) scroll += r.Bottom - lr.Bottom;
            ClampScroll();
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            ChatInfo c = focusRow >= 0 && focusRow < rows.Count ? rows[focusRow].Chat : null;
            switch (e.KeyCode)
            {
                case Keys.Down: MoveFocus(1); e.Handled = true; break;
                case Keys.Up:
                    if (focusRow == FirstChatRow()) { search.Focus(); e.Handled = true; break; }
                    MoveFocus(-1); e.Handled = true; break;
                case Keys.Home: focusRow = -1; MoveFocus(1); e.Handled = true; break;
                case Keys.End: focusRow = rows.Count; MoveFocus(-1); e.Handled = true; break;
                case Keys.Enter: Open(c); e.Handled = true; break;
                case Keys.Delete: Delete(c); e.Handled = true; break;
                case Keys.F2: if (c != null && !archiveView && RenameClicked != null) RenameClicked(c.Path); e.Handled = true; break;
                case Keys.Apps: if (focusRow >= 0 && c != null) { Rectangle r = RowRect(focusRow); ShowRowMenu(focusRow, new Point(r.X + UI.S(20), r.Bottom)); } e.Handled = true; break;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (focusRow < 0) { for (int i = 0; i < rows.Count; i++) if (rows[i].Chat != null && IsActive(rows[i].Chat)) focusRow = i; }
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    class TextPrompt : ThemedForm
    {
        readonly InputBox input;
        readonly TextBox box;

        TextPrompt(Theme t, string title, string label, string value) : base(t, title, 440, 164)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Label l = Dialogs.MakeLabel(label, Th, false);
            l.ForeColor = Th.Fore;
            l.SetBounds(UI.S(22), UI.S(18), ClientSize.Width - UI.S(44), UI.S(22));
            input = MakeField(value, false, false);
            input.SetBounds(UI.S(20), UI.S(46), ClientSize.Width - UI.S(40), UI.S(34));
            box = input.Box;
            Button ok = Dialogs.MakeButton("Save", Th, true);
            Button cancel = Dialogs.MakeButton("Cancel", Th, false);
            cancel.Location = new Point(ClientSize.Width - UI.S(20) - cancel.Width, ClientSize.Height - UI.S(18) - cancel.Height);
            ok.Location = new Point(cancel.Left - UI.S(8) - ok.Width, cancel.Top);
            ok.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { l, input, ok, cancel });
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            box.Focus();
            box.SelectAll();
        }

        public static string Ask(IWin32Window owner, Theme t, string title, string label, string value)
        {
            using (TextPrompt p = new TextPrompt(t, title, label, value))
            {
                if (p.ShowDialog(owner) != DialogResult.OK) return null;
                return p.box.Text.Trim();
            }
        }
    }
}
