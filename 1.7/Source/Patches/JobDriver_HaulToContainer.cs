using System.Collections.Generic;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    // Early inbound-haul validation. A stale positive acceptance-cache entry must never let a
    // pawn pick up and carry an item to a network endpoint that will actually reject it. This adds
    // a first toil, before the pawn travels to fetch the item, that re-checks the exact acceptance
    // decision and cleanly ends an already-obsolete job.
    // The mandatory final deposit check (Patch_Toils_Haul.DepositHauledThingInContainer) still runs
    // regardless - this is purely an early-exit optimization plus a correctness self-heal, not a
    // replacement for it.
    public static class Patch_JobDriver_HaulToContainer
    {
        [HarmonyPatch(typeof(JobDriver_HaulToContainer), "MakeNewToils")]
        public static class MakeNewToils
        {
            public static void Postfix(ref IEnumerable<Toil> __result)
            {
                __result = PrependValidationToil(__result);
            }

            private static IEnumerable<Toil> PrependValidationToil(IEnumerable<Toil> originalToils)
            {
                yield return BuildValidationToil();
                foreach (Toil toil in originalToils)
                {
                    yield return toil;
                }
            }

            private static Toil BuildValidationToil()
            {
                Toil toil = ToilMaker.MakeToil("MN_ValidateAcceptanceBeforePickup");
                toil.initAction = delegate
                {
                    Pawn actor = toil.actor;
                    Job curJob = actor.jobs.curJob;
                    Thing container = curJob.GetTarget(TargetIndex.B).Thing;
                    Thing carryThing = curJob.GetTarget(TargetIndex.A).Thing;

                    if (!TryGetInboundNetwork(container, out DataNetwork network))
                    {
                        return;
                    }

                    if (carryThing == null || carryThing.Destroyed)
                    {
                        return;
                    }

                    if (!network.ValidateCachedCanAccept(carryThing, out bool cacheMismatch))
                    {
                        LogStaleAcceptanceIfNeeded(cacheMismatch, carryThing, container);
                        actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                    }
                };
                toil.defaultCompleteMode = ToilCompleteMode.Instant;
                return toil;
            }
        }

        // Resolves a HaulToContainer job's destination Thing to the DataNetwork it would deposit
        // into, for the three pawn-hauling Matter Network endpoints. Extraction is not covered here;
        // this validator only ever runs for inbound (container-target) jobs.
        internal static bool TryGetInboundNetwork(Thing container, out DataNetwork network)
        {
            switch (container)
            {
                case NetworkBuildingNetworkInterface iface:
                    network = iface.ParentNetwork;
                    return network != null;
                case NetworkBuildingNetworkChute chute:
                    network = chute.ParentNetwork;
                    return network != null;
                case NetworkBuildingController controller:
                    network = controller.ParentNetwork;
                    return network != null;
                default:
                    network = null;
                    return false;
            }
        }

        internal static void LogStaleAcceptanceIfNeeded(bool cacheMismatch, Thing carryThing, Thing container)
        {
            if (!cacheMismatch || !ModSettings.EnableLogging)
            {
                return;
            }

            Log.WarningOnce(
                $"[Matter Network] Acceptance cache mismatch: {carryThing.ToStringSafe()} was cached as accepted by {container.ToStringSafe()} but failed the live check; cache cleared and haul job ended before pickup.",
                (container.GetHashCode() * 397) ^ carryThing.def.GetHashCode());
        }
    }
}
