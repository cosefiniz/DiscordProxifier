using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    public enum NoteKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    public enum StatusKind
    {
        Down,
        Connecting,
        Connected,
        Unhealthy,
        Failed
    }

    // Everything the status card shows, computed once for both looks.
    public class StatusInfo
    {
        public StatusKind Kind;
        public string Title, Sub;
        public float? Progress;   // null = no bar, < 0 = indeterminate, 0..1
        public string ButtonText;
        public bool ButtonPrimary;
        public string RouteHint;
        public string ModeName;
    }

    // What a window must provide to show the app. The classic WinForms window and the
    // Liquid Glass WPF window both implement it; all behaviour lives in AppController.
    public interface IAppView
    {
        bool WindowVisible { get; }      // shown (possibly minimized)
        bool IsShownToUser { get; }      // shown and not minimized
        bool IsInFront { get; }          // the window is the active one (in-app toasts are seen)
        AppError Card { get; }           // the error/info card on screen, null if none
        void ShowCard(AppError e);
        // Hides the card. notify = true: treat it as closed (the view then calls
        // AppController.OnCardDismissed); false: hidden by the app, e.g. on reconnect.
        void HideCard(bool notify);
        void ShowToast(NoteKind kind, string title, string sub);
        void RefreshStatus();
        void RefreshClient();
        void ModeChanged(bool animate);  // the selected route changed (auto route, fallback, tray)
        bool Confirm(string title, string body);
        string PickFolder(string description, string startPath);
        void ShowSettings(string focus); // modal
        Task ShowRepairAsync(Func<IRepairView, Task> run); // modal; run starts once it is shown
        void ShowLogs();
        void ShowWindow();
        void HideWindow();
        void Shutdown();                 // end the UI message loop
    }

    public interface IRepairView
    {
        int Step(string title);
        void Done(int index, int status, string detail); // 0 ok, 1 warning, 2 failed
        void Finish(bool allGood);
    }

    // The app's behaviour, independent of how it looks: connecting, the automatic route,
    // the watchdog, updates, repair, Discord installs and the tray icon.
    public class AppController
    {
        public readonly Configuration Config;
        public readonly Logger Logger;
        public readonly ProxyManager Proxy;
        public readonly DiscordClients Discord;
        public readonly Updater Updater;
        IAppView view;
        SynchronizationContext ui;
        TrayIcon tray;
        Notifier notifier;
        readonly List<DateTime> reconnects = new List<DateTime>();

        ManifestFile pendingUpdate;
        string updateDismissedFor; // the user closed the card for this version: don't show it again
        string updateBalloonFor;   // tray balloon already shown for this version
        string torUpdateNotified;
        DateTime nextUpdateCheck = DateTime.MaxValue;
        DateTime lastUpdateCheck = DateTime.MinValue;
        bool updateChecking;

        // Set by Program when this process was started by a self-update.
        public bool JustUpdated { get; set; }
        // Set by Program when started with Windows (--tray): routine notes stay quiet for a while.
        public bool StartedHidden { get; set; }
        public List<DiscordClient> Clients { get; private set; } = new List<DiscordClient>();
        public DiscordClient Selected { get; private set; }
        public ClientState ClientState { get; private set; } = ClientState.Stopped;
        public ProxyMode Mode { get; private set; }
        public bool Testing { get; private set; }
        public bool Launching { get; private set; }
        public ManifestFile PendingUpdate => pendingUpdate;
        bool initialized;
        bool exiting;
        int tick;
        bool polling;

        // Watchdog: periodic health checks, plus a check shortly after resume / network change.
        DateTime nextHealthCheck = DateTime.MaxValue;
        DateTime pendingCheck = DateTime.MaxValue;
        string pendingReason;
        bool checking;
        int healthFails;
        DateTime lastReconnect = DateTime.MinValue;
        bool lastConnectFailed;
        DateTime nextDirectCheck = DateTime.MaxValue;

        static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);
        static readonly TimeSpan UpdateRetryInterval = TimeSpan.FromMinutes(30);

        public AppController(Configuration cfg, Logger logger)
        {
            Config = cfg;
            Logger = logger;
            Mode = ProxyModes.Parse(cfg.Mode);
            Proxy = new ProxyManager(logger, AppSetup.DataDir);
            Proxy.FellBackToTor += () => Post(() => Notify(NoteKind.Warning, L.T("toast.fallback"), null, NoteGroup.Connection));
            Discord = new DiscordClients(logger, AppSetup.DataDir);
            Updater = new Updater(logger, AppSetup.DataDir);
        }

        public bool Exiting => exiting;

        // Called once by the window, on the UI thread, before anything else.
        public void Attach(IAppView v)
        {
            view = v;
            ui = SynchronizationContext.Current;
            tray = new TrayIcon(this);
            try
            {
                notifier = new Notifier(Config, new Glass.BannerStack(), Logger);
                if (StartedHidden)
                    notifier.QuietFor(TimeSpan.FromMinutes(2));
            }
            catch (Exception ex)
            {
                Logger.Log("Notifications fall back to the tray: " + ex.Message, LogType.WARNING);
            }
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            L.Changed += () => tray.ApplyTexts();
        }

        void Post(Action a)
        {
            if (ui == null || SynchronizationContext.Current == ui)
                a();
            else
                ui.Post(_ => a(), null);
        }

        // ---------------- lifecycle ----------------

        public async Task Init()
        {
            if (initialized)
                return;
            initialized = true;
            var clock = Stopwatch.StartNew();
            Logger.Log($"Discord Proxifier {AppSetup.AppVersion} starting");
            Proxy.KillOrphans();
            Rescan();
            view.RefreshStatus();
            RefreshTray();
            AppSetup.SetAutoStart(Config.OpenAtStartup, Logger);

            // Connect right away with the last used mode; nothing below waits on the network first.
            List<string> missing = await AppSetup.PrepareLocalDataAsync(Logger);
            Task connecting = Config.AutoConnect && missing.Count == 0 ? Connect() : null;
            Logger.Log($"Startup ready in {clock.ElapsedMilliseconds} ms");
            if (JustUpdated)
                Notify(NoteKind.Success, L.F("toast.updated", AppSetup.AppVersion), L.T("toast.updated.sub"), NoteGroup.Update);

            await CheckUpdatesAsync(missing);
            if (connecting != null)
                await connecting;
            else if (Config.AutoConnect && !Proxy.IsRunning && !Proxy.IsStarting && (await AppSetup.PrepareLocalDataAsync(Logger)).Count == 0)
                await Connect();
        }

        // Called by the window every 500 ms.
        public void Tick()
        {
            if (!initialized || exiting)
                return;
            view.RefreshStatus();
            RefreshTray();
            if (++tick % 6 == 0)
                PollClient();
            if (!checking && (DateTime.Now >= pendingCheck || DateTime.Now >= nextHealthCheck))
                _ = WatchdogAsync();
            if (!updateChecking && DateTime.Now >= nextUpdateCheck)
                _ = PeriodicUpdateCheck();
            ShowPendingUpdate();
            notifier?.Tick();
        }

        // The window is closing: stop everything (the app ends with it).
        public void OnViewClosing()
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            if (!exiting)
            {
                exiting = true;
                Proxy.StopNow();
                tray?.Hide();
            }
            notifier?.CloseAll();
        }

        public void Exit()
        {
            exiting = true;
            Proxy.StopNow();
            tray?.Hide();
            notifier?.CloseAll();
            view.Shutdown();
        }

        public void HideToTray()
        {
            view.HideWindow();
            Notify(NoteKind.Info, L.T("toast.tray"), null);
        }

        public void ShowFromTray()
        {
            view.ShowWindow();
            if (DateTime.Now - lastUpdateCheck > TimeSpan.FromHours(1) && lastUpdateCheck != DateTime.MinValue)
                nextUpdateCheck = DateTime.Now; // Tick picks it up
        }

        // Switches between the Liquid Glass and the classic look: saves it and restarts the app.
        public void SwitchAppearance(string appearance)
        {
            if (Config.Appearance == appearance)
                return;
            Config.Appearance = appearance;
            Config.Save();
            Logger.Log("Appearance changed to " + appearance + ", restarting");
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--wait-pid " + Process.GetCurrentProcess().Id)
                {
                    UseShellExecute = true,
                    WorkingDirectory = AppContext.BaseDirectory
                };
                exiting = true;
                Proxy.StopNow();
                tray?.Hide();
                notifier?.CloseAll();
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Logger.Log("Could not restart: " + ex.Message, LogType.ERROR);
            }
            view.Shutdown();
        }

        // ---------------- updates ----------------

        // Shows the update card for the pending version, but never over an error card: it waits
        // until that card is gone (Tick calls this every time). A card the user closed stays closed;
        // one hidden for another reason (reconnect, an error) comes back.
        void ShowPendingUpdate()
        {
            if (pendingUpdate == null || updateDismissedFor == pendingUpdate.Version || view.Card != null || exiting)
                return;
            bool balloon = updateBalloonFor != pendingUpdate.Version;
            updateBalloonFor = pendingUpdate.Version;
            ShowError(AppError.For("UPDATE", pendingUpdate.Version), balloon);
        }

        // While the app keeps running (tray, started with Windows) it checks again every 6 hours,
        // and when the window is opened from the tray if the last check is over an hour old.
        async Task PeriodicUpdateCheck()
        {
            updateChecking = true;
            try
            {
                Logger.Log("Checking for updates");
                await CheckUpdatesAsync(new List<string>());
            }
            finally
            {
                updateChecking = false;
            }
        }

        // Background update check against our own repo: fills in missing files, updates
        // version.dll automatically, and announces Tor / app updates.
        async Task CheckUpdatesAsync(List<string> missing)
        {
            lastUpdateCheck = DateTime.Now;
            nextUpdateCheck = DateTime.Now + UpdateInterval;
            Manifest m = await Updater.FetchManifestAsync();
            if (m == null)
            {
                // Offline (e.g. started with Windows before the network was up): try again sooner.
                nextUpdateCheck = DateTime.Now + UpdateRetryInterval;
                if (missing.Count > 0)
                    ShowError(missing.Contains("x64/version.dll") ? AppError.For("DP-502") : AppError.For("DP-103", missing[0]));
                return;
            }
            if (missing.Count > 0)
                await Updater.EnsureFilesAsync(m);
            bool dllUpdated = await Updater.UpdateDllAsync(m);
            if (!AppSetup.HasDll)
                ShowError(AppError.For("DP-502"));

            bool torUpdate = await Task.Run(() => Updater.TorUpdateAvailable(m));
            pendingUpdate = Updater.AppUpdate(m);
            if (pendingUpdate != null)
            {
                ShowPendingUpdate(); // now, or as soon as the error card is gone
            }
            else if (Updater.IsNewer(m.AppVersion, AppSetup.AppVersion))
                Notify(NoteKind.Info, L.F("toast.update.app", m.AppVersion), L.T("toast.update.sub"), NoteGroup.Update);
            else if (torUpdate && torUpdateNotified != m.Get("Tor.zip")?.Version)
            {
                torUpdateNotified = m.Get("Tor.zip")?.Version;
                Notify(NoteKind.Info, L.T("toast.update.tor"), L.T("toast.update.sub"), NoteGroup.Update);
            }
            else if (dllUpdated)
                Notify(NoteKind.Info, L.F("toast.dll.updated", Updater.InstalledDllVersion), null);
        }

        // Settings → installed Tor version line.
        public string TorVersion()
        {
            return Updater.InstalledTorVersion();
        }

        // Settings → Update Tor. Stops Tor if it is the active backend, swaps the files, reconnects.
        public async Task<string> UpdateTor()
        {
            if (!UpdateSource.Configured)
                return L.T("update.notconfigured");
            Manifest m = await Updater.FetchManifestAsync();
            if (m == null)
                return L.T("update.offline");
            if (!await Task.Run(() => Updater.TorUpdateAvailable(m)))
                return L.T("update.latest");
            string version = m.Get("Tor.zip").Version;
            string zip = await Updater.DownloadTorAsync(m);
            if (zip == null)
                return L.T("update.failed");
            bool torActive = (Proxy.IsRunning || Proxy.IsStarting) && Mode == ProxyMode.Tor;
            if (torActive)
                await Proxy.StopAsync();
            bool ok = await Task.Run(() => Updater.InstallTorZip(zip, version));
            if (torActive)
                _ = Connect();
            view.RefreshStatus();
            return ok ? L.F("update.done", version) : L.T("update.failed");
        }

        // Settings → Check for updates. Installs a newer build after asking.
        public async Task<string> CheckAppUpdate()
        {
            if (!UpdateSource.Configured)
                return L.T("update.notconfigured");
            Manifest m = await Updater.FetchManifestAsync();
            if (m == null)
                return L.T("update.offline");
            bool dll = await Updater.UpdateDllAsync(m);
            if (!Updater.IsNewer(m.AppVersion, AppSetup.AppVersion))
                return dll ? L.F("toast.dll.updated", Updater.InstalledDllVersion) : L.T("update.latest");
            ManifestFile exe = Updater.AppUpdate(m);
            if (exe != null)
            {
                if (!view.Confirm("Discord Proxifier", L.F("update.confirm", exe.Version)))
                    return L.F("UPDATE.t", exe.Version);
                await InstallUpdateAsync(exe);
                return L.T("update.install.failed"); // only reached when the install didn't restart the app
            }
            OpenUrl(string.IsNullOrEmpty(m.AppDownload) ? UpdateSource.ReleasesPage : m.AppDownload);
            return L.F("update.available", m.AppVersion);
        }

        // Downloads, verifies and installs the new exe, then restarts into it. On any failure the
        // running version keeps working and the download page opens instead.
        async Task InstallUpdateAsync(ManifestFile f)
        {
            Notify(NoteKind.Info, L.T("toast.updating"), L.F("UPDATE.t", f.Version), NoteGroup.Update);
            string newExe = await Updater.DownloadAppAsync(f);
            if (newExe != null)
            {
                bool wasHidden = !view.WindowVisible;
                exiting = true;
                Proxy.StopNow();
                if (Updater.SwapAndRestart(newExe, wasHidden))
                {
                    tray?.Hide();
                    notifier?.CloseAll();
                    view.Shutdown();
                    return;
                }
                exiting = false;
                await Connect();
            }
            Notify(NoteKind.Error, L.T("update.install.failed"), L.T("update.install.failed.sub"), NoteGroup.Update);
            OpenUrl(UpdateSource.ReleasesPage);
        }

        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch { }
        }

        // ---------------- repair and report ----------------

        public Task OpenRepair()
        {
            return view.ShowRepairAsync(RunRepairAsync);
        }

        // Checks and fixes files, leftovers, Discord and the connection, showing each step live.
        async Task RunRepairAsync(IRepairView w)
        {
            bool allGood = true;
            Action<int, int, string> done = (i, status, detail) =>
            {
                if (status != 0)
                    allGood = false;
                w.Done(i, status, detail);
            };
            Logger.Log("Repair started");

            // 1. Program files against the published checksums.
            int s1 = w.Step(L.T("repair.files"));
            List<string> missing = await AppSetup.PrepareLocalDataAsync(Logger);
            Manifest m = await Updater.FetchManifestAsync();
            if (m == null)
            {
                done(s1, missing.Count == 0 ? 1 : 2, missing.Count == 0 ? L.T("repair.files.unverified") : L.F("repair.files.missing", string.Join(", ", missing)));
            }
            else
            {
                List<string> bad = await Task.Run(() => Updater.VerifyDataFiles(m));
                if (bad.Count == 0)
                {
                    done(s1, 0, L.T("repair.files.ok"));
                }
                else
                {
                    if (Proxy.IsRunning || Proxy.IsStarting)
                        await Proxy.StopAsync(); // files in use can't be replaced
                    var failed = new List<string>();
                    foreach (string key in bad)
                    {
                        string target = System.IO.Path.Combine(AppSetup.DataDir, key.Replace('/', '\\'));
                        bool ok = await Updater.DownloadVerifiedAsync(m.Get(key), target);
                        if (ok && key == "Tor.zip")
                            ok = await Task.Run(() => Updater.InstallTorZip(target, m.Get(key).Version));
                        if (!ok)
                            failed.Add(key);
                    }
                    done(s1, failed.Count == 0 ? 0 : 2, failed.Count == 0
                        ? L.F("repair.files.fixed", bad.Count)
                        : L.F("repair.files.failed", string.Join(", ", failed)));
                }
            }

            // 2. tor/gost/wireproxy left behind by other copies.
            int s2 = w.Step(L.T("repair.leftovers"));
            int closed = 0;
            foreach (Process p in Proxy.FindForeignLeftovers())
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                    closed++;
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
            Proxy.KillOrphans();
            done(s2, 0, closed == 0 ? L.T("repair.leftovers.none") : L.F("repair.leftovers.closed", closed));

            // 3. Discord install, proxy module, shortcuts.
            int s3 = w.Step(L.T("repair.discord"));
            Rescan();
            bool restartDiscord = false;
            if (Selected == null)
            {
                done(s3, 2, L.T("client.none"));
            }
            else
            {
                DiscordClient c = Selected;
                bool killed = false;
                bool ok = await Task.Run(() => Discord.EnsureDll(c, out killed));
                Discord.FixProxifiedShortcuts(Clients, Config.GostPort);
                restartDiscord = killed;
                done(s3, ok ? 0 : 2, L.F(ok ? (killed ? "repair.discord.restarted" : "repair.discord.ok") : "repair.discord.failed", c.DisplayName));
            }

            // 4. The connection itself.
            int s4 = w.Step(L.T("repair.conn"));
            ProbeResult health = Proxy.IsRunning ? await Proxy.HealthCheckAsync() : null;
            if (health == null || !health.Ok)
                await Connect();
            if (Proxy.IsRunning)
                done(s4, 0, L.F("repair.conn.ok", ProxyModes.Display(Mode)));
            else
                done(s4, 2, L.T("repair.conn.failed"));
            if (restartDiscord && Proxy.IsRunning)
                await LaunchSelected();

            Logger.Log("Repair finished" + (allGood ? "" : " with problems"));
            w.Finish(allGood);
        }

        // Writes the diagnostic report to the desktop and selects it in Explorer.
        public async Task<string> CreateReport()
        {
            var ctx = new Diagnostics.Context
            {
                Config = Config,
                Proxy = Proxy,
                Discord = Discord,
                Clients = Clients,
                Selected = Selected,
                Updater = Updater,
                Logger = Logger
            };
            try
            {
                string text = await Task.Run(() => Diagnostics.BuildReportAsync(ctx));
                string path = Diagnostics.Save(text);
                Logger.Log("Diagnostic report saved: " + Diagnostics.Mask(path));
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
                return L.T("report.saved");
            }
            catch (Exception ex)
            {
                Logger.Log("Could not create the report: " + ex.Message, LogType.ERROR);
                return L.T("update.failed");
            }
        }

        // ---------------- watchdog ----------------

        void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                Post(() => ScheduleCheck("reason.resume"));
        }

        void OnNetworkChanged(object sender, EventArgs e)
        {
            Post(() => ScheduleCheck("reason.network"));
        }

        // Network events come in bursts; check once things have settled.
        void ScheduleCheck(string reason)
        {
            if (exiting)
                return;
            pendingReason = reason;
            pendingCheck = DateTime.Now.AddSeconds(6);
        }

        // Keeps the connection alive without the user noticing: reconnects when a part died,
        // when two health checks in a row fail, or when a check after resume / network change
        // fails. On Tor, a 403/429 from Discord only needs a new exit, not a reconnect.
        async Task WatchdogAsync()
        {
            checking = true;
            try
            {
                bool triggered = DateTime.Now >= pendingCheck;
                string reason = triggered ? pendingReason : null;
                pendingCheck = DateTime.MaxValue;
                nextHealthCheck = DateTime.Now.AddSeconds(60);
                if (exiting || Proxy.IsStarting)
                    return;
                if (!Proxy.IsRunning)
                {
                    // A failed auto-connect gets another chance once the network changes.
                    if (triggered && Config.AutoConnect && lastConnectFailed)
                        await Reconnect(reason);
                    return;
                }
                if (Proxy.DeadPart != null)
                {
                    await Reconnect("reason.dropped");
                    return;
                }
                ProbeResult r = await Proxy.HealthCheckAsync();
                if (r == null || r.Ok)
                {
                    healthFails = 0;
                    await TryUpgradeToDirect();
                    return;
                }
                if (!await DirectBackend.InternetUpAsync())
                {
                    WaitForInternet(reason ?? "reason.dropped");
                    return;
                }
                Logger.Log("Watchdog: health check failed: " + r.Detail, LogType.WARNING);
                if (Proxy.ActiveMode == ProxyMode.Tor && (r.Status == 403 || r.Status == 429))
                {
                    if (await Proxy.NewCircuitAsync())
                        Notify(NoteKind.Info, L.T("toast.newcircuit"), L.T("toast.newcircuit.sub"), NoteGroup.Connection);
                    return;
                }
                if (triggered || ++healthFails >= 2)
                {
                    healthFails = 0;
                    await Reconnect(reason ?? "reason.dropped");
                }
            }
            finally
            {
                checking = false;
            }
        }

        void WaitForInternet(string reasonKey)
        {
            Logger.Log("Watchdog: no internet, waiting for the network");
            pendingReason = reasonKey;
            pendingCheck = DateTime.Now.AddSeconds(8);
        }

        // With the automatic route on a slower route (WARP/Tor), every few minutes check whether
        // Direct works again and move back to it; a temporary outage shouldn't leave the app on Tor.
        async Task TryUpgradeToDirect()
        {
            if (!Config.AutoRoute || Proxy.ActiveMode == ProxyMode.Direct || DateTime.Now < nextDirectCheck)
                return;
            nextDirectCheck = DateTime.Now.AddMinutes(10);
            if (!await DirectBackend.WorksNowAsync(TimeSpan.FromSeconds(6)))
                return;
            Logger.Log("Auto route: Direct works again, switching back");
            SetMode(ProxyMode.Direct, true);
            Task<bool> sw = Proxy.SwitchModeAsync(ProxyMode.Direct, Config);
            view.RefreshStatus();
            AfterStart(await sw, true);
        }

        async Task Reconnect(string reasonKey)
        {
            if (!await DirectBackend.InternetUpAsync())
            {
                WaitForInternet(reasonKey);
                return;
            }
            if (DateTime.Now - lastReconnect < TimeSpan.FromSeconds(30))
                return;
            lastReconnect = DateTime.Now;
            Logger.Log($"Watchdog: reconnecting ({reasonKey})");
            await Connect(reasonKey);
        }

        // ---------------- status ----------------

        // Previews (--glass-preview) show a fixed state without connecting.
        public StatusInfo StatusOverride { get; set; }

        public void SetPreviewClient(DiscordClient c, ClientState state)
        {
            Clients = c == null ? new List<DiscordClient>() : new List<DiscordClient> { c };
            Selected = c;
            ClientState = state;
        }

        public StatusInfo Status
        {
            get
            {
                if (StatusOverride != null)
                    return StatusOverride;
                var s = new StatusInfo { ModeName = ProxyModes.Display(Mode) };
                AppError card = view?.Card;
                AppError err = card != null && !card.IsInfo ? card : null;
                if (Proxy.IsStarting)
                {
                    s.Kind = StatusKind.Connecting;
                    s.Title = L.T("status.connecting");
                    s.Sub = Proxy.Progress;
                    s.Progress = Proxy.Percent >= 0 ? Proxy.Percent / 100f : -1;
                    s.ButtonText = L.T("btn.cancel");
                }
                else if (Proxy.IsRunning)
                {
                    string dead = Proxy.DeadPart;
                    if (dead != null)
                    {
                        s.Kind = StatusKind.Unhealthy;
                        s.Title = L.T("status.error");
                        s.Sub = L.F("status.sub.unhealthy", dead);
                    }
                    else
                    {
                        s.Kind = StatusKind.Connected;
                        s.Title = L.T("status.connected");
                        s.Sub = L.F("status.sub.connected", Proxy.LastLatencyMs, s.ModeName);
                    }
                    s.ButtonText = L.T("btn.disconnect");
                }
                else if (err != null)
                {
                    s.Kind = StatusKind.Failed;
                    s.Title = L.T("status.error");
                    s.Sub = err.Title;
                    s.ButtonText = L.T("btn.connect");
                    s.ButtonPrimary = true;
                }
                else
                {
                    s.Kind = StatusKind.Down;
                    s.Title = L.T("status.down");
                    s.Sub = L.T("status.sub.down");
                    s.ButtonText = L.T("btn.connect");
                    s.ButtonPrimary = true;
                }
                string hint = L.T(Mode == ProxyMode.Warp ? "route.warp.hint" : Mode == ProxyMode.Direct ? "route.direct.hint" : "route.tor.hint");
                s.RouteHint = Config.AutoRoute ? L.F("route.auto.hint", hint) : hint;
                return s;
            }
        }

        public string ActionLabel(ErrorAction a)
        {
            return a == ErrorAction.SwitchMode
                ? L.F("action.SwitchMode", ProxyModes.Display(ProxyModes.Next(Mode)))
                : L.T("action." + a);
        }

        void RefreshTray()
        {
            tray?.Refresh(Status);
        }

        // In the window when the user is looking at it; otherwise the app's own notification
        // (Liquid Glass banner, see Notifier), or the tray balloon if banners are unavailable.
        public void Notify(NoteKind kind, string title, string sub, NoteGroup group = NoteGroup.General)
        {
            if (exiting)
                return;
            if (view.IsShownToUser && view.IsInFront)
            {
                view.ShowToast(kind, title, sub);
                return;
            }
            Post(new Note { Kind = kind, Group = group, Title = title, Sub = sub });
        }

        void Post(Note n)
        {
            n.Open = n.Open ?? ShowFromTray;
            if (notifier != null)
                notifier.Post(n);
            else if (n.Important || view.IsShownToUser)
                tray?.Balloon(4000, n.Title, n.Sub, n.Kind);
        }

        void ShowError(AppError e, bool balloon = true)
        {
            view.ShowCard(e);
            view.RefreshStatus();
            if (!balloon || (view.IsShownToUser && view.IsInFront))
                return;
            var n = new Note
            {
                Kind = e.IsInfo ? NoteKind.Info : NoteKind.Error,
                Group = e.Code == "UPDATE" ? NoteGroup.Update : NoteGroup.Problem,
                Title = e.IsInfo ? e.Title : e.Code + " · " + e.Title,
                Sub = e.Description,
            };
            ErrorAction first = e.Actions.FirstOrDefault();
            if (e.Code == "UPDATE")
            {
                n.Actions.Add(new NoteAction { Label = ActionLabel(ErrorAction.InstallUpdate), Primary = true, Run = () => RunAction(ErrorAction.InstallUpdate) });
                n.Actions.Add(new NoteAction { Label = ActionLabel(ErrorAction.ReleaseNotes), Run = () => RunAction(ErrorAction.ReleaseNotes) });
            }
            else
            {
                if (e.Actions.Length > 0 && !OpensWindow(first))
                    n.Actions.Add(new NoteAction { Label = ActionLabel(first), Primary = true, Run = () => RunAction(first) });
                n.Actions.Add(new NoteAction { Label = L.T("note.open"), Primary = n.Actions.Count == 0, Run = () => { ShowFromTray(); return Task.CompletedTask; } });
            }
            Post(n);
        }

        // Actions that open a dialog need the window in front first.
        static bool OpensWindow(ErrorAction a)
        {
            return a == ErrorAction.OpenSettings || a == ErrorAction.AddBridges || a == ErrorAction.ChangeEndpoint
                || a == ErrorAction.BrowseDiscord || a == ErrorAction.Repair || a == ErrorAction.OpenLogs;
        }

        // Settings → Notifications → Try.
        public void ShowSampleNote()
        {
            var n = new Note { Kind = NoteKind.Success, Group = NoteGroup.General, Title = L.T("notify.test.t"), Sub = L.T("notify.test.d") };
            n.Actions.Add(new NoteAction { Label = L.T("note.open"), Primary = true, Run = () => { ShowFromTray(); return Task.CompletedTask; } });
            n.Open = ShowFromTray;
            notifier?.ShowSample(n);
        }

        // The view calls this after a card was closed (by the user, or by an action that resolved it).
        public void OnCardDismissed(AppError e)
        {
            if (e != null && e.Code == "UPDATE")
                updateDismissedFor = e.Arg;
            Proxy.ClearError();
            view.RefreshStatus();
        }

        // ---------------- proxy ----------------

        bool EnsureWarpConsent()
        {
            if (Config.WarpTosAccepted || WarpBackend.HasAccount(AppSetup.DataDir))
                return true;
            if (!view.Confirm(L.T("warp.consent.title"), L.T("warp.consent.body")))
                return false;
            Config.WarpTosAccepted = true;
            Config.Save();
            return true;
        }

        void SetMode(ProxyMode m, bool animate)
        {
            Mode = m;
            Config.Mode = ProxyModes.ToConfig(m);
            Config.Save();
            view.ModeChanged(animate);
        }

        public void SetAutoConnect(bool on)
        {
            Config.AutoConnect = on;
            Config.Save();
        }

        public void SetAutoRoute(bool on)
        {
            Config.AutoRoute = on;
            Config.Save();
            view.RefreshStatus();
        }

        public async Task ConnectButtonClicked()
        {
            if (Proxy.IsStarting || Proxy.IsRunning)
            {
                lastConnectFailed = false;
                nextHealthCheck = DateTime.MaxValue;
                await Proxy.StopAsync();
                if (!exiting)
                {
                    view.RefreshStatus();
                    Notify(NoteKind.Info, L.T("toast.disconnected"), null, NoteGroup.Connection);
                }
                return;
            }
            await Connect();
        }

        public async Task Connect(string reconnectReason = null)
        {
            if (Mode == ProxyMode.Warp && !EnsureWarpConsent())
            {
                SetMode(ProxyMode.Tor, true);
                Notify(NoteKind.Warning, L.T("warp.consent.declined"), null);
            }
            view.HideCard(false);
            if (Config.AutoRoute && Mode != ProxyMode.Direct && await DirectBackend.WorksNowAsync(TimeSpan.FromSeconds(4)))
            {
                Logger.Log("Auto route: Direct works on this network, trying it first");
                SetMode(ProxyMode.Direct, false);
            }
            Task<bool> start = Proxy.StartAsync(Mode, Config);
            view.RefreshStatus();
            bool ok = await start;
            AfterStart(ok, false, reconnectReason);
            if (ok && Config.LaunchDiscordAfterConnect && Selected != null && ClientState != ClientState.RunningWithProxy)
                await LaunchSelected();
        }

        // The user picked a route (the switch or the tray).
        public async Task ModeChosen(ProxyMode target)
        {
            if (target == Mode)
                return;
            if (target == ProxyMode.Warp && !EnsureWarpConsent())
            {
                view.ModeChanged(true); // back to the current one
                Notify(NoteKind.Warning, L.T("warp.consent.declined"), null);
                return;
            }
            if (Config.AutoRoute)
            {
                Config.AutoRoute = false;
                Config.Save();
                Notify(NoteKind.Info, L.T("toast.auto.off"), null);
            }
            SetMode(target, true);
            view.RefreshStatus();
            if (!Proxy.IsRunning && !Proxy.IsStarting)
                return;
            view.HideCard(false);
            Task<bool> sw = Proxy.SwitchModeAsync(target, Config);
            view.RefreshStatus();
            bool ok = await sw;
            AfterStart(ok, true);
        }

        public Task SwitchToNextMode()
        {
            return ModeChosen(ProxyModes.Next(Mode));
        }

        void AfterStart(bool ok, bool switched, string reconnectReason = null)
        {
            if (exiting)
                return;
            lastConnectFailed = !ok;
            nextDirectCheck = DateTime.Now.AddMinutes(2);
            nextHealthCheck = ok ? DateTime.Now.AddSeconds(60) : DateTime.MaxValue;
            healthFails = 0;
            bool moved = Proxy.ActiveMode.HasValue && Proxy.ActiveMode.Value != Mode;
            if (moved)
                SetMode(Proxy.ActiveMode.Value, true); // auto route, or WARP -> Tor fallback
            view.RefreshStatus();
            if (ok)
            {
                string m = ProxyModes.Display(Mode);
                if (reconnectReason != null)
                {
                    // A flaky network can reconnect several times: one note that counts them.
                    reconnects.RemoveAll(t => DateTime.Now - t > TimeSpan.FromMinutes(10));
                    reconnects.Add(DateTime.Now);
                    string title = reconnects.Count > 1 ? L.F("note.reconnected.n", reconnects.Count, m) : L.F("toast.reconnected", m);
                    Notify(NoteKind.Info, title, L.T(reconnectReason), NoteGroup.Connection);
                }
                else if (moved && Config.AutoRoute)
                    Notify(NoteKind.Info, L.F("toast.auto.picked", m), L.T("toast.auto.picked.sub"), NoteGroup.Connection);
                else if (switched)
                    Notify(NoteKind.Success, L.F("toast.mode", m), L.T("toast.mode.sub"), NoteGroup.Connection);
                else
                    Notify(NoteKind.Success, L.F("toast.connected", m), L.F("status.sub.connected", Proxy.LastLatencyMs, m), NoteGroup.Connection);
            }
            else if (Proxy.LastError != null)
            {
                ShowError(Proxy.LastError);
            }
        }

        public async Task RunAction(ErrorAction a)
        {
            switch (a)
            {
                case ErrorAction.Retry:
                    await Connect();
                    break;
                case ErrorAction.NewCircuit:
                    if (await Proxy.NewCircuitAsync())
                    {
                        view.HideCard(true);
                        Notify(NoteKind.Info, L.T("toast.newcircuit"), null);
                    }
                    else
                    {
                        await Connect();
                    }
                    break;
                case ErrorAction.SwitchMode:
                    ProxyMode target = ProxyModes.Next(Mode);
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
                    foreach (Process p in Proxy.FindForeignLeftovers())
                    {
                        try
                        {
                            Logger.Log($"Closing leftover {p.ProcessName} (pid {p.Id})", LogType.WARNING);
                            p.Kill();
                            p.WaitForExit(3000);
                            n++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Could not close pid {p.Id}: {ex.Message}", LogType.ERROR);
                        }
                        finally
                        {
                            p.Dispose();
                        }
                    }
                    Notify(NoteKind.Info, L.F("toast.leftovers", n), null);
                    await Connect();
                    break;
                case ErrorAction.OpenLogs:
                    view.ShowLogs();
                    break;
                case ErrorAction.OpenFolder:
                    Process.Start("explorer.exe", "\"" + AppContext.BaseDirectory + "\"");
                    break;
                case ErrorAction.BrowseDiscord:
                    BrowseForDiscord();
                    break;
                case ErrorAction.RescanDiscord:
                    Rescan();
                    if (Selected != null)
                        view.HideCard(true);
                    break;
                case ErrorAction.InstallUpdate:
                    if (pendingUpdate != null)
                        await InstallUpdateAsync(pendingUpdate);
                    break;
                case ErrorAction.ReleaseNotes:
                    OpenUrl(UpdateSource.ReleasesPage);
                    break;
                case ErrorAction.Repair:
                    view.HideCard(true);
                    await OpenRepair();
                    break;
                case ErrorAction.RetryDll:
                    Manifest m = await Updater.FetchManifestAsync();
                    if (m != null)
                        await Updater.EnsureFilesAsync(m);
                    if (AppSetup.HasDll)
                        view.HideCard(true);
                    else
                        Notify(NoteKind.Error, L.T("update.offline"), null);
                    break;
            }
        }

        public async Task RunTest()
        {
            if (!Proxy.IsRunning)
            {
                Notify(NoteKind.Info, L.T("toast.notconnected"), null);
                return;
            }
            Testing = true;
            view.RefreshStatus();
            List<TestItem> items;
            try
            {
                items = await Proxy.TestAsync();
            }
            finally
            {
                Testing = false;
                view.RefreshStatus();
            }
            Func<TestItem, string> fmt = i => i.Reachable ? i.Result.Ms + " ms" : (i.Result.Status > 0 ? "HTTP " + i.Result.Status : "✕");
            string detail = string.Join(" · ", items.Select(i => L.T("test." + i.Name) + " " + fmt(i)));
            TestItem bad = items.FirstOrDefault(i => !i.Reachable);
            if (bad == null)
            {
                Notify(NoteKind.Success, L.T("toast.test.ok"), detail);
                return;
            }
            int st = bad.Result.Status;
            if (Proxy.ActiveMode == ProxyMode.Tor && (st == 403 || st == 429) && await Proxy.NewCircuitAsync())
            {
                Notify(NoteKind.Info, L.T("toast.newcircuit"), L.T("toast.newcircuit.sub"));
                return;
            }
            Notify(NoteKind.Error, L.T("toast.test.fail"), detail);
            ShowError(st == 403 || st == 429
                ? AppError.For("DP-402", "HTTP " + st)
                : AppError.For("DP-401", st > 0 ? "HTTP " + st : bad.Name));
        }

        // ---------------- Discord client ----------------

        public void Rescan()
        {
            Clients = Discord.Scan(Config.CustomClientRoots);
            Logger.Log($"Discord installs found: {(Clients.Count == 0 ? "none" : string.Join(", ", Clients.Select(c => c.DisplayName)))}");
            Selected = Clients.FirstOrDefault(c => c.Key == Config.SelectedClient) ?? Clients.FirstOrDefault();
            if (Config.AutoUpdateProxified)
                Discord.FixProxifiedShortcuts(Clients, Config.GostPort);
            ClientState = ClientState.Stopped;
            view.RefreshClient();
            PollClient();
        }

        public void Select(DiscordClient c)
        {
            Selected = c;
            Config.SelectedClient = c.Key;
            Config.Save();
            view.RefreshClient();
            PollClient();
        }

        public void SetShortcutProxy(bool on)
        {
            if (Selected != null)
                Discord.SetShortcutProxy(Selected, on, Config.GostPort);
        }

        public void BrowseForDiscord()
        {
            string path = view.PickFolder(L.T("client.browse.desc"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            if (path == null)
                return;
            DiscordClient c = DiscordClients.FromFolder(path);
            if (c == null)
            {
                Notify(NoteKind.Error, L.T("client.browse.invalid"), path);
                return;
            }
            if (!Config.CustomClientRoots.Contains(c.RootDir, StringComparer.OrdinalIgnoreCase))
                Config.CustomClientRoots = Config.CustomClientRoots.Concat(new[] { c.RootDir }).ToArray();
            Config.SelectedClient = c.Key;
            Config.Save();
            Rescan();
            if (Selected != null && view.Card != null && view.Card.Code == "DP-501")
                view.HideCard(true);
        }

        // WMI is slow-ish, so the running state is read off the UI thread every few seconds.
        void PollClient()
        {
            DiscordClient c = Selected;
            if (c == null || polling)
                return;
            polling = true;
            string port = Config.GostPort;
            Task.Run(() =>
            {
                Discord.PrepareNewAppFolder(c);
                return Discord.GetState(c, port);
            }).ContinueWith(t =>
            {
                polling = false;
                if (exiting || c != Selected)
                    return;
                if (t.Status == TaskStatus.RanToCompletion && t.Result != ClientState)
                {
                    ClientState = t.Result;
                    view.RefreshClient();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public async Task LaunchSelected()
        {
            if (Selected == null)
            {
                ShowError(AppError.For("DP-501"));
                return;
            }
            if (!Proxy.IsRunning)
            {
                // Discord launched before the proxy is up hangs on "Starting"; connect first.
                await Connect();
                if (!Proxy.IsRunning)
                    return;
            }
            Launching = true;
            view.RefreshClient();
            DiscordClient c = Selected;
            string port = Config.GostPort;
            bool restarted = false;
            string error = null;
            bool ok = await Task.Run(() => Discord.Launch(c, port, out restarted, out error));
            Launching = false;
            view.RefreshClient();
            if (!ok)
            {
                ShowError(AppError.For("DP-503", error));
                return;
            }
            Notify(NoteKind.Success, L.T(restarted ? "toast.discord.restarted" : "toast.discord.started"), L.T("toast.discord.sub"));
            await Task.Delay(4000);
            PollClient();
        }

        // ---------------- settings ----------------

        public void OpenSettings(string focus)
        {
            Configuration before = Config.Clone();
            view.ShowSettings(focus);

            if (Config.OpenAtStartup != before.OpenAtStartup)
                AppSetup.SetAutoStart(Config.OpenAtStartup, Logger);
            if (Config.GostPort != before.GostPort && Config.AutoUpdateProxified)
                Discord.FixProxifiedShortcuts(Clients, Config.GostPort);
            if (Config.NetworkDiffers(before) && (Proxy.IsRunning || Proxy.IsStarting))
            {
                Notify(NoteKind.Info, L.T("toast.settings"), null);
                var _ = Connect();
            }
            view.RefreshStatus();
        }

        public void OpenLogs()
        {
            view.ShowLogs();
        }
    }
}
