# Stream Deck keys that only go up, or only go down — design

Date: 2026-08-27
Status: approved

## Goal

Add six Stream Deck actions whose direction is fixed and shown on the key: volume up, volume
down, microphone level up, microphone level down, more game, more chat. The existing five actions
are unchanged.

## What the user asked for

- Other plugins offer a dedicated "volume up" and a dedicated "volume down", each with its own
  picture. Two keys that look different are easier to place and easier to read than two keys that
  look identical and differ only in a setting.
- The step mechanism is worth keeping. How far one press moves the value stays configurable.
- The existing actions stay exactly as they are. This is an addition.
- The key itself is a picture, but pressing it should show the number — `13 / 30`, `20 %`.

## The problem with the step's sign

Today a down key is a Volume key with `step` set to `-1`. Three things follow from that, and all
three are why this design exists:

- The two keys draw the same face, so a pair of them is told apart only by where they sit.
- The sign lives in a settings panel. Nothing on the key says which way it goes.
- Getting it wrong is silent: a key you meant to turn the volume down turns it up.

A directed action has no sign to get wrong. The size of the step remains the user's, the direction
is the action's.

## Decisions

| Question | Decision | Why |
|---|---|---|
| Which settings get a pair | Volume, microphone level, and balance — the three that step | The reason for a directed key applies wherever a step has a sign. Leaving balance out would leave one setting where the old trap remains. |
| What the balance pair is called | **More game** and **More chat** | "Balance up" is only readable if you already know that up means chat. The side is named for the same reason `KeyFace.Lean` names it rather than signing it. |
| Action ids | `.volumeup`, `.volumedown`, `.miclevelup`, `.micleveldown`, `.balancegame`, `.balancechat` | One lower-case word, as the existing five are. Stream Deck matches events by this string, so it is fixed once and never rearranged. |
| How a directed action is modelled | As a pair: the action it moves, and +1 or −1 | The alternative is six new arms in every switch that mentions Volume, Balance or MicLevel — capability, default step, dial title, dial feedback, key face. With a subject and a direction, each of those keeps the arms it has and gains none. |
| What the key normally shows | The manifest's own image; the plugin draws nothing | This is what the user asked for and what every other plugin's up/down key does. It also means a key at rest costs no messages. |
| What a press shows | The reading, drawn from the tray's snapshot, for 1.5 s | Answers "did that do anything, and where am I now" without turning the key back into the readout it deliberately is not. Drawing from the snapshot rather than from the step means the number is the headset's, including when the value has hit the top and did not move. |
| Where the flash lives | A `KeyFlash` class holding contexts and timers, with a redraw callback | Keeps `PluginHost` free of timer bookkeeping, and makes "shows, then goes back" checkable without a deck. |
| Whether the flash is configurable | No | One number, chosen once. A setting for it would be a second thing to explain in the panel and a second thing to get wrong. |
| Whether they go on dials | Yes | Every action in this plugin can be placed on either, and a `ManifestTests` case says so. A dial with a directed action turns as any other dial does; its press steps in the action's direction. |
| What a dial press does | One step, in the action's direction | The plain Balance and Mic level dials use their press for a shortcut (centre, mute). A directed action's whole identity is its direction, so its press is the step. The shortcut stays available on the undirected action, which is where someone wanting it would look. |
| What a dial shows | The subject's reading and bar, under the action's own caption | A `Volume up` dial and a `Volume` dial control the same number, so inventing a second readout for it would be two things to keep agreeing for no gain. The caption is the exception: `FeedbackTests.Every_action_has_a_name_of_its_own_on_a_dial` requires the names to be distinct, and it is right to — two dials captioned `Volume` sitting side by side would be unreadable. The captions are `Volume +`, `Volume -`, `Mic level +`, `Mic level -`, `More game`, `More chat`. |
| Which settings panel | The existing `pi/step.html`, with wording that follows the action | The field is the same field. Only the sentence under it differs, and a second file would be the same file with one paragraph changed. |
| A negative step on a directed key | Its size is used, its sign ignored | The action owns the direction. Honouring a sign there would restore the trap this removes. |

## What changes

### `src/OpenInzone.StreamDeck/ActionIds.cs`

Six constants, added to `All`, and two new functions:

```csharp
/// <summary>
/// The setting a directed action moves — its own id for an action that is not directed.
/// </summary>
/// <remarks>
/// A directed action is a pair: which setting, and which way. Everything that is a fact about the
/// setting — the feature it needs, how far a step goes, what a dial calls it — is answered by the
/// subject, which is what keeps the six new actions from becoming six new arms in five switches.
/// </remarks>
public static string Subject(string actionId) => actionId switch
{
    VolumeUp or VolumeDown => Volume,
    MicLevelUp or MicLevelDown => MicLevel,
    BalanceGame or BalanceChat => Balance,
    _ => actionId,
};

/// <summary>
/// Which way a directed action moves its setting, or 0 for an action that takes its direction
/// from the sign of the step and the way a dial is turned.
/// </summary>
/// <remarks>
/// Game is the low end of the balance scale, so the key that says GAME is the one that subtracts.
/// This is the same fact <see cref="KeyFace.Lean"/> spells out, and it reads backwards to anyone
/// who has not met it.
/// </remarks>
public static int Direction(string actionId) => actionId switch
{
    VolumeUp or MicLevelUp or BalanceChat => 1,
    VolumeDown or MicLevelDown or BalanceGame => -1,
    _ => 0,
};
```

`Feature` and `DefaultStep` switch on `Subject(actionId)` instead of `actionId`. Their arms do not
change. Because `Subject` answers with the id it was given for anything it does not know, the
existing five and any future action behave as before.

### `src/OpenInzone.StreamDeck/PluginHost.cs`

`Decide` gains the direction in its delta, and the two dial shortcuts gain a guard:

```csharp
int direction = ActionIds.Direction(actionId);
int size = Math.Abs(step);

// A directed action's press is its step, on a key and on a dial alike: the direction is the
// reason the action exists, so there is nothing else for the press to mean. A turn still follows
// the way it was turned — a dial that only went one way would not be a dial.
int delta = direction != 0
    ? (pressed ? direction * size : ticks * size)
    : (pressed ? (isEncoder ? 0 : step) : ticks * size);

return ActionIds.Subject(actionId) switch
{
    ActionIds.MicMute => pressed ? (IpcCommands.ToggleMicMute, 0) : null,
    ActionIds.Battery => pressed ? (IpcCommands.Refresh, 0) : null,

    ActionIds.Balance when direction == 0 && pressed && isEncoder => (IpcCommands.SetBalance, MixCentre),
    ActionIds.MicLevel when direction == 0 && pressed && isEncoder => (IpcCommands.ToggleMicMute, 0),

    ActionIds.Volume when delta != 0 => (IpcCommands.AdjustVolume, delta),
    ActionIds.Balance when delta != 0 => (IpcCommands.AdjustBalance, delta),
    ActionIds.MicLevel when delta != 0 => (IpcCommands.AdjustMicLevel, delta),

    _ => null,
};
```

`Feedback` switches on `Subject(actionId)` rather than on `actionId`, so a directed dial reads
exactly as the dial for the setting it moves and gains no arm. `Title` keeps switching on the
action itself and gains six, which is the point: it is the one thing that tells two dials for the
same setting apart.

`Act` tells the flash when a key was pressed:

```csharp
if (decision is not null)
{
    tray.Send(decision.Value.Command, decision.Value.Value);
    if (!instance.IsEncoder && ActionIds.Direction(instance.ActionId) != 0) _flash.Show(context);
}
```

`Redraw` picks the face:

```csharp
if (instance.IsEncoder)
    _ = deck.SetFeedbackAsync(context, Feedback(instance.ActionId, state, capabilities));
else if (ActionIds.Direction(instance.ActionId) == 0)
    _ = deck.SetImageAsync(context, KeyFace.For(instance.ActionId, state, capabilities));
else if (_flash.IsShowing(context))
    _ = deck.SetImageAsync(context, KeyFace.Stepped(instance.ActionId, state, capabilities));
else
    _ = deck.ClearImageAsync(context);
```

Because the flash's window outlives the round trip to the tray, the snapshot that comes back
redraws the key with the value the headset actually settled on. `willDisappear` forgets the
context, and `Dispose` disposes the flash.

### `src/OpenInzone.StreamDeck/KeyFlash.cs` — new

```csharp
/// <summary>
/// Shows a reading on a key for a moment after it is pressed, then puts the key back.
/// </summary>
/// <remarks>
/// A directed key is a picture, not a readout, so the number is a confirmation rather than a
/// display: it says what the press did and then gets out of the way. Pressing again extends the
/// moment rather than starting a second one, so holding a key down reads as one continuous number
/// rather than a flicker.
///
/// The redraw is a callback rather than a deck so this can be checked without one.
/// </remarks>
internal sealed class KeyFlash(TimeSpan duration, Action<string> redraw) : IDisposable
```

with `Show(context)`, `IsShowing(context)`, `Forget(context)` and `Dispose`. One `Timer` per
context, re-armed by `Show` and disposed by `Forget`.

### `src/OpenInzone.StreamDeck/KeyFace.cs`

One new entry point, and nothing else touched:

```csharp
/// <summary>
/// The face a directed key wears while it is showing what a press did: the arrow it carries at
/// rest, kept small and at the top, over the reading the press produced.
/// </summary>
public static string Stepped(
    string actionId, DeviceSnapshot state, DeviceCapabilities? capabilities = null)
```

The arrow points up or down for volume and microphone level, and left or right for the balance
pair: the balance has no up, and the key already draws GAME at the left and CHAT at the right, so
the arrow points the way the marker will move. The body is the same text the existing key draws: `13` over `/ 30`, `20` over `%`,
`GAME 2.0` on its own. A headset that is not answering, or a model without the feature, draws
`--`, as everywhere else.

### `src/OpenInzone.StreamDeck/StreamDeckConnection.cs`

```csharp
/// <summary>
/// Puts the key back to the picture the manifest gives it. Stream Deck reads a setImage with no
/// image as "use the state's own", which is the only way to undo a setImage.
/// </summary>
public Task ClearImageAsync(string context) => ...
```

`ImagePayload.Image` becomes nullable so the serializer's `WhenWritingNull` drops the field.

### `plugin/com.penguinwokrs.openinzone.sdPlugin/manifest.json`

Six actions, each with `Controllers: [Keypad, Encoder]`, an `Encoder` layout of `$B1`, a
`TriggerDescription` naming the press, `PropertyInspectorPath: pi/step.html`, and its own images.

### `plugin/com.penguinwokrs.openinzone.sdPlugin/images/`

Twelve SVGs: `actions/` and `keys/` for each of the six. Each is the existing icon for its
setting — speaker, microphone, balance — with the arrow the action carries.

### `plugin/com.penguinwokrs.openinzone.sdPlugin/pi/step.html`

The page reads the action id out of `actionInfo`. For a directed action the hint becomes a
sentence about that key's own direction, and the field takes a minimum of 1; for the existing five
the page is exactly as it is now.

## Tests

**`DecideTests`** — an up key sends a positive delta and a down key a negative one; a step
configured negative on a down key still goes down; a dial press steps in the action's direction; a
turn follows the turn on a directed dial; `More game` subtracts and `More chat` adds; the centre
and mute shortcuts still belong to the undirected dials and not to the directed ones.

**`ActionFeatureTests`** — each directed action is gated by its subject's feature, so a model
without a microphone level offers neither the level nor its two keys.

**`KeyFlashTests`** (new) — a shown context reports as showing and redraws once; it stops showing
and redraws again when the moment passes; a second `Show` extends rather than doubles; a forgotten
context neither shows nor redraws afterwards.

**`KeyFaceTests`** — `Stepped` for each of the three subjects, and for a disconnected headset.

**`FeedbackTests`** — a directed dial reads the same value and bar as the dial for its subject,
and every dial still has a caption of its own. Both cases already exist and both cover the new
actions the moment they join `ActionIds.All`; neither is edited.

**`ManifestTests`** — unchanged. Every case it already makes is a case about the new actions too:
the manifest declares exactly what `ActionIds.All` holds, every image resolves, a settings panel
exists exactly where there is a step to configure, and every action goes on a key and on a dial.

## Documentation

`README.md` and `README.ja.md`, in the Stream Deck section, in the same shape in both: the actions
table gains the six, and the paragraph about the step's sign gains the sentence that a directed
key ignores it. The screenshot is not reshot — the five keys it shows are still what those five
actions look like.

## Out of scope

- The tray, the daemon, the CLI and the hotkey. Nothing outside the plugin changes.
- Directed actions for microphone mute or battery. Neither steps, so neither has a direction.
- Turning the existing five into anything else. They keep their sign, their faces and their ids.
