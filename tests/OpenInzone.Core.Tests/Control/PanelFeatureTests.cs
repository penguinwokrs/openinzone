// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;
using OpenInzone.Ipc;
using OpenInzone.Protocol;
using OpenInzone.Settings;

namespace OpenInzone.Tests.Control;

/// <summary>
/// What a model is offered besides its settings: the three controls on the panel, the charge, and
/// the microphone level. Taken apart from the reading so it can be checked against models this
/// project does not own.
/// </summary>
public class PanelFeatureTests
{
    private static readonly byte[] Part1 =
        [0x04, 0x00, 0x44, 0x00, 0x43, 0xFF, 0x61, 0x00, 0x15, 0xFF, 0x32, 0x00, 0xFF];

    private static readonly byte[] Part2 =
        [0x00, 0xFF, 0xFF, 0x02, 0x14, 0xFF, 0x00, 0x01, 0x01, 0x01];

    private static List<string> Offered(CapabilityMap map, bool micLevel = true) =>
        IpcSnapshot.Features(map, micLevel).ToList();

    [Fact]
    public void A_model_that_answers_for_everything_is_offered_everything()
    {
        var offered = Offered(CapabilityMap.Parse(Part1, Part2, null));

        Assert.Contains(FeatureIds.Balance, offered);
        Assert.Contains(FeatureIds.Volume, offered);
        Assert.Contains(FeatureIds.MicMute, offered);
        Assert.Contains(FeatureIds.Battery, offered);
        Assert.Contains(FeatureIds.MicLevel, offered);
    }

    /// <summary>
    /// A model with nothing to balance is not given a slider for it, nor a key on a deck. There is
    /// no such headset to hand — this is the map saying so, which is the only way to see it.
    /// </summary>
    [Fact]
    public void A_model_whose_map_says_it_has_no_balance_is_not_offered_one()
    {
        byte[] noBalance = [0x04, 0x00, 0x44, 0x00, 0x43, 0xFF, 0x61, 0x00, 0x15, 0xFF, 0xFF, 0x00, 0xFF];

        var offered = Offered(CapabilityMap.Parse(noBalance, Part2, null));

        Assert.DoesNotContain(FeatureIds.Balance, offered);
        Assert.Contains(FeatureIds.Volume, offered);
    }

    /// <summary>
    /// The charge is offered whatever its slot says, because 0xFF there is a reading rather than an
    /// answer: it is the firmware's own "this part is not reporting", and a headset model's two
    /// battery bytes can both carry it at once while nothing is docked. Reading that as a model
    /// with no battery would blank the panel's charge line and a deck's battery key for the whole
    /// connection.
    /// </summary>
    [Fact]
    public void The_charge_is_offered_even_when_nothing_is_reporting_it()
    {
        byte[] nothingReporting =
            [0x04, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x15, 0xFF, 0x32, 0x00, 0xFF];

        var map = CapabilityMap.Parse(nothingReporting, Part2, null);

        Assert.False(map.Present(EventId.BatteryInfo));
        Assert.Contains(FeatureIds.Battery, Offered(map));
    }

    [Fact]
    public void A_map_that_could_not_be_read_offers_everything_it_cannot_speak_for()
    {
        var offered = Offered(CapabilityMap.Parse(null, null, null));

        Assert.Contains(FeatureIds.Balance, offered);
        Assert.Contains(FeatureIds.Volume, offered);
        Assert.Contains(FeatureIds.MicMute, offered);
        Assert.Contains(FeatureIds.Battery, offered);
    }

    /// <summary>
    /// Windows can enumerate the capture endpoint after the dongle is already open, so the answer
    /// given once per connection can be wrong for the rest of it.
    /// </summary>
    [Fact]
    public void The_microphone_level_follows_windows_rather_than_the_map()
    {
        var map = CapabilityMap.Parse(Part1, Part2, null);

        Assert.Contains(FeatureIds.MicLevel, Offered(map, micLevel: true));
        Assert.DoesNotContain(FeatureIds.MicLevel, Offered(map, micLevel: false));
    }

    /// <summary>
    /// And when it does appear late, the list is corrected rather than left as it was read: a deck's
    /// mic-level key would otherwise stay drawn as nothing and refuse to be pressed for the whole
    /// connection, while the panel's slider beside it came back to life.
    /// </summary>
    [Fact]
    public void A_capture_endpoint_that_appears_late_is_added_to_what_was_already_said()
    {
        var without = new DeviceCapabilities([FeatureIds.Volume, FeatureIds.Battery]);

        var with = without.With(FeatureIds.MicLevel, present: true);

        Assert.True(with.Has(FeatureIds.MicLevel));
        Assert.Contains(FeatureIds.Volume, with.Features);
        Assert.False(without.Has(FeatureIds.MicLevel));
    }

    [Fact]
    public void One_that_goes_away_is_taken_off_it()
    {
        var with = new DeviceCapabilities([FeatureIds.Volume, FeatureIds.MicLevel]);

        Assert.False(with.With(FeatureIds.MicLevel, present: false).Has(FeatureIds.MicLevel));
    }

    /// <summary>Saying what is already true changes nothing, so nothing is republished for it.</summary>
    [Fact]
    public void Saying_what_is_already_so_is_the_same_set()
    {
        var capabilities = new DeviceCapabilities([FeatureIds.MicLevel]);

        Assert.Same(capabilities, capabilities.With(FeatureIds.MicLevel, present: true));
    }
}
