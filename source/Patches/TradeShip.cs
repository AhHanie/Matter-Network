using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_TradeShip
    {
        [HarmonyPatch(typeof(TradeShip), "GiveSoldThingToTrader")]
        public static class GiveSoldThingToTrader
        {
            private static DataNetwork currentNetwork;
            public static void Prefix(Thing toGive, int countToGive, Pawn playerNegotiator)
            {
                NetworksMapComponent mapComp = playerNegotiator.Map.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(toGive, out DataNetwork network))
                {
                    currentNetwork = network;
                    Log.Message($"Removing {countToGive} of {toGive.def.defName} from network due to trade deal");
                    network.RemoveItem(toGive, countToGive, countToGive >= toGive.stackCount);
                }
            }

            public static void Postfix()
            {
                currentNetwork.ValidateNetwork();
                currentNetwork = null;
            }
        }
    }
}
