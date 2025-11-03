using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_TradeUtility
    {
        [HarmonyPatch(typeof(TradeUtility), "AllLaunchableThingsForTrade")]
        public static class AllLaunchableThingsForTrade
        {
            public static IEnumerable<Thing> Postfix(IEnumerable<Thing> values, Map map, ITrader trader)
            {
                foreach (Thing thing in values)
                {
                    yield return thing;
                }

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                foreach (DataNetwork network in mapComp.Networks)
                {
                    foreach (Thing item in network.StoredItems)
                    {
                        if (TradeUtility.PlayerSellableNow(item, trader))
                        {
                            Log.Message($"Adding item: {item.def.defName} to trade");
                            yield return item;
                        }
                    }
                }
            }
        }
    }
}
