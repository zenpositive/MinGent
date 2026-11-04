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
        public bool Background, Sandboxed, GitWarned;
        public string SandboxNote, GitWatch;
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
            return Start(command, shell, cwd, background, null, out error);
        }

        static readonly string[] PlainEnv = { "PYTHONUNBUFFERED", "1", "PYTHONIOENCODING", "utf-8", "NO_COLOR", "1", "TERM", "dumb", "CI", "1", "GIT_TERMINAL_PROMPT", "0", "npm_config_yes", "true", "PIP_NO_INPUT", "1" };
        static readonly string[] HiddenEnv = { "ANTHROPIC_API_KEY", "OPENAI_API_KEY" };

        public static ShellJob Start(string command, string shell, string cwd, bool background, Dictionary<string, string> env, out string error)
        {
            bool lowFailed;
            return Start(command, shell, cwd, background, env, false, out error, out lowFailed);
        }

        public static ShellJob Start(string command, string shell, string cwd, bool background, Dictionary<string, string> env, bool low, out string error, out bool lowFailed)
        {
            error = null;
            lowFailed = false;
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
                j.ScriptFile = Path.Combine(dir, "cmd-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".ps1");
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
            if (low) return StartLow(j, psi.FileName, psi.Arguments, cwd, env, out error, out lowFailed);
            for (int i = 0; i + 1 < PlainEnv.Length; i += 2) psi.EnvironmentVariables[PlainEnv[i]] = PlainEnv[i + 1];
            foreach (string k in HiddenEnv)
                if (psi.EnvironmentVariables.ContainsKey(k)) psi.EnvironmentVariables.Remove(k);
            if (env != null) foreach (KeyValuePair<string, string> kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
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
            j.Job = MakeJob(p.Handle);
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

        static ShellJob StartLow(ShellJob j, string exe, string args, string cwd, Dictionary<string, string> env, out string error, out bool lowFailed)
        {
            error = null;
            lowFailed = false;
            Dictionary<string, string> all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
            {
                string k = de.Key as string;
                if (!string.IsNullOrEmpty(k)) all[k] = de.Value as string ?? "";
            }
            for (int i = 0; i + 1 < PlainEnv.Length; i += 2) all[PlainEnv[i]] = PlainEnv[i + 1];
            foreach (string k in HiddenEnv) all.Remove(k);
            if (env != null) foreach (KeyValuePair<string, string> kv in env) all[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, string> kv in Sandbox.Env()) all[kv.Key] = kv.Value;
            int pid;
            IntPtr process, thread;
            Stream output, errors;
            string why = Sandbox.Start(exe, args, cwd, all, out pid, out process, out thread, out output, out errors);
            if (why != null)
            {
                error = why;
                lowFailed = true;
                Cleanup(j);
                return null;
            }
            try { j.Proc = Process.GetProcessById(pid); }
            catch
            {
                Sandbox.Abandon(process, thread);
                try { output.Dispose(); } catch { }
                try { errors.Dispose(); } catch { }
                error = "the sandboxed command stopped before MinGent could watch it";
                lowFailed = true;
                Cleanup(j);
                return null;
            }
            j.Job = MakeJob(process);
            j.Sandboxed = true;
            Sandbox.Resume(thread);
            Pump(output, j);
            Pump(errors, j);
            lock (jobs) { j.Id = nextId++; jobs.Add(j); }
            Thread waiter = new Thread(delegate ()
            {
                int code = Sandbox.Wait(process);
                j.ExitCode = code;
                Thread.Sleep(150);
                j.Exited = true;
                Cleanup(j);
            });
            waiter.IsBackground = true;
            waiter.Start();
            return j;
        }

        static IntPtr MakeJob(IntPtr process)
        {
            if (!IsWindows || process == IntPtr.Zero) return IntPtr.Zero;
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
                AssignProcessToJobObject(job, process);
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

        static bool Orphan(string script)
        {
            Match m = Regex.Match(Path.GetFileName(script), @"^cmd-(\d+)-");
            if (!m.Success) return false;
            int pid = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            Process me = Process.GetCurrentProcess();
            if (pid == me.Id) return false;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    return !string.Equals(p.ProcessName, me.ProcessName, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return true; }
            catch (Exception) { return false; }
        }

        public static void SweepScripts()
        {
            foreach (string name in new string[] { "MinGent", "JustPad" })
            {
                string dir = Path.Combine(Path.GetTempPath(), name);
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir, "cmd-*.ps1"))
                {
                    try { if (Orphan(f) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(f)).TotalHours >= 24) SecureFile.Erase(f); } catch { }
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
                { @"\[\s*(system\s*\.\s*)?diagnostics\s*\.\s*process\s*\]\s*::\s*start\s*\(", "opens a program, file or address with Process.Start (for web pages, use the built-in browser)", PermissionCategory.Launch },
                { @"(^|\s)-m\s+webbrowser\b|\bwebbrowser\s*\.\s*(open|open_new|open_new_tab)\s*\(", "opens a web page in another browser (use the built-in browser)", PermissionCategory.Launch },
                { @"\brundll32(\.exe)?\s+(url(\.dll)?\s*,\s*(fileprotocolhandler|openurl)|shell32(\.dll)?\s*,\s*shellexec_rundll)\b", "opens an address or file with another program (for web pages, use the built-in browser)", PermissionCategory.Launch },
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
            string opener = null;
            foreach (Rule rule in Rules)
                if (rule.Re.IsMatch(c) || rule.Re.IsMatch(flat))
                {
                    if (rule.Category == PermissionCategory.Launch) { if (opener == null) opener = rule.Why; continue; }
                    category = rule.Category;
                    return rule.Why;
                }
            if (TouchesOwnData(flat)) { category = PermissionCategory.Credentials; return "reads or changes MinGent's own settings, chats or browser sign-ins"; }
            if (SecretWatch.Leaks(c) && LooksNetwork(c)) { category = PermissionCategory.Never; return "sends one of your API keys from MinGent's settings over the internet"; }
            string dir = string.IsNullOrEmpty(cwd) ? workspace : cwd;
            string launchCat;
            string install = InstallOrLaunch(flat, workspace, dir, out launchCat);
            if (install != null && launchCat != PermissionCategory.Launch) { category = launchCat; return install; }
            string del = DeleteDanger(flat, workspace, dir);
            if (del != null) { category = PermissionCategory.Delete; return del; }
            if (install != null) { category = PermissionCategory.Launch; return install; }
            if (opener != null) { category = PermissionCategory.Launch; return opener; }
            return null;
        }

        static readonly Regex ServerSecret = new Regex(
            @"(^|[\s'""/=~:])\.(ssh|aws|azure|kube|gnupg|docker|git-credentials|npmrc|pypirc|netrc|pgpass|my\.cnf|env)(\b|/)|\bid_(rsa|ed25519|ecdsa|dsa)\b|/etc/g?shadow\b|/etc/ssl/private\b|\bprivkey\d*\.pem\b|\bprivate_key\.json\b|\.(key|p12|pfx|jks|keystore)\b|\b(wp-config\.php|debian\.cnf|credentials\.json)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string ServerDanger(string command, out string category)
        {
            category = "";
            string c = command ?? "";
            if (SecretWatch.Leaks(c)) { category = PermissionCategory.Never; return "sends one of your API keys from MinGent's settings to the server"; }
            if (ServerSecret.IsMatch(c) || ServerSecret.IsMatch(Flatten(c))) { category = PermissionCategory.Credentials; return "reads or changes password, key or token files"; }
            return null;
        }

        static readonly Regex Rebinds = new Regex(
            @"\b(set|new)-alias\b|\bset-psreadline(keyhandler|option)\b|\$executioncontext\b|\$(function|alias):|\$env:\w+\s*\+?=(?!=)|\b(set|new|remove|rename|copy)-item\b[^\n]*\b(function|alias|env|variable):|\bprintf\s+(-\S+\s+)*-v\b" +
            @"|(^|[;|&({]\s*)(sudo\s+)?((sal|nal|doskey|alias|unalias|hash|enable|bind|bindkey|trap|complete|compdef|zle|shopt|setopt|typeset|declare|readonly|eval|exec|source|fc|stty|import-module|ipmo)(\s|$)" +
            @"|(function|filter)\s+[\w:.-]+|[A-Za-z_][\w-]*\s*\(\s*\)|\.\s+\S|set\s+[-+]o\b|(export\s+)?(path|pathext|prompt_command|ps[0-4]|bash_env|env|ifs|cdpath|ld_preload|ld_library_path)=|set\s+(path|pathext|comspec|prompt)=|path\s+[^\s=])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool ChangesShell(string command)
        {
            return Rebinds.IsMatch(command ?? "");
        }

        public static bool OpensWindow(string reason)
        {
            return reason != null && reason.StartsWith("opens ", StringComparison.Ordinal);
        }

        public static string WindowReason(string command, string workspace, string cwd)
        {
            string c = command ?? "";
            string flat = Flatten(c);
            foreach (Rule rule in Rules)
                if (rule.Category == PermissionCategory.Launch && OpensWindow(rule.Why) && (rule.Re.IsMatch(c) || rule.Re.IsMatch(flat))) return rule.Why;
            string dir = string.IsNullOrEmpty(cwd) ? workspace : cwd;
            string[] parts = StatementKeep.Split(flat);
            for (int i = 0; i < parts.Length; i += 2)
            {
                string cat;
                string why = OneStatement(parts, i, workspace, dir, out cat);
                if (why != null && cat == PermissionCategory.Launch && OpensWindow(why)) return why;
            }
            return null;
        }

        static readonly Regex Statement = new Regex(@"\r?\n|;|&&|\|\||\||&|\(|\)|\{|\}", RegexOptions.CultureInvariant);
        static readonly Regex StatementKeep = new Regex(@"(\r?\n|;|&&|\|\||\||&|\(|\)|\{|\})", RegexOptions.CultureInvariant);
        static readonly Regex Document = new Regex(
            @"\.(html?|xhtml|mhtml?|svg|pdf|txt|md|markdown|rtf|log|csv|tsv|json|xml|ya?ml|ini|cfg|conf|docx?|xlsx?|pptx?|od[tsp]|png|jpe?g|gif|bmp|webp|ico|tiff?|mp[34]|m4a|wav|flac|ogg|avi|mkv|mov|webm|url|zip|7z|rar)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex GuiExe = new Regex(@"([\w.+-]+)\.exe\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly HashSet<string> Browsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "brave", "vivaldi", "opera", "firefox" };
        static readonly HashSet<string> EditorClis = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "code", "code-insiders", "cursor", "windsurf" };

        static string Windowless(string name, string token, string args)
        {
            string r = " " + (args ?? "").ToLowerInvariant().Replace('\'', ' ').Replace('"', ' ').Replace(',', ' ') + " ";
            if (EditorClis.Contains(name) && Regex.IsMatch(r, @"\s(--version|-v|--help|-h|--list-extensions|--show-versions)\s")) return "info";
            if (name == "devenv" && (token ?? "").Trim('"', '\'').EndsWith(".com", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(r, @"\s[/-](build|rebuild|clean|deploy|upgrade)\s")) return "info";
            if (Browsers.Contains(name) && Regex.IsMatch(r, @"\s--headless(=\S*)?\s")) return "headless";
            if (name == "blender" && Regex.IsMatch(r, @"\s(-b|--background)\s")) return "headless";
            return null;
        }
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
            string[] parts = StatementKeep.Split(c);
            string launch = null;
            for (int i = 0; i < parts.Length; i += 2)
            {
                string cat;
                string why = OneStatement(parts, i, workspace, cwd, out cat);
                if (why == null) continue;
                if (cat != PermissionCategory.Launch) { category = cat; return why; }
                if (launch == null) launch = why;
            }
            category = PermissionCategory.Install;
            Match mp = Regex.Match(c, @"\s-m\s+(pip\s+(\S+\s+)*?(install|download|wheel)|ensurepip)\b", RegexOptions.IgnoreCase);
            if (mp.Success) return "installs or updates software, a runtime or packages (" + Regex.Replace(mp.Value.Trim(), @"\s+", " ") + ")";
            if (LooksNetwork(c) && ProgramDownload.IsMatch(c)) return "downloads a program, installer, script or archive from the internet";
            if (Regex.IsMatch(c, @"\b(downloadfile|downloadfiletaskasync)\s*\(", RegexOptions.IgnoreCase) && ProgramDownload.IsMatch(c))
                return "downloads a program, installer, script or archive from the internet";
            if (launch != null) category = PermissionCategory.Launch;
            return launch;
        }

        static readonly Regex RunsString = new Regex(@"(\bcmd(\.exe)?\s+/[ckr]|\s-(c|command)|\b(iex|invoke-expression))\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static bool InsideString(string before)
        {
            char open = '\0';
            bool runs = false;
            for (int k = 0; k < before.Length; k++)
            {
                char ch = before[k];
                if (open == '\0')
                {
                    if (ch == '"' || ch == '\'') { open = ch; runs = RunsString.IsMatch(before.Substring(0, k)); }
                }
                else if (ch == open) { open = '\0'; runs = false; }
            }
            return open != '\0' && !runs;
        }

        static string OneStatement(string[] parts, int i, string workspace, string cwd, out string category)
        {
            string why = Examine(parts, i, workspace, cwd, out category);
            if (why != null && category == PermissionCategory.Launch && i > 0 && parts[i - 1] != ")" && InsideString(string.Join("", parts, 0, i))) return null;
            return why;
        }

        static string Examine(string[] parts, int i, string workspace, string cwd, out string category)
        {
            category = PermissionCategory.Install;
            string prev = i > 0 ? parts[i - 1] : "", next = i + 1 < parts.Length ? parts[i + 1] : "";
            string s = parts[i].Trim().TrimStart('@').Trim();
            bool viaCmd = false, assigned = false;
            for (int guard = 0; guard < 5; guard++)
            {
                Match pre = Regex.Match(s, @"^(?:(call|cmd(\.exe)?\s+/[ckr]|start-job\s+-scriptblock|invoke-command\s+-scriptblock)\s+|\$[\w:]+\s*=(?!=)\s*)", RegexOptions.IgnoreCase);
                if (!pre.Success || pre.Length == 0) break;
                string p = pre.Value.ToLowerInvariant();
                assigned = p.StartsWith("$");
                if (p.StartsWith("call") || p.StartsWith("cmd")) viaCmd = true;
                s = s.Substring(pre.Length).TrimStart();
                if (!assigned) s = s.Trim('"').TrimStart();
            }
            if (s.Length == 0) return null;
            bool quoted = s[0] == '"' || s[0] == '\'';
            if (assigned && quoted) return null;
            string token, rest;
            Head(s, out token, out rest);
            string name = BaseName(token);
            if (name.Length == 0) return null;
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
                if (name == "start" && Regex.IsMatch(rest, @"^-?(sleep|transcript|job|service|bitstransfer)\b", RegexOptions.IgnoreCase)) return null;
                string target = LaunchTarget(Words(rest));
                if (string.IsNullOrEmpty(target))
                {
                    if (next != "(") return null;
                    category = PermissionCategory.Launch;
                    string inner = i + 2 < parts.Length ? parts[i + 2] : "";
                    foreach (Match m in GuiExe.Matches(inner))
                        if (GuiApps.Contains(m.Groups[1].Value)) return "opens the desktop program " + m.Groups[1].Value.ToLowerInvariant();
                    foreach (string w in Words(inner))
                    {
                        string f = w.Trim('"', '\'');
                        if (Document.IsMatch(f)) return "opens " + f.Substring(Math.Max(f.LastIndexOf('\\'), f.LastIndexOf('/')) + 1) + " with another program (for web pages, use the built-in browser)";
                    }
                    return "starts or opens something whose location the command works out, so it cannot be checked";
                }
                string why = LaunchDanger(target, rest, workspace, cwd, name == "invoke-item" || name == "ii");
                if (why != null) category = why.StartsWith("runs an installer", StringComparison.Ordinal) ? PermissionCategory.Install : PermissionCategory.Launch;
                return why;
            }
            string bare = token.Trim('"', '\'');
            bool pathTok = bare.IndexOf('\\') >= 0 || bare.IndexOf('/') >= 0;
            if (GuiApps.Contains(name) && !(name == "write" && !bare.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                string quiet = Windowless(name, token, rest);
                if (quiet != "info")
                {
                    category = PermissionCategory.Launch;
                    return quiet != null ? "starts " + name + " without a window" : "opens the desktop program " + name;
                }
                if (!pathTok) return null;
            }
            bool starts = prev == "" || prev == "\n" || prev == "\r\n" || prev == ";" || prev == "&&" || prev == "||" || prev == "{";
            if (Document.IsMatch(bare) && bare.IndexOf('"') < 0 && bare.IndexOf('\'') < 0 && !Regex.IsMatch(bare, @"^[a-z][\w+.\-]+://", RegexOptions.IgnoreCase) &&
                (starts || prev == "&" || viaCmd))
            {
                bool called = prev == "&" || viaCmd;
                bool exists = false;
                if (!pathTok && !quoted) { try { exists = !string.IsNullOrEmpty(cwd) && File.Exists(Path.Combine(cwd, bare)); } catch { } }
                if (quoted ? called : (pathTok || called || exists))
                {
                    category = PermissionCategory.Launch;
                    return "opens " + bare.Substring(Math.Max(bare.LastIndexOf('\\'), bare.LastIndexOf('/')) + 1) + " with another program (for web pages, use the built-in browser)";
                }
            }
            bool pathy = token.IndexOf('\\') >= 0 || token.IndexOf('/') >= 0 || token.StartsWith("$env:", StringComparison.OrdinalIgnoreCase) || token.StartsWith("%");
            if (pathy && Regex.IsMatch(bare, @"\.(exe|com|bat|cmd|ps1|vbs|msc|cpl|scr|lnk|msi|msix|appx)$", RegexOptions.IgnoreCase))
            {
                if (InstallerName.IsMatch(bare)) return "runs an installer";
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
            }
            return null;
        }

        static string LaunchDanger(string target, string args, string workspace, string cwd, bool opener)
        {
            string t = target.Trim().Trim('"', '\'').Trim();
            if (t.Length == 0) return null;
            if (Regex.IsMatch(t, @"^[a-z][\w+.\-]+:", RegexOptions.IgnoreCase))
                return "opens " + (t.Length > 60 ? t.Substring(0, 60) + "..." : t) + " in another program (use the built-in browser for web pages)";
            if (InstallerName.IsMatch(t)) return "runs an installer";
            string name = BaseName(t);
            if (GuiApps.Contains(name)) return Windowless(name, t, args) == "headless" ? "starts " + name + " without a window" : "opens the desktop program " + name;
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
            if (unknown && Document.IsMatch(t)) return "opens " + t.Substring(Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/')) + 1) + " with another program (for web pages, use the built-in browser)";
            if (unknown) return "starts or opens something whose location comes from a variable, so it cannot be checked";
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

        const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex SplitPs = new Regex(@"\r?\n|;|&&", Ci), SplitCmd = new Regex(@"\r?\n|&&|&", Ci);
        static readonly Regex Joined = new Regex(@"[ \t]*[12]?>&[12]", Ci);
        static readonly Regex Quiet = new Regex(@"(?:[ \t]*(?:\|[ \t]*out-null|[12]?>[ \t]*(?:\$null|nul)))+[ \t]*$", Ci);
        static readonly Regex PsSleep = new Regex(@"^(?:start-sleep|sleep)(?:[ \t]+-(seconds|s|milliseconds|ms|m)(?:[ \t]*:[ \t]*|[ \t]+)|[ \t]+)([0-9]{1,6}(?:\.[0-9]{1,3})?)$", Ci);
        static readonly Regex ThreadSleep = new Regex(@"^\[(?:system\.)?threading\.thread\]::sleep\([ \t]*([0-9]{1,8})[ \t]*\)$", Ci);
        static readonly Regex TimeoutCmd = new Regex(@"^timeout(?:\.exe)?((?:[ \t]+(?:[/-]t|[/-]nobreak|[0-9]{1,5}))+)$", Ci);
        static readonly Regex PingWait = new Regex(@"^ping(?:\.exe)?[ \t]+(?:[/-]n[ \t]+([0-9]{1,4})[ \t]+(?:127\.0\.0\.1|localhost|::1)|(?:127\.0\.0\.1|localhost|::1)[ \t]+[/-]n[ \t]+([0-9]{1,4}))$", Ci);
        static readonly Regex PsSay = new Regex(@"^(?:write-host|write-output|echo)[ \t]+(?:'((?:[^']|'')*)'|""([^""$`]*)""|([a-z0-9 .,:!?_/\-]+))$", Ci);
        static readonly Regex CmdSay = new Regex(@"^echo(?:\.|[ \t]+([^%!^<>|]*))$", Ci);
        static readonly Regex CmdQuiet = new Regex(@"^(?:rem(?:[ \t].*)?|::.*|echo[ \t]+(?:off|on))$", Ci);

        public static bool PlainWait(string command, string shell, out double seconds, out string printed)
        {
            seconds = 0;
            printed = null;
            if (string.IsNullOrEmpty(command) || command.Length > 800) return false;
            bool cmd = shell == "cmd";
            List<string> said = new List<string>();
            bool waits = false;
            foreach (string part in (cmd ? SplitCmd : SplitPs).Split(Joined.Replace(command, " ")))
            {
                string s = part.Trim();
                if (cmd && s.StartsWith("@")) s = s.Substring(1).TrimStart();
                if (s.Length == 0 || (cmd ? CmdQuiet.IsMatch(s) : s.StartsWith("#"))) continue;
                if (!cmd && s.IndexOf('\'') < 0 && s.IndexOf('"') < 0) s = Regex.Replace(s, @"[ \t]+#.*$", "");
                s = Quiet.Replace(s, "").Trim();
                Match m;
                if (!cmd && (m = PsSleep.Match(s)).Success)
                {
                    double v = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    string unit = m.Groups[1].Value.ToLowerInvariant();
                    seconds += unit == "ms" || unit == "m" || unit == "milliseconds" ? v / 1000 : v;
                    waits = true;
                }
                else if (!cmd && (m = ThreadSleep.Match(s)).Success) { seconds += int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0; waits = true; }
                else if ((m = TimeoutCmd.Match(s)).Success)
                {
                    int t = TimeoutSeconds(m.Groups[1].Value);
                    if (t < 0) return false;
                    seconds += t;
                    waits = true;
                }
                else if ((m = PingWait.Match(s)).Success)
                {
                    seconds += Math.Max(0, int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, CultureInfo.InvariantCulture) - 1);
                    waits = true;
                }
                else if (!cmd && (m = PsSay.Match(s)).Success)
                    said.Add(m.Groups[1].Success ? m.Groups[1].Value.Replace("''", "'") : m.Groups[2].Success ? m.Groups[2].Value : Regex.Replace(m.Groups[3].Value.Trim(), @"[ \t]+", " "));
                else if (cmd && (m = CmdSay.Match(s)).Success) said.Add(m.Groups[1].Value.TrimEnd());
                else return false;
            }
            if (!waits) return false;
            printed = string.Join("\n", said.ToArray());
            return true;
        }

        static int TimeoutSeconds(string args)
        {
            int value = -1;
            bool wantValue = false;
            foreach (string t in Regex.Split(args.Trim(), @"[ \t]+"))
            {
                string a = t.ToLowerInvariant();
                if (a == "/t" || a == "-t") { if (wantValue || value >= 0) return -1; wantValue = true; }
                else if (a == "/nobreak" || a == "-nobreak") { if (wantValue) return -1; }
                else
                {
                    if (value >= 0) return -1;
                    value = int.Parse(a, CultureInfo.InvariantCulture);
                    wantValue = false;
                }
            }
            return wantValue ? -1 : value;
        }

        static readonly Regex Reach = new Regex(@"(^|[\s;&|(])(mosh|tnc|test-netconnection|resolve-dnsname|nslookup|dig|tracert|traceroute|tracepath|pathping|mtr|ping|ping6|rsync)(\.exe)?\s+([^;&|\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex HostWord = new Regex(@"^(?:rsync://)?(?:[^@\s/]+@)?(\[[0-9a-f:]+\]|[0-9]{1,3}(?:\.[0-9]{1,3}){3}|[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*\.[a-z]{2,})(?:[:/].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static bool ReachesOut(string command)
        {
            foreach (Match m in Reach.Matches(command ?? ""))
            {
                bool rsync = m.Groups[2].Value.Equals("rsync", StringComparison.OrdinalIgnoreCase);
                foreach (string raw in m.Groups[4].Value.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string a = raw.Trim('"', '\'');
                    if (a.StartsWith("-", StringComparison.Ordinal)) continue;
                    if (rsync && a.IndexOf(':') < 0 && a.IndexOf('@') < 0) continue;
                    Match h = HostWord.Match(a);
                    if (h.Success && !Net.IsLocal("http://" + h.Groups[1].Value + "/")) return true;
                }
            }
            return false;
        }

        public static bool NeedsInternet(string command)
        {
            string c = command ?? "";
            if (LooksNetwork(c) || ReachesOut(c)) return true;
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
            return Regex.IsMatch(command ?? "", @"\b(invoke-webrequest|iwr|invoke-restmethod|irm|curl|wget|ssh|scp|sftp|ftp|telnet|nc|ncat|mosh|net\.webclient|httpclient|start-bitstransfer)\b|\bgit\s+clone\b", RegexOptions.IgnoreCase);
        }
    }

    static class TerminalRules
    {
        static readonly Regex Opener = new Regex(@"(^|[\s;&|(""'])(wt|wt\.exe)(\s|$|[;&|)""'])|\bstart(-process)?\s+(-filepath\s+)?[""']?(cmd|powershell|pwsh|wsl|bash|wt)(\.exe)?\b|\bstart\s+""[^""]*""\s+(cmd|powershell|pwsh|wsl|bash)\b|\bconhost(\.exe)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool OpensTerminal(string command)
        {
            return Opener.IsMatch(command ?? "");
        }

        static readonly HashSet<string> Looking = new HashSet<string>(StringComparer.Ordinal)
        {
            "ls", "ll", "la", "dir", "vdir", "pwd", "cd", "pushd", "popd", "cat", "tac", "head", "tail", "grep", "egrep", "fgrep", "zgrep",
            "wc", "cut", "diff", "cmp", "comm", "column", "nl", "fold", "rev", "strings", "od", "hexdump", "stat", "du", "df", "free", "uptime",
            "uname", "whoami", "id", "groups", "who", "w", "last", "cal", "echo", "printenv", "ps", "pgrep", "pidof", "top", "htop", "btop",
            "vmstat", "iostat", "mpstat", "lsblk", "lscpu", "lsmem", "lsof", "lsmod", "lsusb", "lspci", "nproc", "arch", "which", "whereis", "type", "realpath",
            "readlink", "basename", "dirname", "md5sum", "sha1sum", "sha256sum", "sha512sum", "cksum", "sleep", "true", "false", "clear", "ping", "ping6",
            "traceroute", "tracepath", "mtr", "dig", "nslookup", "host", "netstat", "help", "exit", "logout", "test", "[", "jq", "locate", "getent", "lsb_release"
        };

        static readonly HashSet<string> ListVerbs = new HashSet<string>(StringComparer.Ordinal)
        {
            "status", "show", "cat", "is-active", "is-enabled", "is-failed", "list-units", "list-unit-files", "list-timers", "list-sockets", "list-dependencies", "list-jobs", "get-default"
        };

        static readonly HashSet<string> DockerLooks = new HashSet<string>(StringComparer.Ordinal) { "ps", "images", "logs", "inspect", "stats", "top", "version", "info", "port", "diff", "history", "ls", "list", "df", "config" };

        public static bool ReadOnly(string line)
        {
            string t = (line ?? "").Trim();
            if (t.Length == 0) return true;
            if (Expands(t)) return false;
            List<string> parts;
            if (!Split(t, out parts)) return false;
            foreach (string p in parts) if (!Looks(p)) return false;
            return true;
        }

        static bool Split(string t, out List<string> parts)
        {
            parts = new List<string>();
            StringBuilder cur = new StringBuilder();
            char q = '\0';
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (q != '\0')
                {
                    if (c == q) q = '\0';
                    else if (c == '\\' && q == '"' && i + 1 < t.Length) { cur.Append(c); cur.Append(t[++i]); continue; }
                    else if (q == '"' && (c == '`' || (c == '$' && i + 1 < t.Length && t[i + 1] == '('))) return false;
                    cur.Append(c);
                    continue;
                }
                if (c == '\\' && i + 1 < t.Length) { cur.Append(c); cur.Append(t[++i]); continue; }
                if (c == '\'' || c == '"') { q = c; cur.Append(c); continue; }
                if (c == '`') return false;
                if (c == '$' && i + 1 < t.Length && t[i + 1] == '(') return false;
                if ((c == '<' || c == '>') && i + 1 < t.Length && t[i + 1] == '(') return false;
                if (c == '>' || (c == '&' && i + 1 < t.Length && t[i + 1] == '>'))
                {
                    int j = c == '&' ? i + 2 : i + 1;
                    if (c == '>' && j < t.Length && t[j] == '>') j++;
                    if (c == '>' && j < t.Length && t[j] == '&') { j++; while (j < t.Length && char.IsDigit(t[j])) j++; cur.Append(t, i, j - i); i = j - 1; continue; }
                    while (j < t.Length && (t[j] == ' ' || t[j] == '\t')) j++;
                    if (string.CompareOrdinal(t, j, "/dev/null", 0, 9) == 0 && (j + 9 == t.Length || " \t;|&)".IndexOf(t[j + 9]) >= 0)) { i = j + 8; continue; }
                    return false;
                }
                if (c == ';' || c == '|' || c == '&' || c == '\n' || c == '\r' || c == '(' || c == ')' || c == '{' || c == '}')
                {
                    if (cur.ToString().Trim().Length > 0) parts.Add(cur.ToString().Trim());
                    cur.Length = 0;
                    continue;
                }
                cur.Append(c);
            }
            if (q != '\0') return false;
            if (cur.ToString().Trim().Length > 0) parts.Add(cur.ToString().Trim());
            return true;
        }

        public static List<string> Words(string s)
        {
            return Words(s, true);
        }

        static List<string> Words(string s, bool posix)
        {
            List<string> w = new List<string>();
            StringBuilder cur = new StringBuilder();
            char q = '\0';
            bool any = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (q != '\0') { if (c == q) q = '\0'; else cur.Append(c); continue; }
                if (c == '\'' || c == '"') { q = c; any = true; continue; }
                if (posix && c == '\\' && i + 1 < s.Length) { cur.Append(s[++i]); any = true; continue; }
                if (c == ' ' || c == '\t') { if (any || cur.Length > 0) { w.Add(cur.ToString()); cur.Length = 0; any = false; } continue; }
                cur.Append(c);
                any = true;
            }
            if (any || cur.Length > 0) w.Add(cur.ToString());
            return w;
        }

        static string Name(string word)
        {
            string n = word ?? "";
            int slash = n.LastIndexOf('/');
            if (slash >= 0) n = n.Substring(slash + 1);
            return n;
        }

        static bool Has(List<string> w, int from, params string[] any)
        {
            for (int i = from; i < w.Count; i++)
                foreach (string a in any)
                    if (w[i] == a || (a.EndsWith("*", StringComparison.Ordinal) && w[i].StartsWith(a.Substring(0, a.Length - 1), StringComparison.Ordinal))) return true;
            return false;
        }

        static string Verb(List<string> w, int from)
        {
            for (int i = from; i < w.Count; i++) if (!w[i].StartsWith("-", StringComparison.Ordinal)) return w[i];
            return null;
        }

        static bool OnlyFlags(List<string> w, int from)
        {
            for (int i = from; i < w.Count; i++) if (!w[i].StartsWith("-", StringComparison.Ordinal)) return false;
            return true;
        }

        static bool Short(List<string> w, int from, string letters)
        {
            for (int i = from; i < w.Count; i++)
            {
                string a = w[i];
                if (a.Length < 2 || a[0] != '-' || a[1] == '-') continue;
                for (int k = 1; k < a.Length; k++) if (letters.IndexOf(a[k]) >= 0) return true;
            }
            return false;
        }

        static int Operands(List<string> w, int from, string valued)
        {
            int n = 0;
            bool rest = false;
            for (int i = from; i < w.Count; i++)
            {
                string a = w[i];
                if (!rest && a == "--") { rest = true; continue; }
                if (!rest && a.Length == 2 && a[0] == '-' && valued.IndexOf(a[1]) >= 0) { i++; continue; }
                if (rest || a == "-" || !a.StartsWith("-", StringComparison.Ordinal)) n++;
            }
            return n;
        }

        static bool PagerCommands(List<string> w)
        {
            for (int i = 1; i < w.Count; i++)
                if (w[i].StartsWith("+", StringComparison.Ordinal) && !Regex.IsMatch(w[i], @"^\+([GgF]|\d+|/[^!|]*)$")) return true;
            return false;
        }

        public static bool Expands(string line)
        {
            string t = (line ?? "").TrimStart();
            if (t.StartsWith("^", StringComparison.Ordinal)) return true;
            bool single = false, dbl = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (single) { if (c == '\'') single = false; continue; }
                if (c == '\\') { i++; continue; }
                if (c == '\'' && !dbl) { single = true; continue; }
                if (c == '"') { dbl = !dbl; continue; }
                if (c != '!' || i + 1 >= t.Length) continue;
                char n = t[i + 1];
                if (n == ' ' || n == '\t' || n == '=' || n == '(' || (n == '"' && dbl)) continue;
                return true;
            }
            return false;
        }

        static bool Looks(string part)
        {
            List<string> w = Words(part);
            if (w.Count == 0) return true;
            if (Regex.IsMatch(w[0], @"^[A-Za-z_][A-Za-z0-9_]*=")) return false;
            string cmd = Name(w[0]);
            string v = Verb(w, 1);
            switch (cmd)
            {
                case "find": return !Has(w, 1, "-delete", "-exec", "-execdir", "-ok", "-okdir", "-fprint", "-fprint0", "-fprintf", "-fls");
                case "sort": return !Short(w, 1, "o") && !Has(w, 1, "--output*", "--compress-program*");
                case "printf": return !Short(w, 1, "v");
                case "man": return !Short(w, 1, "PHXC") && !Has(w, 1, "--pager*", "--html*", "--gxditview*", "--config-file*");
                case "less": case "more": return !Short(w, 1, "oOk") && !Has(w, 1, "--log-file*", "--LOG-FILE*", "--lesskey*") && !PagerCommands(w);
                case "rg": return !Has(w, 1, "--pre*");
                case "tree": return !Short(w, 1, "oR");
                case "uniq": return Operands(w, 1, "fsw") <= 1;
                case "xxd": return Operands(w, 1, "cglnosR") <= 1;
                case "file": return !Short(w, 1, "C") && !Has(w, 1, "--compile");
                case "lastlog": return !Short(w, 1, "CS") && !Has(w, 1, "--clear", "--set");
                case "date":
                    foreach (string x in w) if (Regex.IsMatch(x, @"^-[^-I]*s")) return false;
                    return !Has(w, 1, "--set*");
                case "dmesg": return !Short(w, 1, "cCDEn") && !Has(w, 1, "--clear", "--read-clear", "--console-off", "--console-on", "--console-level*");
                case "history": return w.Count == 1 || (w.Count == 2 && Regex.IsMatch(w[1], @"^\d+$"));
                case "ss": return !Short(w, 1, "KD") && !Has(w, 1, "--kill", "--diag*");
                case "hostname": case "env": return OnlyFlags(w, 1) && !Has(w, 1, "-F", "--file*", "-b", "--boot", "-i", "--ignore-environment", "-u", "--unset*", "-C", "--chdir*", "-S", "--split-string*");
                case "systemctl": return v == null || ListVerbs.Contains(v) || v == "list" || v.StartsWith("list-", StringComparison.Ordinal);
                case "service": return (w.Count == 2 && w[1] == "--status-all") || (w.Count == 3 && w[2] == "status");
                case "journalctl": return !Has(w, 1, "--vacuum*", "--rotate", "--flush", "--sync", "--relinquish-var", "--smart-relinquish-var", "--setup-keys", "--update-catalog");
                case "docker": case "podman":
                    if (v == null) return true;
                    if (DockerLooks.Contains(v) && v != "ls" && v != "list" && v != "df" && v != "config") return true;
                    if (v == "container" || v == "image" || v == "network" || v == "volume" || v == "system" || v == "compose" || v == "context" || v == "node" || v == "service")
                    {
                        int at = w.IndexOf(v);
                        string sub = Verb(w, at + 1);
                        return sub != null && DockerLooks.Contains(sub);
                    }
                    return false;
                case "kubectl":
                    if (Has(w, 1, "--kubeconfig*", "--output-directory*", "--raw")) return false;
                    foreach (string x in w) if (Regex.IsMatch(x, @"(^|,)secrets?(/|,|$)", RegexOptions.IgnoreCase)) return false;
                    return v == "get" || v == "describe" || v == "logs" || v == "top" || v == "version" || v == "cluster-info" || v == "explain" || v == "api-resources" || v == "api-versions" ||
                                       (v == "config" && (Verb(w, w.IndexOf("config") + 1) == "view" || Verb(w, w.IndexOf("config") + 1) == "get-contexts" || Verb(w, w.IndexOf("config") + 1) == "current-context"));
                case "git":
                    {
                        int at = 1;
                        for (; at < w.Count && w[at].StartsWith("-", StringComparison.Ordinal); at++)
                        {
                            if (w[at] == "-C") { at++; continue; }
                            if (w[at] == "-c" || Has(w.GetRange(at, 1), 0, "--git-dir*", "--work-tree*", "--exec-path*", "--config-env*", "--namespace*", "--list-cmds*", "--super-prefix*")) return false;
                        }
                        v = at < w.Count ? w[at] : null;
                    }
                    if (v == "grep") return !Short(w, 1, "O") && !Has(w, 1, "--open-files-in-pager*");
                    if (v == "status" || v == "log" || v == "diff" || v == "show" || v == "blame" || v == "shortlog" || v == "ls-files" || v == "rev-parse" || v == "describe") return !Has(w, 1, "--output*");
                    if (v == "branch") return OnlyFlags(w, w.IndexOf("branch") + 1) && !Has(w, 1, "-d", "-D", "-m", "-M", "-c", "-C", "-f", "--delete", "--move", "--copy", "--force", "--set-upstream-to*", "-u", "--unset-upstream", "--edit-description");
                    if (v == "remote") return OnlyFlags(w, w.IndexOf("remote") + 1);
                    return false;
                case "nginx": return OnlyFlags(w, 1) && Has(w, 1, "-t", "-T", "-v", "-V", "-h", "-?") && !Has(w, 1, "-s", "-c", "-g", "-p", "-e");
                case "apachectl": case "apache2ctl":
                    if (w.Count == 2 && (v == "configtest" || v == "status" || v == "fullstatus")) return true;
                    return w.Count > 1 && OnlyFlags(w, 1) && Has(w, 1, "-t", "-S", "-M", "-v", "-V") && !Has(w, 1, "-k", "-f", "-C", "-c", "-d");
                case "crontab": return w.Count == 2 && w[1] == "-l";
                case "ufw": return v == "status" || (v == "app" && Verb(w, w.IndexOf("app") + 1) == "list") || v == "show";
                case "apt":
                    if (Short(w, 1, "oc") || Has(w, 1, "--option*", "--config-file*")) return false;
                    return v == "list" || v == "show" || v == "search" || v == "policy" || v == "depends" || v == "rdepends";
                case "apt-cache": return v != null && v != "gencaches" && !Short(w, 1, "oc") && !Has(w, 1, "--option*", "--config-file*");
                case "dpkg": return w.Count > 1 && Has(w, 1, "-l", "-L", "-s", "-S", "-p", "--list", "--listfiles", "--status", "--search", "--print-avail", "--print-architecture", "--get-selections") && !Has(w, 1, "-i", "-r", "-P", "--install", "--remove", "--purge", "--configure", "--set-selections");
                case "rpm": return w.Count > 1 && w[1].StartsWith("-q", StringComparison.Ordinal) && !Short(w, 2, "DE") && !Has(w, 1, "--define*", "--eval*", "--macros*", "--rcfile*", "--dbpath*", "--root*", "--pipe*", "--queryformat*", "--qf*");
                case "dnf": case "yum":
                    if (Short(w, 1, "c") || Has(w, 1, "--setopt*", "--config*", "--installroot*")) return false;
                    return v == "list" || v == "info" || v == "search" || v == "repolist" || v == "provides" || (v == "history" && Verb(w, w.IndexOf("history") + 1) == null);
                case "pm2": return v == "list" || v == "ls" || v == "status" || v == "show" || v == "describe" || v == "logs" || v == "monit" || v == "jlist" || v == "prettylist";
                case "ip":
                    if (v == null) return true;
                    if (!Regex.IsMatch(v, "^(a|addr|address|r|route|l|link|n|neigh|neighbour|rule|maddr|maddress|mroute|netns|tunnel)$")) return false;
                    return !Has(w, 1, "add", "del", "delete", "change", "replace", "set", "flush", "append", "prepend", "save", "restore", "exec", "attach", "up", "down");
                case "timedatectl": case "hostnamectl": case "loginctl": case "networkctl": case "resolvectl":
                    return v == null || v == "status" || v == "show" || v.StartsWith("list-", StringComparison.Ordinal) || v == "show-session" || v == "show-user" || v == "query" || v == "statistics";
                case "certbot": return v == "certificates";
                case "watch":
                case "timeout":
                    {
                        int i = 1;
                        while (i < w.Count && w[i].StartsWith("-", StringComparison.Ordinal)) { if (w[i] == "-n" || w[i] == "--interval" || w[i] == "-s" || w[i] == "--signal" || w[i] == "-k" || w[i] == "--kill-after") i++; i++; }
                        if (cmd == "timeout" && i < w.Count) i++;
                        if (i >= w.Count) return true;
                        return ReadOnly(string.Join(" ", w.GetRange(i, w.Count - i).ToArray()));
                    }
            }
            return Looking.Contains(cmd);
        }

        static readonly HashSet<string> SshFlagsWithValue = new HashSet<string>(StringComparer.Ordinal) { "b", "c", "D", "E", "e", "F", "I", "i", "J", "L", "l", "m", "O", "o", "p", "Q", "R", "S", "W", "w", "B" };

        public static string SshTarget(string line, out string remote, out bool plain)
        {
            remote = null;
            plain = true;
            string t = (line ?? "").Trim();
            List<string> parts;
            if (!Split(t, out parts) || parts.Count == 0) { plain = false; parts = new List<string>(new string[] { t }); }
            List<string> w = Words(parts[0], false);
            if (w.Count == 0) return null;
            string cmd = Name(w[0].Replace('\\', '/')).ToLowerInvariant();
            if (cmd != "ssh" && cmd != "ssh.exe") return null;
            if (parts.Count > 1) plain = false;
            string user = null;
            int i = 1;
            for (; i < w.Count; i++)
            {
                string a = w[i];
                if (a == "--") { i++; break; }
                if (!a.StartsWith("-", StringComparison.Ordinal) || a.Length < 2) break;
                string last = a.Substring(a.Length - 1);
                bool glued = false;
                for (int k = 1; k < a.Length; k++)
                    if (SshFlagsWithValue.Contains(a.Substring(k, 1))) { glued = k < a.Length - 1; if (a[k] == 'l') user = glued ? a.Substring(k + 1) : (i + 1 < w.Count ? w[i + 1] : null); break; }
                if (SshFlagsWithValue.Contains(last) && !glued) i++;
            }
            if (i >= w.Count) return null;
            string dest = w[i];
            if (user != null && dest.IndexOf('@') < 0) dest = user + "@" + dest;
            if (dest.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)) dest = dest.Substring(6).TrimEnd('/');
            if (i + 1 < w.Count) remote = string.Join(" ", w.GetRange(i + 1, w.Count - i - 1).ToArray());
            return dest;
        }

        public static bool Leaves(string line)
        {
            string t = (line ?? "").Trim();
            return t == "exit" || t == "logout" || Regex.IsMatch(t, @"^exit\s+\d+$");
        }

        static readonly Regex Nested = new Regex(@"^\s*((sudo\s+(-\S+\s+)*)?(bash|sh|zsh|fish|dash|ksh|tmux|screen|byobu|su|newgrp|script)\b(?!.*\s-c\s)|sudo\s+(-\S+\s+)*-[is]\b|sudo\s+(-\S+\s+)*su\b|docker\s+(exec|run)\b.*\s-\w*i\w*t|docker\s+(exec|run)\b.*\s-\w*t\w*i|python3?$|node$|mysql\b|psql\b|redis-cli\b|mongo(sh)?\b)", RegexOptions.CultureInvariant);

        public static bool Nests(string line)
        {
            return Nested.IsMatch(line ?? "");
        }
    }

    static class CommandWords
    {
        static readonly Regex GenericWords = new Regex(@"^(please\s+)?(run|runs|running|execute|executes|executing|start|starts|invoke|do)?\s*(a|an|the|this|that|my|some|following)?\s*(powershell|cmd|shell|terminal|system|console|cli)?\s*(command|commands|script|cmd|task|it|code)?\s*[.!:]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool Generic(string description, string command)
        {
            string d = Regex.Replace(description ?? "", @"\s+", " ").Trim();
            if (d.Length < 3 || GenericWords.IsMatch(d)) return true;
            string c = Regex.Replace(command ?? "", @"\s+", " ").Trim();
            return c.Length > 0 && string.Equals(d.TrimEnd('.', ' '), c, StringComparison.OrdinalIgnoreCase);
        }

        public static string Explain(string description, string command)
        {
            string d = PermissionAsk.PlainSummary(description, 160);
            return Generic(d, command) ? Describe(command) : d;
        }

        class Seg { public string Text; public bool StepStart; }

        static List<Seg> Split(string c)
        {
            List<Seg> l = new List<Seg>();
            StringBuilder cur = new StringBuilder();
            bool start = true;
            char q = '\0';
            for (int i = 0; i < c.Length; i++)
            {
                char ch = c[i];
                if (q != '\0')
                {
                    cur.Append(ch);
                    if (ch == q) q = '\0';
                    continue;
                }
                if (ch == '"' || ch == '\'') { q = ch; cur.Append(ch); continue; }
                bool step = ch == '\n' || ch == ';' || ((ch == '&' || ch == '|') && i + 1 < c.Length && c[i + 1] == ch);
                bool inner = !step && (ch == '|' || ch == '&' || ch == '(' || ch == ')' || ch == '{' || ch == '}');
                if (!step && !inner) { cur.Append(ch); continue; }
                Seg sg = new Seg();
                sg.Text = cur.ToString();
                sg.StepStart = start;
                l.Add(sg);
                cur.Length = 0;
                start = step;
                if (step && ch != '\n' && ch != ';') i++;
            }
            Seg last = new Seg();
            last.Text = cur.ToString();
            last.StepStart = start;
            l.Add(last);
            return l;
        }

        static List<string> Tokens(string s)
        {
            List<string> l = new List<string>();
            foreach (Match m in Regex.Matches(s ?? "", @"""([^""]*)""|'([^']*)'|(\S+)"))
                l.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
            return l;
        }

        static string Base(string token)
        {
            string t = (token ?? "").Trim().Trim('"', '\'').Trim();
            int slash = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
            if (slash >= 0) t = t.Substring(slash + 1);
            string low = t.ToLowerInvariant();
            if (low.EndsWith(".exe", StringComparison.Ordinal) || low.EndsWith(".com", StringComparison.Ordinal)) low = low.Substring(0, low.Length - 4);
            return low;
        }

        static string Short(string path)
        {
            string p = (path ?? "").Trim().Trim('"', '\'');
            if (p.Length > 2 && p[0] == '.' && (p[1] == '\\' || p[1] == '/')) p = p.Substring(2);
            if (p.Length <= 48) return p;
            if (Regex.IsMatch(p, @"^[a-z]+://", RegexOptions.IgnoreCase))
            {
                Uri u;
                if (Uri.TryCreate(p, UriKind.Absolute, out u))
                {
                    string rest = u.PathAndQuery.Length > 1 ? u.PathAndQuery : "";
                    if (rest.Length > 24) rest = rest.Substring(0, 24) + "…";
                    return u.Host + rest;
                }
                return p.Substring(0, 45) + "…";
            }
            string[] parts = p.Split('\\', '/');
            if (parts.Length >= 2)
            {
                string tail = parts[parts.Length - 2] + "\\" + parts[parts.Length - 1];
                if (tail.Length <= 46) return "…\\" + tail;
            }
            return "…" + p.Substring(p.Length - 45);
        }

        static List<string> Plain(List<string> w)
        {
            List<string> l = new List<string>();
            bool skip = false;
            foreach (string x in w)
            {
                if (skip) { skip = false; continue; }
                if (x.Length > 1 && x[0] == '-') { if (ValueFlag.IsMatch(x)) skip = true; continue; }
                if (x.Length > 1 && x[0] == '/' && Regex.IsMatch(x, @"^/[a-z?]{1,3}(:\S*)?$", RegexOptions.IgnoreCase)) continue;
                if (x == ">" || x == ">>" || x == "2>&1" || x.StartsWith(">") || x.StartsWith("2>") || x.StartsWith("1>")) break;
                l.Add(x);
            }
            return l;
        }

        static readonly Regex ValueFlag = new Regex(@"^-(o|f|m|c|e|n|t|w|filter|include|exclude|encoding|depth|outfile|output|path|literalpath|lp|destination|destinationpath|value|itemtype|type|name|newname|uri|method|body|headers|tail|totalcount|head|first|last|pattern|filepath|argumentlist|workingdirectory|wait|seconds|milliseconds|id|processname|computername|count|message|branch|b)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static string Redirect(string seg)
        {
            char q = '\0';
            for (int i = 0; i < seg.Length; i++)
            {
                char ch = seg[i];
                if (q != '\0') { if (ch == q) q = '\0'; continue; }
                if (ch == '"' || ch == '\'') { q = ch; continue; }
                if (ch != '>') continue;
                int j = i + 1;
                if (j < seg.Length && seg[j] == '>') j++;
                if (j < seg.Length && seg[j] == '&') return null;
                while (j < seg.Length && seg[j] == ' ') j++;
                int k = j;
                if (k < seg.Length && (seg[k] == '"' || seg[k] == '\''))
                {
                    int e = seg.IndexOf(seg[k], k + 1);
                    if (e < 0) return null;
                    string quoted = seg.Substring(k + 1, e - k - 1);
                    return Sink(quoted) ? null : quoted;
                }
                while (k < seg.Length && !char.IsWhiteSpace(seg[k]) && seg[k] != '>') k++;
                string t = seg.Substring(j, k - j);
                if (t.Length == 0 || Sink(t)) { i = k; continue; }
                return t;
            }
            return null;
        }

        static bool Sink(string t)
        {
            string l = t.Trim().ToLowerInvariant();
            return l == "$null" || l == "nul" || l == "null" || l == "/dev/null" || l.StartsWith("&");
        }

        static bool Has(List<string> w, params string[] flags)
        {
            foreach (string x in w) foreach (string f in flags) if (string.Equals(x, f, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string After(List<string> w, params string[] flags)
        {
            for (int i = 0; i + 1 < w.Count; i++) foreach (string f in flags) if (string.Equals(w[i], f, StringComparison.OrdinalIgnoreCase)) return w[i + 1];
            foreach (string x in w)
                foreach (string f in flags)
                    if (x.StartsWith(f + ":", StringComparison.OrdinalIgnoreCase) || x.StartsWith(f + "=", StringComparison.OrdinalIgnoreCase)) return x.Substring(f.Length + 1);
            return null;
        }

        static readonly string[] ComposeVerbs = { "up", "down", "pull", "logs", "ps", "restart", "build", "stop", "start", "exec", "run", "config", "rm" };

        static string Docker(string tool, List<string> p, List<string> w, out int weight)
        {
            string sub = p.Count > 0 ? p[0].ToLowerInvariant() : "";
            if (tool == "docker-compose" || sub == "compose")
            {
                string verb = "";
                for (int i = tool == "docker-compose" ? 0 : 1; i < p.Count && verb.Length == 0; i++)
                    if (Array.IndexOf(ComposeVerbs, p[i].ToLowerInvariant()) >= 0) verb = p[i].ToLowerInvariant();
                switch (verb)
                {
                    case "ps": weight = 1; return "List the app's containers";
                    case "logs": weight = 1; return "Show the logs of the app's containers";
                    case "config": weight = 1; return "Show how the app's containers are set up";
                    case "up": case "start": weight = 4; return "Start the app's containers";
                    case "down": weight = 4; return "Stop and remove the app's containers";
                    case "stop": weight = 4; return "Stop the app's containers";
                    case "restart": weight = 4; return "Restart the app's containers";
                    case "pull": weight = 3; return "Download new images for the app's containers";
                    case "build": weight = 3; return "Build the app's container images";
                    case "exec": case "run": weight = 3; return "Run a command in one of the app's containers";
                    case "rm": weight = 5; return "Remove the app's stopped containers";
                }
                weight = 2; return "Run docker compose";
            }
            List<string> rest = new List<string>();
            int at = sub.Length > 0 ? w.FindIndex(delegate (string x) { return x.Equals(sub, StringComparison.OrdinalIgnoreCase); }) : -1;
            for (int i = at + 1; at >= 0 && i < w.Count; i++) if (w[i].Length > 0 && w[i][0] != '-') rest.Add(w[i]);
            string them = Names(rest, 0);
            string last = rest.Count > 0 ? Short(rest[rest.Count - 1]) : "";
            switch (sub)
            {
                case "ps": weight = 1; return "List the running containers";
                case "images": weight = 1; return "List the container images";
                case "logs": weight = 1; return last.Length > 0 ? "Show the logs of the container " + last : "Show a container's logs";
                case "inspect": case "stats": case "top": weight = 1; return last.Length > 0 ? "Show details of the container " + last : "Show details of the containers";
                case "pull": weight = 3; return last.Length > 0 ? "Download the image " + last : "Download an image";
                case "build": weight = 3; return "Build a container image";
                case "run": weight = 3; return "Start a new container";
                case "exec": weight = 3; return rest.Count > 0 ? "Run a command in the container " + Short(rest[0]) : "Run a command in a container";
                case "start": weight = 3; return them.Length > 0 ? "Start the container " + them : "Start a container";
                case "stop": case "kill": weight = 4; return them.Length > 0 ? "Stop the container " + them : "Stop a container";
                case "restart": weight = 4; return them.Length > 0 ? "Restart the container " + them : "Restart a container";
                case "rm": weight = 5; return them.Length > 0 ? "Delete the container " + them : "Delete a container";
                case "rmi": weight = 5; return them.Length > 0 ? "Delete the image " + them : "Delete an image";
            }
            if (p.Count > 1 && p[1].ToLowerInvariant() == "prune") { weight = 5; return "Delete unused " + (sub == "system" ? "containers, images and networks" : sub + "s"); }
            weight = 2; return "Run " + tool + (sub.Length > 0 && Regex.IsMatch(sub, @"^[a-z][\w-]{0,20}$") ? " " + sub : "");
        }

        static string Names(List<string> items, int from)
        {
            List<string> l = new List<string>();
            for (int i = from; i < items.Count && l.Count < 3; i++) l.Add(Short(items[i]));
            int more = items.Count - from - l.Count;
            if (l.Count == 0) return "";
            string s = l.Count == 1 ? l[0] : string.Join(", ", l.GetRange(0, l.Count - 1).ToArray()) + " and " + l[l.Count - 1];
            return more > 0 ? s + " and " + more + " more" : s;
        }

        static string Target(List<string> plain, string fallback)
        {
            if (plain.Count == 0) return fallback;
            string t = plain[0];
            if (t.StartsWith("$")) return fallback;
            if (t == "." || t == ".\\" || t == "./") return "this folder";
            return Short(t);
        }

        static readonly Dictionary<string, string> Nice = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "python", "Python" }, { "python3", "Python" }, { "py", "Python" }, { "node", "Node.js" }, { "npm", "npm" }, { "git", "Git" }, { "dotnet", ".NET" },
            { "java", "Java" }, { "javac", "the Java compiler" }, { "go", "Go" }, { "cargo", "Cargo" }, { "rustc", "Rust" }, { "gcc", "GCC" }, { "cmake", "CMake" },
            { "pip", "pip" }, { "pip3", "pip" }, { "pwsh", "PowerShell" }, { "powershell", "PowerShell" }, { "deno", "Deno" }, { "bun", "Bun" }, { "ruby", "Ruby" },
            { "php", "PHP" }, { "perl", "Perl" }, { "ffmpeg", "FFmpeg" }, { "winget", "winget" }, { "docker", "Docker" }, { "code", "VS Code" }
        };

        static string Tool(string name) { string n; return Nice.TryGetValue(name, out n) ? n : name; }

        static string One(string seg, bool first, int depth, out int weight)
        {
            weight = -1;
            string s = Regex.Replace(seg.Trim(), @"^(@|[.&](?=\s))\s*", "");
            Match asg = Regex.Match(s, @"^\$(env:)?([\w:]+)\s*[+-]?=(?!=)\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (asg.Success)
            {
                if (asg.Groups[1].Success) { weight = 1; return "Set " + asg.Groups[2].Value + " for the next steps"; }
                s = asg.Groups[3].Value.Trim();
                if (s.Length == 0 || s[0] == '"' || s[0] == '\'' || s[0] == '$' || s[0] == '@' || char.IsDigit(s[0]) || s[0] == '[') { weight = 0; return first ? "Set a variable" : null; }
            }
            List<string> t = Tokens(s);
            if (t.Count == 0) return null;
            string head = t[0];
            string name = Base(head);
            List<string> w = t.GetRange(1, t.Count - 1);
            List<string> p = Plain(w);
            if (name.IndexOf('-') > 0)
            {
                string pathArg = After(w, "-path", "-literalpath", "-lp");
                if (pathArg != null) p.Insert(0, pathArg);
            }
            string sub = p.Count > 0 ? p[0].ToLowerInvariant() : "";
            string low = head.ToLowerInvariant().Trim('"', '\'');
            if (w.Count == 1 && Regex.IsMatch(w[0], @"^(--version|-v|-V|version|/version|-version)$") && !Regex.IsMatch(name, @"^(echo|write-host|write-output|cd|dir|ls)$"))
            { weight = 1; return "Check the " + Tool(name) + " version"; }
            if (low.EndsWith(".ps1")) { weight = 2; return "Run the PowerShell script " + Short(head); }
            if (low.EndsWith(".bat") || low.EndsWith(".cmd")) { weight = 2; return "Run the batch file " + Short(head); }
            if (low.EndsWith(".sh")) { weight = 2; return "Run the script " + Short(head); }
            if (low.EndsWith(".py")) { weight = 2; return "Run the Python script " + Short(head); }
            switch (name)
            {
                case "cd": case "chdir": case "set-location": case "sl": case "pushd": case "popd":
                    weight = -2; return p.Count > 0 ? Short(p[0]) : "";
                case "dir": case "ls": case "gci": case "get-childitem": case "tree":
                    weight = 1;
                    {
                        string where = Target(p, "this folder");
                        if (name == "tree") return "Show the folders in " + where;
                        return Has(w, "-recurse", "-r", "/s", "-recursive") ? "List all files in " + where + " and its subfolders" : "List the files in " + where;
                    }
                case "type": case "cat": case "gc": case "get-content": case "more":
                    weight = 1;
                    if (After(w, "-tail") != null) return "Show the end of " + Target(p, "a file");
                    if (After(w, "-totalcount", "-head", "-first") != null) return "Show the start of " + Target(p, "a file");
                    return "Show " + Target(p, "a file");
                case "select-string": case "sls": case "findstr": case "grep": case "rg":
                    {
                        weight = 1;
                        if (!first && After(w, "-path") == null && p.Count <= 1)
                        {
                            string only = After(w, "-pattern") ?? (p.Count > 0 ? p[0] : null);
                            weight = 0;
                            return only != null ? "\u0001" + (only.Length > 40 ? only.Substring(0, 40) + "\u2026" : only) : null;
                        }
                        string pat = After(w, "-pattern") ?? (p.Count > 0 ? p[0] : null);
                        int from = After(w, "-pattern") != null ? 0 : 1;
                        string where = After(w, "-path") ?? (p.Count > from ? Names(p, from) : null);
                        return "Search " + (string.IsNullOrEmpty(where) ? "the text" : where) + (pat != null ? " for “" + (pat.Length > 40 ? pat.Substring(0, 40) + "…" : pat) + "”" : "");
                    }
                case "test-path": weight = 1; return "Check whether " + Target(p, "a path") + " exists";
                case "get-item": case "gi": case "get-itemproperty": case "gp": weight = 1; return "Show details of " + Target(p, "an item");
                case "get-command": case "gcm": case "which": weight = 1; return "Find where " + Target(p, "a program") + " is installed";
                case "where":
                    if (!first) { weight = 0; return null; }
                    weight = 1; return "Find where " + Target(p, "a program") + " is installed";
                case "get-process": case "ps": case "tasklist": case "gps": weight = 1; return "List running programs";
                case "get-service": case "gsv": weight = 1; return "List Windows services";
                case "get-date": weight = 1; return "Show the date and time";
                case "get-location": case "pwd": case "gl": weight = 1; return "Show the current folder";
                case "hostname": weight = 1; return "Show this PC's name";
                case "whoami": weight = 1; return "Show the signed-in user";
                case "systeminfo": case "get-computerinfo": weight = 1; return "Show system information";
                case "ipconfig": case "get-netipaddress": case "get-netipconfiguration": weight = 1; return "Show network settings";
                case "netstat": case "get-nettcpconnection": weight = 1; return "List network connections";
                case "ping": case "test-connection": case "test-netconnection": case "tnc": weight = 3; return "Check the connection to " + Target(p, "a server");
                case "nslookup": case "resolve-dnsname": weight = 3; return "Look up the address of " + Target(p, "a server");
                case "get-filehash": weight = 1; return "Compute the checksum of " + Target(p, "a file");
                case "compare-object": case "fc": case "comp": case "diff": weight = 1; return p.Count >= 2 ? "Compare " + Short(p[0]) + " and " + Short(p[1]) : "Compare files";
                case "echo": case "write-output": case "write-host": case "write": weight = 0; return "Print text";
                case "start-sleep": case "sleep": case "timeout":
                    {
                        weight = 0;
                        string n = After(w, "-seconds", "-s", "/t") ?? (p.Count > 0 ? p[0] : null);
                        double sec;
                        if (n != null && double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out sec) && sec > 0)
                        {
                            if (After(w, "-milliseconds", "-m") != null) return "Wait a moment";
                            return "Wait " + (sec == 1 ? "1 second" : sec.ToString("0.#", CultureInfo.InvariantCulture) + " seconds");
                        }
                        return "Wait a moment";
                    }
                case "select-object": case "select": case "sort-object": case "sort": case "where-object": case "?": case "foreach-object": case "%": case "format-table": case "ft":
                case "format-list": case "fl": case "measure-object": case "measure": case "group-object": case "out-string": case "out-host": case "out-null": case "tee-object":
                case "convertto-json": case "convertfrom-json": case "convertto-csv": case "convertfrom-csv": case "if": case "else": case "elseif": case "foreach": case "for": case "while": case "try": case "catch": case "finally": case "param": case "return": case "exit":
                    weight = 0; return null;
                case "set-content": case "out-file": weight = 4; return "Write " + Target(p.Count > 0 && name == "out-file" ? p : p, "a file") ;
                case "add-content": case "ac": weight = 4; return "Add text to " + Target(p, "a file");
                case "clear-content": case "clc": weight = 5; return "Empty " + Target(p, "a file");
                case "new-item": case "ni":
                    {
                        weight = 4;
                        string type = (After(w, "-itemtype", "-type") ?? "").ToLowerInvariant();
                        string path = After(w, "-path") ?? (p.Count > 0 ? p[0] : null);
                        string nm = After(w, "-name");
                        string what = path != null ? Short(path) + (nm != null ? "\\" + nm : "") : nm != null ? nm : "an item";
                        return (type.StartsWith("dir") ? "Create the folder " : "Create ") + what;
                    }
                case "mkdir": case "md": weight = 4; return "Create the folder " + Names(p, 0).TrimEnd();
                case "copy-item": case "copy": case "cp": case "cpi": case "xcopy": case "robocopy":
                    {
                        weight = 4;
                        string to = After(w, "-destination");
                        if (to != null) return "Copy " + Target(p, "files") + " to " + Short(to);
                        return p.Count >= 2 ? "Copy " + Short(p[0]) + " to " + Short(p[1]) : "Copy " + Target(p, "files");
                    }
                case "move-item": case "move": case "mv": case "mi":
                    {
                        weight = 4;
                        string to = After(w, "-destination");
                        if (to != null) return "Move " + Target(p, "files") + " to " + Short(to);
                        return p.Count >= 2 ? "Move " + Short(p[0]) + " to " + Short(p[1]) : "Move " + Target(p, "files");
                    }
                case "rename-item": case "ren": case "rni": case "rename":
                    {
                        weight = 4;
                        string to = After(w, "-newname");
                        if (to != null) return "Rename " + Target(p, "an item") + " to " + Short(to);
                        return p.Count >= 2 ? "Rename " + Short(p[0]) + " to " + Short(p[1]) : "Rename " + Target(p, "an item");
                    }
                case "remove-item": case "rm": case "ri": case "del": case "erase": case "rd": case "rmdir":
                    {
                        weight = 5;
                        bool all = Has(w, "-recurse", "-r", "/s", "-rf", "-fr");
                        string what = p.Count > 0 && !p[0].StartsWith("$") ? Names(p, 0) : "files";
                        return "Delete " + what + (all && what != "files" ? " and everything in it" : "");
                    }
                case "expand-archive": weight = 4; { string to = After(w, "-destinationpath"); return "Unpack " + Target(p, "an archive") + (to != null ? " into " + Short(to) : ""); }
                case "compress-archive": weight = 4; { string to = After(w, "-destinationpath") ?? (p.Count > 1 ? p[1] : null); return "Pack " + Target(p, "files") + (to != null ? " into " + Short(to) : " into a zip file"); }
                case "tar":
                    weight = 4;
                    if (w.Count > 0 && Regex.IsMatch(w[0], @"^-?[a-z]*x", RegexOptions.IgnoreCase)) return "Unpack " + Target(p, "an archive");
                    if (w.Count > 0 && Regex.IsMatch(w[0], @"^-?[a-z]*c", RegexOptions.IgnoreCase)) return "Pack files into " + Target(p, "an archive");
                    weight = 1; return "List what is in " + Target(p, "an archive");
                case "invoke-webrequest": case "iwr": case "curl": case "wget": case "invoke-restmethod": case "irm":
                    {
                        weight = 3;
                        string url = After(w, "-uri") ?? p.Find(delegate (string x) { return Regex.IsMatch(x, @"^https?://", RegexOptions.IgnoreCase); }) ?? (p.Count > 0 ? p[0] : null);
                        string to = After(w, "-outfile", "-o", "--output");
                        if (url == null) return "Send a web request";
                        if (to != null) return "Download " + Short(url) + " to " + Short(to);
                        string m = (After(w, "-method", "-x", "--request") ?? "").ToUpperInvariant();
                        return (m == "POST" || m == "PUT" || m == "PATCH" || m == "DELETE" ? "Send a " + m + " request to " : "Fetch ") + Short(url);
                    }
                case "start-process": case "start": case "saps":
                    {
                        weight = 2;
                        string what = After(w, "-filepath") ?? (p.Count > 0 ? p[0] : null);
                        if (what == null) return "Open a program";
                        if (Regex.IsMatch(what, @"^https?://", RegexOptions.IgnoreCase)) return "Open " + Short(what) + " in your web browser";
                        return "Open " + Short(what);
                    }
                case "stop-process": case "kill": case "spps": case "taskkill":
                    {
                        weight = 4;
                        string who = After(w, "-name", "-processname", "/im") ?? After(w, "-id", "/pid") ?? (p.Count > 0 ? p[0] : null);
                        return "Stop " + (who != null ? "the program " + Short(who) : "a program");
                    }
                case "explorer": weight = 2; return "Open " + Target(p, "a folder") + " in File Explorer";
                case "notepad": weight = 2; return "Open " + Target(p, "Notepad") + (p.Count > 0 ? " in Notepad" : "");
                case "code": weight = 2; return "Open " + Target(p, "VS Code") + (p.Count > 0 ? " in VS Code" : "");
                case "ssh": weight = 3; return "Connect to " + Target(p, "a server") + " with SSH";
                case "scp": case "sftp": weight = 3; return "Copy files over SSH";
                case "reg":
                    if (sub == "query") { weight = 1; return "Read " + (p.Count > 1 ? Short(p[1]) : "a key") + " in the registry"; }
                    weight = 5; return "Change the Windows registry";
                case "winget": case "choco": case "scoop":
                    if (sub == "install") { weight = 4; return "Install " + (p.Count > 1 ? Names(p, 1) : "a program") + " with " + name; }
                    if (sub == "uninstall" || sub == "remove") { weight = 5; return "Uninstall " + (p.Count > 1 ? Names(p, 1) : "a program") + " with " + name; }
                    if (sub == "upgrade" || sub == "update") { weight = 4; return "Update " + (p.Count > 1 ? Names(p, 1) : "programs") + " with " + name; }
                    weight = 1; return "Look up programs with " + name;
                case "sudo": case "doas":
                    {
                        if (w.Count == 0 || depth >= 3) { weight = 4; return "Run a command as administrator"; }
                        int iw;
                        string d = Inner(string.Join(" ", w.ToArray()), depth + 1, out iw);
                        weight = Math.Max(4, iw);
                        return d != null ? d + " (as administrator)" : "Run a command as administrator";
                    }
                case "apt": case "apt-get": case "dnf": case "yum": case "pacman": case "brew": case "apk": case "zypper": case "snap":
                    if (sub == "install" || sub == "add" || (name == "pacman" && Has(w, "-S"))) { weight = 4; return "Install " + (p.Count > (name == "pacman" ? 0 : 1) ? Names(p, name == "pacman" ? 0 : 1) : "packages") + " with " + name; }
                    if (sub == "remove" || sub == "purge" || sub == "uninstall" || sub == "del" || (name == "pacman" && Has(w, "-R"))) { weight = 5; return "Uninstall " + (p.Count > 1 ? Names(p, 1) : "packages") + " with " + name; }
                    if ((name == "apt" || name == "apt-get") && sub == "update") { weight = 3; return "Refresh the package lists with " + name; }
                    if ((name == "apt" || name == "apt-get") && (sub == "upgrade" || sub == "full-upgrade" || sub == "dist-upgrade")) { weight = 4; return "Install package updates with " + name; }
                    if (sub == "update" || sub == "upgrade" || (name == "pacman" && Has(w, "-Syu"))) { weight = 4; return "Update the packages with " + name; }
                    weight = 1; return "Look up packages with " + name;
                case "systemctl": case "service":
                    {
                        string act = name == "service" ? (p.Count > 1 ? p[1].ToLowerInvariant() : "") : sub;
                        string unit = name == "service" ? (p.Count > 0 ? p[0] : "a service") : (p.Count > 1 ? p[1] : "a service");
                        if (act == "status" || act == "is-active" || act == "list-units") { weight = 1; return "Show the status of " + unit; }
                        weight = 4;
                        return act == "start" ? "Start the service " + unit : act == "stop" ? "Stop the service " + unit : act == "restart" || act == "reload" ? "Restart the service " + unit : act == "enable" ? "Make " + unit + " start at boot" : act == "disable" ? "Stop " + unit + " from starting at boot" : "Change the service " + unit;
                    }
                case "journalctl": weight = 1; return "Show the system log";
                case "chmod": case "chown": case "chgrp": weight = 4; return "Change the permissions of " + (p.Count > 1 ? Names(p, 1) : "files");
                case "touch": weight = 4; return "Create " + Target(p, "a file");
                case "head": case "tail":
                    {
                        weight = 1;
                        string file = w.FindLast(delegate (string x) { return x.Length > 0 && x[0] != '-' && !Regex.IsMatch(x, @"^\+?\d+$"); });
                        string what = file != null ? Short(file) : "a file";
                        return name == "head" ? "Show the start of " + what : (Has(w, "-f", "-F") ? "Follow the end of " : "Show the end of ") + what;
                    }
                case "less": weight = 1; return "Show " + Target(p, "a file");
                case "find": weight = 1; return "Find files in " + Target(p, "this folder");
                case "du": case "df": weight = 1; return "Show how much disk space is used";
                case "top": case "htop": weight = 1; return "Show what is using the processor";
                case "pkill": case "killall": weight = 4; return "Stop the program " + Target(p, "a program");
                case "nano": case "vim": case "vi": case "nvim": case "emacs": weight = 2; return "Open " + Target(p, "a file") + " in " + name;
                case "export": weight = 1; return p.Count > 0 ? "Set " + p[0].Split('=')[0] + " for the next steps" : "Show the environment";
                case "source": weight = 2; return "Load " + Target(p, "a script") + " into this shell";
                case "bash": case "sh": case "zsh": weight = 2; return Has(w, "-c") ? "Run a shell command" : p.Count > 0 ? "Run the script " + Short(p[0]) : "Start a shell";
                case "git": return Git(p, w, out weight);
                case "npm": case "pnpm": case "yarn": case "bun": return Npm(name, p, out weight);
                case "npx": case "pnpx": case "bunx": weight = 2; return p.Count > 0 ? "Run " + Short(p[0]) + " with " + name : "Run a package with " + name;
                case "pip": case "pip3": return Pip(p, w, out weight);
                case "uv":
                    weight = 4;
                    if (sub == "pip" && p.Count > 1 && p[1] == "install") return "Install the Python packages " + Names(p, 2);
                    if (sub == "add") return "Add " + Names(p, 1) + " to the Python project";
                    if (sub == "sync") return "Install the Python project's packages";
                    if (sub == "venv") return "Create a Python virtual environment";
                    weight = 2; return sub == "run" && p.Count > 1 ? "Run " + Short(p[1]) + " with uv" : "Run uv " + sub;
                case "python": case "python3": case "py": case "pythonw":
                    {
                        string mod = After(w, "-m");
                        if (mod != null)
                        {
                            int at = w.FindIndex(delegate (string x) { return x == "-m"; });
                            List<string> rest = Plain(w.GetRange(Math.Min(w.Count, at + 2), Math.Max(0, w.Count - at - 2)));
                            if (mod == "pip") return Pip(rest, w, out weight);
                            weight = 2;
                            if (mod == "venv") { weight = 4; return "Create a Python virtual environment" + (rest.Count > 0 ? " in " + Short(rest[0]) : ""); }
                            if (mod == "pytest" || mod == "unittest") return "Run the Python tests";
                            if (mod == "http.server") return "Start a local web server";
                            return "Run the Python module " + mod;
                        }
                        weight = 2;
                        if (Has(w, "-c")) return "Run a line of Python";
                        return p.Count > 0 ? "Run the Python script " + Short(p[0]) : "Start Python";
                    }
                case "node": weight = 2; if (Has(w, "-e", "--eval", "-p", "--print")) return "Run a line of JavaScript"; return p.Count > 0 ? "Run " + Short(p[0]) + " with Node.js" : "Start Node.js";
                case "dotnet": return DotNet(p, out weight);
                case "cargo": weight = sub == "add" || sub == "install" ? 4 : 2; return sub == "build" ? "Build the Rust project" : sub == "run" ? "Run the Rust project" : sub == "test" ? "Run the Rust tests" : sub == "add" ? "Add " + Names(p, 1) + " to the Rust project" : "Run cargo " + sub;
                case "go": weight = sub == "get" || sub == "install" ? 4 : 2; return sub == "build" ? "Build the Go program" : sub == "run" ? "Run " + (p.Count > 1 ? Short(p[1]) : "the Go program") : sub == "test" ? "Run the Go tests" : sub == "mod" ? "Update the Go module files" : "Run go " + sub;
                case "make": case "mingw32-make": case "nmake": weight = 2; return p.Count > 0 ? "Run the make target " + p[0] : "Build with make";
                case "cmake": weight = 2; return Has(w, "--build") ? "Build with CMake" : "Set up the build with CMake";
                case "msbuild": weight = 2; return "Build " + Target(p, "the project") + " with MSBuild";
                case "csc": weight = 2; return "Compile " + Target(p, "C# files") + " with the C# compiler";
                case "gcc": case "g++": case "clang": case "clang++": case "cl": weight = 2; return "Compile " + Target(p, "the code");
                case "javac": weight = 2; return "Compile " + Target(p, "Java files");
                case "java": weight = 2; { string jar = After(w, "-jar"); return jar != null ? "Run " + Short(jar) : p.Count > 0 ? "Run the Java program " + Short(p[0]) : "Start Java"; }
                case "pytest": weight = 2; return "Run the Python tests";
                case "tsc": weight = 2; return "Compile the TypeScript code";
                case "eslint": weight = 1; return "Check the code with ESLint";
                case "prettier": weight = 4; return Has(w, "--write", "-w") ? "Format the code with Prettier" : "Check the formatting with Prettier";
                case "black": case "ruff": case "mypy": weight = name == "black" ? 4 : 1; return name == "black" ? "Format the Python code with Black" : "Check the Python code with " + (name == "ruff" ? "Ruff" : "mypy");
                case "docker": case "podman": case "docker-compose": return Docker(name, p, w, out weight);
                case "mysql": case "mariadb": case "psql": case "sqlite3": weight = 3; return Has(w, "-e", "-c", "--execute", "--command") ? "Run an SQL query with " + name : "Open the " + name + " database shell";
                case "free": weight = 1; return "Show how much memory is used";
                case "uptime": weight = 1; return "Show how long the computer has been running";
                case "crontab":
                    if (Has(w, "-l")) { weight = 1; return "Show the scheduled jobs (crontab)"; }
                    if (Has(w, "-r")) { weight = 5; return "Delete all scheduled jobs (crontab)"; }
                    weight = 4; return "Change the scheduled jobs (crontab)";
                case "ln": weight = 4; return p.Count > 1 ? "Link " + Short(p[1]) + " to " + Short(p[0]) : "Make a link to " + Target(p, "a file");
                case "reboot": weight = 5; return "Restart the computer";
                case "shutdown": case "poweroff": case "halt": weight = 5; return Has(w, "-r", "/r", "--reboot") ? "Restart the computer" : "Shut down the computer";
                case "ufw":
                    if (sub == "status" || sub == "show") { weight = 1; return "Show the firewall rules"; }
                    weight = 5;
                    if (sub == "allow") return "Open " + (p.Count > 1 ? Short(p[1]) : "a port") + " in the firewall";
                    if (sub == "deny" || sub == "reject") return "Block " + (p.Count > 1 ? Short(p[1]) : "a port") + " in the firewall";
                    if (sub == "delete") return "Remove a firewall rule";
                    if (sub == "enable") return "Turn the firewall on";
                    if (sub == "disable") return "Turn the firewall off";
                    return "Change the firewall";
                case "certbot":
                    if (sub == "certificates") { weight = 1; return "List the HTTPS certificates"; }
                    weight = 4; return sub == "renew" ? "Renew the HTTPS certificates" : sub == "delete" || sub == "revoke" ? "Remove an HTTPS certificate" : "Get an HTTPS certificate";
                case "pm2":
                    {
                        if (sub == "list" || sub == "ls" || sub == "status" || sub == "show" || sub == "describe" || sub == "monit" || sub == "jlist") { weight = 1; return "List the apps pm2 runs"; }
                        if (sub == "logs") { weight = 1; return "Show the logs of " + (p.Count > 1 ? Short(p[1]) : "the apps pm2 runs"); }
                        string app = p.Count > 1 ? (p[1] == "all" ? "all apps" : Short(p[1])) : "the app";
                        weight = 4;
                        if (sub == "restart" || sub == "reload") return "Restart " + app + " with pm2";
                        if (sub == "start") return "Start " + app + " with pm2";
                        if (sub == "stop") return "Stop " + app + " in pm2";
                        if (sub == "delete") return "Remove " + app + " from pm2";
                        if (sub == "save") return "Save the list of apps pm2 runs";
                        weight = 2; return "Run pm2 " + sub;
                    }
                case "nginx":
                    {
                        if (Has(w, "-t", "-T")) { weight = 1; return "Check the nginx configuration"; }
                        string sig = After(w, "-s");
                        weight = 4;
                        if (sig == "reload") return "Reload nginx with its new configuration";
                        if (sig == "stop" || sig == "quit") return "Stop nginx";
                        return sig != null ? "Send nginx the signal " + sig : "Start nginx";
                    }
                case "useradd": case "adduser": weight = 4; return p.Count > 0 ? "Create the user account " + Short(p[p.Count - 1]) : "Create a user account";
                case "userdel": case "deluser": weight = 5; return p.Count > 0 ? "Delete the user account " + Short(p[p.Count - 1]) : "Delete a user account";
                case "passwd": weight = 5; return p.Count > 0 ? "Change the password of " + Short(p[p.Count - 1]) : "Change your password";
                case "ssh-keygen": weight = 4; return Has(w, "-R") ? "Forget a server's saved SSH key" : "Create an SSH key";
                case "cmd":
                    {
                        string inner = After(w, "/c", "/k");
                        if (inner != null && depth < 3)
                        {
                            int at = w.FindIndex(delegate (string x) { return x.Equals("/c", StringComparison.OrdinalIgnoreCase) || x.Equals("/k", StringComparison.OrdinalIgnoreCase); });
                            string rest = string.Join(" ", w.GetRange(at + 1, w.Count - at - 1).ToArray());
                            int iw;
                            string d = Inner(rest, depth + 1, out iw);
                            if (d != null) { weight = iw; return d; }
                        }
                        weight = 2; return "Start cmd.exe";
                    }
                case "powershell": case "pwsh":
                    {
                        string file = After(w, "-file", "-f");
                        if (file != null) { weight = 2; return "Run the PowerShell script " + Short(file); }
                        string inner = After(w, "-command", "-c");
                        if (inner != null && depth < 3)
                        {
                            int at = w.FindIndex(delegate (string x) { return x.Equals("-command", StringComparison.OrdinalIgnoreCase) || x.Equals("-c", StringComparison.OrdinalIgnoreCase); });
                            string rest = string.Join(" ", w.GetRange(at + 1, w.Count - at - 1).ToArray());
                            int iw;
                            string d = Inner(rest, depth + 1, out iw);
                            if (d != null) { weight = iw; return d; }
                        }
                        weight = 2; return "Start " + Tool(name);
                    }
            }
            if (!first) return null;
            if (low.EndsWith(".exe") || low.IndexOf('\\') >= 0 || low.IndexOf('/') >= 0) { weight = 2; return "Run " + Short(head); }
            if (!Regex.IsMatch(name, @"^[\w.+-]{1,40}$")) return null;
            weight = 2;
            return "Run " + Tool(name) + (sub.Length > 0 && Regex.IsMatch(sub, @"^[a-z][\w-]{0,20}$") ? " " + sub : "");
        }

        static string Git(List<string> p, List<string> w, out int weight)
        {
            string sub = p.Count > 0 ? p[0].ToLowerInvariant() : "";
            weight = 1;
            switch (sub)
            {
                case "status": return "Show the Git status";
                case "log": return "Show the Git history";
                case "diff": return "Show the changes Git sees";
                case "show": return "Show a Git commit";
                case "branch": if (p.Count > 1 || Has(w, "-d", "-D", "--delete", "-m")) { weight = 4; return "Change Git branches"; } return "List the Git branches";
                case "remote": if (p.Count > 1 && p[1] != "-v") { weight = 4; return "Change the Git remotes"; } return "List the Git remotes";
                case "add": weight = 2; return "Stage " + (p.Count > 1 ? (p[1] == "." || p[1] == "-A" ? "all changes" : Names(p, 1)) : "changes") + " for the next commit";
                case "commit":
                    {
                        weight = 4;
                        string m = After(w, "-m", "--message");
                        return m != null ? "Commit the changes as “" + (m.Length > 60 ? m.Substring(0, 60) + "…" : m) + "”" : "Commit the changes";
                    }
                case "push": weight = 4; return "Push the commits to " + (p.Count > 1 ? p[1] : "the remote");
                case "pull": weight = 4; return "Pull changes from " + (p.Count > 1 ? p[1] : "the remote");
                case "fetch": weight = 3; return "Fetch changes from " + (p.Count > 1 ? p[1] : "the remote");
                case "clone": weight = 3; return "Download the repository " + (p.Count > 1 ? Short(p[1]) : "");
                case "checkout": case "switch":
                    {
                        weight = 2;
                        string nb = After(w, "-b", "-c", "-B");
                        if (nb != null) { weight = 4; return "Create the branch " + nb + " and switch to it"; }
                        return p.Count > 1 ? "Switch to " + p[p.Count - 1] : "Switch branches";
                    }
                case "merge": weight = 4; return "Merge " + (p.Count > 1 ? p[1] : "a branch") + " into the current branch";
                case "rebase": weight = 4; return "Rebase the current branch";
                case "reset": weight = 4; return Has(w, "--hard") ? "Throw away uncommitted changes" : "Unstage changes";
                case "restore": weight = 4; return "Restore " + (p.Count > 1 ? Names(p, 1) : "files") + " from Git";
                case "clean": weight = 5; return "Delete files Git doesn't track";
                case "stash": weight = 4; return p.Count > 1 && (p[1] == "pop" || p[1] == "apply") ? "Bring back stashed changes" : "Set changes aside with git stash";
                case "init": weight = 4; return "Create a Git repository";
                case "tag": weight = p.Count > 1 ? 4 : 1; return p.Count > 1 ? "Tag the commit " + p[1] : "List the Git tags";
                case "config": weight = p.Count > 2 ? 4 : 1; return p.Count > 2 ? "Change a Git setting" : "Show the Git settings";
            }
            weight = 2;
            return sub.Length > 0 ? "Run git " + sub : "Run git";
        }

        static string Npm(string tool, List<string> p, out int weight)
        {
            string sub = p.Count > 0 ? p[0].ToLowerInvariant() : "";
            weight = 2;
            switch (sub)
            {
                case "": case "install": case "i": case "ci": case "add":
                    weight = 4;
                    if (p.Count > 1) return "Install " + Names(p, 1) + " with " + tool;
                    if (sub == "add") return "Add packages with " + tool;
                    return "Install the project's packages";
                case "uninstall": case "remove": case "rm": case "un": weight = 4; return "Remove " + (p.Count > 1 ? Names(p, 1) : "packages") + " with " + tool;
                case "update": case "upgrade": case "up": weight = 4; return "Update packages with " + tool;
                case "run": case "run-script": return p.Count > 1 ? "Run the " + tool + " script " + p[1] : "List the " + tool + " scripts";
                case "start": return "Start the app with " + tool;
                case "test": case "t": return "Run the tests with " + tool;
                case "build": return "Build the project with " + tool;
                case "init": case "create": weight = 4; return "Create a new project with " + tool;
                case "audit": weight = 1; return "Check the packages for known security problems";
                case "outdated": case "list": case "ls": weight = 1; return "List the packages";
                case "publish": weight = 4; return "Publish the package";
                case "exec": case "dlx": return p.Count > 1 ? "Run " + Short(p[1]) + " with " + tool : "Run a package with " + tool;
            }
            if (tool == "yarn" || tool == "pnpm" || tool == "bun") return "Run the " + tool + " script " + sub;
            return "Run " + tool + " " + sub;
        }

        static string Pip(List<string> p, List<string> w, out int weight)
        {
            string sub = p.Count > 0 ? p[0].ToLowerInvariant() : "";
            weight = 4;
            string req = After(w, "-r", "--requirement");
            if (sub == "install") return req != null ? "Install the Python packages listed in " + Short(req) : p.Count > 1 ? "Install the Python packages " + Names(p, 1) : "Install Python packages";
            if (sub == "uninstall") return "Remove the Python packages " + (p.Count > 1 ? Names(p, 1) : "");
            weight = 1;
            if (sub == "list" || sub == "freeze") return "List the installed Python packages";
            if (sub == "show") return "Show details of " + (p.Count > 1 ? p[1] : "a package");
            weight = 2;
            return "Run pip " + sub;
        }

        static string DotNet(List<string> p, out int weight)
        {
            string sub = p.Count > 0 ? p[0].ToLowerInvariant() : "";
            weight = 2;
            switch (sub)
            {
                case "build": return "Build the .NET project";
                case "run": return "Run the .NET project";
                case "test": return "Run the .NET tests";
                case "new": weight = 4; return "Create a new .NET " + (p.Count > 1 ? p[1] + " " : "") + "project";
                case "add": weight = 4; return p.Count > 2 && p[1] == "package" ? "Add the NuGet package " + p[2] : "Add a reference to the .NET project";
                case "restore": weight = 4; return "Restore the .NET packages";
                case "publish": weight = 4; return "Publish the .NET project";
                case "clean": weight = 4; return "Clean the .NET build output";
                case "tool": weight = 4; return "Manage .NET tools";
            }
            return p.Count > 0 && p[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "Run " + Short(p[0]) : "Run dotnet " + sub;
        }

        static string Inner(string c, int depth, out int weight)
        {
            string t = c.Trim();
            if (t.Length >= 2 && (t[0] == '"' || t[0] == '\'') && t[t.Length - 1] == t[0]) t = t.Substring(1, t.Length - 2);
            weight = -1;
            List<string> steps = Steps(t, depth, out weight);
            return steps.Count == 0 ? null : Join(steps);
        }

        static List<string> Steps(string c, int depth, out int top)
        {
            List<string> steps = new List<string>();
            List<Seg> segs = Split(c);
            string folder = null, best = null;
            int bestW = -1, firstAt = 0, most = -1;
            bool started = false;
            Action flush = delegate
            {
                if (best != null) { steps.Add(best); if (bestW > most) most = bestW; }
                best = null;
                bestW = -1;
            };
            string pattern = null, saved = null;
            Action finish = delegate
            {
                if (best != null && pattern != null && best.StartsWith("List ", StringComparison.Ordinal)) best += " and search them for \u201C" + pattern + "\u201D";
                if (best != null && saved != null && bestW < 4) { best += " and save the output to " + Short(saved); bestW = 4; }
                pattern = null;
                saved = null;
                flush();
            };
            foreach (Seg sg in segs)
            {
                if (sg.StepStart) { if (started) finish(); started = true; firstAt = 0; }
                if (sg.Text.Trim().Length == 0) continue;
                int wgt;
                string d = One(sg.Text, firstAt == 0, depth, out wgt);
                firstAt++;
                string to = Redirect(sg.Text);
                if (to != null) saved = to;
                if (wgt == -2)
                {
                    if (steps.Count == 0 && best == null) folder = d;
                    continue;
                }
                if (d == null) continue;
                if (d.Length > 0 && d[0] == '\u0001') { pattern = d.Substring(1); continue; }
                if (wgt > bestW) { best = d; bestW = wgt; }
            }
            finish();
            top = most;
            if (steps.Count > 1) steps.RemoveAll(delegate (string x) { return x == "Print text" || x == "Wait a moment" || x == "Set a variable"; });
            if (folder != null && folder.Length > 0 && steps.Count > 0) steps[0] = "In " + folder + ": " + Lower(steps[0]);
            return steps;
        }

        static string Lower(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 2 || !char.IsUpper(s[0]) || char.IsUpper(s[1])) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        static string Join(List<string> steps)
        {
            StringBuilder sb = new StringBuilder(steps[0]);
            int shown = 1;
            for (int i = 1; i < steps.Count && shown < 3; i++)
            {
                string next = ", then " + Lower(steps[i]);
                if (sb.Length + next.Length > 150) break;
                sb.Append(next);
                shown++;
            }
            int more = steps.Count - shown;
            if (more > 0) sb.Append(more == 1 ? " and 1 more step" : " and " + more + " more steps");
            return sb.ToString();
        }

        public static string Describe(string command)
        {
            string c = (command ?? "").Trim();
            if (c.Length == 0) return "";
            if (Regex.IsMatch(c, @"^(exit|logout)(\s+\d+)?$", RegexOptions.IgnoreCase)) return "Close this shell (in a session on a server, this disconnects from it)";
            int top;
            List<string> steps;
            try { steps = Steps(c, 0, out top); }
            catch { steps = new List<string>(); }
            if (steps.Count == 0) return "Run the command shown below";
            return Join(steps);
        }
    }

    static class Sandbox
    {
        const uint TOKEN_DUPLICATE = 0x0002, TOKEN_QUERY = 0x0008, TOKEN_ASSIGN_PRIMARY = 0x0001, TOKEN_ADJUST_DEFAULT = 0x0080, MAXIMUM_ALLOWED = 0x02000000;
        const int TokenIntegrityLevel = 25, SecurityImpersonation = 2, TokenPrimary = 1, FileObject = 1, LabelInfo = 0x10;
        const uint SE_GROUP_INTEGRITY = 0x20, HANDLE_FLAG_INHERIT = 1, INFINITE = 0xFFFFFFFF;
        const uint CREATE_SUSPENDED = 0x4, CREATE_UNICODE_ENVIRONMENT = 0x400, CREATE_NO_WINDOW = 0x08000000, EXTENDED_STARTUPINFO_PRESENT = 0x80000;
        const int STARTF_USESTDHANDLES = 0x100;
        static readonly IntPtr HandleList = (IntPtr)0x20002;

        [StructLayout(LayoutKind.Sequential)]
        struct SecurityAttributes { public int Length; public IntPtr Descriptor; public bool Inherit; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StartupInfo
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr AttributeList; }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int level, int type, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool ConvertStringSidToSid(string sid, out IntPtr psid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool SetTokenInformation(IntPtr token, int cls, IntPtr info, int length);
        [DllImport("advapi32.dll")] static extern int GetLengthSid(IntPtr sid);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessAsUser(IntPtr token, string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation info);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, int revision, out IntPtr descriptor, out int size);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetSecurityDescriptorSacl(IntPtr descriptor, out bool present, out IntPtr sacl, out bool defaulted);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int SetNamedSecurityInfo(string name, int type, int info, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int GetNamedSecurityInfo(string name, int type, int info, IntPtr owner, IntPtr group, IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetFileSecurity(string name, int info, IntPtr descriptor);
        [DllImport("advapi32.dll")] static extern bool GetAce(IntPtr acl, int index, out IntPtr ace);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes attributes, int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr process, out int code);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool GetVolumeInformation(string root, StringBuilder name, int nameSize, IntPtr serial, IntPtr maxLength, out uint flags, StringBuilder fs, int fsSize);
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id, int flags, IntPtr token, out IntPtr path);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr memory);

        static readonly object gate = new object();
        static readonly Dictionary<string, string> ready = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool dataGuarded;
        static readonly IntPtr InvalidHandle = new IntPtr(-1);
        const uint WRITE_OWNER = 0x80000, READ_CONTROL = 0x20000, FILE_READ_ATTRIBUTES = 0x80;

        [StructLayout(LayoutKind.Sequential)]
        struct ByHandleInfo
        {
            public uint Attributes, CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetFileInformationByHandle(IntPtr file, out ByHandleInfo info);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool SetKernelObjectSecurity(IntPtr handle, int info, IntPtr descriptor);

        public static bool Supported { get { return Path.DirectorySeparatorChar == '\\' && Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 6; } }

        public static readonly Regex Blocked = new Regex(@"access (is |was )?denied|access to the path .{1,300}? is denied|unauthorizedaccessexception|permissionerror|\[winerror 5\]|\beacces\b|\beperm\b|permission denied|operation not permitted|requested operation requires elevation|could not read (username|password)|terminal prompts disabled", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static string Known(string guid)
        {
            try
            {
                Guid id = new Guid(guid);
                IntPtr p;
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out p) != 0) return null;
                try { return Marshal.PtrToStringUni(p); }
                finally { CoTaskMemFree(p); }
            }
            catch { return null; }
        }

        public static string LowRoot
        {
            get
            {
                string low = Supported ? Known("A520A1A4-1780-4FF6-BD18-167343C5AF16") : null;
                if (string.IsNullOrEmpty(low)) low = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData"), "LocalLow");
                return Path.Combine(low, Program.AppName);
            }
        }

        public static Dictionary<string, string> Env()
        {
            string root = LowRoot;
            string temp = Path.Combine(root, "temp"), cache = Path.Combine(root, "cache");
            Dictionary<string, string> e = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            e["TEMP"] = temp;
            e["TMP"] = temp;
            e["npm_config_cache"] = Path.Combine(cache, "npm");
            e["YARN_CACHE_FOLDER"] = Path.Combine(cache, "yarn");
            e["PIP_CACHE_DIR"] = Path.Combine(cache, "pip");
            e["PYTHONPYCACHEPREFIX"] = Path.Combine(cache, "pycache");
            e["NUGET_PACKAGES"] = Path.Combine(cache, "nuget");
            e["NUGET_HTTP_CACHE_PATH"] = Path.Combine(cache, "nuget-http");
            e["DOTNET_CLI_HOME"] = Path.Combine(root, "dotnet");
            e["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            e["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            e["DOTNET_NOLOGO"] = "1";
            e["GOCACHE"] = Path.Combine(cache, "go-build");
            e["GOMODCACHE"] = Path.Combine(cache, "go-mod");
            e["XDG_CACHE_HOME"] = cache;
            e["MPLCONFIGDIR"] = Path.Combine(cache, "matplotlib");
            e["PSModuleAnalysisCachePath"] = Path.Combine(Path.Combine(cache, "powershell"), "ModuleAnalysisCache");
            return e;
        }

        static string Trim(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            try { p = Path.GetFullPath(p); } catch { return null; }
            return p.Length > 3 ? p.TrimEnd('\\', '/') : p;
        }

        static bool Under(string path, string folder)
        {
            if (path == null || folder == null) return false;
            if (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)) return true;
            string f = folder.EndsWith("\\") ? folder : folder + "\\";
            return path.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        public static string Broad(string folder)
        {
            string f = Trim(folder);
            if (f == null) return "MinGent can't read its path";
            string why = BroadAt(f);
            if (why != null) return why;
            string real = null;
            try { real = Trim(AgentPaths.Real(f)); } catch { }
            if (real != null && !string.Equals(real, f, StringComparison.OrdinalIgnoreCase))
            {
                why = BroadAt(real);
                if (why != null) return why + " (it is reached through a link)";
            }
            return null;
        }

        static string BroadAt(string f)
        {
            if (f.StartsWith("\\\\")) return "it is on a network share";
            if (f.Length <= 3) return "it is a whole drive";
            try { if (new DriveInfo(Path.GetPathRoot(f)).DriveType == DriveType.Network) return "it is on a network drive"; } catch { }
            string home = Trim(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            List<KeyValuePair<string, string>> whole = new List<KeyValuePair<string, string>>();
            Action<string, string> add = delegate (string path, string name) { string t = Trim(path); if (t != null) whole.Add(new KeyValuePair<string, string>(t, name)); };
            add(home, "your user folder");
            if (home != null) add(Path.GetDirectoryName(home), "the folder with every user's files");
            add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "your Desktop");
            add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "your Documents folder");
            add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "your Pictures folder");
            add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "your Music folder");
            add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "your Videos folder");
            if (Supported) add(Known("374DE290-123F-4565-9164-39C4925E467B"), "your Downloads folder");
            foreach (string v in new string[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }) add(Environment.GetEnvironmentVariable(v), "your OneDrive folder");
            foreach (KeyValuePair<string, string> w in whole) if (Under(w.Key, f)) return string.Equals(w.Key, f, StringComparison.OrdinalIgnoreCase) ? "it is " + w.Value : "it holds " + w.Value;
            string app = Trim(AppDomain.CurrentDomain.BaseDirectory);
            if (app != null && (Under(f, app) || Under(app, f))) return "it holds MinGent itself";
            List<string> inside = new List<string>();
            foreach (Environment.SpecialFolder sf in new Environment.SpecialFolder[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
                inside.Add(Trim(Environment.GetFolderPath(sf)));
            foreach (string s in inside) if (s != null && (Under(f, s) || Under(s, f))) return "it is a Windows or program folder";
            foreach (string s in new string[] { Trim(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)), Trim(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)), Trim(Path.GetDirectoryName(LowRoot)) })
                if (s != null && (Under(f, s) || Under(s, f))) return "it is a folder where programs keep their own data";
            string data = Trim(Program.DataDir);
            if (data != null && (Under(f, data) || Under(data, f))) return "it holds MinGent's own data";
            return null;
        }

        static bool LowLabel(string path)
        {
            IntPtr sacl, sd;
            if (GetNamedSecurityInfo(path, FileObject, LabelInfo, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out sacl, out sd) != 0) return false;
            try
            {
                if (sacl == IntPtr.Zero) return false;
                int count = Marshal.ReadInt16(sacl, 4) & 0xFFFF;
                for (int i = 0; i < count; i++)
                {
                    IntPtr ace;
                    if (!GetAce(sacl, i, out ace)) continue;
                    int type = Marshal.ReadByte(ace, 0), flags = Marshal.ReadByte(ace, 1);
                    if (type != 0x11 || (flags & 8) != 0) continue;
                    if (Marshal.ReadByte(ace, 8) == 1 && Marshal.ReadByte(ace, 9) >= 1 && Marshal.ReadByte(ace, 15) == 16 && Marshal.ReadInt32(ace, 16) <= 0x1000) return true;
                }
                return false;
            }
            finally { LocalFree(sd); }
        }

        public static string GitWatch(string workspace)
        {
            if (!Supported) return null;
            try
            {
                string g = Path.Combine(Path.GetFullPath(workspace), ".git");
                if (!Directory.Exists(g) || Link(g) || LowLabel(g)) return null;
                return g;
            }
            catch { return null; }
        }

        public static bool GitReplaced(string watched)
        {
            if (string.IsNullOrEmpty(watched)) return false;
            try { return !Directory.Exists(watched) || Link(watched) || LowLabel(watched); }
            catch { return false; }
        }

        public static bool Labeled(string path)
        {
            IntPtr sacl, sd;
            if (GetNamedSecurityInfo(path, FileObject, LabelInfo, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out sacl, out sd) != 0) return false;
            try
            {
                if (sacl == IntPtr.Zero) return false;
                int count = Marshal.ReadInt16(sacl, 4) & 0xFFFF;
                for (int i = 0; i < count; i++)
                {
                    IntPtr ace;
                    if (!GetAce(sacl, i, out ace)) continue;
                    int type = Marshal.ReadByte(ace, 0), flags = Marshal.ReadByte(ace, 1);
                    if (type != 0x11 || (flags & 3) != 3 || (flags & 8) != 0) continue;
                    if (Marshal.ReadByte(ace, 8) != 1 || Marshal.ReadByte(ace, 9) < 1 || Marshal.ReadByte(ace, 15) != 16) continue;
                    if (Marshal.ReadInt32(ace, 16) <= 0x1000) return true;
                }
                return false;
            }
            finally { LocalFree(sd); }
        }

        static bool Secured(string path)
        {
            try
            {
                StringBuilder name = new StringBuilder(261), fs = new StringBuilder(261);
                uint flags;
                if (!GetVolumeInformation(Path.GetPathRoot(path), name, name.Capacity, IntPtr.Zero, IntPtr.Zero, out flags, fs, fs.Capacity)) return true;
                return (flags & 0x8) != 0;
            }
            catch { return true; }
        }

        static bool Link(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return false; }
        }

        static string Long(string p)
        {
            if (p.Length < 240 || p.StartsWith("\\\\?\\", StringComparison.Ordinal)) return p;
            return p.StartsWith("\\\\", StringComparison.Ordinal) ? "\\\\?\\UNC\\" + p.Substring(2) : "\\\\?\\" + p;
        }

        static readonly HashSet<string> KeptOut = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".hg", ".svn" };

        static bool LabelOne(string path, IntPtr sd, bool dir, out bool skipped)
        {
            skipped = false;
            IntPtr h = CreateFile(Long(path), WRITE_OWNER | READ_CONTROL | FILE_READ_ATTRIBUTES, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
            if (h == InvalidHandle) return false;
            try
            {
                ByHandleInfo info;
                if (!GetFileInformationByHandle(h, out info)) return false;
                if ((info.Attributes & 0x400) != 0 || (!dir && info.Links > 1)) { skipped = true; return true; }
                return SetKernelObjectSecurity(h, LabelInfo, sd);
            }
            finally { CloseHandle(h); }
        }

        static string LabelTree(string root, CancelToken ct, Action<int> progress, out bool stopped)
        {
            stopped = false;
            if (Link(root)) return "it is a link to another folder";
            IntPtr dirSd, fileSd;
            int size;
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;OICI;NW;;;LW)", 1, out dirSd, out size)) return "Windows could not build the label (error " + Marshal.GetLastWin32Error() + ")";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;;NW;;;LW)", 1, out fileSd, out size)) { LocalFree(dirSd); return "Windows could not build the label (error " + Marshal.GetLastWin32Error() + ")"; }
            try
            {
                Stack<DirectoryInfo> todo = new Stack<DirectoryInfo>();
                todo.Push(new DirectoryInfo(root));
                int count = 0;
                DateTime told = DateTime.UtcNow;
                while (todo.Count > 0)
                {
                    DirectoryInfo d = todo.Pop();
                    try
                    {
                        foreach (FileSystemInfo f in d.EnumerateFileSystemInfos())
                        {
                            if (ct != null && ct.IsCancelled) { stopped = true; return null; }
                            if ((f.Attributes & FileAttributes.ReparsePoint) != 0 || KeptOut.Contains(f.Name)) continue;
                            bool dir = (f.Attributes & FileAttributes.Directory) != 0, skipped;
                            bool done = LabelOne(f.FullName, dir ? dirSd : fileSd, dir, out skipped);
                            if (dir && done && !skipped) todo.Push((DirectoryInfo)f);
                            count++;
                            if (progress != null && (count & 255) == 0 && (DateTime.UtcNow - told).TotalSeconds >= 2) { told = DateTime.UtcNow; progress(count); }
                        }
                    }
                    catch (Exception) { }
                }
                if (ct != null && ct.IsCancelled) { stopped = true; return null; }
                bool rootSkipped;
                if (!LabelOne(root, dirSd, true, out rootSkipped) || rootSkipped)
                {
                    int err = Marshal.GetLastWin32Error();
                    return rootSkipped ? "it is a link to another folder" : err == 5 ? "Windows refused to change it (access denied)" : "Windows refused to change it (error " + err + ")";
                }
                return null;
            }
            finally
            {
                LocalFree(dirSd);
                LocalFree(fileSd);
            }
        }

        static string LabelPlain(string path, string sddl)
        {
            IntPtr sd;
            int size;
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out sd, out size)) return "no label";
            try { return SetFileSecurity(path, LabelInfo, sd) ? null : "refused"; }
            finally { LocalFree(sd); }
        }

        static string LabelBranch(string dir, string sddl, string fileSddl)
        {
            if (Link(dir)) return "link";
            string first = LabelPlain(dir, sddl);
            if (first != null) return first;
            try
            {
                foreach (string f in Directory.GetFiles(dir)) if (!Link(f)) LabelPlain(f, fileSddl);
                foreach (string d in Directory.GetDirectories(dir)) LabelBranch(d, sddl, fileSddl);
            }
            catch (Exception) { }
            return null;
        }

        public static bool Pending(string workspace)
        {
            string ws = Trim(workspace);
            if (ws == null || !Supported) return false;
            lock (gate)
            {
                string known;
                if (ready.TryGetValue(ws, out known) && (known != null || Labeled(ws))) return false;
            }
            return Broad(ws) == null && Directory.Exists(ws) && Secured(ws) && !Labeled(ws);
        }

        static void MakeDirs()
        {
            try
            {
                string root = LowRoot, parent = Path.GetDirectoryName(root);
                if (parent == null || !Directory.Exists(parent) || Link(parent)) return;
                if (Directory.Exists(root) && Link(root)) return;
                Directory.CreateDirectory(root);
                string top = Trim(root);
                foreach (string d in Env().Values)
                {
                    string t = Path.IsPathRooted(d) ? Trim(d) : null;
                    if (t == null || !Under(t, top) || string.Equals(t, top, StringComparison.OrdinalIgnoreCase)) continue;
                    bool linked = false;
                    for (string at = Path.GetDirectoryName(t); at != null && at.Length > top.Length; at = Path.GetDirectoryName(at))
                        if (Directory.Exists(at) && Link(at)) { linked = true; break; }
                    if (!linked && !(Directory.Exists(t) && Link(t))) Directory.CreateDirectory(t);
                }
            }
            catch { }
        }

        public static string Prepare(string workspace)
        {
            return Prepare(workspace, null, null);
        }

        public static string Prepare(string workspace, CancelToken ct, Action<int> progress)
        {
            if (!Supported) return "the sandbox only works on Windows";
            string ws = Trim(workspace);
            if (ws == null) return "MinGent can't read the workspace's path";
            lock (gate)
            {
                GuardData();
                MakeDirs();
            }
            while (true)
            {
                lock (gate)
                {
                    string known;
                    if (ready.TryGetValue(ws, out known) && (known != null || Labeled(ws))) return known;
                    if (!busy.Contains(ws)) { busy.Add(ws); break; }
                }
                if (ct != null && ct.IsCancelled) return "MinGent was still setting up the sandbox for " + ws;
                Thread.Sleep(200);
            }
            string note = null;
            bool stopped = false;
            try
            {
                string why = Broad(ws);
                if (why == null && Directory.Exists(ws) && Secured(ws) && !Labeled(ws)) why = LabelTree(ws, ct, progress, out stopped);
                if (!stopped && why != null) note = "MinGent did not open " + ws + " to sandboxed commands because " + Lower(why);
            }
            catch (Exception ex) { note = "MinGent did not open " + ws + " to sandboxed commands because " + Lower(ex.Message); }
            lock (gate)
            {
                busy.Remove(ws);
                if (!stopped) ready[ws] = note;
            }
            return stopped ? "MinGent stopped setting up the sandbox for " + ws : note;
        }

        static string Lower(string s) { return s.Length > 0 && char.IsUpper(s[0]) && (s.Length == 1 || !char.IsUpper(s[1])) ? char.ToLowerInvariant(s[0]) + s.Substring(1) : s; }

        static void GuardData()
        {
            if (dataGuarded) return;
            dataGuarded = true;
            try
            {
                string d = Trim(Program.DataDir);
                if (d == null || !Directory.Exists(d) || Link(d) || !Secured(d)) return;
                LabelPlain(d, "S:(ML;OIIONP;NRNW;;;ME)");
                foreach (string f in Directory.GetFiles(d)) if (!Link(f)) LabelPlain(f, "S:(ML;;NRNW;;;ME)");
                foreach (string sub in new string[] { "chats", "backup", Path.Combine("admin", "backup") })
                {
                    string dir = Path.Combine(d, sub);
                    if (Directory.Exists(dir)) LabelBranch(dir, "S:(ML;OICI;NRNW;;;ME)", "S:(ML;;NRNW;;;ME)");
                }
            }
            catch { }
        }

        static IntPtr LowToken(out string error)
        {
            error = null;
            IntPtr own, dup = IntPtr.Zero, sid = IntPtr.Zero, info = IntPtr.Zero;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT, out own)) { error = "could not open MinGent's own sign-in token (error " + Marshal.GetLastWin32Error() + ")"; return IntPtr.Zero; }
            try
            {
                if (!DuplicateTokenEx(own, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out dup)) { error = "could not copy the sign-in token (error " + Marshal.GetLastWin32Error() + ")"; return IntPtr.Zero; }
                if (!ConvertStringSidToSid("S-1-16-4096", out sid)) { error = "could not build the low integrity level"; CloseHandle(dup); return IntPtr.Zero; }
                int sidLen = GetLengthSid(sid);
                int size = IntPtr.Size * 2 + sidLen;
                info = Marshal.AllocHGlobal(size);
                Marshal.WriteIntPtr(info, sid);
                Marshal.WriteInt32(info, IntPtr.Size, (int)SE_GROUP_INTEGRITY);
                if (!SetTokenInformation(dup, TokenIntegrityLevel, info, size)) { error = "could not lower the sign-in token (error " + Marshal.GetLastWin32Error() + ")"; CloseHandle(dup); return IntPtr.Zero; }
                return dup;
            }
            finally
            {
                if (info != IntPtr.Zero) Marshal.FreeHGlobal(info);
                if (sid != IntPtr.Zero) LocalFree(sid);
                CloseHandle(own);
            }
        }

        static IntPtr Block(Dictionary<string, string> env)
        {
            List<string> keys = new List<string>(env.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            StringBuilder sb = new StringBuilder();
            foreach (string k in keys)
            {
                string v = env[k];
                if (k.Length == 0 || k.IndexOf('=') >= 0 || k.IndexOf('\0') >= 0 || v == null || v.IndexOf('\0') >= 0) continue;
                sb.Append(k).Append('=').Append(v).Append('\0');
            }
            sb.Append('\0');
            return Marshal.StringToHGlobalUni(sb.ToString());
        }

        static readonly object processLock = FindProcessLock();

        static object FindProcessLock()
        {
            try
            {
                System.Reflection.FieldInfo f = typeof(Process).GetField("s_CreateProcessLock", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                object o = f != null ? f.GetValue(null) : null;
                if (o != null) return o;
            }
            catch { }
            return new object();
        }

        public static string Start(string exe, string args, string cwd, Dictionary<string, string> env, out int pid, out IntPtr process, out IntPtr thread, out Stream output, out Stream errors)
        {
            lock (processLock) return StartLocked(exe, args, cwd, env, out pid, out process, out thread, out output, out errors);
        }

        static string StartLocked(string exe, string args, string cwd, Dictionary<string, string> env, out int pid, out IntPtr process, out IntPtr thread, out Stream output, out Stream errors)
        {
            pid = 0;
            process = thread = IntPtr.Zero;
            output = errors = null;
            if (!Supported) return "the sandbox only works on Windows";
            string err;
            IntPtr token = LowToken(out err);
            if (token == IntPtr.Zero) return err;
            IntPtr outRead = IntPtr.Zero, outWrite = IntPtr.Zero, errRead = IntPtr.Zero, errWrite = IntPtr.Zero, inRead = IntPtr.Zero, inWrite = IntPtr.Zero;
            IntPtr list = IntPtr.Zero, handles = IntPtr.Zero, block = IntPtr.Zero;
            bool listReady = false;
            try
            {
                SecurityAttributes sa = new SecurityAttributes();
                sa.Length = Marshal.SizeOf(typeof(SecurityAttributes));
                sa.Inherit = true;
                if (!CreatePipe(out outRead, out outWrite, ref sa, 0) || !CreatePipe(out errRead, out errWrite, ref sa, 0) || !CreatePipe(out inRead, out inWrite, ref sa, 0))
                    return "could not create the pipes for the output (error " + Marshal.GetLastWin32Error() + ")";
                SetHandleInformation(outRead, HANDLE_FLAG_INHERIT, 0);
                SetHandleInformation(errRead, HANDLE_FLAG_INHERIT, 0);
                SetHandleInformation(inWrite, HANDLE_FLAG_INHERIT, 0);
                IntPtr size = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                if (size == IntPtr.Zero) return "could not set up the process (error " + Marshal.GetLastWin32Error() + ")";
                list = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)) return "could not set up the process (error " + Marshal.GetLastWin32Error() + ")";
                listReady = true;
                handles = Marshal.AllocHGlobal(IntPtr.Size * 3);
                Marshal.WriteIntPtr(handles, 0, inRead);
                Marshal.WriteIntPtr(handles, IntPtr.Size, outWrite);
                Marshal.WriteIntPtr(handles, IntPtr.Size * 2, errWrite);
                if (!UpdateProcThreadAttribute(list, 0, HandleList, handles, (IntPtr)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero)) return "could not limit what the command inherits (error " + Marshal.GetLastWin32Error() + ")";
                StartupInfoEx si = new StartupInfoEx();
                si.StartupInfo.cb = Marshal.SizeOf(typeof(StartupInfoEx));
                si.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
                si.StartupInfo.hStdInput = inRead;
                si.StartupInfo.hStdOutput = outWrite;
                si.StartupInfo.hStdError = errWrite;
                si.AttributeList = list;
                block = Block(env);
                StringBuilder cmd = new StringBuilder("\"" + exe + "\"" + (string.IsNullOrEmpty(args) ? "" : " " + args));
                ProcessInformation pi;
                if (!CreateProcessAsUser(token, exe, cmd, IntPtr.Zero, IntPtr.Zero, true, CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT, block, cwd, ref si, out pi))
                    return "Windows would not start the command with low rights (error " + Marshal.GetLastWin32Error() + ")";
                pid = pi.dwProcessId;
                process = pi.hProcess;
                thread = pi.hThread;
                output = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(outRead, true), FileAccess.Read, 4096, false);
                outRead = IntPtr.Zero;
                errors = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(errRead, true), FileAccess.Read, 4096, false);
                errRead = IntPtr.Zero;
                return null;
            }
            catch (Exception ex)
            {
                if (process != IntPtr.Zero) { TerminateProcess(process, 1); CloseHandle(process); process = IntPtr.Zero; }
                if (thread != IntPtr.Zero) { CloseHandle(thread); thread = IntPtr.Zero; }
                if (output != null) { output.Dispose(); output = null; }
                return "could not start the command in the sandbox (" + ex.Message + ")";
            }
            finally
            {
                foreach (IntPtr h in new IntPtr[] { outWrite, errWrite, inRead, inWrite, outRead, errRead })
                    if (h != IntPtr.Zero) CloseHandle(h);
                if (listReady) DeleteProcThreadAttributeList(list);
                if (list != IntPtr.Zero) Marshal.FreeHGlobal(list);
                if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
                if (block != IntPtr.Zero) Marshal.FreeHGlobal(block);
                CloseHandle(token);
            }
        }

        public static void Resume(IntPtr thread)
        {
            if (thread == IntPtr.Zero) return;
            ResumeThread(thread);
            CloseHandle(thread);
        }

        public static int Wait(IntPtr process)
        {
            int code = -1;
            if (process == IntPtr.Zero) return code;
            WaitForSingleObject(process, INFINITE);
            if (!GetExitCodeProcess(process, out code)) code = -1;
            CloseHandle(process);
            return code;
        }

        public static void Abandon(IntPtr process, IntPtr thread)
        {
            if (process != IntPtr.Zero) { TerminateProcess(process, 1); CloseHandle(process); }
            if (thread != IntPtr.Zero) CloseHandle(thread);
        }
    }
}
