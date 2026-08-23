// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Cli.Output;
using OpenInzone.Hid;
using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Tests.Output;

/// <summary>A report type neither renderer knows how to draw, so the `default` arm has something to catch.</summary>
file sealed record UnknownReport : IReport;

public class TextRendererTests
{
    // One writer for both streams so a test can assert on either.
    private static string Render(IReport report, bool raw = false)
    {
        var writer = new StringWriter { NewLine = "\n" };
        new TextRenderer(writer, writer, raw).Render(report);
        return writer.ToString();
    }

    [Fact]
    public void DrawsBatteryTheWayItAlwaysHas()
    {
        var report = new BatteryReport(BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34]));

        Assert.Equal("L 76%  R --  case 34%\n", Render(report));
    }

    /// <summary>
    /// --raw is for watching what INZONE Hub sends while working out a setting this cannot decode
    /// yet, and it used to reach only the battery - so the decoded lines, the ones worth comparing
    /// against a value in Hub's own window, were the one place the bytes were hidden.
    /// </summary>
    [Fact]
    public void ShowsTheBytesBehindADecodedNotificationUnderRaw()
    {
        var report = new EventReport(new DateTime(2026, 1, 1, 1, 20, 23),
            EventId.SidetoneVolume, new SidetoneReport(new SidetoneVolume(3, 30)), "03 1E");

        Assert.Equal("01:20:23  SidetoneVolume         3  raw 03 1E\n", Render(report, raw: true));
        Assert.Equal("01:20:23  SidetoneVolume         3\n", Render(report));
    }

    [Fact]
    public void DrawsBalanceByNamingTheSideItLeansTo()
    {
        Assert.Equal("60 (chat 1.0)\n", Render(new BalanceReport(new MixBalance(60))));
    }

    [Fact]
    public void DrawsVolumeOutOfThirty()
    {
        Assert.Equal("19/30\n", Render(new VolumeReport(new HeadphoneVolume(false, 19, 0xFF))));
    }

    [Fact]
    public void DrawsSidetoneOnItsOwn()
    {
        Assert.Equal("30\n", Render(new SidetoneReport(new SidetoneVolume(30, 100))));
    }

    [Fact]
    public void DrawsAnErrorOnOneLine()
    {
        var report = new ErrorReport("unreachable", "The earbuds did not answer.");

        Assert.Equal("The earbuds did not answer.\n", Render(report));
    }

    [Fact]
    public void AddsTheRawBytesOnlyWhenAsked()
    {
        var report = new BatteryReport(BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34]));

        Assert.DoesNotContain("raw", Render(report));
        Assert.Contains("01 4C 00 FF 00 22", Render(report, raw: true));
    }

    // `Parse` synthesises four 0xFF bytes for a two-byte payload; `--raw` should show what the
    // wire actually carried, not the synthesised padding.
    [Fact]
    public void ShowsOnlyTheBytesTheWireSentForATwoBytePayload()
    {
        var report = new BatteryReport(BatteryInfo.Parse([0x01, 62]));

        string text = Render(report, raw: true);

        Assert.Contains("01 3E", text);
        Assert.DoesNotContain("FF", text);
    }

    // Muting is a headset flag. `inzone mic toggle` has always printed just that, never the
    // Windows capture level alongside it — README.md shows `muted` on its own.
    [Fact]
    public void DrawsAMuteChangeWithoutTheWindowsLevel()
    {
        var muted = new MicVolume(true, 0xFF, 0xFF);

        Assert.Equal("muted\n", Render(new MicMuteReport(muted)));
    }

    [Fact]
    public void DrawsALevelChangeWithoutTheMuteFlag()
    {
        Assert.Equal("level 50%\n", Render(new MicLevelReport(50)));
    }

    // `inzone mic` with no arguments is the one that reports both.
    [Fact]
    public void DrawsBothWhenAskedForTheMicrophoneAsAWhole()
    {
        var mic = new MicVolume(false, 0xFF, 0xFF);

        Assert.Equal("unmuted, level 95%\n", Render(new MicReport(mic, 95)));
        Assert.Equal("unmuted\n", Render(new MicReport(mic, null)));
    }

    [Fact]
    public void DrawsAWatchLineWithTheEventPaddedToTwentyTwo()
    {
        var report = new EventReport(
            new DateTime(2026, 8, 23, 1, 20, 23),
            EventId.GameChatMixBalance,
            new BalanceReport(new MixBalance(60)),
            "3C");

        Assert.Equal("01:20:23  GameChatMixBalance     60 (chat 1.0)\n", Render(report));
    }

    [Fact]
    public void FallsBackToRawBytesForAnEventWithNoDecoder()
    {
        var report = new EventReport(
            new DateTime(2026, 8, 23, 1, 20, 23), EventId.FirmwareVersion, null, "0A1B");

        Assert.Equal("01:20:23  FirmwareVersion        0A1B\n", Render(report));
    }

    // `inzone watch battery --raw` is exactly how you would see the bytes change during a
    // notification; the `--json` form already worked, the text form silently ignored `raw`.
    [Fact]
    public void AppendsTheRawBytesToAWatchedBatteryEventOnTheSameLine()
    {
        var report = new EventReport(
            new DateTime(2026, 8, 23, 1, 20, 23),
            EventId.BatteryInfo,
            new BatteryReport(BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34])),
            "01 4C 00 FF 00 22");

        string line = Render(report, raw: true);

        Assert.Single(line.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("L 76%  R --  case 34%  raw 01 4C 00 FF 00 22", line);
    }

    [Fact]
    public void OmitsTheRawBytesFromAWatchedBatteryEventByDefault()
    {
        var report = new EventReport(
            new DateTime(2026, 8, 23, 1, 20, 23),
            EventId.BatteryInfo,
            new BatteryReport(BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34])),
            "01 4C 00 FF 00 22");

        Assert.DoesNotContain("raw", Render(report));
    }

    // README.md's "Check the headset is found" transcript, pinned so the documented output stays true.
    [Fact]
    public void DrawsTheFullStatusForAnEarbudModel()
    {
        var status = new StatusReport(
            new ModelInfo(4, 0, 0, 0, 0, "3015430", "3015430", "3015430"),
            BatteryInfo.Parse([0x00, 97, 0x00, 97, 0x00, 34]),
            new MixBalance(50),
            new HeadphoneVolume(false, 15, 0xFF),
            new MicVolume(false, 0xFF, 0xFF),
            100,
            new SidetoneVolume(0, 0));

        Assert.Equal(
            "Device       INZONE Buds\n" +
            "Serial       L 3015430 / R 3015430 / dongle 3015430\n" +
            "Battery      L 97%  R 97%  case 34%\n" +
            "Balance      50 (centre)\n" +
            "Volume       15/30\n" +
            "Microphone   unmuted, level 100%\n" +
            "Sidetone     0\n",
            Render(status));
    }

    [Fact]
    public void DrawsStatusWithoutASerialLineForANonEarbudModel()
    {
        var status = new StatusReport(
            new ModelInfo(0, 0, 0, 0, 0, "", "", ""),
            BatteryInfo.Parse([0x00, 62]),
            new MixBalance(60),
            new HeadphoneVolume(false, 19, 0xFF),
            new MicVolume(false, 0xFF, 0xFF),
            95,
            new SidetoneVolume(0, 0));

        Assert.Equal(
            "Device       INZONE H9\n" +
            "Battery      62%\n" +
            "Balance      60 (chat 1.0)\n" +
            "Volume       19/30\n" +
            "Microphone   unmuted, level 95%\n" +
            "Sidetone     0\n",
            Render(status));
    }

    // README.md's `inzone devices` transcript under Troubleshooting.
    [Fact]
    public void DrawsOneDeviceAsTheDescriptionThenTheIndentedPath()
    {
        var device = new HidDeviceInfo(
            @"\\?\hid#vid_054c&pid_0ec2&mi_05&col03#8&29ddaaec&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}",
            0x054C, 0x0EC2, 0xFF04, 0x0001, 64, 64, "Hid Interface");

        Assert.Equal(
            "VID_054C&PID_0EC2 UsagePage=0xFF04 Usage=0x0001 In=64 Out=64 \"Hid Interface\"\n" +
            "  \\\\?\\hid#vid_054c&pid_0ec2&mi_05&col03#8&29ddaaec&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}\n",
            Render(new DeviceListReport([device])));
    }

    [Fact]
    public void ThrowsForAReportTypeItDoesNotKnowHowToDraw()
    {
        Assert.Throws<NotSupportedException>(() => Render(new UnknownReport()));
    }
}

public class JsonRendererTests
{
    private static string Render(IReport report, bool raw = false)
    {
        var writer = new StringWriter { NewLine = "\n" };
        new JsonRenderer(writer, raw).Render(report);
        return writer.ToString().Trim();
    }

    /// <summary>
    /// A model with no adjustable capture endpoint reports no level, and scripts read
    /// level_available to tell that from a level of nothing. Hard-coding it true went unnoticed.
    /// </summary>
    [Fact]
    public void SaysWhenThereIsNoMicrophoneLevelToReport()
    {
        string json = Render(new MicReport(new MicVolume(Muted: false, 0xFF, 0xFF), null));

        Assert.Contains("\"level\":null", json);
        Assert.Contains("\"level_available\":false", json);
    }

    [Fact]
    public void SaysWhenThereIsAMicrophoneLevelToReport()
    {
        string json = Render(new MicReport(new MicVolume(Muted: false, 0xFF, 0xFF), 75));

        Assert.Contains("\"level\":75", json);
        Assert.Contains("\"level_available\":true", json);
    }

    [Fact]
    public void NullsAPartThatIsNotReportingAndKeepsTheKey()
    {
        var json = Render(new BatteryReport(BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34])));

        Assert.Contains("\"left\":76", json);
        Assert.Contains("\"right\":null", json);
        Assert.Contains("\"case\":34", json);
        Assert.Contains("\"right_state\":\"not_reporting\"", json);
        Assert.Contains("\"case_is_snapshot\":true", json);
    }

    [Fact]
    public void OmitsTheKeysAModelDoesNotHave()
    {
        var json = Render(new BatteryReport(BatteryInfo.Parse([0x01, 62])));

        Assert.Contains("\"left\":62", json);
        Assert.DoesNotContain("\"right\"", json);
        Assert.DoesNotContain("\"case\"", json);
        // No case, so nothing to say is or is not a snapshot.
        Assert.DoesNotContain("\"case_is_snapshot\"", json);
    }

    [Fact]
    public void AddsTheRawBytesOnlyWhenAsked()
    {
        var report = new BatteryReport(BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34]));

        Assert.DoesNotContain("\"raw\"", Render(report));
        Assert.Contains("\"raw\":\"01 4C 00 FF 00 22\"", Render(report, raw: true));
    }

    [Fact]
    public void ShowsOnlyTheBytesTheWireSentForATwoBytePayload()
    {
        var report = new BatteryReport(BatteryInfo.Parse([0x01, 62]));

        string json = Render(report, raw: true);

        Assert.Contains("\"raw\":\"01 3E\"", json);
    }

    [Fact]
    public void StillEmitsWellFormedJsonOnTheErrorPath()
    {
        var json = Render(new ErrorReport("unreachable", "The earbuds did not answer."));

        Assert.Contains("\"error\":\"unreachable\"", json);
        Assert.Contains("\"message\":\"The earbuds did not answer.\"", json);
    }

    [Fact]
    public void NestsTheSameBatteryObjectInsideStatus()
    {
        var status = new StatusReport(
            default,
            BatteryInfo.Parse([0x01, 76, 0x00, 71, 0x00, 34]),
            new MixBalance(50),
            new HeadphoneVolume(false, 15, 0xFF),
            new MicVolume(false, 0xFF, 0xFF),
            100,
            new SidetoneVolume(0, 0));

        var json = Render(status);

        Assert.Contains("\"battery\":{", json);
        Assert.Contains("\"left\":76", json);
        Assert.Contains("\"value\":50", json);
    }

    [Fact]
    public void AddsTheSerialObjectForAnEarbudModel()
    {
        var status = new StatusReport(
            new ModelInfo(4, 0, 0, 0, 0, "3015430", "3015430", "3015430"),
            BatteryInfo.Parse([0x01, 76, 0x00, 71, 0x00, 34]),
            new MixBalance(50),
            new HeadphoneVolume(false, 15, 0xFF),
            new MicVolume(false, 0xFF, 0xFF),
            100,
            new SidetoneVolume(0, 0));

        var json = Render(status);

        Assert.Contains("\"serial\":{\"left\":\"3015430\",\"right\":\"3015430\",\"dongle\":\"3015430\"}", json);
    }

    [Fact]
    public void OmitsTheSerialObjectForANonEarbudModel()
    {
        var status = new StatusReport(
            new ModelInfo(0, 0, 0, 0, 0, "", "", ""),
            BatteryInfo.Parse([0x01, 62]),
            new MixBalance(50),
            new HeadphoneVolume(false, 15, 0xFF),
            new MicVolume(false, 0xFF, 0xFF),
            100,
            new SidetoneVolume(0, 0));

        Assert.DoesNotContain("\"serial\"", Render(status));
    }

    [Fact]
    public void WritesTheSidetoneValueOnItsOwn()
    {
        var json = Render(new SidetoneReport(new SidetoneVolume(30, 100)));

        Assert.Contains("\"value\":30", json);
    }

    // Sidetone in `status --json` used to be an ad-hoc inline object; it now goes through the
    // same `SidetoneReport` the `watch sidetone` event uses.
    [Fact]
    public void RendersSidetoneInsideStatusThroughTheSameReportType()
    {
        var status = new StatusReport(
            default,
            BatteryInfo.Parse([0x01, 62]),
            new MixBalance(50),
            new HeadphoneVolume(false, 15, 0xFF),
            new MicVolume(false, 0xFF, 0xFF),
            100,
            new SidetoneVolume(30, 100));

        Assert.Contains("\"sidetone\":{\"value\":30}", Render(status));
    }

    // A status bar should be able to run `jq 'select(.event=="battery").left'` against the
    // watch stream, not parse a rendered column layout out of a string.
    [Fact]
    public void PutsAWatchedEventsOwnFieldsBesideTimeAndEvent()
    {
        var report = new EventReport(
            new DateTime(2026, 8, 23, 1, 20, 23),
            EventId.BatteryInfo,
            new BatteryReport(BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34])),
            "01 4C 00 FF 00 22");

        var json = Render(report);

        Assert.Contains("\"time\":\"01:20:23\"", json);
        Assert.Contains("\"event\":\"battery\"", json);
        Assert.Contains("\"left\":76", json);
        Assert.Contains("\"right\":null", json);
        Assert.DoesNotContain("\"raw\":\"", json);
    }

    // Renamed from "detail" to "raw": "detail" is otherwise always the battery body's nested
    // object, and a typed decoder cannot deserialize a key whose type varies line to line.
    [Fact]
    public void KeepsTheRawBytesUnderTheirOwnKeyForAnEventWithNoDecoder()
    {
        var report = new EventReport(
            new DateTime(2026, 8, 23, 1, 20, 23), EventId.FirmwareVersion, null, "0A 1B");

        var json = Render(report);

        Assert.Contains("\"raw\":\"0A 1B\"", json);
        Assert.DoesNotContain("\"detail\"", json);
    }

    // Known events read as lowercase words ("battery"); an unmapped one used to leak the
    // PascalCase enum name, mixing two casings in one stream.
    [Fact]
    public void LowerCasesTheEventNameFallback()
    {
        var report = new EventReport(
            new DateTime(2026, 8, 23, 1, 20, 23), EventId.FirmwareVersion, null, "0A 1B");

        Assert.Contains("\"event\":\"firmwareversion\"", Render(report));
    }

    [Fact]
    public void ThrowsForAReportTypeItDoesNotKnowHowToDraw()
    {
        Assert.Throws<NotSupportedException>(() => Render(new UnknownReport()));
    }

    // `devices --json` used to escape "&" and quotation marks meant for a pipe or a file, not HTML.
    [Fact]
    public void DoesNotHtmlEscapeDeviceStrings()
    {
        var device = new HidDeviceInfo(
            @"\\?\hid#vid_054c&pid_0ec2", 0x054C, 0x0EC2, 0xFF04, 0x0001, 64, 64, "Hid Interface");

        var json = Render(new DeviceListReport([device]));

        Assert.Contains("VID_054C&PID_0EC2", json);
        Assert.Contains("Hid Interface", json);
        Assert.DoesNotContain("\\u0026", json);
    }
}

public class HexFormatTests
{
    [Fact]
    public void JoinsBytesWithASpace()
    {
        Assert.Equal("0A 1B", HexFormat.Bytes([0x0A, 0x1B]));
    }

    [Fact]
    public void ShowsOnlyTwoBytesForAHeadsetPayload()
    {
        Assert.Equal("01 3E", HexFormat.Battery(BatteryInfo.Parse([0x01, 62])));
    }

    [Fact]
    public void ShowsAllSixBytesForAnEarbudPayload()
    {
        Assert.Equal(
            "01 4C 00 FF 00 22",
            HexFormat.Battery(BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34])));
    }
}
