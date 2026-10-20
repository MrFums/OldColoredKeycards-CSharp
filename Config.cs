using System.Text.Json.Serialization;
using System.Text.Json;

namespace OldColoredKeycards;

public record ModConfig
{
        [JsonPropertyName("uses")]
    public KeycardUsesConfig Uses { get; set; } = new();

        [JsonPropertyName("prices")]
    public KeycardPricesConfig Prices { get; set; } = new();

        [JsonPropertyName("handbook")]
    public KeycardHandbookConfig Handbook { get; set; } = new();

        [JsonPropertyName("spawns")]
    public KeycardSpawnsConfig Spawns { get; set; } = new();

        [JsonPropertyName("debugLogging")]
    public bool DebugLogging { get; set; } = false;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

public record KeycardUsesConfig
{
    public const int DefaultMaxUses = 40;

        [JsonPropertyName("infinite")]
    public bool Infinite { get; set; } = true;

        [JsonPropertyName("maxUses")]
    public int MaxUses { get; set; } = DefaultMaxUses;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

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

public record KeycardHandbookConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("blue")]
    public double Blue { get; set; } = 9_000_000;

    [JsonPropertyName("red")]
    public double Red { get; set; } = 16_500_000;

    [JsonPropertyName("green")]
    public double Green { get; set; } = 17_000_000;

    [JsonPropertyName("black")]
    public double Black { get; set; } = 2_500_000;

    [JsonPropertyName("violet")]
    public double Violet { get; set; } = 6_000_000;

    [JsonPropertyName("yellow")]
    public double Yellow { get; set; } = 3_500_000;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

public record KeycardSpawnsConfig
{
        [JsonPropertyName("oldLooseLoot")]
    public bool OldLooseLoot { get; set; } = true;

        [JsonPropertyName("oldBotLoot")]
    public bool OldBotLoot { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}
