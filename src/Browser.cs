using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MinGent
{
    [ComVisible(true)]
    class WvEnvDone : ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
    {
        readonly Action<int, ICoreWebView2Environment> cb;
        public WvEnvDone(Action<int, ICoreWebView2Environment> cb) { this.cb = cb; }
        public int Invoke(int hr, ICoreWebView2Environment env) { try { cb(hr, env); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvControllerDone : ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
    {
        readonly Action<int, ICoreWebView2Controller2> cb;
        public WvControllerDone(Action<int, ICoreWebView2Controller2> cb) { this.cb = cb; }
        public int Invoke(int hr, ICoreWebView2Controller2 c) { try { cb(hr, c); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvScriptDone : ICoreWebView2ExecuteScriptCompletedHandler, ICoreWebView2CallDevToolsProtocolMethodCompletedHandler, ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler
    {
        readonly Action<int, string> cb;
        public WvScriptDone(Action<int, string> cb) { this.cb = cb; }
        public int Invoke(int hr, string json) { try { cb(hr, json); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvNavStarting : ICoreWebView2NavigationStartingEventHandler
    {
        readonly Action<ICoreWebView2NavigationStartingEventArgs> cb;
        public WvNavStarting(Action<ICoreWebView2NavigationStartingEventArgs> cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, ICoreWebView2NavigationStartingEventArgs a) { try { cb(a); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvNavCompleted : ICoreWebView2NavigationCompletedEventHandler
    {
        readonly Action<ICoreWebView2NavigationCompletedEventArgs> cb;
        public WvNavCompleted(Action<ICoreWebView2NavigationCompletedEventArgs> cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, ICoreWebView2NavigationCompletedEventArgs a) { try { cb(a); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvNewWindow : ICoreWebView2NewWindowRequestedEventHandler
    {
        readonly Action<ICoreWebView2NewWindowRequestedEventArgs> cb;
        public WvNewWindow(Action<ICoreWebView2NewWindowRequestedEventArgs> cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, ICoreWebView2NewWindowRequestedEventArgs a) { try { cb(a); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvPermission : ICoreWebView2PermissionRequestedEventHandler
    {
        readonly Action<ICoreWebView2PermissionRequestedEventArgs> cb;
        public WvPermission(Action<ICoreWebView2PermissionRequestedEventArgs> cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, ICoreWebView2PermissionRequestedEventArgs a) { try { cb(a); } catch { try { a.SetState(2); } catch { } } return 0; }
    }

    [ComVisible(true)]
    class WvScriptDialog : ICoreWebView2ScriptDialogOpeningEventHandler
    {
        readonly Action<ICoreWebView2ScriptDialogOpeningEventArgs> cb;
        public WvScriptDialog(Action<ICoreWebView2ScriptDialogOpeningEventArgs> cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, ICoreWebView2ScriptDialogOpeningEventArgs a) { try { cb(a); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvDownload : ICoreWebView2DownloadStartingEventHandler
    {
        readonly Action<ICoreWebView2DownloadStartingEventArgs> cb;
        public WvDownload(Action<ICoreWebView2DownloadStartingEventArgs> cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, ICoreWebView2DownloadStartingEventArgs a) { try { cb(a); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvAccel : ICoreWebView2AcceleratorKeyPressedEventHandler
    {
        readonly Action<ICoreWebView2AcceleratorKeyPressedEventArgs> cb;
        public WvAccel(Action<ICoreWebView2AcceleratorKeyPressedEventArgs> cb) { this.cb = cb; }
        public int Invoke(ICoreWebView2Controller2 sender, ICoreWebView2AcceleratorKeyPressedEventArgs a) { try { cb(a); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvWindowClose : ICoreWebView2WindowCloseRequestedEventHandler
    {
        readonly Action cb;
        public WvWindowClose(Action cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, IntPtr args) { try { cb(); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvSimple : ICoreWebView2DocumentTitleChangedEventHandler, ICoreWebView2HistoryChangedEventHandler, ICoreWebView2ContentLoadingEventHandler
    {
        readonly Action cb;
        public WvSimple(Action cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, IntPtr args) { try { cb(); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvSourceChanged : ICoreWebView2SourceChangedEventHandler
    {
        readonly Action cb;
        public WvSourceChanged(Action cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, ICoreWebView2SourceChangedEventArgs a) { try { cb(); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvFocus : ICoreWebView2FocusChangedEventHandler
    {
        readonly Action cb;
        public WvFocus(Action cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, IntPtr args) { try { cb(); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvClearDone : ICoreWebView2ClearBrowsingDataCompletedHandler
    {
        readonly Action<int> cb;
        public WvClearDone(Action<int> cb) { this.cb = cb; }
        public int Invoke(int hr) { try { cb(hr); } catch { } return 0; }
    }

    static class BrowserData
    {
        public static string Folder { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), "Browser"); } }
        public static string ClearMarker { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), "Browser.clear"); } }
        public static string WipeMarker { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), "Browser.wipe"); } }
        public static string TemporaryMarker { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), "Browser.temp"); } }

        public static void FinishTemporary()
        {
            if (!File.Exists(TemporaryMarker)) return;
            Mark(ClearMarker);
            Unmark(TemporaryMarker);
        }

        public static void Mark(string marker)
        {
            if (Program.Erased) return;
            try { if (!File.Exists(marker)) File.WriteAllText(marker, ""); } catch { }
        }

        public static void Unmark(string marker)
        {
            try { if (File.Exists(marker)) File.Delete(marker); } catch { }
        }

        public static bool WaitUnlocked(string folder, int ms)
        {
            string lockFile = Path.Combine(Path.Combine(folder, "EBWebView"), "lockfile");
            DateTime until = DateTime.UtcNow.AddMilliseconds(ms);
            while (true)
            {
                if (!File.Exists(lockFile)) return true;
                try { using (new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } return true; }
                catch { }
                if (DateTime.UtcNow >= until) return false;
                Thread.Sleep(250);
            }
        }

        public static int Wipe(string folder, out int failed)
        {
            int erased = 0;
            failed = 0;
            EraseTree(folder, 0, ref erased, ref failed);
            return erased;
        }

        static void EraseTree(string dir, int depth, ref int erased, ref int failed)
        {
            DirectoryInfo di;
            try { di = new DirectoryInfo(dir); if (!di.Exists) return; }
            catch { failed++; return; }
            if (depth > 64) { failed++; return; }
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                try { Directory.Delete(dir, false); } catch { failed++; }
                return;
            }
            FileInfo[] files;
            DirectoryInfo[] subs;
            try { files = di.GetFiles(); subs = di.GetDirectories(); }
            catch { failed++; return; }
            foreach (FileInfo f in files)
            {
                if (SecureFile.Erase(f.FullName)) erased++;
                else failed++;
            }
            foreach (DirectoryInfo sub in subs) EraseTree(sub.FullName, depth + 1, ref erased, ref failed);
            try { Directory.Delete(dir, false); } catch { if (Directory.Exists(dir)) failed++; }
        }

        public static void FinishPendingWipe()
        {
            if (!File.Exists(WipeMarker)) return;
            int failed;
            Wipe(Folder, out failed);
            if (failed == 0) { Unmark(WipeMarker); Unmark(ClearMarker); }
        }
    }

    [ComVisible(true)]
    class WvResourceRequested : ICoreWebView2WebResourceRequestedEventHandler
    {
        readonly Action<ICoreWebView2WebResourceRequestedEventArgs> cb;
        public WvResourceRequested(Action<ICoreWebView2WebResourceRequestedEventArgs> cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, ICoreWebView2WebResourceRequestedEventArgs a) { try { cb(a); } catch { } return 0; }
    }

    [ComVisible(true)]
    class WvProcessFailed : ICoreWebView2ProcessFailedEventHandler
    {
        readonly Action<int> cb;
        public WvProcessFailed(Action<int> cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, ICoreWebView2ProcessFailedEventArgs a) { int k = 0; try { a.ProcessFailedKind(out k); cb(k); } catch { } return 0; }
    }

    static class WebView2Runtime
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetEnvironmentVariableW(string name, string value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int CreateInternal(int unknown, int runtimeType, [MarshalAs(UnmanagedType.LPWStr)] string userDataFolder, IntPtr options,
            [MarshalAs(UnmanagedType.Interface)] ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler handler);

        static string dllPath, version;
        static bool searched;
        static CreateInternal create;

        public const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        public static string Version { get { Find(); return version; } }

        public static string Find()
        {
            if (searched) return dllPath;
            searched = true;
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
                string arch = IntPtr.Size == 8 ? "x64" : "x86";
                string pa = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
                if (string.Equals(pa, "ARM64", StringComparison.OrdinalIgnoreCase)) arch = "arm64";
                foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32))
                        using (RegistryKey k = root.OpenSubKey(@"Software\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"))
                        {
                            string folder = k == null ? null : k.GetValue("EBWebView") as string;
                            if (string.IsNullOrEmpty(folder)) continue;
                            string v = Path.GetFileName(folder.TrimEnd('\\'));
                            Version parsed;
                            if (!System.Version.TryParse(v, out parsed) || parsed < new Version(86, 0, 616, 0)) continue;
                            string dll = Path.Combine(Path.Combine(Path.Combine(folder, "EBWebView"), arch), "EmbeddedBrowserWebView.dll");
                            if (!File.Exists(dll)) continue;
                            dllPath = dll;
                            version = v;
                            return dllPath;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        public const string OfflineArgs = "--proxy-server=http://127.0.0.1:9 --proxy-bypass-list=<local>;10.0.0.0/8;172.16.0.0/12;192.168.0.0/16;169.254.0.0/16;fc00::/7;fe80::/10;*.local;*.lan;*.home.arpa;*.internal;*.localhost --webrtc-ip-handling-policy=disable_non_proxied_udp --force-webrtc-ip-handling-policy";

        public static string CreateEnvironment(string userDataFolder, string browserArgs, Action<int, ICoreWebView2Environment> done)
        {
            string dll = Find();
            if (dll == null) return "missing";
            try
            {
                if (create == null)
                {
                    SetEnvironmentVariableW("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "");
                    SetEnvironmentVariableW("WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER", "");
                    SetEnvironmentVariableW("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", "");
                    SetEnvironmentVariableW("WEBVIEW2_USER_DATA_FOLDER", "");
                    SetEnvironmentVariableW("WEBVIEW2_RELEASE_CHANNEL_PREFERENCE", "0");
                    IntPtr h = LoadLibraryW(dll);
                    if (h == IntPtr.Zero) return "could not load the WebView2 runtime (error " + Marshal.GetLastWin32Error() + ")";
                    IntPtr f = GetProcAddress(h, "CreateWebViewEnvironmentWithOptionsInternal");
                    if (f == IntPtr.Zero) return "this WebView2 runtime is not supported";
                    create = (CreateInternal)Marshal.GetDelegateForFunctionPointer(f, typeof(CreateInternal));
                }
                Directory.CreateDirectory(userDataFolder);
                SetEnvironmentVariableW("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", browserArgs ?? "");
                int hr = create(1, 0, userDataFolder, IntPtr.Zero, new WvEnvDone(done));
                return hr < 0 ? "the WebView2 runtime refused to start (0x" + hr.ToString("X8") + ")" : null;
            }
            catch (Exception ex) { return "the WebView2 runtime failed to start: " + ex.Message; }
        }

        public static string Str(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUni(p) ?? ""; }
            finally { Marshal.FreeCoTaskMem(p); }
        }
    }

    class BrowserPane : Control, IBrowserBridge
    {
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(System.Drawing.Point p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        class Pending
        {
            readonly object gate = new object();
            bool done;
            public string Error, Result;
            public void Ok(string r) { lock (gate) { if (done) return; Result = r; done = true; Monitor.PulseAll(gate); } }
            public void Fail(string e) { lock (gate) { if (done) return; Error = e ?? "failed"; done = true; Monitor.PulseAll(gate); } }
            public bool Wait(int ms) { lock (gate) { if (!done) Monitor.Wait(gate, ms); return done; } }
        }

        class Download
        {
            public ICoreWebView2DownloadStartingEventArgs Args;
            public ICoreWebView2DownloadOperation Op;
            public ICoreWebView2Deferral Deferral;
            public string Name = "", Url = "";
            public long Bytes = -1;
            public bool Done;
        }

        readonly FlatButton bBack = new FlatButton("", "left", false);
        readonly FlatButton bFwd = new FlatButton("", "right", false);
        readonly FlatButton bReload = new FlatButton("", "reload", false);
        readonly FlatButton bExternal = new FlatButton("", "external", false);
        readonly FlatButton bMore = new FlatButton("", "more", false);
        readonly FlatButton bClose = new FlatButton("", "x", false);
        readonly FlatButton bNoticeOpen = new FlatButton("Open in your browser", null, false);
        readonly FlatButton bNoticeClose = new FlatButton("", "x", false);
        readonly InputBox address = new InputBox("Search or type an address");
        readonly Panel host = new Panel();
        readonly ThemedTip tip = new ThemedTip();
        Theme th = Theme.Light(Color.SteelBlue);

        ICoreWebView2Environment env;
        ICoreWebView2Controller2 ctl;
        ICoreWebView2_4 wv;
        volatile bool ready, creating;
        volatile string failure;
        bool missing, pageBase;
        readonly List<object> keep = new List<object>();

        volatile string currentUrl = "", currentTitle = "", goingUrl = "";
        volatile bool loading, lastNavOk = true;
        volatile int lastNavError;
        ulong navId;
        volatile int navStarts;
        volatile string allowedFolder;
        volatile string pageNote, lastTitle = "";
        volatile bool pageNeedsUser, lastChallenge, lastInterstitial, lastWidget;
        int agentBusy;
        bool Acting { get { return Thread.VolatileRead(ref agentBusy) > 0; } }

        volatile int worldId;
        DateTime actedUntil = DateTime.MinValue;
        DateTime lastCrashReload = DateTime.MinValue;
        int crashReloads;
        ulong resetNavId;
        bool startPending;
        readonly string worldName = "w" + Guid.NewGuid().ToString("N").Substring(0, 12);
        readonly object opLock = new object();
        readonly List<string> events = new List<string>();
        readonly List<BrowserPopup> popups = new List<BrowserPopup>();
        Download download;
        volatile bool userDialogOpen;
        volatile string userDialogText = "";
        readonly List<DateTime> dialogTimes = new List<DateTime>();
        DateTime dialogsMutedUntil = DateTime.MinValue;
        double shotScale, shotX0, shotY0;
        int shotNav = -1, shotW, shotH, viewW, viewH;

        double PageX(double x) { return shotX0 + x * shotScale; }
        double PageY(double y) { return shotY0 + y * shotScale; }
        bool shotResized;
        string notice;
        public volatile bool CaptchaMode, AgentCanSee;
        ICoreWebView2Settings mainSettings;
        int menuOff;

        public int SnapshotChars = 20000;
        public event EventHandler ShowRequested, CloseRequested;
        public event Action<string> TitleChanged;

        public BrowserPane()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = UI.Font;
            host.BackColor = Color.White;
            host.Paint += PaintHost;
            host.Resize += delegate { ResizeView(); };
            Controls.AddRange(new Control[] { bBack, bFwd, bReload, address, bExternal, bMore, bClose, bNoticeOpen, bNoticeClose, host, drawer });
            bNoticeOpen.Visible = bNoticeClose.Visible = false;
            drawer.Visible = false;
            drawer.View.Log = consoleLog;
            bBack.Enabled = bFwd.Enabled = false;
            tip.SetToolTip(bBack, "Back");
            tip.SetToolTip(bFwd, "Forward");
            tip.SetToolTip(bReload, "Reload");
            tip.SetToolTip(bExternal, "Open this page in your own browser");
            tip.SetToolTip(bMore, "Console and developer tools");
            tip.SetToolTip(bClose, "Close the browser");
            bMore.Click += delegate { ShowMoreMenu(); };
            drawer.RunRequested += UserRun;
            drawer.CloseRequested += delegate { ShowConsole(false, true); };
            drawer.ClearRequested += delegate { consoleLog.Clear(); };
            drawer.NoticeClicked += delegate { userSite = HostOf(currentUrl); UpdateCapture(); };
            drawer.HeightDragged += delegate (int h)
            {
                drawerHeight = ClampDrawer(h);
                PerformLayout();
                if (ConsoleHeightChanged != null) ConsoleHeightChanged(drawerHeight);
            };
            consoleLog.Changed += OnConsoleChanged;
            tip.SetToolTip(bNoticeClose, "Hide this message");
            bBack.Click += delegate { userTouched = true; if (wv != null) wv.GoBack(); };
            bFwd.Click += delegate { userTouched = true; if (wv != null) wv.GoForward(); };
            bReload.Click += delegate
            {
                userTouched = true;
                if (wv == null) { if (failure != null && !missing) { failure = null; CreateView(); } return; }
                if (loading) wv.Stop(); else wv.Reload();
            };
            bExternal.Click += delegate { OpenExternally(currentUrl); };
            bClose.Click += delegate { if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); };
            bNoticeOpen.Click += delegate { OpenExternally(GoogleBlocked(currentUrl) && lastSiteUrl.Length > 0 ? lastSiteUrl : currentUrl); };
            bNoticeClose.Click += delegate { ShowNotice(null); };
            address.Box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; UserNavigate(address.Box.Text); }
                else if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; address.Box.Text = currentUrl; if (wv != null) host.Focus(); }
            };
            address.Box.GotFocus += delegate { userTouched = true; BeginInvoke(new Action(delegate { address.Box.SelectAll(); })); };
            host.GotFocus += delegate { if (ctl != null) { try { ctl.MoveFocus(0); } catch { } } };
        }

        public bool Available { get { return !missing && WebView2Runtime.Find() != null; } }
        public bool ViewReady { get { return ready; } }
        public string CurrentUrl { get { return currentUrl; } }
        public string PageNote { get { return pageNote; } }
        public bool PageNeedsUser { get { return pageNeedsUser; } }
        public string PageTitle { get { return currentTitle; } }

        public void SetTheme(Theme t)
        {
            th = t;
            tip.Th = t;
            BackColor = t.Chrome;
            address.Field = true;
            address.Pill = true;
            address.SetTheme(t);
            foreach (Control c in Controls) { FlatButton fb = c as FlatButton; if (fb != null) fb.SetTheme(t); }
            drawer.SetTheme(t);
            host.BackColor = t.Back;
            ApplyBase();
            Invalidate(true);
        }

        void ApplyBase()
        {
            if (ctl == null) return;
            Color c = pageBase ? Color.White : th.Back;
            try { WvColor col = new WvColor(); col.A = 255; col.R = c.R; col.G = c.G; col.B = c.B; ctl.SetDefaultBackgroundColor(col); } catch { }
        }

        int ToolbarH { get { return UI.S(38); } }
        int NoticeH { get { return notice == null ? 0 : UI.S(34); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = UI.S(6), bh = UI.S(28), bw = UI.S(30), y = UI.S(7);
            int x = pad;
            bBack.SetBounds(x, y, bw, bh); x += bw + UI.S(1);
            bFwd.SetBounds(x, y, bw, bh); x += bw + UI.S(1);
            bReload.SetBounds(x, y, bw, bh); x += bw + UI.S(6);
            int right = Width - pad;
            bClose.SetBounds(right - bw, y, bw, bh); right -= bw + UI.S(2);
            bMore.SetBounds(right - bw, y, bw, bh); right -= bw + UI.S(2);
            bExternal.SetBounds(right - bw, y, bw, bh); right -= bw + UI.S(6);
            address.SetBounds(x, y, Math.Max(UI.S(60), right - x), bh);
            int top = ToolbarH;
            if (notice != null)
            {
                int nh = NoticeH, ny = top + (nh - UI.S(26)) / 2;
                bNoticeClose.SetBounds(Width - pad - UI.S(26), ny, UI.S(26), UI.S(26));
                int ow = TextRenderer.MeasureText(bNoticeOpen.Text, UI.Font).Width + UI.S(20);
                bNoticeOpen.SetBounds(bNoticeClose.Left - UI.S(6) - ow, ny, ow, UI.S(26));
                top += nh;
            }
            int bottom = Height;
            if (drawerOpen)
            {
                int dh = ClampDrawer(drawerHeight > 0 ? drawerHeight : Height * 32 / 100);
                drawer.SetBounds(0, Height - dh, Width, dh);
                bottom -= dh;
            }
            host.SetBounds(0, top, Width, Math.Max(0, bottom - top));
        }

        int ClampDrawer(int h)
        {
            int min = UI.S(120), max = Math.Max(min, Height - ToolbarH - UI.S(140));
            return Math.Max(min, Math.Min(max, h));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(th.Chrome)) g.FillRectangle(b, ClientRectangle);
            int top = ToolbarH;
            using (Pen p = new Pen(th.Border)) g.DrawLine(p, 0, top - 1, Width, top - 1);
            if (notice != null)
            {
                Rectangle nr = new Rectangle(0, top, Width, NoticeH);
                using (SolidBrush b = new SolidBrush(th.HighContrast ? th.Chrome : Theme.Blend(th.Chrome, th.Accent, th.Dark ? 0.16 : 0.08))) g.FillRectangle(b, nr);
                using (Pen p = new Pen(th.Border)) g.DrawLine(p, 0, nr.Bottom - 1, Width, nr.Bottom - 1);
                Rectangle tr = new Rectangle(UI.S(12), nr.Top, Math.Max(10, bNoticeOpen.Left - UI.S(20)), nr.Height);
                TextRenderer.DrawText(g, notice, UI.Font, tr, th.ChromeFore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
            if (Acting)
                using (SolidBrush b = new SolidBrush(th.Accent)) g.FillRectangle(b, 0, top - Math.Max(2, UI.S(2)), Width, Math.Max(2, UI.S(2)));
        }

        void ShowNotice(string text)
        {
            if (notice == text) return;
            notice = text;
            bNoticeOpen.Visible = bNoticeClose.Visible = text != null;
            if (text != null) tip.SetToolTip(bNoticeOpen, "Opens " + HostOf(currentUrl) + " in your default browser");
            PerformLayout();
            Invalidate();
        }

        void PaintHost(object sender, PaintEventArgs e)
        {
            if (ready && failure == null) return;
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(th.Back)) g.FillRectangle(b, host.ClientRectangle);
            string msg;
            if (missing || !Available)
                msg = "The built-in browser uses the Microsoft Edge WebView2 Runtime, which is part of Windows 11 and up-to-date Windows 10.\n\nIt was not found on this PC. Install it from Microsoft (free), then reopen the browser:\n" + WebView2Runtime.DownloadUrl;
            else if (failure != null) msg = "The browser could not start: " + failure + "\n\nPress reload to try again.";
            else msg = wiping ? "Wiping the browser's data..." : creating ? "Starting the browser..." : "";
            Rectangle r = new Rectangle(UI.S(24), UI.S(24), Math.Max(10, host.Width - UI.S(48)), Math.Max(10, host.Height - UI.S(48)));
            TextRenderer.DrawText(g, msg, UI.Font, r, th.ChromeDim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { agentOpened = false; userTouched = false; }
            if (ctl != null) { try { ctl.SetIsVisible(Visible ? 1 : 0); } catch { } }
            if (Visible && !ready && !creating && failure == null && IsHandleCreated) CreateView();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (Visible && !ready && !creating) BeginInvoke(new Action(CreateView));
        }

        public void ParentMoved()
        {
            if (ctl != null) { try { ctl.NotifyParentWindowPositionChanged(); } catch { } }
        }

        void ResizeView()
        {
            if (ctl == null) return;
            try
            {
                WvRect r = new WvRect();
                r.Left = 0; r.Top = 0; r.Right = host.ClientSize.Width; r.Bottom = host.ClientSize.Height;
                if (r.Right != viewW || r.Bottom != viewH) { viewW = r.Right; viewH = r.Bottom; shotResized = true; }
                ctl.SetBounds(r);
            }
            catch { }
        }

        void CreateView()
        {
            if (ready || creating || IsDisposed || wiping) return;
            if (WebView2Runtime.Find() == null) { missing = true; host.Invalidate(); return; }
            creating = true;
            failure = null;
            host.Invalidate();
            string folder = Path.Combine(Program.DataDir ?? Path.GetTempPath(), "Browser");
            IntPtr hwnd = host.Handle;
            envOffline = Net.Offline;
            string err = WebView2Runtime.CreateEnvironment(folder, envOffline ? WebView2Runtime.OfflineArgs : "", delegate (int hr, ICoreWebView2Environment e)
            {
                if (hr < 0 || e == null) { Fail("the browser engine did not start (0x" + hr.ToString("X8") + ")"); return; }
                env = e;
                WvControllerDone cd = new WvControllerDone(delegate (int hr2, ICoreWebView2Controller2 c)
                {
                    if (hr2 < 0 || c == null) { Fail("the browser view could not be created (0x" + hr2.ToString("X8") + ")"); return; }
                    Attach(c);
                });
                keep.Add(cd);
                int r2 = env.CreateCoreWebView2Controller(hwnd, cd);
                if (r2 < 0) Fail("the browser view could not be created (0x" + r2.ToString("X8") + ")");
            });
            if (err == "missing") { creating = false; missing = true; host.Invalidate(); }
            else if (err != null) Fail(err);
        }

        void Fail(string why)
        {
            creating = false;
            ready = false;
            failure = why;
            host.Invalidate();
        }

        void Configure(ICoreWebView2_4 view, BrowserPopup popup)
        {
            List<object> hold = popup != null ? popup.Keep : keep;
            long tok;
            WvScriptDialog sd = new WvScriptDialog(delegate (ICoreWebView2ScriptDialogOpeningEventArgs a) { OnDialog(a, popup); });
            WvPermission pr = new WvPermission(OnPermission);
            WvDownload dl = new WvDownload(OnDownload);
            WvNewWindow nw = new WvNewWindow(delegate (ICoreWebView2NewWindowRequestedEventArgs a) { OnNewWindow(a, popup); });
            WvNavStarting fn = new WvNavStarting(delegate (ICoreWebView2NavigationStartingEventArgs a)
            {
                IntPtr fp;
                string fu = a.Uri(out fp) >= 0 ? WebView2Runtime.Str(fp) : "";
                if (!FrameAllowed(fu)) a.SetCancel(1);
            });
            WvResourceRequested rr = new WvResourceRequested(OnResource);
            hold.AddRange(new object[] { sd, pr, dl, nw, fn, rr });
            try { view.add_FrameNavigationStarting(fn, out tok); } catch { }
            try
            {
                IntPtr rp = Marshal.GetComInterfaceForObject(rr, typeof(ICoreWebView2WebResourceRequestedEventHandler));
                try { view.add_WebResourceRequested(rp, out tok); } finally { Marshal.Release(rp); }
                if (Net.Offline && view.AddWebResourceRequestedFilter("*", 0) >= 0) offlineViews.Add(view);
            }
            catch { }
            bool dialogsHooked = view.add_ScriptDialogOpening(sd, out tok) >= 0;
            view.add_PermissionRequested(pr, out tok);
            view.add_DownloadStarting(dl, out tok);
            view.add_NewWindowRequested(nw, out tok);
            ICoreWebView2Settings s;
            if (view.Settings(out s) >= 0 && s != null)
            {
                s.SetAreHostObjectsAllowed(0);
                s.SetIsWebMessageEnabled(0);
                if (dialogsHooked) s.SetAreDefaultScriptDialogsEnabled(0);
                s.SetIsStatusBarEnabled(1);
                s.SetAreDefaultContextMenusEnabled(1);
                s.SetIsZoomControlEnabled(1);
                s.SetIsBuiltInErrorPageEnabled(1);
                if (popup == null) mainSettings = s;
            }
            if (popup == null) return;
            WvNavStarting ns = new WvNavStarting(delegate (ICoreWebView2NavigationStartingEventArgs a)
            {
                IntPtr p;
                a.Uri(out p);
                string uri = WebView2Runtime.Str(p);
                string low = uri.ToLowerInvariant();
                if (!(low.StartsWith("http://") || low.StartsWith("https://") || low == "about:blank")) { a.SetCancel(1); return; }
                if (Net.Offline && !Net.IsLocal(uri)) { a.SetCancel(1); return; }
                Net.Record(uri, "Built-in browser");
                popup.SetAddress(uri);
            });
            WvSourceChanged sc = new WvSourceChanged(delegate
            {
                IntPtr p;
                if (popup.View != null && popup.View.Source(out p) >= 0) popup.SetAddress(WebView2Runtime.Str(p));
            });
            WvSimple tc = new WvSimple(delegate
            {
                IntPtr p;
                if (popup.View != null && popup.View.DocumentTitle(out p) >= 0) popup.SetTitle(WebView2Runtime.Str(p));
            });
            WvWindowClose wc = new WvWindowClose(delegate { BeginInvoke(new Action(delegate { if (!popup.IsDisposed) popup.Close(); })); });
            WvProcessFailed pf = new WvProcessFailed(delegate (int k) { if (k == 0) BeginInvoke(new Action(delegate { if (!popup.IsDisposed) popup.Close(); })); });
            hold.AddRange(new object[] { ns, sc, tc, wc, pf });
            view.add_NavigationStarting(ns, out tok);
            view.add_SourceChanged(sc, out tok);
            view.add_DocumentTitleChanged(tc, out tok);
            view.add_WindowCloseRequested(wc, out tok);
            view.add_ProcessFailed(pf, out tok);
        }

        void Attach(ICoreWebView2Controller2 c)
        {
            ctl = c;
            ICoreWebView2_4 view;
            if (c.CoreWebView2(out view) < 0 || view == null) { Fail("the browser view is not available"); return; }
            wv = view;
            Configure(wv, null);
            HookConsole();
            HookCast();
            long tok;
            WvNavStarting ns = new WvNavStarting(OnNavStarting);
            WvNavCompleted nc = new WvNavCompleted(OnNavCompleted);
            WvSourceChanged sc = new WvSourceChanged(OnSourceChanged);
            WvSimple hc = new WvSimple(UpdateButtons);
            WvSimple tc = new WvSimple(OnTitle);
            WvSimple cl = new WvSimple(OnContentLoading);
            WvProcessFailed pf = new WvProcessFailed(OnProcessFailed);
            WvAccel ak = new WvAccel(OnAccelerator);
            WvFocus gf = new WvFocus(delegate { userTouched = true; });
            keep.AddRange(new object[] { ns, nc, sc, hc, tc, cl, pf, ak, gf });
            try { ctl.add_AcceleratorKeyPressed(ak, out tok); } catch { }
            try { ctl.add_GotFocus(gf, out tok); } catch { }
            wv.add_NavigationStarting(ns, out tok);
            wv.add_NavigationCompleted(nc, out tok);
            wv.add_SourceChanged(sc, out tok);
            wv.add_HistoryChanged(hc, out tok);
            try { commitEvents = wv.add_ContentLoading(cl, out tok) >= 0; } catch { commitEvents = false; }
            wv.add_DocumentTitleChanged(tc, out tok);
            wv.add_ProcessFailed(pf, out tok);
            ApplyBase();
            ResizeView();
            ctl.SetIsVisible(Visible ? 1 : 0);
            creating = false;
            ready = true;
            host.Invalidate();
            if (File.Exists(BrowserData.ClearMarker)) ClearHistory();
            if (pendingUrl != null) { wv.Navigate(pendingUrl); pendingUrl = null; }
            else if (currentUrl.Length == 0) { startPending = true; wv.NavigateToString(StartPage()); }
            UpdateButtons();
        }

        public Func<Keys, bool> AppShortcut;

        void OnAccelerator(ICoreWebView2AcceleratorKeyPressedEventArgs a)
        {
            int kind;
            if (a.KeyEventKind(out kind) < 0 || (kind != 0 && kind != 2)) return;
            uint vk;
            if (a.VirtualKey(out vk) < 0) return;
            Keys key = (Keys)vk | Control.ModifierKeys;
            if (key == (Keys.Control | Keys.L) || key == Keys.F6 || key == (Keys.Alt | Keys.D))
            {
                a.SetHandled(1);
                BeginInvoke(new Action(delegate { address.Box.Focus(); address.Box.SelectAll(); }));
                return;
            }
            if (key == (Keys.Control | Keys.Shift | Keys.J))
            {
                a.SetHandled(1);
                BeginInvoke(new Action(delegate { ShowConsole(!drawerOpen, true); }));
                return;
            }
            Func<Keys, bool> f = AppShortcut;
            if (f != null && f(key)) a.SetHandled(1);
        }

        string StartPage()
        {
            Func<Color, string> hex = delegate (Color c) { return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); };
            return "<!doctype html><html><head><meta charset='utf-8'><title>New tab</title><style>html,body{margin:0;height:100%;background:" + hex(th.Back) +
                   ";color:" + hex(th.ChromeDim) + ";font:14px 'Segoe UI',system-ui,sans-serif}main{padding:40px 32px;max-width:520px}" +
                   "p{line-height:1.5;margin:0 0 10px}</style></head><body><main><p>Type an address above, or ask the agent to look something up.</p>" +
                   "<p>This browser keeps its own sign-ins and history, separate from your other browsers.</p></main></body></html>";
        }

        public static string HostOf(string url)
        {
            Uri u;
            return Uri.TryCreate(url ?? "", UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https") ? u.Host : "";
        }

        void OnNavStarting(ICoreWebView2NavigationStartingEventArgs a)
        {
            IntPtr p;
            a.Uri(out p);
            string uri = WebView2Runtime.Str(p);
            if (!NavigationAllowed(uri) || (Net.Offline && !Net.IsLocal(uri)))
            {
                a.SetCancel(1);
                if (Net.Offline && !Net.IsLocal(uri)) ShowNotice("Offline mode is on (Settings > Privacy), so MinGent doesn't open web pages. Pages on this PC still work.");
                return;
            }
            Net.Record(uri, "Built-in browser");
            if (!pageBase && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) { pageBase = true; ApplyBase(); }
            int redirected = 0;
            try { a.IsRedirected(out redirected); } catch { }
            if (!commitEvents && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && redirected == 0) consoleLog.NewPage(uri);
            goingUrl = uri;
            SetCapture(WantConsole(uri));
            ulong id;
            a.NavigationId(out id);
            navId = id;
            if (resetHistory && resetNavId == 0 && uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) resetNavId = id;
            loading = true;
            worldId = 0;
            navStarts++;
            ShowNotice(null);
            UpdateButtons();
        }

        bool FrameAllowed(string uri)
        {
            string low = (uri ?? "").ToLowerInvariant();
            if (Net.Offline && (low.StartsWith("http://") || low.StartsWith("https://")) && !Net.IsLocal(uri)) return false;
            if (low.Length == 0 || low.StartsWith("http://") || low.StartsWith("https://") || low.StartsWith("about:") || low.StartsWith("data:") || low.StartsWith("blob:")) return true;
            if (low.StartsWith("file:")) return LocalAllowed(uri);
            return false;
        }

        bool LocalAllowed(string uri)
        {
            string folder = allowedFolder;
            if (folder == null) return false;
            try
            {
                string bare, suffix;
                LocalUrl.Split(uri, out bare, out suffix);
                string local = new Uri(bare).LocalPath;
                return AgentPaths.IsInside(local, folder) && AgentPaths.SensitiveReal(local) == null;
            }
            catch { return false; }
        }

        bool NavigationAllowed(string uri)
        {
            string low = (uri ?? "").ToLowerInvariant();
            if (low.StartsWith("http://") || low.StartsWith("https://") || low == "about:blank") return true;
            if (low.StartsWith("data:text/html") && (startPending || currentUrl.Length == 0)) { startPending = false; return true; }
            if (low.StartsWith("file:")) return LocalAllowed(uri);
            return false;
        }

        void OnNavCompleted(ICoreWebView2NavigationCompletedEventArgs a)
        {
            ulong id;
            a.NavigationId(out id);
            int ok, status;
            a.IsSuccess(out ok);
            a.WebErrorStatus(out status);
            if (id == navId || navId == 0)
            {
                loading = false;
                lastNavOk = ok != 0;
                lastNavError = status;
                goingUrl = "";
                UpdateCapture();
            }
            if (resetHistory && resetNavId != 0 && id == resetNavId)
            {
                resetHistory = false;
                resetNavId = 0;
                if (ok != 0) Fire("Page.resetNavigationHistory", "{}");
            }
            CheckNotice();
            UpdateButtons();
        }

        static bool GoogleBlocked(string url)
        {
            Uri u;
            return Uri.TryCreate(url ?? "", UriKind.Absolute, out u) && u.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase) &&
                   u.AbsolutePath.IndexOf("/rejected", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void CheckNotice()
        {
            if (GoogleBlocked(currentUrl)) ShowNotice("Google doesn't allow signing in from apps' built-in browsers, including this one.");
        }

        void OnSourceChanged()
        {
            IntPtr p;
            if (wv.Source(out p) >= 0)
            {
                string s = WebView2Runtime.Str(p);
                currentUrl = s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? "" : s;
                if (!address.Box.Focused) address.Box.Text = currentUrl;
                Uri su;
                if (Uri.TryCreate(currentUrl, UriKind.Absolute, out su) && (su.Scheme == "http" || su.Scheme == "https") &&
                    !su.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase)) lastSiteUrl = currentUrl;
            }
            CheckNotice();
            UpdateButtons();
            UpdateDrawerNotice();
        }

        void OnContentLoading()
        {
            IntPtr p;
            string s = wv != null && wv.Source(out p) >= 0 ? WebView2Runtime.Str(p) : currentUrl;
            if (!s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) consoleLog.NewPage(s);
            SetCapture(WantConsole(s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? "" : s));
        }

        void OnTitle()
        {
            IntPtr p;
            if (wv.DocumentTitle(out p) >= 0) currentTitle = WebView2Runtime.Str(p);
            if (TitleChanged != null) TitleChanged(currentTitle);
        }

        void OnNewWindow(ICoreWebView2NewWindowRequestedEventArgs a, BrowserPopup from)
        {
            IntPtr p;
            string uri = a.Uri(out p) >= 0 ? WebView2Runtime.Str(p) : "";
            int user = 0;
            try { a.IsUserInitiated(out user); } catch { }
            ICoreWebView2WindowFeatures wf = null;
            bool features = false;
            try
            {
                if (a.WindowFeatures(out wf) >= 0 && wf != null)
                {
                    int hp = 0, hs = 0;
                    wf.HasPosition(out hp);
                    wf.HasSize(out hs);
                    features = hp != 0 || hs != 0;
                }
            }
            catch { wf = null; }
            string low = uri.ToLowerInvariant();
            bool web = low.StartsWith("http://") || low.StartsWith("https://");
            bool blank = uri.Length == 0 || low == "about:blank";
            if ((!web && !blank) || user == 0)
            {
                a.SetHandled(1);
                if (Acting) AddEvent("The page tried to open a pop-up window without a click, which was blocked.");
                return;
            }
            if ((features || blank) && popups.Count < 4 && env != null && OpenPopup(a, wf, uri)) return;
            a.SetHandled(1);
            if (!web) return;
            ICoreWebView2_4 target = from != null && from.View != null ? from.View : wv;
            if (target == null) return;
            WvScriptDone go = new WvScriptDone(delegate (int hr, string json) { });
            List<object> hold = from != null ? from.Keep : keep;
            hold.Add(go);
            if (hold.Count > 400) hold.RemoveRange(0, 100);
            try { target.ExecuteScript("location.href = " + Json.Quote(uri) + ";", go); } catch { }
        }

        bool OpenPopup(ICoreWebView2NewWindowRequestedEventArgs a, ICoreWebView2WindowFeatures wf, string uri)
        {
            ICoreWebView2Deferral d = null;
            try { if (a.GetDeferral(out d) < 0) d = null; } catch { d = null; }
            if (d == null) return false;
            Form owner = FindForm();
            Size client;
            Rectangle bounds = PopupBounds(wf, owner, out client);
            BrowserPopup pop = new BrowserPopup(th, bounds, client);
            popups.Add(pop);
            pop.FormClosed += delegate { popups.Remove(pop); };
            if (owner != null) pop.Show(owner); else pop.Show();
            bool completed = false;
            Action finish = delegate { if (!completed) { completed = true; try { d.Complete(); } catch { } } };
            WvControllerDone cd = new WvControllerDone(delegate (int hr, ICoreWebView2Controller2 c)
            {
                try
                {
                    ICoreWebView2_4 v = null;
                    if (hr < 0 || c == null || c.CoreWebView2(out v) < 0 || v == null || pop.IsDisposed)
                    {
                        a.SetHandled(1);
                        if (!pop.IsDisposed) pop.Close();
                        return;
                    }
                    pop.Attach(c, v);
                    Configure(v, pop);
                    a.SetNewWindow(v);
                }
                catch
                {
                    try { a.SetHandled(1); } catch { }
                    try { if (!pop.IsDisposed) pop.Close(); } catch { }
                }
                finally { finish(); }
            });
            pop.Keep.Add(cd);
            int r = -1;
            try { r = env.CreateCoreWebView2Controller(pop.Host.Handle, cd); } catch { r = -1; }
            if (r < 0)
            {
                try { a.SetHandled(1); } catch { }
                finish();
                pop.Close();
                return true;
            }
            if (Acting)
            {
                string h = HostOf(uri);
                AddEvent("A pop-up window opened" + (h.Length > 0 ? " (" + h + ")" : "") + ". Pop-ups are for the user, for example to sign in; the assistant cannot use them. If the task needs it, ask the user to finish there.");
            }
            return true;
        }

        Rectangle PopupBounds(ICoreWebView2WindowFeatures wf, Form owner, out Size client)
        {
            int w = 520, h = 640;
            int x = int.MinValue, y = int.MinValue;
            try
            {
                if (wf != null)
                {
                    int hs = 0, hp = 0;
                    wf.HasSize(out hs);
                    wf.HasPosition(out hp);
                    if (hs != 0)
                    {
                        uint fw, fh;
                        wf.Width(out fw);
                        wf.Height(out fh);
                        if (fw > 0) w = (int)Math.Min(fw, 4000u);
                        if (fh > 0) h = (int)Math.Min(fh, 4000u);
                    }
                    if (hp != 0)
                    {
                        uint fl, ft;
                        wf.Left(out fl);
                        wf.Top(out ft);
                        x = (int)(Math.Min(fl, 20000u) * UI.Scale);
                        y = (int)(Math.Min(ft, 20000u) * UI.Scale);
                    }
                }
            }
            catch { }
            Rectangle area = owner != null ? Screen.FromControl(owner).WorkingArea : Screen.PrimaryScreen.WorkingArea;
            int cw = Math.Max(UI.S(320), Math.Min((int)(w * UI.Scale), area.Width - UI.S(40)));
            int ch = Math.Max(UI.S(300), Math.Min((int)(h * UI.Scale), area.Height - UI.S(80)));
            client = new Size(cw, ch);
            int ow = cw + UI.S(16), oh = ch + UI.S(70);
            if (x == int.MinValue)
            {
                Rectangle ob = owner != null ? owner.Bounds : area;
                x = ob.Left + (ob.Width - ow) / 2;
                y = ob.Top + (ob.Height - oh) / 2;
            }
            x = Math.Max(area.Left, Math.Min(x, area.Right - ow));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - oh));
            return new Rectangle(x, y, ow, oh);
        }

        static readonly Regex Destructive = new Regex(@"\b(delete|remove|erase|destroy|discard|permanent(ly)?|cannot be undone|can't be undone|pay|payment|purchase|buy|charge|transfer|send money|withdraw|unsubscribe|cancel (your |my |the )?(subscription|membership|account|plan|order)|close (your |my )?account|deactivate|reset)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static string Clip(string s, int n) { s = (s ?? "").Replace("\r", " ").Replace("\n", " "); return s.Length > n ? s.Substring(0, n) + "..." : s; }

        void OnDialog(ICoreWebView2ScriptDialogOpeningEventArgs a, BrowserPopup popup)
        {
            int kind = 0;
            a.Kind(out kind);
            IntPtr p;
            string msg = a.Message(out p) >= 0 ? WebView2Runtime.Str(p) : "";
            string uri = a.Uri(out p) >= 0 ? WebView2Runtime.Str(p) : "";
            string def = "";
            if (kind == 2 && a.DefaultText(out p) >= 0) def = WebView2Runtime.Str(p);
            string origin = HostOf(uri);
            if (Acting)
            {
                if (kind != 1 || !Destructive.IsMatch(msg))
                {
                    if (kind == 2) a.SetResultText(def);
                    a.Accept();
                    if (kind == 0) AddEvent("The page showed a message: \"" + Clip(msg, 300) + "\" (closed automatically).");
                    else if (kind == 1) AddEvent("The page asked \"" + Clip(msg, 300) + "\" and MinGent answered OK.");
                    else if (kind == 2) AddEvent("The page asked for text: \"" + Clip(msg, 300) + "\"; MinGent left it at " + (def.Length > 0 ? "\"" + Clip(def, 80) + "\"" : "empty") + ".");
                    else AddEvent("The page warned that unsaved changes may be lost; MinGent left the page.");
                    return;
                }
                AddEvent("The page asked \"" + Clip(msg, 300) + "\". That looks like it deletes, cancels or pays for something, so the question is shown to the user to answer.");
            }
            else
            {
                DateTime now = DateTime.UtcNow;
                if (now < dialogsMutedUntil) return;
                dialogTimes.Add(now);
                dialogTimes.RemoveAll(delegate (DateTime t) { return (now - t).TotalSeconds > 10; });
                if (dialogTimes.Count > 4) { dialogsMutedUntil = now.AddSeconds(30); dialogTimes.Clear(); return; }
            }
            ICoreWebView2Deferral d = null;
            if (a.GetDeferral(out d) < 0 || d == null) return;
            userDialogText = Clip(msg, 300);
            userDialogOpen = true;
            BeginInvoke(new Action(delegate
            {
                try
                {
                    using (PageDialog dlg = new PageDialog(th, kind, origin, msg, def))
                    {
                        bool inPopup = popup != null && !popup.IsDisposed;
                        IWin32Window owner = inPopup ? (IWin32Window)popup : FindForm();
                        Control over = inPopup ? (Control)popup : this;
                        if (over.IsHandleCreated && over.Visible)
                        {
                            Rectangle area = over.RectangleToScreen(over.ClientRectangle);
                            Rectangle wa = Screen.FromRectangle(area).WorkingArea;
                            int x = area.Left + (area.Width - dlg.Width) / 2;
                            int y = area.Top + (inPopup ? UI.S(24) : ToolbarH + UI.S(16));
                            dlg.StartPosition = FormStartPosition.Manual;
                            dlg.Location = new Point(Math.Max(wa.Left, Math.Min(x, wa.Right - dlg.Width)), Math.Max(wa.Top, Math.Min(y, wa.Bottom - dlg.Height)));
                        }
                        if (dlg.ShowDialog(owner) == DialogResult.OK)
                        {
                            if (kind == 2) a.SetResultText(dlg.Value ?? "");
                            a.Accept();
                        }
                    }
                }
                catch { }
                finally
                {
                    userDialogOpen = false;
                    try { d.Complete(); } catch { }
                }
            }));
        }

        static string PermissionName(int kind)
        {
            switch (kind)
            {
                case 1: return "the microphone";
                case 2: return "the camera";
                case 3: return "your location";
                case 4: return "notifications";
                case 5: return "device sensors";
                case 6: return "the clipboard";
                case 7: return "multiple downloads";
                case 8: return "files on this PC";
                case 9: return "autoplay";
                case 10: return "installed fonts";
                case 11: return "MIDI devices";
                case 12: return "window placement";
                default: return "a browser feature";
            }
        }

        bool RecentlyActing { get { return Acting || DateTime.UtcNow < actedUntil; } }

        void OnPermission(ICoreWebView2PermissionRequestedEventArgs a)
        {
            if (!Acting) return;
            int kind = 0;
            try { a.PermissionKind(out kind); } catch { }
            try
            {
                ICoreWebView2PermissionRequestedEventArgs3 a3 = a as ICoreWebView2PermissionRequestedEventArgs3;
                if (a3 != null) a3.SetSavesInProfile(0);
            }
            catch { }
            a.SetState(2);
            AddEvent("The page asked for access to " + PermissionName(kind) + "; MinGent said no because the assistant was in control.");
        }

        void OnDownload(ICoreWebView2DownloadStartingEventArgs a)
        {
            if (!RecentlyActing) return;
            Download dn = new Download();
            dn.Args = a;
            IntPtr p;
            try
            {
                ICoreWebView2DownloadOperation op;
                if (a.DownloadOperation(out op) >= 0 && op != null)
                {
                    dn.Op = op;
                    if (op.Uri(out p) >= 0) dn.Url = WebView2Runtime.Str(p);
                    long total;
                    if (op.TotalBytesToReceive(out total) >= 0) dn.Bytes = total;
                }
            }
            catch { }
            if (a.ResultFilePath(out p) >= 0) dn.Name = Path.GetFileName(WebView2Runtime.Str(p));
            if (string.IsNullOrEmpty(dn.Name)) dn.Name = "download";
            ICoreWebView2Deferral d = null;
            try { if (a.GetDeferral(out d) < 0) d = null; } catch { d = null; }
            if (d == null)
            {
                a.SetCancel(1);
                AddEvent("The page tried to download " + dn.Name + ", which was blocked.");
                return;
            }
            dn.Deferral = d;
            try { a.SetHandled(1); } catch { }
            Download old;
            lock (events) { old = download; download = dn; }
            if (old != null) CancelDownload(old);
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 180000;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                bool stale;
                lock (events) { stale = download == dn; if (stale) download = null; }
                if (stale) CancelDownload(dn);
            };
            timer.Start();
        }

        static void CancelDownload(Download dn)
        {
            if (dn == null || dn.Done) return;
            dn.Done = true;
            try { dn.Args.SetCancel(1); } catch { }
            try { dn.Deferral.Complete(); } catch { }
        }

        void AddEvent(string text)
        {
            lock (events) { if (events.Count < 20) events.Add(text); }
        }

        string TakeEvents()
        {
            lock (events)
            {
                if (events.Count == 0) return "";
                string s = " " + string.Join(" ", events.ToArray());
                events.Clear();
                return s;
            }
        }

        void ArmEvents(CancelToken ct)
        {
            pageNote = null;
            pageNeedsUser = false;
            Download stale;
            lock (events) { events.Clear(); stale = download; download = null; }
            if (stale != null)
            {
                string ignored;
                Ui(delegate (Pending p) { CancelDownload(stale); p.Ok(null); }, 5000, ct, out ignored);
            }
        }

        bool HasDownload { get { lock (events) return download != null; } }

        public bool PendingDownload(out string name, out string url, out long bytes)
        {
            name = url = null;
            bytes = -1;
            lock (events)
            {
                if (download == null) return false;
                name = download.Name;
                url = download.Url;
                bytes = download.Bytes;
                return true;
            }
        }

        static string SafeFileName(string name)
        {
            string n = Path.GetFileName(name ?? "") ?? "";
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            n = n.Trim().TrimEnd('.');
            if (n.Length == 0) n = "download";
            if (n.Length > 120) n = n.Substring(n.Length - 120);
            return n;
        }

        public string ResolveDownload(bool allow, string folder, CancelToken ct, out string savedTo)
        {
            savedTo = null;
            Download dn;
            lock (events) { dn = download; download = null; }
            if (dn == null) return "no download is waiting";
            string target = null;
            if (allow)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    string name = SafeFileName(dn.Name);
                    target = Path.Combine(folder, name);
                    string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
                    for (int i = 2; File.Exists(target) && i < 1000; i++) target = Path.Combine(folder, stem + " (" + i + ")" + ext);
                }
                catch (Exception ex) { allow = false; savedTo = null; target = null; AddEvent("The download could not be saved: " + ex.Message); }
            }
            bool ok = allow;
            string t = target, ignored;
            string err = Ui(delegate (Pending p)
            {
                try
                {
                    if (dn.Done) { p.Fail("the download was already cancelled"); return; }
                    dn.Done = true;
                    if (ok) dn.Args.SetResultFilePath(t);
                    else dn.Args.SetCancel(1);
                    p.Ok(null);
                }
                catch (Exception ex) { p.Fail(ex.Message); }
                finally { try { dn.Deferral.Complete(); } catch { } }
            }, 8000, ct, out ignored);
            if (err != null || !ok) return err;
            DateTime until = DateTime.UtcNow.AddSeconds(90);
            while (true)
            {
                int state = 0;
                long got = 0;
                string st;
                Ui(delegate (Pending p)
                {
                    try { if (dn.Op != null) { dn.Op.State(out state); dn.Op.BytesReceived(out got); } else state = 2; }
                    catch { state = 2; }
                    p.Ok(null);
                }, 5000, ct, out st);
                if (state == 2) { savedTo = target; return null; }
                if (state == 1) return "the download of " + dn.Name + " was interrupted";
                if (ct.IsCancelled) return "stopped by the user";
                if (DateTime.UtcNow > until) { savedTo = target + " (still downloading, " + FormatBytes(got) + " so far)"; return null; }
                ct.Sleep(400);
            }
        }

        public static string FormatBytes(long b)
        {
            if (b < 0) return "unknown size";
            if (b < 1024) return b + " bytes";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            return (b / 1073741824.0).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
        }

        void OnProcessFailed(int kind)
        {
            consoleOn = false;
            if (kind == 0)
            {
                try { if (ctl != null) ctl.Close(); } catch { }
                ctl = null;
                wv = null;
                Fail("the browser process stopped");
                UpdateButtons();
            }
            else if (kind == 1 && wv != null)
            {
                if ((DateTime.UtcNow - lastCrashReload).TotalSeconds > 60) crashReloads = 0;
                lastCrashReload = DateTime.UtcNow;
                if (++crashReloads <= 2) { try { wv.Reload(); } catch { } }
                else ShowNotice("This page keeps crashing, so MinGent stopped reloading it.");
            }
        }

        void UpdateButtons()
        {
            if (wv == null)
            {
                bBack.Enabled = bFwd.Enabled = false;
                bBack.Invalidate(); bFwd.Invalidate();
                return;
            }
            int b = 0, f = 0;
            try { wv.CanGoBack(out b); wv.CanGoForward(out f); } catch { }
            bBack.Enabled = b != 0;
            bFwd.Enabled = f != 0;
            bReload.Glyph = loading ? "x" : "reload";
            tip.SetToolTip(bReload, loading ? "Stop" : "Reload");
            bBack.Invalidate(); bFwd.Invalidate(); bReload.Invalidate();
        }

        void UserNavigate(string text)
        {
            string t = (text ?? "").Trim().Trim('"');
            if (t.Length == 0) return;
            string url = LocalFileUrl(t) ?? NormalizeUserUrl(t);
            if (url == null) return;
            if (wv == null) { pendingUrl = url; CreateView(); return; }
            wv.Navigate(url);
            host.Focus();
        }

        string pendingUrl;

        public void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (wv == null) { pendingUrl = url; if (Visible) CreateView(); return; }
            wv.Navigate(url);
        }

        string LocalFileUrl(string t)
        {
            string path = null;
            try
            {
                if (t.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    string bare, suffix;
                    LocalUrl.Split(t, out bare, out suffix);
                    path = new Uri(bare).LocalPath;
                    if (!File.Exists(path)) return null;
                    string full0 = Path.GetFullPath(path);
                    if (AgentPaths.SensitiveReal(full0) != null) return null;
                    AllowFolder(Path.GetDirectoryName(full0));
                    return new Uri(full0).AbsoluteUri + suffix;
                }
                bool rooted = (t.Length > 2 && t[1] == ':' && (t[2] == '\\' || t[2] == '/')) || t.StartsWith("\\\\");
                if (!rooted) return null;
                string full = Path.GetFullPath(t);
                if (!File.Exists(full) || AgentPaths.SensitiveReal(full) != null) return null;
                AllowFolder(Path.GetDirectoryName(full));
                return new Uri(full).AbsoluteUri;
            }
            catch { return null; }
        }

        static bool LocalHost(string host)
        {
            string h = (host ?? "").ToLowerInvariant();
            int colon = h.LastIndexOf(':');
            if (colon > 0 && h.IndexOf(']') < 0) h = h.Substring(0, colon);
            if (h == "localhost" || h.EndsWith(".localhost") || h.EndsWith(".local") || h.EndsWith(".lan") || h.EndsWith(".internal") || h.EndsWith(".test") || h == "[::1]") return true;
            string[] parts = h.Split('.');
            int a, b;
            if (parts.Length == 4 && int.TryParse(parts[0], out a) && int.TryParse(parts[1], out b))
                return a == 127 || a == 10 || (a == 192 && b == 168) || (a == 172 && b >= 16 && b <= 31) || (a == 169 && b == 254);
            return false;
        }

        public static string NormalizeUserUrl(string t)
        {
            string low = t.ToLowerInvariant();
            if (low.StartsWith("http://") || low.StartsWith("https://")) return t;
            if (low.StartsWith("javascript:") || low.StartsWith("file:") || low.StartsWith("data:") || low.StartsWith("vbscript:")) return null;
            if (t.IndexOf(' ') < 0)
            {
                int slash = t.IndexOf('/');
                string host = slash > 0 ? t.Substring(0, slash) : t;
                if (LocalHost(host)) return "http://" + t;
                bool hostPort = System.Text.RegularExpressions.Regex.IsMatch(host, @"^[A-Za-z0-9-]+:\d{2,5}$");
                if (hostPort) return "http://" + t;
                if (host.IndexOf('.') > 0) return "https://" + t;
            }
            return "https://duckduckgo.com/?q=" + Uri.EscapeDataString(t);
        }

        volatile string lastSiteUrl = "";
        bool resetHistory;

        void Fire(string method, string json)
        {
            if (wv == null) return;
            try { wv.CallDevToolsProtocolMethod(method, json, new WvScriptDone(delegate (int hr, string r) { })); } catch { }
        }

        public void FocusAddress()
        {
            try
            {
                address.Box.Focus();
                address.Box.SelectAll();
            }
            catch { }
        }

        public void AgentRunEnded()
        {
            if (drawerOpen && !drawerUser) ShowConsole(false, false);
        }

        public void ResetSession()
        {
            ClosePopups();
            AgentRunEnded();
            ShowNotice(null);
            allowedFolder = null;
            pageNote = null;
            pageNeedsUser = false;
            lastSiteUrl = "";
            pendingUrl = null;
            agentSite = userSite = "";
            consoleLog.Clear();
            if (wv == null) return;
            resetHistory = true;
            resetNavId = 0;
            startPending = true;
            try { wv.NavigateToString(StartPage()); } catch { resetHistory = false; startPending = false; }
            if (!address.Box.Focused) address.Box.Text = "";
            UpdateButtons();
        }

        public static void OpenExternally(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        void ClosePopups()
        {
            foreach (BrowserPopup bp in popups.ToArray()) { try { bp.Close(); } catch { } }
            popups.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClosePopups();
                try { if (ctl != null) ctl.Close(); } catch { }
                ctl = null;
                wv = null;
                tip.Dispose();
            }
            base.Dispose(disposing);
        }

        public void Shutdown()
        {
            ClosePopups();
            try { if (ctl != null) ctl.Close(); } catch { }
            ctl = null;
            wv = null;
            ready = false;
        }

        string Ui(Action<Pending> start, int timeoutMs, CancelToken ct, out string result)
        {
            result = null;
            if (IsDisposed || !IsHandleCreated) return "the browser is not available";
            Pending p = new Pending();
            try
            {
                BeginInvoke(new Action(delegate
                {
                    try { start(p); }
                    catch (Exception ex) { p.Fail(ex.Message); }
                }));
            }
            catch (Exception ex) { return "the browser is not available (" + ex.Message + ")"; }
            DateTime begin = DateTime.UtcNow, until = begin.AddMilliseconds(timeoutMs);
            while (!p.Wait(40))
            {
                if (ct != null && ct.IsCancelled) return "stopped by the user";
                if (userDialogOpen && (DateTime.UtcNow - begin).TotalMilliseconds > 1500) return "the page is waiting for the user to answer a question";
                if (DateTime.UtcNow > until) return "the browser did not respond in time";
            }
            result = p.Result;
            return p.Error;
        }

        void Script(string js, Pending p)
        {
            if (wv == null) { p.Fail("the browser is not running"); return; }
            WvScriptDone d = new WvScriptDone(delegate (int hr, string json)
            {
                if (hr < 0) p.Fail("the page did not run the script (0x" + hr.ToString("X8") + ")");
                else p.Ok(json);
            });
            int r = wv.ExecuteScript(js, d);
            if (r < 0) p.Fail("could not run a script in the page (0x" + r.ToString("X8") + ")");
        }

        static string CdpError(string method, int hr, string json)
        {
            object o = Json.TryParse(json ?? "");
            string m = Json.Str(o, "message") ?? Json.Str(o, "error", "message");
            return method + " failed" + (m != null ? ": " + m : hr < 0 ? " (0x" + hr.ToString("X8") + ")" : "");
        }

        void Cdp(string method, string paramsJson, Pending p)
        {
            if (wv == null) { p.Fail("the browser is not running"); return; }
            WvScriptDone d = new WvScriptDone(delegate (int hr, string json)
            {
                if (hr < 0) { p.Fail(CdpError(method, hr, json)); return; }
                object o = Json.TryParse(json ?? "");
                Dictionary<string, object> obj = o as Dictionary<string, object>;
                if (obj != null && obj.ContainsKey("code") && obj.ContainsKey("message") && obj.Count <= 3) { p.Fail(CdpError(method, hr, json)); return; }
                p.Ok(json);
            });
            int r = wv.CallDevToolsProtocolMethod(method, paramsJson, d);
            if (r < 0) p.Fail(method + " failed (0x" + r.ToString("X8") + ")");
        }

        string CdpCall(string method, Dictionary<string, object> args, CancelToken ct, out object result) { return CdpCall(method, args, 20000, ct, out result); }

        string CdpCall(string method, Dictionary<string, object> args, int timeoutMs, CancelToken ct, out object result)
        {
            result = null;
            string json;
            string err = Ui(delegate (Pending p) { Cdp(method, Json.Write(args), p); }, timeoutMs, ct, out json);
            if (err != null) return err;
            result = Json.TryParse(json ?? "{}");
            return null;
        }

        string CreateWorld(CancelToken ct, out int id, out bool unsupported)
        {
            id = 0;
            unsupported = false;
            object tree;
            string err = CdpCall("Page.getFrameTree", Json.O(), ct, out tree);
            if (err != null) { unsupported = Unsupported(err); return err; }
            string frame = Json.Str(tree, "frameTree", "frame", "id");
            if (string.IsNullOrEmpty(frame)) { unsupported = true; return "the page has no main frame"; }
            object res;
            err = CdpCall("Page.createIsolatedWorld", Json.O("frameId", frame, "worldName", worldName, "grantUniveralAccess", false), ct, out res);
            if (err != null) { unsupported = Unsupported(err); return err; }
            id = Json.Int(res, "executionContextId");
            if (id == 0) { unsupported = true; return "the browser did not create a script context"; }
            return null;
        }

        static bool Unsupported(string err)
        {
            string e = (err ?? "").ToLowerInvariant();
            return e.Contains("wasn't found") || e.Contains("not found") || e.Contains("method not") || e.Contains("not allowed") || e.Contains("not supported");
        }

        static bool ContextGone(string err)
        {
            string e = (err ?? "").ToLowerInvariant();
            return e.Contains("context") || e.Contains("navigated") || e.Contains("target closed") || e.Contains("frame");
        }

        static object Unwrap(object v)
        {
            string inner = v as string;
            return inner != null && inner.StartsWith("{") ? Json.TryParse(inner) : v;
        }

        string Js(string js, CancelToken ct, out object value) { return Js(js, ct, false, out value); }

        string Js(string js, CancelToken ct, bool wait, out object value)
        {
            value = null;
            if (userDialogOpen) return "the page is waiting for the user to answer a question (\"" + userDialogText + "\"). Wait for the user, then try again.";
            string last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int id = worldId;
                if (id == 0)
                {
                    bool unsupported;
                    string we = CreateWorld(ct, out id, out unsupported);
                    if (we != null)
                    {
                        last = we;
                        if (attempt < 2 && (unsupported || ContextGone(we))) { ct.Sleep(150); continue; }
                        return "MinGent could not read this page safely (" + we + "). Wait a moment or reload the page, then try again.";
                    }
                    worldId = id;
                }
                object res;
                string err = CdpCall("Runtime.evaluate", Json.O("expression", js, "contextId", id, "returnByValue", true, "silent", true, "awaitPromise", wait), ct, out res);
                if (err != null)
                {
                    last = err;
                    if (attempt < 2 && ContextGone(err)) { worldId = 0; continue; }
                    return err;
                }
                object ex = Json.Get(res, "exceptionDetails");
                if (ex != null)
                {
                    string desc = Json.Str(ex, "exception", "description") ?? Json.Str(ex, "text") ?? "unknown error";
                    if (desc.Length > 300) desc = desc.Substring(0, 300);
                    return "a script in the page failed: " + desc;
                }
                value = Unwrap(Json.Get(res, "result", "value"));
                return null;
            }
            return "the page changed while it was being read; try again" + (last != null ? " (" + Clip(last, 120) + ")" : "");
        }

        void FocusEmulation(bool on)
        {
            try
            {
                if (!ready || wv == null) return;
                BeginInvoke(new Action(delegate
                {
                    if (wv == null) return;
                    WvScriptDone d = new WvScriptDone(delegate (int hr, string json) { });
                    keep.Add(d);
                    if (keep.Count > 400) keep.RemoveRange(0, 100);
                    try { wv.CallDevToolsProtocolMethod("Emulation.setFocusEmulationEnabled", Json.Write(Json.O("enabled", on)), d); } catch { }
                }));
            }
            catch { }
        }

        void Busy(bool on)
        {
            if (on) { if (Interlocked.Increment(ref agentBusy) == 1) FocusEmulation(true); }
            else if (Interlocked.Decrement(ref agentBusy) <= 0) { Interlocked.Exchange(ref agentBusy, 0); FocusEmulation(false); }
            if (!on) actedUntil = DateTime.UtcNow.AddSeconds(3);
            try { BeginInvoke(new Action(delegate { Invalidate(new Rectangle(0, ToolbarH - UI.S(4), Width, UI.S(4))); })); } catch { }
        }

        string Ensure(CancelToken ct)
        {
            if (!Available) return "the built-in browser needs the Microsoft Edge WebView2 Runtime, which is not installed on this PC. Use web_fetch instead.";
            if (ready && wv != null) return null;
            if (wiping) return "the browser's data is being wiped right now; try again in a moment";
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (ShowRequested != null) ShowRequested(this, EventArgs.Empty);
                    if (failure != null && !missing) failure = null;
                    CreateView();
                }));
            }
            catch (Exception ex) { return "the browser is not available (" + ex.Message + ")"; }
            DateTime until = DateTime.UtcNow.AddSeconds(40);
            while (!(ready && wv != null))
            {
                if (failure != null) return "the browser could not start: " + failure;
                if (missing) return "the built-in browser needs the Microsoft Edge WebView2 Runtime. Use web_fetch instead.";
                if (ct != null && ct.IsCancelled) return "stopped by the user";
                if (DateTime.UtcNow > until) return "the browser took too long to start";
                Thread.Sleep(60);
            }
            return null;
        }

        void Reveal()
        {
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (Visible || ShowRequested == null) return;
                    ShowRequested(this, EventArgs.Empty);
                    if (Visible) { agentOpened = true; userTouched = false; }
                }));
            }
            catch { }
        }

        volatile bool agentOpened, userTouched;

        public bool UserIsUsing { get { return userTouched && Visible; } }

        public void MarkUserOpened()
        {
            agentOpened = false;
            userTouched = true;
        }

        public string CloseForAgent(bool userAsked)
        {
            if (!IsHandleCreated) return "The browser pane is not open.";
            string res = null;
            try
            {
                Invoke(new Action(delegate
                {
                    if (!Visible) { res = "The browser pane is already closed."; return; }
                    bool blank = currentUrl.Length == 0 || currentUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
                    if (!userAsked && !blank)
                    {
                        string host = HostOf(currentUrl);
                        string shows = host.Length > 0 ? " (it shows " + host + ")" : "";
                        if (!agentOpened) { res = "The user opened the browser pane themselves" + shows + ", so it stays open. Close it only if the user asks."; return; }
                        if (userTouched) { res = "The user has been using the browser pane" + shows + ", so it stays open. Close it only if the user asks."; return; }
                    }
                    if (CloseRequested != null) CloseRequested(this, EventArgs.Empty);
                    if (!Visible && drawerOpen && !drawerUser) ShowConsole(false, false);
                    res = Visible ? "The browser pane could not be closed." : "Closed the browser pane. It opens again if you use the browser.";
                }));
            }
            catch (Exception ex) { return "The browser pane could not be closed (" + ex.Message + ")."; }
            return res ?? "The browser pane is not open.";
        }

        public string CloseConsoleForAgent(bool userAsked)
        {
            if (!IsHandleCreated) return "The console is not open.";
            string res = null;
            try
            {
                Invoke(new Action(delegate
                {
                    if (!drawerOpen) { res = "The console is already closed."; return; }
                    if (drawerUser && !userAsked) { res = "The user is using the console, so it stays open. Close it only if the user asks."; return; }
                    ShowConsole(false, false);
                    res = drawerOpen ? "The console could not be closed." : "Closed the console. The browser pane stays open.";
                }));
            }
            catch (Exception ex) { return "The console could not be closed (" + ex.Message + ")."; }
            return res ?? "The console is not open.";
        }

        string WaitNavigation(int startCount, int startWaitMs, int timeoutMs, CancelToken ct, out string note)
        {
            note = "";
            DateTime begin = DateTime.UtcNow;
            while (navStarts == startCount && !loading)
            {
                if ((DateTime.UtcNow - begin).TotalMilliseconds > startWaitMs) break;
                if (ct.IsCancelled) return "stopped by the user";
                if (userDialogOpen) return null;
                Thread.Sleep(40);
            }
            DateTime until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (loading)
            {
                if (ct.IsCancelled) return "stopped by the user";
                if (userDialogOpen) return null;
                if (DateTime.UtcNow > until) { note = " (the page is still loading)"; break; }
                Thread.Sleep(50);
            }
            if (navStarts != startCount && !lastNavOk && !loading && !HasDownload)
            {
                string why = WebError(lastNavError);
                if (why != null) return "the page could not be opened: " + why;
            }
            DateTime settle = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < settle && !ct.IsCancelled && !userDialogOpen)
            {
                object state;
                if (Js("document.readyState", ct, out state) != null) break;
                if (state as string == "complete") break;
                Thread.Sleep(150);
            }
            ct.Sleep(250);
            return null;
        }

        static string WebError(int s)
        {
            switch (s)
            {
                case 1: case 2: case 3: case 4: case 5: return "the site's security certificate is not valid";
                case 6: return "the server could not be reached";
                case 7: return "the connection timed out";
                case 8: return "the server sent an invalid response";
                case 9: case 10: case 11: return "the connection was interrupted";
                case 12: return "could not connect to the server";
                case 13: return "the address could not be found (check the spelling)";
                case 14: return null;
                case 15: return "the site redirected too many times";
                case 17: return "the site requires a login (HTTP authentication)";
                case 18: return "a proxy login is required";
                default: return null;
            }
        }

        string Snap(CancelToken ct, out string text)
        {
            text = null;
            object v;
            int max = Math.Max(4000, Math.Min(40000, SnapshotChars));
            string err = Js("(" + BrowserScripts.Snapshot + ")(" + max.ToString(CultureInfo.InvariantCulture) + ")", ct, out v);
            if (err != null) return err;
            if (!(v is Dictionary<string, object>)) return "could not read the page";
            StringBuilder sb = new StringBuilder();
            string title = Clip(Json.Str(v, "title") ?? "", 200), url = Json.Str(v, "url") ?? currentUrl;
            if (url.Length > 2000) url = url.Substring(0, 2000) + "...";
            sb.Append("Page: ").Append(title.Length > 0 ? title : "(no title)").Append('\n');
            sb.Append("URL: ").Append(url).Append('\n');
            double screens = Json.Num(v, "screens");
            if (screens > 1.2) sb.Append("Scrolled ").Append(Json.Int(v, "scroll")).Append("% down a page about ").Append(Math.Round(screens).ToString(CultureInfo.InvariantCulture)).Append(" screens long\n");
            lastTitle = title;
            lastChallenge = Json.Bool(v, "challenge");
            lastInterstitial = lastChallenge && Json.Bool(v, "interstitial");
            pageNote = null;
            pageNeedsUser = false;
            if (Json.Bool(v, "googleBlocked"))
            {
                pageNote = "Google does not allow signing in from apps' built-in browsers, including this one. Next: tell the user they can use the \"Open in your browser\" button above the page, or another way to sign in.";
                pageNeedsUser = true;
            }
            else if (lastChallenge && CaptchaMode)
            {
                bool typedCheck = Json.Bool(v, "captchaPicture") && Json.Bool(v, "captchaAnswer");
                pageNote = HostLabel(url) + " shows a human-verification check (CAPTCHA). CAPTCHA mode is on (the user turned it on in Settings), so you may try to solve it yourself: " +
                           (AgentCanSee ? "what it shows can only be seen in a screenshot. " + (typedCheck ? "Take browser_screenshot (MinGent adds a zoomed copy of the CAPTCHA picture), then type the characters it shows into its answer box with browser_type and submit."
                                                      : "Take browser_screenshot, then click, drag or type in it by position (browser_click with x and y). Look carefully before each click.") + " When MinGent says the tries are used up, ask the user to solve it with ask_user."
                                        : "this model can't see images, so you can only click the captcha refs in the page text. If that does not get through, ask the user to solve it with ask_user.");
            }
            else if (lastChallenge)
            {
                pageNote = HostLabel(url) + " shows a human-verification check (CAPTCHA) on this page. MinGent does not solve these checks; only the user can.";
                pageNeedsUser = true;
            }
            string widget = Json.Str(v, "widget") ?? "";
            bool picture = Json.Bool(v, "captchaPicture"), typed = Json.Bool(v, "captchaAnswer");
            string box = Json.Str(v, "captchaBox") ?? "";
            lastWidget = widget.Length > 0 || picture || box.Length > 0;
            if (!lastChallenge && widget.Length > 0 && pageNote == null)
                pageNote = HostLabel(url) + " has a CAPTCHA box on this page (" + Clip(widget, 80) + "), usually next to a form. You can read and fill in the page normally. " +
                           (CaptchaMode ? "CAPTCHA mode is on, so when the form needs it you may try it yourself: click its captcha ref" + (AgentCanSee ? ", then take browser_screenshot to see if a picture puzzle opened." : ".") + " If it does not let you through, ask the user."
                                        : "When you get to the point where the form needs it, ask the user to tick it with ask_user; MinGent leaves CAPTCHAs to the user.");
            else if (!lastChallenge && (picture || box.Length > 0) && pageNote == null)
                pageNote = HostLabel(url) + (box.Length > 0 ? " has a CAPTCHA puzzle on this page (" + Clip(box, 40) + ")" : " has a CAPTCHA picture on this page" + (typed ? ", with a box to type what it shows" : "")) + ". You can read and fill in the page normally. " +
                           (!CaptchaMode ? "When you get to the point where the form needs it, ask the user to solve it with ask_user; MinGent leaves CAPTCHAs to the user."
                            : AgentCanSee ? "CAPTCHA mode is on, so when the form needs it you may try it yourself: take browser_screenshot (MinGent adds a zoomed copy of the CAPTCHA)" + (box.Length == 0 && typed ? ", then type the characters it shows into its answer box with browser_type." : ", then do what it asks with browser_click or browser_drag by position.") + " If it does not let you through, ask the user."
                            : "CAPTCHA mode is on, but this model can't see pictures, so ask the user to solve it with ask_user when the form needs it.");
            if (Json.Bool(v, "modal")) sb.Append("Note: a dialog is open on top of the page. Deal with it first (for example accept or close it).\n");
            if (Json.Int(v, "xframes") > 0) sb.Append("Note: parts of this page are frames from other sites, which can't be read as text here").Append(AgentCanSee ? "; browser_screenshot shows them.\n" : ".\n");
            sb.Append('\n');
            string body = Json.Str(v, "text") ?? "";
            sb.Append(body.Trim().Length > 0 ? body.TrimEnd() : "(the page shows no text)");
            sb.Append('\n');
            if (Json.Bool(v, "cut")) sb.Append("(the page is longer; the rest was cut to save space. Scroll or use web_fetch to read more)\n");
            int refs = Json.Int(v, "refs");
            if (refs > 0) sb.Append("Refs like e12 identify elements for browser_click and browser_type until the page changes.\n");
            text = sb.ToString();
            return null;
        }

        static string HostLabel(string url)
        {
            Uri u;
            return Uri.TryCreate(url ?? "", UriKind.Absolute, out u) && u.Host.Length > 0 ? u.Host : "This page";
        }

        string SettleChallenge(CancelToken ct, string snap)
        {
            if (!lastInterstitial) return snap;
            DateTime until = DateTime.UtcNow.AddSeconds(12);
            object st;
            string seen = Js("document.title + '\\n' + location.href", ct, out st) == null ? (st as string ?? "") : lastTitle + "\n" + currentUrl;
            int nav = navStarts;
            bool waited = false;
            while (DateTime.UtcNow < until && !ct.IsCancelled && !userDialogOpen)
            {
                waited = true;
                if (!ct.Sleep(800)) break;
                if (Js("document.title + '\\n' + location.href", ct, out st) != null) continue;
                string now = st as string ?? "";
                if (navStarts == nav && now == seen && !loading) continue;
                string note;
                if (WaitNavigation(nav, 300, 15000, ct, out note) != null) break;
                string again;
                if (Snap(ct, out again) != null) break;
                snap = again;
                if (!lastInterstitial) return snap;
                if (Js("document.title + '\\n' + location.href", ct, out st) == null) seen = st as string ?? "";
                nav = navStarts;
            }
            if (waited && lastChallenge && !ct.IsCancelled)
            {
                if (CaptchaMode)
                    pageNote = HostLabel(currentUrl) + " shows a bot check that did not clear by itself within about 12 seconds. CAPTCHA mode is on, so you may try it yourself" +
                               (AgentCanSee ? " with browser_screenshot and clicks by position" : " by clicking its captcha ref") + "; when MinGent says the tries are used up, ask the user to solve it with ask_user.";
                else
                {
                    pageNote = HostLabel(currentUrl) + " shows a bot check that did not clear by itself within about 12 seconds. MinGent does not solve these checks; only the user can.";
                    pageNeedsUser = true;
                }
            }
            return snap;
        }

        static string NormRef(string refId)
        {
            string r = (refId ?? "").Trim().TrimStart('[').TrimEnd(']').Trim();
            if (r.StartsWith("ref=")) r = r.Substring(4);
            if (r.Length > 0 && char.IsDigit(r[0])) r = "e" + r;
            return r;
        }

        string Prep(string refId, bool scroll, CancelToken ct, out Dictionary<string, object> info)
        {
            info = null;
            string r = NormRef(refId);
            if (!Regex.IsMatch(r, "^e\\d{1,6}$")) return "\"" + refId + "\" is not an element ref. Use a ref like e12 from browser_read.";
            object v;
            string err = Js("(" + BrowserScripts.Prep + ")(" + Json.Quote(r) + "," + (scroll ? "true" : "false") + ")", ct, out v);
            if (err != null) return err;
            info = v as Dictionary<string, object>;
            if (info == null) return "could not find " + r + " on the page";
            if (Json.Str(info, "err") == "stale") return r + " is no longer on the page (it changed or was reloaded). Call browser_read to get fresh refs.";
            return null;
        }

        void ContextMenus(bool on)
        {
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (mainSettings == null) return;
                    try { mainSettings.SetAreDefaultContextMenusEnabled(on ? 1 : 0); } catch { }
                }));
            }
            catch { }
        }

        string Mouse(double x, double y, CancelToken ct) { return Mouse(x, y, new MouseSpec(), ct); }

        string Mouse(double x, double y, MouseSpec m, CancelToken ct)
        {
            if (m == null) m = new MouseSpec();
            int mask = m.Button == "right" ? 2 : m.Button == "middle" ? 4 : 1;
            int mods = m.CdpModifiers;
            bool right = m.Button == "right";
            if (right)
            {
                Interlocked.Increment(ref menuOff);
                ContextMenus(false);
                ct.Sleep(60);
            }
            try
            {
                object res;
                string err = CdpCall("Input.dispatchMouseEvent", Json.O("type", "mouseMoved", "x", x, "y", y, "modifiers", mods), ct, out res);
                if (err != null) return err;
                ct.Sleep(30);
                for (int i = 1; i <= Math.Max(1, m.Clicks); i++)
                {
                    err = CdpCall("Input.dispatchMouseEvent", Json.O("type", "mousePressed", "x", x, "y", y, "button", m.Button, "buttons", mask, "clickCount", i, "modifiers", mods), ct, out res);
                    if (err != null) return err;
                    if (m.HoldMs > 0 && i == m.Clicks) { if (!ct.Sleep(m.HoldMs)) return "stopped by the user"; }
                    else ct.Sleep(40);
                    err = CdpCall("Input.dispatchMouseEvent", Json.O("type", "mouseReleased", "x", x, "y", y, "button", m.Button, "buttons", 0, "clickCount", i, "modifiers", mods), ct, out res);
                    if (err != null) return err;
                    if (i < m.Clicks) ct.Sleep(30);
                }
                return null;
            }
            finally
            {
                if (right)
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(1200);
                        if (Interlocked.Decrement(ref menuOff) <= 0) { Interlocked.Exchange(ref menuOff, 0); ContextMenus(true); }
                    });
                }
            }
        }

        bool ScreenOf(double cssX, double cssY, CancelToken ct, out System.Drawing.Point screen)
        {
            screen = System.Drawing.Point.Empty;
            if (!AppHighlight.Usable) return false;
            try
            {
                object pv;
                if (Js("(" + BrowserScripts.Probe + ")()", ct, out pv) != null) return false;
                double cssW = Json.Num(pv, "w");
                if (cssW <= 0) return false;
                string at;
                string err = Ui(delegate (Pending p)
                {
                    if (!host.IsHandleCreated || !host.Visible || host.ClientSize.Width <= 0) { p.Ok(""); return; }
                    double sc = host.ClientSize.Width / cssW;
                    System.Drawing.Point sp = host.PointToScreen(new System.Drawing.Point((int)Math.Round(cssX * sc), (int)Math.Round(cssY * sc)));
                    IntPtr hit = WindowFromPoint(sp);
                    if (hit == IntPtr.Zero || GetAncestor(hit, 2) != GetAncestor(host.Handle, 2)) { p.Ok(""); return; }
                    p.Ok(sp.X.ToString(CultureInfo.InvariantCulture) + "," + sp.Y.ToString(CultureInfo.InvariantCulture));
                }, 2000, ct, out at);
                if (err != null || string.IsNullOrEmpty(at)) return false;
                string[] xy = at.Split(',');
                screen = new System.Drawing.Point(int.Parse(xy[0], CultureInfo.InvariantCulture), int.Parse(xy[1], CultureInfo.InvariantCulture));
                return true;
            }
            catch (Exception) { return false; }
        }

        void PointAt(double cssX, double cssY, bool click, bool wait, CancelToken ct)
        {
            System.Drawing.Point sp;
            if (!ScreenOf(cssX, cssY, ct, out sp)) return;
            int ms = AgentCursor.PointAt(sp, click);
            if (wait && ms > 0) ct.Sleep(ms);
        }

        string MoveMouse(double x, double y, int buttons, CancelToken ct)
        {
            object res;
            Dictionary<string, object> a = Json.O("type", "mouseMoved", "x", x, "y", y);
            if (buttons != 0) { a["button"] = "left"; a["buttons"] = buttons; }
            return CdpCall("Input.dispatchMouseEvent", a, ct, out res);
        }

        static string Blocked(KeyPress k)
        {
            if (k.Ctrl && !k.Alt && (k.Code == "KeyP" || k.Code == "KeyS" || k.Code == "KeyO")) return k.Label + " opens a Windows " + (k.Code == "KeyP" ? "print" : k.Code == "KeyS" ? "save" : "open") + " dialog that the agent can't see or use, so it was not pressed. Use the page's own buttons, or ask the user.";
            if (k.Code == "F12" || (k.Ctrl && k.Shift && (k.Code == "KeyI" || k.Code == "KeyJ" || k.Code == "KeyC"))) return k.Label + " opens the developer tools, which the agent can't use, so it was not pressed. browser_console reads the page's console and runs code in it.";
            if (k.Win) return "the Windows key acts on Windows itself rather than the page, so it can't be pressed in the browser";
            return null;
        }

        string SendKey(KeyPress k, CancelToken ct) { return SendKey(k, 0, ct); }

        Dictionary<string, object> KeyEvent(KeyPress k, string type, int mask, bool typed, bool repeat)
        {
            string text = typed && type == "keyDown" ? k.Text : null;
            Dictionary<string, object> e = Json.O("type", type == "keyDown" && text == null ? "rawKeyDown" : type, "key", k.Key, "code", k.Code, "windowsVirtualKeyCode", k.Vk, "nativeVirtualKeyCode", k.Vk, "modifiers", mask);
            if (text != null) { e["text"] = text; e["unmodifiedText"] = text; }
            if (k.Keypad) { e["isKeypad"] = true; e["location"] = 3; }
            if (repeat) e["autoRepeat"] = true;
            return e;
        }

        string SendKey(KeyPress k, int holdMs, CancelToken ct)
        {
            object res;
            bool chord = holdMs > 0 || k.With != null;
            List<KeyPress> held = new List<KeyPress>(), down = new List<KeyPress>();
            int mask = 0;
            string[] order = { "Control", "Alt", "Shift" };
            bool[] want = { k.Ctrl, k.Alt, k.Shift && (chord || !(k.Printable && k.Char != 0 && !k.Ctrl && !k.Alt)) };
            CancelToken release = new CancelToken();
            string err = null;
            try
            {
                for (int i = 0; i < order.Length; i++)
                {
                    if (!want[i]) continue;
                    string merr;
                    KeyPress mk = KeyCombo.Parse(order[i], out merr);
                    if (mk == null) continue;
                    mask |= i == 0 ? 2 : i == 1 ? 1 : 8;
                    held.Add(mk);
                    err = CdpCall("Input.dispatchKeyEvent", Json.O("type", "rawKeyDown", "key", mk.Key, "code", mk.Code, "windowsVirtualKeyCode", mk.Vk, "nativeVirtualKeyCode", mk.Vk, "modifiers", mask, "location", 1), ct, out res);
                    if (err != null) return err;
                }
                if (k.Shift) mask |= 8;
                bool plain = !k.Ctrl && !k.Alt && !k.Win;
                if (k.With != null)
                    foreach (KeyPress w in k.With)
                    {
                        down.Add(w);
                        err = CdpCall("Input.dispatchKeyEvent", KeyEvent(w, "keyDown", mask, plain, false), ct, out res);
                        if (err != null) return err;
                    }
                down.Add(k);
                err = CdpCall("Input.dispatchKeyEvent", KeyEvent(k, "keyDown", mask, plain, false), ct, out res);
                if (err != null) return err;
                if (holdMs > 0)
                {
                    DateTime start = DateTime.UtcNow, end = start.AddMilliseconds(holdMs), next = start.AddMilliseconds(500);
                    while (DateTime.UtcNow < end && !ct.IsCancelled && !userDialogOpen)
                    {
                        if (DateTime.UtcNow >= next)
                        {
                            CdpCall("Input.dispatchKeyEvent", KeyEvent(k, "keyDown", mask, plain, true), ct, out res);
                            next = next.AddMilliseconds(80);
                        }
                        Thread.Sleep(15);
                    }
                }
            }
            finally
            {
                for (int i = down.Count - 1; i >= 0; i--)
                {
                    string uerr = CdpCall("Input.dispatchKeyEvent", KeyEvent(down[i], "keyUp", mask, false, false), 3000, release, out res);
                    if (err == null && uerr != null && i == down.Count - 1) err = uerr;
                }
                for (int i = held.Count - 1; i >= 0; i--)
                {
                    KeyPress mk = held[i];
                    mask &= ~(mk.Code == "ControlLeft" ? 2 : mk.Code == "AltLeft" ? 1 : 8);
                    CdpCall("Input.dispatchKeyEvent", Json.O("type", "keyUp", "key", mk.Key, "code", mk.Code, "windowsVirtualKeyCode", mk.Vk, "nativeVirtualKeyCode", mk.Vk, "modifiers", mask, "location", 1), 3000, release, out res);
                }
            }
            return err;
        }

        string Key(string key, CancelToken ct)
        {
            string perr;
            KeyPress k = KeyCombo.Parse(key, out perr);
            if (k == null) return perr;
            return SendKey(k, ct);
        }

        string TypeChar(string s, CancelToken ct)
        {
            object res;
            if (s.Length == 0) return null;
            char c = s[0];
            if (c == '\n' || c == '\r' || c == '\t' || char.IsSurrogate(c) || char.IsControl(c)) return CdpCall("Input.insertText", Json.O("text", s), ct, out res);
            int vk = 0;
            string code = "";
            if (c < 128 && char.IsLetter(c)) { vk = char.ToUpperInvariant(c); code = "Key" + char.ToUpperInvariant(c); }
            else if (c < 128 && char.IsDigit(c)) { vk = c; code = "Digit" + c; }
            else if (c == ' ') { vk = 32; code = "Space"; }
            Dictionary<string, object> down = Json.O("type", "keyDown", "key", s, "text", s, "unmodifiedText", s, "windowsVirtualKeyCode", vk, "nativeVirtualKeyCode", vk);
            if (code.Length > 0) down["code"] = code;
            string err = CdpCall("Input.dispatchKeyEvent", down, ct, out res);
            if (err != null) return err;
            Dictionary<string, object> up = Json.O("type", "keyUp", "key", s, "windowsVirtualKeyCode", vk, "nativeVirtualKeyCode", vk);
            if (code.Length > 0) up["code"] = code;
            return CdpCall("Input.dispatchKeyEvent", up, ct, out res);
        }

        string InsertText(string text, CancelToken ct)
        {
            System.Globalization.StringInfo si = new System.Globalization.StringInfo(text);
            int n = si.LengthInTextElements;
            string last = n > 0 ? si.SubstringByTextElements(n - 1) : "";
            string head = n > 1 ? si.SubstringByTextElements(0, n - 1) : "";
            object res;
            if (head.Length > 0)
            {
                string err = CdpCall("Input.insertText", Json.O("text", head), ct, out res);
                if (err != null) return err;
            }
            return last.Length == 1 ? TypeChar(last, ct) : CdpCall("Input.insertText", Json.O("text", last), ct, out res);
        }

        string AfterAction(int startCount, string before, CancelToken ct, string done, out string summary)
        {
            summary = null;
            string note;
            string err = WaitNavigation(startCount, 700, 20000, ct, out note);
            if (err != null) return err;
            return Report(before, done, note, true, ct, out summary);
        }

        string Report(string before, string done, string note, bool settle, CancelToken ct, out string summary)
        {
            summary = null;
            if (userDialogOpen)
            {
                summary = done + TakeEvents() + " The page is now waiting for the user to answer: \"" + userDialogText + "\". Wait for the user, then call browser_read.";
                return null;
            }
            string happened = TakeEvents();
            string snap;
            string err = Snap(ct, out snap);
            if (err != null) return err;
            if (settle) snap = SettleChallenge(ct, snap);
            string now = currentUrl;
            string changed = now != before && now.Length > 0 ? " The browser is now at " + now + note + "." : note.Length > 0 ? note.Trim() : "";
            string dl = "";
            string dn, du;
            long db;
            if (PendingDownload(out dn, out du, out db)) dl = " The page wants to download " + dn + " (" + FormatBytes(db) + ").";
            summary = done + changed + happened + dl + "\n\n" + snap;
            return null;
        }

        public void AllowFolder(string folder) { allowedFolder = folder; }

        public string Navigate(string url, CancelToken ct, out string summary)
        {
            summary = null;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    ArmEvents(ct);
                    string u = (url ?? "").Trim();
                    string low = u.ToLowerInvariant();
                    int start = navStarts;
                    string ignored;
                    err = Ui(delegate (Pending p)
                    {
                        int can = 0;
                        if (low == "back") { wv.CanGoBack(out can); if (can == 0) { p.Fail("there is no previous page"); return; } wv.GoBack(); }
                        else if (low == "forward") { wv.CanGoForward(out can); if (can == 0) { p.Fail("there is no next page"); return; } wv.GoForward(); }
                        else if (low == "reload") wv.Reload();
                        else
                        {
                            int hr = wv.Navigate(u);
                            if (hr < 0) { p.Fail("that address could not be opened (0x" + hr.ToString("X8") + ")"); return; }
                        }
                        p.Ok(null);
                    }, 8000, ct, out ignored);
                    if (err != null) return err;
                    string note;
                    err = WaitNavigation(start, 3000, 30000, ct, out note);
                    if (err != null) return err;
                    string happened = TakeEvents();
                    if (userDialogOpen)
                    {
                        summary = "The page is waiting for the user to answer: \"" + userDialogText + "\"." + happened;
                        return null;
                    }
                    string snap;
                    err = Snap(ct, out snap);
                    if (err != null) return err;
                    snap = SettleChallenge(ct, snap);
                    string dn, du;
                    long db;
                    string dl = PendingDownload(out dn, out du, out db) ? "The address started a download of " + dn + " (" + FormatBytes(db) + ").\n" : "";
                    summary = (note.Length > 0 ? note.Trim() + "\n" : "") + (happened.Length > 0 ? happened.Trim() + "\n" : "") + dl + snap;
                    return null;
                }
                finally { Busy(false); }
            }
        }

        public string OpenSearch(string url, CancelToken ct, out string html, out string finalUrl)
        {
            html = null;
            finalUrl = null;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    ArmEvents(ct);
                    int start = navStarts;
                    string ignored;
                    err = Ui(delegate (Pending p)
                    {
                        int hr = wv.Navigate(url);
                        if (hr < 0) { p.Fail("the search page could not be opened (0x" + hr.ToString("X8") + ")"); return; }
                        p.Ok(null);
                    }, 8000, ct, out ignored);
                    if (err != null) return err;
                    string note;
                    err = WaitNavigation(start, 3000, 25000, ct, out note);
                    TakeEvents();
                    if (err != null) return err;
                    if (userDialogOpen) return "the page is waiting for the user to answer a question";
                    object v;
                    err = Js("(" + BrowserScripts.PageHtml + ")()", ct, out v);
                    if (err != null) return err;
                    html = Json.Str(v, "html") ?? "";
                    finalUrl = Json.Str(v, "url") ?? currentUrl;
                    return null;
                }
                finally { Busy(false); }
            }
        }

        public string Read(CancelToken ct, out string text)
        {
            text = null;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try { return Snap(ct, out text); }
                finally { Busy(false); }
            }
        }

        public bool PageIsCheck { get { return pageNeedsUser && lastChallenge; } }

        public bool CaptchaOnPage { get { return lastChallenge || lastWidget; } }

        readonly List<ICoreWebView2_4> offlineViews = new List<ICoreWebView2_4>();

        void OnResource(ICoreWebView2WebResourceRequestedEventArgs a)
        {
            if (!Net.Offline || env == null) return;
            ICoreWebView2WebResourceRequest req;
            if (a.Request(out req) < 0 || req == null) return;
            IntPtr p;
            string uri = req.Uri(out p) >= 0 ? WebView2Runtime.Str(p) : "";
            string low = uri.ToLowerInvariant();
            bool checkable = low.StartsWith("http://") || low.StartsWith("https://") || low.StartsWith("ws://") || low.StartsWith("wss://") || low.StartsWith("file:");
            if (!checkable || Net.IsLocal(uri)) return;
            IntPtr resp;
            if (env.CreateWebResourceResponse(IntPtr.Zero, 403, "Blocked by offline mode", "Content-Type: text/plain", out resp) < 0 || resp == IntPtr.Zero) return;
            try { a.SetResponse(resp); } finally { Marshal.Release(resp); }
        }

        public void ApplyOffline()
        {
            List<ICoreWebView2_4> views = new List<ICoreWebView2_4>();
            if (wv != null) views.Add(wv);
            foreach (BrowserPopup bp in popups) if (bp.View != null) views.Add(bp.View);
            offlineViews.RemoveAll(delegate (ICoreWebView2_4 v) { return !views.Contains(v); });
            foreach (ICoreWebView2_4 v in views)
            {
                bool on = offlineViews.Contains(v);
                try
                {
                    if (Net.Offline && !on && v.AddWebResourceRequestedFilter("*", 0) >= 0) offlineViews.Add(v);
                    else if (!Net.Offline && on) { v.RemoveWebResourceRequestedFilter("*", 0); offlineViews.Remove(v); }
                }
                catch { }
            }
            if (Net.Offline)
                foreach (BrowserPopup bp in popups.ToArray())
                {
                    IntPtr p;
                    string src = bp.View != null && bp.View.Source(out p) >= 0 ? WebView2Runtime.Str(p) : "";
                    if (src.Length > 0 && !Net.IsLocal(src)) { try { bp.Close(); } catch { } }
                }
            LeaveWebIfOffline();
            if (envOffline != Net.Offline && (ready || creating)) RestartEngine();
        }

        bool envOffline;

        void RestartEngine()
        {
            if (wiping) return;
            wiping = true;
            string url = currentUrl;
            ClosePopups();
            object[] coms = { wv, ctl, env };
            try { if (ctl != null) ctl.Close(); } catch { }
            ready = false;
            creating = false;
            wv = null;
            ctl = null;
            env = null;
            worldId = 0;
            foreach (object o in coms) { try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch { } }
            keep.Clear();
            offlineViews.Clear();
            host.Invalidate();
            Thread t = new Thread(delegate ()
            {
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    BrowserData.WaitUnlocked(BrowserData.Folder, 15000);
                }
                catch { }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        wiping = false;
                        if (!string.IsNullOrEmpty(url) && (!Net.Offline || Net.IsLocal(url))) pendingUrl = url;
                        host.Invalidate();
                        if (Visible && IsHandleCreated && !IsDisposed) CreateView();
                    }));
                }
                catch { wiping = false; }
            });
            t.IsBackground = true;
            t.Name = "MinGent browser restart";
            t.Start();
        }

        public void LeaveWebIfOffline()
        {
            if (!Net.Offline || !ready || wv == null) return;
            string cur = currentUrl ?? "";
            if (cur.Length == 0 || Net.IsLocal(cur)) return;
            try { BeginInvoke(new Action(delegate { try { if (wv != null) wv.Navigate("about:blank"); } catch { } })); } catch { }
        }

        public string YouTubeTranscript(string url, CancelToken ct, out object result)
        {
            result = null;
            string summary;
            string err = Navigate(url, ct, out summary);
            if (err != null) return err;
            lock (opLock)
            {
                Busy(true);
                try
                {
                    ct.Sleep(1200);
                    return Js(BrowserScripts.YouTubeTranscript, ct, true, out result);
                }
                finally { Busy(false); }
            }
        }

        const int HistoryKinds = 256 | 512 | 1024 | 4096;
        volatile bool wiping;

        public bool Wiping { get { return wiping; } }

        public void ClearHistory()
        {
            BrowserData.Mark(BrowserData.ClearMarker);
            if (InvokeRequired) { try { BeginInvoke(new Action(ClearHistory)); } catch { } return; }
            if (!ready || wv == null || wiping) return;
            try
            {
                ICoreWebView2_13 v13 = wv as ICoreWebView2_13;
                ICoreWebView2Profile prof;
                if (v13 != null && v13.Profile(out prof) >= 0 && prof != null)
                {
                    ICoreWebView2Profile2 p2 = prof as ICoreWebView2Profile2;
                    if (p2 != null)
                    {
                        WvClearDone d = new WvClearDone(delegate (int hr) { if (hr >= 0) BrowserData.Unmark(BrowserData.ClearMarker); });
                        keep.Add(d);
                        if (p2.ClearBrowsingData(HistoryKinds, d) >= 0) return;
                    }
                }
            }
            catch { }
            try
            {
                WvScriptDone d2 = new WvScriptDone(delegate (int hr, string json) { BrowserData.Unmark(BrowserData.ClearMarker); });
                keep.Add(d2);
                wv.CallDevToolsProtocolMethod("Network.clearBrowserCache", "{}", d2);
            }
            catch { }
        }

        public void Release()
        {
            wiping = true;
            ClosePopups();
            object[] coms = { wv, ctl, env };
            try { if (ctl != null) ctl.Close(); } catch { }
            ready = false;
            creating = false;
            wv = null;
            ctl = null;
            env = null;
            foreach (object o in coms) { try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch { } }
            keep.Clear();
            offlineViews.Clear();
            host.Invalidate();
        }

        readonly List<Action<bool>> wipeWaiters = new List<Action<bool>>();

        public void WipeAll(Action<bool> done)
        {
            if (wiping) { if (done != null) wipeWaiters.Add(done); return; }
            wiping = true;
            BrowserData.Mark(BrowserData.WipeMarker);
            ClosePopups();
            ShowNotice(null);
            pageNote = null;
            pageNeedsUser = false;
            allowedFolder = null;
            pendingUrl = null;
            currentUrl = "";
            currentTitle = "";
            lastSiteUrl = "";
            object[] coms = { wv, ctl, env };
            try { if (ctl != null) ctl.Close(); } catch { }
            ready = false;
            creating = false;
            wv = null;
            ctl = null;
            env = null;
            worldId = 0;
            foreach (object o in coms) { try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch { } }
            keep.Clear();
            offlineViews.Clear();
            if (!address.Box.Focused) address.Box.Text = "";
            UpdateButtons();
            host.Invalidate();
            Invalidate();
            if (TitleChanged != null) TitleChanged("");
            Thread t = new Thread(delegate ()
            {
                bool ok = false;
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    BrowserData.WaitUnlocked(BrowserData.Folder, 15000);
                    int failed;
                    BrowserData.Wipe(BrowserData.Folder, out failed);
                    if (failed > 0) { Thread.Sleep(1500); BrowserData.Wipe(BrowserData.Folder, out failed); }
                    ok = failed == 0;
                    if (ok) { BrowserData.Unmark(BrowserData.WipeMarker); BrowserData.Unmark(BrowserData.ClearMarker); }
                }
                catch { }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        wiping = false;
                        host.Invalidate();
                        if (done != null) done(ok);
                        List<Action<bool>> waiting = new List<Action<bool>>(wipeWaiters);
                        wipeWaiters.Clear();
                        foreach (Action<bool> w in waiting) w(ok);
                        if (Visible && IsHandleCreated && !IsDisposed) CreateView();
                    }));
                }
                catch { wiping = false; }
            });
            t.IsBackground = true;
            t.Name = "MinGent browser wipe";
            t.Start();
        }

        public bool CheckCleared(CancelToken ct)
        {
            if (!ready || wv == null || userDialogOpen || loading) return false;
            lock (opLock)
            {
                string text;
                if (Snap(ct, out text) != null) return false;
                return !lastChallenge;
            }
        }

        public void ShowToUser() { Reveal(); }

        public string ElementInfo(string refId, CancelToken ct, out string kind, out bool sensitive)
        {
            kind = "";
            sensitive = false;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Dictionary<string, object> info;
                err = Prep(refId, false, ct, out info);
                if (err != null) return err;
                kind = Json.Str(info, "kind") ?? "";
                sensitive = Json.Bool(info, "sensitive");
                if (!Json.Bool(info, "captcha")) FrameKind.Inside(info, ref kind, ref sensitive);
                return null;
            }
        }

        public string ClickElement(string refId, MouseSpec mouse, CancelToken ct, out string summary)
        {
            summary = null;
            string err = Ensure(ct);
            if (err != null) return err;
            if (mouse == null) mouse = new MouseSpec();
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    ArmEvents(ct);
                    string r = NormRef(refId);
                    Dictionary<string, object> info;
                    err = Prep(r, true, ct, out info);
                    if (err != null) return err;
                    if (FileFields.Is(info)) return FileFields.NotThisWay(r);
                    string covered = Json.Str(info, "covered") ?? "";
                    string name = Json.Str(info, "name") ?? "";
                    if (covered.Length > 0)
                        return r + " is hidden behind another element (" + Clip(covered, 140) + "). Close or dismiss that first, or scroll.";
                    string before = currentUrl;
                    int start = navStarts;
                    double w = Json.Num(info, "w"), h = Json.Num(info, "h");
                    bool plain = mouse.Button == "left" && mouse.Clicks == 1 && mouse.HoldMs == 0 && mouse.CdpModifiers == 0;
                    bool captcha = Json.Bool(info, "captcha") || Json.Bool(info, "inCaptcha");
                    if (w < 1 || h < 1 || !Json.Bool(info, "inView"))
                    {
                        if (!plain) return r + " is not visible on the screen, so it can't be " + mouse.Describe().ToLowerInvariant() + ". Scroll to it first, or use a plain click.";
                        object v;
                        err = Js("(" + BrowserScripts.Click + ")(" + Json.Quote(r) + ")", ct, out v);
                        if (err != null) return err;
                        if (v as string == "stale") return r + " is no longer on the page. Call browser_read to get fresh refs.";
                    }
                    else
                    {
                        PointAt(Json.Num(info, "x"), Json.Num(info, "y"), true, false, ct);
                        err = Mouse(Json.Num(info, "x"), Json.Num(info, "y"), mouse, ct);
                        if (err != null) return err;
                    }
                    string clickedRef = mouse.Describe() + " " + r + (name.Length > 0 ? " (" + Clip(name, 60) + ")" : "") + ".";
                    if (captcha) return CaptchaAfter(start, before, true, false, clickedRef, ct, out summary);
                    return AfterAction(start, before, ct, clickedRef, out summary);
                }
                finally { Busy(false); }
            }
        }

        string ShotCheck(double x, double y)
        {
            if (shotScale <= 0) return "take a browser_screenshot first: x and y are pixel positions in that screenshot";
            if (shotNav != navStarts) return "the page changed since the last screenshot. Take a new browser_screenshot, then use positions from it";
            if (shotResized) return "the browser view changed size since the last screenshot (the console under the page opened or closed, or the window was resized). Take a new browser_screenshot, then use positions from it";
            if (x < 0 || y < 0 || x > shotW || y > shotH) return "that position is outside the screenshot, which is " + shotW + " x " + shotH + " pixels";
            return null;
        }

        public string PointInfo(double x, double y, CancelToken ct, out string kind, out bool sensitive)
        {
            kind = "";
            sensitive = false;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                err = ShotCheck(x, y);
                if (err != null) return err;
                return KindAt(PageX(x), PageY(y), ct, out kind, out sensitive);
            }
        }

        string KindAt(double cssX, double cssY, CancelToken ct, out string kind, out bool sensitive)
        {
            kind = "";
            sensitive = false;
            object v;
            string err = Js("(" + BrowserScripts.Point + ")(" + cssX.ToString("0.##", CultureInfo.InvariantCulture) + "," + cssY.ToString("0.##", CultureInfo.InvariantCulture) + ")", ct, out v);
            if (err != null)
            {
                if (userDialogOpen) return err;
                sensitive = true;
                kind = "a spot MinGent could not check";
                return null;
            }
            if (Json.Bool(v, "frame"))
            {
                string boxed = FrameKind.Of(Json.Str(v, "src"));
                if (boxed != null) { sensitive = true; kind = boxed; }
                else FrameKind.Inside(v, ref kind, ref sensitive);
                return null;
            }
            if (Json.Bool(v, "captcha"))
            {
                sensitive = true;
                kind = "a CAPTCHA box on this page";
                FrameKind.Inside(v, ref kind, ref sensitive);
                return null;
            }
            sensitive = Json.Bool(v, "sensitive");
            kind = Clip(Json.Str(v, "name") ?? "", 60);
            if (kind.Length == 0) kind = Clip((Json.Str(v, "tag") ?? "element").ToLowerInvariant(), 30);
            FrameKind.Inside(v, ref kind, ref sensitive);
            return null;
        }

        const int CaptchaGapMs = 350;

        public string CaptchaClick(List<double[]> points, MouseSpec mouse, CancelToken ct, out string summary, out bool submitted)
        {
            summary = null;
            submitted = false;
            if (points == null || points.Count == 0) return "no positions to click";
            string err = Ensure(ct);
            if (err != null) return err;
            if (mouse == null) mouse = new MouseSpec();
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    ArmEvents(ct);
                    string before = currentUrl, stopped = null;
                    int start = navStarts, clicked = 0;
                    bool slow = false;
                    for (int i = 0; i < points.Count; i++)
                    {
                        double x = points[i][0], y = points[i][1];
                        if (i > 0 && !ct.Sleep(CaptchaGapMs)) return "stopped by the user";
                        err = navStarts != start || loading ? "the page changed" : ShotCheck(x, y);
                        if (err != null)
                        {
                            if (i == 0) return err;
                            stopped = err;
                            break;
                        }
                        double px = PageX(x), py = PageY(y);
                        if (i > 0)
                        {
                            string kind;
                            bool sens;
                            if (KindAt(px, py, ct, out kind, out sens) != null || !kind.StartsWith("a CAPTCHA box", StringComparison.Ordinal))
                            {
                                stopped = Spot(x, y) + " was no longer inside the CAPTCHA";
                                break;
                            }
                        }
                        if (!slow && ButtonArea(px, py, ct)) slow = submitted = true;
                        PointAt(px, py, true, false, ct);
                        err = Mouse(px, py, mouse, ct);
                        if (err != null) return clicked > 0 ? "Clicked " + clicked + " of " + points.Count + " spots in the CAPTCHA, then: " + err : err;
                        clicked++;
                    }
                    string done = points.Count == 1 ? mouse.Describe() + " at " + Spot(points[0][0], points[0][1]) + " in the CAPTCHA." :
                                  clicked == points.Count ? "Clicked the " + clicked + " spots in the CAPTCHA, in order." :
                                  "Clicked " + clicked + " of the " + points.Count + " spots in the CAPTCHA, in order. Then " + stopped + ", so the rest were not clicked.";
                    return CaptchaAfter(start, before, slow, true, done, ct, out summary);
                }
                finally { Busy(false); }
            }
        }

        static string Spot(double x, double y)
        {
            return Math.Round(x).ToString(CultureInfo.InvariantCulture) + ", " + Math.Round(y).ToString(CultureInfo.InvariantCulture);
        }

        bool ButtonArea(double cssX, double cssY, CancelToken ct)
        {
            object v;
            if (Js("(" + BrowserScripts.CaptchaRect + ")()", ct, out v) != null || !Json.Bool(v, "found")) return true;
            double x = Json.Num(v, "x"), y = Json.Num(v, "y"), w = Json.Num(v, "w"), h = Json.Num(v, "h");
            if (cssX < x || cssX > x + w || cssY < y || cssY > y + h) return true;
            return cssY >= y + h - 90;
        }

        string CaptchaAfter(int start, string before, bool slow, bool lean, string done, CancelToken ct, out string summary)
        {
            summary = null;
            bool navigated;
            Stopwatch clock = Stopwatch.StartNew();
            bool still;
            castWanted = true;
            try
            {
                still = CaptchaSettle.Wait(slow,
                    delegate { return navStarts != start || loading; },
                    delegate { return userDialogOpen; },
                    delegate { return CaptchaPixels(ct); },
                    delegate (int ms) { return ct.Sleep(ms); },
                    delegate { return clock.Elapsed.TotalMilliseconds; },
                    out navigated);
            }
            finally { castWanted = false; StopCast(); }
            if (ct.IsCancelled) return "stopped by the user";
            if (!still) done += " The CAPTCHA was still changing after " + (CaptchaSettle.MaxMs / 1000) + " seconds.";
            string note = "";
            if (navigated)
            {
                string err = WaitNavigation(start, 0, 20000, ct, out note);
                if (err != null) return err;
            }
            else if (lean && !userDialogOpen && CaptchaOpen(ct))
            {
                summary = done + TakeEvents();
                return null;
            }
            return Report(before, done, note, false, ct, out summary);
        }

        string CaptchaPixels(CancelToken ct)
        {
            object v;
            if (Js("(" + BrowserScripts.CaptchaRect + ")()", ct, out v) != null || !(v is Dictionary<string, object>)) return null;
            double x = Json.Num(v, "x"), y = Json.Num(v, "y"), w = Json.Num(v, "w"), h = Json.Num(v, "h"), vw = Json.Num(v, "vw"), vh = Json.Num(v, "vh");
            double cx = Math.Max(0, x - 8), cy = Math.Max(0, y - 8);
            double cw = Math.Min(vw - cx, w + 16), ch = Math.Min(vh - cy, h + 16);
            if (cw < 8 || ch < 8) return null;
            string frame = castWanted ? CastFrame(vw, vh, cw, ch, Json.Num(v, "dpr"), ct) : null;
            if (frame == null)
            {
                object res;
                if (CdpCall("Page.captureScreenshot", Json.O("format", "jpeg", "quality", 50), ct, out res) != null) return null;
                frame = Json.Str(res, "data");
            }
            string seen = ViewCrop.Signature(frame, vw, cx, cy, cw, ch);
            if (seen == null) return null;
            return Math.Round(cx) + "," + Math.Round(cy) + "," + Math.Round(cw) + "," + Math.Round(ch) + ":" + seen;
        }

        readonly object castLock = new object();
        readonly List<object> castHold = new List<object>();
        string castData;
        volatile bool castOn, castWanted, castLive;
        volatile int castFails;

        void HookCast()
        {
            castHold.Clear();
            castFails = 0;
            try
            {
                IntPtr p;
                if (wv.GetDevToolsProtocolEventReceiver("Page.screencastFrame", out p) < 0 || p == IntPtr.Zero) return;
                ICoreWebView2DevToolsProtocolEventReceiver r;
                try { r = (ICoreWebView2DevToolsProtocolEventReceiver)Marshal.GetObjectForIUnknown(p); }
                finally { Marshal.Release(p); }
                WvCdpEvent h = new WvCdpEvent(OnCastFrame);
                long tok;
                if (r.add_DevToolsProtocolEventReceived(h, out tok) >= 0) castHold.AddRange(new object[] { r, h });
            }
            catch { }
        }

        void OnCastFrame(string json)
        {
            object p = Json.TryParse(json ?? "");
            if (p == null) return;
            Fire("Page.screencastFrameAck", "{\"sessionId\":" + ((long)Json.Num(p, "sessionId")).ToString(CultureInfo.InvariantCulture) + "}");
            if (!castOn) return;
            string data = Json.Str(p, "data");
            if (string.IsNullOrEmpty(data)) return;
            lock (castLock) castData = data;
        }

        string CastFrame(double vw, double vh, double cw, double ch, double dpr, CancelToken ct)
        {
            if (castHold.Count == 0 || castFails >= 3) return null;
            if (!castOn)
            {
                double s = Math.Max(1.0, dpr) * Math.Min(1.0, 240.0 / Math.Max(cw, ch));
                int mw = Math.Max(64, (int)Math.Ceiling(vw * s)), mh = Math.Max(64, (int)Math.Ceiling(vh * s));
                lock (castLock) castData = null;
                castOn = true;
                castLive = true;
                object res;
                if (CdpCall("Page.startScreencast", Json.O("format", "jpeg", "quality", 50, "maxWidth", mw, "maxHeight", mh, "everyNthFrame", 1), 3000, ct, out res) != null)
                {
                    castWanted = false;
                    StopCast();
                    return null;
                }
            }
            Stopwatch first = Stopwatch.StartNew();
            while (true)
            {
                lock (castLock) { if (castData != null) { castFails = 0; return castData; } }
                if (first.ElapsedMilliseconds > 300 || (ct != null && ct.IsCancelled))
                {
                    if (first.ElapsedMilliseconds > 300) castFails++;
                    castWanted = false;
                    StopCast();
                    return null;
                }
                Thread.Sleep(10);
            }
        }

        void StopCast()
        {
            castOn = false;
            if (!castLive) return;
            castLive = false;
            object res;
            CdpCall("Page.stopScreencast", Json.O(), 2000, null, out res);
            lock (castLock) castData = null;
        }

        bool CaptchaOpen(CancelToken ct)
        {
            object v;
            return Js("(" + BrowserScripts.CaptchaRect + ")()", ct, out v) == null && Json.Bool(v, "found");
        }

        public string ClickAt(double x, double y, MouseSpec mouse, CancelToken ct, out string summary)
        {
            summary = null;
            string err = Ensure(ct);
            if (err != null) return err;
            if (mouse == null) mouse = new MouseSpec();
            lock (opLock)
            {
                err = ShotCheck(x, y);
                if (err != null) return err;
                Reveal();
                Busy(true);
                try
                {
                    ArmEvents(ct);
                    string before = currentUrl;
                    int start = navStarts;
                    PointAt(PageX(x), PageY(y), true, false, ct);
                    err = Mouse(PageX(x), PageY(y), mouse, ct);
                    if (err != null) return err;
                    return AfterAction(start, before, ct, mouse.Describe() + " at " + Math.Round(x) + ", " + Math.Round(y) + " in the screenshot.", out summary);
                }
                finally { Busy(false); }
            }
        }

        string Point(string refId, double x, double y, CancelToken ct, out double px, out double py, out string label)
        {
            px = py = 0;
            label = "";
            if (!string.IsNullOrEmpty(refId))
            {
                string r = NormRef(refId);
                Dictionary<string, object> info;
                string err = Prep(r, true, ct, out info);
                if (err != null) return err;
                if (Json.Num(info, "w") < 1 || Json.Num(info, "h") < 1 || !Json.Bool(info, "inView")) return r + " is not visible on the screen. Scroll to it first.";
                px = Json.Num(info, "x");
                py = Json.Num(info, "y");
                string name = Json.Str(info, "name") ?? "";
                label = r + (name.Length > 0 ? " (" + Clip(name, 50) + ")" : "");
                return null;
            }
            string se = ShotCheck(x, y);
            if (se != null) return se;
            px = PageX(x);
            py = PageY(y);
            label = Math.Round(x) + ", " + Math.Round(y) + " in the screenshot";
            return null;
        }

        public string Hover(string refId, double x, double y, CancelToken ct, out string summary)
        {
            summary = null;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    ArmEvents(ct);
                    double px, py;
                    string label;
                    err = Point(refId, x, y, ct, out px, out py, out label);
                    if (err != null) return err;
                    string before = currentUrl;
                    int start = navStarts;
                    PointAt(px, py, false, false, ct);
                    err = MoveMouse(px, py, 0, ct);
                    if (err != null) return err;
                    ct.Sleep(450);
                    return AfterAction(start, before, ct, "Moved the mouse over " + label + ".", out summary);
                }
                finally { Busy(false); }
            }
        }

        public string Drag(string fromRef, double fromX, double fromY, string toRef, double toX, double toY, CancelToken ct, out string summary)
        {
            summary = null;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    ArmEvents(ct);
                    string before = currentUrl;
                    int start = navStarts;
                    double ax, ay, bx, by;
                    string from, to;
                    err = Point(fromRef, fromX, fromY, ct, out ax, out ay, out from);
                    if (err != null) return err;
                    bool captcha = false;
                    if (string.IsNullOrEmpty(fromRef))
                    {
                        string fk;
                        bool fs;
                        captcha = KindAt(ax, ay, ct, out fk, out fs) == null && fk.StartsWith("a CAPTCHA box", StringComparison.Ordinal);
                    }
                    else
                    {
                        Dictionary<string, object> fi;
                        captcha = Prep(NormRef(fromRef), false, ct, out fi) == null && Json.Bool(fi, "captcha");
                    }
                    if (!string.IsNullOrEmpty(fromRef))
                    {
                        Dictionary<string, object> info;
                        if (Prep(NormRef(fromRef), false, ct, out info) == null && Json.Bool(info, "draggable"))
                        {
                            err = Point(toRef, toX, toY, ct, out bx, out by, out to);
                            if (err != null) return err;
                            object v;
                            PointAt(ax, ay, true, true, ct);
                            System.Drawing.Point dropAt;
                            if (ScreenOf(bx, by, ct, out dropAt)) AgentCursor.PointAt(dropAt, false);
                            string target = string.IsNullOrEmpty(toRef) ? "null" : Json.Quote(NormRef(toRef));
                            err = Js("(" + BrowserScripts.DragDrop + ")(" + Json.Quote(NormRef(fromRef)) + "," + target + "," + bx.ToString("0.##", CultureInfo.InvariantCulture) + "," + by.ToString("0.##", CultureInfo.InvariantCulture) + ")", ct, out v);
                            if (err != null) return err;
                            string de = Json.Str(v, "err");
                            if (de == "stale") return (Json.Str(v, "which") ?? "that ref") + " is no longer on the page. Call browser_read to get fresh refs.";
                            if (de == "none") return "there is nothing at the drop position";
                            string how = Json.Bool(v, "dropped") ? "The page accepted the drop." : Json.Bool(v, "accepted") ? "The target accepted the drag but did not handle the drop." : "The target did not accept the drop.";
                            return AfterAction(start, before, ct, "Dragged " + from + " to " + to + " (drag and drop). " + how, out summary);
                        }
                    }
                    err = Point(toRef, toX, toY, ct, out bx, out by, out to);
                    if (err != null) return err;
                    object res;
                    PointAt(ax, ay, true, true, ct);
                    System.Drawing.Point endAt;
                    bool showEnd = ScreenOf(bx, by, ct, out endAt);
                    err = MoveMouse(ax, ay, 0, ct);
                    if (err != null) return err;
                    ct.Sleep(40);
                    err = CdpCall("Input.dispatchMouseEvent", Json.O("type", "mousePressed", "x", ax, "y", ay, "button", "left", "buttons", 1, "clickCount", 1), ct, out res);
                    if (err != null) return err;
                    if (showEnd) AgentCursor.PointAt(endAt, false);
                    ct.Sleep(120);
                    const int steps = 12;
                    for (int i = 1; i <= steps && err == null; i++)
                    {
                        err = MoveMouse(ax + (bx - ax) * i / steps, ay + (by - ay) * i / steps, 1, ct);
                        ct.Sleep(25);
                    }
                    ct.Sleep(120);
                    string up = CdpCall("Input.dispatchMouseEvent", Json.O("type", "mouseReleased", "x", bx, "y", by, "button", "left", "buttons", 0, "clickCount", 1), ct, out res);
                    if (err != null) return err;
                    if (up != null) return up;
                    if (captcha) return CaptchaAfter(start, before, true, string.IsNullOrEmpty(fromRef), "Dragged from " + from + " to " + to + ".", ct, out summary);
                    return AfterAction(start, before, ct, "Dragged from " + from + " to " + to + ".", out summary);
                }
                finally { Busy(false); }
            }
        }

        public string FocusedInfo(CancelToken ct, out string kind, out bool sensitive)
        {
            kind = "";
            sensitive = false;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                object v;
                err = Js("(" + BrowserScripts.Focused + ")()", ct, out v);
                if (err != null) return err;
                if (Json.Bool(v, "none")) return "nothing on the page has the keyboard focus. Click the field first, or give its ref.";
                if (Json.Bool(v, "frame")) { kind = "a field inside a frame from another site"; sensitive = true; return null; }
                kind = Json.Str(v, "kind") ?? "";
                sensitive = Json.Bool(v, "sensitive");
                FrameKind.Inside(v, ref kind, ref sensitive);
                return null;
            }
        }

        static bool PickerField(string type)
        {
            switch (type ?? "")
            {
                case "date": case "time": case "datetime-local": case "month": case "week": case "color": case "range": return true;
                default: return false;
            }
        }

        public string TypeText(string refId, string text, bool submit, bool clear, CancelToken ct, out string summary)
        {
            summary = null;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    ArmEvents(ct);
                    string before = currentUrl;
                    int start = navStarts;
                    object v;
                    if (string.IsNullOrEmpty(refId))
                    {
                        if (!string.IsNullOrEmpty(text))
                        {
                            err = InsertText(text, ct);
                            if (err != null) return err;
                        }
                        if (submit)
                        {
                            ct.Sleep(60);
                            err = Key("Enter", ct);
                            if (err != null) return err;
                        }
                        return AfterAction(start, before, ct, "Typed " + (text ?? "").Length + " characters where the keyboard focus was" + (submit ? " and pressed Enter." : "."), out summary);
                    }
                    string r = NormRef(refId);
                    Dictionary<string, object> info;
                    err = Prep(r, true, ct, out info);
                    if (err != null) return err;
                    if (FileFields.Is(info)) return FileFields.NotThisWay(r);
                    if (Json.Bool(info, "select"))
                    {
                        err = Js("(" + BrowserScripts.Select + ")(" + Json.Quote(r) + "," + Json.Quote(text ?? "") + ")", ct, out v);
                        if (err != null) return err;
                        string se = Json.Str(v, "err");
                        if (se == "none")
                        {
                            List<object> opts = Json.List(v, "options");
                            List<string> names = new List<string>();
                            if (opts != null) foreach (object o in opts) names.Add(Clip(o as string ?? "", 60));
                            return "no option of " + r + " matches \"" + text + "\". The options are: " + string.Join(" | ", names.ToArray());
                        }
                        if (se == "disabled") return "the option \"" + Clip(Json.Str(v, "text"), 60) + "\" of " + r + " is disabled";
                        if (se == "stale") return r + " is no longer on the page. Call browser_read to get fresh refs.";
                        return AfterAction(start, before, ct, "Selected \"" + (Json.Str(v, "text") ?? text) + "\" in " + r + ".", out summary);
                    }
                    string ftype = Json.Str(info, "type") ?? "";
                    if (Json.Str(info, "tag") == "INPUT" && PickerField(ftype) && !string.IsNullOrEmpty(text))
                    {
                        err = Js("(" + BrowserScripts.SetField + ")(" + Json.Quote(r) + "," + Json.Quote(text) + ")", ct, out v);
                        if (err != null) return err;
                        string fe = Json.Str(v, "err");
                        if (fe == "stale") return r + " is no longer on the page. Call browser_read to get fresh refs.";
                        if (fe == "format") return "\"" + Clip(text, 40) + "\" doesn't fit " + r + ", a " + ftype + " field: give it as " + Json.Str(v, "want") + ".";
                        if (fe == null)
                        {
                            if (submit)
                            {
                                ct.Sleep(60);
                                err = Key("Enter", ct);
                                if (err != null) return err;
                            }
                            return AfterAction(start, before, ct, "Set " + r + " to " + (Json.Str(v, "value") ?? text) + (submit ? " and pressed Enter." : "."), out summary);
                        }
                    }
                    if (!Json.Bool(info, "editable"))
                    {
                        string tag = Json.Str(info, "tag") ?? "";
                        if (tag != "DIV" && tag != "SPAN" && tag != "CANVAS") return r + " is not a text field (it is a " + Clip(tag.ToLowerInvariant(), 30) + "). Click it instead, or pick a field from browser_read.";
                    }
                    if (Json.Bool(info, "inView") && Json.Num(info, "w") >= 1 && (Json.Str(info, "covered") ?? "").Length == 0)
                    {
                        err = Mouse(Json.Num(info, "x"), Json.Num(info, "y"), ct);
                        if (err != null) return err;
                        ct.Sleep(80);
                    }
                    err = Js("(" + BrowserScripts.Focus + ")(" + Json.Quote(r) + "," + (clear ? "true" : "false") + ")", ct, out v);
                    if (err != null) return err;
                    if (Json.Str(v, "err") == "stale") return r + " is no longer on the page. Call browser_read to get fresh refs.";
                    if (!Json.Bool(v, "ok") && Json.Bool(info, "sensitive")) return "the cursor did not land in " + r + " (the page moved it elsewhere), so nothing was typed. Call browser_read and try again.";
                    if (!Json.Bool(info, "sensitive") && !string.IsNullOrEmpty(text))
                    {
                        object fv;
                        if (Js("(" + BrowserScripts.Focused + ")()", ct, out fv) == null && !Json.Bool(fv, "none"))
                        {
                            string fk = Json.Str(fv, "kind") ?? "";
                            bool fs = Json.Bool(fv, "sensitive") || Json.Bool(fv, "frame");
                            FrameKind.Inside(fv, ref fk, ref fs);
                            if (fs) return "the click on " + r + " put the cursor in " + (Json.Bool(fv, "frame") ? "a field inside a frame from another site" : fk.Length > 0 ? (fk.StartsWith("a ", StringComparison.Ordinal) ? fk : "a " + fk + " field") : "a sensitive field") + ", so nothing was typed. Type into that field by its own ref from browser_read, so MinGent can ask the user first.";
                        }
                    }
                    if (!string.IsNullOrEmpty(text))
                    {
                        err = InsertText(text, ct);
                        if (err != null) return err;
                    }
                    else if (clear)
                    {
                        err = Key("Backspace", ct);
                        if (err != null) return err;
                    }
                    if (submit)
                    {
                        ct.Sleep(60);
                        err = Key("Enter", ct);
                        if (err != null) return err;
                    }
                    string what = string.IsNullOrEmpty(text) ? "Cleared " + r : "Typed " + text.Length + " characters into " + r;
                    return AfterAction(start, before, ct, what + (submit ? " and pressed Enter." : "."), out summary);
                }
                finally { Busy(false); }
            }
        }


        public string PressKey(List<KeyPress> keys, int holdMs, CancelToken ct, out string summary)
        {
            summary = null;
            if (keys == null || keys.Count == 0) return "no key given";
            foreach (KeyPress k in keys)
            {
                string why = null;
                foreach (KeyPress part in KeyCombo.Parts(k)) if (why == null) why = Blocked(part);
                if (why != null) return why;
            }
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Busy(true);
                try
                {
                    ArmEvents(ct);
                    string before = currentUrl;
                    int start = navStarts;
                    for (int i = 0; i < keys.Count; i++)
                    {
                        if (ct.IsCancelled) return "stopped by the user";
                        if (userDialogOpen) break;
                        err = SendKey(keys[i], holdMs, ct);
                        if (err != null) return err;
                        if (i + 1 < keys.Count) ct.Sleep(keys[i].Code == "Enter" || keys[i].Code == "Tab" ? 120 : 35);
                    }
                    return AfterAction(start, before, ct, KeyCombo.Describe(keys, holdMs) + ".", out summary);
                }
                finally { Busy(false); }
            }
        }

        public string Scroll(string direction, string refId, double x, double y, int pixels, CancelToken ct, out string summary)
        {
            summary = null;
            string err = Ensure(ct);
            if (err != null) return err;
            string dir = (direction ?? "down").Trim().ToLowerInvariant();
            if (dir != "up" && dir != "left" && dir != "right") dir = "down";
            bool at = !string.IsNullOrEmpty(refId) || x >= 0 && y >= 0;
            lock (opLock)
            {
                Busy(true);
                try
                {
                    object v;
                    if (at)
                    {
                        double px, py;
                        string label;
                        err = Point(refId, x, y, ct, out px, out py, out label);
                        if (err != null) return err;
                        int amount = pixels > 0 ? Math.Min(20000, pixels) : 400;
                        double dx = dir == "left" ? -amount : dir == "right" ? amount : 0, dy = dir == "up" ? -amount : dir == "down" ? amount : 0;
                        PointAt(px, py, false, false, ct);
                        err = MoveMouse(px, py, 0, ct);
                        if (err != null) return err;
                        object res;
                        err = CdpCall("Input.dispatchMouseEvent", Json.O("type", "mouseWheel", "x", px, "y", py, "deltaX", dx, "deltaY", dy), ct, out res);
                        if (err != null) return err;
                        ct.Sleep(400);
                        summary = "Scrolled " + dir + " by " + amount + " pixels at " + label + ". Use browser_read to see new content, or browser_screenshot to see the screen.";
                        return null;
                    }
                    if (pixels <= 0 && (dir == "up" || dir == "down")) err = Js("(" + BrowserScripts.Scroll + ")(" + Json.Quote(dir) + ")", ct, out v);
                    else err = Js("(" + BrowserScripts.ScrollBy + ")(" + Json.Quote(dir) + "," + Math.Max(0, Math.Min(20000, pixels)).ToString(CultureInfo.InvariantCulture) + ")", ct, out v);
                    if (err != null) return err;
                    ct.Sleep(400);
                    bool moved = Json.Bool(v, "moved");
                    bool sideways = dir == "left" || dir == "right";
                    summary = moved ? "Scrolled " + dir + "; now " + Json.Int(v, "at") + "% of the way " + (sideways ? "across" : "down") + ". Use browser_read to see new content, or browser_screenshot to see the screen."
                                    : "The page did not scroll " + dir + " (already at the " + (dir == "up" ? "top" : dir == "down" ? "bottom" : dir == "left" ? "left edge" : "right edge") + ").";
                    return null;
                }
                finally { Busy(false); }
            }
        }

        public string Screenshot(CancelToken ct, out ImageData image, out int width, out int height)
        {
            image = null;
            width = height = 0;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    object pv;
                    err = Js("(" + BrowserScripts.Probe + ")()", ct, out pv);
                    if (err != null) return err;
                    double cssW = Json.Num(pv, "w");
                    object res;
                    err = CdpCall("Page.captureScreenshot", Json.O("format", "jpeg", "quality", 82), ct, out res);
                    if (err != null) return err;
                    string data = Json.Str(res, "data");
                    if (string.IsNullOrEmpty(data)) return "the browser returned no screenshot";
                    try
                    {
                        using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(data)))
                        using (Image img = Image.FromStream(ms))
                        {
                            const int maxSide = 1366;
                            double sc = Math.Min(1.0, (double)maxSide / Math.Max(img.Width, img.Height));
                            width = Math.Max(1, (int)(img.Width * sc));
                            height = Math.Max(1, (int)(img.Height * sc));
                            image = ImageTools.Encode(img, maxSide);
                        }
                    }
                    catch (Exception ex) { return "could not read the screenshot: " + ex.Message; }
                    shotScale = cssW > 0 ? cssW / width : 0;
                    shotX0 = shotY0 = 0;
                    shotNav = navStarts;
                    shotResized = false;
                    shotW = width;
                    shotH = height;
                    return null;
                }
                finally { Busy(false); }
            }
        }

        public string Upload(string refId, int field, List<string> files, CancelToken ct, out string summary)
        {
            summary = null;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    ArmEvents(ct);
                    string before = currentUrl;
                    int start = navStarts;
                    string r = string.IsNullOrEmpty(refId) ? "" : NormRef(refId);
                    object v;
                    err = Js("(" + BrowserScripts.FileField + ")(" + Json.Quote(r) + "," + field.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")", ct, out v);
                    if (err != null) return err;
                    string bad = Json.Str(v, "err");
                    if (bad != null) return FileFields.Problem(bad, r, v);
                    if (Json.Bool(v, "folder")) return "this file field takes a whole folder, which only the user can choose. Ask the user to do it in the browser pane.";
                    if (files.Count > 1 && !Json.Bool(v, "multiple")) return "this file field takes one file, and you gave " + files.Count + ". Give one file.";
                    object res;
                    err = CdpCall("Runtime.evaluate", Json.O("expression", "globalThis.__jpUpload", "contextId", worldId, "returnByValue", false, "silent", true), ct, out res);
                    if (err != null) return "the page changed while the file was being given to it; call browser_read and try again";
                    string handle = Json.Str(res, "result", "objectId");
                    if (string.IsNullOrEmpty(handle)) return "the file field is no longer on the page. Call browser_read and try again.";
                    List<object> list = new List<object>();
                    foreach (string f in files) list.Add(f);
                    err = CdpCall("DOM.setFileInputFiles", Json.O("files", list, "objectId", handle), ct, out res);
                    if (err != null) return "the page did not take the file (" + Clip(err, 160) + ")";
                    ct.Sleep(150);
                    string fieldName = Json.Str(v, "name") ?? "";
                    object got;
                    Js(BrowserScripts.FileTaken, ct, out got);
                    List<object> names = Json.List(got, "names");
                    List<string> shown = new List<string>();
                    if (names != null) foreach (object n in names) shown.Add(Clip(n as string ?? "", 80));
                    if (shown.Count == 0) foreach (string f in files) shown.Add(Clip(VideoFrames.NameOf(new Uri(f).AbsoluteUri), 80));
                    return AfterAction(start, before, ct, "Gave " + string.Join(", ", shown.ToArray()) + " to the file field" + (fieldName.Length > 0 ? " \"" + Clip(fieldName, 60) + "\"" : "") +
                        ". The page has " + (shown.Count == 1 ? "the file" : "the files") + " now. If it does not send " + (shown.Count == 1 ? "it" : "them") + " by itself, use its upload or submit button.", out summary);
                }
                finally { Busy(false); }
            }
        }

        public string VideoFrame(double seconds, CancelToken ct, out Bitmap frame, out VideoShot shot)
        {
            frame = null;
            shot = null;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    object v;
                    err = Js("(" + BrowserScripts.VideoFrame + ")(" + (seconds < 0 ? "-1" : seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)) + ")", ct, true, out v);
                    if (err != null) return err;
                    if (!(v is Dictionary<string, object>)) return "the page gave no answer about its video";
                    string bad = Json.Str(v, "err");
                    if (bad != null) return VideoFrames.Problem(bad, (int)Json.Num(v, "frames"));
                    shot = new VideoShot();
                    shot.Time = Json.Num(v, "t");
                    shot.Duration = Json.Num(v, "d");
                    shot.Width = (int)Json.Num(v, "vw");
                    shot.Height = (int)Json.Num(v, "vh");
                    shot.Title = Json.Str(v, "title") ?? "";
                    if (seconds < 0) return null;
                    shotScale = 0;
                    object res;
                    err = CdpCall("Page.captureScreenshot", Json.O("format", "jpeg", "quality", 85), ct, out res);
                    if (err != null) return err;
                    string data = Json.Str(res, "data");
                    if (string.IsNullOrEmpty(data)) return "the browser returned no picture of the video";
                    try
                    {
                        using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(data)))
                        using (Image img = Image.FromStream(ms))
                        {
                            RectangleF src = ViewCrop.Source(img.Width, img.Height, Json.Num(v, "iw"), Json.Num(v, "x"), Json.Num(v, "y"), Json.Num(v, "w"), Json.Num(v, "h"));
                            if (src.Width < 16 || src.Height < 16) return "the video is not visible on the page";
                            Bitmap b = new Bitmap((int)src.Width, (int)src.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                            using (Graphics g = Graphics.FromImage(b))
                                g.DrawImage(img, new Rectangle(0, 0, b.Width, b.Height), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
                            frame = b;
                        }
                    }
                    catch (Exception ex) { frame = null; return "could not read the picture of the video: " + ex.Message; }
                    return null;
                }
                finally { Busy(false); }
            }
        }

        static ImageData ShotOf(Image img, out int width, out int height)
        {
            const int maxSide = 1366;
            double sc = Math.Min(1.0, (double)maxSide / Math.Max(img.Width, img.Height));
            width = Math.Max(1, (int)(img.Width * sc));
            height = Math.Max(1, (int)(img.Height * sc));
            return ImageTools.Encode(img, maxSide);
        }

        public string CaptchaShot(CancelToken ct, out ImageData image, out int width, out int height)
        {
            image = null;
            width = height = 0;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                try
                {
                    object v;
                    err = Js("(" + BrowserScripts.CaptchaRect + ")()", ct, out v);
                    if (err != null) return err;
                    if (!(v is Dictionary<string, object>) || !Json.Bool(v, "found")) { captchaName = ""; return null; }
                    captchaName = Json.Str(v, "name") ?? "";
                    captchaTyped = Json.Bool(v, "answer");
                    if (Json.Str(v, "kind") != "frame") return null;
                    double x = Json.Num(v, "x"), y = Json.Num(v, "y"), w = Json.Num(v, "w"), h = Json.Num(v, "h"), vw = Json.Num(v, "vw"), vh = Json.Num(v, "vh");
                    const double pad = 12;
                    double cx = Math.Max(0, x - pad), cy = Math.Max(0, y - pad);
                    double cw = Math.Min(vw - cx, w + 2 * pad), ch = Math.Min(vh - cy, h + 2 * pad);
                    if (cw < 60 || ch < 60) return null;
                    double zoom = Math.Max(1.0, Math.Min(2.0, 1100.0 / Math.Max(cw, ch)));
                    double dpr = Json.Num(v, "dpr"), detail = Math.Min(zoom * Math.Max(1.0, dpr), 1366.0 / Math.Max(cw, ch));
                    object res;
                    Image sharp = null;
                    try
                    {
                        if (dpr >= detail - 0.01)
                        {
                            err = CdpCall("Page.captureScreenshot", Json.O("format", "jpeg", "quality", 88), ct, out res);
                            if (err != null) return err;
                            sharp = ViewCrop.Zoom(Json.Str(res, "data"), vw, cx, cy, cw, ch, (int)Math.Round(cw * detail), (int)Math.Round(ch * detail));
                        }
                        if (sharp != null) image = ShotOf(sharp, out width, out height);
                    }
                    catch (Exception ex) { image = null; return "could not read the screenshot: " + ex.Message; }
                    finally { if (sharp != null) sharp.Dispose(); }
                    if (image == null)
                    {
                        err = CdpCall("Page.captureScreenshot", Json.O("format", "jpeg", "quality", 88,
                            "clip", Json.O("x", cx + Json.Num(v, "sx"), "y", cy + Json.Num(v, "sy"), "width", cw, "height", ch, "scale", zoom)), ct, out res);
                        if (err != null) return err;
                        string data = Json.Str(res, "data");
                        if (string.IsNullOrEmpty(data)) return "the browser returned no screenshot";
                        try
                        {
                            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(data)))
                            using (Image img = Image.FromStream(ms))
                                image = ShotOf(img, out width, out height);
                        }
                        catch (Exception ex) { image = null; return "could not read the screenshot: " + ex.Message; }
                    }
                    shotScale = cw / width;
                    shotX0 = cx;
                    shotY0 = cy;
                    shotNav = navStarts;
                    shotResized = false;
                    shotW = width;
                    shotH = height;
                    return null;
                }
                finally { Busy(false); }
            }
        }

        volatile string captchaName = "";
        volatile bool captchaTyped;

        public string CaptchaName { get { return captchaName; } }

        public bool CaptchaTyped { get { return captchaTyped; } }

        public bool CaptchaShowing(CancelToken ct)
        {
            if (!ready || wv == null) return false;
            lock (opLock)
            {
                return CaptchaOpen(ct);
            }
        }

        public string CaptchaCloseUp(CancelToken ct, out ImageData image, out int width, out int height)
        {
            image = null;
            width = height = 0;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                object v;
                err = Js("(" + BrowserScripts.CaptchaRect + ")()", ct, out v);
                if (err != null) return err;
                if (!(v is Dictionary<string, object>) || !Json.Bool(v, "found") || Json.Str(v, "kind") != "page") return null;
                captchaName = Json.Str(v, "name") ?? "";
                captchaTyped = Json.Bool(v, "answer");
                double x = Json.Num(v, "x"), y = Json.Num(v, "y"), w = Json.Num(v, "w"), h = Json.Num(v, "h"), vw = Json.Num(v, "vw"), vh = Json.Num(v, "vh");
                const double pad = 6;
                double cx = Math.Max(0, x - pad), cy = Math.Max(0, y - pad);
                double cw = Math.Min(vw - cx, w + 2 * pad), ch = Math.Min(vh - cy, h + 2 * pad);
                if (cw < 20 || ch < 12) return null;
                double zoom = Math.Max(1.0, Math.Min(3.0, 800.0 / Math.Max(cw, ch)));
                object res;
                err = CdpCall("Page.captureScreenshot", Json.O("format", "jpeg", "quality", 92), ct, out res);
                if (err != null) return err;
                try
                {
                    using (Bitmap near = ViewCrop.Zoom(Json.Str(res, "data"), vw, cx, cy, cw, ch, (int)Math.Round(cw * zoom), (int)Math.Round(ch * zoom)))
                        if (near != null) image = ShotOf(near, out width, out height);
                }
                catch (Exception ex) { image = null; return "could not read the screenshot: " + ex.Message; }
                return null;
            }
        }

        readonly ConsoleLog consoleLog = new ConsoleLog();
        readonly ConsoleDrawer drawer = new ConsoleDrawer();
        readonly List<object> consoleHold = new List<object>();
        bool drawerOpen, drawerUser;
        int drawerHeight;
        volatile bool consoleOn, countsQueued, commitEvents;
        int userRuns;
        volatile bool agentRunning;
        volatile string agentSite = "", userSite = "";
        public event Action<int> ConsoleHeightChanged;

        public int ConsoleHeight
        {
            get { return drawerHeight; }
            set { drawerHeight = Math.Max(0, value); if (drawerOpen) PerformLayout(); }
        }

        public bool ConsoleOpen { get { return drawerOpen; } }

        public static bool ThisPc(string url)
        {
            if (string.IsNullOrEmpty(url)) return true;
            string low = url.ToLowerInvariant();
            if (low.StartsWith("about:") || low.StartsWith("data:") || low.StartsWith("file:")) return true;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            string h = u.Host.ToLowerInvariant().Trim('[', ']');
            if (h == "localhost" || h.EndsWith(".localhost", StringComparison.Ordinal) || h == "::1") return true;
            string[] parts = h.Split('.');
            int a;
            return parts.Length == 4 && int.TryParse(parts[0], out a) && a == 127;
        }

        static bool SameHost(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static string PlaceName(string url)
        {
            Uri u;
            if (Uri.TryCreate(url ?? "", UriKind.Absolute, out u))
            {
                if (u.IsFile) return u.Segments.Length > 0 ? Uri.UnescapeDataString(u.Segments[u.Segments.Length - 1]) : "this page";
                if (u.Scheme == "http" || u.Scheme == "https") return u.IsDefaultPort ? u.Host : u.Host + ":" + u.Port.ToString(CultureInfo.InvariantCulture);
            }
            return "this page";
        }

        void HookConsole()
        {
            consoleHold.Clear();
            foreach (string ev in new string[] { "Runtime.consoleAPICalled", "Runtime.exceptionThrown", "Log.entryAdded", "Runtime.inspectRequested" })
            {
                string name = ev;
                try
                {
                    IntPtr p;
                    if (wv.GetDevToolsProtocolEventReceiver(name, out p) < 0 || p == IntPtr.Zero) continue;
                    ICoreWebView2DevToolsProtocolEventReceiver r;
                    try { r = (ICoreWebView2DevToolsProtocolEventReceiver)Marshal.GetObjectForIUnknown(p); }
                    finally { Marshal.Release(p); }
                    WvCdpEvent h = new WvCdpEvent(delegate (string json) { OnConsoleEvent(name, json); });
                    long tok;
                    if (r.add_DevToolsProtocolEventReceived(h, out tok) >= 0) consoleHold.AddRange(new object[] { r, h });
                }
                catch { }
            }
            consoleOn = false;
            UpdateCapture();
        }

        void OnConsoleEvent(string method, string json)
        {
            object p = Json.TryParse(json ?? "");
            if (p == null) return;
            if (method == "Runtime.inspectRequested") { CopyRequested(p); return; }
            ConsoleEntry e = method == "Runtime.consoleAPICalled" ? ConsoleFormat.FromApiCall(p) : method == "Runtime.exceptionThrown" ? ConsoleFormat.FromException(p) : ConsoleFormat.FromLog(p);
            if (e == null) return;
            double stamp = method == "Log.entryAdded" ? Json.Num(p, "entry", "timestamp") : Json.Num(p, "timestamp");
            consoleLog.Add(e, method + "|" + stamp.ToString("R", CultureInfo.InvariantCulture) + "|" + e.Kind + "|" + e.Where + "|" + e.Text);
        }

        bool WantConsole(string url)
        {
            if (ThisPc(url)) return true;
            if (drawerOpen && drawerUser) return true;
            string h = HostOf(url);
            return SameHost(h, agentSite) || SameHost(h, userSite);
        }

        void UpdateCapture() { SetCapture(WantConsole(currentUrl)); }

        void SetCapture(bool on)
        {
            if (wv != null && on != consoleOn)
            {
                consoleOn = on;
                Fire(on ? "Runtime.enable" : "Runtime.disable", "{}");
                Fire(on ? "Log.enable" : "Log.disable", "{}");
            }
            UpdateDrawerNotice();
        }

        void UpdateDrawerNotice()
        {
            bool off = !consoleOn && !ThisPc(currentUrl);
            drawer.Notice = drawerOpen && off ? "Not reading this site's console." : null;
            drawer.View.EmptyText = off ? "MinGent reads the console of websites only when you or the agent ask, since some sites turn away browsers that do. Pages on this PC are always read."
                                        : "Nothing logged yet.";
            drawer.View.Invalidate();
        }

        void OnConsoleChanged()
        {
            if (countsQueued || IsDisposed || !IsHandleCreated) return;
            countsQueued = true;
            try { BeginInvoke(new Action(delegate { countsQueued = false; UpdateCounts(); })); }
            catch { countsQueued = false; }
        }

        void UpdateCounts()
        {
            int errors, warnings;
            consoleLog.Counts(out errors, out warnings);
            drawer.SetCounts(errors, warnings);
        }

        public void ShowConsole(bool open, bool byUser)
        {
            if (open == drawerOpen)
            {
                if (open && byUser)
                {
                    drawerUser = true;
                    UpdateCapture();
                    drawer.FocusInput();
                }
                return;
            }
            drawerOpen = open;
            drawerUser = open && byUser;
            bool focused = drawer.ContainsFocus;
            drawer.Visible = open;
            PerformLayout();
            UpdateCapture();
            if (open)
            {
                UpdateCounts();
                drawer.View.ScrollToEnd();
                if (byUser) drawer.FocusInput();
            }
            else if (focused && ctl != null) { try { ctl.MoveFocus(0); } catch { } }
        }

        void ShowMoreMenu()
        {
            ContextMenuStrip m = Menus.Create(th);
            m.ShowCheckMargin = true;
            m.Items.Add(Menus.Detail("Console", "Ctrl+Shift+J", drawerOpen, delegate { ShowConsole(!drawerOpen, true); }));
            ToolStripMenuItem dev = Menus.Detail("Developer tools", "F12", false, delegate { OpenDevTools(); });
            dev.Enabled = wv != null;
            m.Items.Add(dev);
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(bMore, new Point(bMore.Width, bMore.Height + UI.S(2)), ToolStripDropDownDirection.BelowLeft);
        }

        public void OpenDevTools()
        {
            if (wv == null) return;
            try { wv.OpenDevToolsWindow(); } catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Shift | Keys.J)) { ShowConsole(!drawerOpen, true); return true; }
            if (keyData == Keys.F12 || keyData == (Keys.Control | Keys.Shift | Keys.I)) { OpenDevTools(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static Dictionary<string, object> EvalArgs(string code, bool user)
        {
            return Json.O("expression", code, "replMode", true, "awaitPromise", true, "returnByValue", false, "generatePreview", true, "userGesture", user,
                          "includeCommandLineAPI", true, "objectGroup", user ? "mingent-user" : "mingent", "silent", true, "timeout", 20000.0);
        }

        void CopyRequested(object p)
        {
            if (userRuns <= 0 || agentRunning || !Json.Bool(p, "hints", "copyToClipboard")) return;
            Dictionary<string, object> o = Json.Get(p, "object") as Dictionary<string, object>;
            if (o == null) return;
            string type = Json.Str(o, "type") ?? "", id = Json.Str(o, "objectId");
            object v;
            if (type == "string" || (o.TryGetValue("value", out v) && id == null)) { PutOnClipboard(type == "string" ? Json.Str(o, "value") : Json.Write(Json.Get(o, "value"))); return; }
            if (string.IsNullOrEmpty(id) || wv == null) { PutOnClipboard(Json.Str(o, "description") ?? type); return; }
            WvScriptDone d = new WvScriptDone(delegate (int hr, string res)
            {
                object r = Json.TryParse(res ?? "");
                PutOnClipboard(hr >= 0 ? Json.Str(r, "result", "value") ?? Json.Str(o, "description") : Json.Str(o, "description"));
            });
            keep.Add(d);
            try
            {
                wv.CallDevToolsProtocolMethod("Runtime.callFunctionOn", Json.Write(Json.O("objectId", id, "returnByValue", true, "silent", true,
                    "functionDeclaration", "function () { try { return typeof this === 'object' ? JSON.stringify(this, null, 2) : String(this); } catch (e) { return String(this); } }")), d);
            }
            catch { }
        }

        void PutOnClipboard(string text)
        {
            if (text == null) return;
            try
            {
                Clipboard.SetText(text.Length == 0 ? " " : text);
                consoleLog.Add("note", "Copied to the clipboard.", false);
            }
            catch (Exception ex) { consoleLog.Add("fault", "Could not copy to the clipboard: " + ex.Message, false); }
        }

        void UserRun(string code)
        {
            consoleLog.Add("input", code, false);
            if (wv == null) { consoleLog.Add("fault", Available ? "The browser hasn't started yet. Open a page, then try again." : "The built-in browser isn't available on this PC.", false); return; }
            if (!ThisPc(currentUrl)) userSite = HostOf(currentUrl);
            drawerUser = true;
            UpdateCapture();
            userRuns++;
            WvScriptDone d = new WvScriptDone(delegate (int hr, string json)
            {
                userRuns = Math.Max(0, userRuns - 1);
                object res = Json.TryParse(json ?? "");
                Dictionary<string, object> obj = res as Dictionary<string, object>;
                if (hr < 0 || obj == null || (!obj.ContainsKey("result") && obj.ContainsKey("message")))
                {
                    consoleLog.Add("fault", CdpError("Running the code", hr, json), false);
                    return;
                }
                bool failed;
                string v = ConsoleFormat.Result(res, out failed);
                consoleLog.Add(failed ? "fault" : "result", v, false);
                Fire("Runtime.releaseObjectGroup", Json.Write(Json.O("objectGroup", "mingent-user")));
            });
            keep.Add(d);
            if (keep.Count > 400) keep.RemoveRange(0, 100);
            try
            {
                if (wv.CallDevToolsProtocolMethod("Runtime.evaluate", Json.Write(EvalArgs(code, true)), d) < 0) { userRuns = Math.Max(0, userRuns - 1); consoleLog.Add("fault", "The page did not run the code.", false); }
            }
            catch (Exception ex) { userRuns = Math.Max(0, userRuns - 1); consoleLog.Add("fault", "The page did not run the code: " + ex.Message, false); }
        }

        bool AgentWantsConsole(CancelToken ct)
        {
            bool fresh = false;
            string ignored;
            Ui(delegate (Pending p)
            {
                if (!ThisPc(currentUrl)) agentSite = HostOf(currentUrl);
                bool was = consoleOn;
                UpdateCapture();
                fresh = !was && consoleOn;
                p.Ok(null);
            }, 5000, ct, out ignored);
            return fresh;
        }

        public string ConsoleRead(CancelToken ct, out string text)
        {
            text = null;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                if (AgentWantsConsole(ct)) ct.Sleep(400);
                if (!consoleOn) return "the console of this page can't be read right now";
                text = consoleLog.ForAgent(PlaceName(currentUrl), 9000);
                return null;
            }
        }

        public string ConsoleRun(string code, string checkedUrl, CancelToken ct, out string result)
        {
            result = null;
            string err = Ensure(ct);
            if (err != null) return err;
            lock (opLock)
            {
                Reveal();
                Busy(true);
                agentRunning = true;
                try
                {
                    if (AgentWantsConsole(ct)) ct.Sleep(300);
                    OpenForAgent();
                    consoleLog.Add("input", code, true);
                    long mark = consoleLog.Last;
                    DateTime start = DateTime.UtcNow;
                    string json;
                    err = Ui(delegate (Pending p)
                    {
                        string moved = Moved(checkedUrl ?? "");
                        if (moved != null) { p.Fail(moved); return; }
                        Cdp("Runtime.evaluate", Json.Write(EvalArgs(code, false)), p);
                    }, 30000, ct, out json);
                    if (err != null)
                    {
                        string why = err == "the browser did not respond in time" ? "the code was still running after 30 seconds, maybe waiting for something that never happens, so MinGent stopped waiting for it"
                                   : (DateTime.UtcNow - start).TotalSeconds >= 19 && err.IndexOf("Internal error", StringComparison.OrdinalIgnoreCase) >= 0 ? "the code ran for 20 seconds without finishing, so it was stopped"
                                   : err;
                        consoleLog.Add("fault", why, true);
                        if (!err.StartsWith("the browser moved", StringComparison.Ordinal))
                        {
                            string busy = StopIfBusy();
                            if (busy != null) why += ". " + busy;
                        }
                        return why;
                    }
                    try { BeginInvoke(new Action(delegate { Fire("Runtime.releaseObjectGroup", Json.Write(Json.O("objectGroup", "mingent"))); })); } catch { }
                    bool failed;
                    string value = ConsoleFormat.Result(Json.TryParse(json ?? "{}"), out failed);
                    ct.Sleep(80);
                    string stuck = StopIfBusy();
                    List<ConsoleEntry> during = consoleLog.Since(mark);
                    consoleLog.Add(failed ? "fault" : "result", value, true);
                    if (stuck != null) consoleLog.Add("note", stuck, true);
                    consoleLog.SkipNews();
                    result = RunReport(value, failed, during);
                    if (stuck != null) result += "\n\n" + stuck;
                    return null;
                }
                finally { agentRunning = false; Busy(false); }
            }
        }

        string Moved(string was) { return MovedFrom(was, currentUrl, loading ? goingUrl : ""); }

        public static string MovedFrom(string was, string current, string going)
        {
            List<string> now = new List<string>();
            now.Add(current ?? "");
            if (!string.IsNullOrEmpty(going)) now.Add(going);
            foreach (string n in now)
            {
                string u = n.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? "" : n;
                if (ThisPc(u) != ThisPc(was) || PlaceName(u) != PlaceName(was))
                    return "the browser moved to " + PlaceName(u) + " after the code was checked, so the code did not run. Look at the page again, then run the code if it still makes sense there";
            }
            return null;
        }

        string StopIfBusy()
        {
            CancelToken own = new CancelToken();
            object res;
            string err = CdpCall("Runtime.evaluate", Json.O("expression", "0", "returnByValue", true, "silent", true), 2500, own, out res);
            if (err != "the browser did not respond in time") return null;
            try { BeginInvoke(new Action(delegate { Fire("Runtime.terminateExecution", "{}"); })); } catch { return null; }
            err = CdpCall("Runtime.evaluate", Json.O("expression", "0", "returnByValue", true, "silent", true), 3000, own, out res);
            return err == null ? "The page stayed busy running script, probably a loop that never ends, so MinGent stopped that script. The page may need a reload (browser_navigate)."
                               : "The page is not responding: a script in it never finishes. Reload it with browser_navigate.";
        }

        public static string RunReport(string value, bool failed, List<ConsoleEntry> during)
        {
            StringBuilder sb = new StringBuilder();
            string v = value ?? "";
            if (v.Length > 8000) v = v.Substring(0, 8000) + "\n\u2026 (cut: the value is " + v.Length.ToString(CultureInfo.InvariantCulture) + " characters long; return less of it, for example with .slice(0, 2000))";
            sb.Append(failed ? "The code threw: " : "Result: ").Append(v);
            if (!failed && v.IndexOf('\u2026') >= 0 && (v.EndsWith("}", StringComparison.Ordinal) || v.EndsWith("]", StringComparison.Ordinal)))
                sb.Append("\n(This is a shortened preview. For all of it, return JSON.stringify(value, null, 2).)");
            List<string> lines = new List<string>();
            int used = 0, more = 0;
            foreach (ConsoleEntry e in during ?? new List<ConsoleEntry>())
            {
                if (!e.IsMessage) continue;
                string l = ConsoleLog.Line(e);
                if (lines.Count >= 30 || used + l.Length > 4000) { more++; continue; }
                lines.Add(l);
                used += l.Length + 1;
            }
            if (lines.Count > 0)
            {
                sb.Append("\n\nConsole output while it ran:\n").Append(string.Join("\n", lines.ToArray()));
                if (more > 0) sb.Append("\n(").Append(more.ToString(CultureInfo.InvariantCulture)).Append(" more messages left out)");
            }
            return sb.ToString();
        }

        void OpenForAgent()
        {
            try { BeginInvoke(new Action(delegate { if (Visible) ShowConsole(true, false); else drawer.View.ScrollToEnd(); })); } catch { }
        }

        public void ConsoleRefused(string code, string why, bool show)
        {
            consoleLog.Add("input", code, true);
            consoleLog.Add("refused", why, true);
            if (show) OpenForAgent();
        }

        public string ConsoleNews()
        {
            string url = currentUrl;
            if (!consoleOn || !(ThisPc(url) || SameHost(HostOf(url), agentSite))) { consoleLog.SkipNews(); return null; }
            return consoleLog.News();
        }
    }

    static class FileFields
    {
        public static bool Is(Dictionary<string, object> info)
        {
            return Json.Str(info, "tag") == "INPUT" && string.Equals(Json.Str(info, "type"), "file", StringComparison.OrdinalIgnoreCase);
        }

        public static string NotThisWay(string r)
        {
            return r + " is a file field. Clicking it opens a window that only the user can use, and nothing can be typed into it. To give it a file, call browser_upload with this ref and the file's path.";
        }

        public static string Problem(string code, string r, object v)
        {
            switch (code)
            {
                case "stale": return r + " is no longer on the page. Call browser_read to get fresh refs.";
                case "disabled": return "the file field is switched off on this page right now, so it takes no file";
                case "nofield":
                    {
                        int count = Json.Int(v, "count");
                        return count == 0 ? "this page has no file field" : "this page has " + count + (count == 1 ? " file field" : " file fields") + ", so field goes from 1 to " + count;
                    }
                case "none":
                    return "there is no file field on this page" + (Json.Int(v, "frames") > 0 ? ". The page has frames from other sites, and a file field inside such a frame can't be reached from here" : "") +
                           ". Some pages only make their file field at the moment their button is clicked, and that can't be filled in from here. Ask the user to choose the file in the browser pane.";
                case "many":
                    {
                        StringBuilder sb = new StringBuilder("this page has several file fields and it is not clear which one you mean. Call browser_upload again with field set to one of these numbers:");
                        List<object> fields = Json.List(v, "fields");
                        if (fields != null)
                            foreach (object f in fields)
                            {
                                string name = Json.Str(f, "name") ?? "", accept = Json.Str(f, "accept") ?? "";
                                sb.Append("\n").Append(Json.Int(f, "n")).Append(": ").Append(name.Length > 0 ? "\"" + name + "\"" : "(no name)");
                                if (accept.Length > 0) sb.Append(", takes ").Append(accept);
                            }
                        return sb.ToString();
                    }
            }
            return "the file field could not be used (" + code + ")";
        }
    }

    static class ViewCrop
    {
        public static RectangleF Source(int imageW, int imageH, double viewW, double x, double y, double w, double h)
        {
            double r = viewW > 0 ? imageW / viewW : 1.0;
            double left = Math.Max(0, Math.Min(imageW, x * r)), top = Math.Max(0, Math.Min(imageH, y * r));
            double right = Math.Max(left, Math.Min(imageW, (x + w) * r)), bottom = Math.Max(top, Math.Min(imageH, (y + h) * r));
            return new RectangleF((float)left, (float)top, (float)(right - left), (float)(bottom - top));
        }

        public static string Signature(string jpeg, double viewW, double x, double y, double w, double h)
        {
            if (string.IsNullOrEmpty(jpeg)) return null;
            try
            {
                using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(jpeg)))
                using (Image img = Image.FromStream(ms, false, false))
                {
                    RectangleF src = Source(img.Width, img.Height, viewW, x, y, w, h);
                    if (src.Width < 2 || src.Height < 2) return null;
                    double s = Math.Min(1.0, 240.0 / Math.Max(src.Width, src.Height));
                    int sw = Math.Max(1, (int)Math.Round(src.Width * s)), sh = Math.Max(1, (int)Math.Round(src.Height * s));
                    using (Bitmap small = new Bitmap(sw, sh, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                    {
                        using (Graphics g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                            g.DrawImage(img, new Rectangle(0, 0, sw, sh), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
                        }
                        return Hash(small);
                    }
                }
            }
            catch (Exception) { return null; }
        }

        public static Bitmap Zoom(string jpeg, double viewW, double x, double y, double w, double h, int outW, int outH)
        {
            if (string.IsNullOrEmpty(jpeg) || outW < 1 || outH < 1) return null;
            try
            {
                using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(jpeg)))
                using (Image img = Image.FromStream(ms, false, false))
                {
                    RectangleF src = Source(img.Width, img.Height, viewW, x, y, w, h);
                    if (src.Width < 2 || src.Height < 2) return null;
                    Bitmap bm = new Bitmap(outW, outH, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    try
                    {
                        using (Graphics g = Graphics.FromImage(bm))
                        using (System.Drawing.Imaging.ImageAttributes edges = new System.Drawing.Imaging.ImageAttributes())
                        {
                            edges.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.DrawImage(img, new Rectangle(0, 0, outW, outH), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, edges);
                        }
                        return bm;
                    }
                    catch (Exception) { bm.Dispose(); throw; }
                }
            }
            catch (Exception) { return null; }
        }

        static string Hash(Bitmap bm)
        {
            System.Drawing.Imaging.BitmapData d = bm.LockBits(new Rectangle(0, 0, bm.Width, bm.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                int row = bm.Width * 3;
                byte[] line = new byte[row];
                ulong hash = 14695981039346656037UL;
                for (int y = 0; y < bm.Height; y++)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), line, 0, row);
                    for (int i = 0; i < row; i++) { hash ^= line[i]; hash *= 1099511628211UL; }
                }
                return bm.Width.ToString(CultureInfo.InvariantCulture) + "x" + bm.Height.ToString(CultureInfo.InvariantCulture) + ":" + hash.ToString("x16", CultureInfo.InvariantCulture);
            }
            finally { bm.UnlockBits(d); }
        }
    }

    static class CaptchaSettle
    {
        public const int MaxMs = 4000, StillMs = 400, PollMs = 100, QuietMs = 2500;

        public static int Least(bool slow) { return slow ? 1200 : 600; }

        public static bool Wait(bool slow, Func<bool> navigation, Func<bool> interrupted, Func<string> look, Func<int, bool> sleep, Func<double> clock, out bool navigated)
        {
            navigated = false;
            double began = clock(), changed = began;
            string first = null, last = null;
            bool seen = false, moved = false;
            while (true)
            {
                if (navigation()) { navigated = true; return true; }
                if (interrupted()) return true;
                string now = look();
                double t = clock(), gone = t - began;
                if (!seen) { first = now; last = now; seen = true; changed = t; }
                else if (now != last) { last = now; changed = t; moved = moved || now != first; }
                bool settled = gone >= Least(slow) && t - changed >= StillMs;
                if (settled && (!slow || moved)) return true;
                if (slow && !moved && gone >= QuietMs) return true;
                if (gone >= MaxMs) return false;
                if (!sleep(PollMs)) return true;
            }
        }
    }

    static class FrameKind
    {
        static readonly Regex Pay = new Regex(
            @"^https?://([^/?#]+\.)?(paypal\.com|paypalobjects\.com|stripe\.com|stripe\.network|braintreegateway\.com|braintree-api\.com|adyen\.com|adyenpayments\.com|checkout\.com|klarna\.com|klarnaservices\.com|squareup\.com|afterpay\.com|affirm\.com|razorpay\.com|mollie\.com|worldpay\.com|authorize\.net)([/?#:]|$)|^https?://(pay|payments)\.google\.com([/?#:]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex Captcha = new Regex(
            @"^https?://([^/?#]+\.)?(hcaptcha\.com|arkoselabs\.com|funcaptcha\.com|recaptcha\.net|captcha-delivery\.com|px-cloud\.net|px-cdn\.net|perimeterx\.net|geetest\.com|awswaf\.com)([/?#:]|$)|^https?://challenges\.cloudflare\.com([/?#:]|$)|^https?://(www\.)?google\.com/recaptcha/|^https?://([^/?#]+\.)?[a-z0-9-]*captcha[a-z0-9-]*\.[^/?#]+([/?#:]|$)|^https?://([^/?#]+\.)?arkoselabs\.[^/?#]+([/?#:]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Of(string src)
        {
            if (string.IsNullOrEmpty(src)) return null;
            if (Pay.IsMatch(src)) return "a payment box from " + Host(src);
            if (Captcha.IsMatch(src)) return "a CAPTCHA box from " + Host(src);
            return null;
        }

        public static void Inside(object info, ref string kind, ref bool sensitive)
        {
            List<object> via = Json.List(info, "via");
            if (via == null) return;
            string found = null;
            foreach (object o in via)
            {
                string k = Of(o as string);
                if (k != null && (found == null || k.StartsWith("a payment", StringComparison.Ordinal))) found = k;
            }
            if (found == null) return;
            kind = found;
            sensitive = true;
        }

        static string Host(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host : "";
        }
    }
}
