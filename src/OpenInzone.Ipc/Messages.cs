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
    public const string SetSidetone = "set-sidetone";
    public const string SetAmbientMode = "set-ambient-mode";
    public const string SetAmbientLevel = "set-ambient-level";
    public const string SetVoiceFocus = "set-voice-focus";
    public const string SetAutoPowerOff = "set-auto-power-off";
    public const string SetVoiceGuidance = "set-voice-guidance";
    public const string SetVoiceGuidanceLanguage = "set-voice-guidance-language";
    public const string SetBluetoothAutoSwitch = "set-bluetooth-auto-switch";

    public static bool IsKnown(string command) => command is
        Refresh or AdjustVolume or SetVolume or AdjustBalance or SetBalance
        or ToggleMicMute or AdjustMicLevel or SetMicLevel
        or SetMicMuted or SetVolumeMuted or ToggleVolumeMute or Describe
        or GetSettings or SetSidetone or SetAmbientMode or SetAmbientLevel or SetVoiceFocus
        or SetAutoPowerOff or SetVoiceGuidance or SetVoiceGuidanceLanguage
        or SetBluetoothAutoSwitch;
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

/// <summary>
/// The settings INZONE Hub also offers, as a window would show them.
/// </summary>
/// <remarks>
/// Kept out of <see cref="DeviceSnapshot"/> on purpose. The snapshot is what every client draws
/// on a key or a slider and is read constantly; these are read when a settings window opens and
/// written when someone changes one, and nothing else wants them.
///
/// Every field is nullable because a model that does not answer for one of these is not an error.
/// INZONE Buds has no wearing detection and no LED; another model may have no ambient sound.
/// </remarks>
public sealed record DeviceSettings(
    [property: JsonPropertyName("sidetone")] int? Sidetone,
    /// 0 off, 1 noise cancelling, 2 ambient sound.
    [property: JsonPropertyName("ambientMode")] int? AmbientMode,
    [property: JsonPropertyName("ambientLevel")] int? AmbientLevel,
    [property: JsonPropertyName("voiceFocus")] bool? VoiceFocus,
    [property: JsonPropertyName("autoPowerOff")] bool? AutoPowerOff,
    [property: JsonPropertyName("voiceGuidance")] bool? VoiceGuidance,
    /// 0 English, 1 Chinese, 2 Japanese.
    [property: JsonPropertyName("voiceGuidanceLanguage")] int? VoiceGuidanceLanguage,
    [property: JsonPropertyName("bluetoothAutoSwitch")] bool? BluetoothAutoSwitch)
{
    /// <summary>Nothing answered for, which is what a client shows before it has asked.</summary>
    public static DeviceSettings None { get; } = new(null, null, null, null, null, null, null, null);
}

/// <summary>A message from the daemon to a client.</summary>
public sealed record ServerMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("version")] int Version = 0,
    [property: JsonPropertyName("state")] DeviceSnapshot? State = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("detail")] DeviceDetail? Detail = null,
    [property: JsonPropertyName("settings")] DeviceSettings? Settings = null)
{
    public const string Hello = "hello";
    public const string StateUpdate = "state";
    public const string Error = "error";
    public const string DetailUpdate = "detail";
    public const string SettingsUpdate = "settings";
}

/// <summary>A message from a client to the tray.</summary>
public sealed record ClientMessage(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("value")] int Value = 0);
