using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MinGent
{
    static class PixelIcons
    {
        static readonly Dictionary<string, string[]> Art = new Dictionary<string, string[]>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);

        static PixelIcons()
        {
            Art["ask"] = new string[] {
                "................",
                "................",
                ".wwwwwwwwwwwwww.",
                ".w............w.",
                ".w....gggg....w.",
                ".w...gg..gg...w.",
                ".w.......gg...w.",
                ".w......gg....w.",
                ".w.....gg.....w.",
                ".w............w.",
                ".w.....gg.....w.",
                ".w............w.",
                ".wwwww.wwwwwwww.",
                ".....w.w........",
                ".....ww.........",
                "................"
            };
            Art["bookmark"] = new string[] {
                "................",
                "................",
                "...wwwwwwwwww...",
                "...w........w...",
                "...w.gggggg.w...",
                "...w.gggggg.w...",
                "...w........w...",
                "...w........w...",
                "...w........w...",
                "...w........w...",
                "...w...ww...w...",
                "...w..w..w..w...",
                "...w.w....w.w...",
                "...ww......ww...",
                "................",
                "................"
            };
            Art["branch"] = new string[] {
                "................",
                "................",
                "................",
                "...g........g...",
                "...w........w...",
                ".wwwww....wwwww.",
                "w.....w..w.....w",
                "w.g.g.w..w.g.g.w",
                "w.g.g.w..w.g.g.w",
                "w.....w..w.....w",
                "w.www.w..w.www.w",
                "w.....w..w.....w",
                ".wwwww....wwwww.",
                "................",
                "................",
                "................"
            };
            Art["checklist"] = new string[] {
                "................",
                "................",
                "ggg.............",
                "ggg..wwwwwwwwwww",
                "ggg.............",
                "................",
                "................",
                "ggg.............",
                "ggg..wwwwwwwww..",
                "ggg.............",
                "................",
                "................",
                "www.............",
                "w.w..wwwwwwwwwww",
                "www.............",
                "................"
            };
            Art["chip"] = new string[] {
                "................",
                "....w.w..w.w....",
                "....w.w..w.w....",
                "..wwwwwwwwwwww..",
                "..w..........w..",
                "www..........www",
                "..w...gggg...w..",
                "www...gggg...www",
                "..w...gggg...w..",
                "www...gggg...www",
                "..w..........w..",
                "www..........www",
                "..w..........w..",
                "..wwwwwwwwwwww..",
                "....w.w..w.w....",
                "....w.w..w.w...."
            };
            Art["cursor"] = new string[] {
                "................",
                "...w............",
                "...ww...........",
                "...wgw..........",
                "...wggw.........",
                "...wgggw........",
                "...wggggw.......",
                "...wgggggw......",
                "...wggggggw.....",
                "...wgggggggw....",
                "...wggggwwww....",
                "...wggwggw......",
                "...wgw.wggw.....",
                "...ww...wggw....",
                "........wggw....",
                ".........ww....."
            };
            Art["file"] = new string[] {
                "................",
                "................",
                "..wwwwwwwww.....",
                "..w.......ww....",
                "..w.......w.w...",
                "..w.......wwww..",
                "..w..........w..",
                "..w.ggggggg..w..",
                "..w..........w..",
                "..w.ggggggg..w..",
                "..w..........w..",
                "..w.ggggg....w..",
                "..w..........w..",
                "..w..........w..",
                "..wwwwwwwwwwww..",
                "................"
            };
            Art["folder"] = new string[] {
                "................",
                "................",
                "................",
                ".ggggg..........",
                ".gggggg.........",
                ".wwwwwwwwwwwwww.",
                ".w............w.",
                ".w............w.",
                ".w............w.",
                ".w............w.",
                ".w............w.",
                ".w............w.",
                ".wwwwwwwwwwwwww.",
                "................",
                "................",
                "................"
            };
            Art["gear"] = new string[] {
                "................",
                ".......ww.......",
                "...ww..ww..ww...",
                "...wwwwwwwwww...",
                "....ww....ww....",
                "..www......www..",
                "wwww...gg...wwww",
                "wwww..gggg..wwww",
                "..ww..gggg..ww..",
                "wwww..gggg..wwww",
                "wwww...gg...wwww",
                "..www......www..",
                "....ww....ww....",
                "...wwwwwwwwww...",
                "...ww..ww..ww...",
                ".......ww......."
            };
            Art["globe"] = new string[] {
                "................",
                ".....wwwwww.....",
                "...ww..gg..ww...",
                "..w...g..g...w..",
                ".w...g....g...w.",
                ".w...g....g...w.",
                "wwwwwwwwwwwwwwww",
                "w....g....g....w",
                "w....g....g....w",
                "wwwwwwwwwwwwwwww",
                ".w...g....g...w.",
                ".w...g....g...w.",
                "..w...g..g...w..",
                "...ww..gg..ww...",
                ".....wwwwww.....",
                "................"
            };
            Art["lock"] = new string[] {
                "................",
                "................",
                "................",
                ".....wwwwww.....",
                "....ww....ww....",
                "....w......w....",
                "....w......w....",
                "..wwwwwwwwwwww..",
                "..wwwwwwwwwwww..",
                "..wwwwwggwwwww..",
                "..wwwwwggwwwww..",
                "..wwwwwggwwwww..",
                "..wwwwwwwwwwww..",
                "..wwwwwwwwwwww..",
                "................",
                "................"
            };
            Art["newchat"] = new string[] {
                "................",
                "................",
                "................",
                ".wwwwwwwwwwwwww.",
                ".w............w.",
                ".w......g.....w.",
                ".w......g.....w.",
                ".w....ggggg...w.",
                ".w......g.....w.",
                ".w......g.....w.",
                ".w............w.",
                ".wwwww.wwwwwwww.",
                ".....w.w........",
                ".....ww.........",
                "................",
                "................"
            };
            Art["notes"] = new string[] {
                "................",
                "................",
                "................",
                "...g..g..g..g...",
                "..wwwwwwwwwwww..",
                "..w..........w..",
                "..w.wwwwwwww.w..",
                "..w..........w..",
                "..w.wwwwwwww.w..",
                "..w..........w..",
                "..w.wwwwww...w..",
                "..w..........w..",
                "..w..........w..",
                "..wwwwwwwwwwww..",
                "................",
                "................"
            };
            Art["pencil"] = new string[] {
                "................",
                "................",
                "...........www..",
                "..........wgggw.",
                ".........wgggw..",
                "........wgggw...",
                ".......wgggw....",
                "......wgggw.....",
                ".....wgggw......",
                "....wgggw.......",
                "...wgggw........",
                "...wggw.........",
                "...www..........",
                "..w.............",
                "................",
                "................"
            };
            Art["search"] = new string[] {
                "................",
                "................",
                "...wwwww........",
                "..ww...ww.......",
                ".ww.....ww......",
                ".w.......w......",
                ".w.......w......",
                ".w.......w......",
                ".ww.....ww......",
                "..ww...ww.......",
                "...wwwwwgg......",
                "........ggg.....",
                ".........ggg....",
                "..........ggg...",
                "...........gg...",
                "................"
            };
            Art["shield"] = new string[] {
                "................",
                ".......ww.......",
                ".....ww..ww.....",
                "..www......www..",
                "..w..........w..",
                "..w..........w..",
                "..w.......g..w..",
                "..w......gg..w..",
                "..w.g...gg...w..",
                "..w.gg.gg....w..",
                "...w.ggg....w...",
                "...w..g.....w...",
                "....w......w....",
                ".....w....w.....",
                "......w..w......",
                ".......ww......."
            };
            Art["sliders"] = new string[] {
                "................",
                "................",
                "................",
                "...gg...........",
                "wwwggwwwwwwwwwww",
                "...gg...........",
                "................",
                ".........gg.....",
                "wwwwwwwwwggwwwww",
                ".........gg.....",
                "................",
                ".....gg.........",
                "wwwwwggwwwwwwwww",
                ".....gg.........",
                "................",
                "................"
            };
            Art["terminal"] = new string[] {
                "................",
                "................",
                "................",
                "wwwwwwwwwwwwwwww",
                "wwwwwwwwwwwwwwww",
                "w..............w",
                "w.g............w",
                "w..g...........w",
                "w...g..........w",
                "w..g...........w",
                "w.g...ggggg....w",
                "w..............w",
                "w..............w",
                "wwwwwwwwwwwwwwww",
                "................",
                "................"
            };
            Art["trash"] = new string[] {
                "................",
                "................",
                "......wwww......",
                "..wwwwwwwwwwww..",
                "................",
                "...wwwwwwwwww...",
                "...w..g..g..w...",
                "...w..g..g..w...",
                "...w..g..g..w...",
                "...w..g..g..w...",
                "...w..g..g..w...",
                "...w..g..g..w...",
                "...w........w...",
                "...wwwwwwwwww...",
                "................",
                "................"
            };
            Art["wave"] = new string[] {
                "................",
                "................",
                "......g.........",
                "......g...g.....",
                "......g.w.g.....",
                "..w...g.w.g.....",
                "..w.w.g.w.g.w...",
                "w.w.w.g.w.g.w.w.",
                "w.w.w.g.w.g.w.w.",
                "..w.w.g.w.g.w...",
                "..w...g.w.g.....",
                "......g.w.g.....",
                "......g...g.....",
                "......g.........",
                "................",
                "................"
            };
            Art["film"] = new string[] {
                "................",
                "................",
                "wwwwwwwwwwwwwwww",
                "w..............w",
                "w....g.........w",
                "w....ggg.......w",
                "w....ggggg.....w",
                "w....gggggg....w",
                "w....ggggg.....w",
                "w....ggg.......w",
                "w....g.........w",
                "w..............w",
                "wwwwwwwwwwwwwwww",
                "................",
                "................",
                "................"
            };
            Names["ask"] = "ask";
            Names["film"] = "film";
            Names["bookmark"] = "bookmark";
            Names["branch"] = "branch";
            Names["checklist"] = "checklist";
            Names["chip"] = "chip";
            Names["cursor"] = "cursor";
            Names["file"] = "file";
            Names["folder"] = "folder";
            Names["gear"] = "gear";
            Names["globe"] = "globe";
            Names["library"] = "chip";
            Names["lock"] = "lock";
            Names["map"] = "checklist";
            Names["newchat"] = "newchat";
            Names["notes"] = "notes";
            Names["pencil"] = "pencil";
            Names["search"] = "search";
            Names["shield"] = "shield";
            Names["sliders"] = "sliders";
            Names["terminal"] = "terminal";
            Names["trash"] = "trash";
            Names["wave"] = "wave";
        }

        public static bool Has(string name) { return name != null && Names.ContainsKey(name); }

        static bool Gray(Color c) { return Math.Abs(c.R - c.G) < 24 && Math.Abs(c.G - c.B) < 24 && Math.Abs(c.R - c.B) < 24; }

        public static Color GreenFor(Color ink)
        {
            bool light = (0.2126 * ink.R + 0.7152 * ink.G + 0.0722 * ink.B) > 128;
            return light ? Color.FromArgb(0x3D, 0xDC, 0x84) : Color.FromArgb(0x16, 0xA3, 0x4A);
        }

        public static bool Draw(string name, Graphics g, Rectangle r, Color ink)
        {
            string key;
            string[] art;
            if (name == null || !Names.TryGetValue(name, out key) || !Art.TryGetValue(key, out art)) return false;
            int slot = Math.Min(Math.Min(r.Width, r.Height), UI.S(16));
            if (slot < 14) return false;
            int px = Math.Max(1, slot / 16);
            int size = 16 * px;
            int x0 = r.Left + (r.Width - size) / 2, y0 = r.Top + (r.Height - size) / 2;
            Color green = SystemInformation.HighContrast || !Gray(ink) ? ink : GreenFor(ink);
            SmoothingMode sm = g.SmoothingMode;
            PixelOffsetMode pm = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.None;
            using (SolidBrush bw = new SolidBrush(ink))
            using (SolidBrush bg = new SolidBrush(green))
            {
                for (int y = 0; y < art.Length; y++)
                {
                    string row = art[y];
                    int x = 0;
                    while (x < row.Length)
                    {
                        char c = row[x];
                        if (c != 'w' && c != 'g') { x++; continue; }
                        int end = x;
                        while (end < row.Length && row[end] == c) end++;
                        g.FillRectangle(c == 'w' ? bw : bg, x0 + x * px, y0 + y * px, (end - x) * px, px);
                        x = end;
                    }
                }
            }
            g.SmoothingMode = sm;
            g.PixelOffsetMode = pm;
            return true;
        }
    }
}
