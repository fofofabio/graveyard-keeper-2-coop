[CmdletBinding()]
param(
    [string]$HostPath = 'C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2',
    [string]$ClientPath = 'D:\GK2Coop-FullClient',
    [Parameter(Mandatory)][string]$OutputPath,
    # Reconnection is known to fail on this harness: a relaunched client's connection never
    # reaches the host at all, on loopback. Off by default so the suite reports what is true.
    [switch]$IncludeReconnect,
    [switch]$GracefulReconnect,
    # Control run: turn off the host's post-departure socket rebind (CoopTransportGuard) to show
    # that a killed client's reconnect fails without it.
    [switch]$DisableTransportGuard
)

if ($GracefulReconnect -and -not $IncludeReconnect) { throw 'GracefulReconnect requires IncludeReconnect.' }

# Phase 5 gate: a client joining a world that has already progressed.
#
# The existing harness launches both instances together, so both worlds start identical and a
# join snapshot would be indistinguishable from doing nothing. Here the host starts alone, the
# world is changed through real game methods, and only then does the client join.
#
# Every state check reads actual world and inventory state on the client, never a log line.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$invoke = Join-Path $PSScriptRoot 'Invoke-Probe.ps1'
$manifestPath = Join-Path $projectRoot 'artifacts\deployment-manifest.json'
$probeDll = Join-Path $PSScriptRoot 'bin\Release\net472\GameplayProbe.dll'
$gameExeName = if (Test-Path -LiteralPath (Join-Path $HostPath 'GraveyardKeeper2.exe')) { 'GraveyardKeeper2.exe' } else { 'GraveyardKeeper2Demo.exe' }
$processName = [IO.Path]::GetFileNameWithoutExtension($gameExeName)
$env:GK2COOP_TEST_HOST_PATH = $HostPath
$env:GK2COOP_TEST_CLIENT_PATH = $ClientPath
$container = 'a5d8e32d-a635-4c44-a836-1bf9e8d5fb12'
$report = [Collections.Generic.List[string]]::new()

$peers = @(
    [pscustomobject]@{ Name = 'host';   Path = $HostPath;   Mode = 'Host';    Address = '0.0.0.0';   Player = 'LocalHost' },
    [pscustomobject]@{ Name = 'client'; Path = $ClientPath; Mode = 'Connect'; Address = '127.0.0.1'; Player = 'LocalClient' }
)

function Config-Path([string]$Install) { Join-Path $Install 'BepInEx\config\com.fabio.gk2coop.cfg' }
function Probe-Path([string]$Install) { Join-Path $Install 'BepInEx\plugins\GameplayProbe.dll' }
function Stop-Instances {
    try {
        foreach ($process in [Diagnostics.Process]::GetProcessesByName($processName)) {
            try { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
            catch { Write-Warning "Could not stop test process $($process.Id): $_" }
            finally { $process.Dispose() }
        }
    }
    catch { Write-Warning "Could not enumerate test processes: $_" }
}
function Probe([string]$Peer, [string]$Command, [int]$TimeoutSeconds = 15) {
    $result = & $invoke -Peer $Peer -Command $Command -TimeoutSeconds $TimeoutSeconds
    if ($result -match '(?m)^ERROR ') { throw $result }
    return $result
}
function Snapshot([string]$Peer, [string]$Label) {
    $state = Probe $Peer 'inspect'
    [IO.File]::WriteAllText((Join-Path $OutputPath "$Label-$Peer.txt"), $state)
    return $state
}
function Check([bool]$Passed, [string]$Label) {
    $line = $(if ($Passed) { 'PASS ' } else { 'FAIL ' }) + $Label
    $report.Add($line)
    Write-Host $line
    [IO.File]::WriteAllLines((Join-Path $OutputPath 'results.txt'), $report)
}
# Invoke-Probe returns as soon as a result file exists, including an error result, so waiting
# for a world means retrying until it answers rather than passing a longer timeout once.
function Wait-World([string]$Peer, [int]$TimeoutSeconds = 300) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try { return Probe $Peer 'inspect' }
        catch { Start-Sleep -Seconds 5 }
        if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -lt 1) {
            throw "$Peer exited before its world was ready."
        }
    }
    throw "$Peer did not reach a playable world within $TimeoutSeconds s."
}

function Set-PeerConfig($Peer) {
    $path = Config-Path $Peer.Path
    if (-not (Test-Path -LiteralPath $path)) {
        Copy-Item (Join-Path $projectRoot 'package\com.fabio.gk2coop.cfg') $path -Force
    }
    $config = Get-Content -LiteralPath $path -Raw
    $settings = @{
        StartupMode = $Peer.Mode; Address = $Peer.Address; PlayerName = $Peer.Player
        AutoStartNewGame = 'true'; RelaxStartupGate = 'true'; SendJoinSnapshot = 'true'
    }
    if ($Peer.Name -eq 'host') {
        $settings.RebindAfterClientLeaves = $(if ($DisableTransportGuard) { 'false' } else { 'true' })
    }
    if ($IncludeReconnect -and $Peer.Name -eq 'client') {
        $settings.ConnectRetries = '1'
        $settings.ConnectRetrySeconds = '8'
    }
    foreach ($key in $settings.Keys) {
        if ($config -match "(?m)^$key\s*=") {
            $config = [regex]::Replace($config, "(?m)^$key\s*=.*$", "$key = $($settings[$key])")
        } elseif ($key -eq 'RebindAfterClientLeaves' -and $config -match '(?m)^\[Session\]\s*$') {
            # Newer than most installed configs; appending would land it in the wrong section.
            $config = ([regex]'(?m)^\[Session\][ \t\r]*$').Replace($config, "[Session]`r`n$key = $($settings[$key])`r", 1)
        } else {
            $config += "`r`n$key = $($settings[$key])`r`n"
        }
    }
    Set-Content -LiteralPath $path -Value $config -Encoding UTF8
}

if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'The game is running. Close it before a verification run.'
}
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$backupPath = Join-Path $OutputPath 'restore'
New-Item -ItemType Directory -Force -Path $backupPath | Out-Null
foreach ($peer in $peers) {
    if (Test-Path -LiteralPath (Config-Path $peer.Path)) {
        Copy-Item -LiteralPath (Config-Path $peer.Path) -Destination (Join-Path $backupPath "$($peer.Name)-config.cfg") -Force
    }
    if (Test-Path -LiteralPath (Probe-Path $peer.Path)) { throw "A probe DLL is already installed in $($peer.Path)." }
}
if (Test-Path -LiteralPath $manifestPath) { Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $backupPath 'deployment-manifest.json') -Force }

try {
    dotnet build (Join-Path $projectRoot 'src\GK2Coop\GK2Coop.csproj') -c Release -v q --nologo -p:GamePath="$HostPath"
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    dotnet build (Join-Path $PSScriptRoot 'GameplayProbe.csproj') -c Release -v q --nologo -p:GamePath="$HostPath"
    if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
    $install = Join-Path $projectRoot 'scripts\Install-Mod.ps1'
    & $install -GamePath $ClientPath -Configuration Release | Out-Null
    & $install -GamePath $HostPath -Configuration Release | Out-Null
    foreach ($peer in $peers) {
        Copy-Item -LiteralPath $probeDll -Destination (Probe-Path $peer.Path) -Force
        Set-PeerConfig $peer
    }

    $hostLog = Join-Path $HostPath 'BepInEx\LogOutput.log'
    Remove-Item -LiteralPath $hostLog -Force -ErrorAction SilentlyContinue

    # Stage one: the host alone, so the world can progress before anyone joins.
    Start-Process -FilePath (Join-Path $HostPath $gameExeName) -WorkingDirectory $HostPath -WindowStyle Hidden
    Write-Host 'Host launched; waiting for its world.'
    Wait-World Host | Out-Null
    # A playable world is not a finished one. Scenes are still loading additively at that point,
    # and a drop created before its scene loads is queued instead of placed. Waiting for the
    # mod's own attach line is also the realistic case: a host that has been playing.
    $attachDeadline = (Get-Date).AddSeconds(180)
    while ((Get-Date) -lt $attachDeadline) {
        if (Select-String -LiteralPath $hostLog -Pattern 'Attached native host networking' -Quiet) { break }
        Start-Sleep -Seconds 3
    }
    if (-not (Select-String -LiteralPath $hostLog -Pattern 'Attached native host networking' -Quiet)) {
        throw 'Host never attached networking.'
    }
    Start-Sleep -Seconds 5

    $killable = Probe Host 'wgos|1'
    if ($killable -notmatch 'WGOS ([0-9a-f-]+)=(\S+?)(;|$)') { throw "No destructible world object found: $killable" }
    $victim = $Matches[1]
    $victimName = $Matches[2]
    Probe Host "wgo-kill|$victim" | Out-Null
    Probe Host "container-add|$container|berry|3" | Out-Null
    $dropResult = Probe Host 'spawn|bread|2'
    if ($dropResult -notmatch 'SPAWN ([0-9a-f-]+)') { throw $dropResult }
    $dropId = $Matches[1]
    Probe Host 'quest-await|1_intro_prison_find_key' | Out-Null
    Start-Sleep -Seconds 2
    $progressed = Snapshot Host 'progressed'
    # Setup guards: a gate can only mean something if the host state it checks against is real.
    if ($progressed -notmatch "DROP $dropId bread count=2") { throw "Test setup: the drop was not placed in the host world. $progressed" }
    if ($progressed -notmatch 'chest_home size=20 items=berryx8') { throw 'Test setup: the host chest was not changed.' }
    if ((Probe Host "wgo-alive|$victim") -notmatch 'ALIVE False') { throw 'Test setup: the world object survived.' }
    Write-Host "Host world progressed: killed $victimName, chest +3 berries, drop $dropId, quest awaiting."

    # Stage two: the client joins a world that is already different from a fresh one.
    Start-Process -FilePath (Join-Path $ClientPath $gameExeName) -WorkingDirectory $ClientPath -WindowStyle Hidden
    Write-Host 'Client launched; waiting for it to connect.'
    $deadline = (Get-Date).AddSeconds(420)
    $connected = $false
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $hostLog) -and (Select-String -LiteralPath $hostLog -Pattern 'peers=1' -Quiet)) { $connected = $true; break }
        if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -lt 2) { throw 'An instance exited before connecting.' }
        Start-Sleep -Seconds 5
    }
    if (-not $connected) { throw 'Client did not connect within the timeout.' }
    Start-Sleep -Seconds 10
    $joined = Snapshot Client 'joined'

    # Asked directly, because `inspect` never lists world objects: matching its text for an
    # absent id would pass whether or not the client ever heard about the death.
    Check ((Probe Client "wgo-alive|$victim") -match 'ALIVE False') "Destroyed world object ($victimName) is absent on the joining client"
    Check ($joined -match "CONTAINER $container chest_home size=20 items=berryx8") 'Chest changed before the join is present on the client'
    Check ($joined -match "DROP $dropId bread count=2") 'Drop created before the join is present on the client with matching identity'
    Check ($joined -match '1_intro_prison_find_key Awaiting') 'Quest transition made before the join is reflected on the client'

    # Stage three: the client leaves, the world moves on without it, and it comes back.
    # Drops lying together merge: one DropView absorbs the other and that DropData is removed.
    # Only the host may decide the survivor, and the peer must end up with the same drops.
    # Merging is physics-driven and does not always happen, so several are dropped together and
    # the run states plainly when the path was never exercised rather than passing on silence.
    foreach ($extra in 1..3) { Probe Host 'spawn|bread|1' | Out-Null; Start-Sleep -Milliseconds 400 }
    Start-Sleep -Seconds 8
    $mergedHost = Snapshot Host 'merged-host'
    $mergedClient = Snapshot Client 'merged-client'
    $hostDrops = @(($mergedHost -split "`n") | Where-Object { $_ -match '^DROP .* bread ' }) | Sort-Object
    $clientDrops = @(($mergedClient -split "`n") | Where-Object { $_ -match '^DROP .* bread ' }) | Sort-Object
    $mergeCount = @(Select-String -LiteralPath $hostLog -Pattern 'Host merged drop').Count
    if ($mergeCount -eq 0) {
        $note = 'NOTE drop merging never triggered in this run; the replication path was not exercised.'
        $report.Add($note)
        Write-Host $note
        [IO.File]::WriteAllLines((Join-Path $OutputPath 'results.txt'), $report)
    } else {
        Check (($hostDrops -join '|') -eq ($clientDrops -join '|')) "After $mergeCount merge(s) both peers hold the same drops: host=[$($hostDrops -join '|')], client=[$($clientDrops -join '|')]"
    }

    if (-not $IncludeReconnect) {
        Write-Host 'Skipping the reconnection stage; pass -IncludeReconnect to run it.'
        $skipReconnect = $true
    }
    if (-not $skipReconnect) {
    if ($GracefulReconnect) {
        try { Probe Client 'quit' | Out-Null }
        catch { Write-Host "Quit command response unavailable; checking the process directly: $_" }
        $quitDeadline = (Get-Date).AddSeconds(40)
        while ((Get-Date) -lt $quitDeadline -and
            @(Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$ClientPath*" }).Count -gt 0) {
            Start-Sleep -Seconds 2
        }
    } else {
        Get-Process -Name $processName -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -like "$ClientPath*" } | Stop-Process -Force
    }
    Start-Sleep -Seconds 8
    Check (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -eq 1) 'Host survives the client disconnecting'

    Probe Host "container-add|$container|berry|2" | Out-Null
    # Physics may merge this bread into an existing stack or leave it as another drop.
    # Capture the actual host list and compare it after reconnecting.
    Probe Host 'spawn|bread|1' | Out-Null
    Start-Sleep -Seconds 2
    $afterLeaving = Snapshot Host 'after-leaving'
    $breadAfterLeaving = @(($afterLeaving -split "`n") | Where-Object { $_ -match '^DROP .* bread ' }) | Sort-Object
    if ($breadAfterLeaving.Count -eq 0) { throw "Test setup: no bread drops on the host. $afterLeaving" }
    $breadTotalAfterLeaving = ($breadAfterLeaving | ForEach-Object { if ($_ -match 'count=(\d+)') { [int]$Matches[1] } } | Measure-Object -Sum).Sum
    if ($breadTotalAfterLeaving -ne 6) { throw "Test setup: expected six bread after the host's new drop, found $breadTotalAfterLeaving." }
    if ($afterLeaving -notmatch 'chest_home size=20 items=berryx10') { throw "Test setup: the chest is not at 10. $afterLeaving" }

    Start-Process -FilePath (Join-Path $ClientPath $gameExeName) -WorkingDirectory $ClientPath -WindowStyle Hidden
    Write-Host 'Client relaunched; waiting for it to reconnect.'
    $deadline = (Get-Date).AddSeconds(420)
    $reconnected = $false
    while ((Get-Date) -lt $deadline) {
        # Counted rather than matched: 'peers=1' is already in the log from the first join.
        if ((Select-String -LiteralPath $hostLog -Pattern 'Sent the join snapshot to client').Count -ge 2) { $reconnected = $true; break }
        $clientLog = Join-Path $ClientPath 'BepInEx\LogOutput.log'
        if ((Test-Path -LiteralPath $clientLog) -and
            (Select-String -LiteralPath $clientLog -Pattern 'Gave up connecting after' -Quiet)) { break }
        if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -lt 2) { throw 'An instance exited before reconnecting.' }
        Start-Sleep -Seconds 5
    }
    Check $reconnected 'Client reconnects after disconnecting and receives a second join snapshot'
    if ($reconnected) {
        Start-Sleep -Seconds 10
        $rejoined = Snapshot Client 'rejoined'
        $rejoinedHost = Snapshot Host 'rejoined-host'
        Check ($rejoined -match "CONTAINER $container chest_home size=20 items=berryx10") 'Reconnected client sees chest changes made while it was away'
        $breadAfterRejoin = @(($rejoined -split "`n") | Where-Object { $_ -match '^DROP .* bread ' }) | Sort-Object
        $hostBreadAfterRejoin = @(($rejoinedHost -split "`n") | Where-Object { $_ -match '^DROP .* bread ' }) | Sort-Object
        $breadTotalAfterRejoin = ($breadAfterRejoin | ForEach-Object { if ($_ -match 'count=(\d+)') { [int]$Matches[1] } } | Measure-Object -Sum).Sum
        Check (($breadAfterRejoin -join '|') -eq ($hostBreadAfterRejoin -join '|') -and $breadTotalAfterRejoin -eq 6) 'Reconnected client sees all six bread, including the host drop made while it was away'
        Check ((Probe Client "wgo-alive|$victim") -match 'ALIVE False') 'Reconnected client still has the earlier death applied'
    }
    }
}
finally {
    foreach ($peer in $peers) {
        $log = Join-Path $peer.Path 'BepInEx\LogOutput.log'
        if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath "live-$($peer.Name).log") -Force }
    }
    Stop-Instances
    foreach ($peer in $peers) {
        if (Test-Path -LiteralPath (Probe-Path $peer.Path)) {
            Move-Item -LiteralPath (Probe-Path $peer.Path) -Destination (Join-Path $OutputPath "$($peer.Name)-GameplayProbe.dll") -Force
        }
        foreach ($leftover in Get-ChildItem -LiteralPath (Join-Path $peer.Path 'BepInEx') -Filter 'GK2Coop.Probe.command*' -ErrorAction SilentlyContinue) {
            Move-Item -LiteralPath $leftover.FullName -Destination (Join-Path $OutputPath "$($peer.Name)-$($leftover.Name)") -Force
        }
        $backup = Join-Path $backupPath "$($peer.Name)-config.cfg"
        if (Test-Path -LiteralPath $backup) {
            Copy-Item -LiteralPath $backup -Destination (Config-Path $peer.Path) -Force
            $same = (Get-FileHash -Algorithm SHA256 -LiteralPath (Config-Path $peer.Path)).Hash -eq (Get-FileHash -Algorithm SHA256 -LiteralPath $backup).Hash
            Write-Host ("{0} config restored: {1}" -f $peer.Name, $same)
        }
    }
    $manifestBackup = Join-Path $backupPath 'deployment-manifest.json'
    if (Test-Path -LiteralPath $manifestBackup) { Copy-Item -LiteralPath $manifestBackup -Destination $manifestPath -Force }
}
if ($report | Where-Object { $_ -like 'FAIL *' }) { throw 'Join-snapshot verification failed; see results.txt.' }
