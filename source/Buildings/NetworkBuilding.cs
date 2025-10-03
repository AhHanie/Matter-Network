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
            mapComp.AddBuilding(this);
            Logger.Message($"Adding building to map comp, {Position} {parentNetwork != null}");
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
