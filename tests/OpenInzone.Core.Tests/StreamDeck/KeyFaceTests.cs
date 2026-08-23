// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;
using System.Xml.Linq;
using OpenInzone.Ipc;
using OpenInzone.StreamDeck;

namespace OpenInzone.Tests.StreamDeck;

public class KeyFaceTests
{
    private static readonly DeviceSnapshot Live = new(
        true, "INZONE Buds", 16, 30, false, 40, false, 75, true,
        new BatterySnapshot(97, 94, 62, true));

    /// <summary>Undoes the data URI so the markup itself can be examined.</summary>
    private static string Svg(string actionId, DeviceSnapshot state)
    {
        string uri = KeyFace.For(actionId, state);
        Assert.StartsWith("data:image/svg+xml;base64,", uri, StringComparison.Ordinal);
        return Encoding.UTF8.GetString(Convert.FromBase64String(uri["data:image/svg+xml;base64,".Length..]));
    }

    /// <summary>
    /// Stream Deck renders the SVG itself and shows nothing at all if it will not parse, so a key
    /// that is subtly malformed looks exactly like a key that is not working.
    /// </summary>
    [Theory]
    [InlineData(ActionIds.Volume)]
    [InlineData(ActionIds.Balance)]
    [InlineData(ActionIds.MicMute)]
    [InlineData(ActionIds.MicLevel)]
    [InlineData(ActionIds.Battery)]
    public void Every_key_face_is_well_formed_xml(string actionId)
    {
        XDocument.Parse(Svg(actionId, Live));
        XDocument.Parse(Svg(actionId, DeviceSnapshot.Disconnected));
    }

    [Fact]
    public void The_volume_key_shows_the_reading_and_its_scale()
    {
        string svg = Svg(ActionIds.Volume, Live);

        Assert.Contains(">16<", svg, StringComparison.Ordinal);
        Assert.Contains("/ 30", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disconnected_headset_draws_no_reading_rather_than_a_stale_one()
    {
        string svg = Svg(ActionIds.Volume, DeviceSnapshot.Disconnected);

        Assert.Contains(">--<", svg, StringComparison.Ordinal);
        Assert.DoesNotContain(">0<", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void The_balance_key_speaks_the_scale_INZONE_Hub_uses()
    {
        Assert.Contains("CHAT -1.0", Svg(ActionIds.Balance, Live with { Balance = 40 }), StringComparison.Ordinal);
        Assert.Contains("GAME +2.0", Svg(ActionIds.Balance, Live with { Balance = 70 }), StringComparison.Ordinal);
        Assert.Contains("CENTRE", Svg(ActionIds.Balance, Live with { Balance = 50 }), StringComparison.Ordinal);
    }

    [Fact]
    public void The_microphone_key_says_which_state_it_is_in()
    {
        Assert.Contains("MUTED", Svg(ActionIds.MicMute, Live with { MicMuted = true }), StringComparison.Ordinal);
        Assert.Contains("LIVE", Svg(ActionIds.MicMute, Live with { MicMuted = false }), StringComparison.Ordinal);
    }

    [Fact]
    public void The_battery_key_shows_both_earbuds_the_right_way_round()
    {
        string svg = Svg(ActionIds.Battery, Live);

        Assert.True(svg.IndexOf(">97<", StringComparison.Ordinal) > 0);
        Assert.True(svg.IndexOf(">94<", StringComparison.Ordinal) > svg.IndexOf(">97<", StringComparison.Ordinal));
    }

    [Fact]
    public void An_earbud_that_is_not_reporting_draws_two_dashes()
    {
        string svg = Svg(ActionIds.Battery, Live with { Battery = new BatterySnapshot(97, null, 62, true) });

        Assert.Contains(">97<", svg, StringComparison.Ordinal);
        Assert.Contains(">--<", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_headset_with_one_battery_does_not_draw_an_empty_right_earbud()
    {
        var headset = Live with
        {
            Model = "INZONE H9",
            Battery = new BatterySnapshot(88, null, null, false),
        };

        string svg = Svg(ActionIds.Battery, headset);

        Assert.Contains(">88<", svg, StringComparison.Ordinal);
        Assert.DoesNotContain(">R<", svg, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model name reaches the markup as text. One ampersand would make the document unparseable,
    /// which Stream Deck shows as a blank key.
    /// </summary>
    [Fact]
    public void Text_that_would_break_the_markup_is_escaped()
    {
        var awkward = Live with { Model = "Sony & <INZONE>" };

        XDocument.Parse(Svg(ActionIds.Volume, awkward));
    }

    [Fact]
    public void The_microphone_level_key_says_so_when_the_model_has_no_level_to_show()
    {
        string svg = Svg(ActionIds.MicLevel, Live with { MicLevelAvailable = false });

        Assert.Contains(">--<", svg, StringComparison.Ordinal);
    }
}
