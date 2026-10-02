<#
    Copies the joiner's test copy of the game (D:\GK2Coop-FullClient by default) into the test VM
    (C:\GK2Coop\Game) through the guest agent. Run it while that copy is not running: a running
    game holds some of its files. -ModOnly sends just the mod's DLL (after a new build).
#>
param(
    [string]$Source = 'D:\GK2Coop-FullClient',
    [switch]$ModOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'VmGuest.ps1')
. (Join-Path (Split-Path $PSScriptRoot) 'GameplayProbe\TestSafety.ps1')
if ((Get-TestGameProcesses @($Source)).Count -gt 0) { throw "$Source is running; copy it when it is closed." }
if ((Invoke-VmGuestText 'GET' '/ping') -match 'games=\d') { throw 'The game is running in the VM; close it first.' }

if ($ModOnly) {
    Send-VmFile (Join-Path $Source 'BepInEx\plugins\GK2Coop\GK2Coop.dll') 'Game\BepInEx\plugins\GK2Coop\GK2Coop.dll'
    Write-Host "Sent the mod ($((Get-Item (Join-Path $Source 'BepInEx\plugins\GK2Coop\GK2Coop.dll')).VersionInfo.ProductVersion))."
    return
}
$skip = '^(BepInEx\\LogOutput\.log|BepInEx\\cache\\.*|BepInEx\\GK2Coop\.Probe\.command.*|BepInEx\\plugins\\GameplayProbe\.dll|.*\.bak)$'
$files = @(Get-ChildItem -LiteralPath $Source -Recurse -File | Where-Object { $_.FullName.Substring($Source.Length + 1) -notmatch $skip })
$total = ($files | Measure-Object Length -Sum).Sum
$done = 0; $n = 0; $started = Get-Date
foreach ($file in $files) {
    Send-VmFile $file.FullName (Join-Path 'Game' $file.FullName.Substring($Source.Length + 1))
    $done += $file.Length; $n++
    if ($n % 200 -eq 0) { Write-Host ("{0:N0} of {1:N0} MB" -f ($done / 1MB), ($total / 1MB)) }
}
Write-Host ("Copied {0} files, {1:N0} MB in {2:N1} min." -f $n, ($total / 1MB), ((Get-Date) - $started).TotalMinutes)
