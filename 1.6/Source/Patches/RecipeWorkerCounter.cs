using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_RecipeWorkerCounter
    {
        [HarmonyPatch(typeof(RecipeWorkerCounter), nameof(RecipeWorkerCounter.CountProducts))]
        public static class CountProducts
        {
            public static void Postfix(RecipeWorkerCounter __instance, Bill_Production bill, ref int __result)
            {
                ThingDefCountClass product = __instance.recipe.products[0];
                ThingDef thingDef = product.thingDef;
                if (product.thingDef.CountAsResource && !bill.includeEquipped && (bill.includeTainted || !product.thingDef.IsApparel || !product.thingDef.apparel.careIfWornByCorpse) && bill.GetIncludeSlotGroup() == null && bill.hpRange.min == 0f && bill.hpRange.max == 1f && bill.qualityRange.min == QualityCategory.Awful && bill.qualityRange.max == QualityCategory.Legendary && !bill.limitToAllowedStuff)
                {
                    return;
                }

                if (bill.GetIncludeSlotGroup() != null)
                {
                    return;
                }

                int overcount = 0;
                foreach (DataNetwork network in NetworkItemSearchUtility.Networks(bill.Map))
                {
                    int exposureCount = network.NetworkInterfaces.Count(x => x.Spawned);
                    if (network.ActiveController != null && network.ActiveController.Spawned && network.ActiveController.Map == bill.Map)
                    {
                        exposureCount++;
                    }

                    if (exposureCount <= 1)
                    {
                        continue;
                    }

                    foreach (Thing item in network.StoredItems)
                    {
                        if (__instance.CountValidThing(item, bill, thingDef))
                        {
                            overcount += item.stackCount * (exposureCount - 1);
                        }
                    }
                }

                if (overcount > 0)
                {
                    __result -= overcount;
                }
            }
        }
    }
}
