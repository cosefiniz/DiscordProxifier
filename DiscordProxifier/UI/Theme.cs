using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    // Dark, calm palette. The accent is taken from the uwu_discord logo outline.
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(23, 24, 26);
        public static readonly Color Surface = Color.FromArgb(31, 33, 36);
        public static readonly Color Control = Color.FromArgb(38, 41, 45);
        public static readonly Color ControlHover = Color.FromArgb(47, 51, 56);
        public static readonly Color Border = Color.FromArgb(44, 47, 52);
        public static readonly Color BorderStrong = Color.FromArgb(60, 64, 72);
        public static readonly Color Text = Color.FromArgb(232, 233, 235);
        public static readonly Color TextMuted = Color.FromArgb(154, 157, 163);
        public static readonly Color TextFaint = Color.FromArgb(107, 111, 118);
        public static readonly Color Accent = Color.FromArgb(104, 120, 214);
        public static readonly Color AccentHover = Color.FromArgb(122, 137, 226);
        public static readonly Color Success = Color.FromArgb(62, 207, 142);
        public static readonly Color Warning = Color.FromArgb(240, 180, 41);
        public static readonly Color Danger = Color.FromArgb(255, 107, 107);
        public static readonly Color DangerBg = Color.FromArgb(42, 29, 31);
        public static readonly Color DangerBorder = Color.FromArgb(92, 43, 47);
        public static readonly Color Idle = Color.FromArgb(107, 111, 118);
        public static readonly Color InfoBg = Color.FromArgb(30, 33, 50);
        public static readonly Color InfoBorder = Color.FromArgb(62, 72, 128);
        public static readonly Color InfoText = Color.FromArgb(200, 206, 232);
        public static readonly Color DangerText = Color.FromArgb(214, 198, 199);

        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Strong = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 10.5f);
        public static readonly Font Status = new Font("Segoe UI Semibold", 12f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);

        // Segoe MDL2 Assets ships with Windows 10/11; glyphs fall back to text if it is missing.
        // Cheap check: GDI+ substitutes a fallback family when the font is not installed.
        // (Enumerating InstalledFontCollection took a noticeable part of startup.)
        static readonly bool hasIcons = HasFont("Segoe MDL2 Assets");

        static bool HasFont(string name)
        {
            using (var f = new Font(name, 10))
                return f.Name == name;
        }
        static readonly Dictionary<float, Font> iconFonts = new Dictionary<float, Font>();

        public static Font Icons(float size)
        {
            Font f;
            if (!iconFonts.TryGetValue(size, out f))
            {
                f = new Font(hasIcons ? "Segoe MDL2 Assets" : "Segoe UI Symbol", size);
                iconFonts[size] = f;
            }
            return f;
        }

        public static class Glyph
        {
            public const string Close = "";
            public const string Minimize = "";
            public const string ToTray = "";
            public const string Settings = "";
            public const string Logs = "";
            public const string Test = "";
            public const string Globe = "";
            public const string Cloud = "";
            public const string Play = "";
            public const string Refresh = "";
            public const string Warning = "";
            public const string Check = "";
            public const string Info = "";
            public const string Copy = "";
            public const string Folder = "";
            public const string Bolt = "";
            public const string Link = "";
            public const string ChevronDown = "";
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t)
            );
        }

        public static void DrawText(Graphics g, string text, Font font, Rectangle r, Color c, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, r, c, flags | TextFormatFlags.NoPrefix);
        }

        // Logical (96 dpi) pixels to device pixels.
        public static float Scale = 1f;

        public static int S(float px)
        {
            return (int)Math.Round(px * Scale);
        }
    }

    // A tiny tween engine on one UI timer. Starting a tween with an existing key replaces it.
    public static class Anim
    {
        class Tween
        {
            public float From, To;
            public double Ms;
            public Stopwatch Clock;
            public Action<float> Apply;
            public Action Done;
        }

        static readonly Dictionary<object, Tween> active = new Dictionary<object, Tween>();
        static readonly Timer timer = CreateTimer();
        static readonly List<Action> tickers = new List<Action>();

        static Timer CreateTimer()
        {
            var t = new Timer { Interval = 15 };
            t.Tick += (s, e) => Tick();
            return t;
        }

        public static float EaseOut(float t)
        {
            float u = 1 - t;
            return 1 - u * u * u;
        }

        public static void Run(object key, float from, float to, int ms, Action<float> apply, Action done = null)
        {
            if (ms <= 0)
            {
                apply(to);
                done?.Invoke();
                active.Remove(key);
                return;
            }
            active[key] = new Tween
            {
                From = from,
                To = to,
                Ms = ms,
                Clock = Stopwatch.StartNew(),
                Apply = apply,
                Done = done
            };
            timer.Start();
        }

        // Continuous per-frame callbacks (pulsing dots, indeterminate progress).
        public static void AddTicker(Action a)
        {
            if (!tickers.Contains(a))
                tickers.Add(a);
            timer.Start();
        }

        public static void RemoveTicker(Action a)
        {
            tickers.Remove(a);
        }

        static void Tick()
        {
            // A tween whose control was closed or disposed must not take the app down with it
            // (it used to: a toast still sliding in when its window closed threw from the timer).
            foreach (var kv in active.ToList())
            {
                Tween t = kv.Value;
                float p = (float)Math.Min(1, t.Clock.Elapsed.TotalMilliseconds / t.Ms);
                try
                {
                    t.Apply(t.From + (t.To - t.From) * EaseOut(p));
                    if (p >= 1)
                    {
                        active.Remove(kv.Key);
                        t.Done?.Invoke();
                    }
                }
                catch (Exception)
                {
                    active.Remove(kv.Key);
                }
            }
            foreach (Action a in tickers.ToList())
            {
                try
                {
                    a();
                }
                catch (Exception)
                {
                    tickers.Remove(a);
                }
            }
            if (active.Count == 0 && tickers.Count == 0)
                timer.Stop();
        }
    }
}
