using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using System.Linq;


namespace SK_Matter_Network
{
    [StaticConstructorOnStartup]
    public class NetworkBuildingDiskDrive : NetworkBuilding, IThingHolderEvents<Thing>, IHaulEnroute, ILoadReferenceable, IStorageGroupMember, IHaulDestination, IStoreSettingsParent, IHaulSource, IThingHolder, ISearchableContents
    {
        private ThingOwner<Thing> innerContainer;

        private StorageSettings settings;

        private StorageGroup storageGroup;

        private int cachedMaxBytes = 0;
        private int cachedUsedBytes = 0;
        private bool cacheDirty = true;
        private Dictionary<Thing, Thing> itemToDisk;

        public int MaximumItems => def.building.maxItemsInCell * def.size.Area;

        public IReadOnlyList<Thing> HeldItems => innerContainer.InnerListForReading;

        public IEnumerable<float> CellsFilledPercentage
        {
            get
            {
                int books = HeldItems.Count;
                for (int i = 0; i < def.size.Area; i++)
                {
                    int num = Mathf.Min(books, def.building.maxItemsInCell);
                    books -= num;
                    yield return Mathf.Clamp01((float)num / (float)def.building.maxItemsInCell);
                }
            }
        }

        public ThingOwner SearchableContents => innerContainer;

        public bool StorageTabVisible => true;

        public bool HaulSourceEnabled => true;

        public bool HaulDestinationEnabled => true;

        StorageGroup IStorageGroupMember.Group
        {
            get
            {
                return storageGroup;
            }
            set
            {
                storageGroup = value;
            }
        }

        bool IStorageGroupMember.DrawConnectionOverlay => base.Spawned;

        Map IStorageGroupMember.Map => base.MapHeld;

        string IStorageGroupMember.StorageGroupTag => def.building.storageGroupTag;

        StorageSettings IStorageGroupMember.StoreSettings => GetStoreSettings();

        StorageSettings IStorageGroupMember.ParentStoreSettings => GetParentStoreSettings();

        StorageSettings IStorageGroupMember.ThingStoreSettings => settings;

        bool IStorageGroupMember.DrawStorageTab => true;

        bool IStorageGroupMember.ShowRenameButton => base.Faction == Faction.OfPlayer;

        private static StringBuilder sb = new StringBuilder();

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        public StorageSettings GetStoreSettings()
        {
            if (storageGroup != null)
            {
                return storageGroup.GetStoreSettings();
            }
            return settings;
        }

        public StorageSettings GetParentStoreSettings()
        {
            return def.building.fixedStorageSettings;
        }

        public bool Accepts(Thing t)
        {
            if (HeldItems.Count >= MaximumItems)
            {
                if (!innerContainer.InnerListForReading.Contains(t))
                {
                    return false;
                }
            }
            if (GetStoreSettings().AllowedToAccept(t))
            {
                return innerContainer.CanAcceptAnyOf(t);
            }
            return false;
        }

        public int SpaceRemainingFor(ThingDef _)
        {
            return MaximumItems - HeldItems.Count;
        }

        public void Notify_SettingsChanged()
        {
            if (base.Spawned)
            {
                base.MapHeld.listerHaulables.Notify_HaulSourceChanged(this);
            }
        }

        public void Notify_ItemAdded(Thing item)
        {
            base.MapHeld.listerHaulables.Notify_AddedThing(item);
        }

        public void Notify_ItemRemoved(Thing item)
        {
        }

        public NetworkBuildingDiskDrive()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            innerContainer.dontTickContents = true;
            itemToDisk = new Dictionary<Thing, Thing>();
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (storageGroup != null && map != storageGroup.Map)
            {
                StorageSettings storeSettings = storageGroup.GetStoreSettings();
                storageGroup.RemoveMember(this);
                storageGroup = null;
                settings.CopyFrom(storeSettings);
            }
        }

        public override void PostMake()
        {
            base.PostMake();
            settings = new StorageSettings(this);
            if (def.building.defaultStorageSettings != null)
            {
                settings.CopyFrom(def.building.defaultStorageSettings);
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (storageGroup != null)
            {
                storageGroup?.RemoveMember(this);
                storageGroup = null;
            }
            if (mode != DestroyMode.WillReplace)
            {
                innerContainer.TryDropAll(base.Position, base.Map, ThingPlaceMode.Near);
            }
            base.DeSpawn(mode);
        }

        public override void DrawExtraSelectionOverlays()
        {
            
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            foreach (Gizmo item2 in StorageSettingsClipboard.CopyPasteGizmosFor(GetStoreSettings()))
            {
                yield return item2;
            }
            if (StorageTabVisible && base.MapHeld != null)
            {
                foreach (Gizmo item3 in StorageGroupUtility.StorageGroupMemberGizmos(this))
                {
                    yield return item3;
                }
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption floatMenuOption in HaulSourceUtility.GetFloatMenuOptions(this, selPawn))
            {
                yield return floatMenuOption;
            }
            foreach (Thing heldItem in HeldItems)
            {
                foreach (FloatMenuOption floatMenuOption2 in heldItem.GetFloatMenuOptions(selPawn))
                {
                    yield return floatMenuOption2;
                }
            }
            foreach (FloatMenuOption floatMenuOption3 in base.GetFloatMenuOptions(selPawn))
            {
                yield return floatMenuOption3;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Deep.Look(ref settings, "settings", this);
            Scribe_References.Look(ref storageGroup, "storageGroup");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (itemToDisk == null)
                {
                    itemToDisk = new Dictionary<Thing, Thing>();
                }
                RebuildItemToDiskMapping();
            }
        }

        public void UpdateStorageCache()
        {
            cachedMaxBytes = 0;
            cachedUsedBytes = 0;

            foreach (Thing disk in HeldItems)
            {
                CompDiskDataStorage diskStorage = disk.TryGetComp<CompDiskDataStorage>();
                cachedMaxBytes += diskStorage.MaxBytes;
                cachedUsedBytes += diskStorage.UsedBytes;
            }

            cacheDirty = false;
        }

        public override string GetInspectString()
        {
            sb.Clear();
            sb.Append(base.GetInspectString());

            if (base.Spawned)
            {
                if (cacheDirty)
                {
                    UpdateStorageCache();
                }

                // Add storage capacity info
                if (cachedMaxBytes > 0 || HeldItems.Count > 0)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append($"Disk Storage: {cachedUsedBytes}/{cachedMaxBytes} bytes");
                    int availableBytes = cachedMaxBytes - cachedUsedBytes;
                    if (availableBytes > 0)
                    {
                        sb.Append($" ({availableBytes} available)");
                    }
                }

                if (storageGroup != null)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append(string.Format("{0}: {1} ", "StorageGroupLabel".Translate(), storageGroup.RenamableLabel.CapitalizeFirst()));
                    if (storageGroup.MemberCount > 1)
                    {
                        sb.Append(string.Format("({0})", "NumBuildings".Translate(storageGroup.MemberCount)));
                    }
                    else
                    {
                        sb.Append(string.Format("({0})", "OneBuilding".Translate()));
                    }
                }

                if (HeldItems.Count > 0)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append("Contains Disks: ");
                    sb.Append(HeldItems.Select((Thing x) => x.LabelShortCap).Distinct().ToCommaList());
                    sb.Append(".");
                }
            }

            return sb.ToString();
        }

        public int CanAcceptCount(Thing item)
        {
            if (item == null || item.Destroyed)
            {
                return 0;
            }

            int totalCanAccept = 0;

            foreach (Thing disk in HeldItems)
            {
                CompDiskDataStorage diskStorage = disk.TryGetComp<CompDiskDataStorage>();
                if (diskStorage != null)
                {
                    int diskCanAccept = diskStorage.CanAcceptCount(item);
                    totalCanAccept += diskCanAccept;
                }
            }

            return totalCanAccept;
        }

        public (int count, List<Thing> storedItems) AddItem(Thing item, int count)
        {
            int itemsAdded = 0;
            List<Thing> storedItems = new List<Thing>();

            foreach (Thing disk in HeldItems)
            {
                if (itemsAdded >= count)
                {
                    break;
                }

                CompDiskDataStorage diskStorage = disk.TryGetComp<CompDiskDataStorage>();
                int needToAdd = count - itemsAdded;
                int diskCanAccept = diskStorage.CanAcceptCount(item);
                if (diskCanAccept > 0)
                {
                    int toAddThisRound = Mathf.Min(needToAdd, diskCanAccept);
                    Thing itemToStore;
                    if (toAddThisRound >= item.stackCount)
                    {
                        itemToStore = item;
                    }
                    else
                    {
                        itemToStore = item.SplitOff(toAddThisRound);
                    }

                    var actuallyAdded = diskStorage.TryAddItemToComp(itemToStore);
                    itemsAdded += actuallyAdded;

                    if (actuallyAdded > 0)
                    {
                        storedItems.Add(item);
                        itemToDisk[itemToStore] = disk;
                    }
                }
            }

            if (itemsAdded > 0)
            {
                cacheDirty = true;
                Log.Message($"Disk drive at {Position} stored {itemsAdded} of {item.def.defName}");
            }

            return (itemsAdded, storedItems);
        }

        public bool RemoveItem(Thing item, int count)
        {
            if (!itemToDisk.TryGetValue(item, out Thing disk))
            {
                Log.Warning($"Attempted to remove item {item.LabelShort} but it's not tracked in disk drive at {Position}");
                return false;
            }

            CompDiskDataStorage diskStorage = disk.TryGetComp<CompDiskDataStorage>();

            // Actually remove the item from the disk's inner container
            bool success = diskStorage.RemoveItemFromStorage(item, count);

            if (success)
            {
                if (count >= item.stackCount || item.Destroyed)
                {
                    // Item fully removed from network
                    itemToDisk.Remove(item);
                    Log.Message($"Removed {item.LabelShort} from disk {disk.LabelShort} in disk drive at {Position}");
                }
                else
                {
                    // Partial removal - item still exists with reduced stack
                    Log.Message($"Removed {count} of {item.LabelShort} from disk {disk.LabelShort} in disk drive at {Position}");
                }

                cacheDirty = true;
                return true;
            }

            return false;
        }

        private void RebuildItemToDiskMapping()
        {
            itemToDisk.Clear();

            foreach (Thing disk in HeldItems)
            {
                CompDiskDataStorage diskStorage = disk.TryGetComp<CompDiskDataStorage>();
                if (diskStorage != null)
                {
                    foreach (Thing storedItem in diskStorage.GetAllStoredItems())
                    {
                        itemToDisk[storedItem] = disk;
                    }
                }
            }

            Log.Message($"Rebuilt item-to-disk mapping for disk drive at {Position}: {itemToDisk.Count} items tracked");
        }
        public List<Thing> GetAllStoredItems()
        {
            List<Thing> allItems = new List<Thing>();

            foreach (Thing disk in HeldItems)
            {
                CompDiskDataStorage diskStorage = disk.TryGetComp<CompDiskDataStorage>();
                if (diskStorage != null)
                {
                    allItems.AddRange(diskStorage.GetAllStoredItems());
                }
            }

            return allItems;
        }
    }
}
