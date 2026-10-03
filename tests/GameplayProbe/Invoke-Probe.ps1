[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Host','Client','Third','Fourth')][string]$Peer,
    [Parameter(Mandatory)][string]$Command,
    [int]$TimeoutSeconds = 15
)
$ErrorActionPreference = 'Stop'
$install = if ($Peer -eq 'Third') { $env:GK2COOP_TEST_THIRD_PATH } elseif ($Peer -eq 'Fourth') { $env:GK2COOP_TEST_FOURTH_PATH } elseif ($Peer -eq 'Host') { if ($env:GK2COOP_TEST_HOST_PATH) { $env:GK2COOP_TEST_HOST_PATH } else { 'D:\GK2Coop-FullHost' } } else { if ($env:GK2COOP_TEST_CLIENT_PATH) { $env:GK2COOP_TEST_CLIENT_PATH } else { 'D:\GK2Coop-FullClient' } }
$commandPath = Join-Path $install 'BepInEx\GK2Coop.Probe.command'
if (Test-Path -LiteralPath $commandPath) { throw 'A previous probe command is still pending.' }
$resultPath = $commandPath + '.result'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
# Rename publishes a complete command, so the main thread never reads a partial write.
[IO.File]::WriteAllText($commandPath + '.tmp', $Command)
Move-Item -LiteralPath ($commandPath + '.tmp') -Destination $commandPath
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if (Test-Path -LiteralPath $resultPath) {
        # The game may still hold the file open while writing it; try again shortly.
        try { $result = [IO.File]::ReadAllText($resultPath) } catch { Start-Sleep -Milliseconds 200; continue }
        if ($result.StartsWith($Command + "`n")) { return $result }
    }
    Start-Sleep -Milliseconds 250
}
throw "Probe timed out on $Peer"
