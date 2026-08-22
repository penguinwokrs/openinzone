# OpenInzone

An open, unofficial reimplementation of INZONE Hub's device control: a command line tool and a
hotkey daemon for Windows.

> **Not affiliated with Sony.** OpenInzone is an independent project. It is not affiliated with,
> authorised, sponsored or endorsed by Sony Group Corporation or any of its affiliates. "Sony",
> "INZONE" and "INZONE Hub" are trademarks of Sony Group Corporation or its affiliates, used here
> only to identify the hardware and the vendor application this project interoperates with.

Control a Sony INZONE headset from the command line or a physical key, without INZONE Hub.

INZONE Hub can adjust the headphone volume and the game/chat balance, but only through its own
window. This talks to the dongle directly over the same HID channel, so the same settings can be
bound to a key, scripted, or read from a status bar.

Built and verified against **INZONE Buds** (`VID_054C` / `PID_0EC2`). The protocol is shared
across the INZONE range, so other models are likely to work, but only INZONE Buds has been tested.

## What it can do

- Game/chat balance, 0–100
- Headphone volume, 0–30, and mute
- Microphone mute and level
- Battery for both earbuds and the case
- Watch for changes made elsewhere, including from the earbuds themselves

## Requirements

- Windows 10 1809 or later, x64
- The INZONE USB dongle, plugged in, with the earbuds out of the case and connected
- .NET 8 SDK to build

INZONE Hub does not need to be closed. The control interface is opened with sharing enabled, so
both can be connected at once — handy while trying this out, since you can watch INZONE Hub's own
sliders move.

## Quick start

### 1. Build

```sh
dotnet publish src/OpenInzone.Cli    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
dotnet publish src/OpenInzone.Daemon -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

That leaves two standalone executables in `publish/`. They need nothing installed on the machine
that runs them; drop `--self-contained true` for much smaller binaries if the .NET 8 runtime is
already present.

<details>
<summary>Building from WSL</summary>

This cross-builds from WSL without anything extra — the projects target `net8.0` and reach
Windows only through P/Invoke and COM. If the SDK was installed with the `dotnet-install.sh`
script rather than a package manager, put it on the path first:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
```

The resulting `.exe` runs straight from the WSL path through the usual interop, so
`./publish/inzone.exe status` works from the repository directory.
</details>

### 2. Check the dongle is found

```console
$ ./publish/inzone.exe devices
VID_054C&PID_0EC2 UsagePage=0xFF04 Usage=0x0001 In=64 Out=64 "Hid Interface"
  \\?\hid#vid_054c&pid_0ec2&mi_05&col03#8&29ddaaec&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}
```

Nothing listed means the dongle is not plugged in, or this model uses a product id the filter does
not recognise. See Troubleshooting.

### 3. Read the current settings

```console
$ ./publish/inzone.exe status
Device       INZONE Buds
Serial       L 3015430 / R 3015430 / dongle 3015430
Battery      L 97%  R 97%  case 34%
Balance      50 (0.0)
Volume       15/30
Microphone   unmuted, level 100%
Sidetone     0
```

If this prints numbers, everything below will work.

### 4. Change something

Open INZONE Hub next to the terminal and watch its slider move as you run these.

```console
$ ./publish/inzone.exe balance +10
60 (+1.0)

$ ./publish/inzone.exe balance +10
70 (+2.0)

$ ./publish/inzone.exe balance centre
50 (0.0)
```

The number in brackets is the scale INZONE Hub shows, -5.0 to +5.0.

To see it work in the other direction, leave this running and change the balance from INZONE Hub
or from the earbuds themselves:

```console
$ ./publish/inzone.exe watch
Watching INZONE Buds. Press Ctrl+C to stop.
01:20:20  GameChatMixBalance     60 (+1.0)
01:20:21  HeadphoneVolume        16/30
01:20:23  BatteryInfo            L 94%  R 94%  case 34%
01:20:24  MicVolume              muted
```

The headset's replies reach every program that has the interface open, so `watch` also shows
traffic caused by INZONE Hub or by another copy of this tool — not only changes made at the
earbuds. Repeated lines for one change are normal: the headset answers the request and then
announces the new value.

### 5. Bind it to a key

```console
$ ./publish/inzoned.exe config/hotkeys.example.json
Ctrl+Alt+Up          balance +10
Ctrl+Alt+Down        balance -10
Ctrl+Alt+Home        balance = 50
Ctrl+Alt+Right       volume +1
Ctrl+Alt+Left        volume -1
Ctrl+Alt+Shift+M     mic-mute
Ctrl+Alt+PageUp      mic-level +5
Ctrl+Alt+PageDown    mic-level -5

Listening. Press Ctrl+C to stop.
Connected to INZONE Buds - battery L 98%  R 97%  case 34%
```

Press `Ctrl+Alt+Up` and the balance moves, from any application. Each change is echoed in the
console, so you can tell a hotkey that did nothing from one that never arrived:

```
  balance  60 (+1.0)
  mic      level 95%
```

Run it without an argument and it uses `%APPDATA%\openinzone\hotkeys.json`, writing that
file with the defaults above the first time.

## Command reference

```
inzone status                 Show everything at once
inzone devices                List the control interfaces found

inzone balance                Show the game/chat balance
inzone balance 70             Set it (0 = all chat, 100 = all game)
inzone balance +10 | -10      Move it by a step
inzone balance centre         Back to the middle

inzone volume                 Show the headphone volume
inzone volume 20              Set it (0-30)
inzone volume +1 | -1         Move it by a step
inzone volume mute | unmute | toggle

inzone mic                    Show the microphone state
inzone mic mute | unmute | toggle
inzone mic 50                 Set the level (0-100)
inzone mic +5 | -5            Move the level by a step

inzone battery                Show charge levels
inzone watch                  Print changes as they happen
```

### Which volume is which

Worth being precise about, because the names invite the wrong guess:

| Command | What it moves |
|---|---|
| `inzone volume` | the **headset's own** volume, 0–30, the same slider INZONE Hub shows |
| `inzone mic` level | the **Windows capture endpoint** for the headset, 0–100 |
| `inzone mic mute` | the **headset's own** microphone mute |

`inzone volume` does not touch the Windows playback volume, and neither does INZONE Hub. The
microphone is the one setting INZONE Hub splits across both worlds, and this follows it.

## Hotkey daemon

`inzoned.exe` holds the connection open and listens for global hotkeys. Because it keeps the
device open and caches the current values, a held-down key applies one write per press instead of
a read and a write, so repeats stay responsive.

```sh
inzoned                       # uses %APPDATA%\openinzone\hotkeys.json
inzoned C:\path\to\keys.json  # or point it somewhere else
```

```json
{
  "bindings": [
    { "keys": "Ctrl+Alt+Up",    "action": "balance",   "delta": 10 },
    { "keys": "Ctrl+Alt+Down",  "action": "balance",   "delta": -10 },
    { "keys": "Ctrl+Alt+Home",  "action": "balance",   "value": 50 },
    { "keys": "Ctrl+Alt+Right", "action": "volume",    "delta": 1 },
    { "keys": "Ctrl+Alt+Left",  "action": "volume",    "delta": -1 },
    { "keys": "Ctrl+Alt+Shift+M",  "action": "mic-mute" },
    { "keys": "Ctrl+Alt+PageUp",   "action": "mic-level", "delta": 5 },
    { "keys": "Ctrl+Alt+PageDown", "action": "mic-level", "delta": -5 }
  ]
}
```

**Actions**: `balance`, `volume`, `mic-level`, `volume-mute`, `mic-mute`.
The first three take either `delta` to move by a step or `value` to jump to a number.

**Keys**: modifiers `Ctrl`, `Alt`, `Shift`, `Win`, plus one key. Letters and digits, `F1`–`F24`,
arrows, `Home`, `End`, `PageUp`, `PageDown`, `Insert`, `Delete`, `Space`, `Enter`, `Tab`,
`Escape`, `Backspace`, the numpad operators, and the media keys `VolumeUp`, `VolumeDown`,
`VolumeMute`, `MediaNext`, `MediaPrev`, `MediaStop`, `MediaPlayPause`.

A combination another application already holds is reported and skipped; the rest still register.

### Starting it with Windows

Put a shortcut to `inzoned.exe` in the folder that opens from `shell:startup`. To lose the console
window, add `<OutputType>WinExe</OutputType>` to `src/OpenInzone.Daemon/OpenInzone.Daemon.csproj`
and rebuild — note that this also hides the messages above, so get the bindings working first.

## Using it as a library

`OpenInzone.Core` has no dependencies beyond the framework. It is GPL-3.0-only, so anything
you distribute that links against it has to be GPL-3.0 as well.

```csharp
using var device = InzoneDevice.Open();

Console.WriteLine(device.GetModelInfo().Name);   // INZONE Buds
device.AdjustMixBalance(+10);
device.ToggleMicMute();
device.SetMicLevel(80);

device.SettingChanged += (_, e) => Console.WriteLine($"{e.EventId} changed");
```

`device.Session` exposes raw `Get` and `Set` against any event id, for settings this wrapper does
not model yet. `docs/PROTOCOL.md` lists what is known.

## Troubleshooting

**"No INZONE dongle found."**
The dongle is not plugged in, or it enumerates with a product id this does not know. Run
`inzone devices`; if that is empty too, check that a device with vendor `054C` and usage page
`0xFF04` is present. Discovery matches on capability rather than a fixed product id, so a
different INZONE model should still be found.

**"The headset did not answer ... within 1500 ms."**
The dongle is there but the earbuds are not reachable — still in the case, out of range, or
powered off. Take them out and try `inzone status` again.

**A hotkey is reported as already claimed.**
Something else registered that combination first; graphics drivers and chat applications are the
usual culprits. Pick another combination in the config. The remaining bindings still work.

**`inzone mic` shows the mute state but no level.**
Windows is not currently exposing a capture endpoint for the headset. The mute flag lives on the
headset and keeps working; the level is a Windows setting and needs that endpoint.

**Nothing appears when piping the daemon's output.**
Killing the process discards whatever the shell buffered. The daemon flushes each line as it
writes it, so `inzoned | tee log.txt` shows output live.

## How it works

The dongle exposes the game and chat audio as two separate USB audio endpoints and mixes them in
hardware. The balance, the headphone volume and the mute flags are settings on the headset,
reached over a vendor HID collection on usage page `0xFF04` using a packet format of Sony's own.

`docs/PROTOCOL.md` documents the wire format in full, including where each detail was found in
INZONE Hub and which parts were confirmed against hardware.

## Layout

```
src/OpenInzone.Core       protocol and transport
  Native/                 P/Invoke and COM declarations
  Hid/                    device discovery and report I/O
  Protocol/               packet codec and the request/response session
  Audio/                  the headset's Windows capture endpoint
  Model/                  typed values for each setting
src/OpenInzone.Cli        inzone.exe
src/OpenInzone.Daemon     inzoned.exe
docs/PROTOCOL.md          the reverse-engineered wire format
config/                   an example hotkey configuration
```

## License

GPL-3.0-only. See `LICENSE`.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. It talks to
hardware over an undocumented channel; you run it at your own risk.

## Trademarks and scope

OpenInzone is an independent, non-commercial project. It is not affiliated with, authorised,
sponsored or endorsed by Sony Group Corporation or its affiliates.

"Sony", "INZONE" and "INZONE Hub" are trademarks of Sony Group Corporation or its affiliates.
They appear in this repository only to identify the hardware this project talks to and the vendor
application whose behaviour it reproduces — no more of those marks than that requires. No Sony
logo, product photograph, typeface or artwork from INZONE Hub is used or redistributed here.

The project exists for interoperability: using hardware you own from software you choose. The
wire format in `docs/PROTOCOL.md` is a description of observed behaviour, confirmed against a
device. It contains no code, resources or assets taken from INZONE Hub.

Deliberately out of scope, and not accepted as contributions:

- firmware update, firmware extraction, or redistribution of any Sony firmware image
- circumventing any protection, licence check, or restriction
- redistributing any part of INZONE Hub, including decompiler or disassembler output
- anything that presents this project as an official or endorsed Sony product

Writing values the firmware does not expect is a way to find out what happens the hard way. The
ranges here match what INZONE Hub itself sends.
