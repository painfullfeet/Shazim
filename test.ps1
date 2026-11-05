<#
.SYNOPSIS  Runs the unit tests.
.EXAMPLE   ./test.ps1 -Filter "FullyQualifiedName~Signature"
#>
param(
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
    [string]$Filter = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scripts\common.ps1"

$testArgs = @('test', $testProject, '-c', $Configuration, '-nologo')
if ($Filter) { $testArgs += @('--filter', $Filter) }
& $dotnet @testArgs
if ($LASTEXITCODE -eq 0) { Write-Host "All tests passed." -ForegroundColor Green } else { exit $LASTEXITCODE }
