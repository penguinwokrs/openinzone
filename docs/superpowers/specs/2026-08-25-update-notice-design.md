# A notice you can act on

The startup update check tells you there is an update and then leaves you to find it. This gives
the notice somewhere to go, and stops it running while Windows is still logging you in.

## The problem

| # | Problem |
|---|---|
| 1 | The check runs the moment the tray starts, which at login is the busiest minute the machine has |
| 2 | The balloon cannot be clicked. It says an update exists and gives you nothing to do about it |
| 3 | It arrives as a warning — `ToolTipIcon.Warning` — which is what a hotkey clash uses. An update is not a problem |

Nothing here is a bug in the sense of something going wrong. It is a notice that stops halfway.

## Decisions

| Question | Decision | Why |
|---|---|---|
| Which notification | The balloon this already shows | Windows 10 and 11 render `NotifyIcon` balloons through the toast system, so it is already a native notification in the notification centre. What is missing is the click, not the notification |
| Not the toast API | `ToastNotificationManager` needs, for an unpackaged application, a Start-menu shortcut carrying an AppUserModelID and a COM activator registered under a CLSID. The installer could write both; the zip could not — and "the zip installs nothing" is a promise this project makes |
| What clicking does | Opens the settings window on the update tab, with the button already saying install | The state a manual check leaves behind. Asking the person to press check again, having just been told, would be silly |
| What clicking does not do | Start the download | Downloading because a notification was clicked takes the decision away from the person who clicked it |
| Delay | 30 seconds | Long enough to be out of the way of the login, short enough that the answer still arrives while the machine is being sat down at |
| Configurable delay | No | Nobody has a reason to want a different number |

## How the click gets back

`NotifyIcon` raises one `BalloonTipClicked` for the icon, and it does not say which balloon was
clicked — there is only ever one at a time. So the balloon carries what clicking it means, kept in
a private method both public callers funnel into:

```csharp
void ShowBalloon(string title, string text)
void ShowNotice(string title, string text, Action onClick)
private void Show(string title, string text, ToolTipIcon icon, Action? onClick)
```

`ShowBalloon` is the existing plain path, with no action. `ShowNotice` is the new one, for a
balloon that has somewhere to go. `ToolTipIcon` — a `System.Windows.Forms` type this project does
not let outside `TrayIcon` — stays confined to `Show`.

The action is kept when the balloon is raised and replaced when the next one is raised. It is
**not** cleared when the balloon closes: a notification sitting in the notification centre is still
clickable an hour later, and that click should still work — right up until some other balloon is
raised.

That is a real limit, not something worth coding around: `NotifyIcon` has one balloon and one click
event, so it cannot remember more than one pending action. Every existing caller goes through
`ShowBalloon`, which passes none, so raising any balloon after the notice — a failed hotkey
registration, say — replaces the stored action with null and silently disarms the notice, even
while it is still sitting in the notification centre looking clickable.

`ShowBalloon` also gains the disposed guard `Update` already has. A notice raised thirty seconds
after startup can now arrive after the icon has been torn down.

## Opening the window where it is wanted

`SettingsWindow.ShowUpdate(UpdateInfo)` selects the update tab, fills in `_pendingUpdate`, and puts
the button and the status line into the state a check would have left them in. It composes nothing
of its own: the same fields, set the same way.

`App` currently builds the settings window inside the lambda that handles the tray menu. That moves
into `OpenSettings()`, which both the menu and the notice call. One way to open the window, rather
than two that have to agree.

## What is not here

- **No toast buttons.** That is the API this deliberately did not take, and it would be a separate
  piece of work — with a second path for people who installed from the zip.
- **No change to the on-demand check.** The settings window's own button is a person asking, and it
  reports failures. Only the startup path is silent, and it stays silent.

## How it is verified

Not by the test suite. The tray and the settings window are WinForms and WPF, which is outside what
these tests reach and always has been; nothing in this change has a decision worth extracting to
somewhere they could.

By hand on Windows instead, and there is a way to exercise the real path rather than a mocked one.
The latest release is 0.4.0, so a build made with `-p:Version=0.3.0` sees the real 0.4.0 as newer
and takes the whole route against the real GitHub release:

1. The notice arrives about thirty seconds in, not at once, and as information rather than a warning.
2. Clicking it opens the settings window on the update tab with the button saying install.
3. Clicking it again while the window is open brings that window forward rather than making another.
4. A failure balloon — an unregistered hotkey will do — is still inert when clicked.
