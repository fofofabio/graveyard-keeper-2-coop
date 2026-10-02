[CmdletBinding()]
param(
    [string]$VMName = 'GK2Coop-Test',
    [string]$DestinationRoot = 'C:\Users\Public\Desktop\GK2Coop-Guest-Kit',
    [string]$LogPath = 'D:\ISO\GK2Coop-Guest-Update.log',
    [string]$PlayerName = 'Player 2',
    [switch]$CopyOnly,
    [switch]$StartGame
)

# One step instead of three: copy the current build into the guest, then run the installer
# inside it over PowerShell Direct, which needs no guest networking.
#
# Guest credentials are entered by you at the standard Windows prompt and are used only for
# this run. They are never written to disk or passed on the command line. Use -CopyOnly to
# skip the remote install and run Install-GK2Coop.cmd by hand as before.

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
                   '-VMName', "`"$VMName`"", '-DestinationRoot', "`"$DestinationRoot`"",
                   '-LogPath', "`"$LogPath`"", '-PlayerName', "`"$PlayerName`"")
    if ($CopyOnly) { $arguments += '-CopyOnly' }
    if ($StartGame) { $arguments += '-StartGame' }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments
    return
}

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sources = @(
    @{ Source = Join-Path $projectRoot 'artifacts\GK2Coop-0.1.0-dev.zip'; Name = 'GK2Coop-0.1.0-dev.zip' },
    @{ Source = Join-Path $PSScriptRoot 'Install-GK2CoopInGuest.ps1'; Name = 'Install-GK2CoopInGuest.ps1' },
    @{ Source = Join-Path $PSScriptRoot 'Send-GK2LogToHost.ps1'; Name = 'Send-GK2LogToHost.ps1' }
)
$temporaryFiles = @()
foreach ($pair in @(
    @{ File = 'Install-GK2Coop.cmd'; Target = 'Install-GK2CoopInGuest.ps1' },
    @{ File = 'Send-GK2Log.cmd';     Target = 'Send-GK2LogToHost.ps1' }
)) {
    $path = Join-Path ([IO.Path]::GetTempPath()) $pair.File
    "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0$($pair.Target)`"`r`nif errorlevel 1 pause`r`n" |
        Set-Content -LiteralPath $path -Encoding Ascii
    $sources += @{ Source = $path; Name = $pair.File }
    $temporaryFiles += $path
}

$pluginPath = Join-Path $projectRoot 'src\GK2Coop\bin\Release\net472\GK2Coop.dll'
$expectedVersion = if (Test-Path -LiteralPath $pluginPath) {
    [Reflection.AssemblyName]::GetAssemblyName($pluginPath).Version.ToString()
} else { 'unknown' }

Start-Transcript -LiteralPath $LogPath -Force | Out-Null
try {
    Import-Module Hyper-V -ErrorAction Stop

    $vm = Get-VM -Name $VMName -ErrorAction Stop
    if ($vm.State -ne 'Running') {
        throw "VM '$VMName' is $($vm.State). Start it before updating."
    }

    # A failed Build-Package leaves an older zip in place, which then deploys silently and the
    # next test reports on a build that was never shipped. Refuse rather than mislead.
    $packagePath = Join-Path $projectRoot 'artifacts\GK2Coop-0.1.0-dev.zip'
    if ((Test-Path -LiteralPath $packagePath) -and (Test-Path -LiteralPath $pluginPath)) {
        if ((Get-Item $packagePath).LastWriteTimeUtc -lt (Get-Item $pluginPath).LastWriteTimeUtc) {
            throw ("The package is older than the built plugin ($expectedVersion). " +
                   "Run scripts\Build-Package.ps1 with the game closed, then deploy again.")
        }
    }

    foreach ($file in $sources) {
        if (-not (Test-Path -LiteralPath $file.Source)) {
            throw "Missing source file: $($file.Source)"
        }
        Copy-VMFile -Name $VMName -SourcePath $file.Source -DestinationPath "$DestinationRoot\$($file.Name)" `
                    -FileSource Host -CreateFullPath -Force
        Write-Host "Copied: $($file.Name)"
    }
    Write-Host "Host build is version $expectedVersion."

    if ($CopyOnly) {
        Write-Host 'Copy only. In the VM, run Install-GK2Coop.cmd to finish.'
        Write-Host 'GUEST_UPDATE_EXIT=0'
        return
    }

    Write-Host ''
    Write-Host "Enter the Windows credentials for the guest '$VMName' (used for this run only)."
    $credential = Get-Credential -Message "Guest account on $VMName"

    $result = Invoke-Command -VMName $VMName -Credential $credential -ArgumentList $DestinationRoot, $PlayerName -ScriptBlock {
        param($kitRoot, $playerName)
        $installer = Join-Path $kitRoot 'Install-GK2CoopInGuest.ps1'
        if (-not (Test-Path -LiteralPath $installer)) { throw "Installer not found at $installer" }
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -PlayerName $playerName 2>&1
    }
    $result | ForEach-Object { Write-Host "  [guest] $_" }

    if ($StartGame) {
        Invoke-Command -VMName $VMName -Credential $credential -ScriptBlock {
            $exe = @(foreach ($name in @('GraveyardKeeper2.exe','GraveyardKeeper2Demo.exe')) {
                Get-ChildItem -Path 'C:\','D:\' -Filter $name -Recurse -ErrorAction SilentlyContinue |
                    Select-Object -First 1 -ExpandProperty FullName
            }) | Select-Object -First 1
            if ($exe) { Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden }
            else { 'Could not locate Graveyard Keeper 2 in the guest.' }
        } | ForEach-Object { Write-Host "  [guest] $_" }
    }

    Write-Host 'GUEST_UPDATE_EXIT=0'
}
catch {
    Write-Host ('GUEST_UPDATE_ERROR=' + ($_ | Out-String)) -ForegroundColor Red
    Read-Host 'Guest update failed. Press Enter to close this window'
    throw
}
finally {
    Stop-Transcript | Out-Null
    foreach ($path in $temporaryFiles) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
}
