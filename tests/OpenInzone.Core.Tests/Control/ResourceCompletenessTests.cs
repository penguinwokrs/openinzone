// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

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
        data.Add(Path.Combine("src", "OpenInzone.Tray", "Resources"));
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
}
