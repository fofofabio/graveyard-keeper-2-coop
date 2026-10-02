<#
.SYNOPSIS
    Pictures for the GitHub page and the Steam Workshop item: two to four players in one world on
    the D: test copies, in English, with neutral names and their own looks; the game itself takes
    each picture at 1920x1080.

.DESCRIPTION
    The host (D:\GK2Coop-FullHost) continues the day-18 test save; the others join from their main
    menus by address (127.0.0.1), each with its own test save folder. -Shots picks the motifs
    (default: all); pictures go to -OutputPath, named <number>-<motif>-<who>.png.

    The player's own saves are hashed before and after; the game's registry preferences (window
    size, language) are put back; every copy's config, probe and log are put back or kept with the
    run. Nothing is deleted: world copies and test slots are moved into the output folder.
#>
[CmdletBinding()]
param(
    [ValidateRange(2, 4)][int]$Players = 4,
    [string[]]$Shots = @(),
    [string]$SaveBackup = 'C:\FF\graveyard-keeper-2-coop\artifacts\save-backups\full-release-20260923-071217',
    [string]$OutputPath = ('D:\GK2Coop-Artifacts\media-' + (Get-Date -Format 'yyyyMMdd-HHmm'))
)
$ErrorActionPreference = 'Stop'
if ($Shots) { $Shots = @($Shots | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
. (Join-Path $PSScriptRoot 'GameplayProbe\TestSafety.ps1')
$probeDll = Join-Path $PSScriptRoot 'GameplayProbe\bin\Release\net472\GameplayProbe.dll'
$realSaveFolder = Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2'
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$log = Join-Path $OutputPath 'capture.txt'

$cast = @(
    # Each plays in their own language (the quick messages and notices arrive in it).
    [pscustomobject]@{ Name = 'Mortimer'; Path = 'D:\GK2Coop-FullHost'; Look = '0|0|0|0'; Lang = 'en'; Process = $null },
    [pscustomobject]@{ Name = 'Agnes'; Path = 'D:\GK2Coop-FullClient'; Look = '3|2|0|1'; Lang = 'en'; Process = $null },
    [pscustomobject]@{ Name = 'Silas'; Path = 'D:\GK2Coop-FullHost2'; Look = '2|1|0|2'; Lang = 'de'; Process = $null },
    [pscustomobject]@{ Name = 'Edda'; Path = 'D:\GK2Coop-FullClient2'; Look = '4|2|0|0'; Lang = 'ja'; Process = $null }
) | Select-Object -First $Players
$hostPlayer = $cast[0]
$hostConfig = Join-Path $hostPlayer.Path 'BepInEx\config\com.fabio.gk2coop.cfg'
$hostPort = [regex]::Match((Get-Content -LiteralPath $hostConfig -Raw), '(?m)^Port\s*=\s*(\d+)').Groups[1].Value
$slot = 'GK2Coop_Test_' + (Get-Date -Format 'yyyyMMddHHmmss') + '_media'

function Note([string]$text) { Add-Content -LiteralPath $log -Encoding UTF8 -Value "$(Get-Date -Format HH:mm:ss) $text"; Write-Host $text }
function Set-Cfg([string]$text, [string]$key, [string]$value) {
    if ($text -match "(?m)^$key\s*=") { return [regex]::Replace($text, "(?m)^$key\s*=.*$", "$key = $value") }
    return $text
}
function SaveFolderOf($p) { Join-Path 'D:\GK2Coop-Saves' (Split-Path -Leaf $p.Path) }
function LogOf($p) { Join-Path $p.Path 'BepInEx\LogOutput.log' }
function ReadShared([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $stream = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try { (New-Object IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Close() }
}
function WaitForLog($p, [string]$pattern, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) { if ((ReadShared (LogOf $p)).Contains($pattern)) { return }; Start-Sleep -Seconds 2 }
    throw "Timed out waiting for $($p.Name): $pattern"
}
# The probe's command file in that copy (as Invoke-Probe does it for one host and one client).
function P($p, [string]$command, [int]$seconds = 20) {
    $commandPath = Join-Path $p.Path 'BepInEx\GK2Coop.Probe.command'
    $resultPath = $commandPath + '.result'
    if (Test-Path -LiteralPath $resultPath) { Move-Item -LiteralPath $resultPath -Destination (Join-Path $OutputPath 'last-result.txt') -Force }
    [IO.File]::WriteAllText($commandPath + '.tmp', $command)
    Move-Item -LiteralPath ($commandPath + '.tmp') -Destination $commandPath -Force
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $resultPath) {
            try { $result = [IO.File]::ReadAllText($resultPath) } catch { Start-Sleep -Milliseconds 200; continue }
            if ($result.StartsWith($command + "`n")) { if ($result -match '(?m)^ERROR ') { throw "$($p.Name): $result" }; return $result }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Probe timed out on $($p.Name): $command"
}
function Line([string]$text, [string]$prefix) { (($text -split "`n") | Where-Object { $_ -like "$prefix*" }) -join ' ; ' }
$script:shotNumber = 0
function Shot($p, [string]$motif, [int]$settle = 2) {
    # Some steps drop a game back to its small test window; pictures are always full size.
    if ((P $p 'resolution|1920|1080') -notmatch 'screen=(1920|2560)x') {
        # The screen reports the new size before the picture is drawn at it.
        for ($i = 0; $i -lt 8 -and (P $p 'resolution|1920|1080') -notmatch 'screen=(1920|2560)x'; $i++) { Start-Sleep -Seconds 1 }
        Start-Sleep -Seconds 3
    }
    Start-Sleep -Seconds $settle
    $script:shotNumber++
    $file = Join-Path $OutputPath ('{0:D2}-{1}-{2}.png' -f $script:shotNumber, $motif, $p.Name.ToLower())
    P $p "shot|$file" | Out-Null
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $file)) { Start-Sleep -Milliseconds 300 }
    Note "shot $file"
}
function Want([string]$motif) { -not $Shots -or $Shots -contains $motif }
function HostPos {
    $line = Line (P $hostPlayer 'players') 'PLAYER'
    if ($line -notmatch 'host pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)') { throw "No host position: $line" }
    return @([double]$Matches[1], [double]$Matches[2], [double]$Matches[3])
}
# Everyone to a place of the test tools, then side by side around the host.
function Gather([string]$place, [double[]]$offsets = @(0, 1.2, -1.2, 2.4)) {
    foreach ($p in $cast) { P $p "test-tools|$place" | Out-Null }
    Start-Sleep -Seconds 6
    $h = HostPos
    for ($i = 1; $i -lt $cast.Count; $i++) {
        P $cast[$i] ("body-to|{0}|{1}|{2}" -f ($h[0] + $offsets[$i]).ToString([Globalization.CultureInfo]::InvariantCulture), ($h[2] - 0.6).ToString([Globalization.CultureInfo]::InvariantCulture), $h[1].ToString([Globalization.CultureInfo]::InvariantCulture)) | Out-Null
    }
    Start-Sleep -Seconds 3
}

# ---------------------------------------------------------------- checks and set up
foreach ($p in $cast) {
    Assert-TestInstall $p.Path
    if (Test-Path -LiteralPath (Join-Path $p.Path 'BepInEx\plugins\GameplayProbe.dll')) { throw "A probe is installed in $($p.Path) (a test is running or was interrupted)." }
}
if ((Get-TestGameProcesses @($cast | ForEach-Object Path)).Count -gt 0) { throw 'A test copy is running.' }
if (-not (Test-Path -LiteralPath $probeDll)) { throw 'Build the probe first.' }
$saveHashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $realSaveFolder -File | Where-Object { $_.Name -notlike 'GK2Coop_*' -and $_.Extension -in '.dat', '.info' }) { $saveHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash }
$prefs = Save-GamePrefs
Note "Media capture: $($cast.Count) players ($(($cast | ForEach-Object Name) -join ', ')), host port $hostPort, shots: $(if ($Shots) { $Shots -join ',' } else { 'all' })"

try {
    foreach ($p in $cast) {
        $config = Join-Path $p.Path 'BepInEx\config\com.fabio.gk2coop.cfg'
        Copy-Item -LiteralPath $config -Destination (Join-Path $OutputPath "$($p.Name)-config-before.cfg")
        $cfg = Get-Content -LiteralPath $config -Raw
        $values = if ($p -eq $hostPlayer) { @(@('StartupMode', 'Host'), @('Address', '0.0.0.0'), @('Port', $hostPort)) } else { @(@('StartupMode', 'None'), @('Address', '127.0.0.1'), @('Port', $hostPort)) }
        # The status panel is off: on one PC with four games its ping reads like lag.
        foreach ($pair in $values + @(@('AutoStartNewGame', 'false'), @('PlayerName', $p.Name), @('Transport', 'IP'), @('ShowSessionStatus', 'false'))) { $cfg = Set-Cfg $cfg $pair[0] $pair[1] }
        [IO.File]::WriteAllText($config, $cfg)
        Copy-Item -LiteralPath $probeDll -Destination (Join-Path $p.Path 'BepInEx\plugins\GameplayProbe.dll')
        if (Test-Path -LiteralPath (LogOf $p)) { Move-Item -LiteralPath (LogOf $p) -Destination (Join-Path $OutputPath "$($p.Name)-log-before.txt") -Force }
        New-Item -ItemType Directory -Force -Path (SaveFolderOf $p) | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $SaveBackup 'Steam_1.dat') -Destination (Join-Path (SaveFolderOf $hostPlayer) "$slot.dat")
    $info = [regex]::Replace((Get-Content -LiteralPath (Join-Path $SaveBackup 'Steam_1.info') -Raw), '"saveDateTime":"[^"]+"', '"saveDateTime":"01.01.2000 00:00:00"')
    [IO.File]::WriteAllText((Join-Path (SaveFolderOf $hostPlayer) "$slot.info"), $info)

    # ---------------------------------------------------------------- the host
    $env:GK2COOP_TEST_LANG = 'en'
    $env:GK2COOP_TEST_SAVE_FOLDER = SaveFolderOf $hostPlayer
    $env:GK2COOP_TEST_CONTINUE_SLOT = $slot
    $hostPlayer.Process = Start-TestGame $hostPlayer.Path
    Remove-Item Env:\GK2COOP_TEST_CONTINUE_SLOT
    WaitForLog $hostPlayer 'Attached native host networking' 240
    Note 'host up'

    # ---------------------------------------------------------------- the others, from their menus
    $first = $cast[1]
    foreach ($p in $cast | Select-Object -Skip 1) {
        $env:GK2COOP_TEST_SAVE_FOLDER = SaveFolderOf $p
        $env:GK2COOP_TEST_MENU_CONNECT = '1'
        $env:GK2COOP_TEST_LANG = $p.Lang
        $p.Process = Start-TestGame $p.Path
        Remove-Item Env:\GK2COOP_TEST_MENU_CONNECT
        WaitForLog $p 'gameState=MainMenu' 240
        if ($p -eq $first) {
            P $p 'resolution|1920|1080' | Out-Null
            Start-Sleep -Seconds 4
            if (Want 'menu') { Shot $p 'main-menu' 3 }
            if (Want 'coop-menu') {
                P $p 'menu-ui|open|Root' | Out-Null; Shot $p 'coop-start'
                P $p 'menu-ui|open|Host' | Out-Null; Shot $p 'coop-host'
                P $p 'menu-ui|open|Join' | Out-Null; Shot $p 'coop-join'
                P $p 'menu-ui|open|Closed' | Out-Null
            }
        }
        P $p "menu-bootstrap|127.0.0.1|$hostPort|$($p.Name)" | Out-Null
        if ($p -eq $first -and (Want 'joining')) {
            # The real join: the copy's progress bar.
            $deadline = (Get-Date).AddSeconds(120)
            while ((Get-Date) -lt $deadline) {
                $s = Line (P $p 'status-ui') 'STATUS-UI'
                if ($s -match 'bar=0\.(3|4|5|6|7)') { Shot $p 'joining-copy' 0; break }
                Start-Sleep -Milliseconds 150
            }
        }
        WaitForLog $p 'Attached client networking to the normally initialized local game world.' 300
        if ($p -eq $first -and (Want 'joining')) { WaitForLog $p 'Told the player: You joined' 60; Shot $p 'joined' 1 }
        Note "$($p.Name) joined"
    }
    $deadline = (Get-Date).AddSeconds(90)
    do { Start-Sleep -Seconds 3; $roster = Line (P $hostPlayer 'players') 'PLAYER' } while (([regex]::Matches($roster, ' client ')).Count -lt ($cast.Count - 1) -and (Get-Date) -lt $deadline)
    Note "players: $roster"
    # Everyone at full size (Agnes already is): the others take pictures too (the languages).
    foreach ($p in $cast) { if ($p -ne $first) { P $p 'resolution|1920|1080' | Out-Null } }
    Start-Sleep -Seconds 4

    # Everyone in their own colours; a late morning without rain.
    foreach ($p in $cast | Select-Object -Skip 1) { Note (Line (P $p "look-set|$($p.Look)") 'LOOK-SET') }
    P $hostPlayer 'test-tools|clear' | Out-Null
    P $hostPlayer 'time|0.42' | Out-Null
    Start-Sleep -Seconds 8

    # ---------------------------------------------------------------- together
    if (Want 'together') {
        foreach ($place in 'Graveyard', 'Village') {
            Gather $place
            P $hostPlayer 'time|0.42' | Out-Null
            Shot $first "together-$($place.ToLower())" 4
            Shot $hostPlayer "together-$($place.ToLower())" 1
        }
    }
    if ($Shots -contains 'positions') {
        # Diagnosis: where each game sees every player after everyone gathered.
        Gather 'Village'
        Start-Sleep -Seconds 6
        foreach ($p in $cast) { Note ("$($p.Name) sees: " + ((Line (P $p 'players') 'PLAYER') -replace '\s+', ' ')) }
        Shot $first 'positions' 0
        Start-Sleep -Seconds 20
        foreach ($p in $cast) { Note ("$($p.Name) sees after 20 s: " + ((Line (P $p 'players') 'PLAYER') -replace '\s+', ' ')) }
        Shot $first 'positions-later' 0
    }
    if (Want 'chat') {
        Gather 'Village'
        P $hostPlayer 'chat-send|Good morning! The graveyard needs tending.' | Out-Null
        Start-Sleep -Seconds 1
        P $first 'chat-send|On my way, I will bring the shovels.' | Out-Null
        Shot $hostPlayer 'chat' 2
    }
    if (Want 'languages') {
        # Mortimer sends a quick message with the controller's menu; each reads it in their language.
        Gather 'Village'
        # Full size first (teleports drop the small test windows back), then the pictures at once:
        # the message and the notice only stay a few seconds.
        foreach ($p in $cast | Select-Object -Skip 1) { for ($i = 0; $i -lt 8 -and (P $p 'resolution|1920|1080') -notmatch 'screen=(1920|2560)x'; $i++) { Start-Sleep -Seconds 1 } }
        Start-Sleep -Seconds 3
        function QuickShot($p, [string]$motif) { $script:shotNumber++; P $p ("shot|" + (Join-Path $OutputPath ('{0:D2}-{1}-{2}.png' -f $script:shotNumber, $motif, $p.Name.ToLower()))) | Out-Null }
        P $hostPlayer 'pause-coop|open' | Out-Null; Start-Sleep -Seconds 1
        P $hostPlayer 'pause-coop|click|x' | Out-Null; Start-Sleep -Seconds 1
        Note (Line (P $hostPlayer 'pause-coop|choose|Quick message') 'PAUSE-COOP'); Start-Sleep -Seconds 1
        Note (Line (P $hostPlayer 'pause-coop|choose|Come here!') 'PAUSE-COOP')
        P $hostPlayer 'close-windows' | Out-Null
        Start-Sleep -Seconds 1
        foreach ($p in $cast | Select-Object -Skip 1) { QuickShot $p "quick-message-$($p.Lang)" }
        # The fight notice too, in each language.
        P $hostPlayer 'fight-lock|choose|fight_A1_1' | Out-Null
        Start-Sleep -Seconds 2
        foreach ($p in $cast | Select-Object -Skip 1) { QuickShot $p "fight-notice-$($p.Lang)" }
        P $hostPlayer 'fight-stop' | Out-Null
        Start-Sleep -Seconds 3
        P $hostPlayer 'close-windows' | Out-Null
    }
    if (Want 'scene') {
        # A real story scene at the host: the others are asked, with Mortimer's head; Agnes watches.
        Gather 'Village'
        Note (P $hostPlayer 'teleport-wgo|npc_jeffry')
        Start-Sleep -Seconds 2
        P $hostPlayer 'fire-script-event|Event_124_Village_Money|124_village_money_chest_1' | Out-Null
        $deadline = (Get-Date).AddSeconds(30)
        do { Start-Sleep -Seconds 1; $s = Line (P $first 'scene-share') 'SCENE-SHARE' } while ($s -notmatch 'prompt=\[' -and (Get-Date) -lt $deadline)
        Note "offer: $s"
        Shot $first 'scene-offer' 0
        Note (P $first 'scene-answer|watch')
        Start-Sleep -Seconds 10
        Shot $first 'scene-watching' 0
        Start-Sleep -Seconds 6
        Shot $first 'scene-watching-2' 0
        Shot $hostPlayer 'scene-host' 0
        P $first 'scene-answer|stop' | Out-Null
        $deadline = (Get-Date).AddSeconds(120)
        do { Start-Sleep -Seconds 3; $s = Line (P $hostPlayer 'scene-share') 'SCENE-SHARE' } while ($s -match 'running here|watching=\[' -and (Get-Date) -lt $deadline)
        foreach ($p in $cast) { P $p 'close-windows' | Out-Null }
    }
    if (Want 'sermon') {
        # A sermon in the church: offered like a scene.
        foreach ($p in $cast) { P $p 'test-tools|Church' | Out-Null }
        Start-Sleep -Seconds 6
        Note (P $hostPlayer 'test-tools|sermon')
        $deadline = (Get-Date).AddSeconds(40)
        do { Start-Sleep -Seconds 1; $s = Line (P $first 'scene-share') 'SCENE-SHARE' } while ($s -notmatch 'prompt=\[' -and (Get-Date) -lt $deadline)
        Shot $first 'sermon-offer' 0
        Note (P $first 'scene-answer|watch')
        Start-Sleep -Seconds 12
        Shot $first 'sermon-watching' 0
        Shot $hostPlayer 'sermon-host' 0
        P $first 'scene-answer|stop' | Out-Null
        Start-Sleep -Seconds 5
        foreach ($p in $cast) { P $p 'close-windows' | Out-Null }
    }
    if (Want 'worklock') {
        # One player at a time at a grave: the other is told who is working there.
        Gather 'Graveyard'
        $found = Line (P $hostPlayer 'work-lock|find|grave') 'WORK-LOCK'
        $id = ([regex]::Match($found, 'WORK-LOCK ([0-9a-fA-F-]{8,})=')).Groups[1].Value
        Note "grave: $found"
        if ($id) {
            Note (P $hostPlayer "work-lock|interact|$id")
            Start-Sleep -Seconds 2
            Note (P $first "work-lock|interact|$id")
            Shot $first 'worklock' 1
            P $hostPlayer 'work-lock|close' | Out-Null
            foreach ($p in $cast) { P $p 'close-windows' | Out-Null }
        }
    }
    if (Want 'fight') {
        Gather 'Village'
        P $hostPlayer 'fight-lock|choose|fight_A1_1' | Out-Null
        Shot $first 'fight-notice' 4
        P $first 'fight-lock|choose|fight_A1_1' | Out-Null
        Shot $first 'fight-refused' 1
        P $hostPlayer 'fight-stop' | Out-Null
        Start-Sleep -Seconds 3
        P $hostPlayer 'close-windows' | Out-Null
    }
    if (Want 'pause') {
        P $first 'pad|Xbox_XboxController' | Out-Null
        P $first 'pause-coop|open' | Out-Null
        Shot $first 'pause-menu-controller' 2
        Note (Line (P $first 'pause-coop|click|x') 'PAUSE-COOP')
        Shot $first 'pause-coop-controller' 2
        P $first 'close-windows' | Out-Null
        P $first 'pad|off' | Out-Null
    }
    if ($Shots -contains 'staged') {
        # Scenes that look like play, not a line-up: each player placed around the host, looking one
        # way, some at work (the pose and the look travel with the game's own moves).
        $inv = [Globalization.CultureInfo]::InvariantCulture
        function Stage([string]$place, [string]$name, $layout, [double]$time = 0.42) {
            foreach ($p in $cast) { P $p "test-tools|$place" | Out-Null }
            Start-Sleep -Seconds 6
            P $hostPlayer ("time|" + $time.ToString($inv)) | Out-Null
            $h = HostPos
            for ($i = 0; $i -lt $cast.Count; $i++) {
                $l = $layout[$i]
                $p = $cast[$i]
                P $p ("body-to|{0}|{1}|{2}" -f ($h[0] + $l[0]).ToString($inv), ($h[2] + $l[1]).ToString($inv), $h[1].ToString($inv)) | Out-Null
            }
            Start-Sleep -Seconds 2
            for ($i = 0; $i -lt $cast.Count; $i++) {
                $l = $layout[$i]
                $p = $cast[$i]
                P $p ("face|{0}|{1}" -f $l[2].ToString($inv), $l[3].ToString($inv)) | Out-Null
                if ($l[4]) { P $p "pose|$($l[4])" | Out-Null }
            }
            Start-Sleep -Seconds 3
            Shot $hostPlayer "staged-$name" 1
            Shot $first "staged-$name" 0
            foreach ($p in $cast) { P $p 'pose|Idle' | Out-Null }
        }
        # (dx, dz, look x, look y, pose) for Mortimer, Agnes, Silas, Edda.
        # Offsets from the place's teleport stone (1 unit is about 110 pixels at 2560x1440; +dz is up
        # on the screen), away from the stone so it shows no "[E] Use".
        # The graveyard: two dig in front of graves, one hammers between the rows, one looks on.
        $places = if ($env:GK2COOP_MEDIA_STAGES) { $env:GK2COOP_MEDIA_STAGES -split ',' } else { @('graveyard', 'village', 'town') }
        if ($places -contains 'graveyard') {
            # On the path just below the filled graves (1 unit is about 80 pixels at 2560x1440 there).
            Stage 'Graveyard' 'graveyard-graves' @(@(6.4, 1.5, 0, 1, ''), @(7.8, 1.7, -0.5, 1, ''), @(8.9, 1.3, -1, 0.2, ''), @(7.1, 0.6, 0.2, 1, ''))
            Stage 'Graveyard' 'graveyard-group' @(@(5.6, 1.0, 1, -0.2, ''), @(7.2, 1.0, -1, -0.2, ''), @(6.4, 1.8, 0, -1, ''), @(6.4, 0.2, 0.2, 1, ''))
        }
        if ($places -contains 'village') {
            # A talk on the open road left of the stone.
            Stage 'Village' 'village-circle' @(@(-4.6, -0.35, 1, -0.2, ''), @(-2.4, -0.35, -1, -0.2, ''), @(-3.5, 0.5, 0, -1, ''), @(-3.3, -1.2, -0.4, 1, ''))
        }
        if ($places -contains 'town') {
            # The town square by the port: a talk on the open cobbles.
            Stage 'Port' 'town-square' @(@(-3.5, 1.7, 1, -0.2, ''), @(-1.2, 1.7, -1, -0.2, ''), @(-2.35, 2.6, 0, -1, ''), @(-2.0, 0.8, -0.4, 1, ''))
        }
    }
    if ($Shots -contains 'garden') {
        # A planted bed on both screens (in play a joiner saw the host's full field empty).
        $all = @(((P $hostPlayer 'stations') -split "`n") | Where-Object { $_ -match 'garden|bed|field|plant' })
        Note "garden-like stations on the host: $($all.Count)"
        $all | Select-Object -First 12 | ForEach-Object { Note $_.Trim() }
        Note ("garden objects: " + ((((P $hostPlayer 'objects|garden') -split "`n") | Select-Object -First 12 | ForEach-Object { $_.Trim() }) -join ' | '))
        $hostBeds = @(((P $hostPlayer 'objects|garden') -split "`n") | Where-Object { $_ -match '^\s*OBJ ' } | ForEach-Object { $_.Trim() } | Sort-Object)
        $joinerBeds = @(((P $first 'objects|garden') -split "`n") | Where-Object { $_ -match '^\s*OBJ ' } | ForEach-Object { $_.Trim() } | Sort-Object)
        Note "garden objects: host $($hostBeds.Count), joiner $($joinerBeds.Count), different: $(@(Compare-Object $hostBeds $joinerBeds | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join ' | ')"
        $beds = $hostBeds
        if ($beds) {
            $null = $beds[0] -match '^OBJ (\S+) '
            $bed = $Matches[1]
            foreach ($p in $hostPlayer, $first) { Note (P $p "teleport-wgo|$bed") }
            Start-Sleep -Seconds 8
            foreach ($p in $hostPlayer, $first) { Shot $p 'garden' 1 }
            # The host harvests the ripe beds and plants them again, the way a player does.
            $ripe = @($hostBeds | Where-Object { $_ -match '_ready$' } | ForEach-Object { $null = $_ -match '^OBJ (\S+) '; $Matches[1] } | Select-Object -First 4)
            foreach ($id in $ripe) { Note (Line (P $hostPlayer "wgo-kill|$id") 'WGO-KILL') }
            Start-Sleep -Seconds 6
            $empties = @(((P $hostPlayer 'objects|garden') -split "`n") | Where-Object { $_ -match 'garden_empty$' } | ForEach-Object { $null = $_.Trim() -match '^OBJ (\S+) '; $Matches[1] })
            Note "empty beds after the harvest: $($empties.Count)"
            foreach ($id in $empties | Select-Object -First 4) { Note ((P $hostPlayer "plant|$id|auto") -split "`n" | Where-Object { $_ -like 'PLANT *' }) }
            Start-Sleep -Seconds 10
            foreach ($p in $hostPlayer, $first) {
                Note ("$($p.Name) garden: " + ((((P $p 'objects|garden') -split "`n") | Where-Object { $_ -match '^\s*OBJ ' } | ForEach-Object { $_.Trim() -replace '^OBJ (\S{8})\S*', '$1' }) -join ' | '))
                Note ("$($p.Name) beds: " + ((((P $p 'stations') -split "`n") | Where-Object { $_ -match 'garden' } | ForEach-Object { ($_.Trim() -replace 'startable=\S+ ', '') }) -join ' | '))
                Shot $p 'garden-planted' 1
            }
        }
    }
    if ($Shots -contains 'tags') {
        # The name tags while walking: how far each sits above the drawn head, on both screens.
        Gather 'Village'
        # Out from behind the telescope, where the gathering spot leaves her boxed in, onto the road.
        Note (P $first 'nudge|-2|-3')
        Start-Sleep -Seconds 2
        P $hostPlayer 'resolution|1920|1080' | Out-Null
        Start-Sleep -Seconds 3
        Note (P $hostPlayer "burst|$(Join-Path $OutputPath 'walk-frames')|40|0.15")
        # The stick held, as a player walks: the probe's path walk found no path on this spot.
        foreach ($leg in 'stick|1|0|1.6', 'stick|-1|0|1.6', 'stick|0|-1|1.2', 'stick|0|1|1.2') {
            Note (P $first $leg)
            for ($i = 0; $i -lt 6; $i++) {
                foreach ($p in $hostPlayer, $first) { Note ("$($p.Name) tags: " + (Line (P $p 'tag-centres') 'TAG-CENTRES')) }
                Start-Sleep -Milliseconds 300
            }
        }
        Shot $hostPlayer 'tags' 0
    }
    if ($Shots -contains 'gif') {
        # A few seconds for the GitHub page: the others walk past Agnes and back, seen on her screen.
        Gather 'Village'
        P $hostPlayer 'time|0.42' | Out-Null
        P $first 'resolution|1920|1080' | Out-Null
        Start-Sleep -Seconds 3
        $frames = Join-Path $OutputPath 'gif-frames'
        Note (P $first "burst|$frames|60|0.1")
        foreach ($p in $cast | Where-Object { $_ -ne $first }) { try { Note (P $p 'walk|5|-2') } catch { Note "walk: $($_.Exception.Message)" } }
        Start-Sleep -Seconds 3
        foreach ($p in $cast | Where-Object { $_ -ne $first }) { try { Note (P $p 'walk|-5|2') } catch { Note "walk: $($_.Exception.Message)" } }
        Start-Sleep -Seconds 5
        Note "gif frames: $(@(Get-ChildItem -LiteralPath $frames -Filter '*.png' -ErrorAction SilentlyContinue).Count)"
    }
    Note 'done'
}
finally {
    foreach ($p in $cast) { if ($p.Process -and -not $p.Process.HasExited) { $p.Process.Kill(); $p.Process.WaitForExit(10000) | Out-Null } }
    foreach ($name in 'GK2COOP_TEST_LANG', 'GK2COOP_TEST_SAVE_FOLDER', 'GK2COOP_TEST_CONTINUE_SLOT', 'GK2COOP_TEST_MENU_CONNECT') { [Environment]::SetEnvironmentVariable($name, $null) }
    foreach ($p in $cast) {
        $n = $p.Name
        if (Test-Path -LiteralPath (LogOf $p)) { Copy-Item -LiteralPath (LogOf $p) -Destination (Join-Path $OutputPath "$n-log.txt") -Force }
        $probe = Join-Path $p.Path 'BepInEx\plugins\GameplayProbe.dll'
        if (Test-Path -LiteralPath $probe) { Move-Item -LiteralPath $probe -Destination (Join-Path $OutputPath "$n-GameplayProbe.dll") -Force }
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $p.Path 'BepInEx') -Filter 'GK2Coop.Probe.command*' -ErrorAction SilentlyContinue) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath "$n-$($file.Name)") -Force }
        $before = Join-Path $OutputPath "$n-config-before.cfg"
        if (Test-Path -LiteralPath $before) { Copy-Item -LiteralPath $before -Destination (Join-Path $p.Path 'BepInEx\config\com.fabio.gk2coop.cfg') -Force }
        foreach ($file in Get-ChildItem -LiteralPath (SaveFolderOf $p) -Filter 'GK2Coop_*' -File -ErrorAction SilentlyContinue) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath "$n-$($file.Name)") -Force }
    }
    Note "Game preferences restored: $(Restore-GamePrefs $prefs) value(s)"
    $changed = @($saveHashes.Keys | Where-Object { -not (Test-Path -LiteralPath (Join-Path $realSaveFolder $_)) -or (Get-FileHash -LiteralPath (Join-Path $realSaveFolder $_)).Hash -ne $saveHashes[$_] })
    Note "Your saves: $($saveHashes.Count - $changed.Count) unchanged$(if ($changed) { ', changed: ' + ($changed -join ', ') })"
}
