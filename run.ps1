<#
.SYNOPSIS  Builds (unless -NoBuild) and starts Shazim.
#>
param(
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scripts\common.ps1"

if (-not $NoBuild) {
    & $dotnet build $appProject -c $Configuration -nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit $LASTEXITCODE }
}
$exe = Join-Path (Get-AppOutput $Configuration) 'Shazim.exe'
if (-not (Test-Path $exe)) { Write-Host "Executable not found: $exe" -ForegroundColor Red; exit 1 }
Start-Process $exe
