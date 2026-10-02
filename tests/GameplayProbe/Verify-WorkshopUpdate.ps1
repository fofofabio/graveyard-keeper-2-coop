[CmdletBinding()]
param(
    [string]$ClientPath = 'D:\GK2Coop-FullClient',
    [Parameter(Mandatory)][string]$OutputPath
)

# Gate for CoopWorkshopUpdater. A build with a higher version stands in for a newer Workshop
# download; the updater is pointed at it through WorkshopFolderOverride, so no Steam item is
# needed. Runs on the client clone only, alone, with co-op off. Checks real files on disk and the
# version the relaunched game actually loaded, not only the updater's own log line.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $projectRoot 'src\GK2Coop\GK2Coop.csproj'
$exe = Join-Path $ClientPath 'GraveyardKeeper2.exe'
$processName = 'GraveyardKeeper2'
$pluginDir = Join-Path $ClientPath 'BepInEx\plugins\GK2Coop'
$installed = Join-Path $pluginDir 'GK2Coop.dll'
$configPath = Join-Path $ClientPath 'BepInEx\config\com.fabio.gk2coop.cfg'
$log = Join-Path $ClientPath 'BepInEx\LogOutput.log'
$futureVersion = '0.25.99'
$report = [Collections.Generic.List[string]]::new()

function Check([bool]$Passed, [string]$Label) {
    $line = $(if ($Passed) { 'PASS ' } else { 'FAIL ' }) + $Label
    $report.Add($line); Write-Host $line
    [IO.File]::WriteAllLines((Join-Path $OutputPath 'results.txt'), $report)
}
function Stop-Game {
    Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$ClientPath*" } | Stop-Process -Force
    Start-Sleep -Seconds 3
}
function Launch-UntilLogged([string]$Pattern, [int]$TimeoutSeconds = 120) {
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    Start-Process -FilePath $exe -WorkingDirectory $ClientPath -WindowStyle Hidden
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -Pattern $Pattern -Quiet)) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}
function Set-Key([string]$Text, [string]$Section, [string]$Key, [string]$Value) {
    if ($Text -match "(?m)^$Key\s*=") { return [regex]::Replace($Text, "(?m)^$Key\s*=.*$", "$Key = $Value") }
    if ($Text -match "(?m)^\[$Section\][ \t\r]*$") {
        return ([regex]"(?m)^\[$Section\][ \t\r]*$").Replace($Text, "[$Section]`r`n$Key = $Value`r", 1)
    }
    return $Text + "`r`n[$Section]`r`n$Key = $Value`r`n"
}

if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -gt 0) { throw 'The game is running. Close it first.' }
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$restore = Join-Path $OutputPath 'restore'
New-Item -ItemType Directory -Force -Path $restore | Out-Null
$configBackup = if (Test-Path -LiteralPath $configPath) { [IO.File]::ReadAllBytes($configPath) } else { $null }
$future = Join-Path $OutputPath 'workshop\BepInEx\plugins\GK2Coop'
$manifestPath = Join-Path $projectRoot 'artifacts\deployment-manifest.json'
$manifestBackup = if (Test-Path -LiteralPath $manifestPath) { [IO.File]::ReadAllBytes($manifestPath) } else { $null }

try {
    # The stand-in "newer Workshop download": the same source built with a higher version.
    dotnet build $project -c Release -v q --nologo "-p:Version=$futureVersion" -o $future | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Future build failed.' }
    Get-ChildItem -LiteralPath $future | Where-Object { $_.Name -ne 'GK2Coop.dll' } | Remove-Item -Force
    # Rebuild the real version last, so the normal output is not left at the stand-in version.
    dotnet build $project -c Release -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    & (Join-Path $projectRoot 'scripts\Install-Mod.ps1') -GamePath $ClientPath -Configuration Release | Out-Null
    $currentHash = (Get-FileHash -LiteralPath $installed).Hash
    $futureHash = (Get-FileHash -LiteralPath (Join-Path $future 'GK2Coop.dll')).Hash
    $currentVersion = [Reflection.AssemblyName]::GetAssemblyName($installed).Version.ToString(3)

    $config = if ($configBackup) { [Text.Encoding]::UTF8.GetString($configBackup).TrimStart([char]0xFEFF) } else { '' }
    $config = Set-Key $config 'Session' 'StartupMode' 'None'
    $config = Set-Key $config 'Testing' 'AutoStartNewGame' 'false'
    $config = Set-Key $config 'Testing' 'WorkshopFolderOverride' $future
    [IO.File]::WriteAllText($configPath, $config)

    # First launch: the updater should install the stand-in and keep running the old code.
    $ok = Launch-UntilLogged 'Workshop update: installed|Workshop update failed'
    Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath 'first-launch.log') -Force
    Check ($ok -and (Select-String -LiteralPath $log -Pattern "Workshop update: installed $futureVersion over $currentVersion" -Quiet)) "First launch reports installing $futureVersion over $currentVersion"
    Stop-Game
    Check ((Get-FileHash -LiteralPath $installed).Hash -eq $futureHash) 'The plugin folder now holds the Workshop build, byte for byte'
    Check ((Test-Path -LiteralPath "$installed.old") -and (Get-FileHash -LiteralPath "$installed.old").Hash -eq $currentHash) 'The replaced build was moved aside, not lost'

    # Second launch: the new build is the one running, it sees itself as current, and tidies up.
    $ok = Launch-UntilLogged 'Workshop update: running'
    Start-Sleep -Seconds 3
    Copy-Item -LiteralPath $log -Destination (Join-Path $OutputPath 'second-launch.log') -Force
    # "running" is the loaded assembly's own version; Plugin.Version is a constant and would not change.
    Check ($ok -and (Select-String -LiteralPath $log -Pattern "Workshop update: running $futureVersion, Workshop has $futureVersion; up to date" -Quiet)) "Relaunch runs the $futureVersion assembly, sees itself as up to date and copies nothing"
    Stop-Game
    Check (-not (Test-Path -LiteralPath "$installed.old")) 'The moved-aside build is removed on the next start'
}
finally {
    Stop-Game
    & (Join-Path $projectRoot 'scripts\Install-Mod.ps1') -GamePath $ClientPath -Configuration Release | Out-Null
    Remove-Item -LiteralPath "$installed.old" -Force -ErrorAction SilentlyContinue
    if ($null -ne $configBackup) {
        [IO.File]::WriteAllBytes($configPath, $configBackup)
        Write-Host ('client config restored: ' + ([Convert]::ToBase64String([IO.File]::ReadAllBytes($configPath)) -eq [Convert]::ToBase64String($configBackup)))
    }
    if ($null -ne $manifestBackup) { [IO.File]::WriteAllBytes($manifestPath, $manifestBackup) }
    $release = Join-Path $projectRoot 'src\GK2Coop\bin\Release\net472\GK2Coop.dll'
    Write-Host ('client plugin restored to the release build: ' + ((Get-FileHash -LiteralPath $installed).Hash -eq (Get-FileHash -LiteralPath $release).Hash))
}
if ($report | Where-Object { $_ -like 'FAIL *' }) { throw 'Workshop update verification failed; see results.txt.' }
