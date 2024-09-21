using BepInEx;
using BepInEx.Logging;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickSell.Patches;
using System.IO;
using EFT.Communications;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using static GClass1750;
using BepInEx.Configuration;
using UnityEngine;
using UIFixesInterop;

namespace QuickSell
{

    [BepInPlugin("QuickSell.UniqueGUID", "QuickSell", "1.1.0")]
    [BepInDependency("Tyfon.UIFixes", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {

        public static bool EnableQuickSellFlea = true;
        public static bool EnableQuickSellTraders = true;

        public static bool ShowConfirmationDialog = true;
        public static string[] TradersBlacklist = [];

        public static double AvgPricePercent = 100;

        public static string TraderSellKey = "B";
        public static string FleaSellKey = "N";

        public static bool IgnoreFleaCapacity = false;

        public static bool Debug = false;

        public static bool DisableKeybinds = false;

        public static bool EnableUIFixesIntegration = false;

        internal static ConfigEntry<KeyboardShortcut> KeybindTraders;
        internal static ConfigEntry<KeyboardShortcut> KeybindFlea;

        private static GameObject Hook = new GameObject("QuickSell Hook");


        public static ManualLogSource LogSource;

        private void Awake()
        {
            LogSource = Logger;
            new ContextMenuPatch().Enable();
            new TraderInventoryLoadingPatch().Enable();

            try
            {
                var modPath = Path.GetDirectoryName(System.Reflection.Assembly.GetAssembly(typeof(Plugin)).Location);

                modPath.Replace('\\', '/');

                LoadConfig(modPath);


                if (!DisableKeybinds)
                {
                    KeybindFlea = Config.Bind("QuickSell", "SellFlea", new KeyboardShortcut(KeyCode.N), "Quicksell on the Flea");
                    KeybindTraders = Config.Bind("QuickSell", "SellTraders", new KeyboardShortcut(KeyCode.M), "QuickSell to Traders");

                    Hook.AddComponent<ConfigController>();
                    DontDestroyOnLoad(Hook);
                }
               
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
             
        }

        private void LoadConfig(string path)
        {

            var config = JObject.Parse(File.ReadAllText(path + "/config.json"));

            Logger.LogInfo("Loading config");

            if (config.ContainsKey("EnableQuickSellFlea"))
            {
                EnableQuickSellFlea = (bool)config["EnableQuickSellFlea"];
            }

            if (config.ContainsKey("EnableQuickSellTraders"))
            {
                EnableQuickSellTraders = (bool)config["EnableQuickSellTraders"];
            }

            if (config.ContainsKey("ShowConfirmationDialog"))
            {
                ShowConfirmationDialog = (bool)config["ShowConfirmationDialog"];
            }

            if (config.ContainsKey("TradersBlacklist"))
            {
                TradersBlacklist = config["TradersBlacklist"].ToObject<string[]>();
            }

            if (config.ContainsKey("AvgPricePercent"))
            {
                AvgPricePercent = (double)config["AvgPricePercent"];
            }

            if (config.ContainsKey("IgnoreFleaCapacity"))
            {
                IgnoreFleaCapacity = (bool)config["IgnoreFleaCapacity"];
            }

            if (config.ContainsKey("Debug"))
            {
                Debug = (bool)config["Debug"];
            }

            if (config.ContainsKey("DisableKeybinds"))
            {
                DisableKeybinds = (bool)config["DisableKeybinds"];
            }

            if (config.ContainsKey("EnableUIFixesIntegration"))
            {
                EnableUIFixesIntegration = (bool)config["EnableUIFixesIntegration"];
            }
        }
    }
}
