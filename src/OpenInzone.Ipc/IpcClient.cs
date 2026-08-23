// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.IO.Pipes;
using System.Text.Json;

namespace OpenInzone.Ipc;

/// <summary>
/// Talks to the tray's <see cref="IpcServer"/>, reconnecting for as long as it is running.
/// </summary>
/// <remarks>
/// The tray may be stopped, restarted or installed after the client - a Stream Deck plugin outlives
/// several tray sessions - so connecting is a loop rather than a step, and callers are told to draw
/// an unavailable state instead of being handed an error to deal with.
/// </remarks>
public sealed class IpcClient : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private const int ConnectTimeoutMilliseconds = 1000;

    private readonly string _pipeName;
    private readonly CancellationTokenSource _stopping = new();
    private NamedPipeClientStream? _pipe;
    private LineChannel? _channel;
    private OutboundQueue? _outbound;
    private Task? _loop;

    /// <summary>Raised for the hello snapshot and every push after it.</summary>
    public event EventHandler<DeviceSnapshot>? SnapshotReceived;

    /// <summary>Raised when the tray rejects something, or speaks a version this build cannot read.</summary>
    public event EventHandler<string>? ServerError;

    /// <summary>Raised when the link comes up and when it goes down, so a client can grey itself out.</summary>
    public event EventHandler<bool>? ConnectionChanged;

    public IpcClient(string? pipeName = null) => _pipeName = pipeName ?? IpcProtocol.PipeName();

    public bool IsConnected => _channel is not null;

    public void Start() => _loop ??= Task.Run(RunAsync);

    /// <summary>
    /// Sends a command, returning false when the tray is not connected. Commands are queued in
    /// the order they are made: turning a dial and then pressing it must not arrive the other way
    /// round, which would centre the balance and then move it off centre again.
    /// </summary>
    public bool Send(string command, int value = 0)
    {
        var outbound = _outbound;
        if (outbound is null) return false;

        return outbound.Post(JsonSerializer.SerializeToUtf8Bytes(
            new ClientMessage(command, value), IpcJson.Default.ClientMessage));
    }

    private async Task RunAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            if (!await ConnectAsync().ConfigureAwait(false))
            {
                try { await Task.Delay(RetryDelay, _stopping.Token).ConfigureAwait(false); }
                catch (Exception) { return; }
                continue;
            }

            ConnectionChanged?.Invoke(this, true);
            try
            {
                await ReadLoopAsync().ConfigureAwait(false);
            }
            finally
            {
                Drop();
                ConnectionChanged?.Invoke(this, false);
            }

            // Wait before trying again even though the link was up a moment ago. A connection
            // that comes up and goes straight back down - a version this build cannot read, a
            // tray shutting down - would otherwise be reconnected to as fast as the loop runs.
            try { await Task.Delay(RetryDelay, _stopping.Token).ConfigureAwait(false); }
            catch (Exception) { return; }
        }
    }

    private async Task<bool> ConnectAsync()
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMilliseconds, _stopping.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return false;   // the tray is not running yet, or every instance is busy
        }

        _pipe = pipe;
        _channel = new LineChannel(pipe);
        _outbound = new OutboundQueue(_channel, _stopping.Token);
        return true;
    }

    private async Task ReadLoopAsync()
    {
        var channel = _channel;
        if (channel is null) return;

        while (!_stopping.IsCancellationRequested)
        {
            string? line = await channel.ReadLineAsync(_stopping.Token).ConfigureAwait(false);
            if (line is null) return;

            ServerMessage? message;
            try
            {
                message = JsonSerializer.Deserialize(line, IpcJson.Default.ServerMessage);
            }
            catch (JsonException)
            {
                continue;   // a line this build cannot read is not a reason to drop the link
            }

            if (message is null) continue;

            switch (message.Type)
            {
                case ServerMessage.Hello when message.Version != IpcProtocol.Version:
                    ServerError?.Invoke(this,
                        $"The tray speaks protocol version {message.Version}; this build speaks " +
                        $"{IpcProtocol.Version}. Update both to the same release.");
                    return;

                case ServerMessage.Hello:
                case ServerMessage.StateUpdate:
                    if (message.State is not null) SnapshotReceived?.Invoke(this, message.State);
                    break;

                case ServerMessage.Error when message.Message is not null:
                    ServerError?.Invoke(this, message.Message);
                    break;
            }
        }
    }

    private void Drop()
    {
        _outbound?.Dispose();
        _outbound = null;
        _channel = null;
        try { _pipe?.Dispose(); } catch { /* already gone */ }
        _pipe = null;
    }

    public void Dispose()
    {
        if (_stopping.IsCancellationRequested) return;
        _stopping.Cancel();
        Drop();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        _stopping.Dispose();
    }
}
