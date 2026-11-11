using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    // The classic look (WinForms). All behaviour lives in AppController; this window only shows it.
    public class MainWindow : BorderlessWindow, IAppView
    {
        readonly AppController app;
        readonly Configuration cfg;
        bool startHidden;
        bool initialized;

        // Title bar
        readonly FlatButton trayButton, minButton;

        // Status card
        readonly Card statusCard;
        readonly StatusDot dot;
        readonly TextLine statusTitle, statusSub;
        readonly FlatButton connectButton;
        readonly ProgressLine progress;

        // Route
        readonly TextLine routeLabel, routeHint;
        readonly SegmentSwitch route;
        readonly ToggleSwitch autoConnect, autoRoute;

        // Discord card
        readonly Card discordCard;
        readonly TextLine discordLabel, clientName, clientStateText;
        readonly StatusDot clientDot;
        readonly FlatButton changeButton, launchButton;
        readonly ToggleSwitch shortcutToggle;
        readonly ContextMenuStrip clientMenu;

        // Errors, bottom bar, notifications
        readonly ErrorCard errorCard;
        readonly FlatButton testButton, logsButton, settingsButton;
        readonly Toast toast;
        readonly Timer timer;
        LogsWindow logsWindow;

        public MainWindow(AppController app, bool startHidden)
        {
            this.app = app;
            cfg = app.Config;
            this.startHidden = startHidden;
            Subtitle = "v" + AppSetup.AppVersion;
            Text = "Discord Proxifier";

            trayButton = TitleButton(Theme.Glyph.ToTray);
            trayButton.Click += (s, e) => app.HideToTray();
            minButton = TitleButton(Theme.Glyph.Minimize);
            minButton.Click += (s, e) => WindowState = FormWindowState.Minimized;

            // Status card
            statusCard = new Card();
            Controls.Add(statusCard);
            dot = new StatusDot { BackColor = Theme.Surface };
            statusTitle = new TextLine { Font = Theme.Status, BackColor = Theme.Surface, Height = Theme.S(30) };
            statusSub = new TextLine { Font = Theme.Small, ForeColor = Theme.TextMuted, BackColor = Theme.Surface };
            connectButton = new FlatButton { Primary = true, BackColor = Theme.Surface, Height = Theme.S(32) };
            connectButton.Click += async (s, e) => await app.ConnectButtonClicked();
            progress = new ProgressLine { BackColor = Theme.Surface };
            statusCard.Controls.AddRange(new Control[] { dot, statusTitle, statusSub, connectButton, progress });

            // Route switch
            routeLabel = new TextLine { Font = Theme.Small, ForeColor = Theme.TextMuted, Height = Theme.S(18) };
            route = new SegmentSwitch
            {
                Glyphs = new[] { Theme.Glyph.Bolt, Theme.Glyph.Cloud, Theme.Glyph.Globe },
                Labels = ProxyModes.All.Select(ProxyModes.Display).ToArray()
            };
            route.SetIndex(ProxyModes.Index(app.Mode), false);
            route.UserChanged += async i => await app.ModeChosen(ProxyModes.All[i]);
            routeHint = new TextLine { Font = Theme.Small, ForeColor = Theme.TextFaint, Height = Theme.S(18) };
            autoConnect = new ToggleSwitch { Checked = cfg.AutoConnect };
            autoConnect.CheckedChanged += (s, e) =>
            {
                if (autoConnect.Checked != cfg.AutoConnect)
                    app.SetAutoConnect(autoConnect.Checked);
            };
            autoRoute = new ToggleSwitch { Checked = cfg.AutoRoute };
            autoRoute.CheckedChanged += (s, e) =>
            {
                if (autoRoute.Checked != cfg.AutoRoute)
                    app.SetAutoRoute(autoRoute.Checked);
            };
            Controls.AddRange(new Control[] { routeLabel, route, routeHint, autoRoute, autoConnect });

            // Discord card
            discordCard = new Card();
            Controls.Add(discordCard);
            discordLabel = new TextLine { Font = Theme.Small, ForeColor = Theme.TextMuted, BackColor = Theme.Surface, Height = Theme.S(18) };
            clientName = new TextLine { Font = Theme.Strong, BackColor = Theme.Surface, Height = Theme.S(22) };
            clientDot = new StatusDot { BackColor = Theme.Surface, Size = new Size(Theme.S(10), Theme.S(10)) };
            clientStateText = new TextLine { Font = Theme.Small, ForeColor = Theme.TextMuted, BackColor = Theme.Surface, Height = Theme.S(18) };
            changeButton = new FlatButton { Glyph = Theme.Glyph.ChevronDown, Font = Theme.Small, BackColor = Theme.Surface, Height = Theme.S(28) };
            changeButton.Click += (s, e) => ShowClientMenu();
            launchButton = new FlatButton { Glyph = Theme.Glyph.Play, Primary = true, BackColor = Theme.Surface, Height = Theme.S(32) };
            launchButton.Click += async (s, e) => await app.LaunchSelected();
            shortcutToggle = new ToggleSwitch { BackColor = Theme.Surface, Font = Theme.Small };
            shortcutToggle.CheckedChanged += (s, e) =>
            {
                if (app.Selected != null && shortcutToggle.Checked != app.Selected.ShortcutProxied)
                    app.SetShortcutProxy(shortcutToggle.Checked);
            };
            discordCard.Controls.AddRange(new Control[] { discordLabel, clientName, clientDot, clientStateText, changeButton, launchButton, shortcutToggle });
            clientMenu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = true, Font = Theme.Body };

            // Error card
            errorCard = new ErrorCard();
            errorCard.ActionLabel = app.ActionLabel;
            errorCard.HeightChanging += DoLayout;
            errorCard.ActionClicked += async a => await app.RunAction(a);
            errorCard.Dismissed += () => app.OnCardDismissed(errorCard.LastDismissed);
            Controls.Add(errorCard);

            // Bottom bar
            testButton = new FlatButton { Glyph = Theme.Glyph.Test, Font = Theme.Small };
            testButton.Click += async (s, e) => await app.RunTest();
            logsButton = new FlatButton { Glyph = Theme.Glyph.Logs, Font = Theme.Small };
            logsButton.Click += (s, e) => ShowLogs();
            settingsButton = new FlatButton { Glyph = Theme.Glyph.Settings, Font = Theme.Small };
            settingsButton.Click += (s, e) => app.OpenSettings(null);
            Controls.AddRange(new Control[] { testButton, logsButton, settingsButton });

            toast = new Toast();
            Controls.Add(toast);

            timer = new Timer { Interval = 500 };
            timer.Tick += (s, e) => app.Tick();

            L.Changed += ApplyTexts;
            ApplyTexts();
        }

        protected override FlatButton[] TitleButtons => new[] { CloseButton, minButton, trayButton };

        // ---------------- lifecycle ----------------

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !IsHandleCreated)
            {
                // Started with Windows (--tray): live in the tray without flashing a window.
                CreateHandle();
                value = false;
                startHidden = false;
                BeginInvoke(new Action(async () => await Init()));
            }
            base.SetVisibleCore(value);
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await Init();
        }

        async Task Init()
        {
            if (initialized)
                return;
            initialized = true;
            app.Attach(this);
            timer.Start();
            await app.Init();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            app.OnViewClosing();
            base.OnFormClosing(e);
        }

        // ---------------- IAppView ----------------

        public bool WindowVisible => Visible;
        public bool IsShownToUser => Visible && WindowState != FormWindowState.Minimized;
        public AppError Card => errorCard.Visible ? errorCard.Error : null;

        public void ShowCard(AppError e)
        {
            errorCard.ShowError(e);
            DoLayout();
        }

        public void HideCard(bool notify)
        {
            errorCard.Collapse(notify);
        }

        public void ShowToast(NoteKind kind, string title, string sub)
        {
            Toast.Kind k = kind == NoteKind.Success ? Toast.Kind.Success : kind == NoteKind.Warning ? Toast.Kind.Warning : kind == NoteKind.Error ? Toast.Kind.Error : Toast.Kind.Info;
            toast.ShowToast(k, title, sub);
        }

        public void ModeChanged(bool animate)
        {
            route.SetIndex(ProxyModes.Index(app.Mode), animate);
        }

        public bool Confirm(string title, string body)
        {
            return MessageBox.Show(this, body, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        public string PickFolder(string description, string startPath)
        {
            using (var dlg = new FolderBrowserDialog { Description = description, ShowNewFolderButton = false, SelectedPath = startPath })
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
        }

        public void ShowSettings(string focus)
        {
            using (var w = new SettingsWindow(cfg, app.Logger, focus)
            {
                TorVersion = app.TorVersion,
                TorUpdate = app.UpdateTor,
                AppUpdate = app.CheckAppUpdate,
                Repair = app.OpenRepair,
                Report = app.CreateReport,
                SwitchAppearance = app.SwitchAppearance
            })
                w.ShowDialog(this);
        }

        public async Task ShowRepairAsync(Func<IRepairView, Task> run)
        {
            using (var w = new RepairWindow())
            {
                w.ReportClicked = async b =>
                {
                    b.Enabled = false;
                    string result = await app.CreateReport();
                    b.Text = result;
                    b.Width = b.PreferredWidth();
                };
                w.Shown += async (s, e) => await run(w);
                w.ShowDialog(this);
            }
            await Task.CompletedTask;
        }

        public void ShowLogs()
        {
            if (logsWindow == null || logsWindow.IsDisposed)
            {
                logsWindow = new LogsWindow(app.Logger);
                logsWindow.Show();
            }
            else
            {
                logsWindow.Activate();
            }
        }

        public void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        public void HideWindow()
        {
            Hide();
        }

        public void Shutdown()
        {
            Application.Exit();
        }

        // ---------------- layout and texts ----------------

        void ApplyTexts()
        {
            routeLabel.Text = L.T("route.label");
            route.Labels = ProxyModes.All.Select(ProxyModes.Display).ToArray();
            route.Invalidate();
            autoConnect.Text = L.T("autoconnect");
            autoRoute.Text = L.T("autoroute");
            discordLabel.Text = L.T("client.label");
            changeButton.Text = L.T("client.change");
            shortcutToggle.Text = L.T("client.shortcut");
            testButton.Text = L.T("btn.test");
            logsButton.Text = L.T("btn.logs");
            settingsButton.Text = L.T("btn.settings");
            new ToolTip().SetToolTip(trayButton, L.T("tip.tray"));
            if (errorCard.Visible)
                errorCard.Relayout();
            RefreshStatus();
            RefreshClient();
            DoLayout();
        }

        void DoLayout()
        {
            int W = Theme.S(384);
            int pad = Theme.S(14);
            int inner = W - pad * 2;
            int y = TitleHeight + Theme.S(12);

            // Status card
            int cp = Theme.S(16);
            statusCard.SetBounds(pad, y, inner, Theme.S(100));
            connectButton.Width = Math.Max(Theme.S(104), connectButton.PreferredWidth());
            connectButton.Location = new Point(inner - cp - connectButton.Width, Theme.S(16));
            dot.Location = new Point(cp, Theme.S(25));
            statusTitle.SetBounds(cp + Theme.S(20), Theme.S(16), connectButton.Left - cp - Theme.S(28), Theme.S(30));
            statusSub.SetBounds(cp, Theme.S(54), inner - cp * 2, Theme.S(20));
            progress.SetBounds(cp, Theme.S(84), inner - cp * 2, Theme.S(3));
            y += Theme.S(100) + Theme.S(14);

            // Route
            routeLabel.SetBounds(pad + Theme.S(2), y, inner, Theme.S(18));
            y += Theme.S(22);
            route.SetBounds(pad, y, inner, Theme.S(42));
            y += Theme.S(46);
            routeHint.SetBounds(pad + Theme.S(2), y, inner, Theme.S(18));
            y += Theme.S(26);
            autoRoute.SetBounds(pad, y, inner, Theme.S(24));
            y += Theme.S(24) + Theme.S(8);
            autoConnect.SetBounds(pad, y, inner, Theme.S(24));
            y += Theme.S(24) + Theme.S(14);

            // Discord card
            int dh = Theme.S(124);
            discordCard.SetBounds(pad, y, inner, dh);
            discordLabel.SetBounds(cp, Theme.S(12), Theme.S(200), Theme.S(18));
            changeButton.Width = changeButton.PreferredWidth();
            changeButton.Location = new Point(inner - cp - changeButton.Width, Theme.S(14));
            clientName.SetBounds(cp, Theme.S(30), changeButton.Left - cp - Theme.S(8), Theme.S(22));
            clientDot.Location = new Point(cp, Theme.S(58));
            clientStateText.SetBounds(cp + Theme.S(16), Theme.S(54), inner - cp * 2 - Theme.S(16), Theme.S(18));
            launchButton.Width = Math.Max(Theme.S(110), launchButton.PreferredWidth());
            launchButton.Location = new Point(cp, Theme.S(80));
            shortcutToggle.SetBounds(launchButton.Right + Theme.S(14), Theme.S(84), inner - launchButton.Right - Theme.S(14) - cp, Theme.S(24));
            y += dh;

            // Error card (animated height)
            errorCard.Width = inner;
            errorCard.Left = pad;
            if (errorCard.Visible)
            {
                y += Theme.S(12);
                errorCard.Top = y;
                y += errorCard.CurrentHeight;
            }

            // Bottom bar
            y += Theme.S(14);
            int bh = Theme.S(32);
            foreach (FlatButton b in new[] { testButton, logsButton, settingsButton })
            {
                b.Height = bh;
                b.Width = b.PreferredWidth() + Theme.S(6);
            }
            testButton.Location = new Point(pad, y);
            logsButton.Location = new Point(testButton.Right + Theme.S(8), y);
            settingsButton.Location = new Point(pad + inner - settingsButton.Width, y);
            y += bh + pad;

            ClientSize = new Size(W, y);
            LayoutTitleButtons();
        }

        // ---------------- status ----------------

        public void RefreshStatus()
        {
            StatusInfo s = app.Status;
            switch (s.Kind)
            {
                case StatusKind.Connecting:
                    dot.Set(Theme.Warning, true);
                    break;
                case StatusKind.Connected:
                    dot.Set(Theme.Success, false);
                    break;
                case StatusKind.Unhealthy:
                case StatusKind.Failed:
                    dot.Set(Theme.Danger, false);
                    break;
                default:
                    dot.Set(Theme.Idle, false);
                    break;
            }
            progress.Set(s.Progress);
            connectButton.Text = s.ButtonText;
            connectButton.Primary = s.ButtonPrimary;
            statusTitle.Text = s.Title;
            statusSub.Text = s.Sub;
            route.Busy = s.Kind == StatusKind.Connecting;
            routeHint.Text = s.RouteHint;
            if (autoRoute.Checked != cfg.AutoRoute)
                autoRoute.Checked = cfg.AutoRoute;
            testButton.Enabled = !app.Testing;
            int want = Math.Max(Theme.S(104), connectButton.PreferredWidth());
            if (connectButton.Width != want)
                DoLayout();
            connectButton.Invalidate();
        }

        // ---------------- Discord client ----------------

        void ShowClientMenu()
        {
            clientMenu.Items.Clear();
            foreach (DiscordClient c in app.Clients)
            {
                DiscordClient cc = c;
                var item = new ToolStripMenuItem(c.DisplayName) { Checked = c == app.Selected, ToolTipText = c.RootDir };
                item.Click += (s, e) => app.Select(cc);
                clientMenu.Items.Add(item);
            }
            if (app.Clients.Count > 0)
                clientMenu.Items.Add(new ToolStripSeparator());
            var rescan = new ToolStripMenuItem(L.T("client.rescan"));
            rescan.Click += (s, e) => app.Rescan();
            var browse = new ToolStripMenuItem(L.T("client.browse"));
            browse.Click += (s, e) => app.BrowseForDiscord();
            clientMenu.Items.Add(rescan);
            clientMenu.Items.Add(browse);
            clientMenu.Show(changeButton, new Point(0, changeButton.Height + Theme.S(2)));
        }

        public void RefreshClient()
        {
            DiscordClient selected = app.Selected;
            launchButton.Enabled = !app.Launching;
            if (selected == null)
            {
                clientName.Text = L.T("client.none");
                clientStateText.Text = "";
                clientDot.Visible = false;
                launchButton.Text = L.T("client.launch");
                shortcutToggle.Visible = false;
                return;
            }
            clientName.Text = selected.DisplayName;
            clientDot.Visible = true;
            switch (app.ClientState)
            {
                case ClientState.RunningWithProxy:
                    clientDot.Set(Theme.Success, false);
                    clientStateText.Text = L.T("client.running.proxy");
                    launchButton.Text = L.T("client.launch");
                    break;
                case ClientState.RunningDirect:
                    clientDot.Set(Theme.Warning, false);
                    clientStateText.Text = L.T("client.running.direct");
                    launchButton.Text = L.T("client.restart");
                    break;
                default:
                    clientDot.Set(Theme.Idle, false);
                    clientStateText.Text = L.T("client.stopped");
                    launchButton.Text = L.T("client.launch");
                    break;
            }
            shortcutToggle.Visible = selected.ShortcutPath != null;
            shortcutToggle.Checked = selected.ShortcutProxied;
            int want = Math.Max(Theme.S(110), launchButton.PreferredWidth());
            if (launchButton.Width != want)
                DoLayout();
        }
    }
}
