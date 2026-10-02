[CmdletBinding()]
param(
    [string]$BindAddress = '172.19.80.1',
    [ValidateRange(1, 65535)]
    [int]$Port = 8890,
    [int]$TimeoutSeconds = 300,
    [string]$OutputDirectory
)

# The Hyper-V guest cannot be reached from the host: its firewall classifies the Default
# Switch network as Public and blocks inbound SMB, WinRM and ICMP, Copy-VMFile only moves
# files host-to-guest, and PowerShell Direct needs guest credentials. Outbound guest-to-host
# does work, which is how the game reaches the host on UDP 8889. So the guest pushes and the
# host listens.
#
# Binds only to the Default Switch address, never to every interface, and caps the transfer.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'artifacts\guest-logs'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$maxBytes = 32MB

if (-not (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
          Where-Object { $_.IPAddress -eq $BindAddress })) {
    throw "No local interface holds $BindAddress. Check the Hyper-V Default Switch address."
}

$ruleName = "GK2Coop Log TCP $Port"
$ruleExists = $false
try {
    $ruleExists = [bool](Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)
}
catch {
    # Querying the firewall can fail without elevation; fall through and just warn.
}
if (-not $ruleExists) {
    Write-Warning "No inbound rule '$ruleName' was found. A push from the guest will probably be blocked."
    Write-Warning 'Creating a firewall rule is a security change, so it is left to you. In an elevated PowerShell:'
    Write-Host ""
    Write-Host "  New-NetFirewallRule -DisplayName '$ruleName' -Direction Inbound -Protocol TCP ``"
    Write-Host "    -LocalPort $Port -RemoteAddress 172.19.80.0/20 -Action Allow"
    Write-Host ""
    Write-Host 'That scopes the opening to the Hyper-V Default Switch subnet only. Listening anyway.'
}

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse($BindAddress), $Port)
try {
    $listener.Start()
}
catch {
    throw ("Could not listen on ${BindAddress}:${Port}. If Windows Firewall blocked it, allow " +
           "inbound TCP $Port for PowerShell, or run: New-NetFirewallRule -DisplayName " +
           "'GK2Coop Log TCP $Port' -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow`n" +
           $_.Exception.Message)
}

Write-Host "Listening on ${BindAddress}:${Port} for a guest log push (timeout ${TimeoutSeconds}s)."
Write-Host 'In the VM, run: GK2Coop-Guest-Kit\Send-GK2Log.cmd'

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not $listener.Pending()) {
        if ((Get-Date) -gt $deadline) {
            throw "No guest connected within $TimeoutSeconds seconds."
        }
        Start-Sleep -Milliseconds 250
    }

    $client = $listener.AcceptTcpClient()
    try {
        $remote = $client.Client.RemoteEndPoint.Address.ToString()
        $stream = $client.GetStream()
        $stream.ReadTimeout = 30000

        $memory = [System.IO.MemoryStream]::new()
        $buffer = New-Object byte[] 65536
        while ($true) {
            $read = $stream.Read($buffer, 0, $buffer.Length)
            if ($read -le 0) { break }
            if (($memory.Length + $read) -gt $maxBytes) {
                throw "Guest sent more than $([int]($maxBytes/1MB)) MB; aborting."
            }
            $memory.Write($buffer, 0, $read)
        }

        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $destination = Join-Path $OutputDirectory "guest-LogOutput-$stamp.log"
        [IO.File]::WriteAllBytes($destination, $memory.ToArray())
        Write-Host "Received $($memory.Length) bytes from $remote"
        Write-Host "Saved: $destination"
    }
    finally {
        $client.Close()
    }
}
finally {
    $listener.Stop()
}
