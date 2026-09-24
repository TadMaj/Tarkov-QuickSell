using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using EFT;
using System;
using QuickSell.Patches;
using System.IO;
using Newtonsoft.Json.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace QuickSell
{

    [BepInPlugin("QuickSell.UniqueGUID", "QuickSell", "2.0.0")]
    [BepInDependency("com.tyfon.uifixes", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        private static readonly System.Version UIFixesMinimumVersion = new(6, 0);
        private const string UIFixesPluginId = "com.tyfon.uifixes";

        public static bool EnableQuickSellFlea = true;
        public static bool EnableQuickSellTraders = true;

        public static bool ShowConfirmationDialog = true;
        public static string[] TradersBlacklist = [];

        public static double AvgPricePercent = 100;

        public static bool IgnoreFleaCapacity = false;

        public static bool DisableKeybinds = false;

        public static bool EnableUIFixesIntegration = false;
        public static bool UIFixesDetected =>
            Chainloader.PluginInfos.TryGetValue(UIFixesPluginId, out var pluginInfo) &&
            pluginInfo?.Metadata?.Version >= UIFixesMinimumVersion;

        internal static ConfigEntry<KeyboardShortcut> KeybindTraders;
        internal static ConfigEntry<KeyboardShortcut> KeybindFlea;

        public static ManualLogSource LogSource;

        private void Awake()
        {
            LogSource = Logger;
            new ContextMenuPatch().Enable();
            new TraderInventoryLoadingPatch().Enable();

            try
            {
                var modPath = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                if (!string.IsNullOrEmpty(modPath))
                {
                    LoadConfig(modPath.Replace('\\', '/'));
                }
                else
                {
                    Logger.LogWarning("Could not determine plugin path, using defaults");
                    EnableUIFixesIntegration = UIFixesDetected;
                }

                Logger.LogInfo(UIFixesDetected
                    ? $"UIFixes detected, multi-select integration {(EnableUIFixesIntegration ? "enabled" : "disabled in config")}"
                    : "UIFixes not found, multi-select integration disabled");

                if (!DisableKeybinds)
                {
                    KeybindFlea = Config.Bind("QuickSell", "SellFlea", new KeyboardShortcut(KeyCode.N), "Quicksell on the Flea");
                    KeybindTraders = Config.Bind("QuickSell", "SellTraders", new KeyboardShortcut(KeyCode.M), "QuickSell to Traders");
                    KeybindPatches.Enable();
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }

        }

        private void LoadConfig(string path)
        {
            var configPath = Path.Combine(path, "config.json");
            if (!File.Exists(configPath))
            {
                Logger.LogWarning("config.json not found, using defaults");
                EnableUIFixesIntegration = UIFixesDetected;
                return;
            }

            var config = JObject.Parse(File.ReadAllText(configPath));

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

            if (config.ContainsKey("DisableKeybinds"))
            {
                DisableKeybinds = (bool)config["DisableKeybinds"];
            }

            if (config.ContainsKey("EnableUIFixesIntegration"))
            {
                EnableUIFixesIntegration = (bool)config["EnableUIFixesIntegration"];
            }
            else
            {
                EnableUIFixesIntegration = UIFixesDetected;
            }
        }
    }
}
