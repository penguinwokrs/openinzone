// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Protocol;

/// <summary>
/// The pipe packets travel through: HID reports on the INZONE Buds dongle, a USB serial port on the
/// INZONE H9 / H7 dongle. <see cref="HciSession"/> reassembles packets from whatever bytes this hands back.
/// </summary>
public interface IHciTransport : IDisposable
{
    /// <summary>Sends one packet's bytes.</summary>
    void Write(ReadOnlySpan<byte> payload);

    /// <summary>
    /// Returns the next bytes to arrive, or null when the wait elapsed or <paramref name="cancelEvent"/> was signalled.
    /// </summary>
    byte[]? Read(int timeoutMilliseconds, IntPtr cancelEvent);
}
