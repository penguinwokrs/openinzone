// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Cli.Output;
using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Tests.Output;

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

    [Fact]
    public void DrawsBalanceWithTheHubScale()
    {
        Assert.Equal("60 (+1.0)\n", Render(new BalanceReport(new MixBalance(60))));
    }

    [Fact]
    public void DrawsVolumeOutOfThirty()
    {
        Assert.Equal("19/30\n", Render(new VolumeReport(new HeadphoneVolume(false, 19, 0xFF))));
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
            new DateTime(2026, 8, 23, 1, 20, 23), EventId.GameChatMixBalance, "60 (+1.0)");

        Assert.Equal("01:20:23  GameChatMixBalance     60 (+1.0)\n", Render(report));
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
    }

    [Fact]
    public void AddsTheRawBytesOnlyWhenAsked()
    {
        var report = new BatteryReport(BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34]));

        Assert.DoesNotContain("\"raw\"", Render(report));
        Assert.Contains("\"raw\":\"01 4C 00 FF 00 22\"", Render(report, raw: true));
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
}
