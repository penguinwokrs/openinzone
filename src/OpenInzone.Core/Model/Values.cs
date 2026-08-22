// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;

namespace OpenInzone.Model;

/// <summary>Sentinel the firmware uses for "this device does not report the value".</summary>
public static class Unknown
{
    public const byte Byte = 0xFF;
    public static bool Is(byte value) => value == Byte;
}

/// <summary>
/// Balance between the game audio endpoint and the chat audio endpoint.
/// 0 is fully chat, 100 is fully game, 50 is centred. INZONE Hub moves in steps of 10
/// and labels the result -5.0 to +5.0.
/// </summary>
public readonly record struct MixBalance(byte Value)
{
    public const byte Min = 0;
    public const byte Max = 100;
    public const byte Centre = 50;
    public const byte HubStep = 10;

    /// <summary>The same value on the -5.0 to +5.0 scale INZONE Hub shows.</summary>
    public double Notch => (Value - (double)Centre) / HubStep;

    public static byte Clamp(int value) => (byte)Math.Clamp(value, Min, Max);

    public override string ToString() => $"{Value} ({Notch:+0.0;-0.0;0.0})";
}

/// <summary>Headphone volume as tracked by the headset itself, independent of the Windows mixer.</summary>
public readonly record struct HeadphoneVolume(bool Muted, byte Value, byte Percent)
{
    public const byte Min = 0;
    public const byte Max = 30;

    public static byte Clamp(int value) => (byte)Math.Clamp(value, Min, Max);

    public static HeadphoneVolume Parse(byte[] param) => new(param[0] == 1, param[1], param[2]);

    public byte[] ToParam() => [Muted ? (byte)1 : (byte)0, Value, Percent];

    public override string ToString() => $"{Value}/{Max}{(Muted ? " (muted)" : "")}";
}

/// <summary>
/// Microphone level and mute state. INZONE Buds report <see cref="Unknown"/> for the level,
/// meaning only the mute flag is adjustable on this model.
/// </summary>
public readonly record struct MicVolume(bool Muted, byte Value, byte Percent)
{
    public bool SupportsLevel => !Unknown.Is(Value);

    public static MicVolume Parse(byte[] param) => new(param[0] == 1, param[1], param[2]);

    public byte[] ToParam() => [Muted ? (byte)1 : (byte)0, Value, Percent];

    public override string ToString() =>
        SupportsLevel ? $"{Value}{(Muted ? " (muted)" : "")}" : Muted ? "muted" : "unmuted";
}

public readonly record struct SidetoneVolume(byte Value, byte Percent)
{
    public static SidetoneVolume Parse(byte[] param) => new(param[0], param[1]);

    public byte[] ToParam() => [Value, Percent];

    public override string ToString() => Value.ToString();
}

/// <summary>Whether a battery reading is a real value, withheld, or not applicable to this model.</summary>
public enum BatteryPartState
{
    /// <summary>A percentage between 0 and 100.</summary>
    Reporting,

    /// <summary>The part exists but is not reporting: in the case, out of range, or never relayed.</summary>
    NotReporting,

    /// <summary>This model has no such part. Headset models have no separate right earbud or case.</summary>
    Absent,
}

/// <summary>
/// One battery reading. The raw bytes are kept so a value the firmware did not document can still
/// be inspected, while <see cref="Percent"/> stays null unless the reading is usable.
/// </summary>
public readonly record struct BatteryPart(byte RawStatus, byte RawPercent, BatteryPartState State)
{
    public int? Percent => State is BatteryPartState.Reporting ? RawPercent : null;

    public override string ToString() => Percent is int percent ? $"{percent}%" : "--";
}

/// <summary>
/// Charge levels. Earbud models report left, right and case separately; headset models report a single pair.
/// A percentage of 0xFF means the part is not currently reporting — a case that is open, for instance.
/// </summary>
public readonly record struct BatteryInfo(
    byte LeftStatus, byte LeftPercent,
    byte RightStatus, byte RightPercent,
    byte CaseStatus, byte CasePercent,
    bool HasSeparateBuds)
{
    public static BatteryInfo Parse(byte[] param)
    {
        if (param.Length >= 6)
            return new BatteryInfo(param[0], param[1], param[2], param[3], param[4], param[5], true);
        return new BatteryInfo(param[0], param[1], Unknown.Byte, Unknown.Byte, Unknown.Byte, Unknown.Byte, false);
    }

    public BatteryPart Left => Part(LeftStatus, LeftPercent, present: true);

    public BatteryPart Right => Part(RightStatus, RightPercent, HasSeparateBuds);

    public BatteryPart Case => Part(CaseStatus, CasePercent, HasSeparateBuds);

    /// <summary>
    /// The case carries no radio of its own. A reported level was relayed by an earbud the last
    /// time one was docked, so it is a snapshot rather than a live reading.
    /// </summary>
    public bool CaseIsSnapshot => HasSeparateBuds;

    /// <summary>
    /// Never throws: notifications are folded on the HID reader thread, where an exception would
    /// take the connection down. An undocumented percentage is withheld rather than shown.
    /// </summary>
    private static BatteryPart Part(byte status, byte percent, bool present)
    {
        if (!present) return new BatteryPart(status, percent, BatteryPartState.Absent);
        if (percent > 100) return new BatteryPart(status, percent, BatteryPartState.NotReporting);
        return new BatteryPart(status, percent, BatteryPartState.Reporting);
    }

    private static string Format(byte percent) => Unknown.Is(percent) ? "--" : $"{percent}%";

    public override string ToString() => HasSeparateBuds
        ? $"L {Format(LeftPercent)}  R {Format(RightPercent)}  case {Format(CasePercent)}"
        : Format(LeftPercent);
}

/// <summary>Identity of the connected product.</summary>
public readonly record struct ModelInfo(
    byte ModelId, byte Destination, ushort SerialNumber, byte ModelColor, byte ModelStatus,
    string DongleSerial, string LeftSerial, string RightSerial)
{
    /// <summary>Marketing names, keyed by the firmware's model id.</summary>
    public string Name => ModelId switch
    {
        0 => "INZONE H9",
        1 => "INZONE H7",
        2 => "INZONE H3",
        3 => "INZONE H5",
        4 => "INZONE Buds",
        5 => "INZONE H9 II",
        6 => "INZONE E9",
        7 => "INZONE H6 Air",
        _ => $"unknown model ({ModelId})",
    };

    /// <summary>True for the true-wireless models that report per-bud battery levels.</summary>
    public bool IsEarbuds => ModelId is 4;

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
