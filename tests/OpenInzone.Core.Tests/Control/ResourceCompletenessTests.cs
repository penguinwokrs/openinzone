// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OpenInzone.Tests.Control;

/// <summary>
/// A key present in the English resx and missing from a translation does not throw. ResourceManager
/// falls back to the neutral culture and serves the English quietly, so a half-translated build
/// looks finished until somebody reads it. Nothing else in the build would notice, so this does.
///
/// The files are read as XML off disk rather than through the generated classes, so this can cover
/// the tray's resources too without the test project referencing a Windows-only assembly.
/// </summary>
public class ResourceCompletenessTests
{
    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenInzone.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>
    /// Every resource directory in the repository.
    /// </summary>
    public static TheoryData<string> ResourceDirectories()
    {
        // Not a collection expression: xunit 2.5.3's TheoryData does not support one (CS0029).
        var data = new TheoryData<string>();
        data.Add(Path.Combine("src", "OpenInzone.Control", "Resources"));
        data.Add(Path.Combine("src", "OpenInzone.Resources"));
        return data;
    }

    private static string[] KeysIn(string path) =>
        [.. XDocument.Load(path).Root!.Elements("data")
            .Select(d => (string)d.Attribute("name")!)
            .Order()];

    [Theory]
    [MemberData(nameof(ResourceDirectories))]
    public void Every_translation_carries_every_key_the_English_file_has(string relativeDirectory)
    {
        string directory = Path.Combine(Repository(), relativeDirectory);
        string[] english = KeysIn(Path.Combine(directory, "Strings.resx"));

        Assert.NotEmpty(english);

        foreach (string culture in (string[])["ja", "zh-Hans"])
        {
            string[] translated = KeysIn(Path.Combine(directory, $"Strings.{culture}.resx"));

            Assert.Equal(english, translated);
        }
    }

    [Theory]
    [MemberData(nameof(ResourceDirectories))]
    public void No_translated_value_is_left_empty(string relativeDirectory)
    {
        string directory = Path.Combine(Repository(), relativeDirectory);

        foreach (string culture in (string[])["ja", "zh-Hans"])
        {
            var document = XDocument.Load(Path.Combine(directory, $"Strings.{culture}.resx"));

            Assert.All(document.Root!.Elements("data"), entry =>
                Assert.False(string.IsNullOrWhiteSpace((string?)entry.Element("value")),
                    $"{culture}: {(string?)entry.Attribute("name")} has no value"));
        }
    }

    /// <summary>
    /// The English file is the one every translation is diffed against below, so an empty value
    /// here would otherwise slip through with no other signal - unlike the translations, nothing
    /// checks it against anything else.
    /// </summary>
    [Theory]
    [MemberData(nameof(ResourceDirectories))]
    public void No_English_value_is_left_empty(string relativeDirectory)
    {
        string directory = Path.Combine(Repository(), relativeDirectory);
        var document = XDocument.Load(Path.Combine(directory, "Strings.resx"));

        Assert.All(document.Root!.Elements("data"), entry =>
            Assert.False(string.IsNullOrWhiteSpace((string?)entry.Element("value")),
                $"English: {(string?)entry.Attribute("name")} has no value"));
    }

    // Matches {0}, {1:0.0}, etc. and captures just the index - the format specifier after the
    // colon is free to differ (or vanish) between languages, so it plays no part in the guard.
    private static readonly Regex PlaceholderIndex = new(@"\{(\d+)(?::[^{}]*)?\}", RegexOptions.Compiled);

    private static HashSet<int> PlaceholderIndices(string value) =>
        [.. PlaceholderIndex.Matches(value).Select(m => int.Parse(m.Groups[1].Value))];

    /// <summary>
    /// A translation that drops a {0}, or turns it into a {1} that the English text never had,
    /// compiles cleanly and throws FormatException only once that code path actually runs -
    /// several of these have no other test coverage at all (the hotkey-rejection balloon, the
    /// config-unreadable balloon, the download-progress text). Comparing the *set* of indices
    /// rather than a count or the raw text is deliberate: word order legitimately differs between
    /// languages, and Settings_PluginFilter's English text uses {0} twice on purpose.
    /// </summary>
    [Theory]
    [MemberData(nameof(ResourceDirectories))]
    public void Every_translation_keeps_the_same_placeholders_as_English(string relativeDirectory)
    {
        string directory = Path.Combine(Repository(), relativeDirectory);
        var english = XDocument.Load(Path.Combine(directory, "Strings.resx")).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

        foreach (string culture in (string[])["ja", "zh-Hans"])
        {
            var document = XDocument.Load(Path.Combine(directory, $"Strings.{culture}.resx"));

            foreach (var entry in document.Root!.Elements("data"))
            {
                string key = (string)entry.Attribute("name")!;
                var expected = PlaceholderIndices(english[key]);
                var actual = PlaceholderIndices((string)entry.Element("value")!);

                Assert.True(expected.SetEquals(actual),
                    $"{culture}: {key} has placeholders {{{string.Join(",", actual.Order())}}}, " +
                    $"expected {{{string.Join(",", expected.Order())}}}");
            }
        }
    }
}
