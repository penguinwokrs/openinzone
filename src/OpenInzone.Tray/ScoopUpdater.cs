// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using OpenInzone.Ipc;

namespace OpenInzone.Tray;

/// <summary>
/// What the tray does differently when Scoop installed it: update through Scoop, run through a
/// junction outside Scoop's app directory so a manual <c>scoop update</c> is not refused, and
/// register the one path Scoop keeps pointing at the installed version for autostart.
/// </summary>
public static class ScoopUpdater
{
    /// <summary>This process's install, or null when it did not come from Scoop.</summary>
    public static ScoopInstall? Current { get; } = ScoopRun.Locate(AppContext.BaseDirectory);

    /// <summary>
    /// The Run value on a Scoop install. Not this process's own path: that is a versioned junction,
    /// which would keep starting the old version after an update and nothing after scoop cleanup.
    /// </summary>
    public static string? AutostartCommand => Current is { } scoop ? $@"{scoop.CurrentDirectory}\inzonetray.exe" : null;

    /// <summary>Starts the update. The caller exits straight after; the script waits for that.</summary>
    public static void Run(ScoopInstall install) =>
        Process.Start(install.CreateUpdateStartInfo(Environment.ProcessId));

    /// <summary>
    /// Starts this tray again through its version's junction when it was started from Scoop's app
    /// directory - the Start menu shortcut, or autostart. True when that copy is on its way and
    /// this one should exit; false leaves this one running from where it is.
    /// </summary>
    public static bool TryRelaunchThroughJunction(string[] args)
    {
        if (Environment.ProcessPath is not { } path || ScoopRun.RunPathFor(path) is not { } run) return false;

        try
        {
            var start = new ProcessStartInfo(run) { UseShellExecute = false };
            foreach (string argument in args) start.ArgumentList.Add(argument);
            using var _ = Process.Start(start);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Stops the daemons running from <paramref name="directory"/>, so the tray that replaces this one
    /// talks to its own version rather than finding the old daemon still up.
    /// </summary>
    public static void StopDaemonsIn(string directory)
    {
        string prefix = directory.TrimEnd('\\') + '\\';
        foreach (var process in Process.GetProcessesByName("inzoned"))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is not { } file
                        || !file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    process.Kill();
                    process.WaitForExit(2000);
                }
                catch (Exception)
                {
                    // Gone already, or not ours to touch: either way nothing left to do here.
                }
            }
        }
    }
}
