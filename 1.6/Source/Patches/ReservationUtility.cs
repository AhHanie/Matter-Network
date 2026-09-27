using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_ReservationUtility
    {
        // Only known callers are CompGoldenCube.CanInteract and CompObeliskDeactivationInteractor.CanInteract,
        // both querying ThingDefOf.Shard. Both scan map.listerThings directly, which never contains
        // items held inside a NetworkBuildingController, so a network holding the only available Shards
        // reports the interaction as unavailable even though a pawn can already retrieve them.
        [HarmonyPatch(typeof(ReservationUtility), nameof(ReservationUtility.ExistsUnreservedAmountOfDef))]
        public static class ExistsUnreservedAmountOfDef
        {
            public static void Postfix(Map map, ThingDef thingDef, Faction faction, int amount, Predicate<Thing> validator, ref bool __result)
            {
                if (__result || amount <= 0 || map == null || thingDef != ThingDefOf.Shard)
                {
                    return;
                }

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp.Networks.Count == 0)
                {
                    return;
                }

                // Recompute vanilla's own spawned-stock count: __result is only false because it was
                // below amount, not necessarily zero, so a mixed floor + network supply must add up.
                int num = 0;
                foreach (Thing item in map.listerThings.ThingsOfDef(thingDef))
                {
                    if (!item.IsForbidden(faction) && !map.reservationManager.IsReservedByAnyoneOf(item, faction) && (validator == null || validator(item)))
                    {
                        num += item.stackCount;
                    }
                }

                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (item.def != thingDef || item.Destroyed || item.stackCount <= 0)
                    {
                        continue;
                    }

                    if (item.IsForbidden(faction) || map.reservationManager.IsReservedByAnyoneOf(item, faction))
                    {
                        continue;
                    }

                    if (validator != null && !validator(item))
                    {
                        continue;
                    }

                    num += item.stackCount;
                    if (num >= amount)
                    {
                        __result = true;
                        return;
                    }
                }
            }
        }
    }
}
