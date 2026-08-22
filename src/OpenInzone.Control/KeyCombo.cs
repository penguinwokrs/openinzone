// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Control;

/// <summary>A parsed hotkey: modifier flags plus a virtual key code.</summary>
public readonly record struct KeyCombo(uint Modifiers, uint VirtualKey, string Text)
{
    /// <summary>
    /// Named keys, canonical name first. Aliases are accepted when parsing; the first name is the
    /// one produced when formatting, so a captured keystroke always reads back the same way.
    /// </summary>
    private static readonly (string Canonical, uint VirtualKey, string[] Aliases)[] NamedKeys =
    [
        ("Up", 0x26, []), ("Down", 0x28, []), ("Left", 0x25, []), ("Right", 0x27, []),
        ("Home", 0x24, []), ("End", 0x23, []),
        ("PageUp", 0x21, ["pgup"]), ("PageDown", 0x22, ["pgdn"]),
        ("Insert", 0x2D, ["ins"]), ("Delete", 0x2E, ["del"]),
        ("Space", 0x20, []), ("Enter", 0x0D, ["return"]), ("Tab", 0x09, []),
        ("Escape", 0x1B, ["esc"]), ("Backspace", 0x08, []),
        ("NumpadPlus", 0x6B, ["add"]), ("NumpadMinus", 0x6D, ["subtract"]),
        ("NumpadMultiply", 0x6A, ["multiply"]), ("NumpadDivide", 0x6F, ["divide"]),
        ("VolumeUp", 0xAF, []), ("VolumeDown", 0xAE, []), ("VolumeMute", 0xAD, []),
        ("MediaNext", 0xB0, []), ("MediaPrev", 0xB1, []), ("MediaStop", 0xB2, []),
        ("MediaPlayPause", 0xB3, []),
    ];

    public static bool TryParse(string text, out KeyCombo combo)
    {
        try
        {
            combo = Parse(text);
            return true;
        }
        catch (FormatException)
        {
            combo = default;
            return false;
        }
    }

    public static KeyCombo Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new FormatException($"'{text}' is not a key combination.");

        uint modifiers = 0;
        string? keyName = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= HotkeyModifiers.Control; break;
                case "alt": modifiers |= HotkeyModifiers.Alt; break;
                case "shift": modifiers |= HotkeyModifiers.Shift; break;
                case "win" or "meta": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (keyName is not null)
                        throw new FormatException($"'{text}' names more than one key.");
                    keyName = part;
                    break;
            }
        }

        if (keyName is null) throw new FormatException($"'{text}' has modifiers but no key.");

        return FromKey(modifiers, ToVirtualKey(keyName, text));
    }

    /// <summary>Builds a combination from a live keystroke, giving it the canonical text.</summary>
    public static KeyCombo FromKey(uint modifiers, uint virtualKey)
    {
        modifiers |= HotkeyModifiers.NoRepeat;

        var text = new System.Text.StringBuilder();
        if ((modifiers & HotkeyModifiers.Control) != 0) text.Append("Ctrl+");
        if ((modifiers & HotkeyModifiers.Alt) != 0) text.Append("Alt+");
        if ((modifiers & HotkeyModifiers.Shift) != 0) text.Append("Shift+");
        if ((modifiers & HotkeyModifiers.Win) != 0) text.Append("Win+");
        text.Append(KeyName(virtualKey));

        return new KeyCombo(modifiers, virtualKey, text.ToString());
    }

    private static string KeyName(uint virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x70 and <= 0x87) return $"F{virtualKey - 0x70 + 1}";

        foreach (var (canonical, vk, _) in NamedKeys)
            if (vk == virtualKey) return canonical;

        throw new FormatException($"Virtual key 0x{virtualKey:X2} has no name.");
    }

    private static uint ToVirtualKey(string name, string original)
    {
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }

        if (name.Length is 2 or 3 && name[0] is 'f' or 'F'
            && int.TryParse(name[1..], out int fn) && fn is >= 1 and <= 24)
            return (uint)(0x70 + fn - 1);

        foreach (var (canonical, vk, aliases) in NamedKeys)
        {
            if (string.Equals(canonical, name, StringComparison.OrdinalIgnoreCase)) return vk;
            foreach (var alias in aliases)
                if (string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)) return vk;
        }

        throw new FormatException($"'{original}' uses a key name this application does not know: '{name}'.");
    }

    public override string ToString() => Text;
}
