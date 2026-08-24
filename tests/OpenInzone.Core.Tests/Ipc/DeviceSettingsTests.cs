// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;
using OpenInzone.Ipc;

namespace OpenInzone.Tests.Ipc;

/// <summary>
/// The settings message is what the settings window draws from. It used to be a record with a
/// nullable field per setting, where null meant "this model did not answer" and false meant "off",
/// and adding a setting changed the wire. It is a list now: a setting a model does not have is
/// simply not in it, which is the same distinction without the eight fields to keep in step.
/// </summary>
public class DeviceSettingsTests
{
    private static readonly IReadOnlyList<SettingValue> Sample =
    [
        new("sidetone", 3),
        new("ambient-mode", 2),
        new("ambient-level", 14),
        new("voice-focus", 1),
        new("auto-power-off", 1),
        new("voice-guidance", 0),
        new("voice-guidance-language", 2),
        new("bluetooth-auto-switch", 1),
    ];

    [Fact]
    public void The_settings_survive_the_wire_unchanged()
    {
        var message = new ServerMessage(ServerMessage.SettingsUpdate, IpcProtocol.Version, Settings: Sample);

        string json = JsonSerializer.Serialize(message, IpcJson.Default.ServerMessage);
        var back = JsonSerializer.Deserialize(json, IpcJson.Default.ServerMessage);

        Assert.Equal(Sample, back!.Settings);
    }

    /// <summary>
    /// Off and absent both read as "not on" to careless code, so this pins that they stay apart:
    /// a headset that answered off is shown as off, one that has no such setting is not shown.
    /// </summary>
    [Fact]
    public void A_setting_the_model_does_not_have_is_absent_rather_than_off()
    {
        IReadOnlyList<SettingValue> off = [new("voice-guidance", 0)];

        Assert.Equal(0, off.Value("voice-guidance"));
        Assert.Null(off.Value("auto-power-off"));
    }

    [Fact]
    public void Nothing_is_answered_for_before_anything_is_read()
    {
        IReadOnlyList<SettingValue> none = [];

        Assert.Null(none.Value("sidetone"));
        Assert.Null(((IReadOnlyList<SettingValue>?)null).Value("sidetone"));
    }

    /// <summary>
    /// A client told nothing offers everything, which is how this project behaved before it asked
    /// the headset at all. Hiding a control on no information would be worse than showing one the
    /// model turns out not to have.
    /// </summary>
    [Fact]
    public void A_client_that_has_not_been_told_what_a_model_has_offers_everything()
    {
        DeviceCapabilities? untold = null;

        Assert.True(untold.Allows(FeatureIds.Balance));
        Assert.True(untold.Allows(FeatureIds.Sidetone));
    }

    [Fact]
    public void A_client_that_has_been_told_offers_what_the_headset_reported()
    {
        var capabilities = new DeviceCapabilities([FeatureIds.Volume, FeatureIds.Sidetone]);

        Assert.True(capabilities.Allows(FeatureIds.Volume));
        Assert.False(capabilities.Allows(FeatureIds.Balance));
    }

    [Fact]
    public void The_capabilities_survive_the_wire_unchanged()
    {
        var capabilities = new DeviceCapabilities([FeatureIds.Balance, FeatureIds.AutoPowerOff]);
        var message = new ServerMessage(
            ServerMessage.CapabilitiesUpdate, IpcProtocol.Version, Capabilities: capabilities);

        string json = JsonSerializer.Serialize(message, IpcJson.Default.ServerMessage);
        var back = JsonSerializer.Deserialize(json, IpcJson.Default.ServerMessage);

        Assert.Equal(capabilities.Features, back!.Capabilities!.Features);
    }
}
