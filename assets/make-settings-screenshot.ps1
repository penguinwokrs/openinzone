# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 penguinwokrs
#
# Renders each tab of the settings window from the application's own markup, so the picture cannot
# drift from the interface it documents. Nothing is captured from the screen and the application is
# never launched.
#
# Run on Windows:  pwsh -File assets\make-settings-screenshot.ps1 C:\somewhere
#
# Three things have to be undone before XamlReader will take the markup: x:Class, which it cannot
# resolve without the compiled code-behind; the event handlers, which this window attaches in
# markup rather than in C# as the flyout does; and the device tab's Setting attached properties,
# which live in the same code-behind assembly and are the tray's business rather than the
# renderer's - what they would set is set below by hand anyway.
#
# SettingsWindow.xaml also carries xmlns:res="clr-namespace:OpenInzone.Resources;assembly=..." and
# uses {x:Static res:Strings...} throughout. Both resolve while XamlReader parses the markup, so
# the resource assembly - built if it is not already there, satellite directories and all - has to
# be loaded, and the UI culture set to Japanese, before Parse runs below. This produces the
# Japanese screenshots; an English Windows with no culture pinned would otherwise get English text
# in pictures that are meant to show the Japanese UI.

param(
  [Parameter(Mandatory = $true)][string]$OutDirectory,
  [double]$Scale = 2.0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$repo = Split-Path -Parent $PSScriptRoot

# This checks for the resource assembly rather than building it. Building it from here was tried
# and does not work: the .NET SDK lives on the WSL side of this machine and not the Windows side,
# so `dotnet build` fails - and it failed *silently*, leaving whatever dll happened to be lying
# around to be loaded as though the build had succeeded, which is the opposite of what building it
# here was meant to guarantee.
#
# Build it first, from wherever you build this project:
#     dotnet build src/OpenInzone.Resources -c Release
#
# That also lays the ja\ and zh-Hans\ satellite directories down beside the dll, which is what
# makes the Japanese strings reachable at all. The timestamp is printed because a stale dll is the
# one failure this cannot detect for you.
$resourcesProject = Join-Path $repo 'src\OpenInzone.Resources'
$resourcesDll = Join-Path $resourcesProject 'bin\Release\net8.0\OpenInzone.Resources.dll'
if (-not (Test-Path $resourcesDll)) {
  throw "$resourcesDll is missing. Build it first: dotnet build src/OpenInzone.Resources -c Release"
}
Write-Host "using $resourcesDll (built $((Get-Item $resourcesDll).LastWriteTime))"
Add-Type -Path $resourcesDll

# {x:Static} resolves at parse time, not render time, so this has to be set before Parse is called
# below - setting it any later would leave the already-parsed text in whatever culture the host
# happened to be running under.
[System.Threading.Thread]::CurrentThread.CurrentUICulture = 'ja'

$xaml = Get-Content (Join-Path $repo 'src\OpenInzone.Tray\SettingsWindow.xaml') -Raw -Encoding UTF8
New-Item -ItemType Directory -Path $OutDirectory -Force | Out-Null
$xaml = $xaml -replace '\s*x:Class="[^"]*"', ''
# \s+ rather than \s*: without it this also eats the Checked= inside a template's IsChecked=.
$xaml = $xaml -replace '\s+(Click|Checked|Unchecked|ValueChanged|SelectionChanged)="[^"]*"', ''
$xaml = $xaml -replace '\s+tray:Setting\.[A-Za-z]+="[^"]*"', ''
$xaml = $xaml -replace '\s+xmlns:tray="[^"]*"', ''

$app = New-Object System.Windows.Application
$window = [System.Windows.Markup.XamlReader]::Parse($xaml)

function Find($name) { $window.FindName($name) }

# The hotkey table binds to DisplayName / Display / Brush, which any object can supply.
$rows = @(
  @{ n='音量を上げる'; d='Ctrl + Alt + Right'; b='White' },
  @{ n='音量を下げる'; d='Ctrl + Alt + Left'; b='White' },
  @{ n='バランスをゲーム寄りに'; d='Ctrl + Alt + Up'; b='White' },
  @{ n='バランスをチャット寄りに'; d='Ctrl + Alt + Down'; b='White' },
  @{ n='バランスを中央に'; d='Ctrl + Alt + Home'; b='White' },
  @{ n='マイクミュート切り替え'; d='Ctrl + Alt + Shift + M（他のアプリが使用中）'; b='IndianRed' },
  @{ n='マイクレベルを上げる'; d='Ctrl + Alt + PageUp'; b='White' },
  @{ n='マイクレベルを下げる'; d='未割り当て'; b='White' }
) | ForEach-Object {
  [pscustomobject]@{ DisplayName = $_.n; Display = $_.d; Id = $_.n
    Brush = [System.Windows.Media.Brushes]::($_.b) }
}
(Find 'Rows').ItemsSource = $rows

(Find 'VersionText').Text = '現在のバージョン: 0.3.0'
(Find 'UpdateStatusText').Text = '最新バージョンです。'
(Find 'PluginStatusText').Text = '保存しました: C:\Users\owner\Downloads\com.penguinwokrs.openinzone.streamDeckPlugin'
(Find 'PluginOpenButton').Visibility = 'Visible'
(Find 'AutostartBox').IsChecked = $true

# The device tab, filled with a reading a connected headset would give.
(Find 'DevicePanel').IsEnabled = $true
(Find 'DeviceStatusText').Text = '変更はその場で反映されます。'
(Find 'AmbientButton').IsChecked = $true
(Find 'AmbientLevelSlider').Value = 14
(Find 'AmbientLevelText').Text = '14'
(Find 'VoiceFocusBox').IsChecked = $true
(Find 'SidetoneSlider').Value = 3
(Find 'SidetoneText').Text = '3'
(Find 'AutoPowerOffBox').IsChecked = $true
(Find 'BluetoothAutoSwitchBox').IsChecked = $true
(Find 'VoiceGuidanceBox').IsChecked = $true
# By tag rather than by position, as the window itself does: the list is in byte order, and
# 0x01 is Japanese.
(Find 'LanguageBox').SelectedItem = (Find 'LanguageBox').Items | Where-Object { $_.Tag -eq '1' }

# The display language, which is a different thing from the voice guidance language above: this one
# says what the window itself is in. The window's constructor is what normally selects it, and the
# constructor never runs here, so without this the general tab is photographed with an empty combo.
(Find 'UiLanguageBox').SelectedItem = (Find 'UiLanguageBox').Items | Where-Object { $_.Tag -eq 'ja' }

# A Window has no visual tree until it is shown, so its content is taken out and rendered on a
# surface of the window's own colour instead.
$tabs = $window.Content
$window.Content = $null
$surface = New-Object System.Windows.Controls.Border
$surface.Background = $window.Background
# Taking the content out of the window also takes it out of reach of Window.Resources, and the
# implicit styles live there - without this the buttons and the tab strip come back as default
# light chrome and the text stops wrapping, none of which is what the application looks like.
foreach ($key in $window.Resources.Keys) { $surface.Resources[$key] = $window.Resources[$key] }
$surface.Child = $tabs
$surface.Width = $window.Width
$surface.Height = $window.Height

for ($i = 0; $i -lt $tabs.Items.Count; $i++) {
  $tabs.SelectedIndex = $i
  $surface.Measure([System.Windows.Size]::new($window.Width, $window.Height))
  $surface.Arrange([System.Windows.Rect]::new(0, 0, $window.Width, $window.Height))
  $surface.UpdateLayout()

  $rtb = [System.Windows.Media.Imaging.RenderTargetBitmap]::new([int]($window.Width * $Scale), [int]($window.Height * $Scale), 96 * $Scale, 96 * $Scale, [System.Windows.Media.PixelFormats]::Pbgra32)
  $rtb.Render($surface)

  $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
  $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
  $out = Join-Path $OutDirectory "tab-$i.png"
  $fs = [System.IO.File]::Create($out)
  $enc.Save($fs)
  $fs.Close()
  Write-Output "wrote $out"
}
