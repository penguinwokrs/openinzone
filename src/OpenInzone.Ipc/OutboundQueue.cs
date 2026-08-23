// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Threading.Channels;

namespace OpenInzone.Ipc;

/// <summary>
/// Writes lines to a peer one at a time, in the order they were handed over.
/// </summary>
/// <remarks>
/// A lock around the write would keep them from interleaving but not keep them in order:
/// SemaphoreSlim makes no promise about which waiter it releases next, so two snapshots queued a
/// moment apart can arrive the wrong way round and leave a client showing the older one until
/// something else changes. A single reader draining a queue is what makes the order the same at
/// both ends.
///
/// The queue is bounded. A peer that has stopped reading would otherwise grow it without limit,
/// so it is dropped instead - for a channel whose messages are whole snapshots, a client that
/// cannot keep up has nothing to gain from the backlog anyway.
/// </remarks>
internal sealed class OutboundQueue : IDisposable
{
    private const int Capacity = 64;

    private readonly Channel<byte[]> _lines = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    private readonly Task _pump;
    private bool _disposed;

    /// <summary>Raised once, off the caller's thread, when the peer can no longer be written to.</summary>
    public event EventHandler? Broken;

    public OutboundQueue(LineChannel channel, CancellationToken cancellation) =>
        _pump = Task.Run(() => PumpAsync(channel, cancellation), CancellationToken.None);

    /// <summary>Queues a line. Returns false when the peer is gone or too far behind to catch up.</summary>
    public bool Post(byte[] line)
    {
        if (_disposed) return false;
        if (_lines.Writer.TryWrite(line)) return true;

        Fail();
        return false;
    }

    private async Task PumpAsync(LineChannel channel, CancellationToken cancellation)
    {
        try
        {
            await foreach (byte[] line in _lines.Reader.ReadAllAsync(cancellation).ConfigureAwait(false))
                await channel.WriteLineAsync(line, cancellation).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A broken pipe, or shutting down. Either way there is nothing left to write.
        }
        finally
        {
            Fail();
        }
    }

    private void Fail()
    {
        if (_disposed) return;
        _lines.Writer.TryComplete();
        Broken?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lines.Writer.TryComplete();
        try { _pump.Wait(TimeSpan.FromSeconds(1)); } catch { /* shutting down */ }
    }
}
