using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MinGent
{
    enum MdKind { Para, Heading, Code, Quote, Item, Rule, Table, Math }

    class MdRun
    {
        public string Text = "";
        public int Style;
        public string Link;
        public const int Bold = 1, Italic = 2, Code = 4, Strike = 8, Math = 16, Display = 32;
        public MdRun(string t, int style, string link) { Text = t; Style = style; Link = link; }
    }

    class MdBlock
    {
        public MdKind Kind;
        public int Level;
        public string Marker;
        public int Task = -1;
        public List<MdRun> Runs = new List<MdRun>();
        public string Code = "", Lang = "";
        public bool Open;
        public List<List<string>> Rows;
        public List<char> Align;
    }

    static class Markdown
    {
        static readonly Regex Fence = new Regex(@"^(\s*)(`{3,}|~{3,})\s*([\w#+.-]*)", RegexOptions.CultureInvariant);
        static readonly Regex Heading = new Regex(@"^[ \t]{0,3}(#{1,6})[ \t]+(.*)$", RegexOptions.CultureInvariant);
        static readonly Regex Rule = new Regex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", RegexOptions.CultureInvariant);
        static readonly Regex ListItem = new Regex("^(\\s*)([-*+\u2022]|\\d{1,4}[.)])\\s+(.*)$", RegexOptions.CultureInvariant);
        static readonly Regex TableSep = new Regex(@"^(?>[ \t]*)\|?(?>[ \t]*):?-+:?(?>[ \t]*)(?:\|(?>[ \t]*):?-+:?(?>[ \t]*))*\|?(?>[ \t]*)$", RegexOptions.CultureInvariant);

        static string HeadingText(string t)
        {
            t = t.TrimEnd();
            int k = t.Length;
            while (k > 0 && t[k - 1] == '#') k--;
            if (k < t.Length && (k == 0 || t[k - 1] == ' ' || t[k - 1] == '\t')) t = t.Substring(0, k).TrimEnd();
            return t;
        }

        public static List<MdBlock> Parse(string text)
        {
            return Parse(text, 0);
        }

        static List<MdBlock> Parse(string text, int depth)
        {
            List<MdBlock> blocks = new List<MdBlock>();
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder para = new StringBuilder();
            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                Match f = Fence.Match(line);
                if (f.Success)
                {
                    FlushPara(blocks, para);
                    string fence = f.Groups[2].Value;
                    MdBlock b = new MdBlock();
                    b.Kind = MdKind.Code;
                    b.Lang = f.Groups[3].Value;
                    int indent = f.Groups[1].Value.Length;
                    StringBuilder code = new StringBuilder();
                    i++;
                    b.Open = true;
                    while (i < lines.Length)
                    {
                        string l = lines[i];
                        string t = l.TrimStart();
                        if (t.StartsWith(fence) && t.Trim().Trim(fence[0]).Length == 0) { i++; b.Open = false; break; }
                        if (code.Length > 0) code.Append('\n');
                        int lead = l.Length - l.TrimStart().Length;
                        code.Append(indent > 0 ? l.Substring(Math.Min(indent, lead)) : l);
                        i++;
                    }
                    b.Code = code.ToString();
                    if (string.Equals(b.Lang, "math", StringComparison.OrdinalIgnoreCase) && b.Code.Trim().Length > 0 && b.Code.Length <= MathFonts.MaxTex) b.Kind = MdKind.Math;
                    blocks.Add(b);
                    continue;
                }
                if (line.Trim().Length == 0) { FlushPara(blocks, para); i++; continue; }
                string tl = line.TrimStart();
                if (line.Length - tl.Length < 4 && (tl.StartsWith("$$") || tl.StartsWith("\\[") || tl.StartsWith("\\begin{")))
                {
                    string tex, after;
                    int next;
                    if (DisplayMath(lines, i, out tex, out after, out next))
                    {
                        FlushPara(blocks, para);
                        MdBlock mb = new MdBlock();
                        mb.Kind = MdKind.Math;
                        mb.Code = tex;
                        blocks.Add(mb);
                        if (after.Trim().Length > 0) { lines[next - 1] = after; i = next - 1; }
                        else i = next;
                        continue;
                    }
                }
                Match h = Heading.Match(line);
                if (h.Success)
                {
                    FlushPara(blocks, para);
                    MdBlock b = new MdBlock();
                    b.Kind = MdKind.Heading;
                    b.Level = h.Groups[1].Value.Length;
                    b.Runs = Inline(HeadingText(h.Groups[2].Value));
                    blocks.Add(b);
                    i++;
                    continue;
                }
                if (Rule.IsMatch(line) && para.Length == 0)
                {
                    MdBlock b = new MdBlock();
                    b.Kind = MdKind.Rule;
                    blocks.Add(b);
                    i++;
                    continue;
                }
                if (line.TrimStart().StartsWith("|") && i + 1 < lines.Length && lines[i + 1].IndexOf('-') >= 0 && lines[i + 1].Length < 4000 && TableSep.IsMatch(lines[i + 1]))
                {
                    FlushPara(blocks, para);
                    MdBlock b = new MdBlock();
                    b.Kind = MdKind.Table;
                    b.Rows = new List<List<string>>();
                    b.Rows.Add(Cells(line));
                    b.Align = new List<char>();
                    foreach (string c in Cells(lines[i + 1]))
                    {
                        string t = c.Trim();
                        b.Align.Add(t.StartsWith(":") && t.EndsWith(":") ? 'c' : t.EndsWith(":") ? 'r' : 'l');
                    }
                    i += 2;
                    while (i < lines.Length && lines[i].Trim().Length > 0 && lines[i].IndexOf('|') >= 0)
                    {
                        b.Rows.Add(Cells(lines[i]));
                        i++;
                    }
                    blocks.Add(b);
                    continue;
                }
                if (line.TrimStart().StartsWith(">"))
                {
                    FlushPara(blocks, para);
                    StringBuilder q = new StringBuilder();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith(">"))
                    {
                        string t = lines[i].TrimStart().Substring(1);
                        if (t.StartsWith(" ")) t = t.Substring(1);
                        if (q.Length > 0) q.Append('\n');
                        q.Append(t);
                        i++;
                    }
                    if (depth >= 6)
                    {
                        MdBlock flat = new MdBlock();
                        flat.Kind = MdKind.Quote;
                        flat.Runs = Inline(q.ToString().Replace('\n', ' '));
                        blocks.Add(flat);
                        continue;
                    }
                    foreach (MdBlock inner in Parse(q.ToString(), depth + 1))
                    {
                        if (inner.Kind == MdKind.Para || inner.Kind == MdKind.Heading || inner.Kind == MdKind.Item)
                        {
                            inner.Level = inner.Kind == MdKind.Item ? inner.Level : 0;
                            if (inner.Kind != MdKind.Item) inner.Kind = MdKind.Quote;
                        }
                        blocks.Add(inner);
                    }
                    continue;
                }
                Match li = ListItem.Match(line);
                if (li.Success)
                {
                    FlushPara(blocks, para);
                    MdBlock b = new MdBlock();
                    b.Kind = MdKind.Item;
                    int spaces = li.Groups[1].Value.Replace("\t", "    ").Length;
                    b.Level = Math.Min(4, spaces / 2);
                    string marker = li.Groups[2].Value;
                    string body = li.Groups[3].Value;
                    if (body.StartsWith("[ ] ") || body == "[ ]") { b.Task = 0; body = body.Length > 3 ? body.Substring(4) : ""; }
                    else if (body.StartsWith("[x] ") || body.StartsWith("[X] ") || body == "[x]" || body == "[X]") { b.Task = 1; body = body.Length > 3 ? body.Substring(4) : ""; }
                    b.Marker = char.IsDigit(marker[0]) ? marker.TrimEnd(')').TrimEnd('.') + "." : "\u2022";
                    StringBuilder sb = new StringBuilder(body);
                    i++;
                    while (i < lines.Length)
                    {
                        string next = lines[i];
                        if (next.Trim().Length == 0 || ListItem.IsMatch(next) || Fence.IsMatch(next) || Heading.IsMatch(next) || next.TrimStart().StartsWith(">") || next.TrimStart().StartsWith("|")) break;
                        int lead = next.Length - next.TrimStart().Length;
                        if (lead < 2) break;
                        sb.Append('\n').Append(next.Trim());
                        i++;
                    }
                    b.Runs = Inline(sb.ToString());
                    blocks.Add(b);
                    continue;
                }
                if (para.Length > 0) para.Append('\n');
                para.Append(line.TrimEnd());
                i++;
            }
            FlushPara(blocks, para);
            return blocks;
        }

        static List<string> Cells(string line)
        {
            string t = line.Trim();
            if (t.StartsWith("|")) t = t.Substring(1);
            if (t.EndsWith("|") && !t.EndsWith("\\|")) t = t.Substring(0, t.Length - 1);
            List<string> cells = new List<string>();
            StringBuilder cur = new StringBuilder();
            bool code = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '`') code = !code;
                if ((c == '$' || c == '\\') && !code)
                {
                    int ms, me;
                    bool md;
                    int after = MathEnd(t, i, out ms, out me, out md);
                    if (after > 0) { cur.Append(t, i, after - i); i = after - 1; continue; }
                }
                if (c == '\\' && i + 1 < t.Length && t[i + 1] == '|') { cur.Append('|'); i++; continue; }
                if (c == '|' && !code) { cells.Add(cur.ToString().Trim()); cur.Length = 0; continue; }
                cur.Append(c);
            }
            cells.Add(cur.ToString().Trim());
            return cells;
        }

        static readonly Regex MathEnv = new Regex(@"^\\begin\{(equation|align|alignat|alignedat|gather|multline|eqnarray|displaymath|flalign|aligned|gathered|split|cases|dcases|matrix|pmatrix|bmatrix|Bmatrix|vmatrix|Vmatrix|array)\*?\}", RegexOptions.CultureInvariant);

        static bool DisplayMath(string[] lines, int i, out string tex, out string after, out int next)
        {
            tex = "";
            after = "";
            next = i + 1;
            string first = lines[i].TrimStart();
            string close;
            int skip = 2, from = 0;
            bool keep = false;
            if (first.StartsWith("$$"))
            {
                if (first.StartsWith("$$$")) return false;
                close = "$$";
            }
            else if (first.StartsWith("\\[")) close = "\\]";
            else
            {
                Match m = MathEnv.Match(first);
                if (!m.Success) return false;
                int brace = first.IndexOf('}');
                close = "\\end{" + first.Substring(7, brace - 7) + "}";
                skip = 0;
                from = brace + 1;
                keep = true;
            }
            StringBuilder sb = new StringBuilder();
            string rest = first.Substring(skip);
            if (!keep) from = 0;
            int j = i;
            while (true)
            {
                int k = CloseAt(rest, close, from);
                if (k >= 0)
                {
                    sb.Append(rest, 0, keep ? k + close.Length : k);
                    after = rest.Substring(k + close.Length);
                    next = j + 1;
                    tex = sb.ToString();
                    return tex.Trim().Length > 0 && tex.Length <= MathFonts.MaxTex;
                }
                sb.Append(rest).Append('\n');
                if (sb.Length > MathFonts.MaxTex) return false;
                j++;
                if (j >= lines.Length)
                {
                    tex = sb.ToString();
                    next = j;
                    return tex.IndexOfAny(TexMarks) >= 0;
                }
                rest = lines[j];
                from = 0;
                if (rest.Trim().Length == 0) return false;
            }
        }

        static readonly char[] TexMarks = { '\\', '^', '_', '{', '=' };
        static readonly char[] TexStrong = { '\\', '^' };

        static int CloseAt(string s, string close, int from)
        {
            return CloseAt(s, close, from, int.MaxValue);
        }

        static int CloseAt(string s, string close, int from, int window)
        {
            int k = Math.Max(0, from);
            int limit = window == int.MaxValue ? s.Length : (int)Math.Min(s.Length, (long)k + window);
            while (k <= limit - close.Length)
            {
                k = s.IndexOf(close, k, limit - k, StringComparison.Ordinal);
                if (k < 0) return -1;
                int bs = 0;
                for (int q = k - 1; q >= 0 && s[q] == '\\'; q--) bs++;
                if (bs % 2 == 0) return k;
                k++;
            }
            return -1;
        }

        static readonly Regex InlineEnv = new Regex(@"\G\\begin\{(equation|align|alignat|alignedat|gather|multline|eqnarray|displaymath|flalign|aligned|gathered|split|cases|dcases|matrix|pmatrix|bmatrix|Bmatrix|vmatrix|Vmatrix|array)(\*?)\}", RegexOptions.CultureInvariant);

        public static int MathEnd(string s, int i, out int start, out int end, out bool display)
        {
            start = end = 0;
            display = false;
            if (i < 0 || i >= s.Length) return -1;
            char c = s[i];
            if (c == '\\' && i + 7 < s.Length && s[i + 1] == 'b' && string.CompareOrdinal(s, i, "\\begin{", 0, 7) == 0)
            {
                Match em = InlineEnv.Match(s, i);
                if (!em.Success) return -1;
                string close = "\\end{" + em.Groups[1].Value + em.Groups[2].Value + "}";
                int k = CloseAt(s, close, i + em.Length, 4000);
                if (k < 0) return -1;
                start = i;
                end = k + close.Length;
                display = true;
                return end;
            }
            if (c == '\\')
            {
                if (i + 1 >= s.Length || (s[i + 1] != '(' && s[i + 1] != '[')) return -1;
                bool br = s[i + 1] == '[';
                int k = CloseAt(s, br ? "\\]" : "\\)", i + 2, 4000);
                if (k < 0 || s.Substring(i + 2, k - i - 2).Trim().Length == 0) return -1;
                start = i + 2;
                end = k;
                display = br;
                return k + 2;
            }
            if (c != '$') return -1;
            if (i + 1 < s.Length && s[i + 1] == '$')
            {
                int k = CloseAt(s, "$$", i + 2, 4000);
                if (k < 0 || s.Substring(i + 2, k - i - 2).Trim().Length == 0) return -1;
                start = i + 2;
                end = k;
                display = true;
                return k + 2;
            }
            if (i + 1 >= s.Length) return -1;
            bool spaced = char.IsWhiteSpace(s[i + 1]);
            for (int k = i + 1; k < s.Length && k - i <= 1000; k++)
            {
                char ch = s[k];
                if (ch == '\\') { k++; continue; }
                if (ch == '\n' && k + 1 < s.Length && s[k + 1] == '\n') return -1;
                if (ch == '`') return -1;
                if (ch == ']' && k + 1 < s.Length && s[k + 1] == '(') return -1;
                if (ch != '$') continue;
                if (k + 1 < s.Length && char.IsDigit(s[k + 1])) return -1;
                if (s[k - 1] == '/' || s[k - 1] == ':') return -1;
                string inner = s.Substring(i + 1, k - i - 1);
                if (inner.Trim().Length == 0) return -1;
                if ((spaced || char.IsWhiteSpace(s[k - 1])) && inner.IndexOfAny(TexStrong) < 0) return -1;
                start = i + 1;
                end = k;
                return k + 1;
            }
            return -1;
        }

        static void FlushPara(List<MdBlock> blocks, StringBuilder para)
        {
            if (para.Length == 0) return;
            MdBlock b = new MdBlock();
            b.Kind = MdKind.Para;
            b.Runs = Inline(para.ToString());
            blocks.Add(b);
            para.Length = 0;
        }

        static int UrlLength(string s, int i)
        {
            int start;
            if (string.Compare(s, i, "https://", 0, 8, StringComparison.OrdinalIgnoreCase) == 0) start = i + 8;
            else if (string.Compare(s, i, "http://", 0, 7, StringComparison.OrdinalIgnoreCase) == 0) start = i + 7;
            else return 0;
            int j = start, depth = 0;
            while (j < s.Length)
            {
                char c = s[j];
                if (char.IsWhiteSpace(c) || c == '<' || c == '>' || c == '"' || c == '\'' || c == '`' || c == '[' || c == ']') break;
                if (c == '(') depth++;
                else if (c == ')') { if (depth == 0) break; depth--; }
                j++;
            }
            while (j > start && ".,;:!?*_".IndexOf(s[j - 1]) >= 0) j--;
            return j - start >= 3 ? j - i : 0;
        }

        public static List<MdRun> Inline(string s)
        {
            return Inline(s, 0);
        }

        static List<MdRun> Inline(string s, int depth)
        {
            List<MdRun> runs = new List<MdRun>();
            int lastBracket = s.LastIndexOf(']'), lastAngle = s.LastIndexOf('>');
            StringBuilder cur = new StringBuilder();
            int style = 0;
            int i = 0;
            Action<int, string> flush = null;
            flush = delegate (int st, string link)
            {
                if (cur.Length == 0) return;
                runs.Add(new MdRun(cur.ToString(), st, link));
                cur.Length = 0;
            };
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '$' || (c == '\\' && i + 1 < s.Length && (s[i + 1] == '(' || s[i + 1] == '[' || s[i + 1] == 'b')))
                {
                    int ms, me;
                    bool md;
                    int after = MathEnd(s, i, out ms, out me, out md);
                    if (after > 0)
                    {
                        flush(style, null);
                        runs.Add(new MdRun(s.Substring(ms, me - ms), (style & ~MdRun.Code) | MdRun.Math | (md ? MdRun.Display : 0), null));
                        i = after;
                        continue;
                    }
                }
                if (c == '\\' && i + 1 < s.Length && "\\`*_{}[]()#+-.!|~<>$".IndexOf(s[i + 1]) >= 0) { cur.Append(s[i + 1]); i += 2; continue; }
                if (c == '`')
                {
                    int ticks = 0;
                    while (i + ticks < s.Length && s[i + ticks] == '`') ticks++;
                    string fence = new string('`', ticks);
                    int close = s.IndexOf(fence, i + ticks, StringComparison.Ordinal);
                    if (close > i)
                    {
                        flush(style, null);
                        string code = s.Substring(i + ticks, close - i - ticks);
                        if (code.Length >= 2 && code.StartsWith(" ") && code.EndsWith(" ") && code.Trim().Length > 0) code = code.Substring(1, code.Length - 2);
                        runs.Add(new MdRun(code.Replace('\n', ' '), style | MdRun.Code, null));
                        i = close + ticks;
                        continue;
                    }
                }
                if (c == '[' && i < lastBracket)
                {
                    int close = FindClose(s, i);
                    if (close > i && close + 1 < s.Length && s[close + 1] == '(')
                    {
                        int paren = -1, nest = 1;
                        for (int j = close + 2; j < s.Length; j++)
                        {
                            if (s[j] == '\n') break;
                            if (s[j] == '(') nest++;
                            else if (s[j] == ')' && --nest == 0) { paren = j; break; }
                        }
                        if (paren > close)
                        {
                            string label = s.Substring(i + 1, close - i - 1);
                            string url = s.Substring(close + 2, paren - close - 2).Trim();
                            int sp = url.IndexOf(' ');
                            if (sp > 0) url = url.Substring(0, sp);
                            flush(style, null);
                            List<MdRun> inner = depth < 4 ? Inline(label, depth + 1) : new List<MdRun>(new MdRun[] { new MdRun(label, 0, null) });
                            foreach (MdRun r in inner) { r.Link = url; r.Style |= style; runs.Add(r); }
                            i = paren + 1;
                            continue;
                        }
                    }
                }
                if (c == '<' && i < lastAngle)
                {
                    int close = s.IndexOf('>', i + 1, Math.Min(s.Length - i - 1, 2100));
                    if (close > i && (string.CompareOrdinal(s, i + 1, "http://", 0, 7) == 0 || string.CompareOrdinal(s, i + 1, "https://", 0, 8) == 0))
                    {
                        string url = s.Substring(i + 1, close - i - 1);
                        if (url.IndexOf(' ') < 0)
                        {
                            flush(style, null);
                            runs.Add(new MdRun(url, style, url));
                            i = close + 1;
                            continue;
                        }
                    }
                }
                if ((c == 'h' || c == 'H') && (i == 0 || !char.IsLetterOrDigit(s[i - 1])))
                {
                    int n = UrlLength(s, i);
                    if (n > 0)
                    {
                        string url = s.Substring(i, n);
                        flush(style, null);
                        runs.Add(new MdRun(url, style, url));
                        i += n;
                        continue;
                    }
                }
                if ((c == '*' || c == '_') && i + 1 < s.Length && s[i + 1] == c)
                {
                    bool opening = (style & MdRun.Bold) == 0;
                    if (opening ? (i + 2 < s.Length && !char.IsWhiteSpace(s[i + 2]) && s.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal) > i + 2) : (i > 0 && !char.IsWhiteSpace(s[i - 1])))
                    {
                        if (c == '_' && opening && i > 0 && char.IsLetterOrDigit(s[i - 1])) { cur.Append(c); i++; continue; }
                        flush(style, null);
                        style ^= MdRun.Bold;
                        i += 2;
                        continue;
                    }
                }
                if (c == '~' && i + 1 < s.Length && s[i + 1] == '~')
                {
                    bool opening = (style & MdRun.Strike) == 0;
                    if (!opening || s.IndexOf("~~", i + 2, StringComparison.Ordinal) > i + 2)
                    {
                        flush(style, null);
                        style ^= MdRun.Strike;
                        i += 2;
                        continue;
                    }
                }
                if (c == '*' || c == '_')
                {
                    bool opening = (style & MdRun.Italic) == 0;
                    bool ok;
                    if (opening)
                        ok = i + 1 < s.Length && !char.IsWhiteSpace(s[i + 1]) && s.IndexOf(c, i + 1) > i + 1 && (c == '*' || i == 0 || !char.IsLetterOrDigit(s[i - 1]));
                    else
                        ok = i > 0 && !char.IsWhiteSpace(s[i - 1]) && (c == '*' || i + 1 >= s.Length || !char.IsLetterOrDigit(s[i + 1]));
                    if (ok)
                    {
                        flush(style, null);
                        style ^= MdRun.Italic;
                        i++;
                        continue;
                    }
                }
                cur.Append(c);
                i++;
            }
            flush(style, null);
            return runs;
        }

        static int FindClose(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == '\\') { i++; continue; }
                if (s[i] == '[') depth++;
                else if (s[i] == ']') { depth--; if (depth == 0) return i; }
                else if ((s[i] == '\n' && i - open > 300) || i - open > 600) return -1;
            }
            return -1;
        }

        public static string PlainText(List<MdRun> runs)
        {
            StringBuilder sb = new StringBuilder();
            foreach (MdRun r in runs) sb.Append(r.Text);
            return sb.ToString();
        }
    }

    class ChatFonts : IDisposable
    {
        public Font Body, Bold, Italic, BoldItalic, Mono, Small, SmallBold, H1, H2, H3, MonoSmall;
        public float PointSize;
        readonly Dictionary<Font, Dictionary<string, int>> widths = new Dictionary<Font, Dictionary<string, int>>();
        int widthCount;
        public int LineH, MonoLineH, SmallLineH;
        MathFonts maths;

        public MathFonts Maths
        {
            get
            {
                if (maths == null) maths = new MathFonts(this);
                return maths;
            }
        }

        public ChatFonts(float size, string monoName)
        {
            PointSize = size;
            string family = "Segoe UI";
            try { using (Font f = new Font(family, size)) if (f.Name != family) family = UI.Font.FontFamily.Name; } catch { family = UI.Font.FontFamily.Name; }
            Body = new Font(family, size);
            Bold = new Font(family, size, FontStyle.Bold);
            Italic = new Font(family, size, FontStyle.Italic);
            BoldItalic = new Font(family, size, FontStyle.Bold | FontStyle.Italic);
            Small = new Font(family, size * 0.9f);
            SmallBold = new Font(family, size * 0.9f, FontStyle.Bold);
            H1 = new Font(family, size * 1.32f, FontStyle.Bold);
            H2 = new Font(family, size * 1.16f, FontStyle.Bold);
            H3 = new Font(family, size * 1.04f, FontStyle.Bold);
            string mono = string.IsNullOrEmpty(monoName) ? "Consolas" : monoName;
            try { Mono = new Font(mono, size * 0.92f); if (Mono.Name != mono) { Mono.Dispose(); Mono = new Font(FontFamily.GenericMonospace, size * 0.92f); } }
            catch { Mono = new Font(FontFamily.GenericMonospace, size * 0.92f); }
            MonoSmall = new Font(Mono.FontFamily, size * 0.85f);
            LineH = (int)Math.Ceiling(Body.GetHeight() * 1.18f);
            MonoLineH = (int)Math.Ceiling(Mono.GetHeight() * 1.12f);
            SmallLineH = (int)Math.Ceiling(Small.GetHeight() * 1.1f);
        }

        public Font For(int style)
        {
            if ((style & MdRun.Code) != 0) return Mono;
            bool b = (style & MdRun.Bold) != 0, i = (style & MdRun.Italic) != 0;
            return b && i ? BoldItalic : b ? Bold : i ? Italic : Body;
        }

        readonly Dictionary<Font, int> spaces = new Dictionary<Font, int>();

        int SpaceWidth(Graphics g, Font f)
        {
            int w;
            if (spaces.TryGetValue(f, out w)) return w;
            const TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int ten = TextRenderer.MeasureText(g, "x          x", f, Size.Empty, fl).Width;
            int xx = TextRenderer.MeasureText(g, "xx", f, Size.Empty, fl).Width;
            w = Math.Max(1, (int)Math.Round((ten - xx) / 10.0));
            spaces[f] = w;
            return w;
        }

        public int Width(Graphics g, string s, Font f) { return Width(g, s, f, true); }

        public int Width(Graphics g, string s, Font f, bool cache)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            Dictionary<string, int> map = null;
            int w;
            if (cache)
            {
                if (!widths.TryGetValue(f, out map)) { map = new Dictionary<string, int>(StringComparer.Ordinal); widths[f] = map; }
                if (map.TryGetValue(s, out w)) return w;
                if (widthCount > 30000) { foreach (Dictionary<string, int> m in widths.Values) m.Clear(); widthCount = 0; }
            }
            int lead = 0, trail = 0;
            while (lead < s.Length && s[lead] == ' ') lead++;
            while (trail < s.Length - lead && s[s.Length - 1 - trail] == ' ') trail++;
            string core = s.Substring(lead, s.Length - lead - trail);
            w = core.Length > 0 ? TextRenderer.MeasureText(g, core, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width : 0;
            if (lead + trail > 0) w += (lead + trail) * SpaceWidth(g, f);
            if (map != null) { map[s] = w; widthCount++; }
            return w;
        }

        public void Dispose()
        {
            if (maths != null) maths.Dispose();
            foreach (Font f in new Font[] { Body, Bold, Italic, BoldItalic, Mono, Small, SmallBold, H1, H2, H3, MonoSmall }) if (f != null) f.Dispose();
        }
    }

    class DrawOp
    {
        public int Kind;
        public Rectangle R;
        public string Text;
        public Font Font;
        public int Role;
        public string Link;
        public int Tag;
        public byte Brk, Blank;
        public bool NoSelect;
        public MBox Box;
        public int Base;
        public const int KText = 0, KCodeSpan = 1, KCodeBlock = 2, KLine = 3, KCheck = 4, KCopy = 5, KQuoteBar = 6, KTableBorder = 7, KStrike = 8, KHeaderBand = 9, KMath = 10, KBar = 11, KSeg = 12, KDot = 13;
        public const int RFore = 0, RDim = 1, RLink = 2, RCodeFore = 3, RAccent = 4, RError = 5, RSyntax = 100;
    }

    class MdLayout
    {
        public List<DrawOp> Ops = new List<DrawOp>();
        public int Height;
        public int Hidden;
        public List<string> CodeBlocks = new List<string>();
    }

    static class MdRenderer
    {
        public static MdLayout Layout(Graphics g, List<MdBlock> blocks, int width, ChatFonts f, Theme th, bool dim)
        {
            MdLayout L = new MdLayout();
            int y = 0;
            int gap = (int)(f.LineH * 0.55);
            MdKind prev = MdKind.Para;
            bool first = true;
            foreach (MdBlock b in blocks)
            {
                if (!first)
                {
                    if (b.Kind == MdKind.Item && prev == MdKind.Item) y += UI.S(3);
                    else if (b.Kind == MdKind.Heading) y += gap + UI.S(4);
                    else y += gap;
                }
                first = false;
                prev = b.Kind;
                switch (b.Kind)
                {
                    case MdKind.Heading:
                        {
                            Font hf = b.Level == 1 ? f.H1 : b.Level == 2 ? f.H2 : b.Level == 3 ? f.H3 : f.Bold;
                            y = Flow(g, b.Runs, 0, width, y, f, L.Ops, hf, dim ? DrawOp.RDim : DrawOp.RFore, 3);
                            break;
                        }
                    case MdKind.Para:
                        y = Flow(g, b.Runs, 0, width, y, f, L.Ops, null, dim ? DrawOp.RDim : DrawOp.RFore, 3);
                        break;
                    case MdKind.Quote:
                        {
                            int top = y, ind = UI.S(14);
                            y = Flow(g, b.Runs, ind, width - ind, y, f, L.Ops, null, DrawOp.RDim, 3);
                            DrawOp bar = new DrawOp();
                            bar.Kind = DrawOp.KQuoteBar;
                            bar.R = new Rectangle(UI.S(2), top, Math.Max(2, UI.S(3)), y - top);
                            L.Ops.Add(bar);
                            break;
                        }
                    case MdKind.Item:
                        {
                            int ind = b.Level * UI.S(18);
                            int markerW;
                            if (b.Task >= 0)
                            {
                                markerW = UI.S(23);
                                DrawOp ck = new DrawOp();
                                ck.Kind = DrawOp.KCheck;
                                int box = Math.Min(UI.S(13), f.LineH - UI.S(4));
                                ck.R = new Rectangle(ind + UI.S(2), y + (f.LineH - box) / 2, box, box);
                                ck.Tag = b.Task;
                                L.Ops.Add(ck);
                            }
                            else
                            {
                                string m = b.Marker ?? "\u2022";
                                markerW = Math.Max(UI.S(16), f.Width(g, m, f.Body) + UI.S(7));
                                DrawOp mo = new DrawOp();
                                mo.Kind = DrawOp.KText;
                                mo.Text = m;
                                mo.Font = f.Body;
                                mo.Role = DrawOp.RDim;
                                mo.R = new Rectangle(ind + (m == "\u2022" ? UI.S(3) : 0), y, f.Width(g, m, f.Body), f.LineH);
                                mo.Brk = 2;
                                L.Ops.Add(mo);
                            }
                            int marker = L.Ops.Count - 1, lineTop = y;
                            y = Flow(g, b.Runs, ind + markerW, width - ind - markerW, y, f, L.Ops, null, dim ? DrawOp.RDim : DrawOp.RFore, (byte)(b.Task >= 0 ? 2 : 0));
                            if (L.Ops.Count > marker + 1 && marker >= 0)
                            {
                                int shift = L.Ops[marker + 1].R.Y - lineTop;
                                if (shift > 0) L.Ops[marker].R.Y += shift;
                            }
                            break;
                        }
                    case MdKind.Rule:
                        {
                            DrawOp ln = new DrawOp();
                            ln.Kind = DrawOp.KLine;
                            ln.R = new Rectangle(0, y + UI.S(6), width, 1);
                            L.Ops.Add(ln);
                            y += UI.S(13);
                            break;
                        }
                    case MdKind.Code:
                        {
                            bool charted = string.Equals(b.Lang, Charts.Lang, StringComparison.OrdinalIgnoreCase) && width >= UI.S(220);
                            ChartSpec chart = charted ? Charts.Parse(b.Code) : null;
                            if (chart != null) y = Charts.Layout(g, chart, b.Code, width, y, f, L);
                            else if (charted && b.Open && (b.Code.Trim().Length == 0 || b.Code.TrimStart()[0] == '{')) y = Charts.Pending(g, b.Code, width, y, f, L);
                            else y = CodeBlock(g, b, width, y, f, th, L);
                            break;
                        }
                    case MdKind.Table:
                        y = Table(g, b, width, y, f, L);
                        break;
                    case MdKind.Math:
                        y = MathBlock(g, b, width, y, f, th, L, dim ? DrawOp.RDim : DrawOp.RFore);
                        break;
                }
            }
            L.Height = y;
            return L;
        }

        static int Flow(Graphics g, List<MdRun> runs, int x0, int width, int y, ChatFonts f, List<DrawOp> ops, Font forced, int role, byte brk0)
        {
            byte pending = brk0;
            width = Math.Max(UI.S(12), width);
            Font lineFont = forced ?? f.Body;
            int lineH = forced != null ? (int)Math.Ceiling(forced.GetHeight() * 1.18f) : f.LineH;
            int asc = -1;
            int x = x0;
            int right = x0 + width;
            bool lineEmpty = true;
            List<DrawOp> merged = null;
            int lineStart = ops.Count;
            double up = 0, down = 0;
            DrawOp cur = null;
            Action newline = delegate
            {
                y += Grow(ops, lineStart, up, down, asc, lineH) + lineH;
                x = x0;
                lineEmpty = true;
                cur = null;
                lineStart = ops.Count;
                up = 0;
                down = 0;
            };
            foreach (MdRun run0 in runs)
            {
                MdRun run = run0;
                if ((run.Style & MdRun.Math) != 0)
                {
                    List<MathPiece> pieces = null;
                    try { pieces = f.Maths.Inline(g, run.Text, (run.Style & MdRun.Display) != 0, lineFont.SizeInPoints); }
                    catch (Exception) { pieces = null; }
                    if (pieces != null)
                    {
                        if (asc < 0) asc = f.Maths.Ascent(lineFont);
                        int mr = run.Link != null ? DrawOp.RLink : role;
                        foreach (MathPiece pc in pieces)
                        {
                            int w = Math.Max(1, (int)Math.Ceiling(pc.Box.W));
                            if (x + w > right && !lineEmpty)
                            {
                                newline();
                                if (pending == 0) pending = 1;
                            }
                            DrawOp op = new DrawOp();
                            op.Kind = DrawOp.KMath;
                            op.Box = pc.Box;
                            op.Text = pc.Text;
                            op.Font = f.Body;
                            op.Role = mr;
                            op.Link = run.Link;
                            op.R = new Rectangle(x, y, w, lineH);
                            op.Base = asc;
                            op.Brk = pending;
                            pending = 0;
                            ops.Add(op);
                            up = Math.Max(up, pc.Box.H);
                            down = Math.Max(down, pc.Box.D);
                            x += w + (int)Math.Round(pc.Trail);
                            lineEmpty = false;
                            cur = null;
                        }
                        continue;
                    }
                    run = new MdRun(run.Text, MdRun.Code, run.Link);
                }
                bool code = (run.Style & MdRun.Code) != 0;
                Font font = forced != null ? (code ? f.Mono : forced) : f.For(run.Style);
                int r = run.Link != null ? DrawOp.RLink : code ? DrawOp.RCodeFore : role;
                int pad = code ? UI.S(3) : 0;
                List<string> tokens = Tokens(run.Text);
                cur = null;
                foreach (string tok in tokens)
                {
                    if (tok == "\n")
                    {
                        newline();
                        if (pending < 2) pending = 2;
                        continue;
                    }
                    bool space = tok.Trim().Length == 0;
                    if (space && lineEmpty) continue;
                    int w = f.Width(g, tok, font);
                    if (!space && x + w + pad * 2 > right && !lineEmpty)
                    {
                        newline();
                        if (pending == 0) pending = 1;
                    }
                    if (!space && w + pad * 2 > width)
                    {
                        string rest = tok;
                        while (rest.Length > 0)
                        {
                            int n = Fit(g, rest, font, right - x - pad * 2, f);
                            if (n <= 0)
                            {
                                if (!lineEmpty) { newline(); continue; }
                                n = 1;
                            }
                            string piece = rest.Substring(0, n);
                            int pw = f.Width(g, piece, font);
                            DrawOp op = NewText(piece, font, r, run, x, y, pw, lineH, code, pad);
                            op.Brk = pending;
                            pending = 4;
                            ops.Add(op);
                            x += pw + pad * 2;
                            lineEmpty = false;
                            rest = rest.Substring(n);
                            if (rest.Length > 0) newline();
                        }
                        pending = 0;
                        cur = null;
                        continue;
                    }
                    if (cur != null && cur.R.Y == y)
                    {
                        cur.Text += tok;
                        cur.R.Width += w;
                        x = cur.R.X + cur.R.Width + pad;
                        if (merged == null) merged = new List<DrawOp>();
                        if (merged.Count == 0 || merged[merged.Count - 1] != cur) merged.Add(cur);
                    }
                    else
                    {
                        if (space)
                        {
                            x += w;
                            continue;
                        }
                        DrawOp op = NewText(tok, font, r, run, x, y, w, lineH, code, pad);
                        op.Brk = pending;
                        pending = 0;
                        ops.Add(op);
                        cur = op;
                        x += w + pad * 2;
                    }
                    lineEmpty = false;
                }
            }
            if (merged != null)
                foreach (DrawOp op in merged)
                {
                    int exact = f.Width(g, op.Text, op.Font, false);
                    if (exact > 0) op.R.Width = exact;
                }
            return y + lineH + Grow(ops, lineStart, up, down, asc, lineH);
        }

        static int Grow(List<DrawOp> ops, int from, double up, double down, int asc, int lineH)
        {
            if (asc < 0 || (up <= 0 && down <= 0)) return 0;
            int slack = Math.Max(1, lineH / 8);
            int a = Math.Max(0, (int)Math.Ceiling(up - asc - slack));
            int d = Math.Max(0, (int)Math.Ceiling(down - (lineH - asc) - slack));
            if (a > 0) for (int k = from; k < ops.Count; k++) ops[k].R.Y += a;
            return a + d;
        }

        public static void MathWidth(Graphics g, MdRun run, ChatFonts f, Font font, out int total, out int widest)
        {
            total = widest = -1;
            List<MathPiece> pcs = null;
            try { pcs = f.Maths.Inline(g, run.Text, (run.Style & MdRun.Display) != 0, (font ?? f.Body).SizeInPoints); }
            catch (Exception) { pcs = null; }
            if (pcs == null) return;
            total = widest = 0;
            foreach (MathPiece pc in pcs)
            {
                int w = (int)Math.Ceiling(pc.Box.W);
                total += w + (int)Math.Round(pc.Trail);
                widest = Math.Max(widest, w);
            }
        }

        static int MathBlock(Graphics g, MdBlock b, int width, int y, ChatFonts f, Theme th, MdLayout L, int role)
        {
            float pt = f.Body.SizeInPoints;
            MathResult res = null;
            try { res = f.Maths.Display(g, b.Code, pt); }
            catch (Exception) { res = null; }
            if (res == null)
            {
                MdBlock cb = new MdBlock();
                cb.Kind = MdKind.Code;
                cb.Code = b.Code.Trim('\n', '\r');
                cb.Lang = "latex";
                return CodeBlock(g, cb, width, y, f, th, L);
            }
            int tagW = 0;
            if (!string.IsNullOrEmpty(res.Tag)) tagW = f.Width(g, res.Tag, f.Body) + UI.S(10);
            int indent = UI.S(28);
            int avail = Math.Max(UI.S(80), width - 2 * tagW);
            List<List<MathPiece>> lines = Pack(res.Pieces, avail, indent);
            double widest = 0;
            foreach (List<MathPiece> line in lines) widest = Math.Max(widest, LineWidth(line));
            if (widest > avail)
            {
                double scale = Math.Max(0.62, avail / widest * 0.98);
                MathResult small = null;
                try { small = f.Maths.Display(g, b.Code, (float)(pt * scale)); }
                catch (Exception) { small = null; }
                if (small != null)
                {
                    res = small;
                    lines = Pack(res.Pieces, avail, indent);
                }
            }
            y += UI.S(5);
            int top = y;
            for (int li = 0; li < lines.Count; li++)
            {
                List<MathPiece> line = lines[li];
                MBox box = new MBox();
                double x = 0;
                StringBuilder text = new StringBuilder();
                for (int k = 0; k < line.Count; k++)
                {
                    MathPiece pc = line[k];
                    box.Put(pc.Box, x, 0);
                    box.H = Math.Max(box.H, pc.Box.H);
                    box.D = Math.Max(box.D, pc.Box.D);
                    x += pc.Box.W;
                    text.Append(pc.Text);
                    if (k < line.Count - 1) x += pc.Trail;
                }
                box.W = x;
                int w = Math.Max(1, (int)Math.Ceiling(box.W));
                int h = (int)Math.Ceiling(box.H), d = (int)Math.Ceiling(box.D);
                int bx = lines.Count == 1 ? Math.Max(0, (width - w) / 2) : li == 0 ? 0 : indent;
                DrawOp op = new DrawOp();
                op.Kind = DrawOp.KMath;
                op.Box = box;
                op.Text = text.ToString().TrimEnd();
                op.Font = f.Body;
                op.Role = role;
                op.R = new Rectangle(bx, y, w, Math.Max(1, h + d));
                op.Base = h;
                op.Brk = (byte)(li == 0 ? 3 : 2);
                L.Ops.Add(op);
                y += Math.Max(1, h + d) + (li < lines.Count - 1 ? UI.S(5) : 0);
            }
            if (tagW > 0)
            {
                DrawOp t = new DrawOp();
                t.Kind = DrawOp.KText;
                t.Text = res.Tag;
                t.Font = f.Body;
                t.Role = DrawOp.RDim;
                int tw = f.Width(g, res.Tag, f.Body);
                t.R = new Rectangle(Math.Max(0, width - tw), top + Math.Max(0, (y - top - f.LineH) / 2), tw, f.LineH);
                t.Brk = 6;
                L.Ops.Add(t);
            }
            return y + UI.S(5);
        }

        internal static double LineWidth(List<MathPiece> line)
        {
            double w = 0;
            for (int k = 0; k < line.Count; k++) w += line[k].Box.W + (k < line.Count - 1 ? line[k].Trail : 0);
            return w;
        }

        internal static List<List<MathPiece>> Pack(List<MathPiece> pieces, int avail, int indent)
        {
            List<List<MathPiece>> lines = new List<List<MathPiece>>();
            if (LineWidth(pieces) <= avail) { lines.Add(pieces); return lines; }
            List<MathPiece> cur = new List<MathPiece>();
            double lw = 0;
            foreach (MathPiece pc in pieces)
            {
                double limit = lines.Count == 0 ? avail : avail - indent;
                if (cur.Count > 0 && lw + pc.Box.W > limit)
                {
                    lines.Add(cur);
                    cur = new List<MathPiece>();
                    lw = 0;
                }
                cur.Add(pc);
                lw += pc.Box.W + pc.Trail;
            }
            if (cur.Count > 0) lines.Add(cur);
            return lines;
        }

        static DrawOp NewText(string text, Font font, int role, MdRun run, int x, int y, int w, int lineH, bool code, int pad)
        {
            DrawOp op = new DrawOp();
            op.Kind = code ? DrawOp.KCodeSpan : DrawOp.KText;
            op.Text = text;
            op.Font = font;
            op.Role = role;
            op.Link = run.Link;
            op.Tag = (run.Style & MdRun.Strike) != 0 ? 1 : 0;
            op.R = new Rectangle(x + pad, y, w, lineH);
            return op;
        }

        public static int Fit(Graphics g, string s, Font font, int avail, ChatFonts f)
        {
            if (avail <= 0) return 0;
            int lo = 0, hi = Math.Min(s.Length, avail);
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (f.Width(g, s.Substring(0, mid), font, false) <= avail) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        static List<string> Tokens(string s)
        {
            List<string> t = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\n') { t.Add("\n"); i++; continue; }
                int j = i;
                if (c == ' ' || c == '\t')
                {
                    while (j < s.Length && (s[j] == ' ' || s[j] == '\t')) j++;
                    t.Add(s.Substring(i, j - i).Replace('\t', ' '));
                }
                else if (c >= 0x2E80)
                {
                    j = i + 1;
                    if (char.IsHighSurrogate(c) && j < s.Length) j++;
                    t.Add(s.Substring(i, j - i));
                }
                else
                {
                    while (j < s.Length && s[j] != ' ' && s[j] != '\t' && s[j] != '\n' && s[j] < 0x2E80)
                    {
                        j++;
                        if (j < s.Length && (s[j - 1] == '/' || s[j - 1] == '-' || s[j - 1] == '\\') && j - i > 12) break;
                    }
                    t.Add(s.Substring(i, j - i));
                }
                i = j;
            }
            return t;
        }

        static string ExtFor(string lang)
        {
            string l = (lang ?? "").Trim().ToLowerInvariant();
            switch (l)
            {
                case "": return null;
                case "csharp": case "c#": case "cs": return ".cs";
                case "javascript": case "js": case "jsx": case "node": return ".js";
                case "typescript": case "ts": case "tsx": return ".ts";
                case "python": case "py": case "python3": return ".py";
                case "bash": case "sh": case "shell": case "zsh": case "console": return ".sh";
                case "powershell": case "ps": case "ps1": case "pwsh": return ".ps1";
                case "batch": case "bat": case "cmd": return ".bat";
                case "c++": case "cpp": case "cxx": case "hpp": return ".cpp";
                case "rust": case "rs": return ".rs";
                case "golang": case "go": return ".go";
                case "yaml": case "yml": return ".yaml";
                case "markdown": case "md": return ".md";
                case "ruby": case "rb": return ".rb";
                case "kotlin": case "kt": return ".kt";
                case "html": case "xhtml": return ".html";
                case "xml": case "svg": return ".xml";
                case "patch": case "diff": return ".diff";
                case "dockerfile": return "Dockerfile";
                default: return "." + l;
            }
        }

        static int CodeBlock(Graphics g, MdBlock b, int width, int y, ChatFonts f, Theme th, MdLayout L)
        {
            int idx = L.CodeBlocks.Count;
            L.CodeBlocks.Add(b.Code);
            int pad = UI.S(10), head = f.SmallLineH + UI.S(8);
            int top = y;
            DrawOp bg = new DrawOp();
            bg.Kind = DrawOp.KCodeBlock;
            bg.Tag = idx;
            L.Ops.Add(bg);
            DrawOp lang = new DrawOp();
            lang.Kind = DrawOp.KText;
            lang.Text = b.Lang.Length > 0 ? b.Lang : "code";
            lang.Font = f.Small;
            lang.Role = DrawOp.RDim;
            lang.R = new Rectangle(pad, y + UI.S(4), width / 2, f.SmallLineH);
            lang.NoSelect = true;
            L.Ops.Add(lang);
            DrawOp copy = new DrawOp();
            copy.Kind = DrawOp.KCopy;
            copy.Tag = idx;
            int cw = f.Width(g, "Copy", f.Small) + UI.S(26);
            copy.R = new Rectangle(width - cw - UI.S(4), y + UI.S(2), cw, f.SmallLineH + UI.S(4));
            L.Ops.Add(copy);
            y += head;
            Lang syn = null;
            string ext = ExtFor(b.Lang);
            if (ext != null) { try { syn = Syntax.Detect(ext.StartsWith(".") ? "x" + ext : ext, null); } catch { syn = null; } }
            if (syn == Syntax.Plain) syn = null;
            string[] lines = b.Code.Split('\n');
            int state = 0;
            int avail = Math.Max(UI.S(40), width - pad * 2);
            int maxLines = 400;
            byte brk = 3;
            int blanks = 0;
            for (int li = 0; li < lines.Length && li < maxLines; li++)
            {
                string line = lines[li].Replace("\t", "    ");
                byte[] kinds = null;
                if (syn != null && line.Length < 2000)
                {
                    kinds = new byte[line.Length];
                    try { state = Syntax.Tokenize(syn, line, state, kinds); } catch { kinds = null; }
                }
                if (line.Length == 0) { y += f.MonoLineH; if (brk != 3) blanks++; continue; }
                int x = pad;
                int start = 0;
                bool firstSeg = true;
                while (start < line.Length)
                {
                    int end = start + 1;
                    int kind = kinds != null ? kinds[start] : 0;
                    while (end < line.Length && (kinds == null || kinds[end] == kind)) end++;
                    string seg = line.Substring(start, end - start);
                    int w = f.Width(g, seg, f.Mono);
                    if (x + w > pad + avail)
                    {
                        int n = Fit(g, seg, f.Mono, pad + avail - x, f);
                        if (n <= 0 && x > pad) { y += f.MonoLineH; x = pad + UI.S(14); continue; }
                        if (n <= 0) n = 1;
                        seg = seg.Substring(0, n);
                        end = start + n;
                        w = f.Width(g, seg, f.Mono);
                    }
                    DrawOp op = new DrawOp();
                    op.Kind = DrawOp.KText;
                    op.Text = seg;
                    op.Font = f.Mono;
                    op.Role = kinds != null ? DrawOp.RSyntax + kind : DrawOp.RCodeFore;
                    op.R = new Rectangle(x, y, w, f.MonoLineH);
                    if (firstSeg) { op.Brk = brk; op.Blank = (byte)Math.Min(250, blanks); brk = 2; blanks = 0; firstSeg = false; }
                    else if (x == pad + UI.S(14) && L.Ops.Count > 0 && L.Ops[L.Ops.Count - 1].R.Y != y) op.Brk = 4;
                    L.Ops.Add(op);
                    x += w;
                    start = end;
                    if (start < line.Length && x >= pad + avail - UI.S(2)) { y += f.MonoLineH; x = pad + UI.S(14); }
                }
                y += f.MonoLineH;
            }
            if (lines.Length > maxLines)
            {
                DrawOp more = new DrawOp();
                more.Kind = DrawOp.KText;
                more.Text = "(" + (lines.Length - maxLines) + " more lines; Copy gets all of it)";
                more.NoSelect = true;
                more.Font = f.Small;
                more.Role = DrawOp.RDim;
                more.R = new Rectangle(pad, y, width - pad * 2, f.SmallLineH);
                L.Ops.Add(more);
                y += f.SmallLineH;
            }
            y += UI.S(8);
            bg.R = new Rectangle(0, top, width, y - top);
            return y;
        }

        static int Table(Graphics g, MdBlock b, int width, int y, ChatFonts f, MdLayout L)
        {
            int cols = 0;
            foreach (List<string> r in b.Rows) cols = Math.Max(cols, r.Count);
            if (cols == 0) return y;
            List<List<MdRun>[]> cells = new List<List<MdRun>[]>();
            int[] natural = new int[cols];
            int[] word = new int[cols];
            for (int ri = 0; ri < b.Rows.Count; ri++)
            {
                List<string> r = b.Rows[ri];
                List<MdRun>[] row = new List<MdRun>[cols];
                for (int c = 0; c < cols; c++)
                {
                    row[c] = Markdown.Inline(c < r.Count ? r[c] : "");
                    int w = 0;
                    foreach (MdRun run in row[c])
                    {
                        if (ri == 0) run.Style |= MdRun.Bold;
                        if ((run.Style & MdRun.Math) != 0)
                        {
                            int mt, mw;
                            MathWidth(g, run, f, null, out mt, out mw);
                            if (mt >= 0)
                            {
                                w += mt;
                                word[c] = Math.Max(word[c], Math.Min(mw, UI.S(160)));
                                continue;
                            }
                        }
                        Font rf = f.For(run.Style);
                        bool code = (run.Style & MdRun.Code) != 0;
                        w += f.Width(g, run.Text, rf) + (code ? UI.S(8) : 0);
                        foreach (string part in run.Text.Split(' '))
                            if (part.Length > 0) word[c] = Math.Max(word[c], Math.Min(f.Width(g, part, rf) + (code ? UI.S(8) : 0), UI.S(160)));
                    }
                    natural[c] = Math.Max(natural[c], Math.Min(w + UI.S(2), UI.S(320)));
                }
                cells.Add(row);
            }
            int cellPad = UI.S(8);
            int total = 0;
            int[] minW = new int[cols];
            int minTotal = 0;
            for (int c = 0; c < cols; c++)
            {
                natural[c] += cellPad * 2;
                total += natural[c];
                minW[c] = Math.Min(natural[c], Math.Max(UI.S(48), word[c] + UI.S(2) + cellPad * 2));
                minTotal += minW[c];
            }
            if (minTotal > width && minTotal > 0)
                for (int c = 0; c < cols; c++) minW[c] = Math.Max(Math.Min(natural[c], UI.S(36)), (int)((long)minW[c] * width / minTotal));
            int[] colW = new int[cols];
            if (total <= width) for (int c = 0; c < cols; c++) colW[c] = natural[c];
            else
            {
                int flexible = 0;
                for (int c = 0; c < cols; c++) flexible += Math.Max(0, natural[c] - minW[c]);
                int excess = total - width;
                for (int c = 0; c < cols; c++)
                {
                    int share = flexible > 0 ? (int)((long)excess * Math.Max(0, natural[c] - minW[c]) / flexible) : excess / cols;
                    colW[c] = Math.Max(minW[c], natural[c] - share);
                }
            }
            int tableW = 0;
            foreach (int w in colW) tableW += w;
            for (int ri = 0; ri < cells.Count; ri++)
            {
                int x = 0;
                int rowBottom = y;
                for (int c = 0; c < cols; c++)
                {
                    List<MdRun> runs = cells[ri][c];
                    int before = L.Ops.Count;
                    int end = Flow(g, runs, x + cellPad, colW[c] - cellPad * 2, y + UI.S(4), f, L.Ops, null, DrawOp.RFore, (byte)(c > 0 ? 6 : ri == 0 ? 3 : 2));
                    rowBottom = Math.Max(rowBottom, end + UI.S(4));
                    char align = b.Align != null && c < b.Align.Count ? b.Align[c] : 'l';
                    if (align != 'l')
                    {
                        int used = 0;
                        for (int k = before; k < L.Ops.Count; k++) used = Math.Max(used, L.Ops[k].R.Right - (x + cellPad));
                        int shift = align == 'r' ? colW[c] - cellPad * 2 - used : (colW[c] - cellPad * 2 - used) / 2;
                        if (shift > 0) for (int k = before; k < L.Ops.Count; k++) L.Ops[k].R.X += shift;
                    }
                    x += colW[c];
                }
                DrawOp line = new DrawOp();
                line.Kind = DrawOp.KTableBorder;
                line.R = new Rectangle(0, rowBottom, tableW, 1);
                line.Tag = ri == 0 ? 1 : 0;
                L.Ops.Add(line);
                if (ri == 0)
                {
                    DrawOp band = new DrawOp();
                    band.Kind = DrawOp.KHeaderBand;
                    band.R = new Rectangle(0, y, tableW, rowBottom - y);
                    L.Ops.Insert(0, band);
                }
                y = rowBottom + 1;
            }
            return y;
        }

        public static MdLayout PlainFlow(Graphics g, string text, int width, ChatFonts f, Font font, int role)
        {
            MdLayout L = new MdLayout();
            int y = 0;
            bool first = true;
            foreach (string line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                List<MdRun> runs = new List<MdRun>();
                runs.Add(new MdRun(line.Length > 0 ? line.Replace("\t", "    ") : " ", 0, null));
                y = Flow(g, runs, 0, width, y, f, L.Ops, font == f.Body ? null : font, role, (byte)(first ? 3 : 2));
                first = false;
            }
            L.Height = y;
            return L;
        }

        public static MdLayout PlainBox(Graphics g, string text, int width, ChatFonts f, int maxLines)
        {
            MdLayout L = new MdLayout();
            int pad = UI.S(10);
            DrawOp bg = new DrawOp();
            bg.Kind = DrawOp.KCodeBlock;
            L.Ops.Add(bg);
            int y = UI.S(8);
            int avail = Math.Max(UI.S(40), width - pad * 2);
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            int shown = 0, hidden = 0;
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li].Replace("\t", "    ");
                if (shown >= maxLines)
                {
                    for (int k = li; k < lines.Length; k++) hidden += lines[k].Length + 1;
                    break;
                }
                if (line.Length == 0) { y += f.MonoLineH; shown++; continue; }
                string rest = line;
                bool firstPiece = true;
                while (rest.Length > 0)
                {
                    if (shown >= maxLines) { hidden += rest.Length; break; }
                    int n = Fit(g, rest, f.Mono, avail, f);
                    if (n <= 0) n = 1;
                    DrawOp op = new DrawOp();
                    op.Kind = DrawOp.KText;
                    op.Text = rest.Substring(0, n);
                    op.Font = f.Mono;
                    op.Role = DrawOp.RCodeFore;
                    op.R = new Rectangle(pad, y, f.Width(g, op.Text, f.Mono), f.MonoLineH);
                    op.Brk = (byte)(firstPiece ? 2 : 4);
                    firstPiece = false;
                    L.Ops.Add(op);
                    rest = rest.Substring(n);
                    y += f.MonoLineH;
                    shown++;
                }
            }
            if (hidden > 0)
            {
                DrawOp more = new DrawOp();
                more.Kind = DrawOp.KText;
                more.Text = "... " + hidden.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " more characters";
                more.NoSelect = true;
                more.Font = f.Mono;
                more.Role = DrawOp.RDim;
                more.R = new Rectangle(pad, y, f.Width(g, more.Text, f.Mono), f.MonoLineH);
                L.Ops.Add(more);
                y += f.MonoLineH;
            }
            L.Hidden = hidden;
            y += UI.S(8);
            bg.R = new Rectangle(0, 0, width, y);
            L.Height = y;
            return L;
        }

        public static Color RoleColor(int role, Theme th, bool dimAll)
        {
            if (role >= DrawOp.RSyntax)
            {
                int k = role - DrawOp.RSyntax;
                return k >= 0 && k < th.Syn.Length ? th.Syn[k] : th.Fore;
            }
            switch (role)
            {
                case DrawOp.RDim: return th.ChromeDim;
                case DrawOp.RLink: return th.Dark ? Theme.Blend(th.Accent, Color.White, 0.25) : Theme.Blend(th.Accent, Color.Black, 0.1);
                case DrawOp.RCodeFore: return th.Fore;
                case DrawOp.RAccent: return th.Accent;
                case DrawOp.RError: return th.HighContrast ? th.Fore : th.Error;
                default: return dimAll ? th.ChromeDim : th.Fore;
            }
        }

        public static Color CodeBack(Theme th) { return th.HighContrast ? th.Back : th.Dark ? Color.FromArgb(0x17, 0x17, 0x17) : Color.FromArgb(0xF5, 0xF5, 0xF4); }
        public static Color CodeBorder(Theme th) { return th.HighContrast ? th.Border : th.Dark ? Color.FromArgb(0x33, 0x33, 0x33) : Color.FromArgb(0xE3, 0xE3, 0xE0); }
        public static Color SpanBack(Theme th) { return th.HighContrast ? th.Back : th.Dark ? Color.FromArgb(0x30, 0x30, 0x30) : Color.FromArgb(0xEE, 0xEE, 0xEC); }

        public static void Paint(Graphics g, MdLayout L, int ox, int oy, Rectangle clip, Theme th, bool dimAll, string hoverLink, int hoverCopy, int copiedIdx, ChatFonts f)
        {
            Paint(g, L, ox, oy, clip, th, dimAll, hoverLink, hoverCopy, copiedIdx, f, -1, 0, -1, 0);
        }

        public static bool Selectable(DrawOp op) { return (op.Kind == DrawOp.KText || op.Kind == DrawOp.KCodeSpan || op.Kind == DrawOp.KMath) && !op.NoSelect && !string.IsNullOrEmpty(op.Text); }

        static int SelPad(DrawOp op) { return op.Kind == DrawOp.KCodeSpan ? UI.S(3) + 1 : op.Kind == DrawOp.KMath ? UI.S(1) : 0; }

        static int SelEdge(MdLayout L, int k, int selToOp, int spaceW)
        {
            DrawOp op = L.Ops[k];
            int right = op.R.Right + SelPad(op);
            for (int j = k + 1; j <= selToOp && j < L.Ops.Count; j++)
            {
                DrawOp nx = L.Ops[j];
                if (!Selectable(nx)) continue;
                if (nx.R.Y != op.R.Y) return -1;
                int left = nx.R.X - SelPad(nx);
                int gap = left - right;
                return gap >= 0 && gap <= spaceW + UI.S(8) ? left : -1;
            }
            return -1;
        }

        static bool Carried(MdLayout L, int k, int selFromOp, int selToOp, int spaceW)
        {
            for (int p = k - 1; p >= selFromOp && p >= 0; p--)
                if (Selectable(L.Ops[p])) return SelEdge(L, p, selToOp, spaceW) >= 0;
            return false;
        }

        static Rectangle PaintSel(Graphics g, DrawOp op, Rectangle r, int from, int to, Theme th, ChatFonts f, int extendTo)
        {
            int n = op.Text.Length;
            from = Math.Max(0, Math.Min(n, from));
            to = Math.Max(0, Math.Min(n, to));
            if (to <= from) return Rectangle.Empty;
            int pad = SelPad(op);
            int x0 = from > 0 ? r.X + f.Width(g, op.Text.Substring(0, from), op.Font, false) : r.X - pad;
            int x1 = to < n ? r.X + f.Width(g, op.Text.Substring(0, to), op.Font, false) : r.X + Math.Max(r.Width, f.Width(g, op.Text, op.Font, false)) + pad;
            if (to >= n && extendTo > x1) x1 = extendTo;
            Color c = th.HighContrast ? SystemColors.Highlight : th.Sel;
            Rectangle sr = new Rectangle(x0, r.Y, Math.Max(1, x1 - x0), r.Height);
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, sr);
            return sr;
        }

        static void SelText(Graphics g, Rectangle sel, string text, Font font, Point at, bool strike, Rectangle r)
        {
            if (sel.IsEmpty) return;
            GraphicsState gs = g.Save();
            g.SetClip(sel, CombineMode.Intersect);
            TextRenderer.DrawText(g, text, font, at, SystemColors.HighlightText, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
            if (strike) using (Pen p = new Pen(SystemColors.HighlightText)) g.DrawLine(p, r.X, r.Y + r.Height / 2, r.Right, r.Y + r.Height / 2);
            g.Restore(gs);
        }

        public static void Paint(Graphics g, MdLayout L, int ox, int oy, Rectangle clip, Theme th, bool dimAll, string hoverLink, int hoverCopy, int copiedIdx, ChatFonts f, int selFromOp, int selFromCh, int selToOp, int selToCh)
        {
            int spaceW = selFromOp >= 0 ? Math.Max(1, f.Width(g, "x x", f.Body) - f.Width(g, "xx", f.Body)) : 0;
            for (int k = 0; k < L.Ops.Count; k++)
            {
                DrawOp op = L.Ops[k];
                Rectangle r = new Rectangle(op.R.X + ox, op.R.Y + oy, op.R.Width, op.R.Height);
                int inkTop = r.Top, inkBottom = r.Bottom;
                if (op.Kind == DrawOp.KMath && op.Box != null)
                {
                    inkTop = Math.Min(inkTop, r.Y + op.Base - (int)Math.Ceiling(op.Box.H) - 1);
                    inkBottom = Math.Max(inkBottom, r.Y + op.Base + (int)Math.Ceiling(op.Box.D) + 1);
                }
                if (inkBottom < clip.Top - UI.S(4) || inkTop > clip.Bottom + UI.S(4)) continue;
                bool sel = selFromOp >= 0 && k >= selFromOp && k <= selToOp && Selectable(op);
                int sa = k == selFromOp ? selFromCh : 0, sb = k == selToOp ? selToCh : int.MaxValue;
                Rectangle selRect = Rectangle.Empty;
                int edge = sel ? SelEdge(L, k, selToOp, spaceW) : -1;
                if (edge >= 0) edge += ox;
                if (sel && op.Kind == DrawOp.KText) selRect = PaintSel(g, op, r, sa, sb, th, f, edge);
                Rectangle hcClip = selRect;
                switch (op.Kind)
                {
                    case DrawOp.KText:
                        {
                            Color c = RoleColor(op.Role, th, dimAll);
                            Font u = op.Link != null && op.Link == hoverLink ? new Font(op.Font, op.Font.Style | FontStyle.Underline) : null;
                            try
                            {
                                Font font = u ?? op.Font;
                                TextRenderer.DrawText(g, op.Text, font, new Point(r.X, r.Y), c, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                                if (op.Tag == 1)
                                    using (Pen p = new Pen(c)) g.DrawLine(p, r.X, r.Y + r.Height / 2, r.Right, r.Y + r.Height / 2);
                                if (th.HighContrast)
                                {
                                    if (!selRect.IsEmpty && sa == 0 && k > selFromOp && Carried(L, k, selFromOp, selToOp, spaceW)) hcClip = Rectangle.FromLTRB(selRect.Left - UI.S(3), selRect.Top, selRect.Right, selRect.Bottom);
                                    SelText(g, hcClip, op.Text, font, new Point(r.X, r.Y), op.Tag == 1, r);
                                }
                            }
                            finally { if (u != null) u.Dispose(); }
                            break;
                        }
                    case DrawOp.KCodeSpan:
                        {
                            int pad = UI.S(3);
                            int h = (int)Math.Ceiling(op.Font.GetHeight()) + UI.S(2);
                            Rectangle bg = new Rectangle(r.X - pad, r.Y + (r.Height - h) / 2, r.Width + pad * 2, h);
                            Glyphs.FillRound(g, SpanBack(th), bg, UI.S(4));
                            if (sel) selRect = PaintSel(g, op, r, sa, sb, th, f, edge);
                            Color c = op.Link != null ? RoleColor(DrawOp.RLink, th, false) : RoleColor(DrawOp.RCodeFore, th, false);
                            Point at = new Point(r.X, r.Y + (r.Height - (int)Math.Ceiling(op.Font.GetHeight())) / 2);
                            TextRenderer.DrawText(g, op.Text, op.Font, at, c, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                            if (th.HighContrast) SelText(g, selRect, op.Text, op.Font, at, false, r);
                            break;
                        }
                    case DrawOp.KMath:
                        {
                            if (sel && sa < sb && sa < op.Text.Length)
                                using (SolidBrush hb = new SolidBrush(th.HighContrast ? SystemColors.Highlight : th.Sel)) g.FillRectangle(hb, r.X - UI.S(1), r.Y, Math.Max(r.Width + UI.S(2), edge - (r.X - UI.S(1))), r.Height);
                            Color c = op.Link != null ? RoleColor(DrawOp.RLink, th, false) : RoleColor(op.Role, th, dimAll);
                            if (th.HighContrast && sel && sa < sb && sa < op.Text.Length) c = SystemColors.HighlightText;
                            MathPaint.Draw(g, op.Box, r.X, r.Y + op.Base, c, f.Maths);
                            break;
                        }
                    case DrawOp.KCodeBlock:
                        Glyphs.FillRound(g, CodeBack(th), r, UI.S(8));
                        Glyphs.DrawRound(g, CodeBorder(th), new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), UI.S(8));
                        break;
                    case DrawOp.KCopy:
                        {
                            bool hot = hoverCopy == op.Tag;
                            bool copied = copiedIdx == op.Tag;
                            if (hot) Glyphs.Hover(g, th, th.ButtonHover, r, UI.S(4));
                            Rectangle ic = new Rectangle(r.X + UI.S(4), r.Y, UI.S(16), r.Height);
                            Glyphs.Draw(copied ? "check" : "copy", g, ic, th.ChromeDim);
                            TextRenderer.DrawText(g, copied ? "Copied" : "Copy", f.Small, new Rectangle(r.X + UI.S(22), r.Y, r.Width - UI.S(22), r.Height), th.ChromeDim,
                                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                            break;
                        }
                    case DrawOp.KLine:
                        using (Pen p = new Pen(th.Border)) g.DrawLine(p, r.X, r.Y, r.Right, r.Y);
                        break;
                    case DrawOp.KBar:
                        using (SolidBrush fill = new SolidBrush(Charts.Shade(th, op.Tag))) g.FillRectangle(fill, r);
                        break;
                    case DrawOp.KSeg:
                    case DrawOp.KDot:
                        {
                            SmoothingMode was = g.SmoothingMode;
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            Color shade = Charts.Shade(th, op.Tag);
                            if (op.Kind == DrawOp.KDot)
                            {
                                using (SolidBrush fill = new SolidBrush(shade)) g.FillEllipse(fill, r);
                            }
                            else
                            {
                                using (Pen stroke = new Pen(shade, UI.S(2)))
                                {
                                    stroke.StartCap = stroke.EndCap = LineCap.Round;
                                    g.DrawLine(stroke, r.X, op.Role == 1 ? r.Bottom : r.Y, r.Right, op.Role == 1 ? r.Y : r.Bottom);
                                }
                            }
                            g.SmoothingMode = was;
                            break;
                        }
                    case DrawOp.KQuoteBar:
                        using (SolidBrush b = new SolidBrush(th.Border)) g.FillRectangle(b, r);
                        break;
                    case DrawOp.KTableBorder:
                        using (Pen p = new Pen(op.Tag == 1 ? th.ChromeDim : th.Border)) g.DrawLine(p, r.X, r.Y, r.Right, r.Y);
                        break;
                    case DrawOp.KHeaderBand:
                        using (SolidBrush b = new SolidBrush(SpanBack(th))) g.FillRectangle(b, r);
                        break;
                    case DrawOp.KCheck:
                        {
                            Glyphs.DrawRound(g, th.ChromeDim, r, UI.S(3));
                            if (op.Tag == 1) Glyphs.Draw("check", g, r, th.Accent);
                            break;
                        }
                }
            }
        }

        public static DrawOp Hit(MdLayout L, Point p)
        {
            for (int i = L.Ops.Count - 1; i >= 0; i--)
            {
                DrawOp op = L.Ops[i];
                if ((op.Link != null || op.Kind == DrawOp.KCopy) && op.R.Contains(p)) return op;
            }
            return null;
        }
    }
}
