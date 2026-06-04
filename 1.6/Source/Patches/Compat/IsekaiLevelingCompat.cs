using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class IsekaiLevelingCompat
    {
        private const string PackageId = "JellyCreative.IsekaiLeveling";
        private const string ForgeUtilityTypeName = "IsekaiLeveling.Forge.ForgeUtility";
        private const string WindowForgeTypeName = "IsekaiLeveling.Forge.Window_Forge";
        private const string ForgeEnhancementCompTypeName = "IsekaiLeveling.Forge.CompForgeEnhancement";

        private static readonly Type forgeUtilityType = AccessTools.TypeByName(ForgeUtilityTypeName);
        private static readonly Type windowForgeType = AccessTools.TypeByName(WindowForgeTypeName);
        private static readonly Type forgeEnhancementCompType = AccessTools.TypeByName(ForgeEnhancementCompTypeName);

        private static readonly FieldInfo windowMapField = windowForgeType != null
            ? AccessTools.Field(windowForgeType, "map") : null;
        private static readonly FieldInfo selectedItemField = windowForgeType != null
            ? AccessTools.Field(windowForgeType, "selectedItem") : null;

        private static bool IsIsekaiCompatAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && forgeUtilityType != null
                && windowForgeType != null
                && forgeEnhancementCompType != null
                && windowMapField != null
                && selectedItemField != null;
        }

        private static bool HasForgeEnhancementComp(Thing thing)
        {
            if (forgeEnhancementCompType == null) return false;
            if (!(thing is ThingWithComps twc)) return false;
            foreach (ThingComp comp in twc.AllComps)
            {
                if (forgeEnhancementCompType.IsInstanceOfType(comp))
                    return true;
            }
            return false;
        }

        private static bool IsValidForgeMaterial(Thing thing, ThingDef def)
        {
            return thing != null
                && !thing.Destroyed
                && thing.stackCount > 0
                && thing.def == def
                && !thing.IsForbidden(Faction.OfPlayer);
        }

        private static bool IsValidForgeEquipment(Thing thing)
        {
            return thing != null
                && !thing.Destroyed
                && thing.def != null
                && thing.def.category == ThingCategory.Item
                && (thing.def.IsWeapon || thing.def.IsApparel)
                && !thing.def.IsStuff
                && !thing.IsForbidden(Faction.OfPlayer)
                && HasForgeEnhancementComp(thing);
        }

        private static int CountNetworkItems(Map map, ThingDef def)
        {
            if (map == null || def == null) return 0;
            int count = 0;
            foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(map))
            {
                if (IsValidForgeMaterial(thing, def))
                    count += thing.stackCount;
            }
            return count;
        }

        private struct TakeEntry
        {
            public Thing Thing;
            public int Count;
            public DataNetwork Network; // null = spawned map item
        }

        private static bool BuildTakePlan(Map map, ThingDef def, int count, List<TakeEntry> plan)
        {
            int remaining = count;

            // Spawned map stacks first
            List<Thing> spawned = map.listerThings.ThingsOfDef(def);
            foreach (Thing thing in spawned)
            {
                if (remaining <= 0) break;
                if (thing.IsForbidden(Faction.OfPlayer)) continue;
                if (!thing.IsInValidStorage()) continue;
                if (thing.Destroyed || thing.stackCount <= 0) continue;

                int take = Math.Min(thing.stackCount, remaining);
                plan.Add(new TakeEntry { Thing = thing, Count = take, Network = null });
                remaining -= take;
            }

            // Network items to fill the gap, read directly from innerContainer to avoid stale cache
            if (remaining > 0)
            {
                foreach (DataNetwork network in NetworkItemSearchUtility.Networks(map))
                {
                    if (remaining <= 0) break;
                    if (network.ActiveController?.innerContainer == null) continue;
                    List<Thing> items = new List<Thing>(network.ActiveController.innerContainer.InnerListForReading);
                    foreach (Thing thing in items)
                    {
                        if (remaining <= 0) break;
                        if (!IsValidForgeMaterial(thing, def)) continue;

                        int take = Math.Min(thing.stackCount, remaining);
                        plan.Add(new TakeEntry { Thing = thing, Count = take, Network = network });
                        remaining -= take;
                    }
                }
            }

            return remaining <= 0;
        }

        private static void ApplyTakePlan(List<TakeEntry> plan)
        {
            HashSet<DataNetwork> dirtyNetworks = new HashSet<DataNetwork>();

            foreach (TakeEntry entry in plan)
            {
                if (entry.Network == null)
                {
                    // Spawned item
                    if (entry.Thing == null || entry.Thing.Destroyed) continue;
                    entry.Thing.SplitOff(entry.Count).Destroy();
                }
                else
                {
                    // Network item
                    if (entry.Thing == null || entry.Thing.Destroyed) continue;
                    if (entry.Count >= entry.Thing.stackCount)
                    {
                        if (entry.Network.ActiveController?.innerContainer != null &&
                            entry.Network.ActiveController.innerContainer.Remove(entry.Thing))
                        {
                            entry.Network.storedItems.Remove(entry.Thing);
                            entry.Thing.Destroy();
                        }
                    }
                    else
                    {
                        entry.Thing.SplitOff(entry.Count).Destroy();
                    }
                    dirtyNetworks.Add(entry.Network);
                }
            }

            foreach (DataNetwork network in dirtyNetworks)
                network.MarkBytesDirty();
        }

        private static void AddNetworkEquipment(Map map, List<Thing> result)
        {
            if (map == null || result == null) return;

            HashSet<Thing> existing = new HashSet<Thing>(result);
            bool added = false;

            foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(map))
            {
                if (!IsValidForgeEquipment(thing)) continue;
                if (!existing.Add(thing)) continue;
                result.Add(thing);
                added = true;
            }

            if (added)
            {
                result.Sort((a, b) =>
                {
                    string keyA = GetSortKey(a);
                    string keyB = GetSortKey(b);
                    int cmp = string.Compare(keyA, keyB, StringComparison.Ordinal);
                    if (cmp != 0) return cmp;
                    return string.Compare(a.def.label, b.def.label, StringComparison.OrdinalIgnoreCase);
                });
            }
        }

        private static string GetSortKey(Thing t)
        {
            if (t.ParentHolder is Pawn_EquipmentTracker eq)
                return "0_" + (eq.pawn?.LabelShortCap ?? "");
            if (t.ParentHolder is Pawn_ApparelTracker ap)
                return "0_" + (ap.pawn?.LabelShortCap ?? "");
            return "1_storage";
        }

        // Patch 1: Count network materials in forge material checks

        [HarmonyPatch]
        public static class ForgeUtility_CountOnMap
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (forgeUtilityType == null)
                {
                    Logger.Warning("IsekaiLeveling compat: ForgeUtility type not found; CountOnMap patch skipped.");
                    return false;
                }
                if (TargetMethod() == null)
                {
                    Logger.Warning("IsekaiLeveling compat: ForgeUtility.CountOnMap method not found; patch skipped.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(forgeUtilityType, "CountOnMap", new[] { typeof(Map), typeof(ThingDef) });
            }

            public static void Postfix(Map map, ThingDef def, ref int __result)
            {
                __result += CountNetworkItems(map, def);
            }
        }

        // Patch 2: Consume network materials when forge costs are paid

        [HarmonyPatch]
        public static class ForgeUtility_ConsumeFromMap
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (forgeUtilityType == null)
                {
                    Logger.Warning("IsekaiLeveling compat: ForgeUtility type not found; ConsumeFromMap patch skipped.");
                    return false;
                }
                if (TargetMethod() == null)
                {
                    Logger.Warning("IsekaiLeveling compat: ForgeUtility.ConsumeFromMap method not found; patch skipped.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(forgeUtilityType, "ConsumeFromMap", new[] { typeof(Map), typeof(ThingDef), typeof(int) });
            }

            public static bool Prefix(Map map, ThingDef def, int count)
            {
                if (map == null || def == null || count <= 0) return false;

                List<TakeEntry> plan = new List<TakeEntry>();
                if (!BuildTakePlan(map, def, count, plan))
                {
                    // Cannot satisfy from combined spawned + network; fall back to Isekai's original
                    return true;
                }

                ApplyTakePlan(plan);
                return false;
            }
        }

        // Patch 3: Add network-stored equipment to the forge item list

        [HarmonyPatch]
        public static class WindowForge_GetAllEquipment
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (windowForgeType == null)
                {
                    Logger.Warning("IsekaiLeveling compat: Window_Forge type not found; GetAllEquipment patch skipped.");
                    return false;
                }
                if (windowMapField == null)
                {
                    Logger.Warning("IsekaiLeveling compat: Window_Forge.map field not found; GetAllEquipment patch skipped.");
                    return false;
                }
                if (TargetMethod() == null)
                {
                    Logger.Warning("IsekaiLeveling compat: Window_Forge.GetAllEquipment method not found; patch skipped.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(windowForgeType, "GetAllEquipment");
            }

            public static void Postfix(object __instance, ref List<Thing> __result)
            {
                Map map = windowMapField.GetValue(__instance) as Map;
                AddNetworkEquipment(map, __result);
            }
        }

        // Patch 4: Dirty network after refinement or destruction of a network item

        public class RefinementState
        {
            public DataNetwork Network;
            public Thing Item;
        }

        [HarmonyPatch]
        public static class WindowForge_DoRefinement
        {
            private static readonly MethodInfo doRefinementMethod = windowForgeType != null
                ? AccessTools.Method(windowForgeType, "DoRefinement") : null;

            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!IsIsekaiCompatAvailable()) return false;
                if (doRefinementMethod == null)
                {
                    // Non-fatal: core functionality doesn't depend on this
                    return false;
                }
                if (TargetMethod() == null) return false;
                return true;
            }

            public static MethodBase TargetMethod()
            {
                return doRefinementMethod;
            }

            public static void Prefix(object __instance, out RefinementState __state)
            {
                __state = null;
                Thing selectedItem = selectedItemField.GetValue(__instance) as Thing;
                if (selectedItem == null || selectedItem.Destroyed) return;

                Map map = windowMapField.GetValue(__instance) as Map;
                if (map == null) return;

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp == null) return;

                if (mapComp.TryGetItemNetwork(selectedItem, out DataNetwork network))
                    __state = new RefinementState { Network = network, Item = selectedItem };
            }

            public static void Postfix(RefinementState __state)
            {
                if (__state == null) return;
                if (__state.Item != null && __state.Item.Destroyed)
                    __state.Network.storedItems.Remove(__state.Item);
                __state.Network.MarkBytesDirty();
            }
        }
    }
}
