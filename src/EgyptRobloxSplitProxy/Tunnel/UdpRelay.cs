using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EgyptRobloxSplitProxy.Logging;
using EgyptRobloxSplitProxy.Models;

namespace EgyptRobloxSplitProxy.Tunnel;

/// <summary>
/// Lightweight UDP relay. Listens on a local port, obfuscates each datagram and
/// forwards to the configured relay endpoint. Reverse path de-obfuscates.
/// MTU is clamped to 1280.
/// </summary>
public sealed class UdpRelay : IDisposable
{
    private readonly AppConfig _cfg;
    private UdpClient? _local;
    private UdpClient? _remote;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public bool IsRunning => _local is not null;

    public UdpRelay(AppConfig cfg) => _cfg = cfg;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _local = new UdpClient(_cfg.UdpRelayPort);
        if (!string.IsNullOrWhiteSpace(_cfg.RelayEndpoint))
            _remote = new UdpClient();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        Logger.Info($"UDP relay listening on 0.0.0.0:{_cfg.UdpRelayPort}");
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _local?.Close(); } catch { }
        try { _remote?.Close(); } catch { }
        _local = null; _remote = null;
        Logger.Info("UDP relay stopped.");
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        if (_remote is null)
        {
            // No relay endpoint configured: just echo/clamp locally (no-op pass-through placeholder).
            Logger.Warn("No relay endpoint configured; UDP relay in pass-through mode.");
            return;
        }

        var endpoint = ParseEndpoint(_cfg.RelayEndpoint);
        while (!ct.IsCancellationRequested && _local is not null)
        {
            try
            {
                var result = await _local.ReceiveAsync(ct);
                foreach (var chunk in MtuClamper.Split(result.Buffer, _cfg.Mtu))
                {
                    var frame = PacketObfuscator.Frame(chunk, _cfg.TunnelKey);
                    await _remote.SendAsync(frame, frame.Length, endpoint);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Logger.Warn("UDP relay recv error: " + ex.Message); }
        }
    }

    private static IPEndPoint ParseEndpoint(string ep)
    {
        var parts = ep.Split(':');
        var host = parts[0];
        int port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 51820;
        var addr = Dns.GetHostAddresses(host)[0];
        return new IPEndPoint(addr, port);
    }

    public void Dispose() => Stop();
}