// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Control;

/// <summary>
/// The modifier bits RegisterHotKey takes. Declared here rather than beside the P/Invoke so that
/// combinations can be parsed, formatted and tested without a window.
/// </summary>
public static class HotkeyModifiers
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;

    /// <summary>Stops auto-repeat from firing the action once per keyboard repeat tick.</summary>
    public const uint NoRepeat = 0x4000;
}
