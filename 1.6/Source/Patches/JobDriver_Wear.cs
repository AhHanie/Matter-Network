using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobDriver_Wear
    {
        [HarmonyPatch(typeof(JobDriver_Wear), "MakeNewToils")]
        public static class MakeNewToils
        {
            public static IEnumerable<Toil> Postfix(IEnumerable<Toil> values, JobDriver_Wear __instance)
            {
                bool firstToil = true;
                foreach (Toil toil in values)
                {
                    if (firstToil)
                    {
                        WrapFirstToilInitAction(toil, __instance);
                        firstToil = false;
                    }

                    yield return toil;
                }
            }
        }

        private static void WrapFirstToilInitAction(Toil toil, JobDriver_Wear driver)
        {
            Action originalInitAction = toil.initAction;
            toil.initAction = delegate
            {
                if (TryFindNetworkInterfaceForCurrentTarget(driver, out NetworkBuildingNetworkInterface networkInterface))
                {
                    Pawn pawn = driver.pawn;
                    if (pawn.Position == networkInterface.InteractionCell)
                    {
                        pawn.pather.StopDead();
                        pawn.jobs.curDriver.ReadyForNextToil();
                    }
                    else
                    {
                        pawn.pather.StartPath(networkInterface.InteractionCell, PathEndMode.OnCell);
                    }
                    return;
                }

                originalInitAction?.Invoke();
            };
        }

        private static bool TryFindNetworkInterfaceForCurrentTarget(
            JobDriver_Wear driver,
            out NetworkBuildingNetworkInterface networkInterface)
        {
            networkInterface = null;
            if (!driver.job.targetA.HasThing)
            {
                return false;
            }

            Apparel apparel = driver.job.targetA.Thing as Apparel;
            if (apparel?.MapHeld == null)
            {
                return false;
            }

            NetworksMapComponent mapComp = apparel.MapHeld.GetComponent<NetworksMapComponent>();
            if (!mapComp.TryGetItemNetwork(apparel, out DataNetwork network))
            {
                return false;
            }

            networkInterface = Patch_Toils_Goto.GotoThing.FindClosestReachableInterface(driver.pawn, network);
            return networkInterface != null;
        }
    }
}
