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

    // The same three settings again, with the direction settled by the action rather than by the
    // sign of a step. A key that can only go one way says so on its face, and cannot be configured
    // into going the other.
    public const string VolumeUp = Prefix + ".volumeup";
    public const string VolumeDown = Prefix + ".volumedown";
    public const string MicLevelUp = Prefix + ".miclevelup";
    public const string MicLevelDown = Prefix + ".micleveldown";
    public const string BalanceGame = Prefix + ".balancegame";
    public const string BalanceChat = Prefix + ".balancechat";

    public static readonly string[] All =
    [
        Volume, Balance, MicMute, MicLevel, Battery,
        VolumeUp, VolumeDown, MicLevelUp, MicLevelDown, BalanceGame, BalanceChat,
    ];

    /// <summary>
    /// The setting a directed action moves — its own id for an action that is not directed.
    /// </summary>
    /// <remarks>
    /// A directed action is a pair: which setting, and which way. Everything that is a fact about
    /// the setting — the feature it needs, how far a step goes, what its dial reads — is answered
    /// through here, which is what keeps six new actions from becoming six new cases in every
    /// switch that already names Volume, Balance or MicLevel.
    ///
    /// An id this build does not know is its own subject, so a future action is gated and stepped
    /// as itself rather than as whichever case happened to be written last.
    /// </remarks>
    public static string Subject(string actionId) => actionId switch
    {
        VolumeUp or VolumeDown => Volume,
        MicLevelUp or MicLevelDown => MicLevel,
        BalanceGame or BalanceChat => Balance,
        _ => actionId,
    };

    /// <summary>
    /// Which way a directed action moves its setting: 1 up, -1 down, and 0 for an action that
    /// takes its direction from the sign of the step and from the way a dial is turned.
    /// </summary>
    /// <remarks>
    /// Game is the low end of the balance scale — raising the value makes chat louder — so the key
    /// that says GAME is the one that subtracts. That is the same fact <see cref="KeyFace.Lean"/>
    /// spells out rather than signs, and it reads backwards to anyone who has not met it.
    /// </remarks>
    public static int Direction(string actionId) => actionId switch
    {
        VolumeUp or MicLevelUp or BalanceChat => 1,
        VolumeDown or MicLevelDown or BalanceGame => -1,
        _ => 0,
    };

    /// <summary>
    /// Which of the headset's features a key needs. A model that does not have one gets a key that
    /// reads as nothing and does nothing, rather than one that quietly sends a command the headset
    /// has no answer for — the plugin cannot take a key off a deck, but it can stop pretending.
    /// </summary>
    /// <remarks>
    /// Null for an action this build does not know, which gates it on nothing: whatever it is, it
    /// is not the battery, and saying so would hide it behind a capability it never asked about.
    /// <c>Decide</c> already answers null for such an action on its own.
    /// </remarks>
    public static string? Feature(string actionId) => Subject(actionId) switch
    {
        Volume => FeatureIds.Volume,
        Balance => FeatureIds.Balance,
        MicMute => FeatureIds.MicMute,
        MicLevel => FeatureIds.MicLevel,
        Battery => FeatureIds.Battery,
        _ => null,
    };

    /// <summary>
    /// How far one press moves each setting when the Property Inspector says nothing. Volume runs
    /// 0-30 on the headset itself, so one step is one notch; balance follows INZONE Hub, which
    /// moves in tenths of its -5.0 to +5.0 scale; the microphone level is a percentage.
    /// </summary>
    public static int DefaultStep(string actionId) => Subject(actionId) switch
    {
        Volume => 1,
        Balance => 10,
        MicLevel => 5,
        _ => 0,
    };
}
