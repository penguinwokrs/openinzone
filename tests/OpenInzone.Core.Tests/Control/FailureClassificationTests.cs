// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

public class FailureClassificationTests
{
    [Fact]
    public void A_command_the_headset_does_not_answer_is_a_failed_command()
    {
        Assert.Equal(FailureOutcome.CommandFailed,
            DeviceController.Classify(new TimeoutException(), connected: true, fromCommand: true));
    }

    [Fact]
    public void A_command_the_device_layer_refuses_is_a_failed_command()
    {
        Assert.Equal(FailureOutcome.CommandFailed,
            DeviceController.Classify(new InvalidOperationException(), connected: true, fromCommand: true));
    }

    /// <summary>A microphone mute on a headset (#19): nothing was sent, so the link is not re-read.</summary>
    [Fact]
    public void A_command_the_model_does_not_take_is_refused()
    {
        Assert.Equal(FailureOutcome.Refused,
            DeviceController.Classify(new NotSupportedException(), connected: true, fromCommand: true));
    }

    [Fact]
    public void Transport_io_failing_is_a_lost_link()
    {
        Assert.Equal(FailureOutcome.LinkLost,
            DeviceController.Classify(new IOException(), connected: true, fromCommand: true));
    }

    [Fact]
    public void A_disposed_session_is_a_lost_link()
    {
        Assert.Equal(FailureOutcome.LinkLost,
            DeviceController.Classify(new ObjectDisposedException("session"), connected: true, fromCommand: true));
    }

    [Fact]
    public void A_heartbeat_that_fails_is_a_lost_link()
    {
        Assert.Equal(FailureOutcome.LinkLost,
            DeviceController.Classify(new TimeoutException(), connected: true, fromCommand: false));
    }

    [Fact]
    public void Nothing_connected_is_a_lost_link_whatever_threw()
    {
        Assert.Equal(FailureOutcome.LinkLost,
            DeviceController.Classify(new InvalidOperationException(), connected: false, fromCommand: true));
    }
}
