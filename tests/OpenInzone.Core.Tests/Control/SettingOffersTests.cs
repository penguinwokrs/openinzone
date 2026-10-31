// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;
using OpenInzone.Ipc;
using OpenInzone.Settings;

namespace OpenInzone.Tests.Control;

/// <summary>
/// A setting that was left out of the reading is not just hidden: a client that names it is refused,
/// so it cannot be written by way of the CLI, a Stream Deck or the IPC channel either.
/// </summary>
public class SettingOffersTests
{
    [Fact]
    public void Nothing_is_refused_before_the_first_reading()
    {
        Assert.True(new SettingState().Offers(SettingCatalogue.AutoPowerOff));
    }

    [Fact]
    public void A_setting_left_out_of_the_reading_is_not_offered()
    {
        var state = new SettingState();
        state.Replace([new SettingValue(SettingCatalogue.AmbientMode, 1), new SettingValue(SettingCatalogue.Sidetone, 10)]);

        Assert.True(state.Offers(SettingCatalogue.AmbientMode));
        Assert.False(state.Offers(SettingCatalogue.AutoPowerOff));
    }

    [Fact]
    public void Nothing_is_offered_by_name_once_the_reading_is_cleared_and_read_again_without_it()
    {
        var state = new SettingState();
        state.Replace([new SettingValue(SettingCatalogue.AutoPowerOff, 1)]);
        state.Clear();
        Assert.True(state.Offers(SettingCatalogue.AutoPowerOff));    // cleared: nothing known, nothing refused

        state.Replace([new SettingValue(SettingCatalogue.Sidetone, 3)]);
        Assert.False(state.Offers(SettingCatalogue.AutoPowerOff));
    }
}
