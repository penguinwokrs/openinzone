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

        _controller = new DeviceController();
        _tray = new TrayIcon();

        _controller.StateChanged += (_, state) => Dispatcher.Invoke(() => _tray.Update(state));
        _tray.ExitRequested += (_, _) => Shutdown();

        _flyout = new FlyoutWindow(_controller);
        _tray.LeftClicked += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_flyout.IsVisible) _flyout.HideAndFlush();
            else _flyout.ShowNearTray();
        });

        _controller.Refresh();

        _config = HotkeyConfig.LoadOrCreate(HotkeyConfig.DefaultPath);
        _hotkeys = new HotkeyHost(_controller);
        SurfaceRejected(_hotkeys.Apply(_config));

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
        _tray?.Dispose();
        _controller?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
