// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;
using OpenInzone.Model;
using OpenInzone.Protocol;

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

    /// <summary>
    /// The device's own answers, unparsed, for a client that speaks the protocol.
    /// </summary>
    /// <remarks>
    /// Every setting is asked for again rather than taken from the cached state: this exists so
    /// that a tool routed through the daemon prints exactly what it would have printed on its own
    /// connection, and a cache is the one thing that could make those differ.
    /// </remarks>
    public static DeviceDetail Detail(InzoneDevice device)
    {
        string Read(EventId eventId) => Convert.ToBase64String(device.Session.Get(eventId));

        return new DeviceDetail(
            Model: Read(EventId.ModelInfo),
            Battery: Read(EventId.BatteryInfo),
            Balance: Read(EventId.GameChatMixBalance),
            Volume: Read(EventId.HeadphoneVolume),
            Mic: Read(EventId.MicVolume),
            Sidetone: Read(EventId.SidetoneVolume),
            MicLevel: device.Microphone is not null ? device.GetMicLevel() : null);
    }
}
