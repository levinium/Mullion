<#
.SYNOPSIS
    Drives the Mullion demo for the walkthrough video, so a take is repeatable.

.DESCRIPTION
    Fires the hotkeys at a running Mullion on a schedule, with a countdown before
    each beat, so you start recording once and get clean, evenly paced motion
    instead of fumbling chords on camera.

    Keystrokes are sent by SCAN CODE, which is how Mullion binds them - a
    virtual-key-only injection does not populate the scan code and the hook sees
    nothing. Mullion processes injected input by default, so it responds to these
    exactly as it would to your hands.

    The Shift-drag beat is deliberately NOT automated. Mullion detects a drag via
    EVENT_SYSTEM_MOVESIZESTART, which comes from the window manager's real move
    loop, and a synthetic drag is both unreliable at triggering it and obviously
    robotic on camera. Do that one by hand.

.PARAMETER Beat
    Run a single beat instead of the whole sequence. One of:
    snap, cycle, subzone, minimize, all

.PARAMETER Lead
    Seconds of countdown before each beat. Give yourself time to start recording.

.PARAMETER Target
    Part of a window title to keep in front. Strongly recommended - without it,
    focus drift moves the wrong window partway through a take.

.EXAMPLE
    .\Run-Demo.ps1 -Target 'Notepad'
    .\Run-Demo.ps1 -Beat snap -Target 'Chrome' -Lead 5
#>
[CmdletBinding()]
param(
    [ValidateSet('snap', 'cycle', 'subzone', 'minimize', 'all')]
    [string] $Beat = 'all',

    [int] $Lead = 4,

    # Part of a window title to keep in front, e.g. "Notepad". Mullion moves
    # whatever has focus, and focus drifts: the first snap can hand it back to
    # this console, after which the rest of the beats move the WRONG window and
    # the log still cheerfully reports a successful move. Naming the target
    # makes a take repeatable instead of occasionally nonsense.
    [string] $Target
)

$ErrorActionPreference = 'Stop'

$signature = @'
using System;
using System.Runtime.InteropServices;

public class DemoKeys
{
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // MOUSEINPUT is the LARGEST member of the INPUT union, so it is what decides
    // the struct's size - 40 bytes on x64. It is declared here purely for that
    // reason and never filled in. Leave it out and Marshal.SizeOf reports 32,
    // SendInput rejects the cbSize, and it returns 0 having done nothing at all:
    // no error, no exception, no keystroke.
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
        [FieldOffset(8)] public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    public const uint KEYBOARD = 1;
    public const uint KEYUP = 0x0002;
    public const uint SCANCODE = 0x0008;
    public const uint EXTENDED = 0x0001;

    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_SHIFT = 0x10;

    public static int Size() { return Marshal.SizeOf(typeof(INPUT)); }

    static uint Send(ushort vk, ushort scan, bool up, bool extended)
    {
        INPUT[] one = new INPUT[1];
        one[0].type = KEYBOARD;
        one[0].ki.wVk = vk;
        one[0].ki.wScan = scan;

        uint flags = 0;
        if (up) flags |= KEYUP;
        if (scan != 0) flags |= SCANCODE;
        if (extended) flags |= EXTENDED;

        one[0].ki.dwFlags = flags;
        one[0].ki.dwExtraInfo = IntPtr.Zero;

        // Returned rather than discarded. SendInput failing is silent - it
        // reports how many events it accepted, and zero is the only sign that
        // anything went wrong.
        return SendInput(1, one, Marshal.SizeOf(typeof(INPUT)));
    }

    public static uint Down(ushort vk, ushort scan, bool extended) { return Send(vk, scan, false, extended); }
    public static uint Up(ushort vk, ushort scan, bool extended)   { return Send(vk, scan, true,  extended); }
}
'@

if (-not ('DemoKeys' -as [type])) { Add-Type -TypeDefinition $signature }

# The standard set-1 scan codes for A-Z, in alphabetical order. Same table the
# probe uses, which is the one verified to drive the real app.
$scans = @{
    A = 0x1E; B = 0x30; C = 0x2E; D = 0x20; E = 0x12; F = 0x21; G = 0x22
    H = 0x23; I = 0x17; J = 0x24; K = 0x25; L = 0x26; M = 0x32; N = 0x31
    O = 0x18; P = 0x19; Q = 0x10; R = 0x13; S = 0x1F; T = 0x14; U = 0x16
    V = 0x2F; W = 0x11; X = 0x2D; Y = 0x15; Z = 0x2C
}

$Grave     = 0x29   # the ` key, for minimize
$Backspace = 0x0E   # undo

function Hold-Win([scriptblock] $Body) {
    # The Windows key is extended, and must be held across every tap inside the
    # block - that hold is what cycling depends on.
    [void][DemoKeys]::Down([DemoKeys]::VK_LWIN, 0, $true)
    Start-Sleep -Milliseconds 120
    try { & $Body }
    finally {
        [void][DemoKeys]::Up([DemoKeys]::VK_LWIN, 0, $true)
        Start-Sleep -Milliseconds 200
    }
}

function Tap([int] $scan, [int] $pause = 900) {
    [void][DemoKeys]::Down(0, $scan, $false)
    Start-Sleep -Milliseconds 60
    [void][DemoKeys]::Up(0, $scan, $false)
    Start-Sleep -Milliseconds $pause
}

function Focus-Target {
    if (-not $Target) { return }

    $w = Get-Process -ErrorAction SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -like "*$Target*" } |
         Select-Object -First 1

    if (-not $w) {
        Write-Warning "No window matching '$Target' - whatever has focus will be moved instead."
        return
    }

    [void][DemoKeys]::SetForegroundWindow($w.MainWindowHandle)
    Start-Sleep -Milliseconds 250
}

function Chord([string] $letter, [int] $pause = 1100) {
    Focus-Target
    Hold-Win { Tap $scans[$letter.ToUpper()] 250 }
    Start-Sleep -Milliseconds $pause
}

function Countdown([string] $what, [int] $seconds) {
    Write-Host ''
    Write-Host "  NEXT: $what" -ForegroundColor Cyan
    for ($i = $seconds; $i -gt 0; $i--) {
        Write-Host "    $i..." -NoNewline
        Start-Sleep -Seconds 1
        Write-Host "`r" -NoNewline
    }
    Write-Host "    GO        " -ForegroundColor Green
}

function Require-Mullion {
    $p = Get-Process Mullion -ErrorAction SilentlyContinue
    if (-not $p) {
        throw 'Mullion is not running. Start it first, then run this again.'
    }
    Write-Host "Mullion is running (pid $($p[0].Id))." -ForegroundColor Green
}

# ---- the beats -------------------------------------------------------------

function Beat-Snap {
    Countdown 'Win+A, Win+S, Win+D - left, middle, right' $Lead
    Chord 'A' 1400
    Chord 'S' 1400
    Chord 'D' 1400
}

function Beat-Cycle {
    Countdown 'Hold Win, tap A three times - quarter, half, full' $Lead
    # One hold, three taps. Releasing between taps would reset the ring, which
    # is exactly the behaviour being demonstrated - so it must not happen here.
    Hold-Win {
        Tap $scans['A'] 1300
        Tap $scans['A'] 1300
        Tap $scans['A'] 1300
    }
    Start-Sleep -Milliseconds 900

    Countdown 'Release, then A again - back to the start' 2
    Chord 'A' 1200
}

function Beat-Subzone {
    Countdown 'Win+Q, then Win+Shift+Q - the two halves' $Lead
    Chord 'Q' 1600

    [void][DemoKeys]::Down([DemoKeys]::VK_LWIN, 0, $true)
    [void][DemoKeys]::Down([DemoKeys]::VK_SHIFT, 0, $false)
    Start-Sleep -Milliseconds 150
    Tap $scans['Q'] 250
    [void][DemoKeys]::Up([DemoKeys]::VK_SHIFT, 0, $false)
    [void][DemoKeys]::Up([DemoKeys]::VK_LWIN, 0, $true)
    Start-Sleep -Milliseconds 1600
}

function Beat-Minimize {
    Countdown 'Win+backtick to minimize, Win+Backspace to bring it back' $Lead
    Hold-Win { Tap $Grave 250 }
    Start-Sleep -Milliseconds 1800

    Hold-Win { Tap $Backspace 250 }
    Start-Sleep -Milliseconds 1400
}

# ---- run -------------------------------------------------------------------

Require-Mullion

Write-Host ''
Write-Host 'Pass -Target to name the window to move, or put it in front yourself.' -ForegroundColor Yellow
Write-Host 'Without -Target, focus drift will silently move the wrong window.'   -ForegroundColor Yellow

switch ($Beat) {
    'snap'     { Beat-Snap }
    'cycle'    { Beat-Cycle }
    'subzone'  { Beat-Subzone }
    'minimize' { Beat-Minimize }
    'all' {
        Beat-Snap
        Beat-Cycle
        Beat-Subzone
        Beat-Minimize
    }
}

Write-Host ''
Write-Host 'Done. The Shift-drag beat is manual - see the note at the top.' -ForegroundColor Cyan
