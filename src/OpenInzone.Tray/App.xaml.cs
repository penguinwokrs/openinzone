// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows;
using OpenInzone.Control;

namespace OpenInzone.Tray;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    private DeviceController? _controller;
    private TrayIcon? _tray;
    private FlyoutWindow? _flyout;
    private HotkeyHost? _hotkeys;
    private HotkeyConfig _config = HotkeyConfig.Default();
    private SettingsWindow? _settings;

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

        // The tray icon comes first because it owns the balloon, which is the only way anything
        // below can reach the user: nothing here has a window yet, and there is no console.
        _tray = new TrayIcon();
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _controller = new DeviceController();

        _controller.StateChanged += (_, state) => Dispatcher.BeginInvoke(() => _tray.Update(state));
        _tray.ExitRequested += (_, _) => Shutdown();

        _flyout = new FlyoutWindow(_controller);
        _tray.LeftClicked += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_flyout.IsVisible) _flyout.HideAndFlush();
            else _flyout.ShowNearTray();
        });

        _controller.Refresh();

        _config = LoadConfig(out ConfigOrigin origin);
        SettleAutostart(_config, origin);
        _hotkeys = new HotkeyHost(_controller);
        SurfaceRejected(_hotkeys.Apply(_config));

        if (_config.CheckForUpdatesAtStartup) _ = CheckForUpdatesAtStartupAsync();

        _tray.SettingsRequested += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_settings is { IsVisible: true }) { _settings.Activate(); return; }

            // Releases our own registrations for the window's lifetime, so a combination this
            // application already holds does not falsely probe as taken while the user is
            // re-confirming it or moving it to another command.
            _hotkeys?.Suspend();

            _settings = new SettingsWindow(_config);
            // Re-registering here is what makes a saved change take effect without a restart.
            _settings.Saved += (_, config) => SurfaceRejected(_hotkeys?.Apply(config) ?? []);
            // Runs whether the window was saved or dismissed with the X button: Resume puts back
            // whatever configuration was last applied, which after a save is the new one.
            _settings.Closed += (_, _) => SurfaceRejected(_hotkeys?.Resume() ?? []);
            _settings.Show();
        });
    }

    /// <summary>Where the configuration in hand came from, which is what autostart has to know.</summary>
    private enum ConfigOrigin
    {
        /// <summary>Read back from a file the user has saved at least once.</summary>
        Loaded,

        /// <summary>Written by this run, so nothing in it was chosen by anyone yet.</summary>
        Created,

        /// <summary>Stood in for a file that would not parse; it says nothing about the user.</summary>
        Unreadable,
    }

    /// <summary>
    /// A hand-edited or half-written configuration must not cost the user their tray icon: the
    /// defaults stand in and the balloon says which file to go and fix.
    /// </summary>
    private HotkeyConfig LoadConfig(out ConfigOrigin origin)
    {
        try
        {
            var config = HotkeyConfig.LoadOrCreate(HotkeyConfig.DefaultPath, out bool created);
            origin = created ? ConfigOrigin.Created : ConfigOrigin.Loaded;
            return config;
        }
        catch (Exception ex)
        {
            origin = ConfigOrigin.Unreadable;
            _tray?.ShowBalloon("設定ファイルを読み込めませんでした",
                $"既定のホットキーで起動しました。{HotkeyConfig.DefaultPath} を修正してください: {ex.Message}");
            return HotkeyConfig.Default();
        }
    }

    /// <summary>
    /// The Run key is what actually starts the application, so it stays the authority; the
    /// configuration's field is the user's expressed intent, and hand-editing it has to mean
    /// something. Whichever of the two was changed last is unknowable, so the file wins - it is
    /// the only one a person edits directly.
    ///
    /// That reasoning only holds for a file the user has actually saved. On the run that writes
    /// the file there is no intent in it yet - the installer set the Run key moments ago for the
    /// task the user ticked, and a freshly defaulted <see cref="HotkeyConfig.Autostart"/> would
    /// quietly undo it - so the registry is read and recorded instead. A file that would not parse
    /// says nothing either way, and leaves the Run key alone.
    /// </summary>
    private void SettleAutostart(HotkeyConfig config, ConfigOrigin origin)
    {
        try
        {
            switch (origin)
            {
                case ConfigOrigin.Created:
                    config.Autostart = Autostart.IsEnabled;
                    config.Save(HotkeyConfig.DefaultPath);
                    break;

                case ConfigOrigin.Loaded:
                    if (Autostart.IsEnabled != config.Autostart) Autostart.Set(config.Autostart);
                    break;

                case ConfigOrigin.Unreadable:
                    break;
            }
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloon("自動起動を設定できませんでした", ex.Message);
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
            var update = await UpdateChecker.CheckAsync().ConfigureAwait(false);
            if (!update.Available) return;

            // Discarded: this async method has nothing further to do once the balloon is queued, so
            // there is nothing to await the dispatcher operation for.
            _ = Dispatcher.BeginInvoke(() => _tray?.ShowBalloon("アップデートがあります",
                $"バージョン {update.Version} が利用可能です。設定から更新できます。"));
        }
        catch (Exception)
        {
            // No network, a rate limit, a malformed response - none of it is worth interrupting a
            // login over.
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
        _tray?.ShowBalloon("エラーが発生しました", e.Exception.Message);
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
        _tray?.ShowBalloon("ホットキーを登録できませんでした",
            $"他のアプリと競合しているため、次のショートカットは無効です: {string.Join("、", names)}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _flyout?.Close();
        _hotkeys?.Dispose();
        // Before the tray icon: Dispose waits for the worker, and the worker reports its last
        // state into the icon. Taking the icon away first would leave that report with nowhere
        // to land while the UI thread is already blocked waiting for the worker to finish.
        _controller?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
