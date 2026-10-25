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
        IHaulDestination, IStoreSettingsParent, IThingHolder, IThingHolderTickable
    {
        private ThingOwner<Thing> fallbackContainer;
        private StorageSettings settings;
        private StorageGroup storageGroup;

        // fallbackContainer is constructed with dontTickContents = true - stored items are inert
        // "data", never ticked. Without this, Thing.DoTick() would still call
        // GetChildHolders()/GetDirectlyHeldThings() on this building every tick just to discover
        // that ThingOwner.DoTick() is a no-op.
        public bool ShouldTickContents => false;

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

        public StorageGroup LinkedStorageGroup => storageGroup;

        // Settings this chute would use on its own: its storage group's if linked, else its private
        // settings. Used to seed a new network and while the chute is not part of one.
        public StorageSettings GetDetachedStoreSettings()
        {
            if (storageGroup != null) return storageGroup.GetStoreSettings();
            return settings;
        }

        public void StoreNetworkPolicyAsFallback(StorageSettings networkPolicy)
        {
            NetworkSettingsSync.CopyQuietly(settings, networkPolicy);
        }

        // A connected chute always reports the network's own settings object, so admission,
        // current-storage priority and destination priority agree with the controller.
        public StorageSettings GetStoreSettings()
        {
            if (ParentNetwork != null) return ParentNetwork.StorageSettings;
            return GetDetachedStoreSettings();
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

        // Vanilla calls this after a storage group link/unlink and when the private settings change.
        // A connected chute's policy lives on the network, so only a group link needs handling.
        public void Notify_SettingsChanged()
        {
            if (NetworkSettingsSync.IsSuppressed) return;

            if (ParentNetwork != null)
            {
                NetworkSettingsSync.NotifyEndpointGroupChanged(this, storageGroup);
                return;
            }

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
                NetworkSettingsSync.CopyQuietly(settings, storeSettings);
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
            bool preserveContents = mode == DestroyMode.WillReplace
                || NetworkBuildingTransferTracker.ShouldPreserveDuringTransfer(this);
            if (!preserveContents)
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
