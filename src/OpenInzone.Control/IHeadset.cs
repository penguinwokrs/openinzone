// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;

namespace OpenInzone.Control;

/// <summary>
/// What an interface needs from whatever is holding the headset.
/// </summary>
/// <remarks>
/// The tray used to own the device, so its panel talked to <see cref="DeviceController"/>
/// directly. Ownership now sits in the daemon, and the panel talks to this instead - which is the
/// same shape either way, and keeps the interface from caring which process the headset is in.
/// </remarks>
public interface IHeadset : IDeviceActions
{
    /// <summary>The last state received. Never null: a state nobody has reported is a disconnected one.</summary>
    DeviceSnapshot State { get; }

    /// <summary>Raised on a background thread whenever the state changes, from any source.</summary>
    event EventHandler<DeviceSnapshot>? StateChanged;

    /// <summary>
    /// What the connected model has, as the headset itself reported it, or null while nothing has
    /// said. Null is not "nothing": an interface told nothing offers everything.
    /// </summary>
    DeviceCapabilities? Capabilities { get; }

    /// <summary>Raised when a headset says what it has, which is once per connection.</summary>
    event EventHandler<DeviceCapabilities>? CapabilitiesReceived;

    /// <summary>Asks for everything to be read again.</summary>
    void Refresh();

    void SetVolume(int value);

    void SetMicLevel(int value);
}
