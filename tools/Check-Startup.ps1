<#
.SYNOPSIS
  Asserts which window each startup mode puts on screen.

.DESCRIPTION
  This exists because the bug it guards against cannot be reached from the test
  suite. Avalonia's desktop lifetime shows whatever is assigned to MainWindow as
  soon as startup returns - so for a while every auto-start opened the window
  despite --tray, and every first run drew the main window behind the wizard.
  The headless lifetime the tests use does no such thing, so a headless test
  passed the whole time the shipped app was wrong.

  What is actually being asserted is therefore end-to-end and needs a real
  desktop session: launch the built exe, ask Windows which of its top-level
  windows are visible, and compare against what the mode is supposed to show.

  Run it against a build before publishing:
      .\tools\Check-Startup.ps1
      .\tools\Check-Startup.ps1 -Exe .\publish\Mullion.exe

  It stops any running Mullion, and leaves none running.
#>
[CmdletBinding()]
param(
    [string] $Exe = "$PSScriptRoot\..\src\Mullion.App\bin\Debug\net10.0\Mullion.exe",
    [int]    $SettleSeconds = 4
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Exe)) { throw "No executable at $Exe. Build first." }
$Exe = (Resolve-Path $Exe).Path

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class VisibleWindows
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);

    delegate bool EnumProc(IntPtr h, IntPtr p);

    // Titled and visible only: the message-only windows the hook and the display
    // watcher own are always there and are not what anyone means by "the app
    // opened".
    public static List<string> Of(uint pid)
    {
        var found = new List<string>();

        EnumWindows((h, _) =>
        {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner != pid || !IsWindowVisible(h)) return true;

            var title = new StringBuilder(256);
            GetWindowTextW(h, title, title.Capacity);
            if (title.Length > 0) found.Add(title.ToString());

            return true;
        }, IntPtr.Zero);

        return found;
    }
}
'@

function Stop-Mullion {
    Get-Process -Name Mullion -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill() } catch { Write-Warning "Could not stop pid $($_.Id): $($_.Exception.Message)" }
    }
    Start-Sleep -Milliseconds 900
}

function Get-StartupWindows([string[]] $Arguments) {
    Stop-Mullion

    if ($Arguments.Count -gt 0) { Start-Process $Exe -ArgumentList $Arguments | Out-Null }
    else                        { Start-Process $Exe | Out-Null }

    Start-Sleep -Seconds $SettleSeconds

    $process = Get-Process -Name Mullion -ErrorAction SilentlyContinue
    if (-not $process) { return @('<the process exited>') }

    return [VisibleWindows]::Of($process.Id)
}

# The window the wizard opens is titled differently from the main window, which
# is what makes "the main window appeared behind the wizard" detectable at all.
#
# The --tray --show pair is the interesting one. --show is what "restart as
# administrator" relaunches with, and it has to win over a tray start however
# that was asked for - the flag here, or the "always start in the tray" setting
# this script cannot flip. Pairing the two is the closest standing proof of that
# precedence, and it fails if --show is ever demoted to a mere default.
$cases = @(
    @{ Args = @();                    Expected = @('Mullion');        Why = 'a hand launch opens the window' }
    @{ Args = @('--tray');            Expected = @();                 Why = 'auto-start stays in the tray' }
    @{ Args = @('--show');            Expected = @('Mullion');        Why = 'an asked-for relaunch comes back visible' }
    @{ Args = @('--tray', '--show');  Expected = @('Mullion');        Why = '--show outranks a tray start' }
    @{ Args = @('--wizard');          Expected = @('Set up Mullion'); Why = 'setup opens alone, with nothing behind it' }
)

$failures = 0

foreach ($case in $cases) {
    $label  = if ($case.Args.Count) { $case.Args -join ' ' } else { '(no arguments)' }
    $actual = @(Get-StartupWindows $case.Args)

    $same = $actual.Count -eq $case.Expected.Count -and
            -not (Compare-Object -ReferenceObject @($case.Expected) -DifferenceObject $actual)

    $shown = if ($actual.Count) { $actual -join ', ' } else { 'nothing' }

    if ($same) {
        Write-Host ("  PASS  {0,-14} -> {1}" -f $label, $shown) -ForegroundColor Green
    }
    else {
        $want = if ($case.Expected.Count) { $case.Expected -join ', ' } else { 'nothing' }
        Write-Host ("  FAIL  {0,-14} -> {1}" -f $label, $shown) -ForegroundColor Red
        Write-Host ("        expected {0} - {1}" -f $want, $case.Why) -ForegroundColor Red
        $failures++
    }
}

Stop-Mullion

if ($failures) {
    Write-Host ""
    Write-Host "$failures startup mode(s) wrong." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Every startup mode opens what it should." -ForegroundColor Green
