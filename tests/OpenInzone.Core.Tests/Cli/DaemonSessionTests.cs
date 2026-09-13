// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Cli.Session;
using OpenInzone.Ipc;

namespace OpenInzone.Tests.Cli;

/// <summary>The CLI through a daemon, over a real pipe, with the daemon played by hand.</summary>
public class DaemonSessionTests
{
    private static readonly DeviceSnapshot Connected = new(
        true, "INZONE Buds", 16, 30, false, 50, false, 100, true, new BatterySnapshot(90, 90, null, true));

    /// <summary>
    /// A daemon that refuses the microphone mute at once, and answers the n-th read a moment later
    /// with a microphone value of n, so a test can tell whose read an answer was.
    /// </summary>
    private static IpcServer RefusingDaemon(string pipeName)
    {
        var server = new IpcServer(() => Connected, pipeName);
        int reads = 0;
        server.CommandReceived += (_, message) =>
        {
            if (message.Command == IpcCommands.SetMicMuted)
            {
                server.PublishError("INZONE H9 II mutes its microphone with its own button.");
            }
            else if (message.Command == IpcCommands.Describe)
            {
                byte n = (byte)Interlocked.Increment(ref reads);
                string mic = Convert.ToBase64String([0, n, 0xFF]);
                _ = Task.Delay(300).ContinueWith(_ => server.Publish(new DeviceDetail("", "", "", "", mic, "", null)));
            }
        };
        server.Start();
        return server;
    }

    /// <summary>
    /// The refusal is printed in the daemon's words, not as earbuds that did not answer, and the
    /// next invocation gets its own read rather than the one the refused command left behind (#19).
    /// </summary>
    [Fact]
    public void A_refused_command_says_why_and_leaves_nothing_for_the_next_one()
    {
        string pipeName = $"openinzone-test-{Guid.NewGuid():N}";
        using var server = RefusingDaemon(pipeName);

        using (var refused = DaemonSession.TryConnect(pipeName))
        {
            Assert.NotNull(refused);
            var error = Assert.Throws<InvalidOperationException>(() => refused.SetMicMuted(true));
            Assert.Contains("own button", error.Message, StringComparison.Ordinal);
        }

        using var next = DaemonSession.TryConnect(pipeName);
        Assert.NotNull(next);
        Assert.Equal(2, next.GetMicVolume().Value);
    }
}
