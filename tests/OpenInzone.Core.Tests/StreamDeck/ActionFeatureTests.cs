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

    /// <summary>
    /// An action this build does not know names no feature at all. It used to fall through to the
    /// battery, so a future action whose case was forgotten would have been gated on a capability
    /// it never asked about — quietly, since nothing iterates anything but the four known ones.
    /// </summary>
    [Fact]
    public void An_action_this_build_does_not_know_is_gated_on_nothing()
    {
        Assert.Null(ActionIds.Feature("com.penguinwokrs.openinzone.something-later"));

        var capabilities = new DeviceCapabilities([FeatureIds.Volume]);
        Assert.True(capabilities.Allows(ActionIds.Feature("com.penguinwokrs.openinzone.something-later")));
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

    /// <summary>
    /// A headset with everything, so that anything left out below is left out by the capabilities
    /// rather than by the reading.
    /// </summary>
    private static readonly DeviceSnapshot Live = new(
        true, "A model with no balance", 16, 30, false, 40, false, 75, true,
        new BatterySnapshot(97, 94, 62, true));

    /// <summary>
    /// The model this project cannot buy: one that answers for everything except the game/chat
    /// balance. Nothing to hand is like this — INZONE Buds has all thirteen features — so the only
    /// way to see what a deck does with such a model is to say so and look.
    /// </summary>
    private static readonly DeviceCapabilities NoBalance = new(
        [FeatureIds.Volume, FeatureIds.MicMute, FeatureIds.MicLevel, FeatureIds.Battery]);

    [Fact]
    public void A_turn_of_a_balance_dial_means_nothing_on_a_model_that_has_no_balance()
    {
        Assert.Null(PluginHost.Decide(
            ActionIds.Balance, isEncoder: true, pressed: false, ticks: 2, step: 10, NoBalance));
    }

    /// <summary>
    /// Including the press, which on a dial is its own shortcut — centring a balance the model does
    /// not have would be the worst of the three, since it writes rather than merely reads.
    /// </summary>
    [Fact]
    public void A_press_of_a_balance_dial_means_nothing_either()
    {
        Assert.Null(PluginHost.Decide(
            ActionIds.Balance, isEncoder: true, pressed: true, ticks: 0, step: 10, NoBalance));

        Assert.Null(PluginHost.Decide(
            ActionIds.Balance, isEncoder: false, pressed: true, ticks: 0, step: 10, NoBalance));
    }

    [Fact]
    public void The_keys_the_model_does_have_go_on_working()
    {
        Assert.Equal((IpcCommands.AdjustVolume, 1), PluginHost.Decide(
            ActionIds.Volume, isEncoder: false, pressed: true, ticks: 0, step: 1, NoBalance));

        Assert.Equal((IpcCommands.Refresh, 0), PluginHost.Decide(
            ActionIds.Battery, isEncoder: false, pressed: true, ticks: 0, step: 0, NoBalance));
    }

    /// <summary>
    /// Every input still means what it meant when nothing has said what the model has. A deck going
    /// dead because the tray is an older build would be a worse answer than a key that turns out to
    /// do nothing.
    /// </summary>
    [Fact]
    public void An_untold_plugin_decides_exactly_as_it_did_before()
    {
        Assert.Equal((IpcCommands.AdjustBalance, 10), PluginHost.Decide(
            ActionIds.Balance, isEncoder: false, pressed: true, ticks: 0, step: 10));

        Assert.Equal(
            PluginHost.Decide(ActionIds.Balance, isEncoder: false, pressed: true, ticks: 0, step: 10),
            PluginHost.Decide(ActionIds.Balance, isEncoder: false, pressed: true, ticks: 0, step: 10,
                new DeviceCapabilities(FeatureIds.All)));
    }

    [Fact]
    public void A_balance_dial_reads_as_nothing_on_a_model_that_has_no_balance()
    {
        var absent = PluginHost.Feedback(ActionIds.Balance, Live, NoBalance);

        Assert.Equal("--", absent.Value);
        Assert.Equal(0, absent.Indicator?.Value);

        // While the dials it does have carry on reading the headset.
        Assert.Equal("16 / 30", PluginHost.Feedback(ActionIds.Volume, Live, NoBalance).Value);
    }

    /// <summary>
    /// The same face a headset that is not answering draws. From the key's point of view that is
    /// the truth in both cases: there is nothing there to show.
    /// </summary>
    [Fact]
    public void A_balance_key_is_drawn_as_no_reading_on_a_model_that_has_no_balance()
    {
        Assert.Equal(
            KeyFace.For(ActionIds.Balance, DeviceSnapshot.Disconnected),
            KeyFace.For(ActionIds.Balance, Live, NoBalance));

        Assert.NotEqual(
            KeyFace.For(ActionIds.Balance, DeviceSnapshot.Disconnected),
            KeyFace.For(ActionIds.Volume, Live, NoBalance));
    }

    /// <summary>
    /// A directed action is the same setting with the direction settled. Gating it on anything but
    /// its subject's feature would give a model a key for a setting it does not have, or take away
    /// a key for one it does.
    /// </summary>
    [Fact]
    public void A_directed_action_is_gated_on_the_feature_of_the_setting_it_moves()
    {
        Assert.Equal(FeatureIds.Volume, ActionIds.Feature(ActionIds.VolumeUp));
        Assert.Equal(FeatureIds.Volume, ActionIds.Feature(ActionIds.VolumeDown));
        Assert.Equal(FeatureIds.MicLevel, ActionIds.Feature(ActionIds.MicLevelUp));
        Assert.Equal(FeatureIds.MicLevel, ActionIds.Feature(ActionIds.MicLevelDown));
        Assert.Equal(FeatureIds.Balance, ActionIds.Feature(ActionIds.BalanceGame));
        Assert.Equal(FeatureIds.Balance, ActionIds.Feature(ActionIds.BalanceChat));
    }

    [Fact]
    public void A_model_without_a_balance_takes_both_of_its_directed_keys_with_it()
    {
        var capabilities = new DeviceCapabilities([FeatureIds.Volume, FeatureIds.MicMute]);

        Assert.False(capabilities.Allows(ActionIds.Feature(ActionIds.BalanceGame)));
        Assert.False(capabilities.Allows(ActionIds.Feature(ActionIds.BalanceChat)));
        Assert.True(capabilities.Allows(ActionIds.Feature(ActionIds.VolumeUp)));
    }
}
