using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class DataNetwork : IExposable, ILoadReferenceable
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
                    NetworkBuildingDiskDrive diskDrive = building as NetworkBuildingDiskDrive;
                    diskDrives.Add(diskDrive);

                    List<Thing> existingItems = diskDrive.GetAllStoredItems();
                    if (existingItems.Count > 0)
                    {
                        AddDiskDriveItems(diskDrive, existingItems);
                        Log.Message($"Added disk drive at {building.Position} to network {networkId} with {existingItems.Count} existing items");
                    }
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

                    RemoveDiskDriveItems(diskDrive, diskDrive.GetAllStoredItems());

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
                        buildings[0].Map.listerThings.Add(storedThing);
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

            ValidateNetwork();

            return totalAdded;
        }

        // This is happening after TryStartCarry has completed.
        public bool RemoveItem(Thing item, int count, bool forceRemove = false)
        {
            if (!itemToDiskDrive.TryGetValue(item, out NetworkBuildingDiskDrive diskDrive))
            {
                Log.Warning($"Attempted to remove item {item.LabelShort} but it's not tracked in network {networkId}");
                return false;
            }

            bool success = diskDrive.RemoveItem(item, count, forceRemove);

            if (success)
            {
                itemToDiskDrive.Remove(item);
                storedItems.Remove(item);
                buildings[0].Map.listerThings.Remove(item);
                Log.Message($"Removed {item.LabelShort} from network {networkId}");
            }
            else
            {
                Log.Message($"Removed {count} of {item.LabelShort} from network {networkId}");
            }

            ValidateNetwork();

            return success;
        }

        public void RemoveDiskDriveItems(NetworkBuildingDiskDrive diskDrive, List<Thing> items)
        {
            int removedCount = 0;
            foreach (Thing item in items)
            {
                if (itemToDiskDrive.TryGetValue(item, out NetworkBuildingDiskDrive trackedDrive) && trackedDrive == diskDrive)
                {
                    storedItems.Remove(item);
                    itemToDiskDrive.Remove(item);
                    buildings[0].Map.listerThings.Remove(item);
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                Log.Message($"Removed {removedCount} items from network {networkId} tracking due to disk removal from drive at {diskDrive.Position}");
            }
        }

        public void AddDiskDriveItems(NetworkBuildingDiskDrive diskDrive, List<Thing> items)
        {
            int addedCount = 0;
            foreach (Thing item in items)
            {
                if (!storedItems.Contains(item))
                {
                    storedItems.Add(item);
                    itemToDiskDrive[item] = diskDrive;
                    buildings[0].Map.listerThings.Add(item);
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                Log.Message($"Added {addedCount} items to network {networkId} tracking due to disk addition to drive at {diskDrive.Position}");
            }
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

        public string GetUniqueLoadID()
        {
            return networkId;
        }

        // Debugging Function
        public void ValidateNetwork()
        {
            bool foundIssues = false;
            HashSet<Thing> validItems = new HashSet<Thing>();
            Dictionary<Thing, NetworkBuildingDiskDrive> validItemToDiskDrive = new Dictionary<Thing, NetworkBuildingDiskDrive>();

            // Validate each disk drive
            foreach (NetworkBuildingDiskDrive diskDrive in diskDrives)
            {
                // Validate each disk in the disk drive
                foreach (Thing disk in diskDrive.HeldItems)
                {
                    CompDiskDataStorage diskStorage = disk.TryGetComp<CompDiskDataStorage>();
                    if (diskStorage == null)
                    {
                        Log.Warning($"Network {networkId}: Disk {disk.LabelShort} at drive {diskDrive.Position} has no CompDiskDataStorage");
                        continue;
                    }

                    int actualBytes = 0;
                    List<Thing> itemsToRemove = new List<Thing>();

                    // Count actual bytes and find problematic items
                    List<Thing> storedItemsList = diskStorage.GetAllStoredItems();
                    foreach (Thing item in storedItemsList)
                    {
                        if (item == null || item.Destroyed || item.stackCount <= 0)
                        {
                            itemsToRemove.Add(item);
                            foundIssues = true;
                            Log.Error($"Network {networkId}: Found invalid item in disk {disk.LabelShort} at drive {diskDrive.Position} - " +
                                     $"Item: {item?.LabelShort ?? "null"}, Destroyed: {item?.Destroyed ?? true}, StackCount: {item?.stackCount ?? 0}");
                        }
                        else
                        {
                            actualBytes += item.stackCount;
                            validItems.Add(item);
                            validItemToDiskDrive[item] = diskDrive;
                        }
                    }

                    // Remove invalid items from disk storage
                    foreach (Thing itemToRemove in itemsToRemove)
                    {
                        if (itemToRemove != null)
                        {
                            diskDrive.RemoveItem(itemToRemove, 0, true);
                            Log.Message($"Network {networkId}: Removed invalid item from disk drive / disk {diskDrive.LabelShort} {disk.LabelShort}");
                        }
                    }

                    // Validate bytes
                    if (diskStorage.UsedBytes != actualBytes)
                    {
                        foundIssues = true;
                        Log.Error($"Network {networkId}: Disk {disk.LabelShort} at drive {diskDrive.Position} byte mismatch - " +
                                 $"Cached: {diskStorage.UsedBytes}, Actual: {actualBytes}. Correcting...");

                        diskStorage.SetUsedBytes(actualBytes);
                    }
                }

                // Mark disk drive cache as dirty so it recalculates
                diskDrive.MarkCacheDirty();
            }

            // Validate network tracking
            if (storedItems.Count != validItems.Count || !storedItems.SetEquals(validItems))
            {
                foundIssues = true;
                Log.Error($"Network {networkId}: Network item tracking mismatch - " +
                         $"Tracked: {storedItems.Count}, Valid: {validItems.Count}. Correcting...");

                // Remove items from network tracking that shouldn't be there
                List<Thing> itemsToRemoveFromNetwork = storedItems.Where(item => !validItems.Contains(item)).ToList();

                foreach (Thing item in itemsToRemoveFromNetwork)
                {
                    storedItems.Remove(item);
                    itemToDiskDrive.Remove(item);

                    buildings[0].Map.listerThings.Remove(item);

                    Log.Message($"Network {networkId}: Removed {item?.LabelShort ?? "null"} from network tracking");
                }

                // Add items that should be tracked but aren't
                foreach (Thing item in validItems)
                {
                    if (!storedItems.Contains(item))
                    {
                        storedItems.Add(item);

                        buildings[0].Map.listerThings.Add(item);

                        Log.Message($"Network {networkId}: Added {item.LabelShort} to network tracking");
                    }
                }

                // Update itemToDiskDrive mapping
                itemToDiskDrive = validItemToDiskDrive;
            }

            if (foundIssues)
            {
                Log.Error($"Network {networkId}: Validation found and corrected issues. " +
                         $"Final item count: {storedItems.Count}, Disk drives: {diskDrives.Count}");
            }
            else
            {
                Log.Message($"Network {networkId}: Validation passed - no issues found. " +
                           $"Items: {storedItems.Count}, Disk drives: {diskDrives.Count}");
            }
        }
    }
}