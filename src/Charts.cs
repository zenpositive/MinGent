using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace MinGent
{
    class ChartSeries
    {
        public string Name = "";
        public double[] Values = new double[0];
    }

    class ChartSpec
    {
        public bool Line;
        public string Title = "";
        public List<string> Labels = new List<string>();
        public List<ChartSeries> Series = new List<ChartSeries>();
        public int Points;
    }

    static class Charts
    {
        public const string Lang = "chart";
        public const int MostSeries = 8, MostBars = 40, MostPoints = 200;
        static readonly Color[] Light = { Color.FromArgb(0x4C, 0x78, 0xA8), Color.FromArgb(0x59, 0xA1, 0x4F), Color.FromArgb(0xB0, 0x7A, 0xA1), Color.FromArgb(0xE1, 0x57, 0x59), Color.FromArgb(0x79, 0x70, 0x6E), Color.FromArgb(0x2E, 0x91, 0x93), Color.FromArgb(0x9C, 0x75, 0x5F) };
        static readonly Color[] Dark = { Color.FromArgb(0x7B, 0xA7, 0xD7), Color.FromArgb(0x86, 0xC9, 0x7B), Color.FromArgb(0xD4, 0xA6, 0xC8), Color.FromArgb(0xF2, 0x8E, 0x8E), Color.FromArgb(0xB5, 0xAD, 0xAA), Color.FromArgb(0x6C, 0xC5, 0xC6), Color.FromArgb(0xC9, 0xA4, 0x8E) };

        public static Color Shade(Theme th, int series)
        {
            if (series <= 0) return th.Accent;
            Color[] set = th.Dark ? Dark : Light;
            return set[(series - 1) % set.Length];
        }

        public static ChartSpec Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 60000) return null;
            Dictionary<string, object> d;
            try { d = Json.Parse(json.Trim()) as Dictionary<string, object>; } catch { return null; }
            if (d == null) return null;
            string type = (Json.Str(d, "type") ?? "bar").Trim().ToLowerInvariant();
            ChartSpec c = new ChartSpec();
            if (type == "line" || type == "area") c.Line = true;
            else if (type != "bar" && type != "column") return null;
            c.Title = OneLine(Json.Str(d, "title"), 120);
            List<object> series = Json.List(d, "series");
            if (series != null)
            {
                foreach (object o in series)
                {
                    Dictionary<string, object> sd = o as Dictionary<string, object>;
                    if (sd == null) return null;
                    ChartSeries s = new ChartSeries();
                    s.Name = OneLine(Json.Str(sd, "name"), 40);
                    s.Values = Numbers(Json.List(sd, "values") ?? Json.List(sd, "data"));
                    if (s.Values == null) return null;
                    c.Series.Add(s);
                }
            }
            else
            {
                ChartSeries s = new ChartSeries();
                s.Values = Numbers(Json.List(d, "values") ?? Json.List(d, "data"));
                if (s.Values == null) return null;
                c.Series.Add(s);
            }
            if (c.Series.Count == 0 || c.Series.Count > MostSeries) return null;
            foreach (ChartSeries s in c.Series) c.Points = Math.Max(c.Points, s.Values.Length);
            if (c.Points == 0 || c.Points > (c.Line ? MostPoints : MostBars)) return null;
            if (c.Line && c.Points < 2) c.Line = false;
            List<object> labels = Json.List(d, "labels") ?? Json.List(d, "x");
            for (int i = 0; i < c.Points; i++)
            {
                object l = labels != null && i < labels.Count ? labels[i] : null;
                string text = l is string ? (string)l : l == null ? (labels == null ? (i + 1).ToString(CultureInfo.InvariantCulture) : "") : Convert.ToString(l, CultureInfo.InvariantCulture);
                c.Labels.Add(OneLine(text, 24));
            }
            return c;
        }

        static double[] Numbers(List<object> list)
        {
            if (list == null || list.Count == 0) return null;
            double[] v = new double[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                object o = list[i];
                double n;
                if (o == null) n = double.NaN;
                else if (o is string) { if (!double.TryParse(((string)o).Trim().Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return null; }
                else if (o is bool || o is Dictionary<string, object> || o is List<object>) return null;
                else { try { n = Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return null; } }
                if (double.IsInfinity(n)) return null;
                v[i] = n;
            }
            foreach (double n in v) if (!double.IsNaN(n)) return v;
            return null;
        }

        static string OneLine(string text, int most)
        {
            if (string.IsNullOrEmpty(text)) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in text) sb.Append(char.IsControl(ch) ? ' ' : ch);
            string s = sb.ToString().Trim();
            return s.Length > most ? s.Substring(0, most - 3).TrimEnd() + "..." : s;
        }

        public static double Step(double span, int parts)
        {
            if (span <= 0 || double.IsNaN(span)) return 1;
            double raw = span / Math.Max(1, parts);
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double lead = raw / mag;
            return (lead <= 1 ? 1 : lead <= 2 ? 2 : lead <= 2.5 ? 2.5 : lead <= 5 ? 5 : 10) * mag;
        }

        public static void Range(ChartSpec c, out double lo, out double hi, out double step)
        {
            double min = double.MaxValue, max = double.MinValue;
            foreach (ChartSeries s in c.Series)
                foreach (double v in s.Values)
                {
                    if (double.IsNaN(v)) continue;
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                }
            if (!c.Line || (min >= 0 && min <= (max - min) * 2)) min = Math.Min(min, 0);
            if (!c.Line) max = Math.Max(max, 0);
            if (max == min) { max = min + (min == 0 ? 1 : Math.Abs(min) * 0.5); if (!c.Line || min > 0) min = Math.Min(min, 0); }
            step = Step(max - min, 4);
            lo = Math.Floor(min / step + 1e-9) * step;
            hi = Math.Ceiling(max / step - 1e-9) * step;
            if (hi <= lo) hi = lo + step;
        }

        public static string Num(double v, double step)
        {
            double a = Math.Abs(v);
            if (a >= 1e12) return (v / 1e12).ToString("0.##", CultureInfo.InvariantCulture) + "T";
            if (a >= 1e9) return (v / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + "B";
            if (a >= 1e6) return (v / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + "M";
            if (a >= 1e4) return (v / 1e3).ToString("0.##", CultureInfo.InvariantCulture) + "K";
            int places = step >= 1 ? 0 : Math.Min(6, (int)Math.Ceiling(-Math.Log10(step) - 1e-9));
            if (step > 0 && step < 1 && Math.Abs(step * Math.Pow(10, places) - Math.Round(step * Math.Pow(10, places))) > 1e-6) places = Math.Min(6, places + 1);
            return v.ToString(places == 0 ? "0" : "0." + new string('0', places), CultureInfo.InvariantCulture);
        }

        static DrawOp Text(MdLayout L, string text, Font font, int role, int x, int y, int w, int h)
        {
            DrawOp t = new DrawOp();
            t.Kind = DrawOp.KText;
            t.Text = text;
            t.Font = font;
            t.Role = role;
            t.NoSelect = true;
            t.R = new Rectangle(x, y, w, h);
            L.Ops.Add(t);
            return t;
        }

        static void Mark(MdLayout L, int kind, int series, int x, int y, int w, int h, int role)
        {
            DrawOp m = new DrawOp();
            m.Kind = kind;
            m.Tag = series;
            m.Role = role;
            m.R = new Rectangle(x, y, w, h);
            L.Ops.Add(m);
        }

        static string Fit(Graphics g, ChatFonts f, string text, Font font, int room)
        {
            if (f.Width(g, text, font) <= room) return text;
            for (int n = text.Length - 1; n > 1; n--)
            {
                string cut = text.Substring(0, n).TrimEnd() + "...";
                if (f.Width(g, cut, font) <= room) return cut;
            }
            return "";
        }

        public const string Unfinished = "Chart, not finished yet";

        static DrawOp Frame(Graphics g, string source, int width, int y, ChatFonts f, MdLayout L, out int copyWidth)
        {
            int idx = L.CodeBlocks.Count;
            L.CodeBlocks.Add(source);
            DrawOp bg = new DrawOp();
            bg.Kind = DrawOp.KCodeBlock;
            bg.Tag = idx;
            L.Ops.Add(bg);
            DrawOp copy = new DrawOp();
            copy.Kind = DrawOp.KCopy;
            copy.Tag = idx;
            copyWidth = f.Width(g, "Copy", f.Small) + UI.S(26);
            copy.R = new Rectangle(width - copyWidth - UI.S(4), y + UI.S(2), copyWidth, f.SmallLineH + UI.S(4));
            L.Ops.Add(copy);
            return bg;
        }

        public static int Pending(Graphics g, string source, int width, int y, ChatFonts f, MdLayout L)
        {
            int cw, top = y;
            DrawOp bg = Frame(g, source, width, y, f, L, out cw);
            y += UI.S(8);
            Text(L, Unfinished, f.SmallBold, DrawOp.RDim, UI.S(12), y, width - UI.S(24) - cw, f.SmallLineH);
            y += f.SmallLineH + UI.S(10);
            bg.R = new Rectangle(0, top, width, y - top);
            return y;
        }

        public static int Layout(Graphics g, ChartSpec c, string source, int width, int y, ChatFonts f, MdLayout L)
        {
            int cw, pad = UI.S(12), top = y;
            DrawOp bg = Frame(g, source, width, y, f, L, out cw);
            y += UI.S(8);
            string title = Fit(g, f, c.Title.Length > 0 ? c.Title : "Chart", f.SmallBold, width - pad * 2 - cw);
            Text(L, title, f.SmallBold, c.Title.Length > 0 ? DrawOp.RFore : DrawOp.RDim, pad, y, width - pad * 2 - cw, f.SmallLineH);
            y += f.SmallLineH + UI.S(10);

            double lo, hi, step;
            Range(c, out lo, out hi, out step);
            List<double> ticks = new List<double>();
            for (double t = lo; t <= hi + step * 0.001 && ticks.Count < 12; t += step) ticks.Add(Math.Abs(t) < step * 1e-9 ? 0 : t);
            int labelW = 0;
            foreach (double t in ticks) labelW = Math.Max(labelW, f.Width(g, Num(t, step), f.Small));
            int left = pad + labelW + UI.S(8), plotW = Math.Max(UI.S(40), width - left - pad), plotH = UI.S(170), plotTop = y + f.SmallLineH / 2;
            foreach (double t in ticks)
            {
                int ty = plotTop + (int)Math.Round((hi - t) / (hi - lo) * plotH);
                Mark(L, t == 0 ? DrawOp.KTableBorder : DrawOp.KLine, t == 0 ? 1 : 0, left, ty, plotW, 1, 0);
                string label = Num(t, step);
                Text(L, label, f.Small, DrawOp.RDim, left - UI.S(8) - f.Width(g, label, f.Small), ty - f.SmallLineH / 2, labelW, f.SmallLineH);
            }
            double floor = Math.Max(lo, Math.Min(hi, 0));
            int zeroY = plotTop + (int)Math.Round((hi - floor) / (hi - lo) * plotH);
            double slot = (double)plotW / c.Points;
            if (c.Line)
            {
                for (int si = 0; si < c.Series.Count; si++)
                {
                    double[] v = c.Series[si].Values;
                    int px = 0, py = 0;
                    bool have = false;
                    for (int i = 0; i < v.Length; i++)
                    {
                        if (double.IsNaN(v[i])) { have = false; continue; }
                        int x = left + (int)Math.Round(slot * i + slot / 2);
                        int vy = plotTop + (int)Math.Round((hi - v[i]) / (hi - lo) * plotH);
                        if (have) Mark(L, DrawOp.KSeg, si, px, Math.Min(py, vy), x - px, Math.Abs(vy - py), vy < py ? 1 : 0);
                        if (c.Points <= 31) Mark(L, DrawOp.KDot, si, x - UI.S(3), vy - UI.S(3), UI.S(6), UI.S(6), 0);
                        px = x;
                        py = vy;
                        have = true;
                    }
                }
            }
            else
            {
                double group = slot * (c.Points > 20 ? 0.86 : 0.72);
                double bar = Math.Max(1, group / c.Series.Count);
                for (int si = 0; si < c.Series.Count; si++)
                {
                    double[] v = c.Series[si].Values;
                    for (int i = 0; i < v.Length; i++)
                    {
                        if (double.IsNaN(v[i])) continue;
                        int x0 = left + (int)Math.Round(slot * i + (slot - group) / 2 + bar * si);
                        int x1 = left + (int)Math.Round(slot * i + (slot - group) / 2 + bar * (si + 1));
                        int vy = plotTop + (int)Math.Round((hi - v[i]) / (hi - lo) * plotH);
                        int gapW = c.Series.Count > 1 && x1 - x0 > UI.S(4) ? 1 : 0;
                        Mark(L, DrawOp.KBar, si, x0, Math.Min(vy, zeroY), Math.Max(1, x1 - x0 - gapW), Math.Max(1, Math.Abs(zeroY - vy)), 0);
                    }
                }
            }
            y = plotTop + plotH + UI.S(6);
            int widest = 0;
            foreach (string l in c.Labels) widest = Math.Max(widest, f.Width(g, l, f.Small));
            int every = Math.Max(1, (int)Math.Ceiling((widest + UI.S(10)) / slot));
            bool any = false;
            for (int i = 0; i < c.Points; i += every)
            {
                string l = c.Labels[i];
                if (l.Length == 0) continue;
                int w = f.Width(g, l, f.Small);
                int x = left + (int)Math.Round(slot * i + slot / 2) - w / 2;
                x = Math.Max(left - UI.S(4), Math.Min(width - pad - w, x));
                Text(L, l, f.Small, DrawOp.RDim, x, y, w, f.SmallLineH);
                any = true;
            }
            if (any) y += f.SmallLineH;
            bool legend = c.Series.Count > 1;
            foreach (ChartSeries s in c.Series) if (s.Name.Length > 0) legend = true;
            if (legend)
            {
                y += UI.S(8);
                int x = left, sw = UI.S(10);
                for (int si = 0; si < c.Series.Count; si++)
                {
                    string name = c.Series[si].Name.Length > 0 ? c.Series[si].Name : "Series " + (si + 1).ToString(CultureInfo.InvariantCulture);
                    name = Fit(g, f, name, f.Small, Math.Max(UI.S(40), width - left - pad - sw - UI.S(6)));
                    int w = sw + UI.S(6) + f.Width(g, name, f.Small);
                    if (x > left && x + w > width - pad) { x = left; y += f.SmallLineH + UI.S(4); }
                    Mark(L, DrawOp.KBar, si, x, y + (f.SmallLineH - sw) / 2, sw, sw, 1);
                    Text(L, name, f.Small, DrawOp.RFore, x + sw + UI.S(6), y, w - sw - UI.S(6), f.SmallLineH);
                    x += w + UI.S(16);
                }
                y += f.SmallLineH;
            }
            y += UI.S(12);
            bg.R = new Rectangle(0, top, width, y - top);
            return y;
        }
    }
}
