using System;
using System.Diagnostics;

namespace EgyptRobloxSplitProxy.Network;

/// <summary>
/// Adds/removes Windows routes so Roblox traffic flows through the local proxy.
/// Uses the `route` command for simplicity; production code should use IP Helper API.
/// </summary>
public static class RouteManager
{
    public static void AddRoute(string destination, string mask, string gateway)
    {
        Run($"route add {destination} mask {mask} {gateway} metric 5");
    }

    public static void DeleteRoute(string destination, string mask, string gateway)
    {
        Run($"route delete {destination} mask {mask} {gateway}");
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
                RedirectStandardError = true,
                Verb = "runas"
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(10000);
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }
}