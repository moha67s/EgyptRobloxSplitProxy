using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace EgyptRobloxSplitProxy.Proxy;

/// <summary>
/// DNS-over-HTTPS resolver. Routes Roblox DNS lookups through Cloudflare/Google
/// to defeat local ISP DNS poisoning.
/// </summary>
public sealed class DoHResolver : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly ConcurrentDictionary<string, (DateTime expires, string[] ips)> _cache = new();

    public DoHResolver(string provider = "cloudflare")
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Add("Accept", "application/dns-json");
        _endpoint = provider.ToLowerInvariant() switch
        {
            "google" => "https://dns.google/resolve",
            _ => "https://cloudflare-dns.com/dns-query"
        };
    }

    public async Task<string[]> ResolveAsync(string hostname)
    {
        if (_cache.TryGetValue(hostname, out var cached) && cached.expires > DateTime.UtcNow)
            return cached.ips;

        try
        {
            var url = $"{_endpoint}?name={Uri.EscapeDataString(hostname)}&type=A";
            using var resp = await _http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var ips = new System.Collections.Generic.List<string>();
            if (doc.RootElement.TryGetProperty("Answer", out var answers))
            {
                foreach (var a in answers.EnumerateArray())
                {
                    if (a.TryGetProperty("type", out var t) && t.GetInt32() == 1 &&
                        a.TryGetProperty("data", out var data))
                    {
                        ips.Add(data.GetString()!);
                    }
                }
            }
            var result = ips.ToArray();
            _cache[hostname] = (DateTime.UtcNow.AddMinutes(5), result);
            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Dispose() => _http.Dispose();
}