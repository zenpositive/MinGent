using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MinGent
{
    class TtfFont
    {
        public string Path = "";
        public int Index;
        public string Name = "";
        public int UnitsPerEm = 1000;
        public int NumGlyphs;
        public short Ascent, Descent, LineGap, CapHeight, XMin, YMin, XMax, YMax;
        public int Weight = 400;
        public bool Italic, Fixed;
        public double ItalicAngle;
        public bool Embeddable = true;
        public int Flags;
        readonly byte[] data;
        readonly Dictionary<string, KeyValuePair<int, int>> tables = new Dictionary<string, KeyValuePair<int, int>>(StringComparer.Ordinal);
        ushort[] advances;
        int numHMetrics;
        bool longLoca;
        Dictionary<int, int> cmap;

        TtfFont(byte[] data) { this.data = data; }

        public static TtfFont Load(string path, int index)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < 12 || fi.Length > 64L * 1024 * 1024) return null;
                byte[] d = File.ReadAllBytes(path);
                TtfFont f = new TtfFont(d);
                f.Path = path;
                f.Index = index;
                int off = 0;
                if (Tag(d, 0) == "ttcf")
                {
                    int n = (int)U32(d, 8);
                    if (index < 0 || index >= n) return null;
                    off = (int)U32(d, 12 + 4 * index);
                }
                if (!f.Parse(off)) return null;
                return f;
            }
            catch (Exception) { return null; }
        }

        static string Tag(byte[] d, int at) { return Encoding.ASCII.GetString(d, at, 4); }
        static uint U32(byte[] d, int at) { return (uint)(d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3]); }
        static ushort U16(byte[] d, int at) { return (ushort)(d[at] << 8 | d[at + 1]); }
        static short S16(byte[] d, int at) { return (short)(d[at] << 8 | d[at + 1]); }

        bool Has(string tag) { return tables.ContainsKey(tag); }
        int Off(string tag) { return tables[tag].Key; }
        int Len(string tag) { return tables[tag].Value; }

        bool Parse(int off)
        {
            uint version = U32(data, off);
            if (version != 0x00010000 && version != 0x74727565) return false;
            int n = U16(data, off + 4);
            for (int i = 0; i < n; i++)
            {
                int r = off + 12 + 16 * i;
                if (r + 16 > data.Length) return false;
                string tag = Tag(data, r);
                int to = (int)U32(data, r + 8), tl = (int)U32(data, r + 12);
                if (to < 0 || tl < 0 || (long)to + tl > data.Length) continue;
                tables[tag] = new KeyValuePair<int, int>(to, tl);
            }
            foreach (string need in new string[] { "head", "hhea", "maxp", "hmtx", "loca", "glyf", "cmap" }) if (!Has(need)) return false;
            int h = Off("head");
            UnitsPerEm = Math.Max(16, (int)U16(data, h + 18));
            XMin = S16(data, h + 36); YMin = S16(data, h + 38); XMax = S16(data, h + 40); YMax = S16(data, h + 42);
            int macStyle = U16(data, h + 44);
            longLoca = S16(data, h + 50) == 1;
            NumGlyphs = U16(data, Off("maxp") + 4);
            int hh = Off("hhea");
            Ascent = S16(data, hh + 4); Descent = S16(data, hh + 6); LineGap = S16(data, hh + 8);
            numHMetrics = Math.Max(1, (int)U16(data, hh + 34));
            advances = new ushort[NumGlyphs];
            int hm = Off("hmtx");
            ushort last = 0;
            for (int g = 0; g < NumGlyphs; g++)
            {
                if (g < numHMetrics && hm + 4 * g + 2 <= data.Length) last = U16(data, hm + 4 * g);
                advances[g] = last;
            }
            Italic = (macStyle & 2) != 0;
            Weight = (macStyle & 1) != 0 ? 700 : 400;
            CapHeight = (short)(Ascent * 0.7);
            if (Has("OS/2") && Len("OS/2") >= 78)
            {
                int o = Off("OS/2");
                int ver = U16(data, o);
                Weight = U16(data, o + 4);
                int fsType = U16(data, o + 8);
                Embeddable = (fsType & 0x000F) != 0x0002 && (fsType & 0x0200) == 0;
                short typoAsc = S16(data, o + 68), typoDesc = S16(data, o + 70);
                int winAsc = U16(data, o + 74), winDesc = U16(data, o + 76);
                if (winAsc > 0) { Ascent = (short)Math.Min(32767, winAsc); Descent = (short)(-Math.Min(32767, winDesc)); LineGap = 0; }
                else if (typoAsc > 0) { Ascent = typoAsc; Descent = typoDesc; }
                if (ver >= 2 && Len("OS/2") >= 90) { short ch = S16(data, o + 88); if (ch > 0) CapHeight = ch; }
                int fsSel = U16(data, o + 62);
                if ((fsSel & 1) != 0) Italic = true;
            }
            if (Has("post") && Len("post") >= 16)
            {
                int p = Off("post");
                ItalicAngle = S16(data, p + 4) + U16(data, p + 6) / 65536.0;
                Fixed = U32(data, p + 12) != 0;
            }
            Name = ReadName();
            Flags = (Fixed ? 1 : 0) | 32 | (Italic ? 64 : 0);
            return NumGlyphs > 0;
        }

        string ReadName()
        {
            if (!Has("name")) return "Font";
            try
            {
                int n = Off("name");
                int count = U16(data, n + 2), strOff = n + U16(data, n + 4);
                string best = null;
                int bestRank = 99;
                for (int i = 0; i < count; i++)
                {
                    int r = n + 6 + 12 * i;
                    int plat = U16(data, r), lang = U16(data, r + 4), id = U16(data, r + 6), len = U16(data, r + 8), so = U16(data, r + 10);
                    if (id != 6) continue;
                    string s;
                    if (plat == 3 || plat == 0) s = Encoding.BigEndianUnicode.GetString(data, strOff + so, len);
                    else if (plat == 1) s = Encoding.ASCII.GetString(data, strOff + so, len);
                    else continue;
                    int rank = plat == 3 && lang == 0x409 ? 0 : plat == 3 ? 1 : 2;
                    if (rank < bestRank) { bestRank = rank; best = s; }
                }
                if (best == null) return "Font";
                StringBuilder sb = new StringBuilder();
                foreach (char c in best) if (c > ' ' && c < 127 && "[](){}<>/%#".IndexOf(c) < 0) sb.Append(c);
                return sb.Length > 0 ? sb.ToString() : "Font";
            }
            catch (Exception) { return "Font"; }
        }

        void BuildCmap()
        {
            cmap = new Dictionary<int, int>();
            int c = Off("cmap");
            int n = U16(data, c + 2);
            int best = -1, bestRank = 99;
            for (int i = 0; i < n; i++)
            {
                int r = c + 4 + 8 * i;
                int plat = U16(data, r), enc = U16(data, r + 2);
                int sub = c + (int)U32(data, r + 4);
                if (sub + 4 > data.Length) continue;
                int fmt = U16(data, sub);
                int rank = 99;
                if (fmt == 12 && (plat == 3 && enc == 10 || plat == 0)) rank = 0;
                else if (fmt == 4 && plat == 3 && enc == 1) rank = 1;
                else if (fmt == 4 && plat == 0) rank = 2;
                else if (fmt == 4 && plat == 3 && enc == 0) rank = 3;
                if (rank < bestRank) { bestRank = rank; best = sub; }
            }
            if (best < 0) return;
            int f = U16(data, best);
            if (f == 4)
            {
                int segX2 = U16(data, best + 6);
                int ends = best + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
                for (int s = 0; s < segX2 / 2; s++)
                {
                    int end = U16(data, ends + 2 * s), start = U16(data, starts + 2 * s);
                    short delta = S16(data, deltas + 2 * s);
                    int ro = U16(data, ranges + 2 * s);
                    if (start > end || start == 0xFFFF) continue;
                    for (int ch = start; ch <= end && ch < 0xFFFF; ch++)
                    {
                        int g;
                        if (ro == 0) g = (ch + delta) & 0xFFFF;
                        else
                        {
                            int at = ranges + 2 * s + ro + 2 * (ch - start);
                            if (at + 2 > data.Length) continue;
                            g = U16(data, at);
                            if (g != 0) g = (g + delta) & 0xFFFF;
                        }
                        if (g != 0 && g < NumGlyphs) cmap[ch] = g;
                    }
                }
            }
            else if (f == 12)
            {
                uint groups = U32(data, best + 12);
                for (uint gi = 0; gi < groups && gi < 200000; gi++)
                {
                    int r = best + 16 + (int)gi * 12;
                    if (r + 12 > data.Length) break;
                    uint s0 = U32(data, r), e0 = U32(data, r + 4), g0 = U32(data, r + 8);
                    if (e0 < s0 || e0 - s0 > 0x10FFFF) continue;
                    for (uint ch = s0; ch <= e0 && ch <= 0x10FFFF; ch++)
                    {
                        uint g = g0 + (ch - s0);
                        if (g != 0 && g < NumGlyphs) cmap[(int)ch] = (int)g;
                    }
                }
            }
        }

        public int Glyph(int codepoint)
        {
            if (cmap == null) BuildCmap();
            int g;
            return cmap.TryGetValue(codepoint, out g) ? g : 0;
        }

        public int Advance(int gid) { return gid >= 0 && gid < advances.Length ? advances[gid] : 0; }

        int GlyphOffset(int gid, out int length)
        {
            int loca = Off("loca");
            int a, b;
            if (longLoca) { a = (int)U32(data, loca + 4 * gid); b = (int)U32(data, loca + 4 * gid + 4); }
            else { a = U16(data, loca + 2 * gid) * 2; b = U16(data, loca + 2 * gid + 2) * 2; }
            length = Math.Max(0, Math.Min(b - a, Len("glyf") - a));
            return Off("glyf") + a;
        }

        public void AddComponents(HashSet<int> used)
        {
            List<int> todo = new List<int>(used);
            int guard = 0;
            while (todo.Count > 0 && guard++ < 100000)
            {
                int gid = todo[todo.Count - 1];
                todo.RemoveAt(todo.Count - 1);
                if (gid <= 0 || gid >= NumGlyphs) continue;
                int len;
                int at = GlyphOffset(gid, out len);
                if (len < 10 || S16(data, at) >= 0) continue;
                int p = at + 10;
                for (int k = 0; k < 64 && p + 4 <= at + len; k++)
                {
                    int flags = U16(data, p), comp = U16(data, p + 2);
                    if (comp < NumGlyphs && used.Add(comp)) todo.Add(comp);
                    p += 4 + ((flags & 1) != 0 ? 4 : 2);
                    if ((flags & 8) != 0) p += 2;
                    else if ((flags & 0x40) != 0) p += 4;
                    else if ((flags & 0x80) != 0) p += 8;
                    if ((flags & 0x20) == 0) break;
                }
            }
        }

        public byte[] Subset(HashSet<int> used, Dictionary<int, int> unicodeToGid)
        {
            HashSet<int> keep = new HashSet<int>(used);
            keep.Add(0);
            AddComponents(keep);
            MemoryStream glyf = new MemoryStream();
            byte[] loca = new byte[4 * (NumGlyphs + 1)];
            for (int g = 0; g < NumGlyphs; g++)
            {
                Put32(loca, 4 * g, (uint)glyf.Length);
                if (!keep.Contains(g)) continue;
                int len;
                int at = GlyphOffset(g, out len);
                if (len > 0) { glyf.Write(data, at, len); while (glyf.Length % 4 != 0) glyf.WriteByte(0); }
            }
            Put32(loca, 4 * NumGlyphs, (uint)glyf.Length);
            Dictionary<string, byte[]> outTables = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            byte[] head = Copy("head");
            Put32(head, 8, 0);
            head[50] = 0; head[51] = 1;
            outTables["head"] = head;
            outTables["hhea"] = Copy("hhea");
            outTables["maxp"] = Copy("maxp");
            outTables["hmtx"] = Copy("hmtx");
            outTables["loca"] = loca;
            outTables["glyf"] = glyf.ToArray();
            outTables["cmap"] = SmallCmap(unicodeToGid);
            outTables["post"] = Post();
            foreach (string t in new string[] { "cvt ", "fpgm", "prep", "OS/2", "name", "gasp" }) if (Has(t)) outTables[t] = Copy(t);
            List<string> tags = new List<string>(outTables.Keys);
            tags.Sort(StringComparer.Ordinal);
            int n = tags.Count;
            int entry = 1, log = 0;
            while (entry * 2 <= n) { entry *= 2; log++; }
            MemoryStream ms = new MemoryStream();
            byte[] hdr = new byte[12 + 16 * n];
            Put32(hdr, 0, 0x00010000);
            Put16(hdr, 4, n); Put16(hdr, 6, entry * 16); Put16(hdr, 8, log); Put16(hdr, 10, n * 16 - entry * 16);
            int offset = hdr.Length;
            List<byte[]> bodies = new List<byte[]>();
            for (int i = 0; i < n; i++)
            {
                byte[] t = outTables[tags[i]];
                Encoding.ASCII.GetBytes(tags[i], 0, 4, hdr, 12 + 16 * i);
                Put32(hdr, 12 + 16 * i + 4, Checksum(t));
                Put32(hdr, 12 + 16 * i + 8, (uint)offset);
                Put32(hdr, 12 + 16 * i + 12, (uint)t.Length);
                bodies.Add(t);
                offset += (t.Length + 3) & ~3;
            }
            ms.Write(hdr, 0, hdr.Length);
            foreach (byte[] t in bodies) { ms.Write(t, 0, t.Length); for (int pad = t.Length; pad % 4 != 0; pad++) ms.WriteByte(0); }
            byte[] font = ms.ToArray();
            uint sum = Checksum(font);
            int headAt = -1;
            for (int i = 0; i < n; i++) if (tags[i] == "head") headAt = (int)U32(hdr, 12 + 16 * i + 8);
            if (headAt >= 0) Put32(font, headAt + 8, unchecked(0xB1B0AFBA - sum));
            return font;
        }

        byte[] Copy(string tag)
        {
            byte[] b = new byte[Len(tag)];
            Buffer.BlockCopy(data, Off(tag), b, 0, b.Length);
            return b;
        }

        byte[] Post()
        {
            byte[] p = new byte[32];
            Put32(p, 0, 0x00030000);
            if (Has("post") && Len("post") >= 32) Buffer.BlockCopy(data, Off("post") + 4, p, 4, 28);
            return p;
        }

        static byte[] SmallCmap(Dictionary<int, int> map)
        {
            List<int> codes = new List<int>();
            foreach (KeyValuePair<int, int> kv in map) if (kv.Key > 0 && kv.Key < 0xFFFF && kv.Value > 0) codes.Add(kv.Key);
            codes.Sort();
            List<int[]> segs = new List<int[]>();
            foreach (int c in codes)
            {
                int g = map[c];
                if (segs.Count > 0)
                {
                    int[] last = segs[segs.Count - 1];
                    if (last[1] == c - 1 && last[2] + (c - last[0]) == g) { last[1] = c; continue; }
                }
                segs.Add(new int[] { c, c, g });
            }
            segs.Add(new int[] { 0xFFFF, 0xFFFF, 1 });
            int segX2 = segs.Count * 2;
            int len = 16 + segs.Count * 8;
            byte[] sub = new byte[len];
            Put16(sub, 0, 4); Put16(sub, 2, len); Put16(sub, 4, 0);
            Put16(sub, 6, segX2);
            int entry = 1, log = 0;
            while (entry * 2 <= segs.Count) { entry *= 2; log++; }
            Put16(sub, 8, 2 * entry); Put16(sub, 10, log); Put16(sub, 12, segX2 - 2 * entry);
            for (int i = 0; i < segs.Count; i++)
            {
                Put16(sub, 14 + 2 * i, segs[i][1]);
                Put16(sub, 16 + segX2 + 2 * i, segs[i][0]);
                Put16(sub, 16 + 2 * segX2 + 2 * i, (segs[i][2] - segs[i][0]) & 0xFFFF);
                Put16(sub, 16 + 3 * segX2 + 2 * i, 0);
            }
            byte[] t = new byte[12 + len];
            Put16(t, 0, 0); Put16(t, 2, 1);
            Put16(t, 4, 3); Put16(t, 6, 1); Put32(t, 8, 12);
            Buffer.BlockCopy(sub, 0, t, 12, len);
            return t;
        }

        static uint Checksum(byte[] b)
        {
            uint sum = 0;
            int n = (b.Length + 3) / 4;
            for (int i = 0; i < n; i++)
            {
                uint v = 0;
                for (int k = 0; k < 4; k++) { int at = 4 * i + k; v = (v << 8) | (at < b.Length ? b[at] : (byte)0); }
                sum = unchecked(sum + v);
            }
            return sum;
        }

        static void Put32(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }
        static void Put16(byte[] b, int at, int v) { b[at] = (byte)(v >> 8); b[at + 1] = (byte)v; }
    }

    static class FontFiles
    {
        static Dictionary<string, string> index;
        static readonly object gate = new object();

        static IEnumerable<string> Dirs()
        {
            string win = Environment.GetEnvironmentVariable("WINDIR");
            if (!string.IsNullOrEmpty(win)) yield return System.IO.Path.Combine(win, "Fonts");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local)) yield return System.IO.Path.Combine(local, "Microsoft", "Windows", "Fonts");
            if (System.IO.Path.DirectorySeparatorChar == '/')
            {
                yield return "/usr/share/fonts";
                yield return "/usr/local/share/fonts";
            }
        }

        static void Build()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string d in Dirs())
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    SearchOption deep = System.IO.Path.DirectorySeparatorChar == '/' ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    foreach (string f in Directory.GetFiles(d, "*.*", deep))
                    {
                        string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".ttf" && ext != ".ttc") continue;
                        string name = System.IO.Path.GetFileName(f);
                        if (!map.ContainsKey(name)) map[name] = f;
                    }
                }
                catch (Exception) { }
            }
            index = map;
        }

        public static string Find(string fileName)
        {
            lock (gate)
            {
                if (index == null) Build();
                string p;
                return index.TryGetValue(fileName, out p) ? p : null;
            }
        }
    }

    class FontCache
    {
        readonly Dictionary<string, TtfFont> loaded = new Dictionary<string, TtfFont>(StringComparer.OrdinalIgnoreCase);

        public TtfFont Get(string spec)
        {
            TtfFont f;
            if (loaded.TryGetValue(spec, out f)) return f;
            string file = spec;
            int index = 0;
            int hash = spec.IndexOf('#');
            if (hash > 0) { file = spec.Substring(0, hash); int.TryParse(spec.Substring(hash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out index); }
            string path = FontFiles.Find(file);
            f = path != null ? TtfFont.Load(path, index) : null;
            if (f != null && !f.Embeddable) f = null;
            loaded[spec] = f;
            return f;
        }

        public TtfFont First(string[] specs)
        {
            foreach (string s in specs)
            {
                TtfFont f = Get(s);
                if (f != null) return f;
            }
            return null;
        }
    }
}
