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
`0x41`–`0x43` ambient and noise cancelling, `0x61`–`0x63` Bluetooth, `0x81`–`0x8F` various
(auto power off, LED, voice prompts, wearing detection, assignable buttons, mic attach state),
`0xA0` firmware update.

### Game/chat balance, `0x22`

One byte, 0–100. 0 is all chat, 100 is all game, 50 is centred. INZONE Hub moves in steps of 10
and displays the result as -5.0 to +5.0, i.e. `(value - 50) / 10`.

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
