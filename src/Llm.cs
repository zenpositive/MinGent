using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGent
{
    class ToolSpec
    {
        public string Name, Description;
        public Dictionary<string, object> Schema;
        public ToolSpec(string name, string description, Dictionary<string, object> schema) { Name = name; Description = description; Schema = schema; }
    }

    class ToolCall
    {
        public string Id = "", Name = "", Arguments = "";
        public string Signature;
        public Dictionary<string, object> Implied;
        public bool Truncated;
        Dictionary<string, object> parsed;
        bool parsedTried;

        public Dictionary<string, object> Args
        {
            get
            {
                if (!parsedTried)
                {
                    parsedTried = true;
                    parsed = Json.TryParse(string.IsNullOrEmpty(Arguments) ? "{}" : Arguments) as Dictionary<string, object>;
                    if (parsed == null && !Truncated) parsed = LlmText.RepairArgs(Arguments);
                }
                return parsed;
            }
        }

        public bool ArgsValid { get { return Args != null; } }

        public void SetArgs(Dictionary<string, object> a)
        {
            parsed = a;
            parsedTried = true;
            Arguments = Json.Write(a);
        }
    }

    class ImageData
    {
        public string MediaType = "image/png";
        public string Base64 = "";
        public ImageData(string mediaType, string base64) { MediaType = mediaType; Base64 = base64; }
        public string DataUrl { get { return "data:" + MediaType + ";base64," + Base64; } }
    }

    static class EffortLevels
    {
        public static string Nearest(string want, IList<string> options)
        {
            if (options == null || options.Count == 0 || string.IsNullOrEmpty(want)) return null;
            if (options.Contains(want)) return want;
            string[] order;
            switch (want)
            {
                case "none": case "off": order = new string[] { "none", "off", "minimal", "low" }; break;
                case "minimal": order = new string[] { "minimal", "low", "none", "off", "medium" }; break;
                case "low": order = new string[] { "low", "minimal", "medium", "on" }; break;
                case "high": order = new string[] { "high", "xhigh", "max", "medium", "on", "low" }; break;
                case "xhigh": order = new string[] { "xhigh", "max", "high", "medium", "on" }; break;
                case "max": order = new string[] { "max", "xhigh", "high", "medium", "on" }; break;
                default: order = new string[] { "medium", "on", "high", "low", "xhigh" }; break;
            }
            foreach (string v in order) if (options.Contains(v)) return v;
            return null;
        }

        public static string Name(string e)
        {
            switch (e ?? "")
            {
                case "none": case "off": return "Off";
                case "on": return "Thinking on";
                case "xhigh": return "Extra high";
                case "": return "";
            }
            return char.ToUpperInvariant(e[0]) + e.Substring(1);
        }
    }

    class Msg
    {
        public string Role = "user";
        public string Text = "";
        public List<ImageData> Images;
        public List<ToolCall> ToolCalls;
        public string ToolCallId, ToolName;
        public bool ToolError;
        public string Reasoning;
        public string RawProvider;
        public List<object> RawBlocks;
        public string Compact;
        public bool Hidden;
        public bool Snapshot;
        public bool NoThinking;
        public int Turn;
        public long Id;
        static long nextId;

        public Msg() { Id = System.Threading.Interlocked.Increment(ref nextId); }
        public Msg(string role, string text) : this() { Role = role; Text = text ?? ""; }

        public static Msg ToolResult(ToolCall call, string text, bool error, List<ImageData> images)
        {
            Msg m = new Msg("tool", text);
            m.ToolCallId = call.Id;
            m.ToolName = call.Name;
            m.ToolError = error;
            m.Images = images;
            return m;
        }

        public int ApproxChars
        {
            get
            {
                int n = (Text ?? "").Length + (Reasoning != null ? Reasoning.Length / 4 : 0) + 16;
                if (ToolCalls != null) foreach (ToolCall c in ToolCalls) n += c.Name.Length + (c.Arguments ?? "").Length + 24;
                if (Images != null) n += Images.Count * 3000;
                return n;
            }
        }
    }

    class LlmRequest
    {
        public string System = "";
        public List<Msg> Messages = new List<Msg>();
        public List<ToolSpec> Tools = new List<ToolSpec>();
        public int MaxTokens = 8192;
        public string Effort;
        public bool ShowThinking = true;
    }

    class LlmResult
    {
        public string Text = "", Reasoning = "";
        public List<ToolCall> ToolCalls = new List<ToolCall>();
        public List<object> RawBlocks;
        public string RawProvider;
        public string Stop = "";
        public string Error;
        public int Status;
        public bool ConnectFailed, Unauthorized, Retryable, ContextOverflow, Cancelled, Interrupted, SawEnd, EngineGone, ParseFailure;
        public int InputTokens, OutputTokens;
        public bool Truncated { get { return Stop == "length"; } }

        public Msg ToMessage()
        {
            Msg m = new Msg("assistant", Text);
            m.Reasoning = Reasoning.Length > 0 ? Reasoning : null;
            m.ToolCalls = ToolCalls.Count > 0 ? new List<ToolCall>(ToolCalls) : null;
            m.RawProvider = RawProvider;
            m.RawBlocks = RawBlocks;
            return m;
        }
    }

    interface IStreamSink
    {
        void OnText(string delta);
        void OnReasoning(string delta);
        void OnToolProgress(string name, int argChars);
        void OnPromptProgress(int processed, int total, int cached);
    }

    static class Quirks
    {
        static readonly Dictionary<string, object> map = new Dictionary<string, object>();
        public static bool Has(string key) { lock (map) return map.ContainsKey(key); }
        public static void Flag(string key, bool on) { lock (map) { if (on) map[key] = true; else map.Remove(key); } }
        public static T Get<T>(string key) where T : class { lock (map) { object o; return map.TryGetValue(key, out o) ? o as T : null; } }
        public static void Put(string key, object v) { lock (map) { if (v == null) map.Remove(key); else map[key] = v; } }
    }

    abstract class LlmBackend
    {
        public string Model = "";
        public string Provider = "";
        public abstract LlmResult Stream(LlmRequest req, IStreamSink sink, CancelToken ct);

        protected static void Classify(LlmResult r)
        {
            if (r.Error == null) return;
            string low = r.Error.ToLowerInvariant();
            if (r.Status == 401 || r.Status == 403 || low.Contains("invalid api key") || low.Contains("invalid x-api-key") || low.Contains("incorrect api key") || low.Contains("unauthorized") ||
                low.Contains("api key not valid") || low.Contains("api_key_invalid"))
                r.Unauthorized = true;
            if (r.Status == 429 || r.Status == 500 || r.Status == 502 || r.Status == 503 || r.Status == 504 || r.Status == 529 ||
                low.Contains("overloaded") || low.Contains("rate limit") || low.Contains("temporarily") || low.Contains("try again"))
                r.Retryable = true;
            if (low.Contains("context") && (low.Contains("length") || low.Contains("window") || low.Contains("exceed") || low.Contains("too long") || low.Contains("overflow") || low.Contains("maximum")))
                r.ContextOverflow = true;
            if (low.Contains("prompt is too long") || low.Contains("n_ctx") || low.Contains("too many tokens")) r.ContextOverflow = true;
            if ((r.Status == 200 || r.Status >= 500) && ((low.Contains("does not match the expected") && low.Contains("format")) || low.Contains("failed to parse input at pos")))
            {
                if (!r.Retryable) r.ParseFailure = true;
                r.Retryable = true;
            }
        }

        protected static string HeaderOr(HttpWebResponse resp, string name)
        {
            try { return resp.Headers[name]; } catch { return null; }
        }

        protected LlmResult PostStream(string url, Dictionary<string, string> headers, string body, int timeoutMs, CancelToken ct, Action<string, string, LlmResult> onEvent)
        {
            LlmResult r = new LlmResult();
            HttpWebRequest req = null;
            Action abort = null;
            try
            {
                Dictionary<string, string> h = new Dictionary<string, string>(headers);
                h["Content-Type"] = "application/json";
                h["Accept"] = "text/event-stream";
                req = Http.Create(url, "POST", timeoutMs, h);
                req.AllowReadStreamBuffering = false;
                HttpWebRequest captured = req;
                abort = delegate { try { captured.Abort(); } catch { } };
                ct.Register(abort);
                if (ct.IsCancelled) { r.Cancelled = true; r.Stop = "cancelled"; return r; }
                byte[] b = Encoding.UTF8.GetBytes(body);
                req.ContentLength = b.Length;
                using (Stream s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    r.Status = (int)resp.StatusCode;
                    string ctype = resp.ContentType ?? "";
                    using (Stream s = resp.GetResponseStream())
                    {
                        if (ctype.IndexOf("event-stream", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            string whole = Http.ReadText(s, 32 * 1024 * 1024, null);
                            onEvent("__whole__", whole, r);
                            r.SawEnd = true;
                        }
                        else
                        {
                            SseReader sse = new SseReader(s);
                            string ev, data;
                            while (sse.Next(out ev, out data))
                            {
                                if (ct.IsCancelled) { r.Cancelled = true; break; }
                                if (data == "[DONE]") { r.SawEnd = true; break; }
                                onEvent(ev, data, r);
                                if (r.Error != null || r.Stop == "__end__") break;
                            }
                        }
                    }
                }
            }
            catch (OfflineException off)
            {
                r.Error = off.Message;
            }
            catch (WebException wex)
            {
                if (ct.IsCancelled) r.Cancelled = true;
                else
                {
                    HttpResult hr = new HttpResult();
                    Http.ReadError(hr, wex);
                    r.Status = hr.Status;
                    r.Error = hr.Error;
                    r.ConnectFailed = hr.ConnectFailed && r.Text.Length == 0;
                    if (hr.Status == 0 && !hr.ConnectFailed) r.Retryable = true;
                }
            }
            catch (IOException ex)
            {
                if (ct.IsCancelled) r.Cancelled = true;
                else { r.Error = "The connection was interrupted: " + ex.Message; r.Retryable = true; }
            }
            catch (Exception ex)
            {
                if (ct.IsCancelled) r.Cancelled = true;
                else r.Error = ex.Message;
            }
            finally
            {
                if (abort != null) ct.Unregister(abort);
            }
            if (r.Cancelled) { r.Stop = "cancelled"; r.Error = null; }
            if (r.Stop == "__end__") r.Stop = "";
            Classify(r);
            return r;
        }
    }

    static class LlmText
    {
        public static Dictionary<string, object> RepairArgs(string s)
        {
            if (string.IsNullOrEmpty(s)) return new Dictionary<string, object>();
            string t = s.Trim();
            if (t.StartsWith("```"))
            {
                int nl = t.IndexOf('\n');
                if (nl > 0) t = t.Substring(nl + 1);
                if (t.EndsWith("```")) t = t.Substring(0, t.Length - 3);
                t = t.Trim();
            }
            object o = Json.TryParse(t);
            if (o is Dictionary<string, object>) return (Dictionary<string, object>)o;
            string inner = o as string;
            if (inner != null)
            {
                o = Json.TryParse(inner);
                if (o is Dictionary<string, object>) return (Dictionary<string, object>)o;
            }
            int a = t.IndexOf('{'), b = t.LastIndexOf('}');
            if (a >= 0 && b > a)
            {
                o = Json.TryParse(t.Substring(a, b - a + 1));
                if (o is Dictionary<string, object>) return (Dictionary<string, object>)o;
            }
            if (a >= 0)
            {
                string fixedUp = t.Substring(a);
                int depth = 0; bool inStr = false, esc = false;
                foreach (char c in fixedUp)
                {
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == '"') inStr = !inStr;
                    else if (!inStr && c == '{') depth++;
                    else if (!inStr && c == '}') depth--;
                }
                if (inStr) fixedUp += "\"";
                while (depth-- > 0) fixedUp += "}";
                o = Json.TryParse(fixedUp);
                if (o is Dictionary<string, object>) return (Dictionary<string, object>)o;
            }
            return null;
        }

        static readonly Regex HermesCall = new Regex(@"<tool_call>\s*(.*?)\s*</tool_call>", RegexOptions.Singleline);
        static readonly Regex LmsCall = new Regex(@"\[TOOL_REQUEST\]\s*(.*?)\s*\[END_TOOL_REQUEST\]", RegexOptions.Singleline);
        static readonly Regex XmlFunc = new Regex(@"<function=([\w\-\.]+)>\s*(.*?)\s*</function>", RegexOptions.Singleline);
        static readonly Regex XmlParam = new Regex(@"<parameter=([\w\-\.]+)>\r?\n?(.*?)\r?\n?</parameter>", RegexOptions.Singleline);
        static readonly Regex FencedJson = new Regex(@"```(?:json|tool_call|tool)?\s*\n(\{.*?\})\s*\n```", RegexOptions.Singleline);

        public static List<ToolCall> ExtractTextToolCalls(ref string text, ICollection<string> known)
        {
            List<ToolCall> calls = new List<ToolCall>();
            if (string.IsNullOrEmpty(text) || known == null || known.Count == 0) return calls;
            string t = text;
            bool found = false;
            foreach (Regex rx in new Regex[] { HermesCall, LmsCall })
            {
                foreach (Match m in rx.Matches(t))
                {
                    ToolCall c = FromJsonOrXml(m.Groups[1].Value, known);
                    if (c != null) { calls.Add(c); found = true; }
                }
                if (found) { t = rx.Replace(t, ""); break; }
            }
            if (!found)
            {
                foreach (Match m in XmlFunc.Matches(t))
                {
                    ToolCall c = FromXml(m.Groups[1].Value, m.Groups[2].Value, known);
                    if (c != null) { calls.Add(c); found = true; }
                }
                if (found) t = XmlFunc.Replace(t, "").Replace("<tool_call>", "").Replace("</tool_call>", "");
            }
            if (!found)
            {
                MatchCollection ms = FencedJson.Matches(t);
                if (ms.Count > 0)
                {
                    string rest = FencedJson.Replace(t, "").Trim();
                    if (rest.Length < 400)
                    {
                        foreach (Match m in ms)
                        {
                            ToolCall c = FromJsonOrXml(m.Groups[1].Value, known);
                            if (c != null) { calls.Add(c); found = true; }
                        }
                        if (found) t = rest;
                    }
                }
            }
            if (!found)
            {
                string tr = t.Trim();
                if (tr.StartsWith("{") && tr.EndsWith("}") && tr.Length < 200000)
                {
                    ToolCall c = FromJsonOrXml(tr, known);
                    if (c != null) { calls.Add(c); found = true; t = ""; }
                }
            }
            if (found)
            {
                for (int i = 0; i < calls.Count; i++) calls[i].Id = "call_t" + DateTime.UtcNow.Ticks.ToString("x") + i;
                text = t.Trim();
            }
            return calls;
        }

        static ToolCall FromJsonOrXml(string body, ICollection<string> known)
        {
            string b = body.Trim();
            Match xm = XmlFunc.Match(b);
            if (xm.Success) return FromXml(xm.Groups[1].Value, xm.Groups[2].Value, known);
            Dictionary<string, object> d = RepairArgs(b);
            if (d == null) return null;
            string name = Json.Str(d, "name") ?? Json.Str(d, "function", "name") ?? Json.Str(d, "tool") ?? Json.Str(d, "tool_name");
            if (name == null || !known.Contains(name)) return null;
            object args = Json.Get(d, "arguments") ?? Json.Get(d, "parameters") ?? Json.Get(d, "args") ?? Json.Get(d, "input") ?? Json.Get(d, "function", "arguments");
            ToolCall c = new ToolCall();
            c.Name = name;
            if (args is string) c.Arguments = (string)args;
            else c.Arguments = Json.Write(args ?? new Dictionary<string, object>());
            return c;
        }

        static ToolCall FromXml(string name, string body, ICollection<string> known)
        {
            if (!known.Contains(name)) return null;
            Dictionary<string, object> d = new Dictionary<string, object>();
            foreach (Match p in XmlParam.Matches(body))
            {
                string v = p.Groups[2].Value;
                object parsed = null;
                string tv = v.Trim();
                if (tv == "true") parsed = true;
                else if (tv == "false") parsed = false;
                else if (tv.Length > 0 && (char.IsDigit(tv[0]) || tv[0] == '-') && Regex.IsMatch(tv, @"^-?\d+(\.\d+)?$"))
                    parsed = double.Parse(tv, CultureInfo.InvariantCulture);
                else if ((tv.StartsWith("[") && tv.EndsWith("]")) || (tv.StartsWith("{") && tv.EndsWith("}"))) parsed = Json.TryParse(tv);
                d[p.Groups[1].Value] = parsed ?? (object)v;
            }
            ToolCall c = new ToolCall();
            c.Name = name;
            c.Arguments = Json.Write(d);
            return c;
        }
    }

    class ThinkSplitter
    {
        readonly StringBuilder pending = new StringBuilder();
        bool inThink, sawAny;
        public string Open = "<think>", Close = "</think>";
        public readonly StringBuilder Visible = new StringBuilder();
        public readonly StringBuilder Thought = new StringBuilder();
        readonly IStreamSink sink;

        public ThinkSplitter(IStreamSink sink) { this.sink = sink; }

        public void Feed(string piece)
        {
            pending.Append(piece);
            Pump(false);
        }

        public void Finish() { Pump(true); }

        void Pump(bool final)
        {
            while (pending.Length > 0)
            {
                string p = pending.ToString();
                string tag = inThink ? Close : Open;
                int at = p.IndexOf(tag, StringComparison.Ordinal);
                if (!inThink && !sawAny)
                {
                    int close = p.IndexOf(Close, StringComparison.Ordinal);
                    if (close >= 0 && (at < 0 || close < at))
                    {
                        Emit(p.Substring(0, close), true);
                        pending.Remove(0, close + Close.Length);
                        sawAny = true;
                        continue;
                    }
                }
                if (at >= 0)
                {
                    Emit(p.Substring(0, at), inThink);
                    pending.Remove(0, at + tag.Length);
                    inThink = !inThink;
                    sawAny = true;
                    continue;
                }
                int keep = final ? 0 : PartialSuffix(p, tag);
                if (!inThink && !sawAny && !final) keep = Math.Max(keep, PartialSuffix(p, Close));
                if (!sawAny && !inThink && !final && p.Length < 12 && Open.StartsWith(p.TrimStart(), StringComparison.Ordinal)) keep = p.Length;
                int emit = p.Length - keep;
                if (emit <= 0) break;
                Emit(p.Substring(0, emit), inThink);
                pending.Remove(0, emit);
            }
        }

        static int PartialSuffix(string s, string tag)
        {
            for (int k = Math.Min(tag.Length - 1, s.Length); k > 0; k--)
                if (string.CompareOrdinal(s, s.Length - k, tag, 0, k) == 0) return k;
            return 0;
        }

        public bool DropThought;

        void Emit(string s, bool thought)
        {
            if (s.Length == 0) return;
            if (thought) { if (DropThought) return; Thought.Append(s); if (sink != null) sink.OnReasoning(s); }
            else { Visible.Append(s); if (sink != null) sink.OnText(s); }
        }
    }

    class OpenAiChatBackend : LlmBackend
    {
        public string BaseUrl = "", ApiKey = "";
        public bool OfficialOpenAi;
        public bool Gemini, GeminiBudget;
        public const string SkipSignature = "skip_thought_signature_validator";
        public bool SendUsage = true;
        public bool PromptProgress;
        public bool BudgetThinking;
        public int BudgetContext;

        public static int ThinkingBudget(string level, int context)
        {
            int b = level == "low" ? 1024 : level == "medium" ? 4096 : level == "high" ? 16384 : 0;
            if (b > 0 && context > 0) b = Math.Max(512, Math.Min(b, context / 4));
            return b;
        }
        public string LmStudioReasoning;
        public Dictionary<string, object> Sampling;
        public int TimeoutMs = 30 * 60 * 1000;
        public bool SupportsVision = true;
        public Func<string> Gate;
        static readonly Dictionary<string, List<string>> acceptedEffort = new Dictionary<string, List<string>>();

        string Key { get { return BaseUrl + "|" + Model; } }

        static readonly Dictionary<string, List<string>> learnedByModel = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public static List<string> Learned(string model)
        {
            List<string> l;
            lock (learnedByModel) return learnedByModel.TryGetValue(model ?? "", out l) && l != null ? new List<string>(l) : null;
        }

        public override LlmResult Stream(LlmRequest req, IStreamSink sink, CancelToken ct)
        {
            bool tryWithoutExtras = false, triedNoTools = false;
            LlmResult r = null;
            string effort = MapEffort(req.Effort);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                string gone = Gate != null ? Gate() : null;
                if (gone != null)
                {
                    LlmResult g = new LlmResult();
                    g.Error = gone;
                    g.EngineGone = true;
                    return g;
                }
                bool usage = SendUsage && !Quirks.Has("chat|nousage|" + BaseUrl) && !tryWithoutExtras;
                string eff = tryWithoutExtras ? null : effort;
                string body = BuildBody(req, eff, usage, triedNoTools, !tryWithoutExtras && !OfficialOpenAi);
                r = StreamOnce(body, req, sink, ct);
                if (r.Error == null || r.Cancelled || r.Text.Length > 0 || r.ToolCalls.Count > 0 || r.ContextOverflow) break;
                string low = r.Error.ToLowerInvariant();
                if (low.Contains("tool call arguments") && !Quirks.Has(ObjArgsKey) && HasBadArgs(req)) { Quirks.Flag(ObjArgsKey, true); continue; }
                if (low.Contains("roles must alternate") && !Quirks.Has(AlternateKey) && UserAfterTool(req)) { Quirks.Flag(AlternateKey, true); continue; }
                bool effortProblem = eff != null && (low.Contains("reasoning") || low.Contains("effort") || low.Contains("think"));
                bool badRequest = r.Status == 400 || r.Status == 422;
                if (!badRequest && !(effortProblem && (r.Status == 200 || r.Status >= 500))) break;
                bool retry = false;
                if (effortProblem)
                {
                    List<string> acc = AcceptedFrom(r.Error) ?? new List<string>();
                    acc.Remove(eff);
                    lock (acceptedEffort) acceptedEffort[Key] = acc;
                    lock (learnedByModel) learnedByModel[Model ?? ""] = acc;
                    effort = MapEffort(req.Effort);
                    retry = true;
                }
                if (usage && (low.Contains("stream_options") || low.Contains("include_usage"))) { Quirks.Flag("chat|nousage|" + BaseUrl, true); retry = true; }
                if (!retry && !triedNoTools && req.Tools.Count > 0 && (low.Contains("tool") || low.Contains("function")) && !low.Contains("context"))
                {
                    triedNoTools = true;
                    retry = true;
                }
                if (!retry && !tryWithoutExtras) { tryWithoutExtras = true; retry = true; }
                if (!retry) break;
            }
            return r;
        }

        string ObjArgsKey { get { return "chat|objargs|" + Key; } }

        static bool BadArgs(string a) { return !(Json.TryParse(string.IsNullOrEmpty(a) ? "{}" : a) is Dictionary<string, object>); }

        string AlternateKey { get { return "chat|alternate|" + Key; } }

        bool UserAfterTool(LlmRequest req)
        {
            for (int i = 1; i < req.Messages.Count; i++) if (req.Messages[i].Role == "user" && req.Messages[i - 1].Role == "tool") return true;
            if (SupportsVision) foreach (Msg m in req.Messages) if (m.Role == "tool" && m.Images != null && m.Images.Count > 0) return true;
            return false;
        }

        static bool HasBadArgs(LlmRequest req)
        {
            foreach (Msg m in req.Messages)
                if (m.Role == "assistant" && m.ToolCalls != null)
                    foreach (ToolCall c in m.ToolCalls) if (BadArgs(c.Arguments)) return true;
            return false;
        }

        string MapEffort(string want)
        {
            if (string.IsNullOrEmpty(want)) return null;
            string w = want == "off" ? "none" : want == "on" ? "medium" : want;
            List<string> acc;
            lock (acceptedEffort) acceptedEffort.TryGetValue(Key, out acc);
            if (LmStudioReasoning != null && acc == null)
            {
                List<string> opts = new List<string>(LmStudioReasoning.Split(','));
                bool graded = false;
                foreach (string o in opts) if (o != "on" && o != "off") graded = true;
                if (!graded) return w == "none" ? "none" : "medium";
            }
            if (acc == null) return w;
            return EffortLevels.Nearest(w, acc);
        }

        public static List<string> AcceptedFrom(string error)
        {
            Match lead = Regex.Match(error, "\\b(supported|allowed|valid|expected|one of|must be|possible|accepted)\\b", RegexOptions.IgnoreCase);
            if (lead.Success) error = error.Substring(lead.Index);
            List<string> found = new List<string>();
            foreach (string v in new string[] { "none", "minimal", "low", "medium", "high", "xhigh", "max" })
                if (Regex.IsMatch(error, "(?<![A-Za-z0-9_-])" + v + "(?![A-Za-z0-9_-])", RegexOptions.IgnoreCase)) found.Add(v);
            return found.Count >= 1 ? found : null;
        }

        string BuildBody(LlmRequest req, string effort, bool usage, bool noTools, bool reasoningBack)
        {
            List<object> msgs = new List<object>();
            int lastUser = -1;
            for (int i = req.Messages.Count - 1; i >= 0; i--) if (req.Messages[i].Role == "user" && !req.Messages[i].Hidden) { lastUser = i; break; }
            if (!string.IsNullOrEmpty(req.System))
                msgs.Add(Json.O("role", OfficialOpenAi ? "developer" : "system", "content", req.System));
            List<ImageData> pendingImages = new List<ImageData>();
            bool objArgs = Quirks.Has(ObjArgsKey), alternate = Quirks.Has(AlternateKey);
            for (int i = 0; i < req.Messages.Count; i++)
            {
                Msg m = req.Messages[i];
                if (m.Role == "tool")
                {
                    if (noTools)
                    {
                        msgs.Add(Json.O("role", "user", "content", "Result of " + m.ToolName + ":\n" + m.Text));
                        continue;
                    }
                    msgs.Add(Json.O("role", "tool", "tool_call_id", m.ToolCallId ?? "", "content", m.Text ?? ""));
                    if (m.Images != null) pendingImages.AddRange(m.Images);
                    bool nextIsTool = i + 1 < req.Messages.Count && req.Messages[i + 1].Role == "tool";
                    if (!nextIsTool && pendingImages.Count > 0)
                    {
                        if (SupportsVision)
                        {
                            if (alternate) msgs.Add(Json.O("role", "assistant", "content", "(no reply)"));
                            msgs.Add(UserMsg("(Images returned by the tools above.)", pendingImages));
                        }
                        pendingImages = new List<ImageData>();
                    }
                    continue;
                }
                if (m.Role == "assistant")
                {
                    Dictionary<string, object> a = Json.O("role", "assistant", "content", m.Text ?? "");
                    if (Gemini && string.IsNullOrEmpty(m.Text) && m.ToolCalls != null && m.ToolCalls.Count > 0 && !noTools) a["content"] = null;
                    if (reasoningBack && !Gemini && !noTools && i > lastUser && !string.IsNullOrEmpty(m.Reasoning) && m.ToolCalls != null && m.ToolCalls.Count > 0) a["reasoning_content"] = m.Reasoning;
                    if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                    {
                        if (noTools)
                        {
                            StringBuilder sb = new StringBuilder(m.Text ?? "");
                            foreach (ToolCall c in m.ToolCalls) sb.Append("\n[called ").Append(c.Name).Append(' ').Append(c.Arguments).Append(']');
                            a["content"] = sb.ToString();
                        }
                        else
                        {
                            List<object> calls = new List<object>();
                            bool signed = false;
                            foreach (ToolCall c in m.ToolCalls) if (!string.IsNullOrEmpty(c.Signature)) signed = true;
                            foreach (ToolCall c in m.ToolCalls)
                            {
                                string args = string.IsNullOrEmpty(c.Arguments) ? "{}" : c.Arguments;
                                if (objArgs && BadArgs(args)) args = c.Args != null ? Json.Write(c.Args) : "{}";
                                Dictionary<string, object> call = Json.O("id", c.Id, "type", "function", "function", Json.O("name", c.Name, "arguments", args));
                                if (Gemini)
                                {
                                    string sig = !string.IsNullOrEmpty(c.Signature) ? c.Signature : signed ? null : SkipSignature;
                                    if (sig != null) call["extra_content"] = Json.O("google", Json.O("thought_signature", sig));
                                }
                                calls.Add(call);
                            }
                            a["tool_calls"] = calls;
                        }
                    }
                    msgs.Add(a);
                    continue;
                }
                Dictionary<string, object> um = UserMsg(m.Text, SupportsVision ? m.Images : null);
                Dictionary<string, object> prevMsg = msgs.Count > 0 ? msgs[msgs.Count - 1] as Dictionary<string, object> : null;
                if (alternate && prevMsg != null && Json.Str(prevMsg, "role") == "tool") { prevMsg = Json.O("role", "assistant", "content", "(no reply)"); msgs.Add(prevMsg); }
                if (prevMsg != null && Json.Str(prevMsg, "role") == "user")
                {
                    object pc = prevMsg["content"], nc = um["content"];
                    if (pc is string && nc is string) prevMsg["content"] = (string)pc + "\n\n" + (string)nc;
                    else
                    {
                        List<object> parts = new List<object>();
                        foreach (object c in new object[] { pc, nc })
                        {
                            if (c is string) parts.Add(Json.O("type", "text", "text", (string)c));
                            else if (c is List<object>) parts.AddRange((List<object>)c);
                        }
                        prevMsg["content"] = parts;
                    }
                    continue;
                }
                msgs.Add(um);
            }
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = Model;
            body["messages"] = msgs;
            body["stream"] = true;
            if (req.Tools.Count > 0 && !noTools)
            {
                List<object> tools = new List<object>();
                foreach (ToolSpec t in req.Tools)
                    tools.Add(Json.O("type", "function", "function", Json.O("name", t.Name, "description", t.Description, "parameters", t.Schema)));
                body["tools"] = tools;
                body["tool_choice"] = "auto";
            }
            if (OfficialOpenAi) body["max_completion_tokens"] = req.MaxTokens;
            else if (req.MaxTokens > 0) body["max_tokens"] = req.MaxTokens;
            if (BudgetThinking)
            {
                if (req.Effort == "off") body["reasoning_effort"] = "none";
                else
                {
                    int budget = ThinkingBudget(req.Effort, BudgetContext);
                    if (budget > 0)
                    {
                        body["reasoning_budget_tokens"] = budget;
                        body["reasoning_budget_message"] = "\n\nI have thought enough. Now I will act on what I have worked out so far.\n";
                    }
                }
            }
            else if (Gemini && effort != null && effort != "none" && req.ShowThinking && reasoningBack)
            {
                Dictionary<string, object> cfg = new Dictionary<string, object>();
                if (GeminiBudget) cfg["thinking_budget"] = effort == "minimal" ? 512 : effort == "low" ? 1024 : effort == "medium" ? 8192 : 24576;
                else cfg["thinking_level"] = effort == "xhigh" || effort == "max" ? "high" : effort;
                cfg["include_thoughts"] = true;
                body["extra_body"] = Json.O("google", Json.O("thinking_config", cfg));
            }
            else if (effort != null) body["reasoning_effort"] = effort;
            if (usage) body["stream_options"] = Json.O("include_usage", true);
            if (usage && PromptProgress) body["return_progress"] = true;
            if (Sampling != null && !OfficialOpenAi)
                foreach (KeyValuePair<string, object> kv in Sampling) body[kv.Key] = kv.Value;
            return Json.Write(body);
        }

        static Dictionary<string, object> UserMsg(string text, List<ImageData> images)
        {
            if (images == null || images.Count == 0) return Json.O("role", "user", "content", text ?? "");
            List<object> parts = new List<object>();
            parts.Add(Json.O("type", "text", "text", text ?? ""));
            foreach (ImageData img in images) parts.Add(Json.O("type", "image_url", "image_url", Json.O("url", img.DataUrl)));
            return Json.O("role", "user", "content", parts);
        }

        class Acc
        {
            public ThinkSplitter Split;
            public readonly StringBuilder Reasoning = new StringBuilder();
            public readonly List<ToolCall> Calls = new List<ToolCall>();
            public readonly List<StringBuilder> Args = new List<StringBuilder>();
            public string Finish;
        }

        LlmResult StreamOnce(string body, LlmRequest req, IStreamSink sink, CancelToken ct)
        {
            Dictionary<string, string> h = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(ApiKey)) h["Authorization"] = "Bearer " + ApiKey;
            Acc acc = new Acc();
            acc.Split = new ThinkSplitter(sink);
            if (Gemini) { acc.Split.Open = "<thought>"; acc.Split.Close = "</thought>"; }
            LlmResult r = PostStream(BaseUrl.TrimEnd('/') + "/chat/completions", h, body, TimeoutMs, ct,
                delegate (string ev, string data, LlmResult res) { OnData(ev, data, res, acc, sink); });
            acc.Split.Finish();
            r.Text = acc.Split.Visible.ToString();
            string reasoning = acc.Reasoning.ToString();
            if (acc.Split.Thought.Length > 0) reasoning = reasoning.Length > 0 ? reasoning + "\n" + acc.Split.Thought : acc.Split.Thought.ToString();
            r.Reasoning = reasoning;
            for (int i = 0; i < acc.Calls.Count; i++)
            {
                ToolCall c = acc.Calls[i];
                c.Arguments = acc.Args[i].ToString();
                if (string.IsNullOrEmpty(c.Id)) c.Id = "call_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                if (c.Name.Length > 0) r.ToolCalls.Add(c);
            }
            if (r.ToolCalls.Count > 0 && !r.Cancelled && r.Error == null && (acc.Finish == "length" || string.IsNullOrEmpty(acc.Finish)))
            {
                ToolCall last = r.ToolCalls[r.ToolCalls.Count - 1];
                if (!string.IsNullOrEmpty(last.Arguments) && Json.TryParse(last.Arguments) == null)
                {
                    last.Truncated = true;
                    acc.Finish = "length";
                }
            }
            if (r.ToolCalls.Count == 0 && req.Tools.Count > 0 && r.Error == null)
            {
                HashSet<string> known = new HashSet<string>();
                foreach (ToolSpec t in req.Tools) known.Add(t.Name);
                string text = r.Text;
                List<ToolCall> tc = LlmText.ExtractTextToolCalls(ref text, known);
                if (tc.Count == 0 && reasoning.Length > 0 && r.Text.Trim().Length == 0)
                {
                    string rz = reasoning;
                    tc = LlmText.ExtractTextToolCalls(ref rz, known);
                }
                if (tc.Count > 0) { r.ToolCalls.AddRange(tc); r.Text = text; }
            }
            if (!r.Cancelled && r.Error == null && string.IsNullOrEmpty(acc.Finish) && !r.SawEnd)
            {
                r.Error = "The reply stopped before it was finished: the connection to the model server ended early.";
                r.Interrupted = true;
                r.Retryable = true;
                return r;
            }
            if (acc.Finish == "length") r.Stop = "length";
            else if (r.ToolCalls.Count > 0) r.Stop = "tool_calls";
            else if (r.Stop != "cancelled") r.Stop = "end";
            return r;
        }

        void OnData(string ev, string data, LlmResult r, Acc acc, IStreamSink sink)
        {
            if (ev == "__whole__")
            {
                object whole = Json.TryParse(data);
                if (whole == null) { r.Error = "The server sent an answer MinGent could not read."; return; }
                string err0 = ErrorOf(whole);
                if (err0 != null) { r.Error = err0; return; }
                Dictionary<string, object> msg = Json.Obj(whole, "choices", 0, "message");
                if (msg != null)
                {
                    string rz = Json.Str(msg, "reasoning_content") ?? Json.Str(msg, "reasoning");
                    if (!string.IsNullOrEmpty(rz)) { acc.Split.DropThought = true; acc.Reasoning.Append(rz); if (sink != null) sink.OnReasoning(rz); }
                    string content = Json.Str(msg, "content");
                    if (!string.IsNullOrEmpty(content)) acc.Split.Feed(content);
                    List<object> tcs = Json.List(msg, "tool_calls");
                    if (tcs != null)
                        foreach (object tc in tcs)
                        {
                            ToolCall c = new ToolCall();
                            c.Id = Json.Str(tc, "id") ?? "";
                            c.Name = Json.Str(tc, "function", "name") ?? "";
                            c.Signature = Json.Str(tc, "extra_content", "google", "thought_signature");
                            acc.Calls.Add(c);
                            object argsObj = Json.Get(tc, "function", "arguments");
                            acc.Args.Add(new StringBuilder(argsObj is string ? (string)argsObj : Json.Write(argsObj ?? new Dictionary<string, object>())));
                        }
                }
                acc.Finish = Json.Str(whole, "choices", 0, "finish_reason");
                Usage(whole, r);
                return;
            }
            object o = Json.TryParse(data);
            if (o == null)
            {
                if (data.IndexOf("\n{", StringComparison.Ordinal) >= 0)
                    foreach (string part in data.Split('\n')) { if (part.Trim().Length > 0) OnData(ev, part.Trim(), r, acc, sink); }
                return;
            }
            string err = ErrorOf(o);
            if (err != null) { r.Error = err; return; }
            Usage(o, r);
            object pp = Json.Get(o, "prompt_progress");
            if (pp != null && sink != null)
            {
                int total = Json.Int(pp, "total");
                if (total > 0) sink.OnPromptProgress(Json.Int(pp, "processed"), total, Json.Int(pp, "cache"));
            }
            Dictionary<string, object> choice = Json.Obj(o, "choices", 0);
            if (choice == null) return;
            Dictionary<string, object> delta = Json.Obj(choice, "delta") ?? Json.Obj(choice, "message");
            if (delta != null)
            {
                string rz = Json.Str(delta, "reasoning_content") ?? Json.Str(delta, "reasoning");
                if (!string.IsNullOrEmpty(rz)) { acc.Split.DropThought = true; acc.Reasoning.Append(rz); if (sink != null) sink.OnReasoning(rz); }
                string content = Json.Str(delta, "content");
                if (!string.IsNullOrEmpty(content)) acc.Split.Feed(content);
                List<object> tcs = Json.List(delta, "tool_calls");
                if (tcs != null)
                    foreach (object tc in tcs)
                    {
                        string id = Json.Str(tc, "id");
                        string nm = Json.Str(tc, "function", "name");
                        int idx;
                        if (Json.Get(tc, "index") is double) idx = Json.Int(tc, "index");
                        else if (!string.IsNullOrEmpty(id))
                        {
                            idx = acc.Calls.Count;
                            for (int k = 0; k < acc.Calls.Count; k++) if (acc.Calls[k].Id == id) { idx = k; break; }
                        }
                        else if (acc.Calls.Count == 0 || (!string.IsNullOrEmpty(nm) && acc.Calls[acc.Calls.Count - 1].Name.Length > 0)) idx = acc.Calls.Count;
                        else idx = acc.Calls.Count - 1;
                        if (idx < 0 || idx > 64) idx = acc.Calls.Count;
                        while (acc.Calls.Count <= idx) { acc.Calls.Add(new ToolCall()); acc.Args.Add(new StringBuilder()); }
                        ToolCall c = acc.Calls[idx];
                        if (!string.IsNullOrEmpty(id)) c.Id = id;
                        string sig = Json.Str(tc, "extra_content", "google", "thought_signature");
                        if (!string.IsNullOrEmpty(sig)) c.Signature = sig;
                        string name = Json.Str(tc, "function", "name");
                        if (!string.IsNullOrEmpty(name)) c.Name = c.Name.Length == 0 ? name : (c.Name == name ? c.Name : c.Name + name);
                        object args = Json.Get(tc, "function", "arguments");
                        if (args is string) acc.Args[idx].Append((string)args);
                        else if (args != null) acc.Args[idx].Append(Json.Write(args));
                        if (sink != null) sink.OnToolProgress(c.Name, acc.Args[idx].Length);
                    }
            }
            string fin = Json.Str(choice, "finish_reason");
            if (!string.IsNullOrEmpty(fin)) acc.Finish = fin;
        }

        static void Usage(object o, LlmResult r)
        {
            int pt = Json.Int(o, "usage", "prompt_tokens"), ct = Json.Int(o, "usage", "completion_tokens");
            if (pt > 0) { r.InputTokens = pt; r.OutputTokens = ct; }
        }

        static string ErrorOf(object o)
        {
            object e = Json.Get(o, "error");
            if (e == null) return null;
            string s = e as string;
            if (s != null) return s.Length > 0 ? s : null;
            string m = Json.Str(e, "message");
            return string.IsNullOrEmpty(m) ? "The server reported an error." : m;
        }
    }

    class AnthropicBackend : LlmBackend
    {
        public string ApiKey = "";
        public string BaseUrl = "https://api.anthropic.com";
        public bool SupportsAdaptive = true, SupportsDisable, SupportsBetweenTools, SupportsEnabled, SupportsEffort = true, Binding, Interleaved;
        public List<string> EffortLevels;
        public int TimeoutMs = 20 * 60 * 1000;
        string Q(string name) { return "anthropic|" + name + "|" + BaseUrl + "|" + Model; }
        bool noBinding { get { return Quirks.Has(Q("nobinding")); } set { Quirks.Flag(Q("nobinding"), value); } }
        bool noInterleaved { get { return Quirks.Has(Q("nointerleaved")); } set { Quirks.Flag(Q("nointerleaved"), value); } }

        public override LlmResult Stream(LlmRequest req, IStreamSink sink, CancelToken ct)
        {
            bool sendThinking = true, sendEffort = SupportsEffort, sendCache = true, strip = false;
            LlmResult r = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                string beta;
                string body = BuildBody(req, sendThinking, sendEffort, sendCache, strip, out beta);
                r = StreamOnce(body, beta, sink, ct);
                if (r.Error == null || r.Cancelled || r.Text.Length > 0 || r.ToolCalls.Count > 0) break;
                if (r.Status != 400) break;
                string low = r.Error.ToLowerInvariant();
                if (beta != null && (low.Contains("anthropic-beta") || low.Contains("block_binding") || low.Contains("prefix_mismatch") || low.Contains("beta header")))
                {
                    if (low.Contains("interleaved")) noInterleaved = true;
                    else noBinding = true;
                    continue;
                }
                if (!strip && (low.Contains("signature") || low.Contains("bound to a different conversation")))
                {
                    strip = true;
                    continue;
                }
                if (sendThinking && low.Contains("thinking")) { sendThinking = false; continue; }
                if (sendEffort && (low.Contains("effort") || low.Contains("output_config"))) { sendEffort = false; continue; }
                if (sendCache && low.Contains("cache")) { sendCache = false; continue; }
                break;
            }
            return r;
        }

        string EffortValue(string want)
        {
            if (string.IsNullOrEmpty(want) || want == "auto") return null;
            string w = want == "off" ? "low" : want == "on" ? null : want;
            if (w == null) return null;
            if (EffortLevels != null && EffortLevels.Count > 0 && !EffortLevels.Contains(w))
            {
                if (w == "xhigh" && EffortLevels.Contains("high")) return "high";
                if (w == "max" && EffortLevels.Contains("high")) return "high";
                return null;
            }
            return w;
        }

        string BuildBody(LlmRequest req, bool sendThinking, bool sendEffort, bool cache, bool strip, out string beta)
        {
            beta = null;
            List<string> betas = new List<string>();
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = Model;
            int maxTokens = Math.Max(1024, req.MaxTokens);
            body["max_tokens"] = maxTokens;
            body["stream"] = true;
            Dictionary<string, object> sys = Json.O("type", "text", "text", req.System ?? "");
            if (cache) sys["cache_control"] = Json.O("type", "ephemeral");
            body["system"] = Json.A(sys);
            List<object> msgs = BuildMessages(req.Messages, strip || !sendThinking);
            if (cache && msgs.Count > 0)
            {
                List<object> lastContent = Json.List(msgs[msgs.Count - 1], "content");
                if (lastContent != null && lastContent.Count > 0)
                {
                    Dictionary<string, object> lb = lastContent[lastContent.Count - 1] as Dictionary<string, object>;
                    string lt = lb != null ? Json.Str(lb, "type") : null;
                    if (lb != null && lt != "thinking" && lt != "redacted_thinking") lb["cache_control"] = Json.O("type", "ephemeral");
                }
            }
            body["messages"] = msgs;
            if (req.Tools.Count > 0)
            {
                List<object> tools = new List<object>();
                foreach (ToolSpec t in req.Tools) tools.Add(Json.O("name", t.Name, "description", t.Description, "input_schema", t.Schema));
                if (cache) ((Dictionary<string, object>)tools[tools.Count - 1])["cache_control"] = Json.O("type", "ephemeral");
                body["tools"] = tools;
            }
            if (sendThinking)
            {
                bool off = req.Effort == "off";
                if (off && SupportsBetweenTools) body["thinking"] = Json.O("type", "between_tools");
                else if (off && SupportsDisable) body["thinking"] = Json.O("type", "disabled");
                else if (SupportsAdaptive && (!off || !(SupportsDisable || SupportsBetweenTools)))
                {
                    Dictionary<string, object> th = Json.O("type", "adaptive", "display", req.ShowThinking ? "summarized" : "omitted");
                    if (Binding && !noBinding)
                    {
                        th["block_binding"] = Json.O("prefix_mismatch_behavior", "drop_block");
                        betas.Add("thinking-binding-controls-2026-08-01");
                    }
                    body["thinking"] = th;
                }
                else if (!SupportsAdaptive && SupportsEnabled && !off && maxTokens > 2048)
                {
                    int budget = req.Effort == "low" ? 2048 : req.Effort == "medium" ? 8192 : req.Effort == "high" ? 16384 : req.Effort == "max" || req.Effort == "xhigh" ? 32000 : 8192;
                    budget = Math.Max(1024, Math.Min(budget, maxTokens - 1024));
                    body["thinking"] = Json.O("type", "enabled", "budget_tokens", budget);
                    if (Interleaved && !noInterleaved) betas.Add("interleaved-thinking-2025-05-14");
                }
            }
            string eff = sendEffort && (SupportsAdaptive || EffortLevels != null) ? EffortValue(req.Effort) : null;
            if (eff != null) body["output_config"] = Json.O("effort", eff);
            if (betas.Count > 0) beta = string.Join(",", betas.ToArray());
            return Json.Write(body);
        }

        static List<object> BuildMessages(List<Msg> list, bool stripThinking)
        {
            List<object> outList = new List<object>();
            List<object> pendingTool = null;
            foreach (Msg m in list)
            {
                if (m.Role == "tool")
                {
                    if (pendingTool == null) pendingTool = new List<object>();
                    List<object> content = new List<object>();
                    content.Add(Json.O("type", "text", "text", string.IsNullOrEmpty(m.Text) ? "(no output)" : m.Text));
                    if (m.Images != null)
                        foreach (ImageData img in m.Images)
                            content.Add(Json.O("type", "image", "source", Json.O("type", "base64", "media_type", img.MediaType, "data", img.Base64)));
                    Dictionary<string, object> tr = Json.O("type", "tool_result", "tool_use_id", m.ToolCallId ?? "", "content", content);
                    if (m.ToolError) tr["is_error"] = true;
                    pendingTool.Add(tr);
                    continue;
                }
                if (pendingTool != null) { Append(outList, "user", pendingTool); pendingTool = null; }
                if (m.Role == "assistant")
                {
                    List<object> blocks;
                    if (m.RawProvider == "anthropic" && m.RawBlocks != null) blocks = CloneBlocks(m.RawBlocks, stripThinking || m.NoThinking);
                    else
                    {
                        blocks = new List<object>();
                        if (!string.IsNullOrEmpty(m.Text)) blocks.Add(Json.O("type", "text", "text", m.Text));
                        if (m.ToolCalls != null)
                            foreach (ToolCall c in m.ToolCalls)
                                blocks.Add(Json.O("type", "tool_use", "id", c.Id, "name", c.Name, "input", (object)c.Args ?? new Dictionary<string, object>()));
                    }
                    if (blocks.Count == 0) blocks.Add(Json.O("type", "text", "text", "(no text)"));
                    Append(outList, "assistant", blocks);
                    continue;
                }
                List<object> uc = new List<object>();
                if (m.Images != null)
                    foreach (ImageData img in m.Images)
                        uc.Add(Json.O("type", "image", "source", Json.O("type", "base64", "media_type", img.MediaType, "data", img.Base64)));
                uc.Add(Json.O("type", "text", "text", string.IsNullOrEmpty(m.Text) ? "(empty message)" : m.Text));
                Append(outList, "user", uc);
            }
            if (pendingTool != null) Append(outList, "user", pendingTool);
            return outList;
        }

        static List<object> CloneBlocks(List<object> blocks, bool noThinking)
        {
            List<object> res = new List<object>();
            foreach (object b in blocks)
            {
                Dictionary<string, object> d = b as Dictionary<string, object>;
                if (d == null) continue;
                string ty = Json.Str(d, "type");
                if ((ty == "thinking" || ty == "redacted_thinking") && (noThinking || (ty == "thinking" && string.IsNullOrEmpty(Json.Str(d, "signature"))))) continue;
                Dictionary<string, object> c = new Dictionary<string, object>(d);
                c.Remove("cache_control");
                res.Add(c);
            }
            return res;
        }

        static void Append(List<object> outList, string role, List<object> content)
        {
            if (outList.Count > 0 && Json.Str(outList[outList.Count - 1], "role") == role)
            {
                List<object> prev = Json.List(outList[outList.Count - 1], "content");
                if (role == "user")
                {
                    List<object> merged = new List<object>();
                    foreach (object o in prev) if (Json.Str(o, "type") == "tool_result") merged.Add(o);
                    foreach (object o in content) if (Json.Str(o, "type") == "tool_result") merged.Add(o);
                    foreach (object o in prev) if (Json.Str(o, "type") != "tool_result") merged.Add(o);
                    foreach (object o in content) if (Json.Str(o, "type") != "tool_result") merged.Add(o);
                    ((Dictionary<string, object>)outList[outList.Count - 1])["content"] = merged;
                }
                else prev.AddRange(content);
                return;
            }
            outList.Add(Json.O("role", role, "content", content));
        }

        class Block
        {
            public string Type = "";
            public StringBuilder Text = new StringBuilder();
            public StringBuilder Json2 = new StringBuilder();
            public string Signature, Id, Name, Data;
            public Dictionary<string, object> Raw;
        }

        LlmResult StreamOnce(string body, string beta, IStreamSink sink, CancelToken ct)
        {
            Dictionary<string, string> h = new Dictionary<string, string>();
            h["x-api-key"] = ApiKey;
            h["anthropic-version"] = "2023-06-01";
            if (!string.IsNullOrEmpty(beta)) h["anthropic-beta"] = beta;
            SortedDictionary<int, Block> blocks = new SortedDictionary<int, Block>();
            string stop = null, refusalNote = null;
            LlmResult r = PostStream(BaseUrl.TrimEnd('/') + "/v1/messages", h, body, TimeoutMs, ct,
                delegate (string ev, string data, LlmResult res)
                {
                    object o = Json.TryParse(data);
                    if (o == null) return;
                    string type = Json.Str(o, "type") ?? ev ?? "";
                    switch (type)
                    {
                        case "message_start":
                            res.InputTokens = Json.Int(o, "message", "usage", "input_tokens") + Json.Int(o, "message", "usage", "cache_read_input_tokens") + Json.Int(o, "message", "usage", "cache_creation_input_tokens");
                            break;
                        case "content_block_start":
                            {
                                int idx = Json.Int(o, "index");
                                Dictionary<string, object> cb = Json.Obj(o, "content_block");
                                Block b = new Block();
                                b.Type = Json.Str(cb, "type") ?? "";
                                b.Id = Json.Str(cb, "id");
                                b.Name = Json.Str(cb, "name");
                                b.Data = Json.Str(cb, "data");
                                string t0 = Json.Str(cb, "text") ?? Json.Str(cb, "thinking");
                                if (!string.IsNullOrEmpty(t0))
                                {
                                    b.Text.Append(t0);
                                    if (b.Type == "text" && sink != null) sink.OnText(t0);
                                    if (b.Type == "thinking" && sink != null) sink.OnReasoning(t0);
                                }
                                if (b.Type != "text" && b.Type != "thinking" && b.Type != "redacted_thinking" && b.Type != "tool_use") b.Raw = cb;
                                blocks[idx] = b;
                                if (b.Type == "tool_use" && sink != null) sink.OnToolProgress(b.Name ?? "", 0);
                                break;
                            }
                        case "content_block_delta":
                            {
                                int idx = Json.Int(o, "index");
                                Block b;
                                if (!blocks.TryGetValue(idx, out b)) { b = new Block(); b.Type = "text"; blocks[idx] = b; }
                                Dictionary<string, object> d = Json.Obj(o, "delta");
                                string dt = Json.Str(d, "type");
                                if (dt == "text_delta") { string s = Json.Str(d, "text") ?? ""; b.Text.Append(s); if (sink != null && s.Length > 0) sink.OnText(s); }
                                else if (dt == "thinking_delta") { string s = Json.Str(d, "thinking") ?? ""; b.Text.Append(s); if (sink != null && s.Length > 0) sink.OnReasoning(s); }
                                else if (dt == "signature_delta") b.Signature = (b.Signature ?? "") + (Json.Str(d, "signature") ?? "");
                                else if (dt == "input_json_delta") { b.Json2.Append(Json.Str(d, "partial_json") ?? ""); if (sink != null) sink.OnToolProgress(b.Name ?? "", b.Json2.Length); }
                                break;
                            }
                        case "message_delta":
                            stop = Json.Str(o, "delta", "stop_reason") ?? stop;
                            refusalNote = Json.Str(o, "delta", "stop_details", "explanation") ?? refusalNote;
                            if (Json.Int(o, "usage", "output_tokens") > 0) res.OutputTokens = Json.Int(o, "usage", "output_tokens");
                            break;
                        case "message_stop":
                            res.Stop = "__end__";
                            break;
                        case "error":
                            res.Error = (Json.Str(o, "error", "type") ?? "error") + ": " + (Json.Str(o, "error", "message") ?? "The server reported an error.");
                            string et = Json.Str(o, "error", "type") ?? "";
                            if (et == "overloaded_error" || et == "api_error" || et == "rate_limit_error") res.Retryable = true;
                            break;
                    }
                });
            List<object> raw = new List<object>();
            StringBuilder text = new StringBuilder(), thinking = new StringBuilder();
            bool cut = stop == "max_tokens" || stop == "model_context_window_exceeded" || (stop == null && !r.Cancelled && r.Error == null);
            int lastTool = -1;
            foreach (KeyValuePair<int, Block> kv in blocks) if (kv.Value.Type == "tool_use") lastTool = kv.Key;
            if (stop == "refusal")
            {
                List<int> drop = new List<int>();
                foreach (KeyValuePair<int, Block> kv in blocks) if (kv.Value.Type != "text") drop.Add(kv.Key);
                foreach (int k in drop) blocks.Remove(k);
                lastTool = -1;
            }
            foreach (KeyValuePair<int, Block> kv in blocks)
            {
                Block b = kv.Value;
                switch (b.Type)
                {
                    case "text":
                        text.Append(b.Text);
                        raw.Add(Json.O("type", "text", "text", b.Text.ToString()));
                        break;
                    case "thinking":
                        thinking.Append(b.Text);
                        raw.Add(Json.O("type", "thinking", "thinking", b.Text.ToString(), "signature", b.Signature ?? ""));
                        break;
                    case "redacted_thinking":
                        raw.Add(Json.O("type", "redacted_thinking", "data", b.Data ?? ""));
                        break;
                    case "tool_use":
                        {
                            ToolCall c = new ToolCall();
                            c.Id = b.Id ?? ("toolu_" + Guid.NewGuid().ToString("N"));
                            c.Name = b.Name ?? "";
                            c.Arguments = b.Json2.Length > 0 ? b.Json2.ToString() : "{}";
                            if (cut && kv.Key == lastTool && Json.TryParse(c.Arguments) == null) c.Truncated = true;
                            r.ToolCalls.Add(c);
                            raw.Add(Json.O("type", "tool_use", "id", c.Id, "name", c.Name, "input", (object)c.Args ?? new Dictionary<string, object>()));
                            break;
                        }
                    default:
                        if (b.Raw != null) raw.Add(b.Raw);
                        break;
                }
            }
            r.Text = text.ToString();
            r.Reasoning = thinking.ToString();
            r.RawProvider = "anthropic";
            r.RawBlocks = raw;
            if (!r.Cancelled && r.Error == null && stop == null)
            {
                r.Error = "The reply stopped before it was finished: the connection to the model server ended early.";
                r.Interrupted = true;
                r.Retryable = true;
                return r;
            }
            if (r.Cancelled) r.Stop = "cancelled";
            else if (stop == "refusal")
            {
                r.Stop = "end";
                r.ToolCalls.Clear();
                r.RawBlocks = null;
                r.RawProvider = null;
                r.Text = "(The model stopped and declined to continue" + (string.IsNullOrEmpty(refusalNote) ? "." : ": " + refusalNote.Trim()) + ")";
            }
            else if (stop == "max_tokens" || stop == "model_context_window_exceeded") r.Stop = "length";
            else if (r.ToolCalls.Count > 0 && r.ToolCalls[r.ToolCalls.Count - 1].Truncated) r.Stop = "length";
            else if (r.ToolCalls.Count > 0) r.Stop = "tool_calls";
            else if (stop == "pause_turn") r.Stop = "pause";
            else if (r.Error == null) r.Stop = "end";
            return r;
        }
    }

    class OpenAiResponsesBackend : LlmBackend
    {
        public string ApiKey = "";
        public string BaseUrl = "https://api.openai.com/v1";
        public bool Reasoning = true;
        public int TimeoutMs = 20 * 60 * 1000;
        string Q(string name) { return "responses|" + name + "|" + BaseUrl + "|" + Model; }
        bool dropReasoningItems { get { return Quirks.Has(Q("dropreasoning")); } set { Quirks.Flag(Q("dropreasoning"), value); } }
        bool noSummary { get { return Quirks.Has(Q("nosummary")); } set { Quirks.Flag(Q("nosummary"), value); } }
        bool imagesAsUser { get { return Quirks.Has(Q("imagesasuser")); } set { Quirks.Flag(Q("imagesasuser"), value); } }
        List<string> acceptedEffort { get { return Quirks.Get<List<string>>(Q("effort")); } set { Quirks.Put(Q("effort"), value); } }

        public override LlmResult Stream(LlmRequest req, IStreamSink sink, CancelToken ct)
        {
            bool sendReasoning = Reasoning;
            LlmResult r = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                string body = BuildBody(req, sendReasoning, !noSummary);
                r = StreamOnce(body, sink, ct);
                if (r.Error == null || r.Cancelled || r.Text.Length > 0 || r.ToolCalls.Count > 0) break;
                if (r.Status != 400 && r.Status != 404) break;
                string low = r.Error.ToLowerInvariant();
                if (!dropReasoningItems && (low.Contains("rs_") || low.Contains("not found") || low.Contains("encrypted"))) { dropReasoningItems = true; continue; }
                if (!noSummary && low.Contains("summar")) { noSummary = true; continue; }
                if (!imagesAsUser && (low.Contains("function_call_output") || low.Contains("input_image") || (low.Contains("output") && low.Contains("array")))) { imagesAsUser = true; continue; }
                if (sendReasoning && low.Contains("effort") && acceptedEffort == null)
                {
                    List<string> acc = OpenAiChatBackend.AcceptedFrom(r.Error);
                    if (acc != null && acc.Count > 0) { acceptedEffort = acc; continue; }
                }
                if (sendReasoning && (low.Contains("reasoning") || low.Contains("effort"))) { sendReasoning = false; continue; }
                break;
            }
            return r;
        }

        string EffortFor(string want)
        {
            if (string.IsNullOrEmpty(want) || want == "auto" || want == "on") return null;
            string w = want == "off" ? "none" : want;
            if (acceptedEffort == null) return w;
            if (acceptedEffort.Contains(w)) return w;
            string[] order = w == "none" ? new string[] { "none", "minimal", "low" } : new string[] { w, "high", "medium", "xhigh", "max", "low", "minimal" };
            foreach (string v in order) if (acceptedEffort.Contains(v)) return v;
            return null;
        }

        string BuildBody(LlmRequest req, bool sendReasoning, bool sendSummary)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = Model;
            body["stream"] = true;
            body["store"] = false;
            body["instructions"] = req.System ?? "";
            body["max_output_tokens"] = Math.Max(1024, req.MaxTokens);
            if (sendReasoning)
            {
                body["include"] = Json.A("reasoning.encrypted_content");
                Dictionary<string, object> rz = new Dictionary<string, object>();
                string eff = EffortFor(req.Effort);
                if (eff != null) rz["effort"] = eff;
                if (sendSummary && req.ShowThinking) rz["summary"] = "auto";
                if (rz.Count > 0) body["reasoning"] = rz;
            }
            List<object> input = new List<object>();
            List<ImageData> pendingImages = new List<ImageData>();
            for (int i = 0; i < req.Messages.Count; i++)
            {
                Msg m = req.Messages[i];
                if (m.Role == "tool")
                {
                    string outText = string.IsNullOrEmpty(m.Text) ? "(no output)" : m.Text;
                    if (m.Images != null && m.Images.Count > 0 && !imagesAsUser)
                    {
                        List<object> parts = new List<object>();
                        parts.Add(Json.O("type", "input_text", "text", outText));
                        foreach (ImageData img in m.Images) parts.Add(Json.O("type", "input_image", "image_url", img.DataUrl));
                        input.Add(Json.O("type", "function_call_output", "call_id", m.ToolCallId ?? "", "output", parts));
                        continue;
                    }
                    input.Add(Json.O("type", "function_call_output", "call_id", m.ToolCallId ?? "", "output", outText));
                    if (m.Images != null) pendingImages.AddRange(m.Images);
                    bool nextIsTool = i + 1 < req.Messages.Count && req.Messages[i + 1].Role == "tool";
                    if (!nextIsTool && pendingImages.Count > 0) { input.Add(UserItem("(Images returned by the tools above.)", pendingImages)); pendingImages = new List<ImageData>(); }
                    continue;
                }
                if (m.Role == "assistant")
                {
                    if (m.RawProvider == "openai-responses" && m.RawBlocks != null)
                    {
                        foreach (object it in m.RawBlocks)
                        {
                            Dictionary<string, object> d = it as Dictionary<string, object>;
                            if (d == null) continue;
                            string t = Json.Str(d, "type");
                            if (t == "reasoning" && (dropReasoningItems || !sendReasoning)) continue;
                            Dictionary<string, object> c = new Dictionary<string, object>(d);
                            if (dropReasoningItems) c.Remove("id");
                            c.Remove("status");
                            input.Add(c);
                        }
                        continue;
                    }
                    if (!string.IsNullOrEmpty(m.Text))
                        input.Add(Json.O("role", "assistant", "content", Json.A(Json.O("type", "output_text", "text", m.Text))));
                    if (m.ToolCalls != null)
                        foreach (ToolCall c in m.ToolCalls)
                            input.Add(Json.O("type", "function_call", "call_id", c.Id, "name", c.Name, "arguments", string.IsNullOrEmpty(c.Arguments) ? "{}" : c.Arguments));
                    continue;
                }
                input.Add(UserItem(m.Text, m.Images));
            }
            body["input"] = input;
            if (req.Tools.Count > 0)
            {
                List<object> tools = new List<object>();
                foreach (ToolSpec t in req.Tools) tools.Add(Json.O("type", "function", "name", t.Name, "description", t.Description, "parameters", t.Schema, "strict", false));
                body["tools"] = tools;
                body["tool_choice"] = "auto";
            }
            return Json.Write(body);
        }

        static Dictionary<string, object> UserItem(string text, List<ImageData> images)
        {
            List<object> parts = new List<object>();
            parts.Add(Json.O("type", "input_text", "text", string.IsNullOrEmpty(text) ? "(empty message)" : text));
            if (images != null) foreach (ImageData img in images) parts.Add(Json.O("type", "input_image", "image_url", img.DataUrl));
            return Json.O("role", "user", "content", parts);
        }

        LlmResult StreamOnce(string body, IStreamSink sink, CancelToken ct)
        {
            Dictionary<string, string> h = new Dictionary<string, string>();
            h["Authorization"] = "Bearer " + ApiKey;
            StringBuilder text = new StringBuilder(), reasoning = new StringBuilder();
            List<object> finalOutput = null;
            string incomplete = null;
            bool ended = false;
            Dictionary<string, string> callNames = new Dictionary<string, string>();
            Dictionary<string, int> argLens = new Dictionary<string, int>();
            LlmResult r = PostStream(BaseUrl.TrimEnd('/') + "/responses", h, body, TimeoutMs, ct,
                delegate (string ev, string data, LlmResult res)
                {
                    object o = Json.TryParse(data);
                    if (o == null) return;
                    string type = Json.Str(o, "type") ?? ev ?? "";
                    switch (type)
                    {
                        case "response.output_text.delta":
                            { string d = Json.Str(o, "delta") ?? ""; text.Append(d); if (sink != null && d.Length > 0) sink.OnText(d); break; }
                        case "response.reasoning_summary_text.delta":
                        case "response.reasoning_text.delta":
                            { string d = Json.Str(o, "delta") ?? ""; reasoning.Append(d); if (sink != null && d.Length > 0) sink.OnReasoning(d); break; }
                        case "response.reasoning_summary_part.done":
                            if (reasoning.Length > 0) { reasoning.Append("\n\n"); if (sink != null) sink.OnReasoning("\n\n"); }
                            break;
                        case "response.output_item.added":
                            {
                                string it = Json.Str(o, "item", "type");
                                if (it == "function_call")
                                {
                                    string iid = Json.Str(o, "item", "id") ?? "";
                                    callNames[iid] = Json.Str(o, "item", "name") ?? "";
                                    if (sink != null) sink.OnToolProgress(callNames[iid], 0);
                                }
                                break;
                            }
                        case "response.function_call_arguments.delta":
                            {
                                string iid = Json.Str(o, "item_id") ?? "";
                                int n;
                                argLens.TryGetValue(iid, out n);
                                n += (Json.Str(o, "delta") ?? "").Length;
                                argLens[iid] = n;
                                string nm;
                                callNames.TryGetValue(iid, out nm);
                                if (sink != null) sink.OnToolProgress(nm ?? "", n);
                                break;
                            }
                        case "response.completed":
                            ended = true;
                            finalOutput = Json.List(o, "response", "output");
                            res.InputTokens = Json.Int(o, "response", "usage", "input_tokens");
                            res.OutputTokens = Json.Int(o, "response", "usage", "output_tokens");
                            res.Stop = "__end__";
                            break;
                        case "response.incomplete":
                            ended = true;
                            finalOutput = Json.List(o, "response", "output");
                            incomplete = Json.Str(o, "response", "incomplete_details", "reason") ?? "incomplete";
                            res.InputTokens = Json.Int(o, "response", "usage", "input_tokens");
                            res.OutputTokens = Json.Int(o, "response", "usage", "output_tokens");
                            res.Stop = "__end__";
                            break;
                        case "response.failed":
                            res.Error = Json.Str(o, "response", "error", "message") ?? "The request failed.";
                            break;
                        case "error":
                            res.Error = Json.Str(o, "message") ?? Json.Str(o, "error", "message") ?? "The server reported an error.";
                            break;
                    }
                });
            r.RawProvider = "openai-responses";
            if (finalOutput != null)
            {
                r.RawBlocks = finalOutput;
                StringBuilder t2 = new StringBuilder();
                foreach (object it in finalOutput)
                {
                    string ty = Json.Str(it, "type");
                    if (ty == "function_call")
                    {
                        ToolCall c = new ToolCall();
                        c.Id = Json.Str(it, "call_id") ?? Json.Str(it, "id") ?? "";
                        c.Name = Json.Str(it, "name") ?? "";
                        c.Arguments = Json.Str(it, "arguments") ?? "{}";
                        r.ToolCalls.Add(c);
                    }
                    else if (ty == "message")
                    {
                        List<object> parts = Json.List(it, "content");
                        if (parts != null)
                            foreach (object p in parts)
                            {
                                string pt = Json.Str(p, "type");
                                if (pt == "output_text") t2.Append(Json.Str(p, "text") ?? "");
                                else if (pt == "refusal") t2.Append("(The model declined: " + (Json.Str(p, "refusal") ?? "no reason given") + ")");
                            }
                    }
                }
                r.Text = t2.Length > 0 ? t2.ToString() : text.ToString();
            }
            else r.Text = text.ToString();
            r.Reasoning = reasoning.ToString().Trim();
            if (r.ToolCalls.Count > 0 && (incomplete != null || finalOutput == null) && !r.Cancelled)
            {
                ToolCall last = r.ToolCalls[r.ToolCalls.Count - 1];
                if (Json.TryParse(string.IsNullOrEmpty(last.Arguments) ? "{}" : last.Arguments) == null) { last.Truncated = true; incomplete = "max_output_tokens"; }
            }
            if (!r.Cancelled && r.Error == null && !ended)
            {
                r.Error = "The reply stopped before it was finished: the connection to the model server ended early.";
                r.Interrupted = true;
                r.Retryable = true;
                return r;
            }
            if (r.Cancelled) r.Stop = "cancelled";
            else if (incomplete == "max_output_tokens") r.Stop = "length";
            else if (r.ToolCalls.Count > 0) r.Stop = "tool_calls";
            else if (r.Error == null) r.Stop = "end";
            return r;
        }
    }
}
