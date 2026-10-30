using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MinGent
{
    static partial class Glyphs
    {
        static float PenW { get { return Math.Max(1.25f, 1.35f * UI.Scale); } }

        public static void X(Graphics g, Rectangle r, Color c, int half)
        {
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(c, PenW))
            {
                g.DrawLine(p, cx - half, cy - half, cx + half, cy + half);
                g.DrawLine(p, cx - half, cy + half, cx + half, cy - half);
            }
            g.SmoothingMode = old;
        }

        public static void Plus(Graphics g, Rectangle r, Color c, int half)
        {
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(c, PenW))
            {
                g.DrawLine(p, cx - half, cy, cx + half, cy);
                g.DrawLine(p, cx, cy - half, cx, cy + half);
            }
            g.SmoothingMode = old;
        }

        public static void Chevron(Graphics g, Rectangle r, Color c, int half, bool up)
        {
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            float h = half / 2f;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(c, PenW))
            {
                if (up) g.DrawLines(p, new PointF[] { new PointF(cx - half, cy + h), new PointF(cx, cy - h), new PointF(cx + half, cy + h) });
                else g.DrawLines(p, new PointF[] { new PointF(cx - half, cy - h), new PointF(cx, cy + h), new PointF(cx + half, cy - h) });
            }
            g.SmoothingMode = old;
        }

        public static void ChevronH(Graphics g, Rectangle r, Color c, int half, bool left)
        {
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, w = half / 2f;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(c, PenW))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                if (left) g.DrawLines(p, new PointF[] { new PointF(cx + w, cy - half), new PointF(cx - w, cy), new PointF(cx + w, cy + half) });
                else g.DrawLines(p, new PointF[] { new PointF(cx - w, cy - half), new PointF(cx + w, cy), new PointF(cx - w, cy + half) });
            }
            g.SmoothingMode = old;
        }

        public static void Dot(Graphics g, Rectangle r, Color c, int d)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(c))
                g.FillEllipse(b, r.Left + (r.Width - d) / 2f, r.Top + (r.Height - d) / 2f, d, d);
            g.SmoothingMode = old;
        }

        public static GraphicsPath Round(Rectangle r, int radius, bool bottomToo)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            if (bottomToo)
            {
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            }
            else
            {
                p.AddLine(r.Right, r.Top + d / 2, r.Right, r.Bottom);
                p.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
            }
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, Rectangle r, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius, true))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
            g.SmoothingMode = old;
        }

        public static void Hover(Graphics g, Theme th, Color fill, Rectangle r, int radius)
        {
            if (th != null && th.HighContrast) DrawRound(g, SystemColors.Highlight, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), radius);
            else FillRound(g, fill, r, radius);
        }

        public static void DrawRound(Graphics g, Color c, Rectangle r, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius, true))
            using (Pen pen = new Pen(c))
                g.DrawPath(pen, p);
            g.SmoothingMode = old;
        }
    }

    class TabNav : Control
    {
        public readonly FlatButton Back = new FlatButton("", "left", false);
        public readonly FlatButton Forward = new FlatButton("", "right", false);
        Theme th = Theme.Light(Color.SteelBlue);

        public TabNav()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Controls.Add(Back);
            Controls.Add(Forward);
            Back.Enabled = Forward.Enabled = false;
        }

        public void SetTheme(Theme t) { th = t; BackColor = t.Chrome; Back.SetTheme(t); Forward.SetTheme(t); Invalidate(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int b = UI.S(26), y = UI.S(8);
            Back.SetBounds(UI.S(6), y, b, b);
            Forward.SetBounds(UI.S(6) + b + UI.S(1), y, b, b);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(th.Chrome)) e.Graphics.FillRectangle(b, ClientRectangle);
            using (Pen p = new Pen(th.TabBorder)) e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    class ThemedTip : ToolTip
    {
        public Theme Th;

        public ThemedTip()
        {
            OwnerDraw = true;
            InitialDelay = 600;
            ReshowDelay = 100;
            Popup += delegate(object s, PopupEventArgs e)
            {
                Size sz = TextRenderer.MeasureText(GetToolTip(e.AssociatedControl), UI.Font);
                e.ToolTipSize = new Size(sz.Width + UI.S(16), sz.Height + UI.S(10));
            };
            Draw += delegate(object s, DrawToolTipEventArgs e)
            {
                Theme t = Th ?? Theme.Light(Color.SteelBlue);
                using (SolidBrush b = new SolidBrush(t.MenuBack)) e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(t.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, UI.Font, new Rectangle(e.Bounds.X + UI.S(8), e.Bounds.Y, Math.Max(1, e.Bounds.Width - UI.S(16)), e.Bounds.Height), t.MenuFore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };
        }
    }

    class TabStrip : Control
    {
        public List<Document> Docs = new List<Document>();
        public int Active = -1;
        Theme th = Theme.Light(Color.SteelBlue);
        int scrollX, hover = -1, hoverBtn, pressIdx = -1, closePress = -1, middlePress = -1;
        bool hoverClose, dragging, overflow;
        Point pressPt;
        int tabW = 150, visW;
        readonly ThemedTip tip = new ThemedTip();
        string tipText;

        public event Action<int> SelectRequested, CloseRequested;
        public event EventHandler NewRequested, ListRequested;
        public event Action<int, int> MoveRequested;
        public event Action<int, Point> MenuRequested;

        public TabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            AccessibleName = "Tabs";
            AccessibleRole = AccessibleRole.PageTabList;
            Height = UI.S(38);
            Font = UI.Font;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        public void SetTheme(Theme t)
        {
            th = t;
            tip.Th = t;
            BackColor = t.Chrome;
            Invalidate();
        }

        int BtnW { get { return UI.S(38); } }

        void Calc()
        {
            int n = Docs.Count;
            int minW = UI.S(124), maxW = UI.S(220);
            int avail = Math.Max(minW, Width - BtnW - UI.S(4));
            tabW = n > 0 ? Math.Max(minW, Math.Min(maxW, avail / n)) : maxW;
            overflow = n * tabW > avail;
            visW = overflow ? Math.Max(minW, avail - BtnW) : avail;
            if (overflow) tabW = visW / Math.Max(1, visW / minW);
            int maxScroll = Math.Max(0, n * tabW - visW);
            scrollX = Math.Max(0, Math.Min(scrollX, maxScroll));
        }

        Rectangle TabRect(int i) { return new Rectangle(UI.S(4) + i * tabW - scrollX, 0, tabW, Height); }

        Rectangle CloseRect(int i)
        {
            Rectangle r = TabRect(i);
            int sz = UI.S(22);
            return new Rectangle(r.Right - sz - UI.S(8), (Height - sz) / 2 + UI.S(2), sz, sz);
        }

        Rectangle PlusRect()
        {
            int x = overflow ? visW : UI.S(4) + Docs.Count * tabW - scrollX;
            int sz = UI.S(28);
            return new Rectangle(x + UI.S(4), (Height - sz) / 2 + UI.S(2), sz, sz);
        }

        Rectangle ListRect()
        {
            if (!overflow) return Rectangle.Empty;
            int sz = UI.S(28);
            return new Rectangle(Width - sz - UI.S(6), (Height - sz) / 2 + UI.S(2), sz, sz);
        }

        int TabAt(Point p)
        {
            if (p.X >= visW || p.X < UI.S(4) - scrollX) return -1;
            int i = (p.X - UI.S(4) + scrollX) / Math.Max(1, tabW);
            return i >= 0 && i < Docs.Count ? i : -1;
        }

        public void EnsureVisible(int i)
        {
            Calc();
            if (i < 0 || i >= Docs.Count) return;
            int l = i * tabW, r = l + tabW;
            if (l < scrollX) scrollX = l;
            else if (r > scrollX + visW) scrollX = r - visW;
            Calc();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            EnsureVisible(Active);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Calc();
            using (SolidBrush b = new SolidBrush(th.Chrome)) g.FillRectangle(b, ClientRectangle);
            using (Pen p = new Pen(th.TabBorder)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            int n = Docs.Count;
            int top = UI.S(6), radius = UI.S(7);
            g.SetClip(new Rectangle(0, 0, visW, Height));
            for (int i = 0; i < n; i++)
            {
                Rectangle r = TabRect(i);
                if (r.Right < 0 || r.Left > visW) continue;
                bool act = i == Active, hov = i == hover && !dragging;
                Rectangle tr = new Rectangle(r.Left + UI.S(1), top, r.Width - UI.S(2), Height - top);
                if (act)
                {
                    SmoothingMode old = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = Glyphs.Round(tr, radius, false))
                    {
                        using (SolidBrush b = new SolidBrush(th.Back)) g.FillPath(b, path);
                        using (Pen p = new Pen(th.TabBorder)) g.DrawPath(p, path);
                    }
                    g.SmoothingMode = old;
                    using (SolidBrush b = new SolidBrush(th.Back)) g.FillRectangle(b, tr.Left + 1, Height - 2, tr.Width - 1, 2);
                    using (SolidBrush b = new SolidBrush(th.Accent))
                        g.FillRectangle(b, tr.Left + radius, tr.Top, Math.Max(1, tr.Width - radius * 2), Math.Max(2, UI.S(2)));
                }
                else if (hov)
                {
                    Glyphs.Hover(g, th, th.TabHover, new Rectangle(tr.Left + UI.S(2), tr.Top + UI.S(2), tr.Width - UI.S(4), tr.Height - UI.S(6)), radius);
                }
                else if (i + 1 != Active && i != hover - 1)
                {
                    using (Pen p = new Pen(th.TabBorder))
                        g.DrawLine(p, r.Right - 1, top + UI.S(8), r.Right - 1, Height - UI.S(9));
                }

                Rectangle textR = new Rectangle(tr.Left + UI.S(12), tr.Top, Math.Max(0, tr.Width - UI.S(12) - UI.S(34)), tr.Height - UI.S(2));
                TextRenderer.DrawText(g, Docs[i].DisplayName, Font, textR, act ? th.Fore : th.ChromeDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);

                Rectangle cr = CloseRect(i);
                bool overClose = hov && hoverClose;
                if (overClose) Glyphs.Hover(g, th, th.ButtonHover, cr, UI.S(4));
                if (Docs[i].Modified && !overClose) Glyphs.Dot(g, cr, act ? th.Fore : th.ChromeDim, UI.S(8));
                else if (act || hov) Glyphs.X(g, cr, act ? th.Fore : th.ChromeDim, UI.S(4));
            }
            g.ResetClip();

            Rectangle pr = PlusRect();
            if (hoverBtn == 1) Glyphs.Hover(g, th, th.ButtonHover, pr, UI.S(4));
            Glyphs.Plus(g, pr, th.ChromeFore, UI.S(6));
            if (overflow)
            {
                Rectangle lr = ListRect();
                if (hoverBtn == 2) Glyphs.Hover(g, th, th.ButtonHover, lr, UI.S(4));
                Glyphs.Chevron(g, lr, th.ChromeFore, UI.S(5), false);
            }
        }

        void UpdateTip()
        {
            string t = null;
            if (hover >= 0 && hover < Docs.Count) t = Docs[hover].Path ?? Docs[hover].DisplayName;
            else if (hoverBtn == 1) t = "New tab (Ctrl+N)";
            else if (hoverBtn == 2) t = "All open tabs";
            if (t == tipText) return;
            tipText = t;
            tip.Hide(this);
            tip.SetToolTip(this, t ?? "");
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressIdx >= 0 && (e.Button & MouseButtons.Left) != 0)
            {
                if (!dragging && Math.Abs(e.X - pressPt.X) > SystemInformation.DragSize.Width) dragging = true;
                if (dragging)
                {
                    int target = Math.Max(0, Math.Min(Docs.Count - 1, (e.X - UI.S(4) + scrollX) / Math.Max(1, tabW)));
                    if (target != pressIdx && MoveRequested != null)
                    {
                        MoveRequested(pressIdx, target);
                        pressIdx = target;
                    }
                    Invalidate();
                    return;
                }
            }
            int h = TabAt(e.Location);
            bool hc = h >= 0 && CloseRect(h).Contains(e.Location);
            int hb = PlusRect().Contains(e.Location) ? 1 : (overflow && ListRect().Contains(e.Location) ? 2 : 0);
            if (hb != 0) h = -1;
            if (h != hover || hc != hoverClose || hb != hoverBtn)
            {
                hover = h; hoverClose = hc; hoverBtn = hb;
                Invalidate();
                UpdateTip();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1; hoverBtn = 0; hoverClose = false;
            Invalidate();
            UpdateTip();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = TabAt(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                if (PlusRect().Contains(e.Location))
                {
                    if (JustClosedHere(e.Location)) return;
                    if (NewRequested != null) NewRequested(this, EventArgs.Empty);
                    return;
                }
                if (overflow && ListRect().Contains(e.Location)) { if (ListRequested != null) ListRequested(this, EventArgs.Empty); return; }
                if (i < 0) return;
                if (CloseRect(i).Contains(e.Location)) { closePress = i; return; }
                pressIdx = i;
                pressPt = e.Location;
                dragging = false;
                if (i != Active && SelectRequested != null) SelectRequested(i);
            }
            else if (e.Button == MouseButtons.Middle && i >= 0) middlePress = i;
            else if (e.Button == MouseButtons.Right && i >= 0 && MenuRequested != null) MenuRequested(i, e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = TabAt(e.Location);
            if (e.Button == MouseButtons.Left && closePress >= 0)
            {
                if (i == closePress && CloseRect(i).Contains(e.Location) && CloseRequested != null) { lastClose = DateTime.UtcNow; lastClosePt = e.Location; CloseRequested(i); }
                closePress = -1;
            }
            if (e.Button == MouseButtons.Middle && middlePress >= 0)
            {
                if (i == middlePress && CloseRequested != null) CloseRequested(i);
                middlePress = -1;
            }
            pressIdx = -1;
            if (dragging) { dragging = false; Invalidate(); }
        }

        DateTime lastClose = DateTime.MinValue;
        Point lastClosePt;

        bool JustClosedHere(Point p)
        {
            if ((DateTime.UtcNow - lastClose).TotalMilliseconds >= SystemInformation.DoubleClickTime + 100) return false;
            Size dz = SystemInformation.DoubleClickSize;
            return Math.Abs(p.X - lastClosePt.X) <= dz.Width && Math.Abs(p.Y - lastClosePt.Y) <= dz.Height;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if ((DateTime.UtcNow - lastClose).TotalMilliseconds < SystemInformation.DoubleClickTime + 100) return;
            if (e.Button == MouseButtons.Left && TabAt(e.Location) < 0 && !PlusRect().Contains(e.Location) &&
                !(overflow && ListRect().Contains(e.Location)) && NewRequested != null)
                NewRequested(this, EventArgs.Empty);
        }

        readonly WheelAcc wheel = new WheelAcc();

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!overflow) return;
            scrollX -= wheel.Take(e.Delta, tabW);
            Calc();
            Invalidate();
        }
    }
}
