// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;
using OpenInzone.Native;

namespace OpenInzone.Hid;

/// <summary>
/// Raw HID report I/O for the INZONE control collection.
/// Every report carries a two-byte header: [0] report id, [1] number of payload bytes in this report.
/// Payloads longer than one report are split, matching the vendor application's transport.
/// </summary>
public sealed class HidTransport : IDisposable
{
    private readonly IntPtr _handle;
    private readonly IntPtr _readEvent;
    private readonly IntPtr _writeEvent;
    private readonly object _writeLock = new();
    private bool _disposed;

    public int InputReportLength { get; }
    public int OutputReportLength { get; }
    public byte InputReportId { get; }
    public byte OutputReportId { get; }

    /// <summary>Bytes of payload that fit in a single report.</summary>
    public int MaxPayloadPerReport => OutputReportLength - HeaderSize;

    public const int HeaderSize = 2;

    public HidTransport(string devicePath, int inputReportLength, int outputReportLength,
        byte inputReportId, byte outputReportId)
    {
        InputReportLength = inputReportLength;
        OutputReportLength = outputReportLength;
        InputReportId = inputReportId;
        OutputReportId = outputReportId;

        _handle = NativeMethods.CreateFileW(devicePath,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, NativeMethods.FILE_FLAG_OVERLAPPED, IntPtr.Zero);

        if (_handle == NativeMethods.INVALID_HANDLE_VALUE)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Could not open the INZONE control interface (error {error}). " +
                "Check that the dongle is plugged in.");
        }

        _readEvent = NativeMethods.CreateEventW(IntPtr.Zero, true, false, null);
        _writeEvent = NativeMethods.CreateEventW(IntPtr.Zero, true, false, null);
    }

    /// <summary>Writes a payload, splitting it across as many reports as needed.</summary>
    public void Write(ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_writeLock)
        {
            int offset = 0;
            do
            {
                int chunk = Math.Min(payload.Length - offset, MaxPayloadPerReport);
                var report = new byte[OutputReportLength];
                report[0] = OutputReportId;
                report[1] = (byte)chunk;
                payload.Slice(offset, chunk).CopyTo(report.AsSpan(HeaderSize));

                WriteReport(report);
                offset += chunk;
            }
            while (offset < payload.Length);
        }
    }

    private void WriteReport(byte[] report)
    {
        var overlapped = new NativeOverlapped32 { EventHandle = _writeEvent };
        NativeMethods.ResetEvent(_writeEvent);

        if (NativeMethods.WriteFile(_handle, report, (uint)report.Length, IntPtr.Zero, ref overlapped))
            return;

        int error = Marshal.GetLastWin32Error();
        if (error != NativeMethods.ERROR_IO_PENDING)
            throw new IOException($"WriteFile to the INZONE control interface failed (error {error}).");

        if (NativeMethods.WaitForSingleObject(_writeEvent, 1000) != NativeMethods.WAIT_OBJECT_0)
        {
            NativeMethods.CancelIoEx(_handle, IntPtr.Zero);
            throw new TimeoutException("Timed out writing to the INZONE control interface.");
        }

        NativeMethods.GetOverlappedResult(_handle, ref overlapped, out _, false);
    }

    /// <summary>
    /// Reads one report and returns its payload, or null when the wait elapsed or the read was cancelled.
    /// Reports carrying a different report id are skipped.
    /// </summary>
    public byte[]? Read(int timeoutMilliseconds, IntPtr cancelEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (true)
        {
            int remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
            if (remaining == 0) return null;

            var buffer = new byte[InputReportLength];
            var overlapped = new NativeOverlapped32 { EventHandle = _readEvent };
            NativeMethods.ResetEvent(_readEvent);

            uint transferred;
            if (!NativeMethods.ReadFile(_handle, buffer, (uint)InputReportLength, IntPtr.Zero, ref overlapped))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ERROR_IO_PENDING)
                    throw new IOException($"ReadFile from the INZONE control interface failed (error {error}).");

                var waitHandles = cancelEvent == IntPtr.Zero
                    ? new[] { _readEvent }
                    : new[] { _readEvent, cancelEvent };

                uint wait = NativeMethods.WaitForMultipleObjects((uint)waitHandles.Length, waitHandles, false, (uint)remaining);
                if (wait != NativeMethods.WAIT_OBJECT_0)
                {
                    // Timed out, or the caller asked us to stop.
                    NativeMethods.CancelIoEx(_handle, IntPtr.Zero);
                    NativeMethods.GetOverlappedResult(_handle, ref overlapped, out _, true);
                    return null;
                }
            }

            if (!NativeMethods.GetOverlappedResult(_handle, ref overlapped, out transferred, false)) return null;
            if (transferred < HeaderSize) continue;
            if (buffer[0] != InputReportId) continue;

            int length = Math.Min(buffer[1], InputReportLength - HeaderSize);
            var payload = new byte[length];
            Array.Copy(buffer, HeaderSize, payload, 0, length);
            return payload;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        NativeMethods.CancelIoEx(_handle, IntPtr.Zero);
        if (_handle != NativeMethods.INVALID_HANDLE_VALUE) NativeMethods.CloseHandle(_handle);
        if (_readEvent != IntPtr.Zero) NativeMethods.CloseHandle(_readEvent);
        if (_writeEvent != IntPtr.Zero) NativeMethods.CloseHandle(_writeEvent);
    }
}
