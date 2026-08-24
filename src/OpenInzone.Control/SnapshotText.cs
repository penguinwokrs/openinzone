// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control.Resources;
using OpenInzone.Ipc;
using OpenInzone.Model;

namespace OpenInzone.Control;

/// <summary>
/// How a snapshot reads in the tray's own language.
/// </summary>
/// <remarks>
/// Here rather than in the tray so it can be tested: the tray targets net8.0-windows, which the
/// test project cannot reference. The panel and the tooltip used to format the battery two
/// different ways - one saying ケース and the other Case - because each had grown its own copy.
/// </remarks>
public static class SnapshotText
{
    private const string Unavailable = "--";

    /// <summary>The headset's own volume against its scale, as the panel shows it.</summary>
    public static string Volume(DeviceSnapshot state) =>
        state.Connected ? $"{state.Volume}/{state.VolumeMax}" : Unavailable;

    /// <summary>The same, with the headset's mute called out - the tooltip has room for it.</summary>
    public static string VolumeWithMute(DeviceSnapshot state) =>
        !state.Connected ? Unavailable
        : state.VolumeMuted ? string.Format(Strings.Status_VolumeMuted, Volume(state))
        : Volume(state);

    /// <summary>
    /// Which side the mix leans to, said in words. A signed number needs the reader to know which
    /// way the scale runs, and for a long time every description of that was wrong.
    /// </summary>
    public static string Balance(DeviceSnapshot state)
    {
        if (!state.Connected) return Unavailable;

        var balance = new MixBalance(MixBalance.Clamp(state.Balance));
        return balance.IsCentred
            ? Strings.Status_BalanceCentre
            : string.Format(
                balance.FavoursGame ? Strings.Status_BalanceGame : Strings.Status_BalanceChat,
                balance.Notches);
    }

    public static string MicLevel(DeviceSnapshot state) =>
        !state.Connected ? Unavailable
        : state.MicLevelAvailable ? $"{state.MicLevel}%"
        : Strings.Status_MicUnavailable;

    /// <summary>
    /// Both earbuds and the case for the models that have them, a single reading for the rest. A
    /// part that is not reporting reads as dashes rather than as nought per cent.
    /// </summary>
    public static string Battery(DeviceSnapshot state)
    {
        if (!state.Connected) return Unavailable;

        return state.Battery.HasSeparateBuds
            ? $"L {Percent(state.Battery.Left)}   R {Percent(state.Battery.Right)}   " +
              $"{Strings.Status_BatteryCase} {Percent(state.Battery.Case)}"
            : Percent(state.Battery.Left);
    }

    private static string Percent(int? value) => value is int percent ? $"{percent}%" : Unavailable;
}
