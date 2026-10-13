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
                HashSet<Thing> yieldedThings = new HashSet<Thing>();

                foreach (Thing thing in values)
                {
                    if (yieldedThings.Add(thing))
                    {
                        yield return thing;
                    }
                }

                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (TradeUtility.PlayerSellableNow(item, trader) && yieldedThings.Add(item))
                    {
                        yield return item;
                    }
                }
            }
        }
    }
}
