using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using OldColoredKeycards.Keycards;

namespace OldColoredKeycards;

// Spawns go last, so every mod that replaces a map (ManimalInterchange), adds loose loot (LabsBoiler) or adds
// to boss loot pools has already done so. One below int.MaxValue so loot tools that run at int.MaxValue
// (LootSimulator) see the result.
[Injectable(TypePriority = int.MaxValue - 1)]
public class OldColoredKeycardsLateLoad(
    ISptLogger<OldColoredKeycardsLateLoad> logger,
    KeycardLooseLoot keycardLooseLoot,
    KeycardBotLoot keycardBotLoot) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var mapsChanged = keycardLooseLoot.Apply();
        var botSlotsChanged = keycardBotLoot.Apply();

        logger.Success($"[OldColoredKeycards] Loaded: 0.14 loose loot spawns on {mapsChanged} map(s), 0.14 odds in {botSlotsChanged} bot loot slot(s).");
        return Task.CompletedTask;
    }
}
