using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public class NetworkPowerState : IExposable
    {
        private const int DefaultReserveChargingDrawWatts = 300;
        private const int MaxReserveChargingDrawWatts = 10000;
        private const int ChargeStepWatts = 50;

        private DataNetwork owner;
        private float storedEnergyWd;
        private float cachedMaxEnergyWd;
        private int cachedRequiredPowerDrawWatts;
        private int reserveChargingDrawWatts = DefaultReserveChargingDrawWatts;
        private int currentRequestedChargingDrawWatts;
        private NetworkPowerMode mode = NetworkPowerMode.NoController;

        public float StoredEnergyWd => storedEnergyWd;
        public float MaxEnergyWd => cachedMaxEnergyWd;
        public int BuildingPowerDrawWatts => cachedRequiredPowerDrawWatts;
        public int StoredItemPowerDrawWatts => CalculateStoredItemPowerDrawWatts();
        public int RequiredPowerDrawWatts => cachedRequiredPowerDrawWatts + StoredItemPowerDrawWatts;
        public int ReserveChargingDrawWatts => reserveChargingDrawWatts;
        public int CurrentRequestedChargingDrawWatts => currentRequestedChargingDrawWatts;
        public NetworkPowerMode Mode => mode;
        public int MaxReserveChargingDraw => MaxReserveChargingDrawWatts;
        public int ReserveChargeStep => ChargeStepWatts;
        public bool IsOperational => mode == NetworkPowerMode.GridPowered || mode == NetworkPowerMode.ReservePowered;
        public float ReserveFillPercent => cachedMaxEnergyWd > 0f ? Mathf.Clamp01(storedEnergyWd / cachedMaxEnergyWd) : 0f;

        public void Initialize(DataNetwork network)
        {
            owner = network;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref storedEnergyWd, "storedEnergyWd", 0f);
            Scribe_Values.Look(ref reserveChargingDrawWatts, "reserveChargingDrawWatts", DefaultReserveChargingDrawWatts);
            Scribe_Values.Look(ref mode, "powerMode", NetworkPowerMode.NoController);
        }

        public void RebuildAfterLoad()
        {
            RecalculatePowerDemand();
            RecalculateReserveCapacity();
            ClampStoredEnergy();
            RefreshControllerPowerOutput();
        }

        public void NotifyBuildingAdded(NetworkBuilding building)
        {
            RecalculatePowerDemand();
            RecalculateReserveCapacity();
            ClampStoredEnergy();
            RefreshControllerPowerOutput();
        }

        public void NotifyBuildingRemoved(NetworkBuilding building)
        {
            RecalculatePowerDemand();
            RecalculateReserveCapacity();
            ClampStoredEnergy();
            RefreshControllerPowerOutput();
        }

        public void RecalculatePowerDemand()
        {
            cachedRequiredPowerDrawWatts = 0;
            if (owner.Buildings == null)
            {
                return;
            }

            for (int i = 0; i < owner.Buildings.Count; i++)
            {
                NetworkBuilding building = owner.Buildings[i];

                CompNetworkBuilding comp = building.GetComp<CompNetworkBuilding>();
                if (comp != null)
                {
                    cachedRequiredPowerDrawWatts += comp.PowerUsageWatts;
                }
            }
        }

        public void RecalculateReserveCapacity()
        {
            cachedMaxEnergyWd = 0f;
            if (owner.Buildings == null)
            {
                return;
            }

            for (int i = 0; i < owner.Buildings.Count; i++)
            {
                NetworkBuilding building = owner.Buildings[i];

                CompNetworkPowerStorage storage = building.GetComp<CompNetworkPowerStorage>();
                if (storage != null)
                {
                    cachedMaxEnergyWd += storage.CapacityWd;
                }
            }
        }

        public void SetReserveChargingDrawWatts(int watts)
        {
            reserveChargingDrawWatts = Mathf.Clamp(RoundToStep(watts), 0, MaxReserveChargingDrawWatts);
            RecomputeChargingRequest();
            RefreshControllerPowerOutput();
            owner.RefreshUIForPower();
        }

        public void SetStoredEnergy(float value)
        {
            storedEnergyWd = Mathf.Clamp(value, 0f, cachedMaxEnergyWd);
            owner.RefreshUIForPower();
        }

        public void AbsorbFrom(NetworkPowerState other)
        {
            if (other == null || other == this)
            {
                return;
            }

            storedEnergyWd += other.storedEnergyWd;
            other.storedEnergyWd = 0f;
            RecalculateReserveCapacity();
            ClampStoredEnergy();
            RecomputeChargingRequest();
            RefreshControllerPowerOutput();
        }

        public void Update(int deltaTicks)
        {
            if (deltaTicks <= 0)
            {
                return;
            }

            NetworkPowerMode previousMode = mode;
            CompNetworkPowerController powerComp = GetActiveControllerPowerComp();

            if (powerComp == null)
            {
                mode = owner.ActiveController == null ? NetworkPowerMode.NoController : NetworkPowerMode.Offline;
                currentRequestedChargingDrawWatts = 0;
                NotifyModeChangedIfNeeded(previousMode);
                return;
            }

            if (!powerComp.ControllerCanUseReservePower())
            {
                mode = NetworkPowerMode.ControllerDisabled;
                currentRequestedChargingDrawWatts = 0;
                powerComp.RefreshPowerOutput();
                NotifyModeChangedIfNeeded(previousMode);
                return;
            }

            if (powerComp.PowerOn)
            {
                mode = NetworkPowerMode.GridPowered;
                ChargeReserve(deltaTicks, powerComp);
                RecomputeChargingRequest(powerComp);
                powerComp.RefreshPowerOutput();
                NotifyModeChangedIfNeeded(previousMode);
                return;
            }

            currentRequestedChargingDrawWatts = 0;

            if (RequiredPowerDrawWatts <= 0)
            {
                mode = NetworkPowerMode.ReservePowered;
            }
            else if (storedEnergyWd > 0f)
            {
                mode = NetworkPowerMode.ReservePowered;
                DrainReserve(deltaTicks);
                if (storedEnergyWd <= 0f)
                {
                    mode = NetworkPowerMode.Offline;
                }
            }
            else
            {
                mode = NetworkPowerMode.Offline;
            }

            powerComp.RefreshPowerOutput();
            NotifyModeChangedIfNeeded(previousMode);
        }

        public void RefreshControllerPowerOutput()
        {
            GetActiveControllerPowerComp()?.RefreshPowerOutput();
        }

        public int EstimateReserveRuntimeTicks()
        {
            int requiredPowerDrawWatts = RequiredPowerDrawWatts;
            if (requiredPowerDrawWatts <= 0)
            {
                return int.MaxValue;
            }

            float energyPerTick = requiredPowerDrawWatts * CompPower.WattsToWattDaysPerTick;
            if (energyPerTick <= 0f)
            {
                return int.MaxValue;
            }

            return Mathf.FloorToInt(storedEnergyWd / energyPerTick);
        }

        private void ChargeReserve(int deltaTicks, CompNetworkPowerController powerComp)
        {
            if (currentRequestedChargingDrawWatts <= 0 || cachedMaxEnergyWd <= 0f)
            {
                return;
            }

            float added = currentRequestedChargingDrawWatts
                * CompPower.WattsToWattDaysPerTick
                * deltaTicks
                * Mathf.Max(0f, powerComp.NetworkPowerProps.chargeEfficiency);
            storedEnergyWd = Mathf.Min(cachedMaxEnergyWd, storedEnergyWd + added);
        }

        private void DrainReserve(int deltaTicks)
        {
            float required = RequiredPowerDrawWatts * CompPower.WattsToWattDaysPerTick * deltaTicks;
            storedEnergyWd = Mathf.Max(0f, storedEnergyWd - required);
        }

        private int CalculateStoredItemPowerDrawWatts()
        {
            if (!ModSettings.EnableStoredItemPowerDraw)
            {
                return 0;
            }

            int usedBytes = owner.UsedBytes;
            if (usedBytes <= 0)
            {
                return 0;
            }

            return ((usedBytes + 99) / 100) * ModSettings.StoredItemPowerDrawPer100Bytes;
        }

        private void RecomputeChargingRequest(CompNetworkPowerController powerComp = null)
        {
            if (mode != NetworkPowerMode.GridPowered || reserveChargingDrawWatts <= 0 || cachedMaxEnergyWd <= 0f || storedEnergyWd >= cachedMaxEnergyWd)
            {
                currentRequestedChargingDrawWatts = 0;
                return;
            }

            powerComp = powerComp ?? GetActiveControllerPowerComp();
            if (powerComp == null || powerComp.PowerNet == null)
            {
                currentRequestedChargingDrawWatts = 0;
                return;
            }

            float netWatts = powerComp.PowerNet.CurrentEnergyGainRate() / CompPower.WattsToWattDaysPerTick;
            int availableOptionalWatts = Mathf.FloorToInt(netWatts + currentRequestedChargingDrawWatts - powerComp.NetworkPowerProps.chargingSurplusBufferWatts);
            if (availableOptionalWatts <= 0)
            {
                currentRequestedChargingDrawWatts = 0;
                return;
            }

            currentRequestedChargingDrawWatts = Mathf.Clamp(
                RoundToStep(Mathf.Min(reserveChargingDrawWatts, availableOptionalWatts)),
                0,
                reserveChargingDrawWatts);
        }

        private void ClampStoredEnergy()
        {
            if (cachedMaxEnergyWd <= 0f)
            {
                storedEnergyWd = 0f;
            }
            else if (storedEnergyWd > cachedMaxEnergyWd)
            {
                storedEnergyWd = cachedMaxEnergyWd;
            }
            else if (storedEnergyWd < 0f)
            {
                storedEnergyWd = 0f;
            }
        }

        private CompNetworkPowerController GetActiveControllerPowerComp()
        {
            NetworkBuildingController controller = owner.ActiveController;
            if (controller == null || controller.Destroyed)
            {
                return null;
            }

            return controller.GetComp<CompNetworkPowerController>();
        }

        private void NotifyModeChangedIfNeeded(NetworkPowerMode previousMode)
        {
            if (previousMode != mode)
            {
                owner.NotifyPowerModeChanged(previousMode, mode);
            }
            else
            {
                owner.RefreshUIForPower();
            }
        }

        private static int RoundToStep(int value)
        {
            return Mathf.RoundToInt((float)value / ChargeStepWatts) * ChargeStepWatts;
        }
    }
}
