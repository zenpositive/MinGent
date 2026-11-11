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

    static partial class ConsoleCheck
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
        static readonly Regex OuterUrl = new Regex(@"url\(\s*(?:['""]\s*)?(?:https?:|//)", RegexOptions.CultureInvariant);
        static readonly Regex FormWord = new Regex(@"(?:^|[^a-z])form|Form", RegexOptions.CultureInvariant);
        static readonly Regex WidgetWord = new Regex(@"modal|Modal|dialog|Dialog|Dropdown|dropdown|Collapse|Offcanvas|offcanvas|Popover|popover|Tooltip|tooltip|Toast", RegexOptions.CultureInvariant);
        static readonly Regex EditableWord = new Regex(@"contenteditable|textarea|textbox|activeelement|iscontenteditable", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex CaptchaTerm = new Regex(@"captcha|turnstile|arkose|geetest|perimeterx|datadome|awswaf|yidun|cf[-_]chl|challenges\.cloudflare\.com", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static string CaptchaWord(string code)
        {
            string s = code ?? "";
            Match m = CaptchaTerm.Match(s);
            if (!m.Success) return null;
            int a = m.Index, b = m.Index + m.Length;
            while (a > 0 && a > m.Index - 30 && WordChar(s[a - 1])) a--;
            while (b < s.Length && b < m.Index + m.Length + 30 && WordChar(s[b])) b++;
            return s.Substring(a, b - a).Trim('.', '-');
        }

        static bool WordChar(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '.' || c == '-'; }

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
            if (i > 0 && t[i - 1].Kind == Ident && t[i - 1].Text == "function" && !IsMember(t, i - 1)) return true;
            if (i > 1 && t[i - 1].Kind == Punct && t[i - 1].Text == "*" && t[i - 2].Kind == Ident && t[i - 2].Text == "function" && !IsMember(t, i - 2)) return true;
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
                if ((n == "const" || n == "let" || n == "var") && i + 1 < t.Count && t[i + 1].Kind == Ident && !IsMember(t, i)) { Declare(f, t, i + 1); continue; }
                if ((n == "function" || n == "class") && !IsMember(t, i))
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

    class ConsoleReading
    {
        public bool Requests, Unwritten, Here, Messages, CookiesRead, CookiesChanged, StoreRead, StoreChanged, SignIns, Fields, Changes, Lingers, Looks, Failed;
        public readonly List<string> Hosts = new List<string>();
        public readonly List<string> Calls = new List<string>();

        public static string Clip(string name)
        {
            string n = (name ?? "").Trim();
            return n.Length > 40 ? n.Substring(0, 39) + "\u2026" : n;
        }

        public void Call(string name)
        {
            string n = Clip(name);
            if (n.Length > 0 && !Calls.Contains(n)) Calls.Add(n);
        }

        public void Host(string host)
        {
            string h = (host ?? "").Trim();
            if (h.Length == 0) { Unwritten = true; return; }
            if (h.Length > 60) h = h.Substring(0, 59) + "\u2026";
            if (!Hosts.Contains(h)) Hosts.Add(h);
        }

        static string Some(List<string> items)
        {
            int shown = Math.Min(3, items.Count), more = items.Count - shown;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(i == shown - 1 && more == 0 ? " and " : ", ");
                sb.Append(items[i]);
            }
            if (more > 0) sb.Append(" and ").Append(more.ToString(CultureInfo.InvariantCulture)).Append(" more");
            return sb.ToString();
        }

        public string Words()
        {
            if (Failed) return "";
            List<string> p = new List<string>();
            if (CookiesRead || CookiesChanged) p.Add(CookiesRead && CookiesChanged ? "reads and changes cookies" : CookiesChanged ? "changes cookies" : "reads cookies");
            if (StoreRead || StoreChanged) p.Add((StoreRead && StoreChanged ? "reads and changes" : StoreChanged ? "changes" : "reads") + " data the site stored in the browser");
            if (SignIns) p.Add("reads saved sign-ins");
            if (Fields) p.Add("reads form fields");
            if (Requests)
            {
                if (Hosts.Count > 0) p.Add("sends requests to " + Some(Hosts) + (Unwritten ? " and other addresses" : ""));
                else p.Add(Unwritten ? "sends network requests" : "sends requests to this site");
            }
            if (Messages) p.Add("sends messages to other windows or frames");
            if (Changes) p.Add("changes the page");
            if (Calls.Count > 0) p.Add("calls the page's own functions (" + Some(Calls) + ")");
            if (Lingers) p.Add("leaves code running in the page");
            if (p.Count == 0) return Looks ? "reads the page" : "doesn't touch the page";
            if (p.Count == 1) return p[0];
            return string.Join(", ", p.GetRange(0, p.Count - 1).ToArray()) + " and " + p[p.Count - 1];
        }
    }

    static class ConsoleWords
    {
        static readonly Regex GenericWords = new Regex(@"^(please\s+)?((run|runs|running|execute|executes|executing|evaluate|evaluates|evaluating|eval|do|use|try)\s+)?((a|an|the|this|that|some|my|following)\s+)?((js|javascript|console|browser|page|debug)\s+)?(code|script|snippet|expression|command|javascript|js|it|console)?(\s+(in|on|inside|from|with|using|into)\s+(the\s+)?(page|console|browser|devtools|developer tools|site|website|tab)(\s+console)?)*\s*[.!:]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool Generic(string description, string code)
        {
            string d = Regex.Replace(description ?? "", @"\s+", " ").Trim();
            if (d.Length < 3 || GenericWords.IsMatch(d)) return true;
            string c = Regex.Replace(code ?? "", @"\s+", " ").Trim();
            return c.Length > 0 && string.Equals(d.TrimEnd('.', ';', ' '), c.TrimEnd(';', ' '), StringComparison.OrdinalIgnoreCase);
        }

        public static void Explain(PermissionAsk ask, string description, string code, string pageUrl)
        {
            string d = PermissionAsk.PlainSummary(description, 160);
            string words = PermissionAsk.PlainSummary(ConsoleCheck.Read(code, pageUrl).Words(), 400);
            if (Generic(d, code))
            {
                ask.Summary = words.Length > 0 ? char.ToUpperInvariant(words[0]) + words.Substring(1) : "";
                ask.Reading = "";
                return;
            }
            ask.Summary = d;
            ask.Reading = words.Length > 0 ? "MinGent's reading: the code " + words + "." : "";
        }
    }

    static partial class ConsoleCheck
    {
        const int Unsure = 0, Deep = 1, Fresh = 2, Bag = 3, OfDom = 4, OfPage = 5, Native = 6, Stored = 7;

        class Origins
        {
            public readonly Dictionary<string, int> Kinds = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly HashSet<string> Methods = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Rebound = new HashSet<string>(StringComparer.Ordinal);

            public int Kind(string name)
            {
                int k;
                return name != null && Kinds.TryGetValue(name, out k) ? k : Unsure;
            }

            public void Put(string name, int kind)
            {
                int had;
                if (!Kinds.TryGetValue(name, out had)) { Kinds[name] = kind; return; }
                if (had == kind) return;
                Kinds[name] = had == OfPage || kind == OfPage ? OfPage : Unsure;
            }

            public bool Own(string method) { return Methods.Contains(method) && !Rebound.Contains(method); }
        }

        static readonly HashSet<string> Plain = Set("if else for while do switch case default break continue return throw try catch finally function class extends super new delete typeof instanceof void in await yield var let const true false null undefined NaN Infinity this debugger import export with");
        static readonly HashSet<string> Senders = Set("fetch fetchLater XMLHttpRequest sendBeacon WebSocket EventSource RTCPeerConnection WebTransport axios");
        static readonly HashSet<string> Addressed = Set("fetch fetchLater sendBeacon WebSocket EventSource WebTransport axios");
        static readonly HashSet<string> Stores = Set("localStorage sessionStorage indexedDB caches");
        static readonly HashSet<string> StoreWrites = Set("setItem removeItem clear deleteDatabase delete put add");
        static readonly HashSet<string> WindowNames = Set("window self globalThis top parent frames opener content");
        static readonly HashSet<string> WindowProps = Set("document location history navigator screen performance console crypto customElements visualViewport innerWidth innerHeight outerWidth outerHeight scrollX scrollY pageXOffset pageYOffset devicePixelRatio screenX screenY screenLeft screenTop length origin isSecureContext closed frameElement");
        static readonly HashSet<string> BuiltinObjs = Set("Object Array JSON Math Number String Boolean Symbol BigInt Date RegExp Promise Reflect Intl Map Set WeakMap WeakSet Error console performance navigator screen location history CSS crypto customElements URL URLSearchParams TextEncoder TextDecoder Blob File Atomics ArrayBuffer DataView Uint8Array Node NodeFilter Element HTMLElement Document Event XPathResult visualViewport DOMParser XMLSerializer");
        static readonly HashSet<string> BuiltinCtors = Set("Set Map WeakMap WeakSet WeakRef Array Object Date RegExp Error TypeError RangeError SyntaxError EvalError AggregateError Promise URL URLSearchParams Headers Request Response Blob File FileReader TextEncoder TextDecoder AbortController DOMParser XMLSerializer Uint8Array Uint8ClampedArray Uint16Array Uint32Array Int8Array Int16Array Int32Array Float32Array Float64Array BigInt64Array BigUint64Array ArrayBuffer SharedArrayBuffer DataView Proxy Option DocumentFragment Range DOMRect DOMPoint DOMMatrix Path2D OffscreenCanvas CustomEvent Event FormData MessageChannel BroadcastChannel CSSStyleSheet Number String Boolean Text Comment ImageData XPathEvaluator Intl XMLHttpRequest WebSocket EventSource RTCPeerConnection WebTransport Image Audio MutationObserver IntersectionObserver ResizeObserver PerformanceObserver ReportingObserver");
        static readonly HashSet<string> Watchers = Set("MutationObserver IntersectionObserver ResizeObserver PerformanceObserver ReportingObserver");
        static readonly HashSet<string> GlobalCalls = Set("parseInt parseFloat isNaN isFinite encodeURIComponent decodeURIComponent encodeURI decodeURI escape btoa atob structuredClone getComputedStyle matchMedia getSelection requestAnimationFrame cancelAnimationFrame requestIdleCallback cancelIdleCallback queueMicrotask setTimeout setInterval setImmediate clearTimeout clearInterval clearImmediate reportError createImageBitmap getEventListeners keys values dir dirxml table inspect queryObjects clear String Number Boolean BigInt Symbol Array Object Date RegExp Error TypeError RangeError SyntaxError Promise $ $$ $x jQuery");
        static readonly HashSet<string> ReadCalls = Set(
            "querySelector querySelectorAll getElementById getElementsByClassName getElementsByTagName getElementsByTagNameNS getElementsByName closest matches contains getAttribute getAttributeNS getAttributeNames getAttributeNode hasAttribute hasAttributeNS hasAttributes hasChildNodes hasFocus hasPointerCapture getBoundingClientRect getClientRects getComputedStyle getPropertyValue getPropertyPriority getRootNode compareDocumentPosition isEqualNode isSameNode isDefaultNamespace lookupPrefix lookupNamespaceURI checkValidity checkVisibility item namedItem elementFromPoint elementsFromPoint caretRangeFromPoint caretPositionFromPoint getSelection getAnimations cloneNode importNode createElement createElementNS createTextNode createComment createDocumentFragment createRange createTreeWalker createNodeIterator createAttribute createExpression createNSResolver evaluate snapshotItem iterateNext nextNode previousNode getContext toDataURL toBlob getImageData measureText isPointInPath getTotalLength getPointAtLength getBBox getCTM getScreenCTM getSVGDocument assignedNodes assignedElements getRangeAt containsNode comparePoint intersectsNode cloneRange cloneContents matchMedia supports " +
            "map filter forEach reduce reduceRight find findIndex findLast findLastIndex some every includes indexOf lastIndexOf slice join concat flat flatMap keys values entries at from of isArray toSorted toReversed toSpliced groupBy " +
            "split trim trimStart trimEnd trimLeft trimRight toLowerCase toUpperCase toLocaleLowerCase toLocaleUpperCase startsWith endsWith substring substr charAt charCodeAt codePointAt padStart padEnd repeat localeCompare match matchAll search replace replaceAll normalize isWellFormed " +
            "toString toFixed toPrecision toExponential toLocaleString toLocaleDateString toLocaleTimeString toISOString toJSON toUTCString toDateString toTimeString valueOf getTime getFullYear getMonth getDate getDay getHours getMinutes getSeconds getMilliseconds getTimezoneOffset getUTCFullYear getUTCMonth getUTCDate getUTCDay getUTCHours getUTCMinutes getUTCSeconds now parse stringify isInteger isSafeInteger isNaN isFinite parseFloat parseInt " +
            "hasOwnProperty hasOwn getOwnPropertyNames getOwnPropertyDescriptor getOwnPropertyDescriptors getOwnPropertySymbols getPrototypeOf isPrototypeOf propertyIsEnumerable isFrozen isSealed isExtensible fromEntries ownKeys " +
            "then catch finally all allSettled race any test " +
            "getEntries getEntriesByType getEntriesByName getRegistrations getRegistration estimate format formatToParts formatRange resolvedOptions supportedLocalesOf encode decode digest getRandomValues randomUUID getItem key");
        static readonly HashSet<string> ReadWhenEmpty = Set("text json blob arrayBuffer bytes clone html");
        static readonly HashSet<string> PageVerbs = Set("remove prepend before after toggle");
        static readonly HashSet<string> PageChanges = Set("appendChild insertBefore insertAdjacentElement insertAdjacentText replaceChild replaceChildren replaceWith removeChild removeAttribute removeAttributeNS removeAttributeNode setAttribute setAttributeNS setAttributeNode toggleAttribute setProperty removeProperty insertRule deleteRule addRule removeRule replaceSync adoptNode attachShadow requestFullscreen exitFullscreen requestPointerLock exitPointerLock setPointerCapture releasePointerCapture setCustomValidity reportValidity animate startViewTransition setSelectionRange removeAllRanges addRange collapseToStart collapseToEnd selectAllChildren deleteContents extractContents surroundContents insertNode setHTML showPicker removeEventListener");
        static readonly HashSet<string> Finders = Set("querySelector querySelectorAll getElementById getElementsByClassName getElementsByTagName getElementsByTagNameNS getElementsByName closest elementFromPoint elementsFromPoint cloneNode importNode createElement createElementNS createTextNode createDocumentFragment item namedItem assignedNodes assignedElements getRootNode");
        static readonly HashSet<string> Copies = Set("parse json structuredClone toJSON getBoundingClientRect");
        static readonly HashSet<string> Makers = Set("map filter slice concat flat flatMap split from of keys values entries fromEntries parse stringify structuredClone toSorted toReversed toSpliced match matchAll join trim trimStart trimEnd toLowerCase toUpperCase toString toFixed substring substr padStart padEnd repeat replace replaceAll toISOString toJSON getEntries getEntriesByType getEntriesByName getAttribute getAttributeNames getPropertyValue getBoundingClientRect getClientRects getComputedStyle json text blob arrayBuffer");
        static readonly HashSet<string> TakesFunctionFirst = Set("forEach map filter find findIndex findLast findLastIndex some every flatMap reduce reduceRight sort toSorted then catch finally setTimeout setInterval setImmediate requestAnimationFrame requestIdleCallback queueMicrotask");
        static readonly HashSet<string> TakesFunctionSecond = Set("addEventListener replace replaceAll from then");
        static readonly HashSet<string> Namespaces = Set("console Math JSON Number String Date Intl CSS crypto performance Symbol Promise Array URL Reflect");
        static readonly HashSet<string> Verbs = Set("GET POST PUT DELETE PATCH HEAD OPTIONS");
        static readonly HashSet<string> Quiet = Set("Math JSON Number String Boolean Date Intl Object Array Symbol BigInt RegExp Promise Error TypeError RangeError Map Set WeakMap WeakSet URL URLSearchParams TextEncoder TextDecoder Uint8Array ArrayBuffer parseInt parseFloat isNaN isFinite encodeURIComponent decodeURIComponent encodeURI decodeURI btoa atob console structuredClone of async get set static");
        static readonly HashSet<string> Relied = Set("fetch fetchLater XMLHttpRequest sendBeacon WebSocket EventSource RTCPeerConnection WebTransport alert confirm prompt setTimeout setInterval requestAnimationFrame queueMicrotask JSON Object Array Promise Function Math Date RegExp Proxy Reflect Symbol Error document window globalThis navigator console localStorage sessionStorage indexedDB FormData URL URLSearchParams Request Response Headers MutationObserver Event CustomEvent addEventListener removeEventListener dispatchEvent postMessage eval parseInt parseFloat encodeURIComponent decodeURIComponent atob btoa");
        static readonly HashSet<string> Joins = Set("return typeof instanceof in of new delete void throw case do else yield await async function class extends var let const if for while switch default try catch finally");
        static readonly HashSet<string> Unplaced = Set("createElement createElementNS createTextNode createDocumentFragment createComment cloneNode");
        static readonly Regex Handler = new Regex("^on[a-z]+$", RegexOptions.CultureInvariant);
        static readonly Regex Authority = new Regex(@"^(?:[a-z][a-z0-9+.\-]*:)?[/\\]{2}([^/\\?#]*)([/\\?#]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Scheme = new Regex(@"^[a-z][a-z0-9+.\-]*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex CssUrl = new Regex(@"url\(\s*(?:['""]\s*)?((?:https?:)?//[^'"")\s]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static ConsoleReading Read(string code, string pageUrl)
        {
            ConsoleReading r = new ConsoleReading();
            try
            {
                string s = code ?? "";
                bool ambiguous;
                ReadPass(s, false, pageUrl, r, out ambiguous);
                if (ambiguous) ReadPass(s, true, pageUrl, r, out ambiguous);
                if (r.Requests && !r.Here && r.Hosts.Count == 0) r.Unwritten = true;
            }
            catch (Exception) { r.Failed = true; }
            return r;
        }

        static void ReadPass(string code, bool flip, string pageUrl, ConsoleReading r, out bool ambiguous)
        {
            string hidden;
            List<Tok> t = Fold(Lex(code, flip, out hidden, out ambiguous));
            Facts f = Study(t);
            HashSet<string> kept = new HashSet<string>(StringComparer.Ordinal);
            int depth = 0;
            for (int i = 0; i < t.Count; i++)
            {
                if (t[i].Kind == Punct)
                {
                    if (t[i].Text == "{" || t[i].Text == "(" || t[i].Text == "[") depth++;
                    else if (t[i].Text == "}" || t[i].Text == ")" || t[i].Text == "]") depth--;
                    continue;
                }
                foreach (int j in Names(t, i))
                {
                    Declare(f, t, j);
                    if (depth == 0 && t[i].Text == "var" && (!Is(t, j + 1, "=") || Mentions(t, j + 2, Until(t, j), t[j].Text))) kept.Add(t[j].Text);
                }
            }
            Origins o = Trace(t, f);
            foreach (string name in kept) o.Kinds[name] = OfPage;
            string site = SiteOf(pageUrl);
            for (int i = 0; i < t.Count; i++) See(t, i, f, o, site, r);
            Leaves(t, r);
        }

        static void Leaves(List<Tok> t, ConsoleReading r)
        {
            int depth = 0;
            for (int i = 0; i < t.Count; i++)
            {
                Tok k = t[i];
                if (k.Kind == Punct)
                {
                    if (k.Text == "{" || k.Text == "(" || k.Text == "[") depth++;
                    else if (k.Text == "}" || k.Text == ")" || k.Text == "]") depth--;
                    continue;
                }
                if (depth != 0 || k.Kind != Ident || IsMember(t, i)) continue;
                if (k.Text == "function" || k.Text == "class")
                {
                    int before = Word(t, i - 1, "async") ? i - 2 : i - 1, at = Is(t, i + 1, "*") ? i + 2 : i + 1;
                    bool statement = before < 0 || (t[before].Kind == Punct ? t[before].Text == ";" || t[before].Text == "}" || t[before].Text == ")" || t[before].Text == "]" : t[before].Kind != Ident || !Before.Contains(t[before].Text));
                    if (statement && at < t.Count && t[at].Kind == Ident && Relied.Contains(t[at].Text)) r.Changes = true;
                    continue;
                }
                foreach (int j in Names(t, i)) if (Relied.Contains(t[j].Text)) r.Changes = true;
            }
        }

        static List<int> Names(List<Tok> t, int i)
        {
            List<int> names = new List<int>();
            if (!Word(t, i, "var") && !Word(t, i, "let") && !Word(t, i, "const") || IsMember(t, i)) return names;
            int j = i + 1;
            while (j < t.Count && t[j].Kind == Ident && !Plain.Contains(t[j].Text))
            {
                if (j + 1 < t.Count && !Is(t, j + 1, "=") && !Is(t, j + 1, ",") && !Is(t, j + 1, ";")) break;
                names.Add(j);
                int n = Until(t, j);
                if (!Is(t, n, ",")) break;
                j = n + 1;
            }
            return names;
        }

        static int Until(List<Tok> t, int j)
        {
            int d = 0, n = j + 1;
            for (; n < t.Count; n++)
            {
                Tok x = t[n];
                if (d == 0 && n > j + 1 && Starts(x) && Ends(t[n - 1])) break;
                if (x.Kind != Punct) continue;
                if (x.Text == "{" || x.Text == "(" || x.Text == "[") d++;
                else if (x.Text == "}" || x.Text == ")" || x.Text == "]") { if (--d < 0) break; }
                else if (d == 0 && (x.Text == "," || x.Text == ";")) break;
            }
            return n;
        }

        static bool Mentions(List<Tok> t, int from, int to, string name)
        {
            for (int i = from; i < to && i < t.Count; i++)
                if (t[i].Kind == Ident && t[i].Text == name && !IsMember(t, i) && !IsKey(t, i)) return true;
            return false;
        }

        static bool Starts(Tok x)
        {
            if (x.Kind == Ident) return x.Text != "in" && x.Text != "instanceof";
            return x.Kind == Num || x.Kind == Str || x.Kind == Rx;
        }

        static bool Ends(Tok x)
        {
            if (x.Kind == Ident) return !Joins.Contains(x.Text);
            if (x.Kind == Punct) return x.Text == ")" || x.Text == "]" || x.Text == "}";
            return x.Kind == Num || x.Kind == Str || x.Kind == Rx || x.Kind == Tmpl;
        }

        static bool Is(List<Tok> t, int i, string punct)
        {
            return i >= 0 && i < t.Count && t[i].Kind == Punct && t[i].Text == punct;
        }

        static bool Word(List<Tok> t, int i, string ident)
        {
            return i >= 0 && i < t.Count && t[i].Kind == Ident && t[i].Text == ident;
        }

        static bool Dot(List<Tok> t, int i)
        {
            return Is(t, i, ".") || Is(t, i, "?.");
        }

        static int Head(List<Tok> t, int e)
        {
            int j = e, start = e, guard = 0;
            while (j >= 0 && guard++ < 4000)
            {
                Tok x = t[j];
                if (x.Kind == Punct && (x.Text == ")" || x.Text == "]"))
                {
                    int open = Opener(t, j);
                    if (open < 0) return start;
                    start = open;
                    j = open - 1;
                    if (Is(t, j, "?.")) j--;
                    if (j >= 0 && ((t[j].Kind == Ident && !Before.Contains(t[j].Text)) || (t[j].Kind == Punct && (t[j].Text == ")" || t[j].Text == "]")))) continue;
                    return start;
                }
                if (x.Kind == Ident)
                {
                    if (Before.Contains(x.Text)) return start;
                    start = j;
                }
                else if (x.Kind == Str || x.Kind == Num || x.Kind == Rx || x.Kind == Tmpl) return j;
                else return start;
                if (!Dot(t, j - 1)) return start;
                j -= 2;
            }
            return start;
        }

        static int SkipFunction(List<Tok> t, int s, int limit)
        {
            int j = s;
            if (Word(t, j, "async")) j++;
            if (Word(t, j, "function"))
            {
                j++;
                if (Is(t, j, "*")) j++;
                if (j < limit && t[j].Kind == Ident) j++;
                if (!Is(t, j, "(")) return j;
                j = Close(t, j) + 1;
                return Is(t, j, "{") ? Close(t, j) + 1 : j;
            }
            if (Is(t, j, "(")) j = Close(t, j) + 1;
            else j++;
            if (!Is(t, j, "=>")) return j;
            j++;
            if (Is(t, j, "{")) return Close(t, j) + 1;
            int depth = 0;
            for (; j < limit; j++)
            {
                Tok x = t[j];
                if (x.Kind != Punct) continue;
                if (x.Text == "(" || x.Text == "[" || x.Text == "{") depth++;
                else if (x.Text == ")" || x.Text == "]" || x.Text == "}") { if (depth == 0) return j; depth--; }
                else if (x.Text == "," && depth == 0) return j;
            }
            return j;
        }

        static bool Sealed(List<Tok> t, int open, int close)
        {
            for (int j = open + 1; j < close && j < t.Count; j++)
            {
                Tok x = t[j];
                if (x.Kind == Tmpl && !(x.Opens && x.Closes)) return false;
                if (x.Kind != Ident) continue;
                if (FunctionAt(t, j)) { j = SkipFunction(t, j, close) - 1; continue; }
                if (Definition(t, j))
                {
                    int p = Close(t, j + 1);
                    j = Is(t, p + 1, "{") ? Close(t, p + 1) : p;
                    continue;
                }
                if (IsKey(t, j)) continue;
                if ((x.Text == "get" || x.Text == "set" || x.Text == "static") && j + 1 < close && t[j + 1].Kind == Ident) continue;
                if (x.Text == "true" || x.Text == "false" || x.Text == "null" || x.Text == "undefined" || x.Text == "NaN" || x.Text == "Infinity") continue;
                if (x.Text == "new" && j + 1 < close && t[j + 1].Kind == Ident && BuiltinCtors.Contains(t[j + 1].Text) && !Senders.Contains(t[j + 1].Text) && (!Is(t, j + 2, "(") || Is(t, j + 3, ")"))) { j++; continue; }
                return false;
            }
            return true;
        }

        static int WindowKind(List<Tok> t, int s)
        {
            if (!Dot(t, s + 1) || s + 2 >= t.Count || t[s + 2].Kind != Ident) return OfDom;
            string m = t[s + 2].Text;
            if (m == "document" || WindowNames.Contains(m)) return OfDom;
            return WindowProps.Contains(m) || BuiltinObjs.Contains(m) || Stores.Contains(m) || GlobalCalls.Contains(m) || BuiltinCtors.Contains(m) || m == "cookieStore" ? Native : OfPage;
        }

        static int RootKind(List<Tok> t, int s, Facts f, Origins o)
        {
            string n = t[s].Text;
            if (f.Declared.Contains(n)) return o.Kind(n);
            if (n == "document") return OfDom;
            if (WindowNames.Contains(n)) return WindowKind(t, s);
            if (Stores.Contains(n) || n == "cookieStore") return Stored;
            if (Senders.Contains(n)) return Native;
            if (BuiltinObjs.Contains(n) || BuiltinCtors.Contains(n)) return Native;
            if (n == "$" || n == "jQuery" || n == "$$" || n == "$x") return Is(t, s + 1, "(") ? OfDom : OfPage;
            if (GlobalCalls.Contains(n)) return Native;
            return OfPage;
        }

        static int AfterName(int kind, string name)
        {
            if (name == "length") return Deep;
            if (name == "document" && (kind == Native || kind == OfDom)) return OfDom;
            if (kind == Native && (Stores.Contains(name) || name == "cookieStore")) return Stored;
            if (kind == Deep || kind == OfDom || kind == OfPage || kind == Native) return kind;
            return Unsure;
        }

        static int AfterCall(int kind, string name)
        {
            if (name != null && (Makers.Contains(name) || Unplaced.Contains(name))) return Fresh;
            if (name != null && Finders.Contains(name)) return OfDom;
            return kind == OfPage ? OfPage : Unsure;
        }

        static int ValueKind(List<Tok> t, int s, int limit, Facts f, Origins o, int depth, out int end)
        {
            end = s;
            if (s < 0 || s >= limit || s >= t.Count || depth > 12) return Unsure;
            Tok x = t[s];
            int kind;
            int j;
            string called = null;
            if (x.Kind == Str || x.Kind == Num || x.Kind == Rx) { kind = Deep; j = s + 1; }
            else if (x.Kind == Tmpl)
            {
                j = s;
                int nest = 0;
                bool mixed = !(x.Opens && x.Closes);
                while (j < t.Count)
                {
                    if (t[j].Kind == Tmpl)
                    {
                        if (t[j].Opens) nest++;
                        if (t[j].Closes) nest--;
                        if (nest <= 0) { j++; break; }
                    }
                    j++;
                }
                kind = mixed ? Fresh : Deep;
            }
            else if (x.Kind == Punct)
            {
                if (x.Text == "[" || x.Text == "{")
                {
                    int c = Close(t, s);
                    kind = Sealed(t, s, c) ? Deep : x.Text == "[" ? Fresh : Bag;
                    j = c + 1;
                }
                else if (x.Text == "(")
                {
                    int c = Close(t, s), inner;
                    kind = ValueKind(t, s + 1, c, f, o, depth + 1, out inner);
                    if (inner < c) kind = kind == OfPage ? OfPage : Unsure;
                    j = c + 1;
                }
                else if (x.Text == "!" || x.Text == "-" || x.Text == "+" || x.Text == "~") { end = s + 1; return Deep; }
                else return Unsure;
            }
            else if (x.Kind != Ident) return Unsure;
            else
            {
                string n = x.Text;
                if (n == "await" || n == "yield") return ValueKind(t, s + 1, limit, f, o, depth + 1, out end);
                if (n == "typeof" || n == "void" || n == "delete" || n == "true" || n == "false" || n == "null" || n == "undefined" || n == "NaN" || n == "Infinity") { end = s + 1; return Deep; }
                if (n == "class" || FunctionAt(t, s)) { end = s + 1; return Deep; }
                if (n == "new")
                {
                    j = s + 1;
                    if (j >= t.Count || t[j].Kind != Ident) return Unsure;
                    string ctor = t[j].Text;
                    bool known = BuiltinCtors.Contains(ctor), own = f.Declared.Contains(ctor) && o.Kind(ctor) == Deep;
                    j++;
                    while (Dot(t, j) && j + 1 < t.Count && t[j + 1].Kind == Ident) j += 2;
                    bool bare = true;
                    if (Is(t, j, "(")) { int c = Close(t, j); bare = c == j + 1; j = c + 1; }
                    kind = known ? (bare && !Senders.Contains(ctor) ? Deep : Fresh) : own ? Fresh : OfPage;
                }
                else if (n == "this" || n == "super") { kind = Unsure; j = s + 1; }
                else if (Plain.Contains(n)) return Unsure;
                else
                {
                    kind = RootKind(t, s, f, o);
                    j = s + 1;
                    called = n;
                }
            }
            int guard = 0;
            while (j < limit && j < t.Count && guard++ < 4000)
            {
                Tok y = t[j];
                if (Dot(t, j) && j + 1 < t.Count && t[j + 1].Kind == Ident)
                {
                    if (j + 1 >= limit) break;
                    called = t[j + 1].Text;
                    j += 2;
                    if (Is(t, j, "(") || (Is(t, j, "?.") && Is(t, j + 1, "("))) continue;
                    kind = AfterName(kind, called);
                    called = null;
                    continue;
                }
                if (Is(t, j, "?.") && (Is(t, j + 1, "(") || Is(t, j + 1, "["))) { j++; continue; }
                if (y.Kind == Punct && y.Text == "(")
                {
                    int c = Close(t, j);
                    if (called == null) kind = kind == OfPage ? OfPage : Unsure;
                    else if (called == "$" || called == "jQuery" || called == "$$" || called == "$x") kind = kind == OfPage ? OfPage : OfDom;
                    else if (Copies.Contains(called) && kind != OfPage) kind = Deep;
                    else if (kind == Native && (GlobalCalls.Contains(called) || BuiltinCtors.Contains(called))) kind = Fresh;
                    else kind = AfterCall(kind, called);
                    called = null;
                    j = c + 1;
                    continue;
                }
                if (y.Kind == Punct && y.Text == "[")
                {
                    kind = AfterName(kind, "[]");
                    called = null;
                    j = Close(t, j) + 1;
                    continue;
                }
                break;
            }
            end = j;
            if (j < limit && j < t.Count && t[j].Kind == Punct)
            {
                string op = t[j].Text;
                if (op == "||" || op == "&&" || op == "??" || op == "?") return kind == OfPage ? OfPage : Unsure;
                if (op == "+" || op == "-" || op == "*" || op == "/" || op == "%" || op == "**" || op == "<" || op == ">" || op == "<=" || op == ">=" || op == "==" || op == "===" || op == "!=" || op == "!==") return Deep;
            }
            else if (j < limit && j < t.Count && t[j].Kind == Tmpl && t[j].Opens) return kind == OfPage ? OfPage : Unsure;
            return kind;
        }

        static int ReceiverKind(List<Tok> t, int i, Facts f, Origins o)
        {
            int e = i - 2;
            if (e < 0) return Unsure;
            int s = Head(t, e);
            if (Word(t, s - 1, "new")) s--;
            int end;
            return ValueKind(t, s, i - 1, f, o, 0, out end);
        }

        static int Spread(int kind)
        {
            return kind == OfDom || kind == OfPage ? kind : Unsure;
        }

        static Origins Trace(List<Tok> t, Facts f)
        {
            Origins o = new Origins();
            for (int i = 0; i < t.Count; i++)
            {
                Tok x = t[i];
                if (x.Kind != Ident) continue;
                string n = x.Text;
                bool member = IsMember(t, i);
                if (Definition(t, i))
                {
                    o.Methods.Add(n);
                    if (!member && f.Declared.Contains(n)) o.Put(n, Deep);
                    if (Is(t, i + 1, "(")) Params(t, i + 1, Close(t, i + 1), f, false);
                    continue;
                }
                if (!member && (n == "function" || n == "class") && i + 1 < t.Count && t[i + 1].Kind == Ident && f.Declared.Contains(t[i + 1].Text)) { o.Put(t[i + 1].Text, Deep); continue; }
                if (IsKey(t, i))
                {
                    if (FunctionAt(t, i + 2)) o.Methods.Add(n);
                    else o.Rebound.Add(n);
                    continue;
                }
                if (member && Is(t, i + 1, "="))
                {
                    if (FunctionAt(t, i + 2)) o.Methods.Add(n);
                    else o.Rebound.Add(n);
                    continue;
                }
                if (!member && (n == "const" || n == "let" || n == "var") && (Is(t, i + 1, "{") || Is(t, i + 1, "[")))
                {
                    int close = Close(t, i + 1);
                    if (!Is(t, close + 1, "=")) continue;
                    int end;
                    int kind = Spread(ValueKind(t, close + 2, t.Count, f, o, 0, out end));
                    bool whole = close + 2 < t.Count && t[close + 2].Kind == Ident && WindowNames.Contains(t[close + 2].Text) && !f.Declared.Contains(t[close + 2].Text) && end == close + 3;
                    for (int j = i + 2; j < close; j++)
                        if (t[j].Kind == Ident && f.Sites[j]) o.Put(t[j].Text, whole ? OfPage : kind);
                    continue;
                }
                if (!member && n == "for" && Is(t, i + 1, "("))
                {
                    int close = Close(t, i + 1), depth = 0, cut = -1;
                    for (int j = i + 2; j < close && cut < 0; j++)
                    {
                        Tok y = t[j];
                        if (y.Kind == Punct)
                        {
                            if (y.Text == "(" || y.Text == "[" || y.Text == "{") depth++;
                            else if (y.Text == ")" || y.Text == "]" || y.Text == "}") depth--;
                            else if (y.Text == ";" && depth == 0) break;
                        }
                        else if (depth == 0 && y.Kind == Ident && (y.Text == "of" || y.Text == "in")) cut = j;
                    }
                    if (cut < 0) continue;
                    int end;
                    int kind = t[cut].Text == "in" ? Deep : Spread(ValueKind(t, cut + 1, close, f, o, 0, out end));
                    for (int j = i + 2; j < cut; j++)
                        if (t[j].Kind == Ident && f.Sites[j]) o.Put(t[j].Text, kind);
                    continue;
                }
                if (!member && f.Declared.Contains(n) && Is(t, i + 1, "="))
                {
                    int end;
                    o.Put(n, ValueKind(t, i + 2, t.Count, f, o, 0, out end));
                }
            }
            return o;
        }

        static string SiteOf(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out u)) return "";
            if (u.Scheme != "http" && u.Scheme != "https" && u.Scheme != "ws" && u.Scheme != "wss") return "";
            string host = "";
            try { host = u.IdnHost; } catch (Exception) { host = u.Host; }
            host = (host ?? "").ToLowerInvariant();
            return u.IsDefaultPort ? host : host + ":" + u.Port.ToString(CultureInfo.InvariantCulture);
        }

        static void Address(string text, bool whole, string site, ConsoleReading r)
        {
            string u = (text ?? "").Replace("\t", "").Replace("\n", "").Replace("\r", "").Trim();
            if (u.Length == 0) { if (whole) r.Here = true; else r.Unwritten = true; return; }
            Match m = Authority.Match(u);
            if (m.Success)
            {
                string auth = m.Groups[1].Value;
                if (auth.Length == 0 || (!whole && m.Groups[2].Value.Length == 0)) { r.Unwritten = true; return; }
                string head = Scheme.IsMatch(u) ? u.Substring(0, u.IndexOf(':') + 1) : "http:";
                string there = SiteOf(head + "//" + auth + "/");
                if (there.Length == 0) { r.Unwritten = true; return; }
                if (site.Length > 0 && there == site) r.Here = true;
                else r.Host(there);
                return;
            }
            string low = u.ToLowerInvariant();
            if (low.StartsWith("data:", StringComparison.Ordinal) || low.StartsWith("blob:", StringComparison.Ordinal) || low.StartsWith("about:", StringComparison.Ordinal)) { r.Here = true; return; }
            if (Scheme.IsMatch(u)) { r.Unwritten = true; return; }
            bool rooted = u[0] == '?' || u[0] == '#' || u.StartsWith("./", StringComparison.Ordinal) || u.StartsWith("../", StringComparison.Ordinal) ||
                          (u[0] == '/' && u.Length > 1 && u[1] != '/' && u[1] != '\\');
            if (whole ? u[0] != '\\' : rooted) r.Here = true;
            else r.Unwritten = true;
        }

        static void AddressArg(List<Tok> t, int i, int index, string site, ConsoleReading r)
        {
            List<KeyValuePair<int, int>> spans = ArgSpans(t, i);
            if (index >= spans.Count) { r.Unwritten = true; return; }
            int a = spans[index].Key, b = spans[index].Value;
            Tok x = t[a];
            if (x.Kind == Str) { Address(x.Text, b - a == 1, site, r); return; }
            if (x.Kind == Tmpl && x.Opens) { Address(x.Text, x.Closes && b - a == 1, site, r); return; }
            if (x.Kind == Punct && x.Text == "{")
            {
                int close = Close(t, a);
                for (int j = a + 1; j < close; j++)
                    if (Word(t, j, "url") && IsKey(t, j) && j + 2 < close && (t[j + 2].Kind == Str || (t[j + 2].Kind == Tmpl && t[j + 2].Opens)))
                    {
                        bool whole = t[j + 2].Kind == Str ? (Is(t, j + 3, ",") || Is(t, j + 3, "}")) : t[j + 2].Closes && (Is(t, j + 3, ",") || Is(t, j + 3, "}"));
                        Address(t[j + 2].Text, whole, site, r);
                        return;
                    }
            }
            r.Unwritten = true;
        }

        static string Shown(List<Tok> t, int i)
        {
            List<string> parts = new List<string>();
            parts.Add(t[i].Text);
            int j = i - 1;
            while (Dot(t, j) && j >= 1 && t[j - 1].Kind == Ident && (!Plain.Contains(t[j - 1].Text) || t[j - 1].Text == "this"))
            {
                parts.Insert(0, t[j - 1].Text);
                j -= 2;
            }
            bool whole = !Dot(t, j);
            if (parts.Count > 3 || (!whole && parts.Count > 1))
            {
                int keep = Math.Min(2, parts.Count);
                parts = parts.GetRange(parts.Count - keep, keep);
            }
            string name = string.Join(".", parts.ToArray());
            if (whole || parts.Count > 1) return name;
            if (Is(t, j - 1, ")"))
            {
                int open = Opener(t, j - 1);
                return (open > 0 && t[open - 1].Kind == Ident && !Plain.Contains(t[open - 1].Text) ? t[open - 1].Text : "") + "()." + name;
            }
            if (!Is(t, j - 1, "]")) return name;
            int at = Opener(t, j - 1);
            string index = at >= 0 && j - 1 - at == 2 && t[at + 1].Kind == Num ? t[at + 1].Text : "";
            return (at > 0 && t[at - 1].Kind == Ident && !Plain.Contains(t[at - 1].Text) ? t[at - 1].Text : "") + "[" + index + "]." + name;
        }

        static bool Indexing(List<Tok> t, int i)
        {
            int depth = 0;
            for (int j = i - 1; j >= 0; j--)
            {
                if (t[j].Kind != Punct) continue;
                string x = t[j].Text;
                if (x == ")" || x == "]" || x == "}") depth++;
                else if (x == "(" || x == "[" || x == "{")
                {
                    if (depth > 0) { depth--; continue; }
                    return x == "[" && j > 0 && MemberBase(t[j - 1]) && !(t[j - 1].Kind == Ident && Plain.Contains(t[j - 1].Text) && t[j - 1].Text != "this");
                }
            }
            return false;
        }

        static bool Flagged(List<Tok> t, int i, ConsoleReading r)
        {
            if (!Is(t, i - 2, ")")) return false;
            int open = Opener(t, i - 2);
            if (open < 1 || t[open - 1].Kind != Ident) return false;
            return r.Calls.Contains(ConsoleReading.Clip(IsMember(t, open - 1) ? Shown(t, open - 1) : t[open - 1].Text));
        }

        static bool Enclosed(List<Tok> t, int i)
        {
            int depth = 0;
            for (int j = i - 1; j >= 0; j--)
            {
                if (t[j].Kind != Punct) continue;
                if (t[j].Text == "}") depth++;
                else if (t[j].Text == "{")
                {
                    if (depth > 0) { depth--; continue; }
                    if (ClassOrObject(t, j)) return true;
                }
            }
            return false;
        }

        static bool LocalWrite(List<Tok> t, int e, Facts f, Origins o, ConsoleReading r)
        {
            if (e < 0) return false;
            int s = Head(t, e);
            if (Word(t, s - 1, "new")) s--;
            if (Word(t, s, "this") || Word(t, s, "super")) return Enclosed(t, s);
            int end;
            int kind = ValueKind(t, s, e + 1, f, o, 0, out end);
            if (kind == Stored) { r.StoreChanged = true; return true; }
            return kind == Deep || kind == Fresh || kind == Bag;
        }

        static void Wrote(List<Tok> t, int i, bool member, Facts f, Origins o, ConsoleReading r)
        {
            string n = t[i].Text;
            if (!member)
            {
                if (!f.Declared.Contains(n) && !InBody(t, i)) r.Changes = true;
                return;
            }
            if (!LocalWrite(t, i - 2, f, o, r)) r.Changes = true;
        }

        static void StoreUse(List<Tok> t, int i, ConsoleReading r)
        {
            bool cookies = t[i].Text == "cookieStore";
            bool changed = false;
            if (Dot(t, i + 1) && i + 2 < t.Count && t[i + 2].Kind == Ident)
            {
                string m = t[i + 2].Text;
                bool called = Called(t, i + 2);
                changed = (called && (StoreWrites.Contains(m) || (cookies && m == "set"))) || (!called && (Assigned(t, i + 2) || PrefixUpdate(t, i + 2)));
            }
            else if (Is(t, i + 1, "["))
            {
                int c = Close(t, i + 1);
                changed = c + 1 < t.Count && t[c + 1].Kind == Punct && AssignOps.Contains(t[c + 1].Text);
            }
            if (i > 0 && Word(t, Head(t, i) - 1, "delete")) changed = true;
            if (cookies) { if (changed) r.CookiesChanged = true; else r.CookiesRead = true; }
            else if (changed) r.StoreChanged = true;
            else r.StoreRead = true;
        }

        static void Passed(List<Tok> t, int i, int index, Facts f, Origins o, ConsoleReading r)
        {
            List<KeyValuePair<int, int>> spans = ArgSpans(t, i);
            if (index >= spans.Count) return;
            int a = spans[index].Key, b = spans[index].Value;
            if (b <= a || (b - a) % 2 == 0) return;
            for (int j = a; j < b; j++)
            {
                bool name = (j - a) % 2 == 0;
                if (name ? t[j].Kind != Ident || Plain.Contains(t[j].Text) : !Dot(t, j)) return;
            }
            string root = t[a].Text;
            if (b - a == 1)
            {
                if (f.Declared.Contains(root)) { if (o.Kind(root) == OfPage) r.Call(root); return; }
                if (GlobalCalls.Contains(root) || BuiltinCtors.Contains(root) || BuiltinObjs.Contains(root)) return;
                r.Call(root);
                return;
            }
            int end;
            int kind = ValueKind(t, a, b - 1, f, o, 0, out end);
            if (kind == Deep || kind == Fresh || kind == Native) return;
            r.Call(Shown(t, b - 1));
        }

        static void MemberCalled(List<Tok> t, int i, string n, Facts f, Origins o, string site, ConsoleReading r)
        {
            string recv = i >= 2 && t[i - 2].Kind == Ident ? t[i - 2].Text : "";
            if (TakesFunctionFirst.Contains(n)) Passed(t, i, 0, f, o, r);
            if (TakesFunctionSecond.Contains(n)) Passed(t, i, 1, f, o, r);
            if (n == "addEventListener") { r.Lingers = true; return; }
            if (recv == "cookieStore")
            {
                if (n == "set" || n == "delete") r.CookiesChanged = true;
                else r.CookiesRead = true;
                return;
            }
            if ((n == "ajax" || n == "get" || n == "post" || n == "getJSON") && (recv == "$" || recv == "jQuery" || recv == "axios")) { r.Requests = true; AddressArg(t, i, 0, site, r); return; }
            if (n == "sendBeacon") { r.Requests = true; AddressArg(t, i, 0, site, r); return; }
            if (n == "open")
            {
                string verb;
                if (Arg(t, i, 0, out verb) && verb != null && Verbs.Contains(verb.ToUpperInvariant()) && ArgCount(t, i) >= 2) { r.Requests = true; AddressArg(t, i, 1, site, r); return; }
            }
            if (n == "load" && ArgIsString(t, i, 0)) { r.Requests = true; AddressArg(t, i, 0, site, r); return; }
            if (n == "setAttribute" || n == "setAttributeNS")
            {
                string attr;
                int at = n == "setAttributeNS" ? 1 : 0;
                if (Arg(t, i, at, out attr) && attr != null && LoadAttrs.Contains(attr.ToLowerInvariant())) { r.Requests = true; AddressArg(t, i, at + 1, site, r); }
                r.Changes = true;
                return;
            }
            if (PageChanges.Contains(n)) { r.Changes = true; return; }
            if (recv == "classList" || recv == "relList" || recv == "part")
            {
                if (n == "add" || n == "remove" || n == "toggle" || n == "replace") r.Changes = true;
                return;
            }
            if (recv == "customElements") { if (n == "define") r.Changes = true; return; }
            if (recv == "searchParams") return;
            if (recv == "Object")
            {
                if (n == "assign" || n == "freeze" || n == "seal" || n == "preventExtensions")
                {
                    List<KeyValuePair<int, int>> spans = ArgSpans(t, i);
                    if (spans.Count == 0) return;
                    int end;
                    int target = ValueKind(t, spans[0].Key, spans[0].Value, f, o, 0, out end);
                    if (!(end >= spans[0].Value && (target == Deep || target == Fresh || target == Bag))) r.Changes = true;
                }
                return;
            }
            if (Namespaces.Contains(recv) && !f.Declared.Contains(recv)) return;
            int kind = ReceiverKind(t, i, f, o);
            if (kind == Stored)
            {
                if (StoreWrites.Contains(n)) r.StoreChanged = true;
                else r.StoreRead = true;
                return;
            }
            if (ReadCalls.Contains(n) && !(kind == OfPage && Unfit(t, i, n))) return;
            if (ReadWhenEmpty.Contains(n) && EmptyCall(t, i)) return;
            bool mine = Word(t, i - 2, "this") || Word(t, i - 2, "super");
            if (mine && o.Own(n) && Enclosed(t, i)) return;
            if (kind == Deep || kind == Fresh || kind == Native) return;
            if (kind == Bag && o.Own(n)) return;
            if (n == "append" || PageVerbs.Contains(n))
            {
                if (kind != OfPage && !mine) r.Changes = true;
                else if (!Flagged(t, i, r)) r.Call(Shown(t, i));
                return;
            }
            if (!Flagged(t, i, r)) r.Call(Shown(t, i));
        }

        static bool Held(List<Tok> t, int i, Facts f, Origins o)
        {
            int kind = ReceiverKind(t, i, f, o);
            if (kind == Deep || kind == Fresh || kind == Bag) return true;
            return kind == OfPage && i >= 2 && IsMember(t, i - 2) && !Rooted(t, i, f);
        }

        static bool Rooted(List<Tok> t, int i, Facts f)
        {
            int at = i - 2;
            if (at < 0 || t[at].Kind != Ident) return false;
            if (!IsMember(t, at)) return !f.Declared.Contains(t[at].Text);
            return at >= 2 && t[at - 2].Kind == Ident && WindowNames.Contains(t[at - 2].Text) && !IsMember(t, at - 2) && !f.Declared.Contains(t[at - 2].Text);
        }

        static bool Unfit(List<Tok> t, int i, string n)
        {
            if (!TakesFunctionFirst.Contains(n)) return false;
            List<KeyValuePair<int, int>> spans = ArgSpans(t, i);
            if (spans.Count == 0 || spans[0].Value <= spans[0].Key) return false;
            Tok a = t[spans[0].Key];
            return a.Kind == Str || a.Kind == Num || a.Kind == Tmpl || (a.Kind == Punct && (a.Text == "[" || a.Text == "{"));
        }

        static void BareCalled(List<Tok> t, int i, string n, Facts f, Origins o, ConsoleReading r)
        {
            if (TakesFunctionFirst.Contains(n)) Passed(t, i, 0, f, o, r);
            if (Word(t, i - 1, "new")) return;
            if (f.Declared.Contains(n)) { if (o.Kind(n) == OfPage) r.Call(n); return; }
            if (GlobalCalls.Contains(n) || BuiltinCtors.Contains(n) || BuiltinObjs.Contains(n)) return;
            r.Call(n);
        }

        static void Built(List<Tok> t, int i, Facts f, Origins o, ConsoleReading r)
        {
            int j = i + 1;
            if (j >= t.Count || t[j].Kind != Ident) return;
            string ctor = t[j].Text;
            if (Dot(t, j + 1)) return;
            if (Watchers.Contains(ctor)) { r.Lingers = true; return; }
            if (BuiltinCtors.Contains(ctor) || f.Declared.Contains(ctor) && o.Kind(ctor) != OfPage) return;
            r.Call("new " + ctor);
        }

        static void See(List<Tok> t, int i, Facts f, Origins o, string site, ConsoleReading r)
        {
            Tok k = t[i];
            if (k.Kind == Str || k.Kind == Tmpl)
            {
                if (k.Text.IndexOf("url(", StringComparison.OrdinalIgnoreCase) < 0) return;
                foreach (Match m in CssUrl.Matches(k.Text)) { r.Requests = true; Address(m.Groups[1].Value, true, site, r); }
                return;
            }
            if (k.Kind == Punct)
            {
                if (k.Text == "]" && NumberIndex(t, i) && (Called(t, i) || GroupCalled(t, i)))
                {
                    int open = Opener(t, i), end;
                    int s = Head(t, open - 1);
                    if (ValueKind(t, s, open, f, o, 0, out end) != Deep) r.Call(open > 0 && t[open - 1].Kind == Ident ? Shown(t, open - 1) + "[" + t[open + 1].Text + "]" : "a list entry");
                }
                else if (AssignOps.Contains(k.Text) && Is(t, i - 1, "]"))
                {
                    int open = Opener(t, i - 1);
                    if (open > 0 && MemberBase(t[open - 1]) && !(t[open - 1].Kind == Ident && Plain.Contains(t[open - 1].Text) && t[open - 1].Text != "this") && !LocalWrite(t, open - 1, f, o, r)) r.Changes = true;
                }
                else if ((k.Text == "++" || k.Text == "--") && i + 1 < t.Count && (t[i + 1].Kind == Ident || Is(t, i + 1, "(")) && (i == 0 || !MemberBase(t[i - 1])))
                {
                    int e = Is(t, i + 1, "(") ? Close(t, i + 1) : i + 1, guard = 0;
                    while (e + 1 < t.Count && guard++ < 400 && (Dot(t, e + 1) || Is(t, e + 1, "[")))
                        e = Dot(t, e + 1) ? e + 2 : Close(t, e + 1);
                    if (e < t.Count && Is(t, e, "]"))
                    {
                        int open = Opener(t, e);
                        if (open > 0 && !LocalWrite(t, open - 1, f, o, r)) r.Changes = true;
                    }
                }
                return;
            }
            if (k.Kind != Ident) return;
            string n = k.Text;
            bool member = IsMember(t, i);
            if (!member && Plain.Contains(n))
            {
                if (n == "new") Built(t, i, f, o, r);
                else if (n == "delete")
                {
                    int e = i + 1, guard = 0;
                    if (e >= t.Count || (t[e].Kind != Ident && !Is(t, e, "("))) return;
                    if (Is(t, e, "(")) e = Close(t, e);
                    while (e + 1 < t.Count && guard++ < 400 && (Dot(t, e + 1) || Is(t, e + 1, "[")))
                        e = Dot(t, e + 1) ? e + 2 : Close(t, e + 1);
                    if (e >= t.Count) return;
                    if (Is(t, e, "]"))
                    {
                        int open = Opener(t, e);
                        if (open > 0 && !LocalWrite(t, open - 1, f, o, r)) r.Changes = true;
                    }
                    else if (t[e].Kind == Ident && IsMember(t, e) && !LocalWrite(t, e - 2, f, o, r)) r.Changes = true;
                }
                return;
            }
            if (!member && (n == "of" || (n == "async" && FunctionAt(t, i)))) return;
            if (Definition(t, i)) return;
            bool called = Called(t, i) || GroupCalled(t, i);
            bool written = !called && (Assigned(t, i) || PrefixUpdate(t, i) || (f.Targets[i] && !Indexing(t, i)));
            if (!member && !f.Declared.Contains(n) && !Quiet.Contains(n) && !IsKey(t, i)) r.Looks = true;
            if (n == "cookie") { if (written) r.CookiesChanged = true; else r.CookiesRead = true; return; }
            if (Stores.Contains(n) || n == "cookieStore") { if (!(member && f.Declared.Contains(Receiver(t, i)))) { StoreUse(t, i, r); return; } }
            if (n == "credentials" || n == "PasswordCredential" || n == "FederatedCredential") { r.SignIns = true; return; }
            if (n == "FormData") { r.Fields = true; return; }
            if (n == "value" && !written && (member ? !AttributeValue(t, i) && !Held(t, i, f, o) : ValueKey(t, i))) r.Fields = true;
            if (n.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0) r.Fields = true;
            if ((n == "val" || n == "serialize" || n == "serializeArray") && member && called && EmptyCall(t, i)) { r.Fields = true; return; }
            if (Senders.Contains(n) && !(member && n == "axios"))
            {
                r.Requests = true;
                if (Addressed.Contains(n) && called) AddressArg(t, i, 0, site, r);
                return;
            }
            if (!member && (n == "Image" || n == "Audio") && Word(t, i - 1, "new")) { r.Requests = true; return; }
            if (Watchers.Contains(n)) { r.Lingers = true; return; }
            if (n == "postMessage" && called) { r.Messages = true; return; }
            if (n == "setInterval" && called) r.Lingers = true;
            if ((n == "setTimeout" || n == "setImmediate") && called)
            {
                int open = CallOpen(t, i);
                bool waits = open >= 0 && open + 2 < t.Count && t[open + 1].Kind == Ident && f.PromiseParams.Contains(t[open + 1].Text) && (Is(t, open + 2, ",") || Is(t, open + 2, ")"));
                List<KeyValuePair<int, int>> spans = waits ? null : ArgSpans(t, i);
                if (spans != null && spans.Count > 0)
                    for (int j = spans[0].Key; j < spans[0].Value && !waits; j++)
                        waits = t[j].Kind == Ident && f.PromiseParams.Contains(t[j].Text) && !IsMember(t, j) && Is(t, j + 1, "(");
                if (!waits) r.Lingers = true;
            }
            if (written)
            {
                if (member && LoadProps.Contains(n) && Receiver(t, i) != "style")
                {
                    r.Requests = true;
                    if (Is(t, i + 1, "=") && i + 2 < t.Count && (t[i + 2].Kind == Str || (t[i + 2].Kind == Tmpl && t[i + 2].Opens)))
                        Address(t[i + 2].Text, (t[i + 2].Kind == Str || t[i + 2].Closes) && Ends(t, i + 2), site, r);
                    else r.Unwritten = true;
                }
                if (member && Handler.IsMatch(n) && Is(t, i + 1, "=") && (FunctionAt(t, i + 2) || (i + 2 < t.Count && t[i + 2].Kind == Ident && !Plain.Contains(t[i + 2].Text)))) r.Lingers = true;
                Wrote(t, i, member, f, o, r);
                return;
            }
            if (!called) return;
            if (member) MemberCalled(t, i, n, f, o, site, r);
            else BareCalled(t, i, n, f, o, r);
        }
    }
}
