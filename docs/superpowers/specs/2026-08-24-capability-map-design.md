# Ask the headset what it has

Capability, and the settings themselves, taken from the map the headset publishes, rather than
assumed from INZONE Buds and confirmed by watching requests time out.

This is the design for [#3](https://github.com/penguinwokrs/openinzone/issues/3).

## The problem

The settings window assumes INZONE Buds. "Does this model have this setting" is answered by asking
for it and seeing whether the answer arrives.

| # | Problem |
|---|---|
| 1 | A timeout is not an answer. An unsupported id is met with silence, and so is a bad moment on the wireless link |
| 2 | It costs 1.5 s per absent setting, up to 15 s over the ids known here |
| 3 | Adding a setting means editing six places: the IPC record, the JSON context, the client, the daemon, the window and its handlers |
| 4 | Only the settings tab asks at all. The panel draws a balance slider and the Stream Deck offers a balance key whatever the model reports |

## What the headset already publishes

`0x06`–`0x08` (`AllFunctionSettingsPart1`–`3`) return **every setting's own parameter bytes,
concatenated in event-id order**, with every byte `0xFF` where the model does not have one. Read
from INZONE Buds on 2026-08-24:

```
0x06   04 | 00 44 00 43 FF 61 | 00 15 FF | 32 | 00 FF
0x07   00 FF FF | 02 14 FF 00 | 01 01 01
0x08   03 | FF FF FF FF | 0F | FF | 01 | 01 | 00
```

Every slot equals what that id answers when asked for on its own, and every `FF` slot sits where
that id timed out. So the map answers capability *and* value in three exchanges where asking
setting by setting takes eight and cannot tell absence from a slow link.

### The layout, as widths rather than offsets

There is only one model to hand, so nothing here may assume where a field starts. Each slot is as
wide as that setting's parameter, and the parser walks the parts by those widths.

| Part | Slot | Width | |
|---|---|---|---|
| `0x06` | leading byte | 1 | Not accounted for. It reads `0x04` on INZONE Buds, which is also its model id — an observation, not a reading |
| | `0x04` battery | *remainder* | 6 on an earbud model, 2 on a headset model |
| | `0x21` headphone volume | 3 | |
| | `0x22` game/chat balance | 1 | |
| | `0x23` sidetone | 2 | |
| `0x07` | `0x24` microphone | 3 | |
| | `0x41` ambient sound | 4 | mode, level, a byte the earbuds do not report, voice focus |
| | `0x42` noise cancelling toggle | 3 | |
| `0x08` | `0x43` NC startup mode | 1 | |
| | `0x61`–`0x63` Bluetooth, and one more | 4 | Four slots for three known ids; the fourth is unidentified |
| | `0x81` auto power off | 1 | |
| | `0x82` LED | 1 | |
| | `0x83` voice prompt language | 1 | |
| | `0x84` voice guidance | 1 | |
| | `0x85` connection destination | 1 | |

Battery is the only slot whose width varies by model, and it is the only one that can be derived:
everything after it in part 1 is fixed, so its width is what is left over. A headset model reporting
two battery bytes gives a nine-byte part 1 and the same parse.

What is left over must be one of those two widths. Accepting whatever remains would make part 1 the
one part that can never fail to add up, and a model carrying one field this build does not know
would then answer confidently about the wrong ids rather than sending the caller to probing.

**A part whose length does not add up is not parsed at all.** It is not guessed at, and it is not
allowed to produce a capability answer; the fallback below takes over for the settings it carries.

### A slot means absent when every byte in it is `0xFF`

`FF FF FF FF` for Bluetooth and `FF` for the LED are the model saying it has neither. `00 FF FF`
for the microphone is not: the mute flag answered, and only the level and percent bytes are the
firmware's "not reported" sentinel, which is what `MicVolume.SupportsLevel` already reads.

### What the map does not carry

`0x8E`, the Bluetooth automatic connection switch, is in none of the three parts. Neither is the
microphone *level*, which is a Windows capture endpoint and not on the wire at all.

So the catalogue records, per setting, where its answer comes from: a slot in one of the parts, or
a probe. This is a fact about the protocol, not a shortcut — a design that claimed the map covered
everything would be wrong about `0x8E`.

## Decisions

| Question | Decision | Why |
|---|---|---|
| Source of capability | The map, read once per connection | Silence is not an answer; the map is one |
| Source of the values | The map too, for the settings it carries | The slots equal the individual reads, and three exchanges beat eight |
| Settings outside the map | Probed, as today | `0x8E` is not in it, and pretending otherwise would be a guess |
| A part that does not answer, or does not add up | Fall back to probing every setting | An unknown model must degrade to what works now, not to a blank tab |
| A timeout on a setting the map says is present | An error to report | This is the distinction the whole change is for |
| Setting shape | One descriptor per setting in the core | Replaces a hand-written method per setting in five files |
| Settings on the wire | A list of `{id, value}`, and one `set-setting` command | Adding a setting stops touching the wire |
| Capability on the wire | A flat list of feature ids, at hello and on every connect | A client asks one question, and a new feature does not change the shape |
| Wire version | Raised to 2 | The version is in the pipe name, so an older client finds nothing rather than misreading |
| The settings window's markup | Kept as it is, with each control naming its setting id | The layout is something a person arranged, and it still renders on its own |

## Architecture

### `OpenInzone.Core/Settings` — the catalogue

One descriptor per setting. Reading and writing are functions over the event's parameter bytes,
which is what lets the ambient packet stay a single write carrying three settings:

```csharp
public sealed record SettingDescriptor(
    string Id,                          // "ambient-level" — the id used on the wire and in markup
    EventId EventId,                    // 0x41
    SettingKind Kind,                   // Toggle | Range | Choice
    int Minimum, int Maximum,
    Func<byte[], int> Read,             // param => param[1]
    Func<byte[], int, byte[]> Write);   // (param, value) => param with byte 1 replaced
```

`SettingCatalogue.All` holds them in the order a window shows them. Reading every setting is one
GET per distinct event id, not one per setting.

### `OpenInzone.Core/Settings/CapabilityMap` — the three parts

`CapabilityMap.Read(HciSession)` asks for `0x06`–`0x08`, walks each part by the widths above and
returns, per event id, the slot's bytes — or nothing, for a part that did not answer or did not add
up. `Present(EventId)` is false when every byte of the slot is `0xFF`.

Its parse is a pure function over the three byte arrays, so it is tested without a headset.

### `DeviceCapabilities` — what a client is told

A flat list of feature ids, spanning the panel as well as the settings tab:

```
balance  volume  mic-mute  mic-level  battery
sidetone  ambient-mode  ambient-level  voice-focus
auto-power-off  voice-guidance  voice-guidance-language  bluetooth-auto-switch
```

Sent in `hello` and again as a `capabilities` message whenever a device connects, because the
answer belongs to the model that is plugged in, not to the daemon.

### The wire

```json
{"type":"capabilities","version":2,"capabilities":{"features":["balance","volume",…]}}
{"type":"settings","version":2,"settings":[{"id":"sidetone","value":3},{"id":"ambient-mode","value":2}]}
```

```json
{"command":"set-setting","setting":"ambient-level","value":14}
```

A setting the model does not have is absent from the list, which is the same distinction the
nullable fields carried and the reason they were nullable. `DeviceSettings` becomes a list with a
`TryGet`, so "not answered for" stays different from "off" without eight nullable fields to keep in
step.

The nine named `set-*` commands and the eight-field record go. `set-setting` clamps to the
descriptor's own range in the daemon, so a client cannot write a level the setting does not have.

### The settings window

The markup keeps its hand-arranged layout and names the setting each control drives:

```xml
<CheckBox x:Name="AutoPowerOffBox" ctl:Setting.Id="auto-power-off" Content="自動電源オフ" />
```

One binder walks the panel, and for each control that names a setting: hides it when the
capabilities say the model has no such setting, fills it from the settings list, and writes it back
through `set-setting`. The per-control handlers go, the `_showingSettings` re-entry guard stays —
it is what keeps filling a control from writing it straight back.

`SettingsMarkupTests` already pins that the device tab attaches no handlers in markup. It gains a
second rule: every control the binder is meant to drive names a setting id that exists in the
catalogue, checked against the markup on disk, so a renamed setting cannot silently stop binding.

## What is not in this change

- **No model table.** The capability map is what the headset itself reports. Inventing a table for
  hardware nobody here has run this on is exactly what this change removes.
- **`0x42`, `0x43`, `0x85` and the fourth Bluetooth slot are read but not shown.** The map carries
  them; what they mean to a person is not established, and a control drawn from a guess is worse
  than no control.
- **Nothing is verified against a second model.** It cannot be. The parser is written to lengths
  rather than offsets and degrades to today's probing, which is the most that can honestly be
  claimed from one device.

## How it is verified

The parse, the catalogue, the wire shapes and the markup rule are unit tests, and they are what CI
runs — the solution builds and tests on Linux. The device itself is exercised by hand on Windows
with `inzone`, the tray panel and `show-settings`, which is how every protocol change here has been
checked.

Measured against INZONE Buds on 2026-08-24: the three parts take about 700 ms, the whole settings
read about 1.0 s, and probing the same settings one by one about 1.4 s — one exchange being about
240 ms on this link. Every one of the thirteen features is reported, which is what this model
having all of them should look like. Writing the ambient mode leaves the level and voice focus as
the headset reported them, which is the one thing a packet carrying three settings can get wrong.
