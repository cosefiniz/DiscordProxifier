using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordUnblocker
{
    // Builds the plain-text diagnostic report users send when something doesn't work.
    // It answers "what does this provider block, and how" on its own, so problems on other
    // networks can be understood without remote access. Personal paths, the machine name,
    // WARP keys and bridge lines never go into it.
    public static class Diagnostics
    {
        public class Context
        {
            public Configuration Config;
            public ProxyManager Proxy;
            public DiscordClients Discord;
            public List<DiscordClient> Clients;
            public DiscordClient Selected;
            public Updater Updater;
            public Logger Logger;
        }

        public static async Task<string> BuildReportAsync(Context c)
        {
            var sb = new StringBuilder();
            Action<string> line = s => sb.AppendLine(s);
            Action<string> section = s =>
            {
                sb.AppendLine();
                sb.AppendLine("== " + s);
            };

            line("Discord Proxifier diagnostic report");
            line($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
            line($"App: {AppSetup.AppVersion}");
            line($"Windows: {Environment.OSVersion.Version} ({(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit), .NET {Environment.Version}");
            line($"Language: {c.Config.Language}, UI culture {System.Globalization.CultureInfo.CurrentUICulture.Name}");

            section("Settings");
            Configuration cfg = c.Config;
            line($"Route: {cfg.Mode} (automatic: {cfg.AutoRoute}), connect on open: {cfg.AutoConnect}, start with Windows: {cfg.OpenAtStartup}");
            line($"Ports: Discord/HTTP {cfg.GostPort}, backend/SOCKS {cfg.MainProxyPort}");
            line($"WARP: terms accepted {cfg.WarpTosAccepted}, account {(WarpBackend.HasAccount(AppSetup.DataDir) ? "present" : "none")}, fallback to Tor {cfg.WarpFallbackToTor}, custom endpoint {(string.IsNullOrWhiteSpace(cfg.WarpEndpoint) ? "none" : cfg.WarpEndpoint)}");
            line($"Tor bridges: {DescribeBridges(cfg.TorBridges)}");

            section("Connection");
            ProxyManager p = c.Proxy;
            line($"State: {(p.IsStarting ? "starting" : p.IsRunning ? "running" : "stopped")}, active route: {p.ActiveMode?.ToString() ?? "-"}, last latency {p.LastLatencyMs} ms");
            if (p.DeadPart != null)
                line($"Stopped part: {p.DeadPart}");
            if (p.LastError != null)
                line($"Last error: {p.LastError.Code} {p.LastError.Arg}");

            section("Program files");
            Manifest m = await c.Updater.FetchManifestAsync();
            if (m == null)
            {
                line("Update server: not reachable (files not verified)");
            }
            else
            {
                line($"Update server: ok, latest app {m.Get(Updater.AppKey)?.Version ?? m.AppVersion}");
                List<string> bad = c.Updater.VerifyDataFiles(m);
                line(bad.Count == 0 ? "All files match the published checksums" : "Missing or changed: " + string.Join(", ", bad));
            }
            line($"Tor: {c.Updater.InstalledTorVersion() ?? "unknown"}, version.dll: {c.Updater.InstalledDllVersion}");

            section("Discord");
            if (c.Clients == null || c.Clients.Count == 0)
                line("No installation found");
            foreach (DiscordClient d in c.Clients ?? new List<DiscordClient>())
            {
                string app = d.LatestAppDir;
                line($"{d.DisplayName}{(d == c.Selected ? " [selected]" : "")}");
                line($"  folder: {Mask(d.RootDir)}, newest: {(app != null ? Path.GetFileName(app) : "-")}");
                line($"  launcher: {Path.GetFileName(d.LauncherPath)} ({(d.IsSquirrelLauncher ? "Squirrel" : "new launcher")})");
                line($"  version.dll in app folder: {(app != null && File.Exists(Path.Combine(app, "version.dll")) ? "yes" : "no")}");
                line($"  state: {c.Discord.GetState(d, cfg.GostPort)}");
                if (d.ShortcutPath != null)
                    line($"  shortcut: {Mask(d.ShortcutArgs)}");
            }

            section("Network (what this provider blocks)");
            foreach (string l in await NetworkProbeAsync())
                line(l);

            section("Recent log");
            string[] log = c.Logger.AllText().Split('\n');
            foreach (string l in log.Skip(Math.Max(0, log.Length - 300)))
                line(Mask(l.TrimEnd('\r')));

            return sb.ToString();
        }

        // Classifies the block: DNS poisoning, SNI filtering, whether the record split gets
        // through, and whether Cloudflare's WARP endpoint answers at all.
        static async Task<List<string>> NetworkProbeAsync()
        {
            var r = new List<string>();
            r.Add($"Internet: {(await DirectBackend.InternetUpAsync() ? "yes" : "no")}");

            string system;
            try
            {
                IPAddress[] sys = await Dns.GetHostAddressesAsync("discord.com");
                system = string.Join(", ", sys.Select(a => a.ToString()));
            }
            catch (Exception ex)
            {
                system = "failed (" + ex.Message + ")";
            }
            IPAddress[] doh = await Doh.Resolve("discord.com");
            r.Add($"discord.com via system DNS: {system}");
            r.Add($"discord.com via DoH: {(doh.Length > 0 ? string.Join(", ", doh.Select(a => a.ToString())) : "failed")}");
            bool poisoned = doh.Length > 0 && !system.Split(new[] { ", " }, StringSplitOptions.None).Any(s => doh.Any(d => d.ToString() == s));
            r.Add($"DNS poisoned: {(doh.Length == 0 ? "unknown" : poisoned ? "yes" : "no")}");

            if (doh.Length > 0)
            {
                bool tcp = await TcpOpen(doh[0], 443);
                r.Add($"TCP to Discord's address: {(tcp ? "open" : "blocked")}");
                if (tcp)
                {
                    bool plain = await TlsTo(doh[0], "discord.com", split: false);
                    bool split = await DirectBackend.WorksNowAsync(TimeSpan.FromSeconds(6));
                    r.Add($"TLS 1.3 with plain ClientHello: {(plain ? "works (no SNI filter)" : "cut (SNI filter)")}");
                    r.Add($"TLS 1.3 with split ClientHello (Direct route): {(split ? "works" : "cut")}");
                }
            }

            // WARP: the default endpoint over UDP can't be probed simply; report name resolution.
            try
            {
                IPAddress[] warp = await Dns.GetHostAddressesAsync("engage.cloudflareclient.com");
                r.Add($"WARP endpoint name resolves: {string.Join(", ", warp.Select(a => a.ToString()))}");
            }
            catch
            {
                r.Add("WARP endpoint name resolves: no");
            }
            return r;
        }

        static async Task<bool> TcpOpen(IPAddress ip, int port)
        {
            using (var c = new TcpClient(ip.AddressFamily))
            {
                try
                {
                    Task t = c.ConnectAsync(ip, port);
                    return await Task.WhenAny(t, Task.Delay(5000)) == t && !t.IsFaulted;
                }
                catch
                {
                    return false;
                }
            }
        }

        static async Task<bool> TlsTo(IPAddress ip, string host, bool split)
        {
            using (var c = new TcpClient(ip.AddressFamily) { NoDelay = true })
            {
                try
                {
                    Task t = c.ConnectAsync(ip, 443);
                    if (await Task.WhenAny(t, Task.Delay(5000)) != t || t.IsFaulted)
                        return false;
                    return await TlsProbe.HandshakeAsync(c.GetStream(), host, TimeSpan.FromSeconds(6), CancellationToken.None);
                }
                catch
                {
                    return false;
                }
            }
        }

        static string DescribeBridges(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "none";
            var kinds = text.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(l => (l.StartsWith("Bridge ", StringComparison.OrdinalIgnoreCase) ? l.Substring(7) : l).Split(' ')[0])
                .GroupBy(k => k)
                .Select(g => $"{g.Count()} {g.Key}");
            return string.Join(", ", kinds) + " (lines not included)";
        }

        // Hides the user's profile path and the machine name.
        public static string Mask(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
                s = s.Replace(profile, "%USERPROFILE%");
            s = s.Replace(Environment.MachineName, "<pc>");
            s = s.Replace(Environment.UserName, "<user>");
            return s;
        }

        // Writes the report to the desktop and returns the file path.
        public static string Save(string report)
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string path = Path.Combine(dir, $"DiscordProxifier-report-{DateTime.Now:yyyyMMdd-HHmm}.txt");
            File.WriteAllText(path, report, new UTF8Encoding(true));
            return path;
        }
    }
}
