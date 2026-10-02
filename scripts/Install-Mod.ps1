[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2',
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$bepInExStage = Join-Path $projectRoot 'artifacts\bepinex-5.4.23.5'
$projectFile = Join-Path $projectRoot 'src\GK2Coop\GK2Coop.csproj'
$manifestPath = Join-Path $projectRoot 'artifacts\deployment-manifest.json'

$gameExe = @('GraveyardKeeper2.exe', 'GraveyardKeeper2Demo.exe') | ForEach-Object { Join-Path $GamePath $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $gameExe) { throw "No supported Graveyard Keeper 2 executable found in $GamePath" }
if (Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($gameExe)) -ErrorAction SilentlyContinue) {
    throw "Close $gameExe before installing files."
}
if (-not (Test-Path -LiteralPath (Join-Path $bepInExStage 'winhttp.dll'))) {
    throw 'The staged BepInEx distribution is missing.'
}

dotnet build $projectFile -c $Configuration -p:GamePath="$GamePath" -p:BepInExPath="$bepInExStage"
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

$entries = [System.Collections.Generic.List[object]]::new()
if (Test-Path -LiteralPath $manifestPath) {
    $previousManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([string]::Equals([IO.Path]::GetFullPath([string]$previousManifest.GamePath), [IO.Path]::GetFullPath($GamePath), [StringComparison]::OrdinalIgnoreCase)) {
        foreach ($entry in $previousManifest.CreatedFiles) {
            $entries.Add([pscustomobject]@{ Path = [string]$entry.Path; Created = [bool]$entry.Created })
        }
    }
}

function Add-OwnedFile([string]$Path) {
    if (-not ($entries | Where-Object { [string]::Equals([string]$_.Path, $Path, [StringComparison]::OrdinalIgnoreCase) })) {
        $entries.Add([pscustomobject]@{ Path = $Path; Created = $true })
    }
}
$loaderFiles = @('.doorstop_version', 'changelog.txt', 'doorstop_config.ini', 'winhttp.dll')
foreach ($relative in $loaderFiles) {
    $source = Join-Path $bepInExStage $relative
    $destination = Join-Path $GamePath $relative
    if (Test-Path -LiteralPath $destination) {
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $destinationHash) {
            throw "Refusing to overwrite existing file: $destination"
        }
    } else {
        Copy-Item -LiteralPath $source -Destination $destination
        Add-OwnedFile $destination
    }
}

$coreDestination = Join-Path $GamePath 'BepInEx\core'
New-Item -ItemType Directory -Force -Path $coreDestination | Out-Null
Get-ChildItem -LiteralPath (Join-Path $bepInExStage 'BepInEx\core') -File | ForEach-Object {
    $destination = Join-Path $coreDestination $_.Name
    if (Test-Path -LiteralPath $destination) {
        $sourceHash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $destinationHash) {
            throw "Refusing to overwrite existing file: $destination"
        }
    } else {
        Copy-Item -LiteralPath $_.FullName -Destination $destination
        Add-OwnedFile $destination
    }
}

$pluginDirectory = Join-Path $GamePath 'BepInEx\plugins\GK2Coop'
New-Item -ItemType Directory -Force -Path $pluginDirectory | Out-Null
$builtPlugin = Join-Path $projectRoot "src\GK2Coop\bin\$Configuration\net472\GK2Coop.dll"
$pluginDestination = Join-Path $pluginDirectory 'GK2Coop.dll'
if (Test-Path -LiteralPath $pluginDestination) {
    $backupDirectory = Join-Path $projectRoot 'artifacts\deployment-backups'
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    $backup = Join-Path $backupDirectory ('GK2Coop.' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.dll')
    Copy-Item -LiteralPath $pluginDestination -Destination $backup
}
Copy-Item -LiteralPath $builtPlugin -Destination $pluginDestination -Force
Add-OwnedFile $pluginDestination

[pscustomobject]@{
    InstalledAt = (Get-Date).ToString('o')
    GamePath = $GamePath
    PluginSha256 = (Get-FileHash -LiteralPath $pluginDestination -Algorithm SHA256).Hash
    CreatedFiles = $entries
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath

Write-Host "Installed GK2Coop to $pluginDestination"
Write-Host "Deployment manifest: $manifestPath"
