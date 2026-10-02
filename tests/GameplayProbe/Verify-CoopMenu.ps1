[CmdletBinding()]
param(
    [string]$GamePath = 'D:\GK2Coop-LocalClient',
    [Parameter(Mandatory)][string]$OutputPath,
    [int]$MenuSeconds = 70
)

# Checks the main-menu co-op panel without needing anyone to look at the screen.
#
# The panel is IMGUI, so "did it draw" is not observable from outside the process. Instead the
# plugin logs once when it first draws on the menu, and that line plus the absence of drawing
# errors is the evidence. A clone is used rather than the real install so a failure cannot
# disturb the game the user actually plays.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$configPath = Join-Path $GamePath 'BepInEx\config\com.fabio.gk2coop.cfg'
$logPath = Join-Path $GamePath 'BepInEx\LogOutput.log'
$report = [Collections.Generic.List[string]]::new()

function Check([bool]$Passed, [string]$Label) {
    $line = $(if ($Passed) { 'PASS ' } else { 'FAIL ' }) + $Label
    $report.Add($line)
    Write-Host $line
    [IO.File]::WriteAllLines((Join-Path $OutputPath 'results.txt'), $report)
}

if (@(Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'The game is running. Close it first.'
}
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$backup = Join-Path $OutputPath 'restore'
New-Item -ItemType Directory -Force -Path $backup | Out-Null
if (Test-Path -LiteralPath $configPath) { Copy-Item -LiteralPath $configPath -Destination (Join-Path $backup 'config.cfg') -Force }

try {
    dotnet build (Join-Path $projectRoot 'src\GK2Coop\GK2Coop.csproj') -c Release -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    & (Join-Path $projectRoot 'scripts\Install-Mod.ps1') -GamePath $GamePath -Configuration Release | Out-Null

    # Sitting on the menu is the whole point, so nothing may auto-start or auto-connect.
    $config = Get-Content -LiteralPath $configPath -Raw
    foreach ($pair in @(@('StartupMode', 'None'), @('AutoStartNewGame', 'false'), @('ShowCoopMenu', 'true'))) {
        if ($config -match "(?m)^$($pair[0])\s*=") {
            $config = [regex]::Replace($config, "(?m)^$($pair[0])\s*=.*$", "$($pair[0]) = $($pair[1])")
        } else {
            $config += "`r`n$($pair[0]) = $($pair[1])`r`n"
        }
    }
    Set-Content -LiteralPath $configPath -Value $config -Encoding UTF8
    Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue

    Start-Process -FilePath (Join-Path $GamePath 'GraveyardKeeper2Demo.exe') -WorkingDirectory $GamePath
    Write-Host "Launched; leaving it on the menu for $MenuSeconds seconds."
    $deadline = (Get-Date).AddSeconds($MenuSeconds)
    $drew = $false
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $logPath) -and (Select-String -LiteralPath $logPath -Pattern 'Co-op menu is on screen' -Quiet)) {
            $drew = $true
            break
        }
        if (@(Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue).Count -lt 1) { throw 'The game exited on its own.' }
        Start-Sleep -Seconds 3
    }
    # Left running a little longer so a style or layout fault has frames to surface in.
    Start-Sleep -Seconds 12

    Check $drew 'The co-op panel draws on the main menu'
    $disabled = (Test-Path -LiteralPath $logPath) -and (Select-String -LiteralPath $logPath -Pattern 'Co-op menu disabled after a drawing failure' -Quiet)
    Check (-not $disabled) 'The panel draws without a layout or style error'
    $hudDisabled = (Test-Path -LiteralPath $logPath) -and (Select-String -LiteralPath $logPath -Pattern 'Multiplayer HUD disabled' -Quiet)
    Check (-not $hudDisabled) 'The status HUD still draws alongside it'

    # The defect this whole feature exists for: a connection that never succeeds used to leave
    # the player alone in their own world with nothing said. Point the client at an address
    # nobody is listening on and confirm it says so, in words a player can act on.
    Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 3
    $config = Get-Content -LiteralPath $configPath -Raw
    foreach ($pair in @(@('StartupMode', 'Connect'), @('Address', '127.0.0.9'), @('AutoStartNewGame', 'true'),
                        @('RelaxStartupGate', 'true'), @('ConnectRetries', '2'), @('ConnectRetrySeconds', '6'))) {
        if ($config -match "(?m)^$($pair[0])\s*=") {
            $config = [regex]::Replace($config, "(?m)^$($pair[0])\s*=.*$", "$($pair[0]) = $($pair[1])")
        } else {
            $config += "`r`n$($pair[0]) = $($pair[1])`r`n"
        }
    }
    Set-Content -LiteralPath $configPath -Value $config -Encoding UTF8
    Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue

    Start-Process -FilePath (Join-Path $GamePath 'GraveyardKeeper2Demo.exe') -WorkingDirectory $GamePath
    Write-Host 'Launched pointing at an address nobody is listening on.'
    $deadline = (Get-Date).AddSeconds(420)
    $toldThem = $false
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $logPath) -and (Select-String -LiteralPath $logPath -Pattern 'Told the player: Could not join' -Quiet)) {
            $toldThem = $true
            break
        }
        if (@(Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue).Count -lt 1) { throw 'The game exited on its own.' }
        Start-Sleep -Seconds 5
    }
    Check $toldThem 'A connection that never succeeds tells the player, instead of leaving them alone silently'

    if ($toldThem) {
        $told = (Select-String -LiteralPath $logPath -Pattern 'Told the player: Could not join' | Select-Object -Last 1).Line
        [IO.File]::WriteAllText((Join-Path $OutputPath 'failure-message.txt'), $told)
        Check (($told -match '127\.0\.0\.9') -and ($told -match 'port forwarding|Tailscale')) "The message names the address and says what to check: $told"
        $retried = @(Select-String -LiteralPath $logPath -Pattern 'Told the player: No reply from the host; retrying').Count
        Check ($retried -ge 1) "Retries are reported to the player as they happen ($retried shown)"
    }
}
finally {
    if (Test-Path -LiteralPath $logPath) { Copy-Item -LiteralPath $logPath -Destination (Join-Path $OutputPath 'menu.log') -Force }
    Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 3
    $saved = Join-Path $backup 'config.cfg'
    if (Test-Path -LiteralPath $saved) {
        Copy-Item -LiteralPath $saved -Destination $configPath -Force
        $same = (Get-FileHash -Algorithm SHA256 -LiteralPath $configPath).Hash -eq (Get-FileHash -Algorithm SHA256 -LiteralPath $saved).Hash
        Write-Host "config restored: $same"
    }
}
if ($report | Where-Object { $_ -like 'FAIL *' }) { throw 'Co-op menu verification failed; see results.txt.' }
