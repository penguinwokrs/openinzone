// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using OpenInzone.Control;
using OpenInzone.Ipc;
using OpenInzone.Tray.Native;

namespace OpenInzone.Tray;

public partial class FlyoutWindow : Window
{
    private readonly IHeadset _headset;

    /// <summary>
    /// A slider raises a change per pixel of travel. Sending each one would flood the HID channel,
    /// so writes are coalesced and the last value always goes out when the timer fires. Each slider
    /// keeps its own slot: one shared slot let a second slider moved inside the window discard the
    /// first one's write, and the user then watched their adjustment get undone on the next update.
    /// </summary>
    private readonly DispatcherTimer _writeTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Dictionary<string, Action> _pendingWrites = [];

    /// <summary>Set while the state is being copied into the controls, so echoes are not written back.</summary>
    private bool _updating;

    public FlyoutWindow(IHeadset headset)
    {
        InitializeComponent();
        _headset = headset;

        _writeTimer.Tick += (_, _) => Flush();

        VolumeSlider.ValueChanged += (_, e) => Queue("volume", () => _headset.SetVolume((int)e.NewValue));
        MicSlider.ValueChanged += (_, e) => Queue("mic", () => _headset.SetMicLevel((int)e.NewValue));
        BalanceSlider.ValueChanged += (_, e) => Queue("balance", () => _headset.SetBalance((int)e.NewValue));

        MicMuteButton.Click += (_, _) => _headset.ToggleMicMute();

        _headset.StateChanged += OnStateChanged;
        _headset.CapabilitiesReceived += (_, _) => Dispatcher.BeginInvoke(ShowWhatTheModelHas);
        Deactivated += (_, _) => Hide();

        ShowWhatTheModelHas();
        Render(_headset.State);
    }

    /// <summary>Replaces only that slider's pending write; a drag on one never cancels another's.</summary>
    private void Queue(string slider, Action write)
    {
        if (_updating) return;
        _pendingWrites[slider] = write;
        // One timer for all three: restarting it coalesces a burst of travel across sliders into a
        // single flush, which is the point of the delay in the first place.
        _writeTimer.Stop();
        _writeTimer.Start();
    }

    /// <summary>Sends whatever is still pending, so closing never leaves the device behind the UI.</summary>
    private void Flush()
    {
        _writeTimer.Stop();
        if (_pendingWrites.Count == 0) return;

        var writes = _pendingWrites.Values.ToArray();
        _pendingWrites.Clear();
        foreach (var write in writes) write();
    }

    private void OnStateChanged(object? sender, DeviceSnapshot state)
        => Dispatcher.BeginInvoke(() => Render(state));

    private void Render(DeviceSnapshot state)
    {
        _updating = true;
        try
        {
            ModelText.Text = state.Connected ? state.Model : "未接続";

            VolumeRow.IsEnabled = state.Connected;
            VolumeSlider.Value = state.Volume;
            VolumeText.Text = SnapshotText.Volume(state);

            // The level is the Windows capture endpoint; the mute flag is on the headset. Only the
            // slider goes away when Windows exposes no endpoint.
            MicRow.IsEnabled = state.Connected;
            MicSlider.IsEnabled = state.MicLevelAvailable;
            MicSlider.Value = state.MicLevel;
            MicText.Text = SnapshotText.MicLevel(state);
            MicMutedSlash.Visibility = state.MicMuted ? Visibility.Visible : Visibility.Collapsed;
            MicSlider.Opacity = state.MicMuted ? 0.4 : 1.0;

            BalanceRow.IsEnabled = state.Connected;
            BalanceSlider.Value = state.Balance;
            BalanceText.Text = SnapshotText.Balance(state);

            BatteryText.Text = SnapshotText.Battery(state);
        }
        finally
        {
            _updating = false;
        }
    }


    /// <summary>
    /// Leaves out what this model does not have.
    /// </summary>
    /// <remarks>
    /// The panel used to draw all three sliders whatever was plugged in, because there was no way
    /// to ask. There is now: the headset publishes what it carries, and a slider for something it
    /// does not have is one nobody should be given. A model that has not said anything yet keeps
    /// every control, which is what this did before it asked.
    /// </remarks>
    private void ShowWhatTheModelHas()
    {
        var capabilities = _headset.Capabilities;

        Show(VolumeRow, capabilities.Allows(FeatureIds.Volume));
        Show(BalanceRow, capabilities.Allows(FeatureIds.Balance));
        Show(BatteryText, capabilities.Allows(FeatureIds.Battery));

        // Two features share this row: the mute button is the headset's, the slider is the Windows
        // capture endpoint. A model with no mute must not take the slider away with it, so the row
        // goes only when neither is there.
        Show(MicRow, capabilities.Allows(FeatureIds.MicMute) || capabilities.Allows(FeatureIds.MicLevel));
        Show(MicMuteButton, capabilities.Allows(FeatureIds.MicMute));
    }

    private static void Show(UIElement element, bool present) =>
        element.Visibility = present ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Places the panel at the corner the tray lives in, inside the working area.</summary>
    public void ShowNearTray()
    {
        _headset.Refresh();
        Show();
        // The height is only known once the layout has run.
        UpdateLayout();

        PositionNearTray();
        Activate();

        // Moving the window onto a monitor with a different scale factor makes Windows send
        // WM_DPICHANGED, which WPF answers by resizing the panel - after the line above has
        // already anchored it against the pre-resize size. Running the placement again once
        // that has settled corrects it. Doing this unconditionally, rather than only on a
        // cross-monitor DPI change, keeps this free of DPI special-casing.
        Dispatcher.BeginInvoke(new Action(PositionNearTray), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Under this project's default Per-Monitor-V2 DPI awareness, a window's DPI transform - and
    /// so the meaning of Window.Left/Top - depends on which monitor it currently occupies. On
    /// first open the window is still wherever Windows placed it (typically the primary
    /// monitor), so converting the *target* monitor's work area with the *current* monitor's
    /// transform and assigning the result to Left/Top uses the wrong scale factor whenever the
    /// two monitors differ. Staying in physical pixels throughout and moving the window with
    /// SetWindowPos avoids that conversion entirely, so there is nothing left to get wrong.
    /// </summary>
    private void PositionNearTray()
    {
        // If the panel was hidden or closed before this deferred call ran, don't move it.
        if (!IsLoaded || Visibility != Visibility.Visible)
            return;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            // No handle yet; fall back to the previous DIP-based placement against the primary
            // monitor rather than throwing.
            System.Windows.Rect work = SystemParameters.WorkArea;
            Left = work.Right - Width - 12;
            Top = work.Bottom - ActualHeight - 12;
            return;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out OpenInzone.Tray.Native.Rect windowRect))
        {
            // GetWindowRect failed; fall back to the previous DIP-based placement against the primary
            // monitor rather than proceeding with a zeroed rectangle.
            System.Windows.Rect work = SystemParameters.WorkArea;
            Left = work.Right - Width - 12;
            Top = work.Bottom - ActualHeight - 12;
            return;
        }

        int width = windowRect.Right - windowRect.Left;
        int height = windowRect.Bottom - windowRect.Top;

        var screenWork = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;

        // Screen.WorkingArea is in physical pixels, so the margin needs to be scaled the same
        // way to look the same size on a scaled display. The window's own physical-to-DIP ratio
        // gives the target monitor's scale factor directly, with no separate DPI lookup needed.
        double scale = width / ActualWidth;
        int margin = (int)Math.Round(12 * scale);

        int x = screenWork.Right - width - margin;
        int y = screenWork.Bottom - height - margin;

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
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
