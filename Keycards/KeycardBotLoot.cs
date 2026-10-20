using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace OldColoredKeycards.Keycards;

// Bosses and cultists: each keycard's weight in a loot slot is set so its share of that slot's pool is its
// 0.14 share. Only slots that already hold keycards are touched. Runs at the end of startup
// (OldColoredKeycardsLateLoad), so items other mods add to the same pools are counted in the total.
// Partisan and BlackDiv bots have no 0.14 version and get the average 0.14 boss pocket share.
// The event cultists Oni and Prizrak are left as they are.
[Injectable(InjectionType.Singleton)]
public class KeycardBotLoot(
    ConfigManager configManager,
    BotTable botTable,
    OldKeycardSpawns oldSpawns)
{
    private static readonly string[] SlotNames = ["Pockets", "Backpack", "TacticalVest", "SecuredContainer", "SpecialLoot"];

    /// <summary>Returns how many bot loot slots were changed.</summary>
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

    // Everything else in the pool keeps its weight. The keycards get the weights that make
    // weight / (pool total) equal their 0.14 shares. A share of 0 removes that keycard.
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
