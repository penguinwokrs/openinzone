// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;

namespace OpenInzone.Tray;

/// <summary>
/// Where the project lives, and the one way this application opens a page.
/// </summary>
/// <remarks>
/// Opening a URL is the only thing here that hands anything to the shell, so it is worth having
/// in one place: nothing composes an address from something it was given, and a browser that is
/// missing or refuses is never a reason to interrupt what the user was doing.
/// </remarks>
internal static class ProjectLinks
{
    public const string Repository = "https://github.com/penguinwokrs/openinzone";

    public const string LatestRelease = Repository + "/releases/latest";

    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser, or one that refused. There is nothing useful to say about it.
        }
    }
}
