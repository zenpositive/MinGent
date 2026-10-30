using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGent
{
    class PdfName
    {
        public readonly string V;
        public PdfName(string v) { V = v; }
    }

    class PdfRef
    {
        public int Num, Gen;
    }

    class PdfOp
    {
        public readonly string V;
        public PdfOp(string v) { V = v; }
    }

    class PdfStr
    {
        public byte[] B;
    }

    class PdfStream
    {
        public Dictionary<string, object> Dict;
        public int Start, Length, Num, Gen;
    }

    class PdfLazy
    {
        public byte[] Data;
        public int Offset;
    }

    class LimitException : Exception
    {
        public LimitException(string message) : base(message) { }
    }

    class PdfLexer
    {
        readonly byte[] d;
        public int Pos;
        readonly int end;
        public bool Content;
        static readonly object EndArray = new object(), EndDict = new object();

        public PdfLexer(byte[] data, int start, int end)
        {
            d = data;
            Pos = Math.Max(0, start);
            this.end = Math.Min(end, data.Length);
        }

        public bool AtEnd { get { return Pos >= end; } }

        public static bool White(byte b) { return b == 0 || b == 9 || b == 10 || b == 12 || b == 13 || b == 32; }
        public static bool Delim(byte b) { return b == 40 || b == 41 || b == 60 || b == 62 || b == 91 || b == 93 || b == 123 || b == 125 || b == 47 || b == 37; }

        public void SkipWhite()
        {
            while (Pos < end)
            {
                byte b = d[Pos];
                if (White(b)) Pos++;
                else if (b == 37) { while (Pos < end && d[Pos] != 10 && d[Pos] != 13) Pos++; }
                else break;
            }
        }

        public object Read() { return Read(0); }

        object Read(int depth)
        {
            if (depth > 100) throw new InvalidDataException("the PDF nests objects too deeply");
            SkipWhite();
            if (Pos >= end) return null;
            byte b = d[Pos];
            switch (b)
            {
                case 40: return LiteralString();
                case 60:
                    if (Pos + 1 < end && d[Pos + 1] == 60)
                    {
                        Pos += 2;
                        Dictionary<string, object> dict = new Dictionary<string, object>();
                        while (true)
                        {
                            object k = Read(depth + 1);
                            if (k == EndDict || (k == null && Pos >= end)) break;
                            PdfName kn = k as PdfName;
                            if (kn == null) continue;
                            object v = Read(depth + 1);
                            if (v == EndDict) break;
                            dict[kn.V] = v;
                        }
                        return dict;
                    }
                    return HexString();
                case 62:
                    Pos++;
                    if (Pos < end && d[Pos] == 62) Pos++;
                    return EndDict;
                case 91:
                    {
                        Pos++;
                        List<object> arr = new List<object>();
                        while (true)
                        {
                            object v = Read(depth + 1);
                            if (v == EndArray || (v == null && Pos >= end)) break;
                            if (v == EndDict) continue;
                            arr.Add(v);
                            if (arr.Count > 1000000) break;
                        }
                        return arr;
                    }
                case 93: Pos++; return EndArray;
                case 123: case 125: Pos++; return Op(Pos - 1, 1);
                case 47: return Name();
            }
            int start = Pos;
            while (Pos < end && !White(d[Pos]) && !Delim(d[Pos])) Pos++;
            if (Pos == start) { Pos++; return Op(start, 0); }
            int len = Pos - start;
            byte c0 = d[start];
            if ((c0 >= 48 && c0 <= 57) || c0 == 45 || c0 == 43 || c0 == 46)
            {
                double num;
                bool integer;
                if (Number(start, Pos, out num, out integer))
                {
                    if (!Content && integer && num >= 0 && num < int.MaxValue)
                    {
                        int save = Pos;
                        int g = PeekInt();
                        if (g >= 0)
                        {
                            SkipWhite();
                            if (Pos < end && d[Pos] == 82 && (Pos + 1 >= end || White(d[Pos + 1]) || Delim(d[Pos + 1])))
                            {
                                Pos++;
                                PdfRef r = new PdfRef();
                                r.Num = (int)num;
                                r.Gen = g;
                                return r;
                            }
                        }
                        Pos = save;
                    }
                    if (integer && num >= 0 && num < Small.Length) return Small[(int)num];
                    return num;
                }
            }
            if (len == 4 && Is(start, "true")) return true;
            if (len == 5 && Is(start, "false")) return false;
            if (len == 4 && Is(start, "null")) return null;
            return Op(start, len);
        }

        bool Is(int at, string word)
        {
            for (int i = 0; i < word.Length; i++) if (d[at + i] != word[i]) return false;
            return true;
        }

        PdfOp Op(int start, int len)
        {
            if (len <= 3)
            {
                int key = len;
                for (int i = 0; i < len; i++) key |= d[start + i] << (8 * (i + 1));
                PdfOp known;
                if (KnownOps.TryGetValue(key, out known)) return known;
            }
            return new PdfOp(Encoding.ASCII.GetString(d, start, Math.Min(len, 256)));
        }

        static readonly object[] Small = MakeSmall();
        static readonly Dictionary<int, PdfOp> KnownOps = MakeOps();

        static object[] MakeSmall()
        {
            object[] r = new object[1024];
            for (int i = 0; i < r.Length; i++) r[i] = (double)i;
            return r;
        }

        static Dictionary<int, PdfOp> MakeOps()
        {
            Dictionary<int, PdfOp> r = new Dictionary<int, PdfOp>();
            string all = "b B b* B* BDC BI BMC BT BX c cm CS cs d d0 d1 Do DP EI EMC ET EX f F f* G g gs h i ID j J K k l m M MP n q Q re RG rg ri s S SC sc SCN scn sh T* Tc Td TD Tf Tj TJ TL Tm Tr Ts Tw Tz v w W W* y ' \" { }";
            foreach (string op in all.Split(' '))
            {
                int key = op.Length;
                for (int i = 0; i < op.Length; i++) key |= op[i] << (8 * (i + 1));
                r[key] = new PdfOp(op);
            }
            return r;
        }

        bool Number(int s, int e, out double v, out bool integer)
        {
            v = 0;
            integer = true;
            int i = s;
            bool neg = false;
            if (d[i] == 45 || d[i] == 43)
            {
                neg = d[i] == 45;
                i++;
                while (i < e && d[i] == 45) i++;
            }
            double val = 0;
            int digits = 0;
            while (i < e && d[i] >= 48 && d[i] <= 57) { val = val * 10 + (d[i] - 48); i++; digits++; }
            if (i < e && d[i] == 46)
            {
                integer = false;
                i++;
                double f = 0.1;
                while (i < e && d[i] >= 48 && d[i] <= 57) { val += (d[i] - 48) * f; f *= 0.1; i++; digits++; }
            }
            if (i != e || digits == 0) return false;
            v = neg ? -val : val;
            return true;
        }

        int PeekInt()
        {
            SkipWhite();
            int start = Pos, v = 0;
            while (Pos < end && d[Pos] >= 48 && d[Pos] <= 57 && Pos - start < 7) { v = v * 10 + (d[Pos] - 48); Pos++; }
            if (Pos == start || (Pos < end && !White(d[Pos]) && !Delim(d[Pos]))) return -1;
            return v;
        }

        char[] buf = new char[64];

        PdfName Name()
        {
            Pos++;
            int start = Pos;
            bool escaped = false;
            while (Pos < end && !White(d[Pos]) && !Delim(d[Pos]))
            {
                if (d[Pos] == 35) escaped = true;
                Pos++;
            }
            int len = Math.Min(Pos - start, 512);
            if (!escaped)
            {
                if (buf.Length < len) buf = new char[Math.Max(len, buf.Length * 2)];
                for (int k = 0; k < len; k++) buf[k] = (char)d[start + k];
                return new PdfName(new string(buf, 0, len));
            }
            StringBuilder sb = new StringBuilder(len);
            for (int p = start; p < Pos && sb.Length < 512; )
            {
                byte b = d[p];
                if (b == 35 && p + 2 < Pos && Hex(d[p + 1]) >= 0 && Hex(d[p + 2]) >= 0)
                {
                    sb.Append((char)(Hex(d[p + 1]) * 16 + Hex(d[p + 2])));
                    p += 3;
                    continue;
                }
                sb.Append((char)b);
                p++;
            }
            return new PdfName(sb.ToString());
        }

        public static int Hex(byte b)
        {
            if (b >= 48 && b <= 57) return b - 48;
            if (b >= 65 && b <= 70) return b - 55;
            if (b >= 97 && b <= 102) return b - 87;
            return -1;
        }

        PdfStr HexString()
        {
            Pos++;
            List<byte> bytes = new List<byte>();
            int hi = -1;
            while (Pos < end && d[Pos] != 62)
            {
                int h = Hex(d[Pos]);
                Pos++;
                if (h < 0) continue;
                if (hi < 0) hi = h;
                else { bytes.Add((byte)(hi * 16 + h)); hi = -1; }
            }
            if (hi >= 0) bytes.Add((byte)(hi * 16));
            if (Pos < end) Pos++;
            PdfStr s = new PdfStr();
            s.B = bytes.ToArray();
            return s;
        }

        PdfStr LiteralString()
        {
            Pos++;
            List<byte> bytes = new List<byte>();
            int nest = 1;
            while (Pos < end)
            {
                byte b = d[Pos++];
                if (b == 92 && Pos < end)
                {
                    byte e = d[Pos++];
                    switch (e)
                    {
                        case 110: bytes.Add(10); break;
                        case 114: bytes.Add(13); break;
                        case 116: bytes.Add(9); break;
                        case 98: bytes.Add(8); break;
                        case 102: bytes.Add(12); break;
                        case 13: if (Pos < end && d[Pos] == 10) Pos++; break;
                        case 10: break;
                        default:
                            if (e >= 48 && e <= 55)
                            {
                                int v = e - 48;
                                for (int k = 0; k < 2 && Pos < end && d[Pos] >= 48 && d[Pos] <= 55; k++) v = v * 8 + (d[Pos++] - 48);
                                bytes.Add((byte)(v & 0xFF));
                            }
                            else bytes.Add(e);
                            break;
                    }
                    continue;
                }
                if (b == 40) nest++;
                else if (b == 41) { nest--; if (nest == 0) break; }
                bytes.Add(b);
            }
            PdfStr s = new PdfStr();
            s.B = bytes.ToArray();
            return s;
        }

        public bool Keyword(string word)
        {
            SkipWhite();
            if (Pos + word.Length > end) return false;
            for (int i = 0; i < word.Length; i++) if (d[Pos + i] != word[i]) return false;
            int after = Pos + word.Length;
            if (after < end && !White(d[after]) && !Delim(d[after])) return false;
            Pos = after;
            return true;
        }

        public void SkipInlineImage()
        {
            if (Pos < end && White(d[Pos])) Pos++;
            while (Pos + 1 < end)
            {
                if (d[Pos] == 69 && d[Pos + 1] == 73 && Pos > 0 && White(d[Pos - 1]) && (Pos + 2 >= end || White(d[Pos + 2]) || Delim(d[Pos + 2])))
                {
                    Pos += 2;
                    return;
                }
                Pos++;
            }
            Pos = end;
        }
    }

    class PdfFile
    {
        public readonly byte[] D;
        readonly Dictionary<int, object> objs = new Dictionary<int, object>();
        readonly Dictionary<int, int> where = new Dictionary<int, int>();
        readonly HashSet<int> packed = new HashSet<int>();
        readonly List<KeyValuePair<int, Dictionary<string, object>>> tails = new List<KeyValuePair<int, Dictionary<string, object>>>();
        public readonly Dictionary<string, object> Trailer = new Dictionary<string, object>();
        public PdfCrypt Crypt;
        long inflated;
        const long MaxInflated = 256L * 1024 * 1024;
        static readonly string[] TrailerKeys = { "Root", "Encrypt", "ID", "Info" };

        public PdfFile(byte[] data)
        {
            D = data;
            Scan();
            FindTrailer();
            SetupCrypt();
            LoadObjectStreams();
        }

        void Scan()
        {
            int n = D.Length;
            for (int i = 1; i + 3 <= n; i++)
            {
                i = Array.IndexOf(D, (byte)111, i, n - 2 - i);
                if (i < 0) break;
                if (D[i + 1] != 98 || D[i + 2] != 106) continue;
                if (i + 3 < n && !PdfLexer.White(D[i + 3]) && !PdfLexer.Delim(D[i + 3])) continue;
                int k = i - 1;
                if (!PdfLexer.White(D[k])) continue;
                while (k >= 0 && PdfLexer.White(D[k])) k--;
                int ge = k;
                while (k >= 0 && D[k] >= 48 && D[k] <= 57) k--;
                if (k == ge || ge - k > 5 || k < 0 || !PdfLexer.White(D[k])) continue;
                int gen = ParseInt(k + 1, ge);
                while (k >= 0 && PdfLexer.White(D[k])) k--;
                int ne = k;
                while (k >= 0 && D[k] >= 48 && D[k] <= 57) k--;
                if (k == ne || ne - k > 9) continue;
                if (k >= 0 && !PdfLexer.White(D[k]) && !PdfLexer.Delim(D[k])) continue;
                int num = ParseInt(k + 1, ne);
                if (num < 0 || gen < 0) continue;
                PdfLexer lx = new PdfLexer(D, i + 3, n);
                object o;
                try { o = lx.Read(); }
                catch (Exception) { continue; }
                Dictionary<string, object> dict = o as Dictionary<string, object>;
                int next = lx.Pos;
                if (dict != null && lx.Keyword("stream"))
                {
                    int ds = lx.Pos;
                    if (ds < n && D[ds] == 13) ds++;
                    if (ds < n && D[ds] == 10) ds++;
                    int len = -1;
                    object lo;
                    if (dict.TryGetValue("Length", out lo) && lo is double) len = (int)(double)lo;
                    int de = -1;
                    if (len >= 0 && len <= n - ds)
                    {
                        PdfLexer t = new PdfLexer(D, ds + len, n);
                        if (t.Keyword("endstream")) { de = ds + len; next = t.Pos; }
                    }
                    if (de < 0)
                    {
                        int e = Find(D, "endstream", ds);
                        if (e < 0) { de = n; next = n; }
                        else
                        {
                            de = e;
                            next = e + 9;
                            if (de > ds && D[de - 1] == 10) de--;
                            if (de > ds && D[de - 1] == 13) de--;
                        }
                    }
                    PdfStream s = new PdfStream();
                    s.Dict = dict;
                    s.Start = ds;
                    s.Length = Math.Max(0, de - ds);
                    s.Num = num;
                    s.Gen = gen;
                    o = s;
                    if (Name(dict, "Type") == "XRef") tails.Add(new KeyValuePair<int, Dictionary<string, object>>(k + 1, dict));
                }
                objs[num] = o;
                where[num] = k + 1;
                i = Math.Max(i, next - 1);
            }
        }

        void FindTrailer()
        {
            for (int p = Find(D, "trailer", 0); p >= 0; p = Find(D, "trailer", p + 7))
            {
                try
                {
                    PdfLexer lx = new PdfLexer(D, p + 7, D.Length);
                    Dictionary<string, object> t = lx.Read() as Dictionary<string, object>;
                    if (t != null) tails.Add(new KeyValuePair<int, Dictionary<string, object>>(p, t));
                }
                catch (InvalidDataException) { }
            }
            tails.Sort(delegate(KeyValuePair<int, Dictionary<string, object>> a, KeyValuePair<int, Dictionary<string, object>> b) { return b.Key.CompareTo(a.Key); });
            foreach (KeyValuePair<int, Dictionary<string, object>> t in tails)
                foreach (string key in TrailerKeys)
                    if (!Trailer.ContainsKey(key) && t.Value.ContainsKey(key)) Trailer[key] = t.Value[key];
        }

        int ParseInt(int from, int to)
        {
            int v = 0;
            for (int i = from; i <= to; i++) { v = v * 10 + (D[i] - 48); if (v > 100000000) return -1; }
            return v;
        }

        public static int Find(byte[] d, string s, int from)
        {
            int n = d.Length - s.Length;
            byte first = (byte)s[0];
            for (int i = Math.Max(0, from); i <= n; i++)
            {
                i = Array.IndexOf(d, first, i, n - i + 1);
                if (i < 0) return -1;
                int k = 1;
                while (k < s.Length && d[i + k] == s[k]) k++;
                if (k == s.Length) return i;
            }
            return -1;
        }

        void SetupCrypt()
        {
            object raw;
            if (!Trailer.TryGetValue("Encrypt", out raw)) return;
            Dictionary<string, object> ed = Resolve(raw) as Dictionary<string, object>;
            if (ed == null) return;
            byte[] id0 = new byte[0];
            List<object> ids = Get(Trailer, "ID") as List<object>;
            if (ids != null && ids.Count > 0 && Resolve(ids[0]) is PdfStr) id0 = ((PdfStr)Resolve(ids[0])).B;
            Crypt = PdfCrypt.Open(ed, id0, this);
        }

        void LoadObjectStreams()
        {
            List<KeyValuePair<int, PdfStream>> streams = new List<KeyValuePair<int, PdfStream>>();
            foreach (KeyValuePair<int, object> kv in objs)
            {
                PdfStream s = kv.Value as PdfStream;
                if (s != null && Name(s.Dict, "Type") == "ObjStm") streams.Add(new KeyValuePair<int, PdfStream>(kv.Key, s));
            }
            foreach (KeyValuePair<int, PdfStream> kv in streams)
            {
                byte[] data;
                try { data = Decode(kv.Value); }
                catch (LimitException) { throw; }
                catch (Exception) { continue; }
                if (data == null) continue;
                int count = Int(kv.Value.Dict, "N"), first = Int(kv.Value.Dict, "First");
                if (count <= 0 || count > 1000000 || first < 0 || first > data.Length) continue;
                PdfLexer head = new PdfLexer(data, 0, first);
                head.Content = true;
                List<int> nums = new List<int>(), offs = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    object a, b;
                    try { a = head.Read(); b = head.Read(); }
                    catch (InvalidDataException) { break; }
                    if (!(a is double) || !(b is double)) break;
                    nums.Add((int)(double)a);
                    offs.Add((int)(double)b);
                }
                int pos = where.ContainsKey(kv.Key) ? where[kv.Key] : 0;
                for (int i = 0; i < nums.Count; i++)
                {
                    int num = nums[i], prev;
                    if (num < 0 || offs[i] < 0 || offs[i] >= data.Length - first) continue;
                    if (objs.ContainsKey(num) && where.TryGetValue(num, out prev) && prev > pos) continue;
                    PdfLazy lazy = new PdfLazy();
                    lazy.Data = data;
                    lazy.Offset = first + offs[i];
                    objs[num] = lazy;
                    where[num] = pos;
                    packed.Add(num);
                }
            }
        }

        public object Resolve(object o)
        {
            for (int depth = 0; depth < 32; depth++)
            {
                PdfRef r = o as PdfRef;
                if (r == null) return o;
                if (!objs.TryGetValue(r.Num, out o)) return null;
                PdfLazy lazy = o as PdfLazy;
                if (lazy != null)
                {
                    o = Parse(lazy);
                    objs[r.Num] = o;
                }
            }
            return null;
        }

        public string Text(Dictionary<string, object> d, string key, PdfRef owner)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            return Text(v, owner);
        }

        public string Text(object o, PdfRef owner)
        {
            PdfRef r = o as PdfRef;
            if (r != null) { owner = r; o = Resolve(r); }
            PdfStr s = o as PdfStr;
            if (s == null)
            {
                PdfName n = o as PdfName;
                return n != null ? n.V : null;
            }
            byte[] b = s.B;
            if (Crypt != null && owner != null && !packed.Contains(owner.Num))
            {
                byte[] plain = Crypt.Decrypt(b, owner.Num, owner.Gen, false);
                if (plain != null) b = plain;
            }
            return TextString(b);
        }

        public static string TextString(byte[] b)
        {
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, (b.Length - 2) & ~1);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, (b.Length - 2) & ~1);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return new UTF8Encoding(false, false).GetString(b, 3, b.Length - 3);
            char[] c = new char[b.Length];
            for (int i = 0; i < b.Length; i++) c[i] = b[i] < PdfTables.PdfDocEnc.Length ? PdfTables.PdfDocEnc[b[i]] : (char)b[i];
            return new string(c);
        }

        static object Parse(PdfLazy lazy)
        {
            try { return new PdfLexer(lazy.Data, lazy.Offset, lazy.Data.Length).Read(); }
            catch (Exception) { return null; }
        }

        public List<Dictionary<string, object>> Typed(string type)
        {
            List<int> nums = new List<int>(objs.Keys);
            nums.Sort();
            List<Dictionary<string, object>> found = new List<Dictionary<string, object>>();
            foreach (int num in nums)
            {
                PdfRef r = new PdfRef();
                r.Num = num;
                Dictionary<string, object> d = Resolve(r) as Dictionary<string, object>;
                if (d != null && Name(d, "Type") == type) found.Add(d);
                if (found.Count >= 50000) break;
            }
            return found;
        }

        public Dictionary<string, object> Dict(object o)
        {
            object v = Resolve(o);
            PdfStream s = v as PdfStream;
            if (s != null) return s.Dict;
            return v as Dictionary<string, object>;
        }

        public object Get(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            return Resolve(v);
        }

        public static string Name(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            PdfName n = v as PdfName;
            return n != null ? n.V : null;
        }

        public string NameOf(Dictionary<string, object> d, string key)
        {
            PdfName n = Get(d, key) as PdfName;
            return n != null ? n.V : null;
        }

        public int Int(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v is double && (double)v >= int.MinValue && (double)v <= int.MaxValue ? (int)(double)v : -1;
        }

        public double Num(Dictionary<string, object> d, string key, double def)
        {
            object v = Get(d, key);
            return v is double ? (double)v : def;
        }

        public byte[] Decode(PdfStream s)
        {
            byte[] data = new byte[s.Length];
            Buffer.BlockCopy(D, s.Start, data, 0, s.Length);
            if (Crypt != null && Name(s.Dict, "Type") != "XRef")
            {
                data = Crypt.Decrypt(data, s.Num, s.Gen, true);
                if (data == null) return null;
            }
            object f = Get(s.Dict, "Filter");
            object p = Get(s.Dict, "DecodeParms") ?? Get(s.Dict, "DP");
            List<object> filters = f is List<object> ? (List<object>)f : f != null ? new List<object>(new object[] { f }) : new List<object>();
            List<object> parms = p is List<object> ? (List<object>)p : new List<object>(new object[] { p });
            if (filters.Count > 8) return null;
            for (int i = 0; i < filters.Count; i++)
            {
                PdfName fn = Resolve(filters[i]) as PdfName;
                if (fn == null) return null;
                Dictionary<string, object> pd = Dict(i < parms.Count ? parms[i] : null);
                data = PdfFilters.Apply(fn.V, data, pd, this);
                if (data == null) return null;
                inflated += data.Length;
                if (inflated > MaxInflated) throw new LimitException("the PDF unpacks to more data than MinGent reads");
            }
            return data;
        }
    }

    static class PdfFilters
    {
        const int MaxStream = 64 * 1024 * 1024;

        public static byte[] Apply(string name, byte[] data, Dictionary<string, object> parms, PdfFile f)
        {
            switch (name)
            {
                case "FlateDecode": case "Fl": return Predict(Inflate(data), parms, f);
                case "LZWDecode": case "LZW": return Predict(Lzw(data, f.Int(parms, "EarlyChange") != 0), parms, f);
                case "ASCIIHexDecode": case "AHx": return AsciiHex(data);
                case "ASCII85Decode": case "A85": return Ascii85(data);
                case "RunLengthDecode": case "RL": return RunLength(data);
                case "Crypt": return data;
            }
            return null;
        }

        static byte[] Inflate(byte[] data)
        {
            int start = 0;
            if (data.Length >= 2 && (data[0] & 0x0F) == 8 && ((data[0] << 8) | data[1]) % 31 == 0) start = 2;
            using (MemoryStream src = new MemoryStream(data, start, data.Length - start))
            using (DeflateStream z = new DeflateStream(src, CompressionMode.Decompress))
            using (MemoryStream dst = new MemoryStream())
            {
                byte[] buf = new byte[65536];
                try
                {
                    int n;
                    while ((n = z.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        if (dst.Length > MaxStream) throw new LimitException("a part of the PDF unpacks to more data than MinGent reads");
                    }
                }
                catch (InvalidDataException) { }
                catch (IOException) { }
                return dst.ToArray();
            }
        }

        static byte[] Predict(byte[] data, Dictionary<string, object> parms, PdfFile f)
        {
            if (data == null || parms == null) return data;
            int pred = f.Int(parms, "Predictor");
            if (pred <= 1) return data;
            int colors = Math.Max(1, f.Int(parms, "Colors")), bpc = f.Int(parms, "BitsPerComponent"), cols = f.Int(parms, "Columns");
            if (bpc <= 0) bpc = 8;
            if (cols <= 0) cols = 1;
            if (colors > 64 || bpc > 16 || cols > 65536) return data;
            int bpp = Math.Max(1, (colors * bpc + 7) / 8);
            int row = (colors * bpc * cols + 7) / 8;
            if (pred == 2)
            {
                if (bpc != 8) return data;
                byte[] o2 = (byte[])data.Clone();
                for (int r = 0; r + row <= o2.Length; r += row)
                    for (int i = bpp; i < row; i++) o2[r + i] = (byte)(o2[r + i] + o2[r + i - bpp]);
                return o2;
            }
            using (MemoryStream o = new MemoryStream())
            {
                byte[] prev = new byte[row], cur = new byte[row];
                int p = 0;
                while (p + 1 + row <= data.Length)
                {
                    int type = data[p++];
                    Buffer.BlockCopy(data, p, cur, 0, row);
                    p += row;
                    for (int i = 0; i < row; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                        switch (type)
                        {
                            case 1: cur[i] = (byte)(cur[i] + a); break;
                            case 2: cur[i] = (byte)(cur[i] + b); break;
                            case 3: cur[i] = (byte)(cur[i] + ((a + b) >> 1)); break;
                            case 4:
                                {
                                    int pa = Math.Abs(b - c), pb = Math.Abs(a - c), pc = Math.Abs(a + b - 2 * c);
                                    int pr = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                                    cur[i] = (byte)(cur[i] + pr);
                                    break;
                                }
                        }
                    }
                    o.Write(cur, 0, row);
                    byte[] t = prev; prev = cur; cur = t;
                }
                return o.ToArray();
            }
        }

        static byte[] Lzw(byte[] data, bool early)
        {
            List<byte[]> table = new List<byte[]>(4096);
            Action reset = delegate
            {
                table.Clear();
                for (int i = 0; i < 256; i++) table.Add(new byte[] { (byte)i });
                table.Add(null);
                table.Add(null);
            };
            reset();
            using (MemoryStream o = new MemoryStream())
            {
                int bits = 9;
                long bitPos = 0, total = (long)data.Length * 8;
                byte[] prev = null;
                while (bitPos + bits <= total)
                {
                    int code = 0;
                    for (int i = 0; i < bits; i++)
                    {
                        long bp = bitPos + i;
                        code = (code << 1) | ((data[bp >> 3] >> (7 - (int)(bp & 7))) & 1);
                    }
                    bitPos += bits;
                    if (code == 256) { reset(); bits = 9; prev = null; continue; }
                    if (code == 257) break;
                    byte[] entry;
                    if (code < table.Count && table[code] != null) entry = table[code];
                    else if (code == table.Count && prev != null)
                    {
                        entry = new byte[prev.Length + 1];
                        Buffer.BlockCopy(prev, 0, entry, 0, prev.Length);
                        entry[prev.Length] = prev[0];
                    }
                    else break;
                    o.Write(entry, 0, entry.Length);
                    if (o.Length > MaxStream) throw new LimitException("a part of the PDF unpacks to more data than MinGent reads");
                    if (prev != null && table.Count < 4096)
                    {
                        byte[] add = new byte[prev.Length + 1];
                        Buffer.BlockCopy(prev, 0, add, 0, prev.Length);
                        add[prev.Length] = entry[0];
                        table.Add(add);
                    }
                    prev = entry;
                    int limit = table.Count + (early ? 1 : 0);
                    bits = limit >= 2048 ? 12 : limit >= 1024 ? 11 : limit >= 512 ? 10 : 9;
                }
                return o.ToArray();
            }
        }

        static byte[] AsciiHex(byte[] data)
        {
            List<byte> o = new List<byte>(data.Length / 2);
            int hi = -1;
            foreach (byte b in data)
            {
                if (b == 62) break;
                int h = PdfLexer.Hex(b);
                if (h < 0) continue;
                if (hi < 0) hi = h; else { o.Add((byte)(hi * 16 + h)); hi = -1; }
            }
            if (hi >= 0) o.Add((byte)(hi * 16));
            return o.ToArray();
        }

        static byte[] Ascii85(byte[] data)
        {
            using (MemoryStream o = new MemoryStream())
            {
                uint acc = 0;
                int n = 0, i = 0;
                if (data.Length >= 2 && data[0] == 60 && data[1] == 126) i = 2;
                for (; i < data.Length; i++)
                {
                    byte b = data[i];
                    if (b == 126) break;
                    if (PdfLexer.White(b)) continue;
                    if (b == 122 && n == 0) { o.Write(new byte[4], 0, 4); continue; }
                    if (b < 33 || b > 117) continue;
                    acc = unchecked(acc * 85 + (uint)(b - 33));
                    n++;
                    if (n == 5)
                    {
                        o.WriteByte((byte)(acc >> 24)); o.WriteByte((byte)(acc >> 16)); o.WriteByte((byte)(acc >> 8)); o.WriteByte((byte)acc);
                        acc = 0;
                        n = 0;
                    }
                }
                if (n > 1)
                {
                    for (int k = n; k < 5; k++) acc = unchecked(acc * 85 + 84);
                    for (int k = 0; k < n - 1; k++) o.WriteByte((byte)(acc >> (24 - 8 * k)));
                }
                return o.ToArray();
            }
        }

        static byte[] RunLength(byte[] data)
        {
            using (MemoryStream o = new MemoryStream())
            {
                int i = 0;
                while (i < data.Length)
                {
                    int len = data[i++];
                    if (len == 128) break;
                    if (len < 128)
                    {
                        int c = Math.Min(len + 1, data.Length - i);
                        o.Write(data, i, c);
                        i += c;
                    }
                    else if (i < data.Length)
                    {
                        byte v = data[i++];
                        for (int k = 0; k < 257 - len; k++) o.WriteByte(v);
                    }
                    if (o.Length > MaxStream) throw new LimitException("a part of the PDF unpacks to more data than MinGent reads");
                }
                return o.ToArray();
            }
        }
    }

    class PdfCrypt
    {
        byte[] key;
        int rev;
        bool aesStreams, identityStreams, aesStrings, identityStrings;
        public bool Failed;

        static readonly byte[] Pad = {
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A };

        public static PdfCrypt Open(Dictionary<string, object> ed, byte[] id0, PdfFile f)
        {
            PdfCrypt c = new PdfCrypt();
            if (f.NameOf(ed, "Filter") != "Standard") { c.Failed = true; return c; }
            int ver = f.Int(ed, "V");
            c.rev = f.Int(ed, "R");
            int bits = f.Int(ed, "Length");
            int keyLen = c.rev <= 2 ? 5 : bits >= 40 && bits <= 128 ? bits / 8 : 5;
            if (ver >= 4)
            {
                Dictionary<string, object> cf = f.Dict(f.Get(ed, "CF"));
                string stmf = f.NameOf(ed, "StmF") ?? "Identity", strf = f.NameOf(ed, "StrF") ?? "Identity";
                string method = "None", smethod = "None";
                if (stmf != "Identity" && cf != null) method = f.NameOf(f.Dict(f.Get(cf, stmf)), "CFM") ?? "None";
                if (strf != "Identity" && cf != null) smethod = f.NameOf(f.Dict(f.Get(cf, strf)), "CFM") ?? "None";
                c.identityStreams = method == "None";
                c.aesStreams = method == "AESV2" || method == "AESV3";
                c.identityStrings = smethod == "None";
                c.aesStrings = smethod == "AESV2" || smethod == "AESV3";
                if (method == "AESV2" || smethod == "AESV2") keyLen = 16;
                if ((method == "V2" || smethod == "V2") && bits < 40) keyLen = 16;
            }
            PdfStr o = f.Get(ed, "O") as PdfStr, u = f.Get(ed, "U") as PdfStr;
            if (o == null || u == null) { c.Failed = true; return c; }
            try
            {
                if (c.rev >= 5)
                {
                    PdfStr ue = f.Get(ed, "UE") as PdfStr;
                    c.key = c.rev == 5 ? Key5(u.B, ue != null ? ue.B : null) : Key6(u.B, ue != null ? ue.B : null);
                }
                else
                {
                    object em = f.Get(ed, "EncryptMetadata");
                    bool encMeta = !(em is bool) || (bool)em;
                    int p = unchecked((int)(long)f.Num(ed, "P", 0));
                    c.key = Key4(o.B, p, id0, c.rev, keyLen, encMeta);
                    if (!c.Check(u.B, id0)) c.key = null;
                }
            }
            catch (CryptographicException) { c.key = null; }
            catch (ArgumentException) { c.key = null; }
            if (c.key == null) c.Failed = true;
            return c;
        }

        static byte[] Key4(byte[] o, int p, byte[] id0, int rev, int n, bool encMeta)
        {
            using (MD5 md5 = MD5.Create())
            {
                List<byte> input = new List<byte>(Pad);
                input.AddRange(Take(o, 32));
                input.Add((byte)p); input.Add((byte)(p >> 8)); input.Add((byte)(p >> 16)); input.Add((byte)(p >> 24));
                input.AddRange(id0);
                if (rev >= 4 && !encMeta) input.AddRange(new byte[] { 255, 255, 255, 255 });
                byte[] h = md5.ComputeHash(input.ToArray());
                if (rev >= 3) for (int i = 0; i < 50; i++) h = md5.ComputeHash(h, 0, n);
                return Take(h, n);
            }
        }

        bool Check(byte[] u, byte[] id0)
        {
            if (rev <= 2) return Same(Rc4(key, Pad), u, 32);
            using (MD5 md5 = MD5.Create())
            {
                List<byte> input = new List<byte>(Pad);
                input.AddRange(id0);
                byte[] t = Rc4(key, md5.ComputeHash(input.ToArray()));
                for (int i = 1; i <= 19; i++)
                {
                    byte[] k2 = new byte[key.Length];
                    for (int j = 0; j < key.Length; j++) k2[j] = (byte)(key[j] ^ i);
                    t = Rc4(k2, t);
                }
                return Same(t, u, 16);
            }
        }

        static byte[] Key5(byte[] u, byte[] ue)
        {
            if (u.Length < 48 || ue == null || ue.Length < 32) return null;
            using (SHA256 sha = SHA256.Create())
            {
                if (!Same(sha.ComputeHash(Slice(u, 32, 8)), u, 32)) return null;
                return AesNoPad(sha.ComputeHash(Slice(u, 40, 8)), new byte[16], Take(ue, 32), false);
            }
        }

        static byte[] Key6(byte[] u, byte[] ue)
        {
            if (u.Length < 48 || ue == null || ue.Length < 32) return null;
            if (!Same(Hash2B(Slice(u, 32, 8)), u, 32)) return null;
            return AesNoPad(Hash2B(Slice(u, 40, 8)), new byte[16], Take(ue, 32), false);
        }

        static byte[] Hash2B(byte[] salt)
        {
            byte[] k;
            using (SHA256 s256 = SHA256.Create()) k = s256.ComputeHash(salt);
            byte[] e = null;
            for (int round = 0; round < 64 || e[e.Length - 1] > round - 32; round++)
            {
                byte[] block = Take(k, k.Length);
                byte[] k1 = new byte[block.Length * 64];
                for (int i = 0; i < 64; i++) Buffer.BlockCopy(block, 0, k1, i * block.Length, block.Length);
                e = AesNoPad(Take(k, 16), Slice(k, 16, 16), k1, true);
                int sum = 0;
                for (int i = 0; i < 16; i++) sum += e[i];
                switch (sum % 3)
                {
                    case 0: using (SHA256 h = SHA256.Create()) k = h.ComputeHash(e); break;
                    case 1: using (SHA384 h = SHA384.Create()) k = h.ComputeHash(e); break;
                    default: using (SHA512 h = SHA512.Create()) k = h.ComputeHash(e); break;
                }
                if (round > 4096) break;
            }
            return Take(k, 32);
        }

        static byte[] AesNoPad(byte[] key, byte[] iv, byte[] data, bool encrypt)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                using (ICryptoTransform t = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor())
                    return t.TransformFinalBlock(data, 0, data.Length - data.Length % 16);
            }
        }

        public byte[] Decrypt(byte[] data, int num, int gen, bool stream)
        {
            if (Failed || key == null) return null;
            if (stream ? identityStreams : identityStrings) return data;
            bool aes = stream ? aesStreams : aesStrings;
            byte[] k;
            if (rev >= 5) k = key;
            else
            {
                using (MD5 md5 = MD5.Create())
                {
                    List<byte> input = new List<byte>(key);
                    input.Add((byte)num); input.Add((byte)(num >> 8)); input.Add((byte)(num >> 16));
                    input.Add((byte)gen); input.Add((byte)(gen >> 8));
                    if (aes) input.AddRange(new byte[] { 0x73, 0x41, 0x6C, 0x54 });
                    k = Take(md5.ComputeHash(input.ToArray()), Math.Min(key.Length + 5, 16));
                }
            }
            if (!aes) return Rc4(k, data);
            if (data.Length < 32) return new byte[0];
            int body = (data.Length - 16) - (data.Length - 16) % 16;
            using (Aes a = Aes.Create())
            {
                a.Key = k;
                a.IV = Take(data, 16);
                a.Mode = CipherMode.CBC;
                try
                {
                    a.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform t = a.CreateDecryptor()) return t.TransformFinalBlock(data, 16, body);
                }
                catch (CryptographicException)
                {
                    a.Padding = PaddingMode.None;
                    using (ICryptoTransform t = a.CreateDecryptor()) return t.TransformFinalBlock(data, 16, body);
                }
            }
        }

        static byte[] Rc4(byte[] key, byte[] data)
        {
            byte[] s = new byte[256];
            for (int i = 0; i < 256; i++) s[i] = (byte)i;
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 255;
                byte t = s[i]; s[i] = s[j]; s[j] = t;
            }
            byte[] o = new byte[data.Length];
            for (int n = 0, i = 0, j = 0; n < data.Length; n++)
            {
                i = (i + 1) & 255;
                j = (j + s[i]) & 255;
                byte t = s[i]; s[i] = s[j]; s[j] = t;
                o[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 255]);
            }
            return o;
        }

        static byte[] Take(byte[] a, int n) { return Slice(a, 0, n); }

        static byte[] Slice(byte[] a, int from, int n)
        {
            byte[] r = new byte[Math.Max(0, Math.Min(n, a.Length - from))];
            if (r.Length > 0) Buffer.BlockCopy(a, from, r, 0, r.Length);
            return r;
        }

        static bool Same(byte[] a, byte[] b, int n)
        {
            if (a == null || a.Length < n || b.Length < n) return false;
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }

    class PdfFont
    {
        public readonly Dictionary<int, string> Map = new Dictionary<int, string>();
        public readonly List<int[]> Ranges = new List<int[]>();
        public bool TwoByte, FixedCodes, Unmapped, UcsCMap, Vertical;
        public int Pua;
        public string[] Base;
        public double[] Widths;
        public int FirstChar;
        public double DefaultWidth = 500, WidthScale = 1;
        public Dictionary<int, double> CidWidths;
        public string StdWidths;

        public double Width(int code)
        {
            double w;
            if (CidWidths != null) return (CidWidths.TryGetValue(code, out w) ? w : DefaultWidth) * WidthScale;
            int i = code - FirstChar;
            if (Widths != null && i >= 0 && i < Widths.Length && Widths[i] > 0) return Widths[i] * WidthScale;
            if (StdWidths != null)
            {
                string t = Text(code);
                int sw = t.Length == 1 ? PdfText.StdWidth(StdWidths, t[0]) : 0;
                if (sw > 0) return sw;
            }
            return DefaultWidth * WidthScale;
        }

        public List<int> Codes(byte[] b)
        {
            List<int> codes = new List<int>(b.Length);
            bool ranges = TwoByte && !FixedCodes && Ranges.Count > 0;
            int i = 0;
            while (i < b.Length)
            {
                int len = TwoByte ? 2 : 1;
                if (ranges)
                {
                    len = 0;
                    int code = 0;
                    for (int n = 1; n <= 4 && i + n <= b.Length && len == 0; n++)
                    {
                        code = (code << 8) | b[i + n - 1];
                        foreach (int[] r in Ranges) if (r[2] == n && code >= r[0] && code <= r[1]) { len = n; break; }
                    }
                    if (len == 0) len = 1;
                }
                if (i + len > b.Length) len = b.Length - i;
                int c = 0;
                for (int k = 0; k < len; k++) c = (c << 8) | b[i + k];
                codes.Add(c);
                i += len;
            }
            return codes;
        }

        public string Text(int code)
        {
            string s;
            if (Map.TryGetValue(code, out s)) return s;
            if (UcsCMap) return code >= 32 && code <= 0xFFFF ? ((char)code).ToString() : "";
            if (TwoByte || Unmapped) return "";
            if (Base != null && code >= 0 && code < 256) return Base[code] ?? "";
            return "";
        }
    }

    static class PdfText
    {
        static Dictionary<string, int> aglMap;
        static Dictionary<char, int> winIndex;
        static readonly string[] Ligatures = { "ff", "fi", "fl", "ffi", "ffl", "st", "st" };
        static readonly string[] SizeSuffixes = { "display", "text", "Bigg", "bigg", "Big", "big" };
        static readonly Regex DupRx = new Regex(@"dup\s+(\d{1,3})\s*/([^\s/\[\]{}()<>%]{1,64})\s+put", RegexOptions.CultureInvariant);

        static Dictionary<string, int> AglMap
        {
            get
            {
                Dictionary<string, int> m = aglMap;
                if (m != null) return m;
                m = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (string e in PdfTables.Agl.Split('|'))
                {
                    int sp = e.IndexOf(' ');
                    if (sp > 0) m[e.Substring(0, sp)] = int.Parse(e.Substring(sp + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                aglMap = m;
                return m;
            }
        }

        public static int StdWidth(string table, char c)
        {
            Dictionary<char, int> idx = winIndex;
            if (idx == null)
            {
                idx = new Dictionary<char, int>();
                for (int code = 255; code >= 32; code--)
                    if (code < PdfTables.WinEnc.Length && PdfTables.WinEnc[code] != (char)0) idx[PdfTables.WinEnc[code]] = code;
                winIndex = idx;
            }
            int i;
            if (!idx.TryGetValue(c, out i)) return 0;
            int at = (i - 32) * 3;
            if (at + 3 > table.Length) return 0;
            return int.Parse(table.Substring(at, 3), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        public static string GlyphText(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int dot = name.IndexOf('.');
            if (dot == 0) return null;
            if (dot > 0) name = name.Substring(0, dot);
            if (name.IndexOf('_') > 0)
            {
                StringBuilder parts = new StringBuilder();
                foreach (string part in name.Split('_'))
                {
                    string t = GlyphText(part);
                    if (t == null) return null;
                    parts.Append(t);
                }
                return parts.ToString();
            }
            int cp;
            if (AglMap.TryGetValue(name, out cp)) return char.ConvertFromUtf32(cp);
            foreach (string suffix in SizeSuffixes)
                if (name.Length > suffix.Length + 2 && name.EndsWith(suffix, StringComparison.Ordinal) && AglMap.TryGetValue(name.Substring(0, name.Length - suffix.Length), out cp))
                    return char.ConvertFromUtf32(cp);
            if (name.StartsWith("uni") && name.Length >= 7 && (name.Length - 3) % 4 == 0)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 3; i + 4 <= name.Length; i += 4)
                {
                    int v;
                    if (!int.TryParse(name.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out v) || (v >= 0xD800 && v <= 0xDFFF)) return null;
                    sb.Append((char)v);
                }
                return sb.ToString();
            }
            if (name.Length >= 5 && name.Length <= 7 && name[0] == 'u')
            {
                int v;
                if (int.TryParse(name.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out v) && v > 0 && v < 0x110000 && (v < 0xD800 || v > 0xDFFF)) return char.ConvertFromUtf32(v);
            }
            return null;
        }

        static string CodeGlyph(string name, string[] table)
        {
            int code = -1;
            NumberStyles hex = NumberStyles.AllowHexSpecifier;
            if (name.Length == 3 && name[0] == 'G') int.TryParse(name.Substring(1), hex, CultureInfo.InvariantCulture, out code);
            else if (name.Length == 5 && name[0] == 'g') int.TryParse(name.Substring(1), hex, CultureInfo.InvariantCulture, out code);
            else if ((name[0] == 'C' || name[0] == 'c') && name.Length >= 3 && name.Length <= 4) int.TryParse(name.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out code);
            if (code >= 32 && code < 256 && table[code] != null) return table[code];
            return null;
        }

        static string[] Table(string t)
        {
            string[] r = new string[256];
            for (int i = 0; i < 256 && i < t.Length; i++) if (t[i] != (char)0) r[i] = t[i].ToString();
            return r;
        }

        public static string Extract(byte[] data, StringBuilder sb, int maxChars, CancelToken ct, out int pageCount)
        {
            pageCount = 0;
            int head = PdfFile.Find(data, "%PDF", 0);
            bool header = head >= 0 && head <= 1024;
            if (!header && PdfFile.Find(data, " obj", 0) < 0) return "this is not a PDF file";
            PdfFile f = new PdfFile(data);
            if (f.Crypt != null && f.Crypt.Failed) return "the PDF is locked with a password, so its text can't be read";
            object rootRef;
            f.Trailer.TryGetValue("Root", out rootRef);
            Dictionary<string, object> root = f.Dict(rootRef);
            if (root == null)
            {
                List<Dictionary<string, object>> catalogs = f.Typed("Catalog");
                if (catalogs.Count > 0) root = catalogs[catalogs.Count - 1];
            }
            List<PdfPage> pages = new List<PdfPage>();
            HashSet<Dictionary<string, object>> seen = new HashSet<Dictionary<string, object>>();
            if (root != null) CollectPages(f, f.Get(root, "Pages"), null, null, null, pages, seen, 0);
            if (pages.Count == 0)
                foreach (Dictionary<string, object> pd in f.Typed("Page"))
                    CollectPages(f, pd, null, null, null, pages, seen, 0);
            pageCount = pages.Count;
            if (pages.Count == 0) return header ? "the PDF is damaged (MinGent could not find its pages)" : "this is not a PDF file";
            int start = sb.Length, withText = 0, problems = 0;
            string limit = null;
            PageText pt = new PageText(f, sb, maxChars, ct);
            Dictionary<string, object> form = root != null ? f.Dict(f.Get(root, "AcroForm")) : null;
            object need = form != null ? f.Get(form, "NeedAppearances") : null;
            pt.NeedAppearances = need is bool && (bool)need;
            pt.Hidden = HiddenLayers(f, root);
            for (int i = 0; i < pages.Count; i++)
            {
                if ((ct != null && ct.IsCancelled) || sb.Length > maxChars) break;
                sb.Append("--- Page ").Append(i + 1).Append(" ---\n");
                int before = sb.Length;
                string problem = null;
                bool stop = false;
                try { pt.Run(pages[i]); }
                catch (LimitException ex) { problem = ex.Message + ", so MinGent stopped here"; limit = ex.Message; stop = true; }
                catch (Exception) { problem = "part of this page could not be read"; }
                bool any = false;
                for (int k = before; k < sb.Length && !any; k++) if (!char.IsWhiteSpace(sb[k])) any = true;
                if (any) withText++;
                else sb.Length = before;
                if (problem != null)
                {
                    problems++;
                    if (sb.Length > before && sb[sb.Length - 1] != '\n') sb.Append('\n');
                    sb.Append('(').Append(char.ToUpperInvariant(problem[0])).Append(problem, 1, problem.Length - 1).Append(".)\n");
                }
                else if (!any) sb.Append("(no text on this page)\n");
                if (sb[sb.Length - 1] != '\n') sb.Append('\n');
                sb.Append('\n');
                if (stop) break;
            }
            if (withText > 0) return null;
            sb.Length = start;
            if (limit != null) return limit;
            return problems > 0 ? "MinGent could not read the text in this PDF" : null;
        }

        static HashSet<Dictionary<string, object>> HiddenLayers(PdfFile f, Dictionary<string, object> root)
        {
            HashSet<Dictionary<string, object>> hidden = new HashSet<Dictionary<string, object>>();
            Dictionary<string, object> props = root != null ? f.Dict(f.Get(root, "OCProperties")) : null;
            Dictionary<string, object> d = props != null ? f.Dict(f.Get(props, "D")) : null;
            if (d == null) return hidden;
            if (f.NameOf(d, "BaseState") == "OFF")
            {
                Collect(f, f.Get(props, "OCGs"), hidden, true);
                Collect(f, f.Get(d, "ON"), hidden, false);
            }
            Collect(f, f.Get(d, "OFF"), hidden, true);
            return hidden;
        }

        static void Collect(PdfFile f, object list, HashSet<Dictionary<string, object>> set, bool add)
        {
            List<object> items = list as List<object>;
            if (items == null) return;
            foreach (object o in items)
            {
                Dictionary<string, object> g = f.Dict(o);
                if (g == null) continue;
                if (add) set.Add(g);
                else set.Remove(g);
            }
        }

        static void CollectPages(PdfFile f, object node, Dictionary<string, object> res, double[] media, double[] crop, List<PdfPage> pages, HashSet<Dictionary<string, object>> seen, int depth)
        {
            if (depth > 64 || pages.Count >= 50000) return;
            Dictionary<string, object> d = f.Dict(node);
            if (d == null || !seen.Add(d)) return;
            res = f.Dict(f.Get(d, "Resources")) ?? res;
            media = Rect(f, f.Get(d, "MediaBox")) ?? media;
            crop = Rect(f, f.Get(d, "CropBox")) ?? crop;
            List<object> kids = f.Get(d, "Kids") as List<object>;
            string type = f.NameOf(d, "Type");
            if (type == "Page" || (kids == null && type != "Pages"))
            {
                PdfPage p = new PdfPage();
                p.Dict = d;
                p.Res = res ?? f.Dict(Inherited(f, d, "Resources"));
                p.Box = crop ?? media ?? Rect(f, Inherited(f, d, "CropBox")) ?? Rect(f, Inherited(f, d, "MediaBox"));
                pages.Add(p);
                return;
            }
            if (kids != null)
                foreach (object k in kids) CollectPages(f, k, res, media, crop, pages, seen, depth + 1);
        }

        static object Inherited(PdfFile f, Dictionary<string, object> d, string key)
        {
            for (int i = 0; i < 32 && d != null; i++)
            {
                object v = f.Get(d, key);
                if (v != null) return v;
                d = f.Dict(f.Get(d, "Parent"));
            }
            return null;
        }

        static double[] Rect(PdfFile f, object o)
        {
            List<object> a = o as List<object>;
            if (a == null || a.Count != 4) return null;
            double[] v = new double[4];
            for (int i = 0; i < 4; i++)
            {
                object x = f.Resolve(a[i]);
                if (!(x is double)) return null;
                v[i] = (double)x;
            }
            double[] r = { Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Max(v[0], v[2]), Math.Max(v[1], v[3]) };
            if (r[2] - r[0] < 10 || r[3] - r[1] < 10) return null;
            return r;
        }

        public static PdfFont LoadFont(PdfFile f, Dictionary<string, object> fd)
        {
            PdfFont font = new PdfFont();
            string sub = f.NameOf(fd, "Subtype") ?? "";
            string name = f.NameOf(fd, "BaseFont") ?? "";
            if (name.Contains("Symbol")) font.Pua = 1;
            else if (name.Contains("Wingdings") || name.Contains("Webdings")) font.Pua = 2;
            Dictionary<string, object> cid = null;
            if (sub == "Type0") cid = Composite(f, fd, font);
            else Simple(f, fd, font, sub);
            PdfStream tu = f.Get(fd, "ToUnicode") as PdfStream;
            if (tu != null)
            {
                byte[] cm = CMapBytes(f, tu);
                if (cm != null) ParseCMap(cm, font, false, font.TwoByte && !font.FixedCodes && font.Ranges.Count == 0);
            }
            if (font.TwoByte && !font.UcsCMap && cid != null && (font.Map.Count == 0 || Suspicious(font.Map))) TrueTypeMap(f, cid, font);
            if (font.TwoByte && font.Map.Count == 0 && !font.UcsCMap) font.Unmapped = true;
            return font;
        }

        static Dictionary<string, object> Composite(PdfFile f, Dictionary<string, object> fd, PdfFont font)
        {
            font.TwoByte = true;
            object enc = f.Get(fd, "Encoding");
            PdfName en = enc as PdfName;
            if (en != null)
            {
                font.Vertical = en.V.EndsWith("-V");
                if (en.V.StartsWith("Identity")) font.FixedCodes = true;
                else if (en.V.StartsWith("Uni") && (en.V.Contains("UCS2") || en.V.Contains("UTF16")))
                {
                    font.UcsCMap = true;
                    font.FixedCodes = true;
                }
            }
            else if (enc is PdfStream)
            {
                PdfStream es = (PdfStream)enc;
                font.Vertical = f.Int(es.Dict, "WMode") == 1;
                ParseCMap(CMapBytes(f, es), font, true, true);
            }
            else font.FixedCodes = true;
            List<object> desc = f.Get(fd, "DescendantFonts") as List<object>;
            Dictionary<string, object> cid = desc != null && desc.Count > 0 ? f.Dict(desc[0]) : null;
            if (cid == null) return null;
            font.DefaultWidth = f.Num(cid, "DW", 1000);
            List<object> w = f.Get(cid, "W") as List<object>;
            if (w == null) return cid;
            font.CidWidths = new Dictionary<int, double>();
            for (int i = 0; i < w.Count && font.CidWidths.Count < 200000; )
            {
                object a = f.Resolve(w[i]);
                if (!(a is double)) break;
                int c0 = (int)(double)a;
                object next = i + 1 < w.Count ? f.Resolve(w[i + 1]) : null;
                List<object> arr = next as List<object>;
                if (arr != null)
                {
                    for (int k = 0; k < arr.Count; k++) { object v = f.Resolve(arr[k]); if (v is double) font.CidWidths[c0 + k] = (double)v; }
                    i += 2;
                }
                else if (next is double && i + 2 < w.Count && f.Resolve(w[i + 2]) is double)
                {
                    int c1 = (int)(double)next;
                    double v = (double)f.Resolve(w[i + 2]);
                    for (int c = c0; c <= c1 && c - c0 < 65536; c++) font.CidWidths[c] = v;
                    i += 3;
                }
                else break;
            }
            return cid;
        }

        static void Simple(PdfFile f, Dictionary<string, object> fd, PdfFont font, string sub)
        {
            string baseFont = f.NameOf(fd, "BaseFont") ?? "";
            if (baseFont.Length > 7 && baseFont[6] == '+') baseFont = baseFont.Substring(7);
            Dictionary<string, object> desc = f.Dict(f.Get(fd, "FontDescriptor"));
            string table = baseFont.StartsWith("Symbol") ? PdfTables.SymEnc : sub == "TrueType" ? PdfTables.WinEnc : PdfTables.StdEnc;
            object enc = f.Get(fd, "Encoding");
            PdfName en = enc as PdfName;
            Dictionary<string, object> ed = enc as Dictionary<string, object>;
            if (ed != null) en = f.Get(ed, "BaseEncoding") as PdfName;
            string[] builtIn = null;
            if (en != null)
            {
                if (en.V == "WinAnsiEncoding") table = PdfTables.WinEnc;
                else if (en.V == "MacRomanEncoding") table = PdfTables.MacEnc;
                else if (en.V == "StandardEncoding") table = PdfTables.StdEnc;
            }
            else if (sub == "Type1" || sub == "MMType1") builtIn = Type1Encoding(f, desc);
            font.Base = builtIn ?? Table(table);
            bool dingbats = en == null && builtIn == null && baseFont.Contains("Dingbats");
            if (dingbats)
                for (int i = 0; i < 256; i++) font.Base[i] = Dingbat(i);
            List<object> diff = ed != null ? f.Get(ed, "Differences") as List<object> : null;
            if (diff != null)
            {
                string[] win = Table(PdfTables.WinEnc);
                int code = 0;
                foreach (object o in diff)
                {
                    object v = f.Resolve(o);
                    if (v is double) code = (int)(double)v;
                    else if (v is PdfName)
                    {
                        string glyph = ((PdfName)v).V;
                        if (code >= 0 && code < 256 && glyph.Length > 0 && !(dingbats && glyph.StartsWith("a")))
                            font.Base[code] = GlyphText(glyph) ?? CodeGlyph(glyph, win) ?? Numbered(glyph, code, sub == "Type3", win) ?? "";
                        code++;
                    }
                }
            }
            font.FirstChar = Math.Max(0, f.Int(fd, "FirstChar"));
            List<object> widths = f.Get(fd, "Widths") as List<object>;
            if (widths != null)
            {
                font.Widths = new double[Math.Min(widths.Count, 65536)];
                for (int i = 0; i < font.Widths.Length; i++) { object v = f.Resolve(widths[i]); font.Widths[i] = v is double ? (double)v : 0; }
            }
            else StdMetrics(baseFont, font);
            if (desc != null)
            {
                double mw = f.Num(desc, "MissingWidth", 0), aw = f.Num(desc, "AvgWidth", 0);
                if (mw > 0) font.DefaultWidth = mw;
                else if (aw > 0) font.DefaultWidth = aw;
            }
            if (sub == "Type3")
            {
                List<object> fm = f.Get(fd, "FontMatrix") as List<object>;
                object a = fm != null && fm.Count > 0 ? f.Resolve(fm[0]) : null;
                if (a is double && (double)a != 0) font.WidthScale = Math.Min(1000, Math.Abs((double)a) * 1000);
            }
        }

        static void StdMetrics(string name, PdfFont font)
        {
            string n = name.ToLowerInvariant();
            bool bold = n.Contains("bold") || n.Contains("black") || n.Contains("heavy");
            bool italic = n.Contains("italic") || n.Contains("oblique");
            if (n.Contains("courier") || n.Contains("mono")) font.DefaultWidth = 600;
            else if (n.Contains("helvetica") || n.Contains("arial")) font.StdWidths = bold ? PdfTables.WHelveticaBold : PdfTables.WHelvetica;
            else if (n.Contains("times")) font.StdWidths = bold ? (italic ? PdfTables.WTimesBoldItalic : PdfTables.WTimesBold) : (italic ? PdfTables.WTimesItalic : PdfTables.WTimesRoman);
        }

        static string[] Type1Encoding(PdfFile f, Dictionary<string, object> desc)
        {
            PdfStream ff = desc != null ? f.Get(desc, "FontFile") as PdfStream : null;
            if (ff == null) return null;
            byte[] data = null;
            try { data = f.Decode(ff); }
            catch (LimitException) { throw; }
            catch (Exception) { }
            if (data == null || data.Length < 16) return null;
            int start = 0, len = f.Int(ff.Dict, "Length1");
            if (data[0] == 0x80 && data[1] == 1)
            {
                start = 6;
                len = data[2] | (data[3] << 8) | (data[4] << 16) | (data[5] << 24);
            }
            if (len <= 0 || start + len > data.Length) len = data.Length - start;
            string text = Encoding.ASCII.GetString(data, start, Math.Min(len, 256 * 1024));
            int e = text.IndexOf("/Encoding");
            if (e < 0) return null;
            int stop = text.IndexOf("eexec", e);
            if (stop < 0) stop = text.Length;
            string rest = text.Substring(e + 9, stop - e - 9);
            if (rest.TrimStart().StartsWith("StandardEncoding")) return null;
            string[] enc = new string[256];
            int found = 0;
            foreach (Match m in DupRx.Matches(rest))
            {
                int code = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (code > 255) continue;
                enc[code] = GlyphText(m.Groups[2].Value) ?? "";
                found++;
            }
            return found > 0 ? enc : null;
        }

        static int U16(byte[] b, long i) { return i >= 0 && i + 1 < b.Length ? (b[i] << 8) | b[i + 1] : 0; }
        static long U32(byte[] b, long i) { return i >= 0 && i + 3 < b.Length ? ((long)b[i] << 24) | ((long)b[i + 1] << 16) | ((long)b[i + 2] << 8) | b[i + 3] : 0; }

        static bool Bogus(string t)
        {
            return t.Length == 0 || (t.Length == 1 && (t[0] == '?' || t[0] == (char)0 || t[0] == (char)0xFFFD));
        }

        static bool Suspicious(Dictionary<int, string> map)
        {
            foreach (string t in map.Values) if (Bogus(t)) return true;
            return false;
        }

        static void Learn(PdfFont font, int code, string text)
        {
            string old;
            if (!font.Map.TryGetValue(code, out old) || Bogus(old)) font.Map[code] = text;
        }

        static void TrueTypeMap(PdfFile f, Dictionary<string, object> cid, PdfFont font)
        {
            Dictionary<string, object> desc = f.Dict(f.Get(cid, "FontDescriptor"));
            PdfStream ff = desc != null ? f.Get(desc, "FontFile2") as PdfStream : null;
            if (ff == null) return;
            byte[] ttf = null;
            try { ttf = f.Decode(ff); }
            catch (LimitException) { throw; }
            catch (Exception) { }
            if (ttf == null) return;
            Dictionary<int, string> glyphs = GlyphUnicode(ttf);
            if (glyphs == null || glyphs.Count == 0) return;
            PdfStream map = f.Get(cid, "CIDToGIDMap") as PdfStream;
            if (map == null)
            {
                foreach (KeyValuePair<int, string> kv in glyphs) Learn(font, kv.Key, kv.Value);
                return;
            }
            byte[] m = null;
            try { m = f.Decode(map); }
            catch (LimitException) { throw; }
            catch (Exception) { }
            if (m == null) return;
            for (int c = 0; c * 2 + 1 < m.Length && c < 65536; c++)
            {
                int g = (m[c * 2] << 8) | m[c * 2 + 1];
                string t;
                if (g != 0 && glyphs.TryGetValue(g, out t)) Learn(font, c, t);
            }
        }

        static Dictionary<int, string> GlyphUnicode(byte[] ttf)
        {
            int tables = U16(ttf, 4);
            long cmap = -1;
            for (int i = 0; i < tables && i < 512; i++)
            {
                long rec = 12 + i * 16L;
                if (rec + 16 > ttf.Length) return null;
                if (ttf[rec] == 99 && ttf[rec + 1] == 109 && ttf[rec + 2] == 97 && ttf[rec + 3] == 112) cmap = U32(ttf, rec + 8);
            }
            if (cmap < 0 || cmap + 4 > ttf.Length) return null;
            int subs = U16(ttf, cmap + 2), bestScore = 0;
            long best = -1;
            for (int i = 0; i < subs && i < 256; i++)
            {
                long r = cmap + 4 + i * 8L;
                int pid = U16(ttf, r), eid = U16(ttf, r + 2);
                int score = pid == 3 && eid == 10 ? 4 : pid == 3 && eid == 1 ? 3 : pid == 0 ? 2 : 0;
                if (score > bestScore) { bestScore = score; best = cmap + U32(ttf, r + 4); }
            }
            if (best < 0 || best + 4 > ttf.Length) return null;
            Dictionary<int, string> map = new Dictionary<int, string>();
            int fmt = U16(ttf, best);
            if (fmt == 4)
            {
                int segs = U16(ttf, best + 6) / 2;
                long ends = best + 14, starts = ends + segs * 2 + 2, deltas = starts + segs * 2, offs = deltas + segs * 2;
                for (int s = 0; s < segs; s++)
                {
                    int last = U16(ttf, ends + s * 2), first = U16(ttf, starts + s * 2), delta = U16(ttf, deltas + s * 2), ro = U16(ttf, offs + s * 2);
                    for (int c = first; c <= last && c < 0xFFFF; c++)
                    {
                        int g;
                        if (ro == 0) g = (c + delta) & 0xFFFF;
                        else
                        {
                            long ga = offs + s * 2 + ro + (c - first) * 2L;
                            if (ga + 2 > ttf.Length) break;
                            g = U16(ttf, ga);
                            if (g != 0) g = (g + delta) & 0xFFFF;
                        }
                        if (g != 0 && (c < 0xD800 || c > 0xDFFF) && !map.ContainsKey(g)) map[g] = ((char)c).ToString();
                    }
                }
            }
            else if (fmt == 12)
            {
                long groups = U32(ttf, best + 12);
                for (long gi = 0; gi < groups && gi < 65536; gi++)
                {
                    long r = best + 16 + gi * 12;
                    if (r + 12 > ttf.Length) break;
                    long sc = U32(ttf, r), ec = U32(ttf, r + 4), sg = U32(ttf, r + 8);
                    for (long c = sc; c <= ec && c - sc < 65536 && c < 0x110000; c++)
                    {
                        int g = (int)(sg + c - sc);
                        if (g != 0 && (c < 0xD800 || c > 0xDFFF) && !map.ContainsKey(g)) map[g] = char.ConvertFromUtf32((int)c);
                    }
                }
            }
            return map;
        }

        static void ParseCMap(byte[] data, PdfFont font, bool rangesOnly, bool takeRanges)
        {
            if (data == null) return;
            PdfLexer lx = new PdfLexer(data, 0, data.Length);
            lx.Content = true;
            List<object> ops = new List<object>();
            int guard = 0;
            while (!lx.AtEnd && guard++ < 2000000)
            {
                object o;
                try { o = lx.Read(); }
                catch (InvalidDataException) { break; }
                PdfOp op = o as PdfOp;
                if (op == null) { if (o != null && ops.Count < 300000) ops.Add(o); continue; }
                switch (op.V)
                {
                    case "endcodespacerange":
                        if (takeRanges)
                            for (int i = 0; i + 1 < ops.Count; i += 2)
                            {
                                PdfStr a = ops[i] as PdfStr, b = ops[i + 1] as PdfStr;
                                if (a == null || b == null || a.B.Length == 0 || a.B.Length > 4 || font.Ranges.Count > 1000) continue;
                                font.Ranges.Add(new int[] { Code(a.B), Code(b.B), a.B.Length });
                            }
                        break;
                    case "endbfchar":
                        if (!rangesOnly)
                            for (int i = 0; i + 1 < ops.Count; i += 2)
                            {
                                PdfStr a = ops[i] as PdfStr;
                                string t = a != null ? Dst(ops[i + 1]) : null;
                                if (t != null) font.Map[Code(a.B)] = t;
                            }
                        break;
                    case "endbfrange":
                        if (!rangesOnly)
                            for (int i = 0; i + 2 < ops.Count; i += 3)
                            {
                                PdfStr a = ops[i] as PdfStr, b = ops[i + 1] as PdfStr;
                                if (a == null || b == null) continue;
                                int lo = Code(a.B), hi = Code(b.B);
                                if (hi < lo || hi - lo > 65535) continue;
                                List<object> arr = ops[i + 2] as List<object>;
                                if (arr != null)
                                {
                                    for (int k = 0; k < arr.Count && lo + k <= hi; k++) { string t = Dst(arr[k]); if (t != null) font.Map[lo + k] = t; }
                                    continue;
                                }
                                PdfStr dst = ops[i + 2] as PdfStr;
                                if (dst == null || dst.B.Length == 0) continue;
                                for (int c = lo; c <= hi; c++)
                                {
                                    byte[] v = (byte[])dst.B.Clone();
                                    int add = c - lo;
                                    for (int k = v.Length - 1; k >= 0 && add > 0; k--)
                                    {
                                        int s = v[k] + (add & 0xFF);
                                        v[k] = (byte)s;
                                        add = (add >> 8) + (s >> 8);
                                    }
                                    font.Map[c] = Utf16(v);
                                }
                            }
                        break;
                }
                ops.Clear();
            }
        }

        static int Code(byte[] b)
        {
            int c = 0;
            for (int i = 0; i < b.Length && i < 4; i++) c = (c << 8) | b[i];
            return c;
        }

        static string Dst(object o)
        {
            PdfStr s = o as PdfStr;
            if (s != null) return Utf16(s.B);
            PdfName n = o as PdfName;
            if (n != null) return GlyphText(n.V);
            return null;
        }

        static string Utf16(byte[] b)
        {
            if (b.Length == 1) return ((char)b[0]).ToString();
            StringBuilder sb = new StringBuilder(b.Length / 2);
            for (int i = 0; i + 1 < b.Length; i += 2) sb.Append((char)((b[i] << 8) | b[i + 1]));
            return sb.ToString();
        }

        public static void Append(StringBuilder sb, string t, int pua)
        {
            foreach (char ch in t)
            {
                if (pua != 0 && ch >= 0xF020 && ch <= 0xF0FF)
                {
                    string m = Pua(ch - 0xF000, pua);
                    if (m != null) { sb.Append(m); continue; }
                }
                if (ch >= 0xFB00 && ch <= 0xFB06) { sb.Append(Ligatures[ch - 0xFB00]); continue; }
                if (ch != 9 && (ch < 32 || (ch >= 127 && ch <= 159))) continue;
                sb.Append(ch);
            }
        }

        public static bool Rtl(char c)
        {
            if ((c >= 0x0660 && c <= 0x0669) || (c >= 0x06F0 && c <= 0x06F9)) return false;
            return (c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF);
        }

        public static bool Mark(char c)
        {
            UnicodeCategory k = char.GetUnicodeCategory(c);
            return k == UnicodeCategory.NonSpacingMark || k == UnicodeCategory.EnclosingMark;
        }

        static int Kind(string u)
        {
            char c = u[0];
            if (char.IsDigit(c)) return 2;
            if (Rtl(c)) return 1;
            if (char.IsLetter(u, 0)) return 0;
            return 3;
        }

        static void Reverse(List<string> u, int from, int to)
        {
            for (int i = from, j = to; i < j; i++, j--)
            {
                string t = u[i];
                u[i] = u[j];
                u[j] = t;
            }
        }

        static string Mirror(string s)
        {
            if (s.Length != 1) return s;
            switch (s[0])
            {
                case '(': return ")";
                case ')': return "(";
                case '[': return "]";
                case ']': return "[";
                case '{': return "}";
                case '}': return "{";
                case '<': return ">";
                case '>': return "<";
            }
            if (s[0] == (char)0xAB) return ((char)0xBB).ToString();
            if (s[0] == (char)0xBB) return ((char)0xAB).ToString();
            return s;
        }

        public static void Logical(List<string> u)
        {
            int n = u.Count, right = 0, left = 0;
            int[] kind = new int[n];
            for (int i = 0; i < n; i++)
            {
                kind[i] = Kind(u[i]);
                if (kind[i] == 1) right++;
                else if (kind[i] == 0) left++;
            }
            if (right == 0) return;
            if (right >= left)
            {
                Reverse(u, 0, n - 1);
                Array.Reverse(kind);
                bool[] keep = new bool[n];
                int i = 0;
                while (i < n)
                {
                    if (kind[i] != 0 && kind[i] != 2) { i++; continue; }
                    int j = i, last = i;
                    while (j < n && kind[j] != 1) { if (kind[j] == 0 || kind[j] == 2) last = j; j++; }
                    Reverse(u, i, last);
                    for (int k = i; k <= last; k++) keep[k] = true;
                    i = last + 1;
                }
                for (int k = 0; k < n; k++) if (!keep[k] && kind[k] == 3) u[k] = Mirror(u[k]);
                return;
            }
            int p = 0;
            while (p < n)
            {
                if (kind[p] == 0) { p++; continue; }
                int q = p;
                bool hasRight = false;
                while (q < n && kind[q] != 0) { if (kind[q] == 1) hasRight = true; q++; }
                int s0 = p, s1 = q - 1;
                p = q;
                if (!hasRight) continue;
                while (s0 <= s1 && kind[s0] == 3) s0++;
                while (s1 >= s0 && kind[s1] == 3) s1--;
                int[] sub = new int[s1 - s0 + 1];
                Array.Copy(kind, s0, sub, 0, sub.Length);
                Reverse(u, s0, s1);
                Array.Reverse(sub);
                for (int k = 0; k < sub.Length; k++)
                {
                    if (sub[k] == 3) u[s0 + k] = Mirror(u[s0 + k]);
                    if (sub[k] != 2) continue;
                    int e = k;
                    while (e + 1 < sub.Length && (sub[e + 1] == 2 || (sub[e + 1] == 3 && e + 2 < sub.Length && sub[e + 2] == 2 && u[s0 + e + 1] != " "))) e++;
                    Reverse(u, s0 + k, s0 + e);
                    k = e;
                }
            }
        }

        public static void Unshape(StringBuilder sb, int from)
        {
            for (int i = from; i < sb.Length; i++)
            {
                char c = sb[i];
                if ((c < 0xFB50 || c > 0xFDFF) && (c < 0xFE70 || c > 0xFEFF)) continue;
                string plain = c.ToString().Normalize(NormalizationForm.FormKC);
                if (plain.Length == 1 && plain[0] == c) continue;
                sb.Remove(i, 1);
                sb.Insert(i, plain);
                i += plain.Length - 1;
            }
        }

        public static double[] Box(PdfFile f, object o)
        {
            List<object> a = o as List<object>;
            if (a == null || a.Count != 4) return null;
            double[] v = new double[4];
            for (int i = 0; i < 4; i++)
            {
                object x = f.Resolve(a[i]);
                if (!(x is double) || double.IsNaN((double)x) || double.IsInfinity((double)x)) return null;
                v[i] = (double)x;
            }
            double[] r = { Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Max(v[0], v[2]), Math.Max(v[1], v[3]) };
            if (r[2] - r[0] <= 0.01 || r[3] - r[1] <= 0.01) return null;
            return r;
        }

        static string Dingbat(int code)
        {
            if (code == 32) return " ";
            if (code < 0x21 || code > 0x7E) return "";
            int cp;
            switch (code)
            {
                case 0x25: cp = 0x260E; break;
                case 0x2A: cp = 0x261B; break;
                case 0x2B: cp = 0x261E; break;
                case 0x48: cp = 0x2605; break;
                case 0x6C: cp = 0x25CF; break;
                case 0x6E: cp = 0x25A0; break;
                case 0x73: cp = 0x25B2; break;
                case 0x74: cp = 0x25BC; break;
                case 0x75: cp = 0x25C6; break;
                case 0x77: cp = 0x25D7; break;
                default: cp = 0x2700 + code - 0x20; break;
            }
            return ((char)cp).ToString();
        }

        static readonly int[] Ot1Low = {
            0x393, 0x394, 0x398, 0x39B, 0x39E, 0x3A0, 0x3A3, 0x3A5, 0x3A6, 0x3A8, 0x3A9, -1, -2, -3, -4, -5,
            0x131, 0x237, 0x60, 0xB4, 0x2C7, 0x2D8, 0xAF, 0x2DA, 0xB8, 0xDF, 0xE6, 0x153, 0xF8, 0xC6, 0x152, 0xD8 };

        static string Ot1(int code)
        {
            if (code < 32)
            {
                int v = Ot1Low[code];
                return v < 0 ? Ligatures[-v - 1] : ((char)v).ToString();
            }
            switch (code)
            {
                case 0x20: return "";
                case 0x22: return ((char)0x201D).ToString();
                case 0x27: return ((char)0x2019).ToString();
                case 0x3C: return ((char)0xA1).ToString();
                case 0x3E: return ((char)0xBF).ToString();
                case 0x5C: return ((char)0x201C).ToString();
                case 0x5E: return ((char)0x2C6).ToString();
                case 0x5F: return ((char)0x2D9).ToString();
                case 0x60: return ((char)0x2018).ToString();
                case 0x7B: return ((char)0x2013).ToString();
                case 0x7C: return ((char)0x2014).ToString();
                case 0x7D: return ((char)0x2DD).ToString();
                case 0x7E: return ((char)0x2DC).ToString();
                case 0x7F: return ((char)0xA8).ToString();
            }
            return code < 0x7F ? ((char)code).ToString() : null;
        }

        static string Numbered(string name, int code, bool type3, string[] win)
        {
            int i = 0;
            while (i < name.Length && i < 2 && char.IsLetter(name[i])) i++;
            if (i == 0 || i == name.Length || name.Length - i > 3) return null;
            int v;
            if (!int.TryParse(name.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out v) || v != code) return null;
            if (type3 && code < 128) return Ot1(code);
            return code >= 32 && code < 256 ? win[code] : null;
        }

        static bool Has(byte[] data, string text)
        {
            return PdfFile.Find(data, text, 0) >= 0;
        }

        static byte[] CMapBytes(PdfFile f, PdfStream s)
        {
            byte[] data = null;
            try { data = f.Decode(s); }
            catch (LimitException) { throw; }
            catch (Exception) { }
            if (data != null && data.Length > 2 && data[0] == 0x78 && !Has(data, "begin"))
            {
                try
                {
                    byte[] inner = PdfFilters.Apply("FlateDecode", data, null, f);
                    if (inner != null && Has(inner, "begin")) data = inner;
                }
                catch (LimitException) { throw; }
                catch (Exception) { }
            }
            return data;
        }

        public static string Combining(string t)
        {
            if (t == null || t.Length != 1) return null;
            int mark;
            switch ((int)t[0])
            {
                case 0x60: case 0x2CB: mark = 0x300; break;
                case 0xB4: case 0x2CA: mark = 0x301; break;
                case 0x2C6: mark = 0x302; break;
                case 0x2DC: mark = 0x303; break;
                case 0xAF: case 0x2C9: mark = 0x304; break;
                case 0x2D8: mark = 0x306; break;
                case 0x2D9: mark = 0x307; break;
                case 0xA8: mark = 0x308; break;
                case 0x2DA: mark = 0x30A; break;
                case 0x2DD: mark = 0x30B; break;
                case 0x2C7: mark = 0x30C; break;
                case 0xB8: mark = 0x327; break;
                case 0x2DB: mark = 0x328; break;
                default: return null;
            }
            return ((char)mark).ToString();
        }

        public static string Pua(int code, int kind)
        {
            if (kind == 1)
            {
                char m = code < PdfTables.SymEnc.Length ? PdfTables.SymEnc[code] : (char)0;
                return m >= 32 ? m.ToString() : null;
            }
            switch (code)
            {
                case 0x28: return char.ConvertFromUtf32(0x260E);
                case 0x2A: return char.ConvertFromUtf32(0x2709);
                case 0xFB: return char.ConvertFromUtf32(0x2717);
                case 0xFC: return char.ConvertFromUtf32(0x2713);
                case 0xFD: return char.ConvertFromUtf32(0x2612);
                case 0xFE: return char.ConvertFromUtf32(0x2611);
            }
            return char.ConvertFromUtf32(0x2022);
        }
    }

    class PdfPage
    {
        public Dictionary<string, object> Dict, Res;
        public double[] Box;
    }

    class PdfSpan
    {
        public string Text;
        public double X, Y, EndX, EndY, Ux, Uy, Unit;
        public float[] CS, CE;
    }

    struct PdfGlyph
    {
        public string Text;
        public double Start, End;
        public int Span;
        public bool Last;
    }

    class PageText
    {
        readonly PdfFile f;
        readonly Dictionary<object, PdfFont> fonts = new Dictionary<object, PdfFont>();
        readonly StringBuilder sb;
        readonly int max;
        readonly CancelToken ct;
        readonly List<PdfSpan> spans = new List<PdfSpan>(), extra = new List<PdfSpan>();
        readonly List<string> notes = new List<string>();
        readonly StringBuilder piece = new StringBuilder();
        readonly List<float> cs = new List<float>(), ce = new List<float>();
        readonly Stack<object[]> stack = new Stack<object[]>();
        readonly HashSet<PdfStream> forms = new HashSet<PdfStream>();
        double[] ctm, tm, tlm, box;
        PdfFont font;
        double size, leading, charSpace, wordSpace, scale;
        int formDepth, pending;
        long ops;
        bool inAnnot;
        int marked, hideFrom = -1;
        public bool NeedAppearances;
        public HashSet<Dictionary<string, object>> Hidden = new HashSet<Dictionary<string, object>>();
        static PdfFont fallback;
        const long MaxOps = 25000000;
        static readonly string[] CommentTypes = { "Text", "Highlight", "Underline", "StrikeOut", "Squiggly", "Caret", "Square", "Circle", "Line", "Polygon", "PolyLine", "Ink" };

        public PageText(PdfFile f, StringBuilder sb, int max, CancelToken ct)
        {
            this.f = f;
            this.sb = sb;
            this.max = max;
            this.ct = ct;
        }

        void Reset()
        {
            ctm = new double[] { 1, 0, 0, 1, 0, 0 };
            tm = new double[] { 1, 0, 0, 1, 0, 0 };
            tlm = tm;
            font = null;
            size = 12;
            leading = charSpace = wordSpace = 0;
            scale = 1;
        }

        public void Run(PdfPage page)
        {
            Reset();
            formDepth = 0;
            marked = 0;
            hideFrom = -1;
            stack.Clear();
            forms.Clear();
            pending = 0;
            box = page.Box;
            spans.Clear();
            extra.Clear();
            notes.Clear();
            inAnnot = false;
            try
            {
                object contents = f.Get(page.Dict, "Contents");
                List<object> parts = contents as List<object>;
                if (parts == null && contents != null) parts = new List<object>(new object[] { contents });
                if (parts != null)
                    using (MemoryStream all = new MemoryStream())
                    {
                        foreach (object p in parts)
                        {
                            PdfStream s = f.Resolve(p) as PdfStream;
                            if (s == null) continue;
                            byte[] b = f.Decode(s);
                            if (b == null) continue;
                            all.Write(b, 0, b.Length);
                            all.WriteByte(10);
                            if (all.Length > 64L * 1024 * 1024) break;
                        }
                        Exec(all.ToArray(), page.Res);
                    }
                Annotations(page);
            }
            finally
            {
                inAnnot = false;
                Flush();
                foreach (string n in notes) sb.Append(n).Append('\n');
            }
        }

        void Annotations(PdfPage page)
        {
            List<object> annots = f.Get(page.Dict, "Annots") as List<object>;
            if (annots == null) return;
            inAnnot = true;
            int count = 0;
            foreach (object raw in annots)
            {
                if (++count > 3000 || sb.Length + pending > max) break;
                Dictionary<string, object> a = f.Dict(raw);
                if (a == null) continue;
                PdfRef owner = raw as PdfRef;
                string sub = f.NameOf(a, "Subtype") ?? "";
                if (sub == "Link" || sub == "Popup") continue;
                int flags = f.Int(a, "F");
                if (flags > 0 && (flags & 34) != 0) continue;
                if (a.ContainsKey("OC") && !Visible(a["OC"], 0)) continue;
                double[] rect = PdfText.Box(f, f.Get(a, "Rect"));
                if (sub == "Widget")
                {
                    if (rect != null) Widget(a, owner, rect, page.Res);
                    continue;
                }
                PdfStream ap = Appearance(a);
                int before = extra.Count;
                if (ap != null && rect != null) Draw(ap, rect, page.Res);
                if (sub == "FreeText" && extra.Count == before && rect != null)
                {
                    string text = Clean(f.Text(a, "Contents", owner), 4000);
                    if (text.Length > 0) Synth(text, rect);
                }
                if (Array.IndexOf(CommentTypes, sub) >= 0 && notes.Count < 500) Comment(a, owner);
            }
        }

        void Comment(Dictionary<string, object> a, PdfRef owner)
        {
            string text = Clean(f.Text(a, "Contents", owner), 4000);
            if (text.Length == 0) return;
            string who = Clean(f.Text(a, "T", owner), 100);
            string note = "(Comment" + (who.Length > 0 ? " by " + who : "") + ": " + text + ")";
            if (notes.Contains(note)) return;
            notes.Add(note);
            pending += note.Length;
        }

        static string Clean(string s, int limit)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder b = new StringBuilder();
            foreach (char c in s)
            {
                if (b.Length >= limit) { b.Append("..."); break; }
                if (c == '\r' || c == '\n' || c == '\t') { if (b.Length > 0 && b[b.Length - 1] != ' ') b.Append(' '); continue; }
                if (c < 32 || (c >= 127 && c <= 159)) continue;
                b.Append(c);
            }
            return b.ToString().Trim();
        }

        PdfStream Appearance(Dictionary<string, object> a)
        {
            Dictionary<string, object> ap = f.Dict(f.Get(a, "AP"));
            if (ap == null) return null;
            object n = f.Get(ap, "N");
            PdfStream s = n as PdfStream;
            if (s != null) return s;
            Dictionary<string, object> states = n as Dictionary<string, object>;
            string state = f.NameOf(a, "AS");
            if (states == null || state == null) return null;
            return f.Get(states, state) as PdfStream;
        }

        object Field(Dictionary<string, object> a, string key)
        {
            Dictionary<string, object> d = a;
            for (int i = 0; i < 32 && d != null; i++)
            {
                object v = f.Get(d, key);
                if (v != null) return v;
                d = f.Dict(f.Get(d, "Parent"));
            }
            return null;
        }

        void Widget(Dictionary<string, object> a, PdfRef owner, double[] rect, Dictionary<string, object> res)
        {
            PdfName type = Field(a, "FT") as PdfName;
            string ft = type != null ? type.V : "";
            object ffo = Field(a, "Ff");
            int ff = ffo is double ? (int)(double)ffo : 0;
            if (ft == "Btn" && (ff & 65536) == 0)
            {
                string state = f.NameOf(a, "AS");
                if (state == null)
                {
                    PdfName v = Field(a, "V") as PdfName;
                    state = v != null ? v.V : "Off";
                }
                bool on = state != "Off", radio = (ff & 32768) != 0;
                int mark = radio ? (on ? 0x25C9 : 0x25CB) : (on ? 0x2611 : 0x2610);
                Synth(((char)mark).ToString(), rect);
                return;
            }
            if ((ft == "Tx" || ft == "Ch") && NeedAppearances)
            {
                if (Value(a, owner, rect)) return;
            }
            PdfStream ap = Appearance(a);
            if (ap != null)
            {
                int before = extra.Count;
                Draw(ap, rect, res);
                if (extra.Count > before || (ft != "Tx" && ft != "Ch")) return;
            }
            if (ft == "Tx" || ft == "Ch") Value(a, owner, rect);
        }

        bool Value(Dictionary<string, object> a, PdfRef owner, double[] rect)
        {
            object v = Field(a, "V");
            string text = null;
            List<object> many = v as List<object>;
            if (many != null)
            {
                List<string> parts = new List<string>();
                foreach (object o in many) { string t = f.Text(o, owner); if (!string.IsNullOrEmpty(t)) parts.Add(t); }
                text = string.Join(", ", parts.ToArray());
            }
            else if (v != null) text = f.Text(v, owner);
            text = Clean(text, 4000);
            if (text.Length == 0) return false;
            Synth(text, rect);
            return true;
        }

        void Synth(string text, double[] rect)
        {
            double h = rect[3] - rect[1];
            PdfSpan sp = new PdfSpan();
            sp.Text = text;
            sp.Unit = Math.Max(4, Math.Min(12, h * 0.6));
            sp.X = rect[0] + 2;
            sp.Y = rect[1] + Math.Max(0, (h - sp.Unit) / 2);
            sp.Ux = 1;
            sp.Uy = 0;
            double w = sp.Unit * 0.5;
            sp.CS = new float[text.Length];
            sp.CE = new float[text.Length];
            for (int i = 0; i < text.Length; i++) { sp.CS[i] = (float)(i * w); sp.CE[i] = (float)((i + 1) * w); }
            sp.EndX = sp.X + text.Length * w;
            sp.EndY = sp.Y;
            if (extra.Count < 100000) extra.Add(sp);
            pending += text.Length;
        }

        void Draw(PdfStream ap, double[] rect, Dictionary<string, object> res)
        {
            if (!forms.Add(ap)) return;
            byte[] data = null;
            try { data = f.Decode(ap); }
            catch (LimitException) { throw; }
            catch (Exception) { }
            if (data == null || data.Length == 0) { forms.Remove(ap); return; }
            double[] bbox = PdfText.Box(f, f.Get(ap.Dict, "BBox")) ?? rect;
            double[] mx = { 1, 0, 0, 1, 0, 0 };
            List<object> ml = f.Get(ap.Dict, "Matrix") as List<object>;
            if (ml != null && ml.Count == 6)
                for (int i = 0; i < 6; i++) { object v = f.Resolve(ml[i]); if (v is double && !Bad((double)v)) mx[i] = (double)v; }
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            for (int i = 0; i < 4; i++)
            {
                double px = i % 2 == 0 ? bbox[0] : bbox[2], py = i < 2 ? bbox[1] : bbox[3];
                double tx = px * mx[0] + py * mx[2] + mx[4], ty = px * mx[1] + py * mx[3] + mx[5];
                x0 = Math.Min(x0, tx); y0 = Math.Min(y0, ty); x1 = Math.Max(x1, tx); y1 = Math.Max(y1, ty);
            }
            double sx = x1 - x0 > 0.001 ? (rect[2] - rect[0]) / (x1 - x0) : 1, sy = y1 - y0 > 0.001 ? (rect[3] - rect[1]) / (y1 - y0) : 1;
            double[] fit = { sx, 0, 0, sy, rect[0] - x0 * sx, rect[1] - y0 * sy };
            object[] saved = State(true);
            int stackDepth = stack.Count;
            Reset();
            ctm = Mul(mx, fit);
            try { Exec(data, f.Dict(f.Get(ap.Dict, "Resources")) ?? res); }
            finally
            {
                while (stack.Count > stackDepth) stack.Pop();
                Restore(saved);
                forms.Remove(ap);
            }
        }

        static double[] Mul(double[] a, double[] b)
        {
            return new double[] {
                a[0] * b[0] + a[1] * b[2], a[0] * b[1] + a[1] * b[3],
                a[2] * b[0] + a[3] * b[2], a[2] * b[1] + a[3] * b[3],
                a[4] * b[0] + a[5] * b[2] + b[4], a[4] * b[1] + a[5] * b[3] + b[5] };
        }

        static double[] Move(double tx, double ty) { return new double[] { 1, 0, 0, 1, tx, ty }; }

        static double Num(List<object> args, int i)
        {
            if (i < 0 || i >= args.Count) return 0;
            object v = args[i];
            return v is double && !Bad((double)v) ? (double)v : 0;
        }

        static double[] Matrix(List<object> args, int n)
        {
            return new double[] { Num(args, n - 6), Num(args, n - 5), Num(args, n - 4), Num(args, n - 3), Num(args, n - 2), Num(args, n - 1) };
        }

        void Exec(byte[] data, Dictionary<string, object> res)
        {
            PdfLexer lx = new PdfLexer(data, 0, data.Length);
            lx.Content = true;
            List<object> args = new List<object>();
            Dictionary<string, object> fontRes = f.Dict(f.Get(res, "Font"));
            Dictionary<string, object> xobjs = f.Dict(f.Get(res, "XObject"));
            while (!lx.AtEnd)
            {
                if (++ops > MaxOps) throw new LimitException("the PDF has more drawing commands than MinGent reads");
                if ((ops & 0xFFFF) == 0 && ct != null && ct.IsCancelled) return;
                if (sb.Length + pending > max) return;
                object o = lx.Read();
                PdfOp op = o as PdfOp;
                if (op == null)
                {
                    if (args.Count < 100000) args.Add(o);
                    continue;
                }
                int n = args.Count;
                switch (op.V)
                {
                    case "BT": tm = Move(0, 0); tlm = tm; break;
                    case "Tf":
                        {
                            size = Num(args, n - 1);
                            PdfName fn = n >= 2 ? args[n - 2] as PdfName : null;
                            font = fn != null ? Font(fontRes, fn.V) : null;
                            break;
                        }
                    case "Td": tlm = Mul(Move(Num(args, n - 2), Num(args, n - 1)), tlm); tm = tlm; break;
                    case "TD": leading = -Num(args, n - 1); tlm = Mul(Move(Num(args, n - 2), Num(args, n - 1)), tlm); tm = tlm; break;
                    case "Tm": tlm = Matrix(args, n); tm = tlm; break;
                    case "T*": NextLine(); break;
                    case "TL": leading = Num(args, n - 1); break;
                    case "Tc": charSpace = Num(args, n - 1); break;
                    case "Tw": wordSpace = Num(args, n - 1); break;
                    case "Tz": scale = Num(args, n - 1) / 100.0; break;
                    case "Tj": if (n >= 1) Show(args[n - 1] as PdfStr); break;
                    case "'": NextLine(); if (n >= 1) Show(args[n - 1] as PdfStr); break;
                    case "\"": wordSpace = Num(args, n - 3); charSpace = Num(args, n - 2); NextLine(); if (n >= 1) Show(args[n - 1] as PdfStr); break;
                    case "TJ":
                        {
                            List<object> arr = n >= 1 ? args[n - 1] as List<object> : null;
                            if (arr == null) break;
                            bool vertical = font != null && font.Vertical;
                            foreach (object item in arr)
                            {
                                if (item is PdfStr) Show((PdfStr)item);
                                else if (item is double && !Bad((double)item))
                                {
                                    double adj = -(double)item / 1000.0 * size;
                                    tm = Mul(vertical ? Move(0, adj) : Move(adj * scale, 0), tm);
                                }
                            }
                            break;
                        }
                    case "cm": ctm = Mul(Matrix(args, n), ctm); break;
                    case "BMC": marked++; break;
                    case "BDC":
                        {
                            marked++;
                            PdfName tag = n >= 2 ? args[n - 2] as PdfName : null;
                            if (hideFrom >= 0 || tag == null || tag.V != "OC") break;
                            object prop = args[n - 1];
                            PdfName pn = prop as PdfName;
                            if (pn != null)
                            {
                                Dictionary<string, object> props = f.Dict(f.Get(res, "Properties"));
                                prop = props != null && props.ContainsKey(pn.V) ? props[pn.V] : null;
                            }
                            if (prop != null && !Visible(prop, 0)) hideFrom = marked;
                            break;
                        }
                    case "EMC":
                        if (hideFrom >= 0 && marked <= hideFrom) hideFrom = -1;
                        if (marked > 0) marked--;
                        break;
                    case "q": if (stack.Count < 256) stack.Push(State(false)); break;
                    case "Q": if (stack.Count > 0) Restore(stack.Pop()); break;
                    case "Do":
                        {
                            PdfName xn = n >= 1 ? args[n - 1] as PdfName : null;
                            PdfStream xs = xn != null && xobjs != null ? f.Get(xobjs, xn.V) as PdfStream : null;
                            if (xs == null || hideFrom >= 0 || f.NameOf(xs.Dict, "Subtype") != "Form" || formDepth >= 12) break;
                            if (xs.Dict.ContainsKey("OC") && !Visible(xs.Dict["OC"], 0)) break;
                            if (forms.Add(xs)) Form(xs, res);
                            break;
                        }
                    case "BI":
                        while (!lx.AtEnd)
                        {
                            PdfOp to = lx.Read() as PdfOp;
                            if (to != null && to.V == "ID") { lx.SkipInlineImage(); break; }
                        }
                        break;
                }
                args.Clear();
            }
        }

        void Form(PdfStream xs, Dictionary<string, object> res)
        {
            byte[] data = null;
            try { data = f.Decode(xs); }
            catch (LimitException) { throw; }
            catch (Exception) { }
            if (data != null)
            {
                object[] saved = State(true);
                int stackDepth = stack.Count;
                List<object> mx = f.Get(xs.Dict, "Matrix") as List<object>;
                if (mx != null && mx.Count == 6)
                {
                    double[] m = new double[6];
                    for (int i = 0; i < 6; i++) { object v = f.Resolve(mx[i]); m[i] = v is double && !Bad((double)v) ? (double)v : (i == 0 || i == 3 ? 1 : 0); }
                    ctm = Mul(m, ctm);
                }
                Dictionary<string, object> fr = f.Dict(f.Get(xs.Dict, "Resources")) ?? res;
                formDepth++;
                try { Exec(data, fr); }
                finally
                {
                    while (stack.Count > stackDepth) stack.Pop();
                    Restore(saved);
                }
            }
            forms.Remove(xs);
        }

        object[] State(bool full)
        {
            if (!full) return new object[] { ctm, font, size, leading, charSpace, wordSpace, scale };
            return new object[] { ctm, font, size, leading, charSpace, wordSpace, scale, tm, tlm, formDepth, marked, hideFrom };
        }

        void Restore(object[] s)
        {
            ctm = (double[])s[0];
            font = (PdfFont)s[1];
            size = (double)s[2];
            leading = (double)s[3];
            charSpace = (double)s[4];
            wordSpace = (double)s[5];
            scale = (double)s[6];
            if (s.Length < 10) return;
            tm = (double[])s[7];
            tlm = (double[])s[8];
            formDepth = (int)s[9];
            marked = (int)s[10];
            hideFrom = (int)s[11];
        }

        bool Visible(object oc, int depth)
        {
            if (depth > 20) return true;
            Dictionary<string, object> d = f.Dict(oc);
            if (d == null) return true;
            if (f.NameOf(d, "Type") != "OCMD") return !Hidden.Contains(d);
            List<object> ve = f.Get(d, "VE") as List<object>;
            if (ve != null) return Expr(ve, depth + 1);
            object ocgs = f.Get(d, "OCGs");
            List<object> list = ocgs as List<object>;
            if (list == null && ocgs != null) list = new List<object>(new object[] { ocgs });
            if (list == null) return true;
            int on = 0, total = 0;
            foreach (object o in list)
            {
                Dictionary<string, object> g = f.Dict(o);
                if (g == null) continue;
                total++;
                if (!Hidden.Contains(g)) on++;
            }
            if (total == 0) return true;
            switch (f.NameOf(d, "P") ?? "AnyOn")
            {
                case "AllOn": return on == total;
                case "AnyOff": return on < total;
                case "AllOff": return on == 0;
            }
            return on > 0;
        }

        bool Expr(List<object> e, int depth)
        {
            if (e.Count < 2 || depth > 20) return true;
            PdfName op = f.Resolve(e[0]) as PdfName;
            if (op == null) return true;
            bool any = false, all = true;
            for (int i = 1; i < e.Count; i++)
            {
                List<object> inner = f.Resolve(e[i]) as List<object>;
                bool v = inner != null ? Expr(inner, depth + 1) : Visible(e[i], depth + 1);
                any |= v;
                all &= v;
                if (op.V == "Not") return !v;
            }
            return op.V == "Or" ? any : all;
        }

        PdfFont Font(Dictionary<string, object> fontRes, string name)
        {
            object raw;
            if (fontRes == null || !fontRes.TryGetValue(name, out raw) || raw == null) return null;
            object key = raw is PdfRef ? (object)("r" + ((PdfRef)raw).Num) : raw;
            PdfFont result;
            if (fonts.TryGetValue(key, out result)) return result;
            Dictionary<string, object> fd = f.Dict(raw);
            try { result = fd != null ? PdfText.LoadFont(f, fd) : null; }
            catch (LimitException) { throw; }
            catch (Exception) { result = null; }
            fonts[key] = result;
            return result;
        }

        void NextLine()
        {
            tlm = Mul(Move(0, -leading), tlm);
            tm = tlm;
        }

        static PdfFont Fallback
        {
            get
            {
                PdfFont p = fallback;
                if (p != null) return p;
                p = new PdfFont();
                p.Base = new string[256];
                for (int i = 32; i < 256 && i < PdfTables.WinEnc.Length; i++) if (PdfTables.WinEnc[i] != (char)0) p.Base[i] = PdfTables.WinEnc[i].ToString();
                fallback = p;
                return p;
            }
        }

        static bool Bad(double v) { return double.IsNaN(v) || double.IsInfinity(v); }

        void Show(PdfStr s)
        {
            if (s == null || s.B.Length == 0) return;
            PdfFont fnt = font ?? Fallback;
            double[] m = Mul(tm, ctm);
            double h = Math.Abs(size) * Math.Sqrt(m[2] * m[2] + m[3] * m[3]);
            if (h <= 0 || Bad(h)) h = Math.Max(1, Math.Abs(size));
            double ux = fnt.Vertical ? -m[2] : m[0], uy = fnt.Vertical ? -m[3] : m[1];
            double ul = Math.Sqrt(ux * ux + uy * uy);
            if (ul > 0 && !Bad(ul)) { ux /= ul; uy /= ul; } else { ux = 1; uy = 0; ul = 1; }
            List<int> codes = fnt.Codes(s.B);
            piece.Length = 0;
            cs.Clear();
            ce.Clear();
            double adv = 0;
            foreach (int c in codes)
            {
                int before = piece.Length;
                PdfText.Append(piece, fnt.Text(c), fnt.Pua);
                double em = fnt.Vertical ? 1 : fnt.Width(c) / 1000.0;
                if (piece.Length == before && !fnt.Unmapped && em >= 0.15 && before > 0 && piece[before - 1] != ' ') piece.Append(' ');
                double add = charSpace + (!fnt.TwoByte && c == 32 ? wordSpace : 0);
                double glyph, step;
                if (fnt.Vertical) { glyph = size; step = size - add; }
                else { glyph = em * size * scale; step = glyph + add * scale; }
                for (int k = before; k < piece.Length; k++)
                {
                    cs.Add((float)(adv * ul));
                    ce.Add((float)((adv + glyph) * ul));
                }
                adv += step;
            }
            double x = m[4], y = m[5];
            tm = Mul(fnt.Vertical ? Move(0, -adv) : Move(adv, 0), tm);
            if (piece.Length == 0 || Bad(x) || Bad(y) || hideFrom >= 0) return;
            if (box != null)
            {
                double pad = Math.Max(10, h);
                if (x < box[0] - pad || x > box[2] + pad || y < box[1] - pad || y > box[3] + pad) return;
            }
            double[] e = Mul(tm, ctm);
            PdfSpan sp = new PdfSpan();
            sp.Text = piece.ToString();
            sp.X = x;
            sp.Y = y;
            sp.EndX = Bad(e[4]) ? x : e[4];
            sp.EndY = Bad(e[5]) ? y : e[5];
            sp.Ux = ux;
            sp.Uy = uy;
            sp.Unit = Math.Max(1, h);
            sp.CS = cs.ToArray();
            sp.CE = ce.ToArray();
            List<PdfSpan> target = inAnnot ? extra : spans;
            if (target.Count >= 400000) return;
            target.Add(sp);
            pending += sp.Text.Length;
        }

        static double Along(PdfSpan a, double x, double y) { return (x - a.X) * a.Ux + (y - a.Y) * a.Uy; }

        static double Down(PdfSpan a, PdfSpan b) { return (b.X - a.X) * a.Uy - (b.Y - a.Y) * a.Ux; }

        static bool Parallel(PdfSpan a, PdfSpan b) { return a.Ux * b.Ux + a.Uy * b.Uy >= 0.98; }

        static bool SameLine(PdfSpan a, PdfSpan b)
        {
            return Parallel(a, b) && Math.Abs(Down(a, b)) <= 0.6 * Math.Min(a.Unit, b.Unit);
        }

        static double Typical(List<List<PdfSpan>> lines)
        {
            List<double> r = new List<double>();
            for (int i = 1; i < lines.Count; i++)
            {
                PdfSpan a = lines[i - 1][0], b = lines[i][0];
                if (!Parallel(a, b) || a.Unit > b.Unit * 1.25 || b.Unit > a.Unit * 1.25) continue;
                double d = Down(a, b) / Math.Min(a.Unit, b.Unit);
                if (d > 0.8 && d < 3.5) r.Add(d);
            }
            if (r.Count == 0) return 1.2;
            r.Sort();
            return r[r.Count / 4];
        }

        static bool Break(PdfSpan a, PdfSpan b, double typical)
        {
            if (!Parallel(a, b)) return true;
            double unit = Math.Min(a.Unit, b.Unit), d = Down(a, b);
            if (d < 0.6 * unit) return true;
            return d / unit > Math.Max(1.35, typical * 1.3);
        }

        static List<List<PdfSpan>> Group(List<PdfSpan> list)
        {
            List<List<PdfSpan>> lines = new List<List<PdfSpan>>();
            List<PdfSpan> cur = null;
            foreach (PdfSpan s in list)
            {
                if (cur != null && SameLine(cur[0], s)) cur.Add(s);
                else
                {
                    cur = new List<PdfSpan>();
                    cur.Add(s);
                    lines.Add(cur);
                }
            }
            return lines;
        }

        public void Flush()
        {
            List<List<PdfSpan>> lines = Group(spans);
            List<PdfSpan> rest = new List<PdfSpan>();
            foreach (PdfSpan s in extra)
            {
                List<PdfSpan> best = null;
                double bestDown = double.MaxValue;
                foreach (List<PdfSpan> line in lines)
                {
                    if (!SameLine(line[0], s)) continue;
                    double d = Math.Abs(Down(line[0], s));
                    if (d < bestDown) { bestDown = d; best = line; }
                }
                if (best != null) best.Add(s);
                else rest.Add(s);
            }
            lines.AddRange(Group(rest));
            spans.Clear();
            extra.Clear();
            pending = 0;
            if (lines.Count == 0) return;
            double typical = Typical(lines);
            HashSet<string> seen = new HashSet<string>();
            int emitted = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                int mark = sb.Length;
                if (emitted > 0) sb.Append(Break(lines[i - 1][0], lines[i][0], typical) ? "\n\n" : "\n");
                int at = sb.Length;
                EmitLine(lines[i]);
                PdfSpan a = lines[i][0];
                string key = sb.Length == at ? null : sb.ToString(at, sb.Length - at) + "|" + Math.Round(a.X / 2) + "|" + Math.Round(a.Y / 2);
                if (key == null || !seen.Add(key)) sb.Length = mark;
                else emitted++;
            }
            if (emitted > 0) sb.Append('\n');
        }

        void EmitLine(List<PdfSpan> line)
        {
            PdfSpan a = line[0];
            int n = line.Count;
            double[] start = new double[n], end = new double[n];
            int[] order = new int[n];
            for (int i = 0; i < n; i++)
            {
                start[i] = Along(a, line[i].X, line[i].Y);
                end[i] = Along(a, line[i].EndX, line[i].EndY);
                order[i] = i;
            }
            Array.Sort(order, delegate(int p, int q)
            {
                int c = start[p].CompareTo(start[q]);
                return c != 0 ? c : p.CompareTo(q);
            });
            List<PdfGlyph> gl = new List<PdfGlyph>();
            int prev = -1;
            foreach (int i in order)
            {
                PdfSpan s = line[i];
                if (prev >= 0)
                {
                    PdfSpan ps = line[prev];
                    if (s.Text == ps.Text && Math.Abs(start[i] - start[prev]) < (s.Text.Length > 1 ? 0.2 : 0.08) * Math.Min(ps.Unit, s.Unit)) continue;
                }
                for (int k = 0; k < s.Text.Length; k++)
                {
                    char ch = s.Text[k];
                    if (char.IsLowSurrogate(ch) && gl.Count > 0 && gl[gl.Count - 1].Span == i)
                    {
                        PdfGlyph g0 = gl[gl.Count - 1];
                        g0.Text += ch;
                        g0.Last = k == s.Text.Length - 1;
                        gl[gl.Count - 1] = g0;
                        continue;
                    }
                    PdfGlyph g;
                    g.Text = ch.ToString();
                    g.Start = start[i] + (k < s.CS.Length ? s.CS[k] : 0);
                    g.End = start[i] + (k < s.CE.Length ? s.CE[k] : 0);
                    g.Span = i;
                    g.Last = k == s.Text.Length - 1;
                    gl.Add(g);
                }
                prev = i;
            }
            Attach(gl, line);
            List<string> units = new List<string>(gl.Count + 8);
            int lastSpan = -1;
            double lastEnd = 0;
            bool rtl = false;
            foreach (PdfGlyph g in gl)
            {
                if (g.Text == null) continue;
                if (lastSpan >= 0 && g.Span != lastSpan)
                {
                    double gap = g.Start - lastEnd;
                    if (gap > 0.11 * Math.Min(line[lastSpan].Unit, line[g.Span].Unit) && units.Count > 0 && units[units.Count - 1] != " " && g.Text[0] != ' ') units.Add(" ");
                }
                units.Add(g.Text);
                if (!rtl && PdfText.Rtl(g.Text[0])) rtl = true;
                lastSpan = g.Span;
                lastEnd = g.Last ? end[g.Span] : g.End;
            }
            if (rtl && Math.Abs(a.Ux) > 0.5) PdfText.Logical(units);
            int lineStart = sb.Length;
            foreach (string u in units) sb.Append(u);
            if (rtl) PdfText.Unshape(sb, lineStart);
            while (sb.Length > lineStart && sb[sb.Length - 1] == ' ') sb.Length--;
        }

        static void Attach(List<PdfGlyph> gl, List<PdfSpan> line)
        {
            for (int i = 0; i < gl.Count; i++)
            {
                if (gl[i].Text == null) continue;
                string mark = PdfText.Combining(gl[i].Text);
                bool combining = mark == null && gl[i].Text.Length == 1 && PdfText.Mark(gl[i].Text[0]);
                if (mark == null && !combining) continue;
                double c = combining ? gl[i].Start : (gl[i].Start + gl[i].End) / 2, tol = combining ? 0 : 0.05 * line[gl[i].Span].Unit, bestDist = double.MaxValue;
                int best = -1;
                for (int j = Math.Max(0, i - 6); j < Math.Min(gl.Count, i + 7); j++)
                {
                    PdfGlyph b = gl[j];
                    if (j == i || b.Text == null || !char.IsLetter(b.Text, 0)) continue;
                    if (!combining && b.Span == gl[i].Span) continue;
                    if (combining ? (c <= b.Start + 0.01 || c >= b.End - 0.01) : (c < b.Start - tol || c > b.End + tol)) continue;
                    double d = Math.Abs(c - (b.Start + b.End) / 2);
                    if (d < bestDist) { bestDist = d; best = j; }
                }
                if (best < 0) continue;
                PdfGlyph target = gl[best];
                string root = target.Text;
                if (!combining && root.Length == 1 && root[0] == (char)0x131) root = "i";
                else if (!combining && root.Length == 1 && root[0] == (char)0x237) root = "j";
                target.Text = combining ? root + gl[i].Text : (root + mark).Normalize(NormalizationForm.FormC);
                gl[best] = target;
                PdfGlyph gone = gl[i];
                gone.Text = null;
                gl[i] = gone;
            }
        }
    }

    static class PdfTables
    {
        public const string StdEnc = "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F"
            + "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F"
            + " !\u0022#$%&\u2019()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\u005C]^_\u2018abcdefghijk"
            + "lmnopqrstuvwxyz{|}~\u007F\u0080\u0081\u0082\u0083\u0084\u0085\u0086\u0087\u0088\u0089\u008A"
            + "\u008B\u008C\u008D\u008E\u008F\u0090\u0091\u0092\u0093\u0094\u0095\u0096\u0097\u0098\u0099\u009A"
            + "\u009B\u009C\u009D\u009E\u009F\u00A0\u00A1\u00A2\u00A3\u2044\u00A5\u0192\u00A7\u00A4'\u201C"
            + "\u00AB\u2039\u203A\uFB01\uFB02\u00B0\u2013\u2020\u2021\u00B7\u00B5\u00B6\u2022\u201A\u201E\u201D"
            + "\u00BB\u2026\u2030\u00BE\u00BF\u00C0`\u00B4\u02C6\u02DC\u00AF\u02D8\u02D9\u00A8\u00C9\u02DA"
            + "\u00B8\u00CC\u02DD\u02DB\u02C7\u2014\u00D1\u00D2\u00D3\u00D4\u00D5\u00D6\u00D7\u00D8\u00D9\u00DA"
            + "\u00DB\u00DC\u00DD\u00DE\u00DF\u00E0\u00C6\u00E2\u00AA\u00E4\u00E5\u00E6\u00E7\u0141\u00D8\u0152"
            + "\u00BA\u00EC\u00ED\u00EE\u00EF\u00F0\u00E6\u00F2\u00F3\u00F4\u0131\u00F6\u00F7\u0142\u00F8\u0153"
            + "\u00DF\u00FC\u00FD\u00FE\u00FF";

        public const string WinEnc = "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F"
            + "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F"
            + " !\u0022#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\u005C]^_`abcdefghijklmnopqrstu"
            + "vwxyz{|}~\u007F\u20AC\u0081\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152"
            + "\u008D\u017D\u008F\u0090\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153"
            + "\u009D\u017E\u0178\u00A0\u00A1\u00A2\u00A3\u00A4\u00A5\u00A6\u00A7\u00A8\u00A9\u00AA\u00AB\u00AC"
            + "\u00AD\u00AE\u00AF\u00B0\u00B1\u00B2\u00B3\u00B4\u00B5\u00B6\u00B7\u00B8\u00B9\u00BA\u00BB\u00BC"
            + "\u00BD\u00BE\u00BF\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u00C6\u00C7\u00C8\u00C9\u00CA\u00CB\u00CC"
            + "\u00CD\u00CE\u00CF\u00D0\u00D1\u00D2\u00D3\u00D4\u00D5\u00D6\u00D7\u00D8\u00D9\u00DA\u00DB\u00DC"
            + "\u00DD\u00DE\u00DF\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u00E6\u00E7\u00E8\u00E9\u00EA\u00EB\u00EC"
            + "\u00ED\u00EE\u00EF\u00F0\u00F1\u00F2\u00F3\u00F4\u00F5\u00F6\u00F7\u00F8\u00F9\u00FA\u00FB\u00FC"
            + "\u00FD\u00FE\u00FF";

        public const string MacEnc = "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F"
            + "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F"
            + " !\u0022#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\u005C]^_`abcdefghijklmnopqrstu"
            + "vwxyz{|}~\u007F\u00C4\u00C5\u00C7\u00C9\u00D1\u00D6\u00DC\u00E1\u00E0\u00E2\u00E4\u00E3\u00E5"
            + "\u00E7\u00E9\u00E8\u00EA\u00EB\u00ED\u00EC\u00EE\u00EF\u00F1\u00F3\u00F2\u00F4\u00F6\u00F5\u00FA"
            + "\u00F9\u00FB\u00FC\u2020\u00B0\u00A2\u00A3\u00A7\u2022\u00B6\u00DF\u00AE\u00A9\u2122\u00B4\u00A8"
            + "\u2260\u00C6\u00D8\u221E\u00B1\u2264\u2265\u00A5\u00B5\u2202\u2211\u220F\u03C0\u222B\u00AA\u00BA"
            + "\u03A9\u00E6\u00F8\u00BF\u00A1\u00AC\u221A\u0192\u2248\u2206\u00AB\u00BB\u2026\u00A0\u00C0\u00C3"
            + "\u00D5\u0152\u0153\u2013\u2014\u201C\u201D\u2018\u2019\u00F7\u25CA\u00FF\u0178\u2044\u20AC\u2039"
            + "\u203A\uFB01\uFB02\u2021\u00B7\u201A\u201E\u2030\u00C2\u00CA\u00C1\u00CB\u00C8\u00CD\u00CE\u00CF"
            + "\u00CC\u00D3\u00D4\uF8FF\u00D2\u00DA\u00DB\u00D9\u0131\u02C6\u02DC\u00AF\u02D8\u02D9\u02DA\u00B8"
            + "\u02DD\u02DB\u02C7";

        public const string SymEnc = "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F"
            + "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F"
            + "\u00A0!\u2200#\u2203%&\u220B()\u2217+,\u2212./0123456789:;<=>?\u2245\u0391\u0392\u03A7\u2206"
            + "\u0395\u03A6\u0393\u0397\u0399\u03D1\u039A\u039B\u039C\u039D\u039F\u03A0\u0398\u03A1\u03A3\u03A4"
            + "\u03A5\u03C2\u2126\u039E\u03A8\u0396[\u2234]\u22A5_\uF8E5\u03B1\u03B2\u03C7\u03B4\u03B5\u03C6"
            + "\u03B3\u03B7\u03B9\u03D5\u03BA\u03BB\u03BC\u03BD\u03BF\u03C0\u03B8\u03C1\u03C3\u03C4\u03C5\u03D6"
            + "\u03C9\u03BE\u03C8\u03B6{|}\u223C\u007F\u0080\u0081\u0082\u0083\u0084\u0085\u0086\u0087\u0088"
            + "\u0089\u008A\u008B\u008C\u008D\u008E\u008F\u0090\u0091\u0092\u0093\u0094\u0095\u0096\u0097\u0098"
            + "\u0099\u009A\u009B\u009C\u009D\u009E\u009F\u20AC\u03D2\u2032\u2264\u2215\u221E\u0192\u2663\u2666"
            + "\u2665\u2660\u2194\u2190\u2191\u2192\u2193\u00B0\u00B1\u2033\u2265\u00D7\u221D\u2202\u2022\u00F7"
            + "\u2260\u2261\u2248\u2026\uF8E6\uF8E7\u21B5\u2135\u2111\u211C\u2118\u2297\u2295\u2205\u2229\u222A"
            + "\u2283\u2287\u2284\u2282\u2286\u2208\u2209\u2220\u2207\uF6DA\uF6D9\uF6DB\u220F\u221A\u22C5\u00AC"
            + "\u2227\u2228\u21D4\u21D0\u21D1\u21D2\u21D3\u25CA\u2329\uF8E8\uF8E9\uF8EA\u2211\uF8EB\uF8EC\uF8ED"
            + "\uF8EE\uF8EF\uF8F0\uF8F1\uF8F2\uF8F3\uF8F4\u00F0\u232A\u222B\u2320\uF8F5\u2321\uF8F6\uF8F7\uF8F8"
            + "\uF8F9\uF8FA\uF8FB\uF8FC\uF8FD\uF8FE\u00FF";

        public const string PdfDocEnc = "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F"
            + "\u0010\u0011\u0012\u0013\u0014\u0015\u0000\u0017\u02D8\u02C7\u02C6\u02D9\u02DD\u02DB\u02DA\u02DC"
            + " !\u0022#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\u005C]^_`abcdefghijklmnopqrstu"
            + "vwxyz{|}~\u0000\u2022\u2020\u2021\u2026\u2014\u2013\u0192\u2044\u2039\u203A\u2212\u2030\u201E"
            + "\u201C\u201D\u2018\u2019\u201A\u2122\uFB01\uFB02\u0141\u0152\u0160\u0178\u017D\u0131\u0142\u0153"
            + "\u0161\u017E\u0000\u20AC\u00A1\u00A2\u00A3\u00A4\u00A5\u00A6\u00A7\u00A8\u00A9\u00AA\u00AB\u00AC"
            + "\u0000\u00AE\u00AF\u00B0\u00B1\u00B2\u00B3\u00B4\u00B5\u00B6\u00B7\u00B8\u00B9\u00BA\u00BB\u00BC"
            + "\u00BD\u00BE\u00BF\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u00C6\u00C7\u00C8\u00C9\u00CA\u00CB\u00CC"
            + "\u00CD\u00CE\u00CF\u00D0\u00D1\u00D2\u00D3\u00D4\u00D5\u00D6\u00D7\u00D8\u00D9\u00DA\u00DB\u00DC"
            + "\u00DD\u00DE\u00DF\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u00E6\u00E7\u00E8\u00E9\u00EA\u00EB\u00EC"
            + "\u00ED\u00EE\u00EF\u00F0\u00F1\u00F2\u00F3\u00F4\u00F5\u00F6\u00F7\u00F8\u00F9\u00FA\u00FB\u00FC"
            + "\u00FD\u00FE\u00FF";

        public const string Agl = "A 41|AE C6|AEacute 1FC|AEmacron 1E2|Aacute C1|Abreve 102|Abrevecyrillic 4D0|Acaron 1CD|"
            + "Acircumflex C2|Acyrillic 410|Adblgrave 200|Adieresis C4|Adieresiscyrillic 4D2|"
            + "Adieresismacron 1DE|Adotmacron 1E0|Agrave C0|Aiecyrillic 4D4|Ainvertedbreve 202|Alpha 391|"
            + "Alphatonos 386|Amacron 100|Aogonek 104|Aring C5|Aringacute 1FA|Atilde C3|B 42|Becyrillic 411|"
            + "Beta 392|Bhook 181|Btopbar 182|C 43|Cacute 106|Ccaron 10C|Ccedilla C7|Ccircumflex 108|Cdot 10A|"
            + "Cdotaccent 10A|Cheabkhasiancyrillic 4BC|Checyrillic 427|Chedescenderabkhasiancyrillic 4BE|"
            + "Chedescendercyrillic 4B6|Chedieresiscyrillic 4F4|Chekhakassiancyrillic 4CB|"
            + "Cheverticalstrokecyrillic 4B8|Chi 3A7|Chook 187|D 44|DZ 1F1|DZcaron 1C4|Dafrican 189|Dcaron 10E|"
            + "Dcroat 110|Decyrillic 414|Deicoptic 3EE|Delta 2206|Deltagreek 394|Dhook 18A|Digammagreek 3DC|"
            + "Djecyrillic 402|Dslash 110|Dtopbar 18B|Dz 1F2|Dzcaron 1C5|Dzeabkhasiancyrillic 4E0|"
            + "Dzecyrillic 405|Dzhecyrillic 40F|E 45|Eacute C9|Ebreve 114|Ecaron 11A|Ecircumflex CA|"
            + "Ecyrillic 404|Edblgrave 204|Edieresis CB|Edot 116|Edotaccent 116|Efcyrillic 424|Egrave C8|"
            + "Eightroman 2167|Einvertedbreve 206|Eiotifiedcyrillic 464|Elcyrillic 41B|Elevenroman 216A|"
            + "Emacron 112|Emcyrillic 41C|Encyrillic 41D|Endescendercyrillic 4A2|Eng 14A|Enghecyrillic 4A4|"
            + "Enhookcyrillic 4C7|Eogonek 118|Eopen 190|Epsilon 395|Epsilontonos 388|Ercyrillic 420|"
            + "Ereversed 18E|Ereversedcyrillic 42D|Escyrillic 421|Esdescendercyrillic 4AA|Esh 1A9|Eta 397|"
            + "Etatonos 389|Eth D0|Euro 20AC|Ezh 1B7|Ezhcaron 1EE|Ezhreversed 1B8|F 46|Feicoptic 3E4|Fhook 191|"
            + "Fitacyrillic 472|Fiveroman 2164|Fourroman 2163|G 47|Gacute 1F4|Gamma 393|Gammaafrican 194|"
            + "Gangiacoptic 3EA|Gbreve 11E|Gcaron 1E6|Gcedilla 122|Gcircumflex 11C|Gcommaaccent 122|Gdot 120|"
            + "Gdotaccent 120|Gecyrillic 413|Ghemiddlehookcyrillic 494|Ghestrokecyrillic 492|"
            + "Gheupturncyrillic 490|Ghook 193|Gjecyrillic 403|Gstroke 1E4|H 48|H18533 25CF|H18543 25AA|"
            + "H18551 25AB|H22073 25A1|Haabkhasiancyrillic 4A8|Hadescendercyrillic 4B2|Hardsigncyrillic 42A|"
            + "Hbar 126|Hcircumflex 124|Horicoptic 3E8|I 49|IAcyrillic 42F|IJ 132|IUcyrillic 42E|Iacute CD|"
            + "Ibreve 12C|Icaron 1CF|Icircumflex CE|Icyrillic 406|Idblgrave 208|Idieresis CF|"
            + "Idieresiscyrillic 4E4|Idot 130|Idotaccent 130|Iebrevecyrillic 4D6|Iecyrillic 415|Ifraktur 2111|"
            + "Igrave CC|Iicyrillic 418|Iinvertedbreve 20A|Iishortcyrillic 419|Imacron 12A|Imacroncyrillic 4E2|"
            + "Iocyrillic 401|Iogonek 12E|Iota 399|Iotaafrican 196|Iotadieresis 3AA|Iotatonos 38A|Istroke 197|"
            + "Itilde 128|Izhitsacyrillic 474|Izhitsadblgravecyrillic 476|J 4A|Jcircumflex 134|Jecyrillic 408|"
            + "K 4B|Kabashkircyrillic 4A0|Kacyrillic 41A|Kadescendercyrillic 49A|Kahookcyrillic 4C3|Kappa 39A|"
            + "Kastrokecyrillic 49E|Kaverticalstrokecyrillic 49C|Kcaron 1E8|Kcedilla 136|Kcommaaccent 136|"
            + "Khacyrillic 425|Kheicoptic 3E6|Khook 198|Kjecyrillic 40C|Koppacyrillic 480|Koppagreek 3DE|"
            + "Ksicyrillic 46E|L 4C|LJ 1C7|Lacute 139|Lambda 39B|Lcaron 13D|Lcedilla 13B|Lcommaaccent 13B|"
            + "Ldot 13F|Ldotaccent 13F|Lj 1C8|Ljecyrillic 409|Lslash 141|M 4D|Mturned 19C|Mu 39C|N 4E|NJ 1CA|"
            + "Nacute 143|Ncaron 147|Ncedilla 145|Ncommaaccent 145|Nhookleft 19D|Nineroman 2168|Nj 1CB|"
            + "Njecyrillic 40A|Ntilde D1|Nu 39D|O 4F|OE 152|Oacute D3|Obarredcyrillic 4E8|"
            + "Obarreddieresiscyrillic 4EA|Obreve 14E|Ocaron 1D1|Ocenteredtilde 19F|Ocircumflex D4|"
            + "Ocyrillic 41E|Odblacute 150|Odblgrave 20C|Odieresis D6|Odieresiscyrillic 4E6|Ograve D2|Ohm 2126|"
            + "Ohorn 1A0|Ohungarumlaut 150|Oi 1A2|Oinvertedbreve 20E|Omacron 14C|Omega 2126|Omegacyrillic 460|"
            + "Omegagreek 3A9|Omegaroundcyrillic 47A|Omegatitlocyrillic 47C|Omegatonos 38F|Omicron 39F|"
            + "Omicrontonos 38C|Oneroman 2160|Oogonek 1EA|Oogonekmacron 1EC|Oopen 186|Oslash D8|Oslashacute 1FE|"
            + "Ostrokeacute 1FE|Otcyrillic 47E|Otilde D5|P 50|Pecyrillic 41F|Pemiddlehookcyrillic 4A6|Phi 3A6|"
            + "Phook 1A4|Pi 3A0|Psi 3A8|Psicyrillic 470|Q 51|R 52|Racute 154|Rcaron 158|Rcedilla 156|"
            + "Rcommaaccent 156|Rdblgrave 210|Rfraktur 211C|Rho 3A1|Rinvertedbreve 212|"
            + "Rsmallinvertedsuperior 2B6|S 53|SF010000 250C|SF020000 2514|SF030000 2510|SF040000 2518|"
            + "SF050000 253C|SF060000 252C|SF070000 2534|SF080000 251C|SF090000 2524|SF100000 2500|"
            + "SF110000 2502|SF190000 2561|SF200000 2562|SF210000 2556|SF220000 2555|SF230000 2563|"
            + "SF240000 2551|SF250000 2557|SF260000 255D|SF270000 255C|SF280000 255B|SF360000 255E|"
            + "SF370000 255F|SF380000 255A|SF390000 2554|SF400000 2569|SF410000 2566|SF420000 2560|"
            + "SF430000 2550|SF440000 256C|SF450000 2567|SF460000 2568|SF470000 2564|SF480000 2565|"
            + "SF490000 2559|SF500000 2558|SF510000 2552|SF520000 2553|SF530000 256B|SF540000 256A|Sacute 15A|"
            + "Sampigreek 3E0|Scaron 160|Scedilla 15E|Schwa 18F|Schwacyrillic 4D8|Schwadieresiscyrillic 4DA|"
            + "Scircumflex 15C|Scommaaccent 218|Sevenroman 2166|Shacyrillic 428|Shchacyrillic 429|"
            + "Sheicoptic 3E2|Shhacyrillic 4BA|Shimacoptic 3EC|Sigma 3A3|Sixroman 2165|Softsigncyrillic 42C|"
            + "Stigmagreek 3DA|T 54|Tau 3A4|Tbar 166|Tcaron 164|Tcedilla 162|Tcommaaccent 162|Tecyrillic 422|"
            + "Tedescendercyrillic 4AC|Tenroman 2169|Tetsecyrillic 4B4|Theta 398|Thook 1AC|Thorn DE|"
            + "Threeroman 2162|Tonefive 1BC|Tonesix 184|Tonetwo 1A7|Tretroflexhook 1AE|Tsecyrillic 426|"
            + "Tshecyrillic 40B|Twelveroman 216B|Tworoman 2161|U 55|Uacute DA|Ubreve 16C|Ucaron 1D3|"
            + "Ucircumflex DB|Ucyrillic 423|Udblacute 170|Udblgrave 214|Udieresis DC|Udieresisacute 1D7|"
            + "Udieresiscaron 1D9|Udieresiscyrillic 4F0|Udieresisgrave 1DB|Udieresismacron 1D5|Ugrave D9|"
            + "Uhorn 1AF|Uhungarumlaut 170|Uhungarumlautcyrillic 4F2|Uinvertedbreve 216|Ukcyrillic 478|"
            + "Umacron 16A|Umacroncyrillic 4EE|Uogonek 172|Upsilon 3A5|Upsilon1 3D2|"
            + "Upsilonacutehooksymbolgreek 3D3|Upsilonafrican 1B1|Upsilondieresis 3AB|"
            + "Upsilondieresishooksymbolgreek 3D4|Upsilonhooksymbol 3D2|Upsilontonos 38E|Uring 16E|"
            + "Ushortcyrillic 40E|Ustraightcyrillic 4AE|Ustraightstrokecyrillic 4B0|Utilde 168|V 56|"
            + "Vecyrillic 412|Vhook 1B2|W 57|Wcircumflex 174|X 58|Xi 39E|Y 59|Yacute DD|Yatcyrillic 462|"
            + "Ycircumflex 176|Ydieresis 178|Yericyrillic 42B|Yerudieresiscyrillic 4F8|Yhook 1B3|Yicyrillic 407|"
            + "Yusbigcyrillic 46A|Yusbigiotifiedcyrillic 46C|Yuslittlecyrillic 466|"
            + "Yuslittleiotifiedcyrillic 468|Z 5A|Zacute 179|Zcaron 17D|Zdot 17B|Zdotaccent 17B|Zecyrillic 417|"
            + "Zedescendercyrillic 498|Zedieresiscyrillic 4DE|Zeta 396|Zhebrevecyrillic 4C1|Zhecyrillic 416|"
            + "Zhedescendercyrillic 496|Zhedieresiscyrillic 4DC|Zstroke 1B5|a 61|aacute E1|abreve 103|"
            + "abrevecyrillic 4D1|acaron 1CE|acircumflex E2|acute B4|acutelowmod 2CF|acyrillic 430|"
            + "adblgrave 201|adieresis E4|adieresiscyrillic 4D3|adieresismacron 1DF|adotmacron 1E1|ae E6|"
            + "aeacute 1FD|aemacron 1E3|afii00208 2015|afii08941 20A4|afii10017 410|afii10018 411|afii10019 412|"
            + "afii10020 413|afii10021 414|afii10022 415|afii10023 401|afii10024 416|afii10025 417|"
            + "afii10026 418|afii10027 419|afii10028 41A|afii10029 41B|afii10030 41C|afii10031 41D|"
            + "afii10032 41E|afii10033 41F|afii10034 420|afii10035 421|afii10036 422|afii10037 423|"
            + "afii10038 424|afii10039 425|afii10040 426|afii10041 427|afii10042 428|afii10043 429|"
            + "afii10044 42A|afii10045 42B|afii10046 42C|afii10047 42D|afii10048 42E|afii10049 42F|"
            + "afii10050 490|afii10051 402|afii10052 403|afii10053 404|afii10054 405|afii10055 406|"
            + "afii10056 407|afii10057 408|afii10058 409|afii10059 40A|afii10060 40B|afii10061 40C|"
            + "afii10062 40E|afii10065 430|afii10066 431|afii10067 432|afii10068 433|afii10069 434|"
            + "afii10070 435|afii10071 451|afii10072 436|afii10073 437|afii10074 438|afii10075 439|"
            + "afii10076 43A|afii10077 43B|afii10078 43C|afii10079 43D|afii10080 43E|afii10081 43F|"
            + "afii10082 440|afii10083 441|afii10084 442|afii10085 443|afii10086 444|afii10087 445|"
            + "afii10088 446|afii10089 447|afii10090 448|afii10091 449|afii10092 44A|afii10093 44B|"
            + "afii10094 44C|afii10095 44D|afii10096 44E|afii10097 44F|afii10098 491|afii10099 452|"
            + "afii10100 453|afii10101 454|afii10102 455|afii10103 456|afii10104 457|afii10105 458|"
            + "afii10106 459|afii10107 45A|afii10108 45B|afii10109 45C|afii10110 45E|afii10145 40F|"
            + "afii10146 462|afii10147 472|afii10148 474|afii10193 45F|afii10194 463|afii10195 473|"
            + "afii10196 475|afii10846 4D9|afii299 200E|afii300 200F|afii301 200D|afii57636 20AA|afii57929 2BC|"
            + "afii61248 2105|afii61289 2113|afii61352 2116|afii61573 202C|afii61574 202D|afii61575 202E|"
            + "afii61664 200C|afii64937 2BD|agrave E0|aiecyrillic 4D5|ainvertedbreve 203|aleph 2135|"
            + "allequal 224C|alpha 3B1|alphatonos 3AC|amacron 101|ampersand 26|angle 2220|angleleft 2329|"
            + "angleright 232A|angstrom 212B|anoteleia 387|aogonek 105|apostrophemod 2BC|approaches 2250|"
            + "approxequal 2248|approxequalorimage 2252|approximatelyequal 2245|arc 2312|aring E5|"
            + "aringacute 1FB|arrowboth 2194|arrowdashdown 21E3|arrowdashleft 21E0|arrowdashright 21E2|"
            + "arrowdashup 21E1|arrowdblboth 21D4|arrowdbldown 21D3|arrowdblleft 21D0|arrowdblright 21D2|"
            + "arrowdblup 21D1|arrowdown 2193|arrowdownleft 2199|arrowdownright 2198|arrowdownwhite 21E9|"
            + "arrowheaddownmod 2C5|arrowheadleftmod 2C2|arrowheadrightmod 2C3|arrowheadupmod 2C4|"
            + "arrowleft 2190|arrowleftdbl 21D0|arrowleftdblstroke 21CD|arrowleftoverright 21C6|"
            + "arrowleftwhite 21E6|arrowright 2192|arrowrightdblstroke 21CF|arrowrightheavy 279E|"
            + "arrowrightoverleft 21C4|arrowrightwhite 21E8|arrowtableft 21E4|arrowtabright 21E5|arrowup 2191|"
            + "arrowupdn 2195|arrowupdnbse 21A8|arrowupdownbase 21A8|arrowupleft 2196|arrowupleftofdown 21C5|"
            + "arrowupright 2197|arrowupwhite 21E7|asciicircum 5E|asciitilde 7E|asterisk 2A|asteriskmath 2217|"
            + "asterism 2042|asymptoticallyequal 2243|at 40|atilde E3|b 62|backslash 5C|bar 7C|"
            + "beamedsixteenthnotes 266C|because 2235|becyrillic 431|beta 3B2|betasymbolgreek 3D0|"
            + "blackcircle 25CF|blackdiamond 25C6|blackdownpointingtriangle 25BC|blackleftpointingpointer 25C4|"
            + "blackleftpointingtriangle 25C0|blacklowerlefttriangle 25E3|blacklowerrighttriangle 25E2|"
            + "blackrectangle 25AC|blackrightpointingpointer 25BA|blackrightpointingtriangle 25B6|"
            + "blacksmallsquare 25AA|blacksmilingface 263B|blacksquare 25A0|blackstar 2605|"
            + "blackupperlefttriangle 25E4|blackupperrighttriangle 25E5|blackuppointingsmalltriangle 25B4|"
            + "blackuppointingtriangle 25B2|braceleft 7B|braceright 7D|bracketleft 5B|bracketright 5D|breve 2D8|"
            + "brokenbar A6|bstroke 180|btopbar 183|bullet 2022|bulletinverse 25D8|bulletoperator 2219|"
            + "bullseye 25CE|c 63|cacute 107|capslock 21EA|careof 2105|caron 2C7|carriagereturn 21B5|ccaron 10D|"
            + "ccedilla E7|ccircumflex 109|cdot 10B|cdotaccent 10B|cedilla B8|cent A2|centigrade 2103|"
            + "cheabkhasiancyrillic 4BD|checkmark 2713|checyrillic 447|chedescenderabkhasiancyrillic 4BF|"
            + "chedescendercyrillic 4B7|chedieresiscyrillic 4F5|chekhakassiancyrillic 4CC|"
            + "cheverticalstrokecyrillic 4B9|chi 3C7|chook 188|circle 25CB|circlemultiply 2297|circleot 2299|"
            + "circleplus 2295|circlewithlefthalfblack 25D0|circlewithrighthalfblack 25D1|circumflex 2C6|"
            + "clear 2327|clickalveolar 1C2|clickdental 1C0|clicklateral 1C1|clickretroflex 1C3|club 2663|"
            + "clubsuitblack 2663|clubsuitwhite 2667|colon 3A|colonmonetary 20A1|colonsign 20A1|"
            + "colontriangularhalfmod 2D1|colontriangularmod 2D0|comma 2C|commareversedmod 2BD|"
            + "commaturnedmod 2BB|compass 263C|congruent 2245|contourintegral 222E|control 2303|controlDEL 7F|"
            + "copyright A9|cruzeiro 20A2|curlyand 22CF|curlyor 22CE|currency A4|d 64|dagger 2020|"
            + "daggerdbl 2021|dasiapneumatacyrilliccmb 485|dblarrowleft 21D4|dblarrowright 21D2|"
            + "dblintegral 222C|dbllowline 2017|dblprimemod 2BA|dblverticalbar 2016|dcaron 10F|dcroat 111|"
            + "decyrillic 434|degree B0|deicoptic 3EF|deleteleft 232B|deleteright 2326|delta 3B4|"
            + "deltaturned 18D|dialytikatonos 385|diamond 2666|diamondsuitwhite 2662|dieresis A8|"
            + "dieresistonos 385|divide F7|divides 2223|divisionslash 2215|djecyrillic 452|dmacron 111|"
            + "dollar 24|dong 20AB|dotaccent 2D9|dotlessi 131|dotmath 22C5|dottedcircle 25CC|downtackmod 2D5|"
            + "dtopbar 18C|dz 1F3|dzcaron 1C6|dzeabkhasiancyrillic 4E1|dzecyrillic 455|dzhecyrillic 45F|e 65|"
            + "eacute E9|earth 2641|ebreve 115|ecaron 11B|ecircumflex EA|ecyrillic 454|edblgrave 205|"
            + "edieresis EB|edot 117|edotaccent 117|efcyrillic 444|egrave E8|eight 38|"
            + "eightcircleinversesansserif 2791|eighthnotebeamed 266B|eightroman 2177|einvertedbreve 207|"
            + "eiotifiedcyrillic 465|elcyrillic 43B|element 2208|elevenroman 217A|ellipsis 2026|"
            + "ellipsisvertical 22EE|emacron 113|emcyrillic 43C|emdash 2014|emptyset 2205|encyrillic 43D|"
            + "endash 2013|endescendercyrillic 4A3|eng 14B|enghecyrillic 4A5|enhookcyrillic 4C8|enspace 2002|"
            + "eogonek 119|epsilon 3B5|epsilontonos 3AD|equal 3D|equivalence 2261|ercyrillic 440|"
            + "ereversedcyrillic 44D|escyrillic 441|esdescendercyrillic 4AB|eshreversedloop 1AA|estimated 212E|"
            + "eta 3B7|etatonos 3AE|eth F0|eturned 1DD|euro 20AC|exclam 21|exclamdbl 203C|exclamdown A1|"
            + "existential 2203|ezhcaron 1EF|ezhreversed 1B9|ezhtail 1BA|f 66|fahrenheit 2109|feicoptic 3E5|"
            + "female 2640|ff FB00|ffi FB03|ffl FB04|fi FB01|figuredash 2012|filledbox 25A0|filledrect 25AC|"
            + "firsttonechinese 2C9|fisheye 25C9|fitacyrillic 473|five 35|fivecircleinversesansserif 278E|"
            + "fiveeighths 215D|fiveroman 2174|fl FB02|florin 192|forall 2200|four 34|"
            + "fourcircleinversesansserif 278D|fourroman 2173|fourthtonechinese 2CB|fraction 2044|franc 20A3|"
            + "g 67|gacute 1F5|gamma 3B3|gammasuperior 2E0|gangiacoptic 3EB|gbreve 11F|gcaron 1E7|gcedilla 123|"
            + "gcircumflex 11D|gcommaaccent 123|gdot 121|gdotaccent 121|gecyrillic 433|geometricallyequal 2251|"
            + "germandbls DF|ghemiddlehookcyrillic 495|ghestrokecyrillic 493|gheupturncyrillic 491|"
            + "gjecyrillic 453|glottalinvertedstroke 1BE|glottalstopmod 2C0|glottalstopreversedmod 2C1|"
            + "glottalstopreversedsuperior 2E4|gradient 2207|grave 60|gravelowmod 2CE|greater 3E|"
            + "greaterequal 2265|greaterequalorless 22DB|greaterorequivalent 2273|greaterorless 2277|"
            + "greateroverequal 2267|gstroke 1E5|guillemotleft AB|guillemotright BB|guilsinglleft 2039|"
            + "guilsinglright 203A|h 68|haabkhasiancyrillic 4A9|hadescendercyrillic 4B3|hardsigncyrillic 44A|"
            + "harpoonleftbarbup 21BC|harpoonrightbarbup 21C0|hbar 127|hcircumflex 125|heart 2665|"
            + "heartsuitblack 2665|heartsuitwhite 2661|hhooksuperior 2B1|horicoptic 3E9|horizontalbar 2015|"
            + "hotsprings 2668|house 2302|hsuperior 2B0|hungarumlaut 2DD|hv 195|hyphen 2D|hyphentwo 2010|i 69|"
            + "iacute ED|iacyrillic 44F|ibreve 12D|icaron 1D0|icircumflex EE|icyrillic 456|idblgrave 209|"
            + "idieresis EF|idieresiscyrillic 4E5|iebrevecyrillic 4D7|iecyrillic 435|igrave EC|iicyrillic 438|"
            + "iinvertedbreve 20B|iishortcyrillic 439|ij 133|ilde 2DC|imacron 12B|imacroncyrillic 4E3|"
            + "imageorapproximatelyequal 2253|increment 2206|infinity 221E|integral 222B|integralbottom 2321|"
            + "integralbt 2321|integraltop 2320|integraltp 2320|intersection 2229|invbullet 25D8|invcircle 25D9|"
            + "invsmileface 263B|iocyrillic 451|iogonek 12F|iota 3B9|iotadieresis 3CA|iotadieresistonos 390|"
            + "iotatonos 3AF|itilde 129|iucyrillic 44E|izhitsacyrillic 475|izhitsadblgravecyrillic 477|j 6A|"
            + "jcaron 1F0|jcircumflex 135|jecyrillic 458|jsuperior 2B2|k 6B|kabashkircyrillic 4A1|"
            + "kacyrillic 43A|kadescendercyrillic 49B|kahookcyrillic 4C4|kappa 3BA|kappasymbolgreek 3F0|"
            + "kastrokecyrillic 49F|kaverticalstrokecyrillic 49D|kcaron 1E9|kcedilla 137|kcommaaccent 137|"
            + "kgreenlandic 138|khacyrillic 445|kheicoptic 3E7|khook 199|kjecyrillic 45C|koppacyrillic 481|"
            + "ksicyrillic 46F|l 6C|lacute 13A|lambda 3BB|lambdastroke 19B|largecircle 25EF|lbar 19A|lcaron 13E|"
            + "lcedilla 13C|lcommaaccent 13C|ldot 140|ldotaccent 140|less 3C|lessequal 2264|"
            + "lessequalorgreater 22DA|lessorequivalent 2272|lessorgreater 2276|lessoverequal 2266|lira 20A4|"
            + "lj 1C9|ljecyrillic 459|logicaland 2227|logicalnot AC|logicalnotreversed 2310|logicalor 2228|"
            + "longs 17F|lozenge 25CA|lslash 142|lsquare 2113|m 6D|macron AF|macronlowmod 2CD|male 2642|"
            + "mars 2642|middot B7|minus 2212|minuscircle 2296|minusmod 2D7|minusplus 2213|minute 2032|mu B5|"
            + "mu1 B5|muchgreater 226B|muchless 226A|mugreek 3BC|multiply D7|musicalnote 266A|"
            + "musicalnotedbl 266B|musicflatsign 266D|musicsharpsign 266F|n 6E|nabla 2207|nacute 144|"
            + "napostrophe 149|nbspace A0|ncaron 148|ncedilla 146|ncommaaccent 146|newsheqelsign 20AA|nine 39|"
            + "ninecircleinversesansserif 2792|nineroman 2178|nj 1CC|njecyrillic 45A|nlegrightlong 19E|"
            + "nonbreakingspace A0|notcontains 220C|notelement 2209|notelementof 2209|notequal 2260|"
            + "notgreater 226F|notgreaternorequal 2271|notgreaternorless 2279|notidentical 2262|notless 226E|"
            + "notlessnorequal 2270|notparallel 2226|notprecedes 2280|notsubset 2284|notsucceeds 2281|"
            + "notsuperset 2285|ntilde F1|nu 3BD|numbersign 23|numeralsigngreek 374|numeralsignlowergreek 375|"
            + "numero 2116|o 6F|oacute F3|obarredcyrillic 4E9|obarreddieresiscyrillic 4EB|obreve 14F|ocaron 1D2|"
            + "ocircumflex F4|ocyrillic 43E|odblacute 151|odblgrave 20D|odieresis F6|odieresiscyrillic 4E7|"
            + "oe 153|ogonek 2DB|ograve F2|ohorn 1A1|ohungarumlaut 151|oi 1A3|oinvertedbreve 20F|omacron 14D|"
            + "omega 3C9|omega1 3D6|omegacyrillic 461|omegaroundcyrillic 47B|omegatitlocyrillic 47D|"
            + "omegatonos 3CE|omicron 3BF|omicrontonos 3CC|one 31|onecircleinversesansserif 278A|"
            + "onedotenleader 2024|oneeighth 215B|onehalf BD|onequarter BC|oneroman 2170|onesuperior B9|"
            + "onethird 2153|oogonek 1EB|oogonekmacron 1ED|openbullet 25E6|option 2325|ordfeminine AA|"
            + "ordmasculine BA|orthogonal 221F|oslash F8|oslashacute 1FF|ostrokeacute 1FF|otcyrillic 47F|"
            + "otilde F5|overline 203E|overscore AF|p 70|pagedown 21DF|pageup 21DE|"
            + "palatalizationcyrilliccmb 484|palochkacyrillic 4C0|paragraph B6|parallel 2225|parenleft 28|"
            + "parenright 29|partialdiff 2202|pecyrillic 43F|pemiddlehookcyrillic 4A7|percent 25|period 2E|"
            + "periodcentered B7|perpendicular 22A5|perthousand 2030|peseta 20A7|phi 3C6|phi1 3D5|"
            + "phisymbolgreek 3D5|phook 1A5|pi 3C0|pisymbolgreek 3D6|plus 2B|pluscircle 2295|plusminus B1|"
            + "plusmod 2D6|pointingindexdownwhite 261F|pointingindexleftwhite 261C|pointingindexrightwhite 261E|"
            + "pointingindexupwhite 261D|precedes 227A|prescription 211E|primemod 2B9|primereversed 2035|"
            + "product 220F|projective 2305|propellor 2318|propersubset 2282|propersuperset 2283|"
            + "proportion 2237|proportional 221D|psi 3C8|psicyrillic 471|psilipneumatacyrilliccmb 486|q 71|"
            + "quarternote 2669|question 3F|questiondown BF|questiongreek 37E|quotedbl 22|quotedblbase 201E|"
            + "quotedblleft 201C|quotedblright 201D|quoteleft 2018|quoteleftreversed 201B|quotereversed 201B|"
            + "quoteright 2019|quoterightn 149|quotesinglbase 201A|quotesingle 27|r 72|racute 155|radical 221A|"
            + "ratio 2236|rcaron 159|rcedilla 157|rcommaaccent 157|rdblgrave 211|referencemark 203B|"
            + "reflexsubset 2286|reflexsuperset 2287|registered AE|reversedtilde 223D|revlogicalnot 2310|"
            + "rho 3C1|rhookturnedsuperior 2B5|rhosymbolgreek 3F1|rhotichookmod 2DE|rightangle 221F|"
            + "righttriangle 22BF|ring 2DA|ringhalfleft 2BF|ringhalfleftcentered 2D3|ringhalfright 2BE|"
            + "ringhalfrightcentered 2D2|rinvertedbreve 213|rturnedsuperior 2B4|s 73|sacute 15B|scaron 161|"
            + "scedilla 15F|schwacyrillic 4D9|schwadieresiscyrillic 4DB|scircumflex 15D|scommaaccent 219|"
            + "second 2033|secondtonechinese 2CA|section A7|semicolon 3B|seven 37|"
            + "sevencircleinversesansserif 2790|seveneighths 215E|sevenroman 2176|sfthyphen AD|shacyrillic 448|"
            + "shchacyrillic 449|sheicoptic 3E3|sheqel 20AA|sheqelhebrew 20AA|shhacyrillic 4BB|shimacoptic 3ED|"
            + "sigma 3C3|sigma1 3C2|sigmafinal 3C2|sigmalunatesymbolgreek 3F2|similar 223C|six 36|"
            + "sixcircleinversesansserif 278F|sixroman 2175|slash 2F|slong 17F|smileface 263A|softhyphen AD|"
            + "softsigncyrillic 44C|space 20|spacehackarabic 20|spade 2660|spadesuitblack 2660|"
            + "spadesuitwhite 2664|squarediagonalcrosshatchfill 25A9|squarehorizontalfill 25A4|"
            + "squareorthogonalcrosshatchfill 25A6|squareupperlefttolowerrightfill 25A7|"
            + "squareupperrighttolowerleftfill 25A8|squareverticalfill 25A5|squarewhitewithsmallblack 25A3|"
            + "sterling A3|subset 2282|subsetnotequal 228A|subsetorequal 2286|succeeds 227B|suchthat 220B|"
            + "summation 2211|sun 263C|superset 2283|supersetnotequal 228B|supersetorequal 2287|t 74|"
            + "tackdown 22A4|tackleft 22A3|tau 3C4|tbar 167|tcaron 165|tcedilla 163|tcommaaccent 163|"
            + "tecyrillic 442|tedescendercyrillic 4AD|telephone 2121|telephoneblack 260E|tenroman 2179|"
            + "tetsecyrillic 4B5|thereexists 2203|therefore 2234|theta 3B8|theta1 3D1|thetasymbolgreek 3D1|"
            + "thook 1AD|thorn FE|thousandcyrillic 482|three 33|threecircleinversesansserif 278C|"
            + "threeeighths 215C|threequarters BE|threeroman 2172|threesuperior B3|tilde 2DC|tildeoperator 223C|"
            + "timescircle 2297|titlocyrilliccmb 483|tonebarextrahighmod 2E5|tonebarextralowmod 2E9|"
            + "tonebarhighmod 2E6|tonebarlowmod 2E8|tonebarmidmod 2E7|tonefive 1BD|tonesix 185|tonetwo 1A8|"
            + "tonos 384|tpalatalhook 1AB|trademark 2122|triagdn 25BC|triaglf 25C4|triagrt 25BA|triagup 25B2|"
            + "tsecyrillic 446|tshecyrillic 45B|twelveroman 217B|two 32|twocircleinversesansserif 278B|"
            + "twodotenleader 2025|twodotleader 2025|tworoman 2171|twostroke 1BB|twosuperior B2|twothirds 2154|"
            + "u 75|uacute FA|ubreve 16D|ucaron 1D4|ucircumflex FB|ucyrillic 443|udblacute 171|udblgrave 215|"
            + "udieresis FC|udieresisacute 1D8|udieresiscaron 1DA|udieresiscyrillic 4F1|udieresisgrave 1DC|"
            + "udieresismacron 1D6|ugrave F9|uhorn 1B0|uhungarumlaut 171|uhungarumlautcyrillic 4F3|"
            + "uinvertedbreve 217|ukcyrillic 479|umacron 16B|umacroncyrillic 4EF|underscore 5F|"
            + "underscoredbl 2017|union 222A|universal 2200|uogonek 173|upsilon 3C5|upsilondieresis 3CB|"
            + "upsilondieresistonos 3B0|upsilontonos 3CD|uptackmod 2D4|uring 16F|ushortcyrillic 45E|"
            + "ustraightcyrillic 4AF|ustraightstrokecyrillic 4B1|utilde 169|v 76|vecyrillic 432|venus 2640|"
            + "verticalbar 7C|verticallinelowmod 2CC|verticallinemod 2C8|w 77|wcircumflex 175|weierstrass 2118|"
            + "whitebullet 25E6|whitecircle 25CB|whitecircleinverse 25D9|whitediamond 25C7|"
            + "whitediamondcontainingblacksmalldiamond 25C8|whitedownpointingsmalltriangle 25BF|"
            + "whitedownpointingtriangle 25BD|whiteleftpointingsmalltriangle 25C3|"
            + "whiteleftpointingtriangle 25C1|whiterightpointingsmalltriangle 25B9|"
            + "whiterightpointingtriangle 25B7|whitesmallsquare 25AB|whitesmilingface 263A|whitesquare 25A1|"
            + "whitestar 2606|whitetelephone 260F|whiteuppointingsmalltriangle 25B5|"
            + "whiteuppointingtriangle 25B3|won 20A9|wsuperior 2B7|wynn 1BF|x 78|xi 3BE|xsuperior 2E3|y 79|"
            + "yacute FD|yatcyrillic 463|ycircumflex 177|ydieresis FF|yen A5|yericyrillic 44B|"
            + "yerudieresiscyrillic 4F9|yhook 1B4|yicyrillic 457|yinyang 262F|yotgreek 3F3|ypogegrammeni 37A|"
            + "yr 1A6|ysuperior 2B8|yusbigcyrillic 46B|yusbigiotifiedcyrillic 46D|yuslittlecyrillic 467|"
            + "yuslittleiotifiedcyrillic 469|z 7A|zacute 17A|zcaron 17E|zdot 17C|zdotaccent 17C|zecyrillic 437|"
            + "zedescendercyrillic 499|zedieresiscyrillic 4DF|zero 30|zerowidthnonjoiner 200C|"
            + "zerowidthspace 200B|zeta 3B6|zhebrevecyrillic 4C2|zhecyrillic 436|zhedescendercyrillic 497|"
            + "zhedieresiscyrillic 4DD|zstroke 1B6";

        public const string WHelvetica = "11611616322C22C37929B0BF14D14D18524811614D11611622C22C22C22C22C22C22C22C22C22C11611624824824822C"
            + "3F729B29B2D22D229B26330A2D21161F429B22C3412D230A29B30A2D229B2632D229B3B029B29B2631161161161D522C"
            + "14D22C22C1F422C22C11622C22C0DE0DE1F40DE34122C22C22C22C14D1F411622C1F42D21F41F41F414E10414E24815E"
            + "22C15E0DE22C14D3E822C22C14D3E829B14D3E815E26315E15E0DE0DE14D14D15E22C3E814D3E81F414D3B015E1F429B"
            + "11614D22C22C22C22C10422C14D2E117222C24814D2E114D19024814D14D14D22C21911614D14D16D22C342342342263"
            + "29B29B29B29B29B29B3E82D229B29B29B29B1161161161162D22D230A30A30A30A30A24830A2D22D22D22D229B29B263"
            + "22C22C22C22C22C22C3791F422C22C22C22C11611611611622C22C22C22C22C22C22C24826322C22C22C22C1F422C1F4";

        public const string WHelveticaBold = "11614D1DA22C22C3792D20EE14D14D18524811614D11611622C22C22C22C22C22C22C22C22C22C14D14D248248248263"
            + "3CF2D22D22D22D229B26330A2D211622C2D22633412D230A29B30A2D229B2632D229B3B029B29B26314D11614D24822C"
            + "14D22C26322C26322C14D26326311611622C11637926326326326318522C14D26322C30A22C22C1F418511818524815E"
            + "22C15E11622C1F43E822C22C14D3E829B14D3E815E26315E15E1161161F41F415E22C3E814D3E822C14D3B015E1F429B"
            + "11614D22C22C22C22C11822C14D2E117222C24814D2E114D19024814D14D14D26322C11614D14D16D22C342342342263"
            + "2D22D22D22D22D22D23E82D229B29B29B29B1161161161162D22D230A30A30A30A30A24830A2D22D22D22D229B29B263"
            + "22C22C22C22C22C22C37922C22C22C22C22C11611611611626326326326326326326324826326326326326322C26322C";

        public const string WTimesRoman = "0FA14D1981F41F434130A0B414D14D1F42340FA14D0FA1161F41F41F41F41F41F41F41F41F41F41161162342342341BC"
            + "3992D229B29B2D226322C2D22D214D1852D22633792D22D222C2D229B22C2632D22D23B02D22D226314D11614D1D51F4"
            + "14D1BC1F41BC1F41BC14D1F41F41161161F411630A1F41F41F41F414D1851161F41F42D21F41F41BC1E00C81E021D15E"
            + "1F415E14D1F41BC3E81F41F414D3E822C14D37915E26315E15E14D14D1BC1BC15E1F43E814D3D418514D2D215E1BC2D2"
            + "0FA14D1F41F41F41F40C81F414D2F81141F423414D2F814D19023412C12C14D1F41C50FA14D12C1361F42EE2EE2EE1BC"
            + "2D22D22D22D22D22D237929B26326326326314D14D14D14D2D22D22D22D22D22D22D22342D22D22D22D22D22D222C1F4"
            + "1BC1BC1BC1BC1BC1BC29B1BC1BC1BC1BC1BC1161161161161F41F41F41F41F41F41F42341F41F41F41F41F41F41F41F4";

        public const string WTimesBold = "0FA14D22B1F41F43E834111614D14D1F423A0FA14D0FA1161F41F41F41F41F41F41F41F41F41F414D14D23A23A23A1F4"
            + "3A22D229B2D22D229B26330A30A1851F430A29B3B02D230A26330A2D222C29B2D22D23E82D22D229B14D11614D2451F4"
            + "14D1F422C1BC22C1BC14D1F422C11614D22C11634122C1F422C22C1BC18514D22C1F42D21F41F41BC18A0DC18A20815E"
            + "1F415E14D1F41F43E81F41F414D3E822C14D3E815E29B15E15E14D14D1F41F415E1F43E814D3E818514D2D215E1BC2D2"
            + "0FA14D1F41F41F41F40DC1F414D2EB12C1F423A14D2EB14D19023A12C12C14D22C21C0FA14D12C14A1F42EE2EE2EE1F4"
            + "2D22D22D22D22D22D23E82D229B29B29B29B1851851851852D22D230A30A30A30A30A23A30A2D22D22D22D22D226322C"
            + "1F41F41F41F41F41F42D21BC1BC1BC1BC1BC1161161161161F422C1F41F41F41F41F423A1F422C22C22C22C1F422C1F4";

        public const string WTimesItalic = "0FA14D1A41F41F434130A0D614D14D1F42A30FA14D0FA1161F41F41F41F41F41F41F41F41F41F414D14D2A32A32A31F4"
            + "39826326329B2D22632632D22D214D1BC29B22C34129B2D22632D22631F422C2D226334126322C22C1851161851A61F4"
            + "14D1F41F41BC1F41BC1161F41F41161161BC1162D21F41F41F41F41851851161F41BC29B1BC1BC18519011319021D15E"
            + "1F415E14D1F422C3791F41F414D3E81F414D3B015E22C15E15E14D14D22C22C15E1F437914D3D418514D29B15E18522C"
            + "0FA1851F41F41F41F41131F414D2F81141F42A314D2F814D1902A312C12C14D1F420B0FA14D12C1361F42EE2EE2EE1F4"
            + "26326326326326326337929B26326326326314D14D14D14D2D229B2D22D22D22D22D22A32D22D22D22D22D222C2631F4"
            + "1F41F41F41F41F41F429B1BC1BC1BC1BC1BC1161161161161F41F41F41F41F41F41F42A31F41F41F41F41F41BC1F41BC";

        public const string WTimesBoldItalic = "0FA18522B1F41F434130A11614D14D1F423A0FA14D0FA1161F41F41F41F41F41F41F41F41F41F414D14D23A23A23A1F4"
            + "34029B29B29B2D229B29B2D230A1851F429B2633792D22D22632D229B22C2632D229B37929B26326314D11614D23A1F4"
            + "14D1F41F41BC1F41BC14D1F422C1161161F411630A22C1F41F41F418518511622C1BC29B1F41BC18515C0DC15C23A15E"
            + "1F415E14D1F41F43E81F41F414D3E822C14D3B015E26315E15E14D14D1F41F415E1F43E814D3E818514D2D215E185263"
            + "0FA1851F41F41F41F40DC1F414D2EB10A1F425E14D2EB14D19023A12C12C14D2401F40FA14D12C12C1F42EE2EE2EE1F4"
            + "29B29B29B29B29B29B3B029B29B29B29B29B1851851851852D22D22D22D22D22D22D223A2D22D22D22D22D22632631F4"
            + "1F41F41F41F41F41F42D21BC1BC1BC1BC1BC1161161161161F422C1F41F41F41F41F423A1F422C22C22C22C1BC1F41BC";
    }
}
