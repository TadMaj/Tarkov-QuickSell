using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using EFT.UI.Ragfair;
using EFT.Utilities;
using JetBrains.Annotations;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UIFixesInterop;
using UnityEngine;

namespace QuickSell.Patches
{
    // This is the main patch handling most of the logic
    internal class ContextMenuPatch : ModulePatch
    {
        private static Trader[] traders = null;

        private static TarkovApplication GetApp()
        {
            return ClientAppUtils.GetMainApp();
        }

        private static MainMenuShowOperation GetMainMenu()
        {
            var app = GetApp();
            if (app == null)
            {
                Utils.SendError("TarkovApplication instance is null");
                return null;
            }

            var fieldInfo = typeof(TarkovApplication)
                .GetField("_menuOperation", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo == null)
            {
                Utils.SendError("Field '_menuOperation' not found in TarkovApplication");
                return null;
            }

            var mainMenuController = fieldInfo.GetValue(app) as MainMenuShowOperation;
            if (mainMenuController == null)
            {
                Utils.SendError("_menuOperation is null");
            }

            return mainMenuController;
        }

        private static IEftSession GetSession()
        {
            var mainMenuController = GetMainMenu();
            if (mainMenuController == null)
            {
                Utils.SendError("MainMenuShowOperation is null");
                return null;
            }

            var session = mainMenuController.iEftSession;
            if (session == null)
            {
                Utils.SendError("IEftSession is null");
            }

            return session;
        }

        protected override MethodBase GetTargetMethod()
        {
            var methodInfo = typeof(SimpleContextMenu).GetMethod(
                "ShowMenu",
                BindingFlags.Public | BindingFlags.Instance);

            if (methodInfo == null)
            {
                Utils.SendError("SimpleContextMenu.ShowMenu not found");
                return null;
            }

            return methodInfo.MakeGenericMethod(typeof(EItemInfoButton));
        }

        [CanBeNull]
        private static ITradingSession GetTraderInteractions(Trader bestTrader)
        {
            var interactions = bestTrader._trading;
            if (interactions == null)
            {
                Utils.SendError("ITradingSession is null for the provided trader instance");
            }

            return interactions;
        }

        [PatchPrefix]
        private static void Prefix(
            ContextInteractions<EItemInfoButton> contextInteractions,
            Item item)
        {
            if (contextInteractions is not InventoryItemContextInteractions) return;

            if (item == null)
            {
                Utils.SendError("No item is selected");
                return;
            }

            var itemContext = (contextInteractions as BaseItemContextInteractions)?.ItemContext;
            if (itemContext == null || itemContext.ViewType != EItemViewType.Inventory) return;
            if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is not HideoutGameWorld) return;
            if (Singleton<MenuUI>.Instantiated &&
                Singleton<MenuUI>.Instance.HideoutAreaTransferItemsScreen != null &&
                Singleton<MenuUI>.Instance.HideoutAreaTransferItemsScreen.isActiveAndEnabled) return;
            if (item.GetAllParentItems().Any(x => x is InventoryEquipment)) return;
            if (item.Parent.Container.ParentItem.TemplateId == "55d7217a4bdc2d86028b456d") return;

            var dynamicInteractions = contextInteractions._dynamicInteractions;
            if (dynamicInteractions is null) return;

            var unloadSprite = ResourcesCache.Pop<Sprite>("Characteristics/Icons/UnloadAmmo");

            if (Plugin.EnableQuickSellFlea)
            {
                dynamicInteractions["QuickSell (Flea)"] = new DynamicContextInteraction(
                    "QuickSell (Flea)",
                    "QuickSell (Flea)",
                    () => SellToFlea(item),
                    unloadSprite);
            }

            if (Plugin.EnableQuickSellTraders)
            {
                dynamicInteractions["QuickSell (Trader)"] = new DynamicContextInteraction(
                    "QuickSell (Trader)",
                    "QuickSell (Trader)",
                    () => SellToTraders(item),
                    unloadSprite);
            }
        }

        public static void SellToTraders(Item item)
        {
            var items = GetItemsToSell(item);
            var validItems = new List<Item>();
            var total = 0;
            foreach (var i in items)
            {
                var bestTrader = SelectTrader(i);
                if (bestTrader != null && bestTrader.GetUserItemPrice(i) is { } p)
                {
                    validItems.Add(i);
                    total += p.Amount;
                }
            }
            if (validItems.Count == 0)
            {
                Utils.SendError("No items can be sold to traders");
                return;
            }
            var validIds = new HashSet<string>(validItems.Select(i => i.Id));
            ConfirmWindow(
                () =>
                {
                    if (IsMultiSelectActive())
                    {
                        MultiSelect.Apply(i =>
                        {
                            if (i != null && validIds.Contains(i.Id))
                            {
                                SellTrader(i);
                            }
                        }, ItemUiContext.Instance);
                        return;
                    }

                    foreach (var i in validItems)
                    {
                        SellTrader(i);
                    }
                },
                "to the traders",
                validItems.Count,
                total);
        }

        public static void SellTrader(Item item)
        {
            try
            {
                var bestTrader = SelectTrader(item);
                if (bestTrader == null)
                {
                    Utils.SendError("Item cannot be sold traders");
                    return;
                }

                var price = bestTrader.GetUserItemPrice(item).Value.Amount;
                Utils.SendNotification($"Profit: {price}");

                var interactions = GetTraderInteractions(bestTrader);
                if (interactions is null)
                {
                    Utils.SendError("Failed to get trader interactions");
                    return;
                }

                interactions.ConfirmSell(
                    bestTrader.Id,
                    [new TradingItemReference { Item = item, Count = item.StackObjectsCount }],
                    price,
                    new Callback(PlaySellSound)
                );
            }
            catch (Exception ex)
            {
                Utils.SendError(ex.ToString());
                Plugin.LogSource.LogWarning(ex.ToString());
            }
        }

        public static void SellToFlea(Item item)
        {
            var items = GetItemsToSell(item);
            try
            {
                if (!Singleton<MenuUI>.Instantiated || Singleton<MenuUI>.Instance?.TradingScreen == null)
                {
                    Utils.SendError("MenuUI is not available");
                    return;
                }
                var session = GetSession();
                if (session == null) return;
                var mainMenu = GetMainMenu();
                if (mainMenu == null) return;
                var inventoryController = mainMenu.InventoryController;
                if (inventoryController == null || inventoryController is not ItemController traderController)
                {
                    Utils.SendError("Could not load inventory");
                    return;
                }
                var ragFairClass = session.RagFair;
                if (!ragFairClass.Available)
                {
                    Utils.SendError("Flea market is not available");
                    return;
                }
                var helper = new RagfairNewOfferContext(inventoryController.Inventory.Stash.Grids[0], traderController);
                var validItems = items.Where(i => helper.HighlightedAtRagfair(i)).ToList();
                if (validItems.Count == 0)
                {
                    Utils.SendError("No items can be sold on the flea");
                    return;
                }
                var maxOffers = ragFairClass.GetMaxOffersCount(ragFairClass.MyRating);
                var currentOffers = ragFairClass.MyOffersCount;
                if (!Plugin.IgnoreFleaCapacity && currentOffers + validItems.Count > maxOffers)
                {
                    Utils.SendError("Not enough flea offer slots");
                    return;
                }
                if (validItems.Count == 1)
                {
                    var single = validItems[0];
                    ragFairClass.GetMarketPrices(single.TemplateId, (ItemMarketPrices result) =>
                    {
                        try
                        {
                            var price = (int)Math.Ceiling(result.avg / 100.0 * Plugin.AvgPricePercent);
                            var fee = (int)Math.Ceiling(PriceCalculator.CalculateTaxPrice(single, single.StackObjectsCount, price, false));
                            ConfirmWindow(() => DoFleaOffer(single, session, price), "on the flea", 1, price * single.StackObjectsCount, fee);
                        }
                        catch (Exception ex)
                        {
                            Utils.SendError(ex.ToString());
                            Plugin.LogSource.LogWarning(ex.ToString());
                        }
                    });
                    return;
                }
                var pending = validItems.Count;
                var prices = new Dictionary<string, int>();
                var lockObj = new object();
                foreach (var i in validItems)
                {
                    ragFairClass.GetMarketPrices(i.TemplateId, (ItemMarketPrices result) =>
                    {
                        lock (lockObj)
                        {
                            try
                            {
                                prices[i.Id] = (int)Math.Ceiling(result.avg / 100.0 * Plugin.AvgPricePercent);
                                if (--pending == 0)
                                {
                                    var total = validItems.Sum(v => prices[v.Id] * v.StackObjectsCount);
                                    var totalFee = validItems.Sum(v => (int)Math.Ceiling(PriceCalculator.CalculateTaxPrice(v, v.StackObjectsCount, prices[v.Id], false)));
                                    ConfirmWindow(
                                        () =>
                                        {
                                            if (IsMultiSelectActive())
                                            {
                                                MultiSelect.Apply(selectedItem =>
                                                {
                                                    if (selectedItem != null && prices.TryGetValue(selectedItem.Id, out var price))
                                                    {
                                                        DoFleaOffer(selectedItem, session, price);
                                                    }
                                                }, ItemUiContext.Instance);
                                                return;
                                            }

                                            foreach (var validItem in validItems)
                                            {
                                                if (prices.TryGetValue(validItem.Id, out var price))
                                                {
                                                    DoFleaOffer(validItem, session, price);
                                                }
                                            }
                                        },
                                        "on the flea",
                                        prices.Count,
                                        total,
                                        totalFee);
                                }
                            }
                            catch (Exception ex)
                            {
                                Utils.SendError(ex.ToString());
                                Plugin.LogSource.LogWarning(ex.ToString());
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Utils.SendError(ex.ToString());
                Plugin.LogSource.LogWarning(ex.ToString());
            }
        }

        private static void DoFleaOffer(Item item, IEftSession session, int price)
        {
            try
            {
                var list = new List<BarterTemplate>
                {
                    new()
                    {
                        _tpl = (string)CurrencyUtil.GetCurrencyId(ECurrencyType.RUB),
                        count = price,
                        onlyFunctional = true
                    }
                };
                session.RagfairAddOffer(false, [item.Id], [.. list], new Callback(PlaySellSound));
            }
            catch (Exception ex)
            {
                Utils.SendError(ex.ToString());
                Plugin.LogSource.LogWarning(ex.ToString());
            }
        }

        private static SupplyData GetTraderSupplyData()
        {
            var trader = traders?.FirstOrDefault();
            if (trader == null)
            {
                Utils.SendError("No trader found in the collection.");
                return null;
            }

            var supplyData = trader._supplyData;
            if (supplyData == null)
            {
                Utils.SendError("_supplyData is null for the provided trader instance.");
            }

            return supplyData;
        }

        // Returns Trader with best offer of null if unsellable
        internal static Trader SelectTrader(Item item)
        {
            if (traders == null)
            {
                ForceReloadTraders();
            }

            var supplyData = GetTraderSupplyData();
            if (supplyData == null)
            {
                ForceReloadTraders();
            }

            Trader best = null;
            int bestOffer = 0;

            if (traders == null)
            {
                Utils.SendError("Traders is null even after force reloading. Cannot sell.");
                return null;
            }

            foreach (var trader in traders)
            {
                if (Plugin.TradersBlacklist.Contains(trader.LocalizedName)) continue;

                var price = trader.GetUserItemPrice(item);
                if (price == null) continue;

                if (best == null)
                {
                    best = trader;
                    bestOffer = price.Value.Amount;
                    continue;
                }

                if (bestOffer < price.Value.Amount)
                {
                    best = trader;
                    bestOffer = price.Value.Amount;
                }
            }

            return best;
        }

        private static void PlaySellSound(IResult result)
        {
            if (result.Succeed)
            {
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.TradeOperationComplete);
            }
        }

        private static void ForceReloadTraders()
        {
            var session = GetSession();
            if (session == null)
            {
                traders = null;
                return;
            }

            traders = session.Traders
                .Where(trader => !trader.Settings.AvailableInRaid)
                .ToArray();
        }

        private static bool IsMultiSelectActive()
        {
            return Plugin.EnableUIFixesIntegration && MultiSelect.Count > 0;
        }

        /// <summary>
        /// Returns the items to operate on: multi-selection if UIFixes has multiple items selected and item is in that selection, otherwise just the single item.
        /// </summary>
        internal static List<Item> GetItemsToSell(Item item)
        {
            if (item == null) return new List<Item>();
            if (!Plugin.EnableUIFixesIntegration) return new List<Item> { item };
            if (MultiSelect.Count > 0) return MultiSelect.Items.ToList();
            return new List<Item> { item };
        }

        public static void ConfirmWindow(Action callback, string source, int count, int? totalPrice = null, int? fee = null)
        {
            if (!Plugin.ShowConfirmationDialog) { callback(); return; }
            var itemUiContext = ItemUiContext.Instance;
            if (itemUiContext == null) { callback(); return; }

            string message;
            string baseMsg = count == 1
                ? $"Are you sure you want to sell this item {source}?".Localized()
                : $"Are you sure you want to sell {count} items {source}?".Localized();

            if (totalPrice.HasValue)
            {
                if (fee.HasValue && fee.Value > 0)
                {
                    var profit = totalPrice.Value - fee.Value;
                    message = $"{baseMsg}\n\nListing Fee: {fee.Value:N0} ₽\nNet Profit: {profit:N0} ₽";
                }
                else
                {
                    message = count == 1
                        ? $"Are you sure you want to sell this item {source} for {totalPrice.Value:N0} ₽?".Localized()
                        : $"Are you sure you want to sell {count} items {source} for {totalPrice.Value:N0} ₽ total?".Localized();
                }
            }
            else
            {
                message = baseMsg;
            }

            itemUiContext.ShowMessageWindow(message, callback, () => { }, null, 0f, false, TextAlignmentOptions.Center);
        }

    }
}
