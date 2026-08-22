# Battery: correctness, states, and machine-readable output

Everything the project reports about charge, and the machine-readable output that the same
work makes possible.

## The problem

`BatteryInfo` decodes the wire and formats the display in one type. That single conflation is
behind most of what follows.

| # | Problem |
|---|---|
| 1 | L/R byte order had never been verified, and the upstream HeadsetControl driver claims the opposite order |
| 2 | The status bytes (`param[0] [2] [4]`) are parsed, stored, and used by nobody. Their meaning is unknown |
| 3 | Charging is never reported, on a device where every competing tool reports it |
| 4 | The case percentage is structurally a snapshot, and is displayed as though it were live |
| 5 | Both buds docked raises `TimeoutException` after 1500 ms — an error for what is a normal state |
| 6 | No machine-readable output. A status bar has to parse `"L 97%  R 97%  case 34%"` |
| 7 | No way to watch battery alone; `inzone watch` emits everything |
| 8 | `BatteryInfo.Parse` has no unit tests at all |

Problems 4, 5 and 6 are the same problem seen three times: a representation that cannot say
"this value is absent", "this value is old", or "the device is fine but unreachable".

## Decisions

| Question | Decision | Why |
|---|---|---|
| Scope | All eight | — |
| Order of work | Diagnostic surface → hardware experiments → final display | Items 2 and 3 cannot be designed before the bytes are understood |
| `--json` reach | Every command; `watch` emits JSON Lines | One flag, no per-command exceptions to remember |
| JSON shape | Flat values plus a separate `detail` object | `jq -r .left` stays a one-liner; the nuance is still available |
| Unreachable earbuds | Improve the message, exit 1. No link-state probe | `ConnectStatus2Ghz` is itself unverified; not worth depending on |
| Watching one thing | `inzone watch battery` — filter arguments | Generalises to the other events for free |
| Output architecture | Commands build reports; renderers draw them | Below |

## Verified on hardware, 2026-08-23

With the **right** bud in the case and the left in the ear:

```
Device       INZONE Buds
Serial       L 3015430 / R 0000000 / dongle 3015430
Battery      L 76%  R --  case 34%
```

The right slot read `0xFF` while the right bud was the one docked, so
`[status_l, pct_l, status_r, pct_r, status_case, pct_case]` in `docs/PROTOCOL.md` is correct.

`ModelInfo` (`0x02`) corroborates it from an unrelated byte layout: the docked bud's serial read
all zeros in the right slot. Two independent structures agreeing settles the question.

**HeadsetControl's `sony_inzone_buds.hpp` has it backwards** — its `byte[14]` is the payload's
`param[1]`, the left bud, not the right. Its reported number is right by accident, since it takes
`min(l, r)`; only the labels are wrong.

Also new: **a disconnected bud's serial reads all zeros**, which `docs/PROTOCOL.md` does not
mention.

## Architecture

### Layering

The fix for problem 4/5/6 is to stop treating the wire bytes as the display. But the split is two
layers, not three: a single value's human string is wanted by both the CLI and the tray
application, so it stays in Core.

```
Core — wire and domain
  BatteryInfo.Parse(byte[])            decodes 6-byte and 2-byte payloads
    .Left .Right .Case  → BatteryPart  { int? Percent, BatteryPartState State, raw bytes }
    .ToString()                        "L 97%  R 97%  case 34%"  (unchanged, see below)

CLI — presentation (new)
  Output/Reports/   BatteryReport, StatusReport, BalanceReport, VolumeReport,
                    MicReport, DeviceListReport
  Output/TextRenderer                  the aligned columns as today
  Output/JsonRenderer                  --json, and one line per event for watch
```

`Program.cs` shrinks to: parse arguments → run the command → build a report. `Print(string)` goes
away. The choice between renderers is made once, not in six places.

**Why reports rather than serialising in each command.** `--json` on six commands is twelve code
paths if each command formats itself, with JSON key names spelled out in six files. With a report
object it is one branch and one place that knows the schema. `status --json` then nests the same
`BatteryReport` the `battery` command emits, rather than a second implementation that drifts.

### `BatteryPart`

```csharp
public enum BatteryPartState
{
    Reporting,      // 0..100
    NotReporting,   // 0xFF — in the case, not linked, or never relayed
    Absent,         // this model has no such part (headset models: right, case)
}

public readonly record struct BatteryPart(byte RawStatus, byte RawPercent, BatteryPartState State)
{
    public int? Percent => State is BatteryPartState.Reporting ? RawPercent : null;
    public override string ToString() => Percent is int p ? $"{p}%" : "--";
}
```

`NotReporting` and `Absent` are separate so that "the case is not reporting" and "this model has
no case" are distinguishable — the first is `null` in JSON, the second omits the key.

A percent above 100 that is not `0xFF` has never been observed. If one arrives it becomes
`NotReporting`, keeping the raw byte. **Nothing here throws:** the tray application folds
notifications on the HID reader thread, where an exception would take the connection down.

### `BatteryInfo` — additive only

The raw bytes stay the stored state exactly as today. `BatteryPart` is a computed view over them,
so there is no second copy of anything to keep in step.

```csharp
public readonly record struct BatteryInfo(
    byte LeftStatus, byte LeftPercent,        // existing positional members, unchanged
    byte RightStatus, byte RightPercent,
    byte CaseStatus, byte CasePercent,
    bool HasSeparateBuds)
{
    public BatteryPart Left  => ...;          // added, derived from the bytes above
    public BatteryPart Right => ...;
    public BatteryPart Case  => ...;

    /// The case has no radio. A reported value was relayed by a bud at the last docking.
    public bool CaseIsSnapshot => HasSeparateBuds;

    public static BatteryInfo Parse(byte[] param) => ...;   // signature and behaviour unchanged
}
```

## JSON

```json
{
  "left": 97,
  "right": null,
  "case": 34,
  "detail": {
    "left_state": "reporting",
    "right_state": "not_reporting",
    "case_state": "reporting",
    "case_is_snapshot": true
  }
}
```

- Headset models omit `right` and `case` entirely rather than emitting `null` — `Absent`, not
  `NotReporting`.
- `case_is_snapshot` is constant today and is emitted anyway. Someone writing a status bar should
  learn that the number is not live without reading the protocol document; that misunderstanding
  is problem 4.
- `detail.raw` (`"04 61 FF FF 00 22"`) appears only under `--raw`.

`watch --json` emits one object per line, discriminated by `event`:

```json
{"time":"01:20:23","event":"battery","left":94,"right":94,"case":34,"detail":{...}}
```

`status --json` nests the same object:

```json
{
  "device": "INZONE Buds",
  "battery": { "left": 97, "right": 97, "case": 34, "detail": { "…": "as above" } },
  "balance": { "value": 50, "notch": 0.0 },
  "volume":  { "value": 15, "max": 30, "muted": false },
  "mic":     { "muted": false, "level": 100, "level_available": true },
  "sidetone":{ "value": 0 }
}
```

## CLI surface

```
inzone <command> [args...] [--json] [--raw]
```

`--json` and `--raw` are lifted out of the argument list by exact token match before the command
runs, so `inzone volume -1` keeps working.

```
inzone watch                    every event, as today
inzone watch battery            one event
inzone watch balance volume     several
```

Filter words are `battery`, `balance`, `volume`, `mic`, `sidetone` — deliberately the same words
as the JSON `event` field, so `inzone watch battery` and `jq 'select(.event=="battery")'` share a
vocabulary. An unknown word is a usage error that lists the valid ones.

### Exit codes

| Code | Meaning | Today |
|---|---|---|
| 0 | Success | same |
| 1 | Device-side: unreachable, no dongle, no answer | same |
| 2 | Usage: unknown command, unknown filter, bad argument | **currently 1** |

A status bar polling on a timer can then tell "typed wrong" from "they are in the case" and decide
whether retrying is worth it. This changes existing behaviour; it is easily dropped if unwanted.

### Unreachable earbuds

```
$ inzone battery
The earbuds did not answer. They are in the case, out of range, or off.
$ echo $?
1
```

Under `--json` the same exit code, and still well-formed JSON so a consumer's parser does not
break on the error path:

```json
{"error": "unreachable", "message": "The earbuds did not answer..."}
```

## Testing

Everything below runs without hardware, which is the point — the current gap is that none of the
decoding is covered.

- `BatteryInfo.Parse`: 6-byte earbud payload; 2-byte headset payload; `0xFF` in each of the three
  slots; a percent above 100; a payload shorter than 2 bytes.
- `BatteryPart`: state and `Percent` for each of the three states.
- `TextRenderer` and `JsonRenderer`: one expected string per report type, including the
  `Absent`-omitting headset shape and the error path.
- Filter parsing: known words, unknown word, no words.

New files go under `tests/OpenInzone.Core.Tests/Model/` and `.../Output/`. The test project's
`.csproj` is not touched.

## Compatibility with the tray GUI branch

`worktree-tray-gui` consumes `OpenInzone.Model` and never writes to it, so the two efforts are
genuinely parallel and either can merge first. The contract this design keeps:

| Constraint | Because |
|---|---|
| `BatteryInfo` stays a `record struct` | `DeviceState.Disconnected` uses `default` |
| `Parse(byte[])` keeps its signature and behaviour | `DeviceState.Apply` calls it under a `Length >= 2` guard |
| `LeftPercent` / `RightPercent` / `CasePercent` / `HasSeparateBuds` stay | the flyout reads them and does its own `0xFF` check |
| **`BatteryInfo.ToString()` keeps its exact output** | the tray tooltip renders `$"バッテリー {state.Battery}"` |
| Additive changes only — no renames, no removals | so their branch needs no edit |

Untouched: `src/OpenInzone.Control/**`, `src/OpenInzone.Daemon/**`, `OpenInzone.sln`,
`tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj`, `tests/OpenInzone.Core.Tests/Control/**`,
and their spec and plan documents.

Removing the superseded members is deliberately deferred rather than dropped. Once the GUI has
merged and can move to `BatteryPart.State`, they can be marked `[Obsolete]` and retired
separately.

## Phases

**Phase 1 — everything that does not depend on the unknown bytes.** The renderer split,
`BatteryPart`, `--json`, `watch` filters, the unreachable message, the tests, and `--raw`.

**Phase 2 — experiments.** `--raw` from Phase 1 makes these possible.

| # | Experiment | Record | Answers |
|---|---|---|---|
| 2 | `battery --raw` under four conditions: both worn, one docked, case on a charger, after the level drops | `param[0] [2] [4]` | what the status bytes mean |
| 3 | `watch battery` running, then plug the case in with a bud inside | whether an NTFY arrives, which byte moves | whether charging is representable at all |
| 4 | `watch battery` left idle 15 minutes, both buds worn | the interval between pushes | whether polling is needed |
| 5 | note the case level, dock a bud, watch that moment | when the value changes | confirms the snapshot behaviour |

**Phase 3 — close it out.** Report charging if experiment 3 found it; put the status bytes in
`detail` if experiment 2 explained them; write both results into `docs/PROTOCOL.md`.

## Open questions

- **The 2-byte headset payload cannot be tested** — no INZONE headset model is on hand. It stays
  marked unverified in `docs/PROTOCOL.md`.
- **Are a pair's serials really identical?** The README example shows `L 3015430 / R 3015430`, and
  the run above showed `L 3015430 / R 0000000` with the right bud docked. Whether both slots
  genuinely carry the same serial, or one mirrors the other, is unresolved. Not a battery problem;
  noted because the same capture raised it.
- **Exit code 2** is a change to existing behaviour and needs a yes or no.
