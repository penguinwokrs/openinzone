// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;

namespace OpenInzone.StreamDeck;

/// <summary>
/// Shows a reading on a key for a moment after it is pressed, then puts the key back.
/// </summary>
/// <remarks>
/// A directed key is a picture rather than a readout, so the number it answers a press with is a
/// confirmation: it says what the press did and then gets out of the way. Pressing again extends
/// the moment rather than starting a second one, so holding a key down reads as one continuous
/// number rather than a flicker.
///
/// The redraw is a callback rather than a deck, which is what lets the timing be checked without
/// one. A key taken off the deck is forgotten along with its timer - otherwise the timer would be
/// the last thing holding a context that no longer exists, and would draw on it once more.
/// </remarks>
internal sealed class KeyFlash(TimeSpan duration, Action<string> redraw) : IDisposable
{
    // A moment in progress, and when it is actually due to end. The deadline is read from a
    // Stopwatch rather than the wall clock: it only ever needs to be compared against another
    // reading from the same clock a moment later, and the wall clock can jump (an NTP correction,
    // a sleeping laptop resuming) in ways that would make a fresh press look like it was already
    // overdue, or an old one look like it still had a second left. Environment.TickCount64 is
    // monotonic too, but on this machine it only advances in ~10ms steps, which would swallow the
    // few milliseconds of slack below entirely; Stopwatch's counter is fine-grained enough that
    // the slack it is compared against actually means something.
    private sealed record Moment(Timer Timer, long DueAt);

    private const long SlackMilliseconds = 3;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private readonly Dictionary<string, Moment> _showing = [];
    private readonly Lock _gate = new();

    public void Show(string context)
    {
        long dueAt = Clock.ElapsedMilliseconds + (long)duration.TotalMilliseconds;

        lock (_gate)
        {
            if (_showing.TryGetValue(context, out var running))
                _showing[context] = running with { DueAt = dueAt };
            else
                _showing[context] = new Moment(new Timer(Expire, context, duration, Timeout.InfiniteTimeSpan), dueAt);
        }

        redraw(context);
    }

    public bool IsShowing(string context)
    {
        lock (_gate) return _showing.ContainsKey(context);
    }

    /// <summary>Drops a key without drawing on it, for one that has left the deck.</summary>
    public void Forget(string context)
    {
        Timer? timer;
        lock (_gate)
        {
            if (!_showing.Remove(context, out var moment)) return;
            timer = moment.Timer;
        }

        timer.Dispose();
    }

    // A press that extends an existing moment (see Show, above) never touches the Timer itself -
    // it only moves DueAt into the future. That sidesteps the bug this class used to have:
    // Timer.Change can reschedule when a timer will next fire, but it cannot retract a callback
    // the runtime has already dispatched to a thread-pool thread. If Show had called Change to
    // push the deadline out, a press landing at the same moment the old timer was due could still
    // be followed by the stale callback for the old deadline running anyway - either tearing down
    // the very moment the press just extended, or racing the extending Show for the lock and
    // forcing a needless tear-down-and-rebuild. Both are exactly the flicker this class exists to
    // prevent, and a Change call cannot avoid them because by the time it runs, the callback it
    // would like to retract may already be beyond recall.
    //
    // Leaving the Timer alone when extending, and instead having every firing compare the clock
    // to whatever DueAt currently holds, closes that off: whichever deadline is on record when
    // this method takes the lock is the one that decides the outcome, and only a fire that finds
    // it genuinely in the past is allowed to end the moment. A fire that finds DueAt has moved
    // into the future re-arms the timer for what remains of it and leaves the context alone -
    // that one Timer keeps ticking toward whatever the latest press asked for, rather than a new
    // one being spun up each time.
    private void Expire(object? state)
    {
        string context = (string)state!;
        Timer? timer;

        lock (_gate)
        {
            // Gone already means the key left the deck while the moment was running, and there
            // is nothing left to draw on.
            if (!_showing.TryGetValue(context, out var moment)) return;

            // A few milliseconds of slack means a fire that lands a hair before its own nominal
            // deadline - ordinary timer jitter, not an extension - is treated as reached rather
            // than re-armed for a practically-zero remainder, which would just be another round
            // trip through the thread pool to reach the same conclusion.
            long remaining = moment.DueAt - Clock.ElapsedMilliseconds;
            if (remaining > SlackMilliseconds)
            {
                moment.Timer.Change(TimeSpan.FromMilliseconds(remaining), Timeout.InfiniteTimeSpan);
                return;
            }

            _showing.Remove(context);
            timer = moment.Timer;
        }

        timer.Dispose();
        redraw(context);
    }

    public void Dispose()
    {
        Moment[] moments;
        lock (_gate)
        {
            moments = [.. _showing.Values];
            _showing.Clear();
        }

        foreach (var moment in moments) moment.Timer.Dispose();
    }
}
