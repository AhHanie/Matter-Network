using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class OrbitalMarketCompat
    {
        // ─── Reflected types ─────────────────────────────────────────────────
        private static readonly Type dialogType = AccessTools.TypeByName("YourMod.Dialog_CreateListing");
        private static readonly Type gameCompType = AccessTools.TypeByName("YourMod.GameComponent_OrbitalMarket");
        private static readonly Type listingType = AccessTools.TypeByName("YourMod.Listing");
        private static readonly Type autoRuleType = AccessTools.TypeByName("YourMod.AutoListingRule");
        private static readonly Type notifsType = AccessTools.TypeByName("YourMod.OrbitalMarketNotifs");

        // ─── Dialog_CreateListing fields ─────────────────────────────────────
        private static readonly FieldInfo dialogMapField = AccessTools.Field(dialogType, "map");
        private static readonly FieldInfo dialogCountsField = AccessTools.Field(dialogType, "counts");
        private static readonly FieldInfo dialogDefsField = AccessTools.Field(dialogType, "defs");
        private static readonly FieldInfo dialogMinifiedDefsField = AccessTools.Field(dialogType, "minifiedDefs");

        // ─── Listing fields ──────────────────────────────────────────────────
        private static readonly FieldInfo listingMapIdField = AccessTools.Field(listingType, "mapId");
        private static readonly FieldInfo listingDefNameField = AccessTools.Field(listingType, "defName");
        private static readonly FieldInfo listingThingDefField = AccessTools.Field(listingType, "thingDef");
        private static readonly FieldInfo listingCountField = AccessTools.Field(listingType, "count");
        // Stored property getter — handles lazy init of the private backing field
        private static readonly MethodInfo listingStoredGetter =
            AccessTools.PropertyGetter(listingType, "Stored");

        // ─── AutoListingRule fields ───────────────────────────────────────────
        private static readonly FieldInfo ruleMapIdField = AccessTools.Field(autoRuleType, "mapId");
        private static readonly FieldInfo ruleDefNameField = AccessTools.Field(autoRuleType, "defName");
        private static readonly FieldInfo ruleCompField = AccessTools.Field(autoRuleType, "comp");
        private static readonly FieldInfo ruleThresholdField = AccessTools.Field(autoRuleType, "threshold");
        private static readonly FieldInfo rulePostCountField = AccessTools.Field(autoRuleType, "postCount");
        private static readonly FieldInfo ruleCheckIntervalField = AccessTools.Field(autoRuleType, "checkIntervalTicks");
        private static readonly FieldInfo ruleEnabledField = AccessTools.Field(autoRuleType, "enabled");
        private static readonly FieldInfo ruleNextCheckTickField = AccessTools.Field(autoRuleType, "nextCheckTick");

        // ─── GameComponent_OrbitalMarket ─────────────────────────────────────
        private static readonly FieldInfo gameCompAutoRulesField = AccessTools.Field(gameCompType, "autoRules");
        private static readonly MethodInfo tryAddListingMethod = AccessTools.Method(gameCompType, "TryAddListing");
        private static readonly MethodInfo tryAutoCreateListingMethod = AccessTools.Method(gameCompType, "TryAutoCreateListing");

        // ─── OrbitalMarketNotifs ─────────────────────────────────────────────
        private static readonly MethodInfo toastAutoPostedMethod = AccessTools.Method(notifsType, "ToastAutoPosted");

        // AutoRunFail nested enum (for resetting the out param in Patch 7)
        private static readonly Type autoRunFailType = gameCompType?.GetNestedType("AutoRunFail");

        // ─── Availability guards ─────────────────────────────────────────────
        private static bool IsAvailable()
        {
            return dialogType != null
                && gameCompType != null
                && listingType != null
                && dialogMapField != null
                && dialogCountsField != null
                && dialogDefsField != null
                && dialogMinifiedDefsField != null
                && listingMapIdField != null
                && listingThingDefField != null
                && listingCountField != null
                && listingStoredGetter != null
                && tryAddListingMethod != null;
        }

        private static bool IsAutoRuleAvailable()
        {
            return IsAvailable()
                && autoRuleType != null
                && ruleMapIdField != null
                && ruleDefNameField != null
                && ruleCompField != null
                && ruleThresholdField != null
                && rulePostCountField != null
                && ruleCheckIntervalField != null
                && ruleEnabledField != null
                && ruleNextCheckTickField != null
                && gameCompAutoRulesField != null
                && tryAutoCreateListingMethod != null;
        }

        // ─── Shared helpers ──────────────────────────────────────────────────

        private static IEnumerable<Thing> NetworkSellableItems(Map map)
        {
            foreach (Thing t in NetworkItemSearchUtility.AllNetworkItems(map))
            {
                if (t == null || t.Destroyed || t.stackCount <= 0) continue;
                if (t.def.category != ThingCategory.Item) continue;
                if (t is Corpse) continue;
                if (t.def.Minifiable) continue;
                if (t.IsForbidden(Faction.OfPlayer)) continue;
                CompBiocodable bio = t.TryGetComp<CompBiocodable>();
                if (bio != null && bio.Biocoded) continue;
                if (t is Apparel app && app.WornByCorpse) continue;
                yield return t;
            }
        }

        private static ThingDef ListedDefFor(Thing t, out bool isMinified)
        {
            if (t is MinifiedThing mt && mt.InnerThing != null)
            {
                isMinified = true;
                return mt.InnerThing.def;
            }
            isMinified = false;
            return t.def;
        }

        private static int CountSpawnedSellable(Map map, ThingDef def)
        {
            int count = 0;
            foreach (Thing t in map.listerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.MinifiedThing)))
            {
                if (!(t is MinifiedThing mt) || mt.InnerThing?.def != def) continue;
                if (t.IsForbidden(Faction.OfPlayer)) continue;
                CompBiocodable bio = t.TryGetComp<CompBiocodable>();
                if (bio == null || !bio.Biocoded) count += t.stackCount;
            }
            foreach (Thing t in map.listerThings.ThingsOfDef(def))
            {
                if (t is Building || t.IsForbidden(Faction.OfPlayer)) continue;
                CompBiocodable bio = t.TryGetComp<CompBiocodable>();
                if (bio != null && bio.Biocoded) continue;
                if (t is Apparel app && app.WornByCorpse) continue;
                count += t.stackCount;
            }
            return count;
        }

        private static int CountNetworkSellable(Map map, ThingDef def)
        {
            int count = 0;
            foreach (Thing t in NetworkSellableItems(map))
            {
                if (ListedDefFor(t, out _) == def) count += t.stackCount;
            }
            return count;
        }

        private static int CountCombinedSellable(Map map, ThingDef def)
            => CountSpawnedSellable(map, def) + CountNetworkSellable(map, def);

        // Moves up to `need` spawned map items of `def` into `dst`. Returns count moved.
        // Mirrors MoveFromMapToOwner filters so removal is consistent with what OM considers sellable.
        private static int MoveSpawnedToStored(Map map, ThingDef def, int need, ThingOwner<Thing> dst)
        {
            if (need <= 0) return 0;
            List<Thing> list = new List<Thing>();
            foreach (Thing t in map.listerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.MinifiedThing)))
            {
                if (!(t is MinifiedThing mt) || mt.InnerThing?.def != def || t.IsForbidden(Faction.OfPlayer)) continue;
                CompBiocodable bio = t.TryGetComp<CompBiocodable>();
                if (bio != null && bio.Biocoded) continue;
                if (mt.InnerThing is Apparel appInner && appInner.WornByCorpse) continue;
                list.Add(t);
            }
            foreach (Thing t in map.listerThings.ThingsOfDef(def))
            {
                if (t is Building || t.IsForbidden(Faction.OfPlayer)) continue;
                CompBiocodable bio = t.TryGetComp<CompBiocodable>();
                if (bio != null && bio.Biocoded) continue;
                if (t is Apparel app && app.WornByCorpse) continue;
                list.Add(t);
            }
            list = list.OrderByDescending(t => t.stackCount).ToList();

            int moved = 0;
            foreach (Thing t in list)
            {
                if (moved >= need) break;
                if (t.DestroyedOrNull() || !t.Spawned) continue;
                int take = Math.Min(t.stackCount, need - moved);
                Thing taken = t.SplitOff(take);
                dst.TryAdd(taken);
                moved += take;
            }
            return moved;
        }

        // Moves up to `need` network items of `def` into `dst`. Returns count moved.
        private static int MoveNetworkToStored(Map map, ThingDef def, int need, ThingOwner<Thing> dst)
        {
            if (need <= 0) return 0;
            List<Thing> candidates = new List<Thing>();
            foreach (Thing t in NetworkSellableItems(map))
            {
                if (ListedDefFor(t, out _) == def) candidates.Add(t);
            }
            candidates = candidates.OrderByDescending(t => t.stackCount).ToList();

            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            int moved = 0;
            foreach (Thing t in candidates)
            {
                if (moved >= need) break;
                if (t.DestroyedOrNull() || t.holdingOwner == null) continue;

                mapComp.TryGetItemNetwork(t, out DataNetwork network);
                int take = Math.Min(t.stackCount, need - moved);

                Thing taken = t.holdingOwner.Take(t, take);
                if (taken == null) continue;
                dst.TryAdd(taken, canMergeWithExistingStacks: true);
                moved += take;
                network?.MarkBytesDirty();
            }
            return moved;
        }

        // ─── Patch 1: Create-listing dialog includes network stock ────────────

        [HarmonyPatch]
        public static class Patch_Dialog_CreateListing_Ctor
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod()
                => AccessTools.Constructor(dialogType, new[] { gameCompType, typeof(Map) });

            public static void Postfix(object __instance)
            {
                Map map = dialogMapField.GetValue(__instance) as Map;
                if (map == null) return;

                Dictionary<ThingDef, int> counts = dialogCountsField.GetValue(__instance) as Dictionary<ThingDef, int>;
                HashSet<ThingDef> minifiedDefs = dialogMinifiedDefsField.GetValue(__instance) as HashSet<ThingDef>;
                if (counts == null || minifiedDefs == null) return;

                bool modified = false;
                foreach (Thing t in NetworkSellableItems(map))
                {
                    ThingDef listedDef = ListedDefFor(t, out bool isMinified);
                    if (listedDef == null) continue;
                    counts[listedDef] = (counts.TryGetValue(listedDef, out int existing) ? existing : 0) + t.stackCount;
                    if (isMinified) minifiedDefs.Add(listedDef);
                    modified = true;
                }

                if (!modified) return;

                List<ThingDef> defs = counts.Keys
                    .OrderBy(d => d.label ?? d.defName)
                    .ThenBy(d => d.defName)
                    .ToList();
                dialogDefsField.SetValue(__instance, defs);
            }
        }

        // ─── Patch 2: UI stock count includes network ─────────────────────────

        [HarmonyPatch]
        public static class Patch_CountFiltered
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod()
                => AccessTools.Method(dialogType, "CountFiltered");

            public static void Postfix(object __instance, ref int __result, Map m, ThingDef d, ref bool hasMinified)
            {
                if (m == null || d == null) return;
                foreach (Thing t in NetworkSellableItems(m))
                {
                    if (ListedDefFor(t, out bool isMinified) != d) continue;
                    __result += t.stackCount;
                    if (isMinified) hasMinified = true;
                }
            }
        }

        // ─── Patch 3: Price estimate uses network items when no map items exist ─

        [HarmonyPatch]
        public static class Patch_EstimateUnitPriceFromMap
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod()
                => AccessTools.Method(dialogType, "EstimateUnitPriceFromMap");

            public static void Postfix(object __instance, ref int __result, ThingDef def)
            {
                if (def == null) return;
                Map map = dialogMapField.GetValue(__instance) as Map;
                if (map == null) return;

                List<float> combined = new List<float>();
                foreach (Thing t in NetworkSellableItems(map))
                {
                    if (ListedDefFor(t, out _) != def) continue;
                    combined.Add((t is MinifiedThing mt)
                        ? mt.InnerThing.GetStatValue(StatDefOf.MarketValue)
                        : t.GetStatValue(StatDefOf.MarketValue));
                }

                if (combined.Count == 0) return;

                foreach (Thing t in map.listerThings.ThingsOfDef(def))
                {
                    if (!t.Spawned || t is Corpse) continue;
                    if (t.def.category != ThingCategory.Item || t.def.Minifiable) continue;
                    if (t.IsForbidden(Faction.OfPlayer)) continue;
                    CompBiocodable bio = t.TryGetComp<CompBiocodable>();
                    if (bio != null && bio.Biocoded) continue;
                    if (t is Apparel app && app.WornByCorpse) continue;
                    combined.Add(t.GetStatValue(StatDefOf.MarketValue));
                }
                foreach (Thing t in map.listerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.MinifiedThing)))
                {
                    if (!(t is MinifiedThing mt2) || mt2.InnerThing?.def != def) continue;
                    if (!t.Spawned || t.IsForbidden(Faction.OfPlayer)) continue;
                    CompBiocodable bio = mt2.InnerThing.TryGetComp<CompBiocodable>();
                    if (bio != null && bio.Biocoded) continue;
                    if (mt2.InnerThing is Apparel appInner && appInner.WornByCorpse) continue;
                    combined.Add(mt2.InnerThing.GetStatValue(StatDefOf.MarketValue));
                }

                combined.Sort();
                __result = Math.Max(1, (int)Math.Round(combined[combined.Count / 2], MidpointRounding.AwayFromZero));
            }
        }

        // ─── Patch 4: Hard-store network items into L.Stored at listing creation ─
        //
        // When spawned stock alone cannot fill L.count but combined (spawned + network)
        // can, we move the required items directly into L.Stored before TryAddListing
        // runs its MoveFromMapToOwner path. We then set hardStore=false so TryAddListing
        // skips MoveFromMapToOwner (L.Stored is already populated) and reserveStock=false
        // (no separate reservation needed).  Items leave the network immediately when the
        // listing is created, matching vanilla hard-store UX.

        [HarmonyPatch]
        public static class Patch_TryAddListing
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() => tryAddListingMethod;

            public static void Prefix(object L, ref bool reserveStock, ref bool hardStore)
            {
                if (!hardStore || L == null) return;

                ThingDef def = listingThingDefField.GetValue(L) as ThingDef;
                int count = (int)listingCountField.GetValue(L);

                if (def == null)
                {
                    string defName = listingDefNameField.GetValue(L) as string;
                    if (!defName.NullOrEmpty())
                        def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                }

                if (def == null || count <= 0) return;

                int mapId = (int)listingMapIdField.GetValue(L);
                Map map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
                if (map == null) return;

                int spawned = CountSpawnedSellable(map, def);
                // If spawned stock alone covers the order, let MoveFromMapToOwner handle it.
                if (spawned >= count) return;

                int networkCount = CountNetworkSellable(map, def);
                // Not enough combined stock — let TryAddListing fail naturally.
                if (spawned + networkCount < count) return;

                ThingOwner<Thing> stored = listingStoredGetter.Invoke(L, null) as ThingOwner<Thing>;
                if (stored == null) return;

                // Move spawned items first, then fill the deficit from the network.
                int moved = MoveSpawnedToStored(map, def, count, stored);
                if (moved < count)
                    moved += MoveNetworkToStored(map, def, count - moved, stored);

                if (moved <= 0) return; // nothing moved — fall through to original behaviour

                // Clamp the listing count to what was actually stored.
                if (moved < count)
                    listingCountField.SetValue(L, moved);

                // L.Stored is populated; skip MoveFromMapToOwner and stock reservation.
                hardStore = false;
                reserveStock = false;
            }
        }

        // ─── Patch 5: RemoveFromMap covers network deficit ────────────────────
        //
        // TryInstantSell calls RemoveFromMap directly and returns false if removed<=0.
        // This postfix fills any deficit from network items so instant sell and
        // non-hard-stored listing fulfillment both work.

        [HarmonyPatch]
        public static class Patch_RemoveFromMap
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod()
                => AccessTools.Method(gameCompType, "RemoveFromMap",
                    new[] { typeof(Map), typeof(ThingDef), typeof(int), typeof(double).MakeByRefType() });

            public static void Postfix(Map map, ThingDef def, int need, ref double totalBaseValue, ref int __result)
            {
                if (__result >= need || map == null || def == null) return;
                int remaining = need - __result;

                List<Thing> candidates = new List<Thing>();
                foreach (Thing t in NetworkSellableItems(map))
                {
                    if (ListedDefFor(t, out _) == def) candidates.Add(t);
                }
                candidates = candidates.OrderByDescending(t => t.stackCount).ToList();

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                foreach (Thing t in candidates)
                {
                    if (remaining <= 0) break;
                    if (t.DestroyedOrNull() || t.holdingOwner == null) continue;

                    mapComp.TryGetItemNetwork(t, out DataNetwork network);
                    int take = Math.Min(t.stackCount, remaining);

                    double marketValue = (t is MinifiedThing mt)
                        ? mt.InnerThing.GetStatValue(StatDefOf.MarketValue)
                        : t.GetStatValue(StatDefOf.MarketValue);
                    totalBaseValue += marketValue * take;

                    Thing taken = t.holdingOwner.Take(t, take);
                    if (taken != null) taken.Destroy();

                    __result += take;
                    remaining -= take;
                    network?.MarkBytesDirty();
                }
            }
        }

        // ─── Patch 6: GameComponentTick auto-rules fire when network meets threshold ──

        public struct RuleSnapshot
        {
            public object Rule;
            public Map Map;
            public ThingDef Def;
            public int MapOnlyCount;
            public int CombinedCount;
            public int CompValue;   // 0 = AtLeast, 1 = AtMost
            public int Threshold;
            public int PostCount;
        }

        [HarmonyPatch]
        public static class Patch_GameComponentTick
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAutoRuleAvailable();

            public static MethodBase TargetMethod()
                => AccessTools.Method(gameCompType, "GameComponentTick");

            public static void Prefix(object __instance, ref List<RuleSnapshot> __state)
            {
                __state = new List<RuleSnapshot>();
                if (Find.TickManager.TicksGame % 300 != 0) return;
                if (gameCompAutoRulesField == null) return;

                IList rules = gameCompAutoRulesField.GetValue(__instance) as IList;
                if (rules == null || rules.Count == 0) return;

                int ticksGame = Find.TickManager.TicksGame;
                foreach (object rule in rules)
                {
                    if (rule == null || !(bool)ruleEnabledField.GetValue(rule)) continue;
                    if (ticksGame < (int)ruleNextCheckTickField.GetValue(rule)) continue;

                    int mapId = (int)ruleMapIdField.GetValue(rule);
                    Map map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
                    if (map == null) continue;

                    string defName = ruleDefNameField.GetValue(rule) as string;
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def == null) continue;

                    int mapOnlyCount = 0;
                    foreach (Thing t in map.listerThings.ThingsOfDef(def))
                        if (!t.IsForbidden(Faction.OfPlayer)) mapOnlyCount += t.stackCount;

                    int networkCount = CountNetworkSellable(map, def);
                    __state.Add(new RuleSnapshot
                    {
                        Rule = rule,
                        Map = map,
                        Def = def,
                        MapOnlyCount = mapOnlyCount,
                        CombinedCount = mapOnlyCount + networkCount,
                        CompValue = Convert.ToInt32(ruleCompField.GetValue(rule)),
                        Threshold = (int)ruleThresholdField.GetValue(rule),
                        PostCount = (int)rulePostCountField.GetValue(rule),
                    });
                }
            }

            public static void Postfix(object __instance, List<RuleSnapshot> __state)
            {
                if (__state == null || __state.Count == 0) return;
                int ticksGame = Find.TickManager.TicksGame;
                foreach (RuleSnapshot s in __state)
                {
                    bool mapPasses = s.CompValue == 0
                        ? s.MapOnlyCount >= s.Threshold
                        : s.MapOnlyCount <= s.Threshold;
                    if (mapPasses) continue;

                    bool combinedPasses = s.CompValue == 0
                        ? s.CombinedCount >= s.Threshold
                        : s.CombinedCount <= s.Threshold;
                    if (!combinedPasses || s.PostCount <= 0) continue;

                    // Confirm the original evaluated this rule (nextCheckTick was bumped forward).
                    if ((int)ruleNextCheckTickField.GetValue(s.Rule) <= ticksGame) continue;

                    bool created = (bool)tryAutoCreateListingMethod.Invoke(
                        __instance, new object[] { s.Map, s.Def, s.PostCount });
                    if (created)
                        toastAutoPostedMethod?.Invoke(null, new object[] { s.Def, s.PostCount });
                }
            }
        }

        // ─── Patch 7: EvaluateAndMaybePost (Run Now) uses combined stock ──────

        [HarmonyPatch]
        public static class Patch_EvaluateAndMaybePost
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAutoRuleAvailable();

            public static MethodBase TargetMethod()
                => AccessTools.Method(gameCompType, "EvaluateAndMaybePost");

            // __args[2] is the boxed out AutoRunFail fail parameter; we set it to None (0).
            public static void Postfix(ref bool __result, object __instance, object r, bool ignoreThreshold, ref int have, object[] __args)
            {
                if (__result || r == null) return;
                if (!(bool)ruleEnabledField.GetValue(r)) return;

                int mapId = (int)ruleMapIdField.GetValue(r);
                Map map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
                if (map == null) return;

                string defName = ruleDefNameField.GetValue(r) as string;
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null) return;

                int combined = CountCombinedSellable(map, def);
                have = combined;

                int threshold = (int)ruleThresholdField.GetValue(r);
                int compValue = Convert.ToInt32(ruleCompField.GetValue(r));
                bool combinedPasses = compValue == 0 ? combined >= threshold : combined <= threshold;
                if (!ignoreThreshold && !combinedPasses) return;

                int postCount = (int)rulePostCountField.GetValue(r);
                if (postCount <= 0) return;

                bool created = (bool)tryAutoCreateListingMethod.Invoke(
                    __instance, new object[] { map, def, postCount });
                if (!created) return;

                toastAutoPostedMethod?.Invoke(null, new object[] { def, postCount });
                int checkInterval = (int)ruleCheckIntervalField.GetValue(r);
                ruleNextCheckTickField.SetValue(r, Find.TickManager.TicksGame + Math.Max(250, checkInterval));

                if (autoRunFailType != null && __args != null && __args.Length > 2)
                    __args[2] = Enum.ToObject(autoRunFailType, 0);

                __result = true;
            }
        }
    }
}
