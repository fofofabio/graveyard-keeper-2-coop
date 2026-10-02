# Dot-sourced by the host's tests: talking to the guest agent in the test VM (tests\vm\GuestAgent.ps1).
# The key lives in artifacts\vm\guest-key.txt on this PC and in C:\GK2Coop\agent\token.txt in the VM.

$script:VmName = 'GK2Coop-Test'
$script:VmPort = 8766
$script:VmKeyFile = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'artifacts\vm\guest-key.txt'

function Get-VmGuestKey {
    if (-not (Test-Path -LiteralPath $script:VmKeyFile)) {
        New-Item -ItemType Directory -Force -Path (Split-Path $script:VmKeyFile) | Out-Null
        $bytes = New-Object byte[] 32
        [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        [IO.File]::WriteAllText($script:VmKeyFile, (($bytes | ForEach-Object { $_.ToString('x2') }) -join ''))
    }
    return (Get-Content -LiteralPath $script:VmKeyFile -Raw).Trim()
}

function Get-VmGuestAddress {
    $ips = @((Get-VMNetworkAdapter -VMName $script:VmName).IPAddresses | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' })
    if ($ips.Count -eq 0) { throw "No IPv4 address for $script:VmName yet (is it running and logged in?)" }
    return $ips[0]
}

function Invoke-VmGuest([string]$Method, [string]$Path, [byte[]]$Body = $null, [int]$TimeoutSeconds = 30) {
    $uri = "http://$(Get-VmGuestAddress):$script:VmPort$Path"
    $request = [Net.HttpWebRequest]::Create($uri)
    $request.Method = $Method
    $request.Timeout = $TimeoutSeconds * 1000
    $request.ReadWriteTimeout = $TimeoutSeconds * 1000
    $request.Headers.Add('X-GK2Coop-Key', (Get-VmGuestKey))
    if ($Body) {
        $request.ContentLength = $Body.Length
        $stream = $request.GetRequestStream(); $stream.Write($Body, 0, $Body.Length); $stream.Close()
    } elseif ($Method -in 'POST', 'PUT') { $request.ContentLength = 0 }
    try { $response = $request.GetResponse() } catch [Net.WebException] { if ($_.Exception.Response) { $response = $_.Exception.Response } else { throw } }
    $memory = New-Object IO.MemoryStream
    $response.GetResponseStream().CopyTo($memory); $response.Close()
    # The comma keeps an empty answer an empty array (PowerShell would unroll it to nothing).
    return ,$memory.ToArray()
}

function Invoke-VmGuestText([string]$Method, [string]$Path, [string]$Text = $null) {
    $body = if ($Text -ne $null -and $Text -ne '') { [Text.Encoding]::UTF8.GetBytes($Text) } else { $null }
    return [Text.Encoding]::UTF8.GetString((Invoke-VmGuest $Method $Path $body))
}

function Send-VmFile([string]$LocalPath, [string]$GuestRelative) {
    $bytes = [IO.File]::ReadAllBytes($LocalPath)
    $answer = [Text.Encoding]::UTF8.GetString((Invoke-VmGuest 'PUT' ("/file?path=" + [Uri]::EscapeDataString($GuestRelative)) $bytes 300))
    if ($answer -notlike 'WROTE*') { throw "Could not write $GuestRelative in the VM: $answer" }
}

function Receive-VmFile([string]$GuestRelative, [string]$LocalPath) {
    $bytes = Invoke-VmGuest 'GET' ("/file?path=" + [Uri]::EscapeDataString($GuestRelative)) $null 120
    [IO.File]::WriteAllBytes($LocalPath, $bytes)
}

# Copies a folder into the VM, every file.
function Send-VmFolder([string]$LocalFolder, [string]$GuestRelative) {
    $local = (Resolve-Path -LiteralPath $LocalFolder).Path.TrimEnd('\')
    $sent = 0
    foreach ($file in Get-ChildItem -LiteralPath $local -Recurse -File) {
        $relative = $file.FullName.Substring($local.Length + 1)
        Send-VmFile $file.FullName (Join-Path $GuestRelative $relative)
        $sent++
    }
    return $sent
}
