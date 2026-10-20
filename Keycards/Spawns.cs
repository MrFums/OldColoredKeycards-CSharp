using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

namespace OldColoredKeycards.Keycards;

public record OldKeycardSpawnData
{
        [JsonPropertyName("looseLoot")]
    public Dictionary<string, List<Spawnpoint>> LooseLoot { get; set; } = new();

        [JsonPropertyName("spotTargets")]
    public Dictionary<string, Dictionary<string, double>> SpotTargets { get; set; } = new();

        [JsonPropertyName("bots")]
    public Dictionary<string, Dictionary<string, Dictionary<string, double>>> Bots { get; set; } = new();

        [JsonPropertyName("bossAverage")]
    public Dictionary<string, double> BossAverage { get; set; } = new();

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

[Injectable(InjectionType.Singleton)]
public class KeycardLooseLoot(
    ConfigManager configManager,
    LocationTable locationTable,
    LocationConfig locationConfig,
    OldKeycardSpawns oldSpawns)
{
    private readonly ConcurrentDictionary<string, double[]> _weights = new();

        public int Apply()
    {
        if (!configManager.Mod.Spawns.OldLooseLoot)
        {
            return 0;
        }

        var changed = 0;
        foreach (var location in locationTable.GetDictionary().Values)
        {
            var mapId = location.Base?.Id?.ToLowerInvariant();
            if (mapId is null || location.LooseLoot is null)
            {
                continue;
            }

            var oldSpots = oldSpawns.Data.LooseLoot.GetValueOrDefault(mapId) ?? [];
            var spotTargets = oldSpawns.Data.SpotTargets.GetValueOrDefault(mapId);
            var targets = oldSpots.Select(spot => spotTargets?.GetValueOrDefault(spot.Template?.Id ?? string.Empty) ?? 0).ToArray();
            location.LooseLoot.AddTransformer(looseLoot => Restore(mapId, looseLoot, oldSpots, targets));
            changed++;
            DebugLog.Write(() => $"{mapId}: 4.1 keycard spawns will be removed and {oldSpots.Count} 0.14 spawn point(s) added.");
        }

        return changed;
    }

    private LooseLoot? Restore(string mapId, LooseLoot? looseLoot, List<Spawnpoint> oldSpots, double[] targets)
    {
        if (looseLoot is null)
        {
            return null;
        }

        var spawnpoints = (looseLoot.Spawnpoints ?? []).Select(WithoutKeycards).OfType<Spawnpoint>().ToList();
        var weights = oldSpots.Count == 0 ? [] : WeightsFor(mapId, looseLoot, spawnpoints, targets);
        for (var i = 0; i < oldSpots.Count; i++)
        {
            if (weights[i] > 0)
            {
                spawnpoints.Add(oldSpots[i] with { Probability = weights[i], Template = oldSpots[i].Template! with { } });
            }
        }

        looseLoot.Spawnpoints = spawnpoints;
        looseLoot.SpawnpointsForced = (looseLoot.SpawnpointsForced ?? [])
            .Where(spot => spot.Template?.Items?.Any(item => ColoredKeycards.Ids.Contains(item.Template)) != true)
            .ToList();

        return looseLoot;
    }

    private double[] WeightsFor(string mapId, LooseLoot looseLoot, List<Spawnpoint> spawnpoints, double[] targets)
    {
        var blacklist = locationConfig.LooseLootBlacklist.GetValueOrDefault(mapId);
        var drawable = spawnpoints
            .Where(spot => spot.Template?.IsAlwaysSpawn != true && !(blacklist?.Contains(spot.Template?.Id ?? string.Empty) ?? false))
            .ToList();
        var guaranteed = drawable.Count(spot => spot.Probability == 1.0);
        var poolWeights = drawable.Where(spot => spot.Probability != 1.0).Select(spot => spot.Probability ?? 0).ToArray();

        var multiplier = locationConfig.LooseLootMultiplier.TryGetValue(mapId, out var value) ? value : locationConfig.LooseLootMultiplier["default"];
        var mean = looseLoot.SpawnpointCount?.Mean ?? 0;
        var std = looseLoot.SpawnpointCount?.Std ?? 0;

        var key = $"{mapId}|{poolWeights.Length}|{poolWeights.Sum():R}|{guaranteed}|{mean:R}|{std:R}|{multiplier:R}";
        return _weights.GetOrAdd(key, _ =>
        {
            var clock = Stopwatch.StartNew();
            var weights = SpawnWeightSolver.Solve(poolWeights, guaranteed, mean, std, multiplier, targets, Seed(mapId));
            DebugLog.Write(() => $"{mapId}: weighed {targets.Length} 0.14 spawn point(s) against a pool of {poolWeights.Length} spots "
                + $"({guaranteed} certain, about {multiplier * mean:0} drawn per raid) in {clock.ElapsedMilliseconds} ms.");
            return weights;
        });
    }

    private static int Seed(string mapId)
    {
        var hash = 17;
        foreach (var character in mapId)
        {
            hash = hash * 31 + character;
        }

        return hash;
    }

    private static Spawnpoint? WithoutKeycards(Spawnpoint spot)
    {
        var keycardKeys = spot.Template?.Items?
            .Where(item => item.ComposedKey is not null && ColoredKeycards.Ids.Contains(item.Template))
            .Select(item => item.ComposedKey!)
            .ToHashSet();

        if (keycardKeys is not { Count: > 0 } || spot.ItemDistribution is null)
        {
            return spot;
        }

        var rest = spot.ItemDistribution
            .Where(entry => entry.ComposedKey?.Key is not { } key || !keycardKeys.Contains(key))
            .ToList();

        if (rest.Count == 0)
        {
            return null;
        }

        spot.ItemDistribution = rest;
        return spot;
    }
}

[Injectable(InjectionType.Singleton)]
public class KeycardBotLoot(
    ConfigManager configManager,
    BotTable botTable,
    OldKeycardSpawns oldSpawns)
{
    private static readonly string[] SlotNames = ["Pockets", "Backpack", "TacticalVest", "SecuredContainer", "SpecialLoot"];

        public int Apply()
    {
        if (!configManager.Mod.Spawns.OldBotLoot)
        {
            return 0;
        }

        var data = oldSpawns.Data;
        var changed = 0;
        foreach (var (botType, slots) in data.Bots)
        {
            changed += Apply(botType, slots);
        }

        foreach (var botType in data.BossAverageBots)
        {
            var slots = SlotNames.ToDictionary(slot => slot, _ => data.BossAverage);
            changed += Apply(botType, slots);
        }

        return changed;
    }

    private int Apply(string botType, Dictionary<string, Dictionary<string, double>> slots)
    {
        var pools = botTable.Types.GetValueOrDefault(botType)?.BotInventory?.Items;
        if (pools is null)
        {
            DebugLog.Write(() => $"Bot type {botType} not found, skipping.");
            return 0;
        }

        var changed = 0;
        foreach (var (slot, shares) in slots)
        {
            var pool = Slot(pools, slot);
            if (pool is not null && pool.Keys.Any(ColoredKeycards.Ids.Contains) && SetShares(pool, shares))
            {
                changed++;
                DebugLog.Write(() => $"{botType} {slot}: keycards set to their 0.14 share of the loot pool.");
            }
        }

        return changed;
    }

    private static Dictionary<MongoId, double>? Slot(ItemPools pools, string slot)
    {
        return slot switch
        {
            "Pockets" => pools.Pockets,
            "Backpack" => pools.Backpack,
            "TacticalVest" => pools.TacticalVest,
            "SecuredContainer" => pools.SecuredContainer,
            "SpecialLoot" => pools.SpecialLoot,
            _ => null
        };
    }

    private static bool SetShares(Dictionary<MongoId, double> pool, Dictionary<string, double> shares)
    {
        var rest = pool.Where(entry => !ColoredKeycards.Ids.Contains(entry.Key)).Sum(entry => entry.Value);
        var keycardShare = shares.Values.Sum();
        if (rest <= 0 || keycardShare >= 1)
        {
            return false;
        }

        foreach (var keycard in ColoredKeycards.All)
        {
            pool.Remove(keycard.Id);
            var share = shares.GetValueOrDefault(keycard.Id.ToString());
            if (share > 0)
            {
                pool[keycard.Id] = share * rest / (1 - keycardShare);
            }
        }

        return true;
    }
}
