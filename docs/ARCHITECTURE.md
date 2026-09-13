# Design principles

How OpenInzone is meant to be built, and why. The wire formats have their own documents —
[PROTOCOL.md](PROTOCOL.md) for the headset, [IPC.md](IPC.md) for the channel between the daemon and
everything else. This one is about the decisions that sit above both, so that a change can be
checked against them before it is written rather than after it is reviewed.

## One owner, and clients that hold nothing

`inzoned.exe` is the only process that talks to the headset. The tray, the CLI and the Stream Deck
plugin are clients of it, and [IPC.md](IPC.md#why-one-owner) explains why there has to be exactly
one.

What follows from that is the rule most changes touch: **a client does not keep the headset's state
of its own.** It draws what the daemon sends it, and it forwards what the user asked for.

- **A client caches only what it was sent.** The plugin keeps the last snapshot and capabilities so
  that it can draw a key, and replaces them wholesale when the next ones arrive. It
  never edits them, and never works out a value from them.
- **The daemon does the arithmetic.** A key that moves something sends how far — `adjust-volume`,
  `toggle-mic-mute` — rather than the value it expects to end up with. The daemon applies it to what
  the headset last said. Two presses in quick succession then both count, where a client that
  computed the next value itself would send the same value twice.
- **A client does not track where a conversation is.** No flags for "waiting for hello", no memory
  of which key was pressed last. When an answer has to reach a control, it reaches every control
  it applies to.

## A change on the headset reaches every client

The wearer can change something on the headset itself, and INZONE Hub can change it too. Neither
goes through OpenInzone, and a client must not go on showing what was true before.

The headset reports those changes as notifications. The daemon applies each one to what it holds
and pushes the result to every client straight away, the same way it pushes the result of a change
a client asked for.

There are two kinds of value, and both are meant to work that way:

| | State | Settings |
|---|---|---|
| What | Balance, volume, microphone, battery | Everything in `SettingCatalogue`: ambient sound, sidetone, auto power off and the rest |
| Held in | `DeviceState` | A list of `SettingValue` |
| Sent as | `state` | `settings` |
| Written by | A command each (`adjust-volume`, `set-balance`) | `set-setting`, for every setting |

The state already follows notifications. The settings are, for now, only read when a client asks,
so a settings window that is open while the wearer changes the ambient mode on the headset keeps
showing the old one until it is opened again. Closing that gap is in the spirit of this section,
and should be done the same way the state does it.

A notification can land while a full read is under way, and the read can then put an older value
back. The state path accepts that rather than guarding against it: the next notification or the
next read corrects it. The settings path should make the same trade rather than a stricter one.

## Nothing shaped like one feature

A mechanism is added for a kind of thing, not for one instance of it.

- **A new setting is a line in `SettingCatalogue`.** Which packet it lives in, which byte, what
  range it has — described once in the core. Reading it, writing it, offering it and hiding it on
  a model without it then come from the channel as it is. A change that adds a command, a message
  or a special case for one setting is usually a sign the catalogue needs a new kind of entry
  instead.
- **Actions behave alike.** Every Stream Deck action can sit on a key and on a Stream Deck + dial.
  A readout key shows its value all the time; a directed key is a picture that shows a reading for
  a moment after it is pressed. A new action picks one of those rather than inventing a third.
- **One thing has one name.** The tray, the README and the deck use the same words for the same
  setting. The tray calls the ambient modes off, noise cancelling and ambient sound, so a key that
  needs a short form shortens those words rather than introducing others.
- **All three languages.** Text a user reads goes through the resources in English, Japanese and
  Simplified Chinese, and `ResourceCompletenessTests` fails when one is missing.

## The parts are updated apart

The installer puts the tray, the daemon and the CLI in place together, and the tray updates them
together. The Stream Deck plugin is installed separately, by hand. So an older plugin talking to a
newer daemon, and a newer plugin talking to an older one, are both ordinary.

Changes to the channel are made with that in mind:

- **Adding does not break.** A new command, message type or field does not raise the protocol
  version. An older daemon answers an unknown command with an `error`, and the client that sent it
  says on its controls that the app needs updating. The rule is in
  [IPC.md](IPC.md#versioning).
- **Changing or removing does.** Raising the version means every client of the old version stops
  connecting until it is updated too. That is sometimes necessary, and never a way to avoid
  handling an older peer.

## Models differ; the headset and INZONE Hub say how

OpenInzone does not assume a model has something. It asks the headset for its capability map and
offers what the map says ([PROTOCOL.md](PROTOCOL.md#the-headset-publishes-its-own-capability-map-0x060x08)).

Where the map is not enough — the headset carries a setting but does not accept it from the
computer — INZONE Hub is the reference for what to offer, because it is what Sony built against
each model. It offers a microphone mute button on INZONE Buds only. On the headsets it only shows
the mute state the headset reports, and an H9 II left a mute written from the computer unanswered
([#19](https://github.com/penguinwokrs/openinzone/issues/19)). When INZONE Hub's source is read for
this, what was found goes into PROTOCOL.md with where it came from.

A model the maintainer does not own cannot be debugged here. Work that needs one is labelled
`needs hardware`, and a pull request says which models it was tried on.
