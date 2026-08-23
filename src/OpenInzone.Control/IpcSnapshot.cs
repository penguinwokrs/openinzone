// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;
using OpenInzone.Model;

namespace OpenInzone.Control;

/// <summary>
/// Turns the application's own state into the shape that goes out over the local channel.
/// </summary>
/// <remarks>
/// The two types are kept apart on purpose. <see cref="DeviceState"/> answers to the panel and is
/// free to change with it; <see cref="DeviceSnapshot"/> is read by a separately-installed
/// executable that may be an older build. This is the one place that has to know both.
/// </remarks>
public static class IpcSnapshot
{
    public static DeviceSnapshot From(DeviceState state) => new(
        Connected: state.Connected,
        Model: state.ModelName,
        Volume: state.Volume.Value,
        VolumeMax: HeadphoneVolume.Max,
        VolumeMuted: state.Volume.Muted,
        Balance: state.Balance.Value,
        MicMuted: state.Mic.Muted,
        MicLevel: state.MicLevel,
        MicLevelAvailable: state.MicLevelAvailable,
        // Percent is already null for a part that is stowed, out of range or absent, which is
        // exactly the distinction a client needs to draw "--" rather than "0%".
        Battery: new BatterySnapshot(
            Left: state.Battery.Left.Percent,
            Right: state.Battery.Right.Percent,
            Case: state.Battery.Case.Percent,
            HasSeparateBuds: state.Battery.HasSeparateBuds));
}
