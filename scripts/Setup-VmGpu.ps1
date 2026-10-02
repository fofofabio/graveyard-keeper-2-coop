<#
.SYNOPSIS
    Gives the test VM a share of the NVIDIA graphics card (Hyper-V GPU partitioning, GPU-P), so
    Graveyard Keeper 2 can run in it. Run it yourself, as administrator, with the VM's own games
    closed. -DryRun shows every step and changes nothing.

.DESCRIPTION
    Safe for a remote session (Parsec): it never creates or changes a virtual switch, so the host's
    network adapter is never taken over; the VM joins the existing "Default Switch" (NAT) only.
    It also stops the VM from starting with Windows, and makes a checkpoint before anything else.

    Steps: stop the VM -> no automatic start -> checkpoint -> network on the Default Switch ->
    add a GPU partition of the NVIDIA card -> memory-mapped IO for the GPU -> copy the host's
    NVIDIA driver into the VM's disk (HostDriverStore) -> done, the VM stays off.

    After a driver update on this PC, run it again with -DriverOnly (the VM needs the same driver
    version as the host).

.PARAMETER Share
    How much of the card the VM may use, in percent (default 50).
#>
[CmdletBinding()]
param(
    [string]$VMName = 'GK2Coop-Test',
    [ValidateRange(10, 90)][int]$Share = 50,
    [switch]$DryRun,
    [switch]$DriverOnly
)

$ErrorActionPreference = 'Stop'
function Step([string]$text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Do-It([string]$what, [scriptblock]$action) {
    if ($DryRun) { Write-Host "   would: $what" } else { Write-Host "   $what"; & $action }
}

# ---------------------------------------------------------------- checks
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin -and -not $DryRun) { throw 'Run this in PowerShell as administrator (or with -DryRun to only look).' }
$vm = Get-VM -Name $VMName
$gpu = Get-VMHostPartitionableGpu | Where-Object { $_.Name -like '*VEN_10DE*' } | Select-Object -First 1
if (-not $gpu) { throw 'No partitionable NVIDIA GPU found.' }
$card = Get-CimInstance Win32_VideoController | Where-Object { $_.Name -like 'NVIDIA*' } | Select-Object -First 1
$driverFolder = Split-Path (($card.InstalledDisplayDrivers -split ',')[0])
Write-Host "VM:     $($vm.Name) ($($vm.State)), disk $((Get-VMHardDiskDrive -VM $vm | Select-Object -First 1).Path)"
Write-Host "GPU:    $($card.Name), driver $($card.DriverVersion)"
Write-Host "Driver: $driverFolder"
if ($DryRun) { Write-Host 'Dry run: nothing is changed.' -ForegroundColor Yellow }

# ---------------------------------------------------------------- the VM off
Step 'Stop the VM (it must be off for the GPU and the driver copy)'
if ($vm.State -ne 'Off') { Do-It "shut down $VMName" { Stop-VM -VM $vm -Force; while ((Get-VM -Name $VMName).State -ne 'Off') { Start-Sleep -Seconds 2 } } }
else { Write-Host '   already off' }

if (-not $DriverOnly) {
    Step 'Never start with Windows (it cannot take the display or the GPU at boot)'
    Do-It 'automatic start: Nothing; automatic stop: ShutDown' { Set-VM -VM $vm -AutomaticStartAction Nothing -AutomaticStopAction ShutDown }

    Step 'Checkpoint first (a VM with a GPU partition cannot take checkpoints)'
    Do-It "checkpoint 'before GPU-P'" { Checkpoint-VM -VM $vm -SnapshotName ("before GPU-P " + (Get-Date -Format 'yyyy-MM-dd HH-mm')) }

    Step 'Network: the existing Default Switch (NAT) - no switch is created or changed'
    $default = Get-VMSwitch -Name 'Default Switch' -ErrorAction SilentlyContinue
    if (-not $default) { Write-Host '   No Default Switch on this PC: leaving the network as it is. Do not create an External switch while connected remotely.' -ForegroundColor Yellow }
    else {
        foreach ($nic in Get-VMNetworkAdapter -VM $vm) {
            if ($nic.SwitchName -ne 'Default Switch') { Do-It "connect '$($nic.Name)' to the Default Switch" { Connect-VMNetworkAdapter -VMNetworkAdapter $nic -SwitchName 'Default Switch' } }
            else { Write-Host "   '$($nic.Name)' is on the Default Switch" }
        }
    }

    Step "GPU partition: $Share % of the NVIDIA card"
    foreach ($old in Get-VMGpuPartitionAdapter -VM $vm -ErrorAction SilentlyContinue) { Do-It 'remove an earlier GPU partition' { Remove-VMGpuPartitionAdapter -VMGpuPartitionAdapter $old } }
    $part = { param($max) [UInt64]([math]::Floor([double]$max * $Share / 100)) }
    Do-It "add the partition ($($gpu.Name.Substring(0, [math]::Min(40, $gpu.Name.Length)))...)" {
        Add-VMGpuPartitionAdapter -VM $vm -InstancePath $gpu.Name
        Set-VMGpuPartitionAdapter -VM $vm `
            -MinPartitionVRAM (& $part $gpu.MaxPartitionVRAM) -MaxPartitionVRAM (& $part $gpu.MaxPartitionVRAM) -OptimalPartitionVRAM (& $part $gpu.MaxPartitionVRAM) `
            -MinPartitionEncode (& $part $gpu.MaxPartitionEncode) -MaxPartitionEncode (& $part $gpu.MaxPartitionEncode) -OptimalPartitionEncode (& $part $gpu.MaxPartitionEncode) `
            -MinPartitionDecode (& $part $gpu.MaxPartitionDecode) -MaxPartitionDecode (& $part $gpu.MaxPartitionDecode) -OptimalPartitionDecode (& $part $gpu.MaxPartitionDecode) `
            -MinPartitionCompute (& $part $gpu.MaxPartitionCompute) -MaxPartitionCompute (& $part $gpu.MaxPartitionCompute) -OptimalPartitionCompute (& $part $gpu.MaxPartitionCompute)
    }

    Step 'Memory-mapped IO for the GPU'
    Do-It 'guest-controlled cache, 1 GB low / 32 GB high MMIO space' { Set-VM -VM $vm -GuestControlledCacheTypes $true -LowMemoryMappedIoSpace 1GB -HighMemoryMappedIoSpace 32GB }
}

# ---------------------------------------------------------------- the driver into the VM's disk
Step 'Copy the host NVIDIA driver into the VM (HostDriverStore; about 2.6 GB)'
$vhd = (Get-VMHardDiskDrive -VM $vm | Select-Object -First 1).Path
if ($DryRun) {
    Write-Host "   would: mount $vhd, copy $driverFolder to <VM>\Windows\System32\HostDriverStore\FileRepository\, copy C:\Windows\System32\nv*.dll to <VM>\Windows\System32\, dismount"
} else {
    $disk = Mount-VHD -Path $vhd -PassThru | Get-Disk
    try {
        $windows = Get-Partition -DiskNumber $disk.Number | Where-Object { $_.DriveLetter -and (Test-Path "$($_.DriveLetter):\Windows\System32") } | Select-Object -First 1
        if (-not $windows) { throw 'No Windows partition found on the VM disk.' }
        $root = "$($windows.DriveLetter):"
        $store = "$root\Windows\System32\HostDriverStore\FileRepository"
        New-Item -ItemType Directory -Force -Path $store | Out-Null
        Write-Host "   copying the driver to $store ..."
        Copy-Item -LiteralPath $driverFolder -Destination $store -Recurse -Force
        Get-ChildItem C:\Windows\System32 -Filter 'nv*.dll' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination "$root\Windows\System32\" -Force }
        Write-Host '   driver copied'
    }
    finally {
        Dismount-VHD -Path $vhd
    }
}

Step 'Done'
Write-Host '   The VM stays off. Start it with:  Start-VM -Name' $VMName
Write-Host '   Connect with Parsec or RDP into the VM, not the Hyper-V window in full screen.'
Write-Host '   In Hyper-V Manager > Hyper-V Settings > Keyboard: "Use on the physical computer".'
Write-Host '   Inside the VM, Device Manager should show the NVIDIA card; GK2 needs Steam there.'
