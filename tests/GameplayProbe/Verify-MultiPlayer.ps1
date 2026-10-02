[CmdletBinding()]
param(
    [string]$HostPath = 'D:\SteamLibrary\steamapps\common\Graveyard Keeper 2 Demo',
    [string]$ClientPath = 'D:\GK2Coop-LocalClient',
    [string]$Client2Path = 'D:\GK2Coop-LocalClient2',
    # A fourth peer when the clone exists. Nothing in the design assumes two clients, but that
    # is an argument rather than evidence, so the suite uses a third client when one is there.
    [string]$Client3Path = 'D:\GK2Coop-LocalClient3',
    [Parameter(Mandatory)][string]$OutputPath
)

# Three players on one machine. The point is one specific defect: the host executes a client's
# command and then hands its own connection state, not the command, to its send path, so the
# command never reaches the *other* client. At two players that is invisible, because the only
# other participant is the host.
#
# The check is therefore not "three instances connect" but "client 1's movement reaches
# client 2", read from client 2's own copy of the world — specifically from the *body*, because
# a peer's stored record keeps its seeded position: only the authority writes that field.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$invoke = Join-Path $PSScriptRoot 'Invoke-Probe.ps1'
$probeDll = Join-Path $PSScriptRoot 'bin\Release\net472\GameplayProbe.dll'
$manifestPath = Join-Path $projectRoot 'artifacts\deployment-manifest.json'
$report = [Collections.Generic.List[string]]::new()

$peers = @(
    [pscustomobject]@{ Name = 'host';    Path = $HostPath;    Mode = 'Host';    Address = '0.0.0.0';   Player = 'LocalHost' },
    [pscustomobject]@{ Name = 'client';  Path = $ClientPath;  Mode = 'Connect'; Address = '127.0.0.1'; Player = 'LocalClient' },
    [pscustomobject]@{ Name = 'client2'; Path = $Client2Path; Mode = 'Connect'; Address = '127.0.0.1'; Player = 'LocalClient2' }
) | Where-Object { $_.Path -and (Test-Path -LiteralPath (Join-Path $_.Path 'GraveyardKeeper2Demo.exe')) }
if ($Client3Path -and (Test-Path -LiteralPath (Join-Path $Client3Path 'GraveyardKeeper2Demo.exe'))) {
    $peers += [pscustomobject]@{ Name = 'client3'; Path = $Client3Path; Mode = 'Connect'; Address = '127.0.0.1'; Player = 'LocalClient3' }
}
$clientCount = $peers.Count - 1
Write-Host "Running with $($peers.Count) players ($clientCount clients)."

function Config-Path([string]$Install) { Join-Path $Install 'BepInEx\config\com.fabio.gk2coop.cfg' }
function Probe-Path([string]$Install) { Join-Path $Install 'BepInEx\plugins\GameplayProbe.dll' }
function Stop-Instances {
    Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 3
}
function Check([bool]$Passed, [string]$Label) {
    $line = $(if ($Passed) { 'PASS ' } else { 'FAIL ' }) + $Label
    $report.Add($line)
    Write-Host $line
    [IO.File]::WriteAllLines((Join-Path $OutputPath 'results.txt'), $report)
}
# Invoke-Probe only knows Host and Client, so the third instance is driven directly.
function Probe3([string]$Install, [string]$Command, [int]$TimeoutSeconds = 15) {
    $commandPath = Join-Path $Install 'BepInEx\GK2Coop.Probe.command'
    $resultPath = $commandPath + '.result'
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath -Force }
    [IO.File]::WriteAllText($commandPath + '.tmp', $Command)
    Move-Item -LiteralPath ($commandPath + '.tmp') -Destination $commandPath -Force
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $resultPath) {
            $result = [IO.File]::ReadAllText($resultPath)
            if ($result.StartsWith($Command + "`n")) {
                if ($result -match '(?m)^ERROR ') { throw $result }
                return $result
            }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Probe timed out on $Install"
}
function Wait-World([string]$Install, [int]$TimeoutSeconds = 300) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try { return Probe3 $Install 'inspect' } catch { Start-Sleep -Seconds 5 }
    }
    throw "No playable world at $Install within $TimeoutSeconds s."
}
function Set-PeerConfig($Peer) {
    $path = Config-Path $Peer.Path
    if (-not (Test-Path -LiteralPath $path)) {
        Copy-Item (Join-Path $projectRoot 'package\com.fabio.gk2coop.cfg') $path -Force
    }
    $config = Get-Content -LiteralPath $path -Raw
    $settings = @{
        StartupMode = $Peer.Mode; Address = $Peer.Address; PlayerName = $Peer.Player
        AutoStartNewGame = 'true'; RelaxStartupGate = 'true'; RelayClientCommands = 'true'
    }
    foreach ($key in $settings.Keys) {
        if ($config -match "(?m)^$key\s*=") {
            $config = [regex]::Replace($config, "(?m)^$key\s*=.*$", "$key = $($settings[$key])")
        } else {
            $config += "`r`n$key = $($settings[$key])`r`n"
        }
    }
    Set-Content -LiteralPath $path -Value $config -Encoding UTF8
}
function Position([string]$State, [int]$ClientId) {
    foreach ($line in ($State -split "`n")) {
        if ($line -match "^PLAYER id=$ClientId .* pos=\((.+)\)\s*$") { return $Matches[1].Trim() }
    }
    return $null
}

if (@(Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'The game is running. Close it before a verification run.'
}
foreach ($peer in $peers) {
    if (-not (Test-Path -LiteralPath (Join-Path $peer.Path 'GraveyardKeeper2Demo.exe'))) {
        throw "Not a game install: $($peer.Path). Run scripts\Setup-LocalCoopClient.ps1 -ClientPath '$($peer.Path)'."
    }
    if (Test-Path -LiteralPath (Probe-Path $peer.Path)) { throw "A probe DLL is already installed in $($peer.Path)." }
}
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$backupPath = Join-Path $OutputPath 'restore'
New-Item -ItemType Directory -Force -Path $backupPath | Out-Null
foreach ($peer in $peers) {
    if (Test-Path -LiteralPath (Config-Path $peer.Path)) {
        Copy-Item -LiteralPath (Config-Path $peer.Path) -Destination (Join-Path $backupPath "$($peer.Name)-config.cfg") -Force
    }
}
if (Test-Path -LiteralPath $manifestPath) { Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $backupPath 'deployment-manifest.json') -Force }

try {
    dotnet build (Join-Path $projectRoot 'src\GK2Coop\GK2Coop.csproj') -c Release -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    dotnet build (Join-Path $PSScriptRoot 'GameplayProbe.csproj') -c Release -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
    $install = Join-Path $projectRoot 'scripts\Install-Mod.ps1'
    # The real install goes last so the deployment manifest describes it, not a clone.
    # Reversed so the real install is written last and the deployment manifest describes it
    # rather than a clone.
    for ($i = $peers.Count - 1; $i -ge 0; $i--) {
        & $install -GamePath $peers[$i].Path -Configuration Release | Out-Null
    }
    foreach ($peer in $peers) {
        Copy-Item -LiteralPath $probeDll -Destination (Probe-Path $peer.Path) -Force
        Set-PeerConfig $peer
    }

    $hostLog = Join-Path $HostPath 'BepInEx\LogOutput.log'
    Remove-Item -LiteralPath $hostLog -Force -ErrorAction SilentlyContinue

    foreach ($peer in $peers) {
        Start-Process -FilePath (Join-Path $peer.Path 'GraveyardKeeper2Demo.exe') -WorkingDirectory $peer.Path
        Start-Sleep -Seconds 12
    }
    Write-Host "$($peers.Count) instances launched; waiting for all clients to connect."

    $deadline = (Get-Date).AddSeconds(480)
    $connected = $false
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $hostLog) -and (Select-String -LiteralPath $hostLog -Pattern "peers=$clientCount" -Quiet)) { $connected = $true; break }
        if (@(Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue).Count -lt $peers.Count) { throw 'An instance exited before connecting.' }
        Start-Sleep -Seconds 5
    }
    Check $connected "$clientCount clients connect to one host at the same time"
    if ($peers.Count -lt 3) {
        Write-Host "Only $clientCount client(s); skipping the gates that need two."
        # Checked here as well as at the end: returning early would otherwise skip the throw and
        # report success on a run that had already failed a gate.
        if ($report | Where-Object { $_ -like 'FAIL *' }) { throw 'Verification failed; see results.txt.' }
        return
    }

    if (-not $connected) { throw 'Never reached three players.' }

    foreach ($peer in $peers) { Wait-World $peer.Path | Out-Null }
    Start-Sleep -Seconds 5

    $beforeC2 = Probe3 $Client2Path 'players'
    [IO.File]::WriteAllText((Join-Path $OutputPath 'before-client2.txt'), $beforeC2)
    $beforeC1 = Probe3 $ClientPath 'players'
    [IO.File]::WriteAllText((Join-Path $OutputPath 'before-client.txt'), $beforeC1)
    if ($beforeC2 -notmatch '(?m)^PLAYER id=1 ') { throw "Test setup: client 2 does not know about client 1. $beforeC2" }
    $client1Before = Position $beforeC2 1

    # Client 1 moves. The host applies it; the question is whether client 2 ever hears.
    Probe3 $ClientPath 'move|6|0' | Out-Null
    Start-Sleep -Seconds 6

    $afterHost = Probe3 $HostPath 'players'
    $afterC2 = Probe3 $Client2Path 'players'
    [IO.File]::WriteAllText((Join-Path $OutputPath 'after-host.txt'), $afterHost)
    [IO.File]::WriteAllText((Join-Path $OutputPath 'after-client2.txt'), $afterC2)
    $client1OnHost = Position $afterHost 1
    $client1After = Position $afterC2 1
    Check ($client1OnHost -ne (Position $beforeC1 1)) "Host sees client 1 move: [$client1OnHost]"

    # The record and the body are separate: a replicated move drives the body, and the record is
    # only written where the command is executed as authority. Both are checked, and the body is
    # the one that decides whether a player can actually see their peer move.
    $bodiesC2 = Probe3 $Client2Path 'bodies'
    [IO.File]::WriteAllText((Join-Path $OutputPath 'after-bodies-client2.txt'), $bodiesC2)
    $bodiesHost = Probe3 $HostPath 'bodies'
    [IO.File]::WriteAllText((Join-Path $OutputPath 'after-bodies-host.txt'), $bodiesHost)

    # Match on the moved axis alone: the peers seed bodies at their own spawn, so the z is what
    # distinguishes "followed client 1" from "still sitting where it was placed".
    $movedX = if ($client1OnHost -match '^\s*(-?[\d.]+)') { [double]$Matches[1] } else { [double]::NaN }
    $bodyFollowed = $false
    foreach ($line in ($bodiesC2 -split "`n")) {
        if ($line -match 'pos=\((-?[\d.]+),') {
            if ([math]::Abs([double]$Matches[1] - $movedX) -lt 1.0) { $bodyFollowed = $true }
        }
    }
    Check $bodyFollowed "Client 2 has a body at client 1's moved position (x~$movedX): [$(($bodiesC2 -split "`n" | Where-Object { $_ -match '^BODY ' }) -join ' | ')]"
    # Deliberately not asserted: `playerData.position` is written only where the command executes
    # as authority, so on a peer it keeps its seeded value while the body moves. An earlier
    # version of this file checked that field and reported a failure the game never promised.
    # Everything below needs a second client: with one, "reaches every other peer" and "the host
    # is the only route between two clients" are the same statement, which is what made these
    # defects invisible for so long.
    # --- three-way world sharing -------------------------------------------------------------
    $container = 'a5d8e32d-a635-4c44-a836-1bf9e8d5fb12'
    function Berries([string]$State) {
        foreach ($line in ($State -split "`n")) {
            if ($line -match "^CONTAINER $container .*items=berryx(\d+)") { return [int]$Matches[1] }
            if ($line -match "^CONTAINER $container .*items=\s*$") { return 0 }
        }
        return -1
    }
    function PlayerBerries([string]$State) {
        foreach ($line in ($State -split "`n")) {
            if ($line -match '^PLAYER .*berryx(\d+)') { return [int]$Matches[1] }
        }
        return 0
    }

    # A drop made by one client must reach the other client, not just the host: a client
    # broadcasts only to the server, so the host has to forward it.
    $spawn = Probe3 $ClientPath 'spawn|berry|1'
    if ($spawn -notmatch 'SPAWN ([0-9a-f-]+)') { throw $spawn }
    $sharedDrop = $Matches[1]
    Start-Sleep -Seconds 3
    $dropEverywhere = $true
    foreach ($peer in $peers) {
        if ($peer.Path -eq $ClientPath) { continue }
        $state = Probe3 $peer.Path 'inspect'
        [IO.File]::WriteAllText((Join-Path $OutputPath "drop-$($peer.Name).txt"), $state)
        if ($state -notmatch "DROP $sharedDrop berry") { $dropEverywhere = $false }
    }
    Check $dropEverywhere "A drop made by client 1 reaches every other peer: $sharedDrop"

    # All three grab it at one deadline. Exactly one may end up holding it.
    $beforeAll = @{}
    foreach ($peer in $peers) { $beforeAll[$peer.Name] = PlayerBerries (Probe3 $peer.Path 'inspect') }
    $due = [DateTime]::UtcNow.AddSeconds(4).Ticks
    foreach ($peer in $peers) { Probe3 $peer.Path "at|$due|collect|$sharedDrop" | Out-Null }
    Start-Sleep -Seconds 7
    $afterAll = @{}
    $gone = $true
    foreach ($peer in $peers) {
        $state = Probe3 $peer.Path 'inspect'
        [IO.File]::WriteAllText((Join-Path $OutputPath "race-$($peer.Name).txt"), $state)
        $afterAll[$peer.Name] = PlayerBerries $state
        if ($state -match "DROP $sharedDrop ") { $gone = $false }
    }
    $delta = ($afterAll.Values | Measure-Object -Sum).Sum - ($beforeAll.Values | Measure-Object -Sum).Sum
    Check (($delta -eq 1) -and $gone) "$($peers.Count)-way contested pickup credits exactly one player: combined delta=$delta, drop removed everywhere=$gone"

    # Three simultaneous chest additions must all survive and leave every peer agreeing.
    $chestBefore = Berries (Probe3 $HostPath 'inspect')
    $due = [DateTime]::UtcNow.AddSeconds(4).Ticks
    $expectedAdd = 0
    for ($i = 0; $i -lt $peers.Count; $i++) {
        $amount = $i + 1
        $expectedAdd += $amount
        Probe3 $peers[$i].Path "at|$due|container-add|$container|berry|$amount" | Out-Null
    }
    Start-Sleep -Seconds 8
    $chestCounts = @{}
    foreach ($peer in $peers) {
        $state = Probe3 $peer.Path 'inspect'
        [IO.File]::WriteAllText((Join-Path $OutputPath "chest-$($peer.Name).txt"), $state)
        $chestCounts[$peer.Name] = Berries $state
    }
    $agreed = $true
    foreach ($peer in $peers) { if ($chestCounts[$peer.Name] -ne $chestCounts['host']) { $agreed = $false } }
    $detail = ($peers | ForEach-Object { "$($_.Name)=$($chestCounts[$_.Name])" }) -join ', '
    Check ($agreed -and ($chestCounts['host'] -eq $chestBefore + $expectedAdd)) "$($peers.Count) simultaneous chest additions all survive: $chestBefore + $expectedAdd -> $($chestCounts['host']) [$detail]"

    # A quest transition from one client must reach the other client, through the host.
    Probe3 $ClientPath 'quest-await|1_intro_prison_find_key' | Out-Null
    Start-Sleep -Seconds 4
    $questEverywhere = $true
    foreach ($peer in $peers) {
        $state = Probe3 $peer.Path 'inspect'
        [IO.File]::WriteAllText((Join-Path $OutputPath "quest-$($peer.Name).txt"), $state)
        if ($state -notmatch '1_intro_prison_find_key Awaiting') { $questEverywhere = $false }
    }
    Check $questEverywhere "A quest transition from client 1 reaches every other peer"

    # A world object destroyed by one client must be gone for the other client too, not just the
    # host. This is the path where a bush stayed standing for the third player.
    $killable = Probe3 $ClientPath 'wgos|1'
    if ($killable -notmatch 'WGOS ([0-9a-f-]+)=(\S+?)(;|$)') { throw "Test setup: no destructible object. $killable" }
    $victim = $Matches[1]
    $victimName = $Matches[2]
    if ((Probe3 $Client2Path "wgo-alive|$victim") -notmatch 'ALIVE True') { throw "Test setup: $victimName is not alive on client 2." }
    Probe3 $ClientPath "wgo-kill|$victim" | Out-Null
    Start-Sleep -Seconds 4
    $deadEverywhere = $true
    foreach ($peer in $peers) {
        if ($peer.Path -eq $ClientPath) { continue }
        if ((Probe3 $peer.Path "wgo-alive|$victim") -notmatch 'ALIVE False') { $deadEverywhere = $false }
    }
    Check $deadEverywhere "A world object destroyed by client 1 ($victimName) is gone on every other peer"

    # Clearing a container runs through Inventory.Clear, a different path from add and remove.
    Probe3 $Client2Path "container-clear|$container" | Out-Null
    Start-Sleep -Seconds 4
    $clearedCounts = @{}
    foreach ($peer in $peers) {
        $state = Probe3 $peer.Path 'inspect'
        [IO.File]::WriteAllText((Join-Path $OutputPath "cleared-$($peer.Name).txt"), $state)
        $clearedCounts[$peer.Name] = Berries $state
    }
    $allEmpty = $true
    foreach ($peer in $peers) { if ($clearedCounts[$peer.Name] -ne 0) { $allEmpty = $false } }
    $clearedDetail = ($peers | ForEach-Object { "$($_.Name)=$($clearedCounts[$_.Name])" }) -join ', '
    Check $allEmpty "A container cleared by client 2 is empty on every peer: [$clearedDetail]"

    if ($client1After -ne $client1OnHost) {
        $note = "NOTE client 2's stored record of client 1 stays at its seed [$client1After] while the body moves; only the authority writes that field."
        $report.Add($note)
        Write-Host $note
        [IO.File]::WriteAllLines((Join-Path $OutputPath 'results.txt'), $report)
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
if ($report | Where-Object { $_ -like 'FAIL *' }) { throw 'Three-player verification failed; see results.txt.' }
