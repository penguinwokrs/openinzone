// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Control;

/// <summary>
/// Which language the tray shows itself in. Deliberately not the operating system's: the choice is
/// made once, by the installer, and then only changes when someone changes it. Reading
/// CurrentUICulture here instead would mean a machine that switches to Korean silently switches
/// the tray to English, having never been asked.
///
/// This governs CurrentUICulture only. Number formatting - the decimal point in a balance reading,
/// the digit grouping in a byte count - follows CurrentCulture, which this class leaves alone and
/// which therefore keeps following the operating system. A reader who set Windows to a German
/// locale keeps seeing "1,5" regardless of which of the three UI languages they picked; that split
/// is deliberate, not an oversight.
/// </summary>
public static class UiLanguage
{
    /// <summary>Written by the installer beside the executable. Absent in the zip download, which
    /// is exactly how the zip ends up in English without a special case for it.</summary>
    public const string MarkerFileName = "default-language";

    public const string Fallback = "en";

    public static IReadOnlyList<string> Supported { get; } = ["en", "ja", "zh-Hans"];

    /// <summary>
    /// The supported tag this text names, or null if it names none. Case-insensitive and
    /// whitespace-tolerant: both sources are files a person may have typed into.
    /// </summary>
    public static string? Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        return Supported.FirstOrDefault(
            tag => string.Equals(tag, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The configured choice, else what the installer detected, else English. Every failure along
    /// the way is a fall-through rather than a throw: an unreadable preference must not be the
    /// reason a tray icon never appears.
    /// </summary>
    public static string Resolve(string? configured, string applicationDirectory) =>
        Normalise(configured)
        ?? Normalise(ReadMarker(applicationDirectory))
        ?? Fallback;

    private static string? ReadMarker(string applicationDirectory)
    {
        try
        {
            string path = Path.Combine(applicationDirectory, MarkerFileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            // An unreadable marker is a marker that says nothing, not a reason to fail.
            return null;
        }
    }
}
