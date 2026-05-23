using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    [StaticConstructorOnStartup]
    public class NetworkBuildingNetworkChute : NetworkBuilding,
        IHaulEnroute, ILoadReferenceable, IStorageGroupMember,
        IHaulDestination, IStoreSettingsParent, IThingHolder
    {
        private ThingOwner<Thing> fallbackContainer;
        private StorageSettings settings;
        private StorageGroup storageGroup;

        public bool StorageTabVisible => true;

        public bool HaulDestinationEnabled => ParentNetwork?.IsOperational == true;

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

        public NetworkBuildingNetworkChute()
        {
            fallbackContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            fallbackContainer.dontTickContents = true;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings() => fallbackContainer;

        public StorageSettings GetStandaloneSettings() => settings;

        public StorageSettings GetStoreSettings()
        {
            if (storageGroup != null) return storageGroup.GetStoreSettings();
            return settings;
        }

        public StorageSettings GetParentStoreSettings() => def.building.fixedStorageSettings;

        public bool Accepts(Thing t)
        {
            if (ParentNetwork == null || !ParentNetwork.IsOperational) return false;
            return ParentNetwork.CanAccept(t);
        }

        public int SpaceRemainingFor(ThingDef def)
        {
            if (ParentNetwork == null || !ParentNetwork.IsOperational) return 0;
            return ParentNetwork.SpaceRemainingFor(def);
        }

        public void Notify_SettingsChanged()
        {
            if (ParentNetwork != null && !ParentNetwork.IsBroadcastingSettingsChange)
                ParentNetwork.Notify_SettingsChanged(settings);

            if (base.Spawned)
                base.MapHeld.haulDestinationManager.Notify_HaulDestinationChangedPriority();
        }

        public void NotifyNetworkSettingsChanged()
        {
            if (ParentNetwork != null)
                settings.CopyFrom(ParentNetwork.StorageSettings);

            if (base.Spawned)
                base.MapHeld.haulDestinationManager.Notify_HaulDestinationChangedPriority();
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
            if (mode != DestroyMode.WillReplace)
                fallbackContainer.TryDropAll(base.Position, base.Map, ThingPlaceMode.Near);
            base.DeSpawn(mode);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            foreach (Gizmo g in StorageSettingsClipboard.CopyPasteGizmosFor(GetStoreSettings())) yield return g;
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
                {
                    fallbackContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
                }

                fallbackContainer.dontTickContents = true;
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

                if (ParentNetwork?.IsOperational == true)
                {
                    int used = ParentNetwork.UsedBytes;
                    int total = ParentNetwork.TotalCapacityBytes;
                    sb.AppendLineIfNotEmpty();
                    sb.Append("MN_NetworkChuteInspectStorage".Translate(used, total));
                }
                else if (ParentNetwork?.HasActiveController == true)
                {
                    sb.AppendLineIfNotEmpty();
                    sb.Append("MN_NetworkInterfaceInspectOffline".Translate(ParentNetwork.PowerModeLabel));
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
