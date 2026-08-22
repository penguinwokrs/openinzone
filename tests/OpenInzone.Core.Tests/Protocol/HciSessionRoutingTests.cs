// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Protocol;

namespace OpenInzone.Tests.Protocol;

/// <summary>
/// Which endpoint owns which setting. Getting this wrong is the one mistake the hardware does not
/// report: a packet addressed to the wrong end draws no reply at all, so it looks like a timeout
/// rather than an error.
/// </summary>
public class HciSessionRoutingTests
{
    [Theory]
    [InlineData(EventId.ConnectStatus2Ghz)]
    [InlineData(EventId.BootStatus)]
    public void Link_state_lives_on_the_dongle(EventId eventId)
    {
        Assert.Equal(DeviceAddress.Transmitter, HciSession.DestinationFor(eventId));
    }

    [Theory]
    [InlineData(EventId.ModelInfo)]
    [InlineData(EventId.BatteryInfo)]
    [InlineData(EventId.HeadphoneVolume)]
    [InlineData(EventId.GameChatMixBalance)]
    [InlineData(EventId.SidetoneVolume)]
    [InlineData(EventId.MicVolume)]
    public void Everything_else_lives_on_the_earbuds(EventId eventId)
    {
        Assert.Equal(DeviceAddress.Receiver, HciSession.DestinationFor(eventId));
    }
}
