# Update check and winget packaging — design

Date: 2026-08-23
Status: approved

## Goal

Two things the user asked for after the first release went out:

- The tray offers to update itself. Checking at startup is a setting the user can turn off, and
  when an update exists, pressing **更新** replaces the installed copy without them visiting a web
  page. GitHub Releases is the source of truth.
- The application can be installed and updated with `winget`.

## Decisions

| Question | Decision | Why |
|---|---|---|
| How the replacement happens | Download the release's `OpenInzone-<version>-setup.exe` and run it silently, then exit | A running executable cannot overwrite itself. The installer already stops a running tray and relaunches it afterwards, and it is the only path that also fixes up the Start menu entry and the autostart value. |
| Where the logic lives | Parsing and version comparison in `OpenInzone.Control`; download and launch in `OpenInzone.Tray` | The part that can be got wrong — which release is newer — needs no network and no desktop, so it can be tested. |
| Default for the startup check | Off | Reaching the network on every login is not something to switch on for someone without asking. |
| On failure | Silent | No network, a rate limit or a missing asset must not produce a warning at every login. A check the user asked for from the settings window does report what went wrong. |

## The version the application believes it is

`OpenInzone.Tray.csproj` currently sets no `Version`, so a local build reports `1.0.0.0` while a
released build carries what the workflow passed. Left alone, every development build would consider
itself newer than every release and never offer an update.

Set `<Version>` in the project to the current release series so a build with no explicit version
compares sensibly, and keep the workflow's `-p:Version` as the authority for real releases.

## Comparing versions

Release tags are `v<major>.<minor>.<patch>`. The comparison strips the leading `v`, parses with
`System.Version`, and treats anything unparseable as "no update" rather than guessing. A release
marked prerelease or draft is ignored.

This is the piece with tests: equal versions, a newer patch, a newer minor, an older release than
the one running, a malformed tag, a missing tag, a prerelease, and the case where the release
carries no matching installer asset.

## What the tray does

At startup, when the setting is on, ask
`https://api.github.com/repos/penguinwokrs/openinzone/releases/latest` for the latest release. The
call is unauthenticated — 60 requests an hour per address, which one check per login does not
approach.

If the tag is newer than the running version and the release has an asset named
`OpenInzone-<version>-setup.exe`, show a balloon saying so. The settings window gains a line
naming the available version with an **更新** button; that button is also how someone checks on
demand when the startup check is off.

**更新** downloads the installer to the user's temporary directory, verifies its SHA-256 against
the digest the API reports, and runs it with Inno Setup's `/SILENT /NOCANCEL` before exiting the
tray. Verifying the digest matters: this downloads an executable and runs it, so the file must be
the one GitHub says it is.

## winget

`winget` needs three manifest files per version — a version manifest, an installer manifest naming
the URL and its SHA-256, and a locale manifest with the description and licence. They live under
`packaging/winget/` as templates, and the release workflow fills in the version and the digest it
already computes, then attaches the result to the release.

Getting the package into the public winget repository means a pull request against
`microsoft/winget-pkgs`. That is an outward-facing action against someone else's repository and is
left for the user to make, with the generated manifests ready to submit and the command recorded in
the README. Automating the submission needs a token with rights to fork and open pull requests,
which is a decision about credentials rather than about code.

The installer already satisfies what winget requires of it: it installs per-user without elevation,
and Inno Setup's `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` gives winget the unattended install it
needs.

## What this does not protect against

An attacker who can forge the `releases/latest` response can only ever point at a genuine build of
this project - the download URL has to name `github.com/penguinwokrs/openinzone/releases/download/`,
and the digest has to match what actually got fetched from there. What that forged response can
still do is answer with an older release than the one the attacker knows is current, as long as it
is still newer than whatever the victim is running: someone on 0.1.0 told about 0.1.1 when 0.5.0 is
out gets a real, correctly-signed-by-its-own-digest 0.1.1, and every check this design makes passes.
It cannot be made to run anything this project did not itself publish, so the severity is low, but
downgrading to a real older release - and whatever that release's own vulnerabilities are - is the
honest residual of trusting GitHub's API response for "what is newest" rather than pinning against a
signed, ordered manifest.

## Out of scope

- Updating the CLI on its own. It ships in the same installer.
- Delta updates, or any update path that does not go through the installer.
- Rollback. The installer replaces in place; the previous version is a release away.
