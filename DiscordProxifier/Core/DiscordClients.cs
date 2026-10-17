using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace DiscordUnblocker
{
    public enum ClientState
    {
        Stopped,
        RunningWithProxy,
        RunningDirect
    }

    // One Discord installation (stable, PTB, Canary, Development or a custom folder).
    public class DiscordClient
    {
        public string Key; // flavor for standard installs, "custom:<root>" for user-picked folders
        public string Flavor; // process / exe name without .exe, e.g. "DiscordPTB"
        public string DisplayName;
        public string RootDir; // e.g. %LOCALAPPDATA%\Discord
        public string ShortcutPath; // Start menu .lnk, may be null
        public string ShortcutArgs;
        public string LauncherPath; // what we start: shortcut target, new root launcher, or Update.exe

        public string ExeName => Flavor + ".exe";

        // Squirrel's Update.exe only forwards app arguments given as --a=<arg>.
        public bool IsSquirrelLauncher =>
            Path.GetFileName(LauncherPath ?? "").Equals("Update.exe", StringComparison.OrdinalIgnoreCase);

        // The newest app-x.y.z folder; Discord runs from here and version.dll must live here.
        public string LatestAppDir
        {
            get
            {
                try
                {
                    return Directory
                        .GetDirectories(RootDir, "app-*")
                        .Select(d => new { d, v = ParseVersion(Path.GetFileName(d).Substring(4)) })
                        .OrderByDescending(x => x.v)
                        .Select(x => x.d)
                        .FirstOrDefault();
                }
                catch
                {
                    return null;
                }
            }
        }

        static Version ParseVersion(string s)
        {
            Version v;
            return Version.TryParse(s, out v) ? v : new Version(0, 0);
        }

        public bool ShortcutProxied => ShortcutArgs != null && ShortcutArgs.Contains("proxy-server");
    }

    public class DiscordClients
    {
        readonly Logger logger;
        readonly string dataDir;

        public DiscordClients(Logger logger, string dataDir)
        {
            this.logger = logger;
            this.dataDir = dataDir;
        }

        static readonly string[] Flavors = { "Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment" };

        static string Display(string flavor)
        {
            switch (flavor)
            {
                case "DiscordPTB":
                    return "Discord PTB";
                case "DiscordCanary":
                    return "Discord Canary";
                case "DiscordDevelopment":
                    return "Discord Development";
                default:
                    return flavor;
            }
        }

        // Path segment after "Local" names the flavor, e.g. ...\AppData\Local\DiscordPTB\app-1.0.1.
        static string FlavorFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            foreach (string part in path.Split('\\').Reverse())
            {
                string hit = Flavors.FirstOrDefault(f => f.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                    return hit;
            }
            return null;
        }

        static string RootFromWorkingDir(string wd)
        {
            if (string.IsNullOrEmpty(wd))
                return null;
            string name = Path.GetFileName(wd.TrimEnd('\\'));
            return name.StartsWith("app-", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(wd.TrimEnd('\\')) : wd;
        }

        // Discord's newer root launcher (seen since 1.0.9261) sits next to Update.exe as <Flavor>.exe.
        static string PickLauncher(string root, string flavor)
        {
            string modern = Path.Combine(root, flavor + ".exe");
            if (File.Exists(modern))
                return modern;
            return Path.Combine(root, "Update.exe");
        }

        public List<DiscordClient> Scan(IEnumerable<string> customRoots)
        {
            var found = new List<DiscordClient>();

            // 1. Start menu shortcuts created by the Discord installer.
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs\Discord Inc"
            );
            if (Directory.Exists(startMenu))
            {
                foreach (string lnk in Directory.GetFiles(startMenu, "*.lnk"))
                {
                    try
                    {
                        var shell = new IWshRuntimeLibrary.WshShell();
                        var link = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(lnk);
                        string flavor = FlavorFromPath(link.WorkingDirectory) ?? FlavorFromPath(link.TargetPath);
                        string root = RootFromWorkingDir(link.WorkingDirectory) ?? Path.GetDirectoryName(link.TargetPath);
                        if (flavor == null || root == null || found.Any(c => c.Key == flavor))
                            continue;
                        found.Add(
                            new DiscordClient
                            {
                                Key = flavor,
                                Flavor = flavor,
                                DisplayName = Display(flavor),
                                RootDir = root,
                                ShortcutPath = lnk,
                                ShortcutArgs = link.Arguments,
                                LauncherPath = File.Exists(link.TargetPath) ? link.TargetPath : PickLauncher(root, flavor)
                            }
                        );
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"Could not read shortcut {lnk}: {ex.Message}", LogType.WARNING);
                    }
                }
            }

            // 2. Standard install folders without a shortcut.
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string flavor in Flavors)
            {
                string root = Path.Combine(local, flavor);
                if (found.Any(c => c.Key == flavor) || !LooksLikeDiscord(root))
                    continue;
                found.Add(
                    new DiscordClient
                    {
                        Key = flavor,
                        Flavor = flavor,
                        DisplayName = Display(flavor),
                        RootDir = root,
                        LauncherPath = PickLauncher(root, flavor)
                    }
                );
            }

            // 3. Folders the user picked by hand.
            foreach (string root in customRoots ?? new string[0])
            {
                DiscordClient c = FromFolder(root);
                if (c != null && !found.Any(x => x.RootDir.Equals(c.RootDir, StringComparison.OrdinalIgnoreCase)))
                    found.Add(c);
            }
            return found;
        }

        public static bool LooksLikeDiscord(string root)
        {
            try
            {
                return Directory.Exists(root)
                    && (File.Exists(Path.Combine(root, "Update.exe")) || Directory.GetDirectories(root, "app-*").Length > 0);
            }
            catch
            {
                return false;
            }
        }

        // Builds a client from a folder the user chose; accepts the root or an app-* folder inside it.
        public static DiscordClient FromFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return null;
            string root = RootFromWorkingDir(folder);
            if (!LooksLikeDiscord(root))
                return null;
            var probe = new DiscordClient { RootDir = root };
            string app = probe.LatestAppDir;
            string flavor = FlavorFromPath(root);
            if (flavor == null && app != null)
            {
                string exe = Directory
                    .GetFiles(app, "Discord*.exe")
                    .Select(Path.GetFileNameWithoutExtension)
                    .FirstOrDefault(n => Flavors.Contains(n, StringComparer.OrdinalIgnoreCase));
                flavor = exe ?? "Discord";
            }
            flavor = flavor ?? "Discord";
            return new DiscordClient
            {
                Key = "custom:" + root,
                Flavor = flavor,
                DisplayName = Display(flavor) + " (" + Path.GetFileName(root) + ")",
                RootDir = root,
                LauncherPath = PickLauncher(root, flavor)
            };
        }

        // ---------- proxy arguments ----------

        public static string ProxyArgument(DiscordClient c, string gostPort)
        {
            string flag = $"--proxy-server=http://127.0.0.1:{gostPort}";
            return c.IsSquirrelLauncher ? "--a=" + flag : flag;
        }

        public static string LaunchArguments(DiscordClient c, string gostPort)
        {
            return $"--processStart {c.ExeName} {ProxyArgument(c, gostPort)}";
        }

        // ---------- running state ----------

        // Reads the main Discord process command line (the one without --type=) via WMI.
        public ClientState GetState(DiscordClient c, string gostPort)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE Name = '{c.ExeName}'"))
                {
                    bool any = false;
                    var re = new Regex(@"(^|\s)--proxy-server=http://127\.0\.0\.1:" + Regex.Escape(gostPort) + @"(\s|$)");
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string cmd = mo["CommandLine"] as string ?? "";
                        if (cmd.Contains("--type="))
                            continue;
                        any = true;
                        if (re.IsMatch(cmd))
                            return ClientState.RunningWithProxy;
                    }
                    return any ? ClientState.RunningDirect : ClientState.Stopped;
                }
            }
            catch
            {
                return Process.GetProcessesByName(c.Flavor).Length > 0 ? ClientState.RunningDirect : ClientState.Stopped;
            }
        }

        public void Kill(DiscordClient c)
        {
            foreach (Process p in Process.GetProcessesByName(c.Flavor))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
        }

        // ---------- version.dll ----------

        enum MachineType
        {
            I386 = 0x014c,
            x64 = 0x8664
        }

        static MachineType GetMachineType(string fileName)
        {
            byte[] data = new byte[4096];
            using (var s = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                s.Read(data, 0, data.Length);
            int peHeader = BitConverter.ToInt32(data, 60);
            return (MachineType)BitConverter.ToUInt16(data, peHeader + 4);
        }

        string DllSourceFor(DiscordClient c, string appDir)
        {
            string arch = "x64";
            try
            {
                if (GetMachineType(Path.Combine(appDir, c.ExeName)) == MachineType.I386)
                    arch = "x86";
            }
            catch (Exception ex)
            {
                logger.Log("Could not read Discord's PE header, assuming x64: " + ex.Message, LogType.WARNING);
            }
            return Path.Combine(dataDir, arch, "version.dll");
        }

        static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(s));
        }

        // Puts the right version.dll into the newest app folder. Discord is only closed when the
        // DLL there is missing or outdated and the file is locked by a running Discord.
        // Returns false when no DLL is available (DP-502).
        public bool EnsureDll(DiscordClient c, out bool killedDiscord)
        {
            killedDiscord = false;
            string app = c.LatestAppDir;
            if (app == null)
                return false;
            string source = DllSourceFor(c, app);
            if (!File.Exists(source))
            {
                logger.Log($"version.dll not found at {source}", LogType.ERROR);
                return false;
            }
            string target = Path.Combine(app, "version.dll");
            try
            {
                if (File.Exists(target) && Hash(target) == Hash(source))
                    return true;
            }
            catch { }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    File.Copy(source, target, true);
                    logger.Log($"Installed version.dll into {app}");
                    return true;
                }
                catch (IOException) when (attempt == 0)
                {
                    // Locked by a running Discord from this folder.
                    logger.Log($"version.dll in use, closing {c.DisplayName} to update it", LogType.WARNING);
                    Kill(c);
                    killedDiscord = true;
                    Thread.Sleep(1500);
                }
                catch (Exception ex)
                {
                    logger.Log($"Could not copy version.dll: {ex.Message}", LogType.ERROR);
                    return false;
                }
            }
            return false;
        }

        // Called periodically: after a Discord self-update the new app folder has no DLL yet.
        // Copy it without touching the running Discord (the new folder isn't loaded yet).
        public void PrepareNewAppFolder(DiscordClient c)
        {
            string app = c.LatestAppDir;
            if (app == null || File.Exists(Path.Combine(app, "version.dll")))
                return;
            string source = DllSourceFor(c, app);
            if (!File.Exists(source))
                return;
            try
            {
                File.Copy(source, Path.Combine(app, "version.dll"), false);
                logger.Log($"Discord updated; installed version.dll into {app}");
            }
            catch { }
        }

        // ---------- launch ----------

        // Starts Discord through the proxy. A Discord already running *without* the proxy is
        // restarted, because a second launch would only focus the existing window.
        public bool Launch(DiscordClient c, string gostPort, out bool restarted, out string error)
        {
            restarted = false;
            error = null;
            bool killed;
            if (!EnsureDll(c, out killed))
                logger.Log("Launching without a fresh version.dll; Discord's updater may not use the proxy", LogType.WARNING);
            restarted = killed;

            if (!killed && GetState(c, gostPort) == ClientState.RunningDirect)
            {
                logger.Log($"{c.DisplayName} is running without the proxy, restarting it", LogType.WARNING);
                Kill(c);
                restarted = true;
                Thread.Sleep(1000);
            }

            string args = LaunchArguments(c, gostPort);
            logger.Log($"Starting {c.DisplayName}: {Path.GetFileName(c.LauncherPath)} {args}");
            try
            {
                Process.Start(
                    new ProcessStartInfo(c.LauncherPath, args)
                    {
                        WorkingDirectory = c.LatestAppDir ?? c.RootDir,
                        UseShellExecute = false
                    }
                ).Dispose();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.Log($"Error starting Discord: {ex.Message}", LogType.ERROR);
                return false;
            }
        }

        // ---------- Start menu shortcut ----------

        public void SetShortcutProxy(DiscordClient c, bool enable, string gostPort)
        {
            if (c.ShortcutPath == null)
                return;
            try
            {
                var shell = new IWshRuntimeLibrary.WshShell();
                var link = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(c.ShortcutPath);
                link.Arguments = enable ? LaunchArguments(c, gostPort) : $"--processStart {c.ExeName}";
                link.Save();
                c.ShortcutArgs = link.Arguments;
                logger.Log($"Shortcut '{Path.GetFileName(c.ShortcutPath)}' arguments: {link.Arguments}");
            }
            catch (Exception ex)
            {
                logger.Log($"Error updating shortcut: {ex.Message}", LogType.ERROR);
            }
        }

        // Rewrites proxified shortcuts whose port or argument style is out of date
        // (e.g. "--a=" left over for Discord's newer launcher).
        public void FixProxifiedShortcuts(IEnumerable<DiscordClient> clients, string gostPort)
        {
            foreach (DiscordClient c in clients.Where(x => x.ShortcutProxied))
            {
                if ((c.ShortcutArgs ?? "").Trim() != LaunchArguments(c, gostPort))
                    SetShortcutProxy(c, true, gostPort);
            }
        }
    }
}
