using System;
using System.Linq;
using System.Text;

namespace EgyptRobloxSplitProxy.Proxy;

/// <summary>
/// Splits a TLS ClientHello into multiple small TCP segments so that the SNI
/// hostname is not present in any single packet, defeating naive SNI filters.
/// </summary>
public static class SniFragmenter
{
    /// <summary>
    /// Returns the byte ranges to send as separate TCP writes. Each chunk is small
    /// enough that the SNI extension rarely survives intact in one segment.
    /// </summary>
    public static byte[][] Fragment(byte[] payload, int chunkSize = 20)
    {
        if (payload is null || payload.Length == 0) return Array.Empty<byte[]>();
        var chunks = new System.Collections.Generic.List<byte[]>();
        for (int i = 0; i < payload.Length; i += chunkSize)
        {
            int len = Math.Min(chunkSize, payload.Length - i);
            var c = new byte[len];
            Buffer.BlockCopy(payload, i, c, 0, len);
            chunks.Add(c);
        }
        return chunks.ToArray();
    }

    /// <summary>
    /// Detects whether a buffer looks like a TLS ClientHello.
    /// </summary>
    public static bool IsClientHello(ReadOnlySpan<byte> data)
    {
        // TLS record: content type 0x16 (handshake), version 0x0301/0x0303, then handshake type 0x01 (ClientHello)
        return data.Length > 5 && data[0] == 0x16 && data[5] == 0x01;
    }

    /// <summary>
    /// Extracts the SNI hostname from a TLS ClientHello if present.
    /// </summary>
    public static string? ExtractSni(ReadOnlySpan<byte> data)
    {
        try
        {
            // Skip record header (5) + handshake header (4) + client version (2) + random (32)
            int pos = 5 + 4 + 2 + 32;
            if (pos >= data.Length) return null;
            // session id
            int sidLen = data[pos++];
            pos += sidLen;
            // cipher suites
            if (pos + 2 > data.Length) return null;
            int csLen = (data[pos] << 8) | data[pos + 1]; pos += 2 + csLen;
            // compression methods
            if (pos >= data.Length) return null;
            int cmLen = data[pos++];
            pos += cmLen;
            // extensions
            if (pos + 2 > data.Length) return null;
            int extLen = (data[pos] << 8) | data[pos + 1]; pos += 2;
            int extEnd = pos + extLen;
            while (pos + 4 <= extEnd && pos + 4 <= data.Length)
            {
                int extType = (data[pos] << 8) | data[pos + 1]; pos += 2;
                int eLen = (data[pos] << 8) | data[pos + 1]; pos += 2;
                if (extType == 0x0000) // server_name
                {
                    // list length
                    if (pos + 2 > data.Length) return null;
                    pos += 2; // skip list length
                    if (pos + 3 > data.Length) return null;
                    // name type (0 = host_name)
                    if (data[pos] != 0) return null;
                    pos++;
                    int nameLen = (data[pos] << 8) | data[pos + 1]; pos += 2;
                    if (pos + nameLen > data.Length) return null;
                    return Encoding.ASCII.GetString(data.Slice(pos, nameLen));
                }
                pos += eLen;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    public static bool IsRobloxDomain(string? host, string[] domains)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.ToLowerInvariant();
        foreach (var d in domains)
        {
            var dd = d.ToLowerInvariant();
            if (host == dd || host.EndsWith("." + dd)) return true;
        }
        return false;
    }
}