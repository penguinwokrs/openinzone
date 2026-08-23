// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

using System.Globalization;
using OpenInzone;
using OpenInzone.Cli.Output;
using OpenInzone.Cli.Session;
using OpenInzone.Model;
using OpenInzone.Protocol;

namespace OpenInzone.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        bool json = Take(ref args, "--json");
        bool raw = Take(ref args, "--raw");

        IReportRenderer renderer = json
            ? new JsonRenderer(Console.Out, raw)
            : new TextRenderer(Console.Out, Console.Error, raw);

        if (args.Length == 0)
        {
            return NoArguments(renderer, json);
        }

        if (args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return Run(args, renderer);
        }
        catch (TimeoutException)
        {
            renderer.Render(new ErrorReport("unreachable",
                "The earbuds did not answer. They are in the case, out of range, or off."));
            return 1;
        }
        catch (ArgumentException ex)
        {
            renderer.Render(new ErrorReport("usage", ex.Message));
            return 2;
        }
        catch (Exception ex)
        {
            renderer.Render(new ErrorReport("error", ex.Message));
            return 1;
        }
    }

    /// <summary>
    /// Removes a flag wherever it appears. Matching the whole token is what stops
    /// `inzone volume -1` from being read as an option.
    /// </summary>
    private static bool Take(ref string[] args, string flag)
    {
        if (!args.Contains(flag)) return false;
        args = args.Where(a => a != flag).ToArray();
        return true;
    }

    private static int Run(string[] args, IReportRenderer renderer)
    {
        string command = args[0].ToLowerInvariant();
        string[] rest = args[1..];

        return command switch
        {
            "devices" => ListDevices(renderer),
            "status" => WithSession(session => Show(renderer, BuildStatus(session))),
            "balance" => WithSession(session => Show(renderer, new BalanceReport(Balance(session, rest)))),
            "volume" or "vol" => WithSession(session => Show(renderer, new VolumeReport(Volume(session, rest)))),
            "mic" => WithSession(session => Mic(session, rest, renderer)),
            "battery" => WithSession(session => Show(renderer, new BatteryReport(session.GetBattery()))),
            "watch" => WithDevice(device => Watch(device, rest, renderer)),
            _ => Unknown(command, renderer),
        };
    }

    /// <summary>
    /// Runs a command against whoever is holding the headset. Keeping this out of
    /// <see cref="Run"/> is what lets an unknown command be rejected on a machine with nothing
    /// plugged in.
    /// </summary>
    /// <remarks>
    /// A running daemon is asked rather than talked over. Replies are matched on a transaction
    /// number each process counts from one, so two conversations on the same dongle can claim each
    /// other's answers - which is a real possibility whenever the tray or a Stream Deck is up. With
    /// no daemon there is nobody to collide with, and opening the device is both safe and quicker
    /// than starting one to ask a single question.
    /// </remarks>
    private static int WithSession(Func<IHeadsetSession, int> action)
    {
        using var session = DaemonSession.TryConnect() ?? DirectSession.Open();
        return action(session);
    }

    /// <summary>
    /// Opens the dongle directly, for `watch`. A listener issues no requests of its own after the
    /// first, so it has no transactions to collide with anyone else's - and the channel carries
    /// whole snapshots rather than the individual notifications this command exists to show.
    /// </summary>
    private static int WithDevice(Func<InzoneDevice, int> action)
    {
        using var device = InzoneDevice.Open();
        return action(device);
    }

    private static int Show(IReportRenderer renderer, IReport report)
    {
        renderer.Render(report);
        return 0;
    }

    private static int Unknown(string command, IReportRenderer renderer)
    {
        renderer.Render(new ErrorReport("usage",
            $"Unknown command '{command}'. Run 'inzone --help' to see what is available."));
        return 2;
    }

    /// <summary>
    /// No command is a usage failure, not a device failure: exit 2, not 1. Under `--json` the
    /// error path stays well-formed JSON rather than falling back to the text usage block.
    /// </summary>
    internal static int NoArguments(IReportRenderer renderer, bool json)
    {
        if (json)
        {
            renderer.Render(new ErrorReport("usage",
                "No command given. Run 'inzone --help' to see what is available."));
        }
        else
        {
            PrintUsage();
        }

        return 2;
    }

    private static int ListDevices(IReportRenderer renderer)
    {
        var devices = InzoneDevice.Enumerate();
        if (devices.Count == 0)
        {
            renderer.Render(new ErrorReport("no-device", "No INZONE control interface found."));
            return 1;
        }

        renderer.Render(new DeviceListReport(devices));
        return 0;
    }

    private static StatusReport BuildStatus(IHeadsetSession session) => new(
        session.GetModelInfo(),
        session.GetBattery(),
        session.GetMixBalance(),
        session.GetHeadphoneVolume(),
        session.GetMicVolume(),
        session.GetMicLevel(),
        session.GetSidetoneVolume());

    private static MixBalance Balance(IHeadsetSession session, string[] args)
    {
        if (args.Length == 0) return session.GetMixBalance();

        string arg = args[0];
        if (arg is "centre" or "center") return session.SetMixBalance(MixBalance.Centre);

        var (value, relative) = ParseAmount(arg, "balance");
        return relative ? session.AdjustMixBalance(value) : session.SetMixBalance(value);
    }

    private static HeadphoneVolume Volume(IHeadsetSession session, string[] args)
    {
        if (args.Length == 0) return session.GetHeadphoneVolume();

        switch (args[0].ToLowerInvariant())
        {
            case "mute": return session.SetHeadphoneMuted(true);
            case "unmute": return session.SetHeadphoneMuted(false);
            case "toggle": return session.ToggleHeadphoneMute();
        }

        var (value, relative) = ParseAmount(args[0], "volume");
        return relative ? session.AdjustHeadphoneVolume(value) : session.SetHeadphoneVolume(value);
    }

    private static int Mic(IHeadsetSession session, string[] args, IReportRenderer renderer)
    {
        if (args.Length == 0) return Show(renderer, MicNow(session));

        // Each of these reports only what it changed, which is what the command has always
        // printed. Muting is a headset flag; the level is a Windows setting. Reporting both
        // together belongs to `inzone mic` with no arguments, not to these.
        switch (args[0].ToLowerInvariant())
        {
            case "mute": return Show(renderer, new MicMuteReport(session.SetMicMuted(true)));
            case "unmute": return Show(renderer, new MicMuteReport(session.SetMicMuted(false)));
            case "toggle": return Show(renderer, new MicMuteReport(session.ToggleMicMute()));
        }

        var (value, relative) = ParseAmount(args[0], "microphone level");
        int level = relative ? session.AdjustMicLevel(value) : session.SetMicLevel(value);
        return Show(renderer, new MicLevelReport(level));
    }

    private static MicReport MicNow(IHeadsetSession session)
        => new(session.GetMicVolume(), session.GetMicLevel());

    private static int Watch(InzoneDevice device, string[] args, IReportRenderer renderer)
    {
        if (!WatchFilter.TryParse(args, out var wanted, out string? error))
        {
            renderer.Render(new ErrorReport("usage", error!));
            return 2;
        }

        // Not a report: under --json every line on stdout has to be one object, and a banner is not.
        Console.Error.WriteLine($"Watching {device.GetModelInfo().Name}. Press Ctrl+C to stop.");

        using var stop = new ManualResetEventSlim(false);

        device.SettingChanged += (_, e) =>
        {
            if (wanted.Count > 0 && !wanted.Contains(e.EventId)) return;
            renderer.Render(new EventReport(
                DateTime.Now, e.EventId, Payload(e.EventId, e.Param), HexFormat.Bytes(e.Param)));
        };

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
        stop.Wait();
        return 0;
    }

    /// <summary>
    /// The report a notification carries, so both renderers can draw it the way they draw the
    /// matching command's output. Null for an event with no decoder; the caller keeps the raw
    /// bytes for that case.
    /// </summary>
    internal static IReport? Payload(EventId eventId, byte[] param) => eventId switch
    {
        EventId.GameChatMixBalance when param.Length >= 1 => new BalanceReport(new MixBalance(param[0])),
        EventId.HeadphoneVolume when param.Length >= 3 => new VolumeReport(HeadphoneVolume.Parse(param)),
        EventId.MicVolume when param.Length >= 3 => new MicMuteReport(MicVolume.Parse(param)),
        EventId.BatteryInfo when param.Length >= 2 => new BatteryReport(BatteryInfo.Parse(param)),
        EventId.SidetoneVolume when param.Length >= 2 => new SidetoneReport(SidetoneVolume.Parse(param)),
        _ => null,
    };

    /// <summary>Reads "70" as absolute and "+10" / "-10" as relative.</summary>
    private static (int Value, bool Relative) ParseAmount(string text, string what)
    {
        bool relative = text.StartsWith('+') || text.StartsWith('-');
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            throw new ArgumentException($"'{text}' is not a {what} amount. Use a number like 70, or +10 / -10 to adjust.");
        return (value, relative);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            inzone - control an INZONE headset over its USB dongle

            Usage:
              inzone status                 Show everything at once
              inzone devices                List the control interfaces found

              inzone balance                Show the game/chat balance
              inzone balance 70             Set it (0 = all chat, 100 = all game)
              inzone balance +10 | -10      Move it by a step
              inzone balance centre         Back to the middle

              inzone volume                 Show the headphone volume
              inzone volume 20              Set it (0-30)
              inzone volume +1 | -1         Move it by a step
              inzone volume mute | unmute | toggle

              inzone mic                    Show the microphone state
              inzone mic mute | unmute | toggle
              inzone mic 50                 Set the level (0-100)
              inzone mic +5 | -5            Move the level by a step

              inzone battery                Show charge levels
              inzone watch                  Print changes as they happen, including
                                            ones made from the earbuds themselves
              inzone watch battery          Print changes to one event only
                                            (battery, balance, volume, mic, sidetone)

            Flags:
              --json                        Any command, as one JSON object
                                            (watch emits one object per line)
              --raw                         Add the undecoded bytes to battery output
            """);
    }
}
