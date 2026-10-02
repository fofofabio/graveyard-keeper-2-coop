<#
.SYNOPSIS
    Plays the co-op playground on the two test copies: a day-18 world with a test yard at home
    (chest, stations, autopsy table and graves with bodies, a garden bed, a zombie at work) and
    Pause > Co-op > Test tools for everything else.

.DESCRIPTION
    Both windows open side by side on this desktop. In the first (host), press Continue: the
    playground loads and hosting starts by itself. In the second (joiner), open Co-op > Join a
    game > Join by address and press 127.0.0.1:8889 under the recent addresses.

    The playground's games keep their saves in D:\GK2Coop-Saves\playground (GK2COOP_TEST_SAVE_FOLDER),
    never in your own save folder; the host copy's Continue opens the playground through
    [Testing] ContinueSlot. When both windows are closed, everything is put back: the playground slot and the joiner's
    copy go to artifacts\playground\sessions\<time>, the test copies' settings and the game's
    preferences are restored, and your own saves are checked by hash.

    If the script was stopped before its cleanup (window closed, PC restarted), run it again with
    -Cleanup.

.PARAMETER NoJoiner
    Only the host window.

.PARAMETER Cleanup
    Only put back what an interrupted session left behind.
#>
[CmdletBinding()]
param(
    [string]$HostPath = 'D:\GK2Coop-FullHost',
    [string]$ClientPath = 'D:\GK2Coop-FullClient',
    [switch]$NoJoiner,
    [switch]$NoDeploy,
    [switch]$Cleanup,
    # Checks the set-up and the clean-up without starting the games.
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'GameplayProbe\TestSafety.ps1')

# Your own saves: only hashed. The playground's games use their own folder.
$realSaveFolder = Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2'
$saveFolder = 'D:\GK2Coop-Saves\playground'
New-Item -ItemType Directory -Force -Path $saveFolder | Out-Null
$slot = 'GK2Coop_Playground'
$source = Join-Path $root 'artifacts\playground'
$state = Join-Path $source 'session-in-progress.json'
$built = Join-Path $root 'src\GK2Coop\bin\Release\net472\GK2Coop.dll'
$paths = @($HostPath)
if (-not $NoJoiner) { $paths += $ClientPath }

function ConfigPath([string]$path) { Join-Path $path 'BepInEx\config\com.fabio.gk2coop.cfg' }

# Sets one value in a BepInEx config: the line if it is there, else under its section.
function Set-Cfg([string]$text, [string]$section, [string]$key, [string]$value) {
    if ($text -match "(?m)^$key\s*=") { return [regex]::Replace($text, "(?m)^$key\s*=.*$", "$key = $value") }
    if ($text -match "(?m)^\[$section\]\s*$") { return [regex]::Replace($text, "(?m)^\[$section\]\s*$", "[$section]`r`n`r`n$key = $value") }
    return $text + "`r`n[$section]`r`n`r`n$key = $value`r`n"
}

function Restore-Session($saved) {
    $out = $saved.Folder
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    # The playground slot as played, its backups, and the joiner's copy of the world.
    foreach ($file in Get-ChildItem -LiteralPath $saveFolder -File | Where-Object { $_.Name -like "$slot*" -or ($_.BaseName -match '^GK2Coop_[0-9a-f]{16}$' -and $_.BaseName -notin $saved.CoopSlots) }) {
        Move-Item -LiteralPath $file.FullName -Destination (Join-Path $out $file.Name) -Force
    }
    foreach ($copy in $saved.Configs) {
        if (Test-Path -LiteralPath $copy.Backup) { Copy-Item -LiteralPath $copy.Backup -Destination $copy.Path -Force }
    }
    $prefs = @{}
    foreach ($p in $saved.Prefs.PSObject.Properties) {
        $kind = [Microsoft.Win32.RegistryValueKind]$p.Value.Kind
        $text = [string]$p.Value.Value
        $value = switch ($kind) {
            'Binary' { [Convert]::FromBase64String($text) }
            'DWord' { [int32]::Parse($text, [Globalization.CultureInfo]::InvariantCulture) }
            'QWord' { [int64]::Parse($text, [Globalization.CultureInfo]::InvariantCulture) }
            default { $text }
        }
        $prefs[$p.Name] = @($value, $kind)
    }
    Write-Host "Game preferences restored: $(Restore-GamePrefs $prefs) value(s)"
    $changed = 0
    foreach ($h in $saved.SaveHashes.PSObject.Properties) {
        $file = Join-Path $realSaveFolder $h.Name
        if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ne $h.Value) { $changed++; Write-Host "Your save changed during the session: $($h.Name)" }
    }
    $total = @($saved.SaveHashes.PSObject.Properties).Count
    Write-Host "Your saves: $($total - $changed) unchanged, $changed changed."
    Remove-Item -LiteralPath $state -Force
    Write-Host "Session kept in $out"
}

if ($Cleanup) {
    if (-not (Test-Path -LiteralPath $state)) { Write-Host 'Nothing to clean up.'; return }
    if ((Get-TestGameProcesses @($HostPath, $ClientPath)).Count -gt 0) { throw 'Close the playground windows first.' }
    Restore-Session (Get-Content -LiteralPath $state -Raw | ConvertFrom-Json)
    return
}

# ---------------------------------------------------------------- checks
foreach ($path in @($HostPath, $ClientPath)) { Assert-TestInstall $path }
if (Test-Path -LiteralPath $state) { throw "A previous session was not cleaned up. Run: .\tests\Start-Playground.ps1 -Cleanup" }
if ((Get-TestGameProcesses @($HostPath, $ClientPath)).Count -gt 0) { throw 'A test copy of the game is running (a test?). Try again when it has finished.' }
foreach ($path in $paths) { if (Test-Path -LiteralPath (Join-Path $path 'BepInEx\plugins\GameplayProbe.dll')) { throw "A test probe is installed in $path (a test is running or was interrupted)." } }
if (-not (Test-Path -LiteralPath (Join-Path $source "$slot.dat"))) { throw "No playground save in $source. Build one: tests\GameplayProbe\Verify-ProgressedCoop.ps1 -BuildPlayground" }
if (Get-ChildItem -LiteralPath $saveFolder -Filter "$slot*" -File -ErrorAction SilentlyContinue) { throw "A playground slot is already in the save folder; run -Cleanup first." }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$session = [ordered]@{
    Folder = Join-Path $source "sessions\$stamp"
    Configs = @()
    CoopSlots = @(Get-ChildItem -LiteralPath $saveFolder -Filter 'GK2Coop_*.dat' -File -ErrorAction SilentlyContinue | ForEach-Object BaseName)
    SaveHashes = [ordered]@{}
    Prefs = [ordered]@{}
}
New-Item -ItemType Directory -Force -Path $session.Folder | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $realSaveFolder -File | Where-Object { $_.Name -notlike 'GK2Coop_*' -and $_.Extension -in '.dat','.info' }) { $session.SaveHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash }
$prefs = Save-GamePrefs
foreach ($name in $prefs.Keys) {
    $value = $prefs[$name][0]
    if ($value -is [byte[]]) { $value = [Convert]::ToBase64String($value) } else { $value = [Convert]::ToString($value, [Globalization.CultureInfo]::InvariantCulture) }
    $session.Prefs[$name] = [ordered]@{ Value = $value; Kind = [string]$prefs[$name][1] }
}
foreach ($path in @($HostPath, $ClientPath)) {
    $backup = Join-Path $session.Folder ((Split-Path -Leaf $path) + '.cfg')
    Copy-Item -LiteralPath (ConfigPath $path) -Destination $backup
    $session.Configs += [ordered]@{ Path = (ConfigPath $path); Backup = $backup }
}
# Written before anything changes, so -Cleanup can always put it back.
$session | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $state -Encoding UTF8

try {
    # ---------------------------------------------------------------- set up
    if (-not $NoDeploy -and (Test-Path -LiteralPath $built)) {
        foreach ($path in $paths) { Copy-Item -LiteralPath $built -Destination (Join-Path $path 'BepInEx\plugins\GK2Coop\GK2Coop.dll') -Force }
        Write-Host "Mod on the test copies: $((Get-Item $built).VersionInfo.ProductVersion)"
    }
    $hostCfg = Get-Content -LiteralPath (ConfigPath $HostPath) -Raw
    $hostCfg = Set-Cfg $hostCfg 'Network' 'StartupMode' 'Host'
    $hostCfg = Set-Cfg $hostCfg 'Testing' 'TestTools' 'true'
    $hostCfg = Set-Cfg $hostCfg 'Testing' 'ContinueSlot' $slot
    [IO.File]::WriteAllText((ConfigPath $HostPath), $hostCfg)
    if (-not $NoJoiner) {
        $clientCfg = Get-Content -LiteralPath (ConfigPath $ClientPath) -Raw
        $clientCfg = Set-Cfg $clientCfg 'Network' 'StartupMode' 'None'
        $clientCfg = Set-Cfg $clientCfg 'Testing' 'TestTools' 'true'
        $clientCfg = Set-Cfg $clientCfg 'Coop' 'RecentAddresses' '127.0.0.1:8889'
        [IO.File]::WriteAllText((ConfigPath $ClientPath), $clientCfg)
    }
    # Old on purpose: only the host test copy's Continue opens it (ContinueSlot), never yours.
    Copy-Item -LiteralPath (Join-Path $source "$slot.dat") -Destination (Join-Path $saveFolder "$slot.dat")
    $info = Get-Content -LiteralPath (Join-Path $source "$slot.info") -Raw
    $info = [regex]::Replace($info, '"saveDateTime":"[^"]+"', '"saveDateTime":"' + '01.01.2000 00:00:00' + '"')
    [IO.File]::WriteAllText((Join-Path $saveFolder "$slot.info"), $info)

    if ($DryRun) {
        Write-Host "Dry run: set up. Host config: $((Select-String -LiteralPath (ConfigPath $HostPath) -Pattern '^(StartupMode|TestTools|ContinueSlot)\s*=' | ForEach-Object Line) -join ', ')"
        if (-not $NoJoiner) { Write-Host "Joiner config: $((Select-String -LiteralPath (ConfigPath $ClientPath) -Pattern '^(StartupMode|TestTools|RecentAddresses)\s*=' | ForEach-Object Line) -join ', ')" }
        Write-Host "Slot: $((Get-ChildItem -LiteralPath $saveFolder -Filter "$slot*" | ForEach-Object Name) -join ', ')"
        return
    }
    # ---------------------------------------------------------------- play
    $window = '-screen-fullscreen 0 -window-mode windowed -screen-width 1280 -screen-height 720'
    $env:GK2COOP_TEST_SAVE_FOLDER = $saveFolder
    $running = @(Start-Process -FilePath (Join-Path $HostPath 'GraveyardKeeper2.exe') -WorkingDirectory $HostPath -ArgumentList $window -PassThru)
    if (-not $NoJoiner) {
        Start-Sleep -Seconds 5
        $running += Start-Process -FilePath (Join-Path $ClientPath 'GraveyardKeeper2.exe') -WorkingDirectory $ClientPath -ArgumentList $window -PassThru
    }
    Write-Host ''
    Write-Host 'Playground is starting.'
    Write-Host '  Host window:   Continue  (hosting starts by itself)'
    if (-not $NoJoiner) { Write-Host '  Second window: Co-op > Join a game > Join by address > 127.0.0.1:8889' }
    Write-Host '  Test tools:    Pause > Co-op > Test tools'
    Write-Host 'Waiting for the windows to close...'
    foreach ($process in $running) { $process.WaitForExit() }
}
finally {
    foreach ($process in (Get-TestGameProcesses @($HostPath, $ClientPath))) { $process.WaitForExit(15000) | Out-Null }
    if ((Get-TestGameProcesses @($HostPath, $ClientPath)).Count -eq 0) {
        Restore-Session (Get-Content -LiteralPath $state -Raw | ConvertFrom-Json)
    } else {
        Write-Host 'A playground window is still open. After closing it, run: .\tests\Start-Playground.ps1 -Cleanup'
    }
}
