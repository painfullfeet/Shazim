<#
.SYNOPSIS  Builds the installer (./release.ps1) if needed, then opens it.
.DESCRIPTION
    Shazim installs for the current Windows user only, no administrator rights needed:
    %LOCALAPPDATA%\Programs\Shazim, Start menu / desktop shortcuts and an "Installed apps" entry.
    -Quiet installs without showing the window and starts Shazim.
#>
param([switch]$Rebuild, [switch]$Quiet)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scripts\common.ps1"

$setup = Get-ChildItem (Join-Path $root 'dist') -Filter 'Shazim-Setup-*.exe' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($Rebuild -or -not $setup) {
    & (Join-Path $PSScriptRoot 'release.ps1')
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    $setup = Get-ChildItem (Join-Path $root 'dist') -Filter 'Shazim-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

$installArgs = @('--setup')
if ($Quiet) { $installArgs += '--quiet' }
Start-Process $setup.FullName -ArgumentList $installArgs
