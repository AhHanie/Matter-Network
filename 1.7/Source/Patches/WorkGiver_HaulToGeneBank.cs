using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_WorkGiver_HaulToGeneBank
    {
        [HarmonyPatch(typeof(WorkGiver_HaulToGeneBank), "HasJobOnThing")]
        public static class HasJobOnThing
        {
            public static bool Prefix(Pawn pawn, Thing t, bool forced, ref bool __result)
            {
                if (!TryGetNetworkHeldGenepack(pawn, t, out Genepack genepack, out DataNetwork network))
                    return true;

                __result =
                    TryFindClosestReachableInterface(pawn, network, out _) &&
                    CanReserveNetworkGenepack(pawn, genepack, forced) &&
                    FindGeneBankForNetworkGenepack(pawn, genepack, network) != null;
                return false;
            }
        }

        [HarmonyPatch(typeof(WorkGiver_HaulToGeneBank), "JobOnThing")]
        public static class JobOnThing
        {
            public static bool Prefix(Pawn pawn, Thing t, bool forced, ref Job __result)
            {
                if (!TryGetNetworkHeldGenepack(pawn, t, out Genepack genepack, out DataNetwork network))
                    return true;

                if (!TryFindClosestReachableInterface(pawn, network, out NetworkBuildingNetworkInterface iface))
                {
                    __result = null;
                    return false;
                }

                Thing geneBank = FindGeneBankForNetworkGenepack(pawn, genepack, network);
                if (geneBank == null)
                {
                    __result = null;
                    return false;
                }

                Job job = JobMaker.MakeJob(JobDefOf.CarryGenepackToContainer, genepack, geneBank);
                job.count = genepack.stackCount;
                job.targetC = iface;
                __result = job;
                return false;
            }
        }

        [HarmonyPatch(typeof(JobDriver_CarryGenepackToContainer), "TryMakePreToilReservations")]
        public static class TryMakePreToilReservations
        {
            public static bool Prefix(JobDriver_CarryGenepackToContainer __instance, bool errorOnFailed, ref bool __result)
            {
                Pawn pawn = __instance.pawn;
                Job job = __instance.job;
                Thing targetA = job.targetA.Thing;

                if (!TryGetNetworkHeldGenepack(pawn, targetA, out _, out _))
                    return true;

                Thing geneBank = job.targetB.Thing;
                if (geneBank != null && !pawn.Reserve(geneBank, job, 1, -1, null, errorOnFailed))
                {
                    __result = false;
                    return false;
                }

                try
                {
                    pawn.Reserve(targetA, job, 1, -1, null, errorOnFailed: false);
                }
                catch { }

                __result = true;
                return false;
            }
        }

        internal static bool TryGetNetworkHeldGenepack(Pawn pawn, Thing thing, out Genepack genepack, out DataNetwork network)
        {
            genepack = null;
            network = null;

            if (!ModsConfig.BiotechActive) return false;
            if (pawn?.Spawned != true) return false;
            if (!(thing is Genepack gp)) return false;
            if (thing.Destroyed || thing.stackCount <= 0) return false;
            if (thing.Spawned) return false;

            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            if (!mapComp.TryGetItemNetwork(thing, out network)) return false;
            if (!network.CanExtractItems) return false;
            if (network.ActiveController?.innerContainer?.Contains(thing) != true) return false;

            genepack = gp;
            return true;
        }

        private static bool TryFindClosestReachableInterface(Pawn pawn, DataNetwork network, out NetworkBuildingNetworkInterface iface)
        {
            iface = null;
            if (!network.IsOperational) return false;

            float closestDistSq = float.MaxValue;
            List<NetworkBuildingNetworkInterface> interfaces = network.NetworkInterfaces;
            for (int i = 0; i < interfaces.Count; i++)
            {
                NetworkBuildingNetworkInterface candidate = interfaces[i];
                if (candidate == null || candidate.Destroyed || !candidate.Spawned) continue;
                if (candidate.Map != pawn.Map) continue;
                if (!pawn.CanReach(candidate.InteractionCell, PathEndMode.OnCell, Danger.Deadly)) continue;

                float distSq = (pawn.Position - candidate.InteractionCell).LengthHorizontalSquared;
                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    iface = candidate;
                }
            }

            return iface != null;
        }

        private static bool CanReserveNetworkGenepack(Pawn pawn, Genepack genepack, bool forced)
        {
            try
            {
                return pawn.CanReserve(genepack, 1, -1, null, forced);
            }
            catch
            {
                return true;
            }
        }

        private static Thing FindGeneBankForNetworkGenepack(Pawn pawn, Genepack genepack, DataNetwork network)
        {
            if (!genepack.AutoLoad) return null;

            if (genepack.targetContainer != null)
            {
                Thing target = genepack.targetContainer;
                if (!target.Spawned || target.Map != pawn.Map) return null;
                CompGenepackContainer comp = target.TryGetComp<CompGenepackContainer>();
                if (comp == null || comp.Full) return null;
                if (target.IsForbidden(pawn)) return null;
                if (!pawn.CanReserveAndReach(target, PathEndMode.InteractionCell, Danger.Deadly)) return null;
                return target;
            }

            IntVec3 searchRoot = pawn.Position;
            if (TryFindClosestReachableInterface(pawn, network, out NetworkBuildingNetworkInterface iface))
                searchRoot = iface.InteractionCell;

            return GenClosest.ClosestThingReachable(
                searchRoot,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.GenepackHolder),
                PathEndMode.InteractionCell,
                TraverseParms.For(pawn),
                validator: candidate =>
                {
                    if (candidate.IsForbidden(pawn)) return false;
                    if (!pawn.CanReserve(candidate)) return false;
                    CompGenepackContainer comp = candidate.TryGetComp<CompGenepackContainer>();
                    if (comp == null || comp.Full || !comp.autoLoad) return false;
                    if (genepack.targetContainer != null && genepack.targetContainer != candidate) return false;
                    return true;
                });
        }
    }
}
