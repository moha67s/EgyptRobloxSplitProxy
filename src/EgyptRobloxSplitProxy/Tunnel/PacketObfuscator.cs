using System;
using System.Text;

namespace EgyptRobloxSplitProxy.Tunnel;

/// <summary>
/// Lightweight XOR obfuscation for UDP game packets. Not cryptography - just
/// enough to avoid naive DPI signature matching on Roblox UDP traffic.
/// </summary>
public static class PacketObfuscator
{
    public static byte[] Obfuscate(byte[] data, string key)
    {
        if (string.IsNullOrEmpty(key)) return (byte[])data.Clone();
        var kb = Encoding.UTF8.GetBytes(key);
        var outb = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            outb[i] = (byte)(data[i] ^ kb[i % kb.Length] ^ (byte)(i * 31));
        return outb;
    }

    public static byte[] Deobfuscate(byte[] data, string key) => Obfuscate(data, key); // symmetric

    /// <summary>
    /// Frame format: [4 bytes big-endian length][obfuscated payload].
    /// </summary>
    public static byte[] Frame(byte[] payload, string key)
    {
        var obf = Obfuscate(payload, key);
        var frame = new byte[4 + obf.Length];
        var len = BitConverter.GetBytes(obf.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(len);
        Buffer.BlockCopy(len, 0, frame, 0, 4);
        Buffer.BlockCopy(obf, 0, frame, 4, obf.Length);
        return frame;
    }

    public static byte[]? Unframe(ReadOnlySpan<byte> data, string key, out int consumed)
    {
        consumed = 0;
        if (data.Length < 4) return null;
        var lenBytes = data.Slice(0, 4).ToArray();
        if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
        int len = BitConverter.ToInt32(lenBytes, 0);
        if (len < 0 || len > data.Length - 4) return null;
        var payload = data.Slice(4, len).ToArray();
        consumed = 4 + len;
        return Deobfuscate(payload, key);
    }
}