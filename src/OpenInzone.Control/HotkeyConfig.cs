// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenInzone.Control;

/// <summary>
/// Which key runs which command, keyed by <see cref="HotkeyCommand.Id"/>. An empty string means the
/// command is deliberately unbound. Commands missing from the file fall back to their default, so a
/// file written by an older version keeps working when a command is added.
/// </summary>
public sealed class HotkeyConfig
{
    public Dictionary<string, string> Bindings { get; init; } = [];

    // Off by default: reaching the network on every login is not something to switch on for
    // someone without asking.
    public bool CheckForUpdatesAtStartup { get; set; }

    /// <summary>
    /// Where the settings window last saved the Stream Deck plugin. Null means it has never been
    /// asked, and the downloads folder stands in.
    /// </summary>
    public string? PluginSaveFolder { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static HotkeyConfig Default() => new()
    {
        Bindings = HotkeyCommand.All.ToDictionary(c => c.Id, c => c.DefaultCombo),
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "openinzone", "hotkeys.json");

    public static HotkeyConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = Default();
            fresh.Save(path);
            return fresh;
        }

        return FromJson(File.ReadAllText(path));
    }

    public void Save(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var json = new JsonObject
        {
            ["bindings"] = new JsonObject(Bindings.Select(b => KeyValuePair.Create(b.Key, (JsonNode?)b.Value))),
            ["checkForUpdatesAtStartup"] = CheckForUpdatesAtStartup,
            ["pluginSaveFolder"] = PluginSaveFolder,
        };

        // Write beside the file and move it into place, so a save interrupted part-way leaves the
        // previous configuration intact rather than a truncated file that then refuses to load.
        // Same directory, so the move stays on one volume and is a rename rather than a copy.
        string temporary = Path.Combine(directory, Path.GetFileName(path) + ".tmp");
        File.WriteAllText(temporary, json.ToJsonString(Options));
        File.Move(temporary, path, overwrite: true);
    }

    public static HotkeyConfig FromJson(string json)
    {
        var config = Default();
        if (JsonNode.Parse(json) is not JsonObject root)
            throw new InvalidDataException("The hotkey configuration is not a JSON object.");

        // "autostart" may still be present in a file saved by an older version. It carries no
        // meaning any more - see Autostart's class comment for why - and is left unread rather
        // than parsed, so a stale or malformed value here is never a reason to refuse the file.
        if (root["checkForUpdatesAtStartup"] is JsonValue checkForUpdates)
            config.CheckForUpdatesAtStartup = checkForUpdates.GetValue<bool>();

        if (root["pluginSaveFolder"] is JsonValue folder && folder.TryGetValue(out string? path)
            && !string.IsNullOrWhiteSpace(path))
            config.PluginSaveFolder = path;

        switch (root["bindings"])
        {
            // The daemon's shape: an array of action/delta/value triples.
            case JsonArray legacy:
                Migrate(legacy, config);
                break;

            case JsonObject current:
                foreach (var (id, value) in current)
                    if (HotkeyCommand.ById(id) is not null)
                        config.Bindings[id] = value?.GetValue<string>() ?? "";
                break;

            // A bindings value that is neither shape is treated like a missing one: every
            // command keeps its default rather than the file failing to load altogether.
            default:
                break;
        }

        return config;
    }

    /// <summary>
    /// Maps the daemon's action plus delta onto a command id. Anything unrecognised is dropped
    /// rather than guessed at; the command keeps its default.
    /// </summary>
    private static void Migrate(JsonArray legacy, HotkeyConfig config)
    {
        foreach (var entry in legacy.OfType<JsonObject>())
        {
            string keys = entry["keys"]?.GetValue<string>() ?? "";
            string action = entry["action"]?.GetValue<string>() ?? "";
            int? delta = entry["delta"]?.GetValue<int>();
            bool hasValue = entry["value"] is not null;
            if (keys.Length == 0) continue;

            string? id = action switch
            {
                "balance" when hasValue => "balance-centre",
                "balance" when delta > 0 => "balance-game",
                "balance" when delta < 0 => "balance-chat",
                "volume" when delta > 0 => "volume-up",
                "volume" when delta < 0 => "volume-down",
                "mic-mute" => "mic-mute",
                "mic-level" when delta > 0 => "mic-up",
                "mic-level" when delta < 0 => "mic-down",
                _ => null,
            };

            if (id is not null) config.Bindings[id] = keys;
        }
    }
}
