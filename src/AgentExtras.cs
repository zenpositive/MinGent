using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Accessibility;
using Microsoft.Win32;

namespace MinGent
{
    static class MemoryStore
    {
        static readonly object gate = new object();
        public const int MaxChars = 12000;

        public static string FilePath { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), "memory.md"); } }

        public static List<string> Load()
        {
            List<string> l = new List<string>();
            lock (gate)
            {
                try
                {
                    string tmp = FilePath + ".tmp";
                    if (File.Exists(tmp) && (!File.Exists(FilePath) || new FileInfo(FilePath).Length == 0 || File.ReadAllText(FilePath, Encoding.UTF8).Trim('\0', ' ', '\r', '\n').Length == 0))
                        SecureFile.Replace(tmp, FilePath);
                    if (!File.Exists(FilePath)) return l;
                    foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        string t = raw.Trim();
                        if (t.StartsWith("- ")) t = t.Substring(2).Trim();
                        else if (t.StartsWith("#") || t.Length == 0) continue;
                        if (t.Length > 0) l.Add(t);
                    }
                }
                catch { }
            }
            return l;
        }

        public static string Save(List<string> items)
        {
            if (Program.Erased) return null;
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    StringBuilder sb = new StringBuilder();
                    sb.Append("# What the MinGent assistant remembers about you and this PC\r\n");
                    sb.Append("# One fact per line. You can edit or delete lines; the agent reads this file at the start of every chat.\r\n\r\n");
                    foreach (string i in items) sb.Append("- ").Append(i.Replace("\r", " ").Replace("\n", " ").Trim()).Append("\r\n");
                    string tmp = FilePath + ".tmp";
                    if (File.Exists(tmp)) SecureFile.Erase(tmp);
                    SecureFile.WriteAll(tmp, sb.ToString());
                    SecureFile.Replace(tmp, FilePath);
                    return null;
                }
                catch (Exception ex) { return ex.Message; }
            }
        }

        public static int CountPresent(List<string> facts)
        {
            if (facts == null || facts.Count == 0) return 0;
            int n = 0;
            foreach (string i in Load())
                foreach (string f in facts)
                    if (string.Equals(i, f, StringComparison.OrdinalIgnoreCase)) { n++; break; }
            return n;
        }

        public static int Forget(List<string> facts)
        {
            if (facts == null || facts.Count == 0) return 0;
            lock (gate)
            {
                List<string> items = Load();
                int before = items.Count;
                items.RemoveAll(delegate (string i)
                {
                    foreach (string f in facts) if (string.Equals(i, f, StringComparison.OrdinalIgnoreCase)) return true;
                    return false;
                });
                int removed = before - items.Count;
                if (removed > 0 && Save(items) != null) return 0;
                return removed;
            }
        }

        public static string RawText()
        {
            try { return File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8) : ""; } catch { return ""; }
        }

        public static string SaveRaw(string text)
        {
            List<string> items = new List<string>();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string t = raw.Trim();
                if (t.StartsWith("#") || t.Length == 0) continue;
                if (t.StartsWith("- ")) t = t.Substring(2).Trim();
                if (t.Length > 0) items.Add(t.Length > 400 ? t.Substring(0, 400) : t);
            }
            return Save(items);
        }

        public static string ForPrompt(int maxChars)
        {
            List<string> items = Load();
            if (items.Count == 0) return null;
            StringBuilder sb = new StringBuilder();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                string line = "- " + items[i] + "\n";
                if (sb.Length + line.Length > maxChars) break;
                sb.Insert(0, line);
            }
            return sb.ToString();
        }
    }

    static class SecureFile
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle h, out FileInfoByHandle info);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct FileInfoByHandle
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        static bool SharedData(FileStream fs)
        {
            try
            {
                FileInfoByHandle info;
                return GetFileInformationByHandle(fs.SafeFileHandle, out info) && info.Links > 1;
            }
            catch { return false; }
        }

        public static bool Erase(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            FileAttributes at;
            try { at = File.GetAttributes(path); }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
            catch { return false; }
            if ((at & FileAttributes.Directory) != 0) return false;
            if ((at & FileAttributes.ReparsePoint) != 0)
            {
                try { File.Delete(path); } catch { }
                return !File.Exists(path);
            }
            try { if ((at & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, at & ~FileAttributes.ReadOnly); } catch { }
            bool opened = false, shared = false;
            foreach (FileShare share in new FileShare[] { FileShare.None, FileShare.ReadWrite | FileShare.Delete })
            {
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Write, share, 65536, FileOptions.WriteThrough))
                    {
                        shared = SharedData(fs);
                        if (!shared) Overwrite(fs);
                    }
                    opened = true;
                    break;
                }
                catch (FileNotFoundException) { return true; }
                catch (DirectoryNotFoundException) { return true; }
                catch { }
            }
            if (!opened) return false;
            if (!shared)
            {
                string anon = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N").Substring(0, 12));
                bool moved = false;
                try { File.Move(path, anon); moved = true; } catch { }
                if (moved)
                {
                    try { File.Delete(anon); } catch { }
                    if (!File.Exists(anon)) return true;
                    try { File.Move(anon, path); } catch { }
                    return false;
                }
            }
            try { File.Delete(path); } catch { }
            return !File.Exists(path);
        }

        static void Overwrite(FileStream fs)
        {
            long len = fs.Length;
            byte[] noise = new byte[65536];
            long done = 0;
            using (System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                while (done < len)
                {
                    rng.GetBytes(noise);
                    int n = (int)Math.Min(noise.Length, len - done);
                    fs.Write(noise, 0, n);
                    done += n;
                }
            }
            fs.Flush(true);
            fs.SetLength(0);
            fs.Flush(true);
        }

        public static void WriteAll(string path, string text)
        {
            byte[] data = new UTF8Encoding(false).GetBytes(text ?? "");
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
        }

        public static void Replace(string tmp, string target)
        {
            if (File.Exists(target) && !Erase(target))
            {
                File.Copy(tmp, target, true);
                Erase(tmp);
                return;
            }
            File.Move(tmp, target);
        }
    }

    class MemoryTool : AgentTool
    {
        public override string Name { get { return "memory"; } }
        public override string Description { get { return "Your long-term memory about this user and their PC, kept across chats (it is shown to you at the start of every chat). Use action \"add\" to save a durable fact you learned (hardware, installed tools and versions, project locations, how the user likes things done), \"remove\" to delete an outdated fact (give part of its text), or \"list\". Save short, specific facts. Never save passwords, keys or other secrets."; } }
        public override Dictionary<string, object> Schema
        {
            get
            {
                Dictionary<string, object> act = P("string", "add, remove or list");
                act["enum"] = Json.A("add", "remove", "list");
                return Obj("action", act, true, "fact", P("string", "The fact to add, or text identifying the fact to remove"), false);
            }
        }
        public override string Label(Dictionary<string, object> a)
        {
            string act = (S(a, "action") ?? "list").ToLowerInvariant();
            return act == "add" ? "Remember: " + Clip(S(a, "fact"), 60) : act == "remove" ? "Forget: " + Clip(S(a, "fact"), 60) : "Check memory";
        }

        static string Clip(string s, int n) { s = (s ?? "").Replace('\n', ' '); return s.Length > n ? s.Substring(0, n) + "\u2026" : s; }

        static readonly Regex Secretish = new Regex(@"(?i)(password|passwd|pwd|secret|api[_ -]?key|token|bearer|sk-[a-z0-9]{8,}|private key|-----BEGIN|\b[A-Za-z0-9+/]{40,}={0,2}\b)");

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            string act = (S(a, "action") ?? "list").Trim().ToLowerInvariant();
            string fact = (S(a, "fact") ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            List<string> items = MemoryStore.Load();
            if (act == "list")
            {
                if (items.Count == 0) return ToolOutcome.Ok("Memory is empty.", "empty");
                StringBuilder sb = new StringBuilder();
                foreach (string i in items) sb.Append("- ").Append(i).Append('\n');
                return ToolOutcome.Ok(sb.ToString(), items.Count + (items.Count == 1 ? " fact" : " facts"));
            }
            if (fact.Length == 0) return ToolOutcome.Fail("fact is required");
            if (act == "add")
            {
                if (fact.Length > 400) return ToolOutcome.Fail("keep each fact under 400 characters");
                if (Secretish.IsMatch(fact)) return ToolOutcome.Fail("this looks like it contains a password, key or token; secrets are never saved to memory");
                foreach (string i in items) if (string.Equals(i, fact, StringComparison.OrdinalIgnoreCase)) return ToolOutcome.Ok("Already remembered.", "already known");
                string deny = Guard(ctx, "Save to memory", fact);
                if (deny != null) { ToolOutcome d = ToolOutcome.Fail(deny); d.Denied = true; return d; }
                items.Add(fact);
                int total = 0;
                foreach (string i in items) total += i.Length + 3;
                while (total > MemoryStore.MaxChars && items.Count > 1) { total -= items[0].Length + 3; items.RemoveAt(0); }
                string err = MemoryStore.Save(items);
                if (err != null) return ToolOutcome.Fail("could not save the memory file: " + err);
                if (ctx.Session != null) lock (ctx.Session.Remembered) if (!ctx.Session.Remembered.Contains(fact)) ctx.Session.Remembered.Add(fact);
                return ToolOutcome.Ok("Saved to memory: " + fact, "saved");
            }
            if (act == "remove")
            {
                int idx = -1;
                for (int i = 0; i < items.Count; i++) if (items[i].IndexOf(fact, StringComparison.OrdinalIgnoreCase) >= 0) { idx = i; break; }
                if (idx < 0) return ToolOutcome.Fail("no remembered fact contains \"" + fact + "\"");
                string deny = Guard(ctx, "Remove from memory", items[idx]);
                if (deny != null) { ToolOutcome d = ToolOutcome.Fail(deny); d.Denied = true; return d; }
                string removed = items[idx];
                items.RemoveAt(idx);
                string err = MemoryStore.Save(items);
                if (err != null) return ToolOutcome.Fail("could not save the memory file: " + err);
                if (ctx.Session != null) lock (ctx.Session.Remembered) ctx.Session.Remembered.Remove(removed);
                return ToolOutcome.Ok("Removed from memory: " + removed, "removed");
            }
            return ToolOutcome.Fail("action must be add, remove or list");
        }

        static string Guard(ToolContext ctx, string title, string fact)
        {
            PermissionState ps = ctx.Session.Perms;
            bool tainted = ps.WebGuard && ps.Tainted;
            if (ps.Auto && !tainted) return null;
            PermissionAsk ask = new PermissionAsk();
            ask.Kind = "memory";
            ask.Title = title;
            ask.Detail = fact;
            ask.Key = tainted ? "" : "memory";
            ask.Reason = tainted ? "This chat has read web content, so changes to the agent's long-term memory need your OK." : "The agent keeps this in its memory for future chats (Settings > Memory shows all of it).";
            return ctx.Approve(ask) ? null : "The user did not allow changing the memory.";
        }
    }

    class ScheduleTaskTool : AgentTool
    {
        public override string Name { get { return "schedule_task"; } }
        public override string Description { get { return "Scheduled tasks: jobs MinGent starts by itself at a set time, each in a new chat that can't see this one, so write prompt as a complete instruction. Use it when the user asks for something to be done later or regularly. MinGent asks the user before it saves a change."; } }
        public override Dictionary<string, object> Schema
        {
            get
            {
                Dictionary<string, object> act = P("string", "What to do");
                act["enum"] = Json.A("add", "change", "remove", "list");
                Dictionary<string, object> rep = P("string", "How often");
                rep["enum"] = Json.A("daily", "weekdays", "weekly", "monthly", "once");
                return Obj("action", act, true, "name", P("string", "Task name; an existing one for change and remove"), false, "prompt", P("string", "The instruction to run"), false,
                    "repeat", rep, false, "time", P("string", "Such as 08:30"), false, "day", P("string", "weekly: weekday; monthly: 1-31; once: date like 2026-12-24"), false,
                    "folder", P("string", "Work folder; the default folder when left out"), false, "on", P("boolean", "false pauses it"), false);
            }
        }
        public override string Label(Dictionary<string, object> a)
        {
            string act = (S(a, "action") ?? "list").Trim().ToLowerInvariant(), n = (S(a, "name") ?? "").Replace('\n', ' ').Trim();
            if (n.Length > 40) n = n.Substring(0, 40) + "\u2026";
            return act == "add" ? "Schedule a task: " + n : act == "change" ? "Change the scheduled task " + n : act == "remove" ? "Remove the scheduled task " + n : "List the scheduled tasks";
        }

        static string Line(ScheduledTask t, DateTime now)
        {
            DateTime next = t.On ? Schedule.Next(t, t.LastRun > t.Created ? t.LastRun : t.Created) : DateTime.MaxValue;
            if (next != DateTime.MaxValue && next < now) next = now;
            string p = t.Prompt.Replace("\r", "").Replace("\n", " ").Trim();
            return "- \"" + t.Name + "\": " + Schedule.When(t) + (t.On ? "" : ", paused") + (next == DateTime.MaxValue ? (t.On ? ", no run left" : "") : ", next run " + next.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)) +
                   (string.IsNullOrEmpty(t.Folder) ? "" : ", in " + t.Folder) + ". It does: " + (p.Length > 300 ? p.Substring(0, 300) + "..." : p) + "\n";
        }

        static ScheduledTask Find(List<ScheduledTask> tasks, string name, out string wrong)
        {
            wrong = null;
            foreach (ScheduledTask t in tasks) if (string.Equals(t.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) return t;
            List<ScheduledTask> hits = new List<ScheduledTask>();
            foreach (ScheduledTask t in tasks) if (t.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(t);
            if (hits.Count == 1) return hits[0];
            StringBuilder sb = new StringBuilder();
            foreach (ScheduledTask t in hits.Count > 1 ? hits : tasks) sb.Append(sb.Length > 0 ? ", " : "").Append('"').Append(t.Name).Append('"');
            wrong = hits.Count > 1 ? "Several scheduled tasks match \"" + name + "\": " + sb + ". Use the full name." : tasks.Count == 0 ? "There are no scheduled tasks yet." : "No scheduled task is named \"" + name + "\". The tasks are: " + sb + ".";
            return null;
        }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            string act = (S(a, "action") ?? "list").Trim().ToLowerInvariant();
            DateTime now = DateTime.Now;
            List<ScheduledTask> tasks = Schedule.Load();
            if (act == "list")
            {
                if (tasks.Count == 0) return ToolOutcome.Ok("There are no scheduled tasks.", "none");
                StringBuilder sb = new StringBuilder("It is " + now.ToString("dddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " on this PC.\n");
                foreach (ScheduledTask each in tasks) sb.Append(Line(each, now));
                return ToolOutcome.Ok(sb.ToString(), tasks.Count + (tasks.Count == 1 ? " task" : " tasks"));
            }
            if (act != "add" && act != "change" && act != "remove") return ToolOutcome.Fail("action must be add, change, remove or list");
            string name = Regex.Replace((S(a, "name") ?? "").Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
            if (name.Length == 0) return ToolOutcome.Fail("name is required");
            if (name.Length > 60) return ToolOutcome.Fail("keep the name under 60 characters");
            string wrong;
            ScheduledTask was = Find(tasks, name, out wrong);
            if (act == "add" && was != null && string.Equals(was.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) return ToolOutcome.Fail("A scheduled task named \"" + was.Name + "\" already exists. Change it with action \"change\", or pick another name.");
            if (act == "add") was = null;
            else if (was == null) return ToolOutcome.Fail(wrong);
            if (act == "add" && tasks.Count >= Schedule.Most) return ToolOutcome.Fail("There is room for " + Schedule.Most + " scheduled tasks, and all are in use. The user can delete one in Settings > Scheduled.");
            ScheduledTask t = was != null ? was.Copy() : new ScheduledTask();
            PermissionState ps = ctx.Session.Perms;
            PermissionAsk ask = new PermissionAsk();
            ask.Kind = "schedule";
            ask.EvenInFull = true;
            ask.AllowRemember = false;
            ask.Category = "";
            if (act != "remove")
            {
                if (was == null) t.Name = name;
                string prompt = (S(a, "prompt") ?? "").Replace("\r\n", "\n").Trim();
                if (prompt.Length > 4000) return ToolOutcome.Fail("keep the prompt under 4000 characters");
                if (prompt.Length > 0) t.Prompt = prompt;
                string repeat = (S(a, "repeat") ?? "").Trim().ToLowerInvariant();
                if (repeat.Length > 0)
                {
                    if (Array.IndexOf(Schedule.Kinds, repeat) < 0) return ToolOutcome.Fail("repeat must be daily, weekdays, weekly, monthly or once");
                    t.Kind = repeat;
                }
                else if (was == null) return ToolOutcome.Fail("repeat is required: daily, weekdays, weekly, monthly or once");
                string time = (S(a, "time") ?? "").Trim();
                int hour, minute;
                if (time.Length > 0)
                {
                    if (!Schedule.TryTime(time, out hour, out minute)) return ToolOutcome.Fail("time is a time of day such as 08:00 or 17:30");
                    t.Hour = hour;
                    t.Minute = minute;
                }
                else if (was == null) return ToolOutcome.Fail("time is required, such as 08:00");
                string day = (S(a, "day") ?? "").Trim();
                bool needsDay = t.Kind == ScheduledTask.Weekly || t.Kind == ScheduledTask.Monthly || t.Kind == ScheduledTask.Once;
                if (day.Length > 0 || (needsDay && (was == null || was.Kind != t.Kind)))
                {
                    string bad = Schedule.SetDay(t, day);
                    if (bad != null) return ToolOutcome.Fail(bad.Replace("Day is", "day is") + " It is " + now.ToString("dddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " on this PC.");
                }
                if (a != null && a.ContainsKey("folder"))
                {
                    string folder = (S(a, "folder") ?? "").Trim();
                    if (folder.Length > 0)
                    {
                        string clean = Projects.Clean(folder);
                        string no = Projects.Check(folder);
                        if (clean == null || no != null) return ToolOutcome.Fail("The folder can't be used for a scheduled task: " + (no ?? "MinGent can't use that as a folder."));
                        folder = clean;
                    }
                    t.Folder = folder;
                }
                if (a != null && a.ContainsKey("on")) t.On = B(a, "on", true);
                else if (was == null) t.On = true;
                bool moved = was == null || was.Kind != t.Kind || was.Hour != t.Hour || was.Minute != t.Minute || was.Day != t.Day || was.Date != t.Date || (t.On && !was.On);
                if (moved && t.Kind == ScheduledTask.Once) t.LastRun = DateTime.MinValue;
                string check = Schedule.Check(t, moved ? now : DateTime.MinValue);
                if (check != null) return ToolOutcome.Fail(check + " It is " + now.ToString("dddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " on this PC.");
                if (moved) t.Created = now;
                ask.Title = was == null ? "Schedule a task: " + t.Name : "Change the scheduled task \"" + t.Name + "\"";
                ask.Summary = Schedule.When(t) + (t.On ? "" : " (paused)") + (string.IsNullOrEmpty(t.Folder) ? "" : ", in " + t.Folder);
                ask.Detail = t.Prompt;
                ask.Reason = "MinGent will start this task by itself at that time, in a new chat, and do what is written here. Settings > Scheduled shows all tasks.";
            }
            else
            {
                ask.Title = "Remove the scheduled task \"" + was.Name + "\"";
                ask.Summary = Schedule.When(was);
                ask.Detail = was.Prompt;
                ask.Reason = "The task will no longer run.";
            }
            if (ps.WebGuard && ps.Tainted) ask.Reason += " This chat has read web content (" + ps.TaintSource + "), so make sure a page didn't slip in this request.";
            ask.Key = "schedule:" + act + ":" + (was != null ? was.Id : name.ToLowerInvariant()) + ":" + (t.Prompt + "|" + Schedule.When(t) + "|" + t.On + "|" + t.Folder).GetHashCode().ToString(CultureInfo.InvariantCulture);
            if (!ctx.Approve(ask))
            {
                ToolOutcome d = ToolOutcome.Fail("The user did not allow this change to the scheduled tasks. Don't set it up another way; ask the user what they want instead.");
                d.Denied = true;
                d.Summary = "Not allowed";
                return d;
            }
            tasks = Schedule.Load();
            int at = was == null ? -1 : tasks.FindIndex(delegate (ScheduledTask x) { return x.Id == was.Id; });
            if (was != null && at < 0) return ToolOutcome.Fail("The task \"" + was.Name + "\" is no longer there; it was removed in the meantime.");
            if (act == "remove") tasks.RemoveAt(at);
            else if (was == null)
            {
                if (tasks.Count >= Schedule.Most) return ToolOutcome.Fail("There is room for " + Schedule.Most + " scheduled tasks, and all are in use.");
                t.Id = Guid.NewGuid().ToString("N");
                tasks.Add(t);
            }
            else
            {
                if (tasks[at].LastRun > t.LastRun && t.LastRun != DateTime.MinValue) t.LastRun = tasks[at].LastRun;
                tasks[at] = t;
            }
            if (!Schedule.Save(tasks)) return ToolOutcome.Fail("MinGent could not save the scheduled tasks.");
            if (act == "remove") return ToolOutcome.Ok("Removed the scheduled task \"" + was.Name + "\".", "Removed");
            return ToolOutcome.Ok((was == null ? "Scheduled. " : "Changed. ") + Line(t, now).Substring(2).TrimEnd() + "\nIt runs only while MinGent is open, and waits until the user is not in the middle of something. The user can change it in Settings > Scheduled.", was == null ? "Scheduled" : "Changed");
        }
    }

    class SystemInfoTool : AgentTool
    {
        public override string Name { get { return "system_info"; } }
        public override string Description { get { return "Describe this PC without running anything: Windows version, CPU, memory, graphics cards, disks, screens, the shell, and which developer tools are installed (found on PATH, with their locations). Use it to learn the user's setup, then save the useful facts with the memory tool."; } }
        public override Dictionary<string, object> Schema { get { return Obj(); } }
        public override string Label(Dictionary<string, object> a) { return "Look at this PC's setup"; }

        static readonly string[] Tools = { "python", "py", "pip", "node", "npm", "npx", "pnpm", "yarn", "bun", "deno", "git", "gh", "dotnet", "java", "javac", "mvn", "gradle", "go", "cargo", "rustc", "gcc", "g++", "clang", "cl", "cmake", "make", "ninja", "msbuild", "docker", "wsl", "code", "cursor", "pwsh", "winget", "choco", "scoop", "conda", "uv", "ffmpeg", "php", "ruby", "perl", "lua", "lms", "ollama", "nvidia-smi" };

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                Version v = Environment.OSVersion.Version;
                string display = "";
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        if (k != null)
                        {
                            string product = k.GetValue("ProductName") as string ?? "";
                            if (v.Build >= 22000) product = product.Replace("Windows 10", "Windows 11");
                            display = product + " " + (k.GetValue("DisplayVersion") as string ?? "") + " (build " + v.Build + ")";
                        }
                }
                catch { }
                sb.Append("Windows: ").Append(display.Length > 0 ? display : Environment.OSVersion.VersionString).Append(Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit").Append('\n');
                sb.Append("User: ").Append(Environment.UserName).Append(", language ").Append(CultureInfo.CurrentUICulture.Name).Append(", time zone ").Append(TimeZoneInfo.Local.Id).Append('\n');
            }
            catch { }
            try
            {
                string cpu = "";
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    if (k != null) cpu = (k.GetValue("ProcessorNameString") as string ?? "").Trim();
                sb.Append("CPU: ").Append(cpu.Length > 0 ? cpu : "unknown").Append(", ").Append(Environment.ProcessorCount).Append(" logical cores\n");
            }
            catch { }
            long ram = Hardware.RamBytes;
            if (ram > 0) sb.Append("Memory: ").Append(ModelCatalog.Size(ram)).Append('\n');
            try
            {
                foreach (GpuInfo g in Hardware.Gpus())
                    sb.Append("Graphics: ").Append(g.Name).Append(g.VramBytes > 0 ? ", " + ModelCatalog.Size(g.VramBytes) + " VRAM" : "").Append(g.DriverVersion.Length > 0 ? ", driver " + g.DriverVersion : "").Append('\n');
            }
            catch { }
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    sb.Append("Disk ").Append(d.Name).Append(' ').Append(ModelCatalog.Size(d.AvailableFreeSpace)).Append(" free of ").Append(ModelCatalog.Size(d.TotalSize)).Append('\n');
                }
            }
            catch { }
            try
            {
                foreach (Screen s in Screen.AllScreens)
                    sb.Append("Screen: ").Append(s.Bounds.Width).Append('x').Append(s.Bounds.Height).Append(s.Primary ? " (primary)" : "").Append('\n');
            }
            catch { }
            sb.Append("Shell for commands: ").Append(Shell.PowerShellName).Append(" (").Append(Shell.PowerShellExe).Append(")\n");
            sb.Append("Home: ").Append(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Append('\n');
            sb.Append("Workspace: ").Append(ctx.Workspace).Append('\n');
            sb.Append("Developer tools on PATH:\n");
            Dictionary<string, string> found = OnPath();
            int n = 0;
            foreach (string t in Tools)
            {
                string p;
                if (!found.TryGetValue(t, out p)) continue;
                if (StoreShortcut(p)) p += " (looks like only the Microsoft Store shortcut, which opens the Store instead of running Python)";
                sb.Append("  ").Append(t).Append(": ").Append(p).Append('\n');
                n++;
            }
            if (n == 0) sb.Append("  (none of the common ones)\n");
            List<string> missing = new List<string>();
            foreach (string t in Key) if (!found.ContainsKey(t)) missing.Add(t);
            if (missing.Count > 0) sb.Append("Not found on PATH: ").Append(string.Join(", ", missing.ToArray())).Append('\n');
            return ToolOutcome.Ok(sb.ToString(), n + " tools found");
        }

        static readonly string[] Key = { "python", "node", "git", "dotnet", "java", "go", "cargo" };

        static readonly object scanLock = new object();
        static string scannedPath;
        static DateTime scannedAt;
        static Dictionary<string, string> scanned;

        public static Dictionary<string, string> OnPath()
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            lock (scanLock)
                if (scanned != null && scannedPath == path && (DateTime.UtcNow - scannedAt).TotalMinutes < 2)
                    return new Dictionary<string, string>(scanned, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] exts = { ".exe", ".cmd", ".bat", ".ps1" };
            foreach (string dir in path.Split(';'))
            {
                string d = dir.Trim().Trim('"');
                if (!LocalFolder(d)) continue;
                foreach (string t in Tools)
                {
                    if (found.ContainsKey(t)) continue;
                    foreach (string e in exts)
                    {
                        try
                        {
                            string p = Path.Combine(d, t + e);
                            if (File.Exists(p)) { found[t] = p; break; }
                        }
                        catch { }
                    }
                }
            }
            lock (scanLock) { scanned = found; scannedPath = path; scannedAt = DateTime.UtcNow; }
            return new Dictionary<string, string>(found, StringComparer.OrdinalIgnoreCase);
        }

        static bool LocalFolder(string d)
        {
            if (d.Length == 0) return false;
            bool drive = d.Length >= 3 && char.IsLetter(d[0]) && d[1] == ':' && (d[2] == '\\' || d[2] == '/');
            if (!drive && !(Path.DirectorySeparatorChar == '/' && d[0] == '/')) return false;
            try
            {
                if (drive && new DriveInfo(d.Substring(0, 1)).DriveType == DriveType.Network) return false;
                return Directory.Exists(d);
            }
            catch { return false; }
        }

        public static bool StoreShortcut(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Replace('/', '\\').IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) < 0) return false;
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (name != "python" && name != "python3") return false;
            try
            {
                string dir = Path.GetDirectoryName(path);
                foreach (string d in Directory.GetDirectories(dir))
                    if (Path.GetFileName(d).StartsWith("PythonSoftwareFoundation.", StringComparison.OrdinalIgnoreCase)) return false;
                foreach (string f in Directory.GetFiles(dir))
                    if (Regex.IsMatch(Path.GetFileName(f), @"^(python3\.\d+|py|pyw|pymanager|pip\d*(\.\d+)?)\.exe$", RegexOptions.IgnoreCase)) return false;
                return true;
            }
            catch { return false; }
        }

        public static bool HasPython()
        {
            Dictionary<string, string> found = OnPath();
            foreach (string t in new string[] { "python", "py" })
            {
                string p;
                if (found.TryGetValue(t, out p) && !StoreShortcut(p)) return true;
            }
            return false;
        }

        public static string PathLine()
        {
            Dictionary<string, string> found = OnPath();
            List<string> have = new List<string>(), missing = new List<string>();
            foreach (string t in Tools)
            {
                string p;
                if (found.TryGetValue(t, out p) && !StoreShortcut(p)) have.Add(t);
            }
            foreach (string t in Key)
            {
                string p;
                if (!found.TryGetValue(t, out p)) missing.Add(t);
                else if (StoreShortcut(p)) missing.Add(t + " (only a Microsoft Store shortcut)");
            }
            StringBuilder sb = new StringBuilder("Developer tools on PATH: ");
            sb.Append(have.Count > 0 ? string.Join(", ", have.ToArray()) : "none of the common ones").Append('.');
            if (missing.Count > 0) sb.Append(" Not on PATH: ").Append(string.Join(", ", missing.ToArray())).Append('.');
            return sb.ToString();
        }
    }

    class KeyPress
    {
        public string Key = "", Code = "", Text, Label = "";
        public int Vk, Location;
        public bool Keypad, Extended, Printable;
        public char Char;
        public bool Ctrl, Shift, Alt, Win;
        public List<KeyPress> With;
        public int CdpModifiers { get { return (Alt ? 1 : 0) | (Ctrl ? 2 : 0) | (Win ? 4 : 0) | (Shift ? 8 : 0); } }
    }

    static class KeyCombo
    {
        static readonly Dictionary<string, string[]> Named = new Dictionary<string, string[]>(StringComparer.Ordinal);
        static readonly Dictionary<string, char> Marks = new Dictionary<string, char>(StringComparer.Ordinal);
        const string Shifted = "~!@#$%^&*()_+{}|:\"<>?", Unshifted = "`1234567890-=[]\\;',./";
        static readonly string[] PunctCodes = { "Backquote", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9", "Digit0", "Minus", "Equal", "BracketLeft", "BracketRight", "Backslash", "Semicolon", "Quote", "Comma", "Period", "Slash" };
        static readonly int[] PunctVks = { 192, 49, 50, 51, 52, 53, 54, 55, 56, 57, 48, 189, 187, 219, 221, 220, 186, 222, 188, 190, 191 };

        static void N(string key, string code, int vk, string text, bool ext, params string[] names)
        {
            string[] v = { key, code, vk.ToString(CultureInfo.InvariantCulture), text ?? "", ext ? "1" : "" };
            Named[Norm(key)] = v;
            foreach (string n in names) Named[Norm(n)] = v;
        }

        static string Norm(string s) { return Regex.Replace((s ?? "").ToLowerInvariant(), @"[\s_\-]", ""); }

        static void M(char c, params string[] names) { foreach (string n in names) Marks[Norm(n)] = c; }

        static KeyCombo()
        {
            M('-', "minus", "hyphen", "dash", "subtract", "oemminus");
            M('=', "equal", "equals", "equalsign", "oemplus");
            M('+', "plus", "plussign");
            M(',', "comma", "oemcomma");
            M('.', "period", "dot", "fullstop", "oemperiod");
            M('/', "slash", "forwardslash", "oem2");
            M('\\', "backslash", "oem5", "intlbackslash");
            M(';', "semicolon", "oem1");
            M(':', "colon");
            M('\'', "quote", "apostrophe", "singlequote", "oem7");
            M('"', "doublequote", "quotationmark");
            M('`', "backquote", "backtick", "grave", "graveaccent", "oem3");
            M('~', "tilde");
            M('[', "bracketleft", "leftbracket", "openbracket", "lbracket", "oem4");
            M(']', "bracketright", "rightbracket", "closebracket", "rbracket", "oem6");
            M('{', "braceleft", "leftbrace", "openbrace", "lbrace", "curlyleft");
            M('}', "braceright", "rightbrace", "closebrace", "rbrace", "curlyright");
            M('(', "parenleft", "leftparen", "openparen", "lparen", "leftparenthesis");
            M(')', "parenright", "rightparen", "closeparen", "rparen", "rightparenthesis");
            M('<', "lessthan", "less", "lt");
            M('>', "greaterthan", "greater", "gt");
            M('?', "question", "questionmark");
            M('!', "exclamation", "exclamationmark", "exclaim", "bang");
            M('@', "at", "atsign");
            M('#', "hash", "pound", "numbersign", "sharp");
            M('$', "dollar", "dollarsign");
            M('%', "percent");
            M('^', "caret", "circumflex");
            M('&', "ampersand");
            M('*', "asterisk", "star");
            M('_', "underscore");
            M('|', "pipe", "verticalbar");
            N("Enter", "Enter", 13, "\r", false, "return", "ret", "cr", "newline");
            N("Tab", "Tab", 9, null, false);
            N("Escape", "Escape", 27, null, false, "esc");
            N("Backspace", "Backspace", 8, null, false, "bksp", "back", "bs");
            N("Delete", "Delete", 46, null, true, "del");
            N("Insert", "Insert", 45, null, true, "ins");
            N("Space", "Space", 32, " ", false, "spacebar", "spc");
            N("ArrowUp", "ArrowUp", 38, null, true, "up", "uparrow");
            N("ArrowDown", "ArrowDown", 40, null, true, "down", "downarrow");
            N("ArrowLeft", "ArrowLeft", 37, null, true, "left", "leftarrow");
            N("ArrowRight", "ArrowRight", 39, null, true, "right", "rightarrow");
            N("PageUp", "PageUp", 33, null, true, "pgup", "prior");
            N("PageDown", "PageDown", 34, null, true, "pgdn", "pgdown", "next");
            N("Home", "Home", 36, null, true);
            N("End", "End", 35, null, true);
            N("CapsLock", "CapsLock", 20, null, false, "caps");
            N("NumLock", "NumLock", 144, null, true);
            N("ScrollLock", "ScrollLock", 145, null, false);
            N("Pause", "Pause", 19, null, false, "break");
            N("ContextMenu", "ContextMenu", 93, null, true, "apps", "menu", "application");
            N("PrintScreen", "PrintScreen", 44, null, true, "prtsc", "prtscn", "printscrn", "snapshot");
            N("Shift", "ShiftLeft", 16, null, false, "lshift");
            N("Control", "ControlLeft", 17, null, false, "ctrl", "lctrl", "ctl");
            N("Alt", "AltLeft", 18, null, false, "lalt", "option");
            N("Meta", "MetaLeft", 91, null, true, "win", "windows", "lwin", "cmd", "command", "super");
            for (int i = 1; i <= 24; i++) N("F" + i, "F" + i, 111 + i, null, false);
            for (int i = 0; i <= 9; i++) N("Numpad" + i, "Numpad" + i, 96 + i, i.ToString(CultureInfo.InvariantCulture), false, "num" + i, "kp" + i);
            N("NumpadAdd", "NumpadAdd", 107, "+", false, "numpadplus", "numplus", "kpadd", "kpplus");
            N("NumpadSubtract", "NumpadSubtract", 109, "-", false, "numpadminus", "numminus", "kpsubtract", "kpminus");
            N("NumpadMultiply", "NumpadMultiply", 106, "*", false, "nummultiply", "numstar", "kpmultiply");
            N("NumpadDivide", "NumpadDivide", 111, "/", true, "numdivide", "numslash", "kpdivide");
            N("NumpadDecimal", "NumpadDecimal", 110, ".", false, "numdecimal", "numpadperiod", "numdot", "kpdecimal");
            N("NumpadEnter", "NumpadEnter", 13, "\r", true, "numenter", "kpenter");
        }

        static bool Modifier(string name, KeyPress k)
        {
            switch (Norm(name))
            {
                case "ctrl": case "control": case "ctl": case "strg": case "lctrl": case "rctrl": k.Ctrl = true; return true;
                case "shift": case "lshift": case "rshift": k.Shift = true; return true;
                case "alt": case "option": case "opt": case "lalt": case "ralt": k.Alt = true; return true;
                case "altgr": k.Ctrl = true; k.Alt = true; return true;
                case "meta": case "win": case "windows": case "cmd": case "command": case "super": case "os": case "lwin": case "rwin": k.Win = true; return true;
            }
            return false;
        }

        static readonly Regex Media = new Regex(@"^(media|volume|audio|mute|play|pause?play|nexttrack|prevtrack|previoustrack|stop|browser)", RegexOptions.CultureInvariant);

        static bool IsModifier(string name) { return Modifier(name, new KeyPress()); }

        public static KeyPress Parse(string combo, out string error) { return Parse(combo, false, out error); }

        public static KeyPress Parse(string combo, bool hold, out string error)
        {
            error = null;
            string c = (combo ?? "").Trim();
            if (c.Length == 0 && combo != null && combo.Length > 0) c = " ";
            if (c.Length == 0) { error = "no key given"; return null; }
            List<string> parts = new List<string>();
            bool lone = c.Length == 1;
            if (lone) parts.Add(c);
            else
            {
                string body = c;
                bool plusKey = body.EndsWith("++");
                if (plusKey) body = body.Substring(0, body.Length - 2);
                else if (body.EndsWith("+")) { error = "\"" + combo + "\" has no key after the +"; return null; }
                foreach (string p in body.Split('+')) if (p.Trim().Length > 0) parts.Add(p.Trim());
                if (plusKey) parts.Add("+");
                if (parts.Count == 0) { error = "no key given"; return null; }
            }
            List<string> keys = new List<string>();
            foreach (string p in parts) if (!IsModifier(p)) keys.Add(p);
            KeyPress k = new KeyPress();
            string main;
            if (keys.Count == 0)
            {
                main = parts[parts.Count - 1];
                for (int i = 0; i < parts.Count - 1; i++) Modifier(parts[i], k);
            }
            else
            {
                main = keys[keys.Count - 1];
                foreach (string p in parts) Modifier(p, k);
                if (keys.Count > 4) { error = "\"" + combo + "\" has too many keys at once (at most 4 besides Ctrl, Shift, Alt and Win)"; return null; }
                for (int i = 0; i < keys.Count - 1; i++)
                {
                    KeyPress w = new KeyPress();
                    string werr;
                    if (!Fill(keys[i], false, w, out werr))
                    {
                        error = Named.ContainsKey(Norm(keys[i])) || keys[i].Length == 1 ? werr : "\"" + keys[i] + "\" is not a key or a modifier (modifiers are Ctrl, Shift, Alt and Win)";
                        return null;
                    }
                    w.Label = Shown(w);
                    if (k.With == null) k.With = new List<KeyPress>();
                    k.With.Add(w);
                }
            }
            if (!Fill(main, lone && !hold, k, out error))
            {
                if (keys.Count > 1 && !(Named.ContainsKey(Norm(main)) || main.Length == 1)) error = "\"" + main + "\" is not a key or a modifier (modifiers are Ctrl, Shift, Alt and Win)";
                return null;
            }
            List<string> label = new List<string>();
            if (k.Ctrl) label.Add("Ctrl");
            if (k.Alt) label.Add("Alt");
            if (k.Shift && !(k.Printable && k.Char != 0 && lone && !hold)) label.Add("Shift");
            if (k.Win) label.Add("Win");
            if (k.With != null) foreach (KeyPress w in k.With) label.Add(w.Label);
            label.Add(k.Key == " " ? "Space" : k.Key.Length == 1 && char.IsLetter(k.Key[0]) && (k.Ctrl || k.Alt || k.Win || k.With != null || hold) ? k.Key.ToUpperInvariant() : k.Key);
            k.Label = string.Join("+", label.ToArray());
            return k;
        }

        static string Shown(KeyPress w)
        {
            return w.Key == " " ? "Space" : w.Key.Length == 1 && char.IsLetter(w.Key[0]) ? w.Key.ToUpperInvariant() : w.Key;
        }

        static bool Fill(string main, bool lone, KeyPress k, out string error)
        {
            error = null;
            string nm = Norm(main);
            string[] v;
            char mark;
            if (main.Length > 1 && !Named.ContainsKey(nm))
            {
                Match code = Regex.Match(nm, @"^(?:key([a-z])|digit([0-9]))$");
                if (code.Success) { main = code.Groups[1].Success ? code.Groups[1].Value : code.Groups[2].Value; nm = main; }
                else if (Marks.TryGetValue(nm, out mark)) { main = mark.ToString(); nm = main; }
            }
            if (main.Length > 1 && Named.TryGetValue(nm, out v))
            {
                k.Key = v[0]; k.Code = v[1]; k.Vk = int.Parse(v[2], CultureInfo.InvariantCulture); k.Text = v[3].Length > 0 ? v[3] : null; k.Extended = v[4].Length > 0;
                k.Keypad = k.Code.StartsWith("Numpad", StringComparison.Ordinal);
                if (k.Keypad) k.Location = 3;
                if (k.Code == "Enter" || k.Code == "Space") k.Printable = true;
                return true;
            }
            if (main.Length == 1 || (main.Length == 2 && char.IsSurrogatePair(main, 0)))
            {
                char ch = main[0];
                k.Printable = true;
                k.Char = ch;
                if (ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z')
                {
                    char up = char.ToUpperInvariant(ch);
                    if (ch == up && main.Length == 1 && !k.Ctrl && !k.Alt && !k.Win && lone) k.Shift = true;
                    k.Code = "Key" + up;
                    k.Vk = up;
                    k.Key = k.Shift ? up.ToString() : char.ToLowerInvariant(ch).ToString();
                }
                else if (ch >= '0' && ch <= '9') { k.Code = "Digit" + ch; k.Vk = ch; k.Key = ch.ToString(); }
                else if (ch == ' ') { k.Code = "Space"; k.Vk = 32; k.Key = " "; }
                else
                {
                    int i = Unshifted.IndexOf(ch), j = Shifted.IndexOf(ch);
                    if (i >= 0) { k.Code = PunctCodes[i]; k.Vk = PunctVks[i]; }
                    else if (j >= 0) { k.Code = PunctCodes[j]; k.Vk = PunctVks[j]; }
                    k.Key = main;
                }
                if (k.Shift && k.Key.Length == 1)
                {
                    int u = Unshifted.IndexOf(k.Key[0]);
                    if (u >= 0) k.Key = Shifted[u].ToString();
                }
                k.Text = k.Key;
                return true;
            }
            if (Media.IsMatch(nm)) { error = "media and volume keys are not supported"; return false; }
            error = NotAKey + main + "\"" + KeyNames + "Enter, Tab, Escape, Backspace, Delete, Insert, Space, ArrowUp, PageDown, Home, End, F1-F24, Numpad0-9, ContextMenu, Minus, Equal, Comma, Period or Slash, or a single character, with Ctrl+, Shift+, Alt+ in front for shortcuts";
            return false;
        }

        const string TooMany = "press at most 40 keys at a time", NotAKey = "unknown key \"", KeyNames = ". Use a key name such as ";

        public static string TypeInstead(string error, string spec, string typeTool, bool games)
        {
            if (error == null) return null;
            string how = "To type words or a command, use " + typeTool + (games ? ": it types the whole text at once, also into a game's chat or command box once that is open" : ": it types the whole text at once");
            if (error == TooMany)
            {
                int single = 0, all = 0;
                foreach (string t in Regex.Split((spec ?? "").Trim(), @"\s+"))
                {
                    if (t.Length == 0) continue;
                    all++;
                    if (t.Length == 1 || t.Equals("space", StringComparison.OrdinalIgnoreCase)) single++;
                }
                return single * 2 >= all ? error + ". " + how + "." : error;
            }
            int at = error.IndexOf(KeyNames, StringComparison.Ordinal);
            if (!error.StartsWith(NotAKey, StringComparison.Ordinal) || at < 0 || (spec ?? "").IndexOf('+') >= 0) return error;
            return error.Substring(0, at) + ". " + how + ". For a key, use a key name such as " + error.Substring(at + KeyNames.Length);
        }

        public static List<KeyPress> Sequence(string spec, int repeat, out string error) { return Sequence(spec, repeat, false, out error); }

        public static List<KeyPress> Sequence(string spec, int repeat, bool hold, out string error)
        {
            error = null;
            List<KeyPress> res = new List<KeyPress>();
            string s = spec ?? "";
            if (s.Length > 0 && s.Trim().Length == 0) s = "Space";
            s = Regex.Replace(s, @"\b(page|arrow|caps|num|scroll|print|context|space)\s+(up|down|left|right|lock|screen|menu|bar)\b", "$1$2", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"(?<=\S)[ \t]*\+[ \t]*(?=\S)", "+");
            List<string> tokens = new List<string>();
            foreach (string t in Regex.Split(s.Trim(), @"\s+"))
            {
                string x = t;
                if (x.Length > 1 && x.EndsWith(",") && x[x.Length - 2] != '+') x = x.Substring(0, x.Length - 1);
                if (x.Length > 0) tokens.Add(x);
            }
            if (tokens.Count == 0) { error = "no key given"; return null; }
            if (tokens.Count > 40) { error = TooMany; return null; }
            foreach (string t in tokens)
            {
                KeyPress k = Parse(t, hold, out error);
                if (k == null) return null;
                res.Add(k);
            }
            int times = Math.Max(1, Math.Min(100, repeat));
            if (times > 1)
            {
                List<KeyPress> all = new List<KeyPress>();
                for (int i = 0; i < times; i++) all.AddRange(res);
                if (all.Count > 200) { error = "that is too many key presses at once (at most 200)"; return null; }
                res = all;
            }
            return res;
        }

        public static string Describe(List<KeyPress> keys, int holdMs)
        {
            if (holdMs <= 0) return "Pressed " + Describe(keys);
            string secs = holdMs % 1000 == 0 ? (holdMs / 1000).ToString(CultureInfo.InvariantCulture) : (holdMs / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
            return "Held " + Describe(keys) + " down for " + secs + (holdMs == 1000 ? " second" : " seconds") + (keys.Count > 1 ? " each" : "");
        }

        public const int MaxHoldMs = 10000;

        public static List<KeyPress> Parts(KeyPress k)
        {
            List<KeyPress> l = new List<KeyPress>();
            if (k.With != null)
                foreach (KeyPress w in k.With)
                {
                    KeyPress c = new KeyPress();
                    c.Key = w.Key; c.Code = w.Code; c.Vk = w.Vk; c.Text = w.Text; c.Char = w.Char; c.Printable = w.Printable; c.Extended = w.Extended; c.Keypad = w.Keypad;
                    c.Ctrl = k.Ctrl; c.Shift = k.Shift; c.Alt = k.Alt; c.Win = k.Win;
                    c.Label = k.Label;
                    l.Add(c);
                }
            l.Add(k);
            return l;
        }

        public static string HoldError(List<KeyPress> keys, int holdMs)
        {
            if (holdMs < 0) return "hold_ms can't be negative";
            if (holdMs > MaxHoldMs) return "hold keys for at most " + MaxHoldMs + " ms (10 seconds) at a time; to hold longer, call the tool again";
            if (holdMs > 0 && (long)holdMs * keys.Count > 20000) return "that holds keys for more than 20 seconds in one call; hold fewer keys or for a shorter time";
            return null;
        }

        public static string Describe(List<KeyPress> keys)
        {
            if (keys == null || keys.Count == 0) return "";
            List<string> l = new List<string>();
            int i = 0;
            while (i < keys.Count)
            {
                int j = i;
                while (j + 1 < keys.Count && keys[j + 1].Label == keys[i].Label) j++;
                l.Add(keys[i].Label + (j > i ? " x" + (j - i + 1) : ""));
                i = j + 1;
            }
            return string.Join(", ", l.ToArray());
        }
    }

    class AppWindow
    {
        public IntPtr Hwnd;
        public int Pid;
        public string Title = "", Process = "", Class = "";
        public Rectangle Rect;
        public bool Adopted, Terminal, Granted, Veiled, Barred;
        public string Tier = AppTier.Full, Behind = "";
        public int Id;

        public string Ref(List<AppWindow> all) { return "w" + (Id > 0 ? Id : all.IndexOf(this) + 1).ToString(CultureInfo.InvariantCulture); }
    }

    static class AppTier
    {
        public const string Full = "full", Look = "look", Never = "never";
    }

    class AppGrant
    {
        public string Exe = "", Name = "", Tier = AppTier.Full;
        public bool SawWeb;
    }

    class OtherApp
    {
        public string Exe = "", Process = "", Name = "", Tier = AppTier.Full;
        public int Windows;
        public bool Admin;
    }

    class AppElement
    {
        public IAccessible Acc;
        public object Child;
        public int Role, State;
        public string Name = "", Value = "";
        public Rectangle Rect;
    }

    static class AppControl
    {
        delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll", EntryPoint = "SetCursorPos")] static extern bool SetCursorPosRaw(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)] static extern uint SendInputRaw(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll", EntryPoint = "keybd_event")] static extern void KeybdEventRaw(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object acc);
        [DllImport("oleacc.dll")] static extern int AccessibleChildren(IAccessible container, int start, int count, [Out] object[] children, out int obtained);
        [DllImport("user32.dll")] static extern short VkKeyScanEx(char ch, IntPtr hkl);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(int thread);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, IntPtr pid);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        static int ownInputFrom, ownInputTo, answeredAt, gaveUpAt;
        static bool ownInputSent, answered, gaveUp;
        const int UserQuietMs = 1000, UserWaitMaxMs = 4000;

        static void OwnInputStarts()
        {
            int now = Environment.TickCount;
            if (!ownInputSent || unchecked(now - ownInputTo) > UserQuietMs) ownInputFrom = now;
            ownInputTo = now;
            ownInputSent = true;
        }

        static void OwnInputEnds() { ownInputTo = Environment.TickCount; }

        static uint SendInput(uint n, INPUT[] inputs, int size)
        {
            OwnInputStarts();
            uint sent = SendInputRaw(n, inputs, size);
            OwnInputEnds();
            return sent;
        }

        static void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra)
        {
            OwnInputStarts();
            KeybdEventRaw(vk, scan, flags, extra);
            OwnInputEnds();
        }

        static bool SetCursorPos(int x, int y)
        {
            OwnInputStarts();
            bool moved = SetCursorPosRaw(x, y);
            OwnInputEnds();
            return moved;
        }

        public static void UserAnswered()
        {
            answeredAt = Environment.TickCount;
            answered = true;
        }

        static int MsSinceUserInput()
        {
            LASTINPUTINFO li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return int.MaxValue;
            int last = unchecked((int)li.dwTime);
            if (ownInputSent && unchecked(last - ownInputFrom) >= -50 && unchecked(last - ownInputTo) <= 100) return int.MaxValue;
            if (answered && unchecked(last - answeredAt) <= 20) return int.MaxValue;
            return unchecked(Environment.TickCount - last);
        }

        static bool WaitForUser(CancelToken ct)
        {
            int start = Environment.TickCount;
            bool busyLately = gaveUp && unchecked(start - gaveUpAt) < UserWaitMaxMs;
            while (!busyLately && MsSinceUserInput() < UserQuietMs)
            {
                if (ct != null && ct.IsCancelled) return false;
                if (unchecked(Environment.TickCount - start) >= UserWaitMaxMs)
                {
                    gaveUpAt = Environment.TickCount;
                    gaveUp = true;
                    break;
                }
                Thread.Sleep(50);
            }
            return ct == null || !ct.IsCancelled;
        }

        const int RoleButton = 0x2B, RoleCheck = 0x2C, RoleRadio = 0x2D, RoleText = 0x2A, RoleStatic = 0x29, RoleLink = 0x1E, RoleMenuItem = 0x0C,
                  RoleCombo = 0x2E, RoleList = 0x21, RoleListItem = 0x22, RoleTab = 0x25, RoleOutlineItem = 0x24, RoleSlider = 0x33, RoleSpin = 0x34,
                  RoleCell = 0x1D, RoleDropButton = 0x38, RoleSplitButton = 0x3E, RoleMenuBar = 0x02, RoleTitleBar = 0x01, RoleScrollBar = 0x03,
                  RoleGraphic = 0x28, RoleHeading = 0x14, RoleDocument = 0x0F, RoleColumnHeader = 0x19, RoleRowHeader = 0x1A, RolePageTabList = 0x3C,
                  RoleMenuPopup = 0x0B, RoleToolbar = 0x16, RoleStatusBar = 0x17, RoleOutline = 0x23, RoleTable = 0x18;
        const int StateUnavailable = 0x1, StateSelected = 0x2, StateFocused = 0x4, StatePressed = 0x8, StateChecked = 0x10, StateInvisible = 0x8000, StateProtected = 0x20000000, StateExpanded = 0x200;

        static readonly HashSet<string> Blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "cmd", "powershell", "pwsh", "conhost", "openconsole", "windowsterminal", "wt", "bash", "wsl", "wslhost", "mintty", "regedit", "mmc",
            "taskmgr", "explorer", "control", "rundll32", "msiexec", "schtasks", "certmgr", "gpedit", "secpol", "netsh", "sc", "diskpart", "putty", "mingent", "justpad",
            "systemsettings", "useraccountcontrolsettings", "consent", "credentialuibroker", "lockapp", "logonui", "securityhealthsystray", "windowsdefender", "msmpeng" };

        static readonly HashSet<string> Browsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "iexplore", "arc", "librewolf", "waterfox", "chromium", "yandex", "thorium",
            "browser", "floorp", "zen", "palemoon", "basilisk", "seamonkey", "maxthon", "whale", "epic", "duckduckgo", "sidekick", "comet", "ladybird", "mullvadbrowser",
            "iron", "slimjet", "chrome_proxy", "avastbrowser", "avgbrowser", "ccleanerbrowser", "dragon", "cent", "min", "ungoogled-chromium", "supermium", "mercury" };

        static readonly HashSet<string> Messaging = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "outlook", "olk", "hxoutlook", "thunderbird", "mailbird", "em client", "mailclient", "teams", "ms-teams", "slack", "discord", "telegram", "whatsapp", "signal", "skype", "zoom", "messenger",
            "claude", "chatgpt", "copilot", "perplexity", "lm studio", "jan", "msty", "anythingllm", "chatbox", "gpt4all", "ollama app",
            "element", "viber", "line", "kakaotalk", "wechat", "weixin", "qq", "threema", "wire", "session", "mattermost", "rocket.chat", "zulip", "lync", "jitsi meet",
            "beeper", "ferdium", "franz", "rambox", "discordptb", "discordcanary", "whatsapp.root", "telegramdesktop", "skypeapp" };

        static readonly HashSet<string> Vault = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "msrdc", "rdcman", "mremoteng", "vmconnect", "virtualboxvm", "virtualbox", "vmware", "vmplayer", "royalts", "remotedesktopmanager", "remotedesktopmanager64",
            "termius", "mobaxterm", "securecrt", "xshell", "winscp", "filezilla", "nxplayer", "moonlight", "logmein", "ultraviewer", "remotepc", "aa_v3", "ammyy_admin",
            "dwagent", "screenconnect.windowsclient", "connectwisecontrol.client", "bomgar-scc", "g2mui", "zohoassist",
            "keeweb", "pwsafe", "authy", "authy desktop", "yubioath-desktop", "yubico authenticator", "winauth", "enteauth", "atomic wallet", "wasabi wallet", "sparrow",
            "bitcoin-qt", "monero-wallet-gui", "electrum-ltc",
            "keepass", "keepassxc", "1password", "bitwarden", "lastpass", "dashlane", "enpass", "roboform", "nordpass", "proton pass", "keeper",
            "mstsc", "teamviewer", "teamviewer_service", "anydesk", "rustdesk", "parsec", "vncviewer", "tvnviewer", "remoting_host", "quickassist", "msra", "splashtop", "supremo",
            "exodus", "electrum", "ledger live", "trezor suite", "metamask", "winstore.app", "phoneexperiencehost" };

        static readonly HashSet<string> Guard = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "consent", "credentialuibroker", "lockapp", "logonui", "securityhealthsystray", "securityhealthhost", "windowsdefender", "msmpeng", "sechealthui",
            "useraccountcontrolsettings", "mingent", "justpad" };

        static readonly HashSet<string> RemoteTerminals = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "putty", "kitty" };

        static readonly HashSet<string> Private = Both(Messaging, Vault);

        static HashSet<string> Both(HashSet<string> x, HashSet<string> y)
        {
            HashSet<string> all = new HashSet<string>(x, StringComparer.OrdinalIgnoreCase);
            all.UnionWith(y);
            return all;
        }

        public static volatile bool Unlocked;
        public const string Switch = "\"Restricted programs\" in Settings > Agent > Tools";
        public const string LockedNote = " The user can allow it with the switch " + Switch + ": if they asked you to use this program, tell them about that switch, and don't work around it.";

        public static bool Hard(string process)
        {
            string p = process ?? "";
            return Vault.Contains(p) || Guard.Contains(p) || SystemHosts.Contains(p);
        }

        public static bool Limited(string process, string cls)
        {
            string p = process ?? "", c = cls ?? "";
            return Blocked.Contains(p) || SystemTools.Contains(p) || Browsers.Contains(p) || Messaging.Contains(p) || c == "ConsoleWindowClass" || c == "CASCADIA_HOSTING_WINDOW_CLASS" || c == "PseudoConsoleWindow";
        }

        static readonly HashSet<string> Terminals = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "cmd", "powershell", "pwsh", "powershell_ise", "conhost", "openconsole", "windowsterminal", "wt", "bash", "wsl", "wslhost", "mintty", "putty", "kitty",
            "alacritty", "wezterm-gui", "hyper", "tabby", "conemu", "conemu64", "cmder", "fluentterminal" };

        static readonly HashSet<string> Editors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "code", "code - insiders", "codium", "cursor", "windsurf", "trae", "kiro", "zed", "devenv", "idea64", "idea", "pycharm64", "pycharm", "webstorm64", "rider64",
            "clion64", "goland64", "phpstorm64", "rubymine64", "datagrip64", "rustrover64", "studio64", "fleet", "eclipse", "netbeans64", "qtcreator",
            "notepad++", "sublime_text", "sublime_merge", "atom", "geany", "kate", "gvim", "vim", "emacs", "runemacs", "neovide", "nvim-qt", "lapce", "rstudio", "spyder",
            "thonny", "arduino ide", "processing", "codeblocks", "devcpp", "lazarus", "bds", "unity", "unrealeditor", "godot", "blender", "positron", "void", "github desktop",
            "githubdesktop", "sourcetree", "gitkraken", "fork", "tortoisegitproc", "smartgit" };

        static readonly HashSet<string> SystemTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "regedit", "mmc", "taskmgr", "control", "rundll32", "msiexec", "schtasks", "certmgr", "gpedit", "secpol", "netsh", "sc", "diskpart", "systemsettings",
            "useraccountcontrolsettings", "consent", "credentialuibroker", "lockapp", "logonui", "securityhealthsystray", "securityhealthhost", "windowsdefender", "msmpeng",
            "winstore.app", "mingent", "justpad", "perfmon", "resmon", "eventvwr", "compmgmt", "devmgmt", "services", "msconfig", "systempropertiesadvanced",
            "sechealthui", "regedt32", "procexp", "procexp64", "procmon", "procmon64", "autoruns", "autoruns64", "rammap", "rammap64", "tcpview", "tcpview64",
            "systeminformer", "processhacker", "wf", "gpmc", "lusrmgr", "certlm", "fsquirt", "optionalfeatures", "dfrgui", "cleanmgr", "recoverydrive", "rstrui" };

        public static string TierFor(string process, string cls)
        {
            string p = process ?? "";
            if (Hard(p)) return AppTier.Never;
            if (Messaging.Contains(p) || SystemTools.Contains(p)) return Unlocked ? AppTier.Full : AppTier.Never;
            if (RemoteTerminals.Contains(p)) return AppTier.Look;
            if (Browsers.Contains(p) || Terminals.Contains(p) || Editors.Contains(p) || string.Equals(p, "explorer", StringComparison.OrdinalIgnoreCase) || TerminalClass(cls ?? "") || cls == "PseudoConsoleWindow") return Unlocked ? AppTier.Full : AppTier.Look;
            return AppTier.Full;
        }

        public static string NeverWhy(string process)
        {
            string p = process ?? "";
            if (string.Equals(p, "mingent", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "justpad", StringComparison.OrdinalIgnoreCase)) return "it is MinGent itself";
            if (Vault.Contains(p)) return "it handles your passwords, money or remote access";
            return "it is part of Windows' sign-in and security";
        }

        public static string Refusal(string name, string process)
        {
            string p = process ?? "";
            if (Hard(p)) return "MinGent never lets the agent use " + name + ", because " + NeverWhy(p) + ".";
            return name + " is " + Kind(p, "") + ", which MinGent keeps off limits." + LockedNote;
        }

        public static string Kind(string process, string cls)
        {
            string p = process ?? "";
            if (Browsers.Contains(p)) return "a web browser";
            if (string.Equals(p, "explorer", StringComparison.OrdinalIgnoreCase)) return "File Explorer";
            if (Editors.Contains(p)) return "a code editor";
            if (Terminals.Contains(p) || TerminalClass(cls ?? "") || cls == "PseudoConsoleWindow" || cls == "ConsoleWindowClass") return "a terminal";
            if (Messaging.Contains(p)) return "a mail or chat program";
            if (SystemTools.Contains(p)) return "one of Windows' own tools";
            return "a program";
        }

        public static string Risk(string kind)
        {
            return kind == "a web browser" ? "This is your own browser, with your sign-ins. The agent will see its windows and can click and type in them, so it can act on any site you are signed in to." :
                   kind == "File Explorer" ? "The agent will see File Explorer's windows and can click and type in them, so it can move, rename or delete files and start programs there without asking for each one." :
                   kind == "a terminal" ? "The agent will see this terminal and can type in it. Commands typed there run without MinGent checking them first." :
                   kind == "a code editor" ? "The agent will see this editor's windows and can click and type in them, including a terminal built into it." :
                   kind == "a mail or chat program" ? "This program holds your messages. The agent will see them and can click and type in it, so it can send messages as you." :
                   kind == "one of Windows' own tools" ? "This is one of Windows' own tools. The agent will see its windows and can click and type in them, so it can change how your PC works." : null;
        }

        [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)] static extern IntPtr OpenProcessErr(uint access, bool inherit, int pid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int ret);

        static readonly Dictionary<int, KeyValuePair<bool, DateTime>> adminSeen = new Dictionary<int, KeyValuePair<bool, DateTime>>();

        public static bool Admin(int pid)
        {
            if (Program.Elevated || pid == 0 || pid == OwnPid || Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
            DateTime now = DateTime.UtcNow;
            lock (adminSeen)
            {
                KeyValuePair<bool, DateTime> known;
                if (adminSeen.TryGetValue(pid, out known) && (now - known.Value).TotalSeconds < 30) return known.Key;
            }
            bool raised = AdminNow(pid);
            lock (adminSeen)
            {
                if (adminSeen.Count > 400) adminSeen.Clear();
                adminSeen[pid] = new KeyValuePair<bool, DateTime>(raised, now);
            }
            return raised;
        }

        static bool AdminNow(int pid)
        {
            try
            {
                IntPtr hp = OpenProcessErr(0x1000, false, pid);
                if (hp == IntPtr.Zero) return Marshal.GetLastWin32Error() == 5;
                try
                {
                    IntPtr tok;
                    if (!OpenProcessToken(hp, 8, out tok)) return Marshal.GetLastWin32Error() == 5;
                    try
                    {
                        int raised, got;
                        return GetTokenInformation(tok, 20, out raised, 4, out got) && raised != 0;
                    }
                    finally { CloseHandle(tok); }
                }
                finally { CloseHandle(hp); }
            }
            catch { return false; }
        }

        public static Dictionary<IntPtr, string> AdminWindows()
        {
            Dictionary<IntPtr, string> found = new Dictionary<IntPtr, string>();
            if (Program.Elevated || Environment.OSVersion.Platform != PlatformID.Win32NT) return found;
            Dictionary<int, bool> admin = new Dictionary<int, bool>();
            EnumProc cb = delegate (IntPtr h, IntPtr l)
            {
                try
                {
                    if (!IsWindowVisible(h) || Cloaked(h)) return true;
                    RECT r;
                    if (!GetWindowRect(h, out r) || r.Right - r.Left < 40 || r.Bottom - r.Top < 30) return true;
                    int pid;
                    GetWindowThreadProcessId(h, out pid);
                    bool raised;
                    if (!admin.TryGetValue(pid, out raised)) { raised = Admin(pid); admin[pid] = raised; }
                    if (!raised) return true;
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(h, sb, sb.Capacity);
                    string title = Regex.Replace(sb.ToString(), @"[\x00-\x1F\x7F""]", " ").Trim();
                    if (title.Length == 0) return true;
                    found[h] = title.Length > 60 ? title.Substring(0, 60) : title;
                }
                catch { }
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return found;
        }

        public static string AdminNote(string name)
        {
            return "\"" + name + "\" runs with administrator rights, and Windows does not let a program without them read, click or type in its window, so it is out of your reach unless MinGent itself is started as administrator. It stays open for the user. Do the job another way, for example with run_command (to end a program: Stop-Process -Name <name>), or tell the user.";
        }

        public static string AdminMatch(string part)
        {
            string w = (part ?? "").Trim();
            if (w.Length < 3) return "";
            foreach (string title in AdminWindows().Values) if (title.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return " " + AdminNote(title);
            return "";
        }

        public static bool AlwaysLook(string process) { return RemoteTerminals.Contains(process ?? ""); }

        public static IntPtr Foreground()
        {
            try { return GetForegroundWindow(); }
            catch { return IntPtr.Zero; }
        }

        static readonly HashSet<string> Runtimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "python", "pythonw", "py", "pyw", "node", "electron", "java", "javaw", "dotnet", "deno", "bun", "ruby", "rubyw", "php", "php-cgi", "perl", "lua", "luajit",
            "tauri-driver", "flutter", "dart", "godot", "love", "processing", "pwsh-preview" };

        public static string ExePath(int pid)
        {
            IntPtr hp = OpenProcess(0x1000, false, pid);
            if (hp == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(hp, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            catch { return null; }
            finally { CloseHandle(hp); }
        }

        public static bool Trusted(AppWindow w, string workspace)
        {
            if (Runtimes.Contains(w.Process)) return true;
            string exe = ExePath(w.Pid);
            return exe != null && !string.IsNullOrEmpty(workspace) && AgentPaths.IsInside(exe, workspace);
        }

        public static bool RunTimed(Action work, int ms)
        {
            Thread t = new Thread(delegate () { try { work(); } catch { } });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            return t.Join(ms);
        }

        static readonly object adoptGate = new object();
        static readonly Dictionary<int, DateTime> adopted = new Dictionary<int, DateTime>();
        static readonly List<KeyValuePair<DateTime, DateTime>> launches = new List<KeyValuePair<DateTime, DateTime>>();
        static readonly int OwnPid = Process.GetCurrentProcess().Id;

        static readonly HashSet<string> SystemHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "applicationframehost", "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp", "searchui", "textinputhost", "runtimebroker", "sihost",
            "svchost", "taskhostw", "dwm", "ctfmon", "smartscreen", "msedgewebview2", "phoneexperiencehost", "widgets", "widgetservice", "backgroundtaskhost", "dllhost",
            "wwahost", "screenclippinghost", "useroobebroker", "systemsettingsbroker", "gamebar", "gamebarftserver", "crossdeviceresume", "openwith", "pickerhost",
            "filepicker", "credentialenrollmentmanager" };


        static List<KeyValuePair<AppWindow, Rectangle>> Stack(Rectangle screen, List<AppWindow> mine)
        {
            List<KeyValuePair<AppWindow, Rectangle>> order = new List<KeyValuePair<AppWindow, Rectangle>>();
            Dictionary<IntPtr, AppWindow> own = new Dictionary<IntPtr, AppWindow>();
            if (mine != null) foreach (AppWindow w in mine) own[w.Hwnd] = w;
            if (own.Count == 0) return order;
            EnumProc cb = delegate (IntPtr h, IntPtr l)
            {
                try
                {
                    AppWindow w;
                    if (!own.TryGetValue(h, out w) || !IsWindowVisible(h) || IsIconic(h) || Cloaked(h)) return true;
                    RECT r;
                    if (!GetWindowRect(h, out r)) return true;
                    Rectangle rc = Rectangle.Intersect(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), screen);
                    if (rc.Width < 8 || rc.Height < 8) return true;
                    order.Add(new KeyValuePair<AppWindow, Rectangle>(w, rc));
                }
                catch { }
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return order;
        }

        public static List<AppWindow> OnScreen(List<AppWindow> mine)
        {
            List<AppWindow> seen = new List<AppWindow>();
            if (mine == null || mine.Count == 0 || Environment.OSVersion.Platform != PlatformID.Win32NT) return seen;
            foreach (KeyValuePair<AppWindow, Rectangle> e in Stack(System.Windows.Forms.Screen.PrimaryScreen.Bounds, mine)) seen.Add(e.Key);
            return seen;
        }

        public static Bitmap ScreenPicture(List<AppWindow> mine, out List<KeyValuePair<AppWindow, Rectangle>> shown, out Size full)
        {
            shown = new List<KeyValuePair<AppWindow, Rectangle>>();
            full = Size.Empty;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            full = screen.Size;
            List<KeyValuePair<AppWindow, Rectangle>> order = Stack(screen, mine);
            if (order.Count == 0) return null;
            Bitmap canvas = null;
            try
            {
                canvas = new Bitmap(screen.Width, screen.Height, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.FromArgb(24, 24, 24));
                    for (int i = Math.Min(order.Count, 12) - 1; i >= 0; i--)
                    {
                        AppWindow w = order[i].Key;
                        Rectangle rc = order[i].Value;
                        using (Bitmap shot = Capture(w, false))
                        {
                            if (shot == null) continue;
                            g.DrawImage(shot, new Rectangle(rc.Left - screen.Left, rc.Top - screen.Top, rc.Width, rc.Height),
                                new Rectangle(rc.Left - w.Rect.Left, rc.Top - w.Rect.Top, rc.Width, rc.Height), GraphicsUnit.Pixel);
                        }
                        shown.Insert(0, new KeyValuePair<AppWindow, Rectangle>(w, new Rectangle(rc.Left - screen.Left, rc.Top - screen.Top, rc.Width, rc.Height)));
                    }
                }
                if (shown.Count == 0) return null;
                Bitmap done = canvas;
                canvas = null;
                return done;
            }
            catch { return null; }
            finally
            {
                if (canvas != null) canvas.Dispose();
            }
        }

        public static string Place(AppWindow w)
        {
            if (w == null || Environment.OSVersion.Platform != PlatformID.Win32NT) return "";
            try
            {
                if (IsIconic(w.Hwnd)) return "minimized";
                RECT r;
                if (!GetWindowRect(w.Hwnd, out r)) return "";
                Rectangle on = Rectangle.Intersect(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), System.Windows.Forms.Screen.PrimaryScreen.Bounds);
                return on.Width < 8 || on.Height < 8 ? "on another screen" : "";
            }
            catch { return ""; }
        }

        static readonly HashSet<string> PrivateStart = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Microsoft Teams", "Microsoft Teams (work or school)", "Outlook (new)", "Outlook (classic)", "Mail", "Calendar", "Zoom Workplace",
            "Telegram Desktop", "Messenger", "Skype" };
        static readonly HashSet<string> RemoteStart = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Quick Assist", "Remote Desktop Connection", "Remote Desktop", "Phone Link", "Microsoft Store", "Windows Security" };
        static readonly HashSet<string> BrowserStart = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Google Chrome", "Microsoft Edge", "Opera Browser", "Opera GX Browser", "Firefox Private Browsing", "Tor Browser", "Brave Browser", "DuckDuckGo" };

        static List<string> StartNames(string name, string id)
        {
            List<string> names = new List<string>();
            string i = (id ?? "").Trim(), n = (name ?? "").Trim();
            int cut = Math.Max(i.LastIndexOf('\\'), i.LastIndexOf('/'));
            if (cut >= 0 || i.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string file = i.Substring(cut + 1);
                int dot = file.LastIndexOf('.');
                names.Add(dot > 0 ? file.Substring(0, dot) : file);
                if (file.EndsWith(".msc", StringComparison.OrdinalIgnoreCase)) names.Add("mmc");
            }
            else if (i.Length > 0 && i.IndexOfAny(new char[] { '!', '_' }) < 0) names.Add(i);
            if (n.Length > 0) names.Add(n);
            if (PrivateStart.Contains(n)) names.Add("outlook");
            if (RemoteStart.Contains(n)) names.Add(n == "Windows Security" ? "sechealthui" : "mstsc");
            if (BrowserStart.Contains(n)) names.Add("chrome");
            if (i.IndexOf("git-bash", StringComparison.OrdinalIgnoreCase) >= 0) names.Add("mintty");
            if (i.IndexOf("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase) >= 0) names.Add("winstore.app");
            if (i.IndexOf("ControlPanel", StringComparison.OrdinalIgnoreCase) >= 0 || i.IndexOf("RunDialog", StringComparison.OrdinalIgnoreCase) >= 0) names.Add("control");
            if (i.IndexOf("WindowsTerminal", StringComparison.OrdinalIgnoreCase) >= 0) names.Add("windowsterminal");
            if (i.IndexOf("immersivecontrolpanel", StringComparison.OrdinalIgnoreCase) >= 0) names.Add("systemsettings");
            if (i.IndexOf("SecHealthUI", StringComparison.OrdinalIgnoreCase) >= 0) names.Add("sechealthui");
            if (i.IndexOf("Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase) >= 0) names.Add("explorer");
            return names;
        }

        public static string OpenKind(string name, string id, out string process)
        {
            process = null;
            List<string> names = StartNames(name, id);
            foreach (string x in names) if (Hard(x)) { process = x; return "never"; }
            foreach (string x in names)
            {
                process = x;
                if (Messaging.Contains(x)) return "a mail or chat program";
                if (Browsers.Contains(x)) return "a web browser";
                if (RemoteTerminals.Contains(x)) return "never";
                if (Terminals.Contains(x)) return "a terminal";
                if (string.Equals(x, "explorer", StringComparison.OrdinalIgnoreCase)) return "File Explorer";
                if (SystemTools.Contains(x) || Blocked.Contains(x)) return "one of Windows' own tools";
            }
            process = null;
            return null;
        }

        public static string OpenBlock(string name, string id)
        {
            string x, n = (name ?? "").Trim();
            string kind = OpenKind(name, id, out x);
            if (kind == null) return null;
            if (kind == "never") return RemoteTerminals.Contains(x) ? n + " reaches other computers, so MinGent never lets the agent use it. For a server, open a terminal with run_command as your instructions say." : "MinGent never lets the agent use " + n + ", because " + NeverWhy(x) + ".";
            if (kind == "a terminal" && Unlocked) return n + " is a terminal. Run commands with run_command; to work in a terminal window the user can watch, open one with run_command as your instructions say. To type in a terminal the user already has open, ask for it with app_access.";
            if (kind == "File Explorer" && Unlocked) return "File Explorer opens inside the user's own session, so it can't be opened for you. Work with files through your file tools; to click and type in a File Explorer window the user has open, ask for it with app_access.";
            if (Unlocked) return null;
            if (kind == "a mail or chat program") return n + " is a mail or chat program, which MinGent keeps off limits." + LockedNote;
            if (kind == "a web browser") return n + " is a web browser, and MinGent keeps the user's own browser off limits. For the web, use your browser tools." + LockedNote;
            if (kind == "a terminal") return n + " is a terminal. Run commands with run_command; to work in a terminal window the user can watch, open one with run_command as your instructions say." + LockedNote;
            if (kind == "File Explorer") return "Work with files through your file tools. To show the user a folder in File Explorer, start it with run_command: explorer \"<folder>\"." + LockedNote;
            return n + " is one of Windows' own tools, which MinGent keeps off limits. If the user only wants it opened for themselves, start it with run_command" + (string.Equals(x, "taskmgr", StringComparison.OrdinalIgnoreCase) ? "; to end a program, use run_command (Stop-Process -Name <name>)." : ".") + LockedNote;
        }

        static readonly object startGate = new object();
        static List<KeyValuePair<string, string>> startApps;
        static DateTime startWhen;

        public static List<KeyValuePair<string, string>> StartApps()
        {
            lock (startGate)
            {
                if (startApps != null && (DateTime.UtcNow - startWhen).TotalSeconds < 60) return startApps;
            }
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return list;
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell\\v1.0\\powershell.exe"),
                    "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding = [Text.Encoding]::UTF8; Get-StartApps | ForEach-Object { $_.Name + [char]9 + $_.AppID }\"");
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardOutput = true;
                si.StandardOutputEncoding = Encoding.UTF8;
                using (Process p = Process.Start(si))
                {
                    StringBuilder got = new StringBuilder();
                    p.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (got) got.Append(e.Data).Append('\n'); };
                    p.BeginOutputReadLine();
                    if (p.WaitForExit(15000)) p.WaitForExit();
                    else try { p.Kill(); } catch { }
                    string text;
                    lock (got) text = got.ToString();
                    foreach (string line in text.Split('\n'))
                    {
                        string l = line.Trim('\r', ' ', '\uFEFF');
                        int tab = l.IndexOf('\t');
                        if (tab <= 0 || tab == l.Length - 1) continue;
                        list.Add(new KeyValuePair<string, string>(l.Substring(0, tab).Trim(), l.Substring(tab + 1).Trim()));
                    }
                }
            }
            catch { }
            list.Sort(delegate (KeyValuePair<string, string> x, KeyValuePair<string, string> y) { return string.Compare(x.Key, y.Key, StringComparison.OrdinalIgnoreCase); });
            if (list.Count > 0) lock (startGate) { startApps = list; startWhen = DateTime.UtcNow; }
            return list;
        }

        public static int PickStart(List<KeyValuePair<string, string>> apps, string want, out string error)
        {
            error = null;
            string w = (want ?? "").Trim();
            if (w.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) w = w.Substring(0, w.Length - 4);
            if (w.Length == 0 || apps == null) { error = "Give the program's name."; return -1; }
            for (int i = 0; i < apps.Count; i++) if (string.Equals(apps[i].Key, w, StringComparison.OrdinalIgnoreCase)) return i;
            List<int> hits = new List<int>();
            for (int i = 0; i < apps.Count; i++) if (apps[i].Key.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(i);
            if (hits.Count == 1) return hits[0];
            if (hits.Count == 0)
            {
                for (int i = 0; i < apps.Count; i++) if (w.IndexOf(apps[i].Key, StringComparison.OrdinalIgnoreCase) >= 0 && apps[i].Key.Length >= 4) hits.Add(i);
                if (hits.Count == 1) return hits[0];
            }
            if (hits.Count == 0 && w.Length >= 4 && w.IndexOf(' ') < 0)
            {
                for (int i = 0; i < apps.Count; i++) if (apps[i].Value.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(i);
                if (hits.Count == 1) return hits[0];
            }
            if (hits.Count == 0) { error = "No program in the Start menu matches \"" + w + "\". Call app_open without name to list them."; return -1; }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hits.Count && i < 8; i++) sb.Append(i > 0 ? ", " : "").Append('"').Append(apps[hits[i]].Key).Append('"');
            error = "Several programs match \"" + w + "\": " + sb + (hits.Count > 8 ? " and more" : "") + ". Use the full name of one.";
            return -1;
        }

        public static void LaunchWindow(DateTime start, DateTime until)
        {
            lock (adoptGate)
            {
                launches.Add(new KeyValuePair<DateTime, DateTime>(start.AddSeconds(-1), until));
                if (launches.Count > 40) launches.RemoveRange(0, launches.Count - 40);
            }
        }

        public static void ForgetAdopted() { lock (adoptGate) { adopted.Clear(); launches.Clear(); } }

        public static bool AgentInFront(Dictionary<string, AppGrant> grants)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                IntPtr root = GetAncestor(fg, 3);
                foreach (AppWindow w in Windows(grants)) if (w.Hwnd == fg || w.Hwnd == root) return true;
            }
            catch { }
            return false;
        }

        static bool ExplorerWindow(string process, string cls)
        {
            return !string.Equals(process, "explorer", StringComparison.OrdinalIgnoreCase) || cls == "CabinetWClass" || cls == "#32770" || cls == "OperationStatusWindow";
        }

        public static string FriendlyName(string exe, string process)
        {
            if (string.Equals(process, "explorer", StringComparison.OrdinalIgnoreCase)) return "File Explorer";
            string plain = null;
            try
            {
                if (!string.IsNullOrEmpty(exe))
                {
                    string d = (FileVersionInfo.GetVersionInfo(exe).FileDescription ?? "").Trim();
                    d = Regex.Replace(d, @"[\x00-\x1F\x7F""\[\]{}<>`]", " ").Trim();
                    if (d.Length > 0 && d.Length <= 60)
                    {
                        d = Regex.Replace(d, @"\s+", " ");
                        if (!string.Equals(d, process, StringComparison.OrdinalIgnoreCase) && !string.Equals(d, process + ".exe", StringComparison.OrdinalIgnoreCase)) return d;
                        plain = d;
                    }
                }
            }
            catch { }
            return StartName(exe) ?? plain ?? process;
        }

        public static bool FromEntry(string exe, string id)
        {
            string e = exe ?? "", i = id ?? "";
            if (e.Length == 0 || i.Length == 0) return false;
            string family = PackageFamily(e);
            if (family != null) return i.StartsWith(family + "!", StringComparison.OrdinalIgnoreCase);
            int cut = Math.Max(e.LastIndexOf('\\'), e.LastIndexOf('/'));
            string file = e.Substring(cut + 1);
            return file.Length > 4 && i.IndexOf(file, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string PackageFamily(string exe)
        {
            string e = exe ?? "";
            int at = e.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            string rest = e.Substring(at + 13);
            int end = rest.IndexOf('\\');
            if (end <= 0) return null;
            string[] parts = rest.Substring(0, end).Split('_');
            if (parts.Length != 5 || parts[0].Length == 0 || parts[4].Length == 0) return null;
            return parts[0] + "_" + parts[4];
        }

        static string StartName(string exe)
        {
            string family = PackageFamily(exe);
            if (family == null) return null;
            string found = null;
            foreach (KeyValuePair<string, string> a in StartApps())
            {
                if (!a.Value.StartsWith(family + "!", StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null && !string.Equals(found, a.Key, StringComparison.OrdinalIgnoreCase)) return null;
                found = a.Key;
            }
            if (found == null) return null;
            found = Regex.Replace(Regex.Replace(found, @"[\x00-\x1F\x7F""\[\]{}<>`]", " "), @"\s+", " ").Trim();
            return found.Length > 0 && found.Length <= 60 ? found : null;
        }

        public static List<OtherApp> OtherApps()
        {
            List<OtherApp> list = new List<OtherApp>();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return list;
            List<IntPtr> jobs = Shell.JobHandles();
            Dictionary<int, bool> member = new Dictionary<int, bool>(), seen = new Dictionary<int, bool>();
            Dictionary<string, OtherApp> byExe = new Dictionary<string, OtherApp>(StringComparer.OrdinalIgnoreCase);
            Dictionary<int, string> names = new Dictionary<int, string>(), exes = new Dictionary<int, string>();
            bool anyLaunch;
            lock (adoptGate) anyLaunch = adopted.Count > 0 || launches.Count > 0;
            EnumProc cb = delegate (IntPtr h, IntPtr l)
            {
                try
                {
                    if (!IsWindowVisible(h)) return true;
                    RECT r;
                    if (!GetWindowRect(h, out r) || r.Right - r.Left < 40 || r.Bottom - r.Top < 30) return true;
                    if (Cloaked(h)) return true;
                    int pid;
                    GetWindowThreadProcessId(h, out pid);
                    StringBuilder sb = new StringBuilder(256);
                    GetClassName(h, sb, 256);
                    string cls = sb.ToString();
                    if (TerminalClass(cls) && OwnsTerminal(h, pid, cls)) return true;
                    if (cls == "ApplicationFrameWindow")
                    {
                        pid = HostedPid(h);
                        if (pid == 0) return true;
                    }
                    if (pid == 0 || pid == OwnPid || InJob(pid, jobs, member) || (anyLaunch && !TerminalClass(cls) && Adopt(pid, seen))) return true;
                    string name;
                    if (!names.TryGetValue(pid, out name))
                    {
                        try { using (Process p = Process.GetProcessById(pid)) name = p.ProcessName; } catch { name = null; }
                        names[pid] = name;
                        exes[pid] = name == null ? null : ExePath(pid);
                    }
                    if (name == null || !ExplorerWindow(name, cls)) return true;
                    if (string.Equals(name, "explorer", StringComparison.OrdinalIgnoreCase) && cls != "CabinetWClass") return true;
                    if (SystemHosts.Contains(name)) return true;
                    string exe = exes[pid] ?? name;
                    OtherApp app;
                    if (!byExe.TryGetValue(exe, out app))
                    {
                        app = new OtherApp();
                        app.Exe = exe;
                        app.Process = name;
                        app.Tier = TierFor(name, cls);
                        app.Admin = !Hard(name) && Admin(pid);
                        byExe[exe] = app;
                    }
                    app.Windows++;
                }
                catch { }
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            foreach (OtherApp app in byExe.Values)
            {
                app.Name = FriendlyName(app.Exe, app.Process);
                list.Add(app);
            }
            list.Sort(delegate (OtherApp x, OtherApp y) { return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        public static OtherApp FindApp(List<OtherApp> apps, string want)
        {
            string w = (want ?? "").Trim().Trim('"', '\'').Trim();
            if (w.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) w = w.Substring(0, w.Length - 4);
            if (w.Length == 0) return null;
            foreach (OtherApp a in apps) if (string.Equals(a.Name, w, StringComparison.OrdinalIgnoreCase) || string.Equals(a.Process, w, StringComparison.OrdinalIgnoreCase)) return a;
            OtherApp only = null;
            int n = 0;
            foreach (OtherApp a in apps)
                if (a.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || a.Process.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) { only = a; n++; }
            return n == 1 ? only : null;
        }

        static bool Adopt(int pid, Dictionary<int, bool> seen)
        {
            bool r;
            if (seen.TryGetValue(pid, out r)) return r;
            r = AdoptNow(pid);
            seen[pid] = r;
            return r;
        }

        static bool AdoptNow(int pid)
        {
            if (pid == 0 || pid == OwnPid) return false;
            DateTime started;
            string name;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    started = p.StartTime.ToUniversalTime();
                    name = p.ProcessName;
                }
            }
            catch { return false; }
            lock (adoptGate)
            {
                DateTime known;
                if (adopted.TryGetValue(pid, out known))
                {
                    if (Math.Abs((known - started).TotalSeconds) < 1) return true;
                    adopted.Remove(pid);
                }
                if (launches.Count == 0) return false;
            }
            if (Hard(name) || (!Unlocked && (Blocked.Contains(name) || SystemTools.Contains(name) || Browsers.Contains(name) || Private.Contains(name)))) return false;
            lock (adoptGate)
            {
                foreach (KeyValuePair<DateTime, DateTime> w in launches)
                    if (started >= w.Key && started <= w.Value) { adopted[pid] = started; return true; }
            }
            return false;
        }

        static int HostedPid(IntPtr frame)
        {
            int found = 0;
            int own;
            GetWindowThreadProcessId(frame, out own);
            EnumProc cb = delegate (IntPtr h, IntPtr l)
            {
                int pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid != own && pid != 0) { found = pid; return false; }
                return true;
            };
            try { EnumChildWindows(frame, cb, IntPtr.Zero); } catch { }
            GC.KeepAlive(cb);
            return found;
        }

        static bool Cloaked(IntPtr h)
        {
            try { int c; return DwmGetWindowAttribute(h, 14, out c, 4) == 0 && c != 0; }
            catch { return false; }
        }

        static bool InJob(int pid, List<IntPtr> jobs, Dictionary<int, bool> member)
        {
            bool inJob;
            if (member.TryGetValue(pid, out inJob)) return inJob;
            inJob = false;
            if (jobs.Count > 0)
            {
                IntPtr hp = OpenProcess(0x1000, false, pid);
                if (hp != IntPtr.Zero)
                {
                    foreach (IntPtr j in jobs)
                    {
                        bool res;
                        if (IsProcessInJob(hp, j, out res) && res) { inJob = true; break; }
                    }
                    CloseHandle(hp);
                }
            }
            member[pid] = inJob;
            return inJob;
        }

        static readonly object termGate = new object();
        static readonly HashSet<IntPtr> seenTerminals = new HashSet<IntPtr>();
        static readonly Dictionary<IntPtr, TerminalState> ownTerminals = new Dictionary<IntPtr, TerminalState>();
        static DateTime termFrom = DateTime.MinValue, termUntil = DateTime.MinValue;
        static int termWanted;

        public static bool TerminalClass(string cls) { return cls == "CASCADIA_HOSTING_WINDOW_CLASS" || cls == "ConsoleWindowClass"; }

        static List<IntPtr> TerminalWindows()
        {
            List<IntPtr> l = new List<IntPtr>();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return l;
            StringBuilder sb = new StringBuilder(64);
            EnumProc cb = delegate (IntPtr h, IntPtr p)
            {
                sb.Length = 0;
                GetClassName(h, sb, 64);
                if (TerminalClass(sb.ToString())) l.Add(h);
                return true;
            };
            try { EnumWindows(cb, IntPtr.Zero); } catch { }
            GC.KeepAlive(cb);
            return l;
        }

        public static void BeforeTerminal()
        {
            List<IntPtr> now = TerminalWindows();
            lock (termGate) foreach (IntPtr h in now) seenTerminals.Add(h);
        }

        public static void WatchTerminals(DateTime from, DateTime until)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            lock (termGate)
            {
                bool running = termUntil > DateTime.UtcNow && termWanted > 0;
                termFrom = from;
                termWanted++;
                if (until > termUntil) termUntil = until;
                if (running) return;
            }
            Thread t = new Thread(delegate ()
            {
                while (true)
                {
                    try { AdoptTerminals(); } catch { }
                    lock (termGate)
                        if (DateTime.UtcNow > termUntil || termWanted <= 0) { termWanted = 0; break; }
                    Thread.Sleep(250);
                }
            });
            t.IsBackground = true;
            t.Name = "MinGent terminal watch";
            t.Start();
        }

        static void AdoptTerminals()
        {
            foreach (IntPtr h in TerminalWindows())
            {
                lock (termGate)
                {
                    if (termWanted <= 0) return;
                    if (seenTerminals.Contains(h)) continue;
                }
                if (!IsWindowVisible(h)) continue;
                int pid;
                GetWindowThreadProcessId(h, out pid);
                StringBuilder sb = new StringBuilder(64);
                GetClassName(h, sb, 64);
                string cls = sb.ToString();
                lock (termGate) seenTerminals.Add(h);
                if (cls == "ConsoleWindowClass" && !TerminalProbe.FromAgent(pid)) continue;
                TerminalState st = new TerminalState();
                st.Pid = pid;
                st.Class = cls;
                lock (termGate)
                {
                    st.Since = termFrom;
                    ownTerminals[h] = st;
                    termWanted--;
                }
            }
        }

        static bool OwnedTerminals()
        {
            lock (termGate)
            {
                List<IntPtr> gone = new List<IntPtr>();
                foreach (IntPtr h in ownTerminals.Keys) if (!IsWindow(h)) gone.Add(h);
                foreach (IntPtr h in gone) ownTerminals.Remove(h);
                return ownTerminals.Count > 0;
            }
        }

        static bool OwnsTerminal(IntPtr h, int pid, string cls)
        {
            TerminalState st;
            lock (termGate) if (!ownTerminals.TryGetValue(h, out st)) return false;
            return st.Pid == pid && st.Class == cls;
        }

        public static bool InAgentJob(int pid)
        {
            List<IntPtr> jobs = Shell.JobHandles();
            return jobs.Count > 0 && InJob(pid, jobs, new Dictionary<int, bool>());
        }

        public static TerminalState TerminalOf(AppWindow w)
        {
            TerminalState st;
            lock (termGate) return w != null && w.Terminal && ownTerminals.TryGetValue(w.Hwnd, out st) ? st : null;
        }

        public static List<AppWindow> Windows()
        {
            return Windows(null);
        }

        public static List<AppWindow> Windows(Dictionary<string, AppGrant> grants)
        {
            List<AppWindow> list = new List<AppWindow>();
            List<IntPtr> jobs = Shell.JobHandles();
            bool anyLaunch;
            lock (adoptGate) anyLaunch = adopted.Count > 0 || launches.Count > 0;
            bool anyTerminal = OwnedTerminals();
            bool anyGrant = grants != null && grants.Count > 0;
            if (jobs.Count == 0 && !anyLaunch && !anyTerminal && !anyGrant) return list;
            Dictionary<int, bool> member = new Dictionary<int, bool>(), seen = new Dictionary<int, bool>();
            Dictionary<int, AppGrant> grantOf = new Dictionary<int, AppGrant>();
            EnumProc cb = delegate (IntPtr h, IntPtr l)
            {
                try
                {
                    if (!IsWindowVisible(h)) return true;
                    RECT r;
                    if (!GetWindowRect(h, out r) || r.Right - r.Left < 40 || r.Bottom - r.Top < 30) return true;
                    if (Cloaked(h)) return true;
                    int pid;
                    GetWindowThreadProcessId(h, out pid);
                    StringBuilder sb = new StringBuilder(512);
                    GetClassName(h, sb, 256);
                    string cls = sb.ToString();
                    bool termClass = TerminalClass(cls), term = termClass && OwnsTerminal(h, pid, cls);
                    if (termClass && !term && !anyGrant) return true;
                    if (cls == "ApplicationFrameWindow")
                    {
                        pid = HostedPid(h);
                        if (pid == 0) return true;
                    }
                    bool inJob = term || (!termClass && InJob(pid, jobs, member)), took = term;
                    if (!inJob && !termClass && anyLaunch && Adopt(pid, seen)) { inJob = true; took = true; }
                    AppGrant grant = null;
                    if (!inJob && anyGrant && pid != OwnPid)
                    {
                        if (!grantOf.TryGetValue(pid, out grant))
                        {
                            string exe = ExePath(pid);
                            if (exe == null || !grants.TryGetValue(exe.ToLowerInvariant(), out grant)) grant = null;
                            grantOf[pid] = grant;
                        }
                    }
                    if (!inJob && grant == null) return true;
                    AppWindow w = new AppWindow();
                    w.Hwnd = h;
                    w.Pid = pid;
                    w.Adopted = took;
                    w.Terminal = term;
                    w.Class = cls;
                    sb.Length = 0;
                    GetWindowText(h, sb, sb.Capacity);
                    w.Title = sb.ToString();
                    try { using (Process p = Process.GetProcessById(pid)) w.Process = p.ProcessName; } catch { w.Process = "?"; }
                    if (grant != null)
                    {
                        if (!ExplorerWindow(w.Process, cls)) return true;
                        string tier = TierFor(w.Process, cls);
                        if (tier == AppTier.Never) return true;
                        w.Granted = true;
                        w.Tier = tier == AppTier.Full ? grant.Tier : tier;
                    }
                    w.Rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    list.Add(w);
                }
                catch { }
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return list;
        }

        public static void Refresh(AppWindow w)
        {
            RECT r;
            if (w != null && GetWindowRect(w.Hwnd, out r)) w.Rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        public static string SystemKey(KeyPress k)
        {
            if (k.Win || k.Vk == 91 || k.Vk == 92) return "the Windows key works on Windows itself, not on the program, so the agent can't press it";
            if (k.Alt && (k.Vk == 9 || k.Vk == 27)) return k.Label + " switches to other windows, so the agent can't press it. To work in another window of your program, pass its window (w1, w2... from app_read)";
            if (k.Ctrl && k.Vk == 27) return k.Label + " opens the Start menu or Task Manager, so the agent can't press it";
            if (k.Vk == 44) return "PrintScreen works on Windows itself; use app_screenshot to see the window";
            if (k.Ctrl && k.Alt && k.Vk == 46) return "Ctrl+Alt+Delete can't be pressed by programs";
            return null;
        }

        public static string Allowed(AppWindow w)
        {
            if (w.Terminal) return null;
            if (Vault.Contains(w.Process) || Guard.Contains(w.Process)) return "'" + w.Process + "' can't be used because " + NeverWhy(w.Process);
            if (Admin(w.Pid)) return AdminNote(w.Title.Length > 0 ? w.Title : w.Behind.Length > 0 ? w.Behind : w.Process);
            if (w.Granted)
            {
                if (TierFor(w.Process, w.Class) != AppTier.Never) return null;
                return Hard(w.Process) ? "'" + w.Process + "' can't be used because " + NeverWhy(w.Process) : "'" + w.Process + "' is " + Kind(w.Process, w.Class) + ", which MinGent keeps off limits." + LockedNote;
            }
            if (RemoteTerminals.Contains(w.Process)) return "'" + w.Process + "' reaches other computers, so the assistant may not control it. For a server, open a terminal with run_command as your instructions say";
            if (Unlocked) return null;
            if (Blocked.Contains(w.Process) || SystemTools.Contains(w.Process)) return "'" + w.Process + "' is a shell or system tool, which the assistant may not control (use run_command for commands)." + LockedNote;
            if (Browsers.Contains(w.Process)) return "'" + w.Process + "' is a web browser with the user's own sign-ins, which the assistant may not control. Use MinGent's built-in browser (browser_navigate) instead." + LockedNote;
            if (Messaging.Contains(w.Process)) return "'" + w.Process + "' handles the user's messages, so the assistant may not control it." + LockedNote;
            if (w.Class == "ConsoleWindowClass" || w.Class == "CASCADIA_HOSTING_WINDOW_CLASS" || w.Class == "PseudoConsoleWindow") return "console windows cannot be controlled (use run_command for commands)." + LockedNote;
            return null;
        }

        static readonly string[] FileDialogParts = { "DUIViewWndClassName", "SHELLDLL_DefView", "SHBrowseForFolder ShellNameSpace Control" };

        static readonly string[] FileDialogWords = { "save", "open", "browse", "folder", "select", "upload", "tallenna", "avaa", "speichern", "\u00F6ffnen", "ordner",
            "enregistrer", "ouvrir", "dossier", "guardar", "abrir", "carpeta", "salva", "apri", "cartella", "salvar", "pasta", "opslaan", "openen",
            "zapisz", "otw\u00F3rz", "\u0441\u043E\u0445\u0440\u0430\u043D", "\u043E\u0442\u043A\u0440", "\u043F\u0430\u043F\u043A", "kaydet", "spara", "\u00F6ppna",
            "lagre", "\u00E5pne", "\u00E5bn", "ulo\u017Eit", "otev\u0159\u00EDt", "ment\u00E9s", "megnyit\u00E1s", "\u4FDD\u5B58", "\u6253\u5F00", "\u958B\u304F", "\u958B\u555F",
            "\uC800\uC7A5", "\uC5F4\uAE30", "\u05E9\u05DE\u05D5\u05E8", "\u05E4\u05EA\u05D7", "\u062D\u0641\u0638", "\u0641\u062A\u062D" };

        public static bool IsFileDialog(AppWindow w)
        {
            if (w.Class != "#32770") return false;
            bool shell = false;
            try
            {
                StringBuilder sb = new StringBuilder(64);
                EnumProc cb = delegate (IntPtr h, IntPtr l)
                {
                    sb.Length = 0;
                    GetClassName(h, sb, 64);
                    if (Array.IndexOf(FileDialogParts, sb.ToString()) >= 0) { shell = true; return false; }
                    return true;
                };
                EnumChildWindows(w.Hwnd, cb, IntPtr.Zero);
                GC.KeepAlive(cb);
            }
            catch { }
            if (shell) return true;
            string t = w.Title.ToLowerInvariant();
            foreach (string word in FileDialogWords) if (t.Contains(word)) return true;
            return false;
        }

        public static bool Exists(IntPtr hwnd) { return IsWindow(hwnd); }

        public static bool Enabled(IntPtr hwnd) { return IsWindowEnabled(hwnd); }

        public static AppWindow BlockingDialog(AppWindow w, List<AppWindow> all)
        {
            if (w == null || !IsWindow(w.Hwnd) || IsWindowEnabled(w.Hwnd)) return null;
            foreach (AppWindow x in all)
                if (x.Hwnd != w.Hwnd && IsWindowEnabled(x.Hwnd) && OwnedBy(x.Hwnd, w.Hwnd)) return x;
            foreach (AppWindow x in all)
                if (x.Hwnd != w.Hwnd && x.Pid == w.Pid && x.Class == "#32770" && IsWindowEnabled(x.Hwnd)) return x;
            return null;
        }

        public static string NewDialog(AppWindow w)
        {
            if (IsWindow(w.Hwnd) && !IsWindowEnabled(w.Hwnd)) return "typing stopped because the program opened a dialog";
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == w.Hwnd) return null;
            IntPtr root = GetAncestor(w.Hwnd, 3), froot = GetAncestor(fg, 3);
            if (froot == IntPtr.Zero) froot = fg;
            if (froot == root || froot == w.Hwnd) return null;
            StringBuilder sb = new StringBuilder(64);
            GetClassName(froot, sb, 64);
            return sb.ToString() == "#32770" ? "typing stopped because the program opened a dialog" : null;
        }

        public static bool IsTextField(AppElement e) { return e != null && (e.Role == RoleText || e.Role == RoleCombo); }

        public static AppWindow Pick(List<AppWindow> all, string which, out string error)
        {
            return Pick(all, which, false, out error);
        }

        public static AppWindow Pick(List<AppWindow> all, string which, bool others, out string error)
        {
            error = null;
            if (all.Count == 0)
            {
                error = "No window of a program you started is open. Open the program with app_open, or start your own with run_command (background=true), wait a moment, then try again. " +
                        (others ? "To use a program the user opened themselves, ask for it with app_access first." : "A program that was already open before your command belongs to the user. They can let you ask for such programs with the switch \"Programs you opened yourself\" in Settings > Agent > Tools: tell them that, and don't work around it.") + AdminMatch(which);
                return null;
            }
            string w = (which ?? "").Trim();
            if (w.Length == 0)
            {
                IntPtr fg = Foreground();
                foreach (AppWindow x in all) if (x.Hwnd == fg && !x.Granted && !x.Veiled) return x;
                foreach (AppWindow x in all) if (!x.Granted && !x.Veiled) return x;
                bool users = false;
                foreach (AppWindow x in all) if (x.Granted) users = true;
                error = "Say which window to use with window=\"w...\"" + (users ? ": windows of the user's own programs are never picked by default." : ".") + "\n" + ListText(all);
                return null;
            }
            Match m = Regex.Match(w, @"^w(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int n;
                if (int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out n))
                {
                    bool ids = false;
                    foreach (AppWindow x in all) if (x.Id > 0) { ids = true; if (x.Id == n) return x; }
                    if (!ids && n >= 1 && n <= all.Count) return all[n - 1];
                }
                error = "there is no window " + w + " now (it may have closed); call app_read to list the windows";
                return null;
            }
            foreach (AppWindow x in all) if (x.Title.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return x;
            error = "no window of your programs has \"" + w + "\" in its title; call app_read to list them." + AdminMatch(w);
            return null;
        }

        public static string ListText(List<AppWindow> all)
        {
            StringBuilder sb = new StringBuilder();
            List<AppWindow> order = new List<AppWindow>(all);
            order.Sort(delegate (AppWindow x, AppWindow y) { return x.Id != y.Id ? x.Id.CompareTo(y.Id) : all.IndexOf(x).CompareTo(all.IndexOf(y)); });
            foreach (AppWindow w in order)
            {
                sb.Append(w.Ref(all)).Append(": \"").Append(w.Title).Append("\" (").Append(w.Process).Append(", ").Append(w.Rect.Width).Append('x').Append(w.Rect.Height);
                if (w.Class == "#32770") sb.Append(", dialog");
                if (w.Granted) sb.Append(w.Tier == AppTier.Look ? ", the user's, look only" : ", the user's");
                if (w.Veiled) sb.Append(w.Barred ? ", off limits" : ", needs the user's OK first");
                AppWindow d = BlockingDialog(w, all);
                if (d != null) sb.Append(", waiting for ").Append(d.Ref(all));
                sb.Append(")\n");
            }
            return sb.ToString();
        }

        public static Bitmap Capture(AppWindow w)
        {
            return Capture(w, true);
        }

        public static Bitmap Capture(AppWindow w, bool fromScreen)
        {
            Bitmap result = null;
            if (!RunTimed(delegate { result = CaptureNow(w, fromScreen); }, 8000)) return null;
            return result;
        }

        static Bitmap CaptureNow(AppWindow w, bool fromScreen)
        {
            if (IsIconic(w.Hwnd)) ShowWindow(w.Hwnd, 9);
            RECT r;
            GetWindowRect(w.Hwnd, out r);
            int width = Math.Max(1, r.Right - r.Left), height = Math.Max(1, r.Bottom - r.Top);
            Bitmap bm = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bm))
            {
                IntPtr hdc = g.GetHdc();
                bool ok;
                try { ok = PrintWindow(w.Hwnd, hdc, 2); }
                finally { g.ReleaseHdc(hdc); }
                if (!ok && !fromScreen)
                {
                    bm.Dispose();
                    return null;
                }
                if (!ok)
                {
                    AppHighlight.OutOfScreenCopy(true);
                    try
                    {
                        Thread.Sleep(50);
                        g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(width, height));
                    }
                    finally { AppHighlight.OutOfScreenCopy(false); }
                }
            }
            w.Rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return bm;
        }

        public static List<AppElement> Elements(AppWindow w, int max, CancelToken ct)
        {
            List<AppElement> list = new List<AppElement>();
            List<AppElement> shared = new List<AppElement>();
            RunTimed(delegate
            {
                object o;
                Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
                if (AccessibleObjectFromWindow(w.Hwnd, 0, ref iid, out o) != 0) return;
                IAccessible root = o as IAccessible;
                if (root == null) return;
                DateTime until = DateTime.UtcNow.AddSeconds(6);
                List<KeyValuePair<AppElement, IAccessible>> later = new List<KeyValuePair<AppElement, IAccessible>>();
                Walk(root, 0, shared, max, until, ct, later, 300);
                foreach (KeyValuePair<AppElement, IAccessible> d in later)
                {
                    int room;
                    lock (shared) room = max - shared.Count;
                    if (room <= 0 || DateTime.UtcNow > until || (ct != null && ct.IsCancelled)) break;
                    List<AppElement> inner = new List<AppElement>();
                    Walk(d.Value, 1, inner, Math.Min(room, 80), until, ct, null, 60);
                    lock (shared)
                    {
                        int at = shared.IndexOf(d.Key);
                        if (at < 0) shared.AddRange(inner);
                        else shared.InsertRange(at + 1, inner);
                    }
                }
            }, 9000);
            lock (shared) list.AddRange(shared);
            return list;
        }

        static void Walk(IAccessible acc, int depth, List<AppElement> list, int max, DateTime until, CancelToken ct, List<KeyValuePair<AppElement, IAccessible>> later, int cap)
        {
            if (depth > 30 || list.Count >= max || DateTime.UtcNow > until || (ct != null && ct.IsCancelled)) return;
            int count = 0;
            try { count = acc.accChildCount; } catch { return; }
            if (count <= 0) return;
            int total = count;
            count = Math.Min(count, cap);
            object[] kids = new object[count];
            int got = 0;
            try { if (AccessibleChildren(acc, 0, count, kids, out got) < 0) return; } catch { return; }
            if (total > count && cap < 300 && got == count)
            {
                AppElement more = new AppElement();
                more.Name = "(" + (total - count).ToString(CultureInfo.InvariantCulture) + " more items in this list are not shown)";
                more.Rect = Rectangle.Empty;
                try { Walk2(acc, kids, got, depth, list, max, until, ct, later, cap); }
                finally { lock (list) list.Add(more); }
                return;
            }
            Walk2(acc, kids, got, depth, list, max, until, ct, later, cap);
        }

        static void Walk2(IAccessible acc, object[] kids, int got, int depth, List<AppElement> list, int max, DateTime until, CancelToken ct, List<KeyValuePair<AppElement, IAccessible>> later, int cap)
        {
            for (int i = 0; i < got && list.Count < max; i++)
            {
                object k = kids[i];
                IAccessible child = k as IAccessible;
                AppElement e = new AppElement();
                if (child != null) { e.Acc = child; e.Child = 0; }
                else if (k is int) { e.Acc = acc; e.Child = k; }
                else continue;
                try
                {
                    object role = e.Acc.get_accRole(e.Child);
                    e.Role = role is int ? (int)role : 0;
                    object st = e.Acc.get_accState(e.Child);
                    e.State = st is int ? (int)st : 0;
                }
                catch { continue; }
                if ((e.State & StateInvisible) != 0) continue;
                try { e.Name = (e.Acc.get_accName(e.Child) ?? "").Replace('\n', ' ').Trim(); } catch { }
                try { if (e.Role == RoleText || e.Role == RoleCombo || e.Role == RoleSlider || e.Role == RoleSpin) e.Value = (e.Acc.get_accValue(e.Child) ?? "").Replace('\n', ' '); } catch { }
                try
                {
                    int l, t, wd, ht;
                    e.Acc.accLocation(out l, out t, out wd, out ht, e.Child);
                    e.Rect = new Rectangle(l, t, wd, ht);
                }
                catch { }
                if (e.Role != RoleTitleBar && e.Role != RoleScrollBar) lock (list) list.Add(e);
                if (child == null || e.Role == RoleTitleBar || e.Role == RoleScrollBar) continue;
                if (later != null && (e.Role == RoleList || e.Role == RoleOutline || e.Role == RoleTable)) { later.Add(new KeyValuePair<AppElement, IAccessible>(e, child)); continue; }
                if (later == null && (e.Role == RoleListItem || e.Role == RoleOutlineItem) && e.Name.Length > 0) continue;
                Walk(child, depth + 1, list, max, until, ct, later, cap);
            }
        }

        public static string RoleName(int role)
        {
            switch (role)
            {
                case RoleButton: return "button";
                case RoleCheck: return "checkbox";
                case RoleRadio: return "radio";
                case RoleText: return "textbox";
                case RoleStatic: return "text";
                case RoleLink: return "link";
                case RoleMenuItem: return "menuitem";
                case RoleCombo: return "combobox";
                case RoleList: return "list";
                case RoleListItem: return "listitem";
                case RoleTab: return "tab";
                case RoleOutlineItem: return "treeitem";
                case RoleSlider: return "slider";
                case RoleSpin: return "spinner";
                case RoleCell: return "cell";
                case RoleDropButton: case RoleSplitButton: return "button";
                case RoleMenuBar: return "menubar";
                case RoleGraphic: return "image";
                case RoleDocument: return "document";
                case RoleColumnHeader: return "columnheader";
                case RoleRowHeader: return "rowheader";
                case RolePageTabList: return "tablist";
                case RoleMenuPopup: return "menu";
                case RoleToolbar: return "toolbar";
                case RoleStatusBar: return "statusbar";
                case RoleOutline: return "tree";
                case RoleTable: return "table";
                default: return null;
            }
        }

        public static bool Actionable(int role)
        {
            return role == RoleButton || role == RoleCheck || role == RoleRadio || role == RoleText || role == RoleLink || role == RoleMenuItem || role == RoleCombo ||
                   role == RoleListItem || role == RoleTab || role == RoleOutlineItem || role == RoleSlider || role == RoleSpin || role == RoleDropButton || role == RoleSplitButton || role == RoleCell;
        }

        public static string Describe(AppElement e, string id)
        {
            string role = RoleName(e.Role) ?? "element";
            StringBuilder sb = new StringBuilder();
            sb.Append('[').Append(id).Append(' ').Append(role);
            if (e.Name.Length > 0) sb.Append(" \"").Append(e.Name.Length > 80 ? e.Name.Substring(0, 80) + "..." : e.Name).Append('"');
            if ((e.State & StateProtected) != 0) sb.Append(" (password)");
            else if (e.Value.Length > 0) sb.Append(" value=\"").Append(e.Value.Length > 100 ? e.Value.Substring(0, 100) + "..." : e.Value).Append('"');
            if ((e.State & StateChecked) != 0) sb.Append(" checked");
            if ((e.State & StateSelected) != 0) sb.Append(" selected");
            if ((e.State & StatePressed) != 0) sb.Append(" pressed");
            if ((e.State & StateExpanded) != 0) sb.Append(" expanded");
            if ((e.State & StateFocused) != 0) sb.Append(" focused");
            if ((e.State & StateUnavailable) != 0) sb.Append(" disabled");
            sb.Append(']');
            return sb.ToString();
        }

        public static bool IsPassword(AppElement e) { return (e.State & StateProtected) != 0; }

        public static string FocusWhenIdle(AppWindow w, CancelToken ct)
        {
            if (!IsWindow(w.Hwnd)) return "the window was closed";
            if (!WaitForUser(ct)) return "stopped by the user";
            return Focus(w);
        }

        public static string Focus(AppWindow w)
        {
            if (!IsWindow(w.Hwnd)) return "the window was closed";
            if (IsIconic(w.Hwnd)) ShowWindow(w.Hwnd, 9);
            IntPtr root = GetAncestor(w.Hwnd, 3);
            if (root == IntPtr.Zero) root = w.Hwnd;
            IntPtr fg = GetForegroundWindow();
            if (fg == w.Hwnd || fg == root || GetAncestor(fg, 3) == root) return null;
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            SetForegroundWindow(w.Hwnd);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(50);
                fg = GetForegroundWindow();
                if (fg == w.Hwnd || fg == root || GetAncestor(fg, 3) == root || OwnedBy(fg, w.Hwnd)) return null;
            }
            return "Windows did not let the assistant bring \"" + w.Title + "\" to the front. Ask the user to click that window once, then try again.";
        }

        static bool OwnedBy(IntPtr h, IntPtr owner)
        {
            for (int i = 0; i < 6 && h != IntPtr.Zero; i++)
            {
                h = GetWindow(h, 4);
                if (h == owner) return true;
            }
            return false;
        }

        public static bool StillFocused(AppWindow w)
        {
            IntPtr fg = GetForegroundWindow();
            IntPtr root = GetAncestor(w.Hwnd, 3);
            if (root == IntPtr.Zero) root = w.Hwnd;
            int pid;
            GetWindowThreadProcessId(fg, out pid);
            return fg == w.Hwnd || fg == root || GetAncestor(fg, 3) == root || OwnedBy(fg, w.Hwnd) || pid == w.Pid;
        }

        public static bool StaysFocused(AppWindow w)
        {
            for (int i = 0; i < 4; i++)
            {
                if (StillFocused(w)) return true;
                Thread.Sleep(40);
            }
            return StillFocused(w);
        }

        static int InputSize { get { return Marshal.SizeOf(typeof(INPUT)); } }

        static void Mods(MouseSpec m, bool up)
        {
            List<INPUT> l = new List<INPUT>();
            if (m.Ctrl) l.Add(KeyInput(0x11, up));
            if (m.Shift) l.Add(KeyInput(0x10, up));
            if (m.Alt) l.Add(KeyInput(0x12, up));
            if (l.Count > 0) { SendInput((uint)l.Count, l.ToArray(), InputSize); Thread.Sleep(20); }
        }

        static void Button(string button, bool up)
        {
            uint f = button == "right" ? (up ? 0x0010u : 0x0008u) : button == "middle" ? (up ? 0x0040u : 0x0020u) : (up ? 0x0004u : 0x0002u);
            INPUT[] i = new INPUT[1];
            i[0].type = 0;
            i[0].u.mi.dwFlags = f;
            SendInput(1, i, InputSize);
        }

        static void ShowPointer(int x, int y, bool click)
        {
            int wait = AgentCursor.PointAt(new Point(x, y), click);
            if (click && wait > 0) Thread.Sleep(Math.Min(wait, 200));
        }

        public static void Click(int x, int y, MouseSpec m)
        {
            if (m == null) m = new MouseSpec();
            ShowPointer(x, y, true);
            POINT old;
            GetCursorPos(out old);
            SetCursorPos(x, y);
            Thread.Sleep(30);
            Mods(m, false);
            try
            {
                for (int i = 0; i < Math.Max(1, m.Clicks); i++)
                {
                    Button(m.Button, false);
                    Thread.Sleep(m.HoldMs > 0 && i == m.Clicks - 1 ? m.HoldMs : 40);
                    Button(m.Button, true);
                    Thread.Sleep(40);
                }
            }
            finally { Mods(m, true); }
            Thread.Sleep(60);
            if (m.HoldMs == 0) SetCursorPos(old.X, old.Y);
        }

        public static void Hover(int x, int y)
        {
            ShowPointer(x, y, false);
            SetCursorPos(x, y);
            Thread.Sleep(60);
            SetCursorPos(x + 1, y);
            Thread.Sleep(30);
            SetCursorPos(x, y);
        }

        public static void Wheel(int x, int y, bool horizontal, int notches)
        {
            ShowPointer(x, y, false);
            POINT old;
            GetCursorPos(out old);
            SetCursorPos(x, y);
            Thread.Sleep(40);
            int left = Math.Max(-200, Math.Min(200, notches));
            while (left != 0)
            {
                int step = Math.Max(-5, Math.Min(5, left));
                INPUT[] i = new INPUT[1];
                i[0].type = 0;
                i[0].u.mi.dwFlags = horizontal ? 0x01000u : 0x0800u;
                i[0].u.mi.mouseData = unchecked((uint)(step * 120));
                SendInput(1, i, InputSize);
                left -= step;
                Thread.Sleep(30);
            }
            Thread.Sleep(80);
            SetCursorPos(old.X, old.Y);
        }

        public static void Drag(int x1, int y1, int x2, int y2)
        {
            ShowPointer(x1, y1, true);
            AgentCursor.PointAt(new Point(x2, y2), false);
            POINT old;
            GetCursorPos(out old);
            SetCursorPos(x1, y1);
            Thread.Sleep(50);
            Button("left", false);
            Thread.Sleep(120);
            const int steps = 15;
            for (int k = 1; k <= steps; k++)
            {
                SetCursorPos(x1 + (x2 - x1) * k / steps, y1 + (y2 - y1) * k / steps);
                Thread.Sleep(20);
            }
            Thread.Sleep(120);
            Button("left", true);
            Thread.Sleep(80);
            SetCursorPos(old.X, old.Y);
        }

        public static string Move(AppWindow w, int dx, int dy, CancelToken ct)
        {
            int steps = Math.Max(1, (Math.Max(Math.Abs(dx), Math.Abs(dy)) + 49) / 50);
            int doneX = 0, doneY = 0;
            for (int k = 1; k <= steps; k++)
            {
                if (ct != null && ct.IsCancelled) return "stopped by the user after moving the mouse by " + doneX + "," + doneY;
                if (!StaysFocused(w)) return "the mouse stopped after moving by " + doneX + "," + doneY + " of " + dx + "," + dy + " because another window came to the front";
                int toX = (int)((long)dx * k / steps), toY = (int)((long)dy * k / steps);
                INPUT[] i = new INPUT[1];
                i[0].type = 0;
                i[0].u.mi.dx = toX - doneX;
                i[0].u.mi.dy = toY - doneY;
                i[0].u.mi.dwFlags = 0x0001;
                SendInput(1, i, InputSize);
                doneX = toX;
                doneY = toY;
                Thread.Sleep(10);
            }
            return null;
        }

        public static string Press(AppWindow w, List<KeyPress> keys) { return Press(w, keys, 0, null); }

        public static string Press(AppWindow w, List<KeyPress> keys, int holdMs, CancelToken ct)
        {
            int tid = GetWindowThreadProcessId(w.Hwnd, IntPtr.Zero);
            IntPtr hkl = GetKeyboardLayout(tid);
            for (int n = 0; n < keys.Count; n++)
            {
                KeyPress k = keys[n];
                string sys = null;
                foreach (KeyPress part in KeyCombo.Parts(k)) if (sys == null) sys = SystemKey(part);
                if (sys != null) return sys;
                if (ct != null && ct.IsCancelled) return "stopped by the user";
                if (!StaysFocused(w)) return "the keys stopped after " + n + " of " + keys.Count + " because another window came to the front";
                List<ushort> mods = new List<ushort>();
                ushort vk;
                bool ext = k.Extended;
                if (k.Printable && k.Char != 0)
                {
                    short r = VkKeyScanEx(k.Char, hkl);
                    if (r == -1)
                    {
                        if (k.Ctrl || k.Alt || holdMs > 0 || k.With != null) return "\"" + k.Label + "\" can't be " + (holdMs > 0 ? "held" : "pressed") + ": the character " + k.Char + " is not on this keyboard layout";
                        TypeChar(k.Char);
                        continue;
                    }
                    vk = (ushort)(r & 0xFF);
                    bool shift = (r & 0x100) != 0 || k.Shift;
                    if (k.Char >= 'A' && k.Char <= 'Z' && !k.Shift && (k.Ctrl || k.Alt || k.With != null)) shift = false;
                    if (k.Ctrl || (r & 0x200) != 0) mods.Add(0x11);
                    if (k.Alt || (r & 0x400) != 0) mods.Add(0x12);
                    if (shift) mods.Add(0x10);
                }
                else
                {
                    vk = (ushort)k.Vk;
                    if (k.Ctrl) mods.Add(0x11);
                    if (k.Alt) mods.Add(0x12);
                    if (k.Shift) mods.Add(0x10);
                }
                if (holdMs <= 0 && k.With == null) Key(vk, mods, ext);
                else
                {
                    List<ushort> with = new List<ushort>();
                    List<bool> withExt = new List<bool>();
                    if (k.With != null)
                        foreach (KeyPress x in k.With)
                        {
                            ushort xv = (ushort)x.Vk;
                            if (x.Printable && x.Char != 0)
                            {
                                short r = VkKeyScanEx(x.Char, hkl);
                                if (r == -1) return "\"" + k.Label + "\" can't be pressed: the character " + x.Char + " is not on this keyboard layout";
                                xv = (ushort)(r & 0xFF);
                            }
                            with.Add(xv);
                            withExt.Add(x.Extended);
                        }
                    string lost = Hold(w, vk, ext, mods, with, withExt, holdMs, ct);
                    if (lost != null) return lost;
                }
                if (n + 1 < keys.Count) Thread.Sleep(k.Code == "Enter" || k.Code == "Tab" ? 90 : 25);
            }
            return null;
        }

        static string Hold(AppWindow w, ushort vk, bool ext, List<ushort> mods, List<ushort> with, List<bool> withExt, int holdMs, CancelToken ct)
        {
            List<INPUT> pressed = new List<INPUT>();
            string why = null;
            try
            {
                foreach (ushort m in mods) { Send(KeyInput(m, false)); pressed.Add(KeyInput(m, true)); Thread.Sleep(15); }
                for (int i = 0; i < with.Count; i++) { Send(Ext(KeyInput(with[i], false), withExt[i])); pressed.Add(Ext(KeyInput(with[i], true), withExt[i])); Thread.Sleep(15); }
                Send(Ext(KeyInput(vk, false), ext));
                pressed.Add(Ext(KeyInput(vk, true), ext));
                if (holdMs <= 0) { Thread.Sleep(40); return null; }
                DateTime start = DateTime.UtcNow, end = start.AddMilliseconds(holdMs), next = start.AddMilliseconds(500);
                while (DateTime.UtcNow < end)
                {
                    if (ct != null && ct.IsCancelled) { why = "stopped by the user; the keys were let go"; break; }
                    if (!StillFocused(w)) { why = "the keys were let go after " + (int)(DateTime.UtcNow - start).TotalMilliseconds + " ms because another window came to the front"; break; }
                    if (DateTime.UtcNow >= next) { Send(Ext(KeyInput(vk, false), ext)); next = next.AddMilliseconds(80); }
                    Thread.Sleep(15);
                }
            }
            finally
            {
                for (int i = pressed.Count - 1; i >= 0; i--) { Send(pressed[i]); Thread.Sleep(10); }
            }
            return why;
        }

        static INPUT Ext(INPUT i, bool extended)
        {
            if (extended) i.u.ki.flags |= 0x0001;
            return i;
        }

        static void Send(INPUT i)
        {
            INPUT[] one = { i };
            SendInput(1, one, InputSize);
        }

        static void TypeChar(char c)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = 1; inputs[0].u.ki.scan = c; inputs[0].u.ki.flags = 0x0004;
            inputs[1].type = 1; inputs[1].u.ki.scan = c; inputs[1].u.ki.flags = 0x0004 | 0x0002;
            SendInput(2, inputs, InputSize);
            Thread.Sleep(4);
        }

        public static void Click(int x, int y, bool right, bool dbl)
        {
            ShowPointer(x, y, true);
            POINT old;
            GetCursorPos(out old);
            SetCursorPos(x, y);
            Thread.Sleep(30);
            uint down = right ? 0x0008u : 0x0002u, up = right ? 0x0010u : 0x0004u;
            for (int i = 0; i < (dbl ? 2 : 1); i++)
            {
                INPUT[] inputs = new INPUT[2];
                inputs[0].type = 0; inputs[0].u.mi.dwFlags = down;
                inputs[1].type = 0; inputs[1].u.mi.dwFlags = up;
                SendInput(2, inputs, InputSize);
                Thread.Sleep(40);
            }
            Thread.Sleep(60);
            SetCursorPos(old.X, old.Y);
        }

        public static string Type(AppWindow w, string text)
        {
            int sent = 0;
            foreach (char c in text)
            {
                if (!StaysFocused(w)) return "typing stopped because another window came to the front";
                if (sent % 16 == 0 || c == '\n' || c == '\t')
                {
                    string d = NewDialog(w);
                    if (d != null) return d;
                }
                if (c == '\r') continue;
                if (c == '\n') { Key(0x0D, null); sent++; Thread.Sleep(30); continue; }
                if (c == '\t') { Key(0x09, null); sent++; continue; }
                INPUT[] inputs = new INPUT[2];
                inputs[0].type = 1; inputs[0].u.ki.scan = c; inputs[0].u.ki.flags = 0x0004;
                inputs[1].type = 1; inputs[1].u.ki.scan = c; inputs[1].u.ki.flags = 0x0004 | 0x0002;
                SendInput(2, inputs, InputSize);
                sent++;
                Thread.Sleep(4);
            }
            return null;
        }

        public static void Key(ushort vk, List<ushort> mods) { Key(vk, mods, false); }

        public static void Key(ushort vk, List<ushort> mods, bool extended)
        {
            List<INPUT> l = new List<INPUT>();
            if (mods != null) foreach (ushort m in mods) l.Add(KeyInput(m, false));
            INPUT down = KeyInput(vk, false), up = KeyInput(vk, true);
            if (extended) { down.u.ki.flags |= 0x0001; up.u.ki.flags |= 0x0001; }
            l.Add(down);
            l.Add(up);
            if (mods != null) for (int i = mods.Count - 1; i >= 0; i--) l.Add(KeyInput(mods[i], true));
            SendInput((uint)l.Count, l.ToArray(), InputSize);
            Thread.Sleep(30);
        }

        static INPUT KeyInput(ushort vk, bool up)
        {
            INPUT i = new INPUT();
            i.type = 1;
            i.u.ki.vk = vk;
            uint flags = up ? 0x0002u : 0u;
            if (vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 || vk == 0x21 || vk == 0x22 || vk == 0x23 || vk == 0x24 || vk == 0x2D || vk == 0x2E) flags |= 0x0001;
            i.u.ki.flags = flags;
            return i;
        }
    }

    static class ClipboardGuard
    {
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
        static readonly object gate = new object();
        static uint owned;

        public static uint Now()
        {
            try { return GetClipboardSequenceNumber(); }
            catch { return 0; }
        }

        public static void AfterCopy(uint before)
        {
            uint now = Now();
            lock (gate) owned = now != 0 && now != before ? now : 0;
        }

        public static bool AgentOwns()
        {
            uint now = Now();
            lock (gate) return now != 0 && now == owned;
        }

        static bool Has(KeyPress k, string code)
        {
            foreach (KeyPress p in KeyCombo.Parts(k)) if (p.Code == code) return true;
            return false;
        }

        public static bool IsCopy(KeyPress k) { return k.Ctrl && !k.Alt && (Has(k, "KeyC") || Has(k, "KeyX") || Has(k, "Insert")) || (k.Shift && !k.Ctrl && !k.Alt && Has(k, "Delete")); }

        public static bool IsPaste(KeyPress k) { return (k.Ctrl && !k.Alt && Has(k, "KeyV")) || (k.Shift && !k.Ctrl && !k.Alt && Has(k, "Insert")); }

        static readonly Regex PasteWords = new Regex(@"^\s*(paste|einf\u00FCgen|coller|pegar|incolla|liit\u00E4|plakken|wklej|\u0432\u0441\u0442\u0430\u0432\u0438\u0442\u044C|klistra in|lim inn|inds\u00E6t|vlo\u017Eit|beilleszt\u00E9s|yap\u0131\u015Ft\u0131r|\u8CBC\u308A\u4ED8\u3051|\u7C98\u8D34|\u8CBC\u4E0A|\uBD99\uC5EC\uB123\uAE30)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool PasteLabel(string name) { return !string.IsNullOrEmpty(name) && PasteWords.IsMatch(name.Replace("&", "")); }
    }

    class AppSession
    {
        public readonly Dictionary<string, AppElement> Refs = new Dictionary<string, AppElement>();
        public IntPtr RefsWindow = IntPtr.Zero;
        public int RefsThread;
        public int Next;
        public IntPtr ShotWindow = IntPtr.Zero;
        public double ShotScale = 1;
        public int ShotW, ShotH;
        public readonly Dictionary<string, AppGrant> Grants = new Dictionary<string, AppGrant>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<IntPtr, KeyValuePair<int, int>> WinIds = new Dictionary<IntPtr, KeyValuePair<int, int>>();
        public int NextWin = 1;
    }

    abstract class AppTool : AgentTool
    {
        static readonly Dictionary<AgentSession, AppSession> states = new Dictionary<AgentSession, AppSession>();

        protected static AppSession State(ToolContext ctx)
        {
            lock (states)
            {
                AppSession s;
                if (!states.TryGetValue(ctx.Session, out s)) { s = new AppSession(); states[ctx.Session] = s; }
                if (s.RefsThread != Thread.CurrentThread.ManagedThreadId) { s.Refs.Clear(); s.RefsThread = Thread.CurrentThread.ManagedThreadId; }
                return s;
            }
        }

        protected static Dictionary<string, object> WindowArg() { return P("string", "Which window: w1, w2... from app_read, or part of its title (default: the program's front window)"); }

        public static void ForgetGrants(AgentSession session)
        {
            AppSession st;
            lock (states) if (session == null || !states.TryGetValue(session, out st)) return;
            lock (st.Grants) st.Grants.Clear();
        }

        public static Dictionary<string, AppGrant> GrantsOf(AgentSession session)
        {
            AppSession st;
            lock (states) if (session == null || !states.TryGetValue(session, out st)) return null;
            lock (st.Grants) return new Dictionary<string, AppGrant>(st.Grants, StringComparer.OrdinalIgnoreCase);
        }

        protected static Dictionary<string, AppGrant> Grants(ToolContext ctx)
        {
            if (ctx.Cfg == null || !ctx.Cfg.OtherApps) return null;
            AppSession st = State(ctx);
            lock (st.Grants) return new Dictionary<string, AppGrant>(st.Grants, StringComparer.OrdinalIgnoreCase);
        }

        protected static List<AppWindow> All(ToolContext ctx)
        {
            AppSession st = State(ctx);
            List<AppWindow> all = AppControl.Windows(Grants(ctx));
            lock (st.WinIds)
            {
                foreach (AppWindow w in all)
                {
                    KeyValuePair<int, int> known;
                    if (st.WinIds.TryGetValue(w.Hwnd, out known) && known.Value == w.Pid) w.Id = known.Key;
                    else
                    {
                        w.Id = st.NextWin++;
                        st.WinIds[w.Hwnd] = new KeyValuePair<int, int>(w.Id, w.Pid);
                    }
                }
            }
            foreach (AppWindow w in all)
            {
                if (w.Granted || w.Terminal) continue;
                w.Barred = AppControl.Allowed(w) != null;
                if (!w.Barred && Restricted(ctx, w, false) == null) continue;
                w.Veiled = true;
                w.Behind = w.Title;
                w.Title = "";
            }
            return all;
        }

        protected static AppGrant GrantFor(ToolContext ctx, AppWindow w)
        {
            string exe = AppControl.ExePath(w.Pid);
            if (exe == null) return null;
            AppSession st = State(ctx);
            AppGrant g;
            lock (st.Grants) return st.Grants.TryGetValue(exe.ToLowerInvariant(), out g) ? g : null;
        }

        protected virtual string Act { get { return "look"; } }

        public static string TierGate(AppWindow w, string act)
        {
            if (w == null || !w.Granted || w.Tier == AppTier.Full) return null;
            if (act == "look" || act == "scroll" || act == "hover") return null;
            string kind = AppControl.Kind(w.Process, w.Class);
            return "'" + w.Process + "' is " + kind + " the user opened, so you may only look at it: app_read, app_screenshot, app_scroll and app_hover. " +
                   (kind == "a web browser" ? "To open pages, click or type on the web, use MinGent's own browser (browser_navigate)." :
                    kind == "a terminal" ? "Run commands with run_command instead." :
                    kind == "File Explorer" ? "Work with files through your file tools instead." :
                    "Change files with your file tools and run commands with run_command instead.") +
                   (AppControl.AlwaysLook(w.Process) ? "" : AppControl.Unlocked ? " The user has switched on " + AppControl.Switch + ": ask again with app_access to click and type in it." : AppControl.LockedNote);
        }

        protected static string Restricted(ToolContext ctx, AppWindow w, bool ask)
        {
            if (w.Granted || w.Terminal || !AppControl.Limited(w.Process, w.Class)) return null;
            if (!AppControl.Unlocked || AppControl.Allowed(w) != null) return "'" + w.Process + "' is off limits";
            PermissionState ps = ctx.Session.Perms;
            string key = "restricted:" + UseName(w);
            lock (ps.AskLock) if (ps.AllowedKeys.Contains(key)) return null;
            if (!ask) return "not allowed yet";
            string exe = AppControl.ExePath(w.Pid) ?? w.Process;
            return AskRestricted(ctx, key, AppControl.FriendlyName(exe, w.Process), exe, AppControl.Kind(w.Process, w.Class), w.Adopted);
        }

        public static string AskRestricted(ToolContext ctx, string key, string name, string exe, string kind, bool adopted)
        {
            PermissionState ps = ctx.Session.Perms;
            PermissionAsk a = new PermissionAsk();
            a.Kind = "app";
            a.EvenInFull = true;
            a.AllowRemember = false;
            a.Category = "";
            a.High = true;
            a.Title = "Let the agent use " + name;
            a.Detail = exe;
            a.Where = "Until this chat ends";
            a.Reason = (adopted ? "Windows opened this program for something the agent did in this chat. " : "The agent started this program in this chat. ") + (AppControl.Risk(kind) ?? "The agent will see its windows and can click and type in them.") +
                       (ps.WebGuard && ps.Tainted ? " This chat has read web content (" + ps.TaintSource + "), so make sure a page didn't slip in this request." : "");
            a.Key = key;
            if (!ctx.Approve(a)) return "The user did not allow using " + name + ". Don't try to reach it another way; continue without it or ask the user.";
            lock (ps.AskLock) ps.AllowedKeys.Add(key);
            return null;
        }

        protected string Gate(ToolContext ctx, AppWindow w, bool acting)
        {
            return Gate(ctx, w, acting, false, acting ? Act : "look");
        }

        protected string Gate(ToolContext ctx, AppWindow w, bool acting, bool cancelOnly)
        {
            return Gate(ctx, w, acting, cancelOnly, acting ? Act : "look");
        }

        protected string Gate(ToolContext ctx, AppWindow w, bool acting, bool cancelOnly, string act)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return "app control only works on Windows";
            string why = AppControl.Allowed(w);
            if (why != null) return why;
            string tier = TierGate(w, act);
            if (tier != null) return tier;
            bool arrange = ctx.Cfg.SideBySide && !w.Granted;
            bool early = arrange && !w.Adopted && !Refused(ctx, w);
            if (early) SideBySide(ctx, w);
            AppHighlight.Show(w.Hwnd);
            string denied = Check(ctx, w, acting, cancelOnly, act);
            if (denied != null) { AppHighlight.Hide(); return denied; }
            if (!early && arrange) SideBySide(ctx, w);
            return null;
        }

        static string GrantCheck(ToolContext ctx, AppWindow w, bool acting, bool tainted)
        {
            AppGrant g = GrantFor(ctx, w);
            if (g == null) return "The user has not allowed '" + w.Process + "' in this chat. Ask for it with app_access first.";
            if (!acting || !tainted || g.SawWeb) return null;
            PermissionAsk a = new PermissionAsk();
            a.Kind = "app";
            a.EvenInFull = true;
            a.AllowRemember = false;
            a.Category = "";
            a.Title = "Keep letting the agent use " + g.Name;
            a.Detail = g.Exe;
            a.Reason = "This chat has read web content since you allowed " + g.Name + ", and a page could have slipped in instructions. The agent wants to " + (g.Tier == AppTier.Full ? "click and type" : "click") + " in it again.";
            a.Key = "appgrant-web:" + g.Exe.ToLowerInvariant();
            if (!ctx.Approve(a)) return "The user did not allow using " + g.Name + " again after this chat read web content. Don't try to reach it another way.";
            g.SawWeb = true;
            return null;
        }

        static void SideBySide(ToolContext ctx, AppWindow w)
        {
            if (!AppHighlight.Arrange(w.Hwnd)) return;
            ctx.Ct.Sleep(400);
            AppControl.Refresh(w);
        }

        static bool Refused(ToolContext ctx, AppWindow w)
        {
            string exe = UseName(w);
            PermissionState ps = ctx.Session.Perms;
            lock (ps.AskLock) return ps.DeniedKeys.Contains("app:" + exe) || ps.DeniedKeys.Contains("appview:" + exe);
        }

        protected static string UseName(AppWindow w)
        {
            string exe = (AppControl.ExePath(w.Pid) ?? w.Process).ToLowerInvariant();
            return w.Terminal ? exe + "#" + w.Hwnd.ToInt64().ToString(CultureInfo.InvariantCulture) : exe;
        }

        protected static string Check(ToolContext ctx, AppWindow w, bool acting, bool cancelOnly, string act)
        {
            PermissionState ps = ctx.Session.Perms;
            bool tainted = ps.WebGuard && ps.Tainted;
            string limited = Restricted(ctx, w, true);
            if (limited != null) return limited;
            if (w.Veiled && AppControl.Allowed(w) == null)
            {
                w.Veiled = false;
                w.Title = w.Behind;
            }
            if (w.Terminal && !acting && ps.Auto) return null;
            if (acting && !cancelOnly && AppControl.IsFileDialog(w))
            {
                if (w.Granted)
                {
                    string again = GrantCheck(ctx, w, true, tainted);
                    if (again != null) return again;
                }
                PermissionAsk fd = new PermissionAsk();
                fd.Kind = "app";
                fd.Category = ctx.Guarded(PermissionCategory.Launch);
                fd.Title = "Use a file dialog in " + w.Process;
                fd.Detail = "\"" + w.Title + "\"";
                fd.Reason = "Typing in a file dialog can open or save files anywhere on your PC.";
                fd.Key = "appdialog:" + w.Pid;
                if (!ctx.Approve(fd)) return "The user did not allow using this file dialog.";
                return null;
            }
            if (w.Granted) return GrantCheck(ctx, w, act != "look" && act != "scroll" && act != "hover", tainted);
            bool trusted = !w.Adopted && AppControl.Trusted(w, ctx.Workspace);
            if (ps.Auto && !(acting && tainted) && (trusted || (!acting && !w.Adopted))) return null;
            string exe = AppControl.ExePath(w.Pid) ?? w.Process;
            string useKey = "app:" + UseName(w);
            if (!acting) lock (ps.AskLock) if (ps.AllowedKeys.Contains(useKey)) return null;
            bool use = acting || w.Adopted;
            PermissionAsk a = new PermissionAsk();
            a.Kind = "app";
            if (ps.Auto && !(acting && tainted)) a.Category = PermissionCategory.Launch;
            a.Title = (use ? "Click and type in " : "Look at ") + "\"" + (w.Title.Length > 0 ? w.Title : w.Process) + "\"";
            a.Detail = exe + (w.Adopted ? " (Windows opened it for a command the agent ran in this chat)" : " (started by the agent in this chat)");
            a.Key = use ? useKey : "appview:" + UseName(w);
            if (w.Terminal) a.Title = (use ? "Type commands in " : "Look at ") + "the terminal \"" + (w.Title.Length > 0 ? w.Title : w.Process) + "\"";
            a.Reason = w.Terminal ? "The agent opened this terminal in this chat, and you can watch it work there. MinGent checks each command before Enter runs it and asks before risky ones, also on servers reached with ssh." :
                       tainted && acting ? "This chat has read web content, so controlling programs needs your OK." :
                       w.Adopted ? "Windows started this program outside the agent's own process group, so it can't be fully told apart from a program you opened yourself. The agent will look at its window, move the mouse and type in it." :
                       acting && !trusted ? "This program is not part of the project in the workspace. The agent will move the mouse and type in its window." :
                       acting ? "It will move the mouse and type into this program's window, like a person testing it." : "It will capture this program's window.";
            return ctx.Approve(a) ? null : "The user did not allow this.";
        }

        protected static AppWindow Find(ToolContext ctx, Dictionary<string, object> a, out string error)
        {
            List<AppWindow> all = All(ctx);
            bool named = (S(a, "window") ?? "").Trim().Length > 0;
            bool refs = (S(a, "ref") ?? "").Trim().Length > 0 || (S(a, "to_ref") ?? "").Trim().Length > 0;
            if (!named && refs)
            {
                IntPtr from = State(ctx).RefsWindow;
                foreach (AppWindow x in all) if (from != IntPtr.Zero && x.Hwnd == from) { error = null; return x; }
            }
            return AppControl.Pick(all, S(a, "window"), ctx.Cfg.OtherApps, out error);
        }

        protected static string Waiting(ToolContext ctx, AppWindow w)
        {
            List<AppWindow> all = All(ctx);
            AppWindow d = AppControl.BlockingDialog(w, all);
            if (d == null) return null;
            string id = d.Ref(all);
            return "\"" + (w.Title.Length > 0 ? w.Title : w.Process) + "\" is waiting for its dialog \"" + d.Title + "\" (" + id + "), so it does not take clicks or keys now. Use that dialog first: app_read with window=\"" + id + "\" shows it; finish it or press Escape there to cancel it.";
        }

        protected static string ScreenPoint(Dictionary<string, object> a, AppWindow w, AppSession st, string refKey, string xKey, string yKey, bool required, out Point p, out string what)
        {
            p = Point.Empty;
            what = null;
            string r = (S(a, refKey) ?? "").Trim();
            if (r.Length > 0)
            {
                AppElement el;
                if (!st.Refs.TryGetValue(r, out el)) return r + " is not a known ref. Call app_read first (refs change when the window changes).";
                if (el.Rect.Width <= 0 || el.Rect.Height <= 0) return r + " has no position on screen (it may be hidden or scrolled away). Use app_screenshot and x/y instead.";
                p = new Point(el.Rect.X + el.Rect.Width / 2, el.Rect.Y + el.Rect.Height / 2);
                what = r + (el.Name.Length > 0 ? " (" + (el.Name.Length > 40 ? el.Name.Substring(0, 40) + "..." : el.Name) + ")" : "");
                return null;
            }
            if (a != null && a.ContainsKey(xKey) && a.ContainsKey(yKey))
            {
                int x = (int)Math.Round(D(a, xKey, -1)), y = (int)Math.Round(D(a, yKey, -1));
                bool shot = st.ShotWindow == w.Hwnd && st.ShotScale > 0;
                double scale = shot ? st.ShotScale : 1;
                int limW = shot && st.ShotW > 0 ? st.ShotW : w.Rect.Width, limH = shot && st.ShotH > 0 ? st.ShotH : w.Rect.Height;
                if (x < 0 || y < 0 || x >= limW || y >= limH) return xKey + "," + yKey + " must be inside the screenshot (0-" + (limW - 1) + ", 0-" + (limH - 1) + ")";
                p = new Point(w.Rect.X + (int)Math.Round(x * scale), w.Rect.Y + (int)Math.Round(y * scale));
                what = "at " + x + "," + y;
                return null;
            }
            if (required) return "give " + refKey + " (a ref from app_read) or " + xKey + " and " + yKey + " (a position in app_screenshot)";
            p = new Point(w.Rect.X + w.Rect.Width / 2, w.Rect.Y + w.Rect.Height / 2);
            what = "in the middle of the window";
            return null;
        }

        protected static AppWindow RefWindow(ToolContext ctx, Dictionary<string, object> a, AppWindow w, AppSession st, out string error, params string[] refKeys)
        {
            error = null;
            bool anyRef = false;
            foreach (string k in refKeys) if (!string.IsNullOrEmpty((S(a, k) ?? "").Trim())) anyRef = true;
            if (!anyRef || st.RefsWindow == w.Hwnd) return w;
            List<AppWindow> all = All(ctx);
            AppWindow owner = null;
            foreach (AppWindow x in all) if (x.Hwnd == st.RefsWindow) owner = x;
            if (owner == null) { error = "those refs came from a window that is gone now; call app_read again"; return null; }
            if (S(a, "window") != null) { error = "those refs came from " + owner.Ref(all) + " (\"" + owner.Title + "\"), not from the window you named. Use window=\"" + owner.Ref(all) + "\", or call app_read on the window you mean first."; return null; }
            return owner;
        }

        protected static string PasteGate(ToolContext ctx, AppWindow w)
        {
            if (ClipboardGuard.AgentOwns()) return null;
            PermissionAsk ask = new PermissionAsk();
            ask.Kind = "app";
            ask.Category = ctx.Guarded(PermissionCategory.Credentials);
            ask.High = true;
            ask.Title = "Paste your clipboard into \"" + (w.Title.Length > 0 ? w.Title : w.Process) + "\"";
            ask.Detail = w.Process;
            ask.Key = "paste:app:" + w.Process.ToLowerInvariant();
            ask.Reason = "Whatever is on your clipboard (it can hold passwords or private text) would go into this program, where the agent can read it.";
            return ctx.Approve(ask) ? null : "The user did not allow pasting the clipboard. Type the text with app_type instead.";
        }

        protected static ToolOutcome Denied(string g)
        {
            ToolOutcome d = ToolOutcome.Fail(g);
            d.Denied = g.StartsWith("The user", StringComparison.Ordinal);
            return d;
        }

        protected static ToolOutcome After(ToolContext ctx, AppWindow w, int ms, string how)
        {
            string note;
            AppWindow now = Settle(ctx, w, ms, out note);
            if (now == null) return ToolOutcome.Ok(how + ". The program has no open windows now.", how);
            ToolOutcome o = ToolOutcome.Ok(Read(ctx, now, State(ctx), how + "." + (note != null ? " " + note : "")), how);
            o.Untrusted = true;
            return o;
        }

        protected static AppWindow Settle(ToolContext ctx, AppWindow w, int ms, out string note)
        {
            note = null;
            ctx.Ct.Sleep(ms);
            for (int i = 0; i < 30 && !ctx.Ct.IsCancelled; i++)
            {
                if (!AppControl.Exists(w.Hwnd) || AppControl.Enabled(w.Hwnd)) break;
                AppWindow d = AppControl.BlockingDialog(w, All(ctx));
                if (d != null && !d.Veiled)
                {
                    ctx.Ct.Sleep(350);
                    foreach (AppWindow x in All(ctx)) if (x.Hwnd == d.Hwnd) d = x;
                    note = "The program opened the dialog \"" + d.Title + "\".";
                    return d;
                }
                ctx.Ct.Sleep(100);
            }
            List<AppWindow> all = All(ctx);
            if (all.Count == 0) return null;
            string e2;
            if (!w.Granted) return AppControl.Pick(all, null, out e2) ?? Open(all);
            IntPtr fg = AppControl.Foreground();
            foreach (AppWindow x in all) if (x.Hwnd == fg && x.Pid == w.Pid) return x;
            foreach (AppWindow x in all) if (x.Hwnd == w.Hwnd) return x;
            foreach (AppWindow x in all) if (x.Pid == w.Pid) return x;
            return AppControl.Pick(all, null, out e2) ?? Open(all);
        }

        static AppWindow Open(List<AppWindow> all)
        {
            foreach (AppWindow x in all) if (!x.Veiled) return x;
            return null;
        }

        protected static string TerminalEnter(ToolContext ctx, AppWindow w, TerminalState t, string act)
        {
            return TerminalEnter(ctx, w, t, act, null);
        }

        protected static string TerminalEnter(ToolContext ctx, AppWindow w, TerminalState t, string act, string description)
        {
            string line, why;
            bool known;
            lock (t) { line = t.Line; known = t.Known; why = t.Why; }
            TerminalPlace place = TerminalProbe.Place(w, t);
            string shown = line.Trim().Length > 120 ? line.Trim().Substring(0, 120) + "..." : line.Trim();
            string what = known && shown.Length > 0 ? "\"" + shown + "\"" : "unchecked text";
            string bad = CommandSafety.Invisible(line);
            string refused = bad != null ? "Not run: " + bad + "." : Net.Offline && ((place.Name != null && !place.Linux) || !known || CommandSafety.NeedsInternet(line)) ? "Not run: " + Net.OfflineMessage : null;
            if (refused == null)
            {
                PermissionAsk a = TerminalCheck.Ask(line, known, why, act, place, ctx.Session.Perms, ctx.Workspace, w.Title.Length > 0 ? w.Title : w.Process, description);
                if (a == null || ctx.Approve(a)) return null;
                refused = (act == null ? "The user did not allow running " + what : "The user did not allow the agent to " + act + " while " + what + " is on the command line") + (place.Name != null ? " on " + place.Name : "") + ".";
            }
            bool cleared = ClearLine(w, t);
            return refused + (cleared ? " MinGent cleared the command line, so nothing runs by accident." : " It is still typed in the terminal: press Ctrl+C there to clear it.") + " Don't run it another way.";
        }

        static string OfflineRemote(AppWindow w, TerminalState t)
        {
            if (!Net.Offline) return null;
            TerminalPlace place = TerminalProbe.Place(w, t);
            if (place.Name == null || place.Linux) return null;
            return "Not typed: this terminal is connected to " + place.Name + ", and " + Net.OfflineMessage;
        }

        static string ControlChars(string text)
        {
            foreach (char c in text ?? "")
            {
                if (c == '\t') return "the text has a Tab. In a terminal Tab completes the line, so press it with app_key instead.";
                if ((c < ' ' && c != '\n' && c != '\r') || (c >= '\u007F' && c <= '\u009F')) return "the text has control characters, which a terminal treats as keys that can run commands. Type plain text and press keys with app_key.";
            }
            return null;
        }

        static bool ClearLine(AppWindow w, TerminalState t)
        {
            bool? selected = TerminalProbe.Selected(w.Hwnd);
            if (AppControl.Focus(w) != null) return false;
            AppControl.Key(0x1B, null);
            AppControl.Key(0x43, new List<ushort> { 0x11 });
            lock (t) { if (selected == false) t.Cleared(); else { t.Cleared(); t.Lost("clearing the line"); } }
            return true;
        }

        protected static ToolOutcome TerminalType(ToolContext ctx, AppWindow w, string text, bool enter, string description)
        {
            TerminalState t = AppControl.TerminalOf(w);
            if (t == null) return ToolOutcome.Fail("this terminal window is no longer open");
            string ctl = ControlChars(text);
            if (ctl != null) return ToolOutcome.Fail(ctl);
            string off = OfflineRemote(w, t);
            if (off != null) return ToolOutcome.Fail(off);
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int chars = 0, ran = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string part = lines[i];
                if (part.Length > 0)
                {
                    string f = AppControl.FocusWhenIdle(w, ctx.Ct);
                    if (f != null) return ToolOutcome.Fail(f);
                    string terr = AppControl.Type(w, part);
                    lock (t) { t.Typed(part); if (terr != null) t.Lost("typing that stopped halfway"); }
                    if (terr != null) return ToolOutcome.Fail(terr);
                    chars += part.Length;
                }
                if (i == lines.Length - 1 && !enter) break;
                string deny = TerminalEnter(ctx, w, t, null, description);
                if (deny != null) return Denied(deny);
                string f2 = AppControl.FocusWhenIdle(w, ctx.Ct);
                if (f2 != null) return ToolOutcome.Fail(f2);
                AppControl.Key(0x0D, null);
                lock (t) t.Ran();
                ran++;
                if (i < lines.Length - 1) ctx.Ct.Sleep(300);
            }
            string how = "Typed " + chars + " characters" + (ran == 1 ? " and pressed Enter" : ran > 1 ? " and ran " + ran + " lines" : "");
            return After(ctx, w, ran > 0 ? 1200 : 300, how);
        }

        protected static ToolOutcome TerminalKeys(ToolContext ctx, AppWindow w, List<KeyPress> keys, string description)
        {
            TerminalState t = AppControl.TerminalOf(w);
            if (t == null) return ToolOutcome.Fail("this terminal window is no longer open");
            foreach (KeyPress k in keys)
                if (k.With != null) return ToolOutcome.Fail("in a terminal, press keys one after another, not several at once (" + k.Label + "). Shortcuts like Ctrl+C are fine.");
            string off = OfflineRemote(w, t);
            if (off != null) return ToolOutcome.Fail(off);
            List<KeyPress> chunk = new List<KeyPress>();
            int ran = 0;
            for (int i = 0; i <= keys.Count; i++)
            {
                KeyPress key = i < keys.Count ? keys[i] : null;
                if (key != null && TerminalState.Free(key)) { chunk.Add(key); continue; }
                if (chunk.Count > 0)
                {
                    bool copyKey = false;
                    foreach (KeyPress k in chunk) if (k.Ctrl && !k.Alt && !k.Shift && k.Vk == 'C') copyKey = true;
                    if (copyKey) { bool? sel = TerminalProbe.Selected(w.Hwnd); lock (t) t.Selection = sel != false; }
                    string f = AppControl.FocusWhenIdle(w, ctx.Ct);
                    if (f != null) return ToolOutcome.Fail(f);
                    string kerr = AppControl.Press(w, chunk, 0, ctx.Ct);
                    lock (t) { foreach (KeyPress k in chunk) t.Pressed(k); if (kerr != null) t.Lost("keys that stopped halfway"); }
                    if (kerr != null) return ToolOutcome.Fail(kerr);
                    chunk.Clear();
                }
                if (key == null) break;
                bool enter = TerminalState.IsEnter(key);
                string deny = TerminalEnter(ctx, w, t, enter ? null : "press " + key.Label, enter ? description : null);
                if (deny != null) return Denied(deny);
                string f2 = AppControl.FocusWhenIdle(w, ctx.Ct);
                if (f2 != null) return ToolOutcome.Fail(f2);
                string err = AppControl.Press(w, new List<KeyPress> { key }, 0, ctx.Ct);
                lock (t)
                {
                    if (err != null) t.Lost("keys that stopped halfway");
                    else if (enter) t.Ran();
                    else t.Pressed(key);
                }
                if (err != null) return ToolOutcome.Fail(err);
                if (enter) ran++;
            }
            return After(ctx, w, ran > 0 ? 1200 : 400, KeyCombo.Describe(keys, 0));
        }

        static string ReadTerminal(AppWindow w, string header)
        {
            string text = TerminalProbe.Text(w.Hwnd, 6000);
            if (text == null) return null;
            TerminalState t = AppControl.TerminalOf(w);
            TerminalPlace at = t != null ? TerminalProbe.Place(w, t) : null;
            string place = at != null ? at.Name : null;
            bool unsure = false;
            if (t != null) lock (t) unsure = !t.Known;
            StringBuilder sb = new StringBuilder();
            if (header != null) sb.Append(header).Append('\n');
            sb.Append("Terminal \"").Append(w.Title).Append("\" (").Append(w.Process).Append("), ").Append(place != null ? (at.Sure ? "connected to " : "maybe connected to ") + place : "on this PC").Append('\n');
            sb.Append("What it shows now, newest at the bottom:\n").Append(text.Length > 0 ? text : "(nothing yet)").Append('\n');
            sb.Append("Type with app_type (enter=true runs the line) and press keys with app_key. MinGent checks the command line before Enter and before other special keys or clicks");
            if (unsure) sb.Append("; the command line may hold text it hasn't checked, so the next Enter or special key asks the user (Ctrl+C clears the line)");
            sb.Append(".\n");
            return sb.ToString();
        }

        protected static string Read(ToolContext ctx, AppWindow w, AppSession st, string header)
        {
            if (w.Terminal)
            {
                string tt = ReadTerminal(w, header);
                if (tt != null) return tt;
            }
            List<AppElement> els = AppControl.Elements(w, 350, ctx.Ct);
            st.Refs.Clear();
            st.RefsWindow = w.Hwnd;
            StringBuilder sb = new StringBuilder();
            if (header != null) sb.Append(header).Append('\n');
            sb.Append("Window \"").Append(w.Title).Append("\" (").Append(w.Process).Append("), ").Append(w.Rect.Width).Append('x').Append(w.Rect.Height).Append(" at ").Append(w.Rect.X).Append(',').Append(w.Rect.Y).Append('\n');
            int shown = 0;
            foreach (AppElement e in els)
            {
                string role = AppControl.RoleName(e.Role);
                bool act = AppControl.Actionable(e.Role);
                if (role == null && e.Name.Length == 0) continue;
                if (!act && e.Name.Length == 0) continue;
                if (act)
                {
                    string id = "a" + (++st.Next);
                    st.Refs[id] = e;
                    sb.Append(AppControl.Describe(e, id)).Append('\n');
                }
                else sb.Append(role != null && role != "text" ? role + ": " : "").Append(e.Name.Length > 200 ? e.Name.Substring(0, 200) + "..." : e.Name).Append('\n');
                if (++shown > 300) { sb.Append("(more elements not shown)\n"); break; }
            }
            if (els.Count == 0) sb.Append("(this program does not expose its controls to accessibility tools; use app_screenshot and click by position)\n");
            else sb.Append("Refs like a12 work with app_click and app_type until the window changes.\n");
            return sb.ToString();
        }
    }

    class AppAccessTool : AppTool
    {
        public override string Name { get { return "app_access"; } }
        public override string Description { get { return "Ask to use a program the user opened themselves (one you didn't start). Without app it lists the open programs you can ask for. With app MinGent asks the user, and once they allow it, its windows show up in app_read for the rest of this chat." + (AppControl.Unlocked ? "" : " Web browsers, File Explorer, terminals and code editors can only be looked at."); } }
        public override Dictionary<string, object> Schema { get { return Obj("app", P("string", "The program's name from the list, e.g. \"Notepad\""), false, "reason", P("string", "One short sentence for the user: what you need the program for"), false); } }
        public override string Label(Dictionary<string, object> a) { string n = (S(a, "app") ?? "").Trim(); return n.Length == 0 ? "List the open programs" : "Ask to use " + (n.Length > 40 ? n.Substring(0, 40) + "\u2026" : n); }
        public override bool Untrusted { get { return true; } }

        static string TierNote(string tier)
        {
            return tier == AppTier.Look ? "look only" : "look, click and type";
        }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return ToolOutcome.Fail("app control only works on Windows");
            List<OtherApp> apps = AppControl.OtherApps();
            Dictionary<string, AppGrant> have = Grants(ctx);
            string want = (S(a, "app") ?? "").Trim();
            if (want.Length == 0)
            {
                StringBuilder sb = new StringBuilder();
                List<string> allowed = new List<string>();
                foreach (OtherApp x in apps)
                {
                    if (x.Tier == AppTier.Never) continue;
                    if (have.ContainsKey(x.Exe.ToLowerInvariant())) { allowed.Add(x.Name); continue; }
                    sb.Append("- ").Append(x.Name).Append(" (").Append(x.Process).Append(", ").Append(x.Windows).Append(x.Windows == 1 ? " window" : " windows");
                    if (x.Admin) sb.Append(", runs as administrator: out of reach");
                    else if (x.Tier != AppTier.Full) sb.Append(", ").Append(TierNote(x.Tier));
                    sb.Append(")\n");
                }
                string head = allowed.Count > 0 ? "Already allowed in this chat (see app_read): " + string.Join(", ", allowed.ToArray()) + ".\n" : "";
                if (sb.Length == 0) return ToolOutcome.Ok(head + "No other programs you could ask for are open. " + (AppControl.Unlocked ? "Programs for passwords, money and remote access and Windows' sign-in and security are never offered." : "Programs for passwords, money and remote access are never offered, and mail, chat and Windows' own tools only when the user switches on " + AppControl.Switch + "."), "No programs to ask for");
                ToolOutcome lo = ToolOutcome.Ok(head + "Programs the user has open that you can ask to use:\n" + sb + "Ask with app_access app=\"<name>\" only when the task needs that program; MinGent asks the user first.", "Listed open programs");
                lo.Untrusted = true;
                return lo;
            }
            OtherApp hit = AppControl.FindApp(apps, want);
            if (hit == null)
            {
                string proc = want.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? want.Substring(0, want.Length - 4) : want;
                if (AppControl.TierFor(proc, "") == AppTier.Never) return ToolOutcome.Fail(AppControl.Refusal(want, proc));
                return ToolOutcome.Fail("No open program matches \"" + want + "\". Call app_access without app to list them, and use a name from that list.");
            }
            if (hit.Admin) return ToolOutcome.Fail(AppControl.AdminNote(hit.Name));
            if (hit.Tier == AppTier.Never) return ToolOutcome.Fail(AppControl.Refusal(hit.Name, hit.Process));
            string key = hit.Exe.ToLowerInvariant();
            AppGrant old;
            bool more = have.TryGetValue(key, out old) && old.Tier == AppTier.Look && hit.Tier == AppTier.Full;
            if (!have.ContainsKey(key) || more)
            {
                PermissionState ps = ctx.Session.Perms;
                bool tainted = ps.WebGuard && ps.Tainted;
                string kind = AppControl.Kind(hit.Process, "");
                string risk = hit.Tier == AppTier.Full ? AppControl.Risk(kind) : null;
                PermissionAsk ask = new PermissionAsk();
                ask.Kind = "app";
                ask.EvenInFull = true;
                ask.AllowRemember = false;
                ask.Category = "";
                ask.High = hit.Tier != AppTier.Look;
                ask.Title = (hit.Tier == AppTier.Look ? "Let the agent look at " : "Let the agent use ") + hit.Name;
                ask.Summary = PermissionAsk.PlainSummary(S(a, "reason"), 160);
                ask.Detail = hit.Exe;
                ask.Where = "Until this chat ends";
                ask.Reason = risk != null ? "You opened this program yourself. " + risk :
                             hit.Tier == AppTier.Full ? "You opened this program yourself. The agent will see its windows and can click, type and press keys in them, like a person at your PC. Whatever the program can do, such as running macros or scripts, the agent can do in it too." :
                             kind == "a web browser" ? "You opened this browser yourself. The agent will see its windows, including the pages you have open, but can't click or type in them; for the web it uses MinGent's own browser." :
                             kind == "File Explorer" ? "You opened File Explorer yourself. The agent will see its windows, but can't click or type in them; it works with files through its own file tools." :
                             kind == "a terminal" ? "You opened this terminal yourself. The agent will see what it shows, but can't click or type in it, so it can't run anything there." :
                             "You opened this editor yourself. The agent will see its windows, but can't click or type in them; it changes files with its own file tools.";
                if (tainted) ask.Reason += " This chat has read web content (" + ps.TaintSource + "), so make sure a page didn't slip in this request.";
                ask.Key = (more ? "appgrant-full:" : "appgrant:") + key;
                if (!ctx.Approve(ask))
                {
                    ToolOutcome d = ToolOutcome.Fail("The user did not allow using " + hit.Name + ". Don't try to reach it another way; continue without it or ask the user.");
                    d.Denied = true;
                    d.Summary = "Not allowed";
                    return d;
                }
                AppGrant g = new AppGrant();
                g.Exe = hit.Exe;
                g.Name = hit.Name;
                g.Tier = hit.Tier;
                g.SawWeb = tainted;
                AppSession st = State(ctx);
                lock (st.Grants) st.Grants[key] = g;
            }
            List<AppWindow> mine = new List<AppWindow>();
            List<AppWindow> all = All(ctx);
            foreach (AppWindow w in all) if (w.Granted && string.Equals(AppControl.ExePath(w.Pid) ?? "", hit.Exe, StringComparison.OrdinalIgnoreCase)) mine.Add(w);
            StringBuilder ws = new StringBuilder();
            foreach (AppWindow w in mine) ws.Append(w.Ref(all)).Append(": \"").Append(w.Title).Append("\"\n");
            ToolOutcome o = ToolOutcome.Ok("You may use " + hit.Name + " in this chat (" + TierNote(hit.Tier) + "). " +
                (mine.Count > 0 ? "Its windows:\n" + ws + "Read one with app_read window=\"" + mine[0].Ref(all) + "\"." : "It has no window open right now; it shows up in app_read when it has one."),
                "Allowed: " + hit.Name);
            o.Untrusted = true;
            return o;
        }
    }

    class AppOpenTool : AppTool
    {
        public override string Name { get { return "app_open"; } }
        public bool Others;
        public override string Description { get { return "Open a program on this PC by its name in the Start menu, such as \"Calculator\" or \"Excel\". Without name it lists the programs that can be opened. A program you open this way counts as one you started: read it with app_read."; } }
        public override Dictionary<string, object> Schema { get { return Obj("name", P("string", "The program's name as the Start menu shows it"), false, "reason", P("string", "One short sentence for the user: what you need the program for"), false); } }
        public override string Label(Dictionary<string, object> a) { string n = (S(a, "name") ?? "").Trim(); return n.Length == 0 ? "List the programs on this PC" : "Open " + (n.Length > 40 ? n.Substring(0, 40) + "\u2026" : n); }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return ToolOutcome.Fail("app control only works on Windows");
            List<KeyValuePair<string, string>> apps = AppControl.StartApps();
            if (apps.Count == 0) return ToolOutcome.Fail("Windows did not give the list of programs in the Start menu. Start the program with run_command (background=true) instead.");
            string want = (S(a, "name") ?? "").Trim();
            if (want.Length == 0)
            {
                StringBuilder sb = new StringBuilder();
                int n = 0;
                string last = null;
                foreach (KeyValuePair<string, string> x in apps)
                {
                    if (string.Equals(x.Key, last, StringComparison.OrdinalIgnoreCase) || AppControl.OpenBlock(x.Key, x.Value) != null) continue;
                    last = x.Key;
                    if (++n > 250) break;
                    sb.Append(x.Key).Append('\n');
                }
                return ToolOutcome.Ok("Programs in the Start menu of this PC that you can open" + (n > 250 ? " (the first 250; one that is not listed here can still be opened by its name)" : "") + ":\n" + sb + "Open one with app_open name=\"<name>\".", "Listed the programs");
            }
            string wrong;
            int at = AppControl.PickStart(apps, want, out wrong);
            if (at < 0) return ToolOutcome.Fail(wrong);
            string name = apps[at].Key, id = apps[at].Value;
            string block = AppControl.OpenBlock(name, id);
            if (block != null) return ToolOutcome.Fail(block);
            string kindOf;
            string risk = AppControl.Risk(AppControl.OpenKind(name, id, out kindOf));
            if (id.IndexOf('"') >= 0) return ToolOutcome.Fail("Windows gave an unusable entry for " + name + ". Start it with run_command (background=true) instead.");
            if (risk != null)
                foreach (OtherApp x in AppControl.OtherApps())
                    if (string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) || AppControl.FromEntry(x.Exe.ToLowerInvariant(), id) ||
                        (string.Equals(x.Process, kindOf, StringComparison.OrdinalIgnoreCase) && (id.IndexOf(kindOf, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(kindOf, StringComparison.OrdinalIgnoreCase) >= 0)))
                        return ToolOutcome.Ok(name + " is already open, so it is the user's. " + (Others ? "Ask for it with app_access app=\"" + x.Name + "\"." : "It is off limits until they switch on \"Programs you opened yourself\" in Settings > Agent > Tools: tell them that."), name + " is already open");
            PermissionState ps = ctx.Session.Perms;
            PermissionAsk ask = new PermissionAsk();
            ask.Kind = "app";
            ask.Category = ctx.Guarded(PermissionCategory.Launch);
            ask.Title = "Open " + name;
            ask.Summary = PermissionAsk.PlainSummary(S(a, "reason"), 160);
            int slash = id.IndexOf('\\');
            if (slash >= 0) ask.Detail = id.StartsWith("{", StringComparison.Ordinal) && id.IndexOf('}') == slash - 1 ? id.Substring(slash + 1) : id;
            ask.Where = "Until this chat ends";
            ask.Reason = "The agent wants to start this program from the Start menu. It will see its windows and can click, type and press keys in them, like a person at your PC. Whatever the program can do, such as running macros or scripts, the agent can do in it too.";
            ask.Key = "open:" + id.ToLowerInvariant();
            if (risk != null)
            {
                ask.Category = "";
                ask.EvenInFull = true;
                ask.AllowRemember = false;
                ask.High = true;
                ask.Title = "Open " + name + " and let the agent use it";
                ask.Reason = "The agent wants to start this program from the Start menu. " + risk + (ps.WebGuard && ps.Tainted ? " This chat has read web content (" + ps.TaintSource + "), so make sure a page didn't slip in this request." : "");
            }
            bool asked = risk != null || (!ps.Full && !(ps.Auto && ps.AutoAllows(ask.Category)));
            if (!ctx.Approve(ask))
            {
                ToolOutcome d = ToolOutcome.Fail("The user did not allow opening " + name + ". Don't start it another way; continue without it or ask the user.");
                d.Denied = true;
                d.Summary = "Not opened";
                return d;
            }
            Dictionary<IntPtr, bool> before = new Dictionary<IntPtr, bool>();
            foreach (AppWindow w in All(ctx)) before[w.Hwnd] = true;
            Dictionary<IntPtr, string> raised = AppControl.AdminWindows();
            DateTime now = DateTime.UtcNow;
            AppControl.LaunchWindow(now, now.AddSeconds(30));
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"shell:AppsFolder\\" + id + "\"");
                si.UseShellExecute = false;
                using (Process p = Process.Start(si)) { }
            }
            catch (Exception ex) { return ToolOutcome.Fail("Windows could not open " + name + ": " + ex.Message); }
            List<AppWindow> all = null;
            List<AppWindow> fresh = new List<AppWindow>();
            for (int i = 0; i < 20 && fresh.Count == 0 && !ctx.Ct.IsCancelled; i++)
            {
                Thread.Sleep(400);
                all = All(ctx);
                foreach (AppWindow w in all) if (!before.ContainsKey(w.Hwnd)) fresh.Add(w);
            }
            if (fresh.Count == 0)
            {
                string admin = null;
                foreach (KeyValuePair<IntPtr, string> x in AppControl.AdminWindows())
                    if (!raised.ContainsKey(x.Key) || x.Value.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) { admin = x.Value; break; }
                if (admin != null) return ToolOutcome.Ok("Windows opened " + name + " for the user. " + AppControl.AdminNote(admin), "Opened " + name + ", out of reach");
                StringBuilder have = new StringBuilder();
                if (all != null) foreach (AppWindow w in all) if (!w.Veiled) have.Append(w.Ref(all)).Append(": \"").Append(w.Title).Append("\" (").Append(w.Process).Append(")\n");
                ToolOutcome late = ToolOutcome.Ok("Windows was asked to open " + name + ", but no new window of it has shown up for you. " +
                    (have.Length > 0 ? "Windows you may use now:\n" + have : "") +
                    "It may still be starting: look with app_read in a moment. If it is not there then, either it was already running and came to the front, which makes it the user's" +
                    (Others ? " (ask for it with app_access)" : " (off limits until they switch on \"Programs you opened yourself\" in Settings > Agent > Tools)") +
                    ", or it is a kind of program MinGent never hands to you: " + (AppControl.Unlocked ? "" : "browsers, mail, chat, ") + "password, money and remote-access programs and Windows' " + (AppControl.Unlocked ? "sign-in and security." : "own tools."), "Asked to open " + name);
                late.Untrusted = have.Length > 0;
                return late;
            }
            if (asked)
            {
                string one = null;
                bool same = true;
                foreach (AppWindow w in fresh)
                {
                    string u = UseName(w);
                    if (one == null) one = u;
                    else if (one != u) same = false;
                }
                if (same && !fresh[0].Terminal && AppControl.FromEntry(one, id))
                    lock (ps.AskLock)
                    {
                        ps.AllowedKeys.Add("app:" + one);
                        if (risk != null) ps.AllowedKeys.Add("restricted:" + one);
                    }
                all = All(ctx);
                List<AppWindow> again = new List<AppWindow>();
                foreach (AppWindow w in all) foreach (AppWindow f in fresh) if (f.Hwnd == w.Hwnd) again.Add(w);
                if (again.Count > 0) fresh = again;
            }
            StringBuilder ws = new StringBuilder();
            foreach (AppWindow w in fresh) ws.Append(w.Ref(all)).Append(": \"").Append(w.Title).Append("\" (").Append(w.Process).Append(")\n");
            ToolOutcome o = ToolOutcome.Ok("Opened " + name + ". Its window" + (fresh.Count == 1 ? "" : "s") + ":\n" + ws + "Read it with app_read window=\"" + fresh[0].Ref(all) + "\".", "Opened " + name);
            o.Untrusted = true;
            return o;
        }
    }

    class ScreenLookTool : AppTool
    {
        public override string Name { get { return "screen_look"; } }
        public bool Others;
        public override string Description { get { return "See all the windows you may use in one picture, each where it is on the main screen, to find your way between them (programs you started" + (Others ? ", and ones the user allowed with app_access" : "") + "). Windows of other programs are left out, also where they are in front of yours. It lists each window with its place in the picture. To act in a window, use app_read or app_screenshot on it."; } }
        public override Dictionary<string, object> Schema { get { return Obj(); } }
        public override string Label(Dictionary<string, object> a) { return "Look at the screen"; }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return ToolOutcome.Fail("app control only works on Windows");
            List<AppWindow> all = All(ctx);
            string none = "No program you may use is open. Open one with app_open" + (Others ? ", or ask for one the user has open with app_access." : ".");
            if (all.Count == 0) return ToolOutcome.Ok(none, "Nothing to show");
            List<AppWindow> may = new List<AppWindow>();
            Dictionary<IntPtr, bool> refused = new Dictionary<IntPtr, bool>();
            foreach (AppWindow w in AppControl.OnScreen(all))
            {
                if (AppControl.Allowed(w) == null && Restricted(ctx, w, false) == null && Check(ctx, w, false, false, "look") == null) may.Add(w);
                else refused[w.Hwnd] = true;
                if (ctx.Ct.IsCancelled) return ToolOutcome.Fail("stopped");
            }
            List<KeyValuePair<AppWindow, Rectangle>> shown;
            Size full;
            Bitmap bm = AppControl.ScreenPicture(may, out shown, out full);
            ImageData img = null;
            int iw = 0, ih = 0;
            if (bm != null)
                using (bm)
                {
                    const int maxSide = 1366;
                    double sc = Math.Min(1.0, (double)maxSide / Math.Max(bm.Width, bm.Height));
                    iw = Math.Max(1, (int)(bm.Width * sc));
                    ih = Math.Max(1, (int)(bm.Height * sc));
                    img = ImageTools.Encode(bm, maxSide);
                }
            double k = full.Width > 0 && iw > 0 ? (double)iw / full.Width : 1.0;
            StringBuilder sb = new StringBuilder();
            if (img != null) sb.Append("The main screen, ").Append(iw).Append("x").Append(ih).Append(" pixels in this picture, with only the windows you may use. Windows of other programs are left out, also where they are in front of yours.\n");
            else sb.Append("None of the windows you may use is on the main screen right now, so there is no picture.\n");
            Dictionary<IntPtr, bool> listed = new Dictionary<IntPtr, bool>();
            foreach (KeyValuePair<AppWindow, Rectangle> e in shown)
            {
                listed[e.Key.Hwnd] = true;
                sb.Append("- ").Append(e.Key.Ref(all)).Append(" \"").Append(e.Key.Title).Append("\" (").Append(e.Key.Process).Append("): at ")
                  .Append((int)(e.Value.Left * k)).Append(",").Append((int)(e.Value.Top * k)).Append(", ").Append((int)(e.Value.Width * k)).Append("x").Append((int)(e.Value.Height * k)).Append('\n');
            }
            foreach (AppWindow w in all)
            {
                if (listed.ContainsKey(w.Hwnd) || refused.ContainsKey(w.Hwnd) || w.Veiled) continue;
                string where = AppControl.Place(w);
                sb.Append("- ").Append(w.Ref(all)).Append(" \"").Append(w.Title).Append("\" (").Append(w.Process).Append(")").Append(where.Length > 0 ? ": " + where + ", not in the picture" : ": not in the picture").Append('\n');
            }
            sb.Append(img != null ? "This picture is for finding your way. To act in a window, use app_read or app_screenshot with its window id: app_click positions come from those, not from this picture."
                : "app_read and app_screenshot still work on a window that is minimized or on another screen.");
            if (img == null)
            {
                ToolOutcome t = ToolOutcome.Ok(sb.ToString(), "No window on the screen");
                t.Untrusted = true;
                return t;
            }
            ToolOutcome o = ToolOutcome.Ok(sb.ToString(), shown.Count == 1 ? "1 window on the screen" : shown.Count + " windows on the screen");
            o.Images = new List<ImageData>(new ImageData[] { img });
            o.Untrusted = true;
            return o;
        }
    }

    class AppReadTool : AppTool
    {
        public override string Name { get { return "app_read"; } }
        public bool Others;
        public override string Description { get { return "Test a desktop program you started (with app_open, or run_command background=true): lists its windows and the controls of one window (buttons, fields, text, menus) with refs like a12 for app_click and app_type. Only programs started by you in this chat can be read, including ones Windows opened for your command (such as Store apps)" + (Others ? ", and programs the user allowed with app_access." : "."); } }
        public override Dictionary<string, object> Schema { get { return Obj("window", WindowArg(), false); } }
        public override string Label(Dictionary<string, object> a) { string w = S(a, "window"); return "Read the app window" + (string.IsNullOrEmpty(w) ? "" : " " + w); }
        public override bool Untrusted { get { return true; } }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            List<AppWindow> all = All(ctx);
            string err;
            AppWindow w = AppControl.Pick(all, S(a, "window"), ctx.Cfg.OtherApps, out err);
            if (w == null) return ToolOutcome.Fail(err);
            string g = Gate(ctx, w, false);
            if (g != null) { ToolOutcome d = ToolOutcome.Fail(g); d.Denied = g.StartsWith("The user"); return d; }
            string list = all.Count > 1 ? "Windows you can use:\n" + AppControl.ListText(all) : null;
            string wait = Waiting(ctx, w);
            if (wait != null) list = (list != null ? list : "") + wait;
            string text = Read(ctx, w, State(ctx), list);
            ToolOutcome o = ToolOutcome.Ok(text, w.Process + " window");
            o.Untrusted = true;
            return o;
        }
    }

    class AppScreenshotTool : AppTool
    {
        public override string Name { get { return "app_screenshot"; } }
        public override string Description { get { return "Take a screenshot of a window of a desktop program you started, to check how it looks. Positions in the image can be clicked with app_click x/y. When something in it is too small to read, call again with zoom."; } }
        public override Dictionary<string, object> Schema
        {
            get
            {
                return Obj("window", WindowArg(), false,
                           "zoom", P("string", "An area of the last screenshot to see enlarged, as x,y,width,height in its pixels, such as \"400,300,500,250\". Clicks keep using positions from the full screenshot."), false);
            }
        }
        public override string Label(Dictionary<string, object> a) { return AppZoom.Asked(a) ? "Look closer at the app" : "Screenshot the app"; }
        public override bool Untrusted { get { return true; } }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            string err;
            AppWindow w = Find(ctx, a, out err);
            if (w == null) return ToolOutcome.Fail(err);
            string g = Gate(ctx, w, false);
            if (g != null) { ToolOutcome d = ToolOutcome.Fail(g); d.Denied = g.StartsWith("The user"); return d; }
            ImageData img;
            int iw, ih;
            int[] box = null;
            if (AppZoom.Asked(a))
            {
                string wrong = AppZoom.Parse(a["zoom"], out box);
                if (wrong != null) return ToolOutcome.Fail(wrong);
                AppSession last = State(ctx);
                if (last.ShotWindow != w.Hwnd || last.ShotScale <= 0) return ToolOutcome.Fail("Take a full app_screenshot of this window first: zoom is an area of that picture.");
                wrong = AppZoom.Fit(box, last.ShotW, last.ShotH);
                if (wrong != null) return ToolOutcome.Fail(wrong);
            }
            Bitmap shot = AppControl.Capture(w);
            if (shot == null) return ToolOutcome.Fail("the window did not respond to the screenshot (the program may be busy or frozen)");
            if (box != null)
            {
                using (Bitmap whole = shot)
                {
                    double times;
                    using (Bitmap near = AppZoom.Cut(whole, box, State(ctx).ShotScale, out times))
                    {
                        img = ImageTools.Encode(near, AppZoom.MaxSide);
                        string waiting = Waiting(ctx, w);
                        ToolOutcome z = ToolOutcome.Ok("Close-up of \"" + w.Title + "\" (" + w.Process + "): the area " + box[2] + "x" + box[3] + " at " + box[0] + "," + box[1] + " of the screenshot, shown " + times.ToString("0.#", CultureInfo.InvariantCulture) +
                            " times as large. For app_click, keep using positions from the full screenshot: a point at px,py in this picture is at " + box[0] + " + px / " + times.ToString("0.###", CultureInfo.InvariantCulture) + ", " + box[1] + " + py / " + times.ToString("0.###", CultureInfo.InvariantCulture) + " there." +
                            (waiting != null ? "\n" + waiting : ""), "Close-up");
                        z.Images = new List<ImageData>(new ImageData[] { img });
                        z.Untrusted = true;
                        return z;
                    }
                }
            }
            using (Bitmap bm = shot)
            {
                const int maxSide = 1366;
                double sc = Math.Min(1.0, (double)maxSide / Math.Max(bm.Width, bm.Height));
                iw = Math.Max(1, (int)(bm.Width * sc));
                ih = Math.Max(1, (int)(bm.Height * sc));
                img = ImageTools.Encode(bm, maxSide);
                AppSession st = State(ctx);
                st.ShotWindow = w.Hwnd;
                st.ShotScale = (double)bm.Width / iw;
                st.ShotW = iw;
                st.ShotH = ih;
            }
            string wait = Waiting(ctx, w);
            ToolOutcome o = ToolOutcome.Ok("Screenshot of \"" + w.Title + "\" (" + w.Process + "), " + iw + "x" + ih + " pixels. app_click x/y uses these pixel positions (0,0 is the window's top-left corner, including its title bar)." + (wait != null ? "\n" + wait : ""), iw + "x" + ih);
            o.Images = new List<ImageData>(new ImageData[] { img });
            o.Untrusted = true;
            return o;
        }
    }

    static class AppZoom
    {
        public const int MaxSide = 1366;

        public static bool Asked(Dictionary<string, object> a)
        {
            object v;
            if (a == null || !a.TryGetValue("zoom", out v) || v == null) return false;
            string s = v as string;
            return s == null || s.Trim().Length > 0;
        }

        public static string Parse(object value, out int[] box)
        {
            box = null;
            List<double> nums = new List<double>();
            List<object> list = value as List<object>;
            Dictionary<string, object> named = value as Dictionary<string, object>;
            if (named != null)
            {
                foreach (string[] keys in new string[][] { new string[] { "x", "left" }, new string[] { "y", "top" }, new string[] { "width", "w" }, new string[] { "height", "h" } })
                    foreach (string k in keys)
                        if (named.ContainsKey(k)) { nums.Add(Json.Num(named, k)); break; }
            }
            else if (list != null)
            {
                foreach (object o in list)
                {
                    double d;
                    if (double.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) nums.Add(d);
                }
            }
            else
            {
                foreach (string part in Convert.ToString(value, CultureInfo.InvariantCulture).Split(new char[] { ',', ';', ' ', 'x', 'X', '[', ']', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    double d;
                    if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) nums.Add(d);
                    else { nums.Clear(); break; }
                }
            }
            if (nums.Count != 4) return "zoom is four numbers: x,y,width,height of the area in the last screenshot, such as \"400,300,500,250\".";
            foreach (double d in nums) if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 100000) return "zoom is four numbers: x,y,width,height of the area in the last screenshot, such as \"400,300,500,250\".";
            box = new int[] { (int)Math.Round(nums[0]), (int)Math.Round(nums[1]), (int)Math.Round(nums[2]), (int)Math.Round(nums[3]) };
            return null;
        }

        public static string Fit(int[] box, int shotW, int shotH)
        {
            if (box[2] < 1 || box[3] < 1) return "zoom needs a width and a height above zero.";
            int left = Math.Max(0, box[0]), top = Math.Max(0, box[1]);
            int right = Math.Min(shotW, box[0] + box[2]), bottom = Math.Min(shotH, box[1] + box[3]);
            if (right - left < 8 || bottom - top < 8) return "That area is outside the screenshot, which is " + shotW + "x" + shotH + " pixels, or smaller than 8 pixels.";
            box[0] = left;
            box[1] = top;
            box[2] = right - left;
            box[3] = bottom - top;
            return null;
        }

        public static Rectangle Source(int[] box, double shotScale, int fullW, int fullH)
        {
            int x = Math.Max(0, Math.Min(fullW - 1, (int)Math.Floor(box[0] * shotScale)));
            int y = Math.Max(0, Math.Min(fullH - 1, (int)Math.Floor(box[1] * shotScale)));
            int r = Math.Max(x + 1, Math.Min(fullW, (int)Math.Ceiling((box[0] + box[2]) * shotScale)));
            int b = Math.Max(y + 1, Math.Min(fullH, (int)Math.Ceiling((box[1] + box[3]) * shotScale)));
            return new Rectangle(x, y, r - x, b - y);
        }

        public static double Grow(int w, int h)
        {
            double room = (double)MaxSide / Math.Max(1, Math.Max(w, h));
            return Math.Max(1.0, Math.Min(3.0, room));
        }

        public static Bitmap Cut(Bitmap whole, int[] box, double shotScale, out double times)
        {
            Rectangle src = Source(box, shotScale, whole.Width, whole.Height);
            double grow = Grow(src.Width, src.Height);
            double shrink = Math.Min(1.0, (double)MaxSide / Math.Max(src.Width, src.Height));
            double k = grow > 1.0 ? grow : shrink;
            int w = Math.Max(1, (int)Math.Round(src.Width * k)), h = Math.Max(1, (int)Math.Round(src.Height * k));
            Bitmap near = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(near))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(whole, new Rectangle(0, 0, w, h), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
            }
            times = (double)w / Math.Max(1, box[2]);
            return near;
        }
    }

    class AppClickTool : AppTool
    {
        public override string Name { get { return "app_click"; } }
        public override string Description
        {
            get
            {
                return "Click in a desktop program you started: a control by its ref from app_read (e.g. a12), or a pixel position x,y from app_screenshot. " +
                       "Optional: button right or middle, double=true (or clicks=3), modifiers such as \"ctrl\" or \"shift\" held during the click, hold_ms to press and hold. Returns the window's controls afterwards.";
            }
        }
        public override Dictionary<string, object> Schema
        {
            get
            {
                Dictionary<string, object> button = P("string", "left (default), right or middle");
                button["enum"] = Json.A("left", "right", "middle");
                return Obj("ref", P("string", "Control ref from app_read, e.g. a12"), false,
                           "x", P("integer", "X position in the screenshot (only when there is no ref)"), false,
                           "y", P("integer", "Y position in the screenshot (only when there is no ref)"), false,
                           "button", button, false,
                           "double", P("boolean", "Double-click"), false,
                           "clicks", P("integer", "Number of clicks (1-3)"), false,
                           "modifiers", P("string", "Keys held during the click: ctrl, shift, alt, or combined like \"ctrl+shift\""), false,
                           "hold_ms", P("integer", "Hold the button down this many milliseconds (up to 15000) before releasing"), false,
                           "window", WindowArg(), false);
            }
        }
        public override string Label(Dictionary<string, object> a)
        {
            string r = S(a, "ref");
            string verb = B(a, "double", false) || I(a, "clicks", 1) == 2 ? "Double-click " : (S(a, "button") ?? "").StartsWith("right", StringComparison.OrdinalIgnoreCase) || B(a, "right", false) ? "Right-click " : "Click ";
            return verb + (string.IsNullOrEmpty(r) ? "at " + S(a, "x") + "," + S(a, "y") : r) + " in the app";
        }
        public override bool Untrusted { get { return true; } }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            string merr;
            MouseSpec mouse = MouseSpec.From(S(a, "button"), B(a, "right", false), B(a, "double", false), I(a, "clicks", 1), S(a, "modifiers"), I(a, "hold_ms", 0), out merr);
            if (mouse == null) return ToolOutcome.Fail(merr);
            string err;
            AppWindow w = Find(ctx, a, out err);
            if (w == null) return ToolOutcome.Fail(err);
            AppSession st = State(ctx);
            string r = (S(a, "ref") ?? "").Trim();
            AppElement el = null;
            if (r.Length > 0)
            {
                if (!st.Refs.TryGetValue(r, out el)) return ToolOutcome.Fail(r + " is not a known ref. Call app_read first (refs change when the window changes).");
                string rwhy;
                w = RefWindow(ctx, a, w, st, out rwhy, "ref");
                if (w == null) return ToolOutcome.Fail(rwhy);
            }
            else if (a == null || !a.ContainsKey("x") || !a.ContainsKey("y")) return ToolOutcome.Fail("give a ref from app_read, or x and y from app_screenshot");
            string waiting = Waiting(ctx, w);
            if (waiting != null) return ToolOutcome.Fail(waiting);
            bool leftOnly = mouse.Button == "left" && !mouse.Ctrl && !mouse.Shift && !mouse.Alt && mouse.HoldMs == 0;
            string g = Gate(ctx, w, true, false, leftOnly ? "click" : "click+");
            if (g != null) return Denied(g);
            if (w.Terminal && mouse.Button != "left") return ToolOutcome.Fail("in a terminal, right and middle clicks paste the clipboard, so the agent can't use them. Type with app_type instead.");
            if (w.Terminal && el != null && ClipboardGuard.PasteLabel(el.Name)) return ToolOutcome.Fail("in a terminal, type the text with app_type instead of pasting it, so MinGent can check each command before it runs");
            TerminalState term = AppControl.TerminalOf(w);
            if (term != null)
            {
                string deny = TerminalEnter(ctx, w, term, "click");
                if (deny != null) return Denied(deny);
                lock (term) { term.Clicked = true; term.Lost("a click"); }
            }
            if (el != null && ClipboardGuard.PasteLabel(el.Name))
            {
                string pg = PasteGate(ctx, w);
                if (pg != null) return Denied(pg);
            }
            bool plain = mouse.Button == "left" && mouse.Clicks == 1 && !mouse.Ctrl && !mouse.Shift && !mouse.Alt && mouse.HoldMs == 0;
            string how;
            if (el != null && plain && AppControl.Actionable(el.Role) && el.Role != 0x2A)
            {
                int outcome = 0;
                AppElement target = el;
                AppControl.RunTimed(delegate
                {
                    try { target.Acc.accDoDefaultAction(target.Child); outcome = 1; }
                    catch { outcome = 2; }
                }, 5000);
                if (outcome == 2)
                {
                    string f = AppControl.FocusWhenIdle(w, ctx.Ct);
                    if (f != null) return ToolOutcome.Fail(f);
                    AppControl.Click(el.Rect.X + el.Rect.Width / 2, el.Rect.Y + el.Rect.Height / 2, mouse);
                }
                how = "Clicked " + r + (el.Name.Length > 0 ? " (" + el.Name + ")" : "");
                if (outcome == 0) how += "; the program is still busy with the click, probably showing a dialog";
            }
            else
            {
                string f = AppControl.FocusWhenIdle(w, ctx.Ct);
                if (f != null) return ToolOutcome.Fail(f);
                Point pt;
                string what;
                string perr = ScreenPoint(a, w, st, "ref", "x", "y", true, out pt, out what);
                if (perr != null) return ToolOutcome.Fail(perr);
                AppControl.Click(pt.X, pt.Y, mouse);
                how = mouse.Describe() + " " + what;
            }
            return After(ctx, w, mouse.HoldMs > 0 ? 300 : 500, how);
        }
    }

    class AppTypeTool : AppTool
    {
        public override string Name { get { return "app_type"; } }
        protected override string Act { get { return "type"; } }
        public override string Description { get { return "Type text into a desktop program you started, like a person at the keyboard. Give a ref from app_read to click into that field first (otherwise it types where the cursor is). Set enter=true to press Enter afterwards. It also types into a game's chat or command box once that is open."; } }
        public override Dictionary<string, object> Schema
        {
            get
            {
                return Obj("text", P("string", "Text to type"), true,
                           "ref", P("string", "Field ref from app_read, e.g. a7"), false,
                           "enter", P("boolean", "Press Enter after typing"), false,
                           "description", P("string", "In a terminal, when this runs a command: a few plain words saying what it does, shown to the user when MinGent asks first, e.g. \"Restart the web server\""), false,
                           "window", WindowArg(), false);
            }
        }
        public override string Label(Dictionary<string, object> a) { return "Type into the app" + (string.IsNullOrEmpty(S(a, "ref")) ? "" : " (" + S(a, "ref") + ")"); }
        public override bool Untrusted { get { return true; } }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            string text = S(a, "text") ?? "";
            if (text.Length > 4000) return ToolOutcome.Fail("type at most 4000 characters at a time");
            string err;
            AppWindow w = Find(ctx, a, out err);
            if (w == null) return ToolOutcome.Fail(err);
            AppSession st = State(ctx);
            string r = (S(a, "ref") ?? "").Trim();
            AppElement el = null;
            if (r.Length > 0 && !st.Refs.TryGetValue(r, out el)) return ToolOutcome.Fail(r + " is not a known ref. Call app_read first.");
            string rerr;
            w = RefWindow(ctx, a, w, st, out rerr, "ref");
            if (w == null) return ToolOutcome.Fail(rerr);
            string waiting = Waiting(ctx, w);
            if (waiting != null) return ToolOutcome.Fail(waiting);
            string g = Gate(ctx, w, true);
            if (g != null) { ToolOutcome d = ToolOutcome.Fail(g); d.Denied = g.StartsWith("The user"); return d; }
            if (el != null && AppControl.IsPassword(el))
            {
                PermissionAsk ask = new PermissionAsk();
                ask.Kind = "app";
                ask.Category = ctx.Guarded(PermissionCategory.BrowserSensitive);
                ask.High = true;
                ask.AllowRemember = false;
                ask.Title = "Type into a password field";
                ask.Detail = w.Process + ": \"" + w.Title + "\"";
                ask.Reason = "This field is marked as a password field.";
                if (!ctx.Approve(ask)) { ToolOutcome d = ToolOutcome.Fail("The user did not allow typing into this password field."); d.Denied = true; return d; }
            }
            if (el != null && w.Terminal)
            {
                TerminalState tt = AppControl.TerminalOf(w);
                if (tt != null)
                {
                    string deny = TerminalEnter(ctx, w, tt, "click");
                    if (deny != null) return Denied(deny);
                    lock (tt) { tt.Clicked = true; tt.Lost("a click"); }
                }
            }
            string f = AppControl.FocusWhenIdle(w, ctx.Ct);
            if (f != null) return ToolOutcome.Fail(f);
            if (el != null)
            {
                bool focused = false;
                AppElement target = el;
                AppControl.RunTimed(delegate { target.Acc.accSelect(1, target.Child); focused = true; }, 4000);
                if (!focused || el.Rect.Width > 0) AppControl.Click(el.Rect.X + el.Rect.Width / 2, el.Rect.Y + el.Rect.Height / 2, false, false);
                Thread.Sleep(80);
                if (AppControl.IsTextField(el) && AppControl.IsFileDialog(w))
                {
                    if (!AppControl.StaysFocused(w)) return ToolOutcome.Fail("typing stopped because another window came to the front");
                    AppControl.Key(0x24, null);
                    AppControl.Key(0x23, new List<ushort>(new ushort[] { 0x10 }));
                }
            }
            if (w.Terminal) return TerminalType(ctx, w, text, B(a, "enter", false), S(a, "description"));
            string terr = AppControl.Type(w, text);
            if (terr != null) return ToolOutcome.Fail(terr);
            if (B(a, "enter", false))
            {
                string d = AppControl.NewDialog(w);
                if (d != null) return ToolOutcome.Fail(d.Replace("typing stopped", "Enter was not pressed"));
                if (!AppControl.StaysFocused(w)) return ToolOutcome.Fail("Enter was not pressed because another window came to the front");
                AppControl.Key(0x0D, null);
            }
            string note;
            AppWindow now = Settle(ctx, w, 400, out note);
            string summary = "Typed " + text.Length + " characters" + (B(a, "enter", false) ? " and pressed Enter" : "");
            if (now == null) return ToolOutcome.Ok(summary + ". The program has no open windows now.", summary);
            ToolOutcome o = ToolOutcome.Ok(Read(ctx, now, st, summary + "." + (note != null ? " " + note : "")), summary);
            o.Untrusted = true;
            return o;
        }
    }

    class AppKeyTool : AppTool
    {
        public override string Name { get { return "app_key"; } }
        protected override string Act { get { return "key"; } }
        public override string Description
        {
            get
            {
                return "Press keys in a desktop program you started, like a person at the keyboard. Any key works: Enter, Tab, Escape, Backspace, Delete, Insert, Space, arrows, PageUp/PageDown, Home, End, " +
                       "F1-F24, Numpad keys, ContextMenu, Alt (menu bar) or a single character, with Ctrl+, Shift+ and Alt+ for shortcuts (Ctrl+S, Ctrl+Shift+Z, Alt+F4). Keys joined with + go down together (Shift+W, W+D). " +
                       "Several keys separated by spaces are pressed in order (\"Tab Tab Enter\"); repeat presses the whole sequence again. hold_ms keeps the keys held down that long, for games and anything else that reacts to a held key. " +
                       "To type words or a command, use app_type, not one key per letter; in a game, first open its chat or command box with the game's key for it, often T or /.";
            }
        }
        public override Dictionary<string, object> Schema
        {
            get
            {
                return Obj("key", P("string", "Key, shortcut or space-separated sequence"), true,
                           "repeat", P("integer", "How many times to press it (default 1)"), false,
                           "hold_ms", P("integer", "Hold the keys down this many milliseconds before letting go (at most 10000)"), false,
                           "description", P("string", "In a terminal, when Enter runs the command on the line: a few plain words saying what it does, shown to the user when MinGent asks first"), false,
                           "window", WindowArg(), false);
            }
        }
        public override string Label(Dictionary<string, object> a) { int n = I(a, "repeat", 1), h = I(a, "hold_ms", 0); return (h > 0 ? "Hold " : "Press ") + S(a, "key") + (h > 0 ? " for " + (h % 1000 == 0 ? (h / 1000) + " s" : h + " ms") : "") + (n > 1 ? " x" + n : "") + " in the app"; }
        public override bool Untrusted { get { return true; } }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            string perr;
            int hold = I(a, "hold_ms", 0);
            List<KeyPress> keys = KeyCombo.Sequence(S(a, "key"), I(a, "repeat", 1), hold > 0, out perr);
            if (keys == null) return ToolOutcome.Fail(KeyCombo.TypeInstead(perr, S(a, "key"), "app_type", true));
            string herr = KeyCombo.HoldError(keys, hold);
            if (herr != null) return ToolOutcome.Fail(herr);
            bool escOnly = hold <= 0, paste = false, copy = false;
            foreach (KeyPress k in keys)
            {
                string sys = null;
                foreach (KeyPress x in KeyCombo.Parts(k)) if (sys == null) sys = AppControl.SystemKey(x);
                if (sys != null) return ToolOutcome.Fail(sys);
                if (!(k.Vk == 27 && !k.Ctrl && !k.Alt && !k.Shift && k.With == null)) escOnly = false;
                if (ClipboardGuard.IsPaste(k)) paste = true;
                if (ClipboardGuard.IsCopy(k)) copy = true;
            }
            string err;
            AppWindow w = Find(ctx, a, out err);
            if (w == null) return ToolOutcome.Fail(err);
            string waiting = Waiting(ctx, w);
            if (waiting != null) return ToolOutcome.Fail(waiting);
            string g = Gate(ctx, w, true, escOnly);
            if (g != null) return Denied(g);
            if (paste && w.Terminal) return ToolOutcome.Fail("in a terminal, type the text with app_type instead of pasting it, so MinGent can check each command before it runs");
            if (paste)
            {
                string pg = PasteGate(ctx, w);
                if (pg != null) return Denied(pg);
            }
            if (w.Terminal)
            {
                if (hold > 0) return ToolOutcome.Fail("hold_ms doesn't work in a terminal; press the keys without it");
                uint was = copy ? ClipboardGuard.Now() : 0;
                ToolOutcome to = TerminalKeys(ctx, w, keys, S(a, "description"));
                if (copy) ClipboardGuard.AfterCopy(was);
                return to;
            }
            string f = AppControl.FocusWhenIdle(w, ctx.Ct);
            if (f != null) return ToolOutcome.Fail(f);
            uint before = copy ? ClipboardGuard.Now() : 0;
            string kerr = AppControl.Press(w, keys, hold, ctx.Ct);
            if (kerr != null) return ToolOutcome.Fail(kerr);
            string how = KeyCombo.Describe(keys, hold);
            ToolOutcome o = After(ctx, w, 400, how);
            if (copy) ClipboardGuard.AfterCopy(before);
            return o;
        }
    }

    class AppScrollTool : AppTool
    {
        public override string Name { get { return "app_scroll"; } }
        protected override string Act { get { return "scroll"; } }
        public override string Description
        {
            get
            {
                return "Turn the mouse wheel in a desktop program you started: up, down, left or right. To scroll a particular list, document or panel, give its ref from app_read or x/y from app_screenshot " +
                       "(default: the middle of the window). notches is how far (default 5; one notch is usually 3 lines).";
            }
        }
        public override Dictionary<string, object> Schema
        {
            get
            {
                Dictionary<string, object> d = P("string", "up, down, left or right");
                d["enum"] = Json.A("up", "down", "left", "right");
                return Obj("direction", d, true,
                           "notches", P("integer", "How far: mouse-wheel notches, 1-50 (default 5)"), false,
                           "ref", P("string", "Ref of the list or panel to scroll, from app_read"), false,
                           "x", P("integer", "X position in the screenshot to scroll at"), false,
                           "y", P("integer", "Y position in the screenshot to scroll at"), false,
                           "window", WindowArg(), false);
            }
        }
        public override string Label(Dictionary<string, object> a) { return "Scroll " + (S(a, "direction") ?? "down") + " in the app"; }
        public override bool Untrusted { get { return true; } }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            string dir = (S(a, "direction") ?? "down").Trim().ToLowerInvariant();
            if (dir != "up" && dir != "down" && dir != "left" && dir != "right") return ToolOutcome.Fail("direction must be up, down, left or right");
            int n = Math.Max(1, Math.Min(50, I(a, "notches", 5)));
            string err;
            AppWindow w = Find(ctx, a, out err);
            if (w == null) return ToolOutcome.Fail(err);
            AppSession st = State(ctx);
            string rwhy;
            w = RefWindow(ctx, a, w, st, out rwhy, "ref");
            if (w == null) return ToolOutcome.Fail(rwhy);
            string waiting = Waiting(ctx, w);
            if (waiting != null) return ToolOutcome.Fail(waiting);
            string g = Gate(ctx, w, true);
            if (g != null) return Denied(g);
            string f = AppControl.FocusWhenIdle(w, ctx.Ct);
            if (f != null) return ToolOutcome.Fail(f);
            Point pt;
            string what;
            string perr = ScreenPoint(a, w, st, "ref", "x", "y", false, out pt, out what);
            if (perr != null) return ToolOutcome.Fail(perr);
            bool horizontal = dir == "left" || dir == "right";
            int sign = dir == "up" || dir == "right" ? 1 : -1;
            AppControl.Wheel(pt.X, pt.Y, horizontal, sign * n);
            return After(ctx, w, 300, "Scrolled " + dir + " " + n + " notch" + (n == 1 ? "" : "es") + " " + what);
        }
    }

    class AppHoverTool : AppTool
    {
        public override string Name { get { return "app_hover"; } }
        protected override string Act { get { return "hover"; } }
        public override string Description
        {
            get
            {
                return "Move the mouse over a control (ref from app_read) or a position (x/y from app_screenshot) in a desktop program you started, without clicking - to show a tooltip or open a hover menu. " +
                       "For games: dx/dy move the mouse by that much from wherever it is, like a hand moving a real mouse, which is how 3D games turn the view, so use dx/dy to look around in them. " +
                       "How far the view turns depends on the game; try about 100 first and check with app_screenshot. In other programs use ref or x/y, because dx/dy don't match screenshot pixels.";
            }
        }
        public override Dictionary<string, object> Schema
        {
            get
            {
                return Obj("ref", P("string", "Control ref from app_read, e.g. a12"), false,
                           "x", P("integer", "X position in the screenshot (only when there is no ref)"), false,
                           "y", P("integer", "Y position in the screenshot (only when there is no ref)"), false,
                           "dx", P("integer", "For games, instead of ref or x/y: move the mouse right by this much, or left if negative (-5000 to 5000)"), false,
                           "dy", P("integer", "For games, instead of ref or x/y: move the mouse down by this much, or up if negative (-5000 to 5000)"), false,
                           "window", WindowArg(), false);
            }
        }
        public override string Label(Dictionary<string, object> a)
        {
            int dx = (int)Math.Round(By(a, "dx")), dy = (int)Math.Round(By(a, "dy"));
            if (dx != 0 || dy != 0) return "Move the mouse by " + dx + "," + dy + " in the app";
            string r = S(a, "ref");
            return "Hover " + (string.IsNullOrEmpty(r) ? "at " + S(a, "x") + "," + S(a, "y") : "over " + r) + " in the app";
        }
        public override bool Untrusted { get { return true; } }

        static bool Given(Dictionary<string, object> a, string k)
        {
            object v;
            return a != null && a.TryGetValue(k, out v) && v != null && !(v is string && ((string)v).Trim().Length == 0);
        }

        static double By(Dictionary<string, object> a, string k)
        {
            double v = D(a, k, 0);
            return double.IsNaN(v) ? 0 : Math.Max(-1e6, Math.Min(1e6, v));
        }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            double rx = By(a, "dx"), ry = By(a, "dy");
            if (Math.Abs(rx) > 5000 || Math.Abs(ry) > 5000) return ToolOutcome.Fail("dx and dy must be between -5000 and 5000; to move further, call app_hover again");
            int dx = (int)Math.Round(rx), dy = (int)Math.Round(ry);
            bool by = dx != 0 || dy != 0;
            bool to = Given(a, "ref") || Given(a, "x") || Given(a, "y");
            if (by && to) return ToolOutcome.Fail("give either dx/dy to move the mouse from where it is, or a ref or x/y to move it to a place, not both");
            if (!by && !to && (Given(a, "dx") || Given(a, "dy"))) return ToolOutcome.Fail("dx and dy are both 0, so there is nothing to move");
            string err;
            AppWindow w = Find(ctx, a, out err);
            if (w == null) return ToolOutcome.Fail(err);
            AppSession st = State(ctx);
            string rwhy;
            w = RefWindow(ctx, a, w, st, out rwhy, "ref");
            if (w == null) return ToolOutcome.Fail(rwhy);
            string waiting = Waiting(ctx, w);
            if (waiting != null) return ToolOutcome.Fail(waiting);
            string g = Gate(ctx, w, true);
            if (g != null) return Denied(g);
            string f = AppControl.FocusWhenIdle(w, ctx.Ct);
            if (f != null) return ToolOutcome.Fail(f);
            if (by)
            {
                string merr = AppControl.Move(w, dx, dy, ctx.Ct);
                if (merr != null) return ToolOutcome.Fail(merr);
                return After(ctx, w, 700, "Moved the mouse by " + dx + "," + dy);
            }
            Point pt;
            string what;
            string perr = ScreenPoint(a, w, st, "ref", "x", "y", true, out pt, out what);
            if (perr != null) return ToolOutcome.Fail(perr);
            AppControl.Hover(pt.X, pt.Y);
            return After(ctx, w, 700, "Moved the mouse " + (what.StartsWith("at ", StringComparison.Ordinal) ? "to " + what.Substring(3) : "over " + what));
        }
    }

    class AppDragTool : AppTool
    {
        public override string Name { get { return "app_drag"; } }
        protected override string Act { get { return "drag"; } }
        public override string Description
        {
            get
            {
                return "Drag with the left mouse button in a desktop program you started: from a ref (or x/y) to another ref (or to_x/to_y). Positions are from app_screenshot and stay inside the window. " +
                       "Use it for sliders, moving items, resizing panes, drawing or selecting text.";
            }
        }
        public override Dictionary<string, object> Schema
        {
            get
            {
                return Obj("ref", P("string", "Where to start: control ref from app_read"), false,
                           "x", P("integer", "Where to start: x in the screenshot"), false,
                           "y", P("integer", "Where to start: y in the screenshot"), false,
                           "to_ref", P("string", "Where to drop: control ref from app_read"), false,
                           "to_x", P("integer", "Where to drop: x in the screenshot"), false,
                           "to_y", P("integer", "Where to drop: y in the screenshot"), false,
                           "window", WindowArg(), false);
            }
        }
        public override string Label(Dictionary<string, object> a) { return "Drag in the app"; }
        public override bool Untrusted { get { return true; } }

        public override ToolOutcome Run(ToolContext ctx, Dictionary<string, object> a)
        {
            string err;
            AppWindow w = Find(ctx, a, out err);
            if (w == null) return ToolOutcome.Fail(err);
            AppSession st = State(ctx);
            string rwhy;
            w = RefWindow(ctx, a, w, st, out rwhy, "ref", "to_ref");
            if (w == null) return ToolOutcome.Fail(rwhy);
            string waiting = Waiting(ctx, w);
            if (waiting != null) return ToolOutcome.Fail(waiting);
            string g = Gate(ctx, w, true);
            if (g != null) return Denied(g);
            TerminalState term = AppControl.TerminalOf(w);
            if (term != null)
            {
                string deny = TerminalEnter(ctx, w, term, "drag");
                if (deny != null) return Denied(deny);
                lock (term) { term.Clicked = true; term.Lost("dragging"); }
            }
            string f = AppControl.FocusWhenIdle(w, ctx.Ct);
            if (f != null) return ToolOutcome.Fail(f);
            Point from, to;
            string fromWhat, toWhat;
            string e1 = ScreenPoint(a, w, st, "ref", "x", "y", true, out from, out fromWhat);
            if (e1 != null) return ToolOutcome.Fail(e1);
            string e2 = ScreenPoint(a, w, st, "to_ref", "to_x", "to_y", true, out to, out toWhat);
            if (e2 != null) return ToolOutcome.Fail(e2);
            AppControl.Drag(from.X, from.Y, to.X, to.Y);
            return After(ctx, w, 400, "Dragged from " + (fromWhat.StartsWith("at ", StringComparison.Ordinal) ? fromWhat.Substring(3) : fromWhat) + " to " + (toWhat.StartsWith("at ", StringComparison.Ordinal) ? toWhat.Substring(3) : toWhat));
        }
    }
}
