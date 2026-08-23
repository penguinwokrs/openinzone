// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;

namespace OpenInzone.Tests.Ipc;

public class DaemonLauncherTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenInzone.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>
    /// The name is how every client finds the daemon. Renaming the assembly without changing it
    /// would leave clients looking for a file that is no longer built, and the only symptom would
    /// be that nothing ever connects.
    /// </summary>
    [Fact]
    public void The_name_it_looks_for_is_the_name_the_daemon_builds()
    {
        string project = Path.Combine(RepositoryRoot(), "src", "OpenInzone.Daemon",
            "OpenInzone.Daemon.csproj");
        string text = File.ReadAllText(project);

        Assert.Contains($"<AssemblyName>{Path.GetFileNameWithoutExtension(DaemonLauncher.ExecutableName)}</AssemblyName>",
            text, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_found_where_there_is_nothing()
    {
        string empty = Directory.CreateTempSubdirectory("openinzone-launcher").FullName;
        try
        {
            Assert.Null(DaemonLauncher.FindIn([empty]));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void The_first_directory_that_holds_a_copy_wins()
    {
        string first = Directory.CreateTempSubdirectory("openinzone-launcher").FullName;
        string second = Directory.CreateTempSubdirectory("openinzone-launcher").FullName;
        try
        {
            File.WriteAllText(Path.Combine(second, DaemonLauncher.ExecutableName), "");
            Assert.Equal(Path.Combine(second, DaemonLauncher.ExecutableName),
                DaemonLauncher.FindIn([first, second]));

            File.WriteAllText(Path.Combine(first, DaemonLauncher.ExecutableName), "");
            Assert.Equal(Path.Combine(first, DaemonLauncher.ExecutableName),
                DaemonLauncher.FindIn([first, second]));
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    /// <summary>
    /// A registry read that is not there, and a path that was never set, both arrive as null. The
    /// search has to walk past them rather than treat them as a directory called nothing.
    /// </summary>
    [Fact]
    public void Directories_that_are_not_there_at_all_are_stepped_over()
    {
        string found = Directory.CreateTempSubdirectory("openinzone-launcher").FullName;
        try
        {
            File.WriteAllText(Path.Combine(found, DaemonLauncher.ExecutableName), "");

            Assert.Equal(Path.Combine(found, DaemonLauncher.ExecutableName),
                DaemonLauncher.FindIn([null, "", found]));
        }
        finally
        {
            Directory.Delete(found, recursive: true);
        }
    }

    /// <summary>A client that has not asked for it must never reach for the installed copy.</summary>
    [Fact]
    public void A_client_that_did_not_ask_for_a_daemon_does_not_start_one()
    {
        using var client = new IpcClient($"openinzone-test-{Guid.NewGuid():N}");
        bool complained = false;
        client.DaemonUnavailable += (_, _) => complained = true;

        client.Start();
        Thread.Sleep(200);

        Assert.False(complained);
        Assert.False(client.IsConnected);
    }
}
