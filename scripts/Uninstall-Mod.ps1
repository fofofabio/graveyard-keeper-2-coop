[CmdletBinding()]
param(
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Graveyard Keeper 2 Demo'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $projectRoot 'artifacts\deployment-manifest.json'
if (Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue) {
    throw 'Close Graveyard Keeper 2 Demo before uninstalling files.'
}
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw 'No deployment manifest exists. Nothing will be removed automatically.'
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$expectedRoot = [IO.Path]::GetFullPath($GamePath).TrimEnd('\') + '\'
foreach ($entry in $manifest.CreatedFiles) {
    $resolved = [IO.Path]::GetFullPath([string]$entry.Path)
    if (-not $resolved.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Manifest path is outside the expected game directory: $resolved"
    }
}

foreach ($entry in ($manifest.CreatedFiles | Sort-Object { ([string]$_.Path).Length } -Descending)) {
    if ($entry.Created -and (Test-Path -LiteralPath $entry.Path -PathType Leaf)) {
        Remove-Item -LiteralPath $entry.Path -Force
    }
}

Write-Host 'Removed files created by the recorded deployment. Generated BepInEx logs/configs were retained.'
