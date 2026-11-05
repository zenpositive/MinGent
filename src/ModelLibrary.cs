using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace MinGent
{
    class GgufInfo
    {
        public string Architecture = "", Name = "", BaseName = "", SizeLabel = "", Type = "";
        public int FileType = -1, ContextLength, BlockCount, SplitCount, SplitNo;
        public string ChatTemplate;
        public double? Temp, TopP, MinP;
        public int? TopK;
        public long ParameterCount;
    }

    class GgufShape
    {
        public string Architecture = "";
        public int Layers, Context, HeadKv, Heads, Embed, KeyLength, ValueLength, Window, Interval, LoraRank, RopeDims, SwaPeriod = -1, KeyLengthSwa, ValueLengthSwa, SharedKv;
        public int[] HeadKvLayers, SwaLayers, RecurrentLayers;
        public long Params;

        public bool Known
        {
            get
            {
                if (Layers <= 0 || Layers > 4096) return false;
                if (LoraRank > 0) return true;
                bool heads = HeadKv > 0 || HeadKvLayers != null || Heads > 0;
                bool dims = KeyLength > 0 || (Embed > 0 && Heads > 0);
                return heads && dims;
            }
        }

        static int DefaultPeriod(string arch)
        {
            switch (arch)
            {
                case "gemma2": case "gpt-oss": return 2;
                case "gemma3": return 6;
                case "gemma3n": return 5;
                case "cohere2": case "llama4": case "exaone4": return 4;
            }
            return 1;
        }

        bool Sliding(int layer)
        {
            if (SwaLayers != null) return layer < SwaLayers.Length && SwaLayers[layer] != 0;
            int p = SwaPeriod >= 0 ? SwaPeriod : DefaultPeriod(Architecture);
            if (p == 0) return true;
            if (p == 1) return false;
            return layer % p < p - 1;
        }

        public long KvBytes(int ctx, double bytesPerElem)
        {
            if (!Known || ctx <= 0) return 0;
            int heads = Heads > 0 ? Heads : Math.Max(1, HeadKv);
            int kl = KeyLength > 0 ? KeyLength : (Embed > 0 ? Embed / heads : 0);
            int vl = ValueLength > 0 ? ValueLength : kl;
            int interval = Interval;
            if (interval <= 0 && RecurrentLayers == null && (Architecture == "qwen35" || Architecture == "qwen35moe" || Architecture == "qwen3next")) interval = 4;
            int kvLayers = SharedKv > 0 && SharedKv < Layers ? Layers - SharedKv : Layers;
            double total = 0;
            for (int l = 0; l < kvLayers; l++)
            {
                if (RecurrentLayers != null) { if (l < RecurrentLayers.Length && RecurrentLayers[l] != 0) continue; }
                else if (interval > 1 && (l + 1) % interval != 0) continue;
                bool swa = Window > 0 && Sliding(l);
                double elems;
                if (LoraRank > 0) elems = 2.0 * ((double)LoraRank + RopeDims);
                else
                {
                    int hkv = HeadKvLayers != null ? (l < HeadKvLayers.Length ? HeadKvLayers[l] : 0) : (HeadKv > 0 ? HeadKv : heads);
                    if (hkv <= 0) continue;
                    int k = swa && KeyLengthSwa > 0 ? KeyLengthSwa : kl, v = swa && ValueLengthSwa > 0 ? ValueLengthSwa : (swa && KeyLengthSwa > 0 ? KeyLengthSwa : vl);
                    elems = (double)hkv * ((double)k + v);
                }
                long tokens = swa ? Math.Min(ctx, Window) : ctx;
                total += elems * tokens;
            }
            double bytes = total * bytesPerElem;
            return bytes >= 4e18 ? 4000000000000000000L : (long)bytes;
        }
    }

    static class Gguf
    {
        const uint Magic = 0x46554747;
        const long MaxString = 64L * 1024 * 1024;
        const long MaxArray = 64L * 1024 * 1024;

        public static GgufInfo Read(string path, out string error)
        {
            error = null;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
                using (BinaryReader br = new BinaryReader(fs, Encoding.UTF8))
                {
                    if (fs.Length < 24) { error = "too small to be a GGUF file"; return null; }
                    if (br.ReadUInt32() != Magic) { error = "not a GGUF file"; return null; }
                    uint version = br.ReadUInt32();
                    if (version < 2 || version > 3) { error = "unsupported GGUF version " + version; return null; }
                    br.ReadUInt64();
                    ulong kvCount = br.ReadUInt64();
                    if (kvCount > 100000) { error = "corrupt GGUF header"; return null; }
                    GgufInfo g = new GgufInfo();
                    Dictionary<string, object> kv = new Dictionary<string, object>();
                    for (ulong i = 0; i < kvCount; i++)
                    {
                        string key = ReadString(br, fs, 4096);
                        uint type = br.ReadUInt32();
                        bool want = key.StartsWith("general.") || key.EndsWith(".context_length") || key.EndsWith(".block_count") || key == "tokenizer.chat_template" || key.StartsWith("split.");
                        object v = ReadValue(br, fs, type, want, key == "tokenizer.chat_template" ? 4 * 1024 * 1024 : 4096);
                        if (want && v != null) kv[key] = v;
                    }
                    g.Architecture = Str(kv, "general.architecture");
                    g.Name = Str(kv, "general.name");
                    g.BaseName = Str(kv, "general.basename");
                    g.SizeLabel = Str(kv, "general.size_label");
                    g.Type = Str(kv, "general.type");
                    g.FileType = (int)Num(kv, "general.file_type", -1);
                    g.ParameterCount = (long)Num(kv, "general.parameter_count", 0);
                    g.ContextLength = (int)Num(kv, g.Architecture + ".context_length", 0);
                    g.BlockCount = (int)Num(kv, g.Architecture + ".block_count", 0);
                    g.SplitCount = (int)Num(kv, "split.count", 0);
                    g.SplitNo = (int)Num(kv, "split.no", 0);
                    g.ChatTemplate = kv.ContainsKey("tokenizer.chat_template") ? kv["tokenizer.chat_template"] as string : null;
                    if (kv.ContainsKey("general.sampling.temp")) g.Temp = Num(kv, "general.sampling.temp", 0);
                    if (kv.ContainsKey("general.sampling.top_p")) g.TopP = Num(kv, "general.sampling.top_p", 0);
                    if (kv.ContainsKey("general.sampling.min_p")) g.MinP = Num(kv, "general.sampling.min_p", 0);
                    if (kv.ContainsKey("general.sampling.top_k")) g.TopK = (int)Num(kv, "general.sampling.top_k", 0);
                    return g;
                }
            }
            catch (EndOfStreamException) { error = "the file ends early (incomplete download?)"; }
            catch (Exception ex) { error = ex.Message; }
            return null;
        }

        public static GgufShape ReadShape(byte[] data)
        {
            GgufShape g = new GgufShape();
            if (data == null || data.Length < 24) return g;
            Dictionary<string, object> kv = new Dictionary<string, object>();
            string arch = null;
            try
            {
                using (MemoryStream ms = new MemoryStream(data, false))
                using (BinaryReader br = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (br.ReadUInt32() != Magic) return g;
                    uint version = br.ReadUInt32();
                    if (version < 2 || version > 3) return g;
                    br.ReadUInt64();
                    ulong count = br.ReadUInt64();
                    if (count > 100000) return g;
                    for (ulong i = 0; i < count; i++)
                    {
                        ulong klen = br.ReadUInt64();
                        if (klen > 4096 || klen > (ulong)(ms.Length - ms.Position)) break;
                        string key = Encoding.UTF8.GetString(br.ReadBytes((int)klen));
                        uint type = br.ReadUInt32();
                        bool want = key == "general.architecture" || key == "general.parameter_count" || (arch != null && key.StartsWith(arch + ".", StringComparison.Ordinal));
                        object v = ShapeValue(br, ms, type, want);
                        if (want && v != null) kv[key] = v;
                        if (key == "general.architecture") arch = v as string;
                        if (key.StartsWith("tokenizer.", StringComparison.Ordinal) && arch != null) break;
                    }
                }
            }
            catch (IOException) { }
            catch (InvalidDataException) { }
            catch (ArgumentException) { }
            catch (OverflowException) { }
            if (string.IsNullOrEmpty(arch)) return g;
            g.Architecture = arch;
            string a = arch + ".";
            g.Params = (long)ShapeNum(kv, "general.parameter_count");
            g.Layers = (int)ShapeNum(kv, a + "block_count");
            g.Context = (int)ShapeNum(kv, a + "context_length");
            g.Embed = (int)ShapeNum(kv, a + "embedding_length");
            g.KeyLength = (int)ShapeNum(kv, a + "attention.key_length");
            g.ValueLength = (int)ShapeNum(kv, a + "attention.value_length");
            g.Window = (int)ShapeNum(kv, a + "attention.sliding_window");
            g.Interval = (int)ShapeNum(kv, a + "full_attention_interval");
            g.LoraRank = (int)ShapeNum(kv, a + "attention.kv_lora_rank");
            g.RopeDims = (int)ShapeNum(kv, a + "rope.dimension_count");
            g.KeyLengthSwa = (int)ShapeNum(kv, a + "attention.key_length_swa");
            g.ValueLengthSwa = (int)ShapeNum(kv, a + "attention.value_length_swa");
            g.SharedKv = (int)ShapeNum(kv, a + "attention.shared_kv_layers");
            object rec;
            if (kv.TryGetValue(a + "attention.recurrent_layers", out rec) && rec is int[]) g.RecurrentLayers = (int[])rec;
            object heads, headsKv, pattern;
            if (kv.TryGetValue(a + "attention.head_count", out heads)) g.Heads = heads is int[] ? Max((int[])heads) : (int)ShapeNum(kv, a + "attention.head_count");
            if (kv.TryGetValue(a + "attention.head_count_kv", out headsKv))
            {
                if (headsKv is int[]) g.HeadKvLayers = (int[])headsKv;
                else g.HeadKv = (int)ShapeNum(kv, a + "attention.head_count_kv");
            }
            if (kv.TryGetValue(a + "attention.sliding_window_pattern", out pattern))
            {
                if (pattern is int[]) g.SwaLayers = (int[])pattern;
                else g.SwaPeriod = (int)ShapeNum(kv, a + "attention.sliding_window_pattern");
            }
            return g;
        }

        static int Max(int[] a)
        {
            int m = 0;
            foreach (int x in a) if (x > m) m = x;
            return m;
        }

        static double ShapeNum(Dictionary<string, object> kv, string k)
        {
            object v;
            if (!kv.TryGetValue(k, out v) || !(v is double)) return 0;
            double d = (double)v;
            return double.IsNaN(d) || d < 0 || d > 1e15 ? 0 : d;
        }

        static object ShapeValue(BinaryReader br, MemoryStream ms, uint type, bool want)
        {
            if (type == 8)
            {
                ulong len = br.ReadUInt64();
                if (len > (ulong)(ms.Length - ms.Position)) throw new EndOfStreamException();
                if (want && len <= 256) return Encoding.UTF8.GetString(br.ReadBytes((int)len));
                ms.Seek((long)len, SeekOrigin.Current);
                return null;
            }
            if (type == 9)
            {
                uint et = br.ReadUInt32();
                ulong n = br.ReadUInt64();
                int size = FixedSize(et);
                if (size > 0)
                {
                    if ((double)n * size > ms.Length - ms.Position) throw new EndOfStreamException();
                    if (want && n <= 4096 && et != 6 && et != 12)
                    {
                        int[] arr = new int[(int)n];
                        for (int i = 0; i < arr.Length; i++) arr[i] = (int)Math.Max(-1, Math.Min(int.MaxValue, Scalar(br, et)));
                        return arr;
                    }
                    ms.Seek((long)n * size, SeekOrigin.Current);
                    return null;
                }
                if (et != 8 || n > (ulong)(ms.Length - ms.Position)) throw new EndOfStreamException();
                for (ulong i = 0; i < n; i++)
                {
                    ulong len = br.ReadUInt64();
                    if (len > (ulong)(ms.Length - ms.Position)) throw new EndOfStreamException();
                    ms.Seek((long)len, SeekOrigin.Current);
                }
                return null;
            }
            if (FixedSize(type) == 0) throw new InvalidDataException("unknown type");
            double d = Scalar(br, type);
            return want ? (object)d : null;
        }

        static double Scalar(BinaryReader br, uint type)
        {
            switch (type)
            {
                case 0: return br.ReadByte();
                case 1: return br.ReadSByte();
                case 2: return br.ReadUInt16();
                case 3: return br.ReadInt16();
                case 4: return br.ReadUInt32();
                case 5: return br.ReadInt32();
                case 6: return br.ReadSingle();
                case 7: return br.ReadByte() != 0 ? 1 : 0;
                case 10: return br.ReadUInt64();
                case 11: return br.ReadInt64();
                case 12: return br.ReadDouble();
            }
            throw new InvalidDataException("unknown type");
        }

        static string Str(Dictionary<string, object> kv, string k) { object v; return kv.TryGetValue(k, out v) ? v as string ?? "" : ""; }

        static double Num(Dictionary<string, object> kv, string k, double def)
        {
            object v;
            if (!kv.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return def; }
        }

        static string ReadString(BinaryReader br, FileStream fs, int keep)
        {
            ulong len = br.ReadUInt64();
            if (len > (ulong)MaxString || (long)len > fs.Length - fs.Position) throw new InvalidDataException("corrupt string length");
            if ((long)len <= keep) return Encoding.UTF8.GetString(br.ReadBytes((int)len));
            fs.Seek((long)len, SeekOrigin.Current);
            return null;
        }

        static object ReadValue(BinaryReader br, FileStream fs, uint type, bool want, int keepString)
        {
            return ReadValue(br, fs, type, want, keepString, 0);
        }

        static object ReadValue(BinaryReader br, FileStream fs, uint type, bool want, int keepString, int depth)
        {
            if (depth > 4) throw new InvalidDataException("arrays nested too deeply");
            switch (type)
            {
                case 0: return (double)br.ReadByte();
                case 1: return (double)br.ReadSByte();
                case 2: return (double)br.ReadUInt16();
                case 3: return (double)br.ReadInt16();
                case 4: return (double)br.ReadUInt32();
                case 5: return (double)br.ReadInt32();
                case 6: return (double)br.ReadSingle();
                case 7: return br.ReadByte() != 0;
                case 8: return ReadString(br, fs, want ? keepString : 0);
                case 9:
                    {
                        uint et = br.ReadUInt32();
                        ulong n = br.ReadUInt64();
                        if (n > (ulong)MaxArray) throw new InvalidDataException("corrupt array length");
                        int size = FixedSize(et);
                        if (size > 0)
                        {
                            long skip = (long)n * size;
                            if (skip > fs.Length - fs.Position) throw new InvalidDataException("corrupt array");
                            fs.Seek(skip, SeekOrigin.Current);
                            return null;
                        }
                        for (ulong i = 0; i < n; i++) ReadValue(br, fs, et, false, 0, depth + 1);
                        return null;
                    }
                case 10: return (double)br.ReadUInt64();
                case 11: return (double)br.ReadInt64();
                case 12: return br.ReadDouble();
            }
            throw new InvalidDataException("unknown GGUF value type " + type);
        }

        static int FixedSize(uint t)
        {
            switch (t)
            {
                case 0: case 1: case 7: return 1;
                case 2: case 3: return 2;
                case 4: case 5: case 6: return 4;
                case 10: case 11: case 12: return 8;
            }
            return 0;
        }

        static readonly string[] FileTypes = {
            "F32", "F16", "Q4_0", "Q4_1", "Q4_1_F16", "", "", "Q8_0", "Q5_0", "Q5_1", "Q2_K", "Q3_K_S", "Q3_K_M", "Q3_K_L",
            "Q4_K_S", "Q4_K_M", "Q5_K_S", "Q5_K_M", "Q6_K", "IQ2_XXS", "IQ2_XS", "Q2_K_S", "IQ3_XS", "IQ3_XXS", "IQ1_S",
            "IQ4_NL", "IQ3_S", "IQ3_M", "IQ2_S", "IQ2_M", "IQ4_XS", "IQ1_M", "BF16", "", "", "", "TQ1_0", "TQ2_0", "MXFP4" };

        static readonly Regex QuantInName = new Regex(@"(?<![A-Za-z0-9])(I?Q\d(?:_[A-Z0-9]+)*|BF16|F16|F32|MXFP4|UD-[A-Z0-9_]+)(?![A-Za-z0-9])", RegexOptions.IgnoreCase);

        public static bool Helper(string path, long size)
        {
            string p = (path ?? "").Replace('\\', '/');
            string fn = p.Substring(p.LastIndexOf('/') + 1).ToLowerInvariant();
            if (fn.StartsWith("mtp-") || fn.StartsWith("mtp_") || fn.StartsWith("ggml-vocab") || fn.IndexOf("imatrix", StringComparison.Ordinal) >= 0) return true;
            foreach (string seg in p.Split('/'))
                if (seg.Equals("mtp", StringComparison.OrdinalIgnoreCase)) return true;
            return size > 0 && size < 50L * 1024 * 1024;
        }

        public static string QuantName(GgufInfo g, string fileName)
        {
            Match m = QuantInName.Match(Path.GetFileNameWithoutExtension(fileName ?? ""));
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
            if (g != null && g.FileType >= 0 && g.FileType < FileTypes.Length && FileTypes[g.FileType].Length > 0) return FileTypes[g.FileType];
            return "";
        }
    }

    class LocalModel
    {
        public string Path = "", FileName = "", Folder = "", Publisher = "", Repo = "";
        public string DisplayName = "", Quant = "", ParamsText = "", Architecture = "", SourceLabel = "";
        public long Bytes, TotalBytes;
        public int TrainContext, PartIndex, PartCount;
        public bool IsProjector, Thinking, NativeEffort, CanStopThinking, ToolTemplate, OwnDownload, Helper, Incomplete;
        public string ProjectorPath, Error;
        public List<string> EffortNames;
        public double? Temp, TopP, MinP;
        public int? TopK;
        public DateTime Modified;
    }

    static class ModelLibrary
    {
        static readonly object gate = new object();
        static List<LocalModel> cache;
        static DateTime cacheWhen;
        static readonly Dictionary<string, KeyValuePair<long, LocalModel>> parsed = new Dictionary<string, KeyValuePair<long, LocalModel>>(StringComparer.OrdinalIgnoreCase);
        static readonly Regex Part = new Regex(@"-(\d{5})-of-(\d{5})\.gguf$", RegexOptions.IgnoreCase);

        public static string DefaultRoot { get { return System.IO.Path.Combine(Program.DataDir ?? System.IO.Path.GetTempPath(), "models"); } }

        static string customRoot;
        static readonly List<string> oldRoots = new List<string>();

        public static string OwnRoot
        {
            get
            {
                string c = customRoot;
                return string.IsNullOrEmpty(c) ? DefaultRoot : c;
            }
        }

        public static bool CustomFolder { get { return !string.IsNullOrEmpty(customRoot); } }

        public static void Apply(Settings st)
        {
            EngineUpdates.Enabled = st.OfferEngineUpdates;
            lock (gate)
            {
                customRoot = string.IsNullOrEmpty(st.ModelsFolder) ? null : st.ModelsFolder;
                oldRoots.Clear();
                foreach (string o in (st.ModelsOldFolders ?? "").Split('|'))
                    if (o.Trim().Length > 0) oldRoots.Add(o.Trim());
                cache = null;
            }
        }

        public static List<string> OwnRoots()
        {
            List<string> l = new List<string>();
            List<string> olds;
            lock (gate) olds = new List<string>(oldRoots);
            foreach (string r in new string[] { OwnRoot, DefaultRoot })
                if (!l.Exists(delegate (string x) { return string.Equals(x, r, StringComparison.OrdinalIgnoreCase); })) l.Add(r);
            foreach (string r in olds)
                if (!l.Exists(delegate (string x) { return string.Equals(x, r, StringComparison.OrdinalIgnoreCase); })) l.Add(r);
            return l;
        }

        public static string CheckFolder(string folder)
        {
            string full;
            try { full = System.IO.Path.GetFullPath(folder ?? ""); } catch { return "That is not a valid folder."; }
            if (full.TrimEnd('\\', '/').Length <= 2 || System.IO.Path.GetPathRoot(full).TrimEnd('\\', '/') == full.TrimEnd('\\', '/')) return "Pick a folder, not a whole drive.";
            string blocked = AgentPaths.WriteBlocked(full);
            if (blocked != null) return "Windows and program folders can't hold models. Pick a folder of your own.";
            string shell = ShellFolder(full);
            if (shell != null) return "Pick a folder of its own for models, for example a new folder inside " + shell + ", not " + shell + " itself.";
            foreach (KeyValuePair<string, string> other in OtherAppRoots())
                if (SameOrUnder(full, other.Key) || SameOrUnder(other.Key, full))
                    return "That folder holds " + other.Value + "'s models. MinGent finds them by itself; pick another folder for the models it downloads.";
            try
            {
                Directory.CreateDirectory(full);
                string probe = System.IO.Path.Combine(full, ".mingent-write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
            }
            catch (Exception ex) { return "MinGent can't write to that folder (" + ex.Message + ")."; }
            return null;
        }

        static bool SameOrUnder(string path, string root)
        {
            try
            {
                string p = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/'), r = System.IO.Path.GetFullPath(root).TrimEnd('\\', '/');
                return string.Equals(p, r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static string ShellFolder(string full)
        {
            Environment.SpecialFolder[] special = { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos,
                Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData };
            foreach (Environment.SpecialFolder f in special)
            {
                string p = "";
                try { p = Environment.GetFolderPath(f); } catch { }
                if (p.Length == 0) continue;
                if (SameOrUnder(p, full))
                {
                    if (SameOrUnder(full, p)) return System.IO.Path.GetFileName(p.TrimEnd('\\', '/'));
                    return System.IO.Path.GetFileName(full.TrimEnd('\\', '/'));
                }
                if (f == Environment.SpecialFolder.UserProfile)
                {
                    string downloads = System.IO.Path.Combine(p, "Downloads");
                    if (SameOrUnder(downloads, full)) return SameOrUnder(full, downloads) ? "Downloads" : System.IO.Path.GetFileName(full.TrimEnd('\\', '/'));
                }
            }
            if (!string.IsNullOrEmpty(Program.DataDir) && SameOrUnder(Program.DataDir, full) && !SameOrUnder(full, Program.DataDir))
                return System.IO.Path.GetFileName(full.TrimEnd('\\', '/'));
            return null;
        }

        static List<KeyValuePair<string, string>> OtherAppRoots()
        {
            List<KeyValuePair<string, string>> l = new List<KeyValuePair<string, string>>();
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home)) return l;
                l.Add(new KeyValuePair<string, string>(System.IO.Path.Combine(home, ".lmstudio"), "LM Studio"));
                l.Add(new KeyValuePair<string, string>(System.IO.Path.Combine(System.IO.Path.Combine(home, ".cache"), "lm-studio"), "LM Studio"));
                l.Add(new KeyValuePair<string, string>(System.IO.Path.Combine(System.IO.Path.Combine(home, ".cache"), "huggingface"), "the Hugging Face cache"));
            }
            catch { }
            return l;
        }

        public static string LocalPathFor(string repoId, HubVariant v, List<LocalModel> have)
        {
            if (v == null || v.Files.Count == 0) return null;
            string first = System.IO.Path.GetFileName(v.Files[0].Path);
            foreach (LocalModel m in have)
                if (string.Equals(m.FileName, first, StringComparison.OrdinalIgnoreCase) && m.TotalBytes == v.TotalBytes) return m.Path;
            return null;
        }

        static readonly Dictionary<string, KeyValuePair<long, GgufShape>> shapeCache = new Dictionary<string, KeyValuePair<long, GgufShape>>(StringComparer.OrdinalIgnoreCase);

        public static GgufShape ShapeOf(string path)
        {
            long stamp = 0;
            try { FileInfo fi = new FileInfo(path); stamp = fi.Length ^ fi.LastWriteTimeUtc.Ticks; } catch { }
            lock (shapeCache)
            {
                KeyValuePair<long, GgufShape> hit;
                if (stamp != 0 && shapeCache.TryGetValue(path, out hit) && hit.Key == stamp) return hit.Value;
            }
            GgufShape found = ReadShapeOf(path);
            if (stamp != 0) lock (shapeCache) shapeCache[path] = new KeyValuePair<long, GgufShape>(stamp, found);
            return found;
        }

        static GgufShape ReadShapeOf(string path)
        {
            byte[] head;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
            {
                int n = (int)Math.Min(fs.Length, 1 << 20), got = 0, r;
                head = new byte[n];
                while (got < n && (r = fs.Read(head, got, n - got)) > 0) got += r;
                if (got < n) Array.Resize(ref head, got);
            }
            GgufShape g = Gguf.ReadShape(head);
            return g.Known ? g : null;
        }

        public static List<KeyValuePair<string, string>> Roots()
        {
            List<KeyValuePair<string, string>> roots = new List<KeyValuePair<string, string>>();
            foreach (string own in OwnRoots()) roots.Add(new KeyValuePair<string, string>(own, "MinGent"));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string lmDefault = System.IO.Path.Combine(System.IO.Path.Combine(home, ".lmstudio"), "models");
            try
            {
                string settings = System.IO.Path.Combine(System.IO.Path.Combine(home, ".lmstudio"), "settings.json");
                if (File.Exists(settings) && new FileInfo(settings).Length < 4 * 1024 * 1024)
                {
                    object o = Json.TryParse(File.ReadAllText(settings));
                    string dl = Json.Str(o, "downloadsFolder");
                    if (!string.IsNullOrEmpty(dl) && Directory.Exists(dl)) roots.Add(new KeyValuePair<string, string>(dl, "LM Studio"));
                }
            }
            catch { }
            roots.Add(new KeyValuePair<string, string>(lmDefault, "LM Studio"));
            roots.Add(new KeyValuePair<string, string>(System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(home, ".cache"), "lm-studio"), "models"), "LM Studio"));
            roots.Add(new KeyValuePair<string, string>(System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(home, ".cache"), "huggingface"), "hub"), "Hugging Face cache"));
            List<KeyValuePair<string, string>> unique = new List<KeyValuePair<string, string>>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> r in roots)
            {
                string full;
                try { full = System.IO.Path.GetFullPath(r.Key).TrimEnd('\\', '/'); } catch { continue; }
                if (seen.Add(full)) unique.Add(new KeyValuePair<string, string>(full, r.Value));
            }
            return unique;
        }

        public static List<LocalModel> Scan(bool force)
        {
            lock (gate)
            {
                if (!force && cache != null && (DateTime.UtcNow - cacheWhen).TotalSeconds < 20) return new List<LocalModel>(cache);
            }
            List<LocalModel> found = new List<LocalModel>();
            HashSet<string> seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> root in Roots())
            {
                if (!Directory.Exists(root.Key)) continue;
                List<string> files = new List<string>();
                Walk(root.Key, 0, files);
                foreach (string f in files)
                {
                    string real = f;
                    try { real = System.IO.Path.GetFullPath(f); } catch { }
                    if (!seenFiles.Add(real)) continue;
                    LocalModel m = Describe(real, root.Key, root.Value);
                    if (m != null) found.Add(m);
                }
            }
            Dictionary<string, List<LocalModel>> byFolder = new Dictionary<string, List<LocalModel>>(StringComparer.OrdinalIgnoreCase);
            foreach (LocalModel m in found)
            {
                List<LocalModel> l;
                if (!byFolder.TryGetValue(m.Folder, out l)) { l = new List<LocalModel>(); byFolder[m.Folder] = l; }
                l.Add(m);
            }
            foreach (List<LocalModel> group in byFolder.Values)
            {
                LocalModel proj = null;
                foreach (LocalModel m in group) if (m.IsProjector && (proj == null || m.FileName.IndexOf("f16", StringComparison.OrdinalIgnoreCase) >= 0)) proj = m;
                foreach (LocalModel m in group)
                {
                    if (m.IsProjector || m.Helper) continue;
                    m.ProjectorPath = proj != null ? proj.Path : null;
                    if (m.Incomplete)
                    {
                        m.Incomplete = false;
                        if (m.Error != null && m.Error.StartsWith("incomplete", StringComparison.Ordinal)) m.Error = null;
                    }
                    if (m.PartCount > 1 && m.PartIndex == 1)
                    {
                        long total = 0;
                        HashSet<int> parts = new HashSet<int>();
                        foreach (LocalModel o in group)
                            if (o.PartCount == m.PartCount && Part.Replace(o.FileName, "") == Part.Replace(m.FileName, "")) { total += o.Bytes; parts.Add(o.PartIndex); }
                        m.TotalBytes = total;
                        if (parts.Count < m.PartCount)
                        {
                            m.Incomplete = true;
                            m.Error = "incomplete: " + parts.Count + " of " + m.PartCount + " parts";
                        }
                    }
                }
            }
            found.Sort(delegate (LocalModel a, LocalModel b) { return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase); });
            lock (gate) { cache = found; cacheWhen = DateTime.UtcNow; }
            return new List<LocalModel>(found);
        }

        static void Walk(string dir, int depth, List<string> files)
        {
            if (depth > 6 || files.Count > 4000) return;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.gguf")) files.Add(f);
                foreach (string d in Directory.GetDirectories(dir))
                {
                    string name = System.IO.Path.GetFileName(d);
                    if (name.StartsWith(".") || name == "blobs") continue;
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0 && depth > 3) continue; } catch { continue; }
                    Walk(d, depth + 1, files);
                }
            }
            catch { }
        }

        static LocalModel Describe(string path, string root, string source)
        {
            FileInfo fi;
            try { fi = new FileInfo(path); if (!fi.Exists || fi.Length < 1024) return null; } catch { return null; }
            long stamp = fi.Length ^ fi.LastWriteTimeUtc.Ticks;
            lock (gate)
            {
                KeyValuePair<long, LocalModel> hit;
                if (parsed.TryGetValue(path, out hit) && hit.Key == stamp) return hit.Value;
            }
            LocalModel m = new LocalModel();
            m.Path = path;
            m.FileName = fi.Name;
            m.Folder = fi.DirectoryName ?? "";
            m.Bytes = m.TotalBytes = fi.Length;
            m.Modified = fi.LastWriteTimeUtc;
            m.SourceLabel = source;
            m.OwnDownload = source == "MinGent";
            string rel = path.Length > root.Length ? path.Substring(root.Length).TrimStart('\\', '/') : fi.Name;
            string[] segs = rel.Split('\\', '/');
            if (source == "Hugging Face cache" && segs.Length > 0 && segs[0].StartsWith("models--"))
            {
                string[] pr = segs[0].Substring(8).Split(new string[] { "--" }, StringSplitOptions.None);
                if (pr.Length >= 2) { m.Publisher = pr[0]; m.Repo = pr[1]; }
            }
            else if (segs.Length >= 3) { m.Publisher = segs[0]; m.Repo = segs[1]; }
            else if (segs.Length == 2) m.Repo = segs[0];
            Match pm = Part.Match(fi.Name);
            if (pm.Success)
            {
                m.PartIndex = int.Parse(pm.Groups[1].Value, CultureInfo.InvariantCulture);
                m.PartCount = int.Parse(pm.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            string err;
            GgufInfo g = m.PartIndex > 1 ? null : Gguf.Read(path, out err);
            if (m.PartIndex <= 1 && g == null) m.Error = "unreadable";
            bool proj = ModelTraits.IsProjectorName(fi.Name) || (g != null && (g.Type == "mmproj" || g.Architecture == "clip"));
            m.IsProjector = proj;
            m.Helper = !proj && (Gguf.Helper(rel, fi.Length) || (g != null && !string.IsNullOrEmpty(g.Type) && g.Type != "model"));
            if (g != null)
            {
                m.Architecture = g.Architecture;
                m.TrainContext = g.ContextLength;
                m.Quant = Gguf.QuantName(g, fi.Name);
                m.ParamsText = g.SizeLabel;
                if (string.IsNullOrEmpty(m.ParamsText) && g.ParameterCount > 0) m.ParamsText = FormatParams(g.ParameterCount);
                ModelTraits.FromTemplate(g.ChatTemplate, out m.ToolTemplate, out m.Thinking, out m.NativeEffort);
                m.CanStopThinking = ModelTraits.CanStopThinking(g.ChatTemplate);
                m.EffortNames = m.NativeEffort ? ModelTraits.EffortNames(g.ChatTemplate) : null;
                m.Temp = g.Temp; m.TopP = g.TopP; m.MinP = g.MinP; m.TopK = g.TopK;
                string nm = !string.IsNullOrEmpty(g.Name) ? g.Name : null;
                m.DisplayName = nm ?? CleanName(fi.Name);
            }
            else m.DisplayName = CleanName(fi.Name);
            if (string.IsNullOrEmpty(m.Quant)) m.Quant = Gguf.QuantName(null, fi.Name);
            if (m.DisplayName.Length > 0 && m.Quant.Length > 0 && m.DisplayName.IndexOf(m.Quant, StringComparison.OrdinalIgnoreCase) < 0 && !proj)
                m.DisplayName = m.DisplayName + " " + m.Quant;
            lock (gate) parsed[path] = new KeyValuePair<long, LocalModel>(stamp, m);
            return m;
        }

        static string CleanName(string file)
        {
            string n = System.IO.Path.GetFileNameWithoutExtension(file);
            n = Part.Replace(n + ".gguf", "").Replace(".gguf", "");
            return n.Replace('_', ' ').Trim();
        }

        static string FormatParams(long n)
        {
            if (n >= 1000000000) return (n / 1e9).ToString(n >= 10000000000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "B";
            if (n >= 1000000) return (n / 1e6).ToString("0", CultureInfo.InvariantCulture) + "M";
            return n.ToString(CultureInfo.InvariantCulture);
        }

        public static bool DeleteOwn(LocalModel m, out string error)
        {
            error = null;
            if (!m.OwnDownload) { error = "Only models downloaded by MinGent can be deleted here."; return false; }
            try
            {
                string full = System.IO.Path.GetFullPath(m.Path);
                string root = null;
                foreach (string own in OwnRoots())
                {
                    string r = System.IO.Path.GetFullPath(own).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                    if (full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) { root = r; break; }
                }
                if (root == null) { error = "That file is outside MinGent's model folders."; return false; }
                List<string> victims = new List<string>();
                if (m.PartCount > 1)
                {
                    foreach (string f in Directory.GetFiles(m.Folder, "*.gguf"))
                        if (Part.Replace(System.IO.Path.GetFileName(f), "") == Part.Replace(m.FileName, "")) victims.Add(f);
                }
                else victims.Add(full);
                foreach (string v in victims) if (!RecycleBin.Send(v)) File.Delete(v);
                lock (gate) cache = null;
                string proj = m.ProjectorPath != null ? System.IO.Path.GetFullPath(m.ProjectorPath) : null;
                if (proj != null && proj.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(proj))
                {
                    bool used = false;
                    foreach (string f in Directory.GetFiles(m.Folder, "*.gguf"))
                    {
                        string ff = System.IO.Path.GetFullPath(f);
                        if (string.Equals(ff, proj, StringComparison.OrdinalIgnoreCase)) continue;
                        if (System.IO.Path.GetFileName(ff).IndexOf("mmproj", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        used = true;
                    }
                    if (!used && !RecycleBin.Send(proj)) File.Delete(proj);
                }
                try
                {
                    string folder = System.IO.Path.GetFullPath(m.Folder);
                    for (int up = 0; up < 2 && folder.StartsWith(root, StringComparison.OrdinalIgnoreCase) && folder.Length > root.Length && Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0; up++)
                    {
                        Directory.Delete(folder);
                        folder = System.IO.Path.GetDirectoryName(folder);
                    }
                }
                catch { }
                lock (gate) cache = null;
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
    }

    class GpuInfo
    {
        public string Name = "", Vendor = "";
        public long VramBytes;
        public string DriverVersion = "";
        public int NvidiaDriver;

        public bool Nvidia { get { return NvidiaDriver > 0 || (Name ?? "").IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0; } }

        public bool Integrated { get { return Hardware.LooksIntegrated(Name, VramBytes); } }
    }

    static class Hardware
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        static List<GpuInfo> gpus;
        static long ram = -1;

        public static void Use(List<GpuInfo> list, long ramBytes)
        {
            if (list != null)
            {
                list = new List<GpuInfo>(list);
                list.Sort(delegate (GpuInfo a, GpuInfo b) { return b.VramBytes.CompareTo(a.VramBytes); });
            }
            gpus = list;
            ram = ramBytes;
        }

        public static long RamBytes
        {
            get
            {
                if (ram >= 0) return ram;
                long v = 0;
                try { MEMORYSTATUSEX m = new MEMORYSTATUSEX(); if (GlobalMemoryStatusEx(m)) v = (long)m.ullTotalPhys; } catch { }
                ram = v;
                return v;
            }
        }

        public static List<GpuInfo> Gpus()
        {
            if (gpus != null) return gpus;
            List<GpuInfo> list = new List<GpuInfo>();
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls != null)
                        foreach (string sub in cls.GetSubKeyNames())
                        {
                            if (sub.Length != 4) continue;
                            try
                            {
                                using (RegistryKey k = cls.OpenSubKey(sub))
                                {
                                    if (k == null) continue;
                                    string desc = k.GetValue("DriverDesc") as string;
                                    if (string.IsNullOrEmpty(desc)) continue;
                                    GpuInfo g = new GpuInfo();
                                    g.Name = desc;
                                    g.Vendor = (k.GetValue("ProviderName") as string) ?? "";
                                    g.DriverVersion = (k.GetValue("DriverVersion") as string) ?? "";
                                    object q = k.GetValue("HardwareInformation.qwMemorySize");
                                    if (q is long) g.VramBytes = (long)q;
                                    else if (q is byte[] && ((byte[])q).Length >= 8) g.VramBytes = BitConverter.ToInt64((byte[])q, 0);
                                    if (g.VramBytes <= 0)
                                    {
                                        object d = k.GetValue("HardwareInformation.MemorySize");
                                        if (d is int) g.VramBytes = (uint)(int)d;
                                        else if (d is byte[] && ((byte[])d).Length >= 4) g.VramBytes = BitConverter.ToUInt32((byte[])d, 0);
                                    }
                                    if (desc.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 || g.Vendor.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
                                        g.NvidiaDriver = NvidiaVersion(g.DriverVersion);
                                    if (desc.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 || desc.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                    list.Add(g);
                                }
                            }
                            catch { }
                        }
                }
            }
            catch { }
            list.Sort(delegate (GpuInfo a, GpuInfo b) { return b.VramBytes.CompareTo(a.VramBytes); });
            gpus = list;
            return list;
        }

        static int NvidiaVersion(string winVer)
        {
            string[] p = (winVer ?? "").Split('.');
            if (p.Length < 4) return 0;
            string tail = p[2] + p[3];
            if (tail.Length < 5) return 0;
            tail = tail.Substring(tail.Length - 5);
            int v;
            return int.TryParse(tail, out v) ? v : 0;
        }

        public static GpuInfo BestGpu
        {
            get
            {
                GpuInfo first = null;
                foreach (GpuInfo g in Gpus())
                {
                    if (!g.Integrated) return g;
                    if (first == null) first = g;
                }
                return first;
            }
        }

        public static bool LooksIntegrated(string name, long vramBytes)
        {
            string n = Regex.Replace(name ?? "", @"\((TM|R|C)\)|\u2122|\u00AE", " ", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+", " ").Trim();
            if (n.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (Regex.IsMatch(n, @"\bIntel\b", RegexOptions.IgnoreCase))
                return !Regex.IsMatch(n, @"\bArc (Pro )?[AB]\d{2,3}M?\b", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(n, @"\b(AMD|ATI|Radeon)\b", RegexOptions.IgnoreCase))
                return Regex.IsMatch(n, @"\bRadeon ((RX )?Vega \d+ Graphics|R[2-7] Graphics|Graphics|\d{3,4}[MS]\b)", RegexOptions.IgnoreCase);
            return vramBytes < (2L << 30);
        }

        public static List<GpuInfo> UsedBy(string backend)
        {
            List<GpuInfo> used = new List<GpuInfo>();
            if (backend == "cpu") return used;
            bool cuda = backend != null && backend.StartsWith("cuda", StringComparison.Ordinal);
            foreach (GpuInfo g in Gpus())
                if (cuda ? g.Nvidia : !g.Integrated) used.Add(g);
            if (cuda && used.Count > 1 && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUDA_VISIBLE_DEVICES"))) used.RemoveRange(1, used.Count - 1);
            if (!cuda && used.Count == 0 && BestGpu != null) used.Add(BestGpu);
            return used;
        }

        public static string EngineBackend()
        {
            string backend = null, why;
            try { backend = Engine.Instance.InstalledBackend; } catch { }
            return backend ?? RecommendedBackend(out why);
        }

        public static List<GpuInfo> EngineGpus() { return UsedBy(EngineBackend()); }

        public static long TotalVram(List<GpuInfo> list)
        {
            long v = 0;
            foreach (GpuInfo g in list) v += Math.Max(0, g.VramBytes);
            return v;
        }

        public static string ShortName(string name)
        {
            string n = (name ?? "").Trim();
            foreach (string pre in new string[] { "NVIDIA GeForce ", "NVIDIA ", "AMD Radeon ", "Intel(R) " })
                if (n.StartsWith(pre, StringComparison.OrdinalIgnoreCase) && n.Length > pre.Length + 2) return n.Substring(pre.Length);
            return n;
        }

        public static string Names(List<GpuInfo> list, bool shortNames)
        {
            List<string> names = new List<string>();
            List<int> counts = new List<int>();
            foreach (GpuInfo g in list)
            {
                string n = shortNames ? ShortName(g.Name) : (g.Name ?? "").Trim();
                int at = names.IndexOf(n);
                if (at >= 0) counts[at]++;
                else { names.Add(n); counts.Add(1); }
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                if (counts[i] > 1) sb.Append(counts[i]).Append("\u00D7 ");
                sb.Append(names[i]);
            }
            return sb.ToString();
        }

        static bool OldNvidia(string name)
        {
            string n = (name ?? "").ToUpperInvariant();
            return Regex.IsMatch(n, @"\b(GTX?\s?(6\d\d|7\d\d|8\d\d|9\d\d|10\d\d)|GT\s?\d{3,4}|\d{3}MX\b|\d{3}M\b|MX\s?[123]\d\d|QUADRO\s?(M|P|K|GP|GV)\d|TESLA\s?[MPKV]\d|TITAN\s?(X|XP|V|BLACK|Z)\b|P10\d-\d{3})");
        }

        public static string RecommendedBackend(out string why)
        {
            GpuInfo g = BestGpu;
            if (g == null) { why = "No graphics card found. The CPU build works on any PC."; return "cpu"; }
            if (!g.Nvidia)
            {
                List<GpuInfo> vk = UsedBy("vulkan");
                why = Names(vk, vk.Count > 1) + ": the Vulkan build uses " + (vk.Count > 1 ? "them all." : "it.");
                return "vulkan";
            }
            List<GpuInfo> nv = UsedBy("cuda13");
            string names = Names(nv, nv.Count > 1);
            long vram = TotalVram(nv);
            if (vram > 0 && vram < 3584L * 1024 * 1024)
            {
                why = names + (nv.Count > 1 ? " have only " : " has only ") + ModelCatalog.Size(vram) + " of graphics memory, so models run faster on the processor with the CPU build.";
                return "cpu";
            }
            bool old = false;
            foreach (GpuInfo x in nv) if (OldNvidia(x.Name)) old = true;
            int drv = g.NvidiaDriver;
            if (!old && drv >= 58000)
            {
                why = names + " with driver " + (drv / 100) + "." + (drv % 100).ToString("00") + ": CUDA 13 is the fastest option.";
                return "cuda13";
            }
            if (!old && drv >= 55100)
            {
                why = names + ": CUDA 12. Update the NVIDIA driver to 580 or newer for CUDA 13.";
                return "cuda12";
            }
            string reason = old ? (nv.Count > 1 ? "one of them is an older NVIDIA card" : "an older NVIDIA card")
                          : drv > 0 ? "the NVIDIA driver is older than 551" : "the NVIDIA driver version is unknown";
            why = names + ": " + reason + ", so the Vulkan build is the safe choice.";
            return "vulkan";
        }
    }

    class HubRepo
    {
        public string Id = "", Author = "";
        public long Downloads, Likes;
        public DateTime Modified, Created;
        public bool Gated, Official, Verified, GoodSize, Vision, Thinking, TemplateKnown, Tools;
        public string BaseModel = "", Pipeline = "";
        public List<string> Tags = new List<string>();

        static readonly Regex SizeInName = new Regex(@"(?<![A-Za-z0-9.])(\d+(?:\.\d+)?)[Bb](?:-A(\d+(?:\.\d+)?)[Bb])?(?![A-Za-z])", RegexOptions.CultureInvariant);

        public string SizeText
        {
            get
            {
                Match m = SizeInName.Match(Id.IndexOf('/') >= 0 ? Id.Substring(Id.IndexOf('/') + 1) : Id);
                if (!m.Success) return null;
                double total;
                if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out total) || total <= 0 || total > 5000) return null;
                string t = m.Groups[1].Value + "B";
                return m.Groups[2].Success ? t + ", " + m.Groups[2].Value + "B active" : t;
            }
        }

        public string License
        {
            get
            {
                foreach (string t in Tags)
                    if (t.StartsWith("license:", StringComparison.OrdinalIgnoreCase) && t.Length > 8) return t.Substring(8);
                return null;
            }
        }

        public string TrustLabel
        {
            get
            {
                if (Official) return "Official";
                if (Verified) return "Trusted publisher";
                if (Downloads < 1000) return "Few downloads";
                return "";
            }
        }

        public string Name
        {
            get
            {
                string n = Id.IndexOf('/') >= 0 ? Id.Substring(Id.IndexOf('/') + 1) : Id;
                foreach (string suf in new string[] { "-GGUF", "_GGUF", ".GGUF", "-gguf", "_gguf", "-imat", "-i1" })
                    if (n.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - suf.Length);
                if (n.EndsWith("-GGUF", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 5);
                n = n.Replace('_', ' ').Replace('-', ' ').Trim();
                while (n.IndexOf("  ", StringComparison.Ordinal) >= 0) n = n.Replace("  ", " ");
                return n.Length > 0 ? n : Id;
            }
        }
    }

    static class ModelTraits
    {
        public static void FromTemplate(string template, out bool tools, out bool thinking, out bool nativeEffort)
        {
            string t = template ?? "";
            nativeEffort = t.IndexOf("reasoning_effort", StringComparison.Ordinal) >= 0;
            thinking = nativeEffort || t.IndexOf("<think>", StringComparison.Ordinal) >= 0 || t.IndexOf("enable_thinking", StringComparison.Ordinal) >= 0 || t.IndexOf("reasoning_content", StringComparison.Ordinal) >= 0;
            tools = t.IndexOf("tools", StringComparison.Ordinal) >= 0;
        }

        public static bool CanStopThinking(string template)
        {
            return (template ?? "").IndexOf("enable_thinking", StringComparison.Ordinal) >= 0;
        }

        static readonly string[] EffortWords = { "none", "minimal", "low", "medium", "high", "xhigh", "max" };

        public static List<string> EffortNames(string template)
        {
            string t = template ?? "";
            if (t.IndexOf("reasoning_effort", StringComparison.Ordinal) < 0) return null;
            List<string> found = new List<string>();
            foreach (string w in EffortWords)
                if (t.IndexOf("'" + w + "'", StringComparison.Ordinal) >= 0 || t.IndexOf("\"" + w + "\"", StringComparison.Ordinal) >= 0) found.Add(w);
            int levels = 0;
            foreach (string w in found) if (w != "none") levels++;
            return levels >= 2 ? found : null;
        }

        public static bool IsProjectorName(string fileName)
        {
            return (fileName ?? "").IndexOf("mmproj", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    class HubFile
    {
        public string Path = "", Sha256 = "";
        public long Size;
    }

    class HubVariant
    {
        public string Quant = "", Label = "";
        public List<HubFile> Files = new List<HubFile>();
        public HubFile Projector;
        public long TotalBytes;
    }

    static class Hub
    {
        public const string Host = "https://huggingface.co";

        static readonly HashSet<string> TrustedPublishers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "lmstudio-community", "ggml-org", "unsloth", "bartowski", "mradermacher", "Qwen", "google", "microsoft", "meta-llama",
            "mistralai", "ibm-granite", "nvidia", "openai", "deepseek-ai", "allenai", "HuggingFaceTB", "LiquidAI", "zai-org",
            "moonshotai", "MiniMaxAI", "tencent", "baidu", "inclusionAI", "ByteDance-Seed", "stepfun-ai", "THUDM", "CohereLabs",
            "ServiceNow-AI", "NousResearch", "arcee-ai", "Mungert", "QuantFactory", "TheBloke" };

        static readonly HashSet<string> ChatPipelines = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "text-generation", "image-text-to-text", "text2text-generation", "conversational", "any-to-any", "visual-question-answering", "video-text-to-text" };

        public static string Api = Host;

        static readonly HashSet<string> Labs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Qwen", "google", "meta-llama", "mistralai", "microsoft", "openai", "deepseek-ai", "ibm-granite", "nvidia", "zai-org", "THUDM",
            "moonshotai", "MiniMaxAI", "tencent", "baidu", "inclusionAI", "ByteDance-Seed", "stepfun-ai", "allenai", "HuggingFaceTB", "LiquidAI",
            "CohereLabs", "ServiceNow-AI", "arcee-ai", "NousResearch", "swiss-ai", "internlm", "OpenGVLab", "LGAI-EXAONE", "upstage", "tiiuae",
            "XiaomiMiMo", "Alibaba-NLP", "Snowflake", "ai21labs", "apple" };

        static readonly string[] Preferred = { "lmstudio-community", "", "ggml-org", "unsloth", "bartowski" };

        static string Org(string id)
        {
            int i = (id ?? "").IndexOf('/');
            return i > 0 ? id.Substring(0, i) : "";
        }

        public static List<HubRepo> Parse(List<object> arr, int max)
        {
            List<HubRepo> list = new List<HubRepo>();
            foreach (object o in arr)
            {
                HubRepo h = new HubRepo();
                h.Id = Json.Str(o, "id") ?? Json.Str(o, "modelId") ?? "";
                if (h.Id.Length == 0 || Json.Bool(o, "private")) continue;
                h.Author = Json.Str(o, "author") ?? (h.Id.IndexOf('/') > 0 ? h.Id.Substring(0, h.Id.IndexOf('/')) : "");
                h.Downloads = Json.Long(o, "downloads");
                h.Likes = Json.Long(o, "likes");
                object gated = Json.Get(o, "gated");
                h.Gated = (gated is bool && (bool)gated) || (gated is string && (string)gated != "false");
                h.Pipeline = Json.Str(o, "pipeline_tag") ?? "";
                if (h.Pipeline.Length > 0 && !ChatPipelines.Contains(h.Pipeline)) continue;
                DateTime dt;
                if (DateTime.TryParse(Json.Str(o, "lastModified") ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out dt)) h.Modified = dt;
                if (DateTime.TryParse(Json.Str(o, "createdAt") ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out dt)) h.Created = dt;
                List<object> tags = Json.List(o, "tags");
                if (tags != null)
                    foreach (object t in tags)
                    {
                        string s = t as string;
                        if (s == null) continue;
                        h.Tags.Add(s);
                        if (s.StartsWith("base_model:") && !s.StartsWith("base_model:quantized:") && !s.StartsWith("base_model:finetune:") && !s.StartsWith("base_model:adapter:") && !s.StartsWith("base_model:merge:"))
                            h.BaseModel = s.Substring(11);
                        else if (s.StartsWith("base_model:quantized:") && h.BaseModel.Length == 0) h.BaseModel = s.Substring(21);
                    }
                List<object> files = Json.List(o, "siblings");
                if (files != null)
                    foreach (object f in files)
                    {
                        string name = System.IO.Path.GetFileName(Json.Str(f, "rfilename") ?? "");
                        if (ModelTraits.IsProjectorName(name) && name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) { h.Vision = true; break; }
                    }
                string template = Json.Str(o, "gguf", "chat_template");
                if (!string.IsNullOrEmpty(template))
                {
                    bool effort;
                    ModelTraits.FromTemplate(template, out h.Tools, out h.Thinking, out effort);
                    h.TemplateKnown = true;
                }
                string baseOrg = h.BaseModel.IndexOf('/') > 0 ? h.BaseModel.Substring(0, h.BaseModel.IndexOf('/')) : "";
                h.Verified = TrustedPublishers.Contains(h.Author);
                h.Official = h.Verified && baseOrg.Length > 0 && string.Equals(baseOrg, h.Author, StringComparison.OrdinalIgnoreCase);
                list.Add(h);
                if (list.Count >= max) break;
            }
            return list;
        }

        const string Fields = "&expand[]=author&expand[]=downloads&expand[]=likes&expand[]=gated&expand[]=pipeline_tag&expand[]=lastModified&expand[]=createdAt&expand[]=tags";

        public static string ListUrl(string query, string range, string sort, int limit, bool details)
        {
            return Api + "/api/models?filter=gguf&sort=" + sort + "&direction=-1&limit=" + limit + Fields + (details ? "&expand[]=siblings&expand[]=gguf" : "") +
                   (string.IsNullOrEmpty(query) ? "" : "&search=" + Uri.EscapeDataString(query.Trim())) +
                   (string.IsNullOrEmpty(range) ? "" : "&num_parameters=" + Uri.EscapeDataString(range));
        }

        static List<object> Query(string query, string range, string sort, int limit, bool details, CancelToken ct, out string error)
        {
            error = null;
            HttpResult r = Http.Get(ListUrl(query, range, sort, limit, details), null, 20000, ct);
            List<object> arr = r.Json as List<object>;
            if ((!r.Ok || arr == null) && r.Status >= 400 && r.Status < 500 && !(ct != null && ct.IsCancelled))
            {
                string plain = Api + "/api/models?filter=gguf&sort=" + sort + "&direction=-1&limit=" + limit + "&full=true" +
                               (string.IsNullOrEmpty(query) ? "" : "&search=" + Uri.EscapeDataString(query.Trim())) +
                               (string.IsNullOrEmpty(range) ? "" : "&num_parameters=" + Uri.EscapeDataString(range));
                r = Http.Get(plain, null, 20000, ct);
                arr = r.Json as List<object>;
            }
            if (!r.Ok || arr == null) { error = r.Error ?? "Hugging Face did not answer."; return null; }
            return arr;
        }

        public static List<HubRepo> Search(string query, CancelToken ct, out string error)
        {
            List<object> arr = Query(query, null, "downloads", 60, true, ct, out error);
            return arr == null ? new List<HubRepo>() : Parse(arr, 40);
        }

        public static HashSet<string> GoodSizes(string query, CancelToken ct)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string err;
            List<object> arr = Query(query, ParamRange(), "downloads", 60, false, ct, out err);
            if (arr != null) foreach (HubRepo h in Parse(arr, 60)) ids.Add(h.Id);
            return ids;
        }

        static int Rank(HubRepo h)
        {
            if (h.Official) return 1;
            for (int i = 0; i < Preferred.Length; i++) if (Preferred[i].Length > 0 && string.Equals(Preferred[i], h.Author, StringComparison.OrdinalIgnoreCase)) return i;
            return Preferred.Length;
        }

        public static List<HubRepo> Suggested(CancelToken ct, out string error)
        {
            List<HubRepo> res = new List<HubRepo>();
            List<object> arr = Query(null, ParamRange(), "trendingScore", 100, true, ct, out error);
            if (arr == null) return res;
            Dictionary<string, int> slot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (HubRepo h in Parse(arr, 100))
            {
                if (!h.Verified || h.Gated) continue;
                if (!Labs.Contains(Org(h.BaseModel)) && !Labs.Contains(h.Author)) continue;
                string key = h.BaseModel.Length > 0 ? h.BaseModel : h.Id;
                int at;
                if (slot.TryGetValue(key, out at))
                {
                    HubRepo cur = res[at];
                    if (Rank(h) < Rank(cur) || (Rank(h) == Rank(cur) && h.Downloads > cur.Downloads)) res[at] = h;
                    continue;
                }
                slot[key] = res.Count;
                res.Add(h);
            }
            if (res.Count > 24) res.RemoveRange(24, res.Count - 24);
            return res;
        }

        public static void ParamRange(out double lo, out double hi)
        {
            long budget, fitsLimit, limit;
            bool ramPool;
            Pool(out budget, out fitsLimit, out limit, out ramPool);
            double gb = Usable(ramPool) / (double)(1L << 30);
            hi = ramPool ? Math.Min(14, gb * 0.75 / 0.6) : gb * 0.75 / 0.6;
            hi = Math.Max(2, Math.Ceiling(hi));
            lo = Math.Max(1, Math.Floor(hi * (ramPool ? 0.25 : 0.35)));
        }

        public static string ParamRange()
        {
            double lo, hi;
            ParamRange(out lo, out hi);
            return "min:" + lo.ToString("0", CultureInfo.InvariantCulture) + "B,max:" + hi.ToString("0", CultureInfo.InvariantCulture) + "B";
        }

        static long Usable(bool ramPool)
        {
            if (ramPool) return (Hardware.RamBytes > 0 ? Hardware.RamBytes : 8L << 30) / 2;
            long sum = 0;
            foreach (GpuInfo g in Hardware.EngineGpus())
                sum += Math.Max(0, g.VramBytes - Math.Max(1L << 30, g.VramBytes * 7 / 100));
            return sum;
        }

        static long Overhead(long weights) { return (512L << 20) + weights / 40; }

        public static double KvBytesPerElem(string kv) { return kv == "f16" ? 2.0 : kv == "q4_0" ? 0.5625 : 1.0625; }

        static readonly Dictionary<string, GgufShape> shapes = new Dictionary<string, GgufShape>(StringComparer.OrdinalIgnoreCase);

        public static GgufShape Shape(string repo, List<HubVariant> vs, CancelToken ct)
        {
            if (vs == null || vs.Count == 0) return null;
            lock (shapes) { GgufShape have; if (shapes.TryGetValue(repo, out have)) return have; }
            HubVariant probe = null;
            foreach (HubVariant v in vs) if (v.Files.Count > 0 && (probe == null || v.TotalBytes < probe.TotalBytes)) probe = v;
            if (probe == null) return null;
            GgufShape g = Gguf.ReadShape(Head(repo, probe.Files[0].Path, 1 << 20, ct));
            if (!g.Known) return null;
            lock (shapes) shapes[repo] = g;
            return g;
        }

        static byte[] Head(string repo, string path, int max, CancelToken ct)
        {
            HttpWebRequest req = null;
            Action abort = null;
            try
            {
                req = Http.Create(Api + "/" + repo + "/resolve/main/" + Downloader.EscapePath(path), "GET", 20000, null);
                req.AllowAutoRedirect = true;
                req.MaximumAutomaticRedirections = 8;
                req.AddRange(0, max - 1);
                HttpWebRequest captured = req;
                abort = delegate { try { captured.Abort(); } catch { } };
                if (ct != null) ct.Register(abort);
                if (ct != null && ct.IsCancelled) return null;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    Net.Redirected(req.RequestUri.ToString(), resp.ResponseUri);
                    byte[] buf = new byte[max];
                    int got = 0;
                    using (Stream st = resp.GetResponseStream())
                    {
                        int n;
                        while (got < max && (n = st.Read(buf, got, max - got)) > 0) got += n;
                        captured.Abort();
                    }
                    if (got < buf.Length) Array.Resize(ref buf, got);
                    return buf;
                }
            }
            catch (Exception) { return null; }
            finally { if (ct != null && abort != null) ct.Unregister(abort); }
        }

        public static List<HubVariant> Variants(string repo, CancelToken ct, out string error)
        {
            error = null;
            List<HubVariant> res = new List<HubVariant>();
            if (!Regex.IsMatch(repo ?? "", @"^[A-Za-z0-9][A-Za-z0-9._\-]*/[A-Za-z0-9][A-Za-z0-9._\-]*$")) { error = "Not a valid repository name."; return res; }
            HttpResult r = Http.Get(Api + "/api/models/" + repo + "/tree/main?recursive=true", null, 20000, ct);
            List<object> arr = r.Json as List<object>;
            if (!r.Ok || arr == null) { error = r.Error ?? "Hugging Face did not answer."; return res; }
            return Group(arr);
        }

        public static List<HubVariant> Group(List<object> arr)
        {
            List<HubVariant> res = new List<HubVariant>();
            List<HubFile> ggufs = new List<HubFile>(), projs = new List<HubFile>();
            foreach (object o in arr)
            {
                if (Json.Str(o, "type") != "file") continue;
                string p = Json.Str(o, "path") ?? "";
                if (!p.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) continue;
                HubFile f = new HubFile();
                f.Path = p;
                f.Size = Json.Long(o, "lfs", "size");
                if (f.Size <= 0) f.Size = Json.Long(o, "size");
                f.Sha256 = (Json.Str(o, "lfs", "oid") ?? Json.Str(o, "lfs", "sha256") ?? "").ToLowerInvariant();
                if (!Regex.IsMatch(f.Sha256, "^[0-9a-f]{64}$")) f.Sha256 = "";
                string fn = System.IO.Path.GetFileName(p);
                if (ModelTraits.IsProjectorName(fn)) projs.Add(f);
                else if (!Gguf.Helper(p, f.Size)) ggufs.Add(f);
            }
            HubFile proj = null;
            foreach (HubFile f in projs) if (proj == null || f.Path.IndexOf("f16", StringComparison.OrdinalIgnoreCase) >= 0 || f.Path.IndexOf("F16", StringComparison.Ordinal) >= 0) proj = f;
            Dictionary<string, HubVariant> byKey = new Dictionary<string, HubVariant>(StringComparer.OrdinalIgnoreCase);
            Regex part = new Regex(@"-(\d{5})-of-(\d{5})\.gguf$", RegexOptions.IgnoreCase);
            foreach (HubFile f in ggufs)
            {
                string key = part.Replace(f.Path, ".gguf");
                HubVariant v;
                if (!byKey.TryGetValue(key, out v))
                {
                    v = new HubVariant();
                    v.Quant = Gguf.QuantName(null, System.IO.Path.GetFileName(key));
                    v.Label = System.IO.Path.GetFileName(key);
                    v.Projector = proj;
                    byKey[key] = v;
                    res.Add(v);
                }
                v.Files.Add(f);
                v.TotalBytes += f.Size;
            }
            foreach (HubVariant v in res) v.Files.Sort(delegate (HubFile a, HubFile b) { return string.CompareOrdinal(a.Path, b.Path); });
            res.Sort(delegate (HubVariant a, HubVariant b) { return a.TotalBytes.CompareTo(b.TotalBytes); });
            return res;
        }

        public static int QuantClass(string quant)
        {
            string u = (quant ?? "").ToUpperInvariant();
            if (u.StartsWith("UD-")) u = u.Substring(3);
            if (u.StartsWith("F32") || u.StartsWith("BF16") || u.StartsWith("F16")) return 16;
            if (u.StartsWith("Q8")) return 8;
            if (u.StartsWith("Q6")) return 6;
            if (u.StartsWith("Q5")) return 5;
            if (u.StartsWith("Q4") || u.StartsWith("IQ4") || u.StartsWith("MXFP4") || u.StartsWith("NVFP4")) return 4;
            if (u.StartsWith("Q3") || u.StartsWith("IQ3")) return 3;
            if (u.StartsWith("Q2") || u.StartsWith("IQ2") || u.StartsWith("TQ2")) return 2;
            if (u.StartsWith("IQ1") || u.StartsWith("TQ1")) return 1;
            return 0;
        }

        static HubVariant Largest(List<HubVariant> vs, int minClass, int maxClass, long max)
        {
            HubVariant best = null;
            foreach (HubVariant v in vs)
            {
                int c = QuantClass(v.Quant);
                if (c < minClass || c > maxClass || v.TotalBytes <= 0 || v.TotalBytes > max) continue;
                if (best == null || v.TotalBytes > best.TotalBytes) best = v;
            }
            return best;
        }

        static void Pool(out long budget, out long fitsLimit, out long limit, out bool ramPool)
        {
            List<GpuInfo> used = Hardware.EngineGpus();
            long vram = Hardware.TotalVram(used);
            long ram = Hardware.RamBytes > 0 ? Hardware.RamBytes : 8L << 30;
            ramPool = used.Count == 0 || vram < (2L << 30);
            if (ramPool)
            {
                budget = Math.Max(1L << 30, ram * 40 / 100);
                fitsLimit = ram * 50 / 100;
                limit = ram * 65 / 100;
            }
            else
            {
                budget = vram * 72 / 100;
                fitsLimit = vram * 82 / 100;
                limit = vram + ram / 2;
            }
        }

        public static HubVariant Recommend(List<HubVariant> vs, GgufShape shape, double bytesPerElem, int wantCtx, out int room)
        {
            room = 0;
            if (vs == null || vs.Count == 0) return null;
            if (shape == null || !shape.Known) return Recommend(vs);
            long budget, fitsLimit, limit;
            bool ramPool;
            Pool(out budget, out fitsLimit, out limit, out ramPool);
            long usable = Usable(ramPool);
            int target = wantCtx > 0 ? wantCtx : 65536;
            if (shape.Context > 0) target = Math.Min(target, shape.Context);
            List<int> ctxs = new List<int>();
            ctxs.Add(target);
            if (target > 32768) ctxs.Add(32768);
            if (target > Engine.MinAutoContext) ctxs.Add(Engine.MinAutoContext);
            int[] tiers = ramPool ? new int[] { 4, 5, 6, 8 } : new int[] { 8, 6, 5, 4 };
            foreach (int ctx in ctxs)
                foreach (int t in tiers)
                {
                    HubVariant best = null;
                    foreach (HubVariant v in vs)
                    {
                        if (QuantClass(v.Quant) != t || v.TotalBytes <= 0) continue;
                        if (v.TotalBytes + Overhead(v.TotalBytes) + shape.KvBytes(ctx, bytesPerElem) > usable) continue;
                        if (best == null || v.TotalBytes > best.TotalBytes) best = v;
                    }
                    if (best != null)
                    {
                        room = Room(best.TotalBytes, shape, usable, bytesPerElem);
                        return best;
                    }
                }
            return Recommend(vs);
        }

        public static long NeedBytes(long weights, GgufShape shape, int ctx, double bytesPerElem)
        {
            return weights + Overhead(weights) + (shape != null && shape.Known ? shape.KvBytes(ctx, bytesPerElem) : 0);
        }

        public static int RoomFor(long weights, GgufShape shape, double bytesPerElem)
        {
            long budget, fitsLimit, limit;
            bool ramPool;
            Pool(out budget, out fitsLimit, out limit, out ramPool);
            return Room(weights, shape, Usable(ramPool), bytesPerElem);
        }

        static readonly Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Regex CardNoise = new Regex(@"quantiz|\bgguf\b|llama\.cpp|imatrix|original model|model creator|quantized by|made by|provided files|prompt format|download a file|lm studio|special thanks|community model|discord|twitter|huggingface-cli|ollama run|this repo(sitory)? contains|static quants|weighted/imatrix|usage|license", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Describe(string repoId, string baseModel, CancelToken ct)
        {
            string key = repoId + "|" + baseModel;
            lock (descriptions) { string have; if (descriptions.TryGetValue(key, out have)) return have; }
            string text = null;
            foreach (string id in new string[] { baseModel, repoId })
            {
                if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9._\-]*/[A-Za-z0-9][A-Za-z0-9._\-]*$")) continue;
                if (ct != null && ct.IsCancelled) return null;
                HttpResult r = Http.Get(Api + "/" + id + "/raw/main/README.md", null, 15000, ct);
                if (!r.Ok || string.IsNullOrEmpty(r.Text)) continue;
                text = Summary(r.Text);
                if (text != null) break;
            }
            lock (descriptions) descriptions[key] = text;
            return text;
        }

        static readonly TimeSpan CardTime = TimeSpan.FromSeconds(2);

        public static string Summary(string card)
        {
            try { return CardSummary(card, DateTime.UtcNow + CardTime); }
            catch (RegexMatchTimeoutException) { return null; }
        }

        static string CardSummary(string card, DateTime until)
        {
            string t = (card ?? "").Replace("\r\n", "\n");
            if (t.StartsWith("---"))
            {
                int end = t.IndexOf("\n---", 3, StringComparison.Ordinal);
                if (end > 0) t = t.Substring(end + 4);
            }
            t = Web.TimedReplace(t, @"<!--.*?-->", " ", RegexOptions.Singleline, until);
            t = Web.TimedReplace(t, @"```.*?```", "\n\n", RegexOptions.Singleline, until);
            t = Web.TimedReplace(t, @"<(script|style)\b.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase, until);
            t = Web.TimedReplace(t, @"!\[[^\]]*\]\([^)]*\)", " ", RegexOptions.None, until);
            t = Web.TimedReplace(t, @"\[([^\]]*)\]\([^)]*\)", "$1", RegexOptions.None, until);
            t = Web.TimedReplace(t, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase, until);
            t = Web.TimedReplace(t, @"</?(p|div|h\d|ul|ol|li|table|tr)\b[^>]*>", "\n\n", RegexOptions.IgnoreCase, until);
            t = Web.TimedReplace(t, @"<[^>]+>", " ", RegexOptions.None, until);
            t = System.Net.WebUtility.HtmlDecode(t);
            foreach (string block in Regex.Split(t, @"\n\s*\n", RegexOptions.None, Web.Scan(until)))
            {
                if (DateTime.UtcNow > until) return null;
                string b = block.Trim();
                if (b.Length < 60 || b.StartsWith("#") || b.StartsWith("|") || b.StartsWith(">") || b.StartsWith("-") || b.StartsWith("*") || Regex.IsMatch(b, @"^\d+\.", RegexOptions.None, Web.Scan(until))) continue;
                b = Web.TimedReplace(b, @"[*_`]+", "", RegexOptions.None, until);
                b = Web.TimedReplace(b, @"\s+", " ", RegexOptions.None, until).Trim();
                if (b.Split(' ').Length < 9 || CardNoise.IsMatch(b.Length > 160 ? b.Substring(0, 160) : b)) continue;
                MatchCollection sentences = Regex.Matches(b, @"[^.!?]+(?:[.!?](?=\s|$)|$)", RegexOptions.None, Web.Scan(until));
                StringBuilder sb = new StringBuilder();
                foreach (Match m in sentences)
                {
                    if (DateTime.UtcNow > until) return null;
                    string sentence = m.Value.Trim();
                    if (sentence.Length == 0) continue;
                    if (sb.Length > 0 && sb.Length + sentence.Length > 320) break;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(sentence);
                    if (sb.Length > 160) break;
                }
                string res = sb.ToString().Trim();
                if (res.Length > 340) res = res.Substring(0, 337).TrimEnd() + "...";
                if (res.Length >= 40) return res;
            }
            return null;
        }

        static int Room(long weights, GgufShape shape, long usable, double bytesPerElem)
        {
            long free = usable - weights - Overhead(weights);
            if (free <= 0 || shape == null || !shape.Known) return 0;
            int hi = shape.Context > 0 ? shape.Context : 1 << 20, lo = 0;
            if (shape.KvBytes(hi, bytesPerElem) <= free) return hi;
            while (hi - lo > 256)
            {
                int mid = lo + (hi - lo) / 2;
                if (shape.KvBytes(mid, bytesPerElem) <= free) lo = mid; else hi = mid;
            }
            return lo;
        }

        public static string Fit(long bytes, GgufShape shape, double bytesPerElem)
        {
            if (shape == null || !shape.Known) return Fit(bytes);
            long budget, fitsLimit, limit;
            bool ramPool;
            Pool(out budget, out fitsLimit, out limit, out ramPool);
            long need = bytes + Overhead(bytes) + shape.KvBytes(Engine.MinAutoContext, bytesPerElem);
            if (need <= Usable(ramPool)) return ramPool ? "fitsram" : "fits";
            if (need < limit) return ramPool ? "tight" : "partial";
            return "toobig";
        }

        public static HubVariant Recommend(List<HubVariant> vs)
        {
            if (vs == null || vs.Count == 0) return null;
            long budget, fitsLimit, limit;
            bool ramPool;
            Pool(out budget, out fitsLimit, out limit, out ramPool);
            int[] tiers = { 8, 6, 5, 4 };
            int[] share = { 50, 60, 75, 100 };
            for (int i = 0; i < tiers.Length; i++)
            {
                HubVariant v = Largest(vs, tiers[i], tiers[i], budget * share[i] / 100);
                if (v != null) return v;
            }
            HubVariant low = Largest(vs, 1, 3, budget);
            if (low != null) return low;
            HubVariant partial = Largest(vs, 1, 5, limit * 85 / 100);
            if (partial != null) return partial;
            HubVariant least = null, anyLeast = null;
            foreach (HubVariant v in vs)
            {
                if (anyLeast == null || v.TotalBytes < anyLeast.TotalBytes) anyLeast = v;
                int c = QuantClass(v.Quant);
                if (c >= 1 && c <= 8 && (least == null || v.TotalBytes < least.TotalBytes)) least = v;
            }
            return least ?? anyLeast;
        }

        public static string Fit(long bytes)
        {
            long budget, fitsLimit, limit;
            bool ramPool;
            Pool(out budget, out fitsLimit, out limit, out ramPool);
            if (bytes < fitsLimit) return ramPool ? "fitsram" : "fits";
            if (bytes < limit) return ramPool ? "tight" : "partial";
            return "toobig";
        }
    }

    class Downloader
    {
        public volatile bool Cancel;
        public long Done, Total;
        public string Status = "";

        public string Download(string repo, HubVariant v, Action<Downloader> progress)
        {
            if (Net.Offline) return "Offline mode is on (Settings > Privacy), so MinGent can't download models.";
            List<HubFile> files = new List<HubFile>(v.Files);
            if (v.Projector != null) files.Add(v.Projector);
            Total = 0;
            foreach (HubFile f in files) Total += f.Size;
            string[] rp = repo.Split('/');
            string dir = System.IO.Path.Combine(System.IO.Path.Combine(ModelLibrary.OwnRoot, Safe(rp[0])), Safe(rp.Length > 1 ? rp[1] : "model"));
            Directory.CreateDirectory(dir);
            long before = 0;
            foreach (HubFile f in files)
            {
                if (Cancel) return "Cancelled.";
                string name = Safe(System.IO.Path.GetFileName(f.Path));
                string target = System.IO.Path.Combine(dir, name);
                if (File.Exists(target) && new FileInfo(target).Length == f.Size && f.Size > 0)
                {
                    Status = "Checking " + name;
                    if (progress != null) progress(this);
                    if (f.Sha256.Length == 0 || Sha256File(target, this) == f.Sha256) { before += f.Size; Done = before; continue; }
                }
                string err = DownloadFile(repo, f, target, before, progress);
                if (err != null) return err;
                before += f.Size;
                Done = before;
            }
            Status = "Done";
            if (progress != null) progress(this);
            return null;
        }

        static string Safe(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            string r = sb.ToString().Trim('.');
            return r.Length == 0 ? "model" : r;
        }

        string DownloadFile(string repo, HubFile f, string target, long before, Action<Downloader> progress)
        {
            string part = target + ".part";
            string name = System.IO.Path.GetFileName(target);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (Cancel) return "Cancelled.";
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (f.Size > 0 && have > f.Size) { File.Delete(part); have = 0; }
                try
                {
                    if (!(f.Size > 0 && have == f.Size))
                    {
                        string url = Hub.Api + "/" + repo + "/resolve/main/" + EscapePath(f.Path);
                        HttpWebRequest req = Http.Create(url, "GET", 60000, null);
                        req.AllowAutoRedirect = true;
                        req.MaximumAutomaticRedirections = 8;
                        if (have > 0) req.AddRange(have);
                        bool oversize = false;
                        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                        {
                            Net.Redirected(url, resp.ResponseUri);
                            bool partial = resp.StatusCode == HttpStatusCode.PartialContent;
                            if (have > 0 && !partial) have = 0;
                            long served = partial ? RangeTotal(resp.Headers["Content-Range"]) : resp.ContentLength;
                            if (f.Size > 0 && served > 0 && served != f.Size)
                            {
                                resp.Close();
                                try { File.Delete(part); } catch { }
                                return Changed(name);
                            }
                            long cap = f.Size > 0 ? f.Size : 1L << 40;
                            using (Stream s = resp.GetResponseStream())
                            using (FileStream fs = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20))
                            {
                                byte[] buf = new byte[1 << 20];
                                int n;
                                DateTime last = DateTime.MinValue;
                                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                                {
                                    if (Cancel) return "Cancelled.";
                                    if (have + n > cap) { oversize = true; break; }
                                    fs.Write(buf, 0, n);
                                    have += n;
                                    Done = before + have;
                                    if ((DateTime.UtcNow - last).TotalMilliseconds > 250)
                                    {
                                        last = DateTime.UtcNow;
                                        Status = "Downloading " + name;
                                        if (progress != null) progress(this);
                                    }
                                }
                            }
                        }
                        if (oversize)
                        {
                            try { File.Delete(part); } catch { }
                            return Changed(name);
                        }
                    }
                    if (f.Size > 0 && have != f.Size) continue;
                    Status = "Verifying " + name;
                    if (progress != null) progress(this);
                    if (f.Sha256.Length > 0)
                    {
                        string got = Sha256File(part, this);
                        if (got == null) return "Cancelled.";
                        if (got != f.Sha256)
                        {
                            File.Delete(part);
                            return "The download of " + name + " did not match its published SHA-256 checksum, so it was deleted.";
                        }
                    }
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(part, target);
                    return null;
                }
                catch (WebException wex)
                {
                    Net.Redirected(Hub.Api + "/" + repo + "/resolve/main/" + EscapePath(f.Path), wex);
                    HttpWebResponse hr = wex.Response as HttpWebResponse;
                    if (hr != null && ((int)hr.StatusCode == 401 || (int)hr.StatusCode == 403))
                        return "Hugging Face refused the download (the model may be gated and need a login on huggingface.co).";
                    if (hr != null && (int)hr.StatusCode == 416)
                    {
                        long total = RangeTotal(hr.Headers["Content-Range"]);
                        try { File.Delete(part); } catch { }
                        if (f.Size > 0 && total > 0 && total != f.Size) return Changed(name);
                        continue;
                    }
                    if (attempt == 5) return "Download failed: " + wex.Message;
                    System.Threading.Thread.Sleep(1500 * (attempt + 1));
                }
                catch (IOException ex)
                {
                    int code = ex.HResult & 0xFFFF;
                    if (code == 112 || code == 39)
                    {
                        try { File.Delete(part); } catch { }
                        return "The disk ran out of space while downloading " + name + ". The unfinished file was removed to free the space again.";
                    }
                    if (attempt == 5) return "Download failed: " + ex.Message;
                    System.Threading.Thread.Sleep(1500 * (attempt + 1));
                }
            }
            return "Download failed after several attempts.";
        }

        static string Changed(string name)
        {
            return "The size of " + name + " on Hugging Face doesn't match the list MinGent loaded, so the download was stopped. The file may have been updated: open the model again to load a fresh list.";
        }

        static long RangeTotal(string contentRange)
        {
            if (string.IsNullOrEmpty(contentRange)) return -1;
            int slash = contentRange.LastIndexOf('/');
            long v;
            if (slash < 0 || !long.TryParse(contentRange.Substring(slash + 1).Trim(), out v)) return -1;
            return v;
        }

        public static string EscapePath(string p)
        {
            string[] segs = p.Split('/');
            for (int i = 0; i < segs.Length; i++) segs[i] = Uri.EscapeDataString(segs[i]);
            return string.Join("/", segs);
        }

        public static string Sha256File(string path, Downloader d)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                byte[] buf = new byte[1 << 20];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    if (d != null && d.Cancel) return null;
                    sha.TransformBlock(buf, 0, n, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in sha.Hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    class DownloadJob
    {
        public string RepoId = "", RepoName = "";
        public HubVariant Variant;
        public readonly Downloader D = new Downloader();
        public volatile bool Finished;
        public string Error, LocalPath;
        public int Progress;
        public string Text = "Starting...";
        public bool Verified;

        public string Title { get { return RepoName + (Variant != null && Variant.Quant.Length > 0 ? " " + Variant.Quant : ""); } }

        public bool Is(string repoId, HubVariant v)
        {
            return v != null && Variant != null && string.Equals(RepoId, repoId, StringComparison.OrdinalIgnoreCase) && Variant.Label == v.Label;
        }
    }

    static class ModelDownloads
    {
        static readonly object gate = new object();
        static readonly List<DownloadJob> jobs = new List<DownloadJob>();
        public static event Action<DownloadJob> Changed;

        public static List<DownloadJob> All() { lock (gate) return new List<DownloadJob>(jobs); }

        public static List<DownloadJob> Active() { lock (gate) return jobs.FindAll(delegate (DownloadJob j) { return !j.Finished; }); }

        public static DownloadJob Find(string repoId, HubVariant v)
        {
            lock (gate)
                for (int i = jobs.Count - 1; i >= 0; i--)
                    if (jobs[i].Is(repoId, v)) return jobs[i];
            return null;
        }

        public static long PendingBytes()
        {
            long n = 0;
            foreach (DownloadJob j in Active())
            {
                long total = j.D.Total > 0 ? j.D.Total : (j.Variant != null ? j.Variant.TotalBytes : 0);
                n += Math.Max(0, total - j.D.Done);
            }
            return n;
        }

        public static void CancelAll() { foreach (DownloadJob j in Active()) j.D.Cancel = true; }

        static void Raise(DownloadJob j)
        {
            Action<DownloadJob> h = Changed;
            if (h == null) return;
            try { h(j); } catch { }
        }

        public static DownloadJob Start(HubRepo repo, HubVariant v)
        {
            DownloadJob job;
            lock (gate)
            {
                foreach (DownloadJob j in jobs) if (!j.Finished && j.Is(repo.Id, v)) return j;
                jobs.RemoveAll(delegate (DownloadJob j) { return j.Finished && j.Is(repo.Id, v); });
                job = new DownloadJob();
                job.RepoId = repo.Id;
                job.RepoName = repo.Name;
                job.Variant = v;
                jobs.Add(job);
                int finished = 0;
                foreach (DownloadJob j in jobs) if (j.Finished) finished++;
                while (finished > 20)
                {
                    int k = jobs.FindIndex(delegate (DownloadJob j) { return j.Finished; });
                    if (k < 0) break;
                    jobs.RemoveAt(k);
                    finished--;
                }
            }
            Raise(job);
            Thread t = new Thread(delegate () { Run(job); });
            t.IsBackground = true;
            t.Start();
            return job;
        }

        static string Eta(double seconds)
        {
            if (seconds < 60) return (int)Math.Ceiling(seconds) + " s";
            if (seconds < 3600) return (int)Math.Ceiling(seconds / 60) + " min";
            int h = (int)(seconds / 3600), m = (int)((seconds - h * 3600) / 60);
            return h + " h " + m + " min";
        }

        static void Run(DownloadJob job)
        {
            long lastBytes = -1;
            DateTime lastTime = DateTime.UtcNow, lastRaise = DateTime.MinValue;
            double rate = 0;
            string err;
            try
            {
                err = job.D.Download(job.RepoId, job.Variant, delegate (Downloader x)
                {
                    long done = x.Done, total = Math.Max(1, x.Total);
                    DateTime now = DateTime.UtcNow;
                    double dt = (now - lastTime).TotalSeconds;
                    if (lastBytes < 0 || done < lastBytes) { lastBytes = done; lastTime = now; }
                    else if (dt >= 1.0)
                    {
                        double r = (done - lastBytes) / dt;
                        rate = rate <= 0 ? r : rate * 0.7 + r * 0.3;
                        lastBytes = done;
                        lastTime = now;
                    }
                    string st = x.Status ?? "";
                    string text;
                    if (st.StartsWith("Verifying", StringComparison.Ordinal)) text = "Checking the SHA-256 checksum...";
                    else if (st.StartsWith("Checking", StringComparison.Ordinal)) text = "Checking the files already on disk...";
                    else
                    {
                        text = ModelCatalog.Size(done) + " of " + ModelCatalog.Size(total);
                        if (rate > 1024)
                        {
                            text += "  \u00B7  " + ModelCatalog.Size((long)rate) + "/s";
                            double left = (total - done) / rate;
                            if (left >= 1) text += "  \u00B7  " + Eta(left) + " left";
                        }
                    }
                    job.Progress = (int)Math.Min(1000, done * 1000 / total);
                    job.Text = text;
                    if ((now - lastRaise).TotalMilliseconds >= 250 || st != "Downloading") { lastRaise = now; Raise(job); }
                });
            }
            catch (Exception ex) { err = "The download failed: " + ex.Message; }
            if (err == null)
            {
                List<LocalModel> have;
                try { have = ModelLibrary.Scan(true); } catch { have = new List<LocalModel>(); }
                job.LocalPath = ModelLibrary.LocalPathFor(job.RepoId, job.Variant, have);
                bool all = true;
                foreach (HubFile f in job.Variant.Files) if (f.Sha256.Length == 0) all = false;
                job.Verified = all;
                job.Text = all ? "Downloaded and verified" : "Downloaded (no checksum to verify)";
            }
            else
            {
                job.Error = err;
                job.Text = err;
            }
            job.Progress = 1000;
            job.Finished = true;
            Raise(job);
        }
    }
}
