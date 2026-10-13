using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse.AI;
using HarmonyLib;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobDriver_TakeInventory
    {
        private sealed class TakeInventoryDriverData
        {
            public NetworkBuildingNetworkInterface cachedNetworkInterface;
        }

        private static readonly ConditionalWeakTable<JobDriver_TakeInventory, TakeInventoryDriverData> DriverData =
            new ConditionalWeakTable<JobDriver_TakeInventory, TakeInventoryDriverData>();

        [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.ExposeData))]
        public static class ExposeData_Patch
        {
            public static void Postfix(JobDriver __instance)
            {
                if (!(__instance is JobDriver_TakeInventory takeInventoryDriver))
                {
                    return;
                }

                TakeInventoryDriverData data = GetData(takeInventoryDriver);
                Scribe_References.Look(ref data.cachedNetworkInterface, "mn_cachedNetworkInterface");
            }
        }

        [HarmonyPatch(typeof(JobDriver_TakeInventory), "MakeNewToils")]
        public static class MakeNewToils
        {
            public static IEnumerable<Toil> Postfix(IEnumerable<Toil> values, JobDriver_TakeInventory __instance)
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

        private static void WrapFirstToilInitAction(Toil toil, JobDriver_TakeInventory driver)
        {
            Action originalInitAction = toil.initAction;
            toil.initAction = delegate
            {
                if (TryGetCachedOrCurrentNetworkInterface(driver, out NetworkBuildingNetworkInterface networkInterface))
                {
                    driver.pawn.pather.StartPath(networkInterface, PathEndMode.ClosestTouch);
                    return;
                }

                originalInitAction?.Invoke();
            };
        }

        private static bool TryGetCachedOrCurrentNetworkInterface(
            JobDriver_TakeInventory driver,
            out NetworkBuildingNetworkInterface networkInterface)
        {
            networkInterface = null;
            TakeInventoryDriverData data = GetData(driver);
            if (data.cachedNetworkInterface != null && IsUsableNetworkInterface(driver.pawn, data.cachedNetworkInterface))
            {
                networkInterface = data.cachedNetworkInterface;
                return true;
            }

            if (!TryFindNetworkInterfaceForCurrentTarget(driver, out networkInterface))
            {
                data.cachedNetworkInterface = null;
                return false;
            }

            data.cachedNetworkInterface = networkInterface;
            return true;
        }

        private static bool TryFindNetworkInterfaceForCurrentTarget(
            JobDriver_TakeInventory driver,
            out NetworkBuildingNetworkInterface networkInterface)
        {
            networkInterface = null;
            if (!driver.job.targetA.HasThing)
            {
                return false;
            }

            Thing thing = driver.job.targetA.Thing;
            if (thing == null)
            {
                return false;
            }

            NetworksMapComponent mapComp = driver.pawn.Map.GetComponent<NetworksMapComponent>();
            if (!mapComp.TryGetItemNetwork(thing, out DataNetwork network))
            {
                return false;
            }

            networkInterface = Patch_Toils_Goto.GotoThing.FindClosestReachableInterface(driver.pawn, network);
            return networkInterface != null;
        }

        private static bool IsUsableNetworkInterface(Pawn pawn, NetworkBuildingNetworkInterface networkInterface)
        {
            if (networkInterface.Destroyed)
            {
                return false;
            }

            return pawn.CanReach(networkInterface, PathEndMode.ClosestTouch, Danger.Deadly);
        }

        private static TakeInventoryDriverData GetData(JobDriver_TakeInventory driver)
        {
            return DriverData.GetOrCreateValue(driver);
        }
    }
}
