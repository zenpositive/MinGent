using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MinGent
{
    static class Tk
    {
        public const byte Text = 0, Keyword = 1, Type = 2, Number = 3, String = 4, Comment = 5, Preproc = 6,
            Function = 7, Tag = 8, Attr = 9, Variable = 10, Constant = 11, Heading = 12, SelText = 13, Count = 14;
    }

    class Theme
    {
        public string Name;
        public bool Dark, HighContrast;

        public Color Back, Fore, LineNum, LineNumActive, CurLine, Sel, SelInactive, SelFore, Caret;
        public Color FindHi, FindBorder, FindMark, WordHi, BraceBack, BraceBorder, ModUnsaved, ModSaved;

        public Color Chrome, ChromeFore, ChromeDim, TabHover, TabBorder, Accent, Border;
        public Color MenuBack, MenuFore, MenuHover, MenuHoverFore, MenuDisabled;
        public Color StatusBack, StatusFore, StatusBorder;
        public Color InputBack, InputBorder, ButtonHover, ButtonPressed, ToggleOn;
        public Color Thumb, ThumbHover, Error;
        public Color Side, SideHover, SideSel, Pane;
        public Color Card, CardBorder, CardHover, Ctl, CtlHover, CtlBorder, Well, WellBorder, Track, OnAccent, Good;

        public Color Caption, CaptionText, WindowBorder;
        public Color[] Syn = new Color[Tk.Count];

        void StatusLikeChrome()
        {
            StatusBack = Chrome;
            StatusFore = ChromeDim;
            StatusBorder = Border;
        }

        static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        public static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Theme Light(Color accent)
        {
            Theme t = new Theme();
            t.Name = "light";
            t.Dark = false;
            t.Back = C(0xFFFFFF); t.Fore = C(0x1F1F1F);
            t.LineNum = C(0x9A9A9A); t.LineNumActive = C(0x2B2B2B);
            t.CurLine = C(0xEFF3F9); t.Sel = C(0xADD6FF); t.SelInactive = C(0xCFDDEE); t.SelFore = Color.Empty;
            t.Caret = C(0x000000);
            t.FindHi = C(0xFFE28A); t.WordHi = C(0xD5ECCB);
            t.BraceBack = C(0xDCEFD9); t.BraceBorder = C(0x8E8E8E);
            t.ModUnsaved = C(0xF0A030); t.ModSaved = C(0x4CAF50);
            t.Chrome = C(0xF3F3F3); t.ChromeFore = C(0x1F1F1F); t.ChromeDim = C(0x5F5F5F);
            t.TabHover = C(0xE6E6E6); t.TabBorder = C(0xDDDDDD); t.Border = C(0xDADADA);
            t.MenuBack = C(0xF9F9F9); t.MenuFore = C(0x1F1F1F); t.MenuHover = C(0xE3E3E3); t.MenuDisabled = C(0xA0A0A0);
            t.InputBack = C(0xFFFFFF); t.InputBorder = C(0xC4C4C4); t.ButtonHover = C(0xE2E2E2); t.ButtonPressed = C(0xD2D2D2);
            t.Thumb = C(0xC9C9C9); t.ThumbHover = C(0x9E9E9E); t.Error = C(0xC42B1C);
            t.Accent = accent;
            t.ToggleOn = Blend(accent, C(0xFFFFFF), 0.78);
            t.FindMark = C(0xE2A400);
            t.StatusLikeChrome();
            t.Side = C(0xF7F7F7); t.SideHover = C(0xECECEC); t.SideSel = C(0xE3E3E3); t.Pane = C(0xFFFFFF);
            t.Card = C(0xFFFFFF); t.CardBorder = C(0xE4E4E4); t.CardHover = C(0xF5F5F5);
            t.Ctl = C(0xFFFFFF); t.CtlHover = C(0xF3F3F3); t.CtlBorder = C(0xD0D0D0);
            t.Well = C(0xFBFBFB); t.WellBorder = C(0x8A8A8A); t.Track = C(0xE8E8E8);
            t.OnAccent = C(0xFFFFFF); t.Good = C(0x16A34A);
            t.Caption = t.Chrome; t.CaptionText = t.ChromeFore; t.WindowBorder = Color.Empty; t.FindBorder = Color.Empty;
            int[] s = { 0x1F1F1F, 0x0000FF, 0x267F99, 0x098658, 0xA31515, 0x008000, 0xAF00DB,
                        0x795E26, 0x800000, 0xE50000, 0x001080, 0x0000FF, 0x0451A5, 0x1F1F1F };
            for (int i = 0; i < s.Length; i++) t.Syn[i] = C(s[i]);
            return t;
        }

        public static Theme DarkTheme(Color accent)
        {
            Theme t = new Theme();
            t.Name = "dark";
            t.Dark = true;
            t.Back = C(0x1E1E1E); t.Fore = C(0xD4D4D4);
            t.LineNum = C(0x6E7681); t.LineNumActive = C(0xD0D0D0);
            t.CurLine = C(0x2A2A2A); t.Sel = C(0x264F78); t.SelInactive = C(0x3A4556); t.SelFore = Color.Empty;
            t.Caret = C(0xD8D8D8);
            t.FindHi = C(0x6B4E16); t.WordHi = C(0x36493A);
            t.BraceBack = C(0x2C3A2E); t.BraceBorder = C(0x8A8A8A);
            t.ModUnsaved = C(0xD99A33); t.ModSaved = C(0x57A64A);
            t.Chrome = C(0x202020); t.ChromeFore = C(0xE4E4E4); t.ChromeDim = C(0x9D9D9D);
            t.TabHover = C(0x2D2D2D); t.TabBorder = C(0x333333); t.Border = C(0x3A3A3A);
            t.MenuBack = C(0x2B2B2B); t.MenuFore = C(0xE6E6E6); t.MenuHover = C(0x3D3D3D); t.MenuDisabled = C(0x7A7A7A);
            t.InputBack = C(0x2B2B2B); t.InputBorder = C(0x4A4A4A); t.ButtonHover = C(0x383838); t.ButtonPressed = C(0x454545);
            t.Thumb = C(0x4A4A4A); t.ThumbHover = C(0x6A6A6A); t.Error = C(0xF48771);
            t.Accent = accent;
            t.ToggleOn = Blend(accent, C(0x202020), 0.62);
            t.FindMark = C(0xC8963E);
            t.StatusLikeChrome();
            t.Side = C(0x181818); t.SideHover = C(0x252525); t.SideSel = C(0x2C2C2C); t.Pane = C(0x1E1E1E);
            t.Card = C(0x2A2A2A); t.CardBorder = C(0x343434); t.CardHover = C(0x313131);
            t.Ctl = C(0x363636); t.CtlHover = C(0x3E3E3E); t.CtlBorder = C(0x484848);
            t.Well = C(0x1E1E1E); t.WellBorder = C(0x767676); t.Track = C(0x1C1C1C);
            t.OnAccent = C(0x0B0B0B); t.Good = C(0x3DDC84);
            t.Caption = t.Chrome; t.CaptionText = t.ChromeFore; t.WindowBorder = Color.Empty; t.FindBorder = Color.Empty;
            int[] s = { 0xD4D4D4, 0x569CD6, 0x4EC9B0, 0xB5CEA8, 0xCE9178, 0x6A9955, 0xC586C0,
                        0xDCDCAA, 0x569CD6, 0x9CDCFE, 0x9CDCFE, 0x569CD6, 0x569CD6, 0xD4D4D4 };
            for (int i = 0; i < s.Length; i++) t.Syn[i] = C(s[i]);
            return t;
        }

        public static Theme HighContrastTheme()
        {
            Theme t = new Theme();
            t.Name = "highcontrast";
            t.HighContrast = true;
            t.Dark = SystemColors.Window.GetBrightness() < 0.5f;
            t.Back = SystemColors.Window; t.Fore = SystemColors.WindowText;
            t.LineNum = SystemColors.GrayText; t.LineNumActive = SystemColors.WindowText;
            t.CurLine = SystemColors.Window; t.Sel = SystemColors.Highlight; t.SelInactive = SystemColors.Highlight;
            t.SelFore = SystemColors.HighlightText; t.Caret = SystemColors.WindowText;
            t.FindHi = SystemColors.Window; t.WordHi = SystemColors.Window;
            t.BraceBack = SystemColors.Window; t.BraceBorder = SystemColors.WindowText;
            t.ModUnsaved = SystemColors.Highlight; t.ModSaved = SystemColors.GrayText;
            t.Chrome = SystemColors.Control; t.ChromeFore = SystemColors.ControlText; t.ChromeDim = SystemColors.ControlText;
            t.TabHover = SystemColors.Control; t.TabBorder = SystemColors.ControlText; t.Border = SystemColors.ControlText;
            t.MenuBack = SystemColors.Menu; t.MenuFore = SystemColors.MenuText; t.MenuHover = SystemColors.Highlight; t.MenuDisabled = SystemColors.GrayText;
            t.InputBack = SystemColors.Window; t.InputBorder = SystemColors.WindowText;
            t.ButtonHover = SystemColors.Control; t.ButtonPressed = SystemColors.ControlDark; t.ToggleOn = SystemColors.Highlight;
            t.Thumb = SystemColors.ControlDark; t.ThumbHover = SystemColors.ControlText; t.Error = SystemColors.WindowText;
            t.Accent = SystemColors.Highlight;
            for (int i = 0; i < Tk.Count; i++) t.Syn[i] = SystemColors.WindowText;
            t.Syn[Tk.SelText] = SystemColors.HighlightText;
            t.FindMark = SystemColors.WindowText;
            t.MenuHoverFore = SystemColors.HighlightText;
            t.StatusLikeChrome();
            t.StatusFore = SystemColors.ControlText;
            t.Side = SystemColors.Control; t.SideHover = SystemColors.Control; t.SideSel = SystemColors.Highlight; t.Pane = SystemColors.Window;
            t.Card = SystemColors.Window; t.CardBorder = SystemColors.WindowText; t.CardHover = SystemColors.Window;
            t.Ctl = SystemColors.ButtonFace; t.CtlHover = SystemColors.ButtonFace; t.CtlBorder = SystemColors.ControlText;
            t.Well = SystemColors.Window; t.WellBorder = SystemColors.WindowText; t.Track = SystemColors.Window;
            t.OnAccent = SystemColors.HighlightText; t.Good = SystemColors.WindowText;
            return t;
        }

        public static bool SystemUsesDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AppsUseLightTheme");
                        if (v is int) return (int)v == 0;
                    }
                }
            }
            catch { }
            return false;
        }

        public static Color Brand(bool dark) { return dark ? C(0x16A34A) : C(0x15803D); }

        public static Color SystemAccent(bool dark)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    if (k != null)
                    {
                        byte[] pal = k.GetValue("AccentPalette") as byte[];
                        if (pal != null && pal.Length >= 32)
                        {
                            int i = (dark ? 1 : 4) * 4;
                            return Color.FromArgb(255, pal[i], pal[i + 1], pal[i + 2]);
                        }
                    }
                }
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AccentColor");
                        if (v is int)
                        {
                            int c = (int)v;
                            Color col = Color.FromArgb(255, c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
                            return dark ? Blend(col, Color.White, 0.35) : col;
                        }
                    }
                }
            }
            catch { }
            return dark ? Color.FromArgb(0x4C, 0xC2, 0xFF) : Color.FromArgb(0x00, 0x67, 0xC0);
        }
    }

    class MenuColors : ProfessionalColorTable
    {
        readonly Theme t;
        public MenuColors(Theme t) { this.t = t; UseSystemColors = false; }
        public override Color MenuItemSelected { get { return t.MenuHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return t.MenuHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return t.MenuHover; } }
        public override Color MenuItemPressedGradientBegin { get { return t.MenuBack; } }
        public override Color MenuItemPressedGradientMiddle { get { return t.MenuBack; } }
        public override Color MenuItemPressedGradientEnd { get { return t.MenuBack; } }
        public override Color MenuItemBorder { get { return t.MenuHover; } }
        public override Color MenuBorder { get { return t.Border; } }
        public override Color ToolStripDropDownBackground { get { return t.MenuBack; } }
        public override Color ImageMarginGradientBegin { get { return t.MenuBack; } }
        public override Color ImageMarginGradientMiddle { get { return t.MenuBack; } }
        public override Color ImageMarginGradientEnd { get { return t.MenuBack; } }
        public override Color MenuStripGradientBegin { get { return t.Chrome; } }
        public override Color MenuStripGradientEnd { get { return t.Chrome; } }
        public override Color StatusStripGradientBegin { get { return t.StatusBack; } }
        public override Color StatusStripGradientEnd { get { return t.StatusBack; } }
        public override Color ToolStripGradientBegin { get { return t.Chrome; } }
        public override Color ToolStripGradientMiddle { get { return t.Chrome; } }
        public override Color ToolStripGradientEnd { get { return t.Chrome; } }
        public override Color ToolStripBorder { get { return t.Chrome; } }
        public override Color SeparatorDark { get { return t.Border; } }
        public override Color SeparatorLight { get { return t.Border; } }
        public override Color CheckBackground { get { return t.MenuBack; } }
        public override Color CheckSelectedBackground { get { return t.MenuHover; } }
        public override Color CheckPressedBackground { get { return t.MenuHover; } }
        public override Color ButtonSelectedHighlight { get { return t.MenuHover; } }
        public override Color ButtonSelectedGradientBegin { get { return t.MenuHover; } }
        public override Color ButtonSelectedGradientMiddle { get { return t.MenuHover; } }
        public override Color ButtonSelectedGradientEnd { get { return t.MenuHover; } }
        public override Color ButtonSelectedBorder { get { return t.MenuHover; } }
        public override Color ButtonPressedGradientBegin { get { return t.ButtonPressed; } }
        public override Color ButtonPressedGradientMiddle { get { return t.ButtonPressed; } }
        public override Color ButtonPressedGradientEnd { get { return t.ButtonPressed; } }
    }

    class DetailMenuItem : ToolStripMenuItem
    {
        public string Detail = "";

        public DetailMenuItem(string text, string detail) : base(text)
        {
            Detail = detail ?? "";
            if (Detail.Length > 0)
            {
                ShortcutKeyDisplayString = Detail;
                ShowShortcutKeys = true;
            }
        }
    }

    class NoteMenuItem : DetailMenuItem
    {
        public readonly bool Heading;

        public NoteMenuItem(string text, string detail, bool heading) : base(text, detail)
        {
            Heading = heading;
            Enabled = false;
            AccessibleRole = heading ? AccessibleRole.Grouping : AccessibleRole.StaticText;
        }

        public override bool CanSelect { get { return false; } }
    }

    class ThemedRenderer : ToolStripProfessionalRenderer
    {
        readonly Theme t;
        public ThemedRenderer(Theme t) : base(new MenuColors(t)) { this.t = t; RoundedEdges = false; }

        public Theme Th { get { return t; } }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripItem it = e.Item;
            if (!it.IsOnDropDown) { base.OnRenderMenuItemBackground(e); return; }
            if (!it.Selected) return;
            Rectangle hr = new Rectangle(UI.S(3), 0, it.Width - UI.S(6), it.Height);
            if (it.Enabled) Glyphs.FillRound(e.Graphics, t.MenuHover, hr, UI.S(5));
            else Glyphs.DrawRound(e.Graphics, t.HighContrast ? t.MenuFore : t.Border, new Rectangle(hr.X, hr.Y, hr.Width - 1, hr.Height - 1), UI.S(5));
        }

        bool HoverFore(ToolStripItem item)
        {
            return item != null && item.Selected && !t.MenuHoverFore.IsEmpty;
        }

        static Rectangle Content(ToolStripItem it, Rectangle r)
        {
            int top = it.Padding.Top, h = Math.Max(1, it.Height - it.Padding.Vertical);
            return new Rectangle(r.X, top, r.Width, h);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            ToolStripItem it = e.Item;
            if (it is ToolStripMenuItem && it.IsOnDropDown)
            {
                DetailMenuItem dm = it as DetailMenuItem;
                NoteMenuItem note = it as NoteMenuItem;
                bool detail = dm != null && dm.Detail.Length > 0 && e.Text == dm.Detail && e.Text != dm.Text;
                Color c;
                if (note != null) c = t.HighContrast ? t.MenuFore : t.ChromeDim;
                else if (!it.Enabled) c = t.MenuDisabled;
                else if (HoverFore(it)) c = t.MenuHoverFore;
                else c = detail ? t.ChromeDim : t.MenuFore;
                Font font = detail ? Fonts.Note : e.TextFont ?? it.Font;
                TextFormatFlags fl = detail ? TextFormatFlags.Right | TextFormatFlags.NoPrefix : (e.TextFormat & ~(TextFormatFlags.Top | TextFormatFlags.Bottom | TextFormatFlags.WordBreak)) | TextFormatFlags.EndEllipsis;
                TextRenderer.DrawText(e.Graphics, e.Text, font, Content(it, e.TextRectangle), c, fl | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                return;
            }
            if (it is ToolStripStatusLabel) e.TextColor = t.StatusFore;
            else if (it is ToolStripLabel && it.IsOnDropDown && !t.HighContrast) e.TextColor = it.ForeColor;
            else if (!it.Enabled) e.TextColor = t.MenuDisabled;
            else if (it.IsOnDropDown) e.TextColor = HoverFore(it) ? t.MenuHoverFore : t.MenuFore;
            else e.TextColor = (HoverFore(it) && !it.Pressed) ? t.MenuHoverFore : t.ChromeFore;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = HoverFore(e.Item) ? t.MenuHoverFore : t.MenuFore;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            if (e.Item != null && e.Item.IsOnDropDown)
            {
                Rectangle box = Content(e.Item, r);
                r.Y = box.Y + (box.Height - r.Height) / 2;
            }
            Graphics g = e.Graphics;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(HoverFore(e.Item) ? t.MenuHoverFore : t.HighContrast ? t.MenuFore : t.Accent, Math.Max(1.6f, r.Height / 9f)))
            {
                g.DrawLines(p, new PointF[] {
                    new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                    new PointF(r.Left + r.Width * 0.80f, r.Top + r.Height * 0.30f) });
            }
            g.SmoothingMode = old;
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Vertical) { base.OnRenderSeparator(e); return; }
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(t.Border))
                e.Graphics.DrawLine(p, UI.S(10), y, e.Item.Width - UI.S(10), y);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is StatusStrip)
            {
                using (SolidBrush b = new SolidBrush(t.StatusBack)) e.Graphics.FillRectangle(b, e.AffectedBounds);
                return;
            }
            base.OnRenderToolStripBackground(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                if (Native.PopupsRounded) return;
                using (Pen p = new Pen(t.Border))
                    e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
            else if (e.ToolStrip is StatusStrip)
            {
                using (Pen p = new Pen(t.StatusBorder))
                    e.Graphics.DrawLine(p, 0, 0, e.ToolStrip.Width, 0);
            }
        }
    }
}
