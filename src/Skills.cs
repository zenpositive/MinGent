using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MinGent
{
    class Skill
    {
        public string Name = "", About = "", Path = "", File = "";
    }

    static class Skills
    {
        public const int Most = 40;
        const int HeadBytes = 8192;
        const string FolderName = "Skills", Inner = "SKILL.md";
        const string Saving = "To save a new one when the user asks, write name.md there: first the lines \"---\", \"description: \" plus one sentence on when to use it, and \"---\", then the steps.";

        public static string Folder(string defaultWorkspace)
        {
            return string.IsNullOrEmpty(defaultWorkspace) ? null : System.IO.Path.Combine(defaultWorkspace, FolderName);
        }

        public static List<Skill> Load(string folder)
        {
            List<Skill> found = new List<Skill>();
            if (string.IsNullOrEmpty(folder)) return found;
            try
            {
                if (!Directory.Exists(folder)) return found;
                List<string> paths = new List<string>();
                foreach (string f in Directory.GetFiles(folder, "*.md")) paths.Add(f);
                foreach (string d in Directory.GetDirectories(folder))
                {
                    string inner = System.IO.Path.Combine(d, Inner);
                    if (System.IO.File.Exists(inner)) paths.Add(inner);
                }
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string p in paths)
                {
                    Skill s = Read(p, folder);
                    if (s != null && names.Add(s.Name)) found.Add(s);
                }
            }
            catch { }
            found.Sort(delegate (Skill a, Skill b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            if (found.Count > Most) found.RemoveRange(Most, found.Count - Most);
            return found;
        }

        static Skill Read(string path, string folder)
        {
            string head;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] buf = new byte[HeadBytes];
                    int n = fs.Read(buf, 0, buf.Length);
                    head = new UTF8Encoding(false).GetString(buf, 0, n);
                }
            }
            catch { return null; }
            if (head.Length > 0 && head[0] == '\uFEFF') head = head.Substring(1);
            bool inner = string.Equals(System.IO.Path.GetFileName(path), Inner, StringComparison.OrdinalIgnoreCase) && !string.Equals(System.IO.Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase);
            string name = inner ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) : System.IO.Path.GetFileNameWithoutExtension(path);
            string about = "";
            string[] lines = head.Replace("\r\n", "\n").Split('\n');
            int at = 0;
            if (lines.Length > 0 && lines[0].Trim() == "---")
            {
                for (at = 1; at < lines.Length && lines[at].Trim() != "---"; at++)
                {
                    string v;
                    if (Field(lines[at], "name", out v) && v.Length > 0) name = v;
                    else if (Field(lines[at], "description", out v)) about = v;
                }
                at++;
            }
            for (; about.Length == 0 && at < lines.Length; at++)
            {
                string l = lines[at].Trim();
                if (l.Length == 0 || l[0] == '#' || l == "---") continue;
                about = l.TrimStart('-', '*', '>', ' ');
                string v;
                if (Field(about, "description", out v) && v.Length > 0) about = v;
            }
            Skill s = new Skill();
            s.Name = Slug(name);
            if (s.Name.Length == 0) return null;
            s.About = OneLine(about, 160);
            s.Path = path;
            s.File = inner ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) + "\\" + Inner : System.IO.Path.GetFileName(path);
            return s;
        }

        static bool Field(string line, string key, out string value)
        {
            value = "";
            int colon = line.IndexOf(':');
            if (colon <= 0 || !string.Equals(line.Substring(0, colon).Trim(), key, StringComparison.OrdinalIgnoreCase)) return false;
            value = line.Substring(colon + 1).Trim().Trim('"', '\'').Trim();
            return true;
        }

        static string OneLine(string text, int most)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in text ?? "")
                sb.Append(char.IsControl(c) ? ' ' : c);
            string s = sb.ToString().Trim();
            return s.Length > most ? s.Substring(0, most - 3).TrimEnd() + "..." : s;
        }

        public static string Slug(string name)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if ((c == ' ' || c == '-' || c == '.') && sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            string s = sb.ToString().Trim('-');
            return s.Length > 40 ? s.Substring(0, 40).Trim('-') : s;
        }

        public static string ForPrompt(List<Skill> skills, string folder, bool small)
        {
            if (string.IsNullOrEmpty(folder) || (small && (skills == null || skills.Count == 0))) return null;
            StringBuilder sb = new StringBuilder();
            sb.Append("Skills are the user's saved how-tos for jobs that come up again, kept as Markdown files in ").Append(folder).Append(". ");
            if (skills != null && skills.Count > 0)
            {
                sb.Append("When a task matches one, read its file with read_file before you start and follow it. A message that starts with /name asks for that skill.");
                if (!small) sb.Append(' ').Append(Saving);
                sb.Append('\n');
                int most = small ? 10 : Most;
                for (int i = 0; i < skills.Count && i < most; i++)
                {
                    Skill s = skills[i];
                    sb.Append("- /").Append(s.Name).Append(" (").Append(s.File).Append(')');
                    string about = small && s.About.Length > 80 ? s.About.Substring(0, 77).TrimEnd() + "..." : s.About;
                    if (about.Length > 0) sb.Append(": ").Append(about);
                    sb.Append('\n');
                }
                if (skills.Count > most) sb.Append("- and ").Append(skills.Count - most).Append(" more in that folder\n");
            }
            else sb.Append("None are saved yet. ").Append(Saving).Append('\n');
            return sb.ToString();
        }

        public static string Template(string name)
        {
            return "---\r\nname: " + name + "\r\ndescription: Say in one sentence when the agent should use this skill.\r\n---\r\n\r\n# " + name + "\r\n\r\nWrite down how the job is done, step by step, the way you would explain it to a new colleague.\r\n\r\n1. \r\n2. \r\n3. \r\n";
        }

        public static string Create(string folder, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(folder);
                for (int i = 1; i < 200; i++)
                {
                    string name = i == 1 ? "new-skill" : "new-skill-" + i;
                    string path = System.IO.Path.Combine(folder, name + ".md");
                    if (System.IO.File.Exists(path) || Directory.Exists(System.IO.Path.Combine(folder, name))) continue;
                    System.IO.File.WriteAllText(path, Template(name), new UTF8Encoding(false));
                    return path;
                }
                error = "There are too many unnamed skills in " + folder + ". Rename or delete some first.";
            }
            catch (Exception ex) { error = "The skill file could not be created: " + ex.Message; }
            return null;
        }
    }
}
