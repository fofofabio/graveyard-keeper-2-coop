[CmdletBinding()]
param(
    [string]$PackagePath,
    [string]$GamePath,
    [string]$HostAddress,
    [string]$PlayerName,
    [ValidateRange(1, 65535)]
    [int]$Port = 8889
)

$ErrorActionPreference = 'Stop'
if (-not $PackagePath) {
    $PackagePath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'GK2Coop-0.1.0-dev.zip'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-PackagePath', "`"$PackagePath`"", '-Port', $Port)
    if ($GamePath) { $arguments += @('-GamePath', "`"$GamePath`"") }
    if ($HostAddress) { $arguments += @('-HostAddress', "`"$HostAddress`"") }
    if ($PlayerName) { $arguments += @('-PlayerName', "`"$PlayerName`"") }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments
    return
}

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "Mod package not found: $PackagePath"
}

if (-not $GamePath) {
    $steamRoots = [System.Collections.Generic.List[string]]::new()
    foreach ($registryPath in @(
        'HKCU:\Software\Valve\Steam',
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'
    )) {
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

    $installCandidates = foreach ($name in @('Graveyard Keeper 2', 'Graveyard Keeper 2 Demo')) {
        foreach ($root in ($steamRoots | Select-Object -Unique)) {
            Join-Path $root "steamapps\common\$name"
        }
    }
    $GamePath = $installCandidates |
        Where-Object { (Test-Path -LiteralPath (Join-Path $_ 'GraveyardKeeper2.exe') -PathType Leaf) -or
                       (Test-Path -LiteralPath (Join-Path $_ 'GraveyardKeeper2Demo.exe') -PathType Leaf) } |
        Select-Object -First 1
}

if (-not $GamePath) {
    throw 'Graveyard Keeper 2 was not found. Install the full game through Steam or supply -GamePath.'
}
$gameExe = @('GraveyardKeeper2.exe', 'GraveyardKeeper2Demo.exe') |
    ForEach-Object { Join-Path $GamePath $_ } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if (-not $gameExe) { throw "No supported game executable found in $GamePath" }
if (Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($gameExe)) -ErrorAction SilentlyContinue) {
    throw "Close $gameExe before installing the mod."
}

if (-not $HostAddress) {
    $HostAddress = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.NextHop -ne '0.0.0.0' } |
        Sort-Object RouteMetric |
        Select-Object -First 1 -ExpandProperty NextHop
}
if (-not [Net.IPAddress]::TryParse($HostAddress, [ref]([Net.IPAddress]$null))) {
    throw "Could not determine a valid Hyper-V host address. Supply -HostAddress explicitly; received '$HostAddress'."
}

$staging = Join-Path ([IO.Path]::GetTempPath()) ('GK2Coop-install-' + [guid]::NewGuid().ToString('N'))
$backup = Join-Path (Split-Path -Parent $PackagePath) ('GK2Coop-guest-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
try {
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $staging
    foreach ($file in Get-ChildItem -LiteralPath $staging -File -Recurse) {
        $relative = $file.FullName.Substring($staging.Length).TrimStart('\')
        $destination = Join-Path $GamePath $relative
        if (Test-Path -LiteralPath $destination -PathType Leaf) {
            $saved = Join-Path $backup $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $saved) | Out-Null
            Copy-Item -LiteralPath $destination -Destination $saved
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
$configPath = Join-Path $GamePath 'BepInEx\config\com.fabio.gk2coop.cfg'
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "Package did not install the expected config: $configPath"
}

$config = Get-Content -LiteralPath $configPath -Raw
$config = [regex]::Replace($config, '(?m)^Address\s*=.*$', "Address = $HostAddress")
$config = [regex]::Replace($config, '(?m)^Port\s*=.*$', "Port = $Port")
$config = [regex]::Replace($config, '(?m)^StartupMode\s*=.*$', 'StartupMode = Connect')
$config = [regex]::Replace($config, '(?m)^MovementProbe\s*=.*$', 'MovementProbe = false')
if ($PlayerName) {
    $config = [regex]::Replace($config, '(?m)^PlayerName\s*=.*$', "PlayerName = $PlayerName")
}
Set-Content -LiteralPath $configPath -Value $config

$pluginPath = Join-Path $GamePath 'BepInEx\plugins\GK2Coop\GK2Coop.dll'
if (-not (Test-Path -LiteralPath $pluginPath -PathType Leaf)) {
    throw "Package did not install the expected plugin: $pluginPath"
}
$pluginHash = (Get-FileHash -LiteralPath $pluginPath -Algorithm SHA256).Hash
$pluginVersion = [Reflection.AssemblyName]::GetAssemblyName($pluginPath).Version.ToString()

Write-Host "Installed GK2Coop client into: $GamePath"
Write-Host "Plugin assembly version: $pluginVersion"
Write-Host "Plugin SHA256: $pluginHash"
Write-Host "Client target: $HostAddress`:$Port"
Write-Host 'Compare the version and hash above with the host installation before testing.'
Write-Host "Backup of replaced files: $backup"
Write-Host 'Launch the game after the host is already in game.'
