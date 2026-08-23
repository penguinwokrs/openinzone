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
    private const string ValueName = "OpenInzone";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
