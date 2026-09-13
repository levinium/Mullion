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
    snap, tile, cycle, subzone, minimize, all

    tile is the one worth leading with: three different windows into three
    zones, rather than one window visiting each in turn.

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
    [ValidateSet('snap', 'tile', 'drag', 'cycle', 'subzone', 'minimize', 'all')]
    [string] $Beat = 'all',

    [int] $Lead = 4,

    # Part of a window title to keep in front, e.g. "Notepad". Mullion moves
    # whatever has focus, and focus drifts: the first snap can hand it back to
    # this console, after which the rest of the beats move the WRONG window and
    # the log still cheerfully reports a successful move. Naming the target
    # makes a take repeatable instead of occasionally nonsense.
    [string] $Target,

    # Which window goes to which key, as '<title fragment>=<letter>'. Used by
    # the tile beat - the one that shows a desk being laid out rather than a
    # single rectangle moving from zone to zone.
    [string[]] $Tiles = @('Folder 1=A', 'Folder 2=S', 'Folder 3=D'),

    # The drag beat: which window to pick up, and where to drop it. The default
    # drop is the middle of the centre zone on a 5120-wide desk.
    [string] $DragWindow = 'Folder 1',
    [int] $DropX = 2560,
    [int] $DropY = 700
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

    // Enumerating windows rather than processes is what makes -Target work for
    // apps that open several windows from one process, File Explorer being the
    // one that matters here.
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc callback, IntPtr lparam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);

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

    // ---- mouse, for the drag ------------------------------------------------

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public const uint MOUSE = 0;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;

    // Absolute mouse coordinates are 0..65535 across the VIRTUAL desktop, not
    // the primary monitor. On a 5120-wide desk the difference is most of the
    // screen, so the virtual metrics are what the scaling has to use.
    public static void MoveTo(int x, int y)
    {
        int vx = GetSystemMetrics(76);   // SM_XVIRTUALSCREEN
        int vy = GetSystemMetrics(77);   // SM_YVIRTUALSCREEN
        int vw = GetSystemMetrics(78);   // SM_CXVIRTUALSCREEN
        int vh = GetSystemMetrics(79);   // SM_CYVIRTUALSCREEN

        INPUT[] one = new INPUT[1];
        one[0].type = MOUSE;
        one[0].mi.dx = (int)(((double)(x - vx) * 65535.0) / (vw - 1));
        one[0].mi.dy = (int)(((double)(y - vy) * 65535.0) / (vh - 1));
        one[0].mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        SendInput(1, one, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void Button(bool down)
    {
        INPUT[] one = new INPUT[1];
        one[0].type = MOUSE;
        one[0].mi.dwFlags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;

        SendInput(1, one, Marshal.SizeOf(typeof(INPUT)));
    }
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

function Focus-Window([string] $Title) {
    if (-not $Title) { return }

    # Windows are enumerated, not processes.
    #
    # Get-Process exposes ONE MainWindowTitle per process, so every File
    # Explorer window shares a single title and only one of them is findable -
    # and which one is arbitrary. Anything that opens several windows from one
    # process has the same problem. EnumWindows sees them all.
    $script:WantedTitle = $Title

    $callback = [DemoKeys+EnumProc] {
        param($hwnd, $lparam)

        if (-not [DemoKeys]::IsWindowVisible($hwnd)) { return $true }

        $length = [DemoKeys]::GetWindowTextLength($hwnd)
        if ($length -le 0) { return $true }

        $text = New-Object System.Text.StringBuilder ($length + 1)
        [void][DemoKeys]::GetWindowText($hwnd, $text, $text.Capacity)

        if ($text.ToString() -like "*$($script:WantedTitle)*") {
            $script:FoundWindow = $hwnd
            return $false   # stop enumerating
        }

        return $true
    }

    $script:FoundWindow = [IntPtr]::Zero
    [void][DemoKeys]::EnumWindows($callback, [IntPtr]::Zero)
    $match = $script:FoundWindow

    if ($match -eq [IntPtr]::Zero) {
        Write-Warning "No window matching '$Title' - whatever has focus will be moved instead."
        return
    }

    [void][DemoKeys]::SetForegroundWindow($match)
    Start-Sleep -Milliseconds 250
}

function Chord([string] $letter, [int] $pause = 1100, [string] $Window) {
    $wanted = if ($Window) { $Window } else { $Target }
    Focus-Window $wanted

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

function Move-Smoothly {
    param([int] $FromX, [int] $FromY, [int] $ToX, [int] $ToY, [int] $Milliseconds = 900)

    # What makes an injected drag look human is the VELOCITY CURVE, not the
    # path. A hand accelerates away from the start and decelerates into the
    # target; a linear interpolation moves at one speed throughout and reads as
    # mechanical immediately, even to someone not looking for it.
    #
    # Cubic ease-in-out, sampled at about 120Hz, with a pixel of jitter so the
    # line is not mathematically straight either.
    $steps = [Math]::Max(12, [int]($Milliseconds / 8))
    $random = New-Object Random

    for ($i = 1; $i -le $steps; $i++) {
        $t = $i / $steps

        $eased = if ($t -lt 0.5) {
            4 * $t * $t * $t
        } else {
            1 - [Math]::Pow(-2 * $t + 2, 3) / 2
        }

        $x = $FromX + ($ToX - $FromX) * $eased
        $y = $FromY + ($ToY - $FromY) * $eased

        # No jitter at the very end, so the drop lands exactly where intended.
        if ($i -lt $steps - 2) {
            $x += $random.Next(-1, 2)
            $y += $random.Next(-1, 2)
        }

        [DemoKeys]::MoveTo([int]$x, [int]$y)
        Start-Sleep -Milliseconds 8
    }
}

function Beat-Drag {
    # Shift-drag a window into a zone, with the mouse driven rather than the
    # keyboard.
    #
    # Worth saying plainly: this was previously left manual on the assumption
    # that a synthetic drag would not trigger Mullion's detection and would look
    # robotic anyway. The first half was wrong - Windows starts its real move
    # loop from an injected WM_NCLBUTTONDOWN exactly as it does from a hand, so
    # EVENT_SYSTEM_MOVESIZESTART fires normally. The second half was a matter of
    # easing.
    Countdown 'Shift-drag a window into a zone' $Lead

    Focus-Window $DragWindow
    Start-Sleep -Milliseconds 400

    $script:WantedTitle = $DragWindow
    $script:FoundWindow = [IntPtr]::Zero

    $find = [DemoKeys+EnumProc] {
        param($hwnd, $lparam)
        if (-not [DemoKeys]::IsWindowVisible($hwnd)) { return $true }
        $n = [DemoKeys]::GetWindowTextLength($hwnd)
        if ($n -le 0) { return $true }
        $sb = New-Object System.Text.StringBuilder ($n + 1)
        [void][DemoKeys]::GetWindowText($hwnd, $sb, $sb.Capacity)
        if ($sb.ToString() -like "*$($script:WantedTitle)*") { $script:FoundWindow = $hwnd; return $false }
        return $true
    }
    [void][DemoKeys]::EnumWindows($find, [IntPtr]::Zero)

    if ($script:FoundWindow -eq [IntPtr]::Zero) {
        Write-Warning "  no window matching '$DragWindow'; skipping the drag."
        return
    }

    $rect = New-Object DemoKeys+RECT
    [void][DemoKeys]::GetWindowRect($script:FoundWindow, [ref] $rect)

    # The title bar, left of centre so the grab point is clearly on the caption
    # and not on a maximise button or a tab.
    $grabX = $rect.Left + [int](($rect.Right - $rect.Left) * 0.35)
    $grabY = $rect.Top + 16

    Write-Host "    grabbing at $grabX,$grabY -> $DropX,$DropY" -ForegroundColor DarkGray

    # Approach the window first. A pointer that teleports onto a title bar and
    # immediately presses looks like exactly what it is.
    [DemoKeys]::MoveTo($grabX, ($grabY - 140))
    Start-Sleep -Milliseconds 300
    Move-Smoothly ($grabX) ($grabY - 140) $grabX $grabY 420
    Start-Sleep -Milliseconds 250

    [void][DemoKeys]::Down([DemoKeys]::VK_SHIFT, 0, $false)
    Start-Sleep -Milliseconds 180

    [DemoKeys]::Button($true)
    Start-Sleep -Milliseconds 260

    # A small initial nudge, because Windows only begins a move loop once the
    # pointer passes the drag threshold.
    Move-Smoothly $grabX $grabY ($grabX + 30) ($grabY + 8) 160
    Move-Smoothly ($grabX + 30) ($grabY + 8) $DropX $DropY 1100

    # Rest over the target so the zone highlight is on screen long enough to
    # read before the window lands on it.
    Start-Sleep -Milliseconds 900

    [DemoKeys]::Button($false)
    Start-Sleep -Milliseconds 200
    [void][DemoKeys]::Up([DemoKeys]::VK_SHIFT, 0, $false)

    Start-Sleep -Milliseconds 1400
}

function Beat-Tile {
    # Three DIFFERENT windows into three zones, rather than one window visiting
    # each in turn.
    #
    # This is what the app is actually for, and it is the difference between a
    # demo that shows a rectangle moving and one that shows a desk being laid
    # out. A single window hopping between zones demonstrates the mechanism; it
    # does not demonstrate the point.
    Countdown 'Three windows into three zones' $Lead

    foreach ($pair in $Tiles) {
        $window, $key = $pair -split '=', 2

        if (-not $key) {
            Write-Warning "  '$pair' is not <window title>=<key>; skipping."
            continue
        }

        Write-Host "    $window -> Win+$key" -ForegroundColor DarkGray
        Chord $key 1500 -Window $window
    }
}

function Beat-Cycle {
    Countdown 'Hold Win, tap A three times - quarter, half, full' $Lead
    # One hold, three taps. Releasing between taps would reset the ring, which
    # is exactly the behaviour being demonstrated - so it must not happen here.
    Hold-Win {
        Tap $scans['A'] 1500
        Tap $scans['A'] 1500
        Tap $scans['A'] 1500
    }
    Start-Sleep -Milliseconds 1400

    Countdown 'Release, then A again - back to the start' 2
    Chord 'A' 1800

    # The same ring from the centre zone. The narration covers growing AND the
    # reset, and one key's worth of footage runs out well before it finishes
    # saying so.
    Countdown 'And again from the middle' 2
    Hold-Win {
        Tap $scans['S'] 1500
        Tap $scans['S'] 1500
    }
    Start-Sleep -Milliseconds 1600
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
    # Shown twice, on two different windows.
    #
    # Once is under four seconds of footage against twenty-one seconds of
    # narration, and a still frame held for the rest reads as a video that has
    # stopped. Twice also makes the point better: the undo brings back the
    # window you minimized, not just the last one to move.
    Countdown 'Win+backtick to minimize, Win+Backspace to bring it back' $Lead

    foreach ($window in @('Folder 2', 'Folder 1')) {
        Focus-Window $window
        Start-Sleep -Milliseconds 700

        Hold-Win { Tap $Grave 250 }
        Start-Sleep -Milliseconds 2400

        Hold-Win { Tap $Backspace 250 }
        Start-Sleep -Milliseconds 2400
    }
}

# ---- run -------------------------------------------------------------------

Require-Mullion

Write-Host ''
Write-Host 'Pass -Target to name the window to move, or put it in front yourself.' -ForegroundColor Yellow
Write-Host 'Without -Target, focus drift will silently move the wrong window.'   -ForegroundColor Yellow

switch ($Beat) {
    'snap'     { Beat-Snap }
    'tile'     { Beat-Tile }
    'drag'     { Beat-Drag }
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
