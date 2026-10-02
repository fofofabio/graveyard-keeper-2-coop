<#
.SYNOPSIS
    Gate G8: two different mod versions meet. One side of a pair of D: copies gets an older
    GK2Coop.dll (by default the 0.60.0 backup, protocol 28); the joiner tries to join from the menu,
    and what each player is told is recorded (status line, log, screenshot).

.DESCRIPTION
    -Old OldHost: an old host, the current joiner: the joiner should explain it in its own language
    with its version. -Old OldClient: an old joiner, the current host: the old joiner shows the
    host's sentence, which names the host's version since 0.62.1. Everything is put back afterwards:
    both copies' DLLs and configs, the probe, the test slot, the game's preferences; the player's
    own saves are hashed before and after.
#>
[CmdletBinding()]
param(
    [ValidateSet('OldHost', 'OldClient')][string]$Old = 'OldHost',
    [string]$HostPath = 'D:\GK2Coop-FullHost2',
    [string]$ClientPath = 'D:\GK2Coop-FullClient2',
    [string]$OldDll = '',
    [string]$SaveBackup = 'C:\FF\graveyard-keeper-2-coop\artifacts\save-backups\full-release-20260923-071217',
    [string]$OutputPath = ('D:\GK2Coop-Artifacts\mismatch-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameplayProbe\TestSafety.ps1')
$root = Split-Path -Parent $PSScriptRoot
if (-not $OldDll) {
    $backup = Get-ChildItem (Join-Path $root 'artifacts\deployment-backups') -Directory -Filter 'steam-before-0.62.0-*' | Sort-Object Name | Select-Object -Last 1
    $OldDll = Join-Path $backup.FullName 'GK2Coop\GK2Coop.dll'
}
$probeDll = Join-Path $PSScriptRoot 'GameplayProbe\bin\Release\net472\GameplayProbe.dll'
$realSaveFolder = Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2'
$hostSaves = Join-Path 'D:\GK2Coop-Saves' (Split-Path -Leaf $HostPath)
$clientSaves = Join-Path 'D:\GK2Coop-Saves' (Split-Path -Leaf $ClientPath)
$slot = 'GK2Coop_Test_' + (Get-Date -Format 'yyyyMMddHHmmss') + '_mismatch'
New-Item -ItemType Directory -Force -Path $OutputPath, $hostSaves, $clientSaves | Out-Null
$copies = @{ Host = $HostPath; Client = $ClientPath }

function Check([bool]$passed, [string]$label) {
    $line = $(if ($passed) { 'PASS ' } else { 'FAIL ' }) + $label
    Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Value $line
    Write-Host $line
    if (-not $passed) { throw $line }
}
function Set-Cfg([string]$text, [string]$key, [string]$value) {
    if ($text -match "(?m)^$key\s*=") { return [regex]::Replace($text, "(?m)^$key\s*=.*$", "$key = $value") }
    return $text
}
function ReadShared([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $stream = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try { (New-Object IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Close() }
}
function WaitForLog([string]$path, [string]$pattern, [int]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) { if ((ReadShared $path) -match $pattern) { return }; Start-Sleep -Seconds 2 }
    throw "Timed out waiting for $what`: $pattern"
}
function Probe([string]$peer, [string]$command) { & (Join-Path $PSScriptRoot 'GameplayProbe\Invoke-Probe.ps1') -Peer $peer -Command $command -TimeoutSeconds 20 }

# ---------------------------------------------------------------- checks before
foreach ($copy in $copies.Values) {
    Assert-TestInstall $copy
    if (Test-Path -LiteralPath (Join-Path $copy 'BepInEx\plugins\GameplayProbe.dll')) { throw "A probe is installed in $copy (a test is running or was interrupted)." }
}
if ((Get-TestGameProcesses @($HostPath, $ClientPath)).Count -gt 0) { throw 'A test copy of this pair is running.' }
if (-not (Test-Path -LiteralPath $OldDll)) { throw "No old DLL at $OldDll" }
$oldVersion = (Get-Item -LiteralPath $OldDll).VersionInfo.ProductVersion
$newVersion = (Get-Item -LiteralPath (Join-Path $HostPath 'BepInEx\plugins\GK2Coop\GK2Coop.dll')).VersionInfo.ProductVersion
Write-Host "Old side: $Old ($oldVersion); current: $newVersion"
$saveHashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $realSaveFolder -File | Where-Object { $_.Name -notlike 'GK2Coop_*' -and $_.Extension -in '.dat', '.info' }) { $saveHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash }
$prefs = Save-GamePrefs
foreach ($side in $copies.Keys) {
    Copy-Item -LiteralPath (Join-Path $copies[$side] 'BepInEx\config\com.fabio.gk2coop.cfg') -Destination (Join-Path $OutputPath "$side-config-before.cfg")
    Copy-Item -LiteralPath (Join-Path $copies[$side] 'BepInEx\plugins\GK2Coop\GK2Coop.dll') -Destination (Join-Path $OutputPath "$side-GK2Coop-before.dll")
}

$processes = @()
try {
    # ---------------------------------------------------------------- set up
    Copy-Item -LiteralPath (Join-Path $SaveBackup 'Steam_1.dat') -Destination (Join-Path $hostSaves "$slot.dat")
    $info = [regex]::Replace((Get-Content -LiteralPath (Join-Path $SaveBackup 'Steam_1.info') -Raw), '"saveDateTime":"[^"]+"', '"saveDateTime":"01.01.2000 00:00:00"')
    [IO.File]::WriteAllText((Join-Path $hostSaves "$slot.info"), $info)
    foreach ($copy in $copies.Values) { Copy-Item -LiteralPath $probeDll -Destination (Join-Path $copy 'BepInEx\plugins\GameplayProbe.dll') }
    $oldSide = if ($Old -eq 'OldHost') { 'Host' } else { 'Client' }
    Copy-Item -LiteralPath $OldDll -Destination (Join-Path $copies[$oldSide] 'BepInEx\plugins\GK2Coop\GK2Coop.dll') -Force
    $hostCfgPath = Join-Path $HostPath 'BepInEx\config\com.fabio.gk2coop.cfg'
    $hostCfg = Get-Content -LiteralPath $hostCfgPath -Raw
    foreach ($pair in @(@('StartupMode', 'Host'), @('Address', '0.0.0.0'), @('AutoStartNewGame', 'false'), @('PlayerName', 'Host'), @('Transport', 'IP'))) { $hostCfg = Set-Cfg $hostCfg $pair[0] $pair[1] }
    [IO.File]::WriteAllText($hostCfgPath, $hostCfg)
    $hostPort = [regex]::Match($hostCfg, '(?m)^Port\s*=\s*(\d+)').Groups[1].Value
    $clientCfgPath = Join-Path $ClientPath 'BepInEx\config\com.fabio.gk2coop.cfg'
    $clientCfg = Get-Content -LiteralPath $clientCfgPath -Raw
    foreach ($pair in @(@('StartupMode', 'None'), @('AutoStartNewGame', 'false'), @('PlayerName', 'Client'), @('Transport', 'IP'))) { $clientCfg = Set-Cfg $clientCfg $pair[0] $pair[1] }
    [IO.File]::WriteAllText($clientCfgPath, $clientCfg)
    foreach ($copy in $copies.Values) { $log = Join-Path $copy 'BepInEx\LogOutput.log'; if (Test-Path -LiteralPath $log) { Move-Item -LiteralPath $log -Destination (Join-Path $OutputPath ("$(Split-Path -Leaf $copy)-log-before.txt")) -Force } }

    # ---------------------------------------------------------------- host, then the joiner from its menu
    $env:GK2COOP_TEST_HOST_PATH = $HostPath
    $env:GK2COOP_TEST_CLIENT_PATH = $ClientPath
    $env:GK2COOP_TEST_LANG = 'de'
    $env:GK2COOP_TEST_SAVE_FOLDER = $hostSaves
    $env:GK2COOP_TEST_CONTINUE_SLOT = $slot
    $processes += Start-TestGame $HostPath
    WaitForLog (Join-Path $HostPath 'BepInEx\LogOutput.log') 'Attached native host networking' 240 'the host'
    [Environment]::SetEnvironmentVariable('GK2COOP_TEST_CONTINUE_SLOT', $null)
    $env:GK2COOP_TEST_SAVE_FOLDER = $clientSaves
    $env:GK2COOP_TEST_MENU_CONNECT = '1'
    $processes += Start-TestGame $ClientPath
    $clientLog = Join-Path $ClientPath 'BepInEx\LogOutput.log'
    WaitForLog $clientLog 'gameState=MainMenu' 240 'the joiner at its main menu'
    Start-Sleep -Seconds 5
    $bootstrap = Probe Client "menu-bootstrap|127.0.0.1|$hostPort"
    Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Value "bootstrap: $(($bootstrap -split "`n") -join ' ')"
    WaitForLog $clientLog 'Save bootstrap failed' 90 'the joiner being refused'
    Start-Sleep -Seconds 2
    $status = ((Probe Client 'status-ui') -split "`n" | Where-Object { $_ -like 'STATUS-UI*' }) -join ' '
    Probe Client "shot|$(Join-Path $OutputPath 'joiner-refused.png')" | Out-Null
    $failed = ([regex]::Matches((ReadShared $clientLog), 'Save bootstrap failed: [^\r\n]*') | Select-Object -Last 1).Value
    $hostSaw = ([regex]::Matches((ReadShared (Join-Path $HostPath 'BepInEx\LogOutput.log')), 'Could not start save bootstrap for client[^\r\n]*') | Select-Object -Last 1).Value
    Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Value "joiner: $failed`nstatus: $status`nhost: $hostSaw"
    if ($Old -eq 'OldHost') {
        Check ($failed -match [regex]::Escape($newVersion) -and $failed -match 'Version') "A current joiner meeting an old host ($oldVersion) is told in its language, with its version ($failed)"
    } else {
        Check ($failed -match 'same co-op mod version' -and $failed -match [regex]::Escape($newVersion)) "An old joiner ($oldVersion) meeting a current host is told the host's version ($failed)"
    }
    Check ($hostSaw -match 'same co-op mod version') "The host refused the other version and carried on ($hostSaw)"
}
finally {
    foreach ($name in 'GK2COOP_TEST_HOST_PATH', 'GK2COOP_TEST_CLIENT_PATH', 'GK2COOP_TEST_LANG', 'GK2COOP_TEST_SAVE_FOLDER', 'GK2COOP_TEST_CONTINUE_SLOT', 'GK2COOP_TEST_MENU_CONNECT') { [Environment]::SetEnvironmentVariable($name, $null) }
    foreach ($process in $processes) { if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit(10000) | Out-Null } }
    foreach ($side in $copies.Keys) {
        $copy = $copies[$side]
        $log = Join-Path $copy 'BepInEx\LogOutput.log'
        if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath "$side-log.txt") -Force }
        Copy-Item -LiteralPath (Join-Path $OutputPath "$side-GK2Coop-before.dll") -Destination (Join-Path $copy 'BepInEx\plugins\GK2Coop\GK2Coop.dll') -Force
        Copy-Item -LiteralPath (Join-Path $OutputPath "$side-config-before.cfg") -Destination (Join-Path $copy 'BepInEx\config\com.fabio.gk2coop.cfg') -Force
        $probe = Join-Path $copy 'BepInEx\plugins\GameplayProbe.dll'
        if (Test-Path -LiteralPath $probe) { Move-Item -LiteralPath $probe -Destination (Join-Path $OutputPath "$side-GameplayProbe.dll") -Force }
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $copy 'BepInEx') -Filter 'GK2Coop.Probe.command*' -ErrorAction SilentlyContinue) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath "$side-$($file.Name)") -Force }
    }
    foreach ($folder in $hostSaves, $clientSaves) { foreach ($file in Get-ChildItem -LiteralPath $folder -Filter "$slot*" -File -ErrorAction SilentlyContinue) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath $file.Name) -Force } }
    Write-Host "Game preferences restored: $(Restore-GamePrefs $prefs) value(s)"
    $changed = @($saveHashes.Keys | Where-Object { (Get-FileHash -LiteralPath (Join-Path $realSaveFolder $_)).Hash -ne $saveHashes[$_] })
    Write-Host "Your saves: $($saveHashes.Count - $changed.Count) unchanged$(if ($changed) { ', changed: ' + ($changed -join ', ') })"
}
