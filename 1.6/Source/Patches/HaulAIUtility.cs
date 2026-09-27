using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
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

                Map pawnMap = p.Map;
                Map itemMap = t.MapHeld;
                if (itemMap == null || !t.PositionHeld.IsValid)
                {
                    if (ModSettings.EnableLogging)
                    {
                        LogStaleHaulableItem(p, t);
                    }
                    TryRemoveFromHaulables(pawnMap, t);

                    if (itemMap == null && t.stackCount == 0 && !t.Destroyed)
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

            private static void TryRemoveFromHaulables(Map map, Thing item)
            {
                map.listerHaulables.Notify_DeSpawned(item);
            }

            private static void LogStaleHaulableItem(Pawn pawn, Thing item)
            {
                Logger.Error(
                    "PawnCanAutomaticallyHaulFast rejected a stale haulable during TryOpportunisticJob.\n" +
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

        // Only known caller for ThingDefOf.Shard is CompObeliskDeactivationInteractor.OrderDeactivation
        // (Mutator/Duplicator obelisks, shardsRequired = 2). Vanilla only walks region listers, which
        // never see items held inside a NetworkBuildingController, so a network-only Shard supply
        // never reaches job.targetQueueB even after the CanInteract precheck (see Patch_ReservationUtility)
        // reports the interaction as available.
        [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.FindFixedIngredientCount))]
        public static class FindFixedIngredientCount
        {
            public static void Postfix(Pawn pawn, ThingDef def, int maxCount, ref List<Thing> __result)
            {
                if (def != ThingDefOf.Shard || pawn?.Map == null)
                {
                    return;
                }

                int countFound = 0;
                for (int i = 0; i < __result.Count; i++)
                {
                    countFound += __result[i].stackCount;
                }

                if (countFound >= maxCount)
                {
                    return;
                }

                NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
                if (mapComp.Networks.Count == 0)
                {
                    return;
                }

                List<Thing> candidates = new List<Thing>();
                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(pawn.Map))
                {
                    if (item.def != def || item.Destroyed || item.stackCount <= 0 || __result.Contains(item))
                    {
                        continue;
                    }

                    if (item.IsForbidden(pawn) || !NetworkItemSearchUtility.IsUsableNetworkItemForExtraction(pawn, item, out _))
                    {
                        continue;
                    }

                    if (!pawn.CanReserve(item))
                    {
                        continue;
                    }

                    candidates.Add(item);
                }

                if (candidates.Count == 0)
                {
                    return;
                }

                // Preserve vanilla's own (closest-first) ordering, then prefer the closest reachable
                // interface for the network stacks appended after it.
                candidates.Sort((a, b) => NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn, a)
                    .CompareTo(NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn, b)));

                foreach (Thing item in candidates)
                {
                    if (countFound >= maxCount)
                    {
                        break;
                    }

                    __result.Add(item);
                    countFound += item.stackCount;
                }
            }
        }
    }
}
