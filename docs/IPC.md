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
| Pipe | `OpenInzone.Daemon.<user>.v<version>`, e.g. `OpenInzone.Daemon.owner.v2` |
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
{"type":"hello","version":2,"state":{ ... },"capabilities":{"features":[ ... ]}}
```

The capabilities say what the connected model has, and are sent again whenever a device connects,
because the answer belongs to the headset that is plugged in rather than to the daemon:

```json
{"type":"capabilities","version":2,"capabilities":{"features":["balance","volume","sidetone"]}}
```

After every change, from any source — a deck key, the tray's panel, the earbuds themselves:

```json
{"type":"state","version":2,"state":{ ... }}
```

When a command cannot be understood:

```json
{"type":"error","version":2,"message":"unknown command 'format-c'"}
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
| `get-settings` | — | Read the settings below and answer with a `settings` |
| `set-setting` | the setting's own value | Write the setting named in `setting` |

Anything else is answered with an `error` and not acted on.

`set-setting` carries the setting beside the value:

```json
{"command":"set-setting","setting":"ambient-level","value":14}
```

One command for every setting, where there used to be one command each. What a setting is — which
packet it lives in, which byte of it, and what range it has — is described once in the core, so
adding one no longer touches this channel at all. A value outside the setting's range is clamped
rather than refused.

`get-settings` and `set-setting` are both answered with a `settings` read back from the headset, so
a window shows what the headset now says rather than what it was asked for.

## Detail

`describe` is answered with the device's own replies, unparsed:

```json
{"type":"detail","version":2,"detail":{
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

## Settings

`get-settings`, and every write, is answered with the whole set:

```json
{"type":"settings","version":2,"settings":[
  {"id":"ambient-mode","value":2},{"id":"ambient-level","value":14},{"id":"voice-focus","value":1},
  {"id":"sidetone","value":3},{"id":"auto-power-off","value":1},
  {"id":"bluetooth-auto-switch","value":1},{"id":"voice-guidance","value":0},
  {"id":"voice-guidance-language","value":2}]}
```

Unlike a detail, this is decoded — it is what a settings window draws, not what a protocol tool
reads. A setting this model does not have is simply **not in the list**; that is not an error, and
it is a different thing from a setting that answered off. INZONE Buds has no wearing detection and
no LED, and another model may have no ambient sound at all.

It is a list rather than a record with a field per setting, and that is the same reason the
commands collapsed into one: adding a setting should not change the wire. The values are plain
integers — 0 or 1 for a toggle, and the headset's own number for anything else.

Where the answer comes from is [the headset's own capability map](PROTOCOL.md#the-headset-publishes-its-own-capability-map-0x060x08),
read once per connection. Three exchanges say what the model has and what each setting now reads,
where asking setting by setting cost one exchange each and 1.5 seconds of silence for every one the
model turns out not to have — silence that could equally have been a bad moment on the wireless
link. Settings the map does not carry are still probed for.

## Capabilities

```json
{"type":"capabilities","version":2,"capabilities":{"features":[
  "ambient-mode","ambient-level","voice-focus","sidetone","auto-power-off",
  "bluetooth-auto-switch","voice-guidance","voice-guidance-language",
  "balance","volume","mic-mute","battery","mic-level"]}}
```

A flat list of names, spanning the panel as well as the settings tab: a model with no game/chat
balance should not be given a balance slider or a balance key on a Stream Deck either. A feature
the model does not have is left out.

**A client that has not been told anything offers everything.** No capabilities message is not an
empty one — nothing is connected, or the daemon is an older build — and hiding every control on no
information would be a worse answer than showing one the model turns out not to have. This is also
how every client behaved before it could ask.

`mic-level` is the Windows capture endpoint rather than anything on the headset's wire, and is
present when Windows exposes one.

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
It went to 2 when the settings became a list and the nine named setting commands became one.
Because the version is in the pipe name, an older client then simply finds nothing to connect to.
Adding a field to the snapshot does not require a new version: clients ignore what they do not
know.
