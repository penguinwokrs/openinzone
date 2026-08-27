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
    private const string VolumeUp = "com.penguinwokrs.openinzone.volumeup";
    private const string VolumeDown = "com.penguinwokrs.openinzone.volumedown";
    private const string MicMute = "com.penguinwokrs.openinzone.micmute";
    private const string Battery = "com.penguinwokrs.openinzone.battery";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static int _failures;

    private static async Task<int> Main(string[] args)
    {
        if (args.FirstOrDefault() == "--faces")
            return await FacesAsync(args.Skip(1).FirstOrDefault(), args.Skip(2).FirstOrDefault()).ConfigureAwait(false);

        if (args.FirstOrDefault() == "--property-inspector")
            return await InspectAsync(args.Skip(1).FirstOrDefault()).ConfigureAwait(false);

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

        // The step a key was configured with, including its sign: two keys with opposite steps
        // are how a deck gets an up and a down, and nothing else here would notice if the sign
        // were dropped or the configured size ignored in favour of the default.
        await deck.SendAsync(WillAppear(Volume, "key-step", encoder: false)).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);

        await deck.SendAsync(Settings(Volume, "key-step", 2)).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);
        await deck.SendAsync(KeyDown(Volume, "key-step")).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);
        Check("a key moves by the step it was given",
            await CurrentAsync(deck, "dial-volume").ConfigureAwait(false) == start + 2);

        await deck.SendAsync(Settings(Volume, "key-step", -2)).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);
        await deck.SendAsync(KeyDown(Volume, "key-step")).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);
        Check("a negative step makes a key that turns it down",
            await CurrentAsync(deck, "dial-volume").ConfigureAwait(false) == start);

        // A key that has gone must stop being drawn to and stop acting.
        await deck.SendAsync(WillAppear(Battery, "key-gone", encoder: false)).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);
        await deck.SendAsync(WillDisappear(Battery, "key-gone")).ConfigureAwait(false);
        await deck.SettleAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        await deck.SendAsync(KeyDown(Battery, "key-gone")).ConfigureAwait(false);
        var afterGone = await deck.SettleAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        Check("a key that has disappeared is no longer drawn to",
            Find(afterGone, "setImage", "key-gone") is null);

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

        await DirectedKeysAsync(deck, start).ConfigureAwait(false);

        return start;
    }

    /// <summary>
    /// A directed key: a picture until it is pressed, the reading for a moment after, and the
    /// picture again. The pair is exercised up then down, so the headset is left as it was found.
    /// </summary>
    private static async Task DirectedKeysAsync(FakeDeck deck, int start)
    {
        await deck.SendAsync(WillAppear(VolumeUp, "key-volumeup", encoder: false)).ConfigureAwait(false);
        var appeared = await deck.SettleAsync(Patience).ConfigureAwait(false);

        // A directed key wears the picture the manifest gives it, so appearing must leave it as
        // that picture: a setImage carrying no image, or none at all.
        var drawn = Find(appeared, "setImage", "key-volumeup");
        Check("a directed key appears as its own picture",
            drawn is null || !drawn.Value.GetProperty("payload").TryGetProperty("image", out _));

        await deck.SendAsync(KeyDown(VolumeUp, "key-volumeup")).ConfigureAwait(false);
        var pressed = await deck.SettleAsync(Patience).ConfigureAwait(false);

        var reading = Find(pressed, "setImage", "key-volumeup");
        Check("pressing a directed key shows a reading",
            reading is not null && reading.Value.GetProperty("payload").TryGetProperty("image", out _));

        // Three messages land on this context after a press: the reading drawn at once, the
        // reading redrawn when the tray's snapshot arrives with what the headset settled on,
        // and the clear that ends the moment 1.5 s later. Waiting the moment out and then
        // settling collects all of them, and Find answers with the last - which is the clear.
        await Task.Delay(TimeSpan.FromSeconds(2.5)).ConfigureAwait(false);
        var settled = await deck.SettleAsync(Patience).ConfigureAwait(false);
        var cleared = Find(settled, "setImage", "key-volumeup");
        Check("the reading goes away and the picture comes back",
            cleared is not null && !cleared.Value.GetProperty("payload").TryGetProperty("image", out _));

        Check("a directed key moves the volume by one",
            await CurrentAsync(deck, "dial-volume").ConfigureAwait(false) == start + 1);

        await deck.SendAsync(WillAppear(VolumeDown, "key-volumedown", encoder: false)).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);
        await deck.SendAsync(KeyDown(VolumeDown, "key-volumedown")).ConfigureAwait(false);
        await deck.SettleAsync(Patience).ConfigureAwait(false);

        Check("the other key of the pair puts it back",
            await CurrentAsync(deck, "dial-volume").ConfigureAwait(false) == start);

        await deck.SendAsync(WillDisappear(VolumeUp, "key-volumeup")).ConfigureAwait(false);
        await deck.SendAsync(WillDisappear(VolumeDown, "key-volumedown")).ConfigureAwait(false);
        await deck.SettleAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes out the key faces the plugin draws from whatever the headset is doing right now, so
    /// they can be looked at. The tests draw them from made-up states; this is the real thing, and
    /// a face is only right if someone has seen it at the size it will be used.
    /// </summary>
    private static async Task<int> FacesAsync(string? pluginPath, string? outputDirectory)
    {
        string plugin = pluginPath ?? DefaultPluginPath();
        string directory = outputDirectory ?? Path.Combine(Environment.CurrentDirectory, "faces");
        Directory.CreateDirectory(directory);

        using var deck = new FakeDeck();
        await deck.StartAsync(plugin).ConfigureAwait(false);

        foreach (string action in new[] { Volume, "com.penguinwokrs.openinzone.balance", MicMute,
                                          "com.penguinwokrs.openinzone.miclevel", Battery })
        {
            string name = action.Split('.')[^1];
            await deck.SendAsync(WillAppear(action, $"key-{name}", encoder: false)).ConfigureAwait(false);
            var drawn = await deck.SettleAsync(Patience).ConfigureAwait(false);

            var image = Find(drawn, "setImage", $"key-{name}");
            if (image is null)
            {
                Console.WriteLine($"  {name}: nothing drawn");
                continue;
            }

            string uri = image.Value.GetProperty("payload").GetProperty("image").GetString()!;
            const string prefix = "data:image/svg+xml;base64,";
            if (!uri.StartsWith(prefix, StringComparison.Ordinal))
            {
                Console.WriteLine($"  {name}: not an SVG data URI");
                _failures++;
                continue;
            }

            string path = Path.Combine(directory, $"{name}.svg");
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(uri[prefix.Length..]))
                .ConfigureAwait(false);
            Console.WriteLine($"  {name}: {path}");
        }

        return _failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Stands in for the application while a Property Inspector page is loaded by hand in a
    /// browser, and reports what the page sends. The page is ordinary HTML talking over the same
    /// socket, so this is the whole of what the application does for it.
    /// </summary>
    private static async Task<int> InspectAsync(string? action)
    {
        using var deck = new FakeDeck();
        var (uuid, register) = deck.InspectorHandshake;

        Console.WriteLine("Open the Property Inspector page in a browser and run:");
        Console.WriteLine($"  connectElgatoStreamDeckSocket({deck.Port}, \"{uuid}\", \"{register}\", " +
                          "\"{}\", JSON.stringify({action: \"" + (action ?? Volume) +
                          "\", context: \"pi\", payload: {settings: {}}}))");
        Console.WriteLine("waiting...");

        await deck.ListenForInspectorAsync(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        Console.WriteLine("the page registered");

        var messages = await deck.SettleAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        foreach (var message in messages) Console.WriteLine($"  <- {message.RootElement}");

        var settings = messages.FirstOrDefault(m =>
            m.RootElement.TryGetProperty("event", out var e) && e.GetString() == "setSettings");

        Check("the page sends setSettings", settings is not null);
        if (settings is not null)
        {
            Check("the settings carry a step",
                settings.RootElement.GetProperty("payload").TryGetProperty("step", out _));
        }

        Console.WriteLine(_failures == 0 ? "all checks passed" : $"{_failures} check(s) failed");
        return _failures == 0 ? 0 : 1;
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

    private static string Settings(string action, string context, int step) =>
        $"{{\"event\":\"didReceiveSettings\",\"action\":\"{action}\",\"context\":\"{context}\"," +
        $"\"device\":\"fake\",\"payload\":{{\"settings\":{{\"step\":{step}}}," +
        $"\"coordinates\":{{\"column\":0,\"row\":0}}}}}}";

    private static string WillDisappear(string action, string context) =>
        $"{{\"event\":\"willDisappear\",\"action\":\"{action}\",\"context\":\"{context}\"," +
        $"{Common},\"coordinates\":{{\"column\":0,\"row\":0}}}}}}";

    private static string KeyDown(string action, string context) =>
        $"{{\"event\":\"keyDown\",\"action\":\"{action}\",\"context\":\"{context}\"," +
        $"{Common},\"coordinates\":{{\"column\":0,\"row\":0}}}}}}";

    /// <summary>Where plugin/build.sh leaves it, so a plain run needs no arguments.</summary>
    private static string DefaultPluginPath() => Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "dist", "streamdeck", "com.penguinwokrs.openinzone.sdPlugin", "openinzone-streamdeck.exe");
}
