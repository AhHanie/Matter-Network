using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class BulkLoadForTransportersCompat
    {
        private const string PackageId = "ilarion.bulkloadfortransporters";
        private const string WorkGiverUtilityTypeName = "BulkLoadForTransporters.Core.Utils.WorkGiver_Utility";
        private const string JobDefRegistryTypeName = "BulkLoadForTransporters.Core.JobDefRegistry";
        private const string CentralLoadManagerTypeName = "BulkLoadForTransporters.Core.CentralLoadManager";
        private const string GotoHaulableTypeName = "BulkLoadForTransporters.Toils_LoadTransporters.Toil_GotoHaulable";
        private const string DropAndTakeFromContainerTypeName = "BulkLoadForTransporters.Toils_LoadTransporters.Toil_DropAndTakeFromContainer";

        private static readonly Type workGiverUtilityType = AccessTools.TypeByName(WorkGiverUtilityTypeName);
        private static readonly Type jobDefRegistryType = AccessTools.TypeByName(JobDefRegistryTypeName);
        private static readonly Type centralLoadManagerType = AccessTools.TypeByName(CentralLoadManagerTypeName);
        private static readonly Type gotoHaulableType = AccessTools.TypeByName(GotoHaulableTypeName);
        private static readonly Type dropAndTakeFromContainerType = AccessTools.TypeByName(DropAndTakeFromContainerTypeName);

        private static readonly MethodInfo getJobDefForMethod = AccessTools.Method(jobDefRegistryType, "GetJobDefFor");
        private static readonly MethodInfo claimItemsMethod = AccessTools.Method(centralLoadManagerType, "ClaimItems");
        private static readonly MethodInfo getAvailableToClaimMethod = AccessTools.Method(centralLoadManagerType, "GetAvailableToClaim");
        private static readonly PropertyInfo centralLoadManagerInstanceProperty = AccessTools.Property(centralLoadManagerType, "Instance");
        private static readonly Dictionary<Type, LoadableMethodCache> loadableMethodCaches = new Dictionary<Type, LoadableMethodCache>();

        private static bool IsAvailable()
        {
            return ModsConfig.IsActive(PackageId);
        }

        private static bool IsWorkGiverAvailable()
        {
            return IsAvailable()
                && workGiverUtilityType != null
                && jobDefRegistryType != null
                && centralLoadManagerType != null
                && getJobDefForMethod != null
                && claimItemsMethod != null
                && getAvailableToClaimMethod != null
                && centralLoadManagerInstanceProperty != null;
        }

        private static bool IsGotoHaulableAvailable()
        {
            return IsAvailable() && gotoHaulableType != null;
        }

        private static bool IsDropAndTakeAvailable()
        {
            return IsAvailable() && dropAndTakeFromContainerType != null;
        }

        [HarmonyPatch]
        public static class HasPotentialBulkWork
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsWorkGiverAvailable();
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(workGiverUtilityType, "HasPotentialBulkWork");
            }

            public static void Postfix(Pawn pawn, object groupLoadable, ref bool __result)
            {
                if (__result || !CanPawnUseNetworkBulkLoad(pawn, groupLoadable))
                {
                    return;
                }

                __result = TryCreateNetworkHaulPlan(pawn, groupLoadable, out _, out _);
            }
        }

        [HarmonyPatch]
        public static class TryGiveBulkJob
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsWorkGiverAvailable();
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(workGiverUtilityType, "TryGiveBulkJob");
            }

            public static void Postfix(Pawn pawn, object groupLoadable, ref Job job, ref bool __result)
            {
                if (!CanPawnUseNetworkBulkLoad(pawn, groupLoadable) || !IsEmptyOrWaitJob(job))
                {
                    return;
                }

                if (TryCreateNetworkBulkJob(pawn, groupLoadable, out Job networkJob))
                {
                    job = networkJob;
                    __result = true;
                }
            }
        }

        [HarmonyPatch]
        public static class GotoHaulableCreate
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsGotoHaulableAvailable();
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(gotoHaulableType, "Create");
            }

            public static void Postfix(TargetIndex index, Toil jumpTarget, ref Toil __result)
            {
                Toil originalToil = __result;
                Action originalInitAction = originalToil.initAction;

                originalToil.initAction = delegate
                {
                    Pawn actor = originalToil.actor;
                    Thing thing = actor.CurJob.GetTarget(index).Thing;

                    if (!TryGetNetworkFor(actor, thing, out DataNetwork network))
                    {
                        originalInitAction?.Invoke();
                        return;
                    }

                    if (!network.IsOperational)
                    {
                        actor.jobs.curDriver.JumpToToil(jumpTarget);
                        return;
                    }

                    NetworkBuildingNetworkInterface networkInterface = Patch_Toils_Goto.GotoThing.FindClosestReachableInterface(actor, network);
                    if (networkInterface == null)
                    {
                        actor.jobs.curDriver.JumpToToil(jumpTarget);
                        return;
                    }

                    if (actor.Position == networkInterface.InteractionCell)
                    {
                        actor.pather.StopDead();
                        actor.jobs.curDriver.ReadyForNextToil();
                        return;
                    }

                    actor.pather.StartPath(networkInterface.InteractionCell, PathEndMode.OnCell);
                };
            }
        }

        [HarmonyPatch]
        public static class DropAndTakeFromContainerCreate
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsDropAndTakeAvailable();
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(dropAndTakeFromContainerType, "Create");
            }

            public static void Postfix(TargetIndex itemIndex, ref Toil __result)
            {
                Toil originalToil = __result;
                Action originalInitAction = originalToil.initAction;

                originalToil.initAction = delegate
                {
                    Pawn pawn = originalToil.actor;
                    Job job = pawn.CurJob;
                    Thing thingToTake = job.GetTarget(itemIndex).Thing;

                    if (!TryGetNetworkFor(pawn, thingToTake, out DataNetwork network))
                    {
                        originalInitAction?.Invoke();
                        return;
                    }

                    if (thingToTake == null || thingToTake.Destroyed || thingToTake.Spawned || !(thingToTake.ParentHolder is Thing container))
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                        return;
                    }

                    ThingOwner sourceOwner = container.TryGetInnerInteractableThingOwner();
                    if (sourceOwner == null || !sourceOwner.Contains(thingToTake))
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                        return;
                    }

                    int countToDrop = Mathf.Min(job.count, thingToTake.stackCount);
                    if (countToDrop <= 0)
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                        return;
                    }

                    if (sourceOwner.TryDrop(thingToTake, pawn.Position, pawn.Map, ThingPlaceMode.Near, countToDrop, out Thing droppedThing))
                    {
                        job.SetTarget(itemIndex, droppedThing);
                        pawn.Reserve(droppedThing, job);
                        network.MarkBytesDirty();
                        return;
                    }

                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                };
            }
        }

        private static bool TryCreateNetworkBulkJob(Pawn pawn, object loadable, out Job job)
        {
            job = null;
            Thing parentThing = GetParentThing(loadable);
            if (parentThing == null)
            {
                return false;
            }

            JobDef jobDef = GetJobDefFor(parentThing.def);
            if (jobDef == null || jobDef == JobDefOf.Wait)
            {
                return false;
            }

            if (!TryCreateNetworkHaulPlan(pawn, loadable, out List<LocalTargetInfo> targets, out List<int> counts))
            {
                return false;
            }

            job = JobMaker.MakeJob(jobDef);
            job.targetB = new LocalTargetInfo(parentThing);
            job.targetQueueA = targets;
            job.countQueue = counts;
            job.haulOpportunisticDuplicates = false;

            TryClaimItems(pawn, job, loadable);
            return true;
        }

        private static bool TryCreateNetworkHaulPlan(Pawn pawn, object loadable, out List<LocalTargetInfo> targets, out List<int> counts)
        {
            targets = null;
            counts = null;

            Map map = GetMap(loadable) ?? pawn.Map;
            if (map == null || pawn.carryTracker.CarriedThing != null)
            {
                return false;
            }

            List<TransferableOneWay> transferables = GetTransferables(loadable);
            if (transferables.NullOrEmpty())
            {
                return false;
            }

            Dictionary<ThingDef, int> availableToClaim = GetAvailableToClaim(loadable, pawn) ?? BuildAvailableToClaimFallback(transferables);
            if (availableToClaim.Count == 0)
            {
                return false;
            }

            NetworkHaulCandidate bestCandidate = null;
            foreach (TransferableOneWay transferable in transferables)
            {
                if (!transferable.HasAnyThing || transferable.CountToTransfer <= 0)
                {
                    continue;
                }

                if (!availableToClaim.TryGetValue(transferable.ThingDef, out int availableCount) || availableCount <= 0)
                {
                    continue;
                }

                foreach (NetworkHaulCandidate candidate in MatchingNetworkCandidates(pawn, map, transferable, availableCount))
                {
                    if (bestCandidate == null || candidate.DistanceSquared < bestCandidate.DistanceSquared)
                    {
                        bestCandidate = candidate;
                    }
                }
            }

            if (bestCandidate == null || bestCandidate.Count <= 0)
            {
                return false;
            }

            targets = new List<LocalTargetInfo> { new LocalTargetInfo(bestCandidate.Thing) };
            counts = new List<int> { bestCandidate.Count };
            return true;
        }

        private static IEnumerable<NetworkHaulCandidate> MatchingNetworkCandidates(
            Pawn pawn,
            Map map,
            TransferableOneWay transferable,
            int availableCount)
        {
            bool needsExactMatch = !IsFungible(transferable.AnyThing);

            foreach (DataNetwork network in NetworkItemSearchUtility.Networks(map))
            {
                if (network.Faction != null && network.Faction != Faction.OfPlayer)
                {
                    continue;
                }

                float distanceSquared = NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn.Position, pawn, network);
                if (distanceSquared == float.MaxValue)
                {
                    continue;
                }

                foreach (Thing item in network.StoredItems)
                {
                    if (item.Destroyed || item.stackCount <= 0 || item.def != transferable.ThingDef)
                    {
                        continue;
                    }

                    if (item.IsForbidden(pawn))
                    {
                        continue;
                    }

                    if (needsExactMatch && !transferable.things.Contains(item) && FindBestMatchFor(item, transferable) == null)
                    {
                        continue;
                    }

                    int count = GetNetworkPickupCount(pawn, item, transferable.CountToTransfer, availableCount);
                    if (count > 0)
                    {
                        yield return new NetworkHaulCandidate(item, count, distanceSquared);
                    }
                }
            }
        }

        private static int GetNetworkPickupCount(Pawn pawn, Thing item, int neededCount, int availableCount)
        {
            int carryLimit = item.def.stackLimit;
            if (item.def.VolumePerUnit > 0f)
            {
                carryLimit = Mathf.Min(carryLimit, Mathf.FloorToInt(pawn.GetStatValue(StatDefOf.CarryingCapacity) / item.def.VolumePerUnit));
            }

            return Mathf.Min(item.stackCount, neededCount, availableCount, carryLimit);
        }

        private static bool CanPawnUseNetworkBulkLoad(Pawn pawn, object loadable)
        {
            if (loadable == null || pawn.Drafted)
            {
                return false;
            }

            Thing parentThing = GetParentThing(loadable);
            return parentThing != null && pawn.CanReach(parentThing, PathEndMode.Touch, Danger.Deadly);
        }

        private static bool TryGetNetworkFor(Pawn pawn, Thing thing, out DataNetwork network)
        {
            network = null;
            if (pawn?.Map == null || thing == null)
            {
                return false;
            }

            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            return mapComp.TryGetItemNetwork(thing, out network);
        }

        private static bool IsEmptyOrWaitJob(Job job)
        {
            return job == null || job.def == JobDefOf.Wait || job.targetQueueA.NullOrEmpty();
        }

        private static JobDef GetJobDefFor(ThingDef parentDef)
        {
            return getJobDefForMethod.Invoke(null, new object[] { parentDef }) as JobDef;
        }

        private static void TryClaimItems(Pawn pawn, Job job, object loadable)
        {
            object manager = centralLoadManagerInstanceProperty.GetValue(null, null);
            claimItemsMethod.Invoke(manager, new object[] { pawn, job, loadable });
        }

        private static Dictionary<ThingDef, int> GetAvailableToClaim(object loadable, Pawn pawn)
        {
            object manager = centralLoadManagerInstanceProperty.GetValue(null, null);
            return getAvailableToClaimMethod.Invoke(manager, new object[] { loadable, pawn }) as Dictionary<ThingDef, int>;
        }

        private static Dictionary<ThingDef, int> BuildAvailableToClaimFallback(List<TransferableOneWay> transferables)
        {
            Dictionary<ThingDef, int> result = new Dictionary<ThingDef, int>();
            foreach (TransferableOneWay transferable in transferables)
            {
                if (transferable.CountToTransfer <= 0)
                {
                    continue;
                }

                if (result.TryGetValue(transferable.ThingDef, out int current))
                {
                    result[transferable.ThingDef] = current + transferable.CountToTransfer;
                }
                else
                {
                    result[transferable.ThingDef] = transferable.CountToTransfer;
                }
            }

            return result;
        }

        private static Map GetMap(object loadable)
        {
            return InvokeLoadableMethod<Map>(loadable, "GetMap");
        }

        private static Thing GetParentThing(object loadable)
        {
            return InvokeLoadableMethod<Thing>(loadable, "GetParentThing");
        }

        private static List<TransferableOneWay> GetTransferables(object loadable)
        {
            return InvokeLoadableMethod<List<TransferableOneWay>>(loadable, "GetTransferables");
        }

        private static T InvokeLoadableMethod<T>(object loadable, string methodName) where T : class
        {
            MethodInfo method = GetLoadableMethodCache(loadable.GetType()).Get(methodName);
            return method.Invoke(loadable, null) as T;
        }

        private static LoadableMethodCache GetLoadableMethodCache(Type loadableType)
        {
            if (!loadableMethodCaches.TryGetValue(loadableType, out LoadableMethodCache cache))
            {
                cache = new LoadableMethodCache(loadableType);
                loadableMethodCaches.Add(loadableType, cache);
            }

            return cache;
        }

        private static bool IsFungible(Thing thing)
        {
            if (thing is MinifiedThing minifiedThing)
            {
                thing = minifiedThing.InnerThing;
            }

            if (thing.def.stackLimit <= 1) return false;
            if (thing.def.tradeNeverStack) return false;
            if (thing.TryGetQuality(out _)) return false;
            if (thing.def.MadeFromStuff) return false;
            if (thing.def.useHitPoints && thing.HitPoints < thing.MaxHitPoints) return false;
            if (thing.TryGetComp<CompIngredients>() != null) return false;
            if (thing.TryGetComp<CompArt>() != null) return false;
            if (thing is Genepack || thing is Xenogerm) return false;

            ThingWithComps thingWithComps = thing as ThingWithComps;
            if (thingWithComps != null && thingWithComps.TryGetComp<CompBiocodable>()?.Biocoded == true) return false;

            Apparel apparel = thing as Apparel;
            if (apparel != null && apparel.WornByCorpse) return false;

            if (thing is Pawn || thing is Corpse) return false;

            return true;
        }

        private static TransferableOneWay FindBestMatchFor(Thing thing, TransferableOneWay transferable)
        {
            if (transferable.CountToTransfer > 0 && transferable.things.Contains(thing))
            {
                return transferable;
            }

            if (thing.def.stackLimit <= 1)
            {
                return null;
            }

            return transferable.ThingDef == thing.def && transferable.CountToTransfer > 0 ? transferable : null;
        }

        private sealed class NetworkHaulCandidate
        {
            public readonly Thing Thing;
            public readonly int Count;
            public readonly float DistanceSquared;

            public NetworkHaulCandidate(Thing thing, int count, float distanceSquared)
            {
                Thing = thing;
                Count = count;
                DistanceSquared = distanceSquared;
            }
        }

        private sealed class LoadableMethodCache
        {
            private readonly MethodInfo getMapMethod;
            private readonly MethodInfo getParentThingMethod;
            private readonly MethodInfo getTransferablesMethod;

            public LoadableMethodCache(Type loadableType)
            {
                getMapMethod = AccessTools.Method(loadableType, "GetMap");
                getParentThingMethod = AccessTools.Method(loadableType, "GetParentThing");
                getTransferablesMethod = AccessTools.Method(loadableType, "GetTransferables");
            }

            public MethodInfo Get(string methodName)
            {
                switch (methodName)
                {
                    case "GetMap":
                        return getMapMethod;
                    case "GetParentThing":
                        return getParentThingMethod;
                    case "GetTransferables":
                        return getTransferablesMethod;
                    default:
                        throw new ArgumentException(methodName);
                }
            }
        }
    }
}
