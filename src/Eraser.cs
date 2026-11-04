using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MinGent
{
    static class Eraser
    {
        public static List<string> Folders()
        {
            List<string> l = new List<string>();
            if (!string.IsNullOrEmpty(Program.DataDir)) l.Add(Program.DataDir);
            string legacy = null;
            try { legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.LegacyName); } catch { }
            if (legacy != null && !Same(legacy, Program.DataDir) && LooksOurs(legacy)) l.Add(legacy);
            string low = null;
            try { low = Sandbox.LowRoot; } catch { }
            if (!string.IsNullOrEmpty(low) && Directory.Exists(low)) l.Add(low);
            return l;
        }

        static bool SandboxFolder(string dir)
        {
            try { return Same(dir, Sandbox.LowRoot); }
            catch { return false; }
        }

        public static List<string> TempFolders()
        {
            List<string> l = new List<string>();
            try
            {
                string t = Path.GetTempPath();
                l.Add(Path.Combine(t, Program.AppName));
                l.Add(Path.Combine(t, Program.LegacyName));
            }
            catch { }
            return l;
        }

        public static bool LooksOurs(string dir)
        {
            try
            {
                string ini = Path.Combine(dir, "settings.ini");
                if (!File.Exists(ini) || new FileInfo(ini).Length > 1024 * 1024) return false;
                string t = File.ReadAllText(ini);
                return t.IndexOf("aiPermissions=", StringComparison.Ordinal) >= 0 && t.IndexOf("notesWidth=", StringComparison.Ordinal) >= 0;
            }
            catch { return false; }
        }

        static bool IsModelFile(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            return n.EndsWith(".gguf", StringComparison.Ordinal) || n.EndsWith(".gguf.part", StringComparison.Ordinal);
        }

        static string Norm(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd('\\', '/'); } catch { return (path ?? "").TrimEnd('\\', '/'); }
        }

        static bool Same(string a, string b) { return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase); }

        static bool Under(string path, string root)
        {
            string p = Norm(path), r = Norm(root);
            return string.Equals(p, r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static long ModelBytes(List<string> roots)
        {
            long total = 0;
            foreach (string r in roots) total += ModelBytesIn(r, 0);
            return total;
        }

        static long ModelBytesIn(string dir, int depth)
        {
            long sum = 0;
            try
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                if (!di.Exists || depth > 8 || (di.Attributes & FileAttributes.ReparsePoint) != 0) return 0;
                if (depth > 0 && di.Name.StartsWith(".", StringComparison.Ordinal)) return 0;
                foreach (FileInfo f in di.GetFiles()) if (IsModelFile(f.Name)) sum += f.Length;
                foreach (DirectoryInfo d in di.GetDirectories()) sum += ModelBytesIn(d.FullName, depth + 1);
            }
            catch { }
            return sum;
        }

        public static string OutputsFolder()
        {
            string docs = "";
            try { docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch { }
            if (string.IsNullOrEmpty(docs)) return null;
            foreach (string name in new string[] { "MinGent Files", "JustPad Files" })
            {
                string d = Path.Combine(docs, name);
                try
                {
                    if (Directory.Exists(d) && (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) return d;
                }
                catch { }
            }
            return null;
        }

        public const int CountCap = 100000;

        public static int CountFiles(string dir)
        {
            int n = 0;
            try
            {
                DirectoryInfo root = new DirectoryInfo(dir);
                if (!root.Exists) return -1;
                Stack<DirectoryInfo> todo = new Stack<DirectoryInfo>();
                todo.Push(root);
                while (todo.Count > 0 && n < CountCap)
                {
                    DirectoryInfo d = todo.Pop();
                    foreach (FileSystemInfo e in d.EnumerateFileSystemInfos())
                    {
                        DirectoryInfo sub = e as DirectoryInfo;
                        if (sub == null || (e.Attributes & FileAttributes.ReparsePoint) != 0) n++;
                        else todo.Push(sub);
                        if (n >= CountCap) break;
                    }
                }
            }
            catch { return -1; }
            return n;
        }

        public static List<string> DownloadPlaces(List<string> roots)
        {
            List<string> l = new List<string>();
            foreach (string r in roots)
                if (ModelBytesIn(r, 0) > 0 && !l.Exists(delegate (string x) { return Same(x, r); })) l.Add(Norm(r));
            string engine = Engine.Root;
            if (DirBytes(engine, 0) > 0 && !l.Exists(delegate (string x) { return Under(engine, x); })) l.Add(Norm(engine));
            string data = Program.DataDir;
            if (!string.IsNullOrEmpty(data) && l.FindAll(delegate (string x) { return Under(x, data); }).Count > 1)
            {
                l.RemoveAll(delegate (string x) { return Under(x, data); });
                l.Insert(0, Norm(data));
            }
            return l;
        }

        static long DirBytes(string dir, int depth)
        {
            long sum = 0;
            try
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                if (!di.Exists || depth > 12 || (di.Attributes & FileAttributes.ReparsePoint) != 0) return 0;
                foreach (FileInfo f in di.GetFiles()) sum += f.Length;
                foreach (DirectoryInfo d in di.GetDirectories()) sum += DirBytes(d.FullName, depth + 1);
            }
            catch { }
            return sum;
        }

        public static long DownloadBytes(List<string> roots)
        {
            long total = ModelBytes(roots);
            bool engineInsideModels = false;
            foreach (string r in roots) if (Under(Engine.Root, r)) engineInsideModels = true;
            if (!engineInsideModels) total += DirBytes(Engine.Root, 0);
            return total;
        }

        public static EraseResult Run(bool downloads, List<string> roots, string outputs)
        {
            try { BrowserData.WaitUnlocked(BrowserData.Folder, 15000); } catch { }
            EraseResult r = new EraseResult();
            r.KeepModels = !downloads;
            if (downloads) foreach (string root in roots) DeleteModels(root, 0);
            else
            {
                r.KeepDirs.Add(Norm(Engine.Root));
                foreach (string d in Folders()) r.KeepDirs.Add(Norm(Path.Combine(d, "engine")));
            }
            r.Roots.AddRange(Folders());
            if (!string.IsNullOrEmpty(outputs)) r.Roots.Add(outputs);
            for (int pass = 0; pass < 3; pass++)
            {
                r.Left.Clear();
                foreach (string d in r.Roots) EraseTree(d, r, 0, true);
                foreach (string t in TempFolders()) EraseScripts(t, r.Left);
                if (r.Left.Count == 0) break;
                Thread.Sleep(1500);
            }
            r.Roots.AddRange(TempFolders());
            return r;
        }

        static bool DeleteModels(string dir, int depth)
        {
            DirectoryInfo di;
            try
            {
                di = new DirectoryInfo(dir);
                if (!di.Exists || depth > 8 || (di.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (depth > 0 && di.Name.StartsWith(".", StringComparison.Ordinal)) return false;
            }
            catch { return false; }
            bool removed = false;
            try
            {
                foreach (FileInfo f in di.GetFiles())
                {
                    if (!IsModelFile(f.Name) || (f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    try
                    {
                        if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                        f.Delete();
                        removed = true;
                    }
                    catch { }
                }
                foreach (DirectoryInfo d in di.GetDirectories())
                {
                    if (!DeleteModels(d.FullName, depth + 1)) continue;
                    removed = true;
                    try { if (d.GetFileSystemInfos().Length == 0) d.Delete(); } catch { }
                }
            }
            catch { }
            return removed;
        }

        static bool Overlaps(string dir, List<string> keep)
        {
            foreach (string k in keep) if (Under(dir, k) || Under(k, dir)) return true;
            return false;
        }

        static bool InAny(string path, List<string> dirs)
        {
            foreach (string k in dirs) if (Under(path, k)) return true;
            return false;
        }

        static void EraseTree(string dir, EraseResult r, int depth, bool root)
        {
            DirectoryInfo di;
            try { di = new DirectoryInfo(dir); if (!di.Exists) return; }
            catch { return; }
            if (InAny(di.FullName, r.KeepDirs)) return;
            bool link = (di.Attributes & FileAttributes.ReparsePoint) != 0;
            if (link && (!root || SandboxFolder(di.FullName)))
            {
                r.Links.Add(di.FullName);
                try { Directory.Delete(dir, false); } catch { r.Left.Add(dir); }
                return;
            }
            FileInfo[] files;
            DirectoryInfo[] subs;
            try
            {
                if (depth > 64) throw new IOException("too deep");
                files = di.GetFiles();
                subs = di.GetDirectories();
            }
            catch
            {
                if (!Overlaps(di.FullName, r.KeepDirs)) r.Left.Add(dir);
                return;
            }
            foreach (FileInfo f in files)
            {
                bool model = IsModelFile(f.Name);
                if (model && r.KeepModels) continue;
                bool gone;
                if (!model) gone = SecureFile.Erase(f.FullName);
                else
                {
                    try
                    {
                        if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                        f.Delete();
                    }
                    catch { }
                    gone = !File.Exists(f.FullName);
                }
                if (!gone) r.Left.Add(f.FullName);
            }
            foreach (DirectoryInfo sd in subs) EraseTree(sd.FullName, r, depth + 1, false);
            if (link) return;
            try { if (di.GetFileSystemInfos().Length == 0) di.Delete(); } catch { }
        }

        static void EraseScripts(string dir, List<string> left)
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0) return;
                foreach (FileInfo f in di.GetFiles("cmd-*.ps1"))
                    if (!SecureFile.Erase(f.FullName)) left.Add(f.FullName);
                if (di.GetFileSystemInfos().Length == 0) di.Delete();
            }
            catch { }
        }

        public static string ModelsLeftIn(List<string> roots)
        {
            foreach (string r in roots) if (ModelBytesIn(r, 0) > 0) return Norm(r);
            return null;
        }

        public const string FinishArg = "--finish-erase";

        public static void AfterExit(EraseResult r)
        {
            if (r == null || r.Left.Count == 0) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("pid\t").Append(Process.GetCurrentProcess().Id).Append('\n');
                if (r.KeepModels) sb.Append("keepmodels\n");
                foreach (string k in r.KeepDirs) sb.Append("keep\t").Append(k).Append('\n');
                foreach (string l in r.Left) sb.Append("left\t").Append(l).Append('\n');
                foreach (string d in r.Roots) sb.Append("root\t").Append(d).Append('\n');
                string list = Path.Combine(Path.GetTempPath(), "mingent-erase-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".txt");
                File.WriteAllText(list, sb.ToString(), new UTF8Encoding(false));
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, FinishArg + " \"" + list + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetTempPath();
                Process.Start(psi);
            }
            catch { }
        }

        static List<string> AllowedRoots()
        {
            List<string> l = new List<string>();
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string temp = Path.GetTempPath();
                foreach (string name in new string[] { Program.AppName, Program.LegacyName })
                {
                    if (!string.IsNullOrEmpty(local)) l.Add(Norm(Path.Combine(local, name)));
                    if (!string.IsNullOrEmpty(temp)) l.Add(Norm(Path.Combine(temp, name)));
                }
                if (!string.IsNullOrEmpty(docs))
                {
                    l.Add(Norm(Path.Combine(docs, "MinGent Files")));
                    l.Add(Norm(Path.Combine(docs, "JustPad Files")));
                }
            }
            catch { }
            try { string low = Sandbox.LowRoot; if (!string.IsNullOrEmpty(low)) l.Add(Norm(low)); } catch { }
            return l;
        }

        public static void Finish(string list)
        {
            EraseResult r = new EraseResult();
            int pid = 0;
            List<string> allowed = AllowedRoots();
            try
            {
                if (string.IsNullOrEmpty(list) || !File.Exists(list) || new FileInfo(list).Length > 4 * 1024 * 1024) return;
                if (!Path.GetFileName(list).StartsWith("mingent-erase-", StringComparison.Ordinal) || !Same(Path.GetDirectoryName(list), Path.GetTempPath())) return;
                foreach (string line in File.ReadAllLines(list, Encoding.UTF8))
                {
                    int tab = line.IndexOf('\t');
                    string key = tab < 0 ? line : line.Substring(0, tab), val = tab < 0 ? "" : line.Substring(tab + 1);
                    if (key == "pid") int.TryParse(val, out pid);
                    else if (key == "keepmodels") r.KeepModels = true;
                    else if (key == "keep" && val.Length > 0) r.KeepDirs.Add(val);
                    else if (key == "left" && val.Length > 0) r.Left.Add(val);
                    else if (key == "root" && val.Length > 0 && allowed.Exists(delegate (string a) { return Same(val, a); })) r.Roots.Add(val);
                }
            }
            catch { }
            try
            {
                if (pid > 0) using (Process p = Process.GetProcessById(pid)) p.WaitForExit(60000);
            }
            catch { }
            List<string> todo = new List<string>(r.Left);
            for (int attempt = 0; attempt < 6 && todo.Count > 0; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 2000 : 3000);
                List<string> still = new List<string>();
                foreach (string path in todo)
                {
                    if (!r.Roots.Exists(delegate (string root) { return Under(path, root); })) continue;
                    if (Directory.Exists(path))
                    {
                        EraseResult sub = new EraseResult();
                        sub.KeepModels = r.KeepModels;
                        sub.KeepDirs.AddRange(r.KeepDirs);
                        EraseTree(path, sub, 0, false);
                        if (sub.Left.Count > 0) still.Add(path);
                    }
                    else if (File.Exists(path) && !SecureFile.Erase(path)) still.Add(path);
                }
                todo = still;
            }
            foreach (string root in r.Roots) PruneEmpty(root, 0);
            try { SecureFile.Erase(list); } catch { }
        }

        static void PruneEmpty(string dir, int depth)
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                if (!di.Exists || depth > 64) return;
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0) return;
                foreach (DirectoryInfo d in di.GetDirectories()) PruneEmpty(d.FullName, depth + 1);
                if (di.GetFileSystemInfos().Length == 0) di.Delete();
            }
            catch { }
        }
    }

    class EraseResult
    {
        public readonly List<string> Left = new List<string>(), Links = new List<string>(), KeepDirs = new List<string>(), Roots = new List<string>();
        public bool KeepModels;
    }

    class EraseDialog : ThemedForm
    {
        readonly SwitchRow downloads, outputs;
        readonly Button cancel;

        public bool Downloads { get { return downloads != null && downloads.Checked; } }
        public bool Outputs { get { return outputs != null && outputs.Checked; } }

        public EraseDialog(Theme t, long downloadBytes, List<string> downloadPlaces, string outputsDir, int outputFiles, string unsaved)
            : base(t, Program.AppName, 540, 300)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            int pad = UI.S(24), x = pad + UI.S(52), w = ClientSize.Width - x - pad;
            int y = pad + UI.S(2);
            y = AddText(Head, Fonts.Semi, false, x, y, w) + UI.S(10);
            bool showOutputs = outputsDir != null && outputFiles > 0;
            y = AddText(Body + (showOutputs ? "" : " " + OwnFilesStay) + (string.IsNullOrEmpty(unsaved) ? "" : " " + unsaved), UI.Font, false, x, y, w) + UI.S(16);
            bool showDownloads = downloadBytes > 0 && downloadPlaces != null && downloadPlaces.Count > 0;
            if (showOutputs || showDownloads)
            {
                Card c = new Card(Th);
                if (showOutputs)
                    outputs = c.Add(new SwitchRow(Th, "Erase the agent's saved files too", OutputsNote(outputsDir, outputFiles), true));
                if (showDownloads)
                    downloads = c.Add(new SwitchRow(Th, "Delete the engine and models too", ModelCatalog.Size(downloadBytes) + " in " + Places(downloadPlaces) + ". Off: they stay, ready for next time.", false));
                Controls.Add(c);
                c.Location = new Point(x, y);
                c.Arrange(w);
                y += c.Height + UI.S(14);
            }
            y = AddText(SsdNote, UI.Font, true, x, y, w) + UI.S(14);
            Button erase = Dialogs.MakeButton("Erase", Th, false);
            PillButton pe = erase as PillButton;
            if (pe != null) pe.Danger = true;
            cancel = Dialogs.MakeButton("Cancel", Th, false);
            erase.Width = cancel.Width = Math.Max(erase.Width, cancel.Width);
            cancel.Location = new Point(ClientSize.Width - pad - cancel.Width, y);
            erase.Location = new Point(cancel.Left - UI.S(8) - erase.Width, y);
            erase.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            erase.TabIndex = 100;
            cancel.TabIndex = 101;
            Controls.Add(erase);
            Controls.Add(cancel);
            AcceptButton = cancel;
            CancelButton = cancel;
            ClientSize = new Size(ClientSize.Width, y + cancel.Height + pad);
            AccessibleRole = AccessibleRole.Dialog;
            AccessibleDescription = Head + " " + Body;
        }

        public const string Head = "Erase everything MinGent stored on this PC?";
        public const string Body = "Your chats, memory, saved API keys, the built-in browser's data and sign-ins, logs and backups of your notes tabs are overwritten with random data, then deleted. MinGent then closes and starts like new the next time you open it. This can't be undone.";
        public const string OwnFilesStay = "Files in your own folders stay.";
        public const string SsdNote = "On an SSD, no program can guarantee that old copies are physically gone. Device encryption (BitLocker) keeps any leftovers unreadable.";

        public static string OutputsNote(string dir, int files)
        {
            string n = files == 1 ? "1 file" : files.ToString("N0") + (files >= Eraser.CountCap ? "+ files" : " files");
            string s = n + " in " + dir + ", where the agent saves its work.";
            if (dir.IndexOf("\\OneDrive", StringComparison.OrdinalIgnoreCase) >= 0) s += " OneDrive keeps its own copies of deleted files online.";
            return s + " Files in your other folders stay either way.";
        }

        public static string Places(List<string> places)
        {
            if (places.Count == 1) return places[0];
            return string.Join(", ", places.GetRange(0, places.Count - 1).ToArray()) + " and " + places[places.Count - 1];
        }

        int AddText(string text, Font f, bool dim, int x, int y, int w)
        {
            Label l = Dialogs.MakeLabel(text, Th, dim);
            l.Font = f;
            if (!dim) l.ForeColor = Th.Fore;
            int h = Fonts.Height(text, f, w);
            l.SetBounds(x, y, w, h + UI.S(2));
            Controls.Add(l);
            return y + h;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            cancel.Focus();
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Alert.PaintIcon(e.Graphics, Th, MessageBoxIcon.Warning);
        }
    }

    class ConnectionsDialog : ThemedForm
    {
        readonly RowList list = new RowList();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        long shown = -1;

        public ConnectionsDialog(Theme t) : base(t, "Connections", 620, 520)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            int pad = UI.S(20), w = ClientSize.Width - pad * 2;
            string text = (Net.Offline ? "Offline mode is on, so MinGent makes no internet connections now. " : "") +
                          "Internet addresses MinGent connected to since it started, newest first. For the built-in browser, only the pages it opened are listed, not the files those pages load from other sites. Programs that you or the agent start aren't listed either.";
            Label note = Dialogs.MakeLabel(text, Th, true);
            int h = Fonts.Height(text, UI.Font, w);
            note.SetBounds(pad, pad, w, h + UI.S(2));
            Controls.Add(note);
            Button close = Dialogs.MakeButton("Close", Th, true);
            PillButton clear = new PillButton("Clear", Th, false);
            int footY = ClientSize.Height - pad - close.Height;
            close.Location = new Point(ClientSize.Width - pad - close.Width, footY);
            clear.Location = new Point(pad, footY);
            close.DialogResult = DialogResult.OK;
            AcceptButton = close;
            CancelButton = close;
            clear.Click += delegate
            {
                bool file = File.Exists(Net.LogFile);
                if (file && !Alert.Confirm(this, Th, "Clear the list and delete connections.log?", Text, "Clear", true)) return;
                Net.ClearLog();
                Fill();
            };
            list.SetTheme(Th);
            list.RowHeight = 50;
            list.EmptyText = "No internet connections yet.";
            int top = note.Bottom + UI.S(12);
            list.SetBounds(pad, top, w, footY - UI.S(16) - top);
            Controls.Add(list);
            Controls.Add(clear);
            Controls.Add(close);
            timer.Interval = 1500;
            timer.Tick += delegate { Fill(); };
            Fill();
            timer.Start();
        }

        void Fill()
        {
            List<Net.Entry> all = Net.Entries();
            long sig = all.Count;
            foreach (Net.Entry e in all) sig = sig * 31 + e.Count + e.Last.Ticks;
            if (sig == shown) return;
            shown = sig;
            list.Items.Clear();
            foreach (Net.Entry e in all)
            {
                RowItem it = new RowItem();
                it.Title = e.Host;
                it.Sub = e.What + "  ·  " + (e.Count == 1 ? "once" : e.Count.ToString("N0") + " times") + "  ·  last at " + e.Last.ToString("HH:mm:ss");
                list.Items.Add(it);
            }
            list.Changed();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
