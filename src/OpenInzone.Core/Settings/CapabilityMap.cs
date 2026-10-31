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
/// being unknown, which is what sends a caller to probing. The one exception is a model whose short
/// parts have been seen (<see cref="AcceptsShortParts"/>, the INZONE H9 alone for now): its part 3
/// stops exactly where a slot stops, which is how it says it carries fewer settings.
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

    /// <summary>
    /// The two battery widths the protocol records: six bytes on an earbud model, two on a headset
    /// model. Anything else is a part 1 this build cannot account for.
    /// </summary>
    /// <remarks>
    /// Naming them is what makes part 1 refusable at all. Taking whatever is left over as the
    /// battery absorbs any discrepancy, so a model carrying one field this build does not know
    /// would parse without complaint and put the volume, the balance and the sidetone at the wrong
    /// offsets — answering confidently about the wrong ids, which is worse than not answering.
    /// </remarks>
    private static readonly int[] BatteryWidths = [6, 2];

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
    /// <param name="shortParts">
    /// Whether a part 2 or 3 that stops exactly where a slot stops is read as far as it goes. Only for
    /// a model whose short parts have been seen (<see cref="AcceptsShortParts"/>); every other model
    /// has to send the whole part, or the part is refused.
    /// </param>
    public static CapabilityMap Parse(byte[]? part1, byte[]? part2, byte[]? part3, bool shortParts = false)
    {
        var map = new CapabilityMap();
        map.TakePart1(part1);
        map.Take(part2, Part2, shortParts: shortParts);
        map.Take(part3, Part3, shortParts: shortParts);
        return map;
    }

    /// <summary>
    /// The models whose short parts have been seen, and so can be read as far as they go: the
    /// INZONE H9 (model id 0). The H7 shares its dongle, and the H9 II and the rest may well send
    /// short parts too, but nobody has seen their bytes. A short part from a layout this build has
    /// not seen would put slots at the wrong offsets, so another model is added here once someone
    /// has looked at what it sends.
    /// </summary>
    public static bool AcceptsShortParts(ModelInfo model) => model.ModelId == ModelInfo.InzoneH9;

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
        if (!Array.Exists(BatteryWidths, width => width == battery)) return;

        Slice[] layout = [new(EventId.BatteryInfo, battery), .. Part1Tail];
        Take(part, layout, Part1LeadingBytes);
    }

    private void Take(byte[]? part, Slice[] layout, int offset = 0, bool shortParts = false)
    {
        if (part is null) return;

        // How many whole slots the part holds. Where short parts are allowed, a part that ends
        // exactly where a slot ends is a model that carries only the first of them: an INZONE H9's
        // part 3 is seven bytes where Buds' is ten, and what it holds matches the first four slots
        // (the startup mode, the Bluetooth block, auto power off and the LED; the startup mode and
        // auto power off equal what those ids answer alone). The slots past its end are settings it
        // does not have, and are read as if each were all 0xFF. Anywhere else a part that does not
        // hold the whole layout does not add up, and is refused.
        int taken = 0, end = offset;
        while (taken < layout.Length && end + layout[taken].Width <= part.Length)
            end += layout[taken++].Width;

        if (taken == 0 || end != part.Length) return;
        if (taken < layout.Length && !shortParts) return;

        for (int i = 0; i < layout.Length; i++)
        {
            var slice = layout[i];
            if (i < taken)
            {
                _slots[slice.EventId] = part[offset..(offset + slice.Width)];
                offset += slice.Width;
            }
            else
            {
                _slots[slice.EventId] = Enumerable.Repeat(Unknown.Byte, slice.Width).ToArray();
            }
        }
    }
}
