using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MinGent
{
    class ModelEntry
    {
        public string Provider = "", Id = "", Display = "", Detail = "";
        public int Context, MaxOutput;
        public bool Loaded, Vision, ToolUse = true;
        public List<string> ReasoningOptions;
        public bool BudgetThinking;
        public string Path;
        public bool AnthropicAdaptive = true, AnthropicDisable, AnthropicBetweenTools, AnthropicEnabled, AnthropicBinding, AnthropicInterleaved;
        public List<string> EffortLevels;

        public string Ref { get { return Provider + ":" + Id; } }
        public bool Cloud { get { return IsCloud(Provider) || (Provider == "custom" && !Http.IsLoopback(ModelCatalog.CustomUrl)); } }

        public static bool IsCloud(string p) { return p == "anthropic" || p == "openai" || p == "gemini" || p == "openrouter"; }

        public static string ProviderTitle(string p)
        {
            switch (p)
            {
                case "lmstudio": return "LM Studio";
                case "engine": return "Built-in engine";
                case "anthropic": return "Claude (Anthropic)";
                case "openai": return "OpenAI";
                case "gemini": return "Gemini (Google)";
                case "openrouter": return "OpenRouter";
                case "custom": return "Custom server";
            }
            return p;
        }
    }

    class KeyCheck
    {
        public bool Ok, Rejected;
        public int Models;
        public string Message = "";
    }

    class ProviderState
    {
        public string Provider = "";
        public bool Configured, Reachable, NeedsKey;
        public string Status = "", Error;
        public string BaseUrl = "";
    }

    class CatalogSnapshot
    {
        public readonly List<ModelEntry> Models = new List<ModelEntry>();
        public readonly Dictionary<string, ProviderState> States = new Dictionary<string, ProviderState>();
        public DateTime When = DateTime.UtcNow;

        public ProviderState State(string p)
        {
            ProviderState s;
            if (!States.TryGetValue(p, out s)) { s = new ProviderState(); s.Provider = p; States[p] = s; }
            return s;
        }

        public ModelEntry Find(string modelRef)
        {
            if (string.IsNullOrEmpty(modelRef)) return null;
            int c = modelRef.IndexOf(':');
            string p = c < 0 ? modelRef : modelRef.Substring(0, c), id = c < 0 ? "" : modelRef.Substring(c + 1);
            if (p == "lmstudio" && id.Length == 0)
            {
                foreach (ModelEntry m in Models) if (m.Provider == "lmstudio" && m.Loaded) return m;
                return null;
            }
            foreach (ModelEntry m in Models) if (m.Provider == p && string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) return m;
            if (p == "lmstudio")
                foreach (ModelEntry m in Models) if (m.Provider == p && m.Detail != null && string.Equals(m.Path, id, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }
    }

    class ProviderKeys
    {
        public string Anthropic = "", OpenAi = "", Gemini = "", OpenRouter = "", Custom = "", LmStudioToken = "";
        public string CustomUrl = "", CustomModel = "";
        public int LmLoadContext;
        public int EngineContext;
    }

    static class ModelCatalog
    {
        public const int LmStudioDefaultPort = 1234;
        public static string CustomUrl = "";
        static readonly object cacheGate = new object();
        static List<ModelEntry> anthropicCache, openaiCache, geminiCache, openrouterCache;
        static string anthropicCacheKey, openaiCacheKey, geminiCacheKey, openrouterCacheKey;
        static DateTime anthropicWhen, openaiWhen, geminiWhen, openrouterWhen;
        public static string GeminiBase = "https://generativelanguage.googleapis.com";
        public static string OpenRouterBase = "https://openrouter.ai/api/v1";

        public static CatalogSnapshot Refresh(ProviderKeys k, bool includeCloud, CancelToken ct)
        {
            CatalogSnapshot snap = new CatalogSnapshot();
            CustomUrl = k.CustomUrl ?? "";
            List<Thread> threads = new List<Thread>();
            List<ModelEntry> lm = new List<ModelEntry>(), eng = new List<ModelEntry>(), an = new List<ModelEntry>(), oa = new List<ModelEntry>(), ge = new List<ModelEntry>(), ro = new List<ModelEntry>(), cu = new List<ModelEntry>();
            ProviderState sLm = snap.State("lmstudio"), sEng = snap.State("engine"), sAn = snap.State("anthropic"), sOa = snap.State("openai"), sGe = snap.State("gemini"), sOr = snap.State("openrouter"), sCu = snap.State("custom");
            threads.Add(Start(delegate { ProbeLmStudio(k, lm, sLm, ct); }));
            threads.Add(Start(delegate { ProbeEngine(k, eng, sEng); }));
            string wAn = WrongKey("anthropic", k.Anthropic), wOa = WrongKey("openai", k.OpenAi), wGe = WrongKey("gemini", k.Gemini), wOr = WrongKey("openrouter", k.OpenRouter);
            if (wAn != null) sAn.Status = wAn;
            else if (!string.IsNullOrEmpty(k.Anthropic) && includeCloud) threads.Add(Start(delegate { ProbeAnthropic(k.Anthropic, an, sAn, ct); }));
            else { sAn.Status = "Add an API key in Settings to use Claude models"; }
            if (wOa != null) sOa.Status = wOa;
            else if (!string.IsNullOrEmpty(k.OpenAi) && includeCloud) threads.Add(Start(delegate { ProbeOpenAi(k.OpenAi, oa, sOa, ct); }));
            else { sOa.Status = "Add an API key in Settings to use OpenAI models"; }
            if (wGe != null) sGe.Status = wGe;
            else if (!string.IsNullOrEmpty(k.Gemini) && includeCloud) threads.Add(Start(delegate { ProbeGemini(k.Gemini, ge, sGe, ct); }));
            else { sGe.Status = "Add an API key in Settings to use Gemini models"; }
            if (wOr != null) sOr.Status = wOr;
            else if (!string.IsNullOrEmpty(k.OpenRouter) && includeCloud) threads.Add(Start(delegate { ProbeOpenRouter(k.OpenRouter, ro, sOr, ct); }));
            else { sOr.Status = "Add an API key in Settings to use OpenRouter models"; }
            if (!string.IsNullOrEmpty(k.CustomUrl)) threads.Add(Start(delegate { ProbeCustom(k, cu, sCu, ct); }));
            else { sCu.Status = "No custom server set"; }
            foreach (Thread t in threads) t.Join(15000);
            lock (snap)
            {
                snap.Models.AddRange(lm);
                snap.Models.AddRange(eng);
                snap.Models.AddRange(an);
                snap.Models.AddRange(oa);
                snap.Models.AddRange(ge);
                snap.Models.AddRange(ro);
                snap.Models.AddRange(cu);
            }
            return snap;
        }

        public static string KeyOwner(string key)
        {
            string k = (key ?? "").Trim();
            if (k.StartsWith("sk-ant-", StringComparison.Ordinal)) return "anthropic";
            if (k.StartsWith("sk-or-", StringComparison.Ordinal)) return "openrouter";
            if (k.StartsWith("AIza", StringComparison.Ordinal)) return "gemini";
            if (k.StartsWith("sk-", StringComparison.Ordinal)) return "openai";
            return null;
        }

        public static string WrongKey(string provider, string key)
        {
            string owner = KeyOwner(key);
            if (owner == null || owner == provider || !ModelEntry.IsCloud(provider)) return null;
            return "This looks like " + (owner == "openai" || owner == "openrouter" ? "an " : "a ") + ModelEntry.ProviderTitle(owner) + " key, so it was not sent. Paste it in the " + ModelEntry.ProviderTitle(owner) + " row.";
        }

        public static KeyCheck CheckKey(string provider, string key, CancelToken ct)
        {
            KeyCheck c = new KeyCheck();
            string wrong = WrongKey(provider, key);
            if (wrong != null) { c.Rejected = true; c.Message = wrong; return c; }
            List<ModelEntry> list = new List<ModelEntry>();
            ProviderState st = new ProviderState();
            st.Provider = provider;
            key = (key ?? "").Trim();
            try
            {
                switch (provider)
                {
                    case "anthropic": ProbeAnthropic(key, list, st, ct); break;
                    case "openai": ProbeOpenAi(key, list, st, ct); break;
                    case "gemini": ProbeGemini(key, list, st, ct); break;
                    case "openrouter": ProbeOpenRouter(key, list, st, ct); break;
                    default: c.Message = "Unknown provider."; return c;
                }
            }
            catch (Exception ex) { st.Error = ex.Message; }
            if (ct != null && ct.IsCancelled) return c;
            c.Rejected = st.NeedsKey;
            c.Ok = !st.NeedsKey && st.Reachable && (st.Status ?? "").StartsWith("Connected", StringComparison.Ordinal);
            c.Models = c.Ok ? list.Count : 0;
            string why = Brief(st.Error);
            if (c.Ok) c.Message = list.Count == 0 ? "Works, but no chat models are available to this key." : list.Count == 1 ? "Works. 1 model." : "Works. " + list.Count + " models.";
            else if (c.Rejected) c.Message = "The key was rejected" + (why.Length > 0 ? ": " + why : ".");
            else c.Message = "Couldn't check the key" + (why.Length > 0 ? ": " + why : ".");
            return c;
        }

        static string Brief(string error)
        {
            string e = (error ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (e.Length > 240) e = e.Substring(0, 240).TrimEnd() + "...";
            return e.TrimEnd('.');
        }

        static Thread Start(ThreadStart f)
        {
            Thread t = new Thread(delegate () { try { f(); } catch { } });
            t.IsBackground = true;
            t.Start();
            return t;
        }

        public static List<int> LmStudioPorts()
        {
            List<int> ports = new List<int>();
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string cfg = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(home, ".lmstudio"), ".internal"), "http-server-config.json");
                if (File.Exists(cfg) && new FileInfo(cfg).Length < 65536)
                {
                    int p = Json.Int(Json.TryParse(File.ReadAllText(cfg)), "port");
                    if (p > 0 && p < 65536) ports.Add(p);
                }
            }
            catch { }
            if (!ports.Contains(LmStudioDefaultPort)) ports.Add(LmStudioDefaultPort);
            return ports;
        }

        public static bool LmStudioInstalled()
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home)) return false;
                return Directory.Exists(System.IO.Path.Combine(home, ".lmstudio")) || Directory.Exists(System.IO.Path.Combine(System.IO.Path.Combine(home, ".cache"), "lm-studio"));
            }
            catch { return false; }
        }

        static Dictionary<string, string> LmHeaders(ProviderKeys k)
        {
            Dictionary<string, string> h = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(k.LmStudioToken)) h["Authorization"] = "Bearer " + k.LmStudioToken;
            return h;
        }

        static void ProbeLmStudio(ProviderKeys k, List<ModelEntry> list, ProviderState st, CancelToken ct)
        {
            st.Configured = true;
            foreach (int port in LmStudioPorts())
            {
                string root = "http://127.0.0.1:" + port;
                HttpResult r = Http.Get(root + "/api/v1/models", LmHeaders(k), 4000, ct);
                if (r.ConnectFailed || (r.Status == 0 && r.Error != null)) continue;
                st.Reachable = true;
                st.BaseUrl = root + "/v1";
                if (r.Status == 401 || r.Status == 403)
                {
                    st.NeedsKey = true;
                    st.Status = "LM Studio wants an API token - add it in Settings";
                    return;
                }
                List<object> arr = Json.List(r.Json, "models");
                if (arr != null)
                {
                    foreach (object o in arr)
                    {
                        string type = Json.Str(o, "type") ?? "llm";
                        if (type != "llm" && type != "vlm") continue;
                        string key = Json.Str(o, "key") ?? "";
                        if (key.Length == 0) continue;
                        List<object> inst = Json.List(o, "loaded_instances");
                        List<string> think = null;
                        List<object> opts = Json.List(o, "capabilities", "reasoning", "allowed_options");
                        if (opts != null)
                        {
                            think = new List<string>();
                            foreach (object v in opts) { string sv = v as string; if (!string.IsNullOrEmpty(sv) && !think.Contains(sv)) think.Add(sv); }
                            if (think.Count < 2) think = null;
                        }
                        bool vision = Json.Bool(o, "capabilities", "vision") || type == "vlm";
                        object tu = Json.Get(o, "capabilities", "trained_for_tool_use");
                        string disp = Json.Str(o, "display_name") ?? key;
                        string detail = Join(" \u00B7 ", Json.Str(o, "params_string"), Json.Str(o, "quantization", "name"), Size(Json.Long(o, "size_bytes")));
                        int maxCtx = Json.Int(o, "max_context_length");
                        if (inst != null && inst.Count > 0)
                        {
                            foreach (object i in inst)
                            {
                                ModelEntry m = new ModelEntry();
                                m.Provider = "lmstudio";
                                m.Id = Json.Str(i, "id") ?? key;
                                m.Path = key;
                                m.Display = disp + (inst.Count > 1 ? " (" + m.Id + ")" : "");
                                m.Detail = detail;
                                m.Context = Json.Int(i, "config", "context_length");
                                if (m.Context <= 0) m.Context = maxCtx;
                                m.Loaded = true;
                                m.Vision = vision;
                                m.ToolUse = !(tu is bool) || (bool)tu;
                                m.ReasoningOptions = think;
                                list.Add(m);
                            }
                        }
                        else
                        {
                            ModelEntry m = new ModelEntry();
                            m.Provider = "lmstudio";
                            m.Id = key;
                            m.Path = key;
                            m.Display = disp;
                            m.Detail = detail;
                            m.Context = k.LmLoadContext > 0 ? Math.Min(k.LmLoadContext, maxCtx > 0 ? maxCtx : int.MaxValue) : 0;
                            m.Vision = vision;
                            m.ToolUse = !(tu is bool) || (bool)tu;
                            m.ReasoningOptions = think;
                            list.Add(m);
                        }
                    }
                    int loaded = 0;
                    foreach (ModelEntry m in list) if (m.Loaded) loaded++;
                    st.Status = loaded > 0 ? "Running on port " + port : "Running, no model loaded";
                    return;
                }
                HttpResult v1 = Http.Get(root + "/v1/models", LmHeaders(k), 4000, ct);
                List<object> data = Json.List(v1.Json, "data");
                if (data != null)
                {
                    foreach (object o in data)
                    {
                        string id = Json.Str(o, "id");
                        if (string.IsNullOrEmpty(id) || id.IndexOf("embed", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        ModelEntry m = new ModelEntry();
                        m.Provider = "lmstudio";
                        m.Id = id;
                        m.Path = id;
                        m.Display = id;
                        m.Loaded = true;
                        list.Add(m);
                    }
                    st.Status = "Running on port " + port;
                    return;
                }
                st.Status = "Something answers on port " + port + ", but not like LM Studio";
                return;
            }
            st.Reachable = false;
            st.Status = "Not running - start LM Studio's server (Developer tab)";
        }

        static void ProbeEngine(ProviderKeys k, List<ModelEntry> list, ProviderState st)
        {
            Engine e = Engine.Instance;
            st.Configured = e.Installed;
            st.Reachable = e.Installed;
            st.Status = e.StatusText;
            foreach (LocalModel lm in ModelLibrary.Scan(false))
            {
                if (lm.IsProjector || lm.Helper || lm.PartIndex > 1 || lm.Incomplete) continue;
                ModelEntry m = new ModelEntry();
                m.Provider = "engine";
                m.Id = lm.Path;
                m.Path = lm.Path;
                m.Display = lm.DisplayName;
                m.Detail = Join(" \u00B7 ", lm.ParamsText, lm.Quant, Size(lm.TotalBytes), lm.SourceLabel);
                m.Context = e.Running && string.Equals(e.ModelPath, lm.Path, StringComparison.OrdinalIgnoreCase) && e.Context > 0 ? e.Context
                          : Math.Min(k.EngineContext > 0 ? k.EngineContext : 32768, lm.TrainContext > 0 ? lm.TrainContext : int.MaxValue);
                m.Loaded = e.Running && string.Equals(e.ModelPath, lm.Path, StringComparison.OrdinalIgnoreCase);
                m.Vision = lm.ProjectorPath != null;
                m.ReasoningOptions = EngineThinkingLevels(lm);
                m.BudgetThinking = lm.Thinking && !lm.NativeEffort;
                list.Add(m);
            }
        }

        public static List<string> EngineThinkingLevels(LocalModel lm)
        {
            if (lm.NativeEffort)
            {
                List<string> own = new List<string>();
                if (lm.EffortNames == null) own.AddRange(new string[] { "low", "medium", "high" });
                else foreach (string n in lm.EffortNames) if (n != "none") own.Add(n);
                if (lm.CanStopThinking || (lm.EffortNames != null && lm.EffortNames.Contains("none"))) own.Insert(0, "off");
                return own;
            }
            if (!lm.Thinking) return null;
            if (lm.CanStopThinking) return new List<string>(new string[] { "off", "low", "medium", "high", "max" });
            return new List<string>(new string[] { "low", "medium", "high", "max" });
        }

        static readonly string[][] AnthropicFallback = {
            new string[] { "claude-opus-5-5", "Claude Opus 5.5" },
            new string[] { "claude-sonnet-5-5", "Claude Sonnet 5.5" },
            new string[] { "claude-haiku-5-5", "Claude Haiku 5.5" },
            new string[] { "claude-fable-5-1", "Claude Fable 5.1" } };

        static void ProbeAnthropic(string key, List<ModelEntry> list, ProviderState st, CancelToken ct)
        {
            st.Configured = true;
            lock (cacheGate)
            {
                if (anthropicCache != null && anthropicCacheKey == key && (DateTime.UtcNow - anthropicWhen).TotalMinutes < 30)
                {
                    list.AddRange(anthropicCache);
                    st.Reachable = true;
                    st.Status = "Connected";
                    return;
                }
            }
            Dictionary<string, string> h = new Dictionary<string, string>();
            h["x-api-key"] = key;
            h["anthropic-version"] = "2023-06-01";
            HttpResult r = Http.Get("https://api.anthropic.com/v1/models?limit=100", h, 12000, ct);
            List<object> data = Json.List(r.Json, "data");
            if (r.Ok && data != null)
            {
                foreach (object o in data)
                {
                    string id = Json.Str(o, "id");
                    if (string.IsNullOrEmpty(id)) continue;
                    ModelEntry m = new ModelEntry();
                    m.Provider = "anthropic";
                    m.Id = id;
                    m.Display = Json.Str(o, "display_name") ?? id;
                    m.Context = Json.Int(o, "max_input_tokens");
                    if (m.Context <= 0) m.Context = 200000;
                    m.MaxOutput = Json.Int(o, "max_tokens");
                    m.Vision = true;
                    object caps = Json.Get(o, "capabilities");
                    m.ReasoningOptions = new List<string>(new string[] { "low", "medium", "high", "xhigh", "max" });
                    if (caps != null)
                    {
                        object ad = Json.Get(caps, "thinking", "types", "adaptive", "supported");
                        m.AnthropicAdaptive = !(ad is bool) || (bool)ad;
                        m.AnthropicDisable = Json.Bool(caps, "thinking", "types", "disabled", "supported");
                        m.AnthropicBetweenTools = Json.Bool(caps, "thinking", "types", "between_tools", "supported");
                        m.AnthropicEnabled = Json.Bool(caps, "thinking", "types", "enabled", "supported");
                        object ec = Json.Get(caps, "effort");
                        if (ec != null)
                        {
                            m.EffortLevels = new List<string>();
                            if (Json.Bool(ec, "supported"))
                                foreach (string lv in new string[] { "low", "medium", "high", "xhigh", "max" })
                                    if (Json.Bool(ec, lv, "supported")) m.EffortLevels.Add(lv);
                            List<object> legacy = Json.List(ec, "levels") ?? Json.List(ec, "supported_levels");
                            if (legacy != null) foreach (object e in legacy) if (e is string && !m.EffortLevels.Contains((string)e)) m.EffortLevels.Add((string)e);
                            if (m.EffortLevels.Count > 0) m.ReasoningOptions = new List<string>(m.EffortLevels);
                            else if (!m.AnthropicAdaptive && !m.AnthropicEnabled) m.ReasoningOptions = null;
                        }
                    }
                    m.AnthropicBinding = AnthropicBinds(id);
                    m.AnthropicInterleaved = Regex.IsMatch(id, @"^claude-(opus|sonnet|haiku)-4", RegexOptions.IgnoreCase);
                    m.Detail = (m.Context >= 1000000 ? (m.Context / 1000000) + "M" : (m.Context / 1000) + "K") + " context";
                    list.Add(m);
                }
                st.Reachable = true;
                st.Status = "Connected";
                lock (cacheGate) { anthropicCache = new List<ModelEntry>(list); anthropicCacheKey = key; anthropicWhen = DateTime.UtcNow; }
                return;
            }
            st.Reachable = !r.ConnectFailed;
            st.Error = r.Error;
            if (r.Status == 401 || r.Status == 403) { st.NeedsKey = true; st.Status = "The API key was rejected"; return; }
            st.Status = r.ConnectFailed ? "Offline - showing the usual models" : "Couldn't list models (" + r.Error + ")";
            foreach (string[] f in AnthropicFallback)
            {
                ModelEntry m = new ModelEntry();
                m.Provider = "anthropic";
                m.Id = f[0];
                m.Display = f[1];
                m.AnthropicBinding = AnthropicBinds(f[0]);
                m.Context = 1000000;
                m.MaxOutput = 128000;
                m.Vision = true;
                m.Detail = "1M context";
                m.ReasoningOptions = new List<string>(new string[] { "low", "medium", "high", "xhigh", "max" });
                list.Add(m);
            }
        }

        static bool AnthropicBinds(string id)
        {
            Match v = Regex.Match(id ?? "", @"^claude-(opus|sonnet|haiku|fable)-(\d+)(?:-(\d{1,2}))?(?:-|$)", RegexOptions.IgnoreCase);
            if (!v.Success) return false;
            int major = int.Parse(v.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            int minor = v.Groups[3].Success ? int.Parse(v.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            return major > 5 || (major == 5 && minor >= 1);
        }

        static void ProbeOpenAi(string key, List<ModelEntry> list, ProviderState st, CancelToken ct)
        {
            st.Configured = true;
            lock (cacheGate)
            {
                if (openaiCache != null && openaiCacheKey == key && (DateTime.UtcNow - openaiWhen).TotalMinutes < 30)
                {
                    list.AddRange(openaiCache);
                    st.Reachable = true;
                    st.Status = "Connected";
                    return;
                }
            }
            Dictionary<string, string> h = new Dictionary<string, string>();
            h["Authorization"] = "Bearer " + key;
            HttpResult r = Http.Get("https://api.openai.com/v1/models", h, 12000, ct);
            List<object> data = Json.List(r.Json, "data");
            if (!r.Ok || data == null)
            {
                st.Reachable = !r.ConnectFailed;
                st.Error = r.Error;
                st.NeedsKey = r.Status == 401;
                st.Status = r.Status == 401 ? "The API key was rejected" : "Couldn't list models (" + r.Error + ")";
                return;
            }
            List<KeyValuePair<long, ModelEntry>> found = new List<KeyValuePair<long, ModelEntry>>();
            foreach (object o in data)
            {
                string id = Json.Str(o, "id");
                if (string.IsNullOrEmpty(id) || !IsOpenAiChatModel(id)) continue;
                ModelEntry m = new ModelEntry();
                m.Provider = "openai";
                m.Id = id;
                m.Display = id;
                m.Context = OpenAiContext(id);
                m.Vision = true;
                m.Detail = (m.Context >= 1000000 ? (m.Context / 1000000) + "M" : (m.Context / 1000) + "K") + " context";
                if (OpenAiReasoning(id)) m.ReasoningOptions = new List<string>(new string[] { "low", "medium", "high", "xhigh" });
                found.Add(new KeyValuePair<long, ModelEntry>(Json.Long(o, "created"), m));
            }
            found.Sort(delegate (KeyValuePair<long, ModelEntry> a, KeyValuePair<long, ModelEntry> b) { return b.Key.CompareTo(a.Key); });
            foreach (KeyValuePair<long, ModelEntry> kv in found) list.Add(kv.Value);
            st.Reachable = true;
            st.Status = "Connected";
            lock (cacheGate) { openaiCache = new List<ModelEntry>(list); openaiCacheKey = key; openaiWhen = DateTime.UtcNow; }
        }

        static void ProbeGemini(string key, List<ModelEntry> list, ProviderState st, CancelToken ct)
        {
            st.Configured = true;
            lock (cacheGate)
            {
                if (geminiCache != null && geminiCacheKey == key && (DateTime.UtcNow - geminiWhen).TotalMinutes < 30)
                {
                    list.AddRange(geminiCache);
                    st.Reachable = true;
                    st.Status = "Connected";
                    return;
                }
            }
            Dictionary<string, string> h = new Dictionary<string, string>();
            h["x-goog-api-key"] = key;
            HttpResult r = Http.Get(GeminiBase.TrimEnd('/') + "/v1beta/models?pageSize=1000", h, 12000, ct);
            List<object> data = Json.List(r.Json, "models");
            if (!r.Ok || data == null)
            {
                string low = (r.Error ?? "").ToLowerInvariant();
                bool badKey = r.Status == 401 || r.Status == 403 || low.Contains("api key not valid") || low.Contains("api_key_invalid");
                st.Reachable = !r.ConnectFailed;
                st.Error = r.Error;
                st.NeedsKey = badKey;
                st.Status = badKey ? "The API key was rejected" : "Couldn't list models (" + r.Error + ")";
                return;
            }
            foreach (object o in data)
            {
                string name = Json.Str(o, "name") ?? "";
                string id = name.StartsWith("models/", StringComparison.Ordinal) ? name.Substring(7) : name;
                if (!IsGeminiChatModel(id)) continue;
                List<object> methods = Json.List(o, "supportedGenerationMethods");
                bool chat = false;
                if (methods != null) foreach (object mm in methods) if (mm as string == "generateContent") chat = true;
                if (!chat) continue;
                ModelEntry m = new ModelEntry();
                m.Provider = "gemini";
                m.Id = id;
                string shown = Json.Str(o, "displayName");
                m.Display = string.IsNullOrEmpty(shown) ? id : shown;
                m.Context = Json.Int(o, "inputTokenLimit");
                if (m.Context <= 0) m.Context = 1048576;
                m.MaxOutput = Json.Int(o, "outputTokenLimit");
                m.Vision = true;
                m.Detail = (m.Context >= 1000000 ? (m.Context / 1000000) + "M" : (m.Context / 1000) + "K") + " context";
                if (Json.Bool(o, "thinking") || id.StartsWith("gemini-2.5", StringComparison.Ordinal) || !id.StartsWith("gemini-2", StringComparison.Ordinal))
                {
                    m.ReasoningOptions = new List<string>();
                    if (GeminiCanStopThinking(id)) m.ReasoningOptions.Add("off");
                    m.ReasoningOptions.AddRange(new string[] { "low", "medium", "high" });
                }
                list.Add(m);
            }
            list.Sort(delegate (ModelEntry a, ModelEntry b) { return GeminiRank(b.Id).CompareTo(GeminiRank(a.Id)); });
            st.Reachable = true;
            st.Status = list.Count > 0 ? "Connected" : "Connected, but no chat models are available for this key";
            lock (cacheGate) { geminiCache = new List<ModelEntry>(list); geminiCacheKey = key; geminiWhen = DateTime.UtcNow; }
        }

        static void ProbeOpenRouter(string key, List<ModelEntry> list, ProviderState st, CancelToken ct)
        {
            st.Configured = true;
            lock (cacheGate)
            {
                if (openrouterCache != null && openrouterCacheKey == key && (DateTime.UtcNow - openrouterWhen).TotalMinutes < 30)
                {
                    list.AddRange(openrouterCache);
                    st.Reachable = true;
                    st.Status = "Connected";
                    return;
                }
            }
            Dictionary<string, string> h = new Dictionary<string, string>();
            h["Authorization"] = "Bearer " + key;
            HttpResult who = Http.Get(OpenRouterBase + "/key", h, 12000, ct);
            if (!who.Ok)
            {
                st.Reachable = !who.ConnectFailed;
                st.Error = who.Error;
                st.NeedsKey = who.Status == 401 || who.Status == 403;
                st.Status = st.NeedsKey ? "The API key was rejected" : "Couldn't list models (" + who.Error + ")";
                return;
            }
            HttpResult r = Http.Get(OpenRouterBase + "/models?supported_parameters=tools&sort=most-popular", h, 15000, ct);
            if (r.Status == 400 || r.Status == 422) r = Http.Get(OpenRouterBase + "/models?supported_parameters=tools", h, 15000, ct);
            List<object> data = Json.List(r.Json, "data");
            if (!r.Ok || data == null)
            {
                st.Reachable = !r.ConnectFailed;
                st.Error = r.Error;
                st.Status = "Couldn't list models (" + r.Error + ")";
                return;
            }
            list.AddRange(OpenRouterModels(data));
            st.Reachable = true;
            st.Status = list.Count > 0 ? "Connected" : "Connected, but it lists no models that can use tools";
            lock (cacheGate) { openrouterCache = new List<ModelEntry>(list); openrouterCacheKey = key; openrouterWhen = DateTime.UtcNow; }
        }

        public static List<ModelEntry> OpenRouterModels(List<object> data)
        {
            List<ModelEntry> list = new List<ModelEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object o in data)
            {
                string id = Json.Str(o, "id");
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                List<string> pars = Strings(Json.List(o, "supported_parameters"));
                if (!pars.Contains("tools")) continue;
                List<string> outs = Strings(Json.List(o, "architecture", "output_modalities"));
                if (outs.Count > 0 && !outs.Contains("text")) continue;
                ModelEntry m = new ModelEntry();
                m.Provider = "openrouter";
                m.Id = id;
                string name = Json.Str(o, "name");
                m.Display = string.IsNullOrEmpty(name) ? id : name;
                m.Context = Json.Int(o, "context_length");
                if (m.Context <= 0) m.Context = Json.Int(o, "top_provider", "context_length");
                if (m.Context <= 0) m.Context = 128000;
                m.MaxOutput = Json.Int(o, "top_provider", "max_completion_tokens");
                if (m.MaxOutput <= 0) m.MaxOutput = Math.Max(4096, Math.Min(32000, m.Context / 4));
                m.Vision = Strings(Json.List(o, "architecture", "input_modalities")).Contains("image");
                if (pars.Contains("reasoning") || pars.Contains("include_reasoning"))
                {
                    object rs = Json.Get(o, "reasoning");
                    List<string> efforts = Strings(Json.List(o, "reasoning", "supported_efforts"));
                    List<string> opts = new List<string>();
                    if ((rs != null && !Json.Bool(rs, "mandatory")) || efforts.Contains("none")) opts.Add("off");
                    foreach (string lv in new string[] { "minimal", "low", "medium", "high", "xhigh", "max" })
                        if (efforts.Count > 0 ? efforts.Contains(lv) : lv == "low" || lv == "medium" || lv == "high") opts.Add(lv);
                    if (opts.Count > 0 && !(opts.Count == 1 && opts[0] == "off")) m.ReasoningOptions = opts;
                }
                m.Detail = ContextText(m.Context) + " context" + OpenRouterPrice(o);
                list.Add(m);
            }
            return list;
        }

        static List<string> Strings(List<object> l)
        {
            List<string> r = new List<string>();
            if (l != null) foreach (object o in l) { string s = o as string; if (!string.IsNullOrEmpty(s)) r.Add(s.ToLowerInvariant()); }
            return r;
        }

        static string ContextText(int ctx) { return ctx >= 1000000 ? (ctx / 1000000) + "M" : Math.Max(1, ctx / 1000) + "K"; }

        static string OpenRouterPrice(object o)
        {
            double pin, pout;
            if (!PerToken(Json.Get(o, "pricing", "prompt"), out pin) || !PerToken(Json.Get(o, "pricing", "completion"), out pout) || pin < 0 || pout < 0) return "";
            if (pin == 0 && pout == 0) return " · free";
            return " · $" + Dollars(pin * 1e6) + " / $" + Dollars(pout * 1e6) + " per M tokens";
        }

        static bool PerToken(object v, out double d)
        {
            d = 0;
            if (v is double) { d = (double)v; return true; }
            string s = v as string;
            return s != null && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
        }

        static string Dollars(double v)
        {
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            if (v >= 10) return Math.Round(v).ToString("0", inv);
            if (v >= 1) return v.ToString("0.##", inv);
            return v.ToString("0.###", inv);
        }

        public static bool IsGeminiChatModel(string id)
        {
            string s = (id ?? "").ToLowerInvariant();
            if (!s.StartsWith("gemini-")) return false;
            foreach (string bad in new string[] { "embedding", "image", "tts", "audio", "live", "robotics", "veo", "imagen", "aqa", "computer-use", "omni", "lyria", "learnlm", "vision" })
                if (s.Contains(bad)) return false;
            return true;
        }

        public static bool GeminiCanStopThinking(string id)
        {
            string s = (id ?? "").ToLowerInvariant();
            return s.StartsWith("gemini-2.5-flash") || s.StartsWith("gemini-2.0");
        }

        static double GeminiRank(string id)
        {
            Match v = Regex.Match(id ?? "", @"gemini-(\d+(?:\.\d+)?)");
            double ver = 0;
            if (v.Success) double.TryParse(v.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ver);
            string s = (id ?? "").ToLowerInvariant();
            double tier = s.Contains("-pro") ? 0.3 : s.Contains("flash-lite") ? 0.1 : s.Contains("flash") ? 0.2 : 0.15;
            double stable = s.Contains("preview") || s.Contains("exp") ? 0 : 0.05;
            return ver * 10 + tier + stable;
        }

        static bool IsOpenAiChatModel(string id)
        {
            string s = id.ToLowerInvariant();
            if (!(s.StartsWith("gpt-") || s.StartsWith("o1") || s.StartsWith("o3") || s.StartsWith("o4") || s.StartsWith("o5") || s.StartsWith("chatgpt") || s.StartsWith("codex"))) return false;
            foreach (string bad in new string[] { "embed", "tts", "whisper", "dall-e", "image", "realtime", "audio", "transcribe", "moderation", "search", "instruct", "-0301", "-0314", "vision-preview", "computer-use", "babbage", "davinci", "sora" })
                if (s.Contains(bad)) return false;
            return true;
        }

        static bool OpenAiReasoning(string id)
        {
            string s = id.ToLowerInvariant();
            return s.StartsWith("o1") || s.StartsWith("o3") || s.StartsWith("o4") || s.StartsWith("o5") || s.StartsWith("gpt-5") || s.StartsWith("gpt-6") || s.StartsWith("codex");
        }

        static int OpenAiContext(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.StartsWith("gpt-4.1")) return 1047576;
            if (s.StartsWith("gpt-5") || s.StartsWith("gpt-6") || s.StartsWith("codex")) return 400000;
            if (s.StartsWith("o1") || s.StartsWith("o3") || s.StartsWith("o4") || s.StartsWith("o5")) return 200000;
            return 128000;
        }

        static void ProbeCustom(ProviderKeys k, List<ModelEntry> list, ProviderState st, CancelToken ct)
        {
            st.Configured = true;
            string b = (k.CustomUrl ?? "").Trim().TrimEnd('/');
            st.BaseUrl = b;
            Dictionary<string, string> h = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(k.Custom)) h["Authorization"] = "Bearer " + k.Custom;
            HttpResult r = Http.Get(b + "/models", h, 8000, ct);
            if (r.ConnectFailed) { st.Status = "Not reachable"; st.Error = r.Error; return; }
            st.Reachable = true;
            if (r.Status == 401 || r.Status == 403) { st.NeedsKey = true; st.Status = "The server wants an API key"; return; }
            List<object> data = Json.List(r.Json, "data") ?? Json.List(r.Json, "models");
            if (data != null)
                foreach (object o in data)
                {
                    string id = Json.Str(o, "id") ?? Json.Str(o, "name") ?? Json.Str(o, "model");
                    if (string.IsNullOrEmpty(id) || id.IndexOf("embed", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    ModelEntry m = new ModelEntry();
                    m.Provider = "custom";
                    m.Id = id;
                    m.Display = id;
                    m.Loaded = true;
                    list.Add(m);
                }
            if (!string.IsNullOrEmpty(k.CustomModel))
            {
                bool have = false;
                foreach (ModelEntry m in list) if (m.Id == k.CustomModel) have = true;
                if (!have)
                {
                    ModelEntry m = new ModelEntry();
                    m.Provider = "custom";
                    m.Id = k.CustomModel;
                    m.Display = k.CustomModel;
                    m.Loaded = true;
                    list.Insert(0, m);
                }
            }
            st.Status = list.Count > 0 ? "Connected" : "Connected, but it lists no models";
        }

        public static string LoadLmStudioModel(ProviderKeys k, string baseUrl, string key, int ctx, bool unloadOthers, CancelToken ct, out string instanceId)
        {
            instanceId = null;
            string root = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? baseUrl.Substring(0, baseUrl.Length - 3) : baseUrl;
            Dictionary<string, string> h = LmHeaders(k);
            if (unloadOthers)
            {
                HttpResult cur = Http.Get(root + "/api/v1/models", h, 5000, ct);
                List<object> arr = Json.List(cur.Json, "models");
                if (arr != null)
                    foreach (object o in arr)
                    {
                        string type = Json.Str(o, "type") ?? "llm";
                        if (type != "llm" && type != "vlm") continue;
                        List<object> inst = Json.List(o, "loaded_instances");
                        if (inst == null) continue;
                        foreach (object i in inst)
                        {
                            string iid = Json.Str(i, "id");
                            if (!string.IsNullOrEmpty(iid))
                                Http.PostJson(root + "/api/v1/models/unload", h, Json.Write(Json.O("instance_id", iid)), 60000, ct);
                        }
                    }
            }
            Dictionary<string, object> body = Json.O("model", key);
            if (ctx > 0) body["context_length"] = ctx;
            HttpResult r = Http.PostJson(root + "/api/v1/models/load", h, Json.Write(body), 15 * 60 * 1000, ct);
            if (r.Cancelled) return "Cancelled";
            if (!r.Ok) return r.Error ?? ("HTTP " + r.Status);
            instanceId = Json.Str(r.Json, "instance_id") ?? key;
            lock (lmLoads) lmLoads.Add(new string[] { root, instanceId, k.LmStudioToken ?? "" });
            return null;
        }

        static readonly List<string[]> lmLoads = new List<string[]>();

        public static void UnloadOurLmStudioModels(int budgetMs)
        {
            List<string[]> list;
            lock (lmLoads) { list = new List<string[]>(lmLoads); lmLoads.Clear(); }
            if (list.Count == 0) return;
            DateTime until = DateTime.UtcNow.AddMilliseconds(budgetMs);
            HashSet<string> done = new HashSet<string>(StringComparer.Ordinal);
            foreach (string[] l in list)
            {
                if (!done.Add(l[0] + "\n" + l[1])) continue;
                int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
                if (left < 200) break;
                ProviderKeys k = new ProviderKeys();
                k.LmStudioToken = l[2];
                try { Http.PostJson(l[0] + "/api/v1/models/unload", LmHeaders(k), Json.Write(Json.O("instance_id", l[1])), Math.Min(left, 3000), new CancelToken()); }
                catch { }
            }
        }

        public static LlmBackend CreateBackend(ModelEntry m, ProviderKeys k, string baseUrl)
        {
            switch (m.Provider)
            {
                case "anthropic":
                    {
                        AnthropicBackend b = new AnthropicBackend();
                        b.ApiKey = k.Anthropic;
                        b.Model = m.Id;
                        b.Provider = "anthropic";
                        b.SupportsAdaptive = m.AnthropicAdaptive;
                        b.SupportsDisable = m.AnthropicDisable;
                        b.SupportsBetweenTools = m.AnthropicBetweenTools;
                        b.SupportsEnabled = m.AnthropicEnabled;
                        b.Binding = m.AnthropicBinding;
                        b.Interleaved = m.AnthropicInterleaved;
                        b.EffortLevels = m.EffortLevels;
                        if (m.EffortLevels != null) b.SupportsEffort = m.EffortLevels.Count > 0;
                        return b;
                    }
                case "openai":
                    {
                        OpenAiResponsesBackend b = new OpenAiResponsesBackend();
                        b.ApiKey = k.OpenAi;
                        b.Model = m.Id;
                        b.Provider = "openai";
                        b.Reasoning = m.ReasoningOptions != null;
                        return b;
                    }
                case "custom":
                    {
                        if ((k.CustomUrl ?? "").IndexOf("api.openai.com", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            OpenAiResponsesBackend rb = new OpenAiResponsesBackend();
                            rb.ApiKey = k.Custom;
                            rb.BaseUrl = (k.CustomUrl ?? "https://api.openai.com/v1").TrimEnd('/');
                            rb.Model = m.Id;
                            rb.Provider = "openai";
                            rb.Reasoning = OpenAiReasoning(m.Id);
                            return rb;
                        }
                        OpenAiChatBackend b = new OpenAiChatBackend();
                        b.BaseUrl = k.CustomUrl;
                        b.ApiKey = k.Custom;
                        b.Model = m.Id;
                        b.Provider = "custom";
                        b.OfficialOpenAi = (k.CustomUrl ?? "").IndexOf("api.openai.com", StringComparison.OrdinalIgnoreCase) >= 0;
                        return b;
                    }
                case "openrouter":
                    {
                        OpenAiChatBackend b = new OpenAiChatBackend();
                        b.BaseUrl = OpenRouterBase;
                        b.ApiKey = k.OpenRouter;
                        b.Model = m.Id;
                        b.Provider = "openrouter";
                        b.OpenRouter = true;
                        b.SupportsVision = m.Vision;
                        return b;
                    }
                case "gemini":
                    {
                        OpenAiChatBackend b = new OpenAiChatBackend();
                        b.BaseUrl = GeminiBase.TrimEnd('/') + "/v1beta/openai";
                        b.ApiKey = k.Gemini;
                        b.Model = m.Id;
                        b.Provider = "gemini";
                        b.Gemini = true;
                        b.GeminiBudget = m.Id.StartsWith("gemini-2", StringComparison.OrdinalIgnoreCase);
                        b.SupportsVision = true;
                        return b;
                    }
                case "engine":
                    {
                        OpenAiChatBackend b = new OpenAiChatBackend();
                        b.BaseUrl = Engine.Instance.BaseUrl;
                        b.ApiKey = Engine.Instance.ApiKey;
                        int gen = Engine.Instance.Generation;
                        b.Gate = delegate { return Engine.Instance.Gone(gen); };
                        b.Model = "local";
                        b.Provider = "engine";
                        b.PromptProgress = true;
                        b.BudgetThinking = m.BudgetThinking;
                        b.BudgetContext = m.Context;
                        b.SupportsVision = m.Vision;
                        b.Sampling = Engine.Instance.SamplingFor(m.Path);
                        return b;
                    }
                default:
                    {
                        OpenAiChatBackend b = new OpenAiChatBackend();
                        b.BaseUrl = baseUrl;
                        b.ApiKey = k.LmStudioToken;
                        b.Model = m.Id;
                        b.Provider = "lmstudio";
                        b.SupportsVision = m.Vision;
                        if (m.ReasoningOptions != null) b.LmStudioReasoning = string.Join(",", m.ReasoningOptions.ToArray());
                        return b;
                    }
            }
        }

        public static string Join(string sep, params string[] parts)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string p in parts)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (sb.Length > 0) sb.Append(sep);
                sb.Append(p);
            }
            return sb.ToString();
        }

        public static string Size(long bytes)
        {
            if (bytes <= 0) return null;
            double gb = bytes / 1073741824.0;
            if (gb >= 1) return gb.ToString(gb >= 100 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
            double mb = bytes / 1048576.0;
            return mb.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        }
    }
}
