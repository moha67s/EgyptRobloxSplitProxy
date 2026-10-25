using System;
using System.Collections.Generic;

namespace EgyptRobloxSplitProxy.Tunnel;

/// <summary>
/// Clamps UDP payload size to a safe MTU (default 1280) to avoid fragmentation
/// over Egyptian ISP nodes.
/// </summary>
public static class MtuClamper
{
    public const int DefaultMtu = 1280;
    // IPv4 (20) + UDP (8) headers
    public const int IpUdpHeaderSize = 28;

    public static int MaxPayload(int mtu = DefaultMtu) => mtu - IpUdpHeaderSize;

    /// <summary>
    /// Splits a datagram into chunks no larger than the clamped payload size.
    /// </summary>
    public static IEnumerable<byte[]> Split(byte[] data, int mtu = DefaultMtu)
    {
        int max = MaxPayload(mtu);
        if (data.Length <= max) { yield return data; yield break; }
        for (int i = 0; i < data.Length; i += max)
        {
            int len = Math.Min(max, data.Length - i);
            var c = new byte[len];
            Buffer.BlockCopy(data, i, c, 0, len);
            yield return c;
        }
    }
}