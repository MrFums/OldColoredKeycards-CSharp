using System.Collections.Concurrent;
using System.Diagnostics;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace OldColoredKeycards.Keycards;

// Takes the colored keycards out of every loose loot spawn point, and adds the 0.14 ones back.
// SPT reads a map's loose loot fresh from disk for every raid, so this is a transformer on that read,
// not a one-off edit. It is registered at the end of startup (OldColoredKeycardsLateLoad), after other mods:
//  - map replacements (ManimalInterchange's Interchange) are in place, so it lands on the data really served.
//    MapVariants copies it across when it switches a map back to the shipped data.
//  - mods that add spawn points with their own transformer (LabsBoiler) run first, so the pool is complete.
[Injectable(InjectionType.Singleton)]
public class KeycardLooseLoot(
    ConfigManager configManager,
    LocationTable locationTable,
    LocationConfig locationConfig,
    OldKeycardSpawns oldSpawns)
{
    // Solved weights per map and pool. The pool is the same for every raid unless a mod switches the map's data.
    private readonly ConcurrentDictionary<string, double[]> _weights = new();

    /// <summary>Returns how many maps were changed.</summary>
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

            // Maps with no 0.14 spots (Factory, Ground Zero, Labyrinth...) still lose their 4.1 keycards.
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
                // SPT writes the chosen item back into the spawn point it draws, so each read gets its own copies.
                spawnpoints.Add(oldSpots[i] with { Probability = weights[i], Template = oldSpots[i].Template! with { } });
            }
        }

        looseLoot.Spawnpoints = spawnpoints;
        looseLoot.SpawnpointsForced = (looseLoot.SpawnpointsForced ?? [])
            .Where(spot => spot.Template?.Items?.Any(item => ColoredKeycards.Ids.Contains(item.Template)) != true)
            .ToList();

        return looseLoot;
    }

    // The pool SPT will draw from on this map, as GenerateDynamicLoot builds it.
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

    // Same seed for the same map every run, so the weights don't move between restarts.
    private static int Seed(string mapId)
    {
        var hash = 17;
        foreach (var character in mapId)
        {
            hash = hash * 31 + character;
        }

        return hash;
    }

    // The spawn point without its keycard options. Null if keycards were all it could spawn.
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
