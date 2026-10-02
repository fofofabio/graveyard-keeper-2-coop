[CmdletBinding()]
param(
    [string]$IsoPath = 'D:\ISO\Windows11Enterprise-25H2-de-de.iso',
    [string]$LogPath = 'D:\ISO\GK2Coop-VM-Provisioning.log'
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @(
        '-NoProfile'
        '-ExecutionPolicy', 'Bypass'
        '-File', "`"$PSCommandPath`""
        '-IsoPath', "`"$IsoPath`""
        '-LogPath', "`"$LogPath`""
    )
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments
    return
}

$provisioner = Join-Path $PSScriptRoot 'Provision-GK2TestVM.ps1'
$exitCode = 0
Start-Transcript -LiteralPath $LogPath -Force | Out-Null
try {
    & $provisioner -IsoPath $IsoPath -Start
}
catch {
    $exitCode = 1
    Write-Error $_
}
finally {
    Write-Host "PROVISION_EXIT=$exitCode"
    Stop-Transcript | Out-Null
}

if ($exitCode -ne 0) {
    Write-Host ''
    Read-Host 'Provisioning failed. Press Enter to close this window'
}
exit $exitCode
