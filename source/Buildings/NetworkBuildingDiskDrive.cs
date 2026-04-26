using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using System.Linq;

namespace SK_Matter_Network
{
    [StaticConstructorOnStartup]
    public class NetworkBuildingDiskDrive : NetworkBuilding,
        IThingHolderEvents<Thing>, IHaulEnroute, ILoadReferenceable, IStorageGroupMember,
        IHaulDestination, IStoreSettingsParent, IHaulSource, IThingHolder, ISearchableContents
    {
        private ThingOwner<Thing> innerContainer;
        private StorageSettings settings;
        private StorageGroup storageGroup;
        private bool locked = false;

        public int MaximumItems => def.building.maxItemsInCell * def.size.Area;
        public IReadOnlyList<Thing> HeldItems => innerContainer.InnerListForReading;
        public ThingOwner SearchableContents => innerContainer;
        public bool StorageTabVisible => true;
        public bool HaulSourceEnabled => true;
        public bool HaulDestinationEnabled => true;
        public bool Locked => locked;

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

        StorageGroup IStorageGroupMember.Group
        {
            get => storageGroup;
            set => storageGroup = value;
        }

        bool IStorageGroupMember.DrawConnectionOverlay => base.Spawned;
        Map IStorageGroupMember.Map => base.MapHeld;
        string IStorageGroupMember.StorageGroupTag => def.building.storageGroupTag;
        StorageSettings IStorageGroupMember.StoreSettings => GetStoreSettings();
        StorageSettings IStorageGroupMember.ParentStoreSettings => GetParentStoreSettings();
        StorageSettings IStorageGroupMember.ThingStoreSettings => settings;
        bool IStorageGroupMember.DrawStorageTab => true;
        bool IStorageGroupMember.ShowRenameButton => base.Faction == Faction.OfPlayer;

        private static readonly StringBuilder sb = new StringBuilder();

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings() => innerContainer;

        public StorageSettings GetStoreSettings()
        {
            if (storageGroup != null) return storageGroup.GetStoreSettings();
            return settings;
        }

        public StorageSettings GetParentStoreSettings() => def.building.fixedStorageSettings;

        public bool Accepts(Thing t)
        {
            if (HeldItems.Count >= MaximumItems && !innerContainer.Contains(t)) return false;
            return GetStoreSettings().AllowedToAccept(t) && innerContainer.CanAcceptAnyOf(t);
        }

        public int SpaceRemainingFor(ThingDef _) => MaximumItems - HeldItems.Count;

        public void Notify_SettingsChanged()
        {
            if (base.Spawned)
                base.MapHeld.listerHaulables.Notify_HaulSourceChanged(this);
        }

        public void Notify_ItemAdded(Thing disk)
        {
            base.MapHeld.listerHaulables.Notify_AddedThing(disk);
            ParentNetwork?.NotifyDiskCapacityChanged();
            Logger.Message($"Disk {disk.LabelShort} added to drive at {Position}. Recalculated capacity.");
        }

        public void Notify_ItemRemoved(Thing disk)
        {
            ParentNetwork?.NotifyDiskRemovedFromDrive(disk);
            Logger.Message($"Disk {disk.LabelShort} removed from drive at {Position}. Recalculated capacity.");
        }

        public int GetTotalCapacityBytes()
        {
            int total = 0;
            foreach (Thing disk in HeldItems)
            {
                CompDiskCapacity cap = disk.TryGetComp<CompDiskCapacity>();
                if (cap != null && cap.CanContributeActiveCapacity) total += cap.MaxBytes;
            }
            return total;
        }

        public int GetArchivedDiskCount()
        {
            int count = 0;
            foreach (Thing disk in HeldItems)
            {
                CompDiskCapacity cap = disk.TryGetComp<CompDiskCapacity>();
                if (cap != null && cap.HasArchivedItems)
                    count++;
            }
            return count;
        }

        public int GetArchivedBytes()
        {
            int total = 0;
            foreach (Thing disk in HeldItems)
            {
                CompDiskCapacity cap = disk.TryGetComp<CompDiskCapacity>();
                if (cap != null)
                    total += cap.ArchivedUsedBytes;
            }
            return total;
        }

        public NetworkBuildingDiskDrive()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            innerContainer.dontTickContents = true;
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
            if (Locked)
            {
                Map.haulDestinationManager.RemoveHaulSource(this);
            }
        }

        public override void PostMake()
        {
            base.PostMake();
            settings = new StorageSettings(this);
            if (def.building.defaultStorageSettings != null)
                settings.CopyFrom(def.building.defaultStorageSettings);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (mode != DestroyMode.WillReplace)
                ParentNetwork?.NotifyDiskDriveWillBeUnavailable(this);

            if (storageGroup != null)
            {
                storageGroup.RemoveMember(this);
                storageGroup = null;
            }
            if (mode != DestroyMode.WillReplace)
                innerContainer.TryDropAll(base.Position, base.Map, ThingPlaceMode.Near);
            base.DeSpawn(mode);
        }

        public override void DrawExtraSelectionOverlays() { }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;

            yield return new Command_Action
            {
                defaultLabel = (locked ? "MN_DiskDriveUnlockLabel" : "MN_DiskDriveLockLabel").Translate(),
                defaultDesc = "MN_DiskDriveLockDesc".Translate(),
                icon = (locked ? Resources.LockedIcon : Resources.UnlockedIcon).Texture,
                action = delegate
                {
                    locked = !locked;
                    Messages.Message(
                        (locked ? "MN_DiskDriveLockedMessage" : "MN_DiskDriveUnlockedMessage").Translate(LabelCap),
                        this, MessageTypeDefOf.TaskCompletion);
                    if (locked)
                    {
                        Map.haulDestinationManager.RemoveHaulSource(this);
                    }
                    else
                    {
                        Map.haulDestinationManager.AddHaulSource(this);
                    }
                }
            };

            foreach (Gizmo g in StorageSettingsClipboard.CopyPasteGizmosFor(GetStoreSettings())) yield return g;

            if (StorageTabVisible && base.MapHeld != null)
            {
                foreach (Gizmo g in StorageGroupUtility.StorageGroupMemberGizmos(this)) yield return g;
            }

            if (Prefs.DevMode && HeldItems.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Debug: drop random disk",
                    defaultDesc = "Drop one random disk from this drive.",
                    action = DropRandomDiskForDebug
                };
            }
        }

        private void DropRandomDiskForDebug()
        {
            if (!Spawned || HeldItems.Count == 0)
                return;

            Thing disk = HeldItems.RandomElement();
            if (innerContainer.TryDrop(disk, Position, Map, ThingPlaceMode.Near, out Thing dropped))
            {
                Messages.Message($"Dropped {dropped.LabelShort} from {LabelShort}.", dropped, MessageTypeDefOf.NeutralEvent);
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption f in HaulSourceUtility.GetFloatMenuOptions(this, selPawn)) yield return f;
            foreach (Thing disk in HeldItems)
            {
                foreach (FloatMenuOption f in disk.GetFloatMenuOptions(selPawn)) yield return f;
            }
            foreach (FloatMenuOption f in base.GetFloatMenuOptions(selPawn)) yield return f;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Deep.Look(ref settings, "settings", this);
            Scribe_References.Look(ref storageGroup, "storageGroup");
            Scribe_Values.Look(ref locked, "locked", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (innerContainer == null)
                    innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            }
        }

        public override string GetInspectString()
        {
            sb.Clear();
            sb.Append(base.GetInspectString());

            if (base.Spawned)
            {
                int cap = GetTotalCapacityBytes();
                if (cap > 0 || HeldItems.Count > 0)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append("MN_DiskDriveInspectCapacity".Translate(cap));
                }

                if (storageGroup != null)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append($"{"StorageGroupLabel".Translate()}: {storageGroup.RenamableLabel.CapitalizeFirst()} ");
                    sb.Append(storageGroup.MemberCount > 1
                        ? $"({"NumBuildings".Translate(storageGroup.MemberCount)})"
                        : $"({"OneBuilding".Translate()})");
                }

                if (HeldItems.Count > 0)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append("MN_DiskDriveInspectDisks".Translate(HeldItems.Select(x => x.LabelShortCap).Distinct().ToCommaList()));
                }

                int archivedDiskCount = GetArchivedDiskCount();
                if (archivedDiskCount > 0)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append("MN_DiskDriveInspectArchived".Translate(GetArchivedBytes(), archivedDiskCount));
                }
            }

            return sb.ToString();
        }
    }
}
