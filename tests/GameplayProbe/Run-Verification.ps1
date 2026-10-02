[CmdletBinding()]
param(
    [string]$HostPath = 'C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2',
    [string]$ClientPath = 'D:\GK2Coop-FullClient',
    [Parameter(Mandatory)][string]$OutputPath
)

# One command for the whole live gameplay verification, so the manual checklist in README.md
# cannot be half-performed. Everything this touches on the user's machine is captured first and
# restored in the finally block, including after a failing gate or a mid-run error.
#
# The probe is a temporary test plugin. It is installed for this run only and removed again.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$manifestPath = Join-Path $projectRoot 'artifacts\deployment-manifest.json'
$probeDll = Join-Path $PSScriptRoot 'bin\Release\net472\GameplayProbe.dll'
$processName = if (Test-Path -LiteralPath (Join-Path $HostPath 'GraveyardKeeper2.exe')) { 'GraveyardKeeper2' } else { 'GraveyardKeeper2Demo' }
$env:GK2COOP_TEST_HOST_PATH = $HostPath
$env:GK2COOP_TEST_CLIENT_PATH = $ClientPath

$peers = @(
    [pscustomobject]@{ Name = 'host';   Path = $HostPath },
    [pscustomobject]@{ Name = 'client'; Path = $ClientPath }
)

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

function Config-Path([string]$Install) { Join-Path $Install 'BepInEx\config\com.fabio.gk2coop.cfg' }
function Probe-Path([string]$Install) { Join-Path $Install 'BepInEx\plugins\GameplayProbe.dll' }

if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'The game is running. Close it before a verification run.'
}
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$backupPath = Join-Path $OutputPath 'restore'
New-Item -ItemType Directory -Force -Path $backupPath | Out-Null

# Captured before anything is modified, so a restore can never write back a test config.
foreach ($peer in $peers) {
    $config = Config-Path $peer.Path
    if (Test-Path -LiteralPath $config) {
        Copy-Item -LiteralPath $config -Destination (Join-Path $backupPath "$($peer.Name)-config.cfg") -Force
    }
    if (Test-Path -LiteralPath (Probe-Path $peer.Path)) {
        throw "A probe DLL is already installed in $($peer.Path). Clean it up before running."
    }
}
if (Test-Path -LiteralPath $manifestPath) {
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $backupPath 'deployment-manifest.json') -Force
}

try {
    dotnet build (Join-Path $projectRoot 'src\GK2Coop\GK2Coop.csproj') -c Release -v q --nologo -p:GamePath="$HostPath"
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    dotnet build (Join-Path $PSScriptRoot 'GameplayProbe.csproj') -c Release -v q --nologo -p:GamePath="$HostPath"
    if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }

    # Installed here rather than by the harness, so the probe can be placed alongside the mod
    # before either process starts; BepInEx only loads plugins present at startup.
    $install = Join-Path $projectRoot 'scripts\Install-Mod.ps1'
    & $install -GamePath $ClientPath -Configuration Release | Out-Null
    & $install -GamePath $HostPath -Configuration Release | Out-Null
    foreach ($peer in $peers) { Copy-Item -LiteralPath $probeDll -Destination (Probe-Path $peer.Path) -Force }

    & (Join-Path $projectRoot 'scripts\Run-LocalCoopTest.ps1') `
        -HostPath $HostPath -ClientPath $ClientPath -SkipInstall -KeepRunning

    & (Join-Path $PSScriptRoot 'Verify-Live.ps1') -OutputPath $OutputPath
}
finally {
    foreach ($peer in $peers) {
        $log = Join-Path $peer.Path 'BepInEx\LogOutput.log'
        if (Test-Path -LiteralPath $log) {
            Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath "live-$($peer.Name).log") -Force
        }
    }
    Stop-Instances
    foreach ($peer in $peers) {
        $probe = Probe-Path $peer.Path
        if (Test-Path -LiteralPath $probe) {
            Move-Item -LiteralPath $probe -Destination (Join-Path $OutputPath "$($peer.Name)-GameplayProbe.dll") -Force
        }
        foreach ($leftover in Get-ChildItem -LiteralPath (Join-Path $peer.Path 'BepInEx') -Filter 'GK2Coop.Probe.command*' -ErrorAction SilentlyContinue) {
            Move-Item -LiteralPath $leftover.FullName -Destination (Join-Path $OutputPath "$($peer.Name)-$($leftover.Name)") -Force
        }
        $backup = Join-Path $backupPath "$($peer.Name)-config.cfg"
        if (Test-Path -LiteralPath $backup) { Copy-Item -LiteralPath $backup -Destination (Config-Path $peer.Path) -Force }
    }
    $manifestBackup = Join-Path $backupPath 'deployment-manifest.json'
    if (Test-Path -LiteralPath $manifestBackup) { Copy-Item -LiteralPath $manifestBackup -Destination $manifestPath -Force }

    # Restoration is asserted, not assumed: a silent failure here leaves the user's own install
    # with AutoStartNewGame enabled, which skips the menu and the intro on their next session.
    foreach ($peer in $peers) {
        $backup = Join-Path $backupPath "$($peer.Name)-config.cfg"
        if (Test-Path -LiteralPath $backup) {
            $same = (Get-FileHash -Algorithm SHA256 -LiteralPath (Config-Path $peer.Path)).Hash -eq (Get-FileHash -Algorithm SHA256 -LiteralPath $backup).Hash
            Write-Host ("{0} config restored: {1}" -f $peer.Name, $same)
            if (-not $same) { Write-Warning "$($peer.Name) config did NOT restore; compare against $backup." }
        }
        if (Test-Path -LiteralPath (Probe-Path $peer.Path)) { Write-Warning "Probe DLL still present in $($peer.Path)." }
    }
}
