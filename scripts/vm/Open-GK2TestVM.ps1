[CmdletBinding()]
param([string]$VMName = 'GK2Coop-Test')

$ErrorActionPreference = 'Stop'
Import-Module Hyper-V -ErrorAction Stop
$vm = Get-VM -Name $VMName -ErrorAction Stop
if ($vm.State -eq 'Off') {
    Start-VM -VM $vm | Out-Null
}
Start-Process -FilePath "$env:SystemRoot\System32\vmconnect.exe" -ArgumentList 'localhost', $VMName
