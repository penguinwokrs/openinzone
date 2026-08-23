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
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Client, byte> _clients = new();
    private Task? _acceptLoop;

    /// <summary>Raised on a pipe thread for every command a client sends.</summary>
    public event EventHandler<ClientMessage>? CommandReceived;

    /// <summary>Raised when the accept loop cannot continue, with a message fit for a log.</summary>
    public event EventHandler<string>? Failed;

    public IpcServer(Func<DeviceSnapshot> currentState, string? pipeName = null)
    {
        _currentState = currentState;
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
                return;   // stopping, or the pipe broke before anyone arrived
            }

            var client = new Client(pipe, this);
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

    /// <summary>One connected client: a reader loop, and a write lock so pushes cannot interleave.</summary>
    private sealed class Client(NamedPipeServerStream pipe, IpcServer server) : IDisposable
    {
        private readonly LineChannel _channel = new(pipe);
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private volatile bool _disposed;

        public async Task RunAsync(CancellationToken cancellation)
        {
            try
            {
                await SendAsync(new ServerMessage(ServerMessage.Hello, IpcProtocol.Version,
                    server._currentState()), cancellation).ConfigureAwait(false);

                while (!cancellation.IsCancellationRequested)
                {
                    string? line = await _channel.ReadLineAsync(cancellation).ConfigureAwait(false);
                    if (line is null) break;
                    await HandleAsync(line, cancellation).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // A client going away is ordinary. Nothing here should be able to stop the tray.
            }
            finally
            {
                server.Remove(this);
                Dispose();
            }
        }

        private async Task HandleAsync(string line, CancellationToken cancellation)
        {
            ClientMessage? message;
            try
            {
                message = JsonSerializer.Deserialize(line, IpcJson.Default.ClientMessage);
            }
            catch (JsonException)
            {
                await SendAsync(Error("that was not a command object"), cancellation).ConfigureAwait(false);
                return;
            }

            if (message is null || !IpcCommands.IsKnown(message.Command))
            {
                await SendAsync(Error($"unknown command '{message?.Command}'"), cancellation).ConfigureAwait(false);
                return;
            }

            server.CommandReceived?.Invoke(server, message);
        }

        private static ServerMessage Error(string message) =>
            new(ServerMessage.Error, IpcProtocol.Version, Message: message);

        private Task SendAsync(ServerMessage message, CancellationToken cancellation) =>
            WriteAsync(JsonSerializer.SerializeToUtf8Bytes(message, IpcJson.Default.ServerMessage), cancellation);

        /// <summary>Fire-and-forget push, called by <see cref="Publish"/> from the caller's thread.</summary>
        public void Post(byte[] line) => _ = WriteAsync(line, CancellationToken.None);

        private async Task WriteAsync(byte[] line, CancellationToken cancellation)
        {
            if (_disposed) return;
            try
            {
                await _writeLock.WaitAsync(cancellation).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;   // disposed or cancelled while queued
            }

            try
            {
                if (!_disposed) await _channel.WriteLineAsync(line, cancellation).ConfigureAwait(false);
            }
            catch (Exception)
            {
                server.Remove(this);
                Dispose();   // a broken pipe: drop the client rather than retry
                return;
            }
            finally
            {
                try { _writeLock.Release(); } catch (ObjectDisposedException) { /* disposed under us */ }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _channel.Dispose();
            try { pipe.Dispose(); } catch { /* already gone */ }
        }
    }
}
