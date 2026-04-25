using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class DataNetwork : IExposable, ILoadReferenceable
    {
        private sealed class NetworkStorageSettingsParent : IStoreSettingsParent
        {
            private readonly DataNetwork network;

            public NetworkStorageSettingsParent(DataNetwork network)
            {
                this.network = network;
            }

            public StorageSettings GetStoreSettings()
            {
                return network.storageSettings;
            }

            public StorageSettings GetParentStoreSettings()
            {
                if (network.networkInterfaces != null)
                {
                    foreach (NetworkBuildingNetworkInterface iface in network.networkInterfaces)
                    {
                        if (iface != null && !iface.Destroyed)
                        {
                            return iface.GetParentStoreSettings();
                        }
                    }
                }

                if (network.activeController != null && !network.activeController.Destroyed)
                {
                    return network.activeController.GetParentStoreSettings();
                }

                return null;
            }

            public bool StorageTabVisible => false;

            public void Notify_SettingsChanged() { }
        }

        private List<NetworkBuilding> buildings;
        private HashSet<IntVec3> networkBuildingsCells;
        private NetworkBuildingController activeController;
        private List<NetworkBuildingNetworkInterface> networkInterfaces;
        private List<NetworkBuildingDiskDrive> diskDrives;
        private string networkId;
        private Map map;

        private StorageSettings storageSettings;
        private NetworkStorageSettingsParent storageSettingsOwner;
        private bool isBroadcastingSettingsChange = false;
        private bool isAddingBuilding = false;

        public HashSet<Thing> storedItems;
        private Dictionary<ThingDef, int> itemCountByDef;
        private int cachedUsedBytes;
        private int cachedTotalCapacityBytes;
        private bool bytesDirty = true;
        private int controllerCapacityAllowance;

        private ITab_NetworkStorage currentTab;

        public List<NetworkBuilding> Buildings => buildings;
        public string NetworkId => networkId;
        public int BuildingCount => buildings.Count;
        public StorageSettings StorageSettings => storageSettings;
        public bool IsBroadcastingSettingsChange => isBroadcastingSettingsChange;
        public List<NetworkBuildingNetworkInterface> NetworkInterfaces => networkInterfaces;
        public NetworkBuildingController ActiveController => activeController;
        public bool HasActiveController => activeController != null && !activeController.ControllerConflictDisabled && !activeController.Destroyed;
        public bool CanExtractItems => HasActiveController && networkInterfaces.Count > 0;
        public Faction Faction => buildings.Count > 0 ? buildings[0].Faction : null;

        public ITab_NetworkStorage CurrentTab
        {
            get => currentTab;
            set => currentTab = value;
        }

        public IReadOnlyCollection<Thing> StoredItems => storedItems;

        public IReadOnlyDictionary<ThingDef, int> ItemCountByDef
        {
            get
            {
                if (bytesDirty) RecomputeCaches();
                return itemCountByDef;
            }
        }

        public Dictionary<ThingDef, int> ItemDefToStackCount
        {
            get
            {
                if (bytesDirty) RecomputeCaches();
                return itemCountByDef;
            }
        }

        public int UsedBytes
        {
            get
            {
                if (bytesDirty) RecomputeCaches();
                return cachedUsedBytes;
            }
        }

        public int TotalCapacityBytes => cachedTotalCapacityBytes;

        public int OvercommittedBytes => System.Math.Max(0, cachedUsedBytes - cachedTotalCapacityBytes);

        public int ControllerCapacityLimitForAdds => cachedTotalCapacityBytes + controllerCapacityAllowance;

        public DataNetwork()
        {
            buildings = new List<NetworkBuilding>();
            networkBuildingsCells = new HashSet<IntVec3>();
            networkInterfaces = new List<NetworkBuildingNetworkInterface>();
            diskDrives = new List<NetworkBuildingDiskDrive>();
            storageSettingsOwner = new NetworkStorageSettingsParent(this);
            storageSettings = new StorageSettings(storageSettingsOwner);
            storedItems = new HashSet<Thing>();
            itemCountByDef = new Dictionary<ThingDef, int>();
        }

        public DataNetwork(Map map) : this()
        {
            networkId = System.Guid.NewGuid().ToString();
            this.map = map;
        }

        public void MarkBytesDirty()
        {
            bytesDirty = true;
            RefreshUI();
        }

        private void RecomputeCaches()
        {
            SyncStoredItemsWithController(logChanges: false);
            cachedUsedBytes = 0;
            itemCountByDef.Clear();

            if (activeController?.innerContainer != null)
            {
                foreach (Thing t in activeController.innerContainer.InnerListForReading)
                {
                    if (t == null || t.Destroyed) continue;
                    cachedUsedBytes += t.stackCount;
                    if (itemCountByDef.TryGetValue(t.def, out int cur))
                        itemCountByDef[t.def] = cur + t.stackCount;
                    else
                        itemCountByDef[t.def] = t.stackCount;
                }
            }

            bytesDirty = false;
        }

        public void RecalcTotalCapacityBytes()
        {
            cachedTotalCapacityBytes = 0;
            foreach (NetworkBuildingDiskDrive drive in diskDrives)
            {
                cachedTotalCapacityBytes += drive.GetTotalCapacityBytes();
            }
        }

        public bool CanAccept(Thing item)
        {
            if (!HasActiveController) return false;
            if (!storageSettings.AllowedToAccept(item)) return false;
            return UsedBytes + item.stackCount <= cachedTotalCapacityBytes;
        }

        public int CanAcceptCount(Thing item)
        {
            if (!HasActiveController) return 0;
            if (!storageSettings.AllowedToAccept(item)) return 0;
            return System.Math.Max(0, cachedTotalCapacityBytes - UsedBytes);
        }

        public bool AcceptsItem(Thing item) => storageSettings.AllowedToAccept(item);

        public bool ItemInNetwork(Thing item) => storedItems.Contains(item);

        public bool WouldControllerAddExceedCapacity(Thing item)
        {
            if (item == null) return true;
            return UsedBytes + item.stackCount > ControllerCapacityLimitForAdds;
        }

        public void AddBuilding(NetworkBuilding building)
        {
            if (buildings.Contains(building)) return;

            isAddingBuilding = true;
            buildings.Add(building);
            building.ParentNetwork = this;

            if (!networkBuildingsCells.Contains(building.Position))
                networkBuildingsCells.Add(building.Position);

            if (building is NetworkBuildingController ctrl)
            {
                SetController(ctrl);
            }
            else if (building is NetworkBuildingDiskDrive drive)
            {
                diskDrives.Add(drive);
                RecalcTotalCapacityBytes();
                RestoreArchivedItemsToController();
            }
            else if (building is NetworkBuildingNetworkInterface iface)
            {
                networkInterfaces.Add(iface);

                if (networkInterfaces.Count == 1)
                    storageSettings.CopyFrom(iface.GetStandaloneSettings());
                else
                    iface.NotifyNetworkSettingsChanged();
            }

            Logger.Message($"Added {building.def.defName} at {building.Position} to network {networkId}. Count: {buildings.Count}");
            isAddingBuilding = false;
        }

        public void RemoveBuilding(NetworkBuilding building)
        {
            if (!buildings.Remove(building)) return;

            building.ParentNetwork = null;

            bool positionStillOccupied = false;
            foreach (NetworkBuilding b in buildings)
            {
                if (b.Position == building.Position) { positionStillOccupied = true; break; }
            }
            if (!positionStillOccupied)
                networkBuildingsCells.Remove(building.Position);

            if (building is NetworkBuildingController ctrl)
            {
                RemoveController(ctrl);
            }
            else if (building is NetworkBuildingDiskDrive drive)
            {
                diskDrives.Remove(drive);
                RecalcTotalCapacityBytes();
                MarkBytesDirty();
            }
            else if (building is NetworkBuildingNetworkInterface iface)
            {
                networkInterfaces.Remove(iface);
            }

            Logger.Message($"Removed {building.def.defName} at {building.Position} from network {networkId}. Count: {buildings.Count}");
        }

        private void SetController(NetworkBuildingController ctrl)
        {
            if (activeController != null && activeController != ctrl)
            {
                if (ctrl.thingIDNumber < activeController.thingIDNumber)
                {
                    activeController.ControllerConflictDisabled = true;
                    activeController = ctrl;
                }
                else
                {
                    ctrl.ControllerConflictDisabled = true;
                }
            }
            else
            {
                activeController = ctrl;
            }

            RebuildStoredItemsFromController();
            RecalcTotalCapacityBytes();
            RestoreArchivedItemsToController();
            MarkBytesDirty();
        }

        private void RemoveController(NetworkBuildingController ctrl)
        {
            if (activeController == ctrl)
            {
                activeController = null;
                storedItems.Clear();
                MarkBytesDirty();

                foreach (NetworkBuilding b in buildings)
                {
                    if (b is NetworkBuildingController other && !other.ControllerConflictDisabled)
                    {
                        activeController = other;
                        RebuildStoredItemsFromController();
                        RecalcTotalCapacityBytes();
                        RestoreArchivedItemsToController();
                        break;
                    }
                }
            }
            else if (ctrl.ControllerConflictDisabled)
            {
                ctrl.ControllerConflictDisabled = false;
            }
        }

        private void RebuildStoredItemsFromController()
        {
            storedItems.Clear();
            if (activeController?.innerContainer == null) return;

            foreach (Thing t in activeController.innerContainer.InnerListForReading)
            {
                if (t != null && !t.Destroyed)
                    storedItems.Add(t);
            }
        }

        public void NotifyDiskCapacityChanged()
        {
            RecalcTotalCapacityBytes();
            ReconcileCapacityAfterDiskCapacityChanged();
            RestoreArchivedItemsToController();
            MarkBytesDirty();
        }

        public void NotifyDiskRemovedFromDrive(Thing removedDisk)
        {
            RecalcTotalCapacityBytes();

            CompDiskCapacity removedDiskCapacity = removedDisk?.TryGetComp<CompDiskCapacity>();
            if (removedDiskCapacity != null && HasActiveController && UsedBytes > TotalCapacityBytes)
            {
                int overflow = UsedBytes - TotalCapacityBytes;
                ArchiveOverflowToDisk(removedDiskCapacity, overflow);
            }

            ReconcileCapacityAfterDiskCapacityChanged(recalculateFirst: false);
            MarkBytesDirty();
        }

        public void NotifyDiskDriveWillBeUnavailable(NetworkBuildingDiskDrive unavailableDrive)
        {
            RecalcTotalCapacityBytes(excludedDrive: unavailableDrive);
            ReconcileCapacityAfterDiskCapacityChanged(recalculateFirst: false);
            MarkBytesDirty();
        }

        public void ArchiveAllControllerItemsToDisks(bool dropRemainder, IntVec3 fallbackCell, Map fallbackMap)
        {
            if (activeController?.innerContainer == null)
                return;

            List<Thing> activeItems = EnumerateActiveItemsDeterministic();
            foreach (Thing item in activeItems)
            {
                if (item == null || item.Destroyed || item.stackCount <= 0)
                    continue;

                while (!item.Destroyed && item.stackCount > 0)
                {
                    int moved = TryMoveSomeToDisks(item, item.stackCount);
                    if (moved <= 0)
                        break;
                }

                if (!item.Destroyed && item.stackCount > 0 && dropRemainder)
                    DropActiveItem(item, fallbackCell, fallbackMap, item.stackCount);
            }

            RebuildStoredItemsFromController();
            RecalcTotalCapacityBytes();
            MarkBytesDirty();
        }

        public void RestoreArchivedItemsToController()
        {
            if (!HasActiveController || activeController?.innerContainer == null)
                return;

            foreach (CompDiskCapacity disk in EnumerateInsertedDisksDeterministic())
            {
                if (disk == null || !disk.HasArchivedItems)
                    continue;

                int archivedBytes = disk.ArchivedUsedBytes;
                if (archivedBytes <= 0)
                    continue;

                if (UsedBytes + archivedBytes > TotalCapacityBytes + disk.MaxBytes)
                    continue;

                controllerCapacityAllowance += disk.MaxBytes;
                foreach (Thing archived in disk.ArchivedContainer.InnerListForReading.ToList())
                {
                    if (archived == null || archived.Destroyed)
                        continue;

                    while (!archived.Destroyed && archived.stackCount > 0)
                    {
                        int moved = SafeTransfer(archived, disk.ArchivedContainer, activeController.innerContainer, archived.stackCount);
                        if (moved <= 0)
                            break;
                    }
                }
                controllerCapacityAllowance -= disk.MaxBytes;

                if (!disk.HasArchivedItems)
                {
                    RecalcTotalCapacityBytes();
                    RebuildStoredItemsFromController();
                    MarkBytesDirty();
                }
            }
        }

        public void ReconcileCapacityAfterDiskCapacityChanged(bool recalculateFirst = true)
        {
            if (recalculateFirst)
                RecalcTotalCapacityBytes();

            while (HasActiveController && UsedBytes > TotalCapacityBytes)
            {
                int overflow = UsedBytes - TotalCapacityBytes;
                int archived = ArchiveOverflowToDisks(overflow);
                RecalcTotalCapacityBytes();

                if (archived > 0)
                    continue;

                if (!TryGetDropTarget(out IntVec3 dropCell, out Map dropMap))
                    break;

                DropControllerItemsDeterministically(overflow, dropCell, dropMap);
                RecalcTotalCapacityBytes();
            }

            RebuildStoredItemsFromController();
            MarkBytesDirty();
        }

        private void RecalcTotalCapacityBytes(NetworkBuildingDiskDrive excludedDrive)
        {
            cachedTotalCapacityBytes = 0;
            foreach (NetworkBuildingDiskDrive drive in diskDrives)
            {
                if (drive == null || drive == excludedDrive)
                    continue;

                cachedTotalCapacityBytes += drive.GetTotalCapacityBytes();
            }
        }

        private int ArchiveOverflowToDisks(int requiredBytes)
        {
            int remaining = requiredBytes;
            foreach (Thing item in EnumerateActiveItemsDeterministic())
            {
                if (remaining <= 0)
                    break;

                if (item == null || item.Destroyed || item.stackCount <= 0)
                    continue;

                int moved = TryMoveSomeToDisks(item, remaining);
                remaining -= moved;
            }

            int archived = requiredBytes - remaining;
            if (archived > 0)
            {
                RebuildStoredItemsFromController();
                MarkBytesDirty();
            }

            return archived;
        }

        private int ArchiveOverflowToDisk(CompDiskCapacity disk, int requiredBytes)
        {
            if (disk == null || requiredBytes <= 0)
                return 0;

            int remaining = requiredBytes;
            foreach (Thing item in EnumerateActiveItemsDeterministic())
            {
                if (remaining <= 0)
                    break;

                if (item == null || item.Destroyed || item.stackCount <= 0)
                    continue;

                int moved = TryMoveSomeToDisk(item, disk, remaining);
                remaining -= moved;
            }

            int archived = requiredBytes - remaining;
            if (archived > 0)
            {
                RebuildStoredItemsFromController();
                MarkBytesDirty();
            }

            return archived;
        }

        private int TryMoveSomeToDisks(Thing item, int requestedBytes)
        {
            if (activeController?.innerContainer == null || item == null || item.Destroyed || requestedBytes <= 0)
                return 0;

            int movedTotal = 0;
            foreach (CompDiskCapacity disk in EnumerateInsertedDisksDeterministic())
            {
                if (item.Destroyed || item.stackCount <= 0 || movedTotal >= requestedBytes)
                    break;

                int freeBytes = disk.ArchivedFreeBytes;
                if (freeBytes <= 0)
                    continue;

                int count = System.Math.Min(requestedBytes - movedTotal, System.Math.Min(freeBytes, item.stackCount));
                int moved = SafeTransfer(item, activeController.innerContainer, disk.ArchivedContainer, count);
                if (moved <= 0)
                    continue;

                movedTotal += moved;
            }

            return movedTotal;
        }

        private int TryMoveSomeToDisk(Thing item, CompDiskCapacity disk, int requestedBytes)
        {
            if (activeController?.innerContainer == null || item == null || item.Destroyed || disk == null || requestedBytes <= 0)
                return 0;

            int freeBytes = disk.ArchivedFreeBytes;
            if (freeBytes <= 0)
                return 0;

            int count = System.Math.Min(requestedBytes, System.Math.Min(freeBytes, item.stackCount));
            return SafeTransfer(item, activeController.innerContainer, disk.ArchivedContainer, count);
        }

        private void DropControllerItemsDeterministically(int requiredBytes, IntVec3 dropCell, Map dropMap)
        {
            int remaining = requiredBytes;
            foreach (Thing item in EnumerateActiveItemsDeterministic())
            {
                if (remaining <= 0)
                    break;

                if (item == null || item.Destroyed || item.stackCount <= 0)
                    continue;

                int count = System.Math.Min(remaining, item.stackCount);
                DropActiveItem(item, dropCell, dropMap, count);
                remaining -= count;
            }
        }

        private void DropActiveItem(Thing item, IntVec3 dropCell, Map dropMap, int count)
        {
            if (activeController?.innerContainer == null)
                return;

            Thing toDrop;
            if (count >= item.stackCount)
            {
                if (!activeController.innerContainer.Remove(item))
                    return;

                toDrop = item;
            }
            else
            {
                toDrop = item.SplitOff(count);
            }

            GenPlace.TryPlaceThing(toDrop, dropCell, dropMap, ThingPlaceMode.Near);
            RebuildStoredItemsFromController();
            MarkBytesDirty();
        }

        private static int SafeTransfer(Thing source, ThingOwner<Thing> from, ThingOwner<Thing> to, int requestedCount)
        {
            if (source == null || source.Destroyed || from == null || to == null || requestedCount <= 0)
                return 0;

            int count = System.Math.Min(requestedCount, source.stackCount);
            Thing moving;

            if (count >= source.stackCount)
            {
                if (!from.Remove(source))
                    return 0;

                moving = source;
            }
            else
            {
                moving = source.SplitOff(count);
            }

            if (to.TryAdd(moving, canMergeWithExistingStacks: true))
                return count;

            from.TryAdd(moving, canMergeWithExistingStacks: true);
            return 0;
        }

        private List<CompDiskCapacity> EnumerateInsertedDisksDeterministic()
        {
            List<CompDiskCapacity> disks = new List<CompDiskCapacity>();
            foreach (NetworkBuildingDiskDrive drive in diskDrives)
            {
                foreach (Thing diskThing in drive.HeldItems)
                {
                    CompDiskCapacity disk = diskThing?.TryGetComp<CompDiskCapacity>();
                    if (disk != null)
                        disks.Add(disk);
                }
            }

            disks.Sort(CompareDisks);
            return disks;
        }

        private static int CompareDisks(CompDiskCapacity a, CompDiskCapacity b)
        {
            int aId = a.parent.thingIDNumber;
            int bId = b.parent.thingIDNumber;
            return aId.CompareTo(bId);
        }

        private List<Thing> EnumerateActiveItemsDeterministic()
        {
            List<Thing> items = new List<Thing>();
            if (activeController?.innerContainer == null)
                return items;

            foreach (Thing thing in activeController.innerContainer.InnerListForReading)
            {
                if (thing != null && !thing.Destroyed)
                    items.Add(thing);
            }

            items.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            return items;
        }

        private bool TryGetDropTarget(out IntVec3 dropCell, out Map dropMap)
        {
            foreach (NetworkBuildingNetworkInterface iface in networkInterfaces)
            {
                dropCell = iface.Position;
                dropMap = iface.Map;
                return true;
            }

            if (activeController != null)
            {
                dropCell = activeController.Position;
                dropMap = activeController.Map;
                return true;
            }

            dropCell = IntVec3.Invalid;
            dropMap = map;
            return false;
        }

        public void Notify_SettingsChanged(StorageSettings interfaceSettings)
        {
            if (isBroadcastingSettingsChange || isAddingBuilding) return;

            isBroadcastingSettingsChange = true;
            storageSettings.CopyFrom(interfaceSettings);

            foreach (NetworkBuildingNetworkInterface iface in networkInterfaces)
                iface.NotifyNetworkSettingsChanged();

            isBroadcastingSettingsChange = false;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref networkId, "networkId");
            Scribe_Collections.Look(ref buildings, "buildings", LookMode.Reference);
            Scribe_Collections.Look(ref networkBuildingsCells, "networkBuildingsCells", LookMode.Value);
            Scribe_References.Look(ref activeController, "activeController");
            Scribe_Collections.Look(ref diskDrives, "diskDrives", LookMode.Reference);
            Scribe_Collections.Look(ref networkInterfaces, "networkInterfaces", LookMode.Reference);
            Scribe_Deep.Look(ref storageSettings, "storageSettings");
            Scribe_References.Look(ref map, "map");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (buildings == null) buildings = new List<NetworkBuilding>();
                if (networkBuildingsCells == null) networkBuildingsCells = new HashSet<IntVec3>();
                if (networkInterfaces == null) networkInterfaces = new List<NetworkBuildingNetworkInterface>();
                if (diskDrives == null) diskDrives = new List<NetworkBuildingDiskDrive>();
                if (storedItems == null) storedItems = new HashSet<Thing>();
                if (itemCountByDef == null) itemCountByDef = new Dictionary<ThingDef, int>();

                PostLoadInit();
            }
        }

        private void PostLoadInit()
        {
            EnsureStorageSettingsOwner();

            foreach (NetworkBuilding b in buildings)
            {
                if (b != null) b.ParentNetwork = this;
            }

            RebuildStoredItemsFromController();
            RecalcTotalCapacityBytes();
            MarkBytesDirty();

            ValidateControllerConflicts();
        }

        public void ValidateControllerConflicts()
        {
            List<NetworkBuildingController> controllers = new List<NetworkBuildingController>();
            foreach (NetworkBuilding b in buildings)
            {
                if (b is NetworkBuildingController ctrl)
                    controllers.Add(ctrl);
            }

            if (controllers.Count <= 1)
            {
                foreach (NetworkBuildingController ctrl in controllers)
                    ctrl.ControllerConflictDisabled = false;
                if (controllers.Count == 1 && activeController != controllers[0])
                {
                    activeController = controllers[0];
                    RebuildStoredItemsFromController();
                }
                return;
            }

            controllers.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            activeController = controllers[0];
            activeController.ControllerConflictDisabled = false;

            for (int i = 1; i < controllers.Count; i++)
                controllers[i].ControllerConflictDisabled = true;

            RebuildStoredItemsFromController();
            Logger.Warning($"Network {networkId}: Multiple controllers detected. Primary: {activeController.thingIDNumber}. Others disabled.");
        }

        public bool CellHasNetworkBuilding(IntVec3 cell) => networkBuildingsCells.Contains(cell);

        public bool IsEmpty() => buildings.Count == 0;

        public string GetUniqueLoadID() => networkId;

        public void ValidateNetwork()
        {
            SyncStoredItemsWithController(logChanges: true);
            MarkBytesDirty();
        }

        private void SyncStoredItemsWithController(bool logChanges)
        {
            if (activeController?.innerContainer == null)
            {
                if (storedItems.Count > 0)
                {
                    if (logChanges)
                    {
                        Logger.Warning($"Network {networkId}: storedItems not empty but no active controller. Clearing.");
                    }

                    storedItems.Clear();
                }

                return;
            }

            HashSet<Thing> containerItems = new HashSet<Thing>();
            foreach (Thing thing in activeController.innerContainer.InnerListForReading)
            {
                if (thing != null && !thing.Destroyed)
                {
                    containerItems.Add(thing);
                }
            }

            List<Thing> toRemove = new List<Thing>();
            foreach (Thing thing in storedItems)
            {
                if (thing == null || thing.Destroyed || !containerItems.Contains(thing))
                {
                    toRemove.Add(thing);
                }
            }

            foreach (Thing thing in toRemove)
            {
                storedItems.Remove(thing);
                if (logChanges)
                {
                    Logger.Warning($"Network {networkId}: Removed stale item {thing?.LabelShort ?? "null"} from storedItems.");
                }
            }

            foreach (Thing thing in containerItems)
            {
                if (!storedItems.Contains(thing))
                {
                    storedItems.Add(thing);
                    if (logChanges)
                    {
                        Logger.Warning($"Network {networkId}: Added missing item {thing.LabelShort} to storedItems.");
                    }
                }
            }
        }

        private void RefreshUI()
        {
            if (currentTab != null)
                currentTab.ItemsCached = false;
        }

        private void EnsureStorageSettingsOwner()
        {
            if (storageSettingsOwner == null)
                storageSettingsOwner = new NetworkStorageSettingsParent(this);

            if (storageSettings == null)
                storageSettings = new StorageSettings(storageSettingsOwner);
            else
                storageSettings.owner = storageSettingsOwner;
        }
    }
}
