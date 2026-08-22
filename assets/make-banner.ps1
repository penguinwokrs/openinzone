# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 penguinwokrs
#
# Draws the banner at the top of the README. The mark is taken from the generated application icon
# rather than redrawn, so the image at the top of the page and the icon in the notification area can
# never drift apart.
#
# Run on Windows:  powershell -File assets\make-banner.ps1 docs\images\banner.png
#
# Rendered at twice the display size so it stays sharp on a high-DPI screen; GitHub scales it down.

param(
  [Parameter(Mandatory = $true)][string]$OutPath,
  [int]$Width = 1280,
  [int]$Height = 320,
  [double]$Scale = 2.0
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$repo = Split-Path -Parent $PSScriptRoot
$icoPath = Join-Path $repo 'assets\openinzone.ico'

$w = $Width * $Scale
$h = $Height * $Scale

function Brush([string]$hex) {
  New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($hex))
}

$visual = New-Object System.Windows.Media.DrawingVisual
$dc = $visual.RenderOpen()

# A quiet vertical gradient rather than a flat fill, so the panel screenshot below it does not look
# like it is floating on the same slab of colour.
$gradient = New-Object System.Windows.Media.LinearGradientBrush
$gradient.StartPoint = New-Object System.Windows.Point(0, 0)
$gradient.EndPoint = New-Object System.Windows.Point(0, 1)
$gradient.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.ColorConverter]::ConvertFromString('#FF23262B'), 0)))
$gradient.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.ColorConverter]::ConvertFromString('#FF17191D'), 1)))
$dc.DrawRectangle($gradient, $null, (New-Object System.Windows.Rect(0, 0, $w, $h)))

# The mark, straight from the icon that ships with the application.
$stream = [System.IO.File]::OpenRead($icoPath)
$decoder = New-Object System.Windows.Media.Imaging.IconBitmapDecoder(
  $stream,
  [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
  [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
$frame = $decoder.Frames | Sort-Object PixelWidth -Descending | Select-Object -First 1
Write-Output "mark taken from the $($frame.PixelWidth)px frame"

$markSize = $h * 0.46
$markX = $w * 0.085
$markY = ($h - $markSize) / 2
$dc.DrawImage($frame, (New-Object System.Windows.Rect($markX, $markY, $markSize, $markSize)))
$stream.Close()

$textX = $markX + $markSize + ($w * 0.045)

$name = New-Object System.Windows.Media.FormattedText(
  'OpenInzone',
  [System.Globalization.CultureInfo]::InvariantCulture,
  [System.Windows.FlowDirection]::LeftToRight,
  (New-Object System.Windows.Media.Typeface(
    (New-Object System.Windows.Media.FontFamily('Segoe UI')),
    [System.Windows.FontStyles]::Normal,
    [System.Windows.FontWeights]::Light,
    [System.Windows.FontStretches]::Normal)),
  ($h * 0.235), (Brush '#FFF1F3F5'))

$tagline = New-Object System.Windows.Media.FormattedText(
  'Control a Sony INZONE headset without INZONE Hub',
  [System.Globalization.CultureInfo]::InvariantCulture,
  [System.Windows.FlowDirection]::LeftToRight,
  (New-Object System.Windows.Media.Typeface(
    (New-Object System.Windows.Media.FontFamily('Segoe UI')),
    [System.Windows.FontStyles]::Normal,
    [System.Windows.FontWeights]::Normal,
    [System.Windows.FontStretches]::Normal)),
  ($h * 0.085), (Brush '#FF9AA0A6'))

$gap = $h * 0.055
$blockH = $name.Height + $gap + $tagline.Height
$textY = ($h - $blockH) / 2

$dc.DrawText($name, (New-Object System.Windows.Point($textX, $textY)))
$dc.DrawText($tagline, (New-Object System.Windows.Point($textX, ($textY + $name.Height + $gap))))

# A hairline under the wordmark, picking up the panel's border colour.
$rule = New-Object System.Windows.Media.Pen((Brush '#FF3A3D42'), (1.5 * $Scale))
$ruleY = $textY + $name.Height + ($gap * 0.45)
$dc.DrawLine($rule,
  (New-Object System.Windows.Point($textX, $ruleY)),
  (New-Object System.Windows.Point(($textX + [math]::Max($name.Width, $tagline.Width)), $ruleY)))

$dc.Close()

$rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
  [int]$w, [int]$h, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
$rtb.Render($visual)

$dir = Split-Path -Parent $OutPath
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
$fs = [System.IO.File]::Create($OutPath)
$enc.Save($fs)
$fs.Close()
Write-Output "wrote $OutPath ($([int]$w)x$([int]$h))"
