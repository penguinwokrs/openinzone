// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;
using OpenInzone.Model;
using OpenInzone.Protocol;
using OpenInzone.Settings;

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
    /// What one connection to a headset says: the settings it carries, and what it has at all.
    /// </summary>
    public sealed record DeviceReading(
        IReadOnlyList<SettingValue> Settings, DeviceCapabilities Capabilities);

    /// <summary>
    /// Panel features and the id whose slot in the map answers for them.
    /// </summary>
    /// <remarks>
    /// The battery is not here, and cannot be: a slot of nothing but 0xFF means "no such setting"
    /// only where 0xFF is not itself a reading. In the battery it is — it is the firmware's own
    /// "this part is not reporting", which a headset model's two bytes can both carry at once while
    /// nothing is docked. Reading that as a model with no battery would blank the panel's charge
    /// line and the deck's battery key for the whole connection. Every INZONE product runs on one,
    /// so it is offered unconditionally and the reading says the rest.
    /// </remarks>
    private static readonly (string Feature, EventId EventId)[] PanelFeatures =
    [
        (FeatureIds.Balance, EventId.GameChatMixBalance),
        (FeatureIds.Volume, EventId.HeadphoneVolume),
        (FeatureIds.MicMute, EventId.MicVolume),
    ];

    /// <summary>
    /// Reads what this model has and what each of its settings now says.
    /// </summary>
    /// <remarks>
    /// The headset's own capability map answers both at once: every slot holds that setting's
    /// parameter bytes, and a slot of nothing but 0xFF is the model saying it has no such setting.
    /// So where there used to be one exchange per setting, and 1.5 s of silence for each one the
    /// model does not have, there are three.
    ///
    /// Probing survives for what the map does not cover. 0x8E, the Bluetooth automatic connection
    /// switch, is in none of the three parts, and a model that answers none of them at all falls
    /// back to probing for everything - which is exactly what this did before the map was read.
    /// The difference is that a timeout is now the fallback rather than the answer.
    /// </remarks>
    public static DeviceReading Read(InzoneDevice device)
    {
        var map = device.ReadCapabilityMap();

        // One entry per packet, not per setting: the ambient packet carries three of them, and
        // asking for it once cannot see them change in between.
        var answers = SettingCatalogue.Events.ToDictionary(
            eventId => eventId, eventId => Answer(device, map, eventId));

        var settings = new List<SettingValue>();
        var features = new List<string>();

        foreach (var setting in SettingCatalogue.All)
        {
            if (answers[setting.EventId] is not { } param) continue;
            settings.Add(new SettingValue(setting.Id, setting.Read(param)));
            features.Add(setting.Id);
        }

        // The capture endpoint is not on the headset's wire at all, so whether there is one is a
        // question for Windows rather than for the map.
        features.AddRange(Features(map, device.Microphone is not null));

        return new DeviceReading(settings, new DeviceCapabilities(features));
    }

    /// <summary>
    /// The features that are not settings: the three the panel draws, the charge, and the
    /// microphone level.
    /// </summary>
    /// <remarks>
    /// Taken apart from the reading so that it can be checked without a headset, which is the only
    /// way to see what a model this project does not own would be offered.
    /// </remarks>
    public static IEnumerable<string> Features(CapabilityMap map, bool micLevelAvailable)
    {
        // Absent only when the headset said so. An id the map does not carry, or a map that could
        // not be read, leaves the control where it has always been: shown.
        foreach (var (feature, eventId) in PanelFeatures)
            if (map.Present(eventId) != false) yield return feature;

        yield return FeatureIds.Battery;

        if (micLevelAvailable) yield return FeatureIds.MicLevel;
    }

    /// <summary>
    /// Writes one setting, starting from what the headset currently reports so that a packet
    /// carrying three settings keeps the two it was not asked about.
    /// </summary>
    /// <remarks>
    /// Unless there is nothing to keep. A setting that is the whole packet is composed outright,
    /// which is what the methods this replaced did: reading first would be a round trip spent for
    /// nothing, and one more chance for a bad moment on the link to drop the headset on the way to
    /// ticking a checkbox.
    /// </remarks>
    public static void Write(InzoneDevice device, string id, int value)
    {
        var setting = SettingCatalogue.ById(id)
            ?? throw new InvalidOperationException($"No such setting: {id}.");

        byte[] current = setting.OwnsPacket ? [] : device.Session.Get(setting.EventId);
        device.Session.Set(setting.EventId, setting.Write(current, value));
    }

    /// <summary>
    /// The bytes a setting's packet holds, or null when this model does not have it. Taken from
    /// the map where the map carries it, and asked for otherwise.
    /// </summary>
    private static byte[]? Answer(InzoneDevice device, CapabilityMap map, EventId eventId)
    {
        if (map.Present(eventId) is bool present) return present ? map.Slot(eventId) : null;

        try
        {
            return device.Session.Get(eventId);
        }
        catch (TimeoutException)
        {
            // Only a timeout is swallowed, and only for an id the map did not answer for. Anything
            // else means the connection itself is in trouble, and the controller drops the device
            // and says so - a far better answer than a window quietly showing nothing at all.
            return null;
        }
    }
}
