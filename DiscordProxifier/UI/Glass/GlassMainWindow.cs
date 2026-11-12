using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DiscordUnblocker.Glass
{
    // The Liquid Glass main window. Like the classic MainWindow it only shows the state of
    // AppController and forwards what the user does; all behaviour lives in the controller.
    //
    // Layout (top to bottom): status pane (light, large title, detail line, progress, connect),
    // the error / update card, the route switch with its hint and two options, the Discord
    // install group, and a floating toolbar. Notes drop in from the top centre.
    //
    // RefreshStatus runs every 500 ms: it compares against the last state it showed and touches
    // nothing when nothing changed, so an idle window costs no layout or rendering.
    public class GlassMainWindow : GlassWindow, IAppView
    {
        readonly AppController app;
        readonly Configuration cfg;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        // Title row
        readonly GlassButton trayButton, minButton;
        readonly TextBlock versionText;

        // Status pane
        readonly StatusLight light;
        readonly CrossfadeText statusTitle, statusSub;
        readonly GlassProgress progress;
        readonly GlassButton connect;

        // Error / update card
        readonly GlassCard card;

        // Route
        readonly TextBlock routeLabel, routeHint, autoRouteText, autoConnectText;
        readonly LiquidSegments route;
        readonly LiquidToggle autoRoute, autoConnect;

        // Discord
        readonly TextBlock clientLabel, clientName, clientStateText, shortcutText;
        readonly StatusLight clientLight;
        readonly FrameworkElement clientStateRow, shortcutRow, shortcutSeparator;
        readonly Border pickButton;
        readonly GlassButton launch;
        readonly LiquidToggle shortcut;

        // Toolbar and notes
        readonly GlassButton testButton, logsButton, settingsButton;
        readonly Grid overlay;
        readonly GlassToast toast;

        GlassLogsWindow logsWindow;
        Shown last; // what the status pane shows now

        // The state RefreshStatus last applied.
        class Shown
        {
            public StatusKind Kind;
            public string Title, Sub, Button, Hint;
            public bool Primary, Testing, AutoRoute;
            public float? Progress;

            public static Shown Of(StatusInfo s, bool testing, bool autoRoute)
            {
                return new Shown { Kind = s.Kind, Title = s.Title ?? "", Sub = s.Sub ?? "", Button = s.ButtonText ?? "", Hint = s.RouteHint ?? "", Primary = s.ButtonPrimary, Progress = s.Progress, Testing = testing, AutoRoute = autoRoute };
            }

            public bool SameAs(Shown o)
            {
                return o != null && Kind == o.Kind && Title == o.Title && Sub == o.Sub && Button == o.Button && Hint == o.Hint
                    && Primary == o.Primary && Nullable.Equals(Progress, o.Progress) && Testing == o.Testing && AutoRoute == o.AutoRoute;
            }
        }

        public GlassMainWindow(AppController app, bool startHidden) : base(340, true)
        {
            this.app = app;
            cfg = app.Config;

            // ---- title row: logo, name, version; to tray, minimize, close ----
            versionText = G.Text("v" + AppSetup.AppVersion, TextStyle.Caption, GlassTheme.Tertiary);
            versionText.Margin = new Thickness(7, 2, 0, 0);
            TitleLeft.Children.Add(versionText);
            minButton = AddTitleButton(Glyph.Minimize, null, () => WindowState = WindowState.Minimized);
            trayButton = AddTitleButton(Glyph.ToTray, null, () => app.HideToTray());

            var content = new StackPanel { Margin = new Thickness(12, 0, 12, 12) };

            // ---- status pane ----
            // macOS layout: status on the left, the action as a regular push button on the right.
            var statusStack = new StackPanel { Margin = new Thickness(16, 14, 14, 14) };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            light = new StatusLight(9) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 2, 10, 0) };
            head.Children.Add(light);
            statusTitle = new CrossfadeText(TextStyle.LargeTitle, GlassTheme.Text, false);
            Grid.SetColumn(statusTitle, 1);
            head.Children.Add(statusTitle);
            statusStack.Children.Add(head);
            connect = new GlassButton("", ButtonKind.Primary, null, 34) { MinWidth = 84, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            connect.Click += async () => await app.ConnectButtonClicked();
            Grid.SetColumn(connect, 2);
            head.Children.Add(connect);
            statusSub = new CrossfadeText(TextStyle.Subhead, GlassTheme.Secondary, true) { Margin = new Thickness(20, 2, 0, 0) };
            statusStack.Children.Add(statusSub);
            progress = new GlassProgress();
            // The slot keeps its height, so the pane doesn't jump when progress appears.
            statusStack.Children.Add(new Border { Height = 4, Margin = new Thickness(0, 12, 0, 0), Child = progress });
            content.Children.Add(new GlassPane(24) { Child = statusStack });

            // ---- error / update card ----
            card = new GlassCard { ActionLabel = app.ActionLabel };
            card.ActionClicked += async a => await app.RunAction(a);
            card.Dismissed += e => app.OnCardDismissed(e);
            content.Children.Add(card);

            // ---- route ----
            routeLabel = G.Section("");
            content.Children.Add(routeLabel);
            route = new LiquidSegments(ProxyModes.All.Select(ProxyModes.Display).ToArray(), new[] { Glyph.Bolt, Glyph.Cloud, Glyph.Globe });
            route.SetIndex(ProxyModes.Index(app.Mode), false);
            route.UserChanged += async i => await app.ModeChosen(ProxyModes.All[i]);
            content.Children.Add(route);
            routeHint = G.Note("");
            content.Children.Add(routeHint);

            var options = new InsetGroup { Margin = new Thickness(0, 10, 0, 0) };
            autoRoute = new LiquidToggle(cfg.AutoRoute);
            autoRoute.Toggled += on =>
            {
                if (on != cfg.AutoRoute)
                    app.SetAutoRoute(on);
            };
            options.AddRow(RowText(out autoRouteText), autoRoute);
            autoConnect = new LiquidToggle(cfg.AutoConnect);
            autoConnect.Toggled += on =>
            {
                if (on != cfg.AutoConnect)
                    app.SetAutoConnect(on);
            };
            options.AddRow(RowText(out autoConnectText), autoConnect);
            content.Children.Add(options);

            // ---- Discord ----
            clientLabel = G.Section("");
            content.Children.Add(clientLabel);
            var discord = new InsetGroup();
            var clientInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            pickButton = PopUpButton(out clientName);
            pickButton.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                BuildClientMenu().Open(pickButton);
            };
            clientInfo.Children.Add(pickButton);
            var stateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            clientLight = new StatusLight(6) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 1, 7, 0) };
            clientStateText = G.Text("", TextStyle.Footnote, GlassTheme.Secondary);
            stateRow.Children.Add(clientLight);
            stateRow.Children.Add(clientStateText);
            clientStateRow = stateRow;
            clientInfo.Children.Add(stateRow);
            launch = new GlassButton("", ButtonKind.Primary, Glyph.Play, 32);
            launch.Click += async () => await app.LaunchSelected();
            discord.AddRow(clientInfo, launch, 58);
            shortcut = new LiquidToggle(false);
            shortcut.Toggled += on =>
            {
                if (app.Selected != null && on != app.Selected.ShortcutProxied)
                    app.SetShortcutProxy(on);
            };
            shortcutRow = discord.AddRow(RowText(out shortcutText), shortcut);
            shortcutSeparator = (FrameworkElement)discord.Rows[discord.Rows.Count - 2];
            content.Children.Add(discord);

            // ---- floating toolbar ----
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            testButton = ToolButton(Glyph.Test, async () => await app.RunTest());
            logsButton = ToolButton(Glyph.Logs, ShowLogs);
            settingsButton = ToolButton(Glyph.Settings, () => app.OpenSettings(null));
            tools.Children.Add(testButton);
            tools.Children.Add(logsButton);
            tools.Children.Add(settingsButton);
            content.Children.Add(new GlassPane(28) { Child = tools, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) });

            // ---- notes: a layer over everything, lifted over the title row ----
            overlay = new Grid { RenderTransform = new TranslateTransform(0, -40) };
            toast = new GlassToast { Margin = new Thickness(12, 6, 12, 0) };
            overlay.Children.Add(toast);
            var root = new Grid();
            root.Children.Add(content);
            root.Children.Add(overlay);
            Body = root;

            L.Changed += ApplyTexts;
            GlassTheme.Changed += OnToneChanged;
            ApplyTexts();

            Closing += (s, e) =>
            {
                if (!GlassPreview.Active)
                    app.OnViewClosing();
            };
            Closed += (s, e) =>
            {
                timer.Stop();
                L.Changed -= ApplyTexts;
                GlassTheme.Changed -= OnToneChanged;
                if (!GlassPreview.Active)
                    Application.Current?.Shutdown();
            };

            if (GlassPreview.Active)
                return;
            timer.Tick += (s, e) => app.Tick();
            // Attach and start from the dispatcher, so the controller captures WPF's
            // SynchronizationContext. startHidden: GlassApp.Run simply doesn't show the window.
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                app.Attach(this);
                timer.Start();
                await app.Init();
            }));
        }

        // ---------------- building blocks ----------------

        static TextBlock RowText(out TextBlock t)
        {
            t = G.Text("", TextStyle.Body);
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            return t;
        }

        GlassButton ToolButton(string glyph, Action click)
        {
            var b = new GlassButton("", ButtonKind.Plain, glyph, 32);
            b.Click += click;
            return b;
        }

        // The install name with a chevron: opens the picker (Apple's pop-up button).
        static Border PopUpButton(out TextBlock name)
        {
            name = G.Text("", TextStyle.Headline);
            var chevron = G.Icon(Glyph.ChevronDown, 9, GlassTheme.Secondary);
            chevron.Margin = new Thickness(7, 2, 0, 0);
            var dock = new DockPanel { Margin = new Thickness(6, 2, 7, 2) };
            DockPanel.SetDock(chevron, Dock.Right);
            dock.Children.Add(chevron);
            dock.Children.Add(name);
            var hover = new Border { CornerRadius = new CornerRadius(8), Background = GlassTheme.Hover, Opacity = 0 };
            var g = new Grid();
            g.Children.Add(hover);
            g.Children.Add(dock);
            var scale = new ScaleTransform(1, 1);
            var b = new Border
            {
                Child = g,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(-6, 0, 0, 0),
                RenderTransformOrigin = new Point(0.3, 0.5),
                RenderTransform = scale,
            };
            b.MouseEnter += (s, e) => hover.BeginAnimation(OpacityProperty, Motion.To(1, 140, new QuadraticEase()));
            b.MouseLeave += (s, e) => hover.BeginAnimation(OpacityProperty, Motion.To(0, 220, new QuadraticEase()));
            G.Press(b, scale);
            return b;
        }

        GlassMenu BuildClientMenu()
        {
            var m = new GlassMenu();
            foreach (DiscordClient c in app.Clients)
            {
                DiscordClient cc = c;
                m.Add(c.DisplayName, () => app.Select(cc), c == app.Selected, null, c.RootDir);
            }
            if (app.Clients.Count > 0)
                m.AddSeparator();
            m.Add(L.T("client.rescan"), app.Rescan, false, Glyph.Refresh);
            m.Add(L.T("client.browse"), app.BrowseForDiscord, false, ""); // OpenFolderHorizontal: Glyph.Folder (E8B7) reads as a page in MDL2
            return m;
        }

        void ApplyTexts()
        {
            G.SetText(routeLabel, L.T("route.label"));
            route.SetLabels(ProxyModes.All.Select(ProxyModes.Display).ToArray());
            G.SetText(autoRouteText, L.T("autoroute"));
            G.SetText(autoConnectText, L.T("autoconnect"));
            G.SetText(clientLabel, L.T("client.label"));
            G.SetText(shortcutText, L.T("client.shortcut"));
            testButton.Text = L.T("btn.test");
            logsButton.Text = L.T("btn.logs");
            settingsButton.Text = L.T("btn.settings");
            trayButton.ToolTip = L.T("tip.tray");
            minButton.ToolTip = L.T("tip.minimize");
            CloseButton.ToolTip = L.T("btn.close");
            versionText.ToolTip = L.F("glass.version", AppSetup.AppVersion);
            card.ApplyTexts();
            RefreshStatus();
            RefreshClient();
        }

        void OnToneChanged()
        {
            // Primary buttons take their glow colour from the tone when their kind is set.
            connect.Kind = connect.Kind;
            launch.Kind = launch.Kind;
        }

        // ---------------- IAppView ----------------

        public bool WindowVisible => IsVisible;
        public bool IsShownToUser => IsVisible && WindowState != WindowState.Minimized;
        public AppError Card => card.Error;

        public void ShowCard(AppError e)
        {
            card.ShowError(e);
        }

        public void HideCard(bool notify)
        {
            card.Collapse(notify);
        }

        public void ShowToast(NoteKind kind, string title, string sub)
        {
            toast.Show(kind, title, sub);
        }

        public void ModeChanged(bool animate)
        {
            route.SetIndex(ProxyModes.Index(app.Mode), animate && IsLoaded);
        }

        public bool Confirm(string title, string body)
        {
            return GlassDialog.Confirm(OwnerWindow(), title, body);
        }

        public string PickFolder(string description, string startPath)
        {
            return GlassDialog.PickFolder(OwnerWindow(), description, startPath);
        }

        public void ShowSettings(string focus)
        {
            var w = new GlassSettingsWindow(app, focus);
            SetOwner(w, OwnerWindow());
            w.ShowDialog();
        }

        public async Task ShowRepairAsync(Func<IRepairView, Task> run)
        {
            var w = new GlassRepairWindow(app);
            SetOwner(w, OwnerWindow()); // Settings may be open: the repair window goes over it
            bool started = false;
            w.ContentRendered += async (s, e) =>
            {
                if (started)
                    return;
                started = true;
                await run(w);
            };
            w.ShowDialog();
            await Task.CompletedTask;
        }

        public void ShowLogs()
        {
            if (logsWindow == null)
            {
                var w = new GlassLogsWindow(app.Logger);
                w.Closed += (s, e) =>
                {
                    if (logsWindow == w)
                        logsWindow = null;
                };
                logsWindow = w;
                w.Show();
            }
            else
            {
                if (logsWindow.WindowState == WindowState.Minimized)
                    logsWindow.WindowState = WindowState.Normal;
                logsWindow.Activate();
            }
        }

        public void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        public void HideWindow()
        {
            Hide();
        }

        public void Shutdown()
        {
            timer.Stop();
            Application.Current.Shutdown();
        }

        // The app's active window (Settings may be open over this one); this window if it is
        // shown; otherwise none (a window that was never shown can't own another).
        Window OwnerWindow()
        {
            Window active = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
            if (active != null)
                return active;
            return IsVisible ? this : null;
        }

        static void SetOwner(Window w, Window owner)
        {
            if (owner != null)
            {
                w.Owner = owner;
                w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // ---------------- status ----------------

        public void RefreshStatus()
        {
            StatusInfo s = app.Status;
            Shown now = Shown.Of(s, app.Testing, cfg.AutoRoute);
            if (now.SameAs(last))
                return;
            bool animate = last != null && IsLoaded;
            bool kindChanged = last == null || last.Kind != now.Kind;
            last = now;

            Brush color;
            switch (s.Kind)
            {
                case StatusKind.Connecting:
                    color = GlassTheme.Warning;
                    break;
                case StatusKind.Connected:
                    color = GlassTheme.Success;
                    break;
                case StatusKind.Unhealthy:
                case StatusKind.Failed:
                    color = GlassTheme.Danger;
                    break;
                default:
                    color = GlassTheme.Idle;
                    break;
            }
            light.Set(color, s.Kind == StatusKind.Connecting);
            statusTitle.Set(now.Title, animate);
            // The detail line crossfades with the state; progress text while connecting just updates.
            statusSub.Set(now.Sub, animate && kindChanged);
            progress.Set(s.Progress);
            if (connect.Text != now.Button)
                connect.Text = now.Button;
            ButtonKind kind = s.ButtonPrimary ? ButtonKind.Primary : ButtonKind.Secondary;
            if (connect.Kind != kind)
                connect.Kind = kind;
            route.Busy = s.Kind == StatusKind.Connecting;
            G.SetText(routeHint, now.Hint);
            if (autoRoute.IsOn != cfg.AutoRoute)
                autoRoute.SetOn(cfg.AutoRoute, animate);
            if (testButton.IsEnabled == now.Testing)
                testButton.IsEnabled = !now.Testing;
            UpdateLaunchKind();
        }

        // ---------------- Discord ----------------

        public void RefreshClient()
        {
            DiscordClient c = app.Selected;
            bool canLaunch = !app.Launching;
            if (launch.IsEnabled != canLaunch)
                launch.IsEnabled = canLaunch;
            string launchText = L.T("client.launch");
            if (c == null)
            {
                G.SetText(clientName, L.T("client.none"));
                clientStateRow.Visibility = Visibility.Collapsed;
                SetShortcutVisible(false);
                pickButton.ToolTip = L.T("glass.client.pick");
            }
            else
            {
                G.SetText(clientName, c.DisplayName);
                clientStateRow.Visibility = Visibility.Visible;
                switch (app.ClientState)
                {
                    case ClientState.RunningWithProxy:
                        clientLight.Set(GlassTheme.Success, false);
                        G.SetText(clientStateText, L.T("client.running.proxy"));
                        break;
                    case ClientState.RunningDirect:
                        clientLight.Set(GlassTheme.Warning, false);
                        G.SetText(clientStateText, L.T("client.running.direct"));
                        launchText = L.T("client.restart");
                        break;
                    default:
                        clientLight.Set(GlassTheme.Idle, false);
                        G.SetText(clientStateText, L.T("client.stopped"));
                        break;
                }
                SetShortcutVisible(c.ShortcutPath != null);
                if (shortcut.IsOn != c.ShortcutProxied)
                    shortcut.SetOn(c.ShortcutProxied, IsLoaded);
                pickButton.ToolTip = L.T("glass.client.pick") + "\n" + c.RootDir;
            }
            if (launch.Text != launchText)
                launch.Text = launchText;
            UpdateLaunchKind();
        }

        // One filled button per area: Launch is the primary action only when Discord needs it
        // (running without the proxy, or not running while connected).
        void UpdateLaunchKind()
        {
            bool needed = app.Selected != null && (app.ClientState == ClientState.RunningDirect
                || (app.ClientState == ClientState.Stopped && last != null && last.Kind == StatusKind.Connected));
            ButtonKind k = needed ? ButtonKind.Primary : ButtonKind.Secondary;
            if (launch.Kind != k)
                launch.Kind = k;
        }

        void SetShortcutVisible(bool visible)
        {
            Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
            if (shortcutRow.Visibility != v)
            {
                shortcutRow.Visibility = v;
                shortcutSeparator.Visibility = v;
            }
        }

        // ---------------- preview (--glass-preview) ----------------

        // States: connected, connecting, down, error, update, toast, menu, direct, noclient.
        public static Window CreatePreview(AppController app, string state)
        {
            var client = new DiscordClient { Key = "Discord", Flavor = "Discord", DisplayName = "Discord", RootDir = @"C:\x", ShortcutPath = @"C:\x.lnk", ShortcutArgs = "--proxy-server=x" };
            ClientState clientState = ClientState.RunningWithProxy;
            int mode = 0;
            string direct = ProxyModes.Display(ProxyMode.Direct);
            var s = new StatusInfo
            {
                Kind = StatusKind.Connected,
                Title = L.T("status.connected"),
                Sub = L.F("status.sub.connected", 142, direct),
                ButtonText = L.T("btn.disconnect"),
                RouteHint = L.F("route.auto.hint", L.T("route.direct.hint")),
                ModeName = direct,
            };
            AppError cardError = null;
            switch (state)
            {
                case "connecting":
                    mode = 2;
                    s.Kind = StatusKind.Connecting;
                    s.Title = L.T("status.connecting");
                    s.Sub = L.F("progress.tor", 45);
                    s.Progress = 0.45f;
                    s.ButtonText = L.T("btn.cancel");
                    s.RouteHint = L.F("route.auto.hint", L.T("route.tor.hint"));
                    clientState = ClientState.Stopped;
                    break;
                case "down":
                case "noclient":
                    s.Kind = StatusKind.Down;
                    s.Title = L.T("status.down");
                    s.Sub = L.T("status.sub.down");
                    s.ButtonText = L.T("btn.connect");
                    s.ButtonPrimary = true;
                    clientState = ClientState.Stopped;
                    if (state == "noclient")
                        client = null;
                    break;
                case "error":
                    mode = 1;
                    cardError = AppError.For("DP-301");
                    s.Kind = StatusKind.Failed;
                    s.Title = L.T("status.error");
                    s.Sub = cardError.Title;
                    s.ButtonText = L.T("btn.connect");
                    s.ButtonPrimary = true;
                    s.RouteHint = L.T("route.warp.hint");
                    clientState = ClientState.Stopped;
                    break;
                case "update":
                    cardError = AppError.For("UPDATE", "3.0.1");
                    break;
                case "direct":
                    clientState = ClientState.RunningDirect;
                    break;
            }
            app.StatusOverride = s;
            app.SetPreviewClient(client, clientState);
            var w = new GlassMainWindow(app, false);
            w.route.SetIndex(mode, false);
            w.Loaded += (o, e) =>
            {
                if (cardError != null)
                    w.ShowCard(cardError);
                if (state == "toast")
                    w.ShowToast(NoteKind.Success, L.F("toast.connected", direct), L.F("status.sub.connected", 142, direct));
                if (state == "menu")
                {
                    // A pop-up isn't part of the window's picture: draw the menu inside it instead.
                    FrameworkElement menu = w.BuildClientMenu().BuildVisual(null);
                    Point at = w.pickButton.TranslatePoint(new Point(0, w.pickButton.ActualHeight + 4), w.overlay);
                    menu.HorizontalAlignment = HorizontalAlignment.Left;
                    menu.VerticalAlignment = VerticalAlignment.Top;
                    menu.Margin = new Thickness(at.X - 6, at.Y, 0, 0);
                    w.overlay.Children.Add(menu);
                }
            };
            return w;
        }

        // ---------------- crossfading label ----------------

        // Two stacked text blocks: a new text fades and slides in while the old one fades out.
        class CrossfadeText : Grid
        {
            readonly TextBlock[] blocks = new TextBlock[2];
            readonly TranslateTransform[] moves = new TranslateTransform[2];
            int front;
            int gen;

            public CrossfadeText(TextStyle style, Brush brush, bool wrap)
            {
                for (int i = 0; i < 2; i++)
                {
                    TextBlock t = G.Text("", style, brush);
                    if (wrap)
                    {
                        t.TextWrapping = TextWrapping.Wrap;
                        t.TextTrimming = TextTrimming.None;
                    }
                    t.VerticalAlignment = VerticalAlignment.Top;
                    moves[i] = new TranslateTransform();
                    t.RenderTransform = moves[i];
                    blocks[i] = t;
                    Children.Add(t);
                }
                blocks[1].Opacity = 0;
            }

            public void Set(string s, bool animate)
            {
                s = s ?? "";
                TextBlock cur = blocks[front];
                if (cur.Text == s)
                    return;
                if (!animate || GlassTheme.ReduceMotion)
                {
                    gen++;
                    Settle(front);
                    G.SetText(cur, s);
                    return;
                }
                int g = ++gen;
                int next = 1 - front;
                TextBlock nb = blocks[next];
                nb.Text = s;
                nb.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D(240)) { EasingFunction = new QuadraticEase() });
                var slide = new DoubleAnimation(7, 0, Motion.D(460)) { EasingFunction = SpringEase.Soft };
                // The slide is the longest part: once it ends, drop the old text and all clocks.
                slide.Completed += (o, e) =>
                {
                    if (g == gen)
                        Settle(next);
                };
                moves[next].BeginAnimation(TranslateTransform.YProperty, slide);
                cur.BeginAnimation(OpacityProperty, Motion.To(0, 160, new QuadraticEase()));
                moves[front].BeginAnimation(TranslateTransform.YProperty, Motion.To(-5, 200, new QuadraticEase()));
                front = next;
            }

            // Leaves only the front block showing, with no animations held.
            void Settle(int f)
            {
                int back = 1 - f;
                blocks[back].BeginAnimation(OpacityProperty, null);
                blocks[back].Opacity = 0;
                blocks[back].Text = "";
                moves[back].BeginAnimation(TranslateTransform.YProperty, null);
                moves[back].Y = 0;
                blocks[f].BeginAnimation(OpacityProperty, null);
                blocks[f].Opacity = 1;
                moves[f].BeginAnimation(TranslateTransform.YProperty, null);
                moves[f].Y = 0;
                front = f;
            }
        }
    }
}
