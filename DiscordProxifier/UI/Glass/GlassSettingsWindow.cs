using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DiscordUnblocker.Glass
{
    // Settings in the macOS System Settings layout: a glass sidebar of sections, the section's
    // inset grouped lists on the right. Every change is saved at once (as in the classic
    // window); AppController.OpenSettings applies autostart, ports and reconnects afterwards.
    public class GlassSettingsWindow : GlassWindow
    {
        const double SidebarWidth = 188, ItemHeight = 30, ItemGap = 2;

        static readonly string[] Ids = { "general", "appearance", "notify", "connection", "updates", "trouble" };
        static readonly string[] Keys = { "settings.general", "look.appearance", "settings.notify", "settings.connection", "settings.updates", "settings.trouble" };
        static readonly string[] Glyphs = { "\uE713", "\uE790", "\uEA8F", "\uE774", "\uE896", "\uE90F" };
        static readonly Color[,] Tiles =
        {
            { Color.FromRgb(150, 152, 164), Color.FromRgb(112, 115, 130) },
            { Color.FromRgb(184, 132, 246), Color.FromRgb(140, 92, 222) },
            { Color.FromRgb(255, 112, 112), Color.FromRgb(232, 64, 72) },
            { Color.FromRgb(84, 160, 252), Color.FromRgb(40, 114, 232) },
            { Color.FromRgb(70, 204, 130), Color.FromRgb(30, 160, 94) },
            { Color.FromRgb(252, 168, 70), Color.FromRgb(234, 122, 28) },
        };

        readonly AppController app;
        readonly Configuration cfg;
        readonly bool preview;
        readonly List<Action> texts = new List<Action>();

        // sidebar and content frame
        readonly List<SidebarItem> items = new List<SidebarItem>();
        readonly List<FrameworkElement> pages = new List<FrameworkElement>();
        readonly Border pill;
        readonly TranslateTransform pillMove = new TranslateTransform();
        readonly TextBlock pageTitle, versionLine;
        readonly SmoothScrollViewer scroll;
        readonly GlassButton done;
        int current = -1;

        // controls
        LiquidSegments language, style, notifyMode;
        GlassButton makeShortcut, torCheck, appCheck, repair, report, logs, folder;
        GlassField endpoint, bridges, httpPort, socksPort;
        TextBlock endpointNote, portNote, torTitle, appTitle, torStatus, appStatus, reportSub;
        GlassSpinner torSpin, appSpin;
        readonly List<ToneTile> tones = new List<ToneTile>();
        bool shortcutMade;

        public GlassSettingsWindow(AppController app, string focus) : base(680, false, L.T("settings.title"))
        {
            this.app = app;
            cfg = app.Config;
            preview = GlassPreview.Active;
            CloseOnEscape = true;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.Manual;
            Height = Math.Min(540 + ShadowMargin * 2, SystemParameters.WorkArea.Height);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { Margin = new Thickness(14, 0, 14, 14) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SidebarWidth) });
            root.ColumnDefinitions.Add(new ColumnDefinition());

            // ---- sidebar ----
            var side = new GlassPane(18);
            var sideStack = new StackPanel { Margin = new Thickness(10, 14, 10, 10) };
            var who = new Grid { Margin = new Thickness(6, 0, 4, 12) };
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            who.ColumnDefinitions.Add(new ColumnDefinition());
            if (LogoImage != null)
                who.Children.Add(new Image { Source = LogoImage, Width = 28, Height = 28, Margin = new Thickness(0, 0, 9, 0) });
            var whoText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            whoText.Children.Add(G.Text("Discord Proxifier", TextStyle.Headline));
            versionLine = G.Text("", TextStyle.Footnote, GlassTheme.Secondary);
            whoText.Children.Add(versionLine);
            Grid.SetColumn(whoText, 1);
            who.Children.Add(whoText);
            sideStack.Children.Add(who);
            sideStack.Children.Add(new Border { Height = 1, Background = GlassTheme.Separator, Margin = new Thickness(6, 0, 6, 10) });
            var list = new Grid();
            pill = new Border
            {
                Height = ItemHeight,
                CornerRadius = new CornerRadius(10),
                Background = GlassTheme.Selection,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = pillMove,
                Opacity = 0,
            };
            list.Children.Add(pill);
            var itemStack = new StackPanel();
            for (int i = 0; i < Ids.Length; i++)
            {
                int k = i;
                var it = new SidebarItem(Glyphs[i], Tiles[i, 0], Tiles[i, 1]);
                it.Margin = new Thickness(0, 0, 0, ItemGap);
                it.MouseLeftButtonUp += (s, e) => Select(k, true);
                items.Add(it);
                itemStack.Children.Add(it);
            }
            list.Children.Add(itemStack);
            sideStack.Children.Add(list);
            side.Child = sideStack;
            root.Children.Add(side);

            // ---- content ----
            var content = new Grid { Margin = new Thickness(18, 0, 0, 0) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pageTitle = G.Text("", TextStyle.Title);
            pageTitle.Margin = new Thickness(14, 4, 0, 6);
            content.Children.Add(pageTitle);
            var scrollHost = new Grid();
            scroll = new SmoothScrollViewer();
            scrollHost.Children.Add(scroll);
            OverlayScrollBar bar = OverlayScrollBar.For(scroll);
            bar.Margin = new Thickness(0, 6, 0, 6);
            scrollHost.Children.Add(bar);
            Grid.SetRow(scrollHost, 1);
            content.Children.Add(scrollHost);

            var bottom = new Grid { Margin = new Thickness(0, 0, 14, 0) };
            bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hair = new Border { Height = 1, Background = GlassTheme.Separator, Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetColumnSpan(hair, 2);
            bottom.Children.Add(hair);
            var saved = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            TextBlock tick = G.Icon("\uE73E", 11, GlassTheme.Tertiary);
            tick.Margin = new Thickness(0, 1, 7, 0);
            saved.Children.Add(tick);
            saved.Children.Add(Tx(G.Text("", TextStyle.Footnote, GlassTheme.Tertiary), "settings.autosave"));
            Grid.SetRow(saved, 1);
            bottom.Children.Add(saved);
            done = new GlassButton(L.T("btn.done"), ButtonKind.Primary, null, 32) { MinWidth = 96 };
            done.Click += Close;
            texts.Add(() => done.Text = L.T("btn.done"));
            Grid.SetRow(done, 1);
            Grid.SetColumn(done, 1);
            bottom.Children.Add(done);
            Grid.SetRow(bottom, 2);
            content.Children.Add(bottom);
            Grid.SetColumn(content, 1);
            root.Children.Add(content);
            Body = root;

            pages.Add(BuildGeneral());
            pages.Add(BuildAppearance());
            pages.Add(BuildNotifications());
            pages.Add(BuildConnection());
            pages.Add(BuildUpdates());
            pages.Add(BuildTrouble());

            ApplyTexts();
            Select(focus == "endpoint" || focus == "bridges" ? Array.IndexOf(Ids, "connection") : focus == "notify" ? Array.IndexOf(Ids, "notify") : 0, false);

            L.Changed += ApplyTexts;
            GlassTheme.Changed += ThemeChanged;
            Closed += (s, e) =>
            {
                L.Changed -= ApplyTexts;
                GlassTheme.Changed -= ThemeChanged;
                torSpin.Running = false;
                appSpin.Running = false;
            };
            ContentRendered += (s, e) =>
            {
                GlassField f = focus == "endpoint" ? endpoint : focus == "bridges" ? bridges : null;
                if (f != null)
                {
                    f.Box.Focus();
                    f.Box.CaretIndex = f.Box.Text.Length;
                    f.BringIntoView();
                }
                else
                    done.Focus();
            };
        }

        // ---------------- pages ----------------

        FrameworkElement BuildGeneral()
        {
            var page = Page();
            InsetGroup g = Group(page);
            language = new LiquidSegments(new[] { "English", "Türkçe" }, null, 32) { Width = 196 };
            language.SetIndex(cfg.Language == "tr" ? 1 : 0, false);
            language.UserChanged += i =>
            {
                cfg.Language = i == 1 ? "tr" : "en";
                Save();
                L.Set(cfg.Language);
            };
            g.AddRow(Label("settings.language", null), language);

            g = Group(page);
            ToggleRow(g, "settings.startup", null, cfg.OpenAtStartup, v => cfg.OpenAtStartup = v);
            ToggleRow(g, "settings.launchafter", null, cfg.LaunchDiscordAfterConnect, v => cfg.LaunchDiscordAfterConnect = v);
            ToggleRow(g, "settings.shortcuts", "settings.shortcuts.sub", cfg.AutoUpdateProxified, v => cfg.AutoUpdateProxified = v);

            g = Group(page);
            makeShortcut = SmallButton("btn.create", Glyph.Link);
            makeShortcut.Click += () =>
            {
                if (preview || shortcutMade)
                    return;
                if (AppSetup.CreateAppShortcuts(app.Logger))
                {
                    shortcutMade = true;
                    makeShortcut.Kind = ButtonKind.Plain;
                    makeShortcut.Glyph = Glyph.Check;
                    makeShortcut.Text = L.T("settings.shortcut.done");
                    makeShortcut.Cursor = Cursors.Arrow;
                }
            };
            texts.Add(() =>
            {
                if (shortcutMade)
                    makeShortcut.Text = L.T("settings.shortcut.done");
            });
            g.AddRow(Label("settings.shortcut.row", null), makeShortcut);
            return page;
        }

        FrameworkElement BuildAppearance()
        {
            var page = Page();
            InsetGroup g = Group(page);
            style = new LiquidSegments(new[] { L.T("look.glass"), L.T("look.classic") }, null, 32) { Width = 224 };
            style.SetIndex(0, false);
            style.UserChanged += i =>
            {
                if (i != 1)
                    return;
                // Let the pill settle before the alert covers it.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    bool yes = GlassDialog.Confirm(this, L.T("look.confirm.title"), L.T("look.confirm"), L.T("look.confirm.ok"));
                    if (yes && !preview)
                    {
                        Close();
                        app.SwitchAppearance("classic");
                    }
                    else
                        style.SetIndex(0, true);
                }));
            };
            texts.Add(() => style.SetLabels(new[] { L.T("look.glass"), L.T("look.classic") }));
            g.AddRow(Label("look.style", null), style);
            Note(page, "look.restart.hint");

            Section(page, "look.tone");
            g = Group(page);
            var grid = new UniformGrid { Columns = 3, Rows = 1, Margin = new Thickness(0, 10, 0, 8) };
            foreach (GlassTone t in new[] { GlassTone.Light, GlassTone.Mixed, GlassTone.Dark })
            {
                var tile = new ToneTile(t, PickTone);
                Tx(tile.Label, "look.tone." + GlassTheme.ToConfig(t));
                tile.SetSelected(t == GlassTheme.P.Tone, false);
                tones.Add(tile);
                grid.Children.Add(tile);
            }
            g.AddRow(grid);
            Note(page, "look.tone.hint");
            return page;
        }

        // Notifications shown while the window isn't in front (the app's own banners).
        FrameworkElement BuildNotifications()
        {
            var page = Page();
            InsetGroup g = Group(page);
            string[] modes = { "all", "important", "off" };
            notifyMode = new LiquidSegments(new[] { L.T("notify.all"), L.T("notify.important"), L.T("notify.off") }, null, 32) { Width = 236 };
            notifyMode.SetIndex(Math.Max(0, Array.IndexOf(modes, cfg.Notifications)), false);
            notifyMode.UserChanged += i =>
            {
                cfg.Notifications = modes[i];
                Save();
            };
            texts.Add(() => notifyMode.SetLabels(new[] { L.T("notify.all"), L.T("notify.important"), L.T("notify.off") }));
            g.AddRow(Label("notify.mode", null), notifyMode);
            Note(page, "notify.mode.hint");

            g = Group(page);
            ToggleRow(g, "notify.sound", "notify.sound.sub", cfg.NotificationSound, v => cfg.NotificationSound = v);
            GlassButton test = SmallButton("notify.test", "\uEA8F");
            test.Click += () =>
            {
                if (!preview)
                    app.ShowSampleNote();
            };
            g.AddRow(Label("notify.test.row", null), test);
            Note(page, "notify.busy.hint");
            return page;
        }

        FrameworkElement BuildConnection()
        {
            var page = Page();
            Section(page, "settings.warp");
            InsetGroup g = Group(page);
            ToggleRow(g, "settings.fallback", null, cfg.WarpFallbackToTor, v => cfg.WarpFallbackToTor = v);
            endpoint = new GlassField(false, false, 32, "162.159.192.1:2408") { Width = 216 };
            endpoint.Text = cfg.WarpEndpoint ?? "";
            endpoint.Box.TextChanged += (s, e) => CheckEndpoint(true);
            g.AddRow(Label("settings.endpoint", null), endpoint);
            endpointNote = Note(page, null);

            Section(page, "settings.tor");
            g = Group(page);
            var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
            stack.Children.Add(Tx(G.Text("", TextStyle.Body), "settings.bridges"));
            bridges = new GlassField(true, true, 108) { Margin = new Thickness(0, 9, 0, 0) };
            bridges.Text = (cfg.TorBridges ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            bridges.Box.TextChanged += (s, e) =>
            {
                cfg.TorBridges = bridges.Text.Trim();
                Save();
            };
            stack.Children.Add(bridges);
            g.AddRow(stack);
            Note(page, "settings.bridges.hint");

            Section(page, "settings.ports");
            g = Group(page);
            httpPort = new GlassField(false, false, 32, "42212") { Width = 104 };
            httpPort.Text = cfg.GostPort;
            socksPort = new GlassField(false, false, 32, "42213") { Width = 104 };
            socksPort.Text = cfg.MainProxyPort;
            httpPort.Box.TextChanged += (s, e) => CheckPorts(true);
            socksPort.Box.TextChanged += (s, e) => CheckPorts(true);
            g.AddRow(Label("settings.port.http", null), httpPort);
            g.AddRow(Label("settings.port.socks", null), socksPort);
            portNote = Note(page, null);
            return page;
        }

        FrameworkElement BuildUpdates()
        {
            var page = Page();
            InsetGroup g = Group(page);
            torCheck = SmallButton("settings.tor.check", Glyph.Refresh);
            g.AddRow(StatusLabel(out torTitle, out torStatus, out torSpin), torCheck, 58);
            torCheck.Click += async () => await RunUpdate(torCheck, torStatus, torSpin, app.UpdateTor);
            appCheck = SmallButton("settings.app.check", Glyph.Refresh);
            g.AddRow(StatusLabel(out appTitle, out appStatus, out appSpin), appCheck, 58);
            appCheck.Click += async () => await RunUpdate(appCheck, appStatus, appSpin, app.CheckAppUpdate);
            Note(page, "settings.updates.hint");
            return page;
        }

        FrameworkElement BuildTrouble()
        {
            var page = Page();
            InsetGroup g = Group(page);
            repair = SmallButton("settings.repair", Glyph.Repair);
            repair.Click += async () =>
            {
                if (preview)
                    return;
                repair.IsEnabled = false;
                try
                {
                    await app.OpenRepair();
                }
                finally
                {
                    repair.IsEnabled = true;
                }
            };
            g.AddRow(Label("settings.repair.row", "settings.repair.sub"), repair, 58);

            report = SmallButton("settings.report", Glyph.Report);
            TextBlock title;
            StackPanel reportLabel = InsetGroup.Label("", L.T("settings.report.sub"), out title, out reportSub);
            Tx(title, "settings.report.row");
            reportSub.Tag = "default";
            texts.Add(() =>
            {
                if ((string)reportSub.Tag == "default")
                    G.SetText(reportSub, L.T("settings.report.sub"));
            });
            report.Click += async () =>
            {
                if (preview)
                    return;
                report.IsEnabled = false;
                reportSub.Tag = null;
                G.SetText(reportSub, L.T("report.building"));
                string result;
                try
                {
                    result = await app.CreateReport();
                }
                catch (Exception ex)
                {
                    app.Logger.Log("Report failed: " + ex.Message, LogType.ERROR);
                    result = ex.Message;
                }
                G.SetText(reportSub, result);
                report.IsEnabled = true;
            };
            g.AddRow(reportLabel, report, 58);

            g = Group(page);
            logs = SmallButton("btn.open", "\uE8FD"); // BulletedList
            logs.Click += () =>
            {
                if (!preview)
                    app.OpenLogs();
            };
            g.AddRow(Label("logs.title", "settings.logs.sub"), logs, 56);
            folder = SmallButton("btn.open", Glyph.Folder);
            folder.Click += () =>
            {
                if (preview)
                    return;
                try
                {
                    Process.Start("explorer.exe", "\"" + AppContext.BaseDirectory + "\"");
                }
                catch (Exception ex)
                {
                    app.Logger.Log("Could not open the app folder: " + ex.Message, LogType.WARNING);
                }
            };
            TextBlock ft, fs;
            StackPanel folderLabel = InsetGroup.Label("", AppContext.BaseDirectory.TrimEnd('\\'), out ft, out fs);
            Tx(ft, "settings.folder.row");
            fs.TextWrapping = TextWrapping.NoWrap;
            fs.TextTrimming = TextTrimming.CharacterEllipsis;
            fs.ToolTip = AppContext.BaseDirectory;
            g.AddRow(folderLabel, folder, 56);
            return page;
        }

        // ---------------- building blocks ----------------

        static StackPanel Page()
        {
            return new StackPanel { Margin = new Thickness(0, 0, 14, 18) };
        }

        TextBlock Tx(TextBlock t, string key)
        {
            texts.Add(() => G.SetText(t, L.T(key)));
            return t;
        }

        void Section(StackPanel page, string key)
        {
            TextBlock t = Tx(G.Section(""), key);
            t.Tag = "section";
            if (page.Children.Count == 0)
                t.Margin = new Thickness(14, 6, 0, 7);
            else
                t.Margin = new Thickness(14, 22, 0, 7);
            page.Children.Add(t);
        }

        static InsetGroup Group(StackPanel page)
        {
            var g = new InsetGroup(16);
            bool afterHeader = page.Children.Count > 0 && page.Children[page.Children.Count - 1] is TextBlock && ((TextBlock)page.Children[page.Children.Count - 1]).Tag as string == "section";
            g.Margin = new Thickness(0, page.Children.Count == 0 ? 6 : afterHeader ? 0 : 18, 0, 0);
            page.Children.Add(g);
            return g;
        }

        TextBlock Note(StackPanel page, string key)
        {
            TextBlock n = G.Note("");
            if (key != null)
                Tx(n, key);
            page.Children.Add(n);
            return n;
        }

        StackPanel Label(string key, string subKey)
        {
            TextBlock t, s;
            StackPanel sp = InsetGroup.Label("", subKey == null ? null : "-", out t, out s);
            Tx(t, key);
            if (subKey != null)
                Tx(s, subKey);
            return sp;
        }

        // Row title with a status line under it that only appears when there is something to say.
        static StackPanel StatusLabel(out TextBlock title, out TextBlock status, out GlassSpinner spin)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title = G.Text("", TextStyle.Body);
            sp.Children.Add(title);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
            spin = new GlassSpinner(12, 1.6) { Margin = new Thickness(0, 0, 7, 0), Visibility = Visibility.Collapsed };
            status = G.Text("", TextStyle.Footnote, GlassTheme.Secondary);
            status.TextWrapping = TextWrapping.Wrap;
            status.TextTrimming = TextTrimming.None;
            line.Children.Add(spin);
            line.Children.Add(status);
            sp.Children.Add(line);
            return sp;
        }

        void ToggleRow(InsetGroup g, string key, string subKey, bool value, Action<bool> set)
        {
            var t = new LiquidToggle(value);
            t.Toggled += v =>
            {
                set(v);
                Save();
            };
            g.AddRow(Label(key, subKey), t, subKey == null ? 46 : 56);
        }

        GlassButton SmallButton(string key, string glyph)
        {
            var b = new GlassButton(L.T(key), ButtonKind.Secondary, glyph, 30);
            if (key != "btn.create")
                texts.Add(() => b.Text = L.T(key));
            else
                texts.Add(() =>
                {
                    if (!shortcutMade)
                        b.Text = L.T(key);
                });
            return b;
        }

        void Save()
        {
            if (!preview)
                cfg.Save();
        }

        // ---------------- behaviour ----------------

        void Select(int i, bool animate)
        {
            if (i < 0 || i >= pages.Count || i == current)
                return;
            int from = current;
            current = i;
            for (int k = 0; k < items.Count; k++)
                items[k].Selected = k == i;
            double y = i * (ItemHeight + ItemGap);
            if (from < 0 || !animate || GlassTheme.ReduceMotion)
            {
                pillMove.BeginAnimation(TranslateTransform.YProperty, null);
                pillMove.Y = y;
                pill.Opacity = 1;
            }
            else
                pillMove.BeginAnimation(TranslateTransform.YProperty, Motion.To(y, 460, SpringEase.Soft));
            G.SetText(pageTitle, L.T(Keys[i]));
            FrameworkElement page = pages[i];
            scroll.Content = page;
            scroll.JumpTo(0);
            if (animate && !GlassTheme.ReduceMotion)
            {
                // Fade only: a moving page would leave the panes' blurred backdrop misaligned.
                page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D(200)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                pageTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, Motion.D(200)));
            }
        }

        void ApplyTexts()
        {
            Title = L.T("settings.title");
            G.SetText(TitleBlock, Title);
            G.SetText(versionLine, L.F("settings.version", AppSetup.AppVersion));
            for (int i = 0; i < items.Count; i++)
                items[i].Text = L.T(Keys[i]);
            if (current >= 0)
                G.SetText(pageTitle, L.T(Keys[current]));
            foreach (Action a in texts)
                a();
            CheckEndpoint(false);
            CheckPorts(false);
            RefreshVersions();
        }

        void ThemeChanged()
        {
            done.Kind = ButtonKind.Primary; // the primary button's glow follows the tone
            foreach (ToneTile t in tones)
                t.SetSelected(t.Tone == GlassTheme.P.Tone, true);
        }

        void PickTone(GlassTone tone)
        {
            if (tone == GlassTheme.P.Tone)
                return;
            if (preview)
                GlassTheme.Apply(tone, true);
            else
                GlassApp.SetTone(app, tone);
        }

        static void SetNote(TextBlock note, string text, bool error)
        {
            G.SetText(note, text);
            note.Foreground = error ? GlassTheme.Danger : GlassTheme.Tertiary;
        }

        // Same rule as the classic window: empty, or host:port with a valid port.
        void CheckEndpoint(bool save)
        {
            string v = endpoint.Text.Trim();
            int colon = v.LastIndexOf(':');
            int port;
            bool ok = v.Length == 0 || (colon > 0 && int.TryParse(v.Substring(colon + 1), out port) && port > 0 && port <= 65535);
            endpoint.Invalid = !ok;
            SetNote(endpointNote, L.T(ok ? "settings.endpoint.hint" : "settings.endpoint.invalid"), !ok);
            if (ok && save)
            {
                cfg.WarpEndpoint = v;
                Save();
            }
        }

        void CheckPorts(bool save)
        {
            int a, b;
            bool okA = int.TryParse(httpPort.Text.Trim(), out a) && a > 0 && a <= 65535;
            bool okB = int.TryParse(socksPort.Text.Trim(), out b) && b > 0 && b <= 65535;
            bool same = okA && okB && a == b;
            httpPort.Invalid = !okA || same;
            socksPort.Invalid = !okB || same;
            if (!okA || !okB)
            {
                SetNote(portNote, L.T("settings.port.invalid"), true);
                return;
            }
            if (same)
            {
                SetNote(portNote, L.T("settings.port.same"), true);
                return;
            }
            SetNote(portNote, L.T("settings.ports.hint"), false);
            if (save)
            {
                cfg.GostPort = a.ToString();
                cfg.MainProxyPort = b.ToString();
                Save();
            }
        }

        void RefreshVersions()
        {
            string tor = null;
            try
            {
                tor = app.TorVersion();
            }
            catch { }
            G.SetText(torTitle, string.IsNullOrEmpty(tor) ? L.T("settings.tor.unknown") : L.F("settings.tor.version", tor));
            G.SetText(appTitle, L.F("settings.app.version", AppSetup.AppVersion));
        }

        static void ShowStatus(TextBlock status, GlassSpinner spin, string text, bool busy)
        {
            G.SetText(status, text);
            var line = (FrameworkElement)status.Parent;
            bool show = !string.IsNullOrEmpty(text);
            if (show && line.Visibility != Visibility.Visible)
                line.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D(180)));
            line.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            spin.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            spin.Running = busy;
        }

        async Task RunUpdate(GlassButton button, TextBlock status, GlassSpinner spin, Func<Task<string>> action)
        {
            if (preview)
                return;
            button.IsEnabled = false;
            ShowStatus(status, spin, L.T("update.checking"), true);
            string result;
            try
            {
                result = await action();
            }
            catch (Exception ex)
            {
                app.Logger.Log("Update check failed: " + ex.Message, LogType.ERROR);
                result = L.T("update.failed");
            }
            ShowStatus(status, spin, result, false);
            button.IsEnabled = true;
            RefreshVersions();
        }

        // ---------------- design check ----------------

        // States: general, appearance, connection, updates, trouble; "invalid" shows the
        // validation messages; "checking" shows an update check in progress.
        public static Window CreatePreview(AppController app, string state)
        {
            app.Config.Language = L.Lang;
            if (state == "invalid")
            {
                app.Config.WarpEndpoint = "162.159.192.1";
                app.Config.GostPort = "42212";
                app.Config.MainProxyPort = "42212";
                app.Config.TorBridges = "obfs4 192.0.2.10:443 4352E58420E68F5E40BF7C74FADDCCD9D1349413 cert=Ft2… iat-mode=0\r\nwebtunnel [2001:db8::1]:443 2852538D49D7D73E… url=https://example.org/path";
            }
            var w = new GlassSettingsWindow(app, null);
            int i = Array.IndexOf(Ids, state);
            if (state == "invalid")
                i = Array.IndexOf(Ids, "connection");
            if (state == "checking")
            {
                i = Array.IndexOf(Ids, "updates");
                ShowStatus(w.torStatus, w.torSpin, L.T("update.checking"), true);
                w.torCheck.IsEnabled = false;
                ShowStatus(w.appStatus, w.appSpin, L.T("update.latest"), false);
            }
            w.Select(Math.Max(0, i), false);
            return w;
        }

        // One sidebar entry: coloured icon tile and the section name.
        class SidebarItem : Grid
        {
            readonly Border hover;
            readonly TextBlock label;
            bool selected;

            public SidebarItem(string glyph, Color top, Color bottom)
            {
                Height = ItemHeight;
                Background = Brushes.Transparent;
                Cursor = Cursors.Hand;
                hover = new Border { CornerRadius = new CornerRadius(10), Background = GlassTheme.Hover, Opacity = 0 };
                Children.Add(hover);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var fill = new LinearGradientBrush(top, bottom, 90);
                fill.Freeze();
                var tile = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(7),
                    Background = fill,
                    Child = G.Icon(glyph, 13, GlassTheme.OnAccent),
                    Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.18, Color = Colors.Black, Direction = 270 },
                };
                row.Children.Add(tile);
                label = G.Text("", TextStyle.Body);
                label.Margin = new Thickness(10, 0, 0, 1);
                row.Children.Add(label);
                Children.Add(row);
                MouseEnter += (s, e) =>
                {
                    if (!selected)
                        hover.BeginAnimation(OpacityProperty, Motion.To(1, 140, new QuadraticEase()));
                };
                MouseLeave += (s, e) => hover.BeginAnimation(OpacityProperty, Motion.To(0, 220, new QuadraticEase()));
            }

            public string Text
            {
                set { G.SetText(label, value); }
            }

            public bool Selected
            {
                set
                {
                    selected = value;
                    label.FontWeight = value ? FontWeights.SemiBold : FontWeights.Normal;
                    if (value)
                        hover.BeginAnimation(OpacityProperty, Motion.To(0, 120, new QuadraticEase()));
                }
            }
        }

        // Background tone swatch: a miniature of the window in that tone, the name under it,
        // an accent ring around the current one.
        class ToneTile : StackPanel
        {
            public readonly GlassTone Tone;
            public readonly TextBlock Label;
            readonly Border ring;
            readonly ScaleTransform press = new ScaleTransform(1, 1);
            bool selected;

            public ToneTile(GlassTone tone, Action<GlassTone> pick)
            {
                Tone = tone;
                HorizontalAlignment = HorizontalAlignment.Center;
                Cursor = Cursors.Hand;
                Background = Brushes.Transparent;
                var frame = new Grid { Width = 132, Height = 90, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = press };
                ring = new Border
                {
                    CornerRadius = new CornerRadius(15),
                    BorderBrush = GlassTheme.Accent,
                    BorderThickness = new Thickness(2.5),
                    Opacity = 0,
                };
                frame.Children.Add(ring);
                frame.Children.Add(Miniature(TonePalette.For(tone)));
                Children.Add(frame);
                Label = G.Text("", TextStyle.Callout, GlassTheme.Secondary);
                Label.HorizontalAlignment = HorizontalAlignment.Center;
                Label.Margin = new Thickness(0, 8, 0, 0);
                Children.Add(Label);
                G.Press(frame, press);
                MouseEnter += (s, e) =>
                {
                    if (!selected)
                        ring.BeginAnimation(OpacityProperty, Motion.To(0.35, 140, new QuadraticEase()));
                };
                MouseLeave += (s, e) =>
                {
                    if (!selected)
                        ring.BeginAnimation(OpacityProperty, Motion.To(0, 220, new QuadraticEase()));
                };
                MouseLeftButtonUp += (s, e) => pick(Tone);
            }

            public void SetSelected(bool value, bool animate)
            {
                selected = value;
                Label.Foreground = value ? GlassTheme.Text : GlassTheme.Secondary;
                Label.FontWeight = value ? FontWeights.SemiBold : FontWeights.Normal;
                double to = value ? 1 : IsMouseOver ? 0.35 : 0;
                if (animate)
                    ring.BeginAnimation(OpacityProperty, Motion.To(to, 220, new QuadraticEase()));
                else
                {
                    ring.BeginAnimation(OpacityProperty, null);
                    ring.Opacity = to;
                }
            }

            // Fixed colours on purpose: it shows that tone, whatever tone is active.
            static FrameworkElement Miniature(TonePalette p)
            {
                const double W = 121, H = 79;
                var card = new Border
                {
                    Width = W,
                    Height = H,
                    CornerRadius = new CornerRadius(11),
                    BorderBrush = Frozen(new SolidColorBrush(p.IsLight ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(50, 255, 255, 255))),
                    BorderThickness = new Thickness(1),
                    Background = Frozen(new LinearGradientBrush(p.BaseTop, p.BaseBottom, 90)),
                };
                var canvas = new Canvas { ClipToBounds = true, Width = W - 2, Height = H - 2 };
                canvas.Clip = new RectangleGeometry(new Rect(0, 0, W - 2, H - 2), 10, 10);
                double[,] blobs = { { -26, -30, 92, 0 }, { 54, 18, 96, 1 }, { -14, 40, 74, 2 }, { 76, -34, 70, 3 } };
                for (int i = 0; i < blobs.GetLength(0); i++)
                {
                    Color c = p.Blobs[(int)blobs[i, 3] % p.Blobs.Length];
                    byte a0 = (byte)(210 * p.BlobOpacity), a1 = (byte)(90 * p.BlobOpacity);
                    var rg = new RadialGradientBrush();
                    rg.GradientStops.Add(new GradientStop(Color.FromArgb(a0, c.R, c.G, c.B), 0));
                    rg.GradientStops.Add(new GradientStop(Color.FromArgb(a1, c.R, c.G, c.B), 0.45));
                    rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                    var e = new Ellipse { Width = blobs[i, 2], Height = blobs[i, 2], Fill = Frozen(rg) };
                    Canvas.SetLeft(e, blobs[i, 0]);
                    Canvas.SetTop(e, blobs[i, 1]);
                    canvas.Children.Add(e);
                }
                // a mini window: two text lines and a filled button
                var glass = Frozen(new LinearGradientBrush(p.GlassTop, p.GlassBottom, 90));
                var rimBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1) };
                rimBrush.GradientStops.Add(new GradientStop(p.RimTop, 0));
                rimBrush.GradientStops.Add(new GradientStop(p.RimMid, 0.5));
                rimBrush.GradientStops.Add(new GradientStop(p.RimBottom, 1));
                rimBrush.Freeze();
                var pane = new Border { Width = 74, Height = 50, CornerRadius = new CornerRadius(8), Background = glass, BorderBrush = rimBrush, BorderThickness = new Thickness(1) };
                var lines = new StackPanel { Margin = new Thickness(8, 9, 8, 0) };
                lines.Children.Add(new Rectangle { Width = 38, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = Frozen(new SolidColorBrush(p.Text)), HorizontalAlignment = HorizontalAlignment.Left });
                lines.Children.Add(new Rectangle { Width = 26, Height = 4, RadiusX = 2, RadiusY = 2, Fill = Frozen(new SolidColorBrush(p.Secondary)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) });
                lines.Children.Add(new Rectangle { Width = 26, Height = 10, RadiusX = 5, RadiusY = 5, Fill = Frozen(new LinearGradientBrush(p.AccentTop, p.AccentBottom, 90)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0) });
                pane.Child = lines;
                Canvas.SetLeft(pane, 9);
                Canvas.SetTop(pane, 12);
                canvas.Children.Add(pane);
                // a second pane with a switch
                var pane2 = new Border { Width = 28, Height = 50, CornerRadius = new CornerRadius(8), Background = glass, BorderBrush = rimBrush, BorderThickness = new Thickness(1) };
                var sw = new Border { Width = 16, Height = 9, CornerRadius = new CornerRadius(4.5), Background = Frozen(new LinearGradientBrush(p.AccentTop, p.AccentBottom, 90)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 0, 0) };
                sw.Child = new Ellipse { Width = 7, Height = 7, Fill = Frozen(new SolidColorBrush(p.Knob)), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 1, 0) };
                var sw2 = new Border { Width = 16, Height = 9, CornerRadius = new CornerRadius(4.5), Background = Frozen(new SolidColorBrush(p.ToggleOff)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0) };
                sw2.Child = new Ellipse { Width = 7, Height = 7, Fill = Frozen(new SolidColorBrush(p.Knob)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1, 0, 0, 0) };
                var g2 = new Grid();
                g2.Children.Add(sw);
                g2.Children.Add(sw2);
                pane2.Child = g2;
                Canvas.SetLeft(pane2, 88);
                Canvas.SetTop(pane2, 12);
                canvas.Children.Add(pane2);
                card.Child = canvas;
                return card;
            }

            static T Frozen<T>(T b) where T : Freezable
            {
                b.Freeze();
                return b;
            }
        }
    }
}
