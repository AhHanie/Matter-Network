using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_WorkGiver_HaulToAtomizer
    {
        [HarmonyPatch(typeof(WorkGiver_HaulToAtomizer), "HasJobOnThing")]
        public static class HasJobOnThing
        {
            public static void Postfix(Pawn pawn, Thing t, bool forced, ref bool __result)
            {
                if (__result) return;
                if (!pawn.CanReserve(t, 1, -1, null, forced)) return;

                CompAtomizer comp = t.TryGetComp<CompAtomizer>();
                if (comp == null || comp.Full) return;
                if (!forced && !comp.AutoLoad) return;
                if (!forced && comp.FillPercent > 0.5f) return;

                if (HasReachableNetworkWastepacks(pawn, comp, forced))
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(WorkGiver_HaulToAtomizer), "JobOnThing")]
        public static class JobOnThing
        {
            public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
            {
                if (__result != null) return;
                if (!pawn.CanReserve(t, 1, -1, null, forced)) return;

                CompAtomizer comp = t.TryGetComp<CompAtomizer>();
                if (comp == null || comp.Full) return;
                if (!forced && !comp.AutoLoad) return;

                List<Thing> candidates = FindNetworkWastepacks(pawn, comp, forced);
                if (candidates.NullOrEmpty()) return;

                Job job = JobMaker.MakeJob(JobDefOf.HaulToAtomizer, t);
                job.targetQueueB = candidates.Select(f => new LocalTargetInfo(f)).ToList();
                job.count = comp.SpaceLeft;
                __result = job;
            }
        }

        [HarmonyPatch(typeof(JobDriver_HaulToAtomizer), "TryMakePreToilReservations")]
        public static class TryMakePreToilReservations
        {
            public static bool Prefix(JobDriver_HaulToAtomizer __instance, bool errorOnFailed, ref bool __result)
            {
                Pawn pawn = __instance.pawn;
                Job job = __instance.job;

                List<LocalTargetInfo> queue = job.GetTargetQueue(TargetIndex.B);
                if (queue.NullOrEmpty())
                    return true;

                if (!queue.Any(t => t.Thing != null && !t.Thing.Spawned))
                    return true;

                bool atomizerReserved = pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
                if (!atomizerReserved)
                {
                    __result = false;
                    return false;
                }

                foreach (LocalTargetInfo target in queue)
                {
                    try { pawn.Reserve(target, job, 1, -1, null, errorOnFailed: false); }
                    catch { }
                }

                __result = true;
                return false;
            }
        }

        private static bool HasReachableNetworkWastepacks(Pawn pawn, CompAtomizer comp, bool forced)
        {
            ThingDef wastepackDef = comp.Props.thingDef;

            foreach (DataNetwork network in NetworkItemSearchUtility.Networks(pawn.Map))
            {
                if (NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn.Position, pawn, network) == float.MaxValue)
                    continue;

                foreach (Thing item in network.StoredItems)
                {
                    if (IsValidNetworkWastepack(pawn, item, wastepackDef, forced))
                        return true;
                }
            }

            return false;
        }

        private static List<Thing> FindNetworkWastepacks(Pawn pawn, CompAtomizer comp, bool forced)
        {
            ThingDef wastepackDef = comp.Props.thingDef;
            int needed = comp.SpaceLeft;
            List<(Thing thing, float distSq)> candidates = new List<(Thing thing, float distSq)>();

            foreach (DataNetwork network in NetworkItemSearchUtility.Networks(pawn.Map))
            {
                float distSq = NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn.Position, pawn, network);
                if (distSq == float.MaxValue) continue;

                foreach (Thing item in network.StoredItems)
                {
                    if (IsValidNetworkWastepack(pawn, item, wastepackDef, forced))
                        candidates.Add((item, distSq));
                }
            }

            if (candidates.Count == 0) return null;

            candidates.Sort((a, b) =>
            {
                int d = a.distSq.CompareTo(b.distSq);
                return d != 0 ? d : a.thing.thingIDNumber.CompareTo(b.thing.thingIDNumber);
            });

            List<Thing> result = new List<Thing>();
            int accumulated = 0;
            foreach ((Thing thing, float _) in candidates)
            {
                result.Add(thing);
                accumulated += thing.stackCount;
                if (accumulated >= needed) break;
            }

            return result;
        }

        private static bool IsValidNetworkWastepack(Pawn pawn, Thing item, ThingDef wastepackDef, bool forced)
        {
            if (item == null || item.Destroyed || item.stackCount <= 0) return false;
            if (item.def != wastepackDef) return false;
            if (item.IsForbidden(pawn)) return false;
            return NetworkItemSearchUtility.PawnCanUseNetworkItemForHaul(pawn, item, forced);
        }
    }
}
