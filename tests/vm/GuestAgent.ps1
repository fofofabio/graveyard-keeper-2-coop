<#
    Runs inside the test VM, in the logged-in user's session (started at logon by the task that
    Install-GuestAgent.ps1 creates). It lets the host's tests drive the VM's copy of the game
    without any login: a small HTTP service on the VM's internal network that answers only
    requests carrying the key from C:\GK2Coop\agent\token.txt, and can only:
      GET  /ping                     who and what is running
      GET  /list?path=rel            files under C:\GK2Coop
      GET  /file?path=rel            read a file under C:\GK2Coop
      PUT  /file?path=rel            write a file under C:\GK2Coop (body = the bytes)
      POST /start  (JSON)            start C:\GK2Coop\<exe> with arguments and GK2COOP_* variables
      POST /stop                     end the games this agent started
    Nothing outside C:\GK2Coop, no other programs, no shell. Approved by the user on 2026-09-28.
#>
$ErrorActionPreference = 'Stop'
$root = 'C:\GK2Coop'
$port = 8766
$token = (Get-Content -LiteralPath (Join-Path $root 'agent\token.txt') -Raw).Trim()
$log = Join-Path $root 'agent\agent.log'
$started = New-Object System.Collections.ArrayList
Add-Type -AssemblyName System.Web

function Write-Log([string]$text) { Add-Content -LiteralPath $log -Value ("{0:s} {1}" -f (Get-Date), $text) }

function Resolve-Inside([string]$relative) {
    if ([string]::IsNullOrWhiteSpace($relative)) { $relative = '.' }
    $full = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if ($full -ne $root -and -not $full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "outside $root" }
    return $full
}

function Send($context, [int]$status, [byte[]]$bytes, [string]$type = 'text/plain; charset=utf-8') {
    $context.Response.StatusCode = $status
    $context.Response.ContentType = $type
    $context.Response.ContentLength64 = $bytes.Length
    $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $context.Response.OutputStream.Close()
}
function SendText($context, [int]$status, [string]$text) { Send $context $status ([Text.Encoding]::UTF8.GetBytes($text)) }

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://+:$port/")
$listener.Start()
Write-Log "agent listening on $port"

while ($listener.IsListening) {
    $context = $listener.GetContext()
    $request = $context.Request
    try {
        if ($request.Headers['X-GK2Coop-Key'] -ne $token) { SendText $context 403 'no'; Write-Log "refused $($request.RemoteEndPoint)"; continue }
        $path = $request.Url.AbsolutePath
        $query = [Web.HttpUtility]::ParseQueryString($request.Url.Query)
        switch ("$($request.HttpMethod) $path") {
            'GET /ping' {
                $games = @(Get-Process -Name GraveyardKeeper2 -ErrorAction SilentlyContinue | ForEach-Object { "$($_.Id)=$($_.Path)" })
                SendText $context 200 ("PONG user=$env:USERNAME host=$env:COMPUTERNAME games=" + ($games -join ';'))
            }
            'GET /list' {
                $dir = Resolve-Inside $query['path']
                $lines = if (Test-Path -LiteralPath $dir) { Get-ChildItem -LiteralPath $dir | ForEach-Object { "{0}`t{1}`t{2:s}" -f $_.Name, $(if ($_.PSIsContainer) { '<dir>' } else { $_.Length }), $_.LastWriteTime } } else { @() }
                SendText $context 200 ($lines -join "`n")
            }
            'GET /file' {
                $file = Resolve-Inside $query['path']
                if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { SendText $context 404 'missing'; break }
                # Shared read: the game keeps its log open.
                $stream = [IO.File]::Open($file, 'Open', 'Read', 'ReadWrite')
                try { $buffer = New-Object byte[] $stream.Length; [void]$stream.Read($buffer, 0, $buffer.Length) } finally { $stream.Close() }
                Send $context 200 $buffer 'application/octet-stream'
            }
            'PUT /file' {
                $file = Resolve-Inside $query['path']
                New-Item -ItemType Directory -Force -Path (Split-Path $file) | Out-Null
                $memory = New-Object IO.MemoryStream
                $request.InputStream.CopyTo($memory)
                [IO.File]::WriteAllBytes($file, $memory.ToArray())
                SendText $context 200 "WROTE $($memory.Length)"
            }
            'POST /start' {
                $reader = New-Object IO.StreamReader($request.InputStream, [Text.Encoding]::UTF8)
                $spec = $reader.ReadToEnd() | ConvertFrom-Json
                $exe = Resolve-Inside $spec.exe
                if ([IO.Path]::GetFileName($exe) -ne 'GraveyardKeeper2.exe') { throw 'only GraveyardKeeper2.exe can be started' }
                $info = New-Object Diagnostics.ProcessStartInfo($exe, [string]$spec.arguments)
                $info.WorkingDirectory = Split-Path $exe
                $info.UseShellExecute = $false
                if ($spec.environment) {
                    foreach ($p in $spec.environment.PSObject.Properties) {
                        if ($p.Name -notlike 'GK2COOP_*') { throw "variable $($p.Name) not allowed" }
                        $info.EnvironmentVariables[$p.Name] = [string]$p.Value
                    }
                }
                $process = [Diagnostics.Process]::Start($info)
                [void]$started.Add($process)
                Write-Log "started $exe pid $($process.Id)"
                SendText $context 200 "STARTED $($process.Id)"
            }
            'POST /stop' {
                $n = 0
                foreach ($process in @($started)) { if (-not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(10000); $n++ } }
                $started.Clear()
                SendText $context 200 "STOPPED $n"
            }
            default { SendText $context 404 'unknown' }
        }
    }
    catch {
        Write-Log "error: $($_.Exception.Message)"
        try { SendText $context 500 ("ERROR " + $_.Exception.Message) } catch { }
    }
}
