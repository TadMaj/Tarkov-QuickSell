using BepInEx;
using BepInEx.Logging;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickSell.Patches;

namespace QuickSell
{

    [BepInPlugin("QuickSell.UniqueGUID", "QuickSell", "1.0.2")]
    public class Plugin : BaseUnityPlugin
    {

        public static bool EnableQuickSellFlea = true;
        public static bool EnableQuickSellTraders = true;
        public static bool ShowConfirmationDialog = true;
        public static string[] TradersBlacklist = new string[] { };

        public static double AvgPricePercent = 100;

        public static string TraderSellKey = "B";
        public static string FleaSellKey = "N";

        public static bool IgnoreFleaCapacity = false;

        public static ManualLogSource LogSource;

        private void Awake()
        {
            LogSource = Logger;
            new ContextMenuPatch().Enable();
            new TraderInventoryLoadingPatch().Enable();
        }
    }
}
