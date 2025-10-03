using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;
using System.Collections.Generic;

namespace SK_Matter_Network.Patches
{
    public static class Patch_FoodUtility
    {
        private static bool lastAllowPlantValue;

        [HarmonyPatch(typeof(FoodUtility), "SpawnedFoodSearchInnerScan")]
        public static class SpawnedFoodSearchInnerScan
        {
            public static void Postfix(Pawn eater, IntVec3 root, List<Thing> searchSet, PathEndMode peMode, TraverseParms traverseParams, ref Thing __result, float maxDistance, Predicate<Thing> validator)
            {
                Pawn pawn = traverseParams.pawn ?? eater;

                NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
                if (mapComp.Networks.Count == 0)
                {
                    return;
                }

                ThingRequest thingRequest = (((eater.RaceProps.foodType & (FoodTypeFlags.Plant | FoodTypeFlags.Tree)) == 0 || !lastAllowPlantValue) ? ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree) : ThingRequest.ForGroup(ThingRequestGroup.FoodSource));
                Thing bestNetworkItem = null;
                float bestNetworkOptimality = float.MinValue;

                foreach (DataNetwork network in mapComp.ExtractionEnabledNetworks)
                {
                    float distance = GetClosestReachableInterfaceDistance(root, pawn.Map, peMode, traverseParams, network);
                    if (distance == float.MaxValue || distance > maxDistance)
                    {
                        continue;
                    }

                    foreach (Thing storedItem in network.storedItems)
                    {
                        if (!thingRequest.group.Includes(storedItem.def))
                        {
                            continue;
                        }

                        if (validator != null && !validator(storedItem))
                        {
                            continue;
                        }

                        float optimality = FoodUtility.FoodOptimality(eater, storedItem, FoodUtility.GetFinalIngestibleDef(storedItem), distance);
                        if (optimality >= bestNetworkOptimality)
                        {
                            bestNetworkItem = storedItem;
                            bestNetworkOptimality = optimality;
                        }
                    }
                }

                if (bestNetworkItem == null)
                {
                    return;
                }

                if (__result == null)
                {
                    __result = bestNetworkItem;
                    return;
                }

                float currentResultOptimality = GetThingOptimality(eater, root, pawn.Map, peMode, traverseParams, __result);
                if (bestNetworkOptimality >= currentResultOptimality)
                {
                    __result = bestNetworkItem;
                }
            }
        }

        [HarmonyPatch(typeof(FoodUtility), "BestFoodSourceOnMap")]
        public static class BestFoodSourceOnMap
        {
            public static void Prefix(bool allowPlant)
            {
                lastAllowPlantValue = allowPlant;
            }
        }

        private static float GetThingOptimality(Pawn eater, IntVec3 root, Map map, PathEndMode peMode, TraverseParms traverseParams, Thing thing)
        {
            return FoodUtility.FoodOptimality(eater, thing, FoodUtility.GetFinalIngestibleDef(thing), GetThingDistance(root, map, peMode, traverseParams, thing));
        }

        private static float GetThingDistance(IntVec3 root, Map map, PathEndMode peMode, TraverseParms traverseParams, Thing thing)
        {
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (mapComp.TryGetItemNetwork(thing, out DataNetwork network))
            {
                return GetClosestReachableInterfaceDistance(root, map, peMode, traverseParams, network);
            }

            return (root - thing.PositionHeld).LengthManhattan;
        }

        private static float GetClosestReachableInterfaceDistance(IntVec3 root, Map map, PathEndMode peMode, TraverseParms traverseParams, DataNetwork network)
        {
            float closestDistance = float.MaxValue;
            PathEndMode interfacePeMode = peMode == PathEndMode.InteractionCell ? PathEndMode.OnCell : peMode;

            foreach (NetworkBuildingNetworkInterface networkInterface in network.NetworkInterfaces)
            {
                if (networkInterface.Map != map)
                {
                    continue;
                }

                if (!map.reachability.CanReach(root, networkInterface.InteractionCell, interfacePeMode, traverseParams))
                {
                    continue;
                }

                float distance = (root - networkInterface.InteractionCell).LengthManhattan;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                }
            }

            return closestDistance;
        }
    }
}
