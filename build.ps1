<#
.SYNOPSIS  Builds Shazim (Debug by default).
.EXAMPLE   ./build.ps1 -Configuration Release
#>
param(
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
    [switch]$Clean
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scripts\common.ps1"

if ($Clean) { & $dotnet clean $appProject -c $Configuration -nologo -v q }
Write-Host "Building Shazim ($Configuration)..." -ForegroundColor Cyan
& $dotnet build $appProject -c $Configuration -nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit $LASTEXITCODE }
Write-Host "Build succeeded: $(Get-AppOutput $Configuration)" -ForegroundColor Green
