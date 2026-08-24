// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Protocol;

namespace OpenInzone.Settings;

/// <summary>
/// Every setting this project drives, described once.
/// </summary>
/// <remarks>
/// Before this, the same knowledge was written out five times — a method on the device, a field on
/// the IPC record, a command name, a case in the daemon and a handler in the window — and adding a
/// setting meant getting all five to agree. Here a setting is one entry, and everything else walks
/// the list.
///
/// The order is the order the settings window shows them in, so a client that draws the list as it
/// comes gets a sensible arrangement without a second table saying so.
/// </remarks>
public static class SettingCatalogue
{
    /// <summary>Auto power off answers 0x0F rather than 0x01, and is written back the same way.</summary>
    private const byte AutoPowerOffOn = 0x0F;

    public const string Sidetone = "sidetone";
    public const string AmbientMode = "ambient-mode";
    public const string AmbientLevel = "ambient-level";
    public const string VoiceFocus = "voice-focus";
    public const string AutoPowerOff = "auto-power-off";
    public const string VoiceGuidance = "voice-guidance";
    public const string VoiceGuidanceLanguage = "voice-guidance-language";
    public const string BluetoothAutoSwitch = "bluetooth-auto-switch";

    public static IReadOnlyList<SettingDescriptor> All { get; } =
    [
        // The ambient packet is four bytes: mode, level, a byte the earbuds do not report, and
        // voice focus. Three settings read and write their own byte of it.
        Choice(AmbientMode, EventId.AmbientSetting, maximum: 2, index: 0, packetBytes: 4),
        Range(AmbientLevel, EventId.AmbientSetting, minimum: 1, maximum: 20, index: 1, packetBytes: 4),
        Toggle(VoiceFocus, EventId.AmbientSetting, index: 3, packetBytes: 4),

        Range(Sidetone, EventId.SidetoneVolume, minimum: 0, maximum: 10, index: 0, packetBytes: 2),

        Toggle(AutoPowerOff, EventId.AutoPowerOff, index: 0, packetBytes: 1, onValue: AutoPowerOffOn),
        Toggle(BluetoothAutoSwitch, EventId.IncomingPermission, index: 0, packetBytes: 1),
        Toggle(VoiceGuidance, EventId.Guidance, index: 0, packetBytes: 1),
        Choice(VoiceGuidanceLanguage, EventId.VoicePromptLanguage, maximum: 2, index: 0, packetBytes: 1),
    ];

    public static SettingDescriptor? ById(string id) =>
        All.FirstOrDefault(setting => setting.Id == id);

    /// <summary>The settings carried by one packet, which is how many a single write affects.</summary>
    public static IEnumerable<SettingDescriptor> ForEvent(EventId eventId) =>
        All.Where(setting => setting.EventId == eventId);

    /// <summary>Every distinct packet the settings live in, which is how many reads they cost.</summary>
    public static IEnumerable<EventId> Events => All.Select(setting => setting.EventId).Distinct();

    private static SettingDescriptor Range(
        string id, EventId eventId, int minimum, int maximum, int index, int packetBytes) =>
        new(id, eventId, SettingKind.Range, minimum, maximum, packetBytes,
            param => SettingDescriptor.At(param, index),
            (param, value) => SettingDescriptor.Replacing(param, index, (byte)value));

    private static SettingDescriptor Choice(
        string id, EventId eventId, int maximum, int index, int packetBytes) =>
        new(id, eventId, SettingKind.Choice, 0, maximum, packetBytes,
            param => SettingDescriptor.At(param, index),
            (param, value) => SettingDescriptor.Replacing(param, index, (byte)value));

    private static SettingDescriptor Toggle(
        string id, EventId eventId, int index, int packetBytes, byte onValue = 1) =>
        new(id, eventId, SettingKind.Toggle, 0, 1, packetBytes,
            param => SettingDescriptor.At(param, index) == onValue ? 1 : 0,
            (param, value) => SettingDescriptor.Replacing(param, index, value == 1 ? onValue : (byte)0));
}
