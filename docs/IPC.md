# The local channel

One process opens the headset: `inzoned.exe`. Everything else — the tray's panel, the CLI, the
Stream Deck plugin — asks it over a named pipe. The format is written down here because anything
else that wants to drive the headset should use it rather than opening the device a second time.

## Why one owner

The control interface is opened with sharing enabled, so two processes can hold it at once and
both receive every notification. That much works. What does not is the conversation on top of it:
`HciSession` matches a reply to its request on `(event id, transaction id)`, and each process
numbers its own transactions from one. Two processes talking at the same time can therefore claim
each other's answers — the same event id and the same small transaction number are likely to
collide, and each process serialises only its own requests.

Sharing the handle is not the same as sharing the conversation. So there is one owner, and it is
not the tray: a deck key should work without a window being open, and the CLI should be safe to
run while the tray is up.

## Lifetime

The daemon is started by whichever client first needs it and stops thirty seconds after the last
one disconnects. Nothing is running when nothing is being controlled, and no client has to be told
to start it.

It is started with `CREATE_BREAKAWAY_FROM_JOB`. Without that it joins its launcher's job object,
and a launcher whose job kills on close takes the daemon with it — measured with a PowerShell
pipeline and with WSL's interop, both of which did exactly that.

A job may refuse the breakaway, and some do: `Start-Process -Wait` is one, and the daemon then
stops with the client that started it. That is not fatal. Any other client notices the pipe has
gone within a couple of seconds, starts a new daemon and carries on — so the worst case is a brief
gap, not a headset nobody owns.

It holds no hotkeys. Those are registered first come, first served; a second holder is what
retired this project's earlier console daemon, and they stay with the tray.

Clients find it by widening the search: beside the caller (the tray and the CLI sit next to it),
then `InstallLocation` from setup's own uninstall key, then the path in the autostart entry, then
where a per-user install lands by default. The last hops are what let the Stream Deck plugin,
which lives inside Stream Deck's plugins folder, find it at all.

## Shape

| | |
|---|---|
| Pipe | `OpenInzone.Daemon.<user>.v<version>`, e.g. `OpenInzone.Daemon.owner.v1` |
| Framing | one JSON object per line, `\n`, UTF-8 |
| Access | `PipeOptions.CurrentUserOnly` — the same user, on the same machine |
| Line limit | 64 KiB; a longer line drops the connection |

The user is part of the pipe name because pipe names are machine-wide on Windows: without it the
first user to log in would own the name and every other session would fail to serve. The protocol
version is part of it too, so a client built against an incompatible version fails to connect
rather than misreading the traffic.

## Conversation

The daemon speaks first, and keeps speaking. Commands are not acknowledged: every change is
answered by a whole snapshot pushed to every client, so a client never correlates a reply with a
request, and one that misses a push converges on the next.

On connect:

```json
{"type":"hello","version":1,"state":{ ... }}
```

After every change, from any source — a deck key, the tray's panel, the earbuds themselves:

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

Messages keep their order in both directions: a dial turned and then pressed must not arrive the
other way round.

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
| `set-mic-muted` | 0 or 1 | Mute or unmute explicitly |
| `set-volume-muted` | 0 or 1 | Mute or unmute the headset's own volume |
| `toggle-volume-mute` | — | Toggle it |
| `describe` | — | Read the device again and answer with a `detail` |

Anything else is answered with an `error` and not acted on.

## Detail

`describe` is answered with the device's own replies, unparsed:

```json
{"type":"detail","version":1,"detail":{
  "model":"BAAiEQAA","battery":"AGEAXgA+","balance":"KA==",
  "volume":"ABA1","mic":"Af//","sidetone":"Ax4=",
  "micLevel":75}}
```

Each field is base64 of the parameter bytes the headset sent back, and `micLevel` is the Windows
capture endpoint, which is not part of the headset's protocol at all and is absent when the model
has none.

This is deliberately unlike the snapshot. The snapshot is a shape any client can read without
knowing the protocol; this is for a tool that already speaks it. It exists so that the CLI, routed
through the daemon, prints exactly what it prints on its own connection — it decodes these with the
same decoders, so there is nothing left to drift. A client that does not speak the protocol wants
the snapshot instead.

Like everything else on this channel, a detail is pushed to every client rather than returned to
the one that asked. A client with a `describe` outstanding takes the next one that arrives.

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

`micLevelAvailable` is false on models whose microphone level is not adjustable, and while nothing
is connected.

`connected` false means the daemon is running but the earbuds are not answering: in the case, out
of range, or off. Every reading alongside it is at its resting value and should be drawn as no
reading rather than as a number.

## Versioning

`IpcProtocol.Version` is raised when the wire format changes in a way an older client cannot read.
Because the version is in the pipe name, an older client then simply finds nothing to connect to.
Adding a field to the snapshot does not require a new version: clients ignore what they do not
know.
