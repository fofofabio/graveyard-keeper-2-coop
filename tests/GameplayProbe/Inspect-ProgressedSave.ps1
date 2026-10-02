[CmdletBinding()]
param(
    [string]$SaveBackup = 'C:\FF\graveyard-keeper-2-coop\artifacts\save-backups\full-release-20260923-071217',
    [string]$GamePath = 'D:\GK2Coop-FullClient',
    [string]$OutputPath = 'C:\FF\graveyard-keeper-2-coop\artifacts\progressed-save-0.23.0'
)

# Loads an isolated copy of a real save through the game's Continue button. Only the
# temporary slot name can be written; the user's Steam_1 files are verified afterward.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$saveFolder = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2')
$slot = 'GK2Coop_Test_' + (Get-Date -Format 'yyyyMMddHHmmss')
$testDat = Join-Path $saveFolder ($slot + '.dat')
$testInfo = Join-Path $saveFolder ($slot + '.info')
$probePath = Join-Path $GamePath 'BepInEx\plugins\GameplayProbe.dll'
$configPath = Join-Path $GamePath 'BepInEx\config\com.fabio.gk2coop.cfg'
$manifestPath = Join-Path $root 'artifacts\deployment-manifest.json'
$sourceDat = Join-Path $SaveBackup 'Steam_1.dat'
$sourceInfo = Join-Path $SaveBackup 'Steam_1.info'
$gameProcess = $null

if (Get-Process -Name GraveyardKeeper2 -ErrorAction SilentlyContinue) { throw 'Close the game before the progressed-save test.' }
if (-not (Test-Path -LiteralPath $sourceDat) -or -not (Test-Path -LiteralPath $sourceInfo)) { throw 'The backed-up save pair is missing.' }
if ((Test-Path -LiteralPath $testDat) -or (Test-Path -LiteralPath $testInfo)) { throw 'The isolated slot name already exists.' }
if (Test-Path -LiteralPath $probePath) { throw 'A gameplay probe is already installed in the clone.' }

New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
Copy-Item -LiteralPath $configPath -Destination (Join-Path $OutputPath 'client-config-before.cfg')
if (Test-Path -LiteralPath $manifestPath) { Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $OutputPath 'deployment-manifest-before.json') }

try {
    dotnet build (Join-Path $PSScriptRoot 'GameplayProbe.csproj') -c Release -v q --nologo -p:GamePath="$GamePath"
    if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\net472\GameplayProbe.dll') -Destination $probePath

    Copy-Item -LiteralPath $sourceDat -Destination $testDat
    $info = Get-Content -LiteralPath $sourceInfo -Raw
    $newDate = (Get-Date).AddMinutes(2).ToString('dd.MM.yyyy HH:mm:ss')
    $info = [regex]::Replace($info, '"saveDateTime":"[^"]+"', '"saveDateTime":"' + $newDate + '"')
    [IO.File]::WriteAllText($testInfo, $info)
    if ((Get-FileHash $testDat).Hash -ne (Get-FileHash $sourceDat).Hash) { throw 'Test save copy does not match the backup.' }

    $config = Get-Content -LiteralPath $configPath -Raw
    foreach ($pair in @(@('StartupMode','None'),@('AutoStartNewGame','false'),@('RelaxStartupGate','false'))) {
        $config = [regex]::Replace($config, "(?m)^$($pair[0])\s*=.*$", "$($pair[0]) = $($pair[1])")
    }
    [IO.File]::WriteAllText($configPath, $config)
    $env:GK2COOP_TEST_CONTINUE_SLOT = $slot
    $exe = Join-Path $GamePath 'GraveyardKeeper2.exe'
    $gameProcess = Start-Process -FilePath $exe -WorkingDirectory $GamePath -WindowStyle Hidden -PassThru
    Remove-Item Env:\GK2COOP_TEST_CONTINUE_SLOT
    Write-Host "Clone launched with isolated save slot $slot."

    $logPath = Join-Path $GamePath 'BepInEx\LogOutput.log'
    $deadline = (Get-Date).AddSeconds(240)
    $continued = $false
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $logPath) -and (Select-String -LiteralPath $logPath -Pattern "Invoked the game's Continue button for isolated slot $slot" -Quiet)) {
            $continued = $true
            break
        }
        if ($gameProcess.HasExited) { throw 'The clone exited before loading the isolated save.' }
        Start-Sleep -Seconds 3
    }
    if (-not $continued) { throw 'The isolated save was not selected on the main menu.' }

    $env:GK2COOP_TEST_CLIENT_PATH = $GamePath
    $inspect = Join-Path $PSScriptRoot 'Invoke-Probe.ps1'
    $loaded = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $progress = & $inspect -Peer Client -Command progress -TimeoutSeconds 5
            if ($progress -match '(?m)^SAVE version=') {
                [IO.File]::WriteAllText((Join-Path $OutputPath 'progress.txt'), $progress)
                $state = & $inspect -Peer Client -Command inspect -TimeoutSeconds 5
                [IO.File]::WriteAllText((Join-Path $OutputPath 'inspect.txt'), $state)
                $loaded = $true
                break
            }
        }
        catch { }
        Start-Sleep -Seconds 4
    }
    if (-not $loaded) { throw 'The continued save did not reach a playable world.' }
    Write-Host $progress
}
finally {
    Remove-Item Env:\GK2COOP_TEST_CONTINUE_SLOT,Env:\GK2COOP_TEST_CLIENT_PATH -ErrorAction SilentlyContinue
    if ($gameProcess -and -not $gameProcess.HasExited) { $gameProcess.Kill(); $gameProcess.WaitForExit(10000) | Out-Null }
    $logPath = Join-Path $GamePath 'BepInEx\LogOutput.log'
    if (Test-Path -LiteralPath $logPath) { Copy-Item -LiteralPath $logPath -Destination (Join-Path $OutputPath 'client.log') -Force }
    if (Test-Path -LiteralPath $probePath) { Move-Item -LiteralPath $probePath -Destination (Join-Path $OutputPath 'GameplayProbe.dll') -Force }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $GamePath 'BepInEx') -Filter 'GK2Coop.Probe.command*' -ErrorAction SilentlyContinue) {
        Move-Item -LiteralPath $file.FullName -Destination (Join-Path $OutputPath $file.Name) -Force
    }
    foreach ($suffix in @('.dat','.info','_backup_1.dat','_backup_1.info','_backup_2.dat','_backup_2.info','_backup_3.dat','_backup_3.info')) {
        $file = Join-Path $saveFolder ($slot + $suffix)
        if (Test-Path -LiteralPath $file) {
            Copy-Item -LiteralPath $file -Destination (Join-Path $OutputPath ($slot + $suffix)) -Force
            Remove-Item -LiteralPath $file -Force
        }
    }
    Copy-Item -LiteralPath (Join-Path $OutputPath 'client-config-before.cfg') -Destination $configPath -Force
    $savedManifest = Join-Path $OutputPath 'deployment-manifest-before.json'
    if (Test-Path -LiteralPath $savedManifest) { Copy-Item -LiteralPath $savedManifest -Destination $manifestPath -Force }
    foreach ($file in @('Steam_1.dat','Steam_1.info')) {
        $current = Join-Path $saveFolder $file
        $original = Join-Path $SaveBackup $file
        $same = (Get-FileHash $current).Hash -eq (Get-FileHash $original).Hash
        Write-Host "$file untouched: $same"
        if (-not $same) { Write-Warning "$file changed during the test; the verified backup is at $SaveBackup" }
    }
    Write-Host "Temporary save slot removed; evidence in $OutputPath"
}
