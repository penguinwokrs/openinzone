// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Protocol;

namespace OpenInzone.Settings;

/// <summary>
/// The events a headset has not answered, remembered so they are not asked for again.
/// </summary>
/// <remarks>
/// A model that does not have a setting never answers it, and every ask costs a full timeout. On an
/// INZONE H9 three of them made each reading of the settings take five seconds, with everything
/// queued behind it - and a window that showed the old values for that long after a click. They are
/// remembered for a while rather than for good, so one reply lost on the link does not hide a setting
/// until the next connection.
/// </remarks>
public sealed class SilentEvents(TimeSpan memory, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<EventId, DateTimeOffset> _since = [];

    /// <summary>True when the headset was silent on this id recently enough to take its word for it.</summary>
    public bool Contains(EventId eventId)
    {
        lock (_since)
            return _since.TryGetValue(eventId, out var at) && _time.GetUtcNow() - at < memory;
    }

    public void Add(EventId eventId)
    {
        lock (_since) _since[eventId] = _time.GetUtcNow();
    }
}
