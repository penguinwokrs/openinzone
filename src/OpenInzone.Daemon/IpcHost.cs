// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;
using OpenInzone.Ipc;

namespace OpenInzone.Daemon;

/// <summary>
/// Serves the headset to every local client: the tray's panel, the CLI, the Stream Deck plugin.
/// </summary>
/// <remarks>
/// The translation in both directions lives here rather than in OpenInzone.Ipc so that the wire
/// format stays independent of <see cref="DeviceState"/>: clients ship as their own executables
/// and may be older builds than the daemon they are talking to.
/// </remarks>
internal sealed class IpcHost : IDisposable
{
    private readonly DeviceController _controller;
    private readonly IpcServer _server;

    /// <summary>Raised when the channel cannot be served, with a message fit for a balloon.</summary>
    public event EventHandler<string>? Failed;

    public IpcHost(DeviceController controller)
    {
        _controller = controller;
        _server = new IpcServer(() => IpcSnapshot.From(controller.State));
        _server.CommandReceived += (_, message) => Execute(message);
        _server.Failed += (_, message) => Failed?.Invoke(this, message);

        // Raised on the controller's worker thread. Publishing does not block on any client, so a
        // deck that has stopped reading cannot hold up the headset.
        controller.StateChanged += (_, state) => _server.Publish(IpcSnapshot.From(state));
    }

    public void Start() => _server.Start();

    /// <summary>How many clients are connected, which is what decides when the daemon may stop.</summary>
    public int ClientCount => _server.ClientCount;

    /// <summary>
    /// Commands are queued onto the controller's worker like any other request, so a client cannot
    /// interleave with the tray's own panel.
    /// </summary>
    private void Execute(ClientMessage message)
    {
        switch (message.Command)
        {
            case IpcCommands.Refresh: _controller.Refresh(); break;
            case IpcCommands.AdjustVolume: _controller.AdjustVolume(message.Value); break;
            case IpcCommands.SetVolume: _controller.SetVolume(message.Value); break;
            case IpcCommands.AdjustBalance: _controller.AdjustBalance(message.Value); break;
            case IpcCommands.SetBalance: _controller.SetBalance(message.Value); break;
            case IpcCommands.ToggleMicMute: _controller.ToggleMicMute(); break;
            case IpcCommands.AdjustMicLevel: _controller.AdjustMicLevel(message.Value); break;
            case IpcCommands.SetMicLevel: _controller.SetMicLevel(message.Value); break;
        }
    }

    public void Dispose() => _server.Dispose();
}
