using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class BetterWorkbenchManagementCompat
    {
        private const string PackageId = "falconne.BWM";

        private static readonly Type detourType =
            AccessTools.TypeByName("ImprovedWorkbenches.RecipeWorkerCounter_CountProducts_Detour");
        private static readonly Type extendedBillDataType =
            AccessTools.TypeByName("ImprovedWorkbenches.ExtendedBillData");

        private static readonly MethodInfo countProductsMethod =
            (detourType != null && extendedBillDataType != null)
                ? AccessTools.Method(detourType, "CountProducts", new[]
                    {
                        extendedBillDataType, typeof(ThingDef), typeof(Bill_Production),
                        typeof(RecipeWorkerCounter), typeof(bool)
                    })
                : null;

        private static bool warnedMissingApi;

        private static bool IsAvailable() => countProductsMethod != null;

        [HarmonyPatch]
        public static class Patch_CountProducts
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                bool available = IsAvailable();
                if (ModsConfig.IsActive(PackageId) && !available && !warnedMissingApi)
                {
                    warnedMissingApi = true;
                    Logger.Warning("[Better Workbench Management] Compatibility disabled because expected BWM members were not found.");
                }

                return available;
            }

            public static MethodBase TargetMethod() => countProductsMethod;

            public static void Postfix(
                ThingDef productThingDef, Bill_Production bill, RecipeWorkerCounter recipeWorkerCounter,
                bool defaultProduct, ref int __result)
            {
                if (defaultProduct || productThingDef.CountAsResource || bill.GetIncludeSlotGroup() != null)
                {
                    return;
                }

                foreach (DataNetwork network in NetworkItemSearchUtility.Networks(bill.Map))
                {
                    foreach (Thing item in network.StoredItems)
                    {
                        if (item is MinifiedThing minified)
                        {
                            if (recipeWorkerCounter.CountValidThing(minified.InnerThing, bill, productThingDef))
                            {
                                __result += minified.stackCount * minified.InnerThing.stackCount;
                            }
                        }
                        else if (recipeWorkerCounter.CountValidThing(item, bill, productThingDef))
                        {
                            __result += item.stackCount;
                        }
                    }
                }
            }
        }
    }
}
