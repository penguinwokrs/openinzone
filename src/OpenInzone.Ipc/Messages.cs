// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json.Serialization;

namespace OpenInzone.Ipc;

/// <summary>
/// The names of the commands a client may send. Kept as strings rather than an enum so an
/// unrecognised command from a newer client is data to be rejected, not a parse failure.
/// </summary>
public static class IpcCommands
{
    public const string Refresh = "refresh";
    public const string AdjustVolume = "adjust-volume";
    public const string SetVolume = "set-volume";
    public const string AdjustBalance = "adjust-balance";
    public const string SetBalance = "set-balance";
    public const string ToggleMicMute = "toggle-mic-mute";
    public const string AdjustMicLevel = "adjust-mic-level";
    public const string SetMicLevel = "set-mic-level";

    public static bool IsKnown(string command) => command is
        Refresh or AdjustVolume or SetVolume or AdjustBalance or SetBalance
        or ToggleMicMute or AdjustMicLevel or SetMicLevel;
}

/// <summary>One battery reading. A null percentage means the part is not reporting, or is absent.</summary>
public sealed record BatterySnapshot(
    [property: JsonPropertyName("left")] int? Left,
    [property: JsonPropertyName("right")] int? Right,
    [property: JsonPropertyName("case")] int? Case,
    [property: JsonPropertyName("hasSeparateBuds")] bool HasSeparateBuds);

/// <summary>
/// Everything a client needs to draw the headset's state.
/// </summary>
/// <remarks>
/// Deliberately not <c>DeviceState</c> from OpenInzone.Control: this shape is a published contract
/// between two separately-installed executables, so it has to be free to stay still while the
/// application's own type changes.
/// </remarks>
public sealed record DeviceSnapshot(
    [property: JsonPropertyName("connected")] bool Connected,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("volume")] int Volume,
    [property: JsonPropertyName("volumeMax")] int VolumeMax,
    [property: JsonPropertyName("volumeMuted")] bool VolumeMuted,
    [property: JsonPropertyName("balance")] int Balance,
    [property: JsonPropertyName("micMuted")] bool MicMuted,
    [property: JsonPropertyName("micLevel")] int MicLevel,
    [property: JsonPropertyName("micLevelAvailable")] bool MicLevelAvailable,
    [property: JsonPropertyName("battery")] BatterySnapshot Battery)
{
    /// <summary>What a client shows before the tray has said anything.</summary>
    public static DeviceSnapshot Disconnected { get; } =
        new(false, "", 0, 30, false, 50, false, 0, false, new BatterySnapshot(null, null, null, false));
}

/// <summary>A message from the tray to a client.</summary>
public sealed record ServerMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("version")] int Version = 0,
    [property: JsonPropertyName("state")] DeviceSnapshot? State = null,
    [property: JsonPropertyName("message")] string? Message = null)
{
    public const string Hello = "hello";
    public const string StateUpdate = "state";
    public const string Error = "error";
}

/// <summary>A message from a client to the tray.</summary>
public sealed record ClientMessage(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("value")] int Value = 0);
