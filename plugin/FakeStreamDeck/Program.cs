// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Text.Json;

namespace OpenInzone.FakeStreamDeck;

/// <summary>
/// Drives the real plugin binary through the conversation a Stream Deck would have with it.
/// </summary>
/// <remarks>
/// Written because there is no other way to exercise a dial without owning a Stream Deck +.
/// Elgato's documentation has nothing on developing without the hardware, the community emulator
/// handles keys only, and OpenDeck - which does implement encoders - reaches its devices through
/// HID and has no virtual one.
///
/// What this cannot check is how any of it looks. What it can check is everything the plugin
/// decides: that a turn moves by its ticks, that a press is a button rather than a step, and that
/// turning a dial with nothing to turn does nothing at all. Both of those last two were wrong
/// once, and neither showed up in anything but a test like this.
///
/// The headset is left as it was found: the volume is stepped up and back down, and a failure
/// part way through still puts it back.
/// </remarks>
internal static class Program
{
    private const string Volume = "com.penguinwokrs.openinzone.volume";
    private const string MicMute = "com.penguinwokrs.openinzone.micmute";
    private const string Battery = "com.penguinwokrs.openinzone.battery";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static int _failures;

    private static async Task<int> Main(string[] args)
    {
        string plugin = args.FirstOrDefault() ?? DefaultPluginPath();
        if (!File.Exists(plugin))
        {
            await Console.Error.WriteLineAsync(
                $"No plugin at {plugin}. Pass the path to openinzone-streamdeck.exe.").ConfigureAwait(false);
            return 2;
        }

        Console.WriteLine($"driving {plugin}");
        using var deck = new FakeDeck();
        await deck.StartAsync(plugin).ConfigureAwait(false);
        Console.WriteLine("registered");

        int? original = null;
        try
        {
            original = await RunAsync(deck).ConfigureAwait(false);
        }
        finally
        {
            if (original is int wanted) await RestoreAsync(deck, wanted).ConfigureAwait(false);
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "all checks passed" : $"{_failures} check(s) failed");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>Returns the volume as it was found, so it can be put back whatever happens.</summary>
    private static async Task<int?> RunAsync(FakeDeck deck)
    {
        await deck.SendAsync(WillAppear(Volume, "key-volume", encoder: false)).ConfigureAwait(false);
        var drawn = await deck.SettleAsync(Patience).ConfigureAwait(false);
        Check("a key is drawn as soon as it appears", Find(drawn, "setImage", "key-volume") is not null);

        await deck.SendAsync(WillAppear(Volume, "dial-volume", encoder: true)).ConfigureAwait(false);
        var feedback = await deck.SettleAsync(Patience).ConfigureAwait(false);
        var first = Find(feedback, "setFeedback", "dial-volume");
        Check("a dial is drawn as soon as it appears", first is not null);
        if (first is null) return null;

        var payload = first.Value.GetProperty("payload");
        Check("the dial shows a name", payload.TryGetProperty("title", out _));
        Check("the dial shows a reading", payload.TryGetProperty("value", out _));
        Check("the dial shows a bar", payload.TryGetProperty("indicator", out _));

        int? reading = Reading(payload);
        if (reading is null)
        {
            Console.WriteLine("  the headset is not answering; the rest needs a live one");
            return null;
        }

        int start = reading.Value;
        Console.WriteLine($"  volume reads {start}");

        Check("turning the dial one tick moves it one step",
            await TurnAsync(deck, "dial-volume", Volume, 1).ConfigureAwait(false) == start + 1);

        Check("turning it back returns it",
            await TurnAsync(deck, "dial-volume", Volume, -1).ConfigureAwait(false) == start);

        // Pressing a dial is a button in its own right. This was a step once, so pressing the
        // volume dial nudged the volume - which is not what pressing a dial looks like it does.
        await deck.SendAsync(DialDown(Volume, "dial-volume")).ConfigureAwait(false);
        await deck.SettleAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Check("pressing the volume dial changes nothing",
            await CurrentAsync(deck, "dial-volume").ConfigureAwait(false) == start);

        // A rotate event reaches every dial, including ones with nothing to rotate. Acting on it
        // toggled the microphone on every tick.
        await deck.SendAsync(WillAppear(MicMute, "dial-mic", encoder: true)).ConfigureAwait(false);
        var mic = await deck.SettleAsync(Patience).ConfigureAwait(false);
        string? before = Find(mic, "setFeedback", "dial-mic")?.GetProperty("payload")
            .GetProperty("value").GetString();

        await deck.SendAsync(Rotate(MicMute, "dial-mic", 3)).ConfigureAwait(false);
        await deck.SettleAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        await deck.SendAsync(WillAppear(MicMute, "dial-mic", encoder: true)).ConfigureAwait(false);
        var after = await deck.SettleAsync(Patience).ConfigureAwait(false);
        string? now = Find(after, "setFeedback", "dial-mic")?.GetProperty("payload")
            .GetProperty("value").GetString();

        Check($"turning the mute dial leaves the microphone alone ({before} -> {now})", before == now);

        // A key has to appear before it can be pressed: the plugin keeps one instance per context
        // and ignores an event for a context it has never been told about, which is right - and is
        // what this check got wrong the first time it was written.
        await deck.SendAsync(WillAppear(Battery, "key-battery", encoder: false)).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);

        await deck.SendAsync(KeyDown(Battery, "key-battery")).ConfigureAwait(false);
        var refreshed = await deck.SettleAsync(Patience).ConfigureAwait(false);
        Check("pressing the battery key redraws it", Find(refreshed, "setImage", "key-battery") is not null);

        // An event for a key nobody placed must be ignored rather than acted on.
        await deck.SendAsync(KeyDown(Battery, "key-never-appeared")).ConfigureAwait(false);
        var ignored = await deck.SettleAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Check("a press on a key that never appeared is ignored",
            Find(ignored, "setImage", "key-never-appeared") is null);

        return start;
    }

    /// <summary>Puts the volume back to what it was, however the run ended.</summary>
    private static async Task RestoreAsync(FakeDeck deck, int wanted)
    {
        int? now = await CurrentAsync(deck, "dial-volume").ConfigureAwait(false);
        if (now is null || now == wanted) return;

        Console.WriteLine($"  putting the volume back: {now} -> {wanted}");
        await TurnAsync(deck, "dial-volume", Volume, wanted - now.Value).ConfigureAwait(false);
    }

    private static async Task<int?> TurnAsync(FakeDeck deck, string context, string action, int ticks)
    {
        await deck.SendAsync(Rotate(action, context, ticks)).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);
        return await CurrentAsync(deck, context).ConfigureAwait(false);
    }

    /// <summary>Asks for a redraw and reads the number off it, which is all a deck ever sees.</summary>
    private static async Task<int?> CurrentAsync(FakeDeck deck, string context)
    {
        await deck.SendAsync(WillAppear(Volume, context, encoder: true)).ConfigureAwait(false);
        var messages = await deck.SettleAsync(Patience).ConfigureAwait(false);
        var latest = Find(messages, "setFeedback", context);
        return latest is null ? null : Reading(latest.Value.GetProperty("payload"));
    }

    /// <summary>"16 / 30" as 16. Null when the dial is showing no reading at all.</summary>
    private static int? Reading(JsonElement payload)
    {
        string? value = payload.TryGetProperty("value", out var element) ? element.GetString() : null;
        if (value is null) return null;

        string head = value.Split('/')[0].Trim();
        return int.TryParse(head, out int number) ? number : null;
    }

    private static JsonElement? Find(IReadOnlyList<JsonDocument> messages, string @event, string context)
    {
        JsonElement? found = null;
        foreach (var message in messages)
        {
            var root = message.RootElement;
            if (root.TryGetProperty("event", out var e) && e.GetString() == @event
                && root.TryGetProperty("context", out var c) && c.GetString() == context)
            {
                found = root;   // the last one wins: a redraw supersedes what came before it
            }
        }

        return found;
    }

    private static void Check(string what, bool passed)
    {
        Console.WriteLine($"  [{(passed ? "ok" : "FAILED")}] {what}");
        if (!passed) _failures++;
    }

    // Built with ordinary interpolation rather than raw strings: these end in two closing
    // braces straight after a substitution, which a raw string cannot tell apart from the end of
    // one.
    private const string Common = "\"device\":\"fake\",\"payload\":{\"settings\":{}";

    private static string WillAppear(string action, string context, bool encoder) =>
        $"{{\"event\":\"willAppear\",\"action\":\"{action}\",\"context\":\"{context}\"," +
        $"{Common},\"coordinates\":{{\"column\":0,\"row\":0}}," +
        $"\"controller\":\"{(encoder ? "Encoder" : "Keypad")}\"}}}}";

    private static string Rotate(string action, string context, int ticks) =>
        $"{{\"event\":\"dialRotate\",\"action\":\"{action}\",\"context\":\"{context}\"," +
        $"{Common},\"ticks\":{ticks},\"pressed\":false}}}}";

    private static string DialDown(string action, string context) =>
        $"{{\"event\":\"dialDown\",\"action\":\"{action}\",\"context\":\"{context}\"," +
        $"{Common},\"controller\":\"Encoder\"}}}}";

    private static string KeyDown(string action, string context) =>
        $"{{\"event\":\"keyDown\",\"action\":\"{action}\",\"context\":\"{context}\"," +
        $"{Common},\"coordinates\":{{\"column\":0,\"row\":0}}}}}}";

    /// <summary>Where plugin/build.sh leaves it, so a plain run needs no arguments.</summary>
    private static string DefaultPluginPath() => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "dist", "streamdeck", "com.penguinwokrs.openinzone.sdPlugin", "openinzone-streamdeck.exe");
}
