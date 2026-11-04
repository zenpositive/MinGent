using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MinGent
{
    struct Pos
    {
        public int Line, Col;
        public Pos(int line, int col) { Line = line; Col = col; }
        public static bool operator <(Pos a, Pos b) { return a.Line < b.Line || (a.Line == b.Line && a.Col < b.Col); }
        public static bool operator >(Pos a, Pos b) { return b < a; }
        public static bool operator <=(Pos a, Pos b) { return !(b < a); }
        public static bool operator >=(Pos a, Pos b) { return !(a < b); }
        public static bool operator ==(Pos a, Pos b) { return a.Line == b.Line && a.Col == b.Col; }
        public static bool operator !=(Pos a, Pos b) { return !(a == b); }
        public override bool Equals(object o) { return o is Pos && (Pos)o == this; }
        public override int GetHashCode() { return (Line * 397) ^ Col; }
        public static Pos Min(Pos a, Pos b) { return a < b ? a : b; }
        public static Pos Max(Pos a, Pos b) { return a < b ? b : a; }
    }

    static class Cells
    {
        public static int TabSize = 4;
        public static int MaxW { get { return Math.Max(2, TabSize); } }

        static byte[] widths;

        public static int W(char c)
        {
            if (c < 0x0300) return 1;
            byte[] t = widths;
            if (t == null)
            {
                t = new byte[65536];
                for (int i = 0; i < 65536; i++) t[i] = (byte)Calc((char)i);
                widths = t;
            }
            return t[c];
        }

        static int Calc(char c)
        {
            if (c < 0x0300) return 1;
            if (c < 0xD800 || c > 0xDFFF)
            {
                System.Globalization.UnicodeCategory cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == System.Globalization.UnicodeCategory.NonSpacingMark || cat == System.Globalization.UnicodeCategory.EnclosingMark ||
                    (cat == System.Globalization.UnicodeCategory.Format && c != 0x00AD)) return 0;
            }
            if (c <= 0x036F) return 0;
            if (c < 0x1100) return 1;
            if (c <= 0x115F) return 2;
            if (c >= 0x200B && c <= 0x200F) return 0;
            if (c >= 0x2E80 && c <= 0xA4CF && c != 0x303F) return 2;
            if (c >= 0xAC00 && c <= 0xD7A3) return 2;
            if (c == 0xDB40) return 0;
            if (c >= 0xD800 && c <= 0xDBFF) return 2;
            if (c >= 0xDC00 && c <= 0xDFFF) return 0;
            if (c >= 0xF900 && c <= 0xFAFF) return 2;
            if (c >= 0xFE00 && c <= 0xFE0F) return 0;
            if (c >= 0xFE30 && c <= 0xFE4F) return 2;
            if (c >= 0xFF00 && c <= 0xFF60) return 2;
            if (c >= 0xFFE0 && c <= 0xFFE6) return 2;
            return 1;
        }

        public static int Adv(char c, int cell) { return c == '\t' ? TabSize - (cell % TabSize) : W(c); }

        static string uniStr;
        static bool uniVal;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string, object> uniCache = new System.Runtime.CompilerServices.ConditionalWeakTable<string, object>();
        static readonly object yes = new object(), no = new object();

        public static bool Uniform(string s)
        {
            if (ReferenceEquals(s, uniStr)) return uniVal;
            object cached;
            if (s.Length > 512 && uniCache.TryGetValue(s, out cached)) { uniStr = s; uniVal = cached == yes; return uniVal; }
            bool u = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\t' || (c >= 0x0300 && W(c) != 1)) { u = false; break; }
            }
            if (s.Length > 512) { try { uniCache.Add(s, u ? yes : no); } catch (ArgumentException) { } }
            uniStr = s;
            uniVal = u;
            return u;
        }

        public static int Of(string s, int upto)
        {
            if (upto > 4096 && Uniform(s)) return upto;
            int cell = 0;
            for (int i = 0; i < upto; i++)
            {
                char c = s[i];
                if (c == '\t') cell += TabSize - (cell % TabSize);
                else cell += c < 0x0300 ? 1 : W(c);
            }
            return cell;
        }

        public static bool Batchable(char c)
        {
            return c < 0x0590 || (c >= 0x2000 && c < 0x2070) || (c >= 0x2500 && c < 0x2580);
        }
    }

    class EditRec
    {
        public Pos Start;
        public string Removed, Inserted;
        public byte[] MarksBefore;
    }

    class UndoGroup
    {
        public List<EditRec> Edits = new List<EditRec>();
        public Pos CaretBefore, AnchorBefore, CaretAfter, AnchorAfter;
        public int Kind;
        public DateTime Time;
    }

    class Document
    {
        public const int KindNone = 0, KindWord = 1, KindSpace = 2, KindBack = 3, KindDel = 4;
        public const long MaxFileBytes = 256L * 1024 * 1024;
        const int MaxUndo = 4000;
        public static readonly string[] EncNames = { "UTF-8", "UTF-8 with BOM", "UTF-16 LE", "UTF-16 BE", "ANSI" };

        public List<string> Lines = new List<string>();
        public List<byte> Marks = new List<byte>();
        public List<int> States = new List<int>();
        public int StatesValid;
        public List<int[]> Wraps = new List<int[]>();
        public int WrapCols = -1;
        public int[] RowStart;
        public bool RowsDirty = true;
        public int MaxCells = -1;

        public string Path, Title;
        public int EncKind;
        public bool Lossy;
        public int MissingUndoPos = -1;
        public string Eol = "\r\n";
        public Lang Lang = Syntax.Plain;
        public bool LangManual;
        public string IndentUnit = "\t";

        public List<UndoGroup> Undo = new List<UndoGroup>();
        public int UndoPos, SavePoint;

        public Pos Caret, Anchor;
        public int DesiredX = -1, TopLine, ScrollX;

        public DateTime DiskTime;
        public long DiskSize;
        public bool DiskMissing;
        public string BackupFile;
        public bool BackupDirty;
        public DateTime LastBackup;
        public long Version;

        public Document()
        {
            Lines.Add(""); Marks.Add(0); States.Add(0); Wraps.Add(null);
        }

        public bool Modified { get { return UndoPos != SavePoint; } }
        public bool IsEmpty { get { return Lines.Count == 1 && Lines[0].Length == 0; } }
        public bool HighlightOn { get { return Lang != Syntax.Plain; } }
        public bool CanUndo { get { return UndoPos > 0; } }
        public bool CanRedo { get { return UndoPos < Undo.Count; } }

        public string DisplayName
        {
            get
            {
                if (Path != null) { try { return System.IO.Path.GetFileName(Path); } catch { return Path; } }
                return Title ?? "Untitled";
            }
        }

        public long TextLength
        {
            get { long n = Lines.Count - 1; foreach (string s in Lines) n += s.Length; return n; }
        }

        public static Document Load(string path)
        {
            byte[] data = ReadAll(path);
            int enc;
            bool lossy;
            string text = Decode(data, out enc, out lossy);
            Document d = new Document();
            d.Path = path;
            d.EncKind = enc;
            d.Lossy = lossy;
            d.SetText(text, true);
            d.Lang = Syntax.Detect(path, d.Lines[0]);
            d.RefreshDiskInfo();
            return d;
        }

        public void Reload()
        {
            byte[] data = ReadAll(Path);
            int enc;
            bool lossy;
            string text = Decode(data, out enc, out lossy);
            EncKind = enc;
            Lossy = lossy;
            SetText(text, true);
            if (!LangManual) Lang = Syntax.Detect(Path, Lines[0]);
            RefreshDiskInfo();
            DiskMissing = false;
        }

        public void ReloadKeepUndo()
        {
            byte[] data = ReadAll(Path);
            int enc;
            bool lossy;
            string text = Decode(data, out enc, out lossy);
            string eol;
            SplitLines(text, out eol);
            Pos a, b;
            ReplaceAllMinimal(text, out a, out b);
            EncKind = enc;
            Lossy = lossy;
            if (eol != null) Eol = eol;
            MarkSaved();
            RefreshDiskInfo();
            DiskMissing = false;
        }

        public void RefreshDiskInfo()
        {
            try
            {
                FileInfo fi = new FileInfo(Path);
                DiskTime = fi.LastWriteTimeUtc;
                DiskSize = fi.Length;
            }
            catch { }
        }

        static byte[] ReadAll(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long len = fs.Length;
                if (len > MaxFileBytes) throw new IOException("The file is too large to open (" + (len >> 20) + " MB).");
                byte[] data = new byte[len];
                int read = 0;
                while (read < len)
                {
                    int r = fs.Read(data, read, (int)(len - read));
                    if (r <= 0) break;
                    read += r;
                }
                if (read < len) Array.Resize(ref data, read);
                return data;
            }
        }

        public static string Decode(byte[] b, out int enc)
        {
            bool lossy;
            return Decode(b, out enc, out lossy);
        }

        static string Strict(Encoding e, byte[] b, int start, out bool lossy)
        {
            lossy = false;
            try { return e.GetString(b, start, b.Length - start); }
            catch (DecoderFallbackException) { lossy = true; }
            Encoding loose = (Encoding)e.Clone();
            loose.DecoderFallback = new DecoderReplacementFallback("\uFFFD");
            return loose.GetString(b, start, b.Length - start);
        }

        public static string Decode(byte[] b, out int enc, out bool lossy)
        {
            lossy = false;
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { enc = 1; return Strict(new UTF8Encoding(false, true), b, 3, out lossy); }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { enc = 2; return Strict(new UnicodeEncoding(false, false, true), b, 2, out lossy); }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { enc = 3; return Strict(new UnicodeEncoding(true, false, true), b, 2, out lossy); }
            int sample = Math.Min(b.Length, 4096) & ~1;
            if (sample >= 4)
            {
                int zeroEven = 0, zeroOdd = 0;
                for (int i = 0; i < sample; i++)
                    if (b[i] == 0) { if ((i & 1) == 0) zeroEven++; else zeroOdd++; }
                bool l2;
                if (zeroOdd > sample / 4 && zeroEven < sample / 40) { string u = Strict(new UnicodeEncoding(false, false, true), b, 0, out l2); if (!l2) { enc = 2; return u; } }
                if (zeroEven > sample / 4 && zeroOdd < sample / 40) { string u = Strict(new UnicodeEncoding(true, false, true), b, 0, out l2); if (!l2) { enc = 3; return u; } }
            }
            try
            {
                string s = new UTF8Encoding(false, true).GetString(b);
                enc = 0;
                return s;
            }
            catch (DecoderFallbackException) { }
            if (MostlyUtf8(b)) { enc = 0; return Strict(new UTF8Encoding(false, true), b, 0, out lossy); }
            Encoding def = Encoding.Default;
            if (def.IsSingleByte && def.CodePage != 65001) { enc = 4; return def.GetString(b); }
            try
            {
                string s2 = Encoding.GetEncoding(def.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(b);
                enc = 4;
                return s2;
            }
            catch { }
            enc = 5;
            return Encoding.GetEncoding(28591).GetString(b);
        }

        static bool MostlyUtf8(byte[] b)
        {
            int good = 0, bad = 0;
            for (int i = 0; i < b.Length; i++)
            {
                int c = b[i];
                if (c < 0x80) continue;
                int n = c >= 0xC2 && c <= 0xDF ? 1 : c >= 0xE0 && c <= 0xEF ? 2 : c >= 0xF0 && c <= 0xF4 ? 3 : 0;
                bool ok = n > 0 && i + n < b.Length;
                for (int k = 1; ok && k <= n; k++) ok = (b[i + k] & 0xC0) == 0x80;
                if (ok && n == 2 && (c == 0xE0 && b[i + 1] < 0xA0 || c == 0xED && b[i + 1] >= 0xA0)) ok = false;
                if (ok && n == 3 && (c == 0xF0 && b[i + 1] < 0x90 || c == 0xF4 && b[i + 1] >= 0x90)) ok = false;
                if (ok) { good++; i += n; }
                else bad++;
            }
            return good >= 4 && bad * 20 <= good;
        }

        public static byte[] Encode(string text, int enc)
        {
            Encoding e;
            byte[] pre;
            switch (enc)
            {
                case 1: e = new UTF8Encoding(false); pre = new byte[] { 0xEF, 0xBB, 0xBF }; break;
                case 2: e = new UnicodeEncoding(false, false); pre = new byte[] { 0xFF, 0xFE }; break;
                case 3: e = new UnicodeEncoding(true, false); pre = new byte[] { 0xFE, 0xFF }; break;
                case 4: e = Encoding.GetEncoding(Encoding.Default.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); pre = new byte[0]; break;
                case 5: e = Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); pre = new byte[0]; break;
                default: e = new UTF8Encoding(false); pre = new byte[0]; break;
            }
            int count = e.GetByteCount(text);
            byte[] buf = new byte[pre.Length + count];
            Array.Copy(pre, buf, pre.Length);
            e.GetBytes(text, 0, text.Length, buf, pre.Length);
            return buf;
        }

        public static List<string> SplitLines(string text, out string eol)
        {
            List<string> list = new List<string>();
            eol = null;
            int start = 0, n = text.Length, crlf = 0, lf = 0, cr = 0;
            string first = null;
            for (int i = 0; i < n; i++)
            {
                char c = text[i];
                if (c != '\r' && c != '\n') continue;
                list.Add(text.Substring(start, i - start));
                if (c == '\r' && i + 1 < n && text[i + 1] == '\n') { crlf++; if (first == null) first = "\r\n"; i++; }
                else if (c == '\r') { cr++; if (first == null) first = "\r"; }
                else { lf++; if (first == null) first = "\n"; }
                start = i + 1;
            }
            list.Add(text.Substring(start));
            if (first != null)
            {
                int best = Math.Max(crlf, Math.Max(lf, cr));
                int firstCount = first == "\r\n" ? crlf : first == "\n" ? lf : cr;
                eol = firstCount == best ? first : crlf == best ? "\r\n" : lf == best ? "\n" : "\r";
            }
            return list;
        }

        public static string NormalizeNewlines(string s)
        {
            if (s.IndexOf('\r') < 0) return s;
            return s.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        public void SetText(string text, bool detectEol)
        {
            string eol;
            List<string> lines = SplitLines(text, out eol);
            if (detectEol) Eol = eol ?? "\r\n";
            Lines = lines;
            Marks = new List<byte>(new byte[lines.Count]);
            States = new List<int>(new int[lines.Count]);
            Wraps = new List<int[]>(new int[lines.Count][]);
            StatesValid = 0;
            RowsDirty = true;
            WrapCols = -1;
            MaxCells = -1;
            Version++;
            Undo.Clear();
            UndoPos = 0;
            SavePoint = 0;
            Caret = Clamp(Caret);
            Anchor = Clamp(Anchor);
            DetectIndent();
        }

        void DetectIndent()
        {
            int tabs = 0, spaces = 0, odd = 0;
            int n = Math.Min(Lines.Count, 3000);
            for (int i = 0; i < n; i++)
            {
                string s = Lines[i];
                if (s.Length == 0) continue;
                if (s[0] == '\t') tabs++;
                else if (s[0] == ' ')
                {
                    int j = 0;
                    while (j < s.Length && s[j] == ' ') j++;
                    if (j < s.Length && j >= 2)
                    {
                        spaces++;
                        if (j % 4 != 0) odd++;
                    }
                }
            }
            if (spaces > tabs && spaces >= 3) IndentUnit = new string(' ', odd * 4 > spaces ? 2 : 4);
            else IndentUnit = "\t";
        }

        public string AllText(string eol)
        {
            return string.Join(eol, Lines);
        }

        public int FindBlock(string[] search, out bool fuzzy)
        {
            fuzzy = false;
            if (search == null || search.Length == 0 || search.Length > Lines.Count) return -1;
            for (int pass = 0; pass < 2; pass++)
            {
                int found = -1, count = 0;
                string first = Norm(search[0], pass);
                for (int i = 0; i + search.Length <= Lines.Count; i++)
                {
                    if (Norm(Lines[i], pass) != first) continue;
                    bool ok = true;
                    for (int k = 1; k < search.Length; k++)
                        if (Norm(Lines[i + k], pass) != Norm(search[k], pass)) { ok = false; break; }
                    if (!ok) continue;
                    if (found < 0) found = i;
                    count++;
                    if (pass == 0) break;
                }
                if (found >= 0)
                {
                    fuzzy = pass == 1;
                    return pass == 1 && count > 1 ? -2 : found;
                }
            }
            return -1;
        }

        static string Norm(string s, int pass) { return pass == 0 ? s.TrimEnd(' ', '\t') : s.Trim(' ', '\t'); }

        static string LeadingWs(string s)
        {
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            return s.Substring(0, i);
        }

        static int WsWidth(string ws)
        {
            int w = 0;
            foreach (char c in ws) w += c == '\t' ? Cells.TabSize - (w % Cells.TabSize) : 1;
            return w;
        }

        string Reindent(string[] search, int at, string repl)
        {
            int k = 0;
            while (k < search.Length - 1 && search[k].Trim().Length == 0) k++;
            string modelWs = LeadingWs(search[k]), fileWs = LeadingWs(Lines[at + k]);
            int delta = WsWidth(fileWs) - WsWidth(modelWs);
            bool tabs = fileWs.IndexOf('\t') >= 0 || (fileWs.Length == 0 && IndentUnit == "\t");
            string[] rl = repl.Split('\n');
            for (int i = 0; i < rl.Length; i++)
            {
                if (rl[i].Trim().Length == 0) continue;
                string ws = LeadingWs(rl[i]);
                int w = Math.Max(0, WsWidth(ws) + delta);
                string nws = tabs ? new string('\t', w / Cells.TabSize) + new string(' ', w % Cells.TabSize) : new string(' ', w);
                rl[i] = nws + rl[i].Substring(ws.Length);
            }
            return string.Join("\n", rl);
        }

        public static string[] SplitBlock(string text)
        {
            if (text == null) return new string[0];
            string s = NormalizeNewlines(text);
            if (s.StartsWith("\n")) s = s.Substring(1);
            if (s.EndsWith("\n")) s = s.Substring(0, s.Length - 1);
            if (s.Trim().Length == 0) return new string[0];
            return s.Split('\n');
        }

        public string ApplyBlock(string search, string replace, out Pos start, out Pos end)
        {
            start = end = Caret;
            string repl = NormalizeNewlines(replace ?? "");
            if (repl.EndsWith("\n")) repl = repl.Substring(0, repl.Length - 1);
            string[] s = SplitBlock(search);
            UndoGroup g;
            Pos a, b, e;
            if (s.Length == 0)
            {
                if (repl.Trim().Length == 0) return "the change was empty";
                bool empty = IsEmpty;
                a = empty ? new Pos(0, 0) : End;
                string ins = empty || Lines[Lines.Count - 1].Length == 0 ? repl : "\n" + repl;
                g = OpenGroup(KindNone, Caret, Anchor);
                e = Apply(g, a, empty ? End : a, ins);
                Caret = Anchor = e;
                g.CaretAfter = g.AnchorAfter = e;
                start = ins.StartsWith("\n") ? new Pos(a.Line + 1, 0) : a;
                end = e;
                return null;
            }
            bool fuzzy;
            int at = FindBlock(s, out fuzzy);
            if (at == -2) return "the SEARCH text matches several places";
            if (at < 0) return "the SEARCH text was not found";
            if (fuzzy) repl = Reindent(s, at, repl);
            int endLine = at + s.Length - 1;
            a = new Pos(at, 0);
            if (repl.Length == 0 && endLine < Lines.Count - 1) b = new Pos(endLine + 1, 0);
            else if (repl.Length == 0 && at > 0)
            {
                a = new Pos(at - 1, Lines[at - 1].Length);
                b = new Pos(endLine, Lines[endLine].Length);
            }
            else b = new Pos(endLine, Lines[endLine].Length);
            g = OpenGroup(KindNone, Caret, Anchor);
            e = Apply(g, a, b, repl);
            Caret = Anchor = e;
            g.CaretAfter = g.AnchorAfter = e;
            start = a;
            end = e;
            return null;
        }

        public bool ReplaceAllMinimal(string text, out Pos start, out Pos end)
        {
            start = end = Caret;
            string[] nw = NormalizeNewlines(text ?? "").Split('\n');
            int n = Lines.Count, m = nw.Length, p = 0, s = 0;
            while (p < n && p < m && Lines[p] == nw[p]) p++;
            if (p == n && p == m) return false;
            while (s < n - p && s < m - p && Lines[n - 1 - s] == nw[m - 1 - s]) s++;
            int oldEnd = n - s, newEnd = m - s;
            string mid = string.Join("\n", nw, p, newEnd - p);
            Pos a, b;
            string ins;
            if (oldEnd > p && newEnd > p) { a = new Pos(p, 0); b = new Pos(oldEnd - 1, Lines[oldEnd - 1].Length); ins = mid; }
            else if (oldEnd == p)
            {
                if (p < n) { a = b = new Pos(p, 0); ins = mid + "\n"; }
                else { a = b = new Pos(n - 1, Lines[n - 1].Length); ins = "\n" + mid; }
            }
            else
            {
                if (oldEnd < n) { a = new Pos(p, 0); b = new Pos(oldEnd, 0); }
                else if (p > 0) { a = new Pos(p - 1, Lines[p - 1].Length); b = new Pos(n - 1, Lines[n - 1].Length); }
                else { a = new Pos(0, 0); b = End; }
                ins = "";
            }
            UndoGroup g = OpenGroup(KindNone, Caret, Anchor);
            Pos e = Apply(g, a, b, ins);
            Caret = Anchor = e;
            g.CaretAfter = g.AnchorAfter = e;
            start = ins.StartsWith("\n") ? new Pos(a.Line + 1, 0) : a;
            end = e;
            return true;
        }

        public void MarkSaved()
        {
            SavePoint = UndoPos;
            for (int i = 0; i < Marks.Count; i++) if (Marks[i] == 1) Marks[i] = 2;
            BackupDirty = true;
            DiskMissing = false;
        }

        public Pos Clamp(Pos p)
        {
            int l = Math.Max(0, Math.Min(Lines.Count - 1, p.Line));
            string s = Lines[l];
            int c = Math.Max(0, Math.Min(s.Length, p.Col));
            if (c > 0 && c < s.Length && char.IsLowSurrogate(s[c]) && char.IsHighSurrogate(s[c - 1])) c--;
            return new Pos(l, c);
        }

        public Pos End { get { return new Pos(Lines.Count - 1, Lines[Lines.Count - 1].Length); } }

        public string GetText(Pos a, Pos b)
        {
            if (b < a) { Pos t = a; a = b; b = t; }
            if (a.Line == b.Line) return Lines[a.Line].Substring(a.Col, b.Col - a.Col);
            StringBuilder sb = new StringBuilder();
            sb.Append(Lines[a.Line], a.Col, Lines[a.Line].Length - a.Col);
            for (int l = a.Line + 1; l < b.Line; l++) sb.Append('\n').Append(Lines[l]);
            sb.Append('\n').Append(Lines[b.Line], 0, b.Col);
            return sb.ToString();
        }

        public long CountChars(Pos a, Pos b)
        {
            if (b < a) { Pos t = a; a = b; b = t; }
            if (a.Line == b.Line) return b.Col - a.Col;
            long n = Lines[a.Line].Length - a.Col + 1;
            for (int l = a.Line + 1; l < b.Line; l++) n += Lines[l].Length + 1;
            return n + b.Col;
        }

        public static Pos EndOf(Pos start, string text)
        {
            int nl = text.LastIndexOf('\n');
            if (nl < 0) return new Pos(start.Line, start.Col + text.Length);
            int count = 0;
            for (int i = 0; i <= nl; i++) if (text[i] == '\n') count++;
            return new Pos(start.Line + count, text.Length - nl - 1);
        }

        public void EnsureStates(int line)
        {
            if (line >= Lines.Count) line = Lines.Count - 1;
            while (StatesValid < line)
            {
                int i = StatesValid;
                States[i + 1] = Syntax.Tokenize(Lang, Lines[i], States[i], null);
                StatesValid++;
            }
        }

        public void SetLang(Lang l, bool manual)
        {
            Lang = l ?? Syntax.Plain;
            LangManual = manual;
            StatesValid = 0;
            if (States.Count > 0) States[0] = 0;
            Version++;
        }

        static void ReplaceRange<T>(List<T> list, int index, int removeCount, T[] items)
        {
            int common = Math.Min(removeCount, items.Length);
            for (int i = 0; i < common; i++) list[index + i] = items[i];
            if (items.Length > removeCount)
            {
                T[] rest = new T[items.Length - common];
                Array.Copy(items, common, rest, 0, rest.Length);
                list.InsertRange(index + common, rest);
            }
            else if (removeCount > items.Length)
            {
                list.RemoveRange(index + common, removeCount - common);
            }
        }

        public Pos RawReplace(Pos a, Pos b, string text)
        {
            string first = Lines[a.Line], last = Lines[b.Line];
            string prefix = first.Substring(0, a.Col), suffix = last.Substring(b.Col);
            int removed = b.Line - a.Line + 1;
            byte firstMark = Marks[a.Line], lastMark = Marks[b.Line];

            bool widthMayShrink = false;
            if (MaxCells >= 0)
            {
                for (int l = a.Line; l <= b.Line && !widthMayShrink; l++)
                {
                    string s = Lines[l];
                    if ((long)s.Length * Cells.MaxW >= MaxCells && Cells.Of(s, s.Length) >= MaxCells) widthMayShrink = true;
                }
            }

            string[] parts = text.IndexOf('\n') < 0 ? null : text.Split('\n');
            int count = parts == null ? 1 : parts.Length;
            string[] nl = new string[count];
            if (parts == null) nl[0] = prefix + text + suffix;
            else
            {
                nl[0] = prefix + parts[0];
                for (int i = 1; i < count - 1; i++) nl[i] = parts[i];
                nl[count - 1] = parts[count - 1] + suffix;
            }

            byte[] nm = new byte[count];
            for (int i = 0; i < count; i++) nm[i] = 1;
            if (nl[0] == first) nm[0] = firstMark;
            else if (count == 1 && nl[0] == last) nm[0] = lastMark;
            if (count > 1 && nl[count - 1] == last) nm[count - 1] = lastMark;

            int[] ns = new int[count];
            ns[0] = States[a.Line];

            ReplaceRange(Lines, a.Line, removed, nl);
            ReplaceRange(Marks, a.Line, removed, nm);
            ReplaceRange(States, a.Line, removed, ns);
            ReplaceRange(Wraps, a.Line, removed, new int[count][]);

            if (StatesValid > a.Line) StatesValid = a.Line;
            RowsDirty = true;
            Version++;
            BackupDirty = true;
            if (widthMayShrink)
            {
                int widest = -1;
                for (int i = 0; i < count; i++) { int c = Cells.Of(nl[i], nl[i].Length); if (c > widest) widest = c; }
                MaxCells = widest >= MaxCells ? widest : -1;
            }
            else if (MaxCells >= 0)
            {
                for (int i = 0; i < count; i++)
                {
                    int c = Cells.Of(nl[i], nl[i].Length);
                    if (c > MaxCells) MaxCells = c;
                }
            }
            return parts == null ? new Pos(a.Line, a.Col + text.Length) : new Pos(a.Line + count - 1, parts[count - 1].Length);
        }

        public UndoGroup OpenGroup(int kind, Pos caret, Pos anchor)
        {
            if (kind != KindNone && UndoPos == Undo.Count && UndoPos > 0 && SavePoint != UndoPos)
            {
                UndoGroup top = Undo[UndoPos - 1];
                if (top.Kind == kind && top.CaretAfter == caret && top.AnchorAfter == anchor &&
                    (DateTime.UtcNow - top.Time).TotalMilliseconds < 2500)
                    return top;
            }
            if (Undo.Count > UndoPos)
            {
                Undo.RemoveRange(UndoPos, Undo.Count - UndoPos);
                if (SavePoint > UndoPos) SavePoint = -1;
            }
            UndoGroup g = new UndoGroup();
            g.Kind = kind;
            g.CaretBefore = caret; g.AnchorBefore = anchor;
            g.CaretAfter = caret; g.AnchorAfter = anchor;
            g.Time = DateTime.UtcNow;
            Undo.Add(g);
            UndoPos++;
            if (Undo.Count > MaxUndo)
            {
                int drop = Undo.Count - MaxUndo + 500;
                Undo.RemoveRange(0, drop);
                UndoPos -= drop;
                if (SavePoint >= 0) { SavePoint -= drop; if (SavePoint < 0) SavePoint = -1; }
            }
            return g;
        }

        public void DiscardIfEmpty(UndoGroup g)
        {
            if (g.Edits.Count == 0 && UndoPos == Undo.Count && UndoPos > 0 && Undo[UndoPos - 1] == g)
            {
                Undo.RemoveAt(UndoPos - 1);
                UndoPos--;
            }
        }

        public Pos Apply(UndoGroup g, Pos a, Pos b, string text)
        {
            if (b < a) { Pos t = a; a = b; b = t; }
            EditRec r = new EditRec();
            r.Start = a;
            r.Removed = GetText(a, b);
            r.Inserted = text;
            r.MarksBefore = Marks.GetRange(a.Line, b.Line - a.Line + 1).ToArray();
            Pos end = RawReplace(a, b, text);
            g.Edits.Add(r);
            g.Time = DateTime.UtcNow;
            return end;
        }

        public bool UndoStep(out Pos caret, out Pos anchor)
        {
            caret = Caret; anchor = Anchor;
            while (UndoPos > 0 && Undo[UndoPos - 1].Edits.Count == 0) { Undo.RemoveAt(UndoPos - 1); UndoPos--; if (SavePoint > UndoPos) SavePoint = -1; }
            if (UndoPos == 0) return false;
            UndoGroup g = Undo[UndoPos - 1];
            bool crossSave = SavePoint >= UndoPos;
            for (int i = g.Edits.Count - 1; i >= 0; i--)
            {
                EditRec e = g.Edits[i];
                RawReplace(e.Start, EndOf(e.Start, e.Inserted), e.Removed);
                for (int j = 0; j < e.MarksBefore.Length; j++)
                {
                    int l = e.Start.Line + j;
                    if (l < Marks.Count) Marks[l] = crossSave ? (byte)1 : e.MarksBefore[j];
                }
            }
            UndoPos--;
            caret = g.CaretBefore;
            anchor = g.AnchorBefore;
            return true;
        }

        public bool RedoStep(out Pos caret, out Pos anchor)
        {
            caret = Caret; anchor = Anchor;
            if (UndoPos >= Undo.Count) return false;
            UndoGroup g = Undo[UndoPos];
            foreach (EditRec e in g.Edits) RawReplace(e.Start, EndOf(e.Start, e.Removed), e.Inserted);
            UndoPos++;
            caret = g.CaretAfter;
            anchor = g.AnchorAfter;
            return true;
        }
    }

    static class Finder
    {
        public static bool IsWordChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        static bool WholeOk(string s, int i, int len)
        {
            return (i == 0 || !IsWordChar(s[i - 1])) && (i + len >= s.Length || !IsWordChar(s[i + len]));
        }

        public static bool IsMatch(string s, int i, string term, bool mc, bool ww)
        {
            if (i < 0 || i + term.Length > s.Length) return false;
            if (string.Compare(s, i, term, 0, term.Length, mc ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) != 0) return false;
            return !ww || WholeOk(s, i, term.Length);
        }

        public static int IndexIn(string s, string term, int from, bool mc, bool ww)
        {
            StringComparison cmp = mc ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            while (from <= s.Length - term.Length)
            {
                int i = s.IndexOf(term, from, cmp);
                if (i < 0) return -1;
                if (!ww || WholeOk(s, i, term.Length)) return i;
                from = i + 1;
            }
            return -1;
        }

        public static int LastIndexIn(string s, string term, int endExclusive, bool mc, bool ww)
        {
            StringComparison cmp = mc ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int start = Math.Min(endExclusive, s.Length) - term.Length;
            while (start >= 0)
            {
                int i = s.LastIndexOf(term, start + term.Length - 1, start + term.Length, cmp);
                if (i < 0) return -1;
                if (!ww || WholeOk(s, i, term.Length)) return i;
                start = i - 1;
            }
            return -1;
        }

        public static void AllIn(string s, string term, bool mc, bool ww, List<int> hits)
        {
            int from = 0;
            while (true)
            {
                int i = IndexIn(s, term, from, mc, ww);
                if (i < 0) break;
                hits.Add(i);
                from = i + Math.Max(1, term.Length);
            }
        }

        public static bool Find(Document d, string term, bool mc, bool ww, Pos from, bool back, out Pos a, out Pos b, out bool wrapped)
        {
            a = b = from;
            wrapped = false;
            if (string.IsNullOrEmpty(term)) return false;
            int n = d.Lines.Count, len = term.Length;
            if (!back)
            {
                for (int l = from.Line; l < n; l++)
                {
                    int i = IndexIn(d.Lines[l], term, l == from.Line ? from.Col : 0, mc, ww);
                    if (i >= 0) { a = new Pos(l, i); b = new Pos(l, i + len); return true; }
                }
                for (int l = 0; l <= from.Line && l < n; l++)
                {
                    int i = IndexIn(d.Lines[l], term, 0, mc, ww);
                    if (i >= 0 && (l < from.Line || i < from.Col)) { a = new Pos(l, i); b = new Pos(l, i + len); wrapped = true; return true; }
                }
            }
            else
            {
                for (int l = from.Line; l >= 0; l--)
                {
                    string s = d.Lines[l];
                    int i = LastIndexIn(s, term, l == from.Line ? from.Col : s.Length, mc, ww);
                    if (i >= 0) { a = new Pos(l, i); b = new Pos(l, i + len); return true; }
                }
                for (int l = n - 1; l >= from.Line; l--)
                {
                    string s = d.Lines[l];
                    int i = LastIndexIn(s, term, s.Length, mc, ww);
                    if (i >= 0 && (l > from.Line || i + len > from.Col)) { a = new Pos(l, i); b = new Pos(l, i + len); wrapped = true; return true; }
                }
            }
            return false;
        }

        public static int Count(Document d, string term, bool mc, bool ww, Pos pos, out int before, List<int> lines)
        {
            before = 0;
            int total = 0;
            if (string.IsNullOrEmpty(term)) return 0;
            List<int> hits = new List<int>();
            for (int l = 0; l < d.Lines.Count; l++)
            {
                hits.Clear();
                AllIn(d.Lines[l], term, mc, ww, hits);
                if (hits.Count == 0) continue;
                foreach (int h in hits)
                {
                    if (l < pos.Line || (l == pos.Line && h < pos.Col)) before++;
                }
                total += hits.Count;
                if (lines != null && lines.Count < 20000) lines.Add(l);
                if (total > 1000000) break;
            }
            return total;
        }
    }
}
