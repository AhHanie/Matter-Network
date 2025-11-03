using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
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
                        if (item == null || item.Destroyed)
                        {
                            continue;
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
                if (!canLookInHaulableSources)
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
                    NetworkBuildingNetworkInterface reachableInterface = null;
                    float closestInterfaceDistSquared = float.MaxValue;

                    foreach (NetworkBuildingNetworkInterface interf in network.NetworkInterfaces)
                    {
                        if (map.reachability.CanReach(center, interf.InteractionCell, peMode, traverseParams))
                        {
                            float interfaceDistSquared = (center - interf.InteractionCell).LengthHorizontalSquared;
                            if (interfaceDistSquared < closestInterfaceDistSquared)
                            {
                                reachableInterface = interf;
                                closestInterfaceDistSquared = interfaceDistSquared;
                            }
                        }
                    }

                    if (reachableInterface == null)
                    {
                        continue;
                    }

                    foreach (Thing item in network.StoredItems)
                    {
                        if (item == null || item.Destroyed)
                        {
                            continue;
                        }

                        float distSquared = closestInterfaceDistSquared;

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
    }
}