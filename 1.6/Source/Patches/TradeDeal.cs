using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_TradeDeal
    {
        [HarmonyPatch(typeof(TradeDeal), "InSellablePosition")]
        public static class InSellablePosition
        {
            public static bool Prefix(Thing t, ref string reason, ref bool __result)
            {
                Map map = t?.MapHeld;
                if (map == null) return true;

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp == null || !mapComp.TryGetItemNetwork(t, out DataNetwork _)) return true;

                reason = null;
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(TradeDeal), "TryExecute")]
        public static class TryExecute
        {
            public static void Prefix(ref Map __state)
            {
                __state = TradeSession.playerNegotiator?.Map;
            }
            public static void Postfix(Map __state)
            {
                if (__state == null) return;

                NetworksMapComponent mapComp = __state.GetComponent<NetworksMapComponent>();
                if (mapComp == null) return;

                foreach (DataNetwork network in mapComp.Networks)
                {
                    network.MarkBytesDirty();
                }
            }
        }
    }
}
