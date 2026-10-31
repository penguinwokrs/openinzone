// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Model;
using OpenInzone.Settings;

namespace OpenInzone.Tests.Settings;

/// <summary>
/// A toggle is only offered when its reading is one of its two values. An INZONE H9 answers <c>05</c> for
/// auto power off; drawing that as "off" would invite a click that writes bytes nobody has checked there.
/// </summary>
public class SettingRecognitionTests
{
    [Theory]
    [InlineData(0x00, true)]
    [InlineData(0x0F, true)]
    [InlineData(0x05, false)]
    [InlineData(0x01, false)]
    [InlineData(0xFF, false)]
    public void Auto_power_off_is_recognised_only_as_off_or_0F(byte read, bool recognised)
    {
        var setting = SettingCatalogue.ById(SettingCatalogue.AutoPowerOff)!;

        Assert.Equal(recognised, setting.Recognised([read]));
    }

    [Theory]
    [InlineData(0x00, true)]
    [InlineData(0x01, true)]
    [InlineData(0xFF, false)]
    public void Voice_focus_is_judged_by_its_own_byte_of_the_ambient_packet(byte read, bool recognised)
    {
        var setting = SettingCatalogue.ById(SettingCatalogue.VoiceFocus)!;

        Assert.Equal(recognised, setting.Recognised([0x02, 0x11, 0xFF, read]));
    }

    [Fact]
    public void A_range_or_a_choice_is_always_recognised()
    {
        Assert.True(SettingCatalogue.ById(SettingCatalogue.Sidetone)!.Recognised([0x0A, 0xFF]));
        Assert.True(SettingCatalogue.ById(SettingCatalogue.AmbientMode)!.Recognised([0x01, 0x11, 0xFF, 0x00]));
    }

    // ---- the H9's auto power off: a number of minutes ----------------------------------------

    [Theory]
    [InlineData(0x00, true)]
    [InlineData(0x05, true)]
    [InlineData(0x0F, true)]
    [InlineData(0x1E, true)]
    [InlineData(0x3C, true)]
    [InlineData(0xB4, true)]
    [InlineData(0x07, false)]
    [InlineData(0x01, false)]
    [InlineData(0xFF, false)]
    public void The_minutes_are_recognised_only_for_the_delays_hub_offers(byte read, bool recognised)
    {
        var setting = SettingCatalogue.ById(SettingCatalogue.AutoPowerOffTime)!;

        Assert.Equal(recognised, setting.Recognised([read]));
    }

    [Fact]
    public void The_minutes_are_the_byte_itself_both_ways()
    {
        var setting = SettingCatalogue.ById(SettingCatalogue.AutoPowerOffTime)!;

        Assert.Equal(60, setting.Read([0x3C]));
        Assert.Equal([0x1E], setting.Write([0x05], 30));
        Assert.Equal([0x00], setting.Write([0x05], 0));
    }

    [Fact]
    public void A_value_that_is_not_a_delay_is_not_recognised_once_written()
    {
        var setting = SettingCatalogue.ById(SettingCatalogue.AutoPowerOffTime)!;

        // 7 minutes is not a choice. The write builds the byte, and the check on it is what refuses it.
        Assert.False(setting.Recognised(setting.Write([0x05], 7)));
    }

    private static ModelInfo Model(byte id) => new(id, 0, 0, 0, 0, "", "", "");

    [Theory]
    [InlineData(ModelInfo.InzoneH9, false)]
    [InlineData(ModelInfo.InzoneH7, true)]
    [InlineData(ModelInfo.InzoneBuds, true)]
    [InlineData(ModelInfo.InzoneH9II, true)]
    public void The_bluetooth_switch_is_not_asked_of_an_h9_which_never_answers_it(byte modelId, bool offered)
    {
        var setting = SettingCatalogue.ById(SettingCatalogue.BluetoothAutoSwitch)!;

        Assert.Equal(offered, setting.OfferedOn(Model(modelId)));
    }

    [Theory]
    [InlineData(ModelInfo.InzoneH9, false, true)]
    [InlineData(ModelInfo.InzoneH7, true, false)]
    [InlineData(ModelInfo.InzoneBuds, true, false)]
    [InlineData(ModelInfo.InzoneH9II, true, false)]
    public void Only_the_h9_gets_the_minutes_and_everyone_else_keeps_the_cautious_toggle(
        byte modelId, bool toggle, bool minutes)
    {
        var model = Model(modelId);

        Assert.Equal(toggle, SettingCatalogue.ById(SettingCatalogue.AutoPowerOff)!.OfferedOn(model));
        Assert.Equal(minutes, SettingCatalogue.ById(SettingCatalogue.AutoPowerOffTime)!.OfferedOn(model));
    }
}
