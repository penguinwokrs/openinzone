// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json.Serialization;

namespace OpenInzone.StreamDeck;

// The subset of Stream Deck's WebSocket protocol this plugin uses. Field names are fixed by the
// Stream Deck application, so they are spelled out rather than left to a naming policy.

/// <summary>Whatever a key or dial was configured with in its Property Inspector.</summary>
internal sealed record ActionSettings(
    [property: JsonPropertyName("step")] int? Step = null);

internal sealed record InboundPayload(
    [property: JsonPropertyName("settings")] ActionSettings? Settings = null,
    [property: JsonPropertyName("ticks")] int Ticks = 0,
    [property: JsonPropertyName("pressed")] bool Pressed = false,
    // "Keypad" or "Encoder" - which half of a Stream Deck + this instance sits on.
    [property: JsonPropertyName("controller")] string? Controller = null);

internal sealed record InboundEvent(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("action")] string? Action = null,
    [property: JsonPropertyName("context")] string? Context = null,
    [property: JsonPropertyName("device")] string? Device = null,
    [property: JsonPropertyName("payload")] InboundPayload? Payload = null);

internal sealed record RegisterMessage(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("uuid")] string Uuid);

/// <summary>Target 0 means both the hardware key and the on-screen preview.</summary>
internal sealed record TitlePayload(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("target")] int Target = 0);

/// <summary>
/// A null image is how Stream Deck is told to use the picture the manifest gives the state. The
/// serializer drops a null field, and an absent image is the only way to undo a setImage.
/// </summary>
internal sealed record ImagePayload(
    [property: JsonPropertyName("image")] string? Image,
    [property: JsonPropertyName("target")] int Target = 0);

/// <summary>The bar under a dial's readout, as a percentage of its travel.</summary>
internal sealed record Indicator(
    [property: JsonPropertyName("value")] int Value);

internal sealed record FeedbackPayload(
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("indicator")] Indicator? Indicator = null);

internal sealed record ContextMessage<TPayload>(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("payload")] TPayload Payload);

/// <summary>A message with a context and no payload, such as showAlert.</summary>
internal sealed record ContextOnlyMessage(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("context")] string Context);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InboundEvent))]
[JsonSerializable(typeof(RegisterMessage))]
[JsonSerializable(typeof(ContextMessage<TitlePayload>))]
[JsonSerializable(typeof(ContextMessage<ImagePayload>))]
[JsonSerializable(typeof(ContextMessage<FeedbackPayload>))]
[JsonSerializable(typeof(ContextOnlyMessage))]
internal sealed partial class StreamDeckJson : JsonSerializerContext;
