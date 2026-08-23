// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;
using OpenInzone.Model;

namespace OpenInzone.Control;

/// <summary>
/// How a snapshot reads in the tray's own language.
/// </summary>
/// <remarks>
/// Here rather than in the tray so it can be tested: the tray targets net8.0-windows, which the
/// test project cannot reference. The panel and the tooltip used to format the battery two
/// different ways - one saying ケース and the other case - because each had grown its own copy.
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
        : state.VolumeMuted ? $"{Volume(state)}（ミュート）"
        : Volume(state);

    /// <summary>The balance on the -5.0 to +5.0 scale INZONE Hub shows beside the raw value.</summary>
    public static string Balance(DeviceSnapshot state) =>
        state.Connected
            ? new MixBalance(MixBalance.Clamp(state.Balance)).ToString()
            : Unavailable;

    public static string MicLevel(DeviceSnapshot state) =>
        !state.Connected ? Unavailable
        : state.MicLevelAvailable ? $"{state.MicLevel}%"
        : "利用不可";

    /// <summary>
    /// Both earbuds and the case for the models that have them, a single reading for the rest. A
    /// part that is not reporting reads as dashes rather than as nought per cent.
    /// </summary>
    public static string Battery(DeviceSnapshot state)
    {
        if (!state.Connected) return Unavailable;

        return state.Battery.HasSeparateBuds
            ? $"L {Percent(state.Battery.Left)}   R {Percent(state.Battery.Right)}   " +
              $"ケース {Percent(state.Battery.Case)}"
            : Percent(state.Battery.Left);
    }

    private static string Percent(int? value) => value is int percent ? $"{percent}%" : Unavailable;
}
