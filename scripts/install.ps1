# Installs firewall rules and (optionally) copies the exe to Program Files.
# Run as Administrator.
param(
    [string]$ExePath = ".\EgyptRobloxSplitProxy.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath)) {
    Write-Error "Executable not found: $ExePath"
    exit 1
}

$dest = Join-Path $env:ProgramFiles "EgyptRobloxSplitProxy"
New-Item -ItemType Directory -Path $dest -Force | Out-Null
Copy-Item $ExePath (Join-Path $dest "EgyptRobloxSplitProxy.exe") -Force
Write-Host "Installed to $dest"

# Firewall rules are also created by the app at runtime; add them here too.
netsh advfirewall firewall add rule name="EgyptRobloxSplitProxy-HTTPS" dir=in action=allow protocol=TCP localport=8080 | Out-Null
netsh advfirewall firewall add rule name="EgyptRobloxSplitProxy-UDP"   dir=in action=allow protocol=UDP localport=51820 | Out-Null
Write-Host "Firewall rules added."