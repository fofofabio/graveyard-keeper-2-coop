[CmdletBinding()]
param(
    [string]$ClientPath = 'D:\GK2Coop-FullClient',
    [ValidateSet('Library', 'Override')][string]$Mode = 'Library',
    [string]$OutputPath = ('D:\GK2Coop-Artifacts\workshop-update-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
)

# Gate for CoopWorkshopUpdater. A build with a higher version stands in for a newer Workshop
# download. Runs on the client copy only, alone, with co-op off. Checks real files on disk and the
# version the relaunched game actually loaded, not only the updater's own log line.
#
#   -Mode Library (default): the way a player has it. A Steam library is laid out on D:
#     (<library>\steamapps\common\Graveyard Keeper 2 is a junction to the test copy), and the
#     stand-in sits at steamapps\workshop\content\4358690\<pinned id>. The updater finds it through
#     the item id built into this DLL, with no override.
#   -Mode Override: the stand-in is found through [Testing] WorkshopFolderOverride.
#
# Everything is put back: the copy's DLL and config, the game's shared registry preferences; the
# player's own saves are hashed before and after. The built DLL in bin is not rebuilt or touched.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestSafety.ps1')
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $projectRoot 'src\GK2Coop\GK2Coop.csproj'
$release = Join-Path $projectRoot 'src\GK2Coop\bin\Release\net472\GK2Coop.dll'
$updaterSource = Join-Path $projectRoot 'src\GK2Coop\CoopWorkshopUpdater.cs'
$realSaveFolder = Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2'
$testSaves = Join-Path 'D:\GK2Coop-Saves' 'WorkshopUpdate'
$library = 'D:\GK2Coop-WorkshopLibrary'
$report = [Collections.Generic.List[string]]::new()

function Check([bool]$Passed, [string]$Label) {
    $line = $(if ($Passed) { 'PASS ' } else { 'FAIL ' }) + $Label
    $report.Add($line); Write-Host $line
    [IO.File]::WriteAllLines((Join-Path $OutputPath 'results.txt'), $report)
}
function ReadShared([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    try { (New-Object IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Close() }
}
function Set-Key([string]$Text, [string]$Section, [string]$Key, [string]$Value) {
    if ($Text -match "(?m)^$Key\s*=") { return [regex]::Replace($Text, "(?m)^$Key\s*=.*$", "$Key = $Value") }
    if ($Text -match "(?m)^\[$Section\][ \t\r]*$") {
        return ([regex]"(?m)^\[$Section\][ \t\r]*$").Replace($Text, "[$Section]`r`n$Key = $Value`r", 1)
    }
    return $Text + "`r`n[$Section]`r`n$Key = $Value`r`n"
}
$script:game = $null
function Stop-Game {
    # Only the process this test started (by id), and any game running from the copy (by path).
    if ($script:game -and -not $script:game.HasExited) { $script:game.Kill(); $script:game.WaitForExit(15000) | Out-Null }
    $script:game = $null
    foreach ($p in Get-TestGameProcesses @($ClientPath, $launchPath)) { $p.Kill(); $p.WaitForExit(15000) | Out-Null }
    Start-Sleep -Seconds 3
}
function Launch-UntilLogged([string]$Pattern, [int]$TimeoutSeconds = 180) {
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    $script:game = Start-TestGame $launchPath
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((ReadShared $log) -match $Pattern) { return $true }
        if ($script:game.HasExited) { return $false }
        Start-Sleep -Seconds 2
    }
    return $false
}

# ---------------------------------------------------------------- checks before
Assert-TestInstall $ClientPath
$pluginDir = Join-Path $ClientPath 'BepInEx\plugins\GK2Coop'
$installed = Join-Path $pluginDir 'GK2Coop.dll'
$configPath = Join-Path $ClientPath 'BepInEx\config\com.fabio.gk2coop.cfg'
$log = Join-Path $ClientPath 'BepInEx\LogOutput.log'
if (Test-Path -LiteralPath (Join-Path $ClientPath 'BepInEx\plugins\GameplayProbe.dll')) { throw "A probe is installed in $ClientPath (a test is running or was interrupted)." }
if ((Get-TestGameProcesses @($ClientPath)).Count -gt 0) { throw "The test copy $ClientPath is running." }
if (Test-Path -LiteralPath $library) { throw "$library is left over from an earlier run; look at it and move it away first." }
if (-not (Test-Path -LiteralPath $release)) { throw "No built plugin at $release" }
$pinned = [uint64](Select-String -LiteralPath $updaterSource -Pattern 'PublishedItemId = (\d+)').Matches[0].Groups[1].Value
if ($Mode -eq 'Library' -and $pinned -eq 0) { throw 'No Workshop item is pinned in CoopWorkshopUpdater.PublishedItemId; use -Mode Override.' }
$releaseHash = (Get-FileHash -LiteralPath $release).Hash
$current = [Reflection.AssemblyName]::GetAssemblyName($release).Version
if ($current.Build -ge 99) { throw "The stand-in version would not be newer than $current." }
$currentVersion = $current.ToString(3)
$futureVersion = '{0}.{1}.99' -f $current.Major, $current.Minor

New-Item -ItemType Directory -Force -Path $OutputPath, $testSaves | Out-Null
"Mode $Mode; built plugin $currentVersion SHA-256 $releaseHash; pinned item $pinned; stand-in $futureVersion" | Set-Content -LiteralPath (Join-Path $OutputPath 'setup.txt')
$saveHashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $realSaveFolder -File | Where-Object { $_.Name -notlike 'GK2Coop_*' -and $_.Extension -in '.dat', '.info' }) { $saveHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash }
$prefs = Save-GamePrefs
$configBackup = [IO.File]::ReadAllBytes($configPath)
Copy-Item -LiteralPath $installed -Destination (Join-Path $OutputPath 'GK2Coop-before.dll')
if (Test-Path -LiteralPath $log) { Move-Item -LiteralPath $log -Destination (Join-Path $OutputPath 'log-before.txt') -Force }

if ($Mode -eq 'Library') {
    $launchPath = Join-Path $library 'steamapps\common\Graveyard Keeper 2'
    $future = Join-Path $library "steamapps\workshop\content\4358690\$pinned\BepInEx\plugins\GK2Coop"
} else {
    $launchPath = $ClientPath
    $future = Join-Path $OutputPath 'workshop\BepInEx\plugins\GK2Coop'
}

try {
    # The stand-in "newer Workshop download": the same source built with a higher version, in its
    # own intermediate folder so the normal build output is left as it is.
    dotnet build $project -c Release -v q --nologo "-p:Version=$futureVersion" "-p:GamePath=$ClientPath" "-p:IntermediateOutputPath=$OutputPath\obj\" -o $future | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Stand-in build failed.' }
    Get-ChildItem -LiteralPath $future | Where-Object { $_.Name -ne 'GK2Coop.dll' } | Remove-Item -Force
    Check ((Get-FileHash -LiteralPath $release).Hash -eq $releaseHash) 'Building the stand-in left the built plugin as it was'
    $futureHash = (Get-FileHash -LiteralPath (Join-Path $future 'GK2Coop.dll')).Hash
    if ($Mode -eq 'Library') {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $launchPath) | Out-Null
        New-Item -ItemType Junction -Path $launchPath -Target $ClientPath | Out-Null
    }

    Copy-Item -LiteralPath $release -Destination $installed -Force
    $config = [Text.Encoding]::UTF8.GetString($configBackup).TrimStart([char]0xFEFF)
    $config = Set-Key $config 'Session' 'StartupMode' 'None'
    $config = Set-Key $config 'Testing' 'AutoStartNewGame' 'false'
    $config = Set-Key $config 'Updates' 'AutoUpdateFromWorkshop' 'true'
    $config = Set-Key $config 'Updates' 'WorkshopItemId' '0'
    $config = Set-Key $config 'Testing' 'WorkshopFolderOverride' $(if ($Mode -eq 'Override') { $future } else { '' })
    [IO.File]::WriteAllText($configPath, $config)
    $env:GK2COOP_TEST_SAVE_FOLDER = $testSaves

    # First launch: the updater should install the stand-in and keep running the old code.
    $ok = Launch-UntilLogged 'Workshop update: (installed|running|no |no Workshop)|Workshop update failed'
    Start-Sleep -Seconds 3
    Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath 'first-launch.log') -Force
    $text = ReadShared $log
    $line = ([regex]::Matches($text, 'Workshop update[^\r\n]*') | ForEach-Object { $_.Value }) -join ' | '
    # In Library mode there is no override and WorkshopItemId is 0: only the pinned id leads there.
    $how = if ($Mode -eq 'Library') { "found through the pinned item $pinned" } else { 'found through the folder override' }
    Check ($ok -and $text -match [regex]::Escape("Workshop update: installed $futureVersion over $currentVersion")) "First launch installs $futureVersion over $currentVersion, $how ($line)"
    Stop-Game
    Check ((Get-FileHash -LiteralPath $installed).Hash -eq $futureHash) 'The plugin folder now holds the Workshop build, byte for byte'
    Check ((Test-Path -LiteralPath "$installed.old") -and (Get-FileHash -LiteralPath "$installed.old").Hash -eq $releaseHash) 'The replaced build was moved aside, not lost'

    # Second launch: the new build is the one running, it sees itself as current, and tidies up.
    $ok = Launch-UntilLogged 'Workshop update: running'
    Start-Sleep -Seconds 3
    Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath 'second-launch.log') -Force
    # "running" is the loaded assembly's own version; Plugin.Version is a constant and would not change.
    Check ($ok -and (ReadShared $log) -match [regex]::Escape("Workshop update: running $futureVersion, Workshop has $futureVersion; up to date")) "Relaunch runs the $futureVersion assembly, sees itself as up to date and copies nothing"
    Stop-Game
    Check (-not (Test-Path -LiteralPath "$installed.old")) 'The moved-aside build is removed on the next start'
}
finally {
    Stop-Game
    [Environment]::SetEnvironmentVariable('GK2COOP_TEST_SAVE_FOLDER', $null)
    if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath 'log.txt') -Force }
    Copy-Item -LiteralPath (Join-Path $OutputPath 'GK2Coop-before.dll') -Destination $installed -Force
    Remove-Item -LiteralPath "$installed.old" -Force -ErrorAction SilentlyContinue
    [IO.File]::WriteAllBytes($configPath, $configBackup)
    Write-Host ('client config restored: ' + ([Convert]::ToBase64String([IO.File]::ReadAllBytes($configPath)) -eq [Convert]::ToBase64String($configBackup)))
    Write-Host ('client plugin restored: ' + ((Get-FileHash -LiteralPath $installed).Hash -eq (Get-FileHash -LiteralPath (Join-Path $OutputPath 'GK2Coop-before.dll')).Hash))
    if ($Mode -eq 'Library' -and (Test-Path -LiteralPath $library)) {
        # Remove the junction itself (never its target), then keep the rest of the layout with the run.
        if (Test-Path -LiteralPath $launchPath) { [IO.Directory]::Delete($launchPath, $false) }
        if (Test-Path -LiteralPath $launchPath) { Write-Host "The junction $launchPath is still there; remove it by hand (rmdir, not a recursive delete)." }
        else { Move-Item -LiteralPath $library -Destination (Join-Path $OutputPath 'library') }
    }
    Write-Host "built plugin unchanged: $((Get-FileHash -LiteralPath $release).Hash -eq $releaseHash)"
    Write-Host "Game preferences restored: $(Restore-GamePrefs $prefs) value(s)"
    $changed = @($saveHashes.Keys | Where-Object { (Get-FileHash -LiteralPath (Join-Path $realSaveFolder $_)).Hash -ne $saveHashes[$_] })
    $left = @(Get-ChildItem -LiteralPath $realSaveFolder -Filter 'GK2Coop_*' -File -ErrorAction SilentlyContinue)
    Write-Host "Your saves: $($saveHashes.Count - $changed.Count) unchanged$(if ($changed) { ', changed: ' + ($changed -join ', ') }); GK2Coop_ slots in your save folder: $($left.Count)"
}
if ($report | Where-Object { $_ -like 'FAIL *' }) { throw 'Workshop update verification failed; see results.txt.' }
