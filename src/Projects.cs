using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MinGent
{
    static class WorkFolders
    {
        public static string Clean(string folder)
        {
            if (string.IsNullOrEmpty(folder) || folder.Trim().Length == 0) return null;
            try
            {
                string f = Path.GetFullPath(folder.Trim());
                string cut = f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (cut.Length == 0 || (cut.Length == 2 && cut[1] == ':')) return f;
                return cut;
            }
            catch { return null; }
        }

        public static bool Same(string a, string b)
        {
            string x = Clean(a), y = Clean(b);
            return x != null && y != null && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public static string Name(string folder)
        {
            string f = Clean(folder);
            if (f == null) return "";
            string n = Path.GetFileName(f);
            return string.IsNullOrEmpty(n) ? f : n;
        }

        public static string Check(string folder)
        {
            string f = Clean(folder);
            if (f == null) return "MinGent can't use that as a folder.";
            bool there;
            try { there = Directory.Exists(f); } catch { there = false; }
            if (!there) return "That folder doesn't exist.";
            string broad = null;
            try { broad = Sandbox.Personal(f); } catch { broad = "it can't be checked"; }
            if (broad != null) return "The agent changes files in its work folder without asking each time, so MinGent doesn't take this folder: " + broad + (broad.StartsWith("it is a folder where programs", StringComparison.Ordinal) ? ". Choose another folder." : ". Choose a folder inside it.");
            if (AgentPaths.InProgram(f)) return "That folder holds MinGent itself, so it can't be a work folder. Its Files folder can.";
            if (AgentPaths.WriteBlocked(Path.Combine(f, "x.txt")) != null) return "The agent is not allowed to write in that folder, so it can't be a work folder.";
            return null;
        }

        static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

        public static string SafeName(string title)
        {
            StringBuilder sb = new StringBuilder();
            bool space = false;
            foreach (char ch in title ?? "")
            {
                bool bad = ch < 32 || ch == 127 || "<>:\"/\\|?*".IndexOf(ch) >= 0 || char.IsSurrogate(ch);
                if (bad || char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(ch);
                if (sb.Length >= 60) break;
            }
            string s = sb.ToString().Trim().TrimEnd('.', ' ', '\u2026').TrimStart('.', ' ');
            if (s.Length == 0) s = "Chat";
            foreach (string r in Reserved)
                if (string.Equals(s, r, StringComparison.OrdinalIgnoreCase) || s.StartsWith(r + ".", StringComparison.OrdinalIgnoreCase)) { s = s + " chat"; break; }
            return s;
        }

        public static string Unique(string parent, string name, ICollection<string> taken)
        {
            for (int n = 1; n < 10000; n++)
            {
                string candidate = Path.Combine(parent, n == 1 ? name : name + " " + n);
                bool used = false;
                try { used = Directory.Exists(candidate) || File.Exists(candidate); } catch { used = true; }
                if (!used && taken != null) foreach (string t in taken) if (Same(t, candidate)) { used = true; break; }
                if (!used) return candidate;
            }
            return Path.Combine(parent, name + " " + Guid.NewGuid().ToString("N").Substring(0, 8));
        }

        public const string TasksName = "Scheduled tasks";

        public static string TaskFolder(string workRoot, string taskName)
        {
            return Path.Combine(Path.Combine(workRoot, TasksName), SafeName(taskName));
        }

        public static List<string> Kept(string workRoot)
        {
            List<string> l = new List<string>();
            string skills = Skills.Folder(workRoot);
            if (skills != null) l.Add(skills);
            l.Add(Path.Combine(workRoot, TasksName));
            return l;
        }

        public static bool IsOwnFolder(string folder, string workRoot)
        {
            string f = Clean(folder), root = Clean(workRoot);
            if (f == null || root == null || Same(f, root)) return false;
            string parent;
            try { parent = Path.GetDirectoryName(f); } catch { return false; }
            if (!Same(parent, root)) return false;
            foreach (string k in Kept(root)) if (Same(f, k)) return false;
            return true;
        }

        static bool Walk(string dir, Action<string> file)
        {
            foreach (string f in Directory.GetFiles(dir)) file(f);
            foreach (string d in Directory.GetDirectories(dir))
            {
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) { file(d); continue; }
                Walk(d, file);
            }
            return true;
        }

        public static int CountFiles(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return 0;
                int n = 0;
                Walk(folder, delegate (string f) { n++; });
                return n;
            }
            catch { return -1; }
        }

        public static bool Delete(string folder, string workRoot)
        {
            if (!IsOwnFolder(folder, workRoot)) return false;
            try
            {
                if (!Directory.Exists(folder)) return true;
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(folder, false); return true; }
                Walk(folder, delegate (string f)
                {
                    try { if (File.Exists(f)) File.SetAttributes(f, FileAttributes.Normal); } catch { }
                });
                Directory.Delete(folder, true);
                return !Directory.Exists(folder);
            }
            catch { return false; }
        }
    }
}
