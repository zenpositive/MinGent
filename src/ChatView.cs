using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MinGent
{
    enum ChatKind { User, Assistant, Thinking, Tool, Permission, Notice, Error, Footer, Activity, Question }

    class ChatItem
    {
        public ChatKind Kind;
        public string Text = "";
        public bool Streaming;
        public bool Expanded, Clipped;
        public int Version;
        public string CallId = "", Label = "", Summary = "", Detail = "", Live = "", Icon = "dot", Report = "";
        public int Status;
        public DateTime Started = DateTime.UtcNow;
        public double Seconds;
        public PermissionAsk Ask;
        public int Decision = -1;
        public Action<PermissionDecision> Decide;
        public UserQuestion Question;
        public Action<UserAnswer> Answered;
        public int Turn;
        public int Level;
        public int RunId, InboxId;
        public RunChangeLog Changes;
        public bool Undone;
        public string UndoneText, UndoKey;
        public string ActionLabel;
        public Action Action;
        public List<Attachment> Attachments;
        public List<Rectangle> Tiles;
        public int TrayH;
        public string ContextLabel;
        public bool Queued, MidRun, Running, Stopped, SpeedGuess, Draft, Surfaced, Whole;
        public double Speed, FirstToken;
        public int Cached = -1;
        public string Phase = "", CurrentTask = "", Note, LastDoing;
        public int DoingSeq, TaskSeq;
        public ChatItem Group, Under, LastKid;
        public int Kids;
        public bool Hidden;
        public List<ChatItem> Members;
        public int LayoutW = -1, LayoutVersion = -1, H;
        public DateTime LastLayout;
        public double LayoutCostMs;
        public MdLayout Md;
        public int MdOx = -1, MdOy = -1, BubbleOff, BubbleW;
        public List<KeyValuePair<Rectangle, int>> Buttons = new List<KeyValuePair<Rectangle, int>>();
        public List<string> DetailLines;
        public int Top, AnswerY;

        public void Touch() { Version++; }
    }

    class ChatView : Control
    {
        struct SelPos
        {
            public int Item, Op, Ch;
            public SelPos(int item, int op, int ch) { Item = item; Op = op; Ch = ch; }
        }

        struct Hit
        {
            public int Item, Button, Copy, Sug, Act;
            public string Link, LinkText;
            public bool Jump, Bar;
        }

        public readonly List<ChatItem> Items = new List<ChatItem>();
        Theme th = Theme.Light(Color.SteelBlue);
        ChatFonts fonts;
        Font titleFont;
        float fontSize = 10f;
        string monoName = "Consolas";
        int scrollY, contentH;
        bool stick = true;
        readonly Timer anim = new Timer();
        readonly Timer blinkTimer = new Timer();
        readonly Timer dragScroll = new Timer();
        bool layoutPending, emptyBlink;
        int phase;
        int hoverItem = -1, hoverButton = -1, hoverCopy = -1, copiedBlock = -1, copiedItem = -1, copiedMsg = -1, hoverSuggestion = -1, hoverAction = -1;
        string hoverLink;
        bool sbHover, sbDrag;
        int dragOff;
        readonly ThemedTip tip = new ThemedTip();
        string tipLink;
        public readonly List<KeyValuePair<string, string>> Suggestions = new List<KeyValuePair<string, string>>();
        public readonly List<KeyValuePair<string, Action>> EmptyActions = new List<KeyValuePair<string, Action>>();
        public string EmptyTitle = "", EmptyText = "";
        public Rectangle EmptyAnchor = Rectangle.Empty;
        public int MaxContent = 760;
        readonly List<Rectangle> suggestionRects = new List<Rectangle>();
        readonly List<Rectangle> actionRects = new List<Rectangle>();
        Rectangle faceRect, jumpRect;
        bool jumpHover;
        Hit pressHit;
        bool pressed;
        SelPos selA, selB, anchor;
        bool hasSel, selecting, maySelect;
        Point downPt;

        public event Action<string> LinkClicked;
        public event Action<string> SuggestionClicked;
        public event Action<char> TypeAhead;
        public event Action<float> FontsChanged;
        public event EventHandler EmptyChanged;
        bool wasEmpty = true;

        const int BtnAllow = 1, BtnAlways = 2, BtnDeny = 3, BtnExpand = 10, BtnAction = 11, BtnCopyMsg = 12, BtnUndo = 13, BtnOption = 100;
        const int BtnUserCopy = 20, BtnUserEdit = 21, BtnUserDelete = 22, BtnRetry = 23, BtnEditCancel = 24, BtnEditSend = 25, BtnQueueEdit = 26, BtnQueueRemove = 27, BtnAnswer = 30, BtnAnswerSend = 31, BtnAttach = 600;

        ChatItem editing;
        readonly CueTextBox editBox = new CueTextBox();
        readonly CueTextBox answerBox = new CueTextBox();
        ChatItem answering;
        int answerLines = 1;
        int editLines = 1;
        public Func<ChatItem, string, bool> EditSubmit;
        public Func<bool> CanChange;
        public event Action<ChatItem> DeleteRequested, RetryRequested, QueueEditRequested, QueueRemoveRequested, EditCancelled;
        public event Action<ChatItem, Attachment> AttachmentClicked;
        public event EventHandler EditEnded;
        public const string QueueRunning = "Queued: the agent reads this at its next step", QueueHeld = "Queued: sends when you finish editing",
            QueueBehind = "Queued: waits for your edit above", QueueCompacting = "Queued: sends when compacting finishes";

        public ChatView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleName = "Conversation";
            AccessibleRole = AccessibleRole.List;
            anim.Interval = 90;
            anim.Tick += delegate
            {
                phase++;
                if (layoutPending) { layoutPending = false; Relayout(); }
                if (AnyRunning()) InvalidateRunning(); else anim.Stop();
            };
            blinkTimer.Interval = 4200;
            blinkTimer.Tick += delegate
            {
                if (Items.Count > 0 || !Visible) { blinkTimer.Stop(); return; }
                emptyBlink = !emptyBlink;
                blinkTimer.Interval = emptyBlink ? 150 : 3600 + (phase++ % 5) * 400;
                if (!faceRect.IsEmpty) Invalidate(faceRect);
            };
            dragScroll.Interval = 50;
            dragScroll.Tick += delegate
            {
                if (!selecting) { dragScroll.Stop(); return; }
                if ((Control.MouseButtons & MouseButtons.Left) == 0) { selecting = false; dragScroll.Stop(); return; }
                Point p = PointToClient(Cursor.Position);
                int dy = p.Y < 0 ? p.Y : p.Y > ClientSize.Height ? p.Y - ClientSize.Height : 0;
                if (dy == 0) return;
                ScrollBy(Math.Sign(dy) * Math.Min(UI.S(60), Math.Abs(dy) / 2 + UI.S(6)));
                ExtendSelection(p);
            };
            fonts = new ChatFonts(fontSize, monoName);
            MakeTitleFont();
            MaxContent = UI.S(760);
            editBox.Multiline = true;
            editBox.AcceptsReturn = true;
            editBox.WordWrap = true;
            editBox.BorderStyle = BorderStyle.None;
            editBox.ScrollBars = ScrollBars.None;
            editBox.Visible = false;
            editBox.Font = fonts.Body;
            editBox.AccessibleName = "Edit your message";
            editBox.KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift && !e.Control && !e.Alt) { e.Handled = e.SuppressKeyPress = true; SubmitEdit(); }
                else if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; CancelEdit(); }
                else if (e.KeyCode == Keys.A && e.Modifiers == Keys.Control) { e.Handled = e.SuppressKeyPress = true; editBox.SelectAll(); }
            };
            editBox.TextChanged += delegate
            {
                if (editing == null) return;
                int n = EditLines(ContentW - UI.S(28));
                if (n == editLines) return;
                editLines = n;
                editing.Touch();
                Relayout();
            };
            Controls.Add(editBox);
            answerBox.Multiline = true;
            answerBox.AcceptsReturn = true;
            answerBox.WordWrap = true;
            answerBox.BorderStyle = BorderStyle.None;
            answerBox.ScrollBars = ScrollBars.None;
            answerBox.Visible = false;
            answerBox.Font = fonts.Body;
            answerBox.AccessibleName = "Your own answer";
            answerBox.IdleCaret = true;
            answerBox.KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift && !e.Control && !e.Alt) { e.Handled = e.SuppressKeyPress = true; SubmitAnswer(); }
                else if (e.KeyCode == Keys.Escape && answerBox.TextLength > 0) { e.Handled = e.SuppressKeyPress = true; answerBox.Text = ""; }
                else if (e.KeyCode == Keys.A && e.Modifiers == Keys.Control) { e.Handled = e.SuppressKeyPress = true; answerBox.SelectAll(); }
            };
            answerBox.TextChanged += delegate
            {
                if (answering == null) return;
                int n = AnswerLines(AnswerBoxW(answering));
                InvalidateItem(answering);
                if (n == answerLines) return;
                answerLines = n;
                answering.Touch();
                Relayout();
            };
            answerBox.GotFocus += delegate { if (answering != null) InvalidateItem(answering); };
            answerBox.LostFocus += delegate { if (answering != null) InvalidateItem(answering); };
            Controls.Add(answerBox);
        }

        int AnswerBoxW(ChatItem it) { return Math.Max(UI.S(40), ContentW - Depth(it) * Indent - UI.S(28) - UI.S(40) - UI.S(44)); }

        int AnswerLines(int width)
        {
            string t = answerBox.Text;
            if (t.Length == 0 || width <= 0) return 1;
            Size sz = TextRenderer.MeasureText(t.EndsWith("\n") ? t + " " : t, answerBox.Font, new Size(Math.Max(10, width - UI.S(4)), 100000), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            return Math.Max(1, Math.Min(8, (int)Math.Ceiling(sz.Height / (double)Math.Max(1, answerBox.Font.Height))));
        }

        int AnswerRowH { get { return Math.Max(UI.S(40), answerLines * answerBox.Font.Height + UI.S(18)); } }

        static bool WritesIn(ChatItem it)
        {
            return it != null && it.Kind == ChatKind.Question && it.Question != null && !it.Question.Wait && it.Question.FreeText && it.Decision < 0;
        }

        void SubmitAnswer()
        {
            ChatItem it = answering;
            if (!WritesIn(it)) return;
            string t = answerBox.Text.Replace("\r\n", "\n").Trim();
            if (t.Length == 0) { answerBox.Focus(); return; }
            bool focused = answerBox.Focused;
            UserAnswer a = new UserAnswer();
            UserQuestion q = it.Question;
            int n;
            if (q != null && !q.Wait)
            {
                for (int i = 0; i < q.Options.Count && a.Choice < 0; i++)
                    if (string.Equals(q.Options[i].Trim(), t, StringComparison.OrdinalIgnoreCase)) a.Choice = i;
                if (a.Choice < 0 && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 1 && n <= q.Options.Count) a.Choice = n - 1;
                if (a.Choice < 0 && q.PlanApproval) a.Choice = PlanReply.Choice(q.Options, t);
            }
            if (a.Choice < 0) a.Text = t;
            answerBox.Text = "";
            AnswerQuestion(it, a);
            if (focused && EditEnded != null) EditEnded(this, EventArgs.Empty);
        }

        void PositionAnswer()
        {
            ChatItem pq = PendingQuestion();
            if (!WritesIn(pq) || pq.Hidden) pq = null;
            if (pq != answering)
            {
                bool had = answerBox.Focused;
                answering = pq;
                answerBox.Text = "";
                answerLines = 1;
                if (pq != null) answerBox.Cue = pq.Question.PlanApproval ? "Or type what to change in the plan" : pq.Question.Options.Count > 0 ? "Type a different answer" : "Type your answer";
                if (pq == null && had && EditEnded != null) EditEnded(this, EventArgs.Empty);
            }
            if (answering == null || answering.LayoutVersion < 0 || dropHint) { if (answerBox.Visible) answerBox.Visible = false; return; }
            int x = ItemX(answering) + UI.S(14) + UI.S(40);
            int y = answering.Top - scrollY + answering.AnswerY + (AnswerRowH - answerLines * answerBox.Font.Height) / 2;
            Rectangle r = new Rectangle(x, y, AnswerBoxW(answering), answerLines * answerBox.Font.Height);
            if (answerBox.Bounds != r) answerBox.Bounds = r;
            if (!answerBox.Visible) answerBox.Visible = true;
        }

        int UserRowH { get { return UI.S(28); } }

        static bool UserActions(ChatItem it) { return it.Kind == ChatKind.User && !it.MidRun && !it.Queued; }

        bool Changeable { get { return CanChange == null || CanChange(); } }

        int EditLines(int width)
        {
            string t = editBox.Text;
            if (t.Length == 0 || width <= 0) return 1;
            Size sz = TextRenderer.MeasureText(t.EndsWith("\n") ? t + " " : t, editBox.Font, new Size(Math.Max(10, width - UI.S(4)), 100000), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            return Math.Max(1, (int)Math.Ceiling(sz.Height / (double)Math.Max(1, editBox.Font.Height)));
        }

        public bool Editing { get { return editing != null; } }
        public ChatItem EditingItem { get { return editing; } }
        public string EditText { get { return editing == null ? "" : editBox.Text.Replace("\r\n", "\n").Trim(); } }

        public ChatItem LastUserItem()
        {
            for (int i = Items.Count - 1; i >= 0; i--) if (UserActions(Items[i])) return Items[i];
            return null;
        }

        public bool BeginEdit(ChatItem it)
        {
            if (it == null || !UserActions(it) || !Items.Contains(it) || !Changeable) return false;
            return StartEdit(it);
        }

        public bool BeginQueuedEdit(ChatItem it)
        {
            if (it == null || it.Kind != ChatKind.User || !it.Queued || !Items.Contains(it)) return false;
            return StartEdit(it);
        }

        bool StartEdit(ChatItem it)
        {
            if (editing == it) { Reveal(it); editBox.Focus(); return true; }
            if (editing != null)
            {
                ChatItem prev = editing;
                string t = EditText;
                if (prev.Queued && t.Length > 0 && t != (prev.Text ?? "").Trim() && EditSubmit != null && EditSubmit(prev, t) && editing == prev) EndEdit(false);
                else CancelEdit();
            }
            editing = it;
            ClearSelection();
            editBox.Text = (it.Text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            editLines = EditLines(ContentW - UI.S(28));
            it.Touch();
            stick = false;
            Relayout();
            Reveal(it);
            editBox.Focus();
            editBox.SelectionStart = editBox.TextLength;
            return true;
        }

        public void CancelEdit() { EndEdit(true); }

        void EndEdit(bool cancelled)
        {
            if (editing == null) return;
            ChatItem it = editing;
            editing = null;
            bool focused = editBox.Focused;
            it.Touch();
            Relayout();
            editBox.Visible = false;
            if (cancelled && EditCancelled != null) EditCancelled(it);
            if (focused && EditEnded != null) EditEnded(this, EventArgs.Empty);
        }

        void SubmitEdit()
        {
            if (editing == null) return;
            string t = editBox.Text.Replace("\r\n", "\n").Trim();
            ChatItem it = editing;
            bool files = it.Attachments != null && it.Attachments.Count > 0;
            if (t.Length == 0 && !files && it.Queued && QueueRemoveRequested != null) { EndEdit(false); QueueRemoveRequested(it); return; }
            if (t.Length == 0 && !files) { editBox.Focus(); return; }
            if (EditSubmit != null && EditSubmit(it, t) && editing == it) EndEdit(false);
        }

        public void Remove(ChatItem it)
        {
            if (it == null) return;
            if (editing == it) CancelEdit();
            if (!Items.Remove(it)) return;
            Relayout();
            RefreshHover();
        }

        public void FocusEditor()
        {
            if (editing == null) return;
            Reveal(editing);
            if (editBox.Visible) editBox.Focus();
        }

        void Reveal(ChatItem it)
        {
            int view = ClientSize.Height;
            int max = Math.Max(0, contentH - view);
            if (it.Top < scrollY + UI.S(8)) scrollY = Math.Max(0, it.Top - UI.S(16));
            else if (it.Top + it.H > scrollY + view - UI.S(8)) scrollY = Math.Min(max, it.Top + it.H - view + UI.S(16));
            stick = scrollY >= max - UI.S(4);
            PositionEditor();
            Invalidate();
        }

        void PositionEditor()
        {
            PositionAnswer();
            if (editing == null || dropHint) { if (editBox.Visible) editBox.Visible = false; return; }
            if (!Items.Contains(editing) || editing.Hidden) { CancelEdit(); return; }
            int x = ItemX(editing) + UI.S(14);
            int y = editing.Top - scrollY + editing.TrayH + UI.S(12);
            int w = Math.Max(UI.S(40), ContentW - UI.S(28));
            int h = Math.Max(editBox.Font.Height, editing.H - editing.TrayH - UI.S(2) - UI.S(12) - UI.S(12) - UI.S(32) - UI.S(12));
            Rectangle r = new Rectangle(x, y, w, h);
            if (editBox.Bounds != r) editBox.Bounds = r;
            if (!editBox.Visible) editBox.Visible = true;
        }

        void MakeTitleFont()
        {
            if (titleFont != null) titleFont.Dispose();
            titleFont = new Font(fonts.Body.FontFamily, fontSize * 1.75f, FontStyle.Regular);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                anim.Dispose(); blinkTimer.Dispose(); dragScroll.Dispose(); tip.Dispose();
                if (fonts != null) fonts.Dispose();
                if (titleFont != null) titleFont.Dispose();
                foreach (Image img in thumbs.Values) if (img != null) img.Dispose();
            }
            base.Dispose(disposing);
        }

        public void SetTheme(Theme t)
        {
            th = t;
            tip.Th = t;
            BackColor = t.Back;
            editBox.BackColor = t.InputBack;
            editBox.ForeColor = t.Fore;
            editBox.CueColor = t.ChromeDim;
            editBox.MenuTheme = t;
            answerBox.BackColor = CardBack;
            answerBox.ForeColor = t.Fore;
            answerBox.CueColor = t.ChromeDim;
            answerBox.MenuTheme = t;
            foreach (ChatItem it in Items) it.LayoutVersion = -1;
            Relayout();
        }

        public void SetFonts(float size, string mono)
        {
            if (Math.Abs(size - fontSize) < 0.01 && mono == monoName) return;
            fontSize = Math.Max(7f, Math.Min(20f, size));
            monoName = mono;
            ChatFonts old = fonts;
            fonts = new ChatFonts(fontSize, monoName);
            editBox.Font = fonts.Body;
            answerBox.Font = fonts.Body;
            if (old != null) old.Dispose();
            MakeTitleFont();
            foreach (ChatItem it in Items) it.LayoutVersion = -1;
            ClearSelection();
            Relayout();
        }

        public float FontSize { get { return fontSize; } }

        int Pad { get { return UI.S(16); } }
        int ContentW { get { return Math.Max(UI.S(80), Math.Min(MaxContent, ClientSize.Width - Pad * 2 - UI.S(6))); } }
        int ContentX { get { return Math.Max(Pad, (ClientSize.Width - ContentW) / 2); } }
        int Indent { get { return UI.S(18); } }
        int ActionRowH { get { return UI.S(32); } }

        static int Depth(ChatItem it) { return (it.Group != null ? 1 : 0) + (it.Under != null ? 1 : 0); }

        int ItemX(ChatItem it) { return ContentX + Depth(it) * Indent; }

        static bool KidsShown(ChatItem card) { return card.Status == 0 || card.Expanded; }

        static bool Expandable(ChatItem it)
        {
            if (it.Status == 0) return false;
            return it.Detail.Length > 0 || it.Report.Length > 0 || it.Kids > 0 || (it.Icon != "branch" && it.Live.Length > 0);
        }

        bool AnyRunning()
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                ChatItem it = Items[i];
                if ((it.Kind == ChatKind.Tool && it.Status == 0) || it.Streaming || ((it.Kind == ChatKind.Permission || it.Kind == ChatKind.Question) && it.Decision < 0) || (it.Kind == ChatKind.Activity && it.Running)) return true;
            }
            return false;
        }

        void InvalidateRunning()
        {
            for (int i = 0; i < Items.Count; i++)
            {
                ChatItem it = Items[i];
                if (it.Hidden) continue;
                bool live = (it.Kind == ChatKind.Tool && it.Status == 0) || (it.Streaming && (it.Kind == ChatKind.Thinking || it.Kind == ChatKind.Assistant)) || (it.Kind == ChatKind.Activity && it.Running) ||
                            (it.Kind == ChatKind.Question && it.Decision < 0);
                if (!live) continue;
                int y = it.Top - scrollY;
                if (it.Kind == ChatKind.Question) { Invalidate(new Rectangle(0, y, Width, UI.S(52))); continue; }
                if (y + it.H >= 0 && y <= Height) Invalidate(new Rectangle(0, y, Width, Math.Min(it.H, UI.S(44))));
                if (it.Kind == ChatKind.Assistant) Invalidate(new Rectangle(0, y + it.H - UI.S(30), Width, UI.S(36)));
            }
        }

        public void InvalidateItem(ChatItem it)
        {
            if (it == null || it.Hidden) return;
            Invalidate(new Rectangle(0, it.Top - scrollY, Width, it.H + UI.S(4)));
            if (!anim.Enabled && AnyRunning()) anim.Start();
        }

        public ChatItem Add(ChatItem it)
        {
            int at = Items.Count;
            if (!(it.Kind == ChatKind.User && it.Queued))
                while (at > 0 && Items[at - 1].Kind == ChatKind.User && Items[at - 1].Queued) at--;
            Items.Insert(at, it);
            blinkTimer.Stop();
            Relayout();
            if (!anim.Enabled && AnyRunning()) anim.Start();
            return it;
        }

        public void AddUnder(ChatItem card, ChatItem it)
        {
            int at = card != null ? Items.IndexOf(card) : -1;
            if (at < 0) { Add(it); return; }
            it.Under = card;
            at++;
            while (at < Items.Count && Items[at].Under == card) at++;
            Items.Insert(at, it);
            Relayout();
            if (!anim.Enabled && AnyRunning()) anim.Start();
        }

        public void InsertBefore(ChatItem it, ChatItem anchor)
        {
            int at = anchor != null ? Items.IndexOf(anchor) : -1;
            if (at < 0) { Add(it); return; }
            Items.Insert(at, it);
            Relayout();
            if (!anim.Enabled && AnyRunning()) anim.Start();
        }

        public void Changed(ChatItem it)
        {
            it.Touch();
            Relayout();
            if (!anim.Enabled && AnyRunning()) anim.Start();
        }

        public void Clear()
        {
            if (editing != null) { editing = null; editBox.Visible = false; }
            Items.Clear();
            foreach (Image img in thumbs.Values) if (img != null) img.Dispose();
            thumbs.Clear();
            scrollY = 0;
            stick = true;
            ClearSelection();
            Relayout();
            StartBlink();
        }

        void StartBlink()
        {
            if (Items.Count == 0 && Visible && !blinkTimer.Enabled) { emptyBlink = false; blinkTimer.Interval = 3000; blinkTimer.Start(); }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) StartBlink(); else blinkTimer.Stop();
        }

        public ChatItem FindTool(string callId)
        {
            for (int i = Items.Count - 1; i >= 0; i--) if (Items[i].Kind == ChatKind.Tool && Items[i].CallId == callId) return Items[i];
            return null;
        }

        public void ScrollToEnd()
        {
            stick = true;
            scrollY = Math.Max(0, contentH - ClientSize.Height);
            PositionEditor();
            Invalidate();
            RefreshHover();
        }

        void ComputeGroups()
        {
            foreach (ChatItem it in Items) { it.Group = null; it.Hidden = false; it.Kids = 0; it.LastKid = null; if (it.Kind == ChatKind.Activity && it.Members != null) it.Members.Clear(); }
            foreach (ChatItem it in Items) if (it.Under != null) { it.Under.Kids++; it.Under.LastKid = it; }
            int n = Items.Count, s = 0;
            while (s < n)
            {
                int k = s;
                if (Items[k].Kind == ChatKind.User && !Items[k].MidRun) k++;
                while (k < n && !(Items[k].Kind == ChatKind.User && !Items[k].MidRun)) k++;
                ChatItem carry = null;
                int a = s;
                while (a < k)
                {
                    int b = a + 1;
                    while (b < k && !SegmentStart(b)) b++;
                    carry = GroupTurn(a, b, carry);
                    a = b;
                }
                s = k;
            }
            foreach (ChatItem it in Items)
            {
                if (it.Kind == ChatKind.Activity && it.Label.Length == 0) it.Hidden = (it.Members == null || it.Members.Count == 0) && !it.Running;
                if (it.Under != null && (it.Under.Hidden || !KidsShown(it.Under))) it.Hidden = true;
            }
        }

        bool SegmentStart(int i)
        {
            ChatItem it = Items[i];
            if (it.Kind != ChatKind.User || !it.MidRun || it.Queued) return false;
            ChatItem prev = Items[i - 1];
            return !(prev.Kind == ChatKind.User && prev.MidRun && !prev.Queued);
        }

        ChatItem GroupTurn(int s, int e, ChatItem carry)
        {
            int a = -1;
            for (int i = s; i < e; i++) if (Items[i].Kind == ChatKind.Activity && Items[i].Label.Length == 0) { a = i; break; }
            ChatItem act = a >= 0 ? Items[a] : carry;
            if (act == null) return null;
            if (act.Members == null) act.Members = new List<ChatItem>();
            for (int i = a >= 0 ? a + 1 : s; i < e; i++)
            {
                ChatItem it = Items[i];
                if (it.Under != null) { it.Group = act; it.Hidden = !act.Expanded; continue; }
                if (Replaced(i, e)) { it.Hidden = true; continue; }
                bool said = it.Kind == ChatKind.Assistant && !it.Draft && it.Text.Trim().Length > 0;
                if (said && !it.Surfaced && (Substantial(it.Text) || Asks(i, e))) it.Surfaced = true;
                if (said && Answer(i, e) && (it.Surfaced || !act.Running)) continue;
                bool pinned = (it.Kind == ChatKind.Question && it.Decision >= 0 && (it.Question == null || !it.Question.Wait)) || (said && it.Surfaced);
                bool member = it.Kind == ChatKind.Tool || it.Kind == ChatKind.Thinking || it.Kind == ChatKind.Assistant || (it.Kind == ChatKind.Permission && it.Decision >= 0) ||
                              (it.Kind == ChatKind.Question && it.Decision >= 0) || pinned || (it.Kind == ChatKind.Notice && it.Draft);
                if (!member) continue;
                it.Group = act;
                it.Hidden = !act.Expanded && !pinned;
                act.Members.Add(it);
            }
            return act;
        }

        bool Replaced(int i, int e)
        {
            ChatItem it = Items[i];
            if (it.Kind != ChatKind.Assistant || !it.Draft || !it.Whole || it.Streaming) return false;
            for (int j = i + 1; j < e; j++)
                if (Items[j].Kind == ChatKind.Assistant && Items[j].Under == null && Items[j].Whole && Items[j].Text.Trim().Length > 0 && !StatusWords.StatusOnly(Items[j].Text)) return true;
            return false;
        }

        public static string SpeedText(ChatItem it)
        {
            string rate = it.Speed < 10 ? it.Speed.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(it.Speed).ToString("0", CultureInfo.InvariantCulture);
            string s = (it.SpeedGuess ? "about " : "") + rate + " tokens/s";
            if (it.FirstToken >= 0.05) s += "  \u00B7  first token in " + (it.FirstToken < 10 ? it.FirstToken.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(it.FirstToken).ToString("0", CultureInfo.InvariantCulture)) + " s";
            if (it.Cached >= 0) s += "  \u00B7  " + it.Cached.ToString(CultureInfo.InvariantCulture) + "% of input cached";
            return s;
        }

        static readonly System.Text.RegularExpressions.Regex Structured = new System.Text.RegularExpressions.Regex(@"(^|\n)[^\S\n]*(#{1,6}\s|[-*]\s|\d+[.)]\s|\||```)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        public static bool Substantial(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length >= 400) return true;
            if (t.Length >= 40 && Structured.IsMatch(t)) return true;
            if (t.Length < 120) return false;
            return System.Text.RegularExpressions.Regex.Matches(t, @"[.!?](\s|$)").Count >= 3;
        }

        bool Asks(int i, int e)
        {
            for (int j = i + 1; j < e; j++)
            {
                ChatItem n = Items[j];
                if (n.Kind == ChatKind.Notice) continue;
                return n.Kind == ChatKind.Question && n.Decision < 0;
            }
            return false;
        }

        bool Answer(int i, int e)
        {
            for (int j = i + 1; j < e; j++)
            {
                ChatKind k = Items[j].Kind;
                if (k == ChatKind.Tool || k == ChatKind.Thinking) return false;
                if (k == ChatKind.User) return true;
            }
            return true;
        }

        void Relayout()
        {
            bool empty = Items.Count == 0;
            if (empty != wasEmpty)
            {
                wasEmpty = empty;
                if (EmptyChanged != null) EmptyChanged(this, EventArgs.Empty);
            }
            if (!IsHandleCreated || ClientSize.Width <= 0) { Invalidate(); return; }
            ComputeGroups();
            using (Graphics g = CreateGraphics())
            {
                int y = UI.S(16);
                int w = ContentW;
                ChatItem prev = null;
                for (int i = 0; i < Items.Count; i++)
                {
                    ChatItem it = Items[i];
                    if (it.Hidden) { it.Top = y; continue; }
                    int iw = w - Depth(it) * Indent;
                    if (it.LayoutW != iw || it.LayoutVersion != it.Version)
                    {
                        if (it.Streaming && it.Kind == ChatKind.Assistant && it.LayoutW == iw && it.Md != null && it.LayoutVersion >= 0 &&
                            (DateTime.UtcNow - it.LastLayout).TotalMilliseconds < Math.Max(60, it.LayoutCostMs * 4))
                        {
                            layoutPending = true;
                            if (!anim.Enabled) anim.Start();
                        }
                        else
                        {
                            DateTime t0 = DateTime.UtcNow;
                            Measure(g, it, iw);
                            it.LastLayout = DateTime.UtcNow;
                            it.LayoutCostMs = (it.LastLayout - t0).TotalMilliseconds;
                        }
                    }
                    if (prev != null) y += Gap(prev, it);
                    it.Top = y;
                    y += it.H;
                    prev = it;
                }
                contentH = y + UI.S(24);
            }
            int max = Math.Max(0, contentH - ClientSize.Height);
            if (stick) scrollY = max;
            scrollY = Math.Max(0, Math.Min(scrollY, max));
            if (hasSel && (selA.Item >= Items.Count || selB.Item >= Items.Count)) ClearSelection();
            PositionEditor();
            Invalidate();
        }

        static bool Row(ChatItem it)
        {
            if (it.Kind == ChatKind.Tool || it.Kind == ChatKind.Thinking || it.Kind == ChatKind.Notice) return true;
            return (it.Kind == ChatKind.Permission || it.Kind == ChatKind.Question) && it.Decision >= 0;
        }

        int Gap(ChatItem a, ChatItem b)
        {
            if (UserActions(a) && !(b.Kind == ChatKind.User && !b.MidRun)) return UI.S(2);
            bool aSmall = Row(a) || a.Kind == ChatKind.Activity;
            bool bSmall = Row(b);
            if (b.Kind == ChatKind.User && !b.MidRun) return UI.S(26);
            if (b.Kind == ChatKind.Activity) return UI.S(10);
            if (a.Kind == ChatKind.Activity && b.Group == a) return UI.S(2);
            if (aSmall && bSmall) return UI.S(2);
            return UI.S(12);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Relayout();
            StartBlink();
        }

        bool LastAnswer(ChatItem it)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                ChatItem x = Items[i];
                if (x.Kind == ChatKind.Assistant && x.Group == null && !x.Hidden) return x == it;
                if (x.Kind == ChatKind.User && !x.MidRun) return false;
            }
            return false;
        }

        void Measure(Graphics g, ChatItem it, int w)
        {
            it.LayoutW = w;
            it.LayoutVersion = it.Version;
            it.Buttons.Clear();
            it.Clipped = false;
            ChatFonts f = fonts;
            switch (it.Kind)
            {
                case ChatKind.User:
                    {
                        int tray = MeasureTray(g, it, w);
                        if (editing == it)
                        {
                            editLines = EditLines(w - UI.S(28));
                            int lines = Math.Max(2, Math.Min(12, editLines));
                            it.Md = null;
                            it.BubbleOff = 0;
                            it.BubbleW = w;
                            it.H = tray + UI.S(12) + lines * editBox.Font.Height + UI.S(6) + UI.S(12) + UI.S(32) + UI.S(12) + UI.S(2);
                            break;
                        }
                        string text = it.Text.Length > 20000 ? it.Text.Substring(0, 20000) + "\n..." : it.Text;
                        int h;
                        if (tray > 0 && text.Trim().Length == 0 && string.IsNullOrEmpty(it.ContextLabel))
                        {
                            it.Md = null;
                            it.BubbleW = 0;
                            it.BubbleOff = w;
                            h = tray - TileGap;
                        }
                        else
                        {
                            int maxInner = Math.Max(UI.S(60), (int)(w * 0.82) - UI.S(28));
                            Size nat = TextRenderer.MeasureText(g, text.Length > 0 ? text : " ", f.Body, new Size(maxInner, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                            int inner = Math.Min(maxInner, nat.Width + UI.S(4));
                            if (!string.IsNullOrEmpty(it.ContextLabel)) inner = Math.Max(inner, Math.Min(maxInner, TextRenderer.MeasureText(g, it.ContextLabel, f.Small).Width + UI.S(22)));
                            inner = Math.Max(inner, UI.S(24));
                            List<MdBlock> blocks = new List<MdBlock>();
                            MdBlock b = new MdBlock();
                            b.Kind = MdKind.Para;
                            b.Runs.Add(new MdRun(text, 0, null));
                            blocks.Add(b);
                            it.Md = MdRenderer.Layout(g, blocks, inner, f, th, false);
                            it.BubbleW = inner + UI.S(28);
                            it.BubbleOff = w - it.BubbleW;
                            h = it.Md.Height + UI.S(22);
                            if (!string.IsNullOrEmpty(it.ContextLabel)) h += f.SmallLineH + UI.S(4);
                            int maxH = f.LineH * 14 + UI.S(22);
                            if (!it.Expanded && h > maxH + f.LineH * 3) { h = maxH + f.SmallLineH + UI.S(8); it.Clipped = true; }
                            h += tray;
                        }
                        if (it.Queued || UserActions(it)) h += UserRowH;
                        it.H = h;
                        break;
                    }
                case ChatKind.Assistant:
                    {
                        string text = it.Text;
                        if (text.Trim().Length == 0 && it.Streaming) { it.Md = new MdLayout(); it.Md.Height = f.LineH; it.H = f.LineH; break; }
                        it.Md = MdRenderer.Layout(g, Markdown.Parse(text), w, f, th, false);
                        it.H = it.Md.Height + (it.Streaming ? UI.S(4) : UI.S(2));
                        if (it.Group == null && !it.Streaming) it.H += ActionRowH;
                        break;
                    }
                case ChatKind.Activity:
                    it.Md = null;
                    it.H = f.LineH + UI.S(12);
                    break;
                case ChatKind.Thinking:
                    {
                        int h = f.LineH + UI.S(6);
                        if (it.Expanded && it.Text.Length > 0)
                        {
                            List<MdBlock> blocks = new List<MdBlock>();
                            string t = it.Text.Trim();
                            if (t.Length > 30000) t = "..." + t.Substring(t.Length - 30000);
                            foreach (string para in t.Split(new string[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                MdBlock b = new MdBlock();
                                b.Kind = MdKind.Para;
                                b.Runs.Add(new MdRun(para.Trim(), 0, null));
                                blocks.Add(b);
                            }
                            it.Md = MdRenderer.Layout(g, blocks, w - UI.S(26), f, th, true);
                            h += it.Md.Height + UI.S(8);
                        }
                        else it.Md = null;
                        it.H = h;
                        break;
                    }
                case ChatKind.Tool:
                    {
                        int h = f.LineH + UI.S(8);
                        it.DetailLines = null;
                        it.Md = null;
                        if (it.Status == 0 && it.Icon == "branch")
                        {
                            it.H = h;
                            break;
                        }
                        if (it.Status != 0 && it.Report.Length > 0)
                        {
                            if (it.Expanded)
                            {
                                List<string> log = new List<string>();
                                if (it.Kids == 0)
                                    foreach (string l in it.Detail.Replace("\r\n", "\n").Split('\n')) if (l.Trim().Length > 0 && log.Count < 30) log.Add(l.Length > 200 ? l.Substring(0, 200) + "..." : l);
                                if (log.Count > 0) { it.DetailLines = log; h += log.Count * f.SmallLineH + UI.S(10); }
                                it.Md = MdRenderer.Layout(g, Markdown.Parse(it.Report), Math.Max(UI.S(60), w - UI.S(48)), f, th, false);
                                h += it.Md.Height + UI.S(24);
                            }
                            it.H = h;
                            break;
                        }
                        string live = it.Icon == "branch" ? "" : it.Live;
                        string body = it.Status == 0 ? live : (it.Detail.Length > 0 ? it.Detail : (it.Expanded ? live : ""));
                        bool show = (it.Expanded || (it.Status == 0 && live.Length > 0)) && body.Trim().Length > 0;
                        if (show)
                        {
                            List<string> lines = new List<string>();
                            string[] raw = body.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                            int limit = it.Status == 0 ? 8 : 400;
                            int start = it.Status == 0 ? Math.Max(0, raw.Length - limit) : 0;
                            int avail = Math.Max(UI.S(60), w - UI.S(20) - UI.S(16));
                            for (int i = start; i < raw.Length && lines.Count < limit; i++)
                            {
                                string line = raw[i].Replace("\t", "    ");
                                if (line.Length > 2000) line = line.Substring(0, 2000) + "...";
                                if (it.Status == 0 || line.Length == 0) { lines.Add(line); continue; }
                                int guard = 0;
                                while (line.Length > 0 && lines.Count < limit && guard++ < 40)
                                {
                                    bool cont = guard > 1;
                                    int n = cont ? MdRenderer.Fit(g, "  " + line, f.Mono, avail, f) - 2 : MdRenderer.Fit(g, line, f.Mono, avail, f);
                                    if (n <= 0) n = 1;
                                    if (n < line.Length)
                                    {
                                        int sp = line.LastIndexOf(' ', n - 1, Math.Min(n, 40));
                                        if (sp > n / 2) n = sp + 1;
                                    }
                                    lines.Add((cont ? "  " : "") + line.Substring(0, n));
                                    line = line.Substring(n);
                                }
                            }
                            if (it.Status != 0 && raw.Length > 0 && lines.Count >= limit) lines.Add("... more lines (right-click to copy all)");
                            it.DetailLines = lines;
                            h += lines.Count * f.MonoLineH + UI.S(18);
                        }
                        it.H = h;
                        break;
                    }
                case ChatKind.Permission:
                    {
                        if (it.Decision >= 0) { it.H = f.LineH + UI.S(8); it.Md = null; break; }
                        int inner = w - UI.S(28);
                        int h = UI.S(14) + f.LineH + UI.S(6);
                        if (!string.IsNullOrEmpty(it.Ask.Summary))
                            h += TextRenderer.MeasureText(g, it.Ask.Summary, f.Body, new Size(inner, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + UI.S(6);
                        if (!string.IsNullOrEmpty(it.Ask.Reading))
                            h += TextRenderer.MeasureText(g, it.Ask.Reading, f.Body, new Size(inner, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + UI.S(6);
                        string detail = it.Ask.Detail ?? "";
                        if (detail.Length > 0)
                        {
                            it.Md = MdRenderer.PlainBox(g, detail, inner, f, it.Expanded ? 600 : 14);
                            h += it.Md.Height + UI.S(8);
                            if (it.Md.Hidden > 0 || it.Expanded) h += f.SmallLineH + UI.S(8);
                        }
                        else it.Md = null;
                        if (!string.IsNullOrEmpty(it.Ask.Where)) h += f.SmallLineH + UI.S(6);
                        if (!string.IsNullOrEmpty(it.Ask.Reason))
                        {
                            Size rs = TextRenderer.MeasureText(g, it.Ask.Reason, f.Small, new Size(inner, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                            h += rs.Height + UI.S(8);
                        }
                        h += PermissionButtonRows(g, it.Ask, inner) * UI.S(38) - UI.S(8) + UI.S(14);
                        it.H = h;
                        break;
                    }
                case ChatKind.Question:
                    {
                        if (it.Decision >= 0 || it.Question == null) { it.H = f.LineH + UI.S(8); it.Md = null; break; }
                        UserQuestion q = it.Question;
                        int inner = w - UI.S(28), textW = Math.Max(UI.S(60), inner - UI.S(30));
                        int h = UI.S(14) + Math.Max(UI.S(24), TextRenderer.MeasureText(g, QTitle(q), f.Bold, new Size(textW, 4000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);
                        if (q.Detail.Length > 0) h += UI.S(4) + TextRenderer.MeasureText(g, QDetail(q), f.Small, new Size(textW, 4000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                        h += UI.S(12);
                        if (q.Wait) h += QuestionButtonRows(g, q, inner) * UI.S(38) - UI.S(8);
                        else
                        {
                            for (int i = 0; i < q.Options.Count; i++) h += OptionRowH(g, q.Options[i], inner) + UI.S(6);
                            if (q.Options.Count > 0) h -= UI.S(6);
                            if (q.FreeText)
                            {
                                if (q.Options.Count > 0) h += UI.S(6);
                                it.AnswerY = h;
                                h += AnswerRowH;
                            }
                        }
                        it.H = h + UI.S(14);
                        it.Md = null;
                        break;
                    }
                case ChatKind.Notice:
                case ChatKind.Error:
                    {
                        bool err = it.Kind == ChatKind.Error;
                        int inner = w - (err ? UI.S(40) : UI.S(24));
                        it.Md = MdRenderer.PlainFlow(g, it.Text, inner, f, err ? f.Body : f.Small, err ? DrawOp.RFore : it.Level == 2 ? DrawOp.RError : DrawOp.RDim);
                        it.H = it.Md.Height + UI.S(err ? 20 + 40 : 6);
                        break;
                    }
                case ChatKind.Footer:
                    it.H = f.SmallLineH + UI.S(8);
                    break;
            }
        }

        bool dropHint;

        public bool DropHint
        {
            get { return dropHint; }
            set
            {
                if (dropHint == value) return;
                dropHint = value;
                PositionEditor();
                Invalidate();
            }
        }

        public Rectangle DropArea
        {
            get
            {
                int m = UI.S(10);
                return new Rectangle(m, m, ClientSize.Width - 2 * m, ClientSize.Height - 2 * m);
            }
        }

        public Color DropFill { get { return th.HighContrast ? th.Back : Theme.Blend(th.Back, th.Accent, th.Dark ? 0.10 : 0.06); } }

        void PaintDrop(Graphics g)
        {
            if (!dropHint) return;
            Rectangle r = DropArea;
            if (r.Width < UI.S(120) || r.Height < UI.S(80)) return;
            Color fill = DropFill;
            Glyphs.FillRound(g, fill, r, UI.S(12));
            Glyphs.Ring(g, th.HighContrast ? SystemColors.Highlight : th.Accent, r, UI.S(12), Glyphs.FocusWidth);
            string text = "Drop files to attach them";
            int cs = UI.S(44), gs = UI.S(22), mid = r.Y + r.Height / 2;
            if (!EmptyAnchor.IsEmpty && EmptyAnchor.Y - r.Y > cs + fonts.LineH + UI.S(40))
                mid = (r.Y + EmptyAnchor.Y - UI.S(12)) / 2 - (UI.S(12) + fonts.LineH - cs) / 2;
            Rectangle circle = new Rectangle(r.X + (r.Width - cs) / 2, mid - cs - UI.S(2), cs, cs);
            if (!th.HighContrast) Glyphs.Dot(g, circle, Theme.Blend(fill, th.Accent, th.Dark ? 0.22 : 0.14), cs);
            Glyphs.Draw("attach", g, new Rectangle(circle.X + (cs - gs) / 2, circle.Y + (cs - gs) / 2, gs, gs), th.HighContrast ? th.Fore : th.Accent);
            TextRenderer.DrawText(g, text, fonts.Body, new Rectangle(r.X + UI.S(8), mid + UI.S(10), r.Width - UI.S(16), fonts.LineH + UI.S(4)), th.Fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(th.Back);
            if (Items.Count == 0) { PaintEmpty(g); PaintDrop(g); return; }
            faceRect = Rectangle.Empty;
            foreach (ChatItem it in Items)
            {
                if (it.Kind != ChatKind.Activity || it.Hidden || !it.Expanded || it.Members == null || it.Members.Count == 0) continue;
                ChatItem last = null;
                foreach (ChatItem m in it.Members) if (!m.Hidden) last = m;
                if (last == null) continue;
                int top = it.Top + it.H - scrollY, bottom = last.Top + last.H - scrollY;
                if (bottom < 0 || top > ClientSize.Height) continue;
                using (SolidBrush b = new SolidBrush(th.Border)) g.FillRectangle(b, ContentX + UI.S(10), top + UI.S(2), 1, Math.Max(0, bottom - top - UI.S(4)));
            }
            foreach (ChatItem it in Items)
            {
                if (it.Hidden || it.Kids == 0 || it.LastKid == null || it.LastKid.Hidden) continue;
                int top = it.Top + fonts.LineH + UI.S(8) - scrollY, bottom = it.LastKid.Top + it.LastKid.H - scrollY;
                if (bottom < 0 || top > ClientSize.Height) continue;
                using (SolidBrush b = new SolidBrush(th.Border)) g.FillRectangle(b, ItemX(it) + UI.S(7), top + UI.S(2), 1, Math.Max(0, bottom - top - UI.S(4)));
            }
            for (int i = 0; i < Items.Count; i++)
            {
                ChatItem it = Items[i];
                if (it.Hidden) continue;
                int y = it.Top - scrollY;
                if (y > ClientSize.Height || y + it.H < 0) continue;
                int x = ItemX(it);
                int w = ContentW - Depth(it) * Indent;
                GraphicsState gs = g.Save();
                it.Buttons.Clear();
                try { PaintItem(g, it, i, x, y, w, e.ClipRectangle); }
                catch { it.LayoutVersion = -1; layoutPending = true; if (!anim.Enabled) anim.Start(); }
                finally { g.Restore(gs); }
            }
            PaintScrollbar(g);
            PaintJump(g);
            if (Focused && ShowFocusCues && !dropHint) Glyphs.Ring(g, th.Accent, new Rectangle(UI.S(2), UI.S(2), ClientSize.Width - UI.S(4), ClientSize.Height - UI.S(4)), UI.S(8), Glyphs.FocusWidth);
            PaintDrop(g);
        }

        void PaintEmpty(Graphics g)
        {
            suggestionRects.Clear();
            actionRects.Clear();
            int cw = ContentW, cx = ContentX;
            Rectangle a = EmptyAnchor.IsEmpty ? new Rectangle(cx, ClientSize.Height / 2, cw, 0) : EmptyAnchor;
            int center = a.X + a.Width / 2;
            string title = EmptyTitle;
            Size ts = TextRenderer.MeasureText(g, title.Length > 0 ? title : " ", titleFont, new Size(a.Width, 400), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int ty = a.Top - UI.S(26) - ts.Height;
            int fs = UI.S(56);
            faceRect = new Rectangle(center - fs / 2, ty - UI.S(14) - fs, fs, fs);
            if (faceRect.Top > UI.S(4)) Glyphs.Face(g, faceRect, th, Glyphs.FaceIdle, 0, emptyBlink);
            if (title.Length > 0)
                TextRenderer.DrawText(g, title, titleFont, new Rectangle(a.X, ty, a.Width, ts.Height + 2), th.Fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            int y = a.Bottom + UI.S(18);
            if (EmptyActions.Count > 0)
            {
                int total = 0;
                List<int> widths = new List<int>();
                foreach (KeyValuePair<string, Action> kv in EmptyActions) { int bw = TextRenderer.MeasureText(g, kv.Key, fonts.Body).Width + UI.S(32); widths.Add(bw); total += bw + UI.S(10); }
                int x = center - (total - UI.S(10)) / 2;
                for (int i = 0; i < EmptyActions.Count; i++)
                {
                    Rectangle r = new Rectangle(x, y, widths[i], UI.S(34));
                    actionRects.Add(r);
                    DrawButton(g, r, EmptyActions[i].Key, i == 0, hoverAction == i);
                    x += widths[i] + UI.S(10);
                }
                y += UI.S(34) + UI.S(16);
            }
            else if (Suggestions.Count > 0)
            {
                List<List<int>> rowsIdx = new List<List<int>>();
                List<int> widths = new List<int>();
                int rowW = 0;
                List<int> cur = new List<int>();
                for (int i = 0; i < Suggestions.Count; i++)
                {
                    int sw = TextRenderer.MeasureText(g, Suggestions[i].Key, fonts.Body).Width + UI.S(30);
                    widths.Add(sw);
                    if (cur.Count > 0 && rowW + sw > a.Width) { rowsIdx.Add(cur); cur = new List<int>(); rowW = 0; }
                    cur.Add(i);
                    rowW += sw + UI.S(8);
                }
                if (cur.Count > 0) rowsIdx.Add(cur);
                for (int i = 0; i < Suggestions.Count; i++) suggestionRects.Add(Rectangle.Empty);
                foreach (List<int> row in rowsIdx)
                {
                    int total = 0;
                    foreach (int i in row) total += widths[i] + UI.S(8);
                    int x = center - (total - UI.S(8)) / 2;
                    foreach (int i in row)
                    {
                        Rectangle r = new Rectangle(x, y, widths[i], UI.S(34));
                        suggestionRects[i] = r;
                        if (i == hoverSuggestion) Glyphs.Hover(g, th, th.TabHover, r, r.Height / 2);
                        Glyphs.DrawRound(g, th.Border, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), r.Height / 2);
                        TextRenderer.DrawText(g, Suggestions[i].Key, fonts.Body, r, i == hoverSuggestion ? th.Fore : th.ChromeFore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                        x += widths[i] + UI.S(8);
                    }
                    y += UI.S(34) + UI.S(8);
                }
                y += UI.S(10);
            }
            if (EmptyText.Length > 0)
            {
                Size s = TextRenderer.MeasureText(g, EmptyText, fonts.Small, new Size(a.Width, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, EmptyText, fonts.Small, new Rectangle(a.X, y, a.Width, s.Height + 4), th.ChromeDim, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter);
            }
        }

        Color UserBack { get { return th.HighContrast ? th.Back : th.Dark ? Color.FromArgb(0x2D, 0x2D, 0x2F) : Color.FromArgb(0xEF, 0xEF, 0xF1); } }

        void ItemSel(int idx, out int fo, out int fc, out int to, out int tc)
        {
            fo = -1; fc = 0; to = -1; tc = 0;
            if (!hasSel) return;
            SelPos s, e;
            Ordered(out s, out e);
            if (idx < s.Item || idx > e.Item) return;
            fo = idx == s.Item ? s.Op : 0;
            fc = idx == s.Item ? s.Ch : 0;
            to = idx == e.Item ? e.Op : int.MaxValue;
            tc = idx == e.Item ? e.Ch : int.MaxValue;
            if (idx == e.Item && idx == s.Item && s.Op == e.Op && s.Ch == e.Ch) fo = -1;
        }

        void PaintMd(Graphics g, ChatItem it, int idx, int itemX, int itemY, int mdX, int mdY, Rectangle clip, bool dim, int hoverCopyBlock, int copied)
        {
            if (it.Md == null) return;
            it.MdOx = mdX - itemX;
            it.MdOy = mdY - itemY;
            int fo, fc, to, tc;
            ItemSel(idx, out fo, out fc, out to, out tc);
            MdRenderer.Paint(g, it.Md, mdX, mdY, clip, th, dim, hoverLink, hoverCopyBlock, copied, fonts, fo, fc, to, tc);
        }

        void PaintItem(Graphics g, ChatItem it, int idx, int x, int y, int w, Rectangle clip)
        {
            ChatFonts f = fonts;
            switch (it.Kind)
            {
                case ChatKind.User:
                    {
                        if (editing == it) { PaintEditor(g, it, idx, x, y, w); break; }
                        int bh = it.H - (it.Queued || UserActions(it) ? UserRowH : 0);
                        PaintTray(g, it, idx, x, y);
                        if (it.BubbleW > 0)
                        {
                            Rectangle box = new Rectangle(x + it.BubbleOff, y + it.TrayH, it.BubbleW, bh - it.TrayH);
                            Glyphs.FillRound(g, UserBack, box, UI.S(14));
                            if (th.HighContrast) Glyphs.DrawRound(g, th.Border, box, UI.S(14));
                            int bx = box.X + UI.S(14);
                            int ty = box.Y + UI.S(11);
                            if (!string.IsNullOrEmpty(it.ContextLabel))
                            {
                                Glyphs.Draw("file", g, new Rectangle(bx - UI.S(2), ty, UI.S(14), f.SmallLineH), th.ChromeDim);
                                TextRenderer.DrawText(g, it.ContextLabel, f.Small, new Rectangle(bx + UI.S(16), ty, box.Width - UI.S(44), f.SmallLineH), th.ChromeDim,
                                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                                ty += f.SmallLineH + UI.S(4);
                            }
                            Region old = g.Clip;
                            bool clipped = !it.Expanded && it.Clipped;
                            int textBottom = y + bh - UI.S(11) - (clipped ? f.SmallLineH + UI.S(4) : 0);
                            g.SetClip(new Rectangle(box.X, ty, box.Width, Math.Max(0, textBottom - ty)), CombineMode.Intersect);
                            PaintMd(g, it, idx, x, y, bx, ty, clip, false, -1, -1);
                            g.Clip = old;
                            old.Dispose();
                            if (clipped)
                            {
                                Rectangle more = new Rectangle(bx, y + bh - UI.S(8) - f.SmallLineH, UI.S(120), f.SmallLineH);
                                SetButton(it, more, BtnExpand, y);
                                TextRenderer.DrawText(g, "Show all", f.Small, more, RoleLink(), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                            }
                        }
                        if (it.Queued)
                        {
                            int aw = UI.S(28), ah = UI.S(24);
                            int ax = x + w - 2 * aw;
                            int ay = y + bh + (UserRowH - ah) / 2;
                            foreach (int id in new int[] { BtnQueueEdit, BtnQueueRemove })
                            {
                                Rectangle r = new Rectangle(ax, ay, aw - UI.S(2), ah);
                                bool hot = hoverItem == idx && hoverButton == id;
                                if (hot) Glyphs.Hover(g, th, th.ButtonHover, r, UI.S(6));
                                Glyphs.Draw(id == BtnQueueEdit ? "pencil" : "trash", g, r, hot ? th.Fore : th.ChromeDim);
                                SetButton(it, r, id, y);
                                ax += aw;
                            }
                            TextRenderer.DrawText(g, string.IsNullOrEmpty(it.Summary) ? QueueRunning : it.Summary, f.Small, new Rectangle(x, y + bh, Math.Max(0, w - 2 * aw - UI.S(6)), UserRowH), th.ChromeDim,
                                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                        }
                        else if (UserActions(it) && hoverItem == idx && editing == null)
                        {
                            List<int> ids = new List<int>();
                            ids.Add(BtnUserCopy);
                            if (Changeable) { ids.Add(BtnUserEdit); ids.Add(BtnUserDelete); }
                            int aw = UI.S(28), ah = UI.S(24);
                            int ax = x + w - ids.Count * aw;
                            int ay = y + bh + (UserRowH - ah) / 2;
                            foreach (int id in ids)
                            {
                                Rectangle r = new Rectangle(ax, ay, aw - UI.S(2), ah);
                                bool hot = hoverButton == id;
                                if (hot) Glyphs.Hover(g, th, th.ButtonHover, r, UI.S(6));
                                string glyph = id == BtnUserCopy ? (copiedMsg == idx ? "check" : "copy") : id == BtnUserEdit ? "pencil" : "trash";
                                Glyphs.Draw(glyph, g, r, hot ? th.Fore : th.ChromeDim);
                                SetButton(it, r, id, y);
                                ax += aw;
                            }
                        }
                        break;
                    }
                case ChatKind.Assistant:
                    {
                        if (it.Md != null) PaintMd(g, it, idx, x, y, x, y, clip, false, hoverItem == idx ? hoverCopy : -1, copiedItem == idx ? copiedBlock : -1);
                        if (it.Streaming)
                        {
                            int d = UI.S(5);
                            int by = y + it.H - f.LineH / 2 - d / 2;
                            if (it.Text.Trim().Length > 0) by = y + it.H + UI.S(2);
                            if (it.Text.Trim().Length == 0) by = y + f.LineH / 2 - d / 2;
                            for (int i = 0; i < 3; i++)
                            {
                                int a = (phase + i * 3) % 9 < 4 ? 220 : 90;
                                Glyphs.Dot(g, new Rectangle(x + i * UI.S(9), by, d, d), Color.FromArgb(a, th.ChromeDim), d);
                            }
                        }
                        else if (it.Group == null && (hoverItem == idx || LastAnswer(it)))
                        {
                            bool copied = copiedMsg == idx;
                            string lbl = copied ? "Copied" : "Copy";
                            int bw = Math.Max(TextRenderer.MeasureText(g, "Copy", f.Small).Width, TextRenderer.MeasureText(g, "Copied", f.Small).Width) + UI.S(30);
                            Rectangle br = new Rectangle(x - UI.S(6), y + it.H - ActionRowH + UI.S(4), bw, UI.S(26));
                            SetButton(it, br, BtnCopyMsg, y);
                            bool hot = hoverItem == idx && hoverButton == BtnCopyMsg;
                            if (hot) Glyphs.Hover(g, th, th.ButtonHover, br, UI.S(6));
                            Glyphs.Draw(copied ? "check" : "copy", g, new Rectangle(br.X + UI.S(5), br.Y, UI.S(16), br.Height), hot ? th.Fore : th.ChromeDim);
                            TextRenderer.DrawText(g, lbl, f.Small, new Rectangle(br.X + UI.S(24), br.Y, br.Width - UI.S(24), br.Height), hot ? th.Fore : th.ChromeDim,
                                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                            if (RetryRequested != null && LastAnswer(it) && Changeable && editing == null)
                            {
                                string rl = "Retry";
                                Rectangle rr = new Rectangle(br.Right + UI.S(2), br.Y, TextRenderer.MeasureText(g, rl, f.Small).Width + UI.S(30), br.Height);
                                SetButton(it, rr, BtnRetry, y);
                                bool rh = hoverItem == idx && hoverButton == BtnRetry;
                                if (rh) Glyphs.Hover(g, th, th.ButtonHover, rr, UI.S(6));
                                Glyphs.Draw("reload", g, new Rectangle(rr.X + UI.S(5), rr.Y, UI.S(16), rr.Height), rh ? th.Fore : th.ChromeDim);
                                TextRenderer.DrawText(g, rl, f.Small, new Rectangle(rr.X + UI.S(24), rr.Y, rr.Width - UI.S(24), rr.Height), rh ? th.Fore : th.ChromeDim,
                                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                                br = rr;
                            }
                            if (it.Speed > 0)
                            {
                                int sx = br.Right + UI.S(10);
                                if (sx < x + w - UI.S(40))
                                    TextRenderer.DrawText(g, SpeedText(it), f.Small, new Rectangle(sx, br.Y, x + w - sx, br.Height), th.ChromeDim,
                                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                            }
                        }
                        break;
                    }
                case ChatKind.Activity:
                    PaintActivity(g, it, idx, x, y, w);
                    break;
                case ChatKind.Thinking:
                    {
                        Rectangle row = new Rectangle(x, y, w, f.LineH + UI.S(6));
                        bool hot = hoverItem == idx && hoverButton == BtnExpand;
                        Color c = hot ? th.Fore : th.ChromeDim;
                        if (it.Streaming) Glyphs.Spinner(g, new Rectangle(x - UI.S(2), y, UI.S(18), row.Height), th.ChromeDim, phase);
                        else Glyphs.Draw(it.Expanded ? "down" : "right", g, new Rectangle(x - UI.S(1), y, UI.S(16), row.Height), c);
                        double live = (DateTime.UtcNow - it.Started).TotalSeconds;
                        string about = ThoughtTitle(it.Text);
                        string took = it.Streaming ? (live >= 2 ? Secs(live) : "") : Secs(it.Seconds);
                        string label = about != null ? about + (took.Length > 0 ? "  \u00B7  " + took : "") : it.Streaming ? "Working" + (took.Length > 0 ? "  \u00B7  " + took : "") : "Thought for " + took;
                        if (it.Text.Trim().Length == 0 && !it.Streaming) label = "Thought briefly";
                        TextRenderer.DrawText(g, label, f.Body, new Rectangle(x + UI.S(22), y, w - UI.S(22), row.Height), c, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                        if (it.Md != null) PaintMd(g, it, idx, x, y, x + UI.S(22), y + row.Height + UI.S(4), clip, true, -1, -1);
                        break;
                    }
                case ChatKind.Tool:
                    PaintTool(g, it, idx, x, y, w);
                    break;
                case ChatKind.Permission:
                    PaintPermission(g, it, idx, x, y, w);
                    break;
                case ChatKind.Question:
                    PaintQuestion(g, it, idx, x, y, w);
                    break;
                case ChatKind.Notice:
                    {
                        Color c = it.Level == 2 ? th.Error : th.ChromeDim;
                        Glyphs.Draw(it.Level >= 1 ? "warning" : "dot", g, new Rectangle(x - UI.S(1), y + UI.S(2), UI.S(16), f.SmallLineH), c);
                        PaintMd(g, it, idx, x, y, x + UI.S(22), y + UI.S(2), clip, false, -1, -1);
                        break;
                    }
                case ChatKind.Error:
                    {
                        Rectangle box = new Rectangle(x, y, w, it.H);
                        Color edge = Theme.Blend(th.Back, th.Error, 0.45);
                        Glyphs.FillRound(g, Theme.Blend(th.Back, th.Error, th.Dark ? 0.12 : 0.06), box, UI.S(12));
                        Glyphs.DrawRound(g, edge, new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1), UI.S(12));
                        Glyphs.Draw("warning", g, new Rectangle(x + UI.S(8), y + UI.S(9), UI.S(16), f.LineH), th.Error);
                        PaintMd(g, it, idx, x, y, x + UI.S(30), y + UI.S(10), clip, false, -1, -1);
                        int bx = x + UI.S(30);
                        if (it.Action != null)
                        {
                            int bw = TextRenderer.MeasureText(g, it.ActionLabel, f.Body).Width + UI.S(24);
                            Rectangle br = new Rectangle(bx, y + it.H - UI.S(40), bw, UI.S(30));
                            SetButton(it, br, BtnAction, y);
                            DrawButton(g, br, it.ActionLabel, false, hoverItem == idx && hoverButton == BtnAction);
                            bx = br.Right + UI.S(8);
                        }
                        string cl = copiedMsg == idx ? "Copied" : "Copy error";
                        Rectangle cr = new Rectangle(bx, y + it.H - UI.S(40), Math.Max(TextRenderer.MeasureText(g, "Copy error", f.Body).Width, TextRenderer.MeasureText(g, "Copied", f.Body).Width) + UI.S(24), UI.S(30));
                        SetButton(it, cr, BtnCopyMsg, y);
                        DrawButton(g, cr, cl, false, hoverItem == idx && hoverButton == BtnCopyMsg);
                        break;
                    }
                case ChatKind.Footer:
                    {
                        string text = it.Undone ? (string.IsNullOrEmpty(it.UndoneText) ? "Changes undone" : it.UndoneText) : it.Text;
                        int tw = TextRenderer.MeasureText(g, text, f.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                        TextRenderer.DrawText(g, text, f.Small, new Point(x, y + UI.S(4)), th.ChromeDim, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                        if (!it.Undone && it.Action != null)
                        {
                            string ul = it.ActionLabel ?? "Undo";
                            int uw = TextRenderer.MeasureText(g, ul, f.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                            Rectangle ur = new Rectangle(x + tw + UI.S(10), y + UI.S(4), uw, f.SmallLineH);
                            SetButton(it, ur, BtnUndo, y);
                            bool hot = hoverItem == idx && hoverButton == BtnUndo;
                            using (Font u = hot ? new Font(f.Small, FontStyle.Underline) : null)
                                TextRenderer.DrawText(g, ul, u ?? f.Small, new Point(ur.X, ur.Y), RoleLink(), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                        }
                        break;
                    }
            }
        }

        static string LastLine(string s)
        {
            string t = (s ?? "").Replace("\r\n", "\n").TrimEnd('\n');
            int nl = t.LastIndexOf('\n');
            return (nl >= 0 ? t.Substring(nl + 1) : t).Trim();
        }

        int FaceMood(ChatItem a, out string step)
        {
            string dim;
            return FaceMood(a, out step, out dim);
        }

        public string StatusLine(ChatItem a)
        {
            string step;
            FaceMood(a, out step);
            return step;
        }

        public const string FirstStep = "Thinking about your message", AfterAnswer = "Thinking about your answer";

        static string FirstWord(string t)
        {
            int sp = t.IndexOf(' ');
            return sp > 0 ? t.Substring(0, sp) : t;
        }

        int FaceMood(ChatItem a, out string step, out string dim)
        {
            dim = null;
            if (PendingPermission() != null) { step = "Waiting for your OK"; return Glyphs.FaceWaiting; }
            ChatItem pq = PendingQuestion();
            if (pq != null) { step = pq.Question != null && pq.Question.Wait ? "Waiting for you in the browser" : "Waiting for your answer"; return Glyphs.FaceWaiting; }
            ChatItem running = null;
            if (a.Members != null)
                foreach (ChatItem m in a.Members)
                    if (m.Kind == ChatKind.Tool && m.Status == 0) running = m;
            string phase = a.Phase.Length > 0 && a.Phase != "Thinking" ? a.Phase : null;
            string detail = null, sub = null;
            int mood = Glyphs.FaceThinking;
            if (running != null)
            {
                mood = Glyphs.FaceWorking;
                if (phase != null) detail = phase;
                else
                {
                    detail = StatusWords.Doing(running.Label);
                    if (running.Icon == "branch" && running.LastKid != null) sub = StatusWords.Doing(running.LastKid.Label);
                }
                if (detail.StartsWith("Waiting for you")) mood = Glyphs.FaceWaiting;
            }
            else
            {
                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    ChatItem x = Items[i];
                    if (x.Kind == ChatKind.Assistant && x.Streaming && (x.Surfaced || (x.Draft && Substantial(x.Text)))) { step = "Writing the answer"; return Glyphs.FaceWorking; }
                    if (x == a) break;
                }
                if (phase != null)
                {
                    detail = phase;
                    mood = phase.StartsWith("Reading") ? Glyphs.FaceThinking : Glyphs.FaceWorking;
                }
            }
            string task = (a.CurrentTask ?? "").Trim().Length > 0 ? StatusWords.Doing(a.CurrentTask) : null;
            string recent = a.LastDoing != null && (task == null || a.DoingSeq > a.TaskSeq) ? a.LastDoing : task;
            string lead = a.Note ?? detail;
            step = lead ?? recent ?? FirstStep;
            foreach (string d in new string[] { a.Note != null ? detail : sub, sub, lead != null ? recent : null, task })
                if (d != null && d != step && (d != task || FirstWord(d) != FirstWord(step))) { dim = d; break; }
            return mood;
        }

        static string Plural(int n, string one, string many) { return n + " " + (n == 1 ? one : many); }

        static string FileKey(string label)
        {
            string l = (label ?? "").Trim();
            int sp = l.IndexOf(' ');
            string rest = sp > 0 ? l.Substring(sp + 1).Trim() : l;
            if (rest.StartsWith("to ", StringComparison.Ordinal)) rest = rest.Substring(3);
            int paren = rest.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) rest = rest.Substring(0, paren);
            return rest.ToLowerInvariant();
        }

        static string Summary(ChatItem a)
        {
            string tail;
            string head = SummaryHead(a, out tail);
            return tail.Length > 0 ? head + "  \u00B7  " + tail : head;
        }

        static string SummaryHead(ChatItem a, out string tail)
        {
            tail = "";
            HashSet<string> read = new HashSet<string>(), edit = new HashSet<string>(), gone = new HashSet<string>(), sound = new HashSet<string>();
            int cmd = 0, search = 0, web = 0, browse = 0, list = 0, help = 0, video = 0, other = 0;
            bool thought = false, reviewed = false;
            if (a.Members != null)
                foreach (ChatItem m in a.Members)
                {
                    if (m.Kind == ChatKind.Thinking) { thought = true; continue; }
                    if (m.Kind != ChatKind.Tool || (m.Status == 3 && m.Summary != "stopped")) continue;
                    if (m.Status == 1 && m.Under == null && m.Label == AgentSession.ReviewLabel) { reviewed = true; continue; }
                    switch (m.Icon)
                    {
                        case "file": read.Add(FileKey(m.Label)); break;
                        case "pencil": edit.Add(FileKey(m.Label)); break;
                        case "trash": gone.Add(FileKey(m.Label)); break;
                        case "terminal": cmd++; break;
                        case "search": search++; break;
                        case "globe": web++; break;
                        case "cursor": browse++; break;
                        case "folder": list++; break;
                        case "branch": help++; break;
                        case "wave": sound.Add(FileKey(m.Label)); break;
                        case "film": video++; break;
                        case "checklist": case "ask": break;
                        default: other++; break;
                    }
                }
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(a.Label) && read.Count + edit.Count + gone.Count + sound.Count + cmd + search + web + browse + list + help + video + other == 0) { tail = Secs(a.Seconds); return a.Label; }
            if (read.Count > 0) parts.Add("read " + Plural(read.Count, "file", "files"));
            if (edit.Count > 0) parts.Add("edited " + Plural(edit.Count, "file", "files"));
            if (gone.Count > 0) parts.Add("deleted " + Plural(gone.Count, "item", "items"));
            if (sound.Count > 0) parts.Add("analyzed " + Plural(sound.Count, "sound file", "sound files"));
            if (video > 0) parts.Add("watched a video");
            if (cmd > 0) parts.Add("ran " + Plural(cmd, "command", "commands"));
            if (search > 0) parts.Add("searched " + (search == 1 ? "once" : search + " times"));
            if (web > 0) parts.Add("read " + Plural(web, "web page", "web pages"));
            if (browse > 0) parts.Add("used the browser");
            if (list > 0) parts.Add("looked in " + Plural(list, "folder", "folders"));
            if (help > 0) parts.Add("used " + Plural(help, "helper", "helpers"));
            if (other > 0 && parts.Count < 3) parts.Add(Plural(other, "other step", "other steps"));
            string s;
            if (parts.Count == 0)
            {
                s = thought ? "Thought for " + Secs(a.Seconds) : "Worked for " + Secs(a.Seconds);
                tail = a.Stopped ? "stopped" : "";
            }
            else
            {
                if (parts.Count > 3) parts.RemoveRange(3, parts.Count - 3);
                s = string.Join(", ", parts.ToArray());
                s = char.ToUpperInvariant(s[0]) + s.Substring(1);
                tail = Secs(a.Seconds) + (a.Stopped ? "  \u00B7  stopped" : reviewed ? "  \u00B7  reviewed" : "");
            }
            return s;
        }

        void PaintEditor(Graphics g, ChatItem it, int idx, int x, int y, int w)
        {
            ChatFonts f = fonts;
            PaintTray(g, it, idx, x, y);
            Rectangle box = new Rectangle(x, y + it.TrayH, w, it.H - it.TrayH - UI.S(2));
            Glyphs.FillRound(g, th.InputBack, box, UI.S(14));
            Glyphs.DrawRound(g, Theme.Blend(th.InputBorder, th.Fore, 0.35), new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1), UI.S(14));
            int by = box.Bottom - UI.S(12) - UI.S(32);
            string send = it.Queued ? "Save" : "Send", cancel = "Cancel";
            int sw = TextRenderer.MeasureText(g, send, f.Body).Width + UI.S(28);
            int cw = TextRenderer.MeasureText(g, cancel, f.Body).Width + UI.S(24);
            Rectangle sr = new Rectangle(box.Right - UI.S(12) - sw, by, sw, UI.S(32));
            Rectangle cr = new Rectangle(sr.X - UI.S(8) - cw, by, cw, UI.S(32));
            SetButton(it, cr, BtnEditCancel, y);
            SetButton(it, sr, BtnEditSend, y);
            DrawButton(g, cr, cancel, false, hoverItem == idx && hoverButton == BtnEditCancel);
            DrawButton(g, sr, send, true, hoverItem == idx && hoverButton == BtnEditSend);
            string hint = it.Queued ? "Enter saves, Esc cancels" : LastUserItem() == it ? "Enter sends, Esc cancels" : "Sending removes the later messages. Esc cancels.";
            int hw = TextRenderer.MeasureText(g, hint, f.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
            Rectangle hr = new Rectangle(box.X + UI.S(16), by, cr.X - UI.S(12) - (box.X + UI.S(16)), UI.S(32));
            if (hr.Width >= Math.Min(hw, UI.S(120)))
                TextRenderer.DrawText(g, hint, f.Small, hr, th.ChromeDim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        public static string ThoughtTitle(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string head = null, first = null;
            foreach (string raw in text.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0) continue;
                bool bold = l.Length > 4 && l.StartsWith("**", StringComparison.Ordinal) && l.EndsWith("**", StringComparison.Ordinal) && l.IndexOf("**", 2, StringComparison.Ordinal) == l.Length - 2;
                if (bold || l[0] == '#') head = l;
                else if (first == null) first = l;
            }
            string t = head ?? first;
            if (t == null) return null;
            if (head == null)
            {
                for (int i = 12; i < t.Length; i++)
                {
                    char c = t[i];
                    if ((c == '.' || c == '!' || c == '?') && (i + 1 == t.Length || t[i + 1] == ' ')) { t = t.Substring(0, c == '.' ? i : i + 1); break; }
                }
            }
            StringBuilder sb = new StringBuilder();
            int marks = 0;
            while (marks < t.Length && (t[marks] == '#' || t[marks] == ' ')) marks++;
            for (int i = marks; i < t.Length; i++)
                if (t[i] != '*' && t[i] != '`' && !char.IsControl(t[i])) sb.Append(t[i]);
            t = sb.ToString().Trim().TrimEnd(':', '.', ' ');
            if (head == null)
            {
                System.Text.RegularExpressions.Match lead = UserLead.Match(t);
                if (lead.Success && t.Length - lead.Length >= 4) t = char.ToUpperInvariant(t[lead.Length]) + t.Substring(lead.Length + 1);
            }
            if (t.Length == 0) return null;
            return t.Length > 80 ? t.Substring(0, 79).TrimEnd() + "\u2026" : t;
        }

        static readonly System.Text.RegularExpressions.Regex UserLead = new System.Text.RegularExpressions.Regex(
            @"^the user (just |simply |now |also )?(wants|would like|is asking|asks|asked|needs|is requesting|requested|requests)( for)?( me| us)?( to)?( know| find out| understand| learn)?( about)? ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        void PaintActivity(Graphics g, ChatItem it, int idx, int x, int y, int w)
        {
            ChatFonts f = fonts;
            bool hot = hoverItem == idx && hoverButton == BtnExpand;
            int rowH = f.LineH + UI.S(12);
            bool expandable = it.Members != null && it.Members.Count > 0;
            if (hot && expandable) Glyphs.Hover(g, th, th.TabHover, new Rectangle(x - UI.S(8), y, w + UI.S(16), it.H), UI.S(8));
            int tx = x + UI.S(28);
            string label, time = "", task = "";
            Color lc, dc = th.ChromeDim;
            if (it.Running)
            {
                string step, dim;
                int mood = FaceMood(it, out step, out dim);
                bool blink = phase % 46 < 2;
                Rectangle fr = new Rectangle(x - UI.S(2), y + (rowH - UI.S(22)) / 2, UI.S(22), UI.S(22));
                Glyphs.Face(g, fr, th, mood, phase, blink);
                faceRect = fr;
                label = step;
                task = dim ?? "";
                double secs = it.Seconds + (DateTime.UtcNow - it.Started).TotalSeconds;
                if (secs >= 1) time = Secs(secs);
                lc = th.Fore;
            }
            else
            {
                Glyphs.Draw(it.Expanded ? "down" : "right", g, new Rectangle(x + UI.S(2), y, UI.S(16), rowH), hot ? th.Fore : th.ChromeDim);
                label = SummaryHead(it, out time);
                lc = hot ? th.Fore : th.ChromeDim;
            }
            const TextFormatFlags one = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int avail = Math.Max(UI.S(40), x + w - tx - UI.S(24));
            int tw = TextRenderer.MeasureText(g, time, f.Body, Size.Empty, one).Width;
            int reserve = tw > 0 ? tw + UI.S(14) : 0;
            int lw = Math.Min(TextRenderer.MeasureText(g, label, f.Body, Size.Empty, one).Width + UI.S(2), avail - reserve);
            TextRenderer.DrawText(g, label, f.Body, new Rectangle(tx, y, Math.Max(UI.S(20), lw), rowH), lc, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | one);
            int ex = tx + Math.Max(UI.S(20), lw);
            if (task.Length > 0 && task != label)
            {
                string seg = "\u00B7  " + task;
                int room = tx + avail - reserve - (ex + UI.S(8));
                if (room >= UI.S(72))
                {
                    int kw = Math.Min(room, TextRenderer.MeasureText(g, seg, f.Body, Size.Empty, one).Width + UI.S(2));
                    TextRenderer.DrawText(g, seg, f.Body, new Rectangle(ex + UI.S(8), y, kw, rowH), dc, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | one);
                    ex += UI.S(8) + kw;
                }
            }
            if (tw > 0)
            {
                TextRenderer.DrawText(g, "\u00B7  " + time, f.Body, new Rectangle(ex + UI.S(8), y, tw + UI.S(30), rowH), dc, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                ex += UI.S(8) + TextRenderer.MeasureText(g, "\u00B7  " + time, f.Body, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
            }
            if (it.Running && expandable) Glyphs.Draw(it.Expanded ? "down" : "right", g, new Rectangle(ex + UI.S(4), y, UI.S(16), rowH), hot ? th.Fore : th.ChromeDim);
        }

        static string Secs(double s)
        {
            if (s < 60) return Math.Max(1, (int)Math.Round(s)) + "s";
            if (s < 3600) return ((int)s / 60) + "m " + ((int)s % 60) + "s";
            return ((int)s / 3600) + "h " + ((int)s % 3600 / 60) + "m";
        }

        Color RoleLink() { return MdRenderer.RoleColor(DrawOp.RLink, th, false); }

        void SetButton(ChatItem it, Rectangle screen, int id, int itemScreenY)
        {
            Rectangle local = new Rectangle(screen.X, screen.Y - itemScreenY, screen.Width, screen.Height);
            for (int i = 0; i < it.Buttons.Count; i++)
                if (it.Buttons[i].Value == id) { it.Buttons[i] = new KeyValuePair<Rectangle, int>(local, id); return; }
            it.Buttons.Add(new KeyValuePair<Rectangle, int>(local, id));
        }

        void DrawButton(Graphics g, Rectangle r, string text, bool primary, bool hot) { DrawButton(g, r, text, primary, hot, false); }

        void DrawButton(Graphics g, Rectangle r, string text, bool primary, bool hot, bool danger)
        {
            Color fill, border, fore;
            if (danger)
            {
                fill = hot ? th.ButtonHover : th.Back;
                border = th.HighContrast ? th.Error : Theme.Blend(th.InputBorder, th.Error, 0.55);
                fore = th.Error;
            }
            else if (primary)
            {
                fill = hot ? Theme.Blend(th.Accent, th.Dark ? Color.White : Color.Black, 0.12) : th.Accent;
                border = fill;
                fore = th.HighContrast ? SystemColors.HighlightText : th.Dark ? Color.Black : Color.White;
            }
            else
            {
                fill = hot ? th.ButtonHover : th.Back;
                border = th.InputBorder;
                fore = th.Fore;
            }
            Glyphs.FillRound(g, fill, r, UI.S(8));
            Glyphs.DrawRound(g, border, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), UI.S(8));
            TextRenderer.DrawText(g, text, fonts.Body, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        void PaintTool(Graphics g, ChatItem it, int idx, int x, int y, int w)
        {
            ChatFonts f = fonts;
            int rowH = f.LineH + UI.S(8);
            bool hot = hoverItem == idx && hoverButton == BtnExpand;
            if (hot) Glyphs.Hover(g, th, th.TabHover, new Rectangle(x - UI.S(8), y, w + UI.S(16), rowH), UI.S(8));
            Rectangle icon = new Rectangle(x - UI.S(1), y, UI.S(16), rowH);
            if (it.Status == 0) Glyphs.Spinner(g, new Rectangle(x - UI.S(2), y + (rowH - UI.S(18)) / 2, UI.S(18), UI.S(18)), th.Accent, phase);
            else Glyphs.Draw(it.Icon, g, icon, it.Status == 2 ? th.Error : th.ChromeDim);
            int tx = x + UI.S(22);
            int avail = w - UI.S(22) - UI.S(22);
            int openW = 0;
            if (it.Action != null && it.Status == 1 && !string.IsNullOrEmpty(it.ActionLabel))
            {
                openW = TextRenderer.MeasureText(g, it.ActionLabel, f.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + UI.S(14);
                avail = Math.Max(UI.S(60), avail - openW);
            }
            string label = it.Label;
            string summary = it.Status != 0 ? it.Summary ?? "" : it.Icon == "branch" ? LastLine(it.Live) : "";
            Color labelColor = it.Status == 3 ? th.ChromeDim : th.Fore;
            int lw = TextRenderer.MeasureText(g, label, f.Body, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
            int sw = summary.Length > 0 ? TextRenderer.MeasureText(g, summary, f.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width : 0;
            bool failed = it.Status == 2;
            int maxLabel = summary.Length == 0 ? avail : failed ? Math.Max(UI.S(60), avail - sw - UI.S(10)) : Math.Max(Math.Min(lw + UI.S(4), avail * 4 / 5), avail - sw - UI.S(10));
            Rectangle lr = new Rectangle(tx, y, Math.Min(lw + UI.S(4), maxLabel), rowH);
            TextRenderer.DrawText(g, label, f.Body, lr, labelColor, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (summary.Length > 0 && !failed && tx + avail - lr.Right - UI.S(8) < UI.S(64)) summary = "";
            if (summary.Length > 0)
            {
                Color sc = it.Status == 2 ? th.Error : th.ChromeDim;
                Rectangle sr = new Rectangle(lr.Right + UI.S(8), y, Math.Max(0, tx + avail - lr.Right - UI.S(8)), rowH);
                TextRenderer.DrawText(g, summary, f.Small, sr, sc, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            if (Expandable(it)) Glyphs.Draw(it.Expanded ? "down" : "right", g, new Rectangle(x + w - UI.S(16), y, UI.S(16), rowH), hot ? th.Fore : th.ChromeDim);
            if (openW > 0)
            {
                Rectangle orr = new Rectangle(tx + avail + UI.S(8), y, openW - UI.S(8), rowH);
                bool oh = hoverItem == idx && hoverButton == BtnAction;
                using (Font u = oh ? new Font(f.Small, FontStyle.Underline) : null)
                    TextRenderer.DrawText(g, it.ActionLabel, u ?? f.Small, orr, RoleLink(), TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                SetButton(it, new Rectangle(orr.X - UI.S(4), y + UI.S(3), orr.Width + UI.S(4), rowH - UI.S(6)), BtnAction, y);
            }
            if (it.Status != 0 && it.Report.Length > 0)
            {
                if (!it.Expanded || it.Md == null) return;
                int by = y + rowH + UI.S(2);
                if (it.DetailLines != null)
                {
                    foreach (string line in it.DetailLines)
                    {
                        TextRenderer.DrawText(g, line, f.Small, new Rectangle(tx, by, w - UI.S(22), f.SmallLineH), th.ChromeDim,
                            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                        by += f.SmallLineH;
                    }
                    by += UI.S(10);
                }
                Rectangle rb = new Rectangle(tx - UI.S(2), by, w - UI.S(20), it.Md.Height + UI.S(20));
                Glyphs.FillRound(g, CardBack, rb, UI.S(8));
                Glyphs.DrawRound(g, th.InputBorder, new Rectangle(rb.X, rb.Y, rb.Width - 1, rb.Height - 1), UI.S(8));
                PaintMd(g, it, idx, x, y, rb.X + UI.S(12), rb.Y + UI.S(10), ClientRectangle, false, -1, -1);
                return;
            }
            if (it.DetailLines != null && it.DetailLines.Count > 0)
            {
                Rectangle box = new Rectangle(tx - UI.S(2), y + rowH + UI.S(2), w - UI.S(20), it.DetailLines.Count * f.MonoLineH + UI.S(14));
                Glyphs.FillRound(g, MdRenderer.CodeBack(th), box, UI.S(8));
                Glyphs.DrawRound(g, MdRenderer.CodeBorder(th), new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1), UI.S(8));
                int ly = box.Y + UI.S(7);
                Region old = g.Clip;
                g.SetClip(new Rectangle(box.X + UI.S(6), box.Y, box.Width - UI.S(12), box.Height), CombineMode.Intersect);
                bool diff = it.Detail.Length > 0 && (it.Detail.StartsWith("- ") || it.Detail.StartsWith("+ "));
                foreach (string line in it.DetailLines)
                {
                    Color c = th.ChromeDim;
                    if (diff && line.StartsWith("+ ")) c = th.Dark ? Color.FromArgb(0x7E, 0xC6, 0x7E) : Color.FromArgb(0x1E, 0x7B, 0x34);
                    else if (diff && line.StartsWith("- ")) c = th.Dark ? Color.FromArgb(0xE5, 0x8A, 0x8A) : Color.FromArgb(0xB3, 0x26, 0x1E);
                    else if (it.Status != 0) c = th.Fore;
                    TextRenderer.DrawText(g, line, f.Mono, new Point(box.X + UI.S(8), ly), c, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
                    ly += f.MonoLineH;
                }
                g.Clip = old;
                old.Dispose();
            }
        }

        void PaintPermission(Graphics g, ChatItem it, int idx, int x, int y, int w)
        {
            ChatFonts f = fonts;
            PermissionAsk a = it.Ask;
            if (it.Decision >= 0)
            {
                bool allowed = it.Decision != (int)PermissionDecision.Deny;
                Glyphs.Draw(allowed ? "check" : "cross", g, new Rectangle(x - UI.S(1), y, UI.S(16), it.H), allowed ? th.ChromeDim : th.Error);
                string t = (allowed ? (it.Decision == (int)PermissionDecision.AllowAlways ? "Allowed for this chat: " : "Allowed: ") : "Denied: ") + a.Title + (string.IsNullOrEmpty(a.Summary) ? "" : " \u2014 " + a.Summary);
                TextRenderer.DrawText(g, t, f.Small, new Rectangle(x + UI.S(22), y, w - UI.S(22), it.H), th.ChromeDim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                return;
            }
            Rectangle box = new Rectangle(x, y, w, it.H);
            Color edge = a.High ? Theme.Blend(th.Back, th.Error, 0.55) : th.InputBorder;
            Glyphs.FillRound(g, CardBack, box, UI.S(12));
            Glyphs.DrawRound(g, edge, new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1), UI.S(12));
            int ix = x + UI.S(14), iy = y + UI.S(14), inner = w - UI.S(28);
            Glyphs.Draw(a.High ? "warning" : "shield", g, new Rectangle(ix - UI.S(1), iy, UI.S(16), f.LineH), a.High ? th.Error : th.Accent);
            TextRenderer.DrawText(g, a.Title, f.Bold, new Rectangle(ix + UI.S(22), iy, inner - UI.S(22), f.LineH), th.Fore, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            iy += f.LineH + UI.S(6);
            if (!string.IsNullOrEmpty(a.Summary))
            {
                Size ss = TextRenderer.MeasureText(g, a.Summary, f.Body, new Size(inner, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, a.Summary, f.Body, new Rectangle(ix, iy, inner, ss.Height + 2), th.Fore, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                iy += ss.Height + UI.S(6);
            }
            if (!string.IsNullOrEmpty(a.Reading))
            {
                Size rd = TextRenderer.MeasureText(g, a.Reading, f.Body, new Size(inner, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, a.Reading, f.Body, new Rectangle(ix, iy, inner, rd.Height + 2), th.Fore, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                iy += rd.Height + UI.S(6);
            }
            if (it.Md != null)
            {
                PaintMd(g, it, idx, x, y, ix, iy, ClientRectangle, false, -1, -1);
                iy += it.Md.Height + UI.S(8);
                if (it.Md.Hidden > 0 || it.Expanded)
                {
                    string ml = it.Expanded ? "Show less" : "Show everything before deciding";
                    Size ms = TextRenderer.MeasureText(g, ml, f.Small, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    Rectangle mr = new Rectangle(ix, iy, ms.Width, f.SmallLineH);
                    bool hot = hoverItem == idx && hoverButton == BtnExpand;
                    using (Font u = hot ? new Font(f.Small, FontStyle.Underline) : null)
                        TextRenderer.DrawText(g, ml, u ?? f.Small, new Point(mr.X, mr.Y), RoleLink(), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    SetButton(it, mr, BtnExpand, y);
                    iy += f.SmallLineH + UI.S(8);
                }
            }
            if (!string.IsNullOrEmpty(a.Where))
            {
                TextRenderer.DrawText(g, a.Where, f.Small, new Rectangle(ix, iy, inner, f.SmallLineH), th.ChromeDim, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis);
                iy += f.SmallLineH + UI.S(6);
            }
            if (!string.IsNullOrEmpty(a.Reason))
            {
                Size rs = TextRenderer.MeasureText(g, a.Reason, f.Small, new Size(inner, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, a.Reason, f.Small, new Rectangle(ix, iy, inner, rs.Height + 2), a.High ? th.Error : th.ChromeDim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                iy += rs.Height + UI.S(8);
            }
            int bx = ix;
            string[] labels = { "Allow", "Allow for this chat", "Deny" };
            int[] ids = { BtnAllow, BtnAlways, BtnDeny };
            for (int i = 0; i < 3; i++)
            {
                if (ids[i] == BtnAlways && (!a.AllowRemember || string.IsNullOrEmpty(a.Key))) continue;
                int bw = Math.Min(inner, TextRenderer.MeasureText(g, labels[i], f.Body).Width + UI.S(22));
                if (bx + bw > ix + inner && bx > ix) { bx = ix; iy += UI.S(38); }
                Rectangle br = new Rectangle(bx, iy, bw, UI.S(30));
                SetButton(it, br, ids[i], y);
                DrawButton(g, br, labels[i], ids[i] == BtnAllow && !a.High, hoverItem == idx && hoverButton == ids[i], ids[i] == BtnAllow && a.High);
                bx += bw + UI.S(8);
            }
        }

        int PermissionButtonRows(Graphics g, PermissionAsk a, int inner)
        {
            int rows = 1, bx = 0;
            string[] labels = { "Allow", "Allow for this chat", "Deny" };
            for (int i = 0; i < 3; i++)
            {
                if (i == 1 && (!a.AllowRemember || string.IsNullOrEmpty(a.Key))) continue;
                int bw = Math.Min(inner, TextRenderer.MeasureText(g, labels[i], fonts.Body).Width + UI.S(22));
                if (bx + bw > inner && bx > 0) { rows++; bx = 0; }
                bx += bw + UI.S(8);
            }
            return rows;
        }

        static string QTitle(UserQuestion q) { string t = (q.Title ?? "").Trim(); return t.Length > 600 ? t.Substring(0, 600) + "..." : t; }
        static string QDetail(UserQuestion q) { string t = (q.Detail ?? "").Trim(); return t.Length > 1500 ? t.Substring(0, 1500) + "..." : t; }
        static string QOption(string s) { string t = (s ?? "").Trim(); return t.Length > 300 ? t.Substring(0, 300) + "..." : t; }

        int OptionRowH(Graphics g, string label, int inner)
        {
            int lw = Math.Max(UI.S(40), inner - UI.S(52));
            int lh = TextRenderer.MeasureText(g, QOption(label), fonts.Body, new Size(lw, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            return Math.Max(UI.S(36), lh + UI.S(16));
        }

        int QuestionButtonRows(Graphics g, UserQuestion q, int inner)
        {
            int rows = 1, bx = 0;
            foreach (string o in q.Options)
            {
                int bw = Math.Min(inner, TextRenderer.MeasureText(g, QOption(o), fonts.Body).Width + UI.S(22));
                if (bx + bw > inner && bx > 0) { rows++; bx = 0; }
                bx += bw + UI.S(8);
            }
            return rows;
        }

        Color CardBack { get { return th.HighContrast ? th.Back : th.Dark ? Color.FromArgb(0x25, 0x25, 0x25) : Color.White; } }

        void PaintQuestion(Graphics g, ChatItem it, int idx, int x, int y, int w)
        {
            ChatFonts f = fonts;
            UserQuestion q = it.Question;
            if (it.Decision >= 0 || q == null)
            {
                bool declined = it.Decision >= 2;
                Glyphs.Draw(declined ? "cross" : "check", g, new Rectangle(x - UI.S(1), y, UI.S(16), it.H), th.ChromeDim);
                string head = q != null && !q.Wait ? QTitle(q).Replace('\n', ' ') : "";
                Rectangle tr = new Rectangle(x + UI.S(22), y, w - UI.S(22), it.H);
                if (head.Length > 0 && it.Text.Length > 0)
                {
                    TextFormatFlags one = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                    int gap = UI.S(18);
                    TextFormatFlags whole = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
                    int aw = Math.Min(tr.Width * 2 / 3, TextRenderer.MeasureText(g, it.Text, f.Small, Size.Empty, whole).Width + UI.S(4));
                    int hw = Math.Min(Math.Max(UI.S(40), tr.Width - aw - gap), TextRenderer.MeasureText(g, head, f.Small, Size.Empty, whole).Width + UI.S(2));
                    TextRenderer.DrawText(g, head, f.Small, new Rectangle(tr.X, tr.Y, hw, tr.Height), th.ChromeDim, one);
                    TextRenderer.DrawText(g, "\u00B7", f.Small, new Rectangle(tr.X + hw, tr.Y, gap, tr.Height), th.ChromeDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                    TextRenderer.DrawText(g, it.Text, f.Small, new Rectangle(tr.X + hw + gap, tr.Y, Math.Max(0, tr.Right - (tr.X + hw + gap)), tr.Height), declined ? th.ChromeDim : th.Fore, one);
                }
                else TextRenderer.DrawText(g, it.Text.Length > 0 ? it.Text : head, f.Small, tr, th.ChromeDim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                return;
            }
            Rectangle box = new Rectangle(x, y, w, it.H);
            Color back = CardBack;
            Glyphs.FillRound(g, back, box, UI.S(12));
            Glyphs.DrawRound(g, th.HighContrast ? th.Border : Theme.Blend(th.InputBorder, th.Accent, 0.55), new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1), UI.S(12));
            int ix = x + UI.S(14), iy = y + UI.S(14), inner = w - UI.S(28);
            int tx = ix + UI.S(30), textW = Math.Max(UI.S(60), inner - UI.S(30));
            Glyphs.Face(g, new Rectangle(ix - UI.S(2), iy, UI.S(24), UI.S(24)), th, Glyphs.FaceWaiting, phase, phase % 46 < 2);
            string title = QTitle(q);
            Size ts = TextRenderer.MeasureText(g, title, f.Bold, new Size(textW, 4000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int titleH = Math.Max(UI.S(24), ts.Height);
            TextRenderer.DrawText(g, title, f.Bold, new Rectangle(tx, iy + (ts.Height < UI.S(24) ? (UI.S(24) - ts.Height) / 2 : 0), textW, ts.Height + 2), th.Fore, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            iy += titleH;
            if (q.Detail.Length > 0)
            {
                string d = QDetail(q);
                Size ds = TextRenderer.MeasureText(g, d, f.Small, new Size(textW, 4000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, d, f.Small, new Rectangle(tx, iy + UI.S(4), textW, ds.Height + 2), th.ChromeDim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                iy += UI.S(4) + ds.Height;
            }
            iy += UI.S(12);
            if (q.Wait)
            {
                int bx = ix, rowY = iy;
                for (int i = 0; i < q.Options.Count; i++)
                {
                    string lbl = QOption(q.Options[i]);
                    int bw = Math.Min(inner, TextRenderer.MeasureText(g, lbl, f.Body).Width + UI.S(22));
                    if (bx + bw > ix + inner && bx > ix) { bx = ix; iy += UI.S(38); }
                    Rectangle br = new Rectangle(bx, iy, bw, UI.S(30));
                    SetButton(it, br, BtnOption + i, y);
                    DrawButton(g, br, lbl, i == 0, hoverItem == idx && hoverButton == BtnOption + i);
                    bx += bw + UI.S(8);
                }
                if (q.DoneWhen != null && iy == rowY)
                {
                    string watch = "Watching the page";
                    int ww = TextRenderer.MeasureText(g, watch, f.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                    int wx = ix + inner - ww - UI.S(22);
                    if (wx > bx + UI.S(8))
                    {
                        Glyphs.Spinner(g, new Rectangle(wx, iy + UI.S(6), UI.S(18), UI.S(18)), th.ChromeDim, phase);
                        TextRenderer.DrawText(g, watch, f.Small, new Rectangle(wx + UI.S(22), iy, ww + UI.S(4), UI.S(30)), th.ChromeDim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    }
                }
                return;
            }
            for (int i = 0; i < q.Options.Count; i++)
            {
                string lbl = QOption(q.Options[i]);
                int rh = OptionRowH(g, lbl, inner);
                Rectangle r = new Rectangle(ix, iy, inner, rh);
                bool hot = hoverItem == idx && hoverButton == BtnOption + i;
                Glyphs.FillRound(g, hot ? th.ButtonHover : back, r, UI.S(8));
                Glyphs.DrawRound(g, hot ? Theme.Blend(th.InputBorder, th.Fore, 0.35) : th.InputBorder, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), UI.S(8));
                Rectangle nb = new Rectangle(r.X + UI.S(10), r.Y + (Math.Min(rh, UI.S(36)) - UI.S(20)) / 2, UI.S(20), UI.S(20));
                Glyphs.FillRound(g, hot ? th.Accent : th.Chrome, nb, UI.S(5));
                Color nf = hot ? (th.HighContrast ? SystemColors.HighlightText : th.Dark ? Color.Black : Color.White) : th.ChromeFore;
                TextRenderer.DrawText(g, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), f.Small, nb, nf, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                int lw = Math.Max(UI.S(40), inner - UI.S(52));
                Size ls = TextRenderer.MeasureText(g, lbl, f.Body, new Size(lw, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, lbl, f.Body, new Rectangle(r.X + UI.S(40), r.Y + (rh - ls.Height) / 2, lw, ls.Height + 2), th.Fore, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                SetButton(it, r, BtnOption + i, y);
                iy += rh + UI.S(6);
            }
            if (q.FreeText)
            {
                Rectangle r = new Rectangle(ix, y + it.AnswerY, inner, AnswerRowH);
                bool focused = answering == it && answerBox.Focused, hot = hoverItem == idx && (hoverButton == BtnAnswer || hoverButton == BtnAnswerSend);
                Glyphs.DrawRound(g, focused ? th.Accent : hot ? Theme.Blend(th.InputBorder, th.Fore, 0.35) : th.InputBorder, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), UI.S(8));
                Rectangle nb = new Rectangle(r.X + UI.S(10), r.Y + (UI.S(40) - UI.S(20)) / 2, UI.S(20), UI.S(20));
                Glyphs.FillRound(g, th.Chrome, nb, UI.S(5));
                Glyphs.Draw("pencil", g, Rectangle.Inflate(nb, -UI.S(3), -UI.S(3)), th.ChromeFore);
                SetButton(it, r, BtnAnswer, y);
                bool ready = answering == it && answerBox.Text.Trim().Length > 0;
                int sb = UI.S(28);
                Rectangle sr = new Rectangle(r.Right - UI.S(8) - sb, r.Bottom - UI.S(6) - sb, sb, sb);
                if (ready)
                {
                    Glyphs.FillRound(g, hoverItem == idx && hoverButton == BtnAnswerSend ? Theme.Blend(th.Accent, th.Dark ? Color.White : Color.Black, 0.12) : th.Accent, sr, sb / 2);
                    Glyphs.Draw("send", g, Rectangle.Inflate(sr, -UI.S(6), -UI.S(6)), th.OnAccent);
                    SetButton(it, sr, BtnAnswerSend, y);
                }
                else Glyphs.Draw("send", g, Rectangle.Inflate(sr, -UI.S(6), -UI.S(6)), th.MenuDisabled);
            }
        }

        static string QuestionText(ChatItem it)
        {
            UserQuestion q = it.Question;
            if (q == null) return it.Text;
            if (q.Wait) return it.Decision >= 0 ? it.Text : QTitle(q);
            StringBuilder sb = new StringBuilder(QTitle(q));
            if (it.Decision < 0) for (int i = 0; i < q.Options.Count; i++) sb.Append('\n').Append(i + 1).Append(". ").Append(QOption(q.Options[i]));
            else sb.Append('\n').Append(it.Decision >= 2 ? "(not answered)" : "Answer: " + it.Text);
            return sb.ToString();
        }

        public ChatItem PendingQuestion()
        {
            for (int i = Items.Count - 1; i >= 0; i--) if (Items[i].Kind == ChatKind.Question && Items[i].Decision < 0) return Items[i];
            return null;
        }

        public void AnswerQuestion(ChatItem it, UserAnswer a)
        {
            if (it == null || it.Kind != ChatKind.Question || it.Decision >= 0 || a == null) return;
            UserQuestion q = it.Question;
            if (q != null && q.Wait)
            {
                string host = q.Host.Length > 0 ? " on " + q.Host : "";
                if (a.Cancelled) { it.Decision = 3; it.Text = "Stopped waiting for the check" + host; }
                else if (a.Choice == 1) { it.Decision = 2; it.Text = "Skipped the check" + host; }
                else { it.Decision = a.Auto ? 1 : 0; it.Text = (a.Auto ? "Check completed" : "Finished the check") + host; }
            }
            else if (a.Cancelled) { it.Decision = 3; it.Text = "not answered"; }
            else
            {
                it.Decision = 0;
                string t = q != null && a.Choice >= 0 && a.Choice < q.Options.Count ? q.Options[a.Choice] : a.Text ?? "";
                t = t.Replace("\r", "").Replace('\n', ' ').Trim();
                it.Text = t.Length > 300 ? t.Substring(0, 300) + "..." : t;
            }
            Changed(it);
            if (it.Answered != null) it.Answered(a);
        }

        readonly Dictionary<ImageData, Image> thumbs = new Dictionary<ImageData, Image>();

        Image Thumb(ImageData d, int size)
        {
            Image img;
            if (d == null) return null;
            if (thumbs.TryGetValue(d, out img) && (img == null || img.Width == size)) return img;
            if (img != null) img.Dispose();
            img = Attachments.Cover(d, size, size);
            thumbs[d] = img;
            return img;
        }

        int PictureTile { get { return UI.S(88); } }
        int FileTileH { get { return UI.S(48); } }
        int TileGap { get { return UI.S(6); } }

        int MeasureTray(Graphics g, ChatItem it, int w)
        {
            it.Tiles = null;
            it.TrayH = 0;
            if (it.Attachments == null || it.Attachments.Count == 0) return 0;
            int maxW = Math.Max(UI.S(120), (int)(w * 0.82));
            List<Rectangle> tiles = new List<Rectangle>();
            List<int> widths = new List<int>();
            foreach (Attachment a in it.Attachments)
            {
                tiles.Add(Rectangle.Empty);
                widths.Add(a.Picture ? PictureTile : Attachments.FileWidth(g, a, fonts.Body, fonts.Small, 0, maxW));
            }
            int y = 0;
            foreach (bool pictures in new bool[] { true, false })
            {
                List<int> row = new List<int>();
                int rowW = 0, rowH = pictures ? PictureTile : FileTileH;
                for (int i = 0; i <= it.Attachments.Count; i++)
                {
                    bool end = i == it.Attachments.Count;
                    if (!end && it.Attachments[i].Picture != pictures) continue;
                    if (row.Count > 0 && (end || rowW + TileGap + widths[i] > maxW))
                    {
                        int x = w - rowW;
                        foreach (int k in row) { tiles[k] = new Rectangle(x, y, widths[k], rowH); x += widths[k] + TileGap; }
                        y += rowH + TileGap;
                        row.Clear();
                        rowW = 0;
                    }
                    if (end) break;
                    rowW += (row.Count > 0 ? TileGap : 0) + widths[i];
                    row.Add(i);
                }
            }
            it.Tiles = tiles;
            it.TrayH = y;
            return y;
        }

        void PaintTray(Graphics g, ChatItem it, int idx, int x, int y)
        {
            if (it.Tiles == null || it.Attachments == null) return;
            for (int i = 0; i < it.Tiles.Count && i < it.Attachments.Count; i++)
            {
                Attachment a = it.Attachments[i];
                Rectangle r = new Rectangle(x + it.Tiles[i].X, y + it.Tiles[i].Y, it.Tiles[i].Width, it.Tiles[i].Height);
                if (r.IsEmpty) continue;
                bool hot = hoverItem == idx && hoverButton == BtnAttach + i;
                if (a.Picture)
                {
                    Attachments.DrawPicture(g, th, r, Thumb(a.Shown, r.Width));
                    if (hot) Glyphs.DrawRound(g, th.Fore, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), UI.S(10));
                }
                else Attachments.DrawFile(g, th, r, a, fonts.Body, fonts.Small, hot ? Theme.Blend(UserBack, th.Fore, th.Dark ? 0.08 : 0.05) : UserBack, 0);
                SetButton(it, r, BtnAttach + i, y);
            }
        }

        Rectangle Track() { return new Rectangle(ClientSize.Width - UI.S(10), 0, UI.S(10), ClientSize.Height); }

        Rectangle ThumbRect()
        {
            Rectangle t = Track();
            int view = ClientSize.Height;
            if (contentH <= view) return Rectangle.Empty;
            int len = Math.Max(UI.S(28), (int)((long)t.Height * view / contentH));
            int max = contentH - view;
            int y = (int)((long)(t.Height - len) * scrollY / Math.Max(1, max));
            return new Rectangle(t.X, y, t.Width, len);
        }

        void PaintScrollbar(Graphics g)
        {
            Rectangle r = ThumbRect();
            if (r.IsEmpty) return;
            int inset = sbHover || sbDrag ? UI.S(2) : UI.S(3);
            Glyphs.FillRound(g, sbHover || sbDrag ? th.ThumbHover : th.Thumb, new Rectangle(r.X + inset, r.Y + 2, r.Width - inset * 2, r.Height - 4), UI.S(4));
        }

        void PaintJump(Graphics g)
        {
            jumpRect = Rectangle.Empty;
            int max = Math.Max(0, contentH - ClientSize.Height);
            if (scrollY >= max - UI.S(60)) return;
            int s = UI.S(32);
            int right = ContentX + ContentW + UI.S(16), room = ClientSize.Width - UI.S(14) - right;
            int x = room >= s ? right + Math.Min(room - s, UI.S(8)) : ContentX + ContentW - s;
            jumpRect = new Rectangle(x, ClientSize.Height - s - UI.S(10), s, s);
            Glyphs.FillRound(g, jumpHover ? th.ButtonHover : th.Back, jumpRect, s / 2);
            Glyphs.DrawRound(g, th.InputBorder, new Rectangle(jumpRect.X, jumpRect.Y, jumpRect.Width - 1, jumpRect.Height - 1), s / 2);
            Glyphs.Draw("arrowdown", g, jumpRect, th.Fore);
        }

        public void ScrollBy(int dy)
        {
            int max = Math.Max(0, contentH - ClientSize.Height);
            scrollY = Math.Max(0, Math.Min(max, scrollY + dy));
            stick = scrollY >= max - UI.S(4);
            PositionEditor();
            Invalidate();
            RefreshHover();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0)
            {
                int n = zoomWheel.Notches(e.Delta);
                if (n == 0) return;
                SetFonts(fontSize + n * 0.5f, monoName);
                if (FontsChanged != null) FontsChanged(fontSize);
                return;
            }
            int px = wheel.Take(e.Delta, fonts.LineH * 3);
            if (px != 0) ScrollBy(-px);
        }

        readonly WheelAcc wheel = new WheelAcc(), zoomWheel = new WheelAcc();

        public void Zoom(int dir)
        {
            float size = dir == 0 ? 10f : fontSize + dir * 0.5f;
            SetFonts(size, monoName);
            if (FontsChanged != null) FontsChanged(fontSize);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int page = Math.Max(fonts.LineH, ClientSize.Height - fonts.LineH * 2);
            switch (e.KeyCode)
            {
                case Keys.Up: ScrollBy(-fonts.LineH * 2); e.Handled = true; break;
                case Keys.Down: ScrollBy(fonts.LineH * 2); e.Handled = true; break;
                case Keys.PageUp: ScrollBy(-page); e.Handled = true; break;
                case Keys.PageDown: ScrollBy(page); e.Handled = true; break;
                case Keys.Home: ScrollBy(-contentH); e.Handled = true; break;
                case Keys.End: ScrollToEnd(); e.Handled = true; break;
                case Keys.C:
                case Keys.Insert:
                    if (e.Modifiers == Keys.Control && hasSel) { SetClipboard(SelectedText()); e.Handled = e.SuppressKeyPress = true; }
                    break;
                case Keys.A:
                    if (e.Modifiers == Keys.Control) { SelectAll(); e.Handled = e.SuppressKeyPress = true; }
                    break;
                case Keys.Escape:
                    if (hasSel) { ClearSelection(); e.Handled = true; }
                    break;
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (e.Handled || char.IsControl(e.KeyChar)) return;
            Keys mods = ModifierKeys & (Keys.Control | Keys.Alt);
            if (mods == Keys.Control || mods == Keys.Alt) return;
            if (editing != null && editBox.Visible)
            {
                editBox.Focus();
                editBox.SelectedText = e.KeyChar.ToString();
                e.Handled = true;
                return;
            }
            if (TypeAhead != null) { TypeAhead(e.KeyChar); e.Handled = true; }
        }

        public void CopyLastAnswer()
        {
            for (int i = Items.Count - 1; i >= 0; i--)
                if (Items[i].Kind == ChatKind.Assistant && Items[i].Text.Trim().Length > 0) { SetClipboard(Items[i].Text.Trim()); return; }
        }

        static void SetClipboard(string s)
        {
            try { if (!string.IsNullOrEmpty(s)) Clipboard.SetText(s); } catch { }
        }

        int ItemAt(Point p)
        {
            int y = p.Y + scrollY;
            for (int i = 0; i < Items.Count; i++)
            {
                ChatItem it = Items[i];
                if (it.Hidden) continue;
                if (y >= it.Top && y < it.Top + it.H + UI.S(2)) return i;
            }
            return -1;
        }

        Hit HitAt(Point p)
        {
            Hit h = new Hit();
            h.Item = h.Button = h.Copy = h.Sug = h.Act = -1;
            if (!ClientRectangle.Contains(p)) return h;
            h.Bar = !ThumbRect().IsEmpty && Track().Contains(p);
            h.Jump = !jumpRect.IsEmpty && jumpRect.Contains(p);
            if (Items.Count == 0)
            {
                for (int i = 0; i < suggestionRects.Count; i++) if (suggestionRects[i].Contains(p)) h.Sug = i;
                for (int i = 0; i < actionRects.Count; i++) if (actionRects[i].Contains(p)) h.Act = i;
                return h;
            }
            if (h.Bar || h.Jump) return h;
            h.Item = ItemAt(p);
            if (h.Item < 0) return h;
            ChatItem it = Items[h.Item];
            int ix = ItemX(it);
            Point local = new Point(p.X - ix, p.Y + scrollY - it.Top);
            foreach (KeyValuePair<Rectangle, int> kv in it.Buttons)
                if (!kv.Key.IsEmpty && kv.Key.Contains(p.X, p.Y + scrollY - it.Top)) h.Button = kv.Value;
            if (h.Button < 0 && it.Kind == ChatKind.Activity && it.Members != null && it.Members.Count > 0) h.Button = BtnExpand;
            if (h.Button < 0 && (it.Kind == ChatKind.Thinking || it.Kind == ChatKind.Tool) && local.Y < fonts.LineH + UI.S(8))
            {
                if (it.Kind == ChatKind.Thinking ? (!it.Streaming && it.Text.Trim().Length > 0) : Expandable(it)) h.Button = BtnExpand;
            }
            if (h.Button < 0 && it.Md != null && it.MdOy >= 0)
            {
                Point mp = new Point(local.X - it.MdOx, local.Y - it.MdOy);
                DrawOp op = MdRenderer.Hit(it.Md, mp);
                if (op != null)
                {
                    if (op.Kind == DrawOp.KCopy) h.Copy = op.Tag;
                    else { h.Link = op.Link; h.LinkText = op.Text; }
                }
            }
            return h;
        }

        void ApplyHover(Hit h)
        {
            int item = h.Item, button = h.Button, copy = h.Copy, sug = h.Sug, act = h.Act;
            bool changed = item != hoverItem || button != hoverButton || copy != hoverCopy || h.Link != hoverLink || h.Bar != sbHover || sug != hoverSuggestion || act != hoverAction || h.Jump != jumpHover;
            hoverItem = item; hoverButton = button; hoverCopy = copy; hoverLink = h.Link; sbHover = h.Bar; hoverSuggestion = sug; hoverAction = act; jumpHover = h.Jump;
            Cursor = button == BtnAnswer ? Cursors.IBeam : (button >= 0 || copy >= 0 || h.Link != null || sug >= 0 || act >= 0 || h.Jump) ? Cursors.Hand : TextUnder(h) ? Cursors.IBeam : Cursors.Default;
            string want = h.Link ?? ButtonTip(h.Button) ?? AttachmentTip(h);
            if (want != tipLink)
            {
                tipLink = want;
                tip.Hide(this);
                tip.SetToolTip(this, want);
            }
            if (changed) Invalidate();
        }

        string AttachmentTip(Hit h)
        {
            Attachment a = AttachmentAt(h);
            return a != null ? Attachments.Tip(a) : null;
        }

        Attachment AttachmentAt(Hit h)
        {
            if (h.Button < BtnAttach || h.Item < 0 || h.Item >= Items.Count) return null;
            List<Attachment> l = Items[h.Item].Attachments;
            int i = h.Button - BtnAttach;
            return l != null && i < l.Count ? l[i] : null;
        }

        static string ButtonTip(int button)
        {
            switch (button)
            {
                case BtnUserCopy: return "Copy";
                case BtnUserEdit: return "Edit";
                case BtnUserDelete: return "Delete this message and its reply";
                case BtnRetry: return "Run this message again";
                case BtnQueueEdit: return "Edit this message before the agent reads it";
                case BtnQueueRemove: return "Remove it from the queue";
            }
            return null;
        }

        bool TextUnder(Hit h)
        {
            if (h.Item < 0 || h.Bar || h.Jump) return false;
            ChatItem it = Items[h.Item];
            return it.Md != null && it.MdOy >= 0 && (it.Kind == ChatKind.Assistant || it.Kind == ChatKind.User || it.Kind == ChatKind.Thinking || it.Kind == ChatKind.Tool || it.Kind == ChatKind.Permission);
        }

        void RefreshHover()
        {
            if (!IsHandleCreated || selecting || sbDrag) return;
            Point p = PointToClient(Cursor.Position);
            if (!ClientRectangle.Contains(p)) return;
            ApplyHover(HitAt(p));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (sbDrag)
            {
                Rectangle t = Track();
                Rectangle th0 = ThumbRect();
                int max = Math.Max(1, contentH - ClientSize.Height);
                int range = Math.Max(1, t.Height - th0.Height);
                scrollY = Math.Max(0, Math.Min(max, (int)((long)(e.Y - dragOff) * max / range)));
                stick = scrollY >= max - UI.S(4);
                PositionEditor();
                Invalidate();
                return;
            }
            if (maySelect && (e.Button & MouseButtons.Left) != 0)
            {
                if (!selecting && (Math.Abs(e.X - downPt.X) > UI.S(3) || Math.Abs(e.Y - downPt.Y) > UI.S(3)))
                {
                    selecting = true;
                    hasSel = true;
                    selA = anchor;
                    selB = anchor;
                    Capture = true;
                    dragScroll.Start();
                }
                if (selecting) { ExtendSelection(e.Location); return; }
            }
            ApplyHover(HitAt(e.Location));
        }

        void ExtendSelection(Point p)
        {
            SelPos pos;
            if (PosAt(p, out pos)) { selB = pos; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (selecting) return;
            if (hoverItem >= 0 || hoverButton >= 0 || hoverLink != null || sbHover || hoverSuggestion >= 0 || hoverAction >= 0 || jumpHover)
            {
                hoverItem = hoverButton = hoverCopy = hoverSuggestion = hoverAction = -1;
                hoverLink = null;
                sbHover = jumpHover = false;
                Invalidate();
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            pressed = false;
            maySelect = false;
            Hit h = HitAt(e.Location);
            bool inEditor = editing != null && h.Item >= 0 && h.Item < Items.Count && Items[h.Item] == editing && h.Button != BtnEditCancel && h.Button != BtnEditSend;
            if (inEditor) { if (editBox.Visible) editBox.Focus(); return; }
            if (!Focused) Focus();
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            if (e.Button == MouseButtons.Left)
            {
                Rectangle thumb = ThumbRect();
                if (h.Bar)
                {
                    if (thumb.Contains(e.Location)) { sbDrag = true; dragOff = e.Y - thumb.Y; Capture = true; }
                    else ScrollBy(e.Y < thumb.Y ? -ClientSize.Height : ClientSize.Height);
                    return;
                }
                SelPos pos;
                bool interactive = h.Button >= 0 || h.Copy >= 0 || h.Sug >= 0 || h.Act >= 0 || h.Jump;
                if (!interactive && PosAt(e.Location, out pos))
                {
                    if (e.Clicks == 2) { SelectWord(pos); return; }
                    anchor = pos;
                    maySelect = true;
                    downPt = e.Location;
                }
                if (hasSel && e.Clicks == 1 && (ModifierKeys & Keys.Shift) == 0) ClearSelection();
                else if (hasSel && (ModifierKeys & Keys.Shift) != 0 && maySelect) { selB = anchor; anchor = selA; Invalidate(); }
            }
            pressHit = h;
            pressed = true;
        }

        static bool SameTarget(Hit a, Hit b)
        {
            if (a.Sug >= 0 || b.Sug >= 0) return a.Sug == b.Sug;
            if (a.Act >= 0 || b.Act >= 0) return a.Act == b.Act;
            if (a.Jump || b.Jump) return a.Jump == b.Jump;
            if (a.Item != b.Item) return false;
            if (a.Copy >= 0 || b.Copy >= 0) return a.Copy == b.Copy;
            if (a.Link != null || b.Link != null) return a.Link == b.Link;
            return a.Button == b.Button;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragScroll.Stop();
            if (sbDrag) { sbDrag = false; Capture = false; Invalidate(); return; }
            bool wasSelecting = selecting;
            selecting = false;
            maySelect = false;
            if (wasSelecting) { Capture = false; ApplyHover(HitAt(e.Location)); return; }
            if (!pressed) return;
            pressed = false;
            Hit h = HitAt(e.Location);
            ApplyHover(h);
            if (!SameTarget(h, pressHit)) return;
            if (e.Button == MouseButtons.Right) { ShowMenu(e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            if (h.Jump) { ScrollToEnd(); return; }
            if (Items.Count == 0)
            {
                if (h.Sug >= 0 && h.Sug < Suggestions.Count && SuggestionClicked != null) SuggestionClicked(Suggestions[h.Sug].Value);
                else if (h.Act >= 0 && h.Act < EmptyActions.Count && EmptyActions[h.Act].Value != null) EmptyActions[h.Act].Value();
                return;
            }
            if (h.Item < 0 || h.Item >= Items.Count) return;
            ChatItem it = Items[h.Item];
            if (h.Copy >= 0 && it.Md != null && h.Copy < it.Md.CodeBlocks.Count)
            {
                SetClipboard(it.Md.CodeBlocks[h.Copy]);
                copiedItem = h.Item;
                copiedBlock = h.Copy;
                Invalidate();
                Flash(delegate { copiedBlock = -1; copiedItem = -1; });
                return;
            }
            if (h.Link != null) { OpenLink(h.Link, h.LinkText); return; }
            Attachment att = AttachmentAt(h);
            if (att != null) { if (AttachmentClicked != null) AttachmentClicked(it, att); return; }
            switch (h.Button)
            {
                case BtnExpand:
                    it.Expanded = !it.Expanded;
                    bool wasStick = stick;
                    stick = false;
                    Changed(it);
                    stick = wasStick && it.Top + it.H >= contentH - UI.S(40);
                    RefreshHover();
                    break;
                case BtnAllow: Resolve(it, PermissionDecision.Allow); break;
                case BtnAlways: Resolve(it, PermissionDecision.AllowAlways); break;
                case BtnDeny: Resolve(it, PermissionDecision.Deny); break;
                case BtnAction: if (it.Action != null) it.Action(); break;
                case BtnUndo: if (it.Action != null && !it.Undone) it.Action(); break;
                case BtnCopyMsg:
                case BtnUserCopy:
                    SetClipboard(it.Text.Trim());
                    copiedMsg = h.Item;
                    Invalidate();
                    Flash(delegate { copiedMsg = -1; });
                    break;
                case BtnUserEdit: BeginEdit(it); break;
                case BtnUserDelete: if (DeleteRequested != null && Changeable) DeleteRequested(it); break;
                case BtnRetry: if (RetryRequested != null && Changeable && LastAnswer(it)) RetryRequested(it); break;
                case BtnEditCancel: CancelEdit(); break;
                case BtnEditSend: SubmitEdit(); break;
                case BtnAnswer: if (answering == it && answerBox.Visible) answerBox.Focus(); break;
                case BtnAnswerSend: if (answering == it) SubmitAnswer(); break;
                case BtnQueueEdit: if (it.Queued && QueueEditRequested != null) QueueEditRequested(it); break;
                case BtnQueueRemove: if (it.Queued && QueueRemoveRequested != null) QueueRemoveRequested(it); break;
                default:
                    if (h.Button >= BtnOption && it.Kind == ChatKind.Question && it.Question != null && it.Decision < 0)
                    {
                        UserAnswer a = new UserAnswer();
                        a.Choice = h.Button - BtnOption;
                        if (a.Choice < it.Question.Options.Count) AnswerQuestion(it, a);
                    }
                    break;
            }
        }

        void Flash(Action done)
        {
            Timer t = new Timer();
            t.Interval = 1500;
            t.Tick += delegate { t.Stop(); t.Dispose(); done(); Invalidate(); };
            t.Start();
        }

        void OpenLink(string url, string text)
        {
            string shown = (text ?? "").Trim();
            string realHost = HostOf(url), shownHost = shown.IndexOf(' ') < 0 && shown.IndexOf('.') > 0 ? HostOf(shown.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? shown : "https://" + shown) : null;
            if (realHost != null && shownHost != null && !string.Equals(realHost, shownHost, StringComparison.OrdinalIgnoreCase) &&
                !realHost.EndsWith("." + shownHost, StringComparison.OrdinalIgnoreCase))
            {
                if (!Alert.Confirm(FindForm(), th, "This link shows \u201C" + shown + "\u201D but opens a different site:\n\n" + url + "\n\nOpen it anyway?", Program.AppName, "Open anyway", true)) return;
            }
            if (LinkClicked != null) LinkClicked(url);
        }

        static string HostOf(string url)
        {
            try
            {
                Uri u;
                if (Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https")) return u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
            }
            catch { }
            return null;
        }

        public void Resolve(ChatItem it, PermissionDecision d)
        {
            if (it.Kind != ChatKind.Permission || it.Decision >= 0) return;
            it.Decision = (int)d;
            Changed(it);
            if (it.Decide != null) it.Decide(d);
        }

        public ChatItem PendingPermission()
        {
            for (int i = Items.Count - 1; i >= 0; i--) if (Items[i].Kind == ChatKind.Permission && Items[i].Decision < 0) return Items[i];
            return null;
        }

        bool PosAt(Point p, out SelPos pos)
        {
            pos = new SelPos(-1, 0, 0);
            int yAbs = p.Y + scrollY;
            int best = -1, bestDist = int.MaxValue;
            for (int i = 0; i < Items.Count; i++)
            {
                ChatItem it = Items[i];
                if (it.Hidden || it.Md == null || it.MdOy < 0 || !HasText(it.Md)) continue;
                int top = it.Top + it.MdOy, bottom = top + it.Md.Height;
                int d = yAbs < top ? top - yAbs : yAbs > bottom ? yAbs - bottom : 0;
                if (d < bestDist) { bestDist = d; best = i; }
                if (d == 0) break;
            }
            if (best < 0) return false;
            ChatItem bi = Items[best];
            int lx = p.X - (ItemX(bi) + bi.MdOx), ly = yAbs - (bi.Top + bi.MdOy);
            int bestOp = -1;
            long bestScore = long.MaxValue;
            for (int k = 0; k < bi.Md.Ops.Count; k++)
            {
                DrawOp op = bi.Md.Ops[k];
                if (!MdRenderer.Selectable(op)) continue;
                Rectangle r = op.R;
                int dy = ly < r.Top ? r.Top - ly : ly >= r.Bottom ? ly - r.Bottom + 1 : 0;
                int dx = lx < r.Left ? r.Left - lx : lx > r.Right ? lx - r.Right : 0;
                long score = (long)dy * 100000 + dx;
                if (score < bestScore) { bestScore = score; bestOp = k; }
            }
            if (bestOp < 0) return false;
            DrawOp o = bi.Md.Ops[bestOp];
            int ch;
            if (o.Kind == DrawOp.KMath) ch = lx < o.R.Left + o.R.Width / 2 ? 0 : o.Text.Length;
            else if (lx <= o.R.Left) ch = 0;
            else if (lx >= o.R.Right) ch = o.Text.Length;
            else
            {
                using (Graphics g = CreateGraphics())
                {
                    int lo = 0, hi = o.Text.Length;
                    while (lo < hi)
                    {
                        int mid = (lo + hi + 1) / 2;
                        int w0 = fonts.Width(g, o.Text.Substring(0, mid - 1), o.Font, false);
                        int w1 = fonts.Width(g, o.Text.Substring(0, mid), o.Font, false);
                        if (o.R.Left + (w0 + w1) / 2 <= lx) lo = mid; else hi = mid - 1;
                    }
                    ch = lo;
                }
            }
            pos = new SelPos(best, bestOp, ch);
            return true;
        }

        static bool HasText(MdLayout L)
        {
            foreach (DrawOp op in L.Ops) if (MdRenderer.Selectable(op)) return true;
            return false;
        }

        void Ordered(out SelPos s, out SelPos e)
        {
            bool before = selA.Item < selB.Item || (selA.Item == selB.Item && (selA.Op < selB.Op || (selA.Op == selB.Op && selA.Ch <= selB.Ch)));
            s = before ? selA : selB;
            e = before ? selB : selA;
        }

        void ClearSelection()
        {
            if (!hasSel && !selecting) return;
            hasSel = false;
            selecting = false;
            Invalidate();
        }

        public bool HasSelection { get { return hasSel; } }

        void SelectWord(SelPos p)
        {
            ChatItem it = Items[p.Item];
            DrawOp op = it.Md.Ops[p.Op];
            string t = op.Text;
            int a = Math.Min(p.Ch, t.Length), b = a;
            Func<char, bool> word = delegate (char c) { return char.IsLetterOrDigit(c) || c == '_'; };
            while (a > 0 && word(t[a - 1])) a--;
            while (b < t.Length && word(t[b])) b++;
            if (a == b && b < t.Length) b++;
            if (op.Kind == DrawOp.KMath) { a = 0; b = t.Length; }
            selA = new SelPos(p.Item, p.Op, a);
            selB = new SelPos(p.Item, p.Op, b);
            hasSel = true;
            Invalidate();
        }

        public void SelectAll()
        {
            int first = -1, last = -1;
            for (int i = 0; i < Items.Count; i++) if (!Items[i].Hidden && Items[i].Md != null && HasText(Items[i].Md)) { if (first < 0) first = i; last = i; }
            if (first < 0) return;
            MdLayout L = Items[last].Md;
            int lastOp = L.Ops.Count - 1;
            while (lastOp > 0 && !MdRenderer.Selectable(L.Ops[lastOp])) lastOp--;
            selA = new SelPos(first, 0, 0);
            selB = new SelPos(last, lastOp, L.Ops[lastOp].Text == null ? 0 : L.Ops[lastOp].Text.Length);
            hasSel = true;
            Invalidate();
        }

        string SelectedText()
        {
            if (!hasSel) return "";
            SelPos s, e;
            Ordered(out s, out e);
            StringBuilder sb = new StringBuilder();
            for (int i = s.Item; i <= e.Item && i < Items.Count; i++)
            {
                ChatItem it = Items[i];
                if (it.Hidden || it.Md == null) continue;
                DrawOp prev = null;
                bool first = true;
                int o0 = i == s.Item ? s.Op : 0, o1 = i == e.Item ? e.Op : it.Md.Ops.Count - 1;
                for (int k = Math.Max(0, o0); k <= o1 && k < it.Md.Ops.Count; k++)
                {
                    DrawOp op = it.Md.Ops[k];
                    if (!MdRenderer.Selectable(op)) continue;
                    int c0 = i == s.Item && k == s.Op ? s.Ch : 0;
                    int c1 = i == e.Item && k == e.Op ? e.Ch : op.Text.Length;
                    c0 = Math.Max(0, Math.Min(op.Text.Length, c0));
                    c1 = Math.Max(c0, Math.Min(op.Text.Length, c1));
                    if (sb.Length > 0)
                    {
                        if (first) sb.Append("\n\n");
                        else Separator(sb, prev, op);
                    }
                    sb.Append(op.Text, c0, c1 - c0);
                    prev = op;
                    first = false;
                }
            }
            return sb.ToString();
        }

        static bool EndsWithSpace(StringBuilder sb) { return sb.Length == 0 || char.IsWhiteSpace(sb[sb.Length - 1]); }

        void Separator(StringBuilder sb, DrawOp prev, DrawOp op)
        {
            switch (op.Brk)
            {
                case 1: if (!EndsWithSpace(sb)) sb.Append(' '); break;
                case 2: sb.Append('\n'); for (int b = 0; b < op.Blank; b++) sb.Append('\n'); break;
                case 3: sb.Append("\n\n"); break;
                case 4: break;
                case 6: sb.Append('\t'); break;
                default:
                    if (prev == null) break;
                    if (op.R.Y != prev.R.Y) { if (!EndsWithSpace(sb)) sb.Append(' '); break; }
                    int gap = op.R.X - prev.R.Right;
                    int need = op.Kind == DrawOp.KCodeSpan || prev.Kind == DrawOp.KCodeSpan ? UI.S(5) : UI.S(2);
                    if (gap > need && !EndsWithSpace(sb) && op.Text.Length > 0 && !char.IsWhiteSpace(op.Text[0])) sb.Append(' ');
                    break;
            }
        }

        void ShowMenu(Point p)
        {
            int i = ItemAt(p);
            ContextMenuStrip m = Menus.Create(th);
            m.ShowCheckMargin = false;
            if (hasSel)
            {
                string sel = SelectedText();
                m.Items.Add(Menus.Item("Copy", false, delegate { SetClipboard(sel); }));
                m.Items.Add(new ToolStripSeparator());
            }
            if (i >= 0)
            {
                ChatItem it = Items[i];
                string text = it.Kind == ChatKind.Tool ? (it.Label + (it.Summary.Length > 0 ? " - " + it.Summary : "") + (it.Detail.Length > 0 ? "\n" + it.Detail : it.Live.Length > 0 ? "\n" + it.Live : "") + (it.Report.Length > 0 ? "\n\n" + it.Report : ""))
                            : it.Kind == ChatKind.Permission ? it.Ask.Title + (string.IsNullOrEmpty(it.Ask.Summary) ? "" : "\n" + it.Ask.Summary) + (string.IsNullOrEmpty(it.Ask.Reading) ? "" : "\n" + it.Ask.Reading) + "\n" + it.Ask.Detail
                            : it.Kind == ChatKind.Question ? QuestionText(it)
                            : it.Kind == ChatKind.Activity ? Summary(it) : it.Text;
                string what = it.Kind == ChatKind.Assistant ? "Copy this reply (Markdown)" : it.Kind == ChatKind.User ? "Copy this message" : it.Kind == ChatKind.Error ? "Copy the error" : hasSel ? "Copy all of it" : "Copy";
                if (!string.IsNullOrEmpty(text)) m.Items.Add(Menus.Item(what, false, delegate { SetClipboard(text); }));
                if (it.Kind == ChatKind.User && it.Queued && editing != it)
                {
                    ChatItem target = it;
                    if (QueueEditRequested != null) m.Items.Add(Menus.Item("Edit before the agent reads it", false, delegate { if (target.Queued) QueueEditRequested(target); }));
                    if (QueueRemoveRequested != null) m.Items.Add(Menus.Item("Remove from the queue", false, delegate { if (target.Queued) QueueRemoveRequested(target); }));
                }
                if (UserActions(it) && Changeable && editing != it)
                {
                    ChatItem target = it;
                    m.Items.Add(Menus.Item("Edit this message", false, delegate { BeginEdit(target); }));
                    if (DeleteRequested != null) m.Items.Add(Menus.Item("Delete this message", false, delegate { if (Changeable) DeleteRequested(target); }));
                }
                if (it.Kind == ChatKind.Assistant && RetryRequested != null && LastAnswer(it) && Changeable && editing == null)
                {
                    ChatItem target = it;
                    m.Items.Add(Menus.Item("Retry", false, delegate { if (Changeable) RetryRequested(target); }));
                }
                if (it.Kind == ChatKind.Assistant && it.Md != null && it.Md.CodeBlocks.Count > 0)
                {
                    List<string> blocks = it.Md.CodeBlocks;
                    m.Items.Add(Menus.Item(blocks.Count == 1 ? "Copy the code" : "Copy all code blocks", false, delegate { SetClipboard(string.Join("\n\n", blocks.ToArray())); }));
                }
            }
            if (m.Items.Count > 0 && !(m.Items[m.Items.Count - 1] is ToolStripSeparator)) m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Menus.Item("Select all", false, delegate { SelectAll(); }));
            m.Items.Add(Menus.Item("Copy the whole conversation", false, delegate { SetClipboard(Transcript()); }));
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(this, p);
        }

        public string Transcript()
        {
            StringBuilder sb = new StringBuilder();
            foreach (ChatItem it in Items)
            {
                switch (it.Kind)
                {
                    case ChatKind.User:
                        sb.Append("## You\n\n");
                        if (it.Text.Trim().Length > 0) sb.Append(it.Text.Trim()).Append("\n\n");
                        if (it.Attachments != null && it.Attachments.Count > 0)
                        {
                            List<string> names = new List<string>();
                            foreach (Attachment a in it.Attachments) names.Add(a.Name);
                            sb.Append("Attached: ").Append(string.Join(", ", names.ToArray())).Append("\n\n");
                        }
                        break;
                    case ChatKind.Assistant: if (it.Text.Trim().Length > 0) sb.Append("## Assistant\n\n").Append(it.Text.Trim()).Append("\n\n"); break;
                    case ChatKind.Tool: sb.Append(it.Under != null ? "  - " : "- ").Append(it.Label).Append(it.Summary.Length > 0 ? " (" + it.Summary + ")" : "").Append('\n'); break;
                    case ChatKind.Question: if (it.Decision >= 0) sb.Append("> ").Append(QuestionText(it).Replace("\n", "\n> ")).Append("\n\n"); break;
                    case ChatKind.Notice: case ChatKind.Error: sb.Append("> ").Append(it.Text.Replace("\n", "\n> ")).Append("\n\n"); break;
                }
            }
            return sb.ToString();
        }
    }
}
