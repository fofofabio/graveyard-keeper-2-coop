[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2',
    [string]$OutputPath
)

# Builds the folder to upload as the Steam Workshop item (the game's Workshop creator uploads a
# folder). Same contents as the zip package, laid out to be copied straight into the game folder,
# with two differences:
#   - no config file: merging an update must never reset a player's name, address or port.
#     BepInEx writes the defaults on first start, and they match package\com.fabio.gk2coop.cfg.
#   - the Workshop install guide instead of the zip one.
# Files starting with '_' are skipped by the game's uploader, so _UPLOAD-NOTES.txt stays local.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $projectRoot 'artifacts'
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $artifactRoot 'workshop-item' }
$zip = Join-Path $artifactRoot 'GK2Coop-0.1.0-dev.zip'

& (Join-Path $PSScriptRoot 'Build-Package.ps1') -GamePath $GamePath | Out-Null

$resolved = [IO.Path]::GetFullPath($OutputPath)
if (-not $resolved.StartsWith([IO.Path]::GetFullPath($artifactRoot), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace a folder outside the artifact directory: $resolved"
}
if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
Expand-Archive -LiteralPath $zip -DestinationPath $resolved
Remove-Item -LiteralPath (Join-Path $resolved 'BepInEx\config') -Recurse -Force
Remove-Item -LiteralPath (Join-Path $resolved 'INSTALL-GK2COOP.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'package\WORKSHOP-INSTALL.txt') -Destination (Join-Path $resolved 'INSTALL-GK2COOP.txt')
# The Workshop copy explains installing from the Workshop, in every language.
Get-ChildItem -LiteralPath (Join-Path $resolved 'GK2Coop-Guides') -Filter 'INSTALL-GK2COOP.*.txt' | Remove-Item -Force
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'package\guides') -Filter 'WORKSHOP-INSTALL.*.txt' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $resolved ('GK2Coop-Guides\' + ($_.Name -replace '^WORKSHOP-INSTALL', 'INSTALL-GK2COOP')))
}
# The Workshop creator takes the item's preview picture from Thumbnail.png/.jpg in the folder
# (and leaves it out of the content). Without one it makes an empty black Thumbnail.png and
# uploads that, so the folder must carry the real one. It looks for .png first.
$preview = Join-Path $projectRoot 'release\media\preview.jpg'
if (-not (Test-Path -LiteralPath $preview)) { throw "No Workshop preview picture at $preview" }
if ((Get-Item -LiteralPath $preview).Length -ge 1MB) { throw "The Workshop preview must be under 1 MB: $preview" }
Copy-Item -LiteralPath $preview -Destination (Join-Path $resolved 'Thumbnail.jpg')

$dll = Join-Path $resolved 'BepInEx\plugins\GK2Coop\GK2Coop.dll'
$version = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3)
$pinned = Select-String -LiteralPath (Join-Path $projectRoot 'src\GK2Coop\CoopWorkshopUpdater.cs') -Pattern 'PublishedItemId = (\d+)' |
    ForEach-Object { $_.Matches[0].Groups[1].Value } | Select-Object -First 1
@"
Upload notes (not uploaded: the game's Workshop creator skips names starting with '_').

Version $version
Plugin SHA-256 $((Get-FileHash -LiteralPath $dll).Hash)
Pinned Workshop item id in this build: $pinned

First upload: the id is 0, so installed copies cannot update themselves yet. After the first
upload, put the new item id into CoopWorkshopUpdater.PublishedItemId, rebuild, and upload again
as an update to the same item. Every later version then reaches players who copied it in once.

Every upload with the game's Workshop creator (Shift+F11) also sets the item's description to its
title and its visibility to Unlisted. After each upload, on the item's Steam page: paste the text of
release\WORKSHOP-DESCRIPTION.txt again, and set the visibility back to Public.
"@ | Set-Content -LiteralPath (Join-Path $resolved '_UPLOAD-NOTES.txt') -Encoding UTF8

Write-Host "Workshop item folder: $resolved"
Write-Host "Version: $version; pinned item id: $pinned"
Get-ChildItem -LiteralPath $resolved -Recurse -File | Measure-Object -Property Length -Sum |
    ForEach-Object { Write-Host ("{0} files, {1:N0} bytes" -f $_.Count, $_.Sum) }
