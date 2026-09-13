// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Reflection;
using System.Text.Json;
using OpenInzone.Ipc;

namespace OpenInzone.Tests.Ipc;

public class IpcProtocolTests
{
    /// <summary>
    /// The whole name, not just the parts that vary. It is the address two separately-installed
    /// executables agree on, so anything about it changing means nothing connects - and the tests
    /// that only checked the user and the version let a changed prefix through.
    /// </summary>
    [Fact]
    public void The_pipe_is_named_the_same_way_every_build_expects()
    {
        Assert.Equal($"OpenInzone.Daemon.alice.v{IpcProtocol.Version}", IpcProtocol.PipeName("alice"));
    }

    [Fact]
    public void Pipe_name_carries_the_user_so_two_sessions_do_not_collide()
    {
        Assert.NotEqual(IpcProtocol.PipeName("alice"), IpcProtocol.PipeName("bob"));
    }

    [Fact]
    public void Pipe_name_carries_the_protocol_version()
    {
        Assert.EndsWith($".v{IpcProtocol.Version}", IpcProtocol.PipeName("alice"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dev.and\\penguin")]
    [InlineData("a b/c:d")]
    public void Pipe_name_keeps_only_characters_a_pipe_name_may_contain(string userName)
    {
        string name = IpcProtocol.PipeName(userName);

        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain(' ', name);
    }

    [Fact]
    public void A_snapshot_survives_the_wire_unchanged()
    {
        var snapshot = new DeviceSnapshot(true, "INZONE Buds", 16, 30, false, 40, true, 75, true,
            new BatterySnapshot(97, 94, null, true));

        string json = JsonSerializer.Serialize(snapshot, IpcJson.Default.DeviceSnapshot);
        var back = JsonSerializer.Deserialize(json, IpcJson.Default.DeviceSnapshot);

        Assert.Equal(snapshot, back);
    }

    [Fact]
    public void A_battery_part_that_is_not_reporting_stays_null_rather_than_becoming_zero()
    {
        var snapshot = DeviceSnapshot.Disconnected with
        {
            Battery = new BatterySnapshot(null, null, null, true),
        };

        string json = JsonSerializer.Serialize(snapshot, IpcJson.Default.DeviceSnapshot);
        var back = JsonSerializer.Deserialize(json, IpcJson.Default.DeviceSnapshot);

        Assert.Null(back!.Battery.Left);
        Assert.Null(back.Battery.Case);
    }

    [Fact]
    public void A_command_survives_the_wire_unchanged()
    {
        var command = new ClientMessage(IpcCommands.AdjustVolume, -2);

        string json = JsonSerializer.Serialize(command, IpcJson.Default.ClientMessage);

        Assert.Equal(command, JsonSerializer.Deserialize(json, IpcJson.Default.ClientMessage));
    }

    /// <summary>
    /// Both names a daemon claims carry the protocol version, and for the same reason: a build can
    /// only ever use the channel it speaks. The lock did not carry it once, and the effect was that
    /// a daemon of an older version held it against every newer one — whose clients were looking for
    /// a different pipe and so were never served at all. Whoever started first won, which during an
    /// upgrade meant the old build won, silently and for as long as anything kept it alive.
    /// </summary>
    [Fact]
    public void The_lock_a_daemon_holds_names_the_version_it_serves()
    {
        string version = $"v{IpcProtocol.Version}";

        Assert.EndsWith(version, IpcProtocol.SingleInstanceName(), StringComparison.Ordinal);
        Assert.EndsWith(version, IpcProtocol.PipeName("owner"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The lock is not the pipe: one is machine-wide and names the user, the other is per session.
    /// Using one name for both would make a second user's daemon stand down for the first's.
    /// </summary>
    [Fact]
    public void The_lock_is_not_the_pipe()
    {
        Assert.NotEqual(IpcProtocol.PipeName("owner"), IpcProtocol.SingleInstanceName());
        Assert.DoesNotContain("owner", IpcProtocol.SingleInstanceName(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The one command that carries more than a number. Which setting it is about travels beside
    /// the value, so adding a setting no longer adds a command.
    /// </summary>
    [Fact]
    public void A_setting_write_carries_the_setting_it_is_about()
    {
        var command = new ClientMessage(IpcCommands.SetSetting, 14, "ambient-level");

        string json = JsonSerializer.Serialize(command, IpcJson.Default.ClientMessage);

        Assert.Contains("\"setting\":\"ambient-level\"", json);
        Assert.Equal(command, JsonSerializer.Deserialize(json, IpcJson.Default.ClientMessage));
    }

    /// <summary>
    /// A new command is something a client can ask whether the daemon offers, through the list the
    /// hello carries, so it is not the kind of change that strands an older client on a different
    /// pipe. The version stays where the released builds already are.
    /// </summary>
    [Fact]
    public void Adding_a_command_does_not_raise_the_protocol_version()
    {
        Assert.Equal(2, IpcProtocol.Version);
    }

    [Fact]
    public void A_setting_cycle_carries_the_setting_it_is_about()
    {
        var command = new ClientMessage(IpcCommands.CycleSetting, Setting: "ambient-mode");

        string json = JsonSerializer.Serialize(command, IpcJson.Default.ClientMessage);

        Assert.Contains("\"command\":\"cycle-setting\"", json, StringComparison.Ordinal);
        Assert.Contains("\"setting\":\"ambient-mode\"", json, StringComparison.Ordinal);
        Assert.Equal(command, JsonSerializer.Deserialize(json, IpcJson.Default.ClientMessage));
    }

    /// <summary>
    /// A command that is not about a setting says nothing about one, rather than naming an empty
    /// string a daemon would then have to tell apart from a real id.
    /// </summary>
    [Fact]
    public void A_command_that_is_not_about_a_setting_names_none()
    {
        string json = JsonSerializer.Serialize(
            new ClientMessage(IpcCommands.Refresh), IpcJson.Default.ClientMessage);

        Assert.DoesNotContain("setting", json);
    }

    /// <summary>
    /// Read off the class rather than listed by hand: a command named but left out of
    /// <see cref="IpcCommands.IsKnown"/> is rejected at the daemon, and a hand-written list is
    /// exactly the thing that gets forgotten when one is added.
    /// </summary>
    [Fact]
    public void Every_named_command_is_recognised()
    {
        string[] all = [.. typeof(IpcCommands)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];

        Assert.NotEmpty(all);
        Assert.All(all, command => Assert.True(IpcCommands.IsKnown(command), command));
    }

    /// <summary>
    /// The list the hello sends is what a client checks before it uses a newer command, so a
    /// command left out of it is one no client would ever dare send.
    /// </summary>
    [Fact]
    public void Every_named_command_is_in_the_list_the_hello_sends()
    {
        string[] named = [.. typeof(IpcCommands)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];

        Assert.All(named, command => Assert.Contains(command, IpcCommands.All));
    }

    [Fact]
    public void The_list_of_commands_names_each_one_once()
    {
        Assert.Equal(IpcCommands.All.Count, IpcCommands.All.Distinct().Count());
    }

    /// <summary>A hello without a list is from a daemon older than the list, and so older than any command it would have named.</summary>
    [Fact]
    public void A_daemon_that_sent_no_list_offers_no_newer_command()
    {
        Assert.False(IpcCommands.Offered(null, IpcCommands.CycleSetting));
    }

    [Fact]
    public void A_command_in_the_daemons_list_is_offered()
    {
        Assert.True(IpcCommands.Offered(["cycle-setting"], IpcCommands.CycleSetting));
    }

    [Fact]
    public void A_hello_carries_its_list_of_commands_across_the_wire()
    {
        var hello = new ServerMessage(ServerMessage.Hello, IpcProtocol.Version,
            Commands: [IpcCommands.Refresh, IpcCommands.CycleSetting]);

        string json = JsonSerializer.Serialize(hello, IpcJson.Default.ServerMessage);
        var back = JsonSerializer.Deserialize(json, IpcJson.Default.ServerMessage);

        Assert.Contains("\"commands\":[\"refresh\",\"cycle-setting\"]", json, StringComparison.Ordinal);
        Assert.Equal([IpcCommands.Refresh, IpcCommands.CycleSetting], back!.Commands);
    }

    /// <summary>What an older daemon sends, which has to read as no list rather than an empty one.</summary>
    [Fact]
    public void A_hello_without_a_list_of_commands_reads_as_none()
    {
        const string json = "{\"type\":\"hello\",\"version\":2}";

        var back = JsonSerializer.Deserialize(json, IpcJson.Default.ServerMessage);

        Assert.Null(back!.Commands);
    }

    [Theory]
    [InlineData("")]
    [InlineData("reboot")]
    [InlineData("Refresh")]
    public void Anything_else_is_rejected(string command) => Assert.False(IpcCommands.IsKnown(command));

    [Fact]
    public void A_serialised_message_never_contains_the_newline_that_frames_it()
    {
        var message = new ServerMessage(ServerMessage.Error, IpcProtocol.Version,
            Message: "line one\nline two");

        string json = JsonSerializer.Serialize(message, IpcJson.Default.ServerMessage);

        Assert.DoesNotContain('\n', json);
    }
}
