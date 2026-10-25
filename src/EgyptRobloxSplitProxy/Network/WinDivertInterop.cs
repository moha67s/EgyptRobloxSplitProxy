using System;
using System.Runtime.InteropServices;

namespace EgyptRobloxSplitProxy.Network;

/// <summary>
/// P/Invoke bindings for WinDivert (https://reqrypt.org/windivert.html).
/// Requires WinDivert.dll + WinDivert64.sys installed alongside the executable.
/// Only the subset needed for redirecting Roblox TCP/UDP flows is declared.
/// </summary>
public static class WinDivertInterop
{
    public const string FilterRobloxTcp =
        "tcp and (tcp.DstPort == 443 or tcp.DstPort == 80) and " +
        "(ip.DstAddr >= 0.0.0.0)"; // placeholder; refine with IP sets at runtime

    public const uint WINDIVERT_LAYER_NETWORK = 0;
    public const uint WINDIVERT_FLAG_SNIFF = 1;
    public const uint WINDIVERT_FLAG_DROP = 2;
    public const uint WINDIVERT_FLAG_RECV_ONLY = 0x00000100;
    public const uint WINDIVERT_FLAG_SEND_ONLY = 0x00000200;

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
/// Managed wrapper. Start() opens a divert handle in a background thread and
/// invokes PacketReceived for each captured packet. Higher-level redirect logic
/// (rewriting dst port to the local proxy) lives in SplitProxyEngine.
/// </summary>
public sealed class WinDivertSession : IDisposable
{
    private IntPtr _handle = IntPtr.Zero;
    private Thread? _thread;
    private volatile bool _running;
    public bool IsRunning => _running;

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
        var addr = Marshal.AllocHGlobal(64); // WINDIVERT_ADDRESS
        try
        {
            while (_running)
            {
                if (WinDivertInterop.WinDivertRecv(_handle, buf, 65535, out var read, addr))
                {
                    // Default: reinject unmodified so we never black-hole traffic.
                    // Subscribers may inspect/modify via PacketReceived before reinjection.
                    PacketReceived?.Invoke(buf, read, addr);
                    WinDivertInterop.WinDivertSend(_handle, buf, read, out _, addr);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            Marshal.FreeHGlobal(addr);
        }
    }

    public void Stop()
    {
        _running = false;
        if (_handle != IntPtr.Zero) { WinDivertInterop.WinDivertClose(_handle); _handle = IntPtr.Zero; }
        try { _thread?.Join(2000); } catch { }
    }

    public void Dispose() => Stop();
}