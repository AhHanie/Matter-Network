using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class PickupAndHaulCompat
    {
        private const string JobDriverTypeName = "PickUpAndHaul.JobDriver_HaulToInventory";
        private const string UnloadJobDriverTypeName = "PickUpAndHaul.JobDriver_UnloadYourHauledInventory";

        private static readonly Type jobDriverType = AccessTools.TypeByName(JobDriverTypeName);
        private static readonly Type unloadJobDriverType = AccessTools.TypeByName(UnloadJobDriverTypeName);

        public struct TakeThingState
        {
            public Thing Thing;
            public DataNetwork Network;
            public int OriginalStackCount;
            public Pawn Pawn;
        }

        private static bool IsAvailable()
        {
            return ModsConfig.IsActive("mehni.pickupandhaul");
        }

        private static bool IsNetworkContainerTarget(LocalTargetInfo target)
        {
            Thing thing = target.Thing;
            return thing.def == BuildingDefOf.MN_NetworkInterface;
        }

        [HarmonyPatch]
        public static class JobDriverTryMakePreToilReservations
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsAvailable();
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(jobDriverType, nameof(JobDriver.TryMakePreToilReservations));
            }

            public static bool Prefix(JobDriver __instance, ref bool __result)
            {
                Pawn pawn = __instance.pawn;
                Job job = __instance.job;

                if (!job.targetB.HasThing || !IsNetworkContainerTarget(job.targetB))
                {
                    return true;
                }

                Logger.Message($"[Pickup and Haul] Skipping destination reservation for network container {job.targetB}.");

                pawn.ReserveAsManyAsPossible(job.targetQueueA, job);

                __result = pawn.Reserve(job.targetQueueA[0], job);
                return false;
            }
        }

        // PickUpAndHaul.JobDriver_UnloadYourHauledInventory reserves its destination directly via
        // ReservationManager.Reserve instead of going through TryMakePreToilReservations. Network
        // interfaces are IHaulEnroute (multi-pawn, capacity-tracked via Map.enrouteManager), not
        // exclusively-reservable containers, so that call can fail/contend when several pawns are
        // unloading into the same interface, causing JobDriver_UnloadYourHauledInventory to loop on
        // its Wait toil without ever making progress.
        [HarmonyPatch(typeof(ReservationManager), nameof(ReservationManager.Reserve))]
        public static class ReservationManager_Reserve
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsAvailable();
            }

            public static bool Prefix(Job job, LocalTargetInfo target, ref bool __result)
            {
                if (job?.def?.defName != "UnloadYourHauledInventory")
                {
                    return true;
                }

                if (!target.HasThing || !IsNetworkContainerTarget(target))
                {
                    return true;
                }

                __result = true;
                return false;
            }
        }

        // PickUpAndHaul.JobDriver_UnloadYourHauledInventory.FirstUnloadableThing has a pre-existing
        // bug: when the first candidate in carriedThings is no longer in the pawn's inventory (its
        // stack got merged and picked up a new thingID - PUAH's own comment acknowledges this), it
        // removes that stale entry from carriedThings but then unconditionally falls through to
        // `return new ThingCount(thing, ...)`, handing back the very reference it just discarded
        // instead of either the def-matched replacement or moving on to the next candidate.
        // FindTargetOrDrop then targets that stale Thing, and PullItemFromInventory sees it isn't in
        // pawn.inventory.innerContainer and JumpToToil(begin)s without unloading anything - repeating
        // every _unloadDuration ticks with no forward progress. This is independent of our
        // reservation-skip compat above; it can loop even when the destination reserves fine.
        [HarmonyPatch]
        public static class FirstUnloadableThing
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsAvailable();
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(unloadJobDriverType, "FirstUnloadableThing");
            }

            public static bool Prefix(Pawn pawn, HashSet<Thing> carriedThings, ref ThingCount __result)
            {
                ThingOwner innerPawnContainer = pawn.inventory.innerContainer;

                foreach (Thing thing in carriedThings.OrderBy(t => t.def.FirstThingCategory?.index).ThenBy(t => t.def.defName))
                {
                    if (innerPawnContainer.Contains(thing))
                    {
                        __result = new ThingCount(thing, thing.stackCount);
                        return false;
                    }

                    ThingDef stragglerDef = thing.def;
                    carriedThings.Remove(thing);

                    Thing replacement = null;
                    for (int i = 0; i < innerPawnContainer.Count; i++)
                    {
                        if (innerPawnContainer[i].def == stragglerDef)
                        {
                            replacement = innerPawnContainer[i];
                            break;
                        }
                    }

                    if (replacement != null)
                    {
                        __result = new ThingCount(replacement, replacement.stackCount);
                        return false;
                    }
                }

                __result = default;
                return false;
            }
        }
    }
}
