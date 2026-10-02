[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'artifacts\GK2Coop-Guest-Kit.zip')
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packagePath = Join-Path $projectRoot 'artifacts\GK2Coop-0.1.0-dev.zip'
$installerPath = Join-Path $PSScriptRoot 'Install-GK2CoopInGuest.ps1'
$senderPath = Join-Path $PSScriptRoot 'Send-GK2LogToHost.ps1'
$staging = Join-Path ([IO.Path]::GetTempPath()) ('GK2Coop-Guest-Kit-' + [guid]::NewGuid().ToString('N'))

try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    Copy-Item -LiteralPath $packagePath, $installerPath, $senderPath -Destination $staging
    @'
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-GK2CoopInGuest.ps1"
if errorlevel 1 pause
'@ | Set-Content -LiteralPath (Join-Path $staging 'Install-GK2Coop.cmd') -Encoding Ascii
    @'
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Send-GK2LogToHost.ps1"
if errorlevel 1 pause
'@ | Set-Content -LiteralPath (Join-Path $staging 'Send-GK2Log.cmd') -Encoding Ascii
    @'
Install Steam and the full Graveyard Keeper 2 game first.

Then double-click Install-GK2Coop.cmd, approve UAC, and launch the game after the host is in game.

To send this machine's BepInEx log back to the host, run Send-GK2Log.cmd while the host is
listening (Receive-GK2-Guest-Log.cmd on the host). No UAC needed.
The installer automatically locates the Steam library and uses the Hyper-V default gateway as the host address.
'@ | Set-Content -LiteralPath (Join-Path $staging 'README.txt')
    if (Test-Path -LiteralPath $OutputPath) {
        $backupDir = Join-Path $projectRoot 'artifacts\package-backups'
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
        $oldHash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.Substring(0, 12)
        Copy-Item -LiteralPath $OutputPath -Destination (Join-Path $backupDir ("$(Split-Path -Leaf $OutputPath).$oldHash.bak")) -Force
        Remove-Item -LiteralPath $OutputPath -Force
    }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $OutputPath -CompressionLevel Optimal
    Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}
