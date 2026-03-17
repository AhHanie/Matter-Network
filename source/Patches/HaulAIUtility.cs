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
                NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
                foreach (DataNetwork network in mapComp.Networks)
                {
                    if (ThingListProcessor(network.StoredItems.ToList()))
                    {
                        return;
                    }
                }

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

        [HarmonyPatch(typeof(HaulAIUtility), "PawnCanAutomaticallyHaulFast")]
        public static class PawnCanAutomaticallyHaulFast
        {
            public static bool Prefix(Pawn p, Thing t, bool forced, ref bool __result)
            {
                NetworksMapComponent mapComp = p.Map.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(t, out DataNetwork _))
                {
                    if (t.MapHeld == null)
                    {
                        Logger.Error($"Item not in network and map is null: {t.def} {t.thingIDNumber} {t.stackCount} {t.Position}");
                    }
                    return true;
                }

                if (!p.CanReserve(t, 1, -1, null, forced))
                {
                    __result = false;
                }
                if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                {
                    __result = false;
                }
                UnfinishedThing unfinishedThing = t as UnfinishedThing;
                if (unfinishedThing != null &&
                    unfinishedThing.BoundBill != null &&
                    unfinishedThing.BoundBill.billStack.FirstShouldDoNow == unfinishedThing.BoundBill)
                {
                    Building building = unfinishedThing.BoundBill.billStack.billGiver as Building;
                    if (building == null || (building.Spawned && building.OccupiedRect().ExpandedBy(1).Contains(unfinishedThing.Position)))
                    {
                        __result = false;
                    }
                }
                using (ProfilerBlock.Scope("CanReach"))
                {
                    if (!p.CanReach(t, PathEndMode.ClosestTouch, p.NormalMaxDanger()))
                    {
                        __result = false;
                    }
                }
                if (t.def.IsNutritionGivingIngestible && t.def.ingestible.HumanEdible && !t.IsSociallyProper(p, forPrisoner: false, animalsCare: true))
                {
                    JobFailReason.Is(HaulAIUtility.ReservedForPrisonersTrans);
                    __result = false;
                }
                if (t.IsBurning())
                {
                    JobFailReason.Is(HaulAIUtility.BurningLowerTrans);
                    __result = false;
                }
                __result = true;

                return false;
            }
        }
    }
}
