using System.Text.Json;
using System.Text.Json.Serialization;
using OldColoredKeycards.Keycards;

namespace OldColoredKeycards;

// config.jsonc, one section per area. Each section's settings live next to that feature's code.
// Settings missing from the file keep their defaults.
public record ModConfig
{
    /// <summary>How many times each keycard can be used.</summary>
    [JsonPropertyName("uses")]
    public KeycardUsesConfig Uses { get; set; } = new();

    /// <summary>The Flea Market price of each keycard.</summary>
    [JsonPropertyName("prices")]
    public KeycardPricesConfig Prices { get; set; } = new();

    /// <summary>Where keycards spawn, and how often.</summary>
    [JsonPropertyName("spawns")]
    public KeycardSpawnsConfig Spawns { get; set; } = new();

    /// <summary>Log every keycard change. For testing.</summary>
    [JsonPropertyName("debugLogging")]
    public bool DebugLogging { get; set; } = false;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}
