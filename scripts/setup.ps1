# setup.ps1 - One-time setup for Egypt Roblox Split-Proxy.
# Downloads WinDivert 2.2.2 and places the driver files next to the executable.
# Run as Administrator.
param(
    [string]$InstallDir = $PSScriptRoot + "\..\src\EgyptRobloxSplitProxy\bin\Release\net8.0-windows\win-x64\publish"
)

$ErrorActionPreference = "Stop"

# Admin check
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Please run this script as Administrator."
    exit 1
}

# Resolve install dir to absolute
$InstallDir = (Resolve-Path $InstallDir).Path
$exe = Join-Path $InstallDir "EgyptRobloxSplitProxy.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Executable not found at $exe. Build first (scripts/build.ps1)."
    exit 1
}

$zip = Join-Path $env:TEMP "WinDivert-2.2.2-A.zip"
$url = "https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip"

Write-Host "Downloading WinDivert 2.2.2..."
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

Write-Host "Extracting..."
$extract = Join-Path $env:TEMP "WinDivert-extract"
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $extract -Force

$src = Join-Path $extract "WinDivert-2.2.2-A\x64"
Copy-Item (Join-Path $src "WinDivert.dll")   (Join-Path $InstallDir "WinDivert.dll")   -Force
Copy-Item (Join-Path $src "WinDivert64.sys") (Join-Path $InstallDir "WinDivert64.sys") -Force
Write-Host "WinDivert files installed to $InstallDir"

# Firewall rules
netsh advfirewall firewall delete rule name="EgyptRobloxSplitProxy-HTTPS" 2>$null | Out-Null
netsh advfirewall firewall delete rule name="EgyptRobloxSplitProxy-UDP"   2>$null | Out-Null
netsh advfirewall firewall add rule name="EgyptRobloxSplitProxy-HTTPS" dir=in action=allow protocol=TCP localport=8080 | Out-Null
netsh advfirewall firewall add rule name="EgyptRobloxSplitProxy-UDP"   dir=in action=allow protocol=UDP localport=51820 | Out-Null
Write-Host "Firewall rules added."

# Verify driver loads
Write-Host "Verifying WinDivert driver..."
$test = & (Join-Path $src "windivertctl.exe") 2>&1
Write-Host "windivertctl: $test"

Write-Host ""
Write-Host "Setup complete. Run EgyptRobloxSplitProxy.exe as Administrator for transparent mode."