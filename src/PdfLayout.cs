using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MinGent
{
    class LayFace
    {
        public TtfFont Font;
        public string Res = "";
        public string Tag = "";
        public readonly HashSet<int> Used = new HashSet<int>();
        public readonly Dictionary<int, string> Text = new Dictionary<int, string>();
        public readonly Dictionary<int, int> Uni = new Dictionary<int, int>();
    }

    class LayStyle
    {
        public bool Mono, Bold, Italic, Code, Strike;
        public double Size = 11;
        public string Color = "1F2328";
        public string Link;

        public LayStyle Clone()
        {
            return new LayStyle { Mono = Mono, Bold = Bold, Italic = Italic, Code = Code, Strike = Strike, Size = Size, Color = Color, Link = Link };
        }
    }

    class LayGlyph
    {
        public LayFace Face;
        public int Gid;
        public double Adv;
        public string Chars = "";
        public int Cat;
        public bool Space, BreakAfter, NoBreakBefore;
        public LayStyle St;
        public bool FakeBold, FakeItalic;
        public int Level;
        public double X;
    }

    class LayLine
    {
        public List<LayGlyph> Glyphs = new List<LayGlyph>();
        public double Width, Ascent, Height;
        public bool Rtl;
    }

    class LayFonts
    {
        public const int L = 0, R = 1, EN = 2, AN = 3, WS = 4, ON = 5, NSM = 6;

        static readonly string[][] Body = {
            new string[] { "calibri.ttf", "Carlito-Regular.ttf", "segoeui.ttf", "arial.ttf", "LiberationSans-Regular.ttf", "DejaVuSans.ttf" },
            new string[] { "calibrib.ttf", "Carlito-Bold.ttf", "segoeuib.ttf", "arialbd.ttf", "LiberationSans-Bold.ttf", "DejaVuSans-Bold.ttf" },
            new string[] { "calibrii.ttf", "Carlito-Italic.ttf", "segoeuii.ttf", "ariali.ttf", "LiberationSans-Italic.ttf", "DejaVuSans-Oblique.ttf" },
            new string[] { "calibriz.ttf", "Carlito-BoldItalic.ttf", "segoeuiz.ttf", "arialbi.ttf", "LiberationSans-BoldItalic.ttf", "DejaVuSans-BoldOblique.ttf" } };

        static readonly string[][] Mono = {
            new string[] { "consola.ttf", "DejaVuSansMono.ttf", "LiberationMono-Regular.ttf", "cour.ttf" },
            new string[] { "consolab.ttf", "DejaVuSansMono-Bold.ttf", "LiberationMono-Bold.ttf", "courbd.ttf" } };

        static readonly string[] Latin = { "segoeui.ttf", "arial.ttf", "tahoma.ttf", "times.ttf", "DejaVuSans.ttf", "FreeSans.ttf" };
        static readonly string[] Symbols = { "seguisym.ttf", "cambria.ttc#1", "seguiemj.ttf", "DejaVuSans.ttf", "DejaVuMathTeXGyre.ttf", "FreeSerif.ttf", "FreeSans.ttf" };
        static readonly string[] Chinese = { "msyh.ttc", "simsun.ttc", "msjh.ttc", "wqy-zenhei.ttc" };
        static readonly string[] Japanese = { "YuGothR.ttc", "meiryo.ttc", "msgothic.ttc", "ipag.ttf", "fonts-japanese-gothic.ttf", "wqy-zenhei.ttc" };
        static readonly string[] Korean = { "malgun.ttf", "gulim.ttc", "wqy-zenhei.ttc" };
        static readonly string[] Others = { "Nirmala.ttf", "LeelawUI.ttf", "ebrima.ttf", "gadugi.ttf", "seguihis.ttf", "arialuni.ttf", "FreeSerif.ttf" };

        readonly FontCache cache = new FontCache();
        readonly TtfFont[] body = new TtfFont[4];
        readonly TtfFont[] mono = new TtfFont[2];
        readonly Dictionary<TtfFont, LayFace> faces = new Dictionary<TtfFont, LayFace>();
        public readonly List<LayFace> All = new List<LayFace>();
        readonly List<string> fallback = new List<string>();
        readonly Dictionary<long, KeyValuePair<TtfFont, int>> memo = new Dictionary<long, KeyValuePair<TtfFont, int>>();
        public readonly List<string> Missing = new List<string>();
        public bool Ok;

        public LayFonts(string sample)
        {
            for (int i = 0; i < 4; i++) body[i] = cache.First(Body[i]);
            for (int i = 0; i < 2; i++) mono[i] = cache.First(Mono[i]);
            if (body[0] == null) body[0] = cache.First(Latin);
            if (mono[0] == null) mono[0] = body[0];
            Ok = body[0] != null;
            bool kana = false, hangul = false;
            foreach (char c in sample ?? "")
            {
                if (c >= '\u3040' && c <= '\u30FF') kana = true;
                else if (c >= '\uAC00' && c <= '\uD7A3') hangul = true;
            }
            fallback.AddRange(Latin);
            fallback.AddRange(Symbols);
            if (kana) { fallback.AddRange(Japanese); fallback.AddRange(Chinese); fallback.AddRange(Korean); }
            else if (hangul) { fallback.AddRange(Korean); fallback.AddRange(Chinese); fallback.AddRange(Japanese); }
            else { fallback.AddRange(Chinese); fallback.AddRange(Japanese); fallback.AddRange(Korean); }
            fallback.AddRange(Others);
        }

        public double LineHeight(bool monoFace, double size)
        {
            TtfFont f = monoFace ? mono[0] : body[0];
            if (f == null) return size * 1.2;
            return (f.Ascent - f.Descent + f.LineGap) / (double)f.UnitsPerEm * size;
        }

        public double AscentOf(bool monoFace, double size)
        {
            TtfFont f = monoFace ? mono[0] : body[0];
            if (f == null) return size * 0.9;
            return f.Ascent / (double)f.UnitsPerEm * size;
        }

        LayFace FaceOf(TtfFont f)
        {
            LayFace face;
            if (faces.TryGetValue(f, out face)) return face;
            face = new LayFace();
            face.Font = f;
            face.Res = "F" + (All.Count + 1).ToString(CultureInfo.InvariantCulture);
            StringBuilder tag = new StringBuilder();
            uint seed = (uint)(All.Count * 7919 + f.Name.Length * 104729 + 12345);
            for (int i = 0; i < 6; i++) { seed = seed * 1103515245 + 12345; tag.Append((char)('A' + (int)((seed >> 16) % 26))); }
            face.Tag = tag.ToString();
            faces[f] = face;
            All.Add(face);
            return face;
        }

        public LayGlyph Map(int cp, string chars, LayStyle st)
        {
            int role = st.Mono ? 4 + (st.Bold ? 1 : 0) : (st.Bold ? 1 : 0) + (st.Italic ? 2 : 0);
            TtfFont primary = st.Mono ? (mono[st.Bold ? 1 : 0] ?? mono[0]) : (body[role] ?? (st.Bold && body[1] != null ? body[1] : st.Italic && body[2] != null ? body[2] : body[0]));
            bool fakeBold = false, fakeItalic = false;
            if (!st.Mono)
            {
                if (st.Bold && primary != null && primary.Weight < 600) fakeBold = true;
                if (st.Italic && primary != null && !primary.Italic) fakeItalic = true;
            }
            else
            {
                if (st.Bold && mono[1] == null) fakeBold = true;
                if (st.Italic) fakeItalic = true;
            }
            TtfFont font = primary;
            int gid = primary != null ? primary.Glyph(cp) : 0;
            if (gid == 0)
            {
                long key = ((long)role << 32) | (uint)cp;
                KeyValuePair<TtfFont, int> hit;
                if (!memo.TryGetValue(key, out hit))
                {
                    hit = new KeyValuePair<TtfFont, int>(null, 0);
                    if (st.Mono && body[0] != null && body[0].Glyph(cp) != 0) hit = new KeyValuePair<TtfFont, int>(body[0], body[0].Glyph(cp));
                    else
                        foreach (string spec in fallback)
                        {
                            TtfFont f = cache.Get(spec);
                            if (f == null) continue;
                            int g = f.Glyph(cp);
                            if (g != 0) { hit = new KeyValuePair<TtfFont, int>(f, g); break; }
                        }
                    memo[key] = hit;
                }
                if (hit.Key != null)
                {
                    font = hit.Key;
                    gid = hit.Value;
                    fakeBold = st.Bold && font.Weight < 600;
                    fakeItalic = st.Italic && !font.Italic;
                }
                else if (cp > 32 && Missing.Count < 12)
                {
                    string m = char.ConvertFromUtf32(cp);
                    if (!Missing.Contains(m)) Missing.Add(m);
                }
            }
            if (font == null) return null;
            LayFace face = FaceOf(font);
            LayGlyph pg = new LayGlyph();
            pg.Face = face;
            pg.Gid = gid;
            pg.Adv = font.Advance(gid) / (double)font.UnitsPerEm * st.Size;
            pg.Chars = chars;
            pg.St = st;
            pg.FakeBold = fakeBold;
            pg.FakeItalic = fakeItalic;
            face.Used.Add(gid);
            if (gid != 0 && !face.Text.ContainsKey(gid)) face.Text[gid] = chars;
            if (gid != 0 && cp < 0xFFFF && !face.Uni.ContainsKey(cp)) face.Uni[cp] = gid;
            return pg;
        }

        public static int Category(int cp)
        {
            if (cp == ' ' || cp == '\t' || cp == 0x3000) return WS;
            if (cp >= '0' && cp <= '9') return EN;
            if ((cp >= 0x0660 && cp <= 0x0669) || (cp >= 0x06F0 && cp <= 0x06F9)) return AN;
            if ((cp >= 0x0590 && cp <= 0x08FF) || (cp >= 0xFB1D && cp <= 0xFDFF) || (cp >= 0xFE70 && cp <= 0xFEFE) || (cp >= 0x10800 && cp <= 0x10FFF) || (cp >= 0x1E800 && cp <= 0x1EFFF))
            {
                if ((cp >= 0x0591 && cp <= 0x05BD) || (cp >= 0x064B && cp <= 0x065F) || cp == 0x0670 || (cp >= 0x06D6 && cp <= 0x06ED)) return NSM;
                return R;
            }
            UnicodeCategory k = cp < 0x10000 ? CharUnicodeInfo.GetUnicodeCategory((char)cp) : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);
            switch (k)
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.SpacingCombiningMark:
                    return L;
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.EnclosingMark:
                    return NSM;
            }
            return ON;
        }

        public static bool Cjk(int cp)
        {
            return (cp >= 0x2E80 && cp <= 0x9FFF) || (cp >= 0xAC00 && cp <= 0xD7A3) || (cp >= 0xF900 && cp <= 0xFAFF) || (cp >= 0xFF00 && cp <= 0xFFEF) || (cp >= 0x20000 && cp <= 0x3FFFF);
        }

        static bool Closing(int cp)
        {
            return "\u3001\u3002\uFF0C\uFF0E\uFF09\u300D\u300F\u3011\u3009\u300B\uFF01\uFF1F\uFF1A\uFF1B\u30FC\u3005\u309D\u309E\u30FD\u30FE".IndexOf((char)cp) >= 0 || cp == ')' || cp == ']' || cp == '}' || cp == ',' || cp == '.' || cp == '!' || cp == '?' || cp == ';' || cp == ':';
        }

        public List<LayGlyph> Shape(List<KeyValuePair<string, LayStyle>> runs)
        {
            List<int> cps = new List<int>();
            List<LayStyle> styles = new List<LayStyle>();
            foreach (KeyValuePair<string, LayStyle> r in runs)
            {
                string t = r.Key ?? "";
                for (int i = 0; i < t.Length; i++)
                {
                    int cp = t[i];
                    if (char.IsHighSurrogate(t[i]) && i + 1 < t.Length && char.IsLowSurrogate(t[i + 1])) { cp = char.ConvertToUtf32(t[i], t[i + 1]); i++; }
                    else if (char.IsSurrogate(t[i])) continue;
                    cps.Add(cp);
                    styles.Add(r.Value);
                }
            }
            List<ShapedChar> shaped = ArabicShaper.Shape(cps, this);
            List<LayGlyph> outList = new List<LayGlyph>();
            foreach (ShapedChar sc in shaped)
            {
                int cp = sc.Cp;
                LayStyle st = styles[Math.Max(0, Math.Min(sc.Src, styles.Count - 1))];
                if (cp == '\n') { outList.Add(new LayGlyph { Chars = "\n", St = st, Cat = WS }); continue; }
                if (cp == 0x200B) { if (outList.Count > 0) outList[outList.Count - 1].BreakAfter = true; continue; }
                if (cp == 0x200C || cp == 0x200D || cp == 0xFEFF || cp == 0x00AD || (cp >= 0xFE00 && cp <= 0xFE0F) || (cp >= 0xE0100 && cp <= 0xE01EF) || cp == 0x2060 || (cp < 32 && cp != '\t')) continue;
                if (cp == '\t')
                {
                    for (int k = 0; k < 4; k++)
                    {
                        LayGlyph sp = Map(' ', " ", st);
                        if (sp == null) continue;
                        sp.Space = true;
                        sp.Cat = WS;
                        outList.Add(sp);
                    }
                    continue;
                }
                bool nbsp = cp == 0x00A0 || cp == 0x202F || cp == 0x2007;
                LayGlyph g = Map(nbsp ? ' ' : cp, sc.Chars, st);
                if (g == null) continue;
                g.Cat = Category(sc.Base);
                if (cp == ' ' || cp == 0x3000) g.Space = true;
                if (cp == '-' || cp == 0x2010 || cp == 0x2013 || cp == 0x2014 || cp == '/' || cp == 0x2026) g.BreakAfter = true;
                if (Cjk(cp)) { g.BreakAfter = true; if (outList.Count > 0 && !Closing(cp)) outList[outList.Count - 1].BreakAfter = true; }
                if (Closing(cp)) g.NoBreakBefore = true;
                outList.Add(g);
            }
            return outList;
        }

        public bool HasGlyph(int cp)
        {
            TtfFont b = body[0];
            if (b != null && b.Glyph(cp) != 0) return true;
            foreach (string spec in fallback)
            {
                TtfFont f = cache.Get(spec);
                if (f != null && f.Glyph(cp) != 0) return true;
            }
            return false;
        }

        public static List<LayLine> Break(List<LayGlyph> glyphs, double width, double firstIndent)
        {
            List<LayLine> lines = new List<LayLine>();
            int i = 0;
            while (i < glyphs.Count)
            {
                double avail = width - (lines.Count == 0 ? firstIndent : 0);
                double w = 0;
                int lastBreak = -1;
                int j = i;
                while (j < glyphs.Count)
                {
                    LayGlyph g = glyphs[j];
                    if (g.Chars == "\n") { break; }
                    if (!g.Space && w + g.Adv > avail && j > i)
                    {
                        if (lastBreak >= i) { j = lastBreak + 1; }
                        else
                        {
                            while (j > i + 1 && glyphs[j].NoBreakBefore) j--;
                        }
                        break;
                    }
                    w += g.Adv;
                    if (g.Space || (g.BreakAfter && (j + 1 >= glyphs.Count || !glyphs[j + 1].NoBreakBefore))) lastBreak = j;
                    j++;
                }
                LayLine line = new LayLine();
                for (int k = i; k < j && k < glyphs.Count; k++) line.Glyphs.Add(glyphs[k]);
                bool hard = j < glyphs.Count && glyphs[j].Chars == "\n";
                int next = hard ? j + 1 : j;
                while (!hard && next < glyphs.Count && glyphs[next].Space) next++;
                while (line.Glyphs.Count > 0 && line.Glyphs[line.Glyphs.Count - 1].Space) line.Glyphs.RemoveAt(line.Glyphs.Count - 1);
                foreach (LayGlyph g in line.Glyphs) line.Width += g.Adv;
                lines.Add(line);
                if (next <= i) next = i + 1;
                i = next;
            }
            if (lines.Count == 0) lines.Add(new LayLine());
            return lines;
        }

        public static void Order(LayLine line, bool rtlPara)
        {
            List<LayGlyph> gs = line.Glyphs;
            int n = gs.Count;
            if (n == 0) return;
            bool any = rtlPara;
            foreach (LayGlyph g in gs) if (g.Cat == R || g.Cat == AN) { any = true; break; }
            if (!any) { foreach (LayGlyph g in gs) g.Level = 0; return; }
            int baseDir = rtlPara ? 1 : 0;
            int[] dir = new int[n];
            bool[] number = new bool[n];
            int prevStrong = baseDir;
            for (int i = 0; i < n; i++)
            {
                int c = gs[i].Cat;
                if (c == L) { dir[i] = 0; prevStrong = 0; }
                else if (c == R) { dir[i] = 1; prevStrong = 1; }
                else if (c == AN) { dir[i] = 1; number[i] = true; }
                else if (c == EN) { dir[i] = prevStrong; number[i] = prevStrong == 1; }
                else if (c == NSM) dir[i] = i > 0 ? dir[i - 1] : baseDir;
                else dir[i] = -1;
            }
            for (int i = 0; i < n; i++)
            {
                if (dir[i] >= 0) continue;
                int a = i - 1;
                while (a >= 0 && dir[a] < 0) a--;
                int b = i + 1;
                while (b < n && dir[b] < 0) b++;
                int before = a >= 0 ? dir[a] : baseDir, after = b < n ? dir[b] : baseDir;
                int d = before == after ? before : baseDir;
                for (int k = i; k < b; k++) dir[k] = d;
            }
            int max = 0;
            for (int i = 0; i < n; i++)
            {
                int lv;
                if (baseDir == 0) lv = number[i] ? 2 : dir[i] == 1 ? 1 : 0;
                else lv = dir[i] == 1 && !number[i] ? 1 : 2;
                gs[i].Level = lv;
            }
            for (int i = n - 1; i >= 0 && gs[i].Cat == WS; i--) gs[i].Level = baseDir;
            foreach (LayGlyph g in gs) max = Math.Max(max, g.Level);
            for (int level = max; level >= 1; level--)
            {
                int i = 0;
                while (i < n)
                {
                    if (gs[i].Level < level) { i++; continue; }
                    int j = i;
                    while (j < n && gs[j].Level >= level) j++;
                    gs.Reverse(i, j - i);
                    i = j;
                }
            }
            foreach (LayGlyph g in gs)
            {
                if (g.Level % 2 == 0 || g.Face == null || g.Chars.Length != 1) continue;
                int m = Mirror(g.Chars[0]);
                if (m == g.Chars[0]) continue;
                int gid = g.Face.Font.Glyph(m);
                if (gid == 0) continue;
                g.Gid = gid;
                g.Face.Used.Add(gid);
                if (!g.Face.Text.ContainsKey(gid)) g.Face.Text[gid] = ((char)m).ToString();
            }
        }

        public static int Mirror(int cp)
        {
            switch (cp)
            {
                case '(': return ')';
                case ')': return '(';
                case '[': return ']';
                case ']': return '[';
                case '{': return '}';
                case '}': return '{';
                case '<': return '>';
                case '>': return '<';
                case 0x00AB: return 0x00BB;
                case 0x00BB: return 0x00AB;
            }
            return cp;
        }
    }

    class ShapedChar
    {
        public int Cp, Base, Src;
        public string Chars;
        public ShapedChar(int cp, int bas, int src, string chars) { Cp = cp; Base = bas; Src = src; Chars = chars; }
    }

    static class ArabicShaper
    {
        static readonly Dictionary<int, int[]> Forms = new Dictionary<int, int[]>();
        static readonly HashSet<int> Dual = new HashSet<int>();

        static void F(int cp, int iso, int fin, int ini, int med)
        {
            Forms[cp] = new int[] { iso, fin, ini, med };
            if (ini != 0) Dual.Add(cp);
        }

        static ArabicShaper()
        {
            F(0x0621, 0xFE80, 0, 0, 0);
            F(0x0622, 0xFE81, 0xFE82, 0, 0);
            F(0x0623, 0xFE83, 0xFE84, 0, 0);
            F(0x0624, 0xFE85, 0xFE86, 0, 0);
            F(0x0625, 0xFE87, 0xFE88, 0, 0);
            F(0x0626, 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C);
            F(0x0627, 0xFE8D, 0xFE8E, 0, 0);
            F(0x0628, 0xFE8F, 0xFE90, 0xFE91, 0xFE92);
            F(0x0629, 0xFE93, 0xFE94, 0, 0);
            F(0x062A, 0xFE95, 0xFE96, 0xFE97, 0xFE98);
            F(0x062B, 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C);
            F(0x062C, 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0);
            F(0x062D, 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4);
            F(0x062E, 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8);
            F(0x062F, 0xFEA9, 0xFEAA, 0, 0);
            F(0x0630, 0xFEAB, 0xFEAC, 0, 0);
            F(0x0631, 0xFEAD, 0xFEAE, 0, 0);
            F(0x0632, 0xFEAF, 0xFEB0, 0, 0);
            F(0x0633, 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4);
            F(0x0634, 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8);
            F(0x0635, 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC);
            F(0x0636, 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0);
            F(0x0637, 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4);
            F(0x0638, 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8);
            F(0x0639, 0xFEC9, 0xFECA, 0xFECB, 0xFECC);
            F(0x063A, 0xFECD, 0xFECE, 0xFECF, 0xFED0);
            F(0x0641, 0xFED1, 0xFED2, 0xFED3, 0xFED4);
            F(0x0642, 0xFED5, 0xFED6, 0xFED7, 0xFED8);
            F(0x0643, 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC);
            F(0x0644, 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0);
            F(0x0645, 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4);
            F(0x0646, 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8);
            F(0x0647, 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC);
            F(0x0648, 0xFEED, 0xFEEE, 0, 0);
            F(0x0649, 0xFEEF, 0xFEF0, 0xFBE8, 0xFBE9);
            F(0x064A, 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4);
            F(0x067E, 0xFB56, 0xFB57, 0xFB58, 0xFB59);
            F(0x0686, 0xFB7A, 0xFB7B, 0xFB7C, 0xFB7D);
            F(0x0698, 0xFB8A, 0xFB8B, 0, 0);
            F(0x06A9, 0xFB8E, 0xFB8F, 0xFB90, 0xFB91);
            F(0x06AF, 0xFB92, 0xFB93, 0xFB94, 0xFB95);
            F(0x06CC, 0xFBFC, 0xFBFD, 0xFBFE, 0xFBFF);
            F(0x0679, 0xFB66, 0xFB67, 0xFB68, 0xFB69);
            F(0x0688, 0xFB88, 0xFB89, 0, 0);
            F(0x0691, 0xFB8C, 0xFB8D, 0, 0);
            F(0x06BA, 0xFB9E, 0xFB9F, 0, 0);
            F(0x06BE, 0xFBAA, 0xFBAB, 0xFBAC, 0xFBAD);
            F(0x06C1, 0xFBA6, 0xFBA7, 0xFBA8, 0xFBA9);
            F(0x06D2, 0xFBAE, 0xFBAF, 0, 0);
        }

        static bool Transparent(int cp) { return (cp >= 0x064B && cp <= 0x065F) || cp == 0x0670 || (cp >= 0x06D6 && cp <= 0x06ED); }
        static bool JoinsLeft(int cp) { return Dual.Contains(cp) || cp == 0x0640; }
        static bool JoinsRight(int cp) { return (Forms.ContainsKey(cp) && cp != 0x0621) || cp == 0x0640; }

        public static List<ShapedChar> Shape(List<int> cps, LayFonts fonts)
        {
            List<ShapedChar> outList = new List<ShapedChar>(cps.Count);
            bool any = false;
            foreach (int c in cps) if (Forms.ContainsKey(c)) { any = true; break; }
            int n = cps.Count;
            if (!any)
            {
                for (int i = 0; i < n; i++) outList.Add(new ShapedChar(cps[i], cps[i], i, char.ConvertFromUtf32(cps[i])));
                return outList;
            }
            for (int i = 0; i < n; i++)
            {
                int c = cps[i];
                int[] forms;
                if (!Forms.TryGetValue(c, out forms)) { outList.Add(new ShapedChar(c, c, i, char.ConvertFromUtf32(c))); continue; }
                int p = i - 1;
                while (p >= 0 && Transparent(cps[p])) p--;
                int q = i + 1;
                while (q < n && Transparent(cps[q])) q++;
                bool prevJoins = p >= 0 && JoinsLeft(cps[p]);
                if (c == 0x0644 && q < n && (cps[q] == 0x0622 || cps[q] == 0x0623 || cps[q] == 0x0625 || cps[q] == 0x0627))
                {
                    int lig = cps[q] == 0x0622 ? 0xFEF5 : cps[q] == 0x0623 ? 0xFEF7 : cps[q] == 0x0625 ? 0xFEF9 : 0xFEFB;
                    if (prevJoins) lig++;
                    if (fonts.HasGlyph(lig))
                    {
                        outList.Add(new ShapedChar(lig, c, i, "\u0644" + char.ConvertFromUtf32(cps[q])));
                        for (int k = i + 1; k < q; k++) outList.Add(new ShapedChar(cps[k], cps[k], k, char.ConvertFromUtf32(cps[k])));
                        i = q;
                        continue;
                    }
                }
                bool nextJoins = q < n && JoinsLeft(c) && JoinsRight(cps[q]);
                bool joinsPrev = prevJoins && JoinsRight(c);
                int form = joinsPrev && nextJoins ? 3 : joinsPrev ? 1 : nextJoins ? 2 : 0;
                int shaped = forms[form];
                if (shaped == 0) shaped = forms[joinsPrev ? 1 : 0];
                if (shaped == 0) shaped = forms[0];
                if (shaped != 0 && fonts.HasGlyph(shaped)) outList.Add(new ShapedChar(shaped, c, i, char.ConvertFromUtf32(c)));
                else outList.Add(new ShapedChar(c, c, i, char.ConvertFromUtf32(c)));
            }
            return outList;
        }
    }
}
