// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows;
using System.Windows.Forms;
using OpenInzone.Control;
using OpenInzone.Ipc;
using OpenInzone.Resources;

namespace OpenInzone.Tray;

/// <summary>
/// The notification area icon. WinForms owns this because WPF has no equivalent; nothing else in
/// the application uses WinForms.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _disposed;
    private Action? _balloonAction;

    public event EventHandler? LeftClicked;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(Strings.Tray_Settings, null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(Strings.Tray_Help, null, (_, _) => ProjectLinks.Open(ProjectLinks.Repository));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.Tray_Exit, null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

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

        // Raised on this thread: the icon is built on the UI thread, so its messages are pumped by
        // the same dispatcher the action needs.
        _icon.BalloonTipClicked += (_, _) => _balloonAction?.Invoke();
    }

    private static Icon LoadIcon()
    {
        var stream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/openinzone.ico"))!.Stream;
        using (stream) return new Icon(stream);
    }

    /// <summary>The tooltip is the only status a tray icon can show without being opened.</summary>
    public void Update(DeviceSnapshot state)
    {
        // A state report marshalled onto the dispatcher can arrive after OnExit has torn the icon
        // down; NotifyIcon appears to tolerate that, but nothing promises it will.
        if (_disposed) return;

        // NotifyIcon.Text throws above 63 characters.
        string text = state.Connected
            ? $"{state.Model}\n{Strings.Tray_TooltipVolume} {SnapshotText.VolumeWithMute(state)}\n" +
              $"{Strings.Tray_TooltipBattery} {SnapshotText.Battery(state)}"
            : Strings.Tray_NotConnected;
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

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

    public void Dispose()
    {
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
