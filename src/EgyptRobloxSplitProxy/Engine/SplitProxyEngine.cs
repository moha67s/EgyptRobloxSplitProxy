using System;
using EgyptRobloxSplitProxy.Logging;
using EgyptRobloxSplitProxy.Models;
using EgyptRobloxSplitProxy.Network;
using EgyptRobloxSplitProxy.Proxy;
using EgyptRobloxSplitProxy.Tunnel;

namespace EgyptRobloxSplitProxy.Engine;

/// <summary>
/// Orchestrates Layer A (HTTPS/SNI proxy) and Layer B (UDP relay), wiring them
/// to Roblox process lifecycle events.
/// </summary>
public sealed class SplitProxyEngine : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly DoHResolver _doh;
    private readonly HttpsSniProxy _httpsProxy;
    private readonly UdpRelay _udpRelay;
    private readonly ProcessMonitor _processMonitor;
    private readonly WinDivertSession _divert;

    public bool IsRunning { get; private set; }
    public bool WinDivertAvailable => WinDivertInterop.IsAvailable();

    public event Action<string>? StatusChanged;

    public SplitProxyEngine()
    {
        _cfg = AppConfig.Load();
        _doh = new DoHResolver(_cfg.DohProvider);
        _httpsProxy = new HttpsSniProxy(_cfg, _doh);
        _udpRelay = new UdpRelay(_cfg);
        _processMonitor = new ProcessMonitor();
        _divert = new WinDivertSession();

        _processMonitor.RobloxStarted += () =>
        {
            if (_cfg.AutoHookRoblox && IsRunning)
            {
                Logger.Info("Roblox detected -> activating transparent redirect.");
                if (WinDivertInterop.IsAvailable() && !_divert.IsRunning)
                {
                    var filter = "tcp.DstPort == 443 and ip.DstAddr != 127.0.0.1";
                    if (_divert.Start(filter))
                        Logger.Info("WinDivert session started for Roblox.");
                    else
                        Logger.Warn("WinDivert session failed to start.");
                }
                StatusChanged?.Invoke("Roblox detected - tunnel active");
            }
        };
        _processMonitor.RobloxStopped += () =>
        {
            Logger.Info("Roblox exited -> deactivating transparent redirect.");
            _divert.Stop();
            StatusChanged?.Invoke("Roblox exited - idle");
        };
    }

    public void Start()
    {
        if (IsRunning) return;
        try
        {
            FirewallManager.AllowPort(_cfg.HttpsProxyPort, "EgyptRobloxSplitProxy-HTTPS");
            FirewallManager.AllowPort(_cfg.UdpRelayPort, "EgyptRobloxSplitProxy-UDP");

            _httpsProxy.Start();
            _udpRelay.Start();
            _processMonitor.Start();

            if (WinDivertInterop.IsAvailable())
            {
                Logger.Info("WinDivert available - transparent mode enabled (activated when Roblox runs).");
            }
            else
            {
                Logger.Warn("WinDivert not available; running in proxy-only mode. Configure system proxy manually.");
            }

            IsRunning = true;
            StatusChanged?.Invoke("Running");
            Logger.Info("Split proxy engine started.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to start engine: " + ex);
            StatusChanged?.Invoke("Error: " + ex.Message);
            throw;
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try
        {
            _divert.Stop();
            _processMonitor.Stop();
            _httpsProxy.Stop();
            _udpRelay.Stop();
            IsRunning = false;
            StatusChanged?.Invoke("Stopped");
            Logger.Info("Split proxy engine stopped.");
        }
        catch (Exception ex)
        {
            Logger.Error("Error stopping engine: " + ex);
        }
    }

    public AppConfig Config => _cfg;

    public void Dispose()
    {
        Stop();
        _doh.Dispose();
        _httpsProxy.Dispose();
        _udpRelay.Dispose();
        _processMonitor.Dispose();
        _divert.Dispose();
    }
}