// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;
using OpenInzone.StreamDeck;

namespace OpenInzone.Tests.StreamDeck;

public class StreamDeckArgumentsTests
{
    [Fact]
    public void The_four_arguments_Stream_Deck_passes_are_understood()
    {
        var parsed = StreamDeckArguments.Parse(
            ["-port", "28196", "-pluginUUID", "AB12", "-registerEvent", "registerPlugin", "-info", "{}"]);

        Assert.NotNull(parsed);
        Assert.Equal(28196, parsed.Port);
        Assert.Equal("AB12", parsed.PluginUuid);
        Assert.Equal("registerPlugin", parsed.RegisterEvent);
    }

    [Fact]
    public void The_order_they_arrive_in_does_not_matter()
    {
        var parsed = StreamDeckArguments.Parse(
            ["-info", "{}", "-registerEvent", "registerPlugin", "-port", "1", "-pluginUUID", "X"]);

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-port 28196")]
    [InlineData("-pluginUUID X -registerEvent r")]
    [InlineData("-port 0 -pluginUUID X -registerEvent r")]
    [InlineData("-port not-a-number -pluginUUID X -registerEvent r")]
    public void Anything_short_of_all_four_is_not_a_launch_by_Stream_Deck(string commandLine) =>
        Assert.Null(StreamDeckArguments.Parse(
            commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
}

public class FeedbackTests
{
    private static readonly DeviceSnapshot Live = new(
        true, "INZONE Buds", 15, 30, false, 40, false, 75, true,
        new BatterySnapshot(97, 94, 62, true));

    [Fact]
    public void The_volume_bar_is_a_percentage_of_the_headsets_own_scale()
    {
        var feedback = PluginHost.Feedback(ActionIds.Volume, Live);

        Assert.Equal("15 / 30", feedback.Value);
        Assert.Equal(50, feedback.Indicator!.Value);
    }

    [Fact]
    public void The_balance_readout_names_the_side_it_leans_to()
    {
        Assert.Equal("GAME 1.0", PluginHost.Feedback(ActionIds.Balance, Live with { Balance = 40 }).Value);
        Assert.Equal("CHAT 2.0", PluginHost.Feedback(ActionIds.Balance, Live with { Balance = 70 }).Value);
        Assert.Equal("CENTRE", PluginHost.Feedback(ActionIds.Balance, Live with { Balance = 50 }).Value);
    }

    [Fact]
    public void A_disconnected_headset_shows_nothing_on_every_dial()
    {
        foreach (string actionId in ActionIds.All)
        {
            var feedback = PluginHost.Feedback(actionId, DeviceSnapshot.Disconnected);
            Assert.Equal("--", feedback.Value);
        }
    }

    [Fact]
    public void A_model_with_no_microphone_level_shows_nothing_rather_than_zero()
    {
        var feedback = PluginHost.Feedback(ActionIds.MicLevel, Live with { MicLevelAvailable = false });

        Assert.Equal("--", feedback.Value);
    }

    [Fact]
    public void The_battery_dial_shows_both_earbuds()
    {
        Assert.Equal("L 97%  R 94%", PluginHost.Feedback(ActionIds.Battery, Live).Value);
    }

    [Fact]
    public void Every_action_has_a_name_of_its_own_on_a_dial()
    {
        var titles = ActionIds.All.Select(id => PluginHost.Feedback(id, Live).Title).ToList();

        Assert.Equal(titles.Count, titles.Distinct().Count());
        Assert.All(titles, title => Assert.False(string.IsNullOrWhiteSpace(title)));
    }

    /// <summary>
    /// A dial for volume up and a dial for volume are the same number. Inventing a second readout
    /// for it would be a second thing to keep agreeing with the headset.
    /// </summary>
    [Fact]
    public void A_directed_dial_reads_the_same_as_the_dial_for_the_setting_it_moves()
    {
        Assert.Equal(PluginHost.Feedback(ActionIds.Volume, Live).Value,
            PluginHost.Feedback(ActionIds.VolumeUp, Live).Value);
        Assert.Equal(PluginHost.Feedback(ActionIds.Volume, Live).Indicator!.Value,
            PluginHost.Feedback(ActionIds.VolumeDown, Live).Indicator!.Value);
        Assert.Equal(PluginHost.Feedback(ActionIds.Balance, Live).Value,
            PluginHost.Feedback(ActionIds.BalanceGame, Live).Value);
        Assert.Equal(PluginHost.Feedback(ActionIds.MicLevel, Live).Value,
            PluginHost.Feedback(ActionIds.MicLevelUp, Live).Value);
    }
}

public class ActionIdTests
{
    [Fact]
    public void Every_action_is_named_under_the_plugin_that_owns_it()
    {
        Assert.All(ActionIds.All,
            id => Assert.StartsWith(ActionIds.Prefix + ".", id, StringComparison.Ordinal));
    }

    [Fact]
    public void The_actions_that_move_a_value_have_a_step_and_the_others_do_not()
    {
        Assert.True(ActionIds.DefaultStep(ActionIds.Volume) > 0);
        Assert.True(ActionIds.DefaultStep(ActionIds.Balance) > 0);
        Assert.True(ActionIds.DefaultStep(ActionIds.MicLevel) > 0);
        Assert.Equal(0, ActionIds.DefaultStep(ActionIds.MicMute));
        Assert.Equal(0, ActionIds.DefaultStep(ActionIds.Battery));
    }

    /// <summary>The balance step is the one INZONE Hub moves by, so both applications agree.</summary>
    [Fact]
    public void The_balance_step_is_one_notch_of_the_scale_the_headset_uses()
    {
        Assert.Equal(10, ActionIds.DefaultStep(ActionIds.Balance));
    }

    [Fact]
    public void A_directed_action_names_the_setting_it_moves_and_the_way_it_moves_it()
    {
        Assert.Equal(ActionIds.Volume, ActionIds.Subject(ActionIds.VolumeUp));
        Assert.Equal(ActionIds.Volume, ActionIds.Subject(ActionIds.VolumeDown));
        Assert.Equal(ActionIds.MicLevel, ActionIds.Subject(ActionIds.MicLevelUp));
        Assert.Equal(ActionIds.MicLevel, ActionIds.Subject(ActionIds.MicLevelDown));
        Assert.Equal(ActionIds.Balance, ActionIds.Subject(ActionIds.BalanceGame));
        Assert.Equal(ActionIds.Balance, ActionIds.Subject(ActionIds.BalanceChat));

        Assert.Equal(1, ActionIds.Direction(ActionIds.VolumeUp));
        Assert.Equal(-1, ActionIds.Direction(ActionIds.VolumeDown));
        Assert.Equal(1, ActionIds.Direction(ActionIds.MicLevelUp));
        Assert.Equal(-1, ActionIds.Direction(ActionIds.MicLevelDown));
    }

    /// <summary>
    /// Game is the low end of the scale, so the key that says GAME is the one that subtracts. It
    /// reads backwards to anyone who has not met the scale, which is why it is pinned here.
    /// </summary>
    [Fact]
    public void More_game_goes_down_the_scale_and_more_chat_goes_up_it()
    {
        Assert.Equal(-1, ActionIds.Direction(ActionIds.BalanceGame));
        Assert.Equal(1, ActionIds.Direction(ActionIds.BalanceChat));
    }

    /// <summary>
    /// An action that is not directed is its own subject and has no direction, which is what lets
    /// every lookup ask through <c>Subject</c> without the existing five gaining a case.
    /// </summary>
    [Fact]
    public void An_undirected_action_is_its_own_subject()
    {
        Assert.Equal(ActionIds.Volume, ActionIds.Subject(ActionIds.Volume));
        Assert.Equal(ActionIds.Battery, ActionIds.Subject(ActionIds.Battery));
        Assert.Equal(0, ActionIds.Direction(ActionIds.Volume));
        Assert.Equal(0, ActionIds.Direction(ActionIds.MicMute));

        const string later = "com.penguinwokrs.openinzone.something-later";
        Assert.Equal(later, ActionIds.Subject(later));
        Assert.Equal(0, ActionIds.Direction(later));
    }

    [Fact]
    public void A_directed_action_steps_by_the_same_default_as_the_setting_it_moves()
    {
        Assert.Equal(ActionIds.DefaultStep(ActionIds.Volume), ActionIds.DefaultStep(ActionIds.VolumeUp));
        Assert.Equal(ActionIds.DefaultStep(ActionIds.Balance), ActionIds.DefaultStep(ActionIds.BalanceGame));
        Assert.Equal(ActionIds.DefaultStep(ActionIds.MicLevel), ActionIds.DefaultStep(ActionIds.MicLevelDown));
    }
}
