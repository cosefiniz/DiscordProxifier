using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DiscordUnblocker
{
    // "Direct" route: no relay at all. Measured on Superonline (2026-11): Discord is blocked by
    // (1) DNS answers pointing at a block page and (2) an SNI filter that resets TLS when the
    // ClientHello names a Discord host. The filter reassembles TCP segments but does not join
    // TLS records, so splitting the ClientHello into two TLS records inside the host name gets
    // through. This backend is a loopback SOCKS5 server that resolves names over DoH and
    // re-frames the first TLS record that way; everything after it is passed through untouched.
    public class DirectBackend : ProxyBackend
    {
        public DirectBackend(Logger logger, string dataDir)
            : base(logger, dataDir) { }

        public override string Name => "Direct";
        public override ProxyMode Mode => ProxyMode.Direct;
        public override TimeSpan ProbeTimeout => TimeSpan.FromSeconds(15);

        TcpListener listener;
        CancellationTokenSource cts;
        readonly HashSet<string> reported = new HashSet<string>();
        readonly ConcurrentDictionary<TcpClient, byte> open = new ConcurrentDictionary<TcpClient, byte>();

        // TLS 1.2 clients can't use the split trick: in TLS 1.2 the server certificate is sent in
        // clear text and the filter cuts the reply when it sees a Discord name in it. Discord's
        // own traffic (Chromium) is TLS 1.3, but its updater (updater.node, Windows SChannel on
        // Windows 10) is TLS 1.2 and would hang on "Checking for updates". Those connections go
        // through a helper route (WARP if set up, otherwise Tor) started in the background.
        ProxyBackend helper;
        int helperPort;
        TaskCompletionSource<bool> helperReady;

        // Set when name resolution over DoH failed for the probe host (DP-602 vs DP-601).
        public bool DnsFailed { get; private set; }

        public string HelperName => helper?.Name;

        public override bool IsAlive => listener != null;

        public override Task StartAsync(int socksPort, Configuration cfg, CancellationToken ct)
        {
            EnsurePortFree(socksPort);
            Progress = L.T("progress.direct");
            cts = new CancellationTokenSource();
            listener = new TcpListener(IPAddress.Loopback, socksPort);
            listener.Start(256);
            NoInherit(listener.Server);
            logger.Log($"Direct: listening on 127.0.0.1:{socksPort} (DoH + TLS record split)");
            CancellationToken token = cts.Token;
            Task.Run(() => AcceptLoop(token));

            helperReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            helperPort = FreePortNear(socksPort, int.Parse(cfg.GostPort));
            Task.Run(() => StartHelperAsync(cfg, token));
            return Task.FromResult(true);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        // .NET Framework sockets are inheritable, and child processes (the WARP/Tor helper,
        // anything started with UseShellExecute=false) would keep a copy that holds the port.
        static void NoInherit(Socket s)
        {
            try
            {
                SetHandleInformation(s.Handle, 1 /* HANDLE_FLAG_INHERIT */, 0);
            }
            catch { }
        }

        static int FreePortNear(int socksPort, int gostPort)
        {
            for (int p = socksPort + 1; p < socksPort + 40; p++)
                if (p != gostPort && !IsPortOpen(p))
                    return p;
            return socksPort + 41;
        }

        async Task StartHelperAsync(Configuration cfg, CancellationToken ct)
        {
            var order = new List<Func<ProxyBackend>>();
            if (cfg.WarpTosAccepted && WarpBackend.HasAccount(dataDir))
                order.Add(() => new WarpBackend(logger, dataDir));
            order.Add(() => new TorBackend(logger, dataDir));

            foreach (Func<ProxyBackend> make in order)
            {
                if (ct.IsCancellationRequested)
                    break;
                ProxyBackend b = make();
                try
                {
                    await b.StartAsync(helperPort, cfg, ct);
                    if (await HelperWorks(helperPort, ct))
                    {
                        helper = b;
                        logger.Log($"Direct: TLS 1.2 connections (Discord updater) go through {b.Name} on port {helperPort}");
                        helperReady.TrySetResult(true);
                        return;
                    }
                    logger.Log($"Direct: helper {b.Name} started but can't reach Discord", LogType.WARNING);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    logger.Log($"Direct: helper {b.Name} failed: {ex.Message}", LogType.WARNING);
                }
                catch (OperationCanceledException)
                {
                }
                b.Stop();
            }
            if (!ct.IsCancellationRequested)
                logger.Log("Direct: no helper route; Discord's updater may stay on \"Checking for updates\". Use WARP or Tor instead.", LogType.ERROR);
            helperReady.TrySetResult(false);
        }

        // A helper is usable when a TLS 1.3 handshake with updates.discord.com completes through it.
        static async Task<bool> HelperWorks(int port, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 6 && !ct.IsCancellationRequested; attempt++)
            {
                try
                {
                    using (var c = new TcpClient { NoDelay = true })
                    {
                        NetworkStream s = await SocksConnect(c, port, "updates.discord.com", 443);
                        if (s != null && await TlsProbe.HandshakeAsync(s, "updates.discord.com", TimeSpan.FromSeconds(15), ct))
                            return true;
                    }
                }
                catch { }
                await Task.Delay(3000, ct);
            }
            return false;
        }

        // Opens a SOCKS5 CONNECT through a local SOCKS server; null if refused.
        static async Task<NetworkStream> SocksConnect(TcpClient c, int socksPort, string host, int port)
        {
            await c.ConnectAsync(IPAddress.Loopback, socksPort);
            NetworkStream s = c.GetStream();
            await s.WriteAsync(new byte[] { 5, 1, 0 }, 0, 3);
            byte[] method = await ReadExact(s, 2);
            if (method[1] != 0)
                return null;
            byte[] name = Encoding.ASCII.GetBytes(host);
            byte[] req = new byte[] { 5, 1, 0, 3, (byte)name.Length }
                .Concat(name)
                .Concat(new[] { (byte)(port >> 8), (byte)port })
                .ToArray();
            await s.WriteAsync(req, 0, req.Length);
            byte[] reply = await ReadExact(s, 4);
            if (reply[1] != 0)
                return null;
            int skip = reply[3] == 1 ? 4 : reply[3] == 4 ? 16 : (await ReadExact(s, 1))[0];
            await ReadExact(s, skip + 2);
            return s;
        }

        public override void Stop()
        {
            TcpListener l = listener;
            listener = null;
            if (l == null)
                return;
            // Close the listening socket first: cancelling can throw from registered callbacks
            // while Discord connections are open, which used to leave the port taken (DP-101).
            try
            {
                l.Stop();
            }
            catch { }
            try
            {
                cts.Cancel();
            }
            catch { }
            helper?.Stop();
            helper = null;
            helperReady?.TrySetResult(false);
            foreach (TcpClient c in open.Keys.ToList())
            {
                try
                {
                    c.Close();
                }
                catch { }
            }
            open.Clear();
            logger.Log("Direct stopped");
        }

        async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    TcpListener l = listener;
                    if (l == null)
                        return;
                    client = await l.AcceptTcpClientAsync();
                }
                catch
                {
                    return; // listener stopped
                }
                var _ = Task.Run(() => Serve(client, ct));
            }
        }

        void Report(string key, string message, LogType type = LogType.WARNING)
        {
            lock (reported)
            {
                if (!reported.Add(key))
                    return;
            }
            logger.Log("Direct: " + message, type);
        }

        async Task Serve(TcpClient client, CancellationToken ct)
        {
            open[client] = 0;
            TcpClient remote = null;
            string host = null;
            try
            {
                client.NoDelay = true;
                NoInherit(client.Client);
                NetworkStream local = client.GetStream();

                // SOCKS5 greeting: no authentication.
                byte[] head = await ReadExact(local, 2);
                await ReadExact(local, head[1]);
                await local.WriteAsync(new byte[] { 5, 0 }, 0, 2);

                // CONNECT request.
                byte[] req = await ReadExact(local, 4);
                if (req[1] != 1)
                {
                    await Reply(local, 7); // command not supported
                    return;
                }
                IPAddress literal = null;
                switch (req[3])
                {
                    case 1:
                        literal = new IPAddress(await ReadExact(local, 4));
                        break;
                    case 4:
                        literal = new IPAddress(await ReadExact(local, 16));
                        break;
                    case 3:
                        byte[] len = await ReadExact(local, 1);
                        host = Encoding.ASCII.GetString(await ReadExact(local, len[0]));
                        break;
                    default:
                        await Reply(local, 8);
                        return;
                }
                byte[] portBytes = await ReadExact(local, 2);
                int port = (portBytes[0] << 8) | portBytes[1];
                string target = host ?? literal.ToString();

                NetworkStream up;
                if (port == 443)
                {
                    // Reply first so the client sends its ClientHello; its TLS version decides the route.
                    await Reply(local, 0);
                    byte[] hello = await ReadRecord(local, ct);
                    if (!OffersTls13(hello))
                    {
                        if (!await helperReady.Task)
                        {
                            Report("nohelper:" + target, $"{target}: TLS 1.2 client and no helper route");
                            return;
                        }
                        Report("helper:" + target, $"{target}: TLS 1.2 client, sent through {helper?.Name}", LogType.NORMAL);
                        remote = new TcpClient { NoDelay = true };
                        NoInherit(remote.Client);
                        open[remote] = 0;
                        up = await SocksConnect(remote, helperPort, target, port);
                        if (up == null)
                            return;
                        await up.WriteAsync(hello, 0, hello.Length, ct);
                    }
                    else
                    {
                        remote = await ConnectDirect(host, literal, port, ct);
                        if (remote == null)
                            return;
                        open[remote] = 0;
                        up = remote.GetStream();
                        byte[] split = SplitInsideName(hello, host);
                        await up.WriteAsync(split, 0, split.Length, ct);
                    }
                }
                else
                {
                    remote = await ConnectDirect(host, literal, port, ct);
                    if (remote == null)
                    {
                        await Reply(local, 5);
                        return;
                    }
                    open[remote] = 0;
                    await Reply(local, 0);
                    up = remote.GetStream();
                }

                Task a = Pump(local, up, ct);
                Task b = Pump(up, local, ct);
                await Task.WhenAny(a, b);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is ObjectDisposedException))
            {
                if (host != null)
                    Report("err:" + host + ex.GetType().Name, $"{host}: {ex.Message}");
            }
            catch
            {
            }
            finally
            {
                if (remote != null)
                {
                    byte unused;
                    open.TryRemove(remote, out unused);
                    remote.Close();
                }
                byte gone;
                open.TryRemove(client, out gone);
                client.Close();
            }
        }

        async Task<TcpClient> ConnectDirect(string host, IPAddress literal, int port, CancellationToken ct)
        {
            IPAddress[] targets = literal != null ? new[] { literal } : await Doh.Resolve(host);
            if (targets.Length == 0)
            {
                if (host != null && host.EndsWith("discord.com"))
                    DnsFailed = true;
                Report("dns:" + host, $"could not resolve {host} over DoH");
                return null;
            }
            Report("route:" + (host ?? targets[0].ToString()), $"{host ?? "(address)"}:{port} -> {targets[0]}", LogType.NORMAL);
            var remote = new TcpClient(targets[0].AddressFamily) { NoDelay = true };
            NoInherit(remote.Client);
            Task connect = remote.ConnectAsync(targets, port);
            if (await Task.WhenAny(connect, Task.Delay(8000, ct)) != connect || connect.IsFaulted)
            {
                Report("conn:" + host + port, $"could not connect to {host ?? targets[0].ToString()}:{port}");
                remote.Close();
                return null;
            }
            return remote;
        }

        // True when the ClientHello's supported_versions extension offers TLS 1.3.
        static bool OffersTls13(byte[] record)
        {
            try
            {
                if (record.Length < 48 || record[0] != 0x16 || record[5] != 0x01)
                    return false;
                int p = 5 + 4 + 2 + 32; // record header, handshake header, version, random
                p += 1 + record[p]; // session id
                p += 2 + ((record[p] << 8) | record[p + 1]); // cipher suites
                p += 1 + record[p]; // compression methods
                int end = p + 2 + ((record[p] << 8) | record[p + 1]);
                p += 2;
                while (p + 4 <= end && p + 4 <= record.Length)
                {
                    int type = (record[p] << 8) | record[p + 1];
                    int len = (record[p + 2] << 8) | record[p + 3];
                    if (type == 0x002b)
                    {
                        for (int i = p + 5; i + 1 < p + 4 + len; i += 2)
                            if (record[i] == 3 && record[i + 1] == 4)
                                return true;
                        return false;
                    }
                    p += 4 + len;
                }
            }
            catch { }
            return false;
        }

        static async Task<byte[]> ReadRecord(NetworkStream s, CancellationToken ct)
        {
            byte[] header = await ReadExact(s, 5);
            byte[] payload = await ReadExact(s, (header[3] << 8) | header[4]);
            return header.Concat(payload).ToArray();
        }

        // Re-frames one TLS record as two records split inside the host name, which the SNI
        // filter does not reassemble. Returns the record unchanged when the name isn't found.
        static byte[] SplitInsideName(byte[] record, string host)
        {
            byte[] payload = record.Skip(5).ToArray();
            int at = -1;
            if (record[0] == 0x16 && host != null)
            {
                int sni = IndexOf(payload, Encoding.ASCII.GetBytes(host));
                if (sni > 0)
                    at = sni + 2;
            }
            if (at <= 0 || at >= payload.Length)
                return record;
            byte[] split = new byte[record.Length + 5];
            Buffer.BlockCopy(record, 0, split, 0, 3);
            split[3] = (byte)(at >> 8);
            split[4] = (byte)at;
            Buffer.BlockCopy(payload, 0, split, 5, at);
            int rest = payload.Length - at;
            Buffer.BlockCopy(record, 0, split, 5 + at, 3);
            split[5 + at + 3] = (byte)(rest >> 8);
            split[5 + at + 4] = (byte)rest;
            Buffer.BlockCopy(payload, at, split, 10 + at, rest);
            return split;
        }

        static int IndexOf(byte[] data, byte[] needle)
        {
            for (int i = 0; i <= data.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && data[i + j] == needle[j])
                    j++;
                if (j == needle.Length)
                    return i;
            }
            return -1;
        }

        static async Task Pump(Stream from, Stream to, CancellationToken ct)
        {
            byte[] buf = new byte[32768];
            try
            {
                int n;
                while ((n = await from.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                    await to.WriteAsync(buf, 0, n, ct);
            }
            catch { }
        }

        static Task Reply(NetworkStream s, byte code)
        {
            byte[] r = { 5, code, 0, 1, 0, 0, 0, 0, 0, 0 };
            return s.WriteAsync(r, 0, r.Length);
        }

        static async Task<byte[]> ReadExact(Stream s, int count)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await s.ReadAsync(buf, read, count - read);
                if (n == 0)
                    throw new EndOfStreamException();
                read += n;
            }
            return buf;
        }
    }

    // Probe for the Direct route. .NET Framework on Windows 10 only speaks TLS 1.2 (SChannel),
    // and in TLS 1.2 the server certificate travels in clear text: the ISP filter reads
    // "discord.com" in it and cuts the reply. Discord itself (Chromium) uses TLS 1.3, where the
    // certificate is encrypted, so the route is judged by a TLS 1.3 handshake instead:
    // CONNECT through gost, send a TLS 1.3 ClientHello, and require the ServerHello plus at
    // least one encrypted record behind it.
    public static class TlsProbe
    {
        static readonly Random rng = new Random();

        static byte[] Rand(int n)
        {
            var b = new byte[n];
            lock (rng)
                rng.NextBytes(b);
            return b;
        }

        static byte[] U16(int v) => new[] { (byte)(v >> 8), (byte)v };

        static byte[] Ext(int type, byte[] body) => U16(type).Concat(U16(body.Length)).Concat(body).ToArray();

        public static byte[] ClientHello(string host)
        {
            byte[] name = Encoding.ASCII.GetBytes(host);
            byte[] sni = U16(name.Length + 3).Concat(new byte[] { 0 }).Concat(U16(name.Length)).Concat(name).ToArray();
            byte[] exts = new byte[0]
                .Concat(Ext(0x0000, sni))
                .Concat(Ext(0x000a, new byte[] { 0, 4, 0, 0x1d, 0, 0x17 }))                       // x25519, secp256r1
                .Concat(Ext(0x000d, new byte[] { 0, 8, 4, 3, 8, 4, 4, 1, 5, 3 }))                   // signature algorithms
                .Concat(Ext(0x0010, new byte[] { 0, 9, 8, 0x68, 0x74, 0x74, 0x70, 0x2f, 0x31, 0x2e, 0x31 })) // ALPN http/1.1
                .Concat(Ext(0x002b, new byte[] { 2, 3, 4 }))                                         // TLS 1.3 only
                .Concat(Ext(0x002d, new byte[] { 1, 1 }))                                            // psk_dhe_ke
                .Concat(Ext(0x0033, new byte[] { 0, 0x24, 0, 0x1d, 0, 0x20 }.Concat(Rand(32)).ToArray()))
                .ToArray();
            byte[] body = new byte[] { 3, 3 }
                .Concat(Rand(32))
                .Concat(new byte[] { 32 }).Concat(Rand(32))
                .Concat(new byte[] { 0, 6, 0x13, 0x01, 0x13, 0x02, 0x13, 0x03 })
                .Concat(new byte[] { 1, 0 })
                .Concat(U16(exts.Length)).Concat(exts)
                .ToArray();
            byte[] hs = new byte[] { 1, (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length }.Concat(body).ToArray();
            return new byte[] { 0x16, 3, 1 }.Concat(U16(hs.Length)).Concat(hs).ToArray();
        }

        // TLS 1.3 handshake on an already open tunnel: ServerHello plus one encrypted record.
        public static async Task<bool> HandshakeAsync(NetworkStream s, string host, TimeSpan timeout, CancellationToken ct)
        {
            using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                t.CancelAfter(timeout);
                using (t.Token.Register(() => s.Close()))
                {
                    try
                    {
                        byte[] hello = ClientHello(host);
                        await s.WriteAsync(hello, 0, hello.Length, t.Token);
                        byte[] rec = await ReadRecord(s, t.Token);
                        if (rec[0] != 0x16 || rec[5] != 0x02)
                            return false;
                        byte[] next = await ReadRecord(s, t.Token);
                        if (next[0] == 0x14)
                            next = await ReadRecord(s, t.Token);
                        return next[0] == 0x17;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        public static async Task<ProbeResult> ProbeAsync(int httpPort, string host, TimeSpan timeout, CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string label = $"tls13://{host}";
            using (var c = new TcpClient { NoDelay = true })
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(timeout);
                CancellationToken t = timeoutCts.Token;
                using (t.Register(() => c.Close()))
                {
                    try
                    {
                        await c.ConnectAsync(IPAddress.Loopback, httpPort);
                        NetworkStream s = c.GetStream();
                        byte[] connect = Encoding.ASCII.GetBytes($"CONNECT {host}:443 HTTP/1.1\r\nHost: {host}:443\r\n\r\n");
                        await s.WriteAsync(connect, 0, connect.Length, t);
                        string head = "";
                        var one = new byte[1];
                        while (!head.EndsWith("\r\n\r\n"))
                        {
                            if (await s.ReadAsync(one, 0, 1, t) == 0)
                                throw new EndOfStreamException("proxy closed");
                            head += (char)one[0];
                        }
                        int status = int.Parse(head.Split(' ')[1]);
                        if (status != 200)
                            return new ProbeResult { Status = status, Ms = sw.ElapsedMilliseconds, Detail = $"{label} -> CONNECT {status}" };

                        byte[] hello = ClientHello(host);
                        await s.WriteAsync(hello, 0, hello.Length, t);
                        byte[] rec = await ReadRecord(s, t);
                        if (rec[0] != 0x16 || rec[5] != 0x02)
                            return Fail(label, sw, $"unexpected reply type {rec[0]:x2}");
                        long ms = sw.ElapsedMilliseconds;
                        // The encrypted remainder of the server's flight must arrive too.
                        byte[] next = await ReadRecord(s, t);
                        if (next[0] == 0x14)
                            next = await ReadRecord(s, t);
                        if (next[0] != 0x17)
                            return Fail(label, sw, $"no encrypted records (type {next[0]:x2})");
                        return new ProbeResult { Ok = true, Status = 200, Ms = ms, Detail = $"{label} -> TLS 1.3 handshake in {ms} ms" };
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        return Fail(label, sw, ex is ObjectDisposedException || t.IsCancellationRequested ? "timeout" : ex.Message);
                    }
                }
            }
        }

        static ProbeResult Fail(string label, System.Diagnostics.Stopwatch sw, string why)
        {
            return new ProbeResult { Ok = false, Status = 0, Ms = sw.ElapsedMilliseconds, Detail = $"{label} -> failed after {sw.ElapsedMilliseconds} ms: {why}" };
        }

        static async Task<byte[]> ReadRecord(NetworkStream s, CancellationToken t)
        {
            byte[] header = await ReadExactly(s, 5, t);
            byte[] body = await ReadExactly(s, (header[3] << 8) | header[4], t);
            return header.Concat(body).ToArray();
        }

        static async Task<byte[]> ReadExactly(NetworkStream s, int count, CancellationToken t)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await s.ReadAsync(buf, read, count - read, t);
                if (n == 0)
                    throw new EndOfStreamException("connection closed");
                read += n;
            }
            return buf;
        }
    }

    // DNS over HTTPS with a small cache. The ISP's resolver answers Discord names with a block
    // page address, so the system resolver can't be used for this route.
    public static class Doh
    {
        static readonly string[] Endpoints =
        {
            "https://cloudflare-dns.com/dns-query?name={0}&type=A",
            "https://dns.google/resolve?name={0}&type=A",
            "https://1.1.1.1/dns-query?name={0}&type=A",
            "https://8.8.8.8/resolve?name={0}&type=A"
        };

        static readonly ConcurrentDictionary<string, Tuple<IPAddress[], DateTime>> cache =
            new ConcurrentDictionary<string, Tuple<IPAddress[], DateTime>>(StringComparer.OrdinalIgnoreCase);

        static readonly HttpClient http = CreateClient();

        static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            c.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
            return c;
        }

        public static async Task<IPAddress[]> Resolve(string host)
        {
            IPAddress literal;
            if (IPAddress.TryParse(host, out literal))
                return new[] { literal };
            Tuple<IPAddress[], DateTime> hit;
            if (cache.TryGetValue(host, out hit) && hit.Item2 > DateTime.UtcNow)
                return hit.Item1;

            foreach (string url in Endpoints)
            {
                try
                {
                    JObject json = JObject.Parse(await http.GetStringAsync(string.Format(url, Uri.EscapeDataString(host))));
                    var answers = (json["Answer"] as JArray)?.Where(a => (int)a["type"] == 1).ToList();
                    if (answers == null || answers.Count == 0)
                        continue;
                    IPAddress[] ips = answers.Select(a => IPAddress.Parse((string)a["data"])).ToArray();
                    int ttl = Math.Max(60, Math.Min(3600, answers.Min(a => (int)a["TTL"])));
                    cache[host] = Tuple.Create(ips, DateTime.UtcNow.AddSeconds(ttl));
                    return ips;
                }
                catch { }
            }
            return hit != null ? hit.Item1 : new IPAddress[0]; // stale answer beats none
        }
    }
}
