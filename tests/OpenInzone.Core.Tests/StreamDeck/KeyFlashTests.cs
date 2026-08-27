// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
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
