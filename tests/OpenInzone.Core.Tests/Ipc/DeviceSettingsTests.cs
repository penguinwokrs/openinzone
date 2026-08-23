// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;
using OpenInzone.Ipc;

namespace OpenInzone.Tests.Ipc;

/// <summary>
/// The settings message is what the settings window draws from, and every one of its fields is
/// nullable because a model that does not answer for a setting is not an error. These check that
/// the difference between "off" and "not answered for" survives the trip, since the window shows
/// the first and hides the second.
/// </summary>
public class DeviceSettingsTests
{
    private static readonly DeviceSettings Sample = new(
        Sidetone: 3, AmbientMode: 2, AmbientLevel: 14, VoiceFocus: true,
        AutoPowerOff: true, VoiceGuidance: false, VoiceGuidanceLanguage: 2,
        BluetoothAutoSwitch: true);

    [Fact]
    public void The_settings_survive_the_wire_unchanged()
    {
        var message = new ServerMessage(ServerMessage.SettingsUpdate, IpcProtocol.Version, Settings: Sample);

        string json = JsonSerializer.Serialize(message, IpcJson.Default.ServerMessage);
        var back = JsonSerializer.Deserialize(json, IpcJson.Default.ServerMessage);

        Assert.Equal(Sample, back!.Settings);
    }

    /// <summary>
    /// False and null both read as "not on" to careless code, so this pins that they stay apart:
    /// a headset that answered "off" is shown as off, one that did not answer is not shown at all.
    /// </summary>
    [Fact]
    public void An_unanswered_setting_stays_distinct_from_one_that_answered_off()
    {
        var off = new DeviceSettings(0, 0, 1, false, false, false, 0, false);

        string offJson = JsonSerializer.Serialize(off, IpcJson.Default.DeviceSettings);
        string noneJson = JsonSerializer.Serialize(DeviceSettings.None, IpcJson.Default.DeviceSettings);

        Assert.NotEqual(offJson, noneJson);
        Assert.Equal(off, JsonSerializer.Deserialize(offJson, IpcJson.Default.DeviceSettings));
        Assert.Equal(DeviceSettings.None,
            JsonSerializer.Deserialize(noneJson, IpcJson.Default.DeviceSettings));
    }

    [Fact]
    public void Nothing_is_answered_for_before_anything_is_read()
    {
        var none = DeviceSettings.None;

        Assert.Null(none.Sidetone);
        Assert.Null(none.AmbientMode);
        Assert.Null(none.AmbientLevel);
        Assert.Null(none.VoiceFocus);
        Assert.Null(none.AutoPowerOff);
        Assert.Null(none.VoiceGuidance);
        Assert.Null(none.VoiceGuidanceLanguage);
        Assert.Null(none.BluetoothAutoSwitch);
    }
}
