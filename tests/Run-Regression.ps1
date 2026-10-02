<#
.SYNOPSIS
    Runs the co-op regression on the D: test copies: every run is two games (host and joiner)
    through tests\GameplayProbe\Verify-ProgressedCoop.ps1, on up to two pairs of copies at once.

.DESCRIPTION
    -Profile Quick (default) leaves out the long runs (15-minute soak; language, resolution and
    divergence surveys); -Profile Full runs everything, before a package or release.
    -Changed runs only what the source files changed since the last green run touch (a file no
    run names means everything). -Only runs the named runs.

    Before anything starts: the current build is copied to every copy (-NoDeploy skips that), the
    probe is built once, the player's own saves are hashed and the game's registry preferences
    kept; afterwards the preferences are put back and the saves checked. Results go to
    D:\GK2Coop-Artifacts\regress-<version>-<time>\summary.txt.

.EXAMPLE
    .\tests\Run-Regression.ps1 -Changed
.EXAMPLE
    .\tests\Run-Regression.ps1 -Profile Full
.EXAMPLE
    .\tests\Run-Regression.ps1 -Only worldcopy,menu
#>
[CmdletBinding()]
param(
    [ValidateSet('Quick', 'Full')][string]$Profile = 'Quick',
    [string[]]$Only,
    [switch]$Changed,
    [ValidateRange(1, 2)][int]$Lanes = 2,
    [switch]$NoDeploy,
    # Which pairs of copies to use, e.g. 2 for the second only (leaving the first free for a VM session).
    [int[]]$UsePairs = @(),
    [string]$OutputRoot = 'D:\GK2Coop-Artifacts'
)

$ErrorActionPreference = 'Stop'
# "a,b" from a command line arrives as one string.
if ($Only) { $Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'GameplayProbe\TestSafety.ps1')
$harness = Join-Path $PSScriptRoot 'GameplayProbe\Verify-ProgressedCoop.ps1'
$manifest = Join-Path $root 'artifacts\regression-green.json'
$saveFolder = Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2'
$pairs = @(
    [pscustomobject]@{ Host = 'D:\GK2Coop-FullHost'; Client = 'D:\GK2Coop-FullClient' },
    [pscustomobject]@{ Host = 'D:\GK2Coop-FullHost2'; Client = 'D:\GK2Coop-FullClient2' }
)
if ($UsePairs.Count) { $pairs = @($UsePairs | ForEach-Object { $pairs[$_ - 1] }) } else { $pairs = @($pairs | Select-Object -First $Lanes) }
$pairs = @($pairs | Where-Object { Test-Path -LiteralPath (Join-Path $_.Host 'GraveyardKeeper2.exe') })

# ---------------------------------------------------------------- the runs
# Covers: the source files (src\GK2Coop, without .cs) a run is there for; Minutes: how long it
# took last time, for ordering (longest first) and the estimate; Full: only in -Profile Full.
function Run([string]$Name, [hashtable]$Params, [string[]]$Covers, [double]$Minutes, [switch]$Full, [switch]$Solo) {
    [pscustomobject]@{ Name = $Name; Params = $Params; Covers = $Covers; Minutes = $Minutes; Full = [bool]$Full; Solo = [bool]$Solo }
}
$sceneFiles = 'CoopSceneShare', 'CoopScenePrompt', 'CoopProgressWindow', 'CoopInput', 'CoopCutsceneTrace'
$runs = @(
    Run 'menu' @{ MenuBootstrapClient = $true; MenuLookExperiment = $true } @('CoopMenu', 'CoopSaveBootstrap', 'CoopPlayerProfiles', 'CoopProgressWindow', 'CoopStatus', 'CoopJoinSnapshot') 4
    Run 'surveys' @{ MenuBootstrapClient = $true; TranslationSurvey = $true; ResolutionSurvey = $true; AuditExperiment = $true } @('CoopText*', 'L', 'CoopWorldSync', 'CoopDropSync', 'CoopContainerSync') 10 -Full
    Run 'world' @{ HudLookExperiment = $true; TranslationSurvey = $true; ChatExperiment = $true; GemsExperiment = $true; WeatherExperiment = $true; CraftExperiment = $true; SaveHygieneExperiment = $true; FightExperiment = $true } @('CoopHud', 'CoopChat', 'CoopSharedGems', 'CoopWeatherSync', 'CoopCraftSync', 'CoopCraftEndSync', 'CoopSaveHygiene', 'CoopFightSync', 'CoopText*', 'L') 5.5
    Run 'plain' @{ MenuBootstrapClient = $true; MenuLookExperiment = $true; PlainUi = $true } @('CoopMenu') 1.7
    Run 'steam' @{ Transport = 'Steam'; ChatExperiment = $true; GemsExperiment = $true; CraftExperiment = $true } @('SteamSocketsTransport', 'CoopSteamLobby', 'CoopSteamTransportSwitch') 2.3
    # Joining through the host's Steam lobby, as an invite or "Join game" does (one account plays both sides).
    Run 'lobby' @{ JoinViaSteamLobby = $true } @('CoopSteamLobby', 'CoopSaveBootstrap') 3
    Run 'share' @{ SceneShareExperiment = $true; ShareScene = 'Event_124_Village_Money:124_village_money_chest_1'; ShareNear = 'npc_jeffry' } $sceneFiles 2
    Run 'answer' @{ SceneShareExperiment = $true; ShareScene = 'Event_116_Base_Attic:116_base_attic_1'; ShareAnswer = $true } $sceneFiles 5
    Run 'skip' @{ SceneShareExperiment = $true; ShareScene = 'Event_137_Base_Resurrection:137_base_resurection_room_1'; ShareSkipEvent = '137_base_resurection_room_note'; ShareSpawn = 'npc_larry_event' } $sceneFiles 1.7
    Run 'by-joiner' @{ SceneShareExperiment = $true; ShareByJoiner = $true; ShareScene = 'Event_124_Village_Money:124_village_money_chest_1'; ShareNear = 'npc_jeffry' } $sceneFiles 2
    Run 'decline' @{ SceneDeclineExperiment = $true } $sceneFiles 1.6
    Run 'stop' @{ SceneStopExperiment = $true } $sceneFiles 2
    Run 'prompt' @{ ScenePromptLook = $true } ($sceneFiles + 'GameUi') 2.4
    Run 'nav' @{ MenuBootstrapClient = $true; NavSurvey = $true } @('CoopMenu', 'CoopKeyboard', 'CoopInput', 'GameUi', 'CoopSaveBootstrap') 2.3
    Run 'pause' @{ PausePadExperiment = $true } @('CoopPauseEntry', 'CoopChat', 'CoopInput', 'GameUi') 1.7
    Run 'late' @{ LateWatchExperiment = $true } ($sceneFiles + 'CoopPauseEntry') 2
    Run 'late-ans' @{ LateWatchExperiment = $true; LateWithAnswer = $true } ($sceneFiles + 'CoopPauseEntry') 6.8
    Run 'speech' @{ SpeechExperiment = $true } @('CoopSpeechShare', 'CoopHud') 1.9
    Run 'sermon' @{ SceneShareExperiment = $true; ShareScene = 'System_Pray:sermon_start' } $sceneFiles 2.5
    Run 'worklock' @{ WorkLockExperiment = $true } @('CoopWorkLock') 1.6
    Run 'fightwatch' @{ FightWatchExperiment = $true } @('CoopFightWatch') 2
    Run 'fightbuild' @{ FightBuildExperiment = $true } @('CoopBuildSync') 1.8
    Run 'fightlock' @{ FightLockExperiment = $true } @('CoopFightLock', 'CoopSession') 3
    Run 'worldcopy' @{ WorldCopyExperiment = $true } @('CoopSaveBootstrap') 3
    Run 'testtools' @{ TestToolsExperiment = $true } @('CoopTestTools', 'CoopBuildSync', 'CoopWorkLock', 'CoopPauseEntry', 'CoopZombieSync') 2.5
    Run 'tagtrace' @{ TagTraceExperiment = $true } @('CoopHud') 2
    Run 'soak' @{ SoakMinutes = 15 } @() 17 -Full
    # The older focused experiments (each proved its feature when it was built); Full only.
    Run 'garden' @{ GardenExperiment = $true } @('CoopGardenSync') 3 -Full
    Run 'vendor' @{ VendorExperiment = $true; RichExperiment = $true } @('CoopVendorSync') 3 -Full
    Run 'knowledge' @{ KnowledgeExperiment = $true } @('CoopKnowledgeSync', 'CoopQuestSync') 3 -Full
    Run 'craftend' @{ CraftEndExperiment = $true } @('CoopCraftEndSync', 'CoopCraftSync') 3 -Full
    Run 'sleep' @{ SleepExperiment = $true } @('CoopSleepSync') 3 -Full
    Run 'scene' @{ SceneExperiment = $true } @('CoopSceneSync', 'CoopCutsceneTrace') 3 -Full
    Run 'worldres' @{ WorldResExperiment = $true } @('CoopWorldResSync') 3 -Full
    Run 'build' @{ BuildExperiment = $true } @('CoopBuildSync') 3 -Full
    # One zombie experiment per run: each spawns its own zombie and counts them.
    Run 'zombies' @{ ZombieExperiment = $true } @('CoopZombieSync') 3 -Full
    Run 'zombie-work' @{ ZombieWorkExperiment = $true } @('CoopZombieSync') 3 -Full
    Run 'zombie-out' @{ ZombieOutputPickupExperiment = $true } @('CoopZombieSync', 'CoopDropSync') 3 -Full
    Run 'zombie-grab' @{ ZombiePickupConflictExperiment = $true } @('CoopZombieSync', 'CoopDropSync') 3 -Full
    Run 'pausesync' @{ PauseExperiment = $true } @('CoopPauseSync') 3 -Full
    Run 'conveyor' @{ ConveyorExperiment = $true; PorterExperiment = $true } @('CoopConveyorSync') 3 -Full
    Run 'rejoin' @{ ProfileRejoin = $true } @('CoopPlayerProfiles', 'CoopAppearanceSync', 'CoopStableIds') 3 -Full
    Run 'mix' @{ MixExperiment = $true } @() 9
    Run 'mix3' @{ MixExperiment = $true; ThirdPath = 'D:\GK2Coop-FullHost2' } @() 11 -Solo
    # The mix phases, then ten minutes of both players at random, every berry counted.
    Run 'chaos' @{ MixExperiment = $true; ChaosMinutes = 10 } @() 21 -Solo
    Run 'chaos3' @{ MixExperiment = $true; ChaosMinutes = 10; ThirdPath = 'D:\GK2Coop-FullHost2' } @() 24 -Solo
    Run 'rehost' @{ RehostExperiment = $true } @('CoopLifecycle', 'CoopWatchdog', 'CoopHostLeft', 'CoopSaveBootstrap') 6
)

# ---------------------------------------------------------------- the games' logs
# Lines that mean something went wrong in a game during a run: the game's own exceptions, and a co-op
# part reporting it could not do something. Relayed "[guest N]" copies and the logs from before the
# run ("-before") are left out. Known game-side noise unrelated to co-op is listed in $logNoise.
$logNoise = @(
    'Non-Legacy animations cannot be sampled',
    'mesh collider meshes could not be included',
    'No baked bounds for',
    'has duplicate \(icon_',
    'Player did not equipped sword or bow',
    # The game's own quit order: its network objects torn down while the process exits.
    'LazyNetwork\.OnDestroy'
)
function Get-LogProblems([string]$runFolder) {
    if (-not (Test-Path -LiteralPath $runFolder)) { return }
    foreach ($log in Get-ChildItem -LiteralPath $runFolder -Filter '*-log*.txt' -File | Where-Object { $_.Name -notmatch '-before' }) {
        $lines = Get-Content -LiteralPath $log.FullName
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($line -match '\[guest \d+\]') { continue }
            $bad = $line -match '\[game/Exception\]' -or
                   ($line -match '^\[(Warning|Error) *:Graveyard Keeper 2 Co-op Prototype\] (?!\[game/)' -and $line -match 'could not|Could not|failed|Exception|Object reference')
            if (-not $bad) { continue }
            $detail = if ($i + 1 -lt $lines.Count -and $lines[$i + 1] -match '\[game/stack\]') { ' || ' + $lines[$i + 1] } else { '' }
            if (@($logNoise | Where-Object { "$line$detail" -match $_ }).Count) { continue }
            $text = "$($log.Name):$($i + 1): $line$detail"
            if ($text.Length -gt 600) { $text = $text.Substring(0, 600) }
            $text
        }
    }
}

# ---------------------------------------------------------------- what changed
function Get-SourceHashes {
    $hashes = [ordered]@{}
    $files = @(Get-ChildItem -LiteralPath (Join-Path $root 'src\GK2Coop') -Filter '*.cs' -File) +
             @(Get-ChildItem -LiteralPath (Join-Path $root 'tests\GameplayProbe') -File | Where-Object { $_.Extension -in '.cs', '.ps1' })
    foreach ($file in $files) { $hashes[$file.FullName.Substring($root.Length + 1)] = (Get-FileHash -LiteralPath $file.FullName).Hash }
    return $hashes
}
$current = Get-SourceHashes
# Solo runs use a third copy (the second pair's host): only when named with -Only, on one lane.
$selected = @($runs | Where-Object { -not $_.Solo -and ($Profile -eq 'Full' -or -not $_.Full) })
$why = "profile $Profile"
if ($Only) {
    $selected = @($runs | Where-Object { $_.Name -in $Only })
    $why = 'only ' + ($Only -join ', ')
} elseif ($Changed) {
    if (Test-Path -LiteralPath $manifest) {
        $green = (Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).Files
        $changedFiles = @($current.Keys | Where-Object { $green.$_ -ne $current[$_] })
        $core = @($changedFiles | Where-Object {
            $name = [IO.Path]::GetFileNameWithoutExtension($_)
            $_ -like 'tests\*' -or -not @($runs | Where-Object { @($_.Covers | Where-Object { $name -like $_ }).Count -gt 0 }).Count
        })
        if ($changedFiles.Count -eq 0) { Write-Host 'Nothing changed since the last green run.'; return }
        if ($core.Count -eq 0) {
            $names = $changedFiles | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) }
            $selected = @($selected | Where-Object { $run = $_; @($names | Where-Object { $n = $_; @($run.Covers | Where-Object { $n -like $_ }).Count -gt 0 }).Count -gt 0 })
        }
        $why = "changed: $($changedFiles -join ', ')" + $(if ($core.Count) { " (touches everything: $($core -join ', '))" } else { '' })
    } else {
        $why = 'no green run recorded yet: everything'
    }
}
if ($selected.Count -eq 0) { Write-Host "No run selected ($why)."; return }
$selected = @($selected | Sort-Object Minutes -Descending)
if (@($selected | Where-Object Solo).Count) {
    $pairs = @($pairs | Select-Object -First 1)
    foreach ($third in @($selected | Where-Object Solo | ForEach-Object { $_.Params.ThirdPath } | Where-Object { $_ } | Select-Object -Unique)) {
        Assert-TestInstall $third
        if (-not $NoDeploy) { Copy-Item -LiteralPath (Join-Path $root 'src\GK2Coop\bin\Release\net472\GK2Coop.dll') -Destination (Join-Path $third 'BepInEx\plugins\GK2Coop\GK2Coop.dll') -Force }
    }
}

# ---------------------------------------------------------------- set up
foreach ($pair in $pairs) { Assert-TestInstall $pair.Host; Assert-TestInstall $pair.Client }
if ((Get-TestGameProcesses @($pairs | ForEach-Object { $_.Host; $_.Client })).Count -gt 0) { throw 'A test copy of the game is running.' }
$built = Join-Path $root 'src\GK2Coop\bin\Release\net472\GK2Coop.dll'
$version = (Get-Item -LiteralPath $built).VersionInfo.ProductVersion
if (-not $NoDeploy) { foreach ($pair in $pairs) { foreach ($copy in $pair.Host, $pair.Client) { Copy-Item -LiteralPath $built -Destination (Join-Path $copy 'BepInEx\plugins\GK2Coop\GK2Coop.dll') -Force } } }
dotnet build (Join-Path $PSScriptRoot 'GameplayProbe\GameplayProbe.csproj') -c Release -v q --nologo "-p:GamePath=$($pairs[0].Host)" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
$out = Join-Path $OutputRoot ("regress-$version-" + (Get-Date -Format 'yyyyMMdd-HHmm'))
New-Item -ItemType Directory -Force -Path $out | Out-Null
$summary = Join-Path $out 'summary.txt'
$estimate = [math]::Ceiling((($selected | Measure-Object Minutes -Sum).Sum) / $pairs.Count)
Set-Content -LiteralPath $summary -Encoding utf8 -Value @(
    "Regression $version, $($selected.Count) runs on $($pairs.Count) pair(s), started $(Get-Date -Format s) ($why)",
    "Estimate: about $estimate min")
Write-Host "Regression $version`: $($selected.Count) runs on $($pairs.Count) pair(s), about $estimate min ($why)"
$saveHashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $saveFolder -File | Where-Object { $_.Name -notlike 'GK2Coop_*' -and $_.Extension -in '.dat', '.info' }) { $saveHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash }
$prefs = Save-GamePrefs

# ---------------------------------------------------------------- run
$queue = New-Object System.Collections.Queue
foreach ($run in $selected) { $queue.Enqueue($run) }
$busy = @{}
$failures = 0
$lastStart = [datetime]::MinValue
try {
    while ($queue.Count -gt 0 -or $busy.Count -gt 0) {
        foreach ($pair in $pairs) {
            if ($queue.Count -eq 0 -or $busy.ContainsKey($pair.Host)) { continue }
            # Staggered: two games starting at the same moment read the same backup save.
            if (((Get-Date) - $lastStart).TotalSeconds -lt 20) { continue }
            $lastStart = Get-Date
            $run = $queue.Dequeue()
            $params = @{}
            foreach ($key in $run.Params.Keys) { $params[$key] = $run.Params[$key] }
            $job = Start-Job -ArgumentList $harness, $params, (Join-Path $out $run.Name), (Join-Path $out "$($run.Name)-output.txt"), $pair.Host, $pair.Client -ScriptBlock {
                param($harness, $params, $runOut, $file, $hostPath, $clientPath)
                Set-Content -Path $file -Value '' -Encoding utf8
                try {
                    & $harness -OutputPath $runOut -HostPath $hostPath -ClientPath $clientPath -SkipPrefs -SkipProbeBuild @params *>&1 |
                        ForEach-Object { Add-Content -Path $file -Value "$_" -Encoding utf8 }
                } catch {
                    Add-Content -Path $file -Value "THROWN $($_.Exception.Message)" -Encoding utf8
                    Add-Content -Path $file -Value "AT $($_.InvocationInfo.PositionMessage)`n$($_.ScriptStackTrace)" -Encoding utf8
                }
            }
            $busy[$pair.Host] = [pscustomobject]@{ Job = $job; Run = $run; Started = Get-Date }
        }
        $done = Wait-Job -Job @($busy.Values | ForEach-Object Job) -Any -Timeout 10
        if (-not $done) { continue }
        foreach ($key in @($busy.Keys)) {
            $entry = $busy[$key]
            if ($entry.Job.State -notin 'Completed', 'Failed', 'Stopped') { continue }
            Remove-Job -Job $entry.Job -Force
            $busy.Remove($key)
            $lines = @(Get-Content -Path (Join-Path $out "$($entry.Run.Name)-output.txt") -Encoding utf8)
            $pass = @($lines | Where-Object { $_ -like 'PASS *' }).Count
            $fail = @($lines | Where-Object { $_ -like 'FAIL *' -or $_ -like 'THROWN *' })
            # The logs of both games: a game exception or a co-op part failing to apply something is a
            # failure even when every check passed (the half-made mirrored buildings of 0.65.2 and
            # the moving of destroyed bodies passed every check and showed only here).
            $problems = @(Get-LogProblems (Join-Path $out $entry.Run.Name))
            if ($problems.Count) {
                [IO.File]::WriteAllLines((Join-Path $out "$($entry.Run.Name)-log-problems.txt"), [string[]]$problems, (New-Object Text.UTF8Encoding($true)))
                $fail += "LOG $($problems.Count) problem line(s): $($problems[0])"
            }
            if ($fail.Count -gt 0 -or $pass -eq 0) { $failures++ }
            # Someone at the PC: their keys, clicks and mouse reach the test game that has the focus
            # (the probe logs it). Said with the result, so such a failure is not taken for the mod's.
            # Not the "-before" logs: those are the copy's previous run.
            $realInput = @(Get-ChildItem -LiteralPath (Join-Path $out $entry.Run.Name) -Filter '*log*.txt' -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -notlike '*-before*' } |
                ForEach-Object { Select-String -LiteralPath $_.FullName -Pattern 'Real input reached this test game' -SimpleMatch }).Count
            $inputNote = if ($realInput) { "  [someone used the PC: $realInput input note(s) in the games' logs]" } else { '' }
            $line = "{0,-10} pass={1,-3} fail={2} {3,5:N1} min  {4}{6}  {5}" -f $entry.Run.Name, $pass, $fail.Count, ((Get-Date) - $entry.Started).TotalMinutes, $(if ($key -like '*2') { 'pair 2' } else { 'pair 1' }), (($fail | Select-Object -First 2) -join ' | '), $inputNote
            Add-Content -LiteralPath $summary -Value $line -Encoding utf8
            Write-Host $line
        }
    }
}
finally {
    foreach ($entry in $busy.Values) { Stop-Job -Job $entry.Job -ErrorAction SilentlyContinue; Remove-Job -Job $entry.Job -Force -ErrorAction SilentlyContinue }
    Write-Host "Game preferences restored: $(Restore-GamePrefs $prefs) value(s)"
    $changedSaves = @($saveHashes.Keys | Where-Object { -not (Test-Path -LiteralPath (Join-Path $saveFolder $_)) -or (Get-FileHash -LiteralPath (Join-Path $saveFolder $_)).Hash -ne $saveHashes[$_] })
    $left = @(Get-ChildItem -LiteralPath $saveFolder -Filter 'GK2Coop_Test_*' -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-2) })
    $savesLine = "Your saves: $($saveHashes.Count - $changedSaves.Count) unchanged"
    if ($changedSaves) { $savesLine += ", changed: $($changedSaves -join ', ') (your own game may have saved)" }
    $tail = @($savesLine,
              "Test slots left behind: $($left.Count)",
              "Finished $(Get-Date -Format s): $(if ($failures) { "$failures run(s) failed" } else { 'all green' })")
    Add-Content -LiteralPath $summary -Value $tail -Encoding utf8
    $tail | ForEach-Object { Write-Host $_ }
}
# A green run records what it was green for, for the next -Changed.
if ($failures -eq 0 -and -not $Only) {
    [ordered]@{ Version = $version; Date = (Get-Date -Format s); Profile = $Profile; Files = $current } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $manifest -Encoding utf8
    Write-Host "Recorded as green: $manifest"
}
