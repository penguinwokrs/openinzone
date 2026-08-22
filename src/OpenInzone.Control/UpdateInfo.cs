// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json.Nodes;

namespace OpenInzone.Control;

/// <summary>
/// What checking GitHub's latest release concluded. <see cref="Available"/> false means every
/// other field is meaningless - there is nothing to download or show.
/// </summary>
public readonly record struct UpdateInfo(
    bool Available,
    Version? Version,
    string? DownloadUrl,
    long? SizeBytes,
    string? Sha256)
{
    public static UpdateInfo NoUpdate { get; } = new(false, null, null, null, null);

    /// <summary>
    /// Decides whether <paramref name="releaseJson"/> - the body GitHub returns from
    /// releases/latest - describes something newer than <paramref name="currentVersion"/> that can
    /// actually be installed. Pure and offline: the network call is the caller's job, so this, the
    /// part that decides which release is newer, is the part that gets tested.
    ///
    /// Any failure to read the response - malformed JSON, an unexpected field type, a tag that
    /// doesn't parse - comes back as no update rather than an exception. This runs at startup
    /// against an unauthenticated, best-effort endpoint; a bad response must not stop the tray
    /// from appearing.
    /// </summary>
    public static UpdateInfo CheckRelease(string releaseJson, Version currentVersion)
    {
        try
        {
            if (JsonNode.Parse(releaseJson) is not JsonObject root) return NoUpdate;

            // A release still being drafted or flagged as a prerelease is not one to offer,
            // regardless of what its version number says.
            if (root["draft"]?.GetValue<bool>() == true) return NoUpdate;
            if (root["prerelease"]?.GetValue<bool>() == true) return NoUpdate;

            string? tag = root["tag_name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(tag)) return NoUpdate;

            string versionText = tag.StartsWith('v') ? tag[1..] : tag;
            if (!System.Version.TryParse(versionText, out var released)) return NoUpdate;
            if (released <= currentVersion) return NoUpdate;

            // The version string used to name the asset is the tag's own text, not the parsed
            // Version's ToString() - a two-component tag like "v1.5" must not be made to match a
            // "1.5.0.0" that nobody published.
            if (root["assets"] is not JsonArray assets) return NoUpdate;
            string assetName = $"OpenInzone-{versionText}-setup.exe";
            JsonObject? asset = assets.OfType<JsonObject>()
                .FirstOrDefault(a => a["name"]?.GetValue<string>() == assetName);
            if (asset is null) return NoUpdate;

            string? url = asset["browser_download_url"]?.GetValue<string>();
            long? size = asset["size"]?.GetValue<long>();

            // GitHub reports "sha256:<hex>"; only the hex half is the digest. An asset can carry
            // no digest at all - it is still the installer, just not one this can verify.
            string? digest = asset["digest"]?.GetValue<string>();
            string? sha256 = digest is not null && digest.StartsWith("sha256:")
                ? digest["sha256:".Length..]
                : null;

            return new UpdateInfo(true, released, url, size, sha256);
        }
        catch (Exception)
        {
            return NoUpdate;
        }
    }
}
