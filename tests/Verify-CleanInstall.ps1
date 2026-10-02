<#
.SYNOPSIS
    Gate G7 on this PC: the released zip into a game without any mod, then the uninstall the
    install guide describes. Works on D:\GK2Coop-FullClean, a fresh copy of a test copy without
    the loader and the mod (never the Steam install).

.DESCRIPTION
    1. D:\GK2Coop-FullClean from the source copy, without BepInEx and the loader files (an older
       FullClean is moved to the output folder, not deleted).
    2. The zip extracted into it; every file of the zip where the zip puts it.
    3. The game starts: BepInEx loads the plugin (its version), the co-op menu is on the main menu,
       no error from the mod. Saves go to D:\GK2Coop-Saves (the mod's test save folder).
    4. Uninstall step 1 from the guide (BepInEx\plugins\GK2Coop): the game starts, the loader runs,
       the mod does not.
    5. Uninstall step 2 (BepInEx, winhttp.dll, doorstop_config.ini, .doorstop_version,
       changelog.txt): the game starts and runs without the loader; the folder holds exactly the
       files of the game without a mod again.
    Removed files are moved to the output folder. The player's saves are hashed before and after;
    the game's registry preferences are put back. Without the mod the game cannot be pointed at a
    test save folder, so it only goes as far as its main menu.
#>
[CmdletBinding()]
param(
    [string]$Zip = 'C:\FF\graveyard-keeper-2-coop\artifacts\GK2Coop-0.1.0-dev.zip',
    [string]$Source = 'D:\GK2Coop-FullHost2',
    [string]$Clean = 'D:\GK2Coop-FullClean',
    [string]$OutputPath = ('D:\GK2Coop-Artifacts\clean-install-' + (Get-Date -Format 'yyyyMMdd-HHmm'))
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameplayProbe\TestSafety.ps1')
$realSaveFolder = Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2'
$loaderFiles = 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt'
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

function Check([bool]$passed, [string]$label) {
    $line = $(if ($passed) { 'PASS ' } else { 'FAIL ' }) + $label
    Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Encoding UTF8 -Value $line
    Write-Host $line
    if (-not $passed) { throw $line }
}
function Files([string]$root) { @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($root.Length + 1) } | Sort-Object) }
function ReadShared([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $stream = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try { (New-Object IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Close() }
}
function MoveAside([string]$relative) {
    $from = Join-Path $Clean $relative
    if (-not (Test-Path -LiteralPath $from)) { return }
    $to = Join-Path $OutputPath ('removed\' + $relative)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $to) | Out-Null
    Move-Item -LiteralPath $from -Destination $to
}
# Starts the copy, waits until $until says it is far enough (or $seconds pass), ends it.
function RunGame([scriptblock]$until, [int]$seconds, [string]$what) {
    $process = Start-TestGame $Clean
    $deadline = (Get-Date).AddSeconds($seconds)
    $reached = $false
    try {
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 3
            if ($process.HasExited) { break }
            if (& $until) { $reached = $true; break }
        }
        $alive = -not $process.HasExited
    }
    finally {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(15000) | Out-Null }
    }
    return [pscustomobject]@{ Reached = $reached; Alive = $alive; What = $what }
}

Assert-TestInstall $Source
if ([IO.Path]::GetFullPath($Clean).TrimEnd('\') -notlike 'D:\GK2Coop-Full*') { throw "Not a test copy path: $Clean" }
if ((Get-TestGameProcesses @($Source, $Clean)).Count -gt 0) { throw 'The source or the clean copy is running.' }
if (-not (Test-Path -LiteralPath $Zip)) { throw "No zip at $Zip" }

$saveHashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $realSaveFolder -File | Where-Object { $_.Name -notlike 'GK2Coop_*' -and $_.Extension -in '.dat', '.info' }) { $saveHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash }
$prefs = Save-GamePrefs
$env:GK2COOP_TEST_SAVE_FOLDER = Join-Path 'D:\GK2Coop-Saves' 'FullClean'
New-Item -ItemType Directory -Force -Path $env:GK2COOP_TEST_SAVE_FOLDER | Out-Null
try {
    # ---------------------------------------------------------------- a game without any mod
    if (Test-Path -LiteralPath $Clean) { Move-Item -LiteralPath $Clean -Destination (Join-Path $OutputPath 'previous-FullClean') }
    robocopy $Source $Clean /E /NFL /NDL /NJH /NJS /NP /XD (Join-Path $Source 'BepInEx') /XF $loaderFiles | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
    $global:LASTEXITCODE = 0   # robocopy's 1 means "copied"
    $vanilla = Files $Clean
    Check (-not (Test-Path -LiteralPath (Join-Path $Clean 'BepInEx')) -and -not (Test-Path -LiteralPath (Join-Path $Clean 'winhttp.dll'))) "A game copy without any mod ($($vanilla.Count) files)"

    # ---------------------------------------------------------------- install from the zip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    $entries = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName.Replace('/', '\') })
    $archive.Dispose()
    Expand-Archive -LiteralPath $Zip -DestinationPath $Clean
    $missing = @($entries | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Clean $_)) })
    Check ($missing.Count -eq 0) "Extracted next to GraveyardKeeper2.exe: all $($entries.Count) files of the zip where the zip puts them$(if ($missing) { '; missing: ' + ($missing -join ', ') })"
    $dll = Join-Path $Clean 'BepInEx\plugins\GK2Coop\GK2Coop.dll'
    $version = (Get-Item -LiteralPath $dll).VersionInfo.ProductVersion
    Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Encoding UTF8 -Value "zip $Zip; plugin $version MD5 $((Get-FileHash -LiteralPath $dll -Algorithm MD5).Hash)"

    $log = Join-Path $Clean 'BepInEx\LogOutput.log'
    $run = RunGame { (ReadShared $log).Contains('Co-op button added to the main menu') } 240 'installed'
    $text = ReadShared $log
    Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath 'log-installed.txt')
    $loaded = [regex]::Match($text, 'Loading \[Graveyard Keeper 2 Co-op[^\]]*\]').Value
    $errors = @(($text -split "`n") | Where-Object { $_ -match '^\[(Error|Fatal)\s*:Graveyard Keeper 2 Co-op' -or $_ -match 'GK2Coop.*Exception' })
    Check ($run.Reached -and $loaded -match [regex]::Escape($version)) "The game starts with the mod: BepInEx loads it ($loaded) and puts its Co-op button in the main menu"
    Check ($errors.Count -eq 0) "No error from the mod while starting$(if ($errors) { ': ' + ($errors | Select-Object -First 3) -join ' | ' })"

    # ---------------------------------------------------------------- uninstall, step 1: the mod
    MoveAside 'BepInEx\plugins\GK2Coop'
    Move-Item -LiteralPath $log -Destination (Join-Path $OutputPath 'log-installed-final.txt') -Force
    $run = RunGame { (ReadShared $log).Contains('Chainloader startup complete') } 180 'mod removed'
    Start-Sleep -Seconds 2
    $text = ReadShared $log
    Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath 'log-mod-removed.txt') -ErrorAction SilentlyContinue
    Check ($run.Reached -and $text -notmatch 'Graveyard Keeper 2 Co-op') "Without BepInEx\plugins\GK2Coop the game starts, the loader runs and the mod does not"

    # ---------------------------------------------------------------- uninstall, step 2: the loader
    MoveAside 'BepInEx'
    foreach ($file in $loaderFiles) { MoveAside $file }
    # The guide's last uninstall line: the guides themselves.
    MoveAside 'INSTALL-GK2COOP.txt'; MoveAside 'GK2Coop-Guides'
    $run = RunGame { $false } 60 'loader removed'
    Check ($run.Alive -and -not (Test-Path -LiteralPath (Join-Path $Clean 'BepInEx'))) "Without the loader the game starts and runs (60 s, no BepInEx folder written)"
    $after = Files $Clean
    $extra = @($after | Where-Object { $_ -notin $vanilla })
    $gone = @($vanilla | Where-Object { $_ -notin $after })
    Check ($extra.Count -eq 0 -and $gone.Count -eq 0) "After uninstalling, the folder is the game without a mod again$(if ($extra) { '; left: ' + ($extra -join ', ') })$(if ($gone) { '; missing: ' + ($gone -join ', ') })"
}
finally {
    [Environment]::SetEnvironmentVariable('GK2COOP_TEST_SAVE_FOLDER', $null)
    Write-Host "Game preferences restored: $(Restore-GamePrefs $prefs) value(s)"
    $changed = @($saveHashes.Keys | Where-Object { -not (Test-Path -LiteralPath (Join-Path $realSaveFolder $_)) -or (Get-FileHash -LiteralPath (Join-Path $realSaveFolder $_)).Hash -ne $saveHashes[$_] })
    $left = @(Get-ChildItem -LiteralPath $realSaveFolder -Filter 'GK2Coop_*' -File -ErrorAction SilentlyContinue)
    $line = "Your saves: $($saveHashes.Count - $changed.Count) unchanged$(if ($changed) { ', changed: ' + ($changed -join ', ') }); GK2Coop_ slots in your save folder: $($left.Count)"
    Add-Content -LiteralPath (Join-Path $OutputPath 'results.txt') -Encoding UTF8 -Value $line
    Write-Host $line
}
