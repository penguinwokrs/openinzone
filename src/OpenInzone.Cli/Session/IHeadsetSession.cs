// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Model;

namespace OpenInzone.Cli.Session;

/// <summary>
/// Everything the commands ask of a headset, whoever is holding it.
/// </summary>
/// <remarks>
/// There are two implementations because there are two situations. When the daemon is running it
/// owns the device, and a second conversation on the same dongle can claim its replies - so the
/// commands go through it. When nothing is running there is nobody to talk over, and opening the
/// device directly is both safe and faster than starting a daemon to ask one question.
/// </remarks>
internal interface IHeadsetSession : IDisposable
{
    ModelInfo GetModelInfo();

    BatteryInfo GetBattery();

    MixBalance GetMixBalance();

    MixBalance SetMixBalance(int value);

    MixBalance AdjustMixBalance(int delta);

    HeadphoneVolume GetHeadphoneVolume();

    HeadphoneVolume SetHeadphoneVolume(int value);

    HeadphoneVolume SetHeadphoneMuted(bool muted);

    HeadphoneVolume ToggleHeadphoneMute();

    HeadphoneVolume AdjustHeadphoneVolume(int delta);

    MicVolume GetMicVolume();

    MicVolume SetMicMuted(bool muted);

    MicVolume ToggleMicMute();

    SidetoneVolume GetSidetoneVolume();

    /// <summary>Null when the model has no capture endpoint whose level can be moved.</summary>
    int? GetMicLevel();

    int SetMicLevel(int value);

    int AdjustMicLevel(int delta);
}
