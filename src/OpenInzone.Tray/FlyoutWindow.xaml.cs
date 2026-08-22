// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows;
using System.Windows.Threading;
using OpenInzone.Control;
using OpenInzone.Model;

namespace OpenInzone.Tray;

public partial class FlyoutWindow : Window
{
    private readonly DeviceController _controller;

    /// <summary>
    /// A slider raises a change per pixel of travel. Sending each one would flood the HID channel,
    /// so writes are coalesced and the last value always goes out when the timer fires.
    /// </summary>
    private readonly DispatcherTimer _writeTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private Action? _pendingWrite;

    /// <summary>Set while the state is being copied into the controls, so echoes are not written back.</summary>
    private bool _updating;

    public FlyoutWindow(DeviceController controller)
    {
        InitializeComponent();
        _controller = controller;

        _writeTimer.Tick += (_, _) =>
        {
            _writeTimer.Stop();
            var write = _pendingWrite;
            _pendingWrite = null;
            write?.Invoke();
        };

        VolumeSlider.ValueChanged += (_, e) => Queue(() => _controller.SetVolume((int)e.NewValue));
        MicSlider.ValueChanged += (_, e) => Queue(() => _controller.SetMicLevel((int)e.NewValue));
        BalanceSlider.ValueChanged += (_, e) => Queue(() => _controller.SetBalance((int)e.NewValue));

        VolumeMuteButton.Click += (_, _) => _controller.ToggleVolumeMute();
        MicMuteButton.Click += (_, _) => _controller.ToggleMicMute();

        _controller.StateChanged += OnStateChanged;
        Deactivated += (_, _) => Hide();

        Render(_controller.State);
    }

    private void Queue(Action write)
    {
        if (_updating) return;
        _pendingWrite = write;
        _writeTimer.Stop();
        _writeTimer.Start();
    }

    /// <summary>Sends whatever is still pending, so closing never leaves the device behind the UI.</summary>
    private void Flush()
    {
        _writeTimer.Stop();
        var write = _pendingWrite;
        _pendingWrite = null;
        write?.Invoke();
    }

    private void OnStateChanged(object? sender, DeviceState state)
        => Dispatcher.Invoke(() => Render(state));

    private void Render(DeviceState state)
    {
        _updating = true;
        try
        {
            ModelText.Text = state.Connected ? state.ModelName : "未接続";

            VolumeRow.IsEnabled = state.Connected;
            VolumeSlider.Value = state.Volume.Value;
            VolumeText.Text = state.Connected ? $"{state.Volume.Value}/{HeadphoneVolume.Max}" : "--";
            VolumeMutedSlash.Visibility = state.Volume.Muted ? Visibility.Visible : Visibility.Collapsed;
            VolumeSlider.Opacity = state.Volume.Muted ? 0.4 : 1.0;

            // The level is the Windows capture endpoint; the mute flag is on the headset. Only the
            // slider goes away when Windows exposes no endpoint.
            MicRow.IsEnabled = state.Connected;
            MicSlider.IsEnabled = state.MicLevelAvailable;
            MicSlider.Value = state.MicLevel;
            MicText.Text = !state.Connected ? "--" : state.MicLevelAvailable ? $"{state.MicLevel}%" : "利用不可";
            MicMutedSlash.Visibility = state.Mic.Muted ? Visibility.Visible : Visibility.Collapsed;
            MicSlider.Opacity = state.Mic.Muted ? 0.4 : 1.0;

            BalanceRow.IsEnabled = state.Connected;
            BalanceSlider.Value = state.Balance.Value;
            BalanceText.Text = state.Connected ? state.Balance.ToString() : "--";

            BatteryText.Text = state.Connected ? Battery(state.Battery) : "--";
        }
        finally
        {
            _updating = false;
        }
    }

    private static string Battery(BatteryInfo battery)
    {
        static string Percent(byte value) => Unknown.Is(value) ? "--" : $"{value}%";
        return battery.HasSeparateBuds
            ? $"L {Percent(battery.LeftPercent)}   R {Percent(battery.RightPercent)}   ケース {Percent(battery.CasePercent)}"
            : Percent(battery.LeftPercent);
    }

    /// <summary>Places the panel at the corner the tray lives in, inside the working area.</summary>
    public void ShowNearTray()
    {
        _controller.Refresh();
        Show();
        // The height is only known once the layout has run.
        UpdateLayout();

        Rect work = GetTrayMonitorWorkArea();
        Left = work.Right - Width - 12;
        Top = work.Bottom - ActualHeight - 12;
        Activate();
    }

    /// <summary>
    /// SystemParameters.WorkArea is always the primary monitor, so on a multi-monitor setup with
    /// the taskbar elsewhere the panel would open on the wrong screen. Screen.FromPoint(cursor)
    /// finds the monitor the click actually happened on, but its WorkingArea is in physical device
    /// pixels while Window.Left/Top are device-independent units; TransformFromDevice is the
    /// per-monitor DPI matrix that converts one to the other correctly on a scaled display.
    /// </summary>
    private Rect GetTrayMonitorWorkArea()
    {
        var screenWork = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;

        // The source only exists once the window has a handle (i.e. after Show()); fall back to
        // the primary work area rather than throwing if it is somehow unavailable.
        var target = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (target is null) return SystemParameters.WorkArea;

        var transform = target.TransformFromDevice;
        var topLeft = transform.Transform(new System.Windows.Point(screenWork.Left, screenWork.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(screenWork.Right, screenWork.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    /// <summary>Hides the panel for the tray icon's toggle path, flushing first so a pending
    /// drag write is never dropped just because this happens not to also deactivate the window.</summary>
    public void HideAndFlush()
    {
        Flush();
        Hide();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        Flush();
        base.OnDeactivated(e);
    }

    /// <summary>Covers the shutdown path (App.OnExit calls Close() directly): the device must
    /// never end up disagreeing with the panel just because closing skipped deactivation.</summary>
    protected override void OnClosed(EventArgs e)
    {
        Flush();
        base.OnClosed(e);
    }
}
