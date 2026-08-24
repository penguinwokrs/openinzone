// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Xml.Linq;
using OpenInzone.Control;
using OpenInzone.Ipc;

namespace OpenInzone.Tests.Control;

/// <summary>
/// What the tray's panel and tooltip actually say. Worth pinning: the tray targets
/// net8.0-windows and cannot be reached from here, so this is the only place these strings are
/// checked at all.
/// </summary>
public class SnapshotTextTests
{
    private static readonly DeviceSnapshot Live = new(
        true, "INZONE Buds", 16, 30, false, 40, false, 75, true,
        new BatterySnapshot(97, 94, 62, true));

    [Fact]
    public void The_volume_is_shown_against_the_headsets_own_scale()
    {
        Assert.Equal("16/30", SnapshotText.Volume(Live));
    }

    [Fact]
    public void The_tooltip_calls_out_a_muted_headset()
    {
        InCulture("ja", () =>
        {
            Assert.Equal("16/30", SnapshotText.VolumeWithMute(Live));
            Assert.Equal("16/30（ミュート）", SnapshotText.VolumeWithMute(Live with { VolumeMuted = true }));
        });

        InCulture("en", () =>
            Assert.Equal("16/30 (muted)", SnapshotText.VolumeWithMute(Live with { VolumeMuted = true })));
    }

    /// <summary>
    /// Game is the low end of the scale. The side is named rather than signed: a reader of
    /// "+2.0" has to already know which way the scale runs, and every description of that was
    /// wrong until someone listened to it.
    /// </summary>
    [Fact]
    public void The_balance_names_the_side_it_leans_to()
    {
        InCulture("ja", () =>
        {
            Assert.Equal("ゲーム寄り 1.0", SnapshotText.Balance(Live));
            Assert.Equal("中央", SnapshotText.Balance(Live with { Balance = 50 }));
            Assert.Equal("チャット寄り 2.0", SnapshotText.Balance(Live with { Balance = 70 }));
        });

        InCulture("en", () =>
        {
            Assert.Equal("Game 1.0", SnapshotText.Balance(Live));
            Assert.Equal("Centre", SnapshotText.Balance(Live with { Balance = 50 }));
            Assert.Equal("Chat 2.0", SnapshotText.Balance(Live with { Balance = 70 }));
        });
    }

    [Fact]
    public void A_model_with_no_microphone_level_says_so_rather_than_showing_nought()
    {
        InCulture("ja", () =>
        {
            Assert.Equal("75%", SnapshotText.MicLevel(Live));
            Assert.Equal("利用不可", SnapshotText.MicLevel(Live with { MicLevelAvailable = false }));
        });

        InCulture("en", () =>
            Assert.Equal("Unavailable", SnapshotText.MicLevel(Live with { MicLevelAvailable = false })));
    }

    [Fact]
    public void Both_earbuds_and_the_case_are_shown_the_right_way_round()
    {
        InCulture("ja", () =>
            Assert.Equal("L 97%   R 94%   ケース 62%", SnapshotText.Battery(Live)));

        InCulture("en", () =>
            Assert.Equal("L 97%   R 94%   Case 62%", SnapshotText.Battery(Live)));
    }

    [Fact]
    public void An_earbud_in_the_case_reads_as_dashes_rather_than_as_flat()
    {
        InCulture("ja", () =>
            Assert.Equal("L 97%   R --   ケース 62%",
                SnapshotText.Battery(Live with { Battery = new BatterySnapshot(97, null, 62, true) })));

        InCulture("en", () =>
            Assert.Equal("L 97%   R --   Case 62%",
                SnapshotText.Battery(Live with { Battery = new BatterySnapshot(97, null, 62, true) })));
    }

    [Fact]
    public void A_headset_shows_one_reading_and_no_earbuds()
    {
        Assert.Equal("88%",
            SnapshotText.Battery(Live with { Battery = new BatterySnapshot(88, null, null, false) }));
    }

    /// <summary>
    /// Nothing connected has to read as nothing everywhere. A resting snapshot is all zeroes, and
    /// a zero shown as a reading is indistinguishable from a flat battery or a silenced headset.
    /// </summary>
    [Fact]
    public void Nothing_connected_reads_as_nothing_in_every_field()
    {
        var nothing = DeviceSnapshot.Disconnected;

        Assert.Equal("--", SnapshotText.Volume(nothing));
        Assert.Equal("--", SnapshotText.VolumeWithMute(nothing));
        Assert.Equal("--", SnapshotText.Balance(nothing));
        Assert.Equal("--", SnapshotText.MicLevel(nothing));
        Assert.Equal("--", SnapshotText.Battery(nothing));
    }

    /// <summary>
    /// The tooltip is set on a NotifyIcon, which throws above 63 characters. The tray truncates,
    /// but a model name plus two readings should not be reaching that in the first place. Checked
    /// in all three languages, not just Japanese: the tray's own tooltip words come from resources
    /// now, and "Volume"/"Battery" in English are longer than 音量/バッテリー, so English rather
    /// than Japanese may be the worst case. The label words are read from the resx files
    /// themselves, not retyped here, so a future edit to those resources is what this test
    /// actually measures rather than a stale copy of them.
    /// </summary>
    public static TheoryData<string> TooltipCultures() =>
        // Not a collection expression: xunit 2.5.3's TheoryData does not support one (CS0029).
        new()
        {
            "en",
            "ja",
            "zh-Hans",
        };

    [Theory]
    [MemberData(nameof(TooltipCultures))]
    public void The_tooltip_fits_in_what_a_tray_icon_accepts(string culture)
    {
        string volumeWord = TrayResourceValue(culture, "Tray_TooltipVolume");
        string batteryWord = TrayResourceValue(culture, "Tray_TooltipBattery");

        InCulture(culture, () =>
        {
            string tooltip = $"{Live.Model}\n{volumeWord} {SnapshotText.VolumeWithMute(Live)}\n" +
                             $"{batteryWord} {SnapshotText.Battery(Live)}";

            Assert.True(tooltip.Length <= 63, $"{culture}: {tooltip.Length} characters: {tooltip}");
        });
    }

    /// <summary>
    /// Reads a single tray resource value straight off disk, the same way
    /// <see cref="ResourceCompletenessTests"/> does, so this test measures the tooltip words the
    /// tray will actually render rather than a copy of them typed into the test.
    /// </summary>
    private static string TrayResourceValue(string culture, string key)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenInzone.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        string fileName = culture == "en" ? "Strings.resx" : $"Strings.{culture}.resx";
        string path = Path.Combine(directory.FullName, "src", "OpenInzone.Tray", "Resources", fileName);

        var entry = XDocument.Load(path).Root!.Elements("data")
            .Single(d => (string)d.Attribute("name")! == key);

        return (string)entry.Element("value")!;
    }

    /// <summary>
    /// Runs a block with the UI culture pinned. The strings under test now come from resources, so
    /// without this the assertions would depend on whatever culture the test host happened to be
    /// in - which on a build agent is not Japanese.
    /// </summary>
    private static void InCulture(string tag, Action body)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture =
                System.Globalization.CultureInfo.GetCultureInfo(tag);
            body();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }
}
