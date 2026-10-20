using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace OldColoredKeycards.Keycards;

[Injectable(InjectionType.Singleton)]
public class KeycardPrices(
    ISptLogger<KeycardPrices> logger,
    ConfigManager configManager,
    TemplateTable templateTable)
{
    /// <summary>Returns how many prices were changed.</summary>
    public int Apply()
    {
        var settings = configManager.Mod.Prices;
        if (!settings.Enabled)
        {
            DebugLog.Write(() => "prices.enabled is false, keycard prices left alone.");
            return 0;
        }

        var changed = 0;
        foreach (var keycard in ColoredKeycards.All)
        {
            var price = keycard.Price(settings);
            if (price <= 0)
            {
                continue;
            }

            if (!templateTable.Prices.ContainsKey(keycard.Id))
            {
                logger.Warning($"[OldColoredKeycards] {keycard.Name} ({keycard.Id}) not found in the price table, skipping.");
                continue;
            }

            var old = templateTable.Prices[keycard.Id];
            templateTable.Prices[keycard.Id] = price;
            changed++;
            DebugLog.Write(() => $"{keycard.Name} price {old:N0} -> {price:N0}.");
        }

        return changed;
    }
}
