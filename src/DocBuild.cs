using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGent
{
    class DocSpan
    {
        public string Text = "";
        public bool Bold, Italic, Code, Strike, Break;
        public string Link;

        public bool SameLook(DocSpan o)
        {
            return o != null && !Break && !o.Break && Bold == o.Bold && Italic == o.Italic && Code == o.Code && Strike == o.Strike && Link == o.Link;
        }
    }

    enum DocKind { Heading, Para, Item, Code, Quote, Table, Rule, PageBreak, Picture, Math }

    class DocPicture
    {
        public byte[] Data;
        public bool Jpeg;
        public int Width, Height;
        public double DpiX = 96, DpiY = 96;
        public string Alt = "";
        public string Name = "";
        public int Depth;
        public bool Figure;
        public int Tint = -1;
    }

    class DocBlock
    {
        public DocKind Kind;
        public int Level;
        public bool Ordered;
        public int Number;
        public int ListId, Run;
        public int Task = -1;
        public List<DocSpan> Spans = new List<DocSpan>();
        public string Code = "", Lang = "", Tex = "";
        public List<List<List<DocSpan>>> Rows;
        public List<char> Align;
        public DocPicture Picture;

        public string Plain
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                foreach (DocSpan s in Spans) sb.Append(s.Break ? "\n" : s.Text);
                return sb.ToString();
            }
        }
    }

    delegate DocPicture DocPictureLoader(string source, out string problem);

    class DocModel
    {
        public readonly List<DocBlock> Blocks = new List<DocBlock>();
        public string Title = "";
        public readonly List<string> Problems = new List<string>();

        const char Mark = '\u0001';
        const string BreakMark = "\u0001pagebreak\u0001";
        static readonly Regex PageBreakLine = new Regex(@"^(\\pagebreak|\\newpage|<!--\s*page\s*-?\s*break\s*-->|<(div|p)\b[^>]*page-break-(after|before)\s*:\s*always[^>]*>\s*</(div|p)>|<(div|p)\b[^>]*page-break-(after|before)\s*:\s*always[^>]*/>)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex CommentLine = new Regex(@"^<!--.*-->$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        static readonly Regex ImageRef = new Regex("\\G!\\[([^\\]\\n]{0,500})\\]\\(\\s*<?([^)\\s>]{1,2000})>?(?:\\s+\"[^\"\\n]{0,500}\")?\\s*\\)", RegexOptions.CultureInvariant);
        static readonly Regex ImageToken = new Regex("\u0002(\\d{1,5})\u0002", RegexOptions.CultureInvariant);
        static readonly Regex HtmlBreak = new Regex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        readonly List<string[]> images = new List<string[]>();

        public static DocModel FromMarkdown(string markdown, DocPictureLoader loader)
        {
            DocModel m = new DocModel();
            string text = m.Prepare(markdown ?? "");
            foreach (MdBlock b in Markdown.Parse(text)) m.Add(b, loader);
            m.Number();
            foreach (DocBlock b in m.Blocks)
                if (b.Kind == DocKind.Heading && (m.Title.Length == 0 || b.Level == 1) && b.Plain.Trim().Length > 0)
                {
                    m.Title = Flat(b.Plain);
                    if (b.Level == 1) break;
                }
            return m;
        }

        static string Flat(string s)
        {
            string t = Regex.Replace(s ?? "", @"\s+", " ").Trim();
            return t.Length > 200 ? t.Substring(0, 200) : t;
        }

        string Prepare(string md)
        {
            string[] lines = md.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder sb = new StringBuilder();
            string fence = null;
            foreach (string raw in lines)
            {
                string line = raw.Replace(Mark.ToString(), "").Replace("\u0002", "");
                string t = line.Trim();
                if (fence != null)
                {
                    if (t.StartsWith(fence) && t.Trim(fence[0]).Length == 0) fence = null;
                    sb.Append(line).Append('\n');
                    continue;
                }
                if (t.StartsWith("```") || t.StartsWith("~~~"))
                {
                    int n = 0;
                    while (n < t.Length && t[n] == t[0]) n++;
                    fence = t.Substring(0, n);
                    sb.Append(line).Append('\n');
                    continue;
                }
                if (PageBreakLine.IsMatch(t)) { sb.Append('\n').Append(BreakMark).Append("\n\n"); continue; }
                if (CommentLine.IsMatch(t)) continue;
                sb.Append(Images(line)).Append('\n');
            }
            return sb.ToString();
        }

        string Images(string line)
        {
            if (line.IndexOf("![", StringComparison.Ordinal) < 0) return line;
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == '`')
                {
                    int ticks = 0;
                    while (i + ticks < line.Length && line[i + ticks] == '`') ticks++;
                    int close = line.IndexOf(new string('`', ticks), i + ticks, StringComparison.Ordinal);
                    if (close > i)
                    {
                        sb.Append(line, i, close + ticks - i);
                        i = close + ticks;
                        continue;
                    }
                    sb.Append(line, i, ticks);
                    i += ticks;
                    continue;
                }
                if (c == '!' && i + 1 < line.Length && line[i + 1] == '[')
                {
                    Match m = ImageRef.Match(line, i);
                    if (m.Success && m.Index == i)
                    {
                        images.Add(new string[] { m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim() });
                        sb.Append('\u0002').Append((images.Count - 1).ToString(CultureInfo.InvariantCulture)).Append('\u0002');
                        i += m.Length;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        void Add(MdBlock b, DocPictureLoader loader)
        {
            switch (b.Kind)
            {
                case MdKind.Heading:
                    AddText(DocKind.Heading, b, loader);
                    break;
                case MdKind.Para:
                    if (Markdown.PlainText(b.Runs).Trim() == BreakMark) { Blocks.Add(new DocBlock { Kind = DocKind.PageBreak }); break; }
                    AddText(DocKind.Para, b, loader);
                    break;
                case MdKind.Quote:
                    AddText(DocKind.Quote, b, loader);
                    break;
                case MdKind.Item:
                    {
                        DocBlock d = AddText(DocKind.Item, b, loader);
                        if (d != null)
                        {
                            d.Level = Math.Max(0, Math.Min(4, b.Level));
                            d.Ordered = !string.IsNullOrEmpty(b.Marker) && char.IsDigit(b.Marker[0]);
                            int n;
                            d.Number = d.Ordered && int.TryParse(b.Marker.TrimEnd('.', ')'), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 1;
                            d.Task = b.Task;
                        }
                        break;
                    }
                case MdKind.Code:
                    Blocks.Add(new DocBlock { Kind = DocKind.Code, Code = Clean(Untoken(b.Code)).TrimEnd('\n'), Lang = b.Lang ?? "" });
                    break;
                case MdKind.Math:
                    Blocks.Add(new DocBlock { Kind = DocKind.Math, Code = Clean(DocMath.ToText(b.Code)), Tex = Clean(b.Code).Trim() });
                    break;
                case MdKind.Rule:
                    Blocks.Add(new DocBlock { Kind = DocKind.Rule });
                    break;
                case MdKind.Table:
                    {
                        DocBlock t = new DocBlock { Kind = DocKind.Table, Rows = new List<List<List<DocSpan>>>(), Align = new List<char>(b.Align ?? new List<char>()) };
                        int cols = 0;
                        foreach (List<string> r in b.Rows) cols = Math.Max(cols, r.Count);
                        cols = Math.Min(cols, 64);
                        foreach (List<string> r in b.Rows)
                        {
                            List<List<DocSpan>> row = new List<List<DocSpan>>();
                            for (int c = 0; c < cols; c++)
                            {
                                List<DocSpan> cell = Spans(Markdown.Inline(c < r.Count ? r[c] : ""));
                                cell = TakePictures(cell, null, new List<DocPicture>());
                                row.Add(cell);
                            }
                            t.Rows.Add(row);
                        }
                        while (t.Align.Count < cols) t.Align.Add('l');
                        if (t.Rows.Count > 0 && cols > 0) Blocks.Add(t);
                        break;
                    }
            }
        }

        string Untoken(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\u0002') < 0) return text ?? "";
            return ImageToken.Replace(text, delegate(Match m)
            {
                int idx;
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) || idx < 0 || idx >= images.Count) return "";
                return "![" + images[idx][0] + "](" + images[idx][1] + ")";
            });
        }

        DocBlock AddText(DocKind kind, MdBlock b, DocPictureLoader loader)
        {
            List<DocPicture> pics = new List<DocPicture>();
            List<DocSpan> spans = TakePictures(Spans(b.Runs), loader, pics);
            DocBlock d = null;
            bool empty = true;
            foreach (DocSpan s in spans) if (s.Break || s.Text.Trim().Length > 0) { empty = false; break; }
            if (!empty || kind == DocKind.Item)
            {
                while (spans.Count > 0 && spans[spans.Count - 1].Break) spans.RemoveAt(spans.Count - 1);
                while (spans.Count > 0 && spans[0].Break) spans.RemoveAt(0);
                d = new DocBlock { Kind = kind, Level = kind == DocKind.Heading ? Math.Max(1, Math.Min(6, b.Level)) : 0, Spans = spans };
                Blocks.Add(d);
            }
            foreach (DocPicture p in pics)
            {
                p.Figure = d == null && pics.Count == 1 && kind == DocKind.Para;
                Blocks.Add(new DocBlock { Kind = DocKind.Picture, Picture = p });
            }
            return d;
        }

        List<DocSpan> TakePictures(List<DocSpan> spans, DocPictureLoader loader, List<DocPicture> pics)
        {
            List<DocSpan> outSpans = new List<DocSpan>();
            foreach (DocSpan s in spans)
            {
                if (s.Break || s.Text.IndexOf('\u0002') < 0) { outSpans.Add(s); continue; }
                int last = 0;
                foreach (Match m in ImageToken.Matches(s.Text))
                {
                    if (m.Index > last) outSpans.Add(Piece(s, s.Text.Substring(last, m.Index - last).Replace("\u0002", "")));
                    last = m.Index + m.Length;
                    int idx;
                    if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) || idx < 0 || idx >= images.Count) continue;
                    string alt = images[idx][0], src = images[idx][1];
                    string problem = null;
                    DocPicture p = loader != null ? loader(src, out problem) : null;
                    if (p != null)
                    {
                        p.Alt = alt;
                        pics.Add(p);
                        if (outSpans.Count > 0 && !outSpans[outSpans.Count - 1].Break && outSpans[outSpans.Count - 1].Text.EndsWith(" ") && last < s.Text.Length && s.Text[last] == ' ') last++;
                    }
                    else
                    {
                        if (problem != null && !Problems.Contains(problem)) Problems.Add(problem);
                        if (alt.Length > 0) outSpans.Add(Piece(s, "[" + alt + "]"));
                    }
                }
                if (last < s.Text.Length) outSpans.Add(Piece(s, s.Text.Substring(last).Replace("\u0002", "")));
            }
            List<DocSpan> merged = new List<DocSpan>();
            foreach (DocSpan s in outSpans)
            {
                if (!s.Break && s.Text.Length == 0) continue;
                if (merged.Count > 0 && merged[merged.Count - 1].SameLook(s)) merged[merged.Count - 1].Text += s.Text;
                else merged.Add(s);
            }
            return merged;
        }

        static DocSpan Piece(DocSpan like, string text)
        {
            return new DocSpan { Text = text, Bold = like.Bold, Italic = like.Italic, Code = like.Code, Strike = like.Strike, Link = like.Link };
        }

        public static List<DocSpan> Spans(List<MdRun> runs)
        {
            List<DocSpan> list = new List<DocSpan>();
            foreach (MdRun r in runs)
            {
                bool math = (r.Style & MdRun.Math) != 0;
                bool code = (r.Style & MdRun.Code) != 0;
                string text = math ? DocMath.ToText(r.Text) : r.Text;
                text = Clean(text, '\u0002');
                string link = SafeLink(r.Link);
                string[] parts = code ? new string[] { text.Replace('\n', ' ') } : HtmlBreak.Split(text);
                for (int p = 0; p < parts.Length; p++)
                {
                    if (p > 0) list.Add(new DocSpan { Break = true });
                    string[] lines = parts[p].Split('\n');
                    for (int l = 0; l < lines.Length; l++)
                    {
                        if (l > 0) list.Add(new DocSpan { Break = true });
                        if (lines[l].Length == 0) continue;
                        DocSpan s = new DocSpan { Text = lines[l], Bold = (r.Style & MdRun.Bold) != 0, Italic = (r.Style & MdRun.Italic) != 0 || math, Code = code, Strike = (r.Style & MdRun.Strike) != 0, Link = link };
                        if (list.Count > 0 && list[list.Count - 1].SameLook(s)) list[list.Count - 1].Text += s.Text;
                        else list.Add(s);
                    }
                }
            }
            return Linkify(list);
        }

        static readonly Regex AutoLink = new Regex(@"<(?<u>(?:https?://|mailto:)[^\s<>]{1,2000})>|<(?<m>[A-Za-z0-9._%+\-]{1,64}@[A-Za-z0-9.\-]{1,253}\.[A-Za-z]{2,24})>|(?<![\w/@.\-])(?<b>https?://[^\s<>""\u0002]{2,2000})|(?<![\w/@.\-])(?<w>www\.[A-Za-z0-9\-]{1,63}(?:\.[A-Za-z0-9\-]{1,63})+(?:[/?#][^\s<>""\u0002]{0,2000})?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static List<DocSpan> Linkify(List<DocSpan> spans)
        {
            List<DocSpan> outList = null;
            for (int i = 0; i < spans.Count; i++)
            {
                DocSpan s = spans[i];
                if (s.Break || s.Code || s.Link != null || s.Text.Length < 8 || (s.Text.IndexOf("://", StringComparison.Ordinal) < 0 && s.Text.IndexOf('@') < 0 && s.Text.IndexOf("www.", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    if (outList != null) outList.Add(s);
                    continue;
                }
                List<DocSpan> pieces = new List<DocSpan>();
                int last = 0;
                foreach (Match m in AutoLink.Matches(s.Text))
                {
                    string shown, target;
                    int len = m.Length;
                    if (m.Groups["u"].Success) { shown = m.Groups["u"].Value; target = shown; if (shown.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) shown = shown.Substring(7); }
                    else if (m.Groups["m"].Success) { shown = m.Groups["m"].Value; target = "mailto:" + shown; }
                    else
                    {
                        shown = m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["w"].Value;
                        shown = TrimUrl(shown);
                        len = shown.Length;
                        target = m.Groups["w"].Success ? "http://" + shown : shown;
                    }
                    string safe = SafeLink(target);
                    if (safe == null || shown.Length == 0) continue;
                    if (m.Index > last) pieces.Add(Piece(s, s.Text.Substring(last, m.Index - last)));
                    DocSpan link = Piece(s, shown);
                    link.Link = safe;
                    pieces.Add(link);
                    last = m.Index + len;
                }
                if (pieces.Count == 0)
                {
                    if (outList != null) outList.Add(s);
                    continue;
                }
                if (last < s.Text.Length) pieces.Add(Piece(s, s.Text.Substring(last)));
                if (outList == null) { outList = new List<DocSpan>(); for (int k = 0; k < i; k++) outList.Add(spans[k]); }
                outList.AddRange(pieces);
            }
            return outList ?? spans;
        }

        static string TrimUrl(string u)
        {
            while (u.Length > 0)
            {
                char c = u[u.Length - 1];
                if (".,;:!?'\"*_~".IndexOf(c) >= 0) { u = u.Substring(0, u.Length - 1); continue; }
                if (c == ')' || c == ']')
                {
                    char open = c == ')' ? '(' : '[';
                    int opens = 0, closes = 0;
                    foreach (char k in u) { if (k == open) opens++; else if (k == c) closes++; }
                    if (closes > opens) { u = u.Substring(0, u.Length - 1); continue; }
                }
                break;
            }
            return u;
        }

        public static string SafeLink(string link)
        {
            if (string.IsNullOrEmpty(link)) return null;
            string l = link.Trim();
            if (l.Length > 2000 || l.IndexOfAny(new char[] { '"', '<', '>', ' ', '\n', '\r', '\t' }) >= 0) return null;
            if (l.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || l.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || l.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                Uri u;
                return Uri.TryCreate(l, UriKind.Absolute, out u) ? l : null;
            }
            return null;
        }

        public static string Clean(string s)
        {
            return Clean(s, '\0');
        }

        static string Clean(string s, char keep)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok;
                if (char.IsHighSurrogate(c)) { ok = i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]); if (ok) { if (sb != null) sb.Append(c).Append(s[i + 1]); i++; continue; } }
                else if (char.IsLowSurrogate(c)) ok = false;
                else ok = c == '\t' || c == '\n' || (c == keep && keep != '\0') || (c >= ' ' && c != '\uFFFE' && c != '\uFFFF' && !(c >= '\u007F' && c <= '\u009F'));
                if (ok) { if (sb != null) sb.Append(c); continue; }
                if (sb == null) { sb = new StringBuilder(s.Length); sb.Append(s, 0, i); }
                if (c == '\r') sb.Append('\n');
            }
            return sb == null ? s : sb.ToString();
        }

        void Number()
        {
            int list = 0, run = 0;
            bool inList = false;
            int[] count = new int[8];
            int[] runs = new int[8];
            bool[] ord = new bool[8];
            foreach (DocBlock b in Blocks)
            {
                if (b.Kind != DocKind.Item) { inList = false; continue; }
                if (!inList) { list++; inList = true; Array.Clear(count, 0, count.Length); }
                b.ListId = list;
                for (int l = b.Level + 1; l < count.Length; l++) count[l] = 0;
                if (count[b.Level] > 0 && ord[b.Level] != b.Ordered) count[b.Level] = 0;
                ord[b.Level] = b.Ordered;
                if (count[b.Level] == 0) runs[b.Level] = ++run;
                b.Run = runs[b.Level];
                if (b.Ordered)
                {
                    count[b.Level] = count[b.Level] == 0 ? Math.Max(0, Math.Min(99999, b.Number)) : count[b.Level] + 1;
                    b.Number = count[b.Level];
                }
                else count[b.Level] = 1;
            }
        }

        public static string Bullet(int level)
        {
            switch (level % 3)
            {
                case 0: return "\u2022";
                case 1: return "\u25E6";
                default: return "\u25AA";
            }
        }

        public static string OrderedLabel(int level, int n)
        {
            switch (level % 3)
            {
                case 1: return Letters(n) + ".";
                case 2: return Roman(n) + ".";
                default: return n.ToString(CultureInfo.InvariantCulture) + ".";
            }
        }

        static string Letters(int n)
        {
            if (n < 1) return n.ToString(CultureInfo.InvariantCulture);
            StringBuilder sb = new StringBuilder();
            while (n > 0) { n--; sb.Insert(0, (char)('a' + n % 26)); n /= 26; }
            return sb.ToString();
        }

        static string Roman(int n)
        {
            if (n < 1 || n > 3999) return n.ToString(CultureInfo.InvariantCulture);
            string[] r = { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(r[i]); n -= v[i]; }
            return sb.ToString();
        }
    }

    static class DocPictures
    {
        public const long MaxBytes = 32L * 1024 * 1024;
        const int MaxSide = 12000;

        public static DocPicture Load(string full, out string problem)
        {
            problem = null;
            try
            {
                FileInfo fi = new FileInfo(full);
                if (!fi.Exists) { problem = "image not found: " + full; return null; }
                if (fi.Length > MaxBytes) { problem = "image too large (over 32 MB): " + full; return null; }
                byte[] data = File.ReadAllBytes(full);
                return FromBytes(data, Path.GetFileName(full), out problem);
            }
            catch (Exception ex) { problem = "could not read the image " + full + ": " + ex.Message; return null; }
        }

        static bool KnownFormat(byte[] d)
        {
            if (d == null || d.Length < 8) return false;
            if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return true;
            if (d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return true;
            if (d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x38) return true;
            return d[0] == 0x42 && d[1] == 0x4D;
        }

        static int Orientation(byte[] d)
        {
            try
            {
                if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return 1;
                int i = 2;
                while (i + 4 <= d.Length)
                {
                    if (d[i] != 0xFF) return 1;
                    byte m = d[i + 1];
                    if (m == 0xFF) { i++; continue; }
                    if (m == 0xDA || m == 0xD9) return 1;
                    if (m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
                    int len = (d[i + 2] << 8) | d[i + 3];
                    if (len < 2 || i + 2 + len > d.Length) return 1;
                    int b = i + 4;
                    if (m == 0xE1 && len >= 16 && d[b] == 'E' && d[b + 1] == 'x' && d[b + 2] == 'i' && d[b + 3] == 'f' && d[b + 4] == 0)
                    {
                        int t = b + 6, end = i + 2 + len;
                        bool le = d[t] == 'I';
                        Func<int, int> u16 = delegate (int at) { return at + 2 > end ? -1 : le ? d[at] | (d[at + 1] << 8) : (d[at] << 8) | d[at + 1]; };
                        Func<int, long> u32 = delegate (int at)
                        {
                            if (at + 4 > end) return -1;
                            uint v = le ? (uint)(d[at] | (d[at + 1] << 8) | (d[at + 2] << 16) | (d[at + 3] << 24)) : (uint)((d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3]);
                            return v;
                        };
                        long ifd = u32(t + 4);
                        if (ifd < 8 || t + ifd + 2 > end) return 1;
                        int at0 = t + (int)ifd, n = u16(at0);
                        for (int k = 0; k < n && k < 200; k++)
                        {
                            int e = at0 + 2 + k * 12;
                            if (e + 12 > end) break;
                            if (u16(e) == 0x0112)
                            {
                                int v = u16(e + 8);
                                return v >= 1 && v <= 8 ? v : 1;
                            }
                        }
                        return 1;
                    }
                    i += 2 + len;
                }
            }
            catch (Exception) { }
            return 1;
        }

        static readonly RotateFlipType[] Turns = { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone,
            RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };

        public static DocPicture FromBytes(byte[] data, string name, out string problem)
        {
            problem = null;
            if (!KnownFormat(data)) { problem = "the image " + name + " is not a PNG, JPEG, GIF or BMP picture, so it was left out"; return null; }
            try
            {
                using (MemoryStream ms = new MemoryStream(data))
                using (Image img = Image.FromStream(ms, false, true))
                {
                    if (img is Metafile) { problem = "the image " + name + " is a metafile, so it was left out"; return null; }
                    if (img.Width < 1 || img.Height < 1 || img.Width > MaxSide || img.Height > MaxSide) { problem = "the image " + name + " has an unsupported size (" + img.Width + " x " + img.Height + ")"; return null; }
                    DocPicture p = new DocPicture();
                    p.Name = name ?? "";
                    p.DpiX = img.HorizontalResolution >= 30 && img.HorizontalResolution <= 1200 ? img.HorizontalResolution : 96;
                    p.DpiY = img.VerticalResolution >= 30 && img.VerticalResolution <= 1200 ? img.VerticalResolution : 96;
                    bool jpeg = img.RawFormat.Guid == ImageFormat.Jpeg.Guid;
                    int turn = jpeg ? Orientation(data) : 1;
                    if (jpeg && turn == 1 && IsPlainJpeg(data))
                    {
                        p.Jpeg = true;
                        p.Width = img.Width;
                        p.Height = img.Height;
                        p.Data = StripJpeg(data);
                        return p;
                    }
                    using (Bitmap bm = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(bm))
                        {
                            g.Clear(Color.Transparent);
                            g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
                        }
                        if (turn > 1)
                        {
                            bm.RotateFlip(Turns[turn]);
                            if (turn >= 5) { double t = p.DpiX; p.DpiX = p.DpiY; p.DpiY = t; }
                        }
                        p.Width = bm.Width;
                        p.Height = bm.Height;
                        using (MemoryStream outMs = new MemoryStream())
                        {
                            if (jpeg)
                            {
                                ImageCodecInfo codec = null;
                                foreach (ImageCodecInfo ci in ImageCodecInfo.GetImageEncoders()) if (ci.FormatID == ImageFormat.Jpeg.Guid) codec = ci;
                                if (codec != null)
                                {
                                    using (Bitmap flat = new Bitmap(bm.Width, bm.Height, PixelFormat.Format24bppRgb))
                                    {
                                        using (Graphics g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImage(bm, 0, 0, bm.Width, bm.Height); }
                                        using (EncoderParameters ep = new EncoderParameters(1))
                                        {
                                            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                                            flat.Save(outMs, codec, ep);
                                        }
                                    }
                                    p.Jpeg = true;
                                    p.Data = outMs.ToArray();
                                    return p;
                                }
                            }
                            bm.Save(outMs, ImageFormat.Png);
                            p.Data = outMs.ToArray();
                        }
                    }
                    return p;
                }
            }
            catch (Exception ex) { problem = "could not read the image " + name + ": " + ex.Message; return null; }
        }

        static byte[] StripJpeg(byte[] d)
        {
            try
            {
                using (MemoryStream o = new MemoryStream(d.Length))
                {
                    o.WriteByte(0xFF);
                    o.WriteByte(0xD8);
                    int i = 2;
                    while (i + 4 <= d.Length)
                    {
                        if (d[i] != 0xFF) return d;
                        byte m = d[i + 1];
                        if (m == 0xFF) { i++; continue; }
                        if (m == 0xDA)
                        {
                            o.Write(d, i, d.Length - i);
                            return o.ToArray();
                        }
                        if (m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { o.WriteByte(0xFF); o.WriteByte(m); i += 2; continue; }
                        int len = (d[i + 2] << 8) | d[i + 3];
                        if (len < 2 || i + 2 + len > d.Length) return d;
                        bool drop = m == 0xFE || (m >= 0xE1 && m <= 0xEF && m != 0xE2 && m != 0xEE);
                        if (!drop) o.Write(d, i, 2 + len);
                        i += 2 + len;
                    }
                    return d;
                }
            }
            catch (Exception) { return d; }
        }

        static bool IsPlainJpeg(byte[] d)
        {
            if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return false;
            int i = 2;
            while (i + 4 <= d.Length)
            {
                if (d[i] != 0xFF) return false;
                byte m = d[i + 1];
                if (m == 0xD8 || (m >= 0xD0 && m <= 0xD7) || m == 0x01) { i += 2; continue; }
                int len = (d[i + 2] << 8) | d[i + 3];
                if (len < 2) return false;
                if (m == 0xC0 || m == 0xC1 || m == 0xC2)
                {
                    if (i + 9 >= d.Length) return false;
                    int comps = d[i + 9];
                    return comps == 1 || comps == 3;
                }
                if (m == 0xDA || m == 0xD9) return false;
                i += 2 + len;
            }
            return false;
        }
    }

    static class DocXml
    {
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = DocModel.Clean(s);
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                string r = c == '&' ? "&amp;" : c == '<' ? "&lt;" : c == '>' ? "&gt;" : c == '"' ? "&quot;" : null;
                if (r == null) { if (sb != null) sb.Append(c); continue; }
                if (sb == null) { sb = new StringBuilder(s.Length + 16); sb.Append(s, 0, i); }
                sb.Append(r);
            }
            return sb == null ? s : sb.ToString();
        }

        public static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string Stamp(DateTime utc)
        {
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        public static string Core(string title, string author, DateTime utc)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
            sb.Append("<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
            if (!string.IsNullOrEmpty(title)) sb.Append("<dc:title>").Append(Esc(title)).Append("</dc:title>");
            if (!string.IsNullOrEmpty(author)) sb.Append("<dc:creator>").Append(Esc(author)).Append("</dc:creator><cp:lastModifiedBy>").Append(Esc(author)).Append("</cp:lastModifiedBy>");
            sb.Append("<dcterms:created xsi:type=\"dcterms:W3CDTF\">").Append(Stamp(utc)).Append("</dcterms:created>");
            sb.Append("<dcterms:modified xsi:type=\"dcterms:W3CDTF\">").Append(Stamp(utc)).Append("</dcterms:modified>");
            sb.Append("</cp:coreProperties>");
            return sb.ToString();
        }

        public static string Author()
        {
            try { return Environment.UserName ?? ""; } catch { return ""; }
        }
    }

    class DocPackage
    {
        readonly List<KeyValuePair<string, byte[]>> parts = new List<KeyValuePair<string, byte[]>>();

        public void Add(string name, string xml) { parts.Add(new KeyValuePair<string, byte[]>(name, new UTF8Encoding(false).GetBytes(xml))); }
        public void Add(string name, byte[] data) { parts.Add(new KeyValuePair<string, byte[]>(name, data)); }

        public byte[] ToBytes()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (KeyValuePair<string, byte[]> p in parts)
                    {
                        bool packed = p.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || p.Key.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
                        ZipArchiveEntry e = zip.CreateEntry(p.Key, packed ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                        e.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using (Stream s = e.Open()) s.Write(p.Value, 0, p.Value.Length);
                    }
                }
                return ms.ToArray();
            }
        }
    }

    static class DocPage
    {
        static readonly string[] LetterRegions = { "US", "CA", "MX", "PH", "CL", "CO", "CR", "DO", "GT", "NI", "PA", "PR", "SV", "VE", "PE", "BZ" };

        public static bool Letter
        {
            get
            {
                try { return Array.IndexOf(LetterRegions, RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant()) >= 0; }
                catch { return false; }
            }
        }

        public static double WidthPt { get { return Letter ? 612 : 595.28; } }
        public static double HeightPt { get { return Letter ? 792 : 841.89; } }
    }

    static class DocBidi
    {
        public static bool Rtl(char c)
        {
            return (c >= '\u0590' && c <= '\u08FF') || (c >= '\uFB1D' && c <= '\uFDFF') || (c >= '\uFE70' && c <= '\uFEFE');
        }

        static bool Strong(char c)
        {
            return char.IsLetter(c) || Rtl(c);
        }

        public static bool RtlText(string text)
        {
            if (text == null) return false;
            foreach (char c in text)
            {
                if (Rtl(c)) return true;
                if (char.IsLetter(c)) return false;
            }
            return false;
        }

        public static bool RtlSpans(List<DocSpan> spans)
        {
            if (spans == null) return false;
            foreach (DocSpan s in spans)
            {
                if (s.Break) continue;
                foreach (char c in s.Text)
                {
                    if (Rtl(c)) return true;
                    if (char.IsLetter(c)) return false;
                }
            }
            return false;
        }

        public static bool Any(string text)
        {
            if (text == null) return false;
            foreach (char c in text) if (Rtl(c)) return true;
            return false;
        }

        public static List<KeyValuePair<string, bool>> Split(string text, bool paraRtl)
        {
            List<KeyValuePair<string, bool>> parts = new List<KeyValuePair<string, bool>>();
            if (string.IsNullOrEmpty(text)) return parts;
            if (!Any(text)) { parts.Add(new KeyValuePair<string, bool>(text, false)); return parts; }
            StringBuilder cur = new StringBuilder();
            bool rtl = RtlText(text);
            foreach (char c in text)
            {
                if (Strong(c))
                {
                    bool r = Rtl(c);
                    if (r != rtl && cur.Length > 0)
                    {
                        string t = cur.ToString();
                        int keep = t.Length;
                        if (rtl != paraRtl) while (keep > 0 && !Strong(t[keep - 1]) && !char.IsDigit(t[keep - 1])) keep--;
                        parts.Add(new KeyValuePair<string, bool>(t.Substring(0, keep), rtl));
                        cur.Length = 0;
                        cur.Append(t.Substring(keep));
                    }
                    rtl = r;
                }
                cur.Append(c);
            }
            if (cur.Length > 0) parts.Add(new KeyValuePair<string, bool>(cur.ToString(), rtl));
            List<KeyValuePair<string, bool>> clean = new List<KeyValuePair<string, bool>>();
            foreach (KeyValuePair<string, bool> p in parts) if (p.Key.Length > 0) clean.Add(p);
            return clean;
        }
    }

    static class DocMath
    {
        public static string ToText(string tex)
        {
            if (string.IsNullOrEmpty(tex)) return "";
            try
            {
                string tag;
                bool overflow;
                MNode root = TexParser.Parse(tex, true, out tag, out overflow);
                if (root == null || overflow) return tex.Trim();
                string s = TexLinear.Of(root);
                if (!string.IsNullOrEmpty(tag)) s = s + "    " + tag;
                return s.Trim().Length > 0 ? s : tex.Trim();
            }
            catch (Exception) { return tex.Trim(); }
        }
    }

    class DocFormula
    {
        public List<DocPicture> Lines = new List<DocPicture>();
        public string Tag = "";
        public string Text = "";
    }

    class DocFormulas : IDisposable
    {
        readonly double screen, dpi;
        readonly float size;
        ChatFonts cf;
        Bitmap probe;
        bool broken;

        public DocFormulas(double pt)
        {
            double sc = 96;
            try { using (Graphics sg = Graphics.FromHwnd(IntPtr.Zero)) if (sg.DpiY >= 72 && sg.DpiY <= 480) sc = sg.DpiY; }
            catch (Exception) { sc = 96; }
            screen = sc;
            int scale = Math.Max(2, Math.Min(5, (int)Math.Round(384 / screen)));
            dpi = screen * scale;
            size = (float)(pt * scale);
        }

        public DocFormula Render(string tex, double maxWidthPt, Color ink)
        {
            if (broken || string.IsNullOrEmpty(tex) || tex.Length > MathFonts.MaxTex) return null;
            try
            {
                if (cf == null)
                {
                    cf = new ChatFonts(size, "Consolas");
                    probe = new Bitmap(4, 4);
                    probe.SetResolution((float)screen, (float)screen);
                }
                MathResult res;
                int avail = (int)(maxWidthPt / 72.0 * dpi);
                using (Graphics g = Graphics.FromImage(probe))
                {
                    res = cf.Maths.Display(g, tex, size);
                    if (res == null) return null;
                    double widest = MdRenderer.LineWidth(res.Pieces);
                    if (widest > avail)
                    {
                        MathResult small = cf.Maths.Display(g, tex, (float)(size * Math.Max(0.7, avail / widest * 0.98)));
                        if (small != null) res = small;
                    }
                }
                DocFormula f = new DocFormula();
                f.Tag = res.Tag ?? "";
                StringBuilder all = new StringBuilder();
                foreach (List<MathPiece> line in MdRenderer.Pack(res.Pieces, Math.Max(16, avail), (int)(dpi / 4)))
                {
                    MBox box = new MBox();
                    double x = 0;
                    for (int k = 0; k < line.Count; k++)
                    {
                        MathPiece pc = line[k];
                        box.Put(pc.Box, x, 0);
                        box.H = Math.Max(box.H, pc.Box.H);
                        box.D = Math.Max(box.D, pc.Box.D);
                        x += pc.Box.W;
                        all.Append(pc.Text);
                        if (k < line.Count - 1) x += pc.Trail;
                    }
                    box.W = x;
                    DocPicture p = Paint(box, cf.Maths, screen, dpi, ink);
                    if (p == null) return null;
                    p.Alt = tex.Trim();
                    f.Lines.Add(p);
                }
                if (f.Lines.Count == 0) return null;
                f.Text = TexLinear.Tidy(all.ToString());
                return f;
            }
            catch (Exception) { broken = true; return null; }
        }

        public void Dispose()
        {
            if (cf != null) { cf.Dispose(); cf = null; }
            if (probe != null) { probe.Dispose(); probe = null; }
        }

        static DocPicture Paint(MBox box, MathFonts fonts, double screen, double dpi, Color ink)
        {
            int pad = (int)Math.Ceiling(dpi / 48);
            int w = Math.Max(1, (int)Math.Ceiling(box.W)) + 2 * pad, h = Math.Max(1, (int)Math.Ceiling(box.H)), d = Math.Max(0, (int)Math.Ceiling(box.D));
            int full = h + d + 2 * pad;
            if (w > 12000 || full > 12000) return null;
            using (Bitmap bm = new Bitmap(w, full, PixelFormat.Format32bppRgb))
            {
                bm.SetResolution((float)screen, (float)screen);
                using (Graphics g = Graphics.FromImage(bm))
                {
                    g.Clear(Color.White);
                    MathPaint.Draw(g, box, pad, pad + h, Color.Black, fonts);
                }
                using (Bitmap outBm = new Bitmap(w, full, PixelFormat.Format32bppArgb))
                {
                    Rectangle r = new Rectangle(0, 0, w, full);
                    BitmapData src = bm.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    BitmapData dst = outBm.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        int[] row = new int[w];
                        int rgb = ink.ToArgb() & 0xFFFFFF;
                        bool any = false;
                        for (int y = 0; y < full; y++)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(new IntPtr(src.Scan0.ToInt64() + (long)y * src.Stride), row, 0, w);
                            for (int x = 0; x < w; x++)
                            {
                                int px = row[x];
                                int lum = (((px >> 16) & 255) * 299 + ((px >> 8) & 255) * 587 + (px & 255) * 114) / 1000;
                                int a = 255 - lum;
                                if (a > 0) any = true;
                                row[x] = (a << 24) | rgb;
                            }
                            System.Runtime.InteropServices.Marshal.Copy(row, 0, new IntPtr(dst.Scan0.ToInt64() + (long)y * dst.Stride), w);
                        }
                        if (!any) return null;
                    }
                    finally
                    {
                        bm.UnlockBits(src);
                        outBm.UnlockBits(dst);
                    }
                    outBm.SetResolution((float)dpi, (float)dpi);
                    DocPicture p = new DocPicture();
                    using (MemoryStream ms = new MemoryStream())
                    {
                        outBm.Save(ms, ImageFormat.Png);
                        p.Data = ms.ToArray();
                    }
                    p.Width = w;
                    p.Height = full;
                    p.DpiX = dpi;
                    p.DpiY = dpi;
                    p.Depth = d + pad;
                    p.Name = "Equation";
                    p.Tint = ink.ToArgb() & 0xFFFFFF;
                    return p;
                }
            }
        }
    }
}
