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

    /// <summary>
    /// The settings a window shows. A model that does not answer for one of them leaves it null
    /// rather than failing the lot: INZONE Buds has no wearing detection and no LED, and another
    /// model may not have ambient sound at all.
    /// </summary>
    public static DeviceSettings Settings(InzoneDevice device)
    {
        // Read once and used three times: the ambient packet carries mode, level and voice focus
        // together, so asking for it once is a third of the exchanges and cannot see them change
        // in between.
        var ambient = Ask(device.GetAmbientSetting);

        return new DeviceSettings(
            Sidetone: Ask(() => (int)device.GetSidetoneVolume().Value),
            AmbientMode: ambient is { } a ? (int)a.Mode : null,
            AmbientLevel: ambient is { } level ? level.Level : null,
            VoiceFocus: ambient?.VoiceFocus,
            AutoPowerOff: Ask(() => device.GetAutoPowerOff().IsOn),
            VoiceGuidance: Ask(() => device.GetVoiceGuidance().IsOn),
            VoiceGuidanceLanguage: Ask(() => (int)device.GetVoiceGuidanceLanguage()),
            BluetoothAutoSwitch: Ask(() => device.GetBluetoothAutoSwitch().IsOn));
    }

    /// <summary>
    /// A setting this model does not carry answers with a timeout, and one setting being absent
    /// must not cost the window the others. Only a timeout is swallowed: anything else means the
    /// connection itself is in trouble, and the controller drops the device and says so - which is
    /// a far better answer than a window quietly showing every setting as unsupported.
    /// </summary>
    private static T? Ask<T>(Func<T> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
