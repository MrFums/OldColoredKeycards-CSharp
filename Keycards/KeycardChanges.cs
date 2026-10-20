using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace OldColoredKeycards.Keycards;

[Injectable(InjectionType.Singleton)]
public class KeycardUses(
    ISptLogger<KeycardUses> logger,
    ConfigManager configManager,
    TemplateTable templateTable)
{
        public int Apply()
    {
        var settings = configManager.Mod.Uses;

        var maxUses = settings.Infinite ? 0 : settings.MaxUses;

        var changed = 0;
        foreach (var keycard in ColoredKeycards.All)
        {
            if (!templateTable.Items.TryGetValue(keycard.Id, out var item) || item.Properties is null)
            {
                logger.Warning($"[OldColoredKeycards] {keycard.Name} ({keycard.Id}) not found in the database, skipping.");
                continue;
            }

            item.Properties.MaximumNumberOfUsage = maxUses;
            changed++;
            DebugLog.Write(() => $"{keycard.Name} uses set to {(settings.Infinite ? "infinite" : maxUses)}.");
        }

        return changed;
    }
}

[Injectable(InjectionType.Singleton)]
public class KeycardPrices(
    ConfigManager configManager,
    TemplateTable templateTable,
    HideoutTable hideoutTable,
    RagfairConfig ragfairConfig)
{
        public int Apply()
    {
        var settings = configManager.Mod.Prices;
        if (!settings.Enabled)
        {
            DebugLog.Write(() => "prices.enabled is false, keycard prices left alone.");
            return 0;
        }

        var rebuild = ragfairConfig.Dynamic.GenerateBaseFleaPrices;
        var craftItems = rebuild.UseHideoutCraftMultiplier ? CraftItems() : [];

        var changed = 0;
        foreach (var keycard in ColoredKeycards.All)
        {
            var price = keycard.Price(settings);
            if (price <= 0)
            {
                continue;
            }

            templateTable.Prices[keycard.Id] = price;

            if (rebuild.UseHandbookPrice)
            {
                var craftBonus = craftItems.Contains(keycard.Id) ? rebuild.HideoutCraftMultiplier : 0;
                rebuild.ItemTplMultiplierOverride ??= new();
                rebuild.ItemTplMultiplierOverride[keycard.Id] = -craftBonus;
            }

            changed++;
            DebugLog.Write(() => $"{keycard.Name} flea price {price:N0}.");
        }

        return changed;
    }

    private HashSet<MongoId> CraftItems()
    {
        return hideoutTable.Production.Recipes?
            .SelectMany(recipe => recipe.Requirements ?? [])
            .Where(requirement => requirement.Type == "Item" && requirement.TemplateId is not null)
            .Select(requirement => requirement.TemplateId!.Value)
            .ToHashSet() ?? [];
    }
}

[Injectable(InjectionType.Singleton)]
public class KeycardHandbook(
    ISptLogger<KeycardHandbook> logger,
    ConfigManager configManager,
    TemplateTable templateTable)
{
        public int Apply()
    {
        var settings = configManager.Mod.Handbook;
        if (!settings.Enabled)
        {
            return 0;
        }

        var changed = 0;
        foreach (var keycard in ColoredKeycards.All)
        {
            var price = keycard.HandbookPrice(settings);
            if (price <= 0)
            {
                continue;
            }

            var entry = templateTable.Handbook.Items?.FirstOrDefault(item => item.Id == keycard.Id);
            if (entry is null)
            {
                logger.Warning($"[OldColoredKeycards] {keycard.Name} ({keycard.Id}) not found in the handbook, skipping.");
                continue;
            }

            var old = entry.Price;
            entry.Price = price;
            changed++;
            DebugLog.Write(() => $"{keycard.Name} handbook price {old:N0} -> {price:N0}.");
        }

        return changed;
    }
}
