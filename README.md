# inzone-buds-ctl

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
- The INZONE USB dongle
- .NET 8 SDK to build

INZONE Hub does not need to be closed. The control interface is opened with sharing enabled, so
both can be connected at once.

## Build

```sh
dotnet publish src/InzoneBuds.Cli    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
dotnet publish src/InzoneBuds.Daemon -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

This produces `publish/inzone.exe` and `publish/inzoned.exe`, each standalone. Drop
`--self-contained true` for much smaller binaries if the .NET 8 runtime is already installed.

Cross-building from WSL works: the projects target `net8.0` and reach Windows only through
P/Invoke and COM.

## Command line

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

```
$ inzone status
Device       INZONE Buds
Serial       L 3015430 / R 3015430 / dongle 3015430
Battery      L 98%  R 97%  case 34%
Balance      50 (0.0)
Volume       15/30
Microphone   unmuted, level 100%
Sidetone     0
```

The number in brackets after the balance is the same scale INZONE Hub shows, -5.0 to +5.0.

### Which volume is which

Worth being precise about, because the names invite the wrong guess:

| Command | What it moves |
|---|---|
| `inzone volume` | the **headset's own** volume, 0–30, the same slider INZONE Hub shows |
| `inzone mic` level | the **Windows capture endpoint** for the headset, 0–100 |
| `inzone mic mute` | the **headset's own** microphone mute |

`inzone volume` does not touch the Windows playback volume, and neither does INZONE Hub.
The microphone is the one setting INZONE Hub splits across both worlds, and this follows it.

## Hotkey daemon

`inzoned.exe` holds the connection open and listens for global hotkeys. Because it keeps the
device open and caches the current values, a held-down key applies one write per press instead
of a read and a write, so repeats stay responsive.

```sh
inzoned                       # uses %APPDATA%\inzone-buds-ctl\hotkeys.json
inzoned C:\path\to\keys.json  # or point it somewhere else
```

The config file is written with sensible defaults the first time it runs. See
`config/hotkeys.example.json`:

```json
{
  "bindings": [
    { "keys": "Ctrl+Alt+Up",    "action": "balance",   "delta": 10 },
    { "keys": "Ctrl+Alt+Down",  "action": "balance",   "delta": -10 },
    { "keys": "Ctrl+Alt+Home",  "action": "balance",   "value": 50 },
    { "keys": "Ctrl+Alt+Right", "action": "volume",    "delta": 1 },
    { "keys": "Ctrl+Alt+Left",  "action": "volume",    "delta": -1 },
    { "keys": "Ctrl+Alt+M",     "action": "mic-mute" },
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

To start it with Windows, put a shortcut to `inzoned.exe` in `shell:startup`. Add
`<OutputType>WinExe</OutputType>` to the daemon's project file first if you would rather it ran
without a console window.

## Using it as a library

`InzoneBuds.Core` has no dependencies beyond the framework.

```csharp
using var device = InzoneBudsDevice.Open();

Console.WriteLine(device.GetModelInfo().Name);   // INZONE Buds
device.AdjustMixBalance(+10);
device.ToggleMicMute();
device.SetMicLevel(80);

device.SettingChanged += (_, e) => Console.WriteLine($"{e.EventId} changed");
```

`device.Session` exposes raw `Get` and `Set` against any event id, for settings this wrapper
does not model yet. `docs/PROTOCOL.md` lists what is known.

## How it works

The dongle exposes the game and chat audio as two separate USB audio endpoints and mixes them in
hardware. The balance, the headphone volume and the mute flags are settings on the headset,
reached over a vendor HID collection on usage page `0xFF04` using a packet format of Sony's own.

`docs/PROTOCOL.md` documents the wire format in full, including where each detail was found in
INZONE Hub and which parts were confirmed against hardware.

## Layout

```
src/InzoneBuds.Core       protocol and transport
  Native/                 P/Invoke and COM declarations
  Hid/                    device discovery and report I/O
  Protocol/               packet codec and the request/response session
  Audio/                  the headset's Windows capture endpoint
  Model/                  typed values for each setting
src/InzoneBuds.Cli        inzone.exe
src/InzoneBuds.Daemon     inzoned.exe
docs/PROTOCOL.md          the reverse-engineered wire format
config/                   an example hotkey configuration
```

## Notes

This is an independent project. It is not affiliated with or endorsed by Sony, and INZONE is
their trademark. It was written for interoperability — using hardware you own from software you
choose — by reading the vendor application's own code and confirming the result against a device.

Writing values the firmware does not expect is a way to find out what happens the hard way. The
ranges here match what INZONE Hub itself sends.
