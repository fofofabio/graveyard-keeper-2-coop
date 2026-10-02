[CmdletBinding()]
param(
    [string]$HostAddress,
    [ValidateRange(1, 65535)]
    [int]$Port = 8890,
    [string]$GamePath,
    [string]$LogPath
)

# Runs inside the Hyper-V guest. Pushes the BepInEx log to the host, which is listening via
# scripts\vm\Receive-GuestLog.ps1. Needs no elevation and no guest firewall change, because
# this is an outbound connection.

$ErrorActionPreference = 'Stop'

if (-not $LogPath) {
    if (-not $GamePath) {
        $steamRoots = [System.Collections.Generic.List[string]]::new()
        foreach ($registryPath in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
            $steamPath = (Get-ItemProperty -LiteralPath $registryPath -ErrorAction SilentlyContinue).SteamPath
            if ($steamPath) { $steamRoots.Add(($steamPath -replace '/', '\')) }
        }
        foreach ($steamRoot in @($steamRoots)) {
            $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
            if (Test-Path -LiteralPath $libraryFile) {
                $text = Get-Content -LiteralPath $libraryFile -Raw
                [regex]::Matches($text, '"path"\s+"([^"]+)"') | ForEach-Object {
                    $steamRoots.Add(($_.Groups[1].Value -replace '\\\\', '\'))
                }
            }
        }
        $candidates = foreach ($name in @('Graveyard Keeper 2','Graveyard Keeper 2 Demo')) {
            foreach ($root in ($steamRoots | Select-Object -Unique)) {
                Join-Path $root "steamapps\common\$name"
            }
        }
        $GamePath = $candidates |
            Where-Object { (Test-Path -LiteralPath (Join-Path $_ 'GraveyardKeeper2.exe') -PathType Leaf) -or
                           (Test-Path -LiteralPath (Join-Path $_ 'GraveyardKeeper2Demo.exe') -PathType Leaf) } |
            Select-Object -First 1
    }
    if (-not $GamePath) {
        throw 'Graveyard Keeper 2 was not found. Pass -GamePath explicitly.'
    }
    $LogPath = Join-Path $GamePath 'BepInEx\LogOutput.log'
}

if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
    throw "BepInEx log not found: $LogPath"
}

if (-not $HostAddress) {
    $HostAddress = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.NextHop -ne '0.0.0.0' } |
        Sort-Object RouteMetric |
        Select-Object -First 1 -ExpandProperty NextHop
}
if (-not [Net.IPAddress]::TryParse($HostAddress, [ref]([Net.IPAddress]$null))) {
    throw "Could not determine the host address. Supply -HostAddress explicitly; received '$HostAddress'."
}

# Copy first: the game may still hold the log open.
$temporary = Join-Path $env:TEMP ('gk2coop-log-' + [guid]::NewGuid().ToString('N') + '.log')
Copy-Item -LiteralPath $LogPath -Destination $temporary -Force
try {
    $bytes = [IO.File]::ReadAllBytes($temporary)
    Write-Host "Sending $($bytes.Length) bytes to ${HostAddress}:${Port} ..."
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $client.Connect($HostAddress, $Port)
        $stream = $client.GetStream()
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush()
    }
    finally {
        $client.Close()
    }
    Write-Host 'Sent. The host has saved it under artifacts\guest-logs.'
}
catch {
    Write-Host "Send failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Start the host listener first: scripts\vm\Receive-GuestLog.ps1'
    throw
}
finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
}
