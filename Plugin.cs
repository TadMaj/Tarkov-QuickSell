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

    [BepInPlugin("QuickSell.UniqueGUID", "QuickSell", "1.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        

        public static ManualLogSource LogSource;
        public MainMenuController mmController;

        private void Awake()
        {
            LogSource = Logger;
            new ContextMenuPatch().Enable();
            new TraderLoadingPatch().Enable();
            new TraderInventoryLoadingPatch().Enable();
        }
    }
}
