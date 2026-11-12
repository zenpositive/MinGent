using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("MinGent")]
[assembly: AssemblyDescription("MinGent - an AI agent for Windows with its own notepad and browser")]
[assembly: AssemblyProduct("MinGent")]
[assembly: AssemblyCopyright("Built locally from source")]
[assembly: AssemblyVersion("1.1.5.0")]
[assembly: AssemblyFileVersion("1.1.5.0")]
[assembly: AssemblyInformationalVersion("1.1.5 beta")]

namespace MinGent
{
    static class Program
    {
        public const string AppName = "MinGent", LegacyName = "JustPad";
        public static string IpcTitle = "MinGent.IPC.Window.v1";
        public static string DataDir, SessionDir;
        public static bool Elevated, Primary = true;
        public static volatile bool Erased, KeepErrorLog;
        public static MainForm MainWindow;

        [STAThread]
        static void Main(string[] args)
        {
            Native.SafeDllSearch();
            if (args.Length == 2 && args[0] == Eraser.FinishArg)
            {
                Eraser.Finish(args[1]);
                return;
            }
            if (args.Length >= 1 && args[0] == Updater.FinishArg)
            {
                Updater.Finish(args);
                return;
            }
            if (args.Length == 2 && args[0] == Updater.CheckArg)
            {
                Environment.Exit(Updater.CheckOwnRelease(args[1]));
                return;
            }
            Native.TryDpiAware();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DataDir = MoveOldData(Path.Combine(local, LegacyName), Path.Combine(local, AppName));
            Elevated = RunsElevated();
            SessionDir = Elevated ? Path.Combine(DataDir, "admin") : DataDir;
            if (Elevated) IpcTitle += ".admin";

            bool primary = true;
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(false, InstanceMutexName(Elevated));
                primary = Owns(mutex, 0);
            }
            catch { primary = false; }

            if (!primary)
            {
                if (Native.SendToRunningInstance(args)) return;
                primary = mutex != null && Owns(mutex, 5000);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { ReportError(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                LogError(e.ExceptionObject as Exception);
                try
                {
                    MainForm w = MainWindow;
                    if (w != null && w.IsHandleCreated && !w.IsDisposed)
                    {
                        if (w.InvokeRequired)
                        {
                            IAsyncResult ar = w.BeginInvoke(new Action(delegate { try { w.EmergencySave(); } catch { } }));
                            ar.AsyncWaitHandle.WaitOne(3000);
                        }
                        else w.EmergencySave();
                    }
                }
                catch { }
            };

            if (primary)
            {
                try { BrowserData.FinishPendingWipe(); } catch { }
                try { BrowserData.FinishTemporary(); } catch { }
                try { Shell.SweepScripts(); } catch { }
            }
            UI.Init();
            Primary = primary;
            MainWindow = new MainForm(args, primary);
            Application.Run(MainWindow);
            GC.KeepAlive(mutex);
            Thread guard = new Thread(delegate ()
            {
                Thread.Sleep(5000);
                try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
            });
            guard.IsBackground = true;
            guard.Start();
        }

        public static bool RunsElevated()
        {
            try
            {
                using (System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static string InstanceMutexName(bool elevated)
        {
            return "Local\\MinGent.SingleInstance." + Environment.UserName + (elevated ? ".admin" : "");
        }

        static string MoveOldData(string old, string fresh)
        {
            try
            {
                if (Directory.Exists(fresh) || !Directory.Exists(old) || !Eraser.LooksOurs(old)) return fresh;
                Directory.Move(old, fresh);
            }
            catch { return Directory.Exists(fresh) ? fresh : old; }
            try
            {
                string ini = Path.Combine(fresh, "settings.ini");
                if (File.Exists(ini))
                {
                    string text = File.ReadAllText(ini, Encoding.UTF8);
                    string moved = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(old.TrimEnd('\\') + "\\"),
                        (fresh.TrimEnd('\\') + "\\").Replace("$", "$$"), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (moved != text) File.WriteAllText(ini, moved, new UTF8Encoding(false));
                }
            }
            catch { }
            return fresh;
        }

        static bool Owns(Mutex m, int wait)
        {
            try { return m.WaitOne(wait); }
            catch (AbandonedMutexException) { return true; }
            catch { return false; }
        }

        static string ErrorLog { get { return Path.Combine(DataDir, "error.log"); } }

        static void LogError(Exception ex)
        {
            if (ex == null || !KeepErrorLog || Erased) return;
            try
            {
                Directory.CreateDirectory(DataDir);
                FileInfo fi = new FileInfo(ErrorLog);
                if (fi.Exists && fi.Length > 4L * 1024 * 1024) SecureFile.Erase(ErrorLog);
                File.AppendAllText(ErrorLog,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\r\n\r\n");
            }
            catch { }
        }

        static int reporting;
        static DateTime lastReport = DateTime.MinValue, lastSave = DateTime.MinValue;
        static string lastMessage;
        static int repeats;

        static void ReportError(Exception ex)
        {
            if (repeats < 200) LogError(ex);
            repeats++;
            if ((DateTime.UtcNow - lastSave).TotalSeconds > 5)
            {
                lastSave = DateTime.UtcNow;
                try { if (MainWindow != null) MainWindow.EmergencySave(); } catch { }
            }
            if (Interlocked.Exchange(ref reporting, 1) == 1) return;
            try
            {
                string msg = ex.Message;
                if (msg == lastMessage && (DateTime.UtcNow - lastReport).TotalSeconds < 60) return;
                lastMessage = msg;
                MessageBox.Show("Something went wrong, but your tabs are safe.\n\n" + msg +
                    (KeepErrorLog ? "\n\nDetails were written to:\n" + ErrorLog : "\n\nTo keep the details for a bug report, turn on the error log in Settings > Privacy."),
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                lastReport = DateTime.UtcNow;
            }
            finally { Interlocked.Exchange(ref reporting, 0); }
        }
    }

    static class UI
    {
        public static float Scale = 1f;
        public static Font Font;

        public static void Init()
        {
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = Math.Max(1f, g.DpiX / 96f); }
            catch { Scale = 1f; }
            try { Font = SystemFonts.MessageBoxFont; } catch { Font = null; }
            if (Font == null) Font = new Font("Segoe UI", 9f);
        }

        public static int S(int v) { return (int)Math.Round(v * Scale); }
    }

    static class Native
    {
        public const int WM_COPYDATA = 0x004A;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_SYSCOLORCHANGE = 0x0015;
        public const int WM_THEMECHANGED = 0x031A;
        public const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
        public const int WM_NCACTIVATE = 0x0086;
        public const int WM_MOUSEHWHEEL = 0x020E;
        public const int WM_IME_STARTCOMPOSITION = 0x010D;
        public const int WM_IME_COMPOSITION = 0x010F;
        public const int IpcMagic = 0x4A504144;

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct COMPOSITIONFORM { public int dwStyle; public POINT ptCurrentPos; public RECT rcArea; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string cls, string title);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam, int flags, int timeout, out IntPtr result);
        [DllImport("user32.dll")]
        static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);
        [DllImport("user32.dll")]
        static extern bool GetUpdateRect(IntPtr hWnd, ref RECT rect, bool erase);
        [DllImport("user32.dll")]
        static extern bool HideCaret(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool ShowCaret(IntPtr hWnd);
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
        [DllImport("user32.dll")]
        static extern bool SystemParametersInfo(int action, int param, ref int value, int winIni);
        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

        public static void AssociationsChanged()
        {
            try { if (Environment.OSVersion.Platform == PlatformID.Win32NT) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
        }
        [DllImport("imm32.dll")]
        static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")]
        static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll")]
        static extern bool ImmSetCompositionWindow(IntPtr hIMC, ref COMPOSITIONFORM form);

        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        public static void TryDpiAware()
        {
            try { SetProcessDPIAware(); } catch { }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetDllDirectoryW(string path);

        public static void SafeDllSearch()
        {
            try { if (Environment.OSVersion.Platform == PlatformID.Win32NT) SetDllDirectoryW(""); } catch { }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

        public static bool ElevatedByChoice()
        {
            try
            {
                using (System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    int type, got;
                    return GetTokenInformation(id.Token, 18, out type, 4, out got) && type == 2;
                }
            }
            catch { return false; }
        }

        public static string CSharpCompiler()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(win)) return null;
            foreach (string d in new string[] { "Framework64", "Framework" })
            {
                string p = Path.Combine(Path.Combine(Path.Combine(Path.Combine(win, "Microsoft.NET"), d), "v4.0.30319"), "csc.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static string WindowsExe(string name)
        {
            string dir = name == "explorer.exe" ? Environment.GetFolderPath(Environment.SpecialFolder.Windows) : Environment.GetFolderPath(Environment.SpecialFolder.System);
            return string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
        }

        static int ColorRef(Color c)
        {
            return c.IsEmpty ? unchecked((int)0xFFFFFFFF)  : (c.R | (c.G << 8) | (c.B << 16));
        }

        [DllImport("user32.dll")]
        static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, int flags);

        public static bool Cloak(Form f, bool on)
        {
            try
            {
                if (!f.IsHandleCreated || Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
                int v = on ? 1 : 0;
                return DwmSetWindowAttribute(f.Handle, 13, ref v, 4) == 0;
            }
            catch { return false; }
        }

        public static void PaintNow(Control c)
        {
            try { if (c.IsHandleCreated) RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80 | 0x100); }
            catch { }
        }

        public static void SetTitleBar(Form f, Theme t)
        {
            try
            {
                if (t == null || !f.IsHandleCreated) return;
                int v = t.Dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
                int caption = ColorRef(t.Caption), text = ColorRef(t.CaptionText), border = ColorRef(t.WindowBorder);
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(f.Handle, 36, ref text, 4);
                DwmSetWindowAttribute(f.Handle, 34, ref border, 4);
                if (f.Visible)
                {
                    bool active = Form.ActiveForm == f;
                    SendMessage(f.Handle, WM_NCACTIVATE, (IntPtr)(active ? 0 : 1), IntPtr.Zero);
                    SendMessage(f.Handle, WM_NCACTIVATE, (IntPtr)(active ? 1 : 0), IntPtr.Zero);
                }
            }
            catch { }
        }

        static int roundState;

        public static bool PopupsRounded { get { return roundState == 1; } }

        public static bool RoundPopup(IntPtr hwnd, Color border)
        {
            if (roundState == 2 || hwnd == IntPtr.Zero) return false;
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT || Environment.OSVersion.Version.Build < 22000) { roundState = 2; return false; }
                int pref = 3;
                if (DwmSetWindowAttribute(hwnd, 33, ref pref, 4) != 0) { roundState = 2; return false; }
                if (!border.IsEmpty)
                {
                    int c = ColorRef(border);
                    DwmSetWindowAttribute(hwnd, 34, ref c, 4);
                }
                roundState = 1;
                return true;
            }
            catch { roundState = 2; return false; }
        }

        public static bool Animations
        {
            get
            {
                try
                {
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT) return true;
                    int v = 1;
                    if (SystemParametersInfo(0x1042, 0, ref v, 0)) return v != 0;
                }
                catch { }
                return true;
            }
        }

        public static void SetScrollbarTheme(Control c, Theme t)
        {
            try
            {
                if (t == null || !c.IsHandleCreated || Environment.OSVersion.Platform != PlatformID.Win32NT) return;
                if (t.Dark && !t.HighContrast) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                else SetWindowTheme(c.Handle, null, null);
            }
            catch { }
        }

        public static int LineCount(TextBoxBase tb)
        {
            try
            {
                if (tb == null || !tb.IsHandleCreated || Environment.OSVersion.Platform != PlatformID.Win32NT) return 0;
                return (int)SendMessage(tb.Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero);
            }
            catch { return 0; }
        }

        public static void SetCueBanner(TextBox tb, string text)
        {
            try { SendMessage(tb.Handle, 0x1501 , (IntPtr)1, text); } catch { }
        }

        public static Point EditTextOrigin(TextBox tb)
        {
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT && tb.IsHandleCreated)
                {
                    RECT r = new RECT();
                    SendMessage(tb.Handle, 0x00B2, IntPtr.Zero, ref r);
                    if (r.Left >= 0 && r.Top >= 0 && r.Left < tb.ClientSize.Width && r.Top < Math.Max(1, tb.ClientSize.Height)) return new Point(r.Left, r.Top);
                }
            }
            catch { }
            return new Point(1, 0);
        }

        public static bool HideCaretIn(Control c)
        {
            try { return Environment.OSVersion.Platform == PlatformID.Win32NT && c.IsHandleCreated && HideCaret(c.Handle); }
            catch { return false; }
        }

        public static void ShowCaretIn(Control c)
        {
            try { ShowCaret(c.Handle); } catch { }
        }

        public static Rectangle UpdateArea(Control c)
        {
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT && c.IsHandleCreated)
                {
                    RECT r = new RECT();
                    return GetUpdateRect(c.Handle, ref r, false) ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty;
                }
            }
            catch { }
            return c.ClientRectangle;
        }

        public static void SetImePosition(Control c, int x, int y)
        {
            try
            {
                IntPtr himc = ImmGetContext(c.Handle);
                if (himc == IntPtr.Zero) return;
                COMPOSITIONFORM cf = new COMPOSITIONFORM();
                cf.dwStyle = 0x0002;
                cf.ptCurrentPos.X = x;
                cf.ptCurrentPos.Y = y;
                ImmSetCompositionWindow(himc, ref cf);
                ImmReleaseContext(c.Handle, himc);
            }
            catch { }
        }

        public static void BringToFront(Form f)
        {
            try
            {
                if (f.WindowState == FormWindowState.Minimized)
                {
                    try { ShowWindow(f.Handle, 9); } catch { }
                    if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
                }
                f.Activate();
                SetForegroundWindow(f.Handle);
            }
            catch { }
        }

        public static void Foreground(IntPtr hwnd)
        {
            try { if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd); } catch { }
        }

        public static bool SendToRunningInstance(string[] args)
        {
            try
            {
                IntPtr hwnd = IntPtr.Zero;
                for (int i = 0; i < 40 && hwnd == IntPtr.Zero; i++)
                {
                    hwnd = FindWindowEx(HWND_MESSAGE, IntPtr.Zero, null, Program.IpcTitle);
                    if (hwnd == IntPtr.Zero) Thread.Sleep(100);
                }
                if (hwnd == IntPtr.Zero) return false;

                StringBuilder sb = new StringBuilder();
                foreach (string a in args)
                {
                    if (string.IsNullOrEmpty(a)) continue;
                    try { sb.Append(Path.GetFullPath(a)).Append('\n'); } catch { }
                }
                int pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid != 0) AllowSetForegroundWindow(pid);

                string payload = sb.ToString();
                IntPtr mem = Marshal.StringToHGlobalUni(payload);
                try
                {
                    COPYDATASTRUCT cds = new COPYDATASTRUCT();
                    cds.dwData = (IntPtr)IpcMagic;
                    cds.cbData = (payload.Length + 1) * 2;
                    cds.lpData = mem;
                    IntPtr result;
                    IntPtr ok = SendMessageTimeout(hwnd, WM_COPYDATA, IntPtr.Zero, ref cds, 0x0002 , 5000, out result);
                    return ok != IntPtr.Zero && result != IntPtr.Zero;
                }
                finally { Marshal.FreeHGlobal(mem); }
            }
            catch { return false; }
        }
    }

    static class FileTypes
    {
        sealed class Names
        {
            public string ProgId, AppKey, CapKey, AppExe, Added, Existed, Friendly;
            public Names(string name)
            {
                ProgId = name + ".Document";
                AppKey = "Software\\" + name;
                CapKey = AppKey + "\\Capabilities";
                AppExe = "Applications\\" + name + ".exe";
                Added = name + "Added";
                Existed = name + "Existed";
                Friendly = name;
            }
        }

        static readonly Names Current = new Names(Program.AppName), Legacy = new Names(Program.LegacyName);
        public static readonly string ProgId = Current.ProgId;
        const string Classes = "Software\\Classes\\";
        const string Registered = "Software\\RegisteredApplications";

        public static readonly string[] Exts = {
            ".txt", ".text", ".log", ".md", ".markdown", ".json", ".jsonc", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
            ".csv", ".tsv", ".properties", ".gitignore", ".editorconfig", ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".py", ".js", ".mjs",
            ".ts", ".tsx", ".jsx", ".css", ".scss", ".html", ".htm", ".c", ".h", ".cpp", ".hpp", ".cs", ".java", ".kt", ".go", ".rs",
            ".rb", ".php", ".sql", ".lua", ".swift", ".dart", ".vue", ".diff", ".patch", ".srt", ".nfo", ".tex", ".rst", ".cmake", ".gradle" };

        static string Exe { get { return Application.ExecutablePath; } }
        static string Command { get { return "\"" + Exe + "\" \"%1\""; } }

        static string CurrentCommand(Names n)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Classes + n.ProgId + "\\shell\\open\\command"))
                    return k == null ? null : k.GetValue("") as string;
            }
            catch { return null; }
        }

        public static bool IsRegistered { get { return CurrentCommand(Current) != null; } }

        public static string Register()
        {
            Names n = Current;
            try
            {
                string icon = "\"" + Exe + "\",0";
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Classes + n.ProgId))
                {
                    k.SetValue("", "Text document");
                    using (RegistryKey d = k.CreateSubKey("DefaultIcon")) d.SetValue("", "%SystemRoot%\\System32\\imageres.dll,-102", RegistryValueKind.ExpandString);
                    using (RegistryKey c = k.CreateSubKey("shell\\open\\command")) c.SetValue("", Command);
                }
                bool existed, mine = false;
                using (RegistryKey test = Registry.CurrentUser.OpenSubKey(Classes + n.AppExe))
                {
                    existed = test != null;
                    if (test != null) mine = test.GetValue(n.Added) != null;
                }
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Classes + n.AppExe))
                {
                    if (!existed) k.SetValue(n.Added, "1");
                    else if (!mine && k.GetValue(n.Existed) == null) k.SetValue(n.Existed, "1");
                    k.SetValue("FriendlyAppName", n.Friendly);
                    using (RegistryKey d = k.CreateSubKey("DefaultIcon")) d.SetValue("", icon);
                    using (RegistryKey c = k.CreateSubKey("shell\\open\\command")) c.SetValue("", Command);
                    using (RegistryKey t = k.CreateSubKey("SupportedTypes")) foreach (string e in Exts) t.SetValue(e, "");
                }
                using (RegistryKey cap = Registry.CurrentUser.CreateSubKey(n.CapKey))
                {
                    cap.SetValue("ApplicationName", n.Friendly);
                    cap.SetValue("ApplicationDescription", "AI agent for Windows with its own notepad and browser");
                    cap.SetValue("ApplicationIcon", icon);
                    using (RegistryKey fa = cap.CreateSubKey("FileAssociations")) foreach (string e in Exts) fa.SetValue(e, n.ProgId);
                }
                using (RegistryKey r = Registry.CurrentUser.CreateSubKey(Registered)) r.SetValue(n.Friendly, n.CapKey);
                foreach (string e in Exts)
                    using (RegistryKey o = Registry.CurrentUser.CreateSubKey(Classes + e + "\\OpenWithProgids"))
                        o.SetValue(n.ProgId, "");
                Native.AssociationsChanged();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static string Unregister()
        {
            string err = Remove(Current);
            Native.AssociationsChanged();
            return err;
        }

        public static void RemoveAll()
        {
            Remove(Current);
            if (LegacyOurs()) Remove(Legacy);
            Native.AssociationsChanged();
        }

        static string Remove(Names n)
        {
            string err = null;
            try
            {
                foreach (string e in Exts)
                {
                    try
                    {
                        using (RegistryKey o = Registry.CurrentUser.OpenSubKey(Classes + e + "\\OpenWithProgids", true))
                        {
                            if (o == null) continue;
                            if (o.GetValue(n.ProgId) != null || Array.IndexOf(o.GetValueNames(), n.ProgId) >= 0) o.DeleteValue(n.ProgId, false);
                        }
                    }
                    catch (Exception ex) { err = ex.Message; }
                }
                try { Registry.CurrentUser.DeleteSubKeyTree(Classes + n.ProgId, false); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    bool ours;
                    using (RegistryKey app = Registry.CurrentUser.OpenSubKey(Classes + n.AppExe, true))
                    {
                        ours = app != null && (app.GetValue(n.Added) != null ||
                               (app.GetValue(n.Existed) == null && string.Equals(app.GetValue("FriendlyAppName") as string, n.Friendly, StringComparison.Ordinal)));
                        if (app != null && !ours)
                        {
                            app.DeleteValue("FriendlyAppName", false);
                            app.DeleteValue(n.Existed, false);
                            app.DeleteSubKeyTree("SupportedTypes", false);
                        }
                    }
                    if (ours) Registry.CurrentUser.DeleteSubKeyTree(Classes + n.AppExe, false);
                }
                catch (Exception ex) { err = ex.Message; }
                try { using (RegistryKey r = Registry.CurrentUser.OpenSubKey(Registered, true)) if (r != null) r.DeleteValue(n.Friendly, false); } catch (Exception ex) { err = ex.Message; }
                try { Registry.CurrentUser.DeleteSubKeyTree(n.CapKey, false); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    using (RegistryKey a = Registry.CurrentUser.OpenSubKey(n.AppKey))
                        if (a != null && a.SubKeyCount == 0 && a.ValueCount == 0) { a.Close(); Registry.CurrentUser.DeleteSubKey(n.AppKey, false); }
                }
                catch { }
            }
            catch (Exception ex) { err = ex.Message; }
            return err;
        }

        static bool LegacyOurs()
        {
            string cmd = CurrentCommand(Legacy);
            if (cmd == null) return false;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(cmd, "^\"([^\"]+)\"");
            if (!m.Success) return false;
            try
            {
                string exe = m.Groups[1].Value;
                string file = Path.GetFileName(exe);
                if (!string.Equals(file, Program.LegacyName + ".exe", StringComparison.OrdinalIgnoreCase) && !string.Equals(file, Program.AppName + ".exe", StringComparison.OrdinalIgnoreCase)) return false;
                string dir = Path.GetDirectoryName(exe);
                return File.Exists(Path.Combine(dir, "Build-" + Program.LegacyName + ".bat")) || File.Exists(Path.Combine(dir, "Build-" + Program.AppName + ".bat"));
            }
            catch { return false; }
        }

        public static void UpdateIfMoved()
        {
            if (LegacyOurs())
            {
                Remove(Legacy);
                Register();
                return;
            }
            string cur = CurrentCommand(Current);
            if (cur == null || string.Equals(cur, Command, StringComparison.OrdinalIgnoreCase)) return;
            Register();
        }

        public static void OpenDefaultApps()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(Current.Friendly)) { UseShellExecute = true }); }
            catch
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); } catch { }
            }
        }
    }

    class IpcWindow : NativeWindow
    {
        readonly MainForm form;

        public IpcWindow(MainForm f)
        {
            form = f;
            CreateParams cp = new CreateParams();
            cp.Caption = Program.IpcTitle;
            cp.Parent = new IntPtr(-3);
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_COPYDATA)
            {
                try
                {
                    Native.COPYDATASTRUCT cds = (Native.COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(Native.COPYDATASTRUCT));
                    if (cds.dwData == (IntPtr)Native.IpcMagic)
                    {
                        string data = (cds.cbData > 0 && cds.lpData != IntPtr.Zero) ? Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2) : "";
                        string[] files = data.Split(new char[] { '\n', '\0' }, StringSplitOptions.RemoveEmptyEntries);
                        form.BeginInvoke(new Action<string[]>(form.OpenFromOtherInstance), new object[] { files });
                        m.Result = (IntPtr)1;
                        return;
                    }
                }
                catch { }
            }
            base.WndProc(ref m);
        }
    }
}
