[CmdletBinding()]
param(
    [string]$VMName = 'GK2Coop-Test',
    [string]$GuestUser = 'Fabio'
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-VMName', "`"$VMName`"", '-GuestUser', "`"$GuestUser`"")
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments
    return
}

Import-Module Hyper-V -ErrorAction Stop
$source = Join-Path $PSScriptRoot 'Install-GK2CoopInGuest.ps1'
$destination = "C:\Users\$GuestUser\Desktop\Install-GK2CoopInGuest.ps1"
Copy-VMFile -Name $VMName -SourcePath $source -DestinationPath $destination -FileSource Host -CreateFullPath -Force
Write-Host "Repaired guest installer: $destination"
