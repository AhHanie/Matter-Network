using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    // Soft compat: Better Workbench Management's additional-output filter counts physical
    // map items, pawn inventories/equipment, and the vanilla resource counter, but never
    // enumerates IHaulSource containers - so a non-resource filtered output stored in the
    // network never counts toward the bill's target. No compile-time reference to BWM's
    // assembly; everything below is resolved via reflection and skipped if BWM isn't loaded
    // or its internal API no longer matches.
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
                // The default product is already counted by RimWorld's base counter
                // (including Matter Network's own duplicate-count correction), a bill
                // restricted to a physical stockpile must keep ignoring the network, and
                // resource defs are already included once via Patch_ResourceCounter.
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
