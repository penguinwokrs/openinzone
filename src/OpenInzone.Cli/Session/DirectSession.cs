// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Model;

namespace OpenInzone.Cli.Session;

/// <summary>
/// The headset opened by this process, for when no daemon is holding it.
/// </summary>
internal sealed class DirectSession(InzoneDevice device) : IHeadsetSession
{
    public static IHeadsetSession Open() => new DirectSession(InzoneDevice.Open());

    public ModelInfo GetModelInfo() => device.GetModelInfo();

    public BatteryInfo GetBattery() => device.GetBattery();

    public MixBalance GetMixBalance() => device.GetMixBalance();

    public MixBalance SetMixBalance(int value) => device.SetMixBalance(value);

    public MixBalance AdjustMixBalance(int delta) => device.AdjustMixBalance(delta);

    public HeadphoneVolume GetHeadphoneVolume() => device.GetHeadphoneVolume();

    public HeadphoneVolume SetHeadphoneVolume(int value) => device.SetHeadphoneVolume(value);

    public HeadphoneVolume SetHeadphoneMuted(bool muted) =>
        device.SetHeadphoneVolume(device.GetHeadphoneVolume().Value, muted);

    public HeadphoneVolume ToggleHeadphoneMute() => device.ToggleHeadphoneMute();

    public HeadphoneVolume AdjustHeadphoneVolume(int delta) => device.AdjustHeadphoneVolume(delta);

    public MicVolume GetMicVolume() => device.GetMicVolume();

    public MicVolume SetMicMuted(bool muted) => device.SetMicMuted(muted);

    public MicVolume ToggleMicMute() => device.ToggleMicMute();

    public SidetoneVolume GetSidetoneVolume() => device.GetSidetoneVolume();

    /// <summary>The level lives on the Windows capture endpoint, which may not exist.</summary>
    public int? GetMicLevel()
    {
        try
        {
            return device.GetMicLevel();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public int SetMicLevel(int value) => device.SetMicLevel(value);

    public int AdjustMicLevel(int delta) => device.AdjustMicLevel(delta);

    public void Dispose() => device.Dispose();
}
