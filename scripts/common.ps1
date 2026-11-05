# Shared helpers for the build scripts.
$root = Split-Path $PSScriptRoot -Parent
$appProject = Join-Path $root 'src\Shazim\App\Shazim.App.csproj'
$testProject = Join-Path $root 'src\Shazim\Tests\Shazim.Tests.csproj'

# Prefer the .NET 8 SDK bundled in the repository, then the one on PATH.
$bundled = Join-Path $root 'dotnet8sdk\dotnet.exe'
$dotnet = if (Test-Path $bundled) { $bundled } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Get-AppOutput([string]$Configuration) {
    Join-Path $root "src\Shazim\App\bin\$Configuration\net8.0-windows10.0.19041"
}
