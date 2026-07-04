using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_WorkGiver_HaulToSubcoreScanner
    {
        [HarmonyPatch(typeof(WorkGiver_HaulToSubcoreScanner), nameof(WorkGiver_HaulToSubcoreScanner.HasJobOnThing))]
        public static class HasJobOnThing
        {
            public static void Postfix(Pawn pawn, Thing t, bool forced, ref bool __result)
            {
                if (__result) return;
                if (!CanTryLoadScannerIngredient(pawn, t, forced, out Building_SubcoreScanner scanner)) return;

                if (FindNetworkIngredient(pawn, scanner, forced).Thing != null)
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(WorkGiver_HaulToSubcoreScanner), nameof(WorkGiver_HaulToSubcoreScanner.JobOnThing))]
        public static class JobOnThing
        {
            public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
            {
                if (__result != null) return;
                if (!CanTryLoadScannerIngredient(pawn, t, forced, out Building_SubcoreScanner scanner)) return;

                ThingCount thingCount = FindNetworkIngredient(pawn, scanner, forced);
                if (thingCount.Thing == null) return;

                Job job = HaulAIUtility.HaulToContainerJob(pawn, thingCount.Thing, t);
                job.count = Mathf.Min(job.count, thingCount.Count);
                __result = job;
            }
        }

        private static bool CanTryLoadScannerIngredient(Pawn pawn, Thing t, bool forced, out Building_SubcoreScanner scanner)
        {
            scanner = null;
            if (!ModsConfig.BiotechActive) return false;
            if (pawn?.Map == null) return false;
            if (!(t is Building_SubcoreScanner candidate)) return false;
            if (candidate.State != SubcoreScannerState.WaitingForIngredients) return false;
            if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;
            if (pawn.Map.designationManager.DesignationOn(t, DesignationDefOf.Deconstruct) != null) return false;
            if (t.IsBurning()) return false;

            scanner = candidate;
            return true;
        }

        private static ThingCount FindNetworkIngredient(Pawn pawn, Building_SubcoreScanner scanner, bool forced)
        {
            Thing bestThing = null;
            int bestCount = 0;
            float bestDistSq = float.MaxValue;

            foreach (DataNetwork network in NetworkItemSearchUtility.Networks(pawn.Map))
            {
                float distSq = NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn.Position, pawn, network);
                if (distSq == float.MaxValue) continue;

                foreach (Thing item in network.StoredItems)
                {
                    if (!IsValidNetworkIngredient(pawn, scanner, item, forced)) continue;

                    int needed = scanner.GetRequiredCountOf(item.def);
                    int reservable = NetworkItemSearchUtility.GetReservableNetworkStackCount(pawn, item, 1, needed);
                    int count = Mathf.Min(item.stackCount, Mathf.Min(reservable, needed));
                    if (count <= 0) continue;

                    if (bestThing == null ||
                        distSq < bestDistSq ||
                        (distSq == bestDistSq && item.thingIDNumber < bestThing.thingIDNumber))
                    {
                        bestThing = item;
                        bestCount = count;
                        bestDistSq = distSq;
                    }
                }
            }

            return bestThing == null ? default(ThingCount) : new ThingCount(bestThing, bestCount);
        }

        private static bool IsValidNetworkIngredient(Pawn pawn, Building_SubcoreScanner scanner, Thing item, bool forced)
        {
            if (item == null || item.Destroyed || item.stackCount <= 0) return false;
            if (!scanner.CanAcceptIngredient(item)) return false;
            if (scanner.GetRequiredCountOf(item.def) <= 0) return false;
            return NetworkItemSearchUtility.PawnCanUseNetworkItemForHaul(pawn, item, forced);
        }
    }
}
