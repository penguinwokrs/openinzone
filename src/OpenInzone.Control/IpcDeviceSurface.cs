// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;

namespace OpenInzone.Control;

/// <summary>
/// A headset held by the daemon, reached over the local channel.
/// </summary>
/// <remarks>
/// Commands are posted and not waited on: the answer to every one of them is the snapshot the
/// daemon pushes afterwards, which arrives whether the change came from here, from another client,
/// or from the earbuds themselves. A command sent while nothing is connected is dropped rather
/// than queued - by the time a daemon appears, the press that caused it is long past.
/// </remarks>
public sealed class IpcDeviceSurface : IHeadset, IDisposable
{
    private readonly IpcClient _client;
    private volatile DeviceSnapshot _state = DeviceSnapshot.Disconnected;

    public event EventHandler<DeviceSnapshot>? StateChanged;

    /// <summary>Raised when there is no daemon and none could be started.</summary>
    public event EventHandler<string>? Unavailable;

    /// <summary>
    /// Raised for the settings a window shows: once for each request, and again after every
    /// change, carrying what the headset says rather than what it was asked for.
    /// </summary>
    public event EventHandler<DeviceSettings>? SettingsReceived;

    public IpcDeviceSurface(IpcClient? client = null)
    {
        _client = client ?? new IpcClient(startDaemonIfMissing: true);

        _client.SnapshotReceived += (_, snapshot) =>
        {
            _state = snapshot;
            StateChanged?.Invoke(this, snapshot);
        };

        // A link that has gone leaves the last reading looking current, so it is replaced by
        // nothing at all until a daemon says otherwise.
        _client.ConnectionChanged += (_, connected) =>
        {
            if (connected) return;
            _state = DeviceSnapshot.Disconnected;
            StateChanged?.Invoke(this, _state);
        };

        _client.DaemonUnavailable += (_, message) => Unavailable?.Invoke(this, message);
        _client.SettingsReceived += (_, settings) => SettingsReceived?.Invoke(this, settings);
    }

    public DeviceSnapshot State => _state;

    public void Start() => _client.Start();

    public void Refresh() => _client.Send(IpcCommands.Refresh);

    public void AdjustBalance(int delta) => _client.Send(IpcCommands.AdjustBalance, delta);

    public void SetBalance(int value) => _client.Send(IpcCommands.SetBalance, value);

    public void AdjustVolume(int delta) => _client.Send(IpcCommands.AdjustVolume, delta);

    public void SetVolume(int value) => _client.Send(IpcCommands.SetVolume, value);

    public void ToggleMicMute() => _client.Send(IpcCommands.ToggleMicMute);

    public void AdjustMicLevel(int delta) => _client.Send(IpcCommands.AdjustMicLevel, delta);

    public void SetMicLevel(int value) => _client.Send(IpcCommands.SetMicLevel, value);

    // ---- the settings INZONE Hub also offers -------------------------------------------------
    // Nothing here returns anything: every one is answered by SettingsReceived, with the whole set
    // as the headset now reports it.

    public void RequestSettings() => _client.Send(IpcCommands.GetSettings);

    public void SetSidetone(int value) => _client.Send(IpcCommands.SetSidetone, value);

    public void SetAmbientMode(int mode) => _client.Send(IpcCommands.SetAmbientMode, mode);

    public void SetAmbientLevel(int level) => _client.Send(IpcCommands.SetAmbientLevel, level);

    public void SetVoiceFocus(bool on) => _client.Send(IpcCommands.SetVoiceFocus, on ? 1 : 0);

    public void SetAutoPowerOff(bool on) => _client.Send(IpcCommands.SetAutoPowerOff, on ? 1 : 0);

    public void SetVoiceGuidance(bool on) => _client.Send(IpcCommands.SetVoiceGuidance, on ? 1 : 0);

    public void SetVoiceGuidanceLanguage(int language) =>
        _client.Send(IpcCommands.SetVoiceGuidanceLanguage, language);

    public void SetBluetoothAutoSwitch(bool on) =>
        _client.Send(IpcCommands.SetBluetoothAutoSwitch, on ? 1 : 0);

    public void Dispose() => _client.Dispose();
}
