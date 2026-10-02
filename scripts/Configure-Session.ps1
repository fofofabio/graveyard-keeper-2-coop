[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('None', 'Host', 'Connect')]
    [string]$Mode,

    [string]$Address = '127.0.0.1',

    [ValidateRange(1, 65535)]
    [int]$Port = 8889,

    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Graveyard Keeper 2 Demo',

    [switch]$EnableMovementProbe
)

$ErrorActionPreference = 'Stop'
$configPath = Join-Path $GamePath 'BepInEx\config\com.fabio.gk2coop.cfg'
$executablePath = Join-Path $GamePath 'GraveyardKeeper2Demo.exe'

if (Get-Process -Name 'GraveyardKeeper2Demo' -ErrorAction SilentlyContinue) {
    throw 'Close every Graveyard Keeper 2 Demo instance before changing the session configuration.'
}
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "The demo executable was not found at $executablePath"
}
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "The GK2Coop configuration was not found. Launch the installed mod once, then run this script again: $configPath"
}

$configText = Get-Content -LiteralPath $configPath -Raw
$requiredKeys = @('Address', 'Port', 'StartupMode', 'MovementProbe')
foreach ($key in $requiredKeys) {
    if ($configText -notmatch "(?m)^$([regex]::Escape($key))\s*=") {
        throw "The configuration is missing the expected key '$key': $configPath"
    }
}

$probeValue = if ($EnableMovementProbe) { 'true' } else { 'false' }
$configText = [regex]::Replace($configText, '(?m)^Address\s*=.*$', "Address = $Address")
$configText = [regex]::Replace($configText, '(?m)^Port\s*=.*$', "Port = $Port")
$configText = [regex]::Replace($configText, '(?m)^StartupMode\s*=.*$', "StartupMode = $Mode")
$configText = [regex]::Replace($configText, '(?m)^MovementProbe\s*=.*$', "MovementProbe = $probeValue")
Set-Content -LiteralPath $configPath -Value $configText

Write-Host "Configured GK2Coop: Mode=$Mode Address=$Address Port=$Port MovementProbe=$probeValue"
Write-Host "Config: $configPath"
