using EFT.Trading;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace QuickSell.Patches
{
    internal class TraderInventoryLoadingPatch : ModulePatch
    {
        // Lighthouse Keeper is a special trader and should not be force-refreshed.
        private const string LighthouseKeeperTraderId = "638f541a29ffd1183d187f57";

        //This patch is in charge of preloading trader assortment for price checking
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.GetDeclaredConstructors(typeof(Trader))[0];
        }

        [PatchPostfix]
        private static void Postfix(Trader __instance)
        {
            if (__instance.Id == LighthouseKeeperTraderId)
            {
                return;
            }
            __instance.RefreshAssortment(false, true);
        }
    }
}
