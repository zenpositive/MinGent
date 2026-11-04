using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace MinGent
{
    static class PdfWriter
    {
        const string Ink = "1F2328", Muted = "59636E", LinkBlue = "0969DA", LineColor = "D0D7DE", Soft = "F6F8FA", CodeBg = "EFF1F3";
        const double BodySize = 11, Margin = 72;

        class Unit
        {
            public double H, Before, Tail;
            public bool KeepNext, Header, Break;
            public Group G;
            public Action<Canvas, double, double> Draw;
            public int Outline;
            public string OutlineText;
        }

        class Group
        {
            public int Kind;
            public Unit HeaderUnit;
            public double X, W;
            public bool Rtl;
        }

        const int GCode = 1, GQuote = 2;

        class Placed
        {
            public Unit U;
            public double Y;
        }

        class Page
        {
            public readonly List<Placed> Items = new List<Placed>();
            public readonly StringBuilder Ops = new StringBuilder();
            public readonly List<string> Links = new List<string>();
            public readonly HashSet<string> Fonts = new HashSet<string>();
            public readonly HashSet<string> Images = new HashSet<string>();
        }

        class Canvas
        {
            public Page P;
            public double PageH;
            public Ctx C;

            static string N(double v) { return DocXml.Num(Math.Round(v, 3)); }

            public void Color(string hex, bool stroke)
            {
                int v = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                P.Ops.Append(N(((v >> 16) & 255) / 255.0)).Append(' ').Append(N(((v >> 8) & 255) / 255.0)).Append(' ').Append(N((v & 255) / 255.0)).Append(stroke ? " RG\n" : " rg\n");
            }

            public void Rect(double x, double yTop, double w, double h, string fill, string stroke, double lw, double radius)
            {
                if (w <= 0 || h <= 0) return;
                double y = PageH - yTop - h;
                if (fill != null) Color(fill, false);
                if (stroke != null) { Color(stroke, true); P.Ops.Append(N(lw)).Append(" w\n"); }
                if (radius <= 0) P.Ops.Append(N(x)).Append(' ').Append(N(y)).Append(' ').Append(N(w)).Append(' ').Append(N(h)).Append(" re\n");
                else
                {
                    double r = Math.Min(radius, Math.Min(w, h) / 2), k = r * 0.5523;
                    P.Ops.Append(N(x + r)).Append(' ').Append(N(y)).Append(" m\n");
                    P.Ops.Append(N(x + w - r)).Append(' ').Append(N(y)).Append(" l\n");
                    P.Ops.Append(N(x + w - r + k)).Append(' ').Append(N(y)).Append(' ').Append(N(x + w)).Append(' ').Append(N(y + r - k)).Append(' ').Append(N(x + w)).Append(' ').Append(N(y + r)).Append(" c\n");
                    P.Ops.Append(N(x + w)).Append(' ').Append(N(y + h - r)).Append(" l\n");
                    P.Ops.Append(N(x + w)).Append(' ').Append(N(y + h - r + k)).Append(' ').Append(N(x + w - r + k)).Append(' ').Append(N(y + h)).Append(' ').Append(N(x + w - r)).Append(' ').Append(N(y + h)).Append(" c\n");
                    P.Ops.Append(N(x + r)).Append(' ').Append(N(y + h)).Append(" l\n");
                    P.Ops.Append(N(x + r - k)).Append(' ').Append(N(y + h)).Append(' ').Append(N(x)).Append(' ').Append(N(y + h - r + k)).Append(' ').Append(N(x)).Append(' ').Append(N(y + h - r)).Append(" c\n");
                    P.Ops.Append(N(x)).Append(' ').Append(N(y + r)).Append(" l\n");
                    P.Ops.Append(N(x)).Append(' ').Append(N(y + r - k)).Append(' ').Append(N(x + r - k)).Append(' ').Append(N(y)).Append(' ').Append(N(x + r)).Append(' ').Append(N(y)).Append(" c\nh\n");
                }
                P.Ops.Append(fill != null && stroke != null ? "B\n" : fill != null ? "f\n" : "S\n");
            }

            public void Line(double x1, double y1, double x2, double y2, string color, double lw)
            {
                Color(color, true);
                P.Ops.Append(N(lw)).Append(" w 0 J\n").Append(N(x1)).Append(' ').Append(N(PageH - y1)).Append(" m ").Append(N(x2)).Append(' ').Append(N(PageH - y2)).Append(" l S\n");
            }

            public void Check(double x, double yTop, double size, bool done)
            {
                Rect(x, yTop, size, size, done ? "EEF3FB" : null, done ? "2B59C3" : "8C959F", 0.8, 1.6);
                if (!done) return;
                Color("2B59C3", true);
                double y = PageH - yTop - size;
                P.Ops.Append("1.1 w 1 J 1 j\n").Append(N(x + size * 0.22)).Append(' ').Append(N(y + size * 0.52)).Append(" m ").Append(N(x + size * 0.43)).Append(' ').Append(N(y + size * 0.3)).Append(" l ").Append(N(x + size * 0.8)).Append(' ').Append(N(y + size * 0.74)).Append(" l S\n0 J 0 j\n");
            }

            public void Image(string res, double x, double yTop, double w, double h)
            {
                P.Images.Add(res);
                P.Ops.Append("q ").Append(N(w)).Append(" 0 0 ").Append(N(h)).Append(' ').Append(N(x)).Append(' ').Append(N(PageH - yTop - h)).Append(" cm /").Append(res).Append(" Do Q\n");
            }

            public void Glyphs(List<LayGlyph> gs, double x, double baseline)
            {
                double cx = x;
                foreach (LayGlyph g in gs) { g.X = cx; cx += g.Adv; }
                int i = 0;
                while (i < gs.Count)
                {
                    LayGlyph g0 = gs[i];
                    if (g0.Face == null || (g0.Space && g0.Gid == 0)) { i++; continue; }
                    int j = i + 1;
                    while (j < gs.Count && gs[j].Face == g0.Face && gs[j].St.Size == g0.St.Size && Ink(gs[j]) == Ink(g0) && gs[j].FakeBold == g0.FakeBold && gs[j].FakeItalic == g0.FakeItalic) j++;
                    P.Fonts.Add(g0.Face.Res);
                    string color = Ink(g0);
                    P.Ops.Append("BT\n/").Append(g0.Face.Res).Append(' ').Append(N(g0.St.Size)).Append(" Tf\n");
                    Color(color, false);
                    if (g0.FakeBold) { Color(color, true); P.Ops.Append(N(g0.St.Size * 0.028)).Append(" w 2 Tr\n"); }
                    else P.Ops.Append("0 Tr\n");
                    P.Ops.Append(g0.FakeItalic ? "1 0 0.2 1 " : "1 0 0 1 ").Append(N(g0.X)).Append(' ').Append(N(PageH - baseline)).Append(" Tm\n<");
                    for (int k = i; k < j; k++) P.Ops.Append(gs[k].Gid.ToString("X4", CultureInfo.InvariantCulture));
                    P.Ops.Append("> Tj\nET\n");
                    i = j;
                }
            }

            static string Ink(LayGlyph g) { return g.St.Link != null ? LinkBlue : g.St.Color; }

            public void Hidden(List<LayGlyph> gs, double x, double baseline, double width)
            {
                if (gs == null || gs.Count == 0) return;
                double natural = 0;
                foreach (LayGlyph g in gs) natural += g.Adv;
                if (natural <= 0 || width <= 0) return;
                double k = width / natural;
                double cx = x;
                int i = 0;
                while (i < gs.Count)
                {
                    LayGlyph g0 = gs[i];
                    if (g0.Face == null) { cx += g0.Adv * k; i++; continue; }
                    int j = i + 1;
                    double runW = g0.Adv;
                    while (j < gs.Count && gs[j].Face == g0.Face && gs[j].St.Size == g0.St.Size) { runW += gs[j].Adv; j++; }
                    P.Fonts.Add(g0.Face.Res);
                    P.Ops.Append("BT\n/").Append(g0.Face.Res).Append(' ').Append(N(g0.St.Size)).Append(" Tf\n3 Tr ").Append(N(k * 100)).Append(" Tz\n1 0 0 1 ").Append(N(cx)).Append(' ').Append(N(PageH - baseline)).Append(" Tm\n<");
                    for (int q = i; q < j; q++) P.Ops.Append(gs[q].Gid.ToString("X4", CultureInfo.InvariantCulture));
                    P.Ops.Append("> Tj\n100 Tz 0 Tr\nET\n");
                    cx += runW * k;
                    i = j;
                }
            }

            public void Decorations(List<LayGlyph> gs, double baseline, double asc, double desc)
            {
                int i = 0;
                while (i < gs.Count)
                {
                    LayGlyph g = gs[i];
                    int j = i + 1;
                    if (g.St.Code && g.Face != null)
                    {
                        while (j < gs.Count && gs[j].St.Code) j++;
                        double x0 = gs[i].X, x1 = gs[j - 1].X + gs[j - 1].Adv;
                        Rect(x0 - 1.5, baseline - g.St.Size * 0.86, x1 - x0 + 3, g.St.Size * 1.16, CodeBg, null, 0, 2);
                    }
                    i = j;
                }
            }

            public void Lines(List<LayGlyph> gs, double baseline)
            {
                int i = 0;
                while (i < gs.Count)
                {
                    LayGlyph g = gs[i];
                    int j = i + 1;
                    if (g.St.Link != null)
                    {
                        while (j < gs.Count && gs[j].St.Link == g.St.Link) j++;
                        double x0 = gs[i].X, x1 = gs[j - 1].X + gs[j - 1].Adv;
                        Line(x0, baseline + g.St.Size * 0.12, x1, baseline + g.St.Size * 0.12, LinkBlue, Math.Max(0.4, g.St.Size * 0.05));
                        string uri = g.St.Link;
                        try { uri = new Uri(uri).AbsoluteUri; } catch (Exception) { }
                        P.Links.Add("/Type /Annot /Subtype /Link /Border [0 0 0] /Rect [" + N(x0) + " " + N(PageH - baseline - g.St.Size * 0.3) + " " + N(x1) + " " + N(PageH - baseline + g.St.Size * 0.95) + "] /A << /S /URI /URI " + Lit(uri) + " >>");
                    }
                    else if (g.St.Strike)
                    {
                        while (j < gs.Count && gs[j].St.Strike && gs[j].St.Link == null) j++;
                        double x0 = gs[i].X, x1 = gs[j - 1].X + gs[j - 1].Adv;
                        Line(x0, baseline - g.St.Size * 0.3, x1, baseline - g.St.Size * 0.3, g.St.Color, Math.Max(0.4, g.St.Size * 0.055));
                    }
                    i = j;
                }
            }
        }

        static string Lit(string s)
        {
            StringBuilder sb = new StringBuilder("(");
            foreach (char c in s)
            {
                if (c == '(' || c == ')' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32 || c > 126) sb.Append('?');
                else sb.Append(c);
            }
            return sb.Append(')').ToString();
        }

        static string TextString(string s)
        {
            StringBuilder sb = new StringBuilder("<FEFF");
            foreach (char c in DocModel.Clean(s ?? "")) sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            return sb.Append('>').ToString();
        }

        class Ctx
        {
            public LayFonts Fonts;
            public double PageW, PageH, Left, Right, Top, Bottom, Width;
            public readonly List<Unit> Units = new List<Unit>();
            public readonly Dictionary<DocPicture, string> Pictures = new Dictionary<DocPicture, string>();
            public readonly List<DocPicture> PictureList = new List<DocPicture>();
            public DocFormulas Formulas;
            public double BodyLine;

            public string Picture(DocPicture p)
            {
                string res;
                if (Pictures.TryGetValue(p, out res)) return res;
                res = "Im" + (PictureList.Count + 1).ToString(CultureInfo.InvariantCulture);
                Pictures[p] = res;
                PictureList.Add(p);
                return res;
            }
        }

        static LayStyle Base(double size, string color, bool bold, bool italic)
        {
            return new LayStyle { Size = size, Color = color, Bold = bold, Italic = italic };
        }

        static List<KeyValuePair<string, LayStyle>> Runs(List<DocSpan> spans, LayStyle baseStyle)
        {
            List<KeyValuePair<string, LayStyle>> runs = new List<KeyValuePair<string, LayStyle>>();
            if (spans == null) return runs;
            foreach (DocSpan s in spans)
            {
                if (s.Break) { runs.Add(new KeyValuePair<string, LayStyle>("\n", baseStyle)); continue; }
                LayStyle st = baseStyle.Clone();
                if (s.Bold) st.Bold = true;
                if (s.Italic) st.Italic = true;
                if (s.Strike) st.Strike = true;
                if (s.Link != null) st.Link = s.Link;
                if (s.Code) { st.Code = true; st.Mono = true; st.Size = baseStyle.Size * 0.9; }
                runs.Add(new KeyValuePair<string, LayStyle>(s.Text, st));
            }
            return runs;
        }

        static void Metrics(Ctx c, LayLine line, double size, double lineGap)
        {
            double asc = c.Fonts.AscentOf(false, size), h = c.Fonts.LineHeight(false, size);
            foreach (LayGlyph g in line.Glyphs)
            {
                if (g.Face == null) continue;
                double gs = g.St.Size;
                asc = Math.Max(asc, c.Fonts.AscentOf(g.St.Mono, gs));
                h = Math.Max(h, c.Fonts.LineHeight(g.St.Mono, gs));
            }
            line.Ascent = asc;
            line.Height = h * lineGap;
        }

        class Shaped
        {
            public DocPicture Pic;
            public double W, H, Ascent;
            public string Text = "";
        }

        const float ShapeScale = 3f;
        static HashSet<string> installed;

        public static bool NeedsShaping(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char ch in text)
                if ((ch >= '\u0900' && ch <= '\u0DFF') || (ch >= '\u0F00' && ch <= '\u109F') || (ch >= '\u1780' && ch <= '\u17FF') ||
                    (ch >= '\u1B00' && ch <= '\u1B7F') || (ch >= '\uA8E0' && ch <= '\uA8FF') || (ch >= '\uA980' && ch <= '\uA9DF')) return true;
            return false;
        }

        static string ShapeFamily(string text)
        {
            if (installed == null)
            {
                HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { using (InstalledFontCollection fc = new InstalledFontCollection()) foreach (FontFamily f in fc.Families) set.Add(f.Name); }
                catch (Exception) { }
                installed = set;
            }
            int[] counts = new int[4];
            char first = '\0';
            foreach (char ch in text)
            {
                if (ch >= '\u1000' && ch <= '\u109F') counts[1]++;
                else if (ch >= '\u1780' && ch <= '\u17FF') counts[2]++;
                else if (ch >= '\u0F00' && ch <= '\u0FFF') counts[3]++;
                else if (ch >= '\u0900' && ch <= '\u0DFF') { counts[0]++; if (first == '\0') first = ch; }
            }
            int best = 0;
            for (int i = 1; i < 4; i++) if (counts[i] > counts[best]) best = i;
            string[] wanted;
            if (best == 1) wanted = new string[] { "Myanmar Text", "Noto Sans Myanmar", "Padauk", "FreeSerif" };
            else if (best == 2) wanted = new string[] { "Leelawadee UI", "Khmer UI", "DaunPenh", "Noto Sans Khmer", "FreeSerif" };
            else if (best == 3) wanted = new string[] { "Microsoft Himalaya", "Noto Sans Tibetan", "FreeSerif" };
            else
            {
                string old = first >= '\u0D80' ? "Iskoola Pota" : first >= '\u0D00' ? "Kartika" : first >= '\u0C80' ? "Tunga" : first >= '\u0C00' ? "Gautami" : first >= '\u0B80' ? "Latha" :
                             first >= '\u0B00' ? "Kalinga" : first >= '\u0A80' ? "Shruti" : first >= '\u0A00' ? "Raavi" : first >= '\u0980' ? "Vrinda" : "Mangal";
                wanted = new string[] { "Nirmala UI", "Nirmala Text", old, "Noto Sans Devanagari", "Lohit Devanagari", "FreeSans", "FreeSerif" };
            }
            foreach (string f in wanted) if (installed.Contains(f)) return f;
            return null;
        }

        static List<Shaped> ShapeLines(string text, string family, LayStyle st, double widthPt)
        {
            List<Shaped> lines = new List<Shaped>();
            float k = ShapeScale;
            FontStyle fs = (st.Bold ? FontStyle.Bold : FontStyle.Regular) | (st.Italic ? FontStyle.Italic : FontStyle.Regular);
            int rgb = int.Parse(st.Color ?? Ink, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            using (Font f = new Font(family, (float)(st.Size * k), fs, GraphicsUnit.Pixel))
            using (Bitmap probe = new Bitmap(4, 4))
            using (Graphics pg = Graphics.FromImage(probe))
            using (StringFormat fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (StringFormat fit = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (SolidBrush ink = new SolidBrush(System.Drawing.Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255)))
            {
                pg.PageUnit = GraphicsUnit.Pixel;
                pg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                fit.FormatFlags |= StringFormatFlags.LineLimit;
                float lineH = f.GetHeight(pg);
                FontFamily ff = f.FontFamily;
                float ascent = f.Size * ff.GetCellAscent(fs) / Math.Max(1, ff.GetEmHeight(fs));
                float maxW = (float)Math.Max(8, widthPt * k);
                foreach (string hard in text.Split('\n'))
                {
                    string rest = hard.Trim();
                    if (rest.Length == 0) continue;
                    while (rest.Length > 0)
                    {
                        if (lines.Count >= 3000) return null;
                        string head = rest.Length > 600 ? rest.Substring(0, 600) : rest;
                        if (head.Length < rest.Length && char.IsHighSurrogate(head[head.Length - 1])) head = head.Substring(0, head.Length - 1);
                        int chars, rows;
                        pg.MeasureString(head, f, new SizeF(maxW, lineH * 1.5f), fit, out chars, out rows);
                        if (chars <= 0 || chars > head.Length) chars = head.Length;
                        while (chars < rest.Length && chars > 0 && char.IsLowSurrogate(rest[chars])) chars--;
                        if (chars == 0) chars = rest.Length;
                        string piece = rest.Substring(0, chars).TrimEnd();
                        rest = rest.Substring(chars).TrimStart();
                        if (piece.Length == 0) continue;
                        SizeF sz = pg.MeasureString(piece, f, new PointF(0, 0), fmt);
                        int pad = (int)Math.Ceiling(k);
                        int w = Math.Max(1, (int)Math.Ceiling(sz.Width) + 2 * pad), h = Math.Max(1, (int)Math.Ceiling(lineH) + 2 * pad);
                        if (w > 12000 || h > 4000) return null;
                        DocPicture pic = new DocPicture();
                        using (Bitmap bm = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics g = Graphics.FromImage(bm))
                            {
                                g.Clear(System.Drawing.Color.Transparent);
                                g.PageUnit = GraphicsUnit.Pixel;
                                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                                g.DrawString(piece, f, ink, pad, pad, fmt);
                            }
                            using (MemoryStream ms = new MemoryStream())
                            {
                                bm.Save(ms, ImageFormat.Png);
                                pic.Data = ms.ToArray();
                            }
                        }
                        pic.Width = w;
                        pic.Height = h;
                        pic.DpiX = pic.DpiY = 72.0 * k;
                        pic.Name = "Text";
                        pic.Alt = piece;
                        pic.Tint = rgb & 0xFFFFFF;
                        lines.Add(new Shaped { Pic = pic, W = w / k, H = h / k, Ascent = (ascent + pad) / k, Text = piece });
                    }
                }
            }
            return lines;
        }

        static void MeasureShaped(string text, string family, LayStyle st, out double minW, out double maxW)
        {
            minW = maxW = 0;
            float k = ShapeScale;
            FontStyle fs = (st.Bold ? FontStyle.Bold : FontStyle.Regular) | (st.Italic ? FontStyle.Italic : FontStyle.Regular);
            using (Font f = new Font(family, (float)(st.Size * k), fs, GraphicsUnit.Pixel))
            using (Bitmap probe = new Bitmap(4, 4))
            using (Graphics pg = Graphics.FromImage(probe))
            {
                pg.PageUnit = GraphicsUnit.Pixel;
                StringFormat fmt = StringFormat.GenericTypographic;
                foreach (string line in text.Split('\n'))
                {
                    string t = line.Trim();
                    if (t.Length == 0) continue;
                    maxW = Math.Max(maxW, pg.MeasureString(t, f, new PointF(0, 0), fmt).Width / k);
                    foreach (string word in t.Split(' '))
                        if (word.Length > 0) minW = Math.Max(minW, pg.MeasureString(word, f, new PointF(0, 0), fmt).Width / k);
                }
            }
            minW += 1;
            maxW += 1;
        }

        static List<LayGlyph> HiddenGlyphs(Ctx c, string text, LayStyle st)
        {
            int before = c.Fonts.Missing.Count;
            List<LayGlyph> gs = c.Fonts.Shape(new List<KeyValuePair<string, LayStyle>>(new KeyValuePair<string, LayStyle>[] { new KeyValuePair<string, LayStyle>(text, st) }));
            if (c.Fonts.Missing.Count > before) c.Fonts.Missing.RemoveRange(before, c.Fonts.Missing.Count - before);
            return gs;
        }

        static string ShapeText(List<DocSpan> spans)
        {
            StringBuilder sb = new StringBuilder();
            if (spans != null) foreach (DocSpan s in spans) sb.Append(s.Break ? "\n" : s.Text);
            return sb.ToString();
        }

        static bool ShapedText(Ctx c, List<DocSpan> spans, LayStyle st, double indent, double width, double lineGap, double before, double after, bool keepAll, Group g, Action<Canvas, double, double, LayLine, int> marker, int outline, double markerW)
        {
            string text = ShapeText(spans);
            string family = ShapeFamily(text);
            if (family == null) return false;
            List<Shaped> lines;
            try { lines = ShapeLines(text, family, st, width - indent - markerW); }
            catch (Exception) { return false; }
            if (lines == null || lines.Count == 0) return false;
            double natural = c.Fonts.LineHeight(false, st.Size);
            for (int i = 0; i < lines.Count; i++)
            {
                Shaped sl = lines[i];
                double lh = Math.Max(sl.H, natural) * lineGap;
                Unit u = new Unit();
                u.H = lh + (i == lines.Count - 1 ? after : 0);
                u.Tail = i == lines.Count - 1 ? after : 0;
                u.Before = i == 0 ? before : 0;
                u.G = g;
                u.KeepNext = keepAll ? true : (lines.Count > 1 && (i == 0 || i == lines.Count - 2));
                if (keepAll && i == lines.Count - 1) u.KeepNext = true;
                if (outline > 0 && i == 0) { u.Outline = outline; u.OutlineText = Plain(spans); }
                string res = c.Picture(sl.Pic);
                LayLine fake = new LayLine { Ascent = sl.Ascent, Height = lh, Width = sl.W };
                List<LayGlyph> hidden = HiddenGlyphs(c, sl.Text, st);
                int index = i;
                double lineIndent = indent, mw = markerW;
                u.Draw = delegate (Canvas cv, double x, double y)
                {
                    double top = y + (lh - sl.H) / 2;
                    double baseline = top + sl.Ascent;
                    double textX = x + lineIndent + mw;
                    if (marker != null && index == 0) marker(cv, x, baseline, fake, 0);
                    cv.Image(res, textX, top, sl.W, sl.H);
                    cv.Hidden(hidden, textX, baseline, sl.W);
                };
                c.Units.Add(u);
            }
            return true;
        }

        static void Text(Ctx c, List<DocSpan> spans, LayStyle st, double indent, double width, double lineGap, double before, double after, bool keepAll, Group g, Action<Canvas, double, double, LayLine, int> marker, int outline, double markerW)
        {
            if (NeedsShaping(ShapeText(spans)) && ShapedText(c, spans, st, indent, width, lineGap, before, after, keepAll, g, marker, outline, markerW)) return;
            bool rtl = DocBidi.RtlSpans(spans);
            List<LayGlyph> glyphs = c.Fonts.Shape(Runs(spans, st));
            List<LayLine> lines = LayFonts.Break(glyphs, width - indent - markerW, 0);
            for (int i = 0; i < lines.Count; i++)
            {
                LayLine line = lines[i];
                Metrics(c, line, st.Size, lineGap);
                LayFonts.Order(line, rtl);
                line.Rtl = rtl;
                Unit u = new Unit();
                u.H = line.Height + (i == lines.Count - 1 ? after : 0);
                u.Tail = i == lines.Count - 1 ? after : 0;
                u.Before = i == 0 ? before : 0;
                u.G = g;
                u.KeepNext = keepAll ? true : (lines.Count > 1 && (i == 0 || i == lines.Count - 2));
                if (keepAll && i == lines.Count - 1) u.KeepNext = true;
                if (outline > 0 && i == 0) { u.Outline = outline; u.OutlineText = Plain(spans); }
                int index = i;
                LayLine ln = line;
                double lineIndent = indent, mw = markerW;
                u.Draw = delegate (Canvas cv, double x, double y)
                {
                    double baseline = y + ln.Ascent + (ln.Height / Math.Max(0.01, lineGap) * (lineGap - 1)) / 2;
                    double textX = ln.Rtl ? x + c.Width - lineIndent - mw - ln.Width : x + lineIndent + mw;
                    if (marker != null && index == 0) marker(cv, x, baseline, ln, 0);
                    double cx = textX;
                    foreach (LayGlyph pg in ln.Glyphs) { pg.X = cx; cx += pg.Adv; }
                    cv.Decorations(ln.Glyphs, baseline, ln.Ascent, 0);
                    cv.Glyphs(ln.Glyphs, textX, baseline);
                    cv.Lines(ln.Glyphs, baseline);
                };
                c.Units.Add(u);
            }
        }

        static string Plain(List<DocSpan> spans)
        {
            StringBuilder sb = new StringBuilder();
            if (spans == null) return "";
            foreach (DocSpan s in spans) sb.Append(s.Break ? " " : s.Text);
            return sb.ToString().Trim();
        }

        static readonly double[] HSize = { 20, 15, 12.5, 11, 11, 11 };
        static readonly double[] HBefore = { 18, 15, 12, 10, 10, 10 };
        static readonly double[] HAfter = { 6, 5, 4, 3, 3, 3 };

        public static byte[] Build(DocModel m, List<string> problems, out int pageCount)
        {
            Ctx c = new Ctx();
            bool letter = DocPage.Letter;
            c.PageW = letter ? 612 : 595.28;
            c.PageH = letter ? 792 : 841.89;
            c.Left = Margin; c.Right = c.PageW - Margin; c.Top = Margin; c.Bottom = c.PageH - Margin;
            c.Width = c.Right - c.Left;
            StringBuilder sample = new StringBuilder();
            foreach (DocBlock b in m.Blocks) { sample.Append(b.Plain); if (sample.Length > 20000) break; }
            c.Fonts = new LayFonts(sample.ToString());
            if (!c.Fonts.Ok) { pageCount = 0; if (problems != null) problems.Add("no usable font was found on this PC"); return null; }
            c.BodyLine = c.Fonts.LineHeight(false, BodySize) * 1.12;
            using (c.Formulas = new DocFormulas(12))
            {
                for (int i = 0; i < m.Blocks.Count; i++)
                {
                    DocBlock next = i + 1 < m.Blocks.Count ? m.Blocks[i + 1] : null;
                    Block(c, m.Blocks[i], next);
                }
            }
            List<Page> pages = Paginate(c);
            foreach (string miss in c.Fonts.Missing)
                if (problems != null && problems.Count < 30) { problems.Add("no font on this PC has the character \"" + miss + "\", so it shows as a box"); }
            pageCount = pages.Count;
            return Write(c, pages, m.Title);
        }

        static void Block(Ctx c, DocBlock b, DocBlock next)
        {
            switch (b.Kind)
            {
                case DocKind.Heading:
                    {
                        int lv = Math.Max(1, Math.Min(6, b.Level));
                        LayStyle st = Base(HSize[lv - 1], lv >= 5 ? Muted : Ink, true, lv == 6);
                        Text(c, b.Spans, st, 0, c.Width, 1.1, HBefore[lv - 1], HAfter[lv - 1], true, null, null, lv <= 3 ? lv : 0, 0);
                        break;
                    }
                case DocKind.Para:
                    Text(c, b.Spans, Base(BodySize, Ink, false, false), 0, c.Width, 1.15, 0, 8, false, null, null, 0, 0);
                    break;
                case DocKind.Quote:
                    {
                        Group g = new Group { Kind = GQuote, X = 0, W = c.Width, Rtl = DocBidi.RtlSpans(b.Spans) };
                        Text(c, b.Spans, Base(BodySize, Muted, false, true), 14, c.Width, 1.15, 0, 8, false, g, null, 0, 0);
                        break;
                    }
                case DocKind.Item:
                    {
                        bool last = next == null || next.Kind != DocKind.Item || next.ListId != b.ListId;
                        int lvl = Math.Max(0, Math.Min(6, b.Level));
                        double indent = lvl * 18;
                        bool rtl = DocBidi.RtlSpans(b.Spans);
                        DocBlock item = b;
                        double markerW = 18;
                        Text(c, b.Spans, Base(BodySize, Ink, false, false), indent, c.Width, 1.15, 0, last ? 8 : 2.5, false, null, delegate (Canvas cv, double x, double baseline, LayLine ln, int k)
                        {
                            double mx = rtl ? x + c.Width - indent - markerW : x + indent;
                            if (item.Task >= 0)
                            {
                                double box = 8.6;
                                cv.Check(rtl ? mx + markerW - box - 2 : mx + 1, baseline - box + 0.6, box, item.Task == 1);
                                return;
                            }
                            string label = item.Ordered ? DocModel.OrderedLabel(lvl, item.Number) : DocModel.Bullet(lvl);
                            List<LayGlyph> gl = c.Fonts.Shape(new List<KeyValuePair<string, LayStyle>>(new KeyValuePair<string, LayStyle>[] { new KeyValuePair<string, LayStyle>(label, Base(BodySize, item.Ordered ? Ink : Muted, false, false)) }));
                            double w = 0;
                            foreach (LayGlyph pg in gl) w += pg.Adv;
                            double gx = rtl ? mx + markerW - w : mx;
                            if (!item.Ordered) gx = rtl ? mx + markerW - w - 4 : mx + 4;
                            cv.Glyphs(gl, gx, baseline);
                        }, 0, markerW);
                        break;
                    }
                case DocKind.Code:
                    {
                        Group g = new Group { Kind = GCode, X = 0, W = c.Width };
                        LayStyle st = new LayStyle { Mono = true, Size = 9.5, Color = Ink };
                        string[] lines = (b.Code ?? "").Split('\n');
                        double lh = c.Fonts.LineHeight(true, 9.5) * 1.05;
                        int start = c.Units.Count;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            List<LayGlyph> glyphs = c.Fonts.Shape(new List<KeyValuePair<string, LayStyle>>(new KeyValuePair<string, LayStyle>[] { new KeyValuePair<string, LayStyle>(lines[i].Length == 0 ? " " : lines[i], st) }));
                            List<LayLine> wrapped = LayFonts.Break(glyphs, c.Width - 20, 0);
                            for (int k = 0; k < wrapped.Count; k++)
                            {
                                LayLine ln = wrapped[k];
                                LayFonts.Order(ln, false);
                                Unit u = new Unit();
                                u.G = g;
                                u.H = lh;
                                u.KeepNext = lines.Length <= 12;
                                double asc = c.Fonts.AscentOf(true, 9.5);
                                u.Draw = delegate (Canvas cv, double x, double y) { cv.Glyphs(ln.Glyphs, x + 10, y + asc + lh * 0.02); };
                                c.Units.Add(u);
                            }
                        }
                        if (c.Units.Count > start)
                        {
                            c.Units[start].Before = 2;
                            c.Units[start].H += 8;
                            Action<Canvas, double, double> firstDraw = c.Units[start].Draw;
                            c.Units[start].Draw = delegate (Canvas cv, double x, double y) { firstDraw(cv, x, y + 8); };
                            Unit lastU = c.Units[c.Units.Count - 1];
                            lastU.H += 8 + 10;
                            lastU.Tail = 10;
                            lastU.KeepNext = false;
                        }
                        break;
                    }
                case DocKind.Rule:
                    {
                        Unit u = new Unit();
                        u.Before = 4;
                        u.H = 16;
                        u.Draw = delegate (Canvas cv, double x, double y) { cv.Line(x, y + 6, x + c.Width, y + 6, LineColor, 0.75); };
                        c.Units.Add(u);
                        break;
                    }
                case DocKind.PageBreak:
                    c.Units.Add(new Unit { Break = true });
                    break;
                case DocKind.Picture:
                    {
                        DocPicture p = b.Picture;
                        double w = p.Width / p.DpiX * 72, h = p.Height / p.DpiY * 72;
                        double k = Math.Min(1.0, Math.Min(c.Width / w, (c.Bottom - c.Top) * 0.85 / h));
                        w *= k; h *= k;
                        string res = c.Picture(p);
                        bool caption = p.Figure && (p.Alt ?? "").Trim().Length > 0;
                        Unit u = new Unit();
                        u.Before = 4;
                        u.H = h + (caption ? 6 : 12);
                        u.KeepNext = caption;
                        double iw = w, ih = h;
                        u.Draw = delegate (Canvas cv, double x, double y) { cv.Image(res, x + (c.Width - iw) / 2, y, iw, ih); };
                        c.Units.Add(u);
                        if (caption)
                        {
                            List<DocSpan> cap = new List<DocSpan>();
                            cap.Add(new DocSpan { Text = DocModel.Clean(p.Alt.Trim()) });
                            LayStyle cs = Base(9.5, Muted, false, true);
                            List<LayGlyph> glyphs = c.Fonts.Shape(Runs(cap, cs));
                            foreach (LayLine ln in LayFonts.Break(glyphs, c.Width, 0))
                            {
                                Metrics(c, ln, 9.5, 1.1);
                                LayFonts.Order(ln, DocBidi.RtlSpans(cap));
                                Unit cu = new Unit();
                                cu.H = ln.Height;
                                LayLine l2 = ln;
                                cu.Draw = delegate (Canvas cv, double x, double y) { cv.Glyphs(l2.Glyphs, x + (c.Width - l2.Width) / 2, y + l2.Ascent); };
                                c.Units.Add(cu);
                            }
                            c.Units[c.Units.Count - 1].H += 12;
                        }
                        break;
                    }
                case DocKind.Math:
                    MathBlock(c, b);
                    break;
                case DocKind.Table:
                    Table(c, b);
                    break;
            }
        }

        static void MathBlock(Ctx c, DocBlock b)
        {
            DocFormula f = c.Formulas.Render(b.Tex, c.Width * 0.82, System.Drawing.Color.FromArgb(0x1F, 0x23, 0x28));
            if (f == null)
            {
                List<DocSpan> sp = new List<DocSpan>();
                sp.Add(new DocSpan { Text = b.Code ?? "", Italic = true });
                List<LayGlyph> glyphs = c.Fonts.Shape(Runs(sp, Base(12, Ink, false, true)));
                List<LayLine> lines = LayFonts.Break(glyphs, c.Width, 0);
                for (int i = 0; i < lines.Count; i++)
                {
                    LayLine ln = lines[i];
                    Metrics(c, ln, 12, 1.1);
                    LayFonts.Order(ln, false);
                    Unit u = new Unit();
                    u.Before = i == 0 ? 4 : 0;
                    u.H = ln.Height + (i == lines.Count - 1 ? 10 : 0);
                    u.Draw = delegate (Canvas cv, double x, double y) { cv.Glyphs(ln.Glyphs, x + (c.Width - ln.Width) / 2, y + ln.Ascent); };
                    c.Units.Add(u);
                }
                return;
            }
            for (int i = 0; i < f.Lines.Count; i++)
            {
                DocPicture p = f.Lines[i];
                double w = p.Width / p.DpiX * 72, h = p.Height / p.DpiY * 72;
                double k = Math.Min(1.0, c.Width * 0.86 / w);
                w *= k; h *= k;
                string res = c.Picture(p);
                bool tagged = f.Tag.Length > 0 && i == f.Lines.Count - 1;
                Unit u = new Unit();
                u.Before = i == 0 ? 6 : 0;
                u.H = h + (i == f.Lines.Count - 1 ? 12 : 3);
                u.KeepNext = i < f.Lines.Count - 1;
                double iw = w, ih = h;
                List<LayGlyph> tag = null;
                double tagW = 0;
                if (tagged)
                {
                    tag = c.Fonts.Shape(new List<KeyValuePair<string, LayStyle>>(new KeyValuePair<string, LayStyle>[] { new KeyValuePair<string, LayStyle>(f.Tag, Base(BodySize, Ink, false, false)) }));
                    foreach (LayGlyph g in tag) tagW += g.Adv;
                }
                u.Draw = delegate (Canvas cv, double x, double y)
                {
                    cv.Image(res, x + (c.Width - iw) / 2, y, iw, ih);
                    if (tag != null) cv.Glyphs(tag, x + c.Width - tagW, y + ih / 2 + BodySize * 0.35);
                };
                c.Units.Add(u);
            }
        }

        static void Table(Ctx c, DocBlock t)
        {
            int cols = t.Rows[0].Count;
            const double padH = 5, padV = 3.5, size = 10;
            double[] minW = new double[cols], maxW = new double[cols];
            List<List<List<LayGlyph>>> glyphRows = new List<List<List<LayGlyph>>>();
            List<string[]> complexText = new List<string[]>();
            List<string[]> complexFamily = new List<string[]>();
            bool tableRtl = false, decided = false;
            for (int r = 0; r < t.Rows.Count; r++)
            {
                List<List<LayGlyph>> row = new List<List<LayGlyph>>();
                string[] ctext = new string[cols], cfam = new string[cols];
                complexText.Add(ctext);
                complexFamily.Add(cfam);
                for (int k = 0; k < cols; k++)
                {
                    List<DocSpan> cell = k < t.Rows[r].Count ? t.Rows[r][k] : new List<DocSpan>();
                    string plainCell = ShapeText(cell);
                    if (NeedsShaping(plainCell))
                    {
                        string fam = ShapeFamily(plainCell);
                        if (fam != null)
                        {
                            double mn, mx;
                            try
                            {
                                MeasureShaped(plainCell, fam, Base(size, Ink, r == 0, false), out mn, out mx);
                                ctext[k] = plainCell;
                                cfam[k] = fam;
                                minW[k] = Math.Max(minW[k], mn);
                                maxW[k] = Math.Max(maxW[k], mx);
                                row.Add(new List<LayGlyph>());
                                continue;
                            }
                            catch (Exception) { }
                        }
                    }
                    if (!decided)
                        foreach (DocSpan s in cell)
                        {
                            foreach (char ch in s.Text)
                            {
                                if (DocBidi.Rtl(ch)) { tableRtl = true; decided = true; break; }
                                if (char.IsLetter(ch)) { decided = true; break; }
                            }
                            if (decided) break;
                        }
                    List<LayGlyph> g = c.Fonts.Shape(Runs(cell, Base(size, Ink, r == 0, false)));
                    row.Add(g);
                    double word = 0, line = 0;
                    foreach (LayGlyph pg in g)
                    {
                        if (pg.Chars == "\n") { maxW[k] = Math.Max(maxW[k], line); line = 0; word = 0; continue; }
                        line += pg.Adv;
                        if (pg.Space || pg.BreakAfter) { minW[k] = Math.Max(minW[k], word + (pg.Space ? 0 : pg.Adv)); word = 0; }
                        else word += pg.Adv;
                    }
                    minW[k] = Math.Max(minW[k], word);
                    maxW[k] = Math.Max(maxW[k], line);
                }
                glyphRows.Add(row);
            }
            double avail = c.Width;
            double[] w = new double[cols];
            double sumMin = 0, sumMax = 0;
            for (int k = 0; k < cols; k++)
            {
                minW[k] = Math.Min(minW[k], avail * 0.5) + 2 * padH;
                maxW[k] = Math.Max(minW[k], maxW[k] + 2 * padH);
                sumMin += minW[k];
                sumMax += maxW[k];
            }
            if (sumMax <= avail)
                for (int k = 0; k < cols; k++) w[k] = maxW[k] + (avail - sumMax) * maxW[k] / sumMax;
            else if (sumMin >= avail)
                for (int k = 0; k < cols; k++) w[k] = minW[k] * avail / sumMin;
            else
                for (int k = 0; k < cols; k++) w[k] = minW[k] + (avail - sumMin) * (maxW[k] - minW[k]) / Math.Max(0.001, sumMax - sumMin);
            double[] colX = new double[cols];
            double acc = 0;
            for (int k = 0; k < cols; k++)
            {
                int vk = tableRtl ? cols - 1 - k : k;
                colX[vk] = acc;
                acc += w[vk];
            }
            double lh = c.Fonts.LineHeight(false, size) * 1.08, lh0 = lh;
            Group group = new Group { Kind = 0 };
            for (int r = 0; r < t.Rows.Count; r++)
            {
                List<List<LayLine>> cellLines = new List<List<LayLine>>();
                List<Shaped>[] cellShaped = new List<Shaped>[cols];
                List<LayGlyph>[][] cellHidden = new List<LayGlyph>[cols][];
                string[][] cellRes = new string[cols][];
                double rowH = 0;
                for (int k = 0; k < cols; k++)
                {
                    if (complexText[r][k] != null)
                    {
                        List<Shaped> sl = null;
                        try { sl = ShapeLines(complexText[r][k], complexFamily[r][k], Base(size, Ink, r == 0, false), w[k] - 2 * padH); }
                        catch (Exception) { sl = null; }
                        if (sl != null && sl.Count > 0)
                        {
                            cellShaped[k] = sl;
                            cellHidden[k] = new List<LayGlyph>[sl.Count];
                            cellRes[k] = new string[sl.Count];
                            double hsum = 0;
                            for (int q = 0; q < sl.Count; q++)
                            {
                                cellRes[k][q] = c.Picture(sl[q].Pic);
                                cellHidden[k][q] = HiddenGlyphs(c, sl[q].Text, Base(size, Ink, r == 0, false));
                                hsum += Math.Max(lh0, sl[q].H);
                            }
                            cellLines.Add(new List<LayLine>());
                            rowH = Math.Max(rowH, hsum);
                            continue;
                        }
                        glyphRows[r][k] = HiddenGlyphs(c, complexText[r][k], Base(size, Ink, r == 0, false));
                    }
                    List<LayLine> lines = LayFonts.Break(glyphRows[r][k], w[k] - 2 * padH, 0);
                    bool rtl = false;
                    foreach (LayGlyph pg in glyphRows[r][k]) { if (pg.Cat == LayFonts.R) { rtl = true; break; } if (pg.Cat == LayFonts.L) break; }
                    foreach (LayLine ln in lines) { LayFonts.Order(ln, rtl); ln.Rtl = rtl; }
                    cellLines.Add(lines);
                    rowH = Math.Max(rowH, lines.Count * lh);
                }
                bool head = r == 0;
                double asc = c.Fonts.AscentOf(false, size);
                List<List<LayLine>> cl = cellLines;
                List<Shaped>[] cs = cellShaped;
                List<LayGlyph>[][] ch = cellHidden;
                string[][] cr = cellRes;
                bool lastRow = r == t.Rows.Count - 1;
                List<double>[] hts = new List<double>[cols];
                for (int k = 0; k < cols; k++)
                {
                    hts[k] = new List<double>();
                    if (cs[k] != null) foreach (Shaped sl in cs[k]) hts[k].Add(Math.Max(lh, sl.H));
                    else foreach (LayLine ln in cl[k]) hts[k].Add(lh);
                }
                double limit = c.Bottom - c.Top - (group.HeaderUnit != null ? group.HeaderUnit.H : 0) - 2 * padV - 12;
                List<int[][]> chunks = new List<int[][]>();
                List<double> chunkH = new List<double>();
                if (head || rowH <= limit || limit < lh * 3)
                {
                    int[][] all = new int[cols][];
                    for (int k = 0; k < cols; k++) all[k] = new int[] { 0, hts[k].Count };
                    chunks.Add(all);
                    chunkH.Add(rowH);
                }
                else
                {
                    int[] at = new int[cols];
                    while (true)
                    {
                        bool more = false;
                        for (int k = 0; k < cols; k++) if (at[k] < hts[k].Count) more = true;
                        if (!more) break;
                        int[][] range = new int[cols][];
                        double hmax = 0;
                        for (int k = 0; k < cols; k++)
                        {
                            int from = at[k];
                            double hsum = 0;
                            while (at[k] < hts[k].Count && (hsum + hts[k][at[k]] <= limit || at[k] == from)) { hsum += hts[k][at[k]]; at[k]++; }
                            range[k] = new int[] { from, at[k] };
                            hmax = Math.Max(hmax, hsum);
                        }
                        chunks.Add(range);
                        chunkH.Add(hmax);
                    }
                }
                for (int j = 0; j < chunks.Count; j++)
                {
                    int[][] range = chunks[j];
                    double rh = chunkH[j] + 2 * padV;
                    Unit u = new Unit();
                    u.H = rh;
                    u.G = group;
                    u.Header = head;
                    u.Before = head ? 2 : 0;
                    u.KeepNext = head;
                    u.Draw = delegate (Canvas cv, double x, double y)
                    {
                        if (head) cv.Rect(x, y, c.Width, rh, Soft, null, 0, 0);
                        for (int k = 0; k < cols; k++)
                        {
                            double cx = x + colX[k];
                            char al = k < t.Align.Count ? t.Align[k] : 'l';
                            double ly = y + padV;
                            if (cs[k] != null)
                            {
                                for (int q = range[k][0]; q < range[k][1]; q++)
                                {
                                    Shaped sl = cs[k][q];
                                    double inner = w[k] - 2 * padH;
                                    double tx = al == 'c' ? cx + padH + (inner - sl.W) / 2 : al == 'r' ? cx + w[k] - padH - sl.W : cx + padH;
                                    double slot = Math.Max(lh, sl.H);
                                    double top = ly + (slot - sl.H) / 2;
                                    cv.Image(cr[k][q], tx, top, sl.W, sl.H);
                                    cv.Hidden(ch[k][q], tx, top + sl.Ascent, sl.W);
                                    ly += slot;
                                }
                                continue;
                            }
                            for (int q = range[k][0]; q < range[k][1]; q++)
                            {
                                LayLine ln = cl[k][q];
                                double inner = w[k] - 2 * padH;
                                double tx = al == 'c' ? cx + padH + (inner - ln.Width) / 2 : (al == 'r' || ln.Rtl) ? cx + w[k] - padH - ln.Width : cx + padH;
                                double cxx = tx;
                                foreach (LayGlyph pg in ln.Glyphs) { pg.X = cxx; cxx += pg.Adv; }
                                cv.Decorations(ln.Glyphs, ly + asc, asc, 0);
                                cv.Glyphs(ln.Glyphs, tx, ly + asc + (lh - c.Fonts.LineHeight(false, size)) / 2);
                                cv.Lines(ln.Glyphs, ly + asc);
                                ly += lh;
                            }
                        }
                        cv.Line(x, y, x + c.Width, y, LineColor, 0.6);
                        cv.Line(x, y + rh, x + c.Width, y + rh, head ? "B8C0CC" : LineColor, head ? 0.9 : 0.6);
                        for (int k = 0; k <= cols; k++)
                        {
                            double vx = k == cols ? x + c.Width : x + colX[k];
                            cv.Line(vx, y, vx, y + rh, LineColor, 0.6);
                        }
                    };
                    if (head) group.HeaderUnit = u;
                    if (lastRow && j == chunks.Count - 1) { u.H += 10; u.Tail = 10; }
                    c.Units.Add(u);
                }
            }
        }

        static List<Page> Paginate(Ctx c)
        {
            List<Page> pages = new List<Page>();
            Page page = new Page();
            pages.Add(page);
            double y = c.Top;
            double body = c.Bottom - c.Top;
            for (int i = 0; i < c.Units.Count; i++)
            {
                Unit u = c.Units[i];
                if (u.Break)
                {
                    if (page.Items.Count > 0) { page = new Page(); pages.Add(page); y = c.Top; }
                    continue;
                }
                double before = y <= c.Top + 0.01 ? 0 : u.Before;
                double chain = before + u.H;
                for (int k = i; k < c.Units.Count - 1 && c.Units[k].KeepNext && chain < body; k++)
                {
                    Unit nx = c.Units[k + 1];
                    if (nx.Break) break;
                    chain += nx.Before + nx.H;
                }
                if (chain > body) chain = before + u.H;
                if (y + chain > c.Bottom && y > c.Top + 0.01)
                {
                    page = new Page();
                    pages.Add(page);
                    y = c.Top;
                    before = 0;
                    if (u.G != null && u.G.HeaderUnit != null && !u.Header && u.G.HeaderUnit != u)
                    {
                        page.Items.Add(new Placed { U = u.G.HeaderUnit, Y = y });
                        y += u.G.HeaderUnit.H;
                    }
                }
                page.Items.Add(new Placed { U = u, Y = y + before });
                y += before + u.H;
            }
            return pages;
        }

        static void Render(Ctx c, Page page)
        {
            Canvas cv = new Canvas { P = page, PageH = c.PageH, C = c };
            Dictionary<Group, double[]> spans = new Dictionary<Group, double[]>();
            List<Group> order = new List<Group>();
            foreach (Placed p in page.Items)
            {
                Group g = p.U.G;
                if (g == null || (g.Kind != GCode && g.Kind != GQuote)) continue;
                double[] s;
                if (!spans.TryGetValue(g, out s)) { s = new double[] { p.Y, p.Y + p.U.H - p.U.Tail }; spans[g] = s; order.Add(g); }
                s[1] = p.Y + p.U.H - p.U.Tail;
            }
            foreach (Group g in order)
            {
                double[] s = spans[g];
                if (g.Kind == GCode) cv.Rect(c.Left + g.X, s[0], g.W, Math.Max(4, s[1] - s[0]), Soft, LineColor, 0.6, 3);
                else
                {
                    double bx = g.Rtl ? c.Left + c.Width - 2.5 : c.Left;
                    cv.Rect(bx, s[0] + 1, 2.5, Math.Max(4, s[1] - s[0] - 2), LineColor, null, 0, 0);
                }
            }
            foreach (Placed p in page.Items) if (p.U.Draw != null) p.U.Draw(cv, c.Left, p.Y);
        }

        static byte[] Zlib(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0xDA);
                using (DeflateStream z = new DeflateStream(ms, CompressionLevel.Optimal, true)) z.Write(data, 0, data.Length);
                uint a = 1, b = 0;
                foreach (byte x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
                uint ad = (b << 16) | a;
                ms.WriteByte((byte)(ad >> 24)); ms.WriteByte((byte)(ad >> 16)); ms.WriteByte((byte)(ad >> 8)); ms.WriteByte((byte)ad);
                return ms.ToArray();
            }
        }

        class Objects
        {
            readonly List<byte[]> objs = new List<byte[]>();
            static readonly Encoding Latin = Encoding.GetEncoding(28591);

            public int Reserve() { objs.Add(null); return objs.Count; }
            public void Set(int id, string body) { objs[id - 1] = Latin.GetBytes(id + " 0 obj\n" + body + "\nendobj\n"); }
            public int Add(string body) { int id = Reserve(); Set(id, body); return id; }

            public void SetStream(int id, string dict, byte[] data, bool compress)
            {
                byte[] payload = compress ? Zlib(data) : data;
                string head = id + " 0 obj\n<< " + dict + (compress ? " /Filter /FlateDecode" : "") + " /Length " + payload.Length + " >>\nstream\n";
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] h = Latin.GetBytes(head);
                    ms.Write(h, 0, h.Length);
                    ms.Write(payload, 0, payload.Length);
                    byte[] t = Latin.GetBytes("\nendstream\nendobj\n");
                    ms.Write(t, 0, t.Length);
                    objs[id - 1] = ms.ToArray();
                }
            }

            public int AddStream(string dict, byte[] data, bool compress) { int id = Reserve(); SetStream(id, dict, data, compress); return id; }

            public byte[] Finish(int root, int info)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] head = Latin.GetBytes("%PDF-1.7\n%\u00E2\u00E3\u00CF\u00D3\n");
                    ms.Write(head, 0, head.Length);
                    long[] offsets = new long[objs.Count];
                    for (int i = 0; i < objs.Count; i++)
                    {
                        offsets[i] = ms.Position;
                        byte[] o = objs[i] ?? Latin.GetBytes((i + 1) + " 0 obj\nnull\nendobj\n");
                        ms.Write(o, 0, o.Length);
                    }
                    long xref = ms.Position;
                    StringBuilder sb = new StringBuilder();
                    sb.Append("xref\n0 ").Append(objs.Count + 1).Append("\n0000000000 65535 f \n");
                    foreach (long off in offsets) sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
                    string id;
                    using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                    {
                        byte[] hash = md5.ComputeHash(ms.ToArray());
                        StringBuilder hx = new StringBuilder();
                        foreach (byte b in hash) hx.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                        id = hx.ToString();
                    }
                    sb.Append("trailer\n<< /Size ").Append(objs.Count + 1).Append(" /Root ").Append(root).Append(" 0 R /Info ").Append(info).Append(" 0 R /ID [<").Append(id).Append("> <").Append(id).Append(">] >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
                    byte[] tail = Latin.GetBytes(sb.ToString());
                    ms.Write(tail, 0, tail.Length);
                    return ms.ToArray();
                }
            }
        }

        static string ToUnicode(LayFace f)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
            List<int> gids = new List<int>(f.Text.Keys);
            gids.Sort();
            for (int i = 0; i < gids.Count; i += 100)
            {
                int n = Math.Min(100, gids.Count - i);
                sb.Append(n).Append(" beginbfchar\n");
                for (int k = i; k < i + n; k++)
                {
                    sb.Append('<').Append(gids[k].ToString("X4", CultureInfo.InvariantCulture)).Append("> <");
                    foreach (char ch in f.Text[gids[k]]) sb.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                    sb.Append(">\n");
                }
                sb.Append("endbfchar\n");
            }
            sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
            return sb.ToString();
        }

        static int Font(Objects o, LayFace f)
        {
            TtfFont t = f.Font;
            byte[] sub = t.Subset(f.Used, f.Uni);
            int file = o.AddStream("/Length1 " + sub.Length, sub, true);
            double k = 1000.0 / t.UnitsPerEm;
            string name = f.Tag + "+" + t.Name;
            int desc = o.Add("<< /Type /FontDescriptor /FontName /" + name + " /Flags " + t.Flags + " /FontBBox [" + (int)(t.XMin * k) + " " + (int)(t.YMin * k) + " " + (int)(t.XMax * k) + " " + (int)(t.YMax * k) + "] /ItalicAngle " + DocXml.Num(t.ItalicAngle) +
                " /Ascent " + (int)(t.Ascent * k) + " /Descent " + (int)(t.Descent * k) + " /CapHeight " + (int)(t.CapHeight * k) + " /StemV " + (t.Weight >= 600 ? 120 : 80) + " /FontFile2 " + file + " 0 R >>");
            List<int> gids = new List<int>(f.Used);
            gids.Sort();
            StringBuilder w = new StringBuilder("[");
            int i = 0;
            while (i < gids.Count)
            {
                int j = i;
                while (j + 1 < gids.Count && gids[j + 1] == gids[j] + 1) j++;
                w.Append(gids[i]).Append(" [");
                for (int g = i; g <= j; g++) w.Append((int)Math.Round(t.Advance(gids[g]) * k)).Append(g < j ? " " : "");
                w.Append("] ");
                i = j + 1;
            }
            w.Append(']');
            int cid = o.Add("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /" + name + " /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor " + desc + " 0 R /DW 1000 /W " + w + " /CIDToGIDMap /Identity >>");
            byte[] cmap = Encoding.ASCII.GetBytes(ToUnicode(f));
            int tu = o.AddStream("", cmap, true);
            return o.Add("<< /Type /Font /Subtype /Type0 /BaseFont /" + name + " /Encoding /Identity-H /DescendantFonts [" + cid + " 0 R] /ToUnicode " + tu + " 0 R >>");
        }

        static int ImageObject(Objects o, DocPicture p)
        {
            if (p.Jpeg)
            {
                int comps = JpegComponents(p.Data);
                return o.AddStream("/Type /XObject /Subtype /Image /Width " + p.Width + " /Height " + p.Height + " /ColorSpace /" + (comps == 1 ? "DeviceGray" : "DeviceRGB") + " /BitsPerComponent 8 /Filter /DCTDecode", p.Data, false);
            }
            using (MemoryStream ms = new MemoryStream(p.Data))
            using (Bitmap src = new Bitmap(ms))
            using (Bitmap bm = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bm)) { g.Clear(Color.Transparent); g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height)); }
                int w = bm.Width, h = bm.Height;
                byte[] rgb = new byte[w * h * 3], alpha = new byte[w * h];
                bool translucent = false;
                BitmapData d = bm.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int[] row = new int[w];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, w);
                        for (int x = 0; x < w; x++)
                        {
                            int px = row[x];
                            int at = y * w + x;
                            rgb[at * 3] = (byte)(px >> 16); rgb[at * 3 + 1] = (byte)(px >> 8); rgb[at * 3 + 2] = (byte)px;
                            byte a = (byte)((px >> 24) & 255);
                            alpha[at] = a;
                            if (a != 255) translucent = true;
                        }
                    }
                }
                finally { bm.UnlockBits(d); }
                string smask = "";
                if (translucent)
                {
                    int sm = o.AddStream("/Type /XObject /Subtype /Image /Width " + w + " /Height " + h + " /ColorSpace /DeviceGray /BitsPerComponent 8", alpha, true);
                    smask = " /SMask " + sm + " 0 R";
                    if (p.Tint >= 0)
                        return o.AddStream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8" + smask, new byte[] { (byte)(p.Tint >> 16), (byte)(p.Tint >> 8), (byte)p.Tint }, true);
                }
                return o.AddStream("/Type /XObject /Subtype /Image /Width " + w + " /Height " + h + " /ColorSpace /DeviceRGB /BitsPerComponent 8" + smask, rgb, true);
            }
        }

        static int JpegComponents(byte[] d)
        {
            int i = 2;
            while (i + 9 < d.Length)
            {
                if (d[i] != 0xFF) return 3;
                byte m = d[i + 1];
                if (m == 0xC0 || m == 0xC1 || m == 0xC2) return d[i + 9];
                int len = (d[i + 2] << 8) | d[i + 3];
                i += 2 + len;
            }
            return 3;
        }

        static byte[] Write(Ctx c, List<Page> pages, string title)
        {
            Objects o = new Objects();
            int catalog = o.Reserve(), pagesObj = o.Reserve(), info = o.Reserve();
            List<int> pageIds = new List<int>();
            List<int> contentIds = new List<int>();
            foreach (Page p in pages) { pageIds.Add(o.Reserve()); contentIds.Add(o.Reserve()); }
            List<string[]> outline = new List<string[]>();
            for (int i = 0; i < pages.Count; i++)
            {
                Page p = pages[i];
                Render(c, p);
                foreach (Placed pl in p.Items)
                    if (pl.U.Outline > 0 && !string.IsNullOrEmpty(pl.U.OutlineText))
                        outline.Add(new string[] { pl.U.Outline.ToString(CultureInfo.InvariantCulture), pl.U.OutlineText, i.ToString(CultureInfo.InvariantCulture), DocXml.Num(Math.Round(c.PageH - pl.Y + 6, 1)) });
                if (pages.Count > 1 && i > 0)
                {
                    Canvas cv = new Canvas { P = p, PageH = c.PageH, C = c };
                    List<LayGlyph> num = c.Fonts.Shape(new List<KeyValuePair<string, LayStyle>>(new KeyValuePair<string, LayStyle>[] { new KeyValuePair<string, LayStyle>((i + 1).ToString(CultureInfo.InvariantCulture), Base(9, Muted, false, false)) }));
                    double nw = 0;
                    foreach (LayGlyph g in num) nw += g.Adv;
                    cv.Glyphs(num, (c.PageW - nw) / 2, c.PageH - 36);
                }
            }
            Dictionary<LayFace, int> fontIds = new Dictionary<LayFace, int>();
            foreach (LayFace f in c.Fonts.All) if (f.Used.Count > 0) fontIds[f] = Font(o, f);
            Dictionary<string, int> imageIds = new Dictionary<string, int>();
            foreach (DocPicture pic in c.PictureList)
            {
                try { imageIds[c.Pictures[pic]] = ImageObject(o, pic); }
                catch (Exception) { }
            }
            for (int i = 0; i < pages.Count; i++)
            {
                Page p = pages[i];
                o.SetStream(contentIds[i], "", Encoding.GetEncoding(28591).GetBytes(p.Ops.ToString()), true);
                StringBuilder res = new StringBuilder("<< /ProcSet [/PDF /Text /ImageB /ImageC]");
                if (p.Fonts.Count > 0)
                {
                    res.Append(" /Font <<");
                    foreach (LayFace f in c.Fonts.All) if (p.Fonts.Contains(f.Res) && fontIds.ContainsKey(f)) res.Append(" /").Append(f.Res).Append(' ').Append(fontIds[f]).Append(" 0 R");
                    res.Append(" >>");
                }
                if (p.Images.Count > 0)
                {
                    res.Append(" /XObject <<");
                    foreach (string im in p.Images) if (imageIds.ContainsKey(im)) res.Append(" /").Append(im).Append(' ').Append(imageIds[im]).Append(" 0 R");
                    res.Append(" >>");
                }
                res.Append(" >>");
                StringBuilder annots = new StringBuilder();
                if (p.Links.Count > 0)
                {
                    annots.Append(" /Annots [");
                    foreach (string l in p.Links) annots.Append(o.Add("<< " + l + " >>")).Append(" 0 R ");
                    annots.Append(']');
                }
                o.Set(pageIds[i], "<< /Type /Page /Parent " + pagesObj + " 0 R /MediaBox [0 0 " + DocXml.Num(c.PageW) + " " + DocXml.Num(c.PageH) + "] /Resources " + res + " /Contents " + contentIds[i] + " 0 R" + annots + " >>");
            }
            StringBuilder kids = new StringBuilder();
            foreach (int id in pageIds) kids.Append(id).Append(" 0 R ");
            o.Set(pagesObj, "<< /Type /Pages /Kids [" + kids.ToString().TrimEnd() + "] /Count " + pageIds.Count + " >>");
            string outlines = "";
            if (outline.Count > 1)
            {
                int root = o.Reserve();
                int[] ids = new int[outline.Count];
                for (int i = 0; i < outline.Count; i++) ids[i] = o.Reserve();
                int[] parent = new int[outline.Count];
                List<int>[] children = new List<int>[outline.Count];
                List<int> top = new List<int>();
                for (int i = 0; i < outline.Count; i++)
                {
                    children[i] = new List<int>();
                    int lv = int.Parse(outline[i][0], CultureInfo.InvariantCulture);
                    int p = -1;
                    for (int k = i - 1; k >= 0; k--) if (int.Parse(outline[k][0], CultureInfo.InvariantCulture) < lv) { p = k; break; }
                    parent[i] = p;
                    if (p < 0) top.Add(i); else children[p].Add(i);
                }
                for (int i = 0; i < outline.Count; i++)
                {
                    List<int> sib = parent[i] < 0 ? top : children[parent[i]];
                    int at = sib.IndexOf(i);
                    StringBuilder ob = new StringBuilder("<< /Title " + TextString(outline[i][1]) + " /Parent " + (parent[i] < 0 ? root : ids[parent[i]]) + " 0 R");
                    if (at > 0) ob.Append(" /Prev ").Append(ids[sib[at - 1]]).Append(" 0 R");
                    if (at < sib.Count - 1) ob.Append(" /Next ").Append(ids[sib[at + 1]]).Append(" 0 R");
                    if (children[i].Count > 0) ob.Append(" /First ").Append(ids[children[i][0]]).Append(" 0 R /Last ").Append(ids[children[i][children[i].Count - 1]]).Append(" 0 R /Count -").Append(children[i].Count);
                    ob.Append(" /Dest [").Append(pageIds[int.Parse(outline[i][2], CultureInfo.InvariantCulture)]).Append(" 0 R /XYZ 0 ").Append(outline[i][3]).Append(" 0] >>");
                    o.Set(ids[i], ob.ToString());
                }
                o.Set(root, "<< /Type /Outlines /First " + ids[top[0]] + " 0 R /Last " + ids[top[top.Count - 1]] + " 0 R /Count " + top.Count + " >>");
                outlines = " /Outlines " + root + " 0 R /PageMode /UseOutlines";
            }
            o.Set(catalog, "<< /Type /Catalog /Pages " + pagesObj + " 0 R" + outlines + " /ViewerPreferences << /DisplayDocTitle true >> >>");
            string author = DocXml.Author();
            o.Set(info, "<< " + (string.IsNullOrEmpty(title) ? "" : "/Title " + TextString(title) + " ") + (string.IsNullOrEmpty(author) ? "" : "/Author " + TextString(author) + " ") + "/CreationDate (D:" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z) >>");
            return o.Finish(catalog, info);
        }
    }
}
