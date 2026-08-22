// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Model;

namespace OpenInzone.Tests.Model;

public class BatteryInfoTests
{
    // Confirmed on hardware 2026-08-23: with the right earbud docked, the right slot read 0xFF.
    [Fact]
    public void ParsesTheSixByteEarbudPayloadInLeftRightCaseOrder()
    {
        var battery = BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34]);

        Assert.True(battery.HasSeparateBuds);
        Assert.Equal(76, battery.Left.Percent);
        Assert.Null(battery.Right.Percent);
        Assert.Equal(34, battery.Case.Percent);
    }

    [Fact]
    public void MarksAPartThatReportsFfAsNotReporting()
    {
        var battery = BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34]);

        Assert.Equal(BatteryPartState.Reporting, battery.Left.State);
        Assert.Equal(BatteryPartState.NotReporting, battery.Right.State);
        Assert.Equal(BatteryPartState.Reporting, battery.Case.State);
    }

    [Fact]
    public void MarksTheBudAndCaseSlotsAbsentOnATwoBytePayload()
    {
        var battery = BatteryInfo.Parse([0x01, 62]);

        Assert.False(battery.HasSeparateBuds);
        Assert.Equal(62, battery.Left.Percent);
        Assert.Equal(BatteryPartState.Absent, battery.Right.State);
        Assert.Equal(BatteryPartState.Absent, battery.Case.State);
    }

    // Never observed. If one ever arrives it must not be shown as a percentage,
    // and it must not throw: this runs on the HID reader thread.
    [Fact]
    public void TreatsAPercentAboveOneHundredAsNotReporting()
    {
        var battery = BatteryInfo.Parse([0x00, 200, 0x00, 50, 0x00, 50]);

        Assert.Equal(BatteryPartState.NotReporting, battery.Left.State);
        Assert.Null(battery.Left.Percent);
        Assert.Equal(200, battery.Left.RawPercent);
    }

    [Fact]
    public void KeepsTheStatusByteAvailableEvenThoughItsMeaningIsUnknown()
    {
        var battery = BatteryInfo.Parse([0x07, 76, 0x03, 71, 0x01, 34]);

        Assert.Equal(0x07, battery.Left.RawStatus);
        Assert.Equal(0x03, battery.Right.RawStatus);
        Assert.Equal(0x01, battery.Case.RawStatus);
    }

    [Fact]
    public void SaysTheCaseLevelIsASnapshotOnEarbudModels()
    {
        Assert.True(BatteryInfo.Parse([0x01, 76, 0x00, 71, 0x00, 34]).CaseIsSnapshot);
        Assert.False(BatteryInfo.Parse([0x01, 62]).CaseIsSnapshot);
    }

    // The tray GUI renders this string in its tooltip. It must not drift.
    [Fact]
    public void KeepsTheExistingToStringOutput()
    {
        Assert.Equal("L 76%  R --  case 34%",
            BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34]).ToString());
        Assert.Equal("62%", BatteryInfo.Parse([0x01, 62]).ToString());
    }

    [Fact]
    public void FormatsAPartOnItsOwn()
    {
        var battery = BatteryInfo.Parse([0x01, 76, 0x00, 0xFF, 0x00, 34]);

        Assert.Equal("76%", battery.Left.ToString());
        Assert.Equal("--", battery.Right.ToString());
    }
}
