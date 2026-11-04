using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace MinGent
{
    class UpdateOffer
    {
        public string Version = "", Tag = "", Page = "", ZipName = "", ZipUrl = "", NoteUrl = "";
        public long ZipBytes;
        public ReleaseNote Note;
        public string Problem, Blocked;
    }

    static class Updater
    {
        public const string Repo = "zenpositive/MinGent", FinishArg = "--finish-update", CheckArg = "--check-release", NoteSuffix = ".sig.txt";
        public const string ReleasesApi = "https://api.github.com/repos/" + Repo + "/releases?per_page=20", ReleasesPage = "https://github.com/" + Repo + "/releases";
        public const long MaxZip = 64L * 1024 * 1024;
        const string ExeName = "MinGent.exe", ResultFile = "result.txt", HealthyFile = "healthy.txt";
        const int MaxTried = 5;
        static readonly Regex DownloadUrl = new Regex("^https://github\\.com/" + Regex.Escape(Repo) + "/releases/download/[A-Za-z0-9._-]{1,100}/[A-Za-z0-9._-]{1,100}\\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static string Current
        {
            get
            {
                object[] a = typeof(Updater).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                return a.Length > 0 ? ((AssemblyInformationalVersionAttribute)a[0]).InformationalVersion : "0.0.0";
            }
        }

        public static string AppDir
        {
            get
            {
                try { return Path.GetDirectoryName(Path.GetFullPath(typeof(Updater).Assembly.Location)); }
                catch { return AppDomain.CurrentDomain.BaseDirectory; }
            }
        }

        public static string[] Trusted { get { return ReleaseKeys.Pinned; } }

        static string LocalData(string name)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name);
        }

        public static string Work { get { return Path.Combine(Program.DataDir ?? LocalData(Program.AppName), "Updates"); } }

        public static string Practice
        {
            get
            {
                string p = Environment.GetEnvironmentVariable("MINGENT_UPDATE_SOURCE");
                return !string.IsNullOrEmpty(p) && Directory.Exists(p) ? p : null;
            }
        }

        public static string Check(CancelToken ct, out UpdateOffer offer)
        {
            return Check(Trusted, Current, AppDir, ct, out offer);
        }

        public static string Check(string[] trusted, string current, string appDir, CancelToken ct, out UpdateOffer offer)
        {
            offer = null;
            string json;
            string err = Fetch(Practice != null ? "releases.json" : ReleasesApi, 2 * 1024 * 1024, ct, out json);
            if (err != null) return err;
            List<object> list = Json.TryParse(json ?? "") as List<object>;
            if (list == null) return "GitHub sent a list of releases MinGent can't read.";
            List<Dictionary<string, object>> newer = new List<Dictionary<string, object>>();
            foreach (object o in list)
            {
                Dictionary<string, object> r = o as Dictionary<string, object>;
                if (r == null || Json.Bool(r, "draft")) continue;
                string tag = Json.Str(r, "tag_name");
                if (ReleaseVersion.Numbers(tag) != null && ReleaseVersion.Compare(tag, current) > 0) newer.Add(r);
            }
            newer.Sort(delegate (Dictionary<string, object> a, Dictionary<string, object> b) { return ReleaseVersion.Compare(Json.Str(b, "tag_name"), Json.Str(a, "tag_name")); });
            for (int i = 0; i < newer.Count && i < MaxTried; i++)
            {
                UpdateOffer f;
                err = Examine(newer[i], trusted, appDir, ct, out f);
                if (err != null) return err;
                if (offer == null || f.Note != null) offer = f;
                if (f.Note != null) break;
            }
            return null;
        }

        static string Examine(Dictionary<string, object> best, string[] trusted, string appDir, CancelToken ct, out UpdateOffer f)
        {
            f = new UpdateOffer();
            f.Tag = Json.Str(best, "tag_name") ?? "";
            f.Page = SafePage(Json.Str(best, "html_url"));
            List<object> assets = Json.Get(best, "assets") as List<object>;
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    string name = Json.Str(o, "name") ?? "";
                    if (f.ZipUrl.Length > 0 || !ReleaseVersion.ZipName.IsMatch(name)) continue;
                    f.ZipName = name;
                    f.ZipUrl = Json.Str(o, "browser_download_url") ?? "";
                    f.ZipBytes = (long)Json.Num(o, "size");
                }
                foreach (object o in assets)
                    if (f.ZipName.Length > 0 && (Json.Str(o, "name") ?? "") == f.ZipName + NoteSuffix) f.NoteUrl = Json.Str(o, "browser_download_url") ?? "";
            }
            if (f.ZipUrl.Length == 0) { f.Problem = "it has no MinGent zip file"; return null; }
            if (f.NoteUrl.Length == 0) { f.Problem = "it has no signature file"; return null; }
            if (!AssetUrl(f.ZipUrl) || !AssetUrl(f.NoteUrl)) { f.Problem = "its files are not on MinGent's own GitHub release page"; return null; }
            string text;
            string err = Fetch(f.NoteUrl, 64 * 1024, ct, out text);
            if (err != null) return "Could not download the release's signature: " + err;
            string why;
            ReleaseNote note = ReleaseNote.Parse(text, out why);
            if (note == null) { f.Problem = "its signature file can't be read: " + why; return null; }
            List<string> keys = new List<string>();
            foreach (string k in trusted) if (!string.IsNullOrEmpty(k) && !keys.Contains(k)) keys.Add(k);
            if (keys.Count < ReleaseKey.Needed) { f.Problem = "this copy of MinGent has no release keys to check it with"; return null; }
            int signers = note.SignedBy(keys).Count;
            if (signers < ReleaseKey.Needed)
            {
                f.Problem = signers == 0 ? "it isn't signed with the keys this version of MinGent trusts" : "it is signed with only one of the keys this version of MinGent trusts, and it needs two";
                return null;
            }
            if (!ReleaseVersion.Exact(note.Version) || !string.Equals(note.File, "MinGent-" + note.Version + ".zip", StringComparison.Ordinal) || !string.Equals(note.File, f.ZipName, StringComparison.Ordinal))
            {
                f.Problem = "its signature is for another file";
                return null;
            }
            string tagged = f.Tag.Trim();
            if (tagged.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tagged = tagged.Substring(1);
            if (!string.Equals(tagged, note.Version, StringComparison.Ordinal)) { f.Problem = "its signature is for another version"; return null; }
            if (note.Bytes > MaxZip) { f.Problem = "it is larger than MinGent accepts"; return null; }
            if (note.Bytes != f.ZipBytes || note.Bytes <= 0) { f.Problem = "the size of its zip file doesn't match its signature"; return null; }
            f.Note = note;
            f.Version = ReleaseVersion.Display(note.Version);
            f.Blocked = Blocked(appDir);
            return null;
        }

        static bool AssetUrl(string url)
        {
            if (Practice != null && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return url.Length > 0 && url.IndexOfAny(new char[] { '/', '\\', ':' }) < 0;
            return DownloadUrl.IsMatch(url) && url.IndexOf("/.", StringComparison.Ordinal) < 0;
        }

        public static string Blocked(string appDir)
        {
            if (Program.Elevated && Native.ElevatedByChoice()) return "MinGent is running as administrator. Start it normally, then check for updates again";
            if (!Program.Primary) return "more than one MinGent window is open. Close them all, start MinGent again, then check for updates";
            if (!File.Exists(Path.Combine(appDir, ExeName))) return ExeName + " isn't in " + appDir + ", the folder this copy runs from";
            if (Directory.Exists(Path.Combine(appDir, ".git")) || File.Exists(Path.Combine(appDir, ".git"))) return "this copy runs from a git checkout (" + appDir + "), and MinGent doesn't overwrite one. Update it with git";
            string personal = null;
            try { personal = Sandbox.Personal(appDir); } catch { }
            if (personal != null) return "this copy runs straight from " + appDir + " (" + personal + "), so the new files would land among your own. Put MinGent in a folder of its own, then check again";
            if (!Writable(appDir)) return "MinGent can't write to the folder it runs from (" + appDir + ")";
            return null;
        }

        static string SafePage(string url)
        {
            Uri u;
            if (Uri.TryCreate(url ?? "", UriKind.Absolute, out u) && u.Scheme == "https" && u.Host == "github.com" && u.AbsolutePath.StartsWith("/" + Repo + "/", StringComparison.OrdinalIgnoreCase)) return u.ToString();
            return ReleasesPage;
        }

        static bool Writable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".mingent-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        static string Fetch(string url, long max, CancelToken ct, out string text)
        {
            text = null;
            string practice = Practice;
            if (practice != null && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string file = Path.Combine(practice, Path.GetFileName(url));
                try
                {
                    if (!File.Exists(file)) return "the practice folder has no " + Path.GetFileName(url);
                    if (new FileInfo(file).Length > max) return Path.GetFileName(url) + " is too large";
                    text = File.ReadAllText(file, Encoding.UTF8);
                    return null;
                }
                catch (Exception ex) { return ex.Message; }
            }
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "the address is not https";
            Dictionary<string, string> h = new Dictionary<string, string>();
            h["Accept"] = "application/vnd.github+json";
            HttpResult r;
            try { r = Http.Send(url, "GET", h, null, 30000, ct, max); }
            catch (OfflineException ex) { return ex.Message; }
            if (r.Cancelled || (ct != null && ct.IsCancelled)) return "Cancelled.";
            string left, wait;
            r.Headers.TryGetValue("x-ratelimit-remaining", out left);
            r.Headers.TryGetValue("retry-after", out wait);
            if (r.Status == 429 || (r.Status == 403 && (left == "0" || wait != null))) return "GitHub is limiting requests from this network right now. Try again in an hour, or look at " + ReleasesPage + ".";
            if (r.Error != null) return r.Error;
            if (!r.Ok) return "GitHub answered " + r.Status.ToString(CultureInfo.InvariantCulture) + ".";
            text = r.Text;
            return null;
        }

        static string Download(string url, string target, ReleaseNote note, Action<long, long> progress, CancelToken ct)
        {
            string practice = Practice;
            if (practice != null && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try { File.Copy(Path.Combine(practice, Path.GetFileName(url)), target, true); }
                catch (Exception ex) { return "Could not copy the practice release: " + ex.Message; }
            }
            else
            {
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "The download address is not https.";
                string err = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    err = DownloadOnce(url, target, note.Bytes, progress, ct);
                    if (err == null || (ct != null && ct.IsCancelled)) break;
                    Thread.Sleep(1500 * (attempt + 1));
                }
                if (err != null) return err;
            }
            if (new FileInfo(target).Length != note.Bytes) return "The download is not the size its signature gives, so it was not installed.";
            if (ReleaseKey.FileHash(target) != note.Sha256) return "The download does not match the checksum in its signature, so it was not installed.";
            return null;
        }

        static string DownloadOnce(string url, string target, long expected, Action<long, long> progress, CancelToken ct)
        {
            if (ct != null && ct.IsCancelled) return "Cancelled.";
            try
            {
                HttpWebRequest req = Http.Create(url, "GET", 60000, null);
                req.AllowAutoRedirect = true;
                req.MaximumAutomaticRedirections = 8;
                Action abort = delegate { try { req.Abort(); } catch { } };
                if (ct != null) ct.Register(abort);
                try
                {
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (FileStream fs = new FileStream(target, FileMode.Create, FileAccess.Write))
                    {
                        Net.Redirected(url, resp.ResponseUri);
                        byte[] buf = new byte[1 << 16];
                        long got = 0;
                        int n;
                        DateTime last = DateTime.MinValue;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            if (Net.Offline) return "Offline mode is on (Settings > Privacy), so MinGent stopped the download.";
                            got += n;
                            if (got > expected) return "The download is larger than its signature says, so it was stopped.";
                            fs.Write(buf, 0, n);
                            if (progress != null && (DateTime.UtcNow - last).TotalMilliseconds > 150) { last = DateTime.UtcNow; progress(got, expected); }
                        }
                    }
                }
                finally { if (ct != null) ct.Unregister(abort); }
            }
            catch (OfflineException ex) { return ex.Message; }
            catch (Exception ex)
            {
                Net.Redirected(url, ex as WebException);
                if (ct != null && ct.IsCancelled) return "Cancelled.";
                return "The download failed: " + ex.Message;
            }
            return null;
        }

        public static string Prepare(UpdateOffer offer, Action<string, double> say, CancelToken ct)
        {
            return Prepare(offer, AppDir, Work, typeof(Updater).Assembly.Location, Build, SelfCheck, say, ct);
        }

        public static string Prepare(UpdateOffer offer, string appDir, string work, string currentExe, Func<string, CancelToken, string> build, Func<string, string, string> check, Action<string, double> say, CancelToken ct)
        {
            if (offer == null || offer.Note == null) return "There is nothing checked to install.";
            if (Program.Erased) return "Cancelled.";
            if (say == null) say = delegate { };
            string blocked = Blocked(appDir);
            if (blocked != null) return "MinGent won't install it: " + blocked + ".";
            string zip = null, staging = Path.Combine(work, "new"), helper = Path.Combine(work, "helper");
            bool ready = false;
            try
            {
                Directory.CreateDirectory(work);
                foreach (string d in new string[] { staging, helper, Path.Combine(work, "backup") }) Remove(d);
                zip = Path.Combine(work, Path.GetFileName(offer.ZipName) + ".part");
                say("Downloading " + offer.ZipName + "\u2026", 0);
                string err = Download(offer.ZipUrl, zip, offer.Note, delegate (long got, long total) { say("Downloading " + offer.ZipName + "\u2026", total > 0 ? (double)got / total : 0); }, ct);
                if (err != null) return err;
                if (ct != null && ct.IsCancelled) return "Cancelled.";
                Directory.CreateDirectory(staging);
                say("Unpacking\u2026", -1);
                err = ReleaseZip.Extract(zip, staging);
                if (err != null) return "The download was signed but " + err + ", so it was not installed.";
                string keys = Path.Combine(Path.Combine(staging, "src"), "ReleaseKeys.cs");
                if (!File.Exists(keys)) return "The new version has no release key file, so it could not check the updates after it. It was not installed.";
                string keysText = File.ReadAllText(keys, Encoding.UTF8);
                err = offer.Note.KeysMatch(keysText);
                if (err != null) return "The release is signed, but " + err + ". It was not installed.";
                if (offer.Note.SignedBy(ReleaseKey.InText(keysText)).Count < ReleaseKey.Needed) return "The release is signed, but not by two of the keys the new version pins, so updates after it might not work. It was not installed.";
                if (!File.Exists(Path.Combine(staging, "Build-MinGent.bat"))) return "The new version has no Build-MinGent.bat, so it can't be built.";
                if (File.Exists(Path.Combine(staging, ExeName))) File.Delete(Path.Combine(staging, ExeName));
                foreach (string icon in new string[] { "icon.png", "icon.png.png" })
                {
                    string mine = Path.Combine(appDir, icon);
                    if (File.Exists(mine)) File.Copy(mine, Path.Combine(staging, icon), true);
                }
                if (ct != null && ct.IsCancelled) return "Cancelled.";
                say("Building MinGent " + offer.Version + " with the C# compiler in Windows\u2026", -1);
                err = build(staging, ct);
                if (err != null) return err;
                if (!File.Exists(Path.Combine(staging, ExeName))) return "The build finished without making MinGent.exe.";
                if (ct != null && ct.IsCancelled) return "Cancelled.";
                say("Checking the new version\u2026", -1);
                string own = Path.Combine(work, "check" + NoteSuffix);
                File.WriteAllText(own, offer.Note.Text(), new UTF8Encoding(false));
                try { err = check(Path.Combine(staging, ExeName), own); }
                finally { try { File.Delete(own); } catch { } }
                if (err != null) return err;
                if (ct != null && ct.IsCancelled) return "Cancelled.";
                Directory.CreateDirectory(helper);
                File.Copy(currentExe, Path.Combine(helper, ExeName), true);
                ready = true;
                return null;
            }
            catch (Exception ex) { return "The update stopped: " + ex.Message; }
            finally
            {
                if (zip != null) { try { File.Delete(zip); } catch { } }
                if (!ready) { Remove(staging); Remove(helper); }
            }
        }

        static bool Remove(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return true;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) { try { File.SetAttributes(f, FileAttributes.Normal); } catch { } }
                Directory.Delete(dir, true);
                return true;
            }
            catch { return false; }
        }

        static string Quote(string s)
        {
            string v = s ?? "";
            return "\"" + (v.EndsWith("\\", StringComparison.Ordinal) ? v + "\\" : v) + "\"";
        }

        public static string StartFinish(string toVersion)
        {
            string work = Work, helper = Path.Combine(Path.Combine(work, "helper"), ExeName);
            try
            {
                if (!File.Exists(helper) || !File.Exists(Path.Combine(Path.Combine(work, "new"), ExeName))) return "the new version is no longer where MinGent built it";
                string args = FinishArg + " " + Quote(work) + " " + Quote(AppDir) + " " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + " " + Quote(Current) + " " + Quote(toVersion);
                ProcessStartInfo psi = new ProcessStartInfo(helper, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(helper);
                Process.Start(psi);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static bool Same(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        class ApplyPlan
        {
            public string AppDir = "", Backup = "";
            public List<string> Saved = new List<string>(), Added = new List<string>();
            public bool Broken;
        }

        static bool Take(Mutex m, int ms)
        {
            if (m == null) return true;
            try { return m.WaitOne(ms); }
            catch (AbandonedMutexException) { return true; }
            catch { return false; }
        }

        static void Let(Mutex m)
        {
            if (m == null) return;
            try { m.ReleaseMutex(); } catch { }
        }

        static void WaitForOld(string pidText)
        {
            int pid;
            if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out pid) || pid <= 0) return;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                    if (string.Equals(p.ProcessName, Path.GetFileNameWithoutExtension(ExeName), StringComparison.OrdinalIgnoreCase)) p.WaitForExit(30000);
            }
            catch { }
        }

        static void WriteResult(string work, string kind, string a, string b)
        {
            try { File.WriteAllText(Path.Combine(work, ResultFile), kind + "\t" + Flat(a) + "\t" + Flat(b), new UTF8Encoding(false)); }
            catch { }
        }

        static string Flat(string s)
        {
            return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        static Process StartApp(string exe, out string error)
        {
            error = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe);
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                return Process.Start(psi);
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        static bool Free(string path, int ms)
        {
            try
            {
                FileAttributes a = File.GetAttributes(path);
                if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, a & ~FileAttributes.ReadOnly);
            }
            catch { }
            DateTime until = DateTime.UtcNow.AddMilliseconds(ms);
            while (true)
            {
                try
                {
                    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)) return true;
                }
                catch (FileNotFoundException) { return true; }
                catch (DirectoryNotFoundException) { return true; }
                catch (UnauthorizedAccessException) { return true; }
                catch { }
                if (DateTime.UtcNow >= until) return false;
                Thread.Sleep(500);
            }
        }

        static int StartedWell(Process p, string work, string exe)
        {
            string healthy = Path.Combine(work, HealthyFile);
            DateTime until = DateTime.UtcNow.AddSeconds(150);
            while (DateTime.UtcNow < until)
            {
                if (File.Exists(healthy)) return 2;
                if (!Directory.Exists(work)) return 1;
                bool exited;
                try { exited = p.HasExited; } catch { exited = true; }
                if (exited && Free(exe, 0))
                {
                    Thread.Sleep(2000);
                    if (File.Exists(healthy)) return 2;
                    if (Free(exe, 0)) return 0;
                }
                Thread.Sleep(300);
            }
            return 1;
        }

        public static void MarkHealthy()
        {
            if (Program.Erased) return;
            try
            {
                string work = Work;
                if (File.Exists(Path.Combine(Path.Combine(work, "helper"), ExeName))) File.WriteAllText(Path.Combine(work, HealthyFile), Current, new UTF8Encoding(false));
            }
            catch { }
        }

        public static string SelfCheck(string exe, string note)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, CheckArg + " " + Quote(note));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(60000))
                    {
                        StopTree(p);
                        return "The new version didn't finish checking its own signature within a minute, so it was not installed.";
                    }
                    if (p.ExitCode == 0) return null;
                    if (p.ExitCode == 4) return "The new version's own version number doesn't match its signature, so it was not installed.";
                    return "The new version could not verify its own release with the keys it pins, so it couldn't install the updates after it. It was not installed.";
                }
            }
            catch (Exception ex) { return "The new version could not be started to check it (" + ex.Message + "), so it was not installed."; }
        }

        public static int CheckOwnRelease(string notePath)
        {
            try
            {
                string why;
                ReleaseNote note = ReleaseNote.Parse(File.ReadAllText(notePath, Encoding.UTF8), out why);
                if (note == null) return 2;
                List<string> keys = new List<string>();
                foreach (string k in Trusted) if (!string.IsNullOrEmpty(k) && !keys.Contains(k)) keys.Add(k);
                if (note.SignedBy(keys).Count < ReleaseKey.Needed) return 1;
                return ReleaseVersion.Compare(Current, note.Version) == 0 ? 0 : 4;
            }
            catch { return 3; }
        }

        public delegate Process Starter(string exe, out string error);

        public static void Finish(string[] args)
        {
            string problem = Finish(args, StartApp);
            if (problem != null) MessageBox.Show(problem, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static string Finish(string[] args, Starter start)
        {
            if (args.Length < 6) return null;
            string work = args[1], appDir = args[2], from = args[4], to = args[5];
            if (!Same(work, Path.Combine(LocalData(Program.AppName), "Updates")) && !Same(work, Path.Combine(LocalData(Program.LegacyName), "Updates"))) return null;
            string exe = Path.Combine(appDir, ExeName), staging = Path.Combine(work, "new"), backup = Path.Combine(work, "backup");
            if (!File.Exists(exe)) return null;
            Mutex mutex = null;
            try { mutex = new Mutex(false, Program.InstanceMutexName(Program.RunsElevated())); }
            catch { mutex = null; }
            if (!Take(mutex, 120000))
            {
                WriteResult(work, "failed", to, "MinGent didn't close within two minutes, so nothing was changed");
                return null;
            }
            WaitForOld(args[3]);
            if (!Free(exe, 30000))
            {
                WriteResult(work, "failed", to, ExeName + " was still in use, perhaps by another MinGent window, so nothing was changed. Close every MinGent window, then check for updates again");
                Let(mutex);
                return null;
            }
            ApplyPlan plan = null;
            string error = File.Exists(Path.Combine(staging, ExeName)) ? Apply(staging, appDir, backup, out plan)
                : "the new version was gone before it could be put in place, perhaps taken by an antivirus program. This version is unchanged";
            string startError;
            if (error == null)
            {
                try { File.Delete(Path.Combine(work, HealthyFile)); } catch { }
                WriteResult(work, "updated", from, to);
                Let(mutex);
                Process started = start(exe, out startError);
                int health = started != null ? StartedWell(started, work, exe) : 0;
                if (health > 0)
                {
                    if (health == 2) Remove(backup);
                    Remove(staging);
                    try { File.Delete(Path.Combine(work, HealthyFile)); } catch { }
                    return null;
                }
                Take(mutex, 30000);
                string undo = Undo(plan);
                if (undo == null) error = (startError != null ? "it could not start (" + startError + ")" : "it stopped unexpectedly soon after it started") + ", so the version you had was put back";
                else
                {
                    plan.Broken = true;
                    error = "it did not start, and putting the version you had back failed (" + undo + "). Its files are in " + plan.Backup + "; copy them into " + appDir + " to repair MinGent";
                }
            }
            WriteResult(work, plan != null && plan.Broken ? "broken" : "failed", to, error);
            Let(mutex);
            if (start(exe, out startError) != null) return null;
            return "MinGent " + to + " was not installed: " + error + ".\n\nMinGent could not start again (" + startError + "). Start " + exe + " yourself.";
        }

        public static string Apply(string staging, string appDir, string backup)
        {
            ApplyPlan plan;
            string error = Apply(staging, appDir, backup, out plan);
            if (error == null) Remove(backup);
            return error;
        }

        static string Apply(string staging, string appDir, string backup, out ApplyPlan plan)
        {
            plan = new ApplyPlan();
            plan.AppDir = appDir;
            plan.Backup = backup;
            List<string> files = new List<string>();
            string root = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string f in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetFullPath(f).Substring(root.Length);
                string top = rel.Split(Path.DirectorySeparatorChar)[0];
                if (top.Equals("build-temp", StringComparison.OrdinalIgnoreCase) || rel.Equals("icon.png", StringComparison.OrdinalIgnoreCase) || rel.Equals("icon.png.png", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.Equals(ExeName, StringComparison.OrdinalIgnoreCase)) files.Insert(0, rel);
                else files.Add(rel);
            }
            if (files.Count == 0 || !files[0].Equals(ExeName, StringComparison.OrdinalIgnoreCase)) return "the new version has no MinGent.exe. This version is unchanged";
            if (!Remove(backup)) return "the old update folder " + backup + " could not be cleared. This version is unchanged";
            try
            {
                foreach (string rel in files)
                {
                    string target = Path.Combine(appDir, rel);
                    if (File.Exists(target))
                    {
                        string copy = Path.Combine(backup, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(copy));
                        File.Copy(target, copy, true);
                        File.SetAttributes(copy, FileAttributes.Normal);
                        plan.Saved.Add(rel);
                        File.SetAttributes(target, FileAttributes.Normal);
                    }
                    else plan.Added.Add(rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (rel.Equals(ExeName, StringComparison.OrdinalIgnoreCase)) ReplaceExe(Path.Combine(staging, rel), target);
                    else CopyOver(Path.Combine(staging, rel), target);
                }
            }
            catch (Exception ex)
            {
                string undo = Undo(plan);
                if (undo == null) return "the new files could not be copied in (" + ex.Message + "). The old ones were put back, so this version is unchanged";
                plan.Broken = true;
                return "the new files could not be copied in (" + ex.Message + "), and putting the old ones back failed too (" + undo + "). The old files are in " + backup + "; copy them into " + appDir + " to repair MinGent";
            }
            return null;
        }

        static string Undo(ApplyPlan plan)
        {
            if (plan == null) return "nothing was recorded to put back";
            string error = null;
            foreach (string rel in plan.Saved)
            {
                try
                {
                    string target = Path.Combine(plan.AppDir, rel), copy = Path.Combine(plan.Backup, rel);
                    if (SameBytes(target, copy)) continue;
                    if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                    if (rel.Equals(ExeName, StringComparison.OrdinalIgnoreCase)) ReplaceExe(copy, target);
                    else CopyOver(copy, target);
                }
                catch (Exception ex) { error = ex.Message; }
            }
            foreach (string rel in plan.Added)
            {
                try
                {
                    string t = Path.Combine(plan.AppDir, rel);
                    if (File.Exists(t)) { File.SetAttributes(t, FileAttributes.Normal); File.Delete(t); }
                }
                catch { }
            }
            return error;
        }

        static bool SameBytes(string a, string b)
        {
            try
            {
                if (!File.Exists(a) || !File.Exists(b) || new FileInfo(a).Length != new FileInfo(b).Length) return false;
                using (FileStream x = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (FileStream y = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    byte[] bx = new byte[1 << 16], by = new byte[1 << 16];
                    while (true)
                    {
                        int nx = Fill(x, bx), ny = Fill(y, by);
                        if (nx != ny) return false;
                        if (nx == 0) return true;
                        for (int i = 0; i < nx; i++) if (bx[i] != by[i]) return false;
                    }
                }
            }
            catch { return false; }
        }

        static int Fill(Stream s, byte[] buf)
        {
            int got = 0, n;
            while (got < buf.Length && (n = s.Read(buf, got, buf.Length - got)) > 0) got += n;
            return got;
        }

        static void ReplaceExe(string from, string to)
        {
            string fresh = to + ".new";
            using (FileStream src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream dst = new FileStream(fresh, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
            {
                src.CopyTo(dst);
                dst.Flush(true);
            }
            for (int i = 0; ; i++)
            {
                try
                {
                    if (File.Exists(to)) File.Replace(fresh, to, null);
                    else File.Move(fresh, to);
                    return;
                }
                catch (IOException) { if (i >= 19) { try { File.Delete(fresh); } catch { } throw; } }
                catch (UnauthorizedAccessException) { if (i >= 19) { try { File.Delete(fresh); } catch { } throw; } }
                Thread.Sleep(500);
            }
        }

        static void CopyOver(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(from, to, true); return; }
                catch (IOException) { if (i >= 19) throw; }
                catch (UnauthorizedAccessException) { if (i >= 19) throw; }
                Thread.Sleep(500);
            }
        }

        public static string[] TakeResult()
        {
            string f = Path.Combine(Work, ResultFile);
            try
            {
                if (!File.Exists(f)) return null;
                string text = File.ReadAllText(f, Encoding.UTF8);
                File.Delete(f);
                string[] parts = text.Trim().Split('\t');
                return parts.Length == 3 && (parts[0] == "updated" || parts[0] == "failed" || parts[0] == "broken") ? parts : null;
            }
            catch { return null; }
        }

        public static void CleanUp(string[] update)
        {
            string work = Work;
            List<string> dirs = new List<string>(new string[] { "new", "helper" });
            if (update != null && update[0] == "failed") dirs.Add("backup");
            Thread t = new Thread(delegate ()
            {
                for (int i = 0; i < 10; i++)
                {
                    bool left = false;
                    foreach (string d in dirs) if (!Remove(Path.Combine(work, d))) left = true;
                    if (!left) return;
                    Thread.Sleep(1000);
                }
            });
            t.IsBackground = true;
            t.Name = "MinGent update clean-up";
            t.Start();
        }

        public static string Build(string folder, CancelToken ct)
        {
            string cmd = Native.WindowsExe("cmd.exe");
            ProcessStartInfo psi = new ProcessStartInfo(cmd, "/d /c \"\"" + Path.Combine(folder, "Build-MinGent.bat") + "\" /quiet\"");
            psi.WorkingDirectory = folder;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            StringBuilder log = new StringBuilder();
            ManualResetEvent outDone = new ManualResetEvent(false), errDone = new ManualResetEvent(false);
            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data == null) outDone.Set(); else lock (log) { if (log.Length < 64 * 1024) log.AppendLine(e.Data); } };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data == null) errDone.Set(); else lock (log) { if (log.Length < 64 * 1024) log.AppendLine(e.Data); } };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    DateTime until = DateTime.UtcNow.AddMinutes(5);
                    while (!p.WaitForExit(200))
                    {
                        if ((ct != null && ct.IsCancelled) || DateTime.UtcNow > until)
                        {
                            StopTree(p);
                            return ct != null && ct.IsCancelled ? "Cancelled." : "Building the new version took more than 5 minutes, so it was stopped.";
                        }
                    }
                    outDone.WaitOne(10000);
                    errDone.WaitOne(5000);
                    if (p.ExitCode != 0) return "The new version did not build:\n" + Tail(log.ToString(), 12);
                }
            }
            catch (Exception ex) { return "Could not start the build: " + ex.Message; }
            return null;
        }

        static void StopTree(Process p)
        {
            try
            {
                ProcessStartInfo kill = new ProcessStartInfo(Native.WindowsExe("taskkill.exe"), "/T /F /PID " + p.Id.ToString(CultureInfo.InvariantCulture));
                kill.UseShellExecute = false;
                kill.CreateNoWindow = true;
                using (Process k = Process.Start(kill)) k.WaitForExit(10000);
            }
            catch { }
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.WaitForExit(10000); } catch { }
        }

        static string Tail(string text, int lines)
        {
            List<string> keep = new List<string>();
            foreach (string l in text.Replace("\r", "").Split('\n')) if (l.Trim().Length > 0 && !l.Trim().StartsWith("=", StringComparison.Ordinal)) keep.Add(l.Trim());
            if (keep.Count > lines) keep = keep.GetRange(keep.Count - lines, lines);
            return string.Join("\n", keep.ToArray());
        }
    }

    static class UpdateFlow
    {
        public static void Run(IWin32Window owner, Theme th, Action<string> ready)
        {
            string caption = "Check for updates";
            UpdateOffer offer = null;
            string err = null;
            if (!BusyDialog.Run(owner, th, caption, "Asking GitHub for the newest MinGent release\u2026", delegate (CancelToken ct, Action<string, double> say)
                {
                    try { err = Updater.Check(ct, out offer); }
                    catch (Exception ex) { err = ex.Message; offer = null; }
                }))
                return;
            string current = ReleaseVersion.Display(Updater.Current);
            if (err != null) { Alert.Show(owner, th, "MinGent could not check for updates.\n\n" + err, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (offer == null) { Alert.Show(owner, th, "MinGent " + current + " is the newest release.", caption, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (offer.Problem != null)
            {
                Alert.Show(owner, th, "There is a newer release on GitHub, but MinGent could not verify that it comes from the developer: " + offer.Problem + ".\n\nDon't install it. If the developer made a mistake, a fixed release will show up here.", caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (offer.Blocked != null)
            {
                if (Alert.Choose(owner, th, "MinGent " + offer.Version + " is out and signed by the developer, but it can't be installed here: " + offer.Blocked + ".\n\nYou can also download it from the release page and build it yourself.",
                        caption, MessageBoxIcon.Information, new string[] { "Open the release page", "Close" }, new DialogResult[] { DialogResult.Yes, DialogResult.Cancel }, 1, 1) == DialogResult.Yes)
                    OpenPage(offer.Page);
                return;
            }
            string text = "Install MinGent " + offer.Version + "?\n\n" +
                          "Both of the developer's keys signed it. MinGent downloads it from GitHub (" + SizeText(offer.ZipBytes) + "), checks the signatures again and builds it with the compiler in Windows. " +
                          "Then MinGent closes, puts the new version in place of this one and opens again.\n\n" +
                          "Your chats, notes and settings stay as they are.";
            string page = offer.Page;
            DialogResult d = Alert.Choose(owner, th, text, caption, MessageBoxIcon.Information, new string[] { "Install", "What's new", "Not now" },
                new DialogResult[] { DialogResult.Yes, DialogResult.Retry, DialogResult.Cancel }, 0, 2, new Action[] { null, delegate { OpenPage(page); }, null });
            if (d != DialogResult.Yes) return;
            err = null;
            bool finished = BusyDialog.Run(owner, th, caption, "Starting\u2026", delegate (CancelToken ct, Action<string, double> say)
            {
                try { err = Updater.Prepare(offer, say, ct); }
                catch (Exception ex) { err = ex.Message; }
            });
            if (!finished || err == "Cancelled.") { Alert.Show(owner, th, "The update was cancelled. Nothing changed.", caption, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (err != null) { Alert.Show(owner, th, "MinGent " + offer.Version + " was not installed.\n\n" + err + "\n\nThis version is unchanged.", caption, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (ready != null) ready(offer.Version);
        }

        static string SizeText(long bytes)
        {
            if (bytes < 1048576) return Math.Max(1, (bytes + 1023) / 1024).ToString(CultureInfo.InvariantCulture) + " KB";
            return (bytes / 1048576.0).ToString(bytes < 10485760 ? "0.0" : "0", CultureInfo.InvariantCulture) + " MB";
        }

        static void OpenPage(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }

    class BusyDialog : ThemedForm
    {
        readonly Label line;
        readonly Button cancel;
        readonly CancelToken ct = new CancelToken();
        double share = -1;
        bool done;
        int tick;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        BusyDialog(Theme t, string caption, string text) : base(t, caption, 460, 132)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ControlBox = false;
            int pad = UI.S(24);
            line = Dialogs.MakeLabel(text, Th, false);
            line.ForeColor = Th.Fore;
            line.SetBounds(pad, pad, ClientSize.Width - pad * 2, UI.S(40));
            Controls.Add(line);
            cancel = Dialogs.MakeButton("Cancel", Th, false);
            cancel.Location = new Point(ClientSize.Width - pad - cancel.Width, ClientSize.Height - pad - cancel.Height + UI.S(8));
            cancel.Click += delegate { ct.Cancel(); cancel.Enabled = false; };
            Controls.Add(cancel);
            CancelButton = cancel;
            timer.Interval = 60;
            timer.Tick += delegate { tick++; Invalidate(new Rectangle(0, BarTop - 2, ClientSize.Width, UI.S(8))); };
            FormClosing += delegate (object o, FormClosingEventArgs e) { if (!done) { e.Cancel = true; ct.Cancel(); cancel.Enabled = false; } };
        }

        int BarTop { get { return UI.S(24) + UI.S(46); } }

        void Say(string text, double part)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (IsDisposed) return;
                    line.Text = text;
                    share = part;
                    Invalidate(new Rectangle(0, BarTop - 2, ClientSize.Width, UI.S(8)));
                }));
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int pad = UI.S(24), w = ClientSize.Width - pad * 2 - cancel.Width - UI.S(16), h = UI.S(4);
            Rectangle track = new Rectangle(pad, BarTop, w, h);
            using (SolidBrush b = new SolidBrush(Th.Track)) e.Graphics.FillRectangle(b, track);
            using (SolidBrush b = new SolidBrush(Th.Accent))
            {
                if (share >= 0) e.Graphics.FillRectangle(b, new Rectangle(pad, BarTop, (int)(w * Math.Min(1, share)), h));
                else
                {
                    int seg = w / 4, x = pad + (tick * UI.S(6)) % (w + seg) - seg;
                    Rectangle r = Rectangle.Intersect(track, new Rectangle(x, BarTop, seg, h));
                    if (r.Width > 0) e.Graphics.FillRectangle(b, r);
                }
            }
        }

        public static bool Run(IWin32Window owner, Theme t, string caption, string text, Action<CancelToken, Action<string, double>> work)
        {
            using (BusyDialog d = new BusyDialog(t, caption, text))
            {
                d.Shown += delegate
                {
                    d.timer.Start();
                    Thread th = new Thread(delegate ()
                    {
                        try { work(d.ct, d.Say); }
                        catch { }
                        try { d.BeginInvoke(new Action(delegate { d.done = true; d.timer.Stop(); d.DialogResult = DialogResult.OK; d.Close(); })); } catch { }
                    });
                    th.IsBackground = true;
                    th.Name = "MinGent update";
                    th.Start();
                };
                d.ShowDialog(owner);
                return !d.ct.IsCancelled;
            }
        }
    }
}
