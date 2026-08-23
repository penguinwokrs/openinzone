// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using Microsoft.Win32;

namespace OpenInzone.Tray;

/// <summary>
/// Starting with Windows, through the per-user Run key. Per-user rather than machine-wide so it
/// needs no elevation and cannot start for someone who never asked for it.
///
/// The registry is the only place this state lives - it is not mirrored into
/// <c>HotkeyConfig</c>. The installer writes this same value while offering its "run at startup"
/// task, so a copy kept in the configuration file would just be a second writer of the same fact:
/// whichever of the two last touched the value would win, unknowably. That is exactly what deleted
/// a freshly installed entry - the installer wrote the Run key, the tray then read a configuration
/// file left over from an older version with autostart off, and "reconciled" the registry to match
/// it, undoing the installer seconds after it ran.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // Task Manager's enabled/disabled toggle for a startup item does not touch Run at all - it
    // records its verdict here instead, keyed the same way. Deleting and recreating our Run value
    // does not touch this, so a record left over from an earlier "disable" survives and the
    // recreated entry inherits it. See IsEnabled and Set for how each end handles that.
    private const string StartupApprovedKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ValueName = "OpenInzone";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null && !IsMarkedDisabledInStartupApproved();
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");

            // Deleting rather than writing an "enabled" record: an item with no record at all is
            // exactly what a freshly installed entry looks like, and that state is unambiguous in
            // a way that guessing the right bytes to write would not be.
            ClearStartupApprovedRecord();
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);

            // Leave StartupApproved alone: the Run value is gone, so any record here is moot, and
            // the next enable clears it anyway.
        }
    }

    // The first byte of the record is a state flag; every value seen documented elsewhere (02, 06
    // enabled; 03, 07 disabled) and the 01 measured on a machine where Task Manager had disabled
    // this entry are all consistent with "even means enabled, odd means disabled" - that reading is
    // inferred from observation, not from documentation, so treat anything that is not a clean
    // even-first-byte binary value (missing, empty, wrong type, unreadable) as "no opinion" and
    // fall back to enabled, which is the state a record-free entry has anyway.
    private static bool IsMarkedDisabledInStartupApproved()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey);
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } record && record[0] % 2 != 0;
        }
        catch
        {
            // This corner of the registry is undocumented and not ours to depend on; a headset
            // control tray failing to start because a read it never asked for went wrong would be
            // a worse bug than the one this method exists to fix.
            return false;
        }
    }

    private static void ClearStartupApprovedRecord()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Best-effort, for the same reason as IsMarkedDisabledInStartupApproved: worst case a
            // stale record survives and the state it implies is whatever the next read falls back
            // to, not a crash.
        }
    }
}
