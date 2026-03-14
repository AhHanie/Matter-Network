using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using System.Linq;


namespace SK_Matter_Network
{
    [StaticConstructorOnStartup]
    public class NetworkBuildingNetworkInterface : NetworkBuilding, IThingHolderEvents<Thing>, IHaulEnroute, ILoadReferenceable, IStorageGroupMember, IHaulDestination, IStoreSettingsParent, IHaulSource, IThingHolder, ISearchableContents
    {
        private ThingOwner<Thing> innerContainer;

        private StorageSettings settings;

        private StorageGroup storageGroup;

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

        public StorageSettings GetStandaloneSettings()
        {
            return settings;
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
            int networkCanAccept = ParentNetwork.CanAcceptCount(t);

            if (networkCanAccept < t.stackCount)
            {
                return false;
            }

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
            if (ParentNetwork != null && !ParentNetwork.IsBroadcastingSettingsChange)
            {
                ParentNetwork.Notify_SettingsChanged(settings);
            }

            if (base.Spawned)
            {
                base.MapHeld.listerHaulables.Notify_HaulSourceChanged(this);
            }
        }

        public void NotifyNetworkSettingsChanged()
        {
            if (ParentNetwork != null)
            {
                settings.CopyFrom(ParentNetwork.StorageSettings);
            }

            if (base.Spawned)
            {
                base.MapHeld.listerHaulables.Notify_HaulSourceChanged(this);
            }
        }

        public void Notify_ItemAdded(Thing item)
        {
            Logger.Message($"Interface: Item added: {item.stackCount} {item.def.defName}");
            base.MapHeld.listerHaulables.Notify_AddedThing(item);

            if (ParentNetwork != null && innerContainer.Contains(item))
            {
                item.Position = this.Position;
                ProcessItemTransfer(item);
            }
        }

        private void ProcessItemTransfer(Thing item)
        {
            int networkCanAccept = ParentNetwork.CanAcceptCount(item);

            if (networkCanAccept <= 0)
            {
                Logger.Message($"Network storage full - cannot accept {item.def.defName}. Item will remain in interface.");
                return;
            }

            int toTransfer = Mathf.Min(item.stackCount, networkCanAccept);

            Logger.Message($"Attempting to transfer {toTransfer} of {item.stackCount} {item.def.defName} to network");

            Thing itemsToTransfer;
            if (toTransfer >= item.stackCount)
            {
                itemsToTransfer = innerContainer.Take(item);
            }
            else
            {
                itemsToTransfer = innerContainer.Take(item, toTransfer);
            }

            int actuallyTransferred = ParentNetwork.AddItem(itemsToTransfer, itemsToTransfer.stackCount);

            if (actuallyTransferred > 0)
            {
                Logger.Message($"Successfully transferred {actuallyTransferred} {item.def.defName} from interface to network");

                if (actuallyTransferred < itemsToTransfer.stackCount)
                {
                    int remaining = itemsToTransfer.stackCount - actuallyTransferred;
                    Thing remainingItems = itemsToTransfer.SplitOff(remaining);

                    GenPlace.TryPlaceThing(remainingItems, Position, Map, ThingPlaceMode.Near);
                    Logger.Warning($"Network partially full: dropped {remaining} {item.def.defName} near interface at {Position}");
                }
            }
            else
            {
                GenPlace.TryPlaceThing(itemsToTransfer, Position, Map, ThingPlaceMode.Near);
                Logger.Warning($"Failed to transfer to network and failed to return to container: dropped {itemsToTransfer.LabelShort} near interface at {Position}");
            }
        }

        public void Notify_ItemRemoved(Thing item)
        {
        }

        public NetworkBuildingNetworkInterface()
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

        public override void DrawGUIOverlay()
        {
            return;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Deep.Look(ref settings, "settings", this);
            Scribe_References.Look(ref storageGroup, "storageGroup");
        }

        public override string GetInspectString()
        {
            sb.Clear();
            sb.Append(base.GetInspectString());
            if (base.Spawned)
            {
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
                    sb.Append("StoresThings".Translate());
                    sb.Append(": ");
                    sb.Append(HeldItems.Select((Thing x) => x.LabelShortCap).Distinct().ToCommaList());
                    sb.Append(".");
                }
            }
            return sb.ToString();
        }
    }
}
