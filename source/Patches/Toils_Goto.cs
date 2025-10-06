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

                    if (thing == null || thing.MapHeld == null)
                    {
                        originalInitAction();
                        return;
                    }

                    NetworksMapComponent mapComp = thing.MapHeld.GetComponent<NetworksMapComponent>();
                    if (mapComp == null || !mapComp.TryGetItemNetwork(thing, out DataNetwork network))
                    {
                        originalInitAction();
                        return;
                    }

                    originalToil.debugName = "GotoCell";
                    NetworkBuildingNetworkInterface closestInterface = FindClosestReachableInterface(actor, network);

                    if (closestInterface != null)
                    {
                        Log.Message($"Redirecting {actor.LabelShort} to network interface at {closestInterface.Position} to retrieve {thing.LabelShort}");

                        if (actor.Position == closestInterface.InteractionCell)
                        {
                            actor.pather.StopDead();
                            actor.jobs.curDriver.ReadyForNextToil();
                        }
                        else
                        {
                            actor.pather.StartPath(closestInterface.InteractionCell, peMode);
                        }
                    }
                    else
                    {
                        Log.Warning($"No reachable network interface found for {actor.LabelShort} to retrieve {thing.LabelShort}");
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    }
                };
            }

            private static NetworkBuildingNetworkInterface FindClosestReachableInterface(Pawn pawn, DataNetwork network)
            {
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