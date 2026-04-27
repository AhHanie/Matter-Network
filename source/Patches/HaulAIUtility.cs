using HarmonyLib;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_HaulAIUtility
    {
        public static bool InTryOpportunisticJob;

        [HarmonyPatch(typeof(Pawn_JobTracker), "TryOpportunisticJob")]
        public static class TryOpportunisticJob
        {
            public static void Prefix()
            {
                InTryOpportunisticJob = true;
            }

            public static void Postfix()
            {
                InTryOpportunisticJob = false;
            }
        }

        [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast))]
        public static class PawnCanAutomaticallyHaulFast
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(Pawn p, Thing t, ref bool __result)
            {
                if (!InTryOpportunisticJob)
                {
                    return true;
                }

                if (t.MapHeld == null)
                {
                    LogNullMapHeldItem(p, t);

                    if (t.stackCount == 0 && !t.Destroyed)
                    {
                        Logger.Warning($"Destroying zero-stack mapless item {t.def?.defName ?? "nullDef"}{t.thingIDNumber}.");
                        t.Destroy(DestroyMode.Vanish);
                    }

                    __result = false;
                    return false;
                }

                NetworksMapComponent mapComp = p.Map.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(t, out _))
                {
                    __result = false;
                    return false;
                }

                return true;
            }

            private static void LogNullMapHeldItem(Pawn pawn, Thing item)
            {
                Logger.Error(
                    "PawnCanAutomaticallyHaulFast saw an item with null MapHeld during TryOpportunisticJob.\n" +
                    $"Pawn: {DescribePawn(pawn)}\n" +
                    $"Item: {Patch_Pawn_JobTracker.DescribeThingForDebug(item, pawn)}\n" +
                    Patch_Pawn_JobTracker.GetLastStartedJobsReport());
            }

            private static string DescribePawn(Pawn pawn)
            {
                if (pawn == null)
                {
                    return "null";
                }

                return $"{pawn.LabelCap} def={pawn.def?.defName ?? "nullDef"} id={pawn.thingIDNumber} spawned={pawn.Spawned} position={pawn.Position} mapHeld={(pawn.MapHeld == null ? "null" : $"index={pawn.MapHeld.Index} uniqueID={pawn.MapHeld.uniqueID}")}";
            }
        }
    }
}
