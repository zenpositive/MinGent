using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MinGent
{
    class TerminalState
    {
        readonly StringBuilder line = new StringBuilder();
        public bool Known = true, Clicked;
        public string Why = "";
        public readonly List<string> Hosts = new List<string>();
        public bool Selection;
        public DateTime Opened = DateTime.UtcNow, Since = DateTime.UtcNow;
        public int Pid;
        public string Class = "";

        public string Line { get { return line.ToString(); } }
        public string Host { get { return Hosts.Count > 0 ? Hosts[Hosts.Count - 1] : null; } }

        public void Lost(string why)
        {
            if (!Known) return;
            Known = false;
            Why = why;
        }

        public void Typed(string text)
        {
            foreach (char c in text ?? "")
            {
                if (c == '\r' || c == '\n') continue;
                if (c == '\t') { Lost("Tab"); continue; }
                line.Append(c);
            }
        }

        static readonly HashSet<int> Moves = new HashSet<int> { 0x09, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2E };

        public static bool Free(KeyPress k)
        {
            if (k.With != null || k.Win) return false;
            if (k.Vk == 0x10 || k.Vk == 0x11 || k.Vk == 0x12) return true;
            if (k.Vk == 0x08 && !k.Ctrl && !k.Alt) return true;
            if (!k.Alt && Moves.Contains(k.Vk)) return true;
            if (k.Ctrl && !k.Alt && !k.Shift && (k.Vk == 'C' || k.Vk == 'L')) return true;
            return !k.Ctrl && !k.Alt && k.Printable && !string.IsNullOrEmpty(k.Text) && k.Vk != 0x0D && k.Vk != 0x09 && k.Vk != 0x1B;
        }

        public static bool IsEnter(KeyPress k)
        {
            if (k.With != null) foreach (KeyPress x in k.With) if (x.Vk == 0x0D) return true;
            if (k.Alt) return false;
            return k.Vk == 0x0D || (k.Ctrl && (k.Vk == 'M' || k.Vk == 'J'));
        }

        public void Pressed(KeyPress k)
        {
            if (k.With != null) { Lost(k.Label); return; }
            if (k.Vk == 0x10 || k.Vk == 0x11 || k.Vk == 0x12) return;
            if (k.Vk == 0x08 && !k.Ctrl && !k.Alt) { if (Known && line.Length > 0) line.Length--; return; }
            if (k.Ctrl && !k.Alt && !k.Shift && k.Vk == 'C')
            {
                if (!Clicked && !Selection) Cleared();
                else Lost("Ctrl+C while text may be selected");
                return;
            }
            if (k.Ctrl && !k.Alt && !k.Shift && k.Vk == 'L') return;
            if (k.Alt && k.Vk == 0x0D) return;
            if (!k.Ctrl && !k.Alt && !k.Win && k.Printable && !string.IsNullOrEmpty(k.Text) && k.Vk != 0x0D && k.Vk != 0x09 && k.Vk != 0x1B) { line.Append(k.Text); return; }
            Lost(k.Label);
        }

        public void Cleared()
        {
            line.Length = 0;
            Known = true;
            Why = "";
            Clicked = false;
            Selection = false;
        }

        public void Ran()
        {
            string cmd = Known ? line.ToString().Trim() : null;
            line.Length = 0;
            Known = true;
            Why = "";
            Clicked = false;
            if (cmd == null) { if (Hosts.Count == 0) Hosts.Add("?"); return; }
            string remote;
            bool plain;
            string target = TerminalRules.SshTarget(cmd, out remote, out plain);
            if (target != null && remote == null) { Hosts.Add(plain ? target : "?"); return; }
            if (target != null) return;
            if (Hosts.Count == 0)
            {
                if (Regex.IsMatch(cmd, @"^(wsl|wsl\.exe|bash|bash\.exe)(\s+(-d|--distribution|-u|--user)\s+\S+)*\s*$", RegexOptions.IgnoreCase)) Hosts.Add("WSL");
                return;
            }
            if (TerminalRules.Leaves(cmd)) { Hosts.RemoveAt(Hosts.Count - 1); return; }
            if (TerminalRules.Nests(cmd)) Hosts.Add(Host);
        }
    }

    class TerminalPlace
    {
        public const string Wsl = "WSL (Linux on this PC)";
        public string Name;
        public bool Linux, Sure = true;
    }

    static class TerminalCheck
    {
        public static PermissionAsk Ask(string line, bool known, string why, string place, PermissionState ps, string workspace, string window)
        {
            TerminalPlace at = new TerminalPlace();
            at.Name = place;
            at.Linux = place == TerminalPlace.Wsl;
            return Ask(line, known, why, null, at, ps, workspace, window);
        }

        public static PermissionAsk Ask(string line, bool known, string why, string key, TerminalPlace place, PermissionState ps, string workspace, string window)
        {
            if (place == null || place.Name == null) return One(line, known, why, key, null, ps, workspace, window);
            PermissionAsk there = One(line, known, why, key, place.Name, ps, workspace, window);
            if (place.Sure && !place.Linux) return there;
            PermissionAsk here = One(line, known, why, key, null, ps, workspace, window);
            PermissionAsk a = Stricter(there, here);
            if (a != null && !place.Sure) a.Reason = ((a.Reason ?? "") + " MinGent isn't sure this terminal is still connected to " + place.Name + ", so it checked the command both ways.").Trim();
            return a;
        }

        public static PermissionAsk Stricter(PermissionAsk a, PermissionAsk b)
        {
            if (a == null) return b;
            if (b == null) return a;
            if (a.Never) return a;
            if (b.Never) return b;
            PermissionAsk s = a.High || !b.High ? a : b;
            PermissionAsk o = s == a ? b : a;
            if ((s.Category ?? "") != (o.Category ?? ""))
            {
                s.Category = "";
                s.AllowRemember = false;
                s.Key = null;
            }
            if (o.High && !string.IsNullOrEmpty(o.Reason) && (s.Reason ?? "").IndexOf(o.Reason, StringComparison.Ordinal) < 0) s.Reason = ((s.Reason ?? "") + " " + o.Reason).Trim();
            s.High = s.High || o.High;
            return s;
        }

        static PermissionAsk One(string line, bool known, string why, string key, string place, PermissionState ps, string workspace, string window)
        {
            string cmd = (line ?? "").Trim();
            bool guard = ps.WebGuard && ps.Tainted;
            string web = guard ? "This chat has read web content (" + ps.TaintSource + "), so commands need your OK in case a page tried to slip in instructions." : "";
            string act = key == null ? null : char.ToUpperInvariant(key[0]) + key.Substring(1);
            string then = key == null ? "" : "Keys and clicks other than plain typing can run or change what is on the command line, so MinGent checks it first. ";
            PermissionAsk a = new PermissionAsk();
            a.Kind = "command";
            a.Where = "In the terminal \"" + window + "\"" + (place != null ? ", connected to " + place : " on this PC");
            if (!known)
            {
                a.Title = (act ?? "Press Enter") + " in the terminal";
                a.Detail = cmd.Length > 0 ? "\u2026 " + cmd : "(MinGent can't see what is on the command line)";
                a.Reason = then + "The agent pressed " + why + " after its last command, so MinGent can't tell exactly what will run.";
                a.High = true;
                a.AllowRemember = false;
                a.Category = "";
                return a;
            }
            if (cmd.Length == 0) return null;
            a.Detail = cmd;
            a.Summary = CommandWords.Describe(cmd);
            if (TerminalRules.Expands(cmd))
            {
                a.Title = act == null ? "Run a command in the terminal" : act + " in the terminal";
                a.High = true;
                a.AllowRemember = false;
                a.Category = "";
                a.Reason = then + "It uses ! or ^, which a Linux shell can turn into a different command from its history, so MinGent can't check what will run.";
                return a;
            }
            if (place != null)
            {
                string scat;
                string secret = CommandSafety.ServerDanger(cmd, out scat);
                a.Title = act == null ? "Run a command on " + place : act + " in the terminal on " + place;
                if (secret != null)
                {
                    a.High = true;
                    a.AllowRemember = false;
                    a.Reason = then + "This command " + secret + " on " + place + ".";
                    if (scat == PermissionCategory.Never) a.Never = true;
                    else a.Category = guard ? "" : scat;
                    return a;
                }
                bool look = TerminalRules.ReadOnly(cmd);
                if (look && ps.Auto && !guard) return null;
                a.Key = "term:" + place + ":" + cmd;
                if (look) a.Reason = (then + web).Trim();
                else
                {
                    a.High = true;
                    a.Category = guard ? "" : PermissionCategory.Remote;
                    a.Reason = then + "It may change things on " + place + "." + (guard ? " " + web : "");
                }
                return a;
            }
            string category;
            string danger = CommandSafety.Danger(cmd, workspace, Elsewhere(workspace), out category);
            string remote;
            bool plain;
            string target = TerminalRules.SshTarget(cmd, out remote, out plain);
            if (danger == null && target != null && remote != null)
            {
                string scat;
                string secret = CommandSafety.ServerDanger(remote, out scat);
                if (secret != null) { danger = secret + " on " + target; category = scat; }
                else if (!TerminalRules.ReadOnly(remote))
                {
                    danger = "runs a command on " + target + " that may change things there";
                    category = PermissionCategory.Remote;
                }
            }
            if (danger == null && CommandSafety.ChangesShell(cmd))
            {
                danger = "changes how later commands in this terminal behave (aliases, functions, key bindings or settings like PATH), so later checks could be fooled";
                category = "";
            }
            a.Title = act == null ? "Run a command in the terminal" : act + " in the terminal";
            if (danger != null)
            {
                a.High = true;
                a.AllowRemember = false;
                a.Reason = then + "This command " + danger + ".";
                if (category == PermissionCategory.Never) a.Never = true;
                else a.Category = guard ? "" : category ?? "";
                return a;
            }
            if (ps.Auto && !guard) return null;
            a.Key = "term::" + cmd;
            a.Reason = (then + web).Trim();
            return a;
        }

        public static string Elsewhere(string workspace)
        {
            string root = "";
            try { root = System.IO.Path.GetPathRoot(workspace ?? "") ?? ""; } catch (Exception) { }
            return root.Length > 0 ? root : System.IO.Path.DirectorySeparatorChar == '\\' ? "C:\\" : "/";
        }
    }

    static class TerminalProbe
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ProcessEntry
        {
            public uint Size, Usage, Pid;
            public IntPtr Heap;
            public uint Module, Threads, Parent;
            public int Priority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] static extern bool FirstProcess(IntPtr snap, ref ProcessEntry e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] static extern bool NextProcess(IntPtr snap, ref ProcessEntry e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

        class Proc { public int Pid, Parent; public string Name; }

        static Dictionary<int, Proc> Snapshot()
        {
            Dictionary<int, Proc> all = new Dictionary<int, Proc>();
            IntPtr snap = CreateToolhelp32Snapshot(2, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return all;
            try
            {
                ProcessEntry e = new ProcessEntry();
                e.Size = (uint)Marshal.SizeOf(typeof(ProcessEntry));
                if (!FirstProcess(snap, ref e)) return all;
                do
                {
                    Proc p = new Proc();
                    p.Pid = (int)e.Pid;
                    p.Parent = (int)e.Parent;
                    p.Name = (e.Exe ?? "").ToLowerInvariant();
                    all[p.Pid] = p;
                }
                while (NextProcess(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return all;
        }

        static long Created(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return 0;
            try
            {
                long c, x, k, u;
                return GetProcessTimes(h, out c, out x, out k, out u) ? c : 0;
            }
            finally { CloseHandle(h); }
        }

        static readonly HashSet<string> Shells = new HashSet<string>(StringComparer.Ordinal) { "cmd.exe", "powershell.exe", "pwsh.exe", "wsl.exe", "bash.exe" };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")] static extern bool ImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        static bool? WslBash(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int n = sb.Capacity;
                if (!ImageName(h, 0, sb, ref n)) return null;
                string dir = System.IO.Path.GetDirectoryName(sb.ToString()) ?? "";
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System) ?? "";
                return sys.Length > 0 && string.Equals(dir.TrimEnd('\\'), sys.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return null; }
            finally { CloseHandle(h); }
        }

        static string Processes(AppWindow w, TerminalState t)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            Dictionary<int, Proc> all = Snapshot();
            Proc own;
            if (all.Count == 0 || !all.TryGetValue(w.Pid, out own)) return null;
            Dictionary<int, List<int>> kids = new Dictionary<int, List<int>>();
            foreach (Proc p in all.Values)
            {
                List<int> l;
                if (!kids.TryGetValue(p.Parent, out l)) kids[p.Parent] = l = new List<int>();
                l.Add(p.Pid);
            }
            List<int> roots = new List<int>();
            Dictionary<int, long> made = new Dictionary<int, long>();
            long from = t.Since.ToFileTimeUtc() - 20000000L, until = t.Opened.ToFileTimeUtc() + 150000000L;
            if (w.Class == "ConsoleWindowClass")
            {
                roots.Add(w.Pid);
                if ((own.Name == "conhost.exe" || own.Name == "openconsole.exe") && all.ContainsKey(own.Parent)) roots.Add(own.Parent);
            }
            else
            {
                List<int> hosts;
                if (kids.TryGetValue(w.Pid, out hosts))
                    foreach (int k in hosts)
                    {
                        string n = all[k].Name;
                        if (n != "openconsole.exe" && n != "conhost.exe") continue;
                        long c = Created(k);
                        made[k] = c;
                        if (c >= from && c <= until) roots.Add(k);
                    }
                foreach (Proc p in all.Values)
                {
                    if (!Shells.Contains(p.Name) || !AppControl.InAgentJob(p.Pid)) continue;
                    long c = Created(p.Pid);
                    made[p.Pid] = c;
                    if (c >= from && c <= until) roots.Add(p.Pid);
                }
            }
            if (roots.Count == 0) return null;
            bool wsl = false;
            HashSet<int> seen = new HashSet<int>();
            List<int> level = new List<int>(roots);
            for (int depth = 0; depth < 12 && level.Count > 0; depth++)
            {
                List<int> next = new List<int>();
                foreach (int pid in level)
                {
                    if (!seen.Add(pid)) continue;
                    string n = all.ContainsKey(pid) ? all[pid].Name : "";
                    if (n == "ssh.exe" || n == "plink.exe") return "ssh";
                    if (n == "wsl.exe" || n == "wslhost.exe") wsl = true;
                    else if (n == "bash.exe") { bool? sys = WslBash(pid); if (sys == true || (sys == null && depth > 0)) wsl = true; }
                    List<int> c;
                    if (!kids.TryGetValue(pid, out c)) continue;
                    long parentMade;
                    if (!made.TryGetValue(pid, out parentMade)) made[pid] = parentMade = Created(pid);
                    foreach (int k in c)
                    {
                        if (k == pid) continue;
                        long km = Created(k);
                        made[k] = km;
                        if (parentMade != 0 && km != 0 && km < parentMade) continue;
                        next.Add(k);
                    }
                }
                level = next;
            }
            return wsl ? "wsl" : "";
        }

        public static bool FromAgent(int pid)
        {
            if (AppControl.InAgentJob(pid)) return true;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
            Dictionary<int, Proc> all = Snapshot();
            Proc p;
            if (!all.TryGetValue(pid, out p) || (p.Name != "conhost.exe" && p.Name != "openconsole.exe")) return false;
            return all.ContainsKey(p.Parent) && AppControl.InAgentJob(p.Parent);
        }

        public static TerminalPlace Place(AppWindow w, TerminalState t)
        {
            string p = null;
            try { p = Processes(w, t); } catch (Exception) { p = null; }
            TerminalPlace at = new TerminalPlace();
            lock (t)
            {
                string h = t.Host;
                bool named = h != null && h != "?" && h != "WSL";
                if (p == "ssh") { at.Name = named ? h : "a server over ssh"; at.Sure = named; return at; }
                if (p == "wsl") { at.Name = named ? h : TerminalPlace.Wsl; at.Linux = !named; return at; }
                if (h == null) return at;
                at.Name = h == "?" ? "a place MinGent can't tell (it may be connected to a server)" : h == "WSL" ? TerminalPlace.Wsl : h;
                at.Linux = h == "WSL";
                at.Sure = p == null && h != "?";
                return at;
            }
        }

        [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")] class CUIAutomation { }

        [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IUIAutomation
        {
            void CompareElements();
            void CompareRuntimeIds();
            void GetRootElement();
            [PreserveSig] int ElementFromHandle(IntPtr hwnd, out IUIAutomationElement element);
            void ElementFromPoint();
            void GetFocusedElement();
            void GetRootElementBuildCache();
            void ElementFromHandleBuildCache();
            void ElementFromPointBuildCache();
            void GetFocusedElementBuildCache();
            void CreateTreeWalker();
            void ControlViewWalker();
            void ContentViewWalker();
            void RawViewWalker();
            void RawViewCondition();
            void ControlViewCondition();
            void ContentViewCondition();
            void CreateCacheRequest();
            void CreateTrueCondition();
            void CreateFalseCondition();
            [PreserveSig] int CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value, out IUIAutomationCondition condition);
        }

        [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IUIAutomationCondition { }

        [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IUIAutomationElement
        {
            void SetFocus();
            void GetRuntimeId();
            void FindFirst();
            [PreserveSig] int FindAll(int scope, IUIAutomationCondition condition, out IUIAutomationElementArray found);
            void FindFirstBuildCache();
            void FindAllBuildCache();
            void BuildUpdatedCache();
            void GetCurrentPropertyValue();
            void GetCurrentPropertyValueEx();
            void GetCachedPropertyValue();
            void GetCachedPropertyValueEx();
            void GetCurrentPatternAs();
            void GetCachedPatternAs();
            [PreserveSig] int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object pattern);
            void GetCachedPattern();
            void GetCachedParent();
            void GetCachedChildren();
            void CurrentProcessId();
            void CurrentControlType();
            void CurrentLocalizedControlType();
            void CurrentName();
            void CurrentAcceleratorKey();
            void CurrentAccessKey();
            void CurrentHasKeyboardFocus();
            void CurrentIsKeyboardFocusable();
            void CurrentIsEnabled();
            void CurrentAutomationId();
            void CurrentClassName();
            void CurrentHelpText();
            void CurrentCulture();
            void CurrentIsControlElement();
            void CurrentIsContentElement();
            void CurrentIsPassword();
            void CurrentNativeWindowHandle();
            void CurrentItemType();
            void CurrentIsOffscreen();
            void CurrentOrientation();
            void CurrentFrameworkId();
            void CurrentIsRequiredForForm();
            void CurrentItemStatus();
            [PreserveSig] int CurrentBoundingRectangle(out RECT rect);
        }

        [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IUIAutomationElementArray
        {
            [PreserveSig] int Length(out int length);
            [PreserveSig] int GetElement(int index, out IUIAutomationElement element);
        }

        [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IUIAutomationTextPattern
        {
            void RangeFromPoint();
            void RangeFromChild();
            [PreserveSig] int GetSelection(out IUIAutomationTextRangeArray ranges);
            [PreserveSig] int GetVisibleRanges(out IUIAutomationTextRangeArray ranges);
            [PreserveSig] int DocumentRange(out IUIAutomationTextRange range);
        }

        [ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IUIAutomationTextRangeArray
        {
            [PreserveSig] int Length(out int length);
            [PreserveSig] int GetElement(int index, out IUIAutomationTextRange range);
        }

        [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IUIAutomationTextRange
        {
            void Clone();
            void Compare();
            void CompareEndpoints();
            void ExpandToEnclosingUnit();
            void FindAttribute();
            void FindText();
            void GetAttributeValue();
            void GetBoundingRectangles();
            void GetEnclosingElement();
            [PreserveSig] int GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string text);
        }

        static IUIAutomationTextPattern Pattern(IntPtr hwnd)
        {
            IUIAutomation ua = (IUIAutomation)new CUIAutomation();
            IUIAutomationElement root;
            if (ua.ElementFromHandle(hwnd, out root) != 0 || root == null) return null;
            IUIAutomationCondition cond;
            if (ua.CreatePropertyCondition(30040, true, out cond) != 0 || cond == null) return null;
            IUIAutomationElementArray found;
            if (root.FindAll(7, cond, out found) != 0 || found == null) return null;
            int n;
            if (found.Length(out n) != 0) return null;
            IUIAutomationElement best = null;
            long bestArea = 0;
            for (int i = 0; i < n && i < 64; i++)
            {
                IUIAutomationElement e;
                RECT r;
                if (found.GetElement(i, out e) != 0 || e == null || e.CurrentBoundingRectangle(out r) != 0) continue;
                long area = (long)Math.Max(0, r.Right - r.Left) * Math.Max(0, r.Bottom - r.Top);
                if (area > bestArea) { bestArea = area; best = e; }
            }
            object pat;
            if (best == null || best.GetCurrentPattern(10014, out pat) != 0) return null;
            return pat as IUIAutomationTextPattern;
        }

        public static bool? Selected(IntPtr hwnd)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            bool? result = null;
            AppControl.RunTimed(delegate
            {
                IUIAutomationTextPattern tp = Pattern(hwnd);
                IUIAutomationTextRangeArray sel;
                int n;
                if (tp == null || tp.GetSelection(out sel) != 0 || sel == null || sel.Length(out n) != 0) return;
                bool any = false;
                for (int i = 0; i < n && i < 16 && !any; i++)
                {
                    IUIAutomationTextRange r;
                    string s;
                    if (sel.GetElement(i, out r) == 0 && r != null && r.GetText(64, out s) == 0 && !string.IsNullOrEmpty(s)) any = true;
                }
                result = any;
            }, 3000);
            return result;
        }

        public static string Text(IntPtr hwnd, int maxChars)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            string result = null;
            AppControl.RunTimed(delegate
            {
                IUIAutomationTextPattern tp = Pattern(hwnd);
                if (tp == null) return;
                StringBuilder sb = new StringBuilder();
                IUIAutomationTextRangeArray ranges;
                int rn;
                if (tp.GetVisibleRanges(out ranges) == 0 && ranges != null && ranges.Length(out rn) == 0)
                    for (int i = 0; i < rn && i < 500 && sb.Length < maxChars * 3; i++)
                    {
                        IUIAutomationTextRange tr;
                        string s;
                        if (ranges.GetElement(i, out tr) != 0 || tr == null || tr.GetText(-1, out s) != 0 || s == null) continue;
                        sb.Append(s);
                        if (!s.EndsWith("\n", StringComparison.Ordinal)) sb.Append('\n');
                    }
                if (sb.ToString().Trim().Length == 0)
                {
                    IUIAutomationTextRange doc;
                    string all;
                    if (tp.DocumentRange(out doc) == 0 && doc != null && doc.GetText(-1, out all) == 0 && all != null) sb.Append(all);
                }
                result = Tidy(sb.ToString(), maxChars);
            }, 5000);
            return result;
        }

        public static string Tidy(string text, int maxChars)
        {
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            List<string> keep = new List<string>();
            foreach (string l in lines) keep.Add(l.TrimEnd());
            while (keep.Count > 0 && keep[keep.Count - 1].Length == 0) keep.RemoveAt(keep.Count - 1);
            int start = keep.Count, size = 0;
            while (start > 0 && size + keep[start - 1].Length + 1 <= maxChars) { start--; size += keep[start].Length + 1; }
            while (start < keep.Count && keep[start].Length == 0) start++;
            string body = string.Join("\n", keep.GetRange(start, keep.Count - start).ToArray());
            return start > 0 && body.Length > 0 ? "(earlier lines not shown)\n" + body : body;
        }
    }
}
