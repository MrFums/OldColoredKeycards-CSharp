using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace OldColoredKeycards.Keycards;

[Injectable(InjectionType.Singleton)]
public class KeycardUses(
    ISptLogger<KeycardUses> logger,
    ConfigManager configManager,
    TemplateTable templateTable)
{
    /// <summary>Returns how many keycards were changed.</summary>
    public int Apply()
    {
        var settings = configManager.Mod.Uses;

        // 0 is how the game marks an item that never runs out.
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
