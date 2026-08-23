// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;
using System.Xml.Linq;
using OpenInzone.Ipc;
using OpenInzone.StreamDeck;

namespace OpenInzone.Tests.StreamDeck;

/// <summary>
/// The battery key, which shows a reading per earbud with its unit. Setting OPENINZONE_FACE_DIR
/// also writes each face out, because an icon is only right once someone has seen it at the size
/// it is used - and that needs no headset, since a face is a function of the snapshot alone.
/// </summary>
public class BatteryFaceTests
{
    private static readonly DeviceSnapshot Live = new(
        true, "INZONE Buds", 18, 30, false, 50, false, 100, true,
        new BatterySnapshot(80, 30, 62, true));

    private static string Draw(string name, DeviceSnapshot state)
    {
        string uri = KeyFace.For(ActionIds.Battery, state);
        const string prefix = "data:image/svg+xml;base64,";
        byte[] bytes = Convert.FromBase64String(uri[prefix.Length..]);

        string? directory = Environment.GetEnvironmentVariable("OPENINZONE_FACE_DIR");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"{name}.svg"), bytes);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    [Fact]
    public void Each_earbud_is_shown_on_its_own_line_with_its_unit()
    {
        string svg = Draw("battery", Live);

        XDocument.Parse(svg);
        Assert.Contains(">80<", svg, StringComparison.Ordinal);
        Assert.Contains(">30<", svg, StringComparison.Ordinal);
        Assert.Equal(2, svg.Split(">%</tspan>").Length - 1);
    }

    [Fact]
    public void The_left_earbud_is_drawn_above_the_right()
    {
        string svg = Draw("battery", Live);

        Assert.True(svg.IndexOf(">80<", StringComparison.Ordinal)
            < svg.IndexOf(">30<", StringComparison.Ordinal));
    }

    /// <summary>
    /// An earbud in the case has no reading, and "-- %" would read as one. It shows dashes alone.
    /// </summary>
    [Fact]
    public void A_stowed_earbud_shows_no_unit()
    {
        string svg = Draw("battery-stowed", Live with { Battery = new BatterySnapshot(80, null, 62, true) });

        XDocument.Parse(svg);
        Assert.Contains(">--<", svg, StringComparison.Ordinal);
        Assert.Equal(1, svg.Split(">%</tspan>").Length - 1);
    }

    [Fact]
    public void A_headset_shows_one_reading_rather_than_two_lines()
    {
        string svg = Draw("battery-headset", Live with { Battery = new BatterySnapshot(88, null, null, false) });

        XDocument.Parse(svg);
        Assert.Contains(">88<", svg, StringComparison.Ordinal);
        Assert.DoesNotContain(">R<", svg, StringComparison.Ordinal);
    }
}
