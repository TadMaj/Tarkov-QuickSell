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
        private static MainMenuController GetMainMenu()
        {
           return (MainMenuController) typeof(TarkovApplication).GetField("mainMenuController", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(GetApp());
        }
        private static ISession GetSession()
        {
            return (ISession)typeof(MainMenuController).GetField("iSession", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(GetMainMenu());
        }

        protected override MethodBase GetTargetMethod()
        {
            return typeof(SimpleContextMenu).GetMethod(nameof(SimpleContextMenu.method_0)).MakeGenericMethod(typeof(EItemInfoButton));
        }

        [PatchPrefix]
        private static void Prefix(ItemInfoInteractionsAbstractClass<EItemInfoButton> contextInteractions, IReadOnlyDictionary<EItemInfoButton, string> names, Item item)
        {
            if (contextInteractions is not GClass3402 gclass) return;

            if (item == null)
            {
                Utils.SendError("No item is selected");
                return;
            }
            ItemContextAbstractClass itemContext = Traverse.Create(contextInteractions).Field<ItemContextAbstractClass>("itemContextAbstractClass").Value;
            if (itemContext.ViewType != EItemViewType.Inventory) return;
            if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is not HideoutGameWorld) return;
            if (Singleton<MenuUI>.Instance.HideoutAreaTransferItemsScreen.isActiveAndEnabled) return;

            if (item.GetAllParentItems().Any(x => x is InventoryEquipment)) return;
            
            if (item.Parent.Container.ParentItem.TemplateId == "55d7217a4bdc2d86028b456d") return;

            Dictionary<string, DynamicInteractionClass> dynamicInteractions = Traverse.Create(contextInteractions).Field<Dictionary<string, DynamicInteractionClass>>("dictionary_0").Value;
            if (dynamicInteractions is null) return;

            if (Plugin.EnableQuickSellFlea) dynamicInteractions["QuickSell (Flea)"] = new("QuickSell (Flea)", "QuickSell (Flea)", () => ConfirmWindow((i) => UIFixesHandler((i) => SellFlea(i), i), "on the flea", item), CacheResourcesPopAbstractClass.Pop<Sprite>("Characteristics/Icons/UnloadAmmo"));
            if (Plugin.EnableQuickSellTraders) dynamicInteractions["QuickSell (Trader)"] = new("QuickSell (Trader)", "QuickSell (Trader)", () => ConfirmWindow((i) => UIFixesHandler((i) => SellTrader(i), i), "to the traders", item), CacheResourcesPopAbstractClass.Pop<Sprite>("Characteristics/Icons/UnloadAmmo"));
        }

        public static void ConfirmWindow(Action<Item> callback, string source, Item item)
        {
            if (Plugin.ShowConfirmationDialog)
            {
                ItemUiContext.Instance.ShowMessageWindow(string.Format("Are you sure you want to sell this item {0}", source).Localized(null), () => callback(item), () => { }, null, 0f, false, TextAlignmentOptions.Center);
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
                
                Utils.SendNotification(string.Format("Profit: {0}", price));

                ITraderInteractions interactions = (ITraderInteractions) typeof(TraderClass).GetField("iTraderInteractions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bestTrader);
                interactions.ConfirmSell(bestTrader.Id, [new EFT.Trading.TradingItemReference { Item = item, Count = item.StackObjectsCount }], price, new Callback(PlaySellSound));

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
                if (tradingScreen == null) Utils.SendError("Counldnt Load tradingScreen");

                //RagfairScreen flea = (RagfairScreen)typeof(TradingScreen).GetField("_ragfairScreen", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tradingScreen);
                //if (flea == null) Utils.SendError("Counldnt Load flea");

                //flea.method_4();

                var session = GetSession();
                if (session == null) Utils.SendError("Counldnt Load session");

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

            } catch (Exception ex)
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
                    List<GClass2059> list =
                    [
                        new GClass2059 { _tpl = GClass2867.GetCurrencyId(ECurrencyType.RUB), count = Math.Ceiling(result.avg/100.0*Plugin.AvgPricePercent), onlyFunctional = true },
                    ];

                    

                    session.RagfairAddOffer(false, [item.Id], [.. list], new Callback(FleaSellerFixer));

                }
                catch (Exception ex)
                {
                    Utils.SendError(ex.ToString());
                    Plugin.LogSource.LogWarning(ex.ToString());
                }

            }

            return res;
        }

        // Returns Trader with best offer of null if unsellable
        private static TraderClass SelectTrader(Item item)
        {
            if (traders == null)
            {
                forceReloadTraders();

            }

            var supplyData_0 = (SupplyData)typeof(TraderClass).GetField("supplyData_0", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(traders.First());
            if (supplyData_0 == null)
            {
                forceReloadTraders();
            }

            TraderClass best = null;
            int bestOffer = 0;
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
                    continue;
                }
            }
            return best;
        }
        private static void PlaySellSound(IResult result)
        {
            if (result.Succeed) Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.TradeOperationComplete);

        }
        //For some reason selling on the flea causes black screens if the flea market screen is still open
        // This is a janky fix for that issue, i am not putting more tim
        private static void FleaSellerFixer(IResult result)
        {
            if (!result.Succeed) return;

            var rotator = GameObject.Find("/Rotator parent");
            if (rotator != null) GameObject.Destroy(rotator);


            PlaySellSound(result);
        }
        private static void forceReloadTraders()
        {        
            traders = GetSession().Traders.Where(new Func<TraderClass, bool>(MainMenuController.Class1378.class1378_0.method_3)).ToArray<TraderClass>();
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


            MultiSelect.Apply((i) => Utils.SendDebugNotification("Selected item: "+ i.Id), ItemUiContext.Instance);
            
            MultiSelect.Apply((i) => callback(i), ItemUiContext.Instance);
        }

    }
}