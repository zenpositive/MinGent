using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MinGent
{
    class Attachment
    {
        public string Path, Name = "";
        public long Size = -1;
        public bool Folder;
        public ImageData Image, Preview;

        public bool Picture { get { return Image != null || Preview != null; } }
        public ImageData Shown { get { return Image ?? Preview; } }
    }

    static class Attachments
    {
        public const int MaxFiles = 20, MaxPictures = 6;

        static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".pdf", "PDF" }, { ".doc", "Word document" }, { ".docx", "Word document" }, { ".docm", "Word document" }, { ".dotx", "Word template" },
            { ".odt", "Document" }, { ".rtf", "Document" }, { ".epub", "E-book" },
            { ".xls", "Excel workbook" }, { ".xlsx", "Excel workbook" }, { ".xlsm", "Excel workbook" }, { ".xltx", "Excel template" }, { ".ods", "Spreadsheet" }, { ".csv", "CSV" }, { ".tsv", "TSV" },
            { ".ppt", "PowerPoint" }, { ".pptx", "PowerPoint" }, { ".pptm", "PowerPoint" }, { ".ppsx", "PowerPoint" }, { ".odp", "Presentation" },
            { ".txt", "Text" }, { ".log", "Log" }, { ".md", "Markdown" }, { ".json", "JSON" }, { ".xml", "XML" }, { ".yaml", "YAML" }, { ".yml", "YAML" }, { ".toml", "TOML" }, { ".ini", "Settings" },
            { ".html", "HTML" }, { ".htm", "HTML" }, { ".css", "CSS" }, { ".js", "JavaScript" }, { ".mjs", "JavaScript" }, { ".ts", "TypeScript" }, { ".tsx", "TypeScript" }, { ".jsx", "JavaScript" },
            { ".py", "Python" }, { ".cs", "C#" }, { ".java", "Java" }, { ".go", "Go" }, { ".rs", "Rust" }, { ".c", "C" }, { ".h", "C header" }, { ".cpp", "C++" }, { ".hpp", "C++ header" },
            { ".rb", "Ruby" }, { ".php", "PHP" }, { ".ps1", "PowerShell" }, { ".bat", "Batch file" }, { ".cmd", "Batch file" }, { ".sh", "Shell script" }, { ".sql", "SQL" },
            { ".png", "PNG image" }, { ".jpg", "JPEG image" }, { ".jpeg", "JPEG image" }, { ".gif", "GIF image" }, { ".bmp", "Bitmap image" }, { ".webp", "WebP image" }, { ".svg", "SVG image" }, { ".tif", "TIFF image" }, { ".tiff", "TIFF image" }, { ".ico", "Icon" },
            { ".wav", "Audio" }, { ".mp3", "Audio" }, { ".m4a", "Audio" }, { ".flac", "Audio" }, { ".ogg", "Audio" }, { ".opus", "Audio" }, { ".wma", "Audio" }, { ".aac", "Audio" }, { ".aif", "Audio" }, { ".aiff", "Audio" },
            { ".mp4", "Video" }, { ".mov", "Video" }, { ".mkv", "Video" }, { ".avi", "Video" }, { ".webm", "Video" }, { ".wmv", "Video" },
            { ".zip", "Archive" }, { ".7z", "Archive" }, { ".rar", "Archive" }, { ".tar", "Archive" }, { ".gz", "Archive" },
            { ".exe", "Program" }, { ".msi", "Installer" }, { ".dll", "Library" }
        };

        public static Attachment FromPath(string path, out string error)
        {
            error = null;
            string full;
            try { full = System.IO.Path.GetFullPath((path ?? "").Trim().Trim('"')); }
            catch (Exception) { error = "it is not a valid path"; return null; }
            Attachment a = new Attachment();
            a.Path = full;
            a.Name = System.IO.Path.GetFileName(full.TrimEnd('\\', '/'));
            if (a.Name.Length == 0) a.Name = full;
            if (Directory.Exists(full)) { a.Folder = true; return a; }
            if (!File.Exists(full)) { error = "it is not there anymore"; return null; }
            try { a.Size = new FileInfo(full).Length; } catch (Exception) { }
            return a;
        }

        public static Attachment FromImage(ImageData img, string name)
        {
            Attachment a = new Attachment();
            a.Image = img;
            a.Name = string.IsNullOrEmpty(name) ? "Pasted image" : name;
            return a;
        }

        public static string Kind(Attachment a)
        {
            if (a.Folder) return "Folder";
            if (a.Path == null) return "Picture";
            string ext = System.IO.Path.GetExtension(a.Path) ?? "";
            string k;
            if (Kinds.TryGetValue(ext, out k)) return k;
            return ext.Length > 1 ? ext.Substring(1).ToUpperInvariant() + " file" : "File";
        }

        static readonly HashSet<string> TextKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "Text", "Log", "Markdown", "JSON", "XML", "YAML", "TOML", "Settings", "HTML", "CSS", "JavaScript", "TypeScript", "Python", "C#", "Java", "Go", "Rust",
            "C", "C header", "C++", "C++ header", "Ruby", "PHP", "PowerShell", "Batch file", "Shell script", "SQL", "CSV", "TSV"
        };

        public static bool NotText(string path)
        {
            string ext = System.IO.Path.GetExtension(path ?? "") ?? "";
            string k;
            if (!Kinds.TryGetValue(ext, out k) || TextKinds.Contains(k)) return false;
            return !ext.Equals(".svg", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".rtf", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Opens(Attachment a)
        {
            return a != null && !a.Folder && a.Path != null && TextKinds.Contains(Kind(a));
        }

        public static string Glyph(Attachment a)
        {
            if (a.Folder) return "folder";
            string k = Kind(a);
            if (k == "Audio") return "wave";
            if (k == "Program" || k == "Installer" || k == "Library" || k == "PowerShell" || k == "Batch file" || k == "Shell script") return "terminal";
            return "file";
        }

        public static string SizeText(long n)
        {
            if (n < 0) return "";
            if (n < 1024) return n + (n == 1 ? " byte" : " bytes");
            string[] units = { "KB", "MB", "GB", "TB" };
            double v = n / 1024.0;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return v.ToString(v < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + " " + units[u];
        }

        public static string Meta(Attachment a)
        {
            string k = Kind(a), s = SizeText(a.Size);
            return s.Length > 0 ? k + ", " + s : k;
        }

        public static string Tip(Attachment a)
        {
            return a.Path ?? a.Name;
        }

        public static string Block(List<Attachment> list)
        {
            if (list == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (Attachment a in list)
            {
                if (a.Path == null) continue;
                sb.Append("\n- ").Append(a.Path).Append(" (").Append(a.Folder ? "folder" : Meta(a)).Append(')');
            }
            return sb.Length == 0 ? "" : "Attached files:" + sb.ToString();
        }

        public static string WithBlock(string text, List<Attachment> list)
        {
            string b = Block(list);
            if (b.Length == 0) return text ?? "";
            return string.IsNullOrEmpty(text) ? b : text + "\n\n" + b;
        }

        public static List<ImageData> Images(List<Attachment> list)
        {
            List<ImageData> l = new List<ImageData>();
            if (list != null) foreach (Attachment a in list) if (a.Image != null) l.Add(a.Image);
            return l;
        }

        public static int Pictures(List<Attachment> list)
        {
            int n = 0;
            if (list != null) foreach (Attachment a in list) if (a.Picture) n++;
            return n;
        }

        public static Bitmap Cover(ImageData d, int w, int h)
        {
            if (d == null || w <= 0 || h <= 0) return null;
            try
            {
                using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(d.Base64)))
                using (Image full = Image.FromStream(ms))
                {
                    Bitmap bm = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bm))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        float sc = Math.Max(w / (float)full.Width, h / (float)full.Height);
                        float dw = full.Width * sc, dh = full.Height * sc;
                        g.DrawImage(full, (w - dw) / 2, (h - dh) / 2, dw, dh);
                    }
                    return bm;
                }
            }
            catch (Exception) { return null; }
        }

        public static ImageData Thumbnail(ImageData d, int maxSide)
        {
            if (d == null) return null;
            try
            {
                using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(d.Base64)))
                using (Image full = Image.FromStream(ms))
                    return ImageTools.Encode(full, maxSide);
            }
            catch (Exception) { return null; }
        }

        public static List<object> ToJson(List<Attachment> list)
        {
            List<object> l = new List<object>();
            foreach (Attachment a in list)
            {
                Dictionary<string, object> o = Json.O("n", a.Name);
                if (a.Path != null) o["p"] = a.Path;
                if (a.Size >= 0) o["s"] = (double)a.Size;
                if (a.Folder) o["d"] = true;
                ImageData th = a.Picture ? (a.Preview ?? Thumbnail(a.Image, 192)) : null;
                if (th != null) { a.Preview = th; o["th"] = th.Base64; o["tm"] = th.MediaType; }
                l.Add(o);
            }
            return l;
        }

        public static List<Attachment> FromJson(object v)
        {
            List<object> l = v as List<object>;
            if (l == null) return null;
            List<Attachment> r = new List<Attachment>();
            foreach (object x in l)
            {
                Dictionary<string, object> o = x as Dictionary<string, object>;
                if (o == null) continue;
                Attachment a = new Attachment();
                a.Name = Json.Str(o, "n") ?? "";
                a.Path = Json.Str(o, "p");
                object s;
                if (o.TryGetValue("s", out s) && s is double) a.Size = (long)(double)s;
                a.Folder = Json.Bool(o, "d");
                string th = Json.Str(o, "th");
                if (!string.IsNullOrEmpty(th)) a.Preview = new ImageData(Json.Str(o, "tm") ?? "image/jpeg", th);
                if (a.Name.Length == 0 && a.Path == null && a.Preview == null) continue;
                r.Add(a);
            }
            return r.Count > 0 ? r : null;
        }

        public static int FileWidth(Graphics g, Attachment a, Font name, Font meta, int reserve, int max)
        {
            const TextFormatFlags one = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            int text = g != null ? Math.Max(TextRenderer.MeasureText(g, a.Name, name, Size.Empty, one).Width, TextRenderer.MeasureText(g, Meta(a), meta, Size.Empty, one).Width)
                                 : Math.Max(TextRenderer.MeasureText(a.Name, name, Size.Empty, one).Width, TextRenderer.MeasureText(Meta(a), meta, Size.Empty, one).Width);
            int w = UI.S(8) + UI.S(32) + UI.S(10) + text + UI.S(14) + reserve;
            return Math.Max(Math.Min(UI.S(150), max), Math.Min(Math.Min(UI.S(260), max), w));
        }

        public static void DrawFile(Graphics g, Theme th, Rectangle r, Attachment a, Font name, Font meta, Color back, int reserve)
        {
            Glyphs.FillRound(g, th.HighContrast ? th.Back : back, r, UI.S(10));
            if (th.HighContrast) Glyphs.DrawRound(g, th.Border, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), UI.S(10));
            int ic = Math.Min(UI.S(32), r.Height - UI.S(16));
            Rectangle ir = new Rectangle(r.X + UI.S(8), r.Y + (r.Height - ic) / 2, ic, ic);
            if (!th.HighContrast) Glyphs.FillRound(g, Theme.Blend(back, th.Fore, th.Dark ? 0.09 : 0.07), ir, UI.S(7));
            Glyphs.Draw(Glyph(a), g, ir, th.ChromeFore);
            const TextFormatFlags one = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            int tx = ir.Right + UI.S(10);
            int tw = Math.Max(UI.S(20), r.Right - UI.S(12) - reserve - tx);
            int nh = TextRenderer.MeasureText(g, "Ag", name, Size.Empty, one).Height, mh = TextRenderer.MeasureText(g, "Ag", meta, Size.Empty, one).Height;
            int ty = r.Y + (r.Height - nh - mh - UI.S(2)) / 2;
            TextRenderer.DrawText(g, a.Name, name, new Rectangle(tx, ty, tw, nh), th.Fore, one);
            TextRenderer.DrawText(g, Meta(a), meta, new Rectangle(tx, ty + nh + UI.S(2), tw, mh), th.ChromeDim, one);
        }

        public static void DrawPicture(Graphics g, Theme th, Rectangle r, Image thumb)
        {
            int rad = UI.S(10);
            if (thumb == null)
            {
                Glyphs.FillRound(g, th.HighContrast ? th.Back : th.Chrome, r, rad);
                Glyphs.Draw("file", g, r, th.ChromeDim);
            }
            else
            {
                SmoothingMode sm = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath p = Glyphs.RoundF(new RectangleF(r.X, r.Y, r.Width, r.Height), rad))
                using (TextureBrush tb = new TextureBrush(thumb, WrapMode.Clamp))
                {
                    tb.TranslateTransform(r.X, r.Y);
                    g.FillPath(tb, p);
                }
                g.SmoothingMode = sm;
            }
            Glyphs.DrawRound(g, th.HighContrast ? th.Border : Theme.Blend(th.Border, th.Back, 0.25), new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), rad);
        }

        public static Rectangle RemoveRect(Rectangle tile)
        {
            int s = UI.S(18);
            return new Rectangle(tile.Right - s - UI.S(4), tile.Y + UI.S(4), s, s);
        }

        public static void DrawRemove(Graphics g, Theme th, Rectangle x, bool hot)
        {
            Glyphs.FillRound(g, hot ? th.Fore : Color.FromArgb(235, th.Back), x, x.Height / 2);
            Glyphs.X(g, x, hot ? th.Back : th.Fore, UI.S(3));
        }

        static readonly string[] OfficeFormats = { "Rich Text Format", "Csv", "XML Spreadsheet", "Biff8", "Biff12", "Embed Source", "Object Descriptor", "Link Source" };
        static readonly Regex Address = new Regex(@"^((https?|file|ftp|data|blob):|[A-Za-z]:\\|\\\\)\S*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string[] ClipboardFiles()
        {
            try
            {
                IDataObject d = Clipboard.GetDataObject();
                if (d == null || !d.GetDataPresent(DataFormats.FileDrop)) return null;
                string[] files = d.GetData(DataFormats.FileDrop) as string[];
                return files != null && files.Length > 0 ? files : null;
            }
            catch (Exception) { return null; }
        }

        public static bool PicturePreferred(IDataObject d)
        {
            if (d == null) return false;
            bool text = d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent(DataFormats.Text);
            if (!text) return true;
            foreach (string f in OfficeFormats) if (d.GetDataPresent(f)) return false;
            string t = (d.GetData(DataFormats.UnicodeText) as string ?? d.GetData(DataFormats.Text) as string ?? "").Trim();
            if (t.Length == 0 || (t.Length <= 4096 && Address.IsMatch(t))) return true;
            return t.Length <= 300 && t.IndexOf('\n') < 0 && t.IndexOf('\t') < 0;
        }

        public static bool ClipboardHasPicture()
        {
            try
            {
                IDataObject d = Clipboard.GetDataObject();
                if (d == null || !HasPictureData(d)) return false;
                return PicturePreferred(d);
            }
            catch (Exception) { return false; }
        }

        static bool HasPictureData(IDataObject d)
        {
            return d.GetDataPresent("PNG") || d.GetDataPresent("image/png") || d.GetDataPresent(DataFormats.Dib) || d.GetDataPresent(DataFormats.Bitmap);
        }

        public static Image ClipboardPicture()
        {
            try
            {
                IDataObject d = Clipboard.GetDataObject();
                if (d == null) return null;
                foreach (string f in new string[] { "PNG", "image/png" })
                {
                    if (!d.GetDataPresent(f)) continue;
                    Stream s = d.GetData(f) as Stream;
                    if (s == null) continue;
                    try
                    {
                        using (Image img = Image.FromStream(s)) return new Bitmap(img);
                    }
                    catch (Exception) { }
                }
                return Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
            }
            catch (Exception) { return null; }
        }
    }

    class ImagePreview : Form
    {
        readonly Image picture;
        readonly Theme th;

        ImagePreview(Image picture, Theme th, string name)
        {
            this.picture = picture;
            this.th = th;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            KeyPreview = true;
            Text = name;
            AccessibleName = name;
            BackColor = th.Back;
        }

        public static void Show(Form owner, Theme th, ImageData d, string name)
        {
            if (d == null) return;
            Image img;
            try
            {
                using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(d.Base64)))
                using (Image raw = Image.FromStream(ms))
                    img = new Bitmap(raw);
            }
            catch (Exception) { return; }
            ImagePreview p = new ImagePreview(img, th, string.IsNullOrEmpty(name) ? "Picture" : name);
            Rectangle area = owner != null ? owner.Bounds : Screen.PrimaryScreen.WorkingArea;
            Rectangle work = Screen.FromRectangle(area).WorkingArea;
            int pad = UI.S(10);
            double sc = Math.Min(1.0, Math.Min((work.Width * 0.82 - pad * 2) / img.Width, (work.Height * 0.82 - pad * 2) / img.Height));
            int w = Math.Max(UI.S(160), (int)(img.Width * sc) + pad * 2), h = Math.Max(UI.S(120), (int)(img.Height * sc) + pad * 2);
            int x = Math.Max(work.Left, Math.Min(work.Right - w, area.X + (area.Width - w) / 2));
            int y = Math.Max(work.Top, Math.Min(work.Bottom - h, area.Y + (area.Height - h) / 2));
            p.Bounds = new Rectangle(x, y, w, h);
            if (owner != null) p.Show(owner); else p.Show();
            p.Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(th.Back);
            int pad = UI.S(10);
            Rectangle room = new Rectangle(pad, pad, Math.Max(1, ClientSize.Width - pad * 2), Math.Max(1, ClientSize.Height - pad * 2));
            double sc = Math.Min(room.Width / (double)picture.Width, room.Height / (double)picture.Height);
            int w = Math.Max(1, (int)(picture.Width * sc)), h = Math.Max(1, (int)(picture.Height * sc));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(picture, new Rectangle(room.X + (room.Width - w) / 2, room.Y + (room.Height - h) / 2, w, h));
            using (Pen p = new Pen(th.Border)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; Close(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Close();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!IsDisposed) BeginInvoke(new Action(Close));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) picture.Dispose();
            base.Dispose(disposing);
        }
    }
}
