// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;

namespace OpenInzone.Ipc;

/// <summary>
/// Serves the tray's device state to local clients and forwards their commands back.
/// </summary>
/// <remarks>
/// The tray is the only process that opens the headset. Two processes can open the HID interface
/// at once, but <c>HciSession</c> matches replies on (event id, transaction id) and each process
/// numbers its own transactions from one, so concurrent conversations can claim each other's
/// answers. Keeping a single owner and letting everything else ask it removes that class of bug.
/// </remarks>
public sealed class IpcServer : IDisposable
{
    /// <summary>Enough for a deck, a spare client, and room to reconnect before an old pipe drains.</summary>
    private const int MaxClients = 8;

    private readonly Func<DeviceSnapshot> _currentState;
    private readonly Func<DeviceCapabilities?> _currentCapabilities;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Client, byte> _clients = new();
    private Task? _acceptLoop;

    /// <summary>Raised on a pipe thread for every command a client sends.</summary>
    public event EventHandler<ClientMessage>? CommandReceived;

    /// <summary>Raised when the accept loop cannot continue, with a message fit for a log.</summary>
    public event EventHandler<string>? Failed;

    /// <param name="currentCapabilities">
    /// What the connected model has, or null while nothing is connected and there is nothing to
    /// say. A client told nothing offers everything, which is how this project behaved before it
    /// asked the headset at all.
    /// </param>
    public IpcServer(
        Func<DeviceSnapshot> currentState,
        string? pipeName = null,
        Func<DeviceCapabilities?>? currentCapabilities = null)
    {
        _currentState = currentState;
        _currentCapabilities = currentCapabilities ?? (() => null);
        _pipeName = pipeName ?? IpcProtocol.PipeName();
    }

    public int ClientCount => _clients.Count;

    public void Start() => _acceptLoop ??= Task.Run(AcceptLoopAsync);

    /// <summary>Pushes a snapshot to every connected client. Never throws.</summary>
    public void Publish(DeviceSnapshot snapshot)
    {
        if (_clients.IsEmpty) return;
        byte[] line = JsonSerializer.SerializeToUtf8Bytes(
            new ServerMessage(ServerMessage.StateUpdate, IpcProtocol.Version, snapshot),
            IpcJson.Default.ServerMessage);

        foreach (var client in _clients.Keys) client.Post(line);
    }

    /// <summary>
    /// Pushes the device's own answers. Sent to every client rather than only to the one that
    /// asked: the channel has no request/reply, and a detail is a whole reading, so a client that
    /// did not ask for it either wants it or ignores it.
    /// </summary>
    public void Publish(DeviceDetail detail) => Broadcast(
        new ServerMessage(ServerMessage.DetailUpdate, IpcProtocol.Version, Detail: detail));

    /// <summary>Pushes the settings a window shows, after reading them or after one changes.</summary>
    public void Publish(IReadOnlyList<SettingValue> settings) => Broadcast(
        new ServerMessage(ServerMessage.SettingsUpdate, IpcProtocol.Version, Settings: settings));

    /// <summary>
    /// Pushes what the connected model has. Sent on every connect rather than only at hello,
    /// because the answer belongs to the headset that is plugged in: unplugging one model and
    /// plugging in another must not leave a client drawing the first one's controls.
    /// </summary>
    public void Publish(DeviceCapabilities capabilities) => Broadcast(
        new ServerMessage(ServerMessage.CapabilitiesUpdate, IpcProtocol.Version, Capabilities: capabilities));

    /// <summary>Tells every client that something could not be done, in words fit to print.</summary>
    public void PublishError(string message) => Broadcast(
        new ServerMessage(ServerMessage.Error, IpcProtocol.Version, Message: message));

    private void Broadcast(ServerMessage message)
    {
        if (_clients.IsEmpty) return;
        byte[] line = JsonSerializer.SerializeToUtf8Bytes(message, IpcJson.Default.ServerMessage);
        foreach (var client in _clients.Keys) client.Post(line);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, MaxClients,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException)
            {
                // Every instance is busy. Serving the clients already connected matters more than
                // spinning here, so wait for one to free up.
                try { await Task.Delay(500, _stopping.Token).ConfigureAwait(false); } catch { return; }
                continue;
            }
            catch (Exception e)
            {
                Failed?.Invoke(this, $"Could not listen on {_pipeName}: {e.Message}");
                return;
            }

            try
            {
                await pipe.WaitForConnectionAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                // Only shutting down ends the loop. A pipe that broke before anyone arrived is
                // one lost instance, not a reason for the tray to stop serving for the rest of
                // the session - which is what returning unconditionally used to mean.
                if (_stopping.IsCancellationRequested) return;
                continue;
            }

            var client = new Client(pipe, this, _stopping.Token);
            _clients[client] = 0;
            _ = client.RunAsync(_stopping.Token);
        }
    }

    private void Remove(Client client) => _clients.TryRemove(client, out _);

    public void Dispose()
    {
        if (_stopping.IsCancellationRequested) return;
        _stopping.Cancel();
        foreach (var client in _clients.Keys) client.Dispose();
        _clients.Clear();
        try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        _stopping.Dispose();
    }

    /// <summary>One connected client: a reader loop, and a queue that keeps pushes in order.</summary>
    private sealed class Client : IDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly IpcServer _server;
        private readonly LineChannel _channel;
        private readonly OutboundQueue _outbound;
        private volatile bool _disposed;

        public Client(NamedPipeServerStream pipe, IpcServer server, CancellationToken cancellation)
        {
            _pipe = pipe;
            _server = server;
            _channel = new LineChannel(pipe);
            _outbound = new OutboundQueue(_channel, cancellation);
            _outbound.Broken += (_, _) =>
            {
                server.Remove(this);
                Dispose();
            };
        }

        public async Task RunAsync(CancellationToken cancellation)
        {
            try
            {
                Send(new ServerMessage(ServerMessage.Hello, IpcProtocol.Version, _server._currentState(),
                    Capabilities: _server._currentCapabilities()));

                while (!cancellation.IsCancellationRequested)
                {
                    string? line = await _channel.ReadLineAsync(cancellation).ConfigureAwait(false);
                    if (line is null) break;
                    Handle(line);
                }
            }
            catch (Exception)
            {
                // A client going away is ordinary. Nothing here should be able to stop the tray.
            }
            finally
            {
                _server.Remove(this);
                Dispose();
            }
        }

        private void Handle(string line)
        {
            ClientMessage? message;
            try
            {
                message = JsonSerializer.Deserialize(line, IpcJson.Default.ClientMessage);
            }
            catch (JsonException)
            {
                Send(Error("that was not a command object"));
                return;
            }

            if (message is null || !IpcCommands.IsKnown(message.Command))
            {
                Send(Error($"unknown command '{message?.Command}'"));
                return;
            }

            _server.CommandReceived?.Invoke(_server, message);
        }

        private static ServerMessage Error(string message) =>
            new(ServerMessage.Error, IpcProtocol.Version, Message: message);

        private void Send(ServerMessage message) =>
            Post(JsonSerializer.SerializeToUtf8Bytes(message, IpcJson.Default.ServerMessage));

        /// <summary>Queues a line behind whatever is already waiting, so order is preserved.</summary>
        public void Post(byte[] line) => _outbound.Post(line);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _outbound.Dispose();
            try { _pipe.Dispose(); } catch { /* already gone */ }
        }
    }
}
