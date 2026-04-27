using Verse;

namespace SK_Matter_Network
{
    public class NetworkBuilding : Building
    {
        private DataNetwork parentNetwork;
        public DataNetwork ParentNetwork { get => parentNetwork; set => parentNetwork = value; }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            bool beingTransportedOnGravship = BeingTransportedOnGravship;
            Map oldMap = Map;
            DataNetwork network = parentNetwork;
            NetworksMapComponent mapComp = oldMap.GetComponent<NetworksMapComponent>();
            if (!beingTransportedOnGravship)
            {
                mapComp.RemoveBuilding(this);
            }
            base.DeSpawn(mode);
            if (beingTransportedOnGravship && network != null && !network.HasSpawnedBuildingOnMap(oldMap))
            {
                mapComp.RemoveNetwork(network);
            }
            Logger.Message($"Removing building from map comp, {Position}");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad)
            {
                return;
            }
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (BeingTransportedOnGravship && parentNetwork != null)
            {
                parentNetwork.SetMap(map);
                mapComp.AddNetwork(parentNetwork);
                Logger.Message($"Reattaching transported network building to map comp, {Position} {parentNetwork != null}");
                return;
            }

            mapComp.AddBuilding(this);
            Logger.Message($"Adding building to map comp, {Position} {parentNetwork != null}");
        }

        public override void PostSwapMap()
        {
            DataNetwork network = parentNetwork;
            base.PostSwapMap();

            if (Spawned && network != null)
            {
                NetworksMapComponent mapComp = Map.GetComponent<NetworksMapComponent>();
                network.SetMap(Map);
                mapComp.AddNetwork(network);
                network.RebuildNetworkBuildingCells();
                network.ValidateControllerConflicts();
                network.NotifyDiskCapacityChanged();
                network.ValidateNetwork();
            }
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();

            if (ParentNetwork != null)
            {
                if (!string.IsNullOrEmpty(baseString))
                {
                    baseString += "\n";
                }
                baseString += "MN_NetworkInspectNetworkId".Translate(ParentNetwork.NetworkId, ParentNetwork.BuildingCount);
            }

            return baseString;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref parentNetwork, "parentNetwork");
        }
    }
}
