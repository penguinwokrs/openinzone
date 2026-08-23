// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;
using OpenInzone.Ipc;

namespace OpenInzone.Tests.Ipc;

public class IpcProtocolTests
{
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

    [Fact]
    public void Every_named_command_is_recognised()
    {
        string[] all =
        [
            IpcCommands.Refresh, IpcCommands.AdjustVolume, IpcCommands.SetVolume,
            IpcCommands.AdjustBalance, IpcCommands.SetBalance, IpcCommands.ToggleMicMute,
            IpcCommands.AdjustMicLevel, IpcCommands.SetMicLevel,
        ];

        Assert.All(all, command => Assert.True(IpcCommands.IsKnown(command)));
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
