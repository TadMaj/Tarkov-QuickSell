using EFT;
using EFT.UI;
using EFT.Communications;
using EFT.InventoryLogic;
using HarmonyLib;
using QuickSell;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using JetBrains.Annotations;
using BepInEx.Logging;
using Comfort.Common;
using EFT.UI.Screens;
using System.Threading;
using EFT.UI.Ragfair;
using static System.Collections.Specialized.BitVector32;
using TMPro;
using QuickSell.Patches;
using SPT.Reflection.Utils;
using EFT.UI.WeaponModding;

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
           return (MainMenuController)typeof(TarkovApplication).GetField("mainMenuController", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(GetApp());
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
            if (contextInteractions is not GClass3042) return;

            if (item == null)
            {
                Utils.sendError("No item is selected");
                return;
            }
            ItemContextAbstractClass itemContext = Traverse.Create(contextInteractions).Field<ItemContextAbstractClass>("itemContextAbstractClass").Value;
            if (itemContext.ViewType != EItemViewType.Inventory) return;
            if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is not HideoutGameWorld) return;
            if (Singleton<MenuUI>.Instance.HideoutAreaTransferItemsScreen.isActiveAndEnabled) return;

            if (item.GetAllParentItems().Any(x => x is EquipmentClass)) return;
            
            if (item.Parent.Item.TemplateId == "55d7217a4bdc2d86028b456d") return;

            Dictionary<string, DynamicInteractionClass> dynamicInteractions = Traverse.Create(contextInteractions).Field<Dictionary<string, DynamicInteractionClass>>("dictionary_0").Value;
            if (dynamicInteractions is null) return;

            string itemId = itemContext.Item.Id;
            dynamicInteractions["QuickSell (Flea)"] = new("QuickSell (Flea)", "QuickSell (Flea)", () => confirmWindow(() => sellFlea(itemId, item), "on the flea"), CacheResourcesPopAbstractClass.Pop<Sprite>("Characteristics/Icons/UnloadAmmo"));
            dynamicInteractions["QuickSell (Trader)"] = new("QuickSell (Trader)", "QuickSell (Trader)", () => confirmWindow(() => sellTrader(itemId, item), "to the traders"), CacheResourcesPopAbstractClass.Pop<Sprite>("Characteristics/Icons/UnloadAmmo"));
        }

        private static void confirmWindow(Action callback, string source)
        {
            ItemUiContext.Instance.ShowMessageWindow(string.Format("Are you sure you want to sell this item {0}", source).Localized(null), callback, () => { }, null, 0f, false, TextAlignmentOptions.Center);
        }

        private static void sellTrader(string itemId, Item item)
        {
            try
            {
                var bestTrader = selectTrader(item);

                if (bestTrader == null)
                {
                    Utils.sendError("Item cannot be sold traders");
                    return;
                }

                var price = bestTrader.GetUserItemPrice(item).Value.Amount;
                
                Utils.sendNotification(string.Format("Profit: {0}", price));

                ITraderInteractions interactions = (ITraderInteractions) typeof(TraderClass).GetField("iTraderInteractions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bestTrader);
                interactions.ConfirmSell(bestTrader.Id, [new EFT.Trading.TradingItemReference { Item = item, Count = item.StackObjectsCount }], price, new Callback(PlaySellSound));

            }
            catch (Exception ex)
            {
                Utils.sendError(ex.ToString());
                Plugin.LogSource.LogWarning(ex.ToString());
            }


        }

        private static void sellFlea(string itemId, Item item) 
        {
            try
            {
                var tradingScreen = Singleton<MenuUI>.Instance.TradingScreen;
                if (tradingScreen == null) Utils.sendError("Counldnt Load tradingScreen");

                RagfairScreen flea = (RagfairScreen)typeof(TradingScreen).GetField("_ragfairScreen", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tradingScreen);
                if (flea == null) Utils.sendError("Counldnt Load flea");

                flea.method_2();

                var session = GetSession();
                if (session == null) Utils.sendError("Counldnt Load session");

                var inventoryControllerClass = GetMainMenu().InventoryController;
                if (inventoryControllerClass == null) Utils.sendError("Counldnt Load inventoryControllerClass");

                var lootItemClass = new LootItemClass[] { inventoryControllerClass.Inventory.Stash };
                var helper = new RagfairOfferSellHelperClass(session.Profile, lootItemClass[0].Grids[0]);
                if (!helper.HighlightedAtRagfair(item))
                {
                    Utils.sendError("Item cannot be sold on the flea");
                    return;
                }

                var ragFairClass = GetSession().RagFair;

                var max_offers = ragFairClass.GetMaxOffersCount(ragFairClass.MyRating);
                var current_offers = ragFairClass.MyOffersCount;

                if (current_offers == max_offers)
                {
                    Utils.sendError("You have reached the maximum number of offers");
                    return;
                }

                var fleaAction = FleaCallbackFactory(item, ragFairClass, helper);
                ragFairClass.GetMarketPrices(item.TemplateId, fleaAction);



            } catch (Exception ex)
            {
                Utils.sendError(ex.ToString());
                Plugin.LogSource.LogWarning(ex.ToString());
            }
        }
        
        public static Action<ItemMarketPrices> FleaCallbackFactory(Item item, RagFairClass ragFair, RagfairOfferSellHelperClass helper)
        {
            Action<ItemMarketPrices> res = null;

            res = (ItemMarketPrices result) => {
                try
                {
                    List<GClass1859> list = new List<GClass1859>();

                    list.Add(new GClass1859 { _tpl = GClass2531.GetCurrencyId(ECurrencyType.RUB), count = result.avg, onlyFunctional = true });

                    ragFair.AddOffer(false, [item.Id], [.. list], new Action(PlaySellSound));
                }
                catch (Exception ex)
                {
                    Utils.sendError(ex.ToString());
                    Plugin.LogSource.LogWarning(ex.ToString());
                }

            };

            return res;
        }

        // Returns Trader with best offer of null if unsellable
        private static TraderClass selectTrader(Item item)
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
        private static void PlaySellSound()
        {
            Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.TradeOperationComplete);
        }

        private static void forceReloadTraders()
        {        
            traders = GetSession().Traders.Where(new Func<TraderClass, bool>(MainMenuController.Class1271.class1271_0.method_4)).ToArray<TraderClass>();
        }

    }
}