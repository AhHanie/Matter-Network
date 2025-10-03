using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class DataNetwork : IExposable
    {
        private List<NetworkBuilding> buildings;
        private HashSet<IntVec3> networkBuildingsCells;
        private List<NetworkBuildingDiskDrive> diskDrives;
        private List<NetworkBuildingNetworkInterface> networkInterfaces;
        private string networkId;
        private StorageSettings storageSettings;
        private bool isBroadcastingSettingsChange = false;
        private HashSet<Thing> storedItems;

        public List<NetworkBuilding> Buildings => buildings;
        public string NetworkId => networkId;
        public int BuildingCount => buildings.Count;
        public StorageSettings StorageSettings => storageSettings;
        public bool IsBroadcastingSettingsChange => isBroadcastingSettingsChange;
        public HashSet<Thing> StoredItems => storedItems;
        public List<NetworkBuildingNetworkInterface> NetworkInterfaces => networkInterfaces;
        public Faction Faction => buildings.First().Faction;

        public DataNetwork()
        {
            buildings = new List<NetworkBuilding>();
            networkBuildingsCells = new HashSet<IntVec3>();
            diskDrives = new List<NetworkBuildingDiskDrive>();
            networkInterfaces = new List<NetworkBuildingNetworkInterface>();
            networkId = System.Guid.NewGuid().ToString();
            storageSettings = new StorageSettings();
            isBroadcastingSettingsChange = false;
            storedItems = new HashSet<Thing>();
        }

        public void AddBuilding(NetworkBuilding building)
        {
            if (!buildings.Contains(building))
            {
                buildings.Add(building);
                building.ParentNetwork = this;

                if (!networkBuildingsCells.Contains(building.Position))
                {
                    networkBuildingsCells.Add(building.Position);
                }

                if (building.def == BuildingDefOf.MN_DiskDrive)
                {
                    diskDrives.Add(building as NetworkBuildingDiskDrive);
                }
                else if (building.def == BuildingDefOf.MN_NetworkInterface)
                {
                    NetworkBuildingNetworkInterface networkInterface = building as NetworkBuildingNetworkInterface;
                    networkInterfaces.Add(networkInterface);

                    if (networkInterfaces.Count == 1)
                    {
                        storageSettings.CopyFrom(networkInterface.GetStandaloneSettings());
                    }
                }

                Log.Message($"Added building at {building.Position} to network {networkId}. Network now has {buildings.Count} buildings.");
            }
        }

        public void RemoveBuilding(NetworkBuilding building)
        {
            if (buildings.Remove(building))
            {
                building.ParentNetwork = null;

                bool positionStillOccupied = false;
                foreach (NetworkBuilding existingBuilding in buildings)
                {
                    if (existingBuilding.Position == building.Position)
                    {
                        positionStillOccupied = true;
                        break;
                    }
                }

                if (!positionStillOccupied)
                {
                    networkBuildingsCells.Remove(building.Position);
                }

                if (building.def == BuildingDefOf.MN_DiskDrive)
                {
                    diskDrives.Remove(building as NetworkBuildingDiskDrive);
                }
                else if (building.def == BuildingDefOf.MN_NetworkInterface)
                {
                    networkInterfaces.Remove(building as NetworkBuildingNetworkInterface);
                }

                Log.Message($"Removed building at {building.Position} from network {networkId}. Network now has {buildings.Count} buildings.");
            }
        }

        public bool CellHasNetworkBuilding(IntVec3 cell)
        {
            return networkBuildingsCells.Contains(cell);
        }

        public bool IsEmpty()
        {
            return buildings.Count == 0;
        }

        public int CanAcceptCount(Thing item)
        {
            int totalCanAccept = 0;

            foreach (NetworkBuildingDiskDrive diskDrive in diskDrives)
            {
                totalCanAccept += diskDrive.CanAcceptCount(item);
            }

            return totalCanAccept;
        }

        public int AddItem(Thing item, int count)
        {
            int itemsRemaining = count;
            int totalAdded = 0;

            foreach (NetworkBuildingDiskDrive diskDrive in diskDrives)
            {
                if (itemsRemaining <= 0)
                {
                    break;
                }

                int driveCanAccept = diskDrive.CanAcceptCount(item);
                if (driveCanAccept > 0)
                {
                    int toAddThisRound = UnityEngine.Mathf.Min(itemsRemaining, driveCanAccept);
                    var (actuallyAdded, storedThings) = diskDrive.AddItem(item, toAddThisRound);
                    this.storedItems.AddRange(storedThings);

                    totalAdded += actuallyAdded;
                    itemsRemaining -= actuallyAdded;

                    if (actuallyAdded < toAddThisRound)
                    {
                        Log.Warning($"Disk drive at {diskDrive.Position} accepted fewer items than expected");
                        break;
                    }
                }
            }

            if (totalAdded > 0)
            {
                Log.Message($"Network {networkId} stored {totalAdded} of {item.def.defName} across {diskDrives.Count} disk drives");
            }

            return totalAdded;
        }

        public void Notify_SettingsChanged(StorageSettings interfaceSettings)
        {
            if (isBroadcastingSettingsChange)
            {
                return;
            }

            isBroadcastingSettingsChange = true;

            storageSettings.CopyFrom(interfaceSettings);

            foreach (NetworkBuildingNetworkInterface networkInterface in networkInterfaces)
            {
                networkInterface.NotifyNetworkSettingsChanged();
            }

            isBroadcastingSettingsChange = false;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref networkId, "networkId");
            Scribe_Collections.Look(ref buildings, "buildings", LookMode.Reference);
            Scribe_Collections.Look(ref networkBuildingsCells, "networkBuildingsCells", LookMode.Value);
            Scribe_Collections.Look(ref diskDrives, "diskDrives", LookMode.Reference);
            Scribe_Collections.Look(ref networkInterfaces, "networkInterfaces", LookMode.Reference);
            Scribe_Deep.Look(ref storageSettings, "storageSettings");
        }

        public bool AcceptsItem(Thing item)
        {
            return storageSettings.AllowedToAccept(item);
        }

        public bool ItemInNetwork(Thing item)
        {
            return storedItems.Contains(item);
        }
    }
}