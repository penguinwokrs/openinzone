# Stream Deck keys that only go up, or only go down — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add six Stream Deck actions whose direction is fixed and drawn on the key — volume up,
volume down, mic level up, mic level down, more game, more chat — leaving the existing five alone.

**Architecture:** A directed action is modelled as a pair: the action whose setting it moves
(`ActionIds.Subject`) and which way it moves it (`ActionIds.Direction`, +1 or −1). Everything that
is a fact about the setting keeps its existing switch and delegates through `Subject`. At rest a
directed key shows the picture in the manifest and the plugin draws nothing; a press sends the
step and shows the reading for 1.5 seconds, after which the key goes back to its picture.

**Tech Stack:** C# / .NET 8 (`~/.dotnet/dotnet`), xUnit, hand-written SVG, a Stream Deck manifest
and a plain-HTML Property Inspector.

**Spec:** `docs/superpowers/specs/2026-08-27-streamdeck-directed-step-actions-design.md`

## Global Constraints

- Every new `.cs` file starts with the two-line header the others carry:
  `// SPDX-License-Identifier: GPL-3.0-only` then `// Copyright (C) 2026 penguinwokrs`.
- The SDK is at `~/.dotnet/dotnet`; it is not on `PATH`. Build and test with the full path.
- Tests run from the repository root:
  `~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj`.
- `OpenInzone.StreamDeck` is trimmed and AOT-compatible: no reflection-based serialisation. Any
  new payload type must be added to the `StreamDeckJson` source-generated context.
- Action ids are one lower-case word after the prefix, as the existing five are.
- `git commit` messages carry **no** `Co-Authored-By` line (the user's global instruction).
- Comments in this codebase say *why*, in prose, and are worth writing. Match the surrounding
  density and voice — the code blocks below already do.
- Do not touch the tray, the daemon, the CLI or the hotkey. This is a plugin change.

---

### Task 1: The six actions exist, are declared, and read correctly

Adding an id to `ActionIds.All` immediately puts it under four existing tests (the manifest must
declare exactly these ids, every action must name a real feature, every dial must show `--` when
disconnected, and every dial must have a caption of its own). So the ids, the manifest, the
pictures and the captions land together. After this task the six actions appear in Stream Deck's
list and do nothing when pressed; Task 2 makes them act.

**Files:**
- Modify: `src/OpenInzone.StreamDeck/ActionIds.cs`
- Modify: `src/OpenInzone.StreamDeck/PluginHost.cs` (`Title` and `Feedback` only)
- Modify: `plugin/com.penguinwokrs.openinzone.sdPlugin/manifest.json`
- Create: `plugin/com.penguinwokrs.openinzone.sdPlugin/images/actions/volumeup.svg`
- Create: `plugin/com.penguinwokrs.openinzone.sdPlugin/images/actions/volumedown.svg`
- Create: `plugin/com.penguinwokrs.openinzone.sdPlugin/images/actions/miclevelup.svg`
- Create: `plugin/com.penguinwokrs.openinzone.sdPlugin/images/actions/micleveldown.svg`
- Create: `plugin/com.penguinwokrs.openinzone.sdPlugin/images/actions/balancegame.svg`
- Create: `plugin/com.penguinwokrs.openinzone.sdPlugin/images/actions/balancechat.svg`
- Create: the same six names under `plugin/com.penguinwokrs.openinzone.sdPlugin/images/keys/`
- Test: `tests/OpenInzone.Core.Tests/StreamDeck/ActionFeatureTests.cs`
- Test: `tests/OpenInzone.Core.Tests/StreamDeck/PluginHostTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `ActionIds.VolumeUp`, `.VolumeDown`, `.MicLevelUp`, `.MicLevelDown`, `.BalanceGame`,
    `.BalanceChat` — `const string`, all in `ActionIds.All`.
  - `static string ActionIds.Subject(string actionId)` — the id of the action whose setting this
    one moves; the id it was given when that action is not directed.
  - `static int ActionIds.Direction(string actionId)` — `1`, `-1`, or `0` for an undirected action.

- [ ] **Step 1: Write the failing tests**

Append to `tests/OpenInzone.Core.Tests/StreamDeck/ActionFeatureTests.cs`, inside the existing
`ActionFeatureTests` class (before its closing brace):

```csharp
    /// <summary>
    /// A directed action is the same setting with the direction settled. Gating it on anything but
    /// its subject's feature would give a model a key for a setting it does not have, or take away
    /// a key for one it does.
    /// </summary>
    [Fact]
    public void A_directed_action_is_gated_on_the_feature_of_the_setting_it_moves()
    {
        Assert.Equal(FeatureIds.Volume, ActionIds.Feature(ActionIds.VolumeUp));
        Assert.Equal(FeatureIds.Volume, ActionIds.Feature(ActionIds.VolumeDown));
        Assert.Equal(FeatureIds.MicLevel, ActionIds.Feature(ActionIds.MicLevelUp));
        Assert.Equal(FeatureIds.MicLevel, ActionIds.Feature(ActionIds.MicLevelDown));
        Assert.Equal(FeatureIds.Balance, ActionIds.Feature(ActionIds.BalanceGame));
        Assert.Equal(FeatureIds.Balance, ActionIds.Feature(ActionIds.BalanceChat));
    }

    [Fact]
    public void A_model_without_a_balance_takes_both_of_its_directed_keys_with_it()
    {
        var capabilities = new DeviceCapabilities([FeatureIds.Volume, FeatureIds.MicMute]);

        Assert.False(capabilities.Allows(ActionIds.Feature(ActionIds.BalanceGame)));
        Assert.False(capabilities.Allows(ActionIds.Feature(ActionIds.BalanceChat)));
        Assert.True(capabilities.Allows(ActionIds.Feature(ActionIds.VolumeUp)));
    }
```

Append to `tests/OpenInzone.Core.Tests/StreamDeck/PluginHostTests.cs`, inside the existing
`ActionIdTests` class (before its closing brace):

```csharp
    [Fact]
    public void A_directed_action_names_the_setting_it_moves_and_the_way_it_moves_it()
    {
        Assert.Equal(ActionIds.Volume, ActionIds.Subject(ActionIds.VolumeUp));
        Assert.Equal(ActionIds.Volume, ActionIds.Subject(ActionIds.VolumeDown));
        Assert.Equal(ActionIds.MicLevel, ActionIds.Subject(ActionIds.MicLevelUp));
        Assert.Equal(ActionIds.MicLevel, ActionIds.Subject(ActionIds.MicLevelDown));
        Assert.Equal(ActionIds.Balance, ActionIds.Subject(ActionIds.BalanceGame));
        Assert.Equal(ActionIds.Balance, ActionIds.Subject(ActionIds.BalanceChat));

        Assert.Equal(1, ActionIds.Direction(ActionIds.VolumeUp));
        Assert.Equal(-1, ActionIds.Direction(ActionIds.VolumeDown));
        Assert.Equal(1, ActionIds.Direction(ActionIds.MicLevelUp));
        Assert.Equal(-1, ActionIds.Direction(ActionIds.MicLevelDown));
    }

    /// <summary>
    /// Game is the low end of the scale, so the key that says GAME is the one that subtracts. It
    /// reads backwards to anyone who has not met the scale, which is why it is pinned here.
    /// </summary>
    [Fact]
    public void More_game_goes_down_the_scale_and_more_chat_goes_up_it()
    {
        Assert.Equal(-1, ActionIds.Direction(ActionIds.BalanceGame));
        Assert.Equal(1, ActionIds.Direction(ActionIds.BalanceChat));
    }

    /// <summary>
    /// An action that is not directed is its own subject and has no direction, which is what lets
    /// every lookup ask through <c>Subject</c> without the existing five gaining a case.
    /// </summary>
    [Fact]
    public void An_undirected_action_is_its_own_subject()
    {
        Assert.Equal(ActionIds.Volume, ActionIds.Subject(ActionIds.Volume));
        Assert.Equal(ActionIds.Battery, ActionIds.Subject(ActionIds.Battery));
        Assert.Equal(0, ActionIds.Direction(ActionIds.Volume));
        Assert.Equal(0, ActionIds.Direction(ActionIds.MicMute));

        const string later = "com.penguinwokrs.openinzone.something-later";
        Assert.Equal(later, ActionIds.Subject(later));
        Assert.Equal(0, ActionIds.Direction(later));
    }

    [Fact]
    public void A_directed_action_steps_by_the_same_default_as_the_setting_it_moves()
    {
        Assert.Equal(ActionIds.DefaultStep(ActionIds.Volume), ActionIds.DefaultStep(ActionIds.VolumeUp));
        Assert.Equal(ActionIds.DefaultStep(ActionIds.Balance), ActionIds.DefaultStep(ActionIds.BalanceGame));
        Assert.Equal(ActionIds.DefaultStep(ActionIds.MicLevel), ActionIds.DefaultStep(ActionIds.MicLevelDown));
    }
```

Append to `tests/OpenInzone.Core.Tests/StreamDeck/PluginHostTests.cs`, inside the existing
`FeedbackTests` class (before its closing brace):

```csharp
    /// <summary>
    /// A dial for volume up and a dial for volume are the same number. Inventing a second readout
    /// for it would be a second thing to keep agreeing with the headset.
    /// </summary>
    [Fact]
    public void A_directed_dial_reads_the_same_as_the_dial_for_the_setting_it_moves()
    {
        Assert.Equal(PluginHost.Feedback(ActionIds.Volume, Live).Value,
            PluginHost.Feedback(ActionIds.VolumeUp, Live).Value);
        Assert.Equal(PluginHost.Feedback(ActionIds.Volume, Live).Indicator!.Value,
            PluginHost.Feedback(ActionIds.VolumeDown, Live).Indicator!.Value);
        Assert.Equal(PluginHost.Feedback(ActionIds.Balance, Live).Value,
            PluginHost.Feedback(ActionIds.BalanceGame, Live).Value);
        Assert.Equal(PluginHost.Feedback(ActionIds.MicLevel, Live).Value,
            PluginHost.Feedback(ActionIds.MicLevelUp, Live).Value);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj
```

Expected: the build fails with `CS0117: 'ActionIds' does not contain a definition for 'VolumeUp'`
and the same for the other five ids, `Subject` and `Direction`.

- [ ] **Step 3: Add the ids and the two new functions**

In `src/OpenInzone.StreamDeck/ActionIds.cs`, after the existing `Battery` constant:

```csharp
    // The same three settings again, with the direction settled by the action rather than by the
    // sign of a step. A key that can only go one way says so on its face, and cannot be configured
    // into going the other.
    public const string VolumeUp = Prefix + ".volumeup";
    public const string VolumeDown = Prefix + ".volumedown";
    public const string MicLevelUp = Prefix + ".miclevelup";
    public const string MicLevelDown = Prefix + ".micleveldown";
    public const string BalanceGame = Prefix + ".balancegame";
    public const string BalanceChat = Prefix + ".balancechat";
```

Replace the `All` array with:

```csharp
    public static readonly string[] All =
    [
        Volume, Balance, MicMute, MicLevel, Battery,
        VolumeUp, VolumeDown, MicLevelUp, MicLevelDown, BalanceGame, BalanceChat,
    ];
```

Add, after `All`:

```csharp
    /// <summary>
    /// The setting a directed action moves — its own id for an action that is not directed.
    /// </summary>
    /// <remarks>
    /// A directed action is a pair: which setting, and which way. Everything that is a fact about
    /// the setting — the feature it needs, how far a step goes, what its dial reads — is answered
    /// through here, which is what keeps six new actions from becoming six new cases in every
    /// switch that already names Volume, Balance or MicLevel.
    ///
    /// An id this build does not know is its own subject, so a future action is gated and stepped
    /// as itself rather than as whichever case happened to be written last.
    /// </remarks>
    public static string Subject(string actionId) => actionId switch
    {
        VolumeUp or VolumeDown => Volume,
        MicLevelUp or MicLevelDown => MicLevel,
        BalanceGame or BalanceChat => Balance,
        _ => actionId,
    };

    /// <summary>
    /// Which way a directed action moves its setting: 1 up, -1 down, and 0 for an action that
    /// takes its direction from the sign of the step and from the way a dial is turned.
    /// </summary>
    /// <remarks>
    /// Game is the low end of the balance scale — raising the value makes chat louder — so the key
    /// that says GAME is the one that subtracts. That is the same fact <see cref="KeyFace.Lean"/>
    /// spells out rather than signs, and it reads backwards to anyone who has not met it.
    /// </remarks>
    public static int Direction(string actionId) => actionId switch
    {
        VolumeUp or MicLevelUp or BalanceChat => 1,
        VolumeDown or MicLevelDown or BalanceGame => -1,
        _ => 0,
    };
```

Change the first line of `Feature` and of `DefaultStep` from `actionId switch` to
`Subject(actionId) switch`. Their cases are untouched.

- [ ] **Step 4: Give each directed action a caption**

In `src/OpenInzone.StreamDeck/PluginHost.cs`, replace the `Title` method with:

```csharp
    /// <remarks>
    /// A directed dial is captioned by the action rather than by the setting: two dials for the
    /// same setting sitting side by side, both saying "Volume", would be unreadable. The sign is
    /// the shortest thing that separates them, and a dial's caption has room for little more.
    /// </remarks>
    private static string Title(string actionId) => actionId switch
    {
        ActionIds.Volume => "Volume",
        ActionIds.Balance => "Game / Chat",
        ActionIds.MicMute => "Microphone",
        ActionIds.MicLevel => "Mic level",
        ActionIds.Battery => "Battery",
        ActionIds.VolumeUp => "Volume +",
        ActionIds.VolumeDown => "Volume -",
        ActionIds.MicLevelUp => "Mic level +",
        ActionIds.MicLevelDown => "Mic level -",
        ActionIds.BalanceGame => "More game",
        ActionIds.BalanceChat => "More chat",
        _ => "OpenInzone",
    };
```

In the same file, in `Feedback`, change the line `return actionId switch` to:

```csharp
        return ActionIds.Subject(actionId) switch
```

Leave the guard above it, which already calls `Title(actionId)` and `ActionIds.Feature(actionId)`,
exactly as it is.

- [ ] **Step 5: Draw the twelve pictures**

Create each file with exactly this content. The `actions/` files are the 24 px icon Stream Deck
shows in its list; the `keys/` files are the same drawing on the key's own dark tile, which is
what a key wears until it is pressed.

`plugin/com.penguinwokrs.openinzone.sdPlugin/images/actions/volumeup.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">
  <path d="M2,9.5 L5.5,9.5 L10,5 L10,19 L5.5,14.5 L2,14.5 Z" fill="none" stroke="#e8eaed" stroke-width="1.8" stroke-linejoin="round"/>
  <path d="M18.5,6 L23,14 L14,14 Z" fill="#e8eaed"/>
</svg>
```

`actions/volumedown.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">
  <path d="M2,9.5 L5.5,9.5 L10,5 L10,19 L5.5,14.5 L2,14.5 Z" fill="none" stroke="#e8eaed" stroke-width="1.8" stroke-linejoin="round"/>
  <path d="M18.5,18 L14,10 L23,10 Z" fill="#e8eaed"/>
</svg>
```

`actions/miclevelup.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">
  <rect x="4" y="3" width="6" height="11" rx="3" fill="none" stroke="#e8eaed" stroke-width="1.8"/>
  <path d="M2,11 A5,5 0 0 0 12,11 M7,16 L7,21" fill="none" stroke="#e8eaed" stroke-width="1.8" stroke-linecap="round"/>
  <path d="M18.5,6 L23,14 L14,14 Z" fill="#e8eaed"/>
</svg>
```

`actions/micleveldown.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">
  <rect x="4" y="3" width="6" height="11" rx="3" fill="none" stroke="#e8eaed" stroke-width="1.8"/>
  <path d="M2,11 A5,5 0 0 0 12,11 M7,16 L7,21" fill="none" stroke="#e8eaed" stroke-width="1.8" stroke-linecap="round"/>
  <path d="M18.5,18 L14,10 L23,10 Z" fill="#e8eaed"/>
</svg>
```

`actions/balancegame.svg` — the arrow is on the side the marker moves to, and the marker is drawn
already there:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">
  <path d="M2,12 L7.5,7.8 L7.5,16.2 Z" fill="#e8eaed"/>
  <path d="M10,12 L22,12" fill="none" stroke="#e8eaed" stroke-width="1.8" stroke-linecap="round"/>
  <circle cx="13.5" cy="12" r="2.8" fill="#e8eaed"/>
</svg>
```

`actions/balancechat.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">
  <path d="M2,12 L14,12" fill="none" stroke="#e8eaed" stroke-width="1.8" stroke-linecap="round"/>
  <circle cx="10.5" cy="12" r="2.8" fill="#e8eaed"/>
  <path d="M22,12 L16.5,7.8 L16.5,16.2 Z" fill="#e8eaed"/>
</svg>
```

Each `keys/<name>.svg` is its `actions/<name>.svg` with `width="72" height="72"` instead of
`width="24" height="24"`, and with `<rect width="24" height="24" rx="3" fill="#17171b"/>` inserted
as the first child — the same relationship the existing five pairs already have. For example
`keys/volumeup.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="72" height="72">
  <rect width="24" height="24" rx="3" fill="#17171b"/>
  <path d="M2,9.5 L5.5,9.5 L10,5 L10,19 L5.5,14.5 L2,14.5 Z" fill="none" stroke="#e8eaed" stroke-width="1.8" stroke-linejoin="round"/>
  <path d="M18.5,6 L23,14 L14,14 Z" fill="#e8eaed"/>
</svg>
```

- [ ] **Step 6: Declare the six actions in the manifest**

In `plugin/com.penguinwokrs.openinzone.sdPlugin/manifest.json`, add these six objects to the end
of the `Actions` array (after the `battery` entry):

```json
    {
      "UUID": "com.penguinwokrs.openinzone.volumeup",
      "Name": "Volume up",
      "Icon": "images/actions/volumeup",
      "Tooltip": "Turns the headset's own volume up by the step you set. A key that only goes up.",
      "PropertyInspectorPath": "pi/step.html",
      "Controllers": ["Keypad", "Encoder"],
      "Encoder": {
        "layout": "$B1",
        "TriggerDescription": { "Push": "Volume up", "Rotate": "Volume" }
      },
      "States": [{ "Image": "images/keys/volumeup", "TitleAlignment": "middle" }]
    },
    {
      "UUID": "com.penguinwokrs.openinzone.volumedown",
      "Name": "Volume down",
      "Icon": "images/actions/volumedown",
      "Tooltip": "Turns the headset's own volume down by the step you set. A key that only goes down.",
      "PropertyInspectorPath": "pi/step.html",
      "Controllers": ["Keypad", "Encoder"],
      "Encoder": {
        "layout": "$B1",
        "TriggerDescription": { "Push": "Volume down", "Rotate": "Volume" }
      },
      "States": [{ "Image": "images/keys/volumedown", "TitleAlignment": "middle" }]
    },
    {
      "UUID": "com.penguinwokrs.openinzone.miclevelup",
      "Name": "Mic level up",
      "Icon": "images/actions/miclevelup",
      "Tooltip": "Raises the microphone's recording level by the step you set.",
      "PropertyInspectorPath": "pi/step.html",
      "Controllers": ["Keypad", "Encoder"],
      "Encoder": {
        "layout": "$B1",
        "TriggerDescription": { "Push": "Level up", "Rotate": "Level" }
      },
      "States": [{ "Image": "images/keys/miclevelup", "TitleAlignment": "middle" }]
    },
    {
      "UUID": "com.penguinwokrs.openinzone.micleveldown",
      "Name": "Mic level down",
      "Icon": "images/actions/micleveldown",
      "Tooltip": "Lowers the microphone's recording level by the step you set.",
      "PropertyInspectorPath": "pi/step.html",
      "Controllers": ["Keypad", "Encoder"],
      "Encoder": {
        "layout": "$B1",
        "TriggerDescription": { "Push": "Level down", "Rotate": "Level" }
      },
      "States": [{ "Image": "images/keys/micleveldown", "TitleAlignment": "middle" }]
    },
    {
      "UUID": "com.penguinwokrs.openinzone.balancegame",
      "Name": "More game",
      "Icon": "images/actions/balancegame",
      "Tooltip": "Moves the game/chat balance towards game by the step you set.",
      "PropertyInspectorPath": "pi/step.html",
      "Controllers": ["Keypad", "Encoder"],
      "Encoder": {
        "layout": "$B1",
        "TriggerDescription": { "Push": "More game", "Rotate": "Balance" }
      },
      "States": [{ "Image": "images/keys/balancegame", "TitleAlignment": "middle" }]
    },
    {
      "UUID": "com.penguinwokrs.openinzone.balancechat",
      "Name": "More chat",
      "Icon": "images/actions/balancechat",
      "Tooltip": "Moves the game/chat balance towards chat by the step you set.",
      "PropertyInspectorPath": "pi/step.html",
      "Controllers": ["Keypad", "Encoder"],
      "Encoder": {
        "layout": "$B1",
        "TriggerDescription": { "Push": "More chat", "Rotate": "Balance" }
      },
      "States": [{ "Image": "images/keys/balancechat", "TitleAlignment": "middle" }]
    }
```

Remember the comma after the `battery` entry's closing brace.

- [ ] **Step 7: Run the tests**

```bash
~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj
```

Expected: PASS, including `ManifestTests` (which now checks eleven actions, their images and their
settings panels) and `FeedbackTests.Every_action_has_a_name_of_its_own_on_a_dial`.

- [ ] **Step 8: Commit**

```bash
git add src/OpenInzone.StreamDeck/ActionIds.cs src/OpenInzone.StreamDeck/PluginHost.cs \
        plugin/com.penguinwokrs.openinzone.sdPlugin \
        tests/OpenInzone.Core.Tests/StreamDeck
git commit -m "Offer the three stepping settings as keys with a direction"
```

---

### Task 2: A directed key actually moves the setting

**Files:**
- Modify: `src/OpenInzone.StreamDeck/PluginHost.cs` (`Decide` only)
- Test: `tests/OpenInzone.Core.Tests/StreamDeck/DecideTests.cs`

**Interfaces:**
- Consumes: `ActionIds.Subject`, `ActionIds.Direction` from Task 1.
- Produces: no new signature. `PluginHost.Decide(string actionId, bool isEncoder, bool pressed,
  int ticks, int step, DeviceCapabilities? capabilities = null)` keeps its shape and starts
  answering for the six directed ids.

- [ ] **Step 1: Write the failing tests**

Append to `tests/OpenInzone.Core.Tests/StreamDeck/DecideTests.cs`, inside the `DecideTests` class:

```csharp
    [Fact]
    public void A_directed_key_moves_the_way_its_action_says()
    {
        Assert.Equal((IpcCommands.AdjustVolume, 2),
            PluginHost.Decide(ActionIds.VolumeUp, Key, Press, ticks: 0, step: 2));

        Assert.Equal((IpcCommands.AdjustVolume, -2),
            PluginHost.Decide(ActionIds.VolumeDown, Key, Press, ticks: 0, step: 2));

        Assert.Equal((IpcCommands.AdjustMicLevel, 5),
            PluginHost.Decide(ActionIds.MicLevelUp, Key, Press, ticks: 0, step: 5));

        Assert.Equal((IpcCommands.AdjustMicLevel, -5),
            PluginHost.Decide(ActionIds.MicLevelDown, Key, Press, ticks: 0, step: 5));
    }

    /// <summary>
    /// Game is the low end of the scale. A key labelled GAME that raised the value would be the
    /// exact mistake these actions exist to remove.
    /// </summary>
    [Fact]
    public void More_game_lowers_the_balance_and_more_chat_raises_it()
    {
        Assert.Equal((IpcCommands.AdjustBalance, -10),
            PluginHost.Decide(ActionIds.BalanceGame, Key, Press, ticks: 0, step: 10));

        Assert.Equal((IpcCommands.AdjustBalance, 10),
            PluginHost.Decide(ActionIds.BalanceChat, Key, Press, ticks: 0, step: 10));
    }

    /// <summary>
    /// The action owns the direction, so the sign in the settings panel has nothing left to say.
    /// Honouring it would put back the trap of a key that goes the way you did not label it.
    /// </summary>
    [Fact]
    public void A_negative_step_on_a_directed_key_changes_its_size_and_not_its_direction()
    {
        Assert.Equal((IpcCommands.AdjustVolume, -3),
            PluginHost.Decide(ActionIds.VolumeDown, Key, Press, ticks: 0, step: -3));

        Assert.Equal((IpcCommands.AdjustVolume, 3),
            PluginHost.Decide(ActionIds.VolumeUp, Key, Press, ticks: 0, step: -3));
    }

    /// <summary>
    /// The plain dials keep their press for a shortcut, which is why a press there is never a step.
    /// A directed action has nothing else its press could mean.
    /// </summary>
    [Fact]
    public void Pressing_a_directed_dial_steps_in_its_own_direction()
    {
        Assert.Equal((IpcCommands.AdjustVolume, 1),
            PluginHost.Decide(ActionIds.VolumeUp, Dial, Press, ticks: 0, step: 1));

        Assert.Equal((IpcCommands.AdjustVolume, -1),
            PluginHost.Decide(ActionIds.VolumeDown, Dial, Press, ticks: 0, step: 1));

        Assert.Equal((IpcCommands.AdjustBalance, -10),
            PluginHost.Decide(ActionIds.BalanceGame, Dial, Press, ticks: 0, step: 10));

        Assert.Equal((IpcCommands.AdjustMicLevel, 5),
            PluginHost.Decide(ActionIds.MicLevelUp, Dial, Press, ticks: 0, step: 5));
    }

    /// <summary>A dial that only went one way would not be a dial.</summary>
    [Fact]
    public void Turning_a_directed_dial_still_follows_the_way_it_was_turned()
    {
        Assert.Equal((IpcCommands.AdjustVolume, -2),
            PluginHost.Decide(ActionIds.VolumeUp, Dial, Turn, ticks: -2, step: 1));

        Assert.Equal((IpcCommands.AdjustVolume, 2),
            PluginHost.Decide(ActionIds.VolumeDown, Dial, Turn, ticks: 2, step: 1));
    }

    /// <summary>
    /// Centring and muting belong to the plain dials, where someone looking for them would look.
    /// A directed dial's press is its step, so it must not also be a shortcut.
    /// </summary>
    [Fact]
    public void A_directed_dial_has_no_shortcut_on_its_press()
    {
        Assert.Equal((IpcCommands.AdjustBalance, -10),
            PluginHost.Decide(ActionIds.BalanceGame, Dial, Press, ticks: 0, step: 10));

        Assert.Equal((IpcCommands.AdjustMicLevel, -5),
            PluginHost.Decide(ActionIds.MicLevelDown, Dial, Press, ticks: 0, step: 5));

        // ...while the plain ones still have theirs.
        Assert.Equal((IpcCommands.SetBalance, 50),
            PluginHost.Decide(ActionIds.Balance, Dial, Press, ticks: 0, step: 10));

        Assert.Equal((IpcCommands.ToggleMicMute, 0),
            PluginHost.Decide(ActionIds.MicLevel, Dial, Press, ticks: 0, step: 5));
    }

    [Fact]
    public void A_directed_key_for_a_setting_the_model_does_not_have_does_nothing()
    {
        var capabilities = new DeviceCapabilities([FeatureIds.Volume]);

        Assert.Null(PluginHost.Decide(
            ActionIds.BalanceGame, Key, Press, ticks: 0, step: 10, capabilities));

        Assert.Equal((IpcCommands.AdjustVolume, 1),
            PluginHost.Decide(ActionIds.VolumeUp, Key, Press, ticks: 0, step: 1, capabilities));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj --filter DecideTests
```

Expected: FAIL — the new cases report `Assert.Equal() Failure: Values differ` with `null` actual,
because `Decide` still matches on `actionId` and no case names the directed ids.

- [ ] **Step 3: Put the direction into the delta**

In `src/OpenInzone.StreamDeck/PluginHost.cs`, replace the body of `Decide` after the capability
guard with:

```csharp
        int direction = ActionIds.Direction(actionId);
        int size = Math.Abs(step);

        // A directed action's press is its step, on a key and on a dial alike: the direction is the
        // whole reason the action exists, so there is nothing else the press could mean. A turn
        // still follows the way it was turned - a dial that only went one way would not be a dial.
        int delta = direction != 0
            ? (pressed ? direction * size : ticks * size)
            : (pressed ? (isEncoder ? 0 : step) : ticks * size);

        return ActionIds.Subject(actionId) switch
        {
            ActionIds.MicMute => pressed ? (IpcCommands.ToggleMicMute, 0) : null,
            ActionIds.Battery => pressed ? (IpcCommands.Refresh, 0) : null,

            // A dial press is the obvious shortcut for each: centre the balance, mute the
            // microphone. Neither has a counterpart on a plain key, which steps instead - and
            // neither belongs to a directed dial, whose press is already spoken for.
            ActionIds.Balance when direction == 0 && pressed && isEncoder => (IpcCommands.SetBalance, MixCentre),
            ActionIds.MicLevel when direction == 0 && pressed && isEncoder => (IpcCommands.ToggleMicMute, 0),

            ActionIds.Volume when delta != 0 => (IpcCommands.AdjustVolume, delta),
            ActionIds.Balance when delta != 0 => (IpcCommands.AdjustBalance, delta),
            ActionIds.MicLevel when delta != 0 => (IpcCommands.AdjustMicLevel, delta),

            _ => null,
        };
```

Update the `<remarks>` above `Decide` by adding this paragraph after the existing first one:

```csharp
    /// A directed action settles the direction itself, so only the size of the step is its user's:
    /// a down key with a step of -3 still goes down, by three.
```

- [ ] **Step 4: Run the whole suite**

```bash
~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj
```

Expected: PASS. `DecideTests.Nothing_is_sent_that_the_tray_would_reject`, which sweeps every
action against every input, now covers the six as well.

- [ ] **Step 5: Commit**

```bash
git add src/OpenInzone.StreamDeck/PluginHost.cs tests/OpenInzone.Core.Tests/StreamDeck/DecideTests.cs
git commit -m "Move the setting the way the directed key says"
```

---

### Task 3: The face a directed key shows while it is answering

**Files:**
- Modify: `src/OpenInzone.StreamDeck/KeyFace.cs`
- Test: `tests/OpenInzone.Core.Tests/StreamDeck/KeyFaceTests.cs`

**Interfaces:**
- Consumes: `ActionIds.Subject`, `ActionIds.Direction`.
- Produces: `static string KeyFace.Stepped(string actionId, DeviceSnapshot state,
  DeviceCapabilities? capabilities = null)` — an SVG data URI, same form as `KeyFace.For`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/OpenInzone.Core.Tests/StreamDeck/KeyFaceTests.cs`, inside the `KeyFaceTests`
class. Note the second helper: `Stepped` needs its own unwrapper.

```csharp
    /// <summary>Undoes the data URI of the face a directed key wears while it is answering.</summary>
    private static string SteppedSvg(string actionId, DeviceSnapshot state)
    {
        string uri = KeyFace.Stepped(actionId, state);
        Assert.StartsWith("data:image/svg+xml;base64,", uri, StringComparison.Ordinal);
        return Encoding.UTF8.GetString(Convert.FromBase64String(uri["data:image/svg+xml;base64,".Length..]));
    }

    [Theory]
    [InlineData(ActionIds.VolumeUp)]
    [InlineData(ActionIds.VolumeDown)]
    [InlineData(ActionIds.MicLevelUp)]
    [InlineData(ActionIds.MicLevelDown)]
    [InlineData(ActionIds.BalanceGame)]
    [InlineData(ActionIds.BalanceChat)]
    public void Every_stepped_face_is_well_formed_xml(string actionId)
    {
        XDocument.Parse(SteppedSvg(actionId, Live));
        XDocument.Parse(SteppedSvg(actionId, DeviceSnapshot.Disconnected));
    }

    [Fact]
    public void A_pressed_volume_key_shows_the_reading_and_its_scale()
    {
        string svg = SteppedSvg(ActionIds.VolumeUp, Live);

        Assert.Contains(">16<", svg, StringComparison.Ordinal);
        Assert.Contains("/ 30", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pressed_microphone_level_key_shows_the_percentage()
    {
        string svg = SteppedSvg(ActionIds.MicLevelDown, Live);

        Assert.Contains(">75<", svg, StringComparison.Ordinal);
        Assert.Contains(">%<", svg, StringComparison.Ordinal);
    }

    /// <summary>
    /// The balance has no number anyone reads, so the pressed face says the same thing the plain
    /// balance key says rather than inventing a second way to put it.
    /// </summary>
    [Fact]
    public void A_pressed_balance_key_names_the_side_the_mix_leans_to()
    {
        Assert.Contains("GAME 1.0", SteppedSvg(ActionIds.BalanceGame, Live with { Balance = 40 }),
            StringComparison.Ordinal);
        Assert.Contains("CENTRE", SteppedSvg(ActionIds.BalanceChat, Live with { Balance = 50 }),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The arrow is the one thing that stays on the key from rest to reading, so a run of presses
    /// never leaves you unsure which of the pair you are holding down.
    /// </summary>
    [Fact]
    public void A_pressed_key_keeps_the_arrow_it_wears_at_rest()
    {
        Assert.Contains("class=\"up\"", SteppedSvg(ActionIds.VolumeUp, Live), StringComparison.Ordinal);
        Assert.Contains("class=\"down\"", SteppedSvg(ActionIds.VolumeDown, Live), StringComparison.Ordinal);
        Assert.Contains("class=\"left\"", SteppedSvg(ActionIds.BalanceGame, Live), StringComparison.Ordinal);
        Assert.Contains("class=\"right\"", SteppedSvg(ActionIds.BalanceChat, Live), StringComparison.Ordinal);
    }

    [Fact]
    public void A_pressed_key_on_a_headset_that_is_not_answering_shows_no_reading()
    {
        string svg = SteppedSvg(ActionIds.VolumeUp, DeviceSnapshot.Disconnected);

        Assert.Contains(">--<", svg, StringComparison.Ordinal);
        Assert.DoesNotContain(">0<", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pressed_key_for_a_setting_the_model_does_not_have_shows_no_reading()
    {
        var capabilities = new DeviceCapabilities([FeatureIds.Volume]);
        string uri = KeyFace.Stepped(ActionIds.BalanceGame, Live, capabilities);
        string svg = Encoding.UTF8.GetString(
            Convert.FromBase64String(uri["data:image/svg+xml;base64,".Length..]));

        Assert.Contains(">--<", svg, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj --filter KeyFaceTests
```

Expected: build failure, `CS0117: 'KeyFace' does not contain a definition for 'Stepped'`.

- [ ] **Step 3: Draw it**

In `src/OpenInzone.StreamDeck/KeyFace.cs`, add after the existing `For` method:

```csharp
    /// <summary>
    /// The face a directed key wears for the moment after it is pressed: the arrow it carries at
    /// rest, kept small and at the top, over the reading the press produced.
    /// </summary>
    /// <remarks>
    /// A directed key is a picture rather than a readout, so this is a confirmation and not a
    /// display - it says what the press did and then gets out of the way. The arrow stays because
    /// it is the only thing telling a pair of these apart, and a key that dropped it while being
    /// held down would leave you reading a number with no idea which way it was going.
    ///
    /// The reading itself is the one the plain key for the same setting shows, word for word.
    /// </remarks>
    public static string Stepped(
        string actionId, DeviceSnapshot state, DeviceCapabilities? capabilities = null)
    {
        if (!capabilities.Allows(ActionIds.Feature(actionId))) state = DeviceSnapshot.Disconnected;

        string subject = ActionIds.Subject(actionId);
        int direction = ActionIds.Direction(actionId);

        // The balance has no up: its key already draws GAME at the left and CHAT at the right, so
        // the arrow points the way the marker is about to move.
        string arrow = subject == ActionIds.Balance ? Sideways(direction) : Upright(direction);

        return subject switch
        {
            ActionIds.Volume => Arrowed(arrow, state.Connected ? $"{state.Volume}" : null,
                state.Connected ? $"/ {state.VolumeMax}" : null),

            ActionIds.MicLevel => Arrowed(arrow, Level(state), state.MicLevelAvailable ? "%" : null),

            ActionIds.Balance => state.Connected
                ? Frame($"""
                    {arrow}
                    <text x="72" y="112" fill="{Foreground}" font-size="30" text-anchor="middle">{Escape(Lean(state.Balance))}</text>
                    """)
                : Arrowed(arrow, null, null),

            _ => Arrowed(arrow, null, null),
        };
    }

    /// <summary>
    /// The class is on the arrow so a test can say which way it points without measuring a path.
    /// Stream Deck neither styles nor cares about it.
    /// </summary>
    private static string Upright(int direction) => direction >= 0
        ? $"""<path class="up" d="M72,22 L88,44 L56,44 Z" fill="{Accent}"/>"""
        : $"""<path class="down" d="M72,44 L56,22 L88,22 Z" fill="{Accent}"/>""";

    private static string Sideways(int direction) => direction >= 0
        ? $"""<path class="right" d="M92,33 L74,21 L74,45 Z" fill="{Accent}"/>"""
        : $"""<path class="left" d="M52,33 L70,21 L70,45 Z" fill="{Accent}"/>""";

    /// <summary>An arrow, a large reading, and a quieter unit after it. A null reading draws "--".</summary>
    private static string Arrowed(string arrow, string? value, string? unit)
    {
        string body = value ?? "--";
        string colour = value is null ? Dim : Foreground;
        string suffix = unit is null || value is null
            ? ""
            : $"""<text x="72" y="128" fill="{Dim}" font-size="18" text-anchor="middle">{Escape(unit)}</text>""";

        return Frame($"""
            {arrow}
            <text x="72" y="102" fill="{colour}" font-size="44" text-anchor="middle">{Escape(body)}</text>
            {suffix}
            """);
    }
```

- [ ] **Step 4: Run the whole suite**

```bash
~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/OpenInzone.StreamDeck/KeyFace.cs tests/OpenInzone.Core.Tests/StreamDeck/KeyFaceTests.cs
git commit -m "Draw the reading a directed key answers a press with"
```

---

### Task 4: The moment the reading is shown for

**Files:**
- Create: `src/OpenInzone.StreamDeck/KeyFlash.cs`
- Create: `tests/OpenInzone.Core.Tests/StreamDeck/KeyFlashTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal sealed class KeyFlash(TimeSpan duration, Action<string> redraw) : IDisposable`
  with `void Show(string context)`, `bool IsShowing(string context)`, `void Forget(string context)`
  and `void Dispose()`.

- [ ] **Step 1: Write the failing tests**

Create `tests/OpenInzone.Core.Tests/StreamDeck/KeyFlashTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
using OpenInzone.StreamDeck;

namespace OpenInzone.Tests.StreamDeck;

/// <summary>
/// How long a directed key shows what a press did. Timing is the whole of this class, so the tests
/// wait for the outcome rather than for a fixed sleep - a machine under load must fail this for a
/// real reason or not at all.
/// </summary>
public class KeyFlashTests
{
    private static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private sealed class Redraws
    {
        private readonly ConcurrentQueue<string> _contexts = new();
        public void Record(string context) => _contexts.Enqueue(context);
        public int Count => _contexts.Count;
        public IReadOnlyCollection<string> Contexts => _contexts;
    }

    [Fact]
    public void A_pressed_key_shows_at_once_and_is_redrawn_to_say_so()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(Moment, redraws.Record);

        flash.Show("key-1");

        Assert.True(flash.IsShowing("key-1"));
        Assert.Contains("key-1", redraws.Contexts);
    }

    [Fact]
    public void A_key_that_was_never_pressed_is_not_showing()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(Moment, redraws.Record);

        Assert.False(flash.IsShowing("key-1"));
        Assert.Equal(0, redraws.Count);
    }

    [Fact]
    public void The_moment_passes_and_the_key_is_redrawn_to_go_back_to_its_picture()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(Moment, redraws.Record);

        flash.Show("key-1");
        Assert.True(SpinWait.SpinUntil(() => !flash.IsShowing("key-1"), Patience));

        Assert.True(SpinWait.SpinUntil(() => redraws.Count >= 2, Patience));
    }

    /// <summary>
    /// Holding a key down must read as one continuous number rather than a flicker, so a second
    /// press extends the moment instead of starting a competing one.
    /// </summary>
    [Fact]
    public void Pressing_again_extends_the_moment_rather_than_starting_a_second_one()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(TimeSpan.FromMilliseconds(400), redraws.Record);

        flash.Show("key-1");
        Thread.Sleep(200);
        flash.Show("key-1");
        Thread.Sleep(300);

        // Without the extension the first moment would have ended by now.
        Assert.True(flash.IsShowing("key-1"));
    }

    [Fact]
    public void Two_keys_keep_their_own_moments()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(Patience, redraws.Record);

        flash.Show("key-1");

        Assert.True(flash.IsShowing("key-1"));
        Assert.False(flash.IsShowing("key-2"));
    }

    /// <summary>
    /// A key taken off the deck must not be drawn on afterwards: its context is gone, and the
    /// timer that would have fired is the only thing still holding it.
    /// </summary>
    [Fact]
    public void A_key_taken_off_the_deck_stops_showing_and_is_not_drawn_on_again()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(Moment, redraws.Record);

        flash.Show("key-1");
        int drawn = redraws.Count;
        flash.Forget("key-1");

        Assert.False(flash.IsShowing("key-1"));
        Thread.Sleep(300);
        Assert.Equal(drawn, redraws.Count);
    }

    [Fact]
    public void Disposing_forgets_every_key()
    {
        var redraws = new Redraws();
        var flash = new KeyFlash(Moment, redraws.Record);

        flash.Show("key-1");
        flash.Show("key-2");
        int drawn = redraws.Count;
        flash.Dispose();

        Assert.False(flash.IsShowing("key-1"));
        Assert.False(flash.IsShowing("key-2"));
        Thread.Sleep(300);
        Assert.Equal(drawn, redraws.Count);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj --filter KeyFlashTests
```

Expected: build failure, `CS0246: The type or namespace name 'KeyFlash' could not be found`.

- [ ] **Step 3: Write it**

Create `src/OpenInzone.StreamDeck/KeyFlash.cs`:

```csharp
// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.StreamDeck;

/// <summary>
/// Shows a reading on a key for a moment after it is pressed, then puts the key back.
/// </summary>
/// <remarks>
/// A directed key is a picture rather than a readout, so the number it answers a press with is a
/// confirmation: it says what the press did and then gets out of the way. Pressing again extends
/// the moment rather than starting a second one, so holding a key down reads as one continuous
/// number rather than a flicker.
///
/// The redraw is a callback rather than a deck, which is what lets the timing be checked without
/// one. A key taken off the deck is forgotten along with its timer - otherwise the timer would be
/// the last thing holding a context that no longer exists, and would draw on it once more.
/// </remarks>
internal sealed class KeyFlash(TimeSpan duration, Action<string> redraw) : IDisposable
{
    private readonly Dictionary<string, Timer> _showing = [];
    private readonly object _gate = new();

    public void Show(string context)
    {
        lock (_gate)
        {
            if (_showing.TryGetValue(context, out var running))
                running.Change(duration, Timeout.InfiniteTimeSpan);
            else
                _showing[context] = new Timer(Expire, context, duration, Timeout.InfiniteTimeSpan);
        }

        redraw(context);
    }

    public bool IsShowing(string context)
    {
        lock (_gate) return _showing.ContainsKey(context);
    }

    /// <summary>Drops a key without drawing on it, for one that has left the deck.</summary>
    public void Forget(string context)
    {
        Timer? timer;
        lock (_gate)
        {
            if (!_showing.Remove(context, out timer)) return;
        }

        timer.Dispose();
    }

    private void Expire(object? state)
    {
        string context = (string)state!;

        // Gone already means the key left the deck while the moment was running, and there is
        // nothing left to draw on.
        Timer? timer;
        lock (_gate)
        {
            if (!_showing.Remove(context, out timer)) return;
        }

        timer.Dispose();
        redraw(context);
    }

    public void Dispose()
    {
        Timer[] timers;
        lock (_gate)
        {
            timers = [.. _showing.Values];
            _showing.Clear();
        }

        foreach (var timer in timers) timer.Dispose();
    }
}
```

- [ ] **Step 4: Run the whole suite**

```bash
~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/OpenInzone.StreamDeck/KeyFlash.cs tests/OpenInzone.Core.Tests/StreamDeck/KeyFlashTests.cs
git commit -m "Hold a key's reading for a moment, then let it go"
```

---

### Task 5: Wire the key up — picture at rest, reading after a press

**Files:**
- Modify: `src/OpenInzone.StreamDeck/StreamDeckMessages.cs`
- Modify: `src/OpenInzone.StreamDeck/StreamDeckConnection.cs`
- Modify: `src/OpenInzone.StreamDeck/PluginHost.cs`
- Modify: `src/OpenInzone.StreamDeck/Program.cs:97-98`

**Interfaces:**
- Consumes: `KeyFlash` (Task 4), `KeyFace.Stepped` (Task 3), `ActionIds.Direction` (Task 1).
- Produces: `Task StreamDeckConnection.ClearImageAsync(string context)`.

- [ ] **Step 1: Let a key be put back to its own picture**

In `src/OpenInzone.StreamDeck/StreamDeckMessages.cs`, make the image nullable:

```csharp
/// <summary>
/// A null image is how Stream Deck is told to use the picture the manifest gives the state. The
/// serializer drops a null field, and an absent image is the only way to undo a setImage.
/// </summary>
internal sealed record ImagePayload(
    [property: JsonPropertyName("image")] string? Image,
    [property: JsonPropertyName("target")] int Target = 0);
```

In `src/OpenInzone.StreamDeck/StreamDeckConnection.cs`, after `SetImageAsync`:

```csharp
    /// <summary>Puts the key back to the picture the manifest gives it.</summary>
    public Task ClearImageAsync(string context) =>
        SendAsync(new ContextMessage<ImagePayload>("setImage", context, new ImagePayload(null)),
            StreamDeckJson.Default.ContextMessageImagePayload, CancellationToken.None);
```

- [ ] **Step 2: Give PluginHost a constructor it can build the flash in**

A field initialiser cannot reference an instance method, and the flash needs `Redraw`. Replace the
primary constructor of `PluginHost` with an explicit one, and rename the two parameters to fields.
In `src/OpenInzone.StreamDeck/PluginHost.cs`, change the class declaration and add the fields:

```csharp
internal sealed class PluginHost : IDisposable
{
    /// <summary>One key or dial the user has placed on a deck.</summary>
    private sealed record Instance(string ActionId, ActionSettings Settings, bool IsEncoder);

    /// <summary>
    /// How long a directed key shows what a press did. Long enough to read at a glance, short
    /// enough that a key you have finished with is a picture again before you look back at it.
    /// </summary>
    private static readonly TimeSpan Moment = TimeSpan.FromSeconds(1.5);

    private readonly StreamDeckConnection _deck;
    private readonly IpcClient _tray;
    private readonly KeyFlash _flash;

    public PluginHost(StreamDeckConnection deck, IpcClient tray)
    {
        _deck = deck;
        _tray = tray;
        _flash = new KeyFlash(Moment, Redraw);
    }
```

Then replace every remaining bare `deck.` with `_deck.` and every bare `tray.` with `_tray.` in
this file. There are four of the first (`EventReceived`, `ShowAlertAsync`, `SetFeedbackAsync`,
`SetImageAsync`) and six of the second (`SnapshotReceived`, `CapabilitiesReceived`,
`ConnectionChanged`, `Send` twice, and `IsConnected`).

- [ ] **Step 3: Show the reading on a press, and forget a key that leaves**

In `Act`, replace the last line:

```csharp
        if (decision is not null) tray.Send(decision.Value.Command, decision.Value.Value);
```

with:

```csharp
        if (decision is null) return;

        _tray.Send(decision.Value.Command, decision.Value.Value);

        // The moment outlives the round trip to the tray, so the snapshot that comes back redraws
        // the key with the value the headset actually settled on rather than the one this expected.
        // A dial has its own readout and needs no such answer.
        if (!instance.IsEncoder && ActionIds.Direction(instance.ActionId) != 0) _flash.Show(context);
```

In `Handle`, in the `willDisappear` case, add the second line:

```csharp
            case "willDisappear":
                _instances.TryRemove(context, out _);
                _flash.Forget(context);
                break;
```

Replace `Redraw` with:

```csharp
    private void Redraw(string context)
    {
        if (!_instances.TryGetValue(context, out var instance)) return;
        var state = _state;
        var capabilities = _capabilities;

        if (instance.IsEncoder)
        {
            _ = _deck.SetFeedbackAsync(context, Feedback(instance.ActionId, state, capabilities));
        }
        else if (ActionIds.Direction(instance.ActionId) == 0)
        {
            _ = _deck.SetImageAsync(context, KeyFace.For(instance.ActionId, state, capabilities));
        }
        else if (_flash.IsShowing(context))
        {
            _ = _deck.SetImageAsync(context, KeyFace.Stepped(instance.ActionId, state, capabilities));
        }
        else
        {
            // A directed key is the picture the manifest gives it, and drawing over that is
            // exactly what the user did not ask for. Clearing rather than never drawing is what
            // gets it back after a press.
            _ = _deck.ClearImageAsync(context);
        }
    }
```

Replace `Dispose`:

```csharp
    public void Dispose()
    {
        _flash.Dispose();
        _instances.Clear();
    }
```

- [ ] **Step 4: Keep the diagnostic dump honest**

`Program.cs` prints the size of each key's face for `--probe`. A directed key has no resting face,
so ask for the one it answers a press with. Replace lines 97-98:

```csharp
        foreach (string actionId in ActionIds.All)
        {
            // A directed key wears the manifest's picture at rest, so the face it has of its own
            // is the reading it answers a press with.
            string face = ActionIds.Direction(actionId) == 0
                ? KeyFace.For(actionId, state)
                : KeyFace.Stepped(actionId, state);

            Console.WriteLine($"  {actionId,-42} {face.Length} chars of SVG");
        }
```

- [ ] **Step 5: Build and run the whole suite**

```bash
~/.dotnet/dotnet build src/OpenInzone.StreamDeck/OpenInzone.StreamDeck.csproj
~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj
```

Expected: both succeed, with no warnings about `ImagePayload` or the JSON context.

- [ ] **Step 6: Commit**

```bash
git add src/OpenInzone.StreamDeck
git commit -m "Leave a directed key as its picture until it is pressed"
```

---

### Task 6: The settings panel says what this key does

**Files:**
- Modify: `plugin/com.penguinwokrs.openinzone.sdPlugin/pi/step.html`

**Interfaces:**
- Consumes: the action ids from Task 1, as strings in the page.
- Produces: nothing other code uses.

- [ ] **Step 1: Read what the page already does**

```bash
cat plugin/com.penguinwokrs.openinzone.sdPlugin/pi/step.html
```

The page is handed `actionInfo` in `connectElgatoStreamDeckSocket`, and already parses it to fill
the field. Its `action` property is the action's UUID.

- [ ] **Step 2: Make the hint follow the action**

Replace the `<p class="hint">…</p>` element with:

```html
    <p class="hint" id="hint">
      How far one press moves the value. A negative step makes a key that turns it down, so a
      pair of keys gives you up and down. A dial ignores the sign and takes its direction from
      the way it is turned.
    </p>
```

In the `<script>`, after `const field = document.getElementById("step");` add:

```javascript
      const hint = document.getElementById("hint");

      // What each directed key does, in its own words. The sign is the action's, so the sentence
      // about negative steps would be wrong here rather than merely unnecessary.
      const DIRECTED = {
        "com.penguinwokrs.openinzone.volumeup": "turns the volume up",
        "com.penguinwokrs.openinzone.volumedown": "turns the volume down",
        "com.penguinwokrs.openinzone.miclevelup": "raises the microphone level",
        "com.penguinwokrs.openinzone.micleveldown": "lowers the microphone level",
        "com.penguinwokrs.openinzone.balancegame": "moves the balance towards game",
        "com.penguinwokrs.openinzone.balancechat": "moves the balance towards chat",
      };
```

In `connectElgatoStreamDeckSocket`, before `socket = new WebSocket(...)`, add:

```javascript
        describe(safeParse(actionInfo));
```

and add this function next to `apply`:

```javascript
      // A directed key settles its own direction, so the field is a size and nothing else: the
      // minimum stops a sign being offered at all rather than accepted and then ignored.
      function describe(info) {
        const what = info && DIRECTED[info.action];
        if (!what) return;

        field.min = "1";
        hint.textContent =
          "How far one press moves the value. This key always " + what +
          ", whatever you put here. A dial takes its direction from the way it is turned.";
      }
```

- [ ] **Step 3: Check it by hand against the fake deck**

```bash
~/.dotnet/dotnet run --project plugin/FakeStreamDeck -- inspect com.penguinwokrs.openinzone.volumedown
```

Follow the instructions it prints to open the page in a browser. Expected: the hint reads "This
key always turns the volume down, whatever you put here", and typing a value reports a `setSettings`
with that number. Then run it with no action argument and confirm the original wording is back.

- [ ] **Step 4: Run the suite**

```bash
~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj
```

Expected: PASS. `ManifestTests.Every_property_inspector_the_manifest_points_at_is_there` covers
that all eleven actions still find this file.

- [ ] **Step 5: Commit**

```bash
git add plugin/com.penguinwokrs.openinzone.sdPlugin/pi/step.html
git commit -m "Tell the settings panel which way its key goes"
```

---

### Task 7: Drive it without a deck

`plugin/FakeStreamDeck` is how this plugin is exercised end to end: it launches the real
executable, speaks the same WebSocket protocol, and talks to the real daemon and headset. Nothing
else can show that a press draws a reading and that the key goes back to its picture afterwards.

**Files:**
- Modify: `plugin/FakeStreamDeck/Program.cs`

**Interfaces:**
- Consumes: the plugin's behaviour from Tasks 1-5.
- Produces: nothing other code uses.

- [ ] **Step 1: Read how the existing run is written**

```bash
sed -n 20,150p plugin/FakeStreamDeck/Program.cs
```

Note `Check(string, bool)`, `WillAppear`, `KeyDown`, `WillDisappear`, `SettleAsync`, `Find`,
`CurrentAsync` and `Patience`, and that the run puts the volume back to where it found it.

- [ ] **Step 2: Add the constants for the two keys**

Next to the existing `private const string Volume = "com.penguinwokrs.openinzone.volume";`:

```csharp
    private const string VolumeUp = "com.penguinwokrs.openinzone.volumeup";
    private const string VolumeDown = "com.penguinwokrs.openinzone.volumedown";
```

- [ ] **Step 3: Add the check**

`SettleAsync(patience)` waits for the next message, then 400 ms more, then hands over everything
that arrived. `Find` returns the *last* matching message, so a settle spanning the whole 1.5 s
moment ends on the message that put the key back. Add this method to the class:

```csharp
    /// <summary>
    /// A directed key: a picture until it is pressed, the reading for a moment after, and the
    /// picture again. The pair is exercised up then down, so the headset is left as it was found.
    /// </summary>
    private static async Task DirectedKeysAsync(FakeDeck deck, int start)
    {
        await deck.SendAsync(WillAppear(VolumeUp, "key-volumeup", encoder: false)).ConfigureAwait(false);
        var appeared = await deck.SettleAsync(Patience).ConfigureAwait(false);

        // A directed key wears the picture the manifest gives it, so appearing must leave it as
        // that picture: a setImage carrying no image, or none at all.
        var drawn = Find(appeared, "setImage", "key-volumeup");
        Check("a directed key appears as its own picture",
            drawn is null || !drawn.Value.GetProperty("payload").TryGetProperty("image", out _));

        await deck.SendAsync(KeyDown(VolumeUp, "key-volumeup")).ConfigureAwait(false);
        var pressed = await deck.SettleAsync(Patience).ConfigureAwait(false);

        var reading = Find(pressed, "setImage", "key-volumeup");
        Check("pressing a directed key shows a reading",
            reading is not null && reading.Value.GetProperty("payload").TryGetProperty("image", out _));

        // The moment is 1.5 s, and nothing else is sent in the meantime: the next message on this
        // context is the one that puts the key back.
        var settled = await deck.SettleAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        var cleared = Find(settled, "setImage", "key-volumeup");
        Check("the reading goes away and the picture comes back",
            cleared is not null && !cleared.Value.GetProperty("payload").TryGetProperty("image", out _));

        Check("a directed key moves the volume by one",
            await CurrentAsync(deck, "dial-volume").ConfigureAwait(false) == start + 1);

        await deck.SendAsync(WillAppear(VolumeDown, "key-volumedown", encoder: false)).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);
        await deck.SendAsync(KeyDown(VolumeDown, "key-volumedown")).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);

        Check("the other key of the pair puts it back",
            await CurrentAsync(deck, "dial-volume").ConfigureAwait(false) == start);

        await deck.SendAsync(WillDisappear(VolumeUp, "key-volumeup")).ConfigureAwait(false);
        await deck.SendAsync(WillDisappear(VolumeDown, "key-volumedown")).ConfigureAwait(false);
        await deck.SettleAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }
```

Call it from `RunAsync`, on the line before its closing `return start;`:

```csharp
        await DirectedKeysAsync(deck, start).ConfigureAwait(false);

        return start;
```

- [ ] **Step 4: Build the plugin and run the fake deck**

This needs the headset connected and paired, and it changes the volume by one notch and back.

```bash
~/.dotnet/dotnet build plugin/FakeStreamDeck/FakeStreamDeck.csproj
~/.dotnet/dotnet run --project plugin/FakeStreamDeck
```

Expected: every `Check` line prints as a pass, including the four new ones, and the closing lines
report the volume put back to where it started.

- [ ] **Step 5: Commit**

```bash
git add plugin/FakeStreamDeck/Program.cs
git commit -m "Drive a directed key through the fake deck"
```

---

### Task 8: Say so in both READMEs

The two READMEs are the user-facing documentation and must stay in the same shape as each other.

**Files:**
- Modify: `README.md` (the `### Actions` table, the paragraph after it, and the install line)
- Modify: `README.ja.md` (the `### アクション` table, the paragraph after it, and the install line)

**Interfaces:**
- Consumes: the behaviour from Tasks 1-6.
- Produces: nothing.

- [ ] **Step 1: Update the English table and paragraph**

In `README.md`, replace the `### Actions` table with:

```markdown
| Action | On a key | On a dial | Shows |
|---|---|---|---|
| Volume | Steps by the amount you set | Turn to adjust | `16 / 30` |
| Volume up | Raises it by the step | Turn to adjust, press to raise | `16 / 30` while you press |
| Volume down | Lowers it by the step | Turn to adjust, press to lower | `16 / 30` while you press |
| Game / chat balance | Steps | Turn to adjust, press to centre | `GAME 1.0`, `CENTRE`, `CHAT 2.0` |
| More game | Moves it towards game | Turn to adjust, press to move | `GAME 1.0` while you press |
| More chat | Moves it towards chat | Turn to adjust, press to move | `CHAT 2.0` while you press |
| Microphone mute | Toggles | Press to toggle | `MUTED` or `LIVE` |
| Microphone level | Steps | Turn to adjust, press to mute | `75 %` |
| Mic level up | Raises it by the step | Turn to adjust, press to raise | `75 %` while you press |
| Mic level down | Lowers it by the step | Turn to adjust, press to lower | `75 %` while you press |
| Battery | Press to re-read | Press to re-read | `L 97` and `R 94` |
```

Replace the paragraph beginning "Each stepping action has a **Step** setting." with:

```markdown
Each stepping action has a **Step** setting. Left blank, volume moves by 1 of the headset's 30
notches, the balance by one notch of the −5.0…+5.0 scale INZONE Hub uses, and the microphone level
by 5 %.

The plain Volume, balance and microphone level keys take their direction from the sign of the
step, so a negative step makes a key that turns the value down and a pair of them gives you up and
down. The six directed actions settle that themselves: the arrow is on the key, the sign in the
panel is ignored, and a **Volume down** key cannot be configured into turning the volume up. They
are pictures rather than readouts — pressing one shows the reading for a moment and then the key
goes back to its picture.
```

- [ ] **Step 2: Update the English install line**

Replace:

```markdown
Stream Deck asks once whether to install it, and the five actions appear under **OpenInzone**.
```

with:

```markdown
Stream Deck asks once whether to install it, and the eleven actions appear under **OpenInzone**.
```

Leave the screenshot and its alt text alone — the five keys it shows are still what those five
actions look like.

- [ ] **Step 3: Update the Japanese table and paragraph**

In `README.ja.md`, replace the `### アクション` table with:

```markdown
| アクション | キー | ダイヤル | 表示 |
|---|---|---|---|
| 音量 | 設定した幅で増減 | 回して調整 | `16 / 30` |
| 音量アップ | 設定した幅で上げる | 回して調整、押して上げる | 押している間 `16 / 30` |
| 音量ダウン | 設定した幅で下げる | 回して調整、押して下げる | 押している間 `16 / 30` |
| ゲーム / チャットバランス | 設定した幅で増減 | 回して調整、押して中央へ | `GAME 1.0` / `CENTRE` / `CHAT 2.0` |
| ゲーム寄りへ | 設定した幅でゲーム側へ | 回して調整、押して動かす | 押している間 `GAME 1.0` |
| チャット寄りへ | 設定した幅でチャット側へ | 回して調整、押して動かす | 押している間 `CHAT 2.0` |
| マイクミュート | 切り替え | 押して切り替え | `MUTED` / `LIVE` |
| マイクレベル | 設定した幅で増減 | 回して調整、押してミュート | `75 %` |
| マイクレベルアップ | 設定した幅で上げる | 回して調整、押して上げる | 押している間 `75 %` |
| マイクレベルダウン | 設定した幅で下げる | 回して調整、押して下げる | 押している間 `75 %` |
| バッテリー | 押して再読み取り | 押して再読み取り | `L 97` と `R 94` |
```

Replace the paragraph beginning "増減するアクションには **Step** 設定があります。" with:

```markdown
増減するアクションには **Step** 設定があります。空欄のままなら、音量はヘッドセット側 30 段階の 1 段、バランスは INZONE Hub と同じ −5.0〜+5.0 スケールの 1 目盛り、マイクレベルは 5 % ずつ動きます。

音量・バランス・マイクレベルの素のキーは step の符号で向きが決まります。負の値にすると下げるキーになるので、2 つ並べれば上げ下げが揃います。向きが決まっている 6 つのアクションはそれ自体が向きを持っているので、符号は無視されます。**音量ダウン**のキーが設定次第で音量を上げてしまうことはありません。これらは数値表示ではなく絵で、押すと現在値が少しの間だけ出て、そのあとまた絵に戻ります。
```

- [ ] **Step 4: Update the Japanese install line**

Replace:

```markdown
Stream Deck が取り込むかどうかを一度尋ね、**OpenInzone** の下に 5 つのアクションが現れます。
```

with:

```markdown
Stream Deck が取り込むかどうかを一度尋ね、**OpenInzone** の下に 11 個のアクションが現れます。
```

- [ ] **Step 5: Check the two files still line up**

```bash
grep -c "^| " README.md README.ja.md
diff <(grep -n "^### " README.md | sed 's/[0-9]*://') <(grep -n "^### " README.ja.md | sed 's/[0-9]*://') | head -20
```

Expected: the section headings correspond one to one in the same order (their text differs by
language, which is fine — what matters is that neither file gained or lost a section).

- [ ] **Step 6: Commit**

```bash
git add README.md README.ja.md
git commit -m "Document the keys that only go one way"
```

---

## Verification before the branch is finished

- [ ] `~/.dotnet/dotnet build OpenInzone.sln` — no new warnings.
- [ ] `~/.dotnet/dotnet test tests/OpenInzone.Core.Tests/OpenInzone.Core.Tests.csproj` — all pass.
- [ ] `./plugin/build.sh 0.1.0` — the plugin packs, and Elgato's validator raises nothing new.
- [ ] `~/.dotnet/dotnet run --project plugin/FakeStreamDeck` — every check passes and the volume is
      left where it was found.
- [ ] On the real Stream Deck application: the six new actions appear under **OpenInzone** with
      their pictures, a **Volume up** key shows its picture, shows `16 / 30` when pressed, and is a
      picture again a moment later.
