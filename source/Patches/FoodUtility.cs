using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;
using System.Collections.Generic;

namespace SK_Matter_Network.Patches
{
    public static class Patch_FoodUtility
    {
        [HarmonyPatch(typeof(FoodUtility), "SpawnedFoodSearchInnerScan")]
        public static class SpawnedFoodSearchInnerScan
        {
            public static void Postfix(Pawn eater, IntVec3 root, List<Thing> searchSet, PathEndMode peMode, TraverseParms traverseParams, ref Thing __result, float maxDistance, Predicate<Thing> validator)
            {
                if (searchSet == null)
                {
                    return;
                }
                Pawn pawn = traverseParams.pawn ?? eater;
                NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
                int num = 0;
                int num2 = 0;
                Thing result = null;
                float num3 = 0f;
                float num4 = float.MinValue;
                for (int i = 0; i < searchSet.Count; i++)
                {
                    Thing thing = searchSet[i];
                    if (!mapComp.TryGetItemNetwork(thing, out DataNetwork network))
                    {
                        continue;
                    }
                    num2++;
                    float num5 = (root - thing.Position).LengthManhattan;
                    if (!(num5 > maxDistance))
                    {
                        num3 = FoodUtility.FoodOptimality(eater, thing, FoodUtility.GetFinalIngestibleDef(thing), num5);
                        if (!(num3 < num4) && pawn.Map.reachability.CanReach(root, thing, peMode, traverseParams) && (validator == null || validator(thing)))
                        {
                            result = thing;
                            num4 = num3;
                            num++;
                        }
                    }
                }
                if (result != null)
                {
                    __result = result;
                }
            }
        }
    }
}
