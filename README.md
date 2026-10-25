# Egypt Roblox Split-Proxy

A lightweight, open-source Windows background application that restores Roblox
connectivity for users behind Egyptian ISP-level blocks (WE, Vodafone, Orange),
**without** routing all traffic through a full VPN.

## Why this exists

Egyptian ISPs use a two-tiered censorship system that breaks Roblox:

1. **HTTP/TLS SNI filtering** blocks `roblox.com`, `api.roblox.com`, friends/chat
   subdomains.
2. **UDP throttling/dropping** on game server ports (UDP 49152-65535) produces
   Roblox *Error Code 279*.

Full VPNs (Cloudflare WARP, WireGuard) get flagged by Roblox's IP filters or
throttled by ISPs. DPI tools like GoodbyeDPI only fix HTTPS and ignore UDP.

This app implements **Dual-Layer Split Tunneling**:

- **Layer A** - local HTTPS/SNI proxy that fragments TLS ClientHello packets for
  `*.roblox.com` / `*.rbxcdn.com`, restoring web assets, friends, chat and game
  launching at native speed.
- **Layer B** - lightweight UDP relay that obfuscates Roblox game-server traffic
  with MTU clamping to 1280, avoiding fragmentation drops.

Only `RobloxPlayerBeta.exe` traffic is affected; Discord, YouTube, Chrome and
other games are untouched.

## Features

- Single-click Start/Stop system tray app.
- Auto-detects `RobloxPlayerBeta.exe` launch/exit.
- Encrypted DNS via DoH (Cloudflare / Google) to defeat DNS poisoning.
- Executable-based split tunneling.
- Single-file self-contained `.exe`.

## Quick start

1. Download `EgyptRobloxSplitProxy.exe` from Releases.
2. Run as Administrator (required for firewall rules / optional WinDivert).
3. Right-click tray icon -> **Start**.
4. Launch Roblox.

## Build from source

See [docs/BUILD.md](docs/BUILD.md).

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Disclaimer

This tool is provided for personal, lawful use. Users are responsible for
complying with their local laws, ISP terms of service, and Roblox's Terms of
Service. The authors are not responsible for misuse.

## License

MIT - see [LICENSE](LICENSE).