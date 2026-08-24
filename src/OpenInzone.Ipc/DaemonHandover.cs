// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.IO;

namespace OpenInzone.Ipc;

/// <summary>
/// How one daemon hands the headset to a newer one.
/// </summary>
/// <remarks>
/// <para>
/// Only one process may hold the conversation with the headset, and the protocol version is part of
/// the pipe name, so a daemon can only ever serve clients of its own version. Those two facts pull
/// against each other the moment two versions are installed: one daemon per version means two
/// owners, and one daemon overall means whichever started first decides which half of the clients
/// works — and that is usually the older one, because an old client left behind is what started it.
/// </para>
/// <para>
/// So the newest available daemon takes the headset, and the others let go. A daemon serving version
/// N holds a signal named for N; a daemon starting up looks at which versions are being served,
/// stands down if a newer one is there, and asks every older one to stop if it is not.
/// </para>
/// <para>
/// A daemon from before this existed does not listen for the signal and cannot be made to. It is
/// left running rather than killed: its clients would only start it again, and a daemon that shoots
/// at another daemon every ten seconds is worse than two of them reading the same headset. That is
/// one transition — the one out of the version that shipped without this — and it is said out loud
/// in the log rather than passed over.
/// </para>
/// </remarks>
public static class DaemonHandover
{
    /// <summary>Where Windows lists the named pipes that are being served.</summary>
    private const string PipeDirectory = @"\\.\pipe\";

    /// <summary>Names the signal a daemon serving <paramref name="version"/> answers to.</summary>
    public static string StandDownEventName(int version) =>
        $"OpenInzone.Daemon.StandDown.v{version}";

    /// <summary>
    /// The protocol versions being served, read from the pipe names themselves.
    /// </summary>
    /// <remarks>
    /// The pipes are the register of who is serving: a daemon that has one is serving, and one that
    /// has stopped has none. Nothing else has to be kept in step with reality.
    /// </remarks>
    public static IEnumerable<int> VersionsIn(IEnumerable<string> pipeNames, string? userName = null)
    {
        string prefix = IpcProtocol.PipeNamePrefix(userName);

        foreach (string name in pipeNames)
        {
            string leaf = name[(name.LastIndexOf('\\') + 1)..];
            if (!leaf.StartsWith(prefix, StringComparison.Ordinal)) continue;

            if (int.TryParse(leaf[prefix.Length..], out int version)) yield return version;
        }
    }

    /// <summary>
    /// Which versions are being served right now, lowest first. Empty when the question cannot be
    /// asked, which is treated as nobody serving: this decides whether to hand over, and refusing
    /// to start because a directory could not be listed would be the worse mistake.
    /// </summary>
    public static IReadOnlyList<int> Serving(string? userName = null)
    {
        if (!OperatingSystem.IsWindows()) return [];

        try
        {
            return [.. VersionsIn(Directory.GetFiles(PipeDirectory), userName).Distinct().Order()];
        }
        catch (Exception)
        {
            // Listing the pipe directory throws on names this API cannot represent, which are
            // other applications' business and not a reason to refuse to start.
            return [];
        }
    }

    /// <summary>
    /// Asks the daemon serving <paramref name="version"/> to stop.
    /// </summary>
    /// <returns>
    /// False when there is nothing listening for the signal — either the daemon has already gone,
    /// or it is from before this existed and will keep serving.
    /// </returns>
    public static bool AskToStandDown(int version)
    {
        // Checked here rather than left to the caller, as DaemonLauncher does: the analyser cannot
        // see through the try, and suppressing it would hide a real mistake later.
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            if (!EventWaitHandle.TryOpenExisting(StandDownEventName(version), out var handle))
                return false;

            using (handle) return handle.Set();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The signal this build answers to, held for as long as it serves.
    /// </summary>
    /// <remarks>
    /// Manual reset: being asked to stand down is not an event to be consumed by whoever notices it
    /// first, it is a state this daemon is now in.
    /// </remarks>
    public static EventWaitHandle StandDownSignal() => OperatingSystem.IsWindows()
        ? new EventWaitHandle(false, EventResetMode.ManualReset, StandDownEventName(IpcProtocol.Version))
        // Nowhere else runs a daemon; this assembly builds everywhere so that the tests can. An
        // unnamed handle nobody can reach is never signalled, which is the right answer there.
        : new EventWaitHandle(false, EventResetMode.ManualReset);
}
