using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    // TextBox inside a rounded, themed frame.
    public class ThemedTextBox : Card
    {
        public readonly TextBox Box;

        public ThemedTextBox(bool multiline)
        {
            Fill = Theme.Control;
            Stroke = Theme.BorderStrong;
            Radius = 7;
            BackColor = Theme.Control;
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Control,
                ForeColor = Theme.Text,
                Font = multiline ? Theme.Mono : Theme.Body,
                Multiline = multiline,
                ScrollBars = ScrollBars.None, // native scrollbars are light; the box still scrolls with the wheel
                WordWrap = true
            };
            Box.GotFocus += (s, e) => { Stroke = Theme.Accent; Invalidate(); };
            Box.LostFocus += (s, e) => { Stroke = Theme.BorderStrong; Invalidate(); };
            Controls.Add(Box);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int px = Theme.S(10), py = Box.Multiline ? Theme.S(8) : (Height - Box.PreferredHeight) / 2;
            Box.Bounds = new Rectangle(px, py, Width - px * 2, Box.Multiline ? Height - py * 2 : Box.PreferredHeight);
        }
    }

    // All changes are written to Config.json immediately.
    public class SettingsWindow : BorderlessWindow
    {
        readonly Configuration cfg;
        readonly Logger logger;

        readonly TextLine hGeneral, hWarp, hTor, hAdvanced, lLanguage, lEndpoint, hintEndpoint, lBridges, hintBridges, lHttp, lSocks, portError, endpointError, footer;
        readonly SegmentSwitch language;
        readonly ToggleSwitch startup, launchAfter, shortcuts, fallback;
        readonly ThemedTextBox endpoint, bridges, httpPort, socksPort;
        readonly FlatButton done, openFolder, makeShortcut;
        readonly TextLine hUpdates, lTor, sTor, lApp, sApp;
        readonly FlatButton bTor, bApp;

        // Provided by MainWindow; they return a short status line for the Updates section.
        public Func<string> TorVersion;
        public Func<Task<string>> TorUpdate;
        public Func<Task<string>> AppUpdate;

        public SettingsWindow(Configuration cfg, Logger logger, string focus)
        {
            this.cfg = cfg;
            this.logger = logger;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            hGeneral = Header();
            lLanguage = Muted();
            language = new SegmentSwitch { Labels = new[] { "English", "Türkçe" }, Height = Theme.S(34) };
            language.SetIndex(cfg.Language == "tr" ? 1 : 0, false);
            language.UserChanged += i =>
            {
                cfg.Language = i == 1 ? "tr" : "en";
                cfg.Save();
                L.Set(cfg.Language);
                ApplyTexts();
            };
            Controls.Add(language);
            startup = Toggle(cfg.OpenAtStartup, v => cfg.OpenAtStartup = v);
            launchAfter = Toggle(cfg.LaunchDiscordAfterConnect, v => cfg.LaunchDiscordAfterConnect = v);
            shortcuts = Toggle(cfg.AutoUpdateProxified, v => cfg.AutoUpdateProxified = v);
            makeShortcut = new FlatButton { Glyph = Theme.Glyph.Link, Font = Theme.Small, Height = Theme.S(30) };
            makeShortcut.Click += (s, e) =>
            {
                if (AppSetup.CreateAppShortcuts(logger))
                {
                    makeShortcut.Glyph = Theme.Glyph.Check;
                    makeShortcut.Text = L.T("settings.shortcut.done");
                    makeShortcut.Width = makeShortcut.PreferredWidth();
                }
            };
            Controls.Add(makeShortcut);

            hWarp = Header();
            fallback = Toggle(cfg.WarpFallbackToTor, v => cfg.WarpFallbackToTor = v);
            lEndpoint = Muted();
            endpoint = new ThemedTextBox(false) { Height = Theme.S(32) };
            endpoint.Box.Text = cfg.WarpEndpoint ?? "";
            endpoint.Box.TextChanged += (s, e) => SaveEndpoint();
            Controls.Add(endpoint);
            hintEndpoint = Muted(true);
            endpointError = Error();

            hTor = Header();
            lBridges = Muted();
            bridges = new ThemedTextBox(true) { Height = Theme.S(76) };
            bridges.Box.Text = (cfg.TorBridges ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            bridges.Box.TextChanged += (s, e) =>
            {
                cfg.TorBridges = bridges.Box.Text.Trim();
                cfg.Save();
            };
            Controls.Add(bridges);
            hintBridges = Muted(true);

            hUpdates = Header();
            lTor = Muted();
            sTor = Muted(true);
            bTor = new FlatButton { Glyph = Theme.Glyph.Refresh, Font = Theme.Small, Height = Theme.S(30) };
            bTor.Click += async (s, e) => await RunUpdate(bTor, sTor, TorUpdate);
            lApp = Muted();
            sApp = Muted(true);
            bApp = new FlatButton { Glyph = Theme.Glyph.Refresh, Font = Theme.Small, Height = Theme.S(30) };
            bApp.Click += async (s, e) => await RunUpdate(bApp, sApp, AppUpdate);
            Controls.Add(bTor);
            Controls.Add(bApp);

            hAdvanced = Header();
            lHttp = Muted();
            lSocks = Muted();
            httpPort = new ThemedTextBox(false) { Height = Theme.S(32) };
            httpPort.Box.Text = cfg.GostPort;
            socksPort = new ThemedTextBox(false) { Height = Theme.S(32) };
            socksPort.Box.Text = cfg.MainProxyPort;
            httpPort.Box.TextChanged += (s, e) => SavePorts();
            socksPort.Box.TextChanged += (s, e) => SavePorts();
            Controls.Add(httpPort);
            Controls.Add(socksPort);
            portError = Error();

            footer = Muted(true);
            openFolder = new FlatButton { Glyph = Theme.Glyph.Folder, Font = Theme.Small, Height = Theme.S(30) };
            openFolder.Click += (s, e) => Process.Start("explorer.exe", "\"" + AppContext.BaseDirectory + "\"");
            Controls.Add(openFolder);
            done = new FlatButton { Primary = true, Height = Theme.S(30) };
            done.Click += (s, e) => Close();
            Controls.Add(done);

            ApplyTexts();

            Shown += (s, e) =>
            {
                RefreshVersions();
                if (focus == "endpoint")
                    endpoint.Box.Focus();
                else if (focus == "bridges")
                    bridges.Box.Focus();
                else
                    done.Focus();
            };
        }

        async Task RunUpdate(FlatButton button, TextLine status, Func<Task<string>> action)
        {
            if (action == null)
                return;
            button.Enabled = false;
            status.ForeColor = Theme.TextFaint;
            status.Text = L.T("update.checking");
            string result = await action();
            status.Text = result;
            button.Enabled = true;
            RefreshVersions();
        }

        void RefreshVersions()
        {
            string tor = TorVersion?.Invoke();
            lTor.Text = string.IsNullOrEmpty(tor) ? L.T("settings.tor.unknown") : L.F("settings.tor.version", tor);
            lApp.Text = L.F("settings.app.version", AppSetup.AppVersion);
        }

        TextLine Header()
        {
            var t = new TextLine { Font = Theme.Title, ForeColor = Theme.Text, Height = Theme.S(24) };
            Controls.Add(t);
            return t;
        }

        TextLine Muted(bool small = false)
        {
            var t = new TextLine { Font = small ? Theme.Small : Theme.Body, ForeColor = small ? Theme.TextFaint : Theme.TextMuted, Height = Theme.S(small ? 18 : 22) };
            Controls.Add(t);
            return t;
        }

        TextLine Error()
        {
            var t = new TextLine { Font = Theme.Small, ForeColor = Theme.Danger, Height = Theme.S(18) };
            Controls.Add(t);
            return t;
        }

        ToggleSwitch Toggle(bool value, Action<bool> set)
        {
            var t = new ToggleSwitch { Checked = value };
            t.CheckedChanged += (s, e) =>
            {
                set(t.Checked);
                cfg.Save();
            };
            Controls.Add(t);
            return t;
        }

        void SaveEndpoint()
        {
            string v = endpoint.Box.Text.Trim();
            int colon = v.LastIndexOf(':');
            int port;
            bool ok = v.Length == 0 || (colon > 0 && int.TryParse(v.Substring(colon + 1), out port) && port > 0 && port <= 65535);
            endpointError.Text = ok ? "" : L.T("settings.endpoint.invalid");
            if (ok)
            {
                cfg.WarpEndpoint = v;
                cfg.Save();
            }
        }

        void SavePorts()
        {
            int a, b;
            bool okA = int.TryParse(httpPort.Box.Text.Trim(), out a) && a > 0 && a <= 65535;
            bool okB = int.TryParse(socksPort.Box.Text.Trim(), out b) && b > 0 && b <= 65535;
            if (!okA || !okB)
            {
                portError.Text = L.T("settings.port.invalid");
                return;
            }
            if (a == b)
            {
                portError.Text = L.T("settings.port.same");
                return;
            }
            portError.Text = "";
            cfg.GostPort = a.ToString();
            cfg.MainProxyPort = b.ToString();
            cfg.Save();
        }

        void ApplyTexts()
        {
            Text = L.T("settings.title");
            hGeneral.Text = L.T("settings.general");
            lLanguage.Text = L.T("settings.language");
            startup.Text = L.T("settings.startup");
            launchAfter.Text = L.T("settings.launchafter");
            shortcuts.Text = L.T("settings.shortcuts");
            hWarp.Text = L.T("settings.warp");
            fallback.Text = L.T("settings.fallback");
            lEndpoint.Text = L.T("settings.endpoint");
            hintEndpoint.Text = L.T("settings.endpoint.hint");
            hTor.Text = L.T("settings.tor");
            lBridges.Text = L.T("settings.bridges");
            hintBridges.Text = L.T("settings.bridges.hint");
            hUpdates.Text = L.T("settings.updates");
            bTor.Text = L.T("settings.tor.check");
            bApp.Text = L.T("settings.app.check");
            RefreshVersions();
            hAdvanced.Text = L.T("settings.advanced");
            lHttp.Text = L.T("settings.port.http");
            lSocks.Text = L.T("settings.port.socks");
            footer.Text = L.T("settings.autosave");
            openFolder.Text = L.T("settings.folder");
            if (makeShortcut.Glyph != Theme.Glyph.Check)
                makeShortcut.Text = L.T("settings.shortcut");
            done.Text = L.T("btn.done");
            SaveEndpoint();
            SavePorts();
            DoLayout();
            Invalidate();
        }

        void DoLayout()
        {
            int pad = Theme.S(18);
            int w = Theme.S(420);
            int inner = w - pad * 2;
            int y = TitleHeight + Theme.S(14);

            Action<Control, int> place = (c, h) =>
            {
                c.SetBounds(pad, y, inner, h);
                y += h;
            };

            place(hGeneral, Theme.S(24));
            y += Theme.S(6);
            lLanguage.SetBounds(pad, y + Theme.S(6), Theme.S(120), Theme.S(22));
            language.SetBounds(pad + inner - Theme.S(210), y, Theme.S(210), Theme.S(34));
            y += Theme.S(44);
            place(startup, Theme.S(26));
            y += Theme.S(6);
            place(launchAfter, Theme.S(26));
            y += Theme.S(6);
            place(shortcuts, Theme.S(26));
            y += Theme.S(10);
            makeShortcut.Width = makeShortcut.PreferredWidth();
            makeShortcut.Location = new Point(pad, y);
            y += Theme.S(30) + Theme.S(18);

            place(hWarp, Theme.S(24));
            y += Theme.S(6);
            place(fallback, Theme.S(26));
            y += Theme.S(10);
            place(lEndpoint, Theme.S(22));
            place(endpoint, Theme.S(32));
            y += Theme.S(2);
            hintEndpoint.SetBounds(pad, y, inner / 2 + Theme.S(60), Theme.S(18));
            endpointError.SetBounds(pad + inner / 2 + Theme.S(60), y, inner / 2 - Theme.S(60), Theme.S(18));
            endpointError.Flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter;
            y += Theme.S(18) + Theme.S(16);

            place(hTor, Theme.S(24));
            y += Theme.S(6);
            place(lBridges, Theme.S(22));
            place(bridges, Theme.S(76));
            y += Theme.S(2);
            place(hintBridges, Theme.S(18));
            y += Theme.S(16);

            place(hUpdates, Theme.S(24));
            y += Theme.S(8);
            foreach (var row in new[] { Tuple.Create(lTor, sTor, bTor), Tuple.Create(lApp, sApp, bApp) })
            {
                row.Item3.Width = row.Item3.PreferredWidth();
                row.Item3.Location = new Point(pad + inner - row.Item3.Width, y + Theme.S(4));
                int textW = inner - row.Item3.Width - Theme.S(10);
                row.Item1.SetBounds(pad, y, textW, Theme.S(20));
                row.Item2.SetBounds(pad, y + Theme.S(20), textW, Theme.S(18));
                y += Theme.S(44);
            }
            y += Theme.S(12);

            place(hAdvanced, Theme.S(24));
            y += Theme.S(6);
            int half = (inner - Theme.S(12)) / 2;
            lHttp.SetBounds(pad, y, half, Theme.S(22));
            lSocks.SetBounds(pad + half + Theme.S(12), y, half, Theme.S(22));
            y += Theme.S(22);
            httpPort.SetBounds(pad, y, half, Theme.S(32));
            socksPort.SetBounds(pad + half + Theme.S(12), y, half, Theme.S(32));
            y += Theme.S(34);
            place(portError, Theme.S(18));
            y += Theme.S(14);

            place(footer, Theme.S(18));
            y += Theme.S(10);
            done.Width = Math.Max(Theme.S(80), done.PreferredWidth());
            openFolder.Width = openFolder.PreferredWidth();
            done.Location = new Point(pad + inner - done.Width, y);
            openFolder.Location = new Point(done.Left - Theme.S(8) - openFolder.Width, y);
            y += Theme.S(30) + pad;
            ClientSize = new Size(w, y);
            LayoutTitleButtons();
        }
    }
}
