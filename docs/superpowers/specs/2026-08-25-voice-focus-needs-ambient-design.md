# Voice focus is only offered while ambient sound is on — design

Date: 2026-08-25
Status: awaiting review

## Goal

The device tab shows voice focus as adjustable in every ambient mode. It only does anything while
ambient sound is selected. Grey it out elsewhere, the way the ambient level row already is.

## What the user asked for

- Voice focus should be editable only when ambient sound is the active mode.
- Everywhere else it should *look* inactive, not merely refuse to work. As it stands it reads as
  live and changeable at all times.

## The rule already exists, once

`SettingsWindow.ShowSettings` ends with:

```csharp
// The one thing no catalogue entry can say: the level belongs to ambient sound. The headset
// keeps it in every mode, but showing it as adjustable while it does nothing would be a lie.
AmbientLevelRow.IsEnabled = settings.Value(SettingCatalogue.AmbientMode) == 2;
```

Voice focus has no such line. The change is to make it obey the same rule — and the comment's
reasoning applies to it word for word.

## Decisions

| Question | Decision | Why |
|---|---|---|
| Where the mode's number lives | `SettingCatalogue`, as a named constant | `== 2` is a fact about the headset. Naming it in the core lets a test hold it against the `Tag="2"` the markup gives the ambient radio — two files that must agree and currently agree only by luck. |
| Which controls the rule names | The window, explicitly, as now | An earlier draft added a `NeedsAmbientSound(id)` predicate to the catalogue. Nothing would have called it: the window disables two named controls, and asking a predicate which two would mean routing enablement through `SettingBinding` — whose own remarks say "Nothing here knows what any particular setting means, which is the point". A function with no caller is worse than a second line. |
| What the box shows while disabled | The value the headset holds, greyed | Same as the ambient level row. It also tells you in advance what will take effect when you switch back to ambient sound. |
| Which modes disable it | Off and noise cancelling — anything but ambient sound | What the user asked for, and what the level row already does. |
| When it flips | After the headset confirms the mode change | The radio queues a write; the headset answers; `ShowSettings` runs again. The level row has always behaved this way and nobody has complained, so matching it beats inventing a second timing. |
| Markup | Untouched | No `x:Name` and no `tray:Setting.Id` changes, so the screenshot tools and `SettingsMarkupTests` are unaffected. |

## What changes

**`src/OpenInzone.Core/Settings/SettingCatalogue.cs`** gains the mode's name:

```csharp
/// <summary>
/// The ambient mode that the level and voice focus belong to — off is 0, noise cancelling 1. The
/// headset keeps both of those settings in every mode but only acts on them here, so it is also
/// the mode in which they are worth offering.
/// </summary>
public const int AmbientSoundMode = 2;
```

**`src/OpenInzone.Tray/SettingsWindow.xaml.cs`** replaces the single line with two, and the comment
stops being about the level alone:

```csharp
// What no catalogue entry can say on its own: these two belong to ambient sound. The headset
// keeps them in every mode, but showing them as adjustable while they do nothing would be a lie.
bool ambient = settings.Value(SettingCatalogue.AmbientMode) == SettingCatalogue.AmbientSoundMode;
AmbientLevelRow.IsEnabled = ambient;
VoiceFocusBox.IsEnabled = ambient;
```

**A test** pins `AmbientSoundMode` against the markup. `AmbientButton` in `SettingsWindow.xaml`
carries `Tag="2"`, and that tag is what the binding writes to the headset when the radio is picked.
The constant and the tag must be the same number or the window greys the wrong things out. The test
reads the tag out of the XAML as XML — the technique `SettingsMarkupTests` and
`VoiceGuidanceLanguageTests` already use to reach the markup without referencing the Windows-only
tray project — and asserts the two agree.

That pairing is worth pinning for the same reason `VoiceGuidanceLanguageTests` exists: a number in
markup and a number in code that must match, with nothing in the build to notice when they stop.

## Testing

The constant-versus-markup agreement gets a test, as above. **The two assignments that apply it do
not, and cannot from this suite:** `SettingsWindow` is in `OpenInzone.Tray`, which targets
`net8.0-windows`, and `tests/OpenInzone.Core.Tests` targets `net8.0` and must never reference it —
that is what keeps the suite runnable on WSL. The existing ambient level line has been untested for
the same reason since it was written, and this change does not fix that.

What it does fix is the number: it stops being a bare `2` sitting in a window with nothing checking
it against the markup that has to agree.

Checking it on screen is manual: `tools/ShowSettings` against a running daemon and a real headset,
switching ambient mode and watching the checkbox. The screenshot scripts cannot cover it — they
fill the window by hand and never run `ShowSettings`.

## Out of scope

Whether voice focus should also be *written* differently outside ambient sound. The headset accepts
and stores the byte in every mode, and this change does not alter what is sent — only what the
window offers.
