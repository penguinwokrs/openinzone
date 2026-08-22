// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows;
using System.Windows.Forms;
using OpenInzone.Control;

namespace OpenInzone.Tray;

/// <summary>
/// The notification area icon. WinForms owns this because WPF has no equivalent; nothing else in
/// the application uses WinForms.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _disposed;

    public event EventHandler? LeftClicked;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("設定", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "OpenInzone",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) LeftClicked?.Invoke(this, EventArgs.Empty);
        };
    }

    private static Icon LoadIcon()
    {
        var stream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/openinzone.ico"))!.Stream;
        using (stream) return new Icon(stream);
    }

    /// <summary>The tooltip is the only status a tray icon can show without being opened.</summary>
    public void Update(DeviceState state)
    {
        // A state report marshalled onto the dispatcher can arrive after OnExit has torn the icon
        // down; NotifyIcon appears to tolerate that, but nothing promises it will.
        if (_disposed) return;

        // NotifyIcon.Text throws above 63 characters.
        string text = state.Connected
            ? $"{state.ModelName}\n音量 {state.Volume}\nバッテリー {state.Battery}"
            : "OpenInzone - 未接続";
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    /// <summary>The tray has no window to put a dialog in, so a balloon is the only unsolicited way to reach the user.</summary>
    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(10000, title, text, ToolTipIcon.Warning);

    public void Dispose()
    {
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
