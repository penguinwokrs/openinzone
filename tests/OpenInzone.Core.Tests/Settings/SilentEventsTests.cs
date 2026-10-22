// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Protocol;
using OpenInzone.Settings;

namespace OpenInzone.Tests.Settings;

/// <summary>
/// A setting the headset never answers must cost one timeout, not one per reading, and must come back
/// if the silence was only a lost reply.
/// </summary>
public class SilentEventsTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void An_event_stays_silent_for_the_memory_and_no_longer()
    {
        var clock = new Clock();
        var silent = new SilentEvents(TimeSpan.FromMinutes(10), clock);

        silent.Add(EventId.Guidance);

        Assert.True(silent.Contains(EventId.Guidance));
        clock.Now += TimeSpan.FromMinutes(9);
        Assert.True(silent.Contains(EventId.Guidance));
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.False(silent.Contains(EventId.Guidance));
    }

    [Fact]
    public void Silence_is_remembered_per_event()
    {
        var silent = new SilentEvents(TimeSpan.FromMinutes(10), new Clock());

        silent.Add(EventId.Guidance);

        Assert.False(silent.Contains(EventId.AutoPowerOff));
    }
}
