// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;
using OpenInzone.Ipc;

namespace OpenInzone.Daemon;

/// <summary>
/// The one process that opens the headset. Everything else - the tray's panel, the CLI, the
/// Stream Deck plugin - asks it over the local channel.
/// </summary>
/// <remarks>
/// Two processes can hold the HID interface at once, but not the conversation on top of it:
/// replies are matched on a transaction number each process counts from one, so two conversations
/// in flight can claim each other's answers. One owner removes that, and it is also what lets a
/// change made anywhere show up everywhere at once.
///
/// It is started by whichever client first needs it and stops on its own once none are left, so
/// nothing is running when nothing is being controlled. It deliberately holds no hotkeys: those
/// are registered first come, first served, and a second holder is what retired the console daemon
/// this one replaces.
/// </remarks>
internal static class Program
{
    /// <summary>How long to keep the headset open after the last client has gone.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Checked every second rather than every few: the last client's departure is only noticed at
    /// a checkpoint, so a coarser interval would have the daemon stopping measurably sooner than
    /// the timeout it documents.
    /// </summary>
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>Long enough for a client that started us to finish connecting.</summary>
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(20);

    /// <summary>How long to let an older daemon put the headset down before taking it.</summary>
    private static readonly TimeSpan HandoverTimeout = TimeSpan.FromSeconds(3);

    private static int Main(string[] args)
    {
        // Whoever gets here first serves this channel; a second copy started by another client at
        // the same moment simply steps aside, and that client's retry finds the first one's pipe.
        // The name carries the protocol version, so "already serving" means serving the channel
        // this build speaks - a daemon of another version is not one whose pipe our clients could
        // ever use, and standing down for it would strand them for as long as it ran.
        using var single = new Mutex(
            initiallyOwned: true, IpcProtocol.SingleInstanceName(), out bool first);
        if (!first)
        {
            Log("another daemon is already serving this channel");
            return 0;
        }

        // Belt as well as braces: a daemon started just before setup took its mutex would still
        // be holding the file setup is about to replace.
        if (DaemonLauncher.SetupIsRunning())
        {
            Log("an installer is running; standing down");
            return 0;
        }

        // Held for as long as this one serves, so that a newer daemon has something to ask.
        // Taken before looking at the others: a daemon starting at the same moment must be able to
        // see this one, or both would decide they were the newest.
        using var standDown = DaemonHandover.StandDownSignal();
        if (!TakeTheHeadset()) return 0;

        bool stayResident = args.Contains("--resident");

        using var controller = new DeviceController();
        controller.Failed += (_, message) => Log($"device: {message}");

        using var host = new IpcHost(controller);
        host.Failed += (_, message) => Log($"channel: {message}");
        host.Start();

        // Read once at startup so the first client's hello carries real values rather than an
        // empty state it would have to ask to have filled in.
        controller.Refresh();

        Log($"serving on {IpcProtocol.PipeName()}");
        Log(WaitUntilIdle(host, standDown, stayResident)
            ? "a newer daemon has taken over; stopping"
            : "no clients left; stopping");
        return 0;
    }

    /// <summary>
    /// Settles which daemon holds the headset when more than one version is installed.
    /// </summary>
    /// <remarks>
    /// The newest wins. One process has to own the conversation, and the version is in the pipe
    /// name, so a daemon can only ever serve its own clients — which means the choice is really
    /// about which half of the clients works. Left to whoever started first it went to the older
    /// build, because an old client left behind is exactly what starts one.
    /// </remarks>
    /// <returns>False when a newer daemon is serving and this one should not.</returns>
    private static bool TakeTheHeadset()
    {
        var serving = DaemonHandover.Serving();

        if (serving.Any(version => version > IpcProtocol.Version))
        {
            Log("a newer daemon is serving; standing down");
            return false;
        }

        var asked = new List<int>();
        foreach (int older in serving.Where(version => version < IpcProtocol.Version))
        {
            if (DaemonHandover.AskToStandDown(older)) asked.Add(older);
            else Log($"a v{older} daemon is serving and cannot be asked to stop; " +
                     "both will hold the headset until its clients are updated");
        }

        if (asked.Count == 0) return true;

        Log($"asked v{string.Join(", v", asked)} to stand down");
        WaitForHandover(asked);
        return true;
    }

    /// <summary>
    /// Waits for the daemons that were asked to let go, so the device is not opened twice.
    /// </summary>
    /// <remarks>
    /// A daemon that overruns is not waited on for ever: it has already been told, and holding up
    /// every client for a process that is not answering would trade one fault for a worse one.
    /// </remarks>
    private static void WaitForHandover(List<int> asked)
    {
        var deadline = DateTime.UtcNow + HandoverTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var serving = DaemonHandover.Serving();
            if (!asked.Any(serving.Contains)) return;

            Thread.Sleep(IdleCheckInterval);
        }

        Log("an older daemon has not stopped yet; taking the headset anyway");
    }

    /// <summary>
    /// Blocks until nothing has been connected for <see cref="IdleTimeout"/>, or until a newer
    /// daemon asks for the headset. The grace period at the start covers the gap between being
    /// launched and the launching client connecting.
    /// </summary>
    /// <remarks>
    /// Being asked to stand down ends this whatever else is true, including <c>--resident</c> and
    /// clients still connected: those clients are of a version something newer has arrived to
    /// replace, and holding the headset for them would be exactly the fault this is here to fix.
    /// </remarks>
    /// <returns>True when it was a newer daemon that ended it rather than the last client leaving.</returns>
    private static bool WaitUntilIdle(IpcHost host, EventWaitHandle standDown, bool stayResident)
    {
        DateTime lastSeen = DateTime.UtcNow + GracePeriod - IdleTimeout;

        while (true)
        {
            if (standDown.WaitOne(IdleCheckInterval)) return true;

            if (host.ClientCount > 0)
            {
                lastSeen = DateTime.UtcNow;
                continue;
            }

            if (!stayResident && DateTime.UtcNow - lastSeen >= IdleTimeout) return false;
        }
    }

    /// <summary>
    /// Goes to the console when there is one. Started on demand there is no window at all, so this
    /// is for running it by hand while working on it rather than for anyone to read later.
    /// </summary>
    private static void Log(string message)
    {
        try { Console.WriteLine($"inzoned: {message}"); } catch { /* no console attached */ }
    }
}
