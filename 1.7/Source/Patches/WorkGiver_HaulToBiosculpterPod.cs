using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_WorkGiver_HaulToBiosculpterPod
    {
        [HarmonyPatch(typeof(WorkGiver_HaulToBiosculpterPod), "HasJobOnThing")]
        public static class HasJobOnThing
        {
            public static void Postfix(Pawn pawn, Thing t, bool forced, ref bool __result)
            {
                if (__result) return;
                if (!CanTryLoadNutrition(pawn, t, forced, out CompBiosculpterPod pod)) return;

                if (FindNetworkNutrition(pawn, pod, forced).Thing != null)
                {
                    __result = true;
                }
                else
                {
                    JobFailReason.Is("NoFood".Translate());
                }
            }
        }

        [HarmonyPatch(typeof(WorkGiver_HaulToBiosculpterPod), "JobOnThing")]
        public static class JobOnThing
        {
            public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
            {
                if (__result != null) return;
                if (!CanTryLoadNutrition(pawn, t, forced, out CompBiosculpterPod pod)) return;

                ThingCount thingCount = FindNetworkNutrition(pawn, pod, forced);
                if (thingCount.Thing == null) return;

                Job job = HaulAIUtility.HaulToContainerJob(pawn, thingCount.Thing, t);
                job.count = Mathf.Min(job.count, thingCount.Count);
                __result = job;
            }
        }

        private static bool CanTryLoadNutrition(Pawn pawn, Thing t, bool forced, out CompBiosculpterPod pod)
        {
            pod = null;
            if (!ModLister.IdeologyInstalled) return false;
            if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;
            if (pawn.Map.designationManager.DesignationOn(t, DesignationDefOf.Deconstruct) != null) return false;
            pod = t.TryGetComp<CompBiosculpterPod>();
            if (pod == null || !pod.PowerOn) return false;
            if (pod.State != BiosculpterPodState.LoadingNutrition) return false;
            if (!forced && !pod.autoLoadNutrition) return false;
            if (t.IsBurning()) return false;
            if (pod.RequiredNutritionRemaining <= 0f) return false;
            return true;
        }

        private static ThingCount FindNetworkNutrition(Pawn pawn, CompBiosculpterPod pod, bool forced)
        {
            Thing bestThing = null;
            float bestDistSq = float.MaxValue;

            foreach (DataNetwork network in NetworkItemSearchUtility.Networks(pawn.Map))
            {
                float distSq = NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn.Position, pawn, network);
                if (distSq == float.MaxValue) continue;

                foreach (Thing item in network.StoredItems)
                {
                    if (!IsValidNetworkFood(pawn, pod, item, forced)) continue;

                    if (distSq < bestDistSq || (distSq == bestDistSq && item.thingIDNumber < bestThing.thingIDNumber))
                    {
                        bestDistSq = distSq;
                        bestThing = item;
                    }
                }
            }

            if (bestThing == null) return default(ThingCount);

            float nutritionPerUnit = bestThing.GetStatValue(StatDefOf.Nutrition);
            int needed = Mathf.CeilToInt(pod.RequiredNutritionRemaining / nutritionPerUnit);
            int reservable = NetworkItemSearchUtility.GetReservableNetworkStackCount(pawn, bestThing, 1, needed);
            int count = Mathf.Min(bestThing.stackCount, Mathf.Min(reservable, needed));

            if (count <= 0) return default(ThingCount);
            return new ThingCount(bestThing, count);
        }

        private static bool IsValidNetworkFood(Pawn pawn, CompBiosculpterPod pod, Thing item, bool forced)
        {
            if (item == null || item.Destroyed || item.stackCount <= 0) return false;
            if (!ThingRequestGroup.FoodSourceNotPlantOrTree.Includes(item.def)) return false;
            if (!pod.CanAcceptNutrition(item)) return false;
            if (!NetworkItemSearchUtility.PawnCanUseNetworkItemForHaul(pawn, item, forced)) return false;
            if (item.GetStatValue(StatDefOf.Nutrition) <= 0f) return false;
            return true;
        }
    }
}
