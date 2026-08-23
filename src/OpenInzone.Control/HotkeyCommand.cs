// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

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
public sealed record HotkeyCommand(string Id, string DisplayName, string DefaultCombo, Action<IDeviceActions> Run)
{
    /// <summary>Steps match what INZONE Hub itself moves by: ten for balance, one for volume.</summary>
    public static IReadOnlyList<HotkeyCommand> All { get; } =
    [
        new("volume-up",      "音量を上げる",             "Ctrl+Alt+Right",     d => d.AdjustVolume(+1)),
        new("volume-down",    "音量を下げる",             "Ctrl+Alt+Left",      d => d.AdjustVolume(-1)),
        // Game is the low end of the scale, so moving towards it is a step down. These were the
        // other way round, which made both keys do the opposite of what they are named.
        new("balance-game",   "バランスをゲーム寄りに",   "Ctrl+Alt+Up",        d => d.AdjustBalance(-MixBalance.HubStep)),
        new("balance-chat",   "バランスをチャット寄りに", "Ctrl+Alt+Down",      d => d.AdjustBalance(+MixBalance.HubStep)),
        new("balance-centre", "バランスを中央に",         "Ctrl+Alt+Home",      d => d.SetBalance(MixBalance.Centre)),
        new("mic-mute",       "マイクミュート切り替え",   "Ctrl+Alt+Shift+M",   d => d.ToggleMicMute()),
        new("mic-up",         "マイクレベルを上げる",     "Ctrl+Alt+PageUp",    d => d.AdjustMicLevel(+5)),
        new("mic-down",       "マイクレベルを下げる",     "Ctrl+Alt+PageDown",  d => d.AdjustMicLevel(-5)),
    ];

    public static HotkeyCommand? ById(string id) => All.FirstOrDefault(c => c.Id == id);
}
