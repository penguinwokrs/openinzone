// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;
using OpenInzone.Model;

namespace OpenInzone.Tests.Ipc;

public class IpcSnapshotTests
{
    private static DeviceState EarbudState(byte[] battery) => new(
        Connected: true,
        ModelName: "INZONE Buds",
        Balance: new MixBalance(40),
        Volume: new HeadphoneVolume(Muted: false, Value: 16, Percent: 53),
        Mic: new MicVolume(Muted: true, Value: 0xFF, Percent: 0xFF),
        MicLevel: 75,
        MicLevelAvailable: true,
        Battery: BatteryInfo.Parse(battery));

    [Fact]
    public void The_two_earbuds_do_not_change_places()
    {
        var snapshot = IpcSnapshot.From(EarbudState([0, 97, 0, 94, 0, 62]));

        Assert.Equal(97, snapshot.Battery.Left);
        Assert.Equal(94, snapshot.Battery.Right);
        Assert.Equal(62, snapshot.Battery.Case);
        Assert.True(snapshot.Battery.HasSeparateBuds);
    }

    [Fact]
    public void An_earbud_in_the_case_reads_as_nothing_rather_than_as_empty()
    {
        var snapshot = IpcSnapshot.From(EarbudState([0, 97, 0, 0xFF, 0, 62]));

        Assert.Equal(97, snapshot.Battery.Left);
        Assert.Null(snapshot.Battery.Right);
    }

    [Fact]
    public void A_headset_reports_one_level_and_no_case()
    {
        var state = EarbudState([0, 88]) with { ModelName = "INZONE H9" };

        var snapshot = IpcSnapshot.From(state);

        Assert.Equal(88, snapshot.Battery.Left);
        Assert.Null(snapshot.Battery.Right);
        Assert.Null(snapshot.Battery.Case);
        Assert.False(snapshot.Battery.HasSeparateBuds);
    }

    [Fact]
    public void The_volume_scale_travels_with_the_value()
    {
        var snapshot = IpcSnapshot.From(EarbudState([0, 97, 0, 94, 0, 62]));

        Assert.Equal(16, snapshot.Volume);
        Assert.Equal(HeadphoneVolume.Max, snapshot.VolumeMax);
    }

    [Fact]
    public void The_rest_of_the_state_is_carried_across()
    {
        var snapshot = IpcSnapshot.From(EarbudState([0, 97, 0, 94, 0, 62]));

        Assert.True(snapshot.Connected);
        Assert.Equal("INZONE Buds", snapshot.Model);
        Assert.Equal(40, snapshot.Balance);
        Assert.True(snapshot.MicMuted);
        Assert.Equal(75, snapshot.MicLevel);
        Assert.True(snapshot.MicLevelAvailable);
    }

    [Fact]
    public void A_headset_that_is_not_connected_says_so()
    {
        var snapshot = IpcSnapshot.From(DeviceState.Disconnected);

        Assert.False(snapshot.Connected);
        Assert.False(snapshot.MicLevelAvailable);
    }

    /// <summary>
    /// A default BatteryInfo is all zeroes, which is indistinguishable from a flat battery. The
    /// tray never showed it because it checks Connected first, but a client reading the wire has
    /// only the numbers, so the disconnected state has to be honest on its own.
    /// </summary>
    [Fact]
    public void A_headset_that_is_not_connected_reports_no_battery_rather_than_a_flat_one()
    {
        var snapshot = IpcSnapshot.From(DeviceState.Disconnected);

        Assert.Null(snapshot.Battery.Left);
        Assert.Null(snapshot.Battery.Right);
        Assert.Null(snapshot.Battery.Case);
    }
}
