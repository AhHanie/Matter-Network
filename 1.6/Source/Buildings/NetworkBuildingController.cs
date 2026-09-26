using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class NetworkBuildingController : NetworkBuilding,
        IThingHolder, IThingHolderTickable, IThingHolderEvents<Thing>, IHaulDestination, IStoreSettingsParent, IHaulEnroute, IApparelSource, IHaulSource
    {
        public ControllerItemOwner innerContainer;

        // innerContainer is constructed with dontTickContents = true - stored items are inert
        // "data", never ticked. Without this, Thing.DoTick() would still call
        // GetChildHolders()/GetDirectlyHeldThings() on this building every tick just to discover
        // that ThingOwner.DoTick() is a no-op.
        public bool ShouldTickContents => false;
        private StorageSettings storageSettings;
        private bool controllerConflictDisabled = false;

        private static readonly StringBuilder sb = new StringBuilder();
        public bool ControllerConflictDisabled
        {
            get => controllerConflictDisabled;
            set => controllerConflictDisabled = value;
        }

        public bool HasValidStorage => !controllerConflictDisabled && ParentNetwork?.IsOperational == true;
        public bool HasExtractionAccess => !controllerConflictDisabled && ParentNetwork?.CanExtractItems == true;

        public bool HaulDestinationEnabled => false;
        public bool HaulSourceEnabled => HasExtractionAccess;
        public bool ApparelSourceEnabled => HasExtractionAccess;

        public bool Accepts(Thing t)
        {
            if (t == null || t.Destroyed || !HasValidStorage || ParentNetwork == null)
            {
                return false;
            }

            // HaulDestinationEnabled is always false below, so this controller is never registered
            // as a haul destination and GenPlace/UI/direct placement never reach this method. The
            // real callers are vanilla's "is this already-stored item's current location still
            // valid" checks (StoreUtility.IsInValidStorage and friends), reached via Thing.ParentHolder
            // resolving straight to this controller for every item sitting in innerContainer.
            //
            // For a resident item, capacity and quota are irrelevant to whether its current location
            // remains valid - only the filter matters. Answering with the full CanAccept/haul-search
            // capacity check here would mark every resident stack as invalid storage the moment the
            // network fills up or a quota is hit, flooding the haulable list for items that aren't
            // actually misplaced. A resident that the filter now disallows still correctly returns
            // false here and remains eligible for vanilla priority hauling.
            if (ReferenceEquals(t.holdingOwner, innerContainer))
            {
                return ParentNetwork.StorageSettingsAllow(t);
            }

            // Any other caller (compat code, direct queries against an item not actually held by
            // this controller) gets the exact, uncached admission check rather than the
            // positive-only haul-search cache.
            return ParentNetwork.CanAccept(t);
        }

        public void Notify_HaulDestinationChangedPriority() { }

        public StorageSettings GetStoreSettings()
        {
            return ParentNetwork?.StorageSettings ?? storageSettings;
        }

        public StorageSettings GetParentStoreSettings()
        {
            return def.building.fixedStorageSettings;
        }

        public bool StorageTabVisible => false;

        public void Notify_SettingsChanged() { }

        public int SpaceRemainingFor(ThingDef def)
        {
            if (ParentNetwork == null || !ParentNetwork.IsOperational) return 0;
            return ParentNetwork.SpaceRemainingFor(def);
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings() => innerContainer;

        public bool RemoveApparel(Apparel apparel)
        {
            bool removed = innerContainer.Remove(apparel);
            if (removed)
            {
                ParentNetwork?.MarkBytesDirty();
            }
            return removed;
        }

        public void Notify_ItemAdded(Thing item)
        {
            if (!item.Destroyed)
            {
                MapHeld.listerHaulables.Notify_AddedThing(item);
            }
        }

        public void Notify_ItemRemoved(Thing item)
        {
            MapHeld.listerHaulables.Notify_DeSpawned(item);

            DataNetwork network = ParentNetwork;
            if (network != null)
            {
                network.storedItems.Remove(item);
                network.MarkBytesDirty();
            }
        }

        public NetworkBuildingController()
        {
            innerContainer = new ControllerItemOwner(this);
        }

        public override void PostMake()
        {
            base.PostMake();
            storageSettings = new StorageSettings(this);
            if (def.building.defaultStorageSettings != null)
                storageSettings.CopyFrom(def.building.defaultStorageSettings);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            base.DeSpawn(mode);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (mode != DestroyMode.WillReplace)
            {
                ParentNetwork?.ArchiveAllControllerItemsToDisks(
                    dropRemainder: true,
                    fallbackCell: Position,
                    fallbackMap: Map);
            }
            base.Destroy(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Deep.Look(ref storageSettings, "storageSettings", this);
            Scribe_Values.Look(ref controllerConflictDisabled, "controllerConflictDisabled", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (innerContainer == null)
                    innerContainer = new ControllerItemOwner(this);

                innerContainer.SetController(this);
            }
        }

        public override string GetInspectString()
        {
            sb.Clear();
            sb.Append(base.GetInspectString());

            if (Spawned && ParentNetwork != null)
            {
                int used = ParentNetwork.UsedBytes;
                int total = ParentNetwork.TotalCapacityBytes;

                sb.AppendLineIfNotEmpty();
                sb.Append("MN_ControllerInspectStorage".Translate(used, total));

                if (ParentNetwork.OvercommittedBytes > 0)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append("MN_ControllerInspectOvercommitted".Translate(ParentNetwork.OvercommittedBytes));
                }

                if (controllerConflictDisabled)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append("MN_ControllerInspectConflictDisabled".Translate());
                }

                sb.AppendLineIfNotEmpty();
                sb.Append("MN_ControllerInspectPower".Translate(
                    ParentNetwork.PowerModeLabel,
                    ParentNetwork.RequiredPowerDrawWatts,
                    ParentNetwork.StoredReserveEnergyWd.ToString("F0"),
                    ParentNetwork.MaxReserveEnergyWd.ToString("F0")));

                if (Prefs.DevMode)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append(ParentNetwork.GetAcceptanceCacheDebugString());
                }
            }
            else if (Spawned)
            {
                sb.AppendLineIfNotEmpty();
                sb.Append("MN_ControllerInspectNoNetwork".Translate());
            }

            return sb.ToString();
        }
    }
}
