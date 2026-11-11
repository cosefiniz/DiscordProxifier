using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace DiscordUnblocker.Glass
{
    // Glass pop-up menu (the Discord install picker): rounded, frosted, a check mark in front of
    // the selected item, an optional glyph after an item, hairline separators. It opens under
    // its anchor with a short spring, closes on a click outside or Escape, and runs the chosen
    // item's action after it has closed.
    public class GlassMenu
    {
        class Entry
        {
            public string Text, Glyph, Tooltip;
            public bool Checked, Separator;
            public Action Click;
        }

        const double ShadowRoom = 14;
        readonly List<Entry> entries = new List<Entry>();
        Popup popup;

        public GlassMenu Add(string text, Action click, bool isChecked = false, string glyph = null, string tooltip = null)
        {
            entries.Add(new Entry { Text = text, Click = click, Checked = isChecked, Glyph = glyph, Tooltip = tooltip });
            return this;
        }

        public GlassMenu AddSeparator()
        {
            entries.Add(new Entry { Separator = true });
            return this;
        }

        public void Open(FrameworkElement anchor)
        {
            FrameworkElement visual = BuildVisual(Close);
            var root = new Border { Padding = new Thickness(ShadowRoom), Child = visual, Background = Brushes.Transparent, Focusable = true };
            TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(root, TextRenderingMode.ClearType);
            popup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = false,
                Placement = PlacementMode.Bottom,
                PlacementTarget = anchor,
                HorizontalOffset = -ShadowRoom - 6,
                VerticalOffset = -ShadowRoom + 4,
                PopupAnimation = PopupAnimation.None,
                Child = root,
            };
            root.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
            popup.Opened += (s, e) => root.Focus();
            popup.IsOpen = true;

            var st = new ScaleTransform(0.94, 0.94);
            var tt = new TranslateTransform(0, -6);
            var tg = new TransformGroup();
            tg.Children.Add(st);
            tg.Children.Add(tt);
            visual.RenderTransformOrigin = new Point(0.15, 0);
            visual.RenderTransform = tg;
            visual.Opacity = 0;
            Motion.Animate(st, ScaleTransform.ScaleXProperty, 1, 380, SpringEase.Snappy);
            Motion.Animate(st, ScaleTransform.ScaleYProperty, 1, 380, SpringEase.Snappy);
            Motion.Animate(tt, TranslateTransform.YProperty, 0, 380, SpringEase.Snappy);
            visual.BeginAnimation(UIElement.OpacityProperty, Motion.To(1, 140, new QuadraticEase()));
        }

        public void Close()
        {
            if (popup != null)
                popup.IsOpen = false;
        }

        // The menu's surface. Also used by --glass-preview to draw the menu inside a window.
        public FrameworkElement BuildVisual(Action closing)
        {
            var list = new StackPanel { Margin = new Thickness(6) };
            bool anyCheck = false;
            foreach (Entry e in entries)
                anyCheck |= e.Checked;
            foreach (Entry e in entries)
            {
                if (e.Separator)
                {
                    list.Children.Add(new Border { Height = 1, Background = GlassTheme.Separator, Margin = new Thickness(12, 5, 12, 5) });
                    continue;
                }
                list.Children.Add(Item(e, anyCheck, closing));
            }
            var surface = new Border
            {
                MinWidth = 236,
                CornerRadius = new CornerRadius(14),
                Background = GlassTheme.PopupFill,
                BorderBrush = GlassTheme.WindowRim,
                BorderThickness = new Thickness(1),
                Child = list,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 20,
                    ShadowDepth = 4,
                    Direction = 270,
                    Color = GlassTheme.P.Shadow,
                    Opacity = GlassTheme.P.IsLight ? 0.2 : 0.5,
                    RenderingBias = RenderingBias.Performance,
                },
            };
            return surface;
        }

        FrameworkElement Item(Entry e, bool checkColumn, Action closing)
        {
            var g = new Grid { Margin = new Thickness(checkColumn ? 6 : 10, 0, 10, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = checkColumn ? new GridLength(22) : new GridLength(0) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (e.Checked)
            {
                TextBlock check = G.Icon(Glyph.Check, 11, GlassTheme.AccentText);
                check.HorizontalAlignment = HorizontalAlignment.Left;
                check.Margin = new Thickness(2, 1, 0, 0);
                g.Children.Add(check);
            }
            TextBlock label = G.Text(e.Text, TextStyle.Body);
            if (e.Checked)
                label.FontWeight = FontWeights.SemiBold;
            Grid.SetColumn(label, 1);
            g.Children.Add(label);
            if (e.Glyph != null)
            {
                TextBlock gl = G.Icon(e.Glyph, 13, GlassTheme.Secondary);
                gl.Margin = new Thickness(14, 0, 0, 0);
                Grid.SetColumn(gl, 2);
                g.Children.Add(gl);
            }
            var hover = new Border { CornerRadius = new CornerRadius(8), Background = GlassTheme.Hover, Opacity = 0 };
            var cell = new Grid { Height = 34, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = e.Tooltip };
            cell.Children.Add(hover);
            cell.Children.Add(g);
            cell.MouseEnter += (s, a) => hover.BeginAnimation(UIElement.OpacityProperty, Motion.To(1, 120, new QuadraticEase()));
            cell.MouseLeave += (s, a) => hover.BeginAnimation(UIElement.OpacityProperty, Motion.To(0, 200, new QuadraticEase()));
            Action click = e.Click;
            cell.MouseLeftButtonUp += (s, a) =>
            {
                a.Handled = true;
                closing?.Invoke();
                // Run it after the pop-up is gone (it may open a dialog).
                if (click != null)
                    cell.Dispatcher.BeginInvoke(click);
            };
            return cell;
        }
    }
}
