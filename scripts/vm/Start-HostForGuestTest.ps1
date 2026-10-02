[CmdletBinding()]
param(
    [string]$SaveBackup = 'C:\FF\graveyard-keeper-2-coop\artifacts\save-backups\full-release-20260923-071217',
    [string]$HostPath = 'C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2',
    [string]$StatePath = 'C:\FF\graveyard-keeper-2-coop\artifacts\guest-test-state',
    # Run again with -Stop to close the host and put everything back.
    [switch]$Stop
)

# Host half of the two-machine test with the Hyper-V guest. Starts the real game in Host mode on
# a temporary copy of the backed-up day-18 save, never the live Steam_1 slot, using the test
# probe's Continue hook. -Stop closes it, restores the config and deployment manifest, removes the
# probe, and moves the temporary slot and any stored guest profiles into $StatePath.

$ErrorActionPreference = 'Stop'
$root = 'C:\FF\graveyard-keeper-2-coop'
$saveFolder = Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2'
$config = Join-Path $HostPath 'BepInEx\config\com.fabio.gk2coop.cfg'
$probe = Join-Path $HostPath 'BepInEx\plugins\GameplayProbe.dll'
$manifest = Join-Path $root 'artifacts\deployment-manifest.json'
$stateFile = Join-Path $StatePath 'state.txt'

if ($Stop) {
    $slot = (Get-Content -LiteralPath $stateFile -ErrorAction Stop | Select-Object -First 1)
    Get-Process -Name GraveyardKeeper2 -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$HostPath*" } | Stop-Process -Force
    Start-Sleep -Seconds 3
    Copy-Item -LiteralPath (Join-Path $HostPath 'BepInEx\LogOutput.log') -Destination (Join-Path $StatePath 'host-log.txt') -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath (Join-Path $StatePath 'config-before.cfg') -Destination $config -Force
    if (Test-Path -LiteralPath (Join-Path $StatePath 'manifest-before.json')) { Copy-Item -LiteralPath (Join-Path $StatePath 'manifest-before.json') -Destination $manifest -Force }
    if (Test-Path -LiteralPath $probe) { Move-Item -LiteralPath $probe -Destination (Join-Path $StatePath 'GameplayProbe.dll') -Force }
    foreach ($file in Get-ChildItem -LiteralPath $saveFolder -Filter "$slot*" -File) { Move-Item -LiteralPath $file.FullName -Destination $StatePath -Force }
    $profiles = Join-Path $HostPath "BepInEx\config\GK2Coop\players\$slot"
    if (Test-Path -LiteralPath $profiles) { Move-Item -LiteralPath $profiles -Destination (Join-Path $StatePath 'guest-profiles') -Force }
    $same = (Get-FileHash $config).Hash -eq (Get-FileHash (Join-Path $StatePath 'config-before.cfg')).Hash
    "Host stopped. Config restored: $same. Steam_1.dat untouched: " +
        ((Get-FileHash (Join-Path $saveFolder 'Steam_1.dat')).Hash -eq (Get-FileHash (Join-Path $SaveBackup 'Steam_1.dat')).Hash)
    return
}

if (Get-Process -Name GraveyardKeeper2 -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$HostPath*" }) { throw 'The game is already running.' }
New-Item -ItemType Directory -Force -Path $StatePath | Out-Null
$slot = 'GK2Coop_Test_Guest' + (Get-Date -Format 'yyyyMMddHHmmss')
Set-Content -LiteralPath $stateFile -Value $slot
Copy-Item -LiteralPath $config -Destination (Join-Path $StatePath 'config-before.cfg') -Force
if (Test-Path -LiteralPath $manifest) { Copy-Item -LiteralPath $manifest -Destination (Join-Path $StatePath 'manifest-before.json') -Force }

dotnet build (Join-Path $root 'tests\GameplayProbe\GameplayProbe.csproj') -c Release -v q --nologo | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'tests\GameplayProbe\bin\Release\net472\GameplayProbe.dll') -Destination $probe -Force
Copy-Item -LiteralPath (Join-Path $SaveBackup 'Steam_1.dat') -Destination (Join-Path $saveFolder "$slot.dat")
$info = Get-Content -LiteralPath (Join-Path $SaveBackup 'Steam_1.info') -Raw
$info = [regex]::Replace($info, '"saveDateTime":"[^"]+"', '"saveDateTime":"' + (Get-Date).AddMinutes(2).ToString('dd.MM.yyyy HH:mm:ss') + '"')
[IO.File]::WriteAllText((Join-Path $saveFolder "$slot.info"), $info)

$text = Get-Content -LiteralPath $config -Raw
foreach ($pair in @(@('StartupMode', 'Host'), @('Address', '0.0.0.0'), @('PlayerName', 'Host'), @('AutoStartNewGame', 'false'), @('RelaxStartupGate', 'false'))) {
    $text = [regex]::Replace($text, "(?m)^$($pair[0])\s*=.*$", "$($pair[0]) = $($pair[1])")
}
[IO.File]::WriteAllText($config, $text)

$env:GK2COOP_TEST_CONTINUE_SLOT = $slot
Start-Process -FilePath (Join-Path $HostPath 'GraveyardKeeper2.exe') -WorkingDirectory $HostPath
Remove-Item Env:\GK2COOP_TEST_CONTINUE_SLOT
"Host starting on temporary slot $slot. Stop it later with: Start-HostForGuestTest.ps1 -Stop"
