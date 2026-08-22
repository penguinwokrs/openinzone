# Tray application and installer — design

Date: 2026-08-23
Status: approved

## Goal

Replace the console hotkey daemon with a tray application that puts the three settings people
actually reach for behind one left click, keeps the global hotkeys, and ships as an installer a
non-developer can run.

## What the user asked for

- Tray-resident application. Left click opens a panel with three sliders: headphone volume,
  microphone level, game/chat balance.
- Battery percentages for the left and right earbuds.
- An icon in each row, and clicking the volume or microphone icon toggles that mute.
- Right click offers **設定** (settings), which opens a window for assigning a hotkey to every
  command the application offers, each with a default already assigned.
- An installer, with a step that installs prerequisites and skips them when already present.
- Afterwards: a GitHub release workflow that fills in the release notes automatically.

## Decisions

| Question | Decision | Why |
|---|---|---|
| Which "master volume" | The headset's own volume, 0–30, over HID | It is what INZONE Hub's slider shows and what `inzone volume` already controls. The Windows render endpoint is a different value that INZONE Hub never touches. |
| Relation to `inzoned.exe` | The tray application replaces it; the project is deleted | `RegisterHotKey` is first-come-first-served, so two hotkey owners would fight. One startup path is also one thing to maintain. `inzone.exe` (CLI) is untouched. |
| Deployment | Self-contained, packaged by Inno Setup | Chosen for reliability over size. See "The prerequisite step" below for the consequence. |
| Hotkey model | A fixed catalogue of named commands, one key each | Matches "every command the application offers, each with a default". Simpler to present than free-form bindings, and it makes an unassigned command visible at a glance. |
| UI toolkit | WPF window plus WinForms `NotifyIcon` | `NotifyIcon` is the only practical tray API. WPF gives usable sliders without custom drawing. Both cross-build from WSL; see "Verified constraints". |

## Verified constraints

Checked on this machine before the design was settled, because they decide what is buildable here:

- A project with both `UseWPF` and `UseWindowsForms`, plus `EnableWindowsTargeting`, restores,
  compiles XAML and publishes for `win-x64` from WSL. The framework-dependent build was run on
  Windows and its window opened, so the cross-built binary is not merely produced but works.
- Self-contained single-file publish is ~153 MB; framework-dependent is ~184 KB.
- The `wix` dotnet tool warns that behaviour off Windows is undefined and then fails to resolve
  paths, at both v5 and v7. MSI authoring cannot happen on the Linux side.
- Inno Setup is not installed but is available through `winget` as `JRSoftware.InnoSetup`.
  `ISCC.exe` can be invoked from WSL through the usual interop.
- The Windows side already has `Microsoft.WindowsDesktop.App 8.0.16`, contrary to what the
  handoff document says. That note needs correcting.

## The prerequisite step

Self-contained deployment carries its own runtime, and the control paths use only HID and WASAPI,
which ship with the OS. There is therefore nothing left for a prerequisite step to install. The
installer keeps a single check — Windows 10 1809 or later — and installs no libraries. The
requested "install the required libraries, skip when present" step is deliberately absent because
under this deployment choice it would have nothing to do.

## Layout

```
src/OpenInzone.Core        unchanged: protocol and transport
src/OpenInzone.Control     new, net8.0, no UI
src/OpenInzone.Cli         unchanged: inzone.exe
src/OpenInzone.Tray        new, net8.0-windows, WPF: inzonetray.exe
src/OpenInzone.Daemon      deleted
tests/OpenInzone.Core.Tests
installer/openinzone.iss   new
assets/                    new: the application icon and the script that generates it
.github/workflows/         new: release workflow
```

`OpenInzone.Control` exists so that the hotkey catalogue, the configuration format and the device
state reducer can be tested without a window. The current `DeviceController` reports results by
writing to `Console`, which a tray application cannot use, so it moves here and reports through
events instead.

## Components

### OpenInzone.Control

**`DeviceState`** — an immutable snapshot: connected flag, model name, `MixBalance`,
`HeadphoneVolume`, `MicVolume`, microphone level and whether one is available, `BatteryInfo`.

**`DeviceController`** — owns the `InzoneDevice` and a worker thread, as today. Actions are posted
to the worker so a held key never stalls the UI. It caches current values, keeps them in step with
the headset's own notifications, and raises `StateChanged` with a fresh `DeviceState` after every
change from either direction. Reconnect backoff stays at two seconds. No console output.

**`HotkeyCommand`** — the fixed catalogue. Each entry has a stable id, a display name, a default
combination and the action to run against `DeviceController`. Adding a command means adding one
entry here; the settings window and the hotkey host both read from this list, so neither needs
changing.

**`HotkeyConfig`** — persistence at `%APPDATA%\openinzone\hotkeys.json`, now keyed by command id
rather than by an action/delta/value triple. A file in the old format is recognised and migrated
on load, so an existing daemon user keeps their bindings.

**`KeyCombo`** — parsing and formatting of combinations such as `Ctrl+Alt+Shift+M`, and the
modifier and virtual-key values `RegisterHotKey` wants.

### OpenInzone.Tray

**`HotkeyHost`** — a hidden window, `RegisterHotKey` per bound command, `WM_HOTKEY` dispatch.
Registration failures are collected rather than thrown, so one taken combination does not stop the
rest. A single-instance mutex prevents a second copy from fighting over the same combinations.

**`TrayIcon`** — the `NotifyIcon`. Left click opens the flyout, right click opens a menu with
設定 and 終了. The tooltip carries the model name and battery, or the disconnected state.

**`FlyoutWindow`** — a chromeless window positioned at the bottom right of
`SystemParameters.WorkArea`, closed when it loses focus.

**`SettingsWindow`** — the hotkey table and the autostart checkbox.

**`Autostart`** — the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value.

## The flyout

```
┌────────────────────────────┐
│ INZONE Buds                │
│ 🔊  ━━━━━●━━━━━   15/30    │
│ 🎤  ━━━━━━━━●━━   100%     │
│ 🎮  ━━━━━●━━━━━   50 (0.0) │
│ ────────────────────────── │
│ 🎧 L 97%   R 97%   case 34%│
└────────────────────────────┘
```

Rows, and what each one actually writes:

| Row | Slider | Icon click |
|---|---|---|
| Headphone volume | headset's own volume, 0–30, over HID | headset mute, over HID |
| Microphone | Windows capture endpoint, 0–100 | headset microphone mute, over HID |
| Game/chat balance | balance 0–100, labelled on Hub's −5.0…+5.0 scale | none |

The microphone row deliberately sends its two halves to different places. That is what INZONE Hub
does, and `docs/PROTOCOL.md` records why: only the mute flag is on the wire.

Icons are drawn as XAML vector paths rather than shipped as images. That keeps them sharp at any
DPI and keeps the repository free of artwork. A muted row draws the icon struck through and dims
its slider.

**Writes during a drag are throttled.** A slider raises a value change per pixel, and sending each
one would flood the HID channel. Changes are debounced at 100 ms, and the value at the end of the
drag is always sent so the device cannot be left disagreeing with the UI.

Battery shows left, right and case, since `GetBattery()` reports all three on earbud models. A
percentage of `0xFF` means the part is not reporting and displays as `--`.

## The settings window

One row per command in the catalogue, each with the combination currently assigned. Selecting a
row and pressing a combination captures it. Because `RegisterHotKey` is first-come-first-served,
a combination another application already holds can be detected at capture time and marked, before
anything is saved.

| Command | Default |
|---|---|
| Volume up / down | `Ctrl+Alt+Right` / `Ctrl+Alt+Left` |
| Volume mute toggle | `Ctrl+Alt+Shift+V` |
| Balance toward game / toward chat | `Ctrl+Alt+Up` / `Ctrl+Alt+Down` |
| Balance to centre | `Ctrl+Alt+Home` |
| Microphone mute toggle | `Ctrl+Alt+Shift+M` |
| Microphone level up / down | `Ctrl+Alt+PageUp` / `Ctrl+Alt+PageDown` |

Every command ships with a default, as asked. The daemon left volume mute unbound; it gains
`Ctrl+Alt+Shift+V` here, mirroring the microphone's `Ctrl+Alt+Shift+M`. `Ctrl+Alt+M` is left alone:
another application on the development machine already holds it, which is why the daemon's default
moved to `Ctrl+Alt+Shift+M` in the first place.

The window also carries the autostart checkbox. Saving rewrites the configuration and re-registers
the hotkeys without a restart.

## Installer

Inno Setup, script at `installer/openinzone.iss`, built by invoking the Windows-side `ISCC.exe`
from WSL. It installs the self-contained `inzonetray.exe` and `inzone.exe`, creates a Start menu
entry, offers autostart as a checked option, and refuses to run below Windows 10 1809. Uninstall
removes the program but leaves `%APPDATA%\openinzone` alone, so settings survive a reinstall.

## Release workflow

Triggered by a `v*` tag. `windows-latest` publishes both executables self-contained, runs Inno
Setup, and creates the release with the installer and a zip attached. Release notes come from
GitHub's own generation rather than a hand-written body, with `.github/release.yml` grouping
entries by label. Whether the runner image already carries Inno Setup is checked during
implementation; if not, the workflow installs it before building.

## Error handling

| Situation | Behaviour |
|---|---|
| No dongle, or the earbuds are unreachable | Flyout shows the disconnected state with sliders disabled; the controller retries with its existing backoff |
| Windows exposes no capture endpoint | Microphone slider disabled with a note; the mute icon still works, since mute is on the headset |
| A hotkey combination is already taken | That row is marked in the settings window; the remaining bindings still register |
| A write times out | Treated as a disconnect, state refreshed on the next successful connection |

## Testing

New tests in the existing project, against `OpenInzone.Control`:

- `KeyCombo` parse and format round-trip, including every supported modifier and key name
- the catalogue: every command has a default, and no two defaults collide
- migration of an old-format `hotkeys.json`, including a file that mixes both shapes
- the state reducer: applying a notification parameter to `DeviceState` yields the expected values

The WPF windows, `RegisterHotKey` and real device I/O are not covered: they need a running desktop
and the hardware. That boundary is the reason the logic lives in a separate project.

## Out of scope

- Settings beyond hotkeys and autostart. Sidetone, surround and noise cancelling remain unmodelled;
  `docs/PROTOCOL.md` lists them.
- Localisation infrastructure. The window is Japanese, matching the request.
- Code signing. The installer will be unsigned, so SmartScreen will warn on first run.
