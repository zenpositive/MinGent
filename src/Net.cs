using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace MinGent
{
    class OfflineException : WebException
    {
        public OfflineException(string message) : base(message) { }
    }

    static class Net
    {
        public static volatile bool Offline, KeepLog;
        public const string OfflineMessage = "Offline mode is on (the user turned it on in Settings > Privacy), so MinGent makes no internet connections. Work with what is on this PC, or ask the user to turn offline mode off.";

        public static string Blocked(string url)
        {
            Uri u;
            string host = Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out u) ? u.Host : "";
            return "Offline mode is on (Settings > Privacy), so MinGent did not connect" + (host.Length > 0 ? " to " + host : "") + ".";
        }

        public class Entry
        {
            public string Host = "", What = "";
            public int Count;
            public DateTime First, Last;
        }

        static readonly object gate = new object();
        static readonly List<Entry> log = new List<Entry>();

        public static string LogFile { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), "connections.log"); } }

        public static bool IsLocal(string url)
        {
            Uri u;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out u)) return false;
            if (u.IsFile) return LocalHost(u.Host);
            if (u.IsLoopback) return true;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps && u.Scheme != "ws" && u.Scheme != "wss") return u.Scheme == "about" || u.Scheme == "data" || u.Scheme == "blob";
            return LocalHost(u.Host);
        }

        static bool LocalHost(string h)
        {
            string host = (h ?? "").Trim('[', ']').ToLowerInvariant();
            if (host.Length == 0) return true;
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return Private(ip);
            return host.IndexOf('.') < 0 || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".lan", StringComparison.Ordinal) ||
                   host.EndsWith(".home.arpa", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal) || host.EndsWith(".localhost", StringComparison.Ordinal);
        }

        static bool Private(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return true;
            byte[] b = ip.GetAddressBytes();
            if (b.Length == 4)
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || b[0] == 127;
            if (b.Length == 16) return (b[0] & 0xFE) == 0xFC || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);
            return false;
        }

        public static string Purpose(string host)
        {
            string h = (host ?? "").ToLowerInvariant();
            if (h == "huggingface.co" || h.EndsWith(".huggingface.co", StringComparison.Ordinal) || h.EndsWith(".hf.co", StringComparison.Ordinal) || h == "hf.co") return "Hugging Face (models)";
            if (h == "api.github.com") return "GitHub (llama.cpp releases)";
            if (h == "github.com" || h.EndsWith(".githubusercontent.com", StringComparison.Ordinal)) return "GitHub (engine download)";
            if (h == "api.anthropic.com") return "Claude";
            if (h == "openrouter.ai" || h.EndsWith(".openrouter.ai", StringComparison.Ordinal)) return "OpenRouter";
            if (h == "google.com" || h.EndsWith(".google.com", StringComparison.Ordinal)) return "Web search";
            if (h == "api.openai.com") return "OpenAI";
            if (h == "generativelanguage.googleapis.com") return "Gemini";
            if (h.EndsWith("duckduckgo.com", StringComparison.Ordinal) || h == "www.bing.com" || h == "bing.com") return "Web search";
            if (h.EndsWith("youtube.com", StringComparison.Ordinal) || h == "youtu.be") return "YouTube";
            return "Web";
        }

        public static string Check(string url) { return Check(url, null); }

        public static string Check(string url, string what)
        {
            if (IsLocal(url)) return null;
            if (Offline) return Blocked(url);
            Record(url, what);
            return null;
        }

        public static void Redirected(string from, WebException failed)
        {
            WebResponse r = failed != null ? failed.Response : null;
            if (r != null) Redirected(from, r.ResponseUri);
        }

        public static void Redirected(string from, Uri to)
        {
            Uri f;
            if (to == null || !Uri.TryCreate(from ?? "", UriKind.Absolute, out f)) return;
            if (!string.Equals(f.Host, to.Host, StringComparison.OrdinalIgnoreCase)) Record(to.ToString(), null);
        }

        public static void Record(string url, string what)
        {
            Uri u;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out u) || IsLocal(url)) return;
            string host = u.Host.ToLowerInvariant();
            string label = what ?? Purpose(host);
            DateTime now = DateTime.Now;
            lock (gate)
            {
                Entry e = log.Find(delegate (Entry x) { return x.Host == host && x.What == label; });
                if (e == null)
                {
                    e = new Entry();
                    e.Host = host;
                    e.What = label;
                    e.First = now;
                    log.Add(e);
                    if (log.Count > 2000) log.RemoveAt(0);
                }
                e.Count++;
                e.Last = now;
            }
            if (!KeepLog || Program.Erased) return;
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                FileInfo fi = new FileInfo(LogFile);
                if (fi.Exists && fi.Length > 4L * 1024 * 1024) SecureFile.Erase(LogFile);
                File.AppendAllText(LogFile, now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + host + "  " + label + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        public static List<Entry> Entries()
        {
            lock (gate)
            {
                List<Entry> l = new List<Entry>();
                foreach (Entry e in log)
                {
                    Entry c = new Entry();
                    c.Host = e.Host; c.What = e.What; c.Count = e.Count; c.First = e.First; c.Last = e.Last;
                    l.Add(c);
                }
                l.Sort(delegate (Entry a, Entry b) { return b.Last.CompareTo(a.Last); });
                return l;
            }
        }

        public static void ClearLog()
        {
            lock (gate) log.Clear();
            try { if (File.Exists(LogFile)) SecureFile.Erase(LogFile); } catch { }
        }
    }
}
