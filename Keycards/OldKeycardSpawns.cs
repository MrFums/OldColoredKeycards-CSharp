using System.Reflection;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace OldColoredKeycards.Keycards;

// Keycards/Data/old-keycard-spawns.json, made by Tools/Calibrate from SPT 3.9.8's database (EFT 0.14.9).
public record OldKeycardSpawnData
{
    /// <summary>
    /// Keycard-only copies of each map's 0.14 keycard spawn points, in looseLoot.json format. Their probability
    /// is the one calibrated against vanilla 4.1; the mod re-solves it against the server's real pool.
    /// </summary>
    [JsonPropertyName("looseLoot")]
    public Dictionary<string, List<Spawnpoint>> LooseLoot { get; set; } = new();

    /// <summary>Map -> spawn point template Id -> keycards per raid that spot gave in 0.14.</summary>
    [JsonPropertyName("spotTargets")]
    public Dictionary<string, Dictionary<string, double>> SpotTargets { get; set; } = new();

    /// <summary>Bot type -> loot slot -> keycard ID -> that keycard's 0.14 share of the slot's loot pool.</summary>
    [JsonPropertyName("bots")]
    public Dictionary<string, Dictionary<string, Dictionary<string, double>>> Bots { get; set; } = new();

    /// <summary>Keycard ID -> average 0.14 pocket share of the bosses, for bots with no 0.14 version.</summary>
    [JsonPropertyName("bossAverage")]
    public Dictionary<string, double> BossAverage { get; set; } = new();

    /// <summary>Bot types that get <see cref="BossAverage"/> in every slot that holds keycards.</summary>
    [JsonPropertyName("bossAverageBots")]
    public List<string> BossAverageBots { get; set; } = [];
}

[Injectable(InjectionType.Singleton)]
public class OldKeycardSpawns(JsonUtil jsonUtil)
{
    private const string Resource = "OldColoredKeycards.old-keycard-spawns.json";

    private OldKeycardSpawnData? _data;

    public OldKeycardSpawnData Data => _data ??= Load();

    private OldKeycardSpawnData Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException("the 0.14 keycard spawn data is missing from the DLL");
        using var reader = new StreamReader(stream);
        return jsonUtil.Deserialize<OldKeycardSpawnData>(reader.ReadToEnd()) ?? new OldKeycardSpawnData();
    }
}
