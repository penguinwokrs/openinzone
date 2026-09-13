# `scoop update` while running Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `scoop update openinzone` succeeds while the tray and daemon run, and the tray moves itself onto the new version.

**Architecture:** Long-running processes start through a per-version junction outside `apps`, which Scoop's running-process check does not match. The tray polls `current` and restarts when it points elsewhere.

**Tech Stack:** .NET 10, xUnit, WPF, Windows junctions (`mklink /J`), Scoop manifest hooks.

Spec: `docs/superpowers/specs/2026-09-14-scoop-update-while-running-design.md`

## Global Constraints

- Tests run on Linux; anything touching the file system's links lives in `ScoopRun`, not in tested code.
- Junction root is exactly `%LOCALAPPDATA%\openinzone-scoop\<name>\<version>`.
- Poll interval is 15 s.
- Deleting a junction uses non-recursive `Directory.Delete`, never a recursive delete.
- No new UI strings.

---

### Task 1: Move `ScoopInstall` to Ipc and add the pure pieces

**Files:**
- Move: `src/OpenInzone.Control/ScoopInstall.cs` → `src/OpenInzone.Ipc/ScoopInstall.cs` (namespace `OpenInzone.Ipc`)
- Move: `tests/OpenInzone.Core.Tests/Control/ScoopInstallTests.cs` → `tests/OpenInzone.Core.Tests/Ipc/ScoopInstallTests.cs`
- Modify: `src/OpenInzone.Tray/ScoopUpdater.cs`, `src/OpenInzone.Tray/SettingsWindow.xaml.cs` (usings)

**Interfaces:**
- Produces: `string CurrentDirectory`, `string RunDirectory(string localAppData, string version)`,
  `static bool HasMovedOn(string ownVersionDirectory, string? currentTarget, Func<string, bool> fileExists)`.

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public void Run_directory_is_per_app_and_version_under_local_app_data()
{
    var install = new ScoopInstall(@"C:\Users\me\scoop", "openinzone");
    Assert.Equal(@"C:\Users\me\AppData\Local\openinzone-scoop\openinzone\1.1.4",
        install.RunDirectory(@"C:\Users\me\AppData\Local", "1.1.4"));
}

[Theory]
[InlineData(@"C:\s\apps\openinzone\1.1.3")]    // Scoop still points here
[InlineData(@"C:\S\APPS\OPENINZONE\1.1.3\")]   // same place, spelled differently
[InlineData(null)]                             // mid-update: current unlinked
[InlineData(@"C:\s\apps\openinzone\1.1.4")]    // new version, tray not there yet
public void Does_not_move_on_until_the_new_version_is_there(string? current)
{
    Assert.False(ScoopInstall.HasMovedOn(@"C:\s\apps\openinzone\1.1.3", current, _ => false));
}

[Fact]
public void Moves_on_once_current_points_at_another_version_with_a_tray()
{
    Assert.True(ScoopInstall.HasMovedOn(@"C:\s\apps\openinzone\1.1.3", @"C:\s\apps\openinzone\1.1.4\",
        p => p == @"C:\s\apps\openinzone\1.1.4\inzonetray.exe"));
}

[Fact]
public void Script_also_stops_processes_running_through_the_junctions()
{
    string script = new ScoopInstall(@"C:\Users\me\scoop", "openinzone").BuildUpdateScript(1);
    Assert.Contains("'openinzone-scoop\\openinzone'", script);
}
```

- [ ] **Step 2:** `dotnet test tests/OpenInzone.Core.Tests --filter ScoopInstallTests` → fails to build.
- [ ] **Step 3:** Implement (`HasMovedOn` compares with trailing separators trimmed, case-insensitive; the script builds `$runs = Join-Path $env:LOCALAPPDATA 'openinzone-scoop\<name>'` and stops processes whose path starts with either directory).
- [ ] **Step 4:** Tests pass; `dotnet build OpenInzone.sln -warnaserror`.
- [ ] **Step 5:** Commit "Move ScoopInstall into Ipc and teach it about run junctions".

### Task 2: `ScoopRun` and the daemon launcher

**Files:**
- Create: `src/OpenInzone.Ipc/ScoopRun.cs`
- Modify: `src/OpenInzone.Ipc/DaemonLauncher.cs` (`TryStart`)

**Interfaces:**
- Produces: `static ScoopInstall? Locate(string directory)` (follows a junction first),
  `static string? ResolveVersionDirectory(string directory)` (follows links until a real directory),
  `static string? RunPathFor(string executablePath)` (null unless the executable is under `apps`),
  `static void PruneRunDirectories(ScoopInstall install)`.

- [ ] **Step 1:** Implement with `FileSystemInfo.LinkTarget`, `cmd.exe /c mklink /J` (CreateNoWindow, wait up to 5 s), and `Directory.Delete(path)` for pruning. Every method catches and returns null / does nothing on failure.
- [ ] **Step 2:** In `TryStart`: `executable = ScoopRun.RunPathFor(executable) ?? executable;` before `DetachedProcess.Start`.
- [ ] **Step 3:** Build with `-warnaserror` (Ipc builds on Linux; guard Windows-only calls with `OperatingSystem.IsWindows()`), run all tests.
- [ ] **Step 4:** Commit "Start a Scoop install's daemon through a junction outside apps".

### Task 3: The tray

**Files:**
- Modify: `src/OpenInzone.Tray/App.xaml.cs`, `src/OpenInzone.Tray/Autostart.cs`, `src/OpenInzone.Tray/ScoopUpdater.cs`

- [ ] **Step 1:** `ScoopUpdater.Current => ScoopRun.Locate(AppContext.BaseDirectory)`; add `ScoopUpdater.TryRelaunchThroughJunction(string[] args)` (true when a junction copy was started) and `ScoopUpdater.AutostartCommand`.
- [ ] **Step 2:** `App.OnStartup`: before the mutex, `if (ScoopUpdater.TryRelaunchThroughJunction(e.Args)) { Shutdown(); return; }`. After startup, when running through a junction, prune and start a 15 s `DispatcherTimer` that calls `ScoopInstall.HasMovedOn`; on true, stop `inzoned` processes under `AppContext.BaseDirectory` and `Restart(current\inzonetray.exe)`.
- [ ] **Step 3:** Extract the body of `RestartRequested` into `private void Restart(string executable)`; the language restart calls it with `Environment.ProcessPath`.
- [ ] **Step 4:** `Autostart.Set` writes `ScoopUpdater.AutostartCommand ?? Environment.ProcessPath`.
- [ ] **Step 5:** Build `-warnaserror`, all tests; commit "Move a Scoop tray out of apps and onto a new version by itself".

### Task 4: Bucket and docs

- [ ] **Step 1:** README.md / README.ja.md Scoop paragraph: `scoop update openinzone` works with the tray running; the tray switches to the new version within a few seconds; `inzone watch` left running still blocks it.
- [ ] **Step 2:** Commit "Document scoop update with the tray running".
- [ ] **Step 3 (scoop-bucket):** `pre_uninstall` as in the spec; notes updated; `formatjson`; commit and push after the openinzone PR is merged.

### Task 5: Verify on Windows, PR, merge

- [ ] Stop the setup tray; local zip + manifest 0.1.0 with the new `pre_uninstall`; start `apps\openinzone\current\inzonetray.exe`; confirm the running tray's path is under `openinzone-scoop`.
- [ ] Manifest → 0.1.1; `scoop update openinzone` with the tray running; confirm it updates, the tray restarts within ~15 s from the 0.1.1 junction, and no daemon runs from the 0.1.0 junction.
- [ ] Manifest → 0.1.2; update button path still updates.
- [ ] Enable autostart from Settings; Run value is `…\apps\openinzone\current\inzonetray.exe`.
- [ ] `scoop uninstall openinzone`: processes stopped, `openinzone-scoop\openinzone` gone, Run value gone. Restart the setup tray.
- [ ] Push, `gh pr create --label enhancement`, CI green, squash merge; push the bucket commit.
