// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

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

    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>Long enough for a client that started us to finish connecting.</summary>
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(20);

    private static int Main(string[] args)
    {
        // Whoever gets here first serves; a second copy started by another client at the same
        // moment simply steps aside, and that client's retry finds the first one's pipe.
        using var single = new Mutex(initiallyOwned: true, "OpenInzone.Daemon.SingleInstance", out bool first);
        if (!first)
        {
            Log("another daemon is already serving");
            return 0;
        }

        bool stayResident = args.Contains("--resident");

        using var controller = new DeviceController();
        controller.Failed += (_, message) => Log($"device: {message}");

        using var host = new IpcHost(controller);
        host.Failed += (_, message) => Log($"channel: {message}");
        host.Start();

        // Read once at startup so the first client's hello carries real values rather than an
        // empty state it would have to ask to have filled in.
        controller.Refresh();

        Log($"serving on {OpenInzone.Ipc.IpcProtocol.PipeName()}");
        WaitUntilIdle(host, stayResident);
        Log("no clients left; stopping");
        return 0;
    }

    /// <summary>
    /// Blocks until nothing has been connected for <see cref="IdleTimeout"/>. The grace period at
    /// the start covers the gap between being launched and the launching client connecting.
    /// </summary>
    private static void WaitUntilIdle(IpcHost host, bool stayResident)
    {
        DateTime lastSeen = DateTime.UtcNow + GracePeriod - IdleTimeout;

        while (true)
        {
            Thread.Sleep(IdleCheckInterval);

            if (host.ClientCount > 0)
            {
                lastSeen = DateTime.UtcNow;
                continue;
            }

            if (!stayResident && DateTime.UtcNow - lastSeen >= IdleTimeout) return;
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
