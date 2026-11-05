<#
.SYNOPSIS  Produces a self-contained build in .\publish (no .NET install needed on the target PC).
.DESCRIPTION
    Publishes to a staging folder first, then copies only the files that changed. A file still
    held open by a running Shazim is skipped when it is identical, so publishing never fails
    just because the app was left open.
#>
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scripts\common.ps1"

$out = Join-Path $PSScriptRoot 'publish'
$staging = Join-Path $env:TEMP "shazim-publish-$PID"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

& $dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $staging -nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed." -ForegroundColor Red; exit $LASTEXITCODE }

New-Item -ItemType Directory -Force $out | Out-Null
$locked = @()
foreach ($file in Get-ChildItem $staging -Recurse -File) {
    $rel = $file.FullName.Substring($staging.Length + 1)
    $dest = Join-Path $out $rel
    if (Test-Path $dest) {
        $same = (Get-Item $dest).Length -eq $file.Length -and
                (Get-FileHash $dest -Algorithm MD5).Hash -eq (Get-FileHash $file.FullName -Algorithm MD5).Hash
        if ($same) { continue }
    }
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    try { Copy-Item $file.FullName $dest -Force }
    catch {
        # A running exe/dll cannot be overwritten but can be renamed: set it aside, then copy.
        try {
            Rename-Item $dest ("{0}.old-{1}" -f (Split-Path $dest -Leaf), [guid]::NewGuid().ToString('N').Substring(0, 6)) -Force
            Copy-Item $file.FullName $dest -Force
        }
        catch { $locked += $rel }
    }
}

# Remove files that are no longer part of the build.
Get-ChildItem $out -Recurse -File -Filter '*.old-*' | ForEach-Object { try { Remove-Item $_.FullName -Force } catch { } }
foreach ($old in Get-ChildItem $out -Recurse -File) {
    $rel = $old.FullName.Substring($out.Length + 1)
    if (-not (Test-Path (Join-Path $staging $rel))) { try { Remove-Item $old.FullName -Force } catch { } }
}
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue

if ($locked.Count -gt 0) {
    Write-Host "Fichiers verrouillés par un Shazim encore ouvert, non mis à jour : $($locked -join ', ')" -ForegroundColor Yellow
    Write-Host "Quittez Shazim (clic droit sur l'icône près de l'horloge → Quitter) puis relancez ./publish.ps1." -ForegroundColor Yellow
    exit 1
}
Write-Host "Published to $out\Shazim.exe" -ForegroundColor Green
