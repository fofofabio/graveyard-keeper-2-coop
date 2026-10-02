[CmdletBinding()]
param(
    [string]$SaveBackup = 'C:\FF\graveyard-keeper-2-coop\artifacts\save-backups\full-release-20260923-071217',
    [string]$HostPath = 'D:\GK2Coop-FullHost',
    [string]$ClientPath = 'D:\GK2Coop-FullClient',
    [string]$OutputPath = 'C:\FF\graveyard-keeper-2-coop\artifacts\progressed-coop-0.23.0',
    [switch]$FreshClient,
    [switch]$MenuConnectClient,
    [switch]$MenuBootstrapClient,
    # After the bootstrap join: a first-time joiner starts fresh, their progress is stored by the
    # host, and a relaunched joiner copying the world again gets it back.
    [switch]$ProfileRejoin,
    # The host goes to the main menu and hosts again in the same game, three times, and the joiner joins again each time.
    [switch]$RehostExperiment,
    # A busy evening in phases: both at once on one chest and one drop, a craft then leaving, building during a join, the joiner elsewhere, a night; after each, worlds identical.
    [switch]$MixExperiment,
    # With -MixExperiment: after the phases, this many minutes of both players acting at random (berries counted).
    [int]$ChaosMinutes = 0,
    # With -MixExperiment: a third player on this copy (its own save folder), who joins and watches every phase.
    [string]$ThirdPath = '',
    # Measurement, not a gate: the joiner queues a craft and both peers' station state is recorded.
    [switch]$CraftExperiment,
    # Growing beds are host-run: the beds and every drop must agree before and after crops finish.
    [switch]$GardenExperiment,
    # A world-changing craft (it replaces or removes its object) finished on the joiner must change
    # the host's copy the same way.
    [switch]$CraftEndExperiment,
    # Sleeping speeds up time only when everyone sleeps.
    [switch]$SleepExperiment,
    # Vendors: a deal on either side reaches the other side's shop.
    [switch]$VendorExperiment,
    # Items with state of their own (a body with its organs) in graves: taken out, or changed
    # inside, on either side, and both sides must hold the identical item afterwards.
    [switch]$RichExperiment,
    # A tech learned on either side is known on the other, with the recipes it unlocks.
    [switch]$KnowledgeExperiment,
    # A player in another scene (the prison) is hidden from the others, and shown again on return.
    [switch]$SceneExperiment,
    # World values in the resource store (reputation, congregation) agree; personal ones do not travel.
    [switch]$WorldResExperiment,
    # Divergence audit: the two worlds compared object by object, drop by drop and container by
    # container, before and after a night passes and a few minutes of play.
    [switch]$AuditExperiment,
    # Build mode: a building placed on either side appears on the other, and a removal removes it.
    [switch]$BuildExperiment,
    # Zombies: placed on either side they exist on both, run by the host, mirrored on the joiner.
    [switch]$ZombieExperiment,
    # A zombie brought to a station the way the game does it works there, run by the host and
    # mirrored on the joiner.
    [switch]$ZombieWorkExperiment,
    # After zombie output appears, the joiner takes one item from the station inventory.
    [switch]$ZombieOutputPickupExperiment,
    # Force a stale joiner pickup after the host empties the station; the host must revoke it.
    [switch]$ZombiePickupConflictExperiment,
    # Modal UI's PauseGame path must leave both co-op simulations active.
    [switch]$PauseExperiment,
    # Follow one item through the powered conveyor layout in the progressed save.
    [switch]$ConveyorExperiment,
    [ValidateSet('Host','Client')][string]$ConveyorSourcePeer = 'Host',
    # A porter in the carrier zone takes stone from a source chest and follows its delivery route.
    [switch]$PorterExperiment,
    # A save made while hosting holds no co-op session records; the live session keeps them.
    [switch]$SaveHygieneExperiment,
    # A fighting level's stage changed on either side is the same on the other.
    [switch]$FightExperiment,
    [switch]$WeatherExperiment,
    [switch]$GemsExperiment,
    [switch]$ChatExperiment,
    [switch]$UiKitExperiment,
    [switch]$MenuLookExperiment,
    [switch]$HudLookExperiment,
    [switch]$PlainUi,
    [switch]$SpriteSurvey,
    [switch]$ResolutionSurvey,
    [switch]$TranslationSurvey,
    [switch]$CutsceneSurvey,
    [string]$CutsceneScripts = '',
    [string]$ScriptGraphs = '',
    [switch]$CutsceneOnBoth,
    [switch]$SceneShareExperiment,
    [string]$ShareScene = 'Event_113_Sewers_God:113_sewers_god_1',
    [string]$ShareNear = '',
    [switch]$ShareAnswer,
    [string]$ShareSkipEvent = '',
    [string]$ShareSpawn = '',
    [switch]$ShareByJoiner,
    [switch]$SceneDeclineExperiment,
    [switch]$SceneStopExperiment,
    [switch]$ScenePromptLook,
    [switch]$NavSurvey,
    [switch]$PausePadExperiment,
    [switch]$LateWatchExperiment,
    [switch]$LateWithAnswer,
    [switch]$SpeechExperiment,
    [switch]$WorkLockExperiment,
    [switch]$TagTraceExperiment,
    [switch]$TestToolsExperiment,
    # Builds the playground save (tests\Start-Playground.ps1): the test yard at home and the host's
    # kit, saved into the temporary slot, which the cleanup copies to the output folder.
    [switch]$BuildPlayground,
    # A joiner's copy of the host's world never becomes the joiner's own Continue: dated 2000,
    # not saved into, and earlier copies moved out of the save list at start.
    [switch]$WorldCopyExperiment,
    # A fight building (barricade) placed in a pre-fight reaches the other player.
    [switch]$FightBuildExperiment,
    [switch]$FightWatchExperiment,
    # Research: start a real fight on the host and record what fights and where, on both sides.
    [switch]$FightExplore,
    # Another player's fight seen: the fighter's enemies shown on the watcher, where they are.
    [switch]$FightMirrorExperiment,
    [switch]$FightLockExperiment,
    [switch]$ZombieLookExperiment,
    [switch]$FightStuckExperiment,
    [string]$GhostLooks = '',
    [string]$FightLevel = 'fight_A1_1',
    [int]$SoakMinutes = 0,
    # The regression runner saves and restores the game's registry preferences once around all its
    # runs (runs in parallel would restore each other's test values).
    [switch]$SkipPrefs,
    # The runner builds the probe once for all its runs (two builds at once lock each other's files).
    [switch]$SkipProbeBuild,
    # Where this pair's games keep their saves (the mod's GK2COOP_TEST_SAVE_FOLDER): never the
    # player's own save folder, one folder per pair of copies.
    [string]$SaveFolder = '',
    [switch]$BindingsSurvey,
    [string]$SpritePatterns = 'portrait,head,face,keeper,hourglass,clock,icon_,emot,bubble,arrow,circle',
    [string]$Languages = '',
    # Phase 0 of Steam multiplayer: Steam is available in both test copies and they can exchange
    # messages through SteamNetworkingSockets (IP mode, loopback).
    [switch]$SteamSpikeExperiment,
    # The wire under the session: the game's own UDP transport, or Steam's sockets (local IP mode).
    [ValidateSet('IP','Steam')][string]$Transport = 'IP',
    # The joiner joins from the main menu through the host's Steam lobby (as an invite or "Join
    # game" would), instead of typing an address. Implies -MenuBootstrapClient and -Transport Steam.
    [switch]$JoinViaSteamLobby,
    # Research: probe commands run on both peers once the session is up, answers saved to explore.txt.
    [string[]]$Explore
)
if ($ProfileRejoin -or $WorldCopyExperiment -or $RehostExperiment -or $MixExperiment) { $MenuBootstrapClient = [switch]$true }
if ($JoinViaSteamLobby) { $MenuBootstrapClient = [switch]$true; $Transport = 'Steam' }

if (@(@($FreshClient, $MenuConnectClient, $MenuBootstrapClient) | Where-Object { $_ }).Count -gt 1) { throw 'Choose only one client startup mode.' }

# Both processes Continue from a temporary copy of the same progressed save. The
# original Steam_1 pair is never selected and is checked by hash after cleanup.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
# The player's own saves: only hashed, before and after (the games use $saveFolder below).
$realSaveFolder = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2')
$saveFolder = if ($SaveFolder) { $SaveFolder } else { Join-Path 'D:\GK2Coop-Saves' (Split-Path -Leaf $HostPath) }
New-Item -ItemType Directory -Force -Path $saveFolder | Out-Null
# A random tail: two runs in parallel may start in the same second.
$slot = 'GK2Coop_Test_' + (Get-Date -Format 'yyyyMMddHHmmss') + '_' + ([guid]::NewGuid().ToString('N').Substring(0, 4))
$manifest = Join-Path $root 'artifacts\deployment-manifest.json'
$probeDll = Join-Path $PSScriptRoot 'bin\Release\net472\GameplayProbe.dll'
$invoke = Join-Path $PSScriptRoot 'Invoke-Probe.ps1'
$peers = @(
    [pscustomobject]@{Name='Host';Path=$HostPath;Mode='Host';Address='0.0.0.0';Process=$null},
    [pscustomobject]@{Name='Client';Path=$ClientPath;Mode='Connect';Address='127.0.0.1';Process=$null}
)
if ($ThirdPath) { $peers += [pscustomobject]@{Name='Third';Path=$ThirdPath;Mode='Connect';Address='127.0.0.1';Process=$null} }
$sourceDat = Join-Path $SaveBackup 'Steam_1.dat'
$sourceInfo = Join-Path $SaveBackup 'Steam_1.info'
$testDat = Join-Path $saveFolder ($slot + '.dat')
$testInfo = Join-Path $saveFolder ($slot + '.info')
$existingCoopSlots = @(Get-ChildItem -LiteralPath $saveFolder -Filter 'GK2Coop_*.dat' -File -ErrorAction SilentlyContinue | ForEach-Object BaseName)

. (Join-Path $PSScriptRoot 'TestSafety.ps1')
function ConfigPath($peer) { Join-Path $peer.Path 'BepInEx\config\com.fabio.gk2coop.cfg' }
# The host copy's own port (the second pair of copies uses 8890).
$hostPort = [regex]::Match((Get-Content -LiteralPath (Join-Path $HostPath 'BepInEx\config\com.fabio.gk2coop.cfg') -Raw), '(?m)^Port\s*=\s*(\d+)').Groups[1].Value
if (-not $hostPort) { $hostPort = '8889' }
function ProbePath($peer) { Join-Path $peer.Path 'BepInEx\plugins\GameplayProbe.dll' }
function Probe([string]$peer, [string]$command) {
    $result = & $invoke -Peer $peer -Command $command -TimeoutSeconds 10
    if ($result -match '(?m)^ERROR ') { throw $result }
    return $result
}
function Check([bool]$passed, [string]$label) {
    $line = $(if ($passed) { 'PASS ' } else { 'FAIL ' }) + $label
    Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Value $line
    Write-Host $line
    if (-not $passed) { throw $line }
}
# Bodies (probe 'body-players' lines) below the ground: they fell through it and keep falling.
function Fallen([string[]]$bodies) {
    @($bodies | Where-Object { $_ -match 'pos=\(\s*[-\d.E]+,\s*([-\d.E]+),' -and [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture) -lt -5 })
}
function Invoke-Audit {
    function Audit([string]$Peer) { ((Probe $Peer 'audit') -split "`n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -like 'AUDIT-*' }) }
    function Compare-Worlds([string]$Label) {
        $h = @(Audit Host); $c = @(Audit Client)
        [IO.File]::WriteAllText((Join-Path $OutputPath "audit-$Label-host.txt"), ($h -join "`n"))
        [IO.File]::WriteAllText((Join-Path $OutputPath "audit-$Label-client.txt"), ($c -join "`n"))
        $diff = @(Compare-Object $h $c | ForEach-Object { $(if ($_.SideIndicator -eq '<=') { 'host   ' } else { 'joiner ' }) + $_.InputObject })
        [IO.File]::WriteAllText((Join-Path $OutputPath "audit-$Label-diff.txt"), ($diff -join "`n"))
        return $diff
    }
    function Note([bool]$passed, [string]$label) {
        $line = $(if ($passed) { 'PASS ' } else { 'FAIL ' }) + $label
        Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Value $line
        Write-Host $line
    }
    $d0 = Compare-Worlds 'start'
    Note ($d0.Count -eq 0) "The two worlds are identical at the start ($($d0.Count) differing lines; audit-start-diff.txt)"
    Probe Client 'sleep' | Out-Null
    Probe Host 'sleep' | Out-Null
    Start-Sleep -Seconds 50
    Start-Sleep -Seconds 10
    $d1 = Compare-Worlds 'after-night'
    Note ($d1.Count -eq 0) "The two worlds are identical after a night passes ($($d1.Count) differing lines; audit-after-night-diff.txt)"
    Start-Sleep -Seconds 120
    $d2 = Compare-Worlds 'after-play'
    Note ($d2.Count -eq 0) "The two worlds are identical after two more minutes ($($d2.Count) differing lines; audit-after-play-diff.txt)"
    if ($d0.Count + $d1.Count + $d2.Count -gt 0) { throw "The worlds diverged; see the audit-*-diff.txt files." }
}
function WaitFor([string]$log, [string]$pattern, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch $pattern -Quiet)) { return }
        foreach ($peer in $peers) {
            if ($peer.Process -and $peer.Process.HasExited) { throw "$($peer.Name) exited while waiting for: $pattern" }
        }
        Start-Sleep -Seconds 3
    }
    throw "Timed out waiting for: $pattern"
}

Assert-TestInstall $HostPath
Assert-TestInstall $ClientPath
if ($ThirdPath) { Assert-TestInstall $ThirdPath }
$copiesInUse = @($peers | ForEach-Object Path)
if ((Get-TestGameProcesses $copiesInUse).Count -gt 0) { throw 'A test copy of the game is still running.' }
# Slots an interrupted run left behind (no cleanup ran): moved out before anything else.
# Only old ones: a run in parallel on the other pair of copies has a fresh one.
$leftovers = @(Get-ChildItem -LiteralPath $saveFolder -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'GK2Coop_Test_*' -and $_.LastWriteTime -lt (Get-Date).AddHours(-2) })
if ($leftovers.Count -gt 0) {
    $leftoverFolder = 'D:\GK2Coop-Artifacts\leftover-slots'
    New-Item -ItemType Directory -Force -Path $leftoverFolder | Out-Null
    foreach ($file in $leftovers) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $leftoverFolder $file.Name) -Force }
    Write-Host "Moved $($leftovers.Count) file(s) of an interrupted run's test slot to $leftoverFolder"
}
$savedPrefs = if ($SkipPrefs) { $null } else { Save-GamePrefs }
$saveHashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $realSaveFolder -File | Where-Object { $_.Name -notlike 'GK2Coop_*' -and $_.Extension -in '.dat','.info' }) { $saveHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash }
if (-not (Test-Path -LiteralPath $sourceDat) -or -not (Test-Path -LiteralPath $sourceInfo)) { throw 'The backed-up save pair is missing.' }
if ((Test-Path -LiteralPath $testDat) -or (Test-Path -LiteralPath $testInfo)) { throw 'The temporary slot already exists.' }
foreach ($peer in $peers) { if (Test-Path -LiteralPath (ProbePath $peer)) { throw "Probe already installed in $($peer.Path)" } }
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
foreach ($peer in $peers) { Copy-Item -LiteralPath (ConfigPath $peer) -Destination (Join-Path $OutputPath "$($peer.Name)-config-before.cfg") }
if (Test-Path -LiteralPath $manifest) { Copy-Item -LiteralPath $manifest -Destination (Join-Path $OutputPath 'manifest-before.json') }

try {
    if (-not $SkipProbeBuild) {
        dotnet build (Join-Path $PSScriptRoot 'GameplayProbe.csproj') -c Release -v q --nologo -p:GamePath="$HostPath"
        if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
    }
    if (-not (Test-Path -LiteralPath $probeDll)) { throw 'The probe is not built.' }
    Copy-Item -LiteralPath $sourceDat -Destination $testDat
    $info = Get-Content -LiteralPath $sourceInfo -Raw
    # Old on purpose: a slot left by an interrupted run is never what the player's own Continue
    # opens (the probe points the test copies' Continue at it through the mod).
    $date = '01.01.2000 00:00:00'
    $info = [regex]::Replace($info, '"saveDateTime":"[^"]+"', '"saveDateTime":"' + $date + '"')
    [IO.File]::WriteAllText($testInfo, $info)
    if ((Get-FileHash $testDat).Hash -ne (Get-FileHash $sourceDat).Hash) { throw 'Temporary save hash mismatch.' }
    if ($WorldCopyExperiment) {
        # A world copy from an "earlier session": a valid save an hour old, which the mod should
        # move out of the save list when the game starts.
        $oldCopy = 'GK2Coop_00000000000000aa'
        foreach ($ext in '.dat','.info') {
            Copy-Item -LiteralPath (Join-Path $saveFolder ($slot + $ext)) -Destination (Join-Path $saveFolder ($oldCopy + $ext))
            $planted = Get-Item -LiteralPath (Join-Path $saveFolder ($oldCopy + $ext))
            $planted.CreationTime = (Get-Date).AddHours(-1)
            $planted.LastWriteTime = (Get-Date).AddHours(-1)
        }
    }

    foreach ($peer in $peers) {
        Copy-Item -LiteralPath $probeDll -Destination (ProbePath $peer)
        $configPath = ConfigPath $peer
        $config = Get-Content -LiteralPath $configPath -Raw
        $autoStart = if ($FreshClient -and $peer.Name -eq 'Client') { 'true' } else { 'false' }
        $relaxGate = if ($FreshClient -and $peer.Name -eq 'Client') { 'true' } else { 'false' }
        foreach ($pair in @(@('StartupMode',$peer.Mode),@('Address',$peer.Address),@('AutoStartNewGame',$autoStart),@('RelaxStartupGate',$relaxGate),@('PlayerName',$peer.Name))) {
            $config = [regex]::Replace($config, "(?m)^$($pair[0])\s*=.*$", "$($pair[0]) = $($pair[1])")
        }
        if ($config -match '(?m)^Transport\s*=') {
            $config = [regex]::Replace($config, '(?m)^Transport\s*=.*$', "Transport = $Transport")
        } else {
            $config = [regex]::Replace($config, '(?m)^\[Network\]\s*$', "[Network]`r`n`r`nTransport = $Transport")
        }
        $style = if ($PlainUi) { 'false' } else { 'true' }
        if ($config -match '(?m)^GameStyle\s*=') {
            $config = [regex]::Replace($config, '(?m)^GameStyle\s*=.*$', "GameStyle = $style")
        } elseif ($config -match '(?m)^\[UI\]\s*$') {
            $config = [regex]::Replace($config, '(?m)^\[UI\]\s*$', "[UI]`r`n`r`nGameStyle = $style")
        } else {
            $config += "`r`n[UI]`r`n`r`nGameStyle = $style`r`n"
        }
        if ($TestToolsExperiment -or $BuildPlayground) {
            if ($config -match '(?m)^TestTools\s*=') { $config = [regex]::Replace($config, '(?m)^TestTools\s*=.*$', 'TestTools = true') }
            else { $config += "`r`n[Testing]`r`n`r`nTestTools = true`r`n" }
        }
        [IO.File]::WriteAllText($configPath, $config)
    }

    $env:GK2COOP_TEST_SAVE_FOLDER = $saveFolder
    # One language for every test game (the checks read German texts), whatever another game on this PC last saved.
    $env:GK2COOP_TEST_LANG = 'de'
    $env:GK2COOP_TEST_CONTINUE_SLOT = $slot
    if ($WorldCopyExperiment) { $env:GK2COOP_TEST_TIDY = '1' }
    $env:GK2COOP_TEST_HOST_PATH = $HostPath
    $env:GK2COOP_TEST_CLIENT_PATH = $ClientPath
    if ($ThirdPath) { $env:GK2COOP_TEST_THIRD_PATH = $ThirdPath }
    $exe = 'GraveyardKeeper2.exe'
    $peers[0].Process = Start-TestGame $HostPath
    $hostLog = Join-Path $HostPath 'BepInEx\LogOutput.log'
    WaitFor $hostLog ("Invoked the game's Continue button for isolated slot $slot") 120
    WaitFor $hostLog 'Attached native host networking' 180
    if ($FreshClient -or $MenuConnectClient -or $MenuBootstrapClient) { Remove-Item Env:\GK2COOP_TEST_CONTINUE_SLOT }
    if ($MenuConnectClient -or $MenuBootstrapClient) { $env:GK2COOP_TEST_MENU_CONNECT = '1' }
    $clientLog = Join-Path $ClientPath 'BepInEx\LogOutput.log'
    if (Test-Path -LiteralPath $clientLog) { Move-Item -LiteralPath $clientLog -Destination (Join-Path $OutputPath 'Client-log-before.txt') -Force }
    # One PC, one Steam account: the lobby join's last hop goes to the local address.
    if ($JoinViaSteamLobby) { $env:GK2COOP_TEST_STEAM_LOCALIP = '1' }
    $peers[1].Process = Start-TestGame $ClientPath
    if (-not $FreshClient -and -not $MenuConnectClient -and -not $MenuBootstrapClient) { Remove-Item Env:\GK2COOP_TEST_CONTINUE_SLOT }
    if ($MenuBootstrapClient) {
        Remove-Item Env:\GK2COOP_TEST_MENU_CONNECT
        WaitFor $clientLog 'gameState=MainMenu' 120
        if ($NavSurvey) {
            # The co-op menu with a controller only: the game's main menu to the Co-op button, A
            # opens the window, the pad chooses, A opens a section, B back, B closes.
            $nav = New-Object System.Collections.Generic.List[string]
            function Focused { (((Probe Client 'nav-info') -split "`n") | Where-Object { $_ -like 'CONTROLLER *UIMainMenuWindow*' }) -join '' }
            function Pad([string]$Button) { Probe Client "pad-button|$Button" | Out-Null; Start-Sleep -Milliseconds 500 }
            function MenuPad { (((Probe Client 'menu-pad') -split "`n") | Where-Object { $_ -like 'MENU-PAD *' }) -join '' }
            $nav.Add((Probe Client 'pad|Xbox_XboxController'))
            Start-Sleep -Seconds 1
            $nav.Add("start: " + (Focused))
            $reached = $false
            for ($i = 0; $i -lt 8 -and -not $reached; $i++) {
                Pad 'DDown'
                $f = Focused
                $nav.Add("DDown: $f")
                if ($f -match 'focused=[^=]*GK2CoopButton') { $reached = $true }
            }
            Check $reached "The game's own controller navigation reaches the Co-op button ($f)"
            Probe Client "shot|$(Join-Path $OutputPath 'nav-1-main-menu.png')" | Out-Null
            Pad 'A'
            Start-Sleep -Seconds 1
            $m = MenuPad; $nav.Add("A: $m")
            Check ($m -match 'page=Root' -and $m -match 'owned=True' -and $m -notmatch '^MENU-PAD none') "A on the Co-op button opens the co-op window with the controller in it ($m)"
            Probe Client "shot|$(Join-Path $OutputPath 'nav-2-coop-root.png')" | Out-Null
            $before = Focused
            $first = $m
            Pad 'DDown'
            $m = MenuPad; $nav.Add("DDown in window: $m")
            Check ($m -ne $first -and (Focused) -eq $before) "The pad moves the choice in the co-op window, not in the main menu under it ($m)"
            Pad 'DUp'
            $m = MenuPad; $nav.Add("DUp: $m")
            Pad 'A'
            Start-Sleep -Seconds 1
            $m = MenuPad; $nav.Add("A on first: $m")
            Check ($m -match 'page=(Host|Join)') "A opens the chosen section ($m)"
            if ($m -match '\{input\}') {
                # No Steam keyboard outside Big Picture: the mod's own opens; the pad types on it.
                Pad 'A'
                Start-Sleep -Milliseconds 600
                $k = (((Probe Client 'keyboard') -split "`n") | Where-Object { $_ -like 'KEYBOARD *' }) -join ''
                $nav.Add("A on the name field: $k")
                Check ($k -match 'open=True') "A on a text field with a controller opens a keyboard ($k)"
                Probe Client 'keyboard|clear' | Out-Null
                Pad 'B'
                $k = (((Probe Client 'keyboard') -split "`n") | Where-Object { $_ -like 'KEYBOARD *' }) -join ''
                $nav.Add("B on an empty line: $k")
                Check ($k -match 'open=False') "B on an empty line closes the keyboard ($k)"
                Pad 'A'
                Start-Sleep -Milliseconds 600
                Probe Client 'keyboard|clear' | Out-Null
                Pad 'A'
                Pad 'DDown'
                Pad 'A'
                Pad 'DRight'
                Pad 'A'
                $k = (((Probe Client 'keyboard') -split "`n") | Where-Object { $_ -like 'KEYBOARD *' }) -join ''
                $nav.Add("typed: $k")
                Probe Client "shot|$(Join-Path $OutputPath 'nav-keyboard.png')" | Out-Null
                $k2 = (((Probe Client 'keyboard|ok') -split "`n") | Where-Object { $_ -like 'KEYBOARD *' }) -join ''
                Start-Sleep -Milliseconds 500
                $t = MenuPad
                $field = (((Probe Client 'menu-ui|describe') -split "`n") | Where-Object { $_ -like 'MENU-UI *' }) -join ''
                $nav.Add("after OK: $k2 / $field")
                Check ($k -match 'text=1qw' -and $field -match '\{1qw\}') "The pad types on it and OK fills the field ($k / $field)"
            }
            Probe Client "shot|$(Join-Path $OutputPath 'nav-3-section.png')" | Out-Null
            Pad 'B'
            $m = MenuPad; $nav.Add("B: $m")
            Check ($m -match 'page=Root') "B goes back to the start of the window ($m)"
            Pad 'B'
            $m = MenuPad; $nav.Add("B again: $m")
            Check ($m -match 'page=Closed') "B at the start closes the window ($m)"
            # Addresses joined before are offered on the Join page, and a press fills them in.
            Probe Client 'menu-recent|10.0.0.9|8890' | Out-Null
            Probe Client 'menu-ui|open|Join' | Out-Null
            Start-Sleep -Seconds 1
            $j = (((Probe Client 'menu-ui|describe') -split "`n") | Where-Object { $_ -like 'MENU-UI *' }) -join ''
            if ($j -notmatch '10\.0\.0\.9:8890') { Probe Client 'menu-ui|click|Join by address' | Out-Null; Start-Sleep -Seconds 1; $j = (((Probe Client 'menu-ui|describe') -split "`n") | Where-Object { $_ -like 'MENU-UI *' }) -join '' }
            Probe Client 'menu-ui|click|10.0.0.9:8890' | Out-Null
            Start-Sleep -Seconds 1
            $j2 = (((Probe Client 'menu-ui|describe') -split "`n") | Where-Object { $_ -like 'MENU-UI *' }) -join ''
            $nav.Add("recent: $j2")
            Check ($j -match '<10\.0\.0\.9:8890>' -and $j2 -match '\{10\.0\.0\.9\}' -and $j2 -match '\{8890\}') "An address joined before is offered and one press fills it in ($j2)"
            Probe Client 'menu-ui|open|Closed' | Out-Null
            $nav.Add("main menu after: " + (Focused))
            foreach ($word in '^W.hlen$','^Zur.ck$','^Ausw.hlen$','^Schlie.en$','^Best.tigen$') { $nav.Add((((Probe Client "game-keys|$word|value") -split "`n") | Where-Object { $_ -like 'GAME-KEYS*' }) -join '') }
            Probe Client 'pad|off' | Out-Null
            [IO.File]::WriteAllText((Join-Path $OutputPath 'nav-survey.txt'), ($nav -join "`n"))
        }
        if ($MenuLookExperiment) {
            function MenuUi([string]$Action, [string]$Arg = '') { ((((Probe Client "menu-ui|$Action|$Arg") -split "`n") | Where-Object { $_ -like 'MENU-UI *' }) -join '').Substring(8) }
            function Shot([string]$Name) { Probe Client "shot|$(Join-Path $OutputPath $Name)" | Out-Null; Start-Sleep -Seconds 2 }
            $menuLog = New-Object System.Collections.Generic.List[string]
            Start-Sleep -Seconds 4
            # English first (the test copies run the game in German), German at the end.
            $menuLog.Add((Probe Client 'lang|en'))
            Start-Sleep -Seconds 1
            $state = MenuUi 'describe'
            $menuLog.Add("closed: $state")
            if ($PlainUi) {
                Check ($state -match 'gameUi=False' -and $state -match 'entry=none') "With GameStyle off the plain menu is used ($state)"
                Shot 'menu-plain.png'
                [IO.File]::WriteAllText((Join-Path $OutputPath 'menu-look.txt'), $state)
            } else {
            Check ($state -match 'gameUi=True' -and $state -match 'entry=True:Co-op') "The co-op entry is a button in the game's own main menu ($state)"
            Shot 'menu-0-main.png'
            $menuLog.Add((MenuUi 'click' 'entry'))
            Start-Sleep -Seconds 1
            $state = MenuUi 'describe'
            $menuLog.Add("root: $state")
            Check ($state -match 'page=Root' -and $state -match '\[Co-op\]' -and $state -match '<Host a game>') "Pressing it opens the co-op window in the game's frame ($state)"
            Shot 'menu-1-root.png'
            $menuLog.Add((MenuUi 'click' 'Host a game'))
            Start-Sleep -Seconds 1
            $state = MenuUi 'describe'
            $menuLog.Add("host: $state")
            Check ($state -match 'page=Host' -and $state -match '\{') "The host page shows the game's input fields ($state)"
            Check ($state -notmatch '\d+\.\d+\.\d+\.\d+') "The host page shows no address until asked ($state)"
            Shot 'menu-2-host.png'
            $menuLog.Add((MenuUi 'click' 'Host without Steam'))
            Start-Sleep -Seconds 1
            $menuLog.Add((MenuUi 'click' 'Show my addresses'))
            Start-Sleep -Seconds 1
            $state = MenuUi 'describe'
            $menuLog.Add("addresses: $state")
            Check ($state -match '<Hide my addresses>' -and $state -match '\d+\.\d+\.\d+\.\d+  ?' -and $state -notmatch '172\.29\.') "Show my addresses lists the usable ones, not the virtual machine's ($state)"
            Shot 'menu-2b-host-addresses.png'
            MenuUi 'open' 'Join' | Out-Null
            Start-Sleep -Seconds 1
            $menuLog.Add((MenuUi 'click' 'Join by address'))
            Start-Sleep -Seconds 1
            $state = MenuUi 'describe'
            $menuLog.Add("join: $state")
            Shot 'menu-3-join.png'
            $menuLog.Add((MenuUi 'click' 'Back'))
            Start-Sleep -Seconds 1
            $state = MenuUi 'describe'
            $menuLog.Add("back: $state")
            Check ($state -match 'page=Root') "Back returns to the first page ($state)"
            foreach ($path in 'ButtonsContent/DialogueButtonPrefab', 'GK2Coop.Menu', 'Vertical Group/GK2CoopButton') {
                $tree = Probe Client "ui-tree|$path"
                Add-Content -LiteralPath (Join-Path $OutputPath 'ui-tree.txt') -Value $tree
            }
    if ($SpriteSurvey) {
                $pattern = 'portrait|head|face|avatar|keeper|loading|spinner|hourglass|skull|player|char|icon_|emot|bubble|arrow|circle|gear|clock'
                Probe Client "sprites|$pattern" | Set-Content -LiteralPath (Join-Path $OutputPath 'sprites-menu.txt') -Encoding utf8
                foreach ($name in 'portrait_icon_hero','notification-portrait','notification-portrait-mask','hud-world_zone-portrait','inventory_window-header_portrait-el_1','portrait_icon_mirror') {
                    Probe Client "sprite-save|$name|$(Join-Path $OutputPath "sprite-$name.png")" | Out-Null
                }
            }
            # The status window: every state, in English.
            function StatusUi([string]$Arg) { ((((Probe Client "status-ui|$Arg") -split "`n") | Where-Object { $_ -like 'STATUS-UI *' }) -join '').Substring(10) }
            MenuUi 'open' 'Closed' | Out-Null
            $s = StatusUi 'working'; Start-Sleep -Seconds 1; $s = StatusUi 'describe'; $menuLog.Add("status working: $s")
            Probe Client 'canvases' | Set-Content -LiteralPath (Join-Path $OutputPath 'canvases.txt') -Encoding utf8
            Check ($s -match '^Working \[Connecting\]') "The status window shows connecting ($s)"
            Shot 'status-1-connecting.png'
            StatusUi 'copy|0.45' | Out-Null; Start-Sleep -Seconds 1; $s = StatusUi 'describe'; $menuLog.Add("status copy: $s")
            Check ($s -match '\[Copying the world\]' -and $s -match 'bar=0\.45') "The status window fills a bar while copying ($s)"
            Shot 'status-2-copying.png'
            StatusUi 'done' | Out-Null; Start-Sleep -Seconds 1; $s = StatusUi 'describe'; $menuLog.Add("status done: $s")
            Check ($s -match '^Done \[Together\]') "The status window celebrates joining ($s)"
            Shot 'status-3-together.png'
            Start-Sleep -Seconds 3
            $s = StatusUi 'describe'
            Check ($s -eq 'hidden') "It closes by itself after joining ($s)"
            StatusUi 'failed|Could not reach 10.0.0.9:8889. Make sure the host is hosting.' | Out-Null; Start-Sleep -Seconds 1; $s = StatusUi 'describe'; $menuLog.Add("status failed: $s")
            Check ($s -match '^Failed \[Something went wrong\] Could not reach') "A failure stays with its reason ($s)"
            Shot 'status-4-failed.png'
            StatusUi 'ok' | Out-Null; Start-Sleep -Seconds 1; $s = StatusUi 'describe'
            Check ($s -eq 'hidden') "OK closes the failure ($s)"
            if ($ResolutionSurvey) {
                # Menu and status window at common sizes: 16:9 from 720p to 4K, 16:10, 21:9 and 4:3.
                $layouts = [ordered]@{ '1280x720' = '640x360'; '1280x800' = '640x400'; '1280x1024' = '640x512'; '1600x900' = '800x450'; '1920x1080 and 4K' = '960x540'; '1920x1200 and 2880x1800' = '960x600'; '2560x1440' = '1280x720'; '3440x1440' = '1720x720' }
                foreach ($size in $layouts.Keys) {
                    $bw, $bh = $layouts[$size] -split 'x'
                    $menuLog.Add("$size " + (((Probe Client "ui-box|$bw|$bh") -split "`n") | Where-Object { $_ -like 'UI-BOX *' }))
                    Start-Sleep -Seconds 3
                    MenuUi 'open' 'Host' | Out-Null
                    Start-Sleep -Seconds 1
                    MenuUi 'click' 'Host without Steam' | Out-Null
                    Start-Sleep -Seconds 1
                    $b = ((((Probe Client 'ui-bounds') -split "`n") | Where-Object { $_ -like 'UI-BOUNDS *' }) -join '')
                    $menuLog.Add("$size host page: $b")
                    Check ($b -match 'GK2Coop.Menu=\[[^\]]* in\]' -and $b -notmatch ' OUT\]') "At $size ($($layouts[$size]) layout) the co-op window fits ($b)"
                    Shot "res-$($layouts[$size])-menu.png"
                    MenuUi 'click' 'Host without Steam' | Out-Null
                    MenuUi 'open' 'Closed' | Out-Null
                    StatusUi 'copy|0.6' | Out-Null
                    Start-Sleep -Seconds 1
                    $b = ((((Probe Client 'ui-bounds') -split "`n") | Where-Object { $_ -like 'UI-BOUNDS *' }) -join '')
                    $menuLog.Add("$size status: $b")
                    Check ($b -match 'GK2Coop.Status=\[[^\]]* in\]') "At $size ($($layouts[$size]) layout) the status window fits ($b)"
                    Shot "res-$($layouts[$size])-status.png"
                    StatusUi 'hide' | Out-Null
                    Start-Sleep -Seconds 1
                }
                Probe Client 'ui-box|full' | Out-Null
            }
            # German, as the game is set.
            $menuLog.Add((Probe Client 'lang|game'))
            MenuUi 'click' 'entry' | Out-Null
            Start-Sleep -Seconds 1
            $state = MenuUi 'describe'
            $menuLog.Add("german root: $state")
            Check ($state -match '\[Koop\]' -and $state -match '<Spiel hosten>' -and $state -match 'entry=True:Koop') "The menu follows the game's language ($state)"
            Shot 'menu-de-root.png'
            MenuUi 'open' 'Host' | Out-Null; Start-Sleep -Seconds 1; Shot 'menu-de-host.png'
            MenuUi 'open' 'Closed' | Out-Null
            StatusUi 'copy|0.7' | Out-Null; Start-Sleep -Seconds 1; $s = StatusUi 'describe'; $menuLog.Add("german status: $s")
            Check ($s -match '\[Welt wird kopiert\]') "The status window follows the game's language ($s)"
            Shot 'status-de-copying.png'
            StatusUi 'hide' | Out-Null
            Start-Sleep -Seconds 1
            if ($TranslationSurvey) {
                # Every language the game offers: switch the game (without saving the setting), draw each
                # page and status of the mod, and check the fonts have every character it shows.
                $list = Probe Client 'languages'
                [IO.File]::WriteAllText((Join-Path $OutputPath 'languages.txt'), $list)
                $original = if ($list -match 'current=(\S+)') { $Matches[1] } else { 'en' }
                $ids = @(($list -split "`n") | Where-Object { $_ -like 'LANGUAGE *' } | ForEach-Object { ($_ -split ' ')[1] })
                Check ($ids.Count -ge 2 -and @($ids | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -eq 0) "The game's languages are listed ($($ids -join ', '))"
                if ($Languages) { $ids = @($Languages -split ',') }
                $trFails = New-Object System.Collections.Generic.List[string]
                function Glyphs { ((((Probe Client 'glyphs') -split "`n") | Where-Object { $_ -like 'GLYPHS*' }) -join '') }
                foreach ($id in $ids) {
                    $menuLog.Add((Probe Client "game-lang|$id"))
                    Start-Sleep -Seconds 2
                    MenuUi 'open' 'Root' | Out-Null; Start-Sleep -Seconds 1
                    $root = MenuUi 'describe'; $g1 = Glyphs
                    foreach ($labelPath in 'GK2CoopButton/Content/Back/Label','Vertical Group/Load/Content/Back/Label','GK2Coop.Menu/HeaderGroup/Header') { $menuLog.Add("$id " + (((Probe Client "font-of|$labelPath") -split "`n") | Where-Object { $_ -like 'FONT-OF *' })) }
                    Shot "tr-$id-root.png"
                    # Open every section whatever state the last language left it in, so the longest
                    # texts and the address list are checked in each language.
                    MenuUi 'open' 'Host' | Out-Null; Start-Sleep -Seconds 1
                    if ((MenuUi 'describe') -notmatch '\{8889\}') { MenuUi 'click' 'Host without Steam' | Out-Null; Start-Sleep -Seconds 1 }
                    if ((MenuUi 'describe') -notmatch '\d+\.\d+\.\d+\.\d+') { MenuUi 'click' 'Show my addresses' | Out-Null; Start-Sleep -Seconds 1 }
                    $hostPage = MenuUi 'describe'
                    $g2 = Glyphs
                    Shot "tr-$id-host.png"
                    MenuUi 'open' 'Join' | Out-Null; Start-Sleep -Seconds 1
                    if ((MenuUi 'describe') -notmatch '\{127\.0\.0\.1\}') { MenuUi 'click' 'Join by address' | Out-Null; Start-Sleep -Seconds 1 }
                    $joinPage = MenuUi 'describe'
                    $g3 = Glyphs
                    Shot "tr-$id-join.png"
                    if ($hostPage -notmatch '\{8889\}' -or $hostPage -notmatch '\d+\.\d+\.\d+\.\d+' -or $joinPage -notmatch '\{127\.0\.0\.1\}') { $trFails.Add("$id sections did not open") }
                    MenuUi 'open' 'Closed' | Out-Null
                    StatusUi 'failed-key' | Out-Null; Start-Sleep -Seconds 1
                    $status = StatusUi 'describe'; $g4 = Glyphs
                    Shot "tr-$id-status.png"
                    StatusUi 'ok' | Out-Null
                    StatusUi 'copy|0.5' | Out-Null; Start-Sleep -Seconds 1
                    $g5 = Glyphs
                    StatusUi 'hide' | Out-Null; Start-Sleep -Seconds 1
                    $menuLog.Add("$id root: $root"); $menuLog.Add("$id status: $status")
                    foreach ($g in $g1,$g2,$g3,$g4,$g5) { $menuLog.Add("$id $g") }
                    $allGlyphs = "$g1 $g2 $g3 $g4 $g5"
                    $missingHere = @([regex]::Matches($allGlyphs, 'MISSING\[[^\]]*\]') | ForEach-Object { $_.Value } | Select-Object -Unique)
                    $menuLog.Add("$id glyph result: " + $(if ($missingHere.Count) { $missingHere -join ' ' } else { 'all present' }))
                    if ($missingHere.Count) { $trFails.Add("$id " + ($missingHere -join ' ')) }
                    [IO.File]::WriteAllText((Join-Path $OutputPath 'menu-look.txt'), ($menuLog -join "`n"))
                }
                Check ($trFails.Count -eq 0) "In all $($ids.Count) languages every character of the mod's text is in the game's fonts ($($trFails -join ' | '))"
                $menuLog.Add((Probe Client "game-lang|$original"))
                Start-Sleep -Seconds 2
            }
            [IO.File]::WriteAllText((Join-Path $OutputPath 'menu-look.txt'), ($menuLog -join "`n"))
            }
        }
        if ($JoinViaSteamLobby) {
            $lobbyLine = ''
            $deadline = (Get-Date).AddSeconds(60)
            while ((Get-Date) -lt $deadline -and $lobbyLine -notmatch '^STEAM-LOBBY \d+ ') {
                Start-Sleep -Seconds 3
                $lobbyLine = (((Probe Host 'steam-lobby') -split "`n") | Where-Object { $_ -like 'STEAM-LOBBY *' }) -join ''
            }
            Check ($lobbyLine -match 'marker=1 version=\S+ protocol=\d+ limit=[2-4] owner=7656\d+ connect=\+connect_lobby \d+') "The host opens a Steam lobby friends can join ($lobbyLine)"
            $lobbyId = ($lobbyLine -split ' ')[1]
            $realProtocol = if ($lobbyLine -match 'protocol=(\d+)') { $Matches[1] } else { '0' }
            # Version gate: a host on another protocol is refused before anything is copied.
            Probe Host 'steam-lobby-set|protocol|999' | Out-Null
            Start-Sleep -Seconds 2
            Probe Client "steam-join-lobby|$lobbyId" | Out-Null
            Start-Sleep -Seconds 6
            $gate = (((Probe Client 'steam-lobby-outcome') -split "`n") | Where-Object { $_ -like 'STEAM-LOBBY-OUTCOME *' }) -join ''
            Check ($gate -match 'failed: .*(same version|same release|dieselbe Version)' -and -not (Select-String -LiteralPath $clientLog -Pattern 'Verified and imported host save' -Quiet)) "A host on a different mod version is refused before anything is copied ($gate)"
            Probe Host "steam-lobby-set|protocol|$realProtocol" | Out-Null
            # One account plays both sides here, so the refused joiner leaving the lobby empties it
            # and Steam closes it; the host opens a new one. Read the current lobby again.
            $deadline = (Get-Date).AddSeconds(30)
            do {
                Start-Sleep -Seconds 3
                $lobbyLine = (((Probe Host 'steam-lobby') -split "`n") | Where-Object { $_ -like 'STEAM-LOBBY *' }) -join ''
            } while ((Get-Date) -lt $deadline -and $lobbyLine -notmatch "protocol=$realProtocol ")
            $lobbyId = ($lobbyLine -split ' ')[1]
            $bootstrapStart = (((Probe Client "steam-join-lobby|$lobbyId") -split "`n") | Where-Object { $_ -like 'STEAM-JOIN-LOBBY *' }) -join ''
            Check ($bootstrapStart -match 'requested') "The joiner requests to join through the lobby ($bootstrapStart)"
        } else {
            $bootstrapStart = Probe Client "menu-bootstrap|127.0.0.1|$hostPort"
            Check ($bootstrapStart -match 'MENU-BOOTSTRAP started localName=BootstrapGuest') 'Menu settings update the name used by the joining session'
        }
        WaitFor $clientLog 'Verified and imported host save into isolated slot' 180
        WaitFor $clientLog "Invoked the game's Continue button for imported co-op slot" 90
        WaitFor $clientLog 'Attached client networking to the normally initialized local game world.' 180
        WaitFor $hostLog 'joined as' 90
        if ($JoinViaSteamLobby) {
            # Over Steam everyone goes by their Steam name (0.63), not the name typed in the menu.
            $joinedAs = (Select-String -LiteralPath $hostLog -Pattern "joined as '([^']+)'" | Select-Object -Last 1).Matches[0].Groups[1].Value
            Check ($joinedAs -and $joinedAs -ne 'BootstrapGuest') "Over Steam the host knows the joiner by their Steam name ($joinedAs)"
        } else {
            Check ((Select-String -LiteralPath $hostLog -Pattern "joined as 'BootstrapGuest'" -Quiet)) 'Host receives the name selected in the join menu'
        }
        # The joiner connects twice in one process; its handlers must survive the second start.
        WaitFor $clientLog "accepted the connection" 30
        Check ((Select-String -LiteralPath $clientLog -Pattern 'Sent handshake as').Count -le 2) 'The joiner hears the host after copying the world (handshake answered, not repeated)'
        $hostProgress = Probe Host 'progress'
        $clientProgress = Probe Client 'progress'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'host-progress.txt'), $hostProgress)
        [IO.File]::WriteAllText((Join-Path $OutputPath 'client-progress.txt'), $clientProgress)
        Check ($hostProgress -match 'WORLD scenes=6 objects=10\d\d' -and $clientProgress -match 'WORLD scenes=6 objects=10\d\d' -and
            $hostProgress -match 'QUESTS Completed=115' -and $clientProgress -match 'QUESTS Completed=115') 'Network-transferred day-18 save loads through Continue and matches host progress'
        $opened = Select-String -LiteralPath $clientLog -Pattern "OnContinueButtonClicked saveSlotData:\[([^\]]*)\]" | Select-Object -Last 1
        if ($opened) { Check ($opened.Line -match 'GK2Coop_[0-9a-f]{16}') "The joiner's Continue opened the copy of the host's world, not another save ($($opened.Matches[0].Groups[1].Value))" }
        $hostBodies = Probe Host 'bodies'
        $clientBodies = Probe Client 'bodies'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'host-bodies.txt'), $hostBodies)
        [IO.File]::WriteAllText((Join-Path $OutputPath 'client-bodies.txt'), $clientBodies)
        Check (@($hostBodies -split "`n" | Where-Object { $_ -like 'BODY *' }).Count -eq 2 -and
            @($clientBodies -split "`n" | Where-Object { $_ -like 'BODY *' }).Count -eq 2) 'Both peers have two active player bodies after imported-save startup'
        $beforeHostPlayers = Probe Host 'players'
        $beforeClientPlayers = Probe Client 'players'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'players-before-host.txt'), $beforeHostPlayers)
        [IO.File]::WriteAllText((Join-Path $OutputPath 'players-before-client.txt'), $beforeClientPlayers)
        # The joiner's number is not fixed: the copy-the-world connection counts, and a busy machine
        # can need a retry; both sides must name the same one.
        $joinerId = [regex]::Match($beforeHostPlayers, 'PLAYER id=(\d+) client').Groups[1].Value
        Check ($beforeHostPlayers -match 'PLAYER id=0 host' -and $joinerId -and $joinerId -ne '0' -and
            $beforeClientPlayers -match 'PLAYER id=0 host' -and $beforeClientPlayers -match "PLAYER id=$joinerId client") "Both peers agree on host and joining player identities (joiner $joinerId)"
        # World sync in the copied world, which reconnects in the same process: a host chest edit
        # must reach the joiner. This failed silently before 0.26.0 because the joiner's handlers
        # were registered on the first connection's message manager only.
        $chestBefore = Probe Client 'inspect'
        if ($chestBefore -notmatch 'CONTAINER ([0-9a-f-]+) chest_home size=20 items=([^\r\n]*)') { throw 'No chest_home in the copied world.' }
        $chestId = $Matches[1]
        $berriesBefore = if ($Matches[2] -match 'berryx(\d+)') { [int]$Matches[1] } else { 0 }
        Probe Host "container-add|$chestId|berry|2" | Out-Null
        Start-Sleep -Seconds 3
        $chestHost = ((Probe Host 'inspect') -split "`n" | Where-Object { $_ -match "^CONTAINER $chestId " }) -join ''
        $chestClient = ((Probe Client 'inspect') -split "`n" | Where-Object { $_ -match "^CONTAINER $chestId " }) -join ''
        $berriesAfter = if ($chestClient -match 'berryx(\d+)') { [int]$Matches[1] } else { 0 }
        Check ($chestHost -eq $chestClient -and $berriesAfter -eq $berriesBefore + 2) "A host chest edit reaches the joiner in the copied world ($chestClient)"
        $beforeHostBodies = Probe Host 'body-players'
        $beforeClientBodies = Probe Client 'body-players'
        Probe Client 'move|4|0' | Out-Null
        Start-Sleep -Seconds 2
        $afterHostPlayers = Probe Host 'players'
        $afterHostBodies = Probe Host 'body-players'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'players-after-client-move-host.txt'), $afterHostPlayers)
        [IO.File]::WriteAllText((Join-Path $OutputPath 'bodies-after-client-move-host.txt'), $afterHostBodies)
        $beforeRemote = ($beforeHostPlayers -split "`n" | Where-Object { $_ -like "PLAYER id=$joinerId *" }) -join ''
        $afterRemote = ($afterHostPlayers -split "`n" | Where-Object { $_ -like "PLAYER id=$joinerId *" }) -join ''
        Check ($beforeRemote -ne $afterRemote) 'Host sees the imported-save client move'
        $beforeRemoteBody = ($beforeHostBodies -split "`n" | Where-Object { $_ -like "BODY id=$joinerId *" }) -join ''
        $afterRemoteBody = ($afterHostBodies -split "`n" | Where-Object { $_ -like "BODY id=$joinerId *" }) -join ''
        Check ($beforeRemoteBody -ne $afterRemoteBody) 'Host remote body moves with the imported-save client'
        Probe Host 'move|0|4' | Out-Null
        Start-Sleep -Seconds 2
        $afterClientBodies = Probe Client 'body-players'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'bodies-after-host-move-client.txt'), $afterClientBodies)
        $beforeHostBody = ($beforeClientBodies -split "`n" | Where-Object { $_ -like 'BODY id=0 *' }) -join ''
        $afterHostBody = ($afterClientBodies -split "`n" | Where-Object { $_ -like 'BODY id=0 *' }) -join ''
        Check ($beforeHostBody -ne $afterHostBody) 'Client remote body moves with the host'
        # Back where they stood: a saved spot off the (small) house floor is where a later join,
        # which starts there, would drop through the world.
        Probe Client 'move|-4|0' | Out-Null
        Probe Host 'move|0|-4' | Out-Null
        Start-Sleep -Seconds 2
        if ($MixExperiment) {
            # A busy evening, in phases; after each, everything that must hold in co-op. The single
            # runs test one feature each; the bugs the players met lived in the interplay: both at
            # once, acting while the other joins or leaves, being somewhere else, a night between.
            function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
            function Count([string]$log, [string]$pattern) { @(Select-String -LiteralPath $log -Pattern $pattern -SimpleMatch).Count }
            function WaitCount([string]$log, [string]$pattern, [int]$above, [int]$seconds) {
                $deadline = (Get-Date).AddSeconds($seconds)
                while ((Get-Date) -lt $deadline) { if ((Count $log $pattern) -gt $above) { return $true }; Start-Sleep -Seconds 2 }
                return $false
            }
            function WaitState([string]$Peer, [string]$state, [int]$seconds) {
                $deadline = (Get-Date).AddSeconds($seconds)
                while ((Get-Date) -lt $deadline) { $s = Line $Peer 'game-state' 'GAME-STATE'; if ($s -match $state) { return $s }; Start-Sleep -Seconds 2 }
                return $null
            }
            # Both machines run the command at the same instant (one clock on this PC).
            function Together([string]$hostCommand, [string]$clientCommand) {
                $at = [DateTime]::UtcNow.AddSeconds(3).Ticks
                Probe Host "at|$at|$hostCommand" | Out-Null
                Probe Client "at|$at|$clientCommand" | Out-Null
                Start-Sleep -Seconds 6
            }
            function Own([string]$Peer, [string]$item) { if ((Probe $Peer "count|$item") -match 'player=(\d+)') { [int]$Matches[1] } else { -1 } }
            function Total([string]$Peer, [string]$item) { if ((Probe $Peer "count|$item") -match 'player=(\d+) containers=(\d+)') { [int]$Matches[2] } else { -1 } }
            function Shared([string]$Peer) { @(((Probe $Peer 'stations') -split "`n") | Where-Object { $_ -match ' shared=True ' } | ForEach-Object { $_.Trim() -replace ' startable=\S+', '' } | Sort-Object) -join "`n" }
            function Rejoin([string]$label) {
                $joined = Count $clientLog 'Attached client networking to the normally initialized local game world.'
                $accepted = Count $clientLog 'accepted the connection'
                Probe Client "menu-bootstrap|127.0.0.1|$hostPort" | Out-Null
                $ok = (WaitCount $clientLog 'Attached client networking to the normally initialized local game world.' $joined 300) -and (WaitCount $clientLog 'accepted the connection' $accepted 60)
                Check $ok "${label}: the joiner joins again"
                Start-Sleep -Seconds 8
            }
            function Verify([string]$label) {
                Start-Sleep -Seconds 4
                $name = $label -replace '[^A-Za-z0-9]+', '-'
                $h = @(((Probe Host 'audit') -split "`n") | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -like 'AUDIT-*' })
                $hs = Shared Host
                $hb = @((Probe Host 'body-players') -split "`n" | Where-Object { $_ -like 'BODY *' })
                $hw = Line Host 'watchdog' 'WATCHDOG'
                Check ($hb.Count -eq $players -and @($hb | Where-Object { $_ -match '^BODY id= ' }).Count -eq 0 -and $hw -match 'repairs=0') "${label}: the host draws $players keepers, each a player, and its watchdog is idle (host $($hb.Count); $hw)"
                Check (@(Fallen $hb).Count -eq 0) "${label}: no keeper has fallen through the host's world ($((Fallen $hb) -join '; '))"
                foreach ($joiner in $others) {
                    $c = @(((Probe $joiner 'audit') -split "`n") | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -like 'AUDIT-*' })
                    $diff = @(Compare-Object $h $c | ForEach-Object { $(if ($_.SideIndicator -eq '<=') { 'host   ' } else { "$joiner " }) + $_.InputObject })
                    [IO.File]::WriteAllText((Join-Path $OutputPath "mix-$name-$joiner-diff.txt"), ($diff -join "`n"))
                    Check ($h.Count -gt 0 -and $diff.Count -eq 0) "${label}: ${joiner}'s world is the host's ($($h.Count) audit lines, $($diff.Count) differing; mix-$name-$joiner-diff.txt)"
                    Check ((Shared $joiner) -eq $hs) "${label}: every shared station shows the same state for $joiner"
                    $cb = @((Probe $joiner 'body-players') -split "`n" | Where-Object { $_ -like 'BODY *' })
                    $cw = Line $joiner 'watchdog' 'WATCHDOG'
                    Check ($cb.Count -eq $players -and @($cb | Where-Object { $_ -match '^BODY id= ' }).Count -eq 0 -and $cw -match 'repairs=0') "${label}: $joiner draws $players keepers, each a player, and its watchdog is idle ($($cb.Count); $cw)"
                    Check (@(Fallen $cb).Count -eq 0) "${label}: no keeper has fallen through ${joiner}'s world ($((Fallen $cb) -join '; '))"
                }
            }
            if ((Probe Client 'inspect') -notmatch 'CONTAINER ([0-9a-f-]+) chest_home') { throw 'No chest_home.' }
            $chest = $Matches[1]
            $others = @('Client')
            if ($ThirdPath) {
                # A third player, on another copy with a save folder of its own: joins from the menu,
                # and from here on every phase checks its world too (whatever the host and the joiner
                # did reaches it only through the host).
                $thirdLog = Join-Path $ThirdPath 'BepInEx\LogOutput.log'
                if (Test-Path -LiteralPath $thirdLog) { Move-Item -LiteralPath $thirdLog -Destination (Join-Path $OutputPath 'Third-log-before.txt') -Force }
                $thirdSaves = "$saveFolder-third"
                New-Item -ItemType Directory -Force -Path $thirdSaves | Out-Null
                $env:GK2COOP_TEST_SAVE_FOLDER = $thirdSaves
                $env:GK2COOP_TEST_MENU_CONNECT = '1'
                $peers[2].Process = Start-TestGame $ThirdPath
                $env:GK2COOP_TEST_SAVE_FOLDER = $saveFolder
                WaitFor $thirdLog 'gameState=MainMenu' 150
                Probe Third "menu-bootstrap|127.0.0.1|$hostPort|Third" | Out-Null
                WaitFor $thirdLog 'Attached client networking to the normally initialized local game world.' 300
                WaitFor $thirdLog 'accepted the connection' 60
                Start-Sleep -Seconds 8
                $others += 'Third'
            }
            $players = 1 + $others.Count
            Verify 'Start'

            # 1. The same chest, both at the same instant; then both take more than it holds.
            $before = Total Host 'berry'
            Together "container-add|$chest|berry|3" "container-add|$chest|berry|5"
            Together "container-add|$chest|berry|2" "container-add|$chest|berry|2"
            $mid = Total Host 'berry'
            Check ($mid -eq $before + 12 -and (Total Client 'berry') -eq $mid) "1: both filling one chest at once: every berry arrives once ($before + 12 -> host $mid, joiner $(Total Client 'berry'))"
            Together "container-remove|$chest|berry|$($mid - $before)" "container-remove|$chest|berry|$($mid - $before)"
            Verify '1 one chest, both at once'

            # 1b. A big chest, as a late game's store is: more stacks, or a longer list, than the
            # short "id x count" form carries. It went unshared, both ways.
            if ((Probe Host 'inspect') -match 'CONTAINER ([0-9a-f-]+) chest_rough ') {
                $bigChest = $Matches[1]
                Probe Host "container-capacity|$bigChest|80" | Out-Null
                Probe Client "container-capacity|$bigChest|80" | Out-Null
                # The same chest is as big on every machine (a third player's copy too).
                if ($ThirdPath) { Probe Third "container-capacity|$bigChest|80" | Out-Null }
                $addedBig = 0
                for ($round = 0; $round -lt 20 -and @(((Probe Host "inv|$bigChest") -split "`n") | Where-Object { $_ -like '  *' }).Count -le 40; $round++) {
                    Probe Host "container-add|$bigChest|berry|200" | Out-Null
                    $addedBig += 200
                }
                Start-Sleep -Seconds 5
                $bigHost = ((Probe Host "inv|$bigChest") -split "`n" | Where-Object { $_ -like '  *' } | ForEach-Object { $_.Trim() } | Sort-Object) -join ';'
                $bigClient = ((Probe Client "inv|$bigChest") -split "`n" | Where-Object { $_ -like '  *' } | ForEach-Object { $_.Trim() } | Sort-Object) -join ';'
                $stacksBig = @($bigHost -split ';').Count
                Check ($stacksBig -gt 40 -and $bigClient -eq $bigHost) "1b: a big chest the host fills ($stacksBig stacks, $($bigHost.Length) characters) is the same for the joiner"
                Probe Client "container-remove|$bigChest|berry|$([int]($addedBig / 2))" | Out-Null
                Start-Sleep -Seconds 5
                $bigHost = ((Probe Host "inv|$bigChest") -split "`n" | Where-Object { $_ -like '  *' } | ForEach-Object { $_.Trim() } | Sort-Object) -join ';'
                $bigClient = ((Probe Client "inv|$bigChest") -split "`n" | Where-Object { $_ -like '  *' } | ForEach-Object { $_.Trim() } | Sort-Object) -join ';'
                Check ($bigClient -eq $bigHost -and $bigHost -ne '') "1b: what the joiner takes from the big chest is taken for the host too"
                Probe Host "container-remove|$bigChest|berry|$($addedBig - [int]($addedBig / 2))" | Out-Null
                Start-Sleep -Seconds 4
                Verify '1b a big chest'
            } else { Note $false '1b: no chest_rough for the big chest' }

            # 2. One drop, both picking it up at the same instant: one of them gets it.
            $drop = ((Line Host 'spawn|berry|6' 'SPAWN') -split ' ')[1]
            Start-Sleep -Seconds 4
            $ownHost = Own Host 'berry'; $ownClient = Own Client 'berry'
            Together "collect|$drop" "collect|$drop"
            $gotHost = (Own Host 'berry') - $ownHost; $gotClient = (Own Client 'berry') - $ownClient
            Check (($gotHost + $gotClient) -eq 6 -and ($gotHost -eq 0 -or $gotClient -eq 0)) "2: one drop picked up by both at once goes to one of them (host +$gotHost, joiner +$gotClient)"
            Verify '2 one drop, both at once'

            function GroundDrops([string]$Peer, [string]$item) { @(((Probe $Peer 'inspect') -split "`n") | Where-Object { $_ -match "^DROP (\S+) $item count=(\d+)" } | ForEach-Object { [pscustomobject]@{ Id = ($_ -split ' ')[1]; Count = [int]([regex]::Match($_, 'count=(\d+)').Groups[1].Value) } }) }
            function Berries { (Own Host 'berry') + (Own Client 'berry') + ((GroundDrops Host 'berry' | Measure-Object Count -Sum).Sum) }
            $mixLog = Join-Path $OutputPath 'mix-2-harvests-and-bag.txt'
            $ripeBeds = @(((Probe Host 'objects|garden') -split "`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^OBJ (\S+) garden_wheat_ready$' } | ForEach-Object { ($_ -split ' ')[1] })
            # 2c. The everyday case: one player harvests, the other only watches. Each yields once:
            # the watcher's game finishes the shared death a moment later, and ran it as its own
            # harvest (the whole harvest dropped a second time there, and was shared).
            if ($ripeBeds.Count -ge 5) {
                foreach ($pair in @(@('Host', $ripeBeds[2]), @('Client', $ripeBeds[3]))) {
                    $who = $pair[0]; $bedX = $pair[1]
                    $holdsX = [regex]::Match(((Probe Host 'audit') -split "`n" | Where-Object { $_ -like "AUDIT-CONTAINER $bedX *" }) -join '', 'wheatx(\d+)').Groups[1].Value
                    $w0 = (GroundDrops Host 'wheat' | Measure-Object Count -Sum).Sum + (Own Host 'wheat') + (Own Client 'wheat')
                    Probe $who "wgo-kill|$bedX" | Out-Null
                    Start-Sleep -Seconds 8
                    $w1 = (GroundDrops Host 'wheat' | Measure-Object Count -Sum).Sum + (Own Host 'wheat') + (Own Client 'wheat')
                    $gh = (GroundDrops Host 'wheat' | ForEach-Object { "x$($_.Count)" } | Sort-Object) -join ','
                    $gc = (GroundDrops Client 'wheat' | ForEach-Object { "x$($_.Count)" } | Sort-Object) -join ','
                    Add-Content -LiteralPath $mixLog -Value "$who harvests $bedX alone (holds wheat x$holdsX): wheat $w0 -> $w1; ground host [$gh] joiner [$gc]"
                    Check ($holdsX -and $w1 - $w0 -eq [int]$holdsX -and $gh -eq $gc) "2c: a bed the $who harvests alone yields once (wheat +$($w1 - $w0), the bed held $holdsX; ground host [$gh], joiner [$gc])"
                }
                Verify '2c one harvesting, one watching'
            } else { Note $false "2c: not enough ripe beds ($($ripeBeds.Count))" }

            # 2d. One ripe bed harvested by both at the same instant: it yields once (a ripe wheat
            # bed holds its harvest: wheat x6 among it), not once on each machine.
            if ($ripeBeds.Count -ge 3) {
                $bed2 = $ripeBeds[1]
                $holds = [regex]::Match(((Probe Host 'audit') -split "`n" | Where-Object { $_ -like "AUDIT-CONTAINER $bed2 *" }) -join '', 'wheatx(\d+)').Groups[1].Value
                $wheat0 = (GroundDrops Host 'wheat' | Measure-Object Count -Sum).Sum + (Own Host 'wheat') + (Own Client 'wheat')
                Together "wgo-kill|$bed2" "wgo-kill|$bed2"
                Start-Sleep -Seconds 6
                $wheat1 = (GroundDrops Host 'wheat' | Measure-Object Count -Sum).Sum + (Own Host 'wheat') + (Own Client 'wheat')
                $groundHost = (GroundDrops Host 'wheat' | ForEach-Object { "x$($_.Count)" } | Sort-Object) -join ','
                $groundClient = (GroundDrops Client 'wheat' | ForEach-Object { "x$($_.Count)" } | Sort-Object) -join ','
                Add-Content -LiteralPath $mixLog -Value "both harvest $bed2 (holds wheat x$holds): wheat $wheat0 -> $wheat1; ground host [$groundHost] joiner [$groundClient]"
                Check ($holds -and $wheat1 - $wheat0 -eq [int]$holds -and $groundHost -eq $groundClient) "2d: one bed harvested by both at once yields once (wheat +$($wheat1 - $wheat0), the bed held $holds; ground host [$groundHost], joiner [$groundClient])"
                Verify '2d one bed, both at once'
            } else { Note $false "2d: not enough ripe beds ($($ripeBeds.Count))" }

            # 2e. A full bag: the joiner picks up a drop with no room for it. The host decides with its
            # own copy of the joiner's bag, which can be behind; whatever happens, no berry may be
            # lost (joiner, host and the ground together), the ground must be the same for everyone,
            # and once there is room the joiner takes them.
            $jid = [regex]::Match((Probe Host 'players'), 'PLAYER id=(\d+) client').Groups[1].Value
            $size = [int](((Line Client 'capacity' 'CAPACITY') -split ' ')[1])
            $bagJoiner = @(((Probe Client 'pinv') -split "`n") | Where-Object { $_ -like 'PINV inventory *' }).Count
            Add-Content -LiteralPath $mixLog -Value ("joiner bag: size=$size stacks=$bagJoiner; host's copy: $(Line Host "record-bag|$jid" 'RECORD-BAG')")
            # A bag full as a player's is: every slot taken (a stick in its one slot), so berries do not fit.
            if ($bagJoiner -eq 0) {
                $stick = ((Line Host 'spawn|stick|1' 'SPAWN') -split ' ')[1]
                Start-Sleep -Seconds 4
                Probe Client "collect|$stick" | Out-Null
                Start-Sleep -Seconds 3
                $bagJoiner = @(((Probe Client 'pinv') -split "`n") | Where-Object { $_ -like 'PINV inventory *' }).Count
            }
            $slots = [Math]::Max(1, $bagJoiner)
            Add-Content -LiteralPath $mixLog -Value "bag made full: $bagJoiner stack(s) in $slots slot(s)"
            $all0 = Berries
            Probe Client "capacity|$slots" | Out-Null
            $full = ((Line Host 'spawn|berry|5' 'SPAWN') -split ' ')[1]
            Start-Sleep -Seconds 4
            $ownClient = Own Client 'berry'
            Probe Client "collect|$full" | Out-Null
            Start-Sleep -Seconds 5
            $gotFull = (Own Client 'berry') - $ownClient
            $all1 = Berries
            $groundHost = (GroundDrops Host 'berry' | ForEach-Object { "x$($_.Count)" } | Sort-Object) -join ','
            $groundClient = (GroundDrops Client 'berry' | ForEach-Object { "x$($_.Count)" } | Sort-Object) -join ','
            Add-Content -LiteralPath $mixLog -Value "full: joiner +$gotFull; berries $all0 -> $all1; ground host [$groundHost] joiner [$groundClient]"
            Probe Client "capacity|$size" | Out-Null
            Check ($gotFull -eq 0 -and $all1 -eq $all0 + 5 -and $groundHost -eq $groundClient) "2e: with a full bag the joiner gets nothing and no berry is lost (joiner +$gotFull; berries $all0 -> $all1, 5 dropped; ground host [$groundHost], joiner [$groundClient])"
            Start-Sleep -Seconds 1
            $five = (GroundDrops Client 'berry' | Where-Object { $_.Count -eq 5 } | Select-Object -First 1).Id
            if ($five) { Probe Client "collect|$five" | Out-Null }
            Start-Sleep -Seconds 4
            $gotLater = (Own Client 'berry') - $ownClient
            Check ($gotLater -eq 5) "2e: with room again the joiner picks it up (+$gotLater)"
            # The bag filling up between asking and the host's answer: what no longer fits goes back
            # where it lay, for everyone.
            $all2 = Berries
            $race = ((Line Host 'spawn|berry|4' 'SPAWN') -split ' ')[1]
            Start-Sleep -Seconds 4
            $ownRace = Own Client 'berry'
            $at = [DateTime]::UtcNow.AddSeconds(2).Ticks
            Probe Client "at|$at|collect|$race && capacity|$slots" | Out-Null
            Start-Sleep -Seconds 7
            $gotRace = (Own Client 'berry') - $ownRace
            Probe Client "capacity|$size" | Out-Null
            $all3 = Berries
            $groundHost = (GroundDrops Host 'berry' | ForEach-Object { "x$($_.Count)" } | Sort-Object) -join ','
            $groundClient = (GroundDrops Client 'berry' | ForEach-Object { "x$($_.Count)" } | Sort-Object) -join ','
            Add-Content -LiteralPath $mixLog -Value "race: joiner +$gotRace; berries $all2 -> $all3; ground host [$groundHost] joiner [$groundClient]"
            # Picked up again (room is back), so it does not merge with the next phase's drops.
            $four = (GroundDrops Client 'berry' | Where-Object { $_.Count -eq 4 } | Select-Object -First 1).Id
            if ($four) { Probe Client "collect|$four" | Out-Null; Start-Sleep -Seconds 3 }
            Check ($gotRace -eq 0 -and $all3 -eq $all2 + 4 -and $groundHost -match 'x4' -and $groundHost -eq $groundClient) "2e: what no longer fits when the host's answer comes goes back on the ground for everyone (joiner +$gotRace; berries $all2 -> $all3, 4 dropped; ground host [$groundHost], joiner [$groundClient])"
            Verify '2e a full bag'

            if ($ThirdPath) {
                # 2b. The two joiners, without the host, at the same instant: one chest, then one drop.
                # Everything between them goes through the host.
                function TogetherJoiners([string]$clientCommand, [string]$thirdCommand) {
                    $at = [DateTime]::UtcNow.AddSeconds(3).Ticks
                    Probe Client "at|$at|$clientCommand" | Out-Null
                    Probe Third "at|$at|$thirdCommand" | Out-Null
                    Start-Sleep -Seconds 6
                }
                $before2 = Total Host 'berry'
                TogetherJoiners "container-add|$chest|berry|4" "container-add|$chest|berry|6"
                $after2 = Total Host 'berry'
                Check ($after2 -eq $before2 + 10 -and (Total Client 'berry') -eq $after2 -and (Total Third 'berry') -eq $after2) "2b: two joiners filling one chest at once: every berry arrives once ($before2 + 10 -> host $after2, joiner $(Total Client 'berry'), third $(Total Third 'berry'))"
                $drop2 = ((Line Host 'spawn|berry|5' 'SPAWN') -split ' ')[1]
                Start-Sleep -Seconds 4
                $ownC = Own Client 'berry'; $ownT = Own Third 'berry'
                TogetherJoiners "collect|$drop2" "collect|$drop2"
                $gotC = (Own Client 'berry') - $ownC; $gotT = (Own Third 'berry') - $ownT
                Check (($gotC + $gotT) -eq 5 -and ($gotC -eq 0 -or $gotT -eq 0)) "2b: one drop picked up by two joiners at once goes to one of them (joiner +$gotC, third +$gotT)"
                Verify '2b the two joiners at once'
            }

            # 3. The joiner orders a craft and leaves at once; the host's station carries on.
            $candidates = @(((Probe Client 'stations') -split "`n") | Where-Object { $_ -match 'startable=(?!none)' -and $_ -match ' shared=True ' -and $_ -match 'queue=0 cur=-' })
            if ($candidates.Count) {
                $null = $candidates[0] -match '^\s*STATION (\S+) (\S+) .*startable=(\S+)'
                $station = $Matches[1]; $recipe = $Matches[3]
                Probe Client "craft|$station|$recipe" | Out-Null
                Probe Client 'go-to-menu' | Out-Null
                Check ([bool](WaitState Client 'MainMenu' 60)) '3: the joiner leaves right after ordering a craft'
                Start-Sleep -Seconds 4
                $hostStation = ((Probe Host 'stations') -split "`n" | Where-Object { $_ -match "STATION $station " }) -join ''
                Check ($hostStation -notmatch 'queue=0 cur=-') "3: the host's station keeps the craft the joiner ordered ($hostStation)"
                Rejoin '3'
                Verify '3 craft, then leaving'
            } else { Note $false '3: no idle shared station with a startable recipe for the craft-then-leave phase' }

            # 4. The host builds and fills a chest while the joiner is joining (during the copy).
            Probe Client 'go-to-menu' | Out-Null
            $null = WaitState Client 'MainMenu' 60
            Start-Sleep -Seconds 3
            $joined = Count $clientLog 'Attached client networking to the normally initialized local game world.'
            $accepted = Count $clientLog 'accepted the connection'
            Probe Client "menu-bootstrap|127.0.0.1|$hostPort" | Out-Null
            # Meanwhile the joiner looks at the credits (the menu is idle while the world copies):
            # the join used to wait for the main menu to show again, for ever.
            $credits = Line Client 'menu-click|Credits' 'MENU-CLICK'
            Start-Sleep -Seconds 2
            $built = ((Line Host 'build-place|chest' 'BUILD-PLACE') -split ' ')[2]
            Probe Host "container-add|$built|berry|4" | Out-Null
            # And the rest of what a host does while a friend loads: a drop, the old chest, a zombie.
            Probe Host 'spawn|berry|2' | Out-Null
            Probe Host "container-add|$chest|berry|1" | Out-Null
            $zombie = ((Line Host 'zombie-make' 'ZOMBIE-MAKE') -split ' ')[1]
            # And a harvest: the game puts an empty bed in the ripe one's place, under the same id.
            $ripeBed = @(((Probe Host 'objects|garden') -split "`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^OBJ (\S+) \S*_ready$' } | ForEach-Object { ($_ -split ' ')[1] }) | Select-Object -Last 1
            if ($ripeBed) { Probe Host "wgo-kill|$ripeBed" | Out-Null }
            $ok = (WaitCount $clientLog 'Attached client networking to the normally initialized local game world.' $joined 300) -and (WaitCount $clientLog 'accepted the connection' $accepted 60)
            Check $ok "4: the joiner joins while the host builds, with the credits open over the menu meanwhile ($credits)"
            Start-Sleep -Seconds 8
            if ($ripeBed) {
                $bedHost = ((Probe Host 'objects|garden') -split "`n" | Where-Object { $_ -match [regex]::Escape($ripeBed) } | ForEach-Object { $_.Trim() }) -join ''
                $bedClient = ((Probe Client 'objects|garden') -split "`n" | Where-Object { $_ -match [regex]::Escape($ripeBed) } | ForEach-Object { $_.Trim() }) -join ''
                Check ($bedHost -notmatch '_ready$' -and $bedClient -eq $bedHost) "4: a bed the host harvested during the join is harvested for the joiner (host '$bedHost'; joiner '$bedClient')"
            } else { Note $false '4: no ripe bed for the harvest during the join' }
            Check ($zombie -and (Probe Client 'zombies') -match [regex]::Escape($zombie)) "4: a zombie the host made during the join is there for the joiner ($zombie)"
            $inHost = ((Probe Host "inv|$built") -split "`n" | Where-Object { $_ -like '  *' }) -join ';'
            $inClient = ((Probe Client "inv|$built") -split "`n" | Where-Object { $_ -like '  *' }) -join ';'
            Check ((Line Client "has|$built" 'HAS') -match 'True$' -and $inHost -match 'berry' -and $inClient -eq $inHost) "4: a chest the host built and filled during the join is there for the joiner, with its contents (host: $inHost; joiner: $inClient; has on joiner: $(Line Client "has|$built" 'HAS'))"
            Verify '4 building during the join'

            # 5. The joiner is in another scene while the host builds, removes and drops things.
            Probe Client 'teleport-wgo|scene:Prison' | Out-Null
            Start-Sleep -Seconds 10
            $away = ((Line Host 'build-place|chest' 'BUILD-PLACE') -split ' ')[2]
            Probe Host "container-add|$away|berry|2" | Out-Null
            Probe Host "build-remove|$built" | Out-Null
            Probe Host 'spawn|berry|3' | Out-Null
            Start-Sleep -Seconds 4
            Probe Client "teleport-wgo|$chest" | Out-Null
            Start-Sleep -Seconds 12
            Check ((Line Client "has|$away" 'HAS') -match 'True$' -and (Line Client "has|$built" 'HAS') -match 'False$') '5: what the host built and removed while the joiner was elsewhere is so for the joiner'
            Verify '5 the joiner elsewhere'

            # 6. The host's sleep passes the night while the joiner, awake, works the chest.
            Probe Host 'night-rule|Host' | Out-Null
            Probe Host 'sleep' | Out-Null
            Start-Sleep -Seconds 4
            Probe Client "container-add|$chest|berry|1" | Out-Null
            Probe Client "container-remove|$chest|berry|1" | Out-Null
            $deadline = (Get-Date).AddSeconds(90)
            do { Start-Sleep -Seconds 3; $sp = Line Host 'speed' 'SPEED' } while ($sp -match 'asleep=True' -and (Get-Date) -lt $deadline)
            Probe Host 'night-rule|Everyone' | Out-Null
            Verify '6 a night passing'

            # 6b. Midnight: the host's clock turns the day while the others' do too. A correction
            # sent at the instant of turning (day N at 1.0000) set a joiner back to day N at 1.0,
            # which the game reads as another day (`chaos3`: day 1 against the host's 6).
            Probe Host 'time|0.9985' | Out-Null
            Start-Sleep -Seconds 25
            $clockHost = Line Host 'audit' 'AUDIT-CLOCK'
            foreach ($joiner in $others) {
                $clockJoiner = Line $joiner 'audit' 'AUDIT-CLOCK'
                $joinerLog = if ($joiner -eq 'Third') { $thirdLog } else { $clientLog }
                $toOne = @(Select-String -LiteralPath $joinerLog -Pattern 'to host day \d+ 1[,.]0000' -ErrorAction SilentlyContinue).Count
                Check ($clockJoiner -eq $clockHost -and $toOne -eq 0) "6b: after midnight $joiner is on the host's day ($clockJoiner / host $clockHost; corrections to a time of 1.0: $toOne)"
            }

            # 7. The host's game ends (closed, or crashed) and starts again; the joiner, still in
            # the same game, is told, goes to the menu and joins again. The new host numbers its
            # chest updates from the start again: a joiner who kept the old numbers refused them.
            function StatusUi([string]$Arg, [string]$Peer = 'Client') { ((((Probe $Peer "status-ui|$Arg") -split "`n") | Where-Object { $_ -like 'STATUS-UI *' }) -join '').Substring(10) }
            try { Probe Host 'quit' | Out-Null } catch { Write-Host "Quit reply unavailable: $_" }
            if (-not $peers[0].Process.WaitForExit(60000)) { throw 'The host did not quit.' }
            $peers[0].Process = $null
            $deadline = (Get-Date).AddSeconds(90)
            do { Start-Sleep -Seconds 2; $told = StatusUi 'describe' } while ((Get-Date) -lt $deadline -and $told -notmatch '^Failed')
            Check ($told -match '^Failed') "7: the joiner is told when the host's game ends ($told)"
            StatusUi 'ok' | Out-Null
            Check ([bool](WaitState Client 'MainMenu' 60)) '7: OK takes the joiner to the main menu'
            if ($ThirdPath) {
                $deadline = (Get-Date).AddSeconds(60)
                do { Start-Sleep -Seconds 2; $toldThird = StatusUi 'describe' 'Third' } while ((Get-Date) -lt $deadline -and $toldThird -notmatch '^Failed')
                Check ($toldThird -match '^Failed') "7: the third player is told as well ($toldThird)"
                StatusUi 'ok' 'Third' | Out-Null
                Check ([bool](WaitState Third 'MainMenu' 60)) '7: OK takes the third player to the main menu'
            }
            Move-Item -LiteralPath $hostLog -Destination (Join-Path $OutputPath 'Host-log-first-session.txt') -Force
            $env:GK2COOP_TEST_CONTINUE_SLOT = $slot
            $peers[0].Process = Start-TestGame $HostPath
            Remove-Item Env:\GK2COOP_TEST_CONTINUE_SLOT
            WaitFor $hostLog ("Invoked the game's Continue button for isolated slot $slot") 120
            WaitFor $hostLog 'Attached native host networking' 180
            Start-Sleep -Seconds 5
            Rejoin '7'
            if ($ThirdPath) {
                # In the world, not the menu-stage connection that copies it (that one attaches too).
                $joinedThird = Count $thirdLog 'Automatic client connection on normal game completed.'
                Probe Third "menu-bootstrap|127.0.0.1|$hostPort|Third" | Out-Null
                Check ((WaitCount $thirdLog 'Automatic client connection on normal game completed.' $joinedThird 300) -and (WaitState Third 'InGame' 60)) '7: the third player joins again'
                Start-Sleep -Seconds 8
            }
            Probe Host "container-add|$chest|berry|2" | Out-Null
            Start-Sleep -Seconds 5
            $hostChest = ((Probe Host "inv|$chest") -split "`n" | Where-Object { $_ -like '  *' } | ForEach-Object { $_.Trim() } | Sort-Object) -join ';'
            $clientChest = ((Probe Client "inv|$chest") -split "`n" | Where-Object { $_ -like '  *' } | ForEach-Object { $_.Trim() } | Sort-Object) -join ';'
            Check ($hostChest -match 'berry' -and $clientChest -eq $hostChest) "7: after the host's restart a chest change reaches the joiner (host $hostChest; joiner $clientChest)"
            # And a death (the death sync numbers its messages too): the host harvests a ripe bed.
            $ripe = @(((Probe Host 'objects|garden') -split "`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^OBJ (\S+) \S*_ready$' })
            if ($ripe.Count) {
                $null = $ripe[0] -match '^OBJ (\S+) '
                $bedId = $Matches[1]
                Probe Host "wgo-kill|$bedId" | Out-Null
                Start-Sleep -Seconds 6
                $hostBed = ((Probe Host 'objects|garden') -split "`n" | Where-Object { $_ -match [regex]::Escape($bedId) } | ForEach-Object { $_.Trim() }) -join ''
                $clientBed = ((Probe Client 'objects|garden') -split "`n" | Where-Object { $_ -match [regex]::Escape($bedId) } | ForEach-Object { $_.Trim() }) -join ''
                Check ($hostBed -notmatch '_ready$' -and $clientBed -eq $hostBed) "7: after the host's restart a harvest reaches the joiner (host '$hostBed'; joiner '$clientBed')"
            } else { Note $false '7: no ripe bed left for the harvest after the restart' }
            Verify '7 the host started again'

            # 8. The joiner takes from a chest, and a moment later their game ends (a crash, a lost
            # connection). The chest changed on the host at once; what the joiner carries is what the
            # host stored for them. Nothing may be lost or doubled.
            function ChestBerries([string]$Peer) { $line = ((Probe $Peer "inv|$chest") -split "`n" | Where-Object { $_ -match '^\s+berryx(\d+)' }) -join ''; if ($line -match 'berryx(\d+)') { [int]$Matches[1] } else { 0 } }
            Probe Host "container-add|$chest|berry|5" | Out-Null
            Start-Sleep -Seconds 4
            $chestBefore = ChestBerries Host; $carriedBefore = Own Client 'berry'
            Probe Client "container-remove|$chest|berry|3" | Out-Null
            Probe Client 'give|berry|3' | Out-Null
            Start-Sleep -Seconds 4
            $peers[1].Process.Kill()
            $peers[1].Process.WaitForExit(20000) | Out-Null
            $peers[1].Process = $null
            Start-Sleep -Seconds 5
            Move-Item -LiteralPath $clientLog -Destination (Join-Path $OutputPath 'Client-log-before-crash.txt') -Force
            $env:GK2COOP_TEST_MENU_CONNECT = '1'
            $peers[1].Process = Start-TestGame $ClientPath
            WaitFor $clientLog 'gameState=MainMenu' 150
            Probe Client "menu-bootstrap|127.0.0.1|$hostPort" | Out-Null
            WaitFor $clientLog 'Attached client networking to the normally initialized local game world.' 300
            WaitFor $clientLog 'Player profile: restored' 90
            Start-Sleep -Seconds 6
            $chestAfter = ChestBerries Host; $carriedAfter = Own Client 'berry'
            Check (($chestAfter + $carriedAfter) -eq ($chestBefore + $carriedBefore)) "8: the joiner's game ending right after taking from a chest loses and doubles nothing (chest $chestBefore -> $chestAfter, carried $carriedBefore -> $carriedAfter)"
            Verify '8 a crash right after taking'

            if ($ChaosMinutes -gt 0) {
                # 9. Chaos: both players at random, for minutes: berries moved between bags and both
                # chests (now and then the two at the same instant on one chest), drops made and
                # picked up (now and then by both at once). Every two minutes: the worlds are one,
                # nothing for the watchdog, and every berry is where it can be counted (chests, bags,
                # the ground), none made and none lost but the drops the host made.
                function Conserved {
                    $c = Probe Host 'count|berry'
                    $h = if ($c -match 'player=(\d+) containers=(\d+)') { [int]$Matches[1] + [int]$Matches[2] } else { -100000 }
                    $h + (Own Client 'berry') + [int]((GroundDrops Host 'berry' | Measure-Object Count -Sum).Sum)
                }
                $chaosLog = Join-Path $OutputPath 'chaos.txt'
                Probe Host 'give|berry|30' | Out-Null
                Probe Client 'give|berry|30' | Out-Null
                Start-Sleep -Seconds 4
                $chaosBase = Conserved; $made = 0
                [IO.File]::WriteAllText($chaosLog, "base $chaosBase`n")
                $chests = @($chest) + $(if ($bigChest) { @($bigChest) } else { @() })
                $chaosEnd = (Get-Date).AddMinutes($ChaosMinutes); $nextChaosCheck = (Get-Date).AddMinutes(2); $chaosRound = 0
                while ((Get-Date) -lt $chaosEnd) {
                    $chaosRound++
                    $target = $chests | Get-Random
                    $k = Get-Random -Minimum 1 -Maximum 6
                    $act = Get-Random -Minimum 0 -Maximum 8
                    switch ($act) {
                        0 { $r = Line Host "move-to-chest|$target|berry|$k" 'MOVED' }
                        1 { $r = Line Host "move-from-chest|$target|berry|$k" 'MOVED' }
                        2 { $r = Line Client "move-to-chest|$target|berry|$k" 'MOVED' }
                        3 { $r = Line Client "move-from-chest|$target|berry|$k" 'MOVED' }
                        4 { Together "move-to-chest|$target|berry|$k" "move-from-chest|$target|berry|$k"; $r = 'together on a chest' }
                        5 { $r = Line Host "spawn|berry|$k" 'SPAWN'; if ($r) { $made += $k } }
                        6 {
                            $g = GroundDrops Client 'berry' | Get-Random
                            if ($g) { $who = @('Host', 'Client') | Get-Random; $r = "$who collects " + (Line $who "collect|$($g.Id)" 'COLLECT') } else { $r = 'no drop' }
                        }
                        7 {
                            $g = GroundDrops Client 'berry' | Get-Random
                            if ($g) { Together "collect|$($g.Id)" "collect|$($g.Id)"; $r = "both collect x$($g.Count)" } else { $r = 'no drop' }
                        }
                    }
                    Add-Content -LiteralPath $chaosLog -Value "$chaosRound act=$act k=$k $target :: $r"
                    Start-Sleep -Milliseconds (Get-Random -Minimum 300 -Maximum 1500)
                    if ((Get-Date) -ge $nextChaosCheck -or (Get-Date) -ge $chaosEnd) {
                        $nextChaosCheck = (Get-Date).AddMinutes(2)
                        Start-Sleep -Seconds 6
                        $now = Conserved
                        Add-Content -LiteralPath $chaosLog -Value "check after round ${chaosRound}: $now berries, expected $($chaosBase + $made)"
                        Check ($now -eq $chaosBase + $made) "9 chaos, round ${chaosRound}: every berry is accounted for ($now, expected $chaosBase + $made made by the host)"
                        Verify "9 chaos round $chaosRound"
                    }
                }
            }
        }
        if ($RehostExperiment) {
            # Found in play: the host went to the main menu, hosted again in the same game, and the
            # friend joined again; the host then saw three keepers ("Host" and a double of itself).
            # Three rounds; in the first the host leaves with the joiner still in the world, in the
            # others the joiner leaves first.
            function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
            function StatusUi([string]$Arg) { ((((Probe Client "status-ui|$Arg") -split "`n") | Where-Object { $_ -like 'STATUS-UI *' }) -join '').Substring(10) }
            function Count([string]$log, [string]$pattern) { @(Select-String -LiteralPath $log -Pattern $pattern -SimpleMatch).Count }
            function WaitCount([string]$log, [string]$pattern, [int]$above, [int]$seconds) {
                $deadline = (Get-Date).AddSeconds($seconds)
                while ((Get-Date) -lt $deadline) { if ((Count $log $pattern) -gt $above) { return $true }; Start-Sleep -Seconds 2 }
                return $false
            }
            function WaitState([string]$Peer, [string]$state, [int]$seconds) {
                $deadline = (Get-Date).AddSeconds($seconds)
                while ((Get-Date) -lt $deadline) { $s = Line $Peer 'game-state' 'GAME-STATE'; if ($s -match $state) { return $s }; Start-Sleep -Seconds 2 }
                return $null
            }
            $rounds = New-Object System.Collections.Generic.List[string]
            for ($round = 1; $round -le 3; $round++) {
                if ($round -gt 1) {
                    Probe Client 'go-to-menu' | Out-Null
                    $clientMenu = WaitState Client 'MainMenu' 60
                    Check ([bool]$clientMenu) "Round ${round}: the joiner leaves to the main menu"
                }
                Probe Host 'go-to-menu' | Out-Null
                $hostMenu = WaitState Host 'MainMenu' 60
                if ($round -eq 1) {
                    $deadline = (Get-Date).AddSeconds(60)
                    do { Start-Sleep -Seconds 2; $told = StatusUi 'describe' } while ((Get-Date) -lt $deadline -and $told -notmatch '^Failed')
                    Check ($told -match '^Failed') "Round 1: the joiner in the world is told the host left ($told)"
                    StatusUi 'ok' | Out-Null
                    Check ([bool](WaitState Client 'MainMenu' 60)) 'Round 1: OK takes the joiner to the main menu'
                }
                Check ([bool]$hostMenu) "Round ${round}: the host goes to the main menu ($hostMenu)"
                Start-Sleep -Seconds 3
                $hostedBefore = Count $hostLog 'Attached native host networking to the normally initialized game world.'
                $joinedBefore = Count $clientLog 'Attached client networking to the normally initialized local game world.'
                $acceptedBefore = Count $clientLog 'accepted the connection'
                Probe Host 'menu-host' | Out-Null
                Probe Host "menu-continue|$slot" | Out-Null
                Check (WaitCount $hostLog 'Attached native host networking to the normally initialized game world.' $hostedBefore 240) "Round ${round}: the host loads the world again and hosts"
                Start-Sleep -Seconds 5
                Probe Client "menu-bootstrap|127.0.0.1|$hostPort" | Out-Null
                $joined = (WaitCount $clientLog 'Attached client networking to the normally initialized local game world.' $joinedBefore 300) -and
                          (WaitCount $clientLog 'accepted the connection' $acceptedBefore 60)
                Check $joined "Round ${round}: the joiner joins again"
                Start-Sleep -Seconds 8
                $hb = @((Probe Host 'body-players') -split "`n" | Where-Object { $_ -like 'BODY *' })
                $cb = @((Probe Client 'body-players') -split "`n" | Where-Object { $_ -like 'BODY *' })
                $hp = @((Probe Host 'players') -split "`n" | Where-Object { $_ -like 'PLAYER *' })
                $hw = Line Host 'watchdog' 'WATCHDOG'; $cw = Line Client 'watchdog' 'WATCHDOG'
                $rounds.Add("round $round`nhost bodies:`n$($hb -join "`n")`nclient bodies:`n$($cb -join "`n")`nhost players:`n$($hp -join "`n")`nhost $hw`nclient $cw`n")
                [IO.File]::WriteAllText((Join-Path $OutputPath 'rehost.txt'), ($rounds -join "`n"))
                $unowned = @($hb + $cb | Where-Object { $_ -match '^BODY id= ' }).Count
                Check ($hb.Count -eq 2 -and $cb.Count -eq 2 -and $hp.Count -eq 2 -and $unowned -eq 0) "Round ${round}: each side sees exactly two keepers, each one a player, and the host holds two player records (host $($hb.Count) bodies/$($hp.Count) records, joiner $($cb.Count) bodies, $unowned belonging to nobody)"
                Check ($hw -match 'repairs=0' -and $cw -match 'repairs=0') "Round ${round}: nothing left over for the watchdog to repair (host: $hw; joiner: $cw)"
                Check (@(Fallen ($hb + $cb)).Count -eq 0) "Round ${round}: no keeper has fallen through the world ($((Fallen ($hb + $cb)) -join '; '))"
                $id = [regex]::Match(($hp -join ' '), 'PLAYER id=(\d+) client').Groups[1].Value
                $before = ($hb | Where-Object { $_ -like "BODY id=$id *" }) -join ''
                Probe Client 'move|3|0' | Out-Null
                Start-Sleep -Seconds 3
                $after = ((Probe Host 'body-players') -split "`n" | Where-Object { $_ -like "BODY id=$id *" }) -join ''
                Check ($id -and $before -and $before -ne $after) "Round ${round}: the host sees the joiner move ($before -> $after)"
                Probe Host "shot|$(Join-Path $OutputPath "rehost-$round-host.png")" | Out-Null
                Probe Client "shot|$(Join-Path $OutputPath "rehost-$round-joiner.png")" | Out-Null
                Start-Sleep -Seconds 2
            }
        }
        if ($ProfileRejoin) {
            WaitFor $clientLog 'Player profile: new player' 60
            $hostProfile = Probe Host 'profile'
            $clientProfile = Probe Client 'profile'
            [IO.File]::WriteAllText((Join-Path $OutputPath 'profile-first-join.txt'), "$hostProfile`n$clientProfile")
            $hostMoney = if ($hostProfile -match 'money=([0-9.]+)') { $Matches[1] } else { '?' }
            Check ($clientProfile -match 'outcome=new player' -and $clientProfile -notmatch "money=$([regex]::Escape($hostMoney)) ") "A first-time joiner starts with their own inventory, not the host's ($clientProfile vs host $hostProfile)"
            $hostTalents = (((Probe Host 'talents') -split "`n") | Where-Object { $_ -like 'TALENTS *' }) -join ''
            $clientTalents = (((Probe Client 'talents') -split "`n") | Where-Object { $_ -like 'TALENTS *' }) -join ''
            # The host's levels, so the stations are usable; progress is separate from here on.
            Check ($clientTalents -eq $hostTalents) "A first-time joiner starts from the world's talent levels (joiner $clientTalents; host $hostTalents)"
            # Zone ratings live in the same store as money but are world state: they must stay the host's.
            $hostZone = Probe Host 'resource|wz_graveyard'
            $clientZone = Probe Client 'resource|wz_graveyard'
            Check ($hostZone -match 'wz_graveyard=-?[1-9]' -and ($hostZone -replace '^.*RESOURCE ','') -eq ($clientZone -replace '^.*RESOURCE ','')) "A new joiner keeps the world's zone rating ($clientZone, host $hostZone)"
            # Appearance: each side draws the other with that player's own look.
            Start-Sleep -Seconds 6
            $hostLooks = (((Probe Host 'looks') -split "`n") | Where-Object { $_ -like 'LOCAL *' }) -join ''
            $clientLooks = (((Probe Client 'looks') -split "`n") | Where-Object { $_ -like 'LOCAL *' }) -join ''
            [IO.File]::WriteAllText((Join-Path $OutputPath 'looks.txt'), "HOST   $hostLooks`nCLIENT $clientLooks")
            $hostOwn = ($hostLooks -split ' ')[1]; $hostSeesClient = ($hostLooks -split ' ')[3]
            $clientOwn = ($clientLooks -split ' ')[1]; $clientSeesHost = ($clientLooks -split ' ')[3]
            Check ($hostSeesClient -eq $clientOwn -and $clientSeesHost -eq $hostOwn) "Each player is drawn with their own look on the other screen (host $hostLooks; client $clientLooks)"
            Check ($clientOwn -ne $hostOwn) 'A new joiner no longer wears the host''s look'
            $open = (((Probe Client 'open-look') -split "`n") | Where-Object { $_ -like 'OPEN-LOOK *' }) -join ''
            Check ($open -match 'open=True') "The change-look key opens the game's customization window ($open)"
            Probe Client 'recolor' | Out-Null
            Start-Sleep -Seconds 8
            $clientNew = ((((Probe Client 'looks') -split "`n") | Where-Object { $_ -like 'LOCAL *' }) -join '' -split ' ')[1]
            $hostSeesNew = ((((Probe Host 'looks') -split "`n") | Where-Object { $_ -like 'LOCAL *' }) -join '' -split ' ')[3]
            Add-Content -LiteralPath (Join-Path $OutputPath 'looks.txt') -Value "after recolor: client $clientNew, host sees $hostSeesNew"
            Check ($clientNew -ne $clientOwn -and $hostSeesNew -eq $clientNew) "A changed look reaches the other player (client $clientNew, host sees $hostSeesNew)"
            # Where the joiner was when they left is where they come back.
            $startSpot = ((Probe Client 'where') -split "`n" | Where-Object { $_ -like 'WHERE *' }) -join ''
            Probe Client 'nudge|3|2' | Out-Null
            Start-Sleep -Seconds 2
            $leftAt = ((Probe Client 'where') -split "`n" | Where-Object { $_ -like 'WHERE *' }) -join ''
            Probe Client 'setres|money|4321' | Out-Null
            Probe Client 'talent-exp|37' | Out-Null
            $talentsLeft = (((Probe Client 'talents') -split "`n") | Where-Object { $_ -like 'TALENTS *' }) -join ''
            # Something with nested items and item properties to carry across the rejoin.
            Probe Client 'inv-take|9b9f7199-4781-49b8-a34b-42c4e33d5503|body_corpse' | Out-Null
            $carriedBefore = (((Probe Client 'pinv') -split "`n") | Where-Object { $_ -like 'PINV *' } | ForEach-Object { $_.TrimEnd() }) -join "`n"
            $storedBefore = @(Select-String -LiteralPath $hostLog -Pattern 'Player profile: stored').Count
            $deadline = (Get-Date).AddSeconds(45)
            while ((Get-Date) -lt $deadline -and @(Select-String -LiteralPath $hostLog -Pattern 'Player profile: stored').Count -le $storedBefore) { Start-Sleep -Seconds 3 }
            $profileDir = Join-Path $HostPath "BepInEx\config\GK2Coop\players\$slot"
            Check (@(Get-ChildItem -LiteralPath $profileDir -Filter '*.dat' -ErrorAction SilentlyContinue).Count -eq 1) 'The host stores the joiner''s state beside its config, not in the save'
            # Where they are as they leave: after the teleport the body keeps sliding slowly (about
            # 0.06 units a second, 0.6 to 2.9 units by the time of leaving; 0.63.1 did the same), and
            # the state the host keeps is the one the joiner sends as it quits.
            $leftAt = ((Probe Client 'where') -split "`n" | Where-Object { $_ -like 'WHERE *' }) -join ''
            # The game can close before it writes the probe's reply; the exit itself is the answer.
            try { Probe Client 'quit' | Out-Null } catch { Write-Host "Quit reply unavailable; checking the process: $_" }
            if (-not $peers[1].Process.WaitForExit(60000)) { throw 'The joiner did not quit.' }
            $peers[1].Process = $null
            WaitFor $hostLog 'has Disconnected' 60
            Start-Sleep -Seconds 3
            # Moved, not copied: otherwise the waits below match the first session's lines before
            # the relaunched game has replaced the log.
            Move-Item -LiteralPath $clientLog -Destination (Join-Path $OutputPath 'Client-log-first-session.txt') -Force
            # The probe only arms itself for a menu-stage test when this is set at process start.
            $env:GK2COOP_TEST_MENU_CONNECT = '1'
            $peers[1].Process = Start-TestGame $ClientPath
            Remove-Item Env:\GK2COOP_TEST_MENU_CONNECT
            WaitFor $clientLog 'gameState=MainMenu' 120
            Probe Client "menu-bootstrap|127.0.0.1|$hostPort" | Out-Null
            WaitFor $clientLog 'Verified and imported host save into isolated slot' 180
            WaitFor $clientLog 'Attached client networking to the normally initialized local game world.' 240
            WaitFor $clientLog 'Player profile: restored' 90
            $hostAfter = Probe Host 'profile'
            $clientAfter = Probe Client 'profile'
            [IO.File]::WriteAllText((Join-Path $OutputPath 'profile-rejoin.txt'), "$hostAfter`n$clientAfter")
            Check ($clientAfter -match 'money=4321 ' -and $clientAfter -match 'outcome=restored') "A returning joiner gets their own state back after copying the world again ($clientAfter)"
            Check ($hostAfter -eq $hostProfile) "The host's own player state is untouched ($hostAfter)"
            $carriedAfter = (((Probe Client 'pinv') -split "`n") | Where-Object { $_ -like 'PINV *' } | ForEach-Object { $_.TrimEnd() }) -join "`n"
            [IO.File]::WriteAllText((Join-Path $OutputPath 'carried.txt'), "BEFORE`n$carriedBefore`nAFTER`n$carriedAfter")
            Check ($carriedBefore -match 'props=' -and $carriedAfter -eq $carriedBefore) "A returning joiner's items come back exactly, item properties included ($(@($carriedAfter -split "`n" | Where-Object { $_ -match 'props=' }).Count) with properties; see carried.txt)"
            $talentsBack = (((Probe Client 'talents') -split "`n") | Where-Object { $_ -like 'TALENTS *' }) -join ''
            Check ($talentsBack -eq $talentsLeft -and $talentsBack -match ':37/') "A returning joiner gets their own talent progress back ($talentsBack)"
            Check (((((Probe Host 'talents') -split "`n") | Where-Object { $_ -like 'TALENTS *' }) -join '') -eq $hostTalents) 'The host''s talents are untouched by the joiner''s'
            $backAt = ((Probe Client 'where') -split "`n" | Where-Object { $_ -like 'WHERE *' }) -join ''
            function Spot([string]$line) { $p = $line -split ' '; return @([double]::Parse($p[1], [Globalization.CultureInfo]::InvariantCulture), [double]::Parse($p[2], [Globalization.CultureInfo]::InvariantCulture)) }
            $a = Spot $leftAt; $b = Spot $backAt; $c = Spot $startSpot
            $gap = [Math]::Sqrt([Math]::Pow($a[0]-$b[0],2) + [Math]::Pow($a[1]-$b[1],2))
            $moved = [Math]::Sqrt([Math]::Pow($a[0]-$c[0],2) + [Math]::Pow($a[1]-$c[1],2))
            Check ($moved -gt 1 -and $gap -lt 1.5) "A returning joiner is back where they left (left at $leftAt, back at $backAt, the copied world starts at $startSpot)"
            $clientZoneAfter = Probe Client 'resource|wz_graveyard'
            # Compared with the host now: the body taken out of a grave above lowers the rating on both.
            $hostZoneAfter = Probe Host 'resource|wz_graveyard'
            Check (($clientZoneAfter -replace '^.*RESOURCE ','') -eq ($hostZoneAfter -replace '^.*RESOURCE ','')) "A returning joiner keeps the world's zone rating ($clientZoneAfter)"
        }
        if ($AuditExperiment) { Invoke-Audit }
        return
    }
    if ($MenuConnectClient) {
        Remove-Item Env:\GK2COOP_TEST_MENU_CONNECT
        WaitFor $clientLog 'gameState=MainMenu' 120
        $before = Probe Client 'menu-status'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'menu-before.txt'), $before)
        $connect = Probe Client "menu-connect|127.0.0.1|$hostPort"
        [IO.File]::WriteAllText((Join-Path $OutputPath 'menu-connect.txt'), $connect)
        WaitFor $hostLog 'peers=1' 45
        $after = Probe Client 'menu-status'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'menu-after.txt'), $after)
        Check ($before -match 'gameState=MainMenu' -and $connect -match 'started=True' -and $after -match 'connected=True') 'A client on the main menu can connect to the host world'
        Probe Client 'menu-disconnect' | Out-Null
        WaitFor $hostLog 'has Disconnected' 30
        Start-Sleep -Seconds 3
        $disconnected = Probe Client 'menu-status'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'menu-disconnected.txt'), $disconnected)
        Check ($disconnected -match 'connected=False' -and $disconnected -match 'gameState=MainMenu') 'Client can disconnect while staying on the main menu'
        Probe Client "menu-continue|$slot" | Out-Null
        WaitFor $clientLog ("Invoked the game's Continue button for isolated slot $slot") 40
        $deadline = (Get-Date).AddSeconds(120)
        while ((Get-Date) -lt $deadline) {
            if (@(Select-String -LiteralPath $hostLog -Pattern 'joined as').Count -ge 2) { break }
            if ($peers[1].Process.HasExited) { throw 'Client exited during the second connection.' }
            Start-Sleep -Seconds 3
        }
        $secondJoin = @(Select-String -LiteralPath $hostLog -Pattern 'joined as').Count -ge 2
        Check $secondJoin 'Client can Continue the copied save and reconnect after menu-stage transfer'
        WaitFor $clientLog 'Attached client networking to the normally initialized local game world.' 60
        $hostProgress = Probe Host 'progress'
        $clientProgress = Probe Client 'progress'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'host-progress.txt'), $hostProgress)
        [IO.File]::WriteAllText((Join-Path $OutputPath 'client-progress.txt'), $clientProgress)
        Check ($hostProgress -match 'WORLD scenes=6 objects=10\d\d' -and $clientProgress -match 'WORLD scenes=6 objects=10\d\d' -and
            $hostProgress -match 'QUESTS Completed=115' -and $clientProgress -match 'QUESTS Completed=115') 'Both peers retain the progressed world and quests after menu-stage connection'
        return
    }
    WaitFor $hostLog 'peers=1' 180
    $clientLog = Join-Path $ClientPath 'BepInEx\LogOutput.log'
    WaitFor $clientLog 'Attached client networking to the normally initialized local game world.' 120

    $hostProgress = Probe Host 'progress'
    $clientProgress = Probe Client 'progress'
    [IO.File]::WriteAllText((Join-Path $OutputPath 'host-progress.txt'), $hostProgress)
    [IO.File]::WriteAllText((Join-Path $OutputPath 'client-progress.txt'), $clientProgress)
    if ($FreshClient) {
        $hostBaseline = Probe Host 'baseline'
        $clientBaseline = Probe Client 'baseline'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'host-baseline.txt'), $hostBaseline)
        [IO.File]::WriteAllText((Join-Path $OutputPath 'client-baseline.txt'), $clientBaseline)
        $hostWorld = ($hostProgress -split "`n" | Where-Object { $_ -like 'WORLD *' }) -join ''
        $clientWorld = ($clientProgress -split "`n" | Where-Object { $_ -like 'WORLD *' }) -join ''
        $hostCompleted = ($hostProgress -split "`n" | Where-Object { $_ -like 'QUESTS Completed=*' }) -join ''
        $clientCompleted = ($clientProgress -split "`n" | Where-Object { $_ -like 'QUESTS Completed=*' }) -join ''
        $gap = "Host $hostWorld / $hostCompleted; fresh client $clientWorld / $clientCompleted"
        [IO.File]::WriteAllText((Join-Path $OutputPath 'baseline-gap.txt'), $gap)
        Write-Host "BASELINE COMPARISON: $gap"
        $hostWgos = @($hostBaseline -split "`n" | Where-Object { $_ -like 'WGO *' })
        $clientWgos = @($clientBaseline -split "`n" | Where-Object { $_ -like 'WGO *' })
        $hostQuests = @($hostBaseline -split "`n" | Where-Object { $_ -like 'QUEST *' })
        $clientQuests = @($clientBaseline -split "`n" | Where-Object { $_ -like 'QUEST *' })
        $details = @(
            "World objects only on host: $(@(Compare-Object $clientWgos $hostWgos | Where-Object SideIndicator -eq '=>').Count)",
            "World objects only on client: $(@(Compare-Object $hostWgos $clientWgos | Where-Object SideIndicator -eq '=>').Count)",
            "Differing quest rows: $(@(Compare-Object $hostQuests $clientQuests).Count)"
        )
        [IO.File]::WriteAllLines((Join-Path $OutputPath 'baseline-details.txt'), $details)
        $details | ForEach-Object { Write-Host $_ }
        return
    }
    if ($Transport -eq 'Steam') {
        $wires = "host $(((Probe Host 'transport') -split "`n" | Where-Object { $_ -like 'TRANSPORT *' }) -join '') / joiner $(((Probe Client 'transport') -split "`n" | Where-Object { $_ -like 'TRANSPORT *' }) -join '')"
        Check ($wires -match 'host TRANSPORT Steam .* joiner TRANSPORT Steam') "Both players run the session on Steam's networking ($wires)"
    }
    Check ($hostProgress -match 'WORLD scenes=6 objects=10\d\d' -and $clientProgress -match 'WORLD scenes=6 objects=10\d\d') 'Both players loaded the progressed world'
    Check ($hostProgress -match 'QUESTS Completed=115' -and $clientProgress -match 'QUESTS Completed=115') 'Both players retained the progressed quest state'

    $before = Probe Client 'inspect'
    if ($before -notmatch 'CONTAINER ([0-9a-f-]+) chest_home size=20 items=([^\r\n]*)') { throw 'No chest_home in the progressed save.' }
    $chestId = $Matches[1]
    $beforeItems = $Matches[2]
    $beforeBerries = if ($beforeItems -match 'berryx(\d+)') { [int]$Matches[1] } else { 0 }
    Probe Host "container-add|$chestId|berry|2" | Out-Null
    Start-Sleep -Seconds 2
    $afterHost = Probe Host 'inspect'
    $afterClient = Probe Client 'inspect'
    [IO.File]::WriteAllText((Join-Path $OutputPath 'after-host.txt'), $afterHost)
    [IO.File]::WriteAllText((Join-Path $OutputPath 'after-client.txt'), $afterClient)
    $hostChest = ($afterHost -split "`n" | Where-Object { $_ -match "^CONTAINER $chestId " }) -join ''
    $clientChest = ($afterClient -split "`n" | Where-Object { $_ -match "^CONTAINER $chestId " }) -join ''
    $afterBerries = if ($hostChest -match 'berryx(\d+)') { [int]$Matches[1] } else { 0 }
    Check (($hostChest -eq $clientChest) -and ($afterBerries -eq $beforeBerries + 2)) "Progressed-world chest edit reaches both peers (was $beforeItems)"
    if ($PauseExperiment) {
        $pauseLines = New-Object System.Collections.Generic.List[string]
        foreach ($side in 'Host','Client') {
            $beforePause = (((Probe $side 'pause-state') -split "`n") | Where-Object { $_ -like 'PAUSE-STATE *' }) -join ''
            $afterPause = (((Probe $side 'pause-call') -split "`n") | Where-Object { $_ -like 'PAUSE-STATE *' }) -join ''
            $pauseLines.Add("$side before $beforePause")
            $pauseLines.Add("$side after $afterPause")
            Check ($beforePause -match 'paused=False updating=True' -and $afterPause -match 'paused=False updating=True' -and $afterPause -match 'suppressed=.*[1-9]') "$side modal pause leaves the shared simulation active ($afterPause)"
        }
        [IO.File]::WriteAllText((Join-Path $OutputPath 'pause.txt'), ($pauseLines -join "`n"))
    }
    if ($ConveyorExperiment) {
        $sourceConveyor = '5db2e35c-db50-42ec-84aa-184eb5b013d0'
        # Wood when the source takes it; since game 1.007 it may not, then the first item it takes.
        $added = (((Probe $ConveyorSourcePeer "container-add|$sourceConveyor|auto|1") -split "`n") | Where-Object { $_ -like 'ADD *' }) -join ''
        $conveyorItem = if ($added -match ' item=(\S+) ') { $Matches[1] } else { 'none' }
        $itemPattern = 'items=' + [regex]::Escape($conveyorItem) + 'x'
        Check ($added -match '^ADD True' -and $added -match ($itemPattern + '1')) "$ConveyorSourcePeer puts one item ($conveyorItem) into the powered conveyor source ($added)"
        $trace = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt 8; $i++) {
            Start-Sleep -Seconds 5
            foreach ($side in 'Host','Client') {
                $state = (((Probe $side 'conveyors') -split "`n") | Where-Object { $_ -like 'CONVEYOR *' -and $_ -match ($itemPattern + '[1-9]') }) -join ' | '
                $trace.Add("t+$(5*($i+1))s $side $state")
            }
        }
        foreach ($side in 'Host','Client') {
            $workbench = 'd8743c4d-5833-49a1-ba00-208e008feb8a'
            $stock = (((Probe $side "station-stock|$workbench") -split "`n") | Where-Object { $_ -like 'STATION-STOCK *' }) -join ''
            $trace.Add("workbench $side $stock")
            $status = (((Probe $side 'stations') -split "`n") | Where-Object { $_ -like "STATION $workbench *" }) -join ''
            $trace.Add("status $side $status")
        }
        [IO.File]::WriteAllText((Join-Path $OutputPath 'conveyor-trace.txt'), ($trace -join "`n"))
        $lastHost = $trace | Where-Object { $_ -like 't+40s Host *' } | Select-Object -Last 1
        $lastClient = $trace | Where-Object { $_ -like 't+40s Client *' } | Select-Object -Last 1
        $hostItem = if ($lastHost -match ('CONVEYOR ([0-9a-f-]{36}) .*' + $itemPattern + '1')) { $Matches[1] } else { '' }
        $clientItem = if ($lastClient -match ('CONVEYOR ([0-9a-f-]{36}) .*' + $itemPattern + '1')) { $Matches[1] } else { '' }
        Check ($hostItem -and $hostItem -eq $clientItem -and $hostItem -ne $sourceConveyor -and
            $lastHost -notmatch '\| CONVEYOR ' -and $lastClient -notmatch '\| CONVEYOR ') "One $conveyorItem item remains in the same downstream conveyor cell on both peers (host $hostItem; joiner $clientItem)"
    }
    if ($Explore) {
        foreach ($command in $Explore) {
            foreach ($side in 'Host','Client') {
                $answer = try { & $invoke -Peer $side -Command $command -TimeoutSeconds 30 } catch { "ERROR $_" }
                Add-Content -LiteralPath (Join-Path $OutputPath 'explore.txt') -Value "### $side :: $command`n$answer"
            }
        }
    }
    if ($AuditExperiment) { Invoke-Audit }
    if ($SteamSpikeExperiment) {
        function Say([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $log = New-Object System.Collections.Generic.List[string]
        $hs = Say Host 'steam-status' 'STEAM-STATUS'; $cs = Say Client 'steam-status' 'STEAM-STATUS'
        $log.Add("status host:   $hs"); $log.Add("status joiner: $cs")
        Check ($hs -match 'id=7656' -and $cs -match 'id=7656') "Steam is initialised in both test copies ($hs; $cs)"
        $l = Say Host 'steam-listen|27031' 'STEAM-LISTEN'; $log.Add($l)
        $c = Say Client 'steam-connect|27031' 'STEAM-CONNECT'; $log.Add($c)
        Start-Sleep -Seconds 4
        $hp = Say Host 'steam-poll' 'STEAM-POLL'; $cp = Say Client 'steam-poll' 'STEAM-POLL'
        $log.Add("host:   $hp"); $log.Add("joiner: $cp")
        Check ($hp -match 'state=k_ESteamNetworkingConnectionState_Connected' -and $cp -match 'state=k_ESteamNetworkingConnectionState_Connected') "The two copies connect through SteamNetworkingSockets ($hp; $cp)"
        $log.Add((Say Client 'steam-send|hello-from-joiner' 'STEAM-SEND'))
        $log.Add((Say Host 'steam-send|hello-from-host' 'STEAM-SEND'))
        Start-Sleep -Seconds 2
        $hp = Say Host 'steam-poll' 'STEAM-POLL'; $cp = Say Client 'steam-poll' 'STEAM-POLL'
        $log.Add("host:   $hp"); $log.Add("joiner: $cp")
        [IO.File]::WriteAllText((Join-Path $OutputPath 'steam-spike.txt'), ($log -join "`n"))
        Check ($hp -match '\[hello-from-joiner\]' -and $cp -match '\[hello-from-host\]') "Messages travel both ways ($hp; $cp)"
        Say Client 'steam-close' 'STEAM-CLOSE' | Out-Null; Say Host 'steam-close' 'STEAM-CLOSE' | Out-Null
    }
    if ($FightExperiment) {
        function Levels([string]$Peer) { ((Probe $Peer 'fight-levels') -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like 'FIGHT-LEVEL *' }) }
        $hostLevels = @(Levels Host)
        [IO.File]::WriteAllText((Join-Path $OutputPath 'fight-levels.txt'), ($hostLevels -join "`n"))
        Check ($hostLevels.Count -ge 2 -and ($hostLevels -join "`n") -eq (@(Levels Client) -join "`n")) "Both players start with the same fighting levels ($($hostLevels.Count))"
        $a = ($hostLevels[0] -split ' ')[1]; $b = ($hostLevels[1] -split ' ')[1]
        Probe Client "fight-stage|$a|3" | Out-Null
        Start-Sleep -Seconds 3
        Check (((Levels Host) | Where-Object { $_ -like "FIGHT-LEVEL $a *" }) -match 'stage=3 ') "A fighting level advanced on the joiner is advanced on the host ($a)"
        Probe Host "fight-stage|$b|2" | Out-Null
        Start-Sleep -Seconds 3
        Check (((Levels Client) | Where-Object { $_ -like "FIGHT-LEVEL $b *" }) -match 'stage=2 ') "A fighting level advanced on the host is advanced on the joiner ($b)"
        Check (((Levels Host) -join "`n") -eq ((Levels Client) -join "`n")) 'Both players end with the same fighting levels'
    }
    if ($WeatherExperiment) {
        function Weather([string]$Peer) { $w = (((Probe $Peer 'weather') -split "`n") | Where-Object { $_ -like 'WEATHER *' }) -join ''; if ($w -match 'state=(\S+)') { $Matches[1] } else { '?' } }
        $log = New-Object System.Collections.Generic.List[string]
        $start = Weather Host
        $log.Add("start host=$start joiner=$(Weather Client)")
        Check ($start -ne '?' -and (Weather Client) -eq $start) "The joiner starts with the host's weather ($start)"
        $first = if ($start -eq 'RainDayThunder') { 'CleanWeather' } else { 'RainDayThunder' }
        foreach ($target in @($first, $(if ($first -eq 'CleanWeather') { 'RainDayThunder' } else { 'CleanWeather' }))) {
            Probe Host "weather-set|$target" | Out-Null
            Start-Sleep -Seconds 4
            $seen = Weather Client
            $log.Add("host set $target -> joiner=$seen")
            Check ($seen -eq $target) "Weather the host changes to $target changes for the joiner ($seen)"
        }
        $before = Weather Client
        for ($i = 0; $i -lt 3; $i++) { Probe Client 'weather-roll' | Out-Null; Start-Sleep -Milliseconds 500 }
        Start-Sleep -Seconds 3
        $afterRoll = Weather Client
        $log.Add("joiner rolled 3x: $before -> $afterRoll; host=$(Weather Host)")
        Check ($afterRoll -eq $before -and $afterRoll -eq (Weather Host)) "The joiner does not roll weather of its own ($before -> $afterRoll)"
        $rolled = @()
        for ($i = 0; $i -lt 6; $i++) {
            Probe Host 'weather-roll' | Out-Null
            Start-Sleep -Seconds 3
            $h = Weather Host; $c = Weather Client
            $rolled += "$h/$c"
            if ($h -ne $c) { break }
        }
        $log.Add("host rolls (host/joiner): $($rolled -join ', ')")
        Check (@($rolled | Where-Object { ($_ -split '/')[0] -ne ($_ -split '/')[1] }).Count -eq 0) "Weather the host rolls reaches the joiner ($($rolled -join ', '))"
        $log.Add((Probe Client 'weather'))
        [IO.File]::WriteAllText((Join-Path $OutputPath 'weather.txt'), ($log -join "`n"))
    }
    if ($GemsExperiment) {
        function Gems([string]$Peer) {
            $g = (((Probe $Peer 'gems') -split "`n") | Where-Object { $_ -like 'GEMS *' }) -join ''
            if ($g -match 'action gems: (\S+) \(red ([-\d.E]+), green ([-\d.E]+), blue ([-\d.E]+)\)') {
                [pscustomobject]@{ Mode = $Matches[1]; Red = [double]::Parse($Matches[2], [cultureinfo]::InvariantCulture); Green = [double]::Parse($Matches[3], [cultureinfo]::InvariantCulture); Blue = [double]::Parse($Matches[4], [cultureinfo]::InvariantCulture); Text = $g }
            } else { [pscustomobject]@{ Mode = '?'; Red = -1; Green = -1; Blue = -1; Text = $g } }
        }
        $log = New-Object System.Collections.Generic.List[string]
        $h0 = Gems Host; $c0 = Gems Client
        $log.Add("start host: $($h0.Text)"); $log.Add("start joiner: $($c0.Text)")
        Check ($h0.Mode -eq 'shared' -and $c0.Mode -eq 'shared') "Both players play with one pool of action gems ($($h0.Mode), $($c0.Mode))"
        Check ($h0.Red -eq $c0.Red -and $h0.Green -eq $c0.Green -and $h0.Blue -eq $c0.Blue) "Both players see the same gems at the start (red $($h0.Red), green $($h0.Green), blue $($h0.Blue))"
        # Both earn at the same moment: each gain must count.
        Probe Host 'addres|tech_red|5' | Out-Null
        Probe Client 'addres|tech_red|7' | Out-Null
        Probe Client 'addres|tech_green|3' | Out-Null
        Start-Sleep -Seconds 5
        $h1 = Gems Host; $c1 = Gems Client
        $log.Add("after +5 red host, +7 red +3 green joiner: host $($h1.Text); joiner $($c1.Text)")
        Check ($h1.Red -eq $h0.Red + 12 -and $c1.Red -eq $h0.Red + 12) "Red gems earned by both players at once add up for both (host $($h1.Red), joiner $($c1.Red), expected $($h0.Red + 12))"
        Check ($h1.Green -eq $h0.Green + 3 -and $c1.Green -eq $h0.Green + 3) "Green gems a joiner earns reach the host ($($h1.Green), $($c1.Green))"
        # Spending comes out of the same pool.
        Probe Client 'addres|tech_red|-4' | Out-Null
        Start-Sleep -Seconds 4
        Probe Host 'addres|tech_blue|2' | Out-Null
        Start-Sleep -Seconds 4
        $h2 = Gems Host; $c2 = Gems Client
        $log.Add("after joiner spends 4 red, host +2 blue: host $($h2.Text); joiner $($c2.Text)")
        Check ($h2.Red -eq $h1.Red - 4 -and $c2.Red -eq $h1.Red - 4) "Gems a joiner spends leave the pool for both (host $($h2.Red), joiner $($c2.Red))"
        Check ($h2.Blue -eq $h0.Blue + 2 -and $c2.Blue -eq $h0.Blue + 2) "Blue gems the host earns reach the joiner ($($h2.Blue), $($c2.Blue))"
        [IO.File]::WriteAllText((Join-Path $OutputPath 'gems.txt'), ($log -join "`n"))
    }
    if ($CutsceneSurvey) {
        # Research for opt-in cutscenes: which scripts exist, which run in a normal session, and
        # what happens on each machine when one runs on the host (see CoopCutsceneTrace).
        $names = Probe Host 'script-names'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'script-names.txt'), $names)
        Check ($names -match 'total=[1-9]') "The game's scripts are listed ($(([regex]::Match($names, 'total=\d+')).Value))"
        foreach ($side in 'Host','Client') { [IO.File]::WriteAllText((Join-Path $OutputPath "trace-$($side.ToLower())-at-load.txt"), (Probe $side 'cutscene-trace')) }
        [IO.File]::WriteAllText((Join-Path $OutputPath 'script-catalog.txt'), (Probe Host 'script-catalog'))
        foreach ($script in @($CutsceneScripts -split ',' | Where-Object { $_ })) {
            $before = @{}
            foreach ($side in 'Host','Client') { $before[$side] = (((Probe $side 'cutscene-trace') -split "`n") | Select-Object -First 1) }
            # "Script:event" fires one of the script's events, as the game starts a story scene.
            if ($script -match '^([^:]+):(.+)$') { Probe Host "fire-script-event|$($Matches[1])|$($Matches[2])" | Out-Null; $script = $Matches[1] } else { Probe Host "run-script|$script" | Out-Null }
            if ($CutsceneOnBoth -and $script -ne $null) {
                # The same scene replayed on the joiner a moment later: does it show there too?
                Start-Sleep -Seconds 2
                $item = @($CutsceneScripts -split ',' | Where-Object { $_ -like "$script*" })[0]
                if ($item -match '^([^:]+):(.+)$') { Probe Client "fire-script-event|$($Matches[1])|$($Matches[2])" | Out-Null } else { Probe Client "run-script|$script" | Out-Null }
            }
            Start-Sleep -Seconds 12
            foreach ($side in 'Host','Client') {
                Probe $side "shot|$(Join-Path $OutputPath "cut-$script-$($side.ToLower()).png")" | Out-Null
                [IO.File]::WriteAllText((Join-Path $OutputPath "trace-$($side.ToLower())-after-$script.txt"), (Probe $side 'cutscene-trace'))
            }
            Start-Sleep -Seconds 20
            foreach ($side in 'Host','Client') { [IO.File]::WriteAllText((Join-Path $OutputPath "trace-$($side.ToLower())-later-$script.txt"), (Probe $side 'cutscene-trace')) }
        }
    }
    foreach ($graphName in @($ScriptGraphs -split ',' | Where-Object { $_ })) {
        # A scene's flowchart, for choosing and understanding test scenes.
        [IO.File]::WriteAllText((Join-Path $OutputPath "graph-$graphName.txt"), (Probe Host "script-graph|$graphName"))
    }
    if ($SceneStopExperiment) {
        # A scene that waits for the host (Event_113 stops after its camera flight): the joiner
        # stops watching and is back in control; then a second one ends because the joiner's
        # connection goes.
        function Share([string]$Side) { ((((Probe $Side 'scene-share') -split "`n") | Where-Object { $_ -like 'SCENE-SHARE *' }) -join '') }
        $stopLog = New-Object System.Collections.Generic.List[string]
        $before = (((Probe Client 'where') -split "`n" | Where-Object { $_ -like 'WHERE *' }) -join '')
        Probe Host 'fire-script-event|Event_113_Sewers_God|113_sewers_god_1' | Out-Null
        Start-Sleep -Seconds 3
        $stopLog.Add((Probe Client 'scene-answer|watch'))
        Start-Sleep -Seconds 12
        $s1 = Share Client
        $stopLog.Add("watching: $s1")
        Check ($s1 -match 'replaying' -and $s1 -match 'watching=\[') "While watching, a way out is on screen ($s1)"
        Probe Client "shot|$(Join-Path $OutputPath 'stop-1-watching.png')" | Out-Null
        # Another scene starting meanwhile is not offered to a player who is watching one.
        Probe Host 'fire-script-event|Event_127_Palace_Battle|127_palace_battle_1' | Out-Null
        Start-Sleep -Seconds 3
        $busy = Share Client
        $stopLog.Add("second scene while watching: $busy")
        Check ($busy -notmatch 'offer from' -and $busy -match 'replaying') "A player watching a scene is not asked about another one ($busy)"
        # Stopping with a controller: B held (a short press does not stop).
        $stopLog.Add((Probe Client 'pad|Xbox_XboxController'))
        Start-Sleep -Milliseconds 500
        $stopLog.Add((Probe Client 'pad-button|B'))
        Start-Sleep -Seconds 1
        $short = Share Client
        Check ($short -match 'replaying') "A short B does not stop watching ($short)"
        $stopLog.Add((Probe Client 'pad-hold|B|1.2'))
        Start-Sleep -Seconds 6
        Probe Client 'pad|off' | Out-Null
        $s2 = Share Client
        $after = (((Probe Client 'where') -split "`n" | Where-Object { $_ -like 'WHERE *' }) -join '')
        $trace = Probe Client 'cutscene-trace'
        $bubbles = Probe Client 'bubbles'
        $stopLog.Add("stopped: $s2"); $stopLog.Add("before: $before"); $stopLog.Add("after: $after"); $stopLog.Add($bubbles)
        Check ($s2 -match 'stopped=1' -and $s2 -match 'returned=1' -and $s2 -notmatch 'replaying' -and $s2 -match 'watching=hidden') "Stopping ends the scene here and takes the joiner back ($s2)"
        $b = $before -split ' '; $c = $after -split ' '
        $moved = [Math]::Sqrt([Math]::Pow([double]$b[1] - [double]$c[1], 2) + [Math]::Pow([double]$b[2] - [double]$c[2], 2))
        Check ($moved -lt 3 -and $b[3] -eq $c[3]) "The joiner is back where they were ($before -> $after)"
        Check ($trace -match 'control returned' -and $trace -match 'cinematic off' -and $bubbles -match 'active=0') "Control, screen and dialogue are clear after stopping"
        Probe Client "shot|$(Join-Path $OutputPath 'stop-2-back.png')" | Out-Null
        $hostShare = Share Host
        Check ($hostShare -match 'announced=2' -and $hostShare -match 'stopped=0') "The host's own scenes are not touched by the joiner stopping ($hostShare)"
        # The connection goes while watching: the replay ends with it.
        Probe Host 'fire-script-event|Event_113_Sewers_God|113_sewers_god_1' | Out-Null
        Start-Sleep -Seconds 3
        $stopLog.Add((Probe Client 'scene-answer|watch'))
        Start-Sleep -Seconds 10
        $s3 = Share Client
        $stopLog.Add("second: $s3")
        Probe Client 'menu-disconnect' | Out-Null
        Start-Sleep -Seconds 8
        $stopped = @(Select-String -Path (Join-Path $ClientPath 'BepInEx\LogOutput.log') -Pattern "stopped watching .*they left" -ErrorAction SilentlyContinue).Count
        $errors = @(Select-String -Path (Join-Path $ClientPath 'BepInEx\LogOutput.log') -Pattern "Scene share: could not" -ErrorAction SilentlyContinue | ForEach-Object { $_.Line })
        $stopLog.Add("after disconnect: stopped lines $stopped; " + ($errors -join ' | '))
        Check ($s3 -match 'replaying' -and $stopped -eq 1 -and $errors.Count -eq 0) "Losing the connection ends the scene being watched, once and cleanly ($stopped)"
        [IO.File]::WriteAllText((Join-Path $OutputPath 'scene-stop.txt'), ($stopLog -join "`n"))
    }
    if ($BindingsSurvey) {
        [IO.File]::WriteAllText((Join-Path $OutputPath 'bindings.txt'), (Probe Client 'bindings'))
        [IO.File]::WriteAllText((Join-Path $OutputPath 'pad-bindings.txt'), (Probe Client 'pad-bindings'))
        Check (Test-Path (Join-Path $OutputPath 'bindings.txt')) 'Bindings written'
    }
    if ($SoakMinutes -gt 0) {
        # A long session: both players keep using the shared chest, moving and chatting; memory,
        # frame time and object counts are sampled, and the worlds compared line by line.
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        function Audit([string]$Peer) { ((Probe $Peer 'audit') -split "`n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -like 'AUDIT-*' }) }
        function SoakDiff([string]$Label) {
            $h = @(Audit Host); $c = @(Audit Client)
            if ($h.Count -eq 0 -or $c.Count -eq 0) {
                [IO.File]::WriteAllText((Join-Path $OutputPath "soak-audit-$Label-raw.txt"), (Probe Host 'audit'))
                return -1
            }
            $d = @(Compare-Object $h $c | ForEach-Object { $(if ($_.SideIndicator -eq '<=') { 'host   ' } else { 'joiner ' }) + $_.InputObject })
            [IO.File]::WriteAllText((Join-Path $OutputPath "soak-audit-$Label-diff.txt"), ($d -join "`n"))
            return $d.Count
        }
        $soak = New-Object System.Collections.Generic.List[string]
        $samples = New-Object System.Collections.Generic.List[object]
        function Sample([string]$Label) {
            foreach ($side in 'Host','Client') {
                $m = Line $side 'mem' 'MEM'
                $soak.Add("$Label $side $m")
                if ($m -match 'heapMB=(\d+) workingMB=(\d+) frameMs=([\d.]+) objects=(\d+)') {
                    $heap = [int]$Matches[1]; $working = [int]$Matches[2]; $objects = [int]$Matches[4]
                    # One frame time is a single smoothed moment (a garbage collection, an audit just
                    # run): the median of five, a second apart, is the session's pace.
                    $frames = @([double]$Matches[3])
                    # Eleven readings half a second apart: one swings from 10 to 100 ms on a shared PC.
                    for ($i = 0; $i -lt 10; $i++) {
                        Start-Sleep -Milliseconds 500
                        if ((Line $side 'mem' 'MEM') -match 'frameMs=([\d.]+)') { $frames += [double]$Matches[1] }
                    }
                    $frame = ($frames | Sort-Object)[[int][Math]::Floor($frames.Count / 2)]
                    $soak.Add("$Label $side frame median $frame of $($frames -join ',')")
                    $samples.Add([pscustomobject]@{ Label = $Label; Side = $side; Heap = $heap; Working = $working; Frame = $frame; Objects = $objects })
                }
            }
        }
        Start-Sleep -Seconds 45
        $chest = if ((Probe Client 'inspect') -match 'CONTAINER ([0-9a-f-]+) chest_home') { $Matches[1] } else { throw 'No chest_home for the soak.' }
        $berriesAt = { param($text) if ($text -match 'chest_home size=\d+ items=[^\r\n]*?berryx(\d+)') { [int]$Matches[1] } else { 0 } }
        $b0 = & $berriesAt (Probe Host 'inspect')
        Sample 'start'
        $d = SoakDiff 'start'; $soak.Add("audit start: $d differing lines")
        $end = (Get-Date).AddMinutes($SoakMinutes)
        $cycle = 0; $nextCheck = (Get-Date).AddMinutes(5); $audits = @($d)
        while ((Get-Date) -lt $end) {
            $cycle++
            Probe Host "container-add|$chest|berry|1" | Out-Null
            Start-Sleep -Seconds 3
            Probe Client "container-remove|$chest|berry|1" | Out-Null
            $step = if ($cycle % 2) { 2 } else { -2 }
            Probe Host "move|$step|0" | Out-Null
            Probe Client "move|0|$step" | Out-Null
            if ($cycle % 6 -eq 0) { Probe Client "chat-send|soak $cycle" | Out-Null }
            Start-Sleep -Seconds 12
            if ((Get-Date) -ge $nextCheck) {
                $nextCheck = (Get-Date).AddMinutes(5)
                $label = "t$([int]((Get-Date) - $end.AddMinutes(-$SoakMinutes)).TotalMinutes)m"
                Sample $label
                $d = SoakDiff $label; $audits += $d
                $soak.Add("audit ${label}: $d differing lines, cycle $cycle")
                [IO.File]::WriteAllText((Join-Path $OutputPath 'soak.txt'), ($soak -join "`n"))
            }
        }
        Start-Sleep -Seconds 5
        Sample 'end'
        $d = SoakDiff 'end'; $audits += $d
        $soak.Add("audit end: $d differing lines, $cycle cycles")
        $bh = & $berriesAt (Probe Host 'inspect'); $bc = & $berriesAt (Probe Client 'inspect')
        $soak.Add("berries: start $b0, end host $bh / joiner $bc")
        [IO.File]::WriteAllText((Join-Path $OutputPath 'soak.txt'), ($soak -join "`n"))
        Check ($d -eq 0) "After $SoakMinutes minutes and $cycle rounds of play the two worlds are identical (audits: $($audits -join ', '))"
        Check ($bh -eq $b0 -and $bc -eq $b0) "Every berry added and taken again is accounted for on both sides ($b0 -> $bh / $bc)"
        foreach ($side in 'Host','Client') {
            $first = $samples | Where-Object { $_.Side -eq $side } | Select-Object -First 1
            $last = $samples | Where-Object { $_.Side -eq $side } | Select-Object -Last 1
            $soak.Add("$side growth: heap $($first.Heap) -> $($last.Heap) MB, working $($first.Working) -> $($last.Working) MB, objects $($first.Objects) -> $($last.Objects), frame $($first.Frame) -> $($last.Frame) ms")
            Check ($last.Objects -le $first.Objects * 1.05 + 50) "$side keeps its object count over the session ($($first.Objects) -> $($last.Objects))"
            Check ($last.Heap -le [Math]::Max($first.Heap * 1.5, $first.Heap + 200)) "$side keeps its managed memory over the session ($($first.Heap) -> $($last.Heap) MB)"
            Check ($last.Working -le [Math]::Max($first.Working * 1.3, $first.Working + 300)) "$side keeps Unity's memory over the session ($($first.Working) -> $($last.Working) MB)"
            # The probe holds the test games at 20 frames a second (50 ms) and several share this PC: a slowdown
            # that matters is past 100 ms (under 10 frames a second) and half again the start.
            Check ($last.Frame -le [Math]::Max($first.Frame * 1.5, 100)) "$side keeps its frame time over the session ($($first.Frame) -> $($last.Frame) ms)"
        }
        [IO.File]::WriteAllText((Join-Path $OutputPath 'soak.txt'), ($soak -join "`n"))
    }
    if ($LateWatchExperiment) {
        # Decline a scene, then watch it later from the pause menu: it plays from its start, ends,
        # and the joiner comes back.
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        function Share([string]$Side) { Line $Side 'scene-share' 'SCENE-SHARE' }
        $late = New-Object System.Collections.Generic.List[string]
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null }
        if (-not $LateWithAnswer) {
            $late.Add((Probe Host 'teleport-wgo|npc_jeffry'))
            Start-Sleep -Seconds 10
        }
        $before = Line Client 'where' 'WHERE'
        if ($LateWithAnswer) { Probe Host 'fire-script-event|Event_116_Base_Attic|116_base_attic_1' | Out-Null }
        else { Probe Host 'fire-script-event|Event_124_Village_Money|124_village_money_chest_1' | Out-Null }
        Start-Sleep -Seconds 3
        Probe Client 'scene-answer|decline' | Out-Null
        Start-Sleep -Seconds 2
        if ($LateWithAnswer) {
            # The host answers before the joiner changes their mind.
            $answers = 'ANSWERS none'
            for ($i = 0; $i -lt 120 -and $answers -match 'ANSWERS none'; $i++) { Start-Sleep -Seconds 1; $answers = Line Host 'answers' 'ANSWERS' }
            $late.Add("host answers: $answers")
            # The game ignores a choice while its answer bubbles are still opening.
            Start-Sleep -Seconds 1
            Probe Host 'answer-pick|0' | Out-Null
            Start-Sleep -Seconds 2
        }
        $p = Line Client 'pause-coop' 'PAUSE-COOP'
        $s = Share Client
        $late.Add("after decline: $s"); $late.Add("pause: $p")
        Check ($s -match 'running elsewhere=1' -and $s -match 'replays=0') "After declining, the scene is known to be running for the host ($s)"
        Probe Client 'pause-coop|open' | Out-Null
        Start-Sleep -Seconds 1
        Probe Client 'pause-coop|click|Koop' | Out-Null
        Start-Sleep -Seconds 1
        $p = Line Client 'pause-coop' 'PAUSE-COOP'
        $late.Add("co-op window: $p")
        Check ($p -match '<Szene von Host ansehen>|<Watch Host''s scene>') "Pause > Co-op offers to watch the host's scene ($p)"
        Probe Client 'pause-coop|choose|Szene von Host ansehen' | Out-Null
        Start-Sleep -Seconds 8
        $s = Share Client
        $late.Add("watching late: $s")
        Check ($s -match 'late joins=1' -and $s -match 'replaying') "Choosing it plays the scene here from its start ($s)"
        for ($i = 0; $i -lt 120 -and (Share Client) -match 'replaying'; $i++) { Start-Sleep -Seconds 2 }
        Start-Sleep -Seconds 4
        $s = Share Client
        $after = Line Client 'where' 'WHERE'
        $late.Add("after: $s"); $late.Add("where: $before -> $after")
        Check ($s -notmatch 'replaying' -and $s -match 'returned=1') "The late-watched scene ends and the joiner comes back ($s)"
        if ($LateWithAnswer) { Check ($s -match 'answers followed=1' -and $s -match 'finished=1' -and $s -match 'stopped=0') "The late viewer follows the answer the host gave before they started watching ($s)" }
        $b = $before -split ' '; $c = $after -split ' '
        $moved = [Math]::Sqrt([Math]::Pow([double]$b[1] - [double]$c[1], 2) + [Math]::Pow([double]$b[2] - [double]$c[2], 2))
        Check ($moved -lt 3 -and $b[3] -eq $c[3]) "... to where they were ($before -> $after)"
        [IO.File]::WriteAllText((Join-Path $OutputPath 'late-watch.txt'), ($late -join "`n"))
    }
    if ($FightStuckExperiment) {
        # A fighting level is an island the game teleports the fighter onto and back from. A player
        # on it without a fight of their own (here placed there; in play a joiner who fought and
        # dropped out) is brought back to the level's return point. From there: what does a watcher
        # see of a fight on the island?
        function Lines([string]$Peer, [string]$Command, [string]$Prefix) { ((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" } }
        $st = New-Object System.Collections.Generic.List[string]
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null }
        $st.Add("joiner placed: " + ((Lines Client 'body-to|111|16|3.6' 'BODY-TO') -join ''))
        Start-Sleep -Seconds 7
        $arena = (Lines Client 'arena' 'ARENA') -join ''
        $where = ((Lines Client 'players' 'PLAYER') | Where-Object { $_ -match 'client' }) -join ''
        $st.Add("after: $arena; $where")
        $x = if ($where -match 'client pos=\(([-\d.]+),') { [double]$Matches[1] } else { 111 }
        Check ($arena -match 'rescued=[1-9]' -and [Math]::Abs($x - 111) -gt 3) "A player on a fight island without a fight of their own is brought back ($arena; $where)"
        Probe Client "shot|$(Join-Path $OutputPath 'rescue-1-back.png')" | Out-Null
        [IO.File]::WriteAllText((Join-Path $OutputPath 'fight-rescue.txt'), ($st -join "`n"))
    }
    if ($ZombieLookExperiment) {
        # Experiment, pictures only: the host's body on the joiner's screen with a zombie's skin, in
        # each keeper animation, to see which have zombie frames.
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join ' ; ' }
        $zl = New-Object System.Collections.Generic.List[string]
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null }
        # The host two steps beside the joiner, both in view on the joiner's screen.
        $me = ((Probe Client 'players') -split "`n") | Where-Object { $_ -match 'PLAYER id=\d+ client pos=\(' } | Select-Object -First 1
        if ($me -match 'pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)') { $x = [double]$Matches[1] - 1.6; $y = $Matches[2]; $z = [double]$Matches[3] - 1.0 } else { throw "No joiner position: $me" }
        $zl.Add("joiner $me")
        $zl.Add((Line Host "body-to|$x|$z|$y" 'BODY-TO'))
        Start-Sleep -Seconds 3
        foreach ($kind in 'zombie_worker','zombie_assistant') {
            $zl.Add((Line Client "zombie-look|remote|$kind" 'ZOMBIE-LOOK'))
            foreach ($pose in 'Idle','Walk','ToolAxe','ToolShovel','ToolPickaxe','ToolHammer','WorkHands','AttackMelee','FishingIdle','Death') {
                $zl.Add((Line Client "body-pose|remote|$pose" 'BODY-POSE'))
                Start-Sleep -Milliseconds 700
                Probe Client "shot|$(Join-Path $OutputPath "zombie-$kind-$pose.png")" | Out-Null
                if ($pose -eq 'Walk') {
                    # Walking moves the body: stand it still and bring it back beside the joiner.
                    Probe Client 'body-pose|remote|Idle' | Out-Null
                    Probe Host "body-to|$([double]$x + 0.01)|$z|$y" | Out-Null
                    Start-Sleep -Seconds 2
                }
            }
            Probe Client 'body-pose|remote|Idle' | Out-Null
            Probe Host "body-to|$([double]$x - 0.01)|$z|$y" | Out-Null
            Start-Sleep -Seconds 2
        }
        $zl.Add((Line Client 'zombie-look|remote|off' 'ZOMBIE-LOOK'))
        Start-Sleep -Seconds 3
        $zl.Add((Line Client 'body-pose|remote|Idle' 'BODY-POSE'))
        Probe Client "shot|$(Join-Path $OutputPath 'zombie-off.png')" | Out-Null
        [IO.File]::WriteAllText((Join-Path $OutputPath 'zombie-look.txt'), ($zl -join "`n"))
        Check $true 'Zombie look pictures taken'
    }
    if ($FightLockExperiment) {
        # One fight at a time: while one player fights, the other cannot choose one (and is told
        # who is fighting), a fight chosen in the same moment is called off, and the clock stops on
        # both machines until the fight is over.
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join ' ; ' }
        function Time([string]$Peer) { if ((Line $Peer 'fight-lock' 'FIGHT-LOCK') -match 'time=([\d.,]+)') { [double]($Matches[1] -replace ',', '.') } else { -1 } }
        $fl = New-Object System.Collections.Generic.List[string]
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null; $fl.Add("$side " + (Line $side 'body-to|111|16|3.6' 'BODY-TO')); $fl.Add("$side before: " + (Line $side 'fight-lock' 'FIGHT-LOCK')) }
        Start-Sleep -Seconds 6
        foreach ($pair in @(@('Host', 'Client', 'Host'), @('Client', 'Host', 'Client'))) {
            $fighter, $other, $name = $pair
            $fl.Add("$fighter " + (Line $fighter 'fight-lock|choose|fight_A1_1' 'FIGHT-LOCK'))
            Start-Sleep -Seconds 3
            Probe $fighter 'close-windows' | Out-Null
            $lock = Line $other 'fight-lock' 'FIGHT-LOCK'
            $t1 = Time $other; Start-Sleep -Seconds 4; $t2 = Time $other
            $fl.Add("$other while $fighter fights: $lock; time $t1 -> $t2")
            Check ($lock -match "holder=$name," -and $lock -match 'clock held=True' -and $lock -match 'paused=True' -and $t1 -ge 0 -and [Math]::Abs($t2 - $t1) -lt 0.0005) "While the $fighter fights, the $other knows it and its clock stands still ($lock; time $t1 -> $t2)"
            $choose = Line $other 'fight-lock|choose|fight_A1_1' 'FIGHT-LOCK'
            $state = Line $other 'fight-state' 'FIGHT-STATE'
            $fl.Add("$other tries: $choose; $state")
            Check ($choose -match "^FIGHT-LOCK refused $name" -and $state -match 'state=Disabled') "The $other cannot start a fight meanwhile and is told who is fighting ($choose)"
            $fl.Add("$other at once: " + (Line $other 'fight-lock|choose-at-once|fight_A1_1' 'FIGHT-LOCK'))
            Start-Sleep -Seconds 4
            Probe $other 'close-windows' | Out-Null
            $state = Line $other 'fight-state' 'FIGHT-STATE'
            $lock = Line $other 'fight-lock' 'FIGHT-LOCK'
            $mine = Line $fighter 'fight-state' 'FIGHT-STATE'
            $fl.Add("$other after starting at once: $state; $lock; $fighter $mine")
            Check ($state -match 'state=Disabled' -and $lock -match 'cancelled=[1-9]' -and $mine -notmatch 'state=Disabled') "A fight the $other started in the same moment is called off; the $fighter's goes on ($state; $mine)"
            Check ($lock -match 'paused=True') "... and the $other's clock stands still again ($lock)"
            $fl.Add("$fighter " + (Line $fighter 'fight-stop|hard' 'FIGHT-STOP'))
            Start-Sleep -Seconds 3
            Probe $fighter 'close-windows' | Out-Null
            $lock = Line $other 'fight-lock' 'FIGHT-LOCK'
            $t1 = Time $other; Start-Sleep -Seconds 4; $t2 = Time $other
            $fl.Add("$other after the fight: $lock; time $t1 -> $t2")
            Check ($lock -match 'holder=nobody' -and $lock -match 'clock held=False' -and $lock -match 'paused=False' -and $t2 -ne $t1) "After the $fighter's fight the $other's clock runs again and fights are free ($lock; time $t1 -> $t2)"
            # The fight was stopped without a result (as when the fighter's game drops out): its level must
            # not stay shut (stage 3 or 4) on either machine.
            $levels = { param($peer) ((Probe $peer 'fight-levels') -split "`n" | Where-Object { $_ -match 'fight_A1_1' }) -join ' ' }
            $lh = & $levels $other; $lf = & $levels $fighter
            $fl.Add("levels after: $other $lh | $fighter $lf")
            Check ($lh -match 'stage=\d' -and $lh -notmatch 'stage=[34]\b' -and $lf -notmatch 'stage=[34]\b') "A fight ended without a result leaves no arena shut on either machine ($other $lh | $fighter $lf)"
            Start-Sleep -Seconds 3
        }
        # Another player's body dying (the fighter losing their last life while the other watches)
        # must not tell the watcher they are dead or take their controls.
        foreach ($watcher in 'Client','Host') {
            Probe $watcher 'close-windows' | Out-Null
            # The test poses the death; under load the pose is sometimes cut short before its end
            # (where the game would open "you're dead"), so it is posed again, up to three times.
            # Any window shown at any try fails.
            for ($try = 1; $try -le 3; $try++) {
                $fl.Add("$watcher try $try " + (Line $watcher 'body-pose|remote|Death' 'BODY-POSE'))
                Start-Sleep -Seconds 5
                $dialog = Line $watcher 'dialog' 'DIALOG'
                if ($dialog -match 'shown=True' -or $dialog -match 'skipped=[1-9]') { break }
                Probe $watcher 'body-pose|remote|Idle' | Out-Null
                Start-Sleep -Seconds 1
            }
            $fl.Add("$watcher after the other's death: $dialog")
            Check ($dialog -notmatch "shown=True" -and $dialog -match 'skipped=[1-9]') "The other player's body dying does not tell the $watcher they are dead ($dialog)"
            Probe $watcher 'body-pose|remote|Idle' | Out-Null
        }
        [IO.File]::WriteAllText((Join-Path $OutputPath 'fight-lock.txt'), ($fl -join "`n"))
    }
    if ($FightExplore) {
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join ' ; ' }
        $fx = New-Object System.Collections.Generic.List[string]
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null }
        $where = Line Host 'fight-where' 'FIGHT-WHERE'
        $fx.Add("levels: $where")
        $m = [regex]::Match($where, [regex]::Escape($FightLevel) + ' at=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)')
        if ($m.Success) {
            foreach ($side in 'Host','Client') { $fx.Add("$side " + (Line $side "body-to|$($m.Groups[1].Value)|$($m.Groups[3].Value)" 'BODY-TO')) }
            Start-Sleep -Seconds 8
        }
        $fx.Add("start: " + (Line Host "fight-start|$FightLevel" 'FIGHT-START'))
        foreach ($t in 5, 15, 30) {
            Start-Sleep -Seconds (@{ 5 = 5; 15 = 10; 30 = 15 })[$t]
            $fx.Add("t=$t host:   " + (Line Host 'fight-state' 'FIGHT-STATE'))
            $fx.Add("t=$t joiner: " + (Line Client 'fight-state' 'FIGHT-STATE'))
            foreach ($side in 'Host','Client') { Probe $side "shot|$(Join-Path $OutputPath "fight-t$t-$($side.ToLower()).png")" | Out-Null }
        }
        $fx.Add("stop: " + (Line Host 'fight-stop' 'FIGHT-STOP'))
        Start-Sleep -Seconds 5
        $fx.Add("after host:   " + (Line Host 'fight-state' 'FIGHT-STATE'))
        $fx.Add("after joiner: " + (Line Client 'fight-state' 'FIGHT-STATE'))
        [IO.File]::WriteAllText((Join-Path $OutputPath 'fight-explore.txt'), ($fx -join "`n"))
        Check $true 'Fight exploration recorded'
    }
    if ($FightWatchExperiment) {
        # The others hear when a player's fight begins and how it ends (in their own language).
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $fw = New-Object System.Collections.Generic.List[string]
        foreach ($step in @(@('start','kämpft gerade'), @('won','gewonnen'), @('start','kämpft gerade'), @('lost','verloren'), @('left','vorbei'))) {
            Probe Host "fight-watch|$($step[0])" | Out-Null
            Start-Sleep -Seconds 2
            $seen = Line Client 'fight-watch' 'FIGHT-WATCH'
            $fw.Add("$($step[0]): $seen")
            Check ($seen -match $step[1]) "The joiner hears the host's fight: $($step[0]) ($seen)"
        }
        Probe Client "shot|$(Join-Path $OutputPath 'fightwatch-joiner.png')" | Out-Null
        [IO.File]::WriteAllText((Join-Path $OutputPath 'fight-watch.txt'), ($fw -join "`n"))
    }
    if ($FightBuildExperiment) {
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $fb = New-Object System.Collections.Generic.List[string]
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null }
        $before = @{ Host = Line Host 'wgo-count|barricade_1_fight' 'WGO-COUNT'; Client = Line Client 'wgo-count|barricade_1_fight' 'WGO-COUNT' }
        $baseBefore = Line Client 'military-base' 'MILITARY-BASE'
        $placed = Line Host 'build-place-fight|barricade_1_fight' 'BUILD-PLACE-FIGHT'
        Start-Sleep -Seconds 4
        $after = @{ Host = Line Host 'wgo-count|barricade_1_fight' 'WGO-COUNT'; Client = Line Client 'wgo-count|barricade_1_fight' 'WGO-COUNT' }
        $baseAfter = Line Client 'military-base' 'MILITARY-BASE'
        $fb.Add("$placed"); $fb.Add("before $($before.Host) / $($before.Client)"); $fb.Add("after $($after.Host) / $($after.Client)"); $fb.Add("joiner base $baseBefore -> $baseAfter")
        $n = { param($t) [int]([regex]::Match($t, '=(\d+)$').Groups[1].Value) }
        Check ((& $n $after.Host) -eq (& $n $before.Host) + 1) "The fight building is placed on the host ($placed)"
        Check ((& $n $after.Client) -eq (& $n $before.Client) + 1) "The fight building reaches the joiner ($($before.Client) -> $($after.Client))"
        $clientLog = Join-Path $ClientPath 'BepInEx\LogOutput.log'
        Check (-not (Select-String -LiteralPath $clientLog -Pattern 'could not apply a building change' -Quiet)) "The joiner applies it without an error (base $baseBefore -> $baseAfter)"
        Probe Client "shot|$(Join-Path $OutputPath 'fightbuild-joiner.png')" | Out-Null
        [IO.File]::WriteAllText((Join-Path $OutputPath 'fight-build.txt'), ($fb -join "`n"))
    }
    if ($WorldCopyExperiment) {
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $wc = New-Object System.Collections.Generic.List[string]
        $tidied = Join-Path $saveFolder 'GK2Coop\world-copies'
        $movedOld = Test-Path -LiteralPath (Join-Path $tidied 'GK2Coop_00000000000000aa.dat')
        $wc.Add("old copy moved: $movedOld")
        Check ($movedOld -and -not (Test-Path -LiteralPath (Join-Path $saveFolder 'GK2Coop_00000000000000aa.dat'))) "A world copy from an earlier session is moved out of the save list at start"
        $a = Line Client 'active-slot' 'ACTIVE-SLOT'
        $wc.Add("joiner: $a")
        $copy = ([regex]::Match($a, '(GK2Coop_[0-9a-f]{16})@([^;]*)')).Groups
        Check ($copy[1].Success -and $copy[2].Value -match '2000') "The joiner's copy of the host's world is dated 2000 ($($copy[1].Value) @ $($copy[2].Value))"
        Check ($a -notmatch "ACTIVE-SLOT $($copy[1].Value) ") "After joining, the joiner's own Continue no longer opens the copy ($a)"
        $datFile = Join-Path $saveFolder ($copy[1].Value + '.dat')
        $before = (Get-Item -LiteralPath $datFile).LastWriteTime
        $saved = Line Client 'save-now' 'SAVE-NOW'
        Start-Sleep -Seconds 3
        $after = (Get-Item -LiteralPath $datFile).LastWriteTime
        $clientLog = Join-Path $ClientPath 'BepInEx\LogOutput.log'
        $skipped = Select-String -LiteralPath $clientLog -Pattern 'Save bootstrap: not saving' -Quiet
        $wc.Add("save: $saved; before $before after $after; skipped=$skipped")
        Check ($before -eq $after -and $skipped) "A save on the joiner does not write the copy ($saved)"
        [IO.File]::WriteAllText((Join-Path $OutputPath 'world-copy.txt'), ($wc -join "`n"))
    }
    if ($BuildPlayground) {
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $pg = New-Object System.Collections.Generic.List[string]
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null }
        $pg.Add((Line Host 'test-tools|Home' 'TEST-TOOLS'))
        Start-Sleep -Seconds 12
        $pg.Add((Line Host 'test-tools|yard' 'TEST-TOOLS'))
        $pg.Add((Line Host 'test-tools|kit' 'TEST-TOOLS'))
        $pg.Add((Line Host 'test-tools|refill' 'TEST-TOOLS'))
        Start-Sleep -Seconds 3
        Probe Host "shot|$(Join-Path $OutputPath 'playground.png')" | Out-Null
        $saved = Line Host 'save-now' 'SAVE-NOW'
        $pg.Add($saved)
        Start-Sleep -Seconds 5
        [IO.File]::WriteAllText((Join-Path $OutputPath 'playground.txt'), ($pg -join "`n"))
        Check ($saved -match "SAVE-NOW $slot") "The playground is saved ($saved)"
    }
    if ($TestToolsExperiment) {
        # The playground tools, used as a tester would: a test yard built at home reaches the
        # joiner, the kit, the grave/table lock on the new objects, the menu page, a sermon.
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        function Counts([string]$Peer) { ($yardIds | ForEach-Object { Line $Peer "wgo-count|$_" 'WGO-COUNT' }) -join ' ' }
        $tt = New-Object System.Collections.Generic.List[string]
        $yardIds = 'chest','sawhorse','iron_anvil','furnace_1','autopsy_table_1','embalm_table_1','grave_empty','grave_ground','garden_empty'
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null }
        $tt.Add((Line Host 'test-tools|Home' 'TEST-TOOLS'))
        $tt.Add((Line Client 'test-tools|Home' 'TEST-TOOLS'))
        Start-Sleep -Seconds 12
        $tt.Add("host at " + (Line Host 'where' 'WHERE'))
        $before = @{ Host = Counts Host; Client = Counts Client }
        $yard = Line Host 'test-tools|yard' 'TEST-TOOLS'
        $tt.Add($yard)
        Start-Sleep -Seconds 4
        $after = @{ Host = Counts Host; Client = Counts Client }
        $tt.Add("before host: $($before.Host)"); $tt.Add("after host:  $($after.Host)")
        $tt.Add("before joiner: $($before.Client)"); $tt.Add("after joiner:  $($after.Client)")
        $grown = 0; $grownJoiner = 0
        foreach ($id in $yardIds) {
            $pattern = [regex]::Escape($id) + '=(\d+)'
            $hb = [int]([regex]::Match($before.Host, $pattern).Groups[1].Value); $ha = [int]([regex]::Match($after.Host, $pattern).Groups[1].Value)
            $cb = [int]([regex]::Match($before.Client, $pattern).Groups[1].Value); $ca = [int]([regex]::Match($after.Client, $pattern).Groups[1].Value)
            if ($ha -eq $hb + 1) { $grown++ }
            if ($ca -eq $cb + 1) { $grownJoiner++ }
        }
        Check ($yard -match 'zombie: at the sawhorse' -and $grown -eq $yardIds.Count) "The test yard is built on the host: $grown of $($yardIds.Count) objects and a zombie ($yard)"
        Check ($grownJoiner -eq $yardIds.Count) "The joiner gets the whole test yard ($grownJoiner of $($yardIds.Count))"
        foreach ($side in 'Host','Client') { Probe $side "shot|$(Join-Path $OutputPath "testtools-yard-$($side.ToLower()).png")" | Out-Null }
        $kit = Line Client 'test-tools|kit' 'TEST-TOOLS'
        $tt.Add("joiner kit: $kit")
        Check ($kit -match 'money \+1000' -and $kit -match ',') "The kit works for the joiner ($kit)"
        # The new autopsy table is the nearest one: one player at a time there too.
        $found = Line Host 'work-lock|find|autopsy' 'WORK-LOCK'
        $id = ([regex]::Match($found, 'WORK-LOCK ([0-9a-fA-F-]{8,})=')).Groups[1].Value
        $tt.Add("nearest table: $found")
        $t1 = Line Host "work-lock|interact|$id" 'WORK-LOCK'
        Start-Sleep -Seconds 1
        $t2 = Line Client "work-lock|interact|$id" 'WORK-LOCK'
        $tt.Add("lock: $t1 / $t2")
        Check ($found -match '@[0-9]\.' -and $t2 -match 'held by Host') "The yard's autopsy table is locked while the host works it ($t2)"
        Probe Host 'work-lock|close' | Out-Null
        # The menu page, as a tester reaches it.
        Probe Host 'pause-coop|open' | Out-Null
        Start-Sleep -Milliseconds 800
        Probe Host 'pause-coop|click|Koop' | Out-Null
        Start-Sleep -Seconds 1
        Probe Host 'pause-coop|choose|Test tools' | Out-Null
        Start-Sleep -Seconds 1
        $page = Line Host 'pause-coop' 'PAUSE-COOP'
        $tt.Add("page: $page")
        Probe Host "shot|$(Join-Path $OutputPath 'testtools-page.png')" | Out-Null
        Check ($page -match 'Test tools' -and $page -match 'Build the test yard here') "Pause > Co-op > Test tools shows the tools ($page)"
        Probe Host 'close-windows' | Out-Null
        Probe Host 'pause-coop|choose|Back' | Out-Null
        # A sermon from the tools is offered like a story scene.
        $sermon = Line Host 'test-tools|sermon' 'TEST-TOOLS'
        Start-Sleep -Seconds 3
        $share = Line Client 'scene-share' 'SCENE-SHARE'
        $tt.Add("sermon: $sermon / $share")
        Check ($sermon -match 'sermon ' -and $share -match 'offer from Host \(System_Pray\)') "A sermon started from the tools is offered to the joiner ($share)"
        Start-Sleep -Seconds 40
        [IO.File]::WriteAllText((Join-Path $OutputPath 'test-tools.txt'), ($tt -join "`n"))
    }
    if ($TagTraceExperiment) {
        # The local name tag frame by frame while the camera settles after the player moved.
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null }
        Start-Sleep -Seconds 3
        foreach ($step in @(@('1.5','0'), @('0','1.2'), @('-1.5','-1.2'))) {
            Probe Host 'tag-trace|150' | Out-Null
            $moved = Probe Host "move-body|$($step[0])|$($step[1])"
            [IO.File]::AppendAllText((Join-Path $OutputPath 'tag-trace.txt'), "$moved`n")
            Start-Sleep -Seconds 5
            $trace = Probe Host 'tag-trace'
            [IO.File]::AppendAllText((Join-Path $OutputPath 'tag-trace.txt'), "=== move $($step -join ',')`n$trace`n")
        }
        Check (Test-Path (Join-Path $OutputPath 'tag-trace.txt')) 'Tag trace recorded'
    }
    if ($WorkLockExperiment) {
        # One player at a time at a grave or an autopsy table: the other is told who is working
        # there, and can go ahead once that player is done.
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $wl = New-Object System.Collections.Generic.List[string]
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null }
        foreach ($kind in 'grave','autopsy') {
            $found = Line Host "work-lock|find|$kind" 'WORK-LOCK'
            $wl.Add("$kind on host: $found")
            $id = ([regex]::Match($found, 'WORK-LOCK ([0-9a-fA-F-]{8,})=')).Groups[1].Value
            if (-not $id) { Check $false "A $kind to test with ($found)"; continue }
            $t1 = Line Host "work-lock|interact|$id" 'WORK-LOCK'
            Start-Sleep -Seconds 1
            $wl.Add("host opens: $t1")
            Probe Host "shot|$(Join-Path $OutputPath "worklock-$kind-host.png")" | Out-Null
            $t2 = Line Client "work-lock|interact|$id" 'WORK-LOCK'
            Start-Sleep -Milliseconds 500
            $wl.Add("joiner tries: $t2")
            Probe Client "shot|$(Join-Path $OutputPath "worklock-$kind-joiner.png")" | Out-Null
            Check ($t2 -match 'refused=[1-9]' -and $t2 -match 'held by') "While the host works a $kind, the joiner is told and kept out ($t2)"
            Probe Host 'work-lock|close' | Out-Null
            Probe Client 'close-windows' | Out-Null
            Start-Sleep -Seconds 2
            $t3 = Line Client "work-lock|interact|$id" 'WORK-LOCK'
            Start-Sleep -Seconds 1
            $wl.Add("joiner after: $t3")
            $h = Line Host 'work-lock' 'WORK-LOCK'
            $wl.Add("host sees: $h")
            Check ($h -match "held=\[[^\]]*=") "Once the host is done, the joiner can work the $kind and the host sees it held ($h)"
            $t4 = Line Host "work-lock|interact|$id" 'WORK-LOCK'
            $wl.Add("host tries: $t4")
            Check ($t4 -match 'refused=[1-9]') "The other way round, the host is kept out while the joiner works ($t4)"
            Probe Client 'work-lock|close' | Out-Null
            Probe Host 'close-windows' | Out-Null
            Start-Sleep -Seconds 2
            $h = Line Host 'work-lock' 'WORK-LOCK'
            $wl.Add("after: $h")
            Check ($h -match 'held=\[\]') "Closing the window lets go ($h)"
        }
        [IO.File]::WriteAllText((Join-Path $OutputPath 'work-lock.txt'), ($wl -join "`n"))
    }
    if ($SpeechExperiment) {
        # A conversation seen from nearby: the host's line above the host on the joiner's screen,
        # an NPC's line above the NPC; a player far away sees neither.
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $sp = New-Object System.Collections.Generic.List[string]
        foreach ($side in 'Host','Client') { Probe $side 'close-windows' | Out-Null }
        # Both in the village, next to Jeffry.
        foreach ($side in 'Host','Client') { $sp.Add((Probe $side 'teleport-wgo|npc_jeffry')) }
        Start-Sleep -Seconds 10
        $sp.Add((Probe Host 'say|player|124_village_money_chest_1'))
        Start-Sleep -Seconds 1
        $b = Line Client 'bubbles' 'BUBBLES'
        $bl = ((Probe Client 'bubbles') -split "`n" | Where-Object { $_ -like 'BUBBLE *' }) -join ' / '
        $sp.Add("player line: $b $bl")
        Check ($b -match 'active=[1-9]') "The host's own line shows on the joiner nearby ($bl)"
        Probe Client "shot|$(Join-Path $OutputPath 'speech-1-player.png')" | Out-Null
        Start-Sleep -Seconds 6
        $sp.Add((Probe Host 'say|npc_jeffry|124_village_money_chest_2'))
        Start-Sleep -Seconds 1
        $b = Line Client 'bubbles' 'BUBBLES'
        $bl = ((Probe Client 'bubbles') -split "`n" | Where-Object { $_ -like 'BUBBLE *' }) -join ' / '
        $sp.Add("npc line: $b $bl")
        Check ($b -match 'active=[1-9]') "An NPC's line in the host's conversation shows on the joiner nearby ($bl)"
        Probe Client "shot|$(Join-Path $OutputPath 'speech-2-npc.png')" | Out-Null
        Start-Sleep -Seconds 6
        # Far away: the joiner goes home.
        $sp.Add((Probe Client 'teleport-wgo|scene:RuinedTemple'))
        Start-Sleep -Seconds 10
        $before = Line Client 'speech' 'SPEECH'
        $sp.Add((Probe Host 'say|npc_jeffry|124_village_money_chest_2'))
        Start-Sleep -Seconds 2
        $after = Line Client 'speech' 'SPEECH'
        $sp.Add("far: $before -> $after")
        $shownBefore = [int]([regex]::Match($before, 'shown=(\d+)').Groups[1].Value); $shownAfter = [int]([regex]::Match($after, 'shown=(\d+)').Groups[1].Value)
        Check ($shownAfter -eq $shownBefore -and $after -match 'ignored=[1-9]') "A player far away does not see the conversation ($after)"
        [IO.File]::WriteAllText((Join-Path $OutputPath 'speech.txt'), ($sp -join "`n"))
    }
    if ($PausePadExperiment) {
        # In the world with a controller only: the pause menu's Co-op entry opens the co-op
        # window; the pad chooses, A toggles the name tags, B closes; the stick does not walk.
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        function Pad([string]$Button) { Probe Client "pad-button|$Button" | Out-Null; Start-Sleep -Milliseconds 500 }
        function Focused { (((Probe Client 'nav-info') -split "`n") | Where-Object { $_ -like 'CONTROLLER *Pause*' }) -join '' }
        # Moves the co-op window's choice down to the entry whose label matches.
        function PadTo([string]$Pattern) {
            for ($k = 0; $k -lt 10; $k++) {
                $state = Line Client 'pause-coop' 'PAUSE-COOP'
                if ($state -match ('pad=<(' + $Pattern + ')')) { return $state }
                Pad 'DDown'
            }
            return $state
        }
        $log = New-Object System.Collections.Generic.List[string]
        Probe Client 'close-windows' | Out-Null
        $log.Add((Probe Client 'pad|Xbox_XboxController'))
        Start-Sleep -Seconds 1
        $p = Line Client 'pause-coop' 'PAUSE-COOP'
        $log.Add("before: $p")
        Check ($p -match 'entry=True:') "The pause menu has a Co-op entry in a co-op game ($p)"
        $log.Add((Probe Client 'pause-coop|open'))
        Start-Sleep -Seconds 1
        $reached = $false
        for ($i = 0; $i -lt 8 -and -not $reached; $i++) {
            $f = Focused
            $log.Add("focus: $f")
            if ($f -match 'focused=[^=]*GK2CoopPauseButton') { $reached = $true } else { Pad 'DDown' }
        }
        Probe Client "shot|$(Join-Path $OutputPath 'pause-1-menu.png')" | Out-Null
        Check $reached "The controller reaches the Co-op entry in the pause menu ($f)"
        Pad 'A'
        Start-Sleep -Seconds 1
        $p = Line Client 'pause-coop' 'PAUSE-COOP'
        $log.Add("A: $p")
        Probe Client "shot|$(Join-Path $OutputPath 'pause-2-coop.png')" | Out-Null
        Check ($p -match 'open=True' -and $p -notmatch 'pad=none') "A opens the co-op window with the controller in it ($p)"
        $tagsBefore = $p -match '<Namensschilder ausblenden>|<Hide name tags>'
        $where0 = Line Client 'where' 'WHERE'
        $p = PadTo 'Namensschilder|Hide name tags|Show name tags'
        $log.Add("to name tags: $p")
        Pad 'A'
        Start-Sleep -Seconds 1
        $p2 = Line Client 'pause-coop' 'PAUSE-COOP'
        $log.Add("A on it: $p2")
        Check ($p2 -ne $p) "A on a choice in the co-op window does it ($p2)"
        $where1 = Line Client 'where' 'WHERE'
        Check ($where0 -eq $where1) "The player stays put while the window has the controller ($where0 / $where1)"
        # The same in a slow game (a hitch, a busy PC: three frames a second). The window counted
        # its controller time in seconds and took every slow frame for a fresh start: A did nothing.
        Probe Client 'fps|3' | Out-Null
        Start-Sleep -Seconds 2
        Pad 'A'
        $deadline = (Get-Date).AddSeconds(6)
        do { Start-Sleep -Milliseconds 500; $p3 = Line Client 'pause-coop' 'PAUSE-COOP' } while ($p3 -eq $p2 -and (Get-Date) -lt $deadline)
        Probe Client 'fps|0' | Out-Null
        $log.Add("A at 3 fps: $p3")
        Check ($p3 -ne $p2) "A on a choice in the co-op window does it in a slow game too, three frames a second ($p3)"
        Start-Sleep -Seconds 1
        Pad 'B'
        Start-Sleep -Seconds 1
        $p = Line Client 'pause-coop' 'PAUSE-COOP'
        $log.Add("B: $p")
        Check ($p -match 'open=False') "B closes the co-op window ($p)"
        # A quick message with the controller only: Pause > Co-op > Quick message > the first line.
        $log.Add((Probe Client 'pause-coop|open'))
        Start-Sleep -Seconds 1
        for ($i = 0; $i -lt 8 -and (Focused) -notmatch 'focused=[^=]*GK2CoopPauseButton'; $i++) { Pad 'DDown' }
        Pad 'A'
        Start-Sleep -Seconds 1
        $q = PadTo 'Schnellnachricht|Quick message'
        Pad 'A'
        Start-Sleep -Seconds 1
        $q = Line Client 'pause-coop' 'PAUSE-COOP'
        $log.Add("quick page: $q")
        # The host reads in English meanwhile: the line arrives in the reader's language.
        $hostLang = if ((Probe Host 'languages') -match 'current=(\S+)') { $Matches[1] } else { 'de' }
        Probe Host 'game-lang|en' | Out-Null
        # The switch takes a moment under load: wait until the host reads in English.
        for ($i = 0; $i -lt 10 -and (Probe Host 'languages') -notmatch 'current=en\b'; $i++) { Start-Sleep -Milliseconds 500 }
        # The mod looks at the game's language every 2 s (CoopText L.Language).
        Start-Sleep -Seconds 3
        Pad 'A'
        Start-Sleep -Seconds 2
        $last = (((Probe Host 'chat-last') -split "`n") | Where-Object { $_ -like 'CHAT-LAST*' }) -join ''
        $log.Add("host heard: $last")
        Probe Host "game-lang|$hostLang" | Out-Null
        Check ($q -match '<Komm her!>' -and $last -match 'Come here!') "A quick message sent in German with the controller alone arrives in the reader's English ($last)"
        Probe Client 'pad|off' | Out-Null
        [IO.File]::WriteAllText((Join-Path $OutputPath 'pause-pad.txt'), ($log -join "`n"))
    }
    if ($ScenePromptLook) {
        # The scene question and the watching panel as a player sees them, with the keyboard and
        # with a controller (the game's own button icons).
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        Probe Client 'close-windows' | Out-Null
        Probe Client 'teleport-wgo|npc_jeffry' | Out-Null
        Start-Sleep -Seconds 10
        foreach ($device in 'keys','Xbox_XboxController','Sony_DualSense') {
            if ($device -ne 'keys') { $shareLogPad = Probe Client "pad|$device"; Check ($shareLogPad -match 'active=True') "A controller is in use for the look ($shareLogPad)" }
            $tag = if ($device -eq 'keys') { 'keys' } else { $device.Split('_')[0].ToLower() }
            Probe Client 'scene-answer|preview' | Out-Null
            Start-Sleep -Milliseconds 800
            $asking = Line Client 'scene-share' 'SCENE-SHARE'
            Probe Client "shot|$(Join-Path $OutputPath "prompt-$tag-1-asking.png")" | Out-Null
            $fit = Line Client 'panel-fit|GK2Coop.ScenePrompt' 'PANEL-FIT'
            Check ($asking -match 'prompt=\[' -and $fit -match ' in texts fit') "The question is shown ($tag): $asking / $fit"
            Probe Client 'scene-answer|preview-yes' | Out-Null
            Start-Sleep -Milliseconds 500
            Probe Client "shot|$(Join-Path $OutputPath "prompt-$tag-2-yes.png")" | Out-Null
            Start-Sleep -Seconds 2
            Probe Client 'scene-answer|preview' | Out-Null
            Start-Sleep -Milliseconds 800
            Probe Client 'scene-answer|preview-no' | Out-Null
            Start-Sleep -Milliseconds 500
            Probe Client "shot|$(Join-Path $OutputPath "prompt-$tag-3-no.png")" | Out-Null
            Start-Sleep -Seconds 2
            $gone = Line Client 'scene-share' 'SCENE-SHARE'
            Check ($gone -match 'prompt=hidden') "After the answer the question is gone ($tag): $gone"
            Probe Client 'scene-answer|preview-watching' | Out-Null
            Start-Sleep -Milliseconds 800
            Probe Client "shot|$(Join-Path $OutputPath "prompt-$tag-4-watching.png")" | Out-Null
            $w = Line Client 'scene-share' 'SCENE-SHARE'
            $wfit = Line Client 'panel-fit|GK2Coop.SceneWatching' 'PANEL-FIT'
            Check ($w -match 'watching=\[' -and $wfit -match ' in texts fit') "The watching panel is shown ($tag): $wfit"
            Probe Client 'scene-answer|preview-off' | Out-Null
            if ($device -ne 'keys') {
                # The real controller buttons: Y watches, B keeps playing, and the game sees neither.
                Probe Client 'close-windows' | Out-Null
                Probe Client 'scene-answer|preview' | Out-Null
                Start-Sleep -Milliseconds 800
                # The answer is read on the next frame; under load that is not always within 400 ms.
                function Answer([string]$want) {
                    $deadline = (Get-Date).AddSeconds(5)
                    do { Start-Sleep -Milliseconds 300; $s = Line Client 'scene-share' 'SCENE-SHARE' } while ($s -notmatch $want -and (Get-Date) -lt $deadline)
                    return $s
                }
                Probe Client 'pad-button|Y' | Out-Null
                $y = Answer '\(Accepted\)'
                $opened = (((Probe Client 'close-windows') -split "`n") | Where-Object { $_ -like 'CLOSE-WINDOWS*' }) -join ''
                Check ($y -match '\(Accepted\)' -and ($opened -replace '.*CLOSE-WINDOWS','').Trim() -eq '') "Y on the controller answers Watch, and the game does not open anything ($tag): $y / $opened"
                Start-Sleep -Seconds 2
                Probe Client 'scene-answer|preview' | Out-Null
                Start-Sleep -Milliseconds 800
                Probe Client 'pad-button|B' | Out-Null
                $b = Answer '\(Declined\)'
                Check ($b -match '\(Declined\)') "B on the controller answers Keep playing ($tag): $b"
                Start-Sleep -Seconds 2
                # In a slow game (three frames a second) the question stays and B still answers it.
                Probe Client 'fps|3' | Out-Null
                Probe Client 'scene-answer|preview' | Out-Null
                Start-Sleep -Seconds 3
                $asked = Line Client 'scene-share' 'SCENE-SHARE'
                Probe Client 'pad-button|B' | Out-Null
                $bs = Answer '\(Declined\)'
                Probe Client 'fps|0' | Out-Null
                Check ($asked -match '\(Asking\)' -and $bs -match '\(Declined\)') "In a slow game the question stays up and B answers it ($tag): $asked / $bs"
                Start-Sleep -Seconds 2
                Probe Client 'scene-answer|preview-off' | Out-Null
            }
        }
        Probe Client 'pad|off' | Out-Null
    }
    if ($SceneDeclineExperiment) {
        # Declining, and not answering: the joiner keeps playing, nothing plays there.
        function Share([string]$Side) { ((((Probe $Side 'scene-share') -split "`n") | Where-Object { $_ -like 'SCENE-SHARE *' }) -join '') }
        Probe Host 'fire-script-event|Event_113_Sewers_God|113_sewers_god_1' | Out-Null
        Start-Sleep -Seconds 3
        $s1 = Share Client
        Check ($s1 -match 'offer from Host') "The joiner is asked ($s1)"
        Probe Client 'scene-answer|decline' | Out-Null
        Start-Sleep -Seconds 3
        $s2 = Share Client
        $t = Probe Client 'cutscene-trace'
        Check ($s2 -match 'replays=0' -and $s2 -match 'prompt=hidden' -and $t -notmatch 'cinematic on') "Declining keeps the joiner playing: no scene, no prompt ($s2)"
        Probe Host 'fire-script-event|Event_127_Palace_Battle|127_palace_battle_1' | Out-Null
        Start-Sleep -Seconds 3
        $s3 = Share Client
        Check ($s3 -match 'offer from Host \(Event_127') "A second scene is offered too ($s3)"
        Start-Sleep -Seconds 11
        $s4 = Share Client
        $t = Probe Client 'cutscene-trace'
        Check ($s4 -match 'replays=0' -and $s4 -match 'prompt=hidden' -and $s4 -notmatch 'offer from' -and $t -notmatch 'cinematic on') "Without an answer the offer closes after 10 seconds and nothing plays ($s4)"
        [IO.File]::WriteAllText((Join-Path $OutputPath 'scene-decline.txt'), (@($s1, $s2, $s3, $s4, $t) -join "`n"))
    }
    if ($SceneShareExperiment) {
        # A story scene started by the host is offered to the joiner, who watches it: same scene on
        # both screens, the joiner's copy skipping the steps the host's already sync.
        function Share([string]$Side) { ((((Probe $Side 'scene-share') -split "`n") | Where-Object { $_ -like 'SCENE-SHARE *' }) -join '') }
        $shareLog = New-Object System.Collections.Generic.List[string]
        # Who triggers the scene and who watches: the host and the joiner, or the other way round
        # (-ShareByJoiner: the offer goes through the host to the other players).
        $T = 'Host'; $V = 'Client'; $TPath = $HostPath; $VPath = $ClientPath
        if ($ShareByJoiner) { $T = 'Client'; $V = 'Host'; $TPath = $ClientPath; $VPath = $HostPath }
        $shareScript, $shareEvent = $ShareScene -split ':', 2
        foreach ($side in 'Host','Client') { $shareLog.Add("$($side): " + (Probe $side 'close-windows')) }
        if ($ShareNear) {
            # The host goes where the scene happens, as a player triggering it would be.
            $shareLog.Add((Probe $T "teleport-wgo|$ShareNear"))
            Start-Sleep -Seconds 10
            $shareLog.Add("host at " + (((Probe $T 'where') -split "`n" | Where-Object { $_ -like 'WHERE *' }) -join ''))
        }
        # A sermon: the pray window picks the sermon before firing sermon_start.
        $sermon = $shareScript -eq 'System_Pray'
        function Parishioners([string]$Side) { (('npc_monk_*', 'npc_villager*') | ForEach-Object { (((Probe $Side "wgo-count|$_") -split "`n") | Where-Object { $_ -like 'WGO-COUNT*' }) -join '' }) -join ' ' }
        if ($sermon) {
            $shareLog.Add((Probe $T 'sermon-prepare'))
            $parishBefore = @{}; foreach ($side in 'Host','Client') { $parishBefore[$side] = Parishioners $side }
            $shareLog.Add("parishioners before: host $($parishBefore.Host) / joiner $($parishBefore.Client)")
        }
        $clientBefore = (((Probe $V 'where') -split "`n" | Where-Object { $_ -like 'WHERE *' }) -join '')
        Probe $T "fire-script-event|$shareScript|$shareEvent" | Out-Null
        Start-Sleep -Seconds 3
        $hostShare = Share $T; $clientShare = Share $V
        $shareLog.Add("offered: host $hostShare"); $shareLog.Add("offered: joiner $clientShare")
        Check ($hostShare -match 'announced=1') "The host's scene is offered to the others ($hostShare)"
        Check ($clientShare -match "offer from $T \($shareScript\)" -and $clientShare -match 'prompt=\[') "The joiner is asked whether to watch it ($clientShare)"
        Probe $V "shot|$(Join-Path $OutputPath 'share-1-prompt.png')" | Out-Null
        $shareLog.Add((Probe $V 'scene-answer|watch'))
        Start-Sleep -Seconds 10
        $clientShare = Share $V
        $trace = Probe $V 'cutscene-trace'
        $shareLog.Add("watching: joiner $clientShare"); $shareLog.Add($trace)
        Check ($clientShare -match 'replays=1') "Accepting plays the same scene on the joiner ($clientShare)"
        Check ($trace -match 'cinematic on') "The joiner sees the scene: bars on, camera taken ($(([regex]::Match($trace, 'cinematics=\d+')).Value))"
        foreach ($side in 'Host','Client') { Probe $side "shot|$(Join-Path $OutputPath "share-2-watching-$($side.ToLower()).png")" | Out-Null }

        foreach ($side in 'Host','Client') { $shareLog.Add("bubbles $($side): " + (Probe $side 'bubbles')) }
        if ($ShareSkipEvent) {
            # A step that changes the world (here a notes window opening) sent into the joiner's
            # copy: it is skipped there, the scene goes on.
            $before = Share $V
            $shareLog.Add((Probe $V "scene-answer|event:$ShareSkipEvent"))
            Start-Sleep -Seconds 3
            $after = Share $V
            $windows = (((Probe $V 'close-windows') -split "`n") | Where-Object { $_ -like 'CLOSE-WINDOWS*' }) -join ''
            $shareLog.Add("skip: $after / $windows")
            $n0 = [int]([regex]::Match($before, 'skipped steps=(\d+)').Groups[1].Value); $n1 = [int]([regex]::Match($after, 'skipped steps=(\d+)').Groups[1].Value)
            Check ($n1 -gt $n0 -and $windows -notmatch 'Note') "A world step in the joiner's copy is skipped ($n0 -> $n1 skipped; open windows: $windows)"
        }
        if ($ShareAnswer) {
            # The host answers when the scene asks; the joiner's copy follows without asking.
            $answers = 'ANSWERS none'
            for ($i = 0; $i -lt 120 -and $answers -match 'ANSWERS none'; $i++) {
                Start-Sleep -Seconds 1
                $answers = (((Probe $T 'answers') -split "`n") | Where-Object { $_ -like 'ANSWERS *' }) -join ''
            }
            $shareLog.Add("host sees: $answers")
            Probe $T "shot|$(Join-Path $OutputPath 'share-answer-host.png')" | Out-Null
            Probe $V "shot|$(Join-Path $OutputPath 'share-answer-client.png')" | Out-Null
            $clientAnswers = (((Probe $V 'answers') -split "`n") | Where-Object { $_ -like 'ANSWERS *' }) -join ''
            $shareLog.Add("joiner sees: $clientAnswers")
            $shareLog.Add("host then: " + (Share $T)); $shareLog.Add("joiner then: " + (Share $V))
            Start-Sleep -Seconds 1
            [IO.File]::WriteAllText((Join-Path $OutputPath 'scene-share.txt'), ($shareLog -join "`n"))
            Check ($answers -notmatch 'none') "The host is asked to answer ($answers)"
            Check ($clientAnswers -match 'none') "The joiner is not asked; the host answers for the scene ($clientAnswers)"
            $shareLog.Add((Probe $T 'answer-pick|0'))
            Start-Sleep -Seconds 4
            $hostShare = Share $T
            Check ($hostShare -match 'answers sent=1') "The host's answer is sent ($hostShare)"
        }
        # Until the scene is over on the joiner (at most 90 s)
        for ($i = 0; $i -lt 45; $i++) {
            Start-Sleep -Seconds 2
            if ((Share $V) -notmatch 'replaying') { Start-Sleep -Seconds 2; break }
        }
        foreach ($side in 'Host','Client') { $shareLog.Add("bubbles later $($side): " + (Probe $side 'bubbles')) }
        $clientShare = Share $V
        $shareLog.Add("later: joiner $clientShare")
        $hostShare = Share $T
        $shareLog.Add("later: host $hostShare")
        $hostEnded = @(Select-String -Path (Join-Path $TPath 'BepInEx\LogOutput.log') -Pattern "Scene share: $shareScript ended here" -ErrorAction SilentlyContinue).Count
        $clientHeard = @(Select-String -Path (Join-Path $VPath 'BepInEx\LogOutput.log') -Pattern "scene ended there after" -ErrorAction SilentlyContinue).Count
        # (With a step sent into the joiner's copy, that copy may end on its own, before the host's.)
        # (A copy may also run to its own end just before the host's end arrives: finished, not stopped.)
        $ranThrough = $clientShare -match 'finished=1' -and $clientShare -match 'stopped=0'
        if (-not $ShareSkipEvent) { Check ($hostEnded -ge 1 -and ($clientHeard -ge 1 -or $ranThrough)) "The end of the host's scene reaches the joiner (host $hostEnded, joiner heard $clientHeard, ran through $ranThrough)" }
        if ($sermon) {
            $log = Get-Content (Join-Path $VPath 'BepInEx\LogOutput.log') -Raw
            Check ($log -match 'prayer_default_speech_02' -and $log -notmatch 'SetSermonContainerActive') "The joiner's copy of the sermon plays to its last line, with the preacher's result"
            Start-Sleep -Seconds 25
            $parishAfter = @{}; foreach ($side in 'Host','Client') { $parishAfter[$side] = Parishioners $side }
            $shareLog.Add("parishioners after: host $($parishAfter.Host) / joiner $($parishAfter.Client)")
            Check ($parishAfter.Host -eq $parishBefore.Host -and $parishAfter.Client -eq $parishBefore.Client) "The churchgoers leave again in both worlds (host $($parishBefore.Host) -> $($parishAfter.Host); joiner $($parishBefore.Client) -> $($parishAfter.Client))"
        }
        if ($clientShare -match 'finished=1') {
            Start-Sleep -Seconds 4
            $clientShare = Share $V
            $clientAfter = (((Probe $V 'where') -split "`n" | Where-Object { $_ -like 'WHERE *' }) -join '')
            $shareLog.Add("before: $clientBefore"); $shareLog.Add("after: $clientAfter")
            Check ($clientShare -match 'returned=1' -and $clientShare -notmatch 'replaying') "After the scene the joiner is taken back ($clientShare)"
            if ($ShareAnswer) { Check ($clientShare -match 'answers followed=1') "The joiner's scene followed the host's answer ($clientShare)" }
            if ($ShareSpawn) {
                # A story NPC the scene spawns: the joiner's copy brings it into the joiner's world
                # too, once.
                $hc = (((Probe $T "wgo-count|$ShareSpawn") -split "`n") | Where-Object { $_ -like 'WGO-COUNT*' }) -join ''
                $cc = (((Probe $V "wgo-count|$ShareSpawn") -split "`n") | Where-Object { $_ -like 'WGO-COUNT*' }) -join ''
                Check ($hc -eq $cc -and $cc -notmatch '=0$') "The scene's NPC is in both worlds, once ($hc / $cc)"
            }
            $b = $clientBefore -split ' '; $c = $clientAfter -split ' '
            $moved = [Math]::Sqrt([Math]::Pow([double]$b[1] - [double]$c[1], 2) + [Math]::Pow([double]$b[2] - [double]$c[2], 2))
            Check ($moved -lt 3 -and $b[3] -eq $c[3]) "The joiner is back where they were ($clientBefore -> $clientAfter)"
            $hud = ((((Probe $V 'hud-ui') -split "`n") | Where-Object { $_ -like 'HUD-UI *' }) -join '')
            $trace = Probe $V 'cutscene-trace'
            Check ($trace -match 'control returned' -and $trace -match 'cinematic off') "The joiner's control and screen are back ($(([regex]::Matches($trace, '(control returned|cinematic off)') | ForEach-Object Value) -join ', '))"
        } else {
            Check $false "The scene finishes on the joiner ($clientShare)"
        }
        foreach ($side in 'Host','Client') { Probe $side "shot|$(Join-Path $OutputPath "share-3-later-$($side.ToLower()).png")" | Out-Null }
        $hud = ((((Probe $V 'hud-ui') -split "`n") | Where-Object { $_ -like 'HUD-UI *' }) -join '')
        $shareLog.Add("hud during scene: $hud")
        [IO.File]::WriteAllText((Join-Path $OutputPath 'scene-share.txt'), ($shareLog -join "`n"))
        foreach ($side in 'Host','Client') { [IO.File]::WriteAllText((Join-Path $OutputPath "scene-trace-$($side.ToLower()).txt"), (Probe $side 'cutscene-trace')) }
    }
    if ($SpriteSurvey) {
        # One pattern per call: the probe splits its arguments on "|", so no alternation.
        $found = foreach ($pattern in ($SpritePatterns -split ',')) { Probe Host "sprites|$pattern" }
        $found | Set-Content -LiteralPath (Join-Path $OutputPath 'sprites-world.txt') -Encoding utf8
        Check (Test-Path (Join-Path $OutputPath 'sprites-world.txt')) 'Sprite survey written'
    }
    if ($UiKitExperiment) {
        $kit = Probe Host 'ui-kit'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'ui-kit.txt'), $kit)
        Check ($kit -match 'DIALOG windows=[1-9]') "The game's dialog window is loaded ($(([regex]::Match($kit, 'DIALOG windows=\d+')).Value))"
        foreach ($side in 'Host','Client') {
            $shot = Join-Path $OutputPath "screen-$($side.ToLower()).png"
            Probe $side "screenshot|$shot" | Out-Null
        }
        Start-Sleep -Seconds 3
        Check (Test-Path (Join-Path $OutputPath 'screen-host.png')) 'A screenshot of the host was taken'
    }
    if ($HudLookExperiment) {
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $hudLog = New-Object System.Collections.Generic.List[string]
        Probe Client 'chat-send|hello from the joiner' | Out-Null
        Probe Host 'chat-send|and the host says hi' | Out-Null
        Start-Sleep -Seconds 2
        Probe Client 'chat-open' | Out-Null
        Start-Sleep -Seconds 2
        foreach ($side in 'Host','Client') {
            $hud = Line $side 'hud-ui' 'HUD-UI'
            $chat = Line $side 'chat-ui' 'CHAT-UI'
            $hudLog.Add("$side $hud"); $hudLog.Add("$side $chat")
            Check ($hud -match 'gameUi=True status=\[.+\]') "The $side's status is drawn in the game's style ($hud)"
            Check ($hud -match 'tags=\[[^\]]*@') "The $side's name tags are drawn in the game's font ($hud)"
            Check ($chat -match 'gameUi=True' -and $chat -match 'lines=[2-9]') "The $side's chat lines are drawn in the game's style ($chat)"
            # The other player in their own colours: every part of the remote body has a palette
            # (without one it is drawn in the raw source colours, blue hair and pink clothes), none
            # borrowed from the local player's.
            $looks = ((Probe $side 'look-draw') -split "`n") | Where-Object { $_ -like 'LOOK *' }
            $remote = @($looks | Where-Object { $_ -like 'LOOK remote *' })
            $hudLog.Add("$side looks: " + ($looks -join ' | '))
            Check ($remote.Count -gt 0 -and -not ($remote | Where-Object { $_ -notmatch 'preset=yes missing=0 shared-with-local=0' })) "The $side draws the other player in their own colours ($($looks -join ' | '))"
            # Another player's body comes with a wisp of its own (the game's multiplayer code switches it
            # on); only the local player's wisp is the story's.
            $wisp = (((Probe $side 'wisp') -split "`n") | Where-Object { $_ -like 'WISP *' }) -join ''
            $hudLog.Add("$side wisp: $wisp")
            Check ($wisp -match 'other players=[1-9]' -and $wisp -match 'their wisps on=0') "On the $side's screen the other player carries no wisp ($wisp)"
            Probe $side "shot|$(Join-Path $OutputPath "hud-$($side.ToLower()).png")" | Out-Null
        }
        if ($ResolutionSurvey) {
            $layouts = [ordered]@{ '1280x720' = '640x360'; '1280x800' = '640x400'; '1280x1024' = '640x512'; '1600x900' = '800x450'; '1920x1080 and 4K' = '960x540'; '1920x1200 and 2880x1800' = '960x600'; '2560x1440' = '1280x720'; '3440x1440' = '1720x720' }
            foreach ($size in $layouts.Keys) {
                $bw, $bh = $layouts[$size] -split 'x'
                foreach ($side in 'Host','Client') { Probe $side "ui-box|$bw|$bh" | Out-Null }
                Start-Sleep -Seconds 3
                foreach ($side in 'Host','Client') {
                    $b = ((((Probe $side 'ui-bounds') -split "`n") | Where-Object { $_ -like 'UI-BOUNDS *' }) -join '')
                    $hudLog.Add("$size $side $b")
                    Check ($b -match 'Column=\[[^\]]* in\]' -and $b -notmatch ' OUT\]') "At $size ($($layouts[$size]) layout) the $side's status and chat fit ($b)"
                    Probe $side "shot|$(Join-Path $OutputPath "res-$($layouts[$size])-hud-$($side.ToLower()).png")" | Out-Null
                }
                Start-Sleep -Seconds 2
            }
            foreach ($side in 'Host','Client') { Probe $side 'ui-box|full' | Out-Null }
        }
        if ($TranslationSurvey) {
            # The HUD in every language: status plate, announcements and the chat line.
            $list = Probe Client 'languages'
            $original = if ($list -match 'current=(\S+)') { $Matches[1] } else { 'en' }
            $ids = @(($list -split "`n") | Where-Object { $_ -like 'LANGUAGE *' } | ForEach-Object { ($_ -split ' ')[1] })
            if ($Languages) { $ids = @($Languages -split ',') }
            $hudFails = New-Object System.Collections.Generic.List[string]
            # First, before any language switch loads other fonts: a fresh game in the player's own
            # language receiving chat in four other scripts.
            Probe Host 'chat-send|안녕하세요 こんにちは 你好 привет' | Out-Null
            Start-Sleep -Seconds 3
            $g = ((((Probe Client 'glyphs') -split "`n") | Where-Object { $_ -like 'GLYPHS*' }) -join '')
            $hudLog.Add("mixed chat before switching $g")
            if ($g -match 'missing=[1-9]') { $hudFails.Add("mixed-script chat before any switch " + (([regex]::Matches($g, 'MISSING\[[^\]]*\]') | ForEach-Object { $_.Value }) -join ' ')) }
            Probe Client "shot|$(Join-Path $OutputPath 'tr-mixed-chat-fresh.png')" | Out-Null
            foreach ($id in $ids) {
                Probe Client "game-lang|$id" | Out-Null
                Start-Sleep -Seconds 2
                Probe Client 'announce|{0} joined.|Host' | Out-Null
                Probe Client 'announce|Could not reach {0}:{1}. Make sure the host is hosting. Over the internet, port {1} has to be forwarded to their PC.|10.0.0.9|8889' | Out-Null
                Probe Client 'announce|You sleep and recover. The night only passes when everyone sleeps.' | Out-Null
                # The scene question and the watching panel, in this language
                Probe Client 'scene-answer|preview' | Out-Null
                Probe Client 'scene-answer|preview-watching' | Out-Null
                Start-Sleep -Seconds 1
                foreach ($panel in 'GK2Coop.ScenePrompt','GK2Coop.SceneWatching') {
                    $fit = Line Client "panel-fit|$panel" 'PANEL-FIT'
                    $hudLog.Add("$id $fit")
                    if ($fit -notmatch ' in texts fit') { $hudFails.Add("$id $fit") }
                }
                $gs = ((((Probe Client 'glyphs') -split "`n") | Where-Object { $_ -like 'GLYPHS*' }) -join '')
                if ($gs -match 'missing=[1-9]') { $hudFails.Add("$id scene windows " + (([regex]::Matches($gs, 'MISSING\[[^\]]*\]') | ForEach-Object { $_.Value }) -join ' ')) }
                Probe Client "shot|$(Join-Path $OutputPath "tr-$id-scene.png")" | Out-Null
                Probe Client 'scene-answer|preview-off' | Out-Null
                Probe Client 'chat-open' | Out-Null
                Start-Sleep -Seconds 2
                $hud = Line Client 'hud-ui' 'HUD-UI'
                $g = ((((Probe Client 'glyphs') -split "`n") | Where-Object { $_ -like 'GLYPHS*' }) -join '')
                $hudLog.Add("$id " + (((Probe Client 'places') -split "`n") | Where-Object { $_ -like 'PLACES*' }))
                $hudLog.Add("$id " + (((Probe Client 'game-keys|^gem$') -split "`n") | Where-Object { $_ -like 'GAME-KEYS*' }))
                $hudLog.Add("$id $hud"); $hudLog.Add("$id $g")
                Probe Client "shot|$(Join-Path $OutputPath "tr-$id-hud.png")" | Out-Null
                if ($g -match 'missing=[1-9]') { $hudFails.Add("$id " + (([regex]::Matches($g, 'MISSING\[[^\]]*\]') | ForEach-Object { $_.Value }) -join ' ')) }
                if ($hud -notmatch 'toasts=\[[^\]]+\]') { $hudFails.Add("$id no announcements shown") }
                Start-Sleep -Seconds 1
            }
            Probe Client "game-lang|$original" | Out-Null
            Start-Sleep -Seconds 2
            # Players type in any language: Korean, Japanese, Chinese and Russian chat in a Latin-script game.
            Probe Host 'chat-send|안녕하세요 こんにちは 你好 привет' | Out-Null
            Start-Sleep -Seconds 3
            $g = ((((Probe Client 'glyphs') -split "`n") | Where-Object { $_ -like 'GLYPHS*' }) -join '')
            $hudLog.Add("mixed chat $g")
            if ($g -match 'missing=[1-9]') { $hudFails.Add("mixed-script chat " + (([regex]::Matches($g, 'MISSING\[[^\]]*\]') | ForEach-Object { $_.Value }) -join ' ')) }
            Probe Client "shot|$(Join-Path $OutputPath 'tr-mixed-chat.png')" | Out-Null
            [IO.File]::WriteAllText((Join-Path $OutputPath 'hud-look.txt'), ($hudLog -join "`n"))
            Check ($hudFails.Count -eq 0) "In all $($ids.Count) languages the status, announcements and chat draw every character ($($hudFails -join ' | '))"
        }
        $clientChat = Line Client 'chat-ui' 'CHAT-UI'
        Check ($clientChat -match 'open=True' -and $clientChat -match 'input=True focused=True') "The joiner's chat line opens with the game's text field ($clientChat)"
        Start-Sleep -Seconds 3
        [IO.File]::WriteAllText((Join-Path $OutputPath 'hud-look.txt'), ($hudLog -join "`n"))
    }
    if ($ChatExperiment) {
        function LastChat([string]$Peer) { ((((Probe $Peer 'chat-last') -split "`n") | Where-Object { $_ -like 'CHAT-LAST*' }) -join '').Substring(9).Trim() }
        Probe Client 'chat-send|hello from the joiner' | Out-Null
        Start-Sleep -Seconds 2
        $onHost = LastChat Host
        Check ($onHost -match '^.+: hello from the joiner$') "A joiner's chat line reaches the host with the joiner's name ($onHost)"
        Probe Host 'chat-send|and hello from the host' | Out-Null
        Start-Sleep -Seconds 2
        $onJoiner = LastChat Client
        Check ($onJoiner -match '^.+: and hello from the host$') "The host's chat line reaches the joiner with the host's name ($onJoiner)"
        $hostSender = ($onJoiner -split ':')[0]; $joinerSender = ($onHost -split ':')[0]
        if ($Transport -eq 'Steam') {
            # Over Steam everyone goes by their Steam name, and here both games run on this PC's one
            # account: the same name on both lines, and never the role names used for IP play.
            Check ($hostSender -eq $joinerSender -and $hostSender -notmatch '^(Host|Player \d+|BootstrapGuest)$') "Over Steam each line carries the sender's Steam name (one account here: $joinerSender / $hostSender)"
        } else {
            Check ($joinerSender -ne $hostSender) "Each line carries its sender's own name ($joinerSender / $hostSender)"
        }
        [IO.File]::WriteAllText((Join-Path $OutputPath 'chat.txt'), "host saw: $onHost`njoiner saw: $onJoiner")
    }
    if ($SaveHygieneExperiment) {
        $saved = (((Probe Host 'save-now') -split "`n") | Where-Object { $_ -like 'SAVE-NOW *' }) -join ''
        Start-Sleep -Seconds 3
        $inspect = (((Probe Host 'save-inspect') -split "`n") | Where-Object { $_ -like 'SAVE-INSPECT *' }) -join ''
        Check ($saved -match 'live host=True liveClients=[1-9]') "The host saves during the session, which has its session records ($saved)"
        Check ($inspect -match 'host=False clients=0$') "The written save holds no co-op session records ($inspect)"
        $after = (((Probe Host 'save-now') -split "`n") | Where-Object { $_ -like 'SAVE-NOW *' }) -join ''
        Check ($after -match 'live host=True liveClients=[1-9]') "The live session still has its records after saving ($after)"
        Probe Client 'setres|money|555' | Out-Null
        Start-Sleep -Seconds 3
        Check ((Probe Host 'progress') -match 'WORLD ') 'The session carries on normally after the host saved'
    }
    if ($PorterExperiment) {
        $setup = (((Probe Host 'porter-setup') -split "`n") | Where-Object { $_ -like 'PORTER-SETUP *' }) -join ''
        Check ($setup -match 'stone=True .*chestZone=carrier stationZone=carrier') "The disposable carrier station and stone source are in the carrier zone ($setup)"
        $porterChest = if ($setup -match 'chest=([0-9a-f-]{36})') { $Matches[1] } else { '' }
        $porterStation = if ($setup -match 'station=([0-9a-f-]{36})') { $Matches[1] } else { '' }
        Start-Sleep -Seconds 5
        Check ((Probe Client "has|$porterStation") -match 'True') "The joiner sees the carrier station ($porterStation)"
        $made = (((Probe Host "porter-zombie|$porterStation") -split "`n") | Where-Object { $_ -like 'PORTER-ZOMBIE *' }) -join ''
        Check ($made -match 'zone=carrier') "A zombie is assigned as carrier porter ($made)"
        $porterId = ($made -split ' ')[1]
        $observations = New-Object System.Collections.Generic.List[string]
        $hostStates = New-Object System.Collections.Generic.List[string]
        $clientStates = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt 12; $i++) {
            Start-Sleep -Seconds 10
            foreach ($side in 'Host','Client') {
                $state = (((Probe $side "porter-state|$porterId|$porterChest") -split "`n") | Where-Object { $_ -like "PORTER-STATE $porterId *" }) -join ''
                $observations.Add("t+$(10*($i+1))s $side $state")
                if ($side -eq 'Host') { $hostStates.Add($state) } else { $clientStates.Add($state) }
            }
            if ($hostStates[$i] -match 'source=0 bag=0 target=1 staying=1' -and $clientStates[$i] -match 'source=0 bag=0 target=1') { break }
        }
        [IO.File]::WriteAllText((Join-Path $OutputPath 'porter-work.txt'), ($observations -join "`n"))
        Check (@($hostStates | Where-Object { $_ -match 'source=0 bag=1 target=0' }).Count -gt 0) 'The host porter takes the stone from the source chest into its bag'
        Check (@($hostStates | Where-Object { $_ -match 'source=0 bag=0 target=1' }).Count -gt 0) 'The host porter delivers the stone to the conveyor zone'
        $lastHost = $hostStates[$hostStates.Count-1]
        $lastClient = $clientStates[$clientStates.Count-1]
        Check ($lastHost -match 'source=0 bag=0 target=1 staying=1' -and $lastClient -match 'source=0 bag=0 target=1') "Both peers retain the delivered stone when the porter returns ($lastHost; $lastClient)"
        $maxGap = 0.0
        for ($i = 0; $i -lt $hostStates.Count; $i++) {
            $h = [regex]::Match($hostStates[$i], 'pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)')
            $c = [regex]::Match($clientStates[$i], 'pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)')
            if (!$h.Success -or !$c.Success) { $maxGap = [double]::PositiveInfinity; break }
            $sum = 0.0
            for ($axis = 1; $axis -le 3; $axis++) {
                $delta = [double]::Parse($h.Groups[$axis].Value, [cultureinfo]::InvariantCulture) - [double]::Parse($c.Groups[$axis].Value, [cultureinfo]::InvariantCulture)
                $sum += $delta * $delta
            }
            $maxGap = [math]::Max($maxGap, [math]::Sqrt($sum))
        }
        Check ($maxGap -le 1.0) "The joiner's porter follows the host within 1 world unit (maximum $([math]::Round($maxGap,2)))"
    }
    if ($ZombieWorkExperiment) {
        function Zombie([string]$Peer, [string]$Id) { ((((Probe $Peer 'zombies') -split "`n") | Where-Object { $_ -like "ZOMBIE $Id *" }) -join '').Trim() }
        function StationOf([string]$Peer, [string]$Id) { (((Probe $Peer 'stations') -split "`n") | Where-Object { $_ -like "STATION $Id *" }) -join '' }
        function StockOf([string]$Peer, [string]$Id) { (((Probe $Peer "station-stock|$Id") -split "`n") | Where-Object { $_ -like "STATION-STOCK $Id *" }) -join '' }
        $saw = '946ed737-481a-4dc1-8caf-105051deb51f'
        Probe Host "teleport-wgo|$saw" | Out-Null
        Start-Sleep -Seconds 5
        $work = (((Probe Host "zombie-work|$saw|flitch") -split "`n") | Where-Object { $_ -like 'ZOMBIE-WORK *' }) -join ''
        Check ($work -match '^ZOMBIE-WORK [0-9a-f-]{36} ') "The host can dock a zombie at the loaded sawhorse ($work)"
        $zid = ($work -split ' ')[1]
        Start-Sleep -Seconds 4
        Check ($work -match 'type=Crafter' -and (Zombie Client $zid) -match "type=Crafter .*at=$saw") "A zombie docked by the host appears at the joiner's sawhorse ($work; joiner $(Zombie Client $zid))"
        $stock = Probe Host "zombie-stock|$saw|wood|4"
        Check ($stock -match 'added=True') "The sawhorse has wood ready for the zombie ($stock)"
        Probe Host "craft|$saw|flitch" | Out-Null
        [IO.File]::WriteAllText((Join-Path $OutputPath 'zombie-diag.txt'), (Probe Host "zombie-diagnostics|$zid|$saw"))
        $log = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt 6; $i++) {
            Start-Sleep -Seconds 4
            $log.Add("t+$(4*($i+1))s host: $(StationOf Host $saw)")
            $log.Add("       joiner: $(StationOf Client $saw)")
            $log.Add("       zombie host:   $(Zombie Host $zid)")
            $log.Add("       zombie joiner: $(Zombie Client $zid)")
        }
        [IO.File]::WriteAllText((Join-Path $OutputPath 'zombie-work.txt'), ($log -join "`n"))
        $hostOutput = StockOf Host $saw
        $clientOutput = StockOf Client $saw
        [IO.File]::WriteAllText((Join-Path $OutputPath 'zombie-output.txt'), "host $hostOutput`njoiner $clientOutput")
        Check ($hostOutput -match 'flitchx[1-9]' -and $hostOutput -eq $clientOutput) "Zombie-made flitch reaches both station inventories exactly ($hostOutput; $clientOutput)"
        if ($ZombieOutputPickupExperiment) {
            $taken = (((Probe Client "station-take|$saw|flitch|1") -split "`n") | Where-Object { $_ -like 'STATION-TAKE *' }) -join ''
            Start-Sleep -Seconds 3
            $afterTakeHost = StockOf Host $saw
            $afterTakeClient = StockOf Client $saw
            [IO.File]::WriteAllText((Join-Path $OutputPath 'zombie-output-pickup.txt'), "take $taken`nhost $afterTakeHost`njoiner $afterTakeClient")
            Check ($taken -match 'player=.*flitchx1') "The joiner receives one flitch from the station ($taken)"
            Check ($afterTakeHost -match 'flitchx3' -and $afterTakeHost -eq $afterTakeClient) "The host and joiner retain three flitch after the joiner's pickup ($afterTakeHost; $afterTakeClient)"
            if ($ZombiePickupConflictExperiment) {
                Probe Host 'craft-broadcast-pause|8' | Out-Null
                $hostTake = (((Probe Host "station-take|$saw|flitch|3") -split "`n") | Where-Object { $_ -like 'STATION-TAKE *' }) -join ''
                $staleTake = (((Probe Client "station-take|$saw|flitch|1") -split "`n") | Where-Object { $_ -like 'STATION-TAKE *' }) -join ''
                Start-Sleep -Seconds 3
                $afterConflictPlayer = (((Probe Client 'inspect') -split "`n") | Where-Object { $_ -like 'PLAYER *' }) -join ''
                $afterConflictHost = StockOf Host $saw
                $afterConflictClient = StockOf Client $saw
                [IO.File]::WriteAllText((Join-Path $OutputPath 'zombie-pickup-conflict.txt'), "host $hostTake`nstale joiner $staleTake`nafter player $afterConflictPlayer`nhost $afterConflictHost`njoiner $afterConflictClient")
                Check ($staleTake -match 'player=.*flitchx2' -and $afterConflictPlayer -match 'items=.*flitchx1') "A stale joiner pickup is revoked after the host rejects it ($staleTake; $afterConflictPlayer)"
                Check ($afterConflictHost -eq $afterConflictClient -and $afterConflictHost -notmatch 'flitchx') "Both stations are empty after the host takes the remaining output ($afterConflictHost; $afterConflictClient)"
            }
        }
        $final = StationOf Host $saw
        Check ($final -match 'status=(Started|Finished|WaitingForOutputDrop)' -or ($log -join ' ') -match 'status=(Started|Finished|WaitingForOutputDrop)') "The host's zombie starts the station's craft by itself (see zombie-work.txt; now $final)"
        Check (($log -join ' ') -match 'joiner: STATION .*status=(Started|WaitingForWorkerPickUp|Finished)') "The joiner sees the zombie's craft progress (see zombie-work.txt)"
        Check ((Zombie Host $zid) -replace ' at=.*$','' -eq ((Zombie Client $zid) -replace ' at=.*$','')) "The joiner shows the working zombie as the host has it (host $(Zombie Host $zid); joiner $(Zombie Client $zid))"
        $chop = '5345460e-38a3-4330-b2e2-98400403cfb1'
        Probe Client "teleport-wgo|$chop" | Out-Null
        Start-Sleep -Seconds 5
        [IO.File]::WriteAllText((Join-Path $OutputPath 'chopping-recipes.txt'), (Probe Host "station-recipes|$chop"))
        $work2 = (((Probe Client "zombie-work|$chop|wood_wedge|3") -split "`n") | Where-Object { $_ -like 'ZOMBIE-WORK *' }) -join ''
        Check ($work2 -match '^ZOMBIE-WORK [0-9a-f-]{36} ') "The joiner can dock a zombie at the loaded chopping spot ($work2)"
        $zid2 = ($work2 -split ' ')[1]
        Start-Sleep -Seconds 5
        Check ((Zombie Host $zid2) -match "type=Crafter .*at=$chop") "A zombie docked by the joiner appears at the host's chopping spot ($work2; host $(Zombie Host $zid2))"
        $stock2 = Probe Host "zombie-stock|$chop|wood1|1"
        Check ($stock2 -match 'added=True') "The host's chopping spot has wood ready for the joiner's zombie ($stock2)"
        $queued2 = Probe Client "craft|$chop|wood_wedge"
        Check ($queued2 -match 'startStatus=OK') "The joiner requests wood wedges for their zombie ($queued2)"
        Start-Sleep -Seconds 8
        $hostChop = StationOf Host $chop
        $clientChop = StationOf Client $chop
        [IO.File]::WriteAllText((Join-Path $OutputPath 'joiner-zombie-work.txt'), "host $hostChop`njoiner $clientChop`nhost zombie $(Zombie Host $zid2)`njoiner zombie $(Zombie Client $zid2)`nhost diag $(Probe Host "zombie-diagnostics|$zid2|$chop")`njoiner diag $(Probe Client "zombie-diagnostics|$zid2|$chop")")
        Check ($hostChop -match 'status=(Started|WaitingForWorkerPickUp|Finished)') "The host runs the joiner-placed zombie's wood-wedge craft ($hostChop)"
        Check ($clientChop -match 'status=(Started|WaitingForWorkerPickUp|Finished)') "The joiner sees their zombie's wood-wedge craft ($clientChop)"
    }
    if ($ZombieExperiment) {
        function Zombie([string]$Peer, [string]$Id) { ((((Probe $Peer 'zombies') -split "`n") | Where-Object { $_ -like "ZOMBIE $Id *" }) -join '').Trim() }
        function Spot([string]$line) { if ($line -match 'pos=\(([-0-9.]+), ([-0-9.]+), ([-0-9.]+)\)') { return @([double]$Matches[1], [double]$Matches[3]) } return $null }
        $made = (((Probe Host 'zombie-make') -split "`n") | Where-Object { $_ -like 'ZOMBIE-MAKE *' }) -join ''
        $idA = ($made -split ' ')[1]
        Start-Sleep -Seconds 4
        Check ((Zombie Client $idA) -ne '') "A zombie raised on the host appears on the joiner ($made)"
        Start-Sleep -Seconds 8
        $h = Zombie Host $idA; $c = Zombie Client $idA
        $a = Spot $h; $b = Spot $c
        $gap = if ($a -and $b) { [Math]::Sqrt([Math]::Pow($a[0]-$b[0],2) + [Math]::Pow($a[1]-$b[1],2)) } else { 999 }
        Add-Content -LiteralPath (Join-Path $OutputPath 'zombies.txt') -Value "host   $h`njoiner $c`ngap $gap"
        Check ($gap -lt 1.0) "The joiner shows the host's zombie where the host has it (gap $([Math]::Round($gap,2)); host $h; joiner $c)"
        $madeB = (((Probe Client 'zombie-make') -split "`n") | Where-Object { $_ -like 'ZOMBIE-MAKE *' }) -join ''
        $idB = ($madeB -split ' ')[1]
        Start-Sleep -Seconds 4
        Check ((Zombie Host $idB) -ne '') "A zombie raised on the joiner appears on the host ($madeB)"
        Probe Client "zombie-pickup|$idA" | Out-Null
        Start-Sleep -Seconds 4
        Check ((Zombie Host $idA) -eq '' -and (Zombie Client $idA) -eq '') 'A zombie picked up on the joiner leaves the host''s world too'
        # Work: the joiner puts the remaining zombie on the shared sawhorse; the host runs it.
        $station = '946ed737-481a-4dc1-8caf-105051deb51f'
        $spawnSpot = Spot (Zombie Host $idB)
        $assign = (((Probe Client "zombie-assign|$idB|$station") -split "`n") | Where-Object { $_ -like 'ZOMBIE-ASSIGN *' }) -join ''
        Start-Sleep -Seconds 4
        Check ((Zombie Host $idB) -match "type=Crafter .*at=$station") "A zombie put to work on the joiner works the same station on the host ($assign; host $(Zombie Host $idB))"
        Probe Host 'give|wood|3' | Out-Null
        Probe Host "craft|$station|flitch" | Out-Null
        Start-Sleep -Seconds 15
        $hw = Zombie Host $idB; $cw = Zombie Client $idB
        $p = Spot $hw; $q = Spot $cw
        $moved = [Math]::Sqrt([Math]::Pow($p[0]-$spawnSpot[0],2) + [Math]::Pow($p[1]-$spawnSpot[1],2))
        $gapWork = [Math]::Sqrt([Math]::Pow($p[0]-$q[0],2) + [Math]::Pow($p[1]-$q[1],2))
        Add-Content -LiteralPath (Join-Path $OutputPath 'zombies.txt') -Value "working: host $hw`njoiner $cw`nmoved $moved gap $gapWork"
        Add-Content -LiteralPath (Join-Path $OutputPath 'zombies.txt') -Value ((Probe Host 'stations') -split "`n" | Where-Object { $_ -like "STATION $station *" })
        Check ($gapWork -lt 1.0) "The joiner shows the working zombie where the host has it (moved $([Math]::Round($moved,1)), gap $([Math]::Round($gapWork,2)))"
        $counts = "host $((((Probe Host 'zombies') -split "`n") | Where-Object { $_ -like 'ZOMBIES *' }) -join '') / joiner $((((Probe Client 'zombies') -split "`n") | Where-Object { $_ -like 'ZOMBIES *' }) -join '')"
        Check ($counts -match 'host ZOMBIES onScene=1 .* joiner ZOMBIES onScene=1 ') "Both have the same zombies in the world ($counts)"
    }
    if ($BuildExperiment) {
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $placedOnJoiner = Line Client 'build-place|chest' 'BUILD-PLACE'
        $idA = ($placedOnJoiner -split ' ')[2]
        Start-Sleep -Seconds 4
        Check ((Line Host "has|$idA" 'HAS') -match 'True$') "A building placed on the joiner appears on the host with the same id ($placedOnJoiner)"
        $placedOnHost = Line Host 'build-place|chest' 'BUILD-PLACE'
        $idB = ($placedOnHost -split ' ')[2]
        Start-Sleep -Seconds 4
        Check ((Line Client "has|$idB" 'HAS') -match 'True$') "A building placed on the host appears on the joiner ($placedOnHost)"
        Probe Client "build-remove|$idB" | Out-Null
        Start-Sleep -Seconds 4
        Check ((Line Host "has|$idB" 'HAS') -match 'False$') 'A building removed on the joiner is gone on the host too'
        Probe Host "container-add|$idA|berry|3" | Out-Null
        Start-Sleep -Seconds 4
        $chestHost = ((Probe Host "inv|$idA") -split "`n" | Where-Object { $_ -like '  *' }) -join ';'
        $chestClient = ((Probe Client "inv|$idA") -split "`n" | Where-Object { $_ -like '  *' }) -join ';'
        Check ($chestHost -match 'berry' -and $chestHost -eq $chestClient) "The new chest works as a shared chest ($chestHost)"
    }
    if ($WorldResExperiment) {
        function Res([string]$Peer, [string]$Type) { ((((Probe $Peer "resource|$Type") -split "`n") | Where-Object { $_ -like 'RESOURCE *' }) -join '') -replace '^RESOURCE ', '' }
        Probe Host 'setres|global_ppl|7' | Out-Null
        Probe Client 'setres|village_REP|15' | Out-Null
        Probe Client 'setres|money|777' | Out-Null
        Start-Sleep -Seconds 6
        Check ((Res Client 'global_ppl') -eq 'global_ppl=7') "The host's congregation change reaches the joiner ($(Res Client 'global_ppl'))"
        Check ((Res Host 'village_REP') -eq 'village_REP=15') "The joiner's reputation change reaches the host ($(Res Host 'village_REP'))"
        Check ((Res Host 'money') -ne 'money=777') "The joiner's money stays the joiner's ($(Res Host 'money') on the host)"
    }
    if ($SceneExperiment) {
        function Drawn([string]$Peer) { (((Probe $Peer 'remote-drawn') -split "`n") | Where-Object { $_ -like 'REMOTE-DRAWN *' }) -join '' }
        $before = Drawn Host
        Check ($before -match 'body=([1-9][0-9]*)/') "The host draws the joiner while both are in the same scene ($before)"
        $went = (((Probe Client 'teleport-wgo|scene:Prison') -split "`n") | Where-Object { $_ -like 'TELEPORT *' }) -join ''
        $deadline = (Get-Date).AddSeconds(60)
        do { Start-Sleep -Seconds 3; $clientAway = Drawn Client } while ((Get-Date) -lt $deadline -and $clientAway -notmatch 'scene=Prison')
        Start-Sleep -Seconds 3
        $hostAway = Drawn Host; $clientAway = Drawn Client
        Check ($clientAway -match 'scene=Prison' -and $hostAway -match 'body=0/') "A joiner who went into the prison is hidden on the host ($went; host $hostAway)"
        Check ($clientAway -match 'body=0/') "The joiner in the prison does not see the host standing in the open world ($clientAway)"
        $back = (((Probe Client 'teleport-wgo|a5d8e32d-a635-4c44-a836-1bf9e8d5fb12') -split "`n") | Where-Object { $_ -like 'TELEPORT *' }) -join ''
        $deadline = (Get-Date).AddSeconds(60)
        do { Start-Sleep -Seconds 3; $clientBack = Drawn Client } while ((Get-Date) -lt $deadline -and $clientBack -notmatch 'scene=RuinedTemple')
        Start-Sleep -Seconds 3
        $hostBack = Drawn Host; $clientBack = Drawn Client
        Check ($hostBack -match 'body=([1-9][0-9]*)/' -and $clientBack -match 'body=([1-9][0-9]*)/') "Back in the same scene, both see each other again ($back; host $hostBack; joiner $clientBack)"
    }
    if ($KnowledgeExperiment) {
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $learned = Line Client 'learn' 'LEARN'
        $techA = ($learned -split ' ')[1]
        Start-Sleep -Seconds 6
        $hostKnows = Line Host "knows|$techA" 'KNOWS'
        Check ($hostKnows -match 'tech=True crafts=True') "A tech learned by the joiner is known on the host, recipes included ($learned; host: $hostKnows)"
        $learnedB = Line Host "learn|$techA" 'LEARN'
        $techB = ($learnedB -split ' ')[1]
        Start-Sleep -Seconds 6
        $clientKnows = Line Client "knows|$techB" 'KNOWS'
        Check ($clientKnows -match 'tech=True crafts=True') "A tech learned by the host is known on the joiner ($learnedB; joiner: $clientKnows)"
        $h = Line Host "knows|$techB" 'KNOWS'; $c = Line Client "knows|$techA" 'KNOWS'
        Check ((($h -split ' ')[4..5] -join ' ') -eq (($c -split ' ')[4..5] -join ' ')) "Both players end with the same number of techs and recipes (host $h; joiner $c)"
    }
    if ($RichExperiment) {
        function Inv([string]$Peer, [string]$Id) { ((Probe $Peer "inv|$Id") -split "`n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -like 'INV *' -or $_ -like '  *' }) -join "`n" }
        $graveA = '9b9f7199-4781-49b8-a34b-42c4e33d5503'
        $graveB = '3eda2567-0c13-4ec0-a8fd-5cf41dc21a64'
        $log = New-Object System.Collections.Generic.List[string]
        $startA = Inv Host $graveA
        $log.Add("START A`n$startA")
        Check ($startA -match 'body_corpse' -and $startA -eq (Inv Client $graveA)) 'Both players see the same body, organs included, in the grave'
        $took = (((Probe Client "inv-nested-take|$graveA") -split "`n") | Where-Object { $_ -like 'INV-NESTED-TAKE *' }) -join ''
        Start-Sleep -Seconds 4
        $hostA = Inv Host $graveA; $clientA = Inv Client $graveA
        $log.Add("AFTER JOINER NESTED TAKE ($took)`nhost:`n$hostA`nclient:`n$clientA")
        Check ($hostA -eq $clientA -and $hostA -ne $startA -and $hostA -match 'body_corpse') "Taking a part out of a body on the joiner changes the host's body the same way ($took)"
        $tookB = (((Probe Host "inv-nested-take|$graveB") -split "`n") | Where-Object { $_ -like 'INV-NESTED-TAKE *' }) -join ''
        Start-Sleep -Seconds 4
        $hostB = Inv Host $graveB; $clientB = Inv Client $graveB
        $log.Add("AFTER HOST NESTED TAKE ($tookB)`nhost:`n$hostB`nclient:`n$clientB")
        Check ($hostB -eq $clientB -and $clientB -match 'body_corpse') "Taking a part out of a body on the host changes the joiner's body the same way ($tookB)"
        $taken = (((Probe Client "inv-take|$graveA|body_corpse") -split "`n") | Where-Object { $_ -like 'INV-TAKE *' }) -join ''
        Start-Sleep -Seconds 4
        $hostA2 = Inv Host $graveA; $clientA2 = Inv Client $graveA
        $log.Add("AFTER JOINER TAKES BODY ($taken)`nhost:`n$hostA2`nclient:`n$clientA2")
        Check ($hostA2 -eq $clientA2 -and $hostA2 -notmatch 'body_corpse') "A body taken out of a grave on the joiner leaves the host's grave too ($taken)"
        # Changing a grave's decoration goes through the grave's craft, as the grave window does it.
        $beforeDeco = Inv Host $graveB
        $start = (((Probe Client "start-craft|$graveB|rem_grave_top_wd_0") -split "`n") | Where-Object { $_ -like 'START-CRAFT *' }) -join ''
        Probe Client "finish|$graveB|rem_grave_top_wd_0" | Out-Null
        Start-Sleep -Seconds 4
        $hostDeco = Inv Host $graveB; $clientDeco = Inv Client $graveB
        $log.Add("AFTER JOINER REMOVES HEADSTONE ($start)`nbefore:`n$beforeDeco`nhost:`n$hostDeco`nclient:`n$clientDeco")
        Check ($hostDeco -eq $clientDeco -and $clientDeco -ne $beforeDeco) "A headstone taken off a grave on the joiner is gone from the host's grave too ($start)"
        $plain = Inv Host '31dfe17a-4422-4c8b-a0ce-051b938b7e09'
        Check ($plain -eq (Inv Client '31dfe17a-4422-4c8b-a0ce-051b938b7e09')) 'Ordinary chests still agree'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'rich.txt'), ($log -join "`n`n"))
    }
    if ($VendorExperiment) {
        function Shops([string]$Peer) { ((Probe $Peer 'vendors') -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like 'VENDOR *' }) -join "`n" }
        $start = Shops Host
        Check ($start -ne '' -and $start -eq (Shops Client)) 'Both players start with the same shops'
        $vendor = (($start -split "`n") | Where-Object { $_ -notmatch 'stock=$' } | Select-Object -First 1) -replace '^VENDOR (\S+) .*$', '$1'
        $deal = (((Probe Client "vendor-deal|$vendor") -split "`n") | Where-Object { $_ -like 'DEAL *' }) -join ''
        Start-Sleep -Seconds 5
        $afterClientDeal = Shops Host
        Check ($afterClientDeal -eq (Shops Client) -and $afterClientDeal -ne $start) "A deal on the joiner changes the host's shop the same way ($deal)"
        $deal2 = (((Probe Host "vendor-deal|$vendor") -split "`n") | Where-Object { $_ -like 'DEAL *' }) -join ''
        Start-Sleep -Seconds 5
        $afterHostDeal = Shops Client
        [IO.File]::WriteAllText((Join-Path $OutputPath 'vendors.txt'), "START`n$start`nAFTER JOINER DEAL`n$afterClientDeal`nAFTER HOST DEAL`n$afterHostDeal")
        Check ($afterHostDeal -eq (Shops Host) -and $afterHostDeal -ne $afterClientDeal) "A deal on the host changes the joiner's shop the same way ($deal2)"
    }
    if ($SleepExperiment) {
        function Speed([string]$Peer) { (((Probe $Peer 'speed') -split "`n") | Where-Object { $_ -like 'SPEED *' }) -join '' }
        Probe Client 'sleep' | Out-Null
        Start-Sleep -Seconds 6
        $c1 = Speed Client; $h1 = Speed Host
        Add-Content -LiteralPath (Join-Path $OutputPath 'sleep.txt') -Value "joiner asleep: client $c1 / host $h1"
        Check ($c1 -match '^SPEED 1 asleep=True' -and $h1 -match '^SPEED 1 ') "A joiner sleeping alone sleeps at normal speed, and the host's time is unchanged (client $c1, host $h1)"
        Probe Host 'sleep' | Out-Null
        Start-Sleep -Seconds 6
        $c2 = Speed Client; $h2 = Speed Host
        Add-Content -LiteralPath (Join-Path $OutputPath 'sleep.txt') -Value "both asleep: client $c2 / host $h2"
        Check ($h2 -match '^SPEED 50 asleep=True' -and $c2 -match '^SPEED 50 asleep=True') "With both asleep the night passes at the game's speed on both machines (host $h2, client $c2)"
        Check ((Select-String -LiteralPath $hostLog -Pattern 'everyone asleep' -Quiet)) 'The host saw that everyone was asleep'
        # The joiner went to bed first: when the host's night is over, the joiner is up too (0.63
        # and older: the joiner lay there at normal speed until its own bar had filled).
        $deadline = (Get-Date).AddSeconds(120)
        do { Start-Sleep -Seconds 2; $h3 = Speed Host } while ($h3 -match 'asleep=True' -and (Get-Date) -lt $deadline)
        $hostUp = Get-Date
        do { Start-Sleep -Seconds 1; $c3 = Speed Client } while ($c3 -match 'asleep=True' -and ((Get-Date) - $hostUp).TotalSeconds -lt 30)
        $late = [int]((Get-Date) - $hostUp).TotalSeconds
        Add-Content -LiteralPath (Join-Path $OutputPath 'sleep.txt') -Value "host up: $h3 / client $late s later: $c3"
        Check ($h3 -match 'asleep=False' -and $c3 -match '^SPEED 1 asleep=False' -and $late -le 12) "When the night is over the joiner wakes with the host (host $h3; client after $late s: $c3)"

        # The host's setting "The night passes when: I sleep": the host lies down alone, the joiner
        # stays up. The host's world runs the night; the joiner keeps walking at normal speed (the
        # night's speed would also run its walking) and its clock follows the host's.
        # Days and time of day as one number: at the night's speed several days can pass.
        function Clock([string]$Peer) { $l = (((Probe $Peer 'time') -split "`n") | Where-Object { $_ -like 'TIME *' }) -join ''; $p = $l -split ' '; [double]::Parse($p[1], [Globalization.CultureInfo]::InvariantCulture) + [int]($p[2] -replace 'day=', '') }
        function Spot([string]$Peer) { ((Probe $Peer 'where') -split "`n" | Where-Object { $_ -like 'WHERE *' }) -join '' }
        $rule = (((Probe Host 'night-rule|Host') -split "`n") | Where-Object { $_ -like 'NIGHT-RULE *' }) -join ''
        $told = @(Select-String -LiteralPath $clientLog -Pattern 'Told the player:').Count
        # Walking first without a night, to know this spot lets the joiner walk (left, into the room).
        $spot0 = Spot Client
        Probe Client 'stick|-1|0|1' | Out-Null
        Start-Sleep -Seconds 2
        $spotA = Spot Client
        $before = Clock Client
        Probe Host 'sleep' | Out-Null
        Start-Sleep -Seconds 8
        $h4 = Speed Host; $c4 = Speed Client
        $spot1 = Spot Client
        Probe Client 'stick|1|0|1' | Out-Null
        Start-Sleep -Seconds 3
        $spot2 = Spot Client
        $after = Clock Client
        $jump = $after - $before
        $news = @(Select-String -LiteralPath $clientLog -Pattern 'Told the player:' | Select-Object -Skip $told | ForEach-Object { $_.Line })
        Add-Content -LiteralPath (Join-Path $OutputPath 'sleep.txt') -Value "rule $rule; host alone asleep: host $h4 / client $c4; joiner clock $before -> $after (+$jump); walked $spot1 -> $spot2; told: $($news -join ' | ')"
        Check ($h4 -match '^SPEED 50 asleep=True' -and $c4 -match '^SPEED 1 asleep=False') "With 'I sleep' the host's sleep runs the night while the joiner stays up at normal speed (host $h4, client $c4)"
        Check ($spot0 -ne $spotA -and $spot1 -and $spot2 -and $spot1 -ne $spot2) "The joiner who stayed up keeps walking during the host's night (before the night $spot0 -> $spotA; during it $spot1 -> $spot2)"
        Check ($jump -gt 0.03) "The joiner's clock jumps ahead with the host's night (+$([Math]::Round($jump, 3)) days)"
        Check ($news.Count -ge 1 -and (Select-String -LiteralPath $hostLog -Pattern 'NightPasses=Host' -Quiet)) "The joiner is told the host's sleep passes the night ($($news -join ' | '))"
        $deadline = (Get-Date).AddSeconds(90)
        do { Start-Sleep -Seconds 2; $h5 = Speed Host } while ($h5 -match 'asleep=True' -and (Get-Date) -lt $deadline)
        Start-Sleep -Seconds 3
        $c5 = Speed Client
        Probe Host 'night-rule|Everyone' | Out-Null
        Check ($h5 -match '^SPEED 1 asleep=False' -and $c5 -match '^SPEED 1 asleep=False') "When the host is up again both run at normal speed (host $h5, client $c5)"
    }
    if ($CraftEndExperiment) {
        function Obj([string]$Peer, [string]$Id) {
            $line = ((Probe $Peer 'objects|') -split "`n" | Where-Object { $_ -like "OBJ $Id *" } | ForEach-Object { $_.Trim() }) -join ''
            if ($line) { $line } else { "OBJ $Id (gone)" }
        }
        $pick = $null
        foreach ($line in ((Probe Client 'stations') -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -match ' type=Craft shared=False ' -and $_ -notmatch 'garden' })) {
            $null = $line -match '^STATION (\S+) (\S+) '
            $id = $Matches[1]
            $recipe = ((Probe Client "craft-needs|$id") -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^RECIPE (\S+) auto=\S+ replace=(\S+) ' } | Select-Object -First 1)
            if ($recipe -and $recipe -match '^RECIPE (\S+) auto=\S+ replace=(\S+) ') { $pick = @{ Id = $id; Recipe = $Matches[1]; Into = $Matches[2]; Line = $line }; break }
        }
        if (-not $pick) { throw 'No world-changing recipe found on an unshared craft object.' }
        $beforeHost = Obj Host $pick.Id; $beforeClient = Obj Client $pick.Id
        $finish = Probe Client "finish|$($pick.Id)|$($pick.Recipe)"
        Start-Sleep -Seconds 6
        $afterHost = Obj Host $pick.Id; $afterClient = Obj Client $pick.Id
        [IO.File]::WriteAllText((Join-Path $OutputPath 'craft-end.txt'), "$($pick.Line)`nrecipe $($pick.Recipe) -> $($pick.Into)`n$finish`nbefore host:   $beforeHost`nbefore client: $beforeClient`nafter host:    $afterHost`nafter client:  $afterClient")
        Check ($afterClient -ne $beforeClient) "The joiner's finished $($pick.Recipe) changed its own object ($afterClient)"
        Check ($afterHost -eq $afterClient) "The host's object changes the same way ($afterHost)"
    }
    if ($GardenExperiment) {
        # Each machine grows its own beds; a finished crop replaces the bed object. With stable
        # replacement ids both machines must end with the same objects under the same ids.
        function Growing([string]$Peer) {
            @((Probe $Peer 'stations') -split "`n" | Where-Object { $_ -match ' garden_(wheat|carrot) .*status=Started' }).Count
        }
        function Objects([string]$Peer) { ((Probe $Peer 'objects|garden') -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }) -join "`n" }
        [IO.File]::WriteAllText((Join-Path $OutputPath 'garden-objects-start.txt'), "HOST`n$(Objects Host)`nCLIENT`n$(Objects Client)")
        $deadline = (Get-Date).AddMinutes(8)
        while ((Get-Date) -lt $deadline -and ((Growing Host) -gt 0 -or (Growing Client) -gt 0)) { Start-Sleep -Seconds 15 }
        Start-Sleep -Seconds 5
        $hostObjects = Objects Host; $clientObjects = Objects Client
        $hostDrops = ((Probe Host 'inspect') -split "`n" | Where-Object { $_ -like 'DROP *' } | ForEach-Object { $_.Trim() } | Sort-Object) -join "`n"
        $clientDrops = ((Probe Client 'inspect') -split "`n" | Where-Object { $_ -like 'DROP *' } | ForEach-Object { $_.Trim() } | Sort-Object) -join "`n"
        [IO.File]::WriteAllText((Join-Path $OutputPath 'garden-objects-end.txt'), "HOST`n$hostObjects`nCLIENT`n$clientObjects`nHOST DROPS`n$hostDrops`nCLIENT DROPS`n$clientDrops")
        Check ((Growing Host) -eq 0 -and (Growing Client) -eq 0) 'Every crop in the save finishes on both machines within eight minutes'
        Check ($hostObjects -ne '' -and $hostObjects -eq $clientObjects) 'Finished crops leave the same objects with the same ids on both machines (see garden-objects-end.txt)'
        Check ($hostDrops -eq $clientDrops) 'Drops agree on both machines after the crops finish'

        # Harvest on the joiner (the game's death path), then plant on the emptied bed.
        $ready = @($clientObjects -split "`n" | Where-Object { $_ -match '_ready$' })
        if ($ready.Count -gt 0) {
            $null = $ready[0] -match '^OBJ (\S+) '
            $bed = $Matches[1]
            $kill = Probe Client "wgo-kill|$bed"
            Start-Sleep -Seconds 8
            $hostAfter = Objects Host; $clientAfter = Objects Client
            $hostDrops2 = ((Probe Host 'inspect') -split "`n" | Where-Object { $_ -like 'DROP *' } | ForEach-Object { $_.Trim() } | Sort-Object) -join "`n"
            $clientDrops2 = ((Probe Client 'inspect') -split "`n" | Where-Object { $_ -like 'DROP *' } | ForEach-Object { $_.Trim() } | Sort-Object) -join "`n"
            [IO.File]::WriteAllText((Join-Path $OutputPath 'garden-harvest.txt'), "$kill`nHOST`n$hostAfter`nCLIENT`n$clientAfter`nHOST DROPS`n$hostDrops2`nCLIENT DROPS`n$clientDrops2")
            Check ($hostAfter -eq $clientAfter -and $hostDrops2 -eq $clientDrops2) "Harvesting a ready bed on the joiner changes the host's garden the same way ($kill)"

            $empty = @($clientAfter -split "`n" | Where-Object { $_ -match 'garden_empty$' })
            if ($empty.Count -gt 0) {
                $null = $empty[0] -match '^OBJ (\S+) '
                $emptyBed = $Matches[1]
                # Fertiliser goes on an empty bed, before the seed.
                $fert = (((Probe Client "fertilize|$emptyBed") -split "`n") | Where-Object { $_ -like 'FERTILIZE *' }) -join ''
                Start-Sleep -Seconds 5
                $hostPerks = (((Probe Host "bed-perks|$emptyBed") -split "`n") | Where-Object { $_ -like 'PERKS *' }) -join ''
                $clientPerks = (((Probe Client "bed-perks|$emptyBed") -split "`n") | Where-Object { $_ -like 'PERKS *' }) -join ''
                Add-Content -LiteralPath (Join-Path $OutputPath 'garden-harvest.txt') -Value "$fert`nHOST $hostPerks`nCLIENT $clientPerks"
                Check ($fert -match 'ok=True' -and $clientPerks -ne 'PERKS ' -and $hostPerks -eq $clientPerks) "Fertilising on the joiner gives the host's bed the same perk and slot ($fert; $clientPerks)"
                $result = Probe Client "plant|$emptyBed|auto"
                $planted = if ($result -match 'ok=True') { ($result -split "`n" | Where-Object { $_ -like 'PLANT *' }) -join '' } else { $null }
                Start-Sleep -Seconds 8
                $hostPlant = ((Probe Host 'stations') -split "`n" | Where-Object { $_ -like "STATION $emptyBed *" } | ForEach-Object { ($_.Trim()) -replace ' startable=\S+', '' -replace '@[0-9,.]+', '' }) -join ''
                $clientPlant = ((Probe Client 'stations') -split "`n" | Where-Object { $_ -like "STATION $emptyBed *" } | ForEach-Object { ($_.Trim()) -replace ' startable=\S+', '' -replace '@[0-9,.]+', '' }) -join ''
                Add-Content -LiteralPath (Join-Path $OutputPath 'garden-harvest.txt') -Value "PLANT $planted`nHOST $hostPlant`nCLIENT $clientPlant"
                Check ($planted -and $clientPlant -match 'status=Started' -and $hostPlant -eq $clientPlant) "Planting on the joiner starts the same crop on the host ($planted)"
            }
            # The other way: the host harvests a ripe bed and plants it; it grows on the joiner too
            # (0.63 and older: the joiner's game refused the mirrored planting when that player had
            # no shovel of their own, and the host's new field stayed empty on the joiner's screen).
            $ripe = @((Objects Host) -split "`n" | Where-Object { $_ -match '_ready$' })
            if ($ripe.Count -gt 0) {
                $null = $ripe[0] -match '^OBJ (\S+) '
                Probe Host "wgo-kill|$($Matches[1])" | Out-Null
                Start-Sleep -Seconds 6
                $hostEmpty = @((Objects Host) -split "`n" | Where-Object { $_ -match 'garden_empty$' } | ForEach-Object { $null = $_ -match '^OBJ (\S+) '; $Matches[1] })
                $bed2 = $hostEmpty | Where-Object { $_ -ne $emptyBed } | Select-Object -First 1
                if ($bed2) {
                    $result2 = Probe Host "plant|$bed2|auto"
                    Start-Sleep -Seconds 8
                    $hostPlant2 = ((Probe Host 'stations') -split "`n" | Where-Object { $_ -like "STATION $bed2 *" } | ForEach-Object { ($_.Trim()) -replace ' startable=\S+', '' -replace '@[0-9,.]+', '' }) -join ''
                    $clientPlant2 = ((Probe Client 'stations') -split "`n" | Where-Object { $_ -like "STATION $bed2 *" } | ForEach-Object { ($_.Trim()) -replace ' startable=\S+', '' -replace '@[0-9,.]+', '' }) -join ''
                    Add-Content -LiteralPath (Join-Path $OutputPath 'garden-harvest.txt') -Value "HOST PLANTS $result2`nHOST $hostPlant2`nCLIENT $clientPlant2"
                    Check ($result2 -match 'ok=True' -and $clientPlant2 -match 'status=Started' -and $hostPlant2 -eq $clientPlant2) "Planting on the host starts the same crop on the joiner ($clientPlant2)"
                }
            }
        }
    }
    if ($CraftExperiment) {
        function StationLine([string]$Peer, [string]$Id) {
            (((Probe $Peer 'stations') -split "`n" | Where-Object { $_ -like "STATION $Id *" }) -join '') -replace ' startable=\S+', ''
        }
        $clientStations = Probe Client 'stations'
        [IO.File]::WriteAllText((Join-Path $OutputPath 'stations-before-client.txt'), $clientStations)
        [IO.File]::WriteAllText((Join-Path $OutputPath 'stations-before-host.txt'), (Probe Host 'stations'))
        # Stations the mod shares (host-run), as the mod itself reports them.
        $candidates = @($clientStations -split "`n" | Where-Object { $_ -match 'startable=(?!none)' -and $_ -match ' shared=True ' })
        if ($candidates.Count -lt 2) { throw "Need two idle production stations with a startable craft; found $($candidates.Count)." }
        $null = $candidates[0] -match '^STATION (\S+) (\S+) .*startable=(\S+)'
        $station = $Matches[1]; $craftId = $Matches[3]
        $null = $candidates[1] -match '^STATION (\S+) (\S+) .*startable=(\S+)'
        $hostStation = $Matches[1]; $hostCraft = $Matches[3]

        $craftResult = Probe Client "craft|$station|$craftId"
        [IO.File]::WriteAllText((Join-Path $OutputPath 'craft-client.txt'), $craftResult)
        Start-Sleep -Seconds 5
        $h = StationLine Host $station
        $c = StationLine Client $station
        Add-Content -LiteralPath (Join-Path $OutputPath 'craft-observations.txt') -Value "joiner craft +5 s`n  host:   $h`n  client: $c"
        Check ($h -notmatch 'queue=0 cur=-') "A joiner's craft is queued on the host's station ($h)"
        Check ($h -eq $c) "Joiner and host show the same station state after the joiner's craft ($c)"

        Probe Host "craft|$hostStation|$hostCraft" | Out-Null
        Start-Sleep -Seconds 5
        $h2 = StationLine Host $hostStation
        $c2 = StationLine Client $hostStation
        Add-Content -LiteralPath (Join-Path $OutputPath 'craft-observations.txt') -Value "host craft +5 s`n  host:   $h2`n  client: $c2"
        Check ($h2 -notmatch 'queue=0 cur=-' -and $h2 -eq $c2) "A host's craft appears on the joiner's station ($c2)"

        Start-Sleep -Seconds 20
        $h3 = StationLine Host $station
        $c3 = StationLine Client $station
        $hostChests = ((Probe Host 'inspect') -split "`n" | Where-Object { $_ -like 'CONTAINER *' }) -join "`n"
        $clientChests = ((Probe Client 'inspect') -split "`n" | Where-Object { $_ -like 'CONTAINER *' }) -join "`n"
        Add-Content -LiteralPath (Join-Path $OutputPath 'craft-observations.txt') -Value "+25 s`n  host:   $h3`n  client: $c3`nchests equal: $($hostChests -eq $clientChests)"
        Check ($h3 -eq $c3) "The joiner keeps mirroring the station while the host runs it ($c3)"
        Check ($hostChests -eq $clientChests) 'Chests stay identical on both peers after crafting'

        # Ingredients: a manual recipe at a real workbench, supplied only by the joiner, started by
        # the joiner working the station. The ingredients must leave the world exactly once.
        $recipe = $null; $bench = $null
        foreach ($line in ($clientStations -split "`n" | Where-Object { $_ -match ' shared=True ' -and $_ -match 'status=(None|Finished)' -and $_ -notmatch 'conveyor' })) {
            $null = $line -match '^STATION (\S+) (\S+) '
            $candidate = $Matches[1]
            foreach ($r in ((Probe Client "craft-needs|$candidate") -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like 'RECIPE *' })) {
                if ($r -match '^RECIPE (\S+) auto=False replace=\S* ?needs=(\S+)$' -and $Matches[1] -notmatch '^quest' -and $Matches[2] -notmatch ':(?!None)[A-Za-z]+(,|$)') {
                    $recipe = $Matches[1]; $needs = $Matches[2]; $bench = $candidate; break
                }
            }
            if ($recipe) { break }
        }
        if (-not $recipe) { throw 'No manual workbench recipe with plain ingredients found.' }
        $needList = @($needs -split ',' | ForEach-Object { $p = $_ -split ':'; [pscustomobject]@{ Id = $p[0]; Count = [int]$p[1] } })
        $totalsBefore = @{}
        $hostOwnBefore = @{}
        foreach ($n in $needList) {
            $null = (Probe Host "count|$($n.Id)") -match 'player=(\d+)'
            $hostOwnBefore[$n.Id] = [int]$Matches[1]
            $null = (Probe Client "count|$($n.Id)") -match 'player=(\d+) containers=(\d+)'
            $totalsBefore[$n.Id] = [int]$Matches[1] + [int]$Matches[2]
            Probe Client "give|$($n.Id)|$($n.Count)" | Out-Null
        }
        Start-Sleep -Seconds 1
        Probe Client "craft|$bench|$recipe" | Out-Null
        Start-Sleep -Seconds 3
        $work = Probe Client "work|$bench|3"
        Start-Sleep -Seconds 6
        $hb = StationLine Host $bench
        $cb = StationLine Client $bench
        $lines = @("ingredients: $recipe at $bench needs $needs", "  work on joiner: $work", "  host:   $hb", "  client: $cb")
        $exactlyOnce = $true
        foreach ($n in $needList) {
            $null = (Probe Client "count|$($n.Id)") -match 'player=(\d+) containers=(\d+)'
            $clientTotal = [int]$Matches[1] + [int]$Matches[2]; $clientContainers = [int]$Matches[2]
            $null = (Probe Host "count|$($n.Id)") -match 'player=(\d+) containers=(\d+)'
            $hostContainers = [int]$Matches[2]; $hostOwn = [int]$Matches[1]
            $expected = $totalsBefore[$n.Id]
            $lines += "  $($n.Id): before=$($totalsBefore[$n.Id]) +given $($n.Count) -used $($n.Count) => joiner+chests=$clientTotal (expected $expected); chests host=$hostContainers joiner=$clientContainers; host's own $($hostOwnBefore[$n.Id]) -> $hostOwn"
            if ($clientTotal -ne $expected -or $hostContainers -ne $clientContainers -or $hostOwn -ne $hostOwnBefore[$n.Id]) { $exactlyOnce = $false }
        }
        Add-Content -LiteralPath (Join-Path $OutputPath 'craft-observations.txt') -Value ($lines -join "`n")
        Check ($hb -notmatch 'status=(None|Finished|ReadyToStartCraft)') "Working the station on the joiner starts the joiner's craft on the host ($hb)"
        Check $exactlyOnce "The craft's ingredients leave the world exactly once and chests agree (see craft-observations.txt)"

        # Tools: a recipe worked with a tool, the joiner carrying it and the host not. The host runs
        # the joiner's work as the joiner, so the joiner's tool belt must count, not the host's.
        function Line([string]$Peer, [string]$Command, [string]$Prefix) { (((Probe $Peer $Command) -split "`n") | Where-Object { $_ -like "$Prefix *" }) -join '' }
        $toolRecipe = $null; $toolBench = $null; $toolType = $null; $toolNeeds = $null
        foreach ($line in ($clientStations -split "`n" | Where-Object { $_ -match ' shared=True ' -and $_ -match 'status=(None|Finished)' -and $_ -notmatch 'conveyor' })) {
            $null = $line -match '^STATION (\S+) (\S+) '
            $candidate = $Matches[1]
            if ($candidate -eq $bench) { continue }
            foreach ($r in ((Probe Client "craft-tools|$candidate") -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like 'RECIPE *' })) {
                if ($r -match '^RECIPE (\S+) auto=False tool=(\S+) needs=(\S*)$' -and $Matches[2] -notmatch '^(None|Hand)$' -and $Matches[1] -notmatch '^quest' -and $Matches[3] -notmatch ':(?!None)[A-Za-z]+(,|$)') {
                    $toolRecipe = $Matches[1]; $toolType = $Matches[2]; $toolNeeds = $Matches[3]; $toolBench = $candidate; break
                }
            }
            if ($toolRecipe) { break }
        }
        if (-not $toolRecipe) { Note $false 'No station recipe worked with a tool found for the tool check' }
        else {
            $dropped = Line Host "belt-drop|$toolType" 'BELT-DROP'
            $given = Line Client "belt-give|$toolType" 'BELT-GIVE'
            foreach ($n in @($toolNeeds -split ',' | Where-Object { $_ })) { $p = $n -split ':'; Probe Client "give|$($p[0])|$($p[1])" | Out-Null }
            Start-Sleep -Seconds 1
            Probe Client "craft|$toolBench|$toolRecipe" | Out-Null
            Start-Sleep -Seconds 3
            $toolWork = Probe Client "work|$toolBench|3"
            Start-Sleep -Seconds 6
            $ht = StationLine Host $toolBench
            $ct = StationLine Client $toolBench
            Add-Content -LiteralPath (Join-Path $OutputPath 'craft-observations.txt') -Value "tool: $toolRecipe at $toolBench worked with $toolType; host $dropped; joiner $given`n  work on joiner: $toolWork`n  host:   $ht`n  client: $ct"
            Check ($ht -notmatch 'status=(None|Finished|ReadyToStartCraft|DoesntHaveRequiredTool)') "A joiner carrying the tool starts a $toolType craft on the host, although the host has no $toolType ($ht)"
        }
        Get-Content -LiteralPath (Join-Path $OutputPath 'craft-observations.txt') | Write-Host
    }
}
finally {
    [Environment]::SetEnvironmentVariable('GK2COOP_TEST_TIDY', $null)
    [Environment]::SetEnvironmentVariable('GK2COOP_TEST_SAVE_FOLDER', $null)
    Remove-Item Env:\GK2COOP_TEST_LANG,Env:\GK2COOP_TEST_CONTINUE_SLOT,Env:\GK2COOP_TEST_MENU_CONNECT,Env:\GK2COOP_TEST_HOST_PATH,Env:\GK2COOP_TEST_CLIENT_PATH,Env:\GK2COOP_TEST_THIRD_PATH,Env:\GK2COOP_TEST_QUIET,Env:\GK2COOP_TEST_STEAM_LOCALIP -ErrorAction SilentlyContinue
    foreach ($peer in $peers) {
        if ($peer.Process -and -not $peer.Process.HasExited) { $peer.Process.Kill(); $peer.Process.WaitForExit(10000) | Out-Null }
    }
    foreach ($peer in $peers) {
        $log = Join-Path $peer.Path 'BepInEx\LogOutput.log'
        if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath "$($peer.Name)-log.txt") -Force }
        if (Test-Path -LiteralPath (ProbePath $peer)) { Move-Item -LiteralPath (ProbePath $peer) -Destination (Join-Path $OutputPath "$($peer.Name)-GameplayProbe.dll") -Force }
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $peer.Path 'BepInEx') -Filter 'GK2Coop.Probe.command*' -ErrorAction SilentlyContinue) {
            Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath "$($peer.Name)-$($file.Name)") -Force
        }
        $savedConfig = Join-Path $OutputPath "$($peer.Name)-config-before.cfg"
        Copy-Item -LiteralPath $savedConfig -Destination (ConfigPath $peer) -Force
        Write-Host "$($peer.Name) config restored: $((Get-FileHash (ConfigPath $peer)).Hash -eq (Get-FileHash $savedConfig).Hash)"
    }
    if ($ThirdPath) {
        # The third player's save folder is this run's alone: its world copies go with the run.
        $thirdSaves = "$saveFolder-third"
        foreach ($file in Get-ChildItem -LiteralPath $thirdSaves -Filter 'GK2Coop_*' -File -ErrorAction SilentlyContinue) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath "Third-$($file.Name)") -Force }
    }
    $profileRoot = Join-Path $HostPath "BepInEx\config\GK2Coop\players\$slot"
    if (Test-Path -LiteralPath $profileRoot) { Move-Item -LiteralPath $profileRoot -Destination (Join-Path $OutputPath 'host-player-profiles') -Force }
    $savedManifest = Join-Path $OutputPath 'manifest-before.json'
    if (Test-Path -LiteralPath $savedManifest) { Copy-Item -LiteralPath $savedManifest -Destination $manifest -Force }
    foreach ($suffix in @('.dat','.info','_backup_1.dat','_backup_1.info','_backup_2.dat','_backup_2.info','_backup_3.dat','_backup_3.info')) {
        $file = Join-Path $saveFolder ($slot + $suffix)
        if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination (Join-Path $OutputPath ($slot + $suffix)) -Force; Remove-Item -LiteralPath $file -Force }
    }
    # The joiner's copies of the host's world, as its own log names them (not "whatever is new in
    # the folder": a run in parallel has its own).
    $clientLogNow = Join-Path $ClientPath 'BepInEx\LogOutput.log'
    $imported = @()
    if (Test-Path -LiteralPath $clientLogNow) { $imported = @(Select-String -LiteralPath $clientLogNow -Pattern "isolated slot '(GK2Coop_[0-9a-f]{16})'" | ForEach-Object { $_.Matches[0].Groups[1].Value } | Select-Object -Unique) }
    foreach ($generated in Get-ChildItem -LiteralPath $saveFolder -Filter 'GK2Coop_*.dat' -File -ErrorAction SilentlyContinue) {
        if ($generated.BaseName -notin $imported) { continue }
        foreach ($extension in @('.dat','.info')) {
            $path = Join-Path $saveFolder ($generated.BaseName + $extension)
            if (Test-Path -LiteralPath $path) { Move-Item -LiteralPath $path -Destination (Join-Path $OutputPath ($generated.BaseName + $extension)) -Force }
        }
    }
    $tidiedCopies = Join-Path $saveFolder 'GK2Coop\world-copies'
    foreach ($file in Get-ChildItem -LiteralPath $tidiedCopies -Filter 'GK2Coop_00000000000000aa*' -File -ErrorAction SilentlyContinue) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath $file.Name) -Force }
    foreach ($ext in '.dat','.info') { $left = Join-Path $saveFolder ('GK2Coop_00000000000000aa' + $ext); if (Test-Path -LiteralPath $left) { Move-Item -LiteralPath $left -Destination (Join-Path $OutputPath ('GK2Coop_00000000000000aa' + $ext)) -Force } }
    if (-not $SkipPrefs) { Write-Host "Game preferences restored: $(Restore-GamePrefs $savedPrefs) value(s)" }
    # The player's own game may save while a test runs, so a change here is reported with its
    # time, not treated as a failure; the test itself only ever writes GK2Coop_ slots.
    foreach ($file in Get-ChildItem -LiteralPath $realSaveFolder -File | Where-Object { $_.Name -notlike 'GK2Coop_*' -and $_.Extension -in '.dat','.info' }) {
        $before = $saveHashes[$file.Name]
        if ($before -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { Write-Host "Save changed during the test: $($file.Name) (written $($file.LastWriteTime))" }
    }
}
