using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class InfiniteReinforceCompat
    {
        private const string PackageId = "O.inf.reinforce";
        private const string ReinforcerTypeName = "InfiniteReinforce.Building_Reinforcer";
        private const string ReinforceInstanceTypeName = "InfiniteReinforce.Building_Reinforcer+ReinforceInstance";
        private const string ReinforceUtilityTypeName = "InfiniteReinforce.ReinforceUtility";

        private const int CostMode_SameThing = 0;
        private const int CostMode_Fuel = 2;

        private static readonly Type reinforcerType = AccessTools.TypeByName(ReinforcerTypeName);
        private static readonly Type reinforceInstanceType = AccessTools.TypeByName(ReinforceInstanceTypeName);
        private static readonly Type reinforceUtilityType = AccessTools.TypeByName(ReinforceUtilityTypeName);

        private static readonly FieldInfo instanceParentField = reinforceInstanceType != null
            ? AccessTools.Field(reinforceInstanceType, "parent") : null;
        private static readonly MethodInfo costOfMethod = reinforceInstanceType != null
            ? AccessTools.Method(reinforceInstanceType, "CostOf") : null;

        private static readonly MethodInfo targetThingGetter = reinforcerType != null
            ? AccessTools.PropertyGetter(reinforcerType, "TargetThing") : null;
        private static readonly MethodInfo insertMaterialsMethod = reinforcerType != null
            ? AccessTools.Method(reinforcerType, "InsertMaterials") : null;

        private static readonly MethodInfo cannotUseAsMaterialMethod = reinforceUtilityType != null
            ? AccessTools.Method(reinforceUtilityType, "CannotUseAsMaterial") : null;

        private static bool IsCoreCompatAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && reinforcerType != null
                && reinforceInstanceType != null
                && reinforceUtilityType != null
                && instanceParentField != null
                && costOfMethod != null
                && targetThingGetter != null
                && insertMaterialsMethod != null
                && cannotUseAsMaterialMethod != null;
        }

        private static bool CannotUseAsMaterial(Thing thing)
        {
            return (bool)cannotUseAsMaterialMethod.Invoke(null, new object[] { thing });
        }

        private static bool MatchesCostEntry(Thing candidate, ThingDef costDef, ThingDef stuffFilter)
        {
            if (stuffFilter != null && candidate.Stuff != null)
                return candidate.Stuff == stuffFilter;
            return candidate.def == costDef;
        }

        // Replaces GetThingsNearBeacon so the reinforcer dialog counts network items alongside
        // beacon items. A postfix cannot be used here because ref-out propagation back to the
        // caller does not work in this Harmony version; a prefix with return false is required.
        // Other mods' postfixes on this method still run on top of our result.
        [HarmonyPatch]
        public static class GetThingsNearBeacon_Prefix
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;

                if (reinforceUtilityType == null)
                {
                    Logger.Warning("InfiniteReinforce compat: ReinforceUtility type not found; GetThingsNearBeacon patch skipped.");
                    return false;
                }

                if (TargetMethod() == null)
                {
                    Logger.Warning("InfiniteReinforce compat: GetThingsNearBeacon method not found; patch skipped.");
                    return false;
                }

                return true;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    reinforceUtilityType,
                    "GetThingsNearBeacon",
                    new[] { typeof(Map), typeof(List<Thing>).MakeByRefType() });
            }

            public static bool Prefix(Map map, out List<Thing> things, ref bool __result)
            {
                things = new List<Thing>();

                if (map == null)
                {
                    __result = false;
                    return false;
                }

                foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(map))
                {
                    foreach (IntVec3 cell in beacon.TradeableCells)
                        things.AddRange(cell.GetThingList(map));
                }

                HashSet<Thing> seen = new HashSet<Thing>(things);
                foreach (Thing candidate in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (candidate == null || candidate.Destroyed || candidate.stackCount <= 0) continue;
                    if (seen.Add(candidate))
                        things.Add(candidate);
                }

                __result = things.Count > 0;
                return false;
            }
        }

        // Replaces CheckAndInsertMaterials for non-Fuel modes so it validates and consumes
        // from network items in addition to trade beacon items.
        [HarmonyPatch]
        public static class CheckAndInsertMaterials_Prefix
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                bool available = IsCoreCompatAvailable();
                if (ModsConfig.IsActive(PackageId) && !available)
                    Logger.Warning("InfiniteReinforce compat: could not reflect required members; CheckAndInsertMaterials patch skipped.");
                return available;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(reinforceInstanceType, "CheckAndInsertMaterials");
            }

            public static bool Prefix(object __instance, List<ThingDefCountClass> costlist, object costMode, ref bool __result)
            {
                int costModeInt = Convert.ToInt32(costMode);
                if (costModeInt == CostMode_Fuel) return true;

                if (costlist == null || costlist.Count == 0)
                {
                    __result = false;
                    return false;
                }

                object parent = instanceParentField.GetValue(__instance);
                if (parent == null) { __result = false; return false; }

                Map map = (parent as Thing)?.Map;
                if (map == null) { __result = false; return false; }

                ThingWithComps targetThing = targetThingGetter.Invoke(parent, null) as ThingWithComps;
                ThingDef stuffFilter = costModeInt == CostMode_SameThing ? targetThing?.Stuff : null;

                List<Thing> beaconItems = BuildBeaconItems(map);
                HashSet<Thing> beaconSet = new HashSet<Thing>(beaconItems);

                for (int i = 0; i < costlist.Count; i++)
                {
                    int required = (int)costOfMethod.Invoke(__instance, new object[] { costlist, i, costMode });
                    int available = CountCandidates(beaconItems, map, costlist[i].thingDef, stuffFilter, beaconSet);
                    if (available < required)
                    {
                        __result = false;
                        return false;
                    }
                }

                List<List<Thing>> thingsToInsert = new List<List<Thing>>();
                for (int i = 0; i < costlist.Count; i++)
                {
                    int required = (int)costOfMethod.Invoke(__instance, new object[] { costlist, i, costMode });
                    List<Thing> taken = TakeItems(beaconItems, map, costlist[i].thingDef, required, stuffFilter, beaconSet);
                    if (taken == null)
                    {
                        __result = false;
                        return false;
                    }
                    thingsToInsert.Add(taken);
                }

                for (int i = 0; i < thingsToInsert.Count; i++)
                    insertMaterialsMethod.Invoke(parent, new object[] { thingsToInsert[i] });

                __result = true;
                return false;
            }
        }

        private static List<Thing> BuildBeaconItems(Map map)
        {
            List<Thing> items = new List<Thing>();
            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(map))
            {
                foreach (IntVec3 cell in beacon.TradeableCells)
                    items.AddRange(cell.GetThingList(map));
            }
            return items;
        }

        private static int CountCandidates(List<Thing> beaconItems, Map map, ThingDef def, ThingDef stuffFilter, HashSet<Thing> beaconSet)
        {
            int count = 0;

            foreach (Thing thing in beaconItems)
            {
                if (thing == null || thing.Destroyed || thing.stackCount <= 0 || !thing.Spawned) continue;
                if (CannotUseAsMaterial(thing)) continue;
                if (!MatchesCostEntry(thing, def, stuffFilter)) continue;
                count += thing.stackCount;
            }

            foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(map))
            {
                if (thing == null || thing.Destroyed || thing.stackCount <= 0) continue;
                if (beaconSet.Contains(thing)) continue;
                if (CannotUseAsMaterial(thing)) continue;
                if (!MatchesCostEntry(thing, def, stuffFilter)) continue;
                count += thing.stackCount;
            }

            return count;
        }

        private static List<Thing> TakeItems(List<Thing> beaconItems, Map map, ThingDef def, int count, ThingDef stuffFilter, HashSet<Thing> beaconSet)
        {
            List<Thing> taken = new List<Thing>();
            int remaining = count;

            foreach (Thing thing in beaconItems)
            {
                if (remaining <= 0) break;
                if (thing == null || thing.Destroyed || thing.stackCount <= 0 || !thing.Spawned) continue;
                if (!MatchesCostEntry(thing, def, stuffFilter)) continue;
                if (CannotUseAsMaterial(thing)) continue;

                int take = Math.Min(remaining, thing.stackCount);
                taken.Add(thing.SplitOff(take));
                remaining -= take;
            }

            if (remaining > 0)
            {
                foreach (DataNetwork network in NetworkItemSearchUtility.Networks(map))
                {
                    if (remaining <= 0) break;
                    foreach (Thing thing in new List<Thing>(network.StoredItems))
                    {
                        if (remaining <= 0) break;
                        if (thing == null || thing.Destroyed || thing.stackCount <= 0) continue;
                        if (beaconSet.Contains(thing)) continue;
                        if (!MatchesCostEntry(thing, def, stuffFilter)) continue;
                        if (CannotUseAsMaterial(thing)) continue;

                        int take = Math.Min(remaining, thing.stackCount);
                        if (!TryTakeFromNetwork(network, thing, take, out Thing takenItem)) continue;
                        taken.Add(takenItem);
                        remaining -= take;
                    }
                }
            }

            if (remaining > 0) return null;
            return taken;
        }

        private static bool TryTakeFromNetwork(DataNetwork network, Thing item, int count, out Thing taken)
        {
            taken = null;
            if (network?.ActiveController?.innerContainer == null) return false;
            if (item == null || item.Destroyed || item.stackCount < count) return false;

            if (count >= item.stackCount)
            {
                if (!network.ActiveController.innerContainer.Remove(item)) return false;
                taken = item;
            }
            else
            {
                taken = item.SplitOff(count);
                if (taken == null || taken.Destroyed || taken.stackCount <= 0) return false;
            }

            network.MarkBytesDirty();
            return true;
        }
    }
}
