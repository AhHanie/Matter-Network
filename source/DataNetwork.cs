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
        private Dictionary<Thing, NetworkBuildingDiskDrive> itemToDiskDrive;

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
            itemToDiskDrive = new Dictionary<Thing, NetworkBuildingDiskDrive>();
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
                    NetworkBuildingDiskDrive diskDrive = building as NetworkBuildingDiskDrive;

                    List<Thing> itemsToRemove = new List<Thing>();
                    foreach (var kvp in itemToDiskDrive)
                    {
                        if (kvp.Value == diskDrive)
                        {
                            itemsToRemove.Add(kvp.Key);
                        }
                    }

                    foreach (Thing item in itemsToRemove)
                    {
                        itemToDiskDrive.Remove(item);
                        storedItems.Remove(item);
                    }

                    diskDrives.Remove(diskDrive);
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

                    foreach (Thing storedThing in storedThings)
                    {
                        this.storedItems.Add(storedThing);
                        itemToDiskDrive[storedThing] = diskDrive;
                    }

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

        public bool RemoveItem(Thing item, int count)
        {
            if (!itemToDiskDrive.TryGetValue(item, out NetworkBuildingDiskDrive diskDrive))
            {
                Log.Warning($"Attempted to remove item {item.LabelShort} but it's not tracked in network {networkId}");
                return false;
            }

            bool success = diskDrive.RemoveItem(item, count);

            if (success)
            {
                if (count >= item.stackCount || item.Destroyed)
                {
                    // Item fully removed
                    itemToDiskDrive.Remove(item);
                    storedItems.Remove(item);
                    Log.Message($"Removed {item.LabelShort} from network {networkId}");
                }
                else
                {
                    // Partial removal - item still exists in network
                    Log.Message($"Removed {count} of {item.LabelShort} from network {networkId}");
                }
            }

            return success;
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

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildItemTracking();
            }
        }

        public bool AcceptsItem(Thing item)
        {
            return storageSettings.AllowedToAccept(item);
        }

        public bool ItemInNetwork(Thing item)
        {
            return storedItems.Contains(item);
        }

        private void RebuildItemTracking()
        {
            storedItems = new HashSet<Thing>();
            itemToDiskDrive = new Dictionary<Thing, NetworkBuildingDiskDrive>();

            foreach (NetworkBuildingDiskDrive diskDrive in diskDrives)
            {
                foreach (Thing item in diskDrive.GetAllStoredItems())
                {
                    storedItems.Add(item);
                    itemToDiskDrive[item] = diskDrive;
                }
            }

            Log.Message($"Rebuilt item tracking for network {networkId}: {storedItems.Count} items tracked");
        }
    }
}