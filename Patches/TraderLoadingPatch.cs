using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using QuickSell;
using EFT.Visual;

namespace QuickSell.Patches
{
    internal class TraderLoadingPatch : ModulePatch
    {
        // This Patch forces traders to load when inventory is loaded.
        // This is necessary for the contexts to work on first boot when no traders are loaded.
        protected override MethodBase GetTargetMethod()
        {
            return typeof(MainMenuController).GetMethod(nameof(MainMenuController.ShowScreen)); ;
        }

        [PatchPrefix]
        private static bool Prefix(MainMenuController __instance, EMenuType screen, bool turnOn)
        {
            if (screen == EMenuType.Player)
            {
                //__instance.method_34();
                __instance.method_35();
            }

            return true;

        }

    }
}
