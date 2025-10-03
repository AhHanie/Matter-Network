using HarmonyLib;
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Pawn_TradeTracker
    {
        [HarmonyPatch(typeof(Pawn_TraderTracker), "ReachableForTrade")]
        public static class ReachableForTrade_Patch
        {
            public static bool Prefix(Thing thing, ref bool __result)
            {
                NetworksMapComponent mapComp = thing.MapHeld.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(thing, out DataNetwork _)) return true;

                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_TraderTracker), nameof(Pawn_TraderTracker.ColonyThingsWillingToBuy))]
        public static class ColonyThingsWillingToBuy
        {
            public static bool Prefix(Pawn_TraderTracker __instance, Pawn playerNegotiator, ref IEnumerable<Thing> __result)
            {
                __result = BuildColonyThingsWillingToBuyEnumerable(__instance);
                return false;
            }
        }

        public static IEnumerable<Thing> BuildColonyThingsWillingToBuyEnumerable(Pawn_TraderTracker tracker)
        {
            Pawn pawn = tracker.pawn;
            HashSet<Thing> yieldedThings = new HashSet<Thing>();

            foreach (Thing thing in pawn.Map.listerThings.AllThings)
            {
                if (OutsideNetworkTradeValidator(tracker, thing) && yieldedThings.Add(thing))
                    yield return thing;
            }

            foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(pawn.Map))
            {
                if (InNetworkTradeValidator(tracker, thing) && yieldedThings.Add(thing))
                {
                    yield return thing;
                }
            }

            if (ModsConfig.BiotechActive)
            {
                List<Building> geneBanks = pawn.Map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.GeneBank);
                foreach (Building building in geneBanks)
                {
                    if (!tracker.ReachableForTrade(building))
                    {
                        continue;
                    }

                    CompGenepackContainer genepackContainer = building.TryGetComp<CompGenepackContainer>();
                    if (genepackContainer == null)
                    {
                        continue;
                    }

                    foreach (Genepack genepack in genepackContainer.ContainedGenepacks)
                    {
                        if (yieldedThings.Add(genepack))
                        {
                            yield return genepack;
                        }
                    }
                }
            }

            foreach (IHaulSource haulSource in pawn.Map.listerBuildings.AllColonistBuildingsOfType<IHaulSource>())
            {
                Building building = (Building)haulSource;
                if (!tracker.ReachableForTrade(building))
                {
                    continue;
                }

                foreach (Thing thing in haulSource.GetDirectlyHeldThings())
                {
                    if (yieldedThings.Add(thing))
                    {
                        yield return thing;
                    }
                }
            }

            if (pawn.GetLord() != null)
            {
                foreach (Pawn sellablePawn in TradeUtility.AllSellableColonyPawns(pawn.Map))
                {
                    if (!sellablePawn.Downed && tracker.ReachableForTrade(sellablePawn) && yieldedThings.Add(sellablePawn))
                    {
                        yield return sellablePawn;
                    }
                }
            }
        }

        private static bool OutsideNetworkTradeValidator(Pawn_TraderTracker tracker, Thing thing)
        {
            Pawn pawn = tracker.pawn;
            if (thing.def.category != ThingCategory.Item) return false;
            if (!TradeUtility.PlayerSellableNow(thing, pawn)) return false;
            if (thing.Position.Fogged(thing.Map)) return false;
            if (!(pawn.Map.areaManager.Home[thing.Position] || thing.IsInAnyStorage())) return false;

            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            if (mapComp.TryGetItemNetwork(thing, out _)) return false; // skip network items

            return tracker.ReachableForTrade(thing);
        }

        private static bool InNetworkTradeValidator(Pawn_TraderTracker tracker, Thing thing)
        {
            Pawn pawn = tracker.pawn;
            return thing.def.category == ThingCategory.Item &&
                   TradeUtility.PlayerSellableNow(thing, pawn) &&
                   tracker.ReachableForTrade(thing);
        }
    }
}
