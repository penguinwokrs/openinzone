// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using System.Text;

namespace OpenInzone.Ipc;

/// <summary>
/// A copy of OpenInzone that Scoop installed, and the script that updates it through Scoop - setup
/// would install a second copy beside it instead. Plain string work rather than System.IO.Path, so
/// the Linux test run splits Windows paths the same way Windows does.
/// </summary>
public sealed record ScoopInstall(string Root, string AppName)
{
    /// <summary>Where the junctions live, under %LOCALAPPDATA%. See <see cref="RunDirectory"/>.</summary>
    public const string RunRootName = "openinzone-scoop";

    public string AppDirectory => $@"{Root}\apps\{AppName}";

    public string CurrentDirectory => $@"{AppDirectory}\current";

    /// <summary>
    /// The junction a version runs through. Scoop refuses to update while any process's path is
    /// under <see cref="AppDirectory"/>, and a process started through a junction reports the
    /// junction's path - so the tray and the daemon run from here instead. One per version, never
    /// pointed at <c>current</c>: Scoop relinks that mid-update, under a tray still loading assemblies.
    /// </summary>
    public string RunDirectory(string localAppData, string version) =>
        $@"{localAppData.TrimEnd('\\', '/')}\{RunRootName}\{AppName}\{version}";

    /// <summary>
    /// Whether a tray running <paramref name="ownVersionDirectory"/> should restart: Scoop's
    /// <c>current</c> now points at another version, and that version's tray is already there.
    /// A missing target is Scoop between unlinking and relinking, not a new version.
    /// </summary>
    public static bool HasMovedOn(string ownVersionDirectory, string? currentTarget, Func<string, bool> fileExists)
    {
        if (currentTarget is null) return false;

        string target = currentTarget.TrimEnd('\\', '/');
        return !target.Equals(ownVersionDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
               && fileExists($@"{target}\inzonetray.exe");
    }

    /// <summary>
    /// <paramref name="baseDirectory"/> is the tray's own directory, <c>root\apps\name\version</c>
    /// (or <c>current</c>). The shape alone is not enough: <c>install.json</c>, which Scoop writes
    /// into every version it installs, is what tells it apart from a folder someone called apps.
    /// </summary>
    public static ScoopInstall? TryLocate(string baseDirectory, Func<string, bool> fileExists)
    {
        string directory = baseDirectory.TrimEnd('\\', '/');
        string[] parts = directory.Split('\\', '/');
        if (parts.Length < 4 || !parts[^3].Equals("apps", StringComparison.OrdinalIgnoreCase)) return null;
        if (!fileExists($@"{directory}\install.json")) return null;

        return new ScoopInstall(string.Join('\\', parts[..^3]), parts[^2]);
    }

    /// <summary>
    /// For Windows PowerShell 5.1, because it is always there. Holds the setup mutex so a Stream Deck
    /// plugin cannot restart the daemon mid-update, stops only this install's processes (Scoop
    /// refuses to update while any run from the app directory), updates, and brings the tray back.
    /// The window stays open when the version did not change, which covers both a failure and
    /// "already the latest" - the bucket not having the release yet.
    /// </summary>
    // ponytail: a global install (scoop -g) needs admin and "update -g"; this leaves it reported as not updated.
    public string BuildUpdateScript(int trayProcessId)
    {
        string app = Quote(AppDirectory);
        string scoop = Quote($@"{Root}\apps\scoop\current\bin\scoop.ps1");
        string manifest = Quote($@"{AppDirectory}\current\manifest.json");
        string tray = Quote($@"{AppDirectory}\current\inzonetray.exe");

        return $$"""
            $mutex = New-Object System.Threading.Mutex($true, 'OpenInzone.Setup')
            $before = (Get-Content {{manifest}} -Raw | ConvertFrom-Json).version
            $runs = Join-Path $env:LOCALAPPDATA {{Quote($@"{RunRootName}\{AppName}")}}
            try {
                Wait-Process -Id {{trayProcessId}} -Timeout 30 -ErrorAction SilentlyContinue
                Get-Process -Name inzonetray, inzoned, inzone -ErrorAction SilentlyContinue |
                    Where-Object { $_.Path -and ($_.Path.StartsWith({{app}} + '\', [StringComparison]::OrdinalIgnoreCase) -or
                                                 $_.Path.StartsWith($runs + '\', [StringComparison]::OrdinalIgnoreCase)) } |
                    Stop-Process -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 700
                & {{scoop}} update
                & {{scoop}} update {{Quote(AppName)}}
            } catch {
                Write-Host $_ -ForegroundColor Red
            } finally {
                $mutex.ReleaseMutex()
                $mutex.Dispose()
            }
            $after = (Get-Content {{manifest}} -Raw | ConvertFrom-Json).version
            Start-Process {{tray}}
            if ($before -eq $after) { Read-Host 'OpenInzone was not updated. Press Enter to close' }
            """;
    }

    /// <summary>
    /// Runs <see cref="BuildUpdateScript"/> in its own console window. Encoded, so no path in the
    /// script has to survive command-line quoting as well. PSModulePath is dropped because a tray
    /// started from a PowerShell 7 terminal carries 7's module directories, and Windows PowerShell
    /// then loads those instead of its own: Get-FileHash and Read-Host went missing and the update
    /// failed with its window already closed. Without the variable, it rebuilds its default.
    /// </summary>
    public ProcessStartInfo CreateUpdateStartInfo(int trayProcessId)
    {
        // Not through the shell, which cannot be given an environment; a console program started
        // this way from a process with no console still gets a window of its own.
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildUpdateScript(trayProcessId))));
        start.Environment.Remove("PSModulePath");
        return start;
    }

    // PowerShell closes a single-quoted string on any of these, and doubling is how each is escaped.
    private static string Quote(string value)
    {
        var quoted = new StringBuilder("'");
        foreach (char c in value)
        {
            if (c is '\'' or '‘' or '’' or '‚' or '‛') quoted.Append(c);
            quoted.Append(c);
        }
        return quoted.Append('\'').ToString();
    }
}
