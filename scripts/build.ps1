param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\EgyptRobloxSplitProxy\EgyptRobloxSplitProxy.csproj"

Write-Host "Building $proj ($Configuration / $Runtime)..."
dotnet publish $proj -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true

$out = Join-Path $root "src\EgyptRobloxSplitProxy\bin\$Configuration\net8.0-windows\$Runtime\publish"
Write-Host "Published to: $out"
Get-ChildItem $out -Filter "*.exe"