# Scoop-aware update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The tray's update button updates a Scoop install through Scoop instead of installing a second copy with setup.

**Architecture:** A pure `ScoopInstall` in OpenInzone.Control recognises a Scoop install from the tray's own directory and writes the PowerShell script that updates it. The tray starts that script and exits. `release.yml` starts the bucket's Excavator so Scoop has the release within minutes.

**Tech Stack:** .NET 10, xUnit, WPF tray, Windows PowerShell 5.1, GitHub Actions.

Spec: `docs/superpowers/specs/2026-09-14-scoop-aware-update-design.md`

## Global Constraints

- Tests run on Linux in CI (`dotnet build OpenInzone.sln -warnaserror`, `dotnet test`), so Control code must not depend on `Path` splitting Windows paths.
- No new UI strings; failures use `Strings.Settings_UpdateFailed`.
- Mutex name is exactly `OpenInzone.Setup`.
- Secret name is exactly `SCOOP_BUCKET_TOKEN`; a missing secret must not fail the release.
- Every new source file starts with the SPDX / copyright header used elsewhere.

---

### Task 1: `ScoopInstall` — recognise the install and write the script

**Files:**
- Create: `src/OpenInzone.Control/ScoopInstall.cs`
- Test: `tests/OpenInzone.Core.Tests/Control/ScoopInstallTests.cs`

**Interfaces:**
- Produces: `public sealed record ScoopInstall(string Root, string AppName)` with
  `static ScoopInstall? TryLocate(string baseDirectory, Func<string, bool> fileExists)`,
  `string AppDirectory`, `string BuildUpdateScript(int trayProcessId)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

public class ScoopInstallTests
{
    private static Func<string, bool> Only(params string[] files) =>
        path => files.Contains(path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Recognises_a_version_directory_with_install_json()
    {
        var install = ScoopInstall.TryLocate(@"C:\Users\me\scoop\apps\openinzone\1.1.2\",
            Only(@"C:\Users\me\scoop\apps\openinzone\1.1.2\install.json"));

        Assert.Equal(new ScoopInstall(@"C:\Users\me\scoop", "openinzone"), install);
        Assert.Equal(@"C:\Users\me\scoop\apps\openinzone", install!.AppDirectory);
    }

    [Fact]
    public void Recognises_the_current_junction_and_a_custom_name_and_root()
    {
        var install = ScoopInstall.TryLocate(@"D:\tools\apps\inzone-dev\current",
            Only(@"D:\tools\apps\inzone-dev\current\install.json"));

        Assert.Equal(new ScoopInstall(@"D:\tools", "inzone-dev"), install);
    }

    [Fact]
    public void A_scoop_shaped_path_without_install_json_is_not_scoop()
    {
        Assert.Null(ScoopInstall.TryLocate(@"C:\Users\me\scoop\apps\openinzone\1.1.2\", Only()));
    }

    [Fact]
    public void The_setup_install_is_not_scoop()
    {
        Assert.Null(ScoopInstall.TryLocate(@"C:\Users\me\AppData\Local\Programs\OpenInzone\",
            _ => true));
    }

    [Fact]
    public void Script_uses_the_install_paths_and_process_id()
    {
        string script = new ScoopInstall(@"C:\Users\me\scoop", "openinzone").BuildUpdateScript(4242);

        Assert.Contains("'OpenInzone.Setup'", script);
        Assert.Contains("-Id 4242", script);
        Assert.Contains(@"'C:\Users\me\scoop\apps\scoop\current\bin\scoop.ps1'", script);
        Assert.Contains(@"'C:\Users\me\scoop\apps\openinzone\current\inzonetray.exe'", script);
    }

    [Fact]
    public void Script_doubles_every_quote_powershell_treats_as_single()
    {
        // PowerShell ends a single-quoted string on U+2018-U+201B as well as on the ASCII quote.
        string script = new ScoopInstall("C:\\Users\\O'Brien\u2019s\\scoop", "openinzone")
            .BuildUpdateScript(1);

        Assert.Contains("'C:\\Users\\O''Brien\u2019\u2019s\\scoop\\apps\\scoop\\current\\bin\\scoop.ps1'", script);
        Assert.DoesNotContain("O'Brien\u2019s", script);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH"; dotnet test tests/OpenInzone.Core.Tests --filter ScoopInstallTests`
Expected: build error, `ScoopInstall` not found.

- [ ] **Step 3: Implement**

```csharp
// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Control;

/// <summary>
/// A copy of OpenInzone that Scoop installed, and the script that updates it through Scoop. Setup
/// would install a second copy beside it instead - see the scoop-aware-update design. Plain string
/// work rather than <see cref="Path"/>, so the Linux test run splits Windows paths the same way
/// Windows does.
/// </summary>
public sealed record ScoopInstall(string Root, string AppName)
{
    public string AppDirectory => $@"{Root}\apps\{AppName}";

    /// <summary>
    /// <paramref name="baseDirectory"/> is the tray's own directory: <c>&lt;root&gt;\apps\&lt;name&gt;\&lt;version or current&gt;</c>.
    /// The shape alone is not enough - <c>install.json</c>, which Scoop writes into every version it
    /// installs, is what tells it apart from a folder someone happened to call <c>apps</c>.
    /// </summary>
    public static ScoopInstall? TryLocate(string baseDirectory, Func<string, bool> fileExists)
    {
        string directory = baseDirectory.TrimEnd('\\', '/');
        string[] parts = directory.Split('\\', '/');
        if (parts.Length < 4 || !parts[^3].Equals("apps", StringComparison.OrdinalIgnoreCase)) return null;
        if (!fileExists($@"{directory}\install.json")) return null;

        return new ScoopInstall(string.Join('\\', parts[..^3]), parts[^2]);
    }

    /// <summary>
    /// Windows PowerShell 5.1, because it is always there. Holds the setup mutex so no Stream Deck
    /// plugin restarts the daemon mid-update, stops only this install's processes (Scoop refuses to
    /// update while any run from the app directory), updates, and brings the tray back. The window
    /// stays open when the version did not change, which covers both a failure and "already latest".
    /// </summary>
    // ponytail: a global install (scoop -g) needs admin and "update -g"; this reports it as not updated.
    public string BuildUpdateScript(int trayProcessId)
    {
        string app = Quote(AppDirectory);
        string scoop = Quote($@"{Root}\apps\scoop\current\bin\scoop.ps1");
        string manifest = Quote($@"{AppDirectory}\current\manifest.json");
        string tray = Quote($@"{AppDirectory}\current\inzonetray.exe");

        return $$"""
            $mutex = New-Object System.Threading.Mutex($true, 'OpenInzone.Setup')
            try {
                Wait-Process -Id {{trayProcessId}} -Timeout 30 -ErrorAction SilentlyContinue
                $before = (Get-Content {{manifest}} -Raw | ConvertFrom-Json).version
                Get-Process -Name inzonetray, inzoned, inzone -ErrorAction SilentlyContinue |
                    Where-Object { $_.Path -and $_.Path.StartsWith({{app}} + '\', [StringComparison]::OrdinalIgnoreCase) } |
                    Stop-Process -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 700
                & {{scoop}} update
                & {{scoop}} update {{Quote(AppName)}}
                $after = (Get-Content {{manifest}} -Raw | ConvertFrom-Json).version
            } finally {
                $mutex.ReleaseMutex()
                $mutex.Dispose()
            }
            Start-Process {{tray}}
            if ($before -eq $after) { Read-Host 'OpenInzone was not updated. Press Enter to close' }
            """;
    }

    // PowerShell closes a single-quoted string on any of these, and doubling is how each is escaped.
    private static string Quote(string value)
    {
        var quoted = new System.Text.StringBuilder("'");
        foreach (char c in value)
        {
            if (c is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B') quoted.Append(c);
            quoted.Append(c);
        }
        return quoted.Append('\'').ToString();
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/OpenInzone.Core.Tests --filter ScoopInstallTests`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add src/OpenInzone.Control/ScoopInstall.cs tests/OpenInzone.Core.Tests/Control/ScoopInstallTests.cs
git commit -m "Recognise a Scoop install and write the script that updates it"
```

### Task 2: The tray updates a Scoop install through Scoop

**Files:**
- Create: `src/OpenInzone.Tray/ScoopUpdater.cs`
- Modify: `src/OpenInzone.Tray/SettingsWindow.xaml.cs` (`InstallUpdateAsync`, ~line 593)

**Interfaces:**
- Consumes: `ScoopInstall.TryLocate`, `ScoopInstall.BuildUpdateScript` from Task 1.
- Produces: `public static class ScoopUpdater { static ScoopInstall? Current; static void Run(ScoopInstall install); }`

- [ ] **Step 1: Add `ScoopUpdater`**

```csharp
// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using System.IO;
using System.Text;
using OpenInzone.Control;

namespace OpenInzone.Tray;

/// <summary>
/// The update path for a copy Scoop installed: Scoop does the download, the hash check and the
/// switch of <c>current</c>, so all this does is start its script in a window the user can watch.
/// </summary>
public static class ScoopUpdater
{
    /// <summary>This process's install, or null when it did not come from Scoop.</summary>
    public static ScoopInstall? Current { get; } = ScoopInstall.TryLocate(AppContext.BaseDirectory, File.Exists);

    /// <summary>Starts the update. The caller exits straight after; the script waits for that.</summary>
    public static void Run(ScoopInstall install)
    {
        // Encoded, so no path in the script has to survive command-line quoting as well.
        string encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(install.BuildUpdateScript(Environment.ProcessId)));
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}") { UseShellExecute = true });
    }
}
```

- [ ] **Step 2: Branch in `InstallUpdateAsync`** — directly after `UpdateStatusText.Text = "";`:

```csharp
        // Setup would install a second copy beside this one and take autostart with it; Scoop
        // updates the copy that is actually running.
        if (ScoopUpdater.Current is { } scoop)
        {
            try
            {
                ScoopUpdater.Run(scoop);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = string.Format(Strings.Settings_UpdateFailed, ex.Message);
                FinishBusyWithUpdateStillAvailable();
            }
            return;
        }
```

- [ ] **Step 3: Build and test everything**

Run: `dotnet build OpenInzone.sln -warnaserror && dotnet test OpenInzone.sln --no-build`
Expected: build succeeds with 0 warnings, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/OpenInzone.Tray/ScoopUpdater.cs src/OpenInzone.Tray/SettingsWindow.xaml.cs
git commit -m "Update a Scoop install through Scoop from the settings window"
```

### Task 3: Releases reach the bucket within minutes

**Files:**
- Modify: `.github/workflows/release.yml` (append after "Create the release")

- [ ] **Step 1: Append the step**

```yaml
      # Excavator would otherwise pick the release up on its next scheduled run, up to four hours
      # away, and until then the tray offers Scoop installs an update Scoop cannot see. The token is
      # a fine-grained PAT for penguinwokrs/scoop-bucket with Actions: read and write. Without it
      # the schedule still catches up, so its absence is not a reason to fail a release.
      - name: Update the Scoop bucket
        shell: bash
        env:
          GH_TOKEN: ${{ secrets.SCOOP_BUCKET_TOKEN }}
        run: |
          if [ -z "$GH_TOKEN" ]; then
            echo "SCOOP_BUCKET_TOKEN is not set; Excavator's schedule will pick this release up."
            exit 0
          fi
          gh workflow run excavator.yml -R penguinwokrs/scoop-bucket
```

- [ ] **Step 2: Check the YAML parses**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/release.yml'))" && echo ok`
Expected: `ok`

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Start the Scoop bucket's Excavator after a release"
```

### Task 4: Documentation

**Files:**
- Modify: `README.md` ("Installing with Scoop"), `README.ja.md` ("Scoop でインストールする")

- [ ] **Step 1: Replace the paragraph under the code block in README.md**

```markdown
This installs the same programs as the zip, adds **OpenInzone** to the Start menu under
**Scoop Apps**, and puts `inzone` on PATH. The update button in 設定 updates it through Scoop, in a
PowerShell window that closes by itself when it is done. Running `scoop update openinzone` yourself
works too, but only with the tray closed: Scoop will not update an app while any of its programs
is running. The Stream Deck plugin is not included; see [Installing it](#installing-it).
```

- [ ] **Step 2: Same in README.ja.md**

```markdown
zip と同じプログラムが入り、スタートメニューの **Scoop Apps** に **OpenInzone** が追加され、
`inzone` にも PATH が通ります。**設定**の更新ボタンは Scoop を使って更新します（PowerShell の
ウィンドウが開き、終わると自動で閉じます）。`scoop update openinzone` を自分で実行しても更新
できますが、トレイを終了してから行ってください。Scoop はアプリのプログラムが動いている間は更新
しません。Stream Deck プラグインは含まれません。[インストール](#インストール-1)を参照してください。
```

- [ ] **Step 3: Commit**

```bash
git add README.md README.ja.md
git commit -m "Document updating a Scoop install"
```

### Task 5: Verify on Windows, then open the PR

- [ ] **Step 1:** Publish win-x64 self-contained tray + daemon + CLI into one folder and zip it (same layout as `release.yml`).
- [ ] **Step 2:** Write a local manifest (version `0.1.0`, `url` = the local zip, `hash` = its SHA-256) in the scratchpad and `scoop install` it.
- [ ] **Step 3:** Start `inzone.exe watch` from the Scoop directory (a process Scoop's check would refuse on), bump the manifest to `0.1.1`, run the script produced by `BuildUpdateScript` with that process's parent shell id.
- [ ] **Step 4:** Confirm `current` → `0.1.1`, the `inzone watch` process was stopped, `inzonetray.exe` started from `current`, and no `%LOCALAPPDATA%\Programs\OpenInzone` was created by this. Stop the started tray, `scoop uninstall`, and leave the machine as it was.
- [ ] **Step 5:** Push, `gh pr create --label enhancement`, watch CI.
- [ ] **Step 6:** After merge: update `notes` in `penguinwokrs/scoop-bucket` once a release carrying this is tagged; tell the user to create `SCOOP_BUCKET_TOKEN`.
