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
    public const string SetMicMuted = "set-mic-muted";
    public const string SetVolumeMuted = "set-volume-muted";
    public const string ToggleVolumeMute = "toggle-volume-mute";

    /// <summary>Asks for the device's own answers, verbatim. Answered with a detail message.</summary>
    public const string Describe = "describe";

    // The settings INZONE Hub also offers. Read together and answered with a settings message,
    // because a window showing all of them wants one round trip rather than eight.
    public const string GetSettings = "get-settings";

    /// <summary>
    /// Writes one setting, named in <see cref="ClientMessage.Setting"/>.
    /// </summary>
    /// <remarks>
    /// One command for every setting, where there used to be one command each. A setting is
    /// described once in the core and everything else walks that list, so adding one stops
    /// touching the wire at all.
    /// </remarks>
    public const string SetSetting = "set-setting";

    public static bool IsKnown(string command) => command is
        Refresh or AdjustVolume or SetVolume or AdjustBalance or SetBalance
        or ToggleMicMute or AdjustMicLevel or SetMicLevel
        or SetMicMuted or SetVolumeMuted or ToggleVolumeMute or Describe
        or GetSettings or SetSetting;
}

/// <summary>
/// The names of the things a headset may or may not have.
/// </summary>
/// <remarks>
/// Declared here rather than taken from the core's setting catalogue because this assembly is the
/// published contract and is all a client needs to reference — the Stream Deck plugin ships
/// trimmed and has no business carrying the HID stack to learn what a setting is called. A test
/// pins the two lists together so they cannot drift apart.
/// </remarks>
public static class FeatureIds
{
    // The panel, which until now was drawn whatever the model reported.
    public const string Balance = "balance";
    public const string Volume = "volume";
    public const string MicMute = "mic-mute";

    /// <summary>The Windows capture endpoint, which is not on the headset's wire at all.</summary>
    public const string MicLevel = "mic-level";

    public const string Battery = "battery";

    // The settings tab.
    public const string Sidetone = "sidetone";
    public const string AmbientMode = "ambient-mode";
    public const string AmbientLevel = "ambient-level";
    public const string VoiceFocus = "voice-focus";
    public const string AutoPowerOff = "auto-power-off";
    public const string VoiceGuidance = "voice-guidance";
    public const string VoiceGuidanceLanguage = "voice-guidance-language";
    public const string BluetoothAutoSwitch = "bluetooth-auto-switch";

    public static IReadOnlyList<string> All { get; } =
    [
        Balance, Volume, MicMute, MicLevel, Battery,
        Sidetone, AmbientMode, AmbientLevel, VoiceFocus,
        AutoPowerOff, VoiceGuidance, VoiceGuidanceLanguage, BluetoothAutoSwitch,
    ];
}

/// <summary>What this model has, as the headset itself reports it.</summary>
/// <remarks>
/// Sent with the hello and again whenever a device connects, because the answer belongs to the
/// headset that is plugged in rather than to the daemon. A feature that is absent is left out of
/// the list; a client that has not been told anything yet is a different case, handled by
/// <see cref="DeviceCapabilityExtensions.Allows"/>.
/// </remarks>
public sealed record DeviceCapabilities(
    [property: JsonPropertyName("features")] IReadOnlyList<string> Features)
{
    public bool Has(string feature) => Features.Contains(feature);
}

public static class DeviceCapabilityExtensions
{
    /// <summary>
    /// Whether to offer a feature. A client that has not been told what the model has — nothing is
    /// connected, or the daemon is an older build — offers everything, which is how this project
    /// behaved before it asked at all. Hiding a control on no information would be worse than
    /// showing one the model turns out not to have.
    /// </summary>
    public static bool Allows(this DeviceCapabilities? capabilities, string feature) =>
        capabilities is null || capabilities.Has(feature);

    /// <summary>The value of one setting, or null when the model did not answer for it.</summary>
    public static int? Value(this IReadOnlyList<SettingValue>? settings, string id) =>
        settings?.FirstOrDefault(setting => setting.Id == id)?.Value;
}

/// <summary>One setting, as the headset now reports it.</summary>
/// <remarks>
/// A list rather than a record of named fields. Every field of that record had to be nullable to
/// keep "the model answered off" apart from "the model did not answer", and adding a setting meant
/// changing the wire. A setting a model does not have is simply not in the list.
/// </remarks>
public sealed record SettingValue(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("value")] int Value);

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

/// <summary>
/// The device's own answers to each setting, base64 of the bytes it sent back.
/// </summary>
/// <remarks>
/// Deliberately unlike <see cref="DeviceSnapshot"/>, which is a shape any client can read without
/// knowing the protocol. This is for a tool that already speaks it - the CLI - and exists so that
/// routing that tool through the daemon cannot change a single byte of its output: it parses these
/// with the same decoders it would have used on its own connection. A client that does not speak
/// the protocol wants the snapshot, not this.
/// </remarks>
public sealed record DeviceDetail(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("battery")] string Battery,
    [property: JsonPropertyName("balance")] string Balance,
    [property: JsonPropertyName("volume")] string Volume,
    [property: JsonPropertyName("mic")] string Mic,
    [property: JsonPropertyName("sidetone")] string Sidetone,
    /// <summary>The Windows capture endpoint, which is not part of the headset's own protocol.</summary>
    [property: JsonPropertyName("micLevel")] int? MicLevel);

/// <summary>A message from the daemon to a client.</summary>
public sealed record ServerMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("version")] int Version = 0,
    [property: JsonPropertyName("state")] DeviceSnapshot? State = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("detail")] DeviceDetail? Detail = null,
    [property: JsonPropertyName("settings")] IReadOnlyList<SettingValue>? Settings = null,
    [property: JsonPropertyName("capabilities")] DeviceCapabilities? Capabilities = null)
{
    public const string Hello = "hello";
    public const string StateUpdate = "state";
    public const string Error = "error";
    public const string DetailUpdate = "detail";
    public const string SettingsUpdate = "settings";
    public const string CapabilitiesUpdate = "capabilities";
}

/// <summary>A message from a client to the tray.</summary>
public sealed record ClientMessage(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("value")] int Value = 0,
    /// <summary>Which setting <see cref="IpcCommands.SetSetting"/> is about.</summary>
    [property: JsonPropertyName("setting")] string? Setting = null);
