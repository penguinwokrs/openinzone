// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Cli.Output;
using OpenInzone.Model;

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
}
