using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordUnblocker
{
    // The HTTP proxy Discord talks to (127.0.0.1:GostPort). It replaces gost.exe, which did only
    // this and was flagged by many antivirus products (and Defender as PUA), blocking downloads.
    // Same behaviour as the old "gost -L=http://127.0.0.1:P -F=socks5://127.0.0.1:S":
    // - CONNECT host:port  -> tunnel through the SOCKS5 backend (HTTPS, WSS, updater)
    // - absolute-form HTTP -> forwarded through the backend (one request per connection)
    // - host names go to the backend unresolved, so Tor/WARP/Direct do the DNS
    // - 503 when the backend can't connect (the WARP fresh-account retry relies on it)
    // - loopback only
    public class LocalHttpProxy
    {
        readonly Logger logger;
        TcpListener listener;
        CancellationTokenSource cts;
        int socksPort;
        readonly ConcurrentDictionary<TcpClient, byte> open = new ConcurrentDictionary<TcpClient, byte>();

        public LocalHttpProxy(Logger logger)
        {
            this.logger = logger;
        }

        public string Name => "Local proxy";
        public bool IsAlive => listener != null;

        public Task StartAsync(int httpPort, int socksPort, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (ProxyProcess.IsPortOpen(httpPort))
                throw new ProxyFailure("DP-101", httpPort.ToString(), $"Port {httpPort} is already in use");
            this.socksPort = socksPort;
            cts = new CancellationTokenSource();
            var l = new TcpListener(IPAddress.Loopback, httpPort);
            try
            {
                l.Start(512);
            }
            catch (SocketException ex)
            {
                throw new ProxyFailure("DP-101", httpPort.ToString(), ex.Message);
            }
            Sockets.NoInherit(l.Server);
            listener = l;
            logger.Log($"Local proxy: listening on 127.0.0.1:{httpPort} -> SOCKS5 127.0.0.1:{socksPort}");
            CancellationToken token = cts.Token;
            Task.Run(() => AcceptLoop(token));
            return Task.FromResult(true);
        }

        public void Stop()
        {
            TcpListener l = listener;
            listener = null;
            if (l == null)
                return;
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
            foreach (TcpClient c in open.Keys.ToList())
            {
                try
                {
                    c.Close();
                }
                catch { }
            }
            open.Clear();
            logger.Log("Local proxy stopped");
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
                    return;
                }
                var _ = Task.Run(() => Serve(client, ct));
            }
        }

        static readonly byte[] HeaderEnd = { 13, 10, 13, 10 };

        async Task Serve(TcpClient client, CancellationToken ct)
        {
            open[client] = 0;
            TcpClient upstream = null;
            try
            {
                client.NoDelay = true;
                Sockets.NoInherit(client.Client);
                NetworkStream local = client.GetStream();

                // Read the request head; anything after it (request body, early TLS bytes) is kept.
                var buf = new MemoryStream();
                byte[] chunk = new byte[8192];
                int headEnd = -1;
                while (headEnd < 0)
                {
                    int n = await local.ReadAsync(chunk, 0, chunk.Length, ct);
                    if (n == 0)
                        return;
                    buf.Write(chunk, 0, n);
                    headEnd = IndexOf(buf.GetBuffer(), (int)buf.Length, HeaderEnd);
                    if (headEnd < 0 && buf.Length > 65536)
                        return; // not an HTTP proxy request
                }
                byte[] all = buf.ToArray();
                string head = Encoding.ASCII.GetString(all, 0, headEnd);
                byte[] rest = all.Skip(headEnd + 4).ToArray();
                string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] first = lines[0].Split(' ');
                if (first.Length < 3)
                {
                    await Respond(local, "400 Bad Request");
                    return;
                }
                string method = first[0], target = first[1], version = first[2];

                string host;
                int port;
                byte[] toUpstream;
                bool tunnel = method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase);
                if (tunnel)
                {
                    if (!SplitHostPort(target, 443, out host, out port))
                    {
                        await Respond(local, "400 Bad Request");
                        return;
                    }
                    toUpstream = rest;
                }
                else
                {
                    // Absolute-form request ("GET http://host/path HTTP/1.1"): send it on in origin
                    // form without the proxy headers, one request per connection.
                    Uri uri;
                    if (!Uri.TryCreate(target, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "ws"))
                    {
                        await Respond(local, "400 Bad Request");
                        return;
                    }
                    host = uri.Host;
                    port = uri.Port;
                    var sb = new StringBuilder();
                    sb.Append(method).Append(' ').Append(uri.PathAndQuery).Append(' ').Append(version).Append("\r\n");
                    foreach (string h in lines.Skip(1))
                    {
                        if (h.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) || h.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase))
                            continue;
                        sb.Append(h).Append("\r\n");
                    }
                    sb.Append("Connection: close\r\n\r\n");
                    toUpstream = Encoding.ASCII.GetBytes(sb.ToString()).Concat(rest).ToArray();
                }

                upstream = new TcpClient { NoDelay = true };
                Sockets.NoInherit(upstream.Client);
                open[upstream] = 0;
                Task<NetworkStream> connect = Sockets.Socks5ConnectAsync(upstream, socksPort, host, port);
                NetworkStream up = null;
                if (await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(30), ct)) == connect && !connect.IsFaulted)
                    up = connect.Result;
                if (up == null)
                {
                    await Respond(local, "503 Service Unavailable");
                    return;
                }
                if (tunnel)
                {
                    byte[] ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n");
                    await local.WriteAsync(ok, 0, ok.Length, ct);
                }
                if (toUpstream.Length > 0)
                    await up.WriteAsync(toUpstream, 0, toUpstream.Length, ct);

                Task a = Sockets.Pump(local, up, ct);
                Task b = Sockets.Pump(up, local, ct);
                await Task.WhenAny(a, b);
            }
            catch
            {
            }
            finally
            {
                if (upstream != null)
                {
                    byte gone;
                    open.TryRemove(upstream, out gone);
                    upstream.Close();
                }
                byte unused;
                open.TryRemove(client, out unused);
                client.Close();
            }
        }

        static async Task Respond(NetworkStream s, string status)
        {
            byte[] r = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            try
            {
                await s.WriteAsync(r, 0, r.Length);
            }
            catch { }
        }

        static bool SplitHostPort(string s, int defaultPort, out string host, out int port)
        {
            host = s;
            port = defaultPort;
            if (s.StartsWith("["))
            {
                int close = s.IndexOf(']');
                if (close < 0)
                    return false;
                host = s.Substring(1, close - 1);
                return close + 1 >= s.Length || (s[close + 1] == ':' && int.TryParse(s.Substring(close + 2), out port));
            }
            int colon = s.LastIndexOf(':');
            if (colon > 0)
            {
                host = s.Substring(0, colon);
                return int.TryParse(s.Substring(colon + 1), out port) && port > 0 && port < 65536;
            }
            return host.Length > 0;
        }

        static int IndexOf(byte[] data, int length, byte[] needle)
        {
            for (int i = 0; i <= length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && data[i + j] == needle[j])
                    j++;
                if (j == needle.Length)
                    return i;
            }
            return -1;
        }
    }

    // Socket helpers shared by the local HTTP proxy and the Direct backend.
    public static class Sockets
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        // .NET Framework sockets are inheritable; child processes (Discord, the WARP/Tor helper)
        // would keep a copy that holds the port after we close it (DP-101).
        public static void NoInherit(Socket s)
        {
            try
            {
                SetHandleInformation(s.Handle, 1 /* HANDLE_FLAG_INHERIT */, 0);
            }
            catch { }
        }

        // SOCKS5 CONNECT with the host name (remote DNS). Returns null when refused.
        public static async Task<NetworkStream> Socks5ConnectAsync(TcpClient c, int socksPort, string host, int port)
        {
            await c.ConnectAsync(IPAddress.Loopback, socksPort);
            NetworkStream s = c.GetStream();
            await s.WriteAsync(new byte[] { 5, 1, 0 }, 0, 3);
            byte[] method = await ReadExact(s, 2);
            if (method[1] != 0)
                return null;
            byte[] req;
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip))
            {
                byte[] addr = ip.GetAddressBytes();
                req = new byte[] { 5, 1, 0, (byte)(addr.Length == 4 ? 1 : 4) }.Concat(addr).ToArray();
            }
            else
            {
                byte[] name = Encoding.ASCII.GetBytes(host);
                req = new byte[] { 5, 1, 0, 3, (byte)name.Length }.Concat(name).ToArray();
            }
            req = req.Concat(new[] { (byte)(port >> 8), (byte)port }).ToArray();
            await s.WriteAsync(req, 0, req.Length);
            byte[] reply = await ReadExact(s, 4);
            if (reply[1] != 0)
                return null;
            int skip = reply[3] == 1 ? 4 : reply[3] == 4 ? 16 : (await ReadExact(s, 1))[0];
            await ReadExact(s, skip + 2);
            return s;
        }

        public static async Task Pump(Stream from, Stream to, CancellationToken ct)
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

        public static async Task<byte[]> ReadExact(Stream s, int count)
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
}
