using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class CombatExtendedCompat
    {
        private const string PackageId = "CETeam.CombatExtended";

        private static readonly Type _jobGiverType =
            AccessTools.TypeByName("CombatExtended.JobGiver_UpdateLoadout");
        private static readonly Type _loadoutSlotType =
            AccessTools.TypeByName("CombatExtended.LoadoutSlot");
        private static readonly PropertyInfo _loadoutSlotGenericDefProp =
            AccessTools.Property(AccessTools.TypeByName("CombatExtended.LoadoutSlot"), "genericDef");

        private static bool _warnedMissingApi;

        // Set in FindPickup prefix when the slot is generic; cleared in postfix.
        // Signals ClosestThingReachable to include network items for group requests.
        [ThreadStatic]
        internal static bool CEGroupSearchActive;

        private static bool IsAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && _jobGiverType != null
                && _loadoutSlotType != null
                && _loadoutSlotGenericDefProp != null;
        }

        // Called from Patch_GenClosest.ClosestThingReachable before its early-return for group requests.
        internal static void TryAddNetworkItemsForGroupSearch(
            IntVec3 root,
            Map map,
            PathEndMode peMode,
            TraverseParms traverseParams,
            ThingRequest thingReq,
            float maxDistance,
            Predicate<Thing> validator,
            ref Thing result)
        {
            if (!CEGroupSearchActive)
                return;
            if (thingReq.singleDef != null || thingReq.group == ThingRequestGroup.Undefined)
                return;

            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (mapComp.Networks.Count == 0)
                return;

            float maxDistSq = maxDistance * maxDistance;
            float currentBestDistSq = (result != null)
                ? Patch_GenClosest.GetThingDistanceSquared(root, map, peMode, traverseParams, result)
                : float.MaxValue;
            Thing bestThing = null;
            float bestDistSq = float.MaxValue;

            foreach (DataNetwork network in mapComp.ExtractionEnabledNetworks)
            {
                float ifaceDistSq = Patch_GenClosest.GetClosestReachableInterfaceDistanceSquared(
                    root, map, peMode, traverseParams, network);
                if (ifaceDistSq >= maxDistSq)
                    continue;

                foreach (Thing item in network.StoredItems)
                {
                    if (!thingReq.Accepts(item))
                        continue;
                    if (validator != null && !validator(item))
                        continue;
                    if (ifaceDistSq < bestDistSq)
                    {
                        bestThing = item;
                        bestDistSq = ifaceDistSq;
                    }
                }
            }

            if (bestThing != null && (result == null || bestDistSq < currentBestDistSq))
                result = bestThing;
        }

        // Prefix: flag generic-slot FindPickup calls so ClosestThingReachable includes network items.
        [HarmonyPatch]
        public static class Patch_FindPickup
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                bool available = IsAvailable();
                if (ModsConfig.IsActive(PackageId) && !available && !_warnedMissingApi)
                {
                    _warnedMissingApi = true;
                    Logger.Warning("[CE Compat] Combat Extended is loaded but expected API was not found. CE loadout compat disabled.");
                }
                return available;
            }

            public static MethodBase TargetMethod()
            {
                if (!IsAvailable()) return null;
                return AccessTools.Method(_jobGiverType, "FindPickup");
            }

            // curSlot declared as object because LoadoutSlot is a CE type; Harmony matches by name.
            public static void Prefix(object curSlot)
            {
                if (curSlot == null) return;
                object genericDef = _loadoutSlotGenericDefProp.GetValue(curSlot);
                if (genericDef != null)
                    CEGroupSearchActive = true;
            }

            public static void Postfix()
            {
                CEGroupSearchActive = false;
            }
        }

        // Postfix: CE's GetUnreservedStackCount uses thing.Map which is null for network-held items.
        // Fall back to thing.MapHeld so reservation checks work on network items.
        [HarmonyPatch]
        public static class Patch_GetUnreservedStackCount
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsAvailable()
                    && AccessTools.Method(_jobGiverType, "GetUnreservedStackCount") != null;
            }

            public static MethodBase TargetMethod()
            {
                if (!IsAvailable()) return null;
                return AccessTools.Method(_jobGiverType, "GetUnreservedStackCount");
            }

            public static void Postfix(Thing thing, ref int __result)
            {
                // CE's method already handled things that are directly on a map.
                if (__result > 0 || thing.Map != null)
                    return;

                Map map = thing.MapHeld;
                if (map == null)
                    return;

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(thing, out DataNetwork network))
                    return;

                if (!network.IsOperational || !network.CanExtractItems)
                    return;

                __result = thing.stackCount;
            }
        }
    }
}
