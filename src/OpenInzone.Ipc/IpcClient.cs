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
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private LineChannel? _channel;
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

    /// <summary>Sends a command. Silently does nothing when the tray is not connected.</summary>
    public void Send(string command, int value = 0) => _ = SendAsync(command, value);

    public async Task SendAsync(string command, int value = 0)
    {
        var channel = _channel;
        if (channel is null) return;

        byte[] line = JsonSerializer.SerializeToUtf8Bytes(
            new ClientMessage(command, value), IpcJson.Default.ClientMessage);

        try
        {
            await _writeLock.WaitAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            await channel.WriteLineAsync(line, _stopping.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Drop();   // the tray went away mid-write; the loop will reconnect
        }
        finally
        {
            try { _writeLock.Release(); } catch (ObjectDisposedException) { /* stopping */ }
        }
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
        _channel?.Dispose();
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
