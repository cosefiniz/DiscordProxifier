using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DiscordUnblocker.Glass
{
    // Alerts and the folder picker of the Liquid Glass look.
    public static class GlassDialog
    {
        // Apple-style alert sheet: true = OK. Centred on the owner (or the screen when it is hidden).
        public static bool Confirm(Window owner, string title, string body)
        {
            return Confirm(owner, title, body, null);
        }

        public static bool Confirm(Window owner, string title, string body, string okText)
        {
            var w = new GlassAlert(title, body, okText ?? L.T("btn.ok"), L.T("btn.cancel"));
            w.Place(owner);
            w.ShowDialog();
            return w.Result;
        }

        // The modern Explorer folder picker; the old tree dialog only if that one can't be created.
        // null = cancelled.
        public static string PickFolder(Window owner, string description, string startPath)
        {
            IntPtr hwnd = IntPtr.Zero;
            try
            {
                if (owner != null && owner.IsVisible)
                    hwnd = new WindowInteropHelper(owner).Handle;
            }
            catch { }
            bool cancelled;
            string path = FolderPicker.Show(hwnd, description, startPath, out cancelled);
            if (path != null || cancelled)
                return path;
            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = description, ShowNewFolderButton = false, SelectedPath = startPath ?? "" })
                    return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath) ? dlg.SelectedPath : null;
            }
            catch
            {
                return null;
            }
        }
    }

    // IFileOpenDialog with FOS_PICKFOLDERS (Vista+). Returns null with cancelled = false when the
    // dialog itself couldn't be used, so the caller can fall back.
    static class FolderPicker
    {
        const uint FOS_NOCHANGEDIR = 0x8, FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;
        const int ERROR_CANCELLED_HR = unchecked((int)0x800704C7);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogCoClass { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr hwndOwner);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, [In] ref Guid riid, out IShellItem ppv);

        public static string Show(IntPtr owner, string title, string startPath, out bool cancelled)
        {
            cancelled = false;
            IFileOpenDialog dlg = null;
            IShellItem start = null, result = null;
            try
            {
                dlg = (IFileOpenDialog)new FileOpenDialogCoClass();
                uint opts;
                dlg.GetOptions(out opts);
                dlg.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
                if (!string.IsNullOrEmpty(title))
                    dlg.SetTitle(title);
                if (!string.IsNullOrEmpty(startPath) && System.IO.Directory.Exists(startPath))
                {
                    try
                    {
                        Guid iid = typeof(IShellItem).GUID;
                        SHCreateItemFromParsingName(startPath, IntPtr.Zero, ref iid, out start);
                        if (start != null)
                            dlg.SetFolder(start);
                    }
                    catch { }
                }
                int hr = dlg.Show(owner);
                if (hr == ERROR_CANCELLED_HR)
                {
                    cancelled = true;
                    return null;
                }
                if (hr != 0)
                    return null;
                dlg.GetResult(out result);
                IntPtr p;
                result.GetDisplayName(SIGDN_FILESYSPATH, out p);
                try
                {
                    string path = Marshal.PtrToStringUni(p);
                    if (string.IsNullOrEmpty(path))
                        cancelled = true; // a virtual folder with no file-system path
                    return string.IsNullOrEmpty(path) ? null : path;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(p);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                foreach (object o in new object[] { result, start, dlg })
                {
                    if (o != null)
                    {
                        try
                        {
                            Marshal.ReleaseComObject(o);
                        }
                        catch { }
                    }
                }
            }
        }
    }

    // Apple-style alert: app icon, a short bold title, the explanation, then Cancel and the
    // filled default button side by side. Esc cancels, Enter confirms.
    public class GlassAlert : GlassWindow
    {
        public bool Result { get; private set; }
        readonly GlassButton ok, cancel;

        public GlassAlert(string title, string body, string okText, string cancelText) : base(312, false, title ?? "Discord Proxifier")
        {
            ShowInTaskbar = false;
            TitleRow.Visibility = Visibility.Collapsed;
            var sp = new StackPanel { Margin = new Thickness(24, 24, 24, 20), Background = Brushes.Transparent };
            if (LogoImage != null)
                sp.Children.Add(new Image { Source = LogoImage, Width = 52, Height = 52, Margin = new Thickness(0, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Center });
            TextBlock t = G.Text(title, TextStyle.Headline);
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            t.TextAlignment = TextAlignment.Center;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(t);
            if (!string.IsNullOrEmpty(body))
            {
                // Short messages are centred like macOS; long, multi-paragraph ones read better left aligned.
                bool longText = body.Contains("\n") || body.Length > 140;
                TextBlock b = G.Text(body.Trim(), TextStyle.Subhead, GlassTheme.Secondary);
                b.TextWrapping = TextWrapping.Wrap;
                b.TextTrimming = TextTrimming.None;
                b.TextAlignment = longText ? TextAlignment.Left : TextAlignment.Center;
                b.LineHeight = 18;
                b.Margin = new Thickness(0, 8, 0, 0);
                sp.Children.Add(b);
            }
            var buttons = new Grid { Margin = new Thickness(0, 22, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            cancel = new GlassButton(cancelText, ButtonKind.Secondary, null, 32);
            ok = new GlassButton(okText, ButtonKind.Primary, null, 32);
            Grid.SetColumn(ok, 2);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            sp.Children.Add(buttons);
            cancel.Click += () => Finish(false);
            ok.Click += () => Finish(true);
            sp.MouseLeftButtonDown += (s, e) =>
            {
                try
                {
                    DragMove();
                }
                catch { }
            };
            Body = sp;
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Finish(false);
                }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    Finish(true);
                }
            };
            ContentRendered += (s, e) => ok.Focus();
        }

        public void Place(Window owner)
        {
            CenterOn(owner);
            if (Owner == null)
                Topmost = true; // nothing to sit on (the app is in the tray): don't get buried
        }

        void Finish(bool result)
        {
            Result = result;
            Close();
        }

        public static Window CreatePreview(AppController app, string state)
        {
            if (state == "long")
                return new GlassAlert(L.T("warp.consent.title"), L.T("warp.consent.body"), L.T("btn.ok"), L.T("btn.cancel"));
            return new GlassAlert(L.T("look.confirm.title"), L.T("look.confirm"), L.T("look.confirm.ok"), L.T("btn.cancel"));
        }
    }

    // Text field for glass surfaces: a transparent text box in a soft rounded well, theme caret
    // and selection, an accent focus ring, an optional placeholder and a red rim when invalid.
    public class GlassField : Grid
    {
        public readonly TextBox Box;
        readonly Border frame, ring;
        readonly TextBlock placeholder;
        bool invalid;

        public GlassField(bool multiline = false, bool mono = false, double height = 32, string placeholderText = null)
        {
            Height = height;
            ring = new Border
            {
                CornerRadius = new CornerRadius(multiline ? 13 : 12),
                BorderBrush = GlassTheme.Accent,
                BorderThickness = new Thickness(3),
                Margin = new Thickness(-3.5),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            frame = new Border
            {
                CornerRadius = new CornerRadius(multiline ? 10 : 9),
                Background = GlassTheme.Control,
                BorderBrush = GlassTheme.ControlRim,
                BorderThickness = new Thickness(1),
            };
            Box = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = GlassTheme.Text,
                CaretBrush = GlassTheme.AccentText,
                SelectionBrush = GlassTheme.Selection,
                SelectionOpacity = 1,
                FontFamily = mono ? GlassTheme.Mono : GlassTheme.TextFont,
                FontSize = mono ? 12.5 : 13.5,
                FocusVisualStyle = null,
                Padding = multiline ? new Thickness(8, 7, 8, 7) : new Thickness(8, 0, 8, 0),
                VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
            };
            if (multiline)
            {
                Box.AcceptsReturn = true;
                Box.TextWrapping = TextWrapping.Wrap;
                Box.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                Box.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            placeholder = G.Text(placeholderText ?? "", Box.FontSize >= 13 ? TextStyle.Body : TextStyle.Callout, GlassTheme.Tertiary);
            placeholder.FontFamily = Box.FontFamily;
            placeholder.FontSize = Box.FontSize;
            placeholder.IsHitTestVisible = false;
            placeholder.Margin = multiline ? new Thickness(11, 8, 10, 0) : new Thickness(11, 0, 10, 0);
            placeholder.VerticalAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
            Children.Add(ring);
            Children.Add(frame);
            Children.Add(placeholder);
            Children.Add(Box);
            if (multiline)
            {
                var bar = OverlayScrollBar.For(Box);
                bar.Margin = new Thickness(0, 5, 2, 5);
                Children.Add(bar);
            }
            Box.TextChanged += (s, e) => UpdatePlaceholder();
            Box.GotKeyboardFocus += (s, e) => ring.BeginAnimation(OpacityProperty, Motion.To(0.5, 160, new QuadraticEase()));
            Box.LostKeyboardFocus += (s, e) => ring.BeginAnimation(OpacityProperty, Motion.To(0, 200, new QuadraticEase()));
            UpdatePlaceholder();
        }

        public string Text
        {
            get { return Box.Text; }
            set { Box.Text = value ?? ""; }
        }

        public string Placeholder
        {
            set { G.SetText(placeholder, value); }
        }

        public bool Invalid
        {
            get { return invalid; }
            set
            {
                invalid = value;
                frame.BorderBrush = value ? GlassTheme.Danger : GlassTheme.ControlRim;
                ring.BorderBrush = value ? (Brush)GlassTheme.Danger : GlassTheme.Accent;
            }
        }

        void UpdatePlaceholder()
        {
            placeholder.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Slim overlay scrollbar for glass surfaces (the system one is a grey Windows bar). A thin
    // rounded thumb that brightens and widens while scrolling or hovered, then settles back
    // after a moment (one-shot timer, nothing animates while idle). Drag the thumb, or click
    // the lane to page.
    public class OverlayScrollBar : Border
    {
        readonly Border thumb;
        readonly TranslateTransform move = new TranslateTransform();
        readonly Func<double> offset, extent, viewport;
        readonly Action<double> scrollTo;
        readonly DispatcherTimer settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
        bool dragging, hovering, scrollable, active;
        double dragY, dragOffset, thumbH;
        const double Idle = 0.38, Lit = 0.85;

        public OverlayScrollBar(Func<double> offset, Func<double> extent, Func<double> viewport, Action<double> scrollTo)
        {
            this.offset = offset;
            this.extent = extent;
            this.viewport = viewport;
            this.scrollTo = scrollTo;
            Width = 12;
            HorizontalAlignment = HorizontalAlignment.Right;
            Background = Brushes.Transparent;
            Cursor = Cursors.Arrow;
            thumb = new Border
            {
                Width = 5,
                CornerRadius = new CornerRadius(3),
                Background = GlassTheme.Tertiary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = move,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            Child = thumb;
            IsHitTestVisible = false;
            settle.Tick += (s, e) =>
            {
                settle.Stop();
                if (!dragging && !hovering)
                    Light(false);
            };
            SizeChanged += (s, e) => Update(false);
            MouseEnter += (s, e) =>
            {
                hovering = true;
                Light(true);
                thumb.BeginAnimation(WidthProperty, Motion.To(7, 160, new QuadraticEase()));
            };
            MouseLeave += (s, e) =>
            {
                hovering = false;
                if (!dragging)
                {
                    thumb.BeginAnimation(WidthProperty, Motion.To(5, 200, new QuadraticEase()));
                    Restart();
                }
            };
            MouseLeftButtonDown += (s, e) =>
            {
                double y = e.GetPosition(this).Y;
                if (y >= move.Y && y <= move.Y + thumbH)
                {
                    dragging = true;
                    dragY = y;
                    dragOffset = offset();
                    CaptureMouse();
                }
                else
                {
                    double vp = viewport();
                    scrollTo(Clamp(offset() + (y < move.Y ? -vp : vp) * 0.9));
                }
                e.Handled = true;
            };
            MouseMove += (s, e) =>
            {
                if (!dragging)
                    return;
                double track = ActualHeight - thumbH;
                if (track <= 0)
                    return;
                double range = extent() - viewport();
                scrollTo(Clamp(dragOffset + (e.GetPosition(this).Y - dragY) * range / track));
            };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging)
                    return;
                dragging = false;
                ReleaseMouseCapture();
                if (!IsMouseOver)
                {
                    thumb.BeginAnimation(WidthProperty, Motion.To(5, 200, new QuadraticEase()));
                    Restart();
                }
            };
        }

        // For a ScrollViewer: the bar follows its offset; the viewer's own bar is hidden.
        public static OverlayScrollBar For(ScrollViewer sv)
        {
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            var bar = new OverlayScrollBar(() => sv.VerticalOffset, () => sv.ExtentHeight, () => sv.ViewportHeight, v =>
            {
                var smooth = sv as SmoothScrollViewer;
                if (smooth != null)
                    smooth.JumpTo(v);
                else
                    sv.ScrollToVerticalOffset(v);
            });
            sv.ScrollChanged += (s, e) => bar.Update(e.VerticalChange != 0 || e.ExtentHeightChange != 0);
            return bar;
        }

        // For a text box (multiline field, logs).
        public static OverlayScrollBar For(TextBoxBase box)
        {
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            var bar = new OverlayScrollBar(() => box.VerticalOffset, () => box.ExtentHeight, () => box.ViewportHeight, box.ScrollToVerticalOffset);
            box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => bar.Update(e.VerticalChange != 0 || e.ExtentHeightChange != 0)));
            return bar;
        }

        double Clamp(double v)
        {
            return Math.Max(0, Math.Min(v, Math.Max(0, extent() - viewport())));
        }

        // Re-measures the thumb; reveal = the content moved, light the thumb up for a moment.
        public void Update(bool reveal)
        {
            double ext = extent(), vp = viewport(), off = offset(), track = ActualHeight;
            bool can = ext > vp + 0.5 && track > 0 && vp > 0;
            if (can != scrollable)
            {
                scrollable = can;
                IsHitTestVisible = can;
                thumb.BeginAnimation(OpacityProperty, Motion.To(can ? (active ? Lit : Idle) : 0, 180, new QuadraticEase()));
            }
            if (!can)
                return;
            thumbH = Math.Max(26, track * vp / ext);
            thumb.Height = thumbH;
            move.Y = (track - thumbH) * Math.Max(0, Math.Min(1, off / (ext - vp)));
            if (reveal)
            {
                Light(true);
                Restart();
            }
        }

        void Restart()
        {
            settle.Stop();
            settle.Start();
        }

        void Light(bool on)
        {
            if (!scrollable || on == active)
                return;
            active = on;
            thumb.BeginAnimation(OpacityProperty, Motion.To(on ? Lit : Idle, on ? 120 : 360, new QuadraticEase()));
        }
    }

    // Scrolls with a short ease instead of 48 px jumps; pair it with OverlayScrollBar.For.
    public class SmoothScrollViewer : ScrollViewer
    {
        static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.Register("AnimatedOffset", typeof(double), typeof(SmoothScrollViewer),
            new PropertyMetadata(0.0, (d, e) =>
            {
                var sv = (SmoothScrollViewer)d;
                if (sv.animating)
                    sv.ScrollToVerticalOffset((double)e.NewValue);
            }));
        double target;
        bool animating;
        DoubleAnimation current;

        public SmoothScrollViewer()
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            Focusable = false;
            PanningMode = PanningMode.VerticalOnly;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (e.Handled || ScrollableHeight <= 0)
                return;
            double from = animating ? target : VerticalOffset;
            target = Math.Max(0, Math.Min(ScrollableHeight, from - e.Delta * 0.6));
            if (GlassTheme.ReduceMotion)
                JumpTo(target);
            else
            {
                var a = new DoubleAnimation(VerticalOffset, target, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                a.Completed += (s, x) =>
                {
                    if (current == a)
                        animating = false;
                };
                current = a;
                animating = true;
                BeginAnimation(AnimatedOffsetProperty, a);
            }
            e.Handled = true;
        }

        // Immediate scroll (thumb drag, programmatic); stops a running ease.
        public void JumpTo(double v)
        {
            if (animating)
            {
                animating = false;
                BeginAnimation(AnimatedOffsetProperty, null);
            }
            ScrollToVerticalOffset(v);
        }
    }
}
