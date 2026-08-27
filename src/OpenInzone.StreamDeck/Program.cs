// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;

namespace OpenInzone.StreamDeck;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--probe")) return await ProbeAsync().ConfigureAwait(false);

        var options = StreamDeckArguments.Parse(args);
        if (options is null)
        {
            await Console.Error.WriteLineAsync(
                "This is a Stream Deck plugin: the Stream Deck application starts it with -port, " +
                "-pluginUUID, -registerEvent and -info. Run it with --probe to check that the " +
                "OpenInzone tray is reachable.").ConfigureAwait(false);
            return 2;
        }

        // Starts the daemon if nothing is holding the headset: the whole point of the plugin
        // talking to a daemon rather than to the tray is that no window has to be open.
        using var tray = new IpcClient(startDaemonIfMissing: true);
        using var deck = new StreamDeckConnection(options.Port, options.PluginUuid, options.RegisterEvent);
        using var host = new PluginHost(deck, tray);

        host.Start();
        tray.Start();

        // Ends when the Stream Deck application closes the socket, which is how a plugin is asked
        // to quit; there is no other shutdown signal.
        await deck.RunAsync(CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Connects to the tray, asks it to re-read the headset, and prints what comes back. The
    /// plugin cannot be exercised without a Stream Deck attached, but the half that talks to the
    /// tray can be, and that is the half carrying the microphone level.
    /// </summary>
    private static async Task<int> ProbeAsync()
    {
        using var tray = new IpcClient(startDaemonIfMissing: true);

        var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DeviceSnapshot? latest = null;
        int received = 0;

        tray.SnapshotReceived += (_, snapshot) =>
        {
            Interlocked.Increment(ref received);
            latest = snapshot;
            arrived.TrySetResult(true);
        };
        tray.ServerError += (_, message) => Console.Error.WriteLine($"daemon: {message}");
        tray.DaemonUnavailable += (_, message) => Console.Error.WriteLine($"daemon: {message}");
        tray.Start();

        Console.WriteLine($"pipe: {IpcProtocol.PipeName()}");
        try
        {
            // Long enough to cover starting the daemon rather than only connecting to one that is
            // already up: a launch, a retry delay, and the daemon opening the headset.
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine(
                $"No answer from {DaemonLauncher.ExecutableName}. Is OpenInzone installed?");
            return 1;
        }

        // The hello carries whatever the tray last knew. Asking it to read the headset again is
        // what distinguishes "the tray has not looked yet" from "the earbuds are not answering".
        tray.Send(IpcCommands.Refresh);
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

        var state = latest ?? DeviceSnapshot.Disconnected;
        Console.WriteLine($"snapshots : {received}");
        Console.WriteLine($"connected : {state.Connected}");
        Console.WriteLine($"model     : {state.Model}");
        Console.WriteLine($"volume    : {state.Volume}/{state.VolumeMax}");
        Console.WriteLine($"balance   : {state.Balance}");
        Console.WriteLine($"mic       : {(state.MicMuted ? "muted" : "live")}");
        Console.WriteLine($"mic level : {(state.MicLevelAvailable ? $"{state.MicLevel}%" : "unavailable")}");
        Console.WriteLine($"battery   : L {state.Battery.Left?.ToString() ?? "--"} " +
                          $"R {state.Battery.Right?.ToString() ?? "--"} " +
                          $"case {state.Battery.Case?.ToString() ?? "--"}");

        if (!state.Connected)
            Console.WriteLine("The tray is reachable but the earbuds are not answering: in the " +
                              "case, out of range, or off.");

        foreach (string actionId in ActionIds.All)
        {
            // A directed key wears the manifest's picture at rest, so the face it has of its own
            // is the reading it answers a press with.
            string face = ActionIds.Direction(actionId) == 0
                ? KeyFace.For(actionId, state)
                : KeyFace.Stepped(actionId, state);

            Console.WriteLine($"  {actionId,-42} {face.Length} chars of SVG");
        }

        return 0;
    }
}

/// <summary>The four arguments Stream Deck starts a native plugin with.</summary>
internal sealed record StreamDeckArguments(int Port, string PluginUuid, string RegisterEvent)
{
    public static StreamDeckArguments? Parse(string[] args)
    {
        int port = 0;
        string? uuid = null, registerEvent = null;

        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "-port": int.TryParse(args[i + 1], out port); break;
                case "-pluginUUID": uuid = args[i + 1]; break;
                case "-registerEvent": registerEvent = args[i + 1]; break;
            }
        }

        return port > 0 && uuid is not null && registerEvent is not null
            ? new StreamDeckArguments(port, uuid, registerEvent)
            : null;
    }
}
