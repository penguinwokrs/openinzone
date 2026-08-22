// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

public class KeyComboTests
{
    [Fact]
    public void Parses_modifiers_and_key()
    {
        var combo = KeyCombo.Parse("Ctrl+Alt+Shift+M");

        Assert.True((combo.Modifiers & HotkeyModifiers.Control) != 0);
        Assert.True((combo.Modifiers & HotkeyModifiers.Alt) != 0);
        Assert.True((combo.Modifiers & HotkeyModifiers.Shift) != 0);
        Assert.False((combo.Modifiers & HotkeyModifiers.Win) != 0);
        Assert.Equal('M', (char)combo.VirtualKey);
    }

    /// <summary>Auto-repeat would otherwise fire the action once per repeat tick.</summary>
    [Fact]
    public void Always_suppresses_auto_repeat()
    {
        Assert.True((KeyCombo.Parse("Ctrl+Alt+Up").Modifiers & HotkeyModifiers.NoRepeat) != 0);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Up")]
    [InlineData("Ctrl+Alt+Shift+M")]
    [InlineData("Ctrl+Alt+PageDown")]
    [InlineData("Win+F12")]
    [InlineData("Ctrl+VolumeMute")]
    [InlineData("Alt+Space")]
    [InlineData("Ctrl+Alt+Shift+Win+7")]
    public void Round_trips_through_formatting(string text)
    {
        var parsed = KeyCombo.Parse(text);

        var formatted = KeyCombo.FromKey(parsed.Modifiers, parsed.VirtualKey);

        Assert.Equal(text, formatted.Text);
        Assert.Equal(parsed.Modifiers, formatted.Modifiers);
        Assert.Equal(parsed.VirtualKey, formatted.VirtualKey);
    }

    [Fact]
    public void Formats_modifiers_in_a_fixed_order()
    {
        var combo = KeyCombo.Parse("Shift+Alt+Ctrl+M");

        Assert.Equal("Ctrl+Alt+Shift+M", KeyCombo.FromKey(combo.Modifiers, combo.VirtualKey).Text);
    }

    [Theory]
    [InlineData("pgup", "Ctrl+PageUp")]
    [InlineData("esc", "Ctrl+Escape")]
    [InlineData("ins", "Ctrl+Insert")]
    public void Accepts_the_short_key_names_and_formats_the_long_one(string alias, string canonical)
    {
        var combo = KeyCombo.Parse($"Ctrl+{alias}");

        Assert.Equal(canonical, KeyCombo.FromKey(combo.Modifiers, combo.VirtualKey).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+NotAKey")]
    [InlineData("Ctrl+A+B")]
    public void Rejects_what_it_cannot_register(string text)
    {
        Assert.False(KeyCombo.TryParse(text, out _));
        Assert.Throws<FormatException>(() => KeyCombo.Parse(text));
    }
}
