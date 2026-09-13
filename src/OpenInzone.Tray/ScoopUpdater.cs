// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using System.IO;
using System.Text;
using OpenInzone.Control;

namespace OpenInzone.Tray;

/// <summary>
/// The update path for a copy Scoop installed. Scoop does the download, the hash check and the
/// switch of <c>current</c>, so all this does is start its script in a window the user can watch.
/// </summary>
public static class ScoopUpdater
{
    /// <summary>This process's install, or null when it did not come from Scoop.</summary>
    public static ScoopInstall? Current { get; } = ScoopInstall.TryLocate(AppContext.BaseDirectory, File.Exists);

    /// <summary>Starts the update. The caller exits straight after; the script waits for that.</summary>
    public static void Run(ScoopInstall install)
    {
        // Encoded, so no path in the script has to survive command-line quoting as well.
        string encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(install.BuildUpdateScript(Environment.ProcessId)));
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}") { UseShellExecute = true });
    }
}
