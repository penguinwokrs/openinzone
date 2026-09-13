// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;

namespace OpenInzone.Model;

/// <summary>Identity of the connected product.</summary>
public readonly record struct ModelInfo(
    byte ModelId, byte Destination, ushort SerialNumber, byte ModelColor, byte ModelStatus,
    string DongleSerial, string LeftSerial, string RightSerial)
{
    // The firmware's model ids.
    public const byte InzoneH9 = 0;
    public const byte InzoneH7 = 1;
    public const byte InzoneH3 = 2;
    public const byte InzoneH5 = 3;
    public const byte InzoneBuds = 4;
    public const byte InzoneH9II = 5;
    public const byte InzoneE9 = 6;
    public const byte InzoneH6Air = 7;

    /// <summary>Marketing names, keyed by the firmware's model id.</summary>
    public string Name => ModelId switch
    {
        InzoneH9 => "INZONE H9",
        InzoneH7 => "INZONE H7",
        InzoneH3 => "INZONE H3",
        InzoneH5 => "INZONE H5",
        InzoneBuds => "INZONE Buds",
        InzoneH9II => "INZONE H9 II",
        InzoneE9 => "INZONE E9",
        InzoneH6Air => "INZONE H6 Air",
        _ => $"unknown model ({ModelId})",
    };

    /// <summary>True for the true-wireless models that report per-bud battery levels.</summary>
    public bool IsEarbuds => ModelId is InzoneBuds;

    /// <summary>
    /// Whether the microphone mute can be switched from the computer. INZONE Hub offers the button on
    /// INZONE Buds only; the headsets mute with a button of their own and only report it, and an
    /// H9 II leaves a mute written to it unanswered (#19).
    /// </summary>
    public bool AcceptsMicMute => ModelId is InzoneBuds;

    public static ModelInfo Parse(byte[] p)
    {
        string ReadSerial(int offset) => offset + 8 <= p.Length
            ? Encoding.ASCII.GetString(p, offset, 8).TrimEnd('\0')
            : string.Empty;

        return new ModelInfo(
            p[0], p[1], (ushort)((p[3] << 8) | p[2]), p[4], p[5],
            ReadSerial(6), ReadSerial(14), ReadSerial(22));
    }

    public override string ToString() => Name;
}
