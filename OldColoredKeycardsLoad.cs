using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using OldColoredKeycards.Keycards;

namespace OldColoredKeycards;

// The 3.11 postDBLoad: the database is ready, and the handbook, traders and flea haven't read keycard
// prices or uses yet. Spawns wait for OldColoredKeycardsLateLoad.
[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class OldColoredKeycardsLoad(
    ISptLogger<OldColoredKeycardsLoad> logger,
    ConfigManager configManager,
    KeycardUses keycardUses,
    KeycardPrices keycardPrices) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        configManager.Load();

        var usesChanged = keycardUses.Apply();
        var pricesChanged = keycardPrices.Apply();

        var uses = configManager.Mod.Uses.Infinite ? "infinite uses" : $"{configManager.Mod.Uses.MaxUses} uses";
        logger.Success($"[OldColoredKeycards] {usesChanged} keycard(s) set to {uses}, {pricesChanged} price(s) changed.");
        if (configManager.Mod.DebugLogging)
        {
            logger.Warning("[OldColoredKeycards] debugLogging is on: every keycard change will be logged.");
        }

        return Task.CompletedTask;
    }
}
