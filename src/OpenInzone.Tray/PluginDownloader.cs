// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using OpenInzone.Control;

namespace OpenInzone.Tray;

/// <summary>
/// Fetches the Stream Deck plugin attached to the latest release.
/// </summary>
/// <remarks>
/// Deliberately not part of the update machinery even though it reads the same release: what comes
/// back here is handed to Stream Deck rather than executed, and it is saved where the user asked
/// rather than staged and launched. Sharing that code would mean one of the two carrying the
/// other's precautions for no reason.
/// </remarks>
internal static class PluginDownloader
{
    /// <summary>
    /// Far above the six or seven megabytes the plugin actually is, and far below anything that
    /// would fill a disk. A response that keeps coming past this is not the plugin.
    /// </summary>
    private const long MaxBytes = 128L * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // Its own client: the checker's ten seconds suit a small JSON body, not a download.
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("OpenInzone", UpdateChecker.CurrentVersion.ToString()));
        return client;
    }

    public static async Task<PluginAsset> FindAsync(CancellationToken cancellation = default) =>
        PluginAsset.FromRelease(
            await UpdateChecker.FetchLatestReleaseAsync(cancellation).ConfigureAwait(false));

    /// <summary>Saves the plugin to <paramref name="path"/> and returns where it landed.</summary>
    public static async Task<string> SaveAsync(PluginAsset asset, string path,
        IProgress<int>? progress, CancellationToken cancellation = default)
    {
        if (!asset.Found || asset.DownloadUrl is null || asset.FileName is null)
            throw new InvalidOperationException("There is no plugin to download.");

        // Checked again here rather than trusted from the caller: this is the last point before a
        // URL out of an unauthenticated response is handed to an HttpClient.
        if (!UpdateInfo.IsTrustedDownloadUrl(asset.DownloadUrl))
            throw new InvalidOperationException("The download address is not one this will fetch from.");

        string? folder = Path.GetDirectoryName(path);
        if (folder is { Length: > 0 }) Directory.CreateDirectory(folder);

        using var response = await Http
            .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? expected = response.Content.Headers.ContentLength ?? asset.SizeBytes;
        long written = 0;

        // Written to a neighbouring name and moved into place, so an interrupted download does not
        // leave something that looks like a plugin sitting where the user was told one would be.
        string temporary = path + ".part";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
            await using (var target = File.Create(temporary))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > MaxBytes)
                        throw new InvalidOperationException("The download is larger than a plugin should be.");

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    if (expected is > 0) progress?.Report((int)(written * 100 / expected.Value));
                }
            }

            // A connection cut part way leaves a shorter file and no error at all, which would
            // otherwise be handed to Stream Deck as a plugin that will not open.
            if (expected is > 0 && written != expected.Value)
                throw new IOException($"The download stopped at {written} of {expected} bytes.");

            File.Move(temporary, path, overwrite: true);
            return path;
        }
        catch (Exception)
        {
            try { File.Delete(temporary); } catch { /* nothing left to do about it */ }
            throw;
        }
    }
}
