# Update check core — report

Branch: `fix-icon-format`. Scope: `src/OpenInzone.Control` only, plus the `Version` property in
`src/OpenInzone.Tray/OpenInzone.Tray.csproj` as the design explicitly calls for.

## Shape chosen

`UpdateInfo` is a `readonly record struct` (five fields: `Available`, `Version`, `DownloadUrl`,
`SizeBytes`, `Sha256`), matching `DeviceState`'s existing pattern in this project: a value swapped
wholesale rather than a class with mutable state. A static `NoUpdate` singleton and a static
`CheckRelease(string releaseJson, Version currentVersion)` factory method live on the struct
itself, mirroring `HotkeyConfig.FromJson` — this codebase already puts "parse this JSON into me"
on the type it produces rather than in a separate service class, so `UpdateInfo` follows suit
instead of introducing an `UpdateChecker` class.

`CheckRelease` parses with `JsonNode`/`JsonObject`/`JsonArray` as `HotkeyConfig` does, wraps the
whole body in one `try/catch (Exception)` and returns `NoUpdate` on any failure — malformed JSON,
a field of the wrong type, anything. This runs at startup against an unauthenticated, best-effort
GitHub endpoint; per the design, a bad response must not stop the tray from appearing, so the
catch is deliberately broad rather than narrowed to `JsonException`.

The asset name is matched against the tag's own stripped text (`versionText`), not
`Version.ToString()` — see "case not anticipated" below for why that distinction matters.

## TDD evidence

**RED** — `tests/OpenInzone.Core.Tests/Control/UpdateInfoTests.cs` and three new methods appended
to `HotkeyConfigTests.cs` were written first, referencing `UpdateInfo.CheckRelease` and
`HotkeyConfig.CheckForUpdatesAtStartup`, neither of which existed yet.

```
$ export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"; dotnet build OpenInzone.sln
...
tests/OpenInzone.Core.Tests/Control/UpdateInfoTests.cs(86,22): error CS0103: The name 'UpdateInfo' does not exist in the current context [...]
  (14 occurrences, one per call site)
tests/OpenInzone.Core.Tests/Control/HotkeyConfigTests.cs(257,29): error CS1061: 'HotkeyConfig' does not contain a definition for 'CheckForUpdatesAtStartup' [...]
  (3 occurrences)
Build FAILED.
    0 Warning(s)
    18 Error(s)
```

Failed for the expected reason: the members the tests call don't exist yet, not a typo or an
unrelated break.

**GREEN** — after adding `src/OpenInzone.Control/UpdateInfo.cs`, the `CheckForUpdatesAtStartup`
property + save/load wiring in `HotkeyConfig.cs`, and `<Version>0.1.0</Version>` in
`OpenInzone.Tray.csproj`:

```
$ export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"; dotnet build OpenInzone.sln
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ dotnet test OpenInzone.sln
Passed!  - Failed:     0, Passed:   182, Skipped:     0, Total:   182, Duration: 38 ms - OpenInzone.Core.Tests.dll (net8.0)
```

## Test count

165 → 182 (+17):

- 14 in `UpdateInfoTests.cs`: same version, newer patch, newer minor, newer major, older release,
  unparseable tag, missing `tag_name`, draft, prerelease, newer release without the installer
  asset, asset with no digest (still usable, `Sha256` is `null`), the full field carry
  (`DownloadUrl`/`SizeBytes`/`Sha256`) on a normal match, malformed JSON, and an empty string.
- 3 appended to `HotkeyConfigTests.cs`: `CheckForUpdatesAtStartup` defaults to `false`, round-trips
  through `Save`/`LoadOrCreate`, and defaults to `false` when read from a file written before the
  setting existed (a file with `bindings`/`autostart` only, no `checkForUpdatesAtStartup` key).

All fixtures use a realistic trimmed `releases/latest`-shaped fragment (author object, node ids,
timestamps, `tarball_url`/`zipball_url`, a full asset object with `uploader`, `content_type`,
`state`, `download_count`, etc.) rather than a two-field invention, so the parser is proven against
the noise a real response carries.

## Case the design did not anticipate

`System.Version` comparison is component-count-sensitive, not just value-sensitive: a missing
trailing component compares as `-1`, not `0`. So `new Version(1,4,0,0) > new Version(1,4,0)` is
**true**, even though both represent "1.4.0". The running version in this project comes from the
assembly's `FileVersion`/`InformationalVersion`, which is normally three components
(`major.minor.patch`), and the design's tag format is also three components, so this shouldn't
surface in practice — but a four-component tag (`v1.4.0.0`) would be judged strictly newer than an
otherwise-identical three-component running version. Not worth guarding against given the design's
fixed tag format, but worth recording since it's a real gotcha in `Version.CompareTo` that a
different tag convention would hit.

Separately (a design detail rather than a gap): the installer asset name is matched against the
tag's own stripped text, not `released.ToString()`. `System.Version.ToString()` renders however
many components were supplied — `Version.Parse("1.5")` round-trips as `"1.5"`, not `"1.5.0.0"` —
so for the documented three-component tag format the two would agree either way. Matching on the
raw tag text was chosen anyway, since it's the more direct implementation of "the release must
carry an asset whose name matches `OpenInzone-<version>-setup.exe`" — `<version>` is the tag's own
text, not a round-trip through `Version`.
