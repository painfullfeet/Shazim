# Shared helpers for the build scripts.
$root = Split-Path $PSScriptRoot -Parent
$appProject = Join-Path $root 'src\Shazim\App\Shazim.App.csproj'
$testProject = Join-Path $root 'src\Shazim\Tests\Shazim.Tests.csproj'

# Prefer a local .NET 10 SDK copy (not committed), then the one on PATH.
$bundled = Join-Path $root 'dotnet10sdk\dotnet.exe'
$dotnet = if (Test-Path $bundled) { $bundled } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Get-AppOutput([string]$Configuration) {
    Join-Path $root "src\Shazim\App\bin\$Configuration\net10.0-windows10.0.19041"
}
