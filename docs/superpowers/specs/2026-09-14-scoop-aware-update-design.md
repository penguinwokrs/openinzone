# Updating a Scoop install from the tray

OpenInzone is now in a Scoop bucket (`penguinwokrs/scoop-bucket`). The tray's update button does
not know that: it always downloads and runs `setup.exe`. This makes the button and `scoop update`
both work on a Scoop install, each updating the copy that is actually running.

## The problem

| # | Problem |
|---|---|
| 1 | The button on a Scoop install runs setup, which installs a second copy under `%LOCALAPPDATA%\Programs\OpenInzone`, kills the Scoop copy's processes by image name, starts its own tray, and takes over autostart and the Start menu. `scoop update` still succeeds afterwards, but it updates a copy nobody runs any more |
| 2 | `scoop update openinzone` refuses while any process runs from `<scoop>\apps\openinzone\` ("Running process detected, skip updating"). The Stream Deck plugin restarts the daemon within seconds of it being stopped, so closing the tray is not always enough |
| 3 | A release reaches the bucket only when Excavator next runs, up to about four hours later. In that window the tray announces an update that `scoop update` says does not exist |

## Decisions

| Question | Decision | Why |
|---|---|---|
| How the tray knows it is a Scoop install | Its own directory is `<root>\apps\<name>\<version>\` and holds `install.json` | Scoop writes `install.json` into every version directory it installs. The path shape alone would also match someone's hand-made folder called `apps` |
| Where the app name comes from | The path segment, not a constant | Someone can install it from a local manifest under another name |
| What the button does on a Scoop install | Runs `scoop update` then `scoop update <name>` in a visible PowerShell window, then exits | Scoop already downloads, checks the manifest's hash and switches `current`. Doing that again in C# would be a second, weaker copy of it |
| Why a visible window | Scoop's output is the only progress and the only error message there is | Running it hidden would turn every failure into a tray that silently did not come back |
| Why `scoop update` first | `scoop update <name>` syncs buckets only when the last sync is three hours old | Without it, a release published an hour after the last sync is invisible |
| Which PowerShell | `powershell.exe` (Windows PowerShell 5.1) | Always present; Scoop supports it. pwsh 7 is not guaranteed |
| How the scoop script is found | `<root>\apps\scoop\current\bin\scoop.ps1` | Derived from the same path, so a custom `SCOOP` root works and nothing depends on PATH as it was at login |
| Stopping the daemon coming back | The script holds the `OpenInzone.Setup` mutex for the whole update | `DaemonLauncher` already refuses to start a daemon while that mutex exists. Setup uses it for the same reason |
| Which processes it stops | Only `inzonetray`, `inzoned` and `inzone` whose path is under `<root>\apps\<name>\` | Setup kills by image name, which would also stop a separate setup install. Scoop's own check is by path, so matching it by path is exactly enough |
| Digest check in the tray | Skipped on this path | Nothing is downloaded by the tray. Scoop verifies the manifest hash |
| New UI strings | None | The status text already has `Settings_UpdateFailed` for the one failure the tray can see — the script not starting |
| Bucket lag | `release.yml` starts the bucket's Excavator workflow after creating the release | Brings the lag down to minutes. Needs a fine-grained PAT (`scoop-bucket` only, Actions: read and write) in the secret `SCOOP_BUCKET_TOKEN` |
| When the secret is missing | The step is skipped, the release still succeeds | Excavator's schedule still catches up; a release must not fail over a convenience |
| Manual `scoop update` while running | Documented: quit the tray first, or use the button | Scoop's running-process check happens before any manifest hook runs, so the bucket cannot get around it |
| People who already have both copies | Nothing | The bucket was published on 2026-09-14 with the latest release; nobody can have updated through the button yet |

## The script

Built by the tray with every path quoted as a PowerShell single-quoted literal (`'` doubled), and
passed with `-EncodedCommand` so no quoting survives to the command line.

1. Create and take the `OpenInzone.Setup` mutex.
2. Wait for the tray's process id to exit (30 s at most).
3. Stop `inzonetray`, `inzoned`, `inzone` processes whose path is under `<root>\apps\<name>\`, then wait 700 ms for handles to close, as setup does.
4. `& '<root>\apps\scoop\current\bin\scoop.ps1' update`, then `... update <name>`.
5. Release the mutex.
6. Start `<root>\apps\<name>\current\inzonetray.exe`.
7. If step 4 failed, leave the window open (`Read-Host`) so the error can be read; otherwise close.

## Units

| Unit | Project | Does |
|---|---|---|
| `ScoopInstall.TryLocate(baseDirectory, fileExists)` | OpenInzone.Control | Returns root and app name, or null. Pure, tested |
| `ScoopInstall.BuildUpdateScript(install, trayProcessId)` | OpenInzone.Control | Returns the script text. Pure, tested for quoting |
| `ScoopUpdater.Run(install)` | OpenInzone.Tray | Encodes and starts `powershell.exe` |
| `SettingsWindow.InstallUpdateAsync` | OpenInzone.Tray | On a Scoop install calls `ScoopUpdater.Run` and shuts down instead of downloading |

## Testing

- Unit tests: `TryLocate` for a Scoop path, a non-Scoop path, a Scoop-shaped path without
  `install.json`, a custom root; `BuildUpdateScript` with a path containing `'`.
- On Windows: install a locally built zip through a local manifest, publish a higher version in
  that manifest, press the button. `current` points at the new version, the tray comes back, no
  `%LOCALAPPDATA%\Programs\OpenInzone` appears, and a Stream Deck plugin running throughout does
  not make the update fail.
