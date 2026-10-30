using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MinGent
{
    class PageDialog : Form
    {
        readonly Theme th;
        readonly InputBox input;

        public string Value { get { return input == null ? null : input.Box.Text; } }

        public PageDialog(Theme t, int kind, string origin, string message, string defaultText)
        {
            th = t ?? Theme.Light(Color.SteelBlue);
            Text = kind == 3 ? "Leave this page?" : string.IsNullOrEmpty(origin) ? "This page says" : origin + " says";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = UI.Font;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = th.Chrome;
            ForeColor = th.ChromeFore;

            int pad = UI.S(18), w = UI.S(460), inner = w - pad * 2;
            string msg = kind == 3 ? "Changes you made on this page may not be saved." : (message ?? "");
            if (msg.Length > 6000) msg = msg.Substring(0, 6000) + "...";
            msg = msg.Replace("\r\n", "\n").Replace("\n", "\r\n");
            Size need = TextRenderer.MeasureText(msg.Length == 0 ? " " : msg, UI.Font, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int textH = Math.Max(UI.S(22), Math.Min(UI.S(300), need.Height + UI.S(4)));

            CueTextBox text = new CueTextBox();
            text.MenuTheme = th;
            text.Multiline = true;
            text.ReadOnly = true;
            text.BorderStyle = BorderStyle.None;
            text.BackColor = th.Chrome;
            text.ForeColor = th.ChromeFore;
            text.Font = UI.Font;
            text.TabStop = false;
            text.Text = msg;
            text.ScrollBars = need.Height + UI.S(4) > textH ? ScrollBars.Vertical : ScrollBars.None;
            text.SetBounds(pad, pad, inner, textH);
            text.HandleCreated += delegate { Native.SetScrollbarTheme(text, th); };
            Controls.Add(text);

            int y = pad + textH + UI.S(12);
            if (kind == 2)
            {
                input = new InputBox("");
                input.Field = true;
                input.SetTheme(th);
                input.Box.Text = defaultText ?? "";
                input.SetBounds(pad, y, inner, UI.S(34));
                Controls.Add(input);
                y += UI.S(34) + UI.S(14);
            }
            else y += UI.S(4);

            bool two = kind != 0;
            Button ok = Dialogs.MakeButton(kind == 3 ? "Leave" : "OK", th, true);
            ok.DialogResult = DialogResult.OK;
            int bw = UI.S(92), bh = UI.S(32);
            if (two)
            {
                Button cancel = Dialogs.MakeButton(kind == 3 ? "Stay" : "Cancel", th, false);
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(w - pad - bw, y, bw, bh);
                ok.SetBounds(w - pad - bw * 2 - UI.S(8), y, bw, bh);
                Controls.Add(cancel);
                CancelButton = cancel;
            }
            else
            {
                ok.SetBounds(w - pad - bw, y, bw, bh);
                CancelButton = ok;
            }
            Controls.Add(ok);
            AcceptButton = ok;
            ClientSize = new Size(w, y + bh + pad);
            Shown += delegate
            {
                if (input != null) { input.Box.Focus(); input.Box.SelectAll(); }
                else ok.Focus();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetTitleBar(this, th);
        }
    }

    class BrowserPopup : Form
    {
        public readonly Panel Host = new Panel();
        public readonly List<object> Keep = new List<object>();
        public ICoreWebView2Controller2 Ctl;
        public ICoreWebView2_4 View;
        readonly Theme th;
        readonly Control strip = new Control();
        string url = "", title = "";

        public BrowserPopup(Theme t, Rectangle bounds, Size client)
        {
            th = t ?? Theme.Light(Color.SteelBlue);
            Text = "Loading...";
            Font = UI.Font;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = th.Chrome;
            ForeColor = th.ChromeFore;
            ShowInTaskbar = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.Manual;
            try { using (System.IO.Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("MinGent.ico")) if (s != null) Icon = new Icon(s); } catch { }
            strip.Dock = DockStyle.Top;
            strip.Height = UI.S(30);
            strip.Paint += PaintStrip;
            Host.Dock = DockStyle.Fill;
            Host.BackColor = th.Back;
            Controls.Add(Host);
            Controls.Add(strip);
            Location = bounds.Location;
            ClientSize = new Size(client.Width, client.Height + strip.Height);
            Host.Resize += delegate { Fit(); };
            Move += delegate { if (Ctl != null) { try { Ctl.NotifyParentWindowPositionChanged(); } catch { } } };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetTitleBar(this, th);
        }

        public void Attach(ICoreWebView2Controller2 c, ICoreWebView2_4 v)
        {
            Ctl = c;
            View = v;
            try { WvColor col = new WvColor(); col.A = 255; col.R = 255; col.G = 255; col.B = 255; c.SetDefaultBackgroundColor(col); } catch { }
            Fit();
            try { c.SetIsVisible(1); } catch { }
        }

        void Fit()
        {
            if (Ctl == null) return;
            try
            {
                WvRect r = new WvRect();
                r.Right = Host.ClientSize.Width;
                r.Bottom = Host.ClientSize.Height;
                Ctl.SetBounds(r);
            }
            catch { }
        }

        public string Url { get { return url; } }

        public void SetAddress(string u)
        {
            url = u ?? "";
            UpdateTitle();
            strip.Invalidate();
        }

        public void SetTitle(string t)
        {
            title = t ?? "";
            UpdateTitle();
        }

        void UpdateTitle()
        {
            string host = BrowserPane.HostOf(url);
            Text = title.Length > 0 ? title + (host.Length > 0 ? " - " + host : "") : host.Length > 0 ? host : "Pop-up";
        }

        void PaintStrip(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(th.Chrome)) g.FillRectangle(b, strip.ClientRectangle);
            using (Pen p = new Pen(th.Border)) g.DrawLine(p, 0, strip.Height - 1, strip.Width, strip.Height - 1);
            Uri u;
            string scheme = "", host = "", rest = "";
            if (Uri.TryCreate(url, UriKind.Absolute, out u))
            {
                scheme = u.Scheme;
                host = u.Host + (u.IsDefaultPort ? "" : ":" + u.Port);
                rest = u.PathAndQuery == "/" ? "" : u.PathAndQuery;
            }
            else rest = url;
            int x = UI.S(10);
            Rectangle r = new Rectangle(x, 0, strip.Width - x * 2, strip.Height);
            const TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            if (scheme == "http")
            {
                string warn = "Not secure  ";
                TextRenderer.DrawText(g, warn, UI.Font, r, th.Error, f);
                int ww = TextRenderer.MeasureText(g, warn, UI.Font, Size.Empty, f).Width;
                r.X += ww; r.Width -= ww;
            }
            if (host.Length > 0)
            {
                TextRenderer.DrawText(g, host, UI.Font, r, th.ChromeFore, f);
                int hw = TextRenderer.MeasureText(g, host, UI.Font, Size.Empty, f).Width;
                r.X += hw; r.Width -= hw;
            }
            if (r.Width > 0 && rest.Length > 0)
                TextRenderer.DrawText(g, rest, UI.Font, r, th.ChromeDim, f | TextFormatFlags.EndEllipsis);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { if (Ctl != null) Ctl.Close(); } catch { }
            Ctl = null;
            View = null;
            base.OnFormClosed(e);
        }
    }
}
