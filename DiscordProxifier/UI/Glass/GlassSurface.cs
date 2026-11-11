using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace DiscordUnblocker.Glass
{
    // Apple-like spring: settles with a small, damped overshoot.
    public class SpringEase : EasingFunctionBase
    {
        public double Damping { get; set; } = 0.72;   // 1 = no overshoot
        public double Stiffness { get; set; } = 9;     // oscillation speed over the duration

        public SpringEase()
        {
            EasingMode = EasingMode.EaseIn; // the curve below is already the settle-in shape
        }

        protected override double EaseInCore(double t)
        {
            double z = Damping, w = Stiffness;
            if (z >= 1)
                return 1 - Math.Exp(-w * t) * (1 + w * t);
            double wd = w * Math.Sqrt(1 - z * z);
            return 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + z * w / wd * Math.Sin(wd * t));
        }

        protected override Freezable CreateInstanceCore()
        {
            return new SpringEase();
        }

        public static readonly SpringEase Soft = Make(0.8, 8.5);
        public static readonly SpringEase Bouncy = Make(0.6, 10);
        public static readonly SpringEase Snappy = Make(0.86, 11);

        static SpringEase Make(double d, double s)
        {
            var e = new SpringEase { Damping = d, Stiffness = s };
            e.Freeze();
            return e;
        }
    }

    public static class Motion
    {
        // Durations shrink to (almost) nothing when Windows animations are off.
        public static Duration D(double ms)
        {
            return new Duration(TimeSpan.FromMilliseconds(GlassTheme.ReduceMotion ? Math.Min(ms, 120) : ms));
        }

        public static DoubleAnimation To(double to, double ms, IEasingFunction ease = null)
        {
            return new DoubleAnimation(to, D(ms)) { EasingFunction = GlassTheme.ReduceMotion ? null : (ease ?? SpringEase.Soft) };
        }

        public static void Animate(Animatable target, DependencyProperty p, double to, double ms, IEasingFunction ease = null)
        {
            target.BeginAnimation(p, To(to, ms, ease));
        }

        public static void Animate(UIElement target, DependencyProperty p, double to, double ms, IEasingFunction ease = null)
        {
            target.BeginAnimation(p, To(to, ms, ease));
        }
    }

    // Edge refraction of a rounded pane (Assets/Shaders/Refraction.fx, compiled to ps_2_0 so it
    // also runs in WPF's software renderer).
    public class RefractionEffect : ShaderEffect
    {
        static PixelShader shader;

        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(RefractionEffect), 0);
        public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register("Shape", typeof(Point4D), typeof(RefractionEffect),
            new UIPropertyMetadata(new Point4D(100, 100, 16, 16), PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty ParamsProperty = DependencyProperty.Register("Params", typeof(Point4D), typeof(RefractionEffect),
            new UIPropertyMetadata(new Point4D(9, 0.18, 0.10, 0), PixelShaderConstantCallback(1)));

        public static bool Available => Load() != null;

        static PixelShader Load()
        {
            if (shader == null)
            {
                try
                {
                    Assembly asm = typeof(RefractionEffect).Assembly;
                    string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("Refraction.ps", StringComparison.OrdinalIgnoreCase));
                    if (name == null)
                        return null;
                    var ms = new MemoryStream();
                    using (Stream s = asm.GetManifestResourceStream(name))
                        s.CopyTo(ms);
                    ms.Position = 0;
                    var ps = new PixelShader();
                    ps.SetStreamSource(ms);
                    shader = ps;
                }
                catch
                {
                    return null;
                }
            }
            return shader;
        }

        public RefractionEffect()
        {
            PixelShader = Load();
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(ShapeProperty);
            UpdateShaderValue(ParamsProperty);
        }

        public Point4D Shape
        {
            get { return (Point4D)GetValue(ShapeProperty); }
            set { SetValue(ShapeProperty, value); }
        }

        public Point4D Params
        {
            get { return (Point4D)GetValue(ParamsProperty); }
            set { SetValue(ParamsProperty, value); }
        }
    }

    // The colourful field behind every glass pane: a gradient with soft colour blobs that drift
    // slowly. It only moves while its window is active and visible; otherwise nothing is
    // animating and WPF draws nothing, so an idle or hidden window costs no CPU.
    public class Backdrop : Grid
    {
        readonly Canvas canvas = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
        readonly Ellipse[] blobs;
        readonly RadialGradientBrush[] fills;
        readonly TranslateTransform[] moves;
        // Fractions of the window: x, y, size, drift x, drift y, seconds
        static readonly double[,] Layout =
        {
            { -0.25, -0.12, 1.05, 0.30, 0.18, 21 },
            { 0.45, 0.30, 1.10, -0.28, 0.12, 26 },
            { 0.55, -0.10, 0.70, -0.20, 0.22, 23 },
            { -0.20, 0.62, 0.85, 0.32, -0.10, 29 },
            { 0.40, 0.78, 0.80, -0.22, -0.16, 19 },
        };
        bool moving;

        public Backdrop()
        {
            IsHitTestVisible = false;
            Background = GlassTheme.Base;
            Children.Add(canvas);
            int n = Layout.GetLength(0);
            blobs = new Ellipse[n];
            fills = new RadialGradientBrush[n];
            moves = new TranslateTransform[n];
            for (int i = 0; i < n; i++)
            {
                var rg = new RadialGradientBrush();
                rg.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
                rg.GradientStops.Add(new GradientStop(Colors.Transparent, 0.45));
                rg.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                fills[i] = rg;
                moves[i] = new TranslateTransform();
                blobs[i] = new Ellipse { Fill = rg, RenderTransform = moves[i] };
                canvas.Children.Add(blobs[i]);
            }
            SizeChanged += (s, e) => Place();
            GlassTheme.Changed += () => ApplyTone(true);
            ApplyTone(false);
        }

        void Place()
        {
            double w = ActualWidth, h = ActualHeight;
            double unit = Math.Max(w, h);
            for (int i = 0; i < blobs.Length; i++)
            {
                double size = Layout[i, 2] * unit;
                blobs[i].Width = size;
                blobs[i].Height = size;
                Canvas.SetLeft(blobs[i], Layout[i, 0] * w);
                Canvas.SetTop(blobs[i], Layout[i, 1] * h);
            }
            if (moving)
                Start();
        }

        void ApplyTone(bool animate)
        {
            TonePalette p = GlassTheme.P;
            var d = new Duration(TimeSpan.FromMilliseconds(animate && !GlassTheme.ReduceMotion ? 520 : 0));
            for (int i = 0; i < fills.Length; i++)
            {
                Color c = p.Blobs[i % p.Blobs.Length];
                byte a0 = (byte)(210 * p.BlobOpacity), a1 = (byte)(96 * p.BlobOpacity);
                Color[] stops = { Color.FromArgb(a0, c.R, c.G, c.B), Color.FromArgb(a1, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B) };
                for (int k = 0; k < 3; k++)
                {
                    if (d.TimeSpan == TimeSpan.Zero)
                    {
                        fills[i].GradientStops[k].BeginAnimation(GradientStop.ColorProperty, null);
                        fills[i].GradientStops[k].Color = stops[k];
                    }
                    else
                        fills[i].GradientStops[k].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(stops[k], d));
                }
            }
        }

        // Drift on/off. Called by GlassWindow on activate/deactivate/show/hide.
        public bool Moving
        {
            get { return moving; }
            set
            {
                if (GlassTheme.ReduceMotion || GlassTheme.ReduceTransparency)
                    value = false;
                if (value == moving)
                    return;
                moving = value;
                if (moving)
                    Start();
                else
                    Stop();
            }
        }

        void Start()
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0)
                return;
            for (int i = 0; i < blobs.Length; i++)
            {
                double secs = Layout[i, 5];
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                var ax = new DoubleAnimation(Layout[i, 3] * w, TimeSpan.FromSeconds(secs)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                var ay = new DoubleAnimation(Layout[i, 4] * h, TimeSpan.FromSeconds(secs * 1.27)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                // The drift is slow; 20 frames a second looks the same and costs a third.
                Timeline.SetDesiredFrameRate(ax, 20);
                Timeline.SetDesiredFrameRate(ay, 20);
                moves[i].BeginAnimation(TranslateTransform.XProperty, ax, HandoffBehavior.SnapshotAndReplace);
                moves[i].BeginAnimation(TranslateTransform.YProperty, ay, HandoffBehavior.SnapshotAndReplace);
            }
        }

        void Stop()
        {
            // Freeze where they are (no jump back).
            foreach (TranslateTransform t in moves)
            {
                double x = t.X, y = t.Y;
                t.BeginAnimation(TranslateTransform.XProperty, null);
                t.BeginAnimation(TranslateTransform.YProperty, null);
                t.X = x;
                t.Y = y;
            }
        }
    }

    // A Liquid Glass pane: the backdrop under it, blurred and bent at the edges, a light tint,
    // a specular rim and a soft top sheen. Content goes in Child.
    public class GlassPane : Grid
    {
        readonly Border tintLayer, rimLayer, sheenLayer, fxHost, clip;
        readonly Rectangle blurred;
        readonly VisualBrush brush;
        readonly RefractionEffect fx;
        readonly ContentPresenter presenter = new ContentPresenter();
        FrameworkElement backdrop;
        double radius;
        Rect last;
        bool hooked;

        public double BlurRadius { get; set; } = 18;
        public double Refraction { get; set; } = 9;

        public GlassPane(double cornerRadius = 22)
        {
            radius = cornerRadius;
            brush = new VisualBrush { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            blurred = new Rectangle { Fill = brush };
            clip = new Border { Child = blurred };
            fx = RefractionEffect.Available ? new RefractionEffect() : null;
            fxHost = new Border { Child = clip, Effect = fx, IsHitTestVisible = false };
            tintLayer = new Border { Background = GlassTheme.Glass, IsHitTestVisible = false };
            sheenLayer = new Border { Background = GlassTheme.Sheen, Height = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 1, 1, 0), IsHitTestVisible = false };
            rimLayer = new Border { BorderBrush = GlassTheme.Rim, BorderThickness = new Thickness(1), IsHitTestVisible = false };
            Children.Add(fxHost);
            Children.Add(tintLayer);
            Children.Add(sheenLayer);
            Children.Add(rimLayer);
            Children.Add(presenter);
            CornerRadius = cornerRadius;
            SizeChanged += (s, e) => UpdateShape();
            Loaded += (s, e) => Hook();
            Unloaded += (s, e) => Unhook();
        }

        public UIElement Child
        {
            get { return presenter.Content as UIElement; }
            set { presenter.Content = value; }
        }

        public double CornerRadius
        {
            get { return radius; }
            set
            {
                radius = value;
                tintLayer.CornerRadius = new CornerRadius(value);
                rimLayer.CornerRadius = new CornerRadius(value);
                sheenLayer.CornerRadius = new CornerRadius(value, value, 0, 0);
                UpdateShape();
            }
        }

        // A tinted pane (info / error cards). null = the theme's glass tint.
        public Brush Tint
        {
            set { tintLayer.Background = value ?? GlassTheme.Glass; }
        }

        void Hook()
        {
            GlassWindow w = Window.GetWindow(this) as GlassWindow;
            backdrop = w?.BackdropLayer;
            bool glass = backdrop != null && !GlassTheme.ReduceTransparency;
            fxHost.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
            sheenLayer.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
            if (!glass)
            {
                // Solid surface: same shape and rim, no blur.
                if (tintLayer.Background == GlassTheme.Glass)
                    tintLayer.Background = GlassTheme.Solid;
                return;
            }
            brush.Visual = backdrop;
            blurred.Margin = new Thickness(-BlurRadius * 2);
            blurred.Effect = new BlurEffect { Radius = BlurRadius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
            if (fx != null)
                fx.Params = new Point4D(Refraction, 0.18, GlassTheme.P.IsLight ? 0.04 : 0.10, 0);
            if (!hooked)
            {
                LayoutUpdated += Sync;
                hooked = true;
            }
            Sync(null, null);
        }

        void Unhook()
        {
            if (hooked)
            {
                LayoutUpdated -= Sync;
                hooked = false;
            }
        }

        void UpdateShape()
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0)
                return;
            clip.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
            if (fx != null)
                fx.Shape = new Point4D(w, h, Math.Min(radius, Math.Min(w, h) / 2), Math.Max(4, Math.Min(26, Math.Min(w, h) / 2)));
        }

        // Keeps the blurred copy of the backdrop lined up with where this pane sits.
        void Sync(object sender, EventArgs e)
        {
            if (backdrop == null || PresentationSource.FromVisual(this) == null)
                return;
            Rect r;
            try
            {
                r = TransformToVisual(backdrop).TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
            }
            catch (InvalidOperationException)
            {
                return;
            }
            r.Inflate(BlurRadius * 2, BlurRadius * 2);
            if (r != last)
            {
                last = r;
                brush.Viewbox = r;
            }
        }
    }
}
