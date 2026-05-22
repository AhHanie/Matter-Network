using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_WorkGiver_EmptyWasteContainer
    {
        [HarmonyPatch(typeof(WorkGiver_EmptyWasteContainer), nameof(WorkGiver_EmptyWasteContainer.JobOnThing))]
        public static class JobOnThing
        {
            public static void Postfix(Pawn pawn, Thing t, ref Job __result)
            {
                if (__result == null)
                {
                    return;
                }

                if (__result.GetTarget(TargetIndex.C).IsValid)
                {
                    return;
                }

                CompWasteProducer compWasteProducer = t.TryGetComp<CompWasteProducer>();
                Thing waste = compWasteProducer.Waste;

                if (!StoreUtility.TryFindBestBetterStorageFor(waste, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 foundCell, out IHaulDestination haulDestination))
                {
                    return;
                }

                if (foundCell.IsValid)
                {
                    __result.SetTarget(TargetIndex.C, foundCell);
                    return;
                }

                if (haulDestination is Thing haulDestinationThing)
                {
                    __result.SetTarget(TargetIndex.C, haulDestinationThing);
                }
            }
        }

        [HarmonyPatch(typeof(JobDriver_EmptyThingContainer), "MakeNewToils")]
        public static class MakeNewToils
        {
            public static void Postfix(JobDriver_EmptyThingContainer __instance, ref IEnumerable<Toil> __result)
            {
                if (!__instance.job.GetTarget(TargetIndex.C).HasThing)
                {
                    return;
                }

                __result = MakeNewToilsWithHaulDestination(__instance);
            }

            private static IEnumerable<Toil> MakeNewToilsWithHaulDestination(JobDriver_EmptyThingContainer driver)
            {
                Job job = driver.job;
                CompThingContainer comp = null;

                yield return Toils_Goto.GotoThing(TargetIndex.A, GetContainerPathEndMode(driver))
                    .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                    .FailOnSomeonePhysicallyInteracting(TargetIndex.A)
                    .FailOn(() => job.GetTarget(TargetIndex.A).Thing.TryGetComp(out comp) && comp.Empty);

                yield return Toils_General.WaitWhileExtractingContents(TargetIndex.A, TargetIndex.B, 120);

                yield return Toils_General.Do(delegate
                {
                    if (driver.TargetThingA.TryGetInnerInteractableThingOwner().TryDropAll(driver.pawn.Position, driver.Map, ThingPlaceMode.Near))
                    {
                        driver.TargetThingA.TryGetComp<CompThingContainer>()?.Props.dropEffecterDef?.Spawn(driver.TargetThingA, driver.Map).Cleanup();
                    }
                });

                yield return Toils_Reserve.Reserve(TargetIndex.B);
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.B)
                    .FailOnSomeonePhysicallyInteracting(TargetIndex.B);
                yield return Toils_Haul.StartCarryThing(TargetIndex.B, putRemainderInQueue: false, subtractNumTakenFromJobCount: true);
                yield return Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.C);
                yield return Toils_Haul.DepositHauledThingInContainer(TargetIndex.C, TargetIndex.C, null);
            }

            private static PathEndMode GetContainerPathEndMode(JobDriver_EmptyThingContainer driver)
            {
                Thing container = driver.job.GetTarget(TargetIndex.A).Thing;
                if (container == null || !container.def.hasInteractionCell)
                {
                    return PathEndMode.Touch;
                }

                return PathEndMode.InteractionCell;
            }
        }
    }
}
