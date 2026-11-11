using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordUnblocker
{
    // Discord always talks to gost's HTTP proxy (127.0.0.1:GostPort). gost forwards to a
    // SOCKS5 backend on 127.0.0.1:MainProxyPort, which is either Tor or WARP (wireproxy).
    // Switching modes only swaps the backend; Discord's shortcuts and arguments stay the same.
    public enum ProxyMode
    {
        Tor,
        Warp,
        Direct
    }

    public static class ProxyModes
    {
        // Order of the route switch, fastest first.
        public static readonly ProxyMode[] All = { ProxyMode.Direct, ProxyMode.Warp, ProxyMode.Tor };

        public static int Index(ProxyMode m)
        {
            return Array.IndexOf(All, m);
        }

        public static ProxyMode Parse(string s)
        {
            ProxyMode m;
            return Enum.TryParse((s ?? "").Trim(), true, out m) ? m : ProxyMode.Tor;
        }

        public static string ToConfig(ProxyMode m)
        {
            return m.ToString();
        }

        public static string Display(ProxyMode m)
        {
            return m == ProxyMode.Warp ? "WARP" : m == ProxyMode.Direct ? L.T("mode.direct") : "Tor";
        }

        // What to suggest when a route fails: the next one that works differently.
        public static ProxyMode Next(ProxyMode m)
        {
            return m == ProxyMode.Direct ? ProxyMode.Warp : m == ProxyMode.Warp ? ProxyMode.Tor : ProxyMode.Direct;
        }
    }

    // Child processes assigned here are killed by Windows when this app exits or crashes,
    // so tor/gost/wireproxy can no longer be orphaned.
    internal static class ChildProcessJob
    {
        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        const int JobObjectExtendedLimitInformation = 9;
        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(
            IntPtr hJob,
            int infoType,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo,
            uint cbJobObjectInfoLength
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        static readonly IntPtr job = Create();

        static IntPtr Create()
        {
            IntPtr h = CreateJobObject(IntPtr.Zero, null);
            if (h == IntPtr.Zero)
                return h;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            SetInformationJobObject(
                h,
                JobObjectExtendedLimitInformation,
                ref info,
                (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))
            );
            return h;
        }

        public static bool Add(Process p)
        {
            return job != IntPtr.Zero && AssignProcessToJobObject(job, p.Handle);
        }
    }

    public abstract class ProxyProcess
    {
        protected readonly Logger logger;
        protected readonly string dataDir;
        protected Process process;
        string lastLine;

        // Strips the timestamps tools prefix their lines with, so repeats can be detected.
        static readonly Regex TimestampRe = new Regex(
            @"^(\w+: )?(\d{4}/\d\d/\d\d \d\d:\d\d:\d\d|\w{3} \d\d \d\d:\d\d:\d\d\.\d+)\s*"
        );

        protected ProxyProcess(Logger logger, string dataDir)
        {
            this.logger = logger;
            this.dataDir = dataDir;
        }

        public abstract string Name { get; }

        public virtual bool IsAlive
        {
            get
            {
                try
                {
                    return process != null && !process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        // Return true for output lines worth showing in the Logs window.
        protected virtual bool ShouldLog(string line)
        {
            return true;
        }

        protected virtual void OnOutput(string line) { }

        static LogType Severity(string line)
        {
            string l = line.ToLowerInvariant();
            if (l.Contains("[err]") || l.Contains("error") || l.Contains("fatal"))
                return LogType.ERROR;
            if (l.Contains("[warn]") || l.Contains("warning"))
                return LogType.WARNING;
            return LogType.NORMAL;
        }

        protected Process Launch(string exe, string args, string workDir)
        {
            if (!File.Exists(exe))
                throw new ProxyFailure("DP-103", Path.GetFileName(exe), $"{Name}: {exe} not found");

            var p = new Process();
            p.StartInfo.FileName = exe;
            p.StartInfo.Arguments = args;
            p.StartInfo.WorkingDirectory = workDir;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            // The Go tools and tor print UTF-8; the default OEM code page garbles it.
            p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            p.EnableRaisingEvents = true;
            DataReceivedEventHandler h = (s, e) =>
            {
                if (e.Data == null)
                    return;
                OnOutput(e.Data);
                string key = TimestampRe.Replace(e.Data, "");
                if (key != lastLine && ShouldLog(e.Data))
                    logger.Log($"[{Name}] {e.Data}", Severity(e.Data));
                lastLine = key;
            };
            p.OutputDataReceived += h;
            p.ErrorDataReceived += h;

            logger.Log($"Starting {Name}: {Path.GetFileName(exe)} {args}");
            p.Start();
            if (!ChildProcessJob.Add(p))
                logger.Log($"{Name}: could not attach to job object", LogType.WARNING);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        public virtual void Stop()
        {
            Process p = process;
            process = null;
            if (p == null)
                return;
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                logger.Log($"{Name} stopped");
            }
            catch (Exception ex)
            {
                logger.Log($"Error stopping {Name}: {ex.Message}", LogType.ERROR);
            }
            finally
            {
                p.Dispose();
            }
        }

        // First port in [start, start+count) that nobody listens on, skipping 'avoid'.
        public static int FreePort(int start, int count, params int[] avoid)
        {
            for (int p = start; p < start + count; p++)
                if (Array.IndexOf(avoid, p) < 0 && !IsPortOpen(p))
                    return p;
            return start + count;
        }

        public static bool IsPortOpen(int port)
        {
            using (var c = new TcpClient())
            {
                try
                {
                    Task t = c.ConnectAsync(IPAddress.Loopback, port);
                    t.ContinueWith(x => x.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    return t.Wait(500) && c.Connected;
                }
                catch
                {
                    return false;
                }
            }
        }

        protected async Task<bool> WaitForPortAsync(int port, TimeSpan timeout, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                ct.ThrowIfCancellationRequested();
                if (!IsAlive)
                    return false;
                if (await Task.Run(() => IsPortOpen(port)))
                    return true;
                await Task.Delay(300, ct);
            }
            return false;
        }

        protected static void EnsurePortFree(int port)
        {
            if (IsPortOpen(port))
                throw new ProxyFailure("DP-101", port.ToString(), $"Port {port} is already in use");
        }
    }

    public abstract class ProxyBackend : ProxyProcess
    {
        protected ProxyBackend(Logger logger, string dataDir)
            : base(logger, dataDir) { }

        public abstract ProxyMode Mode { get; }

        // Localized progress line, e.g. "Connecting to Tor · 45%". Read by the UI timer.
        public string Progress { get; set; } = "";

        // 0..100 when the backend can tell, -1 otherwise.
        public int Percent { get; protected set; } = -1;

        // How long to wait for the end-to-end Discord probe once the backend is listening.
        public abstract TimeSpan ProbeTimeout { get; }

        // Throws ProxyFailure with a DP-xxx code on failure.
        public abstract Task StartAsync(int socksPort, Configuration cfg, CancellationToken ct);
    }

    public class TorBackend : ProxyBackend
    {
        public TorBackend(Logger logger, string dataDir)
            : base(logger, dataDir) { }

        public override string Name => "Tor";
        public override ProxyMode Mode => ProxyMode.Tor;
        public override TimeSpan ProbeTimeout => TimeSpan.FromSeconds(40);

        TaskCompletionSource<bool> bootstrapped;
        static readonly Regex BootstrapRe = new Regex(@"Bootstrapped (\d+)%");
        int controlPort;
        string cookieFile;

        // Asks Tor for fresh circuits (a new exit). Discord often rejects or rate-limits a
        // particular exit (HTTP 403/429); a new exit fixes that in a second, without a restart.
        public async Task<bool> NewCircuitAsync()
        {
            if (!IsAlive || controlPort == 0 || !File.Exists(cookieFile))
                return false;
            try
            {
                string cookie = BitConverter.ToString(File.ReadAllBytes(cookieFile)).Replace("-", "");
                using (var c = new TcpClient())
                {
                    await c.ConnectAsync(IPAddress.Loopback, controlPort);
                    var s = c.GetStream();
                    var r = new StreamReader(s, Encoding.ASCII);
                    var w = new StreamWriter(s, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };
                    await w.WriteLineAsync("AUTHENTICATE " + cookie);
                    string auth = await r.ReadLineAsync();
                    await w.WriteLineAsync("SIGNAL NEWNYM");
                    string sig = await r.ReadLineAsync();
                    await w.WriteLineAsync("QUIT");
                    bool ok = auth != null && auth.StartsWith("250") && sig != null && sig.StartsWith("250");
                    logger.Log(ok ? "Tor: new circuits requested (NEWNYM)" : $"Tor: NEWNYM refused ({auth} / {sig})", ok ? LogType.NORMAL : LogType.WARNING);
                    return ok;
                }
            }
            catch (Exception ex)
            {
                logger.Log("Tor: NEWNYM failed: " + ex.Message, LogType.WARNING);
                return false;
            }
        }

        protected override bool ShouldLog(string line)
        {
            if (line.Contains("GeoIP"))
                return false; // the bundled zip has no geoip files; not needed for Discord
            return line.Contains("Bootstrapped") || line.Contains("[warn]") || line.Contains("[err]");
        }

        protected override void OnOutput(string line)
        {
            Match m = BootstrapRe.Match(line);
            if (m.Success)
            {
                Percent = int.Parse(m.Groups[1].Value);
                Progress = L.F("progress.tor", Percent);
                if (Percent == 100)
                    bootstrapped?.TrySetResult(true);
            }
        }

        // Accepts lines as given by bridges.torproject.org, with or without a leading "Bridge ".
        static string[] ParseBridges(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new string[0];
            var result = new List<string>();
            foreach (string raw in text.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#"))
                    continue;
                if (l.StartsWith("Bridge ", StringComparison.OrdinalIgnoreCase))
                    l = l.Substring(7).Trim();
                result.Add(l);
            }
            return result.ToArray();
        }

        // Tor Expert Bundle 13.x shipped snowflake-client.exe and webtunnel-client.exe separately;
        // 14.x+ folds both into lyrebird.exe. Pick whatever exists under Data\Tor\PluggableTransports.
        static IEnumerable<string> TransportPluginLines(string torDir)
        {
            string pt = Path.Combine(torDir, "PluggableTransports");
            Func<string, bool> has = exe => File.Exists(Path.Combine(pt, exe));
            const string dir = "PluggableTransports\\";

            if (has("lyrebird.exe"))
            {
                var viaLyrebird = new List<string> { "meek_lite", "obfs2", "obfs3", "obfs4", "scramblesuit" };
                if (!has("webtunnel-client.exe"))
                    viaLyrebird.Add("webtunnel");
                if (!has("snowflake-client.exe"))
                    viaLyrebird.Add("snowflake");
                yield return $"ClientTransportPlugin {string.Join(",", viaLyrebird)} exec {dir}lyrebird.exe";
            }
            if (has("snowflake-client.exe"))
                yield return $"ClientTransportPlugin snowflake exec {dir}snowflake-client.exe";
            if (has("webtunnel-client.exe"))
                yield return $"ClientTransportPlugin webtunnel exec {dir}webtunnel-client.exe";
            if (has("conjure-client.exe"))
                yield return $"ClientTransportPlugin conjure exec {dir}conjure-client.exe -registerURL https://registration.refraction.network/api";
        }

        public override async Task StartAsync(int socksPort, Configuration cfg, CancellationToken ct)
        {
            string torDir = Path.Combine(dataDir, "Tor");
            string dataDirectory = Path.Combine(torDir, "data");
            Directory.CreateDirectory(dataDirectory);
            EnsurePortFree(socksPort);

            string[] bridges = ParseBridges(cfg.TorBridges);
            var torrc = new StringBuilder();
            torrc.AppendLine($"SocksPort 127.0.0.1:{socksPort}");
            torrc.AppendLine($"DataDirectory {dataDirectory}");
            // Control port (cookie auth) so a new exit can be requested without restarting Tor.
            controlPort = FreePort(socksPort + 50, 30, int.Parse(cfg.GostPort));
            cookieFile = Path.Combine(dataDirectory, "control_auth_cookie");
            torrc.AppendLine($"ControlPort 127.0.0.1:{controlPort}");
            torrc.AppendLine("CookieAuthentication 1");
            if (bridges.Length > 0)
            {
                // Relative exec paths are resolved against tor's working directory (Data\Tor).
                torrc.AppendLine("UseBridges 1");
                foreach (string line in TransportPluginLines(torDir))
                    torrc.AppendLine(line);
                foreach (string b in bridges)
                    torrc.AppendLine("Bridge " + b);
                logger.Log($"Tor: using {bridges.Length} bridge(s)");
            }
            File.WriteAllText(Path.Combine(torDir, "torrc"), torrc.ToString());

            bootstrapped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Percent = 0;
            Progress = L.F("progress.tor", 0);
            ct.ThrowIfCancellationRequested();
            process = Launch(Path.Combine(torDir, "tor.exe"), "-f torrc", torDir);
            process.Exited += (s, e) => bootstrapped.TrySetResult(false);

            // Bridges (especially snowflake) can take minutes to bootstrap.
            TimeSpan timeout = TimeSpan.FromSeconds(bridges.Length > 0 ? 240 : 120);
            using (ct.Register(() => bootstrapped.TrySetCanceled()))
            {
                Task done = await Task.WhenAny(bootstrapped.Task, Task.Delay(timeout));
                if (done != bootstrapped.Task)
                    throw new ProxyFailure("DP-201", null, $"Tor did not finish bootstrapping in {timeout.TotalSeconds}s");
                if (!await bootstrapped.Task)
                    throw new ProxyFailure("DP-202", null, "Tor exited during bootstrap");
            }
            if (!await WaitForPortAsync(socksPort, TimeSpan.FromSeconds(10), ct))
                throw new ProxyFailure("DP-202", null, "Tor is not listening on its SOCKS port");
        }
    }

    public class WarpBackend : ProxyBackend
    {
        public WarpBackend(Logger logger, string dataDir)
            : base(logger, dataDir) { }

        public override string Name => "WARP";
        public override ProxyMode Mode => ProxyMode.Warp;
        public override TimeSpan ProbeTimeout => TimeSpan.FromSeconds(20);

        // Set once wireproxy reports a WireGuard handshake response from Cloudflare.
        public bool HandshakeSeen { get; private set; }

        // True when this start created the WARP account; such accounts can take a minute
        // or two before Cloudflare routes their traffic (seen as HTTP 503 from gost).
        public bool FreshAccount { get; private set; }

        string WarpDir => Path.Combine(dataDir, "Warp");
        string AccountFile => Path.Combine(WarpDir, "wgcf-account.toml");
        string ProfileFile => Path.Combine(WarpDir, "wgcf-profile.conf");
        string ActiveProfileFile => Path.Combine(WarpDir, "wgcf-profile.active.conf");

        public static bool HasAccount(string dataDir)
        {
            return File.Exists(Path.Combine(dataDir, "Warp", "wgcf-account.toml"));
        }

        // wireproxy's debug output starts ~50 "Routine: … started" lines; keep only the useful ones.
        protected override bool ShouldLog(string line)
        {
            if (line.Contains("Routine:") || line.Contains("UAPI:") || line.Contains("worker"))
                return false;
            // After the first successful handshake WireGuard repeats handshakes and keepalives
            // every couple of minutes; they only add noise.
            return !(HandshakeSeen && (line.Contains("handshake") || line.Contains("keepalive")));
        }

        protected override void OnOutput(string line)
        {
            if (line.Contains("Received handshake response"))
                HandshakeSeen = true;
        }

        async Task<bool> RunWgcfAsync(string args, CancellationToken ct)
        {
            var helper = new WgcfRunner(logger, dataDir);
            return await helper.RunAsync(Path.Combine(dataDir, "wgcf.exe"), args, WarpDir, ct);
        }

        public override async Task StartAsync(int socksPort, Configuration cfg, CancellationToken ct)
        {
            Directory.CreateDirectory(WarpDir);
            EnsurePortFree(socksPort);

            if (!File.Exists(AccountFile))
            {
                if (!cfg.WarpTosAccepted)
                    throw new ProxyFailure("DP-301", null, "WARP: Cloudflare terms were not accepted");
                Progress = L.T("progress.warp.register");
                if (!await RunWgcfAsync("register --accept-tos", ct) || !File.Exists(AccountFile))
                    throw new ProxyFailure("DP-301", null, "WARP: registration failed");
                FreshAccount = true;
            }
            if (!File.Exists(ProfileFile))
            {
                Progress = L.T("progress.warp.profile");
                if (!await RunWgcfAsync("generate", ct) || !File.Exists(ProfileFile))
                    throw new ProxyFailure("DP-301", null, "WARP: profile generation failed");
            }

            // The default endpoint (engage.cloudflareclient.com:2408) is blocked by some ISPs;
            // an override such as 162.159.192.1:2408 or another WARP port can get around that.
            string profile = File.ReadAllText(ProfileFile);
            if (!string.IsNullOrWhiteSpace(cfg.WarpEndpoint))
            {
                profile = Regex.Replace(
                    profile,
                    @"^\s*Endpoint\s*=.*$",
                    "Endpoint = " + cfg.WarpEndpoint.Trim(),
                    RegexOptions.Multiline
                );
                logger.Log($"WARP: endpoint override {cfg.WarpEndpoint.Trim()}");
            }
            File.WriteAllText(ActiveProfileFile, profile);

            File.WriteAllText(
                Path.Combine(WarpDir, "wireproxy.conf"),
                $"WGConfig = {ActiveProfileFile.Replace('\\', '/')}\r\n\r\n[Socks5]\r\nBindAddress = 127.0.0.1:{socksPort}\r\n"
            );

            Progress = L.T("progress.warp.connect");
            ct.ThrowIfCancellationRequested();
            process = Launch(Path.Combine(dataDir, "wireproxy.exe"), "-c wireproxy.conf", WarpDir);
            // wireproxy listens immediately even if WireGuard can't reach Cloudflare;
            // the manager's end-to-end probe decides whether WARP actually works.
            if (!await WaitForPortAsync(socksPort, TimeSpan.FromSeconds(10), ct))
                throw new ProxyFailure("DP-303", null, "wireproxy did not start listening");
        }
    }

    // Runs one-shot wgcf commands (register / generate) and waits for them to exit.
    class WgcfRunner : ProxyProcess
    {
        public WgcfRunner(Logger logger, string dataDir)
            : base(logger, dataDir) { }

        public override string Name => "wgcf";

        public async Task<bool> RunAsync(string exe, string args, string workDir, CancellationToken ct)
        {
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process = Launch(exe, args, workDir);
            process.Exited += (s, e) => exited.TrySetResult(true);
            if (process.HasExited)
                exited.TrySetResult(true);
            using (ct.Register(() => exited.TrySetCanceled()))
            {
                Task done = await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(60)));
                if (done != exited.Task)
                {
                    logger.Log($"wgcf {args} timed out", LogType.ERROR);
                    Stop();
                    return false;
                }
                await exited.Task;
            }
            process.WaitForExit(); // flush redirected output
            int code = process.ExitCode;
            Stop();
            return code == 0;
        }
    }

    public class ProbeResult
    {
        public bool Ok;
        public int Status; // 0 when no HTTP response was received
        public long Ms;
        public string Detail;
    }

    public class TestItem
    {
        public string Name;
        public bool Reachable;
        public ProbeResult Result;
    }

    public class ProxyManager
    {
        readonly Logger logger;
        readonly string dataDir;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        CancellationTokenSource cts = new CancellationTokenSource();
        LocalHttpProxy gost; // the HTTP proxy Discord uses (formerly gost.exe)
        ProxyBackend backend;
        int httpPort;
        int socksPort;
        string progress = "";

        public ProxyManager(Logger logger, string dataDir)
        {
            this.logger = logger;
            this.dataDir = dataDir;
        }

        public string DataDir => dataDir;
        public bool IsStarting { get; private set; }
        public bool IsRunning => gost != null && backend != null && !IsStarting;
        public bool IsHealthy => IsRunning && gost.IsAlive && backend.IsAlive;
        public ProxyMode? ActiveMode => IsRunning ? backend.Mode : (ProxyMode?)null;
        public int HttpPort => httpPort;
        public AppError LastError { get; private set; }
        public long LastLatencyMs { get; private set; }

        // Raised (on the caller's context) when WARP failed and Tor was used instead.
        public event Action FellBackToTor;

        public string Progress
        {
            get
            {
                ProxyBackend b = backend;
                return b != null && b.Progress.Length > 0 ? b.Progress : progress;
            }
        }

        public int Percent
        {
            get
            {
                ProxyBackend b = backend;
                return b != null ? b.Percent : -1;
            }
        }

        // Which part died, for the "stopped unexpectedly" status; null while healthy.
        public string DeadPart
        {
            get
            {
                if (!IsRunning)
                    return null;
                if (!gost.IsAlive)
                    return gost.Name;
                if (!backend.IsAlive)
                    return backend.Name;
                return null;
            }
        }

        // Quick end-to-end check of the running route, used by the watchdog.
        public async Task<ProbeResult> HealthCheckAsync()
        {
            ProxyBackend b = backend;
            if (!IsRunning || b == null)
                return null;
            return b is DirectBackend
                ? await TlsProbe.ProbeAsync(httpPort, "discord.com", TimeSpan.FromSeconds(15), CancellationToken.None)
                : await ProbeAsync(httpPort, ProbeUrl, TimeSpan.FromSeconds(20), CancellationToken.None);
        }

        public async Task<bool> NewCircuitAsync()
        {
            var tor = backend as TorBackend;
            return tor != null && await tor.NewCircuitAsync();
        }

        // Kills tor/gost/wireproxy left over from a crashed earlier run (only those under our Data dir).
        public void KillOrphans()
        {
            foreach (string name in new[] { "tor", "gost", "wireproxy" })
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        if (path.StartsWith(dataDir, StringComparison.OrdinalIgnoreCase))
                        {
                            logger.Log($"Killing leftover {name} (pid {p.Id})", LogType.WARNING);
                            p.Kill();
                        }
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
        }

        // tor/gost/wireproxy started by *another* DiscordProxifier copy (e.g. the old 01A build).
        // 01A never stopped them on exit, so they keep holding the ports. Matched by the
        // DiscordProxifier Data layout so Tor Browser and unrelated programs are left alone.
        public List<Process> FindForeignLeftovers()
        {
            var result = new List<Process>();
            var suffixes = new[] { "\\data\\gost.exe", "\\data\\tor\\tor.exe", "\\data\\wireproxy.exe" };
            foreach (string name in new[] { "tor", "gost", "wireproxy" })
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        string lower = path.ToLowerInvariant();
                        bool layout = Array.Exists(suffixes, s => lower.EndsWith(s));
                        if (layout && !path.StartsWith(dataDir, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(p);
                            continue;
                        }
                    }
                    catch { }
                    p.Dispose();
                }
            }
            return result;
        }

        static ProxyBackend Create(ProxyMode mode, Logger logger, string dataDir)
        {
            switch (mode)
            {
                case ProxyMode.Warp:
                    return new WarpBackend(logger, dataDir);
                case ProxyMode.Direct:
                    return new DirectBackend(logger, dataDir);
                default:
                    return new TorBackend(logger, dataDir);
            }
        }

        CancellationToken ResetToken()
        {
            cts.Cancel();
            cts = new CancellationTokenSource();
            return cts.Token;
        }

        public async Task<bool> StartAsync(ProxyMode mode, Configuration cfg)
        {
            CancellationToken ct = ResetToken();
            await gate.WaitAsync();
            try
            {
                StopAllInternal();
                LastError = null;
                IsStarting = true;
                httpPort = int.Parse(cfg.GostPort);
                socksPort = int.Parse(cfg.MainProxyPort);

                progress = L.T("progress.gost");
                gost = new LocalHttpProxy(logger);
                await gost.StartAsync(httpPort, socksPort, ct);
                return await StartBackendAsync(mode, cfg, ct);
            }
            catch (OperationCanceledException)
            {
                StopAllInternal();
                return false;
            }
            catch (ProxyFailure f)
            {
                Fail(f);
                return false;
            }
            catch (Exception ex)
            {
                Fail(new ProxyFailure("DP-102", httpPort.ToString(), ex.Message));
                return false;
            }
            finally
            {
                IsStarting = false;
                progress = "";
                gate.Release();
            }
        }

        // Swaps only the SOCKS backend; gost and Discord's proxy setting are untouched.
        public async Task<bool> SwitchModeAsync(ProxyMode mode, Configuration cfg)
        {
            if (gost == null || !gost.IsAlive || cfg.GostPort != httpPort.ToString() || cfg.MainProxyPort != socksPort.ToString())
                return await StartAsync(mode, cfg);

            CancellationToken ct = ResetToken();
            await gate.WaitAsync();
            try
            {
                LastError = null;
                IsStarting = true;
                logger.Log($"Switching mode to {ProxyModes.Display(mode)}");
                backend?.Stop();
                backend = null;
                return await StartBackendAsync(mode, cfg, ct);
            }
            catch (OperationCanceledException)
            {
                StopAllInternal();
                return false;
            }
            catch (ProxyFailure f)
            {
                Fail(f);
                return false;
            }
            catch (Exception ex)
            {
                Fail(new ProxyFailure("DP-401", "error", ex.Message));
                return false;
            }
            finally
            {
                IsStarting = false;
                gate.Release();
            }
        }

        // Order for the automatic route: the preferred (last working) mode, then the rest fastest
        // first. WARP is only tried once its terms were accepted (no dialogs during auto).
        static List<ProxyMode> AutoOrder(ProxyMode preferred, Configuration cfg)
        {
            var order = new List<ProxyMode> { preferred };
            order.AddRange(ProxyModes.All.Where(m => m != preferred));
            if (!cfg.WarpTosAccepted)
                order.Remove(ProxyMode.Warp);
            return order;
        }

        async Task<bool> StartBackendAsync(ProxyMode mode, Configuration cfg, CancellationToken ct)
        {
            if (cfg.AutoRoute)
            {
                List<ProxyMode> order = AutoOrder(mode, cfg);
                var tried = new List<string>();
                foreach (ProxyMode m in order)
                {
                    try
                    {
                        logger.Log($"Auto route: trying {ProxyModes.ToConfig(m)}");
                        progress = L.F("progress.auto", ProxyModes.Display(m));
                        await TryBackendAsync(m, cfg, ct);
                        return true;
                    }
                    catch (ProxyFailure f)
                    {
                        logger.Log($"Auto route: {ProxyModes.ToConfig(m)} failed ({f.Code})", LogType.WARNING);
                        tried.Add($"{ProxyModes.Display(m)} {f.Code}");
                    }
                }
                throw new ProxyFailure("DP-700", string.Join(", ", tried), "No route works: " + string.Join(", ", tried));
            }
            try
            {
                await TryBackendAsync(mode, cfg, ct);
                return true;
            }
            catch (ProxyFailure f) when (mode == ProxyMode.Warp && cfg.WarpFallbackToTor)
            {
                logger.Log($"WARP failed ({f.Code}: {f.Message}), falling back to Tor", LogType.WARNING);
                FellBackToTor?.Invoke();
                await TryBackendAsync(ProxyMode.Tor, cfg, ct);
                return true;
            }
        }

        async Task TryBackendAsync(ProxyMode mode, Configuration cfg, CancellationToken ct)
        {
            backend?.Stop();
            backend = Create(mode, logger, dataDir);
            try
            {
                await backend.StartAsync(socksPort, cfg, ct);

                backend.Progress = L.T("progress.testing");
                ProxyBackend b = backend;
                Func<Task<ProbeResult>> probe = b is DirectBackend
                    ? (Func<Task<ProbeResult>>)(() => TlsProbe.ProbeAsync(httpPort, "discord.com", b.ProbeTimeout, ct))
                    : () => ProbeAsync(httpPort, ProbeUrl, b.ProbeTimeout, ct);
                ProbeResult r = await probe();
                var warp = backend as WarpBackend;
                if (!r.Ok && warp != null && warp.FreshAccount)
                {
                    // New WARP accounts often answer 503 for the first minute or so.
                    backend.Progress = L.T("progress.warp.activating");
                    var sw = Stopwatch.StartNew();
                    while (!r.Ok && sw.Elapsed < TimeSpan.FromSeconds(75))
                    {
                        await Task.Delay(5000, ct);
                        r = await probe();
                    }
                }
                else if (!r.Ok)
                {
                    // WireGuard may need a moment for the first handshake.
                    await Task.Delay(1000, ct);
                    r = await probe();
                }
                logger.Log($"{backend.Name} probe: {r.Detail}", r.Ok ? LogType.NORMAL : LogType.ERROR);
                if (!r.Ok)
                    throw ClassifyProbeFailure(r, backend);

                LastLatencyMs = r.Ms;
                logger.Log($"Proxy is up in {ProxyModes.Display(mode)} mode");
            }
            catch
            {
                backend?.Stop();
                backend = null;
                throw;
            }
        }

        const string ProbeUrl = "https://discord.com/api/v9/gateway";

        static ProxyFailure ClassifyProbeFailure(ProbeResult r, ProxyBackend b)
        {
            if (r.Status == 403 || r.Status == 429)
                return new ProxyFailure("DP-402", "HTTP " + r.Status, r.Detail);
            var direct = b as DirectBackend;
            if (direct != null)
                return new ProxyFailure(direct.DnsFailed ? "DP-602" : "DP-601", null, r.Detail);
            var warp = b as WarpBackend;
            if (warp != null)
                return new ProxyFailure(warp.HandshakeSeen ? "DP-302" : "DP-303", null, r.Detail);
            return new ProxyFailure("DP-401", r.Status > 0 ? "HTTP " + r.Status : "timeout", r.Detail);
        }

        void Fail(ProxyFailure f)
        {
            logger.Log($"{f.Code}: {f.Message}", LogType.ERROR);
            StopAllInternal();
            LastError = AppError.For(f.Code, f.Arg);
        }

        public void ClearError()
        {
            LastError = null;
        }

        void StopAllInternal()
        {
            backend?.Stop();
            backend = null;
            gost?.Stop();
            gost = null;
        }

        public async Task StopAsync()
        {
            ResetToken();
            await gate.WaitAsync();
            try
            {
                StopAllInternal();
                LastError = null;
            }
            finally
            {
                gate.Release();
            }
        }

        // Synchronous stop for app exit; does not wait for an in-flight start.
        public void StopNow()
        {
            cts.Cancel();
            StopAllInternal();
        }

        public static async Task<ProbeResult> ProbeAsync(int httpPort, string url, TimeSpan timeout, CancellationToken ct)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy("http://127.0.0.1:" + httpPort),
                UseProxy = true
            };
            using (var client = new HttpClient(handler) { Timeout = timeout })
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using (HttpResponseMessage resp = await client.GetAsync(url, ct))
                    {
                        return new ProbeResult
                        {
                            Ok = resp.IsSuccessStatusCode,
                            Status = (int)resp.StatusCode,
                            Ms = sw.ElapsedMilliseconds,
                            Detail = $"{url} -> HTTP {(int)resp.StatusCode} in {sw.ElapsedMilliseconds} ms"
                        };
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Exception inner = ex;
                    while (inner.InnerException != null)
                        inner = inner.InnerException;
                    // gost answers a failed upstream CONNECT with 503; surface it as a status.
                    int status = 0;
                    Match m = Regex.Match(inner.Message, @"\((\d{3})\)");
                    if (m.Success)
                        status = int.Parse(m.Groups[1].Value);
                    return new ProbeResult
                    {
                        Ok = false,
                        Status = status,
                        Ms = sw.ElapsedMilliseconds,
                        Detail = $"{url} -> failed after {sw.ElapsedMilliseconds} ms: {inner.Message}"
                    };
                }
            }
        }

        // Manual diagnostics: API (text messages), CDN (images) and media proxy.
        public async Task<List<TestItem>> TestAsync()
        {
            var items = new List<TestItem>();
            if (!IsRunning)
                return items;
            var targets = new[]
            {
                new[] { "API", "https://discord.com/api/v9/gateway" },
                new[] { "CDN", "https://cdn.discordapp.com/embed/avatars/0.png" },
                new[] { "Media", "https://media.discordapp.net/embed/avatars/0.png" }
            };
            bool direct = backend is DirectBackend;
            if (direct)
                targets = targets.Concat(new[] { new[] { "Updater", "https://updates.discord.com/distributions/app/manifests/latest?channel=stable&platform=win&arch=x64" } }).ToArray();
            foreach (string[] t in targets)
            {
                bool viaTls12 = t[0] == "Updater";
                ProbeResult r = direct && !viaTls12
                    ? await TlsProbe.ProbeAsync(httpPort, new Uri(t[1]).Host, TimeSpan.FromSeconds(20), CancellationToken.None)
                    : await ProbeAsync(httpPort, t[1], TimeSpan.FromSeconds(20), CancellationToken.None);
                // Any HTTP answer proves the path works; unsigned media URLs return 401/404 by design.
                // 403 (blocked exit), 429 (rate limited) and 5xx point at a real problem.
                bool reachable = r.Status > 0 && r.Status != 403 && r.Status != 429 && r.Status < 500;
                logger.Log(
                    "Test: " + r.Detail + (reachable && !r.Ok ? " (reachable)" : ""),
                    reachable ? LogType.NORMAL : LogType.ERROR
                );
                items.Add(new TestItem { Name = t[0], Reachable = reachable, Result = r });
            }
            return items;
        }
    }
}
