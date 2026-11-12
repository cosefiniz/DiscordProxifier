using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DiscordUnblocker.Glass
{
    // The app's own notifications: Liquid Glass banners in the bottom-right corner, above the
    // tray. Up to three stack upwards; a note of the same group updates the one on screen.
    // They never take focus, pause while the mouse is on them, and close themselves.
    public class BannerStack : INoteHost
    {
        const int Max = 3;
        const double Gap = 10, Edge = 12;
        readonly List<GlassBanner> shown = new List<GlassBanner>();

        public void Show(Note n)
        {
            GlassBanner same = shown.FirstOrDefault(b => b.Group == n.Group && !b.IsDismissing);
            if (same != null)
            {
                same.Update(n);
                Arrange(true);
                return;
            }
            while (shown.Count(b => !b.IsDismissing) >= Max)
                shown.First(b => !b.IsDismissing).Dismiss();
            var banner = new GlassBanner(n);
            banner.Gone += () =>
            {
                shown.Remove(banner);
                Arrange(true);
            };
            shown.Add(banner);
            ((FrameworkElement)banner.Content).Measure(new Size(banner.Width, double.PositiveInfinity));
            Place(banner, CountHeightBelow(banner), false);
            banner.Show();
            banner.PlayIn();
            Arrange(true);
        }

        public void CloseAll()
        {
            foreach (GlassBanner b in shown.ToList())
                b.Close();
            shown.Clear();
        }

        double CountHeightBelow(GlassBanner b)
        {
            // Newest at the bottom: everything shown after b sits below it.
            double h = 0;
            for (int i = shown.Count - 1; i >= 0 && shown[i] != b; i--)
                if (!shown[i].IsDismissing)
                    h += shown[i].CardHeight + Gap;
            return h;
        }

        void Arrange(bool animate)
        {
            foreach (GlassBanner b in shown.Where(x => !x.IsDismissing))
                Place(b, CountHeightBelow(b), animate);
        }

        static void Place(GlassBanner b, double below, bool animate)
        {
            Rect wa = SystemParameters.WorkArea;
            double m = GlassBanner.ShadowMargin;
            double h = b.CardHeight;
            b.Left = wa.Right - Edge - (b.Width - m);
            double top = wa.Bottom - Edge - below - h - m;
            if (!animate || !b.IsVisible || GlassTheme.ReduceMotion)
            {
                b.BeginAnimation(Window.TopProperty, null);
                b.Top = top;
            }
            else if (Math.Abs(b.Top - top) > 0.5)
                b.BeginAnimation(Window.TopProperty, new DoubleAnimation(top, Motion.D(420)) { EasingFunction = SpringEase.Soft });
        }
    }

    public class GlassBanner : Window
    {
        public const double ShadowMargin = 16, CardWidth = 340;
        readonly Border outer, badge;
        readonly TextBlock badgeIcon, title, sub, time;
        readonly StackPanel actions;
        readonly GlassButton close;
        readonly TranslateTransform slide = new TranslateTransform();
        readonly DispatcherTimer timer = new DispatcherTimer();
        Note note;
        public event Action Gone;
        public bool IsDismissing { get; private set; }

        public NoteGroup Group => note.Group;
        // The card itself, without the shadow margin.
        public double CardHeight => Math.Max(60, outer.ActualHeight > 0 ? outer.ActualHeight : outer.DesiredSize.Height - 2 * ShadowMargin);

        public GlassBanner(Note n)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.Height;
            Width = CardWidth + ShadowMargin * 2;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            FontFamily = GlassTheme.TextFont;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -10000;
            Top = -10000;

            // Card: the tone's backdrop, a glass tint, rim and sheen.
            var frame = new Grid();
            frame.Children.Add(new Backdrop());
            frame.Children.Add(new Border { Background = GlassTheme.Glass });
            frame.Children.Add(new Border { Background = GlassTheme.Sheen, Height = 18, VerticalAlignment = VerticalAlignment.Top });
            frame.SizeChanged += (s, e) => frame.Clip = new RectangleGeometry(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), 13, 13);

            var body = new Grid { Margin = new Thickness(12, 11, 10, 12) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition());

            // App icon with a small status badge.
            var iconHost = new Grid { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 11, 0) };
            iconHost.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = GlassTheme.Control,
                BorderBrush = GlassTheme.ControlRim,
                BorderThickness = new Thickness(1),
                Child = GlassWindow.LogoImage == null ? null : new Image { Source = GlassWindow.LogoImage, Margin = new Thickness(5) },
            });
            badgeIcon = G.Icon("", 8, Brushes.White);
            badge = new Border
            {
                Width = 15,
                Height = 15,
                CornerRadius = new CornerRadius(7.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -4, -4),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Child = badgeIcon,
            };
            iconHost.Children.Add(badge);
            body.Children.Add(iconHost);

            var text = new StackPanel();
            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 1) };
            close = new GlassButton(null, ButtonKind.Icon, Glyph.Close, 24) { Opacity = 0, Margin = new Thickness(6, -3, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            close.Click += Dismiss;
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
            time = G.Text("", TextStyle.Caption, GlassTheme.Tertiary);
            DockPanel.SetDock(time, Dock.Right);
            time.Margin = new Thickness(6, 0, 0, 0);
            header.Children.Add(time);
            header.Children.Add(G.Text("Discord Proxifier", TextStyle.Caption, GlassTheme.Secondary));
            text.Children.Add(header);
            title = G.Text("", TextStyle.Headline);
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.None;
            text.Children.Add(title);
            sub = G.Text("", TextStyle.Footnote, GlassTheme.Secondary);
            sub.TextWrapping = TextWrapping.Wrap;
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sub.MaxHeight = 46;
            sub.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(sub);
            actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
            text.Children.Add(actions);
            Grid.SetColumn(text, 1);
            body.Children.Add(text);
            frame.Children.Add(body);

            outer = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = GlassTheme.WindowRim,
                Child = frame,
                Margin = new Thickness(ShadowMargin),
                RenderTransform = slide,
                Cursor = Cursors.Hand,
                Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = GlassTheme.P.ShadowOpacity, Color = GlassTheme.P.Shadow, RenderingBias = RenderingBias.Performance },
            };
            Content = outer;

            outer.MouseEnter += (s, e) =>
            {
                timer.Stop();
                close.BeginAnimation(OpacityProperty, Motion.To(1, 150, new QuadraticEase()));
            };
            outer.MouseLeave += (s, e) =>
            {
                close.BeginAnimation(OpacityProperty, Motion.To(0, 250, new QuadraticEase()));
                Restart(2500);
            };
            outer.MouseLeftButtonUp += (s, e) =>
            {
                if (e.Handled)
                    return;
                note?.Open?.Invoke();
                Dismiss();
            };
            timer.Tick += (s, e) => Dismiss();
            SourceInitialized += (s, e) => NoActivate();
            Update(n);
        }

        // Shows the note (or replaces the current one in place).
        public void Update(Note n)
        {
            bool replacing = note != null;
            note = n;
            G.SetText(title, n.Title);
            G.SetText(sub, n.Sub);
            sub.Visibility = string.IsNullOrEmpty(n.Sub) ? Visibility.Collapsed : Visibility.Visible;
            G.SetText(time, L.T("note.now"));
            Brush color = n.Kind == NoteKind.Success ? GlassTheme.Success : n.Kind == NoteKind.Warning ? GlassTheme.Warning : n.Kind == NoteKind.Error ? GlassTheme.Danger : (Brush)new SolidColorBrush(Color.FromRgb(104, 120, 214));
            badge.Background = color;
            badgeIcon.Text = n.Kind == NoteKind.Success ? Glyph.Check : n.Kind == NoteKind.Error ? "" : n.Kind == NoteKind.Warning ? "!" : "i";
            badgeIcon.FontFamily = n.Kind == NoteKind.Warning || n.Kind == NoteKind.Info ? GlassTheme.TextFont : GlassTheme.Glyphs;
            badgeIcon.FontWeight = FontWeights.Bold;
            badgeIcon.FontSize = n.Kind == NoteKind.Warning || n.Kind == NoteKind.Info ? 10 : 7;
            actions.Children.Clear();
            foreach (NoteAction a in n.Actions.Take(2))
            {
                NoteAction act = a;
                var b = new GlassButton(a.Label, a.Primary ? ButtonKind.Primary : ButtonKind.Secondary, null, 30) { Margin = new Thickness(0, 0, 8, 0) };
                b.Click += async () =>
                {
                    Dismiss();
                    if (act.Run != null)
                        await act.Run();
                };
                actions.Children.Add(b);
            }
            actions.Visibility = actions.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            Restart(n.DurationMs);
            if (replacing && IsVisible && !GlassTheme.ReduceMotion)
            {
                var pulse = new ScaleTransform(1, 1, CardWidth / 2, 40);
                outer.RenderTransform = new TransformGroup { Children = { pulse, slide } };
                var k = new DoubleAnimationUsingKeyFrames { Duration = Motion.D(360) };
                k.KeyFrames.Add(new EasingDoubleKeyFrame(0.97, KeyTime.FromPercent(0.3)));
                k.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), SpringEase.Bouncy));
                pulse.BeginAnimation(ScaleTransform.ScaleXProperty, k);
                pulse.BeginAnimation(ScaleTransform.ScaleYProperty, k);
            }
            UpdateLayout();
        }

        void Restart(int ms)
        {
            timer.Stop();
            timer.Interval = TimeSpan.FromMilliseconds(ms);
            timer.Start();
        }

        public void PlayIn()
        {
            if (GlassTheme.ReduceMotion)
                return;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(70, 0, Motion.D(560)) { EasingFunction = SpringEase.Soft });
            outer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D(220)));
        }

        public void Dismiss()
        {
            if (IsDismissing)
                return;
            IsDismissing = true;
            timer.Stop();
            var fade = new DoubleAnimation(0, Motion.D(220));
            fade.Completed += (s, e) => Close();
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, Motion.D(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            outer.BeginAnimation(OpacityProperty, fade);
            Gone?.Invoke();
        }

        protected override void OnClosed(EventArgs e)
        {
            timer.Stop();
            if (!IsDismissing)
            {
                IsDismissing = true;
                Gone?.Invoke();
            }
            base.OnClosed(e);
        }

        // Never steal focus from the game/app the user is in; no Alt-Tab entry.
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr h, int i);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr h, int i, int v);

        void NoActivate()
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x00000080); // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
        }

        // --glass-preview GlassBanner <tone> <lang> <update|error|connected|reconnected> out.png
        public static Window CreatePreview(AppController app, string state)
        {
            var n = new Note { Kind = NoteKind.Success, Group = NoteGroup.Connection, Title = L.F("toast.connected", "Direct"), Sub = L.F("status.sub.connected", 142, "Direct") };
            if (state == "update")
            {
                n = new Note { Kind = NoteKind.Info, Group = NoteGroup.Update, Title = L.F("UPDATE.t", "3.1.0"), Sub = L.T("UPDATE.d") };
                n.Actions.Add(new NoteAction { Label = L.T("action.InstallUpdate"), Primary = true });
                n.Actions.Add(new NoteAction { Label = L.T("action.ReleaseNotes") });
            }
            else if (state == "error")
            {
                AppError e = AppError.For("DP-303");
                n = new Note { Kind = NoteKind.Error, Group = NoteGroup.Problem, Title = e.Code + " · " + e.Title, Sub = e.Description };
                n.Actions.Add(new NoteAction { Label = L.T("action.Retry"), Primary = true });
                n.Actions.Add(new NoteAction { Label = L.T("note.open") });
            }
            else if (state == "reconnected")
                n = new Note { Kind = NoteKind.Info, Group = NoteGroup.Connection, Title = L.F("note.reconnected.n", 3, "Direct"), Sub = L.T("reason.network") };
            return new GlassBanner(n);
        }
    }
}
