// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;
using OpenInzone.Ipc;
using OpenInzone.Model;

namespace OpenInzone.Tests.Ipc;

/// <summary>
/// The detail message exists so that routing the CLI through the daemon cannot change its output.
/// It therefore carries the device's answers unparsed, and these check that they survive the trip
/// and decode to the same values they would have on a direct connection.
/// </summary>
public class DeviceDetailTests
{
    private static readonly DeviceDetail Sample = new(
        Model: Convert.ToBase64String([4, 0, 0x22, 0x11, 0, 0]),
        Battery: Convert.ToBase64String([0, 97, 0, 94, 0, 62]),
        Balance: Convert.ToBase64String([40]),
        Volume: Convert.ToBase64String([0, 16, 53]),
        Mic: Convert.ToBase64String([1, 0xFF, 0xFF]),
        Sidetone: Convert.ToBase64String([3, 30]),
        MicLevel: 75);

    [Fact]
    public void A_detail_survives_the_wire_unchanged()
    {
        var message = new ServerMessage(ServerMessage.DetailUpdate, IpcProtocol.Version, Detail: Sample);

        string json = JsonSerializer.Serialize(message, IpcJson.Default.ServerMessage);
        var back = JsonSerializer.Deserialize(json, IpcJson.Default.ServerMessage);

        Assert.Equal(Sample, back!.Detail);
    }

    [Fact]
    public void The_bytes_decode_to_the_values_a_direct_connection_would_have_given()
    {
        var battery = BatteryInfo.Parse(Convert.FromBase64String(Sample.Battery));
        var volume = HeadphoneVolume.Parse(Convert.FromBase64String(Sample.Volume));
        var mic = MicVolume.Parse(Convert.FromBase64String(Sample.Mic));
        var sidetone = SidetoneVolume.Parse(Convert.FromBase64String(Sample.Sidetone));
        var model = ModelInfo.Parse(Convert.FromBase64String(Sample.Model));

        Assert.Equal(97, battery.Left.Percent);
        Assert.Equal(94, battery.Right.Percent);
        Assert.Equal(62, battery.Case.Percent);
        Assert.Equal(16, volume.Value);
        Assert.False(volume.Muted);
        Assert.True(mic.Muted);
        Assert.False(mic.SupportsLevel);
        Assert.Equal(3, sidetone.Value);
        Assert.Equal("INZONE Buds", model.Name);
    }

    /// <summary>
    /// A model with no adjustable capture endpoint reports no level at all, which has to stay
    /// distinct from a level of zero - the CLI prints null and level_available false for it.
    /// </summary>
    [Fact]
    public void No_capture_endpoint_travels_as_nothing_rather_than_as_zero()
    {
        var none = Sample with { MicLevel = null };

        string json = JsonSerializer.Serialize(none, IpcJson.Default.DeviceDetail);
        var back = JsonSerializer.Deserialize(json, IpcJson.Default.DeviceDetail);

        Assert.Null(back!.MicLevel);
        Assert.DoesNotContain("\"micLevel\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_commands_the_CLI_needs_are_all_recognised()
    {
        Assert.True(IpcCommands.IsKnown(IpcCommands.Describe));
        Assert.True(IpcCommands.IsKnown(IpcCommands.SetVolumeMuted));
        Assert.True(IpcCommands.IsKnown(IpcCommands.ToggleVolumeMute));
    }
}
