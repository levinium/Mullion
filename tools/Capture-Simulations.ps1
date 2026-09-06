<#
.SYNOPSIS
    Screenshots Mullion's main window once per simulated display arrangement.

.DESCRIPTION
    Launches the app with --simulate for each preset in Mullion.Core's
    SimulatedTopologies and captures the window, so the defaults the layout
    engine produces for arrangements this machine cannot physically produce can
    be reviewed side by side.

    Output lands in screenshots/ off the repo root, which is gitignored: the
    images are regenerated from this script rather than tracked.

.EXAMPLE
    .\tools\Capture-Simulations.ps1
    .\tools\Capture-Simulations.ps1 -Presets two-across, l-shape
#>
[CmdletBinding()]
param(
    # Preset ids to capture. Defaults to every preset.
    [string[]] $Presets,

    # Where to write the PNGs. Defaults to screenshots/ off the repo root.
    [string] $OutputDirectory,

    # Built app to launch. Defaults to the Debug build.
    [string] $Exe
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot

if (-not $Exe) {
    $Exe = Join-Path $root 'src\Mullion.App\bin\Debug\net10.0\Mullion.exe'
}

if (-not (Test-Path $Exe)) {
    throw "Not built yet: $Exe. Run 'dotnet build' first."
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'screenshots'
}

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

if (-not $Presets) {
    # Kept in step with SimulatedTopologies.All by hand; a preset missing here
    # is simply one nobody captures, not a failure.
    $Presets = @(
        'single-32-9', 'single-21-9', 'single-16-9', 'single-4k',
        'two-across', 'three-across', 'three-portrait',
        'standard-plus-ultrawide', 'verticals-flanking-stacked',
        'two-stacked', 'two-by-two', 'laptop-plus-external',
        'rotated-32-9', 'l-shape', 'ancient-and-modern'
    )
}

# The window is captured from the screen rather than rendered offscreen, so it
# has to be foreground and settled first. Extended frame bounds, not
# GetWindowRect: the latter includes the invisible resize border and the shot
# would carry a margin of whatever is behind the window.
$signature = @'
using System;
using System.Runtime.InteropServices;

public class MullionCapture
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out RECT value, int size);

    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

if (-not ('MullionCapture' -as [type])) {
    Add-Type -TypeDefinition $signature
}

$DwmExtendedFrameBounds = 9
$captured = 0

foreach ($preset in $Presets) {
    Get-Process -Name Mullion -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    Start-Process -FilePath $Exe -ArgumentList @('--simulate', $preset) | Out-Null
    Start-Sleep -Seconds 7

    $process = Get-Process -Name Mullion -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $process) {
        Write-Warning "$preset : did not start"
        continue
    }

    [MullionCapture]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 900

    $bounds = New-Object MullionCapture+RECT
    [MullionCapture]::DwmGetWindowAttribute(
        $process.MainWindowHandle, $DwmExtendedFrameBounds, [ref] $bounds, 16) | Out-Null

    $width = $bounds.Right - $bounds.Left
    $height = $bounds.Bottom - $bounds.Top

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen(
        $bounds.Left, $bounds.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $graphics.Dispose()

    $path = Join-Path $OutputDirectory "sim-$preset.png"
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()

    Write-Output "  $preset"
    $captured++
}

Get-Process -Name Mullion -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output "$captured captures in $OutputDirectory"
