// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;
using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Tests.Control;

/// <summary>
/// The reducer is how the tray stays honest while the wearer, INZONE Hub or another copy of the
/// CLI changes something: every notification the headset sends is folded into the snapshot the
/// UI renders. The parameter layouts come from docs/PROTOCOL.md.
/// </summary>
public class DeviceStateTests
{
    private static readonly DeviceState Connected = DeviceState.Disconnected with { Connected = true };

    [Fact]
    public void Folds_a_balance_notification()
    {
        var state = Connected.Apply(EventId.GameChatMixBalance, [30]);

        Assert.Equal(30, state.Balance.Value);
    }

    [Fact]
    public void Folds_a_headphone_volume_notification()
    {
        // [mute, value, percent]; INZONE Buds report 0xFF for percent.
        var state = Connected.Apply(EventId.HeadphoneVolume, [1, 20, 0xFF]);

        Assert.True(state.Volume.Muted);
        Assert.Equal(20, state.Volume.Value);
    }

    [Fact]
    public void Folds_a_microphone_notification()
    {
        var state = Connected.Apply(EventId.MicVolume, [1, 0xFF, 0xFF]);

        Assert.True(state.Mic.Muted);
    }

    [Fact]
    public void Folds_a_battery_notification()
    {
        var state = Connected.Apply(EventId.BatteryInfo, [1, 97, 1, 94, 1, 34]);

        Assert.Equal(97, state.Battery.LeftPercent);
        Assert.Equal(94, state.Battery.RightPercent);
        Assert.Equal(34, state.Battery.CasePercent);
    }

    [Fact]
    public void Ignores_an_event_it_does_not_model()
    {
        var before = Connected.Apply(EventId.GameChatMixBalance, [30]);

        var after = before.Apply(EventId.SidetoneVolume, [5, 0]);

        Assert.Equal(before, after);
    }

    /// <summary>A truncated parameter must not throw on the reader thread.</summary>
    [Fact]
    public void Ignores_a_parameter_shorter_than_the_layout()
    {
        var before = Connected.Apply(EventId.HeadphoneVolume, [0, 15, 0xFF]);

        var after = before.Apply(EventId.HeadphoneVolume, [0]);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Disconnected_state_reports_no_model()
    {
        Assert.False(DeviceState.Disconnected.Connected);
        Assert.Equal("", DeviceState.Disconnected.ModelName);
    }
}
