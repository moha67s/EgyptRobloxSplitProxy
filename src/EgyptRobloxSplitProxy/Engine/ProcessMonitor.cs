using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using EgyptRobloxSplitProxy.Logging;

namespace EgyptRobloxSplitProxy.Engine;

/// <summary>
/// Watches for RobloxPlayerBeta.exe start/stop using WMI process events.
/// </summary>
public sealed class ProcessMonitor : IDisposable
{
    public event Action? RobloxStarted;
    public event Action? RobloxStopped;

    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private bool _robloxRunning;

    public bool IsRobloxRunning
    {
        get
        {
            try
            {
                return Process.GetProcessesByName("RobloxPlayerBeta").Length > 0;
            }
            catch { return false; }
        }
    }

    public void Start()
    {
        try
        {
            var startQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Process' AND TargetInstance.Name = 'RobloxPlayerBeta.exe'");
            _startWatcher = new ManagementEventWatcher(startQuery);
            _startWatcher.EventArrived += (_, _) => OnStarted();
            _startWatcher.Start();

            var stopQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Process' AND TargetInstance.Name = 'RobloxPlayerBeta.exe'");
            _stopWatcher = new ManagementEventWatcher(stopQuery);
            _stopWatcher.EventArrived += (_, _) => OnStopped();
            _stopWatcher.Start();

            _robloxRunning = IsRobloxRunning;
            Logger.Info("Process monitor started. Roblox running: " + _robloxRunning);
        }
        catch (Exception ex)
        {
            Logger.Warn("Process monitor failed to start: " + ex.Message);
        }
    }

    private void OnStarted()
    {
        if (_robloxRunning) return;
        _robloxRunning = true;
        Logger.Info("RobloxPlayerBeta.exe started.");
        RobloxStarted?.Invoke();
    }

    private void OnStopped()
    {
        // Confirm no instance remains.
        if (IsRobloxRunning) return;
        if (!_robloxRunning) return;
        _robloxRunning = false;
        Logger.Info("RobloxPlayerBeta.exe stopped.");
        RobloxStopped?.Invoke();
    }

    public void Stop()
    {
        try { _startWatcher?.Stop(); } catch { }
        try { _stopWatcher?.Stop(); } catch { }
        _startWatcher?.Dispose();
        _stopWatcher?.Dispose();
        _startWatcher = null;
        _stopWatcher = null;
    }

    public void Dispose() => Stop();
}