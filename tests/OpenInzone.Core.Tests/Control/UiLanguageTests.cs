// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

/// <summary>
/// The tray never asks Windows what language it is in. It asks the configuration, then the file
/// the installer left behind, then gives up and speaks English. These pin that order, and pin
/// that nothing malformed anywhere in it can stop the tray from starting.
/// </summary>
public class UiLanguageTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "openinzone-lang-" + Guid.NewGuid().ToString("N"));

    public UiLanguageTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void WriteMarker(string content) =>
        File.WriteAllText(Path.Combine(_directory, UiLanguage.MarkerFileName), content);

    [Fact]
    public void The_configured_language_wins_over_the_installers_choice()
    {
        WriteMarker("ja");
        Assert.Equal("zh-Hans", UiLanguage.Resolve("zh-Hans", _directory));
    }

    [Fact]
    public void The_installers_choice_is_used_when_nothing_is_configured()
    {
        WriteMarker("ja");
        Assert.Equal("ja", UiLanguage.Resolve(null, _directory));
    }

    [Fact]
    public void English_is_the_answer_when_neither_says_anything()
    {
        Assert.Equal("en", UiLanguage.Resolve(null, _directory));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("zh-TW")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EN; DROP TABLE")]
    public void An_unsupported_configured_value_falls_through_rather_than_being_used(string configured)
    {
        WriteMarker("ja");
        Assert.Equal("ja", UiLanguage.Resolve(configured, _directory));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("")]
    [InlineData("\n\n")]
    public void An_unsupported_marker_falls_through_to_English(string marker)
    {
        WriteMarker(marker);
        Assert.Equal("en", UiLanguage.Resolve(null, _directory));
    }

    [Fact]
    public void A_marker_written_with_stray_whitespace_still_reads()
    {
        WriteMarker("  zh-Hans \r\n");
        Assert.Equal("zh-Hans", UiLanguage.Resolve(null, _directory));
    }

    [Fact]
    public void A_directory_that_does_not_exist_is_not_an_error()
    {
        Assert.Equal("en", UiLanguage.Resolve(null, Path.Combine(_directory, "nope")));
    }

    [Fact]
    public void Every_supported_tag_normalises_to_itself()
    {
        Assert.Equal(["en", "ja", "zh-Hans"], UiLanguage.Supported);
        Assert.All(UiLanguage.Supported, tag => Assert.Equal(tag, UiLanguage.Normalise(tag)));
    }

    [Fact]
    public void Normalising_is_case_insensitive_because_a_hand_edited_file_will_not_match_exactly()
    {
        Assert.Equal("zh-Hans", UiLanguage.Normalise("ZH-HANS"));
        Assert.Equal("en", UiLanguage.Normalise("EN"));
    }

    [Fact]
    public void Normalising_something_unsupported_gives_null_rather_than_a_guess()
    {
        Assert.Null(UiLanguage.Normalise("fr"));
        Assert.Null(UiLanguage.Normalise(null));
    }
}
