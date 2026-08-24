// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Ipc;

namespace OpenInzone.StreamDeck;

/// <summary>
/// The actions this plugin offers, named exactly as manifest.json declares them.
/// </summary>
/// <remarks>
/// Stream Deck matches an inbound event to an action by this string, so a mismatch between the
/// manifest and this file is silent: keys simply do nothing. A test compares the two.
/// </remarks>
internal static class ActionIds
{
    public const string Prefix = "com.penguinwokrs.openinzone";

    public const string Volume = Prefix + ".volume";
    public const string Balance = Prefix + ".balance";
    public const string MicMute = Prefix + ".micmute";
    public const string MicLevel = Prefix + ".miclevel";
    public const string Battery = Prefix + ".battery";

    public static readonly string[] All = [Volume, Balance, MicMute, MicLevel, Battery];

    /// <summary>
    /// Which of the headset's features a key needs. A model that does not have one gets a key that
    /// reads as nothing and does nothing, rather than one that quietly sends a command the headset
    /// has no answer for — the plugin cannot take a key off a deck, but it can stop pretending.
    /// </summary>
    public static string Feature(string actionId) => actionId switch
    {
        Volume => FeatureIds.Volume,
        Balance => FeatureIds.Balance,
        MicMute => FeatureIds.MicMute,
        MicLevel => FeatureIds.MicLevel,
        _ => FeatureIds.Battery,
    };

    /// <summary>
    /// How far one press moves each setting when the Property Inspector says nothing. Volume runs
    /// 0-30 on the headset itself, so one step is one notch; balance follows INZONE Hub, which
    /// moves in tenths of its -5.0 to +5.0 scale; the microphone level is a percentage.
    /// </summary>
    public static int DefaultStep(string actionId) => actionId switch
    {
        Volume => 1,
        Balance => 10,
        MicLevel => 5,
        _ => 0,
    };
}
