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
                if (t.MapHeld == null)
                {
                    return true;
                }

                NetworksMapComponent mapComp = t.MapHeld.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(t, out DataNetwork network))
                {
                    reason = null;
                    __result = true;
                    return false;
                }

                return true;
            }
        }
    }
}
