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
    public event EventHandler<IReadOnlyList<SettingValue>>? SettingsReceived;

    /// <summary>Raised when a headset says what it has, which is once per connection.</summary>
    public event EventHandler<DeviceCapabilities>? CapabilitiesReceived;

    /// <summary>
    /// What the connected model has, or null while nothing has said. Null is not "nothing": a
    /// client that has not been told offers everything, which is what
    /// <see cref="DeviceCapabilityExtensions.Allows"/> is for.
    /// </summary>
    public DeviceCapabilities? Capabilities { get; private set; }

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

        // Kept as well as raised: a window built after the hello has already gone past would
        // otherwise have to ask for something the channel has no request for.
        _client.CapabilitiesReceived += (_, capabilities) =>
        {
            Capabilities = capabilities;
            CapabilitiesReceived?.Invoke(this, capabilities);
        };
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
    // Neither of these returns anything: both are answered by SettingsReceived, with everything
    // the headset now says.

    public void RequestSettings() => _client.Send(IpcCommands.GetSettings);

    /// <summary>
    /// Writes one setting, named by the id it travels under. One method for every setting, where
    /// there used to be one each: what a setting is lives in the core's catalogue, and adding one
    /// no longer touches the channel.
    /// </summary>
    public void SetSetting(string id, int value) =>
        _client.Send(IpcCommands.SetSetting, value, id);

    public void Dispose() => _client.Dispose();
}
