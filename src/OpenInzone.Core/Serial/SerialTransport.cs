// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;
using OpenInzone.Native;
using OpenInzone.Protocol;

namespace OpenInzone.Serial;

/// <summary>
/// The control channel of the INZONE H9 / H7 dongle: the same packets as the HID dongles, as a raw
/// byte stream over its USB serial port. The line settings are INZONE Hub's - 460800 baud, 8N1, no
/// flow control, RTS and DTR raised - and nothing is sent when the port opens.
/// </summary>
public sealed class SerialTransport : IHciTransport
{
    private const uint BaudRate = 460800;
    private const int ReadChunk = 256;
    private const uint WriteTimeoutMilliseconds = 1000;

    // DCB bit fields: fBinary (bit 0), fDtrControl = DTR_CONTROL_ENABLE (bits 4-5), fRtsControl = RTS_CONTROL_ENABLE (bits 12-13).
    private const uint DcbFlags = 1u | (1u << 4) | (1u << 12);

    private readonly IntPtr _handle;
    private readonly IntPtr _readEvent;
    private readonly IntPtr _writeEvent;
    // Overlapped I/O keeps writing into the buffer after the call returns, so it must not move.
    private readonly byte[] _readBuffer = GC.AllocateArray<byte>(ReadChunk, pinned: true);
    private readonly object _writeLock = new();
    private bool _disposed;

    public SerialTransport(string devicePath)
    {
        // A serial port cannot be shared: this is open exclusively, and fails while INZONE Hub has it.
        _handle = NativeMethods.CreateFileW(devicePath,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE, 0,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, NativeMethods.FILE_FLAG_OVERLAPPED, IntPtr.Zero);

        if (_handle == NativeMethods.INVALID_HANDLE_VALUE)
        {
            int error = Marshal.GetLastWin32Error();

            // Access denied or a sharing violation is how Windows says another program has the port.
            string reason = error is NativeMethods.ERROR_ACCESS_DENIED or NativeMethods.ERROR_SHARING_VIOLATION
                ? "Another program has it: the OpenInzone tray or daemon (a Stream Deck starts it), or INZONE Hub. " +
                  "Only one of them can hold an INZONE H9 / H7 at a time, so quit the other first."
                : "Check that the dongle is plugged in.";
            throw new InvalidOperationException($"Could not open the INZONE serial port (error {error}). {reason}");
        }

        try
        {
            Configure();
        }
        catch
        {
            NativeMethods.CloseHandle(_handle);
            throw;
        }

        _readEvent = NativeMethods.CreateEventW(IntPtr.Zero, true, false, null);
        _writeEvent = NativeMethods.CreateEventW(IntPtr.Zero, true, false, null);
    }

    private void Configure()
    {
        var dcb = new Dcb { DcbLength = (uint)Marshal.SizeOf<Dcb>() };
        if (!NativeMethods.GetCommState(_handle, ref dcb)) throw Failure("GetCommState");

        dcb.BaudRate = BaudRate;
        dcb.ByteSize = 8;
        dcb.Parity = 0;     // NOPARITY
        dcb.StopBits = 0;   // ONESTOPBIT
        dcb.Flags = DcbFlags;
        if (!NativeMethods.SetCommState(_handle, ref dcb)) throw Failure("SetCommState");

        // With these three a read returns as soon as one byte is in, and otherwise after the constant.
        var timeouts = new CommTimeouts
        {
            ReadIntervalTimeout = uint.MaxValue,
            ReadTotalTimeoutMultiplier = uint.MaxValue,
            ReadTotalTimeoutConstant = 60_000,
        };
        if (!NativeMethods.SetCommTimeouts(_handle, ref timeouts)) throw Failure("SetCommTimeouts");

        NativeMethods.PurgeComm(_handle, NativeMethods.PURGE_RXCLEAR | NativeMethods.PURGE_TXCLEAR);
    }

    private static IOException Failure(string call) =>
        new($"{call} on the INZONE serial port failed (error {Marshal.GetLastWin32Error()}).");

    public void Write(ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_writeLock)
        {
            var buffer = GC.AllocateArray<byte>(payload.Length, pinned: true);
            payload.CopyTo(buffer);

            var overlapped = new NativeOverlapped32 { EventHandle = _writeEvent };
            NativeMethods.ResetEvent(_writeEvent);

            if (!NativeMethods.WriteFile(_handle, buffer, (uint)buffer.Length, IntPtr.Zero, ref overlapped))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ERROR_IO_PENDING)
                    throw new IOException($"WriteFile to the INZONE serial port failed (error {error}).");

                if (NativeMethods.WaitForSingleObject(_writeEvent, WriteTimeoutMilliseconds) != NativeMethods.WAIT_OBJECT_0)
                {
                    NativeMethods.CancelIoEx(_handle, IntPtr.Zero);
                    NativeMethods.GetOverlappedResult(_handle, ref overlapped, out _, true);
                    throw new TimeoutException("Timed out writing to the INZONE serial port.");
                }
            }

            NativeMethods.GetOverlappedResult(_handle, ref overlapped, out _, false);
        }
    }

    /// <summary>
    /// Returns whatever bytes arrived, or null when nothing did within the wait or the caller asked us to stop.
    /// A byte that lands while the read is being cancelled is returned rather than dropped, since losing
    /// one would desynchronise the packet stream.
    /// </summary>
    public byte[]? Read(int timeoutMilliseconds, IntPtr cancelEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var overlapped = new NativeOverlapped32 { EventHandle = _readEvent };
        NativeMethods.ResetEvent(_readEvent);

        if (!NativeMethods.ReadFile(_handle, _readBuffer, ReadChunk, IntPtr.Zero, ref overlapped))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != NativeMethods.ERROR_IO_PENDING)
                throw new IOException($"ReadFile from the INZONE serial port failed (error {error}).");

            var waitHandles = cancelEvent == IntPtr.Zero
                ? new[] { _readEvent }
                : new[] { _readEvent, cancelEvent };

            uint wait = NativeMethods.WaitForMultipleObjects((uint)waitHandles.Length, waitHandles, false, (uint)timeoutMilliseconds);
            if (wait != NativeMethods.WAIT_OBJECT_0) NativeMethods.CancelIoEx(_handle, IntPtr.Zero);
        }

        // Finished, or cancelled: either way the read is over once this returns, and it says how much came in.
        if (!NativeMethods.GetOverlappedResult(_handle, ref overlapped, out uint transferred, true))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != NativeMethods.ERROR_OPERATION_ABORTED)
                throw new IOException($"ReadFile from the INZONE serial port failed (error {error}).");
        }

        return transferred == 0 ? null : _readBuffer[..(int)transferred];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        NativeMethods.CancelIoEx(_handle, IntPtr.Zero);
        NativeMethods.CloseHandle(_handle);
        if (_readEvent != IntPtr.Zero) NativeMethods.CloseHandle(_readEvent);
        if (_writeEvent != IntPtr.Zero) NativeMethods.CloseHandle(_writeEvent);
    }
}
