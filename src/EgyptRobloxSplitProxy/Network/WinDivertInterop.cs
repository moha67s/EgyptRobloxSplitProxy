using System;
using System.Runtime.InteropServices;
using EgyptRobloxSplitProxy.Proxy;

namespace EgyptRobloxSplitProxy.Network;

/// <summary>
/// P/Invoke bindings for WinDivert.
/// </summary>
public static class WinDivertInterop
{
    public const uint WINDIVERT_LAYER_NETWORK = 0;
    public const uint WINDIVERT_FLAG_SNIFF = 1;

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    public static extern IntPtr WinDivertOpen(string filter, uint layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertRecv(IntPtr handle, IntPtr pPacket, uint packetLen,
        out uint readLen, IntPtr pAddr);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSend(IntPtr handle, IntPtr pPacket, uint packetLen,
        out uint writeLen, IntPtr pAddr);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertClose(IntPtr handle);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern void WinDivertHelperParsePacket(IntPtr pPacket, uint packetLen,
        out IntPtr ppIpHdr, out IntPtr ppIpv6Hdr, out IntPtr ppTcpHdr,
        out IntPtr ppUdpHdr, out IntPtr ppIcmpHdr, out IntPtr ppIcmpv6Hdr,
        out IntPtr ppPayload, out uint payloadLen);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.StdCall)]
    public static extern void WinDivertHelperCalcChecksums(IntPtr pPacket, uint packetLen, IntPtr pAddr, uint flags);

    public static bool IsAvailable()
    {
        try
        {
            var h = WinDivertOpen("false", WINDIVERT_LAYER_NETWORK, 0, WINDIVERT_FLAG_SNIFF);
            if (h != IntPtr.Zero) { WinDivertClose(h); return true; }
            return false;
        }
        catch (DllNotFoundException) { return false; }
        catch { return false; }
    }
}

/// <summary>
/// Managed WinDivert session. Captures outbound TCP 443 (and 80) packets and
/// fragments the TLS ClientHello in transit when its SNI targets a Roblox domain,
/// defeating per-packet SNI inspection. Other packets are reinjected unmodified.
/// </summary>
public sealed class WinDivertSession : IDisposable
{
    private IntPtr _handle = IntPtr.Zero;
    private Thread? _thread;
    private volatile bool _running;
    public bool IsRunning => _running;

    public string[] RobloxDomains { get; set; } = Array.Empty<string>();

    public event Action<IntPtr, uint, IntPtr>? PacketReceived;

    public bool Start(string filter)
    {
        if (_running) return true;
        _handle = WinDivertInterop.WinDivertOpen(filter, WinDivertInterop.WINDIVERT_LAYER_NETWORK, 0, 0);
        if (_handle == IntPtr.Zero) return false;
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "WinDivertRecv" };
        _thread.Start();
        return true;
    }

    private void Run()
    {
        var buf = Marshal.AllocHGlobal(65535);
        var addr = Marshal.AllocHGlobal(128);
        try
        {
            while (_running)
            {
                if (!WinDivertInterop.WinDivertRecv(_handle, buf, 65535, out var read, addr))
                    continue;
                PacketReceived?.Invoke(buf, read, addr);
                try { ProcessPacket(buf, read, addr); }
                catch { Reinject(buf, read, addr); }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            Marshal.FreeHGlobal(addr);
        }
    }

    private void ProcessPacket(IntPtr buf, uint read, IntPtr addr)
    {
        WinDivertInterop.WinDivertHelperParsePacket(buf, read,
            out var pIp, out var pIpv6, out var pTcp, out var pUdp,
            out _, out _, out var pPayload, out var payloadLen);

        // Only handle IPv4 TCP with payload.
        if (pIp == IntPtr.Zero || pTcp == IntPtr.Zero || pPayload == IntPtr.Zero || payloadLen == 0)
        {
            Reinject(buf, read, addr);
            return;
        }
        // IPv6 not handled here; pass through.
        if (pIpv6 != IntPtr.Zero) { Reinject(buf, read, addr); return; }

        byte ihlByte = Marshal.ReadByte(pIp);
        int ihl = (ihlByte & 0x0F) * 4;
        ushort dstPort = (ushort)((Marshal.ReadByte(pTcp, 2) << 8) | Marshal.ReadByte(pTcp, 3));
        if (dstPort != 443 && dstPort != 80)
        {
            Reinject(buf, read, addr);
            return;
        }

        var payload = new byte[payloadLen];
        Marshal.Copy(pPayload, payload, 0, (int)payloadLen);
        if (!SniFragmenter.IsClientHello(payload))
        {
            Reinject(buf, read, addr);
            return;
        }
        var (sniOff, sniLen, host) = SniFragmenter.ExtractSniRange(payload);
        if (sniOff < 0 || !SniFragmenter.IsRobloxDomain(host, RobloxDomains))
        {
            Reinject(buf, read, addr);
            return;
        }

        // Split inside the SNI hostname so no single segment contains the full SNI.
        int splitPoint = sniOff + (sniLen / 2);
        if (splitPoint <= 0 || splitPoint >= payload.Length)
        {
            Reinject(buf, read, addr);
            return;
        }

        int payloadOffset = (int)(pPayload.ToInt64() - buf.ToInt64());
        int tcpHeaderLen = ((Marshal.ReadByte(pTcp, 12) >> 4) & 0x0F) * 4;

        // Packet 1: first splitPoint bytes of payload.
        int len1 = payloadOffset + splitPoint;
        var pkt1 = new byte[len1];
        Marshal.Copy(buf, pkt1, 0, len1);
        pkt1[2] = (byte)((len1 >> 8) & 0xFF);
        pkt1[3] = (byte)(len1 & 0xFF);

        // Packet 2: remaining payload, seq += splitPoint.
        int rem = payload.Length - splitPoint;
        int len2 = ihl + tcpHeaderLen + rem;
        var pkt2 = new byte[len2];
        Marshal.Copy(buf, pkt2, 0, ihl);                 // IP header
        Marshal.Copy(new IntPtr(buf.ToInt64() + ihl), pkt2, ihl, tcpHeaderLen); // TCP header
        Buffer.BlockCopy(payload, splitPoint, pkt2, ihl + tcpHeaderLen, rem);
        pkt2[2] = (byte)((len2 >> 8) & 0xFF);
        pkt2[3] = (byte)(len2 & 0xFF);
        // seq number
        uint origSeq = (uint)((Marshal.ReadByte(pTcp, 4) << 24) | (Marshal.ReadByte(pTcp, 5) << 16) |
                              (Marshal.ReadByte(pTcp, 6) << 8) | Marshal.ReadByte(pTcp, 7));
        uint newSeq = origSeq + (uint)splitPoint;
        int tcpStart2 = ihl;
        pkt2[tcpStart2 + 4] = (byte)((newSeq >> 24) & 0xFF);
        pkt2[tcpStart2 + 5] = (byte)((newSeq >> 16) & 0xFF);
        pkt2[tcpStart2 + 6] = (byte)((newSeq >> 8) & 0xFF);
        pkt2[tcpStart2 + 7] = (byte)(newSeq & 0xFF);

        var gch1 = GCHandle.Alloc(pkt1, GCHandleType.Pinned);
        var gch2 = GCHandle.Alloc(pkt2, GCHandleType.Pinned);
        try
        {
            WinDivertInterop.WinDivertHelperCalcChecksums(gch1.AddrOfPinnedObject(), (uint)pkt1.Length, addr, 0);
            WinDivertInterop.WinDivertHelperCalcChecksums(gch2.AddrOfPinnedObject(), (uint)pkt2.Length, addr, 0);
            WinDivertInterop.WinDivertSend(_handle, gch1.AddrOfPinnedObject(), (uint)pkt1.Length, out _, addr);
            WinDivertInterop.WinDivertSend(_handle, gch2.AddrOfPinnedObject(), (uint)pkt2.Length, out _, addr);
        }
        finally
        {
            gch1.Free();
            gch2.Free();
        }
        // Drop original (do not reinject).
    }

    private void Reinject(IntPtr buf, uint read, IntPtr addr)
    {
        WinDivertInterop.WinDivertSend(_handle, buf, read, out _, addr);
    }

    public void Stop()
    {
        _running = false;
        if (_handle != IntPtr.Zero) { WinDivertInterop.WinDivertClose(_handle); _handle = IntPtr.Zero; }
        try { _thread?.Join(2000); } catch { }
    }

    public void Dispose() => Stop();
}