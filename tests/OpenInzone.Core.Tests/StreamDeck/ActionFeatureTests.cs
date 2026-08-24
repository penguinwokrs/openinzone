// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;
using OpenInzone.StreamDeck;

namespace OpenInzone.Tests.StreamDeck;

/// <summary>
/// A deck cannot be told to take a key away, so a key for something the connected model does not
/// have is drawn as no reading and does nothing when pressed. That decision rests on every action
/// naming a feature the headset can actually answer for.
/// </summary>
public class ActionFeatureTests
{
    [Fact]
    public void Every_action_names_a_feature_the_headset_reports_on()
    {
        Assert.All(ActionIds.All, actionId => Assert.Contains(ActionIds.Feature(actionId), FeatureIds.All));
    }

    [Fact]
    public void Each_action_names_its_own_feature()
    {
        Assert.Equal(FeatureIds.Balance, ActionIds.Feature(ActionIds.Balance));
        Assert.Equal(FeatureIds.Volume, ActionIds.Feature(ActionIds.Volume));
        Assert.Equal(FeatureIds.MicMute, ActionIds.Feature(ActionIds.MicMute));
        Assert.Equal(FeatureIds.MicLevel, ActionIds.Feature(ActionIds.MicLevel));
        Assert.Equal(FeatureIds.Battery, ActionIds.Feature(ActionIds.Battery));
    }

    /// <summary>
    /// A plugin that has not been told what the model has offers everything, which is what it did
    /// before it was told anything at all. A deck going blank because the tray is an older build
    /// would be a worse answer than a key that turns out to do nothing.
    /// </summary>
    [Fact]
    public void A_plugin_that_has_not_been_told_offers_every_key()
    {
        DeviceCapabilities? untold = null;

        Assert.All(ActionIds.All, actionId => Assert.True(untold.Allows(ActionIds.Feature(actionId))));
    }

    [Fact]
    public void A_model_without_a_balance_leaves_the_balance_key_alone_and_keeps_the_rest()
    {
        var capabilities = new DeviceCapabilities(
            [FeatureIds.Volume, FeatureIds.MicMute, FeatureIds.MicLevel, FeatureIds.Battery]);

        Assert.False(capabilities.Allows(ActionIds.Feature(ActionIds.Balance)));
        Assert.True(capabilities.Allows(ActionIds.Feature(ActionIds.Volume)));
    }
}
