using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    // Soft compat: lets Manipulator Beam Emitter buildings beam ordinary map haulables
    // into Matter Network interfaces/chutes when the network is the best storage
    // destination. No compile-time reference to ManipulatorBeam.dll; everything below
    // is resolved via reflection and skipped entirely if the mod isn't loaded.
    public static class ManipulatorBeamCompat
    {
        private static readonly System.Type BeamManipulatorUtilityType =
            AccessTools.TypeByName("ManipulatorBeam.BeamManipulatorUtility");
        private static readonly System.Type BeamTransferType =
            AccessTools.TypeByName("ManipulatorBeam.BeamTransfer");
        private static readonly System.Type BeamClaimUtilityType =
            AccessTools.TypeByName("ManipulatorBeam.BeamClaimUtility");
        private static readonly System.Type BeamManipulatorType =
            AccessTools.TypeByName("ManipulatorBeam.Building_BeamManipulator");
        private static readonly System.Type BeamManipulatorAutoType =
            AccessTools.TypeByName("ManipulatorBeam.Building_BeamManipulatorAuto");

        private static readonly System.Type BeamTransferListType =
            BeamTransferType != null ? typeof(List<>).MakeGenericType(BeamTransferType) : null;

        // Both FillTransferQueue/FillTransferQueueAuto have a public delegator and an
        // internal worker overload; the worker is the one JobDriver_OperateBeamManipulator
        // and Building_BeamManipulatorAuto actually call, and the one whose queue we need
        // to postfix. AccessTools.Method(type, name) - name only - is ambiguous between the
        // two overloads and throws AmbiguousMatchException during PatchAll, so both worker
        // overloads are resolved here by their exact current parameter lists instead. The
        // public delegators are deliberately never patched: they just forward into the
        // worker overload below, so patching both would run our postfix twice per call.

        // Manual worker (9 params): adds a HashSet<IntVec3>/List<IntVec3> candidate-cell
        // scratch pair after the public delegator's 7 params (pawn, manipulator,
        // desiredCount, destinationQueue, excludedThings, excludedDestinations, preferredThing).
        private static readonly MethodInfo FillTransferQueueMethod =
            (BeamManipulatorUtilityType != null && BeamManipulatorType != null && BeamTransferListType != null)
                ? AccessTools.Method(BeamManipulatorUtilityType, "FillTransferQueue", new[]
                    {
                        typeof(Pawn), BeamManipulatorType, typeof(int), BeamTransferListType,
                        typeof(HashSet<Thing>), typeof(HashSet<IntVec3>), typeof(Thing),
                        typeof(HashSet<IntVec3>), typeof(List<IntVec3>)
                    })
                : null;

        // Automatic worker (7 params): same scratch pair appended after the public
        // delegator's 5 params (building, desiredCount, destinationQueue, excludedThings,
        // excludedDestinations).
        private static readonly MethodInfo FillTransferQueueAutoMethod =
            (BeamManipulatorUtilityType != null && BeamManipulatorAutoType != null && BeamTransferListType != null)
                ? AccessTools.Method(BeamManipulatorUtilityType, "FillTransferQueueAuto", new[]
                    {
                        BeamManipulatorAutoType, typeof(int), BeamTransferListType,
                        typeof(HashSet<Thing>), typeof(HashSet<IntVec3>),
                        typeof(HashSet<IntVec3>), typeof(List<IntVec3>)
                    })
                : null;

        private static readonly ConstructorInfo BeamTransferCtor =
            BeamTransferType?.GetConstructor(new[] { typeof(Thing), typeof(IntVec3), typeof(Thing), typeof(int) });
        private static readonly ConstructorInfo BeamTransferCtorCell =
            BeamTransferType?.GetConstructor(new[] { typeof(Thing), typeof(IntVec3), typeof(IntVec3), typeof(int) });
        private static readonly FieldInfo ThingClaimedField =
            AccessTools.Field(BeamTransferType, "thingClaimed");
        private static readonly FieldInfo TransferDestinationContainerField =
            AccessTools.Field(BeamTransferType, "destinationContainer");
        private static readonly FieldInfo TransferThingField =
            AccessTools.Field(BeamTransferType, "thing");

        private static readonly MethodInfo TryClaimThingMethod =
            AccessTools.Method(BeamClaimUtilityType, "TryClaimThing", new[] { typeof(Thing), typeof(int) });
        private static readonly MethodInfo TryClaimDestinationContainerMethod =
            AccessTools.Method(BeamClaimUtilityType, "TryClaimDestinationContainer", new[] { BeamTransferType, typeof(int) });
        private static readonly MethodInfo TryClaimStorageDestinationMethod =
            AccessTools.Method(BeamClaimUtilityType, "TryClaimStorageDestination", new[] { BeamTransferType, typeof(int) });
        private static readonly MethodInfo ReleaseClaimMethod =
            AccessTools.Method(BeamClaimUtilityType, "ReleaseClaim", new[] { BeamTransferType, typeof(int) });
        private static readonly MethodInfo IsThingClaimedByOtherMethod =
            AccessTools.Method(BeamClaimUtilityType, "IsThingClaimedByOther", new[] { typeof(Thing), typeof(int) });

        // Beam's own individual sub-checks - used to mirror its real eligibility gate
        // (CanAutoTransferThingForOwner / IsBeamHaulCandidateForOwner) minus the one
        // sub-check that's actively hostile to this feature (see IsEligibleCandidate).
        private static readonly MethodInfo IsThingLockedForBillWorkMethod =
            AccessTools.Method(BeamManipulatorUtilityType, "IsThingLockedForBillWork", new[] { typeof(Map), typeof(Thing) });
        private static readonly MethodInfo IsPrisonCellFoodMethod =
            AccessTools.Method(BeamManipulatorUtilityType, "IsPrisonCellFood", new[] { typeof(Thing) });
        private static readonly MethodInfo IsSourceUnavailableCoolingDownMethod =
            AccessTools.Method(BeamManipulatorUtilityType, "IsSourceUnavailableCoolingDown", new[] { typeof(Thing) });

        private static bool IsAvailable() =>
            BeamManipulatorUtilityType != null
            && BeamTransferType != null
            && BeamClaimUtilityType != null
            && BeamTransferCtor != null
            && BeamTransferCtorCell != null
            && ThingClaimedField != null
            && TryClaimThingMethod != null
            && TryClaimDestinationContainerMethod != null
            && TryClaimStorageDestinationMethod != null
            && ReleaseClaimMethod != null
            && IsThingClaimedByOtherMethod != null
            && (DefDatabase<ThingDef>.GetNamedSilentFail("MB_ManipulatorBeamEmitter") != null
                || DefDatabase<ThingDef>.GetNamedSilentFail("MB_ManipulatorBeamEmitterAuto") != null);

        private static bool TryClaimThingWrapper(Thing thing, int ownerKey) =>
            (bool)TryClaimThingMethod.Invoke(null, new object[] { thing, ownerKey });

        private static bool TryClaimDestinationContainerWrapper(object transfer, int ownerKey) =>
            (bool)TryClaimDestinationContainerMethod.Invoke(null, new object[] { transfer, ownerKey });

        private static bool TryClaimStorageDestinationWrapper(object transfer, int ownerKey) =>
            (bool)TryClaimStorageDestinationMethod.Invoke(null, new object[] { transfer, ownerKey });

        private static void ReleaseClaimWrapper(object transfer, int ownerKey) =>
            ReleaseClaimMethod.Invoke(null, new object[] { transfer, ownerKey });

        private static bool IsThingClaimedByOtherWrapper(Thing thing, int ownerKey) =>
            (bool)IsThingClaimedByOtherMethod.Invoke(null, new object[] { thing, ownerKey });

        private static bool IsThingLockedForBillWorkWrapper(Map map, Thing thing) =>
            IsThingLockedForBillWorkMethod != null
            && (bool)IsThingLockedForBillWorkMethod.Invoke(null, new object[] { map, thing });

        private static bool IsPrisonCellFoodWrapper(Thing thing) =>
            IsPrisonCellFoodMethod != null
            && (bool)IsPrisonCellFoodMethod.Invoke(null, new object[] { thing });

        private static bool IsSourceUnavailableCoolingDownWrapper(Thing thing) =>
            IsSourceUnavailableCoolingDownMethod != null
            && (bool)IsSourceUnavailableCoolingDownMethod.Invoke(null, new object[] { thing });

        private static bool IsNetworkDestination(Thing t) =>
            t is NetworkBuildingNetworkInterface || t is NetworkBuildingNetworkChute;

        // Bytes committed to a network by transfers we've already queued but that
        // haven't deposited yet (still traveling: reposition/warmup/pickup/transport).
        // ControllerCanAcceptCount only reflects bytes actually in the network, so
        // without this a later queue-fill (or another channel on the same emitter)
        // can believe capacity is free that's really already spoken for, queue a
        // second transfer, and have it rejected on arrival - Beam then just drops
        // the item back at its source cell, which looks like "the beam did nothing".
        private static readonly Dictionary<DataNetwork, int> pendingReservedCounts = new Dictionary<DataNetwork, int>();
        private static readonly Dictionary<object, (DataNetwork network, int count)> pendingTransferReservations =
            new Dictionary<object, (DataNetwork, int)>();

        // thingIDNumbers we currently have an active/queued network transfer for.
        // Beam's own pre-transport gate (inline in Tick(), right before a channel
        // starts carrying an item) re-runs its FULL, unmodified
        // CanAutoTransferThingForOwner - including the vanilla storage-retry cooldown
        // we deliberately don't check ourselves (see IsEligibleCandidate). Beam's own
        // vanilla-destination search independently scans the same haulables and can
        // mark that cooldown at any moment, even after we've already found the item
        // a network destination, so the re-check aborts the transfer right as the
        // beam locks onto it - before pickup ever starts. Patch_IsStorageRetryCoolingDown
        // suppresses just that one check for things tracked here.
        private static readonly HashSet<int> networkClaimedThingIds = new HashSet<int>();

        [HarmonyPatch]
        public static class Patch_IsStorageRetryCoolingDown
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(BeamManipulatorUtilityType, "IsStorageRetryCoolingDown", new[] { typeof(Thing) });

            public static bool Prefix(Thing thing, ref bool __result)
            {
                if (thing == null || !networkClaimedThingIds.Contains(thing.thingIDNumber)) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch]
        public static class Patch_ReleaseClaim
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(BeamClaimUtilityType, "ReleaseClaim", new[] { BeamTransferType, typeof(int) });

            public static void Postfix(object __0)
            {
                if (__0 == null) return;

                Thing destContainer = TransferDestinationContainerField?.GetValue(__0) as Thing;
                if (IsNetworkDestination(destContainer))
                {
                    Thing sourceThing = TransferThingField?.GetValue(__0) as Thing;
                    if (sourceThing != null) networkClaimedThingIds.Remove(sourceThing.thingIDNumber);
                }

                if (!pendingTransferReservations.TryGetValue(__0, out (DataNetwork network, int count) entry)) return;

                pendingTransferReservations.Remove(__0);
                pendingReservedCounts.TryGetValue(entry.network, out int current);
                int updated = current - entry.count;
                if (updated > 0) pendingReservedCounts[entry.network] = updated;
                else pendingReservedCounts.Remove(entry.network);
            }
        }

        // ── PATCH 1 ───────────────────────────────────────────────────────────
        // Fill remaining manual-queue slots with network destinations after Beam's
        // own destination search (construction, transporters, storage cells, etc.)
        // has had first pick.
        [HarmonyPatch]
        public static class Patch_FillTransferQueue
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable() && FillTransferQueueMethod != null;

            public static MethodBase TargetMethod() => FillTransferQueueMethod;

            public static void Postfix(object[] __args)
            {
                if (__args == null || __args.Length < 6) return;
                Pawn pawn = __args[0] as Pawn;
                Thing manipulator = __args[1] as Thing;
                if (pawn == null || manipulator == null || pawn.Map == null) return;

                int desiredCount = (int)__args[2];
                IList destinationQueue = __args[3] as IList;
                HashSet<Thing> excludedThings = __args[4] as HashSet<Thing>;
                HashSet<IntVec3> excludedDestinations = __args[5] as HashSet<IntVec3>;
                Thing preferredThing = __args.Length > 6 ? __args[6] as Thing : null;

                FillQueueWithNetworkDestinations(
                    pawn.Map, manipulator.Position, manipulator.thingIDNumber, desiredCount,
                    destinationQueue, excludedThings, excludedDestinations,
                    pawn, pawn.Faction, preferredThing, manual: true);

                FillQueueWithNetworkExtractions(
                    pawn.Map, manipulator.Position, manipulator.thingIDNumber, desiredCount,
                    destinationQueue, excludedThings, excludedDestinations,
                    pawn, pawn.Faction, manual: true);
            }
        }

        // ── PATCH 2 ───────────────────────────────────────────────────────────
        // Same, for the automatic emitter's queue fill.
        [HarmonyPatch]
        public static class Patch_FillTransferQueueAuto
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable() && FillTransferQueueAutoMethod != null;

            public static MethodBase TargetMethod() => FillTransferQueueAutoMethod;

            public static void Postfix(object[] __args)
            {
                if (__args == null || __args.Length < 5) return;
                Thing building = __args[0] as Thing;
                if (building == null || building.Map == null) return;

                int desiredCount = (int)__args[1];
                IList destinationQueue = __args[2] as IList;
                HashSet<Thing> excludedThings = __args[3] as HashSet<Thing>;
                HashSet<IntVec3> excludedDestinations = __args[4] as HashSet<IntVec3>;

                Faction faction = building.Faction ?? Faction.OfPlayer;

                FillQueueWithNetworkDestinations(
                    building.Map, building.Position, building.thingIDNumber, desiredCount,
                    destinationQueue, excludedThings, excludedDestinations,
                    null, faction, null, manual: false);

                FillQueueWithNetworkExtractions(
                    building.Map, building.Position, building.thingIDNumber, desiredCount,
                    destinationQueue, excludedThings, excludedDestinations,
                    null, faction, manual: false);
            }
        }

        // ── Shared queue-filling logic ────────────────────────────────────────

        private static void FillQueueWithNetworkDestinations(
            Map map, IntVec3 origin, int ownerKey, int desiredCount, IList destinationQueue,
            HashSet<Thing> excludedThings, HashSet<IntVec3> excludedDestinations,
            Pawn pawn, Faction faction, Thing preferredThing, bool manual)
        {
            if (map == null || destinationQueue == null || excludedThings == null || excludedDestinations == null)
                return;
            if (destinationQueue.Count >= desiredCount)
                return;

            // Tracks bytes tentatively committed to each network within this single
            // fill call, so several items queued in the same refresh don't all target
            // the same network beyond its currently known remaining capacity.
            // ControllerItemOwner.TryAdd still re-validates capacity at deposit time.
            Dictionary<DataNetwork, int> reservedThisCall = new Dictionary<DataNetwork, int>();

            if (manual && preferredThing != null && destinationQueue.Count < desiredCount
                && !excludedThings.Contains(preferredThing)
                && IsEligibleCandidate(preferredThing, map, pawn, faction, manual, ownerKey))
            {
                TryQueueNetworkTransfer(preferredThing, map, ownerKey, pawn, faction, manual,
                    destinationQueue, excludedThings, excludedDestinations, reservedThisCall);
            }

            if (destinationQueue.Count >= desiredCount)
                return;

            ICollection<Thing> haulables = map.listerHaulables.ThingsPotentiallyNeedingHauling();
            if (haulables == null || haulables.Count == 0)
                return;

            List<Thing> candidates = new List<Thing>();
            foreach (Thing thing in haulables)
            {
                if (ReferenceEquals(thing, preferredThing)) continue;
                if (excludedThings.Contains(thing)) continue;
                if (!IsEligibleCandidate(thing, map, pawn, faction, manual, ownerKey)) continue;
                candidates.Add(thing);
            }

            candidates.Sort((a, b) =>
                (a.Position - origin).LengthHorizontalSquared.CompareTo((b.Position - origin).LengthHorizontalSquared));

            for (int i = 0; i < candidates.Count; i++)
            {
                if (destinationQueue.Count >= desiredCount) break;
                TryQueueNetworkTransfer(candidates[i], map, ownerKey, pawn, faction, manual,
                    destinationQueue, excludedThings, excludedDestinations, reservedThisCall);
            }
        }

        // Mirrors Beam's own eligibility gates (CanAutoTransferThingForOwner /
        // IsBeamHaulCandidateForOwner) - bill-ingredient locks, prisoner food, claim
        // state, reservation - EXCEPT their vanilla storage-retry cooldown check.
        // That cooldown means "no ordinary stockpile cell was found for this thing
        // recently", which Beam's own vanilla search re-arms every ~30 ticks whenever
        // it fails again. Including it here would make the network permanently
        // unreachable for exactly the items this feature exists for: things that
        // don't fit anywhere in ordinary storage.
        private static bool IsEligibleCandidate(Thing thing, Map map, Pawn pawn, Faction faction, bool manual, int ownerKey)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != map) return false;
            if (!thing.def.EverHaulable) return false;
            if (thing.def.isUnfinishedThing) return false;
            if (thing.Position.Fogged(map)) return false;
            if (IsThingLockedForBillWorkWrapper(map, thing)) return false;
            if (IsPrisonCellFoodWrapper(thing)) return false;
            if (IsSourceUnavailableCoolingDownWrapper(thing)) return false;
            if (IsThingClaimedByOtherWrapper(thing, ownerKey)) return false;

            if (manual)
            {
                if (pawn == null) return false;
                if (thing.IsForbidden(pawn)) return false;
                if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) return false;
                return pawn.CanReserve(thing, 1, thing.stackCount);
            }

            if (thing.IsForbidden(faction)) return false;
            return !map.reservationManager.IsReservedAndRespected(thing, faction);
        }

        private static bool TryQueueNetworkTransfer(
            Thing thing, Map map, int ownerKey, Pawn pawn, Faction faction, bool manual,
            IList destinationQueue, HashSet<Thing> excludedThings, HashSet<IntVec3> excludedDestinations,
            Dictionary<DataNetwork, int> reservedThisCall)
        {
            StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(thing);

            if (!TryFindNetworkDestination(thing, map, currentPriority, excludedDestinations, faction, pawn, manual,
                    reservedThisCall, out Thing endpointThing, out DataNetwork network, out int count))
            {
                return false;
            }

            if (!TryClaimThingWrapper(thing, ownerKey))
                return false;

            object transfer = BeamTransferCtor.Invoke(new object[] { thing, thing.Position, endpointThing, count });
            ThingClaimedField.SetValue(transfer, true);

            if (!TryClaimDestinationContainerWrapper(transfer, ownerKey))
            {
                ReleaseClaimWrapper(transfer, ownerKey);
                return false;
            }

            destinationQueue.Add(transfer);
            excludedThings.Add(thing);
            excludedDestinations.Add(endpointThing.Position);

            reservedThisCall.TryGetValue(network, out int reserved);
            reservedThisCall[network] = reserved + count;

            pendingReservedCounts.TryGetValue(network, out int pendingExisting);
            pendingReservedCounts[network] = pendingExisting + count;
            pendingTransferReservations[transfer] = (network, count);
            networkClaimedThingIds.Add(thing.thingIDNumber);

            return true;
        }

        private static bool TryFindNetworkDestination(
            Thing thing, Map map, StoragePriority currentPriority, HashSet<IntVec3> excludedDestinations,
            Faction viewerFaction, Pawn pawn, bool manual, Dictionary<DataNetwork, int> reservedThisCall,
            out Thing endpointThing, out DataNetwork network, out int count)
        {
            endpointThing = null;
            network = null;
            count = 0;

            StoragePriority bestPriority = StoragePriority.Unstored;
            float bestDistanceSquared = float.MaxValue;
            Thing bestEndpoint = null;
            DataNetwork bestNetwork = null;
            int bestCount = 0;

            List<IHaulDestination> destinations = map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder;
            for (int i = 0; i < destinations.Count; i++)
            {
                IHaulDestination hd = destinations[i];

                DataNetwork candidateNetwork;
                if (hd is NetworkBuildingNetworkInterface networkInterface)
                    candidateNetwork = networkInterface.ParentNetwork;
                else if (hd is NetworkBuildingNetworkChute chute)
                    candidateNetwork = chute.ParentNetwork;
                else
                    continue;

                if (candidateNetwork == null) continue;
                if (!(hd is Thing endpoint) || endpoint.Destroyed || !endpoint.Spawned) continue;
                if (!hd.HaulDestinationEnabled) continue;
                if (excludedDestinations != null && excludedDestinations.Contains(endpoint.Position)) continue;
                if (manual ? endpoint.IsForbidden(pawn) : endpoint.IsForbidden(viewerFaction)) continue;

                StoragePriority destPriority = hd.GetStoreSettings().Priority;
                if ((int)destPriority <= (int)currentPriority) continue;

                if (!hd.Accepts(thing)) continue;

                int cap = candidateNetwork.ControllerCanAcceptCount(thing);
                reservedThisCall.TryGetValue(candidateNetwork, out int reserved);
                pendingReservedCounts.TryGetValue(candidateNetwork, out int pending);
                int available = cap - reserved - pending;
                if (available <= 0) continue;

                int candidateCount = Mathf.Min(thing.stackCount, available);
                if (candidateCount <= 0) continue;

                float distanceSquared = (thing.Position - endpoint.Position).LengthHorizontalSquared;
                bool better = bestEndpoint == null
                    || (int)destPriority > (int)bestPriority
                    || ((int)destPriority == (int)bestPriority && distanceSquared < bestDistanceSquared);

                if (better)
                {
                    bestPriority = destPriority;
                    bestDistanceSquared = distanceSquared;
                    bestEndpoint = endpoint;
                    bestNetwork = candidateNetwork;
                    bestCount = candidateCount;
                }
            }

            if (bestEndpoint == null) return false;

            endpointThing = bestEndpoint;
            network = bestNetwork;
            count = bestCount;
            return true;
        }

        // ── Phase 2: network as a beam source ────────────────────────────────
        //
        // Rather than keeping a network item virtual until Beam picks it up (which
        // would require patching Beam's internal spawned-item checks buried in an
        // anonymous tick closure), we extract the item from the network and spawn
        // it at the source interface's interaction cell up front, before handing
        // Beam a transfer. From that point it is an ordinary spawned Thing, so all
        // of Beam's own pickup/transport/deposit logic runs completely unmodified.
        // A transfer that never completes just leaves an ordinary item sitting at
        // the interface - safe, if less tidy than an instant return to storage.

        private static void FillQueueWithNetworkExtractions(
            Map map, IntVec3 origin, int ownerKey, int desiredCount, IList destinationQueue,
            HashSet<Thing> excludedThings, HashSet<IntVec3> excludedDestinations,
            Pawn pawn, Faction faction, bool manual)
        {
            if (map == null || destinationQueue == null || excludedThings == null || excludedDestinations == null)
                return;
            if (destinationQueue.Count >= desiredCount)
                return;

            List<Thing> candidates = new List<Thing>();
            Dictionary<Thing, DataNetwork> networkByItem = new Dictionary<Thing, DataNetwork>();
            Dictionary<DataNetwork, NetworkBuildingNetworkInterface> interfaceByNetwork = new Dictionary<DataNetwork, NetworkBuildingNetworkInterface>();

            foreach (DataNetwork network in NetworkItemSearchUtility.Networks(map))
            {
                NetworkBuildingNetworkInterface bestInterface = FindBestExtractionInterface(network, pawn, faction, manual);
                if (bestInterface == null) continue;
                interfaceByNetwork[network] = bestInterface;

                foreach (Thing item in network.StoredItems)
                {
                    if (excludedThings.Contains(item)) continue;
                    if (!IsEligibleExtractionCandidate(item, map, pawn, faction, manual, ownerKey)) continue;
                    candidates.Add(item);
                    networkByItem[item] = network;
                }
            }

            if (candidates.Count == 0) return;

            candidates.Sort((a, b) =>
            {
                float da = (interfaceByNetwork[networkByItem[a]].InteractionCell - origin).LengthHorizontalSquared;
                float db = (interfaceByNetwork[networkByItem[b]].InteractionCell - origin).LengthHorizontalSquared;
                return da.CompareTo(db);
            });

            for (int i = 0; i < candidates.Count; i++)
            {
                if (destinationQueue.Count >= desiredCount) break;
                Thing item = candidates[i];
                DataNetwork network = networkByItem[item];
                TryQueueNetworkExtraction(item, network, interfaceByNetwork[network], map, ownerKey, pawn, faction, manual,
                    destinationQueue, excludedThings, excludedDestinations);
            }
        }

        private static NetworkBuildingNetworkInterface FindBestExtractionInterface(
            DataNetwork network, Pawn pawn, Faction faction, bool manual)
        {
            NetworkBuildingNetworkInterface best = null;
            foreach (NetworkBuildingNetworkInterface iface in network.NetworkInterfaces)
            {
                if (iface == null || iface.Destroyed || !iface.Spawned) continue;
                if (manual ? iface.IsForbidden(pawn) : iface.IsForbidden(faction)) continue;
                best = iface;
                break;
            }
            return best;
        }

        private static bool IsEligibleExtractionCandidate(
            Thing item, Map map, Pawn pawn, Faction faction, bool manual, int ownerKey)
        {
            if (item == null || item.Destroyed || item.stackCount <= 0) return false;
            if (item is Pawn || item is Corpse || item is MinifiedThing) return false;
            if (!item.def.EverHaulable || item.def.isUnfinishedThing) return false;
            if (IsThingClaimedByOtherWrapper(item, ownerKey)) return false;

            if (manual)
            {
                return pawn != null && NetworkItemSearchUtility.PawnCanUseNetworkItemForHaul(pawn, item, forced: false);
            }

            if (item.IsForbidden(faction)) return false;
            return !map.reservationManager.IsReservedAndRespected(item, faction);
        }

        private static bool TryQueueNetworkExtraction(
            Thing item, DataNetwork network, NetworkBuildingNetworkInterface sourceInterface, Map map,
            int ownerKey, Pawn pawn, Faction faction, bool manual,
            IList destinationQueue, HashSet<Thing> excludedThings, HashSet<IntVec3> excludedDestinations)
        {
            int desiredExtractCount = manual
                ? NetworkItemSearchUtility.GetReservableNetworkStackCount(pawn, item, 1, item.stackCount)
                : item.stackCount;
            if (desiredExtractCount <= 0) return false;

            StoragePriority sourcePriority = network.StorageSettings?.Priority ?? StoragePriority.Unstored;
            Faction actingFaction = manual ? pawn.Faction : faction;

            if (!StoreUtility.TryFindBestBetterStorageFor(item, null, map, sourcePriority, actingFaction,
                    out IntVec3 foundCell, out IHaulDestination haulDestination))
            {
                return false;
            }

            // Never extract into another network endpoint - that's a lateral move, not extraction.
            if (haulDestination is NetworkBuildingNetworkInterface || haulDestination is NetworkBuildingNetworkChute
                || haulDestination is NetworkBuildingController)
            {
                return false;
            }

            Thing containerThing = null;
            if (!foundCell.IsValid)
            {
                containerThing = haulDestination as Thing;
                if (containerThing == null) return false;
                ThingOwner containerOwner = containerThing.TryGetInnerInteractableThingOwner();
                if (containerOwner == null) return false;
                int containerCap = containerOwner.GetCountCanAccept(item);
                if (containerCap <= 0) return false;
                desiredExtractCount = Mathf.Min(desiredExtractCount, containerCap);
            }
            else if (excludedDestinations != null && excludedDestinations.Contains(foundCell))
            {
                return false;
            }

            // Network stacks aren't bound by the item's normal stack limit, but a
            // spawned Thing is - GenSpawn.Spawn truncates (and logs an error) if we
            // hand it more than def.stackLimit. Cap here so the remainder is simply
            // left in the network for a later extraction pass.
            int count = Mathf.Min(desiredExtractCount, Mathf.Min(item.stackCount, item.def.stackLimit));
            if (count <= 0) return false;

            Thing extracted = ExtractFromNetworkController(network, item, count);
            if (extracted == null) return false;

            IntVec3 spawnCell = sourceInterface.InteractionCell.IsValid ? sourceInterface.InteractionCell : sourceInterface.Position;
            GenSpawn.Spawn(extracted, spawnCell, map);

            object transfer = containerThing != null
                ? BeamTransferCtor.Invoke(new object[] { extracted, spawnCell, containerThing, count })
                : BeamTransferCtorCell.Invoke(new object[] { extracted, spawnCell, foundCell, count });

            if (!TryClaimThingWrapper(extracted, ownerKey))
                return false; // extracted item is left spawned at the interface - safe, just an ordinary item now.

            ThingClaimedField.SetValue(transfer, true);

            bool destinationClaimed = containerThing != null
                ? TryClaimDestinationContainerWrapper(transfer, ownerKey)
                : TryClaimStorageDestinationWrapper(transfer, ownerKey);

            if (!destinationClaimed)
            {
                ReleaseClaimWrapper(transfer, ownerKey);
                return false;
            }

            destinationQueue.Add(transfer);
            excludedThings.Add(extracted);
            excludedDestinations.Add(containerThing != null ? containerThing.Position : foundCell);

            return true;
        }

        private static Thing ExtractFromNetworkController(DataNetwork network, Thing item, int count)
        {
            ControllerItemOwner container = network.ActiveController?.innerContainer;
            if (container == null) return null;

            Thing extracted;
            if (count >= item.stackCount)
            {
                if (!container.Remove(item)) return null;
                extracted = item;
            }
            else
            {
                extracted = item.SplitOff(count);
            }

            network.MarkBytesDirty();
            return extracted;
        }
    }
}
