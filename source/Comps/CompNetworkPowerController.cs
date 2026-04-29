using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public class CompProperties_NetworkPowerController : CompProperties_Power
    {
        public int updateIntervalTicks = 60;
        public float chargeEfficiency = 1f;
        public int chargingSurplusBufferWatts = 50;

        public CompProperties_NetworkPowerController()
        {
            compClass = typeof(CompNetworkPowerController);
        }
    }

    public class CompNetworkPowerController : CompPowerTrader
    {
        public CompProperties_NetworkPowerController NetworkPowerProps => (CompProperties_NetworkPowerController)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            RefreshPowerOutput();
            NotifyNetworkPowerStateChanged();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            NotifyNetworkPowerStateChanged();
        }

        public override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);
            NotifyNetworkPowerStateChanged();
        }

        public bool ControllerCanUseReservePower()
        {
            if (!(parent is NetworkBuildingController controller))
            {
                return false;
            }

            if (controller.ControllerConflictDisabled || controller.Destroyed || !controller.Spawned)
            {
                return false;
            }

            return FlickUtility.WantsToBeOn(parent) && !parent.IsBrokenDown();
        }

        public void RefreshPowerOutput()
        {
            float desiredPowerOutput = 0f;

            if (parent is NetworkBuildingController controller
                && controller.ParentNetwork != null
                && controller.ParentNetwork.ActiveController == controller
                && ControllerCanUseReservePower())
            {
                desiredPowerOutput = -Mathf.Max(
                    0,
                    controller.ParentNetwork.RequiredPowerDrawWatts + controller.ParentNetwork.CurrentRequestedChargingDrawWatts);
            }

            if (!Mathf.Approximately(PowerOutput, desiredPowerOutput))
            {
                PowerOutput = desiredPowerOutput;
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!(parent is NetworkBuildingController controller) || controller.ParentNetwork == null)
            {
                return base.CompInspectStringExtra();
            }

            DataNetwork network = controller.ParentNetwork;
            string powerText = "MN_ControllerPowerInspect".Translate(
                network.PowerModeLabel,
                network.RequiredPowerDrawWatts,
                network.CurrentRequestedChargingDrawWatts,
                network.StoredReserveEnergyWd.ToString("F0"),
                network.MaxReserveEnergyWd.ToString("F0"));

            string baseText = base.CompInspectStringExtra();
            if (string.IsNullOrEmpty(baseText))
            {
                return powerText;
            }

            return powerText + "\n" + baseText;
        }

        private void NotifyNetworkPowerStateChanged()
        {
            if (parent is NetworkBuildingController controller)
            {
                controller.ParentNetwork?.NotifyPowerControllerStateChanged();
            }
        }
    }
}
