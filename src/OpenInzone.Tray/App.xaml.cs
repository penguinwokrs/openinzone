// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.IO;
using System.Windows;
using OpenInzone.Control;
using OpenInzone.Resources;

namespace OpenInzone.Tray;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    private IpcDeviceSurface? _headset;
    private TrayIcon? _tray;
    private FlyoutWindow? _flyout;
    private HotkeyHost? _hotkeys;
    private HotkeyConfig _config = HotkeyConfig.Default();
    private SettingsWindow? _settings;

    /// <summary>
    /// How long to leave the login alone before asking GitHub anything. This runs while Windows is
    /// still logging the user in, which is the busiest minute the machine has, and nothing about an
    /// update is urgent enough to be part of it.
    /// </summary>
    private static readonly TimeSpan StartupCheckDelay = TimeSpan.FromSeconds(30);

    /// <summary>Ends the startup check when the application does, rather than after it.</summary>
    private readonly CancellationTokenSource _stopping = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One process owns the hotkeys; a second copy would silently lose every registration.
        _instance = new Mutex(initiallyOwned: true, "OpenInzone.Tray.SingleInstance", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        // Before the tray icon, whose menu is built in its constructor, and before the flyout.
        // Anything constructed above this line keeps the culture it was built under; no later
        // assignment moves text that already exists.
        var culture = System.Globalization.CultureInfo.GetCultureInfo(
            UiLanguage.Resolve(ConfiguredLanguage(), AppContext.BaseDirectory));
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Globalization.CultureInfo.CurrentUICulture = culture;

        // The tray icon comes first because it owns the balloon, which is the only way anything
        // below can reach the user: nothing here has a window yet, and there is no console.
        _tray = new TrayIcon();
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Off the UI thread and unawaited: a successful update never gets to clean up its own
        // staging directory (see UpdateInstaller's class comment), so each startup sweeps what
        // earlier ones left behind. Must not delay the tray icon appearing, which is why this
        // starts before anything below has had a chance to block on it.
        _ = Task.Run(UpdateInstaller.SweepStaleStagingDirectories);

        // The headset is held by inzoned, which is started on demand and stops once the last
        // client goes. The tray is one client among several: the Stream Deck plugin and the CLI
        // reach the same daemon, which is what keeps two processes from talking over each other.
        _headset = new IpcDeviceSurface();
        _headset.Unavailable += (_, message) => Dispatcher.BeginInvoke(() =>
            _tray?.ShowBalloon(Strings.App_CannotConnectTitle, message));

        _headset.StateChanged += (_, state) => Dispatcher.BeginInvoke(() => _tray.Update(state));
        _tray.ExitRequested += (_, _) => Shutdown();

        _flyout = new FlyoutWindow(_headset);
        _tray.LeftClicked += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_flyout.IsVisible) _flyout.HideAndFlush();
            else _flyout.ShowNearTray();
        });

        _headset.Start();

        _config = LoadConfig();
        _hotkeys = new HotkeyHost(_headset);
        SurfaceRejected(_hotkeys.Apply(_config));

        if (_config.CheckForUpdatesAtStartup) _ = CheckForUpdatesAtStartupAsync();

        _tray.SettingsRequested += (_, _) => Dispatcher.Invoke(() => OpenSettings());
    }

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

        // The window applies as it goes and holds the hotkeys off only while it is waiting for
        // a key, so there is nothing to suspend or resume around its lifetime any more - and
        // nothing to do when it closes, because everything it was asked to do is already done.
        _settings = new SettingsWindow(_config, _hotkeys, _headset);
        _settings.Rejected += (_, rejected) => SurfaceRejected(rejected);
        _settings.RestartRequested += (_, _) =>
        {
            // Environment.ProcessPath is the executable as launched, which is what has to come
            // back.
            string? executable = Environment.ProcessPath;
            if (executable is null) return;

            // Closing the window is not just cleanup here - it has to happen before _hotkeys is
            // touched below. SettingsWindow keeps its own reference to the same HotkeyHost, and
            // Shutdown() further down closes any windows still open as part of tearing down; if
            // a hotkey capture is still in progress on the Hotkeys tab, that close runs
            // OnClosed -> EndCapture -> ApplyHotkeys, which reaches into _hotkeys. Doing it here
            // instead means that call lands while the host is still alive and behaves normally,
            // and it also means Shutdown() later has no open window left to close at all.
            _settings?.Close();

            // The single-instance mutex and the registered hotkeys are OS-level resources: the
            // mutex's named kernel object survives until this process's handle to it is
            // closed, and RegisterHotKey refuses a combination another window still holds,
            // this process's own message-only window included. Process.Start only waits for
            // the new process to exist, not for it to run any code, so starting it before
            // releasing these would race the new instance's own startup check - and losing
            // that race means the new copy sees itself as the second instance and exits
            // immediately, leaving no tray at all. Releasing them here first makes that
            // deterministic instead of a timing bet. This ordering constraint is layered on top
            // of the one above: the window must close before either resource is released, and
            // both must be released before the replacement process starts.
            _hotkeys?.Dispose();
            _hotkeys = null;
            _instance?.Dispose();
            _instance = null;

            // By this point the mutex and the hotkeys are already released, so this process is
            // no longer safely usable whether or not the launch succeeds: a caught failure
            // still has to end in Shutdown(), not a tray left running with neither guard.
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _tray?.ShowBalloon(Strings.App_ErrorTitle, ex.Message);
            }
            Shutdown();
        };
        _settings.Show();

        return _settings;
    }

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

    /// <summary>
    /// A hand-edited or half-written configuration must not cost the user their tray icon: the
    /// defaults stand in and the balloon says which file to go and fix.
    /// </summary>
    private HotkeyConfig LoadConfig()
    {
        try
        {
            return HotkeyConfig.LoadOrCreate(HotkeyConfig.DefaultPath);
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloon(Strings.App_ConfigUnreadableTitle,
                string.Format(Strings.App_ConfigUnreadableBody, HotkeyConfig.DefaultPath, ex.Message));
            return HotkeyConfig.Default();
        }
    }

    /// <summary>
    /// Off by default and, when on, silent on failure: nobody wants a warning at every login
    /// because their wifi was slow or GitHub rate-limited them. A check the user asked for from the
    /// settings window reports what went wrong instead - this path never does.
    /// </summary>
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
                // BalloonTipClicked already runs on this thread, so Dispatcher.Invoke is not here
                // for marshalling - it is here because Invoke is what feeds an exception into
                // DispatcherUnhandledException. Called directly, an exception building the settings
                // window - which has crashed and reached a release before - would unwind through
                // WinForms' own message loop instead, past that handler entirely, and end the
                // process with no icon and no balloon. SettingsRequested below goes through the
                // same Invoke for the same reason.
                () => Dispatcher.Invoke(() => OpenSettings()?.ShowUpdate(update))));
        }
        catch (Exception)
        {
            // No network, a rate limit, a malformed response, or the application closing before the
            // delay was up - none of it is worth interrupting a login over.
        }
    }

    /// <summary>
    /// Without this an exception on the UI thread ends the process with no icon, no balloon and no
    /// console to print to - the user sees the tray simply vanish.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        _tray?.ShowBalloon(Strings.App_ErrorTitle, e.Exception.Message);
    }

    /// <summary>
    /// Warns about any command whose key could not be registered, rather than leaving it as a
    /// binding that silently never fires. Shared by startup and by re-applying after the settings
    /// window closes.
    /// </summary>
    private void SurfaceRejected(IReadOnlyList<string> rejected)
    {
        if (rejected.Count == 0) return;

        var names = rejected.Select(id => HotkeyCommand.ById(id)?.DisplayName ?? id);
        _tray?.ShowBalloon(Strings.App_HotkeyFailedTitle,
            string.Format(Strings.App_HotkeyFailedBody, string.Join(Strings.App_ListSeparator, names)));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // First: a check still waiting out its delay would otherwise raise a notice into an icon
        // this method is about to dispose.
        _stopping.Cancel();
        _flyout?.Close();
        _hotkeys?.Dispose();
        // Before the tray icon: closing the channel reports a last state into the icon, and
        // taking the icon away first would leave that report with nowhere to land while the UI
        // thread is already blocked waiting for the reader to finish.
        _headset?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
