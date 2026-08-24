// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text;

namespace OpenInzone.Ipc;

/// <summary>
/// The contract between the daemon that owns the headset and everything that drives it: the
/// tray's panel, the CLI, the Stream Deck plugin.
/// </summary>
/// <remarks>
/// Messages are UTF-8 JSON, one object per line, over a named pipe. Commands are not acknowledged:
/// the daemon answers every change by pushing a whole snapshot, so a client never has to correlate
/// a reply with a request, and a client that misses a push still converges on the next one.
/// </remarks>
public static class IpcProtocol
{
    /// <summary>Raised when the wire format changes in a way an older client cannot read.</summary>
    public const int Version = 2;

    /// <summary>A line longer than this is treated as a broken peer rather than parsed.</summary>
    public const int MaxLineBytes = 64 * 1024;

    /// <summary>
    /// Pipe names are machine-wide on Windows, so the user is part of the name: without it the
    /// first user to log in would own the name and every other session would fail to serve.
    /// </summary>
    public static string PipeName(string? userName = null)
    {
        var name = new StringBuilder("OpenInzone.Daemon.");
        foreach (char c in userName ?? Environment.UserName)
            name.Append(char.IsLetterOrDigit(c) ? c : '_');
        return name.Append(".v").Append(Version).ToString();
    }

    /// <summary>
    /// Names the lock that keeps one daemon serving this channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The version belongs here for the same reason it belongs in the pipe name, and leaving it out
    /// was a bug that only appeared once there was a second version to have. A daemon holds this
    /// lock for as long as it serves; a newer one starting up would find it held and step aside, on
    /// the reasoning that whoever is already serving will serve the client that tried to start it.
    /// That reasoning holds only while both speak the same version. Across versions the newer
    /// daemon's clients are looking for a different pipe, so they are never served at all — and
    /// because the lock goes to whoever started first, an old build left running beats every new
    /// one, silently and indefinitely.
    /// </para>
    /// <para>
    /// So the two versions serve side by side for as long as a client of the older one keeps it
    /// alive. That means two processes holding the headset, which is the thing the daemon exists to
    /// prevent — but it is the lesser fault by a wide margin. The interfaces already share this
    /// channel with INZONE Hub, which does the same thing; a new build that never works at all and
    /// says nothing about why is not comparable.
    /// </para>
    /// </remarks>
    public static string SingleInstanceName() => $"OpenInzone.Daemon.SingleInstance.v{Version}";
}
