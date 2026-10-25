using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using EgyptRobloxSplitProxy.Engine;
using EgyptRobloxSplitProxy.Logging;

namespace EgyptRobloxSplitProxy.Tray;

/// <summary>
/// System-tray application context with Start/Stop toggle.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SplitProxyEngine _engine;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _statusItem;

    public TrayApplicationContext(SplitProxyEngine engine)
    {
        _engine = engine;
        _statusItem = new ToolStripMenuItem("Status: Stopped") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Start", null, (_, _) => Toggle());

        _engine.StatusChanged += s =>
        {
            try { if (_statusItem is not null) _statusItem.Text = "Status: " + s; } catch { }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add("Verify setup", null, (_, _) => VerifySetup());
        menu.Items.Add("Open Log", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Egypt Roblox Split-Proxy",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Toggle();

        if (_engine.Config.StartMinimized)
        {
            // auto-start
            try { _engine.Start(); _toggleItem.Text = "Stop"; _statusItem.Text = "Status: Running"; }
            catch { }
        }

        if (!_engine.WinDivertAvailable)
        {
            try
            {
                _tray.ShowBalloonTip(5000, "EgyptRobloxSplitProxy",
                    "WinDivert not found - running in proxy-only mode. Roblox will not be redirected automatically. Run scripts/setup.ps1 as Administrator.",
                    ToolTipIcon.Warning);
            }
            catch { }
        }
    }

    private void Toggle()
    {
        try
        {
            if (_engine.IsRunning)
            {
                _engine.Stop();
                _toggleItem.Text = "Start";
            }
            else
            {
                _engine.Start();
                _toggleItem.Text = "Stop";
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            MessageBox.Show(ex.Message, "EgyptRobloxSplitProxy", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void VerifySetup()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("WinDivert driver: " + (_engine.WinDivertAvailable ? "OK (transparent mode available)" : "MISSING (proxy-only mode)"));
        sb.AppendLine("Running as admin: " + IsAdmin());
        sb.AppendLine("Proxy port 8080: " + PortListening(8080));
        sb.AppendLine("Config: " + (_engine.Config.RelayEndpoint == "" ? "no UDP relay endpoint set" : _engine.Config.RelayEndpoint));
        MessageBox.Show(sb.ToString(), "EgyptRobloxSplitProxy - Setup Check", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static bool IsAdmin()
    {
        try
        {
            var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var pr = new System.Security.Principal.WindowsPrincipal(id);
            return pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static bool PortListening(int port)
    {
        try
        {
            using var c = new System.Net.Sockets.TcpClient();
            c.Connect("127.0.0.1", port);
            return true;
        }
        catch { return false; }
    }

    private static void OpenLog()
    {
        try { Process.Start(new ProcessStartInfo(Logger.LogFilePath) { UseShellExecute = true }); }
        catch { }
    }

    private void Exit()
    {
        try { _engine.Dispose(); } catch { }
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }
}