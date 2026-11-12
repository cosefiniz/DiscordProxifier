using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace DiscordUnblocker.Glass
{
    public enum GlassTone
    {
        Light,
        Mixed,
        Dark
    }

    // Colours of one background tone. Everything the glass look paints comes from here.
    public class TonePalette
    {
        public GlassTone Tone;
        public bool IsLight;
        public Color BaseTop, BaseBottom;          // window background gradient
        public Color[] Blobs;                       // slowly drifting colour fields behind the glass
        public double BlobOpacity;
        public Color GlassTop, GlassBottom;         // pane tint (over the blurred backdrop)
        public Color RimTop, RimMid, RimBottom;     // specular edge of a pane
        public Color Sheen;                         // soft highlight on the top of a pane
        public Color Solid;                         // pane fill when transparency is off
        public Color Text, Secondary, Tertiary;     // label colours
        public Color Separator;
        public Color ControlTop, ControlBottom, ControlRim; // secondary buttons, toggle tracks
        public Color Hover;                         // overlay on hover
        public Color AccentTop, AccentBottom, AccentText, OnAccent, AccentGlow;
        public Color ToggleOff, Knob;
        public Color Success, Warning, Danger, Idle;
        public Color InfoTint, DangerTint, InfoIcon, DangerIcon;
        public Color WindowRim;
        public Color Shadow;
        public double ShadowOpacity;
        public Color PopupFill;                     // menus / popups (no backdrop to blur)
        public Color Selection;                     // selected text background

        static Color C(byte r, byte g, byte b, byte a = 255) => Color.FromArgb(a, r, g, b);
        static Color W(byte a) => Color.FromArgb(a, 255, 255, 255);
        static Color K(byte a) => Color.FromArgb(a, 0, 0, 0);

        public static TonePalette For(GlassTone t)
        {
            switch (t)
            {
                case GlassTone.Light:
                    return new TonePalette
                    {
                        Tone = t,
                        IsLight = true,
                        BaseTop = C(244, 245, 251),
                        BaseBottom = C(229, 232, 244),
                        Blobs = new[] { C(150, 165, 245), C(250, 188, 200), C(150, 222, 214), C(196, 182, 246), C(160, 204, 250) },
                        BlobOpacity = 0.85,
                        GlassTop = W(176),
                        GlassBottom = W(138),
                        RimTop = W(255),
                        RimMid = W(150),
                        RimBottom = W(210),
                        Sheen = W(90),
                        Solid = C(250, 250, 253),
                        Text = C(29, 30, 38),
                        Secondary = C(84, 87, 102),
                        Tertiary = C(112, 115, 130),
                        Separator = K(22),
                        ControlTop = W(215),
                        ControlBottom = W(170),
                        ControlRim = K(26),
                        Hover = K(12),
                        AccentTop = C(62, 64, 76),
                        AccentBottom = C(32, 33, 41),
                        AccentText = C(36, 37, 46),
                        OnAccent = C(255, 255, 255),
                        AccentGlow = C(0, 0, 0),
                        ToggleOff = K(34),
                        Knob = C(255, 255, 255),
                        Success = C(25, 160, 105),
                        Warning = C(196, 132, 0),
                        Danger = C(220, 60, 66),
                        Idle = C(138, 141, 153),
                        InfoTint = C(120, 126, 150, 40),
                        DangerTint = C(255, 92, 92, 52),
                        InfoIcon = C(48, 50, 62),
                        DangerIcon = C(205, 48, 56),
                        WindowRim = K(30),
                        Shadow = C(40, 44, 80),
                        ShadowOpacity = 0.28,
                        PopupFill = C(252, 252, 255, 246),
                        Selection = C(40, 42, 54, 50),
                    };
                case GlassTone.Mixed:
                    return new TonePalette
                    {
                        Tone = t,
                        IsLight = false,
                        BaseTop = C(44, 46, 74),
                        BaseBottom = C(30, 30, 50),
                        Blobs = new[] { C(132, 146, 244), C(246, 160, 178), C(84, 196, 214), C(172, 140, 234), C(250, 186, 140) },
                        BlobOpacity = 0.9,
                        GlassTop = W(46),
                        GlassBottom = W(26),
                        RimTop = W(150),
                        RimMid = W(26),
                        RimBottom = W(90),
                        Sheen = W(34),
                        Solid = C(52, 54, 82),
                        Text = C(255, 255, 255),
                        Secondary = W(196),
                        Tertiary = W(150),
                        Separator = W(34),
                        ControlTop = W(52),
                        ControlBottom = W(28),
                        ControlRim = W(70),
                        Hover = W(26),
                        AccentTop = C(146, 160, 250),
                        AccentBottom = C(112, 128, 228),
                        AccentText = C(196, 204, 255),
                        OnAccent = C(255, 255, 255),
                        AccentGlow = C(132, 146, 244),
                        ToggleOff = W(48),
                        Knob = C(255, 255, 255),
                        Success = C(74, 222, 156),
                        Warning = C(250, 196, 70),
                        Danger = C(255, 120, 120),
                        Idle = C(170, 174, 196),
                        InfoTint = C(130, 146, 255, 64),
                        DangerTint = C(255, 96, 96, 60),
                        InfoIcon = C(206, 214, 255),
                        DangerIcon = C(255, 150, 150),
                        WindowRim = W(46),
                        Shadow = C(0, 0, 10),
                        ShadowOpacity = 0.5,
                        PopupFill = C(58, 60, 92, 245),
                        Selection = C(132, 146, 244, 90),
                    };
                default:
                    return new TonePalette
                    {
                        Tone = GlassTone.Dark,
                        IsLight = false,
                        BaseTop = C(16, 17, 26),
                        BaseBottom = C(10, 10, 16),
                        Blobs = new[] { C(104, 120, 214), C(58, 48, 150), C(232, 150, 168), C(60, 170, 200), C(110, 90, 210) },
                        BlobOpacity = 0.75,
                        GlassTop = W(30),
                        GlassBottom = W(14),
                        RimTop = W(120),
                        RimMid = W(16),
                        RimBottom = W(64),
                        Sheen = W(24),
                        Solid = C(30, 32, 44),
                        Text = C(242, 243, 248),
                        Secondary = C(236, 237, 245, 170),
                        Tertiary = C(236, 237, 245, 118),
                        Separator = W(24),
                        ControlTop = W(36),
                        ControlBottom = W(16),
                        ControlRim = W(56),
                        Hover = W(20),
                        AccentTop = C(132, 146, 236),
                        AccentBottom = C(104, 120, 214),
                        AccentText = C(170, 182, 255),
                        OnAccent = C(255, 255, 255),
                        AccentGlow = C(104, 120, 214),
                        ToggleOff = W(40),
                        Knob = C(255, 255, 255),
                        Success = C(62, 207, 142),
                        Warning = C(240, 180, 41),
                        Danger = C(255, 107, 107),
                        Idle = C(130, 134, 150),
                        InfoTint = C(120, 136, 255, 44),
                        DangerTint = C(255, 90, 90, 40),
                        InfoIcon = C(190, 200, 255),
                        DangerIcon = C(255, 140, 140),
                        WindowRim = W(34),
                        Shadow = C(0, 0, 0),
                        ShadowOpacity = 0.6,
                        PopupFill = C(34, 36, 50, 246),
                        Selection = C(104, 120, 214, 110),
                    };
            }
        }
    }

    // Theme tokens as shared, animatable brushes: changing the tone animates every
    // element that uses them at once. Brushes are not frozen on purpose (UI thread only).
    public static class GlassTheme
    {
        public static TonePalette P { get; private set; } = TonePalette.For(GlassTone.Dark);
        public static event Action Changed;

        public static readonly SolidColorBrush Text = new SolidColorBrush();
        public static readonly SolidColorBrush Secondary = new SolidColorBrush();
        public static readonly SolidColorBrush Tertiary = new SolidColorBrush();
        public static readonly SolidColorBrush Separator = new SolidColorBrush();
        public static readonly SolidColorBrush Hover = new SolidColorBrush();
        public static readonly SolidColorBrush AccentText = new SolidColorBrush();
        public static readonly SolidColorBrush OnAccent = new SolidColorBrush();
        public static readonly SolidColorBrush ToggleOff = new SolidColorBrush();
        public static readonly SolidColorBrush Knob = new SolidColorBrush();
        public static readonly SolidColorBrush Success = new SolidColorBrush();
        public static readonly SolidColorBrush Warning = new SolidColorBrush();
        public static readonly SolidColorBrush Danger = new SolidColorBrush();
        public static readonly SolidColorBrush Idle = new SolidColorBrush();
        public static readonly SolidColorBrush Solid = new SolidColorBrush();
        public static readonly SolidColorBrush WindowRim = new SolidColorBrush();
        public static readonly SolidColorBrush PopupFill = new SolidColorBrush();
        public static readonly SolidColorBrush Selection = new SolidColorBrush();
        public static readonly SolidColorBrush InfoTint = new SolidColorBrush();
        public static readonly SolidColorBrush DangerTint = new SolidColorBrush();
        public static readonly SolidColorBrush InfoIcon = new SolidColorBrush();
        public static readonly SolidColorBrush DangerIcon = new SolidColorBrush();
        public static readonly LinearGradientBrush Glass = Vertical();
        public static readonly LinearGradientBrush Rim = Diagonal3();
        public static readonly LinearGradientBrush Sheen = Vertical();
        public static readonly LinearGradientBrush Control = Vertical();
        public static readonly SolidColorBrush ControlRim = new SolidColorBrush();
        public static readonly LinearGradientBrush Accent = Vertical();
        public static readonly LinearGradientBrush Base = Vertical();

        static LinearGradientBrush Vertical()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            b.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            return b;
        }

        static LinearGradientBrush Diagonal3()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1) };
            b.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            b.GradientStops.Add(new GradientStop(Colors.Transparent, 0.5));
            b.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            return b;
        }

        // ---- system settings that change how much glass we draw ----

        // Windows "Transparency effects" off, or a remote session (software rendering):
        // panes become solid, the backdrop stays still.
        public static bool ReduceTransparency { get; private set; }
        // Windows "Show animations" off: motion is shortened to simple fades.
        public static bool ReduceMotion { get; private set; }

        public static void ReadSystemSettings()
        {
            bool transparency = true;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k?.GetValue("EnableTransparency");
                    if (v is int i)
                        transparency = i != 0;
                }
            }
            catch { }
            ReduceTransparency = !transparency || SystemParameters.IsRemoteSession || (RenderCapability.Tier >> 16) == 0;
            ReduceMotion = !SystemParameters.ClientAreaAnimation;
        }

        public static GlassTone Parse(string s)
        {
            return s == "light" ? GlassTone.Light : s == "mixed" ? GlassTone.Mixed : GlassTone.Dark;
        }

        public static string ToConfig(GlassTone t)
        {
            return t == GlassTone.Light ? "light" : t == GlassTone.Mixed ? "mixed" : "dark";
        }

        public static void Apply(GlassTone tone, bool animate)
        {
            P = TonePalette.For(tone);
            TonePalette p = P;
            Duration d = new Duration(TimeSpan.FromMilliseconds(animate && !ReduceMotion ? 520 : 0));
            Set(Text, p.Text, d);
            Set(Secondary, p.Secondary, d);
            Set(Tertiary, p.Tertiary, d);
            Set(Separator, p.Separator, d);
            Set(Hover, p.Hover, d);
            Set(AccentText, p.AccentText, d);
            Set(OnAccent, p.OnAccent, d);
            Set(ToggleOff, p.ToggleOff, d);
            Set(Knob, p.Knob, d);
            Set(Success, p.Success, d);
            Set(Warning, p.Warning, d);
            Set(Danger, p.Danger, d);
            Set(Idle, p.Idle, d);
            Set(Solid, p.Solid, d);
            Set(WindowRim, p.WindowRim, d);
            Set(PopupFill, p.PopupFill, d);
            Set(Selection, p.Selection, d);
            Set(InfoTint, p.InfoTint, d);
            Set(DangerTint, p.DangerTint, d);
            Set(InfoIcon, p.InfoIcon, d);
            Set(DangerIcon, p.DangerIcon, d);
            Set(ControlRim, p.ControlRim, d);
            Set(Glass, d, p.GlassTop, p.GlassBottom);
            Set(Rim, d, p.RimTop, p.RimMid, p.RimBottom);
            Set(Sheen, d, p.Sheen, Color.FromArgb(0, p.Sheen.R, p.Sheen.G, p.Sheen.B));
            Set(Control, d, p.ControlTop, p.ControlBottom);
            Set(Accent, d, p.AccentTop, p.AccentBottom);
            Set(Base, d, p.BaseTop, p.BaseBottom);
            Changed?.Invoke();
        }

        static void Set(SolidColorBrush b, Color c, Duration d)
        {
            if (d.TimeSpan == TimeSpan.Zero)
            {
                b.BeginAnimation(SolidColorBrush.ColorProperty, null);
                b.Color = c;
            }
            else
                b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, d) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        }

        static void Set(GradientBrush b, Duration d, params Color[] colors)
        {
            for (int i = 0; i < colors.Length && i < b.GradientStops.Count; i++)
            {
                GradientStop s = b.GradientStops[i];
                if (d.TimeSpan == TimeSpan.Zero)
                {
                    s.BeginAnimation(GradientStop.ColorProperty, null);
                    s.Color = colors[i];
                }
                else
                    s.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(colors[i], d) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            }
        }

        // ---- typography (Apple-like scale; Segoe UI Variable on Windows 11, Segoe UI otherwise) ----

        static FontFamily text, display;
        public static readonly FontFamily Glyphs = new FontFamily("Segoe MDL2 Assets");
        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");

        public static FontFamily TextFont => text ?? (text = Pick("Segoe UI Variable Text", "Segoe UI"));
        public static FontFamily DisplayFont => display ?? (display = Pick("Segoe UI Variable Display", "Segoe UI"));

        static FontFamily Pick(string preferred, string fallback)
        {
            bool has = Fonts.SystemFontFamilies.Any(f => f.Source.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            return new FontFamily(has ? preferred : fallback);
        }
    }
}
