// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Runtime.InteropServices;

namespace OpenInzone.Ipc;

/// <summary>
/// Starts a process that outlives whoever started it.
/// </summary>
/// <remarks>
/// A daemon must not be a child in any sense that matters. Process.Start hands the new process
/// into the caller's job object, and a launcher whose job is set to kill on close takes the daemon
/// down with it - measured here with a PowerShell pipeline and with WSL's interop, both of which
/// killed a daemon the moment the program that asked for it finished. Stream Deck may well manage
/// its plugins the same way.
///
/// CREATE_BREAKAWAY_FROM_JOB is what asks to be left out of that. A job may forbid it, so a
/// refusal falls back to an ordinary start rather than to no daemon at all.
/// </remarks>
internal static partial class DetachedProcess
{
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const uint CreateNoWindow = 0x08000000;
    private const uint DetachedProcessFlag = 0x00000008;
    private const int ErrorAccessDenied = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StdInput, StdOutput, StdError;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(
        string? applicationName, ref char commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags, IntPtr environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    /// <summary>Starts the executable with no window and no ties. False if it could not be started.</summary>
    public static bool Start(string executablePath, string? workingDirectory)
    {
        if (!OperatingSystem.IsWindows()) return false;

        // CreateProcess may write to the command line buffer, so it cannot be a literal.
        char[] commandLine = $"\"{executablePath}\"\0".ToCharArray();

        const uint common = CreateNoWindow | DetachedProcessFlag;
        if (TryCreate(commandLine, common | CreateBreakawayFromJob, workingDirectory)) return true;

        // A job that forbids breaking away answers with access denied. The daemon is then tied to
        // this process's lifetime, which is worse than not being tied - but far better than the
        // headset having no owner at all.
        if (Marshal.GetLastWin32Error() != ErrorAccessDenied) return false;

        return TryCreate(commandLine, common, workingDirectory);
    }

    private static bool TryCreate(char[] commandLine, uint flags, string? workingDirectory)
    {
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };

        if (!CreateProcess(null, ref commandLine[0], IntPtr.Zero, IntPtr.Zero, false,
                flags, IntPtr.Zero, workingDirectory, ref startup, out var information))
            return false;

        // Nothing here waits on the daemon, so both handles go straight back.
        CloseHandle(information.Thread);
        CloseHandle(information.Process);
        return true;
    }
}
