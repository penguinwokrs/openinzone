// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;

namespace OpenInzone.Ipc;

/// <summary>
/// The file-system half of <see cref="ScoopInstall"/>: following junctions, and making the per-version
/// junction a Scoop install's tray and daemon run through so a manual <c>scoop update</c> is not
/// refused while they are running. Every member gives up quietly - running from <c>apps</c> as
/// before is always the fallback, and only a manual update is worse off for it.
/// </summary>
public static class ScoopRun
{
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>The install <paramref name="directory"/> belongs to, whether it is under apps or a run junction.</summary>
    public static ScoopInstall? Locate(string directory)
    {
        try
        {
            string? version = ResolveVersionDirectory(directory);
            return version is null ? null : ScoopInstall.TryLocate(version, File.Exists);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Follows junctions until a real directory: a run junction to its version directory, and
    /// <c>current</c> to the version it points at. Null when something along the way is missing.
    /// </summary>
    public static string? ResolveVersionDirectory(string directory)
    {
        try
        {
            string path = directory.TrimEnd('\\', '/');
            // A run junction points at a version, current points at a version: two hops at most.
            for (int hop = 0; hop < 3; hop++)
            {
                var info = new DirectoryInfo(path);
                if (!info.Exists) return null;
                if (info.LinkTarget is not { } target) return path;
                path = Path.GetFullPath(target, Path.GetDirectoryName(path)!).TrimEnd('\\', '/');
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="executablePath"/> reached through its version's run junction, made if it is
    /// not there yet. Null when the executable is not under a Scoop app directory - including when
    /// it is already running through a junction - or the junction could not be made.
    /// </summary>
    public static string? RunPathFor(string executablePath)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            string? directory = Path.GetDirectoryName(executablePath);
            if (directory is null || ScoopInstall.TryLocate(directory, File.Exists) is not { } install) return null;

            string? version = ResolveVersionDirectory(directory);
            if (version is null) return null;

            string run = install.RunDirectory(LocalAppData, Path.GetFileName(version));
            return EnsureJunction(run, version) ? Path.Combine(run, Path.GetFileName(executablePath)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes this install's run junctions whose version Scoop has since removed (<c>scoop cleanup</c>
    /// takes the directory and leaves the junction pointing at nothing).
    /// </summary>
    public static void PruneRunDirectories(ScoopInstall install)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            string root = Path.GetDirectoryName(install.RunDirectory(LocalAppData, "_"))!;
            if (!Directory.Exists(root)) return;

            foreach (string link in Directory.EnumerateDirectories(root))
            {
                try
                {
                    // Not recursive: on a junction that removes the link, never what it points at.
                    if (new DirectoryInfo(link).LinkTarget is { } target && !Directory.Exists(target))
                        Directory.Delete(link);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception)
        {
        }
    }

    private static bool EnsureJunction(string link, string target)
    {
        var existing = new DirectoryInfo(link);
        if (existing.Exists)
        {
            if (existing.LinkTarget is not { } pointsAt) return false; // a real directory: not ours to replace
            if (Path.GetFullPath(pointsAt).TrimEnd('\\').Equals(target, StringComparison.OrdinalIgnoreCase)) return true;
            Directory.Delete(link);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(link)!);

        // .NET only makes symbolic links, which need a privilege most accounts lack; a junction does
        // not, and mklink is the one thing on every machine that makes one. Two processes racing
        // here is fine: the loser's mklink fails and the check below finds the winner's junction.
        // ponytail: cmd expands %NAME% even inside quotes, so a path containing one breaks this; P/Invoke FSCTL_SET_REPARSE_POINT if that ever matters.
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using (var mklink = Process.Start(start)) mklink?.WaitForExit(5000);

        return new DirectoryInfo(link).LinkTarget is not null;
    }
}
