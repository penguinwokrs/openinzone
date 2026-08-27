// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
using System.Diagnostics;
using OpenInzone.StreamDeck;

namespace OpenInzone.Tests.StreamDeck;

/// <summary>
/// How long a directed key shows what a press did. Timing is the whole of this class, so the tests
/// wait for the outcome rather than for a fixed sleep - a machine under load must fail this for a
/// real reason or not at all.
/// </summary>
public class KeyFlashTests
{
    private static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private sealed class Redraws
    {
        private readonly ConcurrentQueue<string> _contexts = new();
        public void Record(string context) => _contexts.Enqueue(context);
        public int Count => _contexts.Count;
        public IReadOnlyCollection<string> Contexts => _contexts;
    }

    [Fact]
    public void A_pressed_key_shows_at_once_and_is_redrawn_to_say_so()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(Moment, redraws.Record);

        flash.Show("key-1");

        Assert.True(flash.IsShowing("key-1"));
        Assert.Contains("key-1", redraws.Contexts);
    }

    [Fact]
    public void A_key_that_was_never_pressed_is_not_showing()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(Moment, redraws.Record);

        Assert.False(flash.IsShowing("key-1"));
        Assert.Equal(0, redraws.Count);
    }

    [Fact]
    public void The_moment_passes_and_the_key_is_redrawn_to_go_back_to_its_picture()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(Moment, redraws.Record);

        flash.Show("key-1");
        Assert.True(SpinWait.SpinUntil(() => !flash.IsShowing("key-1"), Patience));

        Assert.True(SpinWait.SpinUntil(() => redraws.Count >= 2, Patience));
    }

    /// <summary>
    /// Holding a key down must read as one continuous number rather than a flicker, so a second
    /// press extends the moment instead of starting a competing one.
    /// </summary>
    [Fact]
    public void Pressing_again_extends_the_moment_rather_than_starting_a_second_one()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(TimeSpan.FromMilliseconds(400), redraws.Record);

        flash.Show("key-1");
        Thread.Sleep(200);
        flash.Show("key-1");
        Thread.Sleep(300);

        // Without the extension the first moment would have ended by now.
        Assert.True(flash.IsShowing("key-1"));
    }

    /// <summary>
    /// <c>Timer.Change</c> cannot retract a callback the runtime has already dispatched to the
    /// thread pool. If a press lands right when the previous moment was due, the already-queued
    /// <c>Expire</c> for the old deadline can still run after the extension has already returned
    /// to the caller having reported success: it finds the (freshly extended) context and tears
    /// it down anyway, unprompted by any further press, and the key goes back to its picture the
    /// instant after confirming it would not. That is the one shape a correct implementation
    /// cannot produce: whatever a press's <c>Show</c> call leaves behind - a clean extension, or
    /// even a fair "this arrived just late enough that the old moment had already genuinely
    /// ended and a new one started" restart - is settled by the time <c>Show</c> returns. Nothing
    /// should still be able to reach in afterwards and take it back down without another call.
    /// (The fair-restart case is why this test does not also assert a fixed number of redraws per
    /// press: an implementation that has genuinely closed the race can still legitimately cost
    /// two redraws for one press when the press lands right on the boundary, and asserting
    /// against that would fail a correct implementation for no reason.)
    ///
    /// Hitting the boundary on purpose needs the calling thread to reach <c>Show</c> at
    /// essentially the same moment the timer's callback is dispatched. A busy spin is used
    /// instead of <c>Thread.Sleep</c> to line the two up: a sleeping thread pays the OS's
    /// wake-up latency to resume, but the timer callback is dispatched to a thread-pool thread
    /// that pays that same kind of latency, so sleeping tends to land safely on one side or the
    /// other rather than in the narrow window in between. Spinning removes our side of that
    /// latency, putting the extension right where a stale, already-dispatched callback for the
    /// old deadline (if the runtime happened to queue one) would still be in flight. Repeating it
    /// many times makes hitting that ordering close to certain, and a short watch window after
    /// every press - long enough to give any such stale callback time to do its damage, short
    /// enough to end well before the next genuine deadline - has to hold every time: once a press
    /// is answered, nothing takes the key back down on its own before the next press.
    /// </summary>
    [Fact]
    public void Extending_at_the_instant_the_timer_is_due_never_takes_the_key_back_down_unprompted()
    {
        var redraws = new Redraws();
        var duration = TimeSpan.FromMilliseconds(10);
        var watch = TimeSpan.FromMilliseconds(2);
        using var flash = new KeyFlash(duration, redraws.Record);
        const string context = "key-1";
        const int iterations = 1500;

        var clock = Stopwatch.StartNew();

        flash.Show(context);
        var due = clock.Elapsed + duration;

        for (int i = 0; i < iterations; i++)
        {
            SpinWait.SpinUntil(() => clock.Elapsed >= due);

            flash.Show(context);
            due = clock.Elapsed + duration;

            // Watch what Show just settled for a while - well short of the deadline it just set -
            // and make sure nothing quietly takes it back down before we press again.
            var watchUntil = clock.Elapsed + watch;
            while (clock.Elapsed < watchUntil)
                Assert.True(flash.IsShowing(context));
        }
    }

    [Fact]
    public void Two_keys_keep_their_own_moments()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(Patience, redraws.Record);

        flash.Show("key-1");

        Assert.True(flash.IsShowing("key-1"));
        Assert.False(flash.IsShowing("key-2"));
    }

    /// <summary>
    /// A key taken off the deck must not be drawn on afterwards: its context is gone, and the
    /// timer that would have fired is the only thing still holding it.
    /// </summary>
    [Fact]
    public void A_key_taken_off_the_deck_stops_showing_and_is_not_drawn_on_again()
    {
        var redraws = new Redraws();
        using var flash = new KeyFlash(Moment, redraws.Record);

        flash.Show("key-1");
        int drawn = redraws.Count;
        flash.Forget("key-1");

        Assert.False(flash.IsShowing("key-1"));
        Thread.Sleep(300);
        Assert.Equal(drawn, redraws.Count);
    }

    [Fact]
    public void Disposing_forgets_every_key()
    {
        var redraws = new Redraws();
        var flash = new KeyFlash(Moment, redraws.Record);

        flash.Show("key-1");
        flash.Show("key-2");
        int drawn = redraws.Count;
        flash.Dispose();

        Assert.False(flash.IsShowing("key-1"));
        Assert.False(flash.IsShowing("key-2"));
        Thread.Sleep(300);
        Assert.Equal(drawn, redraws.Count);
    }
}
