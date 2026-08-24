# Localisation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add English and Simplified Chinese to the tray application and its installer, picking the language once at install time and letting the user change it afterwards.

**Architecture:** Two RESX resource sets — one in `OpenInzone.Control` for text the tests reach, one in a small `OpenInzone.Resources` assembly for window text — compiled to satellite assemblies. A three-step resolver (`hotkeys.json` → installer marker file → English) decides the culture, and `App.OnStartup` applies it before the tray icon and flyout are constructed. The installer localises separately through Inno Setup's own `[Languages]` mechanism.

**Tech Stack:** .NET 8, WPF, xUnit, RESX with SDK-generated sources, Inno Setup 6.

**Spec:** `docs/superpowers/specs/2026-08-24-i18n-design.md`

## Global Constraints

- Supported language tags are exactly `en`, `ja`, `zh-Hans`. Anything else resolves to `en`.
- `HotkeyCommand.Id` values (`volume-up`, `volume-down`, `balance-game`, `balance-chat`, `balance-centre`, `mic-mute`, `mic-up`, `mic-down`) are persisted in `hotkeys.json` and referenced over IPC. **Never translate, rename or reorder them.**
- Every `x:Name` in `SettingsWindow.xaml` must keep its current value. `assets/make-settings-screenshot.ps1` and `tools/ShowSettings` locate elements by name.
- `LanguageBox`'s `ComboBoxItem` `Tag` values stay `0`, `1`, `2` bound to English, Japanese, Chinese in that order. These are bytes the headset receives.
- `tests/OpenInzone.Core.Tests` targets `net8.0` and must never reference `OpenInzone.Tray`. It has to keep running on WSL.
- **XAML cannot reference a resx-generated class in its own assembly** on SDK 8.0.424 — the temporary-assembly compile WPF triggers for local types never runs `PrepareResources`, so `Strings.g.cs` does not exist for it and the build fails with CS0234. Window text therefore lives in `OpenInzone.Resources` and is referenced across the assembly boundary: `xmlns:res="clr-namespace:OpenInzone.Resources;assembly=OpenInzone.Resources"`. Verified in both directions.
- Do **not** use `GenerateSource="true"` on an `EmbeddedResource`. That attribute does not exist in SDK 8.0.424 and generates nothing without complaining. The working form is the in-box `StronglyTypedClassName` / `StronglyTypedNamespace` / `StronglyTypedFileName` / `StronglyTypedLanguage` / `PublicClass` set — copy it from `src/OpenInzone.Control/OpenInzone.Control.csproj`.
- `TheoryData<T>` cannot be built with a collection expression on xunit 2.5.3 (CS0029). Use `new TheoryData<T>()` and `.Add(...)`.
- `grep` here is **ugrep**, not GNU grep: `-c` with `-o` counts matches, not lines. Searching for Japanese needs `\p{Han}` as well as the kana ranges — `未接続` and `音量` are kanji only.
- Build with `export PATH="$HOME/.dotnet:$PATH"` — the SDK is not on the default path.
- No test file may be deleted. Tests that pin Japanese get rewritten, not removed.
- Commit messages: no `Co-Authored-By` line.
- Baseline before starting: 361 tests passing.

---

### Task 1: Language resolution

**Files:**
- Create: `src/OpenInzone.Control/UiLanguage.cs`
- Modify: `src/OpenInzone.Control/HotkeyConfig.cs`
- Test: `tests/OpenInzone.Core.Tests/Control/UiLanguageTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `UiLanguage.Supported` (`IReadOnlyList<string>`), `UiLanguage.Normalise(string?)` → `string?`, `UiLanguage.Resolve(string? configured, string applicationDirectory)` → `string`, `UiLanguage.MarkerFileName` (`const string`), `HotkeyConfig.Language` (`string?`).

- [ ] **Step 1: Write the failing test**

Create `tests/OpenInzone.Core.Tests/Control/UiLanguageTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

/// <summary>
/// The tray never asks Windows what language it is in. It asks the configuration, then the file
/// the installer left behind, then gives up and speaks English. These pin that order, and pin
/// that nothing malformed anywhere in it can stop the tray from starting.
/// </summary>
public class UiLanguageTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "openinzone-lang-" + Guid.NewGuid().ToString("N"));

    public UiLanguageTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void WriteMarker(string content) =>
        File.WriteAllText(Path.Combine(_directory, UiLanguage.MarkerFileName), content);

    [Fact]
    public void The_configured_language_wins_over_the_installers_choice()
    {
        WriteMarker("ja");
        Assert.Equal("zh-Hans", UiLanguage.Resolve("zh-Hans", _directory));
    }

    [Fact]
    public void The_installers_choice_is_used_when_nothing_is_configured()
    {
        WriteMarker("ja");
        Assert.Equal("ja", UiLanguage.Resolve(null, _directory));
    }

    [Fact]
    public void English_is_the_answer_when_neither_says_anything()
    {
        Assert.Equal("en", UiLanguage.Resolve(null, _directory));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("zh-TW")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EN; DROP TABLE")]
    public void An_unsupported_configured_value_falls_through_rather_than_being_used(string configured)
    {
        WriteMarker("ja");
        Assert.Equal("ja", UiLanguage.Resolve(configured, _directory));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("")]
    [InlineData("\n\n")]
    public void An_unsupported_marker_falls_through_to_English(string marker)
    {
        WriteMarker(marker);
        Assert.Equal("en", UiLanguage.Resolve(null, _directory));
    }

    [Fact]
    public void A_marker_written_with_stray_whitespace_still_reads()
    {
        WriteMarker("  zh-Hans \r\n");
        Assert.Equal("zh-Hans", UiLanguage.Resolve(null, _directory));
    }

    [Fact]
    public void A_directory_that_does_not_exist_is_not_an_error()
    {
        Assert.Equal("en", UiLanguage.Resolve(null, Path.Combine(_directory, "nope")));
    }

    [Fact]
    public void Every_supported_tag_normalises_to_itself()
    {
        Assert.Equal(["en", "ja", "zh-Hans"], UiLanguage.Supported);
        Assert.All(UiLanguage.Supported, tag => Assert.Equal(tag, UiLanguage.Normalise(tag)));
    }

    [Fact]
    public void Normalising_is_case_insensitive_because_a_hand_edited_file_will_not_match_exactly()
    {
        Assert.Equal("zh-Hans", UiLanguage.Normalise("ZH-HANS"));
        Assert.Equal("en", UiLanguage.Normalise("EN"));
    }

    [Fact]
    public void Normalising_something_unsupported_gives_null_rather_than_a_guess()
    {
        Assert.Null(UiLanguage.Normalise("fr"));
        Assert.Null(UiLanguage.Normalise(null));
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test --filter UiLanguageTests
```

Expected: compile error, `The name 'UiLanguage' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

Create `src/OpenInzone.Control/UiLanguage.cs`:

```csharp
// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Control;

/// <summary>
/// Which language the tray shows itself in. Deliberately not the operating system's: the choice is
/// made once, by the installer, and then only changes when someone changes it. Reading
/// CurrentUICulture here instead would mean a machine that switches to Korean silently switches
/// the tray to English, having never been asked.
/// </summary>
public static class UiLanguage
{
    /// <summary>Written by the installer beside the executable. Absent in the zip download, which
    /// is exactly how the zip ends up in English without a special case for it.</summary>
    public const string MarkerFileName = "default-language";

    public const string Fallback = "en";

    public static IReadOnlyList<string> Supported { get; } = ["en", "ja", "zh-Hans"];

    /// <summary>
    /// The supported tag this text names, or null if it names none. Case-insensitive and
    /// whitespace-tolerant: both sources are files a person may have typed into.
    /// </summary>
    public static string? Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        return Supported.FirstOrDefault(
            tag => string.Equals(tag, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The configured choice, else what the installer detected, else English. Every failure along
    /// the way is a fall-through rather than a throw: an unreadable preference must not be the
    /// reason a tray icon never appears.
    /// </summary>
    public static string Resolve(string? configured, string applicationDirectory) =>
        Normalise(configured)
        ?? Normalise(ReadMarker(applicationDirectory))
        ?? Fallback;

    private static string? ReadMarker(string applicationDirectory)
    {
        try
        {
            string path = Path.Combine(applicationDirectory, MarkerFileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            // An unreadable marker is a marker that says nothing, not a reason to fail.
            return null;
        }
    }
}
```

- [ ] **Step 4: Add `Language` to the configuration**

In `src/OpenInzone.Control/HotkeyConfig.cs`, after the `PluginSaveFolder` property (around line 26), add:

```csharp
    /// <summary>
    /// Which language the window is shown in, or null when nobody has chosen. Null rather than
    /// "en" on purpose: it is what lets an installation fall through to the language the installer
    /// detected, and what stops a hotkeys.json written before this existed from pinning itself to
    /// English the first time it is read.
    /// </summary>
    public string? Language { get; set; }
```

In `Save`, add the key to the `JsonObject` after `pluginSaveFolder`:

```csharp
            ["language"] = Language,
```

In `FromJson`, after the `pluginSaveFolder` block, add:

```csharp
        if (root["language"] is JsonValue language && language.TryGetValue(out string? tag))
            config.Language = UiLanguage.Normalise(tag);
```

- [ ] **Step 5: Extend the test to cover the round trip**

Append to `tests/OpenInzone.Core.Tests/Control/HotkeyConfigTests.cs`:

```csharp
    [Fact]
    public void The_language_survives_a_save_and_load()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var config = HotkeyConfig.Default();
            config.Language = "zh-Hans";
            config.Save(path);

            Assert.Equal("zh-Hans", HotkeyConfig.FromJson(File.ReadAllText(path)).Language);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_file_with_no_language_reads_as_no_choice_rather_than_English()
    {
        Assert.Null(HotkeyConfig.FromJson("""{"bindings":{}}""").Language);
    }

    [Fact]
    public void A_language_the_build_does_not_have_is_dropped_rather_than_kept()
    {
        Assert.Null(HotkeyConfig.FromJson("""{"language":"fr-FR"}""").Language);
    }
```

- [ ] **Step 6: Run the tests and confirm they pass**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test --filter "UiLanguageTests|HotkeyConfigTests"
```

Expected: PASS, 0 failures.

- [ ] **Step 7: Run the whole suite**

```bash
dotnet test
```

Expected: 361 + the new tests, 0 failures.

- [ ] **Step 8: Commit**

```bash
git add src/OpenInzone.Control/UiLanguage.cs src/OpenInzone.Control/HotkeyConfig.cs tests/OpenInzone.Core.Tests/Control/UiLanguageTests.cs tests/OpenInzone.Core.Tests/Control/HotkeyConfigTests.cs
git commit -m "Decide the language from the configuration, then the installer, then English"
```

---

### Task 2: The Control resource set

**Files:**
- Create: `src/OpenInzone.Control/Resources/Strings.resx`, `Strings.ja.resx`, `Strings.zh-Hans.resx`
- Modify: `src/OpenInzone.Control/OpenInzone.Control.csproj`
- Test: `tests/OpenInzone.Core.Tests/Control/ResourceCompletenessTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `OpenInzone.Control.Resources.Strings` — a generated static class with one `public static string` property per key below.

**Why a completeness test:** a key missing from `Strings.ja.resx` does not throw. `ResourceManager` walks up to the neutral culture and quietly serves the English. A half-translated build therefore looks correct until a reader notices, which is the failure this test exists to prevent.

- [ ] **Step 1: Create the neutral (English) resource file**

Create `src/OpenInzone.Control/Resources/Strings.resx`. RESX needs its header block or MSBuild rejects the file; this is the minimum that works, and every one of the six resx files in this plan uses exactly this skeleton with only the `<data>` entries differing:

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>

  <data name="Hotkey_VolumeUp" xml:space="preserve">
    <value>Volume up</value>
  </data>
  <!-- … one <data> element per row of the table below … -->
</root>
```

`xml:space="preserve"` matters on every entry: `App_ListSeparator` is `", "` with a trailing space, and without it the space is stripped.

The `<data>` entries for this file:

| Key | Value |
|---|---|
| `Hotkey_VolumeUp` | `Volume up` |
| `Hotkey_VolumeDown` | `Volume down` |
| `Hotkey_BalanceGame` | `Balance towards game` |
| `Hotkey_BalanceChat` | `Balance towards chat` |
| `Hotkey_BalanceCentre` | `Centre the balance` |
| `Hotkey_MicMute` | `Toggle microphone mute` |
| `Hotkey_MicUp` | `Microphone level up` |
| `Hotkey_MicDown` | `Microphone level down` |
| `Status_VolumeMuted` | `{0} (muted)` |
| `Status_BalanceCentre` | `Centre` |
| `Status_BalanceGame` | `Game {0:0.0}` |
| `Status_BalanceChat` | `Chat {0:0.0}` |
| `Status_MicUnavailable` | `Unavailable` |
| `Status_BatteryCase` | `Case` |

- [ ] **Step 2: Create `Strings.ja.resx` with the same keys**

| Key | Value |
|---|---|
| `Hotkey_VolumeUp` | `音量を上げる` |
| `Hotkey_VolumeDown` | `音量を下げる` |
| `Hotkey_BalanceGame` | `バランスをゲーム寄りに` |
| `Hotkey_BalanceChat` | `バランスをチャット寄りに` |
| `Hotkey_BalanceCentre` | `バランスを中央に` |
| `Hotkey_MicMute` | `マイクミュート切り替え` |
| `Hotkey_MicUp` | `マイクレベルを上げる` |
| `Hotkey_MicDown` | `マイクレベルを下げる` |
| `Status_VolumeMuted` | `{0}（ミュート）` |
| `Status_BalanceCentre` | `中央` |
| `Status_BalanceGame` | `ゲーム寄り {0:0.0}` |
| `Status_BalanceChat` | `チャット寄り {0:0.0}` |
| `Status_MicUnavailable` | `利用不可` |
| `Status_BatteryCase` | `ケース` |

These are the strings lifted verbatim out of `SnapshotText.cs` and `HotkeyCommand.cs`. Nothing here is newly translated — if a value differs from what the source file says today, that is a mistake, not an improvement.

- [ ] **Step 3: Create `Strings.zh-Hans.resx` with the same keys**

| Key | Value |
|---|---|
| `Hotkey_VolumeUp` | `提高音量` |
| `Hotkey_VolumeDown` | `降低音量` |
| `Hotkey_BalanceGame` | `平衡偏向游戏` |
| `Hotkey_BalanceChat` | `平衡偏向语音` |
| `Hotkey_BalanceCentre` | `平衡居中` |
| `Hotkey_MicMute` | `切换麦克风静音` |
| `Hotkey_MicUp` | `提高麦克风电平` |
| `Hotkey_MicDown` | `降低麦克风电平` |
| `Status_VolumeMuted` | `{0}（已静音）` |
| `Status_BalanceCentre` | `居中` |
| `Status_BalanceGame` | `偏向游戏 {0:0.0}` |
| `Status_BalanceChat` | `偏向语音 {0:0.0}` |
| `Status_MicUnavailable` | `不可用` |
| `Status_BatteryCase` | `充电盒` |

- [ ] **Step 4: Wire the resources into the project**

In `src/OpenInzone.Control/OpenInzone.Control.csproj`, add to the existing `<PropertyGroup>`:

```xml
    <NeutralResourcesLanguage>en</NeutralResourcesLanguage>
```

and add a new `<ItemGroup>`:

```xml
  <ItemGroup>
    <!-- StronglyTyped* is MSBuild's own GenerateResource task doing the code generation, not the
         Visual Studio designer's ResXFileCodeGenerator custom tool. That matters because the
         release is published from WSL, where the designer does not exist: this way the class
         regenerates on every `dotnet build` from the command line, same as any other source. -->
    <EmbeddedResource Update="Resources\Strings.resx"
                      StronglyTypedClassName="Strings"
                      StronglyTypedNamespace="OpenInzone.Control.Resources"
                      StronglyTypedFileName="$(IntermediateOutputPath)Strings.g.cs"
                      StronglyTypedLanguage="CSharp"
                      PublicClass="true"
                      Generator="" />
  </ItemGroup>
```

**Not `GenerateSource="true"`.** An earlier draft of this plan named that attribute; it does not exist in the only SDK installed here (8.0.424) and silently generates nothing. Verified: the `StronglyTyped*` form above produces `obj/<config>/net8.0/Strings.g.cs` and publishes `ja/` and `zh-Hans/` satellite directories.

- [ ] **Step 5: Write the completeness test**

Create `tests/OpenInzone.Core.Tests/Control/ResourceCompletenessTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Xml.Linq;

namespace OpenInzone.Tests.Control;

/// <summary>
/// A key present in the English resx and missing from a translation does not throw. ResourceManager
/// falls back to the neutral culture and serves the English quietly, so a half-translated build
/// looks finished until somebody reads it. Nothing else in the build would notice, so this does.
///
/// The files are read as XML off disk rather than through the generated classes, so this can cover
/// the tray's resources too without the test project referencing a Windows-only assembly.
/// </summary>
public class ResourceCompletenessTests
{
    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenInzone.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>
    /// Every resource directory in the repository. Task 4 adds the tray's; until then this names
    /// only the one that exists, so each task finishes with the suite green.
    /// </summary>
    public static TheoryData<string> ResourceDirectories()
    {
        // Not a collection expression: xunit 2.5.3's TheoryData does not support one (CS0029).
        var data = new TheoryData<string>();
        data.Add(Path.Combine("src", "OpenInzone.Control", "Resources"));
        return data;
    }

    private static string[] KeysIn(string path) =>
        [.. XDocument.Load(path).Root!.Elements("data")
            .Select(d => (string)d.Attribute("name")!)
            .Order()];

    [Theory]
    [MemberData(nameof(ResourceDirectories))]
    public void Every_translation_carries_every_key_the_English_file_has(string relativeDirectory)
    {
        string directory = Path.Combine(Repository(), relativeDirectory);
        string[] english = KeysIn(Path.Combine(directory, "Strings.resx"));

        Assert.NotEmpty(english);

        foreach (string culture in (string[])["ja", "zh-Hans"])
        {
            string[] translated = KeysIn(Path.Combine(directory, $"Strings.{culture}.resx"));

            Assert.Equal(english, translated);
        }
    }

    [Theory]
    [MemberData(nameof(ResourceDirectories))]
    public void No_translated_value_is_left_empty(string relativeDirectory)
    {
        string directory = Path.Combine(Repository(), relativeDirectory);

        foreach (string culture in (string[])["ja", "zh-Hans"])
        {
            var document = XDocument.Load(Path.Combine(directory, $"Strings.{culture}.resx"));

            Assert.All(document.Root!.Elements("data"), entry =>
                Assert.False(string.IsNullOrWhiteSpace((string?)entry.Element("value")),
                    $"{culture}: {(string?)entry.Attribute("name")} has no value"));
        }
    }
}
```

Run it:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test --filter ResourceCompletenessTests
```

Expected: PASS. `ResourceDirectories` names only the Control set for now — Task 4 adds the tray's directory to that list once the files exist, so this task and the next both end with the whole suite green.

- [ ] **Step 6: Verify the generated class exists**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/OpenInzone.Control -c Debug
grep -rl "class Strings" src/OpenInzone.Control/obj/Debug/net8.0/ | head -1
```

Expected: a path to a generated `Strings.Designer.cs` (or similarly named) file. If nothing is found, `GenerateSource` did not take — check the `Update=` path matches the file's location exactly.

- [ ] **Step 7: Commit**

```bash
git add src/OpenInzone.Control/Resources src/OpenInzone.Control/OpenInzone.Control.csproj tests/OpenInzone.Core.Tests/Control/ResourceCompletenessTests.cs
git commit -m "Give the control layer somewhere to keep its words"
```

---

### Task 3: Move the Control strings onto the resources

**Files:**
- Modify: `src/OpenInzone.Control/SnapshotText.cs`, `src/OpenInzone.Control/HotkeyCommand.cs`, `src/OpenInzone.Control/DeviceController.cs:165`
- Modify: `src/OpenInzone.Tray/SettingsWindow.xaml.cs:26` (follows the `HotkeyCommand` change)
- Test: `tests/OpenInzone.Core.Tests/Control/SnapshotTextTests.cs`, `tests/OpenInzone.Core.Tests/Ipc/IpcRoundTripTests.cs`

**Interfaces:**
- Consumes: `OpenInzone.Control.Resources.Strings` from Task 2.
- Produces: `HotkeyCommand(string Id, Func<string> Name, string DefaultCombo, Action<IDeviceActions> Run)` with `DisplayName => Name()`. **The positional parameter changes from `string DisplayName` to `Func<string> Name`** — any construction site must be updated. `HotkeyCommand.All` and `HotkeyCommand.ById` keep their signatures.

- [ ] **Step 1: Rewrite the SnapshotText tests to set a culture**

Replace the body of `tests/OpenInzone.Core.Tests/Control/SnapshotTextTests.cs`'s assertions so each Japanese expectation runs under an explicit culture. Add this helper to the class and wrap the existing Japanese assertions in it:

```csharp
    /// <summary>
    /// Runs a block with the UI culture pinned. The strings under test now come from resources, so
    /// without this the assertions would depend on whatever culture the test host happened to be
    /// in - which on a build agent is not Japanese.
    /// </summary>
    private static void InCulture(string tag, Action body)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture =
                System.Globalization.CultureInfo.GetCultureInfo(tag);
            body();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }
```

Then rewrite each affected fact. For example, the mute test becomes:

```csharp
    [Fact]
    public void The_tooltip_says_when_the_headset_is_muted()
    {
        InCulture("ja", () =>
        {
            Assert.Equal("16/30", SnapshotText.VolumeWithMute(Live));
            Assert.Equal("16/30（ミュート）", SnapshotText.VolumeWithMute(Live with { VolumeMuted = true }));
        });

        InCulture("en", () =>
            Assert.Equal("16/30 (muted)", SnapshotText.VolumeWithMute(Live with { VolumeMuted = true })));
    }
```

Apply the same treatment to the balance test (`中央` / `Centre`, `ゲーム寄り 1.0` / `Game 1.0`, `チャット寄り 2.0` / `Chat 2.0`), the mic level test (`利用不可` / `Unavailable`), the battery test (`ケース` / `Case`) and the tooltip test. The `--` and `16/30` assertions are culture-free and need no wrapper.

- [ ] **Step 2: Run and watch them fail**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test --filter SnapshotTextTests
```

Expected: the `en` assertions fail — `SnapshotText` still returns Japanese regardless of culture.

- [ ] **Step 3: Move `SnapshotText` onto the resources**

In `src/OpenInzone.Control/SnapshotText.cs`, add `using OpenInzone.Control.Resources;` and replace the four literal sites:

```csharp
    public static string VolumeWithMute(DeviceSnapshot state) =>
        !state.Connected ? Unavailable
        : state.VolumeMuted ? string.Format(Strings.Status_VolumeMuted, Volume(state))
        : Volume(state);
```

```csharp
        var balance = new MixBalance(MixBalance.Clamp(state.Balance));
        return balance.IsCentred
            ? Strings.Status_BalanceCentre
            : string.Format(
                balance.FavoursGame ? Strings.Status_BalanceGame : Strings.Status_BalanceChat,
                balance.Notches);
```

```csharp
    public static string MicLevel(DeviceSnapshot state) =>
        !state.Connected ? Unavailable
        : state.MicLevelAvailable ? $"{state.MicLevel}%"
        : Strings.Status_MicUnavailable;
```

```csharp
        return state.Battery.HasSeparateBuds
            ? $"L {Percent(state.Battery.Left)}   R {Percent(state.Battery.Right)}   " +
              $"{Strings.Status_BatteryCase} {Percent(state.Battery.Case)}"
            : Percent(state.Battery.Left);
```

Also update the class's `<remarks>`, which names `ケース` as an example: change *"one saying ケース and the other case"* to *"one saying ケース and the other Case"* — the point it makes is still true and worth keeping.

- [ ] **Step 4: Move `HotkeyCommand` onto the resources**

In `src/OpenInzone.Control/HotkeyCommand.cs`, add `using OpenInzone.Control.Resources;` and change the record and its catalogue:

```csharp
/// <summary>
/// One assignable command. The catalogue is the single place a command is defined: the settings
/// window lists it, the configuration keys off its id, and the hotkey host registers it. Adding a
/// command means adding one entry here and nothing else.
/// </summary>
/// <param name="Id">
/// Persisted as the key in hotkeys.json and named over IPC and by the Stream Deck plugin. It is an
/// identifier, not a label: it is never translated and never renamed.
/// </param>
/// <param name="Name">
/// Read late rather than stored, so the catalogue - which is static and built once - still answers
/// in whatever language the window is being shown in.
/// </param>
public sealed record HotkeyCommand(string Id, Func<string> Name, string DefaultCombo, Action<IDeviceActions> Run)
{
    public string DisplayName => Name();

    /// <summary>Steps match what INZONE Hub itself moves by: ten for balance, one for volume.</summary>
    public static IReadOnlyList<HotkeyCommand> All { get; } =
    [
        new("volume-up",      () => Strings.Hotkey_VolumeUp,      "Ctrl+Alt+Right",     d => d.AdjustVolume(+1)),
        new("volume-down",    () => Strings.Hotkey_VolumeDown,    "Ctrl+Alt+Left",      d => d.AdjustVolume(-1)),
        // Game is the low end of the scale, so moving towards it is a step down. These were the
        // other way round, which made both keys do the opposite of what they are named.
        new("balance-game",   () => Strings.Hotkey_BalanceGame,   "Ctrl+Alt+Up",        d => d.AdjustBalance(-MixBalance.HubStep)),
        new("balance-chat",   () => Strings.Hotkey_BalanceChat,   "Ctrl+Alt+Down",      d => d.AdjustBalance(+MixBalance.HubStep)),
        new("balance-centre", () => Strings.Hotkey_BalanceCentre, "Ctrl+Alt+Home",      d => d.SetBalance(MixBalance.Centre)),
        new("mic-mute",       () => Strings.Hotkey_MicMute,       "Ctrl+Alt+Shift+M",   d => d.ToggleMicMute()),
        new("mic-up",         () => Strings.Hotkey_MicUp,         "Ctrl+Alt+PageUp",    d => d.AdjustMicLevel(+5)),
        new("mic-down",       () => Strings.Hotkey_MicDown,       "Ctrl+Alt+PageDown",  d => d.AdjustMicLevel(-5)),
    ];

    public static HotkeyCommand? ById(string id) => All.FirstOrDefault(c => c.Id == id);
}
```

`tests/OpenInzone.Core.Tests/Control/HotkeyCommandTests.cs:51` asserts `DisplayName` is non-blank and needs no change — it now exercises the resource lookup, which is worth having.

- [ ] **Step 5: Make the device error English**

In `src/OpenInzone.Control/DeviceController.cs:165`:

```csharp
            throw new InvalidOperationException("The headset is not connected.");
```

This is a log line and an IPC error payload, not window text. The tray shows its own wording for the condition.

Update the two assertions in `tests/OpenInzone.Core.Tests/Ipc/IpcRoundTripTests.cs:178,180`:

```csharp
        server.PublishError("The headset is not connected.");

        Assert.Equal("The headset is not connected.", await Within(complaint));
```

- [ ] **Step 6: Run the tests and confirm they pass**

```bash
dotnet test
```

Expected: 0 failures.

- [ ] **Step 7: Confirm the tray still builds**

```bash
dotnet build src/OpenInzone.Tray -c Release
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. `SettingsWindow.xaml.cs:26` (`public string DisplayName => command.DisplayName;`) compiles unchanged because `DisplayName` survives as a property.

- [ ] **Step 8: Commit**

```bash
git add src/OpenInzone.Control tests/OpenInzone.Core.Tests
git commit -m "Read the control layer's words from resources instead of the source"
```

---

### Task 4: The tray resource set, the culture, and the small surfaces

**Files:**
- Create: `src/OpenInzone.Tray/Resources/Strings.resx`, `Strings.ja.resx`, `Strings.zh-Hans.resx`
- Modify: `src/OpenInzone.Tray/OpenInzone.Tray.csproj`, `App.xaml.cs`, `TrayIcon.cs`, `FlyoutWindow.xaml`, `FlyoutWindow.xaml.cs`

**Interfaces:**
- Consumes: `UiLanguage.Resolve` (Task 1), `HotkeyConfig.Language` (Task 1).
- Produces: `OpenInzone.Tray.Resources.Strings`.

- [ ] **Step 1: Create the three resource files**

`src/OpenInzone.Tray/Resources/Strings.resx` (English), `Strings.ja.resx`, `Strings.zh-Hans.resx`, using the same RESX skeleton given in Task 2 Step 1 — header block included, `xml:space="preserve"` on every entry.

| Key | English | Japanese | Simplified Chinese |
|---|---|---|---|
| `Flyout_NotConnected` | `Not connected` | `未接続` | `未连接` |
| `Flyout_Volume` | `Volume` | `音量` | `音量` |
| `Flyout_MicMute` | `Microphone mute` | `マイクミュート` | `麦克风静音` |
| `Flyout_Game` | `Game` | `ゲーム` | `游戏` |
| `Flyout_Chat` | `Chat` | `チャット` | `语音` |
| `Tray_Settings` | `Settings` | `設定` | `设置` |
| `Tray_Help` | `Help` | `ヘルプ` | `帮助` |
| `Tray_Exit` | `Exit` | `終了` | `退出` |
| `Tray_TooltipVolume` | `Volume` | `音量` | `音量` |
| `Tray_TooltipBattery` | `Battery` | `バッテリー` | `电量` |
| `Tray_NotConnected` | `OpenInzone - Not connected` | `OpenInzone - 未接続` | `OpenInzone - 未连接` |
| `App_CannotConnectTitle` | `Cannot reach the headset` | `ヘッドセットに接続できません` | `无法连接耳机` |
| `App_ConfigUnreadableTitle` | `The configuration could not be read` | `設定ファイルを読み込めませんでした` | `无法读取配置文件` |
| `App_ConfigUnreadableBody` | `Started with the default hotkeys. Correct {0}: {1}` | `既定のホットキーで起動しました。{0} を修正してください: {1}` | `已使用默认快捷键启动。请修正 {0}：{1}` |
| `App_UpdateAvailableTitle` | `An update is available` | `アップデートがあります` | `有可用更新` |
| `App_UpdateAvailableBody` | `Version {0} is available. You can install it from Settings.` | `バージョン {0} が利用可能です。設定から更新できます。` | `版本 {0} 可用。可在设置中更新。` |
| `App_ErrorTitle` | `Something went wrong` | `エラーが発生しました` | `发生错误` |
| `App_HotkeyFailedTitle` | `The hotkeys could not be registered` | `ホットキーを登録できませんでした` | `无法注册快捷键` |
| `App_HotkeyFailedBody` | `Another application is already using these, so they are inactive: {0}` | `他のアプリと競合しているため、次のショートカットは無効です: {0}` | `以下快捷键与其他应用冲突，已停用：{0}` |
| `App_ListSeparator` | `, ` | `、` | `、` |

`App_ListSeparator` looks trivial and is not: `App.xaml.cs:145` joins names with the Japanese ideographic comma `、`, which is wrong in an English or Chinese sentence.

- [ ] **Step 2: Wire the resources into the tray project**

In `src/OpenInzone.Tray/OpenInzone.Tray.csproj`, add to the `<PropertyGroup>`:

```xml
    <NeutralResourcesLanguage>en</NeutralResourcesLanguage>
```

and a new `<ItemGroup>`:

```xml
  <ItemGroup>
    <!-- Same in-box MSBuild generator Task 2 wired into OpenInzone.Control, for the same reason:
         it runs on a plain `dotnet build`, so the WSL-published release gets the class too. -->
    <EmbeddedResource Update="Resources\Strings.resx"
                      StronglyTypedClassName="Strings"
                      StronglyTypedNamespace="OpenInzone.Tray.Resources"
                      StronglyTypedFileName="$(IntermediateOutputPath)Strings.g.cs"
                      StronglyTypedLanguage="CSharp"
                      PublicClass="true"
                      Generator="" />
  </ItemGroup>
```

Copy this shape from `src/OpenInzone.Control/OpenInzone.Control.csproj`, which already has it working. **Do not use `GenerateSource="true"`** — that attribute does not exist in SDK 8.0.424 and generates nothing without complaining.

- [ ] **Step 3: Bring the tray's resources under the completeness test**

The tray's files now exist, so add them to the list in `tests/OpenInzone.Core.Tests/Control/ResourceCompletenessTests.cs`:

```csharp
    public static TheoryData<string> ResourceDirectories()
    {
        // Not a collection expression: xunit 2.5.3's TheoryData does not support one (CS0029).
        var data = new TheoryData<string>();
        data.Add(Path.Combine("src", "OpenInzone.Control", "Resources"));
        data.Add(Path.Combine("src", "OpenInzone.Tray", "Resources"));
        return data;
    }
```

and drop the now-stale sentence from its doc comment about Task 4 adding the tray's directory.

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test --filter ResourceCompletenessTests
```

Expected: PASS for both theory cases.

- [ ] **Step 4: Apply the culture at startup, before anything with text on it exists**

**Read this before editing.** `OnStartup` builds `_tray = new TrayIcon()` at line 33 and `_flyout = new FlyoutWindow(...)` at line 52, but does not reach `_config = LoadConfig()` until line 61. The tray's context menu is built in its constructor, so setting the culture from `_config` where the configuration is loaded would be **too late**: the menu and the flyout would already exist in the wrong language.

Moving `LoadConfig()` up is not the fix either. Its failure path calls `_tray?.ShowBalloon(...)` at line 93, and the comment at line 31 records why the tray is constructed first: the balloon is the only way anything in `OnStartup` can reach the user. Move the load above the tray and that error becomes invisible.

So peek at the language separately, ahead of everything, and leave `LoadConfig()` exactly where it is.

In `src/OpenInzone.Tray/App.xaml.cs`, immediately after the single-instance mutex check and **before** `_tray = new TrayIcon();`:

```csharp
        // Before the tray icon, whose menu is built in its constructor, and before the flyout.
        // Anything constructed above this line keeps the culture it was built under; no later
        // assignment moves text that already exists.
        var culture = System.Globalization.CultureInfo.GetCultureInfo(
            UiLanguage.Resolve(ConfiguredLanguage(), AppContext.BaseDirectory));
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Globalization.CultureInfo.CurrentUICulture = culture;
```

and add the helper to the class:

```csharp
    /// <summary>
    /// The language out of hotkeys.json, or null if it cannot be had. Deliberately a second, cheap
    /// read of the file that LoadConfig reads properly further down: the culture has to be settled
    /// before the tray icon is built, and LoadConfig cannot run that early because it reports its
    /// failures through a balloon the tray does not own yet. A malformed file is silent here and
    /// loud there, which is the right way round.
    /// </summary>
    private static string? ConfiguredLanguage()
    {
        try
        {
            string path = HotkeyConfig.DefaultPath;
            return File.Exists(path) ? HotkeyConfig.FromJson(File.ReadAllText(path)).Language : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
```

- [ ] **Step 5: Move the tray icon's text onto resources**

In `src/OpenInzone.Tray/TrayIcon.cs`, add `using OpenInzone.Tray.Resources;` and replace lines 27–30 and 62–64:

```csharp
        menu.Items.Add(Strings.Tray_Settings, null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(Strings.Tray_Help, null, (_, _) => ProjectLinks.Open(ProjectLinks.Repository));
```

```csharp
        menu.Items.Add(Strings.Tray_Exit, null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
```

```csharp
            ? $"{state.Model}\n{Strings.Tray_TooltipVolume} {SnapshotText.VolumeWithMute(state)}\n" +
              $"{Strings.Tray_TooltipBattery} {SnapshotText.Battery(state)}"
            : Strings.Tray_NotConnected;
```

- [ ] **Step 6: Move the flyout's text onto resources**

In `src/OpenInzone.Tray/FlyoutWindow.xaml`, add the namespace to the root element:

```xml
        xmlns:res="clr-namespace:OpenInzone.Tray.Resources"
```

and replace the five literals (lines 30, 39, 49, 67, 71):

```xml
      <TextBlock x:Name="ModelText" Text="{x:Static res:Strings.Flyout_NotConnected}" FontSize="13" Margin="0,0,0,12" Opacity="0.7" />
```

```xml
              Width="28" Height="28" Stretch="Uniform" ToolTip="{x:Static res:Strings.Flyout_Volume}" />
```

```xml
        <Button x:Name="MicMuteButton" Style="{StaticResource IconButton}" ToolTip="{x:Static res:Strings.Flyout_MicMute}">
```

```xml
              Width="28" Height="28" Stretch="Uniform" ToolTip="{x:Static res:Strings.Flyout_Game}" />
```

```xml
              Width="28" Height="28" Stretch="Uniform" ToolTip="{x:Static res:Strings.Flyout_Chat}" />
```

In `src/OpenInzone.Tray/FlyoutWindow.xaml.cs:78`, add `using OpenInzone.Tray.Resources;` and:

```csharp
            ModelText.Text = state.Connected ? state.Model : Strings.Flyout_NotConnected;
```

- [ ] **Step 7: Move the balloon messages onto resources**

In `src/OpenInzone.Tray/App.xaml.cs`, add `using OpenInzone.Tray.Resources;` and replace each site:

```csharp
            _tray?.ShowBalloon(Strings.App_CannotConnectTitle, message));
```

```csharp
            _tray?.ShowBalloon(Strings.App_ConfigUnreadableTitle,
                string.Format(Strings.App_ConfigUnreadableBody, HotkeyConfig.DefaultPath, ex.Message));
```

```csharp
            _ = Dispatcher.BeginInvoke(() => _tray?.ShowBalloon(Strings.App_UpdateAvailableTitle,
                string.Format(Strings.App_UpdateAvailableBody, update.Version)));
```

```csharp
        _tray?.ShowBalloon(Strings.App_ErrorTitle, e.Exception.Message);
```

```csharp
        _tray?.ShowBalloon(Strings.App_HotkeyFailedTitle,
            string.Format(Strings.App_HotkeyFailedBody, string.Join(Strings.App_ListSeparator, names)));
```

- [ ] **Step 8: Build and run everything**

```bash
dotnet build src/OpenInzone.Tray -c Release
dotnet test
```

Expected: build succeeds with 0 warnings; all tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/OpenInzone.Tray tests
git commit -m "Give the tray, the flyout and the balloons their words in three languages"
```

---

### Task 5: Move the tray's resources into their own assembly

**Files:**
- Create: `src/OpenInzone.Resources/OpenInzone.Resources.csproj`
- Move: `src/OpenInzone.Tray/Resources/Strings.resx`, `Strings.ja.resx`, `Strings.zh-Hans.resx` → `src/OpenInzone.Resources/`
- Modify: `src/OpenInzone.Tray/OpenInzone.Tray.csproj`, `App.xaml.cs`, `TrayIcon.cs`, `FlyoutWindow.xaml`, `FlyoutWindow.xaml.cs`, `OpenInzone.sln`
- Modify: `tests/OpenInzone.Core.Tests/Control/ResourceCompletenessTests.cs`

**Interfaces:**
- Consumes: the resource files and keys created in Task 4 — unchanged in content.
- Produces: `OpenInzone.Resources.Strings`, the same public static class with the same 20 keys, in a new assembly. The namespace changes from `OpenInzone.Tray.Resources` to `OpenInzone.Resources`. Tasks 6, 7 and 8 reference it from XAML as `xmlns:res="clr-namespace:OpenInzone.Resources;assembly=OpenInzone.Resources"`.

**Why this task exists.** Task 4 discovered that WPF cannot resolve a same-assembly resx-generated class from XAML on SDK 8.0.424. A `clr-namespace:` reference with no `assembly=` sets `_RequireMCPass2ForMainAssembly`, and the `_CompileTemporaryAssembly` target it triggers depends on `BuildOnlySettings;ResolveKeySource;ResolveProjectReferences;CoreCompile` — it never runs `PrepareResources`, so `Strings.g.cs` does not exist for that temporary compile and every file importing the resource namespace fails with CS0234. Task 4 worked around it by assigning five flyout values from code-behind.

That workaround does not scale to the settings window, which has around forty labels in markup, and it would break `VoiceGuidanceLanguageTests`, which pins the voice-guidance labels to the bytes they select by reading those labels **out of the markup**. Both need `{x:Static}`.

The controller verified the fix: the same `{x:Static}` against a resource class in a **different** assembly builds clean, 0 warnings. So the resources move to an assembly of their own.

`OpenInzone.Control` keeps its own resources where they are. Nothing in XAML references them — hotkey names reach the window by data binding — so they have no reason to move.

- [ ] **Step 1: Create the project**

Create `src/OpenInzone.Resources/OpenInzone.Resources.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>OpenInzone.Resources</RootNamespace>
    <AssemblyName>OpenInzone.Resources</AssemblyName>
    <Copyright>Copyright (C) 2026 penguinwokrs</Copyright>
    <Product>OpenInzone</Product>
    <!-- English is the neutral culture: a language with no satellite assembly gets English, which
         is the whole point. Japanese here would make every unsupported language fall back to
         Japanese instead. -->
    <NeutralResourcesLanguage>en</NeutralResourcesLanguage>
  </PropertyGroup>

  <ItemGroup>
    <!-- Same in-box MSBuild generator as OpenInzone.Control uses, for the same reason: it runs on
         a plain `dotnet build`, so the WSL-published release gets the class too. -->
    <EmbeddedResource Update="Strings.resx"
                      StronglyTypedClassName="Strings"
                      StronglyTypedNamespace="OpenInzone.Resources"
                      StronglyTypedFileName="$(IntermediateOutputPath)Strings.g.cs"
                      StronglyTypedLanguage="CSharp"
                      PublicClass="true"
                      Generator="" />
  </ItemGroup>
</Project>
```

**`net8.0`, not `net8.0-windows`.** This assembly holds strings and nothing else. Keeping it platform-neutral means the test project can reference it directly if a later test needs to, without dragging the suite onto Windows.

- [ ] **Step 2: Move the three resx files**

```bash
git mv src/OpenInzone.Tray/Resources/Strings.resx src/OpenInzone.Resources/Strings.resx
git mv src/OpenInzone.Tray/Resources/Strings.ja.resx src/OpenInzone.Resources/Strings.ja.resx
git mv src/OpenInzone.Tray/Resources/Strings.zh-Hans.resx src/OpenInzone.Resources/Strings.zh-Hans.resx
rmdir src/OpenInzone.Tray/Resources
```

**Their contents do not change.** Not one key, not one translated value. If a value differs after this task, that is a mistake. Remove the now-dead `<EmbeddedResource Update="Resources\Strings.resx" ...>` block and the `<NeutralResourcesLanguage>` line from `src/OpenInzone.Tray/OpenInzone.Tray.csproj`, and add:

```xml
    <ProjectReference Include="..\OpenInzone.Resources\OpenInzone.Resources.csproj" />
```

to the existing `ItemGroup` that already holds the `OpenInzone.Control` and `OpenInzone.Ipc` references.

- [ ] **Step 3: Add the project to the solution**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet sln OpenInzone.sln add src/OpenInzone.Resources/OpenInzone.Resources.csproj
```

- [ ] **Step 4: Point the three code files at the new namespace**

In `src/OpenInzone.Tray/App.xaml.cs`, `TrayIcon.cs` and `FlyoutWindow.xaml.cs`, change

```csharp
using OpenInzone.Tray.Resources;
```

to

```csharp
using OpenInzone.Resources;
```

Nothing else in those files changes: the `Strings.Xxx` call sites keep the same key names.

- [ ] **Step 5: Put the flyout's five values back in markup**

This is the point of the task — undo Task 4's workaround now that markup can see the resources.

In `src/OpenInzone.Tray/FlyoutWindow.xaml`, add to the root `<Window>` element:

```xml
        xmlns:res="clr-namespace:OpenInzone.Resources;assembly=OpenInzone.Resources"
```

and set the five values in markup again:

```xml
      <TextBlock x:Name="ModelText" Text="{x:Static res:Strings.Flyout_NotConnected}" FontSize="13" Margin="0,0,0,12" Opacity="0.7" />
```

```xml
              Width="28" Height="28" Stretch="Uniform" ToolTip="{x:Static res:Strings.Flyout_Volume}" />
```

```xml
        <Button x:Name="MicMuteButton" Style="{StaticResource IconButton}" ToolTip="{x:Static res:Strings.Flyout_MicMute}">
```

```xml
              Width="28" Height="28" Stretch="Uniform" ToolTip="{x:Static res:Strings.Flyout_Game}" />
```

```xml
              Width="28" Height="28" Stretch="Uniform" ToolTip="{x:Static res:Strings.Flyout_Chat}" />
```

Then remove the corresponding code-behind assignments Task 4 added to `FlyoutWindow.xaml.cs`, **including the `x:Name`s it added purely to reach those elements** (`VolumeIcon`, `GameIcon`, `ChatIcon`) if nothing else uses them. Leave `ModelText`'s runtime assignment alone — `ModelText.Text` is reassigned as the device connects and disconnects, so it needs both the markup default and the code-behind update.

- [ ] **Step 6: Update the completeness test's directory list**

In `tests/OpenInzone.Core.Tests/Control/ResourceCompletenessTests.cs`, the tray path is now wrong:

```csharp
        data.Add(Path.Combine("src", "OpenInzone.Control", "Resources"));
        data.Add(Path.Combine("src", "OpenInzone.Resources"));
```

- [ ] **Step 7: Build and test**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/OpenInzone.Tray -c Release
dotnet test
```

Expected: build succeeds with 0 warnings — **this is the load-bearing check for the whole task**, because it is the `{x:Static}` in Step 5 that Task 4 could not get to compile. All tests pass, same count as before this task; nothing here should change a test's outcome.

- [ ] **Step 8: Confirm the satellites still ship**

```bash
dotnet publish src/OpenInzone.Tray -c Release -r win-x64 --self-contained true -o /tmp/openinzone-pub
ls /tmp/openinzone-pub/ja /tmp/openinzone-pub/zh-Hans
```

Expected: each directory holds `OpenInzone.Control.resources.dll` **and** `OpenInzone.Resources.resources.dll`. The second one is new, and its absence would mean the tray ships with English only while looking correct in every other way.

- [ ] **Step 9: Commit**

```bash
git add src tests OpenInzone.sln
git commit -m "Move the window's words where the markup compiler can see them"
```

---

### Task 6: The settings window's markup

**Files:**
- Modify: `src/OpenInzone.Tray/SettingsWindow.xaml`
- Modify: `src/OpenInzone.Resources/Strings.resx`, `Strings.ja.resx`, `Strings.zh-Hans.resx`
- Test: `tests/OpenInzone.Core.Tests/Control/VoiceGuidanceLanguageTests.cs`

**Interfaces:**
- Consumes: `OpenInzone.Resources.Strings` (Task 4).
- Produces: no new API. `LanguageBox`, `DevicePanel` and every other `x:Name` keep their current names.

- [ ] **Step 1: Add the markup keys to all three resource files**

| Key | English | Japanese | Simplified Chinese |
|---|---|---|---|
| `Settings_Title` | `OpenInzone Settings` | `OpenInzone の設定` | `OpenInzone 设置` |
| `Settings_Tab_General` | `General` | `全般` | `常规` |
| `Settings_Tab_Device` | `Device` | `デバイス` | `设备` |
| `Settings_Tab_Hotkeys` | `Hotkeys` | `ホットキー` | `快捷键` |
| `Settings_Tab_Update` | `Update` | `アップデート` | `更新` |
| `Settings_Tab_Plugin` | `Plugin` | `プラグイン` | `插件` |
| `Settings_Autostart` | `Run when Windows starts` | `Windows の起動時に常駐する` | `开机时随 Windows 启动` |
| `Settings_AutostartHint` | `Puts the tray icon up when you log in.` | `ログイン時にトレイアイコンを出します。` | `登录时显示托盘图标。` |
| `Settings_CheckUpdates` | `Check for updates at startup` | `起動時に更新を確認する` | `启动时检查更新` |
| `Settings_CheckUpdatesHint` | `Looks at the GitHub releases. Says nothing if it cannot.` | `GitHub のリリースを見に行きます。失敗しても何も表示しません。` | `查看 GitHub 上的发布。失败时不作任何提示。` |
| `Settings_AppliedImmediatelyHint` | `Changes take effect as you make them. There is nothing to save.` | `変更はその場で反映されます。保存する操作はありません。` | `更改即时生效，无需保存。` |
| `Settings_DeviceQuerying` | `Asking the headset…` | `ヘッドセットに問い合わせています…` | `正在查询耳机…` |
| `Settings_Ambient` | `Ambient sound control` | `外音コントロール` | `环境声控制` |
| `Settings_AmbientOff` | `Off` | `オフ` | `关闭` |
| `Settings_NoiseCancelling` | `Noise cancelling` | `ノイズキャンセリング` | `降噪` |
| `Settings_AmbientOn` | `Ambient sound` | `外音取り込み` | `环境声` |
| `Settings_AmbientLevel` | `Ambient level` | `取り込みレベル` | `环境声等级` |
| `Settings_VoiceFocus` | `Voice focus` | `ボイスフォーカス` | `人声聚焦` |
| `Settings_Sidetone` | `Sidetone` | `サイドトーン` | `侧音` |
| `Settings_Other` | `Other` | `その他` | `其他` |
| `Settings_AutoPowerOff` | `Automatic power off` | `自動電源オフ` | `自动关机` |
| `Settings_BluetoothSwitch` | `Switch the connection automatically for Bluetooth calls` | `Bluetooth の発信・着信時に自動で接続を切り替える` | `蓝牙拨出和来电时自动切换连接` |
| `Settings_VoiceGuidance` | `Voice guidance` | `音声ガイド` | `语音提示` |
| `Settings_VoiceGuidanceLanguage` | `Voice guidance language` | `音声ガイダンスの言語` | `语音提示语言` |
| `Settings_VoiceLanguage_English` | `English` | `英語` | `英语` |
| `Settings_VoiceLanguage_Japanese` | `Japanese` | `日本語` | `日语` |
| `Settings_VoiceLanguage_Chinese` | `Chinese` | `中国語` | `汉语` |
| `Settings_HotkeyHint` | `Select a row, then press a key combination to assign it. Esc clears one.` | `行を押してからキーの組み合わせを押すと割り当てます。Esc で解除。` | `选中一行后按下组合键即可分配。按 Esc 清除。` |
| `Settings_ResetDefaults` | `Restore defaults` | `既定に戻す` | `恢复默认` |
| `Settings_CheckNow` | `Check for updates` | `更新を確認` | `检查更新` |
| `Settings_StreamDeckPlugin` | `Stream Deck plugin` | `Stream Deck プラグイン` | `Stream Deck 插件` |
| `Settings_PluginDescription` | `Puts the headset's volume, balance, microphone and battery on Stream Deck keys. Once imported it works whether or not this window is open.` | `ヘッドセットの音量・バランス・マイク・バッテリーを Stream Deck のキーから操作できます。取り込んだあとは、このアプリを開いていなくても動きます。` | `可通过 Stream Deck 按键控制耳机的音量、平衡、麦克风和电量。导入后，即使不打开本应用也能使用。` |
| `Settings_PluginDownload` | `Download` | `ダウンロード` | `下载` |
| `Settings_PluginImport` | `Import into Stream Deck` | `Stream Deck に取り込む` | `导入到 Stream Deck` |
| `Settings_OpenReleasePage` | `Open the releases page` | `リリースページを開く` | `打开发布页面` |

The voice-guidance label changes from the bare `言語` to `音声ガイダンスの言語`. That is deliberate — the general tab is about to gain a combo also called "language", and the two must be told apart at a glance.

- [ ] **Step 2: Add the namespace and replace every literal in the markup**

In `src/OpenInzone.Tray/SettingsWindow.xaml`, add to the root `<Window>` element:

```xml
        xmlns:res="clr-namespace:OpenInzone.Resources;assembly=OpenInzone.Resources"
```

Then replace each literal with the matching `{x:Static res:Strings.KEY}`. Line by line, using the current line numbers:

- 4: `Title="{x:Static res:Strings.Settings_Title}"`
- 115: `<TabItem Header="{x:Static res:Strings.Settings_Tab_General}">`
- 117: `Content="{x:Static res:Strings.Settings_Autostart}"`
- 120: `Text="{x:Static res:Strings.Settings_AutostartHint}"`
- 122: `Content="{x:Static res:Strings.Settings_CheckUpdates}"`
- 125: `Text="{x:Static res:Strings.Settings_CheckUpdatesHint}"`
- 128: `Text="{x:Static res:Strings.Settings_AppliedImmediatelyHint}"`
- 132: `<TabItem Header="{x:Static res:Strings.Settings_Tab_Device}">`
- 136: `Text="{x:Static res:Strings.Settings_DeviceQuerying}"`
- 138: `Text="{x:Static res:Strings.Settings_Ambient}"`
- 140: `Content="{x:Static res:Strings.Settings_AmbientOff}"`
- 142: `Content="{x:Static res:Strings.Settings_NoiseCancelling}"`
- 144: `Content="{x:Static res:Strings.Settings_AmbientOn}"`
- 152: `Text="{x:Static res:Strings.Settings_AmbientLevel}"`
- 161: `Content="{x:Static res:Strings.Settings_VoiceFocus}"`
- 164: `Text="{x:Static res:Strings.Settings_Sidetone}"`
- 175: `Text="{x:Static res:Strings.Settings_Other}"`
- 176: `Content="{x:Static res:Strings.Settings_AutoPowerOff}"`
- 178: `Content="{x:Static res:Strings.Settings_BluetoothSwitch}"`
- 179: `Content="{x:Static res:Strings.Settings_VoiceGuidance}"`
- 181: `Text="{x:Static res:Strings.Settings_VoiceGuidanceLanguage}"`
- 184–186: the three `ComboBoxItem`s, **`Tag` untouched**:

```xml
              <ComboBoxItem Content="{x:Static res:Strings.Settings_VoiceLanguage_English}" Tag="0" />
              <ComboBoxItem Content="{x:Static res:Strings.Settings_VoiceLanguage_Japanese}" Tag="1" />
              <ComboBoxItem Content="{x:Static res:Strings.Settings_VoiceLanguage_Chinese}" Tag="2" />
```

- 193: `<TabItem Header="{x:Static res:Strings.Settings_Tab_Hotkeys}">`
- 197: `Text="{x:Static res:Strings.Settings_HotkeyHint}"`
- 198: `Content="{x:Static res:Strings.Settings_ResetDefaults}"`
- 220: `<TabItem Header="{x:Static res:Strings.Settings_Tab_Update}">`
- 224: `Content="{x:Static res:Strings.Settings_CheckNow}"`
- 231: `<TabItem Header="{x:Static res:Strings.Settings_Tab_Plugin}">`
- 233: `Text="{x:Static res:Strings.Settings_StreamDeckPlugin}"`
- 235: `Text="{x:Static res:Strings.Settings_PluginDescription}"`
- 238: `Content="{x:Static res:Strings.Settings_PluginDownload}"`
- 240: `Content="{x:Static res:Strings.Settings_PluginImport}"`
- 248: the hyperlink's inline text becomes `<Run Text="{x:Static res:Strings.Settings_OpenReleasePage}" />`

- [ ] **Step 3: Rewrite `VoiceGuidanceLanguageTests` to read keys, not words**

`LanguageItems()` currently returns `(int Tag, string Content)` and the `Heard` table pins Japanese words. Replace both with the resource key, which is what actually identifies the label now:

```csharp
    /// <summary>What each byte was heard to do, named by the resource key the window shows.</summary>
    private static readonly (VoiceGuidanceLanguage Language, string ResourceKey)[] Heard =
    [
        (VoiceGuidanceLanguage.English, "Settings_VoiceLanguage_English"),
        (VoiceGuidanceLanguage.Japanese, "Settings_VoiceLanguage_Japanese"),
        (VoiceGuidanceLanguage.Chinese, "Settings_VoiceLanguage_Chinese"),
    ];

    /// <summary>
    /// The window's list, read out of the markup as tag and resource key together. The Content is
    /// now an {x:Static} reference, so what is pinned is which key sits against which byte - the
    /// words themselves live in the resx files and are checked by ResourceCompletenessTests.
    /// </summary>
    private static (int Tag, string ResourceKey)[] LanguageItems()
    {
        string path = Path.Combine(Repository(), "src", "OpenInzone.Tray", "SettingsWindow.xaml");
        var xaml = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";

        var box = xaml.Descendants(presentation + "ComboBox")
            .Single(element => (string?)element.Attribute(xaml2006 + "Name") == "LanguageBox");

        return [.. box.Elements(presentation + "ComboBoxItem")
            .Select(item => (
                int.Parse((string)item.Attribute("Tag")!),
                KeyFrom((string)item.Attribute("Content")!)))];
    }

    /// <summary>Pulls Settings_VoiceLanguage_Japanese out of "{x:Static res:Strings.Settings_VoiceLanguage_Japanese}".</summary>
    private static string KeyFrom(string staticReference)
    {
        const string marker = "res:Strings.";
        int start = staticReference.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Content is not a resource reference: {staticReference}");
        return staticReference[(start + marker.Length)..].TrimEnd('}', ' ');
    }
```

Then update the second fact to compare against `ResourceKey`:

```csharp
    [Fact]
    public void Each_label_carries_the_byte_that_was_heard_to_produce_it()
    {
        var offered = LanguageItems().ToDictionary(item => (VoiceGuidanceLanguage)item.Tag, item => item.ResourceKey);

        Assert.All(Heard, pair => Assert.Equal(pair.ResourceKey, offered[pair.Language]));
    }
```

The first fact, `The_window_offers_every_language_the_headset_takes_and_no_others`, reads only `Tag` and needs no change. Keep the class comment as it is — the reason the test exists has not changed.

Add one more fact, because the keys are now the thing that can silently drift:

```csharp
    [Fact]
    public void Every_key_the_window_names_actually_exists_in_the_resources()
    {
        string resx = Path.Combine(Repository(), "src", "OpenInzone.Resources", "Strings.resx");
        var defined = XDocument.Load(resx).Root!.Elements("data")
            .Select(d => (string)d.Attribute("name")!).ToHashSet();

        Assert.All(LanguageItems(), item => Assert.Contains(item.ResourceKey, defined));
    }
```

- [ ] **Step 4: Run the tests**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test --filter "VoiceGuidanceLanguageTests|SettingsMarkupTests|ResourceCompletenessTests"
```

Expected: PASS. `SettingsMarkupTests` should be unaffected — `{x:Static}` introduces no event-handler attributes — and its passing confirms that.

- [ ] **Step 5: Build the tray**

```bash
dotnet build src/OpenInzone.Tray -c Release
```

Expected: 0 warnings, 0 errors. A typo in a `{x:Static}` key is a compile error here, which is the point of the RESX choice.

- [ ] **Step 6: Commit**

```bash
git add src/OpenInzone.Tray tests/OpenInzone.Core.Tests/Control/VoiceGuidanceLanguageTests.cs
git commit -m "Read the settings window's labels from resources"
```

---

### Task 7: The settings window's code-behind

**Files:**
- Modify: `src/OpenInzone.Tray/SettingsWindow.xaml.cs`
- Modify: `src/OpenInzone.Resources/Strings.resx`, `Strings.ja.resx`, `Strings.zh-Hans.resx`

**Interfaces:**
- Consumes: `OpenInzone.Resources.Strings` (Task 4).
- Produces: no new API.

- [ ] **Step 1: Add the code-behind keys to all three resource files**

| Key | English | Japanese | Simplified Chinese |
|---|---|---|---|
| `Settings_HotkeyCapturing` | `Press a key combination` | `キーを押してください` | `请按下组合键` |
| `Settings_HotkeyConflict` | `{0} (in use by another application)` | `{0}（他のアプリが使用中）` | `{0}（其他应用正在使用）` |
| `Settings_HotkeyDuplicate` | `{0} (duplicate)` | `{0}（重複）` | `{0}（重复）` |
| `Settings_HotkeyUnassigned` | `Unassigned` | `未割り当て` | `未分配` |
| `Settings_CurrentVersion` | `Current version: {0}` | `現在のバージョン: {0}` | `当前版本：{0}` |
| `Settings_SaveFailed` | `The settings could not be saved: {0}` | `設定を保存できませんでした: {0}` | `无法保存设置：{0}` |
| `Settings_PluginChecking` | `Checking the release…` | `リリースを確認しています…` | `正在检查发布…` |
| `Settings_PluginNotFound` | `The latest release has no Stream Deck plugin in it. Open the releases page to check.` | `最新のリリースに Stream Deck プラグインが見つかりませんでした。リリースページを開いて確認してください。` | `最新发布中未找到 Stream Deck 插件。请打开发布页面查看。` |
| `Settings_PluginSaveTitle` | `Where to save the Stream Deck plugin` | `Stream Deck プラグインの保存先` | `Stream Deck 插件的保存位置` |
| `Settings_PluginFilter` | `Stream Deck plugin (*{0})|*{0}` | `Stream Deck プラグイン (*{0})|*{0}` | `Stream Deck 插件 (*{0})|*{0}` |
| `Settings_PluginDownloading` | `Downloading… {0}%` | `ダウンロード中… {0}%` | `正在下载… {0}%` |
| `Settings_PluginSaved` | `Saved: {0}` | `保存しました: {0}` | `已保存：{0}` |
| `Settings_PluginDownloadFailed` | `The download failed: {0}` | `ダウンロードに失敗しました: {0}` | `下载失败：{0}` |
| `Settings_PluginOpenFailed` | `It could not be opened: {0}` | `開けませんでした: {0}` | `无法打开：{0}` |
| `Settings_UpdateChecking` | `Checking…` | `確認しています…` | `正在检查…` |
| `Settings_UpdateButtonInstall` | `Update` | `更新` | `更新` |
| `Settings_UpdateAvailable` | `Version {0} is available.` | `バージョン {0} が利用可能です。` | `版本 {0} 可用。` |
| `Settings_UpdateNoInstaller` | `A newer version is published, but no installer can be found in it.` | `新しいバージョンが公開されていますが、インストーラーが見つかりません。` | `已发布新版本，但其中找不到安装程序。` |
| `Settings_UpdateUnreadable` | `GitHub's answer could not be read.` | `GitHub の応答を読み取れませんでした。` | `无法读取 GitHub 的响应。` |
| `Settings_UpdateUpToDate` | `This is the latest version.` | `最新バージョンです。` | `已是最新版本。` |
| `Settings_UpdateCheckFailed` | `The check failed: {0}` | `確認に失敗しました: {0}` | `检查失败：{0}` |
| `Settings_UpdateDownloading` | `Downloading… {0}%` | `ダウンロード中… {0}%` | `正在下载… {0}%` |
| `Settings_UpdateVerifyFailed` | `The downloaded file failed verification. It was not run.` | `ダウンロードしたファイルの検証に失敗しました。実行を中止しました。` | `下载的文件校验失败，已中止运行。` |
| `Settings_UpdateNoDigest` | `The downloaded installer cannot be verified (no SHA-256 was published). Run it anyway?` | `ダウンロードしたインストーラーの正当性を確認できません（SHA-256 が提供されていません）。実行しますか？` | `无法验证下载的安装程序（未提供 SHA-256）。仍要运行吗？` |
| `Settings_UpdateCancelled` | `The update was cancelled.` | `更新を中止しました。` | `已取消更新。` |
| `Settings_UpdateFailed` | `The update failed: {0}` | `更新に失敗しました: {0}` | `更新失败：{0}` |
| `Settings_DeviceApplied` | `Changes take effect as you make them.` | `変更はその場で反映されます。` | `更改即时生效。` |
| `Settings_DeviceUnresponsive` | `The headset is not answering.` | `ヘッドセットが応答していません。` | `耳机没有响应。` |

`Settings_UpdateUpToDate` keeps its own key rather than being folded in with the other two outcomes. The comment at `SettingsWindow.xaml.cs:467` records why: collapsing them told someone with an uninstallable newer release that they were up to date.

- [ ] **Step 2: Replace the literals**

Add `using OpenInzone.Resources;` and change each site:

```csharp
    public string Display =>
        Capturing ? Strings.Settings_HotkeyCapturing
        : Conflict ? string.Format(Strings.Settings_HotkeyConflict, Combo)
        : Duplicate ? string.Format(Strings.Settings_HotkeyDuplicate, Combo)
        : Combo.Length == 0 ? Strings.Settings_HotkeyUnassigned
        : Combo;
```

```csharp
        VersionText.Text = string.Format(Strings.Settings_CurrentVersion, UpdateChecker.CurrentVersion);
```

```csharp
            System.Windows.MessageBox.Show(this, string.Format(Strings.Settings_SaveFailed, ex.Message),
```

```csharp
        PluginStatusText.Text = Strings.Settings_PluginChecking;
```

```csharp
                    Strings.Settings_PluginNotFound;
```

```csharp
                Title = Strings.Settings_PluginSaveTitle,
```

```csharp
                Filter = string.Format(Strings.Settings_PluginFilter, PluginAsset.Extension),
```

```csharp
            var progress = new Progress<int>(percent =>
                PluginStatusText.Text = string.Format(Strings.Settings_PluginDownloading, percent));
```

```csharp
            PluginStatusText.Text = string.Format(Strings.Settings_PluginSaved, path);
```

```csharp
            PluginStatusText.Text = string.Format(Strings.Settings_PluginDownloadFailed, ex.Message);
```

```csharp
            PluginStatusText.Text = string.Format(Strings.Settings_PluginOpenFailed, ex.Message);
```

```csharp
        UpdateStatusText.Text = Strings.Settings_UpdateChecking;
```

```csharp
                UpdateButton.Content = Strings.Settings_UpdateButtonInstall;
                UpdateStatusText.Text = string.Format(Strings.Settings_UpdateAvailable, update.Version);
```

```csharp
                        Strings.Settings_UpdateNoInstaller,
                        Strings.Settings_UpdateUnreadable,
                    _ => Strings.Settings_UpdateUpToDate,
```

```csharp
            UpdateStatusText.Text = string.Format(Strings.Settings_UpdateCheckFailed, ex.Message);
```

```csharp
        var progress = new Progress<int>(percent =>
            UpdateButton.Content = string.Format(Strings.Settings_UpdateDownloading, percent));
```

```csharp
                UpdateStatusText.Text = Strings.Settings_UpdateVerifyFailed;
```

```csharp
                    Strings.Settings_UpdateNoDigest,
```

```csharp
                    UpdateStatusText.Text = Strings.Settings_UpdateCancelled;
```

```csharp
            UpdateStatusText.Text = string.Format(Strings.Settings_UpdateFailed, ex.Message);
```

```csharp
        UpdateButton.Content = Strings.Settings_UpdateButtonInstall;
```

```csharp
                ? Strings.Settings_DeviceApplied
                : Strings.Settings_DeviceUnresponsive;
```

Two `///` comments at lines 106 and 438 mention `確認`/`更新` as button text. Reword them to name the resource keys instead — `Settings_CheckNow` becomes `Settings_UpdateButtonInstall` — so a reader can find what they refer to.

- [ ] **Step 3: Verify no Japanese remains outside comments**

```bash
grep -nP '"[^"]*[\p{Hiragana}\p{Katakana}\p{Han}][^"]*"' src/OpenInzone.Tray/SettingsWindow.xaml.cs
```

Expected: no output. (Beware: `grep` here is ugrep — do not combine `-c` with `-o`, it counts matches, not lines.)

- [ ] **Step 4: Build and test**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/OpenInzone.Tray -c Release
dotnet test
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/OpenInzone.Tray
git commit -m "Read the settings window's messages from resources"
```

---

### Task 8: The language combo and the restart

**Files:**
- Modify: `src/OpenInzone.Tray/SettingsWindow.xaml`, `src/OpenInzone.Tray/SettingsWindow.xaml.cs`
- Modify: `src/OpenInzone.Resources/Strings.resx`, `Strings.ja.resx`, `Strings.zh-Hans.resx`

**Interfaces:**
- Consumes: `HotkeyConfig.Language` (Task 1), `UiLanguage.Supported` (Task 1).
- Produces: a `ComboBox` named `UiLanguageBox` in the general tab.

- [ ] **Step 1: Add the keys**

| Key | English | Japanese | Simplified Chinese |
|---|---|---|---|
| `Settings_UiLanguage` | `Display language` | `表示言語` | `显示语言` |
| `Settings_UiLanguageHint` | `Applies when the tray restarts.` | `トレイを再起動すると反映されます。` | `重启托盘程序后生效。` |
| `Settings_RestartTitle` | `Restart OpenInzone?` | `OpenInzone を再起動しますか？` | `要重启 OpenInzone 吗？` |
| `Settings_RestartPrompt` | `The display language changes when the tray restarts. Restart it now?` | `表示言語はトレイを再起動すると変わります。今すぐ再起動しますか？` | `显示语言将在托盘程序重启后更改。现在重启吗？` |

The three language names are **not** resources. Each is written in its own script and must read the same whatever language the window is currently in — that is the whole point of listing them that way.

- [ ] **Step 2: Add the combo to the general tab's markup**

In `src/OpenInzone.Tray/SettingsWindow.xaml`, inside the general `TabItem` (after the `CheckUpdatesBox` block and its hint, before the `Settings_AppliedImmediatelyHint` text at line 128):

```xml
        <TextBlock Text="{x:Static res:Strings.Settings_UiLanguage}" FontSize="13" Margin="0,20,0,0" />
        <ComboBox x:Name="UiLanguageBox" Width="200" HorizontalAlignment="Left" Margin="0,8,0,0"
                  SelectionChanged="OnUiLanguageChanged">
          <!-- Each language named in its own script on purpose: somebody looking at a window they
               cannot read needs to find their language without knowing its English name first.
               These are not resources for the same reason - they must not change with the UI. -->
          <ComboBoxItem Content="English" Tag="en" />
          <ComboBoxItem Content="日本語" Tag="ja" />
          <ComboBoxItem Content="简体中文" Tag="zh-Hans" />
        </ComboBox>
        <TextBlock Text="{x:Static res:Strings.Settings_UiLanguageHint}" FontSize="11"
                   Foreground="{StaticResource Quiet}" Margin="0,6,0,0" />
```

- [ ] **Step 3: Select the current language when the window opens**

In `SettingsWindow.xaml.cs`, in the constructor where `_config` is assigned (line 119) and the other general-tab controls are filled from it alongside `AutostartBox` and `CheckUpdatesBox`, add:

```csharp
        // The resolved language, not the configured one: with nothing configured the combo should
        // show what the window is actually in, which is whatever the installer chose.
        string current = UiLanguage.Resolve(_config.Language, AppContext.BaseDirectory);
        UiLanguageBox.SelectedItem = UiLanguageBox.Items.OfType<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(item => (string?)item.Tag == current);
```

- [ ] **Step 4: Handle the change**

Add the handler. Follow the file's existing convention — attach it in markup as above unless the control lives under `DevicePanel`, which this one does not, so markup is correct and `SettingsMarkupTests` stays satisfied.

```csharp
    /// <summary>
    /// Saves the choice and offers a restart. The window's text comes from {x:Static}, which is
    /// resolved once when the window is built, so nothing already on screen changes language - and
    /// a window half in the old language is worse than one that plainly asks to restart.
    /// </summary>
    private void OnUiLanguageChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;  // Fires once while the window is being built; that is not a choice.
        if (UiLanguageBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;

        string chosen = (string)item.Tag;
        if (chosen == UiLanguage.Resolve(_config.Language, AppContext.BaseDirectory)) return;

        _config.Language = chosen;
        SaveConfig();   // the window's existing save path, at line 210; the same one AutostartBox uses

        var answer = System.Windows.MessageBox.Show(this,
            Strings.Settings_RestartPrompt, Strings.Settings_RestartTitle,
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

        if (answer == System.Windows.MessageBoxResult.Yes) RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user accepts a restart. The application owns process lifetime.</summary>
    public event EventHandler? RestartRequested;
```

- [ ] **Step 5: Restart the process from `App.xaml.cs`**

In the `_tray.SettingsRequested` handler (line 67), just after `_settings = new SettingsWindow(_config, _hotkeys, _headset);` at line 75, add:

```csharp
        _settings.RestartRequested += (_, _) =>
        {
            // Environment.ProcessPath is the executable as launched, which is what has to come
            // back. Shutdown after starting it: the new process needs this one's hotkeys released.
            string? executable = Environment.ProcessPath;
            if (executable is null) return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = true,
            });
            Shutdown();
        };
```

- [ ] **Step 6: Build and test**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/OpenInzone.Tray -c Release
dotnet test
```

Expected: 0 warnings, all tests pass. `SettingsMarkupTests` must still pass — the new handler is attached in markup but the control is not under `DevicePanel`, which is the only place that rule applies.

- [ ] **Step 7: Commit**

```bash
git add src/OpenInzone.Tray
git commit -m "Let someone change the display language, and offer the restart it needs"
```

---

### Task 9: The installer

**Files:**
- Create: `installer/lang/ChineseSimplified.isl`
- Modify: `installer/openinzone.iss`

**Interfaces:**
- Consumes: `UiLanguage.MarkerFileName` (Task 1) — the file name `default-language` must match exactly.
- Produces: `{app}\default-language`.

- [ ] **Step 1: Vendor the Simplified Chinese language file**

Inno Setup 6 ships no Chinese `.isl` — confirmed against the installation on this machine, which has 29 of them including Japanese and Korean but no Chinese. Download `ChineseSimplified.isl` from the Inno Setup unofficial translations (`jrsoftware/issrc`, `Files/Languages/Unofficial/`) and save it as `installer/lang/ChineseSimplified.isl`. Keep its licence header intact.

- [ ] **Step 2: Reorder and extend `[Languages]`**

Replace the `[Languages]` section in `installer/openinzone.iss`:

```
[Languages]
; English first on purpose: Inno falls back to the first entry when the operating system's
; language matches none of them, and "anything we do not have becomes English" is the rule this
; installer is meant to follow. With Japanese first, a French machine got a Japanese wizard.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "chinesesimplified"; MessagesFile: "lang\ChineseSimplified.isl"
```

- [ ] **Step 3: Move the task descriptions into `[CustomMessages]`**

Add a new section after `[Languages]`:

```
[CustomMessages]
english.AutostartTask=Run when Windows starts
english.DesktopIconTask=Create a desktop shortcut
english.AdditionalTasks=Additional tasks:
japanese.AutostartTask=Windows の起動時に常駐する
japanese.DesktopIconTask=デスクトップにショートカットを作成する
japanese.AdditionalTasks=追加のタスク:
chinesesimplified.AutostartTask=开机时随 Windows 启动
chinesesimplified.DesktopIconTask=创建桌面快捷方式
chinesesimplified.AdditionalTasks=附加任务：
```

and replace `[Tasks]`:

```
[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:AdditionalTasks}"
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked
```

- [ ] **Step 4: Write the marker file after installing**

Add to the existing `[Code]` section:

```pascal
{ What the wizard settled on, in the tags UiLanguage.Resolve understands. The application never
  asks Windows what language it is in - it reads this - so an installation is the only thing that
  ever decides, and the zip, which has no such file, stays English. }
function LanguageTag(): String;
begin
  if CompareText(ActiveLanguage(), 'japanese') = 0 then
    Result := 'ja'
  else if CompareText(ActiveLanguage(), 'chinesesimplified') = 0 then
    Result := 'zh-Hans'
  else
    Result := 'en';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\default-language'), LanguageTag(), False);
end;
```

If the file already declares `CurStepChanged`, merge the `ssPostInstall` branch into the existing one rather than declaring it twice.

- [ ] **Step 5: Remove the marker on uninstall**

The existing `[UninstallDelete]` entry `Type: filesandordirs; Name: "{app}"` already takes the whole directory, so the marker goes with it. No change needed — verify the entry is still there rather than adding a duplicate.

- [ ] **Step 6: Build the installer**

```bash
./installer/build.sh
```

Expected: `dist/OpenInzone-<version>-setup.exe` is produced. If `ISCC.exe` reports it cannot open `lang\ChineseSimplified.isl`, the path is relative to the `.iss` file — confirm the file is at `installer/lang/ChineseSimplified.isl`.

- [ ] **Step 7: Confirm the satellite assemblies actually shipped**

```bash
ls dist/tray/ja dist/tray/zh-Hans
```

Expected: each holds `OpenInzone.Control.resources.dll` and `inzonetray.resources.dll`. If these directories are missing, the resx culture suffixes are wrong and every install would silently run in English.

- [ ] **Step 8: Commit**

```bash
git add installer
git commit -m "Let the installer pick the language, and tell the tray what it picked"
```

---

## Finishing

- [ ] Run the full suite one last time: `dotnet test` — expect 0 failures.
- [ ] Confirm the working tree is clean and the branch is `worktree-i18n`.
- [ ] Open the pull request. `main` is protected and rejects direct pushes.
- [ ] Label it `enhancement`. Without a label the release notes generator has nothing to file it under.
- [ ] Title it for the person reading the release notes, not for the diff — the title becomes that line verbatim. Something like *"Speak English and Chinese, not only Japanese"*.
- [ ] Say in the PR body that the English and Simplified Chinese have had no native review, and invite corrections by issue.
