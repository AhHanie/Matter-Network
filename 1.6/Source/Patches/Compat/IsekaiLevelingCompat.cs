using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class IsekaiLevelingCompat
    {
        private const string PackageId = "JellyCreative.IsekaiLeveling";
        private const string ForgeUtilityTypeName = "IsekaiLeveling.Forge.ForgeUtility";
        private const string WindowForgeTypeName = "IsekaiLeveling.Forge.Window_Forge";
        private const string ForgeEnhancementCompTypeName = "IsekaiLeveling.Forge.CompForgeEnhancement";
        private const string WindowRunicStationTypeName = "IsekaiLeveling.Forge.Window_RunicStation";
        private const string RuneDefTypeName = "IsekaiLeveling.Forge.RuneDef";

        private static readonly Type forgeUtilityType = AccessTools.TypeByName(ForgeUtilityTypeName);
        private static readonly Type windowForgeType = AccessTools.TypeByName(WindowForgeTypeName);
        private static readonly Type forgeEnhancementCompType = AccessTools.TypeByName(ForgeEnhancementCompTypeName);
        private static readonly Type windowRunicStationType = AccessTools.TypeByName(WindowRunicStationTypeName);
        private static readonly Type runeDefType = AccessTools.TypeByName(RuneDefTypeName);

        // Window_Forge reflected members
        private static readonly FieldInfo windowMapField = windowForgeType != null
            ? AccessTools.Field(windowForgeType, "map") : null;
        private static readonly FieldInfo selectedItemField = windowForgeType != null
            ? AccessTools.Field(windowForgeType, "selectedItem") : null;

        // Window_RunicStation reflected members
        private static readonly FieldInfo runicMapField = windowRunicStationType != null
            ? AccessTools.Field(windowRunicStationType, "map") : null;
        private static readonly FieldInfo runicSelectedItemField = windowRunicStationType != null
            ? AccessTools.Field(windowRunicStationType, "selectedItem") : null;
        private static readonly MethodInfo findRuneDefAndRankForItemMethod = windowRunicStationType != null
            ? AccessTools.Method(windowRunicStationType, "FindRuneDefAndRankForItem") : null;

        // RuneDef reflected members
        private static readonly FieldInfo runeDefCategoryField = runeDefType != null
            ? AccessTools.Field(runeDefType, "category") : null;

        // CompForgeEnhancement reflected members for rune signature
        private static readonly FieldInfo appliedRuneDefNamesField = forgeEnhancementCompType != null
            ? AccessTools.Field(forgeEnhancementCompType, "appliedRuneDefNames") : null;
        private static readonly FieldInfo appliedRuneRanksField = forgeEnhancementCompType != null
            ? AccessTools.Field(forgeEnhancementCompType, "appliedRuneRanks") : null;

        private static bool IsIsekaiCompatAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && forgeUtilityType != null
                && windowForgeType != null
                && forgeEnhancementCompType != null
                && windowMapField != null
                && selectedItemField != null;
        }

        private static bool IsRunicCompatAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && windowRunicStationType != null
                && forgeEnhancementCompType != null
                && runicMapField != null
                && runicSelectedItemField != null;
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

        private static ThingComp GetForgeEnhancementComp(Thing thing)
        {
            if (forgeEnhancementCompType == null) return null;
            if (!(thing is ThingWithComps twc)) return null;
            foreach (ThingComp comp in twc.AllComps)
            {
                if (forgeEnhancementCompType.IsInstanceOfType(comp))
                    return comp;
            }
            return null;
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

        private static bool IsValidRuneItem(Thing thing)
        {
            return thing != null
                && !thing.Destroyed
                && thing.stackCount > 0
                && thing.def != null
                && thing.def.category == ThingCategory.Item
                && thing.def.defName != null
                && thing.def.defName.StartsWith("Isekai_Rune_", StringComparison.Ordinal)
                && !thing.IsForbidden(Faction.OfPlayer);
        }

        private static string GetRuneSignature(Thing item)
        {
            if (item == null || item.Destroyed) return null;
            ThingComp comp = GetForgeEnhancementComp(item);
            if (comp == null) return null;
            if (appliedRuneDefNamesField == null || appliedRuneRanksField == null) return null;
            IList names = appliedRuneDefNamesField.GetValue(comp) as IList;
            IList ranks = appliedRuneRanksField.GetValue(comp) as IList;
            if (names == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                sb.Append(names[i]);
                sb.Append(':');
                sb.Append(i < (ranks?.Count ?? 0) ? ranks[i].ToString() : "1");
                sb.Append(',');
            }
            return sb.ToString();
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

        private static void AddNetworkRuneItems(Map map, Thing selectedEquipment, List<Thing> result)
        {
            if (map == null || selectedEquipment == null || result == null) return;
            if (findRuneDefAndRankForItemMethod == null || runeDefCategoryField == null) return;

            // RuneCategory.Weapon = 0, RuneCategory.Armor = 1
            int targetCategory = (selectedEquipment.def?.IsWeapon ?? false) ? 0 : 1;

            HashSet<Thing> existing = new HashSet<Thing>(result);

            foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(map))
            {
                if (!IsValidRuneItem(thing)) continue;
                if (!existing.Add(thing)) continue;

                try
                {
                    object tuple = findRuneDefAndRankForItemMethod.Invoke(null, new object[] { thing.def.defName });
                    if (tuple == null) continue;
                    object runeDef = tuple.GetType().GetField("Item1")?.GetValue(tuple);
                    if (runeDef == null) continue;
                    object categoryValue = runeDefCategoryField.GetValue(runeDef);
                    if (categoryValue == null) continue;
                    if (Convert.ToInt32(categoryValue) == targetCategory)
                        result.Add(thing);
                }
                catch
                {
                    // Skip silently; runeDefCategoryField reflection already guarded above
                }
            }
        }

        private static void CleanupNetworkItemAfterExternalMutation(DataNetwork network, Thing thing)
        {
            if (network == null || thing == null) return;
            if (thing.Destroyed || thing.stackCount <= 0)
            {
                if (network.ActiveController?.innerContainer != null)
                    network.ActiveController.innerContainer.Remove(thing);
                network.storedItems?.Remove(thing);
            }
            network.MarkBytesDirty();
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

        // Patch 5: Add network-stored equipment to the runic station item list

        [HarmonyPatch]
        public static class WindowRunicStation_GetAllEquipment
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!ModsConfig.IsActive(PackageId)) return false;
                if (windowRunicStationType == null)
                {
                    Logger.Warning("IsekaiLeveling compat: Window_RunicStation type not found; GetAllEquipment (runic) patch skipped.");
                    return false;
                }
                if (runicMapField == null)
                {
                    Logger.Warning("IsekaiLeveling compat: Window_RunicStation.map field not found; GetAllEquipment (runic) patch skipped.");
                    return false;
                }
                if (TargetMethod() == null)
                {
                    Logger.Warning("IsekaiLeveling compat: Window_RunicStation.GetAllEquipment method not found; patch skipped.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(windowRunicStationType, "GetAllEquipment");
            }

            public static void Postfix(object __instance, ref List<Thing> __result)
            {
                Map map = runicMapField.GetValue(__instance) as Map;
                AddNetworkEquipment(map, __result);
            }
        }

        // Patch 6: Add network-stored compatible rune items to the available rune list

        [HarmonyPatch]
        public static class WindowRunicStation_GetAvailableRuneItems
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!IsRunicCompatAvailable()) return false;
                if (findRuneDefAndRankForItemMethod == null)
                {
                    Logger.Warning("IsekaiLeveling compat: Window_RunicStation.FindRuneDefAndRankForItem not found; GetAvailableRuneItems patch skipped.");
                    return false;
                }
                if (runeDefCategoryField == null)
                {
                    Logger.Warning("IsekaiLeveling compat: RuneDef.category field not found; GetAvailableRuneItems patch skipped.");
                    return false;
                }
                if (TargetMethod() == null)
                {
                    Logger.Warning("IsekaiLeveling compat: Window_RunicStation.GetAvailableRuneItems method not found; patch skipped.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(windowRunicStationType, "GetAvailableRuneItems");
            }

            public static void Postfix(object __instance, object comp, ref List<Thing> __result)
            {
                ThingComp thingComp = comp as ThingComp;
                Thing parent = thingComp?.parent;
                if (parent == null) return;

                Map map = runicMapField.GetValue(__instance) as Map;
                AddNetworkRuneItems(map, parent, __result);
            }
        }

        // Patch 7: Dirty network and clean up rune item stacks after rune application

        public class RunicApplyState
        {
            public Thing SelectedItem;
            public DataNetwork EquipmentNetwork;
            public string RuneSignatureBefore;

            public struct RuneItemSnapshot
            {
                public Thing Thing;
                public int StackCount;
                public DataNetwork Network;
            }

            public List<RuneItemSnapshot> RuneItemSnapshots;
        }

        [HarmonyPatch]
        public static class WindowRunicStation_DrawAvailableRunes
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!IsRunicCompatAvailable()) return false;
                if (TargetMethod() == null)
                {
                    Logger.Warning("IsekaiLeveling compat: Window_RunicStation.DrawAvailableRunes method not found; patch skipped.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(windowRunicStationType, "DrawAvailableRunes");
            }

            public static void Prefix(object __instance, out RunicApplyState __state)
            {
                __state = null;
                Thing selectedItem = runicSelectedItemField.GetValue(__instance) as Thing;
                if (selectedItem == null || selectedItem.Destroyed) return;

                Map map = runicMapField.GetValue(__instance) as Map;
                if (map == null) return;

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp == null) return;

                mapComp.TryGetItemNetwork(selectedItem, out DataNetwork equipNetwork);

                var state = new RunicApplyState
                {
                    SelectedItem = selectedItem,
                    EquipmentNetwork = equipNetwork,
                    RuneSignatureBefore = GetRuneSignature(selectedItem),
                    RuneItemSnapshots = new List<RunicApplyState.RuneItemSnapshot>()
                };

                foreach (DataNetwork network in NetworkItemSearchUtility.Networks(map))
                {
                    if (network.ActiveController?.innerContainer == null) continue;
                    foreach (Thing thing in network.ActiveController.innerContainer.InnerListForReading)
                    {
                        if (!IsValidRuneItem(thing)) continue;
                        state.RuneItemSnapshots.Add(new RunicApplyState.RuneItemSnapshot
                        {
                            Thing = thing,
                            StackCount = thing.stackCount,
                            Network = network
                        });
                    }
                }

                __state = state;
            }

            public static void Postfix(RunicApplyState __state)
            {
                if (__state == null) return;

                if (__state.EquipmentNetwork != null
                    && __state.SelectedItem != null
                    && !__state.SelectedItem.Destroyed)
                {
                    string signatureAfter = GetRuneSignature(__state.SelectedItem);
                    if (signatureAfter != __state.RuneSignatureBefore)
                        __state.EquipmentNetwork.MarkBytesDirty();
                }

                foreach (RunicApplyState.RuneItemSnapshot snapshot in __state.RuneItemSnapshots)
                {
                    if (snapshot.Thing == null) continue;
                    if (snapshot.Thing.Destroyed || snapshot.Thing.stackCount < snapshot.StackCount)
                        CleanupNetworkItemAfterExternalMutation(snapshot.Network, snapshot.Thing);
                }
            }
        }

        // Patch 8: Dirty network when a rune is removed from a network-stored equipment item

        [HarmonyPatch]
        public static class ForgeEnhancement_RemoveRuneAt
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!IsRunicCompatAvailable()) return false;
                if (TargetMethod() == null)
                {
                    Logger.Warning("IsekaiLeveling compat: CompForgeEnhancement.RemoveRuneAt method not found; patch skipped.");
                    return false;
                }
                return true;
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(forgeEnhancementCompType, "RemoveRuneAt");
            }

            public static void Postfix(object __instance, bool __result)
            {
                if (!__result) return;
                ThingComp comp = __instance as ThingComp;
                Thing parent = comp?.parent;
                if (parent == null || parent.Destroyed) return;

                Map map = parent.MapHeld;
                if (map == null) return;

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp == null) return;

                if (mapComp.TryGetItemNetwork(parent, out DataNetwork network))
                    network.MarkBytesDirty();
            }
        }
    }
}
