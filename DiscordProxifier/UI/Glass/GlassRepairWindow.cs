using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DiscordUnblocker.Glass
{
    // Small activity ring (Apple-style arc on a faint track). It only animates while Running is
    // on and the ring is on screen, so a finished step costs nothing.
    public class GlassSpinner : Grid
    {
        readonly Ellipse arc;
        readonly RotateTransform spin = new RotateTransform();
        bool running;

        public GlassSpinner(double size = 18, double thickness = 2)
        {
            Width = size;
            Height = size;
            IsHitTestVisible = false;
            Children.Add(new Ellipse { Stroke = GlassTheme.Separator, StrokeThickness = thickness });
            double circumference = Math.PI * (size - thickness) / thickness; // in stroke widths
            arc = new Ellipse
            {
                Stroke = GlassTheme.AccentText,
                StrokeThickness = thickness,
                StrokeDashCap = PenLineCap.Round,
                StrokeDashArray = new DoubleCollection { circumference * 0.3, circumference },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = spin,
            };
            Children.Add(arc);
            Loaded += (s, e) => Apply();
            Unloaded += (s, e) => spin.BeginAnimation(RotateTransform.AngleProperty, null);
            IsVisibleChanged += (s, e) => Apply();
        }

        public bool Running
        {
            get { return running; }
            set
            {
                if (running == value)
                    return;
                running = value;
                Apply();
            }
        }

        void Apply()
        {
            if (running && IsVisible)
            {
                var a = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(GlassTheme.ReduceMotion ? 1.6 : 0.85)) { RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(a, 40);
                spin.BeginAnimation(RotateTransform.AngleProperty, a);
            }
            else
                spin.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }

    // Repair: each step appears as a row with a spinner, then a coloured check, warning or cross
    // and what was found. Report and Close unlock when everything has run.
    public class GlassRepairWindow : GlassWindow, IRepairView
    {
        class Row
        {
            public Grid Icon;
            public GlassSpinner Spinner;
            public TextBlock Title, Detail;
        }

        readonly AppController app;
        readonly InsetGroup steps = new InsetGroup(16);
        readonly List<Row> rows = new List<Row>();
        readonly StackPanel summary;
        readonly TextBlock summaryIcon, summaryText, intro, reportStatus;
        readonly GlassButton report, close;

        public GlassRepairWindow(AppController app) : base(440, false, L.T("repair.title"))
        {
            this.app = app;
            ShowInTaskbar = false;
            CloseOnEscape = true;
            var sp = new StackPanel { Margin = new Thickness(18, 0, 18, 18) };
            intro = G.Text(L.T("repair.intro"), TextStyle.Subhead, GlassTheme.Secondary);
            intro.TextWrapping = TextWrapping.Wrap;
            intro.TextTrimming = TextTrimming.None;
            intro.Margin = new Thickness(2, 0, 2, 12);
            sp.Children.Add(intro);
            steps.Visibility = Visibility.Collapsed;
            sp.Children.Add(steps);

            summary = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 16, 4, 0), Visibility = Visibility.Collapsed };
            summaryIcon = G.Icon("", 15);
            summaryIcon.Margin = new Thickness(0, 1, 9, 0);
            summaryText = G.Text("", TextStyle.Headline);
            summaryText.TextWrapping = TextWrapping.Wrap;
            summaryText.TextTrimming = TextTrimming.None;
            summaryText.MaxWidth = 370;
            summary.Children.Add(summaryIcon);
            summary.Children.Add(summaryText);
            sp.Children.Add(summary);
            reportStatus = G.Text("", TextStyle.Footnote, GlassTheme.Secondary);
            reportStatus.Margin = new Thickness(28, 4, 4, 0);
            reportStatus.TextWrapping = TextWrapping.Wrap;
            reportStatus.TextTrimming = TextTrimming.None;
            reportStatus.Visibility = Visibility.Collapsed;
            sp.Children.Add(reportStatus);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            report = new GlassButton(L.T("settings.report"), ButtonKind.Secondary, Glyph.Report, 32) { IsEnabled = false };
            close = new GlassButton(L.T("btn.close"), ButtonKind.Primary, null, 32) { IsEnabled = false, MinWidth = 92, Margin = new Thickness(10, 0, 0, 0) };
            buttons.Children.Add(report);
            buttons.Children.Add(close);
            sp.Children.Add(buttons);
            Body = sp;

            report.Click += async () =>
            {
                if (GlassPreview.Active)
                    return;
                report.IsEnabled = false;
                ShowReportStatus(L.T("report.building"));
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
                ShowReportStatus(result);
                report.IsEnabled = true;
            };
            close.Click += Close;
        }

        void ShowReportStatus(string text)
        {
            G.SetText(reportStatus, text);
            if (reportStatus.Visibility != Visibility.Visible)
            {
                reportStatus.Visibility = Visibility.Visible;
                reportStatus.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D(200)));
            }
        }

        public int Step(string title)
        {
            if (!Dispatcher.CheckAccess())
                return (int)Dispatcher.Invoke(new Func<int>(() => Step(title)));
            var r = new Row();
            r.Spinner = new GlassSpinner(17) { Running = true };
            r.Icon = new Grid { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
            r.Icon.Children.Add(r.Spinner);
            TextBlock t, d;
            StackPanel label = InsetGroup.Label(title, L.T("repair.running"), out t, out d);
            r.Title = t;
            r.Detail = d;
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(label, 1);
            line.Children.Add(r.Icon);
            line.Children.Add(label);
            Grid row = steps.AddRow(line, null, 52);
            steps.Visibility = Visibility.Visible;
            if (!GlassTheme.ReduceMotion)
            {
                row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D(220)));
                var tt = new TranslateTransform(0, 6);
                row.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.YProperty, Motion.To(0, 420, SpringEase.Soft));
            }
            rows.Add(r);
            return rows.Count - 1;
        }

        public void Done(int index, int status, string detail)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new Action(() => Done(index, status, detail)));
                return;
            }
            if (index < 0 || index >= rows.Count)
                return;
            Row r = rows[index];
            r.Spinner.Running = false;
            r.Icon.Children.Clear();
            string glyph;
            Brush brush;
            StatusLook(status, out glyph, out brush);
            TextBlock icon = G.Icon(glyph, 16, brush);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 3, 0, 0);
            r.Icon.Children.Add(icon);
            if (!GlassTheme.ReduceMotion)
            {
                var sc = new ScaleTransform(0.4, 0.4);
                icon.RenderTransformOrigin = new Point(0.5, 0.5);
                icon.RenderTransform = sc;
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(1, 460, SpringEase.Bouncy));
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.To(1, 460, SpringEase.Bouncy));
            }
            G.SetText(r.Detail, detail);
            r.Detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        }

        static void StatusLook(int status, out string glyph, out Brush brush)
        {
            switch (status)
            {
                case 0:
                    glyph = "\uEC61"; // CompletedSolid
                    brush = GlassTheme.Success;
                    break;
                case 1:
                    glyph = "\uE7BA"; // Warning
                    brush = GlassTheme.Warning;
                    break;
                default:
                    glyph = "\uEB90"; // StatusErrorFull
                    brush = GlassTheme.Danger;
                    break;
            }
        }

        public void Finish(bool allGood)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new Action(() => Finish(allGood)));
                return;
            }
            string glyph;
            Brush brush;
            StatusLook(allGood ? 0 : 1, out glyph, out brush);
            summaryIcon.Text = glyph;
            summaryIcon.Foreground = brush;
            G.SetText(summaryText, L.T(allGood ? "repair.summary.ok" : "repair.summary.issues"));
            summary.Visibility = Visibility.Visible;
            summary.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D(240)));
            report.IsEnabled = true;
            close.IsEnabled = true;
            close.Focus();
        }

        // Design check states: running, ok, issues.
        public static Window CreatePreview(AppController app, string state)
        {
            var w = new GlassRepairWindow(app);
            int a = w.Step(L.T("repair.files"));
            int b = w.Step(L.T("repair.leftovers"));
            int c = w.Step(L.T("repair.discord"));
            if (state == "running")
            {
                w.Done(a, 0, L.T("repair.files.ok"));
                w.Done(b, 0, L.T("repair.leftovers.none"));
                return w;
            }
            int d = w.Step(L.T("repair.conn"));
            if (state == "issues")
            {
                w.Done(a, 1, L.T("repair.files.unverified"));
                w.Done(b, 0, L.F("repair.leftovers.closed", 2));
                w.Done(c, 0, L.F("repair.discord.ok", "Discord"));
                w.Done(d, 2, L.T("repair.conn.failed"));
                w.Finish(false);
                w.ShowReportStatus(L.T("report.saved"));
            }
            else
            {
                w.Done(a, 0, L.T("repair.files.ok"));
                w.Done(b, 0, L.T("repair.leftovers.none"));
                w.Done(c, 0, L.F("repair.discord.ok", "Discord"));
                w.Done(d, 0, L.F("repair.conn.ok", "Direct"));
                w.Finish(true);
            }
            return w;
        }
    }
}
