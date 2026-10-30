using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGent
{
    static class DocExport
    {
        static readonly string[] Exts = { ".docx", ".xlsx", ".pptx", ".pdf" };
        public const long MaxOutput = 256L * 1024 * 1024;

        public class Result
        {
            public byte[] Data;
            public string Summary = "";
            public string Error;
            public readonly List<string> Problems = new List<string>();
        }

        public static bool CanWrite(string path)
        {
            return Array.IndexOf(Exts, (Path.GetExtension(path ?? "") ?? "").ToLowerInvariant()) >= 0;
        }

        static readonly Regex DataUri = new Regex(@"^data:image/(png|jpe?g|gif|bmp|webp);base64,([A-Za-z0-9+/=\s]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public const long MaxPictures = 192L * 1024 * 1024;

        public static DocPictureLoader Loader(ToolContext ctx, string docPath)
        {
            DocPictureLoader load = RawLoader(ctx, docPath);
            Dictionary<string, KeyValuePair<DocPicture, string>> seen = new Dictionary<string, KeyValuePair<DocPicture, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, DocPicture> byHash = new Dictionary<string, DocPicture>(StringComparer.Ordinal);
            long[] total = new long[1];
            return delegate (string src, out string problem)
            {
                string key = (src ?? "").Trim();
                KeyValuePair<DocPicture, string> hit;
                if (seen.TryGetValue(key, out hit)) { problem = hit.Value; return hit.Key; }
                DocPicture p = load(key, out problem);
                if (p != null && p.Data != null)
                {
                    string hash;
                    using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(p.Data));
                    DocPicture same;
                    if (byHash.TryGetValue(hash, out same)) p = same;
                    else if (total[0] + p.Data.Length > MaxPictures)
                    {
                        problem = "the pictures together would be larger than 192 MB, so " + (p.Name.Length > 0 ? p.Name : "a picture") + " was left out";
                        p = null;
                    }
                    else
                    {
                        total[0] += p.Data.Length;
                        byHash[hash] = p;
                    }
                }
                if (seen.Count < 4096) seen[key] = new KeyValuePair<DocPicture, string>(p, problem);
                return p;
            };
        }

        static DocPictureLoader RawLoader(ToolContext ctx, string docPath)
        {
            string dir = Path.GetDirectoryName(docPath) ?? ctx.Workspace;
            return delegate (string src, out string problem)
            {
                problem = null;
                string s = (src ?? "").Trim();
                if (s.Length == 0) return null;
                Match dm = DataUri.Match(s);
                if (dm.Success)
                {
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(Regex.Replace(dm.Groups[2].Value, @"\s+", "")); }
                    catch (FormatException) { problem = "an embedded image is not valid base64"; return null; }
                    if (bytes.Length > DocPictures.MaxBytes) { problem = "an embedded image is larger than 32 MB"; return null; }
                    return DocPictures.FromBytes(bytes, "image", out problem);
                }
                if (Regex.IsMatch(s, @"^(https?|ftp)://", RegexOptions.IgnoreCase))
                {
                    problem = "web images are not downloaded into documents (" + (s.Length > 80 ? s.Substring(0, 80) + "..." : s) + "); save the image to a file first and use its path";
                    return null;
                }
                if (s.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) s = s.Substring(8);
                else if (s.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
                if (s.IndexOf('%') >= 0) { try { s = Uri.UnescapeDataString(s); } catch (Exception) { } }
                string err;
                string full = AgentPaths.Resolve(s, dir, out err);
                if (full == null) { problem = "image path " + src + ": " + err; return null; }
                if (!File.Exists(full))
                {
                    string alt = AgentPaths.Resolve(s, ctx.Workspace, out err);
                    if (alt != null && File.Exists(alt)) full = alt;
                }
                string deny = ctx.CheckRead(full);
                if (deny != null) { problem = deny; return null; }
                return DocPictures.Load(full, out problem);
            };
        }

        public static Result Build(string path, string content, DocPictureLoader loader)
        {
            Result r = new Result();
            string ext = (Path.GetExtension(path ?? "") ?? "").ToLowerInvariant();
            string kind = DocText.KindName(path);
            try
            {
                switch (ext)
                {
                    case ".docx":
                        {
                            DocModel m = DocModel.FromMarkdown(content, loader);
                            r.Problems.AddRange(m.Problems);
                            r.Data = DocxWriter.Build(m);
                            r.Summary = "Word document" + Describe(m);
                            break;
                        }
                    case ".pdf":
                        {
                            DocModel m = DocModel.FromMarkdown(content, loader);
                            r.Problems.AddRange(m.Problems);
                            int pages;
                            r.Data = PdfWriter.Build(m, r.Problems, out pages);
                            if (r.Data == null) { r.Error = "MinGent found no font it can use for the PDF on this PC"; return r; }
                            r.Summary = pages + (pages == 1 ? "-page PDF" : "-page PDF") + Describe(m);
                            break;
                        }
                    case ".pptx":
                        {
                            int slides;
                            string title;
                            r.Data = PptxWriter.Build(content, loader, r.Problems, out slides, out title);
                            r.Summary = "PowerPoint, " + slides + (slides == 1 ? " slide" : " slides");
                            break;
                        }
                    case ".xlsx":
                        {
                            string err;
                            List<XlsxSheet> sheets = XlsxWriter.Parse(content, r.Problems, out err);
                            if (sheets == null) { r.Error = err; return r; }
                            int rows = 0;
                            foreach (XlsxSheet s in sheets) rows += s.Rows.Count;
                            string title = Path.GetFileNameWithoutExtension(path);
                            r.Data = XlsxWriter.Build(sheets, title);
                            r.Summary = "Excel workbook, " + (sheets.Count == 1 ? "1 sheet" : sheets.Count + " sheets") + ", " + rows + (rows == 1 ? " row" : " rows");
                            break;
                        }
                    default:
                        r.Error = DocText.CannotWrite(path);
                        return r;
                }
            }
            catch (Exception ex)
            {
                r.Error = "MinGent could not build the " + kind + " (" + ex.Message + ")";
                r.Data = null;
                return r;
            }
            if (r.Data == null) r.Error = "MinGent could not build the " + kind;
            else if (r.Data.Length > MaxOutput) { r.Error = "the " + kind + " would be larger than 256 MB"; r.Data = null; }
            return r;
        }

        static string Describe(DocModel m)
        {
            int headings = 0, tables = 0, pictures = 0, math = 0, code = 0;
            foreach (DocBlock b in m.Blocks)
            {
                if (b.Kind == DocKind.Heading) headings++;
                else if (b.Kind == DocKind.Table) tables++;
                else if (b.Kind == DocKind.Picture) pictures++;
                else if (b.Kind == DocKind.Math) math++;
                else if (b.Kind == DocKind.Code) code++;
            }
            List<string> parts = new List<string>();
            if (headings > 0) parts.Add(Count(headings, "heading"));
            if (tables > 0) parts.Add(Count(tables, "table"));
            if (pictures > 0) parts.Add(Count(pictures, "picture"));
            if (math > 0) parts.Add(Count(math, "equation"));
            if (code > 0) parts.Add(Count(code, "code block"));
            return parts.Count > 0 ? ": " + string.Join(", ", parts.ToArray()) : "";
        }

        static string Count(int n, string what) { return n.ToString(CultureInfo.InvariantCulture) + " " + what + (n == 1 ? "" : "s"); }
    }
}
