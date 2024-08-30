using EFT.Communications;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickSell.Patches
{
    internal class TraderInventoryLoadingPatch : ModulePatch
    {
        //This patch is in charge of preloading trader assortment for price checking
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.GetDeclaredConstructors(typeof(TraderClass))[0];
        }

        [PatchPostfix]
        private static void Postfix(TraderClass __instance)
        {
            if (__instance.Id == "638f541a29ffd1183d187f57")
            {
                return;
            }
            __instance.RefreshAssortment(false, true);
        }
    }
}
