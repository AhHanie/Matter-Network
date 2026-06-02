using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    public class NetworkBuilding : Building
    {
        private DataNetwork parentNetwork;
        // Persisted so save/reload mid-transit preserves full-network identity on landing.
        private bool wasFullNetworkMoveParticipant;
        // Quotas captured from the original network before a partial-move removal.
        private Dictionary<ThingDef, int> pendingQuotaTransfer;

        public DataNetwork ParentNetwork { get => parentNetwork; set => parentNetwork = value; }

        public override void PreSwapMap()
        {
            base.PreSwapMap();
            GravshipNetworkTransferTracker.RegisterForTransfer(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            bool beingTransportedOnGravship = BeingTransportedOnGravship;
            Map oldMap = Map;
            DataNetwork network = parentNetwork;
            NetworksMapComponent mapComp = oldMap.GetComponent<NetworksMapComponent>();

            if (!beingTransportedOnGravship)
            {
                mapComp.RemoveBuilding(this);
            }
            else if (network == null || !GravshipNetworkTransferTracker.IsFullNetworkMove(network))
            {
                // Partial move: capture original quotas before removing from the network.
                wasFullNetworkMoveParticipant = false;
                if (network?.ItemQuotaByDef != null)
                {
                    pendingQuotaTransfer = new Dictionary<ThingDef, int>();
                    foreach (KeyValuePair<ThingDef, int> kv in network.ItemQuotaByDef)
                        pendingQuotaTransfer[kv.Key] = kv.Value;
                    if (pendingQuotaTransfer.Count == 0)
                        pendingQuotaTransfer = null;
                }
                mapComp.RemoveBuilding(this);
            }
            else
            {
                // Full-network move: preserve the DataNetwork across maps.
                wasFullNetworkMoveParticipant = true;
            }

            base.DeSpawn(mode);

            if (beingTransportedOnGravship && network != null
                && wasFullNetworkMoveParticipant
                && !network.HasSpawnedBuildingOnMap(oldMap))
            {
                mapComp.RemoveNetwork(network);
            }

            Logger.Message($"Removing building from map comp, {Position}");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad)
            {
                // Already on its map from a save; no transit state applies.
                wasFullNetworkMoveParticipant = false;
                pendingQuotaTransfer = null;
                return;
            }

            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();

            // wasFullNetworkMoveParticipant is persisted, so this works after save/reload mid-transit.
            if (wasFullNetworkMoveParticipant && parentNetwork != null)
            {
                parentNetwork.SetMap(map);
                mapComp.AddNetwork(parentNetwork);
                Logger.Message($"Reattaching full-network transported building to map comp, {Position}");
                return;
            }

            mapComp.AddBuilding(this);
            // Apply quotas captured from the original network before the partial-move removal.
            if (pendingQuotaTransfer != null && parentNetwork != null)
            {
                parentNetwork.MergeMissingQuotasFrom(pendingQuotaTransfer);
                pendingQuotaTransfer = null;
            }
            Logger.Message($"Adding building to map comp, {Position} {parentNetwork != null}");
        }

        public override void PostSwapMap()
        {
            base.PostSwapMap();
            wasFullNetworkMoveParticipant = false;
            pendingQuotaTransfer = null;
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();

            if (ParentNetwork != null)
            {
                if (!string.IsNullOrEmpty(baseString))
                {
                    baseString += "\n";
                }
                baseString += "MN_NetworkInspectNetworkId".Translate(ParentNetwork.NetworkId, ParentNetwork.BuildingCount);
            }

            return baseString;
        }

        public virtual void NetworkTick(int currentTick)
        {
            if (AllComps == null)
            {
                return;
            }

            for (int i = 0; i < AllComps.Count; i++)
            {
                if (AllComps[i] is INetworkTickable tickable)
                {
                    tickable.NetworkTick(currentTick);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref parentNetwork, "parentNetwork");
            Scribe_Values.Look(ref wasFullNetworkMoveParticipant, "wasFullNetworkMoveParticipant", false);
            Scribe_Collections.Look(ref pendingQuotaTransfer, "pendingQuotaTransfer", LookMode.Def, LookMode.Value);
        }
    }
}
