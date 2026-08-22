using System.Globalization;
using InzoneBuds;
using InzoneBuds.Model;
using InzoneBuds.Protocol;

namespace InzoneBuds.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        string command = args[0].ToLowerInvariant();
        string[] rest = args[1..];

        if (command == "devices") return ListDevices();

        using var device = InzoneBudsDevice.Open();

        return command switch
        {
            "status" => ShowStatus(device),
            "balance" => Balance(device, rest),
            "volume" or "vol" => Volume(device, rest),
            "mic" => Mic(device, rest),
            "battery" => Print(device.GetBattery().ToString()),
            "watch" => Watch(device),
            _ => Unknown(command),
        };
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Run 'inzone --help' to see what is available.");
        return 1;
    }

    private static int Print(string text)
    {
        Console.WriteLine(text);
        return 0;
    }

    private static int ListDevices()
    {
        var devices = InzoneBudsDevice.Enumerate();
        if (devices.Count == 0)
        {
            Console.WriteLine("No INZONE control interface found.");
            return 1;
        }

        foreach (var d in devices)
        {
            Console.WriteLine(d);
            Console.WriteLine($"  {d.DevicePath}");
        }
        return 0;
    }

    private static int ShowStatus(InzoneBudsDevice device)
    {
        var model = device.GetModelInfo();
        Console.WriteLine($"Device       {model.Name}");
        if (model.IsEarbuds)
        {
            Console.WriteLine($"Serial       L {model.LeftSerial} / R {model.RightSerial} / dongle {model.DongleSerial}");
        }
        Console.WriteLine($"Battery      {device.GetBattery()}");
        Console.WriteLine($"Balance      {device.GetMixBalance()}");
        Console.WriteLine($"Volume       {device.GetHeadphoneVolume()}");
        Console.WriteLine($"Microphone   {DescribeMic(device)}");
        Console.WriteLine($"Sidetone     {device.GetSidetoneVolume()}");
        return 0;
    }

    private static int Balance(InzoneBudsDevice device, string[] args)
    {
        if (args.Length == 0) return Print(device.GetMixBalance().ToString());

        string arg = args[0];
        if (arg is "centre" or "center") return Print(device.SetMixBalance(MixBalance.Centre).ToString());

        var (value, relative) = ParseAmount(arg, "balance");
        var result = relative ? device.AdjustMixBalance(value) : device.SetMixBalance(value);
        return Print(result.ToString());
    }

    private static int Volume(InzoneBudsDevice device, string[] args)
    {
        if (args.Length == 0) return Print(device.GetHeadphoneVolume().ToString());

        switch (args[0].ToLowerInvariant())
        {
            case "mute": return Print(device.SetHeadphoneVolume(device.GetHeadphoneVolume().Value, muted: true).ToString());
            case "unmute": return Print(device.SetHeadphoneVolume(device.GetHeadphoneVolume().Value, muted: false).ToString());
            case "toggle": return Print(device.ToggleHeadphoneMute().ToString());
        }

        var (value, relative) = ParseAmount(args[0], "volume");
        var result = relative ? device.AdjustHeadphoneVolume(value) : device.SetHeadphoneVolume(value);
        return Print(result.ToString());
    }

    /// <summary>
    /// Mute is a headset setting; the level is the Windows capture endpoint. INZONE Hub splits them
    /// the same way, so both are reported together.
    /// </summary>
    private static string DescribeMic(InzoneBudsDevice device)
    {
        string mute = device.GetMicVolume().Muted ? "muted" : "unmuted";
        try
        {
            return $"{mute}, level {device.GetMicLevel()}%";
        }
        catch (InvalidOperationException)
        {
            return mute;
        }
    }

    private static int Mic(InzoneBudsDevice device, string[] args)
    {
        if (args.Length == 0) return Print(DescribeMic(device));

        switch (args[0].ToLowerInvariant())
        {
            case "mute": return Print(device.SetMicMuted(true).ToString());
            case "unmute": return Print(device.SetMicMuted(false).ToString());
            case "toggle": return Print(device.ToggleMicMute().ToString());
        }

        var (value, relative) = ParseAmount(args[0], "microphone level");
        int level = relative ? device.AdjustMicLevel(value) : device.SetMicLevel(value);
        return Print($"level {level}%");
    }

    private static int Watch(InzoneBudsDevice device)
    {
        // Input reports reach every open handle, so this also shows replies to requests made by
        // INZONE Hub or another copy of this tool, not only changes made at the headset.
        Console.WriteLine($"Watching {device.GetModelInfo().Name}. Press Ctrl+C to stop.");
        using var stop = new ManualResetEventSlim(false);

        device.SettingChanged += (_, e) =>
        {
            string detail = e.EventId switch
            {
                EventId.GameChatMixBalance when e.Param.Length >= 1 => new MixBalance(e.Param[0]).ToString(),
                EventId.HeadphoneVolume when e.Param.Length >= 3 => HeadphoneVolume.Parse(e.Param).ToString(),
                EventId.MicVolume when e.Param.Length >= 3 => MicVolume.Parse(e.Param).ToString(),
                EventId.BatteryInfo when e.Param.Length >= 2 => BatteryInfo.Parse(e.Param).ToString(),
                _ => Convert.ToHexString(e.Param),
            };
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {e.EventId,-22} {detail}");
        };

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
        stop.Wait();
        return 0;
    }

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
            """);
    }
}
