using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace DiscordUnblocker.Glass
{
    // Base for every Liquid Glass window: rounded, borderless, soft shadow, the drifting
    // backdrop behind its content, a title row with round buttons, and a spring-in on open.
    // The backdrop only moves while the window is active, visible and not minimized.
    public class GlassWindow : Window
    {
        public const double ShadowMargin = 18;
        public readonly Backdrop BackdropLayer = new Backdrop();
        readonly Grid frame = new Grid();
        readonly Border outer;
        readonly Grid titleRow;
        readonly ContentPresenter body = new ContentPresenter();
        readonly ScaleTransform openScale = new ScaleTransform(1, 1);
        protected readonly StackPanel TitleButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        protected readonly StackPanel TitleLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        protected readonly TextBlock TitleBlock;
        protected readonly GlassButton CloseButton;
        public bool CloseOnEscape { get; set; }
        static BitmapImage logo;

        public GlassWindow(double contentWidth, bool showLogo = false, string title = "Discord Proxifier")
        {
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = contentWidth + ShadowMargin * 2;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            FontFamily = GlassTheme.TextFont;
            Foreground = GlassTheme.Text;

            // Title row
            titleRow = new Grid { Height = 48, Background = Brushes.Transparent, Margin = new Thickness(18, 0, 12, 0) };
            titleRow.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed && e.ClickCount == 1)
                {
                    try
                    {
                        DragMove();
                    }
                    catch { }
                }
            };
            if (showLogo && LogoImage != null)
                TitleLeft.Children.Add(new Image { Source = LogoImage, Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0) });
            TitleBlock = G.Text(title, TextStyle.Headline);
            TitleLeft.Children.Add(TitleBlock);
            titleRow.Children.Add(TitleLeft);
            titleRow.Children.Add(TitleButtons);
            CloseButton = AddTitleButton(Glyph.Close, null, Close);

            var stack = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(titleRow, Dock.Top);
            stack.Children.Add(titleRow);
            stack.Children.Add(body);

            frame.Children.Add(BackdropLayer);
            frame.Children.Add(stack);
            frame.SizeChanged += (s, e) => frame.Clip = new RectangleGeometry(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), 19, 19);

            outer = new Border
            {
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(1),
                BorderBrush = GlassTheme.WindowRim,
                Child = frame,
                Margin = new Thickness(ShadowMargin),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = openScale,
            };
            ApplyShadow();
            Content = outer;

            Activated += (s, e) => UpdateMotion();
            Deactivated += (s, e) => UpdateMotion();
            IsVisibleChanged += (s, e) =>
            {
                UpdateMotion();
                if (IsVisible)
                    PlayOpen();
            };
            StateChanged += (s, e) => UpdateMotion();
            KeyDown += (s, e) =>
            {
                if (CloseOnEscape && e.Key == Key.Escape)
                    Close();
            };
            GlassTheme.Changed += ApplyShadow;
            Closed += (s, e) => GlassTheme.Changed -= ApplyShadow;
        }

        public UIElement Body
        {
            get { return body.Content as UIElement; }
            set { body.Content = value; }
        }

        protected Grid TitleRow => titleRow;

        public static BitmapImage LogoImage
        {
            get
            {
                if (logo == null)
                {
                    try
                    {
                        Assembly asm = typeof(GlassWindow).Assembly;
                        string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase));
                        if (name == null)
                            return null;
                        var b = new BitmapImage();
                        using (Stream s = asm.GetManifestResourceStream(name))
                        {
                            b.BeginInit();
                            b.CacheOption = BitmapCacheOption.OnLoad;
                            b.StreamSource = s;
                            b.EndInit();
                        }
                        b.Freeze();
                        logo = b;
                    }
                    catch
                    {
                        return null;
                    }
                }
                return logo;
            }
        }

        // Round glass button in the title row (added right to left: the first one is outermost).
        public GlassButton AddTitleButton(string glyph, string tooltip, Action click)
        {
            var b = new GlassButton(null, ButtonKind.Icon, glyph, 28) { Margin = new Thickness(6, 0, 0, 0), ToolTip = tooltip };
            b.Click += () => click?.Invoke();
            TitleButtons.Children.Insert(0, b);
            return b;
        }

        void ApplyShadow()
        {
            outer.Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = GlassTheme.P.ShadowOpacity,
                Color = GlassTheme.P.Shadow,
                RenderingBias = RenderingBias.Performance,
            };
        }

        void UpdateMotion()
        {
            BackdropLayer.Moving = IsVisible && IsActive && WindowState != WindowState.Minimized;
        }

        void PlayOpen()
        {
            if (GlassTheme.ReduceMotion)
                return;
            openScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.965, 1, Motion.D(460)) { EasingFunction = SpringEase.Soft });
            openScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.965, 1, Motion.D(460)) { EasingFunction = SpringEase.Soft });
            outer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D(200)));
        }

        // Centre over the owner (or the screen) once the size is known.
        protected void CenterOn(Window owner)
        {
            if (owner != null && owner.IsVisible)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
