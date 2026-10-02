[CmdletBinding()]
param(
    [string]$VMName = 'GK2Coop-Test',
    [string]$GuestKit = 'C:\FF\graveyard-keeper-2-coop\artifacts\GK2Coop-Guest-Kit.zip',
    [string]$GuestDestination = 'C:\Users\Public\Desktop\GK2Coop-Guest-Kit.zip',
    [int]$Port = 8889,
    [string]$LogPath = 'D:\ISO\GK2Coop-Guest-Prep.log'
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
        '-VMName', "`"$VMName`"", '-GuestKit', "`"$GuestKit`"",
        '-GuestDestination', "`"$GuestDestination`"", '-Port', $Port, '-LogPath', "`"$LogPath`""
    )
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments
    return
}

Import-Module Hyper-V -ErrorAction Stop
Start-Transcript -LiteralPath $LogPath -Force | Out-Null
$failure = $null
try {
if (-not (Test-Path -LiteralPath $GuestKit -PathType Leaf)) { throw "Guest kit not found: $GuestKit" }
$vm = Get-VM -Name $VMName -ErrorAction Stop
if ($vm.State -ne 'Running') { throw "VM '$VMName' must be running; current state: $($vm.State)." }

$ruleName = 'GK2Coop UDP 8889'
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol UDP -LocalPort $Port -Profile Any | Out-Null
}

$integrationServices = @(Get-VMIntegrationService -VMName $VMName)
Write-Host ($integrationServices | Format-Table Name, Enabled, PrimaryStatusDescription -AutoSize | Out-String)
$guestService = $integrationServices |
    Where-Object { $_.Name -match 'Guest Service Interface|Gastdienst|Gastschnittstelle' } |
    Select-Object -First 1
if (-not $guestService) {
    $disabledServices = @($integrationServices | Where-Object { -not $_.Enabled })
    if ($disabledServices.Count -eq 1) { $guestService = $disabledServices[0] }
}
if (-not $guestService) { throw 'Hyper-V Guest Service Interface was not found.' }
if (-not $guestService.Enabled) { Enable-VMIntegrationService -VMIntegrationService $guestService }

$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Seconds 2
    $guestService = Get-VMIntegrationService -VMName $VMName | Where-Object { $_.Name -eq $guestService.Name } | Select-Object -First 1
} until ($guestService.PrimaryStatusDescription -eq 'OK' -or (Get-Date) -ge $deadline)

$copyError = $null
for ($attempt = 1; $attempt -le 8; $attempt++) {
    try {
        Copy-VMFile -Name $VMName -SourcePath $GuestKit -DestinationPath $GuestDestination -FileSource Host -CreateFullPath -Force
        $copyError = $null
        break
    }
    catch {
        $copyError = $_
        if ($attempt -eq 8) { break }
        Write-Host "Guest file-copy service is not ready (attempt $attempt/8); retrying in 5 seconds..."
        Start-Sleep -Seconds 5
    }
}
if ($copyError) { throw $copyError }

Write-Host "Copied guest kit to $GuestDestination"
Write-Host "Enabled inbound UDP $Port on the host."
}
catch {
    $failure = $_
    Write-Host ('PREP_ERROR=' + ($_ | Out-String)) -ForegroundColor Red
}
finally {
    Stop-Transcript | Out-Null
}

if ($failure) {
    Read-Host 'Guest preparation failed. Press Enter to close this window'
    throw $failure
}
