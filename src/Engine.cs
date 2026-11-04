using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MinGent
{
    class EngineBuild
    {
        public string Backend, Title, Note;
        public string[] Assets, Sha256;
        public long ApproxBytes;
        public EngineBuild(string backend, string title, string note, long bytes, string[] assets, string[] sha)
        {
            Backend = backend; Title = title; Note = note; ApproxBytes = bytes; Assets = assets; Sha256 = sha;
        }
    }

    class EngineRelease
    {
        public string Tag, Version, Backend;
        public string[] Assets, Sha256;
        public long Bytes;
    }

    static class EngineUpdates
    {
        public const string LatestApi = "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest";
        const string TagApi = "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/";
        public static volatile bool Enabled;
        public static event Action Changed;
        static readonly object gate = new object();
        static EngineRelease found;
        static string checkedFor, lastError;
        static DateTime checkedAt = DateTime.MinValue;
        static bool running;

        public static EngineRelease Available
        {
            get
            {
                EngineRelease r;
                lock (gate) r = found;
                if (!Enabled || r == null) return null;
                Engine e = Engine.Instance;
                if (r.Backend != e.InstalledBackend || Engine.TagNumber(r.Tag) <= Engine.TagNumber(e.InstalledTag)) return null;
                return r;
            }
        }

        public static string LastError { get { lock (gate) return lastError; } }

        public static void CheckSoon()
        {
            if (!Enabled || Net.Offline) return;
            string backend = Engine.Instance.InstalledBackend;
            if (backend == null) return;
            lock (gate)
            {
                if (running || (checkedFor == backend && (DateTime.UtcNow - checkedAt).TotalHours < 6)) return;
                running = true;
            }
            Thread t = new Thread(delegate ()
            {
                EngineRelease r = null;
                string err = null;
                try { r = Latest(backend, null, out err); } catch (Exception ex) { err = ex.Message; }
                lock (gate)
                {
                    found = r;
                    lastError = err;
                    checkedFor = backend;
                    checkedAt = DateTime.UtcNow;
                    running = false;
                }
                Action c = Changed;
                if (c != null) { try { c(); } catch { } }
            });
            t.IsBackground = true;
            t.Name = "llama.cpp release check";
            t.Start();
        }

        public static EngineRelease Latest(string backend, CancelToken ct, out string error)
        {
            Dictionary<string, string> h = new Dictionary<string, string>();
            h["Accept"] = "application/vnd.github+json";
            HttpResult r = Http.Get(LatestApi, h, 15000, ct);
            if (!r.Ok) { error = Explain(r); return null; }
            object latest = r.Json;
            string version = Version(latest), tag = NightlyTag(latest);
            if (version == null || tag == null) { error = "GitHub's answer did not name a llama.cpp build."; return null; }
            HttpResult t = Http.Get(TagApi + tag, h, 15000, ct);
            if (!t.Ok) { error = Explain(t); return null; }
            return FromAssets(t.Json, tag, version, backend, out error);
        }

        public static string Version(object latest)
        {
            if (Json.Bool(latest, "draft") || Json.Bool(latest, "prerelease")) return null;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(Json.Str(latest, "tag_name") ?? "", @"^v(\d{1,4}(\.\d{1,4}){1,3})\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string NightlyTag(object latest)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(Json.Str(latest, "body") ?? "", @"Nightly build:\**\s*\[?(b\d{3,7})\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static EngineRelease FromAssets(object rel, string tag, string version, string backend, out string error)
        {
            error = null;
            EngineBuild build = Engine.BuildFor(backend);
            if (build == null || Engine.TagNumber(tag) < 0 || Json.Str(rel, "tag_name") != tag || Json.Bool(rel, "draft")) { error = "GitHub's answer did not match the llama.cpp build it named."; return null; }
            List<object> assets = Json.List(rel, "assets");
            Dictionary<string, object> byName = new Dictionary<string, object>(StringComparer.Ordinal);
            if (assets != null)
                foreach (object a in assets)
                {
                    string n = Json.Str(a, "name");
                    if (n != null && Json.Str(a, "state") == "uploaded") byName[n] = a;
                }
            List<string> names = new List<string>();
            if (backend == "cpu" || backend == "vulkan") names.Add("llama-" + tag + "-bin-win-" + backend + "-x64.zip");
            else
            {
                string major = backend.Substring(4);
                System.Text.RegularExpressions.Regex rx = new System.Text.RegularExpressions.Regex("^llama-" + tag + @"-bin-win-cuda-" + major + @"\.(\d{1,3})-x64\.zip\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                int best = -1;
                foreach (string n in byName.Keys)
                {
                    System.Text.RegularExpressions.Match m = rx.Match(n);
                    int minor;
                    if (m.Success && int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out minor) && minor > best && byName.ContainsKey("cudart-llama-bin-win-cuda-" + major + "." + minor + "-x64.zip"))
                        best = minor;
                }
                if (best >= 0)
                {
                    names.Add("llama-" + tag + "-bin-win-cuda-" + major + "." + best + "-x64.zip");
                    names.Add("cudart-llama-bin-win-cuda-" + major + "." + best + "-x64.zip");
                }
            }
            EngineRelease r = new EngineRelease();
            r.Tag = tag;
            r.Version = version;
            r.Backend = backend;
            r.Assets = names.ToArray();
            r.Sha256 = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                object a;
                if (!byName.TryGetValue(names[i], out a)) { names.Clear(); break; }
                System.Text.RegularExpressions.Match d = System.Text.RegularExpressions.Regex.Match(Json.Str(a, "digest") ?? "", @"^sha256:([0-9a-f]{64})\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                if (!d.Success) { error = "GitHub lists no SHA-256 checksum for " + names[i] + ", so MinGent won't install it."; return null; }
                long size = Json.Long(a, "size");
                if (size <= 0 || size > 2L * 1024 * 1024 * 1024) { error = names[i] + " has an unexpected size."; return null; }
                r.Sha256[i] = d.Groups[1].Value;
                r.Bytes += size;
            }
            if (names.Count == 0) { error = "llama.cpp " + version + " has no " + build.Title + " build for Windows."; return null; }
            return r;
        }

        static string Explain(HttpResult r)
        {
            if (r.Error != null && r.Error.IndexOf("Offline mode", StringComparison.Ordinal) >= 0) return r.Error;
            if ((r.Status == 403 || r.Status == 429) && (r.Text ?? "").IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0) return "GitHub is limiting requests right now. Try again in an hour.";
            if (r.ConnectFailed) return "Could not reach GitHub.";
            return r.Error ?? ("GitHub answered with status " + r.Status + ".");
        }
    }

    class Engine
    {
        public const string BuildTag = "b11669", BuildVersion = "0.7.0";
        const string Downloads = "https://github.com/ggml-org/llama.cpp/releases/download/";
        const string VersionFile = "llama.cpp-version.txt";
        static readonly System.Text.RegularExpressions.Regex InstallName = new System.Text.RegularExpressions.Regex(@"^b(\d{3,7})-(cuda13|cuda12|vulkan|cpu)\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        public static readonly EngineBuild[] Builds = {
            new EngineBuild("cuda13", "NVIDIA GPU (CUDA 13)", "Fastest on RTX 20/30/40/50 cards. Needs NVIDIA driver 580 or newer.", 551L * 1024 * 1024,
                new string[] { "llama-" + BuildTag + "-bin-win-cuda-13.4-x64.zip", "cudart-llama-bin-win-cuda-13.4-x64.zip" },
                new string[] { "3b92d5c064f5032e603e531d3ec8961f3bd6613e5dfad047399a92dec8e95dbc", "738f8c251ac22b70c3ae6f83a10cf222725df0395246a2cf58f32bdb85fbe668" }),
            new EngineBuild("cuda12", "NVIDIA GPU (CUDA 12)", "For NVIDIA drivers older than 580.", 627L * 1024 * 1024,
                new string[] { "llama-" + BuildTag + "-bin-win-cuda-12.4-x64.zip", "cudart-llama-bin-win-cuda-12.4-x64.zip" },
                new string[] { "c2da1f73ed176ae093ac5aceb978a7b167a16113f6d74c6789eeaee2135cb8dc", "8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6" }),
            new EngineBuild("vulkan", "Any GPU (Vulkan)", "Works with NVIDIA, AMD and Intel graphics.", 31L * 1024 * 1024,
                new string[] { "llama-" + BuildTag + "-bin-win-vulkan-x64.zip" },
                new string[] { "2c07574e7bde71321cb785282b20cc821e951132149eeceeea32ddebdbf0598e" }),
            new EngineBuild("cpu", "CPU only", "Slow for big models, but runs anywhere.", 19L * 1024 * 1024,
                new string[] { "llama-" + BuildTag + "-bin-win-cpu-x64.zip" },
                new string[] { "37b3efcf962847ca869745fd4ccf7f5e7e8dacfeff9fa3523b4ca8cdc66d632b" })
        };

        public static readonly Engine Instance = new Engine();

        readonly object gate = new object();
        Process proc;
        readonly StringBuilder log = new StringBuilder();
        public string BaseUrl = "", ApiKey = "", ModelPath = "", Backend = "";
        public int Port, Context;
        public volatile bool Starting;
        public string LastError;
        int generation;
        volatile int listenPort;
        static readonly System.Text.RegularExpressions.Regex Listening = new System.Text.RegularExpressions.Regex(@"listening on https?://127\.0\.0\.1:(\d{1,5})\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        public int Generation { get { lock (gate) return generation; } }

        public string Gone(int gen)
        {
            lock (gate)
            {
                bool alive = false;
                try { alive = proc != null && !proc.HasExited && generation == gen && !Starting; } catch { }
                if (alive) return null;
            }
            string why = LastError;
            return "The built-in engine stopped unexpectedly" + (string.IsNullOrEmpty(why) ? "" : " (" + why + ")") + ". It starts again when you send your next message.";
        }

        public static string Root { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), "engine"); } }

        public static string DirFor(string tag, string backend) { return Path.Combine(Root, tag + "-" + backend); }

        public static EngineBuild BuildFor(string backend)
        {
            foreach (EngineBuild b in Builds) if (b.Backend == backend) return b;
            return null;
        }

        public static int TagNumber(string tag)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(tag ?? "", @"^b(\d{3,7})\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return m.Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : -1;
        }

        static string[] Installation()
        {
            string best = null, tag = null, backend = null;
            int bestN = -1;
            long bestStamp = -1;
            try
            {
                if (!Directory.Exists(Root)) return null;
                foreach (string d in Directory.GetDirectories(Root))
                {
                    System.Text.RegularExpressions.Match m = InstallName.Match(Path.GetFileName(d));
                    if (!m.Success || !File.Exists(Path.Combine(d, "llama-server.exe"))) continue;
                    string t = "b" + m.Groups[1].Value;
                    string marker = Path.Combine(d, VersionFile);
                    bool marked = File.Exists(marker);
                    if (!marked && t != BuildTag) continue;
                    int n = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    long stamp = marked ? File.GetLastWriteTimeUtc(marker).Ticks : Directory.GetCreationTimeUtc(d).Ticks;
                    if (n < bestN || (n == bestN && stamp <= bestStamp)) continue;
                    bestN = n;
                    bestStamp = stamp;
                    best = d;
                    tag = t;
                    backend = m.Groups[2].Value;
                }
            }
            catch { }
            return best == null ? null : new string[] { tag, backend, best };
        }

        public string InstalledBackend { get { string[] i = Installation(); return i == null ? null : i[1]; } }

        public string InstalledTag { get { string[] i = Installation(); return i == null ? null : i[0]; } }

        public string InstalledVersion
        {
            get
            {
                string[] i = Installation();
                if (i == null) return null;
                if (i[0] == BuildTag) return BuildVersion;
                try
                {
                    string f = Path.Combine(i[2], VersionFile);
                    if (File.Exists(f) && new FileInfo(f).Length < 64)
                    {
                        string v = File.ReadAllText(f).Trim();
                        if (System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{1,4}(\.\d{1,4}){1,3}$")) return v;
                    }
                }
                catch { }
                return null;
            }
        }

        public static string Describe(string version, string tag)
        {
            return "llama.cpp " + (version ?? tag);
        }

        public string InstalledText { get { string t = InstalledTag; return t == null ? null : Describe(InstalledVersion, t); } }

        public bool Installed { get { return InstalledBackend != null; } }

        public bool OnTestedBuild { get { return InstalledTag == BuildTag; } }

        public bool Running
        {
            get { lock (gate) { try { return proc != null && !proc.HasExited && !Starting; } catch { return false; } } }
        }

        public string StatusText
        {
            get
            {
                string b = InstalledBackend;
                if (b == null) return "Not installed";
                if (Starting) return "Loading a model...";
                if (Running) return "Running " + Path.GetFileName(ModelPath);
                EngineBuild eb = BuildFor(b);
                return "Installed: " + InstalledText + " \u00B7 " + (eb != null ? eb.Title : b);
            }
        }

        public string Install(string backend, Action<string, long, long> progress, CancelToken ct)
        {
            EngineBuild b = BuildFor(backend);
            if (b == null) return "Unknown engine build.";
            return InstallFiles(BuildTag, null, backend, b.Assets, b.Sha256, b.ApproxBytes, false, progress, ct);
        }

        public string Install(EngineRelease r, Action<string, long, long> progress, CancelToken ct)
        {
            if (r == null || BuildFor(r.Backend) == null || TagNumber(r.Tag) < 0 || r.Assets == null || r.Sha256 == null || r.Assets.Length != r.Sha256.Length || r.Assets.Length == 0)
                return "Unknown engine build.";
            return InstallFiles(r.Tag, r.Version, r.Backend, r.Assets, r.Sha256, r.Bytes, true, progress, ct);
        }

        string InstallFiles(string tag, string version, string backend, string[] assets, string[] sha, long approx, bool listed, Action<string, long, long> progress, CancelToken ct)
        {
            if (Net.Offline) return "Offline mode is on (Settings > Privacy), so MinGent can't download the engine.";
            foreach (string a in assets)
                if (string.IsNullOrEmpty(a) || a.IndexOfAny(new char[] { '/', '\\', ':' }) >= 0 || a.Contains("..")) return "Unknown engine build.";
            string dir = DirFor(tag, backend);
            string tmp = Path.Combine(Root, "download-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            try
            {
                long total = 0, done = 0;
                for (int i = 0; i < assets.Length; i++)
                {
                    string zip = Path.Combine(tmp, assets[i]);
                    string name = assets[i];
                    string err = DownloadVerified(Downloads + tag + "/" + assets[i], zip, sha[i], listed, delegate (long d, long t)
                    {
                        if (progress != null) progress("Downloading " + name, done + d, Math.Max(total, approx));
                    }, ct);
                    if (err != null) return err;
                    done += new FileInfo(zip).Length;
                    total = Math.Max(total, done);
                }
                string stage = Path.Combine(tmp, "x");
                Directory.CreateDirectory(stage);
                foreach (string asset in assets)
                {
                    if (ct != null && ct.IsCancelled) return "Cancelled.";
                    if (progress != null) progress("Unpacking " + asset, done, Math.Max(total, done));
                    string err = SafeExtract(Path.Combine(tmp, asset), stage);
                    if (err != null) return err;
                }
                string server = FindFile(stage, "llama-server.exe");
                if (server == null) return "The download did not contain llama-server.exe.";
                string src = Path.GetDirectoryName(server);
                foreach (string f in Directory.GetFiles(stage, "*.dll", SearchOption.AllDirectories))
                {
                    string dst = Path.Combine(src, Path.GetFileName(f));
                    if (!File.Exists(dst)) File.Move(f, dst);
                }
                Stop();
                if (Directory.Exists(dir)) Retire(dir);
                if (Directory.Exists(dir)) return "Install failed: the old engine folder is still in use. Close programs that use it and try again.";
                Directory.CreateDirectory(Path.GetDirectoryName(dir));
                Directory.Move(src, dir);
                File.WriteAllText(Path.Combine(dir, VersionFile), version ?? BuildVersion);
                foreach (string other in Directory.GetDirectories(Root))
                {
                    if (string.Equals(other.TrimEnd('\\', '/'), dir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) continue;
                    string name = Path.GetFileName(other);
                    if (InstallName.IsMatch(name) || name.StartsWith("old-", StringComparison.Ordinal)) Retire(other);
                }
                return null;
            }
            catch (Exception ex) { return "Install failed: " + ex.Message; }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        static void Retire(string folder)
        {
            try { File.Delete(Path.Combine(folder, VersionFile)); } catch { }
            try { Directory.Delete(folder, true); return; } catch { }
            if (Path.GetFileName(folder).StartsWith("old-", StringComparison.Ordinal)) return;
            try { Directory.Move(folder, Path.Combine(Root, "old-" + Guid.NewGuid().ToString("N").Substring(0, 8))); } catch { }
        }

        public string Uninstall()
        {
            Stop();
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); return null; }
            catch (Exception ex) { return ex.Message; }
        }

        static string FindFile(string root, string name)
        {
            foreach (string f in Directory.GetFiles(root, name, SearchOption.AllDirectories)) return f;
            return null;
        }

        static string DownloadVerified(string url, string target, string sha256, bool listed, Action<long, long> progress, CancelToken ct)
        {
            for (int attempt = 0; attempt < 4; attempt++)
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
                        using (SHA256 sha = SHA256.Create())
                        {
                            Net.Redirected(url, resp.ResponseUri);
                            long len = resp.ContentLength, got = 0;
                            byte[] buf = new byte[1 << 20];
                            int n;
                            DateTime last = DateTime.MinValue;
                            while ((n = s.Read(buf, 0, buf.Length)) > 0)
                            {
                                if (Net.Offline) { fs.Close(); try { File.Delete(target); } catch { } return "Offline mode is on (Settings > Privacy), so MinGent stopped the download."; }
                                fs.Write(buf, 0, n);
                                sha.TransformBlock(buf, 0, n, null, 0);
                                got += n;
                                if (got > 2L * 1024 * 1024 * 1024) return "The download was larger than expected, so it was stopped.";
                                if (progress != null && (DateTime.UtcNow - last).TotalMilliseconds > 200) { last = DateTime.UtcNow; progress(got, len); }
                            }
                            sha.TransformFinalBlock(new byte[0], 0, 0);
                            StringBuilder hex = new StringBuilder();
                            foreach (byte x in sha.Hash) hex.Append(x.ToString("x2"));
                            if (!string.Equals(hex.ToString(), sha256, StringComparison.OrdinalIgnoreCase))
                            {
                                fs.Close();
                                try { File.Delete(target); } catch { }
                                return "Security check failed: " + Path.GetFileName(target) + " does not match the SHA-256 checksum " + (listed ? "GitHub lists for it" : "built into MinGent") + ", so it was deleted and not installed.";
                            }
                            return null;
                        }
                    }
                    finally { if (ct != null) ct.Unregister(abort); }
                }
                catch (Exception ex)
                {
                    Net.Redirected(url, ex as WebException);
                    if (ct != null && ct.IsCancelled) return "Cancelled.";
                    if (attempt == 3) return "Download failed: " + ex.Message;
                    Thread.Sleep(2000 * (attempt + 1));
                }
            }
            return "Download failed.";
        }

        public static string SafeExtract(string zipPath, string dest)
        {
            string root = Path.GetFullPath(dest).TrimEnd('\\') + "\\";
            long total = 0;
            try
            {
                using (ZipArchive za = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry e in za.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        string rel = e.FullName.Replace('/', '\\');
                        if (rel.StartsWith("\\") || rel.Contains(":") || rel.Contains("..\\") || rel.StartsWith("..")) return "The archive contains an unsafe path: " + e.FullName;
                        string full = Path.GetFullPath(Path.Combine(dest, rel));
                        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "The archive contains an unsafe path: " + e.FullName;
                        total += e.Length;
                        if (total > 6L * 1024 * 1024 * 1024) return "The archive unpacks to an unexpected size.";
                        Directory.CreateDirectory(Path.GetDirectoryName(full));
                        e.ExtractToFile(full, true);
                    }
                }
                return null;
            }
            catch (Exception ex) { return "Could not unpack " + Path.GetFileName(zipPath) + ": " + ex.Message; }
        }

        static string RandomKey()
        {
            byte[] b = new byte[24];
            using (RNGCryptoServiceProvider r = new RNGCryptoServiceProvider()) r.GetBytes(b);
            StringBuilder sb = new StringBuilder("jp-");
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "") + "\""; }

        public const int MinAutoContext = 16384;

        static long PromptCacheMiB()
        {
            long ram = Hardware.RamBytes / (1024 * 1024);
            if (ram <= 0) return 4096;
            return Math.Max(2048, Math.Min(32768, ram / 4));
        }

        int requested = -1;
        string requestedKv;

        public bool Matches(string modelPath, int ctx, string kvQuant)
        {
            lock (gate)
            {
                try { return proc != null && !proc.HasExited && !Starting && string.Equals(ModelPath, modelPath, StringComparison.OrdinalIgnoreCase) && ctx == requested && string.Equals(kvQuant ?? "", requestedKv ?? "", StringComparison.Ordinal); }
                catch { return false; }
            }
        }

        public string Start(LocalModel model, int ctx, string kvQuant, Action<string> progress, CancelToken ct)
        {
            string backend = InstalledBackend;
            if (backend == null) return "The built-in engine is not installed yet. Set it up from Models in the sidebar.";
            lock (gate)
            {
                try { if (proc != null && !proc.HasExited && string.Equals(ModelPath, model.Path, StringComparison.OrdinalIgnoreCase) && ctx == requested && string.Equals(kvQuant ?? "", requestedKv ?? "", StringComparison.Ordinal)) return null; } catch { }
            }
            string err = Launch(backend, model, ctx, kvQuant, true, progress, ct);
            if (err != null && ArgsRejected(LogTail(60)))
                err = Launch(backend, model, ctx > 0 ? ctx : 32768, kvQuant, false, progress, ct);
            if (err == null) { requested = ctx; requestedKv = kvQuant; }
            return err;
        }

        static bool ArgsRejected(string tail)
        {
            string low = (tail ?? "").ToLowerInvariant();
            return (low.Contains("invalid argument") || low.Contains("unknown argument") || low.Contains("unrecognized")) &&
                   (low.Contains("fit") || low.Contains("cache-ram"));
        }

        string Launch(string backend, LocalModel model, int ctx, string kvQuant, bool modern, Action<string> progress, CancelToken ct)
        {
            string[] inst = Installation();
            if (inst == null) return "The built-in engine is not installed yet. Set it up from Models in the sidebar.";
            string exe = Path.Combine(inst[2], "llama-server.exe");
            Stop();
            Starting = true;
            LastError = null;
            try
            {
                string key = RandomKey();
                StringBuilder args = new StringBuilder();
                args.Append("-m ").Append(Q(model.Path));
                args.Append(" --host 127.0.0.1 --port 0");
                args.Append(" --no-webui --no-slots --offline --jinja -np 1 -fa auto");
                args.Append(" --reasoning-format deepseek --cors-origins localhost");
                if (ctx > 0) args.Append(" -c ").Append(ctx);
                else args.Append(" --fit on --fit-ctx ").Append(MinAutoContext);
                if (modern) args.Append(" --cache-ram ").Append(PromptCacheMiB());
                if (!string.IsNullOrEmpty(kvQuant) && (kvQuant == "q8_0" || kvQuant == "q4_0")) args.Append(" -ctk ").Append(kvQuant).Append(" -ctv ").Append(kvQuant);
                if (model.ProjectorPath != null && File.Exists(model.ProjectorPath)) args.Append(" --mmproj ").Append(Q(model.ProjectorPath));
                args.Append(" --alias local");
                ProcessStartInfo psi = new ProcessStartInfo(exe, args.ToString());
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                List<string> drop = new List<string>();
                foreach (string k in psi.EnvironmentVariables.Keys)
                {
                    string u = k.ToUpperInvariant();
                    if (u.StartsWith("LLAMA_") || u.StartsWith("GGML_") || u.StartsWith("HF_") || u == "HUGGING_FACE_HUB_TOKEN" || u.StartsWith("MTMD_")) drop.Add(k);
                }
                foreach (string k in drop) psi.EnvironmentVariables.Remove(k);
                psi.EnvironmentVariables["LLAMA_API_KEY"] = key;
                Process p = new Process();
                p.StartInfo = psi;
                p.EnableRaisingEvents = true;
                lock (log) log.Length = 0;
                int gen;
                lock (gate) gen = ++generation;
                listenPort = 0;
                p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { AddLog(e.Data, gen); };
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { AddLog(e.Data, gen); };
                p.Exited += delegate
                {
                    bool mine;
                    lock (gate) mine = generation == gen && proc == p;
                    if (mine && !Starting) LastError = CrashReason(LogTail(40));
                };
                if (!p.Start()) { Starting = false; return "Could not start the engine."; }
                try { p.StandardInput.Close(); } catch { }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                AttachJob(p);
                lock (gate)
                {
                    proc = p;
                    Port = 0;
                    ApiKey = key;
                    BaseUrl = "";
                    ModelPath = model.Path;
                    Backend = backend;
                    Context = ctx;
                }
                Dictionary<string, string> h = new Dictionary<string, string>();
                h["Authorization"] = "Bearer " + key;
                DateTime start = DateTime.UtcNow;
                int port = 0;
                while (true)
                {
                    if (ct != null && ct.IsCancelled) { Stop(); Starting = false; return "Cancelled."; }
                    if (p.HasExited)
                    {
                        Starting = false;
                        string why = Explain(LogTail(40), ExitCode(p));
                        LastError = why;
                        return why;
                    }
                    int lp = listenPort;
                    if (lp > 0 && Http.Get("http://127.0.0.1:" + lp + "/health", null, 3000, ct).Status == 200)
                    {
                        port = lp;
                        break;
                    }
                    if ((DateTime.UtcNow - start).TotalMinutes > 15) { Stop(); Starting = false; return "The model took too long to load."; }
                    if (progress != null) progress("Loading " + model.DisplayName);
                    Thread.Sleep(lp > 0 ? 300 : 500);
                }
                lock (gate)
                {
                    Port = port;
                    BaseUrl = "http://127.0.0.1:" + port + "/v1";
                }
                HttpResult props = Http.Get("http://127.0.0.1:" + port + "/props", h, 5000, ct);
                int n = Json.Int(props.Json, "default_generation_settings", "n_ctx");
                if (n <= 0) n = Json.Int(props.Json, "n_ctx");
                if (n > 0) Context = n;
                Starting = false;
                return null;
            }
            catch (Exception ex)
            {
                Starting = false;
                Stop();
                if (BlockedByWindows(ex))
                    return "Windows blocked the engine program (llama-server.exe). Smart App Control or an application control policy on this PC does not allow it to run. Cloud models still work.";
                return "Could not start the engine: " + ex.Message;
            }
        }

        public static bool BlockedByWindows(Exception ex)
        {
            System.ComponentModel.Win32Exception w = ex as System.ComponentModel.Win32Exception;
            if (w == null) return false;
            int c = w.NativeErrorCode;
            return c == 225 || c == 226 || c == 1260 || (c >= 4550 && c <= 4599);
        }

        static int ExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return 0; }
        }

        static string CrashReason(string tail)
        {
            string low = (tail ?? "").ToLowerInvariant();
            if (low.Contains("out of memory") || low.Contains("failed to allocate") || low.Contains("cudamalloc failed")) return "it ran out of memory";
            string[] lines = (tail ?? "").Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string l = lines[i].Trim();
                string ll = l.ToLowerInvariant();
                if (l.Length > 0 && (ll.Contains("error") || ll.Contains("assert") || ll.Contains("failed") || ll.Contains("abort")))
                    return l.Length > 200 ? l.Substring(0, 200) + "..." : l;
            }
            return null;
        }

        static string Explain(string tail, int code)
        {
            if ((tail ?? "").Trim().Length == 0)
            {
                uint u = unchecked((uint)code);
                string hex = "0x" + u.ToString("X8");
                if (u == 0xC0000135) return "The engine closed right away (" + hex + "): a file it needs is missing, usually part of the graphics driver or the Visual C++ runtime. Try the Vulkan or CPU build from the Engine button in the model library.";
                if (u == 0xC000001D) return "The engine closed right away (" + hex + "): this processor doesn't support the instructions this build uses. Try the CPU build from the Engine button in the model library.";
                return "The engine closed right away" + (code != 0 ? " (exit code " + hex + ")" : "") + " without saying why. Try another build from the Engine button in the model library.";
            }
            return Explain(tail);
        }

        static string Explain(string tail)
        {
            string low = tail.ToLowerInvariant();
            if (low.Contains("out of memory") || low.Contains("failed to allocate") || low.Contains("cudamalloc failed"))
                return "The model doesn't fit in memory. Pick a smaller quantization, or lower the context size in Settings.";
            if (low.Contains("unknown model architecture") || low.Contains("unsupported model"))
                return "This engine build doesn't support this model's architecture yet.";
            if (low.Contains("failed to load model") || low.Contains("error loading model"))
                return "The engine could not load this model file (it may be incomplete or corrupt).";
            if (low.Contains("cuda") && (low.Contains("driver") || low.Contains("insufficient")))
                return "The NVIDIA driver is too old for this engine build. Update the driver, or install the Vulkan build in Settings.";
            string last = tail.Trim();
            if (last.Length > 400) last = last.Substring(last.Length - 400);
            return "The engine stopped while loading the model:\n" + last;
        }

        void AddLog(string line, int gen)
        {
            if (line == null) return;
            lock (log)
            {
                log.Append(line).Append('\n');
                if (log.Length > 200000) log.Remove(0, log.Length - 100000);
            }
            if (line.IndexOf("listening on", StringComparison.Ordinal) < 0) return;
            System.Text.RegularExpressions.Match m = Listening.Match(line);
            int v;
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out v) || v <= 0 || v > 65535) return;
            lock (gate) if (gen == generation) listenPort = v;
        }

        public string LogTail(int lines)
        {
            string s;
            lock (log) s = log.ToString();
            string[] all = s.Split('\n');
            int from = Math.Max(0, all.Length - lines);
            return string.Join("\n", all, from, all.Length - from);
        }

        public void Stop()
        {
            Process p;
            lock (gate) { p = proc; proc = null; }
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.WaitForExit(3000); } catch { }
            try { p.Dispose(); } catch { }
        }

        public Dictionary<string, object> SamplingFor(string path)
        {
            LocalModel m = null;
            foreach (LocalModel x in ModelLibrary.Scan(false)) if (string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) { m = x; break; }
            Dictionary<string, object> s = new Dictionary<string, object>();
            if (m == null) return s;
            if (m.Temp.HasValue) s["temperature"] = m.Temp.Value;
            if (m.TopP.HasValue) s["top_p"] = m.TopP.Value;
            if (m.TopK.HasValue) s["top_k"] = m.TopK.Value;
            if (m.MinP.HasValue) s["min_p"] = m.MinP.Value;
            if (s.Count == 0 && (m.Architecture ?? "").StartsWith("qwen", StringComparison.OrdinalIgnoreCase))
            {
                s["temperature"] = 0.6;
                s["top_p"] = 0.95;
                s["top_k"] = 20;
                s["min_p"] = 0.0;
            }
            return s;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr attrs, string name);
        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll")]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic;
            public IO_COUNTERS Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        static readonly object jobGate = new object();
        static IntPtr sharedJob = IntPtr.Zero;

        public static IntPtr KillOnCloseJob()
        {
            lock (jobGate)
            {
                if (sharedJob != IntPtr.Zero) return sharedJob;
                try
                {
                    IntPtr j = CreateJobObject(IntPtr.Zero, null);
                    if (j == IntPtr.Zero) return IntPtr.Zero;
                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.Basic.LimitFlags = 0x2000;
                    int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(info, ptr, false);
                        if (!SetInformationJobObject(j, 9, ptr, (uint)size)) return IntPtr.Zero;
                    }
                    finally { Marshal.FreeHGlobal(ptr); }
                    sharedJob = j;
                    return j;
                }
                catch { return IntPtr.Zero; }
            }
        }

        static void AttachJob(Process p)
        {
            try
            {
                IntPtr j = KillOnCloseJob();
                if (j != IntPtr.Zero) AssignProcessToJobObject(j, p.Handle);
            }
            catch { }
        }
    }
}
