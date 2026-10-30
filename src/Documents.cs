using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace MinGent
{
    class DocInfo
    {
        public string Kind = "", Text = "";
        public int Pages;
        public bool Cut, NoText;
    }

    static class DocText
    {
        const long MaxFile = 96L * 1024 * 1024;
        const long MaxPart = 32L * 1024 * 1024;
        const long MaxTotal = 256L * 1024 * 1024;
        public const int MaxChars = 2000000;

        static readonly string[] Exts = { ".pdf", ".docx", ".docm", ".dotx", ".xlsx", ".xlsm", ".xltx", ".pptx", ".pptm", ".ppsx", ".odt", ".ods", ".odp", ".rtf", ".epub" };

        public static bool IsDocument(string path)
        {
            string ext = (Path.GetExtension(path ?? "") ?? "").ToLowerInvariant();
            return Array.IndexOf(Exts, ext) >= 0;
        }

        public static string KindName(string path)
        {
            switch ((Path.GetExtension(path ?? "") ?? "").ToLowerInvariant())
            {
                case ".pdf": return "PDF";
                case ".docx": case ".docm": case ".dotx": return "Word document";
                case ".xlsx": case ".xlsm": case ".xltx": return "Excel workbook";
                case ".pptx": case ".pptm": case ".ppsx": return "PowerPoint presentation";
                case ".odt": return "OpenDocument text";
                case ".ods": return "OpenDocument spreadsheet";
                case ".odp": return "OpenDocument presentation";
                case ".rtf": return "RTF document";
                case ".epub": return "EPUB book";
            }
            return "document";
        }

        static string Article(string kind)
        {
            return kind.StartsWith("RTF") || (kind.Length > 0 && "AEIOU".IndexOf(char.ToUpperInvariant(kind[0])) >= 0) ? "an " : "a ";
        }

        public static string Describe(DocInfo d)
        {
            if (d.Kind == "PDF") return "a " + d.Pages + "-page PDF";
            return Article(d.Kind) + d.Kind;
        }

        public static string NoTextMessage(string path, DocInfo d)
        {
            string name = Path.GetFileName(path);
            if (d.Kind == "PDF")
                return name + " has " + d.Pages + (d.Pages == 1 ? " page" : " pages") + " but no text MinGent can read. It is probably scanned images (MinGent does not read text out of pictures), or it uses fonts that hide their characters.";
            return name + " has no text.";
        }

        public static string CannotWrite(string path)
        {
            string kind = KindName(path);
            if (DocExport.CanWrite(path))
                return Path.GetFileName(path) + " is " + Article(kind) + kind + ", so it can't be edited in place. Write the complete new version with write_file (Markdown for .docx, .pdf and .pptx; CSV, Markdown tables or JSON rows for .xlsx).";
            return Path.GetFileName(path) + " is " + Article(kind) + kind + ". MinGent can read its text with read_file but can't create or change this kind of file. Write the result as a .docx, .pdf, .pptx or .xlsx file, or as a text file such as .md, .csv or .html.";
        }

        static readonly object cacheGate = new object();
        static readonly List<KeyValuePair<string, DocInfo>> cache = new List<KeyValuePair<string, DocInfo>>();

        public static DocInfo Read(string path, CancelToken ct, out string error)
        {
            error = null;
            FileInfo fi;
            try { fi = new FileInfo(path); } catch (Exception ex) { error = ex.Message; return null; }
            if (!fi.Exists) { error = "file not found: " + path; return null; }
            if (fi.Length > MaxFile) { error = "the document is too large to read (" + ModelCatalog.Size(fi.Length) + ")"; return null; }
            string key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            lock (cacheGate)
                foreach (KeyValuePair<string, DocInfo> kv in cache)
                    if (kv.Key == key) return kv.Value;
            DocInfo d = new DocInfo();
            d.Kind = KindName(path);
            try
            {
                byte[] data = File.ReadAllBytes(fi.FullName);
                string ext = fi.Extension.ToLowerInvariant();
                StringBuilder sb = new StringBuilder();
                if (ext == ".pdf")
                {
                    int pages;
                    string err = PdfText.Extract(data, sb, MaxChars, ct, out pages);
                    d.Pages = pages;
                    if (err != null && sb.Length == 0) { error = err; return null; }
                }
                else if (ext == ".rtf") Rtf(data, sb);
                else
                {
                    using (MemoryStream ms = new MemoryStream(data))
                    using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Read))
                    {
                        Budget b = new Budget();
                        if (ext.StartsWith(".doc") || ext == ".dotx") Docx(zip, sb, b);
                        else if (ext.StartsWith(".xl")) Xlsx(zip, sb, b, ct);
                        else if (ext.StartsWith(".pp")) Pptx(zip, sb, b, ct);
                        else if (ext == ".epub") Epub(zip, sb, b, ct);
                        else Odf(zip, sb, b, ext);
                    }
                }
                if (sb.Length > MaxChars) { sb.Length = MaxChars; d.Cut = true; }
                d.Text = Tidy(sb.ToString());
                d.NoText = d.Text.Trim().Length == 0;
            }
            catch (LimitException ex) { error = ex.Message; return null; }
            catch (InvalidDataException) { error = "the file is damaged or is not really " + Article(d.Kind) + d.Kind; return null; }
            catch (XmlException) { error = "the " + d.Kind + " contains XML MinGent can't read safely"; return null; }
            catch (Exception ex) { error = "the " + d.Kind + " could not be read (" + ex.Message + ")"; return null; }
            lock (cacheGate)
            {
                cache.Insert(0, new KeyValuePair<string, DocInfo>(key, d));
                if (cache.Count > 4) cache.RemoveAt(cache.Count - 1);
            }
            return d;
        }

        static string Tidy(string s)
        {
            s = s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\u00A0", " ");
            s = Regex.Replace(s, "[ \\t]+\\n", "\n");
            s = Regex.Replace(s, "\\n{4,}", "\n\n\n");
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\n' || c == '\t' || c >= ' ') sb.Append(c);
            }
            return sb.ToString().Trim('\n') + "\n";
        }

        class Budget
        {
            public long Used;
        }

        static byte[] Entry(ZipArchive zip, string name, Budget b)
        {
            ZipArchiveEntry e = zip.GetEntry(name);
            if (e == null)
            {
                foreach (ZipArchiveEntry x in zip.Entries)
                    if (string.Equals(x.FullName, name, StringComparison.OrdinalIgnoreCase)) { e = x; break; }
            }
            if (e == null) return null;
            using (Stream s = e.Open())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[65536];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    b.Used += n;
                    if (ms.Length > MaxPart || b.Used > MaxTotal) throw new LimitException("the file unpacks to more data than MinGent reads");
                }
                return ms.ToArray();
            }
        }

        static XmlReaderSettings Safe()
        {
            XmlReaderSettings st = new XmlReaderSettings();
            st.DtdProcessing = DtdProcessing.Prohibit;
            st.XmlResolver = null;
            st.MaxCharactersFromEntities = 0;
            st.IgnoreComments = true;
            st.IgnoreProcessingInstructions = true;
            return st;
        }

        static XmlDocument Load(byte[] data)
        {
            if (data == null) return null;
            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;
            using (MemoryStream ms = new MemoryStream(data))
            using (XmlReader r = XmlReader.Create(ms, Safe()))
                doc.Load(r);
            return doc;
        }

        static string Attr(XmlNode n, string local)
        {
            if (n == null || n.Attributes == null) return null;
            foreach (XmlAttribute a in n.Attributes) if (a.LocalName == local) return a.Value;
            return null;
        }

        static XmlNode Child(XmlNode n, string local)
        {
            if (n == null) return null;
            foreach (XmlNode c in n.ChildNodes) if (c.LocalName == local) return c;
            return null;
        }

        static string ResolveTarget(string baseDir, string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            if (target.StartsWith("/")) return target.TrimStart('/');
            List<string> parts = new List<string>(baseDir.Length > 0 ? baseDir.Split('/') : new string[0]);
            foreach (string p in target.Split('/'))
            {
                if (p == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else if (p != "." && p.Length > 0) parts.Add(p);
            }
            return string.Join("/", parts.ToArray());
        }

        static Dictionary<string, string> Rels(ZipArchive zip, string part, Budget b, Dictionary<string, string> types)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            int slash = part.LastIndexOf('/');
            string dir = slash >= 0 ? part.Substring(0, slash) : "";
            string file = slash >= 0 ? part.Substring(slash + 1) : part;
            XmlDocument d = Load(Entry(zip, (dir.Length > 0 ? dir + "/" : "") + "_rels/" + file + ".rels", b));
            if (d == null) return map;
            foreach (XmlNode n in d.GetElementsByTagName("*"))
            {
                if (n.LocalName != "Relationship") continue;
                string id = Attr(n, "Id"), target = Attr(n, "Target"), mode = Attr(n, "TargetMode");
                if (id == null || target == null || mode == "External") continue;
                map[id] = ResolveTarget(dir, target);
                if (types != null) types[id] = Attr(n, "Type") ?? "";
            }
            return map;
        }

        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        class WordCtx
        {
            public readonly Dictionary<string, string> Names = new Dictionary<string, string>();
            public readonly Dictionary<string, string> BasedOn = new Dictionary<string, string>();
            public readonly Dictionary<string, string> StyleNum = new Dictionary<string, string>();
            public readonly Dictionary<string, int> StyleLevel = new Dictionary<string, int>();
            public readonly Dictionary<string, string> NumToAbstract = new Dictionary<string, string>();
            public readonly Dictionary<string, string[]> Formats = new Dictionary<string, string[]>();
            public readonly Dictionary<string, int[]> Counters = new Dictionary<string, int[]>();
            public Dictionary<string, string> Links = new Dictionary<string, string>();
            public bool HasNumbering;
        }

        static string Val(XmlNode n, string child)
        {
            XmlNode c = n != null ? Child(n, child) : null;
            return c != null ? Attr(c, "val") : null;
        }

        static Dictionary<string, string> Links(ZipArchive zip, string part, Budget b)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            int slash = part.LastIndexOf('/');
            string dir = slash >= 0 ? part.Substring(0, slash) : "";
            string file = slash >= 0 ? part.Substring(slash + 1) : part;
            XmlDocument d = Load(Entry(zip, (dir.Length > 0 ? dir + "/" : "") + "_rels/" + file + ".rels", b));
            if (d == null) return map;
            foreach (XmlNode n in d.GetElementsByTagName("*"))
            {
                if (n.LocalName != "Relationship" || Attr(n, "TargetMode") != "External") continue;
                string id = Attr(n, "Id"), target = Attr(n, "Target"), type = Attr(n, "Type") ?? "";
                if (id != null && target != null && type.EndsWith("/hyperlink") && (target.StartsWith("http://") || target.StartsWith("https://") || target.StartsWith("mailto:")) && target.Length < 500)
                    map[id] = target;
            }
            return map;
        }

        static void Docx(ZipArchive zip, StringBuilder sb, Budget b)
        {
            WordCtx ctx = new WordCtx();
            XmlDocument styles = Load(Entry(zip, "word/styles.xml", b));
            if (styles != null)
                foreach (XmlNode n in styles.GetElementsByTagName("style", W))
                {
                    string id = Attr(n, "styleId");
                    if (id == null) continue;
                    ctx.Names[id] = (Val(n, "name") ?? "").ToLowerInvariant();
                    string based = Val(n, "basedOn");
                    if (based != null) ctx.BasedOn[id] = based;
                    XmlNode numPr = Child(n, "pPr") != null ? Child(Child(n, "pPr"), "numPr") : null;
                    if (numPr == null) continue;
                    string numId = Val(numPr, "numId");
                    int level;
                    int.TryParse(Val(numPr, "ilvl") ?? "0", out level);
                    if (numId != null) { ctx.StyleNum[id] = numId; ctx.StyleLevel[id] = level; }
                }
            XmlDocument numbering = Load(Entry(zip, "word/numbering.xml", b));
            if (numbering != null)
            {
                ctx.HasNumbering = true;
                foreach (XmlNode n in numbering.GetElementsByTagName("abstractNum", W))
                {
                    string aid = Attr(n, "abstractNumId");
                    if (aid == null) continue;
                    string[] fmts = new string[9];
                    foreach (XmlNode lvl in n.ChildNodes)
                    {
                        if (lvl.LocalName != "lvl") continue;
                        int i;
                        if (int.TryParse(Attr(lvl, "ilvl") ?? "", out i) && i >= 0 && i < 9) fmts[i] = Val(lvl, "numFmt") ?? "decimal";
                    }
                    ctx.Formats[aid] = fmts;
                }
                foreach (XmlNode n in numbering.GetElementsByTagName("num", W))
                {
                    string nid = Attr(n, "numId"), aid = Val(n, "abstractNumId");
                    if (nid != null && aid != null) ctx.NumToAbstract[nid] = aid;
                }
            }
            ctx.Links = Links(zip, "word/document.xml", b);
            XmlDocument doc = Load(Entry(zip, "word/document.xml", b));
            if (doc == null) throw new InvalidDataException("no document part");
            XmlNode body = null;
            foreach (XmlNode n in doc.GetElementsByTagName("body", W)) { body = n; break; }
            if (body == null) return;
            Dictionary<string, string> types = new Dictionary<string, string>();
            Dictionary<string, string> parts = Rels(zip, "word/document.xml", b, types);
            List<string> headers = new List<string>(), footers = new List<string>();
            foreach (KeyValuePair<string, string> kv in parts)
            {
                string type = types.ContainsKey(kv.Key) ? types[kv.Key] : "";
                bool header = type.EndsWith("/header");
                if (!header && !type.EndsWith("/footer")) continue;
                XmlDocument hf = Load(Entry(zip, kv.Value, b));
                if (hf == null) continue;
                string t = Squash(WordPara(hf.DocumentElement, null, null));
                List<string> into = header ? headers : footers;
                if (t.Length > 0 && !into.Contains(t) && into.Count < 4) into.Add(t);
            }
            foreach (string h in headers) sb.Append("(Page header: ").Append(h).Append(")\n");
            if (headers.Count > 0) sb.Append('\n');
            WordBlocks(body, sb, ctx, 0);
            Notes(zip, b, "word/footnotes.xml", "footnote", "Footnotes", sb);
            Notes(zip, b, "word/endnotes.xml", "endnote", "Endnotes", sb);
            XmlDocument comments = Load(Entry(zip, "word/comments.xml", b));
            if (comments != null)
            {
                bool first = true;
                foreach (XmlNode n in comments.GetElementsByTagName("comment", W))
                {
                    string t = Squash(WordPara(n, null, null));
                    if (t.Length == 0) continue;
                    if (first) { sb.Append('\n'); first = false; }
                    string who = Attr(n, "author") ?? "";
                    sb.Append("(Comment").Append(who.Length > 0 ? " by " + who : "").Append(": ").Append(t).Append(")\n");
                    if (sb.Length > MaxChars) break;
                }
            }
            foreach (string f in footers) sb.Append("\n(Page footer: ").Append(f).Append(")");
            if (footers.Count > 0) sb.Append('\n');
        }

        static string Squash(string s)
        {
            return Regex.Replace(s ?? "", "\\s+", " ").Trim();
        }

        static void Notes(ZipArchive zip, Budget b, string part, string tag, string title, StringBuilder sb)
        {
            XmlDocument notes = Load(Entry(zip, part, b));
            if (notes == null) return;
            StringBuilder fn = new StringBuilder();
            int n = 0;
            foreach (XmlNode node in notes.GetElementsByTagName(tag, W))
            {
                string type = Attr(node, "type") ?? "";
                if (type == "separator" || type == "continuationSeparator" || type == "continuationNotice") continue;
                string t = Squash(WordPara(node, null, null));
                if (t.Length > 0) fn.Append(++n).Append(". ").Append(t).Append('\n');
            }
            if (fn.Length > 0) sb.Append('\n').Append(title).Append(":\n").Append(fn);
        }

        static void WordBlocks(XmlNode parent, StringBuilder sb, WordCtx ctx, int depth)
        {
            if (depth > 30) return;
            foreach (XmlNode n in parent.ChildNodes)
            {
                if (sb.Length > MaxChars) return;
                if (n.NamespaceURI != W) continue;
                switch (n.LocalName)
                {
                    case "p": WordParagraph(n, sb, ctx); break;
                    case "tbl": WordTable(n, sb, ctx, depth); break;
                    case "sdt": { XmlNode c = Child(n, "sdtContent"); if (c != null) WordBlocks(c, sb, ctx, depth + 1); break; }
                    case "customXml": case "smartTag": case "ins": WordBlocks(n, sb, ctx, depth + 1); break;
                }
            }
        }

        static string ListPrefix(XmlNode ppr, string styleId, WordCtx ctx)
        {
            XmlNode numPr = ppr != null ? Child(ppr, "numPr") : null;
            string numId = Val(numPr, "numId");
            int level = 0;
            if (numPr != null) int.TryParse(Val(numPr, "ilvl") ?? "0", out level);
            string sid = styleId;
            for (int guard = 0; numId == null && sid != null && guard < 8; guard++)
            {
                if (ctx.StyleNum.TryGetValue(sid, out numId)) { if (numPr == null || Val(numPr, "ilvl") == null) level = ctx.StyleLevel[sid]; break; }
                string next;
                sid = ctx.BasedOn.TryGetValue(sid, out next) ? next : null;
            }
            if (numId == null || numId == "0") return null;
            level = Math.Max(0, Math.Min(8, level));
            string fmt = "bullet";
            string aid;
            string[] fmts;
            if (ctx.HasNumbering && ctx.NumToAbstract.TryGetValue(numId, out aid) && ctx.Formats.TryGetValue(aid, out fmts)) fmt = fmts[level] ?? "decimal";
            int[] counts;
            if (!ctx.Counters.TryGetValue(numId, out counts)) ctx.Counters[numId] = counts = new int[9];
            counts[level]++;
            for (int i = level + 1; i < 9; i++) counts[i] = 0;
            string indent = new string(' ', level * 2);
            switch (fmt)
            {
                case "bullet": return indent + "- ";
                case "none": return indent;
                case "lowerLetter": return indent + Letters(counts[level], false) + ". ";
                case "upperLetter": return indent + Letters(counts[level], true) + ". ";
                case "lowerRoman": return indent + Roman(counts[level]).ToLowerInvariant() + ". ";
                case "upperRoman": return indent + Roman(counts[level]) + ". ";
            }
            return indent + counts[level] + ". ";
        }

        static string Letters(int n, bool upper)
        {
            string s = "";
            while (n > 0 && s.Length < 4) { n--; s = (char)((upper ? 'A' : 'a') + n % 26) + s; n /= 26; }
            return s;
        }

        static string Roman(int n)
        {
            if (n <= 0 || n >= 4000) return n.ToString(CultureInfo.InvariantCulture);
            string[] r = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(r[i]); n -= v[i]; }
            return sb.ToString();
        }

        static void WordParagraph(XmlNode p, StringBuilder sb, WordCtx ctx)
        {
            string text = WordPara(p, null, ctx);
            XmlNode ppr = Child(p, "pPr");
            string sid = Val(ppr, "pStyle") ?? "";
            string name;
            if (!ctx.Names.TryGetValue(sid, out name)) name = sid.ToLowerInvariant();
            string prefix = "";
            Match h = Regex.Match(name, "^heading ?(\\d)$");
            if (h.Success) prefix = new string('#', Math.Max(1, Math.Min(6, int.Parse(h.Groups[1].Value)))) + " ";
            else if (name == "title") prefix = "# ";
            else if (name == "subtitle") prefix = "## ";
            if (text.Trim().Length == 0) { sb.Append('\n'); return; }
            if (prefix.Length == 0) prefix = ListPrefix(ppr, sid.Length > 0 ? sid : null, ctx) ?? "";
            if (prefix.StartsWith("#") && sb.Length > 0 && !sb.ToString(Math.Max(0, sb.Length - 2), Math.Min(2, sb.Length)).EndsWith("\n\n")) sb.Append('\n');
            sb.Append(prefix).Append(text.TrimEnd()).Append('\n');
        }

        static string WordPara(XmlNode p, StringBuilder into, WordCtx ctx)
        {
            StringBuilder sb = into ?? new StringBuilder();
            WordRuns(p, sb, ctx, 0);
            return sb.ToString();
        }

        const string MC = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        static void WordRuns(XmlNode n, StringBuilder sb, WordCtx ctx, int depth)
        {
            if (depth > 40) return;
            foreach (XmlNode c in n.ChildNodes)
            {
                if (c.NodeType != XmlNodeType.Element) continue;
                if (c.NamespaceURI == MC && c.LocalName == "Fallback") continue;
                if (c.NamespaceURI == W)
                {
                    switch (c.LocalName)
                    {
                        case "t": sb.Append(c.InnerText); continue;
                        case "tab": sb.Append('\t'); continue;
                        case "br": case "cr": sb.Append('\n'); continue;
                        case "noBreakHyphen": sb.Append('-'); continue;
                        case "softHyphen": continue;
                        case "sym":
                            {
                                string ch = Attr(c, "char"), font = Attr(c, "font") ?? "";
                                int code;
                                int kind = font.Contains("Symbol") ? 1 : font.Contains("Wingdings") || font.Contains("Webdings") ? 2 : 0;
                                if (ch != null && kind != 0 && int.TryParse(ch, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code) && (code & 0xFF) >= 0x20)
                                    sb.Append(PdfText.Pua(code & 0xFF, kind) ?? "");
                                continue;
                            }
                        case "delText": case "instrText": case "pPr": case "rPr": case "fldData": case "footnoteReference": case "endnoteReference": case "commentReference": case "del": case "moveFrom": continue;
                        case "hyperlink":
                            {
                                int start = sb.Length;
                                WordRuns(c, sb, ctx, depth + 1);
                                string rid = null;
                                foreach (XmlAttribute a in c.Attributes) if (a.LocalName == "id" && a.NamespaceURI.Contains("relationships")) rid = a.Value;
                                string url;
                                if (rid != null && ctx != null && ctx.Links.TryGetValue(rid, out url))
                                {
                                    string shown = sb.ToString(start, sb.Length - start).Trim();
                                    if (shown.Length > 0 && shown.IndexOf(url.Replace("mailto:", ""), StringComparison.OrdinalIgnoreCase) < 0) sb.Append(" (").Append(url).Append(')');
                                }
                                continue;
                            }
                        case "p":
                            if (depth > 0) { sb.Append(' '); WordRuns(c, sb, ctx, depth + 1); sb.Append(' '); }
                            else WordRuns(c, sb, ctx, depth + 1);
                            continue;
                    }
                }
                WordRuns(c, sb, ctx, depth + 1);
            }
        }

        static void WordTable(XmlNode tbl, StringBuilder sb, WordCtx ctx, int depth)
        {
            List<List<string>> rows = new List<List<string>>();
            foreach (XmlNode tr in tbl.ChildNodes)
            {
                if (tr.LocalName != "tr" || tr.NamespaceURI != W) continue;
                List<string> cells = new List<string>();
                foreach (XmlNode tc in tr.ChildNodes)
                {
                    if (tc.NamespaceURI != W) continue;
                    if (tc.LocalName == "sdt")
                    {
                        XmlNode content = Child(tc, "sdtContent");
                        XmlNode inner = content != null ? Child(content, "tc") : null;
                        if (inner == null) continue;
                        cells.Add(WordCell(inner, ctx, depth));
                        continue;
                    }
                    if (tc.LocalName != "tc") continue;
                    cells.Add(WordCell(tc, ctx, depth));
                }
                rows.Add(cells);
            }
            Table(rows, sb);
        }

        static string WordCell(XmlNode tc, WordCtx ctx, int depth)
        {
            StringBuilder cell = new StringBuilder();
            foreach (XmlNode cp in tc.ChildNodes)
            {
                if (cp.LocalName == "p" && cp.NamespaceURI == W)
                {
                    string t = WordPara(cp, null, ctx).Trim();
                    if (t.Length > 0) cell.Append(cell.Length > 0 ? " " : "").Append(t);
                }
                else if (cp.LocalName == "tbl" && cp.NamespaceURI == W && depth < 4)
                {
                    StringBuilder inner = new StringBuilder();
                    WordTable(cp, inner, ctx, depth + 1);
                    cell.Append(cell.Length > 0 ? " " : "").Append(inner.ToString().Replace("\n", " "));
                }
            }
            return cell.ToString();
        }

        static void Table(List<List<string>> rows, StringBuilder sb)
        {
            if (rows.Count == 0) return;
            int cols = 0;
            foreach (List<string> r in rows) cols = Math.Max(cols, r.Count);
            if (cols == 0) return;
            if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append('|');
                for (int c = 0; c < cols; c++)
                {
                    string v = c < rows[i].Count ? rows[i][c] : "";
                    sb.Append(' ').Append(v.Replace('\n', ' ').Replace("|", "\\|")).Append(" |");
                }
                sb.Append('\n');
                if (i == 0 && rows.Count > 1)
                {
                    sb.Append('|');
                    for (int c = 0; c < cols; c++) sb.Append("---|");
                    sb.Append('\n');
                }
                if (sb.Length > MaxChars) return;
            }
            sb.Append('\n');
        }

        static void Xlsx(ZipArchive zip, StringBuilder sb, Budget b, CancelToken ct)
        {
            List<string> shared = new List<string>();
            byte[] ss = Entry(zip, "xl/sharedStrings.xml", b);
            if (ss != null)
            {
                using (MemoryStream ms = new MemoryStream(ss))
                using (XmlReader r = XmlReader.Create(ms, Safe()))
                {
                    StringBuilder cur = null;
                    bool inRph = false, held = false;
                    while (held || r.Read())
                    {
                        held = false;
                        if (r.NodeType == XmlNodeType.Element)
                        {
                            if (r.LocalName == "si") { cur = new StringBuilder(); if (r.IsEmptyElement) { shared.Add(""); cur = null; } }
                            else if (r.LocalName == "rPh") inRph = !r.IsEmptyElement;
                            else if (r.LocalName == "t" && cur != null && !inRph && !r.IsEmptyElement) { cur.Append(r.ReadElementContentAsString()); held = true; }
                        }
                        else if (r.NodeType == XmlNodeType.EndElement)
                        {
                            if (r.LocalName == "rPh") inRph = false;
                            else if (r.LocalName == "si" && cur != null) { shared.Add(cur.ToString()); cur = null; }
                        }
                    }
                }
            }
            List<bool> dateStyle = new List<bool>();
            XmlDocument st = Load(Entry(zip, "xl/styles.xml", b));
            if (st != null)
            {
                Dictionary<int, bool> customDate = new Dictionary<int, bool>();
                foreach (XmlNode n in st.GetElementsByTagName("*"))
                    if (n.LocalName == "numFmt")
                    {
                        int id;
                        if (int.TryParse(Attr(n, "numFmtId") ?? "", out id)) customDate[id] = LooksDate(Attr(n, "formatCode") ?? "");
                    }
                foreach (XmlNode n in st.GetElementsByTagName("*"))
                {
                    if (n.LocalName != "cellXfs") continue;
                    foreach (XmlNode xf in n.ChildNodes)
                    {
                        if (xf.LocalName != "xf") continue;
                        int id;
                        int.TryParse(Attr(xf, "numFmtId") ?? "0", out id);
                        bool isDate;
                        if (!customDate.TryGetValue(id, out isDate)) isDate = (id >= 14 && id <= 22) || (id >= 45 && id <= 47);
                        dateStyle.Add(isDate);
                    }
                    break;
                }
            }
            XmlDocument wb = Load(Entry(zip, "xl/workbook.xml", b));
            if (wb == null) throw new InvalidDataException("no workbook part");
            bool date1904 = false;
            foreach (XmlNode n in wb.GetElementsByTagName("*")) if (n.LocalName == "workbookPr") date1904 = (Attr(n, "date1904") ?? "") == "1" || (Attr(n, "date1904") ?? "").ToLowerInvariant() == "true";
            Dictionary<string, string> rels = Rels(zip, "xl/workbook.xml", b, null);
            foreach (XmlNode n in wb.GetElementsByTagName("*"))
            {
                if (n.LocalName != "sheet") continue;
                if (ct != null && ct.IsCancelled) return;
                string name = Attr(n, "name") ?? "Sheet";
                string rid = null;
                foreach (XmlAttribute a in n.Attributes) if (a.LocalName == "id" && a.NamespaceURI.Contains("relationships")) rid = a.Value;
                string target;
                if (rid == null || !rels.TryGetValue(rid, out target)) continue;
                byte[] data = Entry(zip, target, b);
                if (data == null) continue;
                sb.Append("## Sheet: ").Append(name).Append('\n');
                Sheet(data, shared, dateStyle, date1904, sb);
                sb.Append('\n');
                if (sb.Length > MaxChars) return;
            }
        }

        static bool LooksDate(string code)
        {
            string c = Regex.Replace(code ?? "", "\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.", "").ToLowerInvariant();
            return Regex.IsMatch(c, "[dy]|m{1,5}.*[ds]|h.*m|mm.*ss");
        }

        static int ColIndex(string cellRef)
        {
            int c = 0, i = 0;
            while (i < cellRef.Length && char.IsLetter(cellRef[i])) { c = c * 26 + (char.ToUpperInvariant(cellRef[i]) - 'A' + 1); i++; }
            return c - 1;
        }

        static void Sheet(byte[] data, List<string> shared, List<bool> dateStyle, bool date1904, StringBuilder sb)
        {
            const int MaxCols = 60, MaxRows = 5000;
            List<List<string>> rows = new List<List<string>>();
            int rowsSeen = 0;
            using (MemoryStream ms = new MemoryStream(data))
            using (XmlReader r = XmlReader.Create(ms, Safe()))
            {
                List<string> row = null;
                string type = null, cref = null, val = null, inline = null, formula = null;
                int style = 0;
                bool held = false;
                while (held || r.Read())
                {
                    held = false;
                    if (r.NodeType == XmlNodeType.Element)
                    {
                        switch (r.LocalName)
                        {
                            case "row":
                                row = new List<string>();
                                if (r.IsEmptyElement) row = null;
                                break;
                            case "c":
                                type = r.GetAttribute("t");
                                cref = r.GetAttribute("r") ?? "";
                                int.TryParse(r.GetAttribute("s") ?? "0", out style);
                                val = null;
                                inline = null;
                                formula = null;
                                if (r.IsEmptyElement) type = cref = null;
                                break;
                            case "f":
                                if (!r.IsEmptyElement && cref != null) { formula = r.ReadElementContentAsString(); held = true; }
                                break;
                            case "v":
                                if (!r.IsEmptyElement && cref != null) { val = r.ReadElementContentAsString(); held = true; }
                                break;
                            case "t":
                                if (!r.IsEmptyElement && cref != null && type == "inlineStr") { inline = (inline ?? "") + r.ReadElementContentAsString(); held = true; }
                                break;
                        }
                    }
                    else if (r.NodeType == XmlNodeType.EndElement)
                    {
                        if (r.LocalName == "c" && row != null && cref != null)
                        {
                            int col = cref.Length > 0 ? ColIndex(cref) : row.Count;
                            if (col >= 0 && col < MaxCols)
                            {
                                string shown = CellText(type, val, inline, style, shared, dateStyle, date1904);
                                if (shown.Length == 0 && !string.IsNullOrEmpty(formula)) shown = "=" + formula;
                                while (row.Count <= col) row.Add("");
                                row[col] = shown;
                            }
                            cref = null;
                        }
                        else if (r.LocalName == "row" && row != null)
                        {
                            bool any = false;
                            foreach (string v in row) if (v.Length > 0) any = true;
                            if (any)
                            {
                                rowsSeen++;
                                if (rows.Count < MaxRows) rows.Add(row);
                            }
                            row = null;
                        }
                    }
                }
            }
            if (rows.Count == 0) { sb.Append("(empty)\n"); return; }
            int lastCol = 0;
            foreach (List<string> rw in rows) for (int i = rw.Count - 1; i >= 0; i--) if (rw[i].Length > 0) { lastCol = Math.Max(lastCol, i); break; }
            foreach (List<string> rw in rows)
            {
                while (rw.Count <= lastCol) rw.Add("");
                if (rw.Count > lastCol + 1) rw.RemoveRange(lastCol + 1, rw.Count - lastCol - 1);
            }
            Table(rows, sb);
            if (rowsSeen > rows.Count) sb.Append("(").Append(rowsSeen - rows.Count).Append(" more rows not shown)\n");
        }

        static string CellText(string type, string val, string inline, int style, List<string> shared, List<bool> dateStyle, bool date1904)
        {
            if (type == "inlineStr") return inline ?? "";
            if (val == null) return "";
            switch (type)
            {
                case "s":
                    {
                        int i;
                        return int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out i) && i >= 0 && i < shared.Count ? shared[i] : "";
                    }
                case "b": return val == "1" ? "TRUE" : "FALSE";
                case "e": case "str": return val;
            }
            double d;
            if (style >= 0 && style < dateStyle.Count && dateStyle[style] && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d > 0 && d < 2958466)
            {
                try
                {
                    DateTime t = date1904 ? new DateTime(1904, 1, 1).AddDays(d) : new DateTime(1899, 12, 30).AddDays(d);
                    return t.TimeOfDay.TotalSeconds < 1 ? t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d < 1 ? t.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                }
                catch { }
            }
            return val;
        }

        const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";

        static void Pptx(ZipArchive zip, StringBuilder sb, Budget b, CancelToken ct)
        {
            XmlDocument pres = Load(Entry(zip, "ppt/presentation.xml", b));
            if (pres == null) throw new InvalidDataException("no presentation part");
            Dictionary<string, string> rels = Rels(zip, "ppt/presentation.xml", b, null);
            int n = 0;
            foreach (XmlNode s in pres.GetElementsByTagName("*"))
            {
                if (s.LocalName != "sldId") continue;
                if (ct != null && ct.IsCancelled) return;
                string rid = null;
                foreach (XmlAttribute a in s.Attributes) if (a.LocalName == "id" && a.NamespaceURI.Contains("relationships")) rid = a.Value;
                string target;
                if (rid == null || !rels.TryGetValue(rid, out target)) continue;
                n++;
                XmlDocument slide = Load(Entry(zip, target, b));
                if (slide == null) continue;
                sb.Append("## Slide ").Append(n).Append(Attr(slide.DocumentElement, "show") == "0" ? " (hidden)" : "").Append('\n');
                DrawingText(slide.DocumentElement, sb, 0);
                Dictionary<string, string> types = new Dictionary<string, string>();
                Dictionary<string, string> srels = Rels(zip, target, b, types);
                foreach (KeyValuePair<string, string> kv in srels)
                {
                    string ty;
                    if (!types.TryGetValue(kv.Key, out ty)) continue;
                    if (ty.EndsWith("/diagramData"))
                    {
                        XmlDocument dgm = Load(Entry(zip, kv.Value, b));
                        if (dgm != null) DrawingText(dgm.DocumentElement, sb, 0);
                        continue;
                    }
                    if (!ty.EndsWith("/notesSlide")) continue;
                    XmlDocument notes = Load(Entry(zip, kv.Value, b));
                    if (notes == null) continue;
                    StringBuilder nb = new StringBuilder();
                    foreach (XmlNode sp in notes.GetElementsByTagName("*"))
                    {
                        if (sp.LocalName != "sp") continue;
                        bool body = false;
                        foreach (XmlNode ph in sp.SelectNodes(".//*[local-name()='ph']")) if ((Attr(ph, "type") ?? "body") == "body") body = true;
                        if (body) DrawingText(sp, nb, 0);
                    }
                    if (nb.ToString().Trim().Length > 0) sb.Append("Notes: ").Append(nb.ToString().Trim().Replace("\n", " ")).Append('\n');
                }
                sb.Append('\n');
                if (sb.Length > MaxChars) return;
            }
        }

        static void DrawingText(XmlNode n, StringBuilder sb, int depth)
        {
            if (n == null || depth > 60) return;
            foreach (XmlNode c in n.ChildNodes)
            {
                if (c.NodeType != XmlNodeType.Element) continue;
                if (c.NamespaceURI == MC && c.LocalName == "Fallback") continue;
                if (c.NamespaceURI == A && c.LocalName == "p")
                {
                    StringBuilder line = new StringBuilder();
                    foreach (XmlNode t in c.SelectNodes(".//*[local-name()='t' or local-name()='br']"))
                    {
                        if (t.LocalName == "br") line.Append('\n');
                        else line.Append(t.InnerText);
                    }
                    string s = line.ToString().Trim();
                    if (s.Length > 0) sb.Append(s).Append('\n');
                    continue;
                }
                if (c.NamespaceURI == A && c.LocalName == "tbl")
                {
                    List<List<string>> rows = new List<List<string>>();
                    foreach (XmlNode tr in c.ChildNodes)
                    {
                        if (tr.LocalName != "tr") continue;
                        List<string> cells = new List<string>();
                        foreach (XmlNode tc in tr.ChildNodes)
                        {
                            if (tc.LocalName != "tc") continue;
                            StringBuilder cell = new StringBuilder();
                            DrawingText(tc, cell, depth + 1);
                            cells.Add(cell.ToString().Trim().Replace('\n', ' '));
                        }
                        rows.Add(cells);
                    }
                    Table(rows, sb);
                    continue;
                }
                DrawingText(c, sb, depth + 1);
            }
        }

        const string OText = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        const string OTable = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        const string ODraw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

        static void Odf(ZipArchive zip, StringBuilder sb, Budget b, string ext)
        {
            XmlDocument doc = Load(Entry(zip, "content.xml", b));
            if (doc == null) throw new InvalidDataException("no content part");
            XmlNode body = null;
            foreach (XmlNode n in doc.GetElementsByTagName("*")) if (n.LocalName == "body") { body = n; break; }
            if (body == null) return;
            OdfCtx ctx = new OdfCtx();
            foreach (XmlNode n in doc.GetElementsByTagName("*"))
            {
                if (n.LocalName != "style" || n.ParentNode == null || n.ParentNode.LocalName != "automatic-styles") continue;
                string name = Attr(n, "name"), parent = Attr(n, "parent-style-name");
                if (name != null && parent != null) ctx.Parents[name] = parent;
            }
            OdfBlocks(body, sb, 0, 0, ctx);
        }

        class OdfCtx
        {
            public readonly Dictionary<string, string> Parents = new Dictionary<string, string>();
            public int Slide;
        }

        static string OdfStyle(XmlNode n, OdfCtx ctx)
        {
            string style = Attr(n, "style-name") ?? "";
            string parent;
            if (ctx.Parents.TryGetValue(style, out parent)) style = parent;
            return style;
        }

        static void OdfBlocks(XmlNode parent, StringBuilder sb, int depth, int listLevel, OdfCtx ctx)
        {
            if (depth > 40) return;
            foreach (XmlNode n in parent.ChildNodes)
            {
                if (n.NodeType != XmlNodeType.Element || sb.Length > MaxChars) continue;
                if (n.NamespaceURI == OText && (n.LocalName == "p" || n.LocalName == "h"))
                {
                    string t = OdfInline(n).Trim();
                    if (n.LocalName == "h")
                    {
                        int lvl;
                        int.TryParse(Attr(n, "outline-level") ?? "1", out lvl);
                        sb.Append('\n').Append(new string('#', Math.Max(1, Math.Min(6, lvl)))).Append(' ').Append(t).Append('\n');
                    }
                    else if (listLevel > 0) sb.Append(new string(' ', (listLevel - 1) * 2)).Append("- ").Append(t).Append('\n');
                    else
                    {
                        string style = OdfStyle(n, ctx);
                        if (style == "Title" && t.Length > 0) sb.Append("# ");
                        else if (style == "Subtitle" && t.Length > 0) sb.Append("## ");
                        sb.Append(t).Append('\n');
                    }
                    continue;
                }
                if (n.NamespaceURI == OText && n.LocalName == "list") { OdfBlocks(n, sb, depth + 1, listLevel + 1, ctx); continue; }
                if (n.LocalName == "page-thumbnail") continue;
                if (n.LocalName == "frame")
                {
                    string cls = null;
                    foreach (XmlAttribute a in n.Attributes) if (a.LocalName == "class" && a.NamespaceURI.Contains("presentation")) cls = a.Value;
                    if (cls == "page-number" || cls == "date-time" || cls == "page") continue;
                }
                if (n.LocalName == "notes" && n.NamespaceURI.Contains("presentation"))
                {
                    StringBuilder notes = new StringBuilder();
                    OdfBlocks(n, notes, depth + 1, 0, ctx);
                    string t = Squash(notes.ToString());
                    if (t.Length > 0) sb.Append("Notes: ").Append(t).Append('\n');
                    continue;
                }
                if (n.NamespaceURI == OTable && n.LocalName == "table")
                {
                    string name = Attr(n, "name");
                    if (!string.IsNullOrEmpty(name) && parent.LocalName == "spreadsheet") sb.Append("## Sheet: ").Append(name).Append('\n');
                    OdfTable(n, sb);
                    continue;
                }
                if (n.NamespaceURI == ODraw && n.LocalName == "page")
                {
                    sb.Append("## Slide ").Append(++ctx.Slide).Append('\n');
                    OdfBlocks(n, sb, depth + 1, 0, ctx);
                    sb.Append('\n');
                    continue;
                }
                OdfBlocks(n, sb, depth + 1, listLevel, ctx);
            }
        }

        static string OdfInline(XmlNode n)
        {
            StringBuilder sb = new StringBuilder();
            OdfInline(n, sb, 0);
            return sb.ToString();
        }

        static void OdfInline(XmlNode n, StringBuilder sb, int depth)
        {
            if (depth > 40) return;
            foreach (XmlNode c in n.ChildNodes)
            {
                if (c.NodeType == XmlNodeType.Text || c.NodeType == XmlNodeType.SignificantWhitespace || c.NodeType == XmlNodeType.Whitespace) { sb.Append(c.Value); continue; }
                if (c.NodeType != XmlNodeType.Element) continue;
                if (c.NamespaceURI == OText)
                {
                    if (c.LocalName == "s")
                    {
                        int count;
                        if (!int.TryParse(Attr(c, "c") ?? "1", out count)) count = 1;
                        sb.Append(' ', Math.Max(1, Math.Min(count, 40)));
                        continue;
                    }
                    if (c.LocalName == "tab") { sb.Append('\t'); continue; }
                    if (c.LocalName == "line-break") { sb.Append('\n'); continue; }
                    if (c.LocalName == "note" || c.LocalName == "tracked-changes" || c.LocalName == "bookmark-ref") continue;
                }
                OdfInline(c, sb, depth + 1);
            }
        }

        static void OdfTable(XmlNode table, StringBuilder sb)
        {
            List<List<string>> rows = new List<List<string>>();
            foreach (XmlNode tr in table.SelectNodes(".//*[local-name()='table-row']"))
            {
                int rr;
                if (!int.TryParse(Attr(tr, "number-rows-repeated") ?? "1", out rr) || rr < 1) rr = 1;
                List<string> cells = new List<string>();
                foreach (XmlNode tc in tr.ChildNodes)
                {
                    if (tc.LocalName != "table-cell" && tc.LocalName != "covered-table-cell") continue;
                    int cr;
                    if (!int.TryParse(Attr(tc, "number-columns-repeated") ?? "1", out cr) || cr < 1) cr = 1;
                    StringBuilder cell = new StringBuilder();
                    foreach (XmlNode p in tc.ChildNodes)
                        if (p.LocalName == "p") { string t = OdfInline(p).Trim(); if (t.Length > 0) cell.Append(cell.Length > 0 ? " " : "").Append(t); }
                    string v = cell.ToString();
                    for (int i = 0; i < Math.Min(cr, 60) && cells.Count < 60; i++) cells.Add(v);
                }
                int last = cells.Count - 1;
                while (last >= 0 && cells[last].Length == 0) last--;
                if (last < 0) continue;
                cells.RemoveRange(last + 1, cells.Count - last - 1);
                for (int i = 0; i < Math.Min(rr, 50) && rows.Count < 5000; i++) rows.Add(new List<string>(cells));
            }
            Table(rows, sb);
        }

        static void Epub(ZipArchive zip, StringBuilder sb, Budget b, CancelToken ct)
        {
            XmlDocument container = Load(Entry(zip, "META-INF/container.xml", b));
            string opf = null;
            if (container != null)
                foreach (XmlNode n in container.GetElementsByTagName("*"))
                    if (n.LocalName == "rootfile") { opf = Attr(n, "full-path"); break; }
            if (opf == null) throw new InvalidDataException("no package file");
            XmlDocument pkg = Load(Entry(zip, opf, b));
            if (pkg == null) throw new InvalidDataException("no package file");
            int slash = opf.LastIndexOf('/');
            string dir = slash >= 0 ? opf.Substring(0, slash) : "";
            Dictionary<string, string> items = new Dictionary<string, string>();
            foreach (XmlNode n in pkg.GetElementsByTagName("*"))
                if (n.LocalName == "item") { string id = Attr(n, "id"), href = Attr(n, "href"); if (id != null && href != null) items[id] = ResolveTarget(dir, Uri.UnescapeDataString(href)); }
            foreach (XmlNode n in pkg.GetElementsByTagName("*"))
            {
                if (n.LocalName != "itemref") continue;
                if (ct != null && ct.IsCancelled) return;
                string target;
                if (!items.TryGetValue(Attr(n, "idref") ?? "", out target)) continue;
                byte[] html = Entry(zip, target, b);
                if (html == null) continue;
                string text = Web.HtmlToText(Encoding.UTF8.GetString(html)).Trim();
                if (text.Length == 0) continue;
                sb.Append(text).Append("\n\n");
                if (sb.Length > MaxChars) return;
            }
        }

        static void Rtf(byte[] data, StringBuilder sb)
        {
            string s = Encoding.GetEncoding(1252).GetString(data);
            if (!s.StartsWith("{\\rtf")) throw new InvalidDataException("not RTF");
            int i = 0, n = s.Length;
            Stack<bool> skip = new Stack<bool>();
            bool skipping = false;
            int uc = 1, pendingSkip = 0;
            Encoding enc = Encoding.GetEncoding(1252);
            List<byte> bytes = new List<byte>();
            Action flush = delegate
            {
                if (bytes.Count == 0) return;
                if (!skipping) sb.Append(enc.GetString(bytes.ToArray()));
                bytes.Clear();
            };
            while (i < n && sb.Length < MaxChars)
            {
                char c = s[i];
                if (c == '{') { flush(); skip.Push(skipping); i++; if (i + 1 < n && s[i] == '\\' && s[i + 1] == '*') skipping = true; continue; }
                if (c == '}') { flush(); skipping = skip.Count > 0 ? skip.Pop() : false; i++; continue; }
                if (c == '\\' && i + 1 < n)
                {
                    char d = s[i + 1];
                    if (d == '\'' && i + 3 < n)
                    {
                        int v;
                        if (int.TryParse(s.Substring(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                        {
                            if (pendingSkip > 0) pendingSkip--;
                            else bytes.Add((byte)v);
                        }
                        i += 4;
                        continue;
                    }
                    if (!char.IsLetter(d))
                    {
                        flush();
                        if (!skipping)
                        {
                            if (d == '\\' || d == '{' || d == '}') sb.Append(d);
                            else if (d == '~') sb.Append(' ');
                            else if (d == '_') sb.Append('-');
                        }
                        i += 2;
                        continue;
                    }
                    int j = i + 1;
                    while (j < n && char.IsLetter(s[j])) j++;
                    string word = s.Substring(i + 1, j - i - 1);
                    int k = j;
                    if (k < n && (s[k] == '-' || char.IsDigit(s[k]))) { k++; while (k < n && char.IsDigit(s[k])) k++; }
                    string arg = s.Substring(j, k - j);
                    if (k < n && s[k] == ' ') k++;
                    i = k;
                    flush();
                    int num;
                    int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out num);
                    switch (word)
                    {
                        case "fonttbl": case "colortbl": case "stylesheet": case "info": case "pict": case "object": case "header": case "footer":
                        case "headerl": case "headerr": case "headerf": case "footerl": case "footerr": case "footerf": case "listtable": case "listoverridetable":
                        case "rsidtbl": case "generator": case "xmlnstbl": case "themedata": case "colorschememapping": case "latentstyles": case "datastore":
                            skipping = true; break;
                        case "par": case "line": case "sect": case "page": if (!skipping) sb.Append('\n'); break;
                        case "row": if (!skipping) sb.Append('\n'); break;
                        case "trowd": if (!skipping) { if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n'); sb.Append("| "); } break;
                        case "cell": if (!skipping) sb.Append(" | "); break;
                        case "tab": if (!skipping) sb.Append('\t'); break;
                        case "emdash": if (!skipping) sb.Append("\u2014"); break;
                        case "endash": if (!skipping) sb.Append("\u2013"); break;
                        case "bullet": if (!skipping) sb.Append("\u2022"); break;
                        case "lquote": if (!skipping) sb.Append('\u2018'); break;
                        case "rquote": if (!skipping) sb.Append('\u2019'); break;
                        case "ldblquote": if (!skipping) sb.Append('\u201C'); break;
                        case "rdblquote": if (!skipping) sb.Append('\u201D'); break;
                        case "uc": uc = Math.Max(0, Math.Min(4, num)); break;
                        case "u":
                            if (!skipping) sb.Append((char)(num < 0 ? num + 65536 : num));
                            pendingSkip = uc;
                            break;
                        case "ansicpg":
                            try { enc = Encoding.GetEncoding(num); } catch { }
                            break;
                    }
                    continue;
                }
                if (c == '\r' || c == '\n') { i++; continue; }
                if (pendingSkip > 0) { pendingSkip--; i++; continue; }
                bytes.Add((byte)(c & 0xFF));
                i++;
            }
            flush();
        }
    }
}
