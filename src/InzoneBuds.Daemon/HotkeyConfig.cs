using System.Text.Json;
using System.Text.Json.Serialization;

namespace InzoneBuds.Daemon;

public sealed class HotkeyConfig
{
    [JsonPropertyName("bindings")]
    public List<Binding> Bindings { get; set; } = [];

    public sealed class Binding
    {
        /// <summary>A combination such as "Ctrl+Alt+Up". Modifiers are Ctrl, Alt, Shift and Win.</summary>
        [JsonPropertyName("keys")]
        public string Keys { get; set; } = "";

        /// <summary>balance, volume, mic-mute, volume-mute.</summary>
        [JsonPropertyName("action")]
        public string Action { get; set; } = "";

        /// <summary>How far to move the value. Ignored when <see cref="Value"/> is set.</summary>
        [JsonPropertyName("delta")]
        public int? Delta { get; set; }

        /// <summary>Jump straight to this value.</summary>
        [JsonPropertyName("value")]
        public int? Value { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static HotkeyConfig Default() => new()
    {
        Bindings =
        [
            new Binding { Keys = "Ctrl+Alt+Up",    Action = "balance", Delta = 10 },
            new Binding { Keys = "Ctrl+Alt+Down",  Action = "balance", Delta = -10 },
            new Binding { Keys = "Ctrl+Alt+Home",  Action = "balance", Value = 50 },
            new Binding { Keys = "Ctrl+Alt+Right", Action = "volume",  Delta = 1 },
            new Binding { Keys = "Ctrl+Alt+Left",  Action = "volume",  Delta = -1 },
            new Binding { Keys = "Ctrl+Alt+PageUp",   Action = "mic-level", Delta = 5 },
            new Binding { Keys = "Ctrl+Alt+PageDown", Action = "mic-level", Delta = -5 },
            new Binding { Keys = "Ctrl+Alt+Shift+M", Action = "mic-mute" },
        ],
    };

    /// <summary>Loads the config, writing the default file the first time the daemon runs.</summary>
    public static HotkeyConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Default(), Options));
            Console.WriteLine($"Wrote a starter config to {path}");
            return Default();
        }

        var config = JsonSerializer.Deserialize<HotkeyConfig>(File.ReadAllText(path), Options)
                     ?? throw new InvalidDataException($"{path} is empty or not valid JSON.");
        return config;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "inzone-buds-ctl", "hotkeys.json");
}
