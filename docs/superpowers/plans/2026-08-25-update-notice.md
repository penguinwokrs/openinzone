# A notice you can act on — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delay the startup update check by thirty seconds, and give the notice it raises somewhere
to go — a click opens the settings window on the update tab with the button ready to install.

**Architecture:** Three small changes that stay in their own lanes. `TrayIcon` learns to carry an
action alongside a balloon, because `NotifyIcon` raises one `BalloonTipClicked` for the icon and
never says which balloon it was. `SettingsWindow` learns to open showing an update someone else
already found. `App` gets one way to open the settings window instead of two, and waits before
asking GitHub anything.

**Tech Stack:** .NET 8, WPF (tray windows), Windows Forms (`NotifyIcon` only, and only inside
`TrayIcon`), xUnit.

## Global Constraints

- **Windows Forms types stay inside `TrayIcon`.** Its class comment says "nothing else in the
  application uses WinForms", and that stays true: no `ToolTipIcon` in any signature `App` calls.
- **No new user-visible strings.** Everything needed is already in the resources, in all three
  languages: `App_UpdateAvailableTitle`, `App_UpdateAvailableBody`, `Settings_UpdateAvailable`,
  `Settings_UpdateButtonInstall`.
- **The build must stay clean under `-warnaserror`**, which is what CI runs.
- Design: `docs/superpowers/specs/2026-08-25-update-notice-design.md`.

## A word about tests

Three of the four tasks change WinForms and WPF code, which is outside what this project's tests
reach and always has been — `DeviceState`, the protocol and the catalogue are testable because they
have no windows in them, and none of this does.

So Task 3 carries the one test that can exist: the settings markup is read off disk and checked for
the name the code reaches for, which is the failure this change could plausibly introduce later and
the only one a test could catch. Everything else is checked by hand, and Task 5 says exactly how.
Do not invent tests for the rest; a test that constructs no window and asserts a constant is worse
than none.

---

### Task 1: A balloon that knows what clicking it means

**Files:**
- Modify: `src/OpenInzone.Tray/TrayIcon.cs`

**Interfaces:**
- Produces: `TrayIcon.ShowNotice(string title, string text, Action onClick)` — information, clickable.
  `TrayIcon.ShowBalloon(string title, string text)` keeps its signature and its meaning: a warning
  that does nothing when clicked.

- [ ] **Step 1: Add the click plumbing and the two ways in**

Replace the existing `ShowBalloon` (currently at `src/OpenInzone.Tray/TrayIcon.cs:69-71`) with:

```csharp
    /// <summary>The tray has no window to put a dialog in, so a balloon is the only unsolicited way to reach the user.</summary>
    public void ShowBalloon(string title, string text) => Show(title, text, ToolTipIcon.Warning, null);

    /// <summary>
    /// Something worth knowing rather than worrying about, with somewhere to go when it is clicked.
    /// </summary>
    public void ShowNotice(string title, string text, Action onClick) =>
        Show(title, text, ToolTipIcon.Info, onClick);

    /// <summary>
    /// Raises a balloon and remembers what clicking it should do.
    /// </summary>
    /// <remarks>
    /// <see cref="NotifyIcon"/> raises one <c>BalloonTipClicked</c> for the icon and does not say
    /// which balloon was clicked - there is only ever one at a time - so the action is kept here
    /// until another balloon replaces it. It is deliberately not cleared when the balloon closes: a
    /// notification that has gone to the notification centre is still clickable an hour later, and
    /// that click should still work.
    ///
    /// The disposed guard is the one <see cref="Update"/> already carries, for the same reason and
    /// now a likelier one: a notice raised thirty seconds after startup can arrive after the icon
    /// has been torn down.
    /// </remarks>
    private void Show(string title, string text, ToolTipIcon icon, Action? onClick)
    {
        if (_disposed) return;

        _balloonAction = onClick;
        _icon.ShowBalloonTip(10000, title, text, icon);
    }
```

- [ ] **Step 2: Add the field and subscribe once**

Add beside `private bool _disposed;` at the top of the class:

```csharp
    private Action? _balloonAction;
```

And in the constructor, immediately after the existing `_icon.MouseClick += ...` block:

```csharp
        // Raised on this thread: the icon is built on the UI thread, so its messages are pumped by
        // the same dispatcher the action needs.
        _icon.BalloonTipClicked += (_, _) => _balloonAction?.Invoke();
```

- [ ] **Step 3: Build**

Run: `dotnet build OpenInzone.sln -warnaserror`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. Every existing `ShowBalloon` caller compiles
untouched, because its signature did not change.

- [ ] **Step 4: Commit**

```bash
git add src/OpenInzone.Tray/TrayIcon.cs
git commit -m "Let a balloon carry what clicking it means"
```

---

### Task 2: One way to open the settings window

**Files:**
- Modify: `src/OpenInzone.Tray/App.xaml.cs:77-134`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `private SettingsWindow? OpenSettings()` — returns the window now on screen, or null when
  there is nothing to open it with. Task 4 calls it.

- [ ] **Step 1: Extract the handler's body into a method**

The whole `_tray.SettingsRequested += (_, _) => Dispatcher.Invoke(() => { ... });` block becomes:

```csharp
        _tray.SettingsRequested += (_, _) => Dispatcher.Invoke(() => OpenSettings());
```

and its body moves, unchanged, into a new method placed after the constructor. Only the first two
lines and the last line differ from what was in the lambda — `return` where it fell off the end:

```csharp
    /// <summary>
    /// Shows the settings window, or brings the one already open to the front.
    /// </summary>
    /// <remarks>
    /// A method rather than the body of the tray menu's handler because there are two ways in now:
    /// the menu, and clicking the notice that an update is available. Two copies of this would have
    /// to agree about the one already being open, and would eventually not.
    /// </remarks>
    /// <returns>The window on screen, or null when the application is not far enough up to build one.</returns>
    private SettingsWindow? OpenSettings()
    {
        if (_settings is { IsVisible: true }) { _settings.Activate(); return _settings; }
        if (_hotkeys is null || _headset is null) return null;

        // ... the existing body, verbatim, from "// The window applies as it goes" through
        // "_settings.Show();" ...

        return _settings;
    }
```

Move the body verbatim. Do not reword the comments inside it — they explain ordering constraints
that are still exactly as they were.

- [ ] **Step 2: Build**

Run: `dotnet build OpenInzone.sln -warnaserror`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Check by hand that the menu still opens it**

Run the tray, right-click the icon, choose the settings item. The window opens. Choose it again
while the window is open: the same window comes forward rather than a second one appearing.

- [ ] **Step 4: Commit**

```bash
git add src/OpenInzone.Tray/App.xaml.cs
git commit -m "Give the settings window one way in, not two"
```

---

### Task 3: A settings window that can open on an update

**Files:**
- Modify: `src/OpenInzone.Tray/SettingsWindow.xaml:244`
- Modify: `src/OpenInzone.Tray/SettingsWindow.xaml.cs`
- Test: `tests/OpenInzone.Core.Tests/Control/SettingsMarkupTests.cs`

**Interfaces:**
- Produces: `SettingsWindow.ShowUpdate(UpdateInfo update)`. Task 4 calls it on what `OpenSettings`
  returned.

- [ ] **Step 1: Write the failing test**

Add to `tests/OpenInzone.Core.Tests/Control/SettingsMarkupTests.cs`, inside the existing class:

```csharp
    /// <summary>
    /// Clicking the notice that an update is available opens this window on the update tab, which
    /// the code does by name. A tab renamed or lost in the markup would leave that click opening
    /// the window on whatever tab happened to be first, and nothing at build time would say so.
    /// </summary>
    [Fact]
    public void The_update_tab_carries_the_name_the_code_selects_it_by()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenInzone.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var xaml = XDocument.Load(Path.Combine(
            directory.FullName, "src", "OpenInzone.Tray", "SettingsWindow.xaml"));

        var named = xaml.Descendants(Presentation + "TabItem")
            .Select(element => (string?)element.Attribute(Xaml + "Name"))
            .OfType<string>();

        Assert.Contains("UpdateTab", named);
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test OpenInzone.sln --filter 'FullyQualifiedName~The_update_tab_carries_the_name'`
Expected: FAIL — `Assert.Contains() Failure`, because no `TabItem` is named yet.

- [ ] **Step 3: Name the tab**

In `src/OpenInzone.Tray/SettingsWindow.xaml`, line 244:

```xml
    <TabItem x:Name="UpdateTab" Header="{x:Static res:Strings.Settings_Tab_Update}">
```

- [ ] **Step 4: Run it and watch it pass**

Run: `dotnet test OpenInzone.sln --filter 'FullyQualifiedName~The_update_tab_carries_the_name'`
Expected: PASS

- [ ] **Step 5: Add ShowUpdate**

In `src/OpenInzone.Tray/SettingsWindow.xaml.cs`, immediately above `OnUpdateClick`:

```csharp
    /// <summary>
    /// Opens on the update tab with an update someone else has already found.
    /// </summary>
    /// <remarks>
    /// The state a check made on this window would have left behind, set the same way: the button
    /// installs rather than checks, and the line under it names the version. Nothing is composed
    /// here that <see cref="CheckForUpdateAsync"/> does not compose, so the two cannot drift into
    /// showing different things about the same update.
    ///
    /// It does not start the download. Downloading because a notification was clicked would take
    /// the decision away from the person who clicked it.
    /// </remarks>
    public void ShowUpdate(UpdateInfo update)
    {
        if (!update.Available) return;

        _pendingUpdate = update;
        UpdateButton.Content = Strings.Settings_UpdateButtonInstall;
        UpdateStatusText.Text = string.Format(Strings.Settings_UpdateAvailable, update.Version);
        UpdateTab.IsSelected = true;
    }
```

- [ ] **Step 6: Build and run the whole suite**

Run: `dotnet build OpenInzone.sln -warnaserror && dotnet test OpenInzone.sln --no-build`
Expected: build clean, and every test passing with one more than before.

- [ ] **Step 7: Commit**

```bash
git add src/OpenInzone.Tray/SettingsWindow.xaml src/OpenInzone.Tray/SettingsWindow.xaml.cs \
        tests/OpenInzone.Core.Tests/Control/SettingsMarkupTests.cs
git commit -m "Let the settings window open on an update already found"
```

---

### Task 4: Wait, then say something worth clicking

**Files:**
- Modify: `src/OpenInzone.Tray/App.xaml.cs` — the field block, `CheckForUpdatesAtStartupAsync`, `OnExit`

**Interfaces:**
- Consumes: `TrayIcon.ShowNotice` (Task 1), `OpenSettings()` (Task 2), `SettingsWindow.ShowUpdate`
  (Task 3).

- [ ] **Step 1: Add the delay and the token**

Beside the other fields at the top of `App`:

```csharp
    /// <summary>
    /// How long to leave the login alone before asking GitHub anything. This runs while Windows is
    /// still logging the user in, which is the busiest minute the machine has, and nothing about an
    /// update is urgent enough to be part of it.
    /// </summary>
    private static readonly TimeSpan StartupCheckDelay = TimeSpan.FromSeconds(30);

    /// <summary>Ends the startup check when the application does, rather than after it.</summary>
    private readonly CancellationTokenSource _stopping = new();
```

- [ ] **Step 2: Rewrite the startup check**

Replace the body of `CheckForUpdatesAtStartupAsync` (its summary comment above it stays as it is):

```csharp
    private async Task CheckForUpdatesAtStartupAsync()
    {
        try
        {
            await Task.Delay(StartupCheckDelay, _stopping.Token).ConfigureAwait(false);

            var update = await UpdateChecker.CheckAsync(_stopping.Token).ConfigureAwait(false);
            if (!update.Available) return;

            // Discarded: this async method has nothing further to do once the notice is queued, so
            // there is nothing to await the dispatcher operation for.
            _ = Dispatcher.BeginInvoke(() => _tray?.ShowNotice(
                Strings.App_UpdateAvailableTitle,
                string.Format(Strings.App_UpdateAvailableBody, update.Version),
                () => OpenSettings()?.ShowUpdate(update)));
        }
        catch (Exception)
        {
            // No network, a rate limit, a malformed response, or the application closing before the
            // delay was up - none of it is worth interrupting a login over.
        }
    }
```

- [ ] **Step 3: Cancel it on the way out**

In `OnExit`, as the first two statements, before `_flyout?.Close();`:

```csharp
        // First: a check still waiting out its delay would otherwise raise a notice into an icon
        // this method is about to dispose.
        _stopping.Cancel();
```

and as the last statement before `base.OnExit(e);`:

```csharp
        _stopping.Dispose();
```

- [ ] **Step 4: Build and run the suite**

Run: `dotnet build OpenInzone.sln -warnaserror && dotnet test OpenInzone.sln --no-build`
Expected: build clean, every test passing.

- [ ] **Step 5: Commit**

```bash
git add src/OpenInzone.Tray/App.xaml.cs
git commit -m "Wait out the login, then say something worth clicking"
```

---

### Task 5: Check it against the real release

**Files:** none. This is the verification the tests cannot do.

The latest release is 0.4.0, so a build that believes it is 0.3.0 sees the real 0.4.0 as newer and
takes the whole route — the real request, the real release body, the real digest — rather than a
mock of it.

- [ ] **Step 1: Publish a build that thinks it is older**

```bash
dotnet publish src/OpenInzone.Tray -c Release -r win-x64 --self-contained true -p:Version=0.3.0 -o publish/tray
dotnet publish src/OpenInzone.Daemon -c Release -r win-x64 --self-contained true -p:Version=0.3.0 -o publish/tray
```

- [ ] **Step 2: Turn the startup check on**

In `%APPDATA%\openinzone\hotkeys.json`, set `"checkForUpdatesAtStartup": true`. Stop any tray that
is already running — the tray takes a single-instance mutex, so a second one exits at once — and
start `publish\tray\inzonetray.exe`.

- [ ] **Step 3: Watch for the four things**

1. Nothing happens for about thirty seconds. The notice then arrives, and carries the information
   icon rather than the warning one.
2. Clicking it opens the settings window **on the update tab**, with the button reading install and
   the line under it naming 0.4.0.
3. Clicking the notice again, with the window still open, brings that window forward. It does not
   open a second one.
4. A balloon that reports a failure is still inert. Give two commands the same hotkey in
   `hotkeys.json` to make one fail to register, restart, and click the balloon that says so:
   nothing should happen.

- [ ] **Step 4: Put it back**

Stop the published tray, restore `checkForUpdatesAtStartup` and any hotkeys changed for step 3, and
start the installed tray again.

- [ ] **Step 5: Push and open the pull request**

```bash
git push -u origin update/a-notice-you-can-act-on
gh pr create --label enhancement
```

The pull request's title is the line a reader of the release notes sees, so write it for them.
