using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EgyptRobloxSplitProxy.Logging;
using EgyptRobloxSplitProxy.Models;

namespace EgyptRobloxSplitProxy.Proxy;

/// <summary>
/// Local HTTPS/SNI-aware TCP proxy. Listens on 127.0.0.1:port. For each
/// connection it peeks the first bytes; if the TLS ClientHello carries an SNI
/// for a Roblox domain, the upstream write is fragmented into tiny segments.
/// </summary>
public sealed class HttpsSniProxy : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly DoHResolver _doh;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public bool IsRunning => _listener is not null;

    public HttpsSniProxy(AppConfig cfg, DoHResolver doh)
    {
        _cfg = cfg;
        _doh = doh;
    }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Parse(_cfg.ListenAddress), _cfg.HttpsProxyPort);
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        Logger.Info($"HTTPS SNI proxy listening on {_cfg.ListenAddress}:{_cfg.HttpsProxyPort}");
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        Logger.Info("HTTPS SNI proxy stopped.");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Logger.Warn("Accept error: " + ex.Message); try { client?.Dispose(); } catch { } }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        TcpClient? upstream = null;
        try
        {
            client.NoDelay = true;
            var clientStream = client.GetStream();

            // Peek first chunk to inspect SNI without consuming.
            var peekBuf = new byte[4096];
            // TcpClient doesn't support peek easily; read first chunk.
            int firstRead = await clientStream.ReadAsync(peekBuf.AsMemory(0, peekBuf.Length), ct);
            if (firstRead <= 0) { client.Dispose(); return; }

            var firstChunk = peekBuf.AsSpan(0, firstRead).ToArray();
            string? host = null;
            if (SniFragmenter.IsClientHello(firstChunk))
                host = SniFragmenter.ExtractSni(firstChunk);

            bool fragment = SniFragmenter.IsRobloxDomain(host, _cfg.RobloxDomains);

            // Determine upstream target: if CONNECT-style not used, assume transparent
            // forwarding to the original destination. For a forward proxy we expect
            // the client to send CONNECT host:port. Handle CONNECT below.
            // Here we implement a minimal HTTP CONNECT handler.
            // Re-buffer firstChunk for parsing.
            using var ms = new MemoryStream();
            ms.Write(firstChunk, 0, firstChunk.Length);

            // Read until we have full HTTP headers (for CONNECT).
            var buffer = ms.ToArray();
            // Simple: search for \r\n\r\n
            while (Array.IndexOf(buffer, (byte)'\n') < 0 || !ContainsDoubleCrlf(buffer))
            {
                var tmp = new byte[4096];
                int r = await clientStream.ReadAsync(tmp.AsMemory(), ct);
                if (r <= 0) break;
                var nb = new byte[buffer.Length + r];
                Buffer.BlockCopy(buffer, 0, nb, 0, buffer.Length);
                Buffer.BlockCopy(tmp, 0, nb, buffer.Length, r);
                buffer = nb;
                if (buffer.Length > 65536) break;
            }

            // Parse CONNECT
            var text = System.Text.Encoding.ASCII.GetString(buffer);
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string? target = null;
            if (lines.Length > 0 && lines[0].StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = lines[0].Split(' ');
                if (parts.Length >= 2) target = parts[1];
            }

            // Find end of headers
            int headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            int consumed = headerEnd >= 0 ? headerEnd + 4 : buffer.Length;
            var leftover = (headerEnd >= 0 && consumed < buffer.Length)
                ? buffer.AsSpan(consumed).ToArray() : Array.Empty<byte>();

            if (target is null)
            {
                // Not a CONNECT request; just close.
                client.Dispose();
                return;
            }

            var hostPort = target.Split(':');
            var upHost = hostPort[0];
            int upPort = hostPort.Length > 1 && int.TryParse(hostPort[1], out var p) ? p : 443;

            // Resolve via DoH if Roblox domain.
            string connectHost = upHost;
            if (SniFragmenter.IsRobloxDomain(upHost, _cfg.RobloxDomains))
            {
                var ips = await _doh.ResolveAsync(upHost);
                if (ips.Length > 0) connectHost = ips[0];
            }

            upstream = new TcpClient();
            await upstream.ConnectAsync(connectHost, upPort, ct);
            upstream.NoDelay = true;
            var upStream = upstream.GetStream();

            // Send 200 Connection Established
            var ok = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            await clientStream.WriteAsync(ok, ct);

            // If we have leftover bytes (e.g., ClientHello already sent by client), forward.
            // Pump both directions, fragmenting upstream writes for Roblox.
            var upToDown = PumpAsync(upStream, clientStream, ct, fragmentUpstream: false);
            var downToUp = PumpClientToUpstreamAsync(clientStream, upStream, ct, fragment, leftover);
            await Task.WhenAny(upToDown, downToUp);
        }
        catch (Exception ex)
        {
            Logger.Warn("Proxy client error: " + ex.Message);
        }
        finally
        {
            try { client.Dispose(); } catch { }
            try { upstream?.Dispose(); } catch { }
        }
    }

    private static bool ContainsDoubleCrlf(byte[] b)
    {
        for (int i = 0; i + 3 < b.Length; i++)
            if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n') return true;
        return false;
    }

    private static async Task PumpAsync(Stream src, Stream dst, CancellationToken ct, bool fragmentUpstream)
    {
        var buf = new byte[65536];
        try
        {
            while (true)
            {
                int r = await src.ReadAsync(buf.AsMemory(), ct);
                if (r <= 0) break;
                if (fragmentUpstream)
                {
                    foreach (var c in SniFragmenter.Fragment(buf.AsSpan(0, r).ToArray(), 20))
                        await dst.WriteAsync(c, ct);
                    await dst.FlushAsync(ct);
                }
                else
                {
                    await dst.WriteAsync(buf.AsMemory(0, r), ct);
                }
            }
        }
        catch { /* connection closed */ }
    }

    private static async Task PumpClientToUpstreamAsync(Stream src, Stream dst, CancellationToken ct, bool fragment, byte[] initial)
    {
        try
        {
            if (initial.Length > 0)
            {
                if (fragment && SniFragmenter.IsClientHello(initial))
                {
                    foreach (var c in SniFragmenter.Fragment(initial, 20))
                        await dst.WriteAsync(c, ct);
                }
                else
                {
                    await dst.WriteAsync(initial, ct);
                }
                await dst.FlushAsync(ct);
            }
            var buf = new byte[65536];
            while (true)
            {
                int r = await src.ReadAsync(buf.AsMemory(), ct);
                if (r <= 0) break;
                var chunk = buf.AsSpan(0, r).ToArray();
                if (fragment && SniFragmenter.IsClientHello(chunk))
                {
                    foreach (var c in SniFragmenter.Fragment(chunk, 20))
                        await dst.WriteAsync(c, ct);
                }
                else
                {
                    await dst.WriteAsync(chunk, ct);
                }
                await dst.FlushAsync(ct);
            }
        }
        catch { /* connection closed */ }
    }

    public void Dispose() => Stop();
}