// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using OpenInzone.Control;

namespace OpenInzone.Tray;

/// <summary>
/// How a verified <see cref="UpdateInfo"/> becomes the running installation: download into a
/// directory of this process's own making, verify the digest before anything runs, then hand off
/// to the installer and get out of its way. A running executable cannot overwrite itself, so this
/// is as far as the tray goes - Inno Setup already stops the tray and relaunches it once it is
/// done. Because of that, a successful update's staging directory is never cleaned up by the run
/// that created it - <see cref="SweepStaleStagingDirectories"/> is what a later run does about it.
/// </summary>
public static class UpdateInstaller
{
    // Even a release that advertises no size gets a limit: a response that never ends would
    // otherwise fill whatever volume %TEMP% is on. The installer is tens of megabytes, so this is
    // far above anything genuine and far below anything painful.
    private const long MaxInstallerBytes = 256L * 1024 * 1024;

    // Long enough for a large installer over a slow line, short enough that a stalled connection
    // does not leave the button spinning for the session's lifetime.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    // Not UpdateChecker's client: its 10-second timeout covers reads from the response stream too,
    // so a ~70 MB installer would abort partway on anything slower than about 7 MB/s. Cancellation
    // here comes from a token instead, which is what a timeout on a streamed body has to be.
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("OpenInzone", UpdateChecker.CurrentVersion.ToString()));
        return client;
    }

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
    /// Makes a directory of this process's own and returns the path to download into. Two things
    /// matter here. The name is built from the version rather than from anything the response
    /// said, so neither the extension nor the directory it lands in can be chosen remotely -
    /// <see cref="Run"/> starts it through the shell, where a .bat or .lnk would be dispatched by
    /// association instead of run as the Inno Setup installer its arguments assume. And the
    /// directory is freshly created and randomly named, because %TEMP% is writable by everything
    /// running as this user: a predictable path there is one another process can have a file
    /// waiting at, or swap under us between the digest check and the launch.
    /// </summary>
    public static string CreateStagingPath(UpdateInfo update)
    {
        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"OpenInzone-{update.Version}-setup.exe");
    }

    /// <summary>
    /// Downloads to <paramref name="path"/> - from <see cref="CreateStagingPath"/>, computed by the
    /// caller so that a download which fails halfway still leaves it something to delete - reporting
    /// progress as a 0-100 percentage so a ~70 MB download does not look like a frozen window.
    /// Falls back to the release's own <see cref="UpdateInfo.SizeBytes"/> when the response carries
    /// no Content-Length, so progress still moves even when the server omits it.
    ///
    /// Returns the file open for read and write, positioned back at the start, with writers
    /// excluded - the same handle <see cref="VerifyDigest"/> hashes from and <see cref="Run"/>
    /// eventually launches by name. Opening it once here and carrying it forward is what keeps
    /// download, verification and launch looking at the same bytes throughout; the caller owns it
    /// from this point and must dispose it once the installer has been started, or hand it to
    /// <see cref="CleanUp"/>'s caller first if it gives up instead.
    /// </summary>
    public static async Task<FileStream> DownloadAsync(UpdateInfo update, string path, IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        // Belt and braces: UpdateInfo.CheckRelease never reports an update without a URL that
        // passed this, but this is the last point before the bytes are fetched.
        if (!UpdateInfo.IsTrustedDownloadUrl(update.DownloadUrl))
            throw new InvalidOperationException("The release has no download URL this will fetch from.");

        // A release that already declares more than the ceiling is refused before a single byte is
        // requested. The ceiling exists to bound an otherwise-unbounded response, not to be
        // quietly substituted for a number the release never claimed - installing a legitimate
        // installer above it is not this method's decision to make by lying about the limit.
        long? declared = update.SizeBytes is > 0 ? update.SizeBytes : null;
        if (declared > MaxInstallerBytes)
            throw new IOException(
                $"The release declares {declared} bytes, over the {MaxInstallerBytes}-byte cap this installs up to.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);
        CancellationToken token = timeout.Token;

        using HttpResponseMessage response = await Http
            .GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength ?? update.SizeBytes;
        long limit = declared ?? MaxInstallerBytes;

        // ReadWrite and FileShare.Read rather than File.Create's write-only, exclusive handle:
        // this same handle is hashed in place once the loop below ends and then held open through
        // the launch, so nothing else with read access to %TEMP% gets a window to swap the file
        // between those steps.
        var destination = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            await using (Stream source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            {
                byte[] buffer = new byte[81920];
                long readSoFar = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    readSoFar += read;
                    if (readSoFar > limit)
                        throw new IOException($"The download exceeded the {limit}-byte cap.");

                    await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    // Clamped: a server sending more than it advertised must not report 143%.
                    if (total is > 0) progress?.Report((int)Math.Min(100, readSoFar * 100 / total.Value));
                }

                // read == 0 also means "the server closed early", not just "the body is complete" -
                // without this, a truncated response with no digest to catch it hands the installer
                // a partial .exe and calls it a success.
                if (declared is not null && readSoFar != declared.Value)
                    throw new IOException($"The download stopped after {readSoFar} of the {declared} bytes declared.");
            }

            destination.Position = 0;
            return destination;
        }
        catch
        {
            await destination.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// This downloads an executable and runs it, so trusting the bytes matters more than trusting
    /// the connection they arrived over.
    ///
    /// <paramref name="file"/> is the handle <see cref="DownloadAsync"/> returned - open with
    /// writers excluded since before its first byte landed - and the caller must keep it open
    /// until <see cref="Run"/> has started the process. Hashing it in place rather than closing it
    /// and reopening the path by name is what makes that guarantee real: a name is only ever a
    /// snapshot of whatever is there when it is resolved, and on the no-digest branch a modal
    /// dialog sits open for as long as the user takes to read it, with anything else running as
    /// this user free to replace the file underneath a closed handle in the meantime. Absent gets
    /// the same handle as Verified - it is the branch that waits longest.
    /// </summary>
    public static DigestResult VerifyDigest(FileStream file, string? expectedSha256)
    {
        if (expectedSha256 is null) return DigestResult.Absent;

        file.Position = 0;
        string actual = Convert.ToHexString(SHA256.HashData(file));
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

    /// <summary>
    /// Removes the file and the directory <see cref="CreateStagingPath"/> made for it. Called on
    /// every path that does not end in the installer running, including a download that threw
    /// partway and left a fragment behind.
    /// </summary>
    public static void CleanUp(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);

            string? directory = Path.GetDirectoryName(path);
            if (directory is not null) Directory.Delete(directory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Deletes staging directories earlier runs left behind: <see cref="CleanUp"/> only ever runs
    /// on a failed or abandoned update, so a <em>successful</em> one - by design, see the class
    /// comment - leaves its ~70 MB installer in %TEMP% forever unless something later comes back
    /// for it. Meant to be called once at startup, before anything else has a reason to care.
    ///
    /// <see cref="CreateStagingPath"/> names its directory with <see cref="Path.GetRandomFileName"/>,
    /// which carries no prefix of this project's to search for - the file inside, not the
    /// directory's name, is what marks a directory as ours. So this only ever touches a directory
    /// holding exactly one entry named like the installer <see cref="CreateStagingPath"/> creates,
    /// and leaves everything else in %TEMP% - including a directory some other process is using at
    /// this exact moment - alone.
    ///
    /// Never throws and never reports: this runs ahead of the tray icon appearing, and a locked
    /// file (a scan in progress, a directory another process happens to be touching right now) is
    /// simply left for the next startup to try again, not worth a delay or a balloon over.
    /// </summary>
    public static void SweepStaleStagingDirectories()
    {
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(Path.GetTempPath()))
            {
                try
                {
                    string[] entries = Directory.GetFileSystemEntries(directory);
                    if (entries.Length != 1) continue;

                    string name = Path.GetFileName(entries[0]);
                    if (!name.StartsWith("OpenInzone-", StringComparison.OrdinalIgnoreCase)
                        || !name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    File.Delete(entries[0]);
                    Directory.Delete(directory);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
