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

    public static string For(string actionId, DeviceSnapshot state) => actionId switch
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
    /// One earbud's charge, right-aligned, with the unit set smaller and quieter than the number -
    /// the same treatment the microphone level gets, and the reason the number stays legible at
    /// the size a key actually is. A part that is not reporting shows dashes and no unit: "-- %"
    /// would read as a measurement.
    /// </summary>
    private static string Charge(int y, int? percent) => percent is int value
        ? $"""<text x="124" y="{y}" fill="{Foreground}" font-size="30" text-anchor="end">{value}""" +
          $"""<tspan font-size="18" fill="{Dim}"> %</tspan></text>"""
        : $"""<text x="124" y="{y}" fill="{Dim}" font-size="30" text-anchor="end">--</text>""";

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
