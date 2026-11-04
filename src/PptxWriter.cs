using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGent
{
    static class PptxWriter
    {
        const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string P = "http://schemas.openxmlformats.org/presentationml/2006/main";
        const double SlideW = 960, SlideH = 540, Left = 54, ContentW = 852, ContentTop = 128, ContentBottom = 498;
        const string Ink = "1F2328", Body = "2D333B", Muted = "656D76", Accent = "2B59C3", Line = "D0D7DE", Soft = "F6F8FA", Head = "EEF1F5", LinkBlue = "0969DA";
        const string TitleFont = "Segoe UI Semibold", BodyFont = "Segoe UI", CodeFont = "Consolas";

        static long E(double pt) { return (long)Math.Round(pt * 12700); }

        static readonly Regex NotesLine = new Regex(@"^\s*(\*\*|__)?\s*(speaker\s+notes?|notes?)\s*:\s*(\*\*|__)?\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Comment = new Regex(@"<!--(.*?)-->", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        static readonly Regex Aside = new Regex(@"<aside\b[^>]*class\s*=\s*[""']?notes[""']?[^>]*>(.*?)</aside>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Directive = new Regex(@"^\s*_?(class|paginate|header|footer|theme|backgroundColor|backgroundImage|color|size|marp|style|headingDivider|math|transition)\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        class SlideSrc
        {
            public string Text = "";
            public string Notes = "";
        }

        class Item
        {
            public int Kind;
            public List<DocBlock> Blocks = new List<DocBlock>();
            public DocPicture Picture;
            public List<DocPicture> Lines;
            public string Tex, Fallback, Tag;
            public DocBlock Source;
            public double H, MaxH;

            public Item Copy()
            {
                return new Item { Kind = Kind, Blocks = new List<DocBlock>(Blocks), Picture = Picture, Lines = Lines, Tex = Tex, Fallback = Fallback, Tag = Tag, Source = Source, MaxH = MaxH };
            }
        }

        const int KText = 0, KQuote = 1, KCode = 2, KTable = 3, KPicture = 4, KMath = 5;

        class SlideOut
        {
            public List<DocSpan> Title;
            public List<DocSpan> Subtitle;
            public int Layout;
            public List<Item> Items = new List<Item>();
            public string Notes = "";
            public double Scale = 1;
            public bool Continued;
            public bool TwoColumn;
        }

        const int LayoutTitle = 0, LayoutContent = 1, LayoutSection = 2, LayoutQuote = 3;

        public static byte[] Build(string markdown, DocPictureLoader loader, List<string> problems, out int slideCount, out string title)
        {
            List<SlideSrc> srcs = Split(markdown ?? "");
            List<SlideOut> slides = new List<SlideOut>();
            title = "";
            using (Meter meter = new Meter())
            using (DocFormulas formulas = new DocFormulas(32))
            {
                for (int si = 0; si < srcs.Count; si++)
                {
                    DocModel m = DocModel.FromMarkdown(srcs[si].Text, loader);
                    foreach (string pr in m.Problems) if (problems != null && !problems.Contains(pr)) problems.Add(pr);
                    SlideOut s = new SlideOut();
                    s.Notes = srcs[si].Notes;
                    int start = 0;
                    if (m.Blocks.Count > 0 && m.Blocks[0].Kind == DocKind.Heading) { s.Title = m.Blocks[0].Spans; start = 1; }
                    List<DocBlock> rest = new List<DocBlock>();
                    for (int i = start; i < m.Blocks.Count; i++)
                        if (m.Blocks[i].Kind != DocKind.PageBreak && m.Blocks[i].Kind != DocKind.Rule) rest.Add(m.Blocks[i]);
                    if (s.Title == null && rest.Count == 0) continue;
                    if (title.Length == 0 && s.Title != null) title = Plain(s.Title);
                    bool onlyText = true;
                    int chars = 0;
                    foreach (DocBlock b in rest)
                    {
                        if (b.Kind != DocKind.Para) onlyText = false;
                        chars += b.Plain.Length;
                    }
                    if (slides.Count == 0 && s.Title != null && onlyText && rest.Count <= 3 && chars <= 320)
                    {
                        s.Layout = LayoutTitle;
                        if (rest.Count > 0)
                        {
                            s.Subtitle = new List<DocSpan>();
                            for (int i = 0; i < rest.Count; i++)
                            {
                                if (i > 0) s.Subtitle.Add(new DocSpan { Break = true });
                                s.Subtitle.AddRange(rest[i].Spans);
                            }
                        }
                        slides.Add(s);
                        continue;
                    }
                    if (s.Title != null && rest.Count == 0) { s.Layout = LayoutSection; slides.Add(s); continue; }
                    if (s.Title == null && rest.Count > 0 && rest.Count <= 4 && rest[0].Kind == DocKind.Quote)
                    {
                        bool quoteOnly = true;
                        int qchars = 0;
                        foreach (DocBlock b in rest)
                        {
                            if (b.Kind != DocKind.Quote && b.Kind != DocKind.Para) quoteOnly = false;
                            qchars += b.Plain.Length;
                        }
                        if (quoteOnly && qchars <= 400)
                        {
                            s.Layout = LayoutQuote;
                            Item q = new Item { Kind = KQuote };
                            Item who = new Item { Kind = KText };
                            foreach (DocBlock b in rest) (b.Kind == DocKind.Quote ? q : who).Blocks.Add(b);
                            s.Items.Add(q);
                            if (who.Blocks.Count > 0) s.Items.Add(who);
                            slides.Add(s);
                            continue;
                        }
                    }
                    s.Layout = LayoutContent;
                    List<Item> items = Items(rest, formulas, meter, problems);
                    Place(s, items, slides, meter, false);
                }
            }
            if (slides.Count == 0)
            {
                SlideOut empty = new SlideOut();
                empty.Layout = LayoutTitle;
                empty.Title = new List<DocSpan>();
                slides.Add(empty);
            }
            slideCount = slides.Count;
            return Package(slides, title);
        }

        static List<SlideSrc> Split(string md)
        {
            string text = md.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = text.Split('\n');
            int first = 0;
            while (first < lines.Length && lines[first].Trim().Length == 0) first++;
            if (first < lines.Length && lines[first].Trim() == "---")
            {
                for (int k = first + 1; k < lines.Length && k < first + 60; k++)
                {
                    string t = lines[k].Trim();
                    if (t == "---" || t == "...")
                    {
                        bool yaml = true;
                        for (int q = first + 1; q < k; q++) if (lines[q].Trim().Length > 0 && !Regex.IsMatch(lines[q], @"^\s*[A-Za-z_][\w-]*\s*:|^\s+\S|^\s*-\s")) { yaml = false; break; }
                        if (yaml && k > first + 1) first = k + 1;
                        break;
                    }
                }
            }
            List<List<string>> chunks = new List<List<string>>();
            List<string> cur = new List<string>();
            string fence = null;
            bool explicitBreaks = false;
            for (int i = first; i < lines.Length; i++)
            {
                string l = lines[i], t = l.Trim();
                if (fence != null)
                {
                    if (t.StartsWith(fence) && t.Trim(fence[0]).Length == 0) fence = null;
                    cur.Add(l);
                    continue;
                }
                if (t.StartsWith("```") || t.StartsWith("~~~"))
                {
                    int n = 0;
                    while (n < t.Length && t[n] == t[0]) n++;
                    fence = t.Substring(0, n);
                    cur.Add(l);
                    continue;
                }
                bool blankBefore = cur.Count == 0 || cur[cur.Count - 1].Trim().Length == 0;
                if ((t == "---" || t == "***" || t == "___" || Regex.IsMatch(t, @"^-{4,}$")) && blankBefore)
                {
                    explicitBreaks = true;
                    chunks.Add(cur);
                    cur = new List<string>();
                    continue;
                }
                cur.Add(l);
            }
            chunks.Add(cur);
            if (!explicitBreaks)
            {
                List<List<string>> byHeading = new List<List<string>>();
                List<string> c2 = new List<string>();
                fence = null;
                foreach (string l in chunks[0])
                {
                    string t = l.Trim();
                    if (fence != null)
                    {
                        if (t.StartsWith(fence) && t.Trim(fence[0]).Length == 0) fence = null;
                        c2.Add(l);
                        continue;
                    }
                    if (t.StartsWith("```") || t.StartsWith("~~~"))
                    {
                        int n = 0;
                        while (n < t.Length && t[n] == t[0]) n++;
                        fence = t.Substring(0, n);
                        c2.Add(l);
                        continue;
                    }
                    if (Regex.IsMatch(l, @"^#{1,2}\s") && HasContent(c2)) { byHeading.Add(c2); c2 = new List<string>(); }
                    c2.Add(l);
                }
                byHeading.Add(c2);
                chunks = byHeading;
            }
            List<SlideSrc> list = new List<SlideSrc>();
            foreach (List<string> ch in chunks)
            {
                if (!HasContent(ch)) continue;
                list.Add(Notes(string.Join("\n", ch.ToArray())));
            }
            return list;
        }

        static bool HasContent(List<string> lines)
        {
            foreach (string l in lines) if (l.Trim().Length > 0) return true;
            return false;
        }

        static SlideSrc Notes(string chunk)
        {
            SlideSrc s = new SlideSrc();
            StringBuilder notes = new StringBuilder();
            chunk = Aside.Replace(chunk, delegate(Match m) { notes.Append(m.Groups[1].Value.Trim()).Append('\n'); return ""; });
            string[] lines = chunk.Split('\n');
            string fence = null;
            bool inNotes = false;
            StringBuilder plain = new StringBuilder();
            foreach (string l in lines)
            {
                string t = l.Trim();
                if (inNotes) { notes.Append(l).Append('\n'); continue; }
                if (fence != null)
                {
                    if (t.StartsWith(fence) && t.Trim(fence[0]).Length == 0) fence = null;
                    plain.Append(l).Append('\n');
                    continue;
                }
                if (t.StartsWith("```") || t.StartsWith("~~~"))
                {
                    int n = 0;
                    while (n < t.Length && t[n] == t[0]) n++;
                    fence = t.Substring(0, n);
                    plain.Append(l).Append('\n');
                    continue;
                }
                Match nm = NotesLine.Match(l);
                if (nm.Success && !l.TrimStart().StartsWith("#"))
                {
                    inNotes = true;
                    if (nm.Groups[4].Value.Trim().Length > 0) notes.Append(nm.Groups[4].Value.Trim()).Append('\n');
                    continue;
                }
                plain.Append(l).Append('\n');
            }
            string rest = Comment.Replace(plain.ToString(), delegate(Match m)
            {
                string c = m.Groups[1].Value.Trim();
                if (c.Length == 0 || Directive.IsMatch(c) || Regex.IsMatch(c, @"^\s*(page\s*-?\s*break|pagebreak)\s*$", RegexOptions.IgnoreCase)) return "";
                if (Regex.IsMatch(c, @"^\s*notes?\s*:", RegexOptions.IgnoreCase)) c = Regex.Replace(c, @"^\s*notes?\s*:\s*", "", RegexOptions.IgnoreCase);
                notes.Append(c).Append('\n');
                return "";
            });
            s.Text = rest;
            s.Notes = DocModel.Clean(Regex.Replace(notes.ToString().Replace("\r", ""), @"\n{3,}", "\n\n")).Trim();
            return s;
        }

        static List<Item> Items(List<DocBlock> blocks, DocFormulas formulas, Meter meter, List<string> problems)
        {
            List<Item> items = new List<Item>();
            Item text = null;
            foreach (DocBlock b in blocks)
            {
                switch (b.Kind)
                {
                    case DocKind.Para:
                    case DocKind.Item:
                    case DocKind.Heading:
                        if (text == null || text.Kind != KText) { text = new Item { Kind = KText }; items.Add(text); }
                        text.Blocks.Add(b);
                        break;
                    case DocKind.Quote:
                        if (text == null || text.Kind != KQuote) { text = new Item { Kind = KQuote }; items.Add(text); }
                        text.Blocks.Add(b);
                        break;
                    case DocKind.Code:
                        text = null;
                        items.Add(new Item { Kind = KCode, Blocks = new List<DocBlock>(new DocBlock[] { b }) });
                        break;
                    case DocKind.Table:
                        text = null;
                        items.Add(new Item { Kind = KTable, Blocks = new List<DocBlock>(new DocBlock[] { b }) });
                        break;
                    case DocKind.Picture:
                        text = null;
                        items.Add(new Item { Kind = KPicture, Picture = b.Picture });
                        break;
                    case DocKind.Math:
                        {
                            text = null;
                            Item mi = new Item { Kind = KMath, Tex = b.Tex, Fallback = b.Code };
                            Color ink = Color.FromArgb(0x1F, 0x23, 0x28);
                            DocFormula f = formulas.Render(b.Tex, ContentW * 0.94, ink);
                            if (f != null && f.Tag.Length > 0)
                            {
                                double tagRoom = meter.Width(f.Tag, BodyFont, BodyPt(1.0), false) + 24;
                                double widest = 0;
                                foreach (DocPicture p in f.Lines) widest = Math.Max(widest, p.Width / p.DpiX * 72);
                                if (widest + 2 * tagRoom > ContentW)
                                {
                                    DocFormula narrow = formulas.Render(b.Tex, ContentW - 2 * tagRoom, ink);
                                    if (narrow != null) f = narrow;
                                }
                            }
                            if (f != null && f.Lines.Count <= 8) { mi.Lines = f.Lines; mi.Tag = f.Tag; }
                            items.Add(mi);
                            break;
                        }
                }
            }
            return items;
        }

        static double Avail(SlideOut s) { return ContentBottom - (s.Title != null ? ContentTop : 60); }

        static bool IsHeading(Item it)
        {
            return it.Kind == KText && it.Blocks.Count == 1 && it.Blocks[0].Kind == DocKind.Heading;
        }

        static void Promote(SlideOut s, List<Item> items)
        {
            if (items.Count == 0 || items[0].Kind != KText || items[0].Blocks.Count == 0) return;
            DocBlock h = items[0].Blocks[0];
            if (h.Kind != DocKind.Heading || h.Plain.Trim().Length == 0) return;
            if (s.Title != null && !(s.Continued && h.Level <= 2)) return;
            s.Title = h.Spans;
            s.Continued = false;
            Item first = items[0].Copy();
            first.Blocks.RemoveAt(0);
            if (first.Blocks.Count == 0) items.RemoveAt(0);
            else items[0] = first;
        }

        static void Place(SlideOut s, List<Item> items, List<SlideOut> slides, Meter meter, bool more)
        {
            Promote(s, items);
            if (items.Count == 0)
            {
                if (s.Title == null) return;
                s.Layout = LayoutSection;
                slides.Add(s);
                return;
            }
            int pics = 0, texts = 0, others = 0;
            foreach (Item it in items)
            {
                if (it.Kind == KPicture) pics++;
                else if (it.Kind == KText || it.Kind == KQuote) texts++;
                else others++;
            }
            bool two = pics == 1 && texts >= 1 && others == 0;
            double avail = Avail(s);
            foreach (Item it in items) if (it.Kind == KTable) SplitTallRows(it, avail, meter);
            bool dense = items.Count == 1 && (items[0].Kind == KTable || items[0].Kind == KCode);
            double[] scales = more ? new double[] { 1.0 } : dense ? new double[] { 1.0, 0.92, 0.85, 0.78 } : new double[] { 1.0, 0.92, 0.85 };
            foreach (double sc in scales)
            {
                if (Height(items, sc, two, meter) <= avail)
                {
                    s.Items = items;
                    s.Scale = sc;
                    s.TwoColumn = two;
                    slides.Add(s);
                    return;
                }
            }
            if (items.Count == 1 && (items[0].Kind == KPicture || items[0].Kind == KMath))
            {
                s.Items = items;
                s.Scale = 0.72;
                foreach (double sc in new double[] { 0.78, 0.72 }) if (Height(items, sc, false, meter) <= avail) { s.Scale = sc; break; }
                s.TwoColumn = false;
                slides.Add(s);
                return;
            }
            List<Item> flat = new List<Item>();
            foreach (Item it in items)
            {
                if ((it.Kind == KText || it.Kind == KQuote) && it.Blocks.Count > 1)
                    foreach (DocBlock b in it.Blocks) flat.Add(new Item { Kind = it.Kind, Blocks = new List<DocBlock>(new DocBlock[] { b }) });
                else flat.Add(it);
            }
            List<Item> page = new List<Item>();
            int idx = 0;
            while (idx < flat.Count)
            {
                Item next = flat[idx];
                List<Item> trial = new List<Item>(page);
                trial.Add(next);
                if (Height(Merge(trial), 1.0, false, meter) <= avail) { page.Add(next); idx++; continue; }
                if (page.Count > 0)
                {
                    int cut = HeadingBreak(page, avail, meter);
                    if (cut > 0)
                    {
                        idx -= page.Count - cut;
                        page.RemoveRange(cut, page.Count - cut);
                        break;
                    }
                }
                double room = page.Count == 0 ? avail : avail - Height(Merge(page), 1.0, false, meter) - Gap(1.0);
                Item head, tail;
                if (Split(next, room, page.Count > 0, avail, meter, out head, out tail))
                {
                    page.Add(head);
                    flat[idx] = tail;
                }
                else if (page.Count == 0 || (page.Count == 1 && IsHeading(page[0])) || (next.Kind == KPicture && room >= avail * 0.45))
                {
                    if (next.Kind == KPicture) next.MaxH = Math.Max(40, room);
                    page.Add(next);
                    idx++;
                }
                break;
            }
            if (idx < flat.Count)
                while (page.Count > 1 && idx > 0 && page[page.Count - 1] == flat[idx - 1] && IsHeading(page[page.Count - 1]))
                {
                    page.RemoveAt(page.Count - 1);
                    idx--;
                }
            List<Item> merged = Merge(page);
            double best = 0.72;
            foreach (double sc in new double[] { 1.0, 0.92, 0.85, 0.78, 0.72 }) if (Height(merged, sc, false, meter) <= avail) { best = sc; break; }
            s.Items = merged;
            s.Scale = best;
            s.TwoColumn = false;
            slides.Add(s);
            if (idx < flat.Count)
            {
                SlideOut after = new SlideOut();
                after.Layout = LayoutContent;
                after.Title = s.Title;
                after.Continued = s.Title != null;
                List<Item> left = new List<Item>();
                for (int i = idx; i < flat.Count; i++) left.Add(flat[i]);
                Place(after, Merge(left), slides, meter, true);
            }
        }

        static int HeadingBreak(List<Item> page, double avail, Meter meter)
        {
            for (int j = page.Count - 1; j >= 1; j--)
            {
                if (!IsHeading(page[j])) continue;
                int k = j;
                while (k > 0 && IsHeading(page[k - 1])) k--;
                if (k == 0) return -1;
                return Height(Merge(page.GetRange(0, k)), 1.0, false, meter) >= avail * 0.4 ? k : -1;
            }
            return -1;
        }

        static bool Split(Item it, double room, bool hasContent, double avail, Meter meter, out Item head, out Item tail)
        {
            head = tail = null;
            if (room < 40) return false;
            if (hasContent && (room < avail * 0.3 || ItemHeight(it, 1.0, ContentW, meter) <= avail)) return false;
            switch (it.Kind)
            {
                case KTable: return SplitTable(it, room, hasContent ? 2 : 1, meter, out head, out tail);
                case KCode: return SplitCode(it, room, hasContent ? 3 : 1, meter, out head, out tail);
                case KText:
                case KQuote:
                    if (it.Blocks.Count != 1 || it.Blocks[0].Kind == DocKind.Heading || it.Blocks[0].Kind == DocKind.Item) return false;
                    return SplitText(it, room, meter, out head, out tail);
            }
            return false;
        }

        static DocBlock Clone(DocBlock b)
        {
            return new DocBlock { Kind = b.Kind, Level = b.Level, Ordered = b.Ordered, Number = b.Number, ListId = b.ListId, Run = b.Run, Task = b.Task, Spans = b.Spans, Code = b.Code, Lang = b.Lang, Tex = b.Tex, Rows = b.Rows, Align = b.Align, Picture = b.Picture };
        }

        static void SplitTallRows(Item it, double avail, Meter meter)
        {
            DocBlock t = it.Blocks[0];
            if (t.Rows == null || t.Rows.Count < 2) return;
            double pt = TablePt(1.0);
            double[] cols = TableColumns(it.Source ?? t, pt, meter);
            List<List<List<DocSpan>>> rows = new List<List<List<DocSpan>>>();
            rows.Add(t.Rows[0]);
            bool changed = false;
            for (int r = 1; r < t.Rows.Count; r++)
            {
                List<List<List<DocSpan>>> pending = new List<List<List<DocSpan>>> { t.Rows[r] };
                for (int round = 0; round < 4 && pending.Count > 0; round++)
                {
                    List<List<List<DocSpan>>> next = new List<List<List<DocSpan>>>();
                    foreach (List<List<DocSpan>> row in pending)
                    {
                        double h = RowHeight(row, cols, pt, meter);
                        if (h <= avail * 0.85 || round == 3) { rows.Add(row); continue; }
                        int parts = Math.Max(2, (int)Math.Ceiling(h / (avail * 0.6)));
                        List<List<DocSpan>>[] pieces = new List<List<DocSpan>>[parts];
                        for (int p = 0; p < parts; p++) pieces[p] = new List<List<DocSpan>>();
                        for (int c = 0; c < row.Count; c++)
                        {
                            List<List<DocSpan>> split = SplitSpans(row[c], parts);
                            for (int p = 0; p < parts; p++) pieces[p].Add(split[p]);
                        }
                        next.AddRange(pieces);
                        changed = true;
                    }
                    pending = next;
                }
            }
            if (!changed) return;
            DocBlock nb = Clone(t);
            nb.Rows = rows;
            if (it.Source == null) it.Source = t;
            it.Blocks[0] = nb;
        }

        static List<List<DocSpan>> SplitSpans(List<DocSpan> cell, int parts)
        {
            List<List<DocSpan>> res = new List<List<DocSpan>>();
            for (int p = 0; p < parts; p++) res.Add(new List<DocSpan>());
            int total = 0;
            foreach (DocSpan s in cell) total += Math.Max(1, (s.Text ?? "").Length);
            double each = Math.Max(1, total / (double)parts);
            int done = 0, at = 0;
            foreach (DocSpan s in cell)
            {
                string text = s.Text ?? "";
                if (s.Break || text.Length == 0)
                {
                    if (res[at].Count > 0) res[at].Add(s);
                    done += Math.Max(1, text.Length);
                    continue;
                }
                int i = 0;
                while (i < text.Length)
                {
                    int j = text.IndexOf(' ', i);
                    j = j < 0 ? text.Length : j + 1;
                    string word = text.Substring(i, j - i);
                    if (at < parts - 1 && done >= each * (at + 1) && res[at].Count > 0) at++;
                    DocSpan last = res[at].Count > 0 ? res[at][res[at].Count - 1] : null;
                    if (last != null && !last.Break && last.SameLook(s)) last.Text += word;
                    else
                    {
                        DocSpan piece = new DocSpan { Text = word, Bold = s.Bold, Italic = s.Italic, Code = s.Code, Strike = s.Strike, Link = s.Link };
                        res[at].Add(piece);
                    }
                    done += word.Length;
                    i = j;
                }
            }
            foreach (List<DocSpan> piece in res)
                if (piece.Count > 0) piece[piece.Count - 1].Text = piece[piece.Count - 1].Text.TrimEnd(' ');
            return res;
        }

        static bool SplitTable(Item it, double room, int minRows, Meter meter, out Item head, out Item tail)
        {
            head = tail = null;
            DocBlock t = it.Blocks[0];
            DocBlock src = it.Source ?? t;
            if (t.Rows == null || t.Rows.Count < 3) return false;
            double pt = TablePt(1.0);
            double[] cols = TableColumns(src, pt, meter);
            double h = RowHeight(t.Rows[0], cols, pt, meter);
            int n = 0;
            for (int r = 1; r < t.Rows.Count; r++)
            {
                h += RowHeight(t.Rows[r], cols, pt, meter);
                if (h > room) break;
                n++;
            }
            if (n < minRows || n >= t.Rows.Count - 1) return false;
            DocBlock a = Clone(t), b = Clone(t);
            a.Rows = new List<List<List<DocSpan>>>();
            a.Rows.Add(t.Rows[0]);
            a.Rows.AddRange(t.Rows.GetRange(1, n));
            b.Rows = new List<List<List<DocSpan>>>();
            b.Rows.Add(t.Rows[0]);
            b.Rows.AddRange(t.Rows.GetRange(n + 1, t.Rows.Count - n - 1));
            head = new Item { Kind = KTable, Blocks = new List<DocBlock>(new DocBlock[] { a }), Source = src };
            tail = new Item { Kind = KTable, Blocks = new List<DocBlock>(new DocBlock[] { b }), Source = src };
            return true;
        }

        static bool SplitCode(Item it, double room, int minLines, Meter meter, out Item head, out Item tail)
        {
            head = tail = null;
            DocBlock c = it.Blocks[0];
            string[] lines = (c.Code ?? "").Split('\n');
            if (lines.Length < 2) return false;
            double pt = CodePt(1.0), lh = meter.LineHeight(CodeFont, pt);
            double h = 20;
            int n = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                h += Math.Max(1, meter.Lines(lines[i].Length == 0 ? " " : lines[i], CodeFont, pt, false, ContentW - 24)) * lh;
                if (h > room) break;
                n++;
            }
            if (n < minLines || n >= lines.Length) return false;
            int skip = 0;
            for (int k = n; k >= Math.Max(minLines, (int)(n * 0.7)) && k > 0; k--)
                if (lines[k].Trim().Length == 0) { n = k; skip = 1; break; }
            int rest = n + skip;
            while (rest < lines.Length - 1 && lines[rest].Trim().Length == 0) rest++;
            if (rest >= lines.Length) return false;
            DocBlock a = Clone(c), b = Clone(c);
            a.Code = string.Join("\n", lines, 0, n).TrimEnd();
            b.Code = string.Join("\n", lines, rest, lines.Length - rest);
            if (a.Code.Length == 0 || b.Code.Trim().Length == 0) return false;
            head = new Item { Kind = KCode, Blocks = new List<DocBlock>(new DocBlock[] { a }) };
            tail = new Item { Kind = KCode, Blocks = new List<DocBlock>(new DocBlock[] { b }) };
            return true;
        }

        static bool SplitText(Item it, double room, Meter meter, out Item head, out Item tail)
        {
            head = tail = null;
            DocBlock b = it.Blocks[0];
            string plain = b.Plain;
            if (plain.Length < 60) return false;
            List<int> sentences = new List<int>(), words = new List<int>();
            for (int i = 1; i < plain.Length - 1; i++)
            {
                char p = plain[i - 1];
                if (p == '\n') sentences.Add(i);
                else if (p == ' ' && i >= 2 && ".!?;:)".IndexOf(plain[i - 2]) >= 0) sentences.Add(i);
                else if (p == '\u3002' || p == '\uFF01' || p == '\uFF1F' || p == '\u061F' || p == '\u06D4') sentences.Add(i);
                if (p == ' ' || p == '\n') words.Add(i);
            }
            foreach (List<int> cuts in new List<int>[] { sentences, words })
            {
                int lo = 0, hi = cuts.Count - 1, best = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (ItemHeight(TextPart(it, 0, cuts[mid]), 1.0, ContentW, meter) <= room) { best = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                if (best < 0) continue;
                head = TextPart(it, 0, cuts[best]);
                tail = TextPart(it, cuts[best], plain.Length);
                if (head.Blocks[0].Plain.Trim().Length == 0 || tail.Blocks[0].Plain.Trim().Length == 0) { head = tail = null; continue; }
                return true;
            }
            return false;
        }

        static Item TextPart(Item it, int from, int to)
        {
            DocBlock b = it.Blocks[0];
            List<DocSpan> spans = new List<DocSpan>();
            int at = 0;
            foreach (DocSpan s in b.Spans)
            {
                int len = s.Break ? 1 : s.Text.Length;
                int a = Math.Max(from, at), z = Math.Min(to, at + len);
                if (z > a)
                {
                    if (s.Break) spans.Add(s);
                    else spans.Add(new DocSpan { Text = s.Text.Substring(a - at, z - a), Bold = s.Bold, Italic = s.Italic, Code = s.Code, Strike = s.Strike, Link = s.Link });
                }
                at += len;
                if (at >= to) break;
            }
            while (spans.Count > 0 && (spans[0].Break || spans[0].Text.Trim().Length == 0)) spans.RemoveAt(0);
            while (spans.Count > 0 && (spans[spans.Count - 1].Break || spans[spans.Count - 1].Text.Trim().Length == 0)) spans.RemoveAt(spans.Count - 1);
            if (spans.Count > 0)
            {
                DocSpan f = spans[0], l = spans[spans.Count - 1];
                spans[0] = new DocSpan { Text = f.Text.TrimStart(), Bold = f.Bold, Italic = f.Italic, Code = f.Code, Strike = f.Strike, Link = f.Link };
                l = spans[spans.Count - 1];
                spans[spans.Count - 1] = new DocSpan { Text = l.Text.TrimEnd(), Bold = l.Bold, Italic = l.Italic, Code = l.Code, Strike = l.Strike, Link = l.Link };
            }
            DocBlock c = Clone(b);
            c.Spans = spans;
            return new Item { Kind = it.Kind, Blocks = new List<DocBlock>(new DocBlock[] { c }) };
        }

        static List<Item> Merge(List<Item> items)
        {
            List<Item> outList = new List<Item>();
            foreach (Item it in items)
            {
                Item last = outList.Count > 0 ? outList[outList.Count - 1] : null;
                if (last != null && (it.Kind == KText || it.Kind == KQuote) && last.Kind == it.Kind) { last.Blocks.AddRange(it.Blocks); continue; }
                outList.Add(it.Copy());
            }
            return outList;
        }

        static double Gap(double sc) { return 14 * sc; }

        static double Height(List<Item> items, double sc, bool two, Meter meter)
        {
            if (two)
            {
                double textH = 0, picH = 0;
                foreach (Item it in items)
                {
                    if (it.Kind == KPicture) picH = PictureSize(it.Picture, ContentW * 0.42, ContentBottom - ContentTop).Height;
                    else textH += ItemHeight(it, sc, ContentW * 0.54, meter) + Gap(sc);
                }
                return Math.Max(textH - Gap(sc), picH);
            }
            double h = 0;
            for (int i = 0; i < items.Count; i++)
            {
                double ih = ItemHeight(items[i], sc, ContentW, meter);
                items[i].H = ih;
                h += ih + (i > 0 ? Gap(sc) : 0);
            }
            return h;
        }

        static SizeF PictureSize(DocPicture p, double maxW, double maxH)
        {
            if (p == null) return SizeF.Empty;
            double w = p.Width / p.DpiX * 72, h = p.Height / p.DpiY * 72;
            double k = Math.Min(1.0, Math.Min(maxW / w, maxH / h));
            return new SizeF((float)(w * k), (float)(h * k));
        }

        static double BodyPt(double sc) { return Math.Round(26 * sc * 2) / 2; }
        static double CodePt(double sc) { return Math.Round(18 * sc * 2) / 2; }
        static double TablePt(double sc) { return Math.Round(20 * sc * 2) / 2; }

        static double ItemHeight(Item it, double sc, double width, Meter meter)
        {
            switch (it.Kind)
            {
                case KText:
                case KQuote:
                    {
                        double h = 0;
                        double indent = it.Kind == KQuote ? 18 : 0;
                        for (int i = 0; i < it.Blocks.Count; i++)
                        {
                            DocBlock b = it.Blocks[i];
                            double pt = ParaPt(b, sc);
                            double marL = b.Kind == DocKind.Item ? ItemMarL(b) : 0;
                            int lines = meter.Lines(Plain(b.Spans), BodyFont, pt, b.Kind == DocKind.Heading, width - marL - indent - 4);
                            h += lines * meter.LineHeight(BodyFont, pt) + (i > 0 ? SpaceBefore(b, sc) : 0);
                        }
                        return h + 8;
                    }
                case KCode:
                    {
                        double pt = CodePt(sc);
                        string code = it.Blocks[0].Code ?? "";
                        int lines = 0;
                        foreach (string l in code.Split('\n')) lines += Math.Max(1, meter.Lines(l.Length == 0 ? " " : l, CodeFont, pt, false, width - 24));
                        return lines * meter.LineHeight(CodeFont, pt) + 20;
                    }
                case KTable:
                    {
                        double pt = TablePt(sc);
                        double[] cols = TableColumns(it.Source ?? it.Blocks[0], pt, meter);
                        double h = 0;
                        foreach (List<List<DocSpan>> row in it.Blocks[0].Rows) h += RowHeight(row, cols, pt, meter);
                        return h;
                    }
                case KPicture:
                    return PictureSize(it.Picture, width, it.MaxH > 0 ? it.MaxH : ContentBottom - ContentTop).Height;
                case KMath:
                    if (it.Lines != null)
                    {
                        double k = Math.Min(1.0, sc), h = 0;
                        for (int i = 0; i < it.Lines.Count; i++) h += PictureSize(it.Lines[i], width, 100000).Height * k + (i > 0 ? 6 * k : 0);
                        return h + 8;
                    }
                    return meter.LineHeight(BodyFont, BodyPt(sc)) * Math.Max(1, meter.Lines(it.Fallback ?? "", BodyFont, BodyPt(sc), false, width)) + 8;
            }
            return 0;
        }

        static double ParaPt(DocBlock b, double sc)
        {
            double pt = BodyPt(sc);
            if (b.Kind == DocKind.Heading) return Math.Round(pt * 1.1 * 2) / 2;
            if (b.Kind == DocKind.Item && b.Level > 0) return Math.Round(pt * 0.88 * 2) / 2;
            return pt;
        }

        static double SpaceBefore(DocBlock b, double sc)
        {
            if (b.Kind == DocKind.Heading) return 14 * sc;
            if (b.Kind == DocKind.Item && b.Level > 0) return 3 * sc;
            return 9 * sc;
        }

        static double ItemMarL(DocBlock b) { return (b.Ordered && b.Task < 0 ? 36 : 26) + 30 * Math.Max(0, Math.Min(4, b.Level)); }

        static double ItemIndent(DocBlock b) { return b.Ordered && b.Task < 0 ? -36 : -24; }

        static double[] TableColumns(DocBlock t, double pt, Meter meter)
        {
            int cols = t.Rows[0].Count;
            double[] natural = new double[cols];
            foreach (List<List<DocSpan>> row in t.Rows)
                for (int c = 0; c < cols && c < row.Count; c++)
                {
                    double w = 0;
                    foreach (string piece in Plain(row[c]).Split('\n')) w = Math.Max(w, meter.Width(piece, BodyFont, pt, row == t.Rows[0]));
                    natural[c] = Math.Max(natural[c], Math.Min(w, 420) + 16);
                }
            double total = 0;
            for (int c = 0; c < cols; c++) { natural[c] = Math.Max(48, natural[c]); total += natural[c]; }
            double target = total >= ContentW * 0.35 ? ContentW : Math.Min(ContentW, Math.Max(ContentW * 0.5, total * 1.5));
            double[] w2 = new double[cols];
            for (int c = 0; c < cols; c++) w2[c] = natural[c] / total * target;
            return w2;
        }

        static double RowHeight(List<List<DocSpan>> row, double[] cols, double pt, Meter meter)
        {
            int lines = 1;
            for (int c = 0; c < cols.Length && c < row.Count; c++)
            {
                int n = 0;
                foreach (string piece in Plain(row[c]).Split('\n')) n += Math.Max(1, meter.Lines(piece.Length == 0 ? " " : piece, BodyFont, pt, false, cols[c] - 14));
                lines = Math.Max(lines, n);
            }
            return lines * meter.LineHeight(BodyFont, pt) + 10;
        }

        static string Plain(List<DocSpan> spans)
        {
            StringBuilder sb = new StringBuilder();
            if (spans == null) return "";
            foreach (DocSpan s in spans) sb.Append(s.Break ? "\n" : s.Text);
            return sb.ToString();
        }

        class Meter : IDisposable
        {
            readonly Bitmap bmp;
            readonly Graphics g;
            readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>(StringComparer.Ordinal);
            readonly StringFormat fmt;
            readonly double px = 96.0 / 72.0, py = 96.0 / 72.0;
            bool broken;

            public Meter()
            {
                try
                {
                    bmp = new Bitmap(8, 8);
                    bmp.SetResolution(96, 96);
                    g = Graphics.FromImage(bmp);
                    g.PageUnit = GraphicsUnit.Pixel;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    if (g.DpiX >= 48 && g.DpiY >= 48) { px = g.DpiX / 72.0; py = g.DpiY / 72.0; }
                    fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
                    fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                }
                catch (Exception) { broken = true; }
            }

            Font Get(string family, double pt, bool bold)
            {
                string key = family + "|" + pt.ToString(CultureInfo.InvariantCulture) + "|" + bold;
                Font f;
                if (fonts.TryGetValue(key, out f)) return f;
                try { f = new Font(family, (float)pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
                catch (Exception) { f = new Font(FontFamily.GenericSansSerif, (float)pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
                fonts[key] = f;
                return f;
            }

            public double Width(string text, string family, double pt, bool bold)
            {
                if (string.IsNullOrEmpty(text)) return 0;
                if (!broken)
                {
                    try { return g.MeasureString(text, Get(family, pt, bold), new PointF(0, 0), fmt).Width / px * 1.04; }
                    catch (Exception) { broken = true; }
                }
                return Guess(text) * pt;
            }

            static double Guess(string text)
            {
                double em = 0;
                foreach (char c in text) em += c >= 0x2E80 ? 1.0 : c == ' ' ? 0.28 : char.IsUpper(c) ? 0.62 : 0.52;
                return em;
            }

            public int Lines(string text, string family, double pt, bool bold, double width)
            {
                if (width <= 10) width = 10;
                int total = 0;
                foreach (string para in (text ?? "").Split('\n'))
                {
                    if (para.Length == 0) { total++; continue; }
                    if (!broken)
                    {
                        try
                        {
                            int chars, lines;
                            g.MeasureString(para, Get(family, pt, bold), new SizeF((float)(width / 1.04 * px), 1000000f), fmt, out chars, out lines);
                            total += Math.Max(1, lines);
                            continue;
                        }
                        catch (Exception) { broken = true; }
                    }
                    total += Math.Max(1, (int)Math.Ceiling(Guess(para) * pt / width));
                }
                return Math.Max(1, total);
            }

            public double LineHeight(string family, double pt)
            {
                if (!broken)
                {
                    try { return Math.Max(pt * 1.17, Get(family, pt, false).GetHeight(g) / py); }
                    catch (Exception) { broken = true; }
                }
                return pt * 1.3;
            }

            public void Dispose()
            {
                foreach (Font f in fonts.Values) f.Dispose();
                if (g != null) g.Dispose();
                if (bmp != null) bmp.Dispose();
                if (fmt != null) fmt.Dispose();
            }
        }

        class Rels
        {
            public readonly StringBuilder Xml = new StringBuilder();
            public int Next = 2;
            readonly Dictionary<string, string> links = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly List<KeyValuePair<string, byte[]>> Media;
            readonly Dictionary<DocPicture, string> names;
            readonly Dictionary<DocPicture, string> ids = new Dictionary<DocPicture, string>();
            public Rels(List<KeyValuePair<string, byte[]>> media, Dictionary<DocPicture, string> shared) { Media = media; names = shared; }

            public string Link(string url)
            {
                string id;
                if (links.TryGetValue(url, out id)) return id;
                id = "rId" + (Next++).ToString(CultureInfo.InvariantCulture);
                links[url] = id;
                Xml.Append("<Relationship Id=\"").Append(id).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"").Append(DocXml.Esc(url)).Append("\" TargetMode=\"External\"/>");
                return id;
            }

            public string Image(DocPicture p)
            {
                string id;
                if (ids.TryGetValue(p, out id)) return id;
                id = "rId" + (Next++).ToString(CultureInfo.InvariantCulture);
                ids[p] = id;
                string name;
                if (!names.TryGetValue(p, out name))
                {
                    name = "image" + (Media.Count + 1).ToString(CultureInfo.InvariantCulture) + (p.Jpeg ? ".jpeg" : ".png");
                    names[p] = name;
                    Media.Add(new KeyValuePair<string, byte[]>("ppt/media/" + name, p.Data));
                }
                Xml.Append("<Relationship Id=\"").Append(id).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/").Append(name).Append("\"/>");
                return id;
            }
        }

        class Shapes
        {
            public readonly StringBuilder Xml = new StringBuilder();
            public int Next = 2;
            public readonly Rels Rels;
            public Shapes(Rels r) { Rels = r; }
        }

        static string Xfrm(double x, double y, double w, double h)
        {
            return "<a:xfrm><a:off x=\"" + E(x) + "\" y=\"" + E(y) + "\"/><a:ext cx=\"" + Math.Max(1, E(w)) + "\" cy=\"" + Math.Max(1, E(h)) + "\"/></a:xfrm>";
        }

        static string Fill(string rgb) { return "<a:solidFill><a:srgbClr val=\"" + rgb + "\"/></a:solidFill>"; }

        static string Sz(double pt) { return ((int)Math.Round(pt * 100)).ToString(CultureInfo.InvariantCulture); }

        static void Runs(StringBuilder sb, List<DocSpan> spans, Shapes sh, double pt, string color, bool bold, string font)
        {
            if (spans == null) return;
            foreach (DocSpan s in spans)
            {
                if (s.Break) { sb.Append("<a:br><a:rPr sz=\"").Append(Sz(pt)).Append("\"/></a:br>"); continue; }
                if (s.Text.Length == 0) continue;
                sb.Append("<a:r><a:rPr sz=\"").Append(Sz(s.Code ? pt * 0.92 : pt)).Append("\"");
                if (s.Bold || bold) sb.Append(" b=\"1\"");
                if (s.Italic) sb.Append(" i=\"1\"");
                if (s.Strike) sb.Append(" strike=\"sngStrike\"");
                sb.Append(" dirty=\"0\">");
                sb.Append(Fill(s.Link != null ? LinkBlue : color));
                string face = s.Code ? CodeFont : font;
                if (face != null) sb.Append("<a:latin typeface=\"").Append(face).Append("\"/><a:cs typeface=\"").Append(face).Append("\"/>");
                if (s.Link != null) sb.Append("<a:hlinkClick r:id=\"").Append(sh.Rels.Link(s.Link)).Append("\"/>");
                sb.Append("</a:rPr><a:t>").Append(DocXml.Esc(s.Text.Replace('\t', ' '))).Append("</a:t></a:r>");
            }
        }

        static void TextShape(Shapes sh, string name, string ph, double x, double y, double w, double h, string anchor, StringBuilder paras, string inset, bool autofit)
        {
            int id = sh.Next++;
            StringBuilder x1 = sh.Xml;
            x1.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"").Append(id).Append("\" name=\"").Append(name).Append(' ').Append(id - 1).Append("\"/>");
            if (ph != null) x1.Append("<p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr>").Append(ph).Append("</p:nvPr>");
            else x1.Append("<p:cNvSpPr txBox=\"1\"/><p:nvPr/>");
            x1.Append("</p:nvSpPr><p:spPr>").Append(Xfrm(x, y, w, h)).Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>");
            x1.Append("<p:txBody><a:bodyPr wrap=\"square\" ").Append(inset ?? "lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\"").Append(" anchor=\"").Append(anchor).Append("\">").Append(autofit ? "<a:normAutofit/>" : "<a:noAutofit/>").Append("</a:bodyPr><a:lstStyle/>");
            x1.Append(paras.Length > 0 ? paras.ToString() : "<a:p><a:endParaRPr dirty=\"0\"/></a:p>");
            x1.Append("</p:txBody></p:sp>");
        }

        static void Rect(Shapes sh, string name, double x, double y, double w, double h, string fill, string line, string geom)
        {
            int id = sh.Next++;
            sh.Xml.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"").Append(id).Append("\" name=\"").Append(name).Append(' ').Append(id - 1).Append("\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>").Append(Xfrm(x, y, w, h));
            sh.Xml.Append("<a:prstGeom prst=\"").Append(geom).Append("\"><a:avLst/></a:prstGeom>");
            sh.Xml.Append(fill != null ? Fill(fill) : "<a:noFill/>");
            if (line != null) sh.Xml.Append("<a:ln w=\"9525\">").Append(Fill(line)).Append("</a:ln>");
            else sh.Xml.Append("<a:ln><a:noFill/></a:ln>");
            sh.Xml.Append("</p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr dirty=\"0\"/></a:p></p:txBody></p:sp>");
        }

        static void Picture(Shapes sh, DocPicture p, double x, double y, double w, double h)
        {
            int id = sh.Next++;
            string rid = sh.Rels.Image(p);
            string alt = DocXml.Esc(p.Alt ?? "");
            sh.Xml.Append("<p:pic><p:nvPicPr><p:cNvPr id=\"").Append(id).Append("\" name=\"Picture ").Append(id - 1).Append("\" descr=\"").Append(alt).Append("\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>");
            sh.Xml.Append("<p:blipFill><a:blip r:embed=\"").Append(rid).Append("\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>");
            sh.Xml.Append("<p:spPr>").Append(Xfrm(x, y, w, h)).Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>");
        }

        static string Bullet(DocBlock b, double pt, double sc, bool first, bool rtl)
        {
            StringBuilder pp = new StringBuilder();
            double marL = ItemMarL(b);
            pp.Append("<a:pPr marL=\"").Append(E(marL)).Append("\" indent=\"").Append(E(ItemIndent(b))).Append("\"");
            if (rtl) pp.Append(" algn=\"r\" rtl=\"1\"");
            pp.Append(">");
            if (!first) pp.Append("<a:spcBef><a:spcPts val=\"").Append(Sz(SpaceBefore(b, sc))).Append("\"/></a:spcBef>");
            if (b.Task >= 0) pp.Append("<a:buNone/>");
            else if (b.Ordered)
            {
                pp.Append("<a:buClr><a:srgbClr val=\"").Append(Accent).Append("\"/></a:buClr><a:buFont typeface=\"+mj-lt\"/>");
                string type = b.Level % 3 == 1 ? "alphaLcPeriod" : b.Level % 3 == 2 ? "romanLcPeriod" : "arabicPeriod";
                pp.Append("<a:buAutoNum type=\"").Append(type).Append("\"");
                if (b.Number != 1) pp.Append(" startAt=\"").Append(Math.Max(1, Math.Min(32767, b.Number))).Append("\"");
                pp.Append("/>");
            }
            else
            {
                pp.Append("<a:buClr><a:srgbClr val=\"").Append(Accent).Append("\"/></a:buClr><a:buFont typeface=\"Arial\"/>");
                pp.Append("<a:buChar char=\"").Append(b.Level % 2 == 0 ? "\u2022" : "\u2013").Append("\"/>");
            }
            pp.Append("</a:pPr>");
            return pp.ToString();
        }

        static void TextItem(Shapes sh, Item it, double x, double y, double w, double h, double sc, bool body)
        {
            StringBuilder paras = new StringBuilder();
            bool quote = it.Kind == KQuote;
            int startNumber = 0;
            for (int i = 0; i < it.Blocks.Count; i++)
            {
                DocBlock b = it.Blocks[i];
                double pt = ParaPt(b, sc);
                bool rtl = DocBidi.RtlSpans(b.Spans);
                paras.Append("<a:p>");
                if (b.Kind == DocKind.Item)
                {
                    paras.Append(Bullet(b, pt, sc, i == 0, rtl));
                    if (b.Task >= 0) paras.Append("<a:r><a:rPr sz=\"").Append(Sz(pt)).Append("\" dirty=\"0\">").Append(Fill(Accent)).Append("<a:latin typeface=\"Segoe UI Symbol\"/><a:cs typeface=\"Segoe UI Symbol\"/></a:rPr><a:t>").Append(b.Task == 1 ? "\u2611 " : "\u2610 ").Append("</a:t></a:r>");
                    startNumber++;
                }
                else
                {
                    paras.Append("<a:pPr marL=\"0\" indent=\"0\"");
                    if (rtl) paras.Append(" algn=\"r\" rtl=\"1\"");
                    paras.Append(">");
                    if (i > 0) paras.Append("<a:spcBef><a:spcPts val=\"").Append(Sz(SpaceBefore(b, sc))).Append("\"/></a:spcBef>");
                    paras.Append("<a:buNone/></a:pPr>");
                }
                string color = quote ? Muted : b.Kind == DocKind.Heading ? Ink : Body;
                List<DocSpan> spans = b.Spans;
                if (quote)
                {
                    spans = new List<DocSpan>();
                    foreach (DocSpan s0 in b.Spans) spans.Add(new DocSpan { Text = s0.Text, Bold = s0.Bold, Italic = true, Code = s0.Code, Strike = s0.Strike, Break = s0.Break, Link = s0.Link });
                }
                Runs(paras, spans, sh, pt, color, b.Kind == DocKind.Heading, null);
                paras.Append("<a:endParaRPr sz=\"").Append(Sz(pt)).Append("\" dirty=\"0\"/></a:p>");
            }
            if (quote)
            {
                Rect(sh, "Quote bar", x, y + 2, 3.5, Math.Max(8, h - 8), Accent, null, "rect");
                TextShape(sh, "Quote", null, x + 18, y, w - 18, h, "t", paras, null, true);
            }
            else TextShape(sh, body ? "Content Placeholder" : "Text", body ? "<p:ph idx=\"1\"/>" : null, x, y, w, h, "t", paras, null, true);
        }

        static void CodeItem(Shapes sh, Item it, double x, double y, double w, double h, double sc)
        {
            double pt = CodePt(sc);
            StringBuilder paras = new StringBuilder();
            foreach (string l in (it.Blocks[0].Code ?? "").Split('\n'))
            {
                paras.Append("<a:p><a:pPr><a:lnSpc><a:spcPct val=\"100000\"/></a:lnSpc><a:buNone/></a:pPr>");
                if (l.Length > 0) paras.Append("<a:r><a:rPr sz=\"").Append(Sz(pt)).Append("\" dirty=\"0\">").Append(Fill(Ink)).Append("<a:latin typeface=\"").Append(CodeFont).Append("\"/><a:cs typeface=\"").Append(CodeFont).Append("\"/></a:rPr><a:t>").Append(DocXml.Esc(l.Replace("\t", "    "))).Append("</a:t></a:r>");
                paras.Append("<a:endParaRPr sz=\"").Append(Sz(pt)).Append("\" dirty=\"0\"><a:latin typeface=\"").Append(CodeFont).Append("\"/></a:endParaRPr></a:p>");
            }
            int id = sh.Next++;
            sh.Xml.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"").Append(id).Append("\" name=\"Code ").Append(id - 1).Append("\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr>").Append(Xfrm(x, y, w, h));
            sh.Xml.Append("<a:prstGeom prst=\"roundRect\"><a:avLst><a:gd name=\"adj\" fmla=\"val ").Append(Math.Min(8000, (int)(6 / Math.Max(12, h) * 100000))).Append("\"/></a:avLst></a:prstGeom>").Append(Fill(Soft)).Append("<a:ln w=\"9525\">").Append(Fill(Line)).Append("</a:ln></p:spPr>");
            sh.Xml.Append("<p:txBody><a:bodyPr wrap=\"square\" lIns=\"").Append(E(12)).Append("\" tIns=\"").Append(E(9)).Append("\" rIns=\"").Append(E(12)).Append("\" bIns=\"").Append(E(9)).Append("\" anchor=\"t\"><a:normAutofit/></a:bodyPr><a:lstStyle/>").Append(paras).Append("</p:txBody></p:sp>");
        }

        static bool TableRtl(DocBlock t)
        {
            foreach (List<List<DocSpan>> row in t.Rows)
                foreach (List<DocSpan> cell in row)
                    foreach (DocSpan s in cell)
                        foreach (char ch in s.Text)
                        {
                            if (DocBidi.Rtl(ch)) return true;
                            if (char.IsLetter(ch)) return false;
                        }
            return false;
        }

        static void TableItem(Shapes sh, Item it, double x, double y, double w, double sc, Meter meter)
        {
            DocBlock t = it.Blocks[0];
            double pt = TablePt(sc);
            double[] cols = TableColumns(it.Source ?? t, pt, meter);
            double total = 0;
            foreach (double c in cols) total += c;
            bool tableRtl = TableRtl(it.Source ?? t);
            if (tableRtl) x += Math.Max(0, w - total);
            int id = sh.Next++;
            StringBuilder x1 = sh.Xml;
            double totalH = 0;
            List<double> heights = new List<double>();
            foreach (List<List<DocSpan>> row in t.Rows) { double rh = RowHeight(row, cols, pt, meter); heights.Add(rh); totalH += rh; }
            x1.Append("<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"").Append(id).Append("\" name=\"Table ").Append(id - 1).Append("\"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>");
            x1.Append("<p:xfrm><a:off x=\"").Append(E(x)).Append("\" y=\"").Append(E(y)).Append("\"/><a:ext cx=\"").Append(E(total)).Append("\" cy=\"").Append(E(totalH)).Append("\"/></p:xfrm>");
            x1.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblPr").Append(tableRtl ? " rtl=\"1\"" : "").Append(" firstRow=\"1\"/><a:tblGrid>");
            foreach (double c in cols) x1.Append("<a:gridCol w=\"").Append(E(c)).Append("\"/>");
            x1.Append("</a:tblGrid>");
            for (int r = 0; r < t.Rows.Count; r++)
            {
                bool head = r == 0;
                x1.Append("<a:tr h=\"").Append(E(heights[r])).Append("\">");
                for (int c = 0; c < cols.Length; c++)
                {
                    List<DocSpan> cell = c < t.Rows[r].Count ? t.Rows[r][c] : new List<DocSpan>();
                    char al = c < t.Align.Count ? t.Align[c] : 'l';
                    bool rtl = DocBidi.RtlSpans(cell);
                    x1.Append("<a:tc><a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:pPr");
                    if (al == 'c') x1.Append(" algn=\"ctr\"");
                    else if (al == 'r' || rtl) x1.Append(" algn=\"r\"");
                    if (rtl) x1.Append(" rtl=\"1\"");
                    x1.Append("><a:buNone/></a:pPr>");
                    Runs(x1, cell, sh, pt, head ? Ink : Body, head, null);
                    x1.Append("<a:endParaRPr sz=\"").Append(Sz(pt)).Append("\" dirty=\"0\"/></a:p></a:txBody>");
                    x1.Append("<a:tcPr marL=\"").Append(E(7)).Append("\" marR=\"").Append(E(7)).Append("\" marT=\"").Append(E(4)).Append("\" marB=\"").Append(E(4)).Append("\" anchor=\"ctr\">");
                    foreach (string side in new string[] { "lnL", "lnR", "lnT", "lnB" })
                    {
                        bool show = side == "lnB" || (side == "lnT" && r == 0);
                        if (show) x1.Append("<a:").Append(side).Append(" w=\"").Append(head && side == "lnB" ? "12700" : "6350").Append("\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\">").Append(Fill(head && side == "lnB" ? "B8C0CC" : Line)).Append("<a:prstDash val=\"solid\"/></a:").Append(side).Append(">");
                        else x1.Append("<a:").Append(side).Append(" w=\"0\"><a:noFill/></a:").Append(side).Append(">");
                    }
                    x1.Append(head ? Fill(Head) : "<a:noFill/>");
                    x1.Append("</a:tcPr></a:tc>");
                }
                x1.Append("</a:tr>");
            }
            x1.Append("</a:tbl></a:graphicData></a:graphic></p:graphicFrame>");
        }

        static string SlideXml(SlideOut s, int number, Shapes sh, Meter meter)
        {
            sh.Xml.Length = 0;
            sh.Next = 2;
            if (s.Layout == LayoutTitle)
            {
                StringBuilder tp = new StringBuilder();
                bool trtl = DocBidi.RtlSpans(s.Title);
                double tsz = meter.Lines(Plain(s.Title), TitleFont, 54, false, ContentW) > 2 ? 40 : 54;
                tp.Append("<a:p><a:pPr").Append(trtl ? " algn=\"r\" rtl=\"1\"" : "").Append("><a:lnSpc><a:spcPct val=\"90000\"/></a:lnSpc></a:pPr>");
                Runs(tp, s.Title, sh, tsz, Ink, false, null);
                tp.Append("<a:endParaRPr sz=\"").Append(Sz(tsz)).Append("\" dirty=\"0\"/></a:p>");
                TextShape(sh, "Title", "<p:ph type=\"ctrTitle\"/>", Left, 96, ContentW, 224, "b", tp, null, true);
                Rect(sh, "Accent", trtl ? Left + ContentW - 64 : Left, 336, 64, 5, Accent, null, "rect");
                if (s.Subtitle != null)
                {
                    StringBuilder sp = new StringBuilder();
                    bool srtl = DocBidi.RtlSpans(s.Subtitle);
                    sp.Append("<a:p><a:pPr").Append(srtl ? " algn=\"r\" rtl=\"1\"" : "").Append("><a:buNone/></a:pPr>");
                    Runs(sp, s.Subtitle, sh, 24, Muted, false, null);
                    sp.Append("<a:endParaRPr sz=\"2400\" dirty=\"0\"/></a:p>");
                    TextShape(sh, "Subtitle", "<p:ph type=\"subTitle\" idx=\"1\"/>", Left, 360, ContentW, 130, "t", sp, null, true);
                }
            }
            else if (s.Layout == LayoutQuote)
            {
                Item q = s.Items[0];
                Item who = s.Items.Count > 1 ? s.Items[1] : null;
                StringBuilder qp = new StringBuilder();
                bool qrtl = false;
                double qpt = 34;
                string all = "";
                foreach (DocBlock b in q.Blocks) all += Plain(b.Spans) + "\n";
                while (qpt > 22 && meter.Lines(all.TrimEnd('\n'), BodyFont, qpt, false, ContentW - 40) * meter.LineHeight(BodyFont, qpt) > 260) qpt -= 2;
                for (int i = 0; i < q.Blocks.Count; i++)
                {
                    DocBlock b = q.Blocks[i];
                    qrtl = qrtl || DocBidi.RtlSpans(b.Spans);
                    qp.Append("<a:p><a:pPr").Append(DocBidi.RtlSpans(b.Spans) ? " algn=\"r\" rtl=\"1\"" : "").Append("><a:lnSpc><a:spcPct val=\"105000\"/></a:lnSpc>");
                    if (i > 0) qp.Append("<a:spcBef><a:spcPts val=\"1200\"/></a:spcBef>");
                    qp.Append("<a:buNone/></a:pPr>");
                    List<DocSpan> spans = new List<DocSpan>();
                    foreach (DocSpan s0 in b.Spans) spans.Add(new DocSpan { Text = s0.Text, Bold = s0.Bold, Italic = true, Code = s0.Code, Strike = s0.Strike, Break = s0.Break, Link = s0.Link });
                    Runs(qp, spans, sh, qpt, Ink, false, null);
                    qp.Append("<a:endParaRPr sz=\"").Append(Sz(qpt)).Append("\" dirty=\"0\"/></a:p>");
                }
                double qh = meter.Lines(all.TrimEnd('\n'), BodyFont, qpt, false, ContentW - 40) * meter.LineHeight(BodyFont, qpt) * 1.05 + 12 * Math.Max(0, q.Blocks.Count - 1);
                double wh = 0;
                StringBuilder wp = new StringBuilder();
                if (who != null)
                    for (int i = 0; i < who.Blocks.Count; i++)
                    {
                        DocBlock b = who.Blocks[i];
                        wp.Append("<a:p><a:pPr").Append(DocBidi.RtlSpans(b.Spans) ? " algn=\"r\" rtl=\"1\"" : "").Append("><a:buNone/></a:pPr>");
                        Runs(wp, b.Spans, sh, 20, Muted, false, null);
                        wp.Append("<a:endParaRPr sz=\"2000\" dirty=\"0\"/></a:p>");
                        wh += meter.Lines(Plain(b.Spans), BodyFont, 20, false, ContentW - 40) * meter.LineHeight(BodyFont, 20);
                    }
                double total = qh + (wh > 0 ? 18 + wh : 0);
                double y0 = Math.Max(48, (SlideH - total) / 2 - 10);
                Rect(sh, "Quote bar", qrtl ? Left + ContentW - 5 : Left, y0 + 4, 5, Math.Max(20, qh - 8), Accent, null, "rect");
                TextShape(sh, "Quote", null, qrtl ? Left : Left + 32, y0, ContentW - 32, qh, "t", qp, null, true);
                if (wh > 0) TextShape(sh, "Attribution", null, qrtl ? Left : Left + 32, y0 + qh + 18, ContentW - 32, wh + 4, "t", wp, null, true);
                SlideNumber(sh, number);
            }
            else if (s.Layout == LayoutSection)
            {
                StringBuilder tp = new StringBuilder();
                bool trtl = DocBidi.RtlSpans(s.Title);
                tp.Append("<a:p><a:pPr").Append(trtl ? " algn=\"r\" rtl=\"1\"" : "").Append("><a:lnSpc><a:spcPct val=\"90000\"/></a:lnSpc></a:pPr>");
                Runs(tp, s.Title, sh, 44, Ink, false, null);
                tp.Append("<a:endParaRPr sz=\"4400\" dirty=\"0\"/></a:p>");
                TextShape(sh, "Title", "<p:ph type=\"title\"/>", Left, 130, ContentW, 190, "b", tp, null, true);
                Rect(sh, "Accent", trtl ? Left + ContentW - 64 : Left, 336, 64, 5, Accent, null, "rect");
                SlideNumber(sh, number);
            }
            else
            {
                if (s.Title != null)
                {
                    StringBuilder tp = new StringBuilder();
                    List<DocSpan> t = s.Title;
                    bool trtl = DocBidi.RtlSpans(t);
                    tp.Append("<a:p><a:pPr").Append(trtl ? " algn=\"r\" rtl=\"1\"" : "").Append("><a:lnSpc><a:spcPct val=\"92000\"/></a:lnSpc></a:pPr>");
                    double tpt = meter.Lines(Plain(t) + (s.Continued ? " (continued)" : ""), TitleFont, 36, false, ContentW) > 1 ? 28 : 36;
                    Runs(tp, t, sh, tpt, Ink, false, null);
                    if (s.Continued) Runs(tp, new List<DocSpan>(new DocSpan[] { new DocSpan { Text = " (continued)" } }), sh, tpt, Muted, false, BodyFont);
                    tp.Append("<a:endParaRPr sz=\"").Append(Sz(tpt)).Append("\" dirty=\"0\"/></a:p>");
                    TextShape(sh, "Title", "<p:ph type=\"title\"/>", Left, 26, ContentW, 76, "b", tp, null, true);
                    Rect(sh, "Accent", trtl ? Left + ContentW - 44 : Left, 109, 44, 4, Accent, null, "rect");
                }
                double sc = s.Scale;
                double top = s.Title != null ? ContentTop : 60;
                bool bodyUsed = false;
                if (s.TwoColumn)
                {
                    double textW = ContentW * 0.54, picX = Left + ContentW * 0.58, picW = ContentW * 0.42;
                    double textH = 0;
                    List<Item> texts = new List<Item>();
                    Item pic = null;
                    foreach (Item it in s.Items) { if (it.Kind == KPicture) pic = it; else { texts.Add(it); textH += ItemHeight(it, sc, textW, meter) + Gap(sc); } }
                    textH = Math.Max(0, textH - Gap(sc));
                    double avail = ContentBottom - top;
                    double ty = top + Math.Max(0, (avail - textH) / 2 * 0.6);
                    foreach (Item it in texts)
                    {
                        double ih = ItemHeight(it, sc, textW, meter);
                        TextItem(sh, it, Left, ty, textW, ih, sc, !bodyUsed && it.Kind == KText);
                        if (it.Kind == KText) bodyUsed = true;
                        ty += ih + Gap(sc);
                    }
                    SizeF ps = PictureSize(pic.Picture, picW, avail);
                    Picture(sh, pic.Picture, picX + (picW - ps.Width) / 2, top + (avail - ps.Height) / 2, ps.Width, ps.Height);
                }
                else
                {
                    double y = top;
                    double used = 0;
                    foreach (Item it in s.Items) used += ItemHeight(it, sc, ContentW, meter);
                    used += Gap(sc) * Math.Max(0, s.Items.Count - 1);
                    double avail = ContentBottom - top;
                    bool centerAll = s.Items.Count == 1 && (s.Items[0].Kind == KPicture || s.Items[0].Kind == KMath);
                    if (centerAll) y = top + Math.Max(0, (avail - used) / 2);
                    foreach (Item it in s.Items)
                    {
                        double ih = ItemHeight(it, sc, ContentW, meter);
                        double room = Math.Max(20, ContentBottom - y);
                        switch (it.Kind)
                        {
                            case KText:
                            case KQuote:
                                TextItem(sh, it, Left, y, ContentW, Math.Min(ih, room), sc, !bodyUsed && it.Kind == KText);
                                if (it.Kind == KText) bodyUsed = true;
                                break;
                            case KCode:
                                CodeItem(sh, it, Left, y, ContentW, Math.Min(ih, room), sc);
                                break;
                            case KTable:
                                TableItem(sh, it, Left, y, ContentW, sc, meter);
                                break;
                            case KPicture:
                                {
                                    SizeF ps = PictureSize(it.Picture, ContentW, Math.Max(40, Math.Min(room, ih)));
                                    Picture(sh, it.Picture, Left + (ContentW - ps.Width) / 2, y, ps.Width, ps.Height);
                                    ih = ps.Height;
                                    break;
                                }
                            case KMath:
                                if (it.Lines != null)
                                {
                                    double k = Math.Min(1.0, sc), my = y + 4;
                                    for (int li = 0; li < it.Lines.Count; li++)
                                    {
                                        SizeF ps = PictureSize(it.Lines[li], ContentW, 100000);
                                        double pw = ps.Width * k, ph = ps.Height * k;
                                        Picture(sh, it.Lines[li], Left + (ContentW - pw) / 2, my, pw, ph);
                                        if (li == it.Lines.Count - 1 && !string.IsNullOrEmpty(it.Tag)) EquationTag(sh, it.Tag, my, ph, sc, meter);
                                        my += ph + 6 * k;
                                    }
                                }
                                else
                                {
                                    StringBuilder mp = new StringBuilder();
                                    mp.Append("<a:p><a:pPr algn=\"ctr\"><a:buNone/></a:pPr>");
                                    Runs(mp, new List<DocSpan>(new DocSpan[] { new DocSpan { Text = it.Fallback ?? "", Italic = true } }), sh, BodyPt(sc), Ink, false, "Cambria Math");
                                    mp.Append("</a:p>");
                                    TextShape(sh, "Equation", null, Left, y, ContentW, Math.Min(ih, room), "t", mp, null, true);
                                }
                                break;
                        }
                        y += ih + Gap(sc);
                    }
                }
                SlideNumber(sh, number);
            }
            StringBuilder x = new StringBuilder();
            x.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<p:sld xmlns:a=\"").Append(A).Append("\" xmlns:r=\"").Append(R).Append("\" xmlns:p=\"").Append(P).Append("\">");
            x.Append("<p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>");
            x.Append(sh.Xml).Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
            return x.ToString();
        }

        static void EquationTag(Shapes sh, string tag, double y, double h, double sc, Meter meter)
        {
            double pt = BodyPt(sc), lh = meter.LineHeight(BodyFont, pt);
            StringBuilder p = new StringBuilder();
            p.Append("<a:p><a:pPr algn=\"r\"><a:buNone/></a:pPr>");
            Runs(p, new List<DocSpan>(new DocSpan[] { new DocSpan { Text = tag } }), sh, pt, Body, false, null);
            p.Append("<a:endParaRPr sz=\"").Append(Sz(pt)).Append("\" dirty=\"0\"/></a:p>");
            TextShape(sh, "Equation number", null, Left + ContentW - 150, y + (h - lh) / 2, 150, lh, "ctr", p, null, false);
        }

        static void SlideNumber(Shapes sh, int number)
        {
            StringBuilder np = new StringBuilder();
            np.Append("<a:p><a:pPr algn=\"r\"><a:buNone/></a:pPr><a:fld id=\"{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}\" type=\"slidenum\"><a:rPr sz=\"1100\" dirty=\"0\">").Append(Fill(Muted)).Append("</a:rPr><a:t>").Append(number).Append("</a:t></a:fld><a:endParaRPr sz=\"1100\" dirty=\"0\"/></a:p>");
            TextShape(sh, "Slide Number", "<p:ph type=\"sldNum\" sz=\"quarter\" idx=\"12\"/>", SlideW - Left - 80, 506, 80, 20, "ctr", np, null, false);
        }

        static string NotesXml(string notes, int slideNumber)
        {
            StringBuilder paras = new StringBuilder();
            foreach (string l in notes.Split('\n'))
                paras.Append("<a:p>").Append(l.Trim().Length > 0 ? "<a:r><a:rPr dirty=\"0\"/><a:t>" + DocXml.Esc(l.TrimEnd()) + "</a:t></a:r>" : "").Append("<a:endParaRPr dirty=\"0\"/></a:p>");
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<p:notes xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\" xmlns:p=\"" + P + "\"><p:cSld><p:spTree>" +
                "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
                "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>" +
                "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes Placeholder 2\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/>" + paras + "</p:txBody></p:sp>" +
                "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:notes>";
        }

        static string Theme(string name)
        {
            StringBuilder t = new StringBuilder();
            t.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<a:theme xmlns:a=\"").Append(A).Append("\" name=\"").Append(name).Append("\"><a:themeElements>");
            t.Append("<a:clrScheme name=\"Plain\"><a:dk1><a:srgbClr val=\"").Append(Ink).Append("\"/></a:dk1><a:lt1><a:srgbClr val=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"2D333B\"/></a:dk2><a:lt2><a:srgbClr val=\"EEF1F5\"/></a:lt2>");
            string[] acc = { Accent, "1A7F64", "B35900", "8250DF", "0E7490", "B42318" };
            for (int i = 0; i < 6; i++) t.Append("<a:accent").Append(i + 1).Append("><a:srgbClr val=\"").Append(acc[i]).Append("\"/></a:accent").Append(i + 1).Append(">");
            t.Append("<a:hlink><a:srgbClr val=\"").Append(LinkBlue).Append("\"/></a:hlink><a:folHlink><a:srgbClr val=\"8250DF\"/></a:folHlink></a:clrScheme>");
            t.Append("<a:fontScheme name=\"Plain\"><a:majorFont><a:latin typeface=\"").Append(TitleFont).Append("\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont><a:minorFont><a:latin typeface=\"").Append(BodyFont).Append("\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>");
            t.Append("<a:fmtScheme name=\"Plain\"><a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"><a:tint val=\"50000\"/></a:schemeClr></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"><a:shade val=\"80000\"/></a:schemeClr></a:solidFill></a:fillStyleLst>");
            t.Append("<a:lnStyleLst><a:ln w=\"6350\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/></a:ln><a:ln w=\"12700\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/></a:ln><a:ln w=\"19050\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/></a:ln></a:lnStyleLst>");
            t.Append("<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>");
            t.Append("<a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"><a:tint val=\"95000\"/></a:schemeClr></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"><a:shade val=\"90000\"/></a:schemeClr></a:solidFill></a:bgFillStyleLst></a:fmtScheme>");
            t.Append("</a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>");
            return t.ToString();
        }

        static string Level(string tag, double marL, double indent, double pt, bool bullet, string font)
        {
            StringBuilder s = new StringBuilder();
            s.Append("<a:").Append(tag).Append(" marL=\"").Append(E(marL)).Append("\" indent=\"").Append(E(indent)).Append("\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\">");
            s.Append("<a:spcBef><a:spcPts val=\"").Append(bullet ? "900" : "0").Append("\"/></a:spcBef>");
            if (bullet) s.Append("<a:buClr><a:srgbClr val=\"").Append(Accent).Append("\"/></a:buClr><a:buFont typeface=\"Arial\"/><a:buChar char=\"\u2022\"/>");
            else s.Append("<a:buNone/>");
            s.Append("<a:defRPr sz=\"").Append(Sz(pt)).Append("\" kern=\"1200\">").Append(Fill(bullet ? Body : Ink)).Append("<a:latin typeface=\"").Append(font).Append("\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:").Append(tag).Append(">");
            return s.ToString();
        }

        static string Master()
        {
            StringBuilder m = new StringBuilder();
            m.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<p:sldMaster xmlns:a=\"").Append(A).Append("\" xmlns:r=\"").Append(R).Append("\" xmlns:p=\"").Append(P).Append("\">");
            m.Append("<p:cSld><p:bg><p:bgPr>").Append(Fill("FFFFFF")).Append("<a:effectLst/></p:bgPr></p:bg><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>");
            m.Append(Ph(2, "Title Placeholder 1", "<p:ph type=\"title\"/>", Left, 30, ContentW, 72, "b"));
            m.Append(Ph(3, "Text Placeholder 2", "<p:ph type=\"body\" idx=\"1\"/>", Left, ContentTop, ContentW, ContentBottom - ContentTop, "t"));
            m.Append(Ph(4, "Slide Number Placeholder 3", "<p:ph type=\"sldNum\" sz=\"quarter\" idx=\"12\"/>", SlideW - Left - 80, 506, 80, 20, "ctr"));
            m.Append("</p:spTree></p:cSld>");
            m.Append("<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>");
            m.Append("<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/><p:sldLayoutId id=\"2147483650\" r:id=\"rId2\"/><p:sldLayoutId id=\"2147483651\" r:id=\"rId3\"/></p:sldLayoutIdLst>");
            m.Append("<p:txStyles><p:titleStyle>").Append(Level("lvl1pPr", 0, 0, 32, false, "+mj-lt")).Append("</p:titleStyle><p:bodyStyle>");
            for (int i = 0; i < 9; i++) m.Append(Level("lvl" + (i + 1) + "pPr", 22 + 26 * i, -18, i == 0 ? 22 : 20, true, "+mn-lt"));
            m.Append("</p:bodyStyle><p:otherStyle>").Append(Level("lvl1pPr", 0, 0, 18, false, "+mn-lt")).Append("</p:otherStyle></p:txStyles></p:sldMaster>");
            return m.ToString();
        }

        static string Ph(int id, string name, string ph, double x, double y, double w, double h, string anchor)
        {
            return "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"" + name + "\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr>" + ph + "</p:nvPr></p:nvSpPr><p:spPr>" + Xfrm(x, y, w, h) +
                "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr vert=\"horz\" lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\" rtlCol=\"0\" anchor=\"" + anchor + "\"><a:normAutofit/></a:bodyPr><a:lstStyle/><a:p><a:endParaRPr dirty=\"0\"/></a:p></p:txBody></p:sp>";
        }

        static string Layout(string name, string type, string shapes)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<p:sldLayout xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\" xmlns:p=\"" + P + "\" type=\"" + type + "\" preserve=\"1\"><p:cSld name=\"" + name + "\"><p:spTree>" +
                "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
                shapes + "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";
        }

        static string NotesMaster()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<p:notesMaster xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\" xmlns:p=\"" + P + "\"><p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree>" +
                "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
                "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\" idx=\"2\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"685800\" y=\"1143000\"/><a:ext cx=\"5486400\" cy=\"3086100\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/><a:ln w=\"12700\"><a:solidFill><a:prstClr val=\"black\"/></a:solidFill></a:ln></p:spPr></p:sp>" +
                "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes Placeholder 2\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"685800\" y=\"4400550\"/><a:ext cx=\"5486400\" cy=\"3600450\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" rtlCol=\"0\"/><a:lstStyle/><a:p><a:pPr lvl=\"0\"/><a:endParaRPr dirty=\"0\"/></a:p></p:txBody></p:sp>" +
                "</p:spTree></p:cSld><p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
                "<p:notesStyle><a:lvl1pPr marL=\"0\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\"><a:defRPr sz=\"1200\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl1pPr></p:notesStyle></p:notesMaster>";
        }

        static byte[] Package(List<SlideOut> slides, string title)
        {
            DocPackage pkg = new DocPackage();
            List<KeyValuePair<string, byte[]>> media = new List<KeyValuePair<string, byte[]>>();
            Dictionary<DocPicture, string> mediaNames = new Dictionary<DocPicture, string>();
            bool anyNotes = false;
            foreach (SlideOut s in slides) if (s.Notes.Length > 0) anyNotes = true;
            StringBuilder types = new StringBuilder();
            types.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            types.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            types.Append("<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>");
            types.Append("<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>");
            types.Append("<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>");
            for (int i = 1; i <= 3; i++) types.Append("<Override PartName=\"/ppt/slideLayouts/slideLayout").Append(i).Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/>");
            types.Append("<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>");
            types.Append("<Override PartName=\"/ppt/presProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presProps+xml\"/>");
            types.Append("<Override PartName=\"/ppt/viewProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.viewProps+xml\"/>");
            types.Append("<Override PartName=\"/ppt/tableStyles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.tableStyles+xml\"/>");
            types.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            if (anyNotes)
            {
                types.Append("<Override PartName=\"/ppt/notesMasters/notesMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.notesMaster+xml\"/>");
                types.Append("<Override PartName=\"/ppt/theme/theme2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>");
            }
            StringBuilder presRels = new StringBuilder();
            presRels.Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/>");
            presRels.Append("<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"theme/theme1.xml\"/>");
            presRels.Append("<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/presProps\" Target=\"presProps.xml\"/>");
            presRels.Append("<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/viewProps\" Target=\"viewProps.xml\"/>");
            presRels.Append("<Relationship Id=\"rId5\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableStyles\" Target=\"tableStyles.xml\"/>");
            if (anyNotes) presRels.Append("<Relationship Id=\"rId6\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesMaster\" Target=\"notesMasters/notesMaster1.xml\"/>");
            StringBuilder sldIds = new StringBuilder();
            using (Meter meter = new Meter())
            {
                for (int i = 0; i < slides.Count; i++)
                {
                    SlideOut s = slides[i];
                    int n = i + 1;
                    Rels rels = new Rels(media, mediaNames);
                    Shapes sh = new Shapes(rels);
                    string xml = SlideXml(s, n, sh, meter);
                    string layoutTarget = s.Layout == LayoutTitle ? "slideLayout1.xml" : s.Layout == LayoutSection || s.Layout == LayoutQuote ? "slideLayout3.xml" : "slideLayout2.xml";
                    StringBuilder sr = new StringBuilder();
                    sr.Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout\" Target=\"../slideLayouts/").Append(layoutTarget).Append("\"/>");
                    sr.Append(rels.Xml);
                    if (s.Notes.Length > 0)
                    {
                        string nid = "rId" + (rels.Next++).ToString(CultureInfo.InvariantCulture);
                        sr.Append("<Relationship Id=\"").Append(nid).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide\" Target=\"../notesSlides/notesSlide").Append(n).Append(".xml\"/>");
                        pkg.Add("ppt/notesSlides/notesSlide" + n + ".xml", NotesXml(s.Notes, n));
                        pkg.Add("ppt/notesSlides/_rels/notesSlide" + n + ".xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesMaster\" Target=\"../notesMasters/notesMaster1.xml\"/>" +
                            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"../slides/slide" + n + ".xml\"/></Relationships>");
                        types.Append("<Override PartName=\"/ppt/notesSlides/notesSlide").Append(n).Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml\"/>");
                    }
                    pkg.Add("ppt/slides/slide" + n + ".xml", xml);
                    pkg.Add("ppt/slides/_rels/slide" + n + ".xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + sr + "</Relationships>");
                    types.Append("<Override PartName=\"/ppt/slides/slide").Append(n).Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>");
                    string rid = "rId" + (10 + i).ToString(CultureInfo.InvariantCulture);
                    presRels.Append("<Relationship Id=\"").Append(rid).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide").Append(n).Append(".xml\"/>");
                    sldIds.Append("<p:sldId id=\"").Append(256 + i).Append("\" r:id=\"").Append(rid).Append("\"/>");
                }
            }
            types.Append("</Types>");
            pkg.Add("[Content_Types].xml", types.ToString());
            pkg.Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"ppt/presentation.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>");
            StringBuilder pres = new StringBuilder();
            pres.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<p:presentation xmlns:a=\"").Append(A).Append("\" xmlns:r=\"").Append(R).Append("\" xmlns:p=\"").Append(P).Append("\" saveSubsetFonts=\"1\">");
            pres.Append("<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>");
            if (anyNotes) pres.Append("<p:notesMasterIdLst><p:notesMasterId r:id=\"rId6\"/></p:notesMasterIdLst>");
            pres.Append("<p:sldIdLst>").Append(sldIds).Append("</p:sldIdLst>");
            pres.Append("<p:sldSz cx=\"").Append(E(SlideW)).Append("\" cy=\"").Append(E(SlideH)).Append("\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/>");
            pres.Append("<p:defaultTextStyle><a:defPPr><a:defRPr/></a:defPPr>").Append(Level("lvl1pPr", 0, 0, 18, false, "+mn-lt")).Append("</p:defaultTextStyle></p:presentation>");
            pkg.Add("ppt/presentation.xml", pres.ToString());
            pkg.Add("ppt/_rels/presentation.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + presRels + "</Relationships>");
            pkg.Add("ppt/slideMasters/slideMaster1.xml", Master());
            StringBuilder mr = new StringBuilder();
            for (int i = 1; i <= 3; i++) mr.Append("<Relationship Id=\"rId").Append(i).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout\" Target=\"../slideLayouts/slideLayout").Append(i).Append(".xml\"/>");
            mr.Append("<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"../theme/theme1.xml\"/>");
            pkg.Add("ppt/slideMasters/_rels/slideMaster1.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + mr + "</Relationships>");
            pkg.Add("ppt/slideLayouts/slideLayout1.xml", Layout("Title Slide", "title",
                Ph(2, "Title 1", "<p:ph type=\"ctrTitle\"/>", Left, 120, ContentW, 200, "b") + Ph(3, "Subtitle 2", "<p:ph type=\"subTitle\" idx=\"1\"/>", Left, 354, ContentW, 130, "t")));
            pkg.Add("ppt/slideLayouts/slideLayout2.xml", Layout("Title and Content", "obj",
                Ph(2, "Title 1", "<p:ph type=\"title\"/>", Left, 30, ContentW, 72, "b") + Ph(3, "Content Placeholder 2", "<p:ph idx=\"1\"/>", Left, ContentTop, ContentW, ContentBottom - ContentTop, "t") +
                Ph(4, "Slide Number Placeholder 3", "<p:ph type=\"sldNum\" sz=\"quarter\" idx=\"12\"/>", SlideW - Left - 80, 506, 80, 20, "ctr")));
            pkg.Add("ppt/slideLayouts/slideLayout3.xml", Layout("Section Header", "secHead",
                Ph(2, "Title 1", "<p:ph type=\"title\"/>", Left, 150, ContentW, 170, "b") + Ph(3, "Slide Number Placeholder 2", "<p:ph type=\"sldNum\" sz=\"quarter\" idx=\"12\"/>", SlideW - Left - 80, 506, 80, 20, "ctr")));
            for (int i = 1; i <= 3; i++)
                pkg.Add("ppt/slideLayouts/_rels/slideLayout" + i + ".xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/></Relationships>");
            pkg.Add("ppt/theme/theme1.xml", Theme("Plain"));
            if (anyNotes)
            {
                pkg.Add("ppt/theme/theme2.xml", Theme("Notes"));
                pkg.Add("ppt/notesMasters/notesMaster1.xml", NotesMaster());
                pkg.Add("ppt/notesMasters/_rels/notesMaster1.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"../theme/theme2.xml\"/></Relationships>");
            }
            pkg.Add("ppt/presProps.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<p:presentationPr xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\" xmlns:p=\"" + P + "\"/>");
            pkg.Add("ppt/viewProps.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<p:viewPr xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\" xmlns:p=\"" + P + "\"><p:normalViewPr><p:restoredLeft sz=\"15620\"/><p:restoredTop sz=\"94660\"/></p:normalViewPr><p:gridSpacing cx=\"76200\" cy=\"76200\"/></p:viewPr>");
            pkg.Add("ppt/tableStyles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<a:tblStyleLst xmlns:a=\"" + A + "\" def=\"{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}\"/>");
            pkg.Add("docProps/core.xml", DocXml.Core(title, DocXml.Author(), DateTime.UtcNow));
            foreach (KeyValuePair<string, byte[]> md in media) pkg.Add(md.Key, md.Value);
            return pkg.ToBytes();
        }
    }
}
