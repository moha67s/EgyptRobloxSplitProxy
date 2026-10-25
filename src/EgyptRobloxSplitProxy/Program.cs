using System;
using System.Threading;
using System.Windows.Forms;
using EgyptRobloxSplitProxy.Engine;
using EgyptRobloxSplitProxy.Logging;
using EgyptRobloxSplitProxy.Tray;

namespace EgyptRobloxSplitProxy;

internal static class Program
{
    private const string MutexName = "Global\\EgyptRobloxSplitProxy_SingleInstance";

    [STAThread]
    private static void Main()
    {
        // Single-instance guard.
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Egypt Roblox Split-Proxy is already running.", "EgyptRobloxSplitProxy",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Logger.Info("Application starting.");

        try
        {
            var engine = new SplitProxyEngine();
            Application.Run(new TrayApplicationContext(engine));
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal error: " + ex);
            MessageBox.Show("Fatal error: " + ex.Message, "EgyptRobloxSplitProxy",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}