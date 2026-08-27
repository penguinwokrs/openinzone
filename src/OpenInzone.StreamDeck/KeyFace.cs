// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;
using OpenInzone.Ipc;

namespace OpenInzone.StreamDeck;

/// <summary>
/// Draws the face of a key as an SVG data URI.
/// </summary>
/// <remarks>
/// Stream Deck accepts SVG for setImage, which is what keeps a drawing library - and the megabytes
/// that come with it - out of a plugin whose whole job is to show five numbers.
/// </remarks>
internal static class KeyFace
{
    private const int Size = 144;
    private const string Background = "#17171b";
    private const string Dim = "#6f6f78";
    private const string Foreground = "#ffffff";
    private const string Accent = "#4c9aff";
    private const string Warning = "#ff5c5c";

    /// <param name="capabilities">
    /// What the model has, or null when nothing has said — which draws everything, as this plugin
    /// did before it could ask. A key for something the model does not have is drawn as no reading,
    /// the same as a headset that is not answering: from the key's point of view there is nothing
    /// there either way.
    /// </param>
    public static string For(
        string actionId, DeviceSnapshot state, DeviceCapabilities? capabilities = null)
    {
        if (!capabilities.Allows(ActionIds.Feature(actionId))) state = DeviceSnapshot.Disconnected;

        return Face(actionId, state);
    }

    /// <summary>
    /// The face a directed key wears for the moment after it is pressed: the arrow it carries at
    /// rest, kept small and at the top, over the reading the press produced.
    /// </summary>
    /// <remarks>
    /// A directed key is a picture rather than a readout, so this is a confirmation and not a
    /// display - it says what the press did and then gets out of the way. The arrow stays because
    /// it is the only thing telling a pair of these apart, and a key that dropped it while being
    /// held down would leave you reading a number with no idea which way it was going.
    ///
    /// The reading itself is the one the plain key for the same setting shows, word for word.
    /// </remarks>
    public static string Stepped(
        string actionId, DeviceSnapshot state, DeviceCapabilities? capabilities = null)
    {
        if (!capabilities.Allows(ActionIds.Feature(actionId))) state = DeviceSnapshot.Disconnected;

        string subject = ActionIds.Subject(actionId);
        int direction = ActionIds.Direction(actionId);

        // The balance has no up: its key already draws GAME at the left and CHAT at the right, so
        // the arrow points the way the marker is about to move.
        string arrow = subject == ActionIds.Balance ? Sideways(direction) : Upright(direction);

        return subject switch
        {
            ActionIds.Volume => Arrowed(arrow, state.Connected ? $"{state.Volume}" : null,
                state.Connected ? $"/ {state.VolumeMax}" : null),

            ActionIds.MicLevel => Arrowed(arrow, Level(state), state.MicLevelAvailable ? "%" : null),

            ActionIds.Balance => state.Connected
                ? Frame($"""
                    {arrow}
                    <text x="72" y="112" fill="{Foreground}" font-size="30" text-anchor="middle">{Escape(Lean(state.Balance))}</text>
                    """)
                : Arrowed(arrow, null, null),

            _ => Arrowed(arrow, null, null),
        };
    }

    /// <summary>
    /// The class is on the arrow so a test can say which way it points without measuring a path.
    /// Stream Deck neither styles nor cares about it.
    /// </summary>
    private static string Upright(int direction) => direction >= 0
        ? $"""<path class="up" d="M72,22 L88,44 L56,44 Z" fill="{Accent}"/>"""
        : $"""<path class="down" d="M72,44 L56,22 L88,22 Z" fill="{Accent}"/>""";

    private static string Sideways(int direction) => direction >= 0
        ? $"""<path class="right" d="M92,33 L74,21 L74,45 Z" fill="{Accent}"/>"""
        : $"""<path class="left" d="M52,33 L70,21 L70,45 Z" fill="{Accent}"/>""";

    /// <summary>An arrow, a large reading, and a quieter unit after it. A null reading draws "--".</summary>
    private static string Arrowed(string arrow, string? value, string? unit)
    {
        string body = value ?? "--";
        string colour = value is null ? Dim : Foreground;
        string suffix = unit is null || value is null
            ? ""
            : $"""<text x="72" y="128" fill="{Dim}" font-size="18" text-anchor="middle">{Escape(unit)}</text>""";

        return Frame($"""
            {arrow}
            <text x="72" y="102" fill="{colour}" font-size="44" text-anchor="middle">{Escape(body)}</text>
            {suffix}
            """);
    }

    private static string Face(string actionId, DeviceSnapshot state) => actionId switch
    {
        ActionIds.Volume => Reading("VOL", state.Connected ? $"{state.Volume}" : null,
            state.Connected ? $"/ {state.VolumeMax}" : null),
        ActionIds.Balance => Balance(state),
        ActionIds.MicMute => MicMute(state),
        ActionIds.MicLevel => Reading("MIC", Level(state), state.MicLevelAvailable ? "%" : null),
        ActionIds.Battery => Battery(state),
        _ => Reading("", null, null),
    };

    private static string? Level(DeviceSnapshot state) =>
        state is { Connected: true, MicLevelAvailable: true } ? $"{state.MicLevel}" : null;

    private static string Balance(DeviceSnapshot state)
    {
        if (!state.Connected) return Reading("GAME / CHAT", null, null);

        // Game is the low end of the scale: raising the value makes chat louder. This read the
        // other way round, so a key that said GAME was making chat louder.
        //
        // The side is named rather than signed. A reader of "+2.0" has to already know which way
        // the scale runs, which is the very thing that was wrong.
        string label = Lean(state.Balance);
        return Frame($"""
            <text x="72" y="40" fill="{Dim}" font-size="17" text-anchor="middle">{Escape(label)}</text>
            <rect x="18" y="62" width="108" height="8" rx="4" fill="#2c2c33"/>
            <rect x="{18 + state.Balance * 108 / 100 - 4}" y="56" width="8" height="20" rx="3" fill="{Accent}"/>
            <text x="18" y="106" fill="{Dim}" font-size="15">GAME</text>
            <text x="126" y="106" fill="{Dim}" font-size="15" text-anchor="end">CHAT</text>
            """);
    }

    /// <summary>Which side the mix leans to, and by how much, with no sign to misread.</summary>
    internal static string Lean(int balance)
    {
        double notches = Math.Abs(balance - 50) / 10.0;
        return balance == 50 ? "CENTRE" : $"{(balance < 50 ? "GAME" : "CHAT")} {notches:0.0}";
    }

    private static string MicMute(DeviceSnapshot state)
    {
        if (!state.Connected) return Reading("MIC", null, null);

        string colour = state.MicMuted ? Warning : Foreground;
        string slash = state.MicMuted
            ? $"""<line x1="46" y1="42" x2="98" y2="98" stroke="{Warning}" stroke-width="8" stroke-linecap="round"/>"""
            : "";
        return Frame($"""
            <rect x="62" y="34" width="20" height="38" rx="10" fill="{colour}"/>
            <path d="M54,68 a18,18 0 0 0 36,0" fill="none" stroke="{colour}" stroke-width="7" stroke-linecap="round"/>
            <line x1="72" y1="86" x2="72" y2="98" stroke="{colour}" stroke-width="7" stroke-linecap="round"/>
            {slash}
            <text x="72" y="126" fill="{Dim}" font-size="16" text-anchor="middle">{(state.MicMuted ? "MUTED" : "LIVE")}</text>
            """);
    }

    private static string Battery(DeviceSnapshot state)
    {
        if (!state.Connected) return Reading("BATT", null, null);

        if (!state.Battery.HasSeparateBuds)
            return Reading("BATT", Percent(state.Battery.Left), state.Battery.Left is null ? null : "%");

        return Frame($"""
            <text x="72" y="32" fill="{Dim}" font-size="16" text-anchor="middle">BATTERY</text>
            <text x="20" y="70" fill="{Dim}" font-size="18">L</text>
            {Charge(70, state.Battery.Left)}
            <text x="20" y="108" fill="{Dim}" font-size="18">R</text>
            {Charge(108, state.Battery.Right)}
            """);
    }

    /// <summary>
    /// One earbud's charge, centred, with the unit set smaller and quieter than the number - the
    /// same treatment the microphone level gets, and the reason the number stays legible at the
    /// size a key actually is. A part that is not reporting shows dashes and no unit: "-- %" would
    /// read as a measurement.
    /// </summary>
    /// <remarks>
    /// Centred a little right of the key's own middle, which leaves the L and R at the edge where
    /// they belong. They stay put as the reading changes - centring the whole row instead would
    /// shuffle them sideways every time a charge crossed into three digits.
    /// </remarks>
    private static string Charge(int y, int? percent) => percent is int value
        ? $"""<text x="{ChargeCentre}" y="{y}" fill="{Foreground}" font-size="30" text-anchor="middle">{value}""" +
          $"""<tspan dx="{UnitGap}" font-size="18" fill="{Dim}">%</tspan></text>"""
        : $"""<text x="{ChargeCentre}" y="{y}" fill="{Dim}" font-size="30" text-anchor="middle">--</text>""";

    private const int ChargeCentre = 78;

    /// <summary>
    /// The gap before the unit, set here rather than with a space: SVG collapses whitespace, so a
    /// space between the number and the unit is not something you can rely on being drawn.
    /// </summary>
    private const int UnitGap = 9;

    private static string? Percent(int? value) => value?.ToString();

    /// <summary>A label, a large reading, and a quieter unit after it. A null reading draws "--".</summary>
    private static string Reading(string label, string? value, string? unit)
    {
        string body = value ?? "--";
        string colour = value is null ? Dim : Foreground;
        string suffix = unit is null || value is null
            ? ""
            : $"""<text x="72" y="118" fill="{Dim}" font-size="18" text-anchor="middle">{Escape(unit)}</text>""";

        return Frame($"""
            <text x="72" y="44" fill="{Dim}" font-size="17" text-anchor="middle">{Escape(label)}</text>
            <text x="72" y="94" fill="{colour}" font-size="44" text-anchor="middle">{Escape(body)}</text>
            {suffix}
            """);
    }

    private static string Frame(string contents) => DataUri($"""
        <svg xmlns="http://www.w3.org/2000/svg" width="{Size}" height="{Size}" viewBox="0 0 {Size} {Size}">
        <rect width="{Size}" height="{Size}" fill="{Background}"/>
        <g font-family="Segoe UI, Helvetica, Arial, sans-serif" font-weight="600">
        {contents}
        </g>
        </svg>
        """);

    private static string DataUri(string svg) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

    /// <summary>
    /// Model names and labels end up inside SVG text, and a stray ampersand would make the whole
    /// document unparseable - a blank key rather than a wrong one.
    /// </summary>
    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
