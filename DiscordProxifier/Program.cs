using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DiscordUnblocker
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            // Design check: render a Liquid Glass window to a PNG and exit (no app start).
            if (args.Contains("--glass-preview"))
            {
                Environment.ExitCode = Glass.GlassPreview.Run(args);
                return;
            }

            // Render crisply on high-DPI screens; custom controls scale via Theme.S().
            try
            {
                SetProcessDPIAware();
            }
            catch { }
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                Theme.Scale = g.DpiX / 96f;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Configuration cfg = Configuration.Load();
            L.Set(cfg.Language);

            // After a self-update the new exe is started by the old one: wait for it to exit
            // so the single-instance check doesn't ask about it, then remove the old file.
            int waitPid;
            int w = Array.IndexOf(args, "--wait-pid");
            if (w >= 0 && w + 1 < args.Length && int.TryParse(args[w + 1], out waitPid))
            {
                try
                {
                    Process.GetProcessById(waitPid).WaitForExit(15000);
                }
                catch { }
            }
            Updater.CleanupOldExe();

            if (!EnsureSingleInstance())
                return;

            bool startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            var logger = new Logger();
            // An unexpected UI error is logged instead of showing the .NET crash dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => logger.Log("Unexpected error: " + e.Exception, LogType.ERROR);
            bool updated = args.Contains("--updated");
            var app = new AppController(cfg, logger) { JustUpdated = updated, StartedHidden = startHidden };
            if (cfg.Appearance == "classic")
            {
                // Notifications are Liquid Glass banners in both looks; dark matches the classic windows.
                Glass.GlassTheme.ReadSystemSettings();
                Glass.GlassTheme.Apply(Glass.GlassTone.Dark, false);
                Application.Run(new MainWindow(app, startHidden));
            }
            else
                Glass.GlassApp.Run(app, startHidden);
        }

        static bool EnsureSingleInstance()
        {
            Process me = Process.GetCurrentProcess();
            Process[] others = Process.GetProcessesByName(me.ProcessName).Where(p => p.Id != me.Id).ToArray();
            if (others.Length == 0)
                return true;
            DialogResult r = MessageBox.Show(L.T("instance.body"), "Discord Proxifier", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes)
                return false;
            foreach (Process p in others)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch { }
            }
            return true;
        }
    }
}
