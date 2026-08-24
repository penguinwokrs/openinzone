// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Protocol;

namespace OpenInzone.Settings;

/// <summary>What shape a setting has, which is what tells a client how to draw it.</summary>
public enum SettingKind
{
    /// <summary>On or off. The value is 0 or 1, whatever byte the headset uses for on.</summary>
    Toggle,

    /// <summary>A number between <see cref="SettingDescriptor.Minimum"/> and <see cref="SettingDescriptor.Maximum"/>.</summary>
    Range,

    /// <summary>One of a fixed set, numbered from zero.</summary>
    Choice,
}

/// <summary>
/// One setting, described once.
/// </summary>
/// <remarks>
/// Reading and writing are functions over the event's whole parameter, not over a value of their
/// own. That is what lets three settings share one packet — the ambient one carries mode, level
/// and voice focus together — while each writes only the byte it owns and leaves the rest as the
/// headset reported them. It is also what keeps the bytes a setting does not own, such as the
/// sidetone's second byte, going back untouched.
///
/// Neither function throws. Settings are read on the connection's own thread, where an exception
/// takes the link down, so a reply shorter than expected reads as the bottom of the range.
/// </remarks>
/// <param name="Id">The name this setting travels under, on the wire and in the window's markup.</param>
/// <param name="PacketBytes">How many bytes the packet this setting lives in carries.</param>
public sealed record SettingDescriptor(
    string Id,
    EventId EventId,
    SettingKind Kind,
    int Minimum,
    int Maximum,
    int PacketBytes,
    Func<byte[], int> ReadValue,
    Func<byte[], int, byte[]> WriteValue)
{
    /// <summary>
    /// True when this setting is the whole packet, so a write can be composed outright.
    /// </summary>
    /// <remarks>
    /// Everything else has to start from what the headset last reported: the ambient packet carries
    /// two other settings, and the sidetone's second byte is the headset's own reading, which goes
    /// back untouched. Reading first where there is nothing to preserve would be a round trip spent
    /// for nothing — and one more chance for a bad moment on the link to take the connection down
    /// on the way to ticking a checkbox.
    /// </remarks>
    public bool OwnsPacket => PacketBytes == 1;

    public int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);

    public int Read(byte[] param) => ReadValue(param);

    /// <summary>The parameter to send, built from what the headset last reported.</summary>
    public byte[] Write(byte[] param, int value) => WriteValue(param, Clamp(value));

    /// <summary>Reads a byte the reply may be too short to carry.</summary>
    internal static int At(byte[] param, int index) => index < param.Length ? param[index] : 0;

    /// <summary>Replaces one byte of the reply, leaving every other byte as the headset sent it.</summary>
    internal static byte[] Replacing(byte[] param, int index, byte value)
    {
        var next = new byte[Math.Max(param.Length, index + 1)];
        param.CopyTo(next, 0);
        next[index] = value;
        return next;
    }
}
