using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class NetworkBuildingController : NetworkBuilding,
        IThingHolder, IThingHolderEvents<Thing>, IHaulDestination, IStoreSettingsParent, IHaulEnroute, IApparelSource, IHaulSource
    {
        public ControllerItemOwner innerContainer;
        private StorageSettings storageSettings;
        private bool controllerConflictDisabled = false;

        private static readonly StringBuilder sb = new StringBuilder();
        public bool ControllerConflictDisabled
        {
            get => controllerConflictDisabled;
            set => controllerConflictDisabled = value;
        }

        public bool HasValidStorage => !controllerConflictDisabled && ParentNetwork != null;

        public bool HaulDestinationEnabled => false;
        public bool HaulSourceEnabled => HasValidStorage;
        public bool ApparelSourceEnabled => HasValidStorage;

        public bool Accepts(Thing t)
        {
            if (!HasValidStorage || ParentNetwork == null)
            {
                return false;
            }

            return ParentNetwork.AcceptsItem(t);
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
            if (ParentNetwork == null || !ParentNetwork.HasActiveController) return 0;
            return System.Math.Max(0, ParentNetwork.TotalCapacityBytes - ParentNetwork.UsedBytes);
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings() => innerContainer;

        public bool RemoveApparel(Apparel apparel)
        {
            return innerContainer.Remove(apparel);
        }

        public void Notify_ItemAdded(Thing item)
        {
            // Handled by ControllerItemOwner.TryAdd override. No-op here.
        }

        public void Notify_ItemRemoved(Thing item)
        {
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
