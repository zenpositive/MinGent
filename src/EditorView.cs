using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace MinGent
{
    class EditorView : Control
    {
        static readonly int[] NoWraps = new int[0];
        const TextFormatFlags TF = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                                   TextFormatFlags.NoClipping | TextFormatFlags.PreserveGraphicsClipping;

        public Document Doc;
        Theme th;
        Font font;
        int cw = 8, lineH = 18, textY = 1;
        int gutterW = 60, numW = 24;
        bool wrap;
        public bool GutterVisible = true;
        int wrapCols = 80;
        int topRow;
        readonly int sbW;
        double wheelAcc;
        readonly WheelAcc zoomWheel = new WheelAcc(), hWheel = new WheelAcc();

        readonly System.Windows.Forms.Timer caretTimer = new System.Windows.Forms.Timer();
        readonly int blinkMs;
        bool caretOn = true;
        char pendingHigh;

        bool selecting;
        int selMode;
        Pos dragA, dragB;
        int clickCount;
        DateTime lastClick;
        Point lastClickPt;
        readonly System.Windows.Forms.Timer scrollTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer rewrapTimer = new System.Windows.Forms.Timer();
        bool vDrag, hDrag, vHover, hHover;
        int dragOff;

        string lineCopyText;

        public string HiTerm;
        public bool HiCase, HiWord;
        public List<int> FindLines;

        byte[] kinds = new byte[256];
        int[] cellBuf = new int[256];
        int winA, winB, rowX;
        bool plainLine;
        readonly List<int> findHits = new List<int>();
        readonly List<int> wordHits = new List<int>();
        readonly Dictionary<int, SolidBrush> brushes = new Dictionary<int, SolidBrush>();

        long bmVersion = -1;
        Pos bmCaret, bm1, bm2;
        Document bmDoc;
        bool bmHas;

        public event EventHandler TextEdited, CaretMoved;
        public event Action<int> ZoomRequested;

        public EditorView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            Cursor = Cursors.IBeam;
            TabStop = true;
            AccessibleName = "Text editor";
            AccessibleRole = AccessibleRole.Text;
            sbW = UI.S(14);
            blinkMs = SystemInformation.CaretBlinkTime;
            caretTimer.Interval = blinkMs > 0 ? Math.Max(200, blinkMs) : 1000;
            caretTimer.Tick += delegate { caretOn = !caretOn; InvalidateCaret(); };
            scrollTimer.Interval = 40;
            scrollTimer.Tick += delegate { AutoScrollTick(); };
            rewrapTimer.Interval = 120;
            rewrapTimer.Tick += delegate { rewrapTimer.Stop(); if (wrap && Doc != null) ApplyWrapCols(CalcWrapCols()); };
            th = Theme.Light(Color.SteelBlue);
            font = new Font("Consolas", 11f);
            UpdateMetrics();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                caretTimer.Dispose();
                scrollTimer.Dispose();
                rewrapTimer.Dispose();
                foreach (SolidBrush b in brushes.Values) b.Dispose();
                brushes.Clear();
            }
            base.Dispose(disposing);
        }

        public void SetTheme(Theme t)
        {
            th = t;
            foreach (SolidBrush b in brushes.Values) b.Dispose();
            brushes.Clear();
            BackColor = t.Back;
            Invalidate();
        }

        public void SetEditorFont(Font f)
        {
            int line = TopLine;
            font = f;
            UpdateMetrics();
            wrapCols = CalcWrapCols();
            if (Doc != null)
            {
                Doc.WrapCols = -1;
                topRow = LineToRow(line);
                ClampScroll();
            }
            Invalidate();
        }

        public Font EditorFont { get { return font; } }

        void UpdateMetrics()
        {
            Size big = new Size(int.MaxValue, int.MaxValue);
            Size sz = TextRenderer.MeasureText(new string('M', 100), font, big, TF);
            cw = Math.Max(1, (int)Math.Round(sz.Width / 100.0));
            int h = TextRenderer.MeasureText("Mg", font, big, TF).Height;
            lineH = h + Math.Max(2, h / 5);
            textY = (lineH - h) / 2;
            UpdateGutter();
        }

        void UpdateGutter()
        {
            int g;
            if (!GutterVisible)
            {
                numW = 0;
                g = UI.S(10);
            }
            else
            {
                int lines = Doc != null ? Doc.Lines.Count : 1;
                int digits = Math.Max(3, lines.ToString().Length);
                numW = digits * cw;
                g = UI.S(14) + numW + UI.S(10) + Math.Max(2, UI.S(3)) + UI.S(6);
            }
            if (g != gutterW)
            {
                gutterW = g;
                wrapCols = CalcWrapCols();
            }
        }

        int ChangeBarX { get { return UI.S(14) + numW + UI.S(10); } }
        int TextLeft { get { return gutterW + UI.S(4); } }
        int TextRight { get { return ClientSize.Width - sbW; } }
        int TextAreaW { get { return Math.Max(cw, TextRight - TextLeft); } }
        int CaretW { get { return Math.Max(2, UI.S(2)); } }

        public bool WordWrap
        {
            get { return wrap; }
            set
            {
                if (wrap == value) return;
                int line = TopLine;
                wrap = value;
                wrapCols = CalcWrapCols();
                if (Doc != null)
                {
                    Doc.WrapCols = -1;
                    Doc.ScrollX = 0;
                    topRow = LineToRow(line);
                    ClampScroll();
                    EnsureCaretVisible();
                }
                Invalidate();
            }
        }

        public void SetDocument(Document d)
        {
            if (Doc != null && Doc != d) SyncDocState();
            Doc = d;
            bmVersion = -1;
            selecting = vDrag = hDrag = false;
            pendingHigh = '\0';
            UpdateGutter();
            wrapCols = CalcWrapCols();
            topRow = 0;
            if (d != null)
            {
                int tl = Math.Max(0, Math.Min(d.TopLine, d.Lines.Count - 1));
                topRow = LineToRow(tl);
                ClampScroll();
            }
            ResetCaretBlink();
            Invalidate();
            if (CaretMoved != null) CaretMoved(this, EventArgs.Empty);
        }

        public void SyncDocState()
        {
            if (Doc != null) Doc.TopLine = TopLine;
        }

        int TopLine
        {
            get
            {
                if (Doc == null) return 0;
                if (!wrap) return Math.Min(topRow, Doc.Lines.Count - 1);
                if (Doc.RowStart == null || Doc.RowsDirty || Doc.WrapCols < 0) return RowToLineRaw(topRow);
                int line, seg;
                RowToLine(Math.Min(topRow, Math.Max(0, TotalRows - 1)), out line, out seg);
                return line;
            }
        }

        int CalcWrapCols() { return Math.Max(8, (TextRight - TextLeft - UI.S(4)) / cw); }

        void ApplyWrapCols(int cols)
        {
            if (Doc == null) { wrapCols = cols; return; }
            int line = TopLine;
            wrapCols = cols;
            EnsureWraps();
            topRow = LineToRow(line);
            ClampScroll();
            Invalidate();
        }

        void EnsureWraps()
        {
            if (!wrap || Doc == null) return;
            Document d = Doc;
            int cols = wrapCols;
            if (d.WrapCols != cols)
            {
                for (int i = 0; i < d.Wraps.Count; i++) d.Wraps[i] = null;
                d.WrapCols = cols;
                d.RowsDirty = true;
            }
            if (!d.RowsDirty && d.RowStart != null) return;
            int n = d.Lines.Count;
            if (d.RowStart == null || d.RowStart.Length < n + 1) d.RowStart = new int[n + 1 + n / 4];
            int r = 0;
            for (int i = 0; i < n; i++)
            {
                d.RowStart[i] = r;
                int[] w = d.Wraps[i];
                if (w == null) { w = ComputeWraps(d.Lines[i], cols); d.Wraps[i] = w; }
                r += w.Length + 1;
            }
            d.RowStart[n] = r;
            d.RowsDirty = false;
        }

        static int[] ComputeWraps(string s, int cols)
        {
            if ((long)s.Length * Cells.MaxW <= cols) return NoWraps;
            List<int> br = null;
            int cell = 0, segStart = 0, segCell = 0, lastBreak = -1, lastBreakCell = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int w = Cells.Adv(c, cell);
                if (cell + w - segCell > cols && i > segStart && w > 0)
                {
                    int at, atCell;
                    if (lastBreak > segStart) { at = lastBreak; atCell = lastBreakCell; }
                    else { at = i; atCell = cell; }
                    if (br == null) br = new List<int>();
                    br.Add(at);
                    segStart = at;
                    segCell = atCell;
                    lastBreak = -1;
                }
                cell += w;
                if (c == ' ' || c == '\t') { lastBreak = i + 1; lastBreakCell = cell; }
            }
            return br == null ? NoWraps : br.ToArray();
        }

        int TotalRows
        {
            get
            {
                if (Doc == null) return 1;
                if (!wrap) return Doc.Lines.Count;
                EnsureWraps();
                return Doc.RowStart[Doc.Lines.Count];
            }
        }

        int LineToRow(int line)
        {
            if (!wrap || Doc == null) return line;
            EnsureWraps();
            return Doc.RowStart[line];
        }

        void RowToLine(int row, out int line, out int seg)
        {
            if (!wrap) { line = Math.Max(0, Math.Min(row, Doc.Lines.Count - 1)); seg = 0; return; }
            EnsureWraps();
            int lo = 0, hi = Doc.Lines.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (Doc.RowStart[mid] <= row) lo = mid; else hi = mid - 1;
            }
            line = lo;
            seg = Math.Max(0, Math.Min(row - Doc.RowStart[lo], WrapsOf(lo).Length));
        }

        int RowToLineRaw(int row)
        {
            Document d = Doc;
            if (d.RowStart == null || d.RowStart.Length < d.Lines.Count) return Math.Min(row, d.Lines.Count - 1);
            int lo = 0, hi = d.Lines.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (d.RowStart[mid] <= row) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        int[] WrapsOf(int line)
        {
            if (!wrap) return NoWraps;
            int[] w = Doc.Wraps[line];
            if (w == null)
            {
                EnsureWraps();
                w = Doc.Wraps[line];
            }
            return w ?? NoWraps;
        }

        int SegStart(int line, int seg) { return seg == 0 ? 0 : WrapsOf(line)[seg - 1]; }

        int SegEnd(int line, int seg)
        {
            int[] w = WrapsOf(line);
            return seg < w.Length ? w[seg] : Doc.Lines[line].Length;
        }

        int SegOf(int line, int col)
        {
            int[] w = WrapsOf(line);
            int s = 0;
            while (s < w.Length && col >= w[s]) s++;
            return s;
        }

        int RowOfPos(Pos p) { return LineToRow(p.Line) + SegOf(p.Line, p.Col); }

        int MaxCells()
        {
            Document d = Doc;
            if (d.MaxCells >= 0) return d.MaxCells;
            int m = 0;
            foreach (string s in d.Lines)
            {
                if ((long)s.Length * Cells.MaxW <= m) continue;
                int c = Cells.Of(s, s.Length);
                if (c > m) m = c;
            }
            d.MaxCells = m;
            return m;
        }

        int ContentWidth { get { return MaxCells() * cw + UI.S(40); } }
        bool HBarVisible { get { return Doc != null && !wrap && ContentWidth > TextAreaW; } }
        int ViewH { get { return Math.Max(lineH, ClientSize.Height - (HBarVisible ? sbW : 0)); } }
        int VisibleRows { get { return Math.Max(1, ViewH / lineH); } }
        int MaxTop { get { return Math.Max(0, TotalRows - VisibleRows); } }
        int MaxScrollX { get { return Math.Max(0, ContentWidth - TextAreaW); } }

        void ClampScroll()
        {
            if (Doc == null) return;
            topRow = Math.Max(0, Math.Min(topRow, MaxTop));
            Doc.ScrollX = wrap ? 0 : Math.Max(0, Math.Min(Doc.ScrollX, MaxScrollX));
        }

        public void ScrollRows(int delta)
        {
            if (Doc == null) return;
            int old = topRow;
            topRow = Math.Max(0, Math.Min(topRow + delta, MaxTop));
            if (topRow != old) Invalidate();
        }

        void ScrollXBy(int dx)
        {
            if (Doc == null || wrap) return;
            int old = Doc.ScrollX;
            Doc.ScrollX = Math.Max(0, Math.Min(Doc.ScrollX + dx, MaxScrollX));
            if (Doc.ScrollX != old) Invalidate();
        }

        public void EnsureCaretVisible()
        {
            if (Doc == null) return;
            int vis = VisibleRows, row = RowOfPos(Doc.Caret);
            if (row < topRow) topRow = row;
            else if (row >= topRow + vis) topRow = row - vis + 1;
            if (!wrap)
            {
                string s = Doc.Lines[Doc.Caret.Line];
                int cx = Cells.Of(s, Doc.Caret.Col) * cw;
                int vw = Math.Max(cw * 4, TextAreaW - UI.S(8));
                if (cx < Doc.ScrollX) Doc.ScrollX = Math.Max(0, cx - vw / 4);
                else if (cx + cw > Doc.ScrollX + vw) Doc.ScrollX = cx + cw - vw + vw / 4;
            }
            ClampScroll();
        }

        void CenterCaretIfHidden()
        {
            int vis = VisibleRows, row = RowOfPos(Doc.Caret);
            if (row < topRow || row >= topRow + vis) topRow = row - vis / 3;
            EnsureCaretVisible();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Doc == null) return;
            int nc = CalcWrapCols();
            if (wrap && nc != wrapCols)
            {
                if (Doc.Lines.Count > 20000) { rewrapTimer.Stop(); rewrapTimer.Start(); }
                else ApplyWrapCols(nc);
            }
            ClampScroll();
            Invalidate();
        }

        Point CaretPoint()
        {
            Document d = Doc;
            Pos c = d.Caret;
            int seg = SegOf(c.Line, c.Col);
            int row = LineToRow(c.Line) + seg;
            string s = d.Lines[c.Line];
            int a = SegStart(c.Line, seg);
            int x = TextLeft - (wrap ? 0 : d.ScrollX) + (Cells.Of(s, c.Col) - (a > 0 ? Cells.Of(s, a) : 0)) * cw;
            return new Point(x, (row - topRow) * lineH);
        }

        void InvalidateCaret()
        {
            if (Doc == null) return;
            Point p = CaretPoint();
            Invalidate(new Rectangle(p.X - 2, p.Y, CaretW + 4, lineH));
        }

        void ResetCaretBlink()
        {
            caretOn = true;
            caretTimer.Stop();
            if (blinkMs > 0 && Focused) caretTimer.Start();
        }

        Pos PosFromPoint(int x, int y)
        {
            int row = topRow + (int)Math.Floor(y / (double)lineH);
            int total = TotalRows;
            if (row < 0) row = 0;
            if (row >= total) row = total - 1;
            int line, seg;
            RowToLine(row, out line, out seg);
            int xr = x - TextLeft + (wrap ? 0 : Doc.ScrollX);
            return new Pos(line, ColFromX(line, seg, xr));
        }

        int ColFromX(int line, int seg, int xr)
        {
            string s = Doc.Lines[line];
            int a = SegStart(line, seg), b = SegEnd(line, seg);
            if (!wrap && b - a > 4096 && Cells.Uniform(s))
                return Math.Max(a, Math.Min(b, a + (int)Math.Round(xr / (double)cw)));
            int baseCell = a > 0 ? Cells.Of(s, a) : 0;
            int cell = baseCell;
            for (int i = a; i < b; i++)
            {
                int w = Cells.Adv(s[i], cell);
                if (w > 0 && xr < (cell - baseCell) * cw + w * cw / 2.0) return i;
                cell += w;
            }
            if (b < s.Length && b > a)
            {
                int r = b - 1;
                while (r > a && Cells.W(s[r]) == 0) r--;
                return r;
            }
            return b;
        }

        Pos CharLeft(Pos p)
        {
            if (p.Col > 0)
            {
                string s = Doc.Lines[p.Line];
                int c = Math.Min(p.Col, s.Length) - 1;
                while (c > 0 && s[c] != '\t' && Cells.W(s[c]) == 0) c--;
                return new Pos(p.Line, c);
            }
            if (p.Line > 0) return new Pos(p.Line - 1, Doc.Lines[p.Line - 1].Length);
            return p;
        }

        Pos CharRight(Pos p)
        {
            string s = Doc.Lines[p.Line];
            if (p.Col < s.Length)
            {
                int c = p.Col + 1;
                while (c < s.Length && s[c] != '\t' && Cells.W(s[c]) == 0) c++;
                return new Pos(p.Line, c);
            }
            if (p.Line < Doc.Lines.Count - 1) return new Pos(p.Line + 1, 0);
            return p;
        }

        static int CharClass(char c)
        {
            if (char.IsWhiteSpace(c)) return 0;
            if (char.IsLetterOrDigit(c) || c == '_') return 1;
            return 2;
        }

        Pos WordLeft(Pos p)
        {
            if (p.Col == 0) return p.Line > 0 ? new Pos(p.Line - 1, Doc.Lines[p.Line - 1].Length) : p;
            string s = Doc.Lines[p.Line];
            int i = p.Col;
            while (i > 0 && CharClass(s[i - 1]) == 0) i--;
            if (i > 0)
            {
                int cls = CharClass(s[i - 1]);
                while (i > 0 && CharClass(s[i - 1]) == cls) i--;
            }
            return new Pos(p.Line, i);
        }

        Pos WordRight(Pos p)
        {
            string s = Doc.Lines[p.Line];
            if (p.Col >= s.Length) return p.Line < Doc.Lines.Count - 1 ? new Pos(p.Line + 1, 0) : p;
            int i = p.Col;
            int cls = CharClass(s[i]);
            if (cls != 0) while (i < s.Length && CharClass(s[i]) == cls) i++;
            while (i < s.Length && CharClass(s[i]) == 0) i++;
            return new Pos(p.Line, i);
        }

        void WordBounds(Pos p, out Pos a, out Pos b)
        {
            string s = Doc.Lines[p.Line];
            int i = Math.Min(p.Col, s.Length);
            if (s.Length == 0) { a = b = new Pos(p.Line, 0); return; }
            int idx;
            if (i < s.Length && CharClass(s[i]) == 1) idx = i;
            else if (i > 0 && CharClass(s[i - 1]) == 1) idx = i - 1;
            else idx = Math.Min(i, s.Length - 1);
            if (char.IsLowSurrogate(s[idx]) && idx > 0 && char.IsHighSurrogate(s[idx - 1])) idx--;
            int cls = CharClass(s[idx]);
            int st = idx, en = idx + 1;
            if (cls != 2)
            {
                while (st > 0 && CharClass(s[st - 1]) == cls) st--;
                while (en < s.Length && CharClass(s[en]) == cls) en++;
            }
            if (char.IsHighSurrogate(s[en - 1]) && en < s.Length && char.IsLowSurrogate(s[en])) en++;
            while (en < s.Length && Cells.W(s[en]) == 0 && s[en] != '\t') en++;
            while (st > 0 && Cells.W(s[st]) == 0 && s[st] != '\t') st--;
            a = new Pos(p.Line, st);
            b = new Pos(p.Line, en);
        }

        Pos LineEndIncl(int line)
        {
            return line < Doc.Lines.Count - 1 ? new Pos(line + 1, 0) : new Pos(line, Doc.Lines[line].Length);
        }

        Pos SmartHome()
        {
            string s = Doc.Lines[Doc.Caret.Line];
            int ind = 0;
            while (ind < s.Length && (s[ind] == ' ' || s[ind] == '\t')) ind++;
            return new Pos(Doc.Caret.Line, Doc.Caret.Col == ind ? 0 : ind);
        }

        bool GetSel(out Pos s0, out Pos s1)
        {
            s0 = Pos.Min(Doc.Caret, Doc.Anchor);
            s1 = Pos.Max(Doc.Caret, Doc.Anchor);
            return s0 != s1;
        }

        public bool HasSelection { get { return Doc != null && Doc.Caret != Doc.Anchor; } }

        public void GetSelection(out Pos s0, out Pos s1) { GetSel(out s0, out s1); }

        public string SelectedText
        {
            get { Pos a, b; return Doc != null && GetSel(out a, out b) ? Doc.GetText(a, b) : ""; }
        }

        public string FindSeed()
        {
            Pos a, b;
            if (Doc != null && GetSel(out a, out b) && a.Line == b.Line && b.Col - a.Col <= 200) return Doc.GetText(a, b);
            return null;
        }

        void RaiseCaret() { if (CaretMoved != null) CaretMoved(this, EventArgs.Empty); }

        public void MoveTo(Pos p, bool extend)
        {
            p = Doc.Clamp(p);
            Doc.Caret = p;
            if (!extend) Doc.Anchor = p;
            Doc.DesiredX = -1;
            EnsureCaretVisible();
            ResetCaretBlink();
            Invalidate();
            RaiseCaret();
        }

        void SetSel(Pos anchor, Pos caret)
        {
            Doc.Anchor = Doc.Clamp(anchor);
            Doc.Caret = Doc.Clamp(caret);
            Doc.DesiredX = -1;
            EnsureCaretVisible();
            ResetCaretBlink();
            Invalidate();
            RaiseCaret();
        }

        public void Select(Pos a, Pos b)
        {
            Doc.Anchor = Doc.Clamp(a);
            Doc.Caret = Doc.Clamp(b);
            Doc.DesiredX = -1;
            CenterCaretIfHidden();
            ResetCaretBlink();
            Invalidate();
            RaiseCaret();
        }

        public void SelectAll()
        {
            if (Doc == null) return;
            Doc.Anchor = new Pos(0, 0);
            Doc.Caret = Doc.End;
            Doc.DesiredX = -1;
            EnsureCaretVisible();
            Invalidate();
            RaiseCaret();
        }

        public void GoToLine(int line)
        {
            if (Doc == null) return;
            Pos p = Doc.Clamp(new Pos(line, 0));
            Doc.Caret = Doc.Anchor = p;
            Doc.DesiredX = -1;
            CenterCaretIfHidden();
            ResetCaretBlink();
            Invalidate();
            RaiseCaret();
        }

        void MoveVert(int delta, bool extend, bool scroll)
        {
            int row = RowOfPos(Doc.Caret);
            if (Doc.DesiredX < 0)
            {
                Point cp = CaretPoint();
                Doc.DesiredX = cp.X - TextLeft + (wrap ? 0 : Doc.ScrollX);
            }
            int keep = Doc.DesiredX;
            int total = TotalRows;
            int nr = Math.Max(0, Math.Min(total - 1, row + delta));
            Pos p;
            if (nr == row && delta != 0) p = delta < 0 ? new Pos(0, 0) : Doc.End;
            else
            {
                int line, seg;
                RowToLine(nr, out line, out seg);
                p = new Pos(line, ColFromX(line, seg, keep));
            }
            if (scroll) topRow = Math.Max(0, Math.Min(MaxTop, topRow + (nr - row)));
            MoveTo(p, extend);
            Doc.DesiredX = keep;
        }

        void AfterEdit()
        {
            Doc.DesiredX = -1;
            UpdateGutter();
            EnsureCaretVisible();
            ResetCaretBlink();
            Invalidate();
            if (TextEdited != null) TextEdited(this, EventArgs.Empty);
            RaiseCaret();
        }

        void Edit(Pos a, Pos b, string text, int kind)
        {
            UndoGroup g = Doc.OpenGroup(kind, Doc.Caret, Doc.Anchor);
            Pos end = Doc.Apply(g, a, b, text);
            Doc.Caret = Doc.Anchor = end;
            g.CaretAfter = g.AnchorAfter = end;
            AfterEdit();
        }

        public void AppendText(string text)
        {
            if (Doc == null || string.IsNullOrEmpty(text)) return;
            Pos end = Doc.End;
            Doc.RawReplace(end, end, Document.NormalizeNewlines(text));
            Doc.SavePoint = Doc.UndoPos;
            Doc.Caret = Doc.Anchor = Doc.End;
            Doc.DesiredX = -1;
            UpdateGutter();
            EnsureCaretVisible();
            Invalidate();
            RaiseCaret();
        }

        public void ReplaceSelection(string text)
        {
            Pos a, b;
            GetSel(out a, out b);
            Edit(a, b, Document.NormalizeNewlines(text), Document.KindNone);
        }

        public string ApplyReplace(string search, string replace)
        {
            if (Doc == null) return "no document is open";
            Pos a, e;
            string err = Doc.ApplyBlock(search, replace, out a, out e);
            if (err != null) return err;
            AfterEdit();
            Select(a, e);
            EnsureCaretVisible();
            return null;
        }

        public bool ReplaceAllText(string text)
        {
            if (Doc == null) return false;
            Pos a, e;
            if (!Doc.ReplaceAllMinimal(text, out a, out e)) return false;
            AfterEdit();
            Select(a, e);
            EnsureCaretVisible();
            return true;
        }

        void TypeText(string s)
        {
            Pos a, b;
            bool sel = GetSel(out a, out b);
            int kind = sel ? Document.KindNone : (s.Length == 1 && char.IsWhiteSpace(s[0]) ? Document.KindSpace : Document.KindWord);
            Edit(a, b, s, kind);
        }

        void Backspace(bool word)
        {
            Pos a, b;
            if (GetSel(out a, out b)) { Edit(a, b, "", Document.KindNone); return; }
            Pos c = Doc.Caret;
            Pos from = word ? WordLeft(c) : CharLeft(c);
            if (from == c) return;
            Edit(from, c, "", word ? Document.KindNone : Document.KindBack);
        }

        void DeleteForward(bool word)
        {
            Pos a, b;
            if (GetSel(out a, out b)) { Edit(a, b, "", Document.KindNone); return; }
            Pos c = Doc.Caret;
            Pos to = word ? WordRight(c) : CharRight(c);
            if (to == c) return;
            Edit(c, to, "", word ? Document.KindNone : Document.KindDel);
        }

        public void DeleteSelection()
        {
            Pos a, b;
            if (GetSel(out a, out b)) Edit(a, b, "", Document.KindNone);
            else DeleteForward(false);
        }

        void NewLine()
        {
            Pos a, b;
            GetSel(out a, out b);
            string s = Doc.Lines[a.Line];
            int ind = 0;
            while (ind < s.Length && ind < a.Col && (s[ind] == ' ' || s[ind] == '\t')) ind++;
            Edit(a, b, "\n" + s.Substring(0, ind), Document.KindNone);
        }

        void TabKey(bool shift)
        {
            Pos a, b;
            bool sel = GetSel(out a, out b);
            if (shift || (sel && a.Line != b.Line)) { IndentLines(shift); return; }
            string ins = Doc.IndentUnit;
            if (ins != "\t")
            {
                int at = Cells.Of(Doc.Lines[a.Line], a.Col);
                ins = new string(' ', ins.Length - (at % ins.Length));
            }
            Edit(a, b, ins, sel ? Document.KindNone : Document.KindSpace);
        }

        void IndentLines(bool outdent)
        {
            Pos a, b;
            GetSel(out a, out b);
            int l0 = a.Line, l1 = b.Line;
            if (l1 > l0 && b.Col == 0) l1--;
            string unit = Doc.IndentUnit;
            int maxSpaces = unit == "\t" ? Cells.TabSize : unit.Length;
            UndoGroup g = null;
            Pos caret = Doc.Caret, anchor = Doc.Anchor;
            for (int l = l0; l <= l1; l++)
            {
                string s = Doc.Lines[l];
                int delta;
                if (!outdent)
                {
                    if (s.Length == 0 && l0 != l1) continue;
                    if (g == null) g = Doc.OpenGroup(Document.KindNone, Doc.Caret, Doc.Anchor);
                    Doc.Apply(g, new Pos(l, 0), new Pos(l, 0), unit);
                    delta = unit.Length;
                }
                else
                {
                    int rm = 0;
                    if (s.Length > 0 && s[0] == '\t') rm = 1;
                    else while (rm < s.Length && rm < maxSpaces && s[rm] == ' ') rm++;
                    if (rm == 0) continue;
                    if (g == null) g = Doc.OpenGroup(Document.KindNone, Doc.Caret, Doc.Anchor);
                    Doc.Apply(g, new Pos(l, 0), new Pos(l, rm), "");
                    delta = -rm;
                }
                caret = ShiftCol(caret, l, delta, outdent, l0 != l1);
                anchor = ShiftCol(anchor, l, delta, outdent, l0 != l1);
            }
            if (g == null) return;
            Doc.Caret = Doc.Clamp(caret);
            Doc.Anchor = Doc.Clamp(anchor);
            g.CaretAfter = Doc.Caret;
            g.AnchorAfter = Doc.Anchor;
            AfterEdit();
        }

        static Pos ShiftCol(Pos p, int line, int delta, bool outdent, bool block)
        {
            if (p.Line != line) return p;
            if (!outdent && block && p.Col == 0) return p;
            return new Pos(p.Line, Math.Max(0, p.Col + delta));
        }

        public void Undo()
        {
            if (Doc == null) return;
            Pos c, a;
            if (Doc.UndoStep(out c, out a))
            {
                Doc.Caret = Doc.Clamp(c);
                Doc.Anchor = Doc.Clamp(a);
                AfterEdit();
            }
        }

        public void Redo()
        {
            if (Doc == null) return;
            Pos c, a;
            if (Doc.RedoStep(out c, out a))
            {
                Doc.Caret = Doc.Clamp(c);
                Doc.Anchor = Doc.Clamp(a);
                AfterEdit();
            }
        }

        public void Copy()
        {
            Pos a, b;
            if (GetSel(out a, out b))
            {
                SetClipboard(Doc.GetText(a, b));
                lineCopyText = null;
            }
            else
            {
                string t = Doc.Lines[Doc.Caret.Line] + "\n";
                SetClipboard(t);
                lineCopyText = t;
            }
        }

        public void Cut()
        {
            Pos a, b;
            if (GetSel(out a, out b))
            {
                SetClipboard(Doc.GetText(a, b));
                lineCopyText = null;
                Edit(a, b, "", Document.KindNone);
                return;
            }
            int l = Doc.Caret.Line;
            string t = Doc.Lines[l] + "\n";
            SetClipboard(t);
            lineCopyText = t;
            Pos from, to;
            if (l < Doc.Lines.Count - 1) { from = new Pos(l, 0); to = new Pos(l + 1, 0); }
            else if (l > 0) { from = new Pos(l - 1, Doc.Lines[l - 1].Length); to = new Pos(l, Doc.Lines[l].Length); }
            else { from = new Pos(0, 0); to = new Pos(0, Doc.Lines[0].Length); }
            if (from == to) return;
            Edit(from, to, "", Document.KindNone);
        }

        public void Paste()
        {
            if (Doc == null) return;
            string t = GetClipboard();
            if (string.IsNullOrEmpty(t)) return;
            t = Document.NormalizeNewlines(t);
            Pos a, b;
            if (!GetSel(out a, out b) && lineCopyText != null && t == lineCopyText)
            {
                Pos c = Doc.Caret;
                Pos at = new Pos(c.Line, 0);
                UndoGroup g = Doc.OpenGroup(Document.KindNone, Doc.Caret, Doc.Anchor);
                Doc.Apply(g, at, at, t);
                Doc.Caret = Doc.Anchor = Doc.Clamp(new Pos(c.Line + 1, c.Col));
                g.CaretAfter = g.AnchorAfter = Doc.Caret;
                AfterEdit();
                return;
            }
            Edit(a, b, t, Document.KindNone);
        }

        public bool CanPaste
        {
            get { try { return Clipboard.ContainsText(); } catch { return false; } }
        }

        static void SetClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string win = text.Replace("\n", "\r\n");
            for (int i = 0; i < 3; i++)
            {
                try { Clipboard.SetDataObject(win, true); return; }
                catch (Exception) { Thread.Sleep(50); }
            }
        }

        static string GetClipboard()
        {
            for (int i = 0; i < 3; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch (Exception) { Thread.Sleep(50); }
            }
            return null;
        }

        public bool ReplaceIfMatch(string term, string repl, bool mc, bool ww)
        {
            Pos a, b;
            if (!GetSel(out a, out b) || a.Line != b.Line || b.Col - a.Col != term.Length) return false;
            if (!Finder.IsMatch(Doc.Lines[a.Line], a.Col, term, mc, ww)) return false;
            Edit(a, b, repl, Document.KindNone);
            return true;
        }

        public int ReplaceAll(string term, string repl, bool mc, bool ww)
        {
            if (Doc == null || string.IsNullOrEmpty(term)) return 0;
            UndoGroup g = null;
            int count = 0;
            List<int> hits = new List<int>();
            for (int l = 0; l < Doc.Lines.Count; l++)
            {
                string s = Doc.Lines[l];
                hits.Clear();
                Finder.AllIn(s, term, mc, ww, hits);
                if (hits.Count == 0) continue;
                System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length + hits.Count * Math.Max(0, repl.Length - term.Length));
                int last = 0;
                foreach (int h in hits)
                {
                    sb.Append(s, last, h - last).Append(repl);
                    last = h + term.Length;
                }
                sb.Append(s, last, s.Length - last);
                if (g == null) g = Doc.OpenGroup(Document.KindNone, Doc.Caret, Doc.Anchor);
                Doc.Apply(g, new Pos(l, 0), new Pos(l, s.Length), sb.ToString());
                count += hits.Count;
            }
            if (g != null)
            {
                Doc.Caret = Doc.Clamp(Doc.Caret);
                Doc.Anchor = Doc.Caret;
                g.CaretAfter = g.AnchorAfter = Doc.Caret;
                AfterEdit();
            }
            return count;
        }

        protected override bool IsInputChar(char charCode)
        {
            return true;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.Tab: case Keys.Enter: case Keys.Escape:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return (keyData & Keys.Alt) == 0;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || Doc == null) return;
            bool ctrl = e.Control, shift = e.Shift, alt = e.Alt;
            bool cmd = ctrl && !alt;
            bool handled = true;
            switch (e.KeyCode)
            {
                case Keys.Left:
                    if (alt) { handled = false; break; }
                    if (ctrl) MoveTo(WordLeft(Doc.Caret), shift);
                    else if (!shift && HasSelection) MoveTo(Pos.Min(Doc.Caret, Doc.Anchor), false);
                    else MoveTo(CharLeft(Doc.Caret), shift);
                    break;
                case Keys.Right:
                    if (alt) { handled = false; break; }
                    if (ctrl) MoveTo(WordRight(Doc.Caret), shift);
                    else if (!shift && HasSelection) MoveTo(Pos.Max(Doc.Caret, Doc.Anchor), false);
                    else MoveTo(CharRight(Doc.Caret), shift);
                    break;
                case Keys.Up:
                    if (alt) { handled = false; break; }
                    if (ctrl) ScrollRows(-1); else MoveVert(-1, shift, false);
                    break;
                case Keys.Down:
                    if (alt) { handled = false; break; }
                    if (ctrl) ScrollRows(1); else MoveVert(1, shift, false);
                    break;
                case Keys.PageUp: MoveVert(-Math.Max(1, VisibleRows - 1), shift, true); break;
                case Keys.PageDown: MoveVert(Math.Max(1, VisibleRows - 1), shift, true); break;
                case Keys.Home:
                    if (ctrl) MoveTo(new Pos(0, 0), shift); else MoveTo(SmartHome(), shift);
                    break;
                case Keys.End:
                    if (ctrl) MoveTo(Doc.End, shift); else MoveTo(new Pos(Doc.Caret.Line, Doc.Lines[Doc.Caret.Line].Length), shift);
                    break;
                case Keys.Back:
                    if (alt && !ctrl) { if (shift) Redo(); else Undo(); break; }
                    Backspace(ctrl);
                    break;
                case Keys.Delete:
                    if (shift && !ctrl) Cut(); else DeleteForward(ctrl);
                    break;
                case Keys.Insert:
                    if (ctrl && !shift) Copy();
                    else if (shift && !ctrl) Paste();
                    break;
                case Keys.Enter:
                    if (alt) { handled = false; break; }
                    NewLine();
                    break;
                case Keys.Tab:
                    if (ctrl || alt) { handled = false; break; }
                    TabKey(shift);
                    break;
                case Keys.Escape:
                    if (HasSelection) MoveTo(Doc.Caret, false); else handled = false;
                    break;
                case Keys.A: if (cmd && !shift) SelectAll(); else handled = false; break;
                case Keys.C: if (cmd && !shift) Copy(); else handled = false; break;
                case Keys.X: if (cmd && !shift) Cut(); else handled = false; break;
                case Keys.V: if (cmd && !shift) Paste(); else handled = false; break;
                case Keys.Z: if (cmd) { if (shift) Redo(); else Undo(); } else handled = false; break;
                case Keys.Y: if (cmd && !shift) Redo(); else handled = false; break;
                default: handled = false; break;
            }
            if (handled) { e.Handled = true; e.SuppressKeyPress = true; }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (e.Handled || Doc == null) return;
            char c = e.KeyChar;
            if (c < ' ' || c == (char)127) return;
            e.Handled = true;
            if (char.IsHighSurrogate(c)) { pendingHigh = c; return; }
            string s = (char.IsLowSurrogate(c) && pendingHigh != '\0') ? new string(new char[] { pendingHigh, c }) : c.ToString();
            pendingHigh = '\0';
            TypeText(s);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            ResetCaretBlink();
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            caretTimer.Stop();
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (Doc != null)
            {
                if (m.Msg == Native.WM_IME_STARTCOMPOSITION || m.Msg == Native.WM_IME_COMPOSITION)
                {
                    Point p = CaretPoint();
                    Native.SetImePosition(this, p.X, p.Y);
                }
                else if (m.Msg == Native.WM_MOUSEHWHEEL)
                {
                    int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                    ScrollXBy(delta * cw * 4 / 120);
                    m.Result = IntPtr.Zero;
                    return;
                }
                else if (m.Msg == 0x007B  && m.LParam.ToInt64() == -1 && ContextMenuStrip != null)
                {
                    Point p = CaretPoint();
                    ContextMenuStrip.Show(this, new Point(Math.Max(p.X, TextLeft), Math.Max(0, p.Y + lineH)));
                    return;
                }
            }
            base.WndProc(ref m);
        }

        Rectangle VTrack() { return new Rectangle(ClientSize.Width - sbW, 0, sbW, ViewH); }
        bool VBarActive { get { return Doc != null && TotalRows > VisibleRows; } }

        Rectangle VThumb()
        {
            Rectangle t = VTrack();
            int total = TotalRows, vis = VisibleRows, maxTop = Math.Max(1, total - vis);
            int len = (int)Math.Max(UI.S(24), (long)t.Height * vis / Math.Max(1, total));
            if (len > t.Height) len = t.Height;
            int y = t.Top + (int)((long)(t.Height - len) * Math.Min(topRow, maxTop) / maxTop);
            return new Rectangle(t.Left, y, t.Width, len);
        }

        Rectangle HTrack() { return new Rectangle(gutterW, ClientSize.Height - sbW, Math.Max(1, TextRight - gutterW), sbW); }

        Rectangle HThumb()
        {
            Rectangle t = HTrack();
            int content = ContentWidth, view = TextAreaW, max = Math.Max(1, content - view);
            int len = (int)Math.Max(UI.S(24), (long)t.Width * view / Math.Max(1, content));
            if (len > t.Width) len = t.Width;
            int x = t.Left + (int)((long)(t.Width - len) * Math.Min(Doc.ScrollX, max) / max);
            return new Rectangle(x, t.Top, len, t.Height);
        }

        bool HitVBar(Point p) { return VBarActive && VTrack().Contains(p); }
        bool HitHBar(Point p) { return HBarVisible && HTrack().Contains(p); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Doc == null) return;
            if (!Focused) Focus();
            if (e.Button == MouseButtons.Left)
            {
                if (HitVBar(e.Location))
                {
                    Rectangle t = VThumb();
                    if (t.Contains(e.Location)) { vDrag = true; dragOff = e.Y - t.Top; Capture = true; }
                    else ScrollRows(e.Y < t.Top ? -VisibleRows : VisibleRows);
                    Invalidate();
                    return;
                }
                if (HitHBar(e.Location))
                {
                    Rectangle t = HThumb();
                    if (t.Contains(e.Location)) { hDrag = true; dragOff = e.X - t.Left; Capture = true; }
                    else ScrollXBy(e.X < t.Left ? -TextAreaW : TextAreaW);
                    Invalidate();
                    return;
                }
                DateTime now = DateTime.UtcNow;
                Size ds = SystemInformation.DoubleClickSize;
                if ((now - lastClick).TotalMilliseconds <= SystemInformation.DoubleClickTime &&
                    Math.Abs(e.X - lastClickPt.X) <= ds.Width && Math.Abs(e.Y - lastClickPt.Y) <= ds.Height)
                    clickCount++;
                else clickCount = 1;
                lastClick = now;
                lastClickPt = e.Location;

                Pos p = PosFromPoint(e.X, e.Y);
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                if (e.X < gutterW)
                {
                    selMode = 3;
                    if (shift)
                    {
                        Pos anc = Doc.Anchor;
                        dragA = new Pos(anc.Line, 0);
                        dragB = LineEndIncl(anc.Line);
                        if (p.Line < anc.Line) SetSel(dragB, new Pos(p.Line, 0)); else SetSel(dragA, LineEndIncl(p.Line));
                    }
                    else
                    {
                        dragA = new Pos(p.Line, 0);
                        dragB = LineEndIncl(p.Line);
                        SetSel(dragA, dragB);
                    }
                }
                else if (clickCount == 2)
                {
                    selMode = 1;
                    WordBounds(p, out dragA, out dragB);
                    SetSel(dragA, dragB);
                }
                else if (clickCount >= 3)
                {
                    selMode = 2;
                    dragA = new Pos(p.Line, 0);
                    dragB = LineEndIncl(p.Line);
                    SetSel(dragA, dragB);
                }
                else
                {
                    selMode = 0;
                    MoveTo(p, shift);
                }
                selecting = true;
                Capture = true;
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (e.X >= gutterW && !HitVBar(e.Location) && !HitHBar(e.Location))
                {
                    Pos p = PosFromPoint(e.X, e.Y);
                    Pos a, b;
                    if (!GetSel(out a, out b) || p < a || p > b) MoveTo(p, false);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Doc == null) return;
            if (vDrag)
            {
                Rectangle t = VTrack(), thb = VThumb();
                int range = t.Height - thb.Height;
                if (range > 0)
                {
                    topRow = (int)((long)(e.Y - dragOff - t.Top) * MaxTop / range);
                    ClampScroll();
                    Invalidate();
                }
                return;
            }
            if (hDrag)
            {
                Rectangle t = HTrack(), thb = HThumb();
                int range = t.Width - thb.Width;
                if (range > 0)
                {
                    Doc.ScrollX = (int)((long)(e.X - dragOff - t.Left) * MaxScrollX / range);
                    ClampScroll();
                    Invalidate();
                }
                return;
            }
            if (selecting && (e.Button & MouseButtons.Left) != 0)
            {
                ExtendSelection(e.Location);
                bool outside = e.Y < 0 || e.Y > ViewH || (!wrap && (e.X < gutterW || e.X > TextRight));
                if (outside) { if (!scrollTimer.Enabled) scrollTimer.Start(); }
                else scrollTimer.Stop();
                return;
            }
            bool vh = HitVBar(e.Location), hh = HitHBar(e.Location);
            if (vh != vHover || hh != hHover) { vHover = vh; hHover = hh; Invalidate(); }
            Cursor want = (vh || hh || e.X < gutterW) ? Cursors.Arrow : Cursors.IBeam;
            if (Cursor != want) Cursor = want;
        }

        void ExtendSelection(Point pt)
        {
            Pos p = PosFromPoint(pt.X, pt.Y);
            switch (selMode)
            {
                case 1:
                    {
                        Pos wa, wb;
                        WordBounds(p, out wa, out wb);
                        if (p < dragA) { Doc.Anchor = dragB; Doc.Caret = wa; }
                        else { Doc.Anchor = dragA; Doc.Caret = Pos.Max(wb, dragB); }
                        break;
                    }
                case 2:
                case 3:
                    if (p.Line < dragA.Line) { Doc.Anchor = dragB; Doc.Caret = new Pos(p.Line, 0); }
                    else { Doc.Anchor = dragA; Doc.Caret = LineEndIncl(p.Line); }
                    break;
                default:
                    Doc.Caret = p;
                    break;
            }
            Doc.DesiredX = -1;
            EnsureCaretVisible();
            ResetCaretBlink();
            Invalidate();
            RaiseCaret();
        }

        void AutoScrollTick()
        {
            if (!selecting || Doc == null || (MouseButtons & MouseButtons.Left) == 0) { scrollTimer.Stop(); return; }
            Point m = PointToClient(MousePosition);
            int vh = ViewH;
            if (m.Y < 0) topRow = Math.Max(0, topRow - Math.Max(1, -m.Y / lineH + 1));
            else if (m.Y > vh) topRow = Math.Min(MaxTop, topRow + Math.Max(1, (m.Y - vh) / lineH + 1));
            if (!wrap)
            {
                if (m.X < gutterW) Doc.ScrollX = Math.Max(0, Doc.ScrollX - cw * 4);
                else if (m.X > TextRight) Doc.ScrollX = Math.Min(MaxScrollX, Doc.ScrollX + cw * 4);
            }
            ExtendSelection(m);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            selecting = false;
            vDrag = hDrag = false;
            scrollTimer.Stop();
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) { selecting = false; vDrag = hDrag = false; scrollTimer.Stop(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (vHover || hHover) { vHover = hHover = false; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Doc == null) return;
            if ((ModifierKeys & Keys.Control) != 0)
            {
                int n = zoomWheel.Notches(e.Delta);
                for (int i = 0; i < Math.Abs(n) && i < 8 && ZoomRequested != null; i++) ZoomRequested(n > 0 ? 1 : -1);
                return;
            }
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                int px = hWheel.Take(e.Delta, cw * 4);
                if (px != 0) ScrollXBy(-px);
                return;
            }
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) lines = Math.Max(1, VisibleRows - 1);
            wheelAcc += -e.Delta * lines / 120.0;
            int rows = (int)wheelAcc;
            if (rows != 0)
            {
                wheelAcc -= rows;
                ScrollRows(rows);
            }
        }

        SolidBrush B(Color c)
        {
            int key = c.ToArgb();
            SolidBrush b;
            if (!brushes.TryGetValue(key, out b)) { b = new SolidBrush(c); brushes[key] = b; }
            return b;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        void PrepareLine(int line, string smart, bool hasSel, Pos s0, Pos s1)
        {
            string s = Doc.Lines[line];
            bool selColored = th.SelFore != Color.Empty && hasSel && line >= s0.Line && line <= s1.Line && s.Length <= 1000000;
            plainLine = !selColored && (!Doc.HighlightOn || s.Length > Syntax.MaxLineLen);
            if (!plainLine)
            {
                if (kinds.Length < s.Length + 1) kinds = new byte[Math.Max(s.Length + 1, kinds.Length * 2)];
                Array.Clear(kinds, 0, s.Length + 1);
            }
            if (!plainLine && Doc.HighlightOn && s.Length <= Syntax.MaxLineLen)
            {
                Doc.EnsureStates(line);
                Syntax.Tokenize(Doc.Lang, s, Doc.States[line], kinds);
            }
            if (selColored)
            {
                int sc = line == s0.Line ? s0.Col : 0, ec = line == s1.Line ? s1.Col : s.Length;
                for (int i = sc; i < ec; i++) kinds[i] = Tk.SelText;
            }
            findHits.Clear();
            if (!string.IsNullOrEmpty(HiTerm)) Hits(s, HiTerm, HiCase, HiWord, findHits, false);
            wordHits.Clear();
            if (smart != null) Hits(s, smart, true, true, wordHits, true);
        }

        System.Runtime.CompilerServices.ConditionalWeakTable<string, List<int>> hitTable = new System.Runtime.CompilerServices.ConditionalWeakTable<string, List<int>>(),
            wordTable = new System.Runtime.CompilerServices.ConditionalWeakTable<string, List<int>>();
        string hitTerm, wordTerm;
        bool hitCase, hitWord;

        void Hits(string s, string term, bool mc, bool ww, List<int> into, bool smartWord)
        {
            if (s.Length < 2000) { Finder.AllIn(s, term, mc, ww, into); return; }
            System.Runtime.CompilerServices.ConditionalWeakTable<string, List<int>> table;
            if (smartWord)
            {
                if (term != wordTerm) { wordTable = new System.Runtime.CompilerServices.ConditionalWeakTable<string, List<int>>(); wordTerm = term; }
                table = wordTable;
            }
            else
            {
                if (term != hitTerm || mc != hitCase || ww != hitWord)
                {
                    hitTable = new System.Runtime.CompilerServices.ConditionalWeakTable<string, List<int>>();
                    hitTerm = term;
                    hitCase = mc;
                    hitWord = ww;
                }
                table = hitTable;
            }
            List<int> cached;
            if (!table.TryGetValue(s, out cached))
            {
                cached = new List<int>();
                Finder.AllIn(s, term, mc, ww, cached);
                try { table.Add(s, cached); } catch (ArgumentException) { }
            }
            into.AddRange(cached);
        }

        string SmartWord(bool hasSel, Pos s0, Pos s1)
        {
            if (!hasSel || s0.Line != s1.Line) return null;
            int len = s1.Col - s0.Col;
            if (len < 1 || len > 100) return null;
            string s = Doc.Lines[s0.Line];
            for (int i = s0.Col; i < s1.Col; i++) if (!Finder.IsWordChar(s[i])) return null;
            if (s0.Col > 0 && Finder.IsWordChar(s[s0.Col - 1])) return null;
            if (s1.Col < s.Length && Finder.IsWordChar(s[s1.Col])) return null;
            return s.Substring(s0.Col, len);
        }

        static bool IsBracket(char c) { return c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}'; }

        void UpdateBracketMatch()
        {
            if (bmDoc == Doc && bmVersion == Doc.Version && bmCaret == Doc.Caret) return;
            bmDoc = Doc;
            bmVersion = Doc.Version;
            bmCaret = Doc.Caret;
            bmHas = FindBracket(out bm1, out bm2);
        }

        bool FindBracket(out Pos p1, out Pos p2)
        {
            p1 = p2 = new Pos();
            Document d = Doc;
            Pos c = d.Caret;
            string s = d.Lines[c.Line];
            int idx = -1;
            if (c.Col > 0 && c.Col <= s.Length && IsBracket(s[c.Col - 1])) idx = c.Col - 1;
            else if (c.Col < s.Length && IsBracket(s[c.Col])) idx = c.Col;
            if (idx < 0) return false;
            char ch = s[idx], match;
            int dir;
            switch (ch)
            {
                case '(': match = ')'; dir = 1; break;
                case '[': match = ']'; dir = 1; break;
                case '{': match = '}'; dir = 1; break;
                case ')': match = '('; dir = -1; break;
                case ']': match = '['; dir = -1; break;
                default: match = '{'; dir = -1; break;
            }
            int depth = 0, line = c.Line, i = idx, budget = 300000;
            while (budget-- > 0)
            {
                string t = d.Lines[line];
                char x = t[i];
                if (x == ch) depth++;
                else if (x == match)
                {
                    depth--;
                    if (depth == 0) { p1 = new Pos(c.Line, idx); p2 = new Pos(line, i); return true; }
                }
                if (dir > 0)
                {
                    i++;
                    while (i >= d.Lines[line].Length) { line++; if (line >= d.Lines.Count) return false; i = 0; budget--; }
                }
                else
                {
                    i--;
                    while (i < 0) { line--; if (line < 0) return false; i = d.Lines[line].Length - 1; budget--; }
                }
            }
            return false;
        }

        int XAt(int i)
        {
            if (i <= winA) return rowX + cellBuf[0] * cw;
            if (i >= winB) return rowX + cellBuf[winB - winA] * cw;
            return rowX + cellBuf[i - winA] * cw;
        }

        static int LowerBound(int[] a, int len, int value)
        {
            int lo = 0, hi = len;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (a[mid] < value) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int W = ClientSize.Width, H = ClientSize.Height;
            g.FillRectangle(B(th.Back), 0, 0, W, H);
            if (Doc == null || W <= 0 || H <= 0) return;
            Document d = Doc;
            EnsureWraps();
            ClampScroll();
            int total = TotalRows;
            int viewH = ViewH;
            int rows = Math.Max(0, Math.Min((viewH + lineH - 1) / lineH, total - topRow));
            Pos s0, s1;
            bool hasSel = GetSel(out s0, out s1);
            UpdateBracketMatch();
            string smart = string.IsNullOrEmpty(HiTerm) ? SmartWord(hasSel, s0, s1) : null;
            int textLeft = TextLeft, textRight = TextRight;
            int xoff = textLeft - (wrap ? 0 : d.ScrollX);
            int caretLine = d.Caret.Line;
            bool focused = Focused;
            Color selColor = focused ? th.Sel : th.SelInactive;
            int hiLen = HiTerm == null ? 0 : HiTerm.Length;
            int smartLen = smart == null ? 0 : smart.Length;
            Rectangle clip = e.ClipRectangle;

            int[] rowLine = new int[rows], rowSeg = new int[rows];
            int line = 0, seg = 0;
            if (rows > 0) RowToLine(topRow, out line, out seg);
            int prepared = -1, baseCell = 0;

            g.SetClip(new Rectangle(gutterW, 0, Math.Max(0, textRight - gutterW), viewH));
            for (int r = 0; r < rows; r++)
            {
                int y = r * lineH;
                rowLine[r] = line;
                rowSeg[r] = seg;
                string s = d.Lines[line];
                int a = SegStart(line, seg), b = SegEnd(line, seg);
                if (line != prepared)
                {
                    PrepareLine(line, smart, hasSel, s0, s1);
                    prepared = line;
                    baseCell = a > 0 ? Cells.Of(s, a) : 0;
                }
                int segLen = b - a;
                int nextBase = 0;
                int v0 = a, v1 = b;
                rowX = xoff;
                if (!wrap && segLen > 4096)
                {
                    int minCell = Math.Max(0, d.ScrollX / cw - 4), maxCell = (d.ScrollX + textRight - textLeft) / cw + 4;
                    int cell, i;
                    if (Cells.Uniform(s)) { i = Math.Min(b, minCell); cell = i; }
                    else
                    {
                        cell = 0; i = 0;
                        while (i < b && cell < minCell) { cell += Cells.Adv(s[i], cell); i++; }
                    }
                    winA = i;
                    int need = 0, c2 = cell, j = i;
                    while (j < b && c2 <= maxCell) { c2 += Cells.Adv(s[j], c2); j++; need++; }
                    winB = j;
                    if (cellBuf.Length < need + 1) cellBuf = new int[Math.Max(need + 1, cellBuf.Length * 2)];
                    for (int k = winA; k < winB; k++) { cellBuf[k - winA] = cell; cell += Cells.Adv(s[k], cell); }
                    cellBuf[winB - winA] = cell;
                    v0 = winA; v1 = winB;
                }
                else
                {
                    if (cellBuf.Length < segLen + 1) cellBuf = new int[Math.Max(segLen + 1, cellBuf.Length * 2)];
                    int cell = baseCell;
                    for (int i = a; i < b; i++) { cellBuf[i - a] = cell - baseCell; cell += Cells.Adv(s[i], cell); }
                    cellBuf[segLen] = cell - baseCell;
                    nextBase = cell;
                    winA = a; winB = b;
                    if (!wrap && segLen > 0)
                    {
                        int minCell = d.ScrollX / cw - 2, maxCell = (d.ScrollX + textRight - textLeft) / cw + 2;
                        v0 = a + Math.Max(0, LowerBound(cellBuf, segLen + 1, minCell) - 1);
                        v1 = Math.Min(b, a + LowerBound(cellBuf, segLen + 1, maxCell) + 1);
                    }
                }

                if (y + lineH > clip.Top && y < clip.Bottom)
                {
                    if (line == caretLine && !th.HighContrast)
                        g.FillRectangle(B(th.CurLine), gutterW, y, textRight - gutterW, lineH);

                    if (hiLen > 0) DrawHits(g, findHits, hiLen, th.FindHi, th.FindBorder, Math.Max(a, v0), Math.Min(b, v1), y);
                    if (smartLen > 0) DrawHits(g, wordHits, smartLen, th.WordHi, Color.Empty, Math.Max(a, v0), Math.Min(b, v1), y);

                    if (hasSel && line >= s0.Line && line <= s1.Line)
                    {
                        int sc = line == s0.Line ? Math.Max(s0.Col, a) : a;
                        int ec = line == s1.Line ? Math.Min(s1.Col, b) : b;
                        bool eol = line < s1.Line && b == s.Length;
                        if (sc <= b && ec >= a && (sc < ec || (eol && sc <= ec)))
                        {
                            int x1 = XAt(sc), x2 = XAt(ec);
                            if (eol) x2 += Math.Max(UI.S(4), cw / 2);
                            g.FillRectangle(B(selColor), x1, y, x2 - x1, lineH);
                        }
                    }

                    if (bmHas)
                    {
                        DrawBracket(g, bm1, line, a, b, xoff, y);
                        DrawBracket(g, bm2, line, a, b, xoff, y);
                    }

                    DrawText(g, s, a, b, v0, v1, xoff, y);
                }

                baseCell = nextBase;
                if (wrap && seg < WrapsOf(line).Length) seg++;
                else
                {
                    line++;
                    seg = 0;
                    baseCell = 0;
                    if (line >= d.Lines.Count) { rows = r + 1; break; }
                }
            }
            g.ResetClip();
            if (!th.HighContrast && ClientSize.Width > textRight)
                for (int r = 0; r < rows; r++)
                    if (rowLine[r] == caretLine) g.FillRectangle(B(th.CurLine), textRight, r * lineH, ClientSize.Width - textRight, lineH);

            if (GutterVisible)
            {
                g.FillRectangle(B(th.Back), 0, 0, gutterW, viewH);
                int cbx = ChangeBarX, cbw = Math.Max(2, UI.S(3));
                for (int r = 0; r < rows; r++)
                {
                    int y = r * lineH;
                    if (y + lineH <= clip.Top || y >= clip.Bottom) continue;
                    int ln = rowLine[r];
                    if (rowSeg[r] == 0)
                    {
                        string num = (ln + 1).ToString();
                        int nx = UI.S(14) + numW - num.Length * cw;
                        TextRenderer.DrawText(g, num, font, new Point(nx, y + textY), ln == caretLine ? th.LineNumActive : th.LineNum, TF);
                    }
                    byte mk = d.Marks[ln];
                    if (mk != 0) g.FillRectangle(B(mk == 1 ? th.ModUnsaved : th.ModSaved), cbx, y, cbw, lineH);
                }
            }

            if (focused && (caretOn || blinkMs <= 0))
            {
                Point cp = CaretPoint();
                if (cp.X >= gutterW && cp.X < textRight && cp.Y > -lineH && cp.Y < viewH)
                    g.FillRectangle(B(th.Caret), cp.X, cp.Y, CaretW, lineH);
            }

            PaintScrollbars(g);
        }

        void DrawHits(Graphics g, List<int> hits, int len, Color fill, Color border, int a, int b, int y)
        {
            if (th.HighContrast) { fill = Color.Empty; border = th.Fore; }
            int lo = 0, hi = hits.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (hits[mid] + len <= a) lo = mid + 1; else hi = mid; }
            for (int k = lo; k < hits.Count; k++)
            {
                int h = hits[k];
                if (h >= b) break;
                int st = Math.Max(h, a), en = Math.Min(h + len, b);
                if (st >= en) continue;
                int x1 = XAt(st), x2 = XAt(en);
                if (!fill.IsEmpty) g.FillRectangle(B(fill), x1, y + 1, x2 - x1, lineH - 2);
                if (!border.IsEmpty)
                {
                    using (Pen p = new Pen(border)) g.DrawRectangle(p, x1, y + 1, x2 - x1 - 1, lineH - 3);
                }
            }
        }

        void DrawBracket(Graphics g, Pos p, int line, int a, int b, int xoff, int y)
        {
            if (p.Line != line || p.Col < a || p.Col >= b) return;
            int x1 = XAt(p.Col), x2 = XAt(p.Col + 1);
            if (!th.HighContrast) g.FillRectangle(B(th.BraceBack), x1, y + 1, x2 - x1, lineH - 2);
            using (Pen pen = new Pen(th.BraceBorder)) g.DrawRectangle(pen, x1, y + 1, x2 - x1 - 1, lineH - 3);
        }

        void DrawText(Graphics g, string s, int a, int b, int v0, int v1, int xoff, int y)
        {
            int ty = y + textY;
            int i = Math.Max(a, v0), end = Math.Min(b, v1);
            if (i >= end) return;
            while (i > a && i < s.Length && Cells.W(s[i]) == 0 && s[i] != '\t') i--;
            while (i < end)
            {
                char c = s[i];
                if (c == ' ' || c == '\t') { i++; continue; }
                byte k = plainLine ? Tk.Text : kinds[i];
                int j = i + 1;
                if (Cells.Batchable(c))
                {
                    while (j < end && (plainLine || kinds[j] == k) && s[j] != '\t' && Cells.Batchable(s[j])) j++;
                    int je = j;
                    while (je > i + 1 && s[je - 1] == ' ') je--;
                    TextRenderer.DrawText(g, s.Substring(i, je - i), font, new Point(XAt(i), ty), th.Syn[k], TF);
                }
                else
                {
                    if (char.IsHighSurrogate(c) && j < b && char.IsLowSurrogate(s[j])) j++;
                    while (j < b && s[j] != '\t' && Cells.W(s[j]) == 0) j++;
                    if (Cells.W(c) > 0)
                        TextRenderer.DrawText(g, s.Substring(i, j - i), font, new Point(XAt(i), ty), th.Syn[k], TF);
                }
                i = j;
            }
        }

        static void FillRound(Graphics g, Brush br, Rectangle r, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            if (radius < 2) { g.FillRectangle(br, r); return; }
            using (GraphicsPath p = new GraphicsPath())
            {
                int dd = radius * 2;
                p.AddArc(r.Left, r.Top, dd, dd, 180, 90);
                p.AddArc(r.Right - dd, r.Top, dd, dd, 270, 90);
                p.AddArc(r.Right - dd, r.Bottom - dd, dd, dd, 0, 90);
                p.AddArc(r.Left, r.Bottom - dd, dd, dd, 90, 90);
                p.CloseFigure();
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(br, p);
                g.SmoothingMode = old;
            }
        }

        void PaintScrollbars(Graphics g)
        {
            int inset = UI.S(3);
            if (VBarActive)
            {
                Rectangle t = VTrack();
                Rectangle thb = VThumb();
                FillRound(g, B(vDrag || vHover ? th.ThumbHover : th.Thumb),
                    new Rectangle(thb.Left + inset, thb.Top + 1, thb.Width - inset * 2, thb.Height - 2), UI.S(4));
                PaintOverview(g, t);
            }
            if (HBarVisible)
            {
                Rectangle thb = HThumb();
                FillRound(g, B(hDrag || hHover ? th.ThumbHover : th.Thumb),
                    new Rectangle(thb.Left + 1, thb.Top + inset, thb.Width - 2, thb.Height - inset * 2), UI.S(4));
            }
        }

        void PaintOverview(Graphics g, Rectangle t)
        {
            int total = TotalRows;
            if (total <= 0 || t.Height <= 0) return;
            double scale = (double)t.Height / total;
            int h = Math.Max(2, (int)Math.Ceiling(scale));
            int w = Math.Max(2, UI.S(3));
            int lastY = int.MinValue;
            byte lastM = 0;
            Document d = Doc;
            for (int l = 0; l < d.Marks.Count; l++)
            {
                byte m = d.Marks[l];
                if (m == 0) continue;
                int y = t.Top + (int)(LineToRow(l) * scale);
                if (y == lastY && m == lastM) continue;
                lastY = y;
                lastM = m;
                g.FillRectangle(B(m == 1 ? th.ModUnsaved : th.ModSaved), t.Left, y, w, h);
            }
            if (FindLines != null && FindLines.Count > 0)
            {
                Color fc = th.FindMark.IsEmpty ? th.Fore : th.FindMark;
                lastY = int.MinValue;
                foreach (int l in FindLines)
                {
                    if (l >= d.Lines.Count) continue;
                    int y = t.Top + (int)(LineToRow(l) * scale);
                    if (y == lastY) continue;
                    lastY = y;
                    g.FillRectangle(B(fc), t.Right - w, y, w, h);
                }
            }
        }
    }
}
