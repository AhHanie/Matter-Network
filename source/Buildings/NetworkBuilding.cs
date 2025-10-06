using Verse;

namespace SK_Matter_Network
{
    public class NetworkBuilding : Building
    {
        private DataNetwork parentNetwork;
        public DataNetwork ParentNetwork { get => parentNetwork; set => parentNetwork = value; }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            NetworksMapComponent mapComp = Map.GetComponent<NetworksMapComponent>();
            mapComp.RemoveBuilding(this);
            base.DeSpawn(mode);
            Log.Message($"Removing building from map comp, {Position}");
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            NetworksMapComponent mapComp = Map.GetComponent<NetworksMapComponent>();
            mapComp.RemoveBuilding(this);
            base.Destroy(mode);
            Log.Message($"Removing building from map comp, {Position}");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad)
            {
                return;
            }
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            mapComp.AddBuilding(this);
            Log.Message($"Adding building to map comp, {Position}");
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
                baseString += $"Network ID: {ParentNetwork.NetworkId} ({ParentNetwork.BuildingCount} buildings)";
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