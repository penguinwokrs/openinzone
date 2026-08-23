// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;
using OpenInzone.StreamDeck;

namespace OpenInzone.Tests.StreamDeck;

/// <summary>
/// What each input on the deck means. A key and a dial send the same events, so the difference
/// between them lives entirely here.
/// </summary>
public class DecideTests
{
    private const bool Key = false;
    private const bool Dial = true;
    private const bool Press = true;
    private const bool Turn = false;

    [Fact]
    public void A_key_press_moves_by_the_step_it_was_given_including_its_sign()
    {
        Assert.Equal((IpcCommands.AdjustVolume, 1),
            PluginHost.Decide(ActionIds.Volume, Key, Press, ticks: 0, step: 1));

        Assert.Equal((IpcCommands.AdjustVolume, -1),
            PluginHost.Decide(ActionIds.Volume, Key, Press, ticks: 0, step: -1));
    }

    [Fact]
    public void A_turn_takes_its_direction_from_the_dial_rather_than_from_the_step()
    {
        Assert.Equal((IpcCommands.AdjustVolume, -3),
            PluginHost.Decide(ActionIds.Volume, Dial, Turn, ticks: -3, step: 1));

        // A negative step would otherwise flip the dial, so only its size is used.
        Assert.Equal((IpcCommands.AdjustVolume, -3),
            PluginHost.Decide(ActionIds.Volume, Dial, Turn, ticks: -3, step: -1));
    }

    [Fact]
    public void Several_ticks_in_one_event_move_several_steps()
    {
        Assert.Equal((IpcCommands.AdjustBalance, 20),
            PluginHost.Decide(ActionIds.Balance, Dial, Turn, ticks: 2, step: 10));
    }

    /// <summary>
    /// Pressing a dial is a button of its own. Without this, pressing the volume dial would nudge
    /// the volume by a step, which is not what pressing a dial looks like it should do.
    /// </summary>
    [Fact]
    public void Pressing_a_dial_never_counts_as_a_step()
    {
        Assert.Null(PluginHost.Decide(ActionIds.Volume, Dial, Press, ticks: 0, step: 1));
    }

    [Fact]
    public void Pressing_the_balance_dial_centres_it()
    {
        Assert.Equal((IpcCommands.SetBalance, 50),
            PluginHost.Decide(ActionIds.Balance, Dial, Press, ticks: 0, step: 10));
    }

    [Fact]
    public void Pressing_the_microphone_level_dial_mutes_it()
    {
        Assert.Equal((IpcCommands.ToggleMicMute, 0),
            PluginHost.Decide(ActionIds.MicLevel, Dial, Press, ticks: 0, step: 5));
    }

    /// <summary>
    /// Turning a mute or battery dial produces a rotate event like any other. Acting on it would
    /// toggle the microphone on every tick, or ask the tray to re-read the headset dozens of times.
    /// </summary>
    [Theory]
    [InlineData(ActionIds.MicMute)]
    [InlineData(ActionIds.Battery)]
    public void Turning_a_dial_that_has_nothing_to_turn_does_nothing(string actionId)
    {
        Assert.Null(PluginHost.Decide(actionId, Dial, Turn, ticks: 4, step: 0));
        Assert.Null(PluginHost.Decide(actionId, Dial, Turn, ticks: -4, step: 5));
    }

    [Fact]
    public void The_microphone_mute_key_toggles_on_a_press()
    {
        Assert.Equal((IpcCommands.ToggleMicMute, 0),
            PluginHost.Decide(ActionIds.MicMute, Key, Press, ticks: 0, step: 0));
    }

    [Fact]
    public void The_battery_key_asks_the_tray_to_read_the_headset_again()
    {
        Assert.Equal((IpcCommands.Refresh, 0),
            PluginHost.Decide(ActionIds.Battery, Key, Press, ticks: 0, step: 0));
    }

    [Fact]
    public void A_step_of_nothing_sends_nothing()
    {
        Assert.Null(PluginHost.Decide(ActionIds.Volume, Key, Press, ticks: 0, step: 0));
        Assert.Null(PluginHost.Decide(ActionIds.MicLevel, Dial, Turn, ticks: 0, step: 5));
    }

    [Fact]
    public void An_action_this_build_does_not_know_is_ignored_rather_than_guessed_at()
    {
        Assert.Null(PluginHost.Decide("com.penguinwokrs.openinzone.future", Key, Press, 0, 1));
    }

    /// <summary>Every command this plugin can send is one the tray will accept.</summary>
    [Fact]
    public void Nothing_is_sent_that_the_tray_would_reject()
    {
        var inputs =
            from actionId in ActionIds.All
            from isEncoder in new[] { Key, Dial }
            from pressed in new[] { Press, Turn }
            from ticks in new[] { -2, 0, 2 }
            from step in new[] { -5, 0, 1, 10 }
            select PluginHost.Decide(actionId, isEncoder, pressed, ticks, step);

        foreach (var decision in inputs)
            if (decision is not null)
                Assert.True(IpcCommands.IsKnown(decision.Value.Command), decision.Value.Command);
    }
}
