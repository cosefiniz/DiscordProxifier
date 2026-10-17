using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DiscordUnblocker
{
    // Local preparation only (no network): Data\ binaries, autostart, app shortcuts.
    // Downloads and updates live in Updater and use our own GitHub repo.
    public static class AppSetup
    {
        public static readonly Version AppSemVer = Assembly.GetExecutingAssembly().GetName().Version;
        public static string AppVersion => $"{AppSemVer.Major}.{AppSemVer.Minor}.{AppSemVer.Build}";

        public static string DataDir => Path.Combine(AppContext.BaseDirectory, "Data");

        public static bool HasDll => File.Exists(Path.Combine(DataDir, "x64", "version.dll"));

        // Unpacks Data\Tor.zip on first run. Returns the Data files that are still missing
        // (keys as in the update manifest); an empty list means the app can connect.
        public static Task<List<string>> PrepareLocalDataAsync(Logger logger)
        {
            return Task.Run(() =>
            {
                string data = DataDir;
                Directory.CreateDirectory(data);
                string torExe = Path.Combine(data, "Tor", "tor.exe");
                string torZip = Path.Combine(data, "Tor.zip");
                if (!File.Exists(torExe) && File.Exists(torZip))
                    new Updater(logger, data).InstallTorZip(torZip, null);

                var missing = new List<string>();
                if (!File.Exists(torExe))
                    missing.Add("Tor.zip");
                foreach (string f in new[] { "gost.exe", "wgcf.exe", "wireproxy.exe", "x64/version.dll", "x86/version.dll" })
                    if (!File.Exists(Path.Combine(data, f.Replace('/', '\\'))))
                        missing.Add(f);
                logger.Log(missing.Count == 0 ? "Data check: all files present" : "Data check: missing " + string.Join(", ", missing),
                    missing.Count == 0 ? LogType.NORMAL : LogType.WARNING);
                return missing;
            });
        }

        const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "Discord Unblocker Made By Srp"; // same value name as 01A, so it gets replaced

        public static void SetAutoStart(bool enable, Logger logger)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    string want = $"\"{Process.GetCurrentProcess().MainModule.FileName}\" --tray";
                    if (enable)
                    {
                        if (key.GetValue(RunName) as string != want)
                            key.SetValue(RunName, want);
                    }
                    else if (key.GetValue(RunName) != null)
                    {
                        key.DeleteValue(RunName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log("Could not update the Windows startup entry: " + ex.Message, LogType.WARNING);
            }
        }

        // Desktop and Start menu shortcuts to this exe; the icon comes from the exe (uwu_discord logo).
        public static bool CreateAppShortcuts(Logger logger)
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string[] targets =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs)
            };
            bool ok = true;
            foreach (string dir in targets)
            {
                try
                {
                    var shell = new IWshRuntimeLibrary.WshShell();
                    var link = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(Path.Combine(dir, "Discord Proxifier.lnk"));
                    link.TargetPath = exe;
                    link.WorkingDirectory = Path.GetDirectoryName(exe);
                    link.IconLocation = exe + ",0";
                    link.Description = "Discord Proxifier";
                    link.Save();
                    logger.Log($"Created shortcut in {dir}");
                }
                catch (Exception ex)
                {
                    ok = false;
                    logger.Log($"Could not create shortcut in {dir}: {ex.Message}", LogType.ERROR);
                }
            }
            return ok;
        }
    }
}
