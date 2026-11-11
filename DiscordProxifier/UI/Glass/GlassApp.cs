using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DiscordUnblocker.Glass
{
    // Entry point of the Liquid Glass look.
    public static class GlassApp
    {
        public static void Run(AppController app, bool startHidden)
        {
            GlassTheme.ReadSystemSettings();
            GlassTheme.Apply(GlassTheme.Parse(app.Config.GlassTone), false);
            var wpf = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            // An unexpected UI error is logged instead of crashing the app.
            wpf.DispatcherUnhandledException += (s, e) =>
            {
                app.Logger.Log("Unexpected error: " + e.Exception, LogType.ERROR);
                e.Handled = true;
            };
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                // Transparency / animation settings changed in Windows.
                wpf.Dispatcher.BeginInvoke(new Action(GlassTheme.ReadSystemSettings));
            };
            var w = new GlassMainWindow(app, startHidden);
            if (!startHidden)
                w.Show();
            wpf.Run();
        }

        // Settings → Appearance → Background. Live, no restart.
        public static void SetTone(AppController app, GlassTone tone)
        {
            app.Config.GlassTone = GlassTheme.ToConfig(tone);
            app.Config.Save();
            GlassTheme.Apply(tone, true);
        }
    }

    // Renders a glass window to a PNG without starting the app (for design checks):
    //   DiscordProxifier.exe --glass-preview <WindowClass> <light|mixed|dark> <en|tr> <state> <out.png>
    // The window class (in DiscordUnblocker.Glass) provides
    //   public static Window CreatePreview(AppController app, string state)
    // Nothing is connected, saved or registered; no tray icon is created.
    public static class GlassPreview
    {
        public static bool Active { get; private set; }

        public static int Run(string[] args)
        {
            int i = Array.IndexOf(args, "--glass-preview");
            if (i < 0 || args.Length < i + 6)
                return 2;
            string kind = args[i + 1], tone = args[i + 2], lang = args[i + 3], state = args[i + 4], output = args[i + 5];
            Active = true;
            L.Set(lang);
            GlassTheme.ReadSystemSettings();
            GlassTheme.Apply(GlassTheme.Parse(tone), false);
            var wpf = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int code = 0;
            Action<object> fail = x => File.WriteAllText(output + ".error.txt", x.ToString());
            wpf.DispatcherUnhandledException += (s, e) =>
            {
                fail(e.Exception);
                code = 1;
                e.Handled = true;
                wpf.Shutdown(1);
            };
            var app = new AppController(new Configuration(), new Logger());
            Type t = typeof(GlassPreview).Assembly.GetType("DiscordUnblocker.Glass." + kind);
            MethodInfo create = t?.GetMethod("CreatePreview", BindingFlags.Public | BindingFlags.Static);
            if (create == null)
            {
                fail("No public static CreatePreview(AppController, string) on " + kind);
                return 2;
            }
            var w = (Window)create.Invoke(null, new object[] { app, state });
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -12000;
            w.Top = 0;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    w.UpdateLayout();
                    double k = 2;
                    var rtb = new RenderTargetBitmap((int)(w.ActualWidth * k), (int)(w.ActualHeight * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
                    rtb.Render(w);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (FileStream f = File.Create(output))
                        enc.Save(f);
                }
                catch (Exception ex)
                {
                    fail(ex);
                    code = 1;
                }
                wpf.Shutdown(code);
            };
            timer.Start();
            wpf.Run();
            return code;
        }
    }
}
