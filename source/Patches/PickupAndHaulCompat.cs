using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class PickupAndHaulCompat
    {
        private const string JobDriverTypeName = "PickUpAndHaul.JobDriver_HaulToInventory";

        private static readonly Type jobDriverType = AccessTools.TypeByName(JobDriverTypeName);

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
    }
}
