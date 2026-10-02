# Runs inside the Hyper-V guest. Unpacks the full-game test copy once, installs the newest
# GK2Coop.dll the host copied in, and starts the game. The copy runs without Steam.
$ErrorActionPreference = 'Stop'
$zip = 'C:\Users\Public\GK2Coop-GuestGame.zip'
$game = 'C:\GK2Coop-Game'
$newDll = 'C:\Users\Public\GK2Coop.dll'

if (-not (Test-Path -LiteralPath (Join-Path $game 'GraveyardKeeper2.exe'))) {
    Write-Host 'Unpacking the game (about 1 GB, one minute or two)...'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $game)
}
if (Test-Path -LiteralPath $newDll) {
    Copy-Item -LiteralPath $newDll -Destination (Join-Path $game 'BepInEx\plugins\GK2Coop\GK2Coop.dll') -Force
}
$installed = Join-Path $game 'BepInEx\plugins\GK2Coop\GK2Coop.dll'
Write-Host ('GK2Coop ' + [Reflection.AssemblyName]::GetAssemblyName($installed).Version + ', SHA-256 ' + (Get-FileHash -LiteralPath $installed).Hash)
Write-Host 'Starting the game. On the main menu: Co-op (top left) > Join a game > Copy host world and join.'
Start-Process -FilePath (Join-Path $game 'GraveyardKeeper2.exe') -WorkingDirectory $game
