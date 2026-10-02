<#
    Puts the guest agent and its key into the running test VM through Hyper-V's guest service
    (Copy-VMFile), without any login. Then, once, in the VM, in PowerShell as administrator:
        powershell -ExecutionPolicy Bypass -File C:\GK2Coop\agent\Install-GuestAgent.ps1
    Afterwards `.\tests\vm\Push-GuestKit.ps1 -Check` answers PONG when the host can reach it.
#>
param([switch]$Check)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VmGuest.ps1')

if ($Check) {
    Write-Host "VM address: $(Get-VmGuestAddress)"
    Write-Host (Invoke-VmGuestText 'GET' '/ping')
    return
}
$vm = Get-VM -Name $script:VmName
if ($vm.State -ne 'Running') { throw "Start the VM and log in first ($($vm.Name) is $($vm.State))." }
$key = Get-VmGuestKey
$temp = Join-Path $env:TEMP 'gk2coop-guest-token.txt'
[IO.File]::WriteAllText($temp, $key)
try {
    foreach ($pair in @(@((Join-Path $PSScriptRoot 'GuestAgent.ps1'), 'C:\GK2Coop\agent\GuestAgent.ps1'),
                        @((Join-Path $PSScriptRoot 'Install-GuestAgent.ps1'), 'C:\GK2Coop\agent\Install-GuestAgent.ps1'),
                        @($temp, 'C:\GK2Coop\agent\token.txt'))) {
        Copy-VMFile -Name $script:VmName -SourcePath $pair[0] -DestinationPath $pair[1] -FileSource Host -CreateFullPath -Force
        Write-Host "copied $(Split-Path -Leaf $pair[0]) -> $($pair[1])"
    }
}
finally {
    [IO.File]::Delete($temp)
}
Write-Host ''
Write-Host 'Now in the VM, in PowerShell as administrator:'
Write-Host '    powershell -ExecutionPolicy Bypass -File C:\GK2Coop\agent\Install-GuestAgent.ps1'
