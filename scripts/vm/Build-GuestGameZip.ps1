[CmdletBinding()]
param(
    [string]$SourcePath = 'D:\GK2Coop-FullClient',
    [string]$OutputPath = 'D:\ISO\GK2Coop-GuestGame.zip',
    [string]$HostAddress = '172.22.112.1',
    [string]$PlayerName = 'VM Guest'
)

# Packs the local full-game test copy (game + BepInEx + current GK2Coop build) for the Hyper-V
# guest, which has no licence of its own for the full game. The copy runs without Steam, as the
# local second instance does. Leaves out logs, test probes and this machine's player key, and
# presets the guest's config to join the host over the Hyper-V Default Switch through the menu.

$ErrorActionPreference = 'Stop'
if (Get-Process -Name GraveyardKeeper2 -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$SourcePath*" }) {
    throw 'The test copy is running. Close it first.'
}
$stage = Join-Path ([IO.Path]::GetDirectoryName($OutputPath)) ('GK2Coop-GuestGame-' + [guid]::NewGuid().ToString('N'))
try {
    robocopy $SourcePath $stage /E /NFL /NDL /NJH /NJS /NP /XF LogOutput.log GameplayProbe.dll 'GK2Coop.Probe.command*' '*.old' /XD 'GK2Coop' | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with $LASTEXITCODE" }
    # /XD above skips every folder named GK2Coop (including profile stores); the plugin folder is
    # copied explicitly so only the DLL comes along.
    $plugin = Join-Path $stage 'BepInEx\plugins\GK2Coop'
    New-Item -ItemType Directory -Force -Path $plugin | Out-Null
    Copy-Item -LiteralPath (Join-Path $SourcePath 'BepInEx\plugins\GK2Coop\GK2Coop.dll') -Destination $plugin

    $config = Join-Path $stage 'BepInEx\config\com.fabio.gk2coop.cfg'
    $text = [IO.File]::ReadAllText($config)
    foreach ($pair in @(@('StartupMode', 'None'), @('Address', $HostAddress), @('PlayerName', $PlayerName),
                        @('PlayerKey', ''), @('AutoStartNewGame', 'false'), @('RelaxStartupGate', 'false'),
                        @('WorkshopFolderOverride', ''))) {
        $text = [regex]::Replace($text, "(?m)^$($pair[0])\s*=.*$", "$($pair[0]) = $($pair[1])")
    }
    [IO.File]::WriteAllText($config, $text)

    if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $OutputPath, [IO.Compression.CompressionLevel]::Fastest, $false)
}
finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
$zip = Get-Item -LiteralPath $OutputPath
"{0}: {1:N2} GB, SHA-256 {2}" -f $zip.FullName, ($zip.Length / 1GB), (Get-FileHash -LiteralPath $zip.FullName).Hash
