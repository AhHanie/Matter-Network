using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Toils_Goto
    {
        [HarmonyPatch(typeof(Toils_Goto), "GotoThing", new Type[] { typeof(TargetIndex), typeof(PathEndMode), typeof(bool) })]
        public static class GotoThing
        {
            public static void Postfix(ref Toil __result, TargetIndex ind, PathEndMode peMode)
            {
                Toil originalToil = __result;
                Action originalInitAction = originalToil.initAction;

                __result.initAction = delegate
                {
                    Pawn actor = originalToil.actor;
                    LocalTargetInfo dest = actor.jobs.curJob.GetTarget(ind);
                    Thing thing = dest.Thing;

                    if (thing == null || actor.Map == null)
                    {
                        originalInitAction();
                        return;
                    }

                    if (!NetworkItemSearchUtility.TryResolveNetworkItem(actor.Map, thing, out DataNetwork network))
                    {
                        originalInitAction();
                        return;
                    }

                    if (!network.IsOperational)
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                        return;
                    }

                    originalToil.debugName = "GotoCell";

                    // Toil objects are drawn from a global, shared pool (SimplePool<Toil> via
                    // ToilMaker), and under heavy concurrent hauling we've observed this toil's
                    // defaultCompleteMode read back as Never instead of the PatherArrival that
                    // Toils_Goto.GotoThing sets at creation - matching the mode used by an unrelated
                    // later toil in this same job's chain (PossiblyDelay). When that happens,
                    // Notify_PatherArrived becomes a no-op and the pawn never advances past this toil
                    // even though it did arrive. Re-assert the correct mode every time this initAction
                    // runs, right before we rely on it, regardless of how it got clobbered.
                    originalToil.defaultCompleteMode = ToilCompleteMode.PatherArrival;

                    NetworkBuildingNetworkInterface closestInterface = FindClosestReachableInterface(actor, network);

                    if (closestInterface != null)
                    {
                        Logger.Message($"Redirecting {actor.LabelShort} to network interface at {closestInterface.Position} to retrieve {thing.LabelShort}");
                        PathEndMode interfacePeMode = peMode == PathEndMode.InteractionCell ? PathEndMode.OnCell : peMode;

                        if (actor.Position == closestInterface.InteractionCell)
                        {
                            actor.pather.StopDead();
                            actor.jobs.curDriver.ReadyForNextToil();
                        }
                        else
                        {
                            actor.pather.StartPath(closestInterface.InteractionCell, interfacePeMode);
                        }
                    }
                    else
                    {
                        Logger.Warning($"No reachable network interface found for {actor.LabelShort} to retrieve {thing.LabelShort}");
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    }
                };
            }

            public static NetworkBuildingNetworkInterface FindClosestReachableInterface(Pawn pawn, DataNetwork network)
            {
                if (!network.IsOperational)
                {
                    return null;
                }

                List<NetworkBuildingNetworkInterface> interfaces = network.NetworkInterfaces;
                NetworkBuildingNetworkInterface closestInterface = null;
                float closestDistSquared = float.MaxValue;

                foreach (NetworkBuildingNetworkInterface interf in interfaces)
                {
                    if (pawn.CanReach(interf.InteractionCell, PathEndMode.OnCell, Danger.Deadly))
                    {
                        float distSquared = (pawn.Position - interf.InteractionCell).LengthHorizontalSquared;
                        if (distSquared < closestDistSquared)
                        {
                            closestDistSquared = distSquared;
                            closestInterface = interf;
                        }
                    }
                }

                return closestInterface;
            }
        }
    }
}
