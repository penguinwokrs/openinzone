# `scoop update` while the tray is running

Follows `2026-09-14-scoop-aware-update-design.md`. The tray's update button works on a Scoop install;
typing `scoop update openinzone` still does not while the tray or the daemon is running.

## The problem

Scoop skips an update when any process's path is under `<root>\apps\<name>\` ("Running process
detected, skip updating"). The check runs before every manifest hook, so the bucket cannot stop the
processes first. The Stream Deck plugin restarts the daemon within seconds, so closing the tray is
not always enough either.

## What was measured

| Question | Answer |
|---|---|
| Path of a process started through a junction | The junction path, from both `Get-Process` and `Win32_Process` |
| `scoop update` with a process running through a junction outside `apps` | Passes the check and updates; the old version directory stays until `scoop cleanup` |
| When `pre_uninstall` runs | Before the running-process check in `scoop uninstall`, after it in `scoop update` |

## Decisions

| Question | Decision | Why |
|---|---|---|
| How processes get out of `apps` | They run through a junction at `%LOCALAPPDATA%\openinzone-scoop\<name>\<version>` pointing at `<root>\apps\<name>\<version>` | Scoop's check matches paths by prefix, and a junction path is what Windows reports |
| Junction to `current` instead | No | Scoop unlinks and relinks `current` mid-update. A running tray loading an assembly later would find nothing, or the next version's file |
| Who creates the junction | The tray at startup, and `DaemonLauncher` before starting a daemon found under `apps` | Those are the only two ways a long-running process starts. The Start menu shortcut and the plugin both arrive through one of them |
| How it is created | `cmd /c mklink /J` | .NET creates symbolic links, which need a privilege; junctions do not. Nothing else in .NET makes one |
| When the tray moves | Before it takes the single-instance mutex: start the junction copy, exit | The new process then never sees a mutex held by the old one |
| If the junction cannot be made | Carry on from `apps` | Everything but a manual `scoop update` still works, as it does today |
| Autostart on a Scoop install | `<root>\apps\<name>\current\inzonetray.exe` | A versioned junction path would keep starting the old version, and stop working after `scoop cleanup` |
| After a manual update | The tray checks every 15 s whether `current` points at another version with `inzonetray.exe` in it; if so it stops the daemon running from its own junction and restarts from `current` | Chosen by the user over "next time it starts" and "just notify". The daemon has to go too, or the new tray talks to the old one |
| Poll rather than FileSystemWatcher | Poll | One link read every 15 s. A watcher on the junction's parent can miss the unlink/relink pair and needs its own error handling |
| Old junctions | Deleted at tray startup once their target is gone | `scoop cleanup` removes version directories and leaves the junctions dangling |
| Update button script | Also stops processes under the junction directory | Otherwise the old daemon survives the button's update |
| `scoop uninstall` | The bucket manifest's `pre_uninstall` stops processes under the junction directory and `apps`, deletes the junctions, and removes the Run value when it points into `apps\<name>` — only when called from `scoop-uninstall.ps1` | The check no longer protects an uninstall, and `pre_uninstall` also runs during an update, where stopping the tray would leave nobody to restart it |
| `inzone watch` left running | Still blocks a manual update | The CLI runs through Scoop's shim from `apps`. Documented |

## Units

| Unit | Project | Does |
|---|---|---|
| `ScoopInstall` | OpenInzone.Ipc (moved from Control) | Paths, `TryLocate`, `RunDirectory`, update script, start info. Pure, tested |
| `ScoopInstall.HasMovedOn(ownVersionDirectory, currentTarget, fileExists)` | OpenInzone.Ipc | Whether the tray should restart. Pure, tested |
| `ScoopRun` | OpenInzone.Ipc | Windows side: resolve links, create and prune junctions, map an executable under `apps` to its junction |
| `DaemonLauncher.TryStart` | OpenInzone.Ipc | Starts the mapped path |
| `App.OnStartup` | OpenInzone.Tray | Relaunch through the junction; start the 15 s check |
| `App.Restart(executable)` | OpenInzone.Tray | The existing language-change restart, reused |
| `Autostart.Set` | OpenInzone.Tray | Writes the `current` path on a Scoop install |
| `bucket/openinzone.json` | scoop-bucket | `pre_uninstall` |

## Testing

- Unit: `RunDirectory` composition, `HasMovedOn` cases (same version, other version without
  `inzonetray.exe`, other version with it, `current` missing), the script's junction filter.
- Windows, with the setup tray stopped: install a local build as 0.1.0 through Scoop, start the tray
  from the Start menu path; its process path is under `openinzone-scoop`. Raise the manifest to 0.1.1,
  run `scoop update openinzone` with the tray running: it updates, the tray restarts itself as 0.1.1
  within 15 s, the old daemon is gone. The button path still updates. `scoop uninstall` stops
  everything and leaves no junction and no Run value behind.
