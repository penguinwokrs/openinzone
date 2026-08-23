// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace OpenInzone.FakeStreamDeck;

/// <summary>
/// Stands in for the Stream Deck application: launches the plugin the way it would, speaks the
/// same WebSocket protocol, and keeps what comes back so it can be checked.
/// </summary>
internal sealed class FakeDeck : IDisposable
{
    private const string PluginUuid = "fake-streamdeck-plugin";
    private const string RegisterEvent = "registerPlugin";
    private const string InspectorUuid = "fake-streamdeck-inspector";
    private const string InspectorRegisterEvent = "registerPropertyInspector";

    private readonly WebSocketChannel _channel = new();
    private readonly ConcurrentQueue<JsonDocument> _inbound = new();
    private readonly CancellationTokenSource _stopping = new();
    private Process? _plugin;
    private Task? _reader;

    /// <summary>Launches the plugin and waits for it to register, as the real application does.</summary>
    public async Task StartAsync(string pluginPath)
    {
        var start = new ProcessStartInfo(pluginPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(pluginPath))!,
        };

        // The four arguments a native plugin is started with, in the documented spelling.
        foreach (string argument in new[]
                 {
                     "-port", _channel.Port.ToString(),
                     "-pluginUUID", PluginUuid,
                     "-registerEvent", RegisterEvent,
                     "-info", """{"application":{"platform":"windows","version":"6.4.0"}}""",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        _plugin = Process.Start(start) ?? throw new InvalidOperationException("The plugin did not start.");
        await RegisterAsync(RegisterEvent, PluginUuid, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a Property Inspector instead of launching anything. The page is loaded by the
    /// Stream Deck application in its own browser and registers over the same socket the plugin
    /// uses, so standing in for the application is all that is needed to exercise it.
    /// </summary>
    public Task ListenForInspectorAsync(TimeSpan patience) =>
        RegisterAsync(InspectorRegisterEvent, InspectorUuid, patience);

    /// <summary>The port a page or a plugin is told to connect back to.</summary>
    public int Port => _channel.Port;

    /// <summary>What a Property Inspector is told to call connectElgatoStreamDeckSocket with.</summary>
    public (string Uuid, string RegisterEvent) InspectorHandshake => (InspectorUuid, InspectorRegisterEvent);

    private async Task RegisterAsync(string expectedEvent, string expectedUuid, TimeSpan patience)
    {
        using var accepting = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        accepting.CancelAfter(patience);
        await _channel.AcceptAsync(accepting.Token).ConfigureAwait(false);

        string? registration = await _channel.ReceiveAsync(accepting.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connected but did not register.");

        using var message = JsonDocument.Parse(registration);
        string? @event = message.RootElement.GetProperty("event").GetString();
        string? uuid = message.RootElement.GetProperty("uuid").GetString();

        if (@event != expectedEvent || uuid != expectedUuid)
            throw new InvalidOperationException($"Unexpected registration: {registration}");

        _reader = Task.Run(ReadLoopAsync);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                string? line = await _channel.ReceiveAsync(_stopping.Token).ConfigureAwait(false);
                if (line is null) return;
                _inbound.Enqueue(JsonDocument.Parse(line));
            }
        }
        catch (Exception)
        {
            // The plugin going away ends the run; the scenario reports what it had by then.
        }
    }

    public Task SendAsync(string json) => _channel.SendAsync(json, _stopping.Token);

    /// <summary>Everything the plugin has sent since the last time this was called.</summary>
    public IReadOnlyList<JsonDocument> Drain()
    {
        var taken = new List<JsonDocument>();
        while (_inbound.TryDequeue(out var message)) taken.Add(message);
        return taken;
    }

    /// <summary>
    /// Waits for the plugin to say something and then a moment longer, because one action can
    /// produce several messages - a redraw per visible key - and the check wants all of them.
    /// </summary>
    public async Task<IReadOnlyList<JsonDocument>> SettleAsync(TimeSpan patience)
    {
        var deadline = DateTime.UtcNow + patience;
        while (_inbound.IsEmpty && DateTime.UtcNow < deadline) await Task.Delay(25).ConfigureAwait(false);

        await Task.Delay(400).ConfigureAwait(false);
        return Drain();
    }

    public void Dispose()
    {
        _stopping.Cancel();
        try { _reader?.Wait(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        _channel.Dispose();

        try
        {
            if (_plugin is { HasExited: false }) _plugin.Kill(entireProcessTree: false);
            _plugin?.Dispose();
        }
        catch (Exception)
        {
            // Already gone.
        }

        _stopping.Dispose();
    }
}
