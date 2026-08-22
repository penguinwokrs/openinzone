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
        // NotifyIcon.Text throws above 63 characters.
        string text = state.Connected
            ? $"{state.ModelName}\n音量 {state.Volume}\nバッテリー {state.Battery}"
            : "OpenInzone - 未接続";
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
