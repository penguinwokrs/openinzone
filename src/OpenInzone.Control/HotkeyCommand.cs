// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control.Resources;
using OpenInzone.Model;

namespace OpenInzone.Control;

/// <summary>
/// What a hotkey can ask the headset to do. Declared as an interface so the catalogue can be
/// exercised without a device: <see cref="DeviceController"/> is the only real implementation.
/// </summary>
public interface IDeviceActions
{
    void AdjustBalance(int delta);
    void SetBalance(int value);
    void AdjustVolume(int delta);
    void ToggleMicMute();
    void AdjustMicLevel(int delta);
}

/// <summary>
/// One assignable command. The catalogue is the single place a command is defined: the settings
/// window lists it, the configuration keys off its id, and the hotkey host registers it. Adding a
/// command means adding one entry here and nothing else.
/// </summary>
/// <param name="Id">
/// Persisted as the key in hotkeys.json and named over IPC and by the Stream Deck plugin. It is an
/// identifier, not a label: it is never translated and never renamed.
/// </param>
/// <param name="Name">
/// Read late rather than stored, so the catalogue - which is static and built once - still answers
/// in whatever language the window is being shown in.
/// </param>
public sealed record HotkeyCommand(string Id, Func<string> Name, string DefaultCombo, Action<IDeviceActions> Run)
{
    public string DisplayName => Name();

    /// <summary>Steps match what INZONE Hub itself moves by: ten for balance, one for volume.</summary>
    public static IReadOnlyList<HotkeyCommand> All { get; } =
    [
        new("volume-up",      () => Strings.Hotkey_VolumeUp,      "Ctrl+Alt+Right",     d => d.AdjustVolume(+1)),
        new("volume-down",    () => Strings.Hotkey_VolumeDown,    "Ctrl+Alt+Left",      d => d.AdjustVolume(-1)),
        // Game is the low end of the scale, so moving towards it is a step down. These were the
        // other way round, which made both keys do the opposite of what they are named.
        new("balance-game",   () => Strings.Hotkey_BalanceGame,   "Ctrl+Alt+Up",        d => d.AdjustBalance(-MixBalance.HubStep)),
        new("balance-chat",   () => Strings.Hotkey_BalanceChat,   "Ctrl+Alt+Down",      d => d.AdjustBalance(+MixBalance.HubStep)),
        new("balance-centre", () => Strings.Hotkey_BalanceCentre, "Ctrl+Alt+Home",      d => d.SetBalance(MixBalance.Centre)),
        new("mic-mute",       () => Strings.Hotkey_MicMute,       "Ctrl+Alt+Shift+M",   d => d.ToggleMicMute()),
        new("mic-up",         () => Strings.Hotkey_MicUp,         "Ctrl+Alt+PageUp",    d => d.AdjustMicLevel(+5)),
        new("mic-down",       () => Strings.Hotkey_MicDown,       "Ctrl+Alt+PageDown",  d => d.AdjustMicLevel(-5)),
    ];

    public static HotkeyCommand? ById(string id) => All.FirstOrDefault(c => c.Id == id);
}
