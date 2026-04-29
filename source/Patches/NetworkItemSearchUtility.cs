using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    internal static class NetworkItemSearchUtility
    {
        public static IEnumerable<DataNetwork> Networks(Map map)
        {
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            foreach (DataNetwork network in mapComp.ExtractionEnabledNetworks)
            {
                yield return network;
            }
        }

        public static IEnumerable<Thing> AllNetworkItems(Map map)
        {
            foreach (DataNetwork network in Networks(map))
            {
                foreach (Thing item in network.StoredItems)
                {
                    yield return item;
                }
            }
        }

        public static bool AnyMatchingThing(Map map, Predicate<Thing> validator)
        {
            foreach (Thing item in AllNetworkItems(map))
            {
                if (validator(item))
                {
                    return true;
                }
            }

            return false;
        }

        public static int CountMatchingThingStacks(Map map, Predicate<Thing> validator)
        {
            int count = 0;
            foreach (Thing item in AllNetworkItems(map))
            {
                if (validator(item))
                {
                    count += item.stackCount;
                }
            }

            return count;
        }

        public static Thing FindClosestReachableThing(Pawn pawn, Predicate<Thing> validator, out float closestDistanceSquared)
        {
            return FindClosestReachableThing(pawn.Position, pawn, validator, out closestDistanceSquared);
        }

        public static Thing FindClosestReachableThing(IntVec3 root, Pawn pawn, Predicate<Thing> validator, out float closestDistanceSquared)
        {
            Thing closestThing = null;
            closestDistanceSquared = float.MaxValue;

            foreach (DataNetwork network in Networks(pawn.Map))
            {
                float distanceSquared = GetClosestReachableInterfaceDistanceSquared(root, pawn, network);
                if (distanceSquared == float.MaxValue)
                {
                    continue;
                }

                foreach (Thing item in network.StoredItems)
                {
                    if (!validator(item))
                    {
                        continue;
                    }

                    if (distanceSquared < closestDistanceSquared)
                    {
                        closestDistanceSquared = distanceSquared;
                        closestThing = item;
                    }
                }
            }

            return closestThing;
        }

        public static float GetThingDistanceSquared(Pawn pawn, Thing thing)
        {
            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            if (mapComp.TryGetItemNetwork(thing, out DataNetwork network))
            {
                if (!network.IsOperational)
                {
                    return float.MaxValue;
                }

                return GetClosestReachableInterfaceDistanceSquared(pawn.Position, pawn, network);
            }

            return (pawn.Position - thing.PositionHeld).LengthHorizontalSquared;
        }

        public static float GetClosestReachableInterfaceDistanceSquared(Pawn pawn, Thing item)
        {
            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            if (!mapComp.TryGetItemNetwork(item, out DataNetwork network))
            {
                return float.MaxValue;
            }

            if (!network.IsOperational)
            {
                return float.MaxValue;
            }

            return GetClosestReachableInterfaceDistanceSquared(pawn.Position, pawn, network);
        }

        public static float GetClosestReachableInterfaceDistanceSquared(IntVec3 root, Pawn pawn, DataNetwork network)
        {
            if (!network.IsOperational)
            {
                return float.MaxValue;
            }

            float closestDistanceSquared = float.MaxValue;

            foreach (NetworkBuildingNetworkInterface networkInterface in network.NetworkInterfaces)
            {
                if (!pawn.CanReach(networkInterface.InteractionCell, PathEndMode.OnCell, Danger.Deadly))
                {
                    continue;
                }

                float distanceSquared = (root - networkInterface.InteractionCell).LengthHorizontalSquared;
                if (distanceSquared < closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                }
            }

            return closestDistanceSquared;
        }
    }
}
