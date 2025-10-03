using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_WorkGiver_CookFillHopper
    {
        [HarmonyPatch(typeof(WorkGiver_CookFillHopper), nameof(WorkGiver_CookFillHopper.HopperFillFoodJob))]
        public static class HopperFillFoodJob
        {
            public static void Postfix(Pawn pawn, ISlotGroupParent hopperSgp, bool forced, ref Job __result)
            {
                if (__result != null)
                {
                    return;
                }

                Building hopper = hopperSgp as Building;
                if (hopper == null || !pawn.CanReserveAndReach(hopper.Position, PathEndMode.Touch, pawn.NormalMaxDanger()))
                {
                    return;
                }

                Thing firstItem = hopper.Position.GetFirstItem(hopper.Map);
                ThingDef thingDef = null;
                if (firstItem != null)
                {
                    if (!Building_NutrientPasteDispenser.IsAcceptableFeedstock(firstItem.def))
                    {
                        return;
                    }

                    thingDef = firstItem.def;
                }

                Thing networkFood = NetworkItemSearchUtility.FindClosestReachableThing(pawn, item => IsValidHopperFood(pawn, hopper, hopperSgp, item, thingDef, forced), out _);
                if (networkFood != null)
                {
                    __result = HaulAIUtility.HaulToCellStorageJob(pawn, networkFood, hopper.Position, fitInStoreCell: true);
                }
            }

            private static bool IsValidHopperFood(Pawn pawn, Building hopper, ISlotGroupParent hopperSgp, Thing thing, ThingDef requiredDef, bool forced)
            {
                if (requiredDef != null ? thing.def != requiredDef : !thing.def.IsNutritionGivingIngestible)
                {
                    return false;
                }

                if (thing.def.ingestible == null)
                {
                    return false;
                }

                if (requiredDef == null && thing.def.ingestible.preferability != FoodPreferability.RawBad && thing.def.ingestible.preferability != FoodPreferability.RawTasty)
                {
                    return false;
                }

                if (!HaulAIUtility.PawnCanAutomaticallyHaul(pawn, thing, forced))
                {
                    return false;
                }

                if (!pawn.Map.haulDestinationManager.SlotGroupAt(hopper.Position).Settings.AllowedToAccept(thing))
                {
                    return false;
                }

                return (int)StoreUtility.CurrentStoragePriorityOf(thing, forced) < (int)hopperSgp.GetSlotGroup().Settings.Priority;
            }
        }
    }
}
