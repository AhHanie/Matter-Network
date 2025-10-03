using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobGiver_DyeHair
    {
        [HarmonyPatch(typeof(JobGiver_DyeHair), "TryGiveJob")]
        public static class TryGiveJob
        {
            public static void Postfix(Pawn pawn, ref Job __result)
            {
                if (!ModsConfig.IdeologyActive || !pawn.Spawned || !pawn.style.nextHairColor.HasValue || pawn.style.nextHairColor == pawn.story.HairColor)
                {
                    return;
                }

                Thing networkDye = NetworkItemSearchUtility.FindClosestReachableThing(pawn, item => item.def == ThingDefOf.Dye && !item.IsForbidden(pawn) && pawn.CanReserve(item, 1, 1), out float networkDistanceSquared);
                if (networkDye == null)
                {
                    return;
                }

                if (__result != null)
                {
                    float currentDistanceSquared = NetworkItemSearchUtility.GetThingDistanceSquared(pawn, __result.targetB.Thing);
                    if (currentDistanceSquared <= networkDistanceSquared)
                    {
                        return;
                    }

                    __result.targetB = networkDye;
                    return;
                }

                Thing station = GenClosest.ClosestThing_Global_Reachable(pawn.Position, pawn.Map, pawn.Map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.StylingStation), PathEndMode.InteractionCell, TraverseParms.For(pawn), 9999f, x => !x.IsForbidden(pawn) && pawn.CanReserve(x));
                if (station == null)
                {
                    return;
                }

                Job job = JobMaker.MakeJob(JobDefOf.DyeHair, station, networkDye);
                job.count = 1;
                __result = job;
            }
        }
    }
}
