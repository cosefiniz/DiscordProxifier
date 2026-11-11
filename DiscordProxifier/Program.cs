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

            if (!EnsureSingleInstance())
                return;

            bool startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            var logger = new Logger();
            // An unexpected UI error is logged instead of showing the .NET crash dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => logger.Log("Unexpected error: " + e.Exception, LogType.ERROR);
            Application.Run(new MainWindow(cfg, logger, startHidden));
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
