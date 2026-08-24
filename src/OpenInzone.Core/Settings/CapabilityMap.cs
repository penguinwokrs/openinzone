// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Settings;

/// <summary>
/// What the headset says it has, taken from the three parts it publishes rather than from a table
/// of models or from watching requests time out.
/// </summary>
/// <remarks>
/// <para>
/// <c>0x06</c>-<c>0x08</c> (<c>AllFunctionSettingsPart1</c>-<c>3</c>) return every setting's own
/// parameter bytes, concatenated in event-id order, with every byte 0xFF where the model does not
/// have one. Read from INZONE Buds, every slot equals what that id answers when asked for alone,
/// and every 0xFF slot sits where that id timed out.
/// </para>
/// <para>
/// That matters because a timeout is not an answer. An unsupported id is met with silence, and so
/// is a bad moment on the wireless link, so probing can never tell a setting a model does not have
/// from one that was asked at the wrong moment. The map can.
/// </para>
/// <para>
/// There is one model to hand, so nothing here assumes an offset. Each slot is as wide as that
/// setting's parameter and the parts are walked by those widths. The battery is the only slot whose
/// width varies by model — six bytes on an earbud model, two on a headset model — and it is the
/// only one that can be derived, because everything after it in part 1 is fixed.
/// </para>
/// <para>
/// A part whose length does not add up is refused rather than guessed at: reading it anyway would
/// report settings at the wrong offsets, which is worse than not reading it. Its ids go back to
/// being unknown, which is what sends a caller to probing.
/// </para>
/// </remarks>
public sealed class CapabilityMap
{
    /// <summary>One slot in a part: an id, and how many bytes of the part are its parameter.</summary>
    private readonly record struct Slice(EventId EventId, int Width);

    /// <summary>
    /// Part 1 after its leading byte, which is not accounted for. It reads 0x04 on INZONE Buds,
    /// which is also that model's id — an observation, not a reading, so nothing depends on it.
    /// </summary>
    private const int Part1LeadingBytes = 1;

    /// <summary>The battery takes whatever part 1 has left after these.</summary>
    private static readonly Slice[] Part1Tail =
    [
        new(EventId.HeadphoneVolume, 3),
        new(EventId.GameChatMixBalance, 1),
        new(EventId.SidetoneVolume, 2),
    ];

    private static readonly Slice[] Part2 =
    [
        new(EventId.MicVolume, 3),
        new(EventId.AmbientSetting, 4),
        new(EventId.NoiseCancellingToggle, 3),
    ];

    private static readonly Slice[] Part3 =
    [
        new(EventId.NoiseCancellingStartupMode, 1),

        // Four bytes for the three Bluetooth ids and one more that nothing here identifies. They
        // are kept as one block because nothing in the data separates them, and INZONE Buds
        // answers 0xFF across the whole of it.
        new(EventId.BluetoothStatus, 4),

        new(EventId.AutoPowerOff, 1),
        new(EventId.LedSetting, 1),
        new(EventId.VoicePromptLanguage, 1),
        new(EventId.Guidance, 1),
        new(EventId.ConnectionDestinationMode, 1),
    ];

    /// <summary>The smallest battery reading <see cref="BatteryInfo.Parse"/> can make sense of.</summary>
    private const int SmallestBattery = 2;

    private readonly Dictionary<EventId, byte[]> _slots = [];

    private CapabilityMap() { }

    /// <summary>The ids this build knows how to find in the map, whether or not a model has them.</summary>
    public static IEnumerable<EventId> Covered =>
        new[] { EventId.BatteryInfo }
            .Concat(Part1Tail.Select(slice => slice.EventId))
            .Concat(Part2.Select(slice => slice.EventId))
            .Concat(Part3.Select(slice => slice.EventId));

    /// <summary>
    /// Reads the three parts. Each is independent: one that did not answer, or did not add up,
    /// leaves its own ids unknown and costs the others nothing.
    /// </summary>
    public static CapabilityMap Parse(byte[]? part1, byte[]? part2, byte[]? part3)
    {
        var map = new CapabilityMap();
        map.TakePart1(part1);
        map.Take(part2, Part2);
        map.Take(part3, Part3);
        return map;
    }

    /// <summary>True when not one of the three parts could be read, so there is nothing to go on.</summary>
    public bool IsEmpty => _slots.Count == 0;

    /// <summary>The bytes this model reported for an id, or null when the map does not say.</summary>
    public byte[]? Slot(EventId eventId) => _slots.GetValueOrDefault(eventId);

    /// <summary>
    /// Whether the model has a setting: false when every byte of its slot is 0xFF, and null when
    /// the map does not carry it at all. Unknown is not absent — 0x8E is in none of the parts, and
    /// answering false for it would hide a setting INZONE Buds really has.
    /// </summary>
    public bool? Present(EventId eventId) =>
        Slot(eventId) is { } slot ? Array.Exists(slot, b => !Unknown.Is(b)) : null;

    private void TakePart1(byte[]? part)
    {
        if (part is null) return;

        int fixedWidth = Part1Tail.Sum(slice => slice.Width);
        int battery = part.Length - Part1LeadingBytes - fixedWidth;
        if (battery < SmallestBattery) return;

        Slice[] layout = [new(EventId.BatteryInfo, battery), .. Part1Tail];
        Take(part, layout, Part1LeadingBytes);
    }

    private void Take(byte[]? part, Slice[] layout, int offset = 0)
    {
        if (part is null || part.Length != offset + layout.Sum(slice => slice.Width)) return;

        foreach (var slice in layout)
        {
            _slots[slice.EventId] = part[offset..(offset + slice.Width)];
            offset += slice.Width;
        }
    }
}
