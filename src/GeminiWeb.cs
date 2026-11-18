using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MinGent
{
    class GeminiModel
    {
        public string Id = "", Hex = "", Category = "", Version = "", Tagline = "";
        public int Number = 1, Levels = 2;
        public List<int> Steps = new List<int>();

        public bool Thinks { get { return Levels >= 2; } }

        public static string StepName(int id)
        {
            return id == 1 ? "low" : id == 4 ? "medium" : id == 2 ? "high" : null;
        }

        int StepFor(string name)
        {
            foreach (int id in Steps) if (StepName(id) == name) return id;
            return 0;
        }

        public List<string> Efforts
        {
            get
            {
                List<string> named = new List<string>();
                foreach (string name in new string[] { "low", "medium", "high" })
                    if (StepFor(name) != 0) named.Add(name);
                if (named.Count >= 2) return named;
                return Thinks ? new List<string>(new string[] { "on", "high" }) : null;
            }
        }

        public int Level(string effort)
        {
            bool top = effort == "high" || effort == "xhigh" || effort == "max";
            List<string> named = Efforts;
            if (named == null) return 1;
            if (named[0] == "on") return top ? 2 : 1;
            string want = top ? "high" : effort == "medium" ? "medium" : "low";
            if (StepFor(want) == 0) want = want == "medium" ? named[0] : top ? named[named.Count - 1] : named[0];
            return StepFor(want);
        }

        public string Display
        {
            get
            {
                if (Version.Length > 0 && (Category.Length == 0 || Version.IndexOf(Category, StringComparison.OrdinalIgnoreCase) >= 0)) return "Gemini " + Version;
                return "Gemini " + (Category.Length > 0 ? Category : Id);
            }
        }
    }

    class GeminiAccount
    {
        public int Status = 1000;
        public bool Guest;
        public int Capacity = 1, CapacityField = 12;
        public readonly List<GeminiModel> Models = new List<GeminiModel>();
        public DateTime When = DateTime.UtcNow;
        public string For = "";

        public int Context
        {
            get
            {
                if (Guest) return 32000;
                if (CapacityField == 13 || Capacity == 2 || Capacity == 3) return 1000000;
                return Capacity == 4 ? 128000 : 32000;
            }
        }

        public GeminiModel Find(string id)
        {
            foreach (GeminiModel m in Models) if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        public string Problem
        {
            get
            {
                switch (Status)
                {
                    case 1000: case 1016: return null;
                    case 1014: return "Google is limiting this Gemini account for now. Open gemini.google.com in your browser to see why.";
                    case 1021: return "Google rejected this account for Gemini. Check the account at gemini.google.com.";
                    case 1033: return "Google did not let this account use Gemini here. Check the account at gemini.google.com.";
                    case 1040: case 1042: return "Gemini asks you to accept its terms first. Open gemini.google.com in your browser and do that.";
                    case 1054: case 1057: return "This Gemini account needs a parent's or guardian's approval.";
                    case 1060: return "Gemini is not offered in this country or region.";
                }
                return "Gemini answered with account status " + Status.ToString(CultureInfo.InvariantCulture) + ". Open gemini.google.com in your browser to check the account.";
            }
        }
    }

    class GeminiSession
    {
        public string Host = "", Path = "", At = "", Build = "", Sid = "", Push = "";
    }

    class GeminiFile
    {
        public string Name = "", Type = "", Data = "";
    }

    class GeminiChunk
    {
        public string Text = "", Error;
        public bool Done;
        public int Status;
    }

    class GeminiJob
    {
        public readonly string Id;
        readonly Queue<GeminiChunk> parts = new Queue<GeminiChunk>();
        bool finished;

        public GeminiJob(string id) { Id = id; }

        public void Push(GeminiChunk c)
        {
            lock (parts)
            {
                if (finished) return;
                if (c.Done) finished = true;
                parts.Enqueue(c);
                Monitor.PulseAll(parts);
            }
        }

        public bool Take(int ms, out GeminiChunk c)
        {
            lock (parts)
            {
                if (parts.Count == 0) Monitor.Wait(parts, ms);
                c = parts.Count > 0 ? parts.Dequeue() : null;
                return c != null;
            }
        }
    }

    interface IGeminiTransport
    {
        bool Connected { get; }
        string Session(bool create, bool reload, CancelToken ct, out GeminiSession s);
        GeminiJob Start(string url, Dictionary<string, string> headers, string body, GeminiFile file);
        void End(GeminiJob job);
    }

    class GeminiCall
    {
        public string Url = "", Body = "";
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
    }

    static class GeminiProto
    {
        public static string Origin = "https://gemini.google.com";
        public const string UploadUrl = "https://content-push.googleapis.com/upload";
        public const string Overflow = "I'm having a hard time fulfilling your request";
        public const string Trouble = "Sorry, something went wrong";

        public static bool Troubled(string said, bool whole)
        {
            string t = (said ?? "").Trim();
            if (t.Length == 0 || t.Length > 120) return false;
            if (t.StartsWith(Trouble, StringComparison.Ordinal)) return true;
            return !whole && Trouble.StartsWith(t, StringComparison.Ordinal);
        }
        const string Rpc = "/_/BardChatUi/data/";
        const string Form = "application/x-www-form-urlencoded;charset=utf-8";
        const string HexDigits = "0123456789ABCDEF";
        public static readonly string Run = Guid.NewGuid().ToString().ToUpperInvariant();
        static long request = new Random().Next(10000, 99999);

        public static string Enc(string s)
        {
            s = s ?? "";
            StringBuilder sb = new StringBuilder(s.Length + s.Length / 3 + 16);
            byte[] one = new byte[8];
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~') { sb.Append(c); continue; }
                int n;
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { n = Encoding.UTF8.GetBytes(s, i, 2, one, 0); i++; }
                else if (char.IsSurrogate(c)) { one[0] = 0xEF; one[1] = 0xBF; one[2] = 0xBD; n = 3; }
                else n = Encoding.UTF8.GetBytes(s, i, 1, one, 0);
                for (int k = 0; k < n; k++) sb.Append('%').Append(HexDigits[one[k] >> 4]).Append(HexDigits[one[k] & 15]);
            }
            return sb.ToString();
        }

        static string Common(GeminiSession s)
        {
            long id = Interlocked.Add(ref request, 100000);
            return "hl=en&_reqid=" + id.ToString(CultureInfo.InvariantCulture) + "&rt=c" + (s.Build.Length > 0 ? "&bl=" + Enc(s.Build) : "") + (s.Sid.Length > 0 ? "&f.sid=" + Enc(s.Sid) : "");
        }

        public static GeminiCall Status(GeminiSession s)
        {
            GeminiCall c = new GeminiCall();
            c.Url = Origin + Rpc + "batchexecute?rpcids=otAQ7b&source-path=%2Fapp&" + Common(s);
            c.Headers["Content-Type"] = Form;
            c.Headers["X-Same-Domain"] = "1";
            c.Headers["x-goog-ext-525001261-jspb"] = Json.Write(Json.A(1, null, null, null, null, null, null, null, Json.A(4, 5, 6, 8), null, null, null, null, null, null, null, Run));
            c.Headers["x-goog-ext-73010989-jspb"] = "[0]";
            c.Body = "at=" + Enc(s.At) + "&f.req=" + Enc("[[[\"otAQ7b\",\"[]\",null,\"generic\"]]]");
            return c;
        }

        public static GeminiCall Generate(GeminiSession s, GeminiAccount a, GeminiModel m, string prompt, List<object> files, List<object> meta, int level)
        {
            string turn = Guid.NewGuid().ToString().ToUpperInvariant();
            object[] inner = new object[81];
            inner[0] = Json.A(prompt, 0, null, files, null, null, 0);
            inner[1] = Json.A("en");
            inner[2] = meta ?? Json.A("", "", "", null, null, null, null, null, null, "");
            inner[6] = Json.A(1);
            inner[7] = 1;
            inner[10] = 1;
            inner[11] = 0;
            inner[17] = Json.A(Json.A(0));
            inner[18] = 0;
            inner[27] = 1;
            inner[30] = Json.A(4);
            inner[41] = Json.A(1);
            inner[45] = 1;
            inner[53] = 0;
            inner[59] = turn;
            inner[61] = new List<object>();
            inner[68] = 1;
            inner[79] = m.Number;
            inner[80] = level;
            List<object> pick = Json.A(1, null, null, null, m.Hex, null, null, 0, Json.A(4, 5, 6, 8), null, null);
            if (a.CapacityField == 13) pick.Add(null);
            pick.Add(a.Capacity);
            pick.Add(null);
            pick.Add(null);
            pick.Add(m.Number);
            pick.Add(level);
            pick.Add(Run);
            GeminiCall c = new GeminiCall();
            c.Url = Origin + Rpc + "assistant.lamda.BardFrontendService/StreamGenerate?" + Common(s);
            c.Headers["Content-Type"] = Form;
            c.Headers["X-Same-Domain"] = "1";
            c.Headers["x-goog-ext-525001261-jspb"] = Json.Write(pick);
            c.Headers["x-goog-ext-73010989-jspb"] = "[0]";
            c.Headers["x-goog-ext-73010990-jspb"] = "[0,0,0]";
            c.Headers["x-goog-ext-525005358-jspb"] = Json.Write(Json.A(turn, 1));
            c.Body = "at=" + Enc(s.At) + "&f.req=" + Enc(Json.Write(Json.A(null, Json.Write(new List<object>(inner)))));
            return c;
        }

        public static GeminiCall Upload(GeminiSession s)
        {
            GeminiCall c = new GeminiCall();
            c.Url = UploadUrl;
            c.Headers["Push-ID"] = s.Push.Length > 0 ? s.Push : "feeds/mcudyrk2a4khkz";
            c.Headers["X-Tenant-Id"] = "bard-storage";
            return c;
        }

        public static object Field(List<object> block, int index)
        {
            if (block == null || block.Count == 0) return null;
            object v = index >= 0 && index < block.Count ? block[index] : null;
            if (v == null || v is Dictionary<string, object>)
            {
                Dictionary<string, object> sparse = block[block.Count - 1] as Dictionary<string, object>;
                v = null;
                if (sparse != null) sparse.TryGetValue((index + 1).ToString(CultureInfo.InvariantCulture), out v);
            }
            return v;
        }

        static bool Has(List<object> flags, int n)
        {
            if (flags == null) return false;
            foreach (object o in flags) if (o is double && (int)(double)o == n) return true;
            return false;
        }

        static string Slug(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in (s ?? "").Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.') sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        static string First(List<object> item, params int[] places)
        {
            foreach (int i in places)
            {
                string s = (Json.Str(item, i) ?? "").Trim();
                if (s.Length > 0) return s;
            }
            return "";
        }

        static bool Numbered(string s)
        {
            foreach (char c in s ?? "") if (c >= '0' && c <= '9') return true;
            return false;
        }

        public static GeminiAccount ParseStatus(string text)
        {
            foreach (object part in new GeminiFrames().Feed(text))
            {
                List<object> p = part as List<object>;
                if (p == null || p.Count < 3 || Json.Str(p, 1) != "otAQ7b") continue;
                List<object> body = Json.TryParse(Json.Str(p, 2) ?? "") as List<object>;
                if (body == null) continue;
                GeminiAccount a = new GeminiAccount();
                int status = Json.Int(body, 14);
                a.Status = status == 0 ? 1000 : status;
                a.Guest = a.Status == 1016;
                List<object> tier = Json.List(body, 16), able = Json.List(body, 17);
                if (Has(tier, 21)) { a.Capacity = 1; a.CapacityField = 13; }
                else if (Has(tier, 22)) { a.Capacity = 2; a.CapacityField = 13; }
                else if (Has(able, 115)) a.Capacity = 4;
                else if (Has(tier, 16) || Has(able, 106)) a.Capacity = 3;
                else if (Has(tier, 8) || Has(able, 19)) a.Capacity = 2;
                List<object> models = Json.List(body, 15);
                if (models != null)
                    foreach (object o in models)
                    {
                        List<object> item = o as List<object>;
                        string hex = Json.Str(item, 0);
                        if (string.IsNullOrEmpty(hex)) continue;
                        GeminiModel m = new GeminiModel();
                        m.Hex = hex;
                        Name(m, item, First(item, 1, 10), 19, 2, 12, 11);
                        object number = Json.Get(item, 17);
                        if (!(number is double)) number = Json.Get(item, 9);
                        m.Number = number is double ? (int)(double)number : 1;
                        string id = Slug(m.Category.Length > 0 ? m.Category : m.Version);
                        if (id.Length == 0) id = hex;
                        string unique = id;
                        for (int n = 2; a.Find(unique) != null; n++) unique = id + "-" + n.ToString(CultureInfo.InvariantCulture);
                        m.Id = unique;
                        a.Models.Add(m);
                        if (a.Guest) break;
                    }
                Levels(body, a);
                return a;
            }
            return null;
        }

        static void Name(GeminiModel m, List<object> from, string category, params int[] places)
        {
            m.Category = category;
            m.Version = "";
            m.Tagline = "";
            foreach (int at in places)
            {
                string said = (Json.Str(from, at) ?? "").Trim();
                if (said.Length == 0 || string.Equals(said, m.Category, StringComparison.OrdinalIgnoreCase) || said == m.Version) continue;
                if (Numbered(said) && m.Version.Length == 0) m.Version = said;
                else if (m.Tagline.Length == 0) m.Tagline = said;
            }
        }

        static List<object> Entry(List<object> entries, GeminiModel m, List<object> taken)
        {
            foreach (object x in entries)
            {
                List<object> e = Json.List(x, 0);
                string category = (Json.Str(e, 4) ?? "").Trim(), version = (Json.Str(e, 5) ?? "").Trim();
                if (e == null || category.Length == 0) continue;
                if (taken == null)
                {
                    if (string.Equals(m.Category, category, StringComparison.OrdinalIgnoreCase) || (version.Length > 0 && string.Equals(m.Version, version, StringComparison.OrdinalIgnoreCase))) return x as List<object>;
                    continue;
                }
                object number = Json.Get(e, 1);
                if (number is double && (int)(double)number == m.Number && !taken.Contains(x)) return x as List<object>;
            }
            return null;
        }

        static void Levels(List<object> body, GeminiAccount a)
        {
            foreach (object o in body)
            {
                List<object> block = o as List<object>;
                string name = Json.Str(block, 0);
                List<object> entries = Json.List(block, 1);
                if (name == null || entries == null || !name.EndsWith("_policy", StringComparison.Ordinal)) continue;
                List<object> taken = new List<object>();
                List<GeminiModel> unnamed = new List<GeminiModel>();
                foreach (GeminiModel m in a.Models)
                {
                    List<object> x = Entry(entries, m, null);
                    if (x == null) { unnamed.Add(m); continue; }
                    taken.Add(x);
                    Thinking(m, x);
                }
                foreach (GeminiModel m in unnamed)
                {
                    List<object> x = Entry(entries, m, taken);
                    if (x == null) continue;
                    taken.Add(x);
                    List<object> e = Json.List(x, 0);
                    Name(m, e, (Json.Str(e, 4) ?? "").Trim(), 5, 12);
                    Thinking(m, x);
                }
                return;
            }
        }

        static void Thinking(GeminiModel m, List<object> x)
        {
            object levels = Json.Get(x, 0, 15);
            List<int> steps = new List<int>();
            List<object> named = Json.List(x, 1);
            if (named != null)
                foreach (object n in named)
                {
                    object id = Json.Get(n as List<object>, 0);
                    if (id is double && GeminiModel.StepName((int)(double)id) != null && !steps.Contains((int)(double)id)) steps.Add((int)(double)id);
                }
            m.Levels = levels is double ? (int)(double)levels : 1;
            m.Steps = steps;
        }

        static readonly Regex Artifact = new Regex(@"https?://googleusercontent\.com/(?:\w+/)+\d+\n*", RegexOptions.CultureInvariant);
        static readonly Regex Cite = new Regex(@"[ \t]*\[cite:\s*[\d,\s]+\]|\[cite_start\]", RegexOptions.CultureInvariant);
        static readonly Regex OwnCode = new Regex(@"```(\w+)\?code_\w+&code_event_index=\d+", RegexOptions.CultureInvariant);

        public static string Clean(string text)
        {
            string t = text ?? "";
            if (t.IndexOf("googleusercontent.com", StringComparison.Ordinal) >= 0) t = Artifact.Replace(t, "");
            if (t.IndexOf("[cite", StringComparison.Ordinal) >= 0) t = Cite.Replace(t, "");
            if (t.IndexOf("code_event_index", StringComparison.Ordinal) >= 0) t = OwnCode.Replace(t, "```$1");
            return t;
        }

        public static string Describe(int code)
        {
            switch (code)
            {
                case 1013: return "Gemini is temporarily unavailable. Try again in a moment.";
                case 1037: return "Gemini's usage limit for this model is reached for now. Pick another model in the model menu, or wait until the limit resets.";
                case 1050: return "Gemini refused to continue the conversation with this model.";
                case 1052: return "Gemini refused this model for the request. It may not be available to this account right now, or Gemini's site changed and MinGent needs an update.";
                case 1060: return "Google is blocking Gemini requests from this network for now. Wait a while, or try another network.";
            }
            return "Gemini answered with error " + code.ToString(CultureInfo.InvariantCulture) + ". Its site may have changed, or it is having trouble right now.";
        }
    }

    class GeminiFrames
    {
        readonly StringBuilder buf = new StringBuilder();
        bool started;
        public bool Garbage;

        public string Rest { get { return buf.ToString(); } }

        public List<object> Feed(string text)
        {
            List<object> parts = new List<object>();
            if (!string.IsNullOrEmpty(text)) buf.Append(text);
            if (Garbage) return parts;
            if (!started)
            {
                int lead = 0;
                while (lead < buf.Length && char.IsWhiteSpace(buf[lead])) lead++;
                if (buf.Length - lead < 4) return parts;
                if (buf.ToString(lead, 4) == ")]}'") buf.Remove(0, lead + 4);
                started = true;
            }
            while (true)
            {
                int skip = 0;
                while (skip < buf.Length && char.IsWhiteSpace(buf[skip])) skip++;
                if (skip > 0) buf.Remove(0, skip);
                if (buf.Length == 0) break;
                int digits = 0;
                while (digits < buf.Length && digits < 10 && buf[digits] >= '0' && buf[digits] <= '9') digits++;
                if (digits == 0) { Garbage = true; break; }
                if (digits == buf.Length) break;
                if (buf[digits] != '\n' && buf[digits] != '\r') { Garbage = true; break; }
                int length = int.Parse(buf.ToString(0, digits), CultureInfo.InvariantCulture);
                if (buf.Length < digits + length) break;
                string frame = buf.ToString(digits, length);
                buf.Remove(0, digits + length);
                List<object> list = Json.TryParse(frame.Trim()) as List<object>;
                if (list == null) continue;
                foreach (object p in list) parts.Add(p);
            }
            return parts;
        }
    }

    class GeminiReply
    {
        public string Text = "", Thoughts = "", Cid = "", Rid = "", Rcid = "", Context;
        public bool Any, Complete, UsedWeb;
        public int ErrorCode;
        public readonly List<string[]> Sources = new List<string[]>();

        static readonly Regex Card = new Regex(@"^https?://googleusercontent\.com/card_content/\d+", RegexOptions.CultureInvariant);

        public List<object> Meta() { return Json.A(Cid, Rid, Rcid, null, null, null, null, null, null, Context ?? ""); }

        public void Feed(object part)
        {
            List<object> p = part as List<object>;
            if (p == null) return;
            object code = Json.Get(p, 5, 2, 0, 1, 0);
            if (code is double) { ErrorCode = (int)(double)code; return; }
            List<object> b = Json.TryParse(Json.Str(p, 2) ?? "") as List<object>;
            if (b == null) return;
            string cid = Json.Str(b, 1, 0), rid = Json.Str(b, 1, 1);
            if (!string.IsNullOrEmpty(cid)) Cid = cid;
            if (!string.IsNullOrEmpty(rid)) Rid = rid;
            string context = GeminiProto.Field(b, 25) as string;
            if (context != null) Context = context;
            List<object> cand = Json.List(b, 4, 0);
            string rcid = Json.Str(cand, 0);
            if (string.IsNullOrEmpty(rcid)) return;
            Rcid = rcid;
            string text = Json.Str(cand, 1, 0) ?? "";
            if (Card.IsMatch(text)) text = Json.Str(cand, 22, 0) ?? text;
            Text = text;
            string thoughts = Json.Str(cand, 37, 0, 0);
            if (!string.IsNullOrEmpty(thoughts)) Thoughts = thoughts;
            if (Json.Int(cand, 8, 0) == 2) Complete = true;
            if (!UsedWeb) UsedWeb = Grounded(cand);
            Steps(cand);
            Any = true;
        }

        void Steps(List<object> cand)
        {
            List<object> steps = Json.List(cand, 37, 1);
            if (steps == null) return;
            foreach (object o in steps)
            {
                List<object> found = Json.List(o, 7);
                if (found == null) continue;
                foreach (object x in found)
                {
                    string url = Json.Str(x, 1), title = (Json.Str(x, 2) ?? "").Trim();
                    if (url == null || !(url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("http://", StringComparison.Ordinal))) continue;
                    UsedWeb = true;
                    if (Sources.Count < 8 && !Sources.Exists(delegate (string[] s) { return s[0] == url; })) Sources.Add(new string[] { url, title });
                }
            }
        }

        public string SourceList()
        {
            if (Sources.Count == 0) return "";
            StringBuilder sb = new StringBuilder("\n\nSources, from Gemini's own Google Search:");
            foreach (string[] s in Sources)
                sb.Append("\n- [").Append((s[1].Length > 0 ? s[1] : s[0]).Replace("[", "(").Replace("]", ")")).Append("](").Append(s[0].Replace(" ", "%20").Replace(")", "%29")).Append(')');
            return sb.ToString();
        }

        static bool Grounded(List<object> cand)
        {
            List<object> cited = GeminiProto.Field(Json.List(cand, 12), 43) as List<object>;
            if (cited != null && cited.Count > 0) return true;
            int budget = 4000;
            return Searched(Json.Get(cand, 37), 0, ref budget);
        }

        static bool Searched(object o, int depth, ref int budget)
        {
            if (o == null || depth > 12 || --budget < 0) return false;
            string s = o as string;
            if (s != null) return s == "Google Search" || s == "Searched the web" || s == "Searching the web";
            List<object> l = o as List<object>;
            if (l != null) { foreach (object x in l) if (Searched(x, depth + 1, ref budget)) return true; return false; }
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d != null) foreach (object x in d.Values) if (Searched(x, depth + 1, ref budget)) return true;
            return false;
        }
    }

    class GeminiPlan
    {
        public string Prompt = "";
        public readonly List<ImageData> Images = new List<ImageData>();
    }

    static class GeminiPrompt
    {
        public const string Fence = "```mingent";
        public const string OldFence = "```tool_call";
        public const int ImageLimit = 8;

        const string Where = "You are running inside MinGent on the user's PC, not in the Gemini app. Gemini's own built-in features (its Google Search, code execution, image or video generation, extensions, canvas) are switched off here, so never try them.";

        const string Honest = " Never claim to have looked something up or run something without a tool result in this conversation.";

        public const string Nudge = "[MinGent] Reminder: files, commands and web search on this PC are reached only by writing a mingent block with one of MinGent's tools above. The block is text in your reply, not a function call, so you can always write one. Gemini's own search and code execution are off.";

        public const string Next = "(Next: another mingent block, or your answer to the user in a reply without blocks. A block is text, so you can always write one.)";

        const string Protocol = Where + " MinGent's tools, listed under \"MinGent's tools\" below, take their place: they are real, they work, and they are the only way you can search, read, run or change anything. When the user asks for something one of them can do (for example searching the web with a search tool, or running a command with a command tool), use that tool." + Honest + "\n\n" +
            "MinGent's tools are not function calls. You make no function call and no tool call of Gemini's anywhere in this conversation, and it makes no difference whether you have any: you use one of MinGent's tools by writing a fenced block exactly like this as the text of your reply, then you stop writing:\n\n" +
            "```mingent\n{\"name\": \"tool_name\", \"arguments\": {\"key\": \"value\"}}\n```\n\n" +
            "- The block is ordinary text, so you can write one in every reply, also in a final answer, and as many as the task needs. MinGent, a program on the user's PC, reads the block from your reply, runs the tool and sends you what it returned in the next message as \"[MinGent result: name]\". Then you reply again: more blocks, or the answer.\n" +
            "- Wherever these instructions say \"call a tool\" or \"tool call\", they mean writing this block.\n" +
            "- Never tell the user that tools or tool calls are unavailable, used up or limited, and never answer from memory what a tool can find out. Write the block.\n" +
            "- The block holds one JSON object: double quotes, no comments, and line breaks inside strings written as \\n.\n" +
            "- You may write one short sentence before the block. Write nothing after it. The answer for the user goes in a reply of its own, without blocks, once the results are in.\n" +
            "- For several independent tools at once, write several blocks one after another.\n" +
            "- Never write a \"[MinGent result: ...]\" line yourself.\n" +
            "- When no tool is needed, answer normally without a block.";

        public static string Hash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s ?? "") { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }

        public static List<string> Sig(List<Msg> messages)
        {
            List<string> sig = new List<string>(messages.Count);
            foreach (Msg m in messages)
            {
                if (m.Role == "assistant") { sig.Add("a"); continue; }
                int images = m.Images != null ? m.Images.Count : 0;
                sig.Add((m.Role == "tool" ? "t" + Hash((m.ToolName ?? "") + "\n" + m.Text) : "u" + Hash(m.Text)) + "/" + images.ToString(CultureInfo.InvariantCulture));
            }
            return sig;
        }

        public static int Covers(List<string> held, List<string> asked)
        {
            int k = held.Count;
            if (k == 0 || asked.Count <= k) return -1;
            for (int i = 0; i < k; i++) if (held[i] != asked[i]) return -1;
            for (int i = k; i < asked.Count; i++) if (asked[i] == "a") return -1;
            return k;
        }

        static string Kind(Dictionary<string, object> p)
        {
            if (p == null) return "any";
            List<object> choices = Json.List(p, "enum");
            if (choices != null && choices.Count > 0)
            {
                List<string> names = new List<string>();
                foreach (object c in choices) names.Add(Json.Write(c));
                return "one of " + string.Join(", ", names.ToArray());
            }
            string type = Json.Str(p, "type") ?? "any";
            if (type == "array")
            {
                Dictionary<string, object> items = Json.Obj(p, "items");
                if (items == null) return "array";
                return "array of " + (Json.Obj(items, "properties") != null ? Json.Write(items) : Kind(items));
            }
            if (type == "object" && Json.Obj(p, "properties") != null) return "object " + Json.Write(p);
            return type;
        }

        public static string Tools(List<ToolSpec> tools)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ToolSpec t in tools)
            {
                sb.Append("\n## ").Append(t.Name).Append('\n').Append((t.Description ?? "").Trim()).Append('\n');
                Dictionary<string, object> props = Json.Obj(t.Schema, "properties");
                if (props == null || props.Count == 0) { sb.Append("No arguments.\n"); continue; }
                List<object> required = Json.List(t.Schema, "required");
                foreach (KeyValuePair<string, object> kv in props)
                {
                    Dictionary<string, object> p = kv.Value as Dictionary<string, object>;
                    bool must = required != null && required.Contains(kv.Key);
                    sb.Append("- ").Append(kv.Key).Append(" (").Append(Kind(p)).Append(must ? ", required" : "").Append(')');
                    string about = Json.Str(p, "description");
                    if (!string.IsNullOrEmpty(about)) sb.Append(": ").Append(about.Trim());
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        public static string Head(LlmRequest req)
        {
            StringBuilder sb = new StringBuilder((req.System ?? "").Trim());
            if (req.Tools.Count == 0) return sb.Append("\n\n# Where you are\n").Append(Where).Append(Honest).ToString();
            return sb.Append("\n\n# How you act here\n").Append(Protocol).Append("\n\n# MinGent's tools\n").Append(Tools(req.Tools)).ToString();
        }

        static void Render(StringBuilder sb, Msg m, bool label, GeminiPlan plan, int firstImage, ref int seen)
        {
            if (m.Role == "tool") sb.Append("[MinGent result: ").Append(m.ToolName ?? "tool").Append(m.ToolError ? ", failed" : "").Append("]\n");
            else if (m.Role == "assistant") sb.Append("[You]\n");
            else if (label) sb.Append("[User]\n");
            sb.Append((m.Text ?? "").Trim());
            if (m.Role == "assistant" && m.ToolCalls != null)
                foreach (ToolCall c in m.ToolCalls)
                {
                    string args = string.IsNullOrEmpty(c.Arguments) ? "{}" : c.Arguments.Trim();
                    if (!args.StartsWith("{", StringComparison.Ordinal)) args = Json.Quote(args);
                    sb.Append("\n").Append(Fence).Append("\n{\"name\": ").Append(Json.Quote(c.Name)).Append(", \"arguments\": ").Append(args).Append("}\n```");
                }
            if (m.Images != null)
                foreach (ImageData image in m.Images)
                {
                    if (seen++ < firstImage) { sb.Append("\n(an earlier picture, not attached again)"); continue; }
                    plan.Images.Add(image);
                    sb.Append("\n(picture ").Append(plan.Images.Count.ToString(CultureInfo.InvariantCulture)).Append(" is attached to this message)");
                }
            sb.Append("\n\n");
        }

        public static GeminiPlan Plan(LlmRequest req, int covered)
        {
            GeminiPlan plan = new GeminiPlan();
            StringBuilder sb = new StringBuilder();
            List<Msg> all = req.Messages;
            int images = 0, seen = 0;
            for (int i = covered; i < all.Count; i++) if (all[i].Images != null) images += all[i].Images.Count;
            int firstImage = Math.Max(0, images - ImageLimit);
            bool results = false;
            if (covered > 0)
            {
                bool label = all.Count - covered > 1;
                for (int i = covered; i < all.Count; i++)
                {
                    if (all[i].Role == "tool") results = true;
                    Render(sb, all[i], label || all[i].Role != "user", plan, firstImage, ref seen);
                }
            }
            else
            {
                sb.Append(Head(req));
                if (all.Count == 1 && all[0].Role == "user")
                {
                    sb.Append("\n\n# The user's message\n");
                    Render(sb, all[0], false, plan, firstImage, ref seen);
                }
                else
                {
                    sb.Append("\n\n# The conversation so far\n");
                    foreach (Msg m in all) Render(sb, m, true, plan, firstImage, ref seen);
                    sb.Append("# What to do now\nReply to the last message above as your next turn. The earlier steps are done: do not repeat them.");
                }
            }
            string text = sb.ToString().TrimEnd();
            if (req.NoCalls) text += "\n\n[MinGent] MinGent has switched its tools off for this one reply. Answer in plain text, without a mingent block.";
            else if (req.Tools.Count == 0) { }
            else if (!results) text += "\n\n" + Nudge;
            else text += "\n\n" + Next;
            plan.Prompt = text;
            return plan;
        }

        public static int Size(LlmRequest req, int upTo)
        {
            long n = (req.System ?? "").Length;
            foreach (ToolSpec t in req.Tools) n += (t.Description ?? "").Length + Json.Write(t.Schema).Length;
            for (int i = 0; i < req.Messages.Count && i < upTo; i++) n += req.Messages[i].ApproxChars;
            return (int)Math.Min(int.MaxValue, n);
        }

        static readonly Regex Flicker = new Regex(@"\\+[`*_~]", RegexOptions.CultureInvariant);
        static readonly Regex NameField = new Regex(@"""name""\s*:\s*""([^""\\]+)""", RegexOptions.CultureInvariant);

        public static string Shown(string raw, bool final, out string tool, out int argChars, out string head)
        {
            tool = null;
            argChars = 0;
            head = "";
            string t = GeminiProto.Clean(raw);
            if (!final)
            {
                if (t.EndsWith("\n```", StringComparison.Ordinal)) t = t.Substring(0, t.Length - 4);
                Match esc = Flicker.Match(t, t.LastIndexOf('\n') + 1);
                if (esc.Success) t = t.Substring(0, esc.Index);
                int open = t.LastIndexOf('[');
                if (open >= 0 && t.Length - open < 48 && t.IndexOf(']', open) < 0)
                {
                    string tail = t.Substring(open);
                    if (tail.StartsWith("[cite", StringComparison.Ordinal) || "[cite".StartsWith(tail, StringComparison.Ordinal)) t = t.Substring(0, open);
                }
            }
            int fence;
            int at = FenceAt(t, 0, out fence);
            if (at >= 0)
            {
                string rest = t.Substring(at + fence);
                Match name = NameField.Match(rest);
                tool = name.Success ? name.Groups[1].Value : "";
                int a = rest.IndexOf("\"arguments\"", StringComparison.Ordinal);
                string args = a >= 0 ? rest.Substring(a + 11).TrimStart(' ', ':', '\n', '\r') : "";
                argChars = args.Length;
                head = args.Length > Quirks.HeadChars ? args.Substring(0, Quirks.HeadChars) : args;
                return t.Substring(0, at).TrimEnd();
            }
            if (!final)
                for (int k = Math.Min(Math.Max(Fence.Length, OldFence.Length) - 1, t.Length); k > 0; k--)
                    if ((k < Fence.Length && string.CompareOrdinal(t, t.Length - k, Fence, 0, k) == 0) || (k < OldFence.Length && string.CompareOrdinal(t, t.Length - k, OldFence, 0, k) == 0)) { t = t.Substring(0, t.Length - k); break; }
            return t;
        }

        static int FenceAt(string t, int from, out int length)
        {
            int now = t.IndexOf(Fence, from, StringComparison.Ordinal), old = t.IndexOf(OldFence, from, StringComparison.Ordinal);
            bool first = now >= 0 && (old < 0 || now < old);
            length = first ? Fence.Length : OldFence.Length;
            return first ? now : old;
        }

        static int ObjectEnd(string t, int open)
        {
            int depth = 0;
            bool inString = false, escaped = false;
            for (int i = open; i < t.Length; i++)
            {
                char c = t[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return i;
            }
            return -1;
        }

        static string OneLine(string json)
        {
            if (json.IndexOf('\n') < 0 && json.IndexOf('\t') < 0 && json.IndexOf('\r') < 0) return json;
            StringBuilder sb = new StringBuilder(json.Length + 32);
            bool inString = false, escaped = false;
            foreach (char c in json)
            {
                if (inString)
                {
                    if (escaped) { escaped = false; sb.Append(c); continue; }
                    if (c == '\\') { escaped = true; sb.Append(c); continue; }
                    if (c == '"') { inString = false; sb.Append(c); continue; }
                    if (c == '\n') sb.Append("\\n");
                    else if (c == '\r') sb.Append("\\r");
                    else if (c == '\t') sb.Append("\\t");
                    else sb.Append(c);
                    continue;
                }
                if (c == '"') inString = true;
                sb.Append(c);
            }
            return sb.ToString();
        }

        static ToolCall Call(string json, bool cut)
        {
            Dictionary<string, object> d = Json.TryParse(OneLine(json)) as Dictionary<string, object>;
            if (d == null && !cut) d = LlmText.RepairArgs(json);
            ToolCall c = new ToolCall();
            c.Truncated = cut;
            if (d == null || !(Json.Get(d, "name") is string || Json.Get(d, "tool") is string))
            {
                Match name = NameField.Match(json);
                if (!name.Success) return null;
                c.Name = name.Groups[1].Value;
                int a = json.IndexOf("\"arguments\"", StringComparison.Ordinal);
                c.Arguments = a >= 0 ? json.Substring(a + 11).TrimStart(' ', ':', '\n', '\r') : json;
                return c;
            }
            object args = Json.Get(d, "arguments") ?? Json.Get(d, "args") ?? Json.Get(d, "parameters") ?? Json.Get(d, "input");
            string label = args == null && Json.Get(d, "tool") is string ? "tool" : Json.Get(d, "name") is string ? "name" : "tool";
            c.Name = Json.Str(d, label);
            if (args == null)
            {
                Dictionary<string, object> flat = new Dictionary<string, object>();
                foreach (KeyValuePair<string, object> kv in d)
                    if (kv.Key != label) flat[kv.Key] = kv.Value;
                args = flat;
            }
            c.Arguments = args is string ? (string)args : Json.Write(args);
            return c;
        }

        public static List<ToolCall> Extract(ref string text, ICollection<string> known)
        {
            List<ToolCall> calls = new List<ToolCall>();
            string t = text ?? "";
            StringBuilder kept = new StringBuilder();
            int pos = 0;
            while (pos < t.Length)
            {
                int fence;
                int at = FenceAt(t, pos, out fence);
                if (at < 0) break;
                int open = t.IndexOf('{', at + fence);
                if (open < 0 || t.Substring(at + fence, open - at - fence).Trim().Length > 0)
                {
                    kept.Append(t, pos, at + fence - pos);
                    pos = at + fence;
                    continue;
                }
                int close = ObjectEnd(t, open);
                int after = close < 0 ? t.Length : close + 1;
                ToolCall c = Call(close < 0 ? t.Substring(open) : t.Substring(open, close - open + 1), close < 0);
                int end = t.IndexOf("```", after, StringComparison.Ordinal);
                if (end >= 0 && t.Substring(after, end - after).Trim().Length == 0) after = end + 3;
                kept.Append(t, pos, at - pos);
                pos = after;
                if (c != null) calls.Add(c);
            }
            if (calls.Count == 0) return LlmText.ExtractTextToolCalls(ref text, known);
            kept.Append(t, pos, t.Length - pos);
            text = kept.ToString().Trim();
            for (int i = 0; i < calls.Count; i++) calls[i].Id = "call_g" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture) + i.ToString(CultureInfo.InvariantCulture);
            return calls;
        }
    }

    class GeminiThread
    {
        public string Key = "";
        public List<string> Sig = new List<string>();
        public List<object> Meta;
        public DateTime Used = DateTime.UtcNow;
    }

    class GeminiView
    {
        readonly IStreamSink sink;
        readonly bool thoughts;
        string shown = "", thought = "";

        public GeminiView(IStreamSink sink, bool thoughts) { this.sink = sink; this.thoughts = thoughts; }

        public bool Emitted { get { return shown.Length > 0 || thought.Length > 0; } }

        public void Update(string text, string thinking, bool final)
        {
            if (sink == null) return;
            if (thoughts && thinking.Length > thought.Length && thinking.StartsWith(thought, StringComparison.Ordinal))
            {
                sink.OnReasoning(thinking.Substring(thought.Length));
                thought = thinking;
            }
            string tool, head;
            int chars;
            string visible = GeminiPrompt.Shown(text, final, out tool, out chars, out head);
            if (!final && shown.Length == 0 && GeminiProto.Troubled(visible, false)) return;
            if (visible.Length > shown.Length && visible.StartsWith(shown, StringComparison.Ordinal))
            {
                sink.OnText(visible.Substring(shown.Length));
                shown = visible;
            }
            if (tool != null) sink.OnToolProgress(tool, chars, head);
        }
    }

    static class GeminiWeb
    {
        public const string Provider = "geminiweb";
        public const string Title = "Gemini web (experimental)";
        public static readonly object Turn = new object();
        public static IGeminiTransport Transport;
        public static volatile bool Enabled;
        static readonly object gate = new object();
        static GeminiAccount account;
        static readonly List<GeminiThread> threads = new List<GeminiThread>();
        static readonly Dictionary<string, string> uploads = new Dictionary<string, string>();

        public static void Forget()
        {
            lock (gate) { account = null; threads.Clear(); uploads.Clear(); }
        }

        public static GeminiAccount Known { get { lock (gate) return account; } }

        public static void Remember(GeminiAccount a) { lock (gate) account = a; }

        public static string Collect(IGeminiTransport tr, GeminiCall call, GeminiFile file, int idleMs, CancelToken ct, Action<string> onText, out int status)
        {
            status = 0;
            GeminiJob job = tr.Start(call.Url, call.Headers, call.Body, file);
            DateTime last = DateTime.UtcNow;
            try
            {
                while (true)
                {
                    if (ct != null && ct.IsCancelled) return "stopped";
                    GeminiChunk c;
                    if (!job.Take(200, out c))
                    {
                        if ((DateTime.UtcNow - last).TotalMilliseconds > idleMs) return "Gemini stopped answering. Try again.";
                        continue;
                    }
                    last = DateTime.UtcNow;
                    if (c.Status != 0) status = c.Status;
                    if (!string.IsNullOrEmpty(c.Text) && onText != null) onText(c.Text);
                    if (c.Error != null) return c.Error;
                    if (c.Done) { job = null; return null; }
                }
            }
            finally { if (job != null) tr.End(job); }
        }

        public static string Explain(string why)
        {
            switch (why ?? "")
            {
                case "notab": return "No Gemini tab is open in the browser.";
                case "page": return "The helper's Gemini tab is not on Gemini. Open gemini.google.com in " + GeminiHelper.BrowserName + " and get past Google's sign-in or notice page there.";
            }
            return string.IsNullOrEmpty(why) ? "The Gemini helper did not answer." : "The Gemini helper reported: " + why;
        }

        public static string Account(IGeminiTransport tr, GeminiSession s, bool fresh, CancelToken ct, out GeminiAccount a)
        {
            string who = GeminiPrompt.Hash(s.At);
            lock (gate)
            {
                a = account;
                if (!fresh && a != null && a.For == who && a.Models.Count > 0 && (DateTime.UtcNow - a.When).TotalMinutes < 10) return null;
            }
            int status;
            StringBuilder text = new StringBuilder();
            string err = Collect(tr, GeminiProto.Status(s), null, 30000, ct, delegate (string t) { text.Append(t); }, out status);
            if (err != null) { a = null; return err; }
            GeminiAccount got = status == 200 ? GeminiProto.ParseStatus(text.ToString()) : null;
            if (got == null)
            {
                a = null;
                return status == 200 ? "Gemini's answer about the account could not be read. Its site may have changed and MinGent needs an update."
                    : "Gemini refused the account check (HTTP " + status.ToString(CultureInfo.InvariantCulture) + ").";
            }
            got.For = who;
            lock (gate)
            {
                if (account != null && account.For != who) { threads.Clear(); uploads.Clear(); }
                account = got;
            }
            GeminiHelper.SaveModels(got);
            a = got;
            return null;
        }

        public static string Ready(IGeminiTransport tr, bool reload, CancelToken ct, out GeminiSession s, out GeminiAccount a)
        {
            a = null;
            s = null;
            if (tr == null || !tr.Connected) return "The Gemini helper in " + GeminiHelper.BrowserName + " is not connected. Open " + GeminiHelper.BrowserName + " and try again.";
            string err = tr.Session(true, reload, ct, out s);
            if (err != null) return err;
            err = Account(tr, s, reload, ct, out a);
            if (err != null) return err;
            if (a.Problem != null) return a.Problem;
            if (a.Models.Count == 0) return "Gemini listed no models for this account.";
            return null;
        }

        public static GeminiThread FindThread(string key, List<string> sig, out int covered)
        {
            covered = 0;
            GeminiThread best = null;
            lock (gate)
            {
                for (int i = threads.Count - 1; i >= 0; i--)
                {
                    GeminiThread t = threads[i];
                    if ((DateTime.UtcNow - t.Used).TotalMinutes > 30) { threads.RemoveAt(i); continue; }
                    if (t.Key != key) continue;
                    int k = GeminiPrompt.Covers(t.Sig, sig);
                    if (k > covered) { covered = k; best = t; }
                }
            }
            return best;
        }

        public static void DropThread(GeminiThread t)
        {
            if (t != null) lock (gate) threads.Remove(t);
        }

        public static void SaveThread(GeminiThread t, string key, List<string> sig, List<object> meta)
        {
            lock (gate)
            {
                if (t == null || !threads.Contains(t))
                {
                    t = new GeminiThread();
                    threads.Add(t);
                    while (threads.Count > 16) threads.RemoveAt(0);
                }
                t.Key = key;
                t.Sig = new List<string>(sig);
                t.Sig.Add("a");
                t.Meta = meta;
                t.Used = DateTime.UtcNow;
            }
        }

        public static string Uploaded(string key)
        {
            lock (gate) { string v; return uploads.TryGetValue(key, out v) ? v : null; }
        }

        public static void Uploaded(string key, string reference)
        {
            lock (gate)
            {
                if (uploads.Count > 96) uploads.Clear();
                uploads[key] = reference;
            }
        }

        public static List<ModelEntry> Entries(GeminiAccount a)
        {
            List<ModelEntry> list = new List<ModelEntry>();
            if (a == null) return list;
            int context = a.Context;
            string room = (context >= 1000000 ? (context / 1000000).ToString(CultureInfo.InvariantCulture) + "M" : (context / 1000).ToString(CultureInfo.InvariantCulture) + "K") + " context";
            foreach (GeminiModel m in a.Models)
            {
                ModelEntry e = new ModelEntry();
                e.Provider = Provider;
                e.Id = m.Id;
                e.Display = m.Display;
                e.Detail = (m.Tagline.Length > 0 ? m.Tagline + "  \u00B7  " : "") + room;
                e.Context = context;
                e.MaxOutput = 8192;
                e.Vision = true;
                e.ReasoningOptions = m.Efforts;
                list.Add(e);
            }
            return list;
        }

        public static void Probe(List<ModelEntry> list, ProviderState st, CancelToken ct)
        {
            st.Configured = GeminiHelper.SetUp;
            if (!st.Configured) { st.Status = "Set up the helper in Settings to use your Gemini account's models"; return; }
            string browser = GeminiHelper.BrowserName;
            GeminiAccount known = Known ?? GeminiHelper.LoadModels();
            if (Net.Offline) { list.AddRange(Entries(known)); st.Status = "Offline mode is on"; return; }
            IGeminiTransport tr = Transport;
            if (tr == null || !tr.Connected)
            {
                list.AddRange(Entries(known));
                st.Status = "Waiting for the helper in " + browser;
                return;
            }
            GeminiSession s;
            string err = tr.Session(known == null, false, ct, out s);
            GeminiAccount a = null;
            if (err == null) err = Account(tr, s, false, ct, out a);
            if (err != null || a == null)
            {
                list.AddRange(Entries(known));
                st.Reachable = true;
                if (err != Explain("notab")) { st.Error = err; st.Status = err; }
                else st.Status = "Connected";
                return;
            }
            list.AddRange(Entries(a));
            st.Reachable = true;
            if (a.Problem != null) { st.Error = a.Problem; st.Status = a.Problem; }
            else st.Status = a.Guest ? "Not signed in to Gemini in " + browser + " - basic model only" : "Connected";
        }

        public static string Reach(CancelToken ct)
        {
            IGeminiTransport tr = Transport;
            if (tr != null && tr.Connected) return null;
            if (!Enabled || !GeminiHelper.SetUp) return null;
            return WaitReady(delegate (string s) { }, ct);
        }

        public static string WaitReady(Action<string> progress, CancelToken ct)
        {
            if (!Enabled) return "Gemini web is turned off. Turn it on in Settings > Models, or pick another model.";
            if (!GeminiHelper.SetUp) return "Gemini web is not set up yet. Open Settings > Models and set up the helper.";
            if (Net.Offline) return Net.OfflineMessage;
            string bad = GeminiHelper.EnsureBridge(false);
            if (bad != null) return bad;
            IGeminiTransport tr = Transport;
            if (tr == null) return "The Gemini helper could not be started on this PC.";
            if (tr.Connected) return null;
            string browser = GeminiHelper.BrowserName;
            bool running = GeminiHelper.BrowserRunning();
            if (!running)
            {
                progress("Starting " + browser);
                string err = GeminiHelper.StartBrowser(true);
                if (err != null) return err;
            }
            else progress("Waiting for the Gemini helper in " + browser);
            DateTime until = DateTime.UtcNow.AddSeconds(running ? 45 : 70);
            while (!tr.Connected)
            {
                if (ct.IsCancelled) return "stopped";
                if (DateTime.UtcNow > until) return "The Gemini helper in " + browser + " did not answer. Check that the \"MinGent Gemini helper\" extension is on in " + browser + ", or set it up again in Settings.";
                if (!ct.Sleep(200)) return "stopped";
            }
            return null;
        }
    }

    class GeminiWebBackend : LlmBackend
    {
        public override LlmResult Stream(LlmRequest req, IStreamSink sink, CancelToken ct)
        {
            LlmResult r = new LlmResult();
            string off = Net.Check(GeminiProto.Origin + "/", "Gemini web");
            if (off != null) { r.Error = off; return r; }
            bool mine = false;
            try
            {
                while (!mine)
                {
                    Monitor.TryEnter(GeminiWeb.Turn, 200, ref mine);
                    if (!mine && ct.IsCancelled) { r.Cancelled = true; break; }
                }
                if (mine) Run(req, sink, ct, r);
            }
            catch (Exception ex)
            {
                if (ct.IsCancelled) r.Cancelled = true;
                else r.Error = "Gemini web failed: " + ex.Message;
            }
            finally { if (mine) Monitor.Exit(GeminiWeb.Turn); }
            if (r.Cancelled || ct.IsCancelled) { r.Cancelled = true; r.Stop = "cancelled"; r.Error = null; }
            Classify(r);
            return r;
        }

        static string Upload(IGeminiTransport tr, GeminiSession s, List<ImageData> images, CancelToken ct, out List<object> files)
        {
            files = new List<object>();
            for (int i = 0; i < images.Count; i++)
            {
                ImageData image = images[i];
                string type = string.IsNullOrEmpty(image.MediaType) ? "image/png" : image.MediaType;
                string ext = type.EndsWith("jpeg", StringComparison.OrdinalIgnoreCase) || type.EndsWith("jpg", StringComparison.OrdinalIgnoreCase) ? ".jpg"
                    : type.EndsWith("gif", StringComparison.OrdinalIgnoreCase) ? ".gif" : type.EndsWith("webp", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".png";
                string name = "picture" + (i + 1).ToString(CultureInfo.InvariantCulture) + ext;
                string key = GeminiPrompt.Hash(image.Base64) + image.Base64.Length.ToString(CultureInfo.InvariantCulture);
                string reference = GeminiWeb.Uploaded(key);
                if (reference == null)
                {
                    GeminiFile file = new GeminiFile();
                    file.Name = name;
                    file.Type = type;
                    file.Data = image.Base64;
                    StringBuilder got = new StringBuilder();
                    int status;
                    string err = GeminiWeb.Collect(tr, GeminiProto.Upload(s), file, 120000, ct, delegate (string t) { got.Append(t); }, out status);
                    reference = got.ToString().Trim();
                    if (err == null && (status != 200 || !reference.StartsWith("/", StringComparison.Ordinal) || reference.Length > 400))
                        err = "Gemini did not take the picture (HTTP " + status.ToString(CultureInfo.InvariantCulture) + ").";
                    if (err != null) return err;
                    GeminiWeb.Uploaded(key, reference);
                }
                files.Add(Json.A(Json.A(reference), name));
            }
            return null;
        }

        void Run(LlmRequest req, IStreamSink sink, CancelToken ct, LlmResult r)
        {
            string unreachable = GeminiWeb.Reach(ct);
            if (unreachable != null)
            {
                if (ct.IsCancelled) r.Cancelled = true;
                else r.Error = unreachable;
                return;
            }
            IGeminiTransport tr = GeminiWeb.Transport;
            bool fresh = false, reload = false, reloaded = false, plain = false;
            int troubles = 0;
            List<string> sig = GeminiPrompt.Sig(req.Messages);
            string shape = GeminiPrompt.Hash(req.System) + GeminiPrompt.Hash(GeminiPrompt.Tools(req.Tools));
            for (int attempt = 0; attempt < 5; attempt++)
            {
                GeminiSession s;
                GeminiAccount a;
                string err = GeminiWeb.Ready(tr, reload, ct, out s, out a);
                reload = false;
                if (err != null) { r.Error = err; return; }
                GeminiModel m = a.Find(Model);
                if (m == null)
                {
                    r.Error = a.Guest ? "This Gemini model needs a signed-in account. Sign in at gemini.google.com in " + GeminiHelper.BrowserName + ", or pick the basic model."
                        : "This Gemini account no longer lists that model. Pick another one in the model menu.";
                    return;
                }
                string quirk = "geminiweb.standard." + m.Hex;
                int level = plain || Quirks.Has(quirk) ? m.Level(null) : m.Level(req.Effort);
                bool extended = level != m.Level(null);
                string key = m.Id + "|" + shape + "|" + a.For;
                int covered = 0;
                GeminiThread thread = fresh ? null : GeminiWeb.FindThread(key, sig, out covered);
                if (thread == null) covered = 0;
                GeminiPlan plan = GeminiPrompt.Plan(req, covered);
                List<object> files = null;
                if (plan.Images.Count > 0)
                {
                    err = Upload(tr, s, plan.Images, ct, out files);
                    if (err != null) { r.Error = err; return; }
                }
                GeminiReply reply = new GeminiReply();
                GeminiFrames frames = new GeminiFrames();
                GeminiView view = new GeminiView(sink, req.ShowThinking);
                int status;
                err = GeminiWeb.Collect(tr, GeminiProto.Generate(s, a, m, plan.Prompt, files, thread != null ? thread.Meta : null, level), null, 240000, ct, delegate (string text)
                {
                    bool any = false;
                    foreach (object part in frames.Feed(text)) { reply.Feed(part); any = true; }
                    if (any && reply.Any) view.Update(reply.Text, reply.Thoughts, false);
                }, out status);
                r.Status = status;
                if (ct.IsCancelled)
                {
                    GeminiWeb.DropThread(thread);
                    string tool, head;
                    int chars;
                    r.Text = GeminiPrompt.Shown(reply.Text, true, out tool, out chars, out head);
                    r.Cancelled = true;
                    return;
                }
                string said = GeminiProto.Clean(reply.Text);
                bool good = err == null && status == 200 && reply.ErrorCode == 0 && reply.Any && (said.Trim().Length > 0 || reply.Thoughts.Length > 0);
                int total = GeminiPrompt.Size(req, int.MaxValue);
                if (good && said.StartsWith(GeminiProto.Overflow, StringComparison.Ordinal) && said.Length < 200 && total / 3.6 > a.Context * 0.5) { good = false; reply.ErrorCode = -1; }
                if (good && GeminiProto.Troubled(said, true)) { good = false; reply.ErrorCode = -2; }
                if (!good)
                {
                    GeminiWeb.DropThread(thread);
                    if (reply.ErrorCode == -1) { r.Error = "The conversation is too long for this Gemini model (context length exceeded)."; return; }
                    if (view.Emitted) { r.Error = err ?? "Gemini's reply was cut off. Try again."; r.Interrupted = true; r.Retryable = true; return; }
                    if (thread != null) { fresh = true; continue; }
                    if (reply.ErrorCode == -2)
                    {
                        if (troubles++ < 2 && ct.Sleep(3000 * troubles)) continue;
                        r.Error = "Gemini answered with its own error message instead of a reply. It is often busy, or limits how fast a free account may ask. Try again in a moment.";
                        r.Retryable = true;
                        return;
                    }
                    if ((status == 400 || status == 401 || status == 403) && !reloaded) { reloaded = true; reload = true; continue; }
                    if (reply.ErrorCode == 1052 && extended) { plain = true; Quirks.Flag(quirk, true); continue; }
                    if (reply.ErrorCode != 0) r.Error = GeminiProto.Describe(reply.ErrorCode);
                    else if (err != null) r.Error = err;
                    else if (status != 200 && status != 0) r.Error = "Gemini refused the request (HTTP " + status.ToString(CultureInfo.InvariantCulture) + "). Check that you are signed in at gemini.google.com in " + GeminiHelper.BrowserName + ".";
                    else if (frames.Garbage) r.Error = "Gemini's answer could not be read. Its site may have changed and MinGent needs an update.";
                    else r.Error = "Gemini's reply was empty. Try again.";
                    return;
                }
                HashSet<string> known = new HashSet<string>();
                foreach (ToolSpec t in req.Tools) known.Add(t.Name);
                string final = said;
                List<ToolCall> calls = GeminiPrompt.Extract(ref final, known);
                if (req.NoCalls || req.Tools.Count == 0) calls.Clear();
                if (calls.Count == 0 && final.Trim().Length > 0) final = final.TrimEnd() + reply.SourceList();
                view.Update(final, reply.Thoughts, true);
                r.Text = final;
                r.Reasoning = reply.Thoughts;
                r.ToolCalls.AddRange(calls);
                r.Stop = calls.Count > 0 ? "tool_calls" : "stop";
                r.SawEnd = reply.Complete;
                r.UsedWeb = reply.UsedWeb;
                r.InputTokens = Math.Max(1, (int)(total / 3.6));
                r.CachedTokens = covered > 0 ? Math.Min(r.InputTokens, (int)(GeminiPrompt.Size(req, covered) / 3.6)) : 0;
                if (!string.IsNullOrEmpty(reply.Cid)) GeminiWeb.SaveThread(thread, key, sig, reply.Meta());
                else GeminiWeb.DropThread(thread);
                return;
            }
            r.Error = "Gemini kept refusing the request. Try again in a moment.";
        }
    }
}
