# INZONE control protocol

Notes from reverse engineering INZONE Hub 
(`C:\Program Files\Sony\INZONE Hub\INZONEHub.dll`, a .NET 8 WPF application) and confirming
each finding against an INZONE Buds dongle.

Everything below was verified on real hardware unless marked otherwise.

This document describes observed behaviour for the purpose of interoperability. It reproduces no
code, resources or assets from INZONE Hub. "Sony", "INZONE" and "INZONE Hub" are trademarks of
Sony Group Corporation or its affiliates; this project is not affiliated with or endorsed by them.

## Transport

The dongle is a USB composite device. On INZONE Buds it enumerates as `VID_054C` / `PID_0EC2`:

| Interface | What it is |
|---|---|
| `MI_00` | USB audio endpoint, "INZONE Buds - Chat" |
| `MI_03` | USB audio endpoint, "INZONE Buds - Game" |
| `MI_05` | HID, five collections |

Game and chat are separate audio endpoints, so the balance between them is mixed on the dongle
rather than in software on the PC.

The HID interface splits into five collections:

| Collection | Usage page | Usage | In | Out | Feature | Role |
|---|---|---|---|---|---|---|
| COL01 | `0xFF13` | `0x0001` | 62 | 62 | – | not used by this project |
| COL02 | `0x000C` | `0x0001` | 2 | – | – | standard consumer controls |
| **COL03** | **`0xFF04`** | **`0x0001`** | **64** | **64** | – | **control channel** |
| COL04 | `0xFF03` | `0x0020` | – | – | 35 | feature reports |
| COL05 | `0xFF01` | `0x0020` | 8 | – | – | notifications |

Open COL03 with `GENERIC_READ | GENERIC_WRITE` and `FILE_SHARE_READ | FILE_SHARE_WRITE`.
INZONE Hub shares the same way, so both can be connected at once — neither locks the other out,
and both see the same notifications.

Find the collection by capability rather than by path: vendor `0x054C`, usage page `0xFF04`,
64-byte reports. The product id differs per model — INZONE Hub's own driver `.inf` lists
`0x0DFD`, `0x0E47`, `0x0E53`, `0x0EBF`, `0x0EC2`, `0x0F80`, `0x0F81`, `0x0FA8`, `0x0FC0` and `0x0FC1`.

### Report framing

Each 64-byte report carries a two-byte header:

```
[0]      report id, always 0x02
[1]      number of payload bytes in this report
[2..]    payload
         zero padding to 64 bytes
```

So 62 payload bytes fit per report. Longer payloads are split across consecutive reports and
reassembled by the receiver. Input reports use the same shape.

## Packet format

The payload is a packet whose framing resembles Bluetooth HCI, but the contents are Sony's own.

### Command (PC to device)

```
[0]      0x01                    packet type: command
[1..2]   0x00 0xFC               opcode 0xFC00, little endian
[3]      8 + len(param)          data length
[4..5]   0x96 0xC3               key id 0xC396; packets without it are ignored
[6]      destination << 4 | source
[7]      event id
[8]      event type
[9..10]  transaction id, little endian
[11..]   param
[last]   checksum
```

### Event (device to PC)

```
[0]      0x04                    packet type: event
[1]      0xFF                    event code
[2]      9 + len(param)          data length
[3]      0x00                    reserved
[4..5]   0x96 0xC3               key id
[6]      destination << 4 | source
[7]      event id
[8]      event type
[9..10]  transaction id, little endian
[11..]   param
[last]   checksum
```

Total packet length is `12 + len(param)`.

### Checksum

Sum of every byte from the end of the HCI header up to but excluding the checksum itself,
truncated to 8 bits. The header is **4 bytes for commands** and **3 bytes for events**, so
commands sum from index 4 and events from index 3.

```python
checksum = sum(packet[header_len : -1]) & 0xFF
```

### Addresses

| Value | Endpoint |
|---|---|
| `0x1` | PC |
| `0x2` | transmitter, i.e. the dongle |
| `0x4` | receiver, i.e. the earbuds |

A command from the PC to the earbuds therefore has `0x41` in byte 6, and the reply carries `0x14`.

Most settings live on the receiver. Link state — `ConnectStatus2Ghz` (`0x01`) and `BootStatus`
(`0x09`) — lives on the transmitter, and addressing those to the receiver gets no reply at all.
That is the one thing that silently fails rather than returning an error.

### Event types

| Value | Meaning |
|---|---|
| `0x01` | GET |
| `0x02` | SET |
| `0x10` | RET, the reply to a GET |
| `0x20` | NTFY, sent on change |
| `0xA0` | NTFY_ACTIVE, a capability flag in the vendor application rather than a wire value |

A GET is answered with RET carrying the same transaction id. A SET is answered with **NTFY**,
not RET, also carrying the same transaction id. The device also sends unsolicited NTFY packets
when a setting changes some other way — the wearer using the earbud controls, or INZONE Hub
writing a value.

## Event ids

Confirmed on INZONE Buds:

| Id | Name | Param |
|---|---|---|
| `0x02` | model info | 32 bytes, see below |
| `0x04` | battery | 6 bytes on earbuds, 2 bytes on headsets |
| `0x21` | headphone volume | `[mute, value, percent]`, value 0–30 |
| `0x22` | **game/chat balance** | `[value]`, 0–100 |
| `0x23` | sidetone volume | `[value, percent]` |
| `0x24` | microphone | `[mute, value, percent]` |

Read from INZONE Hub but not exercised here: `0x01` link status, `0x03` firmware version,
`0x05` host select, `0x06`–`0x08` bulk setting reads, `0x09` boot status, `0x25` surround,
`0x42`–`0x43` noise cancelling toggle and startup mode, `0x61`–`0x63` Bluetooth,
`0x81`–`0x8F` various (auto power off, LED, voice prompts, wearing detection, assignable buttons,
mic attach state), `0xA0` firmware update.

**Spatial sound is not on this channel.** Toggling it in INZONE Hub produces nothing here at all,
while every other setting in Hub's window does - so on INZONE Buds it is either a PC-side feature
or lives on one of the collections this project does not open.

**INZONE Hub polls.** With Hub open, the balance, volume, microphone and battery are re-read about
once a minute, which shows up as unsolicited notifications to anything else listening.

### Game/chat balance, `0x22`

One byte, 0–100. **0 is all game, 100 is all chat**, 50 is centred. INZONE Hub moves in steps of
10 and displays the result as -5.0 to +5.0, i.e. `(value - 50) / 10`.

This document had the two ends the wrong way round until it was listened to: raising the value
makes chat louder, not game. Every description in the project followed the mistake, and the
hotkeys named after each side moved towards the other one.

### Sidetone, `0x23`

`[value, percent]`. Value is **0-10**, not a percentage. The percent byte reads back as `0xFF` on
INZONE Buds, exactly as the headphone volume's does; echo it back unchanged on a write.

Observed by watching INZONE Hub drive the slider from end to end: `05 FF`, `0A FF`, `00 FF`.

### Ambient sound and noise cancelling, `0x41`

Four bytes. INZONE Buds carries all of it here; `0x42` and `0x43` were never seen.

| Byte | Meaning |
|---|---|
| 0 | mode: `00` off, `01` noise cancelling, `02` ambient sound |
| 1 | ambient level, `0x01`-`0x14` (1-20) |
| 2 | `0xFF` on INZONE Buds, as elsewhere: not reported |
| 3 | voice focus: `00` off, `01` on |

Observed from INZONE Hub, each control named before it was touched and the answers read back in
that order: `01 14 FF 00` for noise cancelling, `02 14 FF 00` for ambient sound, `00 14 FF 00` for
off, `02 01 FF 00` and `02 14 FF 00` for the level at each end of its travel, and `02 14 FF 01`
for voice focus on. Voice focus is the only other control in that panel, so byte 3 is its alone.

The level is carried in every mode, including the ones that do not use it.

### Headphone volume, `0x21`

`[mute, value, percent]`. Mute is 1 or 0, value is 0–30. The percent byte reads back as `0xFF`
on INZONE Buds; echo it back unchanged on a write.

This is the headset's own volume, separate from the Windows mixer.

### Microphone, `0x24`

`[mute, value, percent]`. On INZONE Buds both value and percent read back as `0xFF`.

**Only the mute flag travels over HID.** INZONE Hub's microphone *level* slider is not this packet
at all — it drives the Windows capture endpoint. See "The microphone level is not on the wire" below.

INZONE Hub re-sends a mute change up to four times, waiting two seconds for each acknowledgement,
and rolls the value back in its UI if none arrives. Mute crosses the wireless link, where a single
write is not always enough.

### Battery, `0x04`

Earbud models report six bytes:

```
[status_left, percent_left, status_right, percent_right, status_case, percent_case]
```

Headset models report only the first pair. A percentage of `0xFF` means that part is not
reporting — an open case, for instance.

The order was confirmed on hardware on 2026-08-23. With the right earbud in the case and the left
in the ear, the right slot read `0xFF`:

```
Battery      L 76%  R --  case 34%
```

`ModelInfo` corroborated it from an unrelated byte layout in the same run — the docked earbud's
serial read all zeros:

```
Serial       L 3015430 / R 0000000 / dongle 3015430
```

HeadsetControl's `sony_inzone_buds.hpp` labels these the other way round. Its number is still
correct, since it reports `min(left, right)`, but its labels are not.

#### The status bytes

Still undeciphered, but their range is now narrow. `inzone battery --raw` and `inzone watch
battery --raw` were run across a session covering four conditions: both earbuds worn, one docked,
the case on a charger with an earbud inside, and an earbud reconnecting. **An earbud's status byte
read `0x00` in every one of them**, and the case's read `0xFF` throughout.

So the status byte is not a connection flag — a docked earbud's `percent` becomes `0xFF` while its
status stays `0x00` — and it is not a charging flag either. The case's constant `0xFF` fits a part
that has no radio and therefore no status of its own to report.

#### Charging is not reported

Plugging the case into a charger with an earbud inside changes nothing in this event. Both an
active `GET` and the unsolicited notification that followed returned bytes identical to the
reading before the charger was connected:

```
06:19:08  before the charger   00 2F 00 FF FF 22
06:20:09  charger connected    00 2F 00 FF FF 22   (active GET)
06:20:11  charger connected    00 2F 00 FF FF 22   (notification)
```

Nothing here distinguishes charging from not charging, so a charging state cannot be reported from
this event. This matches [zoneout](https://github.com/marcinjakubowski/zoneout)'s note that no
charging flag has been observed on the Buds, arrived at separately.

That is a statement about event `0x04` only. Whether a charging flag exists under some other event
id is untested; `0x06`–`0x08`, the bulk setting reads, have never been exercised here.

#### The case level is relayed, not live

The case carries no radio, so its level reaches the dongle only by way of an earbud. The reported
number is therefore a snapshot rather than a live reading.

**The transfer happens when an earbud is docked**, not when it is taken out again. Established on
2026-08-23 by leaving the case on a charger and watching what the dongle reported.

The case charged for 37 minutes while the reported level sat frozen — 15 of those minutes under a
watch that would have flagged any change, with both earbuds out of the case:

```
06:18   case put on the charger, reported level 36
        ... 37 minutes, both earbuds out, no change reported ...
06:55:20  right earbud docked
06:56:44  R --   case 42        raw 00 2D 00 FF FF 2A
```

At 06:56:44 the earbud is still `0xFF` — it has not been taken out — and the case has already
jumped from 36 to 42, catching up on everything it had charged in the meantime. So docking is what
carries the value across, and a reading is only ever as fresh as the last time an earbud went in.

An earlier attempt could not tell docking from reconnection, because the earbud was pulled straight
back out and the only packets captured were from the tail of the cycle. Leaving it docked is what
separates the two.

The same session settles one more thing: an earbud's own level is refreshed across a dock cycle.
The right earbud went into the case at 67% and came back at 71%.

#### When notifications arrive

Docking or undocking an earbud produces a notification within a second. Between those, the device
also pushes unprompted at intervals that vary widely — 17, 20, 22, 45, 46, 63, 80 and 130 seconds
were all seen in one session. Anything that wants a current value on demand should issue a `GET`
rather than wait.

### Model info, `0x02`

```
[0]      model id
[1]      destination
[2..3]   serial number, little endian
[4]      colour
[5]      status
[6..13]  dongle serial, ASCII, null padded to 8
[14..21] left serial
[22..29] right serial
[30]     left colour id
[31]     right colour id
```

Bytes 6 onwards are only present on models that report per-bud identity (model ids 4 and 5).

The left and right serial slots normally carry the same value — a connected pair reports the same
serial in both. An earbud that is not connected reads back a serial of all zeros instead.

| Model id | Product |
|---|---|
| 0 | INZONE H9 |
| 1 | INZONE H7 |
| 2 | INZONE H3 |
| 3 | INZONE H5 |
| 4 | INZONE Buds |
| 5 | INZONE H9 II |
| 6 | INZONE E9 |
| 7 | INZONE H6 Air |

## The microphone level is not on the wire

INZONE Hub splits the microphone across two subsystems, which is easy to get wrong:

| INZONE Hub control | Where it actually goes |
|---|---|
| microphone **mute** | HID `EVENT_ID 0x24` |
| microphone **level** slider | Windows capture endpoint, `IAudioEndpointVolume`, 0–100 |

`AudioControl` in INZONE Hub holds a `captureVolume` and no render volume at all, so the level
slider is `captureVolume.MasterVolumeLevelScalar` and nothing more. `ASMControl`, which wraps
`AsmSetVolumeLevel` in `AudioSuperMix.dll`, has no callers anywhere in the application.

The headphone volume is the opposite case and worth stating plainly, because the naming invites
the wrong guess: it is **not** the Windows playback volume. INZONE Hub never touches the Windows
render endpoint. Confirmed by measurement — moving `0x21` leaves the Windows volume untouched.

### Finding the headset's capture endpoint

`PKEY_Device_InstanceId` on an audio endpoint is empty on at least some systems, so it cannot be
used to tie an endpoint back to the USB device. INZONE Hub instead walks the device topology, and
so does this project:

1. `IMMDevice.Activate(IID_IDeviceTopology)`
2. `IDeviceTopology.GetConnector(0)`
3. `IConnector.GetDeviceIdConnectedTo()`

The result names the audio adapter and contains the vendor and product ids, for example
`{2}.\\?\usb#vid_054c&pid_0ec2&mi_00#...`. On INZONE Buds the microphone sits on `MI_00`,
the chat interface.

## Worked example

Reading the game/chat balance.

```
TX  01 00 FC 08 96 C3 41 22 01 01 00 BE
RX  04 FF 0A 00 96 C3 14 22 10 01 00 32 D2
                                    ^^ 0x32 = 50
```

Setting it to 30. Note the reply is NTFY (`0x20`), not RET:

```
TX  01 00 FC 09 96 C3 41 22 02 65 00 1E 41
RX  04 FF 0A 00 96 C3 14 22 20 65 00 1E 32
```

## Where this came from

The vendor application's own source, recovered with ILSpy, maps onto this document as follows:

| Concept | Type in INZONEHub.dll |
|---|---|
| transport constants | `PCWidget.Communication.UsbCommunication` |
| report framing | `PCWidget.ViewModel.HidDevice` |
| packet layout | `PCWidget.ViewModel.HciPacket`, `HciCommandPacket`, `HciEventPacket` |
| event ids | `PCWidget.ViewModel.EVENT_ID` |
| per-setting param layout | `PCWidget.ViewModel.HeadsetParam` |
| request dispatch | `PCWidget.ViewModel.PcWidgetCommunication.SendSelecttedCommand` |
| the vendor's own key bindings | `PCWidget.ViewModel.MainViewModel.VolumeDataReceived` |

To read it again after a Hub update — the assembly is plain .NET, not obfuscated:

```sh
dotnet tool install -g ilspycmd
cp "/mnt/c/Program Files/Sony/INZONE Hub/INZONEHub.dll" .   # ilspycmd dislikes spaces in paths
ilspycmd -p -o decompiled INZONEHub.dll
```

`EVENT_ID` and `HeadsetParam` are where new settings show up first.

## Independently corroborated

The same wire format has been described elsewhere, arrived at from the opposite direction — from
captures rather than from the vendor application:

- [zoneout](https://github.com/marcinjakubowski/zoneout)'s `SPECS.md`, written for INZONE H9 II
  (`0x0FA8`) and noting INZONE Buds as speaking the same protocol.
- [HeadsetControl](https://github.com/Sapd/HeadsetControl)'s INZONE H5 driver,
  `lib/devices/sony_inzone_h5.hpp`, which builds "a Sony vendor HCI COMMAND" and waits for the
  matching EVENT.

Everything the three descriptions have in common agrees: key id `96 C3`, `0x14` in the address byte
of an event, `0xA0` as the event type of a notification, and the event ids `0x04` battery, `0x21`
headphone volume, `0x22` game/chat balance, `0x23` sidetone, `0x24` microphone, `0x41` noise
cancelling.

They differ in how much is generalised, not in what goes on the wire.

### Reading zoneout's tables against this one

zoneout counts bytes from the start of the **report**, so its byte numbers are two higher than the
packet indices used here — its byte 13 is the packet's index 11, the first param byte.

Its checksums are given per command as `(sequence + value + constant) & 0xFF`, with a constant
tabulated for each. That constant is the sum of everything fixed in the packet, which is what the
general rule here produces once the param bytes are taken out. Balance, from the worked example
above:

```
TX  01 00 FC 09  96 C3 41 22 02  65 00  1E  41
    \_________/  \____________/  \___/  \/  \/
     HCI header    fixed for       txn   pa  checksum
     not summed    this command    id    ram

sum(packet[4:-1])          = 0x241 & 0xFF = 0x41    the rule in this document

0x96+0xC3+0x41+0x22+0x02   = 0x1BE
              + txn id 00  = 0x1BE & 0xFF = 0xBE    zoneout's constant for balance
0x65 + 0x1E + 0xBE         = 0x241 & 0xFF = 0x41    zoneout's rule, same answer
```

zoneout's "sequence" is the low byte of the transaction id; the high byte is almost always zero and
lands in the constant with everything else that does not move.

Headphone volume works the same way, and its constant `0xBC` absorbs the two param bytes that never
vary: `0x96+0xC3+0x41+0x21+0x02+0x00` plus the mute byte `0x00` and the percent byte `0xFF`.

So a discrepancy between the two documents is a real disagreement worth resolving, not two
conventions talking past each other. Anything here that zoneout also lists has been confirmed
twice, from separate evidence; anything only here rests on the single reading described above.
