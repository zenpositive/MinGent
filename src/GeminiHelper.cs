using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MinGent
{
    class ChromiumBrowser
    {
        public string Name = "", Exe = "";
        public bool Default;
    }

    static class GeminiBridge
    {
        class Client
        {
            public TcpClient Tcp;
            public NetworkStream Net;
            public readonly object SendLock = new object();
            public volatile bool Ok, Closed;
            public DateTime Seen = DateTime.UtcNow;
            public string Agent = "", Version = "";
        }

        class Waiter
        {
            public Dictionary<string, object> Reply;
            public bool Done;
        }

        class Link : IGeminiTransport
        {
            public bool Connected { get { return GeminiBridge.Connected; } }

            public string Session(bool create, bool reload, CancelToken ct, out GeminiSession s)
            {
                s = null;
                Dictionary<string, object> reply;
                string err = Ask(Json.O("type", "session", "create", create, "reload", reload), 75000, ct, out reply);
                if (err != null) return err;
                if (!Json.Bool(reply, "ok")) return GeminiWeb.Explain(Json.Str(reply, "why"));
                s = new GeminiSession();
                s.Host = Json.Str(reply, "host") ?? "";
                s.Path = Json.Str(reply, "path") ?? "";
                s.At = Json.Str(reply, "at") ?? "";
                s.Build = Json.Str(reply, "build") ?? "";
                s.Sid = Json.Str(reply, "sid") ?? "";
                s.Push = Json.Str(reply, "push") ?? "";
                return null;
            }

            public GeminiJob Start(string url, Dictionary<string, string> headers, string body, GeminiFile file)
            {
                string id = NextId();
                GeminiJob job = new GeminiJob(id);
                Dictionary<string, object> h = new Dictionary<string, object>();
                foreach (KeyValuePair<string, string> kv in headers) h[kv.Key] = kv.Value;
                Dictionary<string, object> m = Json.O("type", "start", "id", id, "url", url, "headers", h, "body", body ?? "");
                if (file != null) m["file"] = Json.O("name", file.Name, "type", file.Type, "data", file.Data);
                lock (gate) jobs[id] = job;
                if (!SendCurrent(Json.Write(m)))
                {
                    lock (gate) jobs.Remove(id);
                    GeminiChunk gone = new GeminiChunk();
                    gone.Done = true;
                    gone.Error = "The Gemini helper is not connected.";
                    job.Push(gone);
                }
                return job;
            }

            public void End(GeminiJob job)
            {
                if (job == null) return;
                lock (gate) jobs.Remove(job.Id);
                SendCurrent(Json.Write(Json.O("type", "end", "id", job.Id)));
            }
        }

        const string Magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        const int MaxMessage = 64 * 1024 * 1024;
        static readonly object gate = new object();
        static readonly Dictionary<string, GeminiJob> jobs = new Dictionary<string, GeminiJob>();
        static readonly Dictionary<string, Waiter> waiters = new Dictionary<string, Waiter>();
        static TcpListener listener;
        static int port;
        static string token = "", version = "";
        static Client current;
        static long ids;
        static bool askedReload;
        public static readonly IGeminiTransport Transport = new Link();
        public static event Action Changed;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        static void NoInherit(Socket s)
        {
            try { SetHandleInformation(s.Handle, 1, 0); } catch { }
        }

        public static TcpListener Listen(int onPort)
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, onPort);
            NoInherit(l.Server);
            l.Start();
            return l;
        }

        public static int Port { get { return listener != null ? port : 0; } }
        public static bool Running { get { return listener != null; } }

        public static bool Connected
        {
            get
            {
                Client c = current;
                return c != null && c.Ok && !c.Closed && (DateTime.UtcNow - c.Seen).TotalSeconds < 50;
            }
        }

        public static string Agent { get { Client c = current; return c != null && c.Ok ? c.Agent : ""; } }

        static string NextId() { return "g" + Interlocked.Increment(ref ids).ToString(CultureInfo.InvariantCulture); }

        public static string Start(int onPort, string secret, string helperVersion)
        {
            lock (gate)
            {
                if (listener != null && port == onPort && token == secret) { version = helperVersion ?? ""; return null; }
            }
            Stop();
            try
            {
                TcpListener l = Listen(onPort);
                lock (gate)
                {
                    listener = l;
                    port = onPort;
                    token = secret ?? "";
                    version = helperVersion ?? "";
                    askedReload = false;
                }
                Thread t = new Thread(delegate () { Accept(l); });
                t.IsBackground = true;
                t.Name = "MinGent Gemini helper link";
                t.Start();
                return null;
            }
            catch (Exception ex)
            {
                string number = onPort.ToString(CultureInfo.InvariantCulture);
                SocketException taken = ex as SocketException;
                if (taken != null && taken.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    return "Another program on this PC uses port " + number + ", which the Gemini helper needs to reach MinGent. Open Settings > Models and set the helper up again: that moves it to a free port.";
                return "MinGent could not open port " + number + " on this PC for the Gemini helper: " + ex.Message;
            }
        }

        public static void Stop()
        {
            TcpListener l;
            Client c;
            lock (gate)
            {
                l = listener;
                listener = null;
                c = current;
            }
            if (l != null) { try { l.Stop(); } catch { } }
            if (c != null) Drop(c);
        }

        static void Accept(TcpListener l)
        {
            while (true)
            {
                TcpClient tcp;
                try { tcp = l.AcceptTcpClient(); }
                catch { return; }
                NoInherit(tcp.Client);
                TcpClient one = tcp;
                Thread t = new Thread(delegate () { Serve(one); });
                t.IsBackground = true;
                t.Name = "MinGent Gemini helper";
                t.Start();
            }
        }

        static void Serve(TcpClient tcp)
        {
            Client c = new Client();
            c.Tcp = tcp;
            try
            {
                tcp.NoDelay = true;
                c.Net = tcp.GetStream();
                c.Net.ReadTimeout = 8000;
                if (!Handshake(c.Net)) return;
                Dictionary<string, object> hello = Json.TryParse(Read(c) ?? "") as Dictionary<string, object>;
                string theirs = Json.Str(hello, "nonce") ?? "";
                if (hello == null || Json.Str(hello, "type") != "hello" || theirs.Length < 16 || theirs.Length > 128) return;
                string secret;
                lock (gate) secret = token;
                if (secret.Length == 0) return;
                byte[] raw = new byte[16];
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(raw);
                string ours = Hex(raw);
                Send(c, Json.Write(Json.O("type", "welcome", "proof", Mac(secret, "server:" + theirs), "nonce", ours)));
                Dictionary<string, object> proof = Json.TryParse(Read(c) ?? "") as Dictionary<string, object>;
                if (proof == null || Json.Str(proof, "type") != "proof" || !Equal(Json.Str(proof, "proof") ?? "", Mac(secret, "helper:" + ours))) return;
                c.Agent = Json.Str(hello, "agent") ?? "";
                c.Version = Json.Str(hello, "version") ?? "";
                bool reload;
                lock (gate)
                {
                    if (listener == null || (current != null && current != c && Connected)) return;
                    reload = version.Length > 0 && c.Version != version && !askedReload;
                    if (reload) askedReload = true;
                    c.Ok = true;
                    c.Seen = DateTime.UtcNow;
                    current = c;
                }
                if (reload) Send(c, Json.Write(Json.O("type", "reload")));
                Notify();
                c.Net.ReadTimeout = 65000;
                while (true)
                {
                    string text = Read(c);
                    if (text == null) return;
                    c.Seen = DateTime.UtcNow;
                    Dictionary<string, object> m = Json.TryParse(text) as Dictionary<string, object>;
                    if (m != null) On(m);
                }
            }
            catch { }
            finally { Drop(c); }
        }

        static string Hex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static string Mac(string secret, string text)
        {
            using (HMACSHA256 mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret))) return Hex(mac.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        static bool Equal(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        static void Notify()
        {
            Action a = Changed;
            if (a != null) { try { a(); } catch { } }
        }

        static void Drop(Client c)
        {
            bool was;
            List<GeminiJob> lostJobs = new List<GeminiJob>();
            List<Waiter> lostWaiters = new List<Waiter>();
            lock (gate)
            {
                was = current == c;
                if (was)
                {
                    current = null;
                    lostJobs.AddRange(jobs.Values);
                    jobs.Clear();
                    lostWaiters.AddRange(waiters.Values);
                    waiters.Clear();
                }
            }
            c.Closed = true;
            try { if (c.Net != null) c.Net.Close(); } catch { }
            try { c.Tcp.Close(); } catch { }
            foreach (GeminiJob j in lostJobs)
            {
                GeminiChunk gone = new GeminiChunk();
                gone.Done = true;
                gone.Error = "The connection to the Gemini helper was lost. Try again.";
                j.Push(gone);
            }
            foreach (Waiter w in lostWaiters) lock (w) { w.Done = true; Monitor.PulseAll(w); }
            if (was) Notify();
        }

        static bool Handshake(NetworkStream s)
        {
            StringBuilder head = new StringBuilder();
            while (head.Length < 8192)
            {
                int b = s.ReadByte();
                if (b < 0) return false;
                head.Append((char)b);
                int n = head.Length;
                if (n >= 4 && head[n - 1] == '\n' && head[n - 2] == '\r' && head[n - 3] == '\n' && head[n - 4] == '\r') break;
            }
            string[] lines = head.ToString().Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, string> h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) h[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            string key, host, origin, upgrade;
            h.TryGetValue("Sec-WebSocket-Key", out key);
            h.TryGetValue("Host", out host);
            h.TryGetValue("Origin", out origin);
            h.TryGetValue("Upgrade", out upgrade);
            string at = ":" + port.ToString(CultureInfo.InvariantCulture);
            bool ok = lines.Length > 0 && lines[0].StartsWith("GET /helper ", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(key) && string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase)
                && (host == "127.0.0.1" + at || host == "localhost" + at)
                && origin != null && origin.StartsWith("chrome-extension://", StringComparison.Ordinal);
            if (!ok)
            {
                byte[] no = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
                s.Write(no, 0, no.Length);
                return false;
            }
            string accept;
            using (SHA1 sha = SHA1.Create()) accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + Magic)));
            byte[] yes = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
            s.Write(yes, 0, yes.Length);
            return true;
        }

        static byte[] Exact(NetworkStream s, int n)
        {
            byte[] b = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = s.Read(b, got, n - got);
                if (r <= 0) return null;
                got += r;
            }
            return b;
        }

        static string Read(Client c)
        {
            NetworkStream s = c.Net;
            MemoryStream whole = null;
            while (true)
            {
                byte[] head = Exact(s, 2);
                if (head == null) return null;
                bool fin = (head[0] & 0x80) != 0, masked = (head[1] & 0x80) != 0;
                int op = head[0] & 0x0F;
                long length = head[1] & 0x7F;
                if (length == 126)
                {
                    byte[] ext = Exact(s, 2);
                    if (ext == null) return null;
                    length = (ext[0] << 8) | ext[1];
                }
                else if (length == 127)
                {
                    byte[] ext = Exact(s, 8);
                    if (ext == null) return null;
                    length = 0;
                    for (int i = 0; i < 8; i++) length = (length << 8) | ext[i];
                }
                if (!masked || length < 0 || length > MaxMessage) return null;
                byte[] mask = Exact(s, 4);
                byte[] data = length == 0 ? new byte[0] : Exact(s, (int)length);
                if (mask == null || data == null) return null;
                for (int i = 0; i < data.Length; i++) data[i] ^= mask[i & 3];
                if (op == 8) return null;
                if (op == 9) { Frame(c, 0x8A, data); continue; }
                if (op == 10) continue;
                if (op != 0 && op != 1) return null;
                if (fin && whole == null) return Encoding.UTF8.GetString(data);
                if (whole == null) whole = new MemoryStream();
                if (whole.Length + data.Length > MaxMessage) return null;
                whole.Write(data, 0, data.Length);
                if (fin) return Encoding.UTF8.GetString(whole.ToArray());
            }
        }

        static void Frame(Client c, byte first, byte[] data)
        {
            byte[] head;
            if (data.Length < 126) head = new byte[] { first, (byte)data.Length };
            else if (data.Length < 65536) head = new byte[] { first, 126, (byte)(data.Length >> 8), (byte)data.Length };
            else
            {
                head = new byte[10];
                head[0] = first;
                head[1] = 127;
                long n = data.Length;
                for (int i = 9; i >= 2; i--) { head[i] = (byte)n; n >>= 8; }
            }
            lock (c.SendLock)
            {
                c.Net.Write(head, 0, head.Length);
                if (data.Length > 0) c.Net.Write(data, 0, data.Length);
                c.Net.Flush();
            }
        }

        static void Send(Client c, string text) { Frame(c, 0x81, Encoding.UTF8.GetBytes(text)); }

        static bool SendCurrent(string text)
        {
            Client c = current;
            if (c == null || !c.Ok || c.Closed) return false;
            try { Send(c, text); return true; }
            catch { Drop(c); return false; }
        }

        static void On(Dictionary<string, object> m)
        {
            string type = Json.Str(m, "type"), id = Json.Str(m, "id") ?? "";
            if (type == "reply")
            {
                Waiter w;
                lock (gate) waiters.TryGetValue(id, out w);
                if (w != null) lock (w) { w.Reply = m; w.Done = true; Monitor.PulseAll(w); }
                return;
            }
            if (type != "chunk") return;
            GeminiJob job;
            bool done = Json.Bool(m, "done");
            lock (gate)
            {
                jobs.TryGetValue(id, out job);
                if (job != null && done) jobs.Remove(id);
            }
            if (job == null) return;
            GeminiChunk c = new GeminiChunk();
            c.Text = Json.Str(m, "t") ?? "";
            c.Done = done;
            c.Status = Json.Int(m, "status");
            c.Error = Json.Str(m, "err");
            job.Push(c);
        }

        static string Ask(Dictionary<string, object> message, int timeoutMs, CancelToken ct, out Dictionary<string, object> reply)
        {
            reply = null;
            string id = NextId();
            message["id"] = id;
            Waiter w = new Waiter();
            lock (gate) waiters[id] = w;
            try
            {
                if (!SendCurrent(Json.Write(message))) return "The Gemini helper is not connected.";
                DateTime until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                lock (w)
                {
                    while (!w.Done)
                    {
                        if (ct != null && ct.IsCancelled) return "stopped";
                        if (DateTime.UtcNow > until) return "The Gemini helper did not answer in time.";
                        Monitor.Wait(w, 200);
                    }
                }
                if (w.Reply == null) return "The connection to the Gemini helper was lost. Try again.";
                reply = w.Reply;
                return null;
            }
            finally { lock (gate) waiters.Remove(id); }
        }
    }

    static class GeminiHelper
    {
        public const string FolderName = "GeminiHelper";
        public const string ExtensionName = "MinGent Gemini helper";
        public const string ExtensionsPage = "chrome://extensions";
        static readonly string[] Bundle = { "manifest.json", "background.js", "content.js", "icon48.png", "icon128.png" };
        public static string SourceDir { get; set; }
        public static string BrowserExe = "";

        static readonly string[][] Known = {
            new string[] { "Brave", "brave.exe", "Brave", @"BraveSoftware\Brave-Browser\Application\brave.exe" },
            new string[] { "Chrome", "chrome.exe", "ChromeHTML", @"Google\Chrome\Application\chrome.exe" },
            new string[] { "Edge", "msedge.exe", "MSEdge", @"Microsoft\Edge\Application\msedge.exe" },
            new string[] { "Vivaldi", "vivaldi.exe", "Vivaldi", @"Vivaldi\Application\vivaldi.exe" },
            new string[] { "Opera GX", "opera.exe", "Opera GX", @"Programs\Opera GX\opera.exe" },
            new string[] { "Opera", "opera.exe", "Opera", @"Programs\Opera\opera.exe" },
            new string[] { "Chromium", "chrome.exe", "Chromium", @"Chromium\Application\chrome.exe" },
        };

        public const string NoBrowser = "The helper needs a Chromium-based browser: Brave, Chrome, Edge, Vivaldi or Opera. None was found on this PC.";

        public static string Dir { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), FolderName); } }
        static string ConfigFile { get { return Path.Combine(Dir, "config.json"); } }
        static string ModelsFile { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), "GeminiModels.json"); } }

        public static bool SetUp
        {
            get { try { return File.Exists(ConfigFile); } catch { return false; } }
        }

        public static string NameOf(string exe)
        {
            string path = (exe ?? "").Replace('/', '\\');
            foreach (string[] k in Known)
                if (path.EndsWith("\\" + k[3], StringComparison.OrdinalIgnoreCase)) return k[0];
            string file = path.Substring(path.LastIndexOf('\\') + 1);
            foreach (string[] k in Known)
                if (string.Equals(file, k[1], StringComparison.OrdinalIgnoreCase)) return k[0];
            return "your browser";
        }

        public static string BrowserName { get { return NameOf(BrowserExe); } }

        static byte[] Bundled(string name)
        {
            try
            {
                using (Stream s = typeof(GeminiHelper).Assembly.GetManifestResourceStream("helper." + name))
                    if (s != null)
                    {
                        byte[] b = new byte[s.Length];
                        int got = 0;
                        while (got < b.Length)
                        {
                            int r = s.Read(b, got, b.Length - got);
                            if (r <= 0) break;
                            got += r;
                        }
                        if (got == b.Length) return b;
                    }
            }
            catch { }
            try
            {
                string dir = SourceDir ?? (Program.AppDir != null ? Path.Combine(Path.Combine(Program.AppDir, "src"), "helper") : null);
                string path = dir != null ? Path.Combine(dir, name) : null;
                if (path != null && File.Exists(path)) return File.ReadAllBytes(path);
            }
            catch { }
            return null;
        }

        public static string Version
        {
            get
            {
                byte[] manifest = Bundled("manifest.json");
                return manifest == null ? "" : Json.Str(Json.TryParse(Encoding.UTF8.GetString(manifest)), "version") ?? "";
            }
        }

        static bool Same(string path, byte[] bytes)
        {
            try
            {
                if (!File.Exists(path)) return false;
                byte[] have = File.ReadAllBytes(path);
                if (have.Length != bytes.Length) return false;
                for (int i = 0; i < have.Length; i++) if (have[i] != bytes[i]) return false;
                return true;
            }
            catch { return false; }
        }

        public static bool ReadConfig(out int port, out string token)
        {
            port = 0;
            token = "";
            try
            {
                if (!File.Exists(ConfigFile)) return false;
                string text = File.ReadAllText(ConfigFile);
                Match p = Regex.Match(text, @"""port""\s*:\s*(\d{2,5})"), t = Regex.Match(text, @"""token""\s*:\s*""([0-9a-f]{16,64})""");
                if (!p.Success || !t.Success) return false;
                port = int.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture);
                token = t.Groups[1].Value;
                return port > 0 && port < 65536;
            }
            catch { return false; }
        }

        static int FreePort()
        {
            for (int p = 47810; p < 47830; p++)
            {
                try
                {
                    GeminiBridge.Listen(p).Stop();
                    return p;
                }
                catch { }
            }
            return 0;
        }

        public static string WriteFiles(string testOrigin)
        {
            if (Program.Erased) return "MinGent's data is being erased.";
            try
            {
                Directory.CreateDirectory(Dir);
                foreach (string name in Bundle)
                {
                    byte[] bytes = Bundled(name);
                    if (bytes == null) return "The helper's files are missing from this copy of MinGent. Download MinGent again, or build it from the full folder.";
                    if (name == "manifest.json" && testOrigin != null)
                        bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"https://gemini.google.com/*\"", "\"https://gemini.google.com/*\", " + Json.Quote(testOrigin + "/*")));
                    string path = Path.Combine(Dir, name);
                    if (!Same(path, bytes)) File.WriteAllBytes(path, bytes);
                }
                int port;
                string token;
                if (!ReadConfig(out port, out token))
                {
                    port = GeminiBridge.Running ? GeminiBridge.Port : FreePort();
                    if (port == 0) return "MinGent found no free port on this PC for the helper.";
                    byte[] raw = new byte[16];
                    using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(raw);
                    StringBuilder hex = new StringBuilder();
                    foreach (byte b in raw) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    token = hex.ToString();
                }
                Dictionary<string, object> config = Json.O("port", port, "token", token);
                if (testOrigin != null) config["origin"] = testOrigin;
                byte[] text = new UTF8Encoding(false).GetBytes(Json.Write(config) + "\n");
                if (!Same(ConfigFile, text)) File.WriteAllBytes(ConfigFile, text);
                return null;
            }
            catch (Exception ex) { return "The helper's files could not be written to " + Dir + ": " + ex.Message; }
        }

        static bool MovePort(int to)
        {
            try
            {
                string text = File.ReadAllText(ConfigFile);
                string moved = Regex.Replace(text, @"(""port""\s*:\s*)\d{2,5}", "${1}" + to.ToString(CultureInfo.InvariantCulture));
                if (moved == text) return false;
                File.WriteAllText(ConfigFile, moved, new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        public static string EnsureBridge(bool mayMove)
        {
            int port;
            string token;
            if (!ReadConfig(out port, out token)) return "The helper is not set up.";
            string err = GeminiBridge.Start(port, token, Version);
            if (err != null && mayMove)
            {
                int free = FreePort();
                if (free != 0 && MovePort(free)) err = GeminiBridge.Start(free, token, Version);
            }
            if (err == null) GeminiWeb.Transport = GeminiBridge.Transport;
            return err;
        }

        public static void Apply(bool enabled, string browserExe)
        {
            BrowserExe = browserExe ?? "";
            GeminiWeb.Enabled = enabled;
            if (enabled && SetUp && !Program.Erased)
            {
                WriteFiles(null);
                EnsureBridge(false);
            }
            else GeminiBridge.Stop();
        }

        public static void Remove()
        {
            GeminiBridge.Stop();
            GeminiWeb.Forget();
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
            try { if (File.Exists(ModelsFile)) File.Delete(ModelsFile); } catch { }
        }

        static string RegText(RegistryKey root, string path, string name)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(path))
                    return k == null ? null : k.GetValue(name) as string;
            }
            catch { return null; }
        }

        static string ExeIn(string command)
        {
            string c = (command ?? "").Trim();
            if (c.Length == 0) return null;
            if (c[0] == '"')
            {
                int end = c.IndexOf('"', 1);
                return end > 1 ? c.Substring(1, end - 1) : null;
            }
            int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? c.Substring(0, exe + 4) : null;
        }

        static string DefaultBrowserExe()
        {
            string prog = RegText(Registry.CurrentUser, @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", "ProgId");
            if (string.IsNullOrEmpty(prog)) return null;
            return ExeIn(RegText(Registry.ClassesRoot, prog + @"\shell\open\command", null));
        }

        public static List<ChromiumBrowser> Browsers()
        {
            List<ChromiumBrowser> found = new List<ChromiumBrowser>();
            if (Path.DirectorySeparatorChar != '\\') return found;
            List<string> roots = new List<string>();
            foreach (string v in new string[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "LOCALAPPDATA" })
            {
                string dir = null;
                try { dir = Environment.GetEnvironmentVariable(v); } catch { }
                if (!string.IsNullOrEmpty(dir) && !roots.Contains(dir)) roots.Add(dir);
            }
            foreach (string[] k in Known)
            {
                string exe = null;
                foreach (string root in roots)
                {
                    string path = Path.Combine(root, k[3]);
                    try { if (File.Exists(path)) { exe = path; break; } } catch { }
                }
                if (exe == null && k[0] != "Chromium" && k[0] != "Opera GX")
                    foreach (RegistryKey hive in new RegistryKey[] { Registry.CurrentUser, Registry.LocalMachine })
                    {
                        string path = ExeIn(RegText(hive, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + k[1], null));
                        try { if (path != null && File.Exists(path) && NameOf(path) == k[0]) { exe = path; break; } } catch { }
                    }
                if (exe == null) continue;
                ChromiumBrowser b = new ChromiumBrowser();
                b.Name = k[0];
                b.Exe = exe;
                found.Add(b);
            }
            string usual = DefaultBrowserExe();
            if (usual != null)
            {
                ChromiumBrowser match = found.Find(delegate (ChromiumBrowser b) { return string.Equals(b.Exe, usual, StringComparison.OrdinalIgnoreCase); });
                if (match == null && NameOf(usual) != "your browser")
                {
                    bool there;
                    try { there = File.Exists(usual); } catch { there = false; }
                    if (there)
                    {
                        match = new ChromiumBrowser();
                        match.Name = NameOf(usual);
                        match.Exe = usual;
                        found.Add(match);
                    }
                }
                if (match != null)
                {
                    match.Default = true;
                    found.Remove(match);
                    found.Insert(0, match);
                }
            }
            return found;
        }

        public static bool BrowserRunning()
        {
            string name = Path.GetFileNameWithoutExtension(BrowserExe ?? "");
            if (name.Length == 0) return false;
            try
            {
                Process[] all = Process.GetProcessesByName(name);
                foreach (Process p in all) p.Dispose();
                return all.Length > 0;
            }
            catch { return false; }
        }

        public static string StartBrowser(bool aside)
        {
            string exe = BrowserExe ?? "";
            bool there;
            try { there = exe.Length > 0 && Path.IsPathRooted(exe) && File.Exists(exe); } catch { there = false; }
            if (!there) return "MinGent no longer finds the browser the helper was set up in. Set up the helper again in Settings.";
            try
            {
                IntPtr front = aside ? AppControl.OwnFront() : IntPtr.Zero;
                ProcessStartInfo psi = new ProcessStartInfo(exe);
                psi.UseShellExecute = false;
                using (Process.Start(psi)) { }
                if (aside) Aside(exe, front);
                return null;
            }
            catch (Exception ex) { return BrowserName + " could not be started: " + ex.Message; }
        }

        static void Aside(string exe, IntPtr front)
        {
            Thread t = new Thread(delegate ()
            {
                DateTime until = DateTime.UtcNow.AddSeconds(25), first = DateTime.MaxValue;
                while (DateTime.UtcNow < until && (first == DateTime.MaxValue || (DateTime.UtcNow - first).TotalSeconds < 6))
                {
                    Thread.Sleep(100);
                    foreach (IntPtr h in AppControl.OpenWindowsOf(exe))
                    {
                        AppControl.Tuck(h);
                        if (first == DateTime.MaxValue) first = DateTime.UtcNow;
                    }
                    if (first != DateTime.MaxValue) AppControl.GiveBack(front, exe);
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        public static void SaveModels(GeminiAccount a)
        {
            if (a == null || Program.Erased || Program.DataDir == null || !SetUp) return;
            try
            {
                List<object> models = new List<object>();
                foreach (GeminiModel m in a.Models)
                {
                    List<object> steps = new List<object>();
                    foreach (int id in m.Steps) steps.Add(id);
                    models.Add(Json.O("id", m.Id, "hex", m.Hex, "category", m.Category, "version", m.Version, "tagline", m.Tagline, "number", m.Number, "levels", m.Levels, "steps", steps));
                }
                string text = Json.Write(Json.O("status", a.Status, "capacity", a.Capacity, "field", a.CapacityField, "models", models));
                if (File.Exists(ModelsFile) && File.ReadAllText(ModelsFile) == text) return;
                File.WriteAllText(ModelsFile, text, new UTF8Encoding(false));
            }
            catch { }
        }

        public static GeminiAccount LoadModels()
        {
            try
            {
                if (!File.Exists(ModelsFile)) return null;
                object o = Json.TryParse(File.ReadAllText(ModelsFile));
                List<object> models = Json.List(o, "models");
                if (models == null || models.Count == 0) return null;
                GeminiAccount a = new GeminiAccount();
                a.Status = Json.Int(o, "status");
                a.Guest = a.Status == 1016;
                a.Capacity = Math.Max(1, Json.Int(o, "capacity"));
                a.CapacityField = Json.Int(o, "field") == 13 ? 13 : 12;
                a.When = DateTime.MinValue;
                foreach (object x in models)
                {
                    GeminiModel m = new GeminiModel();
                    m.Id = Json.Str(x, "id") ?? "";
                    m.Hex = Json.Str(x, "hex") ?? "";
                    m.Category = Json.Str(x, "category") ?? "";
                    m.Version = Json.Str(x, "version") ?? "";
                    m.Tagline = Json.Str(x, "tagline") ?? "";
                    m.Number = Json.Int(x, "number");
                    if (Json.Get(x, "levels") is double) m.Levels = Json.Int(x, "levels");
                    List<object> steps = Json.List(x, "steps");
                    if (steps != null)
                        foreach (object id in steps)
                            if (id is double && GeminiModel.StepName((int)(double)id) != null) m.Steps.Add((int)(double)id);
                    if (m.Id.Length > 0 && m.Hex.Length > 0) a.Models.Add(m);
                }
                return a.Models.Count > 0 ? a : null;
            }
            catch { return null; }
        }

        public static string StatusLine(out bool good)
        {
            good = false;
            if (!SetUp) return "Not set up";
            string browser = BrowserName;
            if (!GeminiBridge.Connected) return "Waiting for the helper in " + browser;
            good = true;
            GeminiAccount a = GeminiWeb.Known;
            if (a == null || a.Models.Count == 0) return "Connected to " + browser;
            if (a.Guest) return "Connected to " + browser + ". Not signed in to Gemini there, so only the basic model works";
            return "Connected to " + browser + ". " + a.Models.Count.ToString(CultureInfo.InvariantCulture) + (a.Models.Count == 1 ? " model" : " models");
        }
    }

    class FolderChip : Control
    {
        readonly Theme th;
        readonly string path;
        bool hover, armed;
        Point from;

        public FolderChip(Theme t, string label, string folder)
        {
            th = t;
            path = folder;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Fonts.Semi;
            Text = label;
            Cursor = Cursors.Hand;
            Size = new Size(UI.S(44) + Fonts.Width(label, Font) + UI.S(16), UI.S(38));
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = label + " folder, drag it onto the Extensions page";
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); armed = false; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            armed = true;
            from = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!armed || (e.Button & MouseButtons.Left) == 0) return;
            Size slack = SystemInformation.DragSize;
            if (Math.Abs(e.X - from.X) < slack.Width / 2 && Math.Abs(e.Y - from.Y) < slack.Height / 2) return;
            armed = false;
            try { DoDragDrop(new DataObject(DataFormats.FileDrop, new string[] { path }), DragDropEffects.Copy | DragDropEffects.Link); } catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Chrome);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            int rad = UI.S(8);
            if (!th.HighContrast) Glyphs.FillRound(g, hover ? th.CardHover : th.Card, r, rad);
            Glyphs.Ring(g, hover ? th.Accent : th.CardBorder, r, rad, Math.Max(1f, UI.Scale));
            Glyphs.Draw("folder", g, new Rectangle(UI.S(12), (Height - UI.S(18)) / 2, UI.S(18), UI.S(18)), th.Accent);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(UI.S(38), 0, Width - UI.S(44), Height), th.ChromeFore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    class GeminiSetupDialog : ThemedForm
    {
        readonly Label status;
        readonly System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer said = new System.Windows.Forms.Timer();
        bool checking;
        DateTime checkedAt = DateTime.MinValue;

        public bool Connected { get { return GeminiBridge.Connected; } }

        public GeminiSetupDialog(Theme t, ChromiumBrowser b)
            : base(t, "Gemini helper", 560, 300)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            TopMost = true;
            int pad = UI.S(24), w = ClientSize.Width - pad * 2, y = pad, inset = UI.S(22);
            y = AddText("Add the helper to " + b.Name, Fonts.Semi, false, pad, y, w) + UI.S(10);
            y = AddText("Three steps, and they are yours: no program is allowed to add an extension to a browser by itself.", UI.Font, true, pad, y, w) + UI.S(14);
            y = AddText("1.  In " + b.Name + ", click the address bar, press Ctrl+V and then Enter.", UI.Font, false, pad, y, w) + UI.S(4);
            y = AddText("That opens its Extensions page. The address is already copied.", UI.Font, true, pad + inset, y, w - inset) + UI.S(10);
            y = AddText("2.  On that page, switch on Developer mode.", UI.Font, false, pad, y, w) + UI.S(10);
            y = AddText("3.  Drag this folder onto the page:", UI.Font, false, pad, y, w) + UI.S(8);
            FolderChip chip = new FolderChip(Th, GeminiHelper.FolderName, GeminiHelper.Dir);
            chip.Location = new Point(pad + inset, y);
            Controls.Add(chip);
            y += chip.Height + UI.S(10);
            Button copy = Dialogs.MakeButton("Copy path", Th, false);
            copy.Location = new Point(ClientSize.Width - pad - copy.Width, y);
            string other = "Or click Load unpacked there and paste the folder's path.";
            int otherW = copy.Left - UI.S(12) - pad - inset;
            int otherH = Fonts.Height(other, UI.Font, otherW);
            AddText(other, UI.Font, true, pad + inset, y + Math.Max(0, (copy.Height - otherH) / 2), otherW);
            said.Interval = 1600;
            said.Tick += delegate { said.Stop(); copy.Text = "Copy path"; };
            copy.Click += delegate
            {
                try { Clipboard.SetText(GeminiHelper.Dir); copy.Text = "Copied"; } catch { copy.Text = "Not copied"; }
                said.Stop();
                said.Start();
            };
            Controls.Add(copy);
            y += Math.Max(copy.Height, otherH) + UI.S(14);
            y = AddText("Sign in at gemini.google.com in " + b.Name + " to get your account's models. Without it, only Gemini's basic model works.", UI.Font, true, pad, y, w) + UI.S(16);
            status = Dialogs.MakeLabel("", Th, false);
            status.Font = Fonts.Semi;
            status.SetBounds(pad, y, w, UI.S(22));
            Controls.Add(status);
            y += UI.S(22) + UI.S(18);
            Button again = Dialogs.MakeButton("Open " + b.Name + " again", Th, false);
            Button close = Dialogs.MakeButton("Done", Th, true);
            close.Location = new Point(ClientSize.Width - pad - close.Width, y);
            again.Location = new Point(close.Left - UI.S(8) - again.Width, y);
            close.DialogResult = DialogResult.OK;
            again.Click += delegate
            {
                string err = Open();
                if (err != null) Alert.Show(this, Th, err, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Controls.Add(again);
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
            ClientSize = new Size(ClientSize.Width, y + close.Height + pad);
            AccessibleRole = AccessibleRole.Dialog;
            watch.Interval = 500;
            watch.Tick += delegate { Refresh(false); };
            Refresh(false);
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

        public string Open()
        {
            try { Clipboard.SetText(GeminiHelper.ExtensionsPage); } catch { }
            return GeminiHelper.StartBrowser(false);
        }

        void Refresh(bool force)
        {
            bool good;
            string line = GeminiHelper.StatusLine(out good);
            status.Text = line + (good ? "." : "...");
            status.ForeColor = good ? Th.Accent : Th.ChromeDim;
            if (!good || checking || (!force && (DateTime.UtcNow - checkedAt).TotalSeconds < 20)) return;
            checking = true;
            checkedAt = DateTime.UtcNow;
            Thread t = new Thread(delegate ()
            {
                try { GeminiWeb.Probe(new List<ModelEntry>(), new ProviderState(), new CancelToken()); } catch { }
                checking = false;
            });
            t.IsBackground = true;
            t.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            watch.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            watch.Stop();
            watch.Dispose();
            said.Stop();
            said.Dispose();
            base.OnFormClosed(e);
        }

        static void Go(IWin32Window owner, Theme t, ChromiumBrowser pick, Action<string> done)
        {
            GeminiHelper.BrowserExe = pick.Exe;
            string err = GeminiHelper.WriteFiles(null);
            if (err == null) err = GeminiHelper.EnsureBridge(true);
            if (err == null)
                using (GeminiSetupDialog d = new GeminiSetupDialog(t, pick))
                {
                    err = d.Open();
                    if (err == null) d.ShowDialog(owner);
                }
            if (err != null) { Alert.Show(owner, t, err, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (done != null) done(pick.Exe);
        }

        public static void Begin(IWin32Window owner, Theme t, Control anchor, Action<string> done)
        {
            List<ChromiumBrowser> all = GeminiHelper.Browsers();
            if (all.Count == 0)
            {
                Alert.Show(owner, t, GeminiHelper.NoBrowser, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (all.Count == 1) { Go(owner, t, all[0], done); return; }
            List<Choice> items = new List<Choice>();
            foreach (ChromiumBrowser b in all)
            {
                Choice c = new Choice();
                c.Title = b.Name;
                c.Note = b.Default ? "your usual browser" : "";
                items.Add(c);
            }
            ChoicePopup p = new ChoicePopup(t, items, 0, true, UI.S(240));
            p.Picked += delegate (int i)
            {
                if (i < 0 || i >= all.Count) return;
                ChromiumBrowser pick = all[i];
                anchor.BeginInvoke(new Action(delegate { Go(owner, t, pick, done); }));
            };
            p.Closed += delegate { anchor.BeginInvoke(new Action(delegate { p.Dispose(); })); };
            p.Show(anchor, new Point(0, anchor.Height + UI.S(4)), ToolStripDropDownDirection.BelowRight);
        }
    }
}
