# System Architecture

## Overview

```
+------------------+        +-------------------------------+
| RobloxPlayerBeta | -----> |  WinDivert (optional kernel)   |
+------------------+        +-------------------------------+
        | TCP 443 (HTTPS)              | UDP 49152-65535
        v                              v
+------------------+        +-------------------------------+
| Layer A: HTTPS   |        | Layer B: UDP Relay            |
| SNI Proxy        |        | (obfuscate + MTU clamp 1280)  |
| 127.0.0.1:8080   |        | 0.0.0.0:51820                |
+------------------+        +-------------------------------+
        |                              |
        v                              v
   Cloudflare/Google DoH         Relay endpoint / direct
```

## Components

### Engine (`Engine/`)
- `SplitProxyEngine` - orchestrates both layers, firewall rules, process hooks.
- `ProcessMonitor` - WMI-based detection of `RobloxPlayerBeta.exe`.

### Layer A - HTTPS/SNI (`Proxy/`)
- `HttpsSniProxy` - local TCP listener, HTTP CONNECT handling, upstream forwarding.
- `SniFragmenter` - TLS ClientHello SNI extraction + byte-level fragmentation.
- `DoHResolver` - DNS-over-HTTPS (Cloudflare/Google) with 5-min cache.

### Layer B - UDP (`Tunnel/`)
- `UdpRelay` - UDP listener, obfuscation, forwarding.
- `PacketObfuscator` - XOR keystream obfuscation + framing.
- `MtuClamper` - splits datagrams to <= 1280 bytes.

### Network (`Network/`)
- `WinDivertInterop` / `WinDivertSession` - optional kernel packet capture/redirect.
- `RouteManager` - Windows route management.
- `FirewallManager` - Windows Firewall allow rules.

### Tray (`Tray/`)
- `TrayApplicationContext` - NotifyIcon, context menu, Start/Stop toggle.

## Data flow

1. Roblox launches -> `ProcessMonitor` fires `RobloxStarted`.
2. User clicks Start (or auto-start) -> engine starts both layers + firewall rules.
3. HTTPS requests for Roblox domains -> local proxy resolves via DoH, fragments
   ClientHello, forwards to upstream.
4. UDP game traffic -> relay obfuscates + clamps MTU -> relay endpoint.
5. Roblox exits -> `RobloxStopped` fired (engine stays armed for next session).

## Notes on kernel redirect

WinDivert requires the signed driver (`WinDivert64.sys`) and `WinDivert.dll`
placed next to the executable, plus Administrator privileges. When unavailable,
the app runs in **proxy-only mode** and the user must set the system/Roblox
proxy to `127.0.0.1:8080` manually.