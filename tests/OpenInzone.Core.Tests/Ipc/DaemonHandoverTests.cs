// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;

namespace OpenInzone.Tests.Ipc;

/// <summary>
/// Which daemon holds the headset when more than one version is installed. One process has to own
/// the conversation, and the version is in the pipe name, so a daemon can only ever serve its own
/// clients — the choice is really about which half of the clients works. Left to whoever started
/// first it went to the older build, because an old client left behind is exactly what starts one.
/// </summary>
public class DaemonHandoverTests
{
    private static string Pipe(int version, string user = "owner") =>
        $@"\\.\pipe\{IpcProtocol.PipeNamePrefix(user)}{version}";

    /// <summary>
    /// The pipes are the register of who is serving: a daemon that has one is serving, and one that
    /// has stopped has none. Nothing else has to be kept in step with reality.
    /// </summary>
    [Fact]
    public void The_versions_being_served_are_read_off_the_pipes()
    {
        var versions = DaemonHandover.VersionsIn([Pipe(1), Pipe(2), Pipe(17)], "owner");

        Assert.Equal([1, 2, 17], versions);
    }

    [Fact]
    public void A_pipe_belonging_to_something_else_is_not_one_of_ours()
    {
        var versions = DaemonHandover.VersionsIn(
            [@"\\.\pipe\chrome.sync.12345", @"\\.\pipe\OpenInzone.Daemon.SingleInstance.v2", Pipe(2)],
            "owner");

        Assert.Equal([2], versions);
    }

    /// <summary>Another user's daemon is not this session's business, and its pipe says whose it is.</summary>
    [Fact]
    public void Another_users_daemon_is_left_alone()
    {
        var versions = DaemonHandover.VersionsIn([Pipe(1, "someone-else"), Pipe(2, "owner")], "owner");

        Assert.Equal([2], versions);
    }

    /// <summary>
    /// A name that looks like ours but does not end in a number is not a version. Answering with
    /// one would have this build stand down for something it has invented.
    /// </summary>
    [Fact]
    public void A_name_that_is_not_a_version_is_not_read_as_one()
    {
        var versions = DaemonHandover.VersionsIn(
            [$@"\\.\pipe\{IpcProtocol.PipeNamePrefix("owner")}next", Pipe(3)], "owner");

        Assert.Equal([3], versions);
    }

    [Fact]
    public void A_bare_name_reads_the_same_as_a_full_path()
    {
        Assert.Equal(
            DaemonHandover.VersionsIn([Pipe(2)], "owner"),
            DaemonHandover.VersionsIn([IpcProtocol.PipeName("owner")], "owner"));
    }

    /// <summary>
    /// The pipe name and the signal name carry the same version, because one is how a daemon is
    /// found and the other is how it is asked to go.
    /// </summary>
    [Fact]
    public void The_signal_a_daemon_answers_to_names_the_version_it_serves()
    {
        Assert.EndsWith("v2", DaemonHandover.StandDownEventName(2), StringComparison.Ordinal);
        Assert.NotEqual(DaemonHandover.StandDownEventName(1), DaemonHandover.StandDownEventName(2));
    }

    /// <summary>
    /// Nothing is listening for a version that is not being served, and asking says so rather than
    /// throwing: the answer is what tells a starting daemon whether it has been handed the headset
    /// or has to share it.
    /// </summary>
    [Fact]
    public void Asking_a_daemon_that_is_not_there_answers_no()
    {
        Assert.False(DaemonHandover.AskToStandDown(31337));
    }

    /// <summary>
    /// The prefix is what makes the pipes a register rather than only a way to reach one, so it has
    /// to be the pipe name with the version taken off and nothing else.
    /// </summary>
    [Fact]
    public void The_prefix_is_the_pipe_name_without_the_version()
    {
        Assert.Equal(IpcProtocol.PipeName("owner"),
            IpcProtocol.PipeNamePrefix("owner") + IpcProtocol.Version);
    }
}
