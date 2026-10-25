# Build instructions

## Prerequisites

- Windows 10/11 x64
- .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)
- (Optional, for kernel redirect) WinDivert 2.2.x binaries in `native/windivert/`

## Build (Debug)

```powershell
cd src
dotnet build EgyptRobloxSplitProxy.sln
```

## Publish single-file .exe

```powershell
cd src/EgyptRobloxSplitProxy
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output: `bin/Release/net8.0-windows/win-x64/publish/EgyptRobloxSplitProxy.exe`

Or use the helper script:

```powershell
./scripts/build.ps1
```

## WinDivert setup (optional)

1. Download WinDivert 2.2.x from https://reqrypt.org/windivert.html
2. Copy `WinDivert.dll` and `WinDivert64.sys` next to the published `.exe`.
3. Run the app as Administrator.

Without these, the app runs in proxy-only mode.