using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MinGent
{
    interface IAssistantHost
    {
        IEditorBridge Editor { get; }
        BrowserPane Browser { get; }
        string ActiveContext(out string label);
        string WorkspaceFolder(out bool isDefault);
        void RunStarted(int runId);
        int RunChangeCount(int runId, RunChangeLog log);
        string UndoRun(int runId, RunChangeLog log, out int reverted, out int kept);
        void ShowSettings();
        void ShowSettings(string page);
        void ShowModelLibrary();
        void ShowBrowser();
        void OpenLink(string target);
        void RevealAssistant();
        void ResetBrowser();
        void ClearBrowserHistory();
        List<string> UnsavedNotes(string chatKey);
    }

    class AiPanel : Control
    {
        readonly Settings settings;
        readonly IAssistantHost host;
        Theme th = Theme.Light(Color.SteelBlue);

        readonly FlatButton bTemp = new FlatButton("", "temp", true);
        readonly SlashList slash = new SlashList();
        bool slashOff;
        readonly FlatButton bNotes = new FlatButton("", "notes", false);
        readonly FlatButton bBrowser = new FlatButton("", "globe", false);
        readonly FlatButton bMore = new FlatButton("", "more", false);
        readonly FlatButton bClose = new FlatButton("", "x", false);
        readonly ChatView chat = new ChatView();
        readonly TodoStrip todo = new TodoStrip();
        readonly Composer composer = new Composer();
        readonly ThemedTip tip = new ThemedTip();
        readonly System.Windows.Forms.Timer pump = new System.Windows.Forms.Timer();
        ModelChip modelChip { get { return composer.Model; } }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) PerformLayout();
        }

        CatalogSnapshot catalog;
        volatile bool refreshing;
        DateTime lastRefresh = DateTime.MinValue;
        ContextMenuStrip modelMenu;
        AgentSession session = new AgentSession();
        string chatFile;
        string chatTitle = "";
        bool titleSet;
        int runCounter, curTurn, turnCounter;
        volatile int liveRun = -1;
        CancelToken cancel;
        RunChangeLog runLog;
        bool running;
        ChatItem curAssistant, curThinking, curActivity;
        DateTime thinkingStart;
        readonly object qGate = new object();
        readonly List<UiEvent> queue = new List<UiEvent>();
        readonly Dictionary<string, ChatItem> toolItems = new Dictionary<string, ChatItem>();
        readonly Dictionary<string, KeyValuePair<DateTime, string>> titleCache = new Dictionary<string, KeyValuePair<DateTime, string>>(StringComparer.OrdinalIgnoreCase);
        Action afterStop;

        public event EventHandler NotesClicked, BrowserClicked, ChatsChanged, StateChanged;

        class UiEvent
        {
            public int Run;
            public int Kind;
            public StringBuilder Text;
            public Action Act;
        }

        public AiPanel(Settings s, IAssistantHost host)
        {
            settings = s;
            this.host = host;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = UI.Font;
            Controls.AddRange(new Control[] { slash, composer, todo, bTemp, bNotes, bBrowser, bMore, bClose, chat });
            slash.Picked += delegate (int i) { RunSlash(i); };
            composer.Box.TextChanged += delegate { UpdateSlash(); };
            composer.Box.LostFocus += delegate { HideSlash(); };
            composer.KeyFilter = SlashKey;
            tip.SetToolTip(bNotes, "Notes (Ctrl+Shift+E)");
            tip.SetToolTip(bBrowser, "Browser (Ctrl+Shift+B)");
            tip.SetToolTip(bMore, "More");
            bTemp.AccessibleName = "Temporary chat";
            bTemp.Click += delegate { bTemp.Checked = temporary; ToggleTemporary(); };
            UpdateTemporaryUi();
            tip.SetToolTip(bClose, "Close the agent (Ctrl+Shift+A opens it again)");
            bClose.AccessibleName = "Close the agent";
            bClose.Click += delegate { if (CloseClicked != null) CloseClicked(this, EventArgs.Empty); };
            bNotes.Click += delegate { if (NotesClicked != null) NotesClicked(this, EventArgs.Empty); };
            bBrowser.Click += delegate { if (BrowserClicked != null) BrowserClicked(this, EventArgs.Empty); };
            bMore.Click += delegate { ShowMore(); };
            composer.ModelClicked += delegate { ShowModelMenu(); };
            todo.Visible = false;
            todo.Toggled += delegate { PerformLayout(); };
            chat.LinkClicked += delegate (string url) { host.OpenLink(url); };
            chat.SuggestionClicked += delegate (string text) { UseStarter(text); };
            chat.FontsChanged += delegate (float size) { settings.ChatFontSize = size; };
            chat.TypeAhead += delegate (char c) { composer.FocusInput(); composer.Box.SelectedText = c.ToString(); };
            chat.EmptyChanged += delegate { PerformLayout(); Invalidate(); };
            chat.CanChange = delegate { return !running; };
            chat.EditSubmit = delegate (ChatItem it, string text) { return it.Queued ? SaveQueued(it, text) : EditTurn(it, text); };
            chat.EditCancelled += QueuedEditCancelled;
            chat.QueueEditRequested += EditQueued;
            chat.QueueRemoveRequested += RemoveQueued;
            chat.DeleteRequested += delegate (ChatItem it) { DeleteTurn(it); };
            chat.RetryRequested += delegate (ChatItem it) { RetryAnswer(it); };
            chat.EditEnded += delegate { composer.FocusInput(); };
            chat.MaxContent = UI.S(760);
            chat.Suggestions.Add(new KeyValuePair<string, string>("Build a web page", "Build a small web page about [topic] in a new folder, then open it in the browser so I can see it."));
            chat.Suggestions.Add(new KeyValuePair<string, string>("Research a topic", "Search the web for the latest on [topic] and give me a short summary with sources."));
            chat.Suggestions.Add(new KeyValuePair<string, string>("Write a note", "Write [what you need] and put it in a new note."));
            chat.Suggestions.Add(new KeyValuePair<string, string>("Fix some code", "Find and fix the bug in [file or folder]. Explain what was wrong."));
            chat.Suggestions.Add(new KeyValuePair<string, string>("Tidy a folder", "Look through [folder] and suggest a cleaner way to organize it. Don't move anything yet."));
            composer.SendRequested += delegate { Send(); };
            composer.StopRequested += delegate { Stop(); };
            composer.AutoChanged += delegate (bool on) { SetLevel(on ? "auto" : "ask"); };
            composer.LevelChosen += delegate (string lv) { SetLevel(lv); };
            composer.CustomizeAuto += delegate { host.ShowSettings("Safety"); };
            composer.PlanChanged += delegate (bool on) { SetPlan(on); };
            composer.EffortClicked += delegate { ShowEffortMenu(); };
            composer.MeterClicked += delegate { ShowContextMenu(); };
            composer.HeightChanged += delegate { PerformLayout(); };
            composer.ContextCleared += delegate { };
            composer.AttachClicked += delegate { Attach(); };
            composer.EditLastRequested += delegate
            {
                int at = queued.IndexOf(chat.EditingItem);
                if (queued.Count > 0) EditQueued(queued[at > 0 ? at - 1 : queued.Count - 1]);
                else if (!running) chat.BeginEdit(chat.LastUserItem());
            };
            composer.FilesDropped += delegate (string[] files) { DropFiles(files); };
            AllowDrop = true;
            composer.Refused += delegate (string why) { Notice(why, 1); };
            chat.AttachmentClicked += OpenAttachment;
            level = settings.AiMode == "auto" ? "auto" : "ask";
            composer.Level = level;
            session.Perms.Mode = level;
            session.Perms.WebGuard = settings.AiWebGuard;
            session.Perms.SetAutoAllows(settings.AutoAllowList);
            chat.SetFonts(settings.ChatFontSize, "Consolas");
            pump.Interval = 40;
            pump.Tick += delegate { Drain(); };
            pump.Start();
            UpdateEmptyState();
            UpdateModelChip();
            LoadLastChat();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { pump.Dispose(); tip.Dispose(); if (cancel != null) cancel.Cancel(); }
            base.Dispose(disposing);
        }

        public TextBox InputTextBox { get { return composer.Box; } }
        public bool Running { get { return running; } }
        public string ActiveChatPath { get { return chatFile; } }

        int conversation;
        readonly List<string> deletedChats = new List<string>();
        public event EventHandler NotesChatChanged;
        public int Conversation { get { return conversation; } }
        public string NotesKey { get { return temporary ? "~temp:" + conversation.ToString(CultureInfo.InvariantCulture) : chatFile != null ? Path.GetFileName(chatFile) : "~new"; } }

        public bool HasChat(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            try { return File.Exists(Path.Combine(ChatDir, name)) || File.Exists(Path.Combine(ArchiveDir, name)); } catch { return false; }
        }

        public List<string> TakeDeletedChats()
        {
            List<string> l = new List<string>(deletedChats);
            deletedChats.Clear();
            return l;
        }

        void NotesChanged()
        {
            if (NotesChatChanged != null) NotesChatChanged(this, EventArgs.Empty);
        }
        public string ChatTitle { get { return chatTitle ?? ""; } }

        public void SetTheme(Theme t)
        {
            th = t;
            tip.Th = t;
            BackColor = t.Back;
            foreach (FlatButton b in new FlatButton[] { bTemp, bNotes, bBrowser, bMore, bClose }) b.SetTheme(t);
            slash.SetTheme(t);
            chat.SetTheme(t);
            todo.SetTheme(t);
            composer.SetTheme(t);
            Invalidate(true);
        }

        public void SetPanes(bool notes, bool browser)
        {
            bNotes.Lit = notes;
            bBrowser.Lit = browser;
            tip.SetToolTip(bNotes, (notes ? "Hide notes" : "Notes") + " (Ctrl+Shift+E)");
            tip.SetToolTip(bBrowser, (browser ? "Hide the browser" : "Browser") + " (Ctrl+Shift+B)");
            bNotes.Invalidate();
            bBrowser.Invalidate();
        }

        public void SetEditorFont(Font f)
        {
            if (f != null) chat.SetFonts(settings.ChatFontSize, f.FontFamily.Name);
        }

        public void ZoomChat(int dir) { chat.Zoom(dir); }

        public void FocusInput() { composer.FocusInput(); }

        public void SetContextLabel(string label) { composer.ContextLabel = label; UpdateEmptyState(); }

        public void WorkspaceChanged() { UpdateEmptyState(); }

        int HeaderH { get { return UI.S(48); } }

        bool EmptyLayout { get { return chat.Items.Count == 0 && !running; } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int w = Width, h = Height;
            int hh = HeaderH, b = UI.S(30), y0 = UI.S(6);
            int x = w - UI.S(12) - b;
            bClose.SetBounds(x, y0, b, b); x -= b + UI.S(10);
            bMore.SetBounds(x, y0, b, b); x -= b + UI.S(4);
            bBrowser.SetBounds(x, y0, b, b); x -= b + UI.S(4);
            bNotes.SetBounds(x, y0, b, b); x -= b + UI.S(4);
            bTemp.SetBounds(x, y0, b, b);
            bTemp.Visible = temporary || EmptyLayout;
            int pad = UI.S(16);
            int colW = Math.Max(UI.S(120), Math.Min(UI.S(760), w - pad * 2 - UI.S(6)));
            int colX = Math.Max(pad, (w - colW) / 2);
            chat.MaxContent = colW;
            int ch = composer.PreferredHeight(colW);
            if (EmptyLayout)
            {
                todo.Visible = false;
                int above = UI.S(170), below = UI.S(140);
                int top = Math.Max(hh + above, (h - ch - above - below) / 2 + above);
                top = Math.Min(top, Math.Max(hh + UI.S(10), h - pad - ch));
                chat.SetBounds(0, hh, w, Math.Max(UI.S(40), h - hh));
                composer.SetBounds(colX, top, colW, ch);
                chat.EmptyAnchor = new Rectangle(colX, top - hh, colW, ch);
                chat.Invalidate();
                PlaceSlash();
                return;
            }
            chat.EmptyAnchor = Rectangle.Empty;
            int y = h - pad - ch;
            composer.SetBounds(colX, y, colW, ch);
            if (todo.HasItems)
            {
                todo.Visible = true;
                todo.Room = Math.Max(0, (h - hh - ch) * 35 / 100);
                int th2 = todo.PreferredHeight;
                y -= th2 + UI.S(8);
                todo.SetBounds(colX, y, colW, th2);
            }
            else todo.Visible = false;
            chat.SetBounds(0, hh, w, Math.Max(UI.S(40), y - hh - UI.S(6)));
            PlaceSlash();
        }

        void PlaceSlash()
        {
            if (!slash.Visible) return;
            int sh = slash.PreferredHeight;
            int sw = Math.Min(composer.Width - UI.S(16), slash.PreferredWidth);
            int sy = composer.Top - sh - UI.S(6);
            if (sy < HeaderH) sy = composer.Bottom + UI.S(6);
            slash.SetBounds(composer.Left + UI.S(8), sy, Math.Max(UI.S(120), sw), sh);
            slash.BringToFront();
        }

        void UpdateSlash()
        {
            string t = composer.Text;
            bool cmd = t.Length >= 1 && t.Length <= 24 && t[0] == '/' && t.IndexOfAny(new char[] { ' ', '\n', '\r', '\t' }) < 0;
            if (!cmd) slashOff = false;
            if (!cmd || slashOff || !composer.Box.Focused || composer.HasAttachments) { HideSlash(); return; }
            string keep = slash.Visible && slash.Selected >= 0 && slash.Selected < slash.Items.Count ? slash.Items[slash.Selected].Name : null;
            slash.Items.Clear();
            foreach (SlashCommand c in SlashCommands.All)
            {
                if (!c.Name.StartsWith(t, StringComparison.OrdinalIgnoreCase)) continue;
                SlashItem it = new SlashItem();
                it.Name = c.Name;
                it.Detail = c.Detail;
                if (c.Name == "/compact" && (running || session.UserTurns() < 2)) it.Note = running ? "after the agent finishes" : "nothing to summarize yet";
                if (c.Name == "/temp" && temporary && chat.Items.Count == 0) it.Note = "this chat is already temporary";
                slash.Items.Add(it);
            }
            if (slash.Items.Count == 0) { HideSlash(); return; }
            int sel = -1;
            for (int i = 0; i < slash.Items.Count; i++) if (slash.Items[i].Name == keep) sel = i;
            for (int i = 0; i < slash.Items.Count && sel < 0; i++) if (slash.Items[i].Note == null) sel = i;
            if (sel < 0) sel = 0;
            slash.Selected = sel;
            bool was = slash.Visible;
            slash.Visible = true;
            PlaceSlash();
            slash.Invalidate();
            if (!was) PerformLayout();
        }

        void HideSlash()
        {
            if (!slash.Visible) return;
            slash.Visible = false;
            slash.Items.Clear();
        }

        bool SlashKey(Keys k)
        {
            if (!slash.Visible || slash.Items.Count == 0) return false;
            switch (k)
            {
                case Keys.Up: slash.Step(-1); return true;
                case Keys.Down: slash.Step(1); return true;
                case Keys.Escape: slashOff = true; HideSlash(); return true;
                case Keys.Tab:
                    {
                        string name = slash.Items[Math.Max(0, slash.Selected)].Name;
                        composer.Text = name;
                        composer.MoveCaretToEnd();
                        return true;
                    }
                case Keys.Enter: RunSlash(slash.Selected); return true;
            }
            return false;
        }

        void RunSlash(int i)
        {
            if (i < 0 || i >= slash.Items.Count) return;
            string name = slash.Items[i].Name;
            HideSlash();
            composer.FocusInput();
            Slash(name);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(th.Back)) g.FillRectangle(b, ClientRectangle);
            string title = chatTitle ?? "";
            if (temporary && (title.Length == 0 || EmptyLayout)) title = "Temporary chat";
            string full = null;
            if (title.Length > 0 && (!EmptyLayout || temporary))
            {
                int right = (bTemp.Visible ? bTemp.Left : bNotes.Left) - UI.S(12);
                int room = Math.Max(UI.S(20), right - UI.S(20));
                using (Font f = new Font(UI.Font.FontFamily, UI.Font.Size * 1.04f, FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, title, f, new Rectangle(UI.S(20), UI.S(6), room, UI.S(30)), th.Fore,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    if (TextRenderer.MeasureText(g, title, f, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width > room) full = title;
                }
            }
            if (full != headerTip)
            {
                headerTip = full;
                tip.Hide(this);
                tip.SetToolTip(this, full);
            }
        }

        string headerTip;

        bool HasAnyModel()
        {
            if (catalog == null) return true;
            foreach (ModelEntry m in catalog.Models) if (m.Provider == "engine" ? Engine.Instance.Installed : true) return true;
            ProviderState ps = catalog.State("lmstudio");
            return ps != null && ps.Reachable;
        }

        void UpdateEmptyState()
        {
            chat.EmptyTitle = temporary ? "Temporary chat" : "What should we work on?";
            chat.EmptyActions.Clear();
            string where = "";
            bool def = true;
            try { where = host.WorkspaceFolder(out def); } catch { }
            if (!HasAnyModel())
            {
                chat.EmptyTitle = "Let's get you a model";
                chat.EmptyActions.Add(new KeyValuePair<string, Action>("Download a free model", delegate { host.ShowModelLibrary(); }));
                chat.EmptyActions.Add(new KeyValuePair<string, Action>("Use a cloud model", delegate { host.ShowSettings(); }));
                chat.EmptyText = "Models from the library run on this PC, so nothing leaves it. With an API key, Claude, Gemini or OpenAI models work too.";
            }
            else
            {
                string folder = where.Length > 0 ? ShortPath(where) : "";
                if (temporary)
                {
                    chat.EmptyText = "Not saved to your chats, and memory stays off. It's gone when you start a new chat or close MinGent.";
                    chat.Invalidate();
                    return;
                }
                chat.EmptyText = (folder.Length > 0 ? "Works in " + folder + (def ? "" : " (the open file's folder)") + "  \u00B7  " : "") +
                                 (composer.Plan ? "Plans first: researches, then waits for you to approve a plan" :
                                  level == "full" ? "Full auto: asks nothing" : level == "auto" ? "Auto: acts without asking, except for risky steps" : "Asks before it runs commands or changes things");
            }
            chat.Invalidate();
        }

        static string ShortPath(string p)
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
                if (home.Length > 3 && p.StartsWith(home, StringComparison.OrdinalIgnoreCase)) return "~" + p.Substring(home.Length);
            }
            catch { }
            return p;
        }

        void UseStarter(string text)
        {
            composer.Text = text;
            composer.FocusInput();
            int a = text.IndexOf('['), b = a >= 0 ? text.IndexOf(']', a) : -1;
            if (a >= 0 && b > a) composer.Box.Select(a, b - a + 1);
            else composer.MoveCaretToEnd();
        }

        void SetPlan(bool on)
        {
            if (composer.Plan != on) composer.Plan = on;
            session.Perms.Planning = on;
            UpdateEmptyState();
            if (on && !settings.AiPlanExplained)
            {
                settings.AiPlanExplained = true;
                settings.Save();
                Notice("Plan is on: the agent reads and researches, then shows you a plan for every task. Nothing changes until you approve it. With Plan off, the agent decides by itself when a job is big or risky enough to show you a plan first. Shift+Tab in the message box turns Plan on or off.", 0);
            }
        }

        string level = "ask";

        void SetAuto(bool on) { SetLevel(on ? "auto" : "ask"); }

        const string LocalFullAutoNote = "With a local model this is riskier: smaller models are easier for a web page to trick and make more mistakes. For Full auto, a frontier model such as Claude, GPT or Gemini is the safer choice.";

        bool LocalModelPicked()
        {
            ModelEntry m = SelectedEntry();
            if (m != null) return !m.Cloud;
            string r = settings.AiModel ?? "";
            int c = r.IndexOf(':');
            string p = c > 0 ? r.Substring(0, c) : "";
            return p == "engine" || p == "lmstudio";
        }

        void SetLevel(string lv)
        {
            if (lv != "auto" && lv != "full") lv = "ask";
            if (lv == "full" && level != "full" && !settings.AiFullExplained)
            {
                Form f = FindForm();
                bool yes = Alert.Ask(f, th, "Full auto asks you nothing. The agent can install software, delete files, change settings, press buy buttons and run any command without your OK, also when a web page tries to trick it.\n\n" +
                                            (LocalModelPicked() ? LocalFullAutoNote + "\n\n" : "") +
                                            "Its file tools still never write into Windows, Program Files or MinGent's own data, and it never sends your saved API keys to a website. Full auto goes back to Auto when you close MinGent.\n\nOnly use it for work you trust.",
                                     Program.AppName, "Turn on Full auto");
                if (!yes) { composer.Level = level; return; }
                settings.AiFullExplained = true;
            }
            string was = level;
            level = lv;
            composer.Level = lv;
            settings.AiMode = lv == "ask" ? "ask" : "auto";
            session.Perms.Mode = lv;
            settings.Save();
            UpdateEmptyState();
            if (lv == "auto" && was == "ask" && !settings.AiAutoExplained)
            {
                settings.AiAutoExplained = true;
                settings.Save();
                Notice("Auto is on: the agent edits files, runs commands and browses without asking. It still asks before risky actions (installing software, starting other programs, wide deletes, system changes, passwords and payments), " +
                       (settings.AiWebGuard ? "and before commands once it has read web content. " : "") + "You can choose what Auto asks about in Settings > Safety. Ctrl+Z or the Undo link reverts file changes.", 0);
            }
            if (lv == "full" && was != "full")
                Notice("Full auto is on: the agent asks nothing until you switch it back (click the red switch). It goes back to Auto when you close MinGent." + (LocalModelPicked() ? " " + LocalFullAutoNote : ""), 2);
            ChatItem pending = chat.PendingPermission();
            if (pending == null || lv == "ask" || pending.Ask.EvenInFull) return;
            if (lv == "full") { chat.Resolve(pending, pending.Ask.Never ? PermissionDecision.Deny : PermissionDecision.Allow); return; }
            bool allowedCategory = !pending.Ask.Never && session.Perms.AutoAllows(pending.Ask.Category);
            if (allowedCategory || (!pending.Ask.High && pending.Ask.Kind != "read" && pending.Ask.Kind != "web" && pending.Ask.Kind != "console" &&
                !(session.Perms.WebGuard && session.Perms.Tainted && (pending.Ask.Kind == "command" || pending.Ask.Kind == "write"))))
                chat.Resolve(pending, PermissionDecision.Allow);
        }

        void Notice(string text, int level)
        {
            ChatItem it = new ChatItem();
            it.Kind = ChatKind.Notice;
            it.Text = text;
            it.Level = level;
            chat.Add(it);
        }

        public void RefreshConnection(bool force)
        {
            if (refreshing) return;
            if (!force && (DateTime.UtcNow - lastRefresh).TotalSeconds < 20) return;
            refreshing = true;
            lastRefresh = DateTime.UtcNow;
            ProviderKeys k = GetKeys();
            Thread t = new Thread(delegate ()
            {
                CatalogSnapshot snap = null;
                try { snap = ModelCatalog.Refresh(k, true, new CancelToken()); } catch { }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        refreshing = false;
                        if (snap != null) catalog = snap;
                        UpdateModelChip();
                        if (modelMenu != null && !modelMenu.IsDisposed && modelMenu.Visible)
                        {
                            FillModelMenu(modelMenu);
                            modelMenu.Show(modelChip, new Point(0, -UI.S(4)), ToolStripDropDownDirection.AboveRight);
                        }
                    }));
                }
                catch { refreshing = false; }
            });
            t.IsBackground = true;
            t.Start();
        }

        ProviderKeys GetKeys()
        {
            ProviderKeys k = new ProviderKeys();
            k.Anthropic = Secret.Unprotect(settings.KeyAnthropic);
            k.OpenAi = Secret.Unprotect(settings.KeyOpenAi);
            k.Gemini = Secret.Unprotect(settings.KeyGemini);
            k.OpenRouter = Secret.Unprotect(settings.KeyOpenRouter);
            k.Custom = Secret.Unprotect(settings.KeyCustom);
            k.LmStudioToken = Secret.Unprotect(settings.KeyLmStudio);
            SecretWatch.Set(k.Anthropic, k.OpenAi, k.Gemini, k.OpenRouter, k.Custom, k.LmStudioToken);
            k.CustomUrl = settings.CustomUrl;
            k.CustomModel = settings.CustomModel;
            k.LmLoadContext = settings.LmContext;
            k.EngineContext = settings.EngineContext;
            return k;
        }

        static string AutoRef(CatalogSnapshot snap)
        {
            if (snap == null) return "";
            if (Engine.Instance.Installed)
            {
                ModelEntry first = null;
                foreach (ModelEntry e in snap.Models)
                {
                    if (e.Provider != "engine") continue;
                    if (e.Loaded) return e.Ref;
                    if (first == null) first = e;
                }
                if (first != null) return first.Ref;
            }
            ProviderState ps = snap.State("lmstudio");
            if (ps != null && ps.Reachable && !ps.NeedsKey)
                foreach (ModelEntry e in snap.Models) if (e.Provider == "lmstudio") return "lmstudio:";
            foreach (ModelEntry e in snap.Models) if (e.Provider == "anthropic") return e.Ref;
            foreach (ModelEntry e in snap.Models) if (e.Provider == "openai") return e.Ref;
            foreach (ModelEntry e in snap.Models) if (e.Provider == "gemini") return e.Ref;
            foreach (ModelEntry e in snap.Models) if (e.Provider == "openrouter") return e.Ref;
            foreach (ModelEntry e in snap.Models) if (e.Provider == "custom") return e.Ref;
            return "";
        }

        string ModelRef()
        {
            string r = settings.AiModel ?? "";
            return r.Length > 0 ? r : AutoRef(catalog);
        }

        ModelEntry SelectedEntry()
        {
            if (catalog == null) return null;
            string r = ModelRef();
            if (r.Length == 0) return null;
            return catalog.Find(r);
        }

        void UpdateModelChip()
        {
            string r = ModelRef();
            string provider = r.IndexOf(':') > 0 ? r.Substring(0, r.IndexOf(':')) : "";
            ModelEntry m = SelectedEntry();
            string name;
            int state;
            if (catalog == null && r.Length == 0) { name = "Looking for models"; state = 0; }
            else if (r.Length == 0) { name = "Choose a model"; state = 2; }
            else if (m != null)
            {
                name = m.Display;
                state = m.Loaded || m.Cloud ? 1 : 0;
            }
            else if (provider == "lmstudio" && r == "lmstudio:")
            {
                ProviderState ps = catalog != null ? catalog.State("lmstudio") : null;
                name = ps == null ? "LM Studio" : ps.Reachable ? "LM Studio (no model loaded)" : "LM Studio (not running)";
                state = ps == null ? 0 : ps.Reachable ? 0 : 2;
            }
            else
            {
                int c = r.IndexOf(':');
                string id = c >= 0 ? r.Substring(c + 1) : r;
                name = provider == "engine" ? Path.GetFileNameWithoutExtension(id) : id;
                state = catalog == null ? 0 : 2;
            }
            modelChip.Set(name, "", state);
            composer.EffortText = EffortLabel();
            composer.ShowEffort = EffortOptions().Count > 0;
            composer.PerformLayout();
            composer.Invalidate();
            UpdateEmptyState();
            PerformLayout();
        }

        List<string> EffortOptions() { return EffortOptionsFor(SelectedEntry()); }

        List<string> EffortOptionsFor(ModelEntry m)
        {
            List<string> opts = new List<string>();
            if (m == null || m.ReasoningOptions == null) return opts;
            foreach (string o in m.ReasoningOptions) if (!opts.Contains(o)) opts.Add(o);
            if (m.Provider == "lmstudio" || m.Provider == "custom" || m.Provider == "gemini" || m.Provider == "openrouter")
            {
                List<string> learned = OpenAiChatBackend.Learned(m.Id);
                if (learned != null && learned.Count > 0)
                {
                    bool off = opts.Contains("off");
                    opts.Clear();
                    if (off && !learned.Contains("none")) opts.Add("off");
                    foreach (string v in new string[] { "none", "minimal", "low", "medium", "high", "xhigh", "max" })
                        if (learned.Contains(v)) opts.Add(v == "none" ? "off" : v);
                }
            }
            if (m.Provider == "anthropic" && m.EffortLevels != null && m.EffortLevels.Count > 0) { opts.Clear(); opts.AddRange(m.EffortLevels); }
            if (m.Provider == "anthropic" && (m.AnthropicDisable || m.AnthropicBetweenTools) && !opts.Contains("off")) opts.Insert(0, "off");
            return opts;
        }

        string CurrentEffort() { return EffectiveEffort(SelectedEntry()); }

        string EffectiveEffort(ModelEntry m)
        {
            List<string> opts = EffortOptionsFor(m);
            if (opts.Count == 0) return null;
            string e = settings.AiEffort ?? "";
            if (opts.Contains(e)) return e;
            if (e.Length > 0)
            {
                string near = EffortLevels.Nearest(e, opts);
                if (near != null) return near;
            }
            if (m != null && (m.BudgetThinking || m.Provider == "anthropic") && opts.Contains("high")) return "high";
            if (opts.Contains("medium")) return "medium";
            if (opts.Contains("on")) return "on";
            return opts[opts.Count - 1];
        }

        string EffortLabel()
        {
            string e = CurrentEffort();
            if (e == null) return "";
            if (e == "on") return "Thinking";
            if (e == "off" || e == "none") return "No thinking";
            return "Thinking " + EffortLevels.Name(e).ToLowerInvariant();
        }

        void ShowEffortMenu()
        {
            List<string> opts = EffortOptions();
            if (opts.Count == 0) return;
            ContextMenuStrip m = Menus.Create(th);
            string cur = CurrentEffort();
            m.Items.Add(Menus.Header("How much the model thinks before answering", th));
            ModelEntry sel = SelectedEntry();
            bool budget = sel != null && sel.BudgetThinking;
            foreach (string o in opts)
            {
                string v = o;
                string label = EffortLevels.Name(o);
                string detail = o == "off" ? "fastest" : "";
                if (budget && o != "off")
                {
                    int tokens = OpenAiChatBackend.ThinkingBudget(o, sel.Context);
                    detail = tokens > 0 ? "up to " + (tokens >= 1024 ? (tokens / 1024) + "K" : tokens.ToString()) + " tokens" : "no limit";
                }
                m.Items.Add(Menus.Detail(label, detail, o == cur, delegate { settings.AiEffort = v; settings.Save(); UpdateModelChip(); }));
            }
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(composer, composer.EffortAnchor, ToolStripDropDownDirection.AboveRight);
            composer.Invalidate();
        }

        void ShowModelMenu()
        {
            ContextMenuStrip m = Menus.Create(th);
            RefreshConnection(true);
            FillModelMenu(m);
            modelMenu = m;
            m.Closed += delegate { BeginInvoke(new Action(delegate { if (modelMenu == m) modelMenu = null; m.Dispose(); })); };
            m.Show(modelChip, new Point(0, -UI.S(4)), ToolStripDropDownDirection.AboveRight);
        }

        void FillModelMenu(ContextMenuStrip m)
        {
            m.SuspendLayout();
            m.Items.Clear();
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            if (catalog == null) m.Items.Add(Menus.Header("Looking for models...", th));
            else
            {
                string cur = ModelRef();
                bool custom = !string.IsNullOrEmpty(settings.CustomUrl);
                bool lm = cur.StartsWith("lmstudio:", StringComparison.Ordinal) || catalog.Models.Exists(delegate (ModelEntry x) { return x.Provider == "lmstudio"; }) || ModelCatalog.LmStudioInstalled();
                bool router = cur.StartsWith("openrouter:", StringComparison.Ordinal) || !string.IsNullOrEmpty(settings.KeyOpenRouter);
                int sections = 4 + (lm ? 1 : 0) + (router ? 1 : 0) + (custom ? 1 : 0), filled = 0;
                foreach (string p in new string[] { "engine", "lmstudio", "anthropic", "openai", "gemini", "openrouter", "custom" })
                    foreach (ModelEntry e in catalog.Models) if (e.Provider == p) { filled++; break; }
                int rows = (wa.Height - UI.S(120)) / UI.S(28) - sections * 2 - 4;
                int cap = Math.Max(4, Math.Min(40, rows / Math.Max(1, filled)));
                AddProviderSection(m, "engine", "On this PC (built-in engine)", cur, cap);
                if (lm) AddProviderSection(m, "lmstudio", "LM Studio", cur, cap);
                AddProviderSection(m, "anthropic", "Claude (Anthropic API)", cur, cap);
                AddProviderSection(m, "openai", "OpenAI API", cur, cap);
                AddProviderSection(m, "gemini", "Gemini (Google AI)", cur, cap);
                if (router) AddProviderSection(m, "openrouter", "OpenRouter", cur, cap);
                if (custom) AddProviderSection(m, "custom", "Custom server", cur, cap);
            }
            m.Items.Add(new ToolStripSeparator());
            EngineRelease upd = EngineUpdates.Available;
            if (upd != null) m.Items.Add(Menus.Item("Update llama.cpp to " + upd.Version + "...", false, delegate { host.ShowModelLibrary(); }));
            m.Items.Add(Menus.Item("Download or manage models...", false, delegate { host.ShowModelLibrary(); }));
            m.Items.Add(Menus.Item("API keys and model settings...", false, delegate { host.ShowSettings(); }));
            ToolStripMenuItem again = Menus.Item(refreshing ? "Checking for models..." : "Refresh", false, delegate { RefreshConnection(true); });
            again.Enabled = !refreshing;
            m.Items.Add(again);
            m.MaximumSize = new Size(wa.Width - UI.S(40), wa.Height - UI.S(40));
            m.ResumeLayout();
        }

        void AddProviderSection(ContextMenuStrip m, string provider, string title, string cur, int cap)
        {
            ProviderState ps = catalog.State(provider);
            List<ModelEntry> list = new List<ModelEntry>();
            foreach (ModelEntry e in catalog.Models) if (e.Provider == provider) list.Add(e);
            if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator());
            bool keyless = ModelEntry.IsCloud(provider) && list.Count == 0 && catalog.State(provider).Configured == false;
            string status = keyless ? "" : ps.Status;
            bool showStatus = status.Length > 0 && (list.Count == 0 || provider == "lmstudio");
            bool shortStatus = showStatus && status.Length <= 30 && status.IndexOf(" - ", StringComparison.Ordinal) < 0;
            m.Items.Add(Menus.Header(title, shortStatus ? status : "", th));
            if (showStatus && !shortStatus) m.Items.Add(Menus.Hint(status, th));
            if (provider == "lmstudio" && ps.Reachable)
            {
                ModelEntry loaded = catalog.Find("lmstudio:");
                ToolStripMenuItem auto = Menus.Item("Use the model loaded in LM Studio" + (loaded != null ? " (" + loaded.Display + ")" : ""), cur == "lmstudio:", delegate { Choose("lmstudio:"); });
                m.Items.Add(auto);
            }
            List<ModelEntry> shown = list;
            if (list.Count > cap)
            {
                shown = list.GetRange(0, cap - 1);
                ModelEntry picked = list.Find(delegate (ModelEntry x) { return string.Equals(cur, x.Ref, StringComparison.OrdinalIgnoreCase); });
                shown.Add(picked != null && !shown.Contains(picked) ? picked : list[cap - 1]);
            }
            foreach (ModelEntry e in shown)
            {
                ModelEntry entry = e;
                ToolStripMenuItem mi = Menus.Detail(e.Display, ModelDetail(e), string.Equals(cur, e.Ref, StringComparison.OrdinalIgnoreCase), delegate { Choose(entry.Ref); });
                if (!e.ToolUse) mi.ToolTipText = "This model was not trained for tool use, so it may handle tasks poorly.";
                m.Items.Add(mi);
            }
            if (shown.Count < list.Count)
            {
                List<ModelEntry> all = list;
                m.Items.Add(Menus.Item("All " + list.Count + " models...", false, delegate { BeginInvoke(new Action(delegate { ShowAllModels(all, cur); })); }));
            }
            if (list.Count == 0)
            {
                if (ModelEntry.IsCloud(provider))
                    m.Items.Add(Menus.Item(ps.NeedsKey ? "Check the API key..." : ps.Configured ? "API key settings..." : "Add an API key...", false, delegate { host.ShowSettings("Models"); }));
                else if (provider == "engine")
                    m.Items.Add(Menus.Item(Engine.Instance.Installed ? "Download a model..." : "Set up the built-in engine...", false, delegate { host.ShowModelLibrary(); }));
            }
        }

        static string ModelDetail(ModelEntry e)
        {
            return e.Detail + (e.Provider == "lmstudio" && e.Loaded ? (e.Detail.Length > 0 ? "  \u00B7  " : "") + "loaded" : "");
        }

        void ShowAllModels(List<ModelEntry> list, string cur)
        {
            List<Choice> items = new List<Choice>();
            int sel = -1;
            for (int i = 0; i < list.Count; i++)
            {
                Choice c = new Choice();
                c.Title = list[i].Display;
                c.Note = ModelDetail(list[i]);
                items.Add(c);
                if (string.Equals(cur, list[i].Ref, StringComparison.OrdinalIgnoreCase)) sel = i;
            }
            ChoicePopup p = new ChoicePopup(th, items, sel, false, UI.S(300));
            if (list.Count > 12) p.MakeFilterable(UI.S(420));
            p.Picked += delegate (int i) { if (i >= 0 && i < list.Count) Choose(list[i].Ref); };
            p.Closed += delegate { BeginInvoke(new Action(delegate { p.Dispose(); })); };
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            p.LimitHeight(Math.Min(UI.S(520), wa.Height - UI.S(120)));
            p.Show(modelChip, new Point(0, -UI.S(4)), ToolStripDropDownDirection.AboveRight);
        }

        void Choose(string modelRef)
        {
            bool wasLocal = LocalModelPicked();
            settings.AiModel = modelRef;
            settings.Save();
            UpdateModelChip();
            if (level == "full" && !wasLocal && LocalModelPicked()) Notice("Full auto is still on. " + LocalFullAutoNote, 2);
            if (modelRef.StartsWith("lmstudio:") && modelRef.Length > 9)
            {
                ModelEntry e = catalog == null ? null : catalog.Find(modelRef);
                if (e != null && !e.Loaded) Notice(e.Display + " will be loaded in LM Studio when you send a message.", 0);
            }
        }

        string PrepareModel(CancelToken ct, Action<string> progress, out ModelEntry model, out LlmBackend be)
        {
            model = null;
            be = null;
            ProviderKeys k = GetKeys();
            string r = settings.AiModel ?? "";
            CatalogSnapshot snap = catalog;
            if (r.Length == 0)
            {
                progress("Looking for models");
                snap = ModelCatalog.Refresh(k, true, ct);
                CatalogSnapshot s1 = snap;
                try { BeginInvoke(new Action(delegate { catalog = s1; UpdateModelChip(); })); } catch { }
                if (ct.IsCancelled) return "stopped";
                r = AutoRef(snap);
                if (r.Length == 0)
                    return Engine.Instance.Installed
                        ? "No model is set up yet. Open the model library to download one that runs on this PC, or add an API key in Settings."
                        : "No model is set up yet. Open the model library to set up the built-in engine and download a model (free, runs on this PC), or add an API key in Settings.";
            }
            string provider = r.Substring(0, r.IndexOf(':') > 0 ? r.IndexOf(':') : r.Length);
            bool cloud = ModelEntry.IsCloud(provider);
            if (snap == null || (DateTime.UtcNow - snap.When).TotalSeconds > 8 || provider == "lmstudio" || provider == "engine")
            {
                progress("Connecting to " + ModelEntry.ProviderTitle(provider));
                snap = ModelCatalog.Refresh(k, cloud || provider == "custom", ct);
                CatalogSnapshot s2 = snap;
                try { BeginInvoke(new Action(delegate { catalog = s2; UpdateModelChip(); })); } catch { }
            }
            if (ct.IsCancelled) return "stopped";
            ProviderState ps = snap.State(provider);
            if (provider == "lmstudio")
            {
                if (!ps.Reachable)
                    return "LM Studio's server is not running. Open LM Studio, go to the Developer tab and start the server - or pick another model.";
                if (ps.NeedsKey) return "LM Studio asks for an API token. Add it in Settings (Models > LM Studio API token).";
                ModelEntry m = snap.Find(r);
                if (m == null && r == "lmstudio:")
                {
                    string last = settings.AiLastLmModel;
                    if (!string.IsNullOrEmpty(last)) m = snap.Find("lmstudio:" + last);
                    if (m == null)
                    {
                        int count = 0;
                        foreach (ModelEntry e in snap.Models) if (e.Provider == "lmstudio") { count++; if (count == 1) m = e; }
                        if (count != 1) m = null;
                    }
                    if (m == null) return "No model is loaded in LM Studio. Choose one from the model menu at the top of this panel and MinGent will load it.";
                }
                if (m == null) return "That model is no longer in LM Studio. Choose another one from the model menu.";
                if (!m.Loaded)
                {
                    progress("Loading " + m.Display + " in LM Studio");
                    string inst;
                    string err = ModelCatalog.LoadLmStudioModel(k, ps.BaseUrl, m.Path, settings.LmContext, settings.LmUnloadOthers, ct, out inst);
                    if (err != null) return "LM Studio could not load " + m.Display + ": " + err;
                    snap = ModelCatalog.Refresh(k, false, ct);
                    CatalogSnapshot s3 = snap;
                    try { BeginInvoke(new Action(delegate { catalog = s3; UpdateModelChip(); })); } catch { }
                    ModelEntry again = snap.Find("lmstudio:" + (inst ?? m.Id));
                    if (again == null) foreach (ModelEntry e in snap.Models) if (e.Provider == "lmstudio" && e.Loaded && e.Path == m.Path) again = e;
                    if (again != null) m = again;
                    m.Loaded = true;
                }
                settings.AiLastLmModel = m.Path;
                model = m;
                be = ModelCatalog.CreateBackend(m, k, snap.State("lmstudio").BaseUrl);
                return null;
            }
            if (provider == "engine")
            {
                string path = r.Substring(7);
                LocalModel lm = null;
                foreach (LocalModel x in ModelLibrary.Scan(false)) if (string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) { lm = x; break; }
                if (lm == null) return "The model file was not found: " + path + ". Choose another model.";
                if (!Engine.Instance.Installed) return "The built-in engine is not set up yet. Open the model library to install it (one small download).";
                ModelEntry m = snap.Find(r);
                if (m == null) { m = new ModelEntry(); m.Provider = "engine"; m.Id = path; m.Path = path; m.Display = lm.DisplayName; }
                int wantCtx = EngineContextFor(lm);
                if (!Engine.Instance.Matches(path, wantCtx, settings.EngineKv))
                {
                    progress((Engine.Instance.Running && string.Equals(Engine.Instance.ModelPath, path, StringComparison.OrdinalIgnoreCase) ? "Reloading " : "Loading ") + lm.DisplayName);
                    string err = Engine.Instance.Start(lm, wantCtx, settings.EngineKv, delegate (string p) { progress(p); }, ct);
                    if (err != null) return err;
                }
                m.Context = Engine.Instance.Context > 0 ? Engine.Instance.Context : EngineContextFor(lm);
                m.Loaded = true;
                model = m;
                be = ModelCatalog.CreateBackend(m, k, null);
                return null;
            }
            ModelEntry entry = snap.Find(r);
            if (entry == null)
            {
                if (provider == "anthropic" && string.IsNullOrEmpty(k.Anthropic)) return "Add your Anthropic API key in Settings to use Claude models.";
                if (provider == "openai" && string.IsNullOrEmpty(k.OpenAi)) return "Add your OpenAI API key in Settings to use OpenAI models.";
                if (provider == "gemini" && string.IsNullOrEmpty(k.Gemini)) return "Add your Gemini API key in Settings to use Gemini models. You can get a free one at aistudio.google.com/apikey.";
                if (provider == "openrouter" && string.IsNullOrEmpty(k.OpenRouter)) return "Add your OpenRouter API key in Settings to use OpenRouter models.";
                if (ps.NeedsKey) return ModelEntry.ProviderTitle(provider) + " rejected the API key. Check it in Settings.";
                if (!string.IsNullOrEmpty(ps.Error)) return ModelEntry.ProviderTitle(provider) + ": " + ps.Error;
                entry = new ModelEntry();
                entry.Provider = provider;
                entry.Id = r.Substring(provider.Length + 1);
                entry.Display = entry.Id;
                entry.Context = provider == "anthropic" ? 200000 : provider == "gemini" ? 1048576 : 128000;
                entry.Vision = provider != "custom";
            }
            model = entry;
            be = ModelCatalog.CreateBackend(entry, k, null);
            return null;
        }

        void Attach()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Attach an image, a document or a file";
                d.Filter = "Images, documents, sound and text|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.pdf;*.docx;*.xlsx;*.pptx;*.odt;*.ods;*.odp;*.rtf;*.epub;*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.opus;*.wma;*.aac;*.aif;*.aiff;*.txt;*.md;*.cs;*.py;*.js;*.ts;*.json;*.html;*.css;*.xml;*.csv;*.log"
                    + "|Documents (PDF, Word, Excel, PowerPoint)|*.pdf;*.docx;*.docm;*.dotx;*.xlsx;*.xlsm;*.xltx;*.pptx;*.pptm;*.ppsx;*.odt;*.ods;*.odp;*.rtf;*.epub"
                    + "|Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp"
                    + "|Sound files|*.wav;*.wave;*.mp3;*.m4a;*.m4b;*.aac;*.flac;*.ogg;*.oga;*.opus;*.wma;*.aif;*.aiff;*.aifc;*.amr;*.mka;*.caf;*.ac3;*.mp2"
                    + "|All files|*.*";
                d.Multiselect = true;
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                AttachFiles(d.FileNames);
            }
        }

        public void DropFiles(string[] files)
        {
            AttachFiles(files);
            composer.FocusInput();
        }

        void ShowDropHint(bool on)
        {
            chat.DropHint = on;
            Color under = Color.Empty;
            if (on)
            {
                Rectangle area = chat.DropArea;
                area.Offset(chat.Left, chat.Top);
                if (area.Contains(composer.Bounds)) under = chat.DropFill;
            }
            composer.Backdrop = under;
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            bool files = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = files ? DragDropEffects.Copy : DragDropEffects.None;
            ShowDropHint(files);
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            ShowDropHint(false);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            ShowDropHint(false);
            string[] files = e.Data != null ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
            if (files == null || files.Length == 0) return;
            DropFiles(files);
            Form f = FindForm();
            if (f != null) f.Activate();
        }

        public void AttachFiles(string[] paths)
        {
            if (paths == null) return;
            List<string> full = new List<string>(), failed = new List<string>();
            string why = null;
            int asFiles = 0;
            foreach (string p in paths)
            {
                string err;
                Attachment a = Attachments.FromPath(p, out err);
                string name = a != null ? a.Name : Path.GetFileName((p ?? "").TrimEnd('\\', '/'));
                if (a == null) { failed.Add(name); if (why == null && !string.IsNullOrEmpty(err)) why = err; continue; }
                if (!a.Folder && ToolKit.IsImagePath(a.Path))
                {
                    string imgErr;
                    if (composer.PictureCount < Attachments.MaxPictures) a.Image = ToolKit.LoadImage(a.Path, out imgErr);
                    else asFiles++;
                }
                if (!a.Picture && composer.FileCount >= Attachments.MaxFiles) { full.Add(name); continue; }
                composer.Add(a);
            }
            if (asFiles > 0)
                Notice("A message can show the model up to " + Attachments.MaxPictures + " pictures, so " + (asFiles == 1 ? "one more was" : asFiles + " more were") + " attached as " + (asFiles == 1 ? "a file" : "files") + ". The agent can still open " + (asFiles == 1 ? "it." : "them."), 0);
            if (failed.Count > 0)
                Notice("Could not attach " + Names(failed) + (failed.Count == 1 && why != null ? ": " + why.TrimEnd('.') + "." : "."), 1);
            if (full.Count > 0)
                Notice("A message can carry up to " + Attachments.MaxFiles + " files besides its pictures, so " + Names(full) + (full.Count == 1 ? " was" : " were") + " not attached.", 1);
        }

        static string Names(List<string> names)
        {
            if (names.Count == 1) return names[0];
            if (names.Count <= 3) return string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1];
            return names[0] + ", " + names[1] + " and " + (names.Count - 2) + " more";
        }

        void OpenAttachment(ChatItem item, Attachment a)
        {
            if (a == null) return;
            if (a.Picture)
            {
                string err;
                ImageData full = a.Image ?? (a.Path != null && File.Exists(a.Path) ? ToolKit.LoadImage(a.Path, out err) : null);
                ImagePreview.Show(FindForm(), th, full ?? a.Shown, a.Name);
                return;
            }
            if (a.Path == null) return;
            if (!(a.Folder ? Directory.Exists(a.Path) : File.Exists(a.Path)))
            {
                Notice(a.Name + " is no longer at " + a.Path + ".", 1);
                return;
            }
            try
            {
                if (a.Folder) System.Diagnostics.Process.Start(Native.WindowsExe("explorer.exe"), "\"" + a.Path + "\"");
                else if (DocExport.CanWrite(a.Path) || Attachments.Opens(a)) host.OpenLink(a.Path);
                else System.Diagnostics.Process.Start(Native.WindowsExe("explorer.exe"), "/select,\"" + a.Path + "\"");
            }
            catch (Exception) { }
        }

        public void Ask(string text)
        {
            if (running) return;
            composer.Text = text;
            Send();
        }

        public void AskAbout(string request)
        {
            if (running)
            {
                if (composer.Text.Trim().Length == 0) { composer.Text = request; composer.FocusInput(); composer.MoveCaretToEnd(); }
                else Notice("The agent is still working. Wait for it to finish or stop it, then ask again.", 1);
                return;
            }
            string label;
            string ctx = host.ActiveContext(out label);
            if (ctx == null) label = null;
            StartRun(request, null, ctx, label, null);
        }

        readonly List<ChatItem> queued = new List<ChatItem>();

        void Send()
        {
            if (running)
            {
                string q = composer.Text.Trim();
                if (q.Length == 0 && !composer.HasAttachments)
                {
                    if (chat.PendingPermission() != null || chat.PendingQuestion() != null) chat.ScrollToEnd();
                    return;
                }
                if (!composer.HasAttachments && Slash(q)) return;
                if (AnswerFromComposer(q))
                {
                    if (!composer.HasAttachments) return;
                    q = "";
                }
                if (composer.HasPictures)
                {
                    Notice("Pictures can be sent once the agent has finished. Press Esc to stop it and send them now.", 1);
                    return;
                }
                List<Attachment> files = composer.TakeAttachments();
                composer.Text = "";
                ChatItem u = new ChatItem();
                u.Kind = ChatKind.User;
                u.Text = q;
                u.Attachments = files.Count > 0 ? files : null;
                u.MidRun = true;
                u.Queued = true;
                u.Turn = curTurn;
                u.InboxId = session.PostUserMessage(InboxText(u));
                queued.Add(u);
                chat.Add(u);
                UpdateQueueNotes();
                chat.ScrollToEnd();
                return;
            }
            string text = composer.Text.Trim();
            if (text.Length == 0 && !composer.HasAttachments) return;
            if (!composer.HasAttachments && Slash(text)) return;
            List<Attachment> parked;
            string held = TakeQueue(true, out parked);
            if (held != null) text = text.Length > 0 && held.Length > 0 ? held + "\n\n" + text : held + text;
            ChatItem stale = chat.PendingQuestion();
            if (stale != null)
            {
                UserAnswer typed = new UserAnswer();
                typed.Text = text;
                if (stale.Question != null && stale.Question.PlanApproval && !composer.HasAttachments) typed.Choice = PlanReply.Choice(stale.Question.Options, text);
                chat.AnswerQuestion(stale, typed);
            }
            List<Attachment> atts = composer.TakeAttachments();
            if (parked != null) atts.InsertRange(0, parked);
            if (chat.Editing) chat.CancelEdit();
            composer.Text = "";
            string label = null;
            string ctxText = settings.AiSendContext && composer.ContextEnabled ? host.ActiveContext(out label) : null;
            if (ctxText == null) label = null;
            StartRun(text, atts, ctxText, label, null);
        }

        static string InboxText(ChatItem u)
        {
            return Attachments.WithBlock(u.Text, u.Attachments);
        }

        void CheckInbox()
        {
            if (running && session.InboxRead != null) return;
            for (int i = 0; i < queued.Count; i++)
            {
                ChatItem u = queued[i];
                if (u.InboxId <= 0 || session.Undelivered(u.InboxId)) continue;
                queued.RemoveAt(i--);
                u.Queued = false;
                u.InboxId = 0;
                if (chat.EditingItem == u) chat.CancelEdit();
                chat.Changed(u);
                UpdateQueueNotes();
            }
        }

        bool compacting, planLater;

        void UpdateQueueNotes()
        {
            ChatItem ed = QueuedEdit();
            bool behind = false;
            foreach (ChatItem u in queued)
            {
                string note = compacting ? ChatView.QueueCompacting : !running ? ChatView.QueueHeld : behind ? ChatView.QueueBehind : ChatView.QueueRunning;
                if (u == ed) behind = true;
                if (u.Summary == note) continue;
                u.Summary = note;
                chat.InvalidateItem(u);
            }
        }

        ChatItem QueuedEdit()
        {
            ChatItem e = chat.EditingItem;
            return e != null && queued.Contains(e) ? e : null;
        }

        void EditQueued(ChatItem u)
        {
            if (u == null || !queued.Contains(u)) return;
            if (chat.EditingItem == u) { chat.FocusEditor(); return; }
            if (u.InboxId > 0 && !session.HoldMessage(u.InboxId, true))
            {
                CheckInbox();
                Notice("The agent has already read that message.", 0);
                return;
            }
            if (!chat.BeginQueuedEdit(u) && u.InboxId > 0) session.HoldMessage(u.InboxId, false);
            UpdateQueueNotes();
        }

        bool SaveQueued(ChatItem u, string text)
        {
            if (!queued.Contains(u)) return true;
            u.Text = text;
            if (u.InboxId > 0 && !session.ReplaceMessage(u.InboxId, InboxText(u))) u.InboxId = running ? session.PostUserMessage(InboxText(u)) : 0;
            chat.Changed(u);
            Later(UpdateQueueNotes);
            if (!running) Later(FlushHeld);
            return true;
        }

        void QueuedEditCancelled(ChatItem u)
        {
            if (!queued.Contains(u)) return;
            if (u.InboxId > 0) session.HoldMessage(u.InboxId, false);
            Later(UpdateQueueNotes);
            if (!running) Later(FlushHeld);
        }

        void RemoveQueued(ChatItem u)
        {
            if (u == null || !queued.Contains(u)) return;
            if (u.InboxId > 0 && !session.WithdrawMessage(u.InboxId))
            {
                CheckInbox();
                Notice("The agent has already read that message.", 0);
                return;
            }
            queued.Remove(u);
            u.Queued = false;
            u.InboxId = 0;
            chat.Remove(u);
            UpdateQueueNotes();
            if (!running) Later(FlushHeld);
        }

        void Later(Action a)
        {
            if (IsHandleCreated) BeginInvoke(a); else a();
        }

        void FlushHeld()
        {
            if (running || QueuedEdit() != null) return;
            if (queued.Count == 0)
            {
                if (planLater && composer.Plan && !planAsked) OfferPlan();
                planLater = false;
                return;
            }
            if (chat.Editing) ParkQueue();
            else FlushQueue(true);
        }

        void DetachQueue()
        {
            session.TakeUndelivered();
            foreach (ChatItem u in queued) u.InboxId = 0;
        }

        void FlushQueue(bool send)
        {
            if (queued.Count == 0) return;
            if (send && QueuedEdit() != null)
            {
                UpdateQueueNotes();
                return;
            }
            List<Attachment> atts;
            string merged = TakeQueue(false, out atts);
            if (merged == null) return;
            if (send) StartRun(merged, atts, null, null, null);
            else Park(merged, atts);
        }

        string TakeQueue(bool sending, out List<Attachment> atts)
        {
            atts = null;
            if (queued.Count == 0) return null;
            ChatItem ed = QueuedEdit();
            if (ed != null)
            {
                string t = chat.EditText;
                if (t.Length > 0) ed.Text = t;
                else if (sending) ed.Text = "";
            }
            List<ChatItem> waiting = new List<ChatItem>(queued);
            queued.Clear();
            List<string> texts = new List<string>();
            List<Attachment> files = new List<Attachment>();
            foreach (ChatItem u in waiting)
            {
                if (u.InboxId > 0) session.WithdrawMessage(u.InboxId);
                u.InboxId = 0;
                u.Queued = false;
                if (u.Text.Trim().Length > 0) texts.Add(u.Text.Trim());
                if (u.Attachments != null) files.AddRange(u.Attachments);
                chat.Remove(u);
            }
            if (files.Count > 0) atts = files;
            if (texts.Count == 0 && files.Count == 0) return null;
            return string.Join("\n\n", texts.ToArray());
        }

        void ParkQueue()
        {
            List<Attachment> atts;
            string merged = TakeQueue(false, out atts);
            if (merged != null) Park(merged, atts);
        }

        void Park(string merged, List<Attachment> atts)
        {
            if (atts != null) foreach (Attachment a in atts) composer.Add(a);
            string cur = composer.Text.Trim();
            composer.Text = cur.Length > 0 && merged.Length > 0 ? merged + "\n\n" + cur : merged + cur;
            if (ContainsFocus) composer.FocusInput();
            composer.MoveCaretToEnd();
        }

        void StartRun(string text, List<Attachment> atts, string ctxText, string label, ChatItem reuse)
        {
            holdNoticeShown = false;
            bool fresh = chatFile == null;
            if (fresh && !temporary) NewChatFile(text.Trim().Length > 0 || atts == null || atts.Count == 0 ? text : atts[0].Name);
            else if (fresh) chatTitle = "Temporary chat";
            ChatItem user = reuse ?? new ChatItem();
            curTurn = ++turnCounter;
            user.Turn = curTurn;
            user.Kind = ChatKind.User;
            user.Text = text;
            user.MidRun = false;
            user.Queued = false;
            user.ContextLabel = label;
            user.Attachments = atts != null && atts.Count > 0 ? atts : null;
            if (reuse == null) chat.Add(user); else chat.Changed(user);
            chat.ScrollToEnd();
            StringBuilder sb = new StringBuilder();
            bool wsDefault;
            string wsNow = host.WorkspaceFolder(out wsDefault);
            sb.Append(SystemPrompt.EnvLine(host.Editor.Tabs(false), session, wsNow, wsDefault, level)).Append("\n\n");
            if (!string.IsNullOrEmpty(ctxText)) sb.Append(ctxText).Append("\n\n");
            string body = Attachments.WithBlock(text, atts);
            sb.Append(body);
            Msg msg = new Msg("user", sb.ToString());
            msg.Compact = body;
            msg.Turn = curTurn;
            msg.PlanApproved = approveNext;
            approveNext = false;
            List<ImageData> images = Attachments.Images(atts);
            if (images.Count > 0) msg.Images = images;
            Begin(msg);
            SaveChat();
            if (fresh) Invalidate();
        }

        ChatItem TurnActivity()
        {
            for (int i = chat.Items.Count - 1; i >= 0; i--)
            {
                ChatItem it = chat.Items[i];
                if (it.Kind == ChatKind.Activity) return it.Label.Length > 0 ? null : it;
                if (it.Kind == ChatKind.User && !it.MidRun) return null;
            }
            return null;
        }

        const string KeepHint = "Tell the agent what to change in the plan";
        string afterRunHint;

        void DismissStale()
        {
            ChatItem pq = chat.PendingQuestion();
            if (pq != null && (!running || pq.RunId != liveRun)) chat.AnswerQuestion(pq, UserAnswer.None());
            composer.Prompt = null;
            afterRunHint = null;
        }

        void Begin(Msg msg)
        {
            DismissStale();
            int run = ++runCounter;
            liveRun = run;
            running = true;
            cancel = new CancelToken();
            runLog = new RunChangeLog();
            curAssistant = null;
            curThinking = null;
            toolItems.Clear();
            planLater = false;
            foreach (ChatItem u in queued)
                if (u.InboxId <= 0)
                {
                    u.InboxId = session.PostUserMessage(InboxText(u));
                    if (QueuedEdit() == u) session.HoldMessage(u.InboxId, true);
                    u.Turn = curTurn;
                    if (chat.Items.Remove(u)) chat.Items.Add(u);
                    chat.Changed(u);
                }
            curActivity = TurnActivity();
            if (curActivity == null)
            {
                curActivity = new ChatItem();
                curActivity.Kind = ChatKind.Activity;
                chat.Add(curActivity);
            }
            planAsked = false;
            session.Perms.Planning = composer.Plan;
            curActivity.Started = DateTime.UtcNow;
            curActivity.Running = true;
            curActivity.Stopped = false;
            curActivity.Phase = "Starting";
            chat.Changed(curActivity);
            composer.Running = true;
            UpdateQueueNotes();
            SetPhase("Starting");
            PerformLayout();
            host.RunStarted(run);
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
            CancelToken ct = cancel;
            RunChangeLog log = runLog;
            bool isDefault;
            string ws = host.WorkspaceFolder(out isDefault);
            BrowserPane browser = host.Browser;
            bool browserOk = settings.AiBrowser && browser != null && browser.Available;
            bool appsOk = settings.AiApps && AgentToolbox.AppsSupported;
            bool memoryOn = settings.AiMemory && !temporary;
            string notes = ReadMemory(ws);
            AgentSession sess = session;
            Bridge ui = new Bridge(this, run);
            replyFirst = false;
            session.InboxRead = delegate (List<int> ids) { ui.Post(delegate { OnInboxRead(ids); }); };
            Thread t = new Thread(delegate ()
            {
                string error = null;
                AgentError agentError = null;
                try
                {
                    ModelEntry m;
                    LlmBackend be;
                    string err = PrepareModel(ct, delegate (string p) { ui.Phase(p); }, out m, out be);
                    if (ct.IsCancelled) return;
                    if (err != null) { error = err; return; }
                    AgentConfig cfg = new AgentConfig();
                    cfg.Backend = be;
                    cfg.Model = m;
                    cfg.ContextTokens = m.Context > 0 ? m.Context : (m.Cloud ? 200000 : 32768);
                    cfg.MaxOutput = m.Cloud ? (m.MaxOutput > 0 ? Math.Min(m.MaxOutput, 64000) : 32000) : Math.Max(2048, Math.Min(16384, cfg.ContextTokens / 3));
                    cfg.Vision = m.Vision;
                    cfg.Workspace = ws;
                    cfg.WorkspaceIsDefault = isDefault;
                    cfg.Effort = CurrentEffortFor(m);
                    cfg.ShowThinking = settings.AiShowThinking;
                    cfg.AutoSave = settings.AiAutoSave;
                    cfg.OpenFiles = settings.AiOpenFiles;
                    cfg.WebAllowed = settings.AiWeb;
                    cfg.BrowserAllowed = browserOk;
                    cfg.TryCaptchas = settings.AiCaptcha && browserOk;
                    cfg.SideBySide = settings.AiSideBySide;
                    cfg.Sandbox = settings.AiSandbox && Sandbox.Supported;
                    cfg.OtherApps = appsOk && settings.AiOtherApps;
                    if (!cfg.OtherApps) AppTool.ForgetGrants(sess);
                    if (browserOk) { browser.CaptchaMode = cfg.TryCaptchas; browser.AgentCanSee = cfg.Vision; }
                    cfg.CustomInstructions = settings.AiInstructions;
                    cfg.RunId = run;
                    if (browserOk) browser.SnapshotChars = Math.Max(6000, Math.Min(24000, (int)(cfg.ContextTokens * 0.15 * 3.3)));
                    bool helpersOk = settings.AiHelpers;
                    List<AgentTool> tools = AgentToolbox.Create(settings.AiWeb, browserOk, cfg.Vision, cfg.ContextTokens, memoryOn, appsOk, helpersOk, Helpers.Parallel(cfg));
                    AgentToolbox.UseSandbox(tools, cfg.Sandbox);
                    if (cfg.OtherApps) AgentToolbox.UseOtherApps(tools);
                    bool helperTools = false;
                    foreach (AgentTool at in tools) if (at.Name == "delegate") helperTools = true;
                    sess.AutoReview = helperTools && settings.AiAutoReview;
                    bool appTools = false;
                    foreach (AgentTool at in tools) if (at.Name == "app_read") appTools = true;
                    bool browserTools = false;
                    foreach (AgentTool at in tools) if (at.Name == "browser_navigate") browserTools = true;
                    string key = SystemPrompt.Key(cfg, sess.Perms, browserTools, cfg.Vision, appTools, memoryOn) + (helperTools ? "|h" + (sess.AutoReview ? "r" : "") : "") + (cfg.TryCaptchas ? "|c" : "") + (Net.Offline ? "|o" : "") + "|n" + (notes ?? "").GetHashCode().ToString(CultureInfo.InvariantCulture);
                    if (sess.FrozenSystem == null || sess.FrozenKey != key)
                    {
                        if (sess.FrozenSystem != null) sess.DropOldThinking();
                        string mem = memoryOn ? MemoryStore.ForPrompt(Math.Max(800, Math.Min(4000, (int)(cfg.ContextTokens * 3.3 * 0.04)))) : null;
                        sess.FrozenSystem = SystemPrompt.Build(cfg, sess.Perms, notes, mem, browserTools, cfg.Vision, appTools, memoryOn, helperTools, sess.AutoReview);
                        sess.FrozenKey = key;
                    }
                    string system = sess.FrozenSystem;
                    if (cfg.ContextTokens < 16000 && m.Provider == "lmstudio") ui.Notice("This model is loaded with a small context window (" + (cfg.ContextTokens / 1024) + "K tokens), so the agent can only keep a little of the conversation in mind and the browser tools are off. Load it with 32K or more for agent work (Settings > Models).", 1);
                    ui.Model(m);
                    if (!cfg.Vision && msg.Images != null && msg.Images.Count > 0)
                        ui.Notice(m.Display + " can't see pictures, so " + (msg.Images.Count == 1 ? "the picture" : "the pictures") + " in your message did not reach it. Pick a model that can (marked with an eye in the model list), or describe what it shows.", 1);
                    sess.Run(msg, cfg, tools, ui, host.Editor, browserOk ? browser : null, ct, log, system);
                }
                catch (AgentError ae) { agentError = ae; }
                catch (Exception ex) { error = "Something went wrong: " + ex.Message; }
                finally
                {
                    string e2 = error;
                    AgentError ae2 = agentError;
                    ui.Post(delegate { EndRun(run, e2, ae2, msg); });
                }
            });
            t.IsBackground = true;
            t.Name = "MinGent agent";
            t.Start();
        }

        string CurrentEffortFor(ModelEntry m)
        {
            return EffectiveEffort(m);
        }

        string ReadMemory(string ws)
        {
            return ProjectNotes.Read(ws);
        }

        void Stop()
        {
            if (!running || cancel == null) return;
            cancel.Cancel();
            SetPhase("Stopping");
            ChatItem pending = chat.PendingPermission();
            if (pending != null) chat.Resolve(pending, PermissionDecision.Deny);
            ChatItem pq = chat.PendingQuestion();
            if (pq != null) chat.AnswerQuestion(pq, UserAnswer.None());
        }

        void EndRun(int run, string error, AgentError ae, Msg msg)
        {
            if (run != liveRun) return;
            StampTurns();
            running = false;
            liveRun = -1;
            AppHighlight.Hide();
            composer.Running = false;
            FinishStreaming();
            UpdateModelChip();
            ChatItem pending = chat.PendingPermission();
            if (pending != null) chat.Resolve(pending, PermissionDecision.Deny);
            ChatItem pq = chat.PendingQuestion();
            if (pq != null) chat.AnswerQuestion(pq, UserAnswer.None());
            composer.Prompt = afterRunHint;
            afterRunHint = null;
            foreach (ChatItem it in toolItems.Values) if (it.Status == 0) { it.Status = 3; it.Summary = "stopped"; chat.Changed(it); }
            bool stopped = cancel != null && cancel.IsCancelled;
            if (curActivity != null)
            {
                curActivity.Running = false;
                curActivity.Seconds += Math.Max(0, (DateTime.UtcNow - curActivity.Started).TotalSeconds);
                curActivity.Stopped = stopped;
                chat.Changed(curActivity);
            }
            foreach (ChatItem a in chat.Items.ToArray()) if (a.Kind == ChatKind.Activity && a.Running) StopActivity(a, stopped);
            CheckInbox();
            DetachQueue();
            if (ae != null) error = Friendly(ae.Result, ModelRef().StartsWith("lmstudio:", StringComparison.Ordinal));
            bool failed = error != null && error != "stopped";
            if (failed)
            {
                ChatItem err = new ChatItem();
                err.Kind = ChatKind.Error;
                err.Text = error;
                bool libraryFix = error.IndexOf("model library", StringComparison.OrdinalIgnoreCase) >= 0;
                bool settingsFix = error.IndexOf("Settings", StringComparison.Ordinal) >= 0 || error.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0;
                bool modelFix = error.IndexOf("model menu", StringComparison.OrdinalIgnoreCase) >= 0 || error.IndexOf("Choose", StringComparison.Ordinal) >= 0;
                if (libraryFix) { err.ActionLabel = "Open the model library"; err.Action = delegate { host.ShowModelLibrary(); }; }
                else if (settingsFix) { err.ActionLabel = "Open settings"; err.Action = delegate { host.ShowSettings(); }; }
                else if (modelFix) { err.ActionLabel = "Choose a model"; err.Action = delegate { ShowModelMenu(); }; }
                else
                {
                    bool partial = session.History.Contains(msg) && session.History.Count > 0 && session.History[session.History.Count - 1] != msg;
                    err.ActionLabel = partial ? "Continue" : "Try again";
                    ChatItem self = err;
                    string why = error;
                    err.Action = delegate
                    {
                        if (running) return;
                        int at = chat.Items.IndexOf(self);
                        for (int k = at + 1; at >= 0 && k < chat.Items.Count; k++)
                        {
                            if (chat.Items[k].Kind == ChatKind.User && !chat.Items[k].MidRun)
                            {
                                Notice("Only your latest message can be sent again. Type a new message instead.", 1);
                                return;
                            }
                        }
                        chat.Items.Remove(self);
                        chat.Changed(self);
                        Retry(msg, why);
                    };
                }
                chat.Add(err);
            }
            else if (stopped) Notice("Stopped.", 0);
            if (runLog != null) runLog.Seal();
            int changes = host.RunChangeCount(run, runLog);
            if (changes > 0)
            {
                ChatItem foot = new ChatItem();
                foot.Kind = ChatKind.Footer;
                foot.RunId = run;
                foot.Text = changes == 1 ? "1 file changed" : changes + " files changed";
                foot.ActionLabel = "Undo";
                foot.Changes = runLog;
                RunChangeLog log = runLog;
                ChatItem self = foot;
                foot.Action = delegate
                {
                    if (running) { Notice("Stop the current task before undoing.", 1); return; }
                    int reverted, kept;
                    string summary = host.UndoRun(run, log, out reverted, out kept);
                    self.Undone = true;
                    self.UndoneText = kept == 0 ? null : reverted > 0 ? "Partly undone (" + kept + " kept)" : "Nothing undone (" + kept + " kept)";
                    chat.Changed(self);
                    if (!string.IsNullOrEmpty(summary)) Notice(summary, 0);
                };
                chat.Add(foot);
            }
            if (!failed && !stopped && queued.Count == 0 && composer.Plan && !planAsked) OfferPlan();
            planLater = !failed && !stopped && queued.Count > 0 && composer.Plan && !planAsked;
            SaveChat();
            if (session.Todos.Count > 0) { todo.SetItems(session.Todos); }
            PerformLayout();
            Form f = FindForm();
            if (f != null && Form.ActiveForm != f) FlashTaskbar(f);
            FlushQueue(!failed && !stopped && afterStop == null);
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
            if (afterStop != null && !running)
            {
                Action a = afterStop;
                afterStop = null;
                a();
            }
        }

        void Retry(Msg msg, string why)
        {
            int last = session.History.Count - 1;
            bool summarized = session.RunMessage == msg && session.Summary != null && !session.History.Contains(msg);
            if (!summarized && (!session.History.Contains(msg) || (last >= 0 && session.History[last] == msg)))
            {
                if (last >= 0 && session.History[last] == msg) session.History.RemoveAt(last);
                Begin(msg);
                return;
            }
            Msg cont = new Msg("user", "[MinGent] The previous attempt stopped because of an error: " + Clip(why, 300) +
                "\nContinue the task from where it stopped. Steps that already succeeded are done; don't repeat them.");
            cont.Hidden = true;
            Begin(cont);
        }

        static string Friendly(LlmResult r, bool lmStudio)
        {
            if (r == null) return "The model did not answer.";
            if (r.EngineGone) return r.Error;
            if (r.Unauthorized) return "The API key was rejected (" + r.Error + "). Check it in Settings.";
            if (r.ConnectFailed) return "Could not reach the model server. " + (r.Error ?? "") + (lmStudio ? "\nMake sure LM Studio's server is running (Developer tab)." : "");
            if (r.ContextOverflow) return "The conversation is too long for this model's context window, even after summarizing. Start a new chat, or load the model with a larger context.";
            if (r.Status == 429) return "The provider is rate-limiting requests (HTTP 429). Wait a moment and try again.";
            if (r.Status >= 500) return "The model server had a problem (HTTP " + r.Status + "): " + r.Error;
            return "The model returned an error: " + r.Error;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool FlashWindow(IntPtr hwnd, bool invert);

        static void FlashTaskbar(Form f)
        {
            try { FlashWindow(f.Handle, true); } catch { }
        }

        void Attention(Form f)
        {
            if (f == null || Form.ActiveForm == f) return;
            if (AppControl.AgentInFront(settings.AiOtherApps ? AppTool.GrantsOf(session) : null)) AppHighlight.Raise(f);
            FlashTaskbar(f);
        }

        class Bridge : IAgentUi
        {
            readonly AiPanel p;
            readonly int run;
            public Bridge(AiPanel p, int run) { this.p = p; this.run = run; }

            public void Post(Action a) { p.Enqueue(run, 0, null, a); }
            public void Model(ModelEntry m) { Post(delegate { p.OnModelReady(m); }); }
            public void Phase(string text) { Post(delegate { p.SetPhase(text); }); }
            public void TextDelta(string d) { p.Enqueue(run, 1, d, null); }
            public void ReasoningDelta(string d) { p.Enqueue(run, 2, d, null); }
            public void StreamReset() { Post(delegate { p.OnStreamReset(); }); }
            public void AssistantDone(Msg m) { Post(delegate { p.OnAssistantDone(m); }); }
            public void ToolStart(ToolCall c, string label) { Post(delegate { p.OnToolStart(c, label); }); }
            public void ToolOutput(ToolCall c, string text) { Post(delegate { p.OnToolOutput(c, text); }); }
            public void ToolEnd(ToolCall c, ToolOutcome o) { Post(delegate { p.OnToolEnd(c, o); }); }
            public void Todos(List<TodoItem> items) { List<TodoItem> copy = new List<TodoItem>(items); Post(delegate { p.todo.SetItems(copy); p.ShowTask(copy); p.PerformLayout(); }); }
            public void Notice(string text, int kind) { Post(delegate { p.Notice(text, kind); }); }
            public void Usage(int used, int context) { Post(delegate { p.composer.SetUsage(used, context); }); }

            public UserAnswer AskUser(UserQuestion q, CancelToken ct)
            {
                if (q == null) return UserAnswer.None();
                ManualResetEvent done = new ManualResetEvent(false);
                UserAnswer result = null;
                ChatItem item = null;
                bool finished = false;
                Post(delegate
                {
                    item = p.AddQuestion(q, delegate (UserAnswer a)
                    {
                        result = a;
                        lock (done) { if (!finished) done.Set(); }
                    });
                });
                DateTime next = DateTime.UtcNow.AddMilliseconds(1500);
                try
                {
                    while (!done.WaitOne(100))
                    {
                        if (ct.IsCancelled)
                        {
                            Post(delegate { if (item != null && item.Decision < 0) p.chat.AnswerQuestion(item, UserAnswer.None()); });
                            return UserAnswer.None();
                        }
                        if (q.DoneWhen == null || DateTime.UtcNow < next) continue;
                        bool cleared = false;
                        try { cleared = q.DoneWhen(); } catch { }
                        next = DateTime.UtcNow.AddMilliseconds(1200);
                        if (!cleared || ct.IsCancelled) continue;
                        if (done.WaitOne(0)) break;
                        UserAnswer auto = new UserAnswer();
                        auto.Auto = true;
                        auto.Choice = 0;
                        Post(delegate { if (item != null && item.Decision < 0) p.chat.AnswerQuestion(item, auto); });
                        return auto;
                    }
                }
                finally
                {
                    lock (done) { finished = true; }
                    done.Close();
                }
                return result ?? UserAnswer.None();
            }

            public PermissionDecision Ask(PermissionAsk a, CancelToken ct)
            {
                ManualResetEvent done = new ManualResetEvent(false);
                PermissionDecision result = PermissionDecision.Deny;
                ChatItem item = null;
                bool finished = false;
                Post(delegate
                {
                    item = p.AddPermission(a, delegate (PermissionDecision d)
                    {
                        result = d;
                        lock (done) { if (!finished) done.Set(); }
                    });
                });
                try
                {
                    while (!done.WaitOne(100))
                    {
                        if (ct.IsCancelled)
                        {
                            Post(delegate { if (item != null && item.Decision < 0) p.chat.Resolve(item, PermissionDecision.Deny); });
                            return PermissionDecision.Deny;
                        }
                    }
                }
                finally
                {
                    lock (done) { finished = true; }
                    done.Close();
                }
                return result;
            }
        }

        void Enqueue(int run, int kind, string text, Action act)
        {
            lock (qGate)
            {
                if (kind != 0 && queue.Count > 0)
                {
                    UiEvent last = queue[queue.Count - 1];
                    if (last.Kind == kind && last.Run == run) { last.Text.Append(text); return; }
                }
                UiEvent e = new UiEvent();
                e.Run = run;
                e.Kind = kind;
                e.Act = act;
                if (kind != 0) e.Text = new StringBuilder(text);
                queue.Add(e);
            }
        }

        void Drain()
        {
            UiEvent[] batch;
            lock (qGate)
            {
                if (queue.Count == 0) { CheckInbox(); return; }
                batch = queue.ToArray();
                queue.Clear();
            }
            foreach (UiEvent e in batch)
            {
                if (e.Run != liveRun) continue;
                try
                {
                    if (e.Kind == 0) e.Act();
                    else if (e.Kind == 1) AppendText(e.Text.ToString());
                    else AppendReasoning(e.Text.ToString());
                }
                catch (Exception ex) { try { Notice("Display error: " + ex.Message, 1); } catch { } }
            }
            CheckInbox();
        }

        void OnModelReady(ModelEntry m)
        {
            if (m.Provider == "lmstudio" && (settings.AiModel ?? "") == "lmstudio:") modelChip.Set(m.Display, "", 1);
        }

        void ShowTask(List<TodoItem> items)
        {
            if (!running || curActivity == null) return;
            TodoItem t = TodoStrip.Current(items);
            string now = t == null ? "" : t.Content;
            if (curActivity.CurrentTask == now) return;
            curActivity.CurrentTask = now;
            chat.InvalidateItem(curActivity);
        }

        void SetPhase(string text)
        {
            if (!running || curActivity == null) return;
            if (curActivity.Phase == text) return;
            curActivity.Phase = text;
            chat.InvalidateItem(curActivity);
        }

        void AppendReasoning(string d)
        {
            if (!settings.AiShowThinking) return;
            if (curThinking == null || !curThinking.Streaming)
            {
                curThinking = new ChatItem();
                curThinking.Kind = ChatKind.Thinking;
                curThinking.Streaming = true;
                thinkingStart = DateTime.UtcNow;
                chat.Add(curThinking);
            }
            curThinking.Text += d;
            if (curThinking.Expanded) chat.Changed(curThinking);
        }

        void OnStreamReset()
        {
            foreach (ChatItem it in new ChatItem[] { curAssistant, curThinking })
            {
                if (it == null || !it.Streaming) continue;
                it.Streaming = false;
                chat.Items.Remove(it);
                chat.Changed(it);
            }
            curAssistant = null;
            curThinking = null;
        }

        void EndThinking()
        {
            if (curThinking != null && curThinking.Streaming)
            {
                curThinking.Streaming = false;
                curThinking.Seconds = (DateTime.UtcNow - thinkingStart).TotalSeconds;
                chat.Changed(curThinking);
            }
        }

        void AppendText(string d)
        {
            EndThinking();
            if (curAssistant == null || !curAssistant.Streaming)
            {
                if (d.Trim().Length == 0 && curAssistant == null) return;
                curAssistant = new ChatItem();
                curAssistant.Kind = ChatKind.Assistant;
                curAssistant.Streaming = true;
                AddReply(curAssistant);
            }
            curAssistant.Text += d;
            chat.Changed(curAssistant);
        }

        void FinishStreaming()
        {
            EndThinking();
            if (curAssistant != null && curAssistant.Streaming)
            {
                curAssistant.Streaming = false;
                if (curAssistant.Text.Trim().Length == 0) chat.Items.Remove(curAssistant);
                chat.Changed(curAssistant);
            }
        }

        void OnAssistantDone(Msg m)
        {
            EndThinking();
            if (curThinking != null && string.IsNullOrEmpty(curThinking.Text) && !string.IsNullOrEmpty(m.Reasoning)) { curThinking.Text = m.Reasoning; chat.Changed(curThinking); }
            string text = m.Text ?? "";
            if (curAssistant != null && curAssistant.Streaming)
            {
                curAssistant.Streaming = false;
                curAssistant.Text = text;
                curAssistant.Speed = m.Speed;
                curAssistant.FirstToken = m.FirstToken;
                curAssistant.SpeedGuess = m.SpeedGuess;
                if (text.Trim().Length == 0) chat.Items.Remove(curAssistant);
                chat.Changed(curAssistant);
            }
            else if (text.Trim().Length > 0 && !text.StartsWith("(stopped"))
            {
                ChatItem a = new ChatItem();
                a.Kind = ChatKind.Assistant;
                a.Text = text;
                a.Speed = m.Speed;
                a.FirstToken = m.FirstToken;
                a.SpeedGuess = m.SpeedGuess;
                AddReply(a);
            }
            curAssistant = null;
            curThinking = null;
        }

        bool replyFirst;

        void AddReply(ChatItem a)
        {
            if (replyFirst && curActivity != null && chat.Items.Contains(curActivity))
            {
                replyFirst = false;
                chat.InsertBefore(a, curActivity);
            }
            else chat.Add(a);
        }

        void OnInboxRead(List<int> ids)
        {
            List<ChatItem> read = new List<ChatItem>();
            foreach (ChatItem u in queued) if (u.InboxId > 0 && ids.Contains(u.InboxId)) read.Add(u);
            if (read.Count == 0) { CheckInbox(); return; }
            FinishStreaming();
            curAssistant = null;
            curThinking = null;
            foreach (ChatItem u in read)
            {
                queued.Remove(u);
                u.Queued = false;
                u.InboxId = 0;
                if (chat.EditingItem == u) chat.CancelEdit();
                u.Touch();
                chat.Items.Remove(u);
                chat.Add(u);
            }
            if (running && curActivity != null && curActivity.Label.Length == 0)
            {
                if (!StillWorking(curActivity)) StopActivity(curActivity, false);
                ChatItem next = new ChatItem();
                next.Kind = ChatKind.Activity;
                next.Turn = curTurn;
                next.RunId = liveRun;
                next.Started = DateTime.UtcNow;
                next.Running = true;
                next.Expanded = curActivity.Expanded;
                next.Phase = "Reading your message";
                next.CurrentTask = curActivity.CurrentTask;
                chat.Add(next);
                curActivity = next;
                replyFirst = true;
            }
            UpdateQueueNotes();
            chat.ScrollToEnd();
        }

        static bool StillWorking(ChatItem activity)
        {
            if (activity.Members != null)
                foreach (ChatItem m in activity.Members) if (m.Kind == ChatKind.Tool && m.Status == 0) return true;
            return false;
        }

        void StopActivity(ChatItem activity, bool stopped)
        {
            if (!activity.Running) return;
            activity.Running = false;
            activity.Seconds += Math.Max(0, (DateTime.UtcNow - activity.Started).TotalSeconds);
            if (stopped) activity.Stopped = true;
            chat.Changed(activity);
        }

        static string IconFor(string tool)
        {
            switch (tool)
            {
                case "read_file": return "file";
                case "edit_file": case "write_file": return "pencil";
                case "delete_file": return "trash";
                case "list_directory": case "find_files": return "folder";
                case "search_text": return "search";
                case "run_command": case "command_status": return "terminal";
                case "web_search": return "search";
                case "web_fetch": return "globe";
                case "todo_write": return "checklist";
                case "delegate": return "branch";
                case "open_in_editor": return "file";
                case "ask_user": return "ask";
                case "present_plan": return "map";
                case "analyze_audio": return "wave";
                case "browser_console": return "terminal";
            }
            if (tool.StartsWith("browser_")) return "cursor";
            return "dot";
        }

        void OnToolStart(ToolCall c, string label)
        {
            ChatItem card = null;
            if (c.Under != null && !toolItems.TryGetValue(c.Under, out card)) return;
            if (card == null)
            {
                FinishStreaming();
                curAssistant = null;
                replyFirst = false;
            }
            ChatItem it = new ChatItem();
            it.Kind = ChatKind.Tool;
            it.CallId = c.Id + "#" + c.Name;
            it.Label = label;
            it.Icon = IconFor(c.Name);
            it.Status = 0;
            toolItems[it.CallId] = it;
            if (card != null) chat.AddUnder(card, it);
            else chat.Add(it);
        }

        void OnToolOutput(ToolCall c, string text)
        {
            ChatItem it;
            if (!toolItems.TryGetValue(c.Id + "#" + c.Name, out it)) return;
            it.Live = text ?? "";
            chat.Changed(it);
        }

        int EngineContextFor(LocalModel lm)
        {
            int want = settings.EngineContext;
            if (want > 0 && lm != null && lm.TrainContext > 0 && want > lm.TrainContext) want = lm.TrainContext;
            return want;
        }

        void OnToolEnd(ToolCall c, ToolOutcome o)
        {
            if (o.Background) return;
            ChatItem it;
            if (!toolItems.TryGetValue(c.Id + "#" + c.Name, out it))
            {
                ChatItem card = null;
                if (c.Under != null && !toolItems.TryGetValue(c.Under, out card)) return;
                it = new ChatItem();
                it.Kind = ChatKind.Tool;
                it.CallId = c.Id + "#" + c.Name;
                it.Label = AgentSession.Pretty(c.Name);
                it.Icon = IconFor(c.Name);
                toolItems[it.CallId] = it;
                if (card != null) chat.AddUnder(card, it);
                else chat.Add(it);
            }
            it.Status = o.Denied ? 3 : o.Error ? 2 : 1;
            string summary = o.Summary ?? "";
            if (summary.Length > 160) summary = summary.Substring(0, 160) + "...";
            it.Summary = summary.Replace('\n', ' ');
            string detail = o.Detail;
            if (detail == null)
            {
                string t = o.Text ?? "";
                if (c.Name == "todo_write" || c.Name == "read_file") t = c.Name == "read_file" && o.Error ? t : "";
                if (t.StartsWith("<untrusted_content")) t = t.Substring(t.IndexOf('\n') + 1);
                detail = t.Length > 8000 ? t.Substring(0, 8000) + "\n..." : t;
            }
            it.Detail = detail ?? "";
            it.Report = "";
            if (o.Open != null && !o.Error)
            {
                string open = o.Open;
                it.ActionLabel = "Open";
                it.Action = delegate { host.OpenLink(open); };
            }
            if (c.Name == "delegate" && !o.Error && o.Detail != null)
            {
                string full = o.Text ?? "";
                int cut = full.IndexOf("\n\n", StringComparison.Ordinal);
                string report = cut > 0 ? full.Substring(cut + 2) : full;
                if (report.StartsWith("<untrusted_content")) report = report.Substring(report.IndexOf('\n') + 1);
                it.Report = report.Trim();
                int at = o.Detail.LastIndexOf(it.Report, StringComparison.Ordinal);
                it.Detail = at > 0 ? o.Detail.Substring(0, at).TrimEnd() : "";
            }
            if (it.Status == 2 && it.Detail.Length == 0) it.Detail = o.Text ?? "";
            chat.Changed(it);
            ChatItem group = it.Group;
            if (group != null && group != curActivity && group.Kind == ChatKind.Activity && group.Running && !StillWorking(group)) StopActivity(group, false);
        }

        bool planAsked, approveNext;

        static Control Innermost(ContainerControl c)
        {
            Control a = c == null ? null : c.ActiveControl;
            while (a is ContainerControl && ((ContainerControl)a).ActiveControl != null) a = ((ContainerControl)a).ActiveControl;
            return a;
        }

        ChatItem AddQuestion(UserQuestion q, Action<UserAnswer> answered)
        {
            FinishStreaming();
            curAssistant = null;
            if (q.PlanApproval) planAsked = true;
            if (!string.IsNullOrEmpty(q.Plan))
            {
                ChatItem plan = new ChatItem();
                plan.Kind = ChatKind.Assistant;
                plan.Text = q.Plan;
                chat.Add(plan);
            }
            ChatItem it = new ChatItem();
            it.Kind = ChatKind.Question;
            it.Question = q;
            it.Turn = curTurn;
            it.RunId = liveRun;
            it.Answered = delegate (UserAnswer a)
            {
                composer.Prompt = null;
                if (q.PlanApproval) PlanAnswered(q, a);
                answered(a);
            };
            chat.Add(it);
            chat.ScrollToEnd();
            composer.Prompt = q.Wait ? "Type a message for the agent, or press Done when you're through" : "Answer the question above";
            Control active = Innermost(FindForm());
            if (composer.Text.Trim().Length == 0 && !composer.HasAttachments && !chat.Editing && (active == null || Contains(active))) chat.FocusAnswerWhenShown(it);
            if (curActivity != null) chat.InvalidateItem(curActivity);
            host.RevealAssistant();
            Attention(FindForm());
            return it;
        }

        void PlanAnswered(UserQuestion q, UserAnswer a)
        {
            if (a == null || a.Cancelled || a.Choice < 0 || a.Choice >= q.Options.Count) return;
            string c = q.Options[a.Choice];
            if (c == PresentPlanTool.Start || c == PresentPlanTool.StartAuto)
            {
                SetPlan(false);
                if (c == PresentPlanTool.StartAuto && level == "ask") SetLevel("auto");
                if (!running) approveNext = true;
            }
            else if (c == PresentPlanTool.Keep)
            {
                if (!composer.Plan) SetPlan(true);
                if (running) afterRunHint = KeepHint;
                else composer.Prompt = KeepHint;
            }
        }

        static readonly System.Text.RegularExpressions.Regex PlanStep = new System.Text.RegularExpressions.Regex(@"^\s*(\d{1,2}[.)]|[-*\u2022])\s+\S", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        void OfferPlan()
        {
            ChatItem last = null;
            for (int i = chat.Items.Count - 1; i >= 0; i--)
            {
                ChatItem it = chat.Items[i];
                if (it.Kind == ChatKind.Assistant) { last = it; break; }
                if (it.Kind == ChatKind.User || it.Kind == ChatKind.Question) return;
            }
            string t = last != null ? (last.Text ?? "").Trim() : "";
            if (t.Length < 80 || t.EndsWith("?") || PlanStep.Matches(t).Count < 2) return;
            UserQuestion q = new UserQuestion();
            q.Title = "Ready to start?";
            q.Detail = composer.Auto ? "Nothing has been changed yet. Auto is on, so the agent will only ask before risky steps." : "Nothing has been changed yet. Start keeps asking before commands and web access; Auto only asks before risky steps.";
            q.PlanApproval = true;
            q.Options.Add(PresentPlanTool.Start);
            if (!composer.Auto) q.Options.Add(PresentPlanTool.StartAuto);
            q.Options.Add(PresentPlanTool.Keep);
            ChatItem card = new ChatItem();
            card.Kind = ChatKind.Question;
            card.Question = q;
            card.Turn = curTurn;
            card.RunId = -1;
            card.Answered = delegate (UserAnswer a)
            {
                composer.Prompt = null;
                PlanAnswered(q, a);
                if (a != null && !a.Cancelled && a.Choice < 0 && !string.IsNullOrEmpty(a.Text) && !running)
                {
                    string said = a.Text;
                    BeginInvoke(new Action(delegate { if (!running) StartRun(said, null, null, null, null); }));
                    return;
                }
                if (a == null || a.Cancelled || a.Choice < 0 || a.Choice >= q.Options.Count) return;
                string c = q.Options[a.Choice];
                if ((c == PresentPlanTool.Start || c == PresentPlanTool.StartAuto) && !running)
                    BeginInvoke(new Action(delegate { if (!running) StartRun("Go ahead with the plan.", null, null, null, null); }));
                else if (c == PresentPlanTool.Keep) composer.FocusInput();
            };
            chat.Add(card);
            chat.ScrollToEnd();
        }

        bool AnswerFromComposer(string text)
        {
            ChatItem pq = chat.PendingQuestion();
            if (pq == null || pq.Question == null || text.Length == 0 || pq.RunId != liveRun) return false;
            UserQuestion q = pq.Question;
            UserAnswer a = new UserAnswer();
            int n;
            if (q.Wait)
            {
                a.Choice = -1;
                a.Text = text;
                chat.AnswerQuestion(pq, a);
                return false;
            }
            for (int i = 0; i < q.Options.Count && a.Choice < 0; i++)
                if (string.Equals(q.Options[i].Trim(), text, StringComparison.OrdinalIgnoreCase)) a.Choice = i;
            if (a.Choice < 0 && q.Options.Count > 0 && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 1 && n <= q.Options.Count) a.Choice = n - 1;
            if (a.Choice < 0 && q.PlanApproval) a.Choice = PlanReply.Choice(q.Options, text);
            if (a.Choice < 0) a.Text = text;
            composer.Text = "";
            chat.AnswerQuestion(pq, a);
            return true;
        }

        ChatItem AddPermission(PermissionAsk a, Action<PermissionDecision> decide)
        {
            FinishStreaming();
            curAssistant = null;
            ChatItem it = new ChatItem();
            it.Kind = ChatKind.Permission;
            it.Ask = a;
            it.Decide = decide;
            chat.Add(it);
            chat.ScrollToEnd();
            if (curActivity != null) chat.InvalidateItem(curActivity);
            host.RevealAssistant();
            Attention(FindForm());
            return it;
        }

        string ChatDir { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), "chats"); } }
        string ArchiveDir { get { return Path.Combine(ChatDir, "archive"); } }

        public bool IsArchived(string path)
        {
            try { return string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)).TrimEnd('\\', '/'), Path.GetFullPath(ArchiveDir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public int ArchivedCount()
        {
            try { return Directory.Exists(ArchiveDir) ? Directory.GetFiles(ArchiveDir, "*.json").Length : 0; }
            catch { return 0; }
        }

        static string FreeName(string dir, string name)
        {
            string target = Path.Combine(dir, name);
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int n = 2; File.Exists(target) && n < 1000; n++) target = Path.Combine(dir, stem + "-" + n.ToString(CultureInfo.InvariantCulture) + ext);
            return target;
        }

        public void ArchiveChat(string path)
        {
            if (string.Equals(path, chatFile, StringComparison.OrdinalIgnoreCase)) WhenIdle("archive the chat", delegate { ArchiveNow(path); });
            else ArchiveNow(path);
        }

        void ArchiveNow(string path)
        {
            if (string.IsNullOrEmpty(path) || IsArchived(path) || !File.Exists(path)) { Changed(); return; }
            bool active = string.Equals(path, chatFile, StringComparison.OrdinalIgnoreCase);
            if (active) SaveChat();
            try
            {
                Directory.CreateDirectory(ArchiveDir);
                string target = FreeName(ArchiveDir, Path.GetFileName(path));
                if (active)
                {
                    ClearView(false, false);
                    host.ResetBrowser();
                }
                File.Move(path, target);
                SecureFile.Erase(path + ".tmp");
            }
            catch (Exception ex)
            {
                Alert.Show(FindForm(), th, "MinGent could not archive this chat: " + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            titleCache.Remove(path);
            if (string.Equals(settings.AiLastChat, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) { settings.AiLastChat = ""; settings.Save(); }
            NotesChanged();
            Changed();
        }

        public string RestoreChat(string path)
        {
            if (string.IsNullOrEmpty(path) || !IsArchived(path) || !File.Exists(path)) return null;
            try
            {
                Directory.CreateDirectory(ChatDir);
                string target = FreeName(ChatDir, Path.GetFileName(path));
                File.Move(path, target);
                titleCache.Remove(path);
                NotesChanged();
                Changed();
                return target;
            }
            catch (Exception ex)
            {
                Alert.Show(FindForm(), th, "MinGent could not bring this chat back: " + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        public List<ChatInfo> ListArchived()
        {
            return ListIn(ArchiveDir);
        }

        void NewChatFile(string firstText)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(ChatDir, stamp + ".json");
            for (int n = 2; File.Exists(path) && n < 100; n++) path = Path.Combine(ChatDir, stamp + "-" + n + ".json");
            chatFile = path;
            chatStamp = DateTime.MinValue;
            savedJson = null;
            chatTitle = TitleFrom(firstText);
            titleSet = false;
            NotesChanged();
        }

        static string TitleFrom(string text)
        {
            string t = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            while (t.IndexOf("  ", StringComparison.Ordinal) >= 0) t = t.Replace("  ", " ");
            t = t.TrimStart('#', '>', '-', '*', ' ');
            if (t.Length <= 48) return t;
            int cut = t.LastIndexOf(' ', 48);
            if (cut < 24) cut = 48;
            return t.Substring(0, cut).TrimEnd(',', '.', ';', ':', ' ') + "\u2026";
        }

        void Detach()
        {
            if (running)
            {
                if (cancel != null) cancel.Cancel();
                ChatItem pending = chat.PendingPermission();
                if (pending != null) chat.Resolve(pending, PermissionDecision.Deny);
                ChatItem pq = chat.PendingQuestion();
                if (pq != null) chat.AnswerQuestion(pq, UserAnswer.None());
                running = false;
                liveRun = -1;
                composer.Running = false;
            }
            AppHighlight.Hide();
            lock (qGate) queue.Clear();
            queued.Clear();
            afterStop = null;
            curActivity = null;
            session = new AgentSession();
            approveNext = false;
            session.Perms.Mode = level;
            session.Perms.WebGuard = settings.AiWebGuard;
            session.Perms.SetAutoAllows(settings.AutoAllowList);
            session.Perms.Planning = composer.Plan;
            curAssistant = null;
            curThinking = null;
            toolItems.Clear();
        }

        void DiscardTemporary()
        {
            if (temporary && chat.Items.Count > 0) host.ClearBrowserHistory();
        }

        void ClearView(bool temp, bool sameChat)
        {
            ParkQueue();
            DiscardTemporary();
            Detach();
            chat.Clear();
            todo.SetItems(new List<TodoItem>());
            chatFile = null;
            savedJson = null;
            chatTitle = "";
            titleSet = false;
            temporary = temp;
            if (!sameChat) conversation++;
            UpdateTemporaryUi();
            curTurn = turnCounter = 0;
            composer.ClearUsage();
            composer.ClearAttachments();
            composer.Prompt = null;
            afterRunHint = null;
            composer.Invalidate();
            UpdateEmptyState();
            PerformLayout();
            Invalidate();
            NotesChanged();
        }

        void Changed()
        {
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
            if (ChatsChanged != null) ChatsChanged(this, EventArgs.Empty);
        }

        void WhenIdle(string what, Action act)
        {
            if (!running) { act(); return; }
            DialogResult r = Alert.Choose(FindForm(), th, "The agent is still working in this chat. Stop it and " + what + "?", Program.AppName, MessageBoxIcon.Question,
                new string[] { "Stop it", "Keep working" }, new DialogResult[] { DialogResult.Yes, DialogResult.No }, 0, 1);
            if (r != DialogResult.Yes) return;
            if (!running) { act(); return; }
            afterStop = act;
            Stop();
        }

        public void StopThen(Action act)
        {
            if (!running) { act(); return; }
            afterStop = act;
            Stop();
        }

        public void NewChat()
        {
            WhenIdle("start a new chat", delegate
            {
                bool had = chatFile != null || chat.Items.Count > 0;
                SaveChat();
                ClearView(false, false);
                if (had) host.ResetBrowser();
                Changed();
                composer.FocusInput();
            });
        }

        public void OpenChat(string path)
        {
            if (string.Equals(path, chatFile, StringComparison.OrdinalIgnoreCase)) { composer.FocusInput(); return; }
            WhenIdle("open the other chat", delegate
            {
                SaveChat();
                if (!File.Exists(path)) { Changed(); return; }
                LoadChat(path);
                settings.AiLastChat = Path.GetFileName(path);
                settings.Save();
                host.ResetBrowser();
                UpdateEmptyState();
                PerformLayout();
                Invalidate();
                Changed();
                composer.FocusInput();
            });
        }

        public string ChatTitleOf(string path)
        {
            if (string.Equals(path, chatFile, StringComparison.OrdinalIgnoreCase)) return chatTitle ?? "";
            DateTime w;
            try { w = File.GetLastWriteTimeUtc(path); } catch { return ""; }
            return ReadTitle(path, w);
        }

        List<string> ChatFacts(string path)
        {
            List<string> l = new List<string>();
            if (string.Equals(path, chatFile, StringComparison.OrdinalIgnoreCase)) lock (session.Remembered) l.AddRange(session.Remembered);
            try
            {
                if (File.Exists(path))
                {
                    List<object> mem = Json.List(Json.TryParse(File.ReadAllText(path, Encoding.UTF8)), "mem");
                    if (mem != null)
                        foreach (object o in mem)
                        {
                            string f = o as string;
                            if (!string.IsNullOrEmpty(f) && !l.Contains(f)) l.Add(f);
                        }
                }
            }
            catch { }
            return l;
        }

        static string Things(int n) { return n == 1 ? "the 1 thing" : "the " + n + " things"; }

        public static string NoteNames(List<string> names)
        {
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return names[0] + " and " + names[1];
            if (names.Count == 3) return names[0] + ", " + names[1] + " and " + names[2];
            return names[0] + ", " + names[1] + " and " + (names.Count - 2).ToString(CultureInfo.InvariantCulture) + " more";
        }

        public bool ConfirmAndDelete(string path)
        {
            bool active = string.Equals(path, chatFile, StringComparison.OrdinalIgnoreCase);
            string title = ChatTitleOf(path);
            int facts = MemoryStore.CountPresent(ChatFacts(path));
            string head = active && running ? "The agent is still working in this chat. Stop it and delete the chat?" :
                          "Delete \u201C" + (title.Length > 0 ? title : "Untitled chat") + "\u201D?";
            List<string> notes = host.UnsavedNotes(Path.GetFileName(path));
            string body = "It is erased from this PC and can't be brought back." +
                          (facts > 0 ? " " + char.ToUpperInvariant(Things(facts)[0]) + Things(facts).Substring(1) + " the agent remembered in this chat " + (facts == 1 ? "is" : "are") + " forgotten too." : "") +
                          (notes != null && notes.Count > 0 ? "\n\nIts notes close with it, and unsaved changes in " + NoteNames(notes) + " are lost. Files saved on your PC stay." : "");
            if (!Alert.Confirm(FindForm(), th, head + "\n\n" + body, Program.AppName, active && running ? "Stop and delete" : "Delete", true)) return false;
            active = string.Equals(path, chatFile, StringComparison.OrdinalIgnoreCase);
            List<string> list = ChatFacts(path);
            if (active)
            {
                ClearView(false, false);
                host.ResetBrowser();
            }
            SecureFile.Erase(path + ".tmp");
            bool gone = SecureFile.Erase(path);
            MemoryStore.Forget(list);
            titleCache.Remove(path);
            host.ClearBrowserHistory();
            if (!gone) Alert.Show(FindForm(), th, "MinGent could not erase this chat file. Another program may have it open:\n" + path, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (string.Equals(settings.AiLastChat, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) { settings.AiLastChat = ""; settings.Save(); }
            if (gone) deletedChats.Add(Path.GetFileName(path));
            NotesChanged();
            Changed();
            return gone;
        }

        public void RenameChat(string path, string title)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0) return;
            if (title.Length > 120) title = title.Substring(0, 120);
            if (string.Equals(path, chatFile, StringComparison.OrdinalIgnoreCase))
            {
                chatTitle = title;
                titleSet = true;
                DateTime keep = DateTime.MinValue;
                try { if (File.Exists(path)) keep = File.GetLastWriteTimeUtc(path); } catch { }
                SaveChat();
                try { if (keep != DateTime.MinValue) File.SetLastWriteTimeUtc(path, keep); } catch { }
                Invalidate();
                Changed();
                return;
            }
            try
            {
                DateTime keep = File.GetLastWriteTimeUtc(path);
                Dictionary<string, object> root = Json.TryParse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return;
                root["title"] = title;
                root["titleSet"] = true;
                if (Program.Erased) return;
                string tmp = path + ".tmp";
                if (File.Exists(tmp)) SecureFile.Erase(tmp);
                SecureFile.WriteAll(tmp, Json.Write(root));
                SecureFile.Replace(tmp, path);
                File.SetLastWriteTimeUtc(path, keep);
            }
            catch { }
            titleCache.Remove(path);
            Changed();
        }

        public void DeleteAllChats()
        {
            List<string> facts = new List<string>();
            lock (session.Remembered) facts.AddRange(session.Remembered);
            ClearView(false, false);
            foreach (string dir in new string[] { ChatDir, ArchiveDir })
            {
                try
                {
                    if (Directory.Exists(dir))
                        foreach (string f in Directory.GetFiles(dir))
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if (ext != ".json" && ext != ".tmp") continue;
                            if (ext == ".json") foreach (string x in ChatFacts(f)) if (!facts.Contains(x)) facts.Add(x);
                            if (SecureFile.Erase(f) && ext == ".json") deletedChats.Add(Path.GetFileName(f));
                        }
                }
                catch { }
            }
            MemoryStore.Forget(facts);
            titleCache.Clear();
            settings.AiLastChat = "";
            settings.Save();
            host.ResetBrowser();
            host.ClearBrowserHistory();
            NotesChanged();
            Changed();
            composer.FocusInput();
        }

        string ReadTitle(string f, DateTime when)
        {
            KeyValuePair<DateTime, string> c;
            if (titleCache.TryGetValue(f, out c) && c.Key == when) return c.Value;
            string title = "";
            try
            {
                using (StreamReader r = new StreamReader(f, Encoding.UTF8))
                {
                    char[] buf = new char[1200];
                    int len = r.Read(buf, 0, buf.Length);
                    System.Text.RegularExpressions.Match mt = System.Text.RegularExpressions.Regex.Match(new string(buf, 0, len), "\"title\":\"((?:[^\"\\\\]|\\\\.)*)\"");
                    if (mt.Success) title = Json.TryParse("\"" + mt.Groups[1].Value + "\"") as string ?? "";
                }
            }
            catch { }
            titleCache[f] = new KeyValuePair<DateTime, string>(when, title);
            return title;
        }

        void RecoverTemps()
        {
            string[] temps;
            try { temps = Directory.Exists(ChatDir) ? Directory.GetFiles(ChatDir, "*.json.tmp") : new string[0]; } catch { return; }
            foreach (string t in temps)
            {
                string target = t.Substring(0, t.Length - 4);
                if (string.Equals(target, chatFile, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    bool tmpOk = Json.TryParse(File.ReadAllText(t, Encoding.UTF8)) is Dictionary<string, object>;
                    bool targetOk = File.Exists(target) && Json.TryParse(File.ReadAllText(target, Encoding.UTF8)) is Dictionary<string, object>;
                    bool newer = tmpOk && targetOk && File.GetLastWriteTimeUtc(t) > File.GetLastWriteTimeUtc(target);
                    if (tmpOk && (!targetOk || newer)) SecureFile.Replace(t, target);
                    else SecureFile.Erase(t);
                }
                catch { }
            }
        }

        public List<ChatInfo> ListChats()
        {
            RecoverTemps();
            return ListIn(ChatDir);
        }

        List<ChatInfo> ListIn(string dir)
        {
            List<ChatInfo> list = new List<ChatInfo>();
            string[] files = new string[0];
            try { if (Directory.Exists(dir)) files = Directory.GetFiles(dir, "*.json"); } catch { }
            foreach (string f in files)
            {
                DateTime w;
                try { w = File.GetLastWriteTimeUtc(f); } catch { continue; }
                ChatInfo c = new ChatInfo();
                c.Path = f;
                c.When = w.ToLocalTime();
                c.Title = string.Equals(f, chatFile, StringComparison.OrdinalIgnoreCase) && (chatTitle ?? "").Length > 0 ? chatTitle : ReadTitle(f, w);
                list.Add(c);
            }
            list.Sort(delegate (ChatInfo a, ChatInfo b) { return b.When.CompareTo(a.When); });
            return list;
        }

        string savedJson;

        string ChatJson()
        {
            Dictionary<string, object> root = Json.O("version", 2, "title", chatTitle, "model", settings.AiModel ?? "", "summary", session.Summary ?? "");
            if (titleSet) root["titleSet"] = true;
            List<object> items = new List<object>();
            foreach (ChatItem it in chat.Items)
            {
                if ((it.Kind == ChatKind.Permission || it.Kind == ChatKind.Question) && it.Decision < 0) continue;
                if (it.Kind == ChatKind.User && it.Queued) continue;
                Dictionary<string, object> o = Json.O("k", (int)it.Kind, "t", Clip(it.Text, 200000));
                if (it.Kind == ChatKind.Tool) { o["l"] = it.Label; o["s"] = it.Summary; o["d"] = Clip(it.Detail, it.Under != null ? 8000 : 20000); o["st"] = it.Status; o["i"] = it.Icon; if (it.Report.Length > 0) o["rp"] = Clip(it.Report, 20000); if (it.Under != null) o["up"] = true; }
                if (it.Kind == ChatKind.Thinking) o["sec"] = it.Seconds;
                if (it.Kind == ChatKind.Assistant && it.Speed > 0) { o["sp"] = Math.Round(it.Speed, 1); if (it.FirstToken > 0) o["ft"] = Math.Round(it.FirstToken, 2); if (it.SpeedGuess) o["spg"] = true; }
                if (it.Kind == ChatKind.Activity) { o["sec"] = it.Seconds + (it.Running ? Math.Max(0, (DateTime.UtcNow - it.Started).TotalSeconds) : 0); if (it.Stopped) o["stp"] = true; if (!string.IsNullOrEmpty(it.Label)) o["l"] = it.Label; }
                if (it.Kind == ChatKind.Permission) { o["l"] = it.Ask.Title; o["dec"] = it.Decision; if (!string.IsNullOrEmpty(it.Ask.Summary)) o["sm"] = it.Ask.Summary; }
                if (it.Kind == ChatKind.Question) { o["l"] = it.Question != null ? it.Question.Title : ""; o["dec"] = it.Decision; if (it.Question != null && it.Question.Wait) o["w"] = true; }
                if (it.Kind == ChatKind.User && it.ContextLabel != null) o["c"] = it.ContextLabel;
                if (it.Kind == ChatKind.User && it.Attachments != null && it.Attachments.Count > 0) o["at"] = Attachments.ToJson(it.Attachments);
                if (it.Kind == ChatKind.User && it.Turn > 0) o["tn"] = it.Turn;
                if (it.Kind == ChatKind.User && it.MidRun) o["mid"] = true;
                if (it.Kind == ChatKind.Notice) o["lv"] = it.Level;
                if (it.Kind == ChatKind.Footer) { o["u"] = true; if (it.Undone) o["ud"] = true; if (!string.IsNullOrEmpty(it.UndoneText)) o["ut"] = it.UndoneText; }
                items.Add(o);
            }
            root["items"] = items;
            if (session.Perms.Tainted) root["tainted"] = true;
            lock (session.Remembered) if (session.Remembered.Count > 0) root["mem"] = new List<object>(session.Remembered.ToArray());
            List<object> hist = new List<object>();
            Msg[] snapshot = null;
            for (int attempt = 0; attempt < 3 && snapshot == null; attempt++)
            {
                try { snapshot = session.History.ToArray(); }
                catch { Thread.Sleep(20); }
            }
            if (snapshot == null) return null;
            foreach (Msg m in snapshot)
            {
                if (m == null) continue;
                Dictionary<string, object> o = Json.O("r", m.Role, "t", Clip(m.Text, 400000));
                if (m.Turn != 0) o["tn"] = m.Turn;
                if (m.Compact != null) o["c"] = m.Compact;
                if (m.Hidden) o["h"] = true;
                if (m.ToolCallId != null) { o["id"] = m.ToolCallId; o["n"] = m.ToolName ?? ""; o["e"] = m.ToolError; }
                if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    List<object> calls = new List<object>();
                    foreach (ToolCall c in m.ToolCalls)
                    {
                        Dictionary<string, object> co = Json.O("id", c.Id, "n", c.Name, "a", c.Arguments);
                        if (!string.IsNullOrEmpty(c.Signature)) co["sg"] = c.Signature;
                        calls.Add(co);
                    }
                    o["calls"] = calls;
                }
                if (m.Images != null && m.Images.Count > 0) o["t"] = (m.Text ?? "") + "\n(" + (m.Images.Count == 1 ? "a picture was" : m.Images.Count + " pictures were") + " attached here)";
                hist.Add(o);
            }
            root["history"] = hist;
            return Json.Write(root);
        }

        public void SaveChat()
        {
            if (Program.Erased || temporary || chatFile == null || chat.Items.Count == 0) return;
            try
            {
                string json = ChatJson();
                if (json == null) return;
                Directory.CreateDirectory(ChatDir);
                bool exists = File.Exists(chatFile);
                bool deletedElsewhere = !exists && chatStamp != DateTime.MinValue;
                if (json == savedJson && (exists || deletedElsewhere))
                {
                    settings.AiLastChat = exists ? Path.GetFileName(chatFile) : "";
                    settings.Save();
                    return;
                }
                if (deletedElsewhere || ChangedElsewhere())
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    string fork = Path.Combine(ChatDir, stamp + "-copy.json");
                    for (int n = 2; File.Exists(fork) && n < 100; n++) fork = Path.Combine(ChatDir, stamp + "-copy" + n + ".json");
                    chatFile = fork;
                }
                string tmp = chatFile + ".tmp";
                if (File.Exists(tmp)) SecureFile.Erase(tmp);
                SecureFile.WriteAll(tmp, json);
                SecureFile.Replace(tmp, chatFile);
                savedJson = json;
                try { chatStamp = File.GetLastWriteTimeUtc(chatFile); } catch { chatStamp = DateTime.MinValue; }
                titleCache.Remove(chatFile);
                settings.AiLastChat = Path.GetFileName(chatFile);
                settings.Save();
                Prune();
            }
            catch { }
            if (ChatsChanged != null) ChatsChanged(this, EventArgs.Empty);
        }

        static string Clip(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n) : s; }

        DateTime chatStamp = DateTime.MinValue;

        bool ChangedElsewhere()
        {
            try { return chatStamp != DateTime.MinValue && File.Exists(chatFile) && File.GetLastWriteTimeUtc(chatFile) > chatStamp; }
            catch { return false; }
        }

        void Prune()
        {
            try
            {
                string[] files = Directory.GetFiles(ChatDir, "*.json");
                if (files.Length <= 200) return;
                List<KeyValuePair<DateTime, string>> list = new List<KeyValuePair<DateTime, string>>();
                foreach (string f in files) { try { list.Add(new KeyValuePair<DateTime, string>(File.GetLastWriteTimeUtc(f), f)); } catch { } }
                list.Sort(delegate (KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b) { return a.Key.CompareTo(b.Key); });
                for (int i = 0; i < list.Count - 200; i++)
                {
                    if (string.Equals(list[i].Value, chatFile, StringComparison.OrdinalIgnoreCase)) continue;
                    SecureFile.Erase(list[i].Value);
                }
            }
            catch { }
        }

        void LoadLastChat()
        {
            if (string.IsNullOrEmpty(settings.AiLastChat)) return;
            string p = Path.Combine(ChatDir, settings.AiLastChat);
            if (File.Exists(p)) LoadChat(p);
        }

        void LoadChat(string path)
        {
            object root;
            try { root = Json.TryParse(File.ReadAllText(path, Encoding.UTF8)); } catch { return; }
            if (!(root is Dictionary<string, object>)) return;
            ParkQueue();
            DiscardTemporary();
            Detach();
            temporary = false;
            UpdateTemporaryUi();
            todo.SetItems(new List<TodoItem>());
            composer.ClearUsage();
            composer.Prompt = null;
            afterRunHint = null;
            composer.Invalidate();
            chat.Clear();
            string summary = Json.Str(root, "summary");
            session.Summary = string.IsNullOrEmpty(summary) ? null : summary;
            List<object> items = Json.List(root, "items");
            List<ChatItem> loaded = new List<ChatItem>();
            ChatItem card = null;
            if (items != null)
                foreach (object o in items)
                {
                    ChatItem it = new ChatItem();
                    int k = Json.Int(o, "k");
                    if (k < 0 || k > (int)ChatKind.Question) continue;
                    it.Kind = (ChatKind)k;
                    it.Text = Json.Str(o, "t") ?? "";
                    if (it.Kind == ChatKind.Tool)
                    {
                        it.Label = Json.Str(o, "l") ?? ""; it.Summary = Json.Str(o, "s") ?? ""; it.Detail = Json.Str(o, "d") ?? ""; it.Report = Json.Str(o, "rp") ?? ""; it.Status = Json.Int(o, "st"); it.Icon = Json.Str(o, "i") ?? "dot"; if (it.Status == 0) it.Status = 3;
                        if (Json.Bool(o, "up")) it.Under = card;
                        else if (it.Icon == "branch") card = it;
                    }
                    if (it.Kind == ChatKind.Thinking) it.Seconds = Json.Num(o, "sec");
                    if (it.Kind == ChatKind.Assistant) { it.Speed = Json.Num(o, "sp"); it.FirstToken = Json.Num(o, "ft"); it.SpeedGuess = Json.Bool(o, "spg"); }
                    if (it.Kind == ChatKind.Activity) { it.Seconds = Json.Num(o, "sec"); it.Stopped = Json.Bool(o, "stp"); it.Label = Json.Str(o, "l") ?? ""; }
                    if (it.Kind == ChatKind.Permission) { it.Ask = new PermissionAsk(); it.Ask.Title = Json.Str(o, "l") ?? ""; it.Ask.Summary = PermissionAsk.PlainSummary(Json.Str(o, "sm"), 160); it.Decision = Json.Int(o, "dec"); if (it.Decision < 0) continue; }
                    if (it.Kind == ChatKind.Question) { it.Question = new UserQuestion(); it.Question.Title = Json.Str(o, "l") ?? ""; it.Question.Wait = Json.Bool(o, "w"); it.Decision = Json.Int(o, "dec"); if (it.Decision < 0) continue; }
                    if (it.Kind == ChatKind.User) { it.ContextLabel = Json.Str(o, "c"); it.MidRun = Json.Bool(o, "mid"); it.Turn = Json.Int(o, "tn"); it.Attachments = Attachments.FromJson(Json.Get(o, "at")); }
                    if (it.Kind == ChatKind.Notice) it.Level = Json.Int(o, "lv");
                    if (it.Kind == ChatKind.Footer) { it.Undone = Json.Bool(o, "ud"); it.UndoneText = Json.Str(o, "ut"); it.Action = null; }
                    if (it.Kind == ChatKind.Error) continue;
                    loaded.Add(it);
                }
            AddActivities(loaded);
            chat.Items.AddRange(loaded);
            List<object> hist = Json.List(root, "history");
            if (hist != null)
                foreach (object o in hist)
                {
                    Msg m = new Msg(Json.Str(o, "r") ?? "user", Json.Str(o, "t") ?? "");
                    m.Turn = Json.Int(o, "tn");
                    m.Compact = Json.Str(o, "c");
                    m.Hidden = Json.Bool(o, "h");
                    if (Json.Get(o, "id") != null) { m.ToolCallId = Json.Str(o, "id"); m.ToolName = Json.Str(o, "n"); m.ToolError = Json.Bool(o, "e"); }
                    List<object> calls = Json.List(o, "calls");
                    if (calls != null)
                    {
                        m.ToolCalls = new List<ToolCall>();
                        foreach (object c in calls)
                        {
                            ToolCall tc = new ToolCall();
                            tc.Id = Json.Str(c, "id") ?? "";
                            tc.Name = Json.Str(c, "n") ?? "";
                            tc.Arguments = Json.Str(c, "a") ?? "{}";
                            tc.Signature = Json.Str(c, "sg");
                            m.ToolCalls.Add(tc);
                        }
                    }
                    session.History.Add(m);
                }
            AgentSession.RepairHistory(session.History);
            turnCounter = MapTurns(loaded, session.History);
            curTurn = turnCounter;
            session.RestoreTaint();
            List<object> mem = Json.List(root, "mem");
            if (mem != null) lock (session.Remembered) foreach (object o in mem) { string f = o as string; if (!string.IsNullOrEmpty(f) && !session.Remembered.Contains(f)) session.Remembered.Add(f); }
            if (Json.Bool(root, "tainted") && !session.Perms.Tainted)
            {
                session.Perms.Tainted = true;
                session.Perms.TaintSource = "web content read earlier in this chat";
            }
            chatFile = path;
            try { chatStamp = File.GetLastWriteTimeUtc(path); } catch { chatStamp = DateTime.MinValue; }
            chatTitle = Json.Str(root, "title") ?? "";
            titleSet = Json.Bool(root, "titleSet");
            savedJson = null;
            try { savedJson = ChatJson(); } catch { }
            chat.ScrollToEnd();
            chat.Changed(chat.Items.Count > 0 ? chat.Items[chat.Items.Count - 1] : new ChatItem());
            PerformLayout();
            conversation++;
            NotesChanged();
        }

        void StampTurns()
        {
            if (curTurn <= 0) return;
            Msg[] all;
            try { all = session.History.ToArray(); } catch { return; }
            foreach (Msg m in all) if (m != null && m.Turn == 0) m.Turn = curTurn;
        }

        static int MapTurns(List<ChatItem> items, List<Msg> history)
        {
            List<ChatItem> users = new List<ChatItem>();
            foreach (ChatItem it in items) if (it.Kind == ChatKind.User && !it.MidRun) users.Add(it);
            int max = 0;
            foreach (ChatItem u in users) max = Math.Max(max, u.Turn);
            foreach (Msg m in history) max = Math.Max(max, m.Turn);
            bool itemsKnown = true;
            foreach (ChatItem u in users) if (u.Turn <= 0) itemsKnown = false;
            bool historyKnown = true;
            foreach (Msg m in history) if (m.Turn == 0) historyKnown = false;
            if (itemsKnown && historyKnown) return max;
            if (!itemsKnown)
            {
                int n = 0;
                foreach (ChatItem u in users) u.Turn = ++n;
                max = Math.Max(max, n);
                foreach (Msg m in history) m.Turn = 0;
            }
            int cur = -1, next = 0;
            foreach (Msg m in history)
            {
                if (m.Turn != 0) { cur = m.Turn; continue; }
                if (m.Role == "user" && !m.Hidden)
                {
                    string body = (m.Compact ?? m.Text ?? "").Trim();
                    string full = (m.Text ?? "").TrimEnd();
                    for (int k = next; k < users.Count; k++)
                    {
                        string t = (users[k].Text ?? "").Trim();
                        if (t.Length == 0) continue;
                        if (body == t || full == t || full.EndsWith("\n" + t, StringComparison.Ordinal))
                        {
                            cur = users[k].Turn;
                            next = k + 1;
                            break;
                        }
                    }
                }
                m.Turn = cur;
            }
            return max;
        }

        int TurnEnd(int start)
        {
            int k = start + 1;
            while (k < chat.Items.Count && !(chat.Items[k].Kind == ChatKind.User && !chat.Items[k].MidRun)) k++;
            return k;
        }

        bool HistoryHas(int turn)
        {
            foreach (Msg m in session.History) if (m.Turn == turn) return true;
            return false;
        }

        bool Summarized(int turn)
        {
            if (session.Summary == null || HistoryHas(turn)) return false;
            foreach (Msg m in session.History) if (m.Turn > 0) return turn < m.Turn;
            return true;
        }

        List<ChatItem> UndoableFooters(int from)
        {
            List<ChatItem> l = new List<ChatItem>();
            for (int k = from; k < chat.Items.Count; k++)
            {
                ChatItem it = chat.Items[k];
                if (it.Kind == ChatKind.Footer && it.Action != null && !it.Undone && it.Changes != null) l.Add(it);
            }
            return l;
        }

        static bool RemovesTodos(List<ChatItem> items, int from, int to)
        {
            for (int k = from; k < to && k < items.Count; k++) if (items[k].Kind == ChatKind.Tool && items[k].Icon == "checklist") return true;
            return false;
        }

        void RecomputeTodos()
        {
            List<TodoItem> found = null;
            for (int i = session.History.Count - 1; i >= 0 && found == null; i--)
            {
                Msg m = session.History[i];
                if (m.Role != "assistant" || m.ToolCalls == null) continue;
                for (int k = m.ToolCalls.Count - 1; k >= 0; k--)
                {
                    ToolCall c = m.ToolCalls[k];
                    if (c.Name != "todo_write" || c.Args == null) continue;
                    object raw;
                    if (c.Args.TryGetValue("todos", out raw)) found = TodoTool.Parse(raw);
                    if (found != null) break;
                }
            }
            session.Todos = found ?? new List<TodoItem>();
            todo.SetItems(new List<TodoItem>(session.Todos));
        }

        void RemoveItems(int from, int to)
        {
            to = Math.Min(to, chat.Items.Count);
            if (from < 0 || from >= to) return;
            ChatItem last = chat.Items[to - 1];
            chat.Items.RemoveRange(from, to - from);
            chat.Changed(last);
        }

        void AfterHistoryEdit(bool todosTouched)
        {
            session.ReadFiles.Clear();
            session.EditFailures.Clear();
            if (todosTouched) RecomputeTodos();
            composer.ClearUsage();
            composer.Invalidate();
            PerformLayout();
        }

        string UndoFooters(List<ChatItem> feet)
        {
            int reverted = 0, kept = 0;
            for (int i = feet.Count - 1; i >= 0; i--)
            {
                ChatItem f = feet[i];
                int r, k;
                try { host.UndoRun(f.RunId, f.Changes, out r, out k); } catch { r = 0; k = 0; }
                f.Undone = true;
                reverted += r;
                kept += k;
            }
            if (reverted == 0 && kept == 0) return null;
            string s = "Undid the file changes from the removed replies" + (reverted > 0 ? " (" + reverted + (reverted == 1 ? " file" : " files") + ")" : "") + ".";
            if (kept > 0) s += " " + kept + (kept == 1 ? " file was" : " files were") + " changed again since, so " + (kept == 1 ? "it was" : "they were") + " kept as is.";
            return s;
        }

        int LaterTurns(int index)
        {
            int n = 0;
            for (int k = index + 1; k < chat.Items.Count; k++) if (chat.Items[k].Kind == ChatKind.User && !chat.Items[k].MidRun) n++;
            return n;
        }

        bool EditTurn(ChatItem user, string text)
        {
            return Resend(user, text, false);
        }

        void RetryAnswer(ChatItem answer)
        {
            if (running) return;
            int i = chat.Items.IndexOf(answer);
            for (int k = i; k >= 0; k--)
            {
                ChatItem it = chat.Items[k];
                if (it.Kind == ChatKind.User && !it.MidRun) { Resend(it, it.Text, true); return; }
            }
        }

        string MidRunTexts(int start)
        {
            List<string> l = new List<string>();
            int end = TurnEnd(start);
            for (int k = start + 1; k < end; k++)
            {
                ChatItem it = chat.Items[k];
                if (it.Kind == ChatKind.User && it.MidRun && !it.Queued && it.Text.Trim().Length > 0) l.Add(it.Text.Trim());
            }
            return l.Count > 0 ? string.Join("\n\n", l.ToArray()) : null;
        }

        List<Attachment> MidRunAttachments(int start)
        {
            List<Attachment> l = new List<Attachment>();
            int end = TurnEnd(start);
            for (int k = start + 1; k < end; k++)
            {
                ChatItem it = chat.Items[k];
                if (it.Kind == ChatKind.User && it.MidRun && !it.Queued && it.Attachments != null) l.AddRange(it.Attachments);
            }
            return l.Count > 0 ? l : null;
        }

        static List<Attachment> Revive(List<Attachment> list, ref int lost, List<string> gone)
        {
            List<Attachment> r = new List<Attachment>();
            if (list == null) return r;
            foreach (Attachment a in list)
            {
                if (a.Path != null && !File.Exists(a.Path) && !Directory.Exists(a.Path)) { gone.Add(a.Name); continue; }
                if (!a.Picture || a.Image != null) { r.Add(a); continue; }
                string err;
                ImageData img = a.Path != null ? ToolKit.LoadImage(a.Path, out err) : null;
                if (img == null) { if (a.Path == null) lost++; else gone.Add(a.Name); continue; }
                Attachment c = new Attachment();
                c.Path = a.Path; c.Name = a.Name; c.Size = a.Size; c.Folder = a.Folder; c.Preview = a.Preview; c.Image = img;
                r.Add(c);
            }
            return r;
        }

        bool Resend(ChatItem user, string text, bool retry)
        {
            if (running) return false;
            int i = chat.Items.IndexOf(user);
            if (i < 0) return true;
            int turn = user.Turn;
            if (Summarized(turn))
            {
                Alert.Show(FindForm(), th, "This message is from before the conversation was summarized to save space, so it can't be " + (retry ? "run again" : "edited") + ".\n\nCopy it into a new message instead.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            int later = LaterTurns(i);
            List<ChatItem> feet = UndoableFooters(i);
            string extra = MidRunTexts(i);
            List<Attachment> extraFiles = MidRunAttachments(i);
            if (extra == null && extraFiles != null) extra = "";
            bool undo = false;
            string head = retry ? "Run this message again?" : later > 0 ? "Send the edited message?" : null;
            string what = later > 0 ? "The reply and the " + (later == 1 ? "message after it are" : later + " messages after it are") + " removed from the chat." : "The reply is replaced.";
            if (extra != null) what += retry ? " What you sent while it worked is sent again with it." : " What you sent while it worked goes back in the message box.";
            if (feet.Count > 0)
            {
                DialogResult r = Alert.Choose(FindForm(), th, (head ?? "Send the edited message?") + "\n\n" + what + " The agent changed files after this message. Undo those changes first?", Program.AppName, MessageBoxIcon.Question,
                    new string[] { "Undo changes", "Keep changes", "Cancel" }, new DialogResult[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel }, 0, 2);
                if (r == DialogResult.Cancel || r == DialogResult.None) return false;
                undo = r == DialogResult.Yes;
            }
            else if (later > 0)
            {
                if (!Alert.Confirm(FindForm(), th, head + "\n\n" + what, Program.AppName, retry ? "Run again" : "Send", false)) return false;
            }
            ParkQueue();
            if (running || chat.Items.IndexOf(user) != i) return false;
            if (!string.IsNullOrEmpty(extra) && retry) text = text.Length > 0 ? text + "\n\n" + extra : extra;
            Msg original = null;
            foreach (Msg m in session.History) if (m.Turn == turn && m.Role == "user") { original = m; break; }
            string ctxText = AgentSession.ContextOf(original);
            string label = ctxText != null ? user.ContextLabel : null;
            int lost = 0;
            List<string> gone = new List<string>();
            List<Attachment> atts = Revive(user.Attachments, ref lost, gone);
            if (retry && extraFiles != null) atts.AddRange(Revive(extraFiles, ref lost, gone));
            string undone = undo ? UndoFooters(feet) : null;
            bool todos = RemovesTodos(chat.Items, i, chat.Items.Count);
            if (chat.Editing) chat.CancelEdit();
            RemoveItems(i, chat.Items.Count);
            int cut = session.History.Count;
            for (int k = 0; k < session.History.Count; k++) if (session.History[k].Turn >= turn || session.History[k].Turn == 0) { cut = k; break; }
            if (cut < session.History.Count)
            {
                foreach (Msg m in session.History.GetRange(cut, session.History.Count - cut))
                    if (m.ToolCalls != null) foreach (ToolCall c in m.ToolCalls) if (c.Name == "todo_write") todos = true;
                session.History.RemoveRange(cut, session.History.Count - cut);
            }
            AfterHistoryEdit(todos);
            if (undone != null) Notice(undone, 0);
            StartRun(text, atts, ctxText, label, null);
            if (extra != null && !retry) Park(extra, extraFiles);
            if (lost > 0) Notice((lost == 1 ? "A pasted picture is" : lost + " pasted pictures are") + " not kept after the chat is closed, so " + (lost == 1 ? "it was" : "they were") + " not sent again. Paste " + (lost == 1 ? "it" : "them") + " again if the agent needs " + (lost == 1 ? "it." : "them."), 1);
            if (gone.Count > 0) Notice((gone.Count == 1 ? "\"" + gone[0] + "\" no longer exists, so it was" : gone.Count + " attached files no longer exist (" + string.Join(", ", gone.ToArray()) + "), so they were") + " not sent again.", 1);
            return true;
        }

        void DeleteTurn(ChatItem user)
        {
            if (running) return;
            int i = chat.Items.IndexOf(user);
            if (i < 0) return;
            int others = 0;
            foreach (ChatItem it in chat.Items) if (it != user && it.Kind == ChatKind.User && !it.MidRun) others++;
            if (others == 0)
            {
                if (chatFile != null && File.Exists(chatFile)) ConfirmAndDelete(chatFile);
                else if (Alert.Confirm(FindForm(), th, "Delete this message?\n\nIt's the only one, so the chat is cleared.", Program.AppName, "Delete", true))
                {
                    bool keep = temporary;
                    ClearView(keep, keep);
                    if (keep)
                    {
                        temporary = true;
                        UpdateTemporaryUi();
                        UpdateEmptyState();
                        PerformLayout();
                        Invalidate();
                    }
                    Changed();
                }
                return;
            }
            int end = TurnEnd(i);
            bool changed = false;
            for (int k = i; k < end; k++) if (chat.Items[k].Kind == ChatKind.Footer && !chat.Items[k].Undone) changed = true;
            string body = "They are removed from this chat for good.";
            if (changed) body += " Files the agent changed stay as they are; use Undo under the reply first if you want them back.";
            if (Summarized(user.Turn)) body += " The summary the agent keeps of older messages may still mention them.";
            if (!Alert.Confirm(FindForm(), th, "Delete this message and its reply?\n\n" + body, Program.AppName, "Delete", true)) return;
            if (running) return;
            ParkQueue();
            i = chat.Items.IndexOf(user);
            if (i < 0) return;
            end = TurnEnd(i);
            bool todos = RemovesTodos(chat.Items, i, end);
            int turn = user.Turn;
            foreach (Msg m in session.History)
                if (m.Turn == turn && m.ToolCalls != null) foreach (ToolCall c in m.ToolCalls) if (c.Name == "todo_write") todos = true;
            RemoveItems(i, end);
            session.History.RemoveAll(delegate (Msg m) { return m.Turn == turn; });
            AfterHistoryEdit(todos);
            SaveChat();
            Changed();
        }

        static void AddActivities(List<ChatItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Kind != ChatKind.User || items[i].MidRun) continue;
                int e = i + 1;
                bool steps = false, has = false;
                while (e < items.Count && !(items[e].Kind == ChatKind.User && !items[e].MidRun))
                {
                    if (items[e].Kind == ChatKind.Activity) has = true;
                    if (items[e].Kind == ChatKind.Tool || items[e].Kind == ChatKind.Thinking) steps = true;
                    e++;
                }
                if (has || !steps) continue;
                ChatItem a = new ChatItem();
                a.Kind = ChatKind.Activity;
                double secs = 0;
                for (int k = i + 1; k < e; k++) if (items[k].Kind == ChatKind.Thinking) secs += items[k].Seconds;
                a.Seconds = secs;
                items.Insert(i + 1, a);
            }
        }

        void ShowMore()
        {
            ContextMenuStrip m = Menus.Create(th);
            m.ShowCheckMargin = true;
            bool saved = chatFile != null && File.Exists(chatFile);
            ToolStripMenuItem rename = Menus.Item("Rename chat...", false, delegate { if (RenameRequested != null) RenameRequested(this, EventArgs.Empty); });
            ToolStripMenuItem delete = Menus.Item("Delete chat", false, delegate { if (DeleteRequested != null) DeleteRequested(this, EventArgs.Empty); });
            rename.Enabled = delete.Enabled = saved;
            m.Items.Add(rename);
            m.Items.Add(delete);
            m.Items.Add(Menus.Item("Copy the conversation", false, delegate { try { string t = chat.Transcript(); if (t.Length > 0) Clipboard.SetText(t); } catch { } }));
            ToolStripMenuItem compact = Menus.Item("Compact the conversation", false, delegate { CompactNow(); });
            compact.Enabled = !running && session.UserTurns() >= 2;
            m.Items.Add(compact);
            if (chat.Items.Count == 0 && !running) m.Items.Add(Menus.Item("Temporary chat", temporary, delegate { ToggleTemporary(); }));
            else if (temporary) m.Items.Add(Menus.Item("Keep this chat", false, delegate { KeepTemporary(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Menus.Item("Show thinking", settings.AiShowThinking, delegate { settings.AiShowThinking = !settings.AiShowThinking; settings.Save(); }));
            m.Items.Add(Menus.Item("Send the notes selection with messages", settings.AiSendContext, delegate { settings.AiSendContext = !settings.AiSendContext; settings.Save(); if (ContextToggled != null) ContextToggled(this, EventArgs.Empty); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Menus.Item("Open the workspace folder", false, delegate
            {
                bool d;
                string ws = host.WorkspaceFolder(out d);
                try { Directory.CreateDirectory(ws); System.Diagnostics.Process.Start(Native.WindowsExe("explorer.exe"), "\"" + ws + "\""); } catch { }
            }));
            m.Items.Add(Menus.Item("Settings...", false, delegate { host.ShowSettings(); }));
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(bMore, new Point(bMore.Width, bMore.Height + UI.S(2)), ToolStripDropDownDirection.BelowLeft);
        }

        public event EventHandler RenameRequested, DeleteRequested, ContextToggled, CloseClicked;

        bool temporary;

        public bool Temporary { get { return temporary; } }

        void UpdateTemporaryUi()
        {
            if (temporary) BrowserData.Mark(BrowserData.TemporaryMarker);
            else BrowserData.Unmark(BrowserData.TemporaryMarker);
            bTemp.Checked = temporary;
            bTemp.Visible = temporary || EmptyLayout;
            tip.SetToolTip(bTemp, temporary
                ? (chat.Items.Count == 0 ? "Temporary chat is on: this chat won't be saved and memory stays off. Click to turn it off." : "Temporary chat: not saved. Click to keep it or end it.")
                : "Temporary chat: not saved to your chats, and memory stays off");
            bTemp.Invalidate();
        }

        void ToggleTemporary()
        {
            if (chat.Items.Count == 0 && !running)
            {
                temporary = !temporary;
                if (temporary)
                {
                    chatFile = null;
                    savedJson = null;
                    chatTitle = "";
                }
                conversation++;
                NotesChanged();
                UpdateTemporaryUi();
                UpdateEmptyState();
                PerformLayout();
                Invalidate();
                composer.FocusInput();
                return;
            }
            UpdateTemporaryUi();
            if (!temporary) return;
            ContextMenuStrip m = Menus.Create(th);
            m.Items.Add(Menus.Header("Temporary chat", "not saved", th));
            ToolStripMenuItem keep = Menus.Detail("Keep this chat", "save it to your chats", false, delegate { KeepTemporary(); });
            keep.Enabled = !running;
            m.Items.Add(keep);
            m.Items.Add(Menus.Detail("End it", "start a new chat", false, delegate { NewChat(); }));
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(bTemp, new Point(bTemp.Width, bTemp.Height + UI.S(2)), ToolStripDropDownDirection.BelowLeft);
        }

        void KeepTemporary()
        {
            if (!temporary || running) return;
            temporary = false;
            if (chatFile == null && chat.Items.Count > 0)
            {
                string first = "";
                foreach (ChatItem it in chat.Items) if (it.Kind == ChatKind.User) { first = it.Text; break; }
                NewChatFile(first);
            }
            SaveChat();
            NotesChanged();
            UpdateTemporaryUi();
            Invalidate();
            Changed();
            Notice("Saved to your chats. Memory is back on for new messages.", 0);
        }

        bool Slash(string text)
        {
            if (text.Length < 2 || text[0] != '/' || text.IndexOf(' ') >= 0 || text.IndexOf('\n') >= 0) return false;
            switch (text.ToLowerInvariant())
            {
                case "/compact":
                    composer.Text = "";
                    CompactNow();
                    return true;
                case "/clear":
                case "/new":
                    composer.Text = "";
                    NewChat();
                    return true;
                case "/temp":
                    composer.Text = "";
                    StartTemporary();
                    return true;
                case "/plan":
                    composer.Text = "";
                    composer.FlipPlan();
                    return true;
            }
            return false;
        }

        void StartTemporary()
        {
            if (chat.Items.Count == 0 && !running)
            {
                if (!temporary) ToggleTemporary();
                composer.FocusInput();
                return;
            }
            WhenIdle("start a temporary chat", delegate
            {
                SaveChat();
                ClearView(true, false);
                host.ResetBrowser();
                temporary = true;
                UpdateTemporaryUi();
                UpdateEmptyState();
                PerformLayout();
                Invalidate();
                Changed();
                composer.FocusInput();
            });
        }

        void ShowContextMenu()
        {
            ContextMenuStrip m = Menus.Create(th);
            int used = composer.UsedTokens, ctx = composer.ContextTokens;
            m.Items.Add(Menus.Header("Context", Composer.Tokens(used) + " of " + Composer.Tokens(ctx) + " tokens", th));
            bool can = !running && session.UserTurns() >= 2;
            ToolStripMenuItem c = Menus.Detail("Compact now", can ? "summarize older messages" : running ? "after the agent finishes" : "nothing older yet", false, delegate { CompactNow(); });
            c.Enabled = can;
            m.Items.Add(c);
            ToolStripMenuItem n = Menus.Detail("New chat", "start with an empty context", false, delegate { NewChat(); });
            m.Items.Add(n);
            m.Items.Add(Menus.Hint("Older messages are summarized automatically when it fills up.", th));
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(composer, composer.MeterAnchor, ToolStripDropDownDirection.AboveLeft);
            composer.Invalidate();
        }

        void CompactNow()
        {
            if (running) { Notice("The agent is working. Wait until it finishes, or stop it first.", 1); return; }
            if (session.UserTurns() < 2) { Notice("There's nothing older to summarize yet.", 0); return; }
            DismissStale();
            int run = ++runCounter;
            liveRun = run;
            running = true;
            cancel = new CancelToken();
            curAssistant = null;
            curThinking = null;
            toolItems.Clear();
            curActivity = new ChatItem();
            curActivity.Kind = ChatKind.Activity;
            curActivity.Label = "Compacted the conversation";
            curActivity.Started = DateTime.UtcNow;
            curActivity.Running = true;
            curActivity.Phase = "Compacting the conversation";
            chat.Add(curActivity);
            chat.ScrollToEnd();
            composer.Running = true;
            composer.Prompt = "Compacting - a message you send now goes out when it finishes";
            compacting = true;
            UpdateQueueNotes();
            SetPhase("Compacting the conversation");
            PerformLayout();
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
            CancelToken ct = cancel;
            AgentSession sess = session;
            Bridge ui = new Bridge(this, run);
            Thread t = new Thread(delegate ()
            {
                string error = null;
                bool did = false;
                try
                {
                    ModelEntry m;
                    LlmBackend be;
                    string err = PrepareModel(ct, delegate (string ph) { ui.Phase(ph); }, out m, out be);
                    if (ct.IsCancelled) return;
                    if (err != null) { error = err; return; }
                    AgentConfig cfg = new AgentConfig();
                    cfg.Backend = be;
                    cfg.Model = m;
                    cfg.ContextTokens = m.Context > 0 ? m.Context : (m.Cloud ? 200000 : 32768);
                    cfg.MaxOutput = m.Cloud ? (m.MaxOutput > 0 ? Math.Min(m.MaxOutput, 64000) : 32000) : Math.Max(2048, Math.Min(16384, cfg.ContextTokens / 3));
                    ui.Model(m);
                    ui.Phase("Compacting the conversation");
                    did = sess.Compact(cfg, ui, ct, sess.FrozenSystem, true);
                    if (!did && !ct.IsCancelled && sess.CompactError != null) error = "The conversation couldn't be compacted: " + sess.CompactError;
                    if (did) ui.Usage(sess.FrozenSystem != null ? sess.EstimateContext(cfg, sess.FrozenSystem) : 0, cfg.ContextTokens);
                }
                catch (Exception ex) { error = "Something went wrong: " + ex.Message; }
                finally
                {
                    string e2 = error;
                    bool d2 = did;
                    ui.Post(delegate { EndCompact(run, e2, d2); });
                }
            });
            t.IsBackground = true;
            t.Name = "MinGent compact";
            t.Start();
        }

        void EndCompact(int run, string error, bool did)
        {
            if (run != liveRun) return;
            running = false;
            compacting = false;
            liveRun = -1;
            composer.Running = false;
            composer.Prompt = null;
            bool stopped = cancel != null && cancel.IsCancelled;
            if (curActivity != null)
            {
                if (did)
                {
                    curActivity.Running = false;
                    curActivity.Seconds += Math.Max(0, (DateTime.UtcNow - curActivity.Started).TotalSeconds);
                    chat.Changed(curActivity);
                }
                else
                {
                    chat.Items.Remove(curActivity);
                    chat.Changed(curActivity);
                }
            }
            curActivity = null;
            UpdateModelChip();
            CheckInbox();
            DetachQueue();
            if (stopped) Notice("Stopped.", 0);
            else if (error != null) Notice(error, 1);
            else if (!did) Notice("There's nothing older to summarize yet.", 0);
            SaveChat();
            PerformLayout();
            FlushQueue(!stopped && afterStop == null);
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
            if (afterStop != null && !running)
            {
                Action a = afterStop;
                afterStop = null;
                a();
            }
        }

        public void SettingsChanged()
        {
            if (level != "full") level = settings.AiMode == "auto" ? "auto" : "ask";
            session.Perms.Mode = level;
            session.Perms.WebGuard = settings.AiWebGuard;
            session.Perms.SetAutoAllows(settings.AutoAllowList);
            composer.Level = level;
            UpdateEmptyState();
            UpdateModelChip();
            RefreshConnection(true);
        }

        public void Shutdown()
        {
            if (running && cancel != null) cancel.Cancel();
            pump.Stop();
            try { DiscardTemporary(); } catch { }
        }

        public void Resume()
        {
            pump.Start();
        }

        public bool HoldOpen()
        {
            ChatItem pq = chat.PendingQuestion();
            bool asking = pq != null && running && pq.RunId == liveRun;
            if (chat.PendingPermission() == null && !asking) return false;
            chat.ScrollToEnd();
            if (!holdNoticeShown)
            {
                holdNoticeShown = true;
                Notice("The agent is waiting for your answer above. Answer it, or press Stop, before closing the agent.", 1);
            }
            return true;
        }

        bool holdNoticeShown;

        public bool HandleKey(Keys keyData)
        {
            if (keyData == Keys.Escape && slash.Visible && composer.Box.Focused)
            {
                slashOff = true;
                HideSlash();
                return true;
            }
            if (keyData == Keys.Escape && chat.Editing && chat.Focused && !chat.HasSelection) { chat.CancelEdit(); return true; }
            if (keyData == Keys.Escape && running && (composer.Box.Focused || (chat.Focused && !chat.HasSelection))) { Stop(); return true; }
            return false;
        }
    }

    class ModelChip : Control
    {
        string name = "", sub = "";
        int state;
        bool hover;
        Theme th = Theme.Light(Color.SteelBlue);
        public Color Surface = Color.Empty;

        public ModelChip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = UI.Font;
            AccessibleRole = AccessibleRole.ButtonDropDown;
            AccessibleName = "Model";
        }

        public void SetTheme(Theme t) { th = t; Invalidate(); }

        public void Set(string name, string sub, int state)
        {
            this.name = name ?? "";
            this.sub = sub ?? "";
            this.state = state;
            AccessibleDescription = this.name;
            Invalidate();
        }

        public int PreferredWidth
        {
            get
            {
                int w = TextRenderer.MeasureText(name, Font).Width + UI.S(51);
                if (sub.Length > 0) w += TextRenderer.MeasureText(sub, Font).Width + UI.S(6);
                return w;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surface.IsEmpty ? th.Chrome : Surface);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (hover) Glyphs.Hover(g, th, th.ButtonHover, r, UI.S(8));
            Color dot = state == 1 ? (th.Dark ? Color.FromArgb(0x6C, 0xC2, 0x6C) : Color.FromArgb(0x2E, 0x8B, 0x3E)) : state == 2 ? th.Error : th.ChromeDim;
            int d = UI.S(7);
            Glyphs.Dot(g, new Rectangle(UI.S(8), (Height - d) / 2, d, d), dot, d);
            int x = UI.S(21);
            int avail = Width - x - UI.S(28);
            int nw = Math.Min(avail, TextRenderer.MeasureText(name, Font).Width);
            TextRenderer.DrawText(g, name, Font, new Rectangle(x, 0, Math.Max(0, nw + UI.S(2)), Height), hover ? th.Fore : th.ChromeFore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (sub.Length > 0 && nw + UI.S(40) < avail)
                TextRenderer.DrawText(g, sub, Font, new Rectangle(x + nw + UI.S(6), 0, avail - nw - UI.S(6), Height), th.ChromeDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Glyphs.Draw("down", g, new Rectangle(Width - UI.S(20), 0, UI.S(16), Height), th.ChromeDim);
        }
    }

    class SlashCommand
    {
        public string Name, Detail;
        public SlashCommand(string name, string detail) { Name = name; Detail = detail; }
    }

    static class SlashCommands
    {
        public static readonly SlashCommand[] All = {
            new SlashCommand("/compact", "Summarize older messages to free up the context"),
            new SlashCommand("/new", "Start a new chat"),
            new SlashCommand("/clear", "Start a new chat (same as /new)"),
            new SlashCommand("/temp", "Start a temporary chat: not saved, memory off"),
            new SlashCommand("/plan", "Turn plan first on or off (Shift+Tab)")
        };
    }

    class SlashItem
    {
        public string Name = "", Detail = "", Note;
    }

    class SlashList : Control
    {
        Theme th = Theme.Light(Color.SteelBlue);
        public readonly List<SlashItem> Items = new List<SlashItem>();
        public int Selected;
        public event Action<int> Picked;

        public SlashList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Visible = false;
            Font = UI.Font;
            AccessibleName = "Commands";
            AccessibleRole = AccessibleRole.List;
        }

        public void SetTheme(Theme t) { th = t; Invalidate(); }

        int RowH { get { return UI.S(30); } }
        int Pad { get { return UI.S(5); } }

        public int PreferredHeight { get { return Items.Count * RowH + Pad * 2; } }

        public int PreferredWidth
        {
            get
            {
                int nameW = 0, rest = 0;
                foreach (SlashItem it in Items)
                {
                    nameW = Math.Max(nameW, TextRenderer.MeasureText(it.Name, Font).Width);
                    rest = Math.Max(rest, TextRenderer.MeasureText(Line(it), Font).Width);
                }
                return Pad * 2 + UI.S(10) + nameW + UI.S(18) + rest + UI.S(16);
            }
        }

        static string Line(SlashItem it) { return it.Note ?? it.Detail; }

        public void Step(int d)
        {
            if (Items.Count == 0) return;
            Selected = (Selected + d + Items.Count) % Items.Count;
            Invalidate();
        }

        int RowAt(Point p)
        {
            int i = (p.Y - Pad) / RowH;
            return p.Y >= Pad && i >= 0 && i < Items.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = RowAt(e.Location);
            if (i >= 0 && i != Selected) { Selected = i; Invalidate(); }
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int i = RowAt(e.Location);
            if (i >= 0 && Picked != null) Picked(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Back);
            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            Glyphs.FillRound(g, th.MenuBack, box, UI.S(12));
            Glyphs.DrawRound(g, th.Border, box, UI.S(12));
            int nameW = 0;
            foreach (SlashItem it in Items) nameW = Math.Max(nameW, TextRenderer.MeasureText(it.Name, Font).Width);
            const TextFormatFlags fl = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            for (int i = 0; i < Items.Count; i++)
            {
                SlashItem it = Items[i];
                Rectangle row = new Rectangle(Pad, Pad + i * RowH, Width - Pad * 2 - 1, RowH);
                bool sel = i == Selected;
                bool off = it.Note != null;
                bool hc = sel && !th.MenuHoverFore.IsEmpty;
                if (sel && off && hc) Glyphs.DrawRound(g, th.MenuHover, new Rectangle(row.X, row.Y, row.Width - 1, row.Height - 1), UI.S(6));
                else if (sel) Glyphs.FillRound(g, th.MenuHover, row, UI.S(6));
                Color fore = off ? th.MenuDisabled : hc ? th.MenuHoverFore : th.MenuFore;
                Color dim = off ? th.MenuDisabled : hc ? th.MenuHoverFore : th.ChromeDim;
                int x = row.X + UI.S(10);
                TextRenderer.DrawText(g, it.Name, Font, new Rectangle(x, row.Y, nameW + UI.S(4), row.Height), fore, fl);
                int dx = x + nameW + UI.S(18);
                TextRenderer.DrawText(g, Line(it), Font, new Rectangle(dx, row.Y, Math.Max(0, row.Right - dx - UI.S(8)), row.Height), dim, fl);
            }
        }
    }

    class TodoStrip : Control
    {
        List<TodoItem> items = new List<TodoItem>();
        bool expanded = true, hover, userCollapsed;
        int scroll, room = int.MaxValue;
        Theme th = Theme.Light(Color.SteelBlue);
        public event EventHandler Toggled;

        public TodoStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = UI.Font;
        }

        public void SetTheme(Theme t) { th = t; Invalidate(); }

        public bool HasItems { get { return items != null && items.Count > 0; } }

        public static TodoItem Current(List<TodoItem> list)
        {
            if (list == null) return null;
            foreach (TodoItem t in list) if (t.Status == "in_progress") return t;
            return null;
        }

        static bool Same(List<TodoItem> a, List<TodoItem> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].Content != b[i].Content) return false;
            return true;
        }

        public void SetItems(List<TodoItem> list)
        {
            List<TodoItem> old = items;
            items = list ?? new List<TodoItem>();
            bool allDone = items.Count > 0;
            foreach (TodoItem t in items) if (t.Status != "completed") allDone = false;
            if (!Same(old, items)) userCollapsed = false;
            Visible = items.Count > 0;
            expanded = !allDone && !userCollapsed;
            ShowFocus();
            Invalidate();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }

        public int Room
        {
            set
            {
                int r = Math.Max(0, value);
                if (r == room) return;
                room = r;
                ClampScroll();
                ShowFocus();
            }
        }

        int RowH { get { return UI.S(22); } }
        int HeadH { get { return UI.S(28); } }

        int Rows { get { return Math.Min(items.Count, Math.Max(3, (room - HeadH - UI.S(4)) / RowH)); } }

        bool Scrollable { get { return expanded && items.Count > Rows; } }

        public int PreferredHeight { get { return HeadH + (expanded ? Rows * RowH + UI.S(4) : 0); } }

        void ClampScroll()
        {
            scroll = Math.Max(0, Math.Min(scroll, items.Count - Rows));
        }

        void ShowFocus()
        {
            int focus = -1;
            for (int i = 0; i < items.Count && focus < 0; i++) if (items[i].Status == "in_progress") focus = i;
            for (int i = 0; i < items.Count && focus < 0; i++) if (items[i].Status != "completed") focus = i;
            if (focus < 0) focus = items.Count - 1;
            int rows = Rows;
            if (focus >= 0)
            {
                int ahead = rows >= 4 && focus < items.Count - 1 ? 1 : 0;
                if (focus < scroll) scroll = focus;
                else if (focus + ahead >= scroll + rows) scroll = focus + ahead - rows + 1;
            }
            ClampScroll();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        int wheel;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Scrollable) { wheel = 0; return; }
            wheel += e.Delta;
            int notches = wheel / 120;
            if (notches == 0) return;
            wheel -= notches * 120;
            int lines = Math.Max(1, Math.Min(Rows - 1, SystemInformation.MouseWheelScrollLines));
            int before = scroll;
            scroll -= notches * lines;
            ClampScroll();
            if (scroll != before) Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            expanded = !expanded;
            userCollapsed = !expanded;
            if (expanded) ShowFocus();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(th.Back);
            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            Glyphs.FillRound(g, hover && !th.HighContrast ? th.TabHover : th.Back, box, UI.S(12));
            Glyphs.DrawRound(g, hover && th.HighContrast ? SystemColors.Highlight : th.Border, box, UI.S(12));
            int done = 0;
            TodoItem active = Current(items);
            foreach (TodoItem t in items) if (t.Status == "completed") done++;
            string head = "Steps  " + done + " of " + items.Count + " done" + (!expanded && active != null ? "  \u00B7  " + active.Content : "");
            Glyphs.Draw("checklist", g, new Rectangle(UI.S(8), 0, UI.S(16), HeadH), th.ChromeDim);
            TextRenderer.DrawText(g, head, Font, new Rectangle(UI.S(30), 0, Width - UI.S(56), HeadH), th.ChromeFore, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Glyphs.Draw(expanded ? "down" : "right", g, new Rectangle(Width - UI.S(24), 0, UI.S(16), HeadH), th.ChromeDim);
            if (!expanded) return;
            int rows = Rows;
            bool bar = items.Count > rows;
            int textRight = Width - (bar ? UI.S(20) : UI.S(10));
            int y = HeadH;
            for (int i = scroll; i < items.Count && i < scroll + rows; i++)
            {
                TodoItem t = items[i];
                Rectangle ic = new Rectangle(UI.S(10), y + (RowH - UI.S(14)) / 2, UI.S(14), UI.S(14));
                Color c = t.Status == "completed" ? th.ChromeDim : th.Fore;
                Glyphs.StepMark(g, ic, t.Status == "completed" ? 2 : t.Status == "in_progress" ? 1 : 0, th);
                Font f = Font;
                const TextFormatFlags one = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                int tw0 = Math.Max(UI.S(20), textRight - UI.S(32));
                TextRenderer.DrawText(g, t.Content, f, new Rectangle(UI.S(32), y, tw0, RowH), c, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | one);
                if (t.Status == "completed")
                {
                    int tw = Math.Min(tw0, TextRenderer.MeasureText(g, t.Content, f, Size.Empty, one).Width);
                    using (Pen p = new Pen(th.ChromeDim)) g.DrawLine(p, UI.S(32), y + RowH / 2, UI.S(32) + tw, y + RowH / 2);
                }
                y += RowH;
            }
            if (bar)
            {
                int trackTop = HeadH + UI.S(2), trackH = rows * RowH - UI.S(4);
                int thumbH = Math.Max(UI.S(16), trackH * rows / items.Count);
                int range = Math.Max(1, items.Count - rows);
                int thumbY = trackTop + (trackH - thumbH) * scroll / range;
                Glyphs.FillRound(g, th.Thumb, new Rectangle(Width - UI.S(10), thumbY, UI.S(4), thumbH), UI.S(2));
            }
        }
    }

    class Composer : Control
    {
        bool CanPasteAttachment()
        {
            return Attachments.ClipboardFiles() != null || Attachments.ClipboardHasPicture();
        }

        void PasteAttachment()
        {
            string[] files = Attachments.ClipboardFiles();
            if (files != null) { if (FilesDropped != null) FilesDropped(files); return; }
            if (PictureCount >= Attachments.MaxPictures) { if (Refused != null) Refused("A message can carry up to " + Attachments.MaxPictures + " pictures."); return; }
            using (Image img = Attachments.ClipboardPicture())
            {
                if (img == null) return;
                ImageData d;
                try { d = ImageTools.Encode(img, 1568); } catch (Exception) { d = null; }
                if (d != null) Add(Attachments.FromImage(d, "Pasted image"));
                else if (Refused != null) Refused("Could not read the picture on the clipboard.");
            }
        }
        public readonly CueTextBox Box = new CueTextBox();
        public readonly ModelChip Model = new ModelChip();
        readonly ToggleSwitch auto = new ToggleSwitch("Auto");
        readonly ToggleChip plan = new ToggleChip("Plan", "map");
        readonly ThemedTip tip = new ThemedTip();
        Theme th = Theme.Light(Color.SteelBlue);
        bool running, hoverSend, hoverEffort, hoverAttach, hoverCtx, hoverMeter;
        int usedTok, ctxTok;
        int hoverTile = -1, hiddenTiles;
        bool hoverRemove;
        string contextLabel;
        bool contextOff;
        readonly List<Attachment> attachments = new List<Attachment>();
        readonly List<Rectangle> tiles = new List<Rectangle>();
        readonly Dictionary<Attachment, Bitmap> thumbs = new Dictionary<Attachment, Bitmap>();
        public string EffortText = "";

        public void SetUsage(int used, int ctx)
        {
            used = Math.Max(0, used);
            ctx = Math.Max(0, ctx);
            if (used == usedTok && ctx == ctxTok) return;
            bool had = UsageFraction >= 0;
            usedTok = used;
            ctxTok = ctx;
            if ((UsageFraction >= 0) != had) PerformLayout();
            Invalidate();
        }

        public void ClearUsage() { SetUsage(0, 0); }

        public int UsedTokens { get { return usedTok; } }
        public int ContextTokens { get { return ctxTok; } }

        public double UsageFraction { get { return ctxTok > 0 && usedTok > 0 ? Math.Min(1.0, usedTok / (double)ctxTok) : -1; } }

        string PercentText
        {
            get
            {
                double f = UsageFraction;
                if (f < 0) return "";
                int pc = (int)Math.Round(f * 100);
                if (pc == 0) pc = 1;
                return pc.ToString(CultureInfo.InvariantCulture) + "%";
            }
        }

        public static string Tokens(int n) { return n.ToString("N0", CultureInfo.InvariantCulture); }

        public string MeterTip
        {
            get
            {
                if (UsageFraction < 0) return null;
                return "Context: " + Tokens(usedTok) + " of " + Tokens(ctxTok) + " tokens (" + PercentText + ")\nOlder messages are summarized automatically when it fills up.\nClick to compact now, or type /compact.";
            }
        }

        bool MeterLayout(out Rectangle r, out bool withText)
        {
            r = Rectangle.Empty;
            withText = false;
            if (UsageFraction < 0 || !Model.Visible) return false;
            Rectangle er = EffortRect;
            int limit = (er.IsEmpty ? (plan.Visible ? plan.Right : auto.Visible ? auto.Right : AttachRect.Right) : er.Right) + UI.S(6);
            int ring = UI.S(16);
            int tw = TextRenderer.MeasureText(PercentText, Font).Width;
            int full = UI.S(6) + ring + UI.S(5) + tw + UI.S(6);
            int bare = UI.S(6) + ring + UI.S(6);
            int right = Model.Left - UI.S(2);
            if (right - full >= limit) { r = new Rectangle(right - full, RowY, full, UI.S(30)); withText = true; return true; }
            if (right - bare >= limit) { r = new Rectangle(right - bare, RowY, bare, UI.S(30)); return true; }
            return false;
        }

        public Point MeterAnchor
        {
            get
            {
                Rectangle r;
                bool t;
                if (!MeterLayout(out r, out t)) return new Point(Model.Left, RowY - UI.S(2));
                return new Point(r.Right, r.Y - UI.S(2));
            }
        }

        public event EventHandler MeterClicked;
        public bool HasAttachments { get { return attachments.Count > 0; } }
        public bool HasPictures { get { return PictureCount > 0; } }
        public int PictureCount { get { return Attachments.Pictures(attachments); } }
        public int FileCount { get { return attachments.Count - PictureCount; } }
        public bool ShowEffort;
        const string IdleCue = "Ask anything, or describe a task";
        const string PlanCue = "Describe the task - the agent researches and shows you a plan first";
        const string BusyCue = "Send a message - the agent reads it at its next step";
        string prompt;

        public string Prompt
        {
            get { return prompt; }
            set { prompt = value; UpdateCue(); }
        }

        void UpdateCue()
        {
            string cue = prompt ?? (running ? BusyCue : plan.On ? PlanCue : IdleCue);
            if (Box.Cue != cue) { Box.Cue = cue; Box.Invalidate(); }
        }

        public event EventHandler SendRequested, StopRequested, EffortClicked, HeightChanged, ContextCleared, AttachClicked, ModelClicked;
        public event Action<bool> AutoChanged, PlanChanged;
        public event Action<string> LevelChosen;
        public event EventHandler CustomizeAuto;
        public event Action<string[]> FilesDropped;
        public event Action<string> Refused;
        public event EventHandler EditLastRequested;
        public Func<Keys, bool> KeyFilter;

        public void FlipPlan() { plan.Flip(); }

        public Composer()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = UI.Font;
            Box.Multiline = true;
            Box.BorderStyle = BorderStyle.None;
            Box.AcceptsReturn = true;
            Box.WordWrap = true;
            Box.ScrollBars = ScrollBars.None;
            Box.Font = new Font(UI.Font.FontFamily, 10.5f);
            Box.Cue = IdleCue;
            Controls.Add(Box);
            Controls.Add(auto);
            Controls.Add(plan);
            Controls.Add(Model);
            auto.Chevron = true;
            auto.DangerText = "Full auto";
            auto.MenuRequested += delegate { ShowLevelMenu(); };
            auto.MouseUp += delegate { if (menuClosedByAuto) BeginInvoke(new Action(delegate { menuClosedByAuto = false; })); };
            tip.SetToolTip(auto, "Auto: the agent acts without asking, except for what you chose in Settings > Safety.\nOff: it asks before commands, web access and changes outside the workspace.\nThe arrow opens Full auto, which asks nothing.");
            tip.SetToolTip(plan, "Plan first: the agent only reads and researches, then shows you a plan.\nNothing changes until you approve it. Shift+Tab turns it on or off.");
            tip.SetToolTip(Model, "Choose the model");
            auto.Changed += delegate { if (AutoChanged != null) AutoChanged(auto.On); };
            plan.Changed += delegate { UpdateCue(); PerformLayout(); if (PlanChanged != null) PlanChanged(plan.On); };
            Model.Click += delegate { if (ModelClicked != null) ModelClicked(this, EventArgs.Empty); };
            Box.KeyDown += OnBoxKey;
            Box.CanPasteOther = CanPasteAttachment;
            Box.PasteOther = PasteAttachment;
            Box.TextChanged += delegate { Relayout(); };
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tip.Dispose(); DropThumbs(); }
            base.Dispose(disposing);
        }

        public bool Auto { get { return auto.On; } set { auto.On = value; } }

        public string Level
        {
            get { return auto.Danger ? "full" : auto.On ? "auto" : "ask"; }
            set
            {
                bool wasDanger = auto.Danger;
                auto.On = value == "auto" || value == "full";
                auto.Danger = value == "full";
                if (wasDanger != auto.Danger) { PerformLayout(); Invalidate(); }
            }
        }

        void ShowLevelMenu()
        {
            if (menuClosedByAuto) { menuClosedByAuto = false; return; }
            string cur = Level;
            ContextMenuStrip m = Menus.Create(th);
            m.Items.Add(Menus.Detail("Ask", "asks before commands, web and outside changes", cur == "ask", delegate { if (LevelChosen != null) LevelChosen("ask"); }));
            m.Items.Add(Menus.Detail("Auto", "asks only about what you chose", cur == "auto", delegate { if (LevelChosen != null) LevelChosen("auto"); }));
            ToolStripMenuItem full = Menus.Detail("Full auto", "asks nothing - dangerous", cur == "full", delegate { if (LevelChosen != null) LevelChosen("full"); });
            full.ForeColor = th.HighContrast ? th.Fore : th.Error;
            m.Items.Add(full);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Menus.Item("Choose what Auto asks about...", false, delegate { if (CustomizeAuto != null) CustomizeAuto(this, EventArgs.Empty); }));
            m.Closed += delegate (object o, ToolStripDropDownClosedEventArgs ce)
            {
                bool overAuto = false;
                try { overAuto = auto.ClientRectangle.Contains(auto.PointToClient(Control.MousePosition)); } catch { }
                menuClosedByAuto = ce.CloseReason == ToolStripDropDownCloseReason.AppClicked && overAuto && (Control.MouseButtons & (MouseButtons.Left | MouseButtons.Right)) != MouseButtons.None;
                BeginInvoke(new Action(delegate { m.Dispose(); }));
            };
            m.Show(auto, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
        }

        bool menuClosedByAuto;

        public bool Plan
        {
            get { return plan.On; }
            set { plan.On = value; UpdateCue(); PerformLayout(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Shift | Keys.Tab) && Box.Focused)
            {
                plan.Flip();
                return true;
            }
            if (keyData == Keys.Tab && Box.Focused && KeyFilter != null && KeyFilter(Keys.Tab)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public bool Running
        {
            get { return running; }
            set
            {
                running = value;
                if (!value) prompt = null;
                UpdateCue();
                Invalidate();
            }
        }

        public override string Text { get { return Box.Text; } set { Box.Text = value ?? ""; } }
        public bool ContextEnabled { get { return !contextOff; } }

        public string ContextLabel
        {
            get { return contextLabel; }
            set
            {
                if (value == contextLabel) return;
                bool hadLine = ShowContextLine;
                contextLabel = value;
                contextOff = false;
                if (hadLine != ShowContextLine) Relayout();
                else Invalidate();
            }
        }

        bool ShowContextLine { get { return !string.IsNullOrEmpty(contextLabel) && !contextOff; } }

        public void SetTheme(Theme t)
        {
            th = t;
            tip.Th = t;
            Box.BackColor = t.InputBack;
            Box.ForeColor = t.Fore;
            Box.CueColor = t.ChromeDim;
            Box.MenuTheme = t;
            auto.Surface = t.InputBack;
            auto.SetTheme(t);
            plan.Surface = t.InputBack;
            plan.SetTheme(t);
            Model.Surface = t.InputBack;
            Model.SetTheme(t);
            Invalidate(true);
        }

        public void FocusInput() { Box.Focus(); }
        public void MoveCaretToEnd() { Box.SelectionStart = Box.TextLength; Box.ScrollToCaret(); }

        public bool Add(Attachment a)
        {
            if (a == null) return false;
            foreach (Attachment o in attachments) if (a.Path != null && string.Equals(o.Path, a.Path, StringComparison.OrdinalIgnoreCase)) return false;
            attachments.Add(a);
            Relayout();
            return true;
        }

        public List<Attachment> TakeAttachments()
        {
            List<Attachment> l = new List<Attachment>(attachments);
            attachments.Clear();
            DropThumbs();
            Relayout();
            return l;
        }

        public void ClearAttachments()
        {
            if (attachments.Count == 0) return;
            attachments.Clear();
            DropThumbs();
            Relayout();
        }

        public void RemoveAttachment(int i)
        {
            if (i < 0 || i >= attachments.Count) return;
            Attachment a = attachments[i];
            attachments.RemoveAt(i);
            Bitmap b;
            if (thumbs.TryGetValue(a, out b)) { thumbs.Remove(a); if (b != null) b.Dispose(); }
            hoverTile = -1;
            hoverRemove = false;
            tip.Hide(this);
            Relayout();
        }

        void DropThumbs()
        {
            foreach (Bitmap b in thumbs.Values) if (b != null) b.Dispose();
            thumbs.Clear();
            hoverTile = -1;
            hoverRemove = false;
        }

        Bitmap Thumb(Attachment a, Size size)
        {
            Bitmap b;
            if (thumbs.TryGetValue(a, out b) && (b == null || b.Size == size)) return b;
            if (b != null) b.Dispose();
            b = Attachments.Cover(a.Shown, size.Width, size.Height);
            thumbs[a] = b;
            return b;
        }

        int TileH { get { return UI.S(48); } }

        int LayoutTiles(int width, List<Rectangle> into)
        {
            if (into != null) { into.Clear(); hiddenTiles = 0; }
            if (attachments.Count == 0) return 0;
            int left = UI.S(12), right = width - UI.S(12), gap = UI.S(6), top = UI.S(10) + (ShowContextLine ? UI.S(30) : 0);
            int x = left, row = 0, shown = 0;
            for (int i = 0; i < attachments.Count; i++)
            {
                Attachment a = attachments[i];
                int w = a.Picture ? TileH : Attachments.FileWidth(null, a, Font, Fonts.Small, UI.S(22), Math.Max(UI.S(60), right - left));
                if (x > left && x + w > right) { row++; x = left; }
                if (row >= 2) break;
                if (into != null) into.Add(new Rectangle(x, top + row * (TileH + gap), w, TileH));
                x += w + gap;
                shown++;
            }
            if (into != null) hiddenTiles = attachments.Count - shown;
            int rows = Math.Min(2, row + 1);
            return rows * TileH + (rows - 1) * gap + UI.S(8);
        }

        int LineH { get { return (int)Math.Ceiling(Box.Font.GetHeight()) + UI.S(1); } }

        int TextLines(int width)
        {
            if (width <= 0) return 1;
            string t = Box.Text;
            if (t.Length == 0) return 1;
            if (Math.Abs(Box.Width - width) <= 1)
            {
                int n = Native.LineCount(Box);
                if (n > 0) return n;
            }
            Size s = TextRenderer.MeasureText(t.EndsWith("\n") ? t + " " : t, Box.Font, new Size(width - UI.S(4), 10000), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            return Math.Max(1, (int)Math.Ceiling(s.Height / (double)Box.Font.GetHeight()));
        }

        int BottomH { get { return UI.S(40); } }

        public int PreferredHeight(int width)
        {
            int lines = Math.Min(12, TextLines(width - UI.S(32)));
            int h = UI.S(14) + Math.Max(2, lines) * LineH + UI.S(4) + BottomH;
            if (ShowContextLine) h += UI.S(28);
            return h + LayoutTiles(width, null);
        }

        int lastH;

        void Relayout()
        {
            int h = PreferredHeight(Width);
            if (h != lastH) { lastH = h; if (HeightChanged != null) HeightChanged(this, EventArgs.Empty); }
            PerformLayout();
            Invalidate();
        }

        int RowY { get { return Height - BottomH + (BottomH - UI.S(30)) / 2; } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int top = UI.S(14) + (ShowContextLine ? UI.S(28) : 0) + LayoutTiles(Width, tiles);
            int bottom = Height - BottomH;
            Box.SetBounds(UI.S(16), top, Width - UI.S(32), Math.Max(LineH, bottom - top));
            Rectangle ar = AttachRect;
            Rectangle sr = SendRect;
            int aw = auto.PreferredWidth, pw = plan.PreferredWidth, pc = UI.S(ToggleChip.CompactWidth);
            int ew = ShowEffort && EffortText.Length > 0 ? TextRenderer.MeasureText(EffortText, Font).Width + UI.S(30) : 0;
            int room = sr.Left - UI.S(6) - (ar.Right + UI.S(6));
            int meter = UsageFraction >= 0 ? UI.S(28) : 0;
            bool autoFits = room >= aw + UI.S(8) + UI.S(70);
            bool planFull = autoFits && room >= aw + UI.S(2) + pw + UI.S(8) + Math.Max(UI.S(70), Math.Min(Model.PreferredWidth, UI.S(130)));
            bool planFits = planFull || (autoFits && room >= aw + UI.S(2) + pc + UI.S(8) + (plan.On ? UI.S(44) : UI.S(70)));
            plan.Compact = !planFull;
            if (!planFull) pw = pc;
            effortFits = planFits && ew > 0 && room >= aw + UI.S(2) + pw + UI.S(6) + ew + UI.S(8) + UI.S(90) + meter;
            auto.Visible = autoFits;
            auto.SetBounds(ar.Right + UI.S(6), RowY, aw, UI.S(30));
            plan.Visible = planFits;
            plan.SetBounds(auto.Right + UI.S(2), RowY, pw, UI.S(30));
            int left = !autoFits ? ar.Right + UI.S(8) : effortFits ? EffortRect.Right + UI.S(8) : planFits ? plan.Right + UI.S(8) : auto.Right + UI.S(8);
            int mw = Math.Min(Model.PreferredWidth, Math.Max(UI.S(90), (Width - UI.S(20)) / 2 - UI.S(30)));
            int space = sr.Left - UI.S(6) - left;
            if (meter > 0 && space - meter >= Math.Min(mw, UI.S(90))) space -= meter;
            mw = Math.Min(mw, space);
            bool showModel = mw >= UI.S(44);
            Model.Visible = showModel;
            if (showModel) Model.SetBounds(sr.Left - UI.S(6) - mw, RowY, mw, UI.S(30));
            string modelTip = showModel && mw < Model.PreferredWidth && !string.IsNullOrEmpty(Model.AccessibleDescription) ? Model.AccessibleDescription : "Choose the model";
            if (tip.GetToolTip(Model) != modelTip) tip.SetToolTip(Model, modelTip);
        }

        bool effortFits = true;

        Rectangle SendRect { get { int s = UI.S(32); return new Rectangle(Width - s - UI.S(10), Height - BottomH + (BottomH - s) / 2 - UI.S(1), s, s); } }

        public Point EffortAnchor { get { Rectangle r = EffortRect; return new Point(r.X, r.Y - UI.S(2)); } }

        Rectangle AttachRect { get { return new Rectangle(UI.S(10), RowY, UI.S(30), UI.S(30)); } }

        Rectangle EffortRect
        {
            get
            {
                if (!ShowEffort || EffortText.Length == 0 || !effortFits) return Rectangle.Empty;
                int x = (plan.Visible ? plan.Right : auto.Right) + UI.S(6);
                int w = TextRenderer.MeasureText(EffortText, Font).Width + UI.S(30);
                return new Rectangle(x, RowY, w, UI.S(30));
            }
        }

        Rectangle CtxRect { get { return new Rectangle(UI.S(12), UI.S(10), Width - UI.S(24), UI.S(24)); } }
        Rectangle CtxCloseRect { get { if (string.IsNullOrEmpty(contextLabel) || contextOff) return Rectangle.Empty; Rectangle r = CtxRect; int w = Math.Min(r.Width, CtxTextW + UI.S(52)); return new Rectangle(r.X + w - UI.S(23), r.Y + UI.S(2), UI.S(20), UI.S(20)); } }
        int CtxTextW { get { return string.IsNullOrEmpty(contextLabel) ? 0 : TextRenderer.MeasureText(contextLabel, Font).Width; } }

        Color backdrop = Color.Empty;

        public Color Backdrop
        {
            get { return backdrop; }
            set { if (backdrop == value) return; backdrop = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(backdrop != Color.Empty ? backdrop : Parent != null ? Parent.BackColor : th.Back);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Glyphs.FillRound(g, th.InputBack, r, UI.S(18));
            Glyphs.DrawRound(g, Box.Focused ? Theme.Blend(th.InputBorder, th.Fore, 0.35) : th.InputBorder, r, UI.S(18));
            if (ShowContextLine)
            {
                Rectangle cr = CtxRect;
                int w = Math.Min(Width - UI.S(24), CtxTextW + UI.S(52));
                Rectangle chip = new Rectangle(UI.S(12), cr.Y, w, cr.Height);
                Glyphs.FillRound(g, hoverCtx ? th.ButtonHover : th.Chrome, chip, UI.S(8));
                Glyphs.Draw("file", g, new Rectangle(chip.X + UI.S(6), chip.Y, UI.S(14), chip.Height), th.ChromeDim);
                TextRenderer.DrawText(g, contextLabel, Font, new Rectangle(chip.X + UI.S(24), chip.Y, w - UI.S(48), chip.Height), th.ChromeFore, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                Glyphs.X(g, CtxCloseRect, th.ChromeDim, UI.S(3));
            }
            for (int i = 0; i < tiles.Count && i < attachments.Count; i++)
            {
                Attachment a = attachments[i];
                Rectangle tr = tiles[i];
                if (a.Picture) Attachments.DrawPicture(g, th, tr, Thumb(a, tr.Size));
                else Attachments.DrawFile(g, th, tr, a, Font, Fonts.Small, th.Chrome, UI.S(22));
                if (hoverTile == i) Attachments.DrawRemove(g, th, Attachments.RemoveRect(tr), hoverRemove);
            }
            if (hiddenTiles > 0 && tiles.Count > 0)
            {
                Rectangle last = tiles[tiles.Count - 1];
                string more = "+" + hiddenTiles.ToString(CultureInfo.InvariantCulture);
                int mw = TextRenderer.MeasureText(more, Font).Width + UI.S(8);
                if (last.Right + UI.S(6) + mw <= Width - UI.S(12))
                    TextRenderer.DrawText(g, more, Font, new Rectangle(last.Right + UI.S(6), last.Y, mw, last.Height), th.ChromeDim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
            Rectangle ar = AttachRect;
            if (hoverAttach) Glyphs.Hover(g, th, th.ButtonHover, ar, ar.Height / 2);
            Glyphs.Draw("attach", g, ar, th.ChromeFore);
            Rectangle er = EffortRect;
            if (!er.IsEmpty)
            {
                if (hoverEffort) Glyphs.Hover(g, th, th.ButtonHover, er, UI.S(8));
                TextRenderer.DrawText(g, EffortText, Font, new Rectangle(er.X + UI.S(10), er.Y, er.Width - UI.S(26), er.Height), hoverEffort ? th.Fore : th.ChromeDim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                Glyphs.Draw("down", g, new Rectangle(er.Right - UI.S(20), er.Y, UI.S(14), er.Height), th.ChromeDim);
            }
            Rectangle sr = SendRect;
            Rectangle mr;
            bool mtext;
            if (MeterLayout(out mr, out mtext))
            {
                double frac = UsageFraction;
                if (hoverMeter) Glyphs.Hover(g, th, th.ButtonHover, mr, UI.S(8));
                Color warn = th.Dark ? Color.FromArgb(0xF5, 0x9E, 0x0B) : Color.FromArgb(0xB4, 0x53, 0x09);
                Color col = th.HighContrast ? th.Fore : frac >= 0.9 ? th.Error : frac >= 0.7 ? warn : th.ChromeDim;
                Color track = th.HighContrast ? th.ChromeDim : Theme.Blend(th.ChromeDim, th.InputBack, th.Dark ? 0.62 : 0.7);
                int ring = UI.S(16);
                float pw = Math.Max(1.6f, 2.1f * UI.Scale);
                RectangleF arc = new RectangleF(mr.X + UI.S(6) + pw / 2, mr.Y + (mr.Height - ring) / 2 + pw / 2, ring - pw, ring - pw);
                SmoothingMode sm = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen tp = new Pen(track, pw)) g.DrawEllipse(tp, arc);
                using (Pen fp = new Pen(col, pw))
                {
                    fp.StartCap = fp.EndCap = LineCap.Round;
                    g.DrawArc(fp, arc, -90f, (float)Math.Max(6, 360 * frac));
                }
                g.SmoothingMode = sm;
                if (mtext)
                    TextRenderer.DrawText(g, PercentText, Font, new Rectangle(mr.X + UI.S(6) + ring + UI.S(5), mr.Y, mr.Width - ring - UI.S(11), mr.Height), frac >= 0.7 && !th.HighContrast ? col : th.ChromeDim,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
            bool stopMode = running && Box.TextLength == 0 && attachments.Count == 0;
            bool can = stopMode || Box.TextLength > 0 || attachments.Count > 0;
            Color fill = stopMode ? th.Fore : can ? th.Accent : th.ButtonHover;
            if (hoverSend && can) fill = Theme.Blend(fill, th.Dark ? Color.White : Color.Black, 0.12);
            Glyphs.FillRound(g, fill, sr, sr.Height / 2);
            Color fg = stopMode ? th.Back : can ? (th.HighContrast ? SystemColors.HighlightText : th.Dark ? Color.Black : Color.White) : th.ChromeDim;
            Glyphs.Draw(stopMode ? "stop" : "send", g, sr, fg);
        }

        int TileAt(Point p, out bool remove)
        {
            remove = false;
            for (int i = 0; i < tiles.Count; i++)
                if (tiles[i].Contains(p)) { remove = Attachments.RemoveRect(tiles[i]).Contains(p); return i; }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool s = SendRect.Contains(e.Location), ef = EffortRect.Contains(e.Location), at = AttachRect.Contains(e.Location);
            bool cx = ShowContextLine && CtxCloseRect.Contains(e.Location);
            bool rm;
            int ti = TileAt(e.Location, out rm);
            Rectangle mr;
            bool mt;
            bool me = MeterLayout(out mr, out mt) && mr.Contains(e.Location);
            if (s != hoverSend || ef != hoverEffort || at != hoverAttach || cx != hoverCtx || ti != hoverTile || rm != hoverRemove || me != hoverMeter)
            {
                hoverSend = s; hoverEffort = ef; hoverAttach = at; hoverCtx = cx; hoverTile = ti; hoverRemove = rm; hoverMeter = me;
                Cursor = s || ef || at || cx || rm || me ? Cursors.Hand : ti >= 0 ? Cursors.Default : Cursors.IBeam;
                tip.Hide(this);
                tip.SetToolTip(this, s ? (running && Box.TextLength == 0 && attachments.Count == 0 ? "Stop (Esc)" : running ? "Send to the agent while it works" : "Send (Enter)") : at ? "Attach files or images" : rm ? "Remove" : ti >= 0 ? Attachments.Tip(attachments[ti]) : me ? MeterTip : null);
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverSend = hoverEffort = hoverAttach = hoverCtx = hoverMeter = hoverRemove = false;
            hoverTile = -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (SendRect.Contains(e.Location))
            {
                if (running && Box.TextLength == 0 && attachments.Count == 0) { if (StopRequested != null) StopRequested(this, EventArgs.Empty); }
                else if (SendRequested != null) SendRequested(this, EventArgs.Empty);
                return;
            }
            if (EffortRect.Contains(e.Location)) { if (EffortClicked != null) EffortClicked(this, EventArgs.Empty); return; }
            if (AttachRect.Contains(e.Location)) { if (AttachClicked != null) AttachClicked(this, EventArgs.Empty); return; }
            Rectangle mr;
            bool mt;
            if (MeterLayout(out mr, out mt) && mr.Contains(e.Location)) { if (MeterClicked != null) MeterClicked(this, EventArgs.Empty); return; }
            bool rm;
            int ti = TileAt(e.Location, out rm);
            if (ti >= 0 && rm) { RemoveAttachment(ti); return; }
            if (ShowContextLine && CtxCloseRect.Contains(e.Location)) { contextOff = true; Relayout(); if (ContextCleared != null) ContextCleared(this, EventArgs.Empty); return; }
            Box.Focus();
        }

        void OnBoxKey(object sender, KeyEventArgs e)
        {
            if (KeyFilter != null && !e.Shift && !e.Control && !e.Alt && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape) && KeyFilter(e.KeyCode))
            {
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Enter && !e.Shift && !e.Control && !e.Alt)
            {
                e.Handled = e.SuppressKeyPress = true;
                if (SendRequested != null) SendRequested(this, EventArgs.Empty);
            }
            else if (e.KeyCode == Keys.Escape && running)
            {
                e.Handled = e.SuppressKeyPress = true;
                if (StopRequested != null) StopRequested(this, EventArgs.Empty);
            }
            else if (e.KeyCode == Keys.Up && !e.Shift && !e.Control && !e.Alt && Box.TextLength == 0 && attachments.Count == 0 && EditLastRequested != null)
            {
                e.Handled = e.SuppressKeyPress = true;
                EditLastRequested(this, EventArgs.Empty);
            }
            else if (((e.KeyCode == Keys.V && e.Modifiers == Keys.Control) || (e.KeyCode == Keys.Insert && e.Modifiers == Keys.Shift)) && CanPasteAttachment())
            {
                e.Handled = e.SuppressKeyPress = true;
                PasteAttachment();
            }
        }
    }
}
