using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MinGent
{
    class MItem
    {
        public const byte Glyphs = 0, Fill = 1, Line = 2, Poly = 3, Child = 4;
        public byte Kind;
        public double X, Y, W, H;
        public string Text;
        public Font Font;
        public PointF[] Pts;
        public float Pen;
        public MBox Box;
    }

    class MBox
    {
        public double W, H, D, Ic;
        public bool Char, Ital;
        public List<MItem> Items;

        public bool Empty { get { return Items == null || Items.Count == 0; } }

        void Add(MItem it)
        {
            if (Items == null) Items = new List<MItem>();
            Items.Add(it);
        }

        public void Put(MBox b, double x, double y)
        {
            if (b == null || b.Empty) return;
            MItem it = new MItem();
            it.Kind = MItem.Child;
            it.Box = b;
            it.X = x;
            it.Y = y;
            Add(it);
        }

        public void Glyph(string text, Font f, double x, double y)
        {
            MItem it = new MItem();
            it.Kind = MItem.Glyphs;
            it.Text = text;
            it.Font = f;
            it.X = x;
            it.Y = y;
            Add(it);
        }

        public void Rect(double x, double y, double w, double h)
        {
            MItem it = new MItem();
            it.Kind = MItem.Fill;
            it.X = x;
            it.Y = y;
            it.W = w;
            it.H = h;
            Add(it);
        }

        public void Line(PointF[] pts, double pen)
        {
            MItem it = new MItem();
            it.Kind = MItem.Line;
            it.Pts = pts;
            it.Pen = (float)pen;
            Add(it);
        }

        public void Poly(PointF[] pts)
        {
            MItem it = new MItem();
            it.Kind = MItem.Poly;
            it.Pts = pts;
            Add(it);
        }
    }

    class MathPiece
    {
        public MBox Box;
        public string Text;
        public double Trail;
    }

    class MathResult
    {
        public List<MathPiece> Pieces;
        public string Tag;
        public bool Failed;
    }

    class MathFonts : IDisposable
    {
        readonly ChatFonts chat;
        readonly string serif, symbol;
        public readonly bool Alphabets;
        readonly Dictionary<long, Font> fonts = new Dictionary<long, Font>();
        readonly Dictionary<Font, int> ascents = new Dictionary<Font, int>();
        readonly Dictionary<string, double[]> inks = new Dictionary<string, double[]>(StringComparer.Ordinal);
        readonly Dictionary<int, double> refBase = new Dictionary<int, double>();
        readonly Dictionary<string, bool> covered = new Dictionary<string, bool>(StringComparer.Ordinal);
        readonly Dictionary<string, MathResult> cache = new Dictionary<string, MathResult>(StringComparer.Ordinal);
        public double AxisEm = 0.25, XEm = 0.45, RuleEm = 0.045;
        public float Dpi = 96f;
        bool measured;
        static bool noGdi;
        static readonly StringFormat Typo;
        public static Action<string, Exception> Trace = null;
        public const int MaxTex = 8000;

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern int GetGlyphIndicesW(IntPtr hdc, string s, int c, [Out] ushort[] g, int flags);
        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr h);

        static MathFonts()
        {
            Typo = (StringFormat)StringFormat.GenericTypographic.Clone();
            Typo.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
        }

        public MathFonts(ChatFonts chat)
        {
            this.chat = chat;
            serif = Pick(new string[] { "Cambria", "Caladea", "Times New Roman", "Liberation Serif", "DejaVu Serif" }) ?? chat.Body.FontFamily.Name;
            string math = Pick(new string[] { "Cambria Math", "STIX Two Math", "DejaVu Math TeX Gyre", "Latin Modern Math", "XITS Math" });
            Alphabets = math != null;
            symbol = math ?? Pick(new string[] { "Segoe UI Symbol" }) ?? serif;
        }

        static string Pick(string[] names)
        {
            foreach (string n in names)
            {
                try
                {
                    using (Font f = new Font(n, 10f))
                        if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return n;
                }
                catch (Exception) { }
            }
            return null;
        }

        public string SerifName { get { return serif; } }
        public string SymbolName { get { return symbol; } }

        public Font Font(int face, double pt)
        {
            pt = Math.Max(3.0, Math.Min(160.0, pt));
            int q = pt < 20 ? (int)Math.Round(pt * 4) * 5 : (int)Math.Round(pt) * 20;
            long key = ((long)face << 32) | (uint)q;
            Font f;
            if (fonts.TryGetValue(key, out f)) return f;
            float size = q / 20f;
            string fam;
            FontStyle st = FontStyle.Regular;
            switch (face)
            {
                case Tex.Italic: fam = serif; st = FontStyle.Italic; break;
                case Tex.Roman: fam = serif; break;
                case Tex.Bold: fam = serif; st = FontStyle.Bold; break;
                case Tex.BoldItalic: fam = serif; st = FontStyle.Bold | FontStyle.Italic; break;
                case Tex.Symbol: fam = symbol; break;
                case Tex.Mono: fam = chat.Mono.FontFamily.Name; break;
                default: fam = chat.Body.FontFamily.Name; break;
            }
            try { f = new Font(fam, size, st); }
            catch (Exception)
            {
                try { f = new Font(fam, size); }
                catch (Exception) { f = new Font(chat.Body.FontFamily, size, st); }
            }
            fonts[key] = f;
            return f;
        }

        public int Width(Graphics g, string s, Font f) { return chat.Width(g, s, f); }

        public int Ascent(Font f)
        {
            int a;
            if (ascents.TryGetValue(f, out a)) return a;
            a = Calibrate(f);
            ascents[f] = a;
            return a;
        }

        static int Calibrate(Font f)
        {
            const TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            try
            {
                Size sz = TextRenderer.MeasureText("H", f, Size.Empty, fl);
                int w = Math.Max(8, Math.Min(1200, sz.Width + 12)), h = Math.Max(8, Math.Min(3000, sz.Height + 12));
                using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb))
                {
                    using (Graphics bg = Graphics.FromImage(bmp))
                    {
                        bg.Clear(Color.White);
                        TextRenderer.DrawText(bg, "H", f, new Point(4, 4), Color.Black, Color.White, fl);
                    }
                    BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    try
                    {
                        int[] row = new int[w];
                        for (int y = h - 1; y >= 0; y--)
                        {
                            Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, w);
                            for (int x = 0; x < w; x++)
                            {
                                int px = row[x];
                                int lum = ((px >> 16) & 255) + ((px >> 8) & 255) + (px & 255);
                                if (lum < 330) return y + 1 - 4;
                            }
                        }
                    }
                    finally { bmp.UnlockBits(d); }
                }
            }
            catch (Exception) { }
            FontFamily ff = f.FontFamily;
            return (int)Math.Round(ff.GetCellAscent(f.Style) * (double)f.Height / Math.Max(1, ff.GetLineSpacing(f.Style)));
        }

        public double[] Ink(int face, string s)
        {
            string key = face.ToString(CultureInfo.InvariantCulture) + "|" + s;
            double[] v;
            if (inks.TryGetValue(key, out v)) return v;
            v = null;
            try { v = PathInk(face, s); }
            catch (Exception) { v = null; }
            if (v == null) v = Approx(s);
            if (inks.Count > 20000) inks.Clear();
            inks[key] = v;
            return v;
        }

        double[] PathInk(int face, string s)
        {
            Font f = Font(face, 50);
            FontFamily fam = f.FontFamily;
            int style = (int)f.Style;
            double baseY = RefBase(face, fam, style);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddString(s, fam, style, 100f, PointF.Empty, Typo);
                if (gp.PointCount == 0) return new double[] { 0, 0, 0, 0 };
                RectangleF r = gp.GetBounds();
                if (r.Width <= 0 && r.Height <= 0) return new double[] { 0, 0, 0, 0 };
                return new double[] { (baseY - r.Top) / 100.0, (r.Bottom - baseY) / 100.0, r.Left / 100.0, r.Right / 100.0 };
            }
        }

        double RefBase(int face, FontFamily fam, int style)
        {
            double b;
            if (refBase.TryGetValue(face, out b)) return b;
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddString("H", fam, style, 100f, PointF.Empty, Typo);
                RectangleF r = gp.GetBounds();
                if (gp.PointCount > 0 && r.Height > 1) b = r.Bottom;
                else b = fam.GetCellAscent((FontStyle)style) * 100.0 / Math.Max(1, fam.GetEmHeight((FontStyle)style));
            }
            refBase[face] = b;
            return b;
        }

        static double[] Approx(string s)
        {
            double top = 0, bottom = 0;
            foreach (char c in s)
            {
                if (c == ' ') continue;
                double t = 0.7, d = 0;
                if ("acemnorsuvwxz".IndexOf(c) >= 0) t = 0.46;
                if ("gjpqy".IndexOf(c) >= 0) { t = 0.46; d = 0.22; }
                if ("()[]{}|".IndexOf(c) >= 0) { t = 0.75; d = 0.25; }
                top = Math.Max(top, t);
                bottom = Math.Max(bottom, d);
            }
            return new double[] { top, bottom, 0, 0.5 * s.Length };
        }

        public bool Covers(Graphics g, Font f, string s)
        {
            if (noGdi || string.IsNullOrEmpty(s)) return true;
            foreach (char c in s) if (char.IsSurrogate(c)) return true;
            string key = f.Name + "|" + ((int)f.Style).ToString(CultureInfo.InvariantCulture) + "|" + s;
            bool ok;
            if (covered.TryGetValue(key, out ok)) return ok;
            ok = true;
            IntPtr hdc = IntPtr.Zero, hf = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hdc = g.GetHdc();
                hf = f.ToHfont();
                old = SelectObject(hdc, hf);
                ushort[] idx = new ushort[s.Length];
                if (GetGlyphIndicesW(hdc, s, s.Length, idx, 1) != -1)
                    foreach (ushort i in idx) if (i == 0xFFFF) { ok = false; break; }
            }
            catch (Exception) { noGdi = true; ok = true; }
            finally
            {
                try
                {
                    if (old != IntPtr.Zero) SelectObject(hdc, old);
                    if (hf != IntPtr.Zero) DeleteObject(hf);
                }
                catch (Exception) { }
                if (hdc != IntPtr.Zero) g.ReleaseHdc(hdc);
            }
            if (covered.Count > 5000) covered.Clear();
            covered[key] = ok;
            return ok;
        }

        public int Choose(Graphics g, int face, string text, double pt)
        {
            if (face == Tex.Sans || face == Tex.Mono) return face;
            if (Covers(g, Font(face, pt), text)) return face;
            if (face != Tex.Symbol && Covers(g, Font(Tex.Symbol, pt), text)) return Tex.Symbol;
            if (face == Tex.Symbol && Covers(g, Font(Tex.Roman, pt), text)) return Tex.Roman;
            return Tex.Sans;
        }

        public bool SerifSafe(string t)
        {
            foreach (char c in t)
            {
                if (c < 0x250 || (c >= 0x370 && c < 0x530) || (c >= 0x1E00 && c < 0x2070) || c == 0x2026 || c == 0x2122) continue;
                return false;
            }
            return true;
        }

        void Measure()
        {
            if (measured) return;
            measured = true;
            double[] minus = Ink(Tex.Symbol, "\u2212");
            double top = minus[0], low = -minus[1];
            double axis = (top + low) / 2, th = top - low;
            if (axis > 0.15 && axis < 0.42) AxisEm = axis;
            if (th > 0.02 && th < 0.12) RuleEm = Math.Max(0.035, Math.Min(0.075, th));
            double[] x = Ink(Tex.Italic, "x");
            if (x[0] > 0.3 && x[0] < 0.62) XEm = x[0];
        }

        public List<MathPiece> Inline(Graphics g, string tex, bool display, float pt)
        {
            MathResult r = Get(g, tex, display ? 0 : 2, pt, false);
            return r == null ? null : r.Pieces;
        }

        public MathResult Display(Graphics g, string tex, float pt)
        {
            return Get(g, tex, 0, pt, true);
        }

        MathResult Get(Graphics g, string tex, int st, float pt, bool before)
        {
            tex = tex ?? "";
            string key = st.ToString(CultureInfo.InvariantCulture) + (before ? "b" : "a") + pt.ToString("0.00", CultureInfo.InvariantCulture) + "|" + tex;
            MathResult r;
            if (cache.TryGetValue(key, out r)) return r.Failed ? null : r;
            r = new MathResult();
            try
            {
                if (tex.Length > MaxTex || tex.Trim().Length == 0) r.Failed = true;
                else
                {
                    Dpi = g.DpiY > 0 ? g.DpiY : 96f;
                    Measure();
                    string tag;
                    bool overflow;
                    MNode root = TexParser.Parse(tex, Alphabets, out tag, out overflow);
                    if (root == null || overflow) r.Failed = true;
                    else
                    {
                        MathLayout L = new MathLayout(this, g, pt * 1.07);
                        r.Pieces = L.Pieces(root, st, before);
                        r.Tag = tag;
                        if (L.Exhausted) r.Failed = true;
                        else foreach (MathPiece pc in r.Pieces) pc.Box = Flatten(g, pc.Box);
                        if (!r.Failed && r.Pieces.Count == 0)
                        {
                            MathPiece empty = new MathPiece();
                            empty.Box = new MBox();
                            empty.Text = "";
                            r.Pieces.Add(empty);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                r.Failed = true;
                if (Trace != null) Trace(tex, e);
            }
            if (cache.Count > 600) cache.Clear();
            cache[key] = r;
            return r.Failed ? null : r;
        }

        MBox Flatten(Graphics g, MBox b)
        {
            MBox o = new MBox();
            o.W = b.W;
            o.H = b.H;
            o.D = b.D;
            List<MItem> flat = new List<MItem>();
            Collect(b, 0, 0, flat, 0);
            MItem run = null;
            double runEnd = 0;
            foreach (MItem it in flat)
            {
                if (it.Kind == MItem.Glyphs && run != null && it.Font == run.Font && Math.Abs(it.Y - run.Y) < 0.01 && Math.Abs(it.X - runEnd) < 0.01 && run.Text.Length + it.Text.Length < 200)
                {
                    run.Text += it.Text;
                    runEnd += Width(g, it.Text, it.Font);
                    continue;
                }
                if (it.Kind == MItem.Glyphs)
                {
                    run = new MItem();
                    run.Kind = MItem.Glyphs;
                    run.Text = it.Text;
                    run.Font = it.Font;
                    run.X = it.X;
                    run.Y = it.Y;
                    runEnd = it.X + Width(g, it.Text, it.Font);
                    if (o.Items == null) o.Items = new List<MItem>();
                    o.Items.Add(run);
                    continue;
                }
                run = null;
                if (o.Items == null) o.Items = new List<MItem>();
                o.Items.Add(it);
            }
            return o;
        }

        static void Collect(MBox b, double x, double y, List<MItem> into, int depth)
        {
            if (b == null || b.Items == null || depth > 400) return;
            foreach (MItem it in b.Items)
            {
                if (it.Kind == MItem.Child) { Collect(it.Box, x + it.X, y + it.Y, into, depth + 1); continue; }
                MItem c = new MItem();
                c.Kind = it.Kind;
                c.X = x + it.X;
                c.Y = y + it.Y;
                c.W = it.W;
                c.H = it.H;
                c.Text = it.Text;
                c.Font = it.Font;
                c.Pen = it.Pen;
                c.Pts = it.Pts;
                into.Add(c);
            }
        }

        public void Dispose()
        {
            foreach (Font f in fonts.Values) f.Dispose();
            fonts.Clear();
            ascents.Clear();
            cache.Clear();
        }
    }

    class MathLayout
    {
        readonly MathFonts F;
        readonly Graphics g;
        readonly double pt, px;
        double midSize = -1;
        int budget = 40000;
        public bool Exhausted;

        static readonly int[,] Gaps = {
            {  0,  1, -2, -3,  0,  0,  0, -1 },
            {  1,  1,  0, -3,  0,  0,  0, -1 },
            { -2, -2,  0,  0, -2,  0,  0, -2 },
            { -3, -3,  0,  0, -3,  0,  0, -3 },
            {  0,  0,  0,  0,  0,  0,  0,  0 },
            {  0,  1, -2, -3,  0,  0,  0, -1 },
            { -1, -1,  0, -1, -1, -1, -1, -1 },
            { -1,  1, -2, -3, -1,  0, -1, -1 }
        };

        public MathLayout(MathFonts f, Graphics g, double pt)
        {
            F = f;
            this.g = g;
            this.pt = pt;
            px = F.Dpi / 72.0;
        }

        static double Scale(int st) { return st < 4 ? 1.0 : st < 6 ? 0.7 : 0.5; }
        double Pt(int st) { return pt * Scale(st); }
        double Em(int st) { return Pt(st) * px; }
        double Axis(int st) { return F.AxisEm * Em(st); }
        double Xh(int st) { return F.XEm * Em(st); }
        double Rule(int st) { return F.RuleEm * Em(st); }
        static readonly MBox Blank = new MBox();
        static int SupSt(int st) { return st < 4 ? 4 + (st & 1) : 6 + (st & 1); }
        static int SubSt(int st) { return SupSt(st) | 1; }
        static int NumSt(int st) { return st < 2 ? 2 + (st & 1) : st < 4 ? 4 + (st & 1) : 6 + (st & 1); }
        static int DenSt(int st) { return NumSt(st) | 1; }
        static PointF P(double x, double y) { return new PointF((float)x, (float)y); }

        double Gap(int a, int b, int st)
        {
            if (a < 0 || b < 0 || a > 7 || b > 7) return 0;
            int code = Gaps[a, b];
            if (code < 0) { if (st >= 4) return 0; code = -code; }
            if (code == 0) return 0;
            int mu = code == 1 ? 3 : code == 2 ? 4 : 5;
            return mu / 18.0 * Em(st);
        }

        class At
        {
            public MBox Box;
            public int Cls = -1, St;
            public double Before;
            public MNode Node;
            public bool Space, Wide;
        }

        List<At> Atoms(List<MNode> list, int st)
        {
            List<At> a = new List<At>();
            if (list == null) return a;
            int cur = st;
            foreach (MNode n in list)
            {
                if (n == null) continue;
                if (n.Kind == MNode.Sty) { cur = n.Style; continue; }
                At t = new At();
                t.Node = n;
                t.St = cur;
                if (n.Kind == MNode.Space)
                {
                    t.Space = true;
                    t.Box = new MBox();
                    t.Box.W = n.Size * Em(cur);
                    a.Add(t);
                    continue;
                }
                t.Box = Node(n, cur);
                t.Cls = n.Cls < 0 || n.Cls > 7 ? Tex.Ord : n.Cls;
                t.Wide = (n.Flags & MNode.WideRel) != 0;
                if (n.Kind == MNode.Sym && t.Box.Ital && t.Box.Ic > 0) t.Box.W += t.Box.Ic;
                a.Add(t);
            }
            int prev = -1;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Space) continue;
                int c = a[i].Cls;
                if (c == Tex.Bin)
                {
                    int pc = prev >= 0 ? a[prev].Cls : -1;
                    if (pc < 0 || pc == Tex.Bin || pc == Tex.Op || pc == Tex.Rel || pc == Tex.Open || pc == Tex.Punct) a[i].Cls = Tex.Ord;
                }
                else if ((c == Tex.Rel || c == Tex.Close || c == Tex.Punct) && prev >= 0 && a[prev].Cls == Tex.Bin) a[prev].Cls = Tex.Ord;
                prev = i;
            }
            if (prev >= 0 && a[prev].Cls == Tex.Bin) a[prev].Cls = Tex.Ord;
            prev = -1;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Space) continue;
                if (prev >= 0)
                {
                    a[i].Before = Gap(a[prev].Cls, a[i].Cls, a[i].St);
                    if (a[i].St < 4 && (a[i].Wide || a[prev].Wide)) a[i].Before += 5 / 18.0 * Em(a[i].St);
                }
                prev = i;
            }
            return a;
        }

        MBox HList(List<MNode> list, int st)
        {
            List<At> atoms = Atoms(list, st);
            MBox r = new MBox();
            double x = 0;
            foreach (At a in atoms)
            {
                x += a.Before;
                if (!a.Space)
                {
                    r.Put(a.Box, x, 0);
                    if (a.Box.H > r.H) r.H = a.Box.H;
                    if (a.Box.D > r.D) r.D = a.Box.D;
                }
                x += a.Box.W;
            }
            r.W = Math.Max(0, x);
            return r;
        }

        public List<MathPiece> Pieces(MNode root, int st, bool before)
        {
            List<MathPiece> res = new List<MathPiece>();
            if (root == null) return res;
            if (root.Kind != MNode.Group)
            {
                MathPiece one = new MathPiece();
                one.Box = Node(root, st);
                one.Text = TexLinear.Of(root);
                res.Add(one);
                return res;
            }
            List<At> atoms = Atoms(root.List, st);
            List<int> breaks = new List<int>();
            int prevReal = -1;
            for (int i = 0; i < atoms.Count; i++)
            {
                if (atoms[i].Space) continue;
                if (prevReal >= 0)
                {
                    int pc = atoms[prevReal].Cls, c = atoms[i].Cls;
                    bool ok;
                    if (before) ok = (c == Tex.Rel && pc != Tex.Rel && pc != Tex.Open) || (c == Tex.Bin && pc != Tex.Open);
                    else
                    {
                        bool comma = pc == Tex.Punct && atoms[prevReal].Node.Kind == MNode.Sym && atoms[prevReal].Node.Text == ",";
                        ok = ((pc == Tex.Rel && c != Tex.Rel) || pc == Tex.Bin || comma) && c != Tex.Close && c != Tex.Punct;
                    }
                    if (ok) breaks.Add(i);
                }
                prevReal = i;
            }
            breaks.Add(atoms.Count);
            int start = 0;
            foreach (int bi in breaks)
            {
                int lastReal = -1;
                for (int k = bi - 1; k >= start; k--) if (!atoms[k].Space) { lastReal = k; break; }
                if (lastReal < 0) { start = bi; continue; }
                MBox box = new MBox();
                double x = 0;
                StringBuilder sb = new StringBuilder();
                int prevCls = -1;
                for (int k = start; k <= lastReal; k++)
                {
                    At a = atoms[k];
                    if (k > start) x += a.Before;
                    if (a.Space) { x += a.Box.W; if (a.Box.W > 0.15 * Em(a.St)) sb.Append(' '); continue; }
                    box.Put(a.Box, x, 0);
                    if (a.Box.H > box.H) box.H = a.Box.H;
                    if (a.Box.D > box.D) box.D = a.Box.D;
                    x += a.Box.W;
                    int next = -1;
                    for (int j = k + 1; j < atoms.Count; j++) if (!atoms[j].Space) { next = atoms[j].Cls; break; }
                    TexLinear.Atom(sb, a.Node, a.Cls, prevCls, next);
                    prevCls = a.Cls;
                }
                box.W = Math.Max(0, x);
                double trail = 0;
                for (int k = lastReal + 1; k < bi; k++) trail += atoms[k].Box.W;
                if (bi < atoms.Count) trail += atoms[bi].Before;
                MathPiece pc2 = new MathPiece();
                pc2.Box = box;
                pc2.Trail = Math.Max(0, trail);
                pc2.Text = TexLinear.Tidy(sb.ToString()) + (trail > 0.5 && bi < atoms.Count ? " " : "");
                res.Add(pc2);
                start = bi;
            }
            return res;
        }

        int depth;

        MBox Node(MNode n, int st)
        {
            if (n == null) return new MBox();
            if (--budget < 0 || depth > 150) { Exhausted = true; return new MBox(); }
            depth++;
            try { return NodeBody(n, st); }
            finally { depth--; }
        }

        MBox NodeBody(MNode n, int st)
        {
            switch (n.Kind)
            {
                case MNode.Sym: return n.Big ? BigOp(n, st) : Glyph(n.Text, n.Face, st);
                case MNode.Group: return HList(n.List, st);
                case MNode.Script: return Scripts(n, st);
                case MNode.Frac: return Frac(n, st);
                case MNode.Sqrt: return Sqrt(n, st);
                case MNode.Fence: return Fence(n, st);
                case MNode.Accent: return Accent(n, st);
                case MNode.Txt: return TextBox(n, st);
                case MNode.Space: { MBox b = new MBox(); b.W = n.Size * Em(st); return b; }
                case MNode.Array: return ArrayBox(n, st);
                case MNode.Boxed: return Boxed(n, st);
                case MNode.Stack: return Stack(n, st);
                case MNode.Delim: return Delimiter(n.Text, n.Size < 0 ? Math.Max(0, midSize) : n.Size * Em(st), st);
                case MNode.Phantom: return Phantom(n, st);
                case MNode.Rule:
                    {
                        MBox b = new MBox();
                        double em = Em(st), h = n.Size2 * em, raise = n.Size3 * em;
                        b.W = n.Size * em;
                        b.H = Math.Max(0, h + raise);
                        b.D = Math.Max(0, -raise);
                        if (b.W > 0 && h > 0) b.Rect(0, -(h + raise), b.W, h);
                        return b;
                    }
                case MNode.XArrow: return XArrow(n, st);
                case MNode.Cancel: return Cancel(n, st);
            }
            return new MBox();
        }

        MBox Glyph(string text, int face, int st) { return GlyphAt(text, face, Pt(st)); }

        MBox GlyphAt(string text, int face, double size)
        {
            MBox b = new MBox();
            if (string.IsNullOrEmpty(text)) return b;
            face = F.Choose(g, face, text, size);
            Font f = F.Font(face, size);
            double em = size * px;
            b.W = F.Width(g, text, f);
            double[] ink = F.Ink(face, text);
            b.H = Math.Max(0, ink[0] * em);
            b.D = Math.Max(0, ink[1] * em);
            b.Ic = Math.Min(0.12 * em, Math.Max(0, ink[3] * em - b.W));
            b.Ital = face == Tex.Italic || face == Tex.BoldItalic;
            b.Char = text.Length == 1 || (text.Length == 2 && char.IsSurrogatePair(text, 0));
            b.Glyph(text, f, 0, 0);
            return b;
        }

        MBox BigOp(MNode n, int st)
        {
            bool integral = Tex.Integral(n.Text);
            double scale = st < 2 ? (integral ? 1.75 : 1.42) : 1.0;
            MBox gl = GlyphAt(n.Text, n.Face, Pt(st) * scale);
            double shift = (gl.H - gl.D) / 2 - Axis(st);
            MBox r = new MBox();
            r.Put(gl, 0, shift);
            r.W = gl.W;
            r.H = Math.Max(0, gl.H - shift);
            r.D = Math.Max(0, gl.D + shift);
            r.Ic = gl.Ic;
            return r;
        }

        MBox Scripts(MNode n, int st)
        {
            MNode bn = n.A;
            MBox b = Node(bn, st);
            bool isOp = bn != null && bn.Cls == Tex.Op;
            bool limits = isOp && (bn.Limits == 1 || (bn.Limits == 0 && st < 2));
            MBox sup = n.B != null ? Node(n.B, SupSt(st)) : null;
            MBox sub = n.C != null ? Node(n.C, SubSt(st)) : null;
            if (limits) return Limits(b, sup, sub, st, b.Ic);
            double em = Em(st), xh = Xh(st), rule = Rule(st);
            bool single = b.Char;
            double u = single ? 0 : b.H - 0.386 * Em(SupSt(st));
            double v = single ? 0 : b.D + 0.05 * Em(SubSt(st));
            if (sup != null)
            {
                double min = (st & 1) == 1 ? 0.289 * em : st < 2 ? 0.413 * em : 0.363 * em;
                u = Math.Max(u, Math.Max(min, sup.D + 0.25 * xh));
            }
            if (sub != null)
            {
                if (sup == null) v = Math.Max(v, Math.Max(0.15 * em, sub.H - 0.8 * xh));
                else
                {
                    v = Math.Max(v, 0.247 * em);
                    double gap = (u - sup.D) - (sub.H - v);
                    if (gap < 4 * rule)
                    {
                        v += 4 * rule - gap;
                        double psi = 0.8 * xh - (u - sup.D);
                        if (psi > 0) { u += psi; v -= psi; }
                    }
                }
            }
            MBox r = new MBox();
            r.Put(b, 0, 0);
            double w = b.W;
            if (sup != null)
            {
                r.Put(sup, b.W + b.Ic, -u);
                w = Math.Max(w, b.W + b.Ic + sup.W);
            }
            if (sub != null)
            {
                r.Put(sub, b.W, v);
                w = Math.Max(w, b.W + sub.W);
            }
            r.W = w + 0.05 * em;
            r.H = Math.Max(b.H, sup != null ? u + sup.H : 0);
            r.D = Math.Max(b.D, sub != null ? v + sub.D : 0);
            return r;
        }

        MBox Limits(MBox b, MBox sup, MBox sub, int st, double ic)
        {
            double em = Em(st);
            double w = b.W;
            if (sup != null) w = Math.Max(w, sup.W);
            if (sub != null) w = Math.Max(w, sub.W);
            MBox r = new MBox();
            r.Put(b, (w - b.W) / 2, 0);
            double h = b.H, d = b.D;
            if (sup != null)
            {
                double gap = Math.Max(0.111 * em, 0.2 * em - sup.D);
                r.Put(sup, (w - sup.W) / 2 + ic / 2, -(b.H + gap + sup.D));
                h = b.H + gap + sup.D + sup.H + 0.1 * em;
            }
            if (sub != null)
            {
                double gap = Math.Max(0.166 * em, 0.6 * em - sub.H);
                r.Put(sub, (w - sub.W) / 2 - ic / 2, b.D + gap + sub.H);
                d = b.D + gap + sub.H + sub.D + 0.1 * em;
            }
            r.W = w;
            r.H = h;
            r.D = d;
            return r;
        }

        MBox Frac(MNode n, int st)
        {
            int fs = n.Style >= 0 ? n.Style : st;
            MBox num = Node(n.A, NumSt(fs)), den = Node(n.B, DenSt(fs));
            double em = Em(fs), axis = Axis(fs);
            double th = n.Size > 0 ? Math.Max(0.6, Rule(fs)) : 0;
            bool disp = fs < 2;
            double u = disp ? 0.677 * em : th > 0 ? 0.394 * em : 0.444 * em;
            double v = disp ? 0.686 * em : 0.345 * em;
            if (th > 0)
            {
                double clr = disp ? 3 * th : th;
                double d1 = (u - num.D) - (axis + th / 2);
                if (d1 < clr) u += clr - d1;
                double d2 = (axis - th / 2) - (den.H - v);
                if (d2 < clr) v += clr - d2;
            }
            else
            {
                double clr = (disp ? 7 : 3) * Rule(fs);
                double d = (u - num.D) - (den.H - v);
                if (d < clr) { u += (clr - d) / 2; v += (clr - d) / 2; }
            }
            bool delims = n.L != null || n.R != null;
            double inner = Math.Max(num.W, den.W);
            double pad = delims ? 0.04 * em : 0.12 * em;
            MBox r = new MBox();
            r.Put(num, pad + (inner - num.W) / 2, -u);
            r.Put(den, pad + (inner - den.W) / 2, v);
            if (th > 0)
            {
                double ext = 0.06 * em;
                r.Rect(pad - ext, -(axis + th / 2), inner + 2 * ext, th);
            }
            r.W = inner + 2 * pad;
            r.H = u + num.H;
            r.D = v + den.D;
            if (!delims) return r;
            double size = Math.Max(disp ? 2.39 * em : 1.01 * em, DelimSize(r.H, r.D, fs));
            MBox l = Delimiter(n.L ?? ".", size, fs), rr = Delimiter(n.R ?? ".", size, fs);
            MBox o = new MBox();
            o.Put(l, 0, 0);
            o.Put(r, l.W, 0);
            o.Put(rr, l.W + r.W, 0);
            o.W = l.W + r.W + rr.W;
            o.H = Math.Max(r.H, Math.Max(l.H, rr.H));
            o.D = Math.Max(r.D, Math.Max(l.D, rr.D));
            return o;
        }

        MBox Sqrt(MNode n, int st)
        {
            double em = Em(st);
            double rule = Math.Max(0.8, Rule(st));
            MBox body = Node(n.A, st | 1);
            double phi = st < 2 ? Xh(st) : rule;
            double clr = rule + phi / 4;
            double need = body.H + body.D + clr + rule;
            double total = Math.Max(need, 1.0 * em);
            clr += (total - need) / 2;
            double top = body.H + clr + rule;
            double dep = total - top;
            double sw = em * (0.56 + 0.07 * Math.Min(4, Math.Max(0, total / em - 1.0)));
            MBox r = new MBox();
            double sx = 0, h = top + rule;
            MBox idx = n.B != null ? Node(n.B, 6) : null;
            if (idx != null && !idx.Empty && idx.W > 0)
            {
                double raise = 0.6 * (top - dep);
                double kb = 5 / 18.0 * em, ka = -10 / 18.0 * em;
                r.Put(idx, kb, -raise);
                sx = Math.Max(0, kb + idx.W + ka);
                h = Math.Max(h, raise + idx.H);
            }
            double y0 = -top, y1 = dep;
            double thin = Math.Max(1.0, 0.045 * em), heavy = Math.Max(1.35, 0.095 * em);
            double tickY = y1 - Math.Min(0.5 * total, 0.55 * em);
            r.Line(new PointF[] { P(sx + 0.02 * sw, tickY + 0.08 * em), P(sx + 0.2 * sw, tickY) }, thin);
            r.Line(new PointF[] { P(sx + 0.2 * sw, tickY), P(sx + 0.5 * sw, y1 - heavy * 0.3) }, heavy);
            r.Line(new PointF[] { P(sx + 0.5 * sw, y1 - heavy * 0.3), P(sx + sw, y0 + rule / 2) }, thin);
            double bx = sx + sw, bw = body.W + 0.14 * em;
            r.Rect(bx - thin / 2, y0, bw + thin / 2, rule);
            r.Put(body, bx + 0.07 * em, 0);
            r.W = bx + bw + 0.04 * em;
            r.H = Math.Max(h, top + rule);
            r.D = Math.Max(dep, body.D);
            return r;
        }

        double DelimSize(double h, double d, int st)
        {
            double a = Axis(st);
            double delta = Math.Max(h - a, d + a);
            return Math.Max(delta * 2 * 0.901, 2 * delta - 0.5 * Em(st));
        }

        static bool HasMiddle(MNode n)
        {
            if (n == null || n.List == null) return false;
            foreach (MNode x in n.List) if (x != null && x.Kind == MNode.Delim && x.Size < 0) return true;
            return false;
        }

        MBox Fence(MNode n, int st)
        {
            double save = midSize;
            midSize = -1;
            MBox body = Node(n.A, st);
            double size = DelimSize(body.H, body.D, st);
            if (HasMiddle(n.A))
            {
                midSize = size;
                body = Node(n.A, st);
            }
            midSize = save;
            MBox l = Delimiter(n.L, size, st), r = Delimiter(n.R, size, st);
            MBox o = new MBox();
            o.Put(l, 0, 0);
            o.Put(body, l.W, 0);
            o.Put(r, l.W + body.W, 0);
            o.W = l.W + body.W + r.W;
            o.H = Math.Max(body.H, Math.Max(l.H, r.H));
            o.D = Math.Max(body.D, Math.Max(l.D, r.D));
            return o;
        }

        static int DelimFace(string d)
        {
            if (d == "(" || d == ")" || d == "[" || d == "]" || d == "{" || d == "}" || d == "/") return Tex.Roman;
            return Tex.Symbol;
        }

        MBox Delimiter(string d, double size, int st)
        {
            double em = Em(st);
            if (string.IsNullOrEmpty(d) || d == ".")
            {
                MBox nb = new MBox();
                nb.W = 0.12 * em;
                return nb;
            }
            MBox gl = Glyph(d, DelimFace(d), st);
            double natural = gl.H + gl.D;
            if (size <= Math.Max(natural * 1.04, 1.04 * em)) return gl;
            size = Math.Min(size, 60 * em);
            return PathDelim(d, size, st, gl);
        }

        MBox PathDelim(string d, double size, int st, MBox glyph)
        {
            double em = Em(st), axis = Axis(st);
            double top = axis + size / 2, bot = size / 2 - axis;
            MBox b = new MBox();
            b.H = top;
            b.D = bot;
            double y0 = -top, y1 = bot, h = size, ym = (y0 + y1) / 2;
            double thin = Math.Max(1.0, 0.045 * em);
            double stem = Math.Max(1.0, 0.075 * em);
            bool left = d == "(" || d == "[" || d == "{" || d == "\u230A" || d == "\u2308" || d == "\u27E8" || d == "\u27E6";
            switch (d)
            {
                case "(":
                case ")":
                    {
                        double w = Math.Min(0.8 * em, 0.34 * em + 0.06 * h);
                        double tm = Math.Min(0.15 * em, Math.Max(1.3, 0.075 * em + 0.012 * h));
                        double te = Math.Max(0.7, 0.025 * em);
                        double xe = w * 0.84, xm = w * 0.16;
                        double k = h <= 1.6 * em ? 0.5 : 0.5 * Math.Pow(1.6 * em / h, 0.75);
                        const int N = 32;
                        PointF[] pts = new PointF[2 * (N + 1)];
                        for (int i = 0; i <= N; i++)
                        {
                            double t = (double)i / N, u = 2 * t - 1;
                            double q = Math.Max(0, 1 - u * u);
                            double xo = xe - (xe - xm) * Math.Pow(q, k);
                            double th = te + (tm - te) * Math.Sqrt(q);
                            double y = y0 + t * h;
                            double xi = xo + th;
                            if (!left) { xo = w - xo; xi = w - xi; }
                            pts[i] = P(xo, y);
                            pts[2 * N + 1 - i] = P(xi, y);
                        }
                        b.Poly(pts);
                        b.W = w;
                        break;
                    }
                case "[":
                case "]":
                case "\u230A":
                case "\u230B":
                case "\u2308":
                case "\u2309":
                case "\u27E6":
                case "\u27E7":
                    {
                        double w = 0.44 * em, arm = 0.3 * em, xs = 0.12 * em;
                        bool topArm = d != "\u230A" && d != "\u230B", botArm = d != "\u2308" && d != "\u2309";
                        bool dbl = d == "\u27E6" || d == "\u27E7";
                        if (dbl) { w += 0.15 * em; arm += 0.15 * em; }
                        double sx = left ? xs : w - xs - stem;
                        double ax = left ? xs : w - xs - arm;
                        b.Rect(sx, y0, stem, h);
                        if (dbl) b.Rect(left ? sx + 0.17 * em : sx - 0.17 * em, y0, thin, h);
                        if (topArm) b.Rect(ax, y0, arm, thin);
                        if (botArm) b.Rect(ax, y1 - thin, arm, thin);
                        b.W = w;
                        break;
                    }
                case "{":
                case "}":
                    {
                        double w = 0.52 * em, pen = Math.Max(1.1, 0.07 * em);
                        double xr = w * 0.86, xs = w * 0.5, xc = w * 0.12;
                        double r = Math.Min(0.28 * em, h / 6);
                        List<PointF> pl = new List<PointF>();
                        Quad(pl, xr, y0 + pen / 2, xs, y0 + pen / 2, xs, y0 + r);
                        pl.Add(P(xs, ym - r));
                        Quad(pl, xs, ym - r, xs, ym, xc, ym);
                        Quad(pl, xc, ym, xs, ym, xs, ym + r);
                        pl.Add(P(xs, y1 - r));
                        Quad(pl, xs, y1 - r, xs, y1 - pen / 2, xr, y1 - pen / 2);
                        if (!left) for (int i = 0; i < pl.Count; i++) pl[i] = P(w - pl[i].X, pl[i].Y);
                        b.Line(pl.ToArray(), pen);
                        b.W = w;
                        break;
                    }
                case "|":
                case "\u2016":
                    {
                        bool dbl = d == "\u2016";
                        double sw = Math.Max(1.0, 0.055 * em);
                        double x = 0.13 * em;
                        b.Rect(x, y0, sw, h);
                        if (dbl) b.Rect(x + 0.16 * em, y0, sw, h);
                        b.W = x * 2 + sw + (dbl ? 0.16 * em : 0);
                        break;
                    }
                case "\u27E8":
                case "\u27E9":
                    {
                        double w = Math.Min(0.6 * em, 0.26 * em + 0.08 * h), pen = Math.Max(1.0, 0.06 * em);
                        double xa = w * 0.82, xb = w * 0.16;
                        if (!left) { xa = w - xa; xb = w - xb; }
                        b.Line(new PointF[] { P(xa, y0), P(xb, ym), P(xa, y1) }, pen);
                        b.W = w;
                        break;
                    }
                case "/":
                case "\\":
                    {
                        double w = 0.2 * em + 0.3 * h, pen = Math.Max(1.0, 0.06 * em);
                        if (d == "/") b.Line(new PointF[] { P(w * 0.9, y0), P(w * 0.1, y1) }, pen);
                        else b.Line(new PointF[] { P(w * 0.1, y0), P(w * 0.9, y1) }, pen);
                        b.W = w;
                        break;
                    }
                default:
                    {
                        double natural = Math.Max(0.3 * em, glyph.H + glyph.D);
                        MBox big = GlyphAt(d, DelimFace(d), Pt(st) * Math.Min(12, size / natural));
                        double shift = (big.H - big.D) / 2 - axis;
                        b.Put(big, 0, shift);
                        b.W = big.W;
                        b.H = Math.Max(0, big.H - shift);
                        b.D = Math.Max(0, big.D + shift);
                        break;
                    }
            }
            return b;
        }

        static void Quad(List<PointF> pl, double x0, double y0, double cx, double cy, double x1, double y1)
        {
            const int N = 10;
            for (int i = 0; i <= N; i++)
            {
                double t = (double)i / N, a = (1 - t) * (1 - t), b = 2 * (1 - t) * t, c = t * t;
                pl.Add(P(a * x0 + b * cx + c * x1, a * y0 + b * cy + c * y1));
            }
        }

        static bool OneChar(MNode n)
        {
            while (n != null && n.Kind == MNode.Group && n.List != null && n.List.Count == 1) n = n.List[0];
            if (n == null || n.Kind != MNode.Sym || string.IsNullOrEmpty(n.Text)) return false;
            return n.Text.Length == 1 || (n.Text.Length == 2 && char.IsSurrogatePair(n.Text, 0));
        }

        static bool ItalicChar(MNode n)
        {
            while (n != null && n.Kind == MNode.Group && n.List != null && n.List.Count == 1) n = n.List[0];
            return n != null && n.Kind == MNode.Sym && (n.Face == Tex.Italic || n.Face == Tex.BoldItalic);
        }

        MBox Accent(MNode n, int st)
        {
            string a = n.Text;
            double em = Em(st), rule = Math.Max(0.8, Rule(st));
            bool under = a.StartsWith("under") || a == "utilde" || a == "uline";
            MBox body = Node(n.A, under ? st : st | 1);
            bool single = OneChar(n.A);
            double skew = single && ItalicChar(n.A) ? 0.1 * body.H : 0;
            double cx = body.W / 2 + skew;
            MBox r = new MBox();
            r.Put(body, 0, 0);
            r.W = body.W;
            r.H = body.H;
            r.D = body.D;
            double pen = Math.Max(1.0, 0.052 * em);
            double gap = Math.Max(1.5, 0.1 * em);
            double h0 = Math.Max(body.H, Xh(st));
            double yb = -(h0 + gap);
            bool wide = !single;
            switch (a)
            {
                case "hat":
                case "widehat":
                case "check":
                case "widecheck":
                    {
                        bool chk = a.EndsWith("check");
                        double w = wide && a.StartsWith("wide") ? Math.Max(0.4 * em, body.W * 0.96) : wide ? Math.Max(0.36 * em, Math.Min(body.W, 0.6 * em)) : 0.36 * em;
                        double ht = a.StartsWith("wide") ? Math.Min(0.32 * em, 0.13 * em + 0.03 * w) : 0.15 * em;
                        if (chk) r.Line(new PointF[] { P(cx - w / 2, yb - ht), P(cx, yb), P(cx + w / 2, yb - ht) }, pen);
                        else r.Line(new PointF[] { P(cx - w / 2, yb), P(cx, yb - ht), P(cx + w / 2, yb) }, pen);
                        r.H = h0 + gap + ht + pen / 2;
                        break;
                    }
                case "tilde":
                case "widetilde":
                case "utilde":
                    {
                        double w = a == "tilde" && !wide ? 0.4 * em : Math.Max(0.4 * em, body.W * 0.96);
                        double ht = a == "tilde" ? 0.09 * em : Math.Min(0.22 * em, 0.08 * em + 0.02 * w);
                        double ym = under ? body.D + gap + ht / 2 : yb - ht / 2;
                        List<PointF> pl = new List<PointF>();
                        for (int i = 0; i <= 16; i++)
                        {
                            double t = i / 16.0;
                            pl.Add(P(cx - w / 2 + t * w, ym - ht / 2 * Math.Sin(2 * Math.PI * t)));
                        }
                        r.Line(pl.ToArray(), pen);
                        if (under) r.D = body.D + gap + ht + pen;
                        else r.H = h0 + gap + ht + pen / 2;
                        break;
                    }
                case "bar":
                    {
                        double w = wide ? body.W : Math.Max(0.28 * em, Math.Min(body.W * 0.95, 0.52 * em));
                        double x0 = wide ? 0 : cx - w / 2;
                        r.Rect(x0, yb - rule, w, rule);
                        r.H = h0 + gap + rule;
                        break;
                    }
                case "overline":
                case "overlinesegment":
                    {
                        double y = -(body.H + 3 * rule) - rule;
                        r.Rect(0, y, body.W, rule);
                        if (a == "overlinesegment") { r.Rect(0, y - 0.06 * em, rule, 0.12 * em + rule); r.Rect(body.W - rule, y - 0.06 * em, rule, 0.12 * em + rule); }
                        r.H = body.H + 3 * rule + 2 * rule;
                        break;
                    }
                case "underline":
                case "uline":
                case "underlinesegment":
                    {
                        double y = body.D + 3 * rule;
                        r.Rect(0, y, body.W, rule);
                        r.D = body.D + 4 * rule + rule;
                        break;
                    }
                case "vec":
                    {
                        double w = wide ? Math.Max(0.4 * em, body.W) : 0.4 * em;
                        double x0 = wide ? (body.W - w) / 2 : cx - w / 2;
                        double ya = yb - 0.07 * em;
                        Arrow(r, x0, x0 + w, ya, false, true, false, pen, em * 0.78);
                        r.H = h0 + gap + 0.16 * em;
                        break;
                    }
                case "overrightarrow":
                case "overleftarrow":
                case "overleftrightarrow":
                case "Overrightarrow":
                    {
                        double w = Math.Max(0.5 * em, body.W);
                        double ya = -(body.H + gap + 0.1 * em);
                        if (a == "Overrightarrow") { DoubleArrow(r, (body.W - w) / 2, (body.W - w) / 2 + w, ya, pen, em); r.H = body.H + gap + 0.26 * em; break; }
                        Arrow(r, (body.W - w) / 2, (body.W - w) / 2 + w, ya, a != "overrightarrow", a != "overleftarrow", false, pen, em);
                        r.H = body.H + gap + 0.22 * em;
                        r.W = Math.Max(r.W, w);
                        break;
                    }
                case "underrightarrow":
                case "underleftarrow":
                case "underleftrightarrow":
                    {
                        double w = Math.Max(0.5 * em, body.W);
                        double ya = body.D + gap + 0.1 * em;
                        Arrow(r, (body.W - w) / 2, (body.W - w) / 2 + w, ya, a != "underrightarrow", a != "underleftarrow", false, pen, em);
                        r.D = body.D + gap + 0.22 * em;
                        break;
                    }
                case "dot":
                case "ddot":
                case "dddot":
                case "ddddot":
                    {
                        int count = a == "dot" ? 1 : a == "ddot" ? 2 : a == "dddot" ? 3 : 4;
                        double dd = Math.Max(2.2, 0.11 * em), sp = 0.19 * em;
                        double x0 = cx - (count - 1) * sp / 2;
                        for (int i = 0; i < count; i++) Dot(r, x0 + i * sp, yb - dd / 2, dd / 2);
                        r.H = h0 + gap + dd;
                        break;
                    }
                case "acute":
                case "grave":
                    {
                        double w = 0.14 * em, ht = 0.17 * em;
                        if (a == "acute") r.Line(new PointF[] { P(cx - w / 2, yb), P(cx + w / 2, yb - ht) }, pen * 1.15);
                        else r.Line(new PointF[] { P(cx + w / 2, yb), P(cx - w / 2, yb - ht) }, pen * 1.15);
                        r.H = h0 + gap + ht;
                        break;
                    }
                case "breve":
                    {
                        double w = 0.32 * em, ht = 0.12 * em;
                        List<PointF> pl = new List<PointF>();
                        for (int i = 0; i <= 12; i++)
                        {
                            double t = Math.PI * i / 12;
                            pl.Add(P(cx - w / 2 * Math.Cos(t), yb - ht + ht * Math.Sin(t)));
                        }
                        r.Line(pl.ToArray(), pen);
                        r.H = h0 + gap + ht + pen;
                        break;
                    }
                case "mathring":
                    {
                        double rr = 0.08 * em;
                        List<PointF> pl = new List<PointF>();
                        for (int i = 0; i <= 16; i++) { double t = 2 * Math.PI * i / 16; pl.Add(P(cx + rr * Math.Cos(t), yb - rr + rr * Math.Sin(t))); }
                        r.Line(pl.ToArray(), pen * 0.9);
                        r.H = h0 + gap + 2 * rr + pen;
                        break;
                    }
                case "overbrace":
                case "underbrace":
                    {
                        double w = Math.Max(0.5 * em, body.W), ht = Math.Min(0.4 * em, 0.2 * em + 0.02 * w);
                        bool over = a == "overbrace";
                        double ybase = over ? -(body.H + gap * 1.5) : body.D + gap * 1.5;
                        double dir = over ? -1 : 1;
                        HBrace(r, (body.W - w) / 2, w, ybase, dir * ht, Math.Max(1.2, 0.07 * em));
                        if (over) r.H = body.H + gap * 1.5 + ht + 0.05 * em;
                        else r.D = body.D + gap * 1.5 + ht + 0.05 * em;
                        r.W = Math.Max(r.W, w);
                        break;
                    }
                case "overgroup":
                case "undergroup":
                case "wideparen":
                    {
                        double w = Math.Max(0.5 * em, body.W), ht = Math.Min(0.3 * em, 0.1 * em + 0.03 * w);
                        bool over = a != "undergroup";
                        List<PointF> pl = new List<PointF>();
                        for (int i = 0; i <= 20; i++)
                        {
                            double t = Math.PI * i / 20;
                            double y = over ? -(body.H + gap) - ht * Math.Sin(t) : body.D + gap + ht * Math.Sin(t);
                            pl.Add(P((body.W - w) / 2 + w * i / 20.0, y));
                        }
                        r.Line(pl.ToArray(), pen);
                        if (over) r.H = body.H + gap + ht + pen; else r.D = body.D + gap + ht + pen;
                        break;
                    }
                default:
                    {
                        double y = -(body.H + 3 * rule) - rule;
                        r.Rect(0, y, body.W, rule);
                        r.H = body.H + 5 * rule;
                        break;
                    }
            }
            return r;
        }

        static void Dot(MBox r, double x, double y, double rad)
        {
            PointF[] pts = new PointF[12];
            for (int i = 0; i < 12; i++) { double t = 2 * Math.PI * i / 12; pts[i] = P(x + rad * Math.Cos(t), y + rad * Math.Sin(t)); }
            r.Poly(pts);
        }

        static void HBrace(MBox r, double x0, double w, double y, double ht, double pen)
        {
            double rr = Math.Min(Math.Abs(ht) / 2, w / 8);
            double ym = y + ht / 2, xm = x0 + w / 2;
            List<PointF> pl = new List<PointF>();
            Quad(pl, x0, y, x0, ym, x0 + rr, ym);
            pl.Add(P(xm - rr, ym));
            Quad(pl, xm - rr, ym, xm, ym, xm, y + ht);
            Quad(pl, xm, y + ht, xm, ym, xm + rr, ym);
            pl.Add(P(x0 + w - rr, ym));
            Quad(pl, x0 + w - rr, ym, x0 + w, ym, x0 + w, y);
            r.Line(pl.ToArray(), pen);
        }

        static void Arrow(MBox r, double x0, double x1, double y, bool leftHead, bool rightHead, bool bar, double pen, double em)
        {
            double hl = 0.24 * em, hw = 0.12 * em;
            r.Line(new PointF[] { P(x0, y), P(x1, y) }, pen);
            if (rightHead) r.Line(new PointF[] { P(x1 - hl, y - hw), P(x1, y), P(x1 - hl, y + hw) }, pen);
            if (leftHead) r.Line(new PointF[] { P(x0 + hl, y - hw), P(x0, y), P(x0 + hl, y + hw) }, pen);
            if (bar) r.Line(new PointF[] { P(x0, y - hw), P(x0, y + hw) }, pen);
        }

        static void DoubleArrow(MBox r, double x0, double x1, double y, double pen, double em)
        {
            double sep = 0.08 * em, hl = 0.3 * em, hw = 0.2 * em;
            r.Line(new PointF[] { P(x0, y - sep), P(x1 - sep * 1.4, y - sep) }, pen);
            r.Line(new PointF[] { P(x0, y + sep), P(x1 - sep * 1.4, y + sep) }, pen);
            r.Line(new PointF[] { P(x1 - hl, y - hw), P(x1, y), P(x1 - hl, y + hw) }, pen);
        }

        MBox TextBox(MNode n, int st)
        {
            MBox b = new MBox();
            string t = n.Text;
            if (string.IsNullOrEmpty(t)) return b;
            int face = n.Face;
            if (face != Tex.Sans && face != Tex.Mono && !F.SerifSafe(t)) face = Tex.Sans;
            double size = Pt(st);
            Font f = F.Font(face, size);
            double em = size * px;
            b.W = F.Width(g, t, f);
            string core = t.Trim();
            if (core.Length > 0)
            {
                double[] ink = F.Ink(face, core);
                b.H = Math.Max(0, ink[0] * em);
                b.D = Math.Max(0, ink[1] * em);
            }
            b.Glyph(t, f, 0, 0);
            return b;
        }

        MBox ArrayBox(MNode n, int st)
        {
            int kind = n.Flags;
            int cs = n.Style >= 0 ? n.Style : kind == 2 || kind == 3 ? 0 : kind == 5 ? 4 : kind == 6 ? st : st >= 4 ? st : 2;
            double em = Em(cs < 4 ? 2 : cs);
            int rows = n.Rows.Count, cols = 0;
            foreach (List<MNode> row in n.Rows) cols = Math.Max(cols, row.Count);
            MBox o = new MBox();
            if (rows == 0 || cols == 0) return o;
            if ((long)rows * cols > 40000) { Exhausted = true; return o; }
            double stretch = kind == 1 ? 1.2 : kind == 5 ? 0.55 : kind == 6 ? 0 : 1.0;
            double strutH = 0.84 * em * stretch, strutD = 0.36 * em * stretch;
            double jot = kind == 2 || kind == 3 ? 0.25 * em : kind == 6 ? 0.08 * em : 0;
            MBox[][] cell = new MBox[rows][];
            double[] colW = new double[cols], rowH = new double[rows], rowD = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                cell[r] = new MBox[cols];
                rowH[r] = strutH;
                rowD[r] = strutD;
                for (int c = 0; c < cols; c++)
                {
                    MBox b = c < n.Rows[r].Count ? Node(n.Rows[r][c], cs) : Blank;
                    cell[r][c] = b;
                    colW[c] = Math.Max(colW[c], b.W);
                    rowH[r] = Math.Max(rowH[r], b.H);
                    rowD[r] = Math.Max(rowD[r], b.D);
                }
            }
            double[] gapAfter = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                if (c == cols - 1) { gapAfter[c] = 0; continue; }
                switch (kind)
                {
                    case 2: gapAfter[c] = c % 2 == 0 ? 0 : 2 * em; break;
                    case 5: gapAfter[c] = 0.55 * em; break;
                    case 6: gapAfter[c] = 0.5 * em; break;
                    default: gapAfter[c] = 1.0 * em; break;
                }
            }
            double outer = kind == 4 ? 0.5 * em : 0;
            string spec = n.Spec ?? "";
            string vl = n.Text ?? "";
            double[] colX = new double[cols];
            double x = outer;
            for (int c = 0; c < cols; c++)
            {
                colX[c] = x;
                x += colW[c] + gapAfter[c];
            }
            double width = x + outer;
            double total = 0;
            for (int r = 0; r < rows; r++) total += rowH[r] + rowD[r] + (r > 0 ? jot : 0);
            double axis = Axis(st < 4 ? st : cs);
            double topY = -(total / 2 + axis);
            double y = topY;
            double rule = Math.Max(0.8, Rule(cs));
            List<int> lines = n.Lines;
            for (int r = 0; r < rows; r++)
            {
                if (r > 0) y += jot;
                if (lines != null && lines.Contains(r)) o.Rect(0, y - (r > 0 ? jot / 2 : 0) - rule / 2, width, rule);
                double baseY = y + rowH[r];
                for (int c = 0; c < cols; c++)
                {
                    MBox b = cell[r][c];
                    char al = c < spec.Length ? spec[c] : 'c';
                    double cx = al == 'l' ? colX[c] : al == 'r' ? colX[c] + colW[c] - b.W : colX[c] + (colW[c] - b.W) / 2;
                    o.Put(b, cx, baseY);
                }
                y = baseY + rowD[r];
            }
            if (lines != null && lines.Contains(rows)) o.Rect(0, y - rule / 2, width, rule);
            for (int c = 0; c <= cols && c < vl.Length; c++)
            {
                if (vl[c] != '|') continue;
                double vx = c == 0 ? 0 : c >= cols ? width - rule : colX[c] - gapAfter[c - 1] / 2;
                o.Rect(vx, topY, rule, total);
            }
            o.W = width;
            o.H = -topY;
            o.D = total + topY;
            if (n.L == null && n.R == null) return o;
            double size = DelimSize(o.H, o.D, st < 4 ? st : cs);
            size = Math.Max(size, total + 0.1 * em);
            MBox l = Delimiter(n.L ?? ".", size, st), rr = Delimiter(n.R ?? ".", size, st);
            double pad = 0.12 * em;
            MBox w = new MBox();
            w.Put(l, 0, 0);
            w.Put(o, l.W + pad, 0);
            w.Put(rr, l.W + pad + o.W + pad, 0);
            w.W = l.W + o.W + rr.W + 2 * pad;
            w.H = Math.Max(o.H, Math.Max(l.H, rr.H));
            w.D = Math.Max(o.D, Math.Max(l.D, rr.D));
            return w;
        }

        MBox Boxed(MNode n, int st)
        {
            MBox body = Node(n.A, st);
            double em = Em(st), pad = 0.3 * em, rule = Math.Max(1.0, 0.045 * em);
            MBox r = new MBox();
            double h = Math.Max(body.H, 0.7 * em) + pad, d = Math.Max(body.D, 0.2 * em) + pad;
            double w = body.W + 2 * pad;
            r.Put(body, rule + pad, 0);
            r.Rect(0, -h - rule, w + 2 * rule, rule);
            r.Rect(0, d, w + 2 * rule, rule);
            r.Rect(0, -h - rule, rule, h + d + 2 * rule);
            r.Rect(w + rule, -h - rule, rule, h + d + 2 * rule);
            r.W = w + 2 * rule;
            r.H = h + rule;
            r.D = d + rule;
            return r;
        }

        MBox Stack(MNode n, int st)
        {
            MBox b = Node(n.A, st);
            MBox over = n.B != null ? Node(n.B, SupSt(st)) : null;
            MBox under = n.C != null ? Node(n.C, SubSt(st)) : null;
            double em = Em(st);
            double w = b.W;
            if (over != null) w = Math.Max(w, over.W);
            if (under != null) w = Math.Max(w, under.W);
            MBox r = new MBox();
            r.Put(b, (w - b.W) / 2, 0);
            r.H = b.H;
            r.D = b.D;
            if (over != null)
            {
                double gap = Math.Max(0.111 * em, 0.2 * em - over.D);
                r.Put(over, (w - over.W) / 2, -(b.H + gap + over.D));
                r.H = b.H + gap + over.D + over.H;
            }
            if (under != null)
            {
                double gap = Math.Max(0.166 * em, 0.4 * em - under.H);
                r.Put(under, (w - under.W) / 2, b.D + gap + under.H);
                r.D = b.D + gap + under.H + under.D;
            }
            r.W = w;
            return r;
        }

        MBox Phantom(MNode n, int st)
        {
            MBox b = Node(n.A, st);
            MBox r = new MBox();
            if (n.Flags == 4)
            {
                r.Put(b, 0, 0);
                r.W = b.W;
                return r;
            }
            r.W = (n.Flags & 1) != 0 ? b.W : 0;
            if ((n.Flags & 2) != 0) { r.H = b.H; r.D = b.D; }
            return r;
        }

        MBox XArrow(MNode n, int st)
        {
            double em = Em(st), axis = Axis(st);
            MBox up = n.A != null ? Node(n.A, SupSt(st)) : null;
            MBox dn = n.B != null ? Node(n.B, SubSt(st)) : null;
            double content = Math.Max(up != null ? up.W : 0, dn != null ? dn.W : 0);
            double len = Math.Max(content + 0.7 * em, 1.3 * em);
            double pen = Math.Max(1.0, 0.05 * em);
            double ay = -axis;
            MBox r = new MBox();
            string kind = n.Text ?? "rightarrow";
            double x0 = 0.08 * em, x1 = len - 0.08 * em;
            switch (kind)
            {
                case "Rightarrow":
                case "Leftarrow":
                case "Leftrightarrow":
                    {
                        double sep = 0.08 * em;
                        r.Line(new PointF[] { P(x0, ay - sep), P(x1, ay - sep) }, pen);
                        r.Line(new PointF[] { P(x0, ay + sep), P(x1, ay + sep) }, pen);
                        double hl = 0.28 * em, hw = 0.22 * em;
                        if (kind != "Leftarrow") r.Line(new PointF[] { P(x1 - hl, ay - hw), P(x1 + 0.02 * em, ay), P(x1 - hl, ay + hw) }, pen);
                        if (kind != "Rightarrow") r.Line(new PointF[] { P(x0 + hl, ay - hw), P(x0 - 0.02 * em, ay), P(x0 + hl, ay + hw) }, pen);
                        break;
                    }
                case "longequal":
                    {
                        double sep = 0.07 * em;
                        r.Line(new PointF[] { P(x0, ay - sep), P(x1, ay - sep) }, pen);
                        r.Line(new PointF[] { P(x0, ay + sep), P(x1, ay + sep) }, pen);
                        break;
                    }
                case "rightleftharpoons":
                case "leftrightharpoons":
                case "tofrom":
                    {
                        double sep = 0.1 * em;
                        Arrow(r, x0, x1, ay - sep, false, true, false, pen, em);
                        Arrow(r, x0, x1, ay + sep, true, false, false, pen, em);
                        break;
                    }
                default:
                    {
                        bool leftHead = kind.StartsWith("left") || kind == "hookleftarrow" || kind == "twoheadleftarrow";
                        bool both = kind == "leftrightarrow";
                        Arrow(r, x0, x1, ay, leftHead || both, !leftHead || both, kind == "mapsto", pen, em);
                        if (kind.StartsWith("twohead")) Arrow(r, leftHead ? x0 + 0.12 * em : x0, leftHead ? x1 : x1 - 0.12 * em, ay, leftHead, !leftHead, false, pen, em);
                        break;
                    }
            }
            r.W = len;
            r.H = axis + 0.24 * em;
            r.D = Math.Max(0, 0.24 * em - axis);
            if (up != null)
            {
                double yb = ay - 0.22 * em;
                r.Put(up, (len - up.W) / 2, yb - up.D);
                r.H = Math.Max(r.H, -(yb - up.D) + up.H);
            }
            if (dn != null)
            {
                double yt = ay + 0.22 * em;
                r.Put(dn, (len - dn.W) / 2, yt + dn.H);
                r.D = Math.Max(r.D, yt + dn.H + dn.D);
            }
            return r;
        }

        MBox Cancel(MNode n, int st)
        {
            MBox b = Node(n.A, st);
            double em = Em(st), pen = Math.Max(1.0, 0.05 * em), ext = 0.08 * em;
            MBox r = new MBox();
            r.Put(b, 0, 0);
            r.W = b.W;
            r.H = b.H;
            r.D = b.D;
            switch (n.Text)
            {
                case "sout":
                    r.Line(new PointF[] { P(0, -Axis(st)), P(b.W, -Axis(st)) }, pen);
                    break;
                case "bcancel":
                    r.Line(new PointF[] { P(-ext, -b.H - ext), P(b.W + ext, b.D + ext) }, pen);
                    break;
                case "xcancel":
                    r.Line(new PointF[] { P(-ext, b.D + ext), P(b.W + ext, -b.H - ext) }, pen);
                    r.Line(new PointF[] { P(-ext, -b.H - ext), P(b.W + ext, b.D + ext) }, pen);
                    break;
                default:
                    r.Line(new PointF[] { P(-ext, b.D + ext), P(b.W + ext, -b.H - ext) }, pen);
                    break;
            }
            return r;
        }
    }

    static class MathPaint
    {
        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;

        public static void Draw(Graphics g, MBox b, double x, double y, Color c, MathFonts F)
        {
            if (b == null || b.Empty || F == null) return;
            SmoothingMode sm = g.SmoothingMode;
            using (SolidBrush br = new SolidBrush(c))
            {
                try { Walk(g, b, x, y, c, br, F, 0); }
                catch (Exception) { }
                finally { g.SmoothingMode = sm; }
            }
        }

        static PointF[] Shift(PointF[] pts, double x, double y)
        {
            PointF[] r = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; i++) r[i] = new PointF((float)(pts[i].X + x), (float)(pts[i].Y + y));
            return r;
        }

        static void Walk(Graphics g, MBox b, double x, double y, Color c, SolidBrush br, MathFonts F, int depth)
        {
            if (b.Items == null || depth > 120) return;
            foreach (MItem it in b.Items)
            {
                double ix = x + it.X, iy = y + it.Y;
                switch (it.Kind)
                {
                    case MItem.Child:
                        Walk(g, it.Box, ix, iy, c, br, F, depth + 1);
                        break;
                    case MItem.Glyphs:
                        TextRenderer.DrawText(g, it.Text, it.Font, new Point((int)Math.Round(ix), (int)Math.Round(iy) - F.Ascent(it.Font)), c, Flags);
                        break;
                    case MItem.Fill:
                        {
                            int x0 = (int)Math.Round(ix), y0 = (int)Math.Round(iy);
                            int w = it.W < 3 ? Math.Max(1, (int)Math.Round(it.W)) : Math.Max(1, (int)Math.Round(ix + it.W) - x0);
                            int h = it.H < 3 ? Math.Max(1, (int)Math.Round(it.H)) : Math.Max(1, (int)Math.Round(iy + it.H) - y0);
                            g.SmoothingMode = SmoothingMode.None;
                            g.FillRectangle(br, x0, y0, w, h);
                            break;
                        }
                    case MItem.Line:
                        {
                            if (it.Pts == null || it.Pts.Length < 2) break;
                            PointF[] pts = Shift(it.Pts, ix, iy);
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            using (Pen p = new Pen(c, Math.Max(1f, it.Pen)))
                            {
                                p.StartCap = LineCap.Round;
                                p.EndCap = LineCap.Round;
                                p.LineJoin = LineJoin.Round;
                                if (pts.Length == 2) g.DrawLine(p, pts[0], pts[1]);
                                else g.DrawLines(p, pts);
                            }
                            break;
                        }
                    case MItem.Poly:
                        if (it.Pts == null || it.Pts.Length < 3) break;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.FillPolygon(br, Shift(it.Pts, ix, iy));
                        break;
                }
            }
        }
    }
}
