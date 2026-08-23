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
