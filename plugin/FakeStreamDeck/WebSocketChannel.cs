// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace OpenInzone.FakeStreamDeck;

/// <summary>
/// The smallest WebSocket server that can hold a conversation with a Stream Deck plugin.
/// </summary>
/// <remarks>
/// Written against a raw socket rather than HttpListener, which on Windows wants a URL
/// reservation and would put an administrator prompt between a developer and a test run. The
/// plugin only ever sends short text frames, so the parts of the protocol that matter here are
/// the handshake, one frame layout, and unmasking - the client is required to mask, the server
/// is required not to.
/// </remarks>
internal sealed class WebSocketChannel : IDisposable
{
    private const string HandshakeGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener _listener;
    private NetworkStream? _stream;
    private TcpClient? _client;

    public WebSocketChannel()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>The port the plugin is told to connect back to.</summary>
    public int Port { get; }

    /// <summary>Waits for the plugin to connect and completes the upgrade.</summary>
    public async Task AcceptAsync(CancellationToken cancellation)
    {
        _client = await _listener.AcceptTcpClientAsync(cancellation).ConfigureAwait(false);
        _stream = _client.GetStream();

        string request = await ReadRequestAsync(cancellation).ConfigureAwait(false);
        string key = HeaderValue(request, "Sec-WebSocket-Key")
            ?? throw new InvalidOperationException("The plugin did not send a WebSocket handshake.");

        string accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.ASCII.GetBytes(key + HandshakeGuid)));

        byte[] response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n");

        await _stream.WriteAsync(response, cancellation).ConfigureAwait(false);
    }

    private async Task<string> ReadRequestAsync(CancellationToken cancellation)
    {
        var request = new List<byte>();
        var one = new byte[1];

        // Reads to the blank line that ends the headers. A handshake is small and arrives at
        // once, but a byte at a time is what keeps this from consuming the first frame with it.
        while (!EndsWithBlankLine(request))
        {
            int read = await _stream!.ReadAsync(one, cancellation).ConfigureAwait(false);
            if (read == 0) throw new InvalidOperationException("The plugin closed before shaking hands.");
            request.Add(one[0]);
        }

        return Encoding.ASCII.GetString(request.ToArray());
    }

    private static bool EndsWithBlankLine(List<byte> bytes) =>
        bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n';

    private static string? HeaderValue(string request, string name)
    {
        foreach (string line in request.Split("\r\n"))
        {
            if (!line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) continue;
            return line[(name.Length + 1)..].Trim();
        }

        return null;
    }

    /// <summary>The next text message, or null when the plugin has gone.</summary>
    public async Task<string?> ReceiveAsync(CancellationToken cancellation)
    {
        while (true)
        {
            byte[] header = await ReadExactlyAsync(2, cancellation).ConfigureAwait(false);
            if (header.Length == 0) return null;

            int opcode = header[0] & 0x0F;
            bool masked = (header[1] & 0x80) != 0;
            long length = header[1] & 0x7F;

            if (length == 126)
            {
                byte[] extended = await ReadExactlyAsync(2, cancellation).ConfigureAwait(false);
                length = (extended[0] << 8) | extended[1];
            }
            else if (length == 127)
            {
                byte[] extended = await ReadExactlyAsync(8, cancellation).ConfigureAwait(false);
                length = 0;
                foreach (byte b in extended) length = (length << 8) | b;
            }

            byte[] mask = masked
                ? await ReadExactlyAsync(4, cancellation).ConfigureAwait(false)
                : [];
            byte[] payload = await ReadExactlyAsync((int)length, cancellation).ConfigureAwait(false);

            if (masked)
                for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];

            switch (opcode)
            {
                case 0x1: return Encoding.UTF8.GetString(payload);
                case 0x8: return null;              // the plugin is closing
                default: continue;                  // ping, pong, binary: nothing here needs them
            }
        }
    }

    private async Task<byte[]> ReadExactlyAsync(int count, CancellationToken cancellation)
    {
        var buffer = new byte[count];
        int filled = 0;

        while (filled < count)
        {
            int read = await _stream!.ReadAsync(buffer.AsMemory(filled), cancellation).ConfigureAwait(false);
            if (read == 0) return filled == 0 ? [] : throw new InvalidOperationException("Truncated frame.");
            filled += read;
        }

        return buffer;
    }

    /// <summary>Sends one text message, unmasked as a server must.</summary>
    public async Task SendAsync(string message, CancellationToken cancellation)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message);
        var frame = new List<byte> { 0x81 };

        if (payload.Length < 126)
        {
            frame.Add((byte)payload.Length);
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            frame.Add(126);
            frame.Add((byte)(payload.Length >> 8));
            frame.Add((byte)payload.Length);
        }
        else
        {
            frame.Add(127);
            for (int shift = 56; shift >= 0; shift -= 8) frame.Add((byte)(payload.Length >> shift));
        }

        frame.AddRange(payload);
        await _stream!.WriteAsync(frame.ToArray(), cancellation).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _listener.Stop();
    }
}
