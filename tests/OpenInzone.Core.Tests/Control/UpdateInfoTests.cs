// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using OpenInzone.Control;

namespace OpenInzone.Tests.Control;

public class UpdateInfoTests
{
    private static readonly Version Current = new(1, 4, 0);

    // The asset GitHub attaches for a real release: every field a genuine assets[] entry carries,
    // not just the three this parser reads, so the parser is proven against noise it must ignore.
    private const string SetupAsset = """
        {
          "url": "https://api.github.com/repos/penguinwokrs/openinzone/releases/assets/987654321",
          "id": 987654321,
          "node_id": "RA_kwDOJgY2Fs4F5f1B",
          "name": "OpenInzone-1.5.0-setup.exe",
          "label": null,
          "uploader": { "login": "penguinwokrs", "id": 1, "node_id": "MDQ6VXNlcjE=", "type": "User", "site_admin": false },
          "content_type": "application/octet-stream",
          "state": "uploaded",
          "size": 5242880,
          "digest": "sha256:8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa",
          "download_count": 12,
          "created_at": "2026-08-01T12:01:00Z",
          "updated_at": "2026-08-01T12:02:00Z",
          "browser_download_url": "https://github.com/penguinwokrs/openinzone/releases/download/v1.5.0/OpenInzone-1.5.0-setup.exe"
        }
        """;

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
    private static string Release(string tagName, bool draft = false, bool prerelease = false, string assets = SetupAsset) => $$"""
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
          "assets": [{{assets}}],
          "tarball_url": "https://api.github.com/repos/penguinwokrs/openinzone/tarball/{{tagName}}",
          "zipball_url": "https://api.github.com/repos/penguinwokrs/openinzone/zipball/{{tagName}}",
          "body": "See the changelog."
        }
        """;

    [Fact]
    public void The_running_version_is_not_an_update_over_itself()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.4.0"), Current);

        Assert.False(result.Available);
    }

    [Fact]
    public void A_newer_patch_is_an_available_update()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.4.1", assets: SetupAssetFor("1.4.1")), Current);

        Assert.True(result.Available);
        Assert.Equal(new Version(1, 4, 1), result.Version);
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

        Assert.False(result.Available);
    }

    [Fact]
    public void A_tag_that_will_not_parse_as_a_version_is_not_an_update()
    {
        var result = UpdateInfo.CheckRelease(Release("nightly"), Current);

        Assert.False(result.Available);
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

        Assert.False(result.Available);
    }

    [Fact]
    public void A_draft_release_is_ignored_even_when_newer()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.5.0", draft: true), Current);

        Assert.False(result.Available);
    }

    [Fact]
    public void A_prerelease_is_ignored_even_when_newer()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.5.0", prerelease: true), Current);

        Assert.False(result.Available);
    }

    [Fact]
    public void A_newer_release_without_the_installer_asset_is_not_an_update()
    {
        var result = UpdateInfo.CheckRelease(Release("v1.5.0", assets: SourceTarballOnly), Current);

        Assert.False(result.Available);
    }

    [Fact]
    public void An_asset_with_no_digest_is_still_usable_but_reports_no_digest()
    {
        const string assetWithoutDigest = """
            {
              "url": "https://api.github.com/repos/penguinwokrs/openinzone/releases/assets/987654321",
              "id": 987654321,
              "node_id": "RA_kwDOJgY2Fs4F5f1B",
              "name": "OpenInzone-1.5.0-setup.exe",
              "label": null,
              "uploader": { "login": "penguinwokrs", "id": 1, "node_id": "MDQ6VXNlcjE=", "type": "User", "site_admin": false },
              "content_type": "application/octet-stream",
              "state": "uploaded",
              "size": 5242880,
              "download_count": 12,
              "created_at": "2026-08-01T12:01:00Z",
              "updated_at": "2026-08-01T12:02:00Z",
              "browser_download_url": "https://github.com/penguinwokrs/openinzone/releases/download/v1.5.0/OpenInzone-1.5.0-setup.exe"
            }
            """;

        var result = UpdateInfo.CheckRelease(Release("v1.5.0", assets: assetWithoutDigest), Current);

        Assert.True(result.Available);
        Assert.Null(result.Sha256);
        Assert.Equal(5242880, result.SizeBytes);
        Assert.Equal(
            "https://github.com/penguinwokrs/openinzone/releases/download/v1.5.0/OpenInzone-1.5.0-setup.exe",
            result.DownloadUrl);
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
        Assert.Equal("8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa", result.Sha256);
    }

    [Fact]
    public void Malformed_json_is_not_an_update_rather_than_an_exception()
    {
        var result = UpdateInfo.CheckRelease("{ this is not json", Current);

        Assert.False(result.Available);
    }

    [Fact]
    public void An_empty_string_is_not_an_update()
    {
        var result = UpdateInfo.CheckRelease("", Current);

        Assert.False(result.Available);
    }

    private static string SetupAssetFor(string version) => $$"""
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
          "digest": "sha256:8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa",
          "download_count": 12,
          "created_at": "2026-08-01T12:01:00Z",
          "updated_at": "2026-08-01T12:02:00Z",
          "browser_download_url": "https://github.com/penguinwokrs/openinzone/releases/download/v{{version}}/OpenInzone-{{version}}-setup.exe"
        }
        """;
}
