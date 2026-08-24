# Voice Focus Follows Ambient Sound — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Grey out the voice focus checkbox unless ambient sound is the active mode, the way the ambient level row already is.

**Architecture:** The ambient mode's byte gets a name in `SettingCatalogue`, a test holds that name against the `Tag` the markup gives the ambient radio, and `SettingsWindow.ShowSettings` disables both controls from one boolean instead of the level alone from a bare `2`.

**Tech Stack:** .NET 8, WPF, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-25-voice-focus-needs-ambient-design.md`

## Global Constraints

- Build and test with `export PATH="$HOME/.dotnet:$PATH"` — the SDK is not on the default path.
- Baseline: 455 tests passing, 0 warnings, `dotnet build src/OpenInzone.Tray -c Release` clean.
- **`src/OpenInzone.Tray` is not in the test project's dependency graph.** `dotnet test` cannot catch a compile break there; build it explicitly.
- `tests/OpenInzone.Core.Tests` targets `net8.0` and must never reference `OpenInzone.Tray`. It has to keep running on WSL.
- **No `x:Name` in `SettingsWindow.xaml` may be renamed**, and no `Tag` value changed. `assets/make-settings-screenshot.ps1` and `tools/ShowSettings` locate elements by name, and the tags are bytes the headset receives.
- **The markup is not edited at all** by this change.
- `grep` here is **ugrep**: `-c` with `-o` counts matches, not lines.
- Commit messages must not carry a `Co-Authored-By` line. Style: a sentence saying what the change does for a reader, imperative mood, no `feat:`/`fix:` prefixes.

---

### Task 1: Name the ambient mode and let voice focus follow it

**Files:**
- Modify: `src/OpenInzone.Core/Settings/SettingCatalogue.cs`
- Modify: `src/OpenInzone.Tray/SettingsWindow.xaml.cs` (the last line of `ShowSettings`)
- Test: `tests/OpenInzone.Core.Tests/Control/SettingsMarkupTests.cs`

**Interfaces:**
- Consumes: `SettingCatalogue.AmbientMode` (`"ambient-mode"`), which already exists.
- Produces: `SettingCatalogue.AmbientSoundMode`, a `public const int` equal to `2`.

**Why the test goes in `SettingsMarkupTests`:** it already walks up to `OpenInzone.sln`, loads `SettingsWindow.xaml`, and cross-checks the markup against `SettingCatalogue` in two other facts. It has the `DevicePanel()`, `Presentation` and `Xaml` helpers this needs, and the `using OpenInzone.Settings;` import. Putting it anywhere else would mean a fourth copy of the directory walk.

- [ ] **Step 1: Write the failing test**

Append to the `SettingsMarkupTests` class in `tests/OpenInzone.Core.Tests/Control/SettingsMarkupTests.cs`:

```csharp
    /// <summary>
    /// The ambient level and voice focus are only worth offering while ambient sound is the chosen
    /// mode, and the window decides that by comparing the reported mode against
    /// <see cref="SettingCatalogue.AmbientSoundMode"/>. The number it compares against has to be
    /// the one this radio writes when someone picks it - the markup's Tag - or the window greys out
    /// the wrong things while every test still passes.
    /// </summary>
    [Fact]
    public void The_ambient_radio_writes_the_mode_the_catalogue_names()
    {
        var radio = DevicePanel().Descendants(Presentation + "RadioButton")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "AmbientButton");

        Assert.Equal(SettingCatalogue.AmbientSoundMode, int.Parse((string)radio.Attribute("Tag")!));
    }
```

- [ ] **Step 2: Run it and watch it fail**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test --filter SettingsMarkupTests
```

Expected: a compile error, `'SettingCatalogue' does not contain a definition for 'AmbientSoundMode'`.

- [ ] **Step 3: Name the mode**

In `src/OpenInzone.Core/Settings/SettingCatalogue.cs`, directly after the `AutoPowerOffOn` constant near the top of the class and before the setting-id constants, add:

```csharp
    /// <summary>
    /// The ambient mode that the level and voice focus belong to - off is 0, noise cancelling 1.
    /// The headset keeps both of those settings in every mode but only acts on them here, so it is
    /// also the only mode in which they are worth offering.
    /// </summary>
    public const int AmbientSoundMode = 2;
```

- [ ] **Step 4: Run it and watch it pass**

```bash
dotnet test --filter SettingsMarkupTests
```

Expected: PASS. If it fails on the assertion rather than compiling, the markup's `Tag` is not `2` and the constant is wrong — stop and report, do not change the markup to suit the constant.

- [ ] **Step 5: Make voice focus follow the level**

In `src/OpenInzone.Tray/SettingsWindow.xaml.cs`, at the end of `ShowSettings`, replace:

```csharp
            // The one thing no catalogue entry can say: the level belongs to ambient sound. The
            // headset keeps it in every mode, but showing it as adjustable while it does nothing
            // would be a lie.
            AmbientLevelRow.IsEnabled = settings.Value(SettingCatalogue.AmbientMode) == 2;
```

with:

```csharp
            // The one thing no catalogue entry can say: these two belong to ambient sound. The
            // headset keeps them in every mode, but showing them as adjustable while they do
            // nothing would be a lie. Both keep the value the headset holds while they are greyed,
            // so switching back to ambient sound has no surprises in it.
            bool ambient = settings.Value(SettingCatalogue.AmbientMode) == SettingCatalogue.AmbientSoundMode;
            AmbientLevelRow.IsEnabled = ambient;
            VoiceFocusBox.IsEnabled = ambient;
```

Nothing else in the method changes. `VoiceFocusBox` already exists in the markup with that `x:Name`; you are not editing the XAML.

- [ ] **Step 6: Build the tray**

```bash
dotnet build src/OpenInzone.Tray -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. This is the only check that covers Step 5 at all — the test suite cannot reach that file.

- [ ] **Step 7: Run the whole suite**

```bash
dotnet test
```

Expected: 456 passing (455 + the new fact), 0 failures.

- [ ] **Step 8: Confirm the markup really was left alone**

```bash
git diff --stat src/OpenInzone.Tray/SettingsWindow.xaml
```

Expected: no output. A change here means an `x:Name` or a `Tag` moved, which breaks the screenshot tools silently.

- [ ] **Step 9: Commit**

```bash
git add src tests
git commit -m "Offer voice focus only while ambient sound is the mode it works in"
```

---

## Finishing

- [ ] Full suite green, tray builds clean, `SettingsWindow.xaml` untouched.
- [ ] Open the pull request. `main` is protected and rejects direct pushes.
- [ ] Label it `fix` — this corrects a control that offered something it could not deliver.
- [ ] Title it for the person reading the release notes: the title becomes that line verbatim.
- [ ] Say in the PR body that the enablement itself has no automated test and why, and that checking it on screen means `tools/ShowSettings` against a real headset, switching ambient mode.
