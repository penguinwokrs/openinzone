// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;
using System.Xml.Linq;
using OpenInzone.StreamDeck;

namespace OpenInzone.Tests.StreamDeck;

/// <summary>
/// Stream Deck matches an event to an action by the string in manifest.json, and looks up every
/// image by a path with the extension left off. Both fail quietly: a mismatched action id gives a
/// key that does nothing, and a missing image gives a blank one. Nothing in the build notices, so
/// the manifest is checked here instead.
/// </summary>
public class ManifestTests
{
    private static readonly string PluginDirectory = FindPluginDirectory();
    private static readonly JsonDocument Manifest = ReadManifest();

    private static string FindPluginDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenInzone.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "plugin", "com.penguinwokrs.openinzone.sdPlugin");
    }

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(PluginDirectory, "manifest.json")));

    private static IEnumerable<JsonElement> Actions => Manifest.RootElement.GetProperty("Actions").EnumerateArray();

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    /// <summary>Resolves a manifest image reference, which never carries its extension.</summary>
    private static bool ImageExists(string reference)
    {
        string path = Path.Combine(PluginDirectory, reference.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path + ".svg") || File.Exists(path + ".png");
    }

    [Fact]
    public void The_manifest_declares_exactly_the_actions_the_plugin_implements()
    {
        var declared = Actions.Select(action => Text(action, "UUID")).OrderBy(id => id, StringComparer.Ordinal);

        Assert.Equal(ActionIds.All.OrderBy(id => id, StringComparer.Ordinal), declared);
    }

    [Fact]
    public void The_plugin_is_launched_by_the_name_the_project_builds()
    {
        Assert.Equal("openinzone-streamdeck.exe", Text(Manifest.RootElement, "CodePath"));
    }

    [Fact]
    public void Every_action_is_named_under_the_plugins_own_identifier()
    {
        string pluginId = Text(Manifest.RootElement, "UUID");

        Assert.Equal(ActionIds.Prefix, pluginId);
        Assert.All(Actions, action =>
            Assert.StartsWith(pluginId + ".", Text(action, "UUID"), StringComparison.Ordinal));
    }

    [Fact]
    public void Every_image_the_manifest_points_at_is_there()
    {
        Assert.True(ImageExists(Text(Manifest.RootElement, "Icon")), "the plugin's own icon");

        foreach (var action in Actions)
        {
            Assert.True(ImageExists(Text(action, "Icon")), $"{Text(action, "UUID")} icon");
            foreach (var state in action.GetProperty("States").EnumerateArray())
                Assert.True(ImageExists(Text(state, "Image")), $"{Text(action, "UUID")} state image");
        }
    }

    [Fact]
    public void Every_property_inspector_the_manifest_points_at_is_there()
    {
        foreach (var action in Actions)
        {
            if (!action.TryGetProperty("PropertyInspectorPath", out var path)) continue;

            Assert.True(File.Exists(Path.Combine(PluginDirectory,
                path.GetString()!.Replace('/', Path.DirectorySeparatorChar))), Text(action, "UUID"));
        }
    }

    /// <summary>
    /// The plugin's own icon is the one image Stream Deck will not take as SVG, so an SVG there
    /// would leave the plugin with no icon in the store and in the action list.
    /// </summary>
    [Fact]
    public void The_plugin_icon_is_a_png_because_that_is_all_Stream_Deck_accepts_for_it()
    {
        string reference = Text(Manifest.RootElement, "Icon").Replace('/', Path.DirectorySeparatorChar);

        Assert.True(File.Exists(Path.Combine(PluginDirectory, reference + ".png")));
        Assert.True(File.Exists(Path.Combine(PluginDirectory, reference + "@2x.png")));
    }

    [Fact]
    public void An_action_offers_a_settings_panel_exactly_when_it_has_a_step_to_configure()
    {
        foreach (var action in Actions)
        {
            string id = Text(action, "UUID");
            bool hasPanel = action.TryGetProperty("PropertyInspectorPath", out _);

            Assert.Equal(ActionIds.DefaultStep(id) != 0, hasPanel);
        }
    }

    [Fact]
    public void Every_action_can_be_placed_on_a_key_and_on_a_dial()
    {
        foreach (var action in Actions)
        {
            var controllers = action.GetProperty("Controllers").EnumerateArray()
                .Select(c => c.GetString()).ToList();

            Assert.Contains("Keypad", controllers);
            Assert.Contains("Encoder", controllers);
            Assert.True(action.TryGetProperty("Encoder", out _), $"{Text(action, "UUID")} has no dial layout");
        }
    }

    /// <summary>An SVG Stream Deck cannot parse shows as a blank key, exactly like a missing file.</summary>
    [Fact]
    public void Every_generated_image_is_well_formed()
    {
        var images = Directory.EnumerateFiles(Path.Combine(PluginDirectory, "images"), "*.svg",
            SearchOption.AllDirectories).ToList();

        Assert.NotEmpty(images);
        foreach (string image in images) XDocument.Load(image);
    }
}
