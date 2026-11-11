using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;

namespace MinGent
{
    class VideoShot
    {
        public double Time, Duration;
        public int Width, Height;
        public string Title = "";
    }

    static class VideoFrames
    {
        public const int SheetCount = 9, CloseCount = 4, SheetWidth = 1366;
        static readonly string[] Extensions = { ".mp4", ".m4v", ".webm", ".mov", ".ogv" };

        public static bool IsVideoFile(string path)
        {
            string p = (path ?? "").Trim().ToLowerInvariant();
            foreach (string e in Extensions) if (p.EndsWith(e, StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool TryTime(string text, out double seconds)
        {
            seconds = 0;
            string t = (text ?? "").Trim();
            if (t.EndsWith("s", StringComparison.OrdinalIgnoreCase) && t.IndexOf(':') < 0) t = t.Substring(0, t.Length - 1).Trim();
            if (t.Length == 0) return false;
            string[] parts = t.Split(':');
            if (parts.Length > 3) return false;
            double total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                double v;
                if (p.Length == 0 || !double.TryParse(p, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v)) return false;
                bool last = i == parts.Length - 1;
                if (!last && v != Math.Floor(v)) return false;
                if (i > 0 && v >= 60) return false;
                total = total * 60 + v;
            }
            if (double.IsNaN(total) || double.IsInfinity(total) || total > 360000) return false;
            seconds = total;
            return true;
        }

        public static string ParseTimes(object value, out List<double> times)
        {
            times = new List<double>();
            if (value == null) return null;
            List<string> parts = new List<string>();
            List<object> list = value as List<object>;
            string text = value as string;
            if (list != null) { foreach (object o in list) parts.Add(Convert.ToString(o, CultureInfo.InvariantCulture)); }
            else if (text != null) parts.AddRange(text.Split(new char[] { ',', ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries));
            else parts.Add(Convert.ToString(value, CultureInfo.InvariantCulture));
            foreach (string p in parts)
            {
                string t = (p ?? "").Trim().Trim('"', '\'', '[', ']');
                if (t.Length == 0) continue;
                double s;
                if (!TryTime(t, out s)) return "\"" + t + "\" is not a moment in a video. Write moments like 1:23, or as seconds.";
                times.Add(s);
            }
            return null;
        }

        public static List<double> Spread(double from, double to, int count)
        {
            List<double> list = new List<double>();
            if (count < 1) return list;
            if (to <= from) { list.Add(Math.Max(0, from)); return list; }
            double step = (to - from) / count;
            for (int i = 0; i < count; i++) list.Add(from + step * (i + 0.5));
            return list;
        }

        public static string NameOf(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out u)) return url ?? "";
            if (u.IsFile)
            {
                string p = u.LocalPath;
                string file = p.Substring(Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\')) + 1);
                if (file.Length > 0) return file;
            }
            return WebFetchTool.Host(url);
        }

        public static string Label(double seconds)
        {
            return YouTube.Time((int)Math.Round(Math.Max(0, seconds)) * 1000);
        }

        public static string Labels(List<double> times)
        {
            List<string> l = new List<string>();
            foreach (double t in times) l.Add(Label(t));
            return string.Join(", ", l.ToArray());
        }

        public static int Columns(int count) { return count <= 1 ? 1 : count <= 4 ? 2 : count <= 9 ? 3 : 4; }

        public static void Stamp(Graphics g, Rectangle cell, string text, float size)
        {
            using (Font f = new Font(FontFamily.GenericSansSerif, size, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF s = g.MeasureString(text, f);
                int pad = Math.Max(3, (int)Math.Round(size / 4));
                Rectangle box = new Rectangle(cell.X + pad, cell.Y + pad, (int)Math.Ceiling(s.Width) + pad * 2, (int)Math.Ceiling(s.Height) + pad);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(220, 0, 0, 0))) g.FillRectangle(b, box);
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.DrawString(text, f, Brushes.White, box.X + pad, box.Y + pad / 2f);
            }
        }

        static Rectangle Fit(Size picture, Rectangle cell)
        {
            double s = Math.Min(cell.Width / (double)Math.Max(1, picture.Width), cell.Height / (double)Math.Max(1, picture.Height));
            int w = Math.Max(1, (int)Math.Round(picture.Width * s)), h = Math.Max(1, (int)Math.Round(picture.Height * s));
            return new Rectangle(cell.X + (cell.Width - w) / 2, cell.Y + (cell.Height - h) / 2, w, h);
        }

        public static Bitmap Sheet(List<Bitmap> frames, List<double> times, int width)
        {
            int n = frames.Count;
            int cols = Columns(n), rows = (n + cols - 1) / cols, gap = 6;
            int cw = Math.Max(16, (width - gap * (cols - 1)) / cols);
            double aspect = frames[0].Height / (double)Math.Max(1, frames[0].Width);
            int ch = Math.Max(16, (int)Math.Round(cw * Math.Max(0.3, Math.Min(2.0, aspect))));
            Bitmap sheet = new Bitmap(cols * cw + gap * (cols - 1), rows * ch + gap * (rows - 1), PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(24, 24, 24));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                for (int i = 0; i < n; i++)
                {
                    Rectangle cell = new Rectangle(i % cols * (cw + gap), i / cols * (ch + gap), cw, ch);
                    Rectangle at = Fit(frames[i].Size, cell);
                    g.DrawImage(frames[i], at, 0, 0, frames[i].Width, frames[i].Height, GraphicsUnit.Pixel);
                    Stamp(g, cell, Label(times[i]), Math.Max(13f, cw / 24f));
                }
            }
            return sheet;
        }

        public static string Problem(string code, int frames)
        {
            switch (code)
            {
                case "none":
                    return "There is no video on the page that is open in the browser." + (frames > 0 ? " The page has frames from other sites, and a video inside such a frame can't be reached from here: open the video's own page instead." : " Open a page with a video, or a video file, with browser_navigate first.");
                case "empty": return "The video on this page has not loaded anything yet. Start it with browser_click, then try again.";
                case "loading": return "The video has not loaded yet. If it does not start by itself, start it with browser_click on the video, then try again.";
                case "broken": return "The browser can't play this video. It plays MP4, WebM and MOV files with common encodings.";
                case "live": return "This is a live stream, or its length is unknown, so there are no moments to pick. browser_screenshot shows what it shows right now.";
                case "ad": return "The page is showing an ad in place of the video. Wait for it to end or skip it with browser_click, then try again.";
            }
            return "The video could not be read (" + code + ").";
        }
    }
}
