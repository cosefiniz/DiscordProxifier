using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DiscordUnblocker.Glass
{
    // PLACEHOLDER — replaced by the full Liquid Glass main window.
    public class GlassMainWindow : GlassWindow, IAppView
    {
        readonly AppController app;
        readonly TextBlock status;
        readonly GlassButton connect;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        AppError card;

        public GlassMainWindow(AppController app, bool startHidden) : base(392, true)
        {
            this.app = app;
            var sp = new StackPanel { Margin = new Thickness(16, 0, 16, 16) };
            var pane = new GlassPane();
            var inner = new StackPanel { Margin = new Thickness(18) };
            status = G.Text("", TextStyle.LargeTitle);
            inner.Children.Add(status);
            connect = new GlassButton("", ButtonKind.Primary) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            connect.Click += async () => await app.ConnectButtonClicked();
            inner.Children.Add(connect);
            pane.Child = inner;
            sp.Children.Add(pane);
            Body = sp;
            AddTitleButton(Glyph.Minimize, null, () => WindowState = WindowState.Minimized);
            CloseButton.Click += () => { };
            Closing += (s, e) => app.OnViewClosing();
            Closed += (s, e) => Application.Current?.Shutdown();
            timer.Tick += (s, e) => app.Tick();
            if (GlassPreview.Active)
                return;
            app.Attach(this);
            timer.Start();
            Dispatcher.BeginInvoke(new Action(async () => await app.Init()));
        }

        public static Window CreatePreview(AppController app, string state)
        {
            var w = new GlassMainWindow(app, false);
            w.RefreshStatus();
            return w;
        }

        public bool WindowVisible => IsVisible;
        public bool IsShownToUser => IsVisible && WindowState != WindowState.Minimized;
        public AppError Card => card;
        public void ShowCard(AppError e) { card = e; }
        public void HideCard(bool notify) { AppError e = card; card = null; if (notify && e != null) app.OnCardDismissed(e); }
        public void ShowToast(NoteKind kind, string title, string sub) { }
        public void RefreshStatus() { StatusInfo s = app.Status; G.SetText(status, s.Title); connect.Text = s.ButtonText; }
        public void RefreshClient() { }
        public void ModeChanged(bool animate) { }
        public bool Confirm(string title, string body) { return GlassDialog.Confirm(this, title, body); }
        public string PickFolder(string description, string startPath) { return GlassDialog.PickFolder(this, description, startPath); }
        public void ShowSettings(string focus) { new GlassSettingsWindow(app, focus) { Owner = this }.ShowDialog(); }
        public async Task ShowRepairAsync(Func<IRepairView, Task> run) { var w = new GlassRepairWindow(app) { Owner = this }; w.ContentRendered += async (s, e) => await run(w); w.ShowDialog(); await Task.CompletedTask; }
        public void ShowLogs() { new GlassLogsWindow(app.Logger).Show(); }
        public void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
        public void HideWindow() { Hide(); }
        public void Shutdown() { timer.Stop(); Application.Current.Shutdown(); }
    }
}
