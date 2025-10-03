using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_RefuelWorkGiverUtility
    {
        [HarmonyPatch(typeof(RefuelWorkGiverUtility), "FindBestFuel")]
        public static class FindBestFuel
        {
            public static void Postfix(Pawn pawn, Thing refuelable, ref Thing __result)
            {
                CompRefuelable compRefuelable = refuelable.TryGetComp<CompRefuelable>();
                Thing bestFuel = FindClosestNetworkFuel(
                    pawn,
                    refuelable,
                    compRefuelable.Props.fuelFilter,
                    pawn.Position);
                NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();

                if (bestFuel == null)
                {
                    return;
                }

                if (__result == null)
                {
                    __result = bestFuel;
                    return;
                }

                float currentResultDistanceSquared = GetThingDistanceSquared(pawn, __result, mapComp);
                float bestNetworkDistanceSquared = GetClosestReachableInterfaceDistanceSquared(pawn, pawn.Position, bestFuel, mapComp);
                if (bestNetworkDistanceSquared < currentResultDistanceSquared)
                {
                    __result = bestFuel;
                }
            }
        }

        [HarmonyPatch(typeof(RefuelWorkGiverUtility), "FindAllFuel")]
        public static class FindAllFuel
        {
            public static void Postfix(Pawn pawn, Thing refuelable, ref List<Thing> __result)
            {
                CompRefuelable compRefuelable = refuelable.TryGetComp<CompRefuelable>();
                int fuelCountToFullyRefuel = compRefuelable.GetFuelCountToFullyRefuel();
                if (fuelCountToFullyRefuel <= 0)
                {
                    return;
                }

                List<Thing> combinedFuel = (__result ?? new List<Thing>()).Distinct().ToList();
                NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();

                foreach (Thing fuel in FindNetworkFuelCandidates(pawn, refuelable, compRefuelable.Props.fuelFilter, pawn.Position, mapComp))
                {
                    if (combinedFuel.Contains(fuel))
                    {
                        continue;
                    }

                    combinedFuel.Add(fuel);
                }

                combinedFuel.Sort((a, b) => GetThingDistanceSquared(pawn, a, mapComp).CompareTo(GetThingDistanceSquared(pawn, b, mapComp)));

                List<Thing> chosenFuel = new List<Thing>();
                int accumulatedQuantity = 0;
                for (int i = 0; i < combinedFuel.Count; i++)
                {
                    Thing fuel = combinedFuel[i];
                    chosenFuel.Add(fuel);
                    accumulatedQuantity += fuel.stackCount;
                    if (accumulatedQuantity >= fuelCountToFullyRefuel)
                    {
                        __result = chosenFuel;
                        return;
                    }
                }

                if (accumulatedQuantity < fuelCountToFullyRefuel)
                {
                    __result = null;
                }
            }
        }

        private static Thing FindClosestNetworkFuel(Pawn pawn, Thing refuelable, ThingFilter filter, IntVec3 root)
        {
            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            if (mapComp.Networks.Count == 0)
            {
                return null;
            }

            Thing bestFuel = null;
            float bestDistanceSquared = float.MaxValue;

            foreach (Thing fuel in FindNetworkFuelCandidates(pawn, refuelable, filter, root, mapComp))
            {
                float distanceSquared = GetClosestReachableInterfaceDistanceSquared(pawn, root, fuel, mapComp);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestFuel = fuel;
                    bestDistanceSquared = distanceSquared;
                }
            }

            return bestFuel;
        }

        private static IEnumerable<Thing> FindNetworkFuelCandidates(Pawn pawn, Thing refuelable, ThingFilter filter, IntVec3 root, NetworksMapComponent mapComp)
        {
            foreach (DataNetwork network in mapComp.ExtractionEnabledNetworks)
            {
                float distanceSquared = GetClosestReachableInterfaceDistanceSquared(pawn, root, network);
                if (distanceSquared == float.MaxValue)
                {
                    continue;
                }

                foreach (Thing item in network.StoredItems)
                {
                    if (!IsValidFuelCandidate(pawn, filter, item))
                    {
                        continue;
                    }

                    yield return item;
                }
            }
        }

        private static bool IsValidFuelCandidate(Pawn pawn, ThingFilter filter, Thing item)
        {
            return filter.Allows(item);
        }

        private static float GetClosestReachableInterfaceDistanceSquared(Pawn pawn, IntVec3 root, Thing item, NetworksMapComponent mapComp)
        {
            if (!mapComp.TryGetItemNetwork(item, out DataNetwork network))
            {
                return float.MaxValue;
            }

            return GetClosestReachableInterfaceDistanceSquared(pawn, root, network);
        }

        private static float GetClosestReachableInterfaceDistanceSquared(Pawn pawn, IntVec3 root, DataNetwork network)
        {
            float closestDistanceSquared = float.MaxValue;
            TraverseParms traverseParams = TraverseParms.For(pawn);

            foreach (NetworkBuildingNetworkInterface networkInterface in network.NetworkInterfaces)
            {
                if (!pawn.Map.reachability.CanReach(root, networkInterface.InteractionCell, PathEndMode.OnCell, traverseParams))
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

        private static float GetThingDistanceSquared(Pawn pawn, Thing thing, NetworksMapComponent mapComp)
        {
            float networkDistanceSquared = GetClosestReachableInterfaceDistanceSquared(pawn, pawn.Position, thing, mapComp);
            if (networkDistanceSquared != float.MaxValue)
            {
                return networkDistanceSquared;
            }

            return (pawn.Position - thing.PositionHeld).LengthHorizontalSquared;
        }
    }
}
