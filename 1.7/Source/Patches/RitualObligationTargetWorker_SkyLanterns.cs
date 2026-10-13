using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_RitualObligationTargetWorker_SkyLanterns
    {
        // Matches the maxPawns the actual fetch job reserves with (JobGiver_TakeCountToInventory.TryGiveJob:
        // pawn.CanReserve(x, 10, toTake)), so this preflight doesn't reject a shared stack that the fetch job
        // could still reserve. The vanilla preflight call omits maxPawns and defaults to 1.
        private const int FetchReservationMaxPawns = 10;

        [HarmonyPatch]
        public static class GetBlockingIssues
        {
            public static MethodBase TargetMethod()
            {
                MethodInfo getBlockingIssues = AccessTools.Method(typeof(RitualObligationTargetWorker_SkyLanterns), nameof(RitualObligationTargetWorker_SkyLanterns.GetBlockingIssues));
                return AccessTools.StateMachineMoveNext(getBlockingIssues);
            }

            // Wraps `target.Map.listerThings.ThingsOfDef(ThingDefOf.WoodLog)` so the returned candidate list
            // also includes extractable network-stored wood. The map comes from the iterator's captured
            // `target` parameter, since ListerThings itself has no map reference.
            [HarmonyPostfix]
            [HarmonyInfix(typeof(ListerThings), nameof(ListerThings.ThingsOfDef), typeof(ThingDef))]
            public static List<Thing> AddNetworkWood(List<Thing> __result, ThingDef def, [HarmonyOuter] [HarmonyArgument("target", ArgumentMode.Captured)] TargetInfo capturedTarget)
            {
                return def == ThingDefOf.WoodLog ? GetWoodCandidates(capturedTarget.Map, __result) : __result;
            }

            // Redirects the participant CanReserveAndReach check to reserve with FetchReservationMaxPawns
            // instead of the vanilla default of 1 (see the constant's comment above).
            [HarmonyPrefix]
            [HarmonyInfix(typeof(ReservationUtility), nameof(ReservationUtility.CanReserveAndReach), typeof(Pawn), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(Danger), typeof(int), typeof(int), typeof(ReservationLayerDef), typeof(bool))]
            public static void ShareWoodReservation(ref int maxPawns)
            {
                maxPawns = FetchReservationMaxPawns;
            }

            private static List<Thing> GetWoodCandidates(Map map, List<Thing> vanillaCandidates)
            {
                List<Thing> candidates = new List<Thing>(vanillaCandidates);
                HashSet<Thing> seen = null;

                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (item.def != ThingDefOf.WoodLog || item.stackCount <= 0 || item.Destroyed)
                    {
                        continue;
                    }

                    if (seen == null)
                    {
                        seen = new HashSet<Thing>(candidates);
                    }

                    if (seen.Add(item))
                    {
                        candidates.Add(item);
                    }
                }

                return candidates;
            }
        }
    }
}
