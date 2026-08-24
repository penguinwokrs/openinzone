# Plan: ask the headset what it has

Implements [the design](../specs/2026-08-24-capability-map-design.md) for
[#3](https://github.com/penguinwokrs/openinzone/issues/3).

Each step leaves the solution building and every test passing. Tests come before the code they
cover; the parse, the catalogue, the wire shapes and the markup rule are all reachable without a
headset, which is what CI runs.

## 1. The catalogue

`src/OpenInzone.Core/Settings/SettingDescriptor.cs`, `SettingCatalogue.cs`.

- `SettingKind` — `Toggle`, `Range`, `Choice`.
- `SettingDescriptor(Id, EventId, Kind, Minimum, Maximum, Read, Write)`, reading and writing over
  the event's parameter bytes.
- `SettingCatalogue.All`, `ById`, `ForEvent`.
- Tests: every id is unique; the three ambient settings share `0x41` and each writes only its own
  byte; a write clamps to the descriptor's range; auto power off writes `0x0F` for on, the other
  toggles write `0x01`.

## 2. The capability map

`src/OpenInzone.Core/Settings/CapabilityMap.cs`.

- `CapabilityMap.Parse(part1, part2, part3)` walks each part by the widths in the design, with
  battery's width taken from what is left over in part 1.
- A part whose length does not add up contributes nothing, and its ids read as unknown rather than
  as absent.
- `Slot(EventId)` returns the bytes; `Present(EventId)` is false when every byte is `0xFF` and
  null when the part was not parsed.
- Tests: the three parts recorded from INZONE Buds decode to the values each id answers on its own;
  Bluetooth and the LED read absent; a two-byte battery part 1 parses with the same widths; a part
  one byte short is refused rather than misread; an empty part is refused.

## 3. Reading through the map

`InzoneDevice.ReadCapabilityMap()` asks for `0x06`–`0x08`, swallowing a timeout per part.

`IpcSnapshot.Settings` and a new `IpcSnapshot.Capabilities` take the map when it parsed and fall
back to probing per setting when it did not. `0x8E` is probed either way — it is in no part.

## 4. The wire

`src/OpenInzone.Ipc/Messages.cs`, `IpcJson.cs`, `IpcProtocol.cs`.

- `SettingValue(Id, Value)`; `DeviceSettings` becomes a list with `TryGet`.
- `DeviceCapabilities(Features)`, and `FeatureIds` for the panel features.
- `ServerMessage.CapabilitiesUpdate`; `capabilities` on `hello`.
- `ClientMessage` gains `Setting`; `IpcCommands.SetSetting` replaces the nine named writes.
- `IpcProtocol.Version` → 2.
- Tests: a settings list survives the round trip; a setting the model lacks is absent from the list
  rather than present and null; an unknown command is still rejected; `hello` carries capabilities.

## 5. The daemon and the controller

- `DeviceController.SetSetting(id, value, deliver)` — one method for every setting, clamping
  through the descriptor, reading the event's current bytes so a packet carrying three settings
  keeps the two it was not asked about.
- `DeviceController.Capabilities` published on connect, alongside the state.
- `IpcHost` routes `set-setting` and publishes `capabilities`.
- The nine `Set*` methods and their command constants go.

## 6. The client and the window

- `IpcClient` raises `CapabilitiesReceived`; `IpcDeviceSurface` re-raises it and gains
  `SetSetting(id, value)`. The nine named methods go.
- `Setting.Id` attached property in the tray; each device-tab control names its setting.
- One binder fills, shows and writes every named control. The per-control handlers go.
- `SettingsMarkupTests` gains: every control naming a setting names one the catalogue has.

## 7. The panel and the deck

The flyout hides the balance slider, and the Stream Deck plugin refuses a balance key, when
capabilities do not list `balance`. This is the part of the issue that reaches beyond the settings
tab.

## 8. The documents

`docs/PROTOCOL.md` — the widths, and that `0x8E` is not in the map.
`docs/IPC.md` — the list-shaped settings, `set-setting`, `capabilities`, version 2.
`README.md` and `README.ja.md` — the device tab shows what the model reports.
