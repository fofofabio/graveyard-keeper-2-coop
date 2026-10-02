<#
    Run once inside the test VM, in PowerShell as administrator, after the host has copied the kit
    to C:\GK2Coop\agent (tests\vm\Push-GuestKit.ps1 does that):
        powershell -ExecutionPolicy Bypass -File C:\GK2Coop\agent\Install-GuestAgent.ps1

    It lets the agent listen on port 8766 for the logged-in user, opens that port in the VM's
    firewall for the Hyper-V network only, and starts the agent at every logon of this user (in
    their session, so the game it starts is visible and has the GPU). Undo with -Remove.
#>
param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$user = "$env:USERDOMAIN\$env:USERNAME"
$port = 8766
$task = 'GK2Coop Guest Agent'
if ($Remove) {
    netsh http delete urlacl url=http://+:$port/ | Out-Null
    Remove-NetFirewallRule -DisplayName 'GK2Coop Guest Agent' -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host 'Removed.'
    return
}
if (-not (Test-Path 'C:\GK2Coop\agent\token.txt')) { throw 'C:\GK2Coop\agent\token.txt is missing: the host has not copied the kit yet.' }
netsh http delete urlacl url=http://+:$port/ 2>$null | Out-Null
netsh http add urlacl url=http://+:$port/ user=$user | Out-Null
# Only the VM's own network: the Hyper-V Default Switch picks a new subnet at times (172.x, then 192.168.x),
# so "LocalSubnet" rather than a fixed range, which locked the host out once.
Remove-NetFirewallRule -DisplayName 'GK2Coop Guest Agent' -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName 'GK2Coop Guest Agent' -Direction Inbound -Protocol TCP -LocalPort $port -RemoteAddress LocalSubnet -Action Allow -Profile Any | Out-Null
# The game's own port, for a host in the VM (the tests' joiner does not need it).
Remove-NetFirewallRule -DisplayName 'GK2Coop Game' -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName 'GK2Coop Game' -Direction Inbound -Protocol UDP -LocalPort 8889,8890 -RemoteAddress LocalSubnet -Action Allow -Profile Any | Out-Null
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\GK2Coop\agent\GuestAgent.ps1'
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Principal $principal -Settings (New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -AllowStartIfOnBatteries) -Force | Out-Null
Start-ScheduledTask -TaskName $task
Write-Host "Agent installed and started for $user on port $port."
