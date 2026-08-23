# The local channel

The tray serves a small channel that other programs on the same machine can use to read the
headset's state and ask for changes. The Stream Deck plugin is the only client today; the
format is written down here because anything else that wants to drive the headset should use
it rather than opening the device a second time.

## Why a channel rather than opening the device

The control interface is opened with sharing enabled, so two processes can hold it at once and
both receive every notification. That much works. What does not is the conversation on top of
it: `HciSession` matches a reply to its request on `(event id, transaction id)`, and each
process numbers its own transactions from one. Two processes talking at the same time can
therefore claim each other's answers — the same event id and the same small transaction number
are likely to collide, and each process serialises only its own requests.

So the tray stays the only owner of the device, and everything else asks it.

## Shape

A named pipe carrying UTF-8 JSON, one object per line.

| | |
|---|---|
| Pipe | `OpenInzone.Tray.<user>.v<version>`, e.g. `OpenInzone.Tray.owner.v1` |
| Framing | one JSON object per line, `\n` |
| Access | `PipeOptions.CurrentUserOnly` — the same user, on the same machine |
| Line limit | 64 KiB; a longer line drops the connection |

The user is part of the pipe name because pipe names are machine-wide on Windows: without it
the first user to log in would own the name and every other session would fail to serve. The
protocol version is part of it too, so a client built against an incompatible version fails to
connect rather than misreading the traffic.

## Conversation

The tray speaks first, and keeps speaking. Commands are not acknowledged: every change is
answered by a whole snapshot pushed to every client, so a client never correlates a reply with a
request, and one that misses a push converges on the next.

On connect:

```json
{"type":"hello","version":1,"state":{ ... }}
```

After every change, from any source — the deck, the tray's own panel, the earbuds themselves:

```json
{"type":"state","version":1,"state":{ ... }}
```

When a command cannot be understood:

```json
{"type":"error","version":1,"message":"unknown command 'format-c'"}
```

From the client:

```json
{"command":"adjust-volume","value":-2}
```

## Commands

| Command | `value` | Effect |
|---|---|---|
| `refresh` | — | Re-read everything from the headset |
| `adjust-volume` | delta | Move the headset's own volume, 0–30 |
| `set-volume` | 0–30 | Set it |
| `adjust-balance` | delta | Move the game/chat balance, 0–100 |
| `set-balance` | 0–100 | Set it (50 is centred) |
| `toggle-mic-mute` | — | Mute or unmute the microphone |
| `adjust-mic-level` | delta | Move the recording level, 0–100 |
| `set-mic-level` | 0–100 | Set it |

Anything else is answered with an `error` and not acted on.

## The snapshot

```json
{
  "connected": true,
  "model": "INZONE Buds",
  "volume": 16,
  "volumeMax": 30,
  "volumeMuted": false,
  "balance": 40,
  "micMuted": false,
  "micLevel": 75,
  "micLevelAvailable": true,
  "battery": { "left": 97, "right": 94, "case": null, "hasSeparateBuds": true }
}
```

A battery reading is `null` when that part is not reporting — an earbud in the case, a case that
has not been docked recently, or a model that has no such part. It is never `0` for those: zero
means a flat battery. `hasSeparateBuds` says whether `right` and `case` mean anything at all;
headset models report a single level in `left`.

`micLevelAvailable` is false on models whose microphone level is not adjustable, and while
nothing is connected.

`connected` false means the tray is running but the earbuds are not answering: in the case, out
of range, or off. Every reading alongside it is at its resting value and should be drawn as no
reading rather than as a number.

## Versioning

`IpcProtocol.Version` is raised when the wire format changes in a way an older client cannot
read. Because the version is in the pipe name, an older client then simply finds nothing to
connect to. Adding a field to the snapshot does not require a new version: clients ignore what
they do not know.
