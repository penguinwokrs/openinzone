// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Model;

namespace OpenInzone.Tests.Model;

/// <summary>
/// The settings read out of INZONE Hub's own traffic. Every byte here was observed rather than
/// guessed - see docs/PROTOCOL.md - and these fix the readings so a later change cannot quietly
/// disagree with what the headset actually said.
/// </summary>
public class AmbientSettingTests
{
    [Theory]
    [InlineData(0x00, AmbientMode.Off)]
    [InlineData(0x01, AmbientMode.NoiseCancelling)]
    [InlineData(0x02, AmbientMode.Ambient)]
    public void The_first_byte_is_the_mode(byte value, AmbientMode expected)
    {
        Assert.Equal(expected, AmbientSetting.Parse([value, 0x14, 0xFF, 0x00]).Mode);
    }

    [Fact]
    public void The_second_byte_is_the_ambient_level()
    {
        Assert.Equal(1, AmbientSetting.Parse([0x02, 0x01, 0xFF, 0x00]).Level);
        Assert.Equal(20, AmbientSetting.Parse([0x02, 0x14, 0xFF, 0x00]).Level);
    }

    [Fact]
    public void The_fourth_byte_is_voice_focus()
    {
        Assert.False(AmbientSetting.Parse([0x02, 0x14, 0xFF, 0x00]).VoiceFocus);
        Assert.True(AmbientSetting.Parse([0x02, 0x14, 0xFF, 0x01]).VoiceFocus);
    }

    /// <summary>What goes back out has to be what came in, byte for byte.</summary>
    [Theory]
    [InlineData(new byte[] { 0x00, 0x14, 0xFF, 0x00 })]
    [InlineData(new byte[] { 0x01, 0x14, 0xFF, 0x00 })]
    [InlineData(new byte[] { 0x02, 0x01, 0xFF, 0x01 })]
    public void A_reading_survives_being_written_back(byte[] observed)
    {
        Assert.Equal(observed, AmbientSetting.Parse(observed).ToParam());
    }

    /// <summary>
    /// The level travels in every mode, including the ones that ignore it. Dropping it on a mode
    /// change would reset a slider the user never touched.
    /// </summary>
    [Fact]
    public void Changing_the_mode_leaves_the_level_alone()
    {
        var ambient = AmbientSetting.Parse([0x02, 0x07, 0xFF, 0x00]);

        var off = ambient with { Mode = AmbientMode.Off };

        Assert.Equal(7, off.Level);
        Assert.Equal(new byte[] { 0x00, 0x07, 0xFF, 0x00 }, off.ToParam());
    }

    [Fact]
    public void The_level_is_held_inside_the_range_the_headset_uses()
    {
        Assert.Equal(1, AmbientSetting.ClampLevel(0));
        Assert.Equal(1, AmbientSetting.ClampLevel(-5));
        Assert.Equal(20, AmbientSetting.ClampLevel(21));
        Assert.Equal(7, AmbientSetting.ClampLevel(7));
    }

    /// <summary>A packet shorter than the layout must not throw on the reader thread.</summary>
    [Fact]
    public void A_reading_without_the_voice_focus_byte_reads_as_off()
    {
        Assert.False(AmbientSetting.Parse([0x02, 0x14, 0xFF]).VoiceFocus);
    }
}

public class SidetoneTests
{
    /// <summary>Ten, not a hundred: INZONE Hub's slider runs 0-10 and so does the packet.</summary>
    [Fact]
    public void The_range_is_the_one_the_headset_uses()
    {
        Assert.Equal(0, SidetoneVolume.Clamp(-1));
        Assert.Equal(10, SidetoneVolume.Clamp(11));
        Assert.Equal(SidetoneVolume.Max, (byte)10);
    }

    [Fact]
    public void The_byte_the_earbuds_do_not_report_goes_back_unchanged()
    {
        var observed = new byte[] { 0x05, 0xFF };

        Assert.Equal(observed, SidetoneVolume.Parse(observed).ToParam());
    }
}

public class DeviceToggleTests
{
    /// <summary>
    /// Auto power off answers 0x0F for on rather than 0x01, so the value for on is carried rather
    /// than assumed - and a reading is written back with whatever byte it arrived as.
    /// </summary>
    [Fact]
    public void On_is_whatever_byte_the_setting_uses_for_on()
    {
        var autoPowerOff = DeviceToggle.Parse([0x0F], onValue: 0x0F);
        var guidance = DeviceToggle.Parse([0x01], onValue: 0x01);

        Assert.True(autoPowerOff.IsOn);
        Assert.True(guidance.IsOn);
    }

    [Fact]
    public void Off_is_zero_whatever_on_is()
    {
        Assert.False(DeviceToggle.Parse([0x00], onValue: 0x0F).IsOn);
        Assert.False(DeviceToggle.Parse([0x00], onValue: 0x01).IsOn);
    }

    [Fact]
    public void Turning_it_on_writes_the_byte_that_setting_uses()
    {
        Assert.Equal(new byte[] { 0x0F }, DeviceToggle.Parse([0x00], 0x0F).With(true).ToParam());
        Assert.Equal(new byte[] { 0x00 }, DeviceToggle.Parse([0x0F], 0x0F).With(false).ToParam());
        Assert.Equal(new byte[] { 0x01 }, DeviceToggle.Parse([0x00], 0x01).With(true).ToParam());
    }
}
