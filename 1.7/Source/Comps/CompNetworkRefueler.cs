using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public class CompProperties_NetworkRefueler : CompProperties
    {
        public float radius = 5.9f;
        public int defaultTickInterval = 3600;
        public int minTickInterval = 600;
        public int maxTickInterval = 18000;

        public CompProperties_NetworkRefueler()
        {
            compClass = typeof(CompNetworkRefueler);
        }
    }

    public class CompNetworkRefueler : ThingComp, INetworkTickable
    {
        private int tickInterval;
        private int nextTick;

        public CompProperties_NetworkRefueler Props => (CompProperties_NetworkRefueler)props;

        public int TickInterval
        {
            get => tickInterval;
            set => tickInterval = Mathf.Clamp(value, Props.minTickInterval, Props.maxTickInterval);
        }

        public float Radius => Props.radius;

        public void NetworkTick(int currentTick)
        {
            if (currentTick < nextTick)
            {
                return;
            }

            nextTick = currentTick + tickInterval;
            TryRefuelNearbyTargets();
        }

        private void TryRefuelNearbyTargets()
        {
            if (parent.IsForbidden(parent.Faction))
            {
                return;
            }

            NetworkBuilding networkBuilding = parent as NetworkBuilding;
            DataNetwork network = networkBuilding.ParentNetwork;
            if (!network.IsOperational || network.UsedBytes <= 0)
            {
                return;
            }

            Map map = parent.Map;
            int cellCount = GenRadial.NumCellsInRadius(Radius);
            for (int i = 0; i < cellCount; i++)
            {
                IntVec3 cell = parent.Position + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> things = map.thingGrid.ThingsListAt(cell);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing target = things[j];
                    CompRefuelable refuelable = target?.TryGetComp<CompRefuelable>();
                    if (!IsValidTarget(target, refuelable))
                    {
                        continue;
                    }

                    TryRefuelTarget(network, refuelable);
                    if (network.UsedBytes <= 0)
                    {
                        return;
                    }
                }
            }
        }

        private bool IsValidTarget(Thing target, CompRefuelable refuelable)
        {
            if (target == null || refuelable == null || target == parent || target.Destroyed || !target.Spawned)
            {
                return false;
            }

            if (target.IsForbidden(parent.Faction))
            {
                return false;
            }

            return refuelable.ShouldAutoRefuelNow && refuelable.GetFuelCountToFullyRefuel() > 0;
        }

        private bool TryRefuelTarget(DataNetwork network, CompRefuelable refuelable)
        {
            int neededFuelCount = refuelable.GetFuelCountToFullyRefuel();
            if (neededFuelCount <= 0)
            {
                return false;
            }

            if (!network.TryTakeItems(refuelable.Props.fuelFilter, neededFuelCount, out List<Thing> fuelThings))
            {
                return false;
            }

            refuelable.Refuel(fuelThings);
            return true;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureInitialized();

            if (nextTick <= 0)
            {
                nextTick = Find.TickManager.TicksGame + parent.thingIDNumber % 120;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref tickInterval, "tickInterval", Props.defaultTickInterval);
            Scribe_Values.Look(ref nextTick, "nextTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureInitialized();
            }
        }

        private void EnsureInitialized()
        {
            if (tickInterval <= 0)
            {
                tickInterval = Props.defaultTickInterval;
            }

            tickInterval = Mathf.Clamp(tickInterval, Props.minTickInterval, Props.maxTickInterval);
        }
    }
}
