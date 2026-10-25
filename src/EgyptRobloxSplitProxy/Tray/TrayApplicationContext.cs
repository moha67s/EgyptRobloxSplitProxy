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