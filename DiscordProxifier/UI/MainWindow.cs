using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    public class MainWindow : BorderlessWindow
    {
        readonly Configuration cfg;
        readonly Logger logger;
        readonly ProxyManager proxy;
        readonly DiscordClients discord;
        readonly Updater updater;
        List<DiscordClient> clients = new List<DiscordClient>();
        DiscordClient selected;
        ClientState clientState = ClientState.Stopped;
        ProxyMode mode;
        bool startHidden;
        bool initialized;
        bool exiting;
        int tick;
        bool polling;

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
        readonly ToggleSwitch autoConnect;

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
        readonly NotifyIcon tray;
        readonly ContextMenuStrip trayMenu;
        readonly ToolStripMenuItem trayStatus, trayConnect, traySwitch, trayLaunch, trayShow, trayExit;
        readonly Timer timer;
        LogsWindow logsWindow;

        public MainWindow(Configuration cfg, Logger logger, bool startHidden)
        {
            this.cfg = cfg;
            this.logger = logger;
            this.startHidden = startHidden;
            mode = ProxyModes.Parse(cfg.Mode);
            proxy = new ProxyManager(logger, AppSetup.DataDir);
            proxy.FellBackToTor += () => Notify(Toast.Kind.Warning, L.T("toast.fallback"), null);
            discord = new DiscordClients(logger, AppSetup.DataDir);
            updater = new Updater(logger, AppSetup.DataDir);
            Subtitle = "v" + AppSetup.AppVersion;
            Text = "Discord Proxifier";

            trayButton = TitleButton(Theme.Glyph.ToTray);
            trayButton.Click += (s, e) => HideToTray();
            minButton = TitleButton(Theme.Glyph.Minimize);
            minButton.Click += (s, e) => WindowState = FormWindowState.Minimized;

            // Status card
            statusCard = new Card();
            Controls.Add(statusCard);
            dot = new StatusDot { BackColor = Theme.Surface };
            statusTitle = new TextLine { Font = Theme.Status, BackColor = Theme.Surface, Height = Theme.S(30) };
            statusSub = new TextLine { Font = Theme.Small, ForeColor = Theme.TextMuted, BackColor = Theme.Surface };
            connectButton = new FlatButton { Primary = true, BackColor = Theme.Surface, Height = Theme.S(32) };
            connectButton.Click += async (s, e) => await ConnectButtonClicked();
            progress = new ProgressLine { BackColor = Theme.Surface };
            statusCard.Controls.AddRange(new Control[] { dot, statusTitle, statusSub, connectButton, progress });

            // Route switch
            routeLabel = new TextLine { Font = Theme.Small, ForeColor = Theme.TextMuted, Height = Theme.S(18) };
            route = new SegmentSwitch
            {
                Glyphs = new[] { Theme.Glyph.Bolt, Theme.Glyph.Cloud, Theme.Glyph.Globe },
                Labels = ProxyModes.All.Select(ProxyModes.Display).ToArray()
            };
            route.SetIndex(ProxyModes.Index(mode), false);
            route.UserChanged += async i => await ModeChosen(ProxyModes.All[i]);
            routeHint = new TextLine { Font = Theme.Small, ForeColor = Theme.TextFaint, Height = Theme.S(18) };
            autoConnect = new ToggleSwitch { Checked = cfg.AutoConnect };
            autoConnect.CheckedChanged += (s, e) =>
            {
                cfg.AutoConnect = autoConnect.Checked;
                cfg.Save();
            };
            Controls.AddRange(new Control[] { routeLabel, route, routeHint, autoConnect });

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
            launchButton.Click += async (s, e) => await LaunchSelected();
            shortcutToggle = new ToggleSwitch { BackColor = Theme.Surface, Font = Theme.Small };
            shortcutToggle.CheckedChanged += (s, e) =>
            {
                if (selected != null)
                    discord.SetShortcutProxy(selected, shortcutToggle.Checked, cfg.GostPort);
            };
            discordCard.Controls.AddRange(new Control[] { discordLabel, clientName, clientDot, clientStateText, changeButton, launchButton, shortcutToggle });
            clientMenu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = true, Font = Theme.Body };

            // Error card
            errorCard = new ErrorCard();
            errorCard.ActionLabel = a => a == ErrorAction.SwitchMode
                ? L.F("action.SwitchMode", ProxyModes.Display(ProxyModes.Next(mode)))
                : L.T("action." + a);
            errorCard.HeightChanging += DoLayout;
            errorCard.ActionClicked += async a => await RunAction(a);
            errorCard.Dismissed += () =>
            {
                proxy.ClearError();
                RefreshStatus();
            };
            Controls.Add(errorCard);

            // Bottom bar
            testButton = new FlatButton { Glyph = Theme.Glyph.Test, Font = Theme.Small };
            testButton.Click += async (s, e) => await RunTest();
            logsButton = new FlatButton { Glyph = Theme.Glyph.Logs, Font = Theme.Small };
            logsButton.Click += (s, e) => OpenLogs();
            settingsButton = new FlatButton { Glyph = Theme.Glyph.Settings, Font = Theme.Small };
            settingsButton.Click += (s, e) => OpenSettings(null);
            Controls.AddRange(new Control[] { testButton, logsButton, settingsButton });

            toast = new Toast();
            Controls.Add(toast);

            // Tray
            trayStatus = new ToolStripMenuItem { Enabled = false };
            trayConnect = new ToolStripMenuItem();
            trayConnect.Click += async (s, e) => await ConnectButtonClicked();
            traySwitch = new ToolStripMenuItem();
            traySwitch.Click += async (s, e) =>
            {
                ProxyMode target = ProxyModes.Next(mode);
                route.SetIndex(ProxyModes.Index(target), true);
                await ModeChosen(target);
            };
            trayLaunch = new ToolStripMenuItem();
            trayLaunch.Click += async (s, e) => await LaunchSelected();
            trayShow = new ToolStripMenuItem();
            trayShow.Click += (s, e) => ShowFromTray();
            trayExit = new ToolStripMenuItem();
            trayExit.Click += (s, e) => ExitApp();
            trayMenu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Body };
            trayMenu.Items.AddRange(new ToolStripItem[] { trayStatus, new ToolStripSeparator(), trayConnect, traySwitch, trayLaunch, new ToolStripSeparator(), trayShow, trayExit });
            tray = new NotifyIcon
            {
                Icon = new Icon(AppIcon, SystemInformation.SmallIconSize),
                Text = "Discord Proxifier",
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            tray.DoubleClick += (s, e) => ShowFromTray();

            timer = new Timer { Interval = 500 };
            timer.Tick += (s, e) => OnTimer();

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
            var clock = Stopwatch.StartNew();
            logger.Log($"Discord Proxifier {AppSetup.AppVersion} starting");
            proxy.KillOrphans();
            Rescan();
            timer.Start();
            RefreshStatus();
            AppSetup.SetAutoStart(cfg.OpenAtStartup, logger);

            // Connect right away with the last used mode; nothing below waits on the network first.
            List<string> missing = await AppSetup.PrepareLocalDataAsync(logger);
            Task connecting = cfg.AutoConnect && missing.Count == 0 ? Connect() : null;
            logger.Log($"Startup ready in {clock.ElapsedMilliseconds} ms");

            await CheckUpdatesAsync(missing);
            if (connecting != null)
                await connecting;
            else if (cfg.AutoConnect && !proxy.IsRunning && !proxy.IsStarting && (await AppSetup.PrepareLocalDataAsync(logger)).Count == 0)
                await Connect();
        }

        // Background update check against our own repo: fills in missing files, updates
        // version.dll automatically, and announces Tor / app updates (installed from Settings).
        async Task CheckUpdatesAsync(List<string> missing)
        {
            Manifest m = await updater.FetchManifestAsync();
            if (m == null)
            {
                if (missing.Count > 0)
                    ShowError(missing.Contains("x64/version.dll") ? AppError.For("DP-502") : AppError.For("DP-103", missing[0]));
                return;
            }
            if (missing.Count > 0)
                await updater.EnsureFilesAsync(m);
            bool dllUpdated = await updater.UpdateDllAsync(m);
            if (!AppSetup.HasDll)
                ShowError(AppError.For("DP-502"));

            bool torUpdate = await Task.Run(() => updater.TorUpdateAvailable(m));
            if (Updater.IsNewer(m.AppVersion, AppSetup.AppVersion))
                Notify(Toast.Kind.Info, L.F("toast.update.app", m.AppVersion), L.T("toast.update.sub"));
            else if (torUpdate)
                Notify(Toast.Kind.Info, L.T("toast.update.tor"), L.T("toast.update.sub"));
            else if (dllUpdated)
                Notify(Toast.Kind.Info, L.F("toast.dll.updated", updater.InstalledDllVersion), null);
        }

        // Settings → Update Tor. Stops Tor if it is the active backend, swaps the files, reconnects.
        async Task<string> UpdateTor()
        {
            if (!UpdateSource.Configured)
                return L.T("update.notconfigured");
            Manifest m = await updater.FetchManifestAsync();
            if (m == null)
                return L.T("update.offline");
            if (!await Task.Run(() => updater.TorUpdateAvailable(m)))
                return L.T("update.latest");
            string version = m.Get("Tor.zip").Version;
            string zip = await updater.DownloadTorAsync(m);
            if (zip == null)
                return L.T("update.failed");
            bool torActive = (proxy.IsRunning || proxy.IsStarting) && mode == ProxyMode.Tor;
            if (torActive)
                await proxy.StopAsync();
            bool ok = await Task.Run(() => updater.InstallTorZip(zip, version));
            if (torActive)
                _ = Connect();
            RefreshStatus();
            return ok ? L.F("update.done", version) : L.T("update.failed");
        }

        // Settings → Check for updates. Opens the download page when a newer build exists.
        async Task<string> CheckAppUpdate()
        {
            if (!UpdateSource.Configured)
                return L.T("update.notconfigured");
            Manifest m = await updater.FetchManifestAsync();
            if (m == null)
                return L.T("update.offline");
            bool dll = await updater.UpdateDllAsync(m);
            if (!Updater.IsNewer(m.AppVersion, AppSetup.AppVersion))
                return dll ? L.F("toast.dll.updated", updater.InstalledDllVersion) : L.T("update.latest");
            try
            {
                Process.Start(string.IsNullOrEmpty(m.AppDownload) ? UpdateSource.ReleasesPage : m.AppDownload);
            }
            catch { }
            return L.F("update.available", m.AppVersion);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting)
            {
                exiting = true;
                proxy.StopNow();
                tray.Visible = false;
            }
            base.OnFormClosing(e);
        }

        void ExitApp()
        {
            exiting = true;
            proxy.StopNow();
            tray.Visible = false;
            Application.Exit();
        }

        void HideToTray()
        {
            Hide();
            Notify(Toast.Kind.Info, L.T("toast.tray"), null);
        }

        void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        // ---------------- layout and texts ----------------

        void ApplyTexts()
        {
            routeLabel.Text = L.T("route.label");
            route.Labels = ProxyModes.All.Select(ProxyModes.Display).ToArray();
            route.Invalidate();
            autoConnect.Text = L.T("autoconnect");
            discordLabel.Text = L.T("client.label");
            changeButton.Text = L.T("client.change");
            shortcutToggle.Text = L.T("client.shortcut");
            testButton.Text = L.T("btn.test");
            logsButton.Text = L.T("btn.logs");
            settingsButton.Text = L.T("btn.settings");
            trayShow.Text = L.T("tray.show");
            trayLaunch.Text = L.T("tray.launch");
            trayExit.Text = L.T("tray.exit");
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

        void OnTimer()
        {
            RefreshStatus();
            if (++tick % 6 == 0)
                PollClient();
        }

        void RefreshStatus()
        {
            string modeName = ProxyModes.Display(mode);
            AppError err = errorCard.Visible ? errorCard.Error : null;
            string title, sub;
            if (proxy.IsStarting)
            {
                dot.Set(Theme.Warning, true);
                title = L.T("status.connecting");
                sub = proxy.Progress;
                progress.Set(proxy.Percent >= 0 ? proxy.Percent / 100f : -1);
                connectButton.Text = L.T("btn.cancel");
                connectButton.Primary = false;
            }
            else if (proxy.IsRunning)
            {
                string dead = proxy.DeadPart;
                if (dead != null)
                {
                    dot.Set(Theme.Danger, false);
                    title = L.T("status.error");
                    sub = L.F("status.sub.unhealthy", dead);
                }
                else
                {
                    dot.Set(Theme.Success, false);
                    title = L.T("status.connected");
                    sub = L.F("status.sub.connected", proxy.LastLatencyMs, modeName);
                }
                progress.Set(null);
                connectButton.Text = L.T("btn.disconnect");
                connectButton.Primary = false;
            }
            else if (err != null)
            {
                dot.Set(Theme.Danger, false);
                title = L.T("status.error");
                sub = err.Title;
                progress.Set(null);
                connectButton.Text = L.T("btn.connect");
                connectButton.Primary = true;
            }
            else
            {
                dot.Set(Theme.Idle, false);
                title = L.T("status.down");
                sub = L.T("status.sub.down");
                progress.Set(null);
                connectButton.Text = L.T("btn.connect");
                connectButton.Primary = true;
            }
            statusTitle.Text = title;
            statusSub.Text = sub;
            route.Busy = proxy.IsStarting;
            routeHint.Text = L.T(mode == ProxyMode.Warp ? "route.warp.hint" : mode == ProxyMode.Direct ? "route.direct.hint" : "route.tor.hint");
            int want = Math.Max(Theme.S(104), connectButton.PreferredWidth());
            if (connectButton.Width != want)
                DoLayout();
            connectButton.Invalidate();

            trayStatus.Text = title + " · " + modeName;
            trayConnect.Text = connectButton.Text;
            traySwitch.Text = L.F("tray.switch", ProxyModes.Display(ProxyModes.Next(mode)));
            traySwitch.Enabled = !proxy.IsStarting;
            string tip = "Discord Proxifier · " + title;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void Notify(Toast.Kind kind, string title, string sub)
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                toast.ShowToast(kind, title, sub);
            }
            else
            {
                ToolTipIcon icon = kind == Toast.Kind.Error ? ToolTipIcon.Error : kind == Toast.Kind.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
                tray.ShowBalloonTip(3000, title, string.IsNullOrEmpty(sub) ? " " : sub, icon);
            }
        }

        void ShowError(AppError e)
        {
            errorCard.ShowError(e);
            DoLayout();
            RefreshStatus();
            if (!Visible || WindowState == FormWindowState.Minimized)
                tray.ShowBalloonTip(5000, e.Code + " · " + e.Title, e.Description, ToolTipIcon.Error);
        }

        // ---------------- proxy ----------------

        bool EnsureWarpConsent()
        {
            if (cfg.WarpTosAccepted || WarpBackend.HasAccount(AppSetup.DataDir))
                return true;
            DialogResult r = MessageBox.Show(this, L.T("warp.consent.body"), L.T("warp.consent.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes)
                return false;
            cfg.WarpTosAccepted = true;
            cfg.Save();
            return true;
        }

        void SetMode(ProxyMode m, bool animate)
        {
            mode = m;
            cfg.Mode = ProxyModes.ToConfig(m);
            cfg.Save();
            route.SetIndex(ProxyModes.Index(m), animate);
        }

        async Task ConnectButtonClicked()
        {
            if (proxy.IsStarting || proxy.IsRunning)
            {
                await proxy.StopAsync();
                if (!exiting)
                {
                    RefreshStatus();
                    Notify(Toast.Kind.Info, L.T("toast.disconnected"), null);
                }
                return;
            }
            await Connect();
        }

        async Task Connect()
        {
            if (mode == ProxyMode.Warp && !EnsureWarpConsent())
            {
                SetMode(ProxyMode.Tor, true);
                Notify(Toast.Kind.Warning, L.T("warp.consent.declined"), null);
            }
            errorCard.Collapse(false);
            Task<bool> start = proxy.StartAsync(mode, cfg);
            RefreshStatus();
            bool ok = await start;
            AfterStart(ok, false);
            if (ok && cfg.LaunchDiscordAfterConnect && selected != null && clientState != ClientState.RunningWithProxy)
                await LaunchSelected();
        }

        async Task ModeChosen(ProxyMode target)
        {
            if (target == mode)
                return;
            if (target == ProxyMode.Warp && !EnsureWarpConsent())
            {
                route.SetIndex(ProxyModes.Index(mode), true);
                Notify(Toast.Kind.Warning, L.T("warp.consent.declined"), null);
                return;
            }
            SetMode(target, true);
            RefreshStatus();
            if (!proxy.IsRunning && !proxy.IsStarting)
                return;
            errorCard.Collapse(false);
            Task<bool> sw = proxy.SwitchModeAsync(target, cfg);
            RefreshStatus();
            bool ok = await sw;
            AfterStart(ok, true);
        }

        void AfterStart(bool ok, bool switched)
        {
            if (exiting)
                return;
            if (proxy.ActiveMode.HasValue && proxy.ActiveMode.Value != mode)
                SetMode(proxy.ActiveMode.Value, true); // WARP -> Tor fallback
            RefreshStatus();
            if (ok)
            {
                string m = ProxyModes.Display(mode);
                if (switched)
                    Notify(Toast.Kind.Success, L.F("toast.mode", m), L.T("toast.mode.sub"));
                else
                    Notify(Toast.Kind.Success, L.F("toast.connected", m), L.F("status.sub.connected", proxy.LastLatencyMs, m));
            }
            else if (proxy.LastError != null)
            {
                ShowError(proxy.LastError);
            }
        }

        async Task RunAction(ErrorAction a)
        {
            switch (a)
            {
                case ErrorAction.Retry:
                    await Connect();
                    break;
                case ErrorAction.SwitchMode:
                    ProxyMode target = ProxyModes.Next(mode);
                    if (target == ProxyMode.Warp && !EnsureWarpConsent())
                        return;
                    SetMode(target, true);
                    await Connect();
                    break;
                case ErrorAction.OpenSettings:
                    OpenSettings(null);
                    break;
                case ErrorAction.AddBridges:
                    OpenSettings("bridges");
                    break;
                case ErrorAction.ChangeEndpoint:
                    OpenSettings("endpoint");
                    break;
                case ErrorAction.KillLeftovers:
                    int n = 0;
                    foreach (Process p in proxy.FindForeignLeftovers())
                    {
                        try
                        {
                            logger.Log($"Closing leftover {p.ProcessName} (pid {p.Id})", LogType.WARNING);
                            p.Kill();
                            p.WaitForExit(3000);
                            n++;
                        }
                        catch (Exception ex)
                        {
                            logger.Log($"Could not close pid {p.Id}: {ex.Message}", LogType.ERROR);
                        }
                        finally
                        {
                            p.Dispose();
                        }
                    }
                    Notify(Toast.Kind.Info, L.F("toast.leftovers", n), null);
                    await Connect();
                    break;
                case ErrorAction.OpenLogs:
                    OpenLogs();
                    break;
                case ErrorAction.OpenFolder:
                    Process.Start("explorer.exe", "\"" + AppContext.BaseDirectory + "\"");
                    break;
                case ErrorAction.BrowseDiscord:
                    BrowseForDiscord();
                    break;
                case ErrorAction.RescanDiscord:
                    Rescan();
                    if (selected != null)
                        errorCard.Collapse();
                    break;
                case ErrorAction.RetryDll:
                    Manifest m = await updater.FetchManifestAsync();
                    if (m != null)
                        await updater.EnsureFilesAsync(m);
                    if (AppSetup.HasDll)
                        errorCard.Collapse();
                    else
                        Notify(Toast.Kind.Error, L.T("update.offline"), null);
                    break;
            }
        }

        async Task RunTest()
        {
            if (!proxy.IsRunning)
            {
                Notify(Toast.Kind.Info, L.T("toast.notconnected"), null);
                return;
            }
            testButton.Enabled = false;
            List<TestItem> items = await proxy.TestAsync();
            testButton.Enabled = true;
            Func<TestItem, string> fmt = i => i.Reachable ? i.Result.Ms + " ms" : (i.Result.Status > 0 ? "HTTP " + i.Result.Status : "✕");
            string detail = string.Join(" · ", items.Select(i => L.T("test." + i.Name) + " " + fmt(i)));
            TestItem bad = items.FirstOrDefault(i => !i.Reachable);
            if (bad == null)
            {
                Notify(Toast.Kind.Success, L.T("toast.test.ok"), detail);
                return;
            }
            Notify(Toast.Kind.Error, L.T("toast.test.fail"), detail);
            int st = bad.Result.Status;
            ShowError(st == 403 || st == 429
                ? AppError.For("DP-402", "HTTP " + st)
                : AppError.For("DP-401", st > 0 ? "HTTP " + st : bad.Name));
        }

        // ---------------- Discord client ----------------

        void Rescan()
        {
            clients = discord.Scan(cfg.CustomClientRoots);
            logger.Log($"Discord installs found: {(clients.Count == 0 ? "none" : string.Join(", ", clients.Select(c => c.DisplayName)))}");
            selected = clients.FirstOrDefault(c => c.Key == cfg.SelectedClient) ?? clients.FirstOrDefault();
            if (cfg.AutoUpdateProxified)
                discord.FixProxifiedShortcuts(clients, cfg.GostPort);
            clientState = ClientState.Stopped;
            RefreshClient();
            PollClient();
        }

        void Select(DiscordClient c)
        {
            selected = c;
            cfg.SelectedClient = c.Key;
            cfg.Save();
            RefreshClient();
            PollClient();
        }

        void ShowClientMenu()
        {
            clientMenu.Items.Clear();
            foreach (DiscordClient c in clients)
            {
                DiscordClient cc = c;
                var item = new ToolStripMenuItem(c.DisplayName) { Checked = c == selected, ToolTipText = c.RootDir };
                item.Click += (s, e) => Select(cc);
                clientMenu.Items.Add(item);
            }
            if (clients.Count > 0)
                clientMenu.Items.Add(new ToolStripSeparator());
            var rescan = new ToolStripMenuItem(L.T("client.rescan"));
            rescan.Click += (s, e) => Rescan();
            var browse = new ToolStripMenuItem(L.T("client.browse"));
            browse.Click += (s, e) => BrowseForDiscord();
            clientMenu.Items.Add(rescan);
            clientMenu.Items.Add(browse);
            clientMenu.Show(changeButton, new Point(0, changeButton.Height + Theme.S(2)));
        }

        void BrowseForDiscord()
        {
            using (var dlg = new FolderBrowserDialog { Description = L.T("client.browse.desc"), ShowNewFolderButton = false })
            {
                dlg.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
                DiscordClient c = DiscordClients.FromFolder(dlg.SelectedPath);
                if (c == null)
                {
                    Notify(Toast.Kind.Error, L.T("client.browse.invalid"), dlg.SelectedPath);
                    return;
                }
                if (!cfg.CustomClientRoots.Contains(c.RootDir, StringComparer.OrdinalIgnoreCase))
                    cfg.CustomClientRoots = cfg.CustomClientRoots.Concat(new[] { c.RootDir }).ToArray();
                cfg.SelectedClient = c.Key;
                cfg.Save();
                Rescan();
                if (selected != null && errorCard.Visible && errorCard.Error.Code == "DP-501")
                    errorCard.Collapse();
            }
        }

        void RefreshClient()
        {
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
            switch (clientState)
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

        // WMI is slow-ish, so the running state is read off the UI thread every few seconds.
        void PollClient()
        {
            DiscordClient c = selected;
            if (c == null || polling)
                return;
            polling = true;
            string port = cfg.GostPort;
            Task.Run(() =>
            {
                discord.PrepareNewAppFolder(c);
                return discord.GetState(c, port);
            }).ContinueWith(t =>
            {
                polling = false;
                if (IsDisposed || c != selected)
                    return;
                if (t.Status == TaskStatus.RanToCompletion && t.Result != clientState)
                {
                    clientState = t.Result;
                    RefreshClient();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        async Task LaunchSelected()
        {
            if (selected == null)
            {
                ShowError(AppError.For("DP-501"));
                return;
            }
            if (!proxy.IsRunning)
            {
                // Discord launched before the proxy is up hangs on "Starting"; connect first.
                await Connect();
                if (!proxy.IsRunning)
                    return;
            }
            launchButton.Enabled = false;
            DiscordClient c = selected;
            string port = cfg.GostPort;
            bool restarted = false;
            string error = null;
            bool ok = await Task.Run(() => discord.Launch(c, port, out restarted, out error));
            launchButton.Enabled = true;
            if (!ok)
            {
                ShowError(AppError.For("DP-503", error));
                return;
            }
            Notify(Toast.Kind.Success, L.T(restarted ? "toast.discord.restarted" : "toast.discord.started"), L.T("toast.discord.sub"));
            await Task.Delay(4000);
            PollClient();
        }

        // ---------------- windows ----------------

        void OpenLogs()
        {
            if (logsWindow == null || logsWindow.IsDisposed)
            {
                logsWindow = new LogsWindow(logger);
                logsWindow.Show();
            }
            else
            {
                logsWindow.Activate();
            }
        }

        void OpenSettings(string focus)
        {
            Configuration before = cfg.Clone();
            using (var w = new SettingsWindow(cfg, logger, focus)
            {
                TorVersion = () => updater.InstalledTorVersion(),
                TorUpdate = UpdateTor,
                AppUpdate = CheckAppUpdate
            })
                w.ShowDialog(this);

            if (cfg.OpenAtStartup != before.OpenAtStartup)
                AppSetup.SetAutoStart(cfg.OpenAtStartup, logger);
            if (cfg.GostPort != before.GostPort && cfg.AutoUpdateProxified)
                discord.FixProxifiedShortcuts(clients, cfg.GostPort);
            if (cfg.NetworkDiffers(before) && (proxy.IsRunning || proxy.IsStarting))
            {
                Notify(Toast.Kind.Info, L.T("toast.settings"), null);
                var _ = Connect();
            }
            RefreshStatus();
        }
    }
}
