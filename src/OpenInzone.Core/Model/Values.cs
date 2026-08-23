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
/// 0 is all game, 100 is all chat, 50 is centred. INZONE Hub moves in steps of 10.
/// </summary>
/// <remarks>
/// The direction was recorded the wrong way round for a long time - as 0 being chat - and every
/// description of it followed. It was heard, not deduced: raising the value makes chat louder.
/// The tray's own panel has always had it right, with the game icon at the low end of its slider.
///
/// Which is why nothing here reports a signed number to a person any more. "+2.0" only means
/// something once you know which way the scale runs, and that was exactly what was wrong.
/// </remarks>
public readonly record struct MixBalance(byte Value)
{
    public const byte Min = 0;
    public const byte Max = 100;
    public const byte Centre = 50;
    public const byte HubStep = 10;

    /// <summary>
    /// The value on the -5.0 to +5.0 scale INZONE Hub shows. Negative favours game, positive
    /// favours chat. Kept as it was so that anything reading the JSON keeps reading the same
    /// numbers; people are shown <see cref="ToString"/> instead.
    /// </summary>
    public double Notch => (Value - (double)Centre) / HubStep;

    public bool IsCentred => Value == Centre;

    /// <summary>True when the mix leans towards game, which is the low end of the scale.</summary>
    public bool FavoursGame => Value < Centre;

    /// <summary>How far from centre, in the steps INZONE Hub moves by, without a direction.</summary>
    public double Notches => Math.Abs(Notch);

    public static byte Clamp(int value) => (byte)Math.Clamp(value, Min, Max);

    /// <summary>Names the side rather than signing a number: "40 (game 1.0)".</summary>
    public override string ToString() => IsCentred
        ? $"{Value} (centre)"
        : $"{Value} ({(FavoursGame ? "game" : "chat")} {Notches:0.0})";
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

/// <summary>
/// How much of your own voice comes back. 0-10, not a percentage - INZONE Hub's slider runs the
/// same range, and the second byte reads back as <see cref="Unknown"/> on INZONE Buds exactly as
/// the headphone volume's does.
/// </summary>
public readonly record struct SidetoneVolume(byte Value, byte Percent)
{
    public const byte Min = 0;
    public const byte Max = 10;

    public static byte Clamp(int value) => (byte)Math.Clamp(value, Min, Max);

    public static SidetoneVolume Parse(byte[] param) => new(param[0], param[1]);

    public byte[] ToParam() => [Value, Percent];

    public override string ToString() => Value.ToString();
}

/// <summary>What the earbuds do with the world outside them.</summary>
public enum AmbientMode : byte
{
    Off = 0,
    NoiseCancelling = 1,
    Ambient = 2,
}

/// <summary>
/// Noise cancelling, ambient sound and voice focus, which INZONE Buds carries in one packet.
/// </summary>
/// <remarks>
/// The level travels in every mode, including the ones that do not use it, so it is kept rather
/// than folded away: writing a mode change would otherwise silently reset it.
/// </remarks>
public readonly record struct AmbientSetting(AmbientMode Mode, byte Level, bool VoiceFocus)
{
    public const byte MinLevel = 1;
    public const byte MaxLevel = 20;

    public static byte ClampLevel(int level) => (byte)Math.Clamp(level, MinLevel, MaxLevel);

    /// <summary>Four bytes: mode, level, a byte the earbuds do not report, and voice focus.</summary>
    public static AmbientSetting Parse(byte[] param) =>
        new((AmbientMode)param[0], param[1], param.Length > 3 && param[3] == 1);

    public byte[] ToParam() => [(byte)Mode, Level, Unknown.Byte, VoiceFocus ? (byte)1 : (byte)0];

    public override string ToString() => Mode switch
    {
        AmbientMode.Off => VoiceFocus ? "off (voice focus)" : "off",
        AmbientMode.NoiseCancelling => VoiceFocus ? "noise cancelling (voice focus)" : "noise cancelling",
        _ => VoiceFocus ? $"ambient {Level} (voice focus)" : $"ambient {Level}",
    };
}

/// <summary>Which language the earbuds speak their prompts in.</summary>
public enum VoiceGuidanceLanguage : byte
{
    English = 0,
    Chinese = 1,
    Japanese = 2,
}

/// <summary>
/// A setting the headset carries as one byte with a value for on that is not 1.
/// </summary>
/// <remarks>
/// Auto power off answers 0x0F rather than 0x01, which reads like the minutes a headset with a
/// choice of delays would carry - INZONE Buds offers only on and off, so this keeps whatever byte
/// it was told rather than assuming every model means fifteen.
/// </remarks>
public readonly record struct DeviceToggle(byte Value, byte OnValue)
{
    public bool IsOn => Value == OnValue;

    public static DeviceToggle Parse(byte[] param, byte onValue) => new(param[0], onValue);

    public DeviceToggle With(bool on) => this with { Value = on ? OnValue : (byte)0 };

    public byte[] ToParam() => [Value];

    public override string ToString() => IsOn ? "on" : "off";
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
        if (param.Length >= 2)
            return new BatteryInfo(param[0], param[1], Unknown.Byte, Unknown.Byte, Unknown.Byte, Unknown.Byte, false);

        // Shorter than any payload the device has been observed to send. Nothing here throws:
        // notifications are folded on the HID reader thread, so a reading where nothing is
        // reporting is the safe answer, not an exception.
        return new BatteryInfo(Unknown.Byte, Unknown.Byte, Unknown.Byte, Unknown.Byte, Unknown.Byte, Unknown.Byte, false);
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
