// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

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
    private readonly Dictionary<string, Timer> _showing = [];
    private readonly Lock _gate = new();

    public void Show(string context)
    {
        lock (_gate)
        {
            if (_showing.TryGetValue(context, out var running))
                running.Change(duration, Timeout.InfiniteTimeSpan);
            else
                _showing[context] = new Timer(Expire, context, duration, Timeout.InfiniteTimeSpan);
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
            if (!_showing.Remove(context, out timer)) return;
        }

        timer.Dispose();
    }

    private void Expire(object? state)
    {
        string context = (string)state!;

        // Gone already means the key left the deck while the moment was running, and there is
        // nothing left to draw on.
        Timer? timer;
        lock (_gate)
        {
            if (!_showing.Remove(context, out timer)) return;
        }

        timer.Dispose();
        redraw(context);
    }

    public void Dispose()
    {
        Timer[] timers;
        lock (_gate)
        {
            timers = [.. _showing.Values];
            _showing.Clear();
        }

        foreach (var timer in timers) timer.Dispose();
    }
}
