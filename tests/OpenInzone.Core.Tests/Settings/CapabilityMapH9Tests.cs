// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Protocol;
using OpenInzone.Settings;

namespace OpenInzone.Tests.Settings;

/// <summary>
/// The three parts read from an INZONE H9 on 2026-10-30, beside a GET of each id on its own. Part 3 is
/// seven bytes where Buds' is ten: the H9 does not carry the voice guidance, its language or the
/// connection mode, and says so by ending early. Reading it only as far as it goes is what keeps those
/// ids from being probed, each probe costing a 1.5 s timeout on every settings reading.
/// </summary>
public class CapabilityMapH9Tests
{
    private static readonly byte[] Part1 = [0x00, 0x00, 0x14, 0x00, 0x1D, 0xFF, 0x0A, 0x0A, 0xFF];
    private static readonly byte[] Part2 = [0x01, 0xFF, 0xFF, 0x02, 0x01, 0xFF, 0x00, 0x00, 0x01, 0x01];
    private static readonly byte[] Part3 = [0x03, 0x01, 0x01, 0x01, 0x02, 0x05, 0x00];

    private static CapabilityMap H9() => CapabilityMap.Parse(Part1, Part2, Part3);

    [Fact]
    public void The_slots_the_short_part_holds_equal_what_each_id_answers_on_its_own()
    {
        var map = H9();

        // 0x43 alone answered 03 and 0x81 alone answered 05, and 0x42 alone answered 00 01 01.
        Assert.Equal(new byte[] { 0x03 }, map.Slot(EventId.NoiseCancellingStartupMode));
        Assert.Equal(new byte[] { 0x05 }, map.Slot(EventId.AutoPowerOff));
        Assert.Equal(new byte[] { 0x00, 0x01, 0x01 }, map.Slot(EventId.NoiseCancellingToggle));
        Assert.True(map.Present(EventId.AutoPowerOff));
    }

    [Fact]
    public void The_slots_past_the_end_of_a_short_part_are_settings_the_model_does_not_have()
    {
        var map = H9();

        Assert.False(map.Present(EventId.Guidance));
        Assert.False(map.Present(EventId.VoicePromptLanguage));
        Assert.False(map.Present(EventId.ConnectionDestinationMode));
    }

    [Fact]
    public void A_part_that_stops_between_two_slots_is_still_refused()
    {
        // Part 3's slots end at 1, 5, 6, 7, 8, 9 and 10 bytes; three falls between two of them.
        var map = CapabilityMap.Parse(Part1, Part2, [0x03, 0x01, 0x01]);

        Assert.Null(map.Present(EventId.AutoPowerOff));
        Assert.Null(map.Slot(EventId.NoiseCancellingStartupMode));
    }

    [Fact]
    public void Buds_whole_part_is_unchanged()
    {
        byte[] budsPart3 = [0x03, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0xFF, 0x01, 0x01, 0x00];

        var map = CapabilityMap.Parse(Part1, Part2, budsPart3);

        Assert.Equal(new byte[] { 0x01 }, map.Slot(EventId.Guidance));
        Assert.True(map.Present(EventId.Guidance));
    }
}
