// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Net.WebSockets;
using System.Text.Json;

namespace OpenInzone.StreamDeck;

/// <summary>
/// The WebSocket half of a native Stream Deck plugin: connect to the port on the command line,
/// name yourself, then trade JSON events until the application closes the socket.
/// </summary>
internal sealed class StreamDeckConnection(int port, string pluginUuid, string registerEvent) : IDisposable
{
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>Raised for every event the Stream Deck application sends.</summary>
    public event EventHandler<InboundEvent>? EventReceived;

    public async Task RunAsync(CancellationToken cancellation)
    {
        await _socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), cancellation).ConfigureAwait(false);
        await SendAsync(new RegisterMessage(registerEvent, pluginUuid),
            StreamDeckJson.Default.RegisterMessage, cancellation).ConfigureAwait(false);

        var buffer = new byte[16 * 1024];
        var message = new List<byte>();

        while (_socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer, cancellation).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;   // Stream Deck went away; the process is expected to end with it
            }

            if (result.MessageType == WebSocketMessageType.Close) return;

            message.AddRange(buffer.AsSpan(0, result.Count));
            if (message.Count > MaxMessageBytes) { message.Clear(); continue; }
            if (!result.EndOfMessage) continue;

            Dispatch(message);
            message.Clear();
        }
    }

    private void Dispatch(List<byte> message)
    {
        InboundEvent? inbound;
        try
        {
            inbound = JsonSerializer.Deserialize(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(message),
                StreamDeckJson.Default.InboundEvent);
        }
        catch (JsonException)
        {
            return;   // an event from a newer Stream Deck than this build knows about
        }

        if (inbound is not null) EventReceived?.Invoke(this, inbound);
    }

    public Task SetTitleAsync(string context, string title) =>
        SendAsync(new ContextMessage<TitlePayload>("setTitle", context, new TitlePayload(title)),
            StreamDeckJson.Default.ContextMessageTitlePayload, CancellationToken.None);

    public Task SetImageAsync(string context, string image) =>
        SendAsync(new ContextMessage<ImagePayload>("setImage", context, new ImagePayload(image)),
            StreamDeckJson.Default.ContextMessageImagePayload, CancellationToken.None);

    /// <summary>Puts the key back to the picture the manifest gives it.</summary>
    public Task ClearImageAsync(string context) =>
        SendAsync(new ContextMessage<ImagePayload>("setImage", context, new ImagePayload(null)),
            StreamDeckJson.Default.ContextMessageImagePayload, CancellationToken.None);

    public Task SetFeedbackAsync(string context, FeedbackPayload feedback) =>
        SendAsync(new ContextMessage<FeedbackPayload>("setFeedback", context, feedback),
            StreamDeckJson.Default.ContextMessageFeedbackPayload, CancellationToken.None);

    /// <summary>Flashes a warning on the key: used when the tray is not there to take the command.</summary>
    public Task ShowAlertAsync(string context) =>
        SendAsync(new ContextOnlyMessage("showAlert", context),
            StreamDeckJson.Default.ContextOnlyMessage, CancellationToken.None);

    private async Task SendAsync<T>(T message, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type,
        CancellationToken cancellation)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, type);

        try
        {
            await _sendLock.WaitAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellation).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A send failing means the socket is going down; the receive loop will notice and end.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _socket.Dispose();
        _sendLock.Dispose();
    }
}
