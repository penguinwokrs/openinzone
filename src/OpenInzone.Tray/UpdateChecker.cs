// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using OpenInzone.Control;

namespace OpenInzone.Tray;

/// <summary>
/// Fetches GitHub's latest release and hands the body to <see cref="UpdateInfo.CheckRelease"/>,
/// which is the part that actually decides whether it is newer. Everything here is the network
/// half that method deliberately has none of, so it stays untested here and tested there.
/// </summary>
public static class UpdateChecker
{
    private const string ReleasesUrl =
        "https://api.github.com/repos/penguinwokrs/openinzone/releases/latest";

    /// <summary>
    /// What this build believes it is, reduced to what a release tag can express - see
    /// <see cref="UpdateSupport.ThreeComponent"/> for why the raw assembly version cannot be
    /// compared directly.
    /// </summary>
    public static Version CurrentVersion { get; } = UpdateSupport.ThreeComponent(
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    // One instance for the process's whole lifetime: a fresh HttpClient per call would open a new
    // socket and TLS handshake for a check that runs at most once per login and once per button
    // press. internal rather than private so UpdateInstaller's download reuses it too.
    internal static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // A short timeout keeps a slow or absent network from holding the tray icon back at
        // startup - this call happens before anything else the user asked for is on screen.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // GitHub's API rejects a request with no User-Agent at all.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("OpenInzone", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>
    /// Throws on any failure - no network, a rate limit, a non-success status. The two callers
    /// disagree about what to do with that: the startup check swallows it silently, the settings
    /// window's on-demand check reports it, so neither behaviour belongs here.
    /// </summary>
    public static async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        string json = await Http.GetStringAsync(ReleasesUrl, cancellationToken).ConfigureAwait(false);
        return UpdateInfo.CheckRelease(json, CurrentVersion);
    }
}
