// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;

namespace OpenInzone.Ipc;

/// <summary>
/// The contract between the tray and anything that drives it - today the Stream Deck plugin.
/// </summary>
/// <remarks>
/// Messages are UTF-8 JSON, one object per line, over a named pipe. Commands are not acknowledged:
/// the tray answers every change by pushing a whole snapshot, so a client never has to correlate a
/// reply with a request, and a client that misses a push still converges on the next one.
/// </remarks>
public static class IpcProtocol
{
    /// <summary>Raised when the wire format changes in a way an older client cannot read.</summary>
    public const int Version = 1;

    /// <summary>A line longer than this is treated as a broken peer rather than parsed.</summary>
    public const int MaxLineBytes = 64 * 1024;

    /// <summary>
    /// Pipe names are machine-wide on Windows, so the user is part of the name: without it the
    /// first user to log in would own the name and every other session would fail to serve.
    /// </summary>
    public static string PipeName(string? userName = null)
    {
        var name = new StringBuilder("OpenInzone.Tray.");
        foreach (char c in userName ?? Environment.UserName)
            name.Append(char.IsLetterOrDigit(c) ? c : '_');
        return name.Append(".v").Append(Version).ToString();
    }
}
