# Renders Mullion's app icon at every size Windows asks for, and produces a
# mockup sheet showing each candidate on light and dark backgrounds.
#
# Small sizes are where icons fail. At 16px a stroked outline with interior
# detail turns to mush, so each candidate here is checked at 16px first and
# scaled up second, not the other way round.
#
#   .\Build-Icon.ps1                      write mullion.ico from the chosen design
#   .\Build-Icon.ps1 -Mockup              also write the candidate contact sheet
#   .\Build-Icon.ps1 -Design solid        pick a different candidate

[CmdletBinding()]
param(
    [ValidateSet('outline', 'solid', 'signature', 'signature-mono', 'bar')]
    [string]$Design = 'signature-mono',

    [switch]$Mockup,

    [string]$OutputPath = (Join-Path $PSScriptRoot '..\..\src\Mullion.App\Assets\mullion.ico'),
    [string]$MockupPath = (Join-Path $PSScriptRoot 'icon-candidates.png')
)

Add-Type -AssemblyName System.Drawing

$Accent = [System.Drawing.Color]::FromArgb(255, 76, 139, 245)

# The secondary tone differentiates by SATURATION, not lightness.
# A lighter blue (the obvious choice) has almost no contrast on a white
# taskbar, and a darker one out-contrasts the accent there and inverts the
# hierarchy. A mid-luminance desaturated blue sits between both backgrounds,
# so it stays visible on either and always reads as subordinate to the accent.
$AccentDim = [System.Drawing.Color]::FromArgb(255, 108, 129, 164)
$IcoSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-IconBitmap {
    param([int]$Size, [string]$Style)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # A 12% margin keeps the glyph clear of the tray's own padding.
    $pad = [Math]::Max(1, [int][Math]::Round($Size * 0.12))
    $w = $Size - 2 * $pad
    $h = [int][Math]::Round($w * 0.72)
    $top = [int][Math]::Round(($Size - $h) / 2)

    $brush = New-Object System.Drawing.SolidBrush($Accent)
    $dim = New-Object System.Drawing.SolidBrush($AccentDim)

    switch ($Style) {
        # A window outline with two interior mullions. Most literal, but the
        # thin interior lines are the first thing to disappear at 16px.
        'outline' {
            $stroke = [Math]::Max(1.0, $Size / 14.0)
            $pen = New-Object System.Drawing.Pen($Accent, $stroke)
            $g.DrawRectangle($pen, $pad, $top, $w, $h)
            $g.DrawLine($pen, ($pad + [int]($w * 0.25)), $top, ($pad + [int]($w * 0.25)), ($top + $h))
            $g.DrawLine($pen, ($pad + [int]($w * 0.75)), $top, ($pad + [int]($w * 0.75)), ($top + $h))
            $pen.Dispose()
        }

        # Three equal filled panes. Solid shapes survive small sizes far better
        # than strokes, but this says nothing about what Mullion actually does.
        'solid' {
            $gap = [Math]::Max(1, [int][Math]::Round($Size * 0.055))
            $paneW = [int][Math]::Floor(($w - 2 * $gap) / 3)
            for ($i = 0; $i -lt 3; $i++) {
                $x = $pad + $i * ($paneW + $gap)
                $g.FillRectangle($brush, $x, $top, $paneW, $h)
            }
        }

        # The 25/50/25 split Mullion actually produces on a wide display, as
        # solid panes. Distinctive at a glance and still legible at 16px.
        'signature' {
            $gap = [Math]::Max(1, [int][Math]::Round($Size * 0.055))
            $usable = $w - 2 * $gap
            $side = [int][Math]::Round($usable * 0.25)
            $centre = $usable - 2 * $side

            $g.FillRectangle($dim, $pad, $top, $side, $h)
            $g.FillRectangle($brush, ($pad + $side + $gap), $top, $centre, $h)
            $g.FillRectangle($dim, ($pad + $side + $gap + $centre + $gap), $top, $side, $h)
        }

        # The same 25/50/25 proportions in one tone. The two-tone version loses
        # its side panes against a light background at 16px, because a dimmed
        # accent has little contrast on white - and an .ico cannot adapt to the
        # background it lands on. Width alone carries the meaning here.
        'signature-mono' {
            $gap = [Math]::Max(1, [int][Math]::Round($Size * 0.055))
            $usable = $w - 2 * $gap
            $side = [int][Math]::Round($usable * 0.25)
            $centre = $usable - 2 * $side

            $g.FillRectangle($brush, $pad, $top, $side, $h)
            $g.FillRectangle($brush, ($pad + $side + $gap), $top, $centre, $h)
            $g.FillRectangle($brush, ($pad + $side + $gap + $centre + $gap), $top, $side, $h)
        }

        # A single strong vertical bar - the mullion itself - against a muted
        # pane pair. The most minimal reading of the name.
        'bar' {
            $barW = [Math]::Max(2, [int][Math]::Round($Size * 0.14))
            $x = $pad + [int][Math]::Round(($w - $barW) / 2)
            $paneW = [int][Math]::Round(($w - $barW) / 2) - [Math]::Max(1, [int]($Size * 0.04))

            $g.FillRectangle($dim, $pad, $top, $paneW, $h)
            $g.FillRectangle($dim, ($pad + $w - $paneW), $top, $paneW, $h)
            $g.FillRectangle($brush, $x, $top, $barW, $h)
        }
    }

    $brush.Dispose(); $dim.Dispose(); $g.Dispose()
    return $bmp
}

function Write-Ico {
    param([string]$Path, [string]$Style)

    $frames = New-Object System.Collections.ArrayList

    foreach ($size in $IcoSizes) {
        $bmp = New-IconBitmap -Size $size -Style $Style

        # ICO frames must be DIB (BITMAPINFOHEADER + bottom-up BGRA + AND mask).
        # PNG-encoded frames are legal per the format but Avalonia's decoder
        # does not reliably accept them, and the failure is silent.
        $ms = New-Object System.IO.MemoryStream
        $bw = New-Object System.IO.BinaryWriter($ms)
        $bw.Write([UInt32]40); $bw.Write([Int32]$size); $bw.Write([Int32]($size * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]($size * $size * 4)); $bw.Write([Int32]0); $bw.Write([Int32]0)
        $bw.Write([UInt32]0); $bw.Write([UInt32]0)

        $data = $bmp.LockBits(
            (New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
            [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

        $row = New-Object byte[] ($size * 4)
        for ($y = $size - 1; $y -ge 0; $y--) {
            [System.Runtime.InteropServices.Marshal]::Copy(
                [IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $row.Length)
            $bw.Write($row)
        }

        $bmp.UnlockBits($data)

        $maskRow = [int][Math]::Ceiling($size / 32.0) * 4
        $zeros = New-Object byte[] ($maskRow * $size)
        $bw.Write($zeros)
        $bw.Flush()

        [void]$frames.Add(@{ Size = $size; Data = $ms.ToArray() })
        $ms.Dispose(); $bmp.Dispose()
    }

    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }

    $fs = [System.IO.File]::Create($Path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)

    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $d = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $bw.Write([Byte]$d); $bw.Write([Byte]$d); $bw.Write([Byte]0); $bw.Write([Byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$f.Data.Length); $bw.Write([UInt32]$offset)
        $offset += $f.Data.Length
    }

    foreach ($f in $frames) { $bw.Write($f.Data) }
    $bw.Close(); $fs.Close()

    Write-Output "wrote $Path ($((Get-Item $Path).Length) bytes, $($frames.Count) frames, design '$Style')"
}

function Write-Mockup {
    param([string]$Path)

    $styles = @('outline', 'solid', 'signature', 'signature-mono', 'bar')
    $shown = @(16, 20, 24, 32, 48, 64, 128)

    $labelW = 110
    $cell = 150
    $rowH = 150
    $width = $labelW + $shown.Count * $cell
    $height = 60 + $styles.Count * $rowH * 2

    $sheet = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::FromArgb(255, 30, 30, 34))

    $font = New-Object System.Drawing.Font('Segoe UI', 11)
    $small = New-Object System.Drawing.Font('Segoe UI', 9)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $grey = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 150, 150, 160))

    for ($i = 0; $i -lt $shown.Count; $i++) {
        $g.DrawString("$($shown[$i])px", $small, $grey, ($labelW + $i * $cell + 10), 20)
    }

    $y = 55
    foreach ($style in $styles) {
        foreach ($bg in @('dark', 'light')) {
            $bgColour = if ($bg -eq 'dark') {
                [System.Drawing.Color]::FromArgb(255, 32, 32, 38)
            } else {
                [System.Drawing.Color]::FromArgb(255, 243, 243, 246)
            }

            $bgBrush = New-Object System.Drawing.SolidBrush($bgColour)
            $g.FillRectangle($bgBrush, 0, $y, $width, $rowH)
            $bgBrush.Dispose()

            $textBrush = if ($bg -eq 'dark') { $white } else { $grey }
            $g.DrawString("$style", $font, $textBrush, 12, ($y + $rowH / 2 - 18))
            $g.DrawString("on $bg", $small, $grey, 12, ($y + $rowH / 2 + 2))

            for ($i = 0; $i -lt $shown.Count; $i++) {
                $size = $shown[$i]
                $icon = New-IconBitmap -Size $size -Style $style
                $x = $labelW + $i * $cell + [int](($cell - $size) / 2)
                $g.DrawImageUnscaled($icon, $x, ($y + [int](($rowH - $size) / 2)))
                $icon.Dispose()
            }

            $y += $rowH
        }
    }

    $g.Dispose()
    $sheet.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    $font.Dispose(); $small.Dispose(); $white.Dispose(); $grey.Dispose()

    Write-Output "wrote mockup sheet $Path"
}

Write-Ico -Path $OutputPath -Style $Design
if ($Mockup) { Write-Mockup -Path $MockupPath }

