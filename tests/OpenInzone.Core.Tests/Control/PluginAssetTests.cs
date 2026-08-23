// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

public class PluginAssetTests
{
    private const string Trusted =
        "https://github.com/penguinwokrs/openinzone/releases/download/v0.3.0/com.penguinwokrs.openinzone.streamDeckPlugin";

    private static string Release(string assets) => $$"""{"tag_name":"v0.3.0","assets":[{{assets}}]}""";

    private static string Asset(string name, string url, long size = 6_600_000) =>
        $$"""{"name":"{{name}}","browser_download_url":"{{url}}","size":{{size}}}""";

    [Fact]
    public void The_plugin_is_found_among_the_other_assets()
    {
        string json = Release(
            Asset("OpenInzone-0.3.0-setup.exe", "https://github.com/penguinwokrs/openinzone/releases/download/v0.3.0/OpenInzone-0.3.0-setup.exe")
            + "," + Asset("com.penguinwokrs.openinzone.streamDeckPlugin", Trusted));

        var found = PluginAsset.FromRelease(json);

        Assert.True(found.Found);
        Assert.Equal(Trusted, found.DownloadUrl);
        Assert.Equal("com.penguinwokrs.openinzone.streamDeckPlugin", found.FileName);
        Assert.Equal(6_600_000, found.SizeBytes);
    }

    [Fact]
    public void A_release_with_no_plugin_offers_nothing()
    {
        string json = Release(Asset("OpenInzone-0.3.0-setup.exe",
            "https://github.com/penguinwokrs/openinzone/releases/download/v0.3.0/OpenInzone-0.3.0-setup.exe"));

        Assert.False(PluginAsset.FromRelease(json).Found);
    }

    /// <summary>
    /// This gets handed to an HttpClient and then to Stream Deck, so it goes through the same
    /// allowlist an installer does: a forged response naming somebody else's repository is
    /// exactly what that check exists to refuse.
    /// </summary>
    [Theory]
    [InlineData("http://github.com/penguinwokrs/openinzone/releases/download/v0.3.0/x.streamDeckPlugin")]
    [InlineData("https://github.com.evil.example/penguinwokrs/openinzone/releases/download/v0.3.0/x.streamDeckPlugin")]
    [InlineData("https://github.com/someone-else/theirs/releases/download/v1/x.streamDeckPlugin")]
    [InlineData("https://objects.githubusercontent.com/penguinwokrs/openinzone/releases/download/v0.3.0/x.streamDeckPlugin")]
    public void An_asset_from_somewhere_this_will_not_fetch_from_is_ignored(string url)
    {
        Assert.False(PluginAsset.FromRelease(Release(Asset("x.streamDeckPlugin", url))).Found);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"assets":"nope"}""")]
    public void An_answer_that_cannot_be_read_offers_nothing(string json) =>
        Assert.False(PluginAsset.FromRelease(json).Found);

    [Fact]
    public void The_extension_is_matched_however_it_is_cased()
    {
        string json = Release(Asset("Plugin.STREAMDECKPLUGIN",
            "https://github.com/penguinwokrs/openinzone/releases/download/v0.3.0/Plugin.STREAMDECKPLUGIN"));

        Assert.True(PluginAsset.FromRelease(json).Found);
    }
}

public class PluginSaveFolderTests
{
    [Fact]
    public void The_chosen_folder_survives_a_save_and_a_load()
    {
        var config = HotkeyConfig.Default();
        config.PluginSaveFolder = @"D:\decks";

        var back = HotkeyConfig.FromJson(Round(config));

        Assert.Equal(@"D:\decks", back.PluginSaveFolder);
    }

    /// <summary>Never asked is not the same as asked and answered with nothing.</summary>
    [Fact]
    public void A_folder_nobody_has_chosen_stays_unset()
    {
        Assert.Null(HotkeyConfig.FromJson(Round(HotkeyConfig.Default())).PluginSaveFolder);
        Assert.Null(HotkeyConfig.FromJson("""{"pluginSaveFolder":"   "}""").PluginSaveFolder);
        Assert.Null(HotkeyConfig.FromJson("""{"pluginSaveFolder":null}""").PluginSaveFolder);
    }

    private static string Round(HotkeyConfig config)
    {
        string path = Path.Combine(Path.GetTempPath(), $"openinzone-{Guid.NewGuid():N}.json");
        try
        {
            config.Save(path);
            return File.ReadAllText(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
