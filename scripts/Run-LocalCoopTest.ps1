[CmdletBinding()]
param(
    [string]$HostPath = 'C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2',
    [string]$ClientPath = 'D:\GK2Coop-FullClient',
    [string]$Configuration = 'Release',
    [int]$TimeoutSeconds = 420,
    [switch]$SkipInstall,
    [switch]$KeepRunning
)

# Runs a full two-player session on this machine, with no second PC and no VM.
#
# The game refuses to run twice from one directory, but two copies in different directories
# run side by side quite happily. The client copy is an ordinary clone of the install
# (Setup-LocalCoopClient.ps1 creates it). Each instance keeps its own BepInEx config and log,
# and the client relays its log to the host, so the host log holds both sides.
#
# [Testing] AutoStartNewGame presses New Game on each instance's behalf — it invokes the same
# MainGame.StartNewGame() the menu button does, so the normal startup flow is preserved.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$gameExeName = if (Test-Path -LiteralPath (Join-Path $HostPath 'GraveyardKeeper2.exe')) { 'GraveyardKeeper2.exe' } else { 'GraveyardKeeper2Demo.exe' }
$processName = [IO.Path]::GetFileNameWithoutExtension($gameExeName)

function Stop-Instances {
    Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

foreach ($path in @($HostPath, $ClientPath)) {
    if (-not (Test-Path -LiteralPath (Join-Path $path $gameExeName))) {
        throw "Not a game install: $path. Run scripts\Setup-LocalCoopClient.ps1 first."
    }
}

Stop-Instances

if (-not $SkipInstall) {
    & (Join-Path $PSScriptRoot 'Install-Mod.ps1') -GamePath $ClientPath -Configuration $Configuration | Out-Null
    # Installed last so the deployment manifest describes the real host install, not the clone.
    & (Join-Path $PSScriptRoot 'Install-Mod.ps1') -GamePath $HostPath -Configuration $Configuration | Out-Null
    Write-Host 'Installed the current build into both instances.'
}

function Set-CoopConfig([string]$GamePath, [string]$Mode, [string]$Address, [string]$PlayerName) {
    $configPath = Join-Path $GamePath 'BepInEx\config\com.fabio.gk2coop.cfg'
    if (-not (Test-Path -LiteralPath $configPath)) {
        Copy-Item (Join-Path $projectRoot 'package\com.fabio.gk2coop.cfg') $configPath -Force
    }
    $config = Get-Content -LiteralPath $configPath -Raw
    $config = [regex]::Replace($config, '(?m)^StartupMode\s*=.*$', "StartupMode = $Mode")
    $config = [regex]::Replace($config, '(?m)^Address\s*=.*$', "Address = $Address")
    $config = [regex]::Replace($config, '(?m)^PlayerName\s*=.*$', "PlayerName = $PlayerName")
    if ($config -match '(?m)^RelaxStartupGate\s*=') {
        $config = [regex]::Replace($config, '(?m)^RelaxStartupGate\s*=.*$', 'RelaxStartupGate = true')
    } else {
        $config += "`r`n[Testing]`r`nRelaxStartupGate = true`r`n"
    }
    if ($config -match '(?m)^AutoStartNewGame\s*=') {
        $config = [regex]::Replace($config, '(?m)^AutoStartNewGame\s*=.*$', 'AutoStartNewGame = true')
    } else {
        $config += "`r`n[Testing]`r`nAutoStartNewGame = true`r`n"
    }
    Set-Content -LiteralPath $configPath -Value $config -Encoding UTF8
}

# The host install is the one the user plays and tests the VM with, so its config is put back
# exactly as found. Leaving AutoStartNewGame or RelaxStartupGate enabled there would silently
# skip the menu and the intro on their next real session.
$hostConfigPath = Join-Path $HostPath 'BepInEx\config\com.fabio.gk2coop.cfg'
$hostConfigBackup = if (Test-Path -LiteralPath $hostConfigPath) { [IO.File]::ReadAllBytes($hostConfigPath) } else { $null }
$backupPath = Join-Path $projectRoot 'artifacts\local-test-host-config.backup.cfg'
if ($null -ne $hostConfigBackup) { Copy-Item -LiteralPath $hostConfigPath -Destination $backupPath -Force }

try {

# 0.0.0.0 keeps the host reachable from the Hyper-V guest as well as from 127.0.0.1.
Set-CoopConfig -GamePath $HostPath   -Mode 'Host'    -Address '0.0.0.0'   -PlayerName 'LocalHost'
Set-CoopConfig -GamePath $ClientPath -Mode 'Connect' -Address '127.0.0.1' -PlayerName 'LocalClient'

$hostLog = Join-Path $HostPath 'BepInEx\LogOutput.log'
Remove-Item -LiteralPath $hostLog -Force -ErrorAction SilentlyContinue

Start-Process -FilePath (Join-Path $HostPath $gameExeName) -WorkingDirectory $HostPath -WindowStyle Hidden
Start-Sleep -Seconds 10
Start-Process -FilePath (Join-Path $ClientPath $gameExeName) -WorkingDirectory $ClientPath -WindowStyle Hidden
Write-Host 'Both instances launched; waiting for them to connect.'

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$connected = $false
while ((Get-Date) -lt $deadline) {
    if ((Test-Path -LiteralPath $hostLog) -and (Select-String -LiteralPath $hostLog -Pattern 'peers=1' -Quiet)) {
        $connected = $true
        break
    }
    if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -lt 2) {
        Write-Warning 'An instance exited before connecting.'
        break
    }
    Start-Sleep -Seconds 5
}

Write-Host ''
if ($connected) { Write-Host 'CONNECTED' -ForegroundColor Green } else { Write-Warning "Not connected within $TimeoutSeconds s" }

if (Test-Path -LiteralPath $hostLog) {
    Write-Host ''
    Write-Host '--- session summary (host log) ---'
    $patterns = 'self-test', 'joined as', 'accepted the connection', 'Assigned stable GUID',
                'Remote body initialized', 'Neutralised proxy', 'Tag geometry', 'tool sync',
                'player context', 'Clock corrected', 'still open at end of frame'
    foreach ($pattern in $patterns) {
        $hits = Select-String -LiteralPath $hostLog -Pattern $pattern | Select-Object -Last 2
        foreach ($hit in $hits) { Write-Host ('  ' + ($hit.Line -replace '^.*?Prototype\] ', '')) }
    }
    Write-Host ''
    Write-Host '--- errors ---'
    $errors = Select-String -LiteralPath $hostLog -Pattern '^\[(Error|Warning)' |
        Where-Object { $_.Line -notmatch 'SteamAPI|duplicate|not readable|Non-Legacy|BoxCollider' } |
        Select-Object -Last 10
    if ($errors) { $errors | ForEach-Object { Write-Host ('  ' + $_.Line) } } else { Write-Host '  none' }
}

}
finally {
    if (-not $KeepRunning) { Stop-Instances }
    if ($null -ne $hostConfigBackup) {
        # Bytes, not text: a text round trip drops a UTF-8 BOM and the hash check below then fails.
        [IO.File]::WriteAllBytes($hostConfigPath, $hostConfigBackup)
        if ((Get-FileHash -LiteralPath $hostConfigPath).Hash -ne (Get-FileHash -LiteralPath $backupPath).Hash) {
            throw "Host config restore failed; backup is at $backupPath"
        }
        Write-Host 'Host config restored to its pre-test state.'
    }
}
