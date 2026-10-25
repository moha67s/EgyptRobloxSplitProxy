using System;
using System.Diagnostics;

namespace EgyptRobloxSplitProxy.Network;

/// <summary>
/// Creates Windows Firewall allow rules for the proxy/listener ports.
/// </summary>
public static class FirewallManager
{
    public static void AllowPort(int port, string name)
    {
        Run($"netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={port}");
        Run($"netsh advfirewall firewall add rule name=\"{name}-udp\" dir=in action=allow protocol=UDP localport={port}");
    }

    public static void RemoveRule(string name)
    {
        Run($"netsh advfirewall firewall delete rule name=\"{name}\"");
        Run($"netsh advfirewall firewall delete rule name=\"{name}-udp\"");
    }

    private static void Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c " + args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(10000);
        }
        catch { }
    }
}