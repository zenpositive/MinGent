using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MinGent
{
    class Settings
    {
        public string FontName = "Consolas";
        public float FontSize = 11f;
        public int Zoom;
        public bool Wrap;
        public string ThemeName = "dark";
        public string AccentName = "green";
        public bool MatchCase, WholeWord;
        public Rectangle Bounds;
        public bool Maximized;

        public bool NotesVisible, SidebarCollapsed, AgentVisible = true;
        public int NotesWidth = 560;
        public string AiModel = "";
        public string AiLastLmModel = "";
        public string AiMode = "ask";
        public bool AiAutoExplained, AiPlanExplained, AiFullExplained;
        public string AiAutoAllow = "";
        public bool AiCaptcha;
        public bool AiWebGuard = true;
        public bool AiWeb = true;
        public bool AiBrowser = true;
        public bool AiApps = true;
        public bool AiSideBySide = true;
        public bool AiMemory = true;
        public bool AiHelpers = true;
        public bool AiAutoReview = true;
        public bool AiAutoSave = true;
        public bool AiOpenFiles = true;
        public bool AiShowThinking = true;
        public bool AiSendContext = true;
        public string AiEffort = "";
        public string AiInstructions = "";
        public string AiWorkspace = "";
        public string AiLastChat = "";
        public float ChatFontSize = 10f;
        public string KeyAnthropic = "", KeyOpenAi = "", KeyGemini = "", KeyCustom = "", KeyLmStudio = "";
        public string CustomUrl = "", CustomModel = "";
        public int LmContext;
        public bool LmUnloadOthers = true;
        public int EngineContext;
        public string EngineKv = "q8_0";
        public bool OfferEngineUpdates;
        public bool BrowserVisible;
        public int BrowserWidth = 560;
        public string ModelsFolder = "", ModelsOldFolders = "";
        public bool Offline, KeepNetLog, KeepErrorLog;

        static readonly string[] KeptOnReset = {
            "KeyAnthropic", "KeyOpenAi", "KeyGemini", "KeyCustom", "KeyLmStudio", "CustomUrl", "CustomModel",
            "ModelsFolder", "ModelsOldFolders", "AiModel", "AiLastLmModel", "AiLastChat",
            "Bounds", "Maximized", "NotesVisible", "AgentVisible", "SidebarCollapsed", "NotesWidth", "BrowserWidth", "BrowserVisible",
            "AiAutoExplained", "AiPlanExplained", "AiFullExplained", "AiInstructions", "AiMemory", "Offline" };

        public void ResetPreferences()
        {
            Settings d = new Settings();
            foreach (System.Reflection.FieldInfo f in typeof(Settings).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (f.IsInitOnly || Array.IndexOf(KeptOnReset, f.Name) >= 0) continue;
                f.SetValue(this, f.GetValue(d));
            }
        }

        public void ApplyPrivacy()
        {
            Net.Offline = Offline;
            Net.KeepLog = KeepNetLog;
            Program.KeepErrorLog = KeepErrorLog;
        }

        static string FilePath { get { return Path.Combine(Program.DataDir, "settings.ini"); } }

        static string Esc(string v) { return (v ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }

        static string Unesc(string v)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length)
                {
                    char n = v[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(v[i]);
            }
            return sb.ToString();
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            string oldMode = null, oldBase = null, oldKey = null, oldModel = null, oldRef = null, newRef = null;
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                    int iv;
                    float fv;
                    switch (k)
                    {
                        case "font": if (v.Length > 0) s.FontName = v; break;
                        case "fontSize":
                            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out fv) && fv >= 5 && fv <= 72) s.FontSize = fv;
                            break;
                        case "zoom": if (int.TryParse(v, out iv) && iv >= -20 && iv <= 60) s.Zoom = iv; break;
                        case "wrap": s.Wrap = v == "1"; break;
                        case "theme": if (v == "system" || v == "light" || v == "dark") s.ThemeName = v; break;
                        case "accent": if (v == "green" || v == "windows") s.AccentName = v; break;
                        case "matchCase": s.MatchCase = v == "1"; break;
                        case "wholeWord": s.WholeWord = v == "1"; break;
                        case "maximized": s.Maximized = v == "1"; break;
                        case "notesVisible": s.NotesVisible = v == "1"; break;
                        case "agentVisible": s.AgentVisible = v != "0"; break;
                        case "sidebarCollapsed": s.SidebarCollapsed = v == "1"; break;
                        case "notesWidth": if (int.TryParse(v, out iv) && iv >= 250 && iv <= 3000) s.NotesWidth = iv; break;
                        case "aiModelRef": oldRef = v; break;
                        case "aiModel2": newRef = v; break;
                        case "aiLastLm": s.AiLastLmModel = v; break;
                        case "aiPermissions": s.AiMode = v == "auto" ? "auto" : "ask"; break;
                        case "aiAutoExplained": s.AiAutoExplained = v == "1"; break;
                        case "aiPlanExplained": s.AiPlanExplained = v == "1"; break;
                        case "aiFullExplained": s.AiFullExplained = v == "1"; break;
                        case "aiAutoAllow": s.AiAutoAllow = v; break;
                        case "aiCaptcha": s.AiCaptcha = v == "1"; break;
                        case "aiWebGuard": s.AiWebGuard = v != "0"; break;
                        case "aiWeb": s.AiWeb = v != "0"; break;
                        case "aiBrowser": s.AiBrowser = v != "0"; break;
                        case "aiApps": s.AiApps = v != "0"; break;
                        case "aiSideBySide": s.AiSideBySide = v != "0"; break;
                        case "aiMemory": s.AiMemory = v != "0"; break;
                        case "aiHelpers": s.AiHelpers = v != "0"; break;
                        case "aiAutoReview": s.AiAutoReview = v != "0"; break;
                        case "aiAutoSave": s.AiAutoSave = v != "0"; break;
                        case "aiOpenFiles": s.AiOpenFiles = v != "0"; break;
                        case "aiShowThinking": s.AiShowThinking = v != "0"; break;
                        case "aiSendContext": s.AiSendContext = v != "0"; break;
                        case "aiEffort": if (v.Length < 20) s.AiEffort = v; break;
                        case "aiInstructions": s.AiInstructions = Unesc(v); break;
                        case "aiWorkspace": s.AiWorkspace = v; break;
                        case "aiLastChat": if (v.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) s.AiLastChat = v; break;
                        case "chatFontSize":
                            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out fv) && fv >= 7 && fv <= 20) s.ChatFontSize = fv;
                            break;
                        case "keyAnthropic": s.KeyAnthropic = v; break;
                        case "keyOpenAi": s.KeyOpenAi = v; break;
                        case "keyGemini": s.KeyGemini = v; break;
                        case "keyCustom": s.KeyCustom = v; break;
                        case "keyLmStudio": s.KeyLmStudio = v; break;
                        case "customUrl": s.CustomUrl = v; break;
                        case "customModel": s.CustomModel = v; break;
                        case "lmContext": if (int.TryParse(v, out iv) && iv >= 0 && iv <= 4194304) s.LmContext = iv; break;
                        case "lmUnloadOthers": s.LmUnloadOthers = v != "0"; break;
                        case "engineContext": if (int.TryParse(v, out iv) && (iv == 0 || (iv >= 2048 && iv <= 4194304))) s.EngineContext = iv; break;
                        case "engineKv": if (v == "f16" || v == "q8_0" || v == "q4_0") s.EngineKv = v; break;
                        case "engineUpdates": s.OfferEngineUpdates = v == "1"; break;
                        case "browserVisible": s.BrowserVisible = v == "1"; break;
                        case "modelsFolder": s.ModelsFolder = v; break;
                        case "modelsOld": s.ModelsOldFolders = v; break;
                        case "offline": s.Offline = v == "1"; break;
                        case "netLog": s.KeepNetLog = v == "1"; break;
                        case "errorLog": s.KeepErrorLog = v == "1"; break;
                        case "browserWidth": if (int.TryParse(v, out iv) && iv >= 250 && iv <= 3000) s.BrowserWidth = iv; break;
                        case "aiMode": oldMode = v; break;
                        case "aiBase": oldBase = v; break;
                        case "aiKey": oldKey = v; break;
                        case "aiModel": oldModel = v; break;
                        case "window":
                            {
                                string[] p = v.Split(',');
                                int x, y, w, h;
                                if (p.Length == 4 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y) &&
                                    int.TryParse(p[2], out w) && int.TryParse(p[3], out h))
                                    s.Bounds = new Rectangle(x, y, w, h);
                                break;
                            }
                    }
                }
                s.AiModel = newRef ?? (oldRef == null || oldRef == "lmstudio:" ? "" : oldRef);
                if (oldMode == "custom" && !string.IsNullOrEmpty(oldBase) && string.IsNullOrEmpty(s.CustomUrl))
                {
                    string b = oldBase.Trim().TrimEnd('/');
                    bool lm = b.EndsWith(":1234/v1") || b.EndsWith(":1234");
                    if (!lm)
                    {
                        s.CustomUrl = b;
                        s.KeyCustom = oldKey ?? "";
                        s.CustomModel = oldModel ?? "";
                        s.AiModel = "custom:" + (oldModel ?? "");
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            if (Program.Erased) return;
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                StringBuilder sb = new StringBuilder();
                Action<string, string> w = delegate (string k, string v) { sb.Append(k).Append('=').Append(v ?? "").Append("\r\n"); };
                w("font", FontName);
                w("fontSize", FontSize.ToString(CultureInfo.InvariantCulture));
                w("zoom", Zoom.ToString(CultureInfo.InvariantCulture));
                w("wrap", Wrap ? "1" : "0");
                w("theme", ThemeName);
                w("accent", AccentName);
                w("matchCase", MatchCase ? "1" : "0");
                w("wholeWord", WholeWord ? "1" : "0");
                w("maximized", Maximized ? "1" : "0");
                w("notesVisible", NotesVisible ? "1" : "0");
                w("agentVisible", AgentVisible ? "1" : "0");
                w("sidebarCollapsed", SidebarCollapsed ? "1" : "0");
                w("notesWidth", NotesWidth.ToString(CultureInfo.InvariantCulture));
                w("aiModel2", AiModel);
                w("aiLastLm", AiLastLmModel);
                w("aiPermissions", AiMode);
                w("aiAutoExplained", AiAutoExplained ? "1" : "0");
                w("aiPlanExplained", AiPlanExplained ? "1" : "0");
                w("aiFullExplained", AiFullExplained ? "1" : "0");
                w("aiAutoAllow", AiAutoAllow);
                w("aiCaptcha", AiCaptcha ? "1" : "0");
                w("aiWebGuard", AiWebGuard ? "1" : "0");
                w("aiWeb", AiWeb ? "1" : "0");
                w("aiBrowser", AiBrowser ? "1" : "0");
                w("aiApps", AiApps ? "1" : "0");
                w("aiSideBySide", AiSideBySide ? "1" : "0");
                w("aiMemory", AiMemory ? "1" : "0");
                w("aiHelpers", AiHelpers ? "1" : "0");
                w("aiAutoReview", AiAutoReview ? "1" : "0");
                w("aiAutoSave", AiAutoSave ? "1" : "0");
                w("aiOpenFiles", AiOpenFiles ? "1" : "0");
                w("aiShowThinking", AiShowThinking ? "1" : "0");
                w("aiSendContext", AiSendContext ? "1" : "0");
                w("aiEffort", AiEffort);
                w("aiInstructions", Esc(AiInstructions));
                w("aiWorkspace", AiWorkspace);
                w("aiLastChat", AiLastChat);
                w("chatFontSize", ChatFontSize.ToString(CultureInfo.InvariantCulture));
                if (KeyAnthropic.Length > 0) w("keyAnthropic", KeyAnthropic);
                if (KeyOpenAi.Length > 0) w("keyOpenAi", KeyOpenAi);
                if (KeyGemini.Length > 0) w("keyGemini", KeyGemini);
                if (KeyCustom.Length > 0) w("keyCustom", KeyCustom);
                if (KeyLmStudio.Length > 0) w("keyLmStudio", KeyLmStudio);
                w("customUrl", CustomUrl);
                w("customModel", CustomModel);
                w("lmContext", LmContext.ToString(CultureInfo.InvariantCulture));
                w("lmUnloadOthers", LmUnloadOthers ? "1" : "0");
                w("engineContext", EngineContext.ToString(CultureInfo.InvariantCulture));
                w("engineKv", EngineKv);
                w("engineUpdates", OfferEngineUpdates ? "1" : "0");
                w("browserVisible", BrowserVisible ? "1" : "0");
                if (ModelsFolder.Length > 0) w("modelsFolder", ModelsFolder);
                if (ModelsOldFolders.Length > 0) w("modelsOld", ModelsOldFolders);
                w("offline", Offline ? "1" : "0");
                w("netLog", KeepNetLog ? "1" : "0");
                w("errorLog", KeepErrorLog ? "1" : "0");
                w("browserWidth", BrowserWidth.ToString(CultureInfo.InvariantCulture));
                w("window", Bounds.X + "," + Bounds.Y + "," + Bounds.Width + "," + Bounds.Height);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null, true);
                else File.Move(tmp, FilePath);
            }
            catch { }
        }

        public List<string> AutoAllowList
        {
            get
            {
                List<string> l = new List<string>();
                foreach (string c in (AiAutoAllow ?? "").Split(','))
                    if (Array.IndexOf(PermissionCategory.All, c.Trim()) >= 0 && !l.Contains(c.Trim())) l.Add(c.Trim());
                return l;
            }
        }

        public string DefaultWorkspace
        {
            get
            {
                if (!string.IsNullOrEmpty(AiWorkspace)) return AiWorkspace;
                string docs = "";
                try { docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch { }
                if (string.IsNullOrEmpty(docs)) docs = Path.GetTempPath();
                string fresh = Path.Combine(docs, "MinGent Files"), old = Path.Combine(docs, "JustPad Files");
                try { if (!Directory.Exists(fresh) && Directory.Exists(old)) return old; } catch { }
                return fresh;
            }
        }
    }

    class MainForm : Form, IAssistantHost, IEditorBridge
    {
        readonly List<Document> docs = new List<Document>();
        int active = -1;
        readonly bool primary;
        readonly EditorView editor = new EditorView();
        readonly TabStrip tabs = new TabStrip();
        readonly FindBar findBar = new FindBar();
        readonly StatusStrip status = new StatusStrip();
        readonly Panel contentPanel = new Panel();
        readonly AppShell shell = new AppShell();
        readonly Sidebar sidebar = new Sidebar();
        readonly ButtonBar notesTools = new ButtonBar();
        FlatButton bUndo, bRedo, bAgent;
        bool openedFiles;
        AiPanel aiPanel;
        BrowserPane browser;
        readonly TabNav nav = new TabNav();
        readonly Panel tabRow = new Panel();
        readonly ThemedTip navTip = new ThemedTip();
        readonly List<NavPoint> navBack = new List<NavPoint>(), navFwd = new List<NavPoint>();
        bool navigating;
        readonly Dictionary<int, RunTabs> runTabs = new Dictionary<int, RunTabs>();
        readonly Dictionary<Document, string> docKeys = new Dictionary<Document, string>();
        int nextDocKey;

        class NotesGroup
        {
            public readonly List<Document> Docs = new List<Document>();
            public Document Active;
        }

        readonly Dictionary<string, NotesGroup> parked = new Dictionary<string, NotesGroup>(StringComparer.OrdinalIgnoreCase);
        readonly List<KeyValuePair<string, NotesGroup>> restoredGroups = new List<KeyValuePair<string, NotesGroup>>();
        readonly List<Document> recovered = new List<Document>();
        string notesKey = "~new";
        int notesConv = -1;
        bool notesStarted, syncingNotes, notesAgain;

        class RunTabs
        {
            public readonly Dictionary<Document, List<UndoGroup>> Groups = new Dictionary<Document, List<UndoGroup>>();
        }

        class NavPoint
        {
            public Document Doc;
            public Pos Caret;
            public NavPoint(Document d, Pos p) { Doc = d; Caret = p; }
        }
        readonly ContextMenuStrip editMenu = new ThemedMenu();
        readonly ContextMenuStrip tabMenu = new ThemedMenu();
        readonly ContextMenuStrip popup = new ThemedMenu();
        ToolStripStatusLabel stPos, stLang, stEnc, stEol, stZoom;
        ToolStripMenuItem cmUndo, cmRedo, cmCut, cmCopy, cmPaste, cmDelete, cmExplain, cmFix;
        ToolStripMenuItem tmCopyPath, tmOpenFolder;
        Theme theme;
        readonly Settings settings;
        readonly Timer sessionTimer = new Timer();
        readonly Timer findTimer = new Timer();
        bool sessionDirty, checkingDisk, closing, themePending;
        IpcWindow ipc;
        Pos searchOrigin;
        int tabMenuIndex = -1;
        string lastSessionError;
        Font editorFont;

        string SessionPath { get { return Path.Combine(Program.SessionDir ?? Program.DataDir, "session.txt"); } }
        string BackupDir { get { return Path.Combine(Program.SessionDir ?? Program.DataDir, "backup"); } }
        readonly HashSet<string> ownedBackups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Document CurDoc { get { return active >= 0 && active < docs.Count ? docs[active] : null; } }

        public MainForm(string[] args, bool primary)
        {
            this.primary = primary;
            Text = Program.AppName;
            Font = UI.Font;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(UI.S(420), UI.S(260));
            AllowDrop = true;
            try { Icon = LoadIcon(); } catch { }

            settings = Settings.Load();
            settings.ApplyPrivacy();
            ModelLibrary.Apply(settings);

            BuildStatus();
            BuildContextMenus();

            editor.Dock = DockStyle.Fill;
            editor.AllowDrop = true;
            editor.ContextMenuStrip = editMenu;
            editor.TextEdited += delegate { OnTextEdited(); };
            editor.CaretMoved += delegate { OnCaretMoved(); };
            editor.ZoomRequested += delegate(int d) { Zoom(d); };
            editor.DragEnter += OnFileDragEnter;
            editor.DragDrop += OnFileDragDrop;

            tabs.Dock = DockStyle.Top;
            tabs.Docs = docs;
            tabs.AllowDrop = true;
            tabs.SelectRequested += delegate(int i) { SelectTab(i); editor.Focus(); };
            tabs.CloseRequested += delegate(int i) { CloseTab(i); };
            tabs.NewRequested += delegate { NewTab(); };
            tabs.ListRequested += delegate { ShowTabList(); };
            tabs.MoveRequested += MoveTab;
            tabs.MenuRequested += ShowTabMenu;
            tabs.DragEnter += OnFileDragEnter;
            tabs.DragDrop += OnFileDragDrop;

            findBar.Dock = DockStyle.Bottom;
            findBar.Visible = false;
            findBar.MatchCase = settings.MatchCase;
            findBar.WholeWord = settings.WholeWord;
            findBar.TermChanged += delegate { OnFindTermChanged(); };
            findBar.OptionsChanged += delegate { OnFindTermChanged(); };
            findBar.FindRequested += delegate(bool back) { FindNext(back); };
            findBar.CloseRequested += delegate { HideFind(); };
            findBar.ReplaceRequested += delegate { ReplaceOne(); };
            findBar.ReplaceAllRequested += delegate { ReplaceAll(); };

            status.Dock = DockStyle.Bottom;

            contentPanel.Controls.Add(editor);
            contentPanel.Controls.Add(findBar);
            contentPanel.Controls.Add(status);
            nav.Dock = DockStyle.Left;
            nav.Width = UI.S(66);
            nav.Back.Click += delegate { NavGo(true); };
            nav.Forward.Click += delegate { NavGo(false); };
            navTip.SetToolTip(nav.Back, "Back (Alt+Left)");
            navTip.SetToolTip(nav.Forward, "Forward (Alt+Right)");
            bAgent = notesTools.Add("face", "Show the agent (Ctrl+Shift+A)", delegate { ShowAgent(true, true); });
            notesTools.AddGap();
            bUndo = notesTools.Add("undo", "Undo (Ctrl+Z)", delegate { editor.Undo(); editor.Focus(); UpdateUndoButtons(); });
            bRedo = notesTools.Add("redo", "Redo (Ctrl+Y)", delegate { editor.Redo(); editor.Focus(); UpdateUndoButtons(); });
            bUndo.Enabled = bRedo.Enabled = false;
            notesTools.AddGap();
            FlatButton bNotesMore = null;
            bNotesMore = notesTools.Add("more", "More", delegate { ShowNotesMenu(bNotesMore); });
            notesTools.Add("x", "Close notes (Ctrl+Shift+E)", delegate { ShowNotes(false, false); });
            notesTools.Dock = DockStyle.Right;
            notesTools.Width = notesTools.PreferredWidth;
            tabs.Dock = DockStyle.Fill;
            tabRow.Dock = DockStyle.Top;
            tabRow.Height = tabs.Height;
            tabRow.Controls.Add(tabs);
            tabRow.Controls.Add(nav);
            tabRow.Controls.Add(notesTools);
            contentPanel.Controls.Add(tabRow);
            MouseEventHandler sideButtons = delegate (object s, MouseEventArgs me)
            {
                if (me.Button == MouseButtons.XButton1) NavGo(true);
                else if (me.Button == MouseButtons.XButton2) NavGo(false);
            };
            editor.MouseDown += sideButtons;
            tabs.MouseDown += sideButtons;

            browser = new BrowserPane();
            browser.Visible = false;
            browser.AppShortcut = BrowserShortcut;
            browser.ShowRequested += delegate { ShowBrowser(true, false); };
            browser.CloseRequested += delegate { ShowBrowser(false, false); };
            aiPanel = new AiPanel(settings, this);
            aiPanel.NotesClicked += delegate { ShowNotes(!shell.NotesOn, true); };
            aiPanel.BrowserClicked += delegate { ShowBrowser(!shell.BrowserOn, true); };
            aiPanel.ChatsChanged += delegate { RefreshSidebar(); };
            aiPanel.StateChanged += delegate { sidebar.SetActive(aiPanel.ActiveChatPath, aiPanel.Running); UpdateTitle(); };
            aiPanel.RenameRequested += delegate { if (aiPanel.ActiveChatPath != null) RenameChat(aiPanel.ActiveChatPath); };
            aiPanel.DeleteRequested += delegate { if (aiPanel.ActiveChatPath != null) DeleteChat(aiPanel.ActiveChatPath); };
            aiPanel.ContextToggled += delegate { UpdateAiChip(); };
            aiPanel.CloseClicked += delegate { ShowAgent(false, true); };
            aiPanel.NotesChatChanged += delegate { SyncNotesChat(); };

            sidebar.NewChatClicked += delegate { aiPanel.NewChat(); };
            sidebar.OpenClicked += delegate (string p) { aiPanel.OpenChat(p); };
            sidebar.DeleteClicked += DeleteChat;
            sidebar.RenameClicked += RenameChat;
            sidebar.DeleteAllClicked += delegate { DeleteAllChats(); };
            sidebar.SettingsClicked += delegate { ShowSettings(); };
            sidebar.LibraryClicked += delegate { ShowModelLibrary(); };
            sidebar.CollapseClicked += delegate { ToggleSidebar(); };

            if (!settings.AgentVisible) settings.NotesVisible = true;
            contentPanel.Visible = settings.NotesVisible;
            sidebar.Visible = aiPanel.Visible = settings.AgentVisible;
            notesTools.SetShown(bAgent, !settings.AgentVisible);
            notesTools.Width = notesTools.PreferredWidth;
            shell.AgentOn = settings.AgentVisible;
            shell.Side = sidebar;
            shell.Center = aiPanel;
            shell.Notes = contentPanel;
            shell.Browser = browser;
            shell.LeftCollapsed = settings.SidebarCollapsed;
            shell.NotesOn = settings.NotesVisible;
            shell.NotesWidth = Math.Max(UI.S(300), UI.S(settings.NotesWidth));
            shell.BrowserWidth = Math.Max(UI.S(340), UI.S(settings.BrowserWidth));
            shell.CenterMin = UI.S(380);
            shell.NotesMin = UI.S(300);
            shell.BrowserMin = UI.S(340);
            shell.Dock = DockStyle.Fill;
            shell.Controls.Add(sidebar);
            shell.Controls.Add(aiPanel);
            shell.Controls.Add(contentPanel);
            shell.Controls.Add(browser);
            shell.WidthsChanged += delegate
            {
                settings.NotesWidth = (int)Math.Round(shell.NotesWidth / UI.Scale);
                settings.BrowserWidth = (int)Math.Round(shell.BrowserWidth / UI.Scale);
            };

            Controls.Add(shell);
            aiPanel.SetPanes(shell.NotesOn, shell.BrowserOn);

            PlaceWindow();
            ApplyFont();
            editor.WordWrap = settings.Wrap;

            if (primary) RestoreSession();
            StartNotes();
            if (primary) { try { FileTypes.UpdateIfMoved(); } catch { } }
            foreach (string a in args) if (!string.IsNullOrEmpty(a)) { OpenFile(a); openedFiles = true; }
            if (docs.Count == 0) AddDoc(NewUntitled());
            else if (active < 0) SelectTab(0);
            ApplyTheme();
            RefreshSidebar();

            sessionTimer.Interval = 2500;
            sessionTimer.Tick += delegate { AutoSave(); };
            sessionTimer.Start();
            findTimer.Interval = 150;
            findTimer.Tick += delegate { findTimer.Stop(); UpdateFindCount(); };
        }

        static Icon LoadIcon()
        {
            using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("MinGent.ico"))
            {
                if (s != null) return new Icon(s);
            }
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }

        void PlaceWindow()
        {
            Rectangle b = settings.Bounds;
            bool ok = false;
            if (b.Width >= MinimumSize.Width && b.Height >= MinimumSize.Height)
                foreach (Screen sc in Screen.AllScreens)
                    if (sc.WorkingArea.IntersectsWith(new Rectangle(b.X + 40, b.Y + 10, Math.Max(1, b.Width - 80), 30))) { ok = true; break; }
            if (ok) Bounds = b;
            else
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Size sz = new Size(Math.Min(UI.S(1100), wa.Width * 9 / 10), Math.Min(UI.S(740), wa.Height * 9 / 10));
                Bounds = new Rectangle(wa.Left + (wa.Width - sz.Width) / 2, wa.Top + (wa.Height - sz.Height) / 2, sz.Width, sz.Height);
            }
            if (settings.Maximized) WindowState = FormWindowState.Maximized;
        }

        static ToolStripMenuItem Item(string text, Keys keys, string display, EventHandler click)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            if (keys != Keys.None) mi.ShortcutKeys = keys;
            if (display != null) mi.ShortcutKeyDisplayString = display;
            if (click != null) mi.Click += click;
            return mi;
        }

        void ShowNotesMenu(Control anchor)
        {
            ContextMenuStrip m = Menus.Create(theme);
            m.ShowCheckMargin = true;
            m.Items.Add(Item("New tab", Keys.None, "Ctrl+N", delegate { NewTab(); }));
            m.Items.Add(Item("Open...", Keys.None, "Ctrl+O", delegate { OpenDialog(); }));
            m.Items.Add(Item("Save", Keys.None, "Ctrl+S", delegate { SaveDoc(CurDoc, false); }));
            m.Items.Add(Item("Save as...", Keys.None, "Ctrl+Shift+S", delegate { SaveDoc(CurDoc, true); }));
            m.Items.Add(Item("Save all", Keys.None, null, delegate { SaveAll(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Find...", Keys.None, "Ctrl+F", delegate { ShowFind(false); }));
            m.Items.Add(Item("Replace...", Keys.None, "Ctrl+H", delegate { ShowFind(true); }));
            m.Items.Add(Item("Go to line...", Keys.None, "Ctrl+G", delegate { GoToLine(); }));
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem wrap = Item("Word wrap", Keys.None, "Alt+Z", delegate { ToggleWrap(); });
            wrap.Checked = editor.WordWrap;
            m.Items.Add(wrap);
            Document cd = CurDoc;
            m.Items.Add(Menus.Detail("Syntax highlighting...", cd != null && cd.Lang != null ? cd.Lang.Name : "", false, delegate
            {
                BeginInvoke(new Action(delegate { ShowLangPicker(anchor, new Point(anchor.Width, anchor.Height + UI.S(2)), ToolStripDropDownDirection.BelowLeft); }));
            }));
            m.Items.Add(Item("Font...", Keys.None, null, delegate { ChooseFont(); }));
            m.Items.Add(Item("Zoom in", Keys.None, "Ctrl+Plus", delegate { Zoom(1); }));
            m.Items.Add(Item("Zoom out", Keys.None, "Ctrl+Minus", delegate { Zoom(-1); }));
            m.Items.Add(Item("Reset zoom", Keys.None, "Ctrl+0", delegate { ZoomReset(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Preview in the browser", Keys.None, null, delegate { PreviewInBrowser(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Close tab", Keys.None, "Ctrl+W", delegate { CloseTab(active); }));
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(anchor, new Point(anchor.Width, anchor.Height + UI.S(2)), ToolStripDropDownDirection.BelowLeft);
        }

        void BuildStatus()
        {
            status.SizingGrip = false;
            status.ShowItemToolTips = true;
            status.Font = UI.Font;
            status.Padding = new Padding(UI.S(10), UI.S(1), UI.S(10), UI.S(1));
            stPos = new ToolStripStatusLabel("Ln 1, Col 1");
            ToolStripStatusLabel spring = new ToolStripStatusLabel("");
            spring.Spring = true;
            stLang = StatusItem("Plain Text", "Syntax highlighting", delegate { ShowLangMenu(); });
            stEol = StatusItem("Windows (CRLF)", "Line endings", delegate { ShowEolMenu(); });
            stEnc = StatusItem("UTF-8", "Encoding", delegate { ShowEncMenu(); });
            stZoom = StatusItem("100%", "Reset zoom", delegate { ZoomReset(); });
            status.Items.AddRange(new ToolStripItem[] { stPos, spring, stLang, stEol, stEnc, stZoom });
        }

        static ToolStripStatusLabel StatusItem(string text, string tip, EventHandler click)
        {
            ToolStripStatusLabel l = new ToolStripStatusLabel(text);
            l.Margin = new Padding(UI.S(14), UI.S(3), 0, UI.S(2));
            l.ToolTipText = tip;
            l.Click += click;
            return l;
        }

        void BuildContextMenus()
        {
            cmUndo = Item("Undo", Keys.None, "Ctrl+Z", delegate { editor.Undo(); });
            cmRedo = Item("Redo", Keys.None, "Ctrl+Y", delegate { editor.Redo(); });
            cmCut = Item("Cut", Keys.None, "Ctrl+X", delegate { editor.Cut(); });
            cmCopy = Item("Copy", Keys.None, "Ctrl+C", delegate { editor.Copy(); });
            cmPaste = Item("Paste", Keys.None, "Ctrl+V", delegate { editor.Paste(); });
            cmDelete = Item("Delete", Keys.None, "Del", delegate { editor.DeleteSelection(); });
            editMenu.Items.AddRange(new ToolStripItem[] { cmUndo, cmRedo, new ToolStripSeparator(), cmCut, cmCopy, cmPaste, cmDelete,
                new ToolStripSeparator(), Item("Select all", Keys.None, "Ctrl+A", delegate { editor.SelectAll(); }) });
            cmExplain = Item("Ask the agent to explain this", Keys.None, null, delegate { AskAssistant("Explain the selected code: what it does and how it works."); });
            cmFix = Item("Ask the agent to fix bugs here", Keys.None, null, delegate { AskAssistant("Find bugs in the selected code and fix them."); });
            editMenu.Items.AddRange(new ToolStripItem[] {
                new ToolStripSeparator(), cmExplain, cmFix,
                Item("Ask the agent...", Keys.None, "Ctrl+Shift+A", delegate { ShowAgent(true, true); }) });
            editMenu.Opening += delegate
            {
                UpdateEditItems(cmUndo, cmRedo, cmCut, cmCopy, cmPaste, cmDelete);
                Pos a, b;
                editor.GetSelection(out a, out b);
                cmExplain.Enabled = cmFix.Enabled = a != b;
            };

            tmCopyPath = Item("Copy file path", Keys.None, null, delegate
            {
                Document d = TabMenuDoc;
                if (d != null && d.Path != null) { try { Clipboard.SetText(d.Path); } catch { } }
            });
            tmOpenFolder = Item("Open containing folder", Keys.None, null, delegate
            {
                Document d = TabMenuDoc;
                if (d != null && d.Path != null && File.Exists(d.Path))
                {
                    try { Process.Start(Native.WindowsExe("explorer.exe"), "/select,\"" + d.Path + "\""); } catch { }
                }
            });
            tabMenu.Items.AddRange(new ToolStripItem[] {
                Item("Close", Keys.None, "Ctrl+W", delegate { if (TabMenuDoc != null) CloseTab(docs.IndexOf(TabMenuDoc)); }),
                Item("Close others", Keys.None, null, delegate { if (TabMenuDoc != null) CloseOthers(TabMenuDoc); }),
                Item("Close tabs to the right", Keys.None, null, delegate { if (TabMenuDoc != null) CloseToRight(TabMenuDoc); }),
                new ToolStripSeparator(), tmCopyPath, tmOpenFolder });
            tabMenu.Opening += delegate
            {
                Document d = TabMenuDoc;
                tmCopyPath.Enabled = tmOpenFolder.Enabled = d != null && d.Path != null;
            };
            foreach (ToolStripDropDown dd in new ToolStripDropDown[] { editMenu, tabMenu, popup }) Menus.Prepare(dd);
        }

        Document TabMenuDoc { get { return tabMenuIndex >= 0 && tabMenuIndex < docs.Count ? docs[tabMenuIndex] : null; } }

        void UpdateUndoButtons()
        {
            if (bUndo == null) return;
            Document d = CurDoc;
            bool u = d != null && d.CanUndo, r = d != null && d.CanRedo;
            if (bUndo.Enabled != u) { bUndo.Enabled = u; bUndo.Invalidate(); }
            if (bRedo.Enabled != r) { bRedo.Enabled = r; bRedo.Invalidate(); }
        }

        TextBox FocusedTextBox()
        {
            if (findBar.FindIn.Box.Focused) return findBar.FindIn.Box;
            if (findBar.ReplIn.Box.Focused) return findBar.ReplIn.Box;
            if (aiPanel != null && aiPanel.InputTextBox.Focused) return aiPanel.InputTextBox;
            return null;
        }

        void UpdateEditItems(ToolStripMenuItem undo, ToolStripMenuItem redo, ToolStripMenuItem cut, ToolStripMenuItem copy, ToolStripMenuItem paste, ToolStripMenuItem del)
        {
            TextBox tb = FocusedTextBox();
            Document d = CurDoc;
            if (tb != null)
            {
                undo.Enabled = tb.CanUndo;
                redo.Enabled = false;
                cut.Enabled = copy.Enabled = del.Enabled = tb.SelectionLength > 0;
            }
            else
            {
                undo.Enabled = d != null && d.CanUndo;
                redo.Enabled = d != null && d.CanRedo;
                cut.Enabled = copy.Enabled = true;
                del.Enabled = true;
            }
            paste.Enabled = editor.CanPaste;
        }

        void EditCmd(string cmd)
        {
            TextBox tb = FocusedTextBox();
            if (tb != null)
            {
                switch (cmd)
                {
                    case "undo": tb.Undo(); break;
                    case "cut": tb.Cut(); break;
                    case "copy": tb.Copy(); break;
                    case "paste": tb.Paste(); break;
                    case "delete": if (tb.SelectionLength > 0) tb.SelectedText = ""; break;
                    case "selectall": tb.SelectAll(); break;
                }
                return;
            }
            switch (cmd)
            {
                case "undo": editor.Undo(); break;
                case "redo": editor.Redo(); break;
                case "cut": editor.Cut(); break;
                case "copy": editor.Copy(); break;
                case "paste": editor.Paste(); break;
                case "delete": editor.DeleteSelection(); break;
                case "selectall": editor.SelectAll(); break;
            }
            editor.Focus();
        }

        void ShowPopupAbove(ToolStripItem item)
        {
            Rectangle b = item.Bounds;
            popup.Show(status, new Point(b.Left, b.Top), ToolStripDropDownDirection.AboveRight);
        }

        void ShowLangMenu()
        {
            Rectangle b = stLang.Bounds;
            ShowLangPicker(status, new Point(b.Left, b.Top), ToolStripDropDownDirection.AboveRight);
        }

        void ShowLangPicker(Control anchor, Point at, ToolStripDropDownDirection dir)
        {
            Document d = CurDoc;
            List<Choice> items = new List<Choice>();
            int sel = -1;
            for (int i = 0; i < Syntax.All.Count; i++)
            {
                Choice c = new Choice();
                c.Title = Syntax.All[i].Name;
                List<string> ex = new List<string>();
                foreach (string x in Syntax.All[i].Exts.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (x.StartsWith(".") && ex.Count < 2) ex.Add(x);
                c.Note = string.Join("  ", ex.ToArray());
                items.Add(c);
                if (d != null && d.Lang == Syntax.All[i]) sel = i;
            }
            ChoicePopup p = new ChoicePopup(theme, items, sel, false, UI.S(260));
            p.Picked += delegate (int i) { if (i >= 0 && i < Syntax.All.Count) SetLang(Syntax.All[i]); };
            p.Closed += delegate { BeginInvoke(new Action(delegate { p.Dispose(); })); };
            p.Show(anchor, at, dir);
        }

        void ShowEncMenu()
        {
            Document d = CurDoc;
            if (d == null) return;
            popup.Items.Clear();
            for (int i = 0; i < Document.EncNames.Length; i++)
            {
                int k = i;
                ToolStripMenuItem mi = new ToolStripMenuItem(Document.EncNames[i]);
                mi.Checked = d.EncKind == i || (i == 4 && d.EncKind == 5);
                mi.Click += delegate { SetEncoding(k); };
                popup.Items.Add(mi);
            }
            ShowPopupAbove(stEnc);
        }

        void ShowEolMenu()
        {
            Document d = CurDoc;
            if (d == null) return;
            popup.Items.Clear();
            string[] names = { "Windows (CRLF)", "Unix (LF)", "Macintosh (CR)" };
            string[] vals = { "\r\n", "\n", "\r" };
            for (int i = 0; i < 3; i++)
            {
                string v = vals[i];
                ToolStripMenuItem mi = new ToolStripMenuItem(names[i]);
                mi.Checked = d.Eol == v;
                mi.Click += delegate { SetEol(v); };
                popup.Items.Add(mi);
            }
            ShowPopupAbove(stEol);
        }

        void ShowTabList()
        {
            popup.Items.Clear();
            for (int i = 0; i < docs.Count; i++)
            {
                int k = i;
                ToolStripMenuItem mi = Menus.Detail(docs[i].DisplayName, docs[i].Modified ? "\u25CF" : "", i == active, null);
                mi.ToolTipText = docs[i].Path;
                mi.Click += delegate { SelectTab(k); editor.Focus(); };
                popup.Items.Add(mi);
            }
            popup.Show(tabs, new Point(tabs.Width - UI.S(6), tabs.Height), ToolStripDropDownDirection.BelowLeft);
        }

        void ShowTabMenu(int i, Point p)
        {
            tabMenuIndex = i;
            tabMenu.Show(tabs, p);
        }

        void SetLang(Lang l)
        {
            Document d = CurDoc;
            if (d == null) return;
            d.SetLang(l, true);
            editor.Invalidate();
            UpdateStatus();
            sessionDirty = true;
        }

        void SetEncoding(int k)
        {
            Document d = CurDoc;
            if (d == null || d.EncKind == k) return;
            d.EncKind = k;
            d.SavePoint = -1;
            d.BackupDirty = true;
            AfterDocMetaChange();
        }

        void SetEol(string eol)
        {
            Document d = CurDoc;
            if (d == null || d.Eol == eol) return;
            d.Eol = eol;
            d.SavePoint = -1;
            d.BackupDirty = true;
            AfterDocMetaChange();
        }

        void AfterDocMetaChange()
        {
            tabs.Invalidate();
            UpdateTitle();
            UpdateStatus();
            sessionDirty = true;
        }

        void SetThemeName(string name)
        {
            settings.ThemeName = name;
            ApplyTheme();
            settings.Save();
        }

        Theme PickTheme()
        {
            if (SystemInformation.HighContrast) return Theme.HighContrastTheme();
            bool windows = settings.AccentName == "windows";
            bool dark = settings.ThemeName == "dark" || (settings.ThemeName != "light" && Theme.SystemUsesDark());
            Color accent = windows ? Theme.SystemAccent(dark) : Theme.Brand(dark);
            return dark ? Theme.DarkTheme(accent) : Theme.Light(accent);
        }

        void ApplyTheme()
        {
            Theme t = PickTheme();
            theme = t;
            SuspendLayout();
            BackColor = t.Chrome;
            ForeColor = t.ChromeFore;
            ThemedRenderer r = new ThemedRenderer(t);
            ToolStripManager.Renderer = r;
            foreach (ToolStrip ts in new ToolStrip[] { status, editMenu, tabMenu, popup }) ts.Renderer = r;
            foreach (ToolStripDropDown dd in new ToolStripDropDown[] { editMenu, tabMenu, popup }) Menus.Round(dd);
            notesTools.SetTheme(t);
            sidebar.SetTheme(t);
            status.BackColor = t.StatusBack;
            status.ForeColor = t.StatusFore;
            editor.SetTheme(t);
            tabs.SetTheme(t);
            nav.SetTheme(t);
            tabRow.BackColor = t.Chrome;
            navTip.Th = t;
            findBar.SetTheme(t);
            if (contentPanel != null) contentPanel.BackColor = t.Chrome;
            if (aiPanel != null) aiPanel.SetTheme(t);
            if (browser != null) browser.SetTheme(t);
            shell.SetTheme(t);
            ResumeLayout();
            Native.SetTitleBar(this, t);
            Invalidate(true);
            AppHighlight.SetColors(t.Accent, t.OnAccent);
        }

        void ScheduleThemeRefresh()
        {
            if (!IsHandleCreated || themePending) return;
            themePending = true;
            BeginInvoke(new Action(delegate { themePending = false; ApplyTheme(); }));
        }

        void ApplyFont()
        {
            float size = Math.Max(5f, Math.Min(96f, settings.FontSize + settings.Zoom));
            Font f = null;
            try
            {
                f = new Font(settings.FontName, size, FontStyle.Regular, GraphicsUnit.Point);
                if (!string.Equals(f.Name, settings.FontName, StringComparison.OrdinalIgnoreCase)) { f.Dispose(); f = null; }
            }
            catch { f = null; }
            if (f == null)
            {
                try { f = new Font("Consolas", size); } catch { f = null; }
                if (f == null || f.Name != "Consolas") f = new Font(FontFamily.GenericMonospace, size);
            }
            Font old = editorFont;
            editorFont = f;
            editor.SetEditorFont(f);
            if (aiPanel != null) aiPanel.SetEditorFont(f);
            if (old != null) old.Dispose();
            UpdateStatus();
        }

        int ZoomPercent { get { return (int)Math.Round(100.0 * (settings.FontSize + settings.Zoom) / settings.FontSize); } }

        void Zoom(int delta)
        {
            float size = settings.FontSize + settings.Zoom + delta;
            if (size < 5 || size > 96) return;
            settings.Zoom += delta;
            ApplyFont();
        }

        void ZoomReset()
        {
            if (settings.Zoom == 0) return;
            settings.Zoom = 0;
            ApplyFont();
        }

        void ChooseFont()
        {
            using (FontDialog dlg = new FontDialog())
            {
                dlg.FixedPitchOnly = true;
                dlg.ShowEffects = false;
                dlg.AllowScriptChange = false;
                dlg.FontMustExist = true;
                try { dlg.Font = new Font(settings.FontName, settings.FontSize); } catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                settings.FontName = dlg.Font.Name;
                settings.FontSize = Math.Max(6f, Math.Min(48f, dlg.Font.SizeInPoints));
                settings.Zoom = 0;
                ApplyFont();
                settings.Save();
            }
        }

        void ToggleWrap()
        {
            settings.Wrap = !editor.WordWrap;
            editor.WordWrap = settings.Wrap;
        }

        public void RevealAssistant()
        {
            if (!shell.AgentOn) ShowAgent(true, false);
        }

        void FocusMain()
        {
            if (shell.AgentOn) aiPanel.FocusInput();
            else editor.Focus();
        }

        void ShowAgent(bool show, bool focus)
        {
            if (!show && shell.AgentOn && aiPanel.HoldOpen()) return;
            if (shell.AgentOn != show)
            {
                if (!show && !shell.NotesOn) ShowNotes(true, false);
                shell.AgentOn = show;
                settings.AgentVisible = show;
                sidebar.Visible = show;
                aiPanel.Visible = show;
                notesTools.SetShown(bAgent, !show);
                notesTools.Width = notesTools.PreferredWidth;
                shell.PerformLayout();
                UpdateTitle();
                UpdateAiChip();
                if (show) RefreshSidebar();
            }
            if (show && focus) aiPanel.FocusInput();
            else if (!show && (focus || aiPanel.ContainsFocus || sidebar.ContainsFocus)) editor.Focus();
        }

        public void ResetBrowser()
        {
            if (browser != null) browser.ResetSession();
        }

        public void ClearBrowserHistory()
        {
            if (browser != null) browser.ClearHistory();
            else BrowserData.Mark(BrowserData.ClearMarker);
        }

        bool NotesShown { get { return shell.NotesOn; } }

        void ShowNotes(bool show, bool focus)
        {
            if (!show && !shell.AgentOn) ShowAgent(true, focus);
            if (shell.NotesOn != show)
            {
                shell.NotesOn = show;
                contentPanel.Visible = show;
                settings.NotesVisible = show;
                shell.PerformLayout();
                aiPanel.SetPanes(shell.NotesOn, shell.BrowserOn);
                UpdateAiChip();
                UpdateTitle();
                aiPanel.WorkspaceChanged();
            }
            if (show && focus) editor.Focus();
            else if (!show && (focus || contentPanel.ContainsFocus)) FocusMain();
        }

        void ShowBrowser(bool show, bool focus)
        {
            if (browser == null) return;
            if (shell.BrowserOn != show)
            {
                browser.Visible = show;
                shell.BrowserOn = show;
                settings.BrowserVisible = show;
                shell.PerformLayout();
                aiPanel.SetPanes(shell.NotesOn, shell.BrowserOn);
            }
            if (show && focus) { browser.MarkUserOpened(); browser.FocusAddress(); }
            else if (!show && (focus || browser.ContainsFocus)) FocusMain();
        }

        void ToggleSidebar()
        {
            if (shell.RailShown && !shell.LeftCollapsed)
            {
                if (shell.BrowserOn) ShowBrowser(false, false);
                if (shell.RailShown && shell.NotesOn) ShowNotes(false, false);
            }
            else shell.LeftCollapsed = !shell.LeftCollapsed;
            settings.SidebarCollapsed = shell.LeftCollapsed;
            shell.PerformLayout();
        }

        void RefreshSidebar()
        {
            if (aiPanel == null) return;
            sidebar.SetChats(aiPanel.ListChats(), aiPanel.ActiveChatPath, aiPanel.Running);
        }

        void DeleteChat(string path)
        {
            aiPanel.ConfirmAndDelete(path);
            RefreshSidebar();
        }

        void RenameChat(string path)
        {
            string old = aiPanel.ChatTitleOf(path);
            string t = TextPrompt.Ask(this, theme, "Rename chat", "Chat name", old);
            if (t == null || t.Length == 0 || t == old) return;
            aiPanel.RenameChat(path, t);
            RefreshSidebar();
        }

        bool WipeBrowserData(IWin32Window owner, Action<bool> done)
        {
            if (browser == null || browser.Wiping) return false;
            bool busy = aiPanel != null && aiPanel.Running;
            string text = "Wipe the built-in browser's data?\n\nYou'll be signed out of every site in MinGent's browser, and its history, cache, cookies and site data are erased from this PC. Your other browsers are not affected." +
                          (busy ? "\n\nThe agent is working right now; it will be stopped first." : "");
            if (!Alert.Confirm(owner, theme, text, Program.AppName, busy ? "Stop and wipe" : "Wipe", true)) return false;
            Action go = delegate { if (!browser.IsDisposed) browser.WipeAll(done); };
            if (busy) aiPanel.StopThen(go); else go();
            return true;
        }

        const string DeleteAllText = "Delete all chats?\n\nThey are erased from this PC and can't be brought back. What the agent remembered in them is forgotten too; facts you added yourself in Settings > Memory stay.";

        string DeleteAllPrompt()
        {
            List<string> unsaved = UnsavedNotes(null);
            return DeleteAllText + (unsaved.Count > 0 ? "\n\nTheir notes close too, and unsaved changes in " + AiPanel.NoteNames(unsaved) + " are lost. Files saved on your PC stay." : "");
        }

        void DeleteAllChats()
        {
            if (!Alert.Confirm(this, theme, DeleteAllPrompt(), Program.AppName, "Delete all", true)) return;
            aiPanel.StopThen(delegate { aiPanel.DeleteAllChats(); RefreshSidebar(); });
        }

        bool AskErase(IWin32Window owner, out bool downloads, out bool outputs)
        {
            downloads = outputs = false;
            List<string> roots = ModelLibrary.OwnRoots();
            Cursor was = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            long bytes = Eraser.DownloadBytes(roots);
            List<string> places = Eraser.DownloadPlaces(roots);
            string outDir = Eraser.OutputsFolder();
            int outFiles = outDir != null ? Eraser.CountFiles(outDir) : 0;
            Cursor.Current = was;
            List<string> unsaved = UnsavedNotes(null);
            string lost = unsaved.Count > 0 ? "Unsaved changes in " + AiPanel.NoteNames(unsaved) + " are lost." : null;
            using (EraseDialog d = new EraseDialog(theme, bytes, places, outDir, outFiles, lost))
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return false;
                downloads = d.Downloads;
                outputs = d.Outputs || (outDir != null && outFiles == 0);
            }
            return true;
        }

        void StartErase(bool downloads, bool outputs)
        {
            if (Program.Erased) return;
            Program.Erased = true;
            sessionTimer.Stop();
            if (library != null && !library.IsDisposed) { try { library.Hide(); } catch { } }
            ThemedForm wait = new ThemedForm(theme, Program.AppName, 400, 96);
            wait.FormBorderStyle = FormBorderStyle.FixedDialog;
            wait.ControlBox = false;
            wait.MaximizeBox = false;
            bool done = false;
            wait.FormClosing += delegate (object o, FormClosingEventArgs e) { if (!done) e.Cancel = true; };
            Label l = Dialogs.MakeLabel("Erasing MinGent's data. This takes a few seconds...", theme, false);
            l.ForeColor = theme.Fore;
            l.SetBounds(UI.S(24), UI.S(24), wait.ClientSize.Width - UI.S(48), wait.ClientSize.Height - UI.S(48));
            wait.Controls.Add(l);
            wait.Show(this);
            Enabled = false;
            aiPanel.StopThen(delegate
            {
                try { aiPanel.Shutdown(); } catch { }
                ModelDownloads.CancelAll();
                Shell.KillAll();
                try { Engine.Instance.Stop(); } catch { }
                if (browser != null) { try { browser.Release(); } catch { } }
                try { FileTypes.RemoveAll(); } catch { }
                List<string> roots = ModelLibrary.OwnRoots();
                string outDir = outputs ? Eraser.OutputsFolder() : null;
                System.Threading.Thread t = new System.Threading.Thread(delegate ()
                {
                    EraseResult result = new EraseResult();
                    try
                    {
                        DateTime until = DateTime.UtcNow.AddSeconds(10);
                        while (ModelDownloads.Active().Count > 0 && DateTime.UtcNow < until) System.Threading.Thread.Sleep(200);
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        result = Eraser.Run(downloads, roots, outDir);
                    }
                    catch { }
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            done = true;
                            try { wait.Close(); } catch { }
                            FinishErase(downloads, roots, result);
                        }));
                    }
                    catch { Eraser.AfterExit(result); Environment.Exit(0); }
                });
                t.IsBackground = true;
                t.Name = "MinGent erase";
                t.Start();
            });
        }

        void ResetSettings()
        {
            settings.ResetPreferences();
            settings.Save();
            settings.ApplyPrivacy();
            ModelLibrary.Apply(settings);
            findBar.MatchCase = settings.MatchCase;
            findBar.WholeWord = settings.WholeWord;
            editor.WordWrap = settings.Wrap;
            ApplyTheme();
            ApplyFont();
            if (aiPanel != null) aiPanel.SettingsChanged();
            UpdateAiChip();
        }

        void FinishErase(bool downloads, List<string> roots, EraseResult result)
        {
            Enabled = true;
            List<string> kept = downloads ? new List<string>() : Eraser.DownloadPlaces(roots);
            string links = "";
            for (int i = 0; i < result.Links.Count && i < 3; i++) links += "\n" + result.Links[i];
            if (result.Links.Count > 3) links += "\n(and " + (result.Links.Count - 3) + " more)";
            string text = (result.Left.Count == 0 ? "Everything MinGent stored on this PC is erased." : "Almost everything is erased. A few files were still in use; they are overwritten and deleted right after MinGent closes.") +
                          (kept.Count > 0 ? "\n\nThe engine and models you kept are in " + EraseDialog.Places(kept) + "." : "") +
                          (links.Length > 0 ? "\n\nThese were links to other folders. MinGent removed the links and left what they point to alone:" + links : "") +
                          "\n\nTo remove MinGent itself, delete the folder it runs from:\n" + Application.StartupPath +
                          "\n\nWindows keeps a few traces of any program you run, such as recent files in its Open and Save dialogs. MinGent can't remove those.";
            Alert.Show(this, theme, text, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Eraser.AfterExit(result);
            Environment.Exit(0);
        }

        void PreviewInBrowser()
        {
            Document d = CurDoc;
            if (d == null) return;
            if (d.Path == null)
            {
                Alert.Show(this, theme, "Save this tab as a file first (for example page.html), then preview it.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!browser.Available)
            {
                ShowBrowser(true, true);
                return;
            }
            if (d.Modified && QuietSave(d) != null && !SaveDoc(d, false)) return;
            AfterDocMetaChange();
            string full = FullOrNull(d.Path);
            if (full == null) return;
            browser.AllowFolder(Path.GetDirectoryName(full));
            ShowBrowser(true, true);
            browser.OpenUrl(new Uri(full).AbsoluteUri);
        }

        void AskAssistant(string request)
        {
            ShowAgent(true, false);
            aiPanel.AskAbout(request);
        }

        static string FullOrNull(string p)
        {
            try { return Path.GetFullPath(p).TrimEnd('\\'); } catch { return null; }
        }

        void AfterAgentChange()
        {
            sessionDirty = true;
            tabs.Invalidate();
            UpdateTitle();
            UpdateStatus();
        }

        readonly int uiThread = System.Threading.Thread.CurrentThread.ManagedThreadId;

        T OnUi<T>(Func<T> f)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == uiThread) return f();
            if (IsDisposed || closing || !IsHandleCreated) throw new InvalidOperationException("MinGent is closing");
            return (T)Invoke(f);
        }

        string DocKey(Document d)
        {
            string k;
            if (!docKeys.TryGetValue(d, out k)) { k = "tab" + (++nextDocKey).ToString(CultureInfo.InvariantCulture); docKeys[d] = k; }
            return k;
        }

        Document DocByKey(string key)
        {
            foreach (Document d in docs) { string k; if (docKeys.TryGetValue(d, out k) && k == key) return d; }
            return null;
        }

        TabInfo Info(Document d, bool withText)
        {
            TabInfo t = new TabInfo();
            t.Key = DocKey(d);
            t.Path = d.Path != null ? FullOrNull(d.Path) : null;
            t.Title = d.Path != null ? d.DisplayName : (d.Title ?? d.DisplayName);
            t.Untitled = d.Path == null;
            t.Modified = d.Modified;
            t.Active = d == CurDoc;
            t.Version = d.Version;
            t.Eol = d.Eol;
            t.Lines = d.Lines.Count;
            if (withText) t.Text = d.TextLength > 8000000 ? "" : d.AllText("\n");
            return t;
        }

        public IEditorBridge Editor { get { return this; } }
        public BrowserPane Browser { get { return browser; } }

        public List<TabInfo> Tabs(bool withText)
        {
            return OnUi<List<TabInfo>>(delegate ()
            {
                editor.SyncDocState();
                List<TabInfo> list = new List<TabInfo>();
                foreach (Document d in docs) list.Add(Info(d, withText));
                return list;
            });
        }

        public TabInfo FindTab(string nameOrPath, bool withText)
        {
            return OnUi<TabInfo>(delegate ()
            {
                if (string.IsNullOrEmpty(nameOrPath)) return null;
                editor.SyncDocState();
                Document byKey = DocByKey(nameOrPath);
                if (byKey != null) return Info(byKey, withText);
                string full = null;
                try { if (Path.IsPathRooted(nameOrPath)) full = FullOrNull(nameOrPath); } catch { }
                foreach (Document d in docs)
                    if (full != null && d.Path != null && string.Equals(FullOrNull(d.Path), full, StringComparison.OrdinalIgnoreCase)) return Info(d, withText);
                foreach (Document d in docs)
                    if (d.Path == null && string.Equals(d.Title ?? d.DisplayName, nameOrPath, StringComparison.OrdinalIgnoreCase)) return Info(d, withText);
                return null;
            });
        }

        public string SetTabText(string key, long expectedVersion, string newText, bool save, int runId)
        {
            return OnUi<string>(delegate ()
            {
                Document d = DocByKey(key);
                if (d == null) return "that tab was closed";
                if (d == CurDoc) editor.SyncDocState();
                if (d.Version != expectedVersion) return "the tab changed while the assistant was working on it (perhaps you typed in it). Read it again and redo the change.";
                bool changed;
                if (d == CurDoc) changed = editor.ReplaceAllText(newText);
                else { Pos a, b; changed = d.ReplaceAllMinimal(newText, out a, out b); }
                if (!changed) return null;
                RecordGroup(d, runId);
                string saveErr = !save || d.Path == null ? null : d.DiskMissing ? "the file was deleted or moved on disk, so it was not written back" : QuietSave(d);
                if (d != CurDoc) editor.Invalidate();
                AfterAgentChange();
                return saveErr != null ? "!The change is in the tab, but saving it to disk failed (" + saveErr + "). The tab has unsaved changes." : null;
            });
        }

        public string OpenFile(string fullPath, int line)
        {
            return OnUi<string>(delegate ()
            {
                Document d = DocByKey(fullPath);
                string full = d == null ? FullOrNull(fullPath) : null;
                if (d == null)
                    foreach (Document x in docs)
                        if (x.Path != null && string.Equals(FullOrNull(x.Path), full, StringComparison.OrdinalIgnoreCase)) { d = x; break; }
                if (d == null)
                {
                    if (full == null || !File.Exists(full)) return "file not found: " + fullPath;
                    if (LooksBinary(full)) return "that is a binary file, so it was not opened";
                    try { if (new FileInfo(full).Length > 64L * 1024 * 1024) return "the file is too large to open in a tab"; } catch { }
                    OpenFile(full);
                    foreach (Document x in docs)
                        if (x.Path != null && string.Equals(FullOrNull(x.Path), full, StringComparison.OrdinalIgnoreCase)) { d = x; break; }
                    if (d == null) return "could not open " + full;
                }
                int i = docs.IndexOf(d);
                if (i != active) { NoteNav(); SelectTab(i); }
                if (line > 0) editor.GoToLine(Math.Min(line, d.Lines.Count) - 1);
                ShowNotes(true, false);
                return null;
            });
        }

        public string NewTab(string title, string text, int runId)
        {
            return OnUi<string>(delegate ()
            {
                Document d = NewUntitled();
                if (!string.IsNullOrEmpty(title)) d.Title = title;
                d.SetText(text ?? "", true);
                d.SavePoint = -1;
                try { d.SetLang(Syntax.Detect(title, d.Lines.Count > 0 ? d.Lines[0] : ""), false); } catch { }
                AddDoc(d);
                ShowNotes(true, false);
                return DocKey(d);
            });
        }

        public string OpenNotes()
        {
            return OnUi<string>(delegate ()
            {
                if (docs.Count == 0) { AddDoc(NewUntitled()); }
                bool was = shell.NotesOn;
                ShowNotes(true, false);
                if (!shell.NotesOn) return "The notes pane could not be opened.";
                Document d = active >= 0 && active < docs.Count ? docs[active] : null;
                string name = d != null && !string.IsNullOrEmpty(d.Title) ? d.Title : "Untitled";
                return (was ? "The notes pane is already open" : "Opened the notes pane") + ", showing " + name + ".";
            });
        }

        public string CloseNotes(bool userAsked)
        {
            return OnUi<string>(delegate ()
            {
                if (!shell.NotesOn) return "The notes pane is already closed.";
                List<string> open = new List<string>();
                foreach (Document d in docs)
                {
                    bool empty = d.Path == null && (d.Lines.Count == 0 || (d.Lines.Count == 1 && d.Lines[0].Length == 0));
                    if (!empty) open.Add(d.Title ?? "Untitled");
                }
                if (!userAsked && open.Count > 0)
                {
                    string names = open.Count <= 3 ? string.Join(", ", open.ToArray()) : open[0] + ", " + open[1] + " and " + (open.Count - 2) + " more";
                    return "The notes pane has " + names + " open, so it stays open. Close it only if the user asks.";
                }
                ShowNotes(false, false);
                if (shell.NotesOn) return "The notes pane could not be closed.";
                return "Closed the notes pane." + (open.Count > 0 ? " Its tabs are still there when it opens again." : "");
            });
        }

        public void FileChangedOnDisk(string fullPath)
        {
            try
            {
                OnUi<bool>(delegate ()
                {
                    string full = FullOrNull(fullPath);
                    foreach (Document d in docs.ToArray())
                        if (d.Path != null && string.Equals(FullOrNull(d.Path), full, StringComparison.OrdinalIgnoreCase)) CheckDisk(d);
                    return true;
                });
            }
            catch { }
        }

        public string ActiveContext(out string label)
        {
            label = null;
            Document d = CurDoc;
            if (d == null || !NotesShown) return null;
            editor.SyncDocState();
            string name = d.Path ?? (d.Title ?? d.DisplayName);
            Pos a, b;
            editor.GetSelection(out a, out b);
            if (a == b && d.Path == null && d.IsEmpty) return null;
            if (a == b)
            {
                label = d.DisplayName;
                return "[The user's active tab is " + (d.Path != null ? d.Path : "\"" + name + "\" (an unsaved tab)") + ", " + d.Lines.Count + " lines" +
                       (d.Modified && d.Path != null ? ", with unsaved changes" : "") + ", cursor on line " + (d.Caret.Line + 1) + ". Read it with read_file if the request is about it.]";
            }
            int from = a.Line + 1, to = b.Col == 0 && b.Line > a.Line ? b.Line : b.Line + 1;
            string sel = d.GetText(a, b);
            if (sel.Length > 30000) sel = sel.Substring(0, 30000) + "\n...(selection truncated)";
            label = d.DisplayName + " \u00B7 " + (from == to ? "line " + from : "lines " + from + "-" + to);
            string fence = sel.Contains("```") ? "````" : "```";
            return "[The user selected " + (from == to ? "line " + from : "lines " + from + "-" + to) + " of " + (d.Path != null ? d.Path : "\"" + name + "\" (an unsaved tab)") + ":]\n" + fence + "\n" + sel + "\n" + fence;
        }

        public string WorkspaceFolder(out bool isDefault)
        {
            bool def = true;
            string ws = OnUi<string>(delegate ()
            {
                Document d = NotesShown ? CurDoc : null;
                string folder = null;
                if (d != null && d.Path != null)
                {
                    try { folder = Path.GetDirectoryName(Path.GetFullPath(d.Path)); } catch { folder = null; }
                }
                if (folder != null && !BroadFolder(folder) && AgentPaths.WriteBlocked(Path.Combine(folder, "x.txt")) == null) { def = false; return folder; }
                return settings.DefaultWorkspace;
            });
            isDefault = def;
            return ws;
        }

        static bool BroadFolder(string folder)
        {
            string f = folder.TrimEnd('\\');
            if (f.Length <= 3) return true;
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
                if (string.Equals(f, home, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(f, Path.GetDirectoryName(home), StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        public void RunStarted(int runId)
        {
            runTabs[runId] = new RunTabs();
            if (runTabs.Count > 40)
            {
                int oldest = int.MaxValue;
                foreach (int k in runTabs.Keys) if (k < oldest) oldest = k;
                runTabs.Remove(oldest);
            }
        }

        void RecordGroup(Document d, int runId)
        {
            RunTabs rt;
            if (!runTabs.TryGetValue(runId, out rt) || d.UndoPos == 0) return;
            UndoGroup g = d.Undo[d.UndoPos - 1];
            List<UndoGroup> list;
            if (!rt.Groups.TryGetValue(d, out list)) { list = new List<UndoGroup>(); rt.Groups[d] = list; }
            if (!list.Contains(g)) list.Add(g);
        }

        public int RunChangeCount(int runId, RunChangeLog log)
        {
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RunTabs rt;
            if (runTabs.TryGetValue(runId, out rt))
                foreach (Document d in rt.Groups.Keys) if (docs.Contains(d)) files.Add(d.Path != null ? FullOrNull(d.Path) ?? d.Path : DocKey(d));
            if (log != null)
            {
                foreach (string f in log.Originals.Keys) files.Add(f);
                foreach (string f in log.Created) files.Add(f);
            }
            return files.Count;
        }

        public string UndoRun(int runId, RunChangeLog log, out int reverted, out int keptCount)
        {
            int undone = 0, kept = 0, restored = 0, removed = 0;
            reverted = keptCount = 0;
            List<string> names = new List<string>();
            RunTabs rt;
            if (runTabs.TryGetValue(runId, out rt))
            {
                foreach (KeyValuePair<Document, List<UndoGroup>> kv in rt.Groups)
                {
                    Document d = kv.Key;
                    if (!docs.Contains(d)) { kept += kv.Value.Count; continue; }
                    int before = undone;
                    while (d.UndoPos > 0 && kv.Value.Contains(d.Undo[d.UndoPos - 1]))
                    {
                        if (d != CurDoc) SelectTab(docs.IndexOf(d));
                        editor.Undo();
                        undone++;
                    }
                    foreach (UndoGroup g in kv.Value) { int at = d.Undo.IndexOf(g); if (at >= 0 && at < d.UndoPos) kept++; }
                    if (undone > before)
                    {
                        names.Add(d.DisplayName);
                        if (settings.AiAutoSave && d.Path != null && d.Modified) QuietSave(d);
                    }
                }
                runTabs.Remove(runId);
            }
            if (log != null)
            {
                foreach (KeyValuePair<string, byte[]> kv in log.Originals)
                {
                    if (log.ChangedSince(kv.Key)) { kept++; continue; }
                    try
                    {
                        if (!File.Exists(kv.Key)) Directory.CreateDirectory(Path.GetDirectoryName(kv.Key));
                        WriteFile(kv.Key, kv.Value);
                        restored++;
                        names.Add(Path.GetFileName(kv.Key));
                        foreach (Document d in docs.ToArray())
                            if (d.Path != null && string.Equals(FullOrNull(d.Path), kv.Key, StringComparison.OrdinalIgnoreCase)) CheckDisk(d);
                    }
                    catch { kept++; }
                }
                foreach (string f in log.Created)
                {
                    if (log.ChangedSince(f)) { kept++; continue; }
                    foreach (Document d in docs.ToArray())
                        if (d.Path != null && string.Equals(FullOrNull(d.Path), f, StringComparison.OrdinalIgnoreCase) && !d.Modified) DropDoc(d);
                    if (File.Exists(f) && (RecycleBin.Send(f) || TryDelete(f))) removed++;
                }
                for (int i = log.CreatedDirs.Count - 1; i >= 0; i--) RemoveEmptyDirs(log.CreatedDirs[i]);
                log.Originals.Clear();
                log.Created.Clear();
                log.CreatedDirs.Clear();
            }
            editor.Invalidate();
            AfterAgentChange();
            StringBuilder sb = new StringBuilder();
            int changed = undone + restored;
            reverted = changed + removed;
            keptCount = kept;
            if (changed > 0) sb.Append("Reverted ").Append(names.Count == 1 ? names[0] : names.Count + " files");
            if (removed > 0) sb.Append(sb.Length > 0 ? " and moved " : "Moved ").Append(removed).Append(removed == 1 ? " new file" : " new files").Append(" to the Recycle Bin");
            if (sb.Length == 0) sb.Append("There was nothing left to undo");
            sb.Append('.');
            if (kept > 0) sb.Append(" ").Append(kept).Append(kept == 1 ? " change was" : " changes were").Append(" kept because the file was edited or closed afterwards.");
            sb.Append(" Commands the agent ran cannot be undone.");
            return sb.ToString();
        }

        static void RemoveEmptyDirs(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (string sub in Directory.GetDirectories(dir)) RemoveEmptyDirs(sub);
                if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
            }
            catch { }
        }

        static bool TryDelete(string f)
        {
            try { File.Delete(f); return true; } catch { return false; }
        }

        void DropBackup(Document d)
        {
            if (!primary || d == null || d.BackupFile == null) return;
            string name = d.BackupFile;
            d.BackupFile = null;
            d.BackupDirty = false;
            EraseBackup(name);
            sessionDirty = true;
        }

        readonly HashSet<string> pendingErase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> erasing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void EraseBackup(string name)
        {
            if (string.IsNullOrEmpty(name) || erasing.Contains(name)) return;
            ownedBackups.Remove(name);
            string file = Path.Combine(BackupDir, name);
            long size = 0;
            try
            {
                if (!File.Exists(file)) { pendingErase.Remove(name); return; }
                size = new FileInfo(file).Length;
            }
            catch { }
            pendingErase.Add(name);
            if (size <= 8L * 1024 * 1024 || !IsHandleCreated)
            {
                if (SecureFile.Erase(file)) pendingErase.Remove(name);
                return;
            }
            erasing.Add(name);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = SecureFile.Erase(file);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        erasing.Remove(name);
                        if (ok) pendingErase.Remove(name);
                    }));
                }
                catch { }
            });
        }

        void ForgetDoc(Document d)
        {
            docKeys.Remove(d);
            navBack.RemoveAll(delegate (NavPoint p) { return p.Doc == d; });
            navFwd.RemoveAll(delegate (NavPoint p) { return p.Doc == d; });
        }

        void DropDoc(Document d)
        {
            int i = docs.IndexOf(d);
            if (i < 0) return;
            Document cur = CurDoc;
            docs.RemoveAt(i);
            if (!OpenElsewhere(d, null, false)) ForgetDoc(d);
            if (docs.Count == 0) { docs.Add(NewUntitled()); SelectTab(0); }
            else if (cur == d) SelectTab(Math.Min(i, docs.Count - 1));
            else SelectTab(Math.Max(0, docs.IndexOf(cur)));
            sessionDirty = true;
        }

        public void ShowSettings() { ShowSettings(null); }

        public void ShowSettings(string page)
        {
            string oldTheme = settings.ThemeName, oldAccent = settings.AccentName;
            ModelLibraryDialog parked = null;
            Point at = Point.Empty, client = Point.Empty;
            bool place = false;
            if (library != null && !library.IsDisposed && library.Visible)
            {
                if (library.WindowState == FormWindowState.Normal) { at = library.Location; client = library.PointToScreen(Point.Empty); place = true; }
                if (library.Busy) { parked = library; parked.Hide(); }
                else library.Close();
            }
            bool saved;
            int erase = -1;
            bool reset = false;
            try
            {
                using (SettingsDialog dlg = new SettingsDialog(settings, theme))
                {
                    if (page != null) dlg.OpenPage(page);
                    if (place)
                    {
                        dlg.StartPosition = FormStartPosition.Manual;
                        dlg.Location = at;
                        dlg.Load += delegate
                        {
                            Point cur = dlg.PointToScreen(Point.Empty);
                            dlg.Location = new Point(dlg.Left + client.X - cur.X, dlg.Top + client.Y - cur.Y);
                        };
                    }
                    dlg.DeleteAllChats = delegate
                    {
                        if (!Alert.Confirm(dlg, theme, DeleteAllPrompt(), Program.AppName, "Delete all", true)) return;
                        aiPanel.StopThen(delegate { aiPanel.DeleteAllChats(); RefreshSidebar(); });
                    };
                    dlg.WipeBrowser = WipeBrowserData;
                    dlg.ResetAll = delegate { reset = true; };
                    dlg.EraseAll = delegate (IWin32Window o)
                    {
                        bool dl, outp;
                        if (!AskErase(o, out dl, out outp)) return false;
                        erase = (dl ? 1 : 0) | (outp ? 2 : 0);
                        return true;
                    };
                    saved = dlg.ShowDialog(this) == DialogResult.OK;
                }
            }
            finally
            {
                if (parked != null && !parked.IsDisposed && erase < 0)
                {
                    parked.Visible = true;
                    Native.BringToFront(parked);
                }
            }
            if (erase >= 0) { StartErase((erase & 1) != 0, (erase & 2) != 0); return; }
            if (reset) { ResetSettings(); return; }
            if (!saved) return;
            bool wasOffline = Net.Offline;
            settings.Save();
            settings.ApplyPrivacy();
            if (Net.Offline && !wasOffline) ModelDownloads.CancelAll();
            if (browser != null && Net.Offline != wasOffline) browser.ApplyOffline();
            ModelLibrary.Apply(settings);
            EngineUpdates.CheckSoon();
            if (settings.ThemeName != oldTheme || settings.AccentName != oldAccent) ApplyTheme();
            if (aiPanel != null) aiPanel.SettingsChanged();
            UpdateAiChip();
        }

        ModelLibraryDialog library;

        public void ShowModelLibrary()
        {
            if (library == null || library.IsDisposed)
            {
                library = new ModelLibraryDialog(settings, theme);
                library.ModelChosen += delegate (string modelRef)
                {
                    settings.AiModel = modelRef;
                    settings.Save();
                    if (aiPanel != null) aiPanel.SettingsChanged();
                };
                library.FormClosed += delegate { library = null; if (aiPanel != null) aiPanel.RefreshConnection(true); };
                library.Show(this);
            }
            else
            {
                if (!library.Visible) library.Visible = true;
                Native.BringToFront(library);
            }
        }

        public void ShowBrowser() { ShowBrowser(true, true); }

        public void OpenLink(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                BrowserPane.OpenExternally(target);
                return;
            }
            string path = target;
            int line = 0;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(target, @"^(.*?):(\d+)(?::\d+)?$");
            if (m.Success && m.Groups[1].Value.Length > 2) { path = m.Groups[1].Value; int.TryParse(m.Groups[2].Value, out line); }
            bool def;
            string err;
            string full = AgentPaths.Resolve(path, WorkspaceFolder(out def), out err);
            if (full == null || !File.Exists(full)) return;
            if (DocExport.CanWrite(full))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(full);
                    psi.UseShellExecute = true;
                    using (Process.Start(psi)) { }
                }
                catch (Exception ex)
                {
                    Alert.Show(this, theme, "Could not open \"" + Path.GetFileName(full) + "\".\n\n" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }
            OpenFile(full, line);
        }

        void NoteNav()
        {
            if (navigating) return;
            Document d = editor.Doc;
            if (d == null || !docs.Contains(d)) return;
            NavPoint last = navBack.Count > 0 ? navBack[navBack.Count - 1] : null;
            if (last != null && last.Doc == d && Math.Abs(last.Caret.Line - d.Caret.Line) < 8) last.Caret = d.Caret;
            else
            {
                navBack.Add(new NavPoint(d, d.Caret));
                if (navBack.Count > 200) navBack.RemoveAt(0);
            }
            navFwd.Clear();
            UpdateNavButtons();
        }

        bool Useful(NavPoint p)
        {
            return docs.Contains(p.Doc) && !(p.Doc == editor.Doc && Math.Abs(p.Doc.Caret.Line - p.Caret.Line) < 2);
        }

        void NavGo(bool back)
        {
            List<NavPoint> from = back ? navBack : navFwd, to = back ? navFwd : navBack;
            while (from.Count > 0 && !Useful(from[from.Count - 1])) from.RemoveAt(from.Count - 1);
            if (from.Count == 0) { UpdateNavButtons(); return; }
            NavPoint p = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Document cur = editor.Doc;
            if (cur != null && docs.Contains(cur)) to.Add(new NavPoint(cur, cur.Caret));
            navigating = true;
            try
            {
                int i = docs.IndexOf(p.Doc);
                if (i != active) SelectTab(i);
                editor.GoToLine(Math.Min(p.Caret.Line, p.Doc.Lines.Count - 1));
                Pos c = p.Doc.Clamp(p.Caret);
                editor.Select(c, c);
            }
            finally { navigating = false; }
            UpdateNavButtons();
            editor.Focus();
        }

        void UpdateNavButtons()
        {
            bool b = false, f = false;
            foreach (NavPoint p in navBack) if (Useful(p)) { b = true; break; }
            foreach (NavPoint p in navFwd) if (Useful(p)) { f = true; break; }
            if (nav.Back.Enabled != b) { nav.Back.Enabled = b; nav.Back.Invalidate(); }
            if (nav.Forward.Enabled != f) { nav.Forward.Enabled = f; nav.Forward.Invalidate(); }
        }

        string QuietSave(Document d)
        {
            if (d.Lossy) return "when the file was opened some of its bytes were not valid text in its encoding, and saving would replace them; save it yourself with Ctrl+S to confirm";
            if (d.Path != null && !d.DiskMissing && d.DiskTime.Ticks > 0)
            {
                try
                {
                    FileInfo fi = new FileInfo(d.Path);
                    if (fi.Exists && (fi.LastWriteTimeUtc != d.DiskTime || fi.Length != d.DiskSize))
                        return "another program changed the file on disk since it was opened, so it was not overwritten";
                }
                catch { }
            }
            byte[] bytes;
            try { bytes = Document.Encode(d.AllText(d.Eol), d.EncKind); }
            catch (EncoderFallbackException) { return "it now has characters its ANSI encoding can't store"; }
            try { WriteFile(d.Path, bytes); }
            catch (Exception ex) { return ex.Message; }
            d.RefreshDiskInfo();
            d.MarkSaved();
            return null;
        }

        Document NewUntitled()
        {
            Document d = new Document();
            for (int n = 1; ; n++)
            {
                string t = n == 1 ? "Untitled" : "Untitled " + n;
                bool used = false;
                foreach (Document o in docs) if (o.Path == null && o.Title == t) { used = true; break; }
                if (!used) { d.Title = t; break; }
            }
            return d;
        }

        void NewTab()
        {
            AddDoc(NewUntitled());
            editor.Focus();
        }

        void AddDoc(Document d)
        {
            docs.Add(d);
            sessionDirty = true;
            SelectTab(docs.Count - 1);
        }

        void SelectTab(int i)
        {
            if (i < 0 || i >= docs.Count) return;
            if (docs[i] != editor.Doc) NoteNav();
            active = i;
            tabs.Active = i;
            editor.SetDocument(docs[i]);
            tabs.EnsureVisible(i);
            tabs.Invalidate();
            UpdateTitle();
            UpdateStatus();
            if (findBar.Visible)
            {
                Pos a, b;
                editor.GetSelection(out a, out b);
                searchOrigin = a;
                RefreshFindHighlight();
                UpdateFindCount();
            }
            sessionDirty = true;
            UpdateNavButtons();
            Document d = docs[i];
            if (d.Path != null && IsHandleCreated && !closing)
                BeginInvoke(new Action(delegate { CheckDisk(d); }));
        }

        void CycleTab(int dir)
        {
            if (docs.Count < 2) return;
            SelectTab(((active + dir) % docs.Count + docs.Count) % docs.Count);
        }

        void MoveTab(int from, int to)
        {
            if (from == to || from < 0 || to < 0 || from >= docs.Count || to >= docs.Count) return;
            Document cur = CurDoc;
            Document d = docs[from];
            docs.RemoveAt(from);
            docs.Insert(to, d);
            active = docs.IndexOf(cur);
            tabs.Active = active;
            tabs.Invalidate();
            sessionDirty = true;
        }

        static bool LooksBinary(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    byte[] buf = new byte[8192];
                    int n = fs.Read(buf, 0, buf.Length);
                    if (n >= 2 && ((buf[0] == 0xFF && buf[1] == 0xFE) || (buf[0] == 0xFE && buf[1] == 0xFF))) return false;
                    int zeroEven = 0, zeroOdd = 0;
                    for (int i = 0; i < n; i++) if (buf[i] == 0) { if ((i & 1) == 0) zeroEven++; else zeroOdd++; }
                    if (zeroEven + zeroOdd == 0) return false;
                    if ((zeroOdd > n / 4 && zeroEven < n / 40) || (zeroEven > n / 4 && zeroOdd < n / 40)) return false;
                    return true;
                }
            }
            catch { return false; }
        }

        public void OpenFile(string path)
        {
            string full;
            try { full = Path.GetFullPath(path.Trim().Trim('"')); }
            catch { return; }
            for (int i = 0; i < docs.Count; i++)
                if (docs[i].Path != null && string.Equals(docs[i].Path, full, StringComparison.OrdinalIgnoreCase)) { SelectTab(i); ShowNotes(true, false); return; }
            if (Directory.Exists(full)) return;
            Document d = SharedDoc(full);
            if (d == null)
            {
                try
                {
                    if (!File.Exists(full))
                    {
                        d = new Document();
                        d.Path = full;
                        d.SetLang(Syntax.Detect(full, null), false);
                        d.SavePoint = -1;
                    }
                    else
                    {
                        if (LooksBinary(full))
                        {
                            DialogResult r = Alert.Show(this, theme, Path.GetFileName(full) + "\n\nThis looks like a binary file, not text. Open it anyway?",
                                Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                            if (r != DialogResult.Yes) return;
                        }
                        d = Document.Load(full);
                    }
                }
                catch (Exception ex)
                {
                    Alert.Show(this, theme, "Could not open \"" + full + "\".\n\n" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (d.TextLength > 8000000) d.SetLang(Syntax.Plain, false);
            }

            Document cur = CurDoc;
            if (cur != null && cur.Path == null && cur.IsEmpty && !cur.Modified && cur.Undo.Count == 0)
            {
                ForgetDoc(cur);
                docs[active] = d;
                SelectTab(active);
            }
            else AddDoc(d);
            sessionDirty = true;
            ShowNotes(true, false);
        }

        public void OpenFromOtherInstance(string[] files)
        {
            bool any = false;
            foreach (string f in files) if (f.Trim().Length > 0) { OpenFile(f); any = true; }
            Native.BringToFront(this);
            if (any && NotesShown) editor.Focus();
        }

        void OpenDialog()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Multiselect = true;
                dlg.Filter = "All files (*.*)|*.*|Text files (*.txt)|*.txt";
                dlg.Title = "Open";
                Document cur = CurDoc;
                if (cur != null && cur.Path != null) { try { dlg.InitialDirectory = Path.GetDirectoryName(cur.Path); } catch { } }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                foreach (string f in dlg.FileNames) OpenFile(f);
            }
            editor.Focus();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle h, out ByHandleInfo info);

        [StructLayout(LayoutKind.Sequential)]
        struct ByHandleInfo
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        static bool HardLinked(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    ByHandleInfo info;
                    return GetFileInformationByHandle(fs.SafeFileHandle, out info) && info.Links > 1;
                }
            }
            catch { return false; }
        }

        public static void WriteFile(string path, byte[] bytes)
        {
            if (File.Exists(path) && !HardLinked(path) && (File.GetAttributes(path) & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) == 0)
            {
                string tmp = path + ".jpsave.tmp";
                FileStream ts = null;
                try { ts = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None); }
                catch { ts = null; }
                if (ts != null)
                {
                    try
                    {
                        using (ts)
                        {
                            ts.Write(bytes, 0, bytes.Length);
                            ts.Flush(true);
                        }
                    }
                    catch
                    {
                        try { File.Delete(tmp); } catch { }
                        throw;
                    }
                    try
                    {
                        File.Replace(tmp, path, null, true);
                        return;
                    }
                    catch
                    {
                        if (!File.Exists(path) && File.Exists(tmp))
                        {
                            File.Move(tmp, path);
                            return;
                        }
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    }
                }
            }
            FileMode mode = File.Exists(path) ? FileMode.Truncate : FileMode.CreateNew;
            using (FileStream fs = new FileStream(path, mode, FileAccess.Write, FileShare.Read))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
        }

        static string SuggestedName(Document d)
        {
            string t = (d.Title ?? "Untitled").Replace('\\', '/').TrimEnd('/');
            int slash = t.LastIndexOf('/');
            if (slash >= 0) t = t.Substring(slash + 1);
            StringBuilder sb = new StringBuilder();
            foreach (char c in t) sb.Append(c < 0x20 || "<>:\"|?*".IndexOf(c) >= 0 ? '_' : c);
            string name = sb.ToString().Trim().TrimEnd('.');
            if (name.Length == 0) name = "Untitled";
            int dot = name.LastIndexOf('.');
            return dot > 0 && dot < name.Length - 1 ? name : name + ".txt";
        }

        bool SaveDoc(Document d, bool saveAs)
        {
            if (d == null) return false;
            string path = d.Path;
            if (saveAs || path == null)
            {
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Filter = "All files (*.*)|*.*|Text files (*.txt)|*.txt";
                    dlg.FileName = d.Path != null ? Path.GetFileName(d.Path) : SuggestedName(d);
                    if (d.Path != null) { try { dlg.InitialDirectory = Path.GetDirectoryName(d.Path); } catch { } }
                    dlg.OverwritePrompt = true;
                    if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                    path = dlg.FileName;
                }
            }
            if (!saveAs && d.Path != null && string.Equals(path, d.Path, StringComparison.OrdinalIgnoreCase) && !d.DiskMissing && d.DiskTime.Ticks > 0)
            {
                try
                {
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && (fi.LastWriteTimeUtc != d.DiskTime || fi.Length != d.DiskSize))
                    {
                        if (!Alert.Confirm(this, theme, path + "\n\nThis file was changed by another program since you opened it.\nSave your version over it?", Program.AppName, "Save over it", true)) return false;
                    }
                }
                catch { }
            }
            if (d.Lossy)
            {
                if (!Alert.Confirm(this, theme, "When this file was opened, some of its bytes were not valid text in its encoding and are shown as \uFFFD.\n\nSaving replaces those bytes for good. Save anyway?", Program.AppName, "Save anyway", true)) return false;
            }
            string text = d.AllText(d.Eol);
            byte[] bytes;
            try { bytes = Document.Encode(text, d.EncKind); }
            catch (EncoderFallbackException)
            {
                DialogResult r = Alert.Show(this, theme, "Some characters in this file can't be stored as ANSI.\n\nSave it as UTF-8 instead?",
                    Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return false;
                d.EncKind = 0;
                bytes = Document.Encode(text, 0);
            }
            try { WriteFile(path, bytes); }
            catch (Exception ex)
            {
                Alert.Show(this, theme, "Could not save \"" + path + "\".\n\n" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            bool pathChanged = !string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase);
            d.Lossy = false;
            d.Path = path;
            d.Title = null;
            d.RefreshDiskInfo();
            d.MarkSaved();
            if (pathChanged && !d.LangManual) d.SetLang(Syntax.Detect(path, d.Lines[0]), false);
            editor.Invalidate();
            tabs.Invalidate();
            UpdateTitle();
            UpdateStatus();
            sessionDirty = true;
            return true;
        }

        void SaveAll()
        {
            for (int i = 0; i < docs.Count; i++)
            {
                Document d = docs[i];
                if (!d.Modified) continue;
                if (d.Path == null) SelectTab(i);
                if (!SaveDoc(d, false)) break;
            }
        }

        bool ConfirmClose(Document d)
        {
            if (!d.Modified || (d.Path == null && d.IsEmpty)) return true;
            int i = docs.IndexOf(d);
            if (i >= 0 && i != active) SelectTab(i);
            DialogResult r = Alert.Choose(this, theme, "Do you want to save changes to " + d.DisplayName + "?", Program.AppName, MessageBoxIcon.None,
                new string[] { "Save", "Don't save", "Cancel" }, new DialogResult[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel }, 0, 2);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return SaveDoc(d, false);
            return true;
        }

        bool CloseTab(int i)
        {
            if (i < 0 || i >= docs.Count) return false;
            Document d = docs[i];
            Document cur = CurDoc;
            bool shared = OpenElsewhere(d, null, false);
            if (!shared && !ConfirmClose(d)) return false;
            i = docs.IndexOf(d);
            if (i < 0) return true;
            docs.RemoveAt(i);
            if (!shared)
            {
                ForgetDoc(d);
                DropBackup(d);
            }
            if (docs.Count == 0)
            {
                docs.Add(NewUntitled());
                SelectTab(0);
            }
            else if (cur == d) SelectTab(Math.Min(i, docs.Count - 1));
            else SelectTab(Math.Max(0, docs.IndexOf(cur)));
            sessionDirty = true;
            return true;
        }

        void CloseOthers(Document keep)
        {
            for (int i = docs.Count - 1; i >= 0; i--)
            {
                if (i >= docs.Count || docs[i] == keep) continue;
                if (!CloseTab(i)) break;
            }
            int k = docs.IndexOf(keep);
            if (k >= 0) SelectTab(k);
        }

        void CloseToRight(Document keep)
        {
            for (int i = docs.Count - 1; i > docs.IndexOf(keep); i--)
                if (!CloseTab(i)) break;
            int k = docs.IndexOf(keep);
            if (k >= 0) SelectTab(k);
        }

        void CheckDisk(Document d)
        {
            if (d == null || d.Path == null || checkingDisk || closing || docs.IndexOf(d) < 0) return;
            FileInfo fi;
            try { fi = new FileInfo(d.Path); fi.Refresh(); }
            catch { return; }
            if (!fi.Exists)
            {
                if (!d.DiskMissing && d.DiskTime != DateTime.MinValue && d.DiskTime.Ticks != 0)
                {
                    d.DiskMissing = true;
                    d.MissingUndoPos = d.Modified ? -1 : d.UndoPos;
                    if (!d.Modified) d.SavePoint = -1;
                    d.BackupDirty = true;
                    AfterDocMetaChange();
                }
                return;
            }
            if (d.DiskMissing)
            {
                d.DiskMissing = false;
                if (fi.LastWriteTimeUtc == d.DiskTime && fi.Length == d.DiskSize && d.MissingUndoPos >= 0 && d.UndoPos == d.MissingUndoPos)
                {
                    d.SavePoint = d.UndoPos;
                    d.MissingUndoPos = -1;
                    AfterDocMetaChange();
                    return;
                }
                d.MissingUndoPos = -1;
            }
            if (fi.LastWriteTimeUtc == d.DiskTime && fi.Length == d.DiskSize) return;
            checkingDisk = true;
            try
            {
                if (!d.Modified) ReloadDoc(d);
                else
                {
                    if (Alert.Confirm(this, theme, d.Path + "\n\nThis file was changed by another program.\nReload it? Your unsaved changes here are replaced, and Undo (Ctrl+Z) brings them back.", Program.AppName, "Reload", true)) ReloadDoc(d);
                    else { d.DiskTime = fi.LastWriteTimeUtc; d.DiskSize = fi.Length; }
                }
            }
            finally { checkingDisk = false; }
        }

        void ReloadDoc(Document d)
        {
            if (d == CurDoc) editor.SyncDocState();
            Pos caret = d.Caret;
            try { if (d.Modified) d.ReloadKeepUndo(); else d.Reload(); }
            catch (Exception ex)
            {
                try { FileInfo fi = new FileInfo(d.Path); d.DiskTime = fi.LastWriteTimeUtc; d.DiskSize = fi.Length; } catch { }
                Alert.Show(this, theme, "Could not reload \"" + d.Path + "\".\n\n" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            d.Caret = d.Anchor = d.Clamp(caret);
            if (d == CurDoc) editor.SetDocument(d);
            AfterDocMetaChange();
        }

        void OnTextEdited()
        {
            tabs.Invalidate();
            UpdateTitle();
            UpdateUndoButtons();
            if (findBar.Visible) { findTimer.Stop(); findTimer.Start(); }
        }

        void UpdateAiChip()
        {
            Document d = CurDoc;
            if (aiPanel == null) return;
            if (d == null || !NotesShown || !settings.AiSendContext) { aiPanel.SetContextLabel(null); return; }
            int from = 0, to = 0;
            if (d.Caret != d.Anchor)
            {
                Pos a = Pos.Min(d.Caret, d.Anchor), b = Pos.Max(d.Caret, d.Anchor);
                from = a.Line + 1;
                to = b.Col == 0 && b.Line > a.Line ? b.Line : b.Line + 1;
            }
            if (from == 0 && d.Path == null && d.IsEmpty) { aiPanel.SetContextLabel(null); return; }
            aiPanel.SetContextLabel(from == 0 ? d.DisplayName : d.DisplayName + " \u00b7 " + (from == to ? "line " + from : "lines " + from + "-" + to));
        }

        void OnCaretMoved()
        {
            UpdateStatus();
            UpdateAiChip();
            if (findBar.Visible) { findTimer.Stop(); findTimer.Start(); }
        }

        void UpdateTitle()
        {
            Document d = CurDoc;
            string t;
            if (NotesShown && d != null) t = (d.Modified ? "*" : "") + d.DisplayName + " - " + Program.AppName;
            else
            {
                string chatTitle = aiPanel != null ? aiPanel.ChatTitle : "";
                t = chatTitle.Length > 0 ? chatTitle + " - " + Program.AppName : Program.AppName;
            }
            if (Text != t) Text = t;
            UpdateAiChip();
        }

        void UpdateStatus()
        {
            Document d = CurDoc;
            if (d == null || stPos == null) return;
            Pos c = d.Caret;
            string pos = "Ln " + (c.Line + 1).ToString("N0") + ", Col " + (c.Col + 1);
            if (d.Caret != d.Anchor) pos += "    " + d.CountChars(d.Caret, d.Anchor).ToString("N0") + " selected";
            stPos.Text = pos;
            stLang.Text = d.Lang.Name;
            stEnc.Text = Document.EncNames[Math.Max(0, Math.Min(4, d.EncKind))];
            stEol.Text = d.Eol == "\n" ? "Unix (LF)" : d.Eol == "\r" ? "Macintosh (CR)" : "Windows (CRLF)";
            stZoom.Text = ZoomPercent + "%";
            UpdateUndoButtons();
        }

        void ShowFind(bool replace)
        {
            string seed = editor.FindSeed();
            findBar.ReplaceMode = replace;
            if (!findBar.Visible) findBar.Visible = true;
            Pos a, b;
            editor.GetSelection(out a, out b);
            searchOrigin = a;
            if (!string.IsNullOrEmpty(seed) && seed.IndexOf('\n') < 0) findBar.SetTerm(seed);
            findBar.FocusFind();
            RefreshFindHighlight();
            UpdateFindCount();
        }

        void HideFind()
        {
            findBar.Visible = false;
            editor.HiTerm = null;
            editor.FindLines = null;
            editor.Invalidate();
            editor.Focus();
        }

        void RefreshFindHighlight()
        {
            editor.HiTerm = findBar.Visible && findBar.Term.Length > 0 ? findBar.Term : null;
            editor.HiCase = findBar.MatchCase;
            editor.HiWord = findBar.WholeWord;
            editor.Invalidate();
        }

        void OnFindTermChanged()
        {
            RefreshFindHighlight();
            Document d = CurDoc;
            string term = findBar.Term;
            if (d != null && term.Length > 0 && findBar.Visible)
            {
                Pos a, b;
                bool wrapped;
                if (Finder.Find(d, term, findBar.MatchCase, findBar.WholeWord, searchOrigin, false, out a, out b, out wrapped))
                    editor.Select(a, b);
            }
            UpdateFindCount();
        }

        void FindNext(bool back)
        {
            Document d = CurDoc;
            if (d == null) return;
            string term = findBar.Term;
            if (term.Length == 0) { ShowFind(false); return; }
            Pos s0, s1;
            editor.GetSelection(out s0, out s1);
            Pos a, b;
            bool wrapped;
            if (Finder.Find(d, term, findBar.MatchCase, findBar.WholeWord, back ? s0 : s1, back, out a, out b, out wrapped))
            {
                editor.Select(a, b);
                searchOrigin = a;
                RefreshFindHighlight();
                UpdateFindCount();
                if (wrapped && findBar.Visible) findBar.SetInfo(CountText() + (back ? "   (wrapped to the end)" : "   (wrapped to the top)"), false);
            }
            else
            {
                if (!findBar.Visible) ShowFind(false);
                findBar.SetInfo("No results", true);
            }
        }

        string lastCountText = "";

        string CountText() { return lastCountText; }

        Document fcDoc;
        long fcVersion = -1;
        string fcTerm;
        bool fcMC, fcWW;
        List<int> fcLines = new List<int>();
        List<int> fcPrefix = new List<int>();
        int fcTotal;

        void RecountMatches(Document d, string term, bool mc, bool ww)
        {
            List<int> lines = new List<int>(), prefix = new List<int>(), hits = new List<int>();
            int total = 0;
            for (int l = 0; l < d.Lines.Count; l++)
            {
                hits.Clear();
                Finder.AllIn(d.Lines[l], term, mc, ww, hits);
                if (hits.Count == 0) continue;
                prefix.Add(total);
                lines.Add(l);
                total += hits.Count;
            }
            prefix.Add(total);
            fcLines = lines;
            fcPrefix = prefix;
            fcTotal = total;
            fcDoc = d; fcVersion = d.Version; fcTerm = term; fcMC = mc; fcWW = ww;
            editor.FindLines = lines;
        }

        void UpdateFindCount()
        {
            Document d = CurDoc;
            if (d == null || !findBar.Visible) return;
            string term = findBar.Term;
            if (term.Length == 0)
            {
                lastCountText = "";
                findBar.SetInfo("", false);
                editor.FindLines = null;
                fcDoc = null;
                editor.Invalidate();
                return;
            }
            bool mc = findBar.MatchCase, ww = findBar.WholeWord;
            if (fcDoc != d || fcVersion != d.Version || fcTerm != term || fcMC != mc || fcWW != ww)
                RecountMatches(d, term, mc, ww);
            editor.FindLines = fcLines;
            if (fcTotal == 0)
            {
                lastCountText = "No results";
                findBar.SetInfo(lastCountText, true);
                editor.Invalidate();
                return;
            }
            Pos s0, s1;
            editor.GetSelection(out s0, out s1);
            int lo = 0, hi = fcLines.Count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (fcLines[mid] < s0.Line) lo = mid + 1; else hi = mid; }
            int before = fcPrefix[lo];
            if (lo < fcLines.Count && fcLines[lo] == s0.Line)
            {
                List<int> hits = new List<int>();
                Finder.AllIn(d.Lines[s0.Line], term, mc, ww, hits);
                foreach (int h in hits) if (h < s0.Col) before++;
            }
            bool onMatch = s0.Line == s1.Line && s1.Col - s0.Col == term.Length && Finder.IsMatch(d.Lines[s0.Line], s0.Col, term, mc, ww);
            lastCountText = onMatch ? (before + 1).ToString("N0") + " of " + fcTotal.ToString("N0")
                                    : fcTotal.ToString("N0") + (fcTotal == 1 ? " match" : " matches");
            findBar.SetInfo(lastCountText, false);
            editor.Invalidate();
        }

        void ReplaceOne()
        {
            string term = findBar.Term;
            if (CurDoc == null || term.Length == 0) return;
            editor.ReplaceIfMatch(term, findBar.Replacement, findBar.MatchCase, findBar.WholeWord);
            FindNext(false);
        }

        void ReplaceAll()
        {
            string term = findBar.Term;
            if (CurDoc == null || term.Length == 0) return;
            int n = editor.ReplaceAll(term, findBar.Replacement, findBar.MatchCase, findBar.WholeWord);
            UpdateFindCount();
            findTimer.Stop();
            findBar.SetInfo(n == 0 ? "No results" : "Replaced " + n.ToString("N0"), n == 0);
        }

        void GoToLine()
        {
            Document d = CurDoc;
            if (d == null) return;
            NoteNav();
            using (GoToDialog dlg = new GoToDialog(theme, d.Caret.Line + 1, d.Lines.Count))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) editor.GoToLine(dlg.Line - 1);
            }
            editor.Focus();
        }

        static string[] ReadLinesRetry(string path)
        {
            for (int i = 0; ; i++)
            {
                try { return File.ReadAllLines(path, Encoding.UTF8); }
                catch (FileNotFoundException) { return null; }
                catch (DirectoryNotFoundException) { return null; }
                catch (IOException) { if (i >= 9) throw; System.Threading.Thread.Sleep(150); }
            }
        }

        void RestoreSession()
        {
            string[] lines = null;
            bool exists = false;
            try { exists = File.Exists(SessionPath); if (exists) lines = ReadLinesRetry(SessionPath); }
            catch { lines = null; }
            if (exists && (lines == null || lines.Length == 0 || !(lines[0].StartsWith("MinGent session") || lines[0].StartsWith("JustPad session"))))
            {
                try { File.Copy(SessionPath, SessionPath + ".broken", true); } catch { }
                lines = null;
            }
            if (lines != null) RestoreTabs(lines);
            RecoverOrphans();
        }

        void RecoverOrphans()
        {
            string[] files;
            try { files = Directory.Exists(BackupDir) ? Directory.GetFiles(BackupDir, "*.txt") : new string[0]; }
            catch { return; }
            List<FileInfo> orphans = new List<FileInfo>();
            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                if (ownedBackups.Contains(name) || pendingErase.Contains(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9a-f]{32}\\.txt$")) continue;
                try { orphans.Add(new FileInfo(f)); } catch { }
            }
            orphans.Sort(delegate (FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
            int n = 0;
            foreach (FileInfo fi in orphans)
            {
                if (n >= 20) break;
                try
                {
                    string text = new UTF8Encoding(false).GetString(File.ReadAllBytes(fi.FullName));
                    if (text.Length == 0) { SecureFile.Erase(fi.FullName); continue; }
                    Document d = new Document();
                    d.SetText(text, false);
                    d.Eol = "\r\n";
                    d.Title = n == 0 ? "Recovered" : "Recovered " + (n + 1);
                    d.BackupFile = fi.Name;
                    d.BackupDirty = false;
                    d.SavePoint = -1;
                    ownedBackups.Add(fi.Name);
                    recovered.Add(d);
                    n++;
                }
                catch { }
            }
            if (n > 0) sessionDirty = true;
        }

        void RestoreTabs(string[] lines)
        {
            int act = 0, groupAct = -1, groupPos = 0;
            NotesGroup group = null;
            Dictionary<string, Document> byPath = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Document> byBackup = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
            for (int li = 1; li < lines.Length; li++)
            {
                string[] f = lines[li].Split('\t');
                if (f[0] == "active" && f.Length > 1) { int.TryParse(f[1], out act); continue; }
                if (f[0] == "erase" && f.Length > 1)
                {
                    if (f[1].Length > 0 && f[1].IndexOfAny(Path.GetInvalidFileNameChars()) < 0) pendingErase.Add(f[1]);
                    continue;
                }
                if (f[0] == "group" && f.Length > 2)
                {
                    group = new NotesGroup();
                    restoredGroups.Add(new KeyValuePair<string, NotesGroup>(f[1], group));
                    if (!int.TryParse(f[2], out groupAct)) groupAct = -1;
                    groupPos = 0;
                    continue;
                }
                if (f[0] != "tab" || f.Length < 13) continue;
                if (group == null)
                {
                    group = new NotesGroup();
                    restoredGroups.Add(new KeyValuePair<string, NotesGroup>("", group));
                    groupAct = act;
                    groupPos = 0;
                }
                int pos = groupPos++;
                try
                {
                    Document d = null;
                    string pathKey = f[1].Length > 0 ? FullOrNull(f[1]) : null;
                    if (pathKey != null) byPath.TryGetValue(pathKey, out d);
                    if (d == null && f[2].Length > 0) byBackup.TryGetValue(f[2], out d);
                    if (d == null)
                    {
                        d = RestoreTab(f);
                        if (d == null) continue;
                        if (pathKey != null) byPath[pathKey] = d;
                        if (f[2].Length > 0) byBackup[f[2]] = d;
                    }
                    if (!group.Docs.Contains(d)) group.Docs.Add(d);
                    if (pos == groupAct) group.Active = d;
                }
                catch { }
            }
        }

        static bool Unsaved(Document d) { return d.Modified || (d.Path == null && !d.IsEmpty); }

        static bool Placeholder(Document d) { return d.Path == null && d.IsEmpty && !d.Modified && d.Undo.Count == 0; }

        bool OpenElsewhere(Document d, NotesGroup except, bool visible)
        {
            if (visible && docs.Contains(d)) return true;
            foreach (NotesGroup g in parked.Values) if (g != except && g.Docs.Contains(d)) return true;
            return false;
        }

        Document SharedDoc(string full)
        {
            foreach (NotesGroup g in parked.Values)
                foreach (Document d in g.Docs)
                    if (d.Path != null && string.Equals(FullOrNull(d.Path), full, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        void StartNotes()
        {
            notesKey = aiPanel != null ? aiPanel.NotesKey : "~new";
            notesConv = aiPanel != null ? aiPanel.Conversation : 0;
            NotesGroup mine = new NotesGroup();
            foreach (KeyValuePair<string, NotesGroup> kv in restoredGroups)
            {
                string key = kv.Key;
                NotesGroup g = kv.Value;
                if (string.Equals(key, notesKey, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (Document d in g.Docs) if (!mine.Docs.Contains(d)) mine.Docs.Add(d);
                    if (mine.Active == null) mine.Active = g.Active;
                    continue;
                }
                bool legacy = key.Length == 0;
                bool alive = key == "~new" || (!legacy && !key.StartsWith("~") && aiPanel != null && aiPanel.HasChat(key));
                if (alive)
                {
                    NotesGroup have;
                    if (parked.TryGetValue(key, out have)) { foreach (Document d in g.Docs) if (!have.Docs.Contains(d)) have.Docs.Add(d); }
                    else parked[key] = g;
                    continue;
                }
                foreach (Document d in g.Docs)
                    if ((legacy || Unsaved(d)) && !mine.Docs.Contains(d)) mine.Docs.Add(d);
                if (legacy && mine.Active == null) mine.Active = g.Active;
            }
            restoredGroups.Clear();
            foreach (Document d in recovered) if (!mine.Docs.Contains(d)) mine.Docs.Add(d);
            recovered.Clear();
            docs.Clear();
            docs.AddRange(mine.Docs);
            active = -1;
            if (docs.Count > 0) SelectTab(Math.Max(0, mine.Active != null ? docs.IndexOf(mine.Active) : 0));
            notesStarted = true;
        }

        void SyncNotesChat()
        {
            if (!notesStarted || aiPanel == null || closing) return;
            if (syncingNotes) { notesAgain = true; return; }
            syncingNotes = true;
            try
            {
                do
                {
                    notesAgain = false;
                    string key = aiPanel.NotesKey;
                    int conv = aiPanel.Conversation;
                    if (conv != notesConv) SwitchNotes(key, conv);
                    else if (!string.Equals(key, notesKey, StringComparison.OrdinalIgnoreCase)) RenameNotes(key);
                    foreach (string gone in aiPanel.TakeDeletedChats()) DropNotes(gone);
                }
                while (notesAgain);
            }
            finally { syncingNotes = false; }
        }

        void RenameNotes(string key)
        {
            NotesGroup clash;
            if (parked.TryGetValue(key, out clash))
            {
                parked.Remove(key);
                foreach (Document d in clash.Docs) if (!docs.Contains(d)) docs.Add(d);
                tabs.Invalidate();
            }
            notesKey = key;
            sessionDirty = true;
        }

        void SwitchNotes(string key, int conv)
        {
            editor.SyncDocState();
            List<Document> carry = new List<Document>();
            if (notesKey.StartsWith("~temp:"))
            {
                List<Document> asked = new List<Document>();
                foreach (Document d in docs) if (Unsaved(d) && !OpenElsewhere(d, null, false)) asked.Add(d);
                bool keep = false;
                if (asked.Count > 0)
                {
                    List<string> names = new List<string>();
                    foreach (Document d in asked) names.Add(d.DisplayName);
                    DialogResult r = Alert.Choose(this, theme, "Keep the notes from the temporary chat?\n\n" + AiPanel.NoteNames(names) + (asked.Count == 1 ? " has" : " have") + " changes that aren't saved. Keep them in your notes, or discard them with the chat.",
                        Program.AppName, MessageBoxIcon.Question, new string[] { "Keep", "Discard" }, new DialogResult[] { DialogResult.Yes, DialogResult.No }, 0, 0);
                    keep = r != DialogResult.No;
                    editor.SyncDocState();
                }
                foreach (Document d in new List<Document>(docs))
                {
                    if (OpenElsewhere(d, null, false)) continue;
                    if (Unsaved(d) && (keep || !asked.Contains(d))) { carry.Add(d); continue; }
                    ForgetDoc(d);
                    DropBackup(d);
                }
                parked.Remove(notesKey);
            }
            else
            {
                Document cur = CurDoc;
                List<Document> leaving = new List<Document>(docs);
                NotesGroup g = new NotesGroup();
                foreach (Document d in leaving)
                {
                    if (Placeholder(d) && !OpenElsewhere(d, null, false)) { ForgetDoc(d); continue; }
                    g.Docs.Add(d);
                }
                g.Active = cur != null && g.Docs.Contains(cur) ? cur : null;
                if (g.Docs.Count > 0) parked[notesKey] = g;
                else parked.Remove(notesKey);
            }
            NotesGroup next;
            if (parked.TryGetValue(key, out next)) parked.Remove(key);
            docs.Clear();
            if (next != null) docs.AddRange(next.Docs);
            foreach (Document d in carry) if (!docs.Contains(d)) docs.Add(d);
            notesKey = key;
            notesConv = conv;
            active = -1;
            if (docs.Count == 0) docs.Add(NewUntitled());
            int sel = carry.Count > 0 ? docs.IndexOf(carry[0]) : next != null && next.Active != null ? docs.IndexOf(next.Active) : 0;
            SelectTab(Math.Max(0, sel));
            tabs.Invalidate();
            sessionDirty = true;
            UpdateAiChip();
            aiPanel.WorkspaceChanged();
        }

        void DropNotes(string key)
        {
            List<string> keys = new List<string>();
            if (parked.ContainsKey(key)) keys.Add(key);
            foreach (string k in keys)
            {
                NotesGroup g = parked[k];
                parked.Remove(k);
                foreach (Document d in g.Docs)
                {
                    if (OpenElsewhere(d, null, true)) continue;
                    ForgetDoc(d);
                    DropBackup(d);
                }
            }
            if (keys.Count > 0) sessionDirty = true;
        }

        public List<string> UnsavedNotes(string chatKey)
        {
            List<string> names = new List<string>();
            List<KeyValuePair<string, NotesGroup>> all = NotesGroups();
            foreach (KeyValuePair<string, NotesGroup> kv in all)
            {
                bool match = chatKey == null ? !kv.Key.StartsWith("~") : string.Equals(kv.Key, chatKey, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
                foreach (Document d in kv.Value.Docs)
                {
                    if (!Unsaved(d) || names.Contains(d.DisplayName)) continue;
                    bool kept = false;
                    foreach (KeyValuePair<string, NotesGroup> o in all)
                    {
                        if (o.Value == kv.Value || !o.Value.Docs.Contains(d)) continue;
                        if (chatKey == null ? o.Key.StartsWith("~") : true) { kept = true; break; }
                    }
                    if (!kept) names.Add(d.DisplayName);
                }
            }
            return names;
        }

        List<KeyValuePair<string, NotesGroup>> NotesGroups()
        {
            List<KeyValuePair<string, NotesGroup>> all = new List<KeyValuePair<string, NotesGroup>>();
            NotesGroup vis = new NotesGroup();
            vis.Docs.AddRange(docs);
            vis.Active = CurDoc;
            all.Add(new KeyValuePair<string, NotesGroup>(notesKey, vis));
            foreach (KeyValuePair<string, NotesGroup> kv in parked) all.Add(kv);
            return all;
        }

        Document RestoreTab(string[] f)
        {
            string path = f[1].Length > 0 ? f[1] : null;
            string backup = f[2].Length > 0 ? f[2] : null;
            bool modified = f[3] == "1";
            int cl, cc, top, enc;
            long ticks, size;
            int.TryParse(f[4], out cl);
            int.TryParse(f[5], out cc);
            int.TryParse(f[6], out top);
            string langName = f[7];
            int.TryParse(f[8], out enc);
            string eol = f[9] == "LF" ? "\n" : f[9] == "CR" ? "\r" : "\r\n";
            string title = f[10];
            long.TryParse(f[11], out ticks);
            long.TryParse(f[12], out size);
            bool lossy = f.Length > 13 && f[13] == "1";

            Document d = null;
            if (backup != null && backup.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            {
                string bp = Path.Combine(BackupDir, backup);
                if (path == null && !File.Exists(bp)) return null;
                if (File.Exists(bp))
                {
                    d = new Document();
                    d.SetText(new UTF8Encoding(false).GetString(File.ReadAllBytes(bp)), false);
                    d.Path = path;
                    d.EncKind = Math.Max(0, Math.Min(5, enc));
                    d.Eol = eol;
                    d.BackupFile = backup;
                    d.BackupDirty = false;
                    d.Lossy = lossy;
                    ownedBackups.Add(backup);
                    if (modified || path == null) d.SavePoint = -1;
                    if (ticks > 0 && ticks < DateTime.MaxValue.Ticks) d.DiskTime = new DateTime(ticks, DateTimeKind.Utc);
                    d.DiskSize = size;
                    d.SetLang(Syntax.Detect(path ?? (title.Length > 0 ? title : null), d.Lines[0]), false);
                }
            }
            if (d == null && path != null)
            {
                if (!File.Exists(path)) return null;
                d = Document.Load(path);
                if (d.TextLength > 8000000) d.SetLang(Syntax.Plain, false);
            }
            if (d == null) d = new Document();
            if (d.Path == null) d.Title = title.Length > 0 ? title : "Untitled";
            if (langName.Length > 0)
            {
                Lang l = Syntax.ByName(langName);
                if (l != null) d.SetLang(l, true);
            }
            d.Caret = d.Anchor = d.Clamp(new Pos(cl, cc));
            d.TopLine = Math.Max(0, Math.Min(top, d.Lines.Count - 1));
            return d;
        }

        static void WriteAtomic(string path, byte[] data)
        {
            string tmp = path + ".tmp";
            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null, true); return; }
                catch { }
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else File.Move(tmp, path);
        }

        void StoreBackup(Document d, bool force, DateTime now, UTF8Encoding utf8, HashSet<string> keep)
        {
            bool need = d.Modified || (d.Path == null && !d.IsEmpty);
            if (!need)
            {
                d.BackupFile = null;
                d.BackupDirty = false;
                return;
            }
            if (d.BackupFile == null) { d.BackupFile = Guid.NewGuid().ToString("N") + ".txt"; d.BackupDirty = true; }
            string bp = Path.Combine(BackupDir, d.BackupFile);
            bool big = d.TextLength > 4000000;
            if ((d.BackupDirty || !File.Exists(bp)) && (force || !big || (now - d.LastBackup).TotalSeconds > 30))
            {
                WriteAtomic(bp, utf8.GetBytes(d.AllText("\n")));
                d.BackupDirty = false;
                d.LastBackup = now;
                ownedBackups.Add(d.BackupFile);
            }
            keep.Add(d.BackupFile);
        }

        public bool SaveSession(bool force)
        {
            if (!primary || Program.Erased) return true;
            try
            {
                editor.SyncDocState();
                Directory.CreateDirectory(BackupDir);
                StringBuilder sb = new StringBuilder();
                sb.Append("MinGent session 1\r\n");
                sb.Append("active\t").Append(active).Append("\r\n");
                HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                DateTime now = DateTime.UtcNow;
                UTF8Encoding utf8 = new UTF8Encoding(false);
                HashSet<Document> stored = new HashSet<Document>();
                foreach (KeyValuePair<string, NotesGroup> group in NotesGroups())
                {
                    if (group.Value.Docs.Count == 0) continue;
                    sb.Append("group\t").Append(group.Key.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '))
                      .Append('\t').Append(group.Value.Active != null ? group.Value.Docs.IndexOf(group.Value.Active) : -1).Append("\r\n");
                    foreach (Document d in group.Value.Docs.ToArray())
                    {
                        if (stored.Add(d)) StoreBackup(d, force, now, utf8, keep);
                        else if (d.BackupFile != null) keep.Add(d.BackupFile);
                        sb.Append("tab\t").Append(d.Path ?? "")
                          .Append('\t').Append(d.BackupFile ?? "")
                          .Append('\t').Append(d.Modified ? "1" : "0")
                          .Append('\t').Append(d.Caret.Line)
                          .Append('\t').Append(d.Caret.Col)
                          .Append('\t').Append(d.TopLine)
                          .Append('\t').Append(d.LangManual ? d.Lang.Name : "")
                          .Append('\t').Append(d.EncKind)
                          .Append('\t').Append(d.Eol == "\n" ? "LF" : d.Eol == "\r" ? "CR" : "CRLF")
                          .Append('\t').Append(d.Path == null ? (d.Title ?? "") : "")
                          .Append('\t').Append(d.DiskTime.Ticks)
                          .Append('\t').Append(d.DiskSize)
                          .Append('\t').Append(d.Lossy ? "1" : "0")
                          .Append("\r\n");
                    }
                }
                foreach (string file in Directory.GetFiles(BackupDir))
                {
                    string name = Path.GetFileName(file);
                    if (keep.Contains(name)) continue;
                    if (ownedBackups.Contains(name) || pendingErase.Contains(name)) EraseBackup(name);
                    else if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        try { if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalDays > 1) SecureFile.Erase(file); } catch { }
                    }
                }
                foreach (string name in pendingErase) sb.Append("erase\t").Append(name).Append("\r\n");
                Directory.CreateDirectory(Path.GetDirectoryName(SessionPath));
                WriteAtomic(SessionPath, utf8.GetBytes(sb.ToString()));
                sessionDirty = false;
                lastSessionError = null;
                return true;
            }
            catch (Exception ex)
            {
                lastSessionError = ex.Message;
                return false;
            }
        }

        void AutoSave()
        {
            if (!primary || closing) return;
            bool any = sessionDirty;
            if (!any) foreach (Document d in docs) if (d.BackupDirty) { any = true; break; }
            if (any) SaveSession(false);
        }

        public void EmergencySave()
        {
            try { SaveSession(true); } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AppHighlight.Init(this);
            AppHighlight.SetColors(theme.Accent, theme.OnAccent);
            Native.SetTitleBar(this, theme);
            if (primary && ipc == null)
            {
                try { ipc = new IpcWindow(this); } catch { ipc = null; }
            }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (browser != null) browser.ParentMoved();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            editor.EnsureCaretVisible();
            editor.Invalidate();
            if ((openedFiles || !shell.AgentOn) && NotesShown) editor.Focus();
            else aiPanel.FocusInput();
            EngineUpdates.CheckSoon();
            if (Program.Elevated && primary)
                BeginInvoke(new Action(delegate
                {
                    Alert.Show(this, theme, "MinGent is running as administrator, so the commands the agent runs have administrator rights too.\n\nUnless you need that, close MinGent and start it normally.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }));
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (closing) return;
            if (theme != null && !theme.HighContrast && settings.ThemeName == "system" && Theme.SystemUsesDark() != theme.Dark) ApplyTheme();
            if (aiPanel != null) aiPanel.RefreshConnection(false);
            Document d = CurDoc;
            if (d != null && d.Path != null) BeginInvoke(new Action(delegate { CheckDisk(d); }));
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0016 && m.WParam == IntPtr.Zero && closing && !IsDisposed)
            {
                closing = false;
                sessionTimer.Start();
                if (aiPanel != null) aiPanel.Resume();
                return;
            }
            if (m.Msg == Native.WM_SETTINGCHANGE)
            {
                string area = null;
                try { if (m.LParam != IntPtr.Zero) area = Marshal.PtrToStringUni(m.LParam); } catch { }
                if (area == "ImmersiveColorSet" || area == "WindowsThemeElement") ScheduleThemeRefresh();
            }
            else if (m.Msg == Native.WM_DWMCOLORIZATIONCOLORCHANGED || m.Msg == Native.WM_SYSCOLORCHANGE || m.Msg == Native.WM_THEMECHANGED)
            {
                ScheduleThemeRefresh();
            }
        }

        bool BrowserShortcut(Keys k)
        {
            Action act = null;
            switch (k)
            {
                case Keys.Control | Keys.S: act = delegate { SaveDoc(CurDoc, false); }; break;
                case Keys.Control | Keys.Shift | Keys.S: act = delegate { SaveDoc(CurDoc, true); }; break;
                case Keys.Control | Keys.O: act = delegate { OpenDialog(); }; break;
                case Keys.Control | Keys.N:
                case Keys.Control | Keys.T: act = delegate { ShowNotes(true, false); NewTab(); }; break;
                case Keys.Control | Keys.Tab:
                case Keys.Control | Keys.PageDown: act = delegate { CycleTab(1); }; break;
                case Keys.Control | Keys.Shift | Keys.Tab:
                case Keys.Control | Keys.PageUp: act = delegate { CycleTab(-1); }; break;
                case Keys.Control | Keys.Shift | Keys.A: act = delegate { ShowAgent(true, true); }; break;
                case Keys.Control | Keys.Shift | Keys.B: act = delegate { ShowBrowser(!shell.BrowserOn, true); }; break;
                case Keys.Control | Keys.Shift | Keys.E: act = delegate { ShowNotes(!shell.NotesOn, true); }; break;
                case Keys.Control | Keys.Shift | Keys.N: act = delegate { aiPanel.NewChat(); }; break;
                case Keys.Control | Keys.Oemcomma: act = delegate { ShowSettings(); }; break;
            }
            if (act == null) return false;
            BeginInvoke(act);
            return true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool inChat = aiPanel != null && aiPanel.ContainsFocus;
            bool inNotes = NotesShown && contentPanel.ContainsFocus;
            switch (keyData)
            {
                case Keys.Control | Keys.N:
                case Keys.Control | Keys.T:
                    ShowNotes(true, false); NewTab(); return true;
                case Keys.Control | Keys.O:
                    OpenDialog(); return true;
                case Keys.Control | Keys.S:
                    if (CurDoc != null && (NotesShown || CurDoc.Modified)) { SaveDoc(CurDoc, false); return true; }
                    break;
                case Keys.Control | Keys.Shift | Keys.S:
                    if (CurDoc != null) { ShowNotes(true, false); SaveDoc(CurDoc, true); return true; }
                    break;
                case Keys.Control | Keys.W:
                case Keys.Control | Keys.F4:
                    if (inNotes || (NotesShown && !inChat)) { CloseTab(active); return true; }
                    break;
                case Keys.Control | Keys.F:
                    if (NotesShown && !inChat) { ShowFind(false); return true; }
                    break;
                case Keys.Control | Keys.H:
                    if (NotesShown && !inChat) { ShowFind(true); return true; }
                    break;
                case Keys.Control | Keys.G:
                    if (NotesShown && !inChat) { GoToLine(); return true; }
                    break;
                case Keys.F3:
                    if (NotesShown) { FindNext(false); return true; }
                    break;
                case Keys.Shift | Keys.F3:
                    if (NotesShown) { FindNext(true); return true; }
                    break;
                case Keys.Alt | Keys.Z:
                    if (NotesShown) { ToggleWrap(); return true; }
                    break;
                case Keys.Control | Keys.Tab:
                case Keys.Control | Keys.PageDown:
                    if (NotesShown) { CycleTab(1); return true; }
                    break;
                case Keys.Control | Keys.Shift | Keys.Tab:
                case Keys.Control | Keys.PageUp:
                    if (NotesShown) { CycleTab(-1); return true; }
                    break;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Shift | Keys.Oemplus:
                case Keys.Control | Keys.Add:
                    if (inChat) aiPanel.ZoomChat(1); else Zoom(1);
                    return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract:
                    if (inChat) aiPanel.ZoomChat(-1); else Zoom(-1);
                    return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0:
                    if (inChat) aiPanel.ZoomChat(0); else ZoomReset();
                    return true;
                case Keys.Control | Keys.Shift | Keys.A:
                    ShowAgent(true, true); return true;
                case Keys.Control | Keys.Shift | Keys.B:
                    ShowBrowser(!shell.BrowserOn, true); return true;
                case Keys.Control | Keys.Shift | Keys.E:
                    ShowNotes(!shell.NotesOn, true); return true;
                case Keys.Control | Keys.Shift | Keys.N:
                    aiPanel.NewChat(); return true;
                case Keys.Control | Keys.Oemcomma:
                    ShowSettings(); return true;
                case Keys.Escape:
                    if (aiPanel != null && aiPanel.HandleKey(keyData)) return true;
                    if (inChat) break;
                    if (browser != null && browser.ContainsFocus) break;
                    if (findBar.Visible) { HideFind(); return true; }
                    break;
                case Keys.Alt | Keys.Left:
                    if (NotesShown && !inChat) { NavGo(true); return true; }
                    break;
                case Keys.Alt | Keys.Right:
                    if (NotesShown && !inChat) { NavGo(false); return true; }
                    break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void OnFileDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnFileDragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (string f in files) OpenFile(f);
            Activate();
        }

        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); OnFileDragEnter(this, e); }
        protected override void OnDragDrop(DragEventArgs e) { base.OnDragDrop(e); OnFileDragDrop(this, e); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            if (Program.Erased) return;
            int downloading = ModelDownloads.Active().Count;
            if (downloading > 0 && e.CloseReason == CloseReason.UserClosing &&
                !Alert.Confirm(this, theme, (downloading == 1 ? "A model is still downloading." : downloading + " models are still downloading.") + " Close MinGent anyway?\n\nA download that stops picks up where it left off when you start it again.", Program.AppName, "Close anyway", false)) { e.Cancel = true; return; }
            if (primary)
            {
                if (!SaveSession(true) && e.CloseReason == CloseReason.UserClosing)
                {
                    if (!Alert.Confirm(this, theme, "MinGent could not store your open tabs:\n" + lastSessionError + "\n\nClose anyway? Unsaved changes may be lost.", Program.AppName, "Close anyway", true)) { e.Cancel = true; return; }
                }
            }
            else
            {
                foreach (Document d in docs.ToArray())
                {
                    if (!ConfirmClose(d)) { e.Cancel = true; return; }
                }
                foreach (NotesGroup g in new List<NotesGroup>(parked.Values))
                    foreach (Document d in g.Docs.ToArray())
                    {
                        if (docs.Contains(d)) continue;
                        if (!ConfirmClose(d)) { e.Cancel = true; return; }
                    }
            }
            closing = true;
            sessionTimer.Stop();
            settings.Wrap = editor.WordWrap;
            settings.MatchCase = findBar.MatchCase;
            settings.WholeWord = findBar.WholeWord;
            settings.Maximized = WindowState == FormWindowState.Maximized;
            Rectangle rb = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (rb.Width > 0 && rb.Height > 0) settings.Bounds = rb;
            if (aiPanel != null) { aiPanel.Shutdown(); aiPanel.SaveChat(); }
            settings.NotesVisible = shell.NotesOn;
            settings.AgentVisible = shell.AgentOn;
            settings.SidebarCollapsed = shell.LeftCollapsed;
            settings.NotesWidth = (int)Math.Round(shell.NotesWidth / UI.Scale);
            settings.BrowserWidth = (int)Math.Round(shell.BrowserWidth / UI.Scale);
            settings.BrowserVisible = false;
            settings.Save();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { Hide(); } catch { }
            AppHighlight.Close();
            if (library != null && !library.IsDisposed) { try { library.Hide(); } catch { } }
            Shell.KillAll();
            try { Engine.Instance.Stop(); } catch { }
            if (browser != null) browser.Shutdown();
            try { ModelCatalog.UnloadOurLmStudioModels(3000); } catch { }
            if (ipc != null) { try { ipc.DestroyHandle(); } catch { } ipc = null; }
        }
    }
}
