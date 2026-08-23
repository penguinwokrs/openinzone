// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json.Serialization;

namespace OpenInzone.Ipc;

/// <summary>
/// Source-generated serialisation for every message on the wire. The plugin publishes trimmed, so
/// the reflection-based serialiser is switched off project-wide and this context is the only way
/// these types can be read or written.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ServerMessage))]
[JsonSerializable(typeof(ClientMessage))]
[JsonSerializable(typeof(DeviceSnapshot))]
[JsonSerializable(typeof(BatterySnapshot))]
[JsonSerializable(typeof(DeviceDetail))]
[JsonSerializable(typeof(DeviceSettings))]
public sealed partial class IpcJson : JsonSerializerContext;
