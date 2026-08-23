// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;

namespace OpenInzone.Ipc;

/// <summary>
/// Finds and starts the daemon that owns the headset, so a client does not need it to have been
/// started for them.
/// </summary>
/// <remarks>
/// Clients live in different places. The tray and the CLI sit beside the daemon in the install
/// directory; the Stream Deck plugin lives inside Stream Deck's own plugins folder and has never
/// heard of it. So the search widens from "next to me" out to where the installer records having
/// put things, and finally to where it puts them by default.
/// </remarks>
public static class DaemonLauncher
{
    public const string ExecutableName = "inzoned.exe";

    private const string UninstallKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E1C6B4A-3F2D-4A77-9C55-1B7E9D0A6F31}_is1";

    private const string RunKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where the daemon is, or null if this machine has no copy where one is expected.</summary>
    public static string? Find() => FindIn(CandidateDirectories());

    /// <summary>The search itself, separated from where it looks so it can be exercised.</summary>
    internal static string? FindIn(IEnumerable<string?> directories)
    {
        foreach (string? directory in directories)
        {
            if (string.IsNullOrEmpty(directory)) continue;

            string candidate = Path.Combine(directory, ExecutableName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static IEnumerable<string?> CandidateDirectories()
    {
        // Beside whoever is asking: the tray, the CLI, and any build tree.
        yield return AppContext.BaseDirectory;

        if (!OperatingSystem.IsWindows()) yield break;

        // Where setup recorded putting it, which survives the user choosing a different directory.
        yield return ReadString(UninstallKey, "InstallLocation");

        // The autostart entry names the tray's full path, and the daemon sits beside it.
        string? trayCommand = ReadString(RunKey, "OpenInzone");
        yield return trayCommand is null ? null : Path.GetDirectoryName(trayCommand.Trim('"'));

        // Where a per-user install lands when nobody changes it.
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "OpenInzone");
    }

    private static string? ReadString(string key, string name)
    {
        // Checked again here rather than relying on the caller's guard: the platform analyser
        // cannot see through an iterator, and suppressing it would hide a real mistake later.
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            return Microsoft.Win32.Registry.GetValue(key, name, null) as string;
        }
        catch (Exception)
        {
            // A key that is not there, or a policy that forbids reading it, is not an error here:
            // it only means this is not where the daemon will be found.
            return null;
        }
    }

    /// <summary>
    /// Starts the daemon if a copy can be found. Returns false when there is none to start, or
    /// when starting it failed - in both cases the caller carries on showing an unavailable state.
    /// </summary>
    public static bool TryStart()
    {
        string? executable = Find();
        if (executable is null) return false;

        try
        {
            // No window: this is started behind a tray panel or a deck key, and a console flashing
            // up would be the only thing the user ever saw of it.
            using var process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
            });

            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
