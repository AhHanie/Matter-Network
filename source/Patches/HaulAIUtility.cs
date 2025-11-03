using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_HaulAIUtility
    {
        [HarmonyPatch(typeof(HaulAIUtility), "IsInHaulableInventory")]
        public static class IsInHaulableInventory
        {
            public static bool Prefix(Thing thing, ref bool __result)
            {
                if (thing.MapHeld == null)
                {
                    return true;
                }
                NetworksMapComponent mapComp = thing.MapHeld.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(thing, out DataNetwork network))
                {
                    return true;
                }
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(HaulAIUtility), "FindFixedIngredientCount")]
        public static class FindFixedIngredientCount
        {
            public static void Postfix(Pawn pawn, ThingDef def, int maxCount, ref List<Thing> __result)
            {
                if (!__result.NullOrEmpty())
                {
                    return;
                }
                List<Thing> chosenThings = __result;
                ThingRequest thingRequest = ThingRequest.ForDef(def);
                int countFound = 0;
                Log.Message($"HMM: {def.defName}");
                NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
                foreach (DataNetwork network in mapComp.Networks)
                {
                    if (ThingListProcessor(network.StoredItems.ToList()))
                    {
                        Log.Message($"CNT: {chosenThings.Count}");
                        return;
                    }
                }

                Log.Message($"CNT: {chosenThings.Count}");

                bool ThingListProcessor(List<Thing> things)
                {
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing thing = things[i];
                        if (thingRequest.Accepts(thing) && !chosenThings.Contains(thing) && !thing.IsForbidden(pawn) && pawn.CanReserve(thing) && pawn.Map.reachability.CanReach(pawn.Position, thing, PathEndMode.Touch, TraverseMode.PassDoors))
                        {
                            chosenThings.Add(thing);
                            countFound += thing.stackCount;
                            if (countFound >= maxCount)
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                }
            }
        }
    }
}
