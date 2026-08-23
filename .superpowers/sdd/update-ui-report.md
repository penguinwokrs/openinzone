# Update check UI — report

Branch: `fix-icon-format`. Builds on `ee51cf4` (the pure `UpdateInfo`/`HotkeyConfig` core).

## Classes added

**`src/OpenInzone.Control/UpdateSupport.cs`** (new, small deviation from "all in Tray" — see
below) — two pure, network-free static methods:

- `ThreeComponent(Version)` reduces an assembly version to major/minor/build. Needed because
  `System.Version` equality is component-count sensitive: the raw entry-assembly version is
  four components (`0.1.0.0`), a release tag parses to three (`0.1.0`), and comparing them
  directly would make an identical release look newer than the build already running it (the
  `update-core-report.md` gotcha, now actually guarded against instead of just recorded).
- `InstallerFileName(string downloadUrl)` extracts the file name from the download URL itself
  (URL-decoded), rather than reconstructing `OpenInzone-<version>-setup.exe` by hand, so it can
  never drift from what GitHub actually served.

Placed in Control rather than Tray because `tests/OpenInzone.Core.Tests` targets plain `net8.0`
and this sandbox has no `Microsoft.WindowsDesktop.App` runtime installed (`dotnet --list-runtimes`
confirms only `Microsoft.NETCore.App`/`Microsoft.AspNetCore.App`) — a `net8.0-windows` assembly
with `UseWPF`/`UseWindowsForms` cannot be loaded by the test host here even if none of its actually
-invoked members touch WPF, because the framework requirement is assembly-wide. Control is exactly
the project this codebase already uses for "logic that must be tested and needs no window" (see
its own doc comment), and neither method duplicates anything `UpdateInfo.CheckRelease` already
decides — they're the two additional network-free decisions the spec calls out by name ("reducing
an assembly version to three components, naming the temporary file").

**`src/OpenInzone.Tray/UpdateChecker.cs`** — the network fetch. Static `HttpClient` (`Timeout =
10s`, `User-Agent: OpenInzone/<version>`, `Accept: application/vnd.github+json`) held for the
process's lifetime and reused by `UpdateInstaller`'s download too (`internal static HttpClient
Http`). `CurrentVersion` reads `Assembly.GetEntryAssembly()` once, reduced via
`UpdateSupport.ThreeComponent`. `CheckAsync` GETs `releases/latest`, hands the body to
`UpdateInfo.CheckRelease`, and **throws** on any network failure rather than swallowing it — the
two callers disagree about what to do with a failure, so neither behaviour belongs in this class.

**`src/OpenInzone.Tray/UpdateInstaller.cs`** — turns a verified `UpdateInfo` into a running
installer:
- `DownloadAsync` streams to `Path.Combine(Path.GetTempPath(), UpdateSupport.InstallerFileName(...))`
  with `HttpCompletionOption.ResponseHeadersRead`, reporting 0–100 via `IProgress<int>` (falls
  back to `UpdateInfo.SizeBytes` when the response has no `Content-Length`).
- `VerifyDigest(path, expectedSha256)` returns `Verified` / `Mismatch` / `Absent` — see below.
- `Run(path)` starts the installer with `/SILENT /NOCANCEL` via `UseShellExecute = true` and
  returns immediately; the installer stops the running tray and relaunches it itself, so the
  caller's only remaining job is to exit.

**`src/OpenInzone.Tray/SettingsWindow.xaml{,.cs}`** — added a `CheckUpdatesBox` checkbox next to
`AutostartBox`, loaded/saved exactly the same way (`IsChecked` from `config.CheckForUpdatesAtStartup`
in the constructor, written back to `_config.CheckForUpdatesAtStartup` in `OnSaveClick` before
`Save`). Added a version line (`VersionText`, from `UpdateChecker.CurrentVersion`) and an
`UpdateButton` + `UpdateStatusText` pair. `HotkeyRow` was not touched and stays public.

**`src/OpenInzone.Tray/App.xaml.cs`** — added `CheckForUpdatesAtStartupAsync`, fired with `_ =`
(fire-and-forget, never awaited by `OnStartup`) right after hotkeys are applied, only when
`_config.CheckForUpdatesAtStartup` is true.

## Failure handling on the two paths

- **Startup** (`App.CheckForUpdatesAtStartupAsync`): the entire body is one `try/catch (Exception)`
  around the network call; on any exception — no network, DNS failure, a GitHub rate limit, a
  malformed JSON body `UpdateInfo.CheckRelease` itself already reduces to `NoUpdate` — nothing is
  shown. Only `update.Available == true` reaches a balloon (`_tray.ShowBalloon`, marshalled via
  `Dispatcher.BeginInvoke`, discarded since the async method has nothing left to await it for).
- **Settings window** (`SettingsWindow.CheckForUpdateAsync`/`OnUpdateClick`): the same call is
  wrapped in its own `try/catch`, but the `catch` sets `UpdateStatusText.Text =
  $"確認に失敗しました: {ex.Message}"` instead of staying silent. Three distinguishable outcomes
  land in that text field: already up to date (`最新バージョンです。`), a version found (button
  becomes `更新`, text names the version), or the failure message. `finally` always re-enables the
  button regardless of outcome, so a failed check doesn't leave it stuck disabled.

## The no-digest case

`UpdateInstaller.VerifyDigest` returns a three-way `DigestResult` enum (`Verified` / `Mismatch` /
`Absent`) rather than a bool, precisely so "no digest" is not silently treated as "trust it" or
conflated with a real mismatch:

- `Mismatch` → the file is deleted (`TryDelete`) and the button reports
  `ダウンロードしたファイルの検証に失敗しました。実行を中止しました。` — nothing runs, no prompt,
  because a wrong digest is unambiguous.
- `Absent` → `SettingsWindow.InstallUpdateAsync` shows a `MessageBox.Show(...,
  MessageBoxButton.YesNo, MessageBoxImage.Warning)` asking whether to run it anyway despite the
  release carrying no digest to check. `No` deletes the file and reports
  `更新を中止しました。`; `Yes` proceeds to `UpdateInstaller.Run` exactly as `Verified` would. This
  is the explicit "say so and let the user decide" the design and task call for — the file is
  never run without either a matching digest or an affirmative choice.

## Test count

**182 → 187 (+5)**, all in the new `tests/OpenInzone.Core.Tests/Control/UpdateSupportTests.cs`,
written first (RED confirmed — 6 `CS0103: UpdateSupport does not exist` before adding the class,
then GREEN after):

1. `Drops_the_revision_component` — a four-component version reduces to three.
2. `A_same_version_release_no_longer_compares_as_newer_once_reduced` — regression for the exact
   `Version` component-count gotcha `update-core-report.md` recorded but didn't guard against.
3. `A_missing_build_component_is_treated_as_zero_not_negative_one` — `Version(1,0)`'s `Build == -1`
   clamps to 0 rather than throwing when passed to `new Version(major, minor, build)`.
4. `The_installer_file_name_comes_from_the_download_url` — basic extraction.
5. `The_installer_file_name_is_url_decoded` — a `%20`-style escape in the URL comes back readable.

Nothing else added (`UpdateChecker`, `UpdateInstaller`'s download/run, the settings-window wiring,
the startup balloon) is network-free or offline-decidable, so per the task's instruction none of it
has a test — mocking `HttpClient`'s transport was explicitly ruled out, and `UpdateInfo`'s own 14
tests already cover the parsing/comparison logic these classes just call into.

## Verification

```
$ export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
$ dotnet build OpenInzone.sln
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ dotnet test OpenInzone.sln
Passed!  - Failed:     0, Passed:   187, Skipped:     0, Total:   187, Duration: 49 ms - OpenInzone.Core.Tests.dll (net8.0)
```

Read-only GET against the real API to confirm the request shape (`User-Agent: OpenInzone/0.1.0`,
`Accept: application/vnd.github+json`) works exactly as `UpdateChecker` sends it:

```
$ curl -s -o /tmp/release.json -w "HTTP %{http_code}\n" \
    -H "User-Agent: OpenInzone/0.1.0" -H "Accept: application/vnd.github+json" \
    https://api.github.com/repos/penguinwokrs/openinzone/releases/latest
HTTP 200
```

Response: `tag_name: v0.1.0`, `draft: false`, `prerelease: false`, assets include
`OpenInzone-0.1.0-setup.exe` and `OpenInzone-0.1.0-win-x64.zip` — matches the shape
`UpdateInfo.CheckRelease` and `UpdateSupport` expect. The tag is `0.1.0`, equal to the tray's own
`<Version>0.1.0</Version>`, so this call reports no update (correct — nothing to install against
itself).

Publish + launch/kill smoke test:

```
$ dotnet publish src/OpenInzone.Tray/OpenInzone.Tray.csproj -c Release -r win-x64 --self-contained true
OpenInzone.Tray -> .../src/OpenInzone.Tray/bin/Release/net8.0-windows/win-x64/publish/

$ ./inzonetray.exe &   # launched via WSL interop
$ tasklist.exe | grep -i inzonetray
inzonetray.exe    46296  Console  1  57,476 K      # started and stayed up

$ taskkill.exe /IM inzonetray.exe /F
（成功メッセージ, Shift-JIS）

$ tasklist.exe | grep -i inzonetray
(no output — nothing left running)
```

No installer was downloaded or run, no keys were synthesised, no headset setting was touched, and
`%APPDATA%\openinzone\hotkeys.json` was not written to (the tray's normal startup read of it is
unrelated to any of this feature's new code paths).
