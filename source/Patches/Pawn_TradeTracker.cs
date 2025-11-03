using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Pawn_TradeTracker
    {
        public static bool PAWNTRADER_COLONYTHINGSBUY_FLAG = false;
        public static Map PAWNTRADER_COLONYTHINGSBUY_MAP;

        [HarmonyPatch(typeof(Pawn_TraderTracker), "ReachableForTrade")]
        public static class ReachableForTrade
        {
            public static bool Prefix(Thing thing, ref bool __result)
            {
                if (thing.MapHeld == null)
                {
                    return true;
                }

                NetworksMapComponent mapComp = thing.MapHeld.GetComponent<NetworksMapComponent>();

                if (!mapComp.TryGetItemNetwork(thing, out DataNetwork network))
                {
                    return true;
                }

                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_TraderTracker), "GiveSoldThingToTrader")]
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

        [HarmonyPatch(typeof(Pawn_TraderTracker), "ColonyThingsWillingToBuy")]
        public static class ColonyThingsWillingToBuy
        {
            // Initialized here, cleaned up @ TradeDeal after items list is evaluated
            public static void Prefix(Pawn playerNegotiator)
            {
                PAWNTRADER_COLONYTHINGSBUY_FLAG = true;
                PAWNTRADER_COLONYTHINGSBUY_MAP = playerNegotiator.Map;
            }
        }
    }
}
