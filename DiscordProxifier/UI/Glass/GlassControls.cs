using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DiscordUnblocker.Glass
{
    public enum TextStyle
    {
        LargeTitle, // 26 semibold, display font
        Title,      // 20 semibold
        Headline,   // 14 semibold
        Body,       // 13.5
        Callout,    // 13
        Subhead,    // 12.5
        Footnote,   // 11.5
        Caption     // 11
    }

    // Segoe MDL2 glyphs used by the glass look.
    public static class Glyph
    {
        public const string Close = "", Minimize = "", ToTray = "", Check = "",
            ChevronDown = "", ChevronRight = "", Bolt = "", Cloud = "", Globe = "",
            Settings = "", Logs = "", Test = "", Play = "", Refresh = "",
            Folder = "", Link = "", Download = "", Info = "", Warning = "",
            Error = "", Copy = "", Repair = "", Report = "", Add = "", Search = "";
    }

    public static class G
    {
        public static TextBlock Text(string s, TextStyle style = TextStyle.Body, Brush brush = null)
        {
            var t = new TextBlock
            {
                Text = s ?? "",
                Foreground = brush ?? GlassTheme.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Style(t, style);
            return t;
        }

        public static void Style(TextBlock t, TextStyle style)
        {
            switch (style)
            {
                case TextStyle.LargeTitle: Set(t, 26, FontWeights.SemiBold, true); break;
                case TextStyle.Title: Set(t, 20, FontWeights.SemiBold, true); break;
                case TextStyle.Headline: Set(t, 14, FontWeights.SemiBold, false); break;
                case TextStyle.Body: Set(t, 13.5, FontWeights.Normal, false); break;
                case TextStyle.Callout: Set(t, 13, FontWeights.Normal, false); break;
                case TextStyle.Subhead: Set(t, 12.5, FontWeights.Normal, false); break;
                case TextStyle.Footnote: Set(t, 11.5, FontWeights.Normal, false); break;
                default: Set(t, 11, FontWeights.Normal, false); break;
            }
        }

        static void Set(TextBlock t, double size, FontWeight w, bool display)
        {
            t.FontSize = size;
            t.FontWeight = w;
            t.FontFamily = display ? GlassTheme.DisplayFont : GlassTheme.TextFont;
        }

        public static TextBlock Icon(string glyph, double size = 14, Brush brush = null)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = GlassTheme.Glyphs,
                FontSize = size,
                Foreground = brush ?? GlassTheme.Text,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
        }

        // Section title above a group, Apple style: sentence case, secondary colour.
        public static TextBlock Section(string s)
        {
            var t = Text(s, TextStyle.Footnote, GlassTheme.Secondary);
            t.FontWeight = FontWeights.SemiBold;
            t.Margin = new Thickness(14, 18, 0, 7);
            return t;
        }

        // Footnote under a group.
        public static TextBlock Note(string s)
        {
            var t = Text(s, TextStyle.Footnote, GlassTheme.Tertiary);
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            t.Margin = new Thickness(14, 7, 14, 0);
            return t;
        }

        // Only assign when different, so idle refreshes don't relayout or redraw anything.
        public static void SetText(TextBlock t, string s)
        {
            s = s ?? "";
            if (t.Text != s)
                t.Text = s;
        }

        public static void Press(UIElement e, ScaleTransform st)
        {
            e.PreviewMouseLeftButtonDown += (s, a) =>
            {
                Motion.Animate(st, ScaleTransform.ScaleXProperty, 0.96, 120, new QuadraticEase());
                Motion.Animate(st, ScaleTransform.ScaleYProperty, 0.96, 120, new QuadraticEase());
            };
            MouseButtonEventHandler up = (s, a) =>
            {
                Motion.Animate(st, ScaleTransform.ScaleXProperty, 1, 420, SpringEase.Bouncy);
                Motion.Animate(st, ScaleTransform.ScaleYProperty, 1, 420, SpringEase.Bouncy);
            };
            e.PreviewMouseLeftButtonUp += up;
            e.MouseLeave += (s, a) =>
            {
                if (st.ScaleX < 1)
                    up(s, null);
            };
        }
    }

    public enum ButtonKind
    {
        Primary,   // filled accent capsule
        Secondary, // glass capsule
        Plain,     // text only, accent colour
        Icon       // round glass button with a glyph
    }

    // Apple-like capsule button: springs when pressed, lightens on hover, dims when disabled.
    public class GlassButton : Border
    {
        readonly Border fill, hover;
        readonly TextBlock label, icon;
        readonly StackPanel content;
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        ButtonKind kind;
        bool down;
        public event Action Click;

        public GlassButton(string text, ButtonKind kind = ButtonKind.Secondary, string glyph = null, double height = 34)
        {
            Height = height;
            Cursor = Cursors.Hand;
            Focusable = true;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = scale;
            fill = new Border { CornerRadius = new CornerRadius(height / 2), BorderThickness = new Thickness(1) };
            hover = new Border { CornerRadius = new CornerRadius(height / 2), Background = GlassTheme.Hover, Opacity = 0 };
            label = G.Text(text, height >= 34 ? TextStyle.Headline : TextStyle.Callout);
            label.FontWeight = FontWeights.SemiBold;
            icon = G.Icon(glyph ?? "", height >= 34 ? 13 : 12);
            icon.Visibility = glyph == null ? Visibility.Collapsed : Visibility.Visible;
            content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(icon);
            content.Children.Add(label);
            var g = new Grid();
            g.Children.Add(fill);
            g.Children.Add(hover);
            g.Children.Add(content);
            Child = g;
            Kind = kind;
            Text = text;
            MouseEnter += (s, e) => hover.BeginAnimation(OpacityProperty, Motion.To(1, 160, new QuadraticEase()));
            MouseLeave += (s, e) =>
            {
                hover.BeginAnimation(OpacityProperty, Motion.To(0, 240, new QuadraticEase()));
                down = false;
            };
            G.Press(this, scale);
            MouseLeftButtonDown += (s, e) =>
            {
                down = true;
                e.Handled = true;
            };
            MouseLeftButtonUp += (s, e) =>
            {
                if (down && IsEnabled)
                    Click?.Invoke();
                down = false;
                e.Handled = true;
            };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    Click?.Invoke();
                    e.Handled = true;
                }
            };
            IsEnabledChanged += (s, e) => BeginAnimation(OpacityProperty, Motion.To(IsEnabled ? 1 : 0.45, 200, new QuadraticEase()));
            // The primary glow follows the tone; subscribed only while shown so closed windows can go.
            Action retone = () =>
            {
                if (kind == ButtonKind.Primary)
                    Kind = kind;
            };
            Loaded += (s, e) => GlassTheme.Changed += retone;
            Unloaded += (s, e) => GlassTheme.Changed -= retone;
        }

        public string Text
        {
            get { return label.Text; }
            set
            {
                G.SetText(label, value);
                label.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
                icon.Margin = new Thickness(0, 0, string.IsNullOrEmpty(value) ? 0 : 7, 0);
                double pad = Height >= 34 ? 20 : 15;
                content.Margin = kind == ButtonKind.Icon || string.IsNullOrEmpty(value) ? new Thickness(0) : new Thickness(pad, 0, pad, 0);
            }
        }

        public string Glyph
        {
            set
            {
                icon.Text = value ?? "";
                icon.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public ButtonKind Kind
        {
            get { return kind; }
            set
            {
                kind = value;
                Effect = null;
                switch (kind)
                {
                    case ButtonKind.Primary:
                        fill.Background = GlassTheme.Accent;
                        fill.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
                        label.Foreground = GlassTheme.OnAccent;
                        icon.Foreground = GlassTheme.OnAccent;
                        Effect = new DropShadowEffect { Color = GlassTheme.P.AccentGlow, BlurRadius = 16, ShadowDepth = 2, Opacity = GlassTheme.P.IsLight ? 0.35 : 0.5 };
                        break;
                    case ButtonKind.Plain:
                        fill.Background = Brushes.Transparent;
                        fill.BorderBrush = Brushes.Transparent;
                        label.Foreground = GlassTheme.AccentText;
                        icon.Foreground = GlassTheme.AccentText;
                        break;
                    default:
                        fill.Background = GlassTheme.Control;
                        fill.BorderBrush = GlassTheme.ControlRim;
                        label.Foreground = GlassTheme.Text;
                        icon.Foreground = GlassTheme.Text;
                        break;
                }
                if (kind == ButtonKind.Icon)
                    Width = Height;
                Text = label.Text;
            }
        }
    }

    // Segmented control: the selected pill glides like a drop of liquid (stretches while it
    // moves, settles with a spring).
    public class LiquidSegments : Grid
    {
        readonly Border pill;
        readonly TranslateTransform move = new TranslateTransform();
        readonly ScaleTransform stretch = new ScaleTransform(1, 1);
        readonly Grid lane = new Grid { Margin = new Thickness(3) };
        readonly List<TextBlock> labels = new List<TextBlock>();
        readonly List<TextBlock> icons = new List<TextBlock>();
        int index = -1;
        bool busy;
        public event Action<int> UserChanged;

        public LiquidSegments(string[] items, string[] glyphs = null, double height = 40)
        {
            Height = height;
            var track = new Border
            {
                CornerRadius = new CornerRadius(height / 2),
                Background = GlassTheme.Control,
                BorderBrush = GlassTheme.ControlRim,
                BorderThickness = new Thickness(1),
            };
            Children.Add(track);
            Children.Add(lane);
            pill = new Border
            {
                CornerRadius = new CornerRadius((height - 6) / 2),
                Background = GlassTheme.Glass,
                BorderBrush = GlassTheme.Rim,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var tg = new TransformGroup();
            tg.Children.Add(stretch);
            tg.Children.Add(move);
            pill.RenderTransform = tg;
            lane.Children.Add(pill);
            for (int i = 0; i < items.Length; i++)
            {
                lane.ColumnDefinitions.Add(new ColumnDefinition());
                int k = i;
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var ic = G.Icon(glyphs != null && i < glyphs.Length ? glyphs[i] : "", 12, GlassTheme.Secondary);
                ic.Margin = new Thickness(0, 1, 7, 0);
                ic.Visibility = glyphs == null ? Visibility.Collapsed : Visibility.Visible;
                var t = G.Text(items[i], TextStyle.Callout, GlassTheme.Secondary);
                t.FontWeight = FontWeights.SemiBold;
                row.Children.Add(ic);
                row.Children.Add(t);
                icons.Add(ic);
                labels.Add(t);
                var hit = new Border { Background = Brushes.Transparent, Child = row, Cursor = Cursors.Hand };
                hit.MouseLeftButtonUp += (s, e) =>
                {
                    if (!busy && k != index)
                    {
                        SetIndex(k, true);
                        UserChanged?.Invoke(k);
                    }
                };
                SetColumn(hit, i);
                lane.Children.Add(hit);
            }
            SetColumnSpan(pill, Math.Max(1, items.Length));
            lane.SizeChanged += (s, e) => PlacePill(false);
            Loaded += (s, e) =>
            {
                GlassTheme.Changed += UpdatePillShadow;
                UpdatePillShadow();
            };
            Unloaded += (s, e) => GlassTheme.Changed -= UpdatePillShadow;
            UpdatePillShadow();
        }

        void UpdatePillShadow()
        {
            pill.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1.5, Opacity = GlassTheme.P.IsLight ? 0.14 : 0.35, Color = GlassTheme.P.Shadow };
            pill.Background = GlassTheme.P.IsLight ? (Brush)new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)) : GlassTheme.Glass;
        }

        public int Index => index;

        public void SetLabels(string[] items)
        {
            for (int i = 0; i < items.Length && i < labels.Count; i++)
                G.SetText(labels[i], items[i]);
        }

        // Disabled while connecting (the route can't change mid-start).
        public bool Busy
        {
            get { return busy; }
            set
            {
                if (busy == value)
                    return;
                busy = value;
                lane.BeginAnimation(OpacityProperty, Motion.To(busy ? 0.6 : 1, 200, new QuadraticEase()));
            }
        }

        public void SetIndex(int i, bool animate)
        {
            if (i < 0 || i >= labels.Count)
                return;
            int from = index;
            index = i;
            for (int k = 0; k < labels.Count; k++)
            {
                labels[k].Foreground = k == i ? GlassTheme.Text : GlassTheme.Secondary;
                icons[k].Foreground = k == i ? GlassTheme.AccentText : GlassTheme.Secondary;
            }
            PlacePill(animate && from >= 0 && from != i);
        }

        void PlacePill(bool animate)
        {
            if (labels.Count == 0 || lane.ActualWidth <= 0 || index < 0)
                return;
            double w = lane.ActualWidth / labels.Count;
            pill.Width = w;
            double x = w * index;
            if (!animate || GlassTheme.ReduceMotion)
            {
                move.BeginAnimation(TranslateTransform.XProperty, null);
                move.X = x;
                return;
            }
            double dist = Math.Abs(x - move.X) / w;
            double ms = 460 + 80 * dist;
            move.BeginAnimation(TranslateTransform.XProperty, Motion.To(x, ms, SpringEase.Soft));
            var sx = new DoubleAnimationUsingKeyFrames { Duration = Motion.D(ms) };
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1 + 0.18 * dist, KeyTime.FromPercent(0.3), new QuadraticEase()));
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), SpringEase.Bouncy));
            var sy = new DoubleAnimationUsingKeyFrames { Duration = Motion.D(ms) };
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(0.88, KeyTime.FromPercent(0.3), new QuadraticEase()));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), SpringEase.Bouncy));
            stretch.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            stretch.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }
    }

    // iOS-style switch: the knob stretches as it slides and settles with a spring.
    public class LiquidToggle : Border
    {
        readonly Border track, onFill, knob;
        readonly TranslateTransform move = new TranslateTransform();
        readonly ScaleTransform stretch = new ScaleTransform(1, 1);
        bool on;
        public event Action<bool> Toggled;

        public LiquidToggle(bool isOn)
        {
            on = isOn;
            Width = 44;
            Height = 26;
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent;
            track = new Border { CornerRadius = new CornerRadius(13), Background = GlassTheme.ToggleOff };
            onFill = new Border { CornerRadius = new CornerRadius(13), Background = GlassTheme.Accent, Opacity = on ? 1 : 0 };
            knob = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 0, 0),
                Background = GlassTheme.Knob,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = 0.3, Color = Colors.Black },
            };
            var tg = new TransformGroup();
            tg.Children.Add(stretch);
            tg.Children.Add(move);
            knob.RenderTransform = tg;
            var g = new Grid();
            g.Children.Add(track);
            g.Children.Add(onFill);
            g.Children.Add(knob);
            Child = g;
            move.X = on ? 18 : 0;
            MouseLeftButtonUp += (s, e) =>
            {
                if (!IsEnabled)
                    return;
                SetOn(!on, true);
                Toggled?.Invoke(on);
            };
            IsEnabledChanged += (s, e) => Opacity = IsEnabled ? 1 : 0.45;
        }

        public bool IsOn => on;

        public void SetOn(bool value, bool animate)
        {
            if (value == on && animate)
                return;
            on = value;
            double x = on ? 18 : 0;
            if (!animate || GlassTheme.ReduceMotion)
            {
                move.BeginAnimation(TranslateTransform.XProperty, null);
                move.X = x;
                onFill.BeginAnimation(OpacityProperty, null);
                onFill.Opacity = on ? 1 : 0;
                return;
            }
            move.BeginAnimation(TranslateTransform.XProperty, Motion.To(x, 420, SpringEase.Bouncy));
            onFill.BeginAnimation(OpacityProperty, Motion.To(on ? 1 : 0, 220, new QuadraticEase()));
            var sx = new DoubleAnimationUsingKeyFrames { Duration = Motion.D(420) };
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1.3, KeyTime.FromPercent(0.35), new QuadraticEase()));
            sx.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), SpringEase.Bouncy));
            stretch.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        }
    }

    // Status light. The halo only breathes while something is in progress, so a steady state
    // doesn't keep WPF rendering.
    public class StatusLight : Grid
    {
        readonly Ellipse dot, halo;
        readonly ScaleTransform haloScale = new ScaleTransform(1, 1);
        bool pulsing;

        public StatusLight(double size = 10)
        {
            Width = size;
            Height = size;
            halo = new Ellipse { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = haloScale, Opacity = 0, IsHitTestVisible = false };
            dot = new Ellipse();
            Children.Add(halo);
            Children.Add(dot);
        }

        public void Set(Brush color, bool pulse)
        {
            if (dot.Fill != color)
            {
                dot.Fill = color;
                halo.Fill = color;
                Color c = (color as SolidColorBrush)?.Color ?? Colors.White;
                dot.Effect = new DropShadowEffect { Color = c, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.75 };
            }
            if (pulse == pulsing)
                return;
            pulsing = pulse && !GlassTheme.ReduceMotion;
            if (pulsing)
            {
                var sa = new DoubleAnimation(1, 2.6, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var oa = new DoubleAnimation(0.5, 0, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(sa, 30);
                Timeline.SetDesiredFrameRate(oa, 30);
                haloScale.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                haloScale.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
                halo.BeginAnimation(OpacityProperty, oa);
            }
            else
            {
                haloScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                haloScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                halo.BeginAnimation(OpacityProperty, null);
                halo.Opacity = 0;
            }
        }
    }

    // Thin progress line: determinate (springs to the value) or indeterminate (a moving glint,
    // only animating while shown).
    public class GlassProgress : Grid
    {
        readonly Border bar, glint;
        readonly TranslateTransform glintMove = new TranslateTransform();
        float? value;

        public GlassProgress()
        {
            Height = 4;
            ClipToBounds = true;
            Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = GlassTheme.ToggleOff });
            bar = new Border { CornerRadius = new CornerRadius(2), Background = GlassTheme.Accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            Children.Add(bar);
            var gb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 140, 155, 245), 0));
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(255, 140, 155, 245), 0.5));
            gb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 140, 155, 245), 1));
            glint = new Border { CornerRadius = new CornerRadius(2), Background = gb, Width = 110, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = glintMove, Visibility = Visibility.Collapsed };
            Children.Add(glint);
            Visibility = Visibility.Collapsed;
            SizeChanged += (s, e) => Set(value, true);
        }

        // null = hidden, < 0 = indeterminate, 0..1 = fraction
        public void Set(float? v, bool force = false)
        {
            if (!force && Nullable.Equals(v, value))
                return;
            bool wasIndeterminate = value.HasValue && value < 0;
            value = v;
            Visibility = v.HasValue ? Visibility.Visible : Visibility.Collapsed;
            bool indeterminate = v.HasValue && v < 0;
            glint.Visibility = indeterminate ? Visibility.Visible : Visibility.Collapsed;
            if (indeterminate && (!wasIndeterminate || force) && ActualWidth > 0)
            {
                var a = new DoubleAnimation(-110, ActualWidth, TimeSpan.FromSeconds(1.25)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                Timeline.SetDesiredFrameRate(a, 40);
                glintMove.BeginAnimation(TranslateTransform.XProperty, a);
            }
            else if (!indeterminate)
                glintMove.BeginAnimation(TranslateTransform.XProperty, null);
            double target = v.HasValue && v >= 0 ? ActualWidth * Math.Min(1, v.Value) : 0;
            bar.BeginAnimation(WidthProperty, Motion.To(target, 500, SpringEase.Soft));
        }
    }

    // Apple "inset grouped" list: rows inside one glass pane, hairlines between them.
    public class InsetGroup : GlassPane
    {
        readonly StackPanel rows = new StackPanel();

        public InsetGroup(double radius = 18) : base(radius)
        {
            Child = rows;
        }

        // A row: title (and optional subtitle) on the left, an accessory (toggle, button, value) on the right.
        public Grid AddRow(UIElement left, UIElement right = null, double minHeight = 46)
        {
            if (rows.Children.Count > 0)
                rows.Children.Add(new Border { Height = 1, Background = GlassTheme.Separator, Margin = new Thickness(16, 0, 0, 0) });
            var g = new Grid { MinHeight = minHeight, Margin = new Thickness(16, 6, 14, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (left != null)
                g.Children.Add(left);
            if (right != null)
            {
                if (right is FrameworkElement fe)
                {
                    fe.VerticalAlignment = VerticalAlignment.Center;
                    fe.Margin = new Thickness(12, 0, 0, 0);
                }
                Grid.SetColumn(right, 1);
                g.Children.Add(right);
            }
            rows.Children.Add(g);
            return g;
        }

        // Title + optional subtitle block for a row.
        public static StackPanel Label(string title, string subtitle, out TextBlock titleBlock, out TextBlock subBlock)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleBlock = G.Text(title, TextStyle.Body);
            sp.Children.Add(titleBlock);
            subBlock = G.Text(subtitle, TextStyle.Footnote, GlassTheme.Secondary);
            subBlock.Margin = new Thickness(0, 2, 0, 0);
            subBlock.TextWrapping = TextWrapping.Wrap;
            subBlock.TextTrimming = TextTrimming.None;
            subBlock.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
            sp.Children.Add(subBlock);
            return sp;
        }

        public UIElementCollection Rows => rows.Children;
    }
}
