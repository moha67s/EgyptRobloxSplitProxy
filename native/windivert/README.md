# WinDivert binaries (not committed)

This directory is intentionally empty in source control. To enable kernel-level
packet redirect (Layer A/B transparent interception), download WinDivert 2.2.x:

https://reqrypt.org/windivert.html

Copy these files next to the published executable:

- `WinDivert.dll`
- `WinDivert64.sys`

Run the application as Administrator. Without them, the app operates in
proxy-only mode.