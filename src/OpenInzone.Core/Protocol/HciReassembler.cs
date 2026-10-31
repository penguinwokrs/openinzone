// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;

namespace OpenInzone.Protocol;

/// <summary>
/// Cuts a stream of bytes into packets.
/// </summary>
/// <remarks>
/// A transport hands back whatever arrived: a whole report on HID, any number of bytes on a serial
/// port. So a packet can come in pieces, several can come at once, and the stream can begin in the
/// middle of one. Plain managed code, so it is checked wherever the tests run.
/// </remarks>
public sealed class HciReassembler
{
    private readonly List<byte> _buffer = [];

    /// <summary>Takes the next bytes off the transport and returns the packets they completed.</summary>
    public List<HciPacket> Add(byte[] bytes)
    {
        var packets = new List<HciPacket>();
        _buffer.AddRange(bytes);

        while (_buffer.Count > 0)
        {
            var span = CollectionsMarshal.AsSpan(_buffer);
            int? total = HciPacket.PeekLength(span);

            if (total is null) break;              // need more bytes before the length is known
            if (total < 0)                          // not a packet start: drop a byte and resynchronise
            {
                _buffer.RemoveAt(0);
                continue;
            }
            if (_buffer.Count < total.Value) break;

            var packet = HciPacket.Parse(span[..total.Value]);
            _buffer.RemoveRange(0, total.Value);
            if (packet is not null) packets.Add(packet);
        }

        return packets;
    }
}
