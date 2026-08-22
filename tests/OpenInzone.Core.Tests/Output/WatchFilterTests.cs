// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Cli;
using OpenInzone.Protocol;

namespace OpenInzone.Tests.Output;

public class WatchFilterTests
{
    [Fact]
    public void NoWordsMeansEverything()
    {
        Assert.True(WatchFilter.TryParse([], out var events, out _));
        Assert.Empty(events);
    }

    [Fact]
    public void OneWordSelectsOneEvent()
    {
        Assert.True(WatchFilter.TryParse(["battery"], out var events, out _));
        Assert.Equal([EventId.BatteryInfo], events);
    }

    [Fact]
    public void SeveralWordsSelectSeveralEvents()
    {
        Assert.True(WatchFilter.TryParse(["balance", "volume"], out var events, out _));
        Assert.Contains(EventId.GameChatMixBalance, events);
        Assert.Contains(EventId.HeadphoneVolume, events);
    }

    [Fact]
    public void AnUnknownWordFailsAndListsTheValidOnes()
    {
        Assert.False(WatchFilter.TryParse(["batery"], out _, out var error));
        Assert.NotNull(error);
        Assert.Contains("batery", error);
        Assert.Contains("battery", error);
        Assert.Contains("sidetone", error);
    }
}
