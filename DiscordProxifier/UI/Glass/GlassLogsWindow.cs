using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DiscordUnblocker.Glass
{
    // Logs: selectable monospace text on a glass pane, warnings and errors in their colours,
    // new lines arrive live (and follow the bottom only when you're already there).
    public class GlassLogsWindow : GlassWindow
    {
        const int MaxLines = 4000; // shown; Copy all always copies everything

        readonly Logger logger;
        readonly RichTextBox box;
        readonly FlowDocument doc;
        readonly OverlayScrollBar bar;
        readonly GlassButton copy, close;
        readonly TextBlock count;
        readonly ConcurrentQueue<Log> pending = new ConcurrentQueue<Log>();
        readonly DispatcherTimer copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        HashSet<Log> loaded;
        int flushQueued, lines;
        bool copied;

        public GlassLogsWindow(Logger logger) : base(640, false, L.T("logs.title"))
        {
            this.logger = logger;
            CloseOnEscape = true;
            SizeToContent = SizeToContent.Manual;
            double chrome = ShadowMargin * 2;
            Height = Math.Min(540 + chrome, SystemParameters.WorkArea.Height);
            MinWidth = 440 + chrome;
            MinHeight = 320 + chrome;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new Grid();
            var content = new Grid { Margin = new Thickness(16, 0, 16, 16) };
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(content);

            // ---- log text ----
            var pane = new GlassPane(16);
            var inner = new Grid();
            doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = GlassTheme.Mono,
                FontSize = 12,
                Foreground = GlassTheme.Text,
                TextAlignment = TextAlignment.Left,
            };
            box = new RichTextBox
            {
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = GlassTheme.Text,
                CaretBrush = GlassTheme.AccentText,
                SelectionBrush = GlassTheme.Selection,
                SelectionOpacity = 1,
                FocusVisualStyle = null,
                Padding = new Thickness(10, 12, 16, 12),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                AutoWordSelection = false,
            };
            inner.Children.Add(box);
            bar = OverlayScrollBar.For(box);
            bar.Margin = new Thickness(0, 8, 3, 8);
            inner.Children.Add(bar);
            pane.Child = inner;
            content.Children.Add(pane);

            // ---- bottom bar ----
            var bottom = new Grid { Margin = new Thickness(4, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            count = G.Text("", TextStyle.Footnote, GlassTheme.Tertiary);
            bottom.Children.Add(count);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            copy = new GlassButton(L.T("logs.copy"), ButtonKind.Secondary, Glyph.Copy, 32);
            close = new GlassButton(L.T("btn.close"), ButtonKind.Primary, null, 32) { MinWidth = 92, Margin = new Thickness(10, 0, 0, 0) };
            buttons.Children.Add(copy);
            buttons.Children.Add(close);
            Grid.SetColumn(buttons, 1);
            bottom.Children.Add(buttons);
            Grid.SetRow(bottom, 1);
            content.Children.Add(bottom);

            root.Children.Add(MakeGrip());
            Body = root;

            copy.Click += CopyAll;
            close.Click += Close;
            copiedTimer.Tick += (s, e) =>
            {
                copiedTimer.Stop();
                copied = false;
                ApplyTexts();
            };

            // Subscribe first, then read what is already there; lines logged in between arrive
            // twice and the duplicates are skipped by reference.
            logger.Added += OnAdded;
            List<Log> snapshot = logger.Snapshot();
            loaded = new HashSet<Log>(snapshot);
            foreach (Log l in snapshot.Skip(Math.Max(0, snapshot.Count - MaxLines)))
                doc.Blocks.Add(Line(l));
            lines = snapshot.Count;
            box.Document = doc;
            ApplyTexts();
            L.Changed += ApplyTexts;
            Closed += (s, e) =>
            {
                logger.Added -= OnAdded;
                L.Changed -= ApplyTexts;
                copiedTimer.Stop();
            };
            ContentRendered += (s, e) =>
            {
                box.ScrollToEnd();
                box.Focus();
            };
        }

        void ApplyTexts()
        {
            Title = L.T("logs.title");
            G.SetText(TitleBlock, Title);
            copy.Text = L.T(copied ? "logs.copied" : "logs.copy");
            copy.Glyph = copied ? Glyph.Check : Glyph.Copy;
            close.Text = L.T("btn.close");
            G.SetText(count, L.F("logs.count", lines));
        }

        static Paragraph Line(Log l)
        {
            Brush color = l.Type == LogType.ERROR ? GlassTheme.Danger : l.Type == LogType.WARNING ? GlassTheme.Warning : null;
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            // Same text as Logger.Format, so a copied selection matches "Copy all".
            p.Inlines.Add(new Run($"[{l.Time.ToLocalTime():HH:mm:ss}][{l.Type}] ") { Foreground = color ?? GlassTheme.Tertiary });
            p.Inlines.Add(new Run(l.Text ?? "") { Foreground = color ?? GlassTheme.Text });
            return p;
        }

        // Logging thread → batched onto the UI thread.
        void OnAdded(Log l)
        {
            pending.Enqueue(l);
            if (Interlocked.Exchange(ref flushQueued, 1) == 0)
            {
                try
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Flush));
                }
                catch { }
            }
        }

        void Flush()
        {
            Interlocked.Exchange(ref flushQueued, 0);
            bool atBottom = box.VerticalOffset + box.ViewportHeight >= box.ExtentHeight - 8;
            bool added = false;
            Log l;
            while (pending.TryDequeue(out l))
            {
                if (loaded != null && loaded.Contains(l))
                    continue;
                doc.Blocks.Add(Line(l));
                lines++;
                added = true;
            }
            loaded = null;
            if (!added)
                return;
            while (doc.Blocks.Count > MaxLines)
                doc.Blocks.Remove(doc.Blocks.FirstBlock);
            G.SetText(count, L.F("logs.count", lines));
            if (atBottom)
                box.ScrollToEnd();
        }

        void CopyAll()
        {
            string text = logger.AllText();
            if (text.Length == 0)
                return;
            try
            {
                Clipboard.SetDataObject(text, true); // retries while another app holds the clipboard
            }
            catch (Exception ex)
            {
                logger.Log("Could not copy the logs: " + ex.Message, LogType.WARNING);
                return;
            }
            copied = true;
            ApplyTexts();
            copiedTimer.Stop();
            copiedTimer.Start();
        }

        // Bottom-right corner handle (the window has no system frame to resize by).
        FrameworkElement MakeGrip()
        {
            var g = new Grid
            {
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 3, 3),
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeNWSE,
            };
            var marks = new Path
            {
                Data = Geometry.Parse("M 13,6 L 6,13 M 13,10 L 10,13"),
                Stroke = GlassTheme.Tertiary,
                StrokeThickness = 1.3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.7,
            };
            g.Children.Add(marks);
            Point start = new Point();
            double w0 = 0, h0 = 0;
            bool sizing = false;
            g.MouseLeftButtonDown += (s, e) =>
            {
                start = e.GetPosition(this);
                w0 = ActualWidth;
                h0 = ActualHeight;
                sizing = g.CaptureMouse();
                e.Handled = true;
            };
            g.MouseMove += (s, e) =>
            {
                if (!sizing)
                    return;
                Point p = e.GetPosition(this);
                Width = Math.Max(MinWidth, w0 + p.X - start.X);
                Height = Math.Max(MinHeight, h0 + p.Y - start.Y);
            };
            g.MouseLeftButtonUp += (s, e) =>
            {
                sizing = false;
                g.ReleaseMouseCapture();
            };
            return g;
        }

        // Design check: "sample" fills the log with a few lines of each kind.
        public static Window CreatePreview(AppController app, string state)
        {
            Logger log = app.Logger;
            log.Log("Discord Proxifier 3.0.0 starting");
            log.Log("Data check passed (Tor 0.4.9.13, version.dll v0.0.13)");
            log.Log("Automatic route: trying Direct first");
            log.Log("Direct: discord.com resolved over DoH (162.159.135.232)");
            log.Log("Direct: TLS 1.2 client sent to the helper backend (WARP)", LogType.WARNING);
            log.Log("Direct route is up, Discord answers in 142 ms");
            log.Log("Launching Discord with --proxy-server=http://127.0.0.1:42212");
            log.Log("WARP probe returned 503, retrying for up to 75 s", LogType.WARNING);
            log.Log("Tor: Bootstrapped 45% (loading_descriptors)");
            log.Log("Tor didn't finish bootstrapping within 120 s (DP-201)", LogType.ERROR);
            log.Log("Watchdog: connection healthy");
            var w = new GlassLogsWindow(log);
            log.Log("Watchdog: network change detected, checking in 6 s");
            return w;
        }
    }
}
