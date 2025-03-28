using EFT;
using EFT.UI;
using UnityEngine;
using EFT.InventoryLogic;
using HarmonyLib;
using QuickSell;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT.UI.Ragfair;
using TMPro;
using SPT.Reflection.Utils;
using UIFixesInterop;
using EFT.Hideout;
using JetBrains.Annotations;

namespace QuickSell.Patches
{
    // This is the main patch handling most of the logic

    internal class ContextMenuPatch : ModulePatch // all patches must inherit ModulePatch
    {
        private static TraderClass[] traders = null;

        private static TarkovApplication GetApp()
        {
            return ClientAppUtils.GetMainApp();
        }

        private static MainMenuControllerClass GetMainMenu()
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

            var mainMenuController = fieldInfo.GetValue(app) as MainMenuControllerClass;
            if (mainMenuController == null)
            {
                Utils.SendError("mainMenuControllerClass is null");
            }

            return mainMenuController;
        }


        private static ISession GetSession()
        {
            var mainMenuController = GetMainMenu();
            if (mainMenuController == null)
            {
                Utils.SendError("MainMenuControllerClass is null");
                return null;
            }

            var session = mainMenuController.iSession;
            if (session == null)
            {
                Utils.SendError("iSession is null");
            }

            return session;
        }

        protected override MethodBase GetTargetMethod()
        {
            // Retrieve method info for method_0 on SimpleContextMenu
            var methodInfo = typeof(SimpleContextMenu).GetMethod(nameof(SimpleContextMenu.method_0));
            if (methodInfo == null)
            {
                Utils.SendError("SimpleContextMenu.method_0 not found");
                return null;
            }

            // Create a generic method with the specified type parameter
            var targetMethod = methodInfo.MakeGenericMethod(typeof(EItemInfoButton));

            return targetMethod;
        }

        [CanBeNull]
        private static ITraderInteractions GetTraderInteractions(TraderClass bestTrader)
        {
            ITraderInteractions interactions = null;

            var fieldInfo = typeof(TraderClass)
                .GetField("iTraderInteractions", BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (fieldInfo != null)
            {
                interactions = fieldInfo.GetValue(bestTrader) as ITraderInteractions;
                if (interactions == null)
                {
                    Utils.SendError("iTraderInteractions is null for the provided trader instance");
                }
            }
            
            return interactions;
        }


        [PatchPrefix]
        private static void Prefix(ItemInfoInteractionsAbstractClass<EItemInfoButton> contextInteractions,
            IReadOnlyDictionary<EItemInfoButton, string> names, Item item)
        {
            if (contextInteractions is not GClass3466 gclass) return;

            if (item == null)
            {
                Utils.SendError("No item is selected");
                return;
            }

            ItemContextAbstractClass itemContext = Traverse.Create(contextInteractions)
                .Field<ItemContextAbstractClass>("itemContextAbstractClass").Value;
            if (itemContext.ViewType != EItemViewType.Inventory) return;
            if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is not HideoutGameWorld) return;
            if (Singleton<MenuUI>.Instance.HideoutAreaTransferItemsScreen.isActiveAndEnabled) return;

            if (item.GetAllParentItems().Any(x => x is InventoryEquipment)) return;

            if (item.Parent.Container.ParentItem.TemplateId == "55d7217a4bdc2d86028b456d") return;

            Dictionary<string, DynamicInteractionClass> dynamicInteractions = Traverse.Create(contextInteractions)
                .Field<Dictionary<string, DynamicInteractionClass>>("dictionary_0").Value;
            if (dynamicInteractions is null) return;

            if (Plugin.EnableQuickSellFlea)
                dynamicInteractions["QuickSell (Flea)"] = new("QuickSell (Flea)", "QuickSell (Flea)",
                    () => ConfirmWindow((i) => UIFixesHandler((i) => SellFlea(i), i), "on the flea", item),
                    CacheResourcesPopAbstractClass.Pop<Sprite>("Characteristics/Icons/UnloadAmmo"));
            if (Plugin.EnableQuickSellTraders)
                dynamicInteractions["QuickSell (Trader)"] = new("QuickSell (Trader)", "QuickSell (Trader)",
                    () => ConfirmWindow((i) => UIFixesHandler((i) => SellTrader(i), i), "to the traders", item),
                    CacheResourcesPopAbstractClass.Pop<Sprite>("Characteristics/Icons/UnloadAmmo"));
        }

        public static void ConfirmWindow(Action<Item> callback, string source, Item item)
        {
            if (Plugin.ShowConfirmationDialog)
            {
                ItemUiContext.Instance.ShowMessageWindow(
                    $"Are you sure you want to sell this item {source}".Localized(),
                    () => callback(item), () => { }, null, 0f, false, TextAlignmentOptions.Center);
            }
            else callback(item);
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

                ITraderInteractions interactions = bestTrader.iTraderInteractions;

                if (interactions is null)
                {
                    Utils.SendError("Failed to get trader interactions");
                    return;
                }
                
                interactions.ConfirmSell(
                    bestTrader.Id,
                    [new EFT.Trading.TradingItemReference { Item = item, Count = item.StackObjectsCount }], 
                    price, 
                    PlaySellSound
                );
            }
            catch (Exception ex)
            {
                Utils.SendError(ex.ToString());
                Plugin.LogSource.LogWarning(ex.ToString());
            }
        }

        public static void SellFlea(Item item)
        {
            try
            {
                var tradingScreen = Singleton<MenuUI>.Instance.TradingScreen;
                if (!tradingScreen) Utils.SendError("Could not Load tradingScreen");

                var session = GetSession();
                if (session == null) Utils.SendError("Could not Load session");

                var inventoryControllerClass = GetMainMenu().InventoryController;
                if (inventoryControllerClass == null) Utils.SendError("Counldnt Load inventoryControllerClass");

                var ragFairClass = GetSession().RagFair;
                if (!ragFairClass.Available)
                {
                    Utils.SendError("Flea market is not available");
                    return;
                }

                var lootItemClass = new CompoundItem[] { inventoryControllerClass.Inventory.Stash };
                var helper = new RagfairOfferSellHelperClass(lootItemClass[0].Grids[0], inventoryControllerClass);
                if (!helper.HighlightedAtRagfair(item))
                {
                    Utils.SendError("Item cannot be sold on the flea");
                    return;
                }


                var max_offers = ragFairClass.GetMaxOffersCount(ragFairClass.MyRating);
                var current_offers = ragFairClass.MyOffersCount;

                if (!Plugin.IgnoreFleaCapacity && current_offers == max_offers)
                {
                    Utils.SendError("You have reached the maximum number of offers");
                    return;
                }

                var fleaAction = FleaCallbackFactory(item, ragFairClass, session);
                ragFairClass.GetMarketPrices(item.TemplateId, fleaAction);
            }
            catch (Exception ex)
            {
                Utils.SendError(ex.ToString());
                Plugin.LogSource.LogWarning(ex.ToString());
            }
        }

        public static Action<ItemMarketPrices> FleaCallbackFactory(Item item, RagFairClass ragFair, ISession session)
        {
            void res(ItemMarketPrices result)
            {
                try
                {
                    List<GClass2102> list =
                    [
                        new()
                        {
                            _tpl = GClass2934.GetCurrencyId(ECurrencyType.RUB),
                            count = Math.Ceiling(result.avg / 100.0 * Plugin.AvgPricePercent), onlyFunctional = true
                        },
                    ];


                    session.RagfairAddOffer(false, [item.Id], [.. list], new Callback(PlaySellSound));
                }
                catch (Exception ex)
                {
                    Utils.SendError(ex.ToString());
                    Plugin.LogSource.LogWarning(ex.ToString());
                }
            }

            return res;
        }

        private static SupplyData GetTraderSupplyData()
        {
            SupplyData supplyData = null;

            var trader = traders.First();
            if (trader == null)
            {
                Utils.SendError("No trader found in the collection.");
            }
            else
            {
                var fieldInfo = typeof(TraderClass)
                    .GetField("supplyData_0", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fieldInfo != null)
                {
                    supplyData = fieldInfo.GetValue(trader) as SupplyData;
                    if (supplyData == null)
                    {
                        Utils.SendError("supplyData_0 is null for the provided trader instance.");
                    }
                }
            }

            return supplyData;
        }

        // Returns Trader with best offer of null if unsellable
        private static TraderClass SelectTrader(Item item)
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

            TraderClass best = null;
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
            if (result.Succeed) Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.TradeOperationComplete);
        }

        private static void ForceReloadTraders()
        {
            traders = GetSession().Traders
                    /*
                     * Look for return !trader.Settings.AvailableInRaid; in MainMenuControllerClass in dnspy
                     */
                .Where(MainMenuControllerClass.Class1394.class1394_0.method_4)
                .ToArray();
        }

        public static void UIFixesHandler(Action<Item> callback, Item item)
        {
            Utils.SendDebugNotification("Enabled: " + Plugin.EnableUIFixesIntegration);
            Utils.SendDebugNotification("Count : " + MultiSelect.Count);

            if (item == null) return;
            if (!Plugin.EnableUIFixesIntegration)
            {
                callback(item);
                return;
            }

            if (MultiSelect.Count <= 1)
            {
                callback(item);
                return;
            }

            if (!MultiSelect.Items.Contains(item))
            {
                callback(item);
                return;
            }


            MultiSelect.Apply((i) => Utils.SendDebugNotification("Selected item: " + i.Id), ItemUiContext.Instance);

            MultiSelect.Apply((i) => callback(i), ItemUiContext.Instance);
        }
    }
}
