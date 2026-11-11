using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DiscordUnblocker.Glass
{
    // Card that explains an AppError with one-click fixes (red tint), or announces something
    // like an available update (accent tint). It springs open (height + fade) and folds away.
    // Error stays set until the card has fully folded, like the classic ErrorCard.
    public class GlassCard : Border
    {
        const double Radius = 20;
        readonly GlassPane pane;
        readonly Border tint;
        readonly TextBlock icon, title, code, desc;
        readonly WrapPanel actions;
        readonly GlassButton close;
        readonly List<GlassButton> buttons = new List<GlassButton>();
        AppError error;
        bool open, notifyOnClose;
        int gen;

        public event Action<ErrorAction> ActionClicked;
        public event Action<AppError> Dismissed;
        public Func<ErrorAction, string> ActionLabel = a => L.T("action." + a);

        public GlassCard()
        {
            ClipToBounds = true;
            Visibility = Visibility.Collapsed;
            Height = 0;
            Opacity = 0;

            var grid = new Grid { Margin = new Thickness(16, 14, 10, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            icon = G.Icon(Glyph.Warning, 15);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 3, 0, 0);
            grid.Children.Add(icon);

            title = G.Text("", TextStyle.Headline);
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.None;
            title.VerticalAlignment = VerticalAlignment.Top;
            title.Margin = new Thickness(0, 1, 0, 0);
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            code = G.Text("", TextStyle.Caption);
            code.FontFamily = GlassTheme.Mono;
            code.VerticalAlignment = VerticalAlignment.Top;
            code.Margin = new Thickness(10, 4, 4, 0);
            Grid.SetColumn(code, 2);
            grid.Children.Add(code);

            close = new GlassButton(null, ButtonKind.Icon, Glyph.Close, 24) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, -2, 0, 0) };
            close.Click += () => Collapse(true);
            Grid.SetColumn(close, 3);
            grid.Children.Add(close);

            desc = G.Text("", TextStyle.Subhead, GlassTheme.Secondary);
            desc.TextWrapping = TextWrapping.Wrap;
            desc.TextTrimming = TextTrimming.None;
            desc.Margin = new Thickness(0, 5, 10, 0);
            Grid.SetRow(desc, 1);
            Grid.SetColumn(desc, 1);
            Grid.SetColumnSpan(desc, 3);
            grid.Children.Add(desc);

            actions = new WrapPanel { Margin = new Thickness(0, 13, 0, 0) };
            Grid.SetRow(actions, 2);
            Grid.SetColumn(actions, 1);
            Grid.SetColumnSpan(actions, 3);
            grid.Children.Add(actions);

            tint = new Border { CornerRadius = new CornerRadius(Radius - 1), Margin = new Thickness(1), Child = grid };
            pane = new GlassPane(Radius) { Child = tint, Margin = new Thickness(0, 12, 0, 0) };
            Child = pane;

            // Primary buttons keep their glow colour per tone.
            GlassTheme.Changed += () =>
            {
                foreach (GlassButton b in buttons)
                    b.Kind = b.Kind;
            };
        }

        // The card on screen (also while it folds away), null once it is gone.
        public AppError Error => error;

        public void ShowError(AppError e)
        {
            error = e;
            notifyOnClose = false;
            bool info = e.IsInfo;
            tint.Background = info ? GlassTheme.InfoTint : GlassTheme.DangerTint;
            Brush accent = info ? GlassTheme.InfoIcon : GlassTheme.DangerIcon;
            icon.Text = info ? Glyph.Info : Glyph.Warning;
            icon.Foreground = accent;
            code.Foreground = accent;
            actions.Children.Clear();
            buttons.Clear();
            for (int i = 0; i < e.Actions.Length; i++)
            {
                ErrorAction a = e.Actions[i];
                var b = new GlassButton("", i == 0 ? ButtonKind.Primary : ButtonKind.Secondary, null, 30) { Margin = new Thickness(0, 0, 8, 8) };
                b.Click += () => ActionClicked?.Invoke(a);
                buttons.Add(b);
                actions.Children.Add(b);
            }
            ApplyTexts();
            Expand();
        }

        public void ApplyTexts()
        {
            if (error == null)
                return;
            G.SetText(title, error.Title);
            G.SetText(code, error.DisplayCode);
            code.Visibility = string.IsNullOrEmpty(error.DisplayCode) ? Visibility.Collapsed : Visibility.Visible;
            G.SetText(desc, error.Description);
            for (int i = 0; i < buttons.Count && i < error.Actions.Length; i++)
                buttons[i].Text = ActionLabel(error.Actions[i]);
            close.ToolTip = L.T("glass.dismiss");
        }

        void Expand()
        {
            int g = ++gen;
            open = true;
            Visibility = Visibility.Visible;
            var host = Parent as FrameworkElement;
            double width = host != null ? host.ActualWidth : 0;
            if (width <= 0 || !IsLoadedInVisibleWindow())
            {
                // Not laid out yet: just appear.
                BeginAnimation(HeightProperty, null);
                BeginAnimation(OpacityProperty, null);
                Height = double.NaN;
                Opacity = 1;
                return;
            }
            double from = ActualHeight;
            pane.Measure(new Size(ActualWidth > 0 ? ActualWidth : width, double.PositiveInfinity));
            double to = pane.DesiredSize.Height;
            var a = new DoubleAnimation(from, to, Motion.D(520)) { EasingFunction = GlassTheme.ReduceMotion ? null : SpringEase.Soft };
            a.Completed += (s, ev) =>
            {
                if (g != gen)
                    return;
                Height = double.NaN; // follows its content from now on (language change, resize)
                BeginAnimation(HeightProperty, null);
            };
            Height = to;
            BeginAnimation(HeightProperty, a);
            BeginAnimation(OpacityProperty, Motion.To(1, 220, new QuadraticEase()));
        }

        bool IsLoadedInVisibleWindow()
        {
            Window w = Window.GetWindow(this);
            return IsLoaded && w != null && w.IsVisible;
        }

        // Folds the card away. notify: tell the owner it was closed (Dismissed) once it's gone.
        public void Collapse(bool notify)
        {
            if (Visibility != Visibility.Visible || error == null)
                return;
            if (notify)
                notifyOnClose = true;
            if (!open)
                return; // already folding
            open = false;
            int g = ++gen;
            if (!IsLoadedInVisibleWindow())
            {
                Finish(g);
                return;
            }
            double from = ActualHeight;
            var a = new DoubleAnimation(from, 0, Motion.D(320)) { EasingFunction = GlassTheme.ReduceMotion ? null : new CubicEase { EasingMode = EasingMode.EaseInOut } };
            a.Completed += (s, ev) => Finish(g);
            Height = 0;
            BeginAnimation(HeightProperty, a);
            BeginAnimation(OpacityProperty, Motion.To(0, 200, new QuadraticEase()));
        }

        void Finish(int g)
        {
            if (g != gen)
                return;
            Height = 0;
            BeginAnimation(HeightProperty, null);
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Visibility = Visibility.Collapsed;
            AppError e = error;
            error = null;
            bool notify = notifyOnClose;
            notifyOnClose = false;
            if (notify && e != null)
                Dismissed?.Invoke(e);
        }
    }
}
