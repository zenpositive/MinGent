using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGent
{
    [ComVisible(true)]
    class WvCdpEvent : ICoreWebView2DevToolsProtocolEventReceivedEventHandler
    {
        readonly Action<string> cb;
        public WvCdpEvent(Action<string> cb) { this.cb = cb; }
        public int Invoke(IntPtr sender, ICoreWebView2DevToolsProtocolEventReceivedEventArgs a)
        {
            try
            {
                IntPtr p;
                if (a != null && a.ParameterObjectAsJson(out p) >= 0) cb(WebView2Runtime.Str(p));
            }
            catch { }
            return 0;
        }
    }

    class ConsoleEntry
    {
        public long Seq;
        public int Page;
        public string Kind = "log";
        public string Text = "";
        public string Where = "";
        public int Count = 1;
        public bool Agent;

        public bool IsMessage { get { return Kind == "log" || Kind == "info" || Kind == "warn" || Kind == "error" || Kind == "debug"; } }

        public string Plain()
        {
            string lead = Kind == "input" ? "> " : Kind == "result" || Kind == "fault" ? "< " : "";
            string t = lead + Text;
            if (Count > 1) t += " (" + Count.ToString(CultureInfo.InvariantCulture) + " times)";
            if (Where.Length > 0) t += "  " + Where;
            return t;
        }
    }

    static class ConsoleFormat
    {
        public const int MaxText = 4000;

        public static string Clip(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "\u2026";
        }

        public static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n');
            return (i < 0 ? s : s.Substring(0, i)).TrimEnd('\r');
        }

        public static ConsoleEntry FromApiCall(object p)
        {
            string type = Json.Str(p, "type") ?? "log";
            if (type == "endGroup") return null;
            List<object> args = Json.List(p, "args") ?? new List<object>();
            ConsoleEntry e = new ConsoleEntry();
            e.Kind = Level(type);
            e.Where = FrameWhere(Json.Get(p, "stackTrace", "callFrames", 0));
            string text = Args(args);
            if (type == "assert") text = "Assertion failed" + (text.Length > 0 ? ": " + text : "");
            else if (type == "trace") text = "console.trace" + (text.Length > 0 ? ": " + text : "");
            else if (type == "clear") { text = "The page cleared the console."; e.Kind = "note"; }
            else if ((type == "startGroup" || type == "startGroupCollapsed") && text.Length == 0) text = "console.group";
            e.Text = Clip(text, MaxText);
            return e;
        }

        static string Level(string type)
        {
            switch (type)
            {
                case "error": case "assert": return "error";
                case "warning": return "warn";
                case "debug": return "debug";
                case "info": return "info";
            }
            return "log";
        }

        public static ConsoleEntry FromException(object p)
        {
            object d = Json.Get(p, "exceptionDetails");
            if (d == null) return null;
            ConsoleEntry e = new ConsoleEntry();
            e.Kind = "error";
            e.Text = Clip(ExceptionText(d, true), MaxText);
            object frame = Json.Get(d, "stackTrace", "callFrames", 0);
            e.Where = frame != null ? FrameWhere(frame) : Where(Json.Str(d, "url"), Json.Get(d, "lineNumber") != null ? Json.Int(d, "lineNumber") : -1, Json.Get(d, "columnNumber") != null ? Json.Int(d, "columnNumber") : -1);
            return e;
        }

        public static string ExceptionText(object d, bool uncaught)
        {
            string text = Json.Str(d, "text") ?? "Uncaught";
            object ex = Json.Get(d, "exception");
            if (ex == null) return text;
            string body = Json.Str(ex, "subtype") == "error" ? Stack(Json.Str(ex, "description") ?? "", 4) : Remote(ex, true);
            if (!uncaught) return body.Length > 0 ? body : text;
            return text.EndsWith(body, StringComparison.Ordinal) ? text : text + " " + body;
        }

        public static ConsoleEntry FromLog(object p)
        {
            object en = Json.Get(p, "entry");
            if (en == null) return null;
            string level = Json.Str(en, "level") ?? "info";
            if (level == "verbose") return null;
            ConsoleEntry e = new ConsoleEntry();
            e.Kind = level == "error" ? "error" : level == "warning" ? "warn" : "info";
            string text = Json.Str(en, "text") ?? "";
            string url = Json.Str(en, "url") ?? "";
            if (Json.Str(en, "source") == "network" && url.Length > 0 && text.IndexOf(url, StringComparison.Ordinal) < 0) text += " " + Clip(url, 300);
            e.Text = Clip(text, MaxText);
            e.Where = Where(url, Json.Get(en, "lineNumber") != null ? Json.Int(en, "lineNumber") : -1, -1);
            return e;
        }

        public static string FrameWhere(object frame)
        {
            if (frame == null) return "";
            return Where(Json.Str(frame, "url"), Json.Int(frame, "lineNumber"), Json.Int(frame, "columnNumber"));
        }

        public static string Where(string url, int line, int col)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string name = url;
            int q = name.IndexOfAny(new char[] { '?', '#' });
            if (q > 0) name = name.Substring(0, q);
            int scheme = name.IndexOf("://", StringComparison.Ordinal);
            if (scheme > 0) name = name.Substring(scheme + 3);
            int slash = name.TrimEnd('/').LastIndexOf('/');
            if (slash >= 0 && !name.EndsWith("/", StringComparison.Ordinal)) name = name.Substring(slash + 1);
            try { name = Uri.UnescapeDataString(name); } catch { }
            if (name.Length > 60) name = "\u2026" + name.Substring(name.Length - 59);
            if (line < 0) return name;
            return name + ":" + (line + 1).ToString(CultureInfo.InvariantCulture) + (col >= 0 ? ":" + (col + 1).ToString(CultureInfo.InvariantCulture) : "");
        }

        public static string Args(List<object> args)
        {
            StringBuilder sb = new StringBuilder();
            int start = 0;
            if (args.Count > 0 && Json.Str(args[0], "type") == "string")
            {
                string first = Json.Str(args[0], "value") ?? "";
                start = 1;
                sb.Append(first.IndexOf('%') >= 0 ? Substitute(first, args, ref start) : first);
            }
            for (int i = start; i < args.Count && sb.Length <= MaxText; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Remote(args[i], true));
            }
            return sb.ToString();
        }

        static string Substitute(string f, List<object> args, ref int used)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < f.Length; i++)
            {
                char c = f[i];
                if (c != '%' || i + 1 >= f.Length) { sb.Append(c); continue; }
                char k = f[i + 1];
                if (k == '%') { sb.Append('%'); i++; continue; }
                if ("sdifoOjc".IndexOf(k) < 0) { sb.Append(c); continue; }
                i++;
                if (used >= args.Count) { sb.Append('%').Append(k); continue; }
                object a = args[used++];
                if (k == 'c') continue;
                string v = Remote(a, k == 's');
                if (k == 'd' || k == 'i')
                {
                    double d;
                    v = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && !double.IsNaN(d) && !double.IsInfinity(d) ? Math.Truncate(d).ToString("R", CultureInfo.InvariantCulture) : "NaN";
                }
                sb.Append(v);
            }
            return sb.ToString();
        }

        public static string Remote(object o, bool top)
        {
            string type = Json.Str(o, "type") ?? "";
            string sub = Json.Str(o, "subtype") ?? "";
            string desc = Json.Str(o, "description");
            string unser = Json.Str(o, "unserializableValue");
            object v = Json.Get(o, "value");
            switch (type)
            {
                case "string": { string s = v as string ?? ""; return top ? s : Quote(s, 200); }
                case "number": return unser ?? Number(v) ?? desc ?? "NaN";
                case "boolean": return v is bool ? ((bool)v ? "true" : "false") : (desc ?? "false");
                case "undefined": return "undefined";
                case "bigint": return unser ?? desc ?? "0n";
                case "symbol": return desc ?? "Symbol()";
                case "function": return Function(desc);
            }
            if (sub == "null") return "null";
            if (sub == "error") return Stack(desc ?? "Error", top ? 4 : 0);
            if (sub == "node" || sub == "regexp" || sub == "date") return desc ?? sub;
            object pv = Json.Get(o, "preview");
            if (pv != null) return Preview(pv);
            if (v != null) return Clip(Json.Write(v), MaxText);
            return desc ?? "Object";
        }

        static string Number(object v)
        {
            if (!(v is double)) return null;
            double d = (double)v;
            return d == Math.Truncate(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Quote(string s, int max)
        {
            string t = Clip(s, max).Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("'", "\\'");
            return "'" + t + "'";
        }

        static string Function(string desc)
        {
            string d = FirstLine(desc ?? "").Trim();
            if (d.Length == 0) return "\u0192";
            if (d.Length > 80) d = d.Substring(0, 80) + "\u2026";
            return d;
        }

        static readonly Regex StackUrl = new Regex(@"(?:https?|file)://[^\s()]+", RegexOptions.CultureInvariant);
        static readonly Regex LineCol = new Regex(@"^(.*?)((?::\d+){1,2})$", RegexOptions.CultureInvariant);

        static string ShortUrl(string url)
        {
            Match m = LineCol.Match(url);
            string at = m.Success ? m.Groups[2].Value : "";
            string place = Where(m.Success ? m.Groups[1].Value : url, -1, -1);
            return place.Length > 0 ? place + at : url;
        }

        public static string Stack(string desc, int frames)
        {
            string[] lines = (desc ?? "").Replace("\r", "").Split('\n');
            StringBuilder sb = new StringBuilder(lines[0]);
            int shown = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (!l.StartsWith("at ", StringComparison.Ordinal)) { if (shown == 0 && l.Length > 0) sb.Append('\n').Append(l); continue; }
                if (shown >= frames) { if (frames > 0) sb.Append("\n    \u2026"); break; }
                sb.Append("\n    ").Append(StackUrl.Replace(l, delegate (Match m) { return ShortUrl(m.Value); }));
                shown++;
            }
            return sb.ToString();
        }

        static string Preview(object pv)
        {
            string sub = Json.Str(pv, "subtype") ?? "";
            string desc = Json.Str(pv, "description") ?? "";
            bool overflow = Json.Bool(pv, "overflow");
            List<object> props = Json.List(pv, "properties") ?? new List<object>();
            List<object> entries = Json.List(pv, "entries");
            StringBuilder sb = new StringBuilder();
            if (sub == "node" || sub == "error" || sub == "regexp" || sub == "date") return sub == "error" ? Stack(desc, 0) : desc;
            if (entries != null)
            {
                sb.Append(desc).Append(" {");
                for (int i = 0; i < entries.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    object key = Json.Get(entries[i], "key");
                    if (key != null) sb.Append(Inline(key)).Append(" => ");
                    sb.Append(Inline(Json.Get(entries[i], "value")));
                }
                if (overflow) sb.Append(entries.Count > 0 ? ", \u2026" : "\u2026");
                return sb.Append('}').ToString();
            }
            bool array = sub == "array" || sub == "typedarray";
            if (array)
            {
                if (desc.Length > 0) sb.Append(desc).Append(' ');
                sb.Append('[');
            }
            else
            {
                if (desc.Length > 0 && desc != "Object") sb.Append(desc).Append(' ');
                sb.Append('{');
            }
            bool first = true;
            foreach (object p in props)
            {
                string name = Json.Str(p, "name") ?? "";
                if (!first) sb.Append(", ");
                first = false;
                if (!array || !IsIndex(name)) sb.Append(name).Append(": ");
                sb.Append(Prop(p));
            }
            if (overflow) sb.Append(first ? "\u2026" : ", \u2026");
            return sb.Append(array ? ']' : '}').ToString();
        }

        static bool IsIndex(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        static string Inline(object pv)
        {
            if (pv == null) return "";
            List<object> props = Json.List(pv, "properties");
            if (Json.Str(pv, "type") == "object" && props != null && props.Count > 0) return Preview(pv);
            string d = Json.Str(pv, "description") ?? "";
            return Json.Str(pv, "type") == "string" ? Quote(d, 200) : d;
        }

        static string Prop(object p)
        {
            object vp = Json.Get(p, "valuePreview");
            if (vp != null) return Preview(vp);
            string type = Json.Str(p, "type") ?? "";
            string val = Json.Str(p, "value");
            if (type == "string") return Quote(val ?? "", 200);
            if (type == "function") return "\u0192";
            if (type == "accessor") return "(\u2026)";
            if (type == "undefined") return "undefined";
            return val ?? type;
        }

        public static string Result(object res, out bool failed)
        {
            object details = Json.Get(res, "exceptionDetails");
            failed = details != null;
            if (failed) return ExceptionText(details, false);
            object r = Json.Get(res, "result");
            return r == null ? "undefined" : Remote(r, true);
        }
    }

    class ConsoleLog
    {
        public const int Limit = 1000;
        readonly object gate = new object();
        readonly List<ConsoleEntry> list = new List<ConsoleEntry>();
        readonly HashSet<ulong> keys = new HashSet<ulong>();
        long seq, newsMark;
        int page;
        public event Action Changed;

        public long Last { get { lock (gate) return seq; } }

        void Fire()
        {
            Action h = Changed;
            if (h != null) { try { h(); } catch { } }
        }

        public void NewPage(string url)
        {
            lock (gate)
            {
                page++;
                keys.Clear();
                ConsoleEntry e = new ConsoleEntry();
                e.Kind = "page";
                e.Text = url ?? "";
                AddLocked(e);
            }
            Fire();
        }

        public ConsoleEntry Add(string kind, string text, bool agent)
        {
            ConsoleEntry e = new ConsoleEntry();
            e.Kind = kind;
            e.Text = ConsoleFormat.Clip(text ?? "", 20000);
            e.Agent = agent;
            lock (gate) AddLocked(e);
            Fire();
            return e;
        }

        public void Add(ConsoleEntry e, string key)
        {
            if (e == null) return;
            lock (gate)
            {
                if (key != null && !keys.Add(Hash(key))) return;
                if (keys.Count > 20000) keys.Clear();
                AddLocked(e);
            }
            Fire();
        }

        static ulong Hash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h;
        }

        void AddLocked(ConsoleEntry e)
        {
            e.Page = page;
            if (e.IsMessage && list.Count > 0)
            {
                ConsoleEntry last = list[list.Count - 1];
                if (last.Kind == e.Kind && last.Page == e.Page && last.Text == e.Text && last.Where == e.Where)
                {
                    last.Count++;
                    last.Seq = ++seq;
                    return;
                }
            }
            e.Seq = ++seq;
            list.Add(e);
            if (list.Count > Limit) list.RemoveRange(0, list.Count - Limit);
        }

        public void Clear()
        {
            lock (gate) { list.Clear(); keys.Clear(); newsMark = seq; }
            Fire();
        }

        public List<ConsoleEntry> Entries() { lock (gate) return new List<ConsoleEntry>(list); }

        public List<ConsoleEntry> Since(long mark)
        {
            List<ConsoleEntry> l = new List<ConsoleEntry>();
            lock (gate) foreach (ConsoleEntry e in list) if (e.Seq > mark) l.Add(e);
            return l;
        }

        public void Counts(out int errors, out int warnings)
        {
            errors = warnings = 0;
            lock (gate)
                foreach (ConsoleEntry e in list)
                {
                    if (e.Page != page) continue;
                    if (e.Kind == "error") errors += e.Count;
                    else if (e.Kind == "warn") warnings += e.Count;
                }
        }

        public void SkipNews() { lock (gate) newsMark = seq; }

        public string News()
        {
            List<ConsoleEntry> errs = new List<ConsoleEntry>();
            lock (gate)
            {
                foreach (ConsoleEntry e in list) if (e.Seq > newsMark && e.Kind == "error" && !Noise(e)) errs.Add(e);
                newsMark = seq;
            }
            if (errs.Count == 0) return null;
            return errs.Count == 1 ? "The page logged an error in its console. browser_console shows it."
                                   : "The page logged " + errs.Count.ToString(CultureInfo.InvariantCulture) + " errors in its console. browser_console shows them.";
        }

        static bool Noise(ConsoleEntry e)
        {
            return e.Text.StartsWith("Failed to load resource", StringComparison.Ordinal) && e.Text.IndexOf("/favicon.ico", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string Line(ConsoleEntry e)
        {
            string[] lines = e.Text.Replace("\r", "").Split('\n');
            StringBuilder sb = new StringBuilder();
            sb.Append('[').Append(e.Kind).Append("] ").Append(lines[0]);
            for (int i = 1; i < lines.Length && i < 8; i++) sb.Append('\n').Append(lines[i].Length > 0 && char.IsWhiteSpace(lines[i][0]) ? "" : "    ").Append(lines[i]);
            if (lines.Length > 8) sb.Append("\n    \u2026");
            string tail = e.Where;
            if (e.Count > 1) tail = (tail.Length > 0 ? tail + ", " : "") + e.Count.ToString(CultureInfo.InvariantCulture) + " times";
            if (tail.Length > 0) sb.Append("  (").Append(tail).Append(')');
            return sb.ToString();
        }

        public string ForAgent(string place, int maxChars)
        {
            List<ConsoleEntry> cur = new List<ConsoleEntry>();
            lock (gate)
            {
                foreach (ConsoleEntry e in list) if (e.IsMessage && e.Page == page) cur.Add(e);
                newsMark = seq;
            }
            if (cur.Count == 0) return "The console of " + place + " is empty: the page has logged nothing since it loaded.";
            int errors = 0, warnings = 0, others = 0;
            foreach (ConsoleEntry e in cur)
            {
                if (e.Kind == "error") errors += e.Count;
                else if (e.Kind == "warn") warnings += e.Count;
                else others += e.Count;
            }
            List<string> lines = new List<string>();
            int used = 0, left = 0;
            for (int i = cur.Count - 1; i >= 0; i--)
            {
                string l = Line(cur[i]);
                if (lines.Count >= 80 || used + l.Length + 1 > maxChars) { left = i + 1; break; }
                lines.Insert(0, l);
                used += l.Length + 1;
            }
            List<string> parts = new List<string>();
            if (errors > 0) parts.Add(errors == 1 ? "1 error" : errors.ToString(CultureInfo.InvariantCulture) + " errors");
            if (warnings > 0) parts.Add(warnings == 1 ? "1 warning" : warnings.ToString(CultureInfo.InvariantCulture) + " warnings");
            if (others > 0) parts.Add(others == 1 ? "1 other message" : others.ToString(CultureInfo.InvariantCulture) + " other messages");
            string head = "Console of " + place + " since the page loaded, oldest first: " + string.Join(", ", parts.ToArray()) + "." +
                          (left > 0 ? " The " + left.ToString(CultureInfo.InvariantCulture) + " oldest are left out." : "");
            return head + "\n" + string.Join("\n", lines.ToArray());
        }
    }

    static class ConsoleCheck
    {
        const int Ident = 0, Str = 1, Num = 2, Punct = 3, Rx = 4, Tmpl = 5;

        public const string Clicks = "click", Typing = "type", Submits = "submit", Keys = "keys", Scrolls = "scroll", Pages = "navigate",
                            Windows = "window", Hidden = "hidden", Network = "network", Secrets = "secrets", Devices = "devices";

        class Tok
        {
            public int Kind;
            public string Text = "";
            public bool Opens, Closes;
        }

        class Facts
        {
            public readonly HashSet<string> Declared = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Functions = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> PromiseParams = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Tainted = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Alias = new Dictionary<string, string>(StringComparer.Ordinal);
            public bool[] Targets, Sites;
            public bool FieldKeys, Editable;

            public string Canon(string name)
            {
                string c;
                if (name == null) return "";
                return Alias.TryGetValue(name, out c) ? c : name;
            }

            public bool Own(string name) { return Declared.Contains(name) && !Alias.ContainsKey(name); }

            public bool Callback(string name) { return (Functions.Contains(name) || PromiseParams.Contains(name)) && !Tainted.Contains(name); }
        }

        static Dictionary<string, string> Map(params string[] pairs)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                foreach (string n in pairs[i + 1].Split(' ')) d[n] = pairs[i];
            return d;
        }

        static HashSet<string> Set(string words) { return new HashSet<string>(words.Split(' '), StringComparer.Ordinal); }

        static readonly Dictionary<string, string> Anywhere = Map(
            Clicks, "click dispatchEvent fireEvent userEvent createEvent initEvent initMouseEvent initUIEvent initTouchEvent MouseEvent PointerEvent TouchEvent WheelEvent FocusEvent DragEvent UIEvent Touch TouchList DataTransfer",
            Keys, "KeyboardEvent initKeyboardEvent",
            Typing, "InputEvent CompositionEvent initCompositionEvent execCommand setRangeText",
            Submits, "requestSubmit SubmitEvent",
            Scrolls, "scrollTo scrollBy scrollIntoView scrollIntoViewIfNeeded scrollByLines scrollByPages",
            Pages, "pushState replaceState",
            Windows, "showModalDialog",
            Hidden, "eval Function AsyncFunction GeneratorFunction AsyncGeneratorFunction importScripts Worker SharedWorker WebAssembly insertAdjacentHTML createContextualFragment setHTMLUnsafe parseHTMLUnsafe parseFromString srcdoc __defineGetter__ __defineSetter__ __lookupGetter__ __lookupSetter__ defineProperty defineProperties setPrototypeOf fromCodePoint unescape execScript XSLTProcessor globalEval getScript",
            Devices, "clipboard ClipboardItem getUserMedia getDisplayMedia geolocation requestPermission requestDevice requestPort requestMIDIAccess requestStorageAccess showOpenFilePicker showSaveFilePicker showDirectoryPicker");

        static readonly Dictionary<string, string> OnWebsites = Map(
            Network, "fetch fetchLater XMLHttpRequest sendBeacon WebSocket EventSource RTCPeerConnection WebTransport axios",
            Secrets, "cookie cookieStore localStorage sessionStorage indexedDB caches credentials PasswordCredential FederatedCredential FormData");

        static readonly Dictionary<string, string> Objects = Map(
            "window", "window self top parent globalThis frames opener content contentWindow",
            "location", "location",
            "history", "history",
            "navigation", "navigation",
            "document", "document contentDocument ownerDocument");

        static readonly HashSet<string> FieldProps = Set("value checked selected selectedIndex files valueAsNumber valueAsDate defaultValue defaultChecked defaultSelected indeterminate");
        static readonly HashSet<string> FieldAttrs = Set("value checked selected selectedindex");
        static readonly HashSet<string> LoadProps = Set("src srcset href action formAction data poster background ping codebase");
        static readonly HashSet<string> LoadAttrs = Set("src srcset href action formaction data poster background ping codebase xlink:href");
        static readonly HashSet<string> CodeTags = Set("script iframe frame frameset object embed link meta base portal fencedframe applet");
        static readonly HashSet<string> Globals = Set("window self top parent globalThis frames opener this content");
        static readonly HashSet<string> Before = Set("return typeof instanceof in of new delete void throw case do else yield await");
        static readonly HashSet<string> AssignOps = Set("= += -= *= /= %= **= <<= >>= >>>= &= |= ^= &&= ||= ??= ++ --");
        static readonly HashSet<string> WindowOps = Set("open alert confirm prompt print close scroll focus blur copy");
        static readonly HashSet<string> EventCalls = Set("change dblclick keyup keydown keypress mouseenter mouseleave mousedown mouseup mouseover mouseout mousemove contextmenu focusin focusout hover");
        static readonly HashSet<string> Widgets = Set("modal collapse dropdown tooltip popover tab tabs offcanvas toast carousel dialog accordion sidebar");
        static readonly HashSet<string> WidgetVerbs = Set("show hide toggle open close next prev cycle");
        static readonly HashSet<string> EditorCalls = Set("setText setData setContent setHTML setMarkdown insertContent insertHtml");
        static readonly string[] Puncts = { ">>>=", "...", "===", "!==", "**=", "<<=", ">>=", ">>>", "&&=", "||=", "??=", "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "?.", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "**", "<<", ">>" };
        static readonly Regex DomHandler = new Regex(@"^(?:(?:on|handle)(?:Click|DoubleClick|DblClick|Submit|Change|Input|Key\w*|Mouse\w*|Pointer\w*|Touch\w*|Focus\w*|Blur|Select|Drag\w*|Drop|Paste|Copy|Cut|Press\w*|Tap|Toggle|Scroll|Wheel|ContextMenu|Login|LogIn|Logout|LogOut|SignIn|SignOut|SignUp|Register|Save|Delete|Remove|Send|Buy|Purchase|Checkout|Pay|Order|Confirm|Cancel|Accept|Decline|Next|Prev|Previous|Back|Upload|Add|Open|Close)(?![a-z])\w*|on(?:click|dblclick|submit|change|input|key\w+|mouse\w+|pointer\w+|touch\w+|focus\w*|blur|scroll|wheel|select|reset|contextmenu|drag\w*|drop|paste|copy|cut))$", RegexOptions.CultureInvariant);
        static readonly Regex HtmlRuns = new Regex(@"<\s*/?\s*(?:script|iframe|frame|frameset|object|embed|base|link|meta|portal|fencedframe|applet)\b|<[a-z][^>]*[\s/]on[a-z]+\s*=", RegexOptions.CultureInvariant);
        static readonly Regex OuterUrl = new Regex(@"url\(\s*['""]?\s*(?:https?:|//)", RegexOptions.CultureInvariant);
        static readonly Regex FormWord = new Regex(@"(?:^|[^a-z])form|Form", RegexOptions.CultureInvariant);
        static readonly Regex WidgetWord = new Regex(@"modal|Modal|dialog|Dialog|Dropdown|dropdown|Collapse|Offcanvas|offcanvas|Popover|popover|Tooltip|tooltip|Toast", RegexOptions.CultureInvariant);
        static readonly Regex EditableWord = new Regex(@"contenteditable|textarea|textbox|activeelement|iscontenteditable", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static string Message(string kind, string what)
        {
            string w = string.IsNullOrEmpty(what) ? "" : " (" + what + ")";
            switch (kind)
            {
                case Clicks: return "Not run: this code clicks, focuses or fires events on the page" + w + ". The console is only for looking into the page and debugging it. To click, use browser_click with a ref from browser_read.";
                case Typing: return "Not run: this code fills in or changes form fields" + w + ". The console is only for looking into the page and debugging it. Type with browser_type.";
                case Keys: return "Not run: this code fakes key presses" + w + ". Use browser_press_key.";
                case Submits: return "Not run: this code submits a form" + w + ". Click the form's button with browser_click, or use browser_type with submit=true.";
                case Scrolls: return "Not run: this code scrolls the page" + w + ". Use browser_scroll.";
                case Pages: return "Not run: this code opens another page or changes the address" + w + ". Use browser_navigate; it also goes back, forward and reloads.";
                case Windows: return "Not run: this code opens a window or dialog" + w + ". Open addresses with browser_navigate.";
                case Hidden: return "Not run: this code builds or hides the code it runs" + w + ". Write it out plainly, without eval, Function, timers given text, HTML that can run scripts, encoded text, or calls through computed names, call, apply or bind.";
                case Network: return "Not run: on websites the console can't make network requests or load addresses" + w + ". Read pages with browser_navigate and browser_read, or with web_fetch.";
                case Secrets: return "Not run: on websites the console can't read cookies, storage, saved sign-ins or form fields" + w + ".";
                case Devices: return "Not run: the console can't use the clipboard or ask for devices, files or permissions" + w + ". The value of the code comes back to you, so return what you need instead.";
            }
            return "Not run" + w + ".";
        }

        public static string Refusal(string code, bool website)
        {
            string s = code ?? "";
            bool ambiguous;
            string r = Pass(s, website, false, out ambiguous);
            if (r != null || !ambiguous) return r;
            return Pass(s, website, true, out ambiguous);
        }

        static string Pass(string code, bool web, bool flip, out bool ambiguous)
        {
            string hidden;
            List<Tok> t = Lex(code, flip, out hidden, out ambiguous);
            if (hidden != null) return Message(Hidden, hidden);
            t = Fold(t);
            Facts f = Study(t);
            for (int i = 0; i < t.Count; i++)
            {
                string r = At(t, i, web, f);
                if (r != null) return r;
            }
            return null;
        }

        static string At(List<Tok> t, int i, bool web, Facts f)
        {
            Tok k = t[i];
            if (k.Kind == Str || k.Kind == Tmpl) return InText(k.Text, web);
            if (k.Kind == Punct)
            {
                if (k.Text == "]" && (Called(t, i) || GroupCalled(t, i)) && !NumberIndex(t, i)) return Message(Hidden, "a call through a computed name");
                if (k.Text == "]" && !NumberIndex(t, i) && StoredThenCalled(t, i)) return Message(Hidden, "a call through a computed name");
                if (k.Text == "[" && i > 0 && t[i - 1].Kind == Punct && (t[i - 1].Text == "{" || t[i - 1].Text == ",") && ComputedPattern(t, i)) return Message(Hidden, "a name taken through a computed key");
                if (k.Text == "[" && i > 0 && MemberBase(t[i - 1]) && PiecedKey(t, i)) return Message(Hidden, "a name put together from pieces");
                return null;
            }
            if (k.Kind != Ident) return null;
            string n = k.Text;
            bool member = IsMember(t, i);
            bool called = Called(t, i) || GroupCalled(t, i);
            bool assigned = Assigned(t, i) || f.Targets[i] || PrefixUpdate(t, i);
            string recv = member ? f.Canon(Receiver(t, i)) : "";
            string kind;
            if (Anywhere.TryGetValue(n, out kind)) return Message(kind, Show(n, member, called));
            if (web && OnWebsites.TryGetValue(n, out kind)) return Message(kind, Show(n, member, called));
            if (web && n.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0) return Message(Secrets, n);
            string special = Special(t, i, n, member, called);
            if (special != null) return special;
            if (member && (n == "call" || n == "apply" || n == "bind")) return Message(Hidden, "." + n);
            if (n == "__proto__" && (Assigned(t, i) || IsKey(t, i))) return Message(Hidden, "__proto__");
            if (member && n == "submit") return Message(Submits, ".submit");
            if (n == "setTimeout" || n == "setInterval" || n == "setImmediate")
                return called && FunctionArg(t, i, f) ? null : Message(Hidden, n + " without a function");
            if (!member && (n == "with" || n == "import") && Next(t, i, "(")) return Message(Hidden, n + "(...)");
            if (called && !Definition(t, i))
            {
                if (DomHandler.IsMatch(n)) return Message(Clicks, n + "()");
                string r = member ? MemberCall(t, i, n, recv, web, f) : f.Own(n) ? null : BareCall(f.Canon(n));
                if (r != null) return r;
            }
            else if (!called && !assigned && WindowOps.Contains(n) && !IsKey(t, i) && !f.Sites[i] && !Definition(t, i) &&
                     (member ? Globals.Contains(recv) && recv != "this" : !f.Own(n)))
                return WindowRef(n, member ? recv + "." : "");
            if (assigned)
            {
                if (member && FieldProps.Contains(n)) return Message(Typing, "." + n + " =");
                if (member && (n == "innerHTML" || n == "outerHTML")) return Message(Hidden, "." + n + " =");
                if (member && (n == "innerText" || n == "textContent" || n == "nodeValue") && f.Editable) return Message(Typing, "." + n + " =");
                if (member && (n == "scrollTop" || n == "scrollLeft")) return Message(Scrolls, "." + n + " =");
                if (n == "location" || recv == "location") return Message(Pages, (recv == "location" ? "location." + n : "location") + " =");
                if (web && member && LoadProps.Contains(n) && recv != "style") return Message(Network, "." + n + " =");
            }
            if (web && n == "value" && (member ? !AttributeValue(t, i) : ValueKey(t, i))) return Message(Secrets, ".value");
            if (IsKey(t, i) && (n == "scrollTop" || n == "scrollLeft")) return Message(Scrolls, n + ":");
            if (web && (n == "Image" || n == "Audio") && i > 0 && t[i - 1].Kind == Ident && t[i - 1].Text == "new") return Message(Network, "new " + n + "()");
            return null;
        }

        static string InText(string text, bool web)
        {
            string low = text.ToLowerInvariant();
            if (low.Replace("\t", "").Replace("\n", "").Replace("\r", "").Contains("javascript:")) return Message(Hidden, "a javascript: address");
            if (low.IndexOf('<') >= 0 && HtmlRuns.IsMatch(low)) return Message(Hidden, "HTML that can run scripts");
            if (web && OuterUrl.IsMatch(low)) return Message(Network, "a url() that loads an address");
            return null;
        }

        static string Show(string n, bool member, bool called)
        {
            return (member ? "." : "") + n + (called ? "()" : "");
        }

        static string Special(List<Tok> t, int i, string n, bool member, bool called)
        {
            switch (n)
            {
                case "constructor":
                    if (Next(t, i, ".") && i + 2 < t.Count && t[i + 2].Text == "name") return null;
                    return !member && Definition(t, i) ? null : Message(Hidden, "constructor");
                case "Reflect":
                    {
                        string m = Next(t, i, ".") && i + 2 < t.Count ? t[i + 2].Text : "";
                        return m == "ownKeys" || m == "has" || m == "getPrototypeOf" || m == "isExtensible" || m == "getOwnPropertyDescriptor" ? null : Message(Hidden, "Reflect" + (m.Length > 0 ? "." + m : ""));
                    }
                case "atob":
                    return InJsonParse(t, i) ? null : Message(Hidden, Show(n, member, called));
                case "fromCharCode":
                    return called && SpreadOnly(t, i) ? null : Message(Hidden, Show(n, member, called));
            }
            return null;
        }

        static string WindowRef(string n, string prefix)
        {
            string what = prefix + n + ", used without calling it";
            switch (n)
            {
                case "scroll": return Message(Scrolls, what);
                case "focus": case "blur": return Message(Clicks, what);
                case "copy": return Message(Devices, what);
            }
            return Message(Windows, what);
        }

        static string MemberCall(List<Tok> t, int i, string n, string recv, bool web, Facts f)
        {
            switch (n)
            {
                case "focus": case "blur": case "play": case "pause": case "trigger": case "triggerHandler": case "simulate":
                case "showModal": case "showPopover": case "togglePopover": case "$emit":
                    return Message(Clicks, "." + n + "()");
                case "select":
                    return EmptyCall(t, i) ? Message(Clicks, ".select()") : null;
                case "scroll":
                    return Message(Scrolls, ".scroll()");
                case "scrollTop": case "scrollLeft":
                    return EmptyCall(t, i) ? null : Message(Scrolls, "." + n + "(...)");
                case "val":
                    if (!EmptyCall(t, i)) return Message(Typing, ".val(...)");
                    return web ? Message(Secrets, ".val()") : null;
                case "serialize": case "serializeArray":
                    return web ? Message(Secrets, "." + n + "()") : null;
                case "setValue": case "insertText": case "executeEdits": case "setFieldValue": case "setFieldsValue": case "stepUp": case "stepDown":
                    return Message(Typing, "." + n + "()");
                case "reset":
                    return ChainMatches(t, i, FormWord, true) ? Message(Typing, ".reset()") : null;
                case "prop": case "attr":
                    {
                        string a;
                        if (Arg(t, i, 0, out a) && a != null && FieldAttrs.Contains(a.ToLowerInvariant()))
                        {
                            if (ArgCount(t, i) > 1) return Message(Typing, "." + n + "('" + a + "', ...)");
                            if (web) return Message(Secrets, "." + n + "('" + a + "')");
                        }
                        if (ArgStarts(t, i, 0, "{") && f.FieldKeys) return Message(Typing, "." + n + "({...})");
                        return null;
                    }
                case "setAttribute": case "setAttributeNS": case "toggleAttribute":
                    {
                        string a;
                        if (!Arg(t, i, n == "setAttributeNS" ? 1 : 0, out a)) return Message(Hidden, n + " with a name that isn't written out");
                        string l = (a ?? "").ToLowerInvariant();
                        if (l.StartsWith("on", StringComparison.Ordinal)) return Message(Hidden, "an " + l + " attribute");
                        if (l == "srcdoc") return Message(Hidden, "srcdoc");
                        if (FieldAttrs.Contains(l)) return Message(Typing, n + "('" + a + "')");
                        if (web && LoadAttrs.Contains(l)) return Message(Network, n + "('" + a + "')");
                        return null;
                    }
                case "createElement": case "createElementNS":
                    {
                        string a;
                        if (!Arg(t, i, n == "createElementNS" ? 1 : 0, out a)) return Message(Hidden, n + " with a tag that isn't written out");
                        return CodeTags.Contains((a ?? "").ToLowerInvariant()) ? Message(Hidden, n + "('" + a + "')") : null;
                    }
                case "open": case "alert": case "confirm": case "prompt": case "print": case "close":
                    return Globals.Contains(recv) || (n == "open" && recv == "document") ? Message(Windows, recv + "." + n + "()") : null;
                case "write": case "writeln":
                    return recv.IndexOf("document", StringComparison.OrdinalIgnoreCase) >= 0 ? Message(Hidden, recv + "." + n + "()") : null;
                case "assign": case "extend":
                    {
                        if (n == "assign" && recv == "location") return Message(Pages, "location.assign()");
                        if (recv != "Object" && recv != "$" && recv != "jQuery" && recv != "_") return null;
                        int first = n == "extend" && ArgIsBool(t, i, 0) ? 1 : 0;
                        if (ArgObject(t, i, first, f) == "location") return Message(Pages, recv + "." + n + "(location, ...)");
                        if (!ArgStarts(t, i, first, "{") && !ArgStarts(t, i, first, "[") && f.FieldKeys) return Message(Typing, recv + "." + n + " with form field values");
                        return null;
                    }
                case "replace": case "reload":
                    if (recv == "location") return Message(Pages, "location." + n + "()");
                    break;
                case "share":
                    return recv == "navigator" ? Message(Devices, "navigator.share()") : null;
                case "register":
                    return recv == "serviceWorker" ? Message(Hidden, "serviceWorker.register()") : null;
                case "getOwnPropertyDescriptor":
                    {
                        string a;
                        return Arg(t, i, 1, out a) && a != null && FieldProps.Contains(a) ? Message(Typing, "the " + a + " setter of form fields") : null;
                    }
                case "navigate": case "navigateByUrl": case "transitionTo": case "transitionToRoute":
                    return Message(Pages, "." + n + "()");
                case "visit":
                    return recv == "Turbo" || recv == "Turbolinks" || recv == "Inertia" || Routerish(recv) || UrlArg(t, i) ? Message(Pages, "." + n + "()") : null;
                case "load":
                    return web && ArgIsString(t, i, 0) ? Message(Network, ".load(...)") : null;
                case "ajax": case "get": case "post": case "getJSON":
                    return web && (recv == "$" || recv == "jQuery") ? Message(Network, recv + "." + n + "()") : null;
            }
            if (EventCalls.Contains(n) && EmptyCall(t, i)) return Message(n.StartsWith("key", StringComparison.Ordinal) ? Keys : Clicks, "." + n + "()");
            if (Widgets.Contains(n))
            {
                string a;
                if (Arg(t, i, 0, out a) && a != null && WidgetVerbs.Contains(a)) return Message(Clicks, "." + n + "('" + a + "')");
            }
            if ((n == "show" || n == "hide" || n == "toggle") && EmptyCall(t, i) && ChainMatches(t, i, WidgetWord, false)) return Message(Clicks, "." + n + "()");
            if (EditorCalls.Contains(n) && (recv.IndexOf("editor", StringComparison.OrdinalIgnoreCase) >= 0 || recv == "tinymce" || recv == "quill")) return Message(Typing, "." + n + "()");
            if (recv == "navigation" && (n == "back" || n == "forward" || n == "traverseTo" || n == "reload")) return Message(Pages, "navigation." + n + "()");
            if ((n == "back" || n == "forward" || n == "go" || n == "push" || n == "replace") && Routerish(recv)) return Message(Pages, recv + "." + n + "()");
            if ((n == "push" || (n == "replace" && ArgCount(t, i) == 1)) && i >= 2 && t[i - 2].Kind == Punct && t[i - 2].Text == ")" && UrlArg(t, i)) return Message(Pages, "." + n + "('" + FirstString(t, i) + "')");
            return null;
        }

        static string BareCall(string n)
        {
            switch (n)
            {
                case "open": case "alert": case "confirm": case "prompt": case "print": case "close": return Message(Windows, n + "()");
                case "scroll": return Message(Scrolls, "scroll()");
                case "focus": case "blur": return Message(Clicks, n + "()");
                case "navigate": return Message(Pages, "navigate()");
                case "copy": return Message(Devices, "copy()");
            }
            return null;
        }

        static bool Routerish(string recv)
        {
            return recv.IndexOf("router", StringComparison.OrdinalIgnoreCase) >= 0 || recv.IndexOf("history", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool UrlArg(List<Tok> t, int i)
        {
            string a = FirstString(t, i);
            if (a == null) return false;
            return a.StartsWith("/", StringComparison.Ordinal) || a.StartsWith("?", StringComparison.Ordinal) || a.StartsWith("#", StringComparison.Ordinal) ||
                   a.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || a.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
        }

        static string FirstString(List<Tok> t, int i)
        {
            string a;
            return Arg(t, i, 0, out a) ? a : null;
        }

        static bool InJsonParse(List<Tok> t, int i)
        {
            int s = ChainStart(t, i);
            return s >= 4 && t[s - 1].Text == "(" && t[s - 2].Text == "parse" && t[s - 3].Text == "." && t[s - 4].Text == "JSON";
        }

        static bool SpreadOnly(List<Tok> t, int i)
        {
            int o = CallOpen(t, i);
            if (o < 0 || o + 1 >= t.Count || t[o + 1].Text != "...") return false;
            int c = Close(t, o);
            for (int j = o + 2; j < c; j++) if (t[j].Kind == Num || t[j].Kind == Str || t[j].Kind == Tmpl || t[j].Kind == Rx) return false;
            return true;
        }

        static bool AttributeValue(List<Tok> t, int i)
        {
            if (i < 2) return false;
            Tok r = t[i - 2];
            if (r.Kind == Ident) return r.Text == "dataset" || r.Text.IndexOf("attr", StringComparison.OrdinalIgnoreCase) >= 0;
            if (r.Kind != Punct || (r.Text != ")" && r.Text != "]")) return false;
            int o = Opener(t, i - 2);
            if (o < 1 || t[o - 1].Kind != Ident) return false;
            string callee = t[o - 1].Text;
            return r.Text == ")" ? callee == "next" || callee.IndexOf("attr", StringComparison.OrdinalIgnoreCase) >= 0 : callee == "attributes";
        }

        static bool PiecedKey(List<Tok> t, int open)
        {
            int close = Close(t, open);
            bool plus = false;
            for (int j = open + 1; j < close; j++)
            {
                Tok x = t[j];
                if (x.Kind == Punct && x.Text == "+") plus = true;
                else if (x.Kind == Punct && (x.Text == "(" || x.Text == ")")) continue;
                else if (x.Kind != Str && x.Kind != Tmpl) return false;
            }
            return plus || (close - open > 2 && t[open + 1].Kind == Tmpl && !t[open + 1].Closes);
        }

        static bool ValueKey(List<Tok> t, int i)
        {
            if (i == 0 || i + 1 >= t.Count) return false;
            Tok p = t[i - 1], q = t[i + 1];
            if (p.Kind != Punct || (p.Text != "{" && p.Text != ",")) return false;
            if (q.Kind != Punct || (q.Text != "," && q.Text != "}" && q.Text != ":" && q.Text != "=")) return false;
            int open = -1, depth = 0;
            for (int j = i - 1; j >= 0 && open < 0; j--)
            {
                if (t[j].Kind != Punct) continue;
                if (t[j].Text == "}") depth++;
                else if (t[j].Text == "{") { if (depth == 0) open = j; else depth--; }
            }
            if (open < 0) return false;
            if (open > 0 && t[open - 1].Kind == Ident && (t[open - 1].Text == "const" || t[open - 1].Text == "let" || t[open - 1].Text == "var")) return true;
            int close = Close(t, open);
            if (close + 1 >= t.Count) return false;
            Tok a = t[close + 1];
            if (a.Kind == Punct && a.Text == "=") return true;
            if (a.Kind == Ident && (a.Text == "of" || a.Text == "in")) return true;
            int end = close + 1;
            for (int d = 0; end < t.Count; end++)
            {
                Tok x = t[end];
                if (x.Kind != Punct) continue;
                if (x.Text == "(" || x.Text == "[" || x.Text == "{") d++;
                else if (x.Text == ")" || x.Text == "]" || x.Text == "}") { if (d == 0) break; d--; }
            }
            return end < t.Count && t[end].Text == ")" && end + 1 < t.Count && t[end + 1].Kind == Punct && (t[end + 1].Text == "=>" || t[end + 1].Text == "{");
        }

        static bool IsMember(List<Tok> t, int i)
        {
            return i > 0 && t[i - 1].Kind == Punct && (t[i - 1].Text == "." || t[i - 1].Text == "?.");
        }

        static string Receiver(List<Tok> t, int i)
        {
            if (i < 2) return "";
            Tok r = t[i - 2];
            return r.Kind == Ident ? r.Text : "";
        }

        static bool Next(List<Tok> t, int i, string text)
        {
            return i + 1 < t.Count && t[i + 1].Kind == Punct && t[i + 1].Text == text;
        }

        static bool Called(List<Tok> t, int i)
        {
            int j = i + 1;
            if (j < t.Count && t[j].Kind == Punct && t[j].Text == "?.") j++;
            if (j >= t.Count) return false;
            if (t[j].Kind == Punct && t[j].Text == "(") return true;
            return j == i + 1 && t[j].Kind == Tmpl && t[j].Opens;
        }

        static bool GroupCalled(List<Tok> t, int i)
        {
            int j = i + 1;
            bool grouped = false;
            while (j < t.Count && t[j].Kind == Punct && t[j].Text == ")")
            {
                int o = Opener(t, j);
                if (o < 0 || o >= i) return false;
                if (o > 0)
                {
                    Tok p = t[o - 1];
                    if (p.Kind == Ident && !Before.Contains(p.Text)) return false;
                    if (p.Kind == Punct && (p.Text == ")" || p.Text == "]" || p.Text == "?.")) return false;
                    if (p.Kind == Str || p.Kind == Num || p.Kind == Rx || (p.Kind == Tmpl && p.Closes)) return false;
                }
                grouped = true;
                j++;
            }
            if (!grouped) return false;
            if (j < t.Count && t[j].Kind == Punct && t[j].Text == "?.") j++;
            return j < t.Count && ((t[j].Kind == Punct && t[j].Text == "(") || (t[j].Kind == Tmpl && t[j].Opens));
        }

        static bool Assigned(List<Tok> t, int i)
        {
            return i + 1 < t.Count && t[i + 1].Kind == Punct && AssignOps.Contains(t[i + 1].Text);
        }

        static bool PrefixUpdate(List<Tok> t, int i)
        {
            if (i + 1 < t.Count && t[i + 1].Kind == Punct && (t[i + 1].Text == "." || t[i + 1].Text == "?." || t[i + 1].Text == "[" || t[i + 1].Text == "(")) return false;
            int s = ChainStart(t, i);
            return s > 0 && t[s - 1].Kind == Punct && (t[s - 1].Text == "++" || t[s - 1].Text == "--");
        }

        static bool IsKey(List<Tok> t, int i)
        {
            if (!Next(t, i, ":") || i == 0) return false;
            Tok p = t[i - 1];
            return p.Kind == Punct && (p.Text == "{" || p.Text == ",");
        }

        static int Close(List<Tok> t, int open)
        {
            string o = t[open].Text, c = o == "(" ? ")" : o == "[" ? "]" : "}";
            int depth = 0;
            for (int j = open; j < t.Count; j++)
            {
                if (t[j].Kind != Punct) continue;
                if (t[j].Text == o) depth++;
                else if (t[j].Text == c && --depth == 0) return j;
            }
            return t.Count;
        }

        static int Opener(List<Tok> t, int close)
        {
            string c = t[close].Text, o = c == ")" ? "(" : c == "]" ? "[" : "{";
            int depth = 0;
            for (int j = close; j >= 0; j--)
            {
                if (t[j].Kind != Punct) continue;
                if (t[j].Text == c) depth++;
                else if (t[j].Text == o && --depth == 0) return j;
            }
            return -1;
        }

        static int ChainStart(List<Tok> t, int i)
        {
            int start = i, j = i - 1;
            while (j >= 0 && t[j].Kind == Punct && (t[j].Text == "." || t[j].Text == "?."))
            {
                j--;
                bool part = false;
                while (j >= 0 && t[j].Kind == Punct && (t[j].Text == ")" || t[j].Text == "]"))
                {
                    int o = Opener(t, j);
                    if (o < 0) return start;
                    start = o;
                    part = true;
                    j = o - 1;
                    if (j >= 0 && t[j].Kind == Punct && t[j].Text == "?.") j--;
                }
                if (j >= 0 && (t[j].Kind == Ident || t[j].Kind == Str || t[j].Kind == Num || (t[j].Kind == Tmpl && t[j].Opens)))
                {
                    if (t[j].Kind == Ident && Before.Contains(t[j].Text)) break;
                    start = j;
                    j--;
                }
                else if (!part) break;
            }
            return start;
        }

        static bool ChainMatches(List<Tok> t, int i, Regex rx, bool strings)
        {
            for (int j = ChainStart(t, i); j < i; j++)
            {
                Tok x = t[j];
                if (x.Kind == Ident && rx.IsMatch(x.Text)) return true;
                if (strings && (x.Kind == Str || x.Kind == Tmpl) && rx.IsMatch(x.Text)) return true;
            }
            return false;
        }

        static int CallOpen(List<Tok> t, int i)
        {
            int j = i + 1;
            if (j < t.Count && t[j].Text == "?.") j++;
            return j < t.Count && t[j].Kind == Punct && t[j].Text == "(" ? j : -1;
        }

        static bool EmptyCall(List<Tok> t, int i)
        {
            int o = CallOpen(t, i);
            return o >= 0 && o + 1 < t.Count && t[o + 1].Kind == Punct && t[o + 1].Text == ")";
        }

        static List<KeyValuePair<int, int>> ArgSpans(List<Tok> t, int i)
        {
            List<KeyValuePair<int, int>> spans = new List<KeyValuePair<int, int>>();
            int o = CallOpen(t, i);
            if (o < 0) return spans;
            int end = Close(t, o), start = o + 1, depth = 0;
            for (int j = o + 1; j < end; j++)
            {
                Tok x = t[j];
                if (x.Kind != Punct) continue;
                if (x.Text == "(" || x.Text == "[" || x.Text == "{") depth++;
                else if (x.Text == ")" || x.Text == "]" || x.Text == "}") depth--;
                else if (x.Text == "," && depth == 0) { spans.Add(new KeyValuePair<int, int>(start, j)); start = j + 1; }
            }
            if (end > start) spans.Add(new KeyValuePair<int, int>(start, end));
            return spans;
        }

        static int ArgCount(List<Tok> t, int i) { return ArgSpans(t, i).Count; }

        static bool Arg(List<Tok> t, int i, int index, out string value)
        {
            value = null;
            List<KeyValuePair<int, int>> spans = ArgSpans(t, i);
            if (index >= spans.Count) return true;
            KeyValuePair<int, int> s = spans[index];
            if (s.Value - s.Key != 1) return false;
            Tok x = t[s.Key];
            if (x.Kind == Str || (x.Kind == Tmpl && x.Opens && x.Closes)) { value = x.Text; return true; }
            return false;
        }

        static bool ArgStarts(List<Tok> t, int i, int index, string text)
        {
            List<KeyValuePair<int, int>> spans = ArgSpans(t, i);
            return index < spans.Count && t[spans[index].Key].Kind == Punct && t[spans[index].Key].Text == text;
        }

        static bool ArgIsBool(List<Tok> t, int i, int index)
        {
            List<KeyValuePair<int, int>> spans = ArgSpans(t, i);
            if (index >= spans.Count || spans[index].Value - spans[index].Key != 1) return false;
            Tok x = t[spans[index].Key];
            return x.Kind == Ident && (x.Text == "true" || x.Text == "false");
        }

        static bool ArgIsString(List<Tok> t, int i, int index)
        {
            List<KeyValuePair<int, int>> spans = ArgSpans(t, i);
            if (index >= spans.Count) return false;
            Tok x = t[spans[index].Key];
            return x.Kind == Str || x.Kind == Tmpl;
        }

        static string ArgObject(List<Tok> t, int i, int index, Facts f)
        {
            List<KeyValuePair<int, int>> spans = ArgSpans(t, i);
            if (index >= spans.Count) return null;
            int end;
            string c = Chain(t, spans[index].Key, f, out end);
            return end == spans[index].Value ? c : null;
        }

        static bool Ends(List<Tok> t, int i)
        {
            return i + 1 >= t.Count || t[i + 1].Kind == Punct && (t[i + 1].Text == ";" || t[i + 1].Text == "," || t[i + 1].Text == ")" || t[i + 1].Text == "}" || t[i + 1].Text == "]");
        }

        static bool StoreTarget(List<Tok> t, int at, out string name, out bool member)
        {
            name = null;
            member = false;
            if (at < 1 || t[at].Kind != Punct || (t[at].Text != "=" && t[at].Text != ":")) return false;
            Tok target = t[at - 1];
            if (target.Kind != Ident && !(t[at].Text == ":" && target.Kind == Str)) return false;
            name = target.Text;
            member = t[at].Text == ":" || IsMember(t, at - 1);
            return true;
        }

        static bool StoredThenCalled(List<Tok> t, int close)
        {
            if (!Ends(t, close)) return false;
            int j = Opener(t, close) - 1;
            while (j >= 0)
            {
                Tok x = t[j];
                if (x.Kind == Ident || x.Kind == Punct && (x.Text == "." || x.Text == "?.")) { j--; continue; }
                if (x.Kind == Punct && (x.Text == ")" || x.Text == "]")) { int o = Opener(t, j); if (o < 0) return false; j = o - 1; continue; }
                break;
            }
            string name;
            bool member, listed = false;
            if (j >= 0 && t[j].Kind == Punct && (t[j].Text == "[" || t[j].Text == ","))
            {
                int depth = 0, open = -1;
                for (int k = j; k >= 0 && open < 0; k--)
                {
                    if (t[k].Kind != Punct) continue;
                    string x = t[k].Text;
                    if (x == "]" || x == ")" || x == "}") depth++;
                    else if (x == "[" || x == "(" || x == "{") { if (depth == 0) open = k; else depth--; }
                }
                if (open < 0 || t[open].Text != "[" || (open > 0 && (t[open - 1].Kind == Ident || t[open - 1].Kind == Punct && (t[open - 1].Text == ")" || t[open - 1].Text == "]")))) return false;
                j = open - 1;
                listed = true;
            }
            if (!StoreTarget(t, j, out name, out member)) return false;
            if (listed)
            {
                for (int c = 0; c + 1 < t.Count; c++)
                {
                    if (t[c].Text != name || !(t[c].Kind == Ident || t[c].Kind == Str) || (member ? !IsMember(t, c) : IsMember(t, c)) || !Next(t, c, "[")) continue;
                    int e = Close(t, c + 1);
                    if (e + 1 < t.Count && t[e + 1].Kind == Punct && (t[e + 1].Text == "(" || t[e + 1].Text == "?.")) return true;
                }
                return false;
            }
            List<KeyValuePair<string, bool>> held = new List<KeyValuePair<string, bool>>();
            held.Add(new KeyValuePair<string, bool>(name, member));
            for (int round = 0; round < 4; round++)
            {
                int had = held.Count;
                for (int c = 1; c < t.Count; c++)
                {
                    if (t[c].Kind != Ident || IsMember(t, c) || !Ends(t, c)) continue;
                    bool holds = false;
                    foreach (KeyValuePair<string, bool> h in held) if (!h.Value && h.Key == t[c].Text) holds = true;
                    string n2;
                    bool m2;
                    if (holds && StoreTarget(t, c - 1, out n2, out m2) && !held.Exists(delegate (KeyValuePair<string, bool> h) { return h.Key == n2 && h.Value == m2; }))
                        held.Add(new KeyValuePair<string, bool>(n2, m2));
                }
                if (held.Count == had) break;
            }
            for (int c = 0; c < t.Count; c++)
            {
                if (!(t[c].Kind == Ident || t[c].Kind == Str) || !(Called(t, c) || GroupCalled(t, c))) continue;
                foreach (KeyValuePair<string, bool> h in held)
                    if (h.Key == t[c].Text && (h.Value ? IsMember(t, c) : !IsMember(t, c))) return true;
            }
            return false;
        }

        static bool ComputedPattern(List<Tok> t, int open)
        {
            int close = Close(t, open);
            if (close + 1 >= t.Count || t[close + 1].Kind != Punct || t[close + 1].Text != ":") return false;
            int depth = 0, brace = -1;
            for (int k = open - 1; k >= 0 && brace < 0; k--)
            {
                if (t[k].Kind != Punct) continue;
                string x = t[k].Text;
                if (x == "]" || x == ")" || x == "}") depth++;
                else if (x == "[" || x == "(" || x == "{") { if (depth == 0) brace = k; else depth--; }
            }
            if (brace < 0 || t[brace].Text != "{") return false;
            int end = Close(t, brace);
            return end + 1 < t.Count && t[end + 1].Kind == Punct && t[end + 1].Text == "=";
        }

        static bool NumberIndex(List<Tok> t, int close)
        {
            int o = Opener(t, close);
            return o >= 0 && close - o == 2 && t[o + 1].Kind == Num;
        }

        static bool Definition(List<Tok> t, int i)
        {
            if (i > 0 && t[i - 1].Kind == Ident && t[i - 1].Text == "function") return true;
            if (i > 1 && t[i - 1].Kind == Punct && t[i - 1].Text == "*" && t[i - 2].Kind == Ident && t[i - 2].Text == "function") return true;
            if (i == 0 || IsMember(t, i) || !Next(t, i, "(")) return false;
            int o = i + 1, c = Close(t, o);
            if (c + 1 >= t.Count || t[c + 1].Kind != Punct || t[c + 1].Text != "{") return false;
            for (int j = o + 1; j < c; j++) if (t[j].Kind != Ident && t[j].Kind != Punct) return false;
            Tok p = t[i - 1];
            bool lead = (p.Kind == Punct && (p.Text == "{" || p.Text == "," || p.Text == ";" || p.Text == "}" || p.Text == "*")) ||
                        (p.Kind == Ident && (p.Text == "get" || p.Text == "set" || p.Text == "static" || p.Text == "async"));
            return lead && InBody(t, i);
        }

        static bool InBody(List<Tok> t, int i)
        {
            int depth = 0;
            for (int j = i - 1; j >= 0; j--)
            {
                if (t[j].Kind != Punct) continue;
                if (t[j].Text == "}") depth++;
                else if (t[j].Text == "{")
                {
                    if (depth == 0) return ClassOrObject(t, j);
                    depth--;
                }
            }
            return false;
        }

        static bool ClassOrObject(List<Tok> t, int open)
        {
            if (open == 0) return false;
            Tok p = t[open - 1];
            if (p.Kind == Punct && (p.Text == "=" || p.Text == "(" || p.Text == "," || p.Text == ":" || p.Text == "[" || p.Text == "?" || p.Text == "||" || p.Text == "&&" || p.Text == "??" || p.Text == "...")) return true;
            if (p.Kind == Ident && (p.Text == "return" || p.Text == "yield" || p.Text == "await")) return true;
            for (int j = open - 1; j >= 0 && j >= open - 24; j--)
            {
                Tok x = t[j];
                if (x.Kind == Ident && x.Text == "class") return true;
                if (x.Kind == Punct && (x.Text == ";" || x.Text == "{" || x.Text == "}")) break;
            }
            return false;
        }

        static bool FunctionArg(List<Tok> t, int i, Facts f)
        {
            int o = CallOpen(t, i);
            if (o < 0 || o + 1 >= t.Count) return false;
            if (FunctionAt(t, o + 1)) return true;
            Tok a = t[o + 1];
            bool ends = o + 2 < t.Count && t[o + 2].Kind == Punct && (t[o + 2].Text == "," || t[o + 2].Text == ")");
            if (a.Kind == Ident && ends) return f.Callback(a.Text);
            return a.Kind == Ident && a.Text == "console" && o + 4 < t.Count && t[o + 2].Text == "." && t[o + 3].Kind == Ident &&
                   t[o + 4].Kind == Punct && (t[o + 4].Text == "," || t[o + 4].Text == ")");
        }

        static bool FunctionAt(List<Tok> t, int s)
        {
            if (s >= t.Count) return false;
            if (t[s].Kind == Ident && t[s].Text == "async") s++;
            if (s >= t.Count) return false;
            Tok x = t[s];
            if (x.Kind == Ident && x.Text == "function") return true;
            if (x.Kind == Ident && Next(t, s, "=>")) return true;
            if (x.Kind == Punct && x.Text == "(")
            {
                int c = Close(t, s);
                return c + 1 < t.Count && t[c + 1].Kind == Punct && t[c + 1].Text == "=>";
            }
            return false;
        }

        static Facts Study(List<Tok> t)
        {
            Facts f = new Facts();
            f.Targets = new bool[t.Count];
            f.Sites = new bool[t.Count];
            for (int i = 0; i < t.Count; i++)
            {
                Tok x = t[i];
                if (x.Kind == Str || x.Kind == Tmpl)
                {
                    if (EditableWord.IsMatch(x.Text)) f.Editable = true;
                    continue;
                }
                if (x.Kind == Punct)
                {
                    if (x.Text == "(")
                    {
                        int c = Close(t, i);
                        if (c + 1 < t.Count && t[c + 1].Kind == Punct && t[c + 1].Text == "=>") Params(t, i, c, f, PromiseExecutor(t, i));
                    }
                    else if (x.Text == "=" && i > 0 && t[i - 1].Kind == Punct && (t[i - 1].Text == "]" || t[i - 1].Text == "}"))
                    {
                        int o = Opener(t, i - 1);
                        if (o >= 0)
                        {
                            bool decl = o > 0 && t[o - 1].Kind == Ident && (t[o - 1].Text == "const" || t[o - 1].Text == "let" || t[o - 1].Text == "var");
                            Pattern(t, o, i - 1, f, decl);
                            if (t[o].Text == "{") FromWindow(t, o, i - 1, i + 1, f);
                        }
                    }
                    else if ((x.Text == "++" || x.Text == "--") && i + 1 < t.Count && t[i + 1].Kind == Ident) f.Tainted.Add(t[i + 1].Text);
                    continue;
                }
                if (x.Kind != Ident) continue;
                string n = x.Text;
                if (EditableWord.IsMatch(n)) f.Editable = true;
                if (FieldProps.Contains(n) && IsKey(t, i)) f.FieldKeys = true;
                if ((n == "const" || n == "let" || n == "var") && i + 1 < t.Count && t[i + 1].Kind == Ident) { Declare(f, t, i + 1); continue; }
                if (n == "function" || n == "class")
                {
                    int j = i + 1;
                    if (j < t.Count && t[j].Kind == Punct && t[j].Text == "*") j++;
                    if (j < t.Count && t[j].Kind == Ident && t[j].Text != "extends")
                    {
                        Declare(f, t, j);
                        if (n == "function") f.Functions.Add(t[j].Text);
                        j++;
                    }
                    if (n == "function" && j < t.Count && t[j].Kind == Punct && t[j].Text == "(") Params(t, j, Close(t, j), f, PromiseExecutor(t, i));
                    continue;
                }
                if (n == "catch" && Next(t, i, "(")) { Params(t, i + 1, Close(t, i + 1), f, false); continue; }
                if (n == "for" && Next(t, i, "(")) { ForHead(t, i + 1, f); continue; }
                if (Next(t, i, "=>"))
                {
                    Declare(f, t, i);
                    if (PromiseExecutor(t, i)) f.PromiseParams.Add(n);
                    continue;
                }
                if (!IsMember(t, i) && i + 1 < t.Count && t[i + 1].Kind == Punct && AssignOps.Contains(t[i + 1].Text))
                {
                    if (t[i + 1].Text == "=" && FunctionAt(t, i + 2)) f.Functions.Add(n);
                    else f.Tainted.Add(n);
                    if (t[i + 1].Text == "=")
                    {
                        int end;
                        string c = Chain(t, i + 2, f, out end);
                        if (c != null) f.Alias[n] = c;
                    }
                }
            }
            return f;
        }

        static void Declare(Facts f, List<Tok> t, int j)
        {
            f.Declared.Add(t[j].Text);
            f.Sites[j] = true;
        }

        static bool Binding(List<Tok> t, int j)
        {
            Tok p = j > 0 ? t[j - 1] : null, q = j + 1 < t.Count ? t[j + 1] : null;
            bool before = p != null && p.Kind == Punct && (p.Text == "(" || p.Text == "," || p.Text == "..." || p.Text == "{" || p.Text == "[" || p.Text == ":");
            bool after = q == null || (q.Kind == Punct && (q.Text == "," || q.Text == ")" || q.Text == "=" || q.Text == "}" || q.Text == "]"));
            return before && after;
        }

        static void Params(List<Tok> t, int open, int close, Facts f, bool promise)
        {
            for (int j = open + 1; j < close && j < t.Count; j++)
                if (t[j].Kind == Ident && !Next(t, j, ":") && Binding(t, j))
                {
                    Declare(f, t, j);
                    if (promise) f.PromiseParams.Add(t[j].Text);
                }
        }

        static void Pattern(List<Tok> t, int open, int close, Facts f, bool decl)
        {
            for (int j = open + 1; j < close && j < t.Count; j++)
            {
                if (t[j].Kind != Ident || Next(t, j, ":")) continue;
                if (decl)
                {
                    if (Binding(t, j)) { Declare(f, t, j); f.Tainted.Add(t[j].Text); }
                    continue;
                }
                if (j > 0 && t[j - 1].Kind == Punct && t[j - 1].Text == "=") continue;
                f.Targets[j] = true;
                if (!IsMember(t, j)) f.Tainted.Add(t[j].Text);
            }
        }

        static void ForHead(List<Tok> t, int open, Facts f)
        {
            int close = Close(t, open), depth = 0, cut = -1;
            for (int j = open + 1; j < close && cut < 0; j++)
            {
                Tok x = t[j];
                if (x.Kind == Punct)
                {
                    if (x.Text == "(" || x.Text == "[" || x.Text == "{") depth++;
                    else if (x.Text == ")" || x.Text == "]" || x.Text == "}") depth--;
                    else if (x.Text == ";" && depth == 0) return;
                }
                else if (depth == 0 && x.Kind == Ident && (x.Text == "of" || x.Text == "in")) cut = j;
            }
            if (cut < 0) return;
            bool decl = t[open + 1].Kind == Ident && (t[open + 1].Text == "const" || t[open + 1].Text == "let" || t[open + 1].Text == "var");
            int from = decl ? open + 1 : open;
            if (decl && from + 1 < cut && t[from + 1].Kind == Ident)
            {
                Declare(f, t, from + 1);
                f.Tainted.Add(t[from + 1].Text);
                return;
            }
            Pattern(t, from, cut, f, decl);
        }

        static bool PromiseExecutor(List<Tok> t, int s)
        {
            int j = s - 1;
            if (j >= 0 && t[j].Kind == Ident && t[j].Text == "async") j--;
            return j >= 2 && t[j].Kind == Punct && t[j].Text == "(" && t[j - 1].Kind == Ident && t[j - 1].Text == "Promise" && t[j - 2].Kind == Ident && t[j - 2].Text == "new";
        }

        static void FromWindow(List<Tok> t, int open, int close, int source, Facts f)
        {
            int end;
            if (Chain(t, source, f, out end) != "window") return;
            int depth = 0;
            for (int j = open + 1; j < close; j++)
            {
                Tok x = t[j];
                if (x.Kind == Punct)
                {
                    if (x.Text == "{" || x.Text == "[" || x.Text == "(") depth++;
                    else if (x.Text == "}" || x.Text == "]" || x.Text == ")") depth--;
                    continue;
                }
                if (depth != 0 || x.Kind != Ident) continue;
                string key = x.Text, name = null;
                if (Next(t, j, ":")) { if (j + 2 < close && t[j + 2].Kind == Ident) name = t[j + 2].Text; }
                else if (Binding(t, j)) name = key;
                if (name == null) continue;
                string o;
                if (Objects.TryGetValue(key, out o)) f.Alias[name] = o;
                else if (WindowOps.Contains(key)) f.Alias[name] = key;
            }
        }

        static string Chain(List<Tok> t, int s, Facts f, out int end)
        {
            end = s;
            if (s >= t.Count || t[s].Kind != Ident) return null;
            List<string> names = new List<string>();
            names.Add(t[s].Text);
            int j = s + 1;
            while (j + 1 < t.Count && t[j].Kind == Punct && (t[j].Text == "." || t[j].Text == "?.") && t[j + 1].Kind == Ident)
            {
                names.Add(t[j + 1].Text);
                j += 2;
            }
            end = j;
            if (j < t.Count && ((t[j].Kind == Punct && (t[j].Text == "(" || t[j].Text == "[" || t[j].Text == "?.")) || (t[j].Kind == Tmpl && t[j].Opens))) return null;
            string head = names[0], cur, o;
            if (f.Alias.TryGetValue(head, out cur)) { }
            else if (Objects.TryGetValue(head, out o)) cur = o;
            else if (names.Count == 1 && WindowOps.Contains(head) && !f.Own(head)) return head;
            else cur = null;
            for (int k = 1; k < names.Count; k++)
            {
                string nm = names[k];
                if (cur == "window" && Objects.TryGetValue(nm, out o)) cur = o;
                else if (cur == "document" && nm == "location") cur = "location";
                else if (nm == "contentDocument" || nm == "ownerDocument") cur = "document";
                else if (nm == "contentWindow") cur = "window";
                else if (cur == "window" && k == names.Count - 1 && WindowOps.Contains(nm)) return nm;
                else cur = null;
            }
            return Objects.ContainsValue(cur ?? "") || WindowOps.Contains(cur ?? "") ? cur : null;
        }

        static List<Tok> Fold(List<Tok> t)
        {
            List<Tok> r = new List<Tok>(t.Count);
            for (int i = 0; i < t.Count; i++)
            {
                Tok x = t[i];
                bool open = x.Kind == Punct && x.Text == "[" && i + 2 < t.Count;
                if (open)
                {
                    Tok key = t[i + 1], close = t[i + 2];
                    bool literal = key.Kind == Str || (key.Kind == Tmpl && key.Opens && key.Closes);
                    if (literal && close.Kind == Punct && close.Text == "]" && r.Count > 0 && MemberBase(r[r.Count - 1]))
                    {
                        if (!(r[r.Count - 1].Kind == Punct && r[r.Count - 1].Text == "?.")) r.Add(P("."));
                        Tok id = new Tok();
                        id.Kind = Ident;
                        id.Text = key.Text;
                        r.Add(id);
                        i += 2;
                        continue;
                    }
                }
                r.Add(x);
            }
            return r;
        }

        static bool MemberBase(Tok p)
        {
            if (p.Kind == Ident) return !Before.Contains(p.Text);
            if (p.Kind == Str || p.Kind == Num) return true;
            if (p.Kind == Tmpl) return p.Closes;
            return p.Kind == Punct && (p.Text == ")" || p.Text == "]" || p.Text == "?.");
        }

        static Tok P(string s)
        {
            Tok x = new Tok();
            x.Kind = Punct;
            x.Text = s;
            return x;
        }

        static void Add(List<Tok> l, int kind, string text)
        {
            Tok x = new Tok();
            x.Kind = kind;
            x.Text = text;
            l.Add(x);
        }

        static bool RegexAllowed(List<Tok> l, bool flip, ref bool ambiguous)
        {
            if (l.Count == 0) return true;
            Tok p = l[l.Count - 1];
            if (p.Kind == Num || p.Kind == Str || p.Kind == Rx) return false;
            if (p.Kind == Tmpl) return !p.Closes;
            if (p.Kind == Ident) return Before.Contains(p.Text);
            if (p.Text == ")") { ambiguous = true; return flip; }
            if (p.Text == "}") { ambiguous = true; return !flip; }
            return p.Text != "]" && p.Text != "++" && p.Text != "--";
        }

        static bool IdStart(char c) { return c == '$' || c == '_' || c == '#' || c == '\\' || char.IsLetter(c); }

        static bool IdPart(char c)
        {
            if (c == '$' || c == '_' || c == '\\' || c == '\u200c' || c == '\u200d' || char.IsLetterOrDigit(c)) return true;
            UnicodeCategory k = char.GetUnicodeCategory(c);
            return k == UnicodeCategory.NonSpacingMark || k == UnicodeCategory.SpacingCombiningMark || k == UnicodeCategory.ConnectorPunctuation;
        }

        static bool Hex(char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'); }

        static bool Digits(char c) { return (c >= '0' && c <= '9') || c == '_'; }

        static int Number(string s, int i)
        {
            int n = s.Length;
            if (s[i] == '0' && i + 1 < n && "xXoObB".IndexOf(s[i + 1]) >= 0)
            {
                i += 2;
                while (i < n && (Hex(s[i]) || s[i] == '_')) i++;
            }
            else
            {
                while (i < n && Digits(s[i])) i++;
                if (i < n && s[i] == '.')
                {
                    i++;
                    while (i < n && Digits(s[i])) i++;
                }
                if (i < n && (s[i] == 'e' || s[i] == 'E'))
                {
                    int j = i + 1;
                    if (j < n && (s[j] == '+' || s[j] == '-')) j++;
                    if (j < n && s[j] >= '0' && s[j] <= '9')
                    {
                        i = j;
                        while (i < n && Digits(s[i])) i++;
                    }
                }
            }
            if (i < n && s[i] == 'n') i++;
            return i;
        }

        static int Escape(string s, int i, StringBuilder sb, ref string hidden)
        {
            int n = s.Length;
            if (i + 1 >= n) return n;
            char e = s[i + 1];
            int code = -1, next = i + 2;
            if (e == 'x' && i + 3 < n && Hex(s[i + 2]) && Hex(s[i + 3]))
            {
                code = Convert.ToInt32(s.Substring(i + 2, 2), 16);
                next = i + 4;
            }
            else if (e == 'u' && i + 2 < n && s[i + 2] == '{')
            {
                int close = s.IndexOf('}', i + 3);
                if (close > i + 3 && close - (i + 3) <= 6)
                {
                    bool ok = true;
                    for (int j = i + 3; j < close; j++) if (!Hex(s[j])) ok = false;
                    if (ok) { code = Convert.ToInt32(s.Substring(i + 3, close - i - 3), 16); next = close + 1; }
                }
            }
            else if (e == 'u' && i + 5 < n && Hex(s[i + 2]) && Hex(s[i + 3]) && Hex(s[i + 4]) && Hex(s[i + 5]))
            {
                code = Convert.ToInt32(s.Substring(i + 2, 4), 16);
                next = i + 6;
            }
            else if (e >= '1' && e <= '7' || (e == '0' && i + 2 < n && s[i + 2] >= '0' && s[i + 2] <= '7'))
            {
                if (hidden == null) hidden = "an octal escape";
                return i + 2;
            }
            if (code >= 0)
            {
                if (code >= 0x20 && code <= 0x7E && hidden == null) hidden = "escaped text such as \\" + s.Substring(i + 1, next - i - 1);
                if (code <= 0xFFFF) sb.Append((char)code);
                else if (code <= 0x10FFFF) sb.Append(char.ConvertFromUtf32(code));
                return next;
            }
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'v': sb.Append('\v'); break;
                case '0': sb.Append('\0'); break;
                case '\r': if (i + 2 < n && s[i + 2] == '\n') return i + 3; break;
                case '\n': case '\u2028': case '\u2029': break;
                default: sb.Append(e); break;
            }
            return i + 2;
        }

        static int Template(string s, int i, List<Tok> l, List<int> open, int depth, bool first, ref string hidden)
        {
            StringBuilder sb = new StringBuilder();
            int n = s.Length;
            while (i < n)
            {
                char c = s[i];
                if (c == '\\') { i = Escape(s, i, sb, ref hidden); continue; }
                if (c == '`' || (c == '$' && i + 1 < n && s[i + 1] == '{'))
                {
                    Tok x = new Tok();
                    x.Kind = Tmpl;
                    x.Text = sb.ToString();
                    x.Opens = first;
                    x.Closes = c == '`';
                    l.Add(x);
                    if (c == '`') return i + 1;
                    open.Add(depth);
                    return i + 2;
                }
                sb.Append(c);
                i++;
            }
            Tok end = new Tok();
            end.Kind = Tmpl;
            end.Text = sb.ToString();
            end.Opens = first;
            end.Closes = true;
            l.Add(end);
            return n;
        }

        static List<Tok> Lex(string s, bool flip, out string hidden, out bool ambiguous)
        {
            hidden = null;
            ambiguous = false;
            List<Tok> l = new List<Tok>();
            List<int> open = new List<int>();
            int depth = 0, i = 0, n = s.Length;
            while (i < n)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == '\ufeff') { i++; continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '/') { while (i < n && s[i] != '\n' && s[i] != '\r' && s[i] != '\u2028' && s[i] != '\u2029') i++; continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? n : e + 2; continue; }
                if (c == '`') { i = Template(s, i + 1, l, open, depth, true, ref hidden); continue; }
                if (c == '}' && open.Count > 0 && open[open.Count - 1] == depth)
                {
                    open.RemoveAt(open.Count - 1);
                    i = Template(s, i + 1, l, open, depth, false, ref hidden);
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    StringBuilder sb = new StringBuilder();
                    i++;
                    while (i < n && s[i] != c && s[i] != '\n')
                    {
                        if (s[i] == '\\') { i = Escape(s, i, sb, ref hidden); continue; }
                        sb.Append(s[i]);
                        i++;
                    }
                    if (i < n && s[i] == c) i++;
                    Add(l, Str, sb.ToString());
                    continue;
                }
                if ((c >= '0' && c <= '9') || (c == '.' && i + 1 < n && s[i + 1] >= '0' && s[i + 1] <= '9'))
                {
                    int st = i;
                    i = Number(s, i);
                    Add(l, Num, s.Substring(st, i - st));
                    continue;
                }
                if (IdStart(c))
                {
                    int st = i;
                    i++;
                    while (i < n && IdPart(s[i])) i++;
                    string id = s.Substring(st, i - st);
                    if (id.IndexOf("\\u", StringComparison.Ordinal) >= 0 && hidden == null) hidden = "an escaped name";
                    Add(l, Ident, id);
                    continue;
                }
                if (c == '/' && RegexAllowed(l, flip, ref ambiguous))
                {
                    int j = i + 1;
                    bool cls = false, ok = false;
                    while (j < n && s[j] != '\n')
                    {
                        char d = s[j];
                        if (d == '\\') { j += 2; continue; }
                        if (d == '[') cls = true;
                        else if (d == ']') cls = false;
                        else if (d == '/' && !cls) { ok = true; break; }
                        j++;
                    }
                    if (ok)
                    {
                        j++;
                        while (j < n && char.IsLetter(s[j])) j++;
                        Add(l, Rx, s.Substring(i, j - i));
                        i = j;
                        continue;
                    }
                }
                string p = null;
                foreach (string q in Puncts)
                    if (string.CompareOrdinal(s, i, q, 0, q.Length) == 0) { p = q; break; }
                if (p == "?." && i + 2 < n && char.IsDigit(s[i + 2])) p = null;
                if (p == null) p = c.ToString();
                if (p == "{") depth++;
                else if (p == "}") depth--;
                Add(l, Punct, p);
                i += p.Length;
            }
            return l;
        }
    }
}
