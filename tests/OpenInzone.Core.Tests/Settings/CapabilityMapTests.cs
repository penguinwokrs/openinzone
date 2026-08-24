// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Protocol;
using OpenInzone.Settings;

namespace OpenInzone.Tests.Settings;

/// <summary>
/// The three parts read from INZONE Buds on 2026-08-24, beside a GET of each id on its own. Every
/// slot below equals what that id answered alone, and every 0xFF slot sits where that id timed out,
/// which is what makes the map an answer where a timeout is not one.
/// </summary>
public class CapabilityMapTests
{
    private static readonly byte[] Part1 =
        [0x04, 0x00, 0x44, 0x00, 0x43, 0xFF, 0x61, 0x00, 0x15, 0xFF, 0x32, 0x00, 0xFF];

    private static readonly byte[] Part2 =
        [0x00, 0xFF, 0xFF, 0x02, 0x14, 0xFF, 0x00, 0x01, 0x01, 0x01];

    private static readonly byte[] Part3 =
        [0x03, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0xFF, 0x01, 0x01, 0x00];

    private static CapabilityMap Buds() => CapabilityMap.Parse(Part1, Part2, Part3);

    [Fact]
    public void Part_one_decodes_to_what_each_id_answers_on_its_own()
    {
        var map = Buds();

        Assert.Equal(new byte[] { 0x00, 0x44, 0x00, 0x43, 0xFF, 0x61 }, map.Slot(EventId.BatteryInfo));
        Assert.Equal(new byte[] { 0x00, 0x15, 0xFF }, map.Slot(EventId.HeadphoneVolume));
        Assert.Equal(new byte[] { 0x32 }, map.Slot(EventId.GameChatMixBalance));
        Assert.Equal(new byte[] { 0x00, 0xFF }, map.Slot(EventId.SidetoneVolume));
    }

    [Fact]
    public void Part_two_decodes_to_what_each_id_answers_on_its_own()
    {
        var map = Buds();

        Assert.Equal(new byte[] { 0x00, 0xFF, 0xFF }, map.Slot(EventId.MicVolume));
        Assert.Equal(new byte[] { 0x02, 0x14, 0xFF, 0x00 }, map.Slot(EventId.AmbientSetting));
        Assert.Equal(new byte[] { 0x01, 0x01, 0x01 }, map.Slot(EventId.NoiseCancellingToggle));
    }

    [Fact]
    public void Part_three_decodes_to_what_each_id_answers_on_its_own()
    {
        var map = Buds();

        Assert.Equal(new byte[] { 0x03 }, map.Slot(EventId.NoiseCancellingStartupMode));
        Assert.Equal(new byte[] { 0x0F }, map.Slot(EventId.AutoPowerOff));
        Assert.Equal(new byte[] { 0x01 }, map.Slot(EventId.VoicePromptLanguage));
        Assert.Equal(new byte[] { 0x01 }, map.Slot(EventId.Guidance));
        Assert.Equal(new byte[] { 0x00 }, map.Slot(EventId.ConnectionDestinationMode));
    }

    /// <summary>
    /// The settings INZONE Buds does not carry. Each of these is an id that times out when asked
    /// for on its own, which on its own says nothing — a slot of 0xFF says it plainly.
    /// </summary>
    [Fact]
    public void A_slot_of_nothing_but_FF_is_the_model_saying_it_has_no_such_setting()
    {
        var map = Buds();

        Assert.False(map.Present(EventId.BluetoothStatus));
        Assert.False(map.Present(EventId.LedSetting));
    }

    [Fact]
    public void A_slot_with_a_value_in_it_is_present_even_when_some_bytes_are_FF()
    {
        var map = Buds();

        // 00 FF FF: the mute flag answered, and the level and percent bytes are the firmware's
        // own "not reported" sentinel, which is a different thing from the setting being absent.
        Assert.True(map.Present(EventId.MicVolume));
        Assert.True(map.Present(EventId.AmbientSetting));
        Assert.True(map.Present(EventId.AutoPowerOff));
    }

    /// <summary>
    /// 0x8E, the Bluetooth automatic connection switch, is in none of the three parts. Unknown is
    /// not absent: a caller has to probe for it, and saying false here would hide a real setting.
    /// </summary>
    [Fact]
    public void An_id_the_map_does_not_carry_is_unknown_rather_than_absent()
    {
        var map = Buds();

        Assert.Null(map.Present(EventId.IncomingPermission));
        Assert.Null(map.Slot(EventId.IncomingPermission));
    }

    /// <summary>
    /// A headset model reports two battery bytes rather than six. Nothing here knows that number:
    /// everything after the battery in part 1 is fixed, so what is left over is the battery.
    /// </summary>
    [Fact]
    public void A_shorter_battery_shifts_every_slot_after_it_without_a_model_table()
    {
        byte[] headsetPart1 = [0x00, 0x00, 0x55, 0x00, 0x15, 0xFF, 0x32, 0x00, 0xFF];

        var map = CapabilityMap.Parse(headsetPart1, Part2, Part3);

        Assert.Equal(new byte[] { 0x00, 0x55 }, map.Slot(EventId.BatteryInfo));
        Assert.Equal(new byte[] { 0x00, 0x15, 0xFF }, map.Slot(EventId.HeadphoneVolume));
        Assert.Equal(new byte[] { 0x32 }, map.Slot(EventId.GameChatMixBalance));
        Assert.Equal(new byte[] { 0x00, 0xFF }, map.Slot(EventId.SidetoneVolume));
    }

    /// <summary>
    /// A part whose length does not add up is a model this build cannot read, and reading it anyway
    /// would report settings at the wrong offsets. Everything it carries goes back to being unknown,
    /// which is what sends the caller to probing.
    /// </summary>
    [Fact]
    public void A_part_that_does_not_add_up_is_refused_rather_than_misread()
    {
        var map = CapabilityMap.Parse(Part1, [0x00, 0xFF, 0xFF, 0x02], Part3);

        Assert.Null(map.Present(EventId.AmbientSetting));
        Assert.Null(map.Slot(EventId.MicVolume));

        // The parts are independent, so one that did not add up does not cost the others.
        Assert.Equal(new byte[] { 0x0F }, map.Slot(EventId.AutoPowerOff));
        Assert.Equal(new byte[] { 0x32 }, map.Slot(EventId.GameChatMixBalance));
    }

    [Fact]
    public void A_part_that_never_answered_leaves_its_ids_unknown()
    {
        var map = CapabilityMap.Parse(null, null, Part3);

        Assert.Null(map.Present(EventId.GameChatMixBalance));
        Assert.Null(map.Present(EventId.AmbientSetting));
        Assert.False(map.Present(EventId.LedSetting));
        Assert.False(map.IsEmpty);
    }

    [Fact]
    public void A_headset_that_answered_none_of_the_three_leaves_nothing_to_go_on()
    {
        var map = CapabilityMap.Parse(null, null, null);

        Assert.True(map.IsEmpty);
        Assert.Null(map.Present(EventId.AutoPowerOff));
    }

    [Fact]
    public void A_part_one_with_no_room_for_a_battery_is_refused()
    {
        Assert.Null(CapabilityMap.Parse([0x04, 0x00, 0x15, 0xFF, 0x32, 0x00, 0xFF], null, null)
            .Slot(EventId.GameChatMixBalance));
        Assert.Null(CapabilityMap.Parse([], null, null).Slot(EventId.BatteryInfo));
    }

    /// <summary>
    /// Part 1 has to be refusable too, and taking whatever is left over as the battery meant it
    /// never could be: any discrepancy was absorbed silently, and every slot after the battery
    /// moved. A part carrying one field this build does not know would then have answered
    /// confidently about the wrong ids — hiding or showing panel controls at random — where not
    /// answering sends the caller to probing instead.
    /// </summary>
    [Fact]
    public void A_part_one_whose_battery_is_neither_width_the_protocol_records_is_refused()
    {
        // Six bytes and two are the widths seen; five is a part this build cannot account for.
        byte[] fiveByteBattery = [0x04, 0x00, 0x44, 0x00, 0x43, 0xFF, 0x00, 0x15, 0xFF, 0x32, 0x00, 0xFF];

        var map = CapabilityMap.Parse(fiveByteBattery, null, null);

        Assert.Null(map.Slot(EventId.BatteryInfo));
        Assert.Null(map.Present(EventId.GameChatMixBalance));
        Assert.True(map.IsEmpty);
    }

    [Fact]
    public void Both_widths_the_protocol_records_are_taken()
    {
        Assert.NotNull(CapabilityMap.Parse(Part1, null, null).Slot(EventId.BatteryInfo));

        byte[] headset = [0x00, 0x00, 0x55, 0x00, 0x15, 0xFF, 0x32, 0x00, 0xFF];
        Assert.NotNull(CapabilityMap.Parse(headset, null, null).Slot(EventId.BatteryInfo));
    }
}
