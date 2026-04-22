using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobDriver_Refuel
    {
        private const int SharedFuelReservationMaxPawns = 10;

        [HarmonyPatch(typeof(JobDriver_Refuel), nameof(JobDriver_Refuel.TryMakePreToilReservations))]
        public static class TryMakePreToilReservations
        {
            public static bool Prefix(JobDriver_Refuel __instance, bool errorOnFailed, ref bool __result)
            {
                LocalTargetInfo fuelTarget = __instance.job.GetTarget(TargetIndex.B);
                if (!IsNetworkFuelTarget(__instance.pawn, fuelTarget))
                {
                    return true;
                }

                LocalTargetInfo refuelableTarget = __instance.job.GetTarget(TargetIndex.A);
                if (!__instance.pawn.Reserve(refuelableTarget, __instance.job, 1, -1, null, errorOnFailed))
                {
                    __result = false;
                    return false;
                }

                int fuelCountToReserve = ResolveFuelReservationCount(__instance.pawn, __instance.job, fuelTarget);
                __result = fuelCountToReserve > 0
                    && __instance.pawn.Reserve(fuelTarget, __instance.job, SharedFuelReservationMaxPawns, fuelCountToReserve, null, errorOnFailed);
                return false;
            }
        }

        [HarmonyPatch]
        public static class MakeNewToils
        {
            public static MethodBase TargetMethod()
            {
                MethodInfo makeNewToils = AccessTools.Method(typeof(JobDriver_Refuel), "MakeNewToils");
                return AccessTools.EnumeratorMoveNext(makeNewToils);
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var reserveMethod = AccessTools.Method(typeof(Toils_Reserve), nameof(Toils_Reserve.Reserve), new[] { typeof(TargetIndex), typeof(int), typeof(int), typeof(ReservationLayerDef), typeof(bool) });
                var customReserveMethod = AccessTools.Method(typeof(Patch_JobDriver_Refuel), nameof(MakeSharedFuelReserveToil));
                int replacements = 0;

                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.opcode == OpCodes.Call && instruction.operand is System.Reflection.MethodInfo method && method == reserveMethod)
                    {
                        replacements++;
                        yield return new CodeInstruction(OpCodes.Call, customReserveMethod);
                        continue;
                    }

                    yield return instruction;
                }

                if (replacements > 0)
                {
                    Logger.Message($"Patched JobDriver_Refuel.MakeNewToils transpiler successfully. Replaced {replacements} reserve toil call(s).");
                }
                else
                {
                    Logger.Error("Failed to patch JobDriver_Refuel.MakeNewToils transpiler: no reserve toil call was replaced.");
                }
            }
        }

        [HarmonyPatch(typeof(JobDriver_RefuelAtomic), nameof(JobDriver_RefuelAtomic.TryMakePreToilReservations))]
        public static class TryMakePreToilReservationsAtomic
        {
            public static bool Prefix(JobDriver_RefuelAtomic __instance, bool errorOnFailed, ref bool __result)
            {
                List<LocalTargetInfo> fuelQueue = __instance.job.GetTargetQueue(TargetIndex.B);
                if (fuelQueue.NullOrEmpty() || !ContainsNetworkFuelTarget(__instance.pawn, fuelQueue))
                {
                    return true;
                }

                ReserveFuelQueue(__instance.pawn, __instance.job, fuelQueue);
                __result = __instance.pawn.Reserve(__instance.job.GetTarget(TargetIndex.A), __instance.job, 1, -1, null, errorOnFailed);
                return false;
            }
        }

        public static Toil MakeSharedFuelReserveToil(TargetIndex ind, int maxPawns = 1, int stackCount = -1, ReservationLayerDef layer = null, bool ignoreOtherReservations = false)
        {
            Toil toil = ToilMaker.MakeToil("MN_ReserveSharedFuel");
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job curJob = actor?.CurJob;
                if (actor == null || curJob == null)
                {
                    return;
                }

                LocalTargetInfo fuelTarget = curJob.GetTarget(ind);
                if (!IsNetworkFuelTarget(actor, fuelTarget))
                {
                    if (!actor.Reserve(fuelTarget, curJob, maxPawns, stackCount, layer, errorOnFailed: false, ignoreOtherReservations))
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    }

                    return;
                }

                int fuelCountToReserve = ResolveFuelReservationCount(actor, curJob, fuelTarget);
                if (fuelCountToReserve <= 0
                    || !actor.Reserve(fuelTarget, curJob, SharedFuelReservationMaxPawns, fuelCountToReserve, layer, errorOnFailed: false, ignoreOtherReservations))
                {
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.atomicWithPrevious = true;
            return toil;
        }

        private static void ReserveFuelQueue(Pawn pawn, Job job, List<LocalTargetInfo> fuelQueue)
        {
            int remainingFuelToReserve = GetDesiredFuelCount(job);
            for (int i = 0; i < fuelQueue.Count; i++)
            {
                LocalTargetInfo fuelTarget = fuelQueue[i];
                if (!fuelTarget.HasThing)
                {
                    continue;
                }

                if (!IsNetworkFuelTarget(pawn, fuelTarget))
                {
                    pawn.Map.reservationManager.Reserve(
                        pawn,
                        job,
                        fuelTarget,
                        1,
                        -1,
                        null,
                        errorOnFailed: false,
                        ignoreOtherReservations: false,
                        canReserversStartJobs: false);
                    continue;
                }

                int fuelCountToReserve = ResolveFuelReservationCount(pawn, job, fuelTarget, remainingFuelToReserve);
                if (fuelCountToReserve <= 0)
                {
                    continue;
                }

                pawn.Map.reservationManager.Reserve(
                    pawn,
                    job,
                    fuelTarget,
                    SharedFuelReservationMaxPawns,
                    fuelCountToReserve,
                    null,
                    errorOnFailed: false,
                    ignoreOtherReservations: false,
                    canReserversStartJobs: false);

                remainingFuelToReserve -= fuelCountToReserve;
                if (remainingFuelToReserve <= 0)
                {
                    return;
                }
            }
        }

        private static bool ContainsNetworkFuelTarget(Pawn pawn, List<LocalTargetInfo> fuelQueue)
        {
            for (int i = 0; i < fuelQueue.Count; i++)
            {
                if (IsNetworkFuelTarget(pawn, fuelQueue[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNetworkFuelTarget(Pawn pawn, LocalTargetInfo fuelTarget)
        {
            if (pawn?.Map == null || !fuelTarget.HasThing)
            {
                return false;
            }

            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            return mapComp.TryGetItemNetwork(fuelTarget.Thing, out _);
        }

        private static int ResolveFuelReservationCount(Pawn pawn, Job job, LocalTargetInfo fuelTarget, int? desiredFuelCountOverride = null)
        {
            if (pawn?.Map == null || !fuelTarget.HasThing)
            {
                return 0;
            }

            int desiredFuelCount = desiredFuelCountOverride ?? GetDesiredFuelCount(job);
            if (desiredFuelCount <= 0)
            {
                return 0;
            }

            int reservableFuelCount = pawn.Map.reservationManager.CanReserveStack(pawn, fuelTarget, SharedFuelReservationMaxPawns);
            if (reservableFuelCount <= 0)
            {
                return 0;
            }

            return System.Math.Min(desiredFuelCount, reservableFuelCount);
        }

        private static int GetDesiredFuelCount(Job job)
        {
            if (job == null)
            {
                return 0;
            }

            if (job.count > 0)
            {
                return job.count;
            }

            Thing refuelable = job.GetTarget(TargetIndex.A).Thing;
            CompRefuelable compRefuelable = refuelable?.TryGetComp<CompRefuelable>();
            return compRefuelable?.GetFuelCountToFullyRefuel() ?? 0;
        }
    }
}
