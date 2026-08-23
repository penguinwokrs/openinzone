// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json.Nodes;

namespace OpenInzone.Control;

/// <summary>
/// Why <see cref="UpdateInfo.Available"/> is false. The startup check ignores this and stays
/// silent either way, but someone who pressed 更新を確認 is owed the difference between "there is
/// nothing newer" and "there is something newer that cannot be installed from here".
/// </summary>
public enum UpdateUnavailableReason
{
    /// <summary>Not a negative answer at all - an update is available.</summary>
    None,

    /// <summary>Nothing published is newer than what is running.</summary>
    UpToDate,

    /// <summary>
    /// A newer release exists, but nothing in it can be installed: no asset named for the tag, or
    /// one whose download URL is missing or not somewhere this is willing to fetch from.
    /// </summary>
    NoInstaller,

    /// <summary>
    /// GitHub's answer could not be read - malformed JSON, no tag, a tag that is not a version.
    /// Says nothing about whether an update exists.
    /// </summary>
    Unreadable,
}

/// <summary>
/// What checking GitHub's latest release concluded. <see cref="Available"/> false means every
/// other field is meaningless - there is nothing to download or show - except
/// <see cref="Reason"/>, which says which kind of nothing it is.
/// </summary>
public readonly record struct UpdateInfo(
    bool Available,
    Version? Version,
    string? DownloadUrl,
    long? SizeBytes,
    string? Sha256,
    UpdateUnavailableReason Reason)
{
    public static UpdateInfo NoUpdate { get; } = new(false, null, null, null, null, UpdateUnavailableReason.UpToDate);

    private static UpdateInfo No(UpdateUnavailableReason reason) => new(false, null, null, null, null, reason);

    /// <summary>
    /// Whether a URL is one this is willing to hand to <see cref="System.Net.Http.HttpClient"/>.
    /// The response that carries it is not signed and is fetched over an unauthenticated endpoint,
    /// so the host it names is the one thing about the download that has to be checked before
    /// anything is fetched - a redirected or substituted URL would otherwise be downloaded, and
    /// then run, on GitHub's word alone. Release assets live on github.com and are served from the
    /// *.githubusercontent.com storage it redirects to; nothing else is a release of this project.
    /// </summary>
    public static bool IsTrustedDownloadUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

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
            if (JsonNode.Parse(releaseJson) is not JsonObject root)
                return No(UpdateUnavailableReason.Unreadable);

            // A release still being drafted or flagged as a prerelease is not one to offer,
            // regardless of what its version number says. Nothing installable is published, which
            // from the user's side is indistinguishable from being current.
            if (root["draft"]?.GetValue<bool>() == true) return No(UpdateUnavailableReason.UpToDate);
            if (root["prerelease"]?.GetValue<bool>() == true) return No(UpdateUnavailableReason.UpToDate);

            string? tag = root["tag_name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(tag)) return No(UpdateUnavailableReason.Unreadable);

            string versionText = tag.StartsWith('v') ? tag[1..] : tag;
            if (!System.Version.TryParse(versionText, out var released))
                return No(UpdateUnavailableReason.Unreadable);
            if (released <= currentVersion) return No(UpdateUnavailableReason.UpToDate);

            // The version string used to name the asset is the tag's own text, not the parsed
            // Version's ToString() - a two-component tag like "v1.5" must not be made to match a
            // "1.5.0.0" that nobody published.
            if (root["assets"] is not JsonArray assets) return No(UpdateUnavailableReason.NoInstaller);
            string assetName = $"OpenInzone-{versionText}-setup.exe";
            JsonObject? asset = assets.OfType<JsonObject>()
                .FirstOrDefault(a => a["name"]?.GetValue<string>() == assetName);
            if (asset is null) return No(UpdateUnavailableReason.NoInstaller);

            // Validating here rather than at the download is what makes it unbypassable: an
            // UpdateInfo reporting Available always carries a URL that passed this.
            string? url = asset["browser_download_url"]?.GetValue<string>();
            if (!IsTrustedDownloadUrl(url)) return No(UpdateUnavailableReason.NoInstaller);

            long? size = asset["size"]?.GetValue<long>();

            // GitHub reports "sha256:<hex>"; only the hex half is the digest, and only if it is
            // shaped like one - a truncated or empty value would otherwise be carried as an
            // expectation nothing can ever match, turning a missing digest into a failed check.
            // An asset can carry no digest at all - it is still the installer, just not one this
            // can verify.
            string? digest = asset["digest"]?.GetValue<string>();
            string? sha256 = digest is not null && digest.StartsWith("sha256:")
                ? digest["sha256:".Length..]
                : null;
            if (sha256 is not null && !IsSha256Hex(sha256)) sha256 = null;

            return new UpdateInfo(true, released, url, size, sha256, UpdateUnavailableReason.None);
        }
        catch (Exception)
        {
            return No(UpdateUnavailableReason.Unreadable);
        }
    }

    private static bool IsSha256Hex(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
