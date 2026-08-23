// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;

namespace OpenInzone.Ipc;

/// <summary>
/// Reads and writes newline-delimited UTF-8 over a stream, refusing lines beyond
/// <see cref="IpcProtocol.MaxLineBytes"/>. Owns nothing: the stream is the caller's to close.
/// </summary>
/// <remarks>
/// StreamReader.ReadLineAsync would grow without bound on a peer that never sends a newline, so
/// framing is done here instead: the cap turns that into a closed connection rather than memory
/// the tray cannot reclaim.
/// </remarks>
internal sealed class LineChannel(Stream stream)
{
    // Not pooled. Disposing a channel while a read is outstanding would hand the buffer back
    // while the pending ReadAsync can still write into it, and eight kilobytes per connection -
    // with a handful of connections at most - is not worth that hazard.
    private readonly byte[] _buffer = new byte[8192];
    private readonly List<byte> _pending = [];

    /// <summary>The next line, or null when the peer has gone away or overran the cap.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken cancellation)
    {
        while (true)
        {
            int newline = _pending.IndexOf((byte)'\n');
            if (newline >= 0)
            {
                string line = Encoding.UTF8.GetString(CollectionsMarshalSpan()[..newline]).TrimEnd('\r');
                _pending.RemoveRange(0, newline + 1);
                if (line.Length > 0) return line;
                continue;
            }

            if (_pending.Count > IpcProtocol.MaxLineBytes) return null;

            int read;
            try
            {
                read = await stream.ReadAsync(_buffer, cancellation).ConfigureAwait(false);
            }
            catch (Exception) when (cancellation.IsCancellationRequested)
            {
                return null;
            }
            catch (IOException)
            {
                return null;   // the peer closed the pipe mid-read
            }

            if (read <= 0) return null;
            _pending.AddRange(_buffer.AsSpan(0, read));
        }
    }

    private Span<byte> CollectionsMarshalSpan() =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pending);

    public async Task WriteLineAsync(byte[] utf8Json, CancellationToken cancellation)
    {
        await stream.WriteAsync(utf8Json, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(Newline, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    private static readonly byte[] Newline = "\n"u8.ToArray();
}
