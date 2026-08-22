// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Daemon;

/// <summary>A parsed hotkey: modifier flags plus a virtual key code.</summary>
public readonly record struct KeyCombo(uint Modifiers, uint VirtualKey, string Text)
{
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
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; break;
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "shift": modifiers |= Native.MOD_SHIFT; break;
                case "win" or "meta": modifiers |= Native.MOD_WIN; break;
                default:
                    if (keyName is not null)
                        throw new FormatException($"'{text}' names more than one key.");
                    keyName = part;
                    break;
            }
        }

        if (keyName is null) throw new FormatException($"'{text}' has modifiers but no key.");

        return new KeyCombo(modifiers | Native.MOD_NOREPEAT, ToVirtualKey(keyName, text), text);
    }

    private static uint ToVirtualKey(string name, string original)
    {
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z') return c;
            if (c is >= '0' and <= '9') return c;
        }

        if (name.Length is 2 or 3 && (name[0] is 'f' or 'F') && int.TryParse(name[1..], out int fn) && fn is >= 1 and <= 24)
            return (uint)(0x70 + fn - 1);

        return name.ToLowerInvariant() switch
        {
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "insert" or "ins" => 0x2D,
            "delete" or "del" => 0x2E,
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "backspace" => 0x08,
            "numpadplus" or "add" => 0x6B,
            "numpadminus" or "subtract" => 0x6D,
            "numpadmultiply" or "multiply" => 0x6A,
            "numpaddivide" or "divide" => 0x6F,
            "volumeup" => 0xAF,
            "volumedown" => 0xAE,
            "volumemute" => 0xAD,
            "medianext" => 0xB0,
            "mediaprev" => 0xB1,
            "mediastop" => 0xB2,
            "mediaplaypause" => 0xB3,
            _ => throw new FormatException($"'{original}' uses a key name this daemon does not know: '{name}'."),
        };
    }

    public override string ToString() => Text;
}
