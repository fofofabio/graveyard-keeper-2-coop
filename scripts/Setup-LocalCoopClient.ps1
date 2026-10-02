[CmdletBinding()]
param(
    [string]$HostPath = 'C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2',
    [string]$ClientPath = 'D:\GK2Coop-FullClient'
)

# Clones the installed demo so two instances can run side by side on one machine.
#
# The game refuses to start twice from the same directory — the second process exits
# immediately — but two copies in different directories run happily together. That turns a
# two-machine test into a single-machine one for everything that does not need a real second
# GPU or a real network.
#
# The clone is a plain file copy: no Steam involvement, so it runs without Steam auth and logs
# a harmless SteamAPI_Init failure. It is roughly 1.4 GB.

$ErrorActionPreference = 'Stop'

$gameExeName = if (Test-Path -LiteralPath (Join-Path $HostPath 'GraveyardKeeper2.exe')) { 'GraveyardKeeper2.exe' } else { 'GraveyardKeeper2Demo.exe' }
if (-not (Test-Path -LiteralPath (Join-Path $HostPath $gameExeName))) {
    throw "Source install not found: $HostPath"
}
if (Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($gameExeName)) -ErrorAction SilentlyContinue) {
    throw 'Close every Graveyard Keeper 2 instance first.'
}
if (Test-Path -LiteralPath $ClientPath) { throw "Refusing to mirror into an existing folder: $ClientPath" }

$drive = Get-PSDrive -Name (Split-Path -Qualifier $ClientPath).TrimEnd(':') -ErrorAction SilentlyContinue
if ($drive -and $drive.Free -lt 3GB) {
    throw ("Less than 3 GB free on drive " + $drive.Name + ": the clone needs about 1.4 GB.")
}

Write-Host "Cloning $HostPath -> $ClientPath ..."
robocopy $HostPath $ClientPath /MIR /MT:16 /NFL /NDL /NJH /NP /R:1 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "robocopy failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath (Join-Path $ClientPath $gameExeName))) {
    throw 'Clone did not produce a runnable install.'
}

Write-Host "Local co-op client ready at $ClientPath"
Write-Host 'Run scripts\Run-LocalCoopTest.ps1 to drive a two-instance session.'
