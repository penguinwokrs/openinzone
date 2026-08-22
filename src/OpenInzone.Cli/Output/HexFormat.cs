// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Model;

namespace OpenInzone.Cli.Output;

/// <summary>
/// The one place that turns bytes into the spaced hex string the tool shows under `--raw` and for
/// an undecoded watch event. Shared by both renderers, so neither reaches into the other for it.
/// </summary>
internal static class HexFormat
{
    public static string Bytes(byte[] bytes) => string.Join(' ', bytes.Select(b => b.ToString("X2")));

    /// <summary>
    /// What the wire actually carried: two bytes for a headset payload, six for an earbud one.
    /// `BatteryInfo.Parse` synthesises the other four as 0xFF on a headset payload; showing them
    /// would claim the device sent bytes it never did.
    /// </summary>
    public static string Battery(BatteryInfo battery)
    {
        byte[] raw = battery.HasSeparateBuds
            ? [battery.LeftStatus, battery.LeftPercent, battery.RightStatus, battery.RightPercent, battery.CaseStatus, battery.CasePercent]
            : [battery.LeftStatus, battery.LeftPercent];
        return Bytes(raw);
    }
}
