// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;
using OpenInzone.Ipc;
using OpenInzone.Protocol;

namespace OpenInzone.Tests.Control;

public class SettingStateTests
{
    private static readonly IReadOnlyList<SettingValue> Initial =
    [
        new("ambient-mode", 0),
        new("ambient-level", 20),
        new("voice-focus", 0),
        new("sidetone", 3),
    ];

    [Fact]
    public void A_notification_before_the_initial_read_publishes_no_partial_list()
    {
        var state = new SettingState();

        Assert.Null(state.Apply(EventId.AmbientSetting, [0x01, 0x07, 0xFF, 0x01]));
    }

    [Fact]
    public void An_ambient_notification_updates_every_setting_in_its_packet()
    {
        var state = new SettingState();
        state.Replace(Initial);

        var next = state.Apply(EventId.AmbientSetting, [0x01, 0x07, 0xFF, 0x01]);

        Assert.NotNull(next);
        Assert.Equal(1, next.Value("ambient-mode"));
        Assert.Equal(7, next.Value("ambient-level"));
        Assert.Equal(1, next.Value("voice-focus"));
    }

    [Fact]
    public void A_notification_keeps_unrelated_settings_in_the_complete_list()
    {
        var state = new SettingState();
        state.Replace(Initial);

        var next = state.Apply(EventId.AmbientSetting, [0x02, 0x12, 0xFF, 0x00]);

        Assert.NotNull(next);
        Assert.Equal(Initial.Count, next.Count);
        Assert.Equal(3, next.Value("sidetone"));
    }

    [Fact]
    public void A_notification_that_arrives_during_a_full_read_wins_over_that_reads_older_value()
    {
        var state = new SettingState();
        state.Replace(Initial);
        long read = state.BeginRead();

        state.Apply(EventId.AmbientSetting, [0x01, 0x07, 0xFF, 0x01]);
        var next = state.Replace(Initial, read);

        Assert.Equal(1, next.Value("ambient-mode"));
        Assert.Equal(7, next.Value("ambient-level"));
        Assert.Equal(1, next.Value("voice-focus"));
        Assert.Equal(3, next.Value("sidetone"));
    }

    [Fact]
    public async Task A_notification_cannot_publish_a_newer_value_before_an_older_read_is_published()
    {
        var state = new SettingState();
        state.Replace(Initial);
        long read = state.BeginRead();
        var published = new System.Collections.Concurrent.ConcurrentQueue<int>();
        using var readingPublication = new ManualResetEventSlim();
        using var releaseReading = new ManualResetEventSlim();
        using var notificationStarted = new ManualResetEventSlim();

        Task reading = Task.Run(() => state.ReplaceAndPublish(Initial, read, settings =>
        {
            readingPublication.Set();
            releaseReading.Wait();
            published.Enqueue(settings.Value("ambient-mode")!.Value);
        }));
        Assert.True(readingPublication.Wait(TimeSpan.FromSeconds(10)));

        Task notification = Task.Run(() =>
        {
            notificationStarted.Set();
            state.ApplyAndPublish(
                EventId.AmbientSetting,
                [0x01, 0x07, 0xFF, 0x01],
                settings => published.Enqueue(settings.Value("ambient-mode")!.Value));
        });
        Assert.True(notificationStarted.Wait(TimeSpan.FromSeconds(10)));

        releaseReading.Set();
        await Task.WhenAll(reading, notification).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([0, 1], published);
    }

    [Fact]
    public void A_short_or_uncatalogued_notification_changes_nothing()
    {
        var state = new SettingState();
        state.Replace(Initial);

        Assert.Null(state.Apply(EventId.AmbientSetting, [0x01]));
        Assert.Null(state.Apply(EventId.BatteryInfo, [0x00, 0x64]));
    }

    [Fact]
    public void Clearing_forgets_the_previous_headsets_settings()
    {
        var state = new SettingState();
        state.Replace(Initial);
        state.Clear();

        Assert.Null(state.Apply(EventId.AmbientSetting, [0x01, 0x07, 0xFF, 0x01]));
    }
}
