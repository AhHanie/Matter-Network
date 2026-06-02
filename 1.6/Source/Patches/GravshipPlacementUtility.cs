using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network.Patches
{
    [HarmonyPatch(typeof(GravshipPlacementUtility), "PostSwapMap")]
    public static class Patch_GravshipPlacementUtility_PostSwapMap
    {
        public static void Postfix(Gravship gravship, List<Thing> gravshipThings)
        {
            if (!ModsConfig.OdysseyActive) return;

            HashSet<DataNetwork> affectedNetworks = new HashSet<DataNetwork>();
            foreach (Thing thing in gravshipThings)
            {
                if (thing is NetworkBuilding nb && nb.ParentNetwork != null)
                    affectedNetworks.Add(nb.ParentNetwork);
            }

            foreach (DataNetwork network in affectedNetworks)
            {
                Map newMap = null;
                foreach (NetworkBuilding b in network.Buildings)
                {
                    if (b != null && b.Spawned && b.Map != null)
                    {
                        newMap = b.Map;
                        break;
                    }
                }

                if (newMap != null)
                {
                    Logger.Message($"Refreshing network {network.NetworkId} after gravship landing");
                    network.RefreshAfterMapChange(newMap);
                }
            }

            GravshipNetworkTransferTracker.Clear();
        }
    }
}
