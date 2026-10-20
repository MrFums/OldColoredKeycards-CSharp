using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Hideout;
using SPTarkov.Server.Core.Services.Ragfair;

namespace OldColoredKeycards.Keycards;

internal static class KeycardTraderPrices
{
    private static ConfigManager? _config;
    private static ItemHelper? _itemHelper;
    private static HandbookHelper? _handbook;
    private static TradersTable? _traders;

    private static readonly ConcurrentDictionary<MongoId, double> BestBuyPrices = new();

    public static void Bind(ConfigManager configManager, ItemHelper itemHelper, HandbookHelper handbookHelper, TradersTable tradersTable)
    {
        _config = configManager;
        _itemHelper = itemHelper;
        _handbook = handbookHelper;
        _traders = tradersTable;
    }

    public static double? FleaPrice(MongoId tpl)
    {
        var keycard = ColoredKeycards.All.FirstOrDefault(card => card.Id == tpl);
        var price = keycard is null || _config is null ? 0 : keycard.Price(_config.Mod.Prices);
        return price > 0 ? price : null;
    }

    public static double BestBuyPrice(MongoId tpl)
    {
        return BestBuyPrices.GetOrAdd(tpl, static tpl =>
        {
            var handbookPrice = _handbook?.GetTemplatePrice(tpl) ?? 0;
            if (handbookPrice <= 0 || _traders is null)
            {
                return 0;
            }

            var best = 0.0;
            foreach (var trader in _traders.Values)
            {
                var traderBase = trader.Base;
                if (!Buys(traderBase, tpl) || traderBase.LoyaltyLevels is not { Count: > 0 } levels)
                {
                    continue;
                }

                var lowestCut = levels.Min(level => level.BuyPriceCoefficient ?? 100.0);
                best = Math.Max(best, handbookPrice * (100.0 - lowestCut) / 100.0);
            }

            return best;
        });
    }

    public static double Floor(MongoId tpl, Item? item, MongoId currency)
    {
        var floor = BestBuyPrice(tpl) * (item is null ? 1.0 : _itemHelper!.GetItemQualityModifier(item));
        if (currency == Money.ROUBLES)
        {
            return Math.Ceiling(floor);
        }

        var rate = _handbook!.GetTemplatePrice(currency);
        return rate > 0 ? Math.Ceiling(floor / rate) : _handbook.FromRoubles(floor, currency);
    }

    private static bool Buys(TraderBase trader, MongoId tpl)
    {
        return Matches(trader.ItemsBuy, tpl) && !Matches(trader.ItemsBuyProhibited, tpl);
    }

    private static bool Matches(ItemBuyData? list, MongoId tpl)
    {
        if (list is null)
        {
            return false;
        }

        return list.IdList.Contains(tpl)
            || (list.Category.Count > 0 && _itemHelper!.IsOfBaseclasses(tpl, list.Category));
    }
}

public class KeycardHighestTraderPricePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(TraderHelper).GetMethod(
            nameof(TraderHelper.GetHighestSellToTraderPrice),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(MongoId tpl, ref double __result)
    {
        if (ColoredKeycards.Ids.Contains(tpl))
        {
            __result = KeycardTraderPrices.BestBuyPrice(tpl);
        }
    }
}

public class KeycardOfferFloorPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairPriceService).GetMethod(
            nameof(RagfairPriceService.GetDynamicItemPrice),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(MongoId itemTemplateId, MongoId desiredCurrency, Item? item, ref double? __result)
    {
        if (__result is not { } price || !ColoredKeycards.Ids.Contains(itemTemplateId))
        {
            return;
        }

        var floor = KeycardTraderPrices.Floor(itemTemplateId, item, desiredCurrency);
        if (price < floor)
        {
            __result = floor;
            DebugLog.Write(() => $"Flea offer for {itemTemplateId} raised {price:N0} -> {floor:N0} (what a trader pays).");
        }
    }
}

public class KeycardSacrificePatch : AbstractPatch
{
    [ThreadStatic]
    internal static bool InSacrifice;

    protected override MethodBase GetTargetMethod()
    {
        return typeof(CircleOfCultistService).GetMethod(
            nameof(CircleOfCultistService.StartSacrifice),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPrefix]
    public static void Prefix()
    {
        InSacrifice = true;
    }

    [PatchFinalizer]
    public static void Finalizer()
    {
        InSacrifice = false;
    }
}

public class KeycardSacrificeValuePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(ItemHelper).GetMethod(
            nameof(ItemHelper.GetItemPrice),
            BindingFlags.Instance | BindingFlags.Public,
            [typeof(MongoId)])!;
    }

    [PatchPostfix]
    public static void Postfix(MongoId tpl, ref double? __result)
    {
        if (KeycardSacrificePatch.InSacrifice && ColoredKeycards.Ids.Contains(tpl) && KeycardTraderPrices.FleaPrice(tpl) is { } fleaPrice)
        {
            var handbookPrice = __result;
            __result = fleaPrice;
            DebugLog.Write(() => $"Cultist Circle: {tpl} counted at its flea price {fleaPrice:N0} instead of {handbookPrice:N0}.");
        }
    }
}

public class KeycardSacrificeEndPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(CircleOfCultistService).GetMethod(
            "GetRewardAmountMultiplier",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPrefix]
    public static void Prefix()
    {
        KeycardSacrificePatch.InSacrifice = false;
    }
}
