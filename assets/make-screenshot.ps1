# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 penguinwokrs
#
# Renders the flyout panel for the README from the application's own XAML, so the picture cannot
# drift from the interface it documents. Nothing is captured from the screen and the application is
# never launched: the markup is loaded directly, filled with the values a connected headset reports,
# and rendered.
#
# Run on Windows:  powershell -File assets\make-screenshot.ps1 docs\images\flyout.png
#
# The window's own markup carries x:Class, which XamlReader cannot resolve without the compiled
# code-behind, so it is stripped before parsing. The flyout attaches its handlers in C# rather than
# in markup, so nothing else has to be removed.

param(
  [Parameter(Mandatory = $true)][string]$OutPath,
  [double]$Scale = 2.0
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$repo = Split-Path -Parent $PSScriptRoot
$trayDir = Join-Path $repo 'src\OpenInzone.Tray'

# StaticResource is resolved while parsing, so the icon dictionary has to be in place first.
$app = New-Object System.Windows.Application
$icons = [System.Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $trayDir 'Icons.xaml') -Raw -Encoding UTF8))
$app.Resources.MergedDictionaries.Add($icons)

$xaml = Get-Content (Join-Path $trayDir 'FlyoutWindow.xaml') -Raw -Encoding UTF8
$xaml = $xaml -replace '\s*x:Class="[^"]*"', ''
$window = [System.Windows.Markup.XamlReader]::Parse($xaml)

function Set-Text($name, $value) {
  $el = $window.FindName($name)
  if ($null -ne $el) { $el.Text = $value } else { Write-Warning "no element named $name" }
}
function Set-Slider($name, $value) {
  $el = $window.FindName($name)
  if ($null -ne $el) { $el.Value = $value } else { Write-Warning "no slider named $name" }
}

# The values a connected INZONE Buds reports, so the picture shows the real thing.
Set-Text 'ModelText' 'INZONE Buds'
Set-Slider 'VolumeSlider' 15;  Set-Text 'VolumeText' '15/30'
Set-Slider 'MicSlider' 100;    Set-Text 'MicText' '100%'
Set-Slider 'BalanceSlider' 50; Set-Text 'BalanceText' '50 (0.0)'
Set-Text 'BatteryText' 'L 36%   R 59%   ケース 42%'

# Render the panel itself. The Window is chromeless and transparent, so its content is the picture.
$width = [double]$window.Width
$panel = $window.Content
$window.Content = $null

# The panel has to carry the window's width itself once detached, or it measures to nothing.
$panel.Width = $width
$panel.Measure((New-Object System.Windows.Size([double]::PositiveInfinity, [double]::PositiveInfinity)))
$panel.Arrange((New-Object System.Windows.Rect(0, 0, $panel.DesiredSize.Width, $panel.DesiredSize.Height)))
$panel.UpdateLayout()

$w = [int][math]::Ceiling($panel.DesiredSize.Width)
$h = [int][math]::Ceiling($panel.DesiredSize.Height)
if ($w -le 0 -or $h -le 0) { throw "panel measured to ${w}x${h}; nothing to render" }
$dpi = 96 * $Scale
$rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
  [int]($w * $Scale), [int]($h * $Scale), $dpi, $dpi, [System.Windows.Media.PixelFormats]::Pbgra32)
$rtb.Render($panel)

$dir = Split-Path -Parent $OutPath
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
$fs = [System.IO.File]::Create($OutPath)
$enc.Save($fs)
$fs.Close()
Write-Output "wrote $OutPath ($([int]($w * $Scale))x$([int]($h * $Scale)))"
