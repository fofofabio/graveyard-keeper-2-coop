[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$IsoPath,

    [string]$VMName = 'GK2Coop-Test',

    [string]$VMRoot = 'D:\VMs\GK2Coop-Test',

    [string]$SwitchName = 'Default Switch',

    [ValidateRange(2GB, 16GB)]
    [long]$StartupMemory = 6GB,

    [ValidateRange(2, 6)]
    [int]$ProcessorCount = 4,

    [ValidateRange(64GB, 160GB)]
    [long]$DiskSize = 96GB,

    [switch]$Start
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this provisioning script from an elevated PowerShell window (Run as administrator).'
}

Import-Module Hyper-V -ErrorAction Stop
$resolvedIso = (Resolve-Path -LiteralPath $IsoPath -ErrorAction Stop).Path
if ([IO.Path]::GetExtension($resolvedIso) -ne '.iso') {
    throw "Expected a Windows ISO file: $resolvedIso"
}
if (Get-VM -Name $VMName -ErrorAction SilentlyContinue) {
    throw "A Hyper-V VM named '$VMName' already exists. This script will not overwrite it."
}
if (-not (Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue)) {
    throw "Hyper-V switch '$SwitchName' does not exist."
}

$resolvedRoot = [IO.Path]::GetFullPath($VMRoot)
$vhdPath = Join-Path $resolvedRoot "$VMName.vhdx"
if (Test-Path -LiteralPath $resolvedRoot) {
    $existing = Get-ChildItem -LiteralPath $resolvedRoot -Force -ErrorAction Stop
    if ($existing.Count -ne 0) {
        throw "The VM directory is not empty. Refusing to reuse it: $resolvedRoot"
    }
}
New-Item -ItemType Directory -Force -Path $resolvedRoot | Out-Null

try {
    New-VHD -Path $vhdPath -Dynamic -SizeBytes $DiskSize | Out-Null
    $vm = New-VM -Name $VMName -Generation 2 -MemoryStartupBytes $StartupMemory -VHDPath $vhdPath -Path $resolvedRoot -SwitchName $SwitchName
    Set-VM -VM $vm -ProcessorCount $ProcessorCount -DynamicMemory -MemoryMinimumBytes 4GB -MemoryMaximumBytes 10GB -AutomaticCheckpointsEnabled $false -AutomaticStopAction ShutDown
    Set-VMProcessor -VMName $VMName -ExposeVirtualizationExtensions $false
    Set-VMFirmware -VMName $VMName -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows
    Set-VMKeyProtector -VMName $VMName -NewLocalKeyProtector
    Enable-VMTPM -VMName $VMName
    $dvd = Add-VMDvdDrive -VMName $VMName -Path $resolvedIso -Passthru
    Set-VMFirmware -VMName $VMName -FirstBootDevice $dvd

    $hostAddress = Get-NetIPAddress -InterfaceAlias 'vEthernet (Default Switch)' -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty IPAddress
    Write-Host "Created Hyper-V VM '$VMName' at $resolvedRoot"
    Write-Host "Resources: $ProcessorCount vCPU, $([math]::Round($StartupMemory / 1GB, 1)) GB startup RAM, $([math]::Round($DiskSize / 1GB)) GB dynamic disk"
    Write-Host "Network: $SwitchName; current host-side address: $hostAddress"
    Write-Host 'Install Windows, Hyper-V integration updates, Steam, the demo, then extract GK2Coop-0.1.0-dev.zip into the game folder.'

    if ($Start) {
        Start-VM -Name $VMName | Out-Null
        Start-Process -FilePath "$env:SystemRoot\System32\vmconnect.exe" -ArgumentList 'localhost', $VMName
    }
}
catch {
    $createdVM = Get-VM -Name $VMName -ErrorAction SilentlyContinue
    if ($createdVM) {
        Stop-VM -VM $createdVM -TurnOff -Force -ErrorAction SilentlyContinue
        Remove-VM -VM $createdVM -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $vhdPath -PathType Leaf) {
        Remove-Item -LiteralPath $vhdPath -Force -ErrorAction SilentlyContinue
    }
    throw
}
