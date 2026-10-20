using System.Text.Json;
using System.Text.Json.Serialization;

namespace OldColoredKeycards.Keycards;

public record KeycardUsesConfig
{
    public const int DefaultMaxUses = 40;

    /// <summary>true = keycards never run out of uses.</summary>
    [JsonPropertyName("infinite")]
    public bool Infinite { get; set; } = true;

    /// <summary>Uses per keycard when infinite is off.</summary>
    [JsonPropertyName("maxUses")]
    public int MaxUses { get; set; } = DefaultMaxUses;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

// templates/prices.json: the price the Flea Market works from. Trader prices come from the handbook and don't change.
// 0 = keep the normal price for that keycard.
public record KeycardPricesConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("blue")]
    public double Blue { get; set; } = 6_100_000;

    [JsonPropertyName("red")]
    public double Red { get; set; } = 11_300_000;

    [JsonPropertyName("green")]
    public double Green { get; set; } = 11_500_000;

    [JsonPropertyName("black")]
    public double Black { get; set; } = 1_650_000;

    [JsonPropertyName("violet")]
    public double Violet { get; set; } = 4_000_000;

    [JsonPropertyName("yellow")]
    public double Yellow { get; set; } = 2_300_000;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

// Where keycards spawn, and how often, as in EFT 0.14 (SPT 3.9). See Keycards/Data and Tools/Calibrate.
public record KeycardSpawnsConfig
{
    /// <summary>Loose keycards spawn at their 0.14 spots, as often as in 0.14.</summary>
    [JsonPropertyName("oldLooseLoot")]
    public bool OldLooseLoot { get; set; } = true;

    /// <summary>Bosses and cultists carry keycards as often as in 0.14.</summary>
    [JsonPropertyName("oldBotLoot")]
    public bool OldBotLoot { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}
