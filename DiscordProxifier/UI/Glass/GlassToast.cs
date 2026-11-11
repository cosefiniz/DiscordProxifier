using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DiscordUnblocker.Glass
{
    // A short note that drops in from the top centre of the window (Dynamic-Island style):
    // a glass capsule with a coloured icon, a title and an optional line under it. It hides
    // itself after a few seconds, a new note replaces the current one, and a click dismisses it.
    // Only the capsule itself takes clicks; it is collapsed while hidden.
    public class GlassToast : Border
    {
        readonly Ellipse badge;
        readonly TextBlock icon, title, sub;
        readonly TranslateTransform move = new TranslateTransform();
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        readonly DispatcherTimer hideTimer = new DispatcherTimer();
        bool shown;
        int gen;

        public GlassToast()
        {
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Top;
            Background = GlassTheme.PopupFill;
            BorderBrush = GlassTheme.Rim;
            BorderThickness = new Thickness(1);
            MinWidth = 180;
            Cursor = Cursors.Hand;
            Visibility = Visibility.Collapsed;
            Opacity = 0;
            RenderTransformOrigin = new Point(0.5, 0);
            var tg = new TransformGroup();
            tg.Children.Add(scale);
            tg.Children.Add(move);
            RenderTransform = tg;

            var row = new Grid { Margin = new Thickness(10, 8, 20, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var mark = new Grid { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center };
            badge = new Ellipse { Opacity = 0.18 };
            icon = G.Icon(Glyph.Check, 13);
            mark.Children.Add(badge);
            mark.Children.Add(icon);
            row.Children.Add(mark);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 1) };
            title = G.Text("", TextStyle.Callout);
            title.FontWeight = FontWeights.SemiBold;
            sub = G.Text("", TextStyle.Footnote, GlassTheme.Secondary);
            sub.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(title);
            text.Children.Add(sub);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Child = row;

            SizeChanged += (s, e) => CornerRadius = new CornerRadius(ActualHeight / 2);
            MouseLeftButtonUp += (s, e) =>
            {
                Hide(true);
                e.Handled = true;
            };
            hideTimer.Tick += (s, e) =>
            {
                hideTimer.Stop();
                Hide(true);
            };
            ApplyShadow();
            GlassTheme.Changed += ApplyShadow;
        }

        void ApplyShadow()
        {
            Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 4,
                Direction = 270,
                Color = GlassTheme.P.Shadow,
                Opacity = GlassTheme.P.IsLight ? 0.22 : 0.55,
                RenderingBias = RenderingBias.Performance,
            };
        }

        public bool IsShown => shown;

        public void Show(NoteKind kind, string titleText, string subText, int ms = 3200)
        {
            Brush c;
            string glyph;
            switch (kind)
            {
                case NoteKind.Success:
                    c = GlassTheme.Success;
                    glyph = Glyph.Check;
                    break;
                case NoteKind.Warning:
                    c = GlassTheme.Warning;
                    glyph = Glyph.Warning;
                    break;
                case NoteKind.Error:
                    c = GlassTheme.Danger;
                    glyph = Glyph.Error;
                    break;
                default:
                    c = GlassTheme.AccentText;
                    glyph = Glyph.Info;
                    break;
            }
            badge.Fill = c;
            icon.Foreground = c;
            icon.Text = glyph;
            G.SetText(title, titleText);
            G.SetText(sub, subText);
            sub.Visibility = string.IsNullOrEmpty(subText) ? Visibility.Collapsed : Visibility.Visible;
            ToolTip = string.IsNullOrEmpty(subText) ? titleText : titleText + "\n" + subText;

            gen++;
            Visibility = Visibility.Visible;
            if (shown)
            {
                // Replacing a visible note: a small squeeze instead of dropping in again.
                var sx = new DoubleAnimationUsingKeyFrames { Duration = Motion.D(420) };
                sx.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, KeyTime.FromPercent(0.25), new QuadraticEase()));
                sx.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), SpringEase.Bouncy));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, sx);
                Motion.Animate(move, TranslateTransform.YProperty, 0, 420, SpringEase.Soft);
                BeginAnimation(OpacityProperty, Motion.To(1, 160, new QuadraticEase()));
            }
            else
            {
                shown = true;
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-64, 0, Motion.D(560)) { EasingFunction = GlassTheme.ReduceMotion ? null : SpringEase.Soft });
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.55, 1, Motion.D(620)) { EasingFunction = GlassTheme.ReduceMotion ? null : SpringEase.Bouncy });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.7, 1, Motion.D(560)) { EasingFunction = GlassTheme.ReduceMotion ? null : SpringEase.Soft });
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D(180)));
            }
            hideTimer.Stop();
            hideTimer.Interval = TimeSpan.FromMilliseconds(ms);
            hideTimer.Start();
        }

        public void Hide(bool animate)
        {
            hideTimer.Stop();
            if (!shown)
                return;
            shown = false;
            int g = ++gen;
            if (!animate || !IsLoaded)
            {
                Collapse(g);
                return;
            }
            var fade = Motion.To(0, 240, new QuadraticEase { EasingMode = EasingMode.EaseIn });
            fade.Completed += (s, e) => Collapse(g);
            move.BeginAnimation(TranslateTransform.YProperty, Motion.To(-48, 300, new CubicEase { EasingMode = EasingMode.EaseIn }));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(0.8, 300, new CubicEase { EasingMode = EasingMode.EaseIn }));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.To(0.8, 300, new CubicEase { EasingMode = EasingMode.EaseIn }));
            BeginAnimation(OpacityProperty, fade);
        }

        void Collapse(int g)
        {
            if (g != gen)
                return;
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Visibility = Visibility.Collapsed;
            // Release the held animation values so nothing keeps a clock alive while hidden.
            move.BeginAnimation(TranslateTransform.YProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
    }
}
