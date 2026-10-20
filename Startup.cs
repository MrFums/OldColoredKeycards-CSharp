using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Ragfair;
using OldColoredKeycards.Keycards;

namespace OldColoredKeycards;

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class OldColoredKeycardsLoad(
    ISptLogger<OldColoredKeycardsLoad> logger,
    ConfigManager configManager,
    KeycardUses keycardUses,
    KeycardHandbook keycardHandbook) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        configManager.Load();

        var usesChanged = keycardUses.Apply();
        var handbookChanged = keycardHandbook.Apply();

        var uses = configManager.Mod.Uses.Infinite ? "infinite uses" : $"{configManager.Mod.Uses.MaxUses} uses";
        logger.Success($"[OldColoredKeycards] {usesChanged} keycard(s) set to {uses}"
            + (configManager.Mod.Handbook.Enabled ? $", {handbookChanged} handbook price(s) changed." : "."));
        if (configManager.Mod.DebugLogging)
        {
            logger.Warning("[OldColoredKeycards] debugLogging is on: every keycard change will be logged.");
        }

        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public class OldColoredKeycardsFleaLoad(
    ISptLogger<OldColoredKeycardsFleaLoad> logger,
    ConfigManager configManager,
    KeycardPrices keycardPrices,
    ItemHelper itemHelper,
    HandbookHelper handbookHelper,
    TradersTable tradersTable) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var settings = configManager.Mod;
        var pricesChanged = keycardPrices.Apply();
        if (!settings.Prices.Enabled)
        {
            return Task.CompletedTask;
        }

        if (settings.Handbook.Enabled)
        {
            KeycardTraderPrices.Bind(configManager, itemHelper, handbookHelper, tradersTable);
            new KeycardHighestTraderPricePatch().Enable();
            new KeycardOfferFloorPatch().Enable();
            new KeycardSacrificePatch().Enable();
            new KeycardSacrificeValuePatch().Enable();
            new KeycardSacrificeEndPatch().Enable();
        }

        logger.Success($"[OldColoredKeycards] {pricesChanged} flea price(s) set.");
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks + 1)]
public class OldColoredKeycardsFleaCheck(
    ISptLogger<OldColoredKeycardsFleaCheck> logger,
    ConfigManager configManager,
    TemplateTable templateTable,
    TraderHelper traderHelper,
    RagfairOfferService ragfairOfferService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!configManager.Mod.DebugLogging)
        {
            return Task.CompletedTask;
        }

        foreach (var keycard in ColoredKeycards.All)
        {
            var handbook = templateTable.Handbook.Items?.FirstOrDefault(item => item.Id == keycard.Id)?.Price ?? 0;
            var fleaBase = templateTable.Prices.GetValueOrDefault(keycard.Id);
            var traderPrice = traderHelper.GetHighestSellToTraderPrice(keycard.Id);

            var offers = ragfairOfferService.GetOffersOfType(keycard.Id)?.ToList() ?? [];
            var money = offers.Where(offer => offer.Requirements?.All(requirement => Money.GetMoneyTpls().Contains(requirement.TemplateId)) == true).ToList();
            var perItem = money.Select(offer => (offer.RequirementsCost ?? 0) / Math.Max(1, offer.Quantity)).Order().ToList();
            var cheapest = perItem.Count > 0 ? $"{perItem[0]:N0}" : "-";
            var median = perItem.Count > 0 ? $"{perItem[perItem.Count / 2]:N0}" : "-";

            logger.Info($"[OldColoredKeycards] Flea check {keycard.Name}: base {fleaBase:N0}, handbook {handbook:N0}, highest trader price {traderPrice:N0}, "
                + $"{offers.Count} offer(s) ({offers.Count - money.Count} barter), cheapest {cheapest}, median {median}");
        }

        return Task.CompletedTask;
    }
}

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
