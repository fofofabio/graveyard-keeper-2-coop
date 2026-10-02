[CmdletBinding()]
param(
    [string]$HostPath = 'D:\SteamLibrary\steamapps\common\Graveyard Keeper 2 Demo',
    [string]$ClientPath = 'D:\GK2Coop-LocalClient',
    [Parameter(Mandatory)][string]$OutputPath
)

# Phase 5 research. Reproduces the one failure that blocks rejoining a progressed world:
# the client applying the host's actual GameSave rather than rebuilding a fresh baseline.
#
# The claim that this fails inside RecastGraphScanPromise.Apply predates any surviving log.
# This captures the real exception and stack instead of designing against a remembered one.
#
# Nothing here is a gate. It is expected to fail; the output is the evidence.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$clientConfig = Join-Path $ClientPath 'BepInEx\config\com.fabio.gk2coop.cfg'
$hostConfig = Join-Path $HostPath 'BepInEx\config\com.fabio.gk2coop.cfg'

if (@(Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'The game is running. Close it first.'
}
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$backup = Join-Path $OutputPath 'restore'
New-Item -ItemType Directory -Force -Path $backup | Out-Null
foreach ($pair in @(@('host', $hostConfig), @('client', $clientConfig))) {
    if (Test-Path -LiteralPath $pair[1]) { Copy-Item -LiteralPath $pair[1] -Destination (Join-Path $backup ($pair[0] + '-config.cfg')) -Force }
}

try {
    & (Join-Path $PSScriptRoot 'Install-Mod.ps1') -GamePath $ClientPath -Configuration Release | Out-Null
    & (Join-Path $PSScriptRoot 'Install-Mod.ps1') -GamePath $HostPath -Configuration Release | Out-Null

    # Set before the harness runs: Run-LocalCoopTest rewrites only the keys it owns and
    # preserves the rest, and BepInEx reads the config once at startup.
    $config = Get-Content -LiteralPath $clientConfig -Raw
    if ($config -match '(?m)^ApplyReceivedWorld\s*=') {
        $config = [regex]::Replace($config, '(?m)^ApplyReceivedWorld\s*=.*$', 'ApplyReceivedWorld = true')
    } else {
        $config += "`r`n[Testing]`r`nApplyReceivedWorld = true`r`n"
    }
    Set-Content -LiteralPath $clientConfig -Value $config -Encoding UTF8

    & (Join-Path $PSScriptRoot 'Run-LocalCoopTest.ps1') -HostPath $HostPath -ClientPath $ClientPath -SkipInstall -KeepRunning
    Start-Sleep -Seconds 45
}
finally {
    foreach ($pair in @(@('host', $HostPath), @('client', $ClientPath))) {
        $log = Join-Path $pair[1] 'BepInEx\LogOutput.log'
        if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath ($pair[0] + '.log')) -Force }
    }
    Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 3
    foreach ($pair in @(@('host', $hostConfig), @('client', $clientConfig))) {
        $file = Join-Path $backup ($pair[0] + '-config.cfg')
        if (Test-Path -LiteralPath $file) {
            Copy-Item -LiteralPath $file -Destination $pair[1] -Force
            $same = (Get-FileHash -Algorithm SHA256 -LiteralPath $pair[1]).Hash -eq (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash
            Write-Host ("{0} config restored: {1}" -f $pair[0], $same)
        }
    }
}

$clientLog = Join-Path $OutputPath 'client.log'
if (Test-Path -LiteralPath $clientLog) {
    Write-Host ''
    Write-Host '--- world applied, and what happened next ---'
    Select-String -LiteralPath $clientLog -Pattern 'ApplyReceivedWorld is on|Received world:|game/(Error|Exception)|game/stack|Failed to reconstruct' |
        Select-Object -First 40 | ForEach-Object { '  ' + $_.Line }
}
