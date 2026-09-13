# Contributing

Thank you for wanting to help. Issues and pull requests are both welcome, and a small one is as
welcome as a large one.

## Before you start

- **Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).** It is short, and it is what a pull request
  is reviewed against: the daemon owns the headset's state and clients hold none, a change on the
  headset reaches every client, nothing is built for one feature only, and the app and the Stream
  Deck plugin are updated apart.
- **For anything larger than a fix, open an issue first.** Agreeing on the approach there is cheaper
  than reworking a finished pull request.
- **The wire formats are written down.** [docs/PROTOCOL.md](docs/PROTOCOL.md) for the headset,
  [docs/IPC.md](docs/IPC.md) for the channel between the daemon and its clients. A change to either
  updates the document in the same pull request.

## Building and testing

The [developer guide](README.md#developer-guide) in the README covers what you need, building on
Windows or from WSL, running the tests, and driving the Stream Deck plugin without a deck.

```sh
dotnet test
```

The tests need no hardware, and a pull request keeps them passing. Anything that needs the headset
— discovery, the Windows audio endpoint, the windows themselves — is checked by hand.

## Hardware

INZONE models differ, and the maintainer owns INZONE Buds only. So:

- **Say which model you tried it on** in the pull request, and what you checked.
- **Put back what you change.** If you test against a headset you are wearing, note the values
  first and restore them afterwards.
- Issues that can only be worked on with a particular model are labelled `needs hardware`. If you
  own that model, those are the ones where you can help most.

## Pull requests

- **The title is a line in the release notes.** The notes are generated from merged pull requests,
  so write the title for someone reading what changed in a release, not for the commit log.
- **Keep the user's and the developer's documentation apart.** The README has a part for people who
  install and use OpenInzone and a developer guide for people who build it. Put each change in the
  part its reader looks in, and keep `README.md` and `README.ja.md` in the same shape.
- **Text a user reads comes in three languages.** English, Japanese and Simplified Chinese, through
  the resource files; the tests fail when a translation is missing.

## Reporting a bug

Say which model and which version of OpenInzone, what you did, and what happened. For anything to do
with the headset, the output of `inzone status` helps, and `inzone watch` while you reproduce it
helps more.
