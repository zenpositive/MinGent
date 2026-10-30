using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace MinGent
{
    class CancelToken
    {
        volatile bool cancelled;
        readonly List<Action> handlers = new List<Action>();

        public bool IsCancelled { get { return cancelled; } }

        public void Cancel()
        {
            Action[] hs;
            lock (handlers)
            {
                if (cancelled) return;
                cancelled = true;
                hs = handlers.ToArray();
            }
            if (hs.Length == 0) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { foreach (Action a in hs) { try { a(); } catch { } } });
        }

        public void Register(Action a)
        {
            bool now;
            lock (handlers)
            {
                now = cancelled;
                if (!now) handlers.Add(a);
            }
            if (now) { try { a(); } catch { } }
        }

        public void Unregister(Action a) { lock (handlers) handlers.Remove(a); }

        public bool Sleep(int ms)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                if (cancelled) return false;
                Thread.Sleep(50);
            }
            return !cancelled;
        }
    }

    class HttpResult
    {
        public int Status;
        public string Text = "";
        public string Error;
        public bool ConnectFailed, Cancelled;
        public string ContentType = "";
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool Ok { get { return Error == null && Status >= 200 && Status < 300; } }
        public object Json { get { return MinGent.Json.TryParse(Text); } }
    }

    static class Http
    {
        public const string UserAgent = "MinGent/1.0";

        static Http()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; } catch { }
            try { ServicePointManager.Expect100Continue = false; } catch { }
            try { if (ServicePointManager.DefaultConnectionLimit < 16) ServicePointManager.DefaultConnectionLimit = 16; } catch { }
        }

        public static void Init() { }

        public static bool IsLoopback(string url)
        {
            Uri u;
            return Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out u) && u.IsLoopback;
        }

        public static HttpWebRequest Create(string url, string method, int timeoutMs, Dictionary<string, string> headers)
        {
            string off = Net.Check(url);
            if (off != null) throw new OfflineException(off);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = UserAgent;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.KeepAlive = true;
            if (Net.IsLocal(url)) req.Proxy = null;
            if (Net.Offline) req.AllowAutoRedirect = false;
            if (headers != null)
                foreach (KeyValuePair<string, string> kv in headers)
                {
                    if (kv.Value == null) continue;
                    if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) req.ContentType = kv.Value;
                    else if (string.Equals(kv.Key, "Accept", StringComparison.OrdinalIgnoreCase)) req.Accept = kv.Value;
                    else if (string.Equals(kv.Key, "User-Agent", StringComparison.OrdinalIgnoreCase)) req.UserAgent = kv.Value;
                    else req.Headers[kv.Key] = kv.Value;
                }
            return req;
        }

        public static HttpResult Get(string url, Dictionary<string, string> headers, int timeoutMs, CancelToken ct)
        {
            return Send(url, "GET", headers, null, timeoutMs, ct, 8 * 1024 * 1024);
        }

        public static HttpResult PostJson(string url, Dictionary<string, string> headers, string body, int timeoutMs, CancelToken ct)
        {
            Dictionary<string, string> h = headers != null ? new Dictionary<string, string>(headers) : new Dictionary<string, string>();
            h["Content-Type"] = "application/json";
            if (!h.ContainsKey("Accept")) h["Accept"] = "application/json";
            return Send(url, "POST", h, body, timeoutMs, ct, 16 * 1024 * 1024);
        }

        public static HttpResult Send(string url, string method, Dictionary<string, string> headers, string body, int timeoutMs, CancelToken ct, long maxBytes)
        {
            HttpResult r = new HttpResult();
            HttpWebRequest req = null;
            Action abort = null;
            try
            {
                req = Create(url, method, timeoutMs, headers);
                HttpWebRequest captured = req;
                abort = delegate { try { captured.Abort(); } catch { } };
                if (ct != null) ct.Register(abort);
                if (ct != null && ct.IsCancelled) { r.Cancelled = true; return r; }
                if (body != null)
                {
                    byte[] b = Encoding.UTF8.GetBytes(body);
                    req.ContentLength = b.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    Net.Redirected(url, resp.ResponseUri);
                    Fill(r, resp, maxBytes);
                }
            }
            catch (WebException wex)
            {
                if (ct != null && ct.IsCancelled) r.Cancelled = true;
                else ReadError(r, wex);
            }
            catch (Exception ex)
            {
                if (ct != null && ct.IsCancelled) r.Cancelled = true;
                else r.Error = ex.Message;
            }
            finally
            {
                if (ct != null && abort != null) ct.Unregister(abort);
            }
            return r;
        }

        static void Fill(HttpResult r, HttpWebResponse resp, long maxBytes)
        {
            r.Status = (int)resp.StatusCode;
            r.ContentType = resp.ContentType ?? "";
            foreach (string k in resp.Headers.AllKeys) r.Headers[k] = resp.Headers[k];
            using (Stream s = resp.GetResponseStream())
                r.Text = ReadText(s, maxBytes, resp.CharacterSet);
        }

        public static string ReadText(Stream s, long maxBytes, string charset)
        {
            MemoryStream ms = new MemoryStream();
            byte[] buf = new byte[16384];
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                if (ms.Length + n > maxBytes) { ms.Write(buf, 0, (int)Math.Max(0, maxBytes - ms.Length)); break; }
                ms.Write(buf, 0, n);
            }
            Encoding enc = Encoding.UTF8;
            try { if (!string.IsNullOrEmpty(charset) && charset.IndexOf("utf", StringComparison.OrdinalIgnoreCase) < 0) enc = Encoding.GetEncoding(charset.Trim('"')); } catch { }
            return enc.GetString(ms.ToArray());
        }

        public static void ReadError(HttpResult r, WebException wex)
        {
            HttpWebResponse resp = wex.Response as HttpWebResponse;
            if (resp == null)
            {
                r.Error = wex.Message;
                r.ConnectFailed = IsConnectFailure(wex);
                return;
            }
            try
            {
                r.Status = (int)resp.StatusCode;
                r.ContentType = resp.ContentType ?? "";
                foreach (string k in resp.Headers.AllKeys) r.Headers[k] = resp.Headers[k];
                using (Stream s = resp.GetResponseStream()) r.Text = ReadText(s, 1024 * 1024, resp.CharacterSet);
            }
            catch { }
            finally { try { resp.Close(); } catch { } }
            r.Error = "HTTP " + r.Status + ": " + ErrorMessage(r.Text, wex.Message);
        }

        public static string ErrorMessage(string body, string fallback)
        {
            object o = MinGent.Json.TryParse(body ?? "");
            System.Collections.Generic.List<object> arr = o as System.Collections.Generic.List<object>;
            if (arr != null && arr.Count > 0) o = arr[0];
            string m = MinGent.Json.Str(o, "error", "message") ?? MinGent.Json.Str(o, "error") ?? MinGent.Json.Str(o, "message") ?? MinGent.Json.Str(o, "detail");
            if (!string.IsNullOrEmpty(m)) return m;
            string t = (body ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length > 300) t = t.Substring(0, 300) + "...";
            return t.Length > 0 ? t : fallback;
        }

        public static bool IsConnectFailure(WebException wex)
        {
            if (wex.Response != null) return false;
            switch (wex.Status)
            {
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.SendFailure:
                case WebExceptionStatus.Timeout:
                    return true;
            }
            return false;
        }
    }

    class SseReader
    {
        readonly StreamReader reader;
        public SseReader(Stream s) { reader = new StreamReader(s, new UTF8Encoding(false), false, 8192); }

        public bool Next(out string eventName, out string data)
        {
            eventName = null;
            data = null;
            StringBuilder sb = null;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0)
                {
                    if (sb != null) { data = sb.ToString(); return true; }
                    eventName = null;
                    continue;
                }
                if (line[0] == ':') continue;
                int colon = line.IndexOf(':');
                string field = colon < 0 ? line : line.Substring(0, colon);
                string value = colon < 0 ? "" : line.Substring(colon + 1);
                if (value.StartsWith(" ")) value = value.Substring(1);
                if (field == "event") eventName = value;
                else if (field == "data")
                {
                    if (sb == null) sb = new StringBuilder();
                    else sb.Append('\n');
                    sb.Append(value);
                }
            }
            if (sb != null) { data = sb.ToString(); return true; }
            return false;
        }
    }
}
