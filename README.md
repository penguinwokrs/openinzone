# OpenInzone

English · [日本語](README.ja.md)

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

Nothing else needs installing. The download is self-contained, so there is no .NET runtime to set
up first.

Windows only. On Linux, [zoneout](https://github.com/marcinjakubowski/zoneout) covers the same
device and more of it — see [Related projects](#related-projects).

INZONE Hub does not need to be closed. The control interface is opened with sharing enabled, so
both can be connected at once — handy while trying this out, since you can watch INZONE Hub's own
sliders move.

## Install

### 1. Download

Take `OpenInzone-win-x64.zip` from the
[latest release](https://github.com/penguinwokrs/openinzone/releases/latest). It contains two
programs:

| | |
|---|---|
| `inzone.exe` | the command line tool — read and change settings |
| `inzoned.exe` | the hotkey daemon — bind those settings to keys |

### 2. Unpack it somewhere permanent

Open Windows Terminal or PowerShell (right-click the Start button → **Terminal**) and run:

```powershell
$dir = "$env:LOCALAPPDATA\OpenInzone"
Expand-Archive "$env:USERPROFILE\Downloads\OpenInzone-win-x64.zip" -DestinationPath $dir -Force
Get-ChildItem $dir -Recurse | Unblock-File
```

`Unblock-File` clears the mark Windows puts on anything that came from the internet. Without it
the first run is met with **"Windows protected your PC"**. These builds are not code-signed, so
if SmartScreen still appears, choose **More info → Run anyway**.

### 3. Put it on PATH

So that `inzone` works from any directory, not just that folder:

```powershell
[Environment]::SetEnvironmentVariable(
    "Path",
    [Environment]::GetEnvironmentVariable("Path", "User") + ";$env:LOCALAPPDATA\OpenInzone",
    "User")
```

Close the terminal and open a new one for that to take effect.

This step is optional. Skipping it only means writing `.\inzone.exe` from inside the folder
wherever the examples below say `inzone`.

### 4. Check the headset is found

With the dongle plugged in and the earbuds out of the case:

```console
PS> inzone status
Device       INZONE Buds
Serial       L 3015430 / R 3015430 / dongle 3015430
Battery      L 97%  R 97%  case 34%
Balance      50 (0.0)
Volume       15/30
Microphone   unmuted, level 100%
Sidetone     0
```

If this prints numbers, everything below will work. If it prints an error instead, see
[Troubleshooting](#troubleshooting).

## Using it

### Change something

Open INZONE Hub next to the terminal and watch its slider move as you run these.

```console
PS> inzone balance +10
60 (+1.0)

PS> inzone balance +10
70 (+2.0)

PS> inzone balance centre
50 (0.0)
```

The number in brackets is the scale INZONE Hub shows, -5.0 to +5.0.

The headphone volume and the microphone work the same way:

```console
PS> inzone volume 20
20/30

PS> inzone volume -1
19/30

PS> inzone mic toggle
muted
```

### Battery

`inzone battery` shows charge for both earbuds and the case.

The case percentage is not a live reading. The case has no radio of its own, so its level only
reaches the dongle when you put an earbud in — that is the moment it catches up. A case left
charging on its own keeps reporting the same number however long you wait: in one measurement it
sat at 36% for 37 minutes on a charger, then jumped straight to 42% the moment an earbud went in.
Both earbuds in the case means nothing answers at all, and `inzone battery` says so and exits 1.

**Charging is not reported.** The headset sends nothing that distinguishes charging from not
charging, so neither this tool nor INZONE Hub can show it. An earbud that has been in the case
simply comes back with a higher percentage.

### Watch changes as they happen

Leave this running and change the balance from INZONE Hub, or from the earbuds themselves:

```console
PS> inzone watch
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

This is what makes a status bar or stream overlay possible: pipe it somewhere and read it.

### Bind it to a key

`inzoned.exe` stays running, holds the connection open, and listens for global hotkeys. Start it
with no argument and it uses `%APPDATA%\openinzone\hotkeys.json`, writing that file with a set of
defaults the first time:

```console
PS> inzoned
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

Press `Ctrl+Alt+Up` and the balance moves, from any application — including from inside a
full-screen game. Each change is echoed in the console, so you can tell a hotkey that did nothing
from one that never arrived:

```
  balance  60 (+1.0)
  mic      level 95%
```

Because it keeps the device open and caches the current values, a held-down key applies one write
per press instead of a read and a write, so repeats stay responsive.

### Editing the bindings

Open `%APPDATA%\openinzone\hotkeys.json` — `notepad $env:APPDATA\openinzone\hotkeys.json` — or
point the daemon at another file with `inzoned C:\path\to\keys.json`.

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

Restart the daemon after editing the file.

### Starting the daemon with Windows

Press `Win+R`, enter `shell:startup`, and put a shortcut to `inzoned.exe` in the folder that
opens. The quickest way to create one:

```powershell
$link = (New-Object -ComObject WScript.Shell).CreateShortcut(
    "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\OpenInzone.lnk")
$link.TargetPath = "$env:LOCALAPPDATA\OpenInzone\inzoned.exe"
$link.Save()
```

A console window stays open while it runs. Minimising it is enough; getting rid of it entirely
needs a rebuild, described under [Developer guide](#a-windowless-daemon).

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
inzone watch battery          Print changes to one event only
                              (battery, balance, volume, mic, sidetone)

--json                        Any command, as one JSON object
                              (watch emits one object per line)
--raw                         Add the undecoded bytes to battery output
```

`inzone --help` prints the same list.

### Which volume is which

Worth being precise about, because the names invite the wrong guess:

| Command | What it moves |
|---|---|
| `inzone volume` | the **headset's own** volume, 0–30, the same slider INZONE Hub shows |
| `inzone mic` level | the **Windows capture endpoint** for the headset, 0–100 |
| `inzone mic mute` | the **headset's own** microphone mute |

`inzone volume` does not touch the Windows playback volume, and neither does INZONE Hub. The
microphone is the one setting INZONE Hub splits across both worlds, and this follows it.

## Troubleshooting

**"No INZONE dongle found."**
The dongle is not plugged in, or it enumerates with a product id this does not know. Run
`inzone devices`, which lists what was found:

```console
PS> inzone devices
VID_054C&PID_0EC2 UsagePage=0xFF04 Usage=0x0001 In=64 Out=64 "Hid Interface"
  \\?\hid#vid_054c&pid_0ec2&mi_05&col03#8&29ddaaec&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}
```

If that is empty too, check that a device with vendor `054C` and usage page `0xFF04` is present.
Discovery matches on capability rather than a fixed product id, so a different INZONE model should
still be found.

**"The headset did not answer ... within 1500 ms."**
The dongle is there but the earbuds are not reachable — still in the case, out of range, or
powered off. Take them out and try `inzone status` again.

**"Windows protected your PC", or the exe will not start.**
The download still carries its mark of the web. Run
`Get-ChildItem $env:LOCALAPPDATA\OpenInzone -Recurse | Unblock-File`. Nothing here is code-signed,
so SmartScreen may still want **More info → Run anyway**.

**`inzone` is not recognised as a command.**
The PATH step was skipped, or the terminal predates it. Open a new terminal, or run the executable
by path: `& "$env:LOCALAPPDATA\OpenInzone\inzone.exe" status`.

**A hotkey is reported as already claimed.**
Something else registered that combination first; graphics drivers and chat applications are the
usual culprits. Pick another combination in the config. The remaining bindings still work.

**`inzone mic` shows the mute state but no level.**
Windows is not currently exposing a capture endpoint for the headset. The mute flag lives on the
headset and keeps working; the level is a Windows setting and needs that endpoint.

**Nothing appears when piping the daemon's output.**
Killing the process discards whatever the shell buffered. The daemon flushes each line as it
writes it, so `inzoned | tee log.txt` shows output live.

## Scripting

`--json` works on every command, not just `battery` — add it and you get one JSON object on
stdout instead of the aligned columns. `watch --json` prints one object per line instead, so each
line is a complete record on its own.

### Examples

```console
$ inzone battery --json
{"left":51,"right":71,"case":34,"detail":{"left_state":"reporting","right_state":"reporting","case_state":"reporting","case_is_snapshot":true}}
```

```console
$ inzone status --json
{"device":"INZONE Buds","serial":{"left":"3015430","right":"3015430","dongle":"3015430"},"battery":{"left":51,"right":71,"case":34,"detail":{…}},"balance":{"value":50,"notch":0},"volume":{"value":15,"max":30,"muted":false},"mic":{"muted":false,"level":100,"level_available":true},"sidetone":{"value":0}}
```

```console
$ inzone watch battery --json
{"time":"05:21:11","event":"battery","left":57,"right":78,"case":34,"detail":{…}}
{"time":"05:23:21","event":"battery","left":56,"right":78,"case":34,"detail":{…}}
```

The vocabulary matches on purpose: `inzone watch battery` and `jq 'select(.event=="battery")'`
pick out the same thing, one server-side and one client-side.

```console
$ inzone watch --json | jq -c 'select(.event=="battery")'
```

### What the battery keys mean

`left`, `right` and `case` are percentages, or `null` when that part is not reporting — an earbud
sitting in the case, or a case level that has never been relayed.

A headset model has no separate right earbud and no case, so on that model those keys are
**absent** from the object rather than `null`. `null` means "this part exists but is not
reporting right now"; a missing key means "this model has no such part".

`detail.case_is_snapshot` is always `true` for earbuds. The case has no radio of its own — an
earbud relays its level when it is docked, so the number is a snapshot from that moment, not a
live reading.

`detail.raw` appears only with `--raw`, and carries the undecoded bytes.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | The device side failed — no dongle, earbuds in the case, no answer |
| 2 | The command itself was wrong — unknown command, unknown watch filter, a value that is not a number |

The split is there for anything polling on a timer: it can tell "I typed this wrong" apart from
"they are charging", and decide whether retrying is worth it.

### Errors are JSON too

In text mode, errors go to stderr, same as always. Under `--json`, everything goes to stdout —
success or failure — so a consumer reading stdout gets exactly one object either way:

```console
$ inzone battery --json          # with both earbuds in the case
{"error":"unreachable","message":"The earbuds did not answer. They are in the case, out of range, or off."}
```

### Watch filters

`inzone watch` takes filter words to print changes to just one thing: `battery`, `balance`,
`volume`, `mic`, `sidetone` — the same words the `event` field carries above.

---

## Developer guide

Everything below is about building OpenInzone from source and working on it. None of it is needed
to use the released build.

### What you need

- .NET 8 SDK
- The dongle and the earbuds, for anything beyond the protocol tests
- Windows to run it; the build itself also works from Linux or WSL

The projects target `net8.0` and reach Windows only through P/Invoke and COM, so they compile
anywhere the SDK runs. Only the resulting `.exe` is Windows-only.

### Building on Windows

```powershell
winget install Microsoft.DotNet.SDK.8
git clone https://github.com/penguinwokrs/openinzone.git
cd openinzone

dotnet publish src\OpenInzone.Cli    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
dotnet publish src\OpenInzone.Daemon -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

That leaves `inzone.exe` and `inzoned.exe` in `publish\`, needing nothing installed on the machine
that runs them — the same thing the release zip contains. Drop `--self-contained true` for much
smaller binaries if the .NET 8 runtime is already present.

```console
PS> .\publish\inzone.exe status
```

`dotnet run --project src\OpenInzone.Cli -- status` works too, for a quick loop without
publishing.

### Building from WSL

This cross-builds from WSL without anything extra. If the SDK was installed with the
`dotnet-install.sh` script rather than a package manager, put it on the path first:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
```

```sh
dotnet publish src/OpenInzone.Cli    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
dotnet publish src/OpenInzone.Daemon -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The resulting `.exe` runs straight from the WSL path through the usual interop, so
`./publish/inzone.exe status` works from the repository directory — the dongle is reached through
the Windows side, no USB passthrough involved.

The daemon is the exception: WSL is a poor place to exercise it, since global hotkeys are
registered against the Windows session. It runs, but test it from a Windows terminal.

### Tests

The protocol layer has unit tests. They are plain managed code with no device involved, so they
run anywhere the SDK does, WSL included:

```sh
dotnet test
```

The expected packets come from the worked example in `docs/PROTOCOL.md`, captured from a real
dongle. They pin the framing, the address nibbles, the little endian transaction id and where each
checksum starts — the last one differing between commands and events is the detail most likely to
be reintroduced by mistake.

Discovery, report I/O and the Windows audio endpoint need the hardware and are not covered.

### Layout

```
src/OpenInzone.Core       protocol and transport
  Native/                 P/Invoke and COM declarations
  Hid/                    device discovery and report I/O
  Protocol/               packet codec and the request/response session
  Audio/                  the headset's Windows capture endpoint
  Model/                  typed values for each setting
src/OpenInzone.Cli        inzone.exe
src/OpenInzone.Daemon     inzoned.exe
tests/OpenInzone.Core.Tests
  Protocol/               packet codec tests, checked against docs/PROTOCOL.md
docs/PROTOCOL.md          the reverse-engineered wire format
config/                   an example hotkey configuration
```

`OpenInzone.sln` ties the four projects together for Visual Studio and Rider.

### A windowless daemon

To lose the daemon's console window, add `<OutputType>WinExe</OutputType>` to
`src/OpenInzone.Daemon/OpenInzone.Daemon.csproj` and rebuild. Note that this also hides the
startup listing and the per-change echo, so get the bindings working first.

### Using it as a library

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

### How it works

The dongle exposes the game and chat audio as two separate USB audio endpoints and mixes them in
hardware. The balance, the headphone volume and the mute flags are settings on the headset,
reached over a vendor HID collection on usage page `0xFF04` using a packet format of Sony's own.

`docs/PROTOCOL.md` documents the wire format in full, including where each detail was found in
INZONE Hub and which parts were confirmed against hardware.

## Related projects

Other people have taken on the same hardware. Where the work overlaps, this is what each one is
for.

| Project | Platform | What it does |
|---|---|---|
| [HeadsetControl](https://github.com/Sapd/HeadsetControl) | Windows, macOS, Linux | Controls a wide range of gaming headsets from one CLI. INZONE H5 gets sidetone, chat mix and microphone volume; INZONE Buds is battery only, read passively. |
| [zoneout](https://github.com/marcinjakubowski/zoneout) | Linux | CLI, Qt GUI and Python library for INZONE H9 II and INZONE Buds. Reaches further into the device than this project does: noise cancelling, ambient sound, auto power off, voice guide language, boot defaults. |
| [LINZONE Hub](https://github.com/patyhank/linzone-hub) | Linux | GUI and CLI, plus a DKMS module that publishes INZONE battery through `power_supply` so UPower and the desktop shell can see it. |
| [inzone-linux](https://github.com/smartinio/inzone-linux) | Linux | Reads INZONE Buds battery, with an optional tray icon. |

On Windows the field is battery indicators —
[takamachi66](https://github.com/takamachi66/inzone-buds-battery-tray) and
[kinako19](https://github.com/kinako19/inzone-buds-battery-tray) both put INZONE Buds charge in the
notification area, and [InzoneBudsBattery](https://github.com/zxe-ll/InzoneBudsBattery) puts it
inside Final Fantasy XIV. They read; none of them write. Changing a setting from a script or a key,
on Windows, is the part this project adds.

If you are on Linux, zoneout is the better starting point. It covers more of the device, and it is
where this project would send a feature request for anything beyond volume, balance and mute.

### The protocol was found twice

zoneout's `SPECS.md` documents the same wire format, arrived at independently and from the other
direction — from captures rather than from the vendor application. The two agree: the same key id
`96 C3`, the same event ids `0x21` volume, `0x22` balance, `0x23` sidetone, `0x24` microphone and
`0x04` battery, the same `0x14` in the address byte of an event, the same `0xA0`. HeadsetControl's
INZONE H5 driver builds "a Sony vendor HCI COMMAND" and waits for the matching EVENT, which is that
framing a third time.

The descriptions differ in generality rather than in substance. zoneout gives each command a value
offset, a checksum offset and a constant; `docs/PROTOCOL.md` describes the framing those offsets
fall out of, which makes each of those constants the sum over that command's fixed header bytes.
Two readings taken separately and agreeing is the strongest evidence either is right, short of
Sony saying so.

---

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
