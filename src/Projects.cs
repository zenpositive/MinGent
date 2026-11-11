using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MinGent
{
    static class Projects
    {
        public const int Most = 12;

        public static string FilePath { get { return Path.Combine(Program.DataDir, "projects.json"); } }

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

        public static int Find(List<string> list, string folder)
        {
            for (int i = 0; i < list.Count; i++) if (Same(list[i], folder)) return i;
            return -1;
        }

        public static bool Add(List<string> list, string folder)
        {
            string f = Clean(folder);
            if (f == null || Find(list, f) >= 0) return false;
            list.Insert(0, f);
            while (list.Count > Most) list.RemoveAt(list.Count - 1);
            return true;
        }

        public static bool Forget(List<string> list, string folder)
        {
            int at = Find(list, folder);
            if (at < 0) return false;
            list.RemoveAt(at);
            return true;
        }

        public static string Name(string folder)
        {
            string f = Clean(folder);
            if (f == null) return "";
            string n = Path.GetFileName(f);
            return string.IsNullOrEmpty(n) ? f : n;
        }

        static string Parent(string folder)
        {
            try
            {
                string p = Path.GetDirectoryName(Clean(folder) ?? "");
                return string.IsNullOrEmpty(p) ? "" : Name(p);
            }
            catch { return ""; }
        }

        public static List<string> Names(List<string> folders)
        {
            List<string> names = new List<string>();
            foreach (string f in folders) names.Add(Name(f));
            for (int i = 0; i < names.Count; i++)
            {
                bool twice = false;
                for (int k = 0; k < folders.Count && !twice; k++)
                    twice = k != i && string.Equals(Name(folders[k]), Name(folders[i]), StringComparison.OrdinalIgnoreCase);
                if (!twice) continue;
                string p = Parent(folders[i]);
                if (p.Length > 0) names[i] = names[i] + " (" + p + ")";
            }
            return names;
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
            if (AgentPaths.WriteBlocked(Path.Combine(f, "x.txt")) != null) return "The agent is not allowed to write in that folder, so it can't be a work folder.";
            return null;
        }

        public static List<string> Load()
        {
            List<string> list = new List<string>();
            try
            {
                if (Program.Erased || !File.Exists(FilePath)) return list;
                List<object> items = Json.List(Json.Parse(File.ReadAllText(FilePath)), "folders");
                if (items == null) return list;
                foreach (object o in items)
                {
                    string f = Clean(o as string);
                    if (f != null && Find(list, f) < 0 && list.Count < Most) list.Add(f);
                }
            }
            catch { }
            return list;
        }

        public static bool Save(List<string> list)
        {
            try
            {
                if (Program.Erased) return false;
                List<object> items = new List<object>();
                foreach (string f in list) items.Add(f);
                Directory.CreateDirectory(Program.DataDir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json.Write(Json.O("folders", items)), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
                return true;
            }
            catch { return false; }
        }
    }
}
