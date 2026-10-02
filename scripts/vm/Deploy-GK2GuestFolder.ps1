[CmdletBinding()]
param(
    [string]$VMName = 'GK2Coop-Test',
    [string]$DestinationRoot = 'C:\Users\Public\Desktop\GK2Coop-Guest-Kit',
    [string]$LogPath = 'D:\ISO\GK2Coop-Guest-Deploy.log'
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-VMName', "`"$VMName`"", '-DestinationRoot', "`"$DestinationRoot`"", '-LogPath', "`"$LogPath`"")
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments
    return
}

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sources = @(
    @{ Source = Join-Path $projectRoot 'artifacts\GK2Coop-0.1.0-dev.zip'; Name = 'GK2Coop-0.1.0-dev.zip' },
    @{ Source = Join-Path $PSScriptRoot 'Install-GK2CoopInGuest.ps1'; Name = 'Install-GK2CoopInGuest.ps1' },
    @{ Source = Join-Path $PSScriptRoot 'Send-GK2LogToHost.ps1'; Name = 'Send-GK2LogToHost.ps1' }
)
$cmdPath = Join-Path ([IO.Path]::GetTempPath()) 'Install-GK2Coop.cmd'
@'
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-GK2CoopInGuest.ps1"
if errorlevel 1 pause
'@ | Set-Content -LiteralPath $cmdPath -Encoding Ascii
$sources += @{ Source = $cmdPath; Name = 'Install-GK2Coop.cmd' }

$sendCmdPath = Join-Path ([IO.Path]::GetTempPath()) 'Send-GK2Log.cmd'
@'
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Send-GK2LogToHost.ps1"
if errorlevel 1 pause
'@ | Set-Content -LiteralPath $sendCmdPath -Encoding Ascii
$sources += @{ Source = $sendCmdPath; Name = 'Send-GK2Log.cmd' }

Start-Transcript -LiteralPath $LogPath -Force | Out-Null
try {
    Import-Module Hyper-V -ErrorAction Stop
    foreach ($file in $sources) {
        $destination = "$DestinationRoot\$($file.Name)"
        Copy-VMFile -Name $VMName -SourcePath $file.Source -DestinationPath $destination -FileSource Host -CreateFullPath -Force
        Write-Host "Copied: $destination"
    }
    Write-Host 'GUEST_DEPLOY_EXIT=0'
}
catch {
    Write-Host ('GUEST_DEPLOY_ERROR=' + ($_ | Out-String)) -ForegroundColor Red
    Read-Host 'Guest deployment failed. Press Enter to close this window'
    throw
}
finally {
    Stop-Transcript | Out-Null
    Remove-Item -LiteralPath $cmdPath, $sendCmdPath -Force -ErrorAction SilentlyContinue
}
