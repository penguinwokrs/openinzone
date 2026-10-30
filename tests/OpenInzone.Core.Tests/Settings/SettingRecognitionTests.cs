// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

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
}
