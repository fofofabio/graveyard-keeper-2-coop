<#
.SYNOPSIS
    Brings the D: test copies to the game version of the Steam install, after a game update.

.DESCRIPTION
    Steam updates only the Steam install; the test copies stay on the old game version, and every
    test then runs on a game the players no longer have (seen on 2 October 2026: Steam had 1.007.1
    since 29 September, the copies 1.004.2). This mirrors GraveyardKeeper2_Data and the files next
    to the exe from the Steam install into each copy. The Steam install is only read. Not touched in
    the copies: BepInEx (the mod, the probe, the configs), the Doorstop files, steam_appid.txt and
    the mod's guides. The old data folder of the first copy is kept once, as
    D:\GK2Coop-GameData-<old version>, so the old version can still be tested.
#>
[CmdletBinding()]
param(
    [string]$SteamGame = 'C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2',
    [string[]]$Copies = @('D:\GK2Coop-FullHost', 'D:\GK2Coop-FullClient', 'D:\GK2Coop-FullHost2', 'D:\GK2Coop-FullClient2', 'D:\GK2Coop-FullClean')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameplayProbe\TestSafety.ps1')

function Get-GameVersion([string]$Path) {
    # The version the game prints at start ("Game: Graveyard Keeper 2 1.007.1") is in the data
    # folder's globalgamemanagers; the main assembly's hash tells two versions apart for sure.
    $asm = Join-Path $Path 'GraveyardKeeper2_Data\Managed\Assembly-CSharp.dll'
    $bytes = [IO.File]::ReadAllBytes((Join-Path $Path 'GraveyardKeeper2_Data\globalgamemanagers'))
    $text = [Text.Encoding]::ASCII.GetString($bytes)
    $m = [regex]::Match($text, '\b1\.\d{3}\.\d+\b')
    [pscustomobject]@{ Version = $(if ($m.Success) { $m.Value } else { '?' }); Hash = (Get-FileHash -LiteralPath $asm).Hash.Substring(0, 8) }
}

if (-not (Test-Path -LiteralPath (Join-Path $SteamGame 'GraveyardKeeper2.exe'))) { throw "No game at $SteamGame" }
foreach ($copy in $Copies) { Assert-TestInstall $copy }
if ((Get-TestGameProcesses $Copies).Count -gt 0) { throw 'A test copy is running; wait for its test to end.' }
foreach ($copy in $Copies) {
    if (Test-Path -LiteralPath (Join-Path $copy 'BepInEx\plugins\GameplayProbe.dll')) { throw "A probe is installed in $copy (a test is running or was interrupted)." }
}
$target = Get-GameVersion $SteamGame
Write-Host "Steam install: game $($target.Version) (Assembly-CSharp $($target.Hash))"

$kept = $false
foreach ($copy in $Copies) {
    $before = Get-GameVersion $copy
    if ($before.Hash -eq $target.Hash) { Write-Host "$copy is already on $($target.Version)"; continue }
    if (-not $kept) {
        $backup = "D:\GK2Coop-GameData-$($before.Version)"
        if (-not (Test-Path -LiteralPath $backup)) {
            robocopy (Join-Path $copy 'GraveyardKeeper2_Data') (Join-Path $backup 'GraveyardKeeper2_Data') /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "Keeping the old data folder failed (robocopy $LASTEXITCODE)." }
            Write-Host "Old data folder ($($before.Version)) kept in $backup"
        }
        $kept = $true
    }
    # The data folder exactly as in the Steam install (old bundles removed).
    robocopy (Join-Path $SteamGame 'GraveyardKeeper2_Data') (Join-Path $copy 'GraveyardKeeper2_Data') /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Updating $copy failed (robocopy $LASTEXITCODE)." }
    # The files next to the exe and the Mono runtime, without the mod's and Doorstop's files.
    robocopy $SteamGame $copy /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XF winhttp.dll doorstop_config.ini .doorstop_version changelog.txt steam_appid.txt INSTALL-GK2COOP.txt LICENSE.txt THIRD-PARTY-NOTICES.txt | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Updating the files next to the exe in $copy failed (robocopy $LASTEXITCODE)." }
    robocopy (Join-Path $SteamGame 'MonoBleedingEdge') (Join-Path $copy 'MonoBleedingEdge') /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Updating the Mono runtime in $copy failed (robocopy $LASTEXITCODE)." }
    $after = Get-GameVersion $copy
    if ($after.Hash -ne $target.Hash) { throw "$copy is not on the Steam version after the update ($($after.Hash))." }
    Write-Host "$copy`: $($before.Version) -> $($after.Version)"
}
