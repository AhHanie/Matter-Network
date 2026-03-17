using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_GenClosest
    {
        [HarmonyPatch(typeof(GenClosest), "ClosestThing_Global")]
        public static class ClosestThing_Global
        {
            public static void Postfix(
                IntVec3 center,
                IEnumerable searchSet,
                float maxDistance,
                Predicate<Thing> validator,
                Func<Thing, float> priorityGetter,
                bool lookInHaulSources,
                ref Thing __result)
            {
                if (!lookInHaulSources)
                {
                    return;
                }

                Map map = TryGetMapFromSearchSet(searchSet);
                if (map == null)
                {
                    return;
                }

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp.Networks.Count == 0)
                {
                    return;
                }

                float maxDistanceSquared = maxDistance * maxDistance;
                float currentBestDistSquared = (__result != null) ? (center - __result.Position).LengthHorizontalSquared : float.MaxValue;
                float currentBestPrio = (priorityGetter != null && __result != null) ? priorityGetter(__result) : float.MinValue;
                Thing bestNetworkThing = null;
                float bestNetworkDistSquared = float.MaxValue;
                float bestNetworkPrio = float.MinValue;

                foreach (DataNetwork network in mapComp.Networks)
                {
                    float distSquared = GetClosestInterfaceDistanceSquared(center, network);
                    foreach (Thing item in network.StoredItems)
                    {
                        if (distSquared > maxDistanceSquared)
                        {
                            continue;
                        }

                        if (validator != null && !validator(item))
                        {
                            continue;
                        }

                        if (priorityGetter != null)
                        {
                            float prio = priorityGetter(item);
                            if (prio > bestNetworkPrio || (Mathf.Approximately(prio, bestNetworkPrio) && distSquared < bestNetworkDistSquared))
                            {
                                bestNetworkThing = item;
                                bestNetworkDistSquared = distSquared;
                                bestNetworkPrio = prio;
                            }
                        }
                        else
                        {
                            if (distSquared < bestNetworkDistSquared)
                            {
                                bestNetworkThing = item;
                                bestNetworkDistSquared = distSquared;
                            }
                        }
                    }
                }

                if (bestNetworkThing != null)
                {
                    if (__result == null)
                    {
                        __result = bestNetworkThing;
                    }
                    else if (priorityGetter != null)
                    {
                        if (bestNetworkPrio > currentBestPrio ||
                            (Mathf.Approximately(bestNetworkPrio, currentBestPrio) && bestNetworkDistSquared < currentBestDistSquared))
                        {
                            __result = bestNetworkThing;
                        }
                    }
                    else
                    {
                        if (bestNetworkDistSquared < currentBestDistSquared)
                        {
                            __result = bestNetworkThing;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(GenClosest), "ClosestThing_Global_Reachable")]
        public static class ClosestThing_Global_Reachable
        {
            public static void Postfix(
                IntVec3 center,
                Map map,
                IEnumerable<Thing> searchSet,
                PathEndMode peMode,
                TraverseParms traverseParams,
                float maxDistance,
                Predicate<Thing> validator,
                Func<Thing, float> priorityGetter,
                bool canLookInHaulableSources,
                ref Thing __result)
            {
                if (searchSet is IList<Pawn> || searchSet is IList<Building>)
                {
                    return;
                }

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp.Networks.Count == 0)
                {
                    return;
                }

                float maxDistanceSquared = maxDistance * maxDistance;
                float currentBestDistSquared = (__result != null) ? GetThingDistanceSquared(center, map, peMode, traverseParams, __result) : float.MaxValue;
                float currentBestPrio = (priorityGetter != null && __result != null) ? priorityGetter(__result) : float.MinValue;
                Thing bestNetworkThing = null;
                float bestNetworkDistSquared = float.MaxValue;
                float bestNetworkPrio = float.MinValue;
                Dictionary<DataNetwork, float> reachableInterfaceDistanceByNetwork = new Dictionary<DataNetwork, float>();

                foreach (Thing item in searchSet)
                {
                    if (!mapComp.TryGetItemNetwork(item, out DataNetwork network))
                    {
                        continue;
                    }

                    if (!reachableInterfaceDistanceByNetwork.TryGetValue(network, out float distSquared))
                    {
                        distSquared = GetClosestReachableInterfaceDistanceSquared(center, map, peMode, traverseParams, network);
                        reachableInterfaceDistanceByNetwork.Add(network, distSquared);
                    }

                    if (distSquared > maxDistanceSquared)
                    {
                        continue;
                    }

                    if (validator != null && !validator(item))
                    {
                        continue;
                    }

                    if (priorityGetter != null)
                    {
                        float prio = priorityGetter(item);
                        if (prio > bestNetworkPrio || (Mathf.Approximately(prio, bestNetworkPrio) && distSquared < bestNetworkDistSquared))
                        {
                            bestNetworkThing = item;
                            bestNetworkDistSquared = distSquared;
                            bestNetworkPrio = prio;
                        }
                    }
                    else if (distSquared < bestNetworkDistSquared)
                    {
                        bestNetworkThing = item;
                        bestNetworkDistSquared = distSquared;
                    }
                }

                if (bestNetworkThing != null)
                {
                    if (__result == null)
                    {
                        __result = bestNetworkThing;
                    }
                    else if (priorityGetter != null)
                    {
                        if (bestNetworkPrio > currentBestPrio ||
                            (Mathf.Approximately(bestNetworkPrio, currentBestPrio) && bestNetworkDistSquared < currentBestDistSquared))
                        {
                            __result = bestNetworkThing;
                        }
                    }
                    else
                    {
                        if (bestNetworkDistSquared < currentBestDistSquared)
                        {
                            __result = bestNetworkThing;
                        }
                    }
                }
            }
        }

        //[HarmonyPatch(typeof(GenClosest), nameof(GenClosest.ClosestThingReachable))]
        //public static class ClosestThingReachable
        //{
        //    public static void Postfix(
        //        IntVec3 root,
        //        Map map,
        //        ThingRequest thingReq,
        //        PathEndMode peMode,
        //        TraverseParms traverseParams,
        //        float maxDistance,
        //        Predicate<Thing> validator,
        //        IEnumerable<Thing> customGlobalSearchSet,
        //        int searchRegionsMin,
        //        int searchRegionsMax,
        //        bool forceAllowGlobalSearch,
        //        RegionType traversableRegionTypes,
        //        bool ignoreEntirelyForbiddenRegions,
        //        bool lookInHaulSources,
        //        ref Thing __result)
        //    {
        //        if (!lookInHaulSources)
        //        {
        //            return;
        //        }

        //        NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
        //        if (mapComp.Networks.Count == 0)
        //        {
        //            return;
        //        }

        //        float maxDistanceSquared = maxDistance * maxDistance;
        //        float currentBestDistSquared = GetThingDistanceSquared(root, map, peMode, traverseParams, __result);
        //        Thing bestNetworkThing = null;
        //        float bestNetworkDistSquared = float.MaxValue;

        //        foreach (DataNetwork network in mapComp.Networks)
        //        {
        //            float reachableInterfaceDistSquared = GetClosestReachableInterfaceDistanceSquared(root, map, peMode, traverseParams, network);
        //            if (reachableInterfaceDistSquared > maxDistanceSquared)
        //            {
        //                continue;
        //            }

        //            foreach (Thing item in network.StoredItems)
        //            {
        //                if (!thingReq.IsUndefined && !thingReq.Accepts(item))
        //                {
        //                    continue;
        //                }

        //                if (!map.reachability.CanReach(root, item, peMode, traverseParams))
        //                {
        //                    continue;
        //                }

        //                if (validator != null && !validator(item))
        //                {
        //                    continue;
        //                }

        //                if (reachableInterfaceDistSquared < bestNetworkDistSquared)
        //                {
        //                    bestNetworkThing = item;
        //                    bestNetworkDistSquared = reachableInterfaceDistSquared;
        //                }
        //            }
        //        }

        //        if (bestNetworkThing != null && (__result == null || bestNetworkDistSquared < currentBestDistSquared))
        //        {
        //            __result = bestNetworkThing;
        //        }
        //    }
        //}

        private static Map TryGetMapFromSearchSet(IEnumerable searchSet)
        {
            if (searchSet == null)
            {
                return null;
            }

            foreach (object obj in searchSet)
            {
                if (obj is Thing thing && thing.Map != null)
                {
                    return thing.Map;
                }
            }

            return null;
        }

        private static float GetClosestInterfaceDistanceSquared(IntVec3 center, DataNetwork network)
        {
            float closestDistSquared = float.MaxValue;

            foreach (NetworkBuildingNetworkInterface interf in network.NetworkInterfaces)
            {
                float distSquared = (center - interf.InteractionCell).LengthHorizontalSquared;
                if (distSquared < closestDistSquared)
                {
                    closestDistSquared = distSquared;
                }
            }

            return closestDistSquared;
        }

        private static float GetClosestReachableInterfaceDistanceSquared(IntVec3 center, Map map, PathEndMode peMode, TraverseParms traverseParams, DataNetwork network)
        {
            float closestDistSquared = float.MaxValue;
            PathEndMode interfacePeMode = GetInterfacePathEndMode(peMode);

            foreach (NetworkBuildingNetworkInterface interf in network.NetworkInterfaces)
            {
                if (interf.Map != map)
                {
                    continue;
                }

                if (!map.reachability.CanReach(center, interf.InteractionCell, interfacePeMode, traverseParams))
                {
                    continue;
                }

                float distSquared = (center - interf.InteractionCell).LengthHorizontalSquared;
                if (distSquared < closestDistSquared)
                {
                    closestDistSquared = distSquared;
                }
            }

            return closestDistSquared;
        }

        private static float GetThingDistanceSquared(IntVec3 center, Map map, PathEndMode peMode, TraverseParms traverseParams, Thing thing)
        {
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (mapComp.TryGetItemNetwork(thing, out DataNetwork network))
            {
                return GetClosestReachableInterfaceDistanceSquared(center, map, peMode, traverseParams, network);
            }

            return (center - thing.PositionHeld).LengthHorizontalSquared;
        }

        private static PathEndMode GetInterfacePathEndMode(PathEndMode peMode)
        {
            return peMode == PathEndMode.InteractionCell ? PathEndMode.OnCell : peMode;
        }
    }
}
