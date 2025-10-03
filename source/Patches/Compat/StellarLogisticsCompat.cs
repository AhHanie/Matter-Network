using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class StellarLogisticsCompat
    {
        private const string PackageId = "f.stellarlogistics";
        private const string WindowTypeName = "StellarLogistics.Window_MarketTerminal";
        private const string ModTypeName = "StellarLogistics.StellarLogisticsMod";
        private const string UtilityTypeName = "StellarLogistics.StellarUtility";

        private static readonly System.Type windowType = AccessTools.TypeByName(WindowTypeName);
        private static readonly System.Type modType = AccessTools.TypeByName(ModTypeName);
        private static readonly System.Type utilityType = AccessTools.TypeByName(UtilityTypeName);
        private static readonly MethodInfo addToLotsMethod = AccessTools.Method(windowType, "AddToLots");
        private static readonly MethodInfo filterSellLotsMethod = AccessTools.Method(windowType, "FilterSellLots");
        private static readonly MethodInfo isSellBlacklistedMethod = AccessTools.Method(modType, "IsSellBlacklisted");
        private static readonly FieldInfo cachedSellLotsField = AccessTools.Field(windowType, "cachedSellLots");

        private static bool IsWindowCompatAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && windowType != null
                && modType != null
                && addToLotsMethod != null
                && filterSellLotsMethod != null
                && isSellBlacklistedMethod != null
                && cachedSellLotsField != null;
        }

        private static bool IsUtilityCompatAvailable()
        {
            return ModsConfig.IsActive(PackageId) && utilityType != null;
        }

        [HarmonyPatch]
        public static class GetAllItemsNearBeacons
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsUtilityCompatAvailable();
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(utilityType, "GetAllItemsNearBeacons");
            }

            public static IEnumerable<Thing> Postfix(IEnumerable<Thing> values, Map map)
            {
                HashSet<Thing> yieldedThings = new HashSet<Thing>();

                if (values != null)
                {
                    foreach (Thing thing in values)
                    {
                        if (thing != null && yieldedThings.Add(thing))
                        {
                            yield return thing;
                        }
                    }
                }

                if (map == null)
                {
                    yield break;
                }

                foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (thing != null && thing.def.category == ThingCategory.Item && yieldedThings.Add(thing))
                    {
                        yield return thing;
                    }
                }
            }
        }

        [HarmonyPatch]
        public static class PaySilver
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsUtilityCompatAvailable();
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(utilityType, "PaySilver");
            }

            public static bool Prefix(Map map, int amount, bool launchSpace)
            {
                if (map == null || amount <= 0)
                {
                    return false;
                }

                int remaining = amount;
                remaining -= ConsumeSpawnedSilver(map, remaining);
                remaining -= ConsumeNetworkSilver(map, remaining);

                if (remaining > 0)
                {
                    object state = Current.Game.GetComponent(AccessTools.TypeByName("StellarLogistics.MarketState"));
                    FieldInfo bankAccountBalanceField = AccessTools.Field(state?.GetType(), "BankAccountBalance");
                    if (state != null && bankAccountBalanceField != null)
                    {
                        int bankBalance = (int)bankAccountBalanceField.GetValue(state);
                        bankAccountBalanceField.SetValue(state, bankBalance - remaining);
                    }
                }

                return false;
            }
        }

        [HarmonyPatch]
        public static class PopulateSellableLots
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsWindowCompatAvailable();
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(windowType, "PopulateSellableLots");
            }

            public static void Postfix(object __instance, Map map)
            {
                HashSet<Thing> existingThings = GetExistingSellThings(__instance);
                bool addedNetworkThing = false;

                foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (!IsValidNetworkSellable(thing) || !existingThings.Add(thing))
                    {
                        continue;
                    }

                    addToLotsMethod.Invoke(__instance, new object[] { thing });
                    addedNetworkThing = true;
                }

                if (addedNetworkThing)
                {
                    filterSellLotsMethod.Invoke(__instance, null);
                }
            }
        }

        private static HashSet<Thing> GetExistingSellThings(object windowInstance)
        {
            HashSet<Thing> existingThings = new HashSet<Thing>();
            List<TransferableOneWay> cachedSellLots = cachedSellLotsField.GetValue(windowInstance) as List<TransferableOneWay>;
            if (cachedSellLots == null)
            {
                return existingThings;
            }

            foreach (TransferableOneWay lot in cachedSellLots)
            {
                foreach (Thing thing in lot.things)
                {
                    existingThings.Add(thing);
                }
            }

            return existingThings;
        }

        private static bool IsValidNetworkSellable(Thing thing)
        {
            return thing.def.category == ThingCategory.Item
                && thing.def != ThingDefOf.Silver
                && !(bool)isSellBlacklistedMethod.Invoke(null, new object[] { thing.def });
        }

        private static int ConsumeSpawnedSilver(Map map, int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int consumed = 0;
            HashSet<Thing> visited = new HashSet<Thing>();

            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(map))
            {
                foreach (IntVec3 cell in beacon.TradeableCells)
                {
                    foreach (Thing thing in map.thingGrid.ThingsAt(cell))
                    {
                        if (thing.def != ThingDefOf.Silver || !visited.Add(thing))
                        {
                            continue;
                        }

                        int take = System.Math.Min(amount - consumed, thing.stackCount);
                        if (take <= 0)
                        {
                            return consumed;
                        }

                        thing.SplitOff(take).Destroy();
                        consumed += take;

                        if (consumed >= amount)
                        {
                            return consumed;
                        }
                    }
                }
            }

            return consumed;
        }

        private static int ConsumeNetworkSilver(Map map, int amount)
        {
            int consumed = 0;
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();

            foreach (DataNetwork network in mapComp.Networks)
            {
                List<Thing> items = new List<Thing>(network.StoredItems);
                foreach (Thing thing in items)
                {
                    if (thing.def != ThingDefOf.Silver)
                    {
                        continue;
                    }

                    int take = System.Math.Min(amount - consumed, thing.stackCount);
                    if (take <= 0)
                    {
                        return consumed;
                    }

                    thing.SplitOff(take).Destroy();
                    network.MarkBytesDirty();
                    consumed += take;

                    if (consumed >= amount)
                    {
                        return consumed;
                    }
                }
            }

            return consumed;
        }
    }
}
