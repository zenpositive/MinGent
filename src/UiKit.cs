using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MinGent
{
    static partial class Glyphs
    {
        static Pen StrokePen(Color c, float w)
        {
            Pen p = new Pen(c, w);
            p.StartCap = p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            return p;
        }

        public static bool Draw(string name, Graphics g, Rectangle r, Color c)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (PixelIcons.Draw(name, g, r, c)) return true;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            float u = Math.Min(s, UI.S(16)) / 16f;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            float w = Math.Max(1.25f, 1.4f * UI.Scale);
            bool done = true;
            using (Pen p = StrokePen(c, w))
            using (SolidBrush b = new SolidBrush(c))
            {
                switch (name)
                {
                    case "reload":
                        g.DrawArc(p, cx - 5.5f * u, cy - 5.5f * u, 11f * u, 11f * u, -60, 300);
                        g.FillPolygon(b, new PointF[] { new PointF(cx + 2.2f * u, cy - 7.6f * u), new PointF(cx + 6.6f * u, cy - 5.6f * u), new PointF(cx + 2.4f * u, cy - 2.6f * u) });
                        break;
                    case "external":
                        g.DrawLines(p, new PointF[] { new PointF(cx - 1 * u, cy - 5.5f * u), new PointF(cx - 5.5f * u, cy - 5.5f * u), new PointF(cx - 5.5f * u, cy + 5.5f * u), new PointF(cx + 5.5f * u, cy + 5.5f * u), new PointF(cx + 5.5f * u, cy + 1 * u) });
                        g.DrawLine(p, cx - 0.5f * u, cy + 0.5f * u, cx + 6f * u, cy - 6f * u);
                        g.DrawLines(p, new PointF[] { new PointF(cx + 1.5f * u, cy - 6f * u), new PointF(cx + 6f * u, cy - 6f * u), new PointF(cx + 6f * u, cy - 1.5f * u) });
                        break;
                    case "send":
                        g.DrawLine(p, cx, cy + 5f * u, cx, cy - 5f * u);
                        g.DrawLines(p, new PointF[] { new PointF(cx - 4.5f * u, cy - 0.5f * u), new PointF(cx, cy - 5f * u), new PointF(cx + 4.5f * u, cy - 0.5f * u) });
                        break;
                    case "stop":
                        FillRound(g, c, new Rectangle((int)(cx - 4.5f * u), (int)(cy - 4.5f * u), (int)(9 * u), (int)(9 * u)), Math.Max(1, (int)(2 * u)));
                        break;
                    case "newchat":
                        g.DrawLines(p, new PointF[] { new PointF(cx + 1 * u, cy - 6f * u), new PointF(cx - 6f * u, cy - 6f * u), new PointF(cx - 6f * u, cy + 6f * u), new PointF(cx + 6f * u, cy + 6f * u), new PointF(cx + 6f * u, cy - 1 * u) });
                        g.DrawLine(p, cx - 1.5f * u, cy + 1.5f * u, cx + 5.5f * u, cy - 5.5f * u);
                        break;
                    case "temp":
                        using (GraphicsPath gp = new GraphicsPath())
                        using (Pen dp = StrokePen(c, w))
                        {
                            float l = cx - 6.5f * u, t = cy - 6f * u, rr = 3.2f * u, rgt = cx + 6.5f * u, bot = cy + 3.5f * u;
                            gp.AddArc(l, t, rr * 2, rr * 2, 180, 90);
                            gp.AddArc(rgt - rr * 2, t, rr * 2, rr * 2, 270, 90);
                            gp.AddArc(rgt - rr * 2, bot - rr * 2, rr * 2, rr * 2, 0, 90);
                            gp.AddLine(rgt - rr, bot, cx - 1.5f * u, bot);
                            gp.AddLine(cx - 1.5f * u, bot, cx - 4.5f * u, cy + 6.5f * u);
                            gp.AddLine(cx - 4.5f * u, cy + 6.5f * u, cx - 4f * u, bot);
                            gp.AddArc(l, bot - rr * 2, rr * 2, rr * 2, 90, 90);
                            gp.CloseFigure();
                            dp.DashStyle = DashStyle.Custom;
                            dp.DashPattern = new float[] { 1.7f, 1.5f };
                            dp.DashCap = DashCap.Round;
                            g.DrawPath(dp, gp);
                        }
                        break;
                    case "history":
                        g.DrawEllipse(p, cx - 6f * u, cy - 6f * u, 12f * u, 12f * u);
                        g.DrawLines(p, new PointF[] { new PointF(cx, cy - 3.5f * u), new PointF(cx, cy), new PointF(cx + 2.8f * u, cy + 1.8f * u) });
                        break;
                    case "attach":
                        g.DrawLine(p, cx - 5.5f * u, cy, cx + 5.5f * u, cy);
                        g.DrawLine(p, cx, cy - 5.5f * u, cx, cy + 5.5f * u);
                        break;
                    case "file":
                        g.DrawLines(p, new PointF[] { new PointF(cx + 1.5f * u, cy - 6.5f * u), new PointF(cx - 4.5f * u, cy - 6.5f * u), new PointF(cx - 4.5f * u, cy + 6.5f * u), new PointF(cx + 4.5f * u, cy + 6.5f * u), new PointF(cx + 4.5f * u, cy - 3.5f * u), new PointF(cx + 1.5f * u, cy - 6.5f * u), new PointF(cx + 1.5f * u, cy - 3.5f * u), new PointF(cx + 4.5f * u, cy - 3.5f * u) });
                        break;
                    case "pencil":
                        g.DrawLines(p, new PointF[] { new PointF(cx - 5.5f * u, cy + 5.5f * u), new PointF(cx - 5f * u, cy + 2.5f * u), new PointF(cx + 3f * u, cy - 5.5f * u), new PointF(cx + 5.5f * u, cy - 3f * u), new PointF(cx - 2.5f * u, cy + 5f * u), new PointF(cx - 5.5f * u, cy + 5.5f * u) });
                        break;
                    case "folder":
                        g.DrawLines(p, new PointF[] { new PointF(cx - 6.5f * u, cy + 5f * u), new PointF(cx - 6.5f * u, cy - 5f * u), new PointF(cx - 2f * u, cy - 5f * u), new PointF(cx - 0.5f * u, cy - 3f * u), new PointF(cx + 6.5f * u, cy - 3f * u), new PointF(cx + 6.5f * u, cy + 5f * u), new PointF(cx - 6.5f * u, cy + 5f * u) });
                        break;
                    case "search":
                        g.DrawEllipse(p, cx - 6f * u, cy - 6f * u, 9f * u, 9f * u);
                        g.DrawLine(p, cx + 1.8f * u, cy + 1.8f * u, cx + 6f * u, cy + 6f * u);
                        break;
                    case "terminal":
                        g.DrawLines(p, new PointF[] { new PointF(cx - 6f * u, cy - 4.5f * u), new PointF(cx - 1.5f * u, cy), new PointF(cx - 6f * u, cy + 4.5f * u) });
                        g.DrawLine(p, cx + 0.5f * u, cy + 5f * u, cx + 6.5f * u, cy + 5f * u);
                        break;
                    case "globe":
                        g.DrawEllipse(p, cx - 6.5f * u, cy - 6.5f * u, 13f * u, 13f * u);
                        g.DrawEllipse(p, cx - 2.8f * u, cy - 6.5f * u, 5.6f * u, 13f * u);
                        g.DrawLine(p, cx - 6.3f * u, cy, cx + 6.3f * u, cy);
                        break;
                    case "cursor":
                        g.DrawPolygon(p, new PointF[] { new PointF(cx - 5f * u, cy - 6f * u), new PointF(cx + 5f * u, cy + 0.5f * u), new PointF(cx + 0.5f * u, cy + 1.5f * u), new PointF(cx - 1.5f * u, cy + 6f * u) });
                        break;
                    case "map":
                        g.DrawLines(p, new PointF[] { new PointF(cx - 6.5f * u, cy - 4.5f * u), new PointF(cx - 2.2f * u, cy - 6f * u), new PointF(cx + 2.2f * u, cy - 4.5f * u), new PointF(cx + 6.5f * u, cy - 6f * u), new PointF(cx + 6.5f * u, cy + 4.5f * u), new PointF(cx + 2.2f * u, cy + 6f * u), new PointF(cx - 2.2f * u, cy + 4.5f * u), new PointF(cx - 6.5f * u, cy + 6f * u), new PointF(cx - 6.5f * u, cy - 4.5f * u) });
                        g.DrawLine(p, cx - 2.2f * u, cy - 6f * u, cx - 2.2f * u, cy + 4.5f * u);
                        g.DrawLine(p, cx + 2.2f * u, cy - 4.5f * u, cx + 2.2f * u, cy + 6f * u);
                        break;
                    case "checklist":
                        g.DrawLines(p, new PointF[] { new PointF(cx - 6.5f * u, cy - 4f * u), new PointF(cx - 5f * u, cy - 2.5f * u), new PointF(cx - 2.5f * u, cy - 5.5f * u) });
                        g.DrawLines(p, new PointF[] { new PointF(cx - 6.5f * u, cy + 3f * u), new PointF(cx - 5f * u, cy + 4.5f * u), new PointF(cx - 2.5f * u, cy + 1.5f * u) });
                        g.DrawLine(p, cx + 0.5f * u, cy - 4f * u, cx + 6.5f * u, cy - 4f * u);
                        g.DrawLine(p, cx + 0.5f * u, cy + 3f * u, cx + 6.5f * u, cy + 3f * u);
                        break;
                    case "check":
                        g.DrawLines(p, new PointF[] { new PointF(cx - 5f * u, cy + 0.5f * u), new PointF(cx - 1.5f * u, cy + 4f * u), new PointF(cx + 5.5f * u, cy - 4f * u) });
                        break;
                    case "cross":
                        g.DrawLine(p, cx - 4f * u, cy - 4f * u, cx + 4f * u, cy + 4f * u);
                        g.DrawLine(p, cx - 4f * u, cy + 4f * u, cx + 4f * u, cy - 4f * u);
                        break;
                    case "minus":
                        g.DrawLine(p, cx - 4.5f * u, cy, cx + 4.5f * u, cy);
                        break;
                    case "copy":
                        DrawRoundF(g, p, new RectangleF(cx - 3f * u, cy - 3f * u, 9f * u, 9f * u), 1.6f * u);
                        g.DrawLines(p, new PointF[] { new PointF(cx - 6f * u, cy + 3f * u), new PointF(cx - 6f * u, cy - 6f * u), new PointF(cx + 3f * u, cy - 6f * u) });
                        break;
                    case "ask":
                        DrawRoundF(g, p, new RectangleF(cx - 6.5f * u, cy - 5.5f * u, 13f * u, 9.5f * u), 3f * u);
                        g.DrawLines(p, new PointF[] { new PointF(cx - 3.5f * u, cy + 4f * u), new PointF(cx - 4.5f * u, cy + 6.8f * u), new PointF(cx - 0.5f * u, cy + 4f * u) });
                        break;
                    case "warning":
                        g.DrawPolygon(p, new PointF[] { new PointF(cx, cy - 6.5f * u), new PointF(cx + 7f * u, cy + 5.5f * u), new PointF(cx - 7f * u, cy + 5.5f * u) });
                        g.DrawLine(p, cx, cy - 2f * u, cx, cy + 1.5f * u);
                        g.FillEllipse(b, cx - 0.9f * u, cy + 3f * u, 1.8f * u, 1.8f * u);
                        break;
                    case "shield":
                        g.DrawLines(p, new PointF[] { new PointF(cx, cy - 6.5f * u), new PointF(cx + 5.5f * u, cy - 4.5f * u), new PointF(cx + 5.5f * u, cy), new PointF(cx, cy + 6.5f * u), new PointF(cx - 5.5f * u, cy), new PointF(cx - 5.5f * u, cy - 4.5f * u), new PointF(cx, cy - 6.5f * u) });
                        break;
                    case "spark":
                        g.DrawLine(p, cx, cy - 6f * u, cx, cy + 6f * u);
                        g.DrawLine(p, cx - 6f * u, cy, cx + 6f * u, cy);
                        g.DrawLine(p, cx - 3.6f * u, cy - 3.6f * u, cx + 3.6f * u, cy + 3.6f * u);
                        g.DrawLine(p, cx - 3.6f * u, cy + 3.6f * u, cx + 3.6f * u, cy - 3.6f * u);
                        break;
                    case "chip":
                        DrawRoundF(g, p, new RectangleF(cx - 4.5f * u, cy - 4.5f * u, 9f * u, 9f * u), 1.5f * u);
                        for (int i = -1; i <= 1; i++)
                        {
                            g.DrawLine(p, cx + i * 3f * u, cy - 4.5f * u, cx + i * 3f * u, cy - 6.8f * u);
                            g.DrawLine(p, cx + i * 3f * u, cy + 4.5f * u, cx + i * 3f * u, cy + 6.8f * u);
                        }
                        break;
                    case "down":
                        g.DrawLines(p, new PointF[] { new PointF(cx - 4f * u, cy - 2f * u), new PointF(cx, cy + 2f * u), new PointF(cx + 4f * u, cy - 2f * u) });
                        break;
                    case "right":
                        g.DrawLines(p, new PointF[] { new PointF(cx - 2f * u, cy - 4f * u), new PointF(cx + 2f * u, cy), new PointF(cx - 2f * u, cy + 4f * u) });
                        break;
                    case "arrowdown":
                        g.DrawLine(p, cx, cy - 5f * u, cx, cy + 5f * u);
                        g.DrawLines(p, new PointF[] { new PointF(cx - 4.5f * u, cy + 0.5f * u), new PointF(cx, cy + 5f * u), new PointF(cx + 4.5f * u, cy + 0.5f * u) });
                        break;
                    case "dot":
                        g.FillEllipse(b, cx - 2.5f * u, cy - 2.5f * u, 5f * u, 5f * u);
                        break;
                    case "more":
                        for (int i = -1; i <= 1; i++)
                            g.FillEllipse(b, cx + i * 5f * u - 1.5f * u, cy - 1.5f * u, 3f * u, 3f * u);
                        break;
                    case "tool":
                        {
                            PointF[] head = { new PointF(cx + 6.8f * u, cy - 0.02f * u), new PointF(cx + 3.98f * u, cy + 2.81f * u), new PointF(cx - 2.81f * u, cy - 3.98f * u), new PointF(cx + 0.02f * u, cy - 6.8f * u) };
                            g.FillPolygon(b, head);
                            using (Pen handle = StrokePen(c, w * 1.25f)) g.DrawLine(handle, cx - 6f * u, cy + 6f * u, cx + 1.6f * u, cy - 1.6f * u);
                            break;
                        }
                    case "eye":
                        g.DrawBezier(p, cx - 7f * u, cy, cx - 3.5f * u, cy - 5.5f * u, cx + 3.5f * u, cy - 5.5f * u, cx + 7f * u, cy);
                        g.DrawBezier(p, cx + 7f * u, cy, cx + 3.5f * u, cy + 5.5f * u, cx - 3.5f * u, cy + 5.5f * u, cx - 7f * u, cy);
                        g.FillEllipse(b, cx - 2.3f * u, cy - 2.3f * u, 4.6f * u, 4.6f * u);
                        break;
                    case "bulb":
                        g.DrawArc(p, cx - 4.6f * u, cy - 6.8f * u, 9.2f * u, 9.2f * u, 135f, 270f);
                        g.DrawLine(p, cx - 3.25f * u, cy + 1.05f * u, cx - 1.9f * u, cy + 3.4f * u);
                        g.DrawLine(p, cx + 3.25f * u, cy + 1.05f * u, cx + 1.9f * u, cy + 3.4f * u);
                        g.DrawLine(p, cx - 1.9f * u, cy + 3.4f * u, cx + 1.9f * u, cy + 3.4f * u);
                        g.DrawLine(p, cx - 1.4f * u, cy + 5.6f * u, cx + 1.4f * u, cy + 5.6f * u);
                        break;
                    case "wave":
                        {
                            float[] hs = { 1f, 3f, 2f, 6f, 4f, 5f, 2f, 1f };
                            for (int i = 0; i < hs.Length; i++)
                            {
                                float x = cx + (i * 2 - 7) * 0.9f * u;
                                g.DrawLine(p, x, cy - hs[i] * u, x, cy + hs[i] * u);
                            }
                            break;
                        }
                    case "branch":
                        {
                            float nr = 1.9f * u;
                            g.DrawEllipse(p, cx - 4f * u - nr, cy - 5f * u - nr, nr * 2, nr * 2);
                            g.DrawEllipse(p, cx - 4f * u - nr, cy + 5f * u - nr, nr * 2, nr * 2);
                            g.DrawEllipse(p, cx + 4f * u - nr, cy - 3f * u - nr, nr * 2, nr * 2);
                            g.DrawLine(p, cx - 4f * u, cy - 5f * u + nr, cx - 4f * u, cy + 5f * u - nr);
                            g.DrawBezier(p, new PointF(cx + 4f * u, cy - 3f * u + nr), new PointF(cx + 4f * u, cy + 1.5f * u), new PointF(cx - 4f * u, cy), new PointF(cx - 4f * u, cy + 3f * u));
                            break;
                        }
                    case "sidebar":
                        DrawRoundF(g, p, new RectangleF(cx - 6.5f * u, cy - 5.5f * u, 13f * u, 11f * u), 2f * u);
                        g.DrawLine(p, cx - 2f * u, cy - 5.5f * u, cx - 2f * u, cy + 5.5f * u);
                        break;
                    case "trash":
                        g.DrawLine(p, cx - 6f * u, cy - 4f * u, cx + 6f * u, cy - 4f * u);
                        g.DrawLines(p, new PointF[] { new PointF(cx - 2f * u, cy - 4f * u), new PointF(cx - 2f * u, cy - 6f * u), new PointF(cx + 2f * u, cy - 6f * u), new PointF(cx + 2f * u, cy - 4f * u) });
                        g.DrawLines(p, new PointF[] { new PointF(cx - 4.5f * u, cy - 4f * u), new PointF(cx - 3.8f * u, cy + 6f * u), new PointF(cx + 3.8f * u, cy + 6f * u), new PointF(cx + 4.5f * u, cy - 4f * u) });
                        g.DrawLine(p, cx - 1.2f * u, cy - 1.5f * u, cx - 1.2f * u, cy + 3.5f * u);
                        g.DrawLine(p, cx + 1.2f * u, cy - 1.5f * u, cx + 1.2f * u, cy + 3.5f * u);
                        break;
                    case "gear":
                        g.DrawEllipse(p, cx - 2.3f * u, cy - 2.3f * u, 4.6f * u, 4.6f * u);
                        g.DrawEllipse(p, cx - 5f * u, cy - 5f * u, 10f * u, 10f * u);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                            g.DrawLine(p, cx + ca * 5f * u, cy + sa * 5f * u, cx + ca * 7f * u, cy + sa * 7f * u);
                        }
                        break;
                    case "archive":
                        DrawRoundF(g, p, new RectangleF(cx - 6.5f * u, cy - 5.5f * u, 13f * u, 3.6f * u), 1f * u);
                        g.DrawLines(p, new PointF[] { new PointF(cx - 5.5f * u, cy - 1.9f * u), new PointF(cx - 5.5f * u, cy + 5.5f * u), new PointF(cx + 5.5f * u, cy + 5.5f * u), new PointF(cx + 5.5f * u, cy - 1.9f * u) });
                        g.DrawLine(p, cx - 2f * u, cy + 1f * u, cx + 2f * u, cy + 1f * u);
                        break;
                    case "back":
                        g.DrawLine(p, cx - 5.5f * u, cy, cx + 5.5f * u, cy);
                        g.DrawLines(p, new PointF[] { new PointF(cx - 1f * u, cy - 4.5f * u), new PointF(cx - 5.5f * u, cy), new PointF(cx - 1f * u, cy + 4.5f * u) });
                        break;
                    case "library":
                        g.DrawPolygon(p, new PointF[] { new PointF(cx, cy - 6.5f * u), new PointF(cx + 6f * u, cy - 3.2f * u), new PointF(cx + 6f * u, cy + 3.2f * u), new PointF(cx, cy + 6.5f * u), new PointF(cx - 6f * u, cy + 3.2f * u), new PointF(cx - 6f * u, cy - 3.2f * u) });
                        g.DrawLines(p, new PointF[] { new PointF(cx - 6f * u, cy - 3.2f * u), new PointF(cx, cy), new PointF(cx + 6f * u, cy - 3.2f * u) });
                        g.DrawLine(p, cx, cy, cx, cy + 6.5f * u);
                        break;
                    case "notes":
                        DrawRoundF(g, p, new RectangleF(cx - 5f * u, cy - 6.5f * u, 10f * u, 13f * u), 1.6f * u);
                        g.DrawLine(p, cx - 2.5f * u, cy - 3f * u, cx + 2.5f * u, cy - 3f * u);
                        g.DrawLine(p, cx - 2.5f * u, cy, cx + 2.5f * u, cy);
                        g.DrawLine(p, cx - 2.5f * u, cy + 3f * u, cx + 0.5f * u, cy + 3f * u);
                        break;
                    case "sliders":
                        {
                            float y1 = cy - 3.5f * u, y2 = cy + 3.5f * u, k = 2.1f * u, k1 = cx + 2.5f * u, k2 = cx - 2.5f * u;
                            g.DrawLine(p, cx - 6.5f * u, y1, k1 - k, y1);
                            g.DrawLine(p, k1 + k, y1, cx + 6.5f * u, y1);
                            g.DrawEllipse(p, k1 - k, y1 - k, 2 * k, 2 * k);
                            g.DrawLine(p, cx - 6.5f * u, y2, k2 - k, y2);
                            g.DrawLine(p, k2 + k, y2, cx + 6.5f * u, y2);
                            g.DrawEllipse(p, k2 - k, y2 - k, 2 * k, 2 * k);
                            break;
                        }
                    case "lock":
                        DrawRoundF(g, p, new RectangleF(cx - 5f * u, cy - 1f * u, 10f * u, 7.5f * u), 1.6f * u);
                        using (GraphicsPath sh = new GraphicsPath())
                        {
                            sh.AddLine(cx - 3.2f * u, cy - 1f * u, cx - 3.2f * u, cy - 3.3f * u);
                            sh.AddArc(cx - 3.2f * u, cy - 6.5f * u, 6.4f * u, 6.4f * u, 180, 180);
                            sh.AddLine(cx + 3.2f * u, cy - 3.3f * u, cx + 3.2f * u, cy - 1f * u);
                            g.DrawPath(p, sh);
                        }
                        break;
                    case "bookmark":
                        g.DrawPolygon(p, new PointF[] { new PointF(cx - 4.5f * u, cy + 6.5f * u), new PointF(cx - 4.5f * u, cy - 6f * u), new PointF(cx + 4.5f * u, cy - 6f * u), new PointF(cx + 4.5f * u, cy + 6.5f * u), new PointF(cx, cy + 3f * u) });
                        break;
                    case "undo":
                    case "redo":
                        {
                            float m = name == "undo" ? 1f : -1f;
                            using (GraphicsPath path = new GraphicsPath())
                            {
                                path.AddLine(cx - m * 5.5f * u, cy - 2.5f * u, cx + m * 1.5f * u, cy - 2.5f * u);
                                if (m > 0) path.AddArc(cx - 2f * u, cy - 2.5f * u, 7f * u, 7f * u, -90, 180);
                                else path.AddArc(cx - 5f * u, cy - 2.5f * u, 7f * u, 7f * u, -90, -180);
                                path.AddLine(cx + m * 1.5f * u, cy + 4.5f * u, cx - m * 2.5f * u, cy + 4.5f * u);
                                g.DrawPath(p, path);
                            }
                            g.DrawLines(p, new PointF[] { new PointF(cx - m * 2.5f * u, cy - 5.5f * u), new PointF(cx - m * 5.5f * u, cy - 2.5f * u), new PointF(cx - m * 2.5f * u, cy + 0.5f * u) });
                            break;
                        }
                    default:
                        done = false;
                        break;
                }
            }
            g.SmoothingMode = old;
            return done;
        }

        static void DrawRoundF(Graphics g, Pen p, RectangleF r, float rad)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                float d = rad * 2;
                path.AddArc(r.Left, r.Top, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.DrawPath(p, path);
            }
        }

        public const int FaceIdle = 0, FaceThinking = 1, FaceWorking = 2, FaceWaiting = 3;

        public static void Face(Graphics g, RectangleF r, Theme th, int mood, int phase, bool blink)
        {
            float s = Math.Min(r.Width, r.Height);
            float ox = r.X + (r.Width - s) / 2f, oy = r.Y + (r.Height - s) / 2f;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool hc = th.HighContrast;
            Color fill = hc ? th.Back : Color.White;
            Color line = hc ? th.Fore : Color.FromArgb(0x2B, 0x2F, 0x36);
            Color eye = hc ? th.Fore : Color.FromArgb(0x16, 0xA3, 0x4A);
            float pw = Math.Max(1.1f, s * 0.0625f);
            float x0 = ox + s * 0.1406f, y0 = oy + s * 0.297f, x1 = ox + s * 0.859f, y1 = oy + s * 0.922f;
            float rad = s * 0.18f;
            float pivotX = ox + s * 0.5f, pivotY = oy + s * 0.3125f;
            GraphicsState gs = g.Save();
            g.TranslateTransform(pivotX, pivotY);
            g.RotateTransform(10f);
            g.TranslateTransform(-pivotX, -pivotY);
            using (Pen pen = StrokePen(hc ? th.Fore : th.Dark ? Color.FromArgb(0xC9, 0xCE, 0xD6) : line, Math.Max(1.1f, s * 0.0586f)))
                g.DrawLine(pen, ox + s * 0.5f, oy + s * 0.297f, ox + s * 0.5f, oy + s * 0.164f);
            float br = s * 0.0645f;
            using (SolidBrush b = new SolidBrush(hc ? th.Fore : eye))
                g.FillEllipse(b, ox + s * 0.5f - br, oy + s * 0.117f - br, br * 2, br * 2);
            g.Restore(gs);
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddArc(x0, y0, 2 * rad, 2 * rad, 180, 90);
                p.AddArc(x1 - 2 * rad, y0, 2 * rad, 2 * rad, 270, 90);
                p.AddArc(x1 - 2 * rad, y1 - 2 * rad, 2 * rad, 2 * rad, 0, 90);
                p.AddArc(x0, y1 - 2 * rad, 2 * rad, 2 * rad, 90, 90);
                p.CloseFigure();
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = StrokePen(line, pw)) g.DrawPath(pen, p);
            }
            float cx = ox + s * 0.5f, ey = oy + s * 0.516f;
            float dx = 0, dy = 0;
            if (mood == FaceThinking)
            {
                int c = phase % 36;
                if (c < 11) { dx = -s * 0.045f; dy = -s * 0.03f; }
                else if (c >= 18 && c < 29) { dx = s * 0.045f; dy = -s * 0.03f; }
            }
            else if (mood == FaceWorking) { dx = s * 0.02f; dy = s * 0.025f; }
            else if (mood == FaceWaiting) { dx = s * 0.04f; dy = -s * 0.035f; }
            float ew = Math.Max(1.4f, s * 0.0703f), eh = blink ? Math.Max(pw * 0.9f, s * 0.035f) : s * 0.1875f;
            float gap = s * 0.129f;
            using (SolidBrush b = new SolidBrush(eye))
            {
                foreach (float ex in new float[] { cx - gap + dx, cx + gap + dx })
                {
                    RectangleF er = new RectangleF(ex - ew / 2f, ey + dy - eh / 2f, ew, eh);
                    using (GraphicsPath ep = new GraphicsPath())
                    {
                        float d = Math.Min(ew, eh);
                        ep.AddArc(er.X, er.Y, d, d, 180, 180);
                        ep.AddArc(er.X, er.Bottom - d, d, d, 0, 180);
                        ep.CloseFigure();
                        g.FillPath(b, ep);
                    }
                }
            }
            if (s >= UI.S(26) && !hc)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(0xFF, 0xD5, 0xDD)))
                {
                    float bw = s * 0.082f, bh = s * 0.043f, by = oy + s * 0.67f;
                    g.FillEllipse(b, ox + s * 0.256f - bw / 2f + dx * 0.4f, by - bh / 2f, bw, bh);
                    g.FillEllipse(b, ox + s * 0.744f - bw / 2f + dx * 0.4f, by - bh / 2f, bw, bh);
                }
            using (Pen pen = StrokePen(line, Math.Max(1.1f, s * 0.035f)))
            using (SolidBrush b = new SolidBrush(line))
            using (GraphicsPath mp = new GraphicsPath())
            {
                float mx = dx * 0.5f, my = dy * 0.3f;
                PointF a = new PointF(ox + s * 0.42f + mx, oy + s * 0.68f + my), c2 = new PointF(ox + s * 0.58f + mx, oy + s * 0.68f + my);
                PointF ctl = new PointF(ox + s * 0.5f + mx, oy + s * 0.742f + my);
                mp.AddBezier(a, new PointF(a.X + (ctl.X - a.X) * 2f / 3f, a.Y + (ctl.Y - a.Y) * 2f / 3f), new PointF(c2.X + (ctl.X - c2.X) * 2f / 3f, c2.Y + (ctl.Y - c2.Y) * 2f / 3f), c2);
                mp.CloseFigure();
                g.FillPath(b, mp);
                g.DrawPath(pen, mp);
            }
            g.SmoothingMode = old;
        }

        public static void Spinner(Graphics g, Rectangle r, Color c, int phase)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height) - UI.S(4);
            float x = r.Left + (r.Width - s) / 2f, y = r.Top + (r.Height - s) / 2f;
            using (Pen bg = new Pen(Color.FromArgb(50, c), Math.Max(1.5f, 1.6f * UI.Scale)))
                g.DrawEllipse(bg, x, y, s, s);
            using (Pen p = StrokePen(c, Math.Max(1.5f, 1.6f * UI.Scale)))
                g.DrawArc(p, x, y, s, s, (phase * 30) % 360, 100);
            g.SmoothingMode = old;
        }
    }

    class GlyphToolButton : ToolStripButton
    {
        readonly string glyph;
        Theme th;

        public GlyphToolButton(string glyph, string name, string shortcut)
        {
            this.glyph = glyph;
            DisplayStyle = ToolStripItemDisplayStyle.None;
            AutoSize = false;
            Size = new Size(UI.S(28), UI.S(22));
            ToolTipText = shortcut == null ? name : name + " (" + shortcut + ")";
            AccessibleName = name;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public void SetTheme(Theme t) { th = t; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Color c;
            if (th == null) c = Enabled ? SystemColors.ControlText : SystemColors.GrayText;
            else if (!Enabled) c = th.MenuDisabled;
            else if (Selected && !Pressed && !th.MenuHoverFore.IsEmpty) c = th.MenuHoverFore;
            else c = th.ChromeFore;
            Glyphs.Draw(glyph, e.Graphics, new Rectangle(0, 0, Width, Height), c);
        }
    }

    class ToggleChip : Control
    {
        bool on, hover, keyDown, compact;
        Theme th = Theme.Light(Color.SteelBlue);
        public Color Surface = Color.Empty;
        public string Icon;
        public event EventHandler Changed;

        public ToggleChip(string text, string icon)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true;
            Text = text;
            Icon = icon;
            Font = UI.Font;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
            AccessibleDescription = "Off";
        }

        public bool On
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                AccessibleDescription = on ? "On" : "Off";
                Invalidate();
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }
        }

        public void Flip()
        {
            On = !on;
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void SetTheme(Theme t) { th = t; Invalidate(); }

        public int PreferredWidth { get { return UI.S(32) + TextRenderer.MeasureText(Text, Font).Width + UI.S(8); } }

        public const int CompactWidth = 32;

        public bool Compact
        {
            get { return compact; }
            set { if (compact == value) return; compact = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Flip(); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && e.Modifiers == Keys.None) { keyDown = true; e.Handled = e.SuppressKeyPress = true; }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && keyDown) { keyDown = false; Flip(); e.Handled = true; }
        }

        protected override void OnLeave(EventArgs e) { base.OnLeave(e); keyDown = false; }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acc(this); }

        class Acc : ControlAccessibleObject
        {
            readonly ToggleChip o;
            public Acc(ToggleChip t) : base(t) { o = t; }
            public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }
            public override string Name { get { return o.Text; } set { } }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates s = base.State;
                    if (o.on) s |= AccessibleStates.Checked;
                    return s;
                }
            }
            public override string DefaultAction { get { return o.on ? "Turn off" : "Turn on"; } }
            public override void DoDefaultAction() { o.Flip(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color surface = !Surface.IsEmpty ? Surface : Parent != null ? Parent.BackColor : th.Chrome;
            g.Clear(surface);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fg;
            if (th.HighContrast)
            {
                if (on) Glyphs.FillRound(g, th.Accent, r, UI.S(8));
                else if (hover) Glyphs.DrawRound(g, th.Fore, r, UI.S(8));
                fg = on ? SystemColors.HighlightText : th.Fore;
            }
            else
            {
                if (on)
                {
                    Glyphs.FillRound(g, Theme.Blend(surface, th.Accent, hover ? (th.Dark ? 0.30 : 0.18) : (th.Dark ? 0.22 : 0.12)), r, UI.S(8));
                    Glyphs.DrawRound(g, Theme.Blend(surface, th.Accent, th.Dark ? 0.55 : 0.40), r, UI.S(8));
                }
                else if (hover) Glyphs.Hover(g, th, th.ButtonHover, r, UI.S(8));
                fg = on ? (th.Dark ? Theme.Blend(th.Accent, Color.White, 0.25) : th.Accent) : hover ? th.Fore : th.ChromeDim;
            }
            if (compact) Glyphs.Draw(Icon, g, new Rectangle((Width - UI.S(18)) / 2, r.Y, UI.S(18), r.Height), fg);
            else
            {
                Glyphs.Draw(Icon, g, new Rectangle(UI.S(7), r.Y, UI.S(18), r.Height), fg);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(UI.S(27), r.Y, Math.Max(1, Width - UI.S(29)), r.Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
            if (Focused && ShowFocusCues) Glyphs.Ring(g, th.Accent, Rectangle.Inflate(r, -UI.S(1), -UI.S(1)), UI.S(8), Glyphs.FocusWidth);
        }
    }

    class ToggleSwitch : Control
    {
        bool on, hover, danger, chevron, downOnMenu, overMenu;
        Theme th = Theme.Light(Color.SteelBlue);
        readonly Tween knob;
        public Color Surface = Color.Empty;
        public string DangerText = "";
        public event EventHandler Changed, MenuRequested;

        public bool Danger
        {
            get { return danger; }
            set { if (danger == value) return; danger = value; AccessibleDescription = danger ? "On, " + DangerText : on ? "On" : "Off"; Invalidate(); }
        }

        public bool Chevron
        {
            get { return chevron; }
            set { if (chevron == value) return; chevron = value; Invalidate(); }
        }

        int ChevronW { get { return chevron ? UI.S(18) : 0; } }
        string Label { get { return danger && DangerText.Length > 0 ? DangerText : Text; } }

        public ToggleSwitch(string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            TabStop = true;
            Text = text;
            Font = UI.Font;
            AccessibleRole = AccessibleRole.CheckButton;
            Cursor = Cursors.Hand;
            knob = new Tween(0f, Invalidate);
        }

        public bool On
        {
            get { return on; }
            set { Set(value, false); }
        }

        void Set(bool v, bool animate)
        {
            if (on == v) return;
            on = v;
            AccessibleDescription = on ? "On" : "Off";
            knob.To(on ? 1f : 0f, 170, animate && IsHandleCreated && Visible);
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }

        public void SetTheme(Theme t) { th = t; Invalidate(); }

        static Size TrackSize { get { return new Size(UI.S(34), UI.S(18)); } }

        public int PreferredWidth { get { return TrackSize.Width + UI.S(8) + TextRenderer.MeasureText(Label, Font).Width + UI.S(6) + ChevronW; } }

        int MenuLeft { get { return UI.S(2) + TrackSize.Width + UI.S(4); } }

        bool InMenu(int x) { return chevron && x >= MenuLeft; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            downOnMenu = InMenu(e.X);
            if (e.Button == MouseButtons.Right && chevron && MenuRequested != null) MenuRequested(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool m = InMenu(e.X);
            if (m != overMenu) { overMenu = m; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; overMenu = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            MouseEventArgs me = e as MouseEventArgs;
            if (me != null && me.Button == MouseButtons.Right) return;
            if (downOnMenu && MenuRequested != null) { downOnMenu = false; MenuRequested(this, EventArgs.Empty); return; }
            Flip();
        }

        void Flip()
        {
            Set(!on, true);
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        bool spaceDown;

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || (chevron && (keyData == Keys.Down || keyData == (Keys.Alt | Keys.Down))) || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None) { spaceDown = true; e.Handled = e.SuppressKeyPress = true; }
            else if (chevron && e.KeyCode == Keys.Down && MenuRequested != null) { e.Handled = e.SuppressKeyPress = true; MenuRequested(this, EventArgs.Empty); }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && spaceDown) { spaceDown = false; Flip(); e.Handled = true; }
        }

        protected override void OnLeave(EventArgs e) { base.OnLeave(e); spaceDown = false; }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Acc(this); }

        class Acc : ControlAccessibleObject
        {
            readonly ToggleSwitch o;
            public Acc(ToggleSwitch t) : base(t) { o = t; }
            public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }
            public override string Name { get { return o.Text; } set { } }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates s = base.State;
                    if (o.on) s |= AccessibleStates.Checked;
                    return s;
                }
            }
            public override string DefaultAction { get { return o.on ? "Turn off" : "Turn on"; } }
            public override void DoDefaultAction() { o.Flip(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(!Surface.IsEmpty ? Surface : Parent != null ? Parent.BackColor : th.Chrome);
            Size ts = TrackSize;
            Rectangle track = new Rectangle(UI.S(2), (Height - ts.Height) / 2, ts.Width, ts.Height);
            Color red = th.HighContrast ? SystemColors.Highlight : th.Error;
            bool menuHot = hover && overMenu, trackHot = hover && !overMenu;
            if (menuHot && !th.HighContrast) Glyphs.Hover(g, th, th.ButtonHover, new Rectangle(MenuLeft, UI.S(2), Width - MenuLeft, Height - UI.S(4)), UI.S(6));
            if (danger)
            {
                System.Drawing.Drawing2D.SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                RectangleF tf = new RectangleF(track.X + 0.5f, track.Y + 0.5f, track.Width - 1f, track.Height - 1f);
                using (System.Drawing.Drawing2D.GraphicsPath p = Glyphs.Pill(tf))
                using (SolidBrush b = new SolidBrush(trackHot ? Theme.Blend(red, th.Dark ? Color.White : Color.Black, 0.12) : red))
                    g.FillPath(b, p);
                float h = track.Height, d = h - UI.S(6) + (trackHot ? UI.S(1) : 0), cx = track.Right - h / 2f, cy = track.Y + h / 2f;
                using (SolidBrush kb = new SolidBrush(Color.White)) g.FillEllipse(kb, cx - d / 2f, cy - d / 2f, d, d);
                g.SmoothingMode = old;
            }
            else SwitchPaint.Draw(g, track, knob.Value, chevron ? trackHot : hover, Enabled, th);
            Rectangle tr = new Rectangle(track.Right + UI.S(8), 0, Math.Max(1, Width - track.Right - UI.S(8) - ChevronW), Height);
            TextRenderer.DrawText(g, Label, Font, tr, danger ? red : on || menuHot ? th.ChromeFore : th.ChromeDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (chevron) Glyphs.Chevron(g, new Rectangle(Width - ChevronW - UI.S(1), 0, ChevronW, Height), danger ? red : menuHot ? th.ChromeFore : th.ChromeDim, UI.S(3), false);
            if (Focused && ShowFocusCues)
            {
                Rectangle ring = Rectangle.Inflate(track, UI.S(2), UI.S(2));
                Glyphs.Ring(g, th.Accent, ring, ring.Height / 2, Glyphs.FocusWidth);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) knob.Dispose();
            base.Dispose(disposing);
        }
    }

    static class Menus
    {
        public static ContextMenuStrip Create(Theme t)
        {
            ContextMenuStrip m = new ThemedMenu();
            m.Renderer = new ThemedRenderer(t);
            m.BackColor = t.MenuBack;
            m.Font = UI.Font;
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
            Prepare(m);
            return m;
        }

        static readonly List<ToolStripDropDown> open = new List<ToolStripDropDown>();

        public static void Track(ToolStripDropDown m)
        {
            m.Opened += delegate { if (!open.Contains(m)) open.Add(m); };
            m.Closed += delegate { open.Remove(m); };
            m.Disposed += delegate { open.Remove(m); };
        }

        public static void CloseAll()
        {
            foreach (ToolStripDropDown m in open.ToArray())
            {
                try { m.Close(); } catch { }
            }
            open.Clear();
        }

        public static void Prepare(ToolStripDropDown m)
        {
            Track(m);
            if (!(m is ThemedMenu)) m.Padding = new Padding(UI.S(2), UI.S(4), UI.S(2), UI.S(4));
            foreach (ToolStripItem it in m.Items) Pad(it);
            m.ItemAdded += delegate (object o, ToolStripItemEventArgs e) { Pad(e.Item); };
            m.HandleCreated += delegate { Round(m); };
            m.Opening += delegate { Round(m); };
            Round(m);
        }

        public static void Round(ToolStripDropDown m)
        {
            if (m == null || !m.IsHandleCreated) return;
            ThemedRenderer r = m.Renderer as ThemedRenderer;
            Native.RoundPopup(m.Handle, r != null ? r.Th.Border : Color.Empty);
        }

        static void Pad(ToolStripItem it)
        {
            NoteMenuItem note = it as NoteMenuItem;
            if (note != null) it.Padding = new Padding(it.Padding.Left, note.Heading ? UI.S(6) : 0, it.Padding.Right, note.Heading ? UI.S(2) : UI.S(4));
            else if (it is ToolStripMenuItem) it.Padding = new Padding(it.Padding.Left, UI.S(4), it.Padding.Right, UI.S(4));
            else if (it is ToolStripSeparator) it.Margin = new Padding(0, UI.S(2), 0, UI.S(2));
        }

        public static ToolStripMenuItem Item(string text, bool check, EventHandler click)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.Checked = check;
            if (click != null) mi.Click += click;
            return mi;
        }

        public static ToolStripMenuItem Detail(string text, string detail, bool check, EventHandler click)
        {
            DetailMenuItem mi = new DetailMenuItem(text, detail);
            mi.Checked = check;
            if (click != null) mi.Click += click;
            return mi;
        }

        public static ToolStripItem Header(string text, Theme t)
        {
            return Header(text, "", t);
        }

        public static ToolStripItem Header(string text, string detail, Theme t)
        {
            NoteMenuItem l = new NoteMenuItem(text, detail, true);
            l.ForeColor = t.ChromeDim;
            l.Font = Fonts.SmallSemi;
            return l;
        }

        public static ToolStripItem Hint(string text, Theme t)
        {
            NoteMenuItem l = new NoteMenuItem(text, "", false);
            l.ForeColor = t.ChromeDim;
            l.Font = Fonts.Note;
            return l;
        }
    }

    class WheelAcc
    {
        int rem;

        public int Take(int delta, int unit)
        {
            if ((rem > 0 && delta < 0) || (rem < 0 && delta > 0)) rem = 0;
            long total = (long)delta * unit + rem;
            long n = total / 120;
            rem = (int)(total - n * 120);
            return (int)n;
        }

        public int Notches(int delta) { return Take(delta, 1); }
    }

    class ThemedMenu : ContextMenuStrip
    {
        protected override Padding DefaultPadding
        {
            get
            {
                Padding p = base.DefaultPadding;
                return new Padding(p.Left, UI.S(4), p.Right, UI.S(4));
            }
        }
    }

    static class Dialogs
    {
        public static Button MakeButton(string text, Theme t, bool primary)
        {
            if (!t.HighContrast) return new PillButton(text, t, primary);
            Button b = new Button();
            b.Text = text;
            b.Font = UI.Font;
            b.UseMnemonic = false;
            b.BackColor = SystemColors.ButtonFace;
            b.ForeColor = SystemColors.ControlText;
            b.Height = UI.S(32);
            b.Width = Math.Max(UI.S(80), TextRenderer.MeasureText(text, UI.Font).Width + UI.S(32));
            return b;
        }

        public static Label MakeLabel(string text, Theme t, bool dim)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Font = UI.Font;
            l.ForeColor = dim ? t.ChromeDim : t.ChromeFore;
            l.BackColor = Color.Transparent;
            l.UseMnemonic = false;
            return l;
        }
    }
}

namespace MinGent
{
    class AppShell : Control
    {
        public Sidebar Side;
        public Control Center, Notes, Browser;
        public bool LeftCollapsed, NotesOn, BrowserOn, AgentOn = true;
        public int NotesWidth = 520, BrowserWidth = 620;
        public int CenterMin = 360, NotesMin = 300, BrowserMin = 340;
        bool autoRail;
        Theme th = Theme.Light(Color.SteelBlue);
        int drag = -1, dragStart, dragNotes, dragBrowser;
        public event EventHandler WidthsChanged;

        public AppShell()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        public void SetTheme(Theme t) { th = t; BackColor = t.Back; Invalidate(); }

        public bool RailShown { get { return LeftCollapsed || autoRail; } }

        int Gap { get { return Math.Max(4, UI.S(5)); } }

        bool NotesShown { get { return NotesOn && Notes != null; } }
        bool BrowserShown { get { return BrowserOn && Browser != null; } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Center == null || Side == null) return;
            int w = Width, h = Height, gap = Gap;
            if (!AgentOn && (NotesShown || BrowserShown))
            {
                if (NotesShown && BrowserShown)
                {
                    int nb = Math.Max(Math.Min(BrowserMin, w / 2), Math.Min(BrowserWidth, w - gap - NotesMin));
                    int nn = Math.Max(0, w - gap - nb);
                    Notes.SetBounds(0, 0, nn, h);
                    Browser.SetBounds(nn + gap, 0, nb, h);
                }
                else if (NotesShown) Notes.SetBounds(0, 0, w, h);
                else Browser.SetBounds(0, 0, w, h);
                Invalidate();
                return;
            }
            int gaps = (NotesShown ? gap : 0) + (BrowserShown ? gap : 0);
            int mins = (NotesShown ? NotesMin : 0) + (BrowserShown ? BrowserMin : 0);
            bool auto = !LeftCollapsed && w - Sidebar.FullWidth - gaps - mins < CenterMin;
            autoRail = auto;
            bool rail = LeftCollapsed || auto;
            if (Side.Collapsed != rail) Side.Collapsed = rail;
            int side = rail ? Sidebar.RailWidth : Sidebar.FullWidth;
            int avail = Math.Max(0, w - side - gaps);
            int nN = NotesShown ? NotesWidth : 0, nB = BrowserShown ? BrowserWidth : 0;
            int need = nN + nB + CenterMin - avail;
            if (need > 0 && nB > 0) { int cut = Math.Min(need, Math.Max(0, nB - BrowserMin)); nB -= cut; need -= cut; }
            if (need > 0 && nN > 0) { int cut = Math.Min(need, Math.Max(0, nN - NotesMin)); nN -= cut; need -= cut; }
            if (need > 0 && nN + nB > 0)
            {
                int allowed = Math.Max(0, avail - Math.Min(CenterMin, avail / 2));
                int total = nN + nB;
                if (total > allowed)
                {
                    int an = nB > 0 ? (int)((long)nN * allowed / total) : allowed;
                    nB = nB > 0 ? allowed - an : 0;
                    nN = nN > 0 ? an : 0;
                    if (nN == 0 && NotesShown) nN = allowed - nB;
                }
            }
            int cw = Math.Max(0, avail - nN - nB);
            Side.SetBounds(0, 0, side, h);
            int x = side;
            Center.SetBounds(x, 0, cw, h);
            x += cw;
            if (NotesShown) { x += gap; Notes.SetBounds(x, 0, nN, h); x += nN; }
            if (BrowserShown) { x += gap; Browser.SetBounds(x, 0, nB, h); }
            Invalidate();
        }

        Rectangle SplitRect(int which)
        {
            if (which == 0 && !AgentOn) return Rectangle.Empty;
            if (which == 0 && NotesShown) return new Rectangle(Notes.Left - Gap, 0, Gap, Height);
            if (which == 1 && BrowserShown) return new Rectangle(Browser.Left - Gap, 0, Gap, Height);
            return Rectangle.Empty;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(th.Back)) g.FillRectangle(b, ClientRectangle);
            for (int i = 0; i < 2; i++)
            {
                Rectangle r = SplitRect(i);
                if (r.IsEmpty) continue;
                if (i == 1 && NotesShown) using (SolidBrush b = new SolidBrush(th.Pane)) g.FillRectangle(b, r);
                int lw = drag == i ? Math.Max(2, UI.S(2)) : 1;
                using (SolidBrush b = new SolidBrush(drag == i ? th.Accent : th.Border)) g.FillRectangle(b, r.Right - lw, 0, lw, Height);
            }
        }

        int AvailForPanes { get { return Math.Max(0, Width - (RailShown ? Sidebar.RailWidth : Sidebar.FullWidth) - (NotesShown ? Gap : 0) - (BrowserShown ? Gap : 0) - CenterMin); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag >= 0)
            {
                int d = dragStart - e.X;
                if (drag == 0)
                {
                    int max = AvailForPanes - (BrowserShown ? Browser.Width : 0);
                    NotesWidth = Math.Max(NotesMin, Math.Min(Math.Max(NotesMin, max), dragNotes + d));
                }
                else if (!AgentOn)
                {
                    BrowserWidth = Math.Max(BrowserMin, Math.Min(Width - Gap - NotesMin, dragBrowser + d));
                }
                else if (NotesShown)
                {
                    int sum = dragNotes + dragBrowser;
                    int nb = Math.Max(BrowserMin, Math.Min(sum - NotesMin, dragBrowser + d));
                    BrowserWidth = nb;
                    NotesWidth = Math.Max(NotesMin, sum - nb);
                }
                else
                {
                    BrowserWidth = Math.Max(BrowserMin, Math.Min(Math.Max(BrowserMin, AvailForPanes), dragBrowser + d));
                }
                PerformLayout();
                Update();
                return;
            }
            bool over = SplitRect(0).Contains(e.Location) || SplitRect(1).Contains(e.Location);
            Cursor = over ? Cursors.VSplit : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            for (int i = 0; i < 2; i++)
            {
                Rectangle r = SplitRect(i);
                if (r.IsEmpty || !r.Contains(e.Location)) continue;
                drag = i;
                dragStart = e.X;
                dragNotes = NotesShown ? Notes.Width : NotesWidth;
                dragBrowser = BrowserShown ? Browser.Width : BrowserWidth;
                Capture = true;
                Invalidate();
                return;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (drag < 0) return;
            drag = -1;
            Capture = false;
            Invalidate();
            if (WidthsChanged != null) WidthsChanged(this, EventArgs.Empty);
        }
    }

    class ButtonBar : Control
    {
        readonly List<FlatButton> buttons = new List<FlatButton>();
        readonly List<int> gaps = new List<int>();
        readonly ThemedTip tip = new ThemedTip();
        Theme th = Theme.Light(Color.SteelBlue);

        public ButtonBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        public FlatButton Add(string glyph, string tipText, EventHandler click)
        {
            FlatButton b = new FlatButton("", glyph, false);
            b.Click += click;
            tip.SetToolTip(b, tipText);
            b.AccessibleName = tipText;
            buttons.Add(b);
            gaps.Add(0);
            Controls.Add(b);
            return b;
        }

        public void AddGap() { if (gaps.Count > 0) gaps[gaps.Count - 1] = UI.S(8); }

        readonly HashSet<FlatButton> hidden = new HashSet<FlatButton>();

        public void SetShown(FlatButton b, bool on)
        {
            if (on) hidden.Remove(b); else hidden.Add(b);
            b.Visible = on;
            PerformLayout();
        }

        public int PreferredWidth
        {
            get
            {
                int w = UI.S(10);
                for (int i = 0; i < buttons.Count; i++) if (!hidden.Contains(buttons[i])) w += UI.S(28) + UI.S(2) + gaps[i];
                return w;
            }
        }

        public void SetTheme(Theme t)
        {
            th = t;
            tip.Th = t;
            BackColor = t.Chrome;
            foreach (FlatButton b in buttons) b.SetTheme(t);
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int b = UI.S(28), y = UI.S(5) + Math.Max(0, (Height - UI.S(5) - b) / 2);
            int x = UI.S(4);
            for (int i = 0; i < buttons.Count; i++)
            {
                if (hidden.Contains(buttons[i])) continue;
                buttons[i].SetBounds(x, y, b, b);
                x += b + UI.S(2) + gaps[i];
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush br = new SolidBrush(th.Chrome)) e.Graphics.FillRectangle(br, ClientRectangle);
            using (Pen p = new Pen(th.TabBorder)) e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }
}
