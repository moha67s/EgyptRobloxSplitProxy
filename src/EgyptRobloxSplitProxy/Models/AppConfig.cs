using System;
using System.IO;
using System.Text.Json;

namespace EgyptRobloxSplitProxy.Models;

public sealed class AppConfig
{
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int HttpsProxyPort { get; set; } = 8080;
    public int UdpRelayPort { get; set; } = 51820;
    public int Mtu { get; set; } = 1280;

    // DoH provider: "cloudflare" or "google"
    public string DohProvider { get; set; } = "cloudflare";

    // Upstream relay endpoint for UDP tunnel (host:port). Empty = direct (no relay).
    public string RelayEndpoint { get; set; } = "";

    // Shared secret used to obfuscate UDP payloads (XOR keystream seed).
    public string TunnelKey { get; set; } = "change-me-please";

    // Domains that trigger SNI fragmentation.
    public string[] RobloxDomains { get; set; } =
    {
        "roblox.com", "www.roblox.com", "api.roblox.com",
        "friends.roblox.com", "chat.roblox.com", "realtime.roblox.com",
        "rbxcdn.com", "www.rbxcdn.com", "t0.rbxcdn.com", "t1.rbxcdn.com",
        "t2.rbxcdn.com", "t3.rbxcdn.com", "t4.rbxcdn.com", "t5.rbxcdn.com",
        "t6.rbxcdn.com", "t7.rbxcdn.com", "assetdelivery.roblox.com",
        "thumbnails.roblox.com", "avatar.roblox.com", "games.roblox.com",
        "presence.roblox.com", "notifications.roblox.com"
    };

    // Roblox game server UDP port range.
    public int UdpPortMin { get; set; } = 49152;
    public int UdpPortMax { get; set; } = 65535;

    // Auto-start tunnel when RobloxPlayerBeta.exe is detected.
    public bool AutoHookRoblox { get; set; } = true;

    public bool StartMinimized { get; set; } = true;

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EgyptRobloxSplitProxy", "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg is not null) return cfg;
            }
        }
        catch { /* fall through to default */ }
        var def = new AppConfig();
        def.Save();
        return def;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { /* ignore */ }
    }
}