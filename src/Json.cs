using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MinGent
{
    static class Json
    {
        const int MaxDepth = 256;

        public static object Parse(string s)
        {
            if (s == null) throw new FormatException("No JSON text.");
            int i = 0;
            object v = ParseValue(s, ref i, 0);
            SkipWs(s, ref i);
            if (i != s.Length) throw new FormatException("Unexpected text after JSON value at position " + i + ".");
            return v;
        }

        public static object TryParse(string s)
        {
            try { return Parse(s); } catch { return null; }
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static object ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON nested too deeply.");
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON.");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i, depth);
            if (c == '[') return ParseArray(s, ref i, depth);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { Expect(s, ref i, "true"); return true; }
            if (c == 'f') { Expect(s, ref i, "false"); return false; }
            if (c == 'n') { Expect(s, ref i, "null"); return null; }
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref i);
            throw new FormatException("Unexpected character '" + c + "' at position " + i + ".");
        }

        static void Expect(string s, ref int i, string lit)
        {
            if (i + lit.Length > s.Length || string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0)
                throw new FormatException("Invalid literal at position " + i + ".");
            i += lit.Length;
        }

        static Dictionary<string, object> ParseObject(string s, ref int i, int depth)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("Expected a property name at position " + i + ".");
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':' at position " + i + ".");
                i++;
                d[key] = ParseValue(s, ref i, depth + 1);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new FormatException("Expected ',' or '}' at position " + i + ".");
            }
        }

        static List<object> ParseArray(string s, ref int i, int depth)
        {
            List<object> list = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i, depth + 1));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return list; }
                throw new FormatException("Expected ',' or ']' at position " + i + ".");
            }
        }

        static string ParseString(string s, ref int i)
        {
            i++;
            StringBuilder sb = null;
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"')
                {
                    string r = sb == null ? s.Substring(start, i - start) : sb.Append(s, start, i - start).ToString();
                    i++;
                    return r;
                }
                if (c == '\\')
                {
                    if (sb == null) sb = new StringBuilder();
                    sb.Append(s, start, i - start);
                    i++;
                    if (i >= s.Length) break;
                    switch (s[i])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            {
                                if (i + 4 >= s.Length) throw new FormatException("Bad \\u escape.");
                                int code;
                                if (!int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                    throw new FormatException("Bad \\u escape.");
                                sb.Append((char)code);
                                i += 4;
                                break;
                            }
                        default: throw new FormatException("Bad escape at position " + i + ".");
                    }
                    i++;
                    start = i;
                    continue;
                }
                i++;
            }
            throw new FormatException("Unterminated string.");
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            if (s[i] == '-') i++;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') i++;
                else break;
            }
            double d;
            string t = s.Substring(start, i - start);
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("Bad number '" + t + "'.");
            return d;
        }

        public static string Write(object o)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, o, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object o, int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("JSON nested too deeply.");
            if (o == null) { sb.Append("null"); return; }
            string str = o as string;
            if (str != null) { WriteString(sb, str); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is double || o is float || o is decimal)
            {
                double d = Convert.ToDouble(o, CultureInfo.InvariantCulture);
                sb.Append((double.IsNaN(d) || double.IsInfinity(d)) ? "null" : d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (o is int || o is long || o is short || o is byte || o is uint || o is ulong)
            {
                sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture));
                return;
            }
            RawJson raw = o as RawJson;
            if (raw != null) { sb.Append(raw.Text); return; }
            IDictionary<string, object> dict = o as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            IEnumerable list = o as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object x in list) { if (!first) sb.Append(','); first = false; WriteValue(sb, x, depth + 1); }
                sb.Append(']');
                return;
            }
            WriteString(sb, Convert.ToString(o, CultureInfo.InvariantCulture));
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\u2028': sb.Append("\\u2028"); break;
                    case '\u2029': sb.Append("\\u2029"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static string Quote(string s)
        {
            StringBuilder sb = new StringBuilder();
            WriteString(sb, s ?? "");
            return sb.ToString();
        }

        public static object Get(object o, params object[] path)
        {
            foreach (object step in path)
            {
                if (o == null) return null;
                string key = step as string;
                if (key != null)
                {
                    IDictionary<string, object> d = o as IDictionary<string, object>;
                    if (d == null || !d.TryGetValue(key, out o)) return null;
                }
                else
                {
                    List<object> l = o as List<object>;
                    int i = (int)step;
                    if (l == null || i < 0 || i >= l.Count) return null;
                    o = l[i];
                }
            }
            return o;
        }

        public static string Str(object o, params object[] path) { return Get(o, path) as string; }

        public static int Int(object o, params object[] path)
        {
            object v = Get(o, path);
            if (!(v is double)) return 0;
            double d = (double)v;
            if (d >= int.MaxValue) return int.MaxValue;
            if (d <= int.MinValue) return int.MinValue;
            return (int)d;
        }

        public static long Long(object o, params object[] path)
        {
            object v = Get(o, path);
            if (!(v is double)) return 0;
            double d = (double)v;
            if (d >= long.MaxValue) return long.MaxValue;
            if (d <= long.MinValue) return long.MinValue;
            return (long)d;
        }

        public static double Num(object o, params object[] path)
        {
            object v = Get(o, path);
            return v is double ? (double)v : 0;
        }

        public static bool Bool(object o, params object[] path)
        {
            object v = Get(o, path);
            return v is bool && (bool)v;
        }

        public static List<object> List(object o, params object[] path) { return Get(o, path) as List<object>; }

        public static Dictionary<string, object> Obj(object o, params object[] path) { return Get(o, path) as Dictionary<string, object>; }

        public static Dictionary<string, object> O(params object[] kv)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        public static List<object> A(params object[] items) { return new List<object>(items); }
    }

    class RawJson
    {
        public readonly string Text;
        public RawJson(string text) { Text = text; }
    }
}
