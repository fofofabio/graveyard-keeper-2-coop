[CmdletBinding()]
param([string]$VMName = 'GK2Coop-Test')

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-VMName', "`"$VMName`"")
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments
    return
}

Import-Module Hyper-V -ErrorAction Stop
$vm = Get-VM -Name $VMName -ErrorAction Stop
$dvd = Get-VMDvdDrive -VMName $VMName -ErrorAction Stop | Where-Object Path | Select-Object -First 1
if (-not $dvd) { throw "No mounted DVD was found for '$VMName'." }

if ($vm.State -ne 'Off') {
    Stop-VM -VM $vm -TurnOff -Force
}
Set-VMFirmware -VMName $VMName -FirstBootDevice $dvd
Start-VM -Name $VMName | Out-Null
Start-Process -FilePath "$env:SystemRoot\System32\vmconnect.exe" -ArgumentList 'localhost', $VMName
