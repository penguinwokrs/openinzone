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

        _controller.Refresh();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _controller?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
