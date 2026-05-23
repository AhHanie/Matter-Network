using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Toils_Haul
    {
        [HarmonyPatch(typeof(Toils_Haul), "ErrorCheckForCarry")]
        public static class ErrorCheckForCarry
        {
            public static bool Prefix(Pawn pawn, Thing haulThing, bool canTakeFromInventory, ref bool __result)
            {
                NetworksMapComponent mapComp = haulThing.MapHeld.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(haulThing, out DataNetwork network))
                {
                    return true;
                }

                if (!network.CanExtractItems)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    __result = true;
                    return false;
                }

                if (haulThing.stackCount == 0)
                {
                    Log.Message(pawn?.ToString() + " tried to start carry " + haulThing?.ToString() + " which had stackcount 0.");
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    __result = true;
                    return false;
                }

                if (pawn.jobs.curJob.count <= 0)
                {
                    Log.Error("Invalid count: " + pawn.jobs.curJob.count + ", setting to 1. Job was " + pawn.jobs.curJob);
                    pawn.jobs.curJob.count = 1;
                }

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.DepositHauledThingInContainer))]
        public static class DepositHauledThingInContainer
        {
            public static void Postfix(TargetIndex containerInd, Action onDeposited, ref Toil __result)
            {
                Toil toil = __result;
                Action originalInitAction = toil.initAction;
                toil.initAction = delegate
                {
                    Pawn actor = toil.actor;
                    Job curJob = actor.jobs.curJob;
                    Thing container = curJob.GetTarget(containerInd).Thing;

                    NetworkBuilding networkTarget = container as NetworkBuildingNetworkInterface;
                    if (networkTarget == null)
                    {
                        networkTarget = container as NetworkBuildingNetworkChute;
                    }

                    if (networkTarget == null)
                    {
                        originalInitAction?.Invoke();
                        return;
                    }

                    DataNetwork network = networkTarget.ParentNetwork;
                    ThingOwner thingOwner = network?.ActiveController?.innerContainer;
                    if (network == null || thingOwner == null)
                    {
                        originalInitAction?.Invoke();
                        return;
                    }

                    Thing carried = actor.carryTracker.CarriedThing;
                    if (carried == null)
                    {
                        Log.Error(actor?.ToString() + " tried to place hauled thing in network but is not hauling anything.");
                        return;
                    }

                    int acceptedCount = Math.Min(carried.stackCount, network.ControllerCanAcceptCount(carried));
                    if (acceptedCount > 0)
                    {
                        Thing carriedBeforeTransfer = carried;
                        int moved = actor.carryTracker.innerContainer.TryTransferToContainer(carried, thingOwner, acceptedCount);
                        if (moved > 0)
                        {
                            if (networkTarget is IHaulEnroute enroute)
                            {
                                actor.Map.enrouteManager.ReleaseFor(enroute, actor);
                            }

                            if (networkTarget is INotifyHauledTo notifyHauledTo)
                            {
                                notifyHauledTo.Notify_HauledTo(actor, carriedBeforeTransfer, moved);
                            }

                            if (curJob.def == JobDefOf.DoBill)
                            {
                                HaulAIUtility.UpdateJobWithPlacedThings(curJob, carriedBeforeTransfer, moved);
                            }

                            onDeposited?.Invoke();
                        }
                    }

                    carried = actor.carryTracker.CarriedThing;
                    if (carried != null && !carried.Destroyed)
                    {
                        actor.carryTracker.TryDropCarriedThing(networkTarget.Position, ThingPlaceMode.Near, out Thing _);
                    }
                };
            }
        }
    }
}
