// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

public class HotkeyConfigTests
{
    [Fact]
    public void Default_binds_every_command_to_its_default()
    {
        var config = HotkeyConfig.Default();

        Assert.Equal(HotkeyCommand.All.Count, config.Bindings.Count);
        Assert.All(HotkeyCommand.All, c => Assert.Equal(c.DefaultCombo, config.Bindings[c.Id]));
    }

    [Fact]
    public void Reads_the_current_format()
    {
        var config = HotkeyConfig.FromJson("""
            { "bindings": { "volume-up": "Ctrl+F1", "mic-mute": "Win+M" }, "autostart": true }
            """);

        Assert.Equal("Ctrl+F1", config.Bindings["volume-up"]);
        Assert.Equal("Win+M", config.Bindings["mic-mute"]);
        Assert.True(config.Autostart);
    }

    /// <summary>
    /// The daemon wrote an array of action/delta/value triples. Someone upgrading has one of those
    /// files and should keep the keys they chose.
    /// </summary>
    [Fact]
    public void Migrates_the_daemon_format()
    {
        var config = HotkeyConfig.FromJson("""
            { "bindings": [
                { "keys": "Ctrl+Alt+Up",       "action": "balance",   "delta": 10 },
                { "keys": "Ctrl+Alt+Down",     "action": "balance",   "delta": -10 },
                { "keys": "Ctrl+Alt+Home",     "action": "balance",   "value": 50 },
                { "keys": "Ctrl+F5",           "action": "volume",    "delta": 1 },
                { "keys": "Ctrl+F4",           "action": "volume",    "delta": -1 },
                { "keys": "Ctrl+Alt+Shift+M",  "action": "mic-mute" },
                { "keys": "Ctrl+Alt+PageUp",   "action": "mic-level", "delta": 5 },
                { "keys": "Ctrl+Alt+PageDown", "action": "mic-level", "delta": -5 }
              ] }
            """);

        Assert.Equal("Ctrl+Alt+Up", config.Bindings["balance-game"]);
        Assert.Equal("Ctrl+Alt+Down", config.Bindings["balance-chat"]);
        Assert.Equal("Ctrl+Alt+Home", config.Bindings["balance-centre"]);
        Assert.Equal("Ctrl+F5", config.Bindings["volume-up"]);
        Assert.Equal("Ctrl+F4", config.Bindings["volume-down"]);
        Assert.Equal("Ctrl+Alt+Shift+M", config.Bindings["mic-mute"]);
        Assert.Equal("Ctrl+Alt+PageUp", config.Bindings["mic-up"]);
        Assert.Equal("Ctrl+Alt+PageDown", config.Bindings["mic-down"]);
    }

    /// <summary>The daemon left volume mute unbound; migration must still produce its default.</summary>
    [Fact]
    public void Migration_fills_commands_the_old_file_never_had()
    {
        var config = HotkeyConfig.FromJson("""
            { "bindings": [ { "keys": "Ctrl+Alt+Up", "action": "balance", "delta": 10 } ] }
            """);

        Assert.Equal("Ctrl+Alt+Shift+V", config.Bindings["volume-mute"]);
    }

    [Fact]
    public void Migration_drops_an_action_it_does_not_recognise()
    {
        var config = HotkeyConfig.FromJson("""
            { "bindings": [ { "keys": "Ctrl+Alt+Q", "action": "surround", "delta": 1 } ] }
            """);

        Assert.DoesNotContain("Ctrl+Alt+Q", config.Bindings.Values);
    }

    [Fact]
    public void An_unknown_command_id_is_ignored_rather_than_carried_forward()
    {
        var config = HotkeyConfig.FromJson("""
            { "bindings": { "volume-up": "Ctrl+F1", "surround-toggle": "Ctrl+F2" } }
            """);

        Assert.DoesNotContain("surround-toggle", config.Bindings.Keys);
    }

    [Fact]
    public void An_unbound_command_is_left_unbound()
    {
        var config = HotkeyConfig.FromJson("""{ "bindings": { "volume-up": "" } }""");

        Assert.Equal("", config.Bindings["volume-up"]);
    }

    [Fact]
    public void A_top_level_array_raises_invalid_data_exception()
    {
        Assert.Throws<InvalidDataException>(() => HotkeyConfig.FromJson("[]"));
    }

    [Fact]
    public void A_top_level_scalar_raises_invalid_data_exception()
    {
        Assert.Throws<InvalidDataException>(() => HotkeyConfig.FromJson("42"));
    }

    [Fact]
    public void A_bindings_value_of_the_wrong_shape_leaves_every_command_at_its_default()
    {
        var config = HotkeyConfig.FromJson("""{ "bindings": "oops" }""");

        Assert.All(HotkeyCommand.All, c => Assert.Equal(c.DefaultCombo, config.Bindings[c.Id]));
    }

    [Fact]
    public void Round_trips_through_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openinzone-test-{Guid.NewGuid():N}.json");
        try
        {
            var written = HotkeyConfig.Default();
            written.Bindings["volume-up"] = "Ctrl+F9";
            written.Autostart = true;
            written.Save(path);

            var read = HotkeyConfig.LoadOrCreate(path);

            Assert.Equal("Ctrl+F9", read.Bindings["volume-up"]);
            Assert.True(read.Autostart);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // These are the shapes App.LoadConfig has to survive: a hand-edited or half-written file must
    // cost the user a balloon, not their tray icon. Loading is what throws; the catch is in the tray.
    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("\"nope\"")]
    [InlineData("{\"bindings\":{\"volume-up\":7}}")]
    [InlineData("{\"bindings\":[{\"keys\":\"Ctrl+F9\",\"action\":\"volume\",\"delta\":\"lots\"}]}")]
    [InlineData("{\"autostart\":\"yes\"}")]
    public void A_malformed_file_is_reported_rather_than_quietly_accepted(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"openinzone-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            Assert.ThrowsAny<Exception>(() => HotkeyConfig.LoadOrCreate(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Saving_over_an_existing_file_leaves_no_scratch_file_behind()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openinzone-test-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "hotkeys.json");
        try
        {
            HotkeyConfig.Default().Save(path);

            var second = HotkeyConfig.Default();
            second.Bindings["volume-up"] = "Ctrl+F10";
            second.Save(path);

            Assert.Equal(["hotkeys.json"], Directory.GetFiles(directory).Select(Path.GetFileName));
            Assert.Equal("Ctrl+F10", HotkeyConfig.LoadOrCreate(path).Bindings["volume-up"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Writes_the_defaults_when_there_is_no_file_yet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openinzone-test-{Guid.NewGuid():N}", "hotkeys.json");
        try
        {
            var config = HotkeyConfig.LoadOrCreate(path);

            Assert.True(File.Exists(path));
            Assert.Equal(HotkeyCommand.All.Count, config.Bindings.Count);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
