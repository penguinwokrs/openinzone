// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Control;

/// <summary>
/// Everything the interface draws, as one value. Snapshots are swapped wholesale rather than
/// mutated so the UI thread never reads a half-updated set of values.
/// </summary>
public readonly record struct DeviceState(
    bool Connected,
    string ModelName,
    MixBalance Balance,
    HeadphoneVolume Volume,
    MicVolume Mic,
    int MicLevel,
    bool MicLevelAvailable,
    BatteryInfo Battery)
{
    /// <summary>
    /// A default <see cref="BatteryInfo"/> is all zeroes, which reads back as a genuine nought per
    /// cent rather than as no reading at all. Parsing an empty payload gives the sentinel the
    /// firmware itself uses, so anything reading this state sees "not reporting" without having to
    /// check <see cref="Connected"/> first.
    /// </summary>
    public static DeviceState Disconnected { get; } =
        new(false, "", default, default, default, 0, false, BatteryInfo.Parse([]));

    /// <summary>
    /// Folds a notification from the headset into the snapshot. Events this does not model, and
    /// parameters shorter than their documented layout, leave the state alone: this runs on the
    /// reader thread, where throwing would take the connection down.
    /// </summary>
    public DeviceState Apply(EventId eventId, byte[] param) => eventId switch
    {
        EventId.GameChatMixBalance when param.Length >= 1 => this with { Balance = new MixBalance(param[0]) },
        EventId.HeadphoneVolume when param.Length >= 3 => this with { Volume = HeadphoneVolume.Parse(param) },
        EventId.MicVolume when param.Length >= 3 => this with { Mic = MicVolume.Parse(param) },
        EventId.BatteryInfo when param.Length >= 2 => this with { Battery = BatteryInfo.Parse(param) },
        _ => this,
    };
}
