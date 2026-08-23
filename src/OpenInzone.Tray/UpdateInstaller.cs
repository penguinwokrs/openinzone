// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using OpenInzone.Control;

namespace OpenInzone.Tray;

/// <summary>
/// How a verified <see cref="UpdateInfo"/> becomes the running installation: download to the
/// temporary directory, verify the digest before anything runs, then hand off to the installer and
/// get out of its way. A running executable cannot overwrite itself, so this is as far as the tray
/// goes - Inno Setup already stops the tray and relaunches it once it is done.
/// </summary>
public static class UpdateInstaller
{
    /// <summary>What checking the downloaded file against <see cref="UpdateInfo.Sha256"/> found.</summary>
    public enum DigestResult
    {
        /// <summary>Matches - safe to run.</summary>
        Verified,

        /// <summary>Does not match. The file must not be run and is not worth keeping.</summary>
        Mismatch,

        /// <summary>
        /// The release carried no digest to check against at all. Not a failure of the download,
        /// but not proof of it either - whether to proceed anyway is the caller's decision to
        /// hand to the user, not this method's to make quietly.
        /// </summary>
        Absent,
    }

    /// <summary>
    /// Downloads to the user's temporary directory under the name GitHub served it as, reporting
    /// progress as a 0-100 percentage so a ~70 MB download does not look like a frozen window.
    /// Falls back to the release's own <see cref="UpdateInfo.SizeBytes"/> when the response carries
    /// no Content-Length, so progress still moves even when the server omits it.
    /// </summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        if (update.DownloadUrl is null)
            throw new InvalidOperationException("The release has no download URL to fetch.");

        string path = Path.Combine(Path.GetTempPath(), UpdateSupport.InstallerFileName(update.DownloadUrl));

        using HttpResponseMessage response = await UpdateChecker.Http
            .GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength ?? update.SizeBytes;

        await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (FileStream destination = File.Create(path))
        {
            byte[] buffer = new byte[81920];
            long readSoFar = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                readSoFar += read;
                if (total is > 0) progress?.Report((int)(readSoFar * 100 / total.Value));
            }
        }

        return path;
    }

    /// <summary>
    /// This downloads an executable and runs it, so trusting the bytes matters more than trusting
    /// the connection they arrived over.
    /// </summary>
    public static DigestResult VerifyDigest(string path, string? expectedSha256)
    {
        if (expectedSha256 is null) return DigestResult.Absent;

        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        string actual = Convert.ToHexString(hash);

        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase)
            ? DigestResult.Verified
            : DigestResult.Mismatch;
    }

    /// <summary>
    /// The installer already stops a running tray and relaunches it once it is done, so nothing
    /// here waits for it or restarts anything - the caller's job after this is only to exit.
    /// </summary>
    public static void Run(string installerPath) =>
        Process.Start(new ProcessStartInfo(installerPath, "/SILENT /NOCANCEL") { UseShellExecute = true });
}
