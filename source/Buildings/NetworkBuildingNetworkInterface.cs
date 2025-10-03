using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using System.Linq;

namespace SK_Matter_Network
{
    [StaticConstructorOnStartup]
    public class NetworkBuildingNetworkInterface : NetworkBuilding,
        IHaulEnroute, ILoadReferenceable, IStorageGroupMember,
        IHaulDestination, IStoreSettingsParent, IHaulSource, IThingHolder, ISearchableContents
    {
        private ThingOwner<Thing> fallbackContainer;

        private StorageSettings settings;
        private StorageGroup storageGroup;

        private const int InfiniteSpace = 1_000_000_000;

        public IReadOnlyList<Thing> HeldItems
        {
            get
            {
                if (ParentNetwork?.ActiveController?.innerContainer != null)
                    return ParentNetwork.ActiveController.innerContainer.InnerListForReading;
                return fallbackContainer.InnerListForReading;
            }
        }

        public ThingOwner SearchableContents => GetDirectlyHeldThings();

        public bool StorageTabVisible => true;

        public bool HaulSourceEnabled => true;

        public bool HaulDestinationEnabled => ParentNetwork?.HasActiveController ?? false;

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

        // ---- IThingHolder ----

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }
        public ThingOwner GetDirectlyHeldThings()
        {
            if (ParentNetwork?.ActiveController?.innerContainer != null && ParentNetwork.ActiveController.Spawned)
                return ParentNetwork.ActiveController.innerContainer;
            return fallbackContainer;
        }

        public StorageSettings GetStandaloneSettings() => settings;

        public StorageSettings GetStoreSettings()
        {
            if (storageGroup != null) return storageGroup.GetStoreSettings();
            return settings;
        }

        public StorageSettings GetParentStoreSettings() => def.building.fixedStorageSettings;

        public bool Accepts(Thing t)
        {
            if (ParentNetwork == null || !ParentNetwork.HasActiveController) return false;
            return ParentNetwork.CanAccept(t);
        }

        public int SpaceRemainingFor(ThingDef def)
        {
            if (ParentNetwork == null || !ParentNetwork.HasActiveController) return 0;
            return ParentNetwork.TotalCapacityBytes > ParentNetwork.UsedBytes ? InfiniteSpace : 0;
        }

        public void Notify_SettingsChanged()
        {
            if (ParentNetwork != null && !ParentNetwork.IsBroadcastingSettingsChange)
                ParentNetwork.Notify_SettingsChanged(settings);

            if (base.Spawned)
                base.MapHeld.listerHaulables.Notify_HaulSourceChanged(this);
        }

        public void NotifyNetworkSettingsChanged()
        {
            if (ParentNetwork != null)
                settings.CopyFrom(ParentNetwork.StorageSettings);

            if (base.Spawned)
                base.MapHeld.listerHaulables.Notify_HaulSourceChanged(this);
        }

        public NetworkBuildingNetworkInterface()
        {
            fallbackContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            fallbackContainer.dontTickContents = true;
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
                settings.CopyFrom(def.building.defaultStorageSettings);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (storageGroup != null)
            {
                storageGroup.RemoveMember(this);
                storageGroup = null;
            }
            base.DeSpawn(mode);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            foreach (Gizmo g in StorageSettingsClipboard.CopyPasteGizmosFor(GetStoreSettings())) yield return g;

            if (StorageTabVisible && base.MapHeld != null)
            {
                foreach (Gizmo g in StorageGroupUtility.StorageGroupMemberGizmos(this)) yield return g;
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption f in HaulSourceUtility.GetFloatMenuOptions(this, selPawn)) yield return f;
            foreach (Thing item in HeldItems)
            {
                foreach (FloatMenuOption f in item.GetFloatMenuOptions(selPawn)) yield return f;
            }
            foreach (FloatMenuOption f in base.GetFloatMenuOptions(selPawn)) yield return f;
        }

        public override void DrawGUIOverlay() { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref fallbackContainer, "fallbackContainer", this);
            Scribe_Deep.Look(ref settings, "settings", this);
            Scribe_References.Look(ref storageGroup, "storageGroup");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (fallbackContainer == null)
                    fallbackContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            }
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
                    sb.Append($"{"StorageGroupLabel".Translate()}: {storageGroup.RenamableLabel.CapitalizeFirst()} ");
                    sb.Append(storageGroup.MemberCount > 1
                        ? $"({"NumBuildings".Translate(storageGroup.MemberCount)})"
                        : $"({"OneBuilding".Translate()})");
                }

                if (ParentNetwork?.HasActiveController == true)
                {
                    int used = ParentNetwork.UsedBytes;
                    int total = ParentNetwork.TotalCapacityBytes;
                    sb.AppendLineIfNotEmpty();
                    sb.Append("MN_NetworkInterfaceInspectStorage".Translate(used, total, HeldItems.Count));
                }
                else if (ParentNetwork != null)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append("MN_NetworkInterfaceInspectNoController".Translate());
                }
            }

            return sb.ToString();
        }
    }
}
