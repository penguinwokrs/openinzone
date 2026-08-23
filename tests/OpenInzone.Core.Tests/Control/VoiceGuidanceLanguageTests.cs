// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Xml.Linq;
using OpenInzone.Model;

namespace OpenInzone.Tests.Control;

/// <summary>
/// The settings window offers three languages and the headset takes one byte, and nothing in the
/// build connects the words to the bytes: a list written in the wrong order gives a headset that
/// speaks Chinese to someone who asked for Japanese, and it compiles. That is not hypothetical -
/// it shipped as far as a tag before someone listened to it - so the pairing is pinned here.
/// </summary>
public class VoiceGuidanceLanguageTests
{
    /// <summary>What each byte was heard to do, in the words the window uses.</summary>
    private static readonly (VoiceGuidanceLanguage Language, string Japanese)[] Heard =
    [
        (VoiceGuidanceLanguage.English, "英語"),
        (VoiceGuidanceLanguage.Japanese, "日本語"),
        (VoiceGuidanceLanguage.Chinese, "中国語"),
    ];

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenInzone.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>The window's list, read out of the markup as tag and label together.</summary>
    private static (int Tag, string Content)[] LanguageItems()
    {
        string path = Path.Combine(Repository(), "src", "OpenInzone.Tray", "SettingsWindow.xaml");
        var xaml = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";

        var box = xaml.Descendants(presentation + "ComboBox")
            .Single(element => (string?)element.Attribute(xaml2006 + "Name") == "LanguageBox");

        return [.. box.Elements(presentation + "ComboBoxItem")
            .Select(item => (int.Parse((string)item.Attribute("Tag")!), (string)item.Attribute("Content")!))];
    }

    [Fact]
    public void The_window_offers_every_language_the_headset_takes_and_no_others()
    {
        var offered = LanguageItems().Select(item => (VoiceGuidanceLanguage)item.Tag);

        Assert.Equal(Enum.GetValues<VoiceGuidanceLanguage>(), offered.Order());
    }

    [Fact]
    public void Each_label_carries_the_byte_that_was_heard_to_produce_it()
    {
        var items = LanguageItems().ToDictionary(item => item.Content, item => item.Tag);

        Assert.All(Heard, heard => Assert.Equal((int)heard.Language, items[heard.Japanese]));
    }

    /// <summary>
    /// Japanese is 0x01. Written out so that a rename or a reordering of the enum cannot quietly
    /// move it: the byte is what the headset speaks, not an index into anything.
    /// </summary>
    [Theory]
    [InlineData(VoiceGuidanceLanguage.English, 0)]
    [InlineData(VoiceGuidanceLanguage.Japanese, 1)]
    [InlineData(VoiceGuidanceLanguage.Chinese, 2)]
    public void The_bytes_are_the_ones_the_headset_answered(VoiceGuidanceLanguage language, byte value) =>
        Assert.Equal(value, (byte)language);
}
