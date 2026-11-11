using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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

        // Set when name resolution over DoH failed for the probe host (DP-602 vs DP-601).
        public bool DnsFailed { get; private set; }

        public override bool IsAlive => listener != null && cts != null && !cts.IsCancellationRequested;

        public override Task StartAsync(int socksPort, Configuration cfg, CancellationToken ct)
        {
            EnsurePortFree(socksPort);
            Progress = L.T("progress.direct");
            cts = new CancellationTokenSource();
            listener = new TcpListener(IPAddress.Loopback, socksPort);
            listener.Start(256);
            logger.Log($"Direct: listening on 127.0.0.1:{socksPort} (DoH + TLS record split)");
            Task.Run(() => AcceptLoop(cts.Token));
            return Task.FromResult(true);
        }

        public override void Stop()
        {
            if (listener == null)
                return;
            try
            {
                cts.Cancel();
                listener.Stop();
            }
            catch { }
            listener = null;
            logger.Log("Direct stopped");
        }

        async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch
                {
                    return; // listener stopped
                }
                var _ = Task.Run(() => Serve(client, ct));
            }
        }

        void Report(string key, string message)
        {
            lock (reported)
            {
                if (!reported.Add(key))
                    return;
            }
            logger.Log("Direct: " + message, LogType.WARNING);
        }

        async Task Serve(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                client.NoDelay = true;
                NetworkStream local = client.GetStream();
                string host = null;
                try
                {
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
                    IPAddress[] targets;
                    switch (req[3])
                    {
                        case 1:
                            targets = new[] { new IPAddress(await ReadExact(local, 4)) };
                            break;
                        case 4:
                            targets = new[] { new IPAddress(await ReadExact(local, 16)) };
                            break;
                        case 3:
                            byte[] len = await ReadExact(local, 1);
                            host = Encoding.ASCII.GetString(await ReadExact(local, len[0]));
                            targets = null;
                            break;
                        default:
                            await Reply(local, 8);
                            return;
                    }
                    byte[] portBytes = await ReadExact(local, 2);
                    int port = (portBytes[0] << 8) | portBytes[1];

                    if (targets == null)
                    {
                        targets = await Doh.Resolve(host);
                        if (targets.Length == 0)
                        {
                            if (host.EndsWith("discord.com"))
                                DnsFailed = true;
                            Report("dns:" + host, $"could not resolve {host} over DoH");
                            await Reply(local, 4); // host unreachable
                            return;
                        }
                    }

                    lock (reported)
                    {
                        if (reported.Add("route:" + (host ?? targets[0].ToString())))
                            logger.Log($"Direct: {(host != null ? host : "(address)")}:{port} -> {targets[0]}");
                    }

                    var remote = new TcpClient(targets[0].AddressFamily) { NoDelay = true };
                    using (remote)
                    {
                        Task connect = remote.ConnectAsync(targets, port);
                        if (await Task.WhenAny(connect, Task.Delay(8000, ct)) != connect || connect.IsFaulted)
                        {
                            Report("conn:" + host + port, $"could not connect to {host ?? targets[0].ToString()}:{port}");
                            await Reply(local, 5);
                            return;
                        }
                        await Reply(local, 0);
                        NetworkStream up = remote.GetStream();

                        if (port == 443)
                            await ForwardFirstRecord(local, up, host);

                        Task a = Pump(local, up, ct);
                        Task b = Pump(up, local, ct);
                        await Task.WhenAny(a, b);
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    if (host != null)
                        Report("err:" + host + ex.GetType().Name, $"{host}: {ex.Message}");
                }
            }
        }

        // Reads the client's first TLS record (the ClientHello) and sends it to the server as
        // two records split inside the host name, which the SNI filter does not reassemble.
        static async Task ForwardFirstRecord(NetworkStream client, NetworkStream server, string host)
        {
            byte[] header = await ReadExact(client, 5);
            int length = (header[3] << 8) | header[4];
            byte[] payload = await ReadExact(client, length);
            byte[] record = header.Concat(payload).ToArray();

            int at = -1;
            if (header[0] == 0x16 && host != null)
            {
                int sni = IndexOf(payload, Encoding.ASCII.GetBytes(host));
                if (sni > 0)
                    at = sni + 2; // cut inside the name
            }
            if (at <= 0 || at >= payload.Length)
            {
                await server.WriteAsync(record, 0, record.Length);
                return;
            }
            byte[] split = new byte[record.Length + 5];
            Buffer.BlockCopy(header, 0, split, 0, 3);
            split[3] = (byte)(at >> 8);
            split[4] = (byte)at;
            Buffer.BlockCopy(payload, 0, split, 5, at);
            int rest = payload.Length - at;
            Buffer.BlockCopy(header, 0, split, 5 + at, 3);
            split[5 + at + 3] = (byte)(rest >> 8);
            split[5 + at + 4] = (byte)rest;
            Buffer.BlockCopy(payload, at, split, 10 + at, rest);
            await server.WriteAsync(split, 0, split.Length);
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
