<#
.SYNOPSIS
    Co-op across a real network: the host on this PC (D:\GK2Coop-FullHost, hidden, its own save
    folder), the joiner in the test VM (C:\GK2Coop\Game, visible there), joining from the menu as a
    friend would, over the Hyper-V network. Driven through the guest agent (tests\vm\GuestAgent.ps1).

.DESCRIPTION
    Checks: the joiner copies the host's world and joins; chest edits travel both ways; the host's
    fight shows in the VM. The player's own saves are hashed before and after; the host copy's
    config, the probe and the test slot are put back as by the other tests.
#>
[CmdletBinding()]
param(
    [string]$HostPath = 'D:\GK2Coop-FullHost',
    [string]$SaveBackup = 'C:\FF\graveyard-keeper-2-coop\artifacts\save-backups\full-release-20260923-071217',
    [string]$OutputPath = ('D:\GK2Coop-Artifacts\vm-coop-' + (Get-Date -Format 'yyyyMMdd-HHmm')),
    [switch]$NoFight,
    # A long session: this many minutes of chest edits both ways and walking, checked every half minute.
    [int]$SoakMinutes = 0,
    # Leave and come back: the VM's game quits and joins again; its player is recognised and kept.
    [switch]$Rejoin,
    # At the end the host closes the game (as a player closes the window); the VM's side is recorded:
    # its log from then on, its co-op status and two pictures.
    [switch]$HostQuits,
    # Three players: after the VM, a second test copy on this PC joins by address (IP only).
    [switch]$ThirdPlayer,
    [string]$ThirdPath = 'D:\GK2Coop-FullClient',
    # Four players: with -ThirdPlayer, one more copy on this PC.
    [switch]$FourthPlayer,
    [string]$FourthPath = 'D:\GK2Coop-FullClient2',
    # IP: the VM joins the host's address. Steam: the host hosts on Steam with this PC's account, the VM
    # joins its lobby with its own account (friends), through Steam's relay: no address, no port.
    [ValidateSet('IP', 'Steam')][string]$Transport = 'IP'
)
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot) 'GameplayProbe\TestSafety.ps1')
. (Join-Path $PSScriptRoot 'VmGuest.ps1')
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$probeDll = Join-Path (Split-Path $PSScriptRoot) 'GameplayProbe\bin\Release\net472\GameplayProbe.dll'
$realSaveFolder = Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2'
$saveFolder = Join-Path 'D:\GK2Coop-Saves' (Split-Path -Leaf $HostPath)
$slot = 'GK2Coop_Test_' + (Get-Date -Format 'yyyyMMddHHmmss') + '_vm'
$hostConfig = Join-Path $HostPath 'BepInEx\config\com.fabio.gk2coop.cfg'
$hostLog = Join-Path $HostPath 'BepInEx\LogOutput.log'
$guestGame = 'Game'
$guestConfig = 'Game\BepInEx\config\com.fabio.gk2coop.cfg'
$guestLog = 'Game\BepInEx\LogOutput.log'
New-Item -ItemType Directory -Force -Path $OutputPath, $saveFolder | Out-Null

function Check([bool]$passed, [string]$label) {
    $line = $(if ($passed) { 'PASS ' } else { 'FAIL ' }) + $label
    Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Encoding UTF8 -Value $line
    Write-Host $line
    if (-not $passed) { throw $line }
}
function Set-Cfg([string]$text, [string]$key, [string]$value) {
    if ($text -match "(?m)^$key\s*=") { return [regex]::Replace($text, "(?m)^$key\s*=.*$", "$key = $value") }
    return $text
}
function HostProbe([string]$command) {
    $result = & (Join-Path (Split-Path $PSScriptRoot) 'GameplayProbe\Invoke-Probe.ps1') -Peer Host -Command $command -TimeoutSeconds 15
    if ($result -match '(?m)^ERROR ') { throw $result }
    return $result
}
function GuestProbe([string]$command, [int]$seconds = 20) {
    # An empty result first: the agent cannot delete, and a repeated command must not read an old answer.
    Invoke-VmGuest 'PUT' '/file?path=Game%5CBepInEx%5CGK2Coop.Probe.command.result' ([byte[]]@()) | Out-Null
    Invoke-VmGuest 'PUT' '/file?path=Game%5CBepInEx%5CGK2Coop.Probe.command' ([Text.Encoding]::UTF8.GetBytes($command)) | Out-Null
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $text = [Text.Encoding]::UTF8.GetString((Invoke-VmGuest 'GET' '/file?path=Game%5CBepInEx%5CGK2Coop.Probe.command.result'))
        if ($text.StartsWith($command + "`n")) { if ($text -match '(?m)^ERROR ') { throw $text }; return $text }
    }
    throw "Probe timed out in the VM: $command"
}
function GuestLog { [Text.Encoding]::UTF8.GetString((Invoke-VmGuest 'GET' "/file?path=$([Uri]::EscapeDataString($guestLog))" $null 60)) }
function WaitForLog([scriptblock]$read, [string]$pattern, [int]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $text = & $read
        if ($text -and $text.Contains($pattern)) { return }
        Start-Sleep -Seconds 3
    }
    throw "Timed out waiting for $what`: $pattern"
}
# The game keeps its log open while writing: read it shared.
function ReadShared([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $stream = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try { (New-Object IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Close() }
}
function Line([string]$text, [string]$prefix) { (($text -split "`n") | Where-Object { $_ -like "$prefix *" }) -join ' ; ' }

# ---------------------------------------------------------------- checks before
Assert-TestInstall $HostPath
if ((Get-TestGameProcesses @($HostPath)).Count -gt 0) { throw 'The host test copy is running.' }
if (Test-Path -LiteralPath (Join-Path $HostPath 'BepInEx\plugins\GameplayProbe.dll')) { throw 'A probe is installed in the host copy (a test is running or was interrupted).' }
$ping = Invoke-VmGuestText 'GET' '/ping'
if ($ping -notlike 'PONG*') { throw "The VM's agent does not answer: $ping" }
if ($ping -match 'games=\d') { throw "A game is already running in the VM: $ping" }
$hostAddress = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -like '*Default Switch*' } | Select-Object -First 1).IPAddress
$hostPort = [regex]::Match((Get-Content -LiteralPath $hostConfig -Raw), '(?m)^Port\s*=\s*(\d+)').Groups[1].Value
Write-Host "Host $hostAddress`:$hostPort, VM $(Get-VmGuestAddress) ($ping)"
if (-not (Test-Path -LiteralPath $probeDll)) { dotnet build (Join-Path (Split-Path $PSScriptRoot) 'GameplayProbe\GameplayProbe.csproj') -c Release -v q --nologo "-p:GamePath=$HostPath" | Out-Null }
$saveHashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $realSaveFolder -File | Where-Object { $_.Name -notlike 'GK2Coop_*' -and $_.Extension -in '.dat', '.info' }) { $saveHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash }
$prefs = Save-GamePrefs
Copy-Item -LiteralPath $hostConfig -Destination (Join-Path $OutputPath 'Host-config-before.cfg')
[IO.File]::WriteAllBytes((Join-Path $OutputPath 'Guest-config-before.cfg'), (Invoke-VmGuest 'GET' "/file?path=$([Uri]::EscapeDataString($guestConfig))"))

# Players 3 and 4: test copies on this PC joining by address.
$extras = @()
if ($ThirdPlayer) { $extras += [pscustomobject]@{ Name = 'Third'; Path = $ThirdPath; Saves = 'D:\GK2Coop-Saves\FullClient'; Process = $null } }
if ($FourthPlayer) { $extras += [pscustomobject]@{ Name = 'Fourth'; Path = $FourthPath; Saves = 'D:\GK2Coop-Saves\FullClient2'; Process = $null } }
foreach ($x in $extras) {
    if ($Transport -ne 'IP') { throw 'Players 3 and 4 join by address: use -Transport IP.' }
    Assert-TestInstall $x.Path
    if ((Get-TestGameProcesses @($x.Path)).Count -gt 0) { throw "The $($x.Name) copy is running." }
    if (Test-Path -LiteralPath (Join-Path $x.Path 'BepInEx\plugins\GameplayProbe.dll')) { throw "A probe is installed in the $($x.Name) copy (a test is running or was interrupted)." }
    Copy-Item -LiteralPath (Join-Path $x.Path 'BepInEx\config\com.fabio.gk2coop.cfg') -Destination (Join-Path $OutputPath "$($x.Name)-config-before.cfg")
}
function ExtraProbe($x, [string]$command) {
    $env:GK2COOP_TEST_CLIENT_PATH = $x.Path
    $result = & (Join-Path (Split-Path $PSScriptRoot) 'GameplayProbe\Invoke-Probe.ps1') -Peer Client -Command $command -TimeoutSeconds 20
    if ($result -match '(?m)^ERROR ') { throw $result }
    return $result
}

$hostProcess = $null
try {
    foreach ($x in $extras) {
        $xConfig = Join-Path $x.Path 'BepInEx\config\com.fabio.gk2coop.cfg'
        $cfgX = Get-Content -LiteralPath $xConfig -Raw
        foreach ($pair in @(@('StartupMode', 'None'), @('Address', '127.0.0.1'), @('Port', $hostPort), @('AutoStartNewGame', 'false'), @('PlayerName', $x.Name), @('Transport', 'IP'))) { $cfgX = Set-Cfg $cfgX $pair[0] $pair[1] }
        [IO.File]::WriteAllText($xConfig, $cfgX)
        Copy-Item -LiteralPath $probeDll -Destination (Join-Path $x.Path 'BepInEx\plugins\GameplayProbe.dll')
        $xLog = Join-Path $x.Path 'BepInEx\LogOutput.log'
        if (Test-Path -LiteralPath $xLog) { Move-Item -LiteralPath $xLog -Destination (Join-Path $OutputPath "$($x.Name)-log-before.txt") -Force }
        New-Item -ItemType Directory -Force -Path $x.Saves | Out-Null
    }
    # ---------------------------------------------------------------- set up
    Copy-Item -LiteralPath (Join-Path $SaveBackup 'Steam_1.dat') -Destination (Join-Path $saveFolder "$slot.dat")
    $info = [regex]::Replace((Get-Content -LiteralPath (Join-Path $SaveBackup 'Steam_1.info') -Raw), '"saveDateTime":"[^"]+"', '"saveDateTime":"01.01.2000 00:00:00"')
    [IO.File]::WriteAllText((Join-Path $saveFolder "$slot.info"), $info)
    Copy-Item -LiteralPath $probeDll -Destination (Join-Path $HostPath 'BepInEx\plugins\GameplayProbe.dll')
    $cfg = Get-Content -LiteralPath $hostConfig -Raw
    foreach ($pair in @(@('StartupMode', 'Host'), @('Address', '0.0.0.0'), @('AutoStartNewGame', 'false'), @('PlayerName', 'Host'), @('Transport', $Transport))) { $cfg = Set-Cfg $cfg $pair[0] $pair[1] }
    [IO.File]::WriteAllText($hostConfig, $cfg)
    $guestCfg = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $OutputPath 'Guest-config-before.cfg')))
    foreach ($pair in @(@('StartupMode', 'None'), @('Address', $hostAddress), @('Port', $hostPort), @('AutoStartNewGame', 'false'), @('PlayerName', 'VmGuest'), @('Transport', $Transport))) { $guestCfg = Set-Cfg $guestCfg $pair[0] $pair[1] }
    Invoke-VmGuest 'PUT' "/file?path=$([Uri]::EscapeDataString($guestConfig))" ([Text.Encoding]::UTF8.GetBytes($guestCfg)) | Out-Null
    Send-VmFile $probeDll 'Game\BepInEx\plugins\GameplayProbe.dll'
    Invoke-VmGuest 'PUT' "/file?path=$([Uri]::EscapeDataString($guestLog))" ([byte[]]@()) | Out-Null

    # ---------------------------------------------------------------- host
    $env:GK2COOP_TEST_SAVE_FOLDER = $saveFolder
    $env:GK2COOP_TEST_CONTINUE_SLOT = $slot
    $env:GK2COOP_TEST_HOST_PATH = $HostPath
    if (Test-Path -LiteralPath $hostLog) { Move-Item -LiteralPath $hostLog -Destination (Join-Path $OutputPath 'Host-log-before.txt') -Force }
    $hostProcess = Start-TestGame $HostPath
    WaitForLog { ReadShared $hostLog } 'Attached native host networking' 240 'the host'
    if ($Transport -eq 'Steam') {
        $lobbyLine = ''
        $deadline = (Get-Date).AddSeconds(90)
        while ((Get-Date) -lt $deadline -and $lobbyLine -notmatch '^STEAM-LOBBY \d+ ') {
            Start-Sleep -Seconds 3
            $lobbyLine = Line (HostProbe 'steam-lobby') 'STEAM-LOBBY'
        }
        Check ($lobbyLine -match 'owner=7656\d+ connect=\+connect_lobby \d+') "The host hosts on Steam: a lobby for friends ($lobbyLine)"
        $lobbyId = ($lobbyLine -split ' ')[1]
    } else {
        Check $true "The host hosts on $hostAddress`:$hostPort"
    }

    # ---------------------------------------------------------------- the joiner in the VM
    $start = @{ exe = 'Game\GraveyardKeeper2.exe'; arguments = '-screen-fullscreen 0 -window-mode windowed -screen-width 1280 -screen-height 720'; environment = @{ GK2COOP_TEST_MENU_CONNECT = '1'; GK2COOP_TEST_SAVE_FOLDER = 'C:\GK2Coop\Saves' } } | ConvertTo-Json
    Write-Host (Invoke-VmGuestText 'POST' '/start' $start)
    WaitForLog { GuestLog } 'gameState=MainMenu' 240 'the VM game at its main menu'
    if ($Transport -eq 'Steam') {
        # As the invite or "Join game" would: enter the friend's lobby; the mod then connects to the
        # lobby's owner by SteamID (Steam's relay), not by address.
        $join = GuestProbe "steam-join-lobby|$lobbyId" 30
        Check ($join -match 'requested') "The VM's account joins the host's Steam lobby ($(Line $join 'STEAM-JOIN-LOBBY'))"
        WaitForLog { GuestLog } 'Steam lobby: joining host 7656' 90 'the VM joining through the lobby'
        $how = ([regex]::Match((GuestLog), 'Steam lobby: joining host [^\r\n]*')).Value
        Check ($how -match 'at steam') "The VM connects to the host's SteamID, not an address ($how)"
    } else {
        $bootstrap = GuestProbe "menu-bootstrap|$hostAddress|$hostPort"
        Check ($bootstrap -match 'MENU-BOOTSTRAP started') "The VM's joiner starts joining from the menu ($(Line $bootstrap 'MENU-BOOTSTRAP'))"
    }
    WaitForLog { GuestLog } 'Verified and imported host save into isolated slot' 240 "the host's world in the VM"
    Check $true 'The VM receives and verifies the copy of the host''s world over the network'
    WaitForLog { GuestLog } 'Attached client networking to the normally initialized local game world.' 240 'the VM joining'
    WaitForLog { ReadShared $hostLog } 'joined as' 90 'the host seeing the joiner'
    $players = HostProbe 'players'
    Check ($players -match 'PLAYER id=\d+ client') "The host has the VM's player ($((Line $players 'PLAYER') -replace '\s+', ' '))"
    Start-Sleep -Seconds 10

    # ---------------------------------------------------------------- chests both ways
    $inspect = HostProbe 'inspect'
    if ($inspect -notmatch 'CONTAINER ([0-9a-f-]+) chest_home size=\d+ items=([^\r\n]*)') { throw 'No chest_home.' }
    $chest = $Matches[1]
    # All berry stacks in the chest (a stack holds 50; more go into a second one).
    $berries = { param($text) $line = [regex]::Match($text, "(?m)^CONTAINER $chest [^\r\n]*").Value; $n = 0; foreach ($m in [regex]::Matches($line, 'berryx(\d+)')) { $n += [int]$m.Groups[1].Value }; $n }
    $b0 = & $berries $inspect
    HostProbe "container-add|$chest|berry|2" | Out-Null
    Start-Sleep -Seconds 3
    $b1 = & $berries (GuestProbe 'inspect')
    Check ($b1 -eq $b0 + 2) "A chest edit on the host reaches the VM ($b0 -> $b1 berries)"
    GuestProbe "container-add|$chest|berry|3" | Out-Null
    Start-Sleep -Seconds 3
    $b2 = & $berries (HostProbe 'inspect')
    Check ($b2 -eq $b0 + 5) "A chest edit in the VM reaches the host ($b1 -> $b2 berries)"
    if ($Transport -eq 'Steam') {
        # Over Steam everyone goes by their Steam name.
        $names = Line (HostProbe 'players') 'PLAYER'
        $joined = ([regex]::Match((ReadShared $hostLog), 'joined as [^\r\n]*')).Value
        Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Encoding UTF8 -Value "names: $joined"
        Check ($joined -and $joined -notmatch 'VmGuest|BootstrapGuest|Player \d') "Over Steam the VM's player goes by their Steam name ($joined)"
    }
    $looks = @(((GuestProbe 'look-draw') -split "`n") | Where-Object { $_ -like 'LOOK remote *' })
    Check ($looks.Count -gt 0 -and -not ($looks | Where-Object { $_ -notmatch 'preset=yes missing=0 shared-with-local=0' })) "The VM draws the host in the host's own colours ($($looks -join ' | '))"
    $looks = @(((HostProbe 'look-draw') -split "`n") | Where-Object { $_ -like 'LOOK remote *' })
    Check ($looks.Count -gt 0 -and -not ($looks | Where-Object { $_ -notmatch 'preset=yes missing=0 shared-with-local=0' })) "The host draws the VM's player in their own colours ($($looks -join ' | '))"
    $wisp = Line (GuestProbe 'wisp') 'WISP'
    Check ($wisp -match 'their wisps on=0') "The host carries no wisp of their own on the VM's screen ($wisp)"
    $wisp = Line (HostProbe 'wisp') 'WISP'
    Check ($wisp -match 'their wisps on=0') "The VM's player carries no wisp of their own on the host's screen ($wisp)"
    $centres = Line (GuestProbe 'tag-centres') 'TAG-CENTRES'
    $worst = ([regex]::Matches($centres, 'vsHead=(-?[\d.]+)') | ForEach-Object { [Math]::Abs([double]$_.Groups[1].Value) } | Measure-Object -Maximum).Maximum
    Check ($centres -match 'vsHead=-?\d' -and $worst -lt 6) "The VM's name tags sit centred over the heads, within 6 pixels ($centres)"
    GuestProbe "shot|C:\GK2Coop\look.png" | Out-Null
    try { Receive-VmFile 'look.png' (Join-Path $OutputPath 'vm-look.png') } catch { }
    $status = Line (GuestProbe 'status-ui') 'STATUS-UI'
    Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Encoding UTF8 -Value "VM status: $status"

    # ---------------------------------------------------------------- a third player on this PC
    $count = 2
    foreach ($x in $extras) {
        $count++
        $xLog = Join-Path $x.Path 'BepInEx\LogOutput.log'
        $env:GK2COOP_TEST_SAVE_FOLDER = $x.Saves
        Remove-Item Env:\GK2COOP_TEST_CONTINUE_SLOT -ErrorAction SilentlyContinue
        $env:GK2COOP_TEST_MENU_CONNECT = '1'
        $x.Process = Start-TestGame $x.Path
        Remove-Item Env:\GK2COOP_TEST_MENU_CONNECT
        $env:GK2COOP_TEST_SAVE_FOLDER = $saveFolder
        WaitForLog { ReadShared $xLog } 'gameState=MainMenu' 240 "the $($x.Name) game at its main menu"
        $bootstrap = ExtraProbe $x "menu-bootstrap|127.0.0.1|$hostPort"
        Check ($bootstrap -match 'MENU-BOOTSTRAP started') "Player $count joins from the menu by address ($(Line $bootstrap 'MENU-BOOTSTRAP'))"
        WaitForLog { ReadShared $xLog } 'Attached client networking to the normally initialized local game world.' 300 "player $count joining"
        $deadline = (Get-Date).AddSeconds(90)
        do { Start-Sleep -Seconds 3; $players = (Line (HostProbe 'players') 'PLAYER') -replace '\s+', ' ' } while (([regex]::Matches($players, ' client ')).Count -lt ($count - 1) -and (Get-Date) -lt $deadline)
        Check (([regex]::Matches($players, ' client ')).Count -eq ($count - 1)) "The host has $count players ($players)"
        Start-Sleep -Seconds 8
        $h0 = & $berries (HostProbe 'inspect')
        ExtraProbe $x "container-add|$chest|berry|4" | Out-Null
        Start-Sleep -Seconds 4
        $h1 = & $berries (HostProbe 'inspect'); $g1 = & $berries (GuestProbe 'inspect')
        Check ($h1 -eq $h0 + 4 -and $g1 -eq $h1) "A chest edit by player $count reaches the host and the VM ($h0 -> host $h1, VM $g1)"
    }
    if ($extras) {
        $seen = @(@($extras | ForEach-Object { @(((ExtraProbe $_ 'players') -split "`n") | Where-Object { $_ -like 'PLAYER *' }).Count }) + @(@(((GuestProbe 'players') -split "`n") | Where-Object { $_ -like 'PLAYER *' }).Count))
        Check (-not ($seen | Where-Object { $_ -lt $count })) "Everyone has all $count players (the copies on this PC and the VM: $($seen -join ', '))"
        # Each joiner steps somewhere of their own; every game must see every player there (0.63.1
        # and older: a joiner saw the other joiners stand still and the host jump to their places).
        $me = Line (HostProbe 'players') 'PLAYER'
        if ($me -notmatch 'host pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)') { throw "No host position: $me" }
        $hx = [double]$Matches[1]; $hy = $Matches[2]; $hz = [double]$Matches[3]
        $step = 0
        foreach ($x in $extras) { $step++; ExtraProbe $x ("body-to|{0}|{1}|{2}" -f ($hx + 1.5 * $step).ToString([Globalization.CultureInfo]::InvariantCulture), ($hz - 1).ToString([Globalization.CultureInfo]::InvariantCulture), $hy) | Out-Null }
        GuestProbe ("body-to|{0}|{1}|{2}" -f ($hx - 1.5).ToString([Globalization.CultureInfo]::InvariantCulture), ($hz - 1).ToString([Globalization.CultureInfo]::InvariantCulture), $hy) | Out-Null
        Start-Sleep -Seconds 6
        # "PLAYER id=N host|client pos=(x, y, z)" per game; the host's view is the truth.
        $positions = { param($text) $map = @{}; foreach ($m in [regex]::Matches($text, 'PLAYER id=(\d+)[^(]*pos=\(([-\d.]+), [-\d.]+, ([-\d.]+)\)')) { $map[$m.Groups[1].Value] = @([double]$m.Groups[2].Value, [double]$m.Groups[3].Value) }; $map }
        $truth = & $positions (HostProbe 'players')
        $views = @(@{ Who = 'VM'; Map = (& $positions (GuestProbe 'players')) }) + @($extras | ForEach-Object { @{ Who = $_.Name; Map = (& $positions (ExtraProbe $_ 'players')) } })
        $off = New-Object System.Collections.Generic.List[string]
        foreach ($view in $views) {
            foreach ($id in $truth.Keys) {
                $p = $view.Map[$id]
                if (-not $p -or [Math]::Abs($p[0] - $truth[$id][0]) -gt 0.5 -or [Math]::Abs($p[1] - $truth[$id][1]) -gt 0.5) { $off.Add("$($view.Who) sees player $id at $(if ($p) { '{0:N1},{1:N1}' -f $p[0], $p[1] } else { 'nowhere' }), host at $('{0:N1},{1:N1}' -f $truth[$id][0], $truth[$id][1])") }
            }
        }
        Check ($off.Count -eq 0) "Every game sees every player where they are, the other joiners too$(if ($off.Count) { ': ' + ($off -join '; ') })"
        GuestProbe "container-add|$chest|berry|1" | Out-Null
        Start-Sleep -Seconds 4
        $h2 = & $berries (HostProbe 'inspect')
        $others = @($extras | ForEach-Object { & $berries (ExtraProbe $_ 'inspect') })
        Check ($h2 -eq $h1 + 1 -and -not ($others | Where-Object { $_ -ne $h2 })) "A chest edit in the VM reaches the host and every other player (host $h2, others $($others -join ', '))"
        foreach ($x in $extras) { try { ExtraProbe $x "shot|$(Join-Path $OutputPath "$($x.Name.ToLower())-look.png")" | Out-Null } catch { } }
        try { GuestProbe "shot|C:\GK2Coop\look-many.png" | Out-Null; Receive-VmFile 'look-many.png' (Join-Path $OutputPath 'vm-look-many.png') } catch { }
        # One joiner leaves (the game's way to the main menu): every other game takes their body
        # away (0.64 and older: it stayed standing on the other joiners' screens). The leaver stays
        # in its menu until the clean-up.
        $leaver = $extras[-1]
        ExtraProbe $leaver 'go-to-menu' | Out-Null
        Start-Sleep -Seconds 12
        $left = $count - 1
        $bodyCount = { param($text) @(($text -split "`n") | Where-Object { $_ -like 'BODY *' }).Count }
        $hostBodies = & $bodyCount (HostProbe 'body-players')
        $vmBodies = & $bodyCount (GuestProbe 'body-players')
        $stay = @($extras | Where-Object { $_ -ne $leaver } | ForEach-Object { & $bodyCount (ExtraProbe $_ 'body-players') })
        Check ($hostBodies -eq $left -and $vmBodies -eq $left -and -not ($stay | Where-Object { $_ -ne $left })) "When a joiner leaves, every other game draws $left keepers (host $hostBodies, VM $vmBodies, others $($stay -join ', '))"
        Check ((GuestLog) -match "left; removed their record and 1 body") "The VM was told who left and took their body away"
    }

    # ---------------------------------------------------------------- a fight on the host, over the network
    if (-not $NoFight) {
        HostProbe 'fight-lock|choose|fight_A1_1' | Out-Null
        Start-Sleep -Seconds 6
        $notice = Line (GuestProbe 'fight-watch') 'FIGHT-WATCH'
        $lock = Line (GuestProbe 'fight-lock') 'FIGHT-LOCK'
        $t1 = if ($lock -match 'time=([\d.,]+)') { [double]($Matches[1] -replace ',', '.') } else { -1 }
        Start-Sleep -Seconds 4
        $t2 = if ((Line (GuestProbe 'fight-lock') 'FIGHT-LOCK') -match 'time=([\d.,]+)') { [double]($Matches[1] -replace ',', '.') } else { -2 }
        Check ($notice -match 'fight watch' -and $lock -match 'holder=(?!nobody)\S' -and $lock -match 'paused=True' -and [Math]::Abs($t2 - $t1) -lt 0.0005) "The VM hears of the host's fight and its clock stands still ($notice; $lock; time $t1 -> $t2)"
        $refused = Line (GuestProbe 'fight-lock|choose|fight_A1_1') 'FIGHT-LOCK'
        Check ($refused -match '^FIGHT-LOCK refused ') "The VM cannot start a fight meanwhile ($refused)"
        HostProbe 'fight-stop' | Out-Null
        Start-Sleep -Seconds 3
        HostProbe 'close-windows' | Out-Null
        Start-Sleep -Seconds 6
        $lock = Line (GuestProbe 'fight-lock') 'FIGHT-LOCK'
        $notice = Line (GuestProbe 'fight-watch') 'FIGHT-WATCH'
        Check ($lock -match 'holder=nobody' -and $lock -match 'paused=False') "After the host's fight the VM's clock runs again ($notice; $lock)"
    }

    # ---------------------------------------------------------------- a long session
    if ($SoakMinutes -gt 0) {
        $soak = New-Object System.Collections.Generic.List[string]
        $end = (Get-Date).AddMinutes($SoakMinutes)
        $round = 0
        $bad = 0
        $expected = & $berries (HostProbe 'inspect')
        $me = (Line (HostProbe 'players') 'PLAYER')
        if ($me -notmatch 'host pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)') { throw "No host position: $me" }
        $hx = [double]$Matches[1]; $hy = $Matches[2]; $hz = $Matches[3]
        while ((Get-Date) -lt $end) {
            $round++
            # Chest edits from alternating sides, both players walking a little.
            if ($round % 2) { HostProbe "container-add|$chest|berry|1" | Out-Null } else { GuestProbe "container-add|$chest|berry|1" | Out-Null }
            $expected++
            $dx = @(0.8, -0.8)[$round % 2]
            HostProbe "body-to|$($hx + $dx)|$hz|$hy" | Out-Null
            Start-Sleep -Seconds 25
            $hb = & $berries (HostProbe 'inspect'); $gb = & $berries (GuestProbe 'inspect')
            $players = (Line (HostProbe 'players') 'PLAYER') -replace '\s+', ' '
            $status = Line (GuestProbe 'status-ui') 'STATUS-UI'
            $ok = ($hb -eq $expected -and $gb -eq $expected -and $players -match 'client')
            if (-not $ok) { $bad++ }
            $soak.Add("$(Get-Date -Format HH:mm:ss) round $round host=$hb vm=$gb expected=$expected $(if ($ok) { 'ok' } else { 'DIFFERENT' }); $players; $status")
            [IO.File]::WriteAllText((Join-Path $OutputPath 'soak.txt'), ($soak -join "`n"))
        }
        Check ($bad -eq 0 -and $round -gt 0) "A $SoakMinutes-minute session over the network stays in step ($round rounds of chest edits both ways and walking, $bad out of step)"
    }

    # ---------------------------------------------------------------- leave and come back
    if ($Rejoin) {
        $before = (GuestProbe 'players') -split "`n" | Where-Object { $_ -like 'PLAYER *' }
        Write-Host (Invoke-VmGuestText 'POST' '/stop')
        $hostText = ReadShared $hostLog
        $joinsBefore = ([regex]::Matches($hostText, 'joined as')).Count
        # Killed like a crash, the VM's game says no goodbye: the host notices when the connection times out.
        $gone = Get-Date
        do { Start-Sleep -Seconds 3; $players = Line (HostProbe 'players') 'PLAYER' } while ($players -match 'client' -and ((Get-Date) - $gone).TotalSeconds -lt 90)
        Check ($players -notmatch 'client') "When the VM's game drops out, the host carries on alone (noticed after $([int]((Get-Date) - $gone).TotalSeconds) s; $players)"
        Invoke-VmGuest 'PUT' "/file?path=$([Uri]::EscapeDataString($guestLog))" ([byte[]]@()) | Out-Null
        Write-Host (Invoke-VmGuestText 'POST' '/start' $start)
        WaitForLog { GuestLog } 'gameState=MainMenu' 240 'the VM game at its main menu again'
        if ($Transport -eq 'Steam') {
            $lobbyId = ((Line (HostProbe 'steam-lobby') 'STEAM-LOBBY') -split ' ')[1]
            GuestProbe "steam-join-lobby|$lobbyId" 30 | Out-Null
        } else {
            GuestProbe "menu-bootstrap|$hostAddress|$hostPort" | Out-Null
        }
        WaitForLog { GuestLog } 'Attached client networking to the normally initialized local game world.' 300 'the VM joining again'
        $deadline = (Get-Date).AddSeconds(90)
        while ((Get-Date) -lt $deadline -and ([regex]::Matches((ReadShared $hostLog), 'joined as')).Count -le $joinsBefore) { Start-Sleep -Seconds 3 }
        Start-Sleep -Seconds 8
        $hostText = ReadShared $hostLog
        $profile = ([regex]::Matches($hostText, 'Player profile: client \d+ is [^\r\n]*') | Select-Object -Last 1).Value
        $players = Line (HostProbe 'players') 'PLAYER'
        $gb = & $berries (GuestProbe 'inspect'); $hb = & $berries (HostProbe 'inspect')
        Check ($players -match 'client' -and $profile -match 'sent Stored') "The VM's player comes back and is recognised: their own state, not a new player ($profile; $players)"
        Check ($gb -eq $hb) "After coming back the VM's world matches the host's (chest: host $hb, VM $gb)"
    }

    # ---------------------------------------------------------------- the host leaves
    if ($HostQuits) {
        function Picture([string]$name) { try { GuestProbe "shot|C:\GK2Coop\$name.png" | Out-Null; Receive-VmFile "$name.png" (Join-Path $OutputPath "vm-$name.png") } catch { } }
        function GuestState { Line (GuestProbe 'game-state') 'GAME-STATE' }
        function WaitGuest([scriptblock]$ok, [int]$seconds) { $deadline = (Get-Date).AddSeconds($seconds); do { Start-Sleep -Seconds 2; $v = & $ok; if ($v) { return $v } } while ((Get-Date) -lt $deadline); return $null }
        $logBefore = (GuestLog).Length

        # The joiner leaving on their own (the game's way to the main menu) is theirs: no "session ended".
        GuestProbe 'go-to-menu' | Out-Null
        $menu = WaitGuest { $s = GuestState; if ($s -match 'MainMenu') { $s } } 30
        Start-Sleep -Seconds 15
        $status = Line (GuestProbe 'status-ui') 'STATUS-UI'
        Check ($menu -and $status -notmatch '^STATUS-UI Failed') "The VM's player leaving to the main menu themselves gets no 'session ended' ($menu; $status)"
        $attached = ([regex]::Matches((GuestLog), 'accepted the connection')).Count
        if ($Transport -eq 'Steam') {
            # As seen once after the VM's game was killed: Steam closes the host's lobby and the
            # friend's join names the old one. The host opens a new lobby; the VM must find it.
            $oldLobby = ((Line (HostProbe 'steam-lobby') 'STEAM-LOBBY') -split ' ')[1]
            HostProbe 'steam-lobby-drop' | Out-Null
            GuestProbe "steam-join-lobby|$oldLobby" 30 | Out-Null
            $told = WaitGuest { $o = Line (GuestProbe 'steam-lobby-outcome') 'STEAM-LOBBY-OUTCOME'; if ($o -match 'failed') { $o } } 30
            Check ($told -and $told -notmatch 'k_EChatRoom') "Joining a lobby Steam has closed says to try again, without Steam's code ($told)"
            # As the player trying again: "Join game" gives the host's current lobby.
            $newLobby = $oldLobby
            $deadline = (Get-Date).AddSeconds(30)
            while ($newLobby -eq $oldLobby -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2; $newLobby = ((Line (HostProbe 'steam-lobby') 'STEAM-LOBBY') -split ' ')[1] }
            GuestProbe "steam-join-lobby|$newLobby" 30 | Out-Null
        } else { GuestProbe "menu-bootstrap|$hostAddress|$hostPort" | Out-Null }
        $again = WaitGuest { if (([regex]::Matches((GuestLog), 'accepted the connection')).Count -gt $attached) { 'joined' } } 300
        Start-Sleep -Seconds 10
        Check ($again -and (Line (HostProbe 'players') 'PLAYER') -match 'client') "The VM's player joins again from the main menu$(if ($Transport -eq 'Steam') { ' (the second try, through the host''s new lobby)' })"

        # The host leaves (the game's way to the main menu): the VM is told, and OK takes it to the menu.
        HostProbe 'go-to-menu' | Out-Null
        $left = Get-Date
        $ended = WaitGuest { $s = Line (GuestProbe 'status-ui') 'STATUS-UI'; if ($s -match '^STATUS-UI Failed') { $s } } 90
        Picture 'hostleft'
        Check ([bool]$ended) "When the host leaves, the VM is told the session has ended (after $([int]((Get-Date) - $left).TotalSeconds) s: $ended)"
        GuestProbe 'status-ui|ok' | Out-Null
        $menu = WaitGuest { $s = GuestState; if ($s -match 'MainMenu') { $s } } 30
        Start-Sleep -Seconds 5
        Picture 'hostleft-ok'
        Check ([bool]$menu) "OK takes the VM's player to the main menu ($menu; $(Line (GuestProbe 'status-ui') 'STATUS-UI'))"
        if ($Transport -eq 'Steam') {
            # The host is in its menu now, not hosting: joining them by SteamID fails, and the message
            # names them and says nothing of addresses or ports (Steam needs none).
            $owner = [regex]::Match($lobbyLine, 'owner=(\d+)').Groups[1].Value
            GuestProbe "menu-bootstrap|steam:$owner|8889" | Out-Null
            $unreachable = WaitGuest { $s = Line (GuestProbe 'status-ui') 'STATUS-UI'; if ($s -match '^STATUS-UI Failed') { $s } } 150
            Picture 'unreachable'
            Check ($unreachable -match 'Steam' -and $unreachable -notmatch '8889|7656\d{10,}') "Joining a friend who is not hosting says so by name, without an address or port ($unreachable)"
        }
        $after = GuestLog
        [IO.File]::WriteAllText((Join-Path $OutputPath 'VM-log-after-host-quit.txt'), $after.Substring([Math]::Min($logBefore, $after.Length)), (New-Object Text.UTF8Encoding($true)))
    }
}
finally {
    [Environment]::SetEnvironmentVariable('GK2COOP_TEST_SAVE_FOLDER', $null)
    [Environment]::SetEnvironmentVariable('GK2COOP_TEST_CONTINUE_SLOT', $null)
    [Environment]::SetEnvironmentVariable('GK2COOP_TEST_HOST_PATH', $null)
    try { Write-Host (Invoke-VmGuestText 'POST' '/stop') } catch { Write-Host "VM stop: $($_.Exception.Message)" }
    if ($hostProcess -and -not $hostProcess.HasExited) { $hostProcess.Kill(); $hostProcess.WaitForExit(10000) | Out-Null }
    foreach ($x in $extras) {
        if ($x.Process -and -not $x.Process.HasExited) { $x.Process.Kill(); $x.Process.WaitForExit(10000) | Out-Null }
        $n = $x.Name
        $xLog = Join-Path $x.Path 'BepInEx\LogOutput.log'
        if (Test-Path -LiteralPath $xLog) { Copy-Item -LiteralPath $xLog -Destination (Join-Path $OutputPath "$n-log.txt") -Force }
        $xProbe = Join-Path $x.Path 'BepInEx\plugins\GameplayProbe.dll'
        if (Test-Path -LiteralPath $xProbe) { Move-Item -LiteralPath $xProbe -Destination (Join-Path $OutputPath "$n-GameplayProbe.dll") -Force }
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $x.Path 'BepInEx') -Filter 'GK2Coop.Probe.command*' -ErrorAction SilentlyContinue) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath "$n-$($file.Name)") -Force }
        if (Test-Path -LiteralPath (Join-Path $OutputPath "$n-config-before.cfg")) { Copy-Item -LiteralPath (Join-Path $OutputPath "$n-config-before.cfg") -Destination (Join-Path $x.Path 'BepInEx\config\com.fabio.gk2coop.cfg') -Force }
        # This game's copy of the host's world: kept with the run, out of its save folder.
        foreach ($file in Get-ChildItem -LiteralPath $x.Saves -Filter 'GK2Coop_*' -File -ErrorAction SilentlyContinue) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath "$n-$($file.Name)") -Force }
    }
    [Environment]::SetEnvironmentVariable('GK2COOP_TEST_CLIENT_PATH', $null)
    try { [IO.File]::WriteAllBytes((Join-Path $OutputPath 'VM-log.txt'), (Invoke-VmGuest 'GET' "/file?path=$([Uri]::EscapeDataString($guestLog))" $null 60)) } catch { }
    try { Invoke-VmGuest 'PUT' "/file?path=$([Uri]::EscapeDataString($guestConfig))" ([IO.File]::ReadAllBytes((Join-Path $OutputPath 'Guest-config-before.cfg'))) | Out-Null } catch { Write-Host "VM config not restored: $($_.Exception.Message)" }
    # The VM's probe stays in its plugins folder (the agent cannot delete); without GK2COOP_TEST_* it does nothing.
    if (Test-Path -LiteralPath $hostLog) { Copy-Item -LiteralPath $hostLog -Destination (Join-Path $OutputPath 'Host-log.txt') -Force }
    $probe = Join-Path $HostPath 'BepInEx\plugins\GameplayProbe.dll'
    if (Test-Path -LiteralPath $probe) { Move-Item -LiteralPath $probe -Destination (Join-Path $OutputPath 'Host-GameplayProbe.dll') -Force }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $HostPath 'BepInEx') -Filter 'GK2Coop.Probe.command*' -ErrorAction SilentlyContinue) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath $file.Name) -Force }
    Copy-Item -LiteralPath (Join-Path $OutputPath 'Host-config-before.cfg') -Destination $hostConfig -Force
    foreach ($file in Get-ChildItem -LiteralPath $saveFolder -Filter "$slot*" -File -ErrorAction SilentlyContinue) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath $file.Name) -Force }
    Write-Host "Game preferences restored: $(Restore-GamePrefs $prefs) value(s)"
    $changed = @($saveHashes.Keys | Where-Object { (Get-FileHash -LiteralPath (Join-Path $realSaveFolder $_)).Hash -ne $saveHashes[$_] })
    Write-Host "Your saves: $($saveHashes.Count - $changed.Count) unchanged$(if ($changed) { ', changed: ' + ($changed -join ', ') })"
}
