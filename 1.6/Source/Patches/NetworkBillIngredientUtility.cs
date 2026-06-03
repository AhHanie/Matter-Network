using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    internal static class NetworkBillIngredientUtility
    {
        internal const int SharedIngredientReservationMaxPawns = 10;

        public static void AddNetworkIngredientCandidates(
            Pawn pawn,
            Thing billGiver,
            float searchRadius,
            List<Thing> relevantThings,
            HashSet<Thing> processedThings,
            Predicate<Thing> thingValidator)
        {
            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            float radiusSq = searchRadius * searchRadius;

            foreach (DataNetwork network in mapComp.ExtractionEnabledNetworks)
            {
                if (!HasReachableInterfaceInRadius(pawn, billGiver, searchRadius, radiusSq, network))
                    continue;

                foreach (Thing thing in network.StoredItems)
                {
                    if (processedThings.Contains(thing))
                        continue;
                    if (thing.IsForbidden(pawn))
                        continue;
                    if (!thingValidator(thing))
                        continue;
                    if (pawn.Map.reservationManager.CanReserveStack(pawn, thing, SharedIngredientReservationMaxPawns) <= 0)
                        continue;

                    relevantThings.Add(thing);
                    processedThings.Add(thing);
                }
            }
        }

        public static bool HasReachableInterfaceInRadius(Pawn pawn, Thing billGiver, float searchRadius, float radiusSq, DataNetwork network)
        {
            foreach (NetworkBuildingNetworkInterface interf in network.NetworkInterfaces)
            {
                if (!interf.Position.InHorDistOf(billGiver.Position, searchRadius))
                    continue;

                if ((interf.Position - billGiver.Position).LengthHorizontalSquared > radiusSq)
                    continue;

                if (pawn.Map.reachability.CanReach(pawn.Position, interf.InteractionCell, PathEndMode.OnCell, TraverseParms.For(pawn)))
                    return true;
            }

            return false;
        }

        public static bool ContainsNetworkIngredient(Pawn pawn, List<LocalTargetInfo> targetQueue)
        {
            if (pawn?.Map == null)
                return false;

            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            for (int i = 0; i < targetQueue.Count; i++)
            {
                if (targetQueue[i].HasThing && mapComp.TryGetItemNetwork(targetQueue[i].Thing, out _))
                    return true;
            }

            return false;
        }

        public static void ReserveIngredientQueue(Pawn pawn, Job job, List<LocalTargetInfo> targetQueue)
        {
            if (pawn?.Map == null)
                return;

            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            List<int> countQueue = job.countQueue;

            for (int i = 0; i < targetQueue.Count; i++)
            {
                LocalTargetInfo target = targetQueue[i];
                if (!target.HasThing)
                    continue;

                int desiredCount = (countQueue != null && i < countQueue.Count) ? countQueue[i] : -1;

                if (!mapComp.TryGetItemNetwork(target.Thing, out _))
                {
                    pawn.Map.reservationManager.Reserve(pawn, job, target, 1, -1, null, false, false, false);
                    continue;
                }

                int reservableCount = ResolveIngredientReservationCount(pawn, target, desiredCount);
                if (reservableCount <= 0)
                    continue;

                pawn.Map.reservationManager.Reserve(pawn, job, target, SharedIngredientReservationMaxPawns, reservableCount, null, false, false, false);
            }
        }

        private static int ResolveIngredientReservationCount(Pawn pawn, LocalTargetInfo ingredientTarget, int desiredCount)
        {
            int reservableCount = pawn.Map.reservationManager.CanReserveStack(pawn, ingredientTarget, SharedIngredientReservationMaxPawns);
            if (reservableCount <= 0)
                return 0;

            if (desiredCount <= 0)
                return reservableCount;

            return Math.Min(desiredCount, reservableCount);
        }
    }
}
