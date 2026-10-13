using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_WealthWatcher
    {
        [HarmonyPatch(typeof(WealthWatcher), "CalculateWealthItems")]
        public static class CalculateWealthItems
        {
            public static bool Prefix(WealthWatcher __instance, ref float __result)
            {
                Map map = __instance.map;

                List<Thing> things = new List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(
                    map,
                    ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                    things,
                    allowUnreal: false,
                    WealthWatcher.WealthItemsFilter);

                HashSet<Thing> seenThings = new HashSet<Thing>();
                HashSet<Thing> networkItems = ModSettings.DisableNetworkItemsForWealth
                    ? GetNetworkItems(map)
                    : null;

                float wealth = 0f;
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (!seenThings.Add(thing))
                    {
                        continue;
                    }

                    if (networkItems != null && networkItems.Contains(thing))
                    {
                        continue;
                    }

                    if (thing.SpawnedOrAnyParentSpawned && !thing.PositionHeld.Fogged(map))
                    {
                        wealth += thing.MarketValue * thing.stackCount;
                    }
                }

                __result = wealth;
                return false;
            }

            private static HashSet<Thing> GetNetworkItems(Map map)
            {
                HashSet<Thing> networkItems = new HashSet<Thing>();
                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();

                foreach (DataNetwork network in mapComp.Networks)
                {
                    foreach (Thing thing in network.StoredItems)
                    {
                        if (!thing.Destroyed)
                        {
                            networkItems.Add(thing);
                        }
                    }
                }

                return networkItems;
            }
        }
    }
}
