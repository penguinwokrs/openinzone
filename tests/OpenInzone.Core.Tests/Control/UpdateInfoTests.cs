// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

public class UpdateInfoTests
{
    private static readonly Version Current = new(1, 4, 0);

    // 64 hex characters, the shape a real sha256 has - the fixture this replaced was 63 long and
    // so proved nothing about the parser it was fed to.
    private const string Digest = "8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa5";

    // How GitHub reports it on the asset itself.
    private const string DigestField = "sha256:" + Digest;

    // The asset GitHub attaches for a real release: every field a genuine assets[] entry carries,
    // not just the three this parser reads, so the parser is proven against noise it must ignore.
    private static readonly string SetupAsset = SetupAssetFor("1.5.0");

    private const string SourceTarballOnly = """
        {
          "url": "https://api.github.com/repos/penguinwokrs/openinzone/releases/assets/111111111",
          "id": 111111111,
          "node_id": "RA_kwDOJgY2Fs4F5f1C",
          "name": "Source code",
          "label": null,
          "uploader": { "login": "penguinwokrs", "id": 1, "node_id": "MDQ6VXNlcjE=", "type": "User", "site_admin": false },
          "content_type": "application/zip",
          "state": "uploaded",
          "size": 1024,
          "digest": null,
          "download_count": 1,
          "created_at": "2026-08-01T12:01:00Z",
          "updated_at": "2026-08-01T12:02:00Z",
          "browser_download_url": "https://github.com/penguinwokrs/openinzone/archive/refs/tags/v1.5.0.zip"
        }
        """;

    // Trimmed to the fields this test file varies, but with the same nesting and the surrounding
    // noise (author, tarball_url, body, ...) a real releases/latest response carries, so a parser
    // that assumes a minimal shape would fail here the way it would against the real API.
    private static string Release(string tagName, bool draft = false, bool prerelease = false, string? assets = null) => $$"""
        {
          "url": "https://api.github.com/repos/penguinwokrs/openinzone/releases/123456789",
          "html_url": "https://github.com/penguinwokrs/openinzone/releases/tag/{{tagName}}",
          "id": 123456789,
          "node_id": "RE_kwDOJgY2Fs4HYbCB",
          "tag_name": "{{tagName}}",
          "target_commitish": "main",
          "name": "{{tagName}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "created_at": "2026-08-01T12:00:00Z",
          "published_at": "2026-08-01T12:05:00Z",
          "author": {
            "login": "penguinwokrs",
            "id": 1,
            "node_id": "MDQ6VXNlcjE=",
            "avatar_url": "https://avatars.githubusercontent.com/u/1?v=4",
            "type": "User",
            "site_admin": false
          },
          "assets": [{{assets ?? SetupAsset}}],
          "tarball_url": "https://api.github.com/repos/penguinwokrs/openinzone/tarball/{{tagName}}",
          "zipball_url": "https://api.github.com/repos/penguinwokrs/openinzone/zipball/{{tagName}}",
          "body": "See the changelog."
        }
        """;

    [Fact]
    public void The_running_version_is_not_an_update_over_itself()
    {
        // The asset is named for the tag under test on purpose: with a mismatched one the asset
        // lookup fails first and this passes without the version comparison ever running, which
        // is what let the equal-version case go unguarded.
        var result = UpdateInfo.CheckRelease(Release("v1.4.0", assets: SetupAssetFor("1.4.0")), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.UpToDate);
    }

    [Fact]
    public void A_newer_patch_is_an_available_update()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.4.1", assets: SetupAssetFor("1.4.1")), Current);

        Assert.True(result.Available);
        Assert.Equal(new Version(1, 4, 1), result.Version);
        Assert.Equal(UpdateUnavailableReason.None, result.Reason);
    }

    [Fact]
    public void A_newer_minor_is_an_available_update()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.5.0"), Current);

        Assert.True(result.Available);
        Assert.Equal(new Version(1, 5, 0), result.Version);
    }

    [Fact]
    public void A_newer_major_is_an_available_update()
    {
        var result = UpdateInfo.CheckRelease(Release("v2.0.0", assets: SetupAssetFor("2.0.0")), Current);

        Assert.True(result.Available);
        Assert.Equal(new Version(2, 0, 0), result.Version);
    }

    [Fact]
    public void An_older_release_than_the_one_running_is_not_an_update()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.3.9", assets: SetupAssetFor("1.3.9")), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.UpToDate);
    }

    [Fact]
    public void A_two_component_tag_matches_the_asset_named_the_same_way()
    {
        // The tag's own text names the asset, so "v1.5" looks for OpenInzone-1.5-setup.exe and not
        // for the "1.5.0.0" that Version.ToString() would have produced.
        var result = UpdateInfo.CheckRelease(Release("v1.5", assets: SetupAssetFor("1.5")), Current);

        Assert.True(result.Available);
        Assert.Equal(new Version(1, 5), result.Version);
    }

    [Fact]
    public void A_two_component_tag_does_not_match_a_three_component_asset_name()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.5", assets: SetupAssetFor("1.5.0")), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.NoInstaller);
    }

    [Fact]
    public void A_tag_with_no_v_prefix_is_read_the_same_way()
    {
        var result = UpdateInfo.CheckRelease(Release("1.5.0"), Current);

        Assert.True(result.Available);
        Assert.Equal(new Version(1, 5, 0), result.Version);
    }

    [Fact]
    public void An_installer_asset_named_for_another_version_is_not_this_releases()
    {
        // A release that ships last version's installer by mistake is not an update to offer:
        // matching any *-setup.exe would download 1.4.9 while claiming 1.5.0.
        var result = UpdateInfo.CheckRelease(Release("v1.5.0", assets: SetupAssetFor("1.4.9")), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.NoInstaller);
    }

    [Fact]
    public void A_tag_that_will_not_parse_as_a_version_is_not_an_update()
    {
        var result = UpdateInfo.CheckRelease(Release("nightly"), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.Unreadable);
    }

    [Fact]
    public void A_release_with_no_tag_name_is_not_an_update()
    {
        const string json = """
            {
              "url": "https://api.github.com/repos/penguinwokrs/openinzone/releases/123456789",
              "id": 123456789,
              "draft": false,
              "prerelease": false,
              "assets": [],
              "body": "See the changelog."
            }
            """;

        var result = UpdateInfo.CheckRelease(json, Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.Unreadable);
    }

    [Fact]
    public void A_draft_release_is_ignored_even_when_newer()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.5.0", draft: true), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.UpToDate);
    }

    [Fact]
    public void A_prerelease_is_ignored_even_when_newer()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.5.0", prerelease: true), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.UpToDate);
    }

    [Fact]
    public void A_newer_release_without_the_installer_asset_is_not_an_update()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.5.0", assets: SourceTarballOnly), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.NoInstaller);
    }

    [Fact]
    public void An_asset_with_no_digest_is_still_usable_but_reports_no_digest()
    {
        var result = UpdateInfo.CheckRelease(
            Release("v1.5.0", assets: SetupAssetFor("1.5.0", digest: null)), Current);

        Assert.True(result.Available);
        Assert.Null(result.Sha256);
        Assert.Equal(5242880, result.SizeBytes);
        Assert.Equal(
            "https://github.com/penguinwokrs/openinzone/releases/download/v1.5.0/OpenInzone-1.5.0-setup.exe",
            result.DownloadUrl);
    }

    [Theory]
    [InlineData("sha256:")]                                                                     // the prefix and nothing else
    [InlineData("sha256:8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa")]      // 63, truncated
    [InlineData("sha256:8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa55")]    // 65
    [InlineData("sha256:not-hex-at-all-not-hex-at-all-not-hex-at-all-not-hex-at-all-notxx")]
    [InlineData("md5:d41d8cd98f00b204e9800998ecf8427e")]
    public void A_digest_that_is_not_a_sha256_is_no_digest_rather_than_one_nothing_can_match(string digest)
    {
        var result = UpdateInfo.CheckRelease(
            Release("v1.5.0", assets: SetupAssetFor("1.5.0", digest: digest)), Current);

        Assert.True(result.Available);
        Assert.Null(result.Sha256);
    }

    [Fact]
    public void The_installer_asset_carries_its_download_url_size_and_digest()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.5.0"), Current);

        Assert.True(result.Available);
        Assert.Equal(
            "https://github.com/penguinwokrs/openinzone/releases/download/v1.5.0/OpenInzone-1.5.0-setup.exe",
            result.DownloadUrl);
        Assert.Equal(5242880, result.SizeBytes);
        Assert.Equal(Digest, result.Sha256);
    }

    [Fact]
    public void An_asset_with_no_download_url_is_not_an_update_to_offer()
    {
        // The balloon would otherwise advertise an update whose button can only throw.
        var result = UpdateInfo.CheckRelease(
            Release("v1.5.0", assets: SetupAssetFor("1.5.0", url: null)), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.NoInstaller);
    }

    [Theory]
    [InlineData("http://github.com/penguinwokrs/openinzone/releases/download/v1.5.0/OpenInzone-1.5.0-setup.exe")]
    [InlineData("https://evil.example/OpenInzone-1.5.0-setup.exe")]
    [InlineData("https://github.com.evil.example/OpenInzone-1.5.0-setup.exe")]
    [InlineData("https://evil.example/github.com/OpenInzone-1.5.0-setup.exe")]
    [InlineData("https://notgithubusercontent.com/OpenInzone-1.5.0-setup.exe")]
    [InlineData("file:///C:/OpenInzone-1.5.0-setup.exe")]
    [InlineData("not a url at all")]
    public void A_download_url_that_is_not_githubs_is_not_an_update(string url)
    {
        var result = UpdateInfo.CheckRelease(
            Release("v1.5.0", assets: SetupAssetFor("1.5.0", url: url)), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.NoInstaller);
    }

    [Fact]
    public void The_storage_host_github_redirects_asset_downloads_to_is_rejected()
    {
        // objects.githubusercontent.com is not this project's to claim - it serves release assets
        // for every public GitHub repository, so naming it in the allowlist would accept an
        // installer from any of them. The redirect onto it is deliberately left unchecked instead;
        // see the reasoning on IsTrustedDownloadUrl.
        const string url = "https://objects.githubusercontent.com/github-production-release-asset/1/2";

        var result = UpdateInfo.CheckRelease(
            Release("v1.5.0", assets: SetupAssetFor("1.5.0", url: url)), Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.NoInstaller);
    }

    [Theory]
    [InlineData("https://github.com/penguinwokrs/openinzone/releases/download/v1.5.0/x.exe", true)]
    [InlineData("https://objects.githubusercontent.com/x", false)]
    [InlineData("http://github.com/x", false)]
    [InlineData("https://githubusercontent.com.evil.example/x", false)]
    [InlineData("https://evil.example/x", false)]
    // The right host, but someone else's repository - exactly what an allowlist keyed on host
    // alone would have let through.
    [InlineData("https://github.com/attacker/repo/releases/download/v1/x.exe", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void A_download_url_is_trusted_only_over_https_from_github(string? url, bool trusted)
    {
        Assert.Equal(trusted, UpdateInfo.IsTrustedDownloadUrl(url));
    }

    [Fact]
    public void Malformed_json_is_not_an_update_rather_than_an_exception()
    {
        var result = UpdateInfo.CheckRelease("{ this is not json", Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.Unreadable);
    }

    [Fact]
    public void An_empty_string_is_not_an_update()
    {
        var result = UpdateInfo.CheckRelease("", Current);

        AssertNothingToInstall(result, UpdateUnavailableReason.Unreadable);
    }

    /// <summary>
    /// Available false means the other fields are meaningless, so a negative answer has to actually
    /// leave them empty - a caller reading DownloadUrl without checking Available first would
    /// otherwise be handed something to fetch.
    /// </summary>
    private static void AssertNothingToInstall(UpdateInfo result, UpdateUnavailableReason reason)
    {
        Assert.False(result.Available);
        Assert.Equal(reason, result.Reason);
        Assert.Null(result.Version);
        Assert.Null(result.DownloadUrl);
        Assert.Null(result.SizeBytes);
        Assert.Null(result.Sha256);
    }

    // Distinguishes "this test does not care about the URL" from "this asset carries no URL",
    // which null has to mean if the missing-URL case is to be expressible at all.
    private const string RealUrl = "(the url GitHub would serve this asset from)";

    private static string SetupAssetFor(string version, string? digest = DigestField, string? url = RealUrl)
    {
        string downloadUrl = url == RealUrl
            ? $"https://github.com/penguinwokrs/openinzone/releases/download/v{version}/OpenInzone-{version}-setup.exe"
            : url!;

        return $$"""
            {
              "url": "https://api.github.com/repos/penguinwokrs/openinzone/releases/assets/987654321",
              "id": 987654321,
              "node_id": "RA_kwDOJgY2Fs4F5f1B",
              "name": "OpenInzone-{{version}}-setup.exe",
              "label": null,
              "uploader": { "login": "penguinwokrs", "id": 1, "node_id": "MDQ6VXNlcjE=", "type": "User", "site_admin": false },
              "content_type": "application/octet-stream",
              "state": "uploaded",
              "size": 5242880,
              "digest": {{(digest is null ? "null" : $"\"{digest}\"")}},
              "download_count": 12,
              "created_at": "2026-08-01T12:01:00Z",
              "updated_at": "2026-08-01T12:02:00Z",
              "browser_download_url": {{(url is null ? "null" : $"\"{downloadUrl}\"")}}
            }
            """;
    }
}
