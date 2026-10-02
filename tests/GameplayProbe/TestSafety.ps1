# Dot-sourced by the live tests so they can run beside someone playing the real game:
# test copies only (never the Steam install), processes matched by path (never by name),
# test windows on a private desktop, muted at low priority, and the game's shared registry
# preferences restored. The active user's desktop never receives their windows.

$script:SteamInstallRoots = @('C:\Program Files (x86)\Steam', 'C:\Program Files\Steam')
$script:GamePrefsKey = 'HKCU:\Software\Lazy Bear Games\Graveyard Keeper 2'
$script:TestWindowGuards = New-Object System.Collections.ArrayList

# Unity can show a window even after a hidden launch. The private desktop keeps these windows
# off the active desktop; this PID guard also hides them there if Unity opens them again.
if (-not ('GK2CoopTestWindowGuard' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public sealed class GK2CoopTestWindowGuard {
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr value);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory,
        ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo {
        public int Size;
        public string Reserved;
        public string Desktop;
        public string Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer, StandardInput, StandardOutput, StandardError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }
    private delegate bool EnumWindowProc(IntPtr window, IntPtr value);
    private readonly int pid;
    private readonly Timer timer;
    private static readonly string desktopName = "GK2CoopTest_" + Process.GetCurrentProcess().Id;
    private static IntPtr privateDesktop;
    public static int StartOnPrivateDesktop(string executable, string directory, string arguments) {
        if (privateDesktop == IntPtr.Zero) {
            privateDesktop = CreateDesktop(desktopName, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
            if (privateDesktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a private test desktop");
        }
        var startup = new StartupInfo {
            Size = Marshal.SizeOf(typeof(StartupInfo)), Desktop = "WinSta0\\" + desktopName,
            Flags = 1, ShowWindow = 0
        };
        var command = new StringBuilder("\"" + executable + "\" " + arguments);
        ProcessInfo result;
        if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x4000,
                           IntPtr.Zero, directory, ref startup, out result))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not launch on private test desktop");
        CloseHandle(result.Thread);
        CloseHandle(result.Process);
        return result.ProcessId;
    }
    public static bool IsForegroundFullscreen() {
        IntPtr window = GetForegroundWindow();
        Rect rect;
        if (window == IntPtr.Zero || !GetWindowRect(window, out rect)) return false;
        IntPtr monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        return rect.Left <= info.Monitor.Left + 2 && rect.Top <= info.Monitor.Top + 2 &&
               rect.Right >= info.Monitor.Right - 2 && rect.Bottom >= info.Monitor.Bottom - 2;
    }
    public static int ForegroundProcessId() {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        uint owner;
        GetWindowThreadProcessId(window, out owner);
        return (int)owner;
    }
    public GK2CoopTestWindowGuard(int processId) {
        pid = processId;
        timer = new Timer(Hide, null, 0, 50);
    }
    private void Hide(object ignored) {
        try {
            using (var process = Process.GetProcessById(pid)) {
                if (process.HasExited) { timer.Dispose(); return; }
            }
            EnumWindows((window, value) => {
                uint owner;
                GetWindowThreadProcessId(window, out owner);
                if (owner == pid && IsWindowVisible(window)) ShowWindowAsync(window, 0);
                return true;
            }, IntPtr.Zero);
        } catch (ArgumentException) { timer.Dispose(); }
          catch (InvalidOperationException) { timer.Dispose(); }
    }
}
'@
}

function Assert-TestInstall([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    foreach ($root in $script:SteamInstallRoots) {
        if ($full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to test in the Steam install ($full). Use a copy such as D:\GK2Coop-FullHost."
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $full 'GraveyardKeeper2.exe'))) { throw "No game copy at $full" }
}

function Get-TestGameProcesses([string[]]$Paths) {
    $roots = $Paths | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') + '\' }
    @(Get-Process -Name GraveyardKeeper2 -ErrorAction SilentlyContinue | Where-Object {
        $exe = $_.Path
        $exe -and @($roots | Where-Object { $exe.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
    })
}

function Start-TestGame([string]$Path) {
    Assert-TestInstall $Path
    $env:GK2COOP_TEST_QUIET = '1'
    $exe = Join-Path $Path 'GraveyardKeeper2.exe'
    $id = [GK2CoopTestWindowGuard]::StartOnPrivateDesktop($exe, $Path, '-screen-fullscreen 0 -screen-width 640 -screen-height 360 -window-mode windowed')
    $process = Get-Process -Id $id
    $null = $script:TestWindowGuards.Add([GK2CoopTestWindowGuard]::new($process.Id))
    try { $process.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal } catch { }
    return $process
}

# Files the game writes into the player's own data folder whatever save folder a test uses: since
# game 1.007 the bug reporter notes the last loaded slot there, and a test game set it to a test slot.
$script:SharedGameFiles = @('bug-reporter-relevant-save.txt')
$script:SharedGameFolder = Join-Path $env:USERPROFILE 'AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2'

function Save-GamePrefs {
    $values = @{}
    if (Test-Path $script:GamePrefsKey) {
        $key = Get-Item $script:GamePrefsKey
        foreach ($name in $key.GetValueNames()) { $values[$name] = @($key.GetValue($name, $null, 'DoNotExpandEnvironmentNames'), $key.GetValueKind($name)) }
    }
    foreach ($file in $script:SharedGameFiles) {
        $path = Join-Path $script:SharedGameFolder $file
        # The comma keeps the bytes one array (an if would hand them out one by one).
        $bytes = if (Test-Path -LiteralPath $path) { ,[IO.File]::ReadAllBytes($path) } else { $null }
        $values["<file>$file"] = @($bytes, 'File')
    }
    return $values
}

# Puts back every preference a test instance changed (window size, fullscreen, the game's settings
# blob) and the shared files above.
function Restore-GamePrefs($Saved) {
    if (-not $Saved) { return 0 }
    $restored = 0
    foreach ($name in @($Saved.Keys | Where-Object { $_ -like '<file>*' })) {
        $path = Join-Path $script:SharedGameFolder $name.Substring(6)
        $was = $Saved[$name][0]
        $now = if (Test-Path -LiteralPath $path) { ,[IO.File]::ReadAllBytes($path) } else { $null }
        if ($null -eq $was) {
            if ($null -ne $now) { Remove-Item -LiteralPath $path -Force; $restored++ }
        } elseif ($null -eq $now -or [Convert]::ToBase64String($was) -ne [Convert]::ToBase64String($now)) {
            [IO.File]::WriteAllBytes($path, $was); $restored++
        }
    }
    if (-not (Test-Path $script:GamePrefsKey)) { return $restored }
    $key = Get-Item $script:GamePrefsKey
    foreach ($name in $Saved.Keys) {
        if ($name -like '<file>*') { continue }
        if ($name -like 'unity*session*') { continue }
        $now = $key.GetValue($name, $null, 'DoNotExpandEnvironmentNames')
        $was = $Saved[$name][0]
        $same = if ($was -is [byte[]] -and $now -is [byte[]]) { [Convert]::ToBase64String($was) -eq [Convert]::ToBase64String($now) } else { "$was" -eq "$now" }
        if (-not $same) {
            Set-ItemProperty -Path $script:GamePrefsKey -Name $name -Value $was -Type $Saved[$name][1]
            $restored++
        }
    }
    return $restored
}
