// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Protocol;

namespace OpenInzone.Cli;

/// <summary>
/// The words `inzone watch` accepts. They are deliberately the same words the JSON `event` field
/// uses, so `inzone watch battery` and `jq 'select(.event=="battery")'` share one vocabulary.
/// </summary>
internal static class WatchFilter
{
    private static readonly Dictionary<string, EventId> Names = new()
    {
        ["battery"] = EventId.BatteryInfo,
        ["balance"] = EventId.GameChatMixBalance,
        ["volume"] = EventId.HeadphoneVolume,
        ["mic"] = EventId.MicVolume,
        ["sidetone"] = EventId.SidetoneVolume,
    };

    /// <summary>An empty result means no filtering: every event is shown.</summary>
    public static bool TryParse(string[] words, out HashSet<EventId> events, out string? error)
    {
        events = [];
        error = null;

        foreach (string word in words)
        {
            if (!Names.TryGetValue(word.ToLowerInvariant(), out var eventId))
            {
                error = $"Unknown event '{word}'. Valid: {string.Join(", ", Names.Keys)}.";
                return false;
            }
            events.Add(eventId);
        }

        return true;
    }
}
