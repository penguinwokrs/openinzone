// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

public class HotkeyCommandTests
{
    /// <summary>Records what a command asked for, so the catalogue can be checked without hardware.</summary>
    private sealed class Recorder : IDeviceActions
    {
        public List<string> Calls { get; } = [];
        public void AdjustBalance(int delta) => Calls.Add($"balance {delta:+#;-#;0}");
        public void SetBalance(int value) => Calls.Add($"balance = {value}");
        public void AdjustVolume(int delta) => Calls.Add($"volume {delta:+#;-#;0}");
        public void ToggleMicMute() => Calls.Add("mic mute");
        public void AdjustMicLevel(int delta) => Calls.Add($"mic level {delta:+#;-#;0}");
    }

    [Fact]
    public void Every_command_ships_with_a_default()
    {
        Assert.All(HotkeyCommand.All, c => Assert.False(string.IsNullOrWhiteSpace(c.DefaultCombo)));
    }

    [Fact]
    public void No_two_defaults_collide()
    {
        var combos = HotkeyCommand.All.Select(c => KeyCombo.Parse(c.DefaultCombo))
            .Select(k => (k.Modifiers, k.VirtualKey));

        Assert.Equal(HotkeyCommand.All.Count, combos.Distinct().Count());
    }

    [Fact]
    public void Every_default_is_a_combination_that_can_be_registered()
    {
        Assert.All(HotkeyCommand.All, c => Assert.True(KeyCombo.TryParse(c.DefaultCombo, out _)));
    }

    [Fact]
    public void Ids_are_unique_because_the_configuration_is_keyed_by_them()
    {
        Assert.Equal(HotkeyCommand.All.Count, HotkeyCommand.All.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Every_command_has_a_display_name()
    {
        Assert.All(HotkeyCommand.All, c => Assert.False(string.IsNullOrWhiteSpace(c.DisplayName)));
    }

    [Theory]
    [InlineData("volume-up", "volume +1")]
    [InlineData("volume-down", "volume -1")]
    // Game is the low end of the scale, so the key named after it steps down.
    [InlineData("balance-game", "balance -10")]
    [InlineData("balance-chat", "balance +10")]
    [InlineData("balance-centre", "balance = 50")]
    [InlineData("mic-mute", "mic mute")]
    [InlineData("mic-up", "mic level +5")]
    [InlineData("mic-down", "mic level -5")]
    public void Runs_what_its_name_says(string id, string expected)
    {
        var recorder = new Recorder();

        HotkeyCommand.All.Single(c => c.Id == id).Run(recorder);

        Assert.Equal([expected], recorder.Calls);
    }

    [Fact]
    public void Covers_every_command_the_settings_window_lists()
    {
        Assert.Equal(8, HotkeyCommand.All.Count);
    }
}
