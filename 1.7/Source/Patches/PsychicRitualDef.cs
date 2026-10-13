using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_PsychicRitualDef
    {
        // Vanilla only scans map.listerThings.ThingsMatchingFilter, which never contains items held
        // inside a NetworkBuildingController, so the ritual setup dialog undercounts reachable
        // offerings (Shards, Bioferrite) that are sitting in a powered, interfaced network.
        [HarmonyPatch(typeof(PsychicRitualDef), nameof(PsychicRitualDef.OfferingReachable))]
        public static class OfferingReachable
        {
            public static void Postfix(Map map, List<Pawn> pawns, IngredientCount offering, ref int reachableCount, ref bool __result)
            {
                if (__result || map == null || pawns.NullOrEmpty())
                {
                    return;
                }

                float needed = offering.GetBaseCount() - reachableCount;
                if (needed <= 0f)
                {
                    __result = true;
                    return;
                }

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp.Networks.Count == 0)
                {
                    return;
                }

                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (needed <= 0f)
                    {
                        break;
                    }

                    if (!offering.filter.Allows(item) || item.Destroyed || item.stackCount <= 0 || item.Fogged())
                    {
                        continue;
                    }

                    for (int i = 0; i < pawns.Count; i++)
                    {
                        Pawn pawn = pawns[i];
                        if (!item.IsForbidden(pawn) && pawn.CanReserveAndReach(item, PathEndMode.Touch, pawn.NormalMaxDanger()))
                        {
                            needed -= item.stackCount;
                            reachableCount += item.stackCount;
                            break;
                        }
                    }
                }

                __result = needed <= 0f;
            }
        }
    }
}
