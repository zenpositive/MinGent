using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MinGent
{
    static class AppHighlight
    {
        const int IdleSeconds = 45;
        static Control invoker;
        static Frame frame;
        static DateTime lastUse = DateTime.MinValue;
        static Color accent = Color.FromArgb(0x00, 0x67, 0xC0), onAccent = Color.White;
        static bool broken;

        public static void Init(Control ui)
        {
            invoker = ui;
        }

        public static void SetColors(Color a, Color on)
        {
            Post(delegate
            {
                accent = a;
                onAccent = on;
                if (frame != null) frame.Redraw();
            });
        }

        public static void Show(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            Post(delegate
            {
                lastUse = DateTime.UtcNow;
                if (frame == null) frame = new Frame();
                if (frame.Target != hwnd) frame.Track(hwnd);
                else frame.Wake();
            });
        }

        public static void Hide()
        {
            Post(delegate
            {
                if (frame != null) frame.FadeOut();
            });
        }

        public static void Close()
        {
            if (frame != null) { try { frame.Dispose(); } catch { } frame = null; }
            if (watch != null) { try { watch.Stop(); watch.Dispose(); } catch { } watch = null; }
        }

        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out WinRect r);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out WinRect r, int size);
        [StructLayout(LayoutKind.Sequential)] struct WinRect { public int Left, Top, Right, Bottom; }

        static readonly List<IntPtr> seen = new List<IntPtr>(), placed = new List<IntPtr>();
        static Rectangle oldBounds, putBounds;
        static FormWindowState oldState;
        static bool restorePending;
        static int misses;
        static System.Windows.Forms.Timer watch;

        public static bool Arrange(IntPtr app)
        {
            if (app == IntPtr.Zero || broken || Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
            Control c = invoker;
            if (c == null || c.IsDisposed || !c.IsHandleCreated) return false;
            bool moved = false;
            using (System.Threading.ManualResetEvent done = new System.Threading.ManualResetEvent(false))
            {
                MethodInvoker work = delegate
                {
                    try { moved = ArrangeNow(app); }
                    catch (Exception) { moved = false; }
                    finally { try { done.Set(); } catch (Exception) { } }
                };
                try
                {
                    if (c.InvokeRequired) c.BeginInvoke(work);
                    else work();
                }
                catch (Exception) { return false; }
                if (!done.WaitOne(3000)) return false;
            }
            return moved;
        }

        static bool ArrangeNow(IntPtr app)
        {
            seen.RemoveAll(delegate (IntPtr h) { return !IsWindow(h); });
            if (seen.Contains(app)) return false;
            seen.Add(app);
            Form main = invoker as Form ?? invoker.FindForm();
            if (main == null || !main.Visible || main.WindowState == FormWindowState.Minimized || !IsWindow(app) || !IsWindowVisible(app) || IsIconic(app)) return false;
            if (GetWindow(app, 4) != IntPtr.Zero) return false;
            System.Text.StringBuilder cls = new System.Text.StringBuilder(64);
            GetClassName(app, cls, 64);
            if (cls.ToString() == "#32770") return false;
            int ex = GetWindowLong(app, -20);
            if ((ex & 0x80) != 0 || (ex & 0x8) != 0) return false;
            Rectangle appR = Visible(app), mainR = Visible(main.Handle);
            Screen scr = Screen.FromHandle(main.Handle);
            Rectangle work = scr.WorkingArea;
            if (work.Width < UI.S(1100) || work.Height < UI.S(560)) return false;
            if (!scr.Bounds.IntersectsWith(appR)) return false;
            Rectangle both = Rectangle.Intersect(appR, mainR);
            long overlap = (long)both.Width * both.Height, smaller = Math.Min((long)appR.Width * appR.Height, (long)mainR.Width * mainR.Height);
            if (both.IsEmpty || overlap * 4 < smaller) return false;
            bool padLeft = mainR.Left + mainR.Width / 2 <= work.Left + work.Width / 2;
            int half = work.Width / 2;
            Rectangle left = new Rectangle(work.Left, work.Top, half, work.Height), right = new Rectangle(work.Left + half, work.Top, work.Width - half, work.Height);
            if (!restorePending)
            {
                oldState = main.WindowState;
                oldBounds = main.WindowState == FormWindowState.Normal ? main.Bounds : main.RestoreBounds;
            }
            if (main.WindowState != FormWindowState.Normal) main.WindowState = FormWindowState.Normal;
            Place(main.Handle, padLeft ? left : right, true);
            putBounds = main.Bounds;
            restorePending = true;
            if (IsZoomed(app)) ShowWindow(app, 9);
            bool sizable = (GetWindowLong(app, -16) & 0x40000) != 0;
            Place(app, padLeft ? right : left, sizable);
            if (!placed.Contains(app)) placed.Add(app);
            misses = 0;
            if (watch == null)
            {
                watch = new System.Windows.Forms.Timer();
                watch.Interval = 1000;
                watch.Tick += delegate
                {
                    try { Watch(); }
                    catch (Exception) { if (watch != null) watch.Stop(); }
                };
            }
            watch.Start();
            return true;
        }

        static Rectangle Visible(IntPtr h)
        {
            WinRect r;
            try
            {
                if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(WinRect))) == 0 && r.Right > r.Left)
                    return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
            catch (Exception) { }
            GetWindowRect(h, out r);
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        static void Place(IntPtr h, Rectangle target, bool resize)
        {
            WinRect wr;
            GetWindowRect(h, out wr);
            Rectangle outer = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom), vis = Visible(h);
            int l = Math.Max(0, vis.Left - outer.Left), t = Math.Max(0, vis.Top - outer.Top), r = Math.Max(0, outer.Right - vis.Right), b = Math.Max(0, outer.Bottom - vis.Bottom);
            const uint flags = 0x0004 | 0x0010 | 0x0200;
            if (resize)
            {
                SetWindowPos(h, IntPtr.Zero, target.Left - l, target.Top - t, target.Width + l + r, target.Height + t + b, flags);
                return;
            }
            int x = target.Left + Math.Max(0, (target.Width - vis.Width) / 2), y = target.Top + Math.Max(0, (target.Height - vis.Height) / 3);
            SetWindowPos(h, IntPtr.Zero, x - l, y - t, 0, 0, flags | 0x0001);
        }

        static void Watch()
        {
            placed.RemoveAll(delegate (IntPtr h) { return !IsWindow(h); });
            bool showing = placed.Exists(delegate (IntPtr h) { return IsWindowVisible(h); });
            if (showing) { misses = 0; return; }
            if (++misses < 2) return;
            misses = 0;
            placed.Clear();
            watch.Stop();
            if (!restorePending) return;
            restorePending = false;
            Form main = invoker as Form ?? (invoker != null ? invoker.FindForm() : null);
            if (main == null || main.IsDisposed || main.WindowState != FormWindowState.Normal || main.Bounds != putBounds) return;
            main.Bounds = oldBounds;
            if (oldState == FormWindowState.Maximized) main.WindowState = FormWindowState.Maximized;
        }

        static void Post(Action a)
        {
            if (broken || Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            Control c = invoker;
            if (c == null || c.IsDisposed || !c.IsHandleCreated) return;
            Action safe = delegate
            {
                try { a(); }
                catch (Exception) { broken = true; Close(); }
            };
            try
            {
                if (c.InvokeRequired) c.BeginInvoke(safe);
                else safe();
            }
            catch (Exception) { }
        }

        internal static bool Idle { get { return (DateTime.UtcNow - lastUse).TotalSeconds > IdleSeconds; } }

        internal static void DrawFrame(Graphics g, Rectangle win, bool inside, bool labelAbove, bool rounded)
        {
            Color a = accent;
            float line = Math.Max(2f, UI.S(3)), glow = UI.S(12);
            float radius = rounded && !inside ? UI.S(8) : 0;
            RectangleF edge = inside ? new RectangleF(win.Left + line / 2, win.Top + line / 2, win.Width - line, win.Height - line)
                                     : new RectangleF(win.Left - line / 2, win.Top - line / 2, win.Width + line, win.Height + line);
            if (!inside)
                for (int i = (int)glow; i >= 1; i--)
                {
                    float t = i / glow;
                    int al = (int)(70 * (1 - t) * (1 - t));
                    if (al <= 0) continue;
                    using (Pen p = new Pen(Color.FromArgb(al, a), 2f))
                    using (GraphicsPath gp = RoundRect(RectangleF.Inflate(edge, i, i), radius + i))
                        g.DrawPath(p, gp);
                }
            else
                for (int i = UI.S(8); i >= 1; i--)
                {
                    float t = i / (float)UI.S(8);
                    int al = (int)(55 * (1 - t) * (1 - t));
                    if (al <= 0) continue;
                    using (Pen p = new Pen(Color.FromArgb(al, a), 2f))
                    using (GraphicsPath gp = RoundRect(RectangleF.Inflate(edge, -i, -i), 0))
                        g.DrawPath(p, gp);
                }
            using (Pen p = new Pen(a, line))
            using (GraphicsPath gp = RoundRect(edge, radius))
                g.DrawPath(p, gp);
            string text = win.Width >= UI.S(340) ? "MinGent is using this window" : "MinGent";
            using (Font f = new Font("Segoe UI Semibold", 8.5f * (float)UI.Scale, FontStyle.Regular, GraphicsUnit.Point))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                SizeF ts = g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic);
                float dot = UI.S(6), gap = UI.S(6), padX = UI.S(10), h = UI.S(22);
                float w = padX * 2 + dot + gap + ts.Width;
                float x = win.Left + (win.Width - w) / 2;
                float y = labelAbove ? win.Top - line - h + UI.S(1) : win.Top + (inside ? UI.S(6) : line + UI.S(4));
                RectangleF pill = new RectangleF(x, y, w, h);
                using (GraphicsPath shadow = RoundRect(new RectangleF(pill.X, pill.Y + UI.S(1), pill.Width, pill.Height), h / 2))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(46, 0, 0, 0)))
                    g.FillPath(sb, shadow);
                using (GraphicsPath gp = RoundRect(pill, h / 2))
                using (SolidBrush fill = new SolidBrush(a))
                    g.FillPath(fill, gp);
                Color on = onAccent;
                using (SolidBrush db = new SolidBrush(Color.FromArgb(220, on)))
                    g.FillEllipse(db, x + padX, y + (h - dot) / 2, dot, dot);
                using (SolidBrush tb = new SolidBrush(on))
                    g.DrawString(text, f, tb, x + padX + dot + gap, y + (h - ts.Height) / 2, StringFormat.GenericTypographic);
            }
        }

        internal static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath gp = new GraphicsPath();
            if (radius <= 0.5f || r.Width < 2 * radius || r.Height < 2 * radius) { gp.AddRectangle(r); return gp; }
            float d = radius * 2;
            gp.AddArc(r.Left, r.Top, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        sealed class Frame : NativeWindow, IDisposable
        {
            [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
            [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
            [StructLayout(LayoutKind.Sequential)] struct SIZE { public int W, H; }
            [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }

            [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
            [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
            [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
            [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
            [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
            [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
            [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
            [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
            [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
            [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
            [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
            [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
            [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT at, ref SIZE size, IntPtr src, ref POINT from, int key, ref BLEND blend, int flags);
            [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")] static extern bool UpdateBlend(IntPtr h, IntPtr dst, IntPtr at, IntPtr size, IntPtr src, IntPtr from, int key, ref BLEND blend, int flags);
            [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
            [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
            [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
            [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
            [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
            [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmGetInt(IntPtr h, int attr, out int v, int size);

            const int WS_POPUP = unchecked((int)0x80000000), WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
            const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200, GW_HWNDPREV = 3;
            static readonly IntPtr HWND_TOP = IntPtr.Zero, HWND_TOPMOST = new IntPtr(-1);

            readonly Timer timer = new Timer();
            readonly bool rounded = Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
            IntPtr hwnd = IntPtr.Zero;
            Rectangle drawn = Rectangle.Empty, bounds = Rectangle.Empty;
            bool inside, labelAbove, shown, dirty;
            int alpha, goal;

            public Frame()
            {
                CreateParams cp = new CreateParams();
                cp.Caption = "MinGent";
                cp.Style = WS_POPUP;
                cp.ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                cp.X = -32000;
                cp.Y = -32000;
                cp.Width = 1;
                cp.Height = 1;
                CreateHandle(cp);
                try { SetWindowDisplayAffinity(Handle, 0x11); } catch (Exception) { }
                timer.Interval = 100;
                timer.Tick += delegate
                {
                    try { Tick(); }
                    catch (Exception) { Vanish(); }
                };
            }

            public IntPtr Target { get { return hwnd; } }

            public void Track(IntPtr h)
            {
                hwnd = h;
                drawn = Rectangle.Empty;
                dirty = true;
                Wake();
            }

            public void Wake()
            {
                goal = 255;
                timer.Interval = 40;
                timer.Start();
                Tick();
            }

            public void Redraw()
            {
                dirty = true;
                if (shown) Tick();
            }

            public void FadeOut()
            {
                goal = 0;
                if (shown)
                {
                    timer.Interval = 40;
                    timer.Start();
                }
                else Vanish();
            }

            void Tick()
            {
                if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) { Vanish(); return; }
                if (goal > 0 && AppHighlight.Idle) goal = 0;
                bool cloaked = false;
                try { int c; cloaked = DwmGetInt(hwnd, 14, out c, 4) == 0 && c != 0; } catch (Exception) { }
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || cloaked)
                {
                    if (shown) { ShowWindow(Handle, 0); shown = false; }
                    if (goal == 0) { timer.Stop(); alpha = 0; }
                    return;
                }
                Rectangle r = FrameRect(hwnd);
                if (r.Width < 8 || r.Height < 8) return;
                Rectangle work = Screen.FromRectangle(r).Bounds;
                bool fills = r.Left <= work.Left && r.Top <= work.Top && r.Right >= work.Right && r.Bottom >= work.Bottom;
                bool nowInside = fills || IsZoomed(hwnd);
                int label = UI.S(24), m = UI.S(14);
                bool above = !nowInside && r.Top - label - UI.S(4) >= Screen.FromRectangle(r).WorkingArea.Top;
                if (r != drawn || nowInside != inside || above != labelAbove || dirty)
                {
                    inside = nowInside;
                    labelAbove = above;
                    int top = inside ? 0 : m + (labelAbove ? label : 0);
                    int pad = inside ? 0 : m;
                    Rectangle b = new Rectangle(r.Left - pad, r.Top - top, r.Width + 2 * pad, r.Height + top + pad);
                    if (b.Size != bounds.Size || dirty || r.Size != drawn.Size) Paint(b, new Rectangle(pad, top, r.Width, r.Height));
                    else SetWindowPos(Handle, IntPtr.Zero, b.Left, b.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                    bounds = b;
                    drawn = r;
                    dirty = false;
                }
                Order();
                if (!shown) { ShowWindow(Handle, 4); shown = true; }
                if (alpha != goal)
                {
                    alpha = goal > alpha ? Math.Min(goal, alpha + 64) : Math.Max(goal, alpha - 52);
                    SetAlpha(alpha);
                }
                if (alpha == goal)
                {
                    if (goal == 0) { ShowWindow(Handle, 0); shown = false; timer.Stop(); hwnd = IntPtr.Zero; return; }
                    timer.Interval = 100;
                }
            }

            void Vanish()
            {
                timer.Stop();
                if (shown) ShowWindow(Handle, 0);
                shown = false;
                alpha = 0;
                hwnd = IntPtr.Zero;
                drawn = Rectangle.Empty;
            }

            static Rectangle FrameRect(IntPtr h)
            {
                RECT r;
                try
                {
                    if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0 && r.Right > r.Left)
                        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                }
                catch (Exception) { }
                GetWindowRect(h, out r);
                return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }

            void Order()
            {
                IntPtr above = GetWindow(hwnd, GW_HWNDPREV);
                if (above == Handle) return;
                bool topmost = (GetWindowLong(hwnd, -20) & WS_EX_TOPMOST) != 0;
                IntPtr after = topmost ? HWND_TOPMOST : above == IntPtr.Zero || (GetWindowLong(above, -20) & WS_EX_TOPMOST) != 0 ? HWND_TOP : above;
                SetWindowPos(Handle, after, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }

            void SetAlpha(int a)
            {
                BLEND bl = new BLEND { Op = 0, Flags = 0, Alpha = (byte)Math.Max(0, Math.Min(255, a)), Format = 1 };
                UpdateBlend(Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref bl, 2);
            }

            void Paint(Rectangle b, Rectangle win)
            {
                if (b.Width <= 0 || b.Height <= 0 || b.Width > 16384 || b.Height > 16384) return;
                using (Bitmap bm = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bm))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        AppHighlight.DrawFrame(g, win, inside, labelAbove, rounded);
                    }
                    IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbm = IntPtr.Zero, old = IntPtr.Zero;
                    try
                    {
                        hbm = bm.GetHbitmap(Color.FromArgb(0));
                        old = SelectObject(mem, hbm);
                        POINT at = new POINT { X = b.Left, Y = b.Top }, from = new POINT();
                        SIZE size = new SIZE { W = b.Width, H = b.Height };
                        BLEND bl = new BLEND { Op = 0, Flags = 0, Alpha = (byte)Math.Max(0, Math.Min(255, alpha)), Format = 1 };
                        UpdateLayeredWindow(Handle, screen, ref at, ref size, mem, ref from, 0, ref bl, 2);
                    }
                    finally
                    {
                        if (old != IntPtr.Zero) SelectObject(mem, old);
                        if (hbm != IntPtr.Zero) DeleteObject(hbm);
                        DeleteDC(mem);
                        ReleaseDC(IntPtr.Zero, screen);
                    }
                }
            }

            public void Dispose()
            {
                timer.Stop();
                timer.Dispose();
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
        }
    }
}
