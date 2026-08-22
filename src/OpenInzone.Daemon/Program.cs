// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 penguinwokrs

namespace OpenInzone.Daemon;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Without this a redirected stdout stays block-buffered, so `inzoned | tee log.txt`
        // shows nothing until the daemon exits.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

        string configPath = args.Length > 0 ? args[0] : HotkeyConfig.DefaultPath;

        HotkeyConfig config;
        try
        {
            config = HotkeyConfig.LoadOrCreate(configPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read {configPath}: {ex.Message}");
            return 1;
        }

        if (config.Bindings.Count == 0)
        {
            Console.Error.WriteLine($"{configPath} has no bindings. Add one and start again.");
            return 1;
        }

        using var controller = new DeviceController();
        var actions = new Dictionary<int, Action>();
        int registered = 0;
        int id = 1;

        foreach (var binding in config.Bindings)
        {
            KeyCombo combo;
            Action action;
            try
            {
                combo = KeyCombo.Parse(binding.Keys);
                action = BuildAction(controller, binding);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Skipping '{binding.Keys}': {ex.Message}");
                continue;
            }

            if (!Native.RegisterHotKey(IntPtr.Zero, id, combo.Modifiers, combo.VirtualKey))
            {
                Console.Error.WriteLine(
                    $"Could not claim {combo}. Another application already holds that combination.");
                continue;
            }

            actions[id] = action;
            Console.WriteLine($"{combo,-20} {Describe(binding)}");
            id++;
            registered++;
        }

        if (registered == 0)
        {
            Console.Error.WriteLine("No hotkeys were registered.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Listening. Press Ctrl+C to stop.");

        uint threadId = Native.GetCurrentThreadId();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Native.PostThreadMessageW(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        };

        while (Native.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.Message != Native.WM_HOTKEY) continue;
            if (actions.TryGetValue((int)msg.WParam, out var action)) controller.Post(action);
        }

        for (int i = 1; i < id; i++) Native.UnregisterHotKey(IntPtr.Zero, i);
        Console.WriteLine("Stopped.");
        return 0;
    }

    private static Action BuildAction(DeviceController controller, HotkeyConfig.Binding binding)
        => binding.Action.ToLowerInvariant() switch
        {
            "balance" when binding.Value is int v => () => controller.SetBalance(v),
            "balance" when binding.Delta is int d => () => controller.AdjustBalance(d),
            "balance" => throw new ArgumentException("'balance' needs either a delta or a value."),

            "volume" when binding.Value is int v => () => controller.SetVolume(v),
            "volume" when binding.Delta is int d => () => controller.AdjustVolume(d),
            "volume" => throw new ArgumentException("'volume' needs either a delta or a value."),

            "mic-level" when binding.Value is int v => () => controller.SetMicLevel(v),
            "mic-level" when binding.Delta is int d => () => controller.AdjustMicLevel(d),
            "mic-level" => throw new ArgumentException("'mic-level' needs either a delta or a value."),

            "volume-mute" => controller.ToggleVolumeMute,
            "mic-mute" => controller.ToggleMicMute,

            _ => throw new ArgumentException(
                $"unknown action '{binding.Action}'. Use balance, volume, mic-level, volume-mute or mic-mute."),
        };

    private static string Describe(HotkeyConfig.Binding b) => b switch
    {
        { Value: int v } => $"{b.Action} = {v}",
        { Delta: int d } => $"{b.Action} {d:+#;-#;0}",
        _ => b.Action,
    };
}
