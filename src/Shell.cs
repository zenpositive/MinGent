using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MinGent
{
    class ShellJob
    {
        public int Id;
        public string Command = "", Shell = "", Cwd = "";
        public Process Proc;
        public IntPtr Job = IntPtr.Zero;
        public readonly MemoryStream Buffer = new MemoryStream();
        public readonly object Gate = new object();
        public long Read;
        public DateTime Started = DateTime.UtcNow;
        public volatile bool Exited;
        public int ExitCode = -1;
        public string Ending = "";
        public string ScriptFile;
        public bool Background;
        public long Dropped;

        public string Text(long from)
        {
            byte[] b;
            lock (Gate)
            {
                long len = Buffer.Length;
                if (from >= len) return "";
                b = new byte[len - from];
                Buffer.Position = from;
                Buffer.Read(b, 0, b.Length);
                Buffer.Position = len;
            }
            return MinGent.Shell.Decode(b);
        }

        public long Length { get { lock (Gate) return Buffer.Length; } }
    }

    static class Shell
    {
        public const long MaxKeep = 2 * 1024 * 1024;
        static readonly List<ShellJob> jobs = new List<ShellJob>();
        static int nextId = 1;
        static string pwshPath;
        static bool pwshChecked;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr attrs, string name);
        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll")]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")]
        static extern bool TerminateJobObject(IntPtr job, uint exitCode);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        static bool IsWindows { get { return Path.DirectorySeparatorChar == '\\'; } }

        public static string PowerShellExe
        {
            get
            {
                if (!pwshChecked)
                {
                    pwshChecked = true;
                    if (IsWindows)
                    {
                        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                        {
                            try
                            {
                                string d = dir.Trim().Trim('"');
                                if (d.Length == 0 || !Path.IsPathRooted(d) || d.StartsWith("\\\\")) continue;
                                string p = Path.Combine(d, "pwsh.exe");
                                if (File.Exists(p)) { pwshPath = p; break; }
                            }
                            catch { }
                        }
                        if (pwshPath == null)
                        {
                            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                            string p = Path.Combine(Path.Combine(Path.Combine(pf, "PowerShell"), "7"), "pwsh.exe");
                            if (File.Exists(p)) pwshPath = p;
                        }
                    }
                }
                if (pwshPath != null) return pwshPath;
                if (!IsWindows) return "pwsh";
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                return Path.Combine(Path.Combine(Path.Combine(sys, "WindowsPowerShell"), "v1.0"), "powershell.exe");
            }
        }

        public static string PowerShellName { get { return PowerShellExe.EndsWith("pwsh.exe", StringComparison.OrdinalIgnoreCase) ? "PowerShell 7" : "Windows PowerShell 5.1"; } }

        public static ShellJob Start(string command, string shell, string cwd, bool background, out string error)
        {
            error = null;
            Sweep();
            ShellJob j = new ShellJob();
            j.Command = command;
            j.Shell = shell == "cmd" ? "cmd" : "powershell";
            j.Cwd = cwd;
            j.Background = background;
            ProcessStartInfo psi;
            if (j.Shell == "cmd")
            {
                psi = IsWindows ? new ProcessStartInfo(Native.WindowsExe("cmd.exe"), "/d /s /c \"" + command + "\"")
                                : new ProcessStartInfo("/bin/sh", "-c \"" + command.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
            }
            else
            {
                string dir = Path.Combine(Path.GetTempPath(), "MinGent");
                Directory.CreateDirectory(dir);
                j.ScriptFile = Path.Combine(dir, "cmd-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".ps1");
                StringBuilder sb = new StringBuilder();
                sb.Append("$ProgressPreference = 'SilentlyContinue'\r\n");
                sb.Append("try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}\r\n");
                sb.Append("$global:LASTEXITCODE = 0\r\n");
                sb.Append(command.Replace("\r\n", "\n").Replace("\n", "\r\n")).Append("\r\n");
                sb.Append("if ($global:LASTEXITCODE -ne 0 -and $global:LASTEXITCODE -ne $null) { exit $global:LASTEXITCODE }\r\n");
                sb.Append("if (-not $?) { exit 1 }\r\n");
                File.WriteAllText(j.ScriptFile, sb.ToString(), new UTF8Encoding(true));
                psi = new ProcessStartInfo(PowerShellExe, "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + j.ScriptFile + "\"");
            }
            psi.WorkingDirectory = cwd;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            psi.EnvironmentVariables["TERM"] = "dumb";
            psi.EnvironmentVariables["CI"] = "1";
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            psi.EnvironmentVariables["npm_config_yes"] = "true";
            psi.EnvironmentVariables["PIP_NO_INPUT"] = "1";
            foreach (string k in new string[] { "ANTHROPIC_API_KEY", "OPENAI_API_KEY" })
                if (psi.EnvironmentVariables.ContainsKey(k)) psi.EnvironmentVariables.Remove(k);
            Process p = new Process();
            p.StartInfo = psi;
            try { p.Start(); }
            catch (Exception ex)
            {
                error = "could not start: " + ex.Message;
                Cleanup(j);
                return null;
            }
            j.Proc = p;
            j.Job = MakeJob(p);
            try { p.StandardInput.Close(); } catch { }
            Pump(p.StandardOutput.BaseStream, j);
            Pump(p.StandardError.BaseStream, j);
            lock (jobs) { j.Id = nextId++; jobs.Add(j); }
            Thread waiter = new Thread(delegate ()
            {
                try { p.WaitForExit(); } catch { }
                try { j.ExitCode = p.ExitCode; } catch { }
                Thread.Sleep(150);
                j.Exited = true;
                Cleanup(j);
            });
            waiter.IsBackground = true;
            waiter.Start();
            return j;
        }

        static IntPtr MakeJob(Process p)
        {
            if (!IsWindows) return IntPtr.Zero;
            try
            {
                IntPtr job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) return IntPtr.Zero;
                ExtendedLimits info = new ExtendedLimits();
                info.Basic.LimitFlags = 0x2000;
                int size = Marshal.SizeOf(typeof(ExtendedLimits));
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(job, 9, ptr, (uint)size);
                }
                finally { Marshal.FreeHGlobal(ptr); }
                AssignProcessToJobObject(job, p.Handle);
                return job;
            }
            catch { return IntPtr.Zero; }
        }

        static void Pump(Stream s, ShellJob j)
        {
            Thread t = new Thread(delegate ()
            {
                byte[] b = new byte[8192];
                try
                {
                    int n;
                    while ((n = s.Read(b, 0, b.Length)) > 0)
                        lock (j.Gate)
                        {
                            if (j.Buffer.Length < MaxKeep) j.Buffer.Write(b, 0, (int)Math.Min(n, MaxKeep - j.Buffer.Length));
                            else j.Dropped += n;
                        }
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        static void Cleanup(ShellJob j)
        {
            if (j.ScriptFile != null) SecureFile.Erase(j.ScriptFile);
        }

        public static void SweepScripts()
        {
            foreach (string name in new string[] { "MinGent", "JustPad" })
            {
                string dir = Path.Combine(Path.GetTempPath(), name);
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir, "cmd-*.ps1"))
                {
                    try { if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalHours >= 24) SecureFile.Erase(f); } catch { }
                }
                if (name == "JustPad") { try { if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir); } catch { } }
            }
        }

        [DllImport("kernel32.dll")]
        static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out BasicAccounting info, int length, IntPtr returned);

        [StructLayout(LayoutKind.Sequential)]
        struct BasicAccounting
        {
            public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
            public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
        }

        static int ActiveProcesses(IntPtr job)
        {
            if (job == IntPtr.Zero) return 0;
            try
            {
                BasicAccounting info;
                if (QueryInformationJobObject(job, 1, out info, Marshal.SizeOf(typeof(BasicAccounting)), IntPtr.Zero)) return (int)info.ActiveProcesses;
            }
            catch { }
            return -1;
        }

        public static void Sweep()
        {
            lock (jobs)
            {
                foreach (ShellJob j in jobs)
                {
                    if (!j.Exited || j.Job == IntPtr.Zero) continue;
                    if (ActiveProcesses(j.Job) == 0)
                    {
                        try { CloseHandle(j.Job); } catch { }
                        j.Job = IntPtr.Zero;
                    }
                }
                int excess = jobs.Count - 60;
                for (int i = 0; i < jobs.Count && excess > 0; )
                {
                    ShellJob j = jobs[i];
                    if (j.Exited && j.Job == IntPtr.Zero) { jobs.RemoveAt(i); excess--; }
                    else i++;
                }
            }
        }

        public static void Kill(ShellJob j, string why)
        {
            if (j == null || j.Exited) return;
            j.Ending = why;
            if (j.Job != IntPtr.Zero) { try { TerminateJobObject(j.Job, 1); } catch { } }
            try { if (j.Proc != null && !j.Proc.HasExited) j.Proc.Kill(); } catch { }
            if (IsWindows && j.Proc != null)
            {
                try
                {
                    ProcessStartInfo k = new ProcessStartInfo(Native.WindowsExe("taskkill.exe"), "/T /F /PID " + j.Proc.Id);
                    k.CreateNoWindow = true;
                    k.UseShellExecute = false;
                    using (Process kp = Process.Start(k)) kp.WaitForExit(3000);
                }
                catch { }
            }
        }

        public static ShellJob Find(int id)
        {
            lock (jobs) foreach (ShellJob j in jobs) if (j.Id == id) return j;
            return null;
        }

        public static List<IntPtr> JobHandles()
        {
            Sweep();
            List<IntPtr> l = new List<IntPtr>();
            lock (jobs) foreach (ShellJob j in jobs) if (j.Job != IntPtr.Zero) l.Add(j.Job);
            return l;
        }

        public static List<ShellJob> Background()
        {
            List<ShellJob> l = new List<ShellJob>();
            lock (jobs) foreach (ShellJob j in jobs) if (j.Background && !j.Exited) l.Add(j);
            return l;
        }

        public static void KillAll()
        {
            ShellJob[] all;
            lock (jobs) all = jobs.ToArray();
            foreach (ShellJob j in all) Kill(j, "MinGent closed");
        }

        public static string Decode(byte[] b)
        {
            try { return Document.NormalizeNewlines(new UTF8Encoding(false, true).GetString(b)); }
            catch (DecoderFallbackException) { }
            try { return Document.NormalizeNewlines(Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage).GetString(b)); }
            catch { return Document.NormalizeNewlines(Encoding.Default.GetString(b)); }
        }

        public static string StripAnsi(string s)
        {
            if (s.IndexOf('\x1b') < 0) return s;
            return Regex.Replace(s, @"\x1b\[[0-9;?]*[ -/]*[@-~]|\x1b\][^\x07]*(\x07|\x1b\\)", "");
        }

        public static string Tail(string s, int n)
        {
            if (s.Length <= n) return s;
            int cut = s.IndexOf('\n', s.Length - n);
            return "..." + (cut >= 0 ? s.Substring(cut) : s.Substring(s.Length - n));
        }

        public static string ForModel(string output, int max)
        {
            output = StripAnsi(output).TrimEnd();
            if (output.Length <= max) return output;
            int head = max / 4, tail = max - head;
            return output.Substring(0, head) + "\n\n... (" + (output.Length - max).ToString("N0", CultureInfo.InvariantCulture) + " characters of output omitted) ...\n\n" + output.Substring(output.Length - tail);
        }
    }

    static class CommandSafety
    {
        public static string Invisible(string command)
        {
            foreach (char c in command ?? "")
            {
                if ((c >= '\u200B' && c <= '\u200F') || (c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069') || c == '\uFEFF' || c == '\u00AD' ||
                    (c < ' ' && c != '\t' && c != '\n' && c != '\r'))
                    return "it contains invisible or text-direction characters that could hide what it really does";
            }
            return null;
        }

        class Rule { public Regex Re; public string Why, Category; }

        static readonly Rule[] Rules = BuildRules();

        static Rule[] BuildRules()
        {
            string[,] r = {
                { @"\bformat(\.com)?\s+[a-z]:(?!\w)|\b(format-volume|clear-disk|initialize-disk|remove-partition|diskpart|mountvol|bcdedit|bootrec|bcdboot)\b", "formats or repartitions disks / changes boot settings", PermissionCategory.System },
                { @"\b(vssadmin|wbadmin|wmic\s+shadowcopy)\b|cipher(\.exe)?\s+/w|\bsdelete\b", "deletes backups or wipes free space", PermissionCategory.Delete },
                { @"\b(shutdown(\.exe)?\s|restart-computer|stop-computer|logoff\b)", "shuts down or restarts the PC", PermissionCategory.System },
                { @"\breg(\.exe)?\s+(add|delete|import|load|unload|restore|copy)\b", "changes the Windows registry", PermissionCategory.System },
                { @"\b(set|new|remove|rename)-itemproperty\b[^\n]*\b(hklm|hkcu|registry::)", "changes the Windows registry", PermissionCategory.System },
                { @"\b(new|remove)-item\b[^\n]*\b(hklm|hkcu):", "changes the Windows registry", PermissionCategory.System },
                { @"\bregedit(\.exe)?\b", "changes the Windows registry", PermissionCategory.System },
                { @"\b(net(\.exe)?\s+(user|localgroup|share|accounts)|new-localuser|remove-localuser|add-localgroupmember|set-localuser)\b", "changes user accounts or shares", PermissionCategory.System },
                { @"\b(takeown|icacls|cacls|set-acl)\b", "changes file ownership or permissions", PermissionCategory.System },
                { @"\b(schtasks(\.exe)?\s+/(create|change|delete)|register-scheduledtask|unregister-scheduledtask|new-scheduledtask)\b", "creates or changes scheduled tasks", PermissionCategory.System },
                { @"\b(sc(\.exe)?\s+(create|config|delete|failure)|new-service|set-service|remove-service)\b", "creates or changes Windows services", PermissionCategory.System },
                { @"\b(set-mppreference|add-mppreference|remove-mppreference)\b|disablerealtimemonitoring|\bnetsh(\.exe)?\s+(adv)?firewall\b|\b(set|new|remove)-netfirewall", "changes antivirus or firewall settings", PermissionCategory.System },
                { @"\bset-executionpolicy\b", "changes the PowerShell execution policy", PermissionCategory.System },
                { @"\b(invoke-expression|iex)\b|(^|\s)[-/]e(c|nc|ncodedcommand)?\s+[a-z0-9+/=]{20,}|frombase64string|\[scriptblock\]\s*::\s*create|\.(invokescript|newscriptblock)\s*\(|(^|[\s;|{(=])[&.]\s*\(|(\[char\]\s*\d+\s*[+,]\s*){4,}", "runs hidden or dynamically built code", PermissionCategory.System },
                { @"\bmklink\b|-itemtype\s+['""]?(junction|symboliclink|hardlink)\b|\bfsutil(\.exe)?\s+(hardlink|reparsepoint)\b", "creates a link to another folder, so later steps could reach outside the work folder", PermissionCategory.Delete },
                { @"\brobocopy(\.exe)?\b[^\n]*\s/(mir|purge)\b", "mirrors folders, which deletes files in the target folder", PermissionCategory.Delete },
                { @"\bclear-recyclebin\b|\$recycle\.bin", "empties the Recycle Bin", PermissionCategory.Delete },
                { @"\b(mshta|rundll32|regsvr32|installutil|regasm|regsvcs|msxsl|cmstp|odbcconf)(\.exe)?\b", "uses a Windows tool often abused to run hidden code", PermissionCategory.System },
                { @"\bcertutil(\.exe)?\b[^\n]*-(urlcache|decode|encode)\b|\bbitsadmin\b|start-bitstransfer", "downloads or decodes files in an unusual way", PermissionCategory.Install },
                { @"\b(invoke-webrequest|iwr|invoke-restmethod|irm|curl(\.exe)?|wget)\b[^\n]*(-infile|--upload-file|-t\s|-f\s|--data-binary\s+@|-d\s+@|--form)", "uploads files from your PC to the internet", PermissionCategory.Publish },
                { @"\b(invoke-webrequest|iwr|invoke-restmethod|irm|curl|wget)\b[^\n|]*\|\s*(sh|bash|pwsh|powershell|cmd|python|node)\b", "downloads and runs a script", PermissionCategory.Install },
                { @"\bgit\s+push\b[^\n]*(--force|-f\b)|\bgit\s+(reset\s+--hard|clean\s+-[a-z]*f)", "rewrites or discards git history / files", PermissionCategory.Publish },
                { @"\b(npm|pnpm|yarn)\s+publish\b|\bdotnet\s+nuget\s+push\b|\btwine\s+upload\b|\bdocker\s+push\b|\bcargo\s+publish\b|\bgh\s+(release|repo\s+delete)", "publishes or deletes something online", PermissionCategory.Publish },
                { @"\b(cmdkey|vaultcmd)\b|\bmimikatz\b|\\config\\(sam|security|system)\b|login data|\bpasswords?\.(txt|csv)\b", "reads stored passwords or credentials", PermissionCategory.Credentials },
                { @"[~\\/]\.(ssh|aws|azure|kube|gnupg)(\b|[\\/])|(^|[\s'""\\/=])\.(git-credentials|npmrc|pypirc|netrc)\b|[\\/]\.docker[\\/]config\.json|[\\/]\.cargo[\\/]credentials|\\github cli\\|\bid_(rsa|ed25519|ecdsa)\b", "touches credential, token or key files", PermissionCategory.Credentials },
                { @"\$profile\b|\\documents\\(windows)?powershell\\.*profile\.ps1|\\start menu\\programs\\startup\\|\\currentversion\\run", "sets something to run automatically at startup", PermissionCategory.System },
                { @"\b(set-itemproperty|new-itemproperty)\b[^\n]*\\(policies|system)\\", "changes Windows policies", PermissionCategory.System },
                { @"\b(bcdedit|wevtutil\s+cl|clear-eventlog|remove-eventlog)\b", "clears system logs or boot settings", PermissionCategory.System },
                { @"\buninstall-(package|module)\b|\bmsiexec(\.exe)?\s+/x\b|\bwinget\s+uninstall\b|\bchoco\s+uninstall\b", "uninstalls software", PermissionCategory.Install },
                { @"\b(winget|choco|scoop)\s+install\b|\bmsiexec(\.exe)?\s+/i\b|\binstall-module\b|\binstall-package\b", "installs software system-wide", PermissionCategory.Install },
                { @"\b(stop-process|taskkill)\b[^\n]*\b(explorer|lsass|csrss|winlogon|svchost|services|smss|wininit|msmpeng)\b", "kills an essential Windows process", PermissionCategory.System },
                { @"-verb[\s:]+['""]?runas\b|\brunas(\.exe)?\s+/|(^|[\r\n;&|(]\s*)sudo(\.exe)?\s|\bgsudo\b|\bnircmd(c)?\b[^\n]*\belevate\b", "runs something with administrator rights", PermissionCategory.System },
                { @"\b(add-mppreference|set-mppreference)\b|\bbcdedit\b|\bset-netconnectionprofile\b|\bdisable-netadapter\b", "changes security or network settings", PermissionCategory.System },
            };
            Rule[] res = new Rule[r.GetLength(0)];
            for (int i = 0; i < res.Length; i++)
            {
                res[i] = new Rule();
                res[i].Re = new Regex(r[i, 0], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                res[i].Why = r[i, 1];
                res[i].Category = r[i, 2];
            }
            return res;
        }

        const string PathEnd = @"[^\s'""`,;|&(){}<>]";

        static readonly Regex DeleteCmd = new Regex(
            @"(?<![\w-])(remove-item|rm|ri|del|erase|rd|rmdir|clear-content|clc)(?![\w-])([^\n;|&]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex NetDelete = new Regex(
            @"\]\s*::\s*(delete\w*)\s*\(([^\n;]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex ScriptDelete = new Regex(
            @"\b(shutil\.rmtree|os\.(remove|unlink|rmdir|removedirs)|rimraf|del-cli)\b|\.(rm|rmsync|rmdir|rmdirsync|unlink|unlinksync|rmtree)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex ScriptHome = new Regex(
            @"process\.env|os\.environ|getenv|expanduser|path\.home|homedir|userprofile|\$home|appdata", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex OtherDelete = new Regex(
            @"\.delete\s*\(([^\n;)]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex VarCall = new Regex(
            @"(^|[\s;|{(=])[&.]\s*\$\{?\w+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Recurse = new Regex(
            @"(^|\s)(-r|-recurse|-rf|-fr|/s)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex WildRoot = new Regex(
            @"(^|\s|=)['""]?([a-z]:\\?\*?|\\|/|~[\\/]?\*?|\$home|\$\{?env:(userprofile|homepath|homedrive|systemroot|windir|programfiles|programdata|appdata|localappdata|public|onedrive)\}?[\\/]?\*?|%(userprofile|homepath|systemroot|windir|programfiles|programdata|appdata|localappdata|public|onedrive)%[\\/]?\*?)['""]?(\s|$|,)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex AbsPath = new Regex(
            @"(?<![\w$%~\\/.:-])((?<![a-z])[a-z]:[\\/]" + PathEnd + @"*|\\\\" + PathEnd + @"+|(\$\{?env:\w+\}?|%\w+%|\$\{?home\}?)([\\/]" + PathEnd + @"*)?|~(?=[\\/\s]|$)([\\/]" + PathEnd + @"*)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex DotPath = new Regex(PathEnd + @"*\.\.(?=[\\/'""\s]|$)" + PathEnd + @"*", RegexOptions.CultureInvariant);

        public static string Danger(string command, string workspace)
        {
            string cat;
            return Danger(command, workspace, workspace, out cat);
        }

        public static string Danger(string command, string workspace, string cwd)
        {
            string cat;
            return Danger(command, workspace, cwd, out cat);
        }

        public static string Danger(string command, string workspace, string cwd, out string category)
        {
            category = "";
            string c = command ?? "";
            string flat = Flatten(c);
            foreach (Rule rule in Rules)
                if (rule.Re.IsMatch(c) || rule.Re.IsMatch(flat)) { category = rule.Category; return rule.Why; }
            if (TouchesOwnData(flat)) { category = PermissionCategory.Credentials; return "reads or changes MinGent's own settings, chats or browser sign-ins"; }
            if (SecretWatch.Leaks(c) && LooksNetwork(c)) { category = PermissionCategory.Never; return "sends one of your API keys from MinGent's settings over the internet"; }
            string install = InstallOrLaunch(flat, workspace, string.IsNullOrEmpty(cwd) ? workspace : cwd, out category);
            if (install != null) return install;
            category = "";
            string del = DeleteDanger(flat, workspace, string.IsNullOrEmpty(cwd) ? workspace : cwd);
            if (del != null) category = PermissionCategory.Delete;
            return del;
        }

        static readonly Regex Statement = new Regex(@"\r?\n|;|&&|\|\||\||&|\(|\)|\{|\}", RegexOptions.CultureInvariant);
        static readonly Regex RemoteSpec = new Regex(@"(^|\s)['""]?(https?://|npm:|jsr:)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex ProgramDownload = new Regex(
            @"\.(exe|msi|msix|msixbundle|appx|appxbundle|zip|7z|rar|tar|tgz|gz|xz|bz2|zst|whl|nupkg|vsix|jar|ps1|psm1|bat|cmd|vbs|js|msu|cab|dmg|pkg|deb|rpm|appimage|iso|img|vhdx?)(['""?#\s]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex InstallerName = new Regex(@"(^|[\\/])[^\\/]*(setup|install|installer|bootstrapper|redist)[^\\/]*\.exe$|\.(msi|msix|msixbundle|appx|appxbundle|msu)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly HashSet<string> Runtimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "python", "python3", "pythonw", "py", "pyw", "node", "dotnet", "java", "javaw", "deno", "bun", "ruby", "php", "perl", "lua", "luajit",
            "pwsh", "powershell", "cmd", "julia", "rscript", "r" };

        static readonly HashSet<string> DevTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "python", "python3", "pythonw", "py", "node", "npm", "dotnet", "git", "java", "javac", "javaw", "go", "gofmt", "cargo", "rustc", "gcc", "g++", "clang", "clang++",
            "cl", "link", "msbuild", "csc", "vbc", "cmake", "ninja", "make", "mingw32-make", "pwsh", "powershell", "cmd", "deno", "bun", "ruby", "php", "perl", "lua",
            "rscript", "julia", "kotlinc", "scala", "gradle", "gradlew", "mvn", "mvnw", "tsc", "eslint", "prettier", "pytest", "black", "ruff", "mypy", "where", "findstr",
            "certutil", "tar", "curl", "robocopy", "xcopy", "tree", "whoami", "hostname", "ipconfig", "ping", "nslookup", "tasklist", "sort", "more", "fc", "comp", "attrib" };

        static readonly HashSet<string> GuiApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "notepad", "notepad++", "mspaint", "paint", "pbrush", "calc", "calculator", "write", "wordpad", "charmap", "snippingtool", "magnify", "osk", "explorer", "control",
            "mmc", "taskmgr", "msconfig", "msinfo32", "devmgmt", "compmgmt", "eventvwr", "services", "perfmon", "resmon", "ms-settings", "msedge", "chrome", "firefox", "brave",
            "opera", "vivaldi", "iexplore", "code", "code-insiders", "cursor", "windsurf", "devenv", "rider64", "idea64", "pycharm64", "webstorm64", "clion64", "winword", "excel",
            "powerpnt", "outlook", "olk", "onenote", "msaccess", "mspub", "teams", "ms-teams", "slack", "discord", "telegram", "whatsapp", "signal", "zoom", "skype", "spotify",
            "steam", "epicgameslauncher", "vlc", "wmplayer", "obs64", "obs", "gimp", "krita", "blender", "audacity", "photoshop", "acrobat", "acrord32", "notion", "obsidian",
            "figma", "postman", "insomnia", "putty", "winscp", "filezilla", "mstsc", "anydesk", "teamviewer", "wt", "windowsterminal", "powershell_ise", "lmstudio", "ollama app" };

        static string BaseName(string token)
        {
            string t = (token ?? "").Trim().Trim('"', '\'').Trim();
            int slash = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
            if (slash >= 0) t = t.Substring(slash + 1);
            string low = t.ToLowerInvariant();
            foreach (string ext in new string[] { ".exe", ".cmd", ".bat", ".ps1", ".com", ".lnk" })
                if (low.EndsWith(ext, StringComparison.Ordinal)) return low.Substring(0, low.Length - ext.Length);
            return low;
        }

        static void Head(string stmt, out string token, out string rest)
        {
            string s = stmt.TrimStart();
            token = "";
            rest = "";
            if (s.Length == 0) return;
            int end;
            if (s[0] == '"' || s[0] == '\'')
            {
                end = s.IndexOf(s[0], 1);
                if (end < 0) end = s.Length - 1;
                token = s.Substring(1, Math.Max(0, end - 1));
                rest = end + 1 < s.Length ? s.Substring(end + 1).Trim() : "";
                return;
            }
            end = 0;
            while (end < s.Length && !char.IsWhiteSpace(s[end])) end++;
            token = s.Substring(0, end);
            rest = s.Substring(end).Trim();
        }

        static List<string> Words(string rest)
        {
            List<string> l = new List<string>();
            foreach (Match m in Regex.Matches(rest ?? "", @"""([^""]*)""|'([^']*)'|(\S+)"))
                l.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
            return l;
        }

        static string Verb(List<string> w, int n)
        {
            int seen = 0;
            foreach (string x in w)
            {
                if (x == "--") break;
                if (x.Length == 0 || x[0] == '-' || x.IndexOf('\\') >= 0 || x.IndexOf('/') >= 0 || x.StartsWith(".") || Regex.IsMatch(x, @"^\d+$")) continue;
                if (seen++ == n) return x.ToLowerInvariant();
            }
            return "";
        }

        static bool Any(string word, params string[] options)
        {
            foreach (string o in options) if (word == o) return true;
            return false;
        }

        static string PackageInstall(string name, string rest)
        {
            List<string> w = Words(rest);
            string a = Verb(w, 0), b = Verb(w, 1);
            string r = " " + rest.ToLowerInvariant() + " ";
            if (Regex.IsMatch(name, @"^pip[\d.]*$")) return Any(a, "install", "download", "wheel") ? "pip " + a : null;
            if (Regex.IsMatch(name, @"^(python|pythonw|py)[\d.]*$"))
            {
                Match m = Regex.Match(r, @"\s-m\s+(pip|ensurepip|pipx|uv|venv)\b");
                if (!m.Success) return Regex.IsMatch(r, @"\ssetup\.py\s+(install|develop)\b") ? "setup.py install" : null;
                string mod = m.Groups[1].Value;
                List<string> after = Words(r.Substring(m.Index + m.Length));
                string sub = Verb(after, 0);
                if (mod == "pip") return Any(sub, "install", "download", "wheel") ? "pip " + sub : null;
                if (mod == "pipx") return Any(sub, "install", "run", "inject", "upgrade", "upgrade-all", "reinstall", "reinstall-all") ? "pipx " + sub : null;
                if (mod == "uv") return Any(sub, "pip", "add", "sync", "tool", "python", "run") ? "uv " + sub : null;
                if (mod == "venv") return r.IndexOf("--upgrade-deps", StringComparison.Ordinal) >= 0 ? "venv --upgrade-deps" : null;
                return mod;
            }
            switch (name)
            {
                case "npm":
                    if (Any(a, "install", "i", "in", "ins", "inst", "insta", "instal", "isnt", "isnta", "isntal", "isntall", "add", "ci", "clean-install", "ic", "install-clean",
                            "isntall-clean", "install-ci-test", "cit", "update", "up", "upgrade", "udpate", "exec", "x", "create")) return "npm " + a;
                    if (Any(a, "init", "innit") && b.Length > 0 && !b.StartsWith("-")) return "npm init " + b;
                    return null;
                case "npx": case "pnpx": case "bunx": case "uvx": case "dnx": return name;
                case "pnpm":
                    if (a.Length == 0) return w.Count == 0 ? "pnpm install" : null;
                    return Any(a, "install", "i", "add", "update", "up", "upgrade", "dlx", "create") ? "pnpm " + a : null;
                case "yarn":
                    if (a.Length == 0) return w.Count == 0 ? "yarn install" : null;
                    if (a == "global" && b == "add") return "yarn global add";
                    return Any(a, "install", "add", "upgrade", "up", "upgrade-interactive", "dlx", "create") ? "yarn " + a : null;
                case "bun": return Any(a, "install", "i", "add", "a", "update", "upgrade", "x", "create", "c") ? "bun " + a : null;
                case "deno":
                    if (Any(a, "install", "add", "x", "upgrade")) return "deno " + a;
                    return Any(a, "run", "eval", "serve", "compile", "test") && RemoteSpec.IsMatch(rest) ? "deno " + a + " with a package from the internet" : null;
                case "uv":
                    if (a == "pip") return Any(b, "install", "sync") ? "uv pip " + b : null;
                    if (Any(a, "add", "sync")) return "uv " + a;
                    if (a == "tool") return Any(b, "install", "run", "upgrade") ? "uv tool " + b : null;
                    if (a == "python") return b == "install" ? "uv python install" : null;
                    return a == "run" && Regex.IsMatch(r, @"\s--(with|from)\b") ? "uv run --with" : null;
                case "pipx": return Any(a, "install", "run", "inject", "upgrade", "upgrade-all", "reinstall", "reinstall-all", "install-all") ? "pipx " + a : null;
                case "poetry": return Any(a, "install", "add", "update", "self", "sync") ? "poetry " + a : null;
                case "pdm": return Any(a, "install", "add", "update", "sync") ? "pdm " + a : null;
                case "pipenv": return Any(a, "install", "update", "sync", "upgrade") ? "pipenv " + a : null;
                case "conda": case "mamba": case "micromamba":
                    if (Any(a, "install", "create", "update", "upgrade")) return name + " " + a;
                    return a == "env" && Any(b, "create", "update") ? name + " env " + b : null;
                case "easy_install": return name;
                case "dotnet":
                    if (a == "add" && Regex.IsMatch(r, @"\spackage\s")) return "dotnet add package";
                    if (a == "tool") return Any(b, "install", "update", "run", "exec", "restore") ? "dotnet tool " + b : null;
                    if (a == "workload") return Any(b, "install", "update", "restore", "repair") ? "dotnet workload " + b : null;
                    if (a == "new") return Any(b, "install", "-i", "--install", "update") ? "dotnet new " + b : null;
                    return a == "restore" ? "dotnet restore" : null;
                case "nuget": return Any(a, "install", "restore", "update") ? "nuget " + a : null;
                case "cargo": return Any(a, "install", "add", "binstall", "fetch") ? "cargo " + a : null;
                case "rustup": return Any(a, "install", "update", "toolchain", "target", "component", "default", "self") ? "rustup " + a : null;
                case "rustup-init": return name;
                case "go":
                    if (Any(a, "install", "get")) return "go " + a;
                    if (a == "mod" && Any(b, "download", "tidy", "vendor")) return "go mod " + b;
                    return a == "run" && Regex.IsMatch(r, @"\s[\w.\-]+\.[a-z]{2,}/\S*@") ? "go run with a package from the internet" : null;
                case "gem": return Any(a, "install", "update") ? "gem " + a : null;
                case "bundle": case "bundler": return a.Length == 0 || Any(a, "install", "update", "add") ? "bundle " + (a.Length == 0 ? "install" : a) : null;
                case "composer": return Any(a, "install", "require", "update", "global", "create-project") ? "composer " + a : null;
                case "cpan": case "cpanm": return name;
                case "cabal": return Any(a, "install", "v2-install") ? "cabal " + a : null;
                case "stack": case "opam": case "luarocks": case "nimble": return a == "install" ? name + " install" : null;
                case "ghcup": return Any(a, "install", "upgrade", "set") ? "ghcup " + a : null;
                case "mix": return Any(a, "deps.get", "archive.install", "escript.install", "local.hex", "local.rebar") ? "mix " + a : null;
                case "dart": case "flutter":
                    if (a == "pub") return Any(b, "get", "add", "upgrade", "global", "downgrade") ? name + " pub " + b : null;
                    return name == "flutter" && Any(a, "upgrade", "precache", "downgrade") ? "flutter " + a : null;
                case "pod": return Any(a, "install", "update") ? "pod " + a : null;
                case "vcpkg": case "conan": return a == "install" ? name + " install" : null;
                case "winget": return Any(a, "install", "add", "upgrade", "update", "import", "configure") || (a == "source" && b == "add") ? "winget " + a : null;
                case "choco": return Any(a, "install", "upgrade", "update") ? "choco " + a : null;
                case "cinst": case "cup": return name;
                case "scoop": return Any(a, "install", "update", "bucket", "reset") ? "scoop " + a : null;
                case "dism": return Regex.IsMatch(r, @"/(add-package|enable-feature|add-capability|add-provisionedappxpackage|add-driver)\b") ? "dism" : null;
                case "msiexec": return Regex.IsMatch(r, @"\s/(i|package|a|update|p|j[mu]?)\b") ? "msiexec" : null;
                case "wsl": return Regex.IsMatch(r, @"\s--(install|update|import)\b") ? "wsl --install" : null;
                case "code": case "code-insiders": case "cursor": case "windsurf": return Regex.IsMatch(r, @"\s--install-extension\b") ? name + " --install-extension" : null;
                case "gh": return a == "extension" && Any(b, "install", "upgrade") ? "gh extension " + b : null;
                case "nvm": case "fnm": case "volta": case "pyenv": case "sdk": case "mise": case "proto": case "n":
                    return Any(a, "install", "use", "fetch", "i") ? name + " " + a : null;
                case "asdf": return a == "install" || (a == "plugin" && b == "add") ? "asdf " + a : null;
                case "corepack": return Any(a, "enable", "prepare", "install", "use", "up") ? "corepack " + a : null;
                case "docker": case "podman":
                    if (Any(a, "run", "pull", "create", "build", "buildx")) return name + " " + a;
                    return a == "compose" && Any(b, "up", "pull", "run", "create", "build") ? name + " compose " + b : null;
                case "docker-compose": return Any(a, "up", "pull", "run", "create", "build") ? "docker-compose " + a : null;
                case "rscript": case "r":
                    return Regex.IsMatch(r, @"install\.packages|remotes::install|devtools::install|biocmanager::install|pak::pkg_install") ? "install.packages" : null;
                case "julia": return Regex.IsMatch(r, @"pkg\.(add|instantiate|update|develop)") ? "Pkg.add" : null;
                case "install-module": case "install-package": case "install-script": case "update-module": case "update-script": case "save-module": case "save-package":
                case "save-script": case "install-psresource": case "update-psresource": case "save-psresource": case "install-packageprovider": case "install-windowsfeature":
                case "enable-windowsoptionalfeature": case "add-windowscapability": case "add-windowspackage": case "add-appxpackage": case "add-apppackage":
                case "add-appxprovisionedpackage": case "add-windowsdriver":
                    return name;
            }
            return null;
        }

        static string LaunchTarget(List<string> w)
        {
            for (int i = 0; i < w.Count; i++)
            {
                string x = w[i], lx = x.ToLowerInvariant();
                if (lx == "-filepath" || lx == "-path" || lx == "-literalpath") return i + 1 < w.Count ? w[i + 1] : null;
                if (lx.StartsWith("-filepath:") || lx.StartsWith("-path:")) return x.Substring(x.IndexOf(':') + 1);
                if (lx == "/d" || lx == "-workingdirectory" || lx == "-argumentlist" || lx == "-args" || lx == "-windowstyle" || lx == "-verb" || lx == "-credential") { i++; continue; }
                if (x.Length == 0 && i == 0) continue;
                if (lx.StartsWith("/") && lx.Length <= 12 && Regex.IsMatch(lx, @"^/[a-z]+$")) continue;
                if (lx.StartsWith("-") && Regex.IsMatch(lx, @"^-[a-z]+$")) continue;
                return x;
            }
            return null;
        }

        static string InstallOrLaunch(string c, string workspace, string cwd, out string category)
        {
            category = PermissionCategory.Install;
            foreach (string raw in Statement.Split(c))
            {
                string s = raw.Trim().TrimStart('@').Trim();
                for (int guard = 0; guard < 4; guard++)
                {
                    Match pre = Regex.Match(s, @"^(call|cmd(\.exe)?\s+/[ckr]|start-job\s+-scriptblock|invoke-command\s+-scriptblock)\s+", RegexOptions.IgnoreCase);
                    if (!pre.Success) break;
                    s = s.Substring(pre.Length).TrimStart().Trim('"').TrimStart();
                }
                if (s.Length == 0) continue;
                string token, rest;
                Head(s, out token, out rest);
                string name = BaseName(token);
                if (name.Length == 0) continue;
                string pkg = PackageInstall(name, rest);
                if (pkg != null)
                {
                    bool run = name == "npx" || name == "pnpx" || name == "bunx" || name == "uvx" || name == "dnx" || pkg.EndsWith(" x") || pkg.EndsWith(" dlx") || pkg.EndsWith(" exec") ||
                               pkg.EndsWith(" create") || pkg.StartsWith("npm init") || pkg.IndexOf(" run", StringComparison.Ordinal) > 0 || pkg.IndexOf("from the internet", StringComparison.Ordinal) > 0 ||
                               name == "docker" || name == "podman" || name == "docker-compose";
                    return run ? "downloads and runs a package or program (" + pkg + ")"
                               : "installs or updates software, a runtime or packages (" + pkg + ")";
                }
                if (name == "explorer") { category = PermissionCategory.Launch; return "opens a File Explorer window"; }
                if (name == "start-process" || name == "saps" || name == "start" || name == "invoke-item" || name == "ii")
                {
                    if (name == "start" && Regex.IsMatch(rest, @"^-?(sleep|transcript|job|service|bitstransfer)\b", RegexOptions.IgnoreCase)) continue;
                    string target = LaunchTarget(Words(rest));
                    if (string.IsNullOrEmpty(target)) continue;
                    string why = LaunchDanger(target, workspace, cwd, name == "invoke-item" || name == "ii");
                    if (why != null) { category = why.StartsWith("runs an installer", StringComparison.Ordinal) ? PermissionCategory.Install : PermissionCategory.Launch; return why; }
                    continue;
                }
                if (GuiApps.Contains(name)) { category = PermissionCategory.Launch; return "opens the desktop program " + name + ""; }
                bool pathy = token.IndexOf('\\') >= 0 || token.IndexOf('/') >= 0 || token.StartsWith("$env:", StringComparison.OrdinalIgnoreCase) || token.StartsWith("%");
                if (pathy && Regex.IsMatch(token.Trim('"', '\''), @"\.(exe|com|bat|cmd|ps1|vbs|msc|cpl|scr|lnk|msi|msix|appx)$", RegexOptions.IgnoreCase))
                {
                    if (InstallerName.IsMatch(token.Trim('"', '\''))) return "runs an installer";
                    bool unknown;
                    string full = Full(token, cwd, out unknown);
                    category = PermissionCategory.Launch;
                    if (unknown) return "starts a program whose location comes from a variable, so it cannot be checked";
                    if (full != null && !Inside(full, workspace) && !DevTools.Contains(name))
                    {
                        string win = "";
                        try { win = Environment.GetFolderPath(Environment.SpecialFolder.Windows); } catch { }
                        if (win.Length == 0 || !AgentPaths.IsInside(full, win) || GuiApps.Contains(name))
                            return "starts a program from outside the work folder (" + Path.GetFileName(full) + ")";
                    }
                    category = PermissionCategory.Install;
                }
            }
            Match mp = Regex.Match(c, @"\s-m\s+(pip\s+(\S+\s+)*?(install|download|wheel)|ensurepip)\b", RegexOptions.IgnoreCase);
            if (mp.Success) return "installs or updates software, a runtime or packages (" + Regex.Replace(mp.Value.Trim(), @"\s+", " ") + ")";
            if (LooksNetwork(c) && ProgramDownload.IsMatch(c)) return "downloads a program, installer, script or archive from the internet";
            if (Regex.IsMatch(c, @"\b(downloadfile|downloadfiletaskasync)\s*\(", RegexOptions.IgnoreCase) && ProgramDownload.IsMatch(c))
                return "downloads a program, installer, script or archive from the internet";
            return null;
        }

        static string LaunchDanger(string target, string workspace, string cwd, bool opener)
        {
            string t = target.Trim().Trim('"', '\'').Trim();
            if (t.Length == 0) return null;
            if (Regex.IsMatch(t, @"^[a-z][\w+.\-]+:", RegexOptions.IgnoreCase))
                return "opens " + (t.Length > 60 ? t.Substring(0, 60) + "..." : t) + " in another program (use the built-in browser for web pages)";
            if (InstallerName.IsMatch(t)) return "runs an installer";
            string name = BaseName(t);
            bool pathy = t.IndexOf('\\') >= 0 || t.IndexOf('/') >= 0 || t.StartsWith(".") || t.StartsWith("$") || t.StartsWith("%");
            if (!pathy)
            {
                if (Runtimes.Contains(name) && !opener) return null;
                bool localFile = false;
                try { localFile = !string.IsNullOrEmpty(cwd) && File.Exists(Path.Combine(cwd, t)); } catch { }
                if (!localFile) return "starts the program " + name + "";
            }
            bool unknown;
            string full = Full(t, cwd, out unknown);
            if (unknown) return "opens something whose location comes from a variable, so it cannot be checked";
            if (full == null) return null;
            bool runnable = Regex.IsMatch(full, @"\.(exe|com|bat|cmd|ps1|py|pyw|js|jar)$", RegexOptions.IgnoreCase);
            if (!Inside(full, workspace))
                return runnable ? "starts a program from outside the work folder (" + Path.GetFileName(full) + ")"
                                : "opens " + Path.GetFileName(full.TrimEnd('\\')) + " from outside the work folder with another program";
            if (!runnable) return "opens " + Path.GetFileName(full.TrimEnd('\\')) + " with another program (for web pages, use the built-in browser)";
            return null;
        }

        static string Flatten(string c)
        {
            return c.Replace("^", "").Replace("`", "").Replace("\"\"", "").Replace("''", "");
        }

        static bool TouchesOwnData(string c)
        {
            string l = c.ToLowerInvariant().Replace('/', '\\');
            string dd = Program.DataDir;
            if (!string.IsNullOrEmpty(dd) && l.Contains(dd.ToLowerInvariant().Replace('/', '\\').TrimEnd('\\'))) return true;
            if (Regex.IsMatch(l, @"(localappdata%?|env:localappdata\}?|appdata\\+local)\\+(mingent|justpad)\b")) return true;
            return l.Contains("localapplicationdata") && (l.Contains("mingent") || l.Contains("justpad"));
        }

        static string DeleteDanger(string c, string workspace, string cwd)
        {
            foreach (Match m in DeleteCmd.Matches(c))
            {
                string rest = m.Groups[2].Value;
                bool recursive = false, computed = false;
                List<string> paths = new List<string>();
                foreach (string a in Args(rest))
                {
                    if (a.Length > 1 && (a[0] == '-' || (a[0] == '/' && Regex.IsMatch(a, @"^/[a-z](:\S*)?$", RegexOptions.IgnoreCase))))
                    {
                        string name = a.TrimStart('-', '/').ToLowerInvariant();
                        int colon = name.IndexOf(':');
                        if (colon >= 0)
                        {
                            string v = a.Substring(a.IndexOf(':') + 1);
                            name = name.Substring(0, colon);
                            if (v.Length > 0 && !v.StartsWith("$") || v.StartsWith("$env:", StringComparison.OrdinalIgnoreCase) || v.StartsWith("${env:", StringComparison.OrdinalIgnoreCase) || v.StartsWith("$home", StringComparison.OrdinalIgnoreCase))
                                if (!(v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("false", StringComparison.OrdinalIgnoreCase))) paths.Add(v);
                        }
                        if (name.Length > 0 && ("recurse".StartsWith(name) || name == "s" || Regex.IsMatch(name, "^[rfvdi]*r[rfvdi]*$"))) recursive = true;
                        continue;
                    }
                    paths.Add(a);
                }
                string before = c.Substring(0, m.Index).TrimEnd();
                bool piped = before.EndsWith("|") && !before.EndsWith("||");
                if (recursive && piped) return "deletes files picked by an earlier command in a pipeline, so they cannot be checked";
                if (WildRoot.IsMatch(rest)) return "deletes a whole drive, your user folder or a system folder";
                foreach (string p in paths)
                {
                    bool unknown;
                    string full = Full(p, cwd, out unknown);
                    if (unknown) { computed = true; continue; }
                    if (full == null) continue;
                    if (Broad(full)) return "deletes a whole drive, your user folder or a system folder";
                    int slash = full.LastIndexOf('\\');
                    string tail = slash >= 0 ? full.Substring(slash + 1) : full;
                    bool all = tail == "*" || tail == "*.*";
                    string dir = all && slash > 0 ? full.Substring(0, slash) : full;
                    if ((all || p.Trim('"', '\'') == ".") && !string.IsNullOrEmpty(workspace) && string.Equals(dir.TrimEnd('\\'), workspace.Replace('/', '\\').TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        return "deletes everything in the work folder";
                    if (!Inside(full, workspace))
                    {
                        if (recursive) return "deletes folders outside the work folder";
                        if (p.IndexOfAny(new char[] { '*', '?' }) >= 0) return "deletes files outside the work folder";
                    }
                }
                if (computed && recursive && !Regex.IsMatch(rest, @"^\s*\$(_|psitem)\b", RegexOptions.IgnoreCase))
                    return "deletes a folder whose location comes from a variable, so it cannot be checked";
                if (computed || piped)
                {
                    string why = Outside(c, workspace, cwd);
                    if (why != null) return "deletes files that are picked while it runs, and " + why;
                }
            }
            foreach (Match m in NetDelete.Matches(c))
            {
                string args = m.Groups[2].Value;
                bool recursive = m.Groups[1].Value.Equals("deletedirectory", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(args, @"\$?\btrue\b", RegexOptions.IgnoreCase);
                bool computed = false, any = false;
                foreach (Match q in Regex.Matches(args, @"""([^""]*)""|'([^']*)'"))
                {
                    any = true;
                    bool unknown;
                    string full = Full(q.Groups[1].Success ? q.Groups[1].Value : q.Groups[2].Value, cwd, out unknown);
                    if (unknown || full == null) { computed = true; continue; }
                    if (Broad(full)) return "deletes a whole drive, your user folder or a system folder";
                    if (recursive && !Inside(full, workspace)) return "deletes folders outside the work folder";
                }
                if (!any && recursive) return "deletes a folder whose location comes from a variable, so it cannot be checked";
                if (!any || computed)
                {
                    string why = Outside(c, workspace, cwd);
                    if (why != null) return "deletes files that are picked while it runs, and " + why;
                }
            }
            if (ScriptDelete.IsMatch(c))
            {
                if (ScriptHome.IsMatch(c)) return "deletes files in your user folder from a script, so the target cannot be checked";
                string why = Outside(c, workspace, cwd);
                if (why != null) return "deletes files that are picked while it runs, and " + why;
            }
            foreach (Match m in OtherDelete.Matches(c))
            {
                bool recursive = Regex.IsMatch(m.Groups[1].Value, @"\$?\btrue\b", RegexOptions.IgnoreCase);
                string rest = Regex.Replace(c, @"\$(true|false|null|_|psitem)\b", "", RegexOptions.IgnoreCase);
                if (recursive && rest.IndexOf('$') >= 0) return "deletes a folder whose location comes from a variable, so it cannot be checked";
                string why = Outside(c, workspace, cwd);
                if (why != null) return "deletes files that are picked while it runs, and " + why;
            }
            if (VarCall.IsMatch(c) && Recurse.IsMatch(c))
            {
                string why = Outside(c, workspace, cwd);
                if (why != null) return "runs a command whose name comes from a variable, and " + why;
            }
            return null;
        }

        static List<string> Args(string rest)
        {
            List<string> l = new List<string>();
            foreach (Match a in Regex.Matches(rest, @"""([^""]*)""|'([^']*)'|([^\s,'""(){}]+)"))
                l.Add(a.Groups[1].Success ? a.Groups[1].Value : a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value);
            return l;
        }

        static string Outside(string c, string workspace, string cwd)
        {
            if (!Inside(cwd, workspace)) return "it runs outside the work folder";
            foreach (Match m in AbsPath.Matches(c))
            {
                bool unknown;
                string full = Full(m.Value, cwd, out unknown);
                if (unknown) return "the command uses a folder that cannot be checked";
                if (full != null && !Inside(full, workspace)) return "the command points outside the work folder";
            }
            foreach (Match m in DotPath.Matches(c))
            {
                bool unknown;
                string full = Full(m.Value, cwd, out unknown);
                if (!unknown && full != null && !Inside(full, workspace)) return "the command points outside the work folder";
            }
            return null;
        }

        static bool Inside(string full, string workspace)
        {
            return !string.IsNullOrEmpty(workspace) && !string.IsNullOrEmpty(full) && AgentPaths.IsInside(full, workspace);
        }

        static string Full(string token, string cwd, out bool unknown)
        {
            unknown = false;
            string s = (token ?? "").Trim().Trim('"', '\'').Trim();
            if (s.Length == 0 || Regex.IsMatch(s, @"^[a-z][\w+.-]*://", RegexOptions.IgnoreCase)) return null;
            if (Regex.IsMatch(s, @"^(\$\{?env:\w+\}?|%\w+%|\$\{?home\}?|~)([\\/]|$)", RegexOptions.IgnoreCase))
            {
                string e = s;
                Match env = Regex.Match(e, @"^\$\{?env:(\w+)\}?", RegexOptions.IgnoreCase);
                if (env.Success)
                {
                    string v = Environment.GetEnvironmentVariable(env.Groups[1].Value);
                    if (v == null) { unknown = true; return null; }
                    e = v + e.Substring(env.Length);
                }
                Match home = Regex.Match(e, @"^(\$\{?home\}?|~)(?=[\\/]|$)", RegexOptions.IgnoreCase);
                if (home.Success)
                {
                    string h = "";
                    try { h = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
                    if (h.Length == 0) { unknown = true; return null; }
                    e = h + e.Substring(home.Length);
                }
                s = Environment.ExpandEnvironmentVariables(e);
            }
            if (s.IndexOf('$') >= 0 || Regex.IsMatch(s, @"%\w+%|^%%?~?[a-z]*[a-z](?![\w%])|%%?~", RegexOptions.IgnoreCase)) { unknown = true; return null; }
            s = s.Replace('/', '\\');
            bool unc = s.StartsWith("\\\\");
            s = Regex.Replace(s, @"\\{2,}", "\\");
            if (unc) s = "\\" + s;
            string baseDir = (cwd ?? "").Replace('/', '\\').TrimEnd('\\');
            if (Regex.IsMatch(s, @"^[a-z]:\\", RegexOptions.IgnoreCase) || unc) { }
            else if (Regex.IsMatch(s, @"^[a-z]:", RegexOptions.IgnoreCase)) s = s.Substring(0, 2) + "\\" + s.Substring(2);
            else if (s.StartsWith("\\")) s = (baseDir.Length >= 2 && baseDir[1] == ':' ? baseDir.Substring(0, 2) : "") + s;
            else s = baseDir + "\\" + s;
            return Dots(s);
        }

        static bool Broad(string full)
        {
            string f = full.TrimEnd('*', '?', '.').TrimEnd('\\');
            if (Regex.IsMatch(f, @"^[a-z]:$", RegexOptions.IgnoreCase) || f.Length == 0) return true;
            if (Regex.IsMatch(f, @"^\\\\[^\\]+(\\[^\\]+)?$")) return true;
            List<string> roots = new List<string>();
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                roots.Add(home);
                roots.Add(System.IO.Path.GetDirectoryName(home.TrimEnd('\\', '/')) ?? "");
                foreach (Environment.SpecialFolder sf in new Environment.SpecialFolder[] {
                    Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles,
                    Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
                    roots.Add(Environment.GetFolderPath(sf));
                roots.Add(System.IO.Path.Combine(home, "Downloads"));
                roots.Add(System.IO.Path.Combine(home, "OneDrive"));
            }
            catch { }
            foreach (string r in roots)
                if (!string.IsNullOrEmpty(r) && r.Length > 3 && string.Equals(f, r.Replace('/', '\\').TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string Dots(string path)
        {
            List<string> outp = new List<string>();
            foreach (string part in path.Replace('/', '\\').Split('\\'))
            {
                if (part == ".") continue;
                if (part == "..") { if (outp.Count > 1) outp.RemoveAt(outp.Count - 1); continue; }
                outp.Add(part);
            }
            return string.Join("\\", outp.ToArray());
        }

        public static bool NeedsInternet(string command)
        {
            string c = command ?? "";
            if (LooksNetwork(c)) return true;
            if (Regex.IsMatch(c, @"\b(certutil(\.exe)?\s+.*-urlcache|bitsadmin(\.exe)?\s+.*/transfer|webrequest|urllib|requests\.(get|post)|fetch\s*\(|xmlhttp|msxml2|winhttp)", RegexOptions.IgnoreCase)) return true;
            foreach (Match m in Regex.Matches(c, @"\b(https?|wss?|ftp)://[^\s'""`<>|]+", RegexOptions.IgnoreCase))
                if (!Net.IsLocal(m.Value)) return true;
            foreach (Match m in Regex.Matches(c, @"(^|[\s'""(=])\\\\([A-Za-z0-9.\-]+)\\"))
                if (!Net.IsLocal("file://" + m.Groups[2].Value + "/")) return true;
            if (Regex.IsMatch(c, @"\bgit(\.exe)?\s+((-[Cc]\s+(""[^""]*""|'[^']*'|\S+)|--?\S+)\s+)*(clone|fetch|pull|push|ls-remote|submodule\s+update|remote\s+update)\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(c, @"\s-m\s+(pip\s+(\S+\s+)*?(install|download|wheel)|ensurepip)\b", RegexOptions.IgnoreCase)) return true;
            foreach (string raw in Statement.Split(c))
            {
                string s = raw.Trim().TrimStart('@').Trim();
                if (s.Length == 0) continue;
                string token, rest;
                Head(s, out token, out rest);
                string name = BaseName(token);
                if (name.Length > 0 && PackageInstall(name, rest) != null) return true;
            }
            return false;
        }

        public static bool LooksNetwork(string command)
        {
            return Regex.IsMatch(command ?? "", @"\b(invoke-webrequest|iwr|invoke-restmethod|irm|curl|wget|ssh|scp|sftp|ftp|telnet|nc|ncat|net\.webclient|httpclient|start-bitstransfer)\b|\bgit\s+clone\b", RegexOptions.IgnoreCase);
        }
    }
}
