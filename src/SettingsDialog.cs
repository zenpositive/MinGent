using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MinGent
{
    class ThemedForm : Form
    {
        protected Theme Th;
        public const int WindowW = 900, WindowH = 640;

        public ThemedForm(Theme t, string title, int w, int h)
        {
            Th = t ?? Theme.Light(Color.SteelBlue);
            Text = title;
            Font = UI.Font;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Th.Chrome;
            ForeColor = Th.ChromeFore;
            ClientSize = new Size(UI.S(w), UI.S(h));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            try { using (System.IO.Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("MinGent.ico")) if (s != null) Icon = new Icon(s); } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetTitleBar(this, Th);
        }

        public void Retheme(Theme t)
        {
            if (t == null || t == Th) return;
            Theme old = Th;
            Th = t;
            SuspendLayout();
            BackColor = t.Chrome;
            ForeColor = t.ChromeFore;
            Recolor(this, old, t);
            ResumeLayout();
            if (IsHandleCreated) Native.SetTitleBar(this, t);
            ThemeScrollbars(this);
            Invalidate(true);
        }

        static void Recolor(Control root, Theme old, Theme t)
        {
            foreach (Control c in root.Controls)
            {
                IThemed themed = c as IThemed;
                if (themed != null) themed.SetTheme(t);
                else if (c is Label)
                {
                    int fc = c.ForeColor.ToArgb();
                    if (fc == old.ChromeDim.ToArgb()) c.ForeColor = t.ChromeDim;
                    else if (fc == old.Fore.ToArgb()) c.ForeColor = t.Fore;
                    else if (fc == old.ChromeFore.ToArgb()) c.ForeColor = t.ChromeFore;
                }
                else if (!(c is TextBoxBase))
                {
                    int bc = c.BackColor.ToArgb();
                    if (bc == old.Chrome.ToArgb()) c.BackColor = t.Chrome;
                    else if (bc == old.Card.ToArgb()) c.BackColor = t.Card;
                }
                if (c.HasChildren) Recolor(c, old, t);
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ThemeScrollbars(this);
            if (!Modal && StartPosition == FormStartPosition.CenterParent && Owner != null && Owner.Visible && Owner.WindowState != FormWindowState.Minimized)
            {
                Rectangle o = Owner.Bounds;
                Rectangle wa = Screen.FromRectangle(o).WorkingArea;
                int x = o.Left + (o.Width - Width) / 2, y = o.Top + (o.Height - Height) / 2;
                Location = new Point(Math.Max(wa.Left, Math.Min(x, wa.Right - Width)), Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height)));
            }
        }

        protected InputBox MakeField(string value, bool password, bool multiline)
        {
            InputBox b = new InputBox("");
            b.Field = true;
            if (multiline)
            {
                b.Box.Multiline = true;
                b.Box.ScrollBars = ScrollBars.Vertical;
                b.Box.AcceptsReturn = true;
                b.Box.WordWrap = true;
            }
            b.SetTheme(Th);
            b.Box.Text = value ?? "";
            if (password) b.Box.UseSystemPasswordChar = true;
            return b;
        }

        protected ThemedCombo MakeCombo(string[] items, int selected)
        {
            return new ThemedCombo(Th, items, selected);
        }

        void ThemeScrollbars(Control root)
        {
            foreach (Control c in root.Controls)
            {
                TextBoxBase tb = c as TextBoxBase;
                ScrollableControl sc = c as ScrollableControl;
                if ((tb != null && tb.Multiline) || (sc != null && sc.AutoScroll))
                {
                    Control target = c;
                    Native.SetScrollbarTheme(target, Th);
                    target.HandleCreated += delegate { Native.SetScrollbarTheme(target, Th); };
                }
                if (c.HasChildren) ThemeScrollbars(c);
            }
        }
    }

    class SettingsDialog : ThemedForm
    {
        readonly Settings s;
        readonly NavList nav;
        readonly Panel body = new Panel();
        readonly List<ScrollHost> pages = new List<ScrollHost>();
        readonly Button ok, cancel;

        InputBox lmToken, customUrl, customKey, customModel, workspace, instructions, memoryBox;
        ApiKeyRow anthropicKey, openaiKey, geminiKey, openrouterKey;
        string memoryOriginal = "";
        ThemedCombo lmCtx, engineCtx, engineKv;
        Segmented themeBox, accentBox;
        public Action DeleteAllChats;
        public Func<IWin32Window, Action<bool>, bool> WipeBrowser;
        public Func<IWin32Window, bool> EraseAll;
        public Action ResetAll;
        public Action<string, string> PreviewLook;

        string ThemeChoice { get { return themeBox.SelectedIndex == 1 ? "light" : themeBox.SelectedIndex == 2 ? "dark" : "system"; } }
        string AccentChoice { get { return accentBox.SelectedIndex == 1 ? "windows" : "green"; } }

        public const string ResetPrompt = "Reset every setting to its default?\n\nThe theme, tools, what Auto asks about, the agent's folder and everything else on these pages go back to how MinGent comes. Your chats, memory, API keys, instructions, models and offline mode stay.";
        SwitchRow offline, netLog, errorLog, lmUnload, engineUpdates, autoSave, openFiles, showThinking, sendContext, web, browserOn, guard, appsOn, sideBySide, memoryOn, helpersOn, autoReview, captcha;
        readonly Dictionary<string, SwitchRow> autoAsk = new Dictionary<string, SwitchRow>();
        readonly List<string> pageTitles = new List<string>();

        static readonly int[] CtxValues = { 0, 8192, 16384, 32768, 49152, 65536, 98304, 131072, 196608, 262144 };
        static readonly string[] CtxNames = { "LM Studio's default", "8K", "16K", "32K", "48K", "64K", "96K", "128K", "192K", "256K" };

        static int NavW { get { return UI.S(212); } }
        static int FootH { get { return UI.S(64); } }

        public SettingsDialog(Settings settings, Theme t) : base(t, "Settings", WindowW, WindowH)
        {
            s = settings;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            try
            {
                Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                int maxH = wa.Height - UI.S(60);
                if (ClientSize.Height > maxH) ClientSize = new Size(ClientSize.Width, Math.Max(UI.S(420), maxH));
            }
            catch { }
            nav = new NavList(Th);
            nav.SetBounds(0, 0, NavW, ClientSize.Height - FootH);
            nav.SelectedIndexChanged += delegate { Select(nav.SelectedIndex); };
            nav.MouseWheel += delegate (object o, MouseEventArgs e)
            {
                int i = nav.SelectedIndex;
                if (i < 0 || i >= pages.Count) return;
                Point at = pages[i].PointToClient(Control.MousePosition);
                if (pages[i].ClientRectangle.Contains(at)) pages[i].Wheel(e.Delta);
            };
            body.SetBounds(NavW, 0, ClientSize.Width - NavW, ClientSize.Height - FootH);
            body.BackColor = Th.Chrome;
            Controls.Add(nav);
            Controls.Add(body);
            ok = Dialogs.MakeButton("Save", Th, true);
            cancel = Dialogs.MakeButton("Cancel", Th, false);
            cancel.Location = new Point(ClientSize.Width - UI.S(20) - cancel.Width, ClientSize.Height - FootH + (FootH - cancel.Height) / 2);
            ok.Location = new Point(cancel.Left - UI.S(8) - ok.Width, cancel.Top);
            ok.Click += delegate { if (!Apply()) return; DialogResult = DialogResult.OK; Close(); };
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = null;
            CancelButton = cancel;
            Controls.Add(ok);
            Controls.Add(cancel);
            BuildGeneral();
            BuildModels();
            BuildAgent();
            BuildMemory();
            BuildSafety();
            BuildPrivacy();
            Select(0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Th.Border))
                e.Graphics.DrawLine(p, 0, ClientSize.Height - FootH, ClientSize.Width, ClientSize.Height - FootH);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            nav.Focus();
        }

        int X0 { get { return UI.S(14); } }
        int Y(ScrollHost p) { return (int)p.Tag; }
        void Advance(ScrollHost p, int dy) { p.Tag = (int)p.Tag + dy; }
        int W(ScrollHost p) { return p.Width - ScrollHost.BarW - X0 - UI.S(14); }

        public void OpenPage(string title)
        {
            int i = pageTitles.IndexOf(title ?? "");
            if (i >= 0) Select(i);
        }

        ScrollHost AddPage(string title, string glyph)
        {
            pageTitles.Add(title);
            nav.Add(title, glyph);
            ScrollHost p = new ScrollHost(Th);
            p.SetBounds(0, 0, body.Width, body.Height);
            p.Visible = false;
            body.Controls.Add(p);
            pages.Add(p);
            p.Tag = UI.S(20);
            Label l = Dialogs.MakeLabel(title, Th, false);
            l.Font = Fonts.Title;
            l.ForeColor = Th.Fore;
            int h = Fonts.Title.Height + UI.S(6);
            l.SetBounds(X0, Y(p), W(p), h);
            p.Content.Controls.Add(l);
            Advance(p, h + UI.S(14));
            return p;
        }

        void Finish(ScrollHost p)
        {
            p.ContentHeight = Y(p) + UI.S(12);
            p.Watch(p.Content);
        }

        void Select(int i)
        {
            if (i < 0 || i >= pages.Count) return;
            for (int k = 0; k < pages.Count; k++) pages[k].Visible = k == i;
            if (nav.SelectedIndex != i) nav.SelectedIndex = i;
        }

        void Section(ScrollHost p, string title, string desc)
        {
            if (Y(p) > UI.S(90)) Advance(p, UI.S(14));
            Label l = Dialogs.MakeLabel(title, Th, false);
            l.Font = Fonts.Heading;
            l.ForeColor = Th.Fore;
            int h = Fonts.Heading.Height + UI.S(2);
            l.SetBounds(X0 + UI.S(2), Y(p), W(p), h);
            p.Content.Controls.Add(l);
            Advance(p, h + UI.S(4));
            if (!string.IsNullOrEmpty(desc)) Note(p, desc);
            else Advance(p, UI.S(4));
        }

        void Note(ScrollHost p, string text)
        {
            Label l = Dialogs.MakeLabel(text, Th, true);
            int w = W(p) - UI.S(4);
            int h = Fonts.Height(text, UI.Font, w);
            l.SetBounds(X0 + UI.S(2), Y(p), w, h + UI.S(2));
            p.Content.Controls.Add(l);
            Advance(p, h + UI.S(10));
        }

        Card NewCard(ScrollHost p)
        {
            Card c = new Card(Th);
            p.Content.Controls.Add(c);
            return c;
        }

        void Place(ScrollHost p, Card c)
        {
            c.Location = new Point(X0, Y(p));
            c.Arrange(W(p));
            Advance(p, c.Height + UI.S(10));
        }

        FieldRow Row(Card c, string title, string note, Control field, int width)
        {
            int h = UI.S(32);
            return c.Add(new FieldRow(Th, title, note, field, width, h, false, null));
        }

        SwitchRow Switch(Card c, string title, string note, bool value)
        {
            return c.Add(new SwitchRow(Th, title, note, value));
        }

        ApiKeyRow KeyRow(Card c, string title, string source, string provider, string page, string saved)
        {
            ApiKeyRow r = new ApiKeyRow(Th, title, source, provider, page, Secret.Unprotect(saved));
            r.OfflineOn = delegate { return offline != null ? offline.Checked : Net.Offline; };
            return c.Add(r);
        }

        static string Protect(string key) { return string.IsNullOrEmpty(key) ? "" : Secret.Protect(key); }

        static string Words(int tokens) { return "about " + (tokens * 3 / 4).ToString("N0") + " words"; }

        static int Round(int tokens) { return Math.Max(50, (int)Math.Round(tokens / 50.0) * 50); }

        static string Tokens(List<AgentTool> tools) { return "about " + Round(AgentToolbox.Tokens(tools)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " tokens"; }

        static string Tokens(AgentTool tool) { return Tokens(new List<AgentTool>(new AgentTool[] { tool })); }

        static string Cap(string s) { return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s; }

        void BuildGeneral()
        {
            ScrollHost p = AddPage("General", "sliders");
            Section(p, "Appearance", null);
            Card c = NewCard(p);
            themeBox = new Segmented(Th, new string[] { "System", "Light", "Dark" }, s.ThemeName == "light" ? 1 : s.ThemeName == "dark" ? 2 : 0);
            Row(c, "Theme", "System follows the light or dark setting in Windows.", themeBox, themeBox.PreferredWidth);
            accentBox = new Segmented(Th, new string[] { "MinGent green", "Windows" }, s.AccentName == "windows" ? 1 : 0);
            Row(c, "Accent color", "Windows uses the accent color you picked in Windows Settings.", accentBox, accentBox.PreferredWidth);
            themeBox.SelectedIndexChanged += delegate { if (PreviewLook != null) PreviewLook(ThemeChoice, AccentChoice); };
            accentBox.SelectedIndexChanged += delegate { if (PreviewLook != null) PreviewLook(ThemeChoice, AccentChoice); };
            Place(p, c);

            Section(p, "Files", null);
            c = NewCard(p);
            bool registered = FileTypes.IsRegistered;
            PillButton reg = new PillButton(registered ? "Remove" : "Add", Th, false);
            reg.Width = Math.Max(reg.Width, UI.S(104));
            PillButton defaults = new PillButton("Open Windows Settings", Th, false);
            defaults.Enabled = registered;
            reg.Click += delegate
            {
                bool was = FileTypes.IsRegistered;
                string err = was ? FileTypes.Unregister() : FileTypes.Register();
                bool now = FileTypes.IsRegistered;
                reg.Text = now ? "Remove" : "Add";
                defaults.Enabled = now;
                if (err != null) Alert.Show(this, Th, "Windows did not accept the change.\n\n" + err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else if (now && !was) Alert.Show(this, Th, "MinGent is now in the Open with menu for text and code files.\n\nRight-click a file and choose Open with > MinGent. To open them with a double-click, use Open Windows Settings below.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            defaults.Click += delegate { FileTypes.OpenDefaultApps(); };
            Row(c, "Show MinGent in Open with", "For text and code files such as .txt, .md, .json, .log and .py. They open in Notes.", reg, reg.Width);
            Row(c, "Open files with a double-click", "Windows lets only you choose default apps. This opens Windows Settings, where you pick MinGent for the file types you want.", defaults, defaults.Width);
            Place(p, c);

            Section(p, "Keyboard shortcuts", null);
            c = NewCard(p);
            c.Add(new KeysRow(Th, "New chat", "Ctrl Shift N"));
            c.Add(new KeysRow(Th, "Open the agent and jump to the message box", "Ctrl Shift A"));
            c.Add(new KeysRow(Th, "Stop the agent", "Esc"));
            c.Add(new KeysRow(Th, "Plan first on or off", "Shift Tab"));
            c.Add(new KeysRow(Th, "Show or hide notes", "Ctrl Shift E"));
            c.Add(new KeysRow(Th, "Show or hide the browser", "Ctrl Shift B"));
            c.Add(new KeysRow(Th, "New, open and save notes", "Ctrl N", "Ctrl O", "Ctrl S"));
            c.Add(new KeysRow(Th, "Zoom in, out and back", "Ctrl +", "Ctrl -", "Ctrl 0"));
            c.Add(new KeysRow(Th, "Settings", "Ctrl ,"));
            Place(p, c);

            Section(p, "Commands", "Type them in the message box. A list shows up as soon as you type /.");
            c = NewCard(p);
            foreach (SlashCommand sc in SlashCommands.All) c.Add(new KeysRow(Th, sc.Detail, sc.Name));
            Place(p, c);

            Section(p, "Reset", null);
            c = NewCard(p);
            PillButton reset = new PillButton("Reset...", Th, false);
            reset.Click += delegate
            {
                if (!Alert.Confirm(this, Th, ResetPrompt, Text, "Reset", true)) return;
                if (ResetAll != null) ResetAll();
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Row(c, "Reset settings", "Every setting goes back to how MinGent comes. Your chats, memory, API keys, instructions, models and offline mode stay.", reset, reset.Width);
            Place(p, c);
            Finish(p);
        }

        void BuildModels()
        {
            ScrollHost p = AddPage("Models", "library");
            Section(p, "Built-in engine", "Runs GGUF models on this PC with llama.cpp " + Engine.BuildVersion + ", checked against SHA-256 checksums built into MinGent. It only listens on this PC. Set it up and download models from Models in the sidebar.");
            Card c = NewCard(p);
            c.Add(new FieldRow(Th, "Status", Engine.Instance.StatusText, null, 0, 0, false, null));
            int[] ctxVals = { 0, 8192, 16384, 32768, 49152, 65536, 98304, 131072, 196608, 262144 };
            string[] ctxNames = { "Auto", "8K", "16K", "32K", "48K", "64K", "96K", "128K", "192K", "256K" };
            int ei = Array.IndexOf(ctxVals, s.EngineContext);
            engineCtx = MakeCombo(ctxNames, ei < 0 ? 0 : ei);
            engineCtx.Tag = ctxVals;
            engineCtx.SetNote(0, "as much as fits");
            engineCtx.SetBadge(0, "Recommended");
            for (int i = 1; i < ctxVals.Length; i++) engineCtx.SetNote(i, Words(ctxVals[i]));
            Row(c, "Context length", "How much of the chat the model can see at once. Auto picks the most that fits in your graphics memory.", engineCtx, UI.S(200));
            engineKv = MakeCombo(new string[] { "Full precision", "Compact", "Smallest" }, s.EngineKv == "f16" ? 0 : s.EngineKv == "q4_0" ? 2 : 1);
            engineKv.NotesBelow = true;
            engineKv.SetNote(0, "f16, uses the most memory");
            engineKv.SetNote(1, "q8_0, about twice the context, almost no quality loss");
            engineKv.SetNote(2, "q4_0, the most context, slightly lower quality");
            engineKv.SetBadge(1, "Recommended");
            Row(c, "Context memory", "Compact fits about twice as much context as full precision.", engineKv, UI.S(200));
            PillButton folder = new PillButton(ModelLibrary.OwnRoot, Th, false);
            folder.PathText = true;
            folder.Click += delegate
            {
                string err;
                if (ModelFolder.Choose(this, Th, s, out err)) { folder.Text = ModelLibrary.OwnRoot; folder.Invalidate(); }
                else if (err != null) Alert.Show(this, Th, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Row(c, "Model folder", "Where downloaded models are saved. Models already in other folders stay there and keep working.", folder, UI.S(300));
            engineUpdates = Switch(c, "Offer newer llama.cpp releases", "Off: MinGent only installs the release it was tested with. On: it also looks on GitHub for newer releases, with the CUDA runtime they come with, and offers them in Models. Those are only checked against the checksums GitHub lists for them.", s.OfferEngineUpdates);
            Place(p, c);

            Section(p, "Cloud models", "With a cloud model, your messages and the files the agent reads go to that provider. Keys are encrypted with your Windows account (DPAPI) and only sent to their own provider. MinGent checks a key when you paste it.");
            c = NewCard(p);
            anthropicKey = KeyRow(c, "Claude API key", "From platform.claude.com", "anthropic", "https://platform.claude.com/settings/keys", s.KeyAnthropic);
            openaiKey = KeyRow(c, "OpenAI API key", "From platform.openai.com", "openai", "https://platform.openai.com/api-keys", s.KeyOpenAi);
            geminiKey = KeyRow(c, "Gemini API key", "From aistudio.google.com. Free keys work, with daily limits.", "gemini", "https://aistudio.google.com/apikey", s.KeyGemini);
            openrouterKey = KeyRow(c, "OpenRouter API key", "From openrouter.ai. One key for models from many providers.", "openrouter", "https://openrouter.ai/settings/keys", s.KeyOpenRouter);
            Place(p, c);

            Section(p, "Other model servers", "Only if you already run one. The built-in engine does the same job.");
            c = NewCard(p);
            customUrl = MakeField(s.CustomUrl, false, false);
            Row(c, "Server address", "Any OpenAI-compatible server, ending in /v1. For example http://localhost:11434/v1 for Ollama.", customUrl, UI.S(260));
            customKey = MakeField(Secret.Unprotect(s.KeyCustom), true, false);
            Row(c, "API key", "Optional", customKey, UI.S(260));
            customModel = MakeField(s.CustomModel, false, false);
            Row(c, "Model name", "Optional", customModel, UI.S(260));
            Place(p, c);
            c = NewCard(p);
            lmToken = MakeField(Secret.Unprotect(s.KeyLmStudio), true, false);
            Row(c, "LM Studio API token", "MinGent finds LM Studio's server by itself. A token is only needed if you turned on authentication there.", lmToken, UI.S(240));
            int ci = Array.IndexOf(CtxValues, s.LmContext);
            lmCtx = MakeCombo(CtxNames, ci < 0 ? 0 : ci);
            lmCtx.SetNote(0, "the model's own setting");
            for (int i = 1; i < CtxValues.Length; i++) lmCtx.SetNote(i, Words(CtxValues[i]));
            Row(c, "LM Studio context", "Used when MinGent loads a model in LM Studio for you.", lmCtx, UI.S(200));
            lmUnload = Switch(c, "Unload other LM Studio models first", "Frees graphics memory when MinGent loads a model there.", s.LmUnloadOthers);
            Place(p, c);
            Finish(p);
        }

        void BuildAgent()
        {
            ScrollHost p = AddPage("Agent", "face");
            Section(p, "Workspace", "When the open notes tab is a saved file, the agent works in that file's folder. Otherwise new files go here.");
            Card c = NewCard(p);
            workspace = MakeField(s.AiWorkspace.Length > 0 ? s.AiWorkspace : s.DefaultWorkspace, false, false);
            PillButton browse = new PillButton("Browse...", Th, false);
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.SelectedPath = workspace.Box.Text;
                    d.ShowNewFolderButton = true;
                    if (d.ShowDialog(this) == DialogResult.OK) workspace.Box.Text = d.SelectedPath;
                }
            };
            c.Add(new FieldRow(Th, "Default folder", null, workspace, 0, UI.S(32), true, browse));
            Place(p, c);

            Section(p, "Your instructions", "Sent with every chat: how you like answers, your coding style, or facts about your setup. For notes about one project, ask the agent to keep them in MINGENT.md in that folder; it reads that file by itself.");
            c = NewCard(p);
            instructions = MakeField(s.AiInstructions, false, true);
            instructions.Box.AccessibleName = "Your instructions";
            c.Add(new FieldRow(Th, null, null, instructions, 0, UI.S(120), true, null));
            Place(p, c);

            Section(p, "Tools", "Every tool is part of MinGent's own C# code: no plugins, no MCP servers, nothing downloaded. Each group you switch on takes room in the model's context, so with a small local model, switch off what you don't use. Reading and changing files, running commands and planning are always on (" + Tokens(AgentToolbox.Create(false, false, false, 32768, false, false, false, false)) + ").");
            c = NewCard(p);
            web = Switch(c, "Web search and web pages", "Search the web and read pages and YouTube videos. With the built-in browser on, searches open there so you can see them. When this is off, the agent still opens addresses on this PC or your local network. " + Cap(Tokens(new WebSearchTool())) + ".", s.AiWeb);
            browserOn = Switch(c, "Built-in browser", "Open pages, click, type and scroll in MinGent's own browser, which has its own sign-ins. Each chat starts on a new page. " + Cap(Tokens(AgentToolbox.BrowserTools(true))) + "; off by itself when the model's context is under 16K.", s.AiBrowser);
            captcha = Switch(c, "Let the agent try CAPTCHAs (experimental)", "Off (the default): CAPTCHAs are left to you. On: the agent may try to solve them in its own browser, just to see what the model can do; after three failed tries it hands the CAPTCHA to you. A reCAPTCHA logo on a page is never treated as a check.", s.AiCaptcha);
            appsOn = Switch(c, "Programs it starts itself", "Read their windows, click, type and scroll in them. Programs that were already open stay off limits. Store apps and other programs that Windows opens for it need your OK first (except in Full auto). " + Cap(Tokens(AgentToolbox.AppTools(true))) + "; off by itself under 24K.", s.AiApps);
            sideBySide = Switch(c, "Put MinGent and the program side by side", "When the agent starts working in a program that covers MinGent, MinGent takes one half of the screen and the program the other. MinGent gets its old size back when the program closes.", s.AiSideBySide);
            helpersOn = Switch(c, "Helper agents", "Hands focused tasks to helpers, each with a fresh context, which keeps the agent's own context small and gives its work an independent check. On a local model they run one after another, so they take extra time; with a cloud model they can run at the same time. " + Cap(Tokens(new DelegateTool(false))) + ".", s.AiHelpers);
            autoReview = Switch(c, "Review big changes", "When the agent changes a lot of code, a helper reviews the work before it answers.", s.AiAutoReview);
            Place(p, c);

            Section(p, "Behavior", null);
            c = NewCard(p);
            autoSave = Switch(c, "Save files after the agent changes them", "Ctrl+Z still undoes each change.", s.AiAutoSave);
            openFiles = Switch(c, "Show the files the agent writes", "Opens the files it creates or edits in tabs. Turn this off when you only watch the result, like a website in the browser. Files you open yourself still update.", s.AiOpenFiles);
            showThinking = Switch(c, "Show the model's thinking", "See its reasoning while it works.", s.AiShowThinking);
            sendContext = Switch(c, "Share the open notes tab", "Tells the agent which notes tab is open and sends the text you selected with each message.", s.AiSendContext);
            Place(p, c);

            Finish(p);
        }

        void BuildMemory()
        {
            ScrollHost p = AddPage("Memory", "bookmark");
            Card c = NewCard(p);
            memoryOn = Switch(c, "Remember things between chats", "Facts like your hardware, the tools you have installed, where your projects are and how you like things done. Never passwords or keys. The memory tool takes about " + Round(AgentToolbox.Tokens(new MemoryTool())) + " tokens of the model's context, plus the facts themselves.", s.AiMemory);
            Place(p, c);

            Section(p, "Saved facts", "One fact per line. Edit or delete anything you like; changes apply when you press Save.");
            c = NewCard(p);
            List<string> items = MemoryStore.Load();
            memoryOriginal = string.Join("\r\n", items.ToArray());
            memoryBox = MakeField(memoryOriginal, false, true);
            memoryBox.Box.AccessibleName = "Saved facts";
            memoryBox.Box.AccessibleDescription = "One fact per line";
            c.Add(new FieldRow(Th, null, null, memoryBox, 0, UI.S(200), true, null));
            PillButton clear = new PillButton("Clear list", Th, false);
            clear.Danger = true;
            clear.Click += delegate { memoryBox.Box.Text = ""; memoryBox.Box.Focus(); };
            Row(c, "Forget everything", "Clears the list above. Nothing is deleted until you press Save.", clear, clear.Width);
            Place(p, c);
            Note(p, "Saved in " + MemoryStore.FilePath + " on this PC only.");
            Finish(p);
        }

        void BuildSafety()
        {
            ScrollHost p = AddPage("Safety", "shield");
            Card c;
            Section(p, "Auto mode", "Auto (the switch under the message box) lets the agent edit files, run commands and browse without asking. The arrow next to it has Full auto, which asks nothing at all and goes back to Auto when you close MinGent.");
            c = NewCard(p);
            guard = Switch(c, "Prompt-injection guard (recommended)", "Once the agent has read web content, Auto asks again before commands, writes outside the workspace, addresses that carry a lot of data and everything listed below, also rows you switched off.", s.AiWebGuard);
            Place(p, c);

            Section(p, "What Auto still asks about", "Switched on, Auto stops and asks you first. Switch a row off to let Auto do it without asking. Ask mode always asks; Full auto never does.");
            c = NewCard(p);
            List<string> allowed = s.AutoAllowList;
            foreach (string cat in PermissionCategory.All)
                autoAsk[cat] = Switch(c, PermissionCategory.Title(cat), PermissionCategory.Note(cat), !allowed.Contains(cat));
            Place(p, c);

            Section(p, "Always on", "These limits apply in every mode, also in Full auto.");
            c = NewCard(p);
            c.Dividers = false;
            string[] rules = {
                "The agent's file tools never write into Windows, Program Files or MinGent's own data, also not through links. Commands are limited by Windows permissions instead.",
                "Sending one of your saved API keys to a website or over the internet asks you first, and Full auto refuses it.",
                "The agent only controls programs it started, never browsers, mail, chat, password managers or remote-access tools.",
                "Web pages and documents reach the model marked as untrusted data, with a note not to follow them.",
                "Changes made with the agent's file tools can be undone with the Undo link under the reply, or Ctrl+Z in the notes tab. Changes made by commands can't.",
                "Commands run with MinGent's rights, so start MinGent normally, not as administrator. Closing MinGent stops everything it started: commands, the built-in engine, and models it loaded in LM Studio." };
            foreach (string r in rules) c.Add(new BulletRow(Th, "check", r));
            Place(p, c);
            Finish(p);
        }

        void BuildPrivacy()
        {
            ScrollHost p = AddPage("Privacy", "lock");
            Section(p, "Offline mode", null);
            Card c = NewCard(p);
            offline = Switch(c, "Work offline", "MinGent makes no internet connections: no cloud models, web search, web pages or downloads. Local models and everything on this PC keep working, and the agent may not run commands that need the internet. It is not a firewall: a program that goes online by itself still can.", s.Offline);
            Place(p, c);

            Section(p, "Logs", "MinGent sends nothing about you anywhere: no telemetry, no crash reports, and no update checks unless you switch on newer llama.cpp releases in Models.");
            c = NewCard(p);
            PillButton show = new PillButton("Show...", Th, false);
            show.Click += delegate { using (ConnectionsDialog d = new ConnectionsDialog(Th)) d.ShowDialog(this); };
            Row(c, "Connections", "Every internet address MinGent connected to since it started, and why. The list lives in memory and is gone when MinGent closes.", show, show.Width);
            netLog = Switch(c, "Keep a connection log file", "Also writes each connection to connections.log in MinGent's data folder.", s.KeepNetLog);
            errorLog = Switch(c, "Keep an error log", "When something goes wrong, the details go to error.log in MinGent's data folder, for a bug report. Off: nothing is written.", s.KeepErrorLog);
            Place(p, c);

            Section(p, "Your data", "MinGent keeps everything it stores in one folder. The files the agent saves for you go to your own folders, not this one.");
            c = NewCard(p);
            PillButton open = new PillButton("Open", Th, false);
            open.Click += delegate
            {
                try { Directory.CreateDirectory(Program.DataDir); System.Diagnostics.Process.Start(Native.WindowsExe("explorer.exe"), "\"" + Program.DataDir + "\""); } catch { }
            };
            PillButton del = new PillButton("Delete all chats...", Th, false);
            del.Danger = true;
            del.Click += delegate { if (DeleteAllChats != null) DeleteAllChats(); };
            PillButton wipe = new PillButton("Wipe browser data...", Th, false);
            wipe.Danger = true;
            PillButton erase = new PillButton("Erase everything...", Th, false);
            erase.Danger = true;
            open.Width = del.Width = wipe.Width = erase.Width = Math.Max(Math.Max(del.Width, wipe.Width), erase.Width);
            wipe.Click += delegate
            {
                if (WipeBrowser == null) return;
                bool started = WipeBrowser(this, delegate (bool ok)
                {
                    if (wipe.IsDisposed) return;
                    wipe.Text = "Wipe browser data...";
                    wipe.Enabled = true;
                    if (IsDisposed || !Visible) return;
                    Alert.Show(this, Th, ok ? "The built-in browser's data was wiped. You're signed out of every site in it."
                                            : "Most of the browser's data was wiped. A few files were still in use; MinGent wipes them the next time it starts.",
                               Text, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                });
                if (!started) return;
                wipe.Text = "Wiping...";
                wipe.Enabled = false;
            };
            erase.Click += delegate
            {
                if (EraseAll == null || !EraseAll(this)) return;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Row(c, "Data folder", Program.DataDir ?? "", open, open.Width);
            Row(c, "Saved chats", "Right-click a chat in the sidebar to rename or delete it. Deleting erases it for good.", del, del.Width);
            Row(c, "Browser data", "The built-in browser's sign-ins, history, cache and site data. Deleting a chat clears the history and cache; wiping signs you out of every site and erases it all.", wipe, wipe.Width);
            Row(c, "Erase everything", "Overwrites and deletes your chats, memory, API keys, browser data, logs and backups of your notes tabs, then closes MinGent so it starts like new. You choose whether the agent's saved files, the engine and the models go too.", erase, erase.Width);
            Place(p, c);
            Finish(p);
        }

        bool Apply()
        {
            string wsText = workspace.Box.Text.Trim();
            if (wsText.Length > 0 && !string.Equals(wsText, s.DefaultWorkspace, StringComparison.OrdinalIgnoreCase))
            {
                string werr;
                string wfull = AgentPaths.Resolve(wsText, null, out werr);
                string why = wfull == null ? (werr ?? "it is not a valid folder path") : AgentPaths.WriteBlocked(Path.Combine(wfull, "x.txt"));
                if (why != null)
                {
                    Select(2);
                    Alert.Show(this, Th, "The default folder can't be used:\n" + wsText + "\n\n" + why, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    workspace.Box.Focus();
                    workspace.Box.SelectAll();
                    return false;
                }
            }
            foreach (ApiKeyRow kr in new ApiKeyRow[] { anthropicKey, openaiKey, geminiKey, openrouterKey })
            {
                string wrong = kr.Problem;
                if (wrong == null) continue;
                Select(1);
                Alert.Show(this, Th, kr.Text + ": " + wrong, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (offline.Checked && !s.Offline)
            {
                int n = ModelDownloads.Active().Count;
                if (n > 0 && !Alert.Confirm(this, Th, (n == 1 ? "A model is downloading. Offline mode stops it." : n + " models are downloading. Offline mode stops them.") +
                        "\n\nA download that stops picks up where it left off when you start it again.", Text, "Turn on offline mode", false))
                {
                    Select(5);
                    return false;
                }
            }
            s.ThemeName = ThemeChoice;
            s.AccentName = AccentChoice;
            s.KeyLmStudio = lmToken.Box.Text.Trim().Length > 0 ? Secret.Protect(lmToken.Box.Text.Trim()) : "";
            s.LmContext = CtxValues[Math.Max(0, lmCtx.SelectedIndex)];
            s.LmUnloadOthers = lmUnload.Checked;
            s.OfferEngineUpdates = engineUpdates.Checked;
            int[] ev = (int[])engineCtx.Tag;
            s.EngineContext = ev[Math.Max(0, engineCtx.SelectedIndex)];
            s.EngineKv = engineKv.SelectedIndex == 0 ? "f16" : engineKv.SelectedIndex == 2 ? "q4_0" : "q8_0";
            s.KeyAnthropic = Protect(anthropicKey.Value);
            s.KeyOpenAi = Protect(openaiKey.Value);
            s.KeyGemini = Protect(geminiKey.Value);
            s.KeyOpenRouter = Protect(openrouterKey.Value);
            s.CustomUrl = customUrl.Box.Text.Trim().TrimEnd('/');
            s.KeyCustom = customKey.Box.Text.Trim().Length > 0 ? Secret.Protect(customKey.Box.Text.Trim()) : "";
            s.CustomModel = customModel.Box.Text.Trim();
            string ws = workspace.Box.Text.Trim();
            string def = s.AiWorkspace;
            s.AiWorkspace = "";
            s.AiWorkspace = ws.Length == 0 || string.Equals(ws, s.DefaultWorkspace, StringComparison.OrdinalIgnoreCase) ? "" : ws;
            if (s.AiWorkspace.Length > 0)
            {
                string err;
                string full = AgentPaths.Resolve(s.AiWorkspace, null, out err);
                if (full == null || AgentPaths.WriteBlocked(Path.Combine(full, "x.txt")) != null) s.AiWorkspace = def;
                else s.AiWorkspace = full;
            }
            s.AiInstructions = instructions.Box.Text.Replace("\r\n", "\n").Trim();
            s.AiAutoSave = autoSave.Checked;
            s.AiOpenFiles = openFiles.Checked;
            s.AiShowThinking = showThinking.Checked;
            s.AiSendContext = sendContext.Checked;
            s.AiWeb = web.Checked;
            s.AiBrowser = browserOn.Checked;
            s.AiApps = appsOn.Checked;
            s.AiSideBySide = sideBySide.Checked;
            s.AiWebGuard = guard.Checked;
            s.AiCaptcha = captcha.Checked;
            s.Offline = offline.Checked;
            s.KeepNetLog = netLog.Checked;
            s.KeepErrorLog = errorLog.Checked;
            List<string> allow = new List<string>();
            foreach (KeyValuePair<string, SwitchRow> kv in autoAsk) if (!kv.Value.Checked) allow.Add(kv.Key);
            s.AiAutoAllow = string.Join(",", allow.ToArray());
            s.AiMemory = memoryOn.Checked;
            s.AiHelpers = helpersOn.Checked;
            s.AiAutoReview = autoReview.Checked;
            string mem = memoryBox.Box.Text.Replace("\r\n", "\n").Trim();
            if (mem != memoryOriginal.Replace("\r\n", "\n").Trim()) MemoryStore.SaveRaw(mem);
            return true;
        }
    }

    class ApiKeyRow : Control, IRow, IThemed
    {
        Theme th;
        readonly string provider, source, page;
        readonly InputBox field;
        readonly PillButton change, remove, getKey, cancelEdit, undo;
        readonly Timer debounce = new Timer();
        string current, removed, status = "";
        int tone;
        bool editing;
        CancelToken pending;
        public Func<bool> OfflineOn;

        public void SetTheme(Theme t) { th = t; Invalidate(); }

        public ApiKeyRow(Theme t, string title, string source, string provider, string page, string key)
        {
            th = t;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = UI.Font;
            Text = title;
            this.source = source ?? "";
            this.provider = provider;
            this.page = page;
            current = Clean(key);
            field = new InputBox("Paste your API key");
            field.Field = true;
            field.SetTheme(t);
            field.Box.UseSystemPasswordChar = true;
            field.Box.AccessibleName = title;
            change = Button("Change", "Change the " + title);
            remove = Button("Remove", "Remove the " + title);
            getKey = Button("Get a key", "Open the page where you create a key");
            getKey.Glyph = "external";
            getKey.Width = getKey.PreferredWidth;
            cancelEdit = Button("Cancel", "Keep the saved key");
            undo = Button("Undo", "Keep the key");
            field.TabIndex = 0;
            getKey.TabIndex = cancelEdit.TabIndex = undo.TabIndex = 1;
            change.TabIndex = 2;
            remove.TabIndex = 3;
            Controls.AddRange(new Control[] { field, getKey, cancelEdit, undo, change, remove });
            debounce.Interval = 600;
            debounce.Tick += delegate { debounce.Stop(); CheckTyped(); };
            field.Box.TextChanged += delegate
            {
                debounce.Stop();
                if (pending != null) { pending.Cancel(); pending = null; }
                string v = Clean(field.Box.Text);
                if (v.Length >= 20) { debounce.Start(); return; }
                SetStatus(v.Length > 0 ? "" : removed != null ? "Removed when you save." : current.Length > 0 ? "Paste the new key. The saved one stays until you do." : "", 0);
            };
            field.Box.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None) { debounce.Stop(); CheckTyped(); e.Handled = e.SuppressKeyPress = true; }
            };
            change.Click += delegate
            {
                StopCheck();
                editing = true;
                field.Box.Text = "";
                SetStatus("Paste the new key. The saved one stays until you do.", 0);
                Arrange();
                field.Box.Focus();
            };
            cancelEdit.Click += delegate
            {
                editing = false;
                field.Box.Text = "";
                Arrange();
                change.Focus();
                Check(current);
            };
            remove.Click += delegate
            {
                StopCheck();
                removed = current;
                current = "";
                editing = true;
                field.Box.Text = "";
                SetStatus("Removed when you save.", 0);
                Arrange();
                undo.Focus();
            };
            undo.Click += delegate
            {
                current = removed ?? "";
                removed = null;
                editing = current.Length == 0;
                field.Box.Text = "";
                Arrange();
                if (!editing) change.Focus();
                Check(current);
            };
            getKey.Click += delegate { BrowserPane.OpenExternally(this.page); };
            MouseUp += delegate (object o, MouseEventArgs e) { if (e.Button == MouseButtons.Right) ShowMenu(e.Location); };
            editing = current.Length == 0;
            Arrange();
            HandleCreated += delegate { if (current.Length > 0 && status.Length == 0) Check(current); };
        }

        PillButton Button(string text, string name)
        {
            PillButton b = new PillButton(text, th, false);
            b.AccessibleName = name;
            b.Width = b.PreferredWidth;
            return b;
        }

        static string Clean(string key)
        {
            string k = (key ?? "").Trim().Trim('"', '\'').Trim();
            if (k.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) k = k.Substring(7).Trim();
            return k;
        }

        public string Value
        {
            get
            {
                string typed = Clean(field.Box.Text);
                return editing && typed.Length > 0 ? typed : current;
            }
        }

        public string Problem { get { return ModelCatalog.WrongKey(provider, Value); } }

        public static string Masked(string key)
        {
            string k = Clean(key);
            if (k.Length <= 12) return new string('•', Math.Min(8, Math.Max(1, k.Length)));
            return k.Substring(0, Math.Min(7, k.Length / 4)) + "…" + k.Substring(k.Length - 4);
        }

        static int PadX { get { return UI.S(14); } }
        static int PadY { get { return UI.S(12); } }
        static int RightW { get { return UI.S(330); } }

        int TextWidth(int width) { return Math.Max(UI.S(80), width - PadX * 2 - RightW - UI.S(20)); }

        public int MeasureHeight(int width)
        {
            int tw = TextWidth(width);
            return Math.Max(UI.S(64), Fonts.Height("Ag", Font, tw) + UI.S(2) + Fonts.Height("Ag", Fonts.Note, tw) * 2 + PadY * 2);
        }

        void Arrange()
        {
            PillButton side = removed != null ? undo : current.Length > 0 ? cancelEdit : getKey;
            field.Visible = editing;
            getKey.Visible = editing && side == getKey;
            cancelEdit.Visible = editing && side == cancelEdit;
            undo.Visible = editing && side == undo;
            change.Visible = remove.Visible = !editing;
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (field == null) return;
            int h = UI.S(32), y = (Height - h) / 2, right = Width - PadX;
            foreach (PillButton b in new PillButton[] { getKey, cancelEdit, undo })
                b.SetBounds(right - b.Width, y, b.Width, h);
            PillButton side = undo.Visible ? undo : cancelEdit.Visible ? cancelEdit : getKey;
            field.SetBounds(right - RightW, y, Math.Max(UI.S(60), RightW - side.Width - UI.S(8)), h);
            remove.SetBounds(right - remove.Width, y, remove.Width, h);
            change.SetBounds(remove.Left - UI.S(8) - change.Width, y, change.Width, h);
        }

        void SetStatus(string text, int kind)
        {
            status = text ?? "";
            tone = kind;
            string d = status.Length > 0 ? status : source;
            field.Box.AccessibleDescription = d;
            change.AccessibleDescription = d;
            AccessibleDescription = d;
            Invalidate();
        }

        void CheckTyped()
        {
            string v = Clean(field.Box.Text);
            if (v.Length > 0) Check(v);
        }

        void StopCheck()
        {
            debounce.Stop();
            if (pending != null) { pending.Cancel(); pending = null; }
        }

        void Check(string key)
        {
            if (pending != null) { pending.Cancel(); pending = null; }
            if (string.IsNullOrEmpty(key)) return;
            bool saved = key == current && !editing;
            if (OfflineOn != null && OfflineOn())
            {
                SetStatus(saved ? "Saved. Keys are not checked while offline mode is on." : "Not checked while offline mode is on.", 0);
                return;
            }
            string wrong = ModelCatalog.WrongKey(provider, key);
            if (wrong != null) { SetStatus(wrong, 2); return; }
            if (!IsHandleCreated) return;
            CancelToken ct = new CancelToken();
            pending = ct;
            SetStatus("Checking the key...", 0);
            string prov = provider;
            System.Threading.Thread t = new System.Threading.Thread(delegate ()
            {
                KeyCheck c;
                try { c = ModelCatalog.CheckKey(prov, key, ct); }
                catch (Exception ex) { c = new KeyCheck(); c.Message = "Couldn't check the key: " + ex.Message; }
                if (ct.IsCancelled) return;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (ct.IsCancelled || IsDisposed) return;
                        pending = null;
                        Checked(key, c);
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void Checked(string key, KeyCheck c)
        {
            if (editing ? Clean(field.Box.Text) != key : key != current) return;
            if (c.Ok && editing && Clean(field.Box.Text) == key)
            {
                bool had = field.Box.Focused;
                current = key;
                removed = null;
                editing = false;
                field.Box.Text = "";
                Arrange();
                if (had) change.Focus();
            }
            SetStatus(c.Message, c.Ok ? 1 : c.Rejected ? 2 : 0);
        }

        void ShowMenu(Point at)
        {
            string text = status.Length > 0 ? status : source;
            if (text.Length == 0) return;
            ContextMenuStrip m = Menus.Create(th);
            m.Items.Add(Menus.Item("Copy this message", false, delegate { try { Clipboard.SetText(Text + ": " + text); } catch { } }));
            m.Closed += delegate { BeginInvoke(new Action(delegate { m.Dispose(); })); };
            m.Show(this, at);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : th.Card);
            int tw = TextWidth(Width);
            string line2 = status.Length > 0 ? status : source;
            int h1 = Fonts.Height(Text, Font, tw);
            int lineH = Fonts.Height("Ag", Fonts.Note, tw);
            int h2 = Math.Min(Fonts.Height(line2, Fonts.Note, tw), lineH * 2);
            int y = (Height - h1 - (h2 > 0 ? UI.S(2) + h2 : 0)) / 2;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(PadX, y, tw, h1), Enabled ? th.Fore : th.MenuDisabled, Fonts.Wrap);
            if (h2 > 0)
            {
                Color c = status.Length > 0 && tone == 1 ? th.Good : status.Length > 0 && tone == 2 ? th.Error : th.ChromeDim;
                TextRenderer.DrawText(g, line2, Fonts.Note, new Rectangle(PadX, y + h1 + UI.S(2), tw, h2), c, Fonts.Wrap | TextFormatFlags.EndEllipsis);
            }
            if (!editing && current.Length > 0)
            {
                string m = Masked(current);
                int mw = Fonts.Width(m, Font);
                int x = change.Left - UI.S(12) - mw;
                if (x > PadX + tw) TextRenderer.DrawText(g, m, Font, new Rectangle(x, 0, mw + UI.S(2), Height), th.ChromeDim, Fonts.Line);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                debounce.Stop();
                debounce.Dispose();
                if (pending != null) pending.Cancel();
            }
            base.Dispose(disposing);
        }
    }
}
