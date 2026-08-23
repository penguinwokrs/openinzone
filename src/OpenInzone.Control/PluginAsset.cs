// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json.Nodes;

namespace OpenInzone.Control;

/// <summary>
/// The Stream Deck plugin attached to a GitHub release, if there is one.
/// </summary>
/// <remarks>
/// Pure and offline, like <see cref="UpdateInfo.CheckRelease"/> and for the same reason: the
/// network half belongs to the caller, so the part that decides which asset to fetch is the part
/// that can be tested. It reuses the same allowlist - this ends up handed to an HttpClient and
/// then to Stream Deck, so it has to come from this project's own releases.
/// </remarks>
public readonly record struct PluginAsset(bool Found, string? DownloadUrl, string? FileName, long? SizeBytes)
{
    /// <summary>What the packaging tool writes, and the only extension Stream Deck installs.</summary>
    public const string Extension = ".streamDeckPlugin";

    public static PluginAsset None { get; } = new(false, null, null, null);

    /// <summary>
    /// Finds the plugin in the body GitHub returns from releases/latest. Missing, unreadable and
    /// untrusted all come back the same way: there is nothing here to fetch.
    /// </summary>
    public static PluginAsset FromRelease(string releaseJson)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(releaseJson);
        }
        catch (Exception)
        {
            return None;
        }

        if (root is not JsonObject release || release["assets"] is not JsonArray assets) return None;

        foreach (var asset in assets.OfType<JsonObject>())
        {
            string? name = asset["name"]?.GetValue<string>();
            if (name is null || !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) continue;

            string? url = asset["browser_download_url"]?.GetValue<string>();
            if (!UpdateInfo.IsTrustedDownloadUrl(url)) continue;

            long? size = asset["size"] is JsonValue value && value.TryGetValue(out long bytes) ? bytes : null;
            return new PluginAsset(true, url, name, size);
        }

        return None;
    }
}
