<#
.SYNOPSIS  Builds the release installer: dist\Shazim-Setup-<version>.exe
.DESCRIPTION
    One self-contained, single-file executable (no .NET needed on the target PC).
    Not compressed: the window shows in ~1.5 s instead of ~2.5 s (measured), for a bigger file.
    Named Shazim-Setup*.exe it opens the installer; once installed it is Shazim itself.
    Also writes a SHA-256 checksum next to it.
#>
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scripts\common.ps1"

[xml]$proj = Get-Content $appProject
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$dist = Join-Path $root 'dist'
$staging = Join-Path $env:TEMP "shazim-release-$PID"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

Write-Host "Building Shazim $version (single file, win-x64)..." -ForegroundColor Cyan
& $dotnet publish $appProject -c Release -r win-x64 --self-contained true -o $staging -nologo -v q `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=false `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:GenerateDocumentationFile=false
if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed." -ForegroundColor Red; exit $LASTEXITCODE }

New-Item -ItemType Directory -Force $dist | Out-Null
$setup = Join-Path $dist "Shazim-Setup-$version.exe"
Copy-Item (Join-Path $staging 'Shazim.exe') $setup -Force
Remove-Item $staging -Recurse -Force

$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $setup -Leaf)" | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "OK  $setup  ($mb MB)" -ForegroundColor Green
Write-Host "SHA-256  $hash"

# Sign the checksums with the release key. The app refuses updates without a valid signature.
# The private key lives outside the repository (back it up!); override with $env:SHAZIM_SIGNING_KEY.
$key = if ($env:SHAZIM_SIGNING_KEY) { $env:SHAZIM_SIGNING_KEY } else { Join-Path $env:APPDATA 'Shazim-Release\update-signing-key.pem' }
$sums = Join-Path $dist 'SHA256SUMS.txt'
Remove-Item "$sums.sig" -ErrorAction SilentlyContinue
if (Test-Path $key) {
    $signer = Join-Path $root 'tools\ReleaseSigner\ReleaseSigner.csproj'
    & $dotnet run --project $signer -c Release -- sign $key $sums | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "Signing failed." -ForegroundColor Red; exit 1 }
    Write-Host "Signed   SHA256SUMS.txt.sig" -ForegroundColor Green
} elseif ($env:CI) {
    Write-Host "No signing key on CI: build only, not publishable." -ForegroundColor Yellow
} else {
    Write-Host "Signing key not found ($key): this build cannot be published as an update." -ForegroundColor Red
    exit 1
}
